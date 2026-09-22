using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;

namespace Hexprite.Rendering
{
    /// <summary>
    /// Manages the brush cursor overlay: bitmap generation, positioning, and crosshair.
    /// Extracted from MainWindow.xaml.cs.
    /// </summary>
    public class BrushCursorManager(
        CanvasElementProvider elements,
        Func<MainViewModel?> getVm,
        Func<(int x, int y)> getPixelCoords)
    {
        private readonly CanvasElementProvider _elements = elements ?? throw new ArgumentNullException(nameof(elements));
        private readonly Func<MainViewModel?> _getVm = getVm ?? throw new ArgumentNullException(nameof(getVm));
        private readonly Func<(int x, int y)> _getPixelCoords = getPixelCoords ?? throw new ArgumentNullException(nameof(getPixelCoords));

        private WriteableBitmap? _brushCursorBitmap;
        private int _brushCursorCachedSize = -1;
        private BrushShape _brushCursorCachedShape = (BrushShape)(-1);
        private int _brushCursorCachedAngle = -1;

        // Cached overlay dimensions — only recomputed when brush params or canvas size change
        private double _cachedOverlayW;
        private double _cachedOverlayH;
        private double _cachedHalfW;
        private double _cachedHalfH;
        private int _cachedMinDx;
        private int _cachedMinDy;
        private int _cachedCanvasW = -1;
        private int _cachedCanvasH = -1;

        // Reusable TranslateTransform for the overlay (avoids layout invalidation)
        private TranslateTransform? _overlayTransform;
        private TranslateTransform? _crossHTransform;
        private TranslateTransform? _crossVTransform;
        private double _cachedZoom = -1.0;

        public Point LastCanvasMousePos { get; set; }
        public bool IsMouseOverCanvas { get; set; }

        /// <summary>
        /// Called when BrushSize/Shape/Angle changes to instantly refresh using last mouse position.
        /// </summary>
        public void Refresh()
        {
            InvalidateCache();
            RefreshPosition();
        }

        /// <summary>
        /// Updates the overlay without invalidating the cache, useful for view transforms like zooming.
        /// </summary>
        public void RefreshPosition()
        {
            if (!IsMouseOverCanvas) return;
            var overlay = _elements.GetBrushCursorOverlay();
            if (overlay == null) return;
            var image = _elements.GetCanvasImage();
            if (image == null || image.ActualWidth == 0) return;

            var (x, y) = _getPixelCoords();
            Update(x, y, LastCanvasMousePos, image.ActualWidth, image.ActualHeight);
        }

        public void Update(int pixelX, int pixelY, Point mousePos, double imgWidth, double imgHeight)
        {
            var overlay = _elements.GetBrushCursorOverlay();
            if (overlay == null) return;

            var vm = _getVm();
            if (vm == null || !(vm.CurrentTool == ToolMode.Pencil || vm.CurrentTool == ToolMode.Eraser ||
                                vm.CurrentTool == ToolMode.Dither ||
                                vm.CurrentTool == ToolMode.Line || vm.CurrentTool == ToolMode.Rectangle ||
                                vm.CurrentTool == ToolMode.Ellipse || vm.CurrentTool == ToolMode.FilledRectangle ||
                                vm.CurrentTool == ToolMode.FilledEllipse))
            {
                Hide();
                return;
            }

            if (!vm.IsBrushCursorVisible)
            {
                Hide();
                var canvasImageDirect = _elements.GetCanvasImage();
                if (canvasImageDirect != null && canvasImageDirect.Cursor != Cursors.Cross)
                    canvasImageDirect.Cursor = Cursors.Cross;
                return;
            }

            int brushSize = vm.BrushSize;
            var brushShape = vm.BrushShape;
            int brushAngle = vm.BrushAngle;
            int w = vm.SpriteState.Width;
            int h = vm.SpriteState.Height;

            // Rebuild cached overlay dimensions when brush params or canvas resolution change
            if (brushSize != _brushCursorCachedSize ||
                brushShape != _brushCursorCachedShape ||
                brushAngle != _brushCursorCachedAngle ||
                w != _cachedCanvasW || h != _cachedCanvasH ||
                _brushCursorBitmap == null)
            {
                RebuildOverlayCache(brushSize, brushShape, brushAngle, w, h);
            }

            bool completelyOutside =
                mousePos.X + _cachedHalfW < 0 || mousePos.X - _cachedHalfW > imgWidth ||
                mousePos.Y + _cachedHalfH < 0 || mousePos.Y - _cachedHalfH > imgHeight;

            var targetVis = completelyOutside ? Visibility.Hidden : Visibility.Visible;
            if (overlay.Visibility != targetVis)
                overlay.Visibility = targetVis;

            if (!completelyOutside)
            {
                // Ensure the overlay has a TranslateTransform for positioning
                // (RenderTransform doesn't trigger layout passes — much faster than Canvas.SetLeft/Top)
                if (_overlayTransform == null)
                {
                    _overlayTransform = new TranslateTransform();
                    overlay.RenderTransform = _overlayTransform;
                }

                double cellW = imgWidth / w;
                double cellH = imgHeight / h;
                double newX = (pixelX + _cachedMinDx) * cellW;
                double newY = (pixelY + _cachedMinDy) * cellH;
                if (_overlayTransform.X != newX) _overlayTransform.X = newX;
                if (_overlayTransform.Y != newY) _overlayTransform.Y = newY;
            }

            // Crosshair at exact mouse position — length and stroke are scaled inversely by zoom
            // to remain a constant visual size on screen.
            double zoom = vm.ZoomLevel > 0 ? vm.ZoomLevel : 1.0;
            var crossH = _elements.GetCrosshairH();
            var crossV = _elements.GetCrosshairV();

            if (zoom != _cachedZoom)
            {
                _cachedZoom = zoom;
                double crossLen = 8.0 / zoom;
                double crossStroke = 1.0 / zoom;

                if (crossH != null)
                {
                    crossH.X1 = -crossLen;
                    crossH.X2 = crossLen;
                    crossH.Y1 = 0;
                    crossH.Y2 = 0;
                    crossH.StrokeThickness = crossStroke;
                }
                if (crossV != null)
                {
                    crossV.X1 = 0;
                    crossV.X2 = 0;
                    crossV.Y1 = -crossLen;
                    crossV.Y2 = crossLen;
                    crossV.StrokeThickness = crossStroke;
                }
            }

            if (crossH != null)
            {
                if (crossH.Visibility != targetVis) crossH.Visibility = targetVis;
                if (!completelyOutside)
                {
                    if (_crossHTransform == null)
                    {
                        _crossHTransform = new TranslateTransform();
                        crossH.RenderTransform = _crossHTransform;
                    }
                    if (_crossHTransform.X != mousePos.X) _crossHTransform.X = mousePos.X;
                    if (_crossHTransform.Y != mousePos.Y) _crossHTransform.Y = mousePos.Y;
                }
            }
            if (crossV != null)
            {
                if (crossV.Visibility != targetVis) crossV.Visibility = targetVis;
                if (!completelyOutside)
                {
                    if (_crossVTransform == null)
                    {
                        _crossVTransform = new TranslateTransform();
                        crossV.RenderTransform = _crossVTransform;
                    }
                    if (_crossVTransform.X != mousePos.X) _crossVTransform.X = mousePos.X;
                    if (_crossVTransform.Y != mousePos.Y) _crossVTransform.Y = mousePos.Y;
                }
            }

            var canvasImage = _elements.GetCanvasImage();
            if (canvasImage != null && canvasImage.Cursor != Cursors.None)
                canvasImage.Cursor = Cursors.None;
        }

        public void Hide()
        {
            var overlay = _elements.GetBrushCursorOverlay();
            if (overlay != null && overlay.Visibility != Visibility.Hidden) overlay.Visibility = Visibility.Hidden;
            var crossH = _elements.GetCrosshairH();
            if (crossH != null && crossH.Visibility != Visibility.Hidden) crossH.Visibility = Visibility.Hidden;
            var crossV = _elements.GetCrosshairV();
            if (crossV != null && crossV.Visibility != Visibility.Hidden) crossV.Visibility = Visibility.Hidden;

            IsMouseOverCanvas = false;

            var canvasImage = _elements.GetCanvasImage();
            if (canvasImage != null && canvasImage.Cursor == Cursors.None)
                canvasImage.Cursor = null;
        }

        public void OnMouseLeave() => Hide();

        // ── Private helpers ───────────────────────────────────────────────

        private void InvalidateCache()
        {
            _brushCursorCachedSize = -1;
            _brushCursorCachedShape = (BrushShape)(-1);
            _brushCursorCachedAngle = -1;
            _cachedCanvasW = -1;
            _cachedCanvasH = -1;
            _cachedZoom = -1.0;
        }

        /// <summary>
        /// Rebuilds the overlay bitmap AND all cached dimensions/metrics.
        /// Called only when brush parameters or canvas resolution change,
        /// NOT on every mouse move.
        /// </summary>
        private void RebuildOverlayCache(int brushSize, BrushShape shape, int angleDeg, int canvasW, int canvasH)
        {
            // Bug 5: Guard against divide-by-zero when sprite dimensions are zero
            // (e.g. new document not yet initialised, or mid-resize transition).
            if (canvasW <= 0 || canvasH <= 0) return;

            _cachedCanvasW = canvasW;
            _cachedCanvasH = canvasH;

            var grid = _elements.GetPixelGridContainer();
            double gw = grid.ActualWidth > 0 ? grid.ActualWidth : 400.0;
            double gh = grid.ActualHeight > 0 ? grid.ActualHeight : 400.0;
            double cw = gw / canvasW;
            double ch = gh / canvasH;
            double cellUnit = Math.Min(cw, ch);

            // Apply crosshair theme brush once per cache rebuild to avoid DynamicResource lag.
            // Bug 4: Use TryFindResource so a missing/hot-reloading theme key doesn't throw InvalidCastException.
            var crosshairBrush = (Application.Current.TryFindResource("Brush.Canvas.Crosshair") as SolidColorBrush)
                ?? new SolidColorBrush(Colors.White);
            var crossH = _elements.GetCrosshairH();
            var crossV = _elements.GetCrosshairV();
            if (crossH != null) crossH.Stroke = crosshairBrush;
            if (crossV != null) crossV.Stroke = crosshairBrush;

            // Rebuild bitmap (will skip if brush params haven't changed)
            RebuildBitmap(brushSize, shape, angleDeg);

            // Compute overlay dimensions from the stamp offsets
            var offsets = DrawingService.ComputeStampOffsets(brushSize, shape, angleDeg);
            int minDx = 0, maxDx = 0, minDy = 0, maxDy = 0;
            foreach (var (dx, dy) in offsets)
            {
                if (dx < minDx) minDx = dx;
                if (dx > maxDx) maxDx = dx;
                if (dy < minDy) minDy = dy;
                if (dy > maxDy) maxDy = dy;
            }
            _cachedMinDx = minDx;
            _cachedMinDy = minDy;

            int stampW = maxDx - minDx + 1;
            int stampH = maxDy - minDy + 1;

            _cachedOverlayW = stampW * cw;
            _cachedOverlayH = stampH * ch;
            _cachedHalfW = _cachedOverlayW / 2.0;
            _cachedHalfH = _cachedOverlayH / 2.0;

            // Apply overlay dimensions (only when they change, not every frame)
            var overlay = _elements.GetBrushCursorOverlay();
            if (overlay != null)
            {
                overlay.Width = _cachedOverlayW;
                overlay.Height = _cachedOverlayH;
            }
        }

        // ── Private: bitmap generation ────────────────────────────────────

        private void RebuildBitmap(int brushSize, BrushShape shape, int angleDeg)
        {
            if (brushSize == _brushCursorCachedSize &&
                shape == _brushCursorCachedShape &&
                angleDeg == _brushCursorCachedAngle &&
                _brushCursorBitmap != null) return;

            _brushCursorCachedSize = brushSize;
            _brushCursorCachedShape = shape;
            _brushCursorCachedAngle = angleDeg;

            var offsets = DrawingService.ComputeStampOffsets(brushSize, shape, angleDeg);

            int minDx = 0, maxDx = 0, minDy = 0, maxDy = 0;
            foreach (var (dx, dy) in offsets)
            {
                if (dx < minDx) minDx = dx;
                if (dx > maxDx) maxDx = dx;
                if (dy < minDy) minDy = dy;
                if (dy > maxDy) maxDy = dy;
            }

            int bmpW = maxDx - minDx + 1;
            int bmpH = maxDy - minDy + 1;
            if (bmpW < 1) bmpW = 1;
            if (bmpH < 1) bmpH = 1;

            _brushCursorBitmap = new WriteableBitmap(bmpW, bmpH, 96, 96, PixelFormats.Pbgra32, palette: null);

            var pixels = new uint[bmpW * bmpH];
            // Bug 4: Fetch colors from active theme with null-safe fallback so a missing
            // or hot-reloading theme key doesn't throw InvalidCastException.
            var res = Application.Current.Resources;
            Color edge = res["Palette.Common.BrushEdge"] is Color c1 ? c1 : Colors.White;
            Color fill = res["Palette.Common.BrushFill"] is Color c2 ? c2 : Colors.Gray;
            
            // Premultiply alpha for Pbgra32 to avoid WPF D3D Bgra32 red/blue swap bug
            uint edgeColor = (uint)((edge.A << 24) | ((edge.R * edge.A / 255) << 16) | ((edge.G * edge.A / 255) << 8) | (edge.B * edge.A / 255));
            uint fillColor = (uint)((fill.A << 24) | ((fill.R * fill.A / 255) << 16) | ((fill.G * fill.A / 255) << 8) | (fill.B * fill.A / 255));

            var inStamp = new bool[bmpW * bmpH];
            foreach (var (dx, dy) in offsets)
            {
                int px = dx - minDx;
                int py = dy - minDy;
                inStamp[py * bmpW + px] = true;
            }

            for (int py = 0; py < bmpH; py++)
            {
                for (int px = 0; px < bmpW; px++)
                {
                    if (!inStamp[py * bmpW + px]) continue;

                    bool isEdge = px == 0 || py == 0 || px == bmpW - 1 || py == bmpH - 1
                        || !inStamp[py * bmpW + (px - 1)]
                        || !inStamp[py * bmpW + (px + 1)]
                        || !inStamp[(py - 1) * bmpW + px]
                        || !inStamp[(py + 1) * bmpW + px];

                    pixels[py * bmpW + px] = isEdge ? edgeColor : fillColor;
                }
            }

            _brushCursorBitmap.WritePixels(
                new Int32Rect(0, 0, bmpW, bmpH), pixels, bmpW * 4, 0);
            var overlayImg = _elements.GetBrushCursorOverlay();
            if (overlayImg != null) overlayImg.Source = _brushCursorBitmap;
        }
    }
}
