using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Application = System.Windows.Application;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;

namespace Hexprite.Rendering
{
    /// <summary>
    /// Manages marquee and lasso selection overlay rendering.
    /// Reads pixel-space data from ISelectionService and converts to screen-space.
    /// </summary>
    public class SelectionOverlayRenderer
    {
        private readonly CanvasElementProvider _elements;
        private readonly Func<MainViewModel?> _getVm;
        private readonly Func<ISelectionService?> _getSelection;
        private readonly List<Rectangle> _transformHandlePool = new(8);

        // Bug 6: Cached geometry objects — reused every frame to avoid per-mouse-move
        // GDI+ heap allocations that promote to gen-2 GC during lasso drags.
        private GeometryGroup? _cachedGroupGeom;
        private StreamGeometry? _cachedDragGeom;
        // H2: also cache the inner per-pixel boundary geometry so it is not
        // re-allocated on every Update() call during a lasso / magic-wand drag.
        // Open() internally unfreezes the geometry for reuse; do NOT Freeze() it.
        private StreamGeometry? _cachedMaskGeom;
        private bool[,]? _lastTracedMask;
        private double _lastTracedCw;
        private double _lastTracedCh;

        /// <summary>Initializes a new renderer.</summary>
        public SelectionOverlayRenderer(
            CanvasElementProvider elements,
            Func<MainViewModel?> getVm,
            Func<ISelectionService?> getSelection)
        {
            _elements = elements ?? throw new ArgumentNullException(nameof(elements));
            _getVm = getVm ?? throw new ArgumentNullException(nameof(getVm));
            _getSelection = getSelection ?? throw new ArgumentNullException(nameof(getSelection));
            for (int i = 0; i < 8; i++)
                _transformHandlePool.Add(new Rectangle { SnapsToDevicePixels = true, Visibility = Visibility.Hidden });
        }

        /// <summary>Updates the selection overlay based on the current selection state.</summary>
        public void Update()
        {
            var sel = _getSelection();
            if (sel == null) { Clear(); return; }

            if (!sel.HasActiveSelection && !sel.IsSelecting)
            {
                Clear();
                return;
            }

            var vm = _getVm();
            if (vm == null) return;
            
            vm.NotifySelectionBoundsChanged();

            var (needsLasso, showMarquee, showEllipse) =
                ComputeOverlayVisibility(sel.Mask != null, sel.IsSelecting, sel.HasActiveSelection,
                    sel.BaseMask != null, vm.CurrentTool);

            bool showLasso = false;

            if (needsLasso)
            {
                showLasso = UpdateLassoOverlay(sel, vm);
            }

            if (sel.IsSelecting)
            {
                if (vm.CurrentTool == ToolMode.EllipticalMarquee)
                {
                    UpdateEllipseOverlay(sel, vm);
                    showEllipse = true;
                }
                else if (vm.CurrentTool == ToolMode.Marquee)
                {
                    UpdateMarqueeOverlay(sel, vm);
                    showMarquee = true;
                }
            }
            else if (!needsLasso)
            {
                // Finalized simple rectangle selection
                UpdateMarqueeOverlay(sel, vm);
                showMarquee = true;
            }

            var lasso = _elements.GetLassoOverlay();
            if (lasso != null) lasso.Visibility = showLasso ? Visibility.Visible : Visibility.Hidden;

            var marquee = _elements.GetMarqueeOverlay();
            if (marquee != null) marquee.Visibility = showMarquee ? Visibility.Visible : Visibility.Hidden;

            var ellipse = _elements.GetEllipseOverlay();
            if (ellipse != null) ellipse.Visibility = showEllipse ? Visibility.Visible : Visibility.Hidden;

            UpdateTransformHandles(sel, vm);
        }

        public void Clear()
        {
            var m = _elements.GetMarqueeOverlay();
            var e = _elements.GetEllipseOverlay();
            var l = _elements.GetLassoOverlay();
            var th = _elements.GetTransformHandlesLayer();
            if (m != null) m.Visibility = Visibility.Hidden;
            if (e != null) e.Visibility = Visibility.Hidden;
            if (l != null) l.Visibility = Visibility.Hidden;
            if (th != null)
            {
                foreach (var handle in _transformHandlePool)
                    handle.Visibility = Visibility.Hidden;
                th.Visibility = Visibility.Hidden;
            }
        }

        // ── Marquee (rectangle selection) ─────────────────────────────────

        private void UpdateMarqueeOverlay(ISelectionService sel, MainViewModel vm)
        {
            var marquee = _elements.GetMarqueeOverlay();
            if (marquee == null) return;

            var grid = _elements.GetPixelGridContainer();
            double gw = grid.ActualWidth  > 0 ? grid.ActualWidth  : 400.0;
            double gh = grid.ActualHeight > 0 ? grid.ActualHeight : 400.0;
            // M4: guard against zero sprite dimensions to prevent double.PositiveInfinity
            // propagating into Canvas.SetLeft / marquee.Width as NaN.
            int sw = vm.SpriteState.Width;
            int sh = vm.SpriteState.Height;
            if (sw <= 0 || sh <= 0) return;
            double cw = gw / sw;
            double ch = gh / sh;

            // When rotating, RotationAngle is non-zero and FloatingX/Y/W/H
            // hold the pre-rotation dimensions (UpdateRotation only stores the angle).
            // The marquee is drawn at the original rectangle and a RotateTransform
            // is applied to rotate it visually with the content.
            bool isRotating = Math.Abs(sel.RotationAngle) > 0.01;
            int minX, minY, maxX, maxY;
            if (sel.IsSelecting)
            {
                minX = sel.DragMinX;
                minY = sel.DragMinY;
                maxX = sel.DragMaxX;
                maxY = sel.DragMaxY;
            }
            else
            {
                minX = sel.IsFloating ? sel.FloatingX : sel.MinX;
                minY = sel.IsFloating ? sel.FloatingY : sel.MinY;
                maxX = sel.IsFloating ? sel.FloatingX + sel.FloatingWidth - 1 : sel.MaxX;
                maxY = sel.IsFloating ? sel.FloatingY + sel.FloatingHeight - 1 : sel.MaxY;
            }

            Canvas.SetLeft(marquee, minX * cw);
            Canvas.SetTop(marquee, minY * ch);
            marquee.Width = (maxX - minX + 1) * cw;
            marquee.Height = (maxY - minY + 1) * ch;
            // visibility is managed by Update()

            // Apply rotation to the marquee overlay
            if (isRotating)
            {
                double centerX = (minX + (maxX - minX + 1) * 0.5) * cw;
                double centerY = (minY + (maxY - minY + 1) * 0.5) * ch;
                // RenderTransformOrigin is relative to layout, so use explicit center
                double localCenterX = centerX - minX * cw;
                double localCenterY = centerY - minY * ch;
                marquee.RenderTransform = new RotateTransform(sel.RotationAngle, localCenterX, localCenterY);
            }
            else
            {
                marquee.RenderTransform = null;
            }
        }

        // ── Ellipse (ellipse selection) ───────────────────────────────────

        private void UpdateEllipseOverlay(ISelectionService sel, MainViewModel vm)
        {
            var ellipse = _elements.GetEllipseOverlay();
            if (ellipse == null) return;

            var grid = _elements.GetPixelGridContainer();
            double gw = grid.ActualWidth  > 0 ? grid.ActualWidth  : 400.0;
            double gh = grid.ActualHeight > 0 ? grid.ActualHeight : 400.0;
            int sw = vm.SpriteState.Width;
            int sh = vm.SpriteState.Height;
            if (sw <= 0 || sh <= 0) return;
            double cw = gw / sw;
            double ch = gh / sh;

            bool isRotating = Math.Abs(sel.RotationAngle) > 0.01;
            int minX, minY, maxX, maxY;
            if (sel.IsSelecting)
            {
                minX = sel.DragMinX;
                minY = sel.DragMinY;
                maxX = sel.DragMaxX;
                maxY = sel.DragMaxY;
            }
            else
            {
                minX = sel.IsFloating ? sel.FloatingX : sel.MinX;
                minY = sel.IsFloating ? sel.FloatingY : sel.MinY;
                maxX = sel.IsFloating ? sel.FloatingX + sel.FloatingWidth - 1 : sel.MaxX;
                maxY = sel.IsFloating ? sel.FloatingY + sel.FloatingHeight - 1 : sel.MaxY;
            }

            Canvas.SetLeft(ellipse, minX * cw);
            Canvas.SetTop(ellipse, minY * ch);
            ellipse.Width = (maxX - minX + 1) * cw;
            ellipse.Height = (maxY - minY + 1) * ch;
            // visibility is managed by Update()

            // Apply rotation to the ellipse overlay
            if (isRotating)
            {
                double centerX = (minX + (maxX - minX + 1) * 0.5) * cw;
                double centerY = (minY + (maxY - minY + 1) * 0.5) * ch;
                double localCenterX = centerX - minX * cw;
                double localCenterY = centerY - minY * ch;
                ellipse.RenderTransform = new RotateTransform(sel.RotationAngle, localCenterX, localCenterY);
            }
            else
            {
                ellipse.RenderTransform = null;
            }
        }

        // ── Lasso / Magic Wand (per-pixel boundary tracing) ───────────────

        private bool UpdateLassoOverlay(ISelectionService sel, MainViewModel vm)
        {
            var lasso = _elements.GetLassoOverlay();
            if (lasso == null) return false;

            var grid = _elements.GetPixelGridContainer();
            double gw = grid.ActualWidth  > 0 ? grid.ActualWidth  : 400.0;
            double gh = grid.ActualHeight > 0 ? grid.ActualHeight : 400.0;
            // M4: guard against zero sprite dimensions.
            int sw = vm.SpriteState.Width;
            int sh = vm.SpriteState.Height;
            if (sw <= 0 || sh <= 0) return false;
            double cw = gw / sw;
            double ch = gh / sh;

            bool[,]? mask;
            int baseX, baseY, maskW, maskH;

            if (sel.IsFloating && sel.FloatingPixels != null)
            {
                // Use the original mask shape if available, otherwise fallback to the colored pixels
                mask = sel.Mask ?? sel.FloatingPixels;
                baseX = sel.FloatingX;
                baseY = sel.FloatingY;
                maskW = sel.FloatingWidth;
                maskH = sel.FloatingHeight;
            }
            else if (sel.IsSelecting && sel.BaseMask != null)
            {
                // During a boolean operation drag, show the original un-modified selection 
                // boundaries so the user knows what they are intersecting/subtracting from.
                mask = sel.BaseMask;
                baseX = sel.BaseMinX;
                baseY = sel.BaseMinY;
                maskW = sel.BaseMaxX - sel.BaseMinX + 1;
                maskH = sel.BaseMaxY - sel.BaseMinY + 1;
            }
            else if (sel.HasActiveSelection || sel.IsSelecting)
            {
                mask = sel.Mask;
                baseX = sel.MinX;
                baseY = sel.MinY;
                maskW = sel.MaxX - sel.MinX + 1;
                maskH = sel.MaxY - sel.MinY + 1;
            }
            else
            {
                mask = null;
                baseX = 0; baseY = 0; maskW = 0; maskH = 0;
            }

            bool hasDragGeom = sel.IsSelecting && sel.LassoPoints.Count > 1;
            if (hasDragGeom)
            {
                _cachedDragGeom ??= new StreamGeometry();
                using var ctx = _cachedDragGeom.Open();
                // Drag geometry must use local coords (relative to baseX/baseY) because
                // lasso.RenderTransform always applies TranslateTransform(baseX*cw, baseY*ch).
                // Using global coords here caused a double-offset equal to the first click position.
                ctx.BeginFigure(new Point((sel.LassoPoints[0].X - baseX) * cw, (sel.LassoPoints[0].Y - baseY) * ch), isFilled: true, isClosed: true);
                for (int i = 1; i < sel.LassoPoints.Count; i++)
                    ctx.LineTo(new Point((sel.LassoPoints[i].X - baseX) * cw, (sel.LassoPoints[i].Y - baseY) * ch), isStroked: true, isSmoothJoin: false);
            }

            bool maskUpdated = false;
            if (mask != null)
            {
                int effectiveMaskW = Math.Min(maskW, mask.GetLength(0));
                int effectiveMaskH = Math.Min(maskH, mask.GetLength(1));
                if (effectiveMaskW > 0 && effectiveMaskH > 0)
                {
                    if (_lastTracedMask != mask || Math.Abs(_lastTracedCw - cw) > 0.001 || Math.Abs(_lastTracedCh - ch) > 0.001)
                    {
                        int estimatedEdges = (effectiveMaskW + effectiveMaskH) * 2;
                        var edgesByStart = new Dictionary<(int, int), List<(int, int)>>(estimatedEdges);

                        void addEdge(int x1, int y1, int x2, int y2)
                        {
                            if (!edgesByStart.TryGetValue((x1, y1), out var list))
                            {
                                list = new List<(int, int)>(2);
                                edgesByStart[(x1, y1)] = list;
                            }
                            list.Add((x2, y2));
                        }

                        for (int y = 0; y < effectiveMaskH; y++)
                        {
                            for (int x = 0; x < effectiveMaskW; x++)
                            {
                                if (mask[x, y])
                                {
                                    if (y == 0 || !mask[x, y - 1]) addEdge(x, y, x + 1, y);
                                    if (y == effectiveMaskH - 1 || !mask[x, y + 1]) addEdge(x + 1, y + 1, x, y + 1);
                                    if (x == 0 || !mask[x - 1, y]) addEdge(x, y + 1, x, y);
                                    if (x == effectiveMaskW - 1 || !mask[x + 1, y]) addEdge(x + 1, y, x + 1, y + 1);
                                }
                            }
                        }

                        _cachedMaskGeom ??= new StreamGeometry();
                        using (var ctx = _cachedMaskGeom.Open())
                        {
                            while (edgesByStart.Count > 0)
                            {
                                var e = edgesByStart.GetEnumerator();
                                e.MoveNext();
                                var startNode = e.Current.Key;
                                e.Dispose();

                                ctx.BeginFigure(new Point(startNode.Item1 * cw, startNode.Item2 * ch), isFilled: false, isClosed: true);

                                var curr = startNode;
                                while (true)
                                {
                                    if (!edgesByStart.TryGetValue(curr, out var list) || list.Count == 0)
                                        break;

                                    var next = list[^1];
                                    list.RemoveAt(list.Count - 1);
                                    if (list.Count == 0) edgesByStart.Remove(curr);

                                    ctx.LineTo(new Point(next.Item1 * cw, next.Item2 * ch), isStroked: true, isSmoothJoin: false);
                                    curr = next;
                                    if (curr == startNode) break;
                                }
                            }
                        }
                        
                        _lastTracedMask = mask;
                        _lastTracedCw = cw;
                        _lastTracedCh = ch;
                        maskUpdated = true;
                    }
                }
            }

            Geometry dataGeom;
            if (hasDragGeom && _cachedMaskGeom != null && mask != null)
            {
                _cachedGroupGeom ??= new GeometryGroup();
                if (_cachedGroupGeom.Children.Count != 2 || maskUpdated)
                {
                    _cachedGroupGeom.Children.Clear();
                    if (_cachedDragGeom != null) _cachedGroupGeom.Children.Add(_cachedDragGeom);
                    _cachedGroupGeom.Children.Add(_cachedMaskGeom);
                }
                dataGeom = _cachedGroupGeom;
            }
            else if (hasDragGeom && _cachedDragGeom != null)
            {
                dataGeom = _cachedDragGeom;
            }
            else if (_cachedMaskGeom != null && mask != null)
            {
                dataGeom = _cachedMaskGeom;
            }
            else
            {
                return false;
            }

            var transform = new TransformGroup();
            transform.Children.Add(new TranslateTransform(baseX * cw, baseY * ch));

            bool isRotating = sel.IsFloating && Math.Abs(sel.RotationAngle) > 0.01;
            if (isRotating)
            {
                double cx = (baseX + maskW * 0.5) * cw;
                double cy = (baseY + maskH * 0.5) * ch;
                transform.Children.Add(new RotateTransform(sel.RotationAngle, cx, cy));
            }

            lasso.RenderTransform = transform;
            lasso.Data = dataGeom;
            return true;
        }

        /// <summary>
        /// Decorative resize handles around a floating selection (hit-testing is done in the View).
        /// During a rotation transform, draws handles at the original (pre-rotation) bounding box
        /// and applies a RotateTransform to the layer so handles rotate visually with the selection.
        /// </summary>
        private void UpdateTransformHandles(ISelectionService sel, MainViewModel vm)
        {
            var layer = _elements.GetTransformHandlesLayer();
            if (layer == null) return;

            if (!sel.HasActiveSelection || !sel.IsFloating || sel.IsSelecting)
            {
                foreach (var handle in _transformHandlePool)
                    handle.Visibility = Visibility.Hidden;
                layer.Visibility = Visibility.Hidden;
                layer.RenderTransform = null;
                return;
            }

            var grid = _elements.GetPixelGridContainer();
            double gw = grid.ActualWidth  > 0 ? grid.ActualWidth  : 400.0;
            double gh = grid.ActualHeight > 0 ? grid.ActualHeight : 400.0;
            // M4: guard against zero sprite dimensions before dividing — avoids
            // double.PositiveInfinity being written into handle Canvas positions.
            int sw = vm.SpriteState.Width;
            int sh = vm.SpriteState.Height;
            if (sw <= 0 || sh <= 0)
            {
                foreach (var handle in _transformHandlePool)
                    handle.Visibility = Visibility.Hidden;
                layer.Visibility = Visibility.Hidden;
                layer.RenderTransform = null;
                return;
            }
            double cw = gw / sw;
            double ch = gh / sh;

            Brush stroke = (Brush)(Application.Current.TryFindResource("Brush.Accent.PreviewBorder") ?? Brushes.White);
            Brush fill = (Brush)(Application.Current.TryFindResource("Brush.Surface.Base") ?? Brushes.Black);

            double cellMin = Math.Min(cw, ch);
            if (cellMin <= 0)
            {
                foreach (var handle in _transformHandlePool)
                    handle.Visibility = Visibility.Hidden;
                layer.Visibility = Visibility.Hidden;
                layer.RenderTransform = null;
                return;
            }
            // Constant screen-space handle size regardless of zoom
            // The container has a ScaleTransform, so we counter-scale to maintain constant size
            double zoomLevel = vm.ZoomLevel;
            if (zoomLevel <= 0) zoomLevel = 1.0;
            const double handleSize = 10.0;
            double hs = handleSize / zoomLevel;

            // When rotating, FloatingX/Y/W/H hold the pre-rotation dimensions.
            // Handles are positioned at the original rectangle and the layer
            // gets a RotateTransform to rotate them visually.
            bool isRotating = Math.Abs(sel.RotationAngle) > 0.01;
            int fx = sel.FloatingX;
            int fy = sel.FloatingY;
            int fw = sel.FloatingWidth;
            int fh = sel.FloatingHeight;

            double left = fx * cw;
            double top = fy * ch;
            double right = (fx + fw) * cw;
            double bottom = (fy + fh) * ch;
            double midX = (fx + fw * 0.5) * cw;
            double midY = (fy + fh * 0.5) * ch;

            double strokeThick = hs * 0.12;

            if (layer.Children.Count == 0)
            {
                foreach (var handle in _transformHandlePool)
                    layer.Children.Add(handle);
            }

            static void PositionHandle(Rectangle r, double px, double py, double hs, double zoom, Brush stroke, Brush fill, double strokeThick)
            {
                r.Width = hs;
                r.Height = hs;
                r.Stroke = stroke;
                r.StrokeThickness = strokeThick;
                r.Fill = fill;
                // px is in the scaled container coordinate system
                // hs is counter-scaled, so hs * zoom = visual size (10px)
                // To center the handle, subtract half the visual size in container coords
                Canvas.SetLeft(r, px - 5.0 / zoom);
                Canvas.SetTop(r, py - 5.0 / zoom);
                r.Visibility = Visibility.Visible;
            }

            PositionHandle(_transformHandlePool[0], left, top, hs, zoomLevel, stroke, fill, strokeThick);
            PositionHandle(_transformHandlePool[1], midX, top, hs, zoomLevel, stroke, fill, strokeThick);
            PositionHandle(_transformHandlePool[2], right, top, hs, zoomLevel, stroke, fill, strokeThick);
            PositionHandle(_transformHandlePool[3], right, midY, hs, zoomLevel, stroke, fill, strokeThick);
            PositionHandle(_transformHandlePool[4], right, bottom, hs, zoomLevel, stroke, fill, strokeThick);
            PositionHandle(_transformHandlePool[5], midX, bottom, hs, zoomLevel, stroke, fill, strokeThick);
            PositionHandle(_transformHandlePool[6], left, bottom, hs, zoomLevel, stroke, fill, strokeThick);
            PositionHandle(_transformHandlePool[7], left, midY, hs, zoomLevel, stroke, fill, strokeThick);

            // Apply rotation transform to the handles layer during rotation
            if (isRotating)
            {
                // Center of rotation in the handles layer coordinate system
                double centerX = (fx + fw * 0.5) * cw;
                double centerY = (fy + fh * 0.5) * ch;
                layer.RenderTransform = new RotateTransform(sel.RotationAngle, centerX, centerY);
            }
            else
            {
                layer.RenderTransform = null;
            }

            layer.Visibility = Visibility.Visible;
        }

        // ── Testable overlay visibility logic ─────────────────────────────

        /// <summary>
        /// Pure decision logic for which selection overlays should be visible.
        /// Extracted so unit tests can verify the boolean conditions without WPF dependencies.
        /// </summary>
        /// <returns>
        /// needsLasso: whether the lasso/mask boundary renderer should run.
        /// showMarquee: initial marquee visibility (may be set true later by the caller for finalized rects).
        /// showEllipse: initial ellipse visibility (may be set true later by the caller during drag).
        /// </returns>
        internal static (bool needsLasso, bool showMarquee, bool showEllipse) ComputeOverlayVisibility(
            bool hasMask, bool isSelecting, bool hasActiveSelection,
            bool hasBaseMask, ToolMode currentTool)
        {
            bool needsLasso = (hasMask
                || (isSelecting && hasBaseMask)
                || (isSelecting && (currentTool == ToolMode.Lasso || currentTool == ToolMode.MagicWand)))
                && !(isSelecting && currentTool == ToolMode.EllipticalMarquee && !hasBaseMask);

            // Initial values — callers may override these after calling this method.
            bool showMarquee = false;
            bool showEllipse = false;

            return (needsLasso, showMarquee, showEllipse);
        }
    }
}
