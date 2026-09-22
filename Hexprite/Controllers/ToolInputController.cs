using System;
using System.Collections.Generic;
using Hexprite.Core;
using Hexprite.Rendering;
using Hexprite.Services;
using Hexprite.ViewModels;

namespace Hexprite.Controllers
{
    /// <summary>
    /// Owns the tool input state machine: down/move/up dispatch, shape constraint
    /// logic, drawing-in-progress tracking, and preview orchestration.
    /// Extracted from MainViewModel to separate input handling from ViewModel state.
    /// </summary>
    public class ToolInputController : IToolInputController
    {
        private readonly MainViewModel _vm;
        private readonly IDrawingService _drawingService;
        private readonly BitmapPreviewRenderer _preview;
        private SelectionClipAdapter? _currentClip;

        // ── Internal drawing tracking ─────────────────────────────────────
        private const int NoPosition = int.MinValue;
        private int _lineStartX = NoPosition;
        private int _lineStartY = NoPosition;
        private int _lineCurrentX = NoPosition;
        private int _lineCurrentY = NoPosition;
        private bool _lineDrawState;
        private int _lastClickedX = NoPosition;
        private int _lastClickedY = NoPosition;
        private bool _strokeHasUndoState;
        private bool _isStrokeSessionActive;
        private bool _pendingTextUpdateDuringDrag;
        private bool _lastShiftDown;
        private bool _lastAltDown;
        private readonly List<(int x, int y)> _pixelPerfectStrokePath = [];
        private readonly Dictionary<int, bool> _pixelPerfectOriginalStates = [];
        private List<(int dx, int dy)>? _cachedStampOffsets;
        private int _cachedBrushSize = -1;
        private BrushShape _cachedBrushShape = (BrushShape)(-1);
        private int _cachedBrushAngle = -1;

        // ── Shape drawing state ───────────────────────────────────────────
        // Replaces the five individual IsDrawingXxx booleans with a single
        // flag + active tool, eliminating massive duplication in handlers.
        private bool _isDrawingShape;
        private ToolMode _activeShapeTool;

        // ── Move tool state ───────────────────────────────────────────────
        private bool _isMoving;
        private int _moveStartX;
        private int _moveStartY;
        private int _moveOffsetX;
        private int _moveOffsetY;
        private int _moveOriginalFloatingX;
        private int _moveOriginalFloatingY;
        private Core.IPixelBuffer? _moveOverflowSnapshot;
        // True when the active move drag was started without an explicit selection
        // (Aseprite-style "move whole layer"). On mouse-up such a drag commits and
        // auto-deselects so no residual full-canvas selection lingers.
        private bool _isLayerMove;

        // Deferred-start fields: a Move-tool mouse-down does NOT immediately mutate
        // state or push undo. We wait for either the first mouse-move (drag) to
        // commit to a real lift, or mouse-up (click without drag) to handle the
        // click-only semantics. This avoids spurious undo entries on plain clicks.
        private enum MovePendingKind { None, SelectionDrag, LayerMove }
        private MovePendingKind _movePending = MovePendingKind.None;
        private int _movePendingX;
        private int _movePendingY;

        // Computed properties preserve the existing public API
        public bool IsDrawingLine => _isDrawingShape && _activeShapeTool == ToolMode.Line;
        public bool IsDrawingRectangle => _isDrawingShape && _activeShapeTool == ToolMode.Rectangle;
        public bool IsDrawingEllipse => _isDrawingShape && _activeShapeTool == ToolMode.Ellipse;
        public bool IsDrawingFilledRectangle => _isDrawingShape && _activeShapeTool == ToolMode.FilledRectangle;
        public bool IsDrawingFilledEllipse => _isDrawingShape && _activeShapeTool == ToolMode.FilledEllipse;
        public bool IsDrawingGradient => _isDrawingShape && _activeShapeTool == ToolMode.Gradient;

        // Move tool state
        public bool IsMoving => _isMoving;

        // ── Shape tool dispatch tables ────────────────────────────────────
        // Maps each shape ToolMode to its preview callback and commit callback,
        // collapsing 5 copy-pasted branches into a single generic path.

        private delegate void PreviewAction(int x0, int y0, int x1, int y1, bool state, int brushSize, BrushShape shape, int angleDeg);
        private delegate void CommitAction(SpriteState s, int x0, int y0, int x1, int y1, bool state, int brushSize, BrushShape shape, int angleDeg);

        private readonly Dictionary<ToolMode, PreviewAction> _shapePreviewMap;
        private readonly Dictionary<ToolMode, CommitAction> _shapeCommitMap;

        // The set of tool modes that are handled as "shapes" (drag-to-draw)
        private static readonly HashSet<ToolMode> ShapeTools =
        [
            ToolMode.Line,
            ToolMode.Rectangle,
            ToolMode.Ellipse,
            ToolMode.FilledRectangle,
            ToolMode.FilledEllipse,
            ToolMode.Gradient,
        ];

        // ── Symmetry support ──────────────────────────────────────────────

        private IEnumerable<(int x, int y, bool isMirroredX, bool isMirroredY)> GetSymmetryPoints(int originalX, int originalY)
        {
            bool horz = _vm.IsSymmetryHorizontalEnabled;
            bool vert = _vm.IsSymmetryVerticalEnabled;

            yield return (originalX, originalY, false, false);

            int mirroredX = vert ? (int)Math.Round(2 * _vm.SymmetryAxisX - originalX - 1, MidpointRounding.AwayFromZero) : originalX;
            int mirroredY = horz ? (int)Math.Round(2 * _vm.SymmetryAxisY - originalY - 1, MidpointRounding.AwayFromZero) : originalY;
            
            bool diffX = vert && (mirroredX != originalX);
            bool diffY = horz && (mirroredY != originalY);

            if (diffX) yield return (mirroredX, originalY, true, false);
            if (diffY) yield return (originalX, mirroredY, false, true);
            if (diffX && diffY) yield return (mirroredX, mirroredY, true, true);
        }

        public ToolInputController(MainViewModel vm, IDrawingService drawingService, BitmapPreviewRenderer preview)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            _drawingService = drawingService ?? throw new ArgumentNullException(nameof(drawingService));
            _preview = preview ?? throw new ArgumentNullException(nameof(preview));

            // Wire up shape tool dispatch tables
            _shapePreviewMap = new Dictionary<ToolMode, PreviewAction>
            {
                { ToolMode.Line,            _preview.PreviewLine },
                { ToolMode.Rectangle,       _preview.PreviewRectangle },
                { ToolMode.Ellipse,         _preview.PreviewEllipse },
                { ToolMode.FilledRectangle, _preview.PreviewFilledRectangle },
                { ToolMode.FilledEllipse,   _preview.PreviewFilledEllipse },
                { ToolMode.Gradient,        (x0, y0, x1, y1, _, _, _, _) => _preview.PreviewDitherGradient(x0, y0, x1, y1) },
            };

            _shapeCommitMap = new Dictionary<ToolMode, CommitAction>
            {
                { ToolMode.Line,            (s, x0, y0, x1, y1, st, bs, sh, ang) => _drawingService.DrawLine(s, x0, y0, x1, y1, st, bs, sh, ang, _currentClip) },
                { ToolMode.Rectangle,       (s, x0, y0, x1, y1, st, bs, sh, ang) => _drawingService.DrawRectangle(s, x0, y0, x1, y1, st, bs, sh, ang, _currentClip) },
                { ToolMode.Ellipse,         (s, x0, y0, x1, y1, st, bs, sh, ang) => _drawingService.DrawEllipse(s, x0, y0, x1, y1, st, bs, sh, ang, _currentClip) },
                { ToolMode.FilledRectangle, (s, x0, y0, x1, y1, st, bs, sh, ang) => _drawingService.DrawFilledRectangle(s, x0, y0, x1, y1, st, bs, sh, ang, _currentClip) },
                { ToolMode.FilledEllipse,   (s, x0, y0, x1, y1, st, bs, sh, ang) => _drawingService.DrawFilledEllipse(s, x0, y0, x1, y1, st, bs, sh, ang, _currentClip) },
                { ToolMode.Gradient,        (s, x0, y0, x1, y1, st, bs, sh, ang) => _drawingService.DrawDitherGradient(s, x0, y0, x1, y1, st, _currentClip) },
            };
        }

        /// <summary>
        /// Main entry point for all non-selection tool input from the View.
        /// Uses typed enums instead of the previous magic strings and bool? tri-state.
        /// </summary>
        public void ProcessToolInput(int x, int y, ToolAction action, DrawMode mode, bool isShiftDown, bool isAltDown = false)
        {
            if (_vm.IsProcessing) return;
            if (_vm.CurrentTool == ToolMode.Marquee || _vm.CurrentTool == ToolMode.Lasso || _vm.CurrentTool == ToolMode.EllipticalMarquee) return;

            // Clip drawing to the active selection whenever one exists, including
            // when it is floating. The prior assumption ("floating means already
            // committed") was incorrect — undo/redo or interrupted tool switches can
            // leave a floating selection active while a drawing tool is used.
            // SelectionClipAdapter.IsPixelInSelection already handles the floating
            // case by testing against FloatingPixels bounds.
            var sel = _vm.SelectionService;
            bool hasClip = sel.HasActiveSelection;
            _currentClip = hasClip ? new SelectionClipAdapter(sel) : null;

            if (action == ToolAction.Down)
            {
                _strokeHasUndoState = false;
            }

            switch (action)
            {
                case ToolAction.Down:
                    HandleToolDown(x, y, mode, isShiftDown);
                    break;
                case ToolAction.Move:
                    HandleToolMove(x, y, mode, isShiftDown, isAltDown);
                    break;
                case ToolAction.Up:
                    HandleToolUp(isShiftDown, isAltDown);
                    break;
            }
        }

        /// <summary>
        /// Cancels any in-progress shape drawing and resets tracking state.
        /// Called by the View when switching tools to prevent stale draw flags.
        /// </summary>
        public void CancelInProgressDrawing()
        {
            if (_isDrawingShape)
            {
                _isDrawingShape = false;
                ResetLineTracking();
                _vm.RedrawGridFromMemory();  // remove any shape preview
            }
            
            if (_isMoving)
            {
                var selection = _vm.SelectionService;
                if (selection.IsFloating)
                {
                    selection.EndDrag();
                    // Restore to original position first.
                    selection.MoveFloatingTo(_moveOriginalFloatingX, _moveOriginalFloatingY);
                    
                    if (_isLayerMove)
                    {
                        // Restore the overflow snapshot if we have one (no shift needed — cancel = no change)
                        if (_moveOverflowSnapshot is Core.OverflowPixelBuffer restoredOvf)
                        {
                            var state = _vm.SpriteState;
                            var frame = state.Frames[state.ActiveFrameIndex];
                            frame.LayerPixels[state.ActiveLayerIndex] = restoredOvf;
                            state.Pixels = restoredOvf.GetMonochromeData();
                            _moveOverflowSnapshot = null;
                            selection.Cancel();
                        }
                        else
                        {
                            // Standard layer: commit pixels back to layer before canceling selection to prevent data loss
                            selection.CommitSelection(_vm.SpriteState);
                            selection.Cancel();
                        }
                    }
                        
                    _vm.RedrawGridFromMemory();
                    _vm.MarkCodeStale();
                }
                _isMoving = false;
                _isLayerMove = false;
            }

            _movePending = MovePendingKind.None;

            if (_isStrokeSessionActive)
            {
                _isStrokeSessionActive = false;
                _vm.EndStrokeRenderSession();
            }

            _lastClickedX = NoPosition;
            _lastClickedY = NoPosition;

            ClearPixelPerfectStrokeState();
            _pendingTextUpdateDuringDrag = false;
        }

        private void EnsureUndoState(int x, int y, int brushSize = 1, bool applySymmetry = true)
        {
            if (_strokeHasUndoState) return;
            
            var state = _vm.SpriteState;
            var domain = DrawingService.GetPixelDomain(state);
            int r = brushSize / 2;
            int offset = brushSize % 2 == 0 ? 0 : 1;

            bool Intersects(int cx, int cy)
            {
                int minX = cx - r;
                int maxX = cx + r + offset;
                int minY = cy - r;
                int maxY = cy + r + offset;
                return maxX > domain.minX && minX <= domain.maxX && maxY > domain.minY && minY <= domain.maxY;
            }

            bool touches = Intersects(x, y);
            if (!touches && applySymmetry && (_vm.IsSymmetryHorizontalEnabled || _vm.IsSymmetryVerticalEnabled))
            {
                foreach (var (sx, sy, _, _) in GetSymmetryPoints(x, y))
                {
                    if (Intersects(sx, sy)) { touches = true; break; }
                }
            }

            if (touches)
            {
                _vm.SaveStateForUndo();
                _strokeHasUndoState = true;
            }
        }

        private void EnsureUndoStateForBounds(int x0, int y0, int x1, int y1, int brushSize = 1, bool applySymmetry = true)
        {
            if (_strokeHasUndoState) return;
            var state = _vm.SpriteState;
            var domain = DrawingService.GetPixelDomain(state);
            int r = brushSize / 2;
            int offset = brushSize % 2 == 0 ? 0 : 1;

            bool Intersects(int cx0, int cy0, int cx1, int cy1)
            {
                int minX = Math.Min(cx0, cx1) - r;
                int maxX = Math.Max(cx0, cx1) + r + offset;
                int minY = Math.Min(cy0, cy1) - r;
                int maxY = Math.Max(cy0, cy1) + r + offset;
                return maxX > domain.minX && minX <= domain.maxX && maxY > domain.minY && minY <= domain.maxY;
            }

            bool touches = Intersects(x0, y0, x1, y1);
            if (!touches && applySymmetry && (_vm.IsSymmetryHorizontalEnabled || _vm.IsSymmetryVerticalEnabled))
            {
                // Approximation: if endpoints or any symmetry of endpoints intersect, consider it touching
                // We just rely on basic endpoints for symmetry bounding
                foreach (var (sx0, sy0, _, _) in GetSymmetryPoints(x0, y0))
                {
                    if (Intersects(sx0, sy0, sx0, sy0)) touches = true;
                }
                foreach (var (sx1, sy1, _, _) in GetSymmetryPoints(x1, y1))
                {
                    if (Intersects(sx1, sy1, sx1, sy1)) touches = true;
                }
            }

            if (touches)
            {
                _vm.SaveStateForUndo();
                _strokeHasUndoState = true;
            }
        }

        // ── Private: tool dispatch ────────────────────────────────────────

        private void HandleToolDown(int x, int y, DrawMode mode, bool isShiftDown)
        {
            if (!_vm.CanModifyActiveLayer)
            {
                _vm.ShowStatus("Cannot modify: layer is locked, hidden, or multiple layers selected.", 3000);
                return;
            }

            bool newState = mode == DrawMode.Draw;

            // ── Shape tools (Line, Rectangle, Ellipse, FilledRectangle, FilledEllipse) ──
            if (ShapeTools.Contains(_vm.CurrentTool))
            {
                _isDrawingShape = true;
                _activeShapeTool = _vm.CurrentTool;
                _lineStartX = x;
                _lineStartY = y;
                _lineCurrentX = x;
                _lineCurrentY = y;
                _lineDrawState = newState;
                _shapePreviewMap[_activeShapeTool](x, y, x, y, newState, _vm.BrushSize, _vm.BrushShape, _vm.BrushAngle);
                return;
            }

            switch (_vm.CurrentTool)
            {
                case ToolMode.Text:
                    _vm.StartTextEditing(x, y);
                    return;

                case ToolMode.Fill:
                    var state = _vm.SpriteState;
                    if (x >= 0 && x < state.Width && y >= 0 && y < state.Height && !state.Pixels[y * state.Width + x].Equals(newState))
                    {
                        EnsureUndoState(x, y);
                        _vm.IsProcessing = true;
                        _vm.StatusMessage = "Filling...";
                        
                        // Extract symmetry points before the background thread runs
                        var pointsToFill = GetSymmetryPoints(x, y).ToList();
                        bool isContiguous = state.ColorMode == ColorMode.Monochrome || _vm.IsContiguousFillEnabled;

                        System.Threading.Tasks.Task.Run(() =>
                        {
                            bool anyFillApplied = false;
                            try
                            {
                                foreach (var (sx, sy, _, _) in pointsToFill)
                                {
                                    if (sx >= 0 && sx < state.Width && sy >= 0 && sy < state.Height)
                                    {
                                        bool isClipped = _currentClip != null && !_currentClip.IsPixelInClip(sx, sy);
                                        if (!isClipped && state.Pixels[sy * state.Width + sx] != newState)
                                        {
                                            if (_drawingService.ApplyFloodFill(state, sx, sy, newState, isContiguous, _currentClip))
                                            {
                                                anyFillApplied = true;
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Flood fill error: {ex.Message}");
                            }
                            finally
                            {
                                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                                if (dispatcher != null)
                                {
                                    dispatcher.InvokeAsync(() =>
                                    {
                                        if (anyFillApplied)
                                        {
                                            _vm.RedrawGridFromMemory();
                                            _vm.MarkCodeStale();
                                        }
                                        _vm.IsProcessing = false;
                                        _vm.StatusMessage = "";
                                    });
                                }
                                else
                                {
                                    if (anyFillApplied)
                                    {
                                        _vm.RedrawGridFromMemory();
                                        _vm.MarkCodeStale();
                                    }
                                    _vm.IsProcessing = false;
                                    _vm.StatusMessage = "";
                                }
                            }
                        });
                    }
                    break;

                case ToolMode.Move:
                    HandleMoveDown(x, y);
                    break;

                case ToolMode.Pencil:
                case ToolMode.Eraser:
                case ToolMode.Dither:
                    _vm.BeginStrokeRenderSession();
                    _isStrokeSessionActive = true;
                    ClearPixelPerfectStrokeState();
                    if (isShiftDown && _lastClickedX != NoPosition)
                    {
                        EnsureUndoStateForBounds(_lastClickedX, _lastClickedY, x, y, _vm.BrushSize);
                        if (ShouldUsePixelPerfect())
                        {
                            ApplyPixelPerfectSegment(_lastClickedX, _lastClickedY, x, y, newState, skipFirstPoint: false);
                        }
                        else
                        {
                            if (_vm.CurrentTool == ToolMode.Dither)
                                _drawingService.DrawLineDithered(_vm.SpriteState, _lastClickedX, _lastClickedY, x, y, newState, _vm.BrushSize, _vm.DitherPattern, _vm.BrushShape, _vm.BrushAngle, _currentClip);
                            else
                                _drawingService.DrawLine(_vm.SpriteState, _lastClickedX, _lastClickedY, x, y, newState, _vm.BrushSize, _vm.BrushShape, _vm.BrushAngle, _currentClip);

                            var (dirtyMinX, dirtyMinY, dirtyMaxX, dirtyMaxY) = GetSymmetricDirtyRegion(x, y, _lastClickedX, _lastClickedY);
                            _vm.RedrawRegion(dirtyMinX, dirtyMinY, dirtyMaxX, dirtyMaxY, updatePreviewSimulation: false);
                        }

                        _lastClickedX = x;
                        _lastClickedY = y;
                        _pendingTextUpdateDuringDrag = true;
                    }
                    else
                    {
                        EnsureUndoState(x, y, _vm.BrushSize);
                        if (ShouldUsePixelPerfect())
                        {
                            PlotPixelPerfectPoint(x, y, newState);
                        }
                        else
                        {
                            // Draw at primary position and all symmetry positions
                            var spriteState = _vm.SpriteState;
                            foreach (var (sx, sy, isMirroredX, isMirroredY) in GetSymmetryPoints(x, y))
                            {
                                int angle = _vm.BrushAngle;
                                if (isMirroredX || isMirroredY)
                                {
                                    angle = MirrorBrushAngle(angle, isMirroredX, isMirroredY);
                                }
                                if (_vm.CurrentTool == ToolMode.Dither)
                                    _drawingService.DrawBrushStampDithered(spriteState, sx, sy, _vm.BrushSize, newState, _vm.DitherPattern, _vm.BrushShape, angle, _currentClip);
                                else
                                    _drawingService.DrawBrushStamp(spriteState, sx, sy, _vm.BrushSize, newState, _vm.BrushShape, angle, _currentClip);
                            }
                        }
                        _lastClickedX = x;
                        _lastClickedY = y;

                        if (!ShouldUsePixelPerfect())
                        {
                            var (dirtyMinX, dirtyMinY, dirtyMaxX, dirtyMaxY) = GetSymmetricDirtyRegion(x, y);
                            _vm.RedrawRegion(dirtyMinX, dirtyMinY, dirtyMaxX, dirtyMaxY, updatePreviewSimulation: false);
                        }
                        _pendingTextUpdateDuringDrag = true;
                    }
                    break;
            }
        }

        private void HandleToolMove(int x, int y, DrawMode mode, bool isShiftDown, bool isAltDown)
        {
            using var perfScope = _vm.BeginMovePerfScope();
            if (!_vm.CanModifyActiveLayer)
                return;

            bool newState = mode == DrawMode.Draw;

            // ── Shape tool move (generic for all shape tools) ──
            if (_isDrawingShape && _vm.CurrentTool == _activeShapeTool)
            {
                if (_lineCurrentX != x || _lineCurrentY != y || _lastShiftDown != isShiftDown || _lastAltDown != isAltDown)
                {
                    _lineCurrentX = x;
                    _lineCurrentY = y;
                    _lastShiftDown = isShiftDown;
                    _lastAltDown = isAltDown;
                    var (x0, y0, x1, y1) = GetConstrainedShapeBounds(_lineStartX, _lineStartY, _lineCurrentX, _lineCurrentY, _activeShapeTool, isShiftDown, isAltDown);
                    _shapePreviewMap[_activeShapeTool](x0, y0, x1, y1, _lineDrawState, _vm.BrushSize, _vm.BrushShape, _vm.BrushAngle);
                }
                return;
            }

            // ── Move tool drag ──
            if (_vm.CurrentTool == ToolMode.Move && (_isMoving || _movePending != MovePendingKind.None))
            {
                HandleMoveMove(x, y);
                return;
            }

            // ── Pencil/Eraser drag ──
            if ((_vm.CurrentTool == ToolMode.Pencil || _vm.CurrentTool == ToolMode.Eraser || _vm.CurrentTool == ToolMode.Dither) && mode != DrawMode.None)
            {
                if (_lastClickedX != NoPosition && (_lastClickedX != x || _lastClickedY != y))
                {
                    int prevX = _lastClickedX, prevY = _lastClickedY;

                    // Continuous pencil drag: draw line segment but don't push undo
                    // (the undo entry was already pushed on Down)
                    EnsureUndoStateForBounds(prevX, prevY, x, y, _vm.BrushSize);
                    if (ShouldUsePixelPerfect())
                    {
                        ApplyPixelPerfectSegment(prevX, prevY, x, y, newState, skipFirstPoint: true);
                    }
                    else
                    {
                        var state = _vm.SpriteState;
                        bool horz = _vm.IsSymmetryHorizontalEnabled;
                        bool vert = _vm.IsSymmetryVerticalEnabled;

                        void DrawSymLine(int cx, int cy, int px, int py, bool mx, bool my)
                        {
                            int ang = _vm.BrushAngle;
                            if (mx || my) ang = MirrorBrushAngle(ang, mx, my);
                            if (_vm.CurrentTool == ToolMode.Dither)
                                _drawingService.DrawLineDithered(state, px, py, cx, cy, newState, _vm.BrushSize, _vm.DitherPattern, _vm.BrushShape, ang, _currentClip);
                            else
                                _drawingService.DrawLine(state, px, py, cx, cy, newState, _vm.BrushSize, _vm.BrushShape, ang, _currentClip);
                        }

                        DrawSymLine(x, y, prevX, prevY, false, false);
                        
                        int mx = vert ? (int)Math.Round(2 * _vm.SymmetryAxisX - x - 1, MidpointRounding.AwayFromZero) : x;
                        int mpx = vert ? (int)Math.Round(2 * _vm.SymmetryAxisX - prevX - 1, MidpointRounding.AwayFromZero) : prevX;
                        int my = horz ? (int)Math.Round(2 * _vm.SymmetryAxisY - y - 1, MidpointRounding.AwayFromZero) : y;
                        int mpy = horz ? (int)Math.Round(2 * _vm.SymmetryAxisY - prevY - 1, MidpointRounding.AwayFromZero) : prevY;
                        
                        bool diffV = vert && (mx != x || mpx != prevX);
                        bool diffH = horz && (my != y || mpy != prevY);
                        
                        if (diffV) DrawSymLine(mx, y, mpx, prevY, true, false);
                        if (diffH) DrawSymLine(x, my, prevX, mpy, false, true);
                        if (diffV && diffH) DrawSymLine(mx, my, mpx, mpy, true, true);
                    }
                    _lastClickedX = x;
                    _lastClickedY = y;
                    _pendingTextUpdateDuringDrag = true;

                    if (!ShouldUsePixelPerfect())
                    {
                        // Partial redraw: only update the stroke segment footprint plus
                        // a conservative 1px safety margin for brush edge rounding.
                        var (dirtyMinX, dirtyMinY, dirtyMaxX, dirtyMaxY) = GetSymmetricDirtyRegion(x, y, prevX, prevY);
                        _vm.RedrawRegion(dirtyMinX, dirtyMinY, dirtyMaxX, dirtyMaxY, updatePreviewSimulation: false);
                    }
                }
            }
        }

        private void HandleToolUp(bool isShiftDown, bool isAltDown)
        {
            // ── Shape tool commit (generic for all shape tools) ──
            if (_isDrawingShape)
            {
                var tool = _activeShapeTool;
                _isDrawingShape = false;

                if (_lineStartX != NoPosition && _shapeCommitMap.TryGetValue(tool, out var commitAction))
                {
                    var (x0, y0, x1, y1) = GetConstrainedShapeBounds(_lineStartX, _lineStartY, _lineCurrentX, _lineCurrentY, tool, isShiftDown, isAltDown);
                    if (tool == ToolMode.Gradient)
                    {
                        if (!_strokeHasUndoState)
                        {
                            _vm.SaveStateForUndo();
                            _strokeHasUndoState = true;
                        }
                    }
                    else
                    {
                        EnsureUndoStateForBounds(x0, y0, x1, y1, _vm.BrushSize);
                    }
                    
                    bool horz = _vm.IsSymmetryHorizontalEnabled;
                    bool vert = _vm.IsSymmetryVerticalEnabled;

                    commitAction(_vm.SpriteState, x0, y0, x1, y1, _lineDrawState, _vm.BrushSize, _vm.BrushShape, _vm.BrushAngle);

                    int mx0 = vert ? (int)Math.Round(2 * _vm.SymmetryAxisX - x0 - 1, MidpointRounding.AwayFromZero) : x0;
                    int mx1 = vert ? (int)Math.Round(2 * _vm.SymmetryAxisX - x1 - 1, MidpointRounding.AwayFromZero) : x1;
                    int my0 = horz ? (int)Math.Round(2 * _vm.SymmetryAxisY - y0 - 1, MidpointRounding.AwayFromZero) : y0;
                    int my1 = horz ? (int)Math.Round(2 * _vm.SymmetryAxisY - y1 - 1, MidpointRounding.AwayFromZero) : y1;

                    bool diffV = vert && (mx0 != x0 || mx1 != x1);
                    bool diffH = horz && (my0 != y0 || my1 != y1);

                    if (diffV) commitAction(_vm.SpriteState, mx0, y0, mx1, y1, _lineDrawState, _vm.BrushSize, _vm.BrushShape, _vm.BrushAngle);
                    if (diffH) commitAction(_vm.SpriteState, x0, my0, x1, my1, _lineDrawState, _vm.BrushSize, _vm.BrushShape, _vm.BrushAngle);
                    if (diffV && diffH) commitAction(_vm.SpriteState, mx0, my0, mx1, my1, _lineDrawState, _vm.BrushSize, _vm.BrushShape, _vm.BrushAngle);
                    
                    _lastClickedX = x1;
                    _lastClickedY = y1;
                    _vm.RedrawGridFromMemory();
                }

                ResetLineTracking();
                _vm.MarkCodeStale();
                return;
            }

            // ── Move tool commit ──
            if (_vm.CurrentTool == ToolMode.Move && (_isMoving || _movePending != MovePendingKind.None))
            {
                HandleMoveUp();
                return;
            }

            // ── Pencil/Eraser deferred text update ──
            if (_vm.CurrentTool == ToolMode.Pencil || _vm.CurrentTool == ToolMode.Eraser || _vm.CurrentTool == ToolMode.Dither)
            {
                if (_pendingTextUpdateDuringDrag)
                {
                    _pendingTextUpdateDuringDrag = false;
                    ClearPixelPerfectStrokeState();
                    _vm.MarkCodeStale();
                    _vm.UpdatePreviewSimulation();
                }

                if (_isStrokeSessionActive)
                {
                    _isStrokeSessionActive = false;
                    _vm.EndStrokeRenderSession();
                }
            }
        }

        // ── Shape constraint logic ────────────────────────────────────────

        internal static (int x0, int y0, int x1, int y1) GetConstrainedShapeBounds(int startX, int startY, int currentX, int currentY, ToolMode tool, bool isShift, bool isAlt)
        {
            int targetX = currentX;
            int targetY = currentY;

            if (tool == ToolMode.Line || tool == ToolMode.Gradient)
            {
                if (isShift)
                {
                    double angle = Math.Atan2(targetY - startY, targetX - startX);
                    angle = Math.Round(angle / (Math.PI / 12.0), MidpointRounding.AwayFromZero) * (Math.PI / 12.0);
                    double dist = Math.Sqrt(Math.Pow(targetX - startX, 2) + Math.Pow(targetY - startY, 2));
                    targetX = startX + (int)Math.Round(Math.Cos(angle) * dist, MidpointRounding.AwayFromZero);
                    targetY = startY + (int)Math.Round(Math.Sin(angle) * dist, MidpointRounding.AwayFromZero);
                }

                int x0 = startX;
                int y0 = startY;
                if (isAlt)
                {
                    x0 = 2 * startX - targetX;
                    y0 = 2 * startY - targetY;
                }
                return (x0, y0, targetX, targetY);
            }
            else
            {
                if (isShift)
                {
                    int dx = currentX - startX;
                    int dy = currentY - startY;
                    int side = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    targetX = startX + (dx >= 0 ? side : -side);
                    targetY = startY + (dy >= 0 ? side : -side);
                }

                int x0 = startX;
                int y0 = startY;
                if (isAlt)
                {
                    x0 = 2 * startX - targetX;
                    y0 = 2 * startY - targetY;
                }
                return (x0, y0, targetX, targetY);
            }
        }

        private void ResetLineTracking()
        {
            _lineStartX = NoPosition;
            _lineStartY = NoPosition;
            _lineCurrentX = NoPosition;
            _lineCurrentY = NoPosition;
        }

        private int ComputeDirtyMargin()
        {
            if (_vm.BrushShape == BrushShape.Circle || _vm.BrushAngle % 90 == 0)
            {
                return ((_vm.BrushSize + 1) / 2) + 1;
            }
            double maxRadius = _vm.BrushSize * 0.70710678;
            return (int)Math.Ceiling(maxRadius) + 2;
        }

        /// <summary>
        /// Calculates the total dirty region encompassing all symmetry points
        /// for the given coordinates, with brush margin padding.
        /// </summary>
        private (int minX, int minY, int maxX, int maxY) GetSymmetricDirtyRegion(int x, int y, int prevX = NoPosition, int prevY = NoPosition)
        {
            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue;
            int margin = ComputeDirtyMargin();

            void ExpandBounds(int px, int py)
            {
                if (px == NoPosition || py == NoPosition) return;
                foreach (var (sx, sy, _, _) in GetSymmetryPoints(px, py))
                {
                    minX = Math.Min(minX, sx - margin);
                    minY = Math.Min(minY, sy - margin);
                    maxX = Math.Max(maxX, sx + margin);
                    maxY = Math.Max(maxY, sy + margin);
                }
            }

            ExpandBounds(x, y);
            ExpandBounds(prevX, prevY);
            if (minX > maxX || minY > maxY) return (0, 0, 0, 0);
            int w = _vm.SpriteState.Width;
            int h = _vm.SpriteState.Height;
            return (
                Math.Clamp(minX, 0, w - 1),
                Math.Clamp(minY, 0, h - 1),
                Math.Clamp(maxX, 0, w - 1),
                Math.Clamp(maxY, 0, h - 1)
            );
        }

        private bool ShouldUsePixelPerfect()
            => (_vm.CurrentTool == ToolMode.Pencil || _vm.CurrentTool == ToolMode.Eraser || _vm.CurrentTool == ToolMode.Dither)
               && _vm.BrushSize == 1
               && _vm.IsPixelPerfectEnabled;

        private static int MirrorBrushAngle(int angle, bool isMirroredX, bool isMirroredY)
        {
            angle = ((angle % 360) + 360) % 360;
            if (isMirroredX && isMirroredY) return (angle + 180) % 360;
            if (isMirroredX) return (180 - angle + 360) % 360;
            if (isMirroredY) return (360 - angle) % 360;
            return angle;
        }

        private void ClearPixelPerfectStrokeState()
        {
            _pixelPerfectStrokePath.Clear();
            _pixelPerfectOriginalStates.Clear();
            _cachedStampOffsets = null;
        }

        private List<(int dx, int dy)> GetStampOffsets()
        {
            if (_cachedStampOffsets == null || 
                _cachedBrushSize != _vm.BrushSize || 
                _cachedBrushShape != _vm.BrushShape || 
                _cachedBrushAngle != _vm.BrushAngle)
            {
                _cachedBrushSize = _vm.BrushSize;
                _cachedBrushShape = _vm.BrushShape;
                _cachedBrushAngle = _vm.BrushAngle;
                _cachedStampOffsets = DrawingService.ComputeStampOffsets(_cachedBrushSize, _cachedBrushShape, _cachedBrushAngle);
            }
            return _cachedStampOffsets;
        }

        private void ApplyPixelPerfectSegment(int x0, int y0, int x1, int y1, bool newState, bool skipFirstPoint)
        {
            bool first = true;
            foreach (var (x, y) in EnumerateLinePoints(x0, y0, x1, y1))
            {
                if (skipFirstPoint && first)
                {
                    first = false;
                    continue;
                }

                first = false;
                PlotPixelPerfectPoint(x, y, newState);
            }
        }

        private void PlotPixelPerfectPoint(int x, int y, bool newState)
        {
            var state = _vm.SpriteState;

            if (_pixelPerfectStrokePath.Count > 0 && _pixelPerfectStrokePath[^1] == (x, y))
                return; // Already plotted this exact point

            _pixelPerfectStrokePath.Add((x, y));

            // Record original states of all pixels that each symmetry stamp footprint will touch
            foreach (var (sx, sy, isMirroredX, isMirroredY) in GetSymmetryPoints(x, y))
            {
                int angle = _vm.BrushAngle;
                if (isMirroredX || isMirroredY)
                    angle = MirrorBrushAngle(angle, isMirroredX, isMirroredY);

                var symOffsets = DrawingService.GetStampOffsetsArray(_vm.BrushSize, _vm.BrushShape, angle);
                foreach (var (dx, dy) in symOffsets)
                {
                    int px = sx + dx;
                    int py = sy + dy;
                    if (px >= 0 && px < state.Width && py >= 0 && py < state.Height)
                    {
                        int idx = py * state.Width + px;
                        if (!_pixelPerfectOriginalStates.ContainsKey(idx))
                            _pixelPerfectOriginalStates[idx] = state.Pixels[idx];
                    }
                }

                if (_vm.CurrentTool == ToolMode.Dither)
                    _drawingService.DrawBrushStampDithered(state, sx, sy, _vm.BrushSize, newState, _vm.DitherPattern, _vm.BrushShape, angle, _currentClip);
                else
                    _drawingService.DrawBrushStamp(state, sx, sy, _vm.BrushSize, newState, _vm.BrushShape, angle, _currentClip);
            }

            var (dirtyMinX, dirtyMinY, dirtyMaxX, dirtyMaxY) = GetSymmetricDirtyRegion(x, y);
            _vm.RedrawRegion(dirtyMinX, dirtyMinY, dirtyMaxX, dirtyMaxY, updatePreviewSimulation: false);
            TryRemovePixelPerfectCorner(newState);
        }

        private void TryRemovePixelPerfectCorner(bool newState)
        {
            if (_pixelPerfectStrokePath.Count < 3)
                return;

            var (x, y) = _pixelPerfectStrokePath[^3];
            var b = _pixelPerfectStrokePath[^2];
            var c = _pixelPerfectStrokePath[^1];

            int abDx = b.x - x;
            int abDy = b.y - y;
            int bcDx = c.x - b.x;
            int bcDy = c.y - b.y;
            int acDx = c.x - x;
            int acDy = c.y - y;

            bool isAxisAlignedSteps =
                Math.Abs(abDx) + Math.Abs(abDy) == 1 &&
                Math.Abs(bcDx) + Math.Abs(bcDy) == 1;
            bool isRightAngleTurn = (abDx != bcDx || abDy != bcDy) && ((abDx == 0 && bcDx != 0) || (abDy == 0 && bcDy != 0));
            bool isStaircaseCorner = Math.Abs(acDx) == 1 && Math.Abs(acDy) == 1;
            if (!isAxisAlignedSteps || !isRightAngleTurn || !isStaircaseCorner)
                return;

            // Remove the corner point from the path
            _pixelPerfectStrokePath.RemoveAt(_pixelPerfectStrokePath.Count - 2);

            var state = _vm.SpriteState;

            int maxDx = 0;
            int maxDy = 0;

            // 1. Restore the original pixels for the stamp footprint at B (all symmetry points)
            foreach (var (sx, sy, isMirroredX, isMirroredY) in GetSymmetryPoints(b.x, b.y))
            {
                int angle = _vm.BrushAngle;
                if (isMirroredX || isMirroredY)
                    angle = MirrorBrushAngle(angle, isMirroredX, isMirroredY);

                var symOffsets = DrawingService.GetStampOffsetsArray(_vm.BrushSize, _vm.BrushShape, angle);
                foreach (var (dx, dy) in symOffsets)
                {
                    if (Math.Abs(dx) > maxDx) maxDx = Math.Abs(dx);
                    if (Math.Abs(dy) > maxDy) maxDy = Math.Abs(dy);

                    int px = sx + dx;
                    int py = sy + dy;
                    if (px >= 0 && px < state.Width && py >= 0 && py < state.Height)
                    {
                        int idx = py * state.Width + px;
                        if (_pixelPerfectOriginalStates.TryGetValue(idx, out bool orig))
                        {
                            state.Pixels[idx] = orig;
                        }
                    }
                }
            }

            // 2. Redraw all valid stamps in the path that intersect with B's bounding box
            foreach (var p in _pixelPerfectStrokePath)
            {
                bool intersects = IsSymmetricIntersection(p.x, p.y, b.x, b.y, maxDx, maxDy, 
                    _vm.IsSymmetryHorizontalEnabled, _vm.IsSymmetryVerticalEnabled, _vm.SymmetryAxisX, _vm.SymmetryAxisY);

                if (intersects)
                {
                    foreach (var (sx, sy, isMirroredX, isMirroredY) in GetSymmetryPoints(p.x, p.y))
                    {
                        int angle = _vm.BrushAngle;
                        if (isMirroredX || isMirroredY)
                            angle = MirrorBrushAngle(angle, isMirroredX, isMirroredY);
                        
                        if (_vm.CurrentTool == ToolMode.Dither)
                            _drawingService.DrawBrushStampDithered(state, sx, sy, _vm.BrushSize, newState, _vm.DitherPattern, _vm.BrushShape, angle, _currentClip);
                        else
                            _drawingService.DrawBrushStamp(state, sx, sy, _vm.BrushSize, newState, _vm.BrushShape, angle, _currentClip);
                    }
                }
            }

            // 3. Redraw the affected visual region (including all symmetry points of B)
            var (dirtyMinX, dirtyMinY, dirtyMaxX, dirtyMaxY) = GetSymmetricDirtyRegion(b.x, b.y);
            _vm.RedrawRegion(dirtyMinX, dirtyMinY, dirtyMaxX, dirtyMaxY, updatePreviewSimulation: false);
        }

        internal static bool IsSymmetricIntersection(int px, int py, int bx, int by, int maxDx, int maxDy,
            bool horz, bool vert, double axisX, double axisY)
        {
            // Primary point
            if (Math.Abs(px - bx) <= maxDx * 2 && Math.Abs(py - by) <= maxDy * 2)
                return true;

            if (vert)
            {
                int mx = (int)Math.Round(2 * axisX - px - 1, MidpointRounding.AwayFromZero);
                if (Math.Abs(mx - bx) <= maxDx * 2 && Math.Abs(py - by) <= maxDy * 2)
                    return true;
            }
            if (horz)
            {
                int my = (int)Math.Round(2 * axisY - py - 1, MidpointRounding.AwayFromZero);
                if (Math.Abs(px - bx) <= maxDx * 2 && Math.Abs(my - by) <= maxDy * 2)
                    return true;
            }
            if (horz && vert)
            {
                int mx = (int)Math.Round(2 * axisX - px - 1, MidpointRounding.AwayFromZero);
                int my = (int)Math.Round(2 * axisY - py - 1, MidpointRounding.AwayFromZero);
                if (Math.Abs(mx - bx) <= maxDx * 2 && Math.Abs(my - by) <= maxDy * 2)
                    return true;
            }

            return false;
        }

        private static IEnumerable<(int x, int y)> EnumerateLinePoints(int x0, int y0, int x1, int y1)
        {
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                yield return (x0, y0);
                if (x0 == x1 && y0 == y1) break;

                int e2 = 2 * err;
                // Force 4-connected (orthogonal) steps by prioritizing one axis at a time.
                // This guarantees the pixel-perfect corner detection never misses a diagonal jump.
                if (e2 > -dy) 
                { 
                    err -= dy; 
                    x0 += sx; 
                }
                else if (e2 < dx) 
                { 
                    err += dx; 
                    y0 += sy; 
                }
            }
        }

        // ── Move tool implementation ────────────────────────────────────────
        //
        // Behaviour summary:
        //   • By default, the move tool moves the entire active layer/canvas.
        //   • If there is a floating selection, the move tool only moves that
        //     floating selection.
        //   • On release, layer moves auto-commit and deselect; floating
        //     selection moves stay floating for further adjustment.
        //   • A plain click (no drag) is a no-op — it must NOT push an undo entry.
        //
        // To honour the "no undo on plain clicks" rule we DEFER the lift / undo
        // save until the user actually drags. HandleMoveDown only records intent
        // in `_movePending`; HandleMoveMove promotes that into a real drag.

        private void HandleMoveDown(int x, int y)
        {
            // Reset any leftover pending state from a previous interaction.
            _movePending = MovePendingKind.None;
            _movePendingX = x;
            _movePendingY = y;

            var selection = _vm.SelectionService;

            // New behavior: Move tool always moves entire canvas by default.
            // Only if there's an active floating selection do we move that instead.
            if (selection.IsFloating || selection.HasActiveSelection)
            {
                _movePending = MovePendingKind.SelectionDrag;
            }
            else
            {
                // No floating selection: prepare to move the whole canvas.
                _movePending = MovePendingKind.LayerMove;
            }
        }

        private void HandleMoveMove(int x, int y)
        {
            // Promote pending intent into a real drag the moment the cursor
            // actually moves to a different pixel than the down position.
            if (_movePending != MovePendingKind.None && !_isMoving)
            {
                if (x == _movePendingX && y == _movePendingY)
                    return;

                BeginPendingMoveDrag();
                if (!_isMoving)
                    return;
            }

            if (!_isMoving)
                return;

            var selection = _vm.SelectionService;
            if (!selection.IsFloating)
                return;

            int newX = x + _moveOffsetX;
            int newY = y + _moveOffsetY;

            if (newX != selection.FloatingX || newY != selection.FloatingY)
            {
                int oldX = selection.FloatingX;
                int oldY = selection.FloatingY;
                int fw = selection.FloatingWidth;
                int fh = selection.FloatingHeight;

                selection.MoveFloatingTo(newX, newY);

                int minX = Math.Min(oldX, newX);
                int minY = Math.Min(oldY, newY);
                int maxX = Math.Max(oldX + fw - 1, newX + fw - 1);
                int maxY = Math.Max(oldY + fh - 1, newY + fh - 1);

                const int padding = 1;
                _vm.RedrawRegion(minX - padding, minY - padding, maxX + padding, maxY + padding, updatePreviewSimulation: false);
            }
        }

        private void HandleMoveUp()
        {
            // Active drag → finalize it. Layer moves auto-commit and deselect;
            // selection moves stay floating for further adjustment.
            if (_isMoving)
            {
                var selection = _vm.SelectionService;
                if (selection.IsFloating)
                {
                    selection.EndDrag();
                    if (_isLayerMove && _vm.SpriteState.ActivePixelBuffer is Core.OverflowPixelBuffer)
                    {
                        // For overflow layers, use ShiftContent to preserve off-canvas data.
                        // The floating selection was used for live preview only — discard it
                        // and apply the displacement directly to the buffer.
                        int dx = selection.FloatingX - _moveOriginalFloatingX;
                        int dy = selection.FloatingY - _moveOriginalFloatingY;
                        selection.Cancel();
                        
                        // Restore the pre-lift snapshot and apply the shift displacement.
                        if (_moveOverflowSnapshot is Core.OverflowPixelBuffer restoredOvf)
                        {
                            var frame = _vm.SpriteState.Frames[_vm.SpriteState.ActiveFrameIndex];
                            frame.LayerPixels[_vm.SpriteState.ActiveLayerIndex] = restoredOvf;
                            restoredOvf.ShiftContent(dx, dy);
                            _vm.SpriteState.Pixels = restoredOvf.GetMonochromeData();
                        }
                        _moveOverflowSnapshot = null;
                    }
                    else if (_isLayerMove)
                    {
                        selection.CommitSelection(_vm.SpriteState);
                        selection.Cancel();
                    }
                    _vm.RedrawGridFromMemory();
                    _vm.MarkCodeStale();
                }

                _isMoving = false;
                _isLayerMove = false;
                _movePending = MovePendingKind.None;
                return;
            }

            // No drag occurred — plain clicks are no-ops (no state change, no undo entry).
            _movePending = MovePendingKind.None;
        }

        /// <summary>
        /// Promotes a pending Move-tool mouse-down into an active drag.
        /// Pushes an undo entry, lifts the appropriate region into a floating
        /// selection, and primes the drag offsets.
        /// </summary>
        private void BeginPendingMoveDrag()
        {
            var state = _vm.SpriteState;
            var selection = _vm.SelectionService;
            int downX = _movePendingX;
            int downY = _movePendingY;
            var kind = _movePending;
            _movePending = MovePendingKind.None;

            switch (kind)
            {
                case MovePendingKind.SelectionDrag:
                {
                    if (!selection.IsFloating)
                    {
                        if (!selection.HasAnyPixelInSelection(state))
                        {
                            _vm.ShowStatus("Selected area on active layer is empty.", 3000);
                            _isMoving = false;
                            return;
                        }

                        _vm.SaveStateForUndo();
                        selection.LiftSelection(state);
                        _vm.RedrawGridFromMemory(updateHardware: false);
                    }
                    _isLayerMove = false;
                    break;
                }
                case MovePendingKind.LayerMove:
                {
                    _vm.SaveStateForUndo();
                    
                    // For overflow layers, save the full buffer before the lift erases state.Pixels.
                    // SyncActiveLayer (called by RedrawGridFromMemory) would overwrite _extendedPixels
                    // with the erased data, so we must snapshot here.
                    if (state.ActivePixelBuffer is Core.OverflowPixelBuffer ovfSnap)
                    {
                        state.SyncActiveLayer(); // Ensure latest canvas is in _extendedPixels
                        _moveOverflowSnapshot = ovfSnap.Clone();
                    }
                    else
                    {
                        _moveOverflowSnapshot = null;
                    }

                    SelectEntireCanvas(state, selection);
                    if (selection.HasActiveSelection)
                        selection.LiftSelection(state);

                    // For overflow layers, the default LiftSelection only grabbed the canvas bounds.
                    // To live-preview the whole overflow during drag, we inject the extended data 
                    // into the floating selection, and wipe the active layer blank.
                    if (state.ActivePixelBuffer is Core.OverflowPixelBuffer ovfRef && _moveOverflowSnapshot is Core.OverflowPixelBuffer snapshot)
                    {
                        int extW = snapshot.ExtendedWidth;
                        int extH = snapshot.ExtendedHeight;
                        var extPixels = snapshot.GetExtendedData();
                        var floating2D = new bool[extW, extH];
                        for(int y=0; y<extH; y++)
                        {
                           for(int x=0; x<extW; x++)
                           {
                              floating2D[x,y] = extPixels[y * extW + x];
                           }
                        }
                              
                        selection.ReplaceFloatingData(floating2D, -snapshot.MarginX, -snapshot.MarginY, extW, extH);
                        
                        // Wipe the active layer entirely so no original pixels show underneath the floating preview.
                        ovfRef.Clear();
                        state.Pixels = ovfRef.GetMonochromeData();
                    }

                    _vm.RedrawGridFromMemory(updateHardware: false);
                    _isLayerMove = true;
                    break;
                }
                default:
                    return;
            }

            if (!selection.IsFloating)
            {
                // Layer was empty (or the lift produced no floating data).
                // Nothing to drag — abort cleanly without leaving stale state.
                _isLayerMove = false;
                return;
            }

            _isMoving = true;
            _moveStartX = downX;
            _moveStartY = downY;
            _moveOriginalFloatingX = selection.FloatingX;
            _moveOriginalFloatingY = selection.FloatingY;
            _moveOffsetX = selection.FloatingX - downX;
            _moveOffsetY = selection.FloatingY - downY;
            selection.BeginDrag();
        }

        /// <summary>
        /// Synthesises a full-canvas selection used as the lift region for a
        /// no-selection "move whole layer" operation.
        /// </summary>
        private static void SelectEntireCanvas(SpriteState state, ISelectionService selection)
        {
            int w = state.Width;
            int h = state.Height;
            if (w <= 0 || h <= 0) return;

            var mask = new bool[w, h];
            for (int yy = 0; yy < h; yy++)
                for (int xx = 0; xx < w; xx++)
                    mask[xx, yy] = true;

            selection.ApplyMask(mask, 0, 0, w - 1, h - 1, SelectionMode.Replace);
        }
    }
}
