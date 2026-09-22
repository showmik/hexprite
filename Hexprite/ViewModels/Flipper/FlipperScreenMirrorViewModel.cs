using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hexprite.Core;
using Hexprite.Resources.Fonts;
using Hexprite.Services;

namespace Hexprite.ViewModels.Flipper
{
    public enum FlipperStreamSourceMode
    {
        ActiveCanvas,
        AnimationLoop,
        AssetPackStudio,
    }

    public partial class FlipperScreenMirrorViewModel : ObservableObject, IDisposable
    {
        private readonly IFlipperScreenStreamService _streamService;
        private readonly IWorkspaceTabService? _tabService;
        private readonly IDialogService? _dialogService;
        private DispatcherTimer? _uiTimer;
        private readonly uint[] _previewPixelBuffer = new uint[128 * 64];
        private readonly bool[] _normalizedFrameBuffer = new bool[128 * 64];
        private bool[]? _lastFramePixels;
        private bool _disposed;
        private int _animationStepIndex;
        private DateTime _lastAnimStepTime = DateTime.UtcNow;
        private static bool[]? _idleBuffer;

        public ObservableCollection<FlipperDeviceInfo> Devices { get; } = [];

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "WPF DataBinding to ViewModel instance")]
        public IReadOnlyList<ThemePaletteInfo> ThemePalettes => FlipperThemeService.Palettes;

        [ObservableProperty]
        public partial FlipperDeviceInfo? SelectedDevice { get; set; }

        [ObservableProperty]
        public partial bool IsConnected { get; set; }

        [ObservableProperty]
        public partial bool IsConnecting { get; set; }

        [ObservableProperty]
        public partial bool AutoReconnect { get; set; } = true;

        [ObservableProperty]
        public partial bool IsStreamPaused { get; set; }

        [ObservableProperty]
        public partial string StatusText { get; set; } = "Disconnected";

        [ObservableProperty]
        public partial string MetricsText { get; set; } = "0 FPS • 0 frames sent • 0.0 KB/s";

        [ObservableProperty]
        public partial string ThroughputText { get; set; } = "0.0 KB/s";

        [ObservableProperty]
        public partial string ConnectButtonText { get; set; } = "🔌 Connect & Stream";

        [ObservableProperty]
        public partial string ConnectButtonStyleKey { get; set; } = "AccentButtonStyle";

        [ObservableProperty]
        public partial SolidColorBrush StatusLedBrush { get; set; } = new(Color.FromRgb(136, 136, 136));

        [ObservableProperty]
        public partial int TargetFps { get; set; } = 20;

        [ObservableProperty]
        public partial string TargetFpsLabel { get; set; } = "20 FPS";

        [ObservableProperty]
        public partial FlipperStreamSourceMode SourceMode { get; set; } = FlipperStreamSourceMode.ActiveCanvas;

        [ObservableProperty]
        public partial int SelectedPaletteIndex { get; set; }

        [ObservableProperty]
        public partial bool ShowLcdGrid { get; set; } = true;

        [ObservableProperty]
        public partial string SnapshotFeedbackText { get; set; } = string.Empty;
        [ObservableProperty]
        public partial double SpeedMultiplier { get; set; } = 1.0;

        public bool IsAnimationSource
        {
            get => SourceMode == FlipperStreamSourceMode.AnimationLoop;
            set => SourceMode = value ? FlipperStreamSourceMode.AnimationLoop : FlipperStreamSourceMode.ActiveCanvas;
        }

        public bool IsCanvasSourceActive => SourceMode == FlipperStreamSourceMode.ActiveCanvas;
        public bool IsAnimationSourceActive => SourceMode == FlipperStreamSourceMode.AnimationLoop;
        public bool IsStudioSourceActive => SourceMode == FlipperStreamSourceMode.AssetPackStudio;

        public bool IsFps10Active => TargetFps == 10;
        public bool IsFps15Active => TargetFps == 15;
        public bool IsFps20Active => TargetFps == 20;
        public bool IsFps30Active => TargetFps == 30;
        public bool IsFps60Active => TargetFps == 60;

        public bool IsSpeed05xActive => Math.Abs(SpeedMultiplier - 0.5) < 0.05;
        public bool IsSpeed10xActive => Math.Abs(SpeedMultiplier - 1.0) < 0.05;
        public bool IsSpeed15xActive => Math.Abs(SpeedMultiplier - 1.5) < 0.05;
        public bool IsSpeed20xActive => Math.Abs(SpeedMultiplier - 2.0) < 0.05;

        public string NativeFpsText
        {
            get
            {
                var sprite = _tabService?.GetActiveSpriteState();
                int fps = sprite != null && sprite.FrameRateFps > 0 ? sprite.FrameRateFps : 10;
                return $"Native: {fps} FPS";
            }
        }

        public string PauseButtonText => IsStreamPaused ? "▶ Resume" : "⏸ Pause";

        public string PauseButtonToolTip => IsStreamPaused ? "Resume sending live frames to Flipper Zero" : "Pause live frame stream without disconnecting USB";

        public WriteableBitmap PreviewBitmap { get; }

        public Action? RequestClose { get; set; }

        public FlipperScreenMirrorViewModel(
            IFlipperScreenStreamService? streamService = null,
            IWorkspaceTabService? tabService = null,
            IDialogService? dialogService = null)
        {
            _streamService = streamService ?? new FlipperScreenStreamService();
            _tabService = tabService;
            _dialogService = dialogService;

            PreviewBitmap = new WriteableBitmap(128, 64, 96, 96, PixelFormats.Bgra32, palette: null);

            var activeSprite = _tabService?.GetActiveSpriteState();
            if (activeSprite != null && activeSprite.Frames.Count > 1)
            {
                SourceMode = FlipperStreamSourceMode.AnimationLoop;
            }

            _streamService.ConnectionStateChanged += OnStreamConnectionStateChanged;
            _streamService.FpsUpdated += OnStreamFpsUpdated;

            try
            {
                if (Application.Current?.Dispatcher != null)
                {
                    _uiTimer = new DispatcherTimer(DispatcherPriority.Render)
                    {
                        Interval = TimeSpan.FromMilliseconds(Math.Max(8, 1000.0 / Math.Clamp(TargetFps * 2, 20, 120))),
                    };
                    _uiTimer.Tick += OnUiTimerTick;
                    _uiTimer.Start();
                }
            }
            catch
            {
                // Headless test fallback
            }

            _ = ScanDevicesAsync();
        }

        private void OnUiTimerTick(object? sender, EventArgs e) => UiTimerTick();

        public async Task ScanDevicesAsync()
        {
            try
            {
                string? prevPort = SelectedDevice?.PortName;
                var discovered = await _streamService.ScanDevicesAsync();
                if (_disposed) return;

                Devices.Clear();
                foreach (var d in discovered)
                {
                    Devices.Add(d);
                }

                if (prevPort != null)
                {
                    var matching = discovered.Find(d => string.Equals(d.PortName, prevPort, StringComparison.OrdinalIgnoreCase));
                    if (matching != null)
                    {
                        SelectedDevice = matching;
                        return;
                    }
                }

                var flipper = discovered.Find(d => d.IsConnected || d.DisplayName.Contains("Flipper", StringComparison.OrdinalIgnoreCase));
                if (flipper != null)
                {
                    SelectedDevice = flipper;
                }
                else if (Devices.Count > 0)
                {
                    SelectedDevice = Devices[0];
                }
            }
            catch
            {
                // Ignore transient scan errors
            }
        }

        partial void OnAutoReconnectChanged(bool value)
        {
            _streamService.AutoReconnect = value;
        }

        public bool[]? GetFramePixels()
        {
            if (IsStreamPaused && _lastFramePixels != null)
            {
                return _lastFramePixels;
            }

            if (_tabService != null)
            {
                var activeSprite = _tabService.GetActiveSpriteState();

                // 🎬 Autonomous Animation Loop mode
                if (SourceMode == FlipperStreamSourceMode.AnimationLoop && activeSprite != null && activeSprite.Frames.Count > 1)
                {
                    int baseFps = Math.Clamp(activeSprite.FrameRateFps > 0 ? activeSprite.FrameRateFps : 10, 1, 60);
                    double effectiveFps = Math.Clamp(baseFps * SpeedMultiplier, 0.25, 60.0);
                    double intervalMs = 1000.0 / effectiveFps;
                    var now = DateTime.UtcNow;
                    double elapsedMs = (now - _lastAnimStepTime).TotalMilliseconds;
                    if (elapsedMs >= intervalMs)
                    {
                        int steps = (int)(elapsedMs / intervalMs);
                        _lastAnimStepTime = _lastAnimStepTime.AddMilliseconds(steps * intervalMs);
                        if (_lastAnimStepTime > now) _lastAnimStepTime = now;

                        if (activeSprite.FlipperCycle?.FramesOrder is { Length: > 0 } order)
                        {
                            _animationStepIndex = (_animationStepIndex + steps) % order.Length;
                        }
                        else
                        {
                            _animationStepIndex = (_animationStepIndex + steps) % activeSprite.Frames.Count;
                        }
                    }

                    int targetFrame = (activeSprite.FlipperCycle?.FramesOrder is { Length: > 0 } fOrder)
                        ? Math.Clamp(fOrder[_animationStepIndex % fOrder.Length], 0, activeSprite.Frames.Count - 1)
                        : (_animationStepIndex % activeSprite.Frames.Count);

                    var animPx = activeSprite.CompositeFramePixels(targetFrame);
                    FlipperScreenStreamService.NormalizeTo128x64(animPx, activeSprite.Width, activeSprite.Height, _normalizedFrameBuffer);
                    _lastFramePixels = _normalizedFrameBuffer;
                    return _lastFramePixels;
                }

                bool animated = SourceMode != FlipperStreamSourceMode.ActiveCanvas;
                var px = _tabService.GetActiveFramePixels(animated);
                if (px != null)
                {
                    if (px.Length == 128 * 64 && (activeSprite == null || (activeSprite.Width == 128 && activeSprite.Height == 64)))
                    {
                        _lastFramePixels = px;
                        return px;
                    }

                    if (px.Length > 0)
                    {
                        int w = activeSprite?.Width ?? 128;
                        int h = activeSprite?.Height ?? 64;
                        if (activeSprite == null)
                        {
                            // If sprite state unavailable and length isn't 128x64, choose clean power-of-2 factor
                            w = (px.Length % 128 == 0) ? 128 : (px.Length % 64 == 0 ? 64 : 32);
                            h = px.Length / Math.Max(1, w);
                        }
                        FlipperScreenStreamService.NormalizeTo128x64(px, w, h, _normalizedFrameBuffer);
                        _lastFramePixels = _normalizedFrameBuffer;
                        return _lastFramePixels;
                    }
                }
            }

            if (_lastFramePixels == null)
            {
                _lastFramePixels = GetIdleScreenBuffer();
            }

            return _lastFramePixels;
        }

        public static bool[] GetIdleScreenBuffer()
        {
            if (_idleBuffer != null) return _idleBuffer;
            var buf = new bool[128 * 64];

            // Subtle corner crosshairs
            buf[2 * 128 + 2] = buf[2 * 128 + 3] = buf[3 * 128 + 2] = true;
            buf[2 * 128 + 125] = buf[2 * 128 + 124] = buf[3 * 128 + 125] = true;
            buf[61 * 128 + 2] = buf[61 * 128 + 3] = buf[60 * 128 + 2] = true;
            buf[61 * 128 + 125] = buf[61 * 128 + 124] = buf[60 * 128 + 125] = true;

            // Centered standby labels
            FlipperFonts.DrawCenteredString(buf, 128, 64, 20, "FLIPPER MIRROR", FlipperFontType.FontPrimary);
            FlipperFonts.DrawCenteredString(buf, 128, 64, 36, "Open or select sprite", FlipperFontType.FontSecondary);

            _idleBuffer = buf;
            return _idleBuffer;
        }

        public void RenderPreview(bool[] px)
        {
            if (px == null || px.Length < 128 * 64) return;
            _lastFramePixels = px;

            var palette = FlipperThemeService.GetPalette(SelectedPaletteIndex);
            uint colorBg = palette.BgColor;
            uint colorFg = palette.FgColor;
            uint gridDotColor = palette.GridDotColor;

            int len = Math.Min(_previewPixelBuffer.Length, px.Length);
            for (int i = 0; i < len; i++)
            {
                if (px[i])
                {
                    _previewPixelBuffer[i] = colorFg;
                }
                else if (ShowLcdGrid && (i % 2 == 0) && ((i / 128) % 2 == 0))
                {
                    _previewPixelBuffer[i] = gridDotColor;
                }
                else
                {
                    _previewPixelBuffer[i] = colorBg;
                }
            }

            try
            {
                PreviewBitmap.WritePixels(new Int32Rect(0, 0, 128, 64), _previewPixelBuffer, 128 * sizeof(uint), 0);
            }
            catch
            {
                // Headless test fallback
            }
        }

        public void UiTimerTick()
        {
            var px = GetFramePixels();
            if (px != null)
            {
                RenderPreview(px);
            }

            if (IsConnected)
            {
                double kbps = (_streamService.CurrentFps * 1024.0) / 1024.0;
                ThroughputText = string.Create(CultureInfo.InvariantCulture, $"{kbps:F1} KB/s");
                MetricsText = string.Create(CultureInfo.InvariantCulture, $"{_streamService.CurrentFps} FPS • {_streamService.TotalFramesSent} frames sent • {ThroughputText}");
            }
            else
            {
                ThroughputText = "0.0 KB/s";
                var activeSprite = _tabService?.GetActiveSpriteState();
                if (SourceMode == FlipperStreamSourceMode.AnimationLoop && activeSprite != null)
                {
                    int nativeFps = activeSprite.FrameRateFps > 0 ? activeSprite.FrameRateFps : 10;
                    MetricsText = string.Create(CultureInfo.InvariantCulture, $"{nativeFps} FPS (Native) • {SpeedMultiplier:0.#}× Speed • USB Stream {TargetFps} FPS");
                }
                else
                {
                    MetricsText = string.Create(CultureInfo.InvariantCulture, $"USB Stream {TargetFps} FPS • Offline Preview");
                }
            }
        }

        partial void OnSpeedMultiplierChanged(double value)
        {
            OnPropertyChanged(nameof(IsSpeed05xActive));
            OnPropertyChanged(nameof(IsSpeed10xActive));
            OnPropertyChanged(nameof(IsSpeed15xActive));
            OnPropertyChanged(nameof(IsSpeed20xActive));
            _lastAnimStepTime = DateTime.UtcNow;
        }

        partial void OnSelectedPaletteIndexChanged(int value)
        {
            if (_lastFramePixels != null)
            {
                RenderPreview(_lastFramePixels);
            }
        }

        partial void OnShowLcdGridChanged(bool value)
        {
            if (_lastFramePixels != null)
            {
                RenderPreview(_lastFramePixels);
            }
        }

        partial void OnSourceModeChanged(FlipperStreamSourceMode value)
        {
            OnPropertyChanged(nameof(IsAnimationSource));
            OnPropertyChanged(nameof(IsCanvasSourceActive));
            OnPropertyChanged(nameof(IsAnimationSourceActive));
            OnPropertyChanged(nameof(IsStudioSourceActive));
            OnPropertyChanged(nameof(NativeFpsText));
        }

        partial void OnTargetFpsChanged(int value)
        {
            TargetFpsLabel = $"{value} FPS";
            OnPropertyChanged(nameof(IsFps10Active));
            OnPropertyChanged(nameof(IsFps15Active));
            OnPropertyChanged(nameof(IsFps20Active));
            OnPropertyChanged(nameof(IsFps30Active));
            OnPropertyChanged(nameof(IsFps60Active));

            _lastAnimStepTime = DateTime.UtcNow;

            if (_uiTimer != null)
            {
                _uiTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(8, 1000.0 / Math.Clamp(value * 2, 20, 120)));
            }

            if (!IsConnected)
            {
                var activeSprite = _tabService?.GetActiveSpriteState();
                if (SourceMode == FlipperStreamSourceMode.AnimationLoop && activeSprite != null)
                {
                    int nativeFps = activeSprite.FrameRateFps > 0 ? activeSprite.FrameRateFps : 10;
                    MetricsText = string.Create(CultureInfo.InvariantCulture, $"{nativeFps} FPS (Native) • {SpeedMultiplier:0.#}× Speed • USB Stream {value} FPS");
                }
                else
                {
                    MetricsText = string.Create(CultureInfo.InvariantCulture, $"USB Stream {value} FPS • Offline Preview");
                }
            }

            if (_streamService.IsStreaming && !IsStreamPaused)
            {
                _streamService.StartStreaming(GetFramePixels, value);
            }
        }

        [RelayCommand]
        public void SetSpeedMultiplier(string speedStr)
        {
            if (double.TryParse(speedStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
            {
                SpeedMultiplier = Math.Clamp(val, 0.25, 4.0);
            }
        }

        [RelayCommand]
        public void SetFps(string fpsValue)
        {
            if (int.TryParse(fpsValue, out int fps))
            {
                TargetFps = Math.Clamp(fps, 5, 60);
            }
        }

        [RelayCommand]
        public void SetSourceMode(string mode)
        {
            SourceMode = mode switch
            {
                "Animation" => FlipperStreamSourceMode.AnimationLoop,
                "Studio" => FlipperStreamSourceMode.AssetPackStudio,
                _ => FlipperStreamSourceMode.ActiveCanvas,
            };
        }

        [RelayCommand]
        public async Task RefreshDevices()
        {
            await ScanDevicesAsync();
        }

        [RelayCommand]
        public void TogglePauseStream()
        {
            IsStreamPaused = !IsStreamPaused;
            OnPropertyChanged(nameof(PauseButtonText));
            OnPropertyChanged(nameof(PauseButtonToolTip));

            if (IsStreamPaused)
            {
                if (IsConnected)
                {
                    StatusText = $"⏸ Paused ({_streamService.CurrentPort})";
                }
                else
                {
                    StatusText = "⏸ Paused (Offline Preview)";
                }
                StatusLedBrush = new SolidColorBrush(Color.FromRgb(255, 183, 77));
            }
            else
            {
                if (IsConnected)
                {
                    StatusText = $"🟢 Streaming ({_streamService.CurrentPort})";
                    StatusLedBrush = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                }
                else
                {
                    StatusText = "Disconnected";
                    StatusLedBrush = new SolidColorBrush(Color.FromRgb(136, 136, 136));
                }
            }
        }

        [RelayCommand]
        public void SendSnapshot()
        {
            var px = GetFramePixels();
            if (px == null || px.Length < 128 * 64)
            {
                _dialogService?.ShowMessage("No active frame pixels available to capture snapshot.", "Snapshot", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_streamService.IsConnected)
            {
                bool sent = _streamService.SendSingleFrame(px);
                if (sent)
                {
                    SnapshotFeedbackText = "✓ Sent frame snapshot to Flipper!";
                }
                else
                {
                    SnapshotFeedbackText = "⚠️ Failed to send frame.";
                }
            }
            else
            {
                SnapshotFeedbackText = "✓ Frame captured (connect USB to send)";
            }
        }

        [RelayCommand]
        public async Task ToggleConnect()
        {
            if (_streamService.IsConnected)
            {
                _streamService.Disconnect();
            }
            else
            {
                if (SelectedDevice == null || string.IsNullOrWhiteSpace(SelectedDevice.PortName))
                {
                    _dialogService?.ShowMessage("Please select a valid COM port first.", "Screen Mirror", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                IsConnecting = true;
                ConnectButtonText = "⏳ Connecting...";

                bool ok = await _streamService.ConnectAsync(SelectedDevice.PortName);
                if (_disposed) return;

                IsConnecting = false;

                if (ok)
                {
                    IsStreamPaused = false;
                    OnPropertyChanged(nameof(PauseButtonText));
                    OnPropertyChanged(nameof(PauseButtonToolTip));
                    _streamService.StartStreaming(GetFramePixels, TargetFps);
                }
                else
                {
                    _dialogService?.ShowMessage($"Failed to connect to {SelectedDevice.PortName}.\n\nTroubleshooting tips:\n1. Ensure qFlipper, PuTTY, or other serial apps are closed.\n2. Ensure Flipper Zero is unlocked and not in a sub-menu.\n3. Try unplugging and replugging the USB cable.", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OnStreamConnectionStateChanged(object? sender, bool connected)
        {
            void UpdateState()
            {
                if (_disposed) return;

                IsConnected = connected;
                if (connected)
                {
                    IsStreamPaused = false;
                    OnPropertyChanged(nameof(PauseButtonText));
                    OnPropertyChanged(nameof(PauseButtonToolTip));
                    StatusLedBrush = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                    StatusText = $"🟢 Streaming ({_streamService.CurrentPort})";
                    ConnectButtonText = "⏹ Disconnect";
                    ConnectButtonStyleKey = "ModernButtonStyle";
                }
                else
                {
                    IsStreamPaused = false;
                    OnPropertyChanged(nameof(PauseButtonText));
                    OnPropertyChanged(nameof(PauseButtonToolTip));
                    StatusLedBrush = new SolidColorBrush(Color.FromRgb(136, 136, 136));
                    StatusText = "Disconnected";
                    ConnectButtonText = "🔌 Connect & Stream";
                    ConnectButtonStyleKey = "AccentButtonStyle";
                }
            }

            try
            {
                if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                {
                    Application.Current.Dispatcher.Invoke(UpdateState);
                }
                else
                {
                    UpdateState();
                }
            }
            catch
            {
                // Ignore dispatcher shutdown errors
            }
        }

        private void OnStreamFpsUpdated(object? sender, int fps)
        {
            void UpdateFps()
            {
                if (_disposed) return;

                double kbps = (fps * 1024.0) / 1024.0;
                ThroughputText = string.Create(CultureInfo.InvariantCulture, $"{kbps:F1} KB/s");
                MetricsText = string.Create(CultureInfo.InvariantCulture, $"{fps} FPS • {_streamService.TotalFramesSent} frames sent • {ThroughputText}");
            }

            try
            {
                if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                {
                    Application.Current.Dispatcher.Invoke(UpdateFps);
                }
                else
                {
                    UpdateFps();
                }
            }
            catch
            {
                // Ignore dispatcher shutdown errors
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _streamService.ConnectionStateChanged -= OnStreamConnectionStateChanged;
            _streamService.FpsUpdated -= OnStreamFpsUpdated;

            if (_uiTimer != null)
            {
                _uiTimer.Stop();
                _uiTimer.Tick -= OnUiTimerTick;
                _uiTimer = null;
            }
            RequestClose = null;
            Devices.Clear();
            _streamService.Disconnect();
            _streamService.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
