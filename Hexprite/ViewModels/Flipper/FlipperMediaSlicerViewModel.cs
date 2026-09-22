using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hexprite.Core;
using Hexprite.Services;

namespace Hexprite.ViewModels.Flipper
{
    public partial class FlipperMediaSlicerViewModel : ObservableObject, IDisposable
    {
        private readonly IFlipperExportService _exportService;
        private readonly IWorkspaceTabService? _tabService;
        private readonly IDialogService? _dialogService;
        private readonly uint[] _pixelBuffer = new uint[128 * 64];
        private DispatcherTimer? _animTimer;
        private bool _disposed;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "WPF DataBinding to ViewModel instance")]
        public IReadOnlyList<ThemePaletteInfo> ThemePalettes => FlipperThemeService.Palettes;

        [ObservableProperty]
        private int _selectedPaletteIndex;

        [ObservableProperty]
        private bool _showLcdGrid = true;

        [ObservableProperty]
        private BitmapSource? _sourceImage;

        [ObservableProperty]
        private string _sourceInfoText = "No image loaded (Sample grid active)";

        [ObservableProperty]
        private int _frameWidth = 128;

        [ObservableProperty]
        private int _frameHeight = 64;

        [ObservableProperty]
        private int _columns = 4;

        [ObservableProperty]
        private int _rows = 1;

        [ObservableProperty]
        private int _maxFrames = 32;

        [ObservableProperty]
        private int _selectedLayoutIndex;

        [ObservableProperty]
        private int _selectedDitherIndex;

        [ObservableProperty]
        private int _brightnessThreshold = 128;

        [ObservableProperty]
        private int _ditherAmount = 100;

        [ObservableProperty]
        private bool _invertColors;

        [ObservableProperty]
        private int _fps = 8;

        [ObservableProperty]
        private string _fpsLabel = "8 FPS";

        [ObservableProperty]
        private int _currentFrameIndex;

        [ObservableProperty]
        private int _totalFrames = 1;

        public int MaxFrameIndex => Math.Max(0, TotalFrames - 1);

        partial void OnTotalFramesChanged(int value)
        {
            OnPropertyChanged(nameof(MaxFrameIndex));
        }

        [ObservableProperty]
        private string _frameCounterText = "Frame 1 / 1";

        [ObservableProperty]
        private bool _isPlaying = true;

        [ObservableProperty]
        private string _playPauseButtonText = "⏸ Pause";

        public WriteableBitmap LcdBitmap { get; }

        public SpriteState SlicedSprite { get; private set; } = new(128, 64);

        public Action? RequestClose { get; set; }

        public FlipperMediaSlicerViewModel(
            IFlipperExportService? exportService = null,
            IWorkspaceTabService? tabService = null,
            IDialogService? dialogService = null,
            SpriteState? initialSprite = null)
        {
            _exportService = exportService ?? new FlipperExportService();
            _tabService = tabService;
            _dialogService = dialogService;

            LcdBitmap = new WriteableBitmap(128, 64, 96, 96, PixelFormats.Bgra32, null);

            if (initialSprite != null && initialSprite.Frames.Count > 0)
            {
                SlicedSprite = initialSprite;
                TotalFrames = SlicedSprite.Frames.Count;
                SourceInfoText = $"Active Canvas ({initialSprite.Width}×{initialSprite.Height}px, {initialSprite.Frames.Count} frames)";
                RenderCurrentFrame();
            }
            else
            {
                CreateSampleSourceImage();
                Reslice();
            }

            try
            {
                if (Application.Current?.Dispatcher != null)
                {
                    _animTimer = new DispatcherTimer(DispatcherPriority.Render)
                    {
                        Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, Fps))
                    };
                    _animTimer.Tick += OnAnimTimerTick;
                    _animTimer.Start();
                }
            }
            catch
            {
                // Headless test fallback
            }
        }

        private void OnAnimTimerTick(object? sender, EventArgs e) => AdvanceFrame();

        public void CreateSampleSourceImage()
        {
            int w = 512;
            int h = 64;
            var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            uint[] pixels = new uint[w * h];

            for (int f = 0; f < 4; f++)
            {
                int startX = f * 128;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < 128; x++)
                    {
                        int cx = 64 + (f * 8);
                        int cy = 32;
                        int distSq = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                        bool isDot = distSq < (16 + f * 4) * (16 + f * 4);
                        pixels[y * w + (startX + x)] = isDot ? 0xFFFFFFFF : 0xFF111111;
                    }
                }
            }

            bmp.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
            SourceImage = bmp;
            SourceInfoText = "Sample grid active (512×64px, 4 frames)";
        }

        public void LoadImage(BitmapSource bitmap, string? fileName = null)
        {
            ArgumentNullException.ThrowIfNull(bitmap);

            SourceImage = bitmap;
            SourceInfoText = !string.IsNullOrWhiteSpace(fileName)
                ? $"{Path.GetFileName(fileName)} ({bitmap.PixelWidth}×{bitmap.PixelHeight}px)"
                : $"Custom Image ({bitmap.PixelWidth}×{bitmap.PixelHeight}px)";

            if (bitmap.PixelWidth > bitmap.PixelHeight * 2)
            {
                SelectedLayoutIndex = 0; // Horizontal
            }
            else if (bitmap.PixelHeight > bitmap.PixelWidth * 2)
            {
                SelectedLayoutIndex = 1; // Vertical
            }

            Reslice();
        }

        public void Reslice()
        {
            if (SourceImage == null) return;

            var layout = SelectedLayoutIndex switch
            {
                1 => SpriteSheetLayout.VerticalStrip,
                2 => SpriteSheetLayout.Grid,
                _ => SpriteSheetLayout.HorizontalStrip
            };

            var dither = SelectedDitherIndex switch
            {
                1 => BitmapDitheringAlgorithm.Atkinson,
                2 => BitmapDitheringAlgorithm.Bayer,
                3 => BitmapDitheringAlgorithm.Stucki,
                4 => BitmapDitheringAlgorithm.SierraLite,
                5 => BitmapDitheringAlgorithm.Binary,
                _ => BitmapDitheringAlgorithm.FloydSteinberg
            };

            var settings = new MediaSliceSettings
            {
                Layout = layout,
                FrameWidth = Math.Max(1, FrameWidth),
                FrameHeight = Math.Max(1, FrameHeight),
                Columns = Math.Max(1, Columns),
                Rows = Math.Max(1, Rows),
                MaxFrames = Math.Max(1, MaxFrames),
                DitheringAlgorithm = dither,
                BrightnessThreshold = Math.Clamp(BrightnessThreshold, 0, 255),
                DitherAmount = Math.Clamp(DitherAmount, 0, 100),
                InvertColors = InvertColors
            };

            SlicedSprite = FlipperMediaSlicerService.SliceToAnimationSprite(SourceImage, settings);
            TotalFrames = Math.Max(1, SlicedSprite.Frames.Count);
            CurrentFrameIndex = 0;
            UpdateFrameCounterText();
            RenderCurrentFrame();
        }

        public void AdvanceFrame()
        {
            if (!IsPlaying || SlicedSprite.Frames.Count == 0) return;

            CurrentFrameIndex = (CurrentFrameIndex + 1) % SlicedSprite.Frames.Count;
        }

        public void StepBackward()
        {
            IsPlaying = false;
            PlayPauseButtonText = "▶ Play";
            if (SlicedSprite.Frames.Count > 0)
            {
                CurrentFrameIndex = (CurrentFrameIndex - 1 + SlicedSprite.Frames.Count) % SlicedSprite.Frames.Count;
            }
        }

        public void StepForward()
        {
            IsPlaying = false;
            PlayPauseButtonText = "▶ Play";
            if (SlicedSprite.Frames.Count > 0)
            {
                CurrentFrameIndex = (CurrentFrameIndex + 1) % SlicedSprite.Frames.Count;
            }
        }

        public void RenderCurrentFrame()
        {
            if (SlicedSprite.Frames.Count == 0) return;
            if (CurrentFrameIndex >= SlicedSprite.Frames.Count) CurrentFrameIndex = 0;

            var frame = SlicedSprite.Frames[CurrentFrameIndex];
            if (frame.LayerPixels == null || frame.LayerPixels.Count == 0 || frame.LayerPixels[0] == null) return;
            var pixels = frame.LayerPixels[0].GetMonochromeData();
            var palette = FlipperThemeService.GetPalette(SelectedPaletteIndex);
            uint colorBg = palette.BgColor;
            uint colorFg = palette.FgColor;
            uint gridDotColor = palette.GridDotColor;

            int len = Math.Min(_pixelBuffer.Length, pixels.Length);
            for (int i = 0; i < len; i++)
            {
                if (pixels[i])
                {
                    _pixelBuffer[i] = colorFg;
                }
                else if (ShowLcdGrid && (i % 2 == 0) && ((i / 128) % 2 == 0))
                {
                    _pixelBuffer[i] = gridDotColor;
                }
                else
                {
                    _pixelBuffer[i] = colorBg;
                }
            }

            try
            {
                LcdBitmap.WritePixels(new Int32Rect(0, 0, 128, 64), _pixelBuffer, 128 * sizeof(uint), 0);
            }
            catch
            {
                // Headless test fallback
            }
        }

        private void UpdateFrameCounterText()
        {
            FrameCounterText = $"Frame {CurrentFrameIndex + 1} / {TotalFrames}";
        }

        private DispatcherTimer? _debounceTimer;
        private CancellationTokenSource? _resliceCts;

        public void ScheduleReslice()
        {
            if (_debounceTimer == null)
            {
                try
                {
                    if (Application.Current?.Dispatcher != null)
                    {
                        _debounceTimer = new DispatcherTimer
                        {
                            Interval = TimeSpan.FromMilliseconds(40)
                        };
                        _debounceTimer.Tick += (s, e) =>
                        {
                            _debounceTimer.Stop();
                            Reslice();
                        };
                    }
                }
                catch
                {
                    // Headless fallback
                }
            }

            if (_debounceTimer != null)
            {
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
            else
            {
                Reslice();
            }
        }

        partial void OnSelectedLayoutIndexChanged(int value) => ScheduleReslice();
        partial void OnSelectedDitherIndexChanged(int value) => ScheduleReslice();
        partial void OnFrameWidthChanged(int value) => ScheduleReslice();
        partial void OnFrameHeightChanged(int value) => ScheduleReslice();
        partial void OnColumnsChanged(int value) => ScheduleReslice();
        partial void OnRowsChanged(int value) => ScheduleReslice();
        partial void OnMaxFramesChanged(int value) => ScheduleReslice();
        partial void OnBrightnessThresholdChanged(int value) => ScheduleReslice();
        partial void OnDitherAmountChanged(int value) => ScheduleReslice();
        partial void OnInvertColorsChanged(bool value) => ScheduleReslice();
        partial void OnSelectedPaletteIndexChanged(int value) => RenderCurrentFrame();
        partial void OnShowLcdGridChanged(bool value) => RenderCurrentFrame();

        partial void OnCurrentFrameIndexChanged(int value)
        {
            UpdateFrameCounterText();
            RenderCurrentFrame();
        }

        partial void OnFpsChanged(int value)
        {
            FpsLabel = $"{value} FPS";
            if (_animTimer != null)
            {
                _animTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, value));
            }
        }

        partial void OnIsPlayingChanged(bool value)
        {
            PlayPauseButtonText = value ? "⏸ Pause" : "▶ Play";
        }

        [RelayCommand]
        public void TogglePlayPause()
        {
            IsPlaying = !IsPlaying;
        }

        [RelayCommand]
        public void FirstFrame()
        {
            IsPlaying = false;
            PlayPauseButtonText = "▶ Play";
            if (SlicedSprite.Frames.Count > 0)
            {
                CurrentFrameIndex = 0;
            }
        }

        [RelayCommand]
        public void LastFrame()
        {
            IsPlaying = false;
            PlayPauseButtonText = "▶ Play";
            if (SlicedSprite.Frames.Count > 0)
            {
                CurrentFrameIndex = SlicedSprite.Frames.Count - 1;
            }
        }

        [RelayCommand]
        public void PrevFrame()
        {
            StepBackward();
        }

        [RelayCommand]
        public void NextFrame()
        {
            StepForward();
        }

        public void LoadFromFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

            try
            {
                var uri = new Uri(filePath);
                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource = uri;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();

                LoadImage(img, filePath);
            }
            catch (Exception ex)
            {
                _dialogService?.ShowMessage($"Failed to load image:\n{ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void OpenImage()
        {
            const string filter = "Image Files (*.png;*.bmp;*.jpg;*.jpeg;*.gif)|*.png;*.bmp;*.jpg;*.jpeg;*.gif|All Files (*.*)|*.*";
            const string title = "Open Sprite Sheet / Animation Strip";

            string? filePath;
            if (_dialogService != null)
            {
                filePath = _dialogService.ShowOpenFileDialog(filter, title);
            }
            else
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Title = title,
                    Filter = filter
                };
                filePath = ofd.ShowDialog() == true ? ofd.FileName : null;
            }

            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                LoadFromFile(filePath);
            }
        }

        [RelayCommand]
        public void OpenInCanvas()
        {
            if (_tabService == null)
            {
                _dialogService?.ShowMessage("Main application shell not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _tabService.OpenSpritesInTabs([("Sliced Animation", SlicedSprite)]);
            _dialogService?.ShowMessage($"Successfully opened {SlicedSprite.Frames.Count}-frame animation in canvas editor!", "Opened in Canvas", MessageBoxButton.OK, MessageBoxImage.Information);
            RequestClose?.Invoke();
        }

        [RelayCommand]
        public void ExportGif()
        {
            if (SlicedSprite.Frames.Count == 0) return;

            string? savePath = _dialogService?.ShowSaveFileDialog(
                "Animated GIF (*.gif)|*.gif",
                "Export Sliced Animation as Animated GIF",
                "animation.gif");

            if (!string.IsNullOrEmpty(savePath))
            {
                try
                {
                    var palette = FlipperThemeService.GetPalette(SelectedPaletteIndex);
                    var gifService = new GifAnimationExportService();
                    gifService.ExportGif(
                        SlicedSprite,
                        savePath,
                        scale: 3,
                        fgColor: palette.FgColor,
                        bgColor: palette.BgColor,
                        fps: Fps);

                    _dialogService?.ShowMessage($"Successfully exported animated GIF to:\n{savePath}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    _dialogService?.ShowMessage($"Failed to export GIF:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        public void ExportPngSequence()
        {
            if (SlicedSprite.Frames.Count == 0) return;

            string? folder = _dialogService?.ShowOpenFolderDialog("Select Destination Folder for PNG Frames");
            if (!string.IsNullOrEmpty(folder))
            {
                try
                {
                    var palette = FlipperThemeService.GetPalette(SelectedPaletteIndex);
                    uint fgColor = palette.FgColor;
                    uint bgColor = palette.BgColor;

                    for (int f = 0; f < SlicedSprite.Frames.Count; f++)
                    {
                        var frame = SlicedSprite.Frames[f];
                        var px = (frame.LayerPixels != null && frame.LayerPixels.Count > 0 && frame.LayerPixels[0] != null)
                            ? frame.LayerPixels[0].GetMonochromeData()
                            : new bool[128 * 64];
                        var bmp = new WriteableBitmap(128, 64, 96, 96, PixelFormats.Bgra32, null);
                        uint[] pixels = new uint[128 * 64];
                        for (int i = 0; i < pixels.Length && i < px.Length; i++)
                        {
                            pixels[i] = px[i] ? fgColor : bgColor;
                        }
                        bmp.WritePixels(new Int32Rect(0, 0, 128, 64), pixels, 128 * sizeof(uint), 0);

                        string filePath = Path.Combine(folder, $"frame_{f:D3}.png");
                        using var stream = new FileStream(filePath, FileMode.Create);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bmp));
                        encoder.Save(stream);
                    }

                    _dialogService?.ShowMessage($"Successfully exported {SlicedSprite.Frames.Count} PNG frames to:\n{folder}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    _dialogService?.ShowMessage($"Failed to export PNG sequence:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        public void ExportFolder()
        {
            string? folder = _dialogService?.ShowOpenFolderDialog("Select Destination Folder for Flipper Animation");

            if (string.IsNullOrEmpty(folder))
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Select Destination Folder for Flipper Animation"
                };
                if (dialog.ShowDialog() == true)
                {
                    folder = dialog.FolderName;
                }
            }

            if (!string.IsNullOrEmpty(folder))
            {
                try
                {
                    var exportSettings = new FlipperExportSettings
                    {
                        TargetFolder = folder,
                        AnimationName = "anim_sliced",
                        FrameRate = Fps,
                        PassiveFrames = SlicedSprite.Frames.Count,
                        ActiveFrames = 0
                    };

                    _exportService.ExportAnimation(SlicedSprite, exportSettings);
                    _dialogService?.ShowMessage($"Successfully exported {SlicedSprite.Frames.Count} frames to:\n{folder}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    _dialogService?.ShowMessage($"Failed to export animation:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_animTimer != null)
            {
                _animTimer.Stop();
                _animTimer.Tick -= OnAnimTimerTick;
                _animTimer = null;
            }
            if (_debounceTimer != null)
            {
                _debounceTimer.Stop();
                _debounceTimer = null;
            }
            if (_resliceCts != null)
            {
                _resliceCts.Cancel();
                _resliceCts.Dispose();
                _resliceCts = null;
            }
            RequestClose = null;
            SourceImage = null;
            SlicedSprite = new SpriteState(1, 1);
            GC.SuppressFinalize(this);
        }
    }
}
