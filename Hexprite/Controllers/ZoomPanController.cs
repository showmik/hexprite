using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Hexprite.ViewModels;

namespace Hexprite.Controllers
{
    /// <summary>
    /// Handles zoom (scroll wheel, buttons, keyboard) and middle-click panning.
    /// Extracted from MainWindow.xaml.cs to isolate navigation concerns.
    /// </summary>
    public class ZoomPanController
    {
        private readonly Slider _zoomSlider;
        private readonly Func<Image> _getCanvasImage;
        private readonly Func<ShellViewModel> _getShell;

        private bool _isInternalZoomUpdate;

        // ── Panning state ─────────────────────────────────────────────────
        private Point _panStartMouse;
        private Point _panStartScroll;
        public bool IsPanning { get; private set; }

        public const double ZoomFactor = 1.15;

        public ZoomPanController(Slider zoomSlider, Func<Image> getCanvasImage, Func<ShellViewModel> getShell)
        {
            _zoomSlider = zoomSlider ?? throw new ArgumentNullException(nameof(zoomSlider));
            _getCanvasImage = getCanvasImage;
            _getShell = getShell;

            _zoomSlider.ValueChanged += ZoomSlider_ValueChanged;
        }

        /// <summary>
        /// Unsubscribes all event handlers registered during construction.
        /// Call from the owning window's Closed handler to prevent a rooted-controller leak (Bug 1).
        /// </summary>
        public void Detach()
        {
            _zoomSlider.ValueChanged -= ZoomSlider_ValueChanged;
        }

        private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInternalZoomUpdate) return;
            ApplyAbsoluteZoomCentered(e.NewValue, e.OldValue);
        }

        // ── Button handlers ───────────────────────────────────────────────

        public void ZoomIn() => ApplyZoomCentered(ZoomFactor);
        public void ZoomOut() => ApplyZoomCentered(1.0 / ZoomFactor);
        
        public void ZoomReset()
        {
            _isInternalZoomUpdate = true;
            try
            {
                _zoomSlider.Value = 1.0;

                var sv = FindScrollViewer();
                if (sv != null)
                {
                    var image = _getCanvasImage();
                    var border = image != null ? FindParent<Border>(image) : null;
                    if (border != null && border.LayoutTransform is ScaleTransform scale)
                    {
                        scale.SetCurrentValue(ScaleTransform.ScaleXProperty, 1.0);
                        scale.SetCurrentValue(ScaleTransform.ScaleYProperty, 1.0);
                    }

                    sv.UpdateLayout();

                    double targetOffsetX = (sv.ExtentWidth - sv.ViewportWidth) / 2.0;
                    double targetOffsetY = (sv.ExtentHeight - sv.ViewportHeight) / 2.0;

                    if (targetOffsetX > 0) sv.ScrollToHorizontalOffset(targetOffsetX);
                    else sv.ScrollToHorizontalOffset(0);

                    if (targetOffsetY > 0) sv.ScrollToVerticalOffset(targetOffsetY);
                    else sv.ScrollToVerticalOffset(0);
                }
            }
            finally
            {
                _isInternalZoomUpdate = false;
            }
        }

        // ── Scroll wheel ──────────────────────────────────────────────────

        public void HandleMouseWheel(ScrollViewer sv, MouseWheelEventArgs e)
        {
            // Ctrl+Scroll = adjust brush size
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (_getShell()?.ActiveDocument is ViewModels.MainViewModel vm)
                    vm.BrushSize += e.Delta > 0 ? 1 : -1;
                e.Handled = true;
                return;
            }

            double factor = e.Delta > 0 ? ZoomFactor : 1.0 / ZoomFactor;
            double oldZoom = _zoomSlider.Value;
            double newZoom = SnapToTick(Math.Clamp(oldZoom * factor, _zoomSlider.Minimum, _zoomSlider.Maximum), factor > 1.0 ? 1 : -1);

            e.Handled = true;
            if (Math.Abs(newZoom - oldZoom) < 0.001) return;

            var image = _getCanvasImage();
            if (image == null) return;

            var mouseInSv    = e.GetPosition(sv);
            var mouseInImage = e.GetPosition(image);

            _isInternalZoomUpdate = true;
            try
            {
                _zoomSlider.Value = newZoom;

                var border = FindParent<Border>(image);
                if (border != null && border.LayoutTransform is ScaleTransform scale)
                {
                    scale.SetCurrentValue(ScaleTransform.ScaleXProperty, newZoom);
                    scale.SetCurrentValue(ScaleTransform.ScaleYProperty, newZoom);
                }

                if (sv.Content is not UIElement capturedGrid) return;

                sv.UpdateLayout();

                var targetPointInGrid = image.TranslatePoint(mouseInImage, capturedGrid);
                sv.ScrollToHorizontalOffset(targetPointInGrid.X - mouseInSv.X);
                sv.ScrollToVerticalOffset(targetPointInGrid.Y - mouseInSv.Y);
            }
            finally
            {
                _isInternalZoomUpdate = false;
            }
        }

        // ── Pan start / move / end ────────────────────────────────────────

        /// <summary>Returns true if a pan gesture was started and the event should be handled.</summary>
        public bool TryStartPan(ScrollViewer sv, MouseButtonEventArgs e, Window window)
        {
            bool isPanGesture = e.ChangedButton == MouseButton.Middle ||
                               (e.ChangedButton == MouseButton.Left && Keyboard.IsKeyDown(Key.Space));
            if (!isPanGesture) return false;

            IsPanning = true;
            _panStartMouse = e.GetPosition(window);
            _panStartScroll = new Point(sv.HorizontalOffset, sv.VerticalOffset);
            sv.CaptureMouse();
            sv.Cursor = Cursors.SizeAll;
            return true;
        }

        public void HandlePanMove(ScrollViewer sv, MouseEventArgs e, Window window)
        {
            if (!IsPanning) return;
            var delta = e.GetPosition(window) - _panStartMouse;
            sv.ScrollToHorizontalOffset(_panStartScroll.X - delta.X);
            sv.ScrollToVerticalOffset(_panStartScroll.Y - delta.Y);
            e.Handled = true;
        }

        public bool TryEndPan(ScrollViewer sv, MouseButtonEventArgs e)
        {
            if (!IsPanning) return false;
            if (e.ChangedButton != MouseButton.Middle && e.ChangedButton != MouseButton.Left) return false;
            IsPanning = false;
            sv.ReleaseMouseCapture();
            sv.Cursor = Cursors.Arrow;
            e.Handled = true;
            return true;
        }

        // ── Centered zoom (for buttons / keyboard) ────────────────────────

        public void ApplyZoomCentered(double factor)
        {
            var sv = FindScrollViewer();
            if (sv == null)
            {
                _zoomSlider.Value = SnapToTick(Math.Clamp(_zoomSlider.Value * factor, _zoomSlider.Minimum, _zoomSlider.Maximum), factor > 1.0 ? 1 : -1);
                return;
            }

            double oldZoom = _zoomSlider.Value;
            double newZoom = SnapToTick(Math.Clamp(oldZoom * factor, _zoomSlider.Minimum, _zoomSlider.Maximum), factor > 1.0 ? 1 : -1);
            
            ApplyAbsoluteZoomCentered(newZoom, oldZoom);
        }

        public void ApplyAbsoluteZoomCentered(double newZoom, double oldZoom)
        {
            var sv = FindScrollViewer();
            if (sv == null) return;

            if (Math.Abs(newZoom - oldZoom) < 0.001) return;

            double cx = sv.ViewportWidth / 2.0;
            double cy = sv.ViewportHeight / 2.0;

            var image = _getCanvasImage();
            if (sv.Content is not UIElement grid || image == null) return;

            Point centerInGrid  = new(sv.HorizontalOffset + cx, sv.VerticalOffset + cy);
            Point centerInImage = grid.TranslatePoint(centerInGrid, image);

            _isInternalZoomUpdate = true;
            try
            {
                _zoomSlider.Value = newZoom;

                var border = FindParent<Border>(image);
                if (border != null && border.LayoutTransform is ScaleTransform scale)
                {
                    scale.SetCurrentValue(ScaleTransform.ScaleXProperty, newZoom);
                    scale.SetCurrentValue(ScaleTransform.ScaleYProperty, newZoom);
                }

                sv.UpdateLayout();

                Point targetPointInGrid = image.TranslatePoint(centerInImage, grid);
                sv.ScrollToHorizontalOffset(targetPointInGrid.X - cx);
                sv.ScrollToVerticalOffset(targetPointInGrid.Y - cy);
            }
            finally
            {
                _isInternalZoomUpdate = false;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────

        internal static double SnapToTick(double value, double direction, double tick, double current, double min, double max)
        {
            if (tick <= 0) return Math.Clamp(value, min, max);
            long ticks = (long)Math.Round(value / tick, MidpointRounding.AwayFromZero);
            double snapped = ticks * tick;

            if (Math.Abs(snapped - current) < 0.001)
                snapped = direction > 0 ? current + tick : current - tick;

            return Math.Clamp(snapped, min, max);
        }

        private double SnapToTick(double value, double direction)
        {
            return SnapToTick(value, direction, _zoomSlider.TickFrequency, _zoomSlider.Value, _zoomSlider.Minimum, _zoomSlider.Maximum);
        }

        private ScrollViewer? FindScrollViewer()
        {
            var image = _getCanvasImage();
            return image != null ? FindParent<ScrollViewer>(image) : null;
        }

        public static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var current = VisualTreeHelper.GetParent(child);
            while (current != null)
            {
                if (current is T t) return t;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}
