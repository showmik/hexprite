using System;
using System.Buffers;
using System.Collections.Generic;
using Hexprite.Core;

namespace Hexprite.Services
{
    /// <summary>
    /// Manages selection state, floating layers, and transformation logic (move, resize, rotate, flip).
    /// </summary>
    public class SelectionService : ISelectionService
    {
        // ── Status flags ──────────────────────────────────────────────────
        public bool HasActiveSelection { get; private set; }
        public bool IsSelecting { get; private set; }
        public bool IsFloating { get; private set; }
        public bool IsDragging { get; private set; }
        public bool IsTransforming { get; private set; }
        public TransformHandle ActiveTransformHandle { get; private set; }

        // ── Transform snapshot (nearest-neighbor resize source) ───────────
        private bool[,]? _originalFloatingPixels;
        private int _originalFloatingX;
        private int _originalFloatingY;
        private int _originalFloatingW;
        private int _originalFloatingH;

        // ── Pristine pixels: the original lifted/pasted data, never resampled ──
        // Persists across multiple transform sessions so every resize always
        // resamples from the original pixel art, avoiding cascading quality loss.
        private bool[,]? _pristineFloatingPixels;
        private int _pristineFloatingW;
        private int _pristineFloatingH;
        private bool[,]? _originalMask;
        private bool[,]? _pristineMask;
        private int _pristineMaskW;
        private int _pristineMaskH;
        private bool _activeTransformFlipX;
        private bool _activeTransformFlipY;

        // ── Reselect state ───────────────────────────────────────────────
        private bool[,]? _lastMask;
        private int _lastMinX = -1;
        private int _lastMinY = -1;
        private int _lastMaxX = -1;
        private int _lastMaxY = -1;
        private bool _lastIsEllipse;
        private bool _hasLastSelection;

        public bool CanReselect => _hasLastSelection && !HasActiveSelection && !IsSelecting;

        // ── Rotation state ────────────────────────────────────────────────
        public double RotationAngle { get; private set; }
        private double _preTransformRotationAngle;
        /// <summary>Pre-rotation floating X (for overlay rendering during rotation).</summary>
        public int OriginalFloatingX => _originalFloatingX;
        /// <summary>Pre-rotation floating Y (for overlay rendering during rotation).</summary>
        public int OriginalFloatingY => _originalFloatingY;
        /// <summary>Pre-rotation floating width (for overlay rendering during rotation).</summary>
        public int OriginalFloatingW => _originalFloatingW;
        /// <summary>Pre-rotation floating height (for overlay rendering during rotation).</summary>
        public int OriginalFloatingH => _originalFloatingH;

        // ── Selection bounds ──────────────────────────────────────────────
        public int MinX { get; private set; } = -1;
        public int MaxX { get; private set; } = -1;
        public int MinY { get; private set; } = -1;
        public int MaxY { get; private set; } = -1;

        public int DragMinX => _dragMinX;
        public int DragMaxX => _dragMaxX;
        public int DragMinY => _dragMinY;
        public int DragMaxY => _dragMaxY;
        public bool[,]? Mask { get; private set; }

        // ── Floating layer ────────────────────────────────────────────────
        public bool[,]? FloatingPixels { get; set; }
        public int FloatingX { get; private set; }
        public int FloatingY { get; private set; }
        public int FloatingWidth { get; private set; }
        public int FloatingHeight { get; private set; }

        // ── Lasso path ────────────────────────────────────────────────────
        private readonly List<PixelPoint> _lassoPoints = [];
        public IReadOnlyList<PixelPoint> LassoPoints => _lassoPoints;

        // ── Rectangle anchor (set on BeginRectangleSelection) ────────────
        private int _anchorX;
        private int _anchorY;

        // ── Event ─────────────────────────────────────────────────────────
        public event EventHandler? SelectionChanged;
        private void Notify()
        {
            ValidateState();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void ValidateState()
        {
            // Floating requires an active selection
            if (IsFloating && !HasActiveSelection)
                throw new InvalidOperationException("Invalid state: IsFloating=true but HasActiveSelection=false");

            // Transforming requires floating state
            if (IsTransforming && !IsFloating)
                throw new InvalidOperationException("Invalid state: IsTransforming=true but IsFloating=false");

            // Cannot be selecting and dragging simultaneously
            if (IsSelecting && IsDragging)
                throw new InvalidOperationException("Invalid state: IsSelecting=true and IsDragging=true");

            // Cannot be selecting and transforming simultaneously
            if (IsSelecting && IsTransforming)
                throw new InvalidOperationException("Invalid state: IsSelecting=true and IsTransforming=true");
        }

        // ── State for Boolean Operations ──────────────────────────────────
        private SelectionMode _currentMode = SelectionMode.Replace;
        private bool _isEllipseSelection;
        private bool[,]? _baseMask;
        private int _baseMinX = -1, _baseMaxX = -1, _baseMinY = -1, _baseMaxY = -1;
        private int _dragMinX = -1, _dragMaxX = -1, _dragMinY = -1, _dragMaxY = -1;

        public bool[,]? BaseMask => _baseMask;
        public int BaseMinX => _baseMinX;
        public int BaseMinY => _baseMinY;
        public int BaseMaxX => _baseMaxX;
        public int BaseMaxY => _baseMaxY;

        // ── Building a selection ──────────────────────────────────────────

        private void SnapshotBaseSelection()
        {
            if (!HasActiveSelection)
            {
                _baseMask = null;
                _baseMinX = _baseMaxX = _baseMinY = _baseMaxY = -1;
                return;
            }

            _baseMinX = MinX;
            _baseMaxX = MaxX;
            _baseMinY = MinY;
            _baseMaxY = MaxY;

            if (Mask == null)
            {
                int w = MaxX - MinX + 1;
                int h = MaxY - MinY + 1;
                _baseMask = new bool[w, h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        _baseMask[x, y] = true;
            }
            else
            {
                int w = Mask.GetLength(0);
                int h = Mask.GetLength(1);
                _baseMask = new bool[w, h];
                Array.Copy(Mask, _baseMask, Mask.Length);
            }
        }

        public void BeginRectangleSelection(int x, int y, SelectionMode mode = SelectionMode.Replace)
        {
            _anchorX = x;
            _anchorY = y;
            _currentMode = mode;
            _isEllipseSelection = false;
            SnapshotBaseSelection();
            
            _dragMinX = _dragMaxX = x;
            _dragMinY = _dragMaxY = y;
            _lassoPoints.Clear();
            
            IsSelecting = true;
            HasActiveSelection = mode != SelectionMode.Replace && _baseMask != null;
            
            RecomputeCombinedSelection();
            Notify();
        }

        public void UpdateRectangleSelection(int currentX, int currentY, bool isAltDown = false)
        {
            if (isAltDown)
            {
                int dx = Math.Abs(currentX - _anchorX);
                int dy = Math.Abs(currentY - _anchorY);
                _dragMinX = _anchorX - dx;
                _dragMaxX = _anchorX + dx;
                _dragMinY = _anchorY - dy;
                _dragMaxY = _anchorY + dy;
            }
            else
            {
                _dragMinX = Math.Min(_anchorX, currentX);
                _dragMaxX = Math.Max(_anchorX, currentX);
                _dragMinY = Math.Min(_anchorY, currentY);
                _dragMaxY = Math.Max(_anchorY, currentY);
            }
            
            RecomputeCombinedSelection();
            Notify();
        }

        public void BeginEllipseSelection(int x, int y, SelectionMode mode = SelectionMode.Replace)
        {
            _anchorX = x;
            _anchorY = y;
            _currentMode = mode;
            _isEllipseSelection = true;
            SnapshotBaseSelection();

            _dragMinX = _dragMaxX = x;
            _dragMinY = _dragMaxY = y;
            _lassoPoints.Clear();

            IsSelecting = true;
            HasActiveSelection = mode != SelectionMode.Replace && _baseMask != null;

            RecomputeCombinedSelection();
            Notify();
        }

        public void UpdateEllipseSelection(int currentX, int currentY, bool isAltDown = false)
        {
            if (isAltDown)
            {
                int dx = Math.Abs(currentX - _anchorX);
                int dy = Math.Abs(currentY - _anchorY);
                _dragMinX = _anchorX - dx;
                _dragMaxX = _anchorX + dx;
                _dragMinY = _anchorY - dy;
                _dragMaxY = _anchorY + dy;
            }
            else
            {
                _dragMinX = Math.Min(_anchorX, currentX);
                _dragMaxX = Math.Max(_anchorX, currentX);
                _dragMinY = Math.Min(_anchorY, currentY);
                _dragMaxY = Math.Max(_anchorY, currentY);
            }

            RecomputeCombinedSelection();
            Notify();
        }

        public void BeginLassoSelection(int x, int y, SelectionMode mode = SelectionMode.Replace)
        {
            _currentMode = mode;
            _isEllipseSelection = false;
            SnapshotBaseSelection();

            _lassoPoints.Clear();
            _lassoPoints.Add(new PixelPoint(x, y));
            
            _dragMinX = _dragMaxX = x;
            _dragMinY = _dragMaxY = y;
            
            IsSelecting = true;
            HasActiveSelection = mode != SelectionMode.Replace && _baseMask != null;
            
            RecomputeCombinedSelection();
            Notify();
        }

        public void AddLassoPoint(int x, int y)
        {
            if (_lassoPoints.Count > 0)
            {
                var last = _lassoPoints[^1];
                if (last.X == x && last.Y == y) return; // skip duplicates

                // OPTIMIZATION: Drop collinear points to vastly reduce polygon vertices.
                // This prevents O(Pixels * Vertices) freezing in IsPointInPolygon.
                if (_lassoPoints.Count >= 2)
                {
                    var prev = _lassoPoints[^2];
                    
                    // Cross product to check if the 3 points form a straight line
                    int crossProduct = (last.X - prev.X) * (y - prev.Y) - (last.Y - prev.Y) * (x - prev.X);
                    
                    if (crossProduct == 0)
                    {
                        // Ensure it's continuing in the same direction (not a U-turn)
                        int dotProduct = (last.X - prev.X) * (x - last.X) + (last.Y - prev.Y) * (y - last.Y);
                        if (dotProduct > 0)
                        {
                            // Replace the last point with the current one to simply extend the existing line segment
                            _lassoPoints[^1] = new PixelPoint(x, y);
                            
                            _dragMinX = Math.Min(_dragMinX, x);
                            _dragMaxX = Math.Max(_dragMaxX, x);
                            _dragMinY = Math.Min(_dragMinY, y);
                            _dragMaxY = Math.Max(_dragMaxY, y);
                            
                            Notify();
                            return;
                        }
                    }
                }
            }

            _lassoPoints.Add(new PixelPoint(x, y));
            _dragMinX = Math.Min(_dragMinX, x);
            _dragMaxX = Math.Max(_dragMaxX, x);
            _dragMinY = Math.Min(_dragMinY, y);
            _dragMaxY = Math.Max(_dragMaxY, y);
            
            Notify();
        }

        private void RecomputeCombinedSelection()
        {
            // FAST PATH: Pure Marquee Replace requires ZERO memory allocation.
            // Avoids severe GC thrashing on every mouse move.
            if (_lassoPoints.Count == 0 && !_isEllipseSelection && _currentMode == SelectionMode.Replace)
            {
                MinX = _dragMinX; MaxX = _dragMaxX;
                MinY = _dragMinY; MaxY = _dragMaxY;
                Mask = null;
                return;
            }

            bool[,]? dragMask;
            if (_lassoPoints.Count > 0)
            {
                if (_lassoPoints.Count >= 3)
                {
                    int w = _dragMaxX - _dragMinX + 1;
                    int h = _dragMaxY - _dragMinY + 1;
                    dragMask = new bool[w, h];
                    for (int y = _dragMinY; y <= _dragMaxY; y++)
                        for (int x = _dragMinX; x <= _dragMaxX; x++)
                            dragMask[x - _dragMinX, y - _dragMinY] = IsPointInPolygon(x, y);
                }
                else
                {
                    dragMask = new bool[_dragMaxX - _dragMinX + 1, _dragMaxY - _dragMinY + 1];
                }
            }
            else if (_isEllipseSelection)
            {
                int w = _dragMaxX - _dragMinX + 1;
                int h = _dragMaxY - _dragMinY + 1;
                dragMask = new bool[w, h];
                // Apply a sub-pixel offset of -0.1 to avoid pixel-art blockiness (making 3x3 look like a square, etc.)
                // while ensuring we never divide by zero or negative values.
                double rx = Math.Max(0.1, (w / 2.0) - 0.1);
                double ry = Math.Max(0.1, (h / 2.0) - 0.1);
                // Sub-pixel center coordinates relative to _dragMinX / _dragMinY
                double cx = (w / 2.0) - 0.5;
                double cy = (h / 2.0) - 0.5;

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        if (rx <= 0.0 || ry <= 0.0)
                        {
                            dragMask[x, y] = true;
                        }
                        else
                        {
                            double dx = x - cx;
                            double dy = y - cy;
                            double termX = dx / rx;
                            double termY = dy / ry;
                            if ((termX * termX) + (termY * termY) <= 1.0)
                            {
                                dragMask[x, y] = true;
                            }
                        }
                    }
                }
            }
            else
            {
                int w = _dragMaxX - _dragMinX + 1;
                int h = _dragMaxY - _dragMinY + 1;
                dragMask = new bool[w, h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        dragMask[x, y] = true;
            }

            if (_currentMode == SelectionMode.Replace)
            {
                MinX = _dragMinX; MaxX = _dragMaxX;
                MinY = _dragMinY; MaxY = _dragMaxY;
                if (_lassoPoints.Count == 0 && !_isEllipseSelection)
                    Mask = null;
                else
                    Mask = dragMask;
                return;
            }

            CombineWithBase(dragMask, _dragMinX, _dragMinY, _dragMaxX, _dragMaxY, _currentMode);
        }

        private void CombineWithBase(bool[,] newMask, int nMinX, int nMinY, int nMaxX, int nMaxY, SelectionMode mode)
        {
            if (_baseMinX == -1)
            {
                if (mode == SelectionMode.Add)
                {
                    MinX = nMinX; MaxX = nMaxX;
                    MinY = nMinY; MaxY = nMaxY;
                    Mask = newMask;
                }
                else
                {
                    MinX = MaxX = MinY = MaxY = -1;
                    Mask = null;
                    HasActiveSelection = false;
                }
                return;
            }

            int minX = _baseMinX;
            int maxX = _baseMaxX;
            int minY = _baseMinY;
            int maxY = _baseMaxY;

            if (mode == SelectionMode.Add)
            {
                minX = Math.Min(_baseMinX, nMinX);
                maxX = Math.Max(_baseMaxX, nMaxX);
                minY = Math.Min(_baseMinY, nMinY);
                maxY = Math.Max(_baseMaxY, nMaxY);
            }
            else if (mode == SelectionMode.Intersect)
            {
                minX = Math.Max(_baseMinX, nMinX);
                maxX = Math.Min(_baseMaxX, nMaxX);
                minY = Math.Max(_baseMinY, nMinY);
                maxY = Math.Min(_baseMaxY, nMaxY);
            }

            if (minX > maxX || minY > maxY)
            {
                MinX = MaxX = MinY = MaxY = -1;
                Mask = null;
                HasActiveSelection = false;
                return;
            }

            int w = maxX - minX + 1;
            int h = maxY - minY + 1;
            int total = w * h;
            bool[] combined = ArrayPool<bool>.Shared.Rent(total);
            Array.Clear(combined, 0, total);
            bool anyTrue = false;
            int trueMinX = int.MaxValue, trueMinY = int.MaxValue, trueMaxX = int.MinValue, trueMaxY = int.MinValue;

            try
            {
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        bool inBase = false;
                        if (x >= _baseMinX && x <= _baseMaxX && y >= _baseMinY && y <= _baseMaxY && _baseMask != null)
                        {
                            int bx = x - _baseMinX;
                            int by = y - _baseMinY;
                            if (bx >= 0 && bx < _baseMask.GetLength(0) && by >= 0 && by < _baseMask.GetLength(1))
                                inBase = _baseMask[bx, by];
                        }

                        bool inNew = false;
                        if (x >= nMinX && x <= nMaxX && y >= nMinY && y <= nMaxY)
                        {
                            int nx = x - nMinX;
                            int ny = y - nMinY;
                            if (nx >= 0 && nx < newMask.GetLength(0) && ny >= 0 && ny < newMask.GetLength(1))
                                inNew = newMask[nx, ny];
                        }

                        bool result = false;
                        switch (mode)
                        {
                            case SelectionMode.Add: result = inBase || inNew; break;
                            case SelectionMode.Subtract: result = inBase && !inNew; break;
                            case SelectionMode.Intersect: result = inBase && inNew; break;
                        }

                        int localX = x - minX;
                        int localY = y - minY;
                        if (result)
                        {
                            combined[(localY * w) + localX] = true;
                            anyTrue = true;
                            trueMinX = Math.Min(trueMinX, x);
                            trueMaxX = Math.Max(trueMaxX, x);
                            trueMinY = Math.Min(trueMinY, y);
                            trueMaxY = Math.Max(trueMaxY, y);
                        }
                    }
                }

                if (!anyTrue)
                {
                    MinX = MaxX = MinY = MaxY = -1;
                    Mask = null;
                    HasActiveSelection = false;
                    return;
                }

                int finalW = trueMaxX - trueMinX + 1;
                int finalH = trueMaxY - trueMinY + 1;
                var trimmed = new bool[finalW, finalH];
                for (int y = trueMinY; y <= trueMaxY; y++)
                {
                    for (int x = trueMinX; x <= trueMaxX; x++)
                    {
                        int srcX = x - minX;
                        int srcY = y - minY;
                        trimmed[x - trueMinX, y - trueMinY] = combined[(srcY * w) + srcX];
                    }
                }

                MinX = trueMinX;
                MaxX = trueMaxX;
                MinY = trueMinY;
                MaxY = trueMaxY;
                Mask = trimmed;
                HasActiveSelection = true;
            }
            finally
            {
                ArrayPool<bool>.Shared.Return(combined);
            }
        }

        public void ApplyMask(bool[,] mask, int minX, int minY, int maxX, int maxY, SelectionMode mode)
        {
            bool hasAnyPixel = false;
            int w = mask.GetLength(0);
            int h = mask.GetLength(1);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (mask[x, y]) { hasAnyPixel = true; break; }
                }
                if (hasAnyPixel) break;
            }

            if (!hasAnyPixel)
            {
                if (mode == SelectionMode.Replace || mode == SelectionMode.Intersect)
                {
                    Cancel();
                }
                return;
            }

            // When nothing is selected, ApplyMask(Add, …) should behave like Replace (magic wand etc.);
            // rectangle/lasso never hits ApplyMask during drag—they use CombineWithBase only.
            if (mode == SelectionMode.Replace || !HasActiveSelection)
            {
                if (mode == SelectionMode.Intersect || mode == SelectionMode.Subtract)
                {
                    Cancel();
                    return;
                }
                MinX = minX; MaxX = maxX;
                MinY = minY; MaxY = maxY;
                Mask = mask;
                HasActiveSelection = true;
                IsSelecting = false;
                _lassoPoints.Clear();
                Notify();
                return;
            }

            SnapshotBaseSelection();
            CombineWithBase(mask, minX, minY, maxX, maxY, mode);
            IsSelecting = false;
            _lassoPoints.Clear();
            Notify();
        }

        public void FinalizeSelection()
        {
            if (_lassoPoints.Count > 0)
            {
                if (_lassoPoints.Count < 3)
                {
                    if (_currentMode == SelectionMode.Replace)
                    {
                        Cancel();
                        return;
                    }

                    if (_baseMask != null)
                    {
                        MinX = _baseMinX; MaxX = _baseMaxX;
                        MinY = _baseMinY; MaxY = _baseMaxY;
                        Mask = _baseMask;
                        HasActiveSelection = true;
                    }
                    else
                    {
                        Cancel();
                        return;
                    }
                    IsSelecting = false;
                    _lassoPoints.Clear();
                    Notify();
                    return;
                }
                
                RecomputeCombinedSelection();

                // Release boolean-operation snapshot and temporary polygon vertices.
                _baseMask = null;
                _baseMinX = _baseMaxX = _baseMinY = _baseMaxY = -1;
                _lassoPoints.Clear();
            }

            if (MinX == -1 || MaxX == -1 || MinY == -1 || MaxY == -1)
            {
                Cancel();
                return;
            }

            if (Mask != null && _currentMode == SelectionMode.Replace)
            {
                bool hasAnyPixel = false;
                int w = Mask.GetLength(0);
                int h = Mask.GetLength(1);
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        if (Mask[x, y])
                        {
                            hasAnyPixel = true;
                            break;
                        }
                    }
                    if (hasAnyPixel) break;
                }

                if (!hasAnyPixel)
                {
                    Cancel();
                    return;
                }
            }

            // Release boolean-operation snapshot for rectangle selections too.
            _baseMask = null;
            _baseMinX = _baseMaxX = _baseMinY = _baseMaxY = -1;

            IsSelecting = false;
            HasActiveSelection = true;
            Notify();
        }

        /// <inheritdoc/>
        public void CancelSelectionDrag()
        {
            if (_currentMode != SelectionMode.Replace && _baseMask != null)
            {
                MinX = _baseMinX;
                MaxX = _baseMaxX;
                MinY = _baseMinY;
                MaxY = _baseMaxY;
                Mask = _baseMask;
                HasActiveSelection = true;
                IsSelecting = false;
                _lassoPoints.Clear();
                _baseMask = null;
                _baseMinX = _baseMaxX = _baseMinY = _baseMaxY = -1;
                Notify();
                return;
            }

            Cancel();
        }

        // ── Querying ──────────────────────────────────────────────────────

        public bool IsPixelInSelection(int x, int y)
        {
            if (!HasActiveSelection) return false;
            
            if (IsFloating && FloatingPixels != null)
            {
                if (Math.Abs(RotationAngle) > 0.01)
                {
                    var eff = GetEffectiveFloating();
                    int efx = x - eff.x;
                    int efy = y - eff.y;
                    if (efx < 0 || efx >= eff.w || efy < 0 || efy >= eff.h) return false;
                    if (eff.mask != null && !eff.mask[efx, efy]) return false;
                    return eff.pixels[efx, efy];
                }

                int fx = x - FloatingX;
                int fy = y - FloatingY;
                if (fx < 0 || fx >= FloatingWidth || fy < 0 || fy >= FloatingHeight) return false;
                if (Mask != null && (fx >= Mask.GetLength(0) || fy >= Mask.GetLength(1) || !Mask[fx, fy])) return false;
                return FloatingPixels[fx, fy];
            }

            if (x < MinX || x > MaxX || y < MinY || y > MaxY) return false;
            if (Mask == null) return true;
            int mx = x - MinX;
            int my = y - MinY;
            if (mx < 0 || my < 0 || mx >= Mask.GetLength(0) || my >= Mask.GetLength(1)) return false;
            return Mask[mx, my];
        }

        /// <inheritdoc/>
        public bool IsPointInSelectionBounds(int x, int y)
        {
            if (!HasActiveSelection) return false;

            if (IsFloating)
            {
                // When rotated, inverse-rotate the point into local space
                // and test against the pre-rotation bounding box.
                if (Math.Abs(RotationAngle) > 0.01)
                {
                    double cx = FloatingX + FloatingWidth / 2.0;
                    double cy = FloatingY + FloatingHeight / 2.0;
                    double rad = -RotationAngle * Math.PI / 180.0;
                    double cos = Math.Cos(rad);
                    double sin = Math.Sin(rad);
                    double dx = x - cx;
                    double dy = y - cy;
                    double lx = cos * dx - sin * dy + cx;
                    double ly = sin * dx + cos * dy + cy;
                    return lx >= FloatingX && lx < FloatingX + FloatingWidth
                        && ly >= FloatingY && ly < FloatingY + FloatingHeight;
                }

                return x >= FloatingX && x < FloatingX + FloatingWidth
                    && y >= FloatingY && y < FloatingY + FloatingHeight;
            }

            // Non-floating: fall back to the full pixel-level check
            // (mask may have irregular shapes from lasso/magic-wand)
            return IsPixelInSelection(x, y);
        }

        public bool IsPointInLasso(int x, int y) => IsPointInPolygon(x, y);

        public bool HasAnyPixelInSelection(SpriteState state)
        {
            if (!HasActiveSelection || state == null || state.Width <= 0 || state.Height <= 0)
                return false;

            if (IsFloating && FloatingPixels != null)
            {
                int fw = FloatingPixels.GetLength(0);
                int fh = FloatingPixels.GetLength(1);
                for (int y = 0; y < fh; y++)
                {
                    for (int x = 0; x < fw; x++)
                    {
                        if (FloatingPixels[x, y])
                            return true;
                    }
                }
                return false;
            }

            int w = state.Width;
            int h = state.Height;
            var ovf = state.ActivePixelBuffer as OverflowPixelBuffer;
            bool[]? extData = ovf?.GetExtendedData();

            for (int y = MinY; y <= MaxY; y++)
            {
                for (int x = MinX; x <= MaxX; x++)
                {
                    if (IsPixelInSelection(x, y))
                    {
                        if (ovf != null && extData != null)
                        {
                            int ex = x + ovf.MarginX;
                            int ey = y + ovf.MarginY;
                            if (ex >= 0 && ex < ovf.ExtendedWidth && ey >= 0 && ey < ovf.ExtendedHeight)
                            {
                                if (extData[ey * ovf.ExtendedWidth + ex])
                                    return true;
                            }
                        }
                        else if (x >= 0 && x < w && y >= 0 && y < h)
                        {
                            int idx = (y * w) + x;
                            if (idx >= 0 && idx < state.Pixels.Length && state.Pixels[idx])
                                return true;
                        }
                    }
                }
            }

            return false;
        }

        public int CountPixelsInSelection(SpriteState state)
        {
            if (state == null || !HasActiveSelection) return 0;

            int count = 0;
            if (IsFloating && FloatingPixels != null)
            {
                int fw = FloatingPixels.GetLength(0);
                int fh = FloatingPixels.GetLength(1);
                for (int y = 0; y < fh; y++)
                {
                    for (int x = 0; x < fw; x++)
                    {
                        if (FloatingPixels[x, y])
                            count++;
                    }
                }
                return count;
            }

            int w = state.Width;
            int h = state.Height;
            var ovf = state.ActivePixelBuffer as OverflowPixelBuffer;
            bool[]? extData = ovf?.GetExtendedData();

            for (int y = MinY; y <= MaxY; y++)
            {
                for (int x = MinX; x <= MaxX; x++)
                {
                    if (IsPixelInSelection(x, y))
                    {
                        if (ovf != null && extData != null)
                        {
                            int ex = x + ovf.MarginX;
                            int ey = y + ovf.MarginY;
                            if (ex >= 0 && ex < ovf.ExtendedWidth && ey >= 0 && ey < ovf.ExtendedHeight)
                            {
                                if (extData[ey * ovf.ExtendedWidth + ex])
                                    count++;
                            }
                        }
                        else if (x >= 0 && x < w && y >= 0 && y < h)
                        {
                            int idx = (y * w) + x;
                            if (idx >= 0 && idx < state.Pixels.Length && state.Pixels[idx])
                                count++;
                        }
                    }
                }
            }

            return count;
        }

        public void Reselect()
        {
            if (!CanReselect) return;

            MinX = _lastMinX;
            MinY = _lastMinY;
            MaxX = _lastMaxX;
            MaxY = _lastMaxY;
            Mask = _lastMask != null ? (bool[,])_lastMask.Clone() : null;
            _isEllipseSelection = _lastIsEllipse;
            HasActiveSelection = true;
            IsSelecting = false;
            IsFloating = false;
            FloatingPixels = null;
            FloatingX = FloatingY = FloatingWidth = FloatingHeight = 0;
            RotationAngle = 0;

            Notify();
        }

        public void NudgeSelection(int dx, int dy, int canvasWidth, int canvasHeight)
        {
            if (!HasActiveSelection || IsFloating || IsSelecting) return;
            if (dx == 0 && dy == 0) return;

            int newMinX = MinX + dx;
            int newMaxX = MaxX + dx;
            int newMinY = MinY + dy;
            int newMaxY = MaxY + dy;

            if (canvasWidth > 0 && canvasHeight > 0)
            {
                if (newMaxX < 0 || newMinX >= canvasWidth || newMaxY < 0 || newMinY >= canvasHeight)
                    return;
            }

            MinX = newMinX;
            MaxX = newMaxX;
            MinY = newMinY;
            MaxY = newMaxY;

            Notify();
        }

        // ── Committing / cancelling ───────────────────────────────────────

        public void LiftSelection(SpriteState state)
        {
            if (state == null || !HasActiveSelection || IsFloating) return;

            FloatingWidth = MaxX - MinX + 1;
            FloatingHeight = MaxY - MinY + 1;
            if (FloatingWidth <= 0 || FloatingHeight <= 0) return;

            FloatingX = MinX;
            FloatingY = MinY;
            FloatingPixels = new bool[FloatingWidth, FloatingHeight];

            int w = state.Width;
            int h = state.Height;
            var ovf = state.ActivePixelBuffer as OverflowPixelBuffer;
            bool[]? extData = ovf?.GetExtendedData();

            for (int y = MinY; y <= MaxY; y++)
            {
                for (int x = MinX; x <= MaxX; x++)
                {
                    if (IsPixelInSelection(x, y))
                    {
                        if (ovf != null && extData != null)
                        {
                            int ex = x + ovf.MarginX;
                            int ey = y + ovf.MarginY;
                            if (ex >= 0 && ex < ovf.ExtendedWidth && ey >= 0 && ey < ovf.ExtendedHeight)
                            {
                                if (extData[ey * ovf.ExtendedWidth + ex])
                                {
                                    FloatingPixels[x - MinX, y - MinY] = true;
                                    ovf.SetPixelNoInvalidate(x, y, value: false);
                                    
                                    if (x >= 0 && x < w && y >= 0 && y < h)
                                    {
                                        state.Pixels[(y * w) + x] = false;
                                    }
                                }
                            }
                        }
                        else if (x >= 0 && x < w && y >= 0 && y < h)
                        {
                            int idx = (y * w) + x;
                            if (state.Pixels[idx])
                            {
                                FloatingPixels[x - MinX, y - MinY] = true;
                                state.Pixels[idx] = false;
                            }
                        }
                    }
                }
            }
            if (ovf != null) ovf.InvalidateViewCache();

            IsFloating = true;

            // Capture pristine pixels at lift time — this is the one true source
            // for all future resamples, surviving across multiple transform sessions.
            _pristineFloatingPixels = (bool[,])FloatingPixels.Clone();
            _pristineFloatingW = FloatingWidth;
            _pristineFloatingH = FloatingHeight;
            _pristineMask = Mask != null ? (bool[,])Mask.Clone() : null;
            _pristineMaskW = FloatingWidth;
            _pristineMaskH = FloatingHeight;
            _activeTransformFlipX = false;
            _activeTransformFlipY = false;

            Notify();
        }

        public void ReplaceFloatingData(bool[,] pixels, int x, int y, int w, int h)
        {
            if (!IsFloating || pixels == null || w <= 0 || h <= 0) return;
            if (pixels.GetLength(0) < w || pixels.GetLength(1) < h) return;

            FloatingPixels = pixels;
            FloatingX = x;
            FloatingY = y;
            FloatingWidth = w;
            FloatingHeight = h;
            _pristineFloatingPixels = (bool[,])pixels.Clone();
            _pristineFloatingW = w;
            _pristineFloatingH = h;
            _pristineMask = (bool[,])pixels.Clone();
            _pristineMaskW = w;
            _pristineMaskH = h;
            _activeTransformFlipX = false;
            _activeTransformFlipY = false;
            
            // Sync the selection mask and bounds to match the injected overflow data so the UI renderer 
            // traces the correct full-size pixel outline rather than the obsolete canvas-sized mask.
            Mask = (bool[,])pixels.Clone();
            MinX = x;
            MinY = y;
            MaxX = x + w - 1;
            MaxY = y + h - 1;
            
            Notify();
        }

        public void CommitSelection(SpriteState state, FloatingPasteMode pasteMode = FloatingPasteMode.Transparent)
        {
            if (state == null || !HasActiveSelection) return;

            if (IsFloating && FloatingPixels != null)
            {
                // If a committed rotation is pending, bake the rotation into pixels
                // before stamping onto the canvas.
                var eff = GetEffectiveFloating();

                int w = state.Width;
                int h = state.Height;
                var activeBuffer = state.ActivePixelBuffer as OverflowPixelBuffer;
                for (int fy = 0; fy < eff.h; fy++)
                {
                    for (int fx = 0; fx < eff.w; fx++)
                    {
                        if (eff.mask != null && (fx >= eff.mask.GetLength(0) || fy >= eff.mask.GetLength(1) || !eff.mask[fx, fy])) continue;

                        int gx = eff.x + fx;
                        int gy = eff.y + fy;

                        bool floatingPixel = eff.pixels[fx, fy];
                        if (activeBuffer != null)
                        {
                            if (pasteMode == FloatingPasteMode.Transparent)
                            {
                                if (floatingPixel)
                                    activeBuffer.SetPixel(gx, gy, value: true);
                            }
                            else
                            {
                                activeBuffer.SetPixel(gx, gy, floatingPixel);
                            }
                        }
                        else
                        {
                            if (gx < 0 || gx >= w || gy < 0 || gy >= h) continue;

                            int idx = (gy * w) + gx;
                            if (idx < 0 || idx >= state.Pixels.Length) continue;

                            if (pasteMode == FloatingPasteMode.Transparent)
                            {
                                if (floatingPixel)
                                    state.Pixels[idx] = true;
                            }
                            else
                            {
                                state.Pixels[idx] = floatingPixel;
                            }
                        }
                    }
                }
                
                if (activeBuffer != null)
                {
                    // Update state.Pixels view
                    var mono = activeBuffer.GetMonochromeData();
                    int copyLen = Math.Min(mono.Length, state.Pixels.Length);
                    Array.Copy(mono, state.Pixels, copyLen);
                }

                MinX = eff.x;
                MinY = eff.y;
                MaxX = eff.x + eff.w - 1;
                MaxY = eff.y + eff.h - 1;

                // The floating pixels represent the exact active geometry of the
                // rotated/resized selection. Adopt it as the new mask to prevent
                // index-out-of-bounds crashes when the original mask size is stale.
                Mask = eff.mask != null ? (bool[,])eff.mask.Clone() : (bool[,])eff.pixels.Clone();

                FloatingPixels = null;
                IsFloating = false;
                FloatingX = FloatingY = FloatingWidth = FloatingHeight = 0;

                _baseMask = null;
                _baseMinX = _baseMaxX = _baseMinY = _baseMaxY = -1;
                _dragMinX = _dragMaxX = _dragMinY = _dragMaxY = -1;
                _lassoPoints.Clear();

                IsTransforming = false;
                ActiveTransformHandle = TransformHandle.None;
                RotationAngle = 0;
                _originalFloatingPixels = null;
                _originalMask = null;
                _activeTransformFlipX = false;
                _activeTransformFlipY = false;
                IsDragging = false;

                Notify();
            }
        }

        public void DeleteSelection(SpriteState state)
        {
            if (state == null || !HasActiveSelection || state.Width <= 0 || state.Height <= 0) return;

            if (IsFloating)
            {
                // Just drop the floating pixels — lifting already cleared pixels from the canvas.
                ResetState(notify: false);
            }
            else
            {
                int w = state.Width;
                int h = state.Height;

                if (state.ActivePixelBuffer is OverflowPixelBuffer ovf)
                {
                    for (int y = MinY; y <= MaxY; y++)
                    {
                        for (int x = MinX; x <= MaxX; x++)
                        {
                            if (IsPixelInSelection(x, y))
                            {
                                ovf.SetPixelNoInvalidate(x, y, value: false);
                                if (x >= 0 && x < w && y >= 0 && y < h)
                                {
                                    state.Pixels[(y * w) + x] = false;
                                }
                            }
                        }
                    }
                    ovf.InvalidateViewCache();
                }
                else
                {
                    for (int i = 0; i < state.Pixels.Length; i++)
                    {
                        int x = i % w;
                        int y = i / w;
                        if (IsPixelInSelection(x, y))
                            state.Pixels[i] = false;
                    }
                }

                // Match expected UX: deleting a selection should also clear
                // selection state (marquee/lasso preview goes away).
                ResetState(notify: false);
            }

            Notify();
        }

        public void Cancel() => ResetState(notify: true);

        // ── Drag ─────────────────────────────────────────────────────────

        public void BeginDrag()
        {
            IsDragging = true;
            Notify();
        }

        public void MoveFloatingTo(int newX, int newY)
        {
            if (!IsFloating) return;

            FloatingX = newX;
            FloatingY = newY;
            Notify();
        }

        public void EndDrag()
        {
            IsDragging = false;
            Notify();
        }

        /// <inheritdoc/>
        public (bool[,] pixels, bool[,]? mask, int x, int y, int w, int h) GetEffectiveFloating()
        {
            if (FloatingPixels == null)
                return (new bool[0, 0], null, 0, 0, 0, 0);

            // FloatingPixels is always stored in the pre-rotation orientation.
            // When a rotation angle is active, compute the rotated pixels and
            // AABB position on-the-fly for rendering/compositing.
            if (Math.Abs(RotationAngle) > 0.01)
            {
                var (rotated, rw, rh) = RotatePixels2D(
                    FloatingPixels, FloatingWidth, FloatingHeight, RotationAngle);
                
                bool[,]? rotatedMask = null;
                if (Mask != null)
                {
                    var (rm, _, _) = RotatePixels2D(Mask, Mask.GetLength(0), Mask.GetLength(1), RotationAngle);
                    rotatedMask = rm;
                }

                double cx = FloatingX + FloatingWidth / 2.0;
                double cy = FloatingY + FloatingHeight / 2.0;
                int rx = (int)Math.Round(cx - rw / 2.0, MidpointRounding.AwayFromZero);
                int ry = (int)Math.Round(cy - rh / 2.0, MidpointRounding.AwayFromZero);
                return (rotated, rotatedMask, rx, ry, rw, rh);
            }

            return (FloatingPixels, Mask, FloatingX, FloatingY, FloatingWidth, FloatingHeight);
        }

        // ── Clipboard integration ────────────────────────────────────────

        public PixelClipboardData? CopySelection(SpriteState state)
        {
            if (!HasActiveSelection || state == null || state.Width <= 0 || state.Height <= 0) return null;

            if (IsFloating && FloatingPixels != null)
            {
                // Copy from the floating layer (with any committed rotation applied)
                var eff = GetEffectiveFloating();
                if (eff.w <= 0 || eff.h <= 0) return null;
                var copy = new bool[eff.w, eff.h];
                Array.Copy(eff.pixels, copy, eff.pixels.Length);
                return new PixelClipboardData(copy, eff.w, eff.h);
            }

            // Copy from the canvas (and overflow buffer if present) using the selection bounds + mask
            int w = MaxX - MinX + 1;
            int h = MaxY - MinY + 1;
            if (w <= 0 || h <= 0) return null;
            var pixels = new bool[w, h];

            int canvasW = state.Width;
            var ovf = state.ActivePixelBuffer as OverflowPixelBuffer;
            bool[]? extData = ovf?.GetExtendedData();

            for (int y = MinY; y <= MaxY; y++)
            {
                for (int x = MinX; x <= MaxX; x++)
                {
                    if (!IsPixelInSelection(x, y)) continue;

                    if (ovf != null && extData != null)
                    {
                        int ex = x + ovf.MarginX;
                        int ey = y + ovf.MarginY;
                        if (ex >= 0 && ex < ovf.ExtendedWidth && ey >= 0 && ey < ovf.ExtendedHeight)
                        {
                            pixels[x - MinX, y - MinY] = extData[ey * ovf.ExtendedWidth + ex];
                        }
                    }
                    else
                    {
                        if (x < 0 || x >= state.Width || y < 0 || y >= state.Height) continue;
                        int idx = (y * canvasW) + x;
                        pixels[x - MinX, y - MinY] = state.Pixels[idx];
                    }
                }
            }

            return new PixelClipboardData(pixels, w, h);
        }

        public void PasteAsFloating(PixelClipboardData data, int canvasWidth, int canvasHeight)
        {
            if (data == null || data.Pixels == null || data.Width <= 0 || data.Height <= 0) return;

            // Reset any existing selection state
            ResetState(notify: false);

            // Center the pasted content on the canvas
            FloatingX = (canvasWidth - data.Width) / 2;
            FloatingY = (canvasHeight - data.Height) / 2;
            FloatingWidth = data.Width;
            FloatingHeight = data.Height;
            FloatingPixels = new bool[data.Width, data.Height];
            Array.Copy(data.Pixels, FloatingPixels, data.Pixels.Length);

            // Capture pristine pixels at paste time
            _pristineFloatingPixels = (bool[,])FloatingPixels.Clone();
            _pristineFloatingW = data.Width;
            _pristineFloatingH = data.Height;
            _pristineMask = Mask != null ? (bool[,])Mask.Clone() : null;
            _pristineMaskW = data.Width;
            _pristineMaskH = data.Height;
            _activeTransformFlipX = false;
            _activeTransformFlipY = false;

            // Mark as a floating active selection
            IsFloating = true;
            HasActiveSelection = true;

            // Set selection bounds to match the floating layer
            MinX = FloatingX;
            MinY = FloatingY;
            MaxX = FloatingX + FloatingWidth - 1;
            MaxY = FloatingY + FloatingHeight - 1;

            Notify();
        }

        public void PasteAsFloatingAt(PixelClipboardData data, int x, int y)
        {
            if (data == null || data.Pixels == null || data.Width <= 0 || data.Height <= 0) return;

            // Reset any existing selection state
            ResetState(notify: false);

            FloatingX = x;
            FloatingY = y;
            FloatingWidth = data.Width;
            FloatingHeight = data.Height;
            FloatingPixels = new bool[data.Width, data.Height];
            Array.Copy(data.Pixels, FloatingPixels, data.Pixels.Length);

            // Capture pristine pixels at paste time
            _pristineFloatingPixels = (bool[,])FloatingPixels.Clone();
            _pristineFloatingW = data.Width;
            _pristineFloatingH = data.Height;

            // Mark as a floating active selection
            IsFloating = true;
            HasActiveSelection = true;

            // Set selection bounds to match the floating layer
            MinX = FloatingX;
            MinY = FloatingY;
            MaxX = FloatingX + FloatingWidth - 1;
            MaxY = FloatingY + FloatingHeight - 1;

            Notify();
        }
        // ── Snapshots ────────────────────────────────────────────────────

        public SelectionSnapshot CreateSnapshot()
        {
            // Transient interaction flags (IsTransforming, IsDragging, ActiveTransformHandle)
            // are always cleared in snapshots. These are ephemeral input-controller state that
            // should never survive an undo/redo restore — restoring IsTransforming=true without
            // an active controller causes GetEffectiveFloating() to skip committed-rotation
            // rendering (it guards on !IsTransforming) and leaves the selection in a broken
            // phantom-transform state. Similarly, _originalFloatingPixels is a transform-local
            // cache that would be dangling after restore.
            return new SelectionSnapshot
            {
                HasActiveSelection = HasActiveSelection,
                IsSelecting = IsSelecting,
                IsFloating = IsFloating,
                IsDragging = false,
                IsTransforming = false,
                ActiveTransformHandle = TransformHandle.None,
                RotationAngle = RotationAngle,
                OriginalFloatingPixels = null,
                OriginalFloatingX = _originalFloatingX,
                OriginalFloatingY = _originalFloatingY,
                OriginalFloatingWidth = _originalFloatingW,
                OriginalFloatingHeight = _originalFloatingH,
                PristineFloatingPixels = _pristineFloatingPixels != null ? (bool[,])_pristineFloatingPixels.Clone() : null,
                PristineFloatingWidth = _pristineFloatingW,
                PristineFloatingHeight = _pristineFloatingH,
                MinX = MinX,
                MaxX = MaxX,
                MinY = MinY,
                MaxY = MaxY,
                Mask = Mask != null ? (bool[,])Mask.Clone() : null,
                FloatingPixels = FloatingPixels != null ? (bool[,])FloatingPixels.Clone() : null,
                FloatingX = FloatingX,
                FloatingY = FloatingY,
                FloatingWidth = FloatingWidth,
                FloatingHeight = FloatingHeight,
                LassoPoints = [.. _lassoPoints],
            };
        }

        public void RestoreSnapshot(SelectionSnapshot snapshot)
        {
            if (snapshot == null) return;

            HasActiveSelection = snapshot.HasActiveSelection;
            IsSelecting = snapshot.IsSelecting;
            IsFloating = HasActiveSelection && snapshot.IsFloating && snapshot.FloatingPixels != null;
            IsDragging = false;
            IsTransforming = false;
            ActiveTransformHandle = TransformHandle.None;
            RotationAngle = snapshot.RotationAngle;
            _originalFloatingPixels = snapshot.OriginalFloatingPixels != null ? (bool[,])snapshot.OriginalFloatingPixels.Clone() : null;
            _originalFloatingX = snapshot.OriginalFloatingX;
            _originalFloatingY = snapshot.OriginalFloatingY;
            _originalFloatingW = snapshot.OriginalFloatingWidth;
            _originalFloatingH = snapshot.OriginalFloatingHeight;
            MinX = snapshot.MinX;
            MaxX = snapshot.MaxX;
            MinY = snapshot.MinY;
            MaxY = snapshot.MaxY;
            Mask = snapshot.Mask != null ? (bool[,])snapshot.Mask.Clone() : null;
            FloatingPixels = snapshot.FloatingPixels != null ? (bool[,])snapshot.FloatingPixels.Clone() : null;
            FloatingX = snapshot.FloatingX;
            FloatingY = snapshot.FloatingY;
            FloatingWidth = snapshot.FloatingWidth;
            FloatingHeight = snapshot.FloatingHeight;

            _lassoPoints.Clear();
            if (snapshot.LassoPoints != null)
            {
                _lassoPoints.AddRange(snapshot.LassoPoints);
            }

            // Restore pristine data from snapshot
            _pristineFloatingPixels = snapshot.PristineFloatingPixels != null ? (bool[,])snapshot.PristineFloatingPixels.Clone() : null;
            _pristineFloatingW = snapshot.PristineFloatingWidth;
            _pristineFloatingH = snapshot.PristineFloatingHeight;
            _originalMask = null;
            _activeTransformFlipX = false;
            _activeTransformFlipY = false;
            _pristineMask = snapshot.Mask != null ? (bool[,])snapshot.Mask.Clone() : null;
            _pristineMaskW = snapshot.FloatingWidth;
            _pristineMaskH = snapshot.FloatingHeight;

            // Clear intermediate boolean operation state
            _baseMask = null;
            _baseMinX = _baseMaxX = _baseMinY = _baseMaxY = -1;

            Notify();
        }

        // ── Transform (resize floating selection) ─────────────────────────

        public void BeginTransform(TransformHandle handle)
        {
            if (!HasActiveSelection || handle == TransformHandle.None)
                return;
            if (!IsFloating || FloatingPixels == null)
                return;

            ActiveTransformHandle = handle;
            IsTransforming = true;
            _preTransformRotationAngle = RotationAngle;
            _originalFloatingPixels = (bool[,])FloatingPixels.Clone();
            _originalFloatingX = FloatingX;
            _originalFloatingY = FloatingY;
            _originalFloatingW = FloatingWidth;
            _originalFloatingH = FloatingHeight;
            _originalMask = Mask != null ? (bool[,])Mask.Clone() : null;
            _activeTransformFlipX = false;
            _activeTransformFlipY = false;

            // Ensure pristine data exists (defensive — should already be set by
            // LiftSelection / PasteAsFloating, but guard against edge cases).
            if (_pristineFloatingPixels == null)
            {
                _pristineFloatingPixels = (bool[,])FloatingPixels.Clone();
                _pristineFloatingW = FloatingWidth;
                _pristineFloatingH = FloatingHeight;
            }
            if (_pristineMask == null && Mask != null)
            {
                _pristineMask = (bool[,])Mask.Clone();
                _pristineMaskW = FloatingWidth;
                _pristineMaskH = FloatingHeight;
            }

            Notify();
        }

        public void UpdateTransform(int newX, int newY, int newW, int newH, bool flipX = false, bool flipY = false)
        {
            if (!IsTransforming || _originalFloatingPixels == null)
                return;

            const int maxTransformDimension = 4096;
            newW = Math.Clamp(newW, 1, maxTransformDimension);
            newH = Math.Clamp(newH, 1, maxTransformDimension);

            FloatingX = newX;
            FloatingY = newY;
            FloatingWidth = newW;
            FloatingHeight = newH;
            _activeTransformFlipX = flipX;
            _activeTransformFlipY = flipY;

            // Always resample from the pristine (original lifted) pixels to avoid
            // cascading quality loss when scaling down then back up.
            var resampleSrc = _pristineFloatingPixels ?? _originalFloatingPixels;
            int resampleW = _pristineFloatingPixels != null ? _pristineFloatingW : _originalFloatingW;
            int resampleH = _pristineFloatingPixels != null ? _pristineFloatingH : _originalFloatingH;
            FloatingPixels = ResamplePixels(resampleSrc, resampleW, resampleH, newW, newH);

            if (_pristineMask != null || _originalMask != null || Mask != null)
            {
                var maskSrc = _pristineMask ?? _originalMask ?? Mask;
                int maskSrcW = _pristineMask != null ? _pristineMaskW : (_originalMask != null ? _originalFloatingW : (Mask?.GetLength(0) ?? newW));
                int maskSrcH = _pristineMask != null ? _pristineMaskH : (_originalMask != null ? _originalFloatingH : (Mask?.GetLength(1) ?? newH));
                if (maskSrc != null)
                {
                    Mask = ResamplePixels(maskSrc, maskSrcW, maskSrcH, newW, newH);
                    if (flipX) MirrorXInPlace(Mask, newW, newH);
                    if (flipY) MirrorYInPlace(Mask, newW, newH);
                }
            }

            // Safety check: ensure buffer dimensions match expected size
            if (FloatingPixels != null &&
                (FloatingPixels.GetLength(0) != newW || FloatingPixels.GetLength(1) != newH))
            {
                // Mismatch - safely copy existing data into a properly sized buffer to avoid data loss
                var emergencyBuffer = new bool[newW, newH];
                int copyW = Math.Min(newW, FloatingPixels.GetLength(0));
                int copyH = Math.Min(newH, FloatingPixels.GetLength(1));
                
                for (int y = 0; y < copyH; y++)
                {
                    for (int x = 0; x < copyW; x++)
                    {
                        emergencyBuffer[x, y] = FloatingPixels[x, y];
                    }
                }
                        
                FloatingPixels = emergencyBuffer;
            }

            if (FloatingPixels != null)
            {
                if (flipX) MirrorXInPlace(FloatingPixels, newW, newH);
                if (flipY) MirrorYInPlace(FloatingPixels, newW, newH);
            }

            MinX = newX;
            MinY = newY;
            MaxX = newX + newW - 1;
            MaxY = newY + newH - 1;
            Notify();
        }

        public void CommitTransform()
        {
            if (!IsTransforming)
                return;

            // End the transform session but keep RotationAngle — the
            // rotation persists visually (overlay draws rotated box) until
            // the selection is stamped onto the canvas by CommitSelection.
            // FloatingPixels stays in the pre-rotation orientation;
            // GetEffectiveFloating() applies the rotation on-the-fly.
            if (_activeTransformFlipX && _pristineFloatingPixels != null)
                MirrorXInPlace(_pristineFloatingPixels, _pristineFloatingW, _pristineFloatingH);
            if (_activeTransformFlipY && _pristineFloatingPixels != null)
                MirrorYInPlace(_pristineFloatingPixels, _pristineFloatingW, _pristineFloatingH);
            if (_activeTransformFlipX && _pristineMask != null)
                MirrorXInPlace(_pristineMask, _pristineMaskW, _pristineMaskH);
            if (_activeTransformFlipY && _pristineMask != null)
                MirrorYInPlace(_pristineMask, _pristineMaskW, _pristineMaskH);

            IsTransforming = false;
            ActiveTransformHandle = TransformHandle.None;
            _originalFloatingPixels = null;
            _originalMask = null;
            _activeTransformFlipX = false;
            _activeTransformFlipY = false;

            MinX = FloatingX;
            MinY = FloatingY;
            MaxX = FloatingX + FloatingWidth - 1;
            MaxY = FloatingY + FloatingHeight - 1;
            Notify();
        }

        public void CancelTransform()
        {
            if (!IsTransforming)
                return;

            if (_originalFloatingPixels != null)
            {
                FloatingPixels = (bool[,])_originalFloatingPixels.Clone();
                FloatingX = _originalFloatingX;
                FloatingY = _originalFloatingY;
                FloatingWidth = _originalFloatingW;
                FloatingHeight = _originalFloatingH;

                MinX = FloatingX;
                MinY = FloatingY;
                MaxX = FloatingX + FloatingWidth - 1;
                MaxY = FloatingY + FloatingHeight - 1;
            }

            if (_originalMask != null)
            {
                Mask = (bool[,])_originalMask.Clone();
            }

            IsTransforming = false;
            ActiveTransformHandle = TransformHandle.None;
            RotationAngle = _preTransformRotationAngle;
            _originalFloatingPixels = null;
            _originalMask = null;
            _activeTransformFlipX = false;
            _activeTransformFlipY = false;
            Notify();
        }

        public void UpdateRotation(double angleDeg)
        {
            if (!IsTransforming)
                return;

            // Don't bake pixels — just store the angle.
            // FloatingPixels/X/Y/W/H stay in the pre-rotation orientation.
            // GetEffectiveFloating() applies the rotation on-the-fly for rendering.
            RotationAngle = angleDeg;
            Notify();
        }

        /// <summary>
        /// Rotates a bool[,] pixel buffer by an arbitrary angle (in degrees) around its center.
        /// Uses inverse-mapping with nearest-neighbor sampling — ideal for pixel art.
        /// Returns the rotated buffer and its new dimensions.
        /// </summary>
        internal static (bool[,] dst, int dw, int dh) RotatePixels2D(bool[,] src, int sw, int sh, double angleDeg)
        {
            if (sw <= 0 || sh <= 0)
                return (new bool[1, 1], 1, 1);

            if (!double.IsFinite(angleDeg))
                angleDeg = 0;

            double normalized = ((angleDeg % 360.0) + 360.0) % 360.0;
            if (Math.Abs(normalized) < 0.001 || Math.Abs(normalized - 360.0) < 0.001)
                return ((bool[,])src.Clone(), sw, sh);

            if (Math.Abs(normalized - 90.0) < 0.001)
            {
                var dst90 = new bool[sh, sw];
                for (int y = 0; y < sh; y++)
                    for (int x = 0; x < sw; x++)
                        dst90[sh - 1 - y, x] = src[x, y];
                return (dst90, sh, sw);
            }
            if (Math.Abs(normalized - 180.0) < 0.001)
            {
                var dst180 = new bool[sw, sh];
                for (int y = 0; y < sh; y++)
                    for (int x = 0; x < sw; x++)
                        dst180[sw - 1 - x, sh - 1 - y] = src[x, y];
                return (dst180, sw, sh);
            }
            if (Math.Abs(normalized - 270.0) < 0.001)
            {
                var dst270 = new bool[sh, sw];
                for (int y = 0; y < sh; y++)
                    for (int x = 0; x < sw; x++)
                        dst270[y, sw - 1 - x] = src[x, y];
                return (dst270, sh, sw);
            }

            double angleRad = angleDeg * Math.PI / 180.0;
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);

            // Compute the bounding box of the rotated rectangle.
            // Rotate all 4 corners of the source around its center and find extents.
            double cx = (sw - 1) / 2.0;
            double cy = (sh - 1) / 2.0;

            double[] cornersX = [0, sw - 1, sw - 1, 0];
            double[] cornersY = [0, 0, sh - 1, sh - 1];

            double minRx = double.MaxValue, maxRx = double.MinValue;
            double minRy = double.MaxValue, maxRy = double.MinValue;

            for (int i = 0; i < 4; i++)
            {
                double dx = cornersX[i] - cx;
                double dy = cornersY[i] - cy;
                double rx = cos * dx - sin * dy;
                double ry = sin * dx + cos * dy;
                minRx = Math.Min(minRx, rx);
                maxRx = Math.Max(maxRx, rx);
                minRy = Math.Min(minRy, ry);
                maxRy = Math.Max(maxRy, ry);
            }

            int dw = Math.Max(1, (int)Math.Ceiling(maxRx - minRx) + 1);
            int dh = Math.Max(1, (int)Math.Ceiling(maxRy - minRy) + 1);
            var dst = new bool[dw, dh];

            double dcx = (dw - 1) / 2.0;
            double dcy = (dh - 1) / 2.0;

            // Inverse-map: for each destination pixel, find the corresponding source pixel.
            double cosInv = Math.Cos(-angleRad);
            double sinInv = Math.Sin(-angleRad);

            for (int dy = 0; dy < dh; dy++)
            {
                double relY = dy - dcy;
                for (int dx = 0; dx < dw; dx++)
                {
                    double relX = dx - dcx;
                    // Rotate backwards to find the source coordinate
                    double srcXd = cosInv * relX - sinInv * relY + cx;
                    double srcYd = sinInv * relX + cosInv * relY + cy;

                    int sx = (int)Math.Round(srcXd, MidpointRounding.AwayFromZero);
                    int sy = (int)Math.Round(srcYd, MidpointRounding.AwayFromZero);

                    if (sx >= 0 && sx < sw && sy >= 0 && sy < sh)
                        dst[dx, dy] = src[sx, sy];
                }
            }

            return (dst, dw, dh);
        }

        // ── Flip (floating selection only) ─────────────────────────────

        public void FlipFloatingHorizontally()
        {
            if (!IsFloating || FloatingPixels == null) return;

            // Don't allow resampling-based resize to continue after a flip;
            // commit now to end transform mode (and avoid stale _originalFloatingPixels).
            if (IsTransforming)
                CommitTransform();

            int fw = FloatingWidth;
            int fh = FloatingHeight;
            var flipped = new bool[fw, fh];

            for (int y = 0; y < fh; y++)
            {
                for (int x = 0; x < fw; x++)
                {
                    flipped[fw - 1 - x, y] = FloatingPixels[x, y];
                }
            }

            FloatingPixels = flipped;

            // Flip also invalidates the pristine data — the user's intent is to
            // permanently flip, so future resamples should use the flipped version.
            if (_pristineFloatingPixels != null)
            {
                int pw = _pristineFloatingW;
                int ph = _pristineFloatingH;
                var flippedPristine = new bool[pw, ph];
                for (int py = 0; py < ph; py++)
                    for (int px = 0; px < pw; px++)
                        flippedPristine[pw - 1 - px, py] = _pristineFloatingPixels[px, py];
                _pristineFloatingPixels = flippedPristine;
            }

            if (_pristineMask != null)
            {
                int pw = _pristineMaskW;
                int ph = _pristineMaskH;
                var flippedPristineMask = new bool[pw, ph];
                for (int py = 0; py < ph; py++)
                    for (int px = 0; px < pw; px++)
                        flippedPristineMask[pw - 1 - px, py] = _pristineMask[px, py];
                _pristineMask = flippedPristineMask;
            }

            // Keep the per-pixel boundary mask aligned with the flipped content.
            if (Mask != null && Mask.GetLength(0) == fw && Mask.GetLength(1) == fh)
            {
                var flippedMask = new bool[fw, fh];
                for (int y = 0; y < fh; y++)
                {
                    for (int x = 0; x < fw; x++)
                        flippedMask[fw - 1 - x, y] = Mask[x, y];
                }
                Mask = flippedMask;
            }

            Notify();
        }

        public void FlipFloatingVertically()
        {
            if (!IsFloating || FloatingPixels == null) return;

            if (IsTransforming)
                CommitTransform();

            int fw = FloatingWidth;
            int fh = FloatingHeight;
            var flipped = new bool[fw, fh];

            for (int y = 0; y < fh; y++)
            {
                int ny = fh - 1 - y;
                for (int x = 0; x < fw; x++)
                {
                    flipped[x, ny] = FloatingPixels[x, y];
                }
            }

            FloatingPixels = flipped;

            // Flip also invalidates the pristine data — keep it in sync.
            if (_pristineFloatingPixels != null)
            {
                int pw = _pristineFloatingW;
                int ph = _pristineFloatingH;
                var flippedPristine = new bool[pw, ph];
                for (int py = 0; py < ph; py++)
                {
                    int npy = ph - 1 - py;
                    for (int px = 0; px < pw; px++)
                        flippedPristine[px, npy] = _pristineFloatingPixels[px, py];
                }
                _pristineFloatingPixels = flippedPristine;
            }

            if (_pristineMask != null)
            {
                int pw = _pristineMaskW;
                int ph = _pristineMaskH;
                var flippedPristineMask = new bool[pw, ph];
                for (int py = 0; py < ph; py++)
                {
                    int npy = ph - 1 - py;
                    for (int px = 0; px < pw; px++)
                        flippedPristineMask[px, npy] = _pristineMask[px, py];
                }
                _pristineMask = flippedPristineMask;
            }

            if (Mask != null && Mask.GetLength(0) == fw && Mask.GetLength(1) == fh)
            {
                var flippedMask = new bool[fw, fh];
                for (int y = 0; y < fh; y++)
                {
                    int ny = fh - 1 - y;
                    for (int x = 0; x < fw; x++)
                        flippedMask[x, ny] = Mask[x, y];
                }
                Mask = flippedMask;
            }

            Notify();
        }

        internal static bool[,] ResamplePixels(bool[,] src, int sw, int sh, int dw, int dh)
        {
            // Fix #5: Guard before allocation — new bool[0,0] is legal but
            // new bool[-1,n] would throw OverflowException before reaching the old guard.
            if (sw <= 0 || sh <= 0 || dw <= 0 || dh <= 0)
                return new bool[Math.Max(dw, 1), Math.Max(dh, 1)];

            // Safety clamp to avoid runaway OOM allocations on extreme cursor drags
            const int maxDimension = 4096;
            dw = Math.Clamp(dw, 1, maxDimension);
            dh = Math.Clamp(dh, 1, maxDimension);

            // 1:1 scale fast path: exact clone, zero resampling degradation, zero GC thrash
            if (dw == sw && dh == sh)
                return (bool[,])src.Clone();

            var dst = new bool[dw, dh];

            // Detect near-integer scales for ScaleNx optimization
            // Scale2x/3x/4x provide superior quality for pixel art at exact integer ratios
            // Only use when dimensions exactly match the integer multiple
            int DetectScaleFactor(int srcDim, int dstDim)
            {
                if (srcDim <= 0) return 0;
                int ratio = dstDim / srcDim;
                if (ratio < 2 || ratio > 4) return 0;
                // Fix #4: Require exact match. The old ±1 tolerance caused ScaleNx to return
                // a (srcDim*ratio)-sized array when the caller requested (srcDim*ratio - 1),
                // producing a 1-pixel dimension mismatch caught only by UpdateTransform's
                // emergency-copy guard (an avoidable double-allocation every time).
                if (dstDim == srcDim * ratio)
                    return ratio;
                return 0;
            }

            int scaleX = DetectScaleFactor(sw, dw);
            int scaleY = DetectScaleFactor(sh, dh);

            // Use ScaleNx when both dimensions are at the same integer ratio (2x, 3x, or 4x)
            if (scaleX > 0 && scaleX == scaleY)
            {
                return ScaleNx(src, sw, sh, scaleX);
            }

            // Super-sample factor: higher = better quality but slower
            // 4x provides good quality for pixel art without excessive cost
            const int superSample = 4;

            // Build super-sampled buffer for high-quality resampling
            int ssw = dw * superSample;
            int ssh = dh * superSample;
            var superBuffer = new bool[ssw, ssh];

            double superScaleX = (double)ssw / sw;
            double superScaleY = (double)ssh / sh;

            // Phase 1: Render source pixels to super-sampled buffer
            // Each source pixel becomes a crisp block (no interpolation)
            for (int sy = 0; sy < sh; sy++)
            {
                int yStart = (int)(sy * superScaleY);
                int yEnd = Math.Min((int)((sy + 1) * superScaleY), ssh);

                for (int sx = 0; sx < sw; sx++)
                {
                    if (!src[sx, sy]) continue;

                    int xStart = (int)(sx * superScaleX);
                    int xEnd = Math.Min((int)((sx + 1) * superScaleX), ssw);

                    for (int y = yStart; y < yEnd; y++)
                        for (int x = xStart; x < xEnd; x++)
                            superBuffer[x, y] = true;
                }
            }

            // Phase 2: Detect edges in super-sampled buffer for edge-aware sampling
            bool IsEdge(int x, int y)
            {
                if (x <= 0 || x >= ssw - 1 || y <= 0 || y >= ssh - 1) return true;
                bool center = superBuffer[x, y];
                return superBuffer[x - 1, y] != center ||
                       superBuffer[x + 1, y] != center ||
                       superBuffer[x, y - 1] != center ||
                       superBuffer[x, y + 1] != center;
            }

            // Phase 3: Downsample with edge and connectivity awareness
            for (int ty = 0; ty < dh; ty++)
            {
                int yStart = ty * superSample;
                int yEnd = Math.Min(yStart + superSample, ssh);

                for (int tx = 0; tx < dw; tx++)
                {
                    int xStart = tx * superSample;
                    int xEnd = Math.Min(xStart + superSample, ssw);

                    // Count filled samples and edge samples
                    int filled = 0;
                    int edges = 0;
                    int total = 0;

                    for (int y = yStart; y < yEnd; y++)
                    {
                        for (int x = xStart; x < xEnd; x++)
                        {
                            total++;
                            if (superBuffer[x, y])
                            {
                                filled++;
                                if (IsEdge(x, y)) edges++;
                            }
                        }
                    }

                    if (total == 0)
                    {
                        dst[tx, ty] = false;
                        continue;
                    }

                    double fillRatio = (double)filled / total;
                    double edgeRatio = (double)edges / Math.Max(filled, 1);

                    // Adaptive threshold based on edge presence
                    // High edge ratio = preserve pixel (it's part of an edge)
                    // Low edge ratio = use standard threshold
                    double threshold;
                    if (edgeRatio > 0.3)  // High edge concentration
                        threshold = 0.15;  // Very permissive - preserve edges
                    else if (fillRatio > 0.6)
                        threshold = 0.35;  // Solid area, slightly stricter
                    else
                        threshold = 0.25;  // Standard threshold

                    dst[tx, ty] = fillRatio >= threshold;
                }
            }

            // Phase 4: Post-process to preserve connectivity and remove isolated pixels
            PreserveConnectivity(dst, dw, dh);

            return dst;
        }

        /// <summary>
        /// Post-processing pass to clean up artifacts while preserving important features:
        /// - Remove isolated single pixels (noise)
        /// - Preserve thin lines and corners
        /// - Maintain connectivity of larger regions
        /// </summary>
        private static void PreserveConnectivity(bool[,] pixels, int w, int h)
        {
            if (w < 3 || h < 3) return;

            // Two passes: first mark pixels to remove, then apply
            var toRemove = new bool[w, h];

            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    if (!pixels[x, y]) continue;

                    // Count 8-connected neighbors
                    int neighbors = 0;
                    bool hasCorner = false;

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            if (pixels[x + dx, y + dy])
                            {
                                neighbors++;
                                if (Math.Abs(dx) == 1 && Math.Abs(dy) == 1)
                                    hasCorner = true;
                            }
                        }
                    }

                    // Never remove if part of a 2x2 block (interior or solid)
                    bool isIn2x2 = pixels[x - 1, y] && pixels[x, y - 1] && pixels[x - 1, y - 1] ||
                                   pixels[x + 1, y] && pixels[x, y - 1] && pixels[x + 1, y - 1] ||
                                   pixels[x - 1, y] && pixels[x, y + 1] && pixels[x - 1, y + 1] ||
                                   pixels[x + 1, y] && pixels[x, y + 1] && pixels[x + 1, y + 1];

                    if (isIn2x2) continue;

                    // Mark for removal if:
                    // - Isolated (no neighbors)
                    // - Single pixel with only diagonal neighbors (diamond artifact)
                    // But preserve thin lines: if 2+ cardinal neighbors, keep it
                    int cardinal = (pixels[x - 1, y] ? 1 : 0) + (pixels[x + 1, y] ? 1 : 0) +
                                   (pixels[x, y - 1] ? 1 : 0) + (pixels[x, y + 1] ? 1 : 0);

                    if (cardinal == 0)
                    {
                        // No direct neighbors - check if it's a valid corner or noise
                        if (neighbors <= 1 || (neighbors == 2 && hasCorner))
                            toRemove[x, y] = true;
                    }
                }
            }

            // Apply removals
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                    if (toRemove[x, y])
                        pixels[x, y] = false;
        }

        /// <summary>
        /// ScaleNx algorithm for pixel-perfect 2x/3x/4x upscaling.
        /// Detects edges and preserves hard edges while interpolating smooth areas.
        /// Produces significantly better quality than generic resampling at integer scales.
        /// </summary>
        private static bool[,] ScaleNx(bool[,] src, int sw, int sh, int scale)
        {
            int dw = sw * scale;
            int dh = sh * scale;
            var dst = new bool[dw, dh];

            // Get pixel with bounds checking
            bool GetPixel(int x, int y)
            {
                if (x < 0 || x >= sw || y < 0 || y >= sh) return false;
                return src[x, y];
            }

            for (int sy = 0; sy < sh; sy++)
            {
                for (int sx = 0; sx < sw; sx++)
                {
                    bool e = GetPixel(sx, sy);
                    bool b = GetPixel(sx, sy - 1);
                    bool d = GetPixel(sx - 1, sy);
                    bool f = GetPixel(sx + 1, sy);
                    bool h = GetPixel(sx, sy + 1);

                    // Map destination pixels based on scale
                    int dx = sx * scale;
                    int dy = sy * scale;

                    if (scale == 2)
                    {
                        // Scale2x: 2x2 output per input pixel
                        // Classic algorithm:
                        // 1 | 2    where 1,2,3,4 are determined by edge patterns
                        // 3 | 4
                        dst[dx, dy] = d == b && d != e && b != e ? d : e;
                        dst[dx + 1, dy] = b == f && b != e && f != e ? f : e;
                        dst[dx, dy + 1] = d == h && d != e && h != e ? d : e;
                        dst[dx + 1, dy + 1] = h == f && h != e && f != e ? f : e;
                    }
                    else if (scale == 3)
                    {
                        // Scale3x: 3x3 output per input pixel
                        // The 9 output pixels are arranged as:
                        // 1 2 3
                        // 4 5 6
                        // 7 8 9
                        // Center (5) always equals center pixel
                        // Corners and edges determined by patterns

                        // Center always matches original
                        dst[dx + 1, dy + 1] = e;

                        // Corners
                        dst[dx, dy] = (d == b && d != e && b != e) ? d : e;
                        dst[dx + 2, dy] = (b == f && b != e && f != e) ? f : e;
                        dst[dx, dy + 2] = (d == h && d != e && h != e) ? d : e;
                        dst[dx + 2, dy + 2] = (h == f && h != e && f != e) ? f : e;

                        // Edges with more sophisticated pattern matching
                        // Top edge (2)
                        if (b != e && ((b == d && b != f) || (b == f && b != d)))
                            dst[dx + 1, dy] = b;
                        else
                            dst[dx + 1, dy] = e;

                        // Left edge (4)
                        if (d != e && ((d == b && d != h) || (d == h && d != b)))
                            dst[dx, dy + 1] = d;
                        else
                            dst[dx, dy + 1] = e;

                        // Right edge (6)
                        if (f != e && ((f == b && f != h) || (f == h && f != b)))
                            dst[dx + 2, dy + 1] = f;
                        else
                            dst[dx + 2, dy + 1] = e;

                        // Bottom edge (8)
                        if (h != e && ((h == d && h != f) || (h == f && h != d)))
                            dst[dx + 1, dy + 2] = h;
                        else
                            dst[dx + 1, dy + 2] = e;
                    }
                    else // scale == 4
                    {
                        // For 4x, apply Scale2x then replicate each 2x2 sub-pixel to 4x4.
                        // Fix #3: Use local scalars instead of bool[2,2] — the old code
                        // allocated a new heap object per source pixel (262 144 allocations
                        // for a 512×512 selection), causing significant GC pressure during
                        // interactive transform drags.
                        bool t00 = d == b && d != e && b != e ? d : e;
                        bool t10 = b == f && b != e && f != e ? f : e;
                        bool t01 = d == h && d != e && h != e ? d : e;
                        bool t11 = h == f && h != e && f != e ? f : e;

                        // Scale each of the four Scale2x sub-pixels to a 2×2 quadrant
                        for (int y = 0; y < 4; y++)
                        {
                            bool row0 = y < 2 ? t00 : t01;
                            bool row1 = y < 2 ? t10 : t11;
                            dst[dx,     dy + y] = row0;
                            dst[dx + 1, dy + y] = row0;
                            dst[dx + 2, dy + y] = row1;
                            dst[dx + 3, dy + y] = row1;
                        }
                    }
                }
            }

            return dst;
        }

        private static void MirrorXInPlace(bool[,] pixels, int w, int h)
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w / 2; x++)
                {
                    int nx = w - 1 - x;
                    (pixels[x, y], pixels[nx, y]) = (pixels[nx, y], pixels[x, y]);
                }
            }
        }

        private static void MirrorYInPlace(bool[,] pixels, int w, int h)
        {
            for (int y = 0; y < h / 2; y++)
            {
                int ny = h - 1 - y;
                for (int x = 0; x < w; x++)
                    (pixels[x, y], pixels[x, ny]) = (pixels[x, ny], pixels[x, y]);
            }
        }

        // ── Private helpers ───────────────────────────────────────────────

        private void ResetState(bool notify)
        {
            if (HasActiveSelection && MinX >= 0 && MinY >= 0 && MaxX >= MinX && MaxY >= MinY)
            {
                _lastMask = Mask != null ? (bool[,])Mask.Clone() : null;
                _lastMinX = MinX;
                _lastMinY = MinY;
                _lastMaxX = MaxX;
                _lastMaxY = MaxY;
                _lastIsEllipse = _isEllipseSelection;
                _hasLastSelection = true;
            }

            HasActiveSelection = false;
            IsSelecting = false;
            IsFloating = false;
            IsDragging = false;
            IsTransforming = false;
            ActiveTransformHandle = TransformHandle.None;
            RotationAngle = 0;
            _originalFloatingPixels = null;
            _pristineFloatingPixels = null;
            _pristineFloatingW = _pristineFloatingH = 0;
            _originalMask = null;
            _pristineMask = null;
            _pristineMaskW = _pristineMaskH = 0;
            _activeTransformFlipX = false;
            _activeTransformFlipY = false;
            MinX = MaxX = MinY = MaxY = -1;
            Mask = null;
            FloatingPixels = null;
            FloatingX = FloatingY = FloatingWidth = FloatingHeight = 0;
            _lassoPoints.Clear();
            _isEllipseSelection = false;

            _baseMask = null;
            _baseMinX = _baseMaxX = _baseMinY = _baseMaxY = -1;
            _dragMinX = _dragMaxX = _dragMinY = _dragMaxY = -1;

            if (notify)
                Notify();
        }

        /// <summary>
        /// Ray-casting point-in-polygon test. Moved here from MainWindow.xaml.cs
        /// so it can be used by FinalizeSelection() and IsPixelInSelection().
        /// </summary>
        private bool IsPointInPolygon(int px, int py)
        {
            int count = _lassoPoints.Count;
            if (count == 0) return false;
            if (count == 1) return _lassoPoints[0].X == px && _lassoPoints[0].Y == py;
            if (count == 2)
                return _lassoPoints[0].Equals(new PixelPoint(px, py))
                    || _lassoPoints[1].Equals(new PixelPoint(px, py));

            // Fast exact-vertex check
            foreach (var v in _lassoPoints)
                if (v.X == px && v.Y == py) return true;

            // Offset slightly to avoid sitting exactly on an edge
            double tx = px + 0.01;
            double ty = py + 0.01;
            bool inside = false;

            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                double xi = _lassoPoints[i].X, yi = _lassoPoints[i].Y;
                double xj = _lassoPoints[j].X, yj = _lassoPoints[j].Y;

                if ((yi > ty) != (yj > ty) &&
                    tx < (xj - xi) * (ty - yi) / (yj - yi) + xi)
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }
}
