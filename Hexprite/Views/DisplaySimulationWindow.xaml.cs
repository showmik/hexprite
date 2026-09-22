using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hexprite.Core;
using Hexprite.Rendering;
using Hexprite.ViewModels;

namespace Hexprite.Views
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "WPF Window resource lifecycle is managed in OnClosed")]
    public partial class DisplaySimulationWindow : Window
    {
        private readonly MainViewModel _vm;
        private WriteableBitmap? _simBitmap;
        private int _zoom = 3;
        private const int MinZoom = 1;
        private const int MaxZoom = 20;
        private const int MaxOutputDimension = 2048;

        private int _renderVersion;
        private volatile int _renderInFlight;
        private CancellationTokenSource? _renderCts;

        // Viewport panning state
        private bool _isPanning;
        private Point _panStartPoint;
        private double _panHorizontalOffset;
        private double _panVerticalOffset;

        private static DisplaySimulationWindow? _currentInstance;

        public static DisplaySimulationWindow ShowOrActivate(MainViewModel vm, Window? owner = null)
        {
            if (_currentInstance != null && _currentInstance.IsLoaded)
            {
                if (_currentInstance.WindowState == WindowState.Minimized)
                    _currentInstance.WindowState = WindowState.Normal;
                _currentInstance.Activate();
                return _currentInstance;
            }

            _currentInstance = new DisplaySimulationWindow(vm);
            var targetOwner = owner ?? Application.Current?.MainWindow;
            if (targetOwner != null && targetOwner != _currentInstance && targetOwner.IsVisible)
            {
                try
                {
                    _currentInstance.Owner = targetOwner;
                }
                catch
                {
                    // Fallback if targetOwner is not in a valid state
                }
            }
            _currentInstance.Closed += (_, _) => _currentInstance = null;
            _currentInstance.Show();
            return _currentInstance;
        }

        public DisplaySimulationWindow(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

            _vm.PropertyChanged += OnVmPropertyChanged;
            Loaded += (_, _) =>
            {
                UpdateZoomUI();
                QueueRender();
            };
            UpdateZoomUI();
        }

        // ── ViewModel change listener ────────────────────────────────────

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.DisplayPreviewBitmap):
                case nameof(MainViewModel.PreviewBitmap):
                case nameof(MainViewModel.PreviewDisplayTypeIndex):
                case nameof(MainViewModel.PreviewRealismStrength):
                case nameof(MainViewModel.PreviewQualityIndex):
                case nameof(MainViewModel.SpriteState):
                case nameof(MainViewModel.SelectedFrameIndex):
                case nameof(MainViewModel.IsPlaying):
                case nameof(MainViewModel.IsCodeStale):
                case nameof(MainViewModel.SelectedLayerIndex):
                case nameof(MainViewModel.Layers):
                case nameof(MainViewModel.IsDirty):
                    QueueRender();
                    break;
            }
        }

        // ── Rendering ────────────────────────────────────────────────────

        private void QueueRender()
        {
            if (!IsLoaded) return;
            int version = Interlocked.Increment(ref _renderVersion);
            if (Interlocked.Exchange(ref _renderInFlight, 1) == 1)
                return;
            DoRender(version);
        }

        private void DoRender(int version)
        {
            var state = _vm.SpriteState;
            if (state == null || state.Frames.Count == 0)
            {
                Interlocked.Exchange(ref _renderInFlight, 0);
                return;
            }

            // Capture state and take immutable snapshots on UI thread
            int srcW = state.Width, srcH = state.Height;
            if (srcW <= 0 || srcH <= 0)
            {
                Interlocked.Exchange(ref _renderInFlight, 0);
                return;
            }

            int zoom = ClampZoom(_zoom, srcW, srcH);
            int outW = srcW * zoom, outH = srcH * zoom;

            int frameIdx = Math.Clamp(_vm.CurrentDisplayFrameIndex, 0, state.Frames.Count - 1);
            var colorMode = state.ColorMode;
            var preset = _vm.GetDisplaySimulationPreset();
            var quality = (PreviewQuality)_vm.PreviewQualityIndex;
            double strength01 = _vm.PreviewRealismStrength / 100.0;
            var (bg, fg) = MainViewModel.GetSimulationColors((DisplayType)_vm.PreviewDisplayTypeIndex);

            // Clone layers metadata
            var layerSnapshots = new LayerState[state.Layers.Count];
            for (int i = 0; i < state.Layers.Count; i++)
            {
                var l = state.Layers[i];
                layerSnapshots[i] = new LayerState
                {
                    Name = l.Name,
                    IsVisible = l.IsVisible,
                    OpacityMode = l.OpacityMode,
                    BlendMode = l.BlendMode,
                };
            }

            // Clone active frame's pixel buffers
            var frame = state.Frames[frameIdx];
            var pixelSnapshots = new IPixelBuffer[frame.LayerPixels.Count];
            for (int i = 0; i < frame.LayerPixels.Count; i++)
            {
                pixelSnapshots[i] = frame.LayerPixels[i]?.Clone() ?? new MonochromePixelBuffer(new bool[srcW * srcH]);
            }

            // Prepare cancellation and dedicated render buffer
            _renderCts?.Cancel();
            _renderCts?.Dispose();
            _renderCts = new CancellationTokenSource();
            var ct = _renderCts.Token;

            EnsureBitmap(outW, outH);
            var buffer = new uint[outW * outH];
            var dispatcher = Dispatcher;

            Task.Run(() =>
            {
                if (ct.IsCancellationRequested) return;

                DisplaySimulationRenderer.Render(
                    srcW, srcH, layerSnapshots, pixelSnapshots, colorMode, selectionService: null,
                    FloatingPasteMode.Transparent, outW, outH, bg, fg, preset, quality,
                    strength01, (double)zoom, isScaleCapped: false, buffer);

                if (ct.IsCancellationRequested) return;

                dispatcher.BeginInvoke(() =>
                {
                    if (!IsLoaded || ct.IsCancellationRequested)
                    {
                        Interlocked.Exchange(ref _renderInFlight, 0);
                        return;
                    }

                    try
                    {
                        _simBitmap?.WritePixels(
                            new Int32Rect(0, 0, outW, outH), buffer, outW * 4, 0);
                        SimImage.Source = _simBitmap;
                    }
                    catch { /* Bitmap may have been disposed during close */ }

                    UpdateStatus(srcW, srcH, zoom);
                    Interlocked.Exchange(ref _renderInFlight, 0);

                    // If new changes arrived while rendering, process latest state
                    if (_renderVersion != version)
                        QueueRender();
                });
            }, ct);
        }

        private void EnsureBitmap(int w, int h)
        {
            if (_simBitmap == null || _simBitmap.PixelWidth != w || _simBitmap.PixelHeight != h)
            {
                _simBitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, palette: null);
            }
        }

        // ── Zoom controls ────────────────────────────────────────────────

        private static int ClampZoom(int zoom, int srcW, int srcH)
        {
            int maxDim = Math.Max(srcW, srcH);
            int maxAllowed = maxDim > 0 ? Math.Clamp(MaxOutputDimension / maxDim, MinZoom, MaxZoom) : MaxZoom;
            return Math.Clamp(zoom, MinZoom, maxAllowed);
        }

        private void SetZoom(int newZoom)
        {
            var state = _vm.SpriteState;
            int srcW = state?.Width ?? 16, srcH = state?.Height ?? 16;
            _zoom = ClampZoom(newZoom, srcW, srcH);
            UpdateZoomUI();
            QueueRender();
        }

        private void UpdateZoomUI()
        {
            TxtZoom.Text = string.Create(CultureInfo.InvariantCulture, $"{_zoom}x");
            BtnZoomOut.IsEnabled = _zoom > MinZoom;
            var state = _vm.SpriteState;
            int srcW = state?.Width ?? 16, srcH = state?.Height ?? 16;
            int maxAllowed = Math.Max(srcW, srcH) > 0
                ? Math.Clamp(MaxOutputDimension / Math.Max(srcW, srcH), MinZoom, MaxZoom)
                : MaxZoom;
            BtnZoomIn.IsEnabled = _zoom < maxAllowed;
        }

        private void UpdateStatus(int srcW, int srcH, int zoom)
        {
            TxtStatus.Text = string.Create(CultureInfo.InvariantCulture, $"Canvas: {srcW}×{srcH}   •   Output: {srcW * zoom}×{srcH * zoom} px");
        }

        // ── Event handlers ───────────────────────────────────────────────

        private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom + 1);
        private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom - 1);

        private void FitZoom_Click(object sender, RoutedEventArgs e)
        {
            var state = _vm.SpriteState;
            if (state == null || state.Width <= 0 || state.Height <= 0) return;

            double availW = Math.Max(50, ViewportScrollViewer.ActualWidth - 32);
            double availH = Math.Max(50, ViewportScrollViewer.ActualHeight - 32);

            int fitW = (int)Math.Floor(availW / state.Width);
            int fitH = (int)Math.Floor(availH / state.Height);
            int fitZoom = Math.Max(1, Math.Min(fitW, fitH));
            SetZoom(fitZoom);
        }

        private CancellationTokenSource? _copyFeedbackCts;

        private async void CopyImage_Click(object sender, RoutedEventArgs e)
        {
            if (_simBitmap != null)
            {
                try
                {
                    Clipboard.SetImage(_simBitmap);
                    var state = _vm.SpriteState;
                    int srcW = state?.Width ?? 16, srcH = state?.Height ?? 16;
                    int zoom = ClampZoom(_zoom, srcW, srcH);
                    TxtStatus.Text = $"✓ Copied {srcW * zoom}×{srcH * zoom} px simulated image to clipboard!";

                    _copyFeedbackCts?.Cancel();
                    _copyFeedbackCts?.Dispose();
                    _copyFeedbackCts = new CancellationTokenSource();
                    var token = _copyFeedbackCts.Token;

                    TxtCopyLabel.Text = "Copied!";
                    try
                    {
                        await Task.Delay(1500, token);
                        if (!token.IsCancellationRequested)
                        {
                            TxtCopyLabel.Text = "Copy Image";
                        }
                    }
                    catch (OperationCanceledException) { }
                }
                catch (Exception ex)
                {
                    TxtStatus.Text = $"Failed to copy image: {ex.Message}";
                }
            }
        }

        private void DisplayType_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (CboDisplayType.SelectedIndex >= 0 && CboDisplayType.SelectedIndex != _vm.PreviewDisplayTypeIndex)
            {
                _vm.PreviewDisplayTypeIndex = CboDisplayType.SelectedIndex;
            }
            QueueRender();
        }

        private void Quality_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (CboQuality.SelectedIndex >= 0 && CboQuality.SelectedIndex != _vm.PreviewQualityIndex)
            {
                _vm.PreviewQualityIndex = CboQuality.SelectedIndex;
            }
            QueueRender();
        }

        private void Strength_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => QueueRender();

        private void Viewport_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                SetZoom(_zoom + (e.Delta > 0 ? 1 : -1));
                e.Handled = true;
            }
        }

        // ── Viewport mouse panning ───────────────────────────────────────

        private void Viewport_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.MiddleButton == MouseButtonState.Pressed ||
                (e.LeftButton == MouseButtonState.Pressed && Keyboard.IsKeyDown(Key.Space)))
            {
                _isPanning = true;
                _panStartPoint = e.GetPosition(this);
                _panHorizontalOffset = ViewportScrollViewer.HorizontalOffset;
                _panVerticalOffset = ViewportScrollViewer.VerticalOffset;
                ViewportScrollViewer.CaptureMouse();
                Cursor = Cursors.SizeAll;
                e.Handled = true;
            }
        }

        private void Viewport_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                Point current = e.GetPosition(this);
                double dx = current.X - _panStartPoint.X;
                double dy = current.Y - _panStartPoint.Y;
                ViewportScrollViewer.ScrollToHorizontalOffset(_panHorizontalOffset - dx);
                ViewportScrollViewer.ScrollToVerticalOffset(_panVerticalOffset - dy);
                e.Handled = true;
            }
        }

        private void Viewport_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning && (e.MiddleButton == MouseButtonState.Released || e.LeftButton == MouseButtonState.Released))
            {
                _isPanning = false;
                ViewportScrollViewer.ReleaseMouseCapture();
                Cursor = Cursors.Arrow;
                e.Handled = true;
            }
        }

        // ── Keyboard shortcuts ───────────────────────────────────────────

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                if (e.Key == Key.OemPlus || e.Key == Key.Add)
                {
                    SetZoom(_zoom + 1);
                    e.Handled = true;
                }
                else if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
                {
                    SetZoom(_zoom - 1);
                    e.Handled = true;
                }
                else if (e.Key == Key.D0 || e.Key == Key.NumPad0)
                {
                    FitZoom_Click(sender, e);
                    e.Handled = true;
                }
                else if (e.Key == Key.C)
                {
                    CopyImage_Click(sender, e);
                    e.Handled = true;
                }
            }
        }

        // ── Title bar caption buttons ────────────────────────────────────

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else if (e.LeftButton == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch
                {
                    // Ignore DragMove exceptions if mouse is captured
                }
            }
        }

        private void CaptionMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void CaptionMaximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CaptionClose_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (MaximizeIcon != null)
            {
                MaximizeIcon.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _copyFeedbackCts?.Cancel();
            _copyFeedbackCts?.Dispose();
            _copyFeedbackCts = null;
            _renderCts?.Cancel();
            _renderCts?.Dispose();
            _renderCts = null;
            _simBitmap = null;
            base.OnClosed(e);
        }
    }
}
