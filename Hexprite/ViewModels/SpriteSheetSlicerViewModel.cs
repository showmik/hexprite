using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hexprite.Core;
using Hexprite.Services;

namespace Hexprite.ViewModels
{
    public partial class SliceOverlayItem : ObservableObject
    {
        public int Index { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string IndexText => (Index + 1).ToString(CultureInfo.InvariantCulture);

        [ObservableProperty]
        private bool _isActive;
    }

    public partial class FrameThumbnailItem : ObservableObject
    {
        public int Index { get; set; }
        public BitmapSource? Thumbnail { get; set; }
        public bool IsEmpty { get; set; }
        public string Label => $"#{Index + 1}";

        [ObservableProperty]
        private bool _isSelected;
    }

    public partial class SpriteSheetSlicerViewModel : ObservableObject, IDisposable
    {
        private readonly ISpriteSheetSlicerService _slicerService;
        private readonly IWorkspaceTabService? _tabService;
        private readonly IDialogService? _dialogService;
        private readonly IExportService? _exportService;
        private readonly IFlipperExportService? _flipperExportService;
        private readonly ICodeGeneratorService? _codeGeneratorService;

        private uint[] _pixelBuffer = [];
        private DispatcherTimer? _animTimer;
        private DispatcherTimer? _debounceTimer;
        private bool _disposed;
        private bool _isUpdatingFromPreset;
        private bool _isUpdatingGeometry;
        private bool _pingPongForward = true;

        [ObservableProperty]
        private BitmapSource? _sourceImage;

        [ObservableProperty]
        private string _sourceInfoText = "No image loaded (drag & drop or browse)";

        [ObservableProperty]
        private string _sourceDimensionsText = "—";

        [ObservableProperty]
        private int _selectedPresetIndex;

        [ObservableProperty]
        private int _selectedLayoutIndex;

        [ObservableProperty]
        private int _frameWidth = 32;

        [ObservableProperty]
        private int _frameHeight = 32;

        [ObservableProperty]
        private int _columns = 4;

        [ObservableProperty]
        private int _rows = 1;

        [ObservableProperty]
        private int _offsetX;

        [ObservableProperty]
        private int _offsetY;

        [ObservableProperty]
        private int _spacingX;

        [ObservableProperty]
        private int _spacingY;

        [ObservableProperty]
        private int _maxFrames = 64;

        [ObservableProperty]
        private int _selectedCanvasModeIndex;

        [ObservableProperty]
        private int _selectedScalingModeIndex;

        [ObservableProperty]
        private int _canvasWidth = 128;

        [ObservableProperty]
        private int _canvasHeight = 64;

        [ObservableProperty]
        private int _selectedAlignmentIndex;

        [ObservableProperty]
        private int _selectedDitherIndex;

        [ObservableProperty]
        private int _brightnessThreshold = 128;

        [ObservableProperty]
        private int _ditherAmount = 100;

        [ObservableProperty]
        private int _alphaThreshold = 128;

        [ObservableProperty]
        private bool _invertColors;

        [ObservableProperty]
        private bool _useSerpentineScanning;

        [ObservableProperty]
        private bool _useGammaCorrection;

        [ObservableProperty]
        private int _contrast;

        [ObservableProperty]
        private int _brightness;

        [ObservableProperty]
        private bool _autoTrimEmptyFrames;

        [ObservableProperty]
        private bool _skipEmptyFrames;

        [ObservableProperty]
        private BitmapSource? _colorPreviewBitmap;

        [ObservableProperty]
        private int _selectedPreviewModeIndex = 0;

        public bool IsColorPreviewActive => SelectedPreviewModeIndex == 1;
        public bool IsLcdPreviewActive => SelectedPreviewModeIndex == 0;

        public bool IsLcdPreviewMode
        {
            get => SelectedPreviewModeIndex == 0;
            set { if (value) SelectedPreviewModeIndex = 0; }
        }

        public bool IsColorPreviewMode
        {
            get => SelectedPreviewModeIndex == 1;
            set { if (value) SelectedPreviewModeIndex = 1; }
        }

        partial void OnSelectedPreviewModeIndexChanged(int value)
        {
            OnPropertyChanged(nameof(IsColorPreviewActive));
            OnPropertyChanged(nameof(IsLcdPreviewActive));
            OnPropertyChanged(nameof(IsColorPreviewMode));
            OnPropertyChanged(nameof(IsLcdPreviewMode));
        }

        [ObservableProperty]
        private double _previewPanX = 0;

        [ObservableProperty]
        private double _previewPanY = 0;

        [ObservableProperty]
        private int _selectedPreviewZoomIndex = 0;

        public bool IsPreviewZoomFit => SelectedPreviewZoomIndex == 0;
        public bool IsPreviewZoomFixed => SelectedPreviewZoomIndex > 0;

        public double PreviewZoomScale => SelectedPreviewZoomIndex switch
        {
            1 => 1.0,
            2 => 2.0,
            3 => 3.0,
            4 => 4.0,
            5 => 6.0,
            6 => 8.0,
            _ => 1.0
        };

        public string PreviewZoomLabel => SelectedPreviewZoomIndex switch
        {
            1 => "1x",
            2 => "2x",
            3 => "3x",
            4 => "4x",
            5 => "6x",
            6 => "8x",
            _ => "🔍 Fit"
        };

        partial void OnSelectedPreviewZoomIndexChanged(int value)
        {
            if (value == 0)
            {
                PreviewPanX = 0;
                PreviewPanY = 0;
            }
            OnPropertyChanged(nameof(IsPreviewZoomFit));
            OnPropertyChanged(nameof(IsPreviewZoomFixed));
            OnPropertyChanged(nameof(PreviewZoomScale));
            OnPropertyChanged(nameof(PreviewZoomLabel));
        }

        [RelayCommand]
        public void CyclePreviewZoom()
        {
            SelectedPreviewZoomIndex = (SelectedPreviewZoomIndex + 1) % 7;
        }

        [RelayCommand]
        public void ResetPreviewPanAndZoom()
        {
            PreviewPanX = 0;
            PreviewPanY = 0;
            SelectedPreviewZoomIndex = 0;
        }

        public void ZoomPreview(int delta)
        {
            if (delta > 0)
            {
                SelectedPreviewZoomIndex = Math.Min(6, SelectedPreviewZoomIndex + 1);
            }
            else if (delta < 0)
            {
                SelectedPreviewZoomIndex = Math.Max(0, SelectedPreviewZoomIndex - 1);
            }
        }

        [ObservableProperty]
        private bool _onionSkinEnabled;

        [ObservableProperty]
        private double _onionSkinOpacity = 0.35;

        [ObservableProperty]
        private BitmapSource? _onionSkinPrevBitmap;

        [ObservableProperty]
        private BitmapSource? _onionSkinNextBitmap;

        public ObservableCollection<FrameThumbnailItem> FrameThumbnails { get; } = [];

        [ObservableProperty]
        private int _fps = 12;

        [ObservableProperty]
        private string _fpsLabel = "12 FPS";

        [ObservableProperty]
        private int _selectedLoopModeIndex;

        [ObservableProperty]
        private int _currentFrameIndex;

        [ObservableProperty]
        private int _totalFrames = 0;

        public int MaxFrameIndex => Math.Max(0, TotalFrames - 1);

        public string ExportSummaryText
        {
            get
            {
                if (TotalFrames == 0 || SourceImage == null)
                    return "No frames loaded";

                int w = SlicedSprite?.Width ?? CanvasWidth;
                int h = SlicedSprite?.Height ?? CanvasHeight;
                double duration = TotalFrames / (double)Math.Max(1, Fps);
                return $"🎞️ {TotalFrames} Frame{(TotalFrames > 1 ? "s" : "")} • {w}×{h} px • {Fps} FPS ({duration:F2}s loop)";
            }
        }

        public bool IsGeometryTabSelected
        {
            get => SelectedSidebarTabIndex == 0;
            set { if (value) SelectedSidebarTabIndex = 0; }
        }

        public bool IsCanvasTabSelected
        {
            get => SelectedSidebarTabIndex == 1;
            set { if (value) SelectedSidebarTabIndex = 1; }
        }

        public bool IsDitherTabSelected
        {
            get => SelectedSidebarTabIndex == 2;
            set { if (value) SelectedSidebarTabIndex = 2; }
        }

        [ObservableProperty]
        private string _frameCounterText = "No Frames";

        [ObservableProperty]
        private bool _isPlaying = false;

        [ObservableProperty]
        private string _playPauseButtonText = "▶ Play";

        [ObservableProperty]
        private int _selectedThemeIndex;

        [ObservableProperty]
        private double _zoomLevel = 1.0;

        [ObservableProperty]
        private string _zoomLevelLabel = "100%";

        [ObservableProperty]
        private string? _sourceFilePath;

        [ObservableProperty]
        private string? _sourceFileName;

        [ObservableProperty]
        private IReadOnlyList<Int32Rect> _sliceRects = [];

        public ObservableCollection<SliceOverlayItem> SliceOverlays { get; } = [];

        [ObservableProperty]
        private WriteableBitmap _lcdBitmap;

        public SpriteState SlicedSprite { get; private set; } = new(32, 32);

        public Action? RequestClose { get; set; }

        public SpriteSheetSlicerViewModel(
            ISpriteSheetSlicerService? slicerService = null,
            IWorkspaceTabService? tabService = null,
            IDialogService? dialogService = null,
            IExportService? exportService = null,
            IFlipperExportService? flipperExportService = null,
            ICodeGeneratorService? codeGeneratorService = null,
            SpriteState? initialSprite = null,
            SpriteSheetSliceSettings? initialSettings = null)
        {
            _slicerService = slicerService ?? new SpriteSheetSlicerService();
            _tabService = tabService ?? TryResolveMainWindowTabService();
            _dialogService = dialogService;
            _exportService = exportService ?? new ExportService();
            _flipperExportService = flipperExportService ?? new FlipperExportService();
            _codeGeneratorService = codeGeneratorService ?? new CodeGeneratorService();

            _lcdBitmap = new WriteableBitmap(32, 32, 96, 96, PixelFormats.Bgra32, null);

            if (initialSettings != null)
            {
                ApplySettings(initialSettings);
            }

            if (initialSprite != null && initialSprite.Frames.Count > 0)
            {
                SlicedSprite = initialSprite;
                TotalFrames = SlicedSprite.Frames.Count;
                SourceInfoText = $"Active Canvas ({initialSprite.Width}×{initialSprite.Height}px, {initialSprite.Frames.Count} frames)";
                SourceDimensionsText = $"{initialSprite.Width} × {initialSprite.Height} px";
                FrameWidth = initialSprite.Width;
                FrameHeight = initialSprite.Height;
                IsPlaying = true;
                PlayPauseButtonText = "⏸ Pause";
                RecreateLcdBitmap(initialSprite.Width, initialSprite.Height);
                RenderCurrentFrame();
            }

            try
            {
                if (Application.Current?.Dispatcher != null)
                {
                    _animTimer = new DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render)
                    {
                        Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, Fps))
                    };
                    _animTimer.Tick += OnAnimTimerTick;
                    if (IsPlaying)
                    {
                        _animTimer.Start();
                    }
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
            IsPlaying = true;
            PlayPauseButtonText = "⏸ Pause";
            SourceInfoText = "Sample grid active (512×64px, 4 frames)";
            SourceDimensionsText = $"{w} × {h} px";
            FrameWidth = 128;
            FrameHeight = 64;
            Columns = 4;
            Rows = 1;
        }

        public void LoadImage(BitmapSource bitmap, string? fileName = null)
        {
            ArgumentNullException.ThrowIfNull(bitmap);

            SourceImage = bitmap;
            SourceFilePath = fileName;
            SourceFileName = !string.IsNullOrWhiteSpace(fileName) ? Path.GetFileNameWithoutExtension(fileName) : null;
            IsPlaying = true;
            PlayPauseButtonText = "⏸ Pause";
            SourceInfoText = !string.IsNullOrWhiteSpace(fileName)
                ? $"{Path.GetFileName(fileName)} ({bitmap.PixelWidth}×{bitmap.PixelHeight}px)"
                : $"Custom Image ({bitmap.PixelWidth}×{bitmap.PixelHeight}px)";
            SourceDimensionsText = $"{bitmap.PixelWidth} × {bitmap.PixelHeight} px";

            // Heuristic initial layout detection
            if (bitmap.PixelWidth > bitmap.PixelHeight * 2)
            {
                SelectedLayoutIndex = 0; // Horizontal
            }
            else if (bitmap.PixelHeight > bitmap.PixelWidth * 2)
            {
                SelectedLayoutIndex = 1; // Vertical
            }
            else
            {
                SelectedLayoutIndex = 2; // Grid
            }

            // Auto detect standard frame dimensions
            var detected = _slicerService.DetectGrid(bitmap);
            _isUpdatingGeometry = true;
            FrameWidth = detected.SuggestedFrameWidth;
            FrameHeight = detected.SuggestedFrameHeight;
            Columns = detected.Columns;
            Rows = detected.Rows;
            _isUpdatingGeometry = false;

            Reslice();
        }

        public void ApplySettings(SpriteSheetSliceSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            _isUpdatingFromPreset = true;

            SelectedLayoutIndex = settings.Layout switch
            {
                SpriteSheetLayout.VerticalStrip => 1,
                SpriteSheetLayout.Grid => 2,
                SpriteSheetLayout.AutoDetect => 3,
                _ => 0
            };

            FrameWidth = settings.FrameWidth;
            FrameHeight = settings.FrameHeight;
            Columns = settings.Columns;
            Rows = settings.Rows;
            OffsetX = settings.OffsetX;
            OffsetY = settings.OffsetY;
            SpacingX = settings.SpacingX;
            SpacingY = settings.SpacingY;
            MaxFrames = settings.MaxFrames;

            SelectedCanvasModeIndex = settings.CanvasMode == SliceCanvasMode.FixedCanvas ? 1 : 0;
            SelectedScalingModeIndex = settings.ScalingMode switch
            {
                SliceScalingMode.FitAspect => 1,
                SliceScalingMode.FillAspect => 2,
                SliceScalingMode.Stretch => 3,
                _ => 0
            };

            CanvasWidth = settings.CanvasWidth;
            CanvasHeight = settings.CanvasHeight;

            SelectedAlignmentIndex = settings.Alignment switch
            {
                SliceCanvasAlignment.TopLeft => 1,
                SliceCanvasAlignment.TopRight => 2,
                SliceCanvasAlignment.BottomLeft => 3,
                SliceCanvasAlignment.BottomRight => 4,
                _ => 0
            };

            SelectedDitherIndex = settings.DitheringAlgorithm switch
            {
                BitmapDitheringAlgorithm.Atkinson => 1,
                BitmapDitheringAlgorithm.Bayer => 2,
                BitmapDitheringAlgorithm.Stucki => 3,
                BitmapDitheringAlgorithm.SierraLite => 4,
                BitmapDitheringAlgorithm.Binary => 5,
                _ => 0
            };

            BrightnessThreshold = settings.BrightnessThreshold;
            DitherAmount = settings.DitherAmount;
            AlphaThreshold = settings.AlphaThreshold;
            InvertColors = settings.InvertColors;
            UseSerpentineScanning = settings.UseSerpentineScanning;
            UseGammaCorrection = settings.UseGammaCorrection;
            Contrast = settings.Contrast;
            Brightness = settings.Brightness;
            AutoTrimEmptyFrames = settings.AutoTrimEmptyFrames;
            SkipEmptyFrames = settings.SkipEmptyFrames;
            Fps = settings.Fps;

            SelectedLoopModeIndex = settings.LoopMode switch
            {
                PlaybackLoopMode.PingPong => 1,
                PlaybackLoopMode.Once => 2,
                _ => 0
            };

            _isUpdatingFromPreset = false;
            ScheduleReslice();
        }

        public SpriteSheetSliceSettings GetCurrentSettings()
        {
            var layout = SelectedLayoutIndex switch
            {
                1 => SpriteSheetLayout.VerticalStrip,
                2 => SpriteSheetLayout.Grid,
                3 => SpriteSheetLayout.AutoDetect,
                _ => SpriteSheetLayout.HorizontalStrip
            };

            var scalingMode = SelectedScalingModeIndex switch
            {
                1 => SliceScalingMode.FitAspect,
                2 => SliceScalingMode.FillAspect,
                3 => SliceScalingMode.Stretch,
                _ => SliceScalingMode.Crop1To1
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

            var alignment = SelectedAlignmentIndex switch
            {
                1 => SliceCanvasAlignment.TopLeft,
                2 => SliceCanvasAlignment.TopRight,
                3 => SliceCanvasAlignment.BottomLeft,
                4 => SliceCanvasAlignment.BottomRight,
                _ => SliceCanvasAlignment.Center
            };

            var loopMode = SelectedLoopModeIndex switch
            {
                1 => PlaybackLoopMode.PingPong,
                2 => PlaybackLoopMode.Once,
                _ => PlaybackLoopMode.Loop
            };

            return new SpriteSheetSliceSettings
            {
                Layout = layout,
                FrameWidth = Math.Max(1, FrameWidth),
                FrameHeight = Math.Max(1, FrameHeight),
                Columns = Math.Max(0, Columns),
                Rows = Math.Max(0, Rows),
                OffsetX = Math.Max(0, OffsetX),
                OffsetY = Math.Max(0, OffsetY),
                SpacingX = Math.Max(0, SpacingX),
                SpacingY = Math.Max(0, SpacingY),
                MaxFrames = Math.Clamp(MaxFrames, 1, 512),
                CanvasMode = SelectedCanvasModeIndex == 1 ? SliceCanvasMode.FixedCanvas : SliceCanvasMode.FitFrame,
                ScalingMode = scalingMode,
                CanvasWidth = Math.Max(1, CanvasWidth),
                CanvasHeight = Math.Max(1, CanvasHeight),
                Alignment = alignment,
                DitheringAlgorithm = dither,
                BrightnessThreshold = Math.Clamp(BrightnessThreshold, 0, 255),
                DitherAmount = Math.Clamp(DitherAmount, 0, 100),
                AlphaThreshold = Math.Clamp(AlphaThreshold, 0, 255),
                InvertColors = InvertColors,
                UseSerpentineScanning = UseSerpentineScanning,
                UseGammaCorrection = UseGammaCorrection,
                Contrast = Math.Clamp(Contrast, -100, 100),
                Brightness = Math.Clamp(Brightness, -100, 100),
                AutoTrimEmptyFrames = AutoTrimEmptyFrames,
                SkipEmptyFrames = SkipEmptyFrames,
                Fps = Math.Clamp(Fps, 1, 60),
                LoopMode = loopMode
            };
        }

        public bool HasSourceImage => SourceImage != null;
        public bool HasNoSourceImage => SourceImage == null;

        public void Reslice()
        {
            if (SourceImage == null) return;

            var settings = GetCurrentSettings();
            SliceRects = _slicerService.CalculateSliceRects(SourceImage, settings);
            SlicedSprite = _slicerService.SliceToAnimationSprite(SourceImage, settings);
            TotalFrames = Math.Max(1, SlicedSprite.Frames.Count);
            OnPropertyChanged(nameof(MaxFrameIndex));
            OnPropertyChanged(nameof(ExportSummaryText));
            if (CurrentFrameIndex >= TotalFrames) CurrentFrameIndex = 0;

            UpdateSliceOverlays();
            PopulateFrameThumbnails();
            UpdateColorPreview();
            RecreateLcdBitmap(SlicedSprite.Width, SlicedSprite.Height);
            UpdateFrameCounterText();
            RenderCurrentFrame();
        }

        private int _previousActiveOverlayIndex = -1;

        private void UpdateSliceOverlays()
        {
            SliceOverlays.Clear();
            _previousActiveOverlayIndex = CurrentFrameIndex;
            for (int i = 0; i < SliceRects.Count; i++)
            {
                var r = SliceRects[i];
                SliceOverlays.Add(new SliceOverlayItem
                {
                    Index = i,
                    X = r.X,
                    Y = r.Y,
                    Width = r.Width,
                    Height = r.Height,
                    IsActive = i == CurrentFrameIndex
                });
            }
        }

        private static Int32Rect GetSafeCropRect(Int32Rect r, int imgW, int imgH)
        {
            if (imgW <= 0 || imgH <= 0) return new Int32Rect(0, 0, 1, 1);
            int rx = Math.Clamp(r.X, 0, Math.Max(0, imgW - 1));
            int ry = Math.Clamp(r.Y, 0, Math.Max(0, imgH - 1));
            int rw = Math.Clamp(r.Width, 1, Math.Max(1, imgW - rx));
            int rh = Math.Clamp(r.Height, 1, Math.Max(1, imgH - ry));
            return new Int32Rect(rx, ry, rw, rh);
        }

        private int _previousSelectedThumbnailIndex = -1;

        private void PopulateFrameThumbnails()
        {
            FrameThumbnails.Clear();
            _previousSelectedThumbnailIndex = CurrentFrameIndex;
            if (SourceImage == null || SliceRects.Count == 0) return;

            int imgW = SourceImage.PixelWidth;
            int imgH = SourceImage.PixelHeight;

            for (int i = 0; i < SliceRects.Count; i++)
            {
                var r = SliceRects[i];
                BitmapSource? thumb = null;
                try
                {
                    var safeRect = GetSafeCropRect(r, imgW, imgH);
                    var cropped = new CroppedBitmap(SourceImage, safeRect);
                    thumb = CreateThumbnail(cropped, 48);
                }
                catch
                {
                    // Fallback
                }

                FrameThumbnails.Add(new FrameThumbnailItem
                {
                    Index = i,
                    Thumbnail = thumb,
                    IsSelected = (i == CurrentFrameIndex),
                    IsEmpty = false
                });
            }
        }

        private static BitmapSource CreateThumbnail(BitmapSource source, int maxDim = 48)
        {
            if (source.PixelWidth <= maxDim && source.PixelHeight <= maxDim)
            {
                return source;
            }
            double scale = (double)maxDim / Math.Max(source.PixelWidth, source.PixelHeight);
            var transform = new ScaleTransform(scale, scale);
            var transformed = new TransformedBitmap(source, transform);
            if (transformed.CanFreeze) transformed.Freeze();
            return transformed;
        }

        private void SyncActiveSliceOverlay()
        {
            if (_previousActiveOverlayIndex >= 0 && _previousActiveOverlayIndex < SliceOverlays.Count)
            {
                SliceOverlays[_previousActiveOverlayIndex].IsActive = false;
            }

            if (CurrentFrameIndex >= 0 && CurrentFrameIndex < SliceOverlays.Count)
            {
                SliceOverlays[CurrentFrameIndex].IsActive = true;
                _previousActiveOverlayIndex = CurrentFrameIndex;
            }
            else
            {
                _previousActiveOverlayIndex = -1;
            }
        }

        private void SyncSelectedThumbnail()
        {
            if (_previousSelectedThumbnailIndex >= 0 && _previousSelectedThumbnailIndex < FrameThumbnails.Count)
            {
                FrameThumbnails[_previousSelectedThumbnailIndex].IsSelected = false;
            }

            if (CurrentFrameIndex >= 0 && CurrentFrameIndex < FrameThumbnails.Count)
            {
                FrameThumbnails[CurrentFrameIndex].IsSelected = true;
                _previousSelectedThumbnailIndex = CurrentFrameIndex;
            }
            else
            {
                _previousSelectedThumbnailIndex = -1;
            }
        }

        private void UpdateColorPreview()
        {
            if (SourceImage != null && CurrentFrameIndex >= 0 && CurrentFrameIndex < SliceRects.Count)
            {
                try
                {
                    var rect = SliceRects[CurrentFrameIndex];
                    var safeRect = GetSafeCropRect(rect, SourceImage.PixelWidth, SourceImage.PixelHeight);
                    ColorPreviewBitmap = new CroppedBitmap(SourceImage, safeRect);
                }
                catch
                {
                    ColorPreviewBitmap = null;
                }
            }
            else
            {
                ColorPreviewBitmap = null;
            }
            UpdateOnionSkin();
        }

        private void UpdateOnionSkin()
        {
            if (!OnionSkinEnabled || SlicedSprite == null || SlicedSprite.Frames.Count == 0 || SourceImage == null)
            {
                OnionSkinPrevBitmap = null;
                OnionSkinNextBitmap = null;
                return;
            }

            int imgW = SourceImage.PixelWidth;
            int imgH = SourceImage.PixelHeight;

            if (CurrentFrameIndex > 0 && CurrentFrameIndex - 1 < SliceRects.Count)
            {
                try
                {
                    var rect = SliceRects[CurrentFrameIndex - 1];
                    var safeRect = GetSafeCropRect(rect, imgW, imgH);
                    OnionSkinPrevBitmap = new CroppedBitmap(SourceImage, safeRect);
                }
                catch { OnionSkinPrevBitmap = null; }
            }
            else
            {
                OnionSkinPrevBitmap = null;
            }

            if (CurrentFrameIndex + 1 < SliceRects.Count)
            {
                try
                {
                    var rect = SliceRects[CurrentFrameIndex + 1];
                    var safeRect = GetSafeCropRect(rect, imgW, imgH);
                    OnionSkinNextBitmap = new CroppedBitmap(SourceImage, safeRect);
                }
                catch { OnionSkinNextBitmap = null; }
            }
            else
            {
                OnionSkinNextBitmap = null;
            }
        }

        private void RecreateLcdBitmap(int w, int h)
        {
            if (LcdBitmap == null || LcdBitmap.PixelWidth != w || LcdBitmap.PixelHeight != h)
            {
                LcdBitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
                _pixelBuffer = new uint[w * h];
                OnPropertyChanged(nameof(LcdBitmap));
            }
        }

        public void AdvanceFrame()
        {
            if (!IsPlaying || SlicedSprite.Frames.Count == 0) return;

            int count = SlicedSprite.Frames.Count;
            if (count <= 1) return;

            switch (SelectedLoopModeIndex)
            {
                case 1: // Ping-Pong
                    if (_pingPongForward)
                    {
                        if (CurrentFrameIndex + 1 < count)
                        {
                            CurrentFrameIndex++;
                        }
                        else
                        {
                            _pingPongForward = false;
                            CurrentFrameIndex = Math.Max(0, CurrentFrameIndex - 1);
                        }
                    }
                    else
                    {
                        if (CurrentFrameIndex > 0)
                        {
                            CurrentFrameIndex--;
                        }
                        else
                        {
                            _pingPongForward = true;
                            CurrentFrameIndex = Math.Min(count - 1, CurrentFrameIndex + 1);
                        }
                    }
                    break;

                case 2: // Play Once
                    if (CurrentFrameIndex + 1 < count)
                    {
                        CurrentFrameIndex++;
                    }
                    else
                    {
                        IsPlaying = false;
                    }
                    break;

                default: // Standard Loop
                    CurrentFrameIndex = (CurrentFrameIndex + 1) % count;
                    break;
            }
        }

        public void StepBackward()
        {
            IsPlaying = false;
            if (SlicedSprite.Frames.Count > 0)
            {
                CurrentFrameIndex = (CurrentFrameIndex - 1 + SlicedSprite.Frames.Count) % SlicedSprite.Frames.Count;
            }
        }

        public void StepForward()
        {
            IsPlaying = false;
            if (SlicedSprite.Frames.Count > 0)
            {
                CurrentFrameIndex = (CurrentFrameIndex + 1) % SlicedSprite.Frames.Count;
            }
        }

        public void RenderCurrentFrame()
        {
            if (SlicedSprite.Frames.Count == 0 || LcdBitmap == null) return;
            if (CurrentFrameIndex < 0 || CurrentFrameIndex >= SlicedSprite.Frames.Count) CurrentFrameIndex = 0;

            var frame = SlicedSprite.Frames[CurrentFrameIndex];
            if (frame.LayerPixels.Count == 0) return;

            var pixels = frame.LayerPixels[0].GetMonochromeData();
            int w = SlicedSprite.Width;
            int h = SlicedSprite.Height;

            if (_pixelBuffer.Length != w * h)
            {
                _pixelBuffer = new uint[w * h];
            }

            (uint colorBg, uint colorFg) = GetThemeColors(SelectedThemeIndex);

            int len = Math.Min(_pixelBuffer.Length, pixels.Length);
            for (int i = 0; i < len; i++)
            {
                _pixelBuffer[i] = pixels[i] ? colorFg : colorBg;
            }

            try
            {
                LcdBitmap.WritePixels(new Int32Rect(0, 0, w, h), _pixelBuffer, w * sizeof(uint), 0);
            }
            catch
            {
                // Headless test fallback
            }
        }

        private static (uint Bg, uint Fg) GetThemeColors(int themeIndex)
        {
            return themeIndex switch
            {
                1 => (0xFFF0F0F0, 0xFF111111), // Light Monochrome (Black on Light Gray)
                2 => (0xFFFF8200, 0xFF000000), // Flipper Classic (Black on Flipper Orange)
                3 => (0xFF0A1C0A, 0xFF33FF33), // Matrix Green (Neon Green on Dark)
                4 => (0xFF1F1200, 0xFFFFB000), // Amber CRT (Warm Amber on Dark)
                5 => (0xFF002244, 0xFFFFFFFF), // OLED Blue (White on Deep Blue)
                _ => (0xFF11141E, 0xFFFFFFFF)  // Dark Monochrome (White on Dark Surface)
            };
        }

        private void UpdateFrameCounterText()
        {
            FrameCounterText = TotalFrames > 0
                ? $"Frame {CurrentFrameIndex + 1} / {TotalFrames}"
                : "No Frames";
        }

        public void ScheduleReslice()
        {
            if (_isUpdatingFromPreset) return;

            if (_debounceTimer == null)
            {
                try
                {
                    if (Application.Current?.Dispatcher != null)
                    {
                        _debounceTimer = new DispatcherTimer
                        {
                            Interval = TimeSpan.FromMilliseconds(30)
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

        partial void OnSourceImageChanged(BitmapSource? value)
        {
            OnPropertyChanged(nameof(HasSourceImage));
            OnPropertyChanged(nameof(HasNoSourceImage));
        }

        partial void OnCurrentFrameIndexChanged(int value)
        {
            SyncActiveSliceOverlay();
            SyncSelectedThumbnail();
            UpdateColorPreview();
            UpdateFrameCounterText();
            RenderCurrentFrame();
        }

        partial void OnSelectedPresetIndexChanged(int value)
        {
            if (_isUpdatingFromPreset) return;
            _isUpdatingFromPreset = true;

            switch (value)
            {
                case 1: // Flipper Zero
                    SelectedCanvasModeIndex = 1;
                    SelectedScalingModeIndex = 0; // 1:1 Crop
                    CanvasWidth = 128;
                    CanvasHeight = 64;
                    SelectedAlignmentIndex = 0; // Center
                    SelectedThemeIndex = 2; // Flipper Orange
                    Fps = 12;
                    break;
                case 2: // SSD1306 128x64
                    SelectedCanvasModeIndex = 1;
                    SelectedScalingModeIndex = 0;
                    CanvasWidth = 128;
                    CanvasHeight = 64;
                    SelectedAlignmentIndex = 0;
                    SelectedThemeIndex = 0; // Dark
                    break;
                case 3: // SSD1306 128x32
                    SelectedCanvasModeIndex = 1;
                    SelectedScalingModeIndex = 0;
                    CanvasWidth = 128;
                    CanvasHeight = 32;
                    SelectedAlignmentIndex = 0;
                    SelectedThemeIndex = 0;
                    break;
                case 4: // Pico / Arduino 128x128
                    SelectedCanvasModeIndex = 1;
                    SelectedScalingModeIndex = 0;
                    CanvasWidth = 128;
                    CanvasHeight = 128;
                    SelectedAlignmentIndex = 0;
                    break;
                case 5: // Nokia 5110 84x48
                    SelectedCanvasModeIndex = 1;
                    SelectedScalingModeIndex = 0;
                    CanvasWidth = 84;
                    CanvasHeight = 48;
                    SelectedAlignmentIndex = 0;
                    SelectedThemeIndex = 3; // Matrix / LCD Green
                    break;
                case 6: // Micro:bit 5x5
                    SelectedCanvasModeIndex = 1;
                    SelectedScalingModeIndex = 1; // Fit Aspect
                    CanvasWidth = 5;
                    CanvasHeight = 5;
                    SelectedAlignmentIndex = 0;
                    SelectedThemeIndex = 4; // Amber CRT
                    break;
                case 7: // Custom Canvas Size
                    SelectedCanvasModeIndex = 1;
                    break;
                default: // Fit to Frame
                    SelectedCanvasModeIndex = 0;
                    SelectedScalingModeIndex = 0;
                    break;
            }

            _isUpdatingFromPreset = false;
            ScheduleReslice();
        }

        partial void OnSelectedLayoutIndexChanged(int value)
        {
            if (_isUpdatingGeometry || SourceImage == null) return;
            _isUpdatingGeometry = true;

            int imgW = SourceImage.PixelWidth;
            int imgH = SourceImage.PixelHeight;

            switch (value)
            {
                case 0: // HorizontalStrip
                    Rows = 1;
                    if (FrameWidth > 0)
                        Columns = Math.Max(1, (imgW - OffsetX + SpacingX) / (FrameWidth + SpacingX));
                    break;
                case 1: // VerticalStrip
                    Columns = 1;
                    if (FrameHeight > 0)
                        Rows = Math.Max(1, (imgH - OffsetY + SpacingY) / (FrameHeight + SpacingY));
                    break;
                case 2: // Grid
                    if (FrameWidth > 0)
                        Columns = Math.Max(1, (imgW - OffsetX + SpacingX) / (FrameWidth + SpacingX));
                    if (FrameHeight > 0)
                        Rows = Math.Max(1, (imgH - OffsetY + SpacingY) / (FrameHeight + SpacingY));
                    break;
                case 3: // AutoDetect (Alpha Islands)
                    break;
            }

            _isUpdatingGeometry = false;
            ScheduleReslice();
        }

        partial void OnFrameWidthChanged(int value)
        {
            if (!_isUpdatingGeometry && SourceImage != null && value > 0 && SelectedLayoutIndex != 1)
            {
                _isUpdatingGeometry = true;
                int imgW = SourceImage.PixelWidth;
                int step = value + SpacingX;
                Columns = step > 0 ? Math.Max(1, (imgW - OffsetX + SpacingX) / step) : 1;
                _isUpdatingGeometry = false;
            }
            ScheduleReslice();
        }

        partial void OnFrameHeightChanged(int value)
        {
            if (!_isUpdatingGeometry && SourceImage != null && value > 0 && SelectedLayoutIndex != 0)
            {
                _isUpdatingGeometry = true;
                int imgH = SourceImage.PixelHeight;
                int step = value + SpacingY;
                Rows = step > 0 ? Math.Max(1, (imgH - OffsetY + SpacingY) / step) : 1;
                _isUpdatingGeometry = false;
            }
            ScheduleReslice();
        }

        partial void OnColumnsChanged(int value)
        {
            if (!_isUpdatingGeometry && SourceImage != null && value > 0 && SelectedLayoutIndex != 1)
            {
                _isUpdatingGeometry = true;
                int imgW = SourceImage.PixelWidth;
                FrameWidth = Math.Max(1, (imgW - OffsetX - (value - 1) * SpacingX) / value);
                _isUpdatingGeometry = false;
            }
            ScheduleReslice();
        }

        partial void OnRowsChanged(int value)
        {
            if (!_isUpdatingGeometry && SourceImage != null && value > 0 && SelectedLayoutIndex != 0)
            {
                _isUpdatingGeometry = true;
                int imgH = SourceImage.PixelHeight;
                FrameHeight = Math.Max(1, (imgH - OffsetY - (value - 1) * SpacingY) / value);
                _isUpdatingGeometry = false;
            }
            ScheduleReslice();
        }

        partial void OnSelectedScalingModeIndexChanged(int value) => ScheduleReslice();
        partial void OnSelectedDitherIndexChanged(int value) => ScheduleReslice();
        partial void OnSelectedCanvasModeIndexChanged(int value) => ScheduleReslice();
        partial void OnSelectedAlignmentIndexChanged(int value) => ScheduleReslice();
        partial void OnSelectedThemeIndexChanged(int value) => RenderCurrentFrame();
        partial void OnOffsetXChanged(int value) => ScheduleReslice();
        partial void OnOffsetYChanged(int value) => ScheduleReslice();
        partial void OnSpacingXChanged(int value) => ScheduleReslice();
        partial void OnSpacingYChanged(int value) => ScheduleReslice();
        partial void OnMaxFramesChanged(int value) => ScheduleReslice();
        partial void OnCanvasWidthChanged(int value) => ScheduleReslice();
        partial void OnCanvasHeightChanged(int value) => ScheduleReslice();
        partial void OnBrightnessThresholdChanged(int value) => ScheduleReslice();
        partial void OnDitherAmountChanged(int value) => ScheduleReslice();
        partial void OnAlphaThresholdChanged(int value) => ScheduleReslice();
        partial void OnInvertColorsChanged(bool value) => ScheduleReslice();
        partial void OnUseSerpentineScanningChanged(bool value) => ScheduleReslice();
        partial void OnUseGammaCorrectionChanged(bool value) => ScheduleReslice();
        partial void OnContrastChanged(int value) => ScheduleReslice();
        partial void OnBrightnessChanged(int value) => ScheduleReslice();
        partial void OnAutoTrimEmptyFramesChanged(bool value) => ScheduleReslice();
        partial void OnSkipEmptyFramesChanged(bool value) => ScheduleReslice();
        partial void OnOnionSkinEnabledChanged(bool value) => UpdateOnionSkin();
        partial void OnOnionSkinOpacityChanged(double value) => UpdateOnionSkin();

        [RelayCommand]
        public void AutoDetectIslands()
        {
            _isUpdatingGeometry = true;
            OffsetX = 0;
            OffsetY = 0;
            SpacingX = 0;
            SpacingY = 0;
            SelectedLayoutIndex = 3;
            _isUpdatingGeometry = false;
            Reslice();
        }

        [RelayCommand]
        public void ToggleOnionSkin()
        {
            OnionSkinEnabled = !OnionSkinEnabled;
        }

        [RelayCommand]
        public void CopyFrame(int? frameIndex = null)
        {
            int idx = frameIndex ?? CurrentFrameIndex;
            if (SourceImage == null || idx < 0 || idx >= SliceRects.Count) return;

            try
            {
                var rect = SliceRects[idx];
                var safeRect = GetSafeCropRect(rect, SourceImage.PixelWidth, SourceImage.PixelHeight);
                var cropped = new CroppedBitmap(SourceImage, safeRect);
                Clipboard.SetImage(cropped);
                _dialogService?.ShowMessage($"Frame #{idx + 1} copied to clipboard as image!", "Copied Frame", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _dialogService?.ShowMessage($"Failed to copy frame:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void ExportJsonMetadata()
        {
            if (SourceImage == null || SliceRects.Count == 0) return;

            string? savePath = _dialogService?.ShowSaveFileDialog(
                "JSON Metadata (*.json)|*.json",
                "Export Sprite Sheet JSON Metadata",
                "spritesheet.json");

            if (!string.IsNullOrEmpty(savePath))
            {
                try
                {
                    string? dir = Path.GetDirectoryName(savePath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    int durationMs = (int)Math.Round(1000.0 / Math.Max(1, Fps));
                    var framesList = new List<object>();

                    for (int i = 0; i < SliceRects.Count; i++)
                    {
                        var r = SliceRects[i];
                        framesList.Add(new
                        {
                            filename = $"frame_{i:D3}.png",
                            frame = new { x = r.X, y = r.Y, w = r.Width, h = r.Height },
                            rotated = false,
                            trimmed = false,
                            spriteSourceSize = new { x = 0, y = 0, w = r.Width, h = r.Height },
                            sourceSize = new { w = r.Width, h = r.Height },
                            duration = durationMs
                        });
                    }

                    var jsonObject = new
                    {
                        frames = framesList,
                        meta = new
                        {
                            app = "Hexprite Sprite Sheet & Atlas Slicer Studio",
                            version = "1.0",
                            image = "spritesheet.png",
                            format = "RGBA8888",
                            size = new { w = SourceImage.PixelWidth, h = SourceImage.PixelHeight },
                            scale = "1",
                            frameTags = new[]
                            {
                                new { name = "all", from = 0, to = Math.Max(0, SliceRects.Count - 1), direction = "forward" }
                            }
                        }
                    };

                    string json = System.Text.Json.JsonSerializer.Serialize(jsonObject, s_jsonOptions);

                    SafeFileIo.WriteAllTextAtomic(savePath, json);
                    _dialogService?.ShowMessage($"Successfully exported JSON metadata to:\n{savePath}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    _dialogService?.ShowMessage($"Failed to export JSON metadata:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private static readonly System.Text.Json.JsonSerializerOptions s_jsonOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        partial void OnZoomLevelChanged(double value)
        {
            ZoomLevelLabel = $"{(int)(Math.Round(value, 2) * 100)}%";
        }

        partial void OnFpsChanged(int value)
        {
            FpsLabel = $"{value} FPS";
            if (_animTimer != null)
            {
                _animTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, value));
            }
            OnPropertyChanged(nameof(ExportSummaryText));
        }

        partial void OnIsPlayingChanged(bool value)
        {
            PlayPauseButtonText = value ? "⏸ Pause" : "▶ Play";
            if (value)
            {
                if (SelectedLoopModeIndex == 2 && SlicedSprite.Frames.Count > 0 && CurrentFrameIndex >= SlicedSprite.Frames.Count - 1)
                {
                    CurrentFrameIndex = 0;
                }
                _animTimer?.Start();
            }
            else
            {
                _animTimer?.Stop();
            }
        }

        [RelayCommand]
        public void TogglePlayPause()
        {
            if (!IsPlaying && SelectedLoopModeIndex == 2 && SlicedSprite.Frames.Count > 0 && CurrentFrameIndex >= SlicedSprite.Frames.Count - 1)
            {
                CurrentFrameIndex = 0;
            }
            IsPlaying = !IsPlaying;
        }

        [RelayCommand]
        public void PrevFrame() => StepBackward();

        [RelayCommand]
        public void NextFrame() => StepForward();

        [RelayCommand]
        public void SelectSlice(int index)
        {
            if (index >= 0 && index < TotalFrames)
            {
                CurrentFrameIndex = index;
            }
        }

        [ObservableProperty]
        private int _selectedSidebarTabIndex;

        partial void OnSelectedSidebarTabIndexChanged(int value)
        {
            OnPropertyChanged(nameof(IsGeometryTabSelected));
            OnPropertyChanged(nameof(IsCanvasTabSelected));
            OnPropertyChanged(nameof(IsDitherTabSelected));
        }

        [RelayCommand]
        public void SelectSidebarTab(string? tabIndexStr)
        {
            if (int.TryParse(tabIndexStr, out int idx))
            {
                SelectedSidebarTabIndex = idx;
            }
        }

        [RelayCommand]
        public void ResetContrast() => Contrast = 0;

        [RelayCommand]
        public void ResetBrightness() => Brightness = 0;

        [RelayCommand]
        public void FirstFrame()
        {
            IsPlaying = false;
            if (SlicedSprite.Frames.Count > 0)
            {
                CurrentFrameIndex = 0;
            }
        }

        [RelayCommand]
        public void LastFrame()
        {
            IsPlaying = false;
            if (SlicedSprite.Frames.Count > 0)
            {
                CurrentFrameIndex = SlicedSprite.Frames.Count - 1;
            }
        }

        [RelayCommand]
        public void SetFps(string? fpsStr)
        {
            if (int.TryParse(fpsStr, out int f))
            {
                Fps = Math.Clamp(f, 1, 60);
                OnPropertyChanged(nameof(ExportSummaryText));
            }
        }

        [RelayCommand]
        public void ZoomIn() => ZoomLevel = Math.Min(8.0, ZoomLevel * 2.0);

        [RelayCommand]
        public void ZoomOut() => ZoomLevel = Math.Max(0.25, ZoomLevel / 2.0);

        [RelayCommand]
        public void ResetZoom() => ZoomLevel = 1.0;

        [RelayCommand]
        public void ResetThreshold() => BrightnessThreshold = 128;

        [RelayCommand]
        public void ResetDitherAmount() => DitherAmount = 100;

        [RelayCommand]
        public void ZoomToFit()
        {
            if (SourceImage != null && SourceImage.PixelWidth > 0 && SourceImage.PixelHeight > 0)
            {
                double scaleX = 580.0 / SourceImage.PixelWidth;
                double scaleY = 320.0 / SourceImage.PixelHeight;
                double fit = Math.Min(scaleX, scaleY);
                ZoomLevel = Math.Clamp(Math.Round(fit * 4.0) / 4.0, 0.25, 8.0);
            }
            else
            {
                ZoomLevel = 1.0;
            }
        }

        [RelayCommand]
        public void AutoDetectGrid()
        {
            if (SourceImage == null) return;
            var detected = _slicerService.DetectGrid(SourceImage);
            _isUpdatingGeometry = true;
            OffsetX = 0;
            OffsetY = 0;
            SpacingX = 0;
            SpacingY = 0;
            FrameWidth = detected.SuggestedFrameWidth;
            FrameHeight = detected.SuggestedFrameHeight;
            Columns = detected.Columns;
            Rows = detected.Rows;
            if (detected.Rows > 1 && detected.Columns > 1)
            {
                SelectedLayoutIndex = 2; // Grid
            }
            else if (detected.Rows > 1 && detected.Columns == 1)
            {
                SelectedLayoutIndex = 1; // Vertical Strip
            }
            else
            {
                SelectedLayoutIndex = 0; // Horizontal Strip
            }
            _isUpdatingGeometry = false;
            Reslice();
        }

        public void HandleFileDrop(string[]? filePaths)
        {
            if (filePaths == null || filePaths.Length == 0) return;
            string path = filePaths[0];

            if (!File.Exists(path)) return;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".png" or ".bmp" or ".jpg" or ".jpeg" or ".gif" or ".xbm")
            {
                LoadImageFromFile(path);
            }
        }

        public void LoadImageFromFile(string filePath)
        {
            if (!File.Exists(filePath)) return;

            try
            {
                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (ext == ".gif")
                {
                    var gifStrip = _slicerService.CreateStripFromGif(filePath);
                    if (gifStrip != null)
                    {
                        LoadImage(gifStrip, filePath);
                        return;
                    }
                }

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
            const string filter = "Image Files (*.png;*.bmp;*.jpg;*.jpeg;*.gif;*.xbm)|*.png;*.bmp;*.jpg;*.jpeg;*.gif;*.xbm|All Files (*.*)|*.*";
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

            if (!string.IsNullOrEmpty(filePath))
            {
                LoadImageFromFile(filePath);
            }
        }

        [RelayCommand]
        public void ClearSourceImage()
        {
            SourceImage = null;
            SourceFilePath = null;
            SourceFileName = null;
            SlicedSprite = new(32, 32);
            SliceRects = [];
            SliceOverlays.Clear();
            FrameThumbnails.Clear();
            TotalFrames = 0;
            CurrentFrameIndex = 0;
            SourceInfoText = "No image loaded (drag & drop or browse)";
            SourceDimensionsText = "—";
            FrameCounterText = "No Frames";
            ColorPreviewBitmap = null;
            IsPlaying = false;
            PlayPauseButtonText = "▶ Play";
            RecreateLcdBitmap(32, 32);
            OnPropertyChanged(nameof(ExportSummaryText));
        }

        [RelayCommand]
        public void CopyCode()
        {
            if (SlicedSprite == null || SlicedSprite.Frames.Count == 0 || _codeGeneratorService == null) return;

            try
            {
                int w = Math.Max(1, SlicedSprite.Width);
                int h = Math.Max(1, SlicedSprite.Height);
                var frames = SlicedSprite.Frames
                    .Select(f => f.LayerPixels.Count > 0 ? f.LayerPixels[0].GetMonochromeData() : new bool[w * h])
                    .ToList();

                var exportSettings = new ExportSettings
                {
                    SpriteName = !string.IsNullOrWhiteSpace(SourceFileName) ? CodeGeneratorService.SanitiseName(SourceFileName) : "sliced_sprite",
                    Format = ExportFormat.AdafruitGfx,
                    IncludeUsageComment = true,
                    IncludeDimensionConstants = true
                };

                string code = _codeGeneratorService.GenerateCode(
                    frames,
                    SlicedSprite.Width,
                    SlicedSprite.Height,
                    exportSettings,
                    isFloating: false,
                    floatingPixels: null,
                    floatX: 0, floatY: 0, floatW: 0, floatH: 0);

                Clipboard.SetText(code);
                _dialogService?.ShowMessage("Generated C/C++ byte array copied to clipboard!", "Copied to Clipboard", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _dialogService?.ShowMessage($"Failed to generate code:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void OpenInCanvas()
        {
            var tabService = _tabService ?? TryResolveMainWindowTabService();
            if (tabService == null)
            {
                _dialogService?.ShowMessage("Main application workspace not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (SourceImage == null || SlicedSprite == null || SlicedSprite.Frames.Count == 0)
            {
                _dialogService?.ShowMessage("No sliced frames available to import.", "Import Slices", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var clonedSprite = SlicedSprite.Clone();
            clonedSprite.NormalizeLayerState();
            string animName = !string.IsNullOrWhiteSpace(SourceFileName)
                ? SourceFileName
                : "Sliced Animation";

            clonedSprite.IsAnimationEnabled = true;
            clonedSprite.FrameRateFps = Math.Clamp(Fps, 1, 60);

            tabService.OpenSpritesInTabs([(animName, clonedSprite)]);
            RequestClose?.Invoke();
        }

        [RelayCommand]
        public void OpenAsSeparateTabs()
        {
            var tabService = _tabService ?? TryResolveMainWindowTabService();
            if (tabService == null)
            {
                _dialogService?.ShowMessage("Main application workspace not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (SourceImage == null)
            {
                _dialogService?.ShowMessage("No source image loaded to slice.", "Import Slices", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var settings = GetCurrentSettings();
            var individualSprites = _slicerService.SliceToIndividualSprites(SourceImage, settings);
            if (individualSprites.Count == 0)
            {
                _dialogService?.ShowMessage("No slices generated to open.", "Import Slices", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string prefix = !string.IsNullOrWhiteSpace(SourceFileName)
                ? SourceFileName
                : "Slice";

            var tuples = individualSprites
                .Select((s, idx) => ($"{prefix} {idx + 1}", s))
                .ToList();

            tabService.OpenSpritesInTabs(tuples);
            RequestClose?.Invoke();
        }

        private static IWorkspaceTabService? TryResolveMainWindowTabService()
        {
            try
            {
                if (Application.Current?.Dispatcher != null && Application.Current.Dispatcher.CheckAccess())
                {
                    return Application.Current.MainWindow?.DataContext as IWorkspaceTabService;
                }
            }
            catch
            {
                // Ignore cross-thread or headless test exceptions
            }
            return null;
        }

        private List<BitmapSource> GetCroppedColorFrames()
        {
            var frames = new List<BitmapSource>();
            if (SourceImage == null || SliceRects.Count == 0) return frames;

            int imgW = SourceImage.PixelWidth;
            int imgH = SourceImage.PixelHeight;

            foreach (var rect in SliceRects)
            {
                try
                {
                    var safeRect = GetSafeCropRect(rect, imgW, imgH);
                    frames.Add(new CroppedBitmap(SourceImage, safeRect));
                }
                catch
                {
                    // Fallback
                }
            }
            return frames;
        }

        private static System.Windows.Media.Color ColorFromUint(uint u)
        {
            return System.Windows.Media.Color.FromArgb((byte)(u >> 24), (byte)(u >> 16), (byte)(u >> 8), (byte)u);
        }

        [RelayCommand]
        public void ExportGif()
        {
            string? savePath = _dialogService?.ShowSaveFileDialog(
                "Animated GIF (*.gif)|*.gif",
                "Export Animated GIF",
                "animation.gif");

            if (!string.IsNullOrEmpty(savePath))
            {
                try
                {
                    string? dir = Path.GetDirectoryName(savePath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    var imgSettings = new ImageExportSettings
                    {
                        Format = ImageExportFormat.Gif,
                        Scale = 1,
                        GifFps = Math.Max(1, Fps)
                    };

                    if (SelectedPreviewModeIndex == 1) // Full Color
                    {
                        var colorFrames = GetCroppedColorFrames();
                        if (colorFrames.Count > 0)
                        {
                            _exportService?.ExportBitmaps(savePath, colorFrames, imgSettings);
                        }
                        else
                        {
                            _exportService?.Export(savePath, SlicedSprite, imgSettings);
                        }
                    }
                    else // 1-Bit with active theme
                    {
                        (uint bgUint, uint fgUint) = GetThemeColors(SelectedThemeIndex);
                        imgSettings.ColorMode = ExportColorMode.CustomPalette;
                        imgSettings.CustomBackgroundColor = ColorFromUint(bgUint);
                        imgSettings.CustomForegroundColor = ColorFromUint(fgUint);
                        _exportService?.Export(savePath, SlicedSprite, imgSettings);
                    }

                    _dialogService?.ShowMessage($"Successfully exported GIF to:\n{savePath}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
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
            string? savePath = _dialogService?.ShowSaveFileDialog(
                "PNG Sequence (*.png)|*.png",
                "Export PNG Sequence Base Filename",
                "frame.png");

            if (!string.IsNullOrEmpty(savePath))
            {
                try
                {
                    string? dir = Path.GetDirectoryName(savePath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    var imgSettings = new ImageExportSettings
                    {
                        Format = ImageExportFormat.PngSequence,
                        Scale = 1
                    };

                    if (SelectedPreviewModeIndex == 1) // Full Color
                    {
                        var colorFrames = GetCroppedColorFrames();
                        if (colorFrames.Count > 0)
                        {
                            _exportService?.ExportBitmaps(savePath, colorFrames, imgSettings);
                        }
                        else
                        {
                            _exportService?.Export(savePath, SlicedSprite, imgSettings);
                        }
                    }
                    else // 1-Bit with active theme
                    {
                        (uint bgUint, uint fgUint) = GetThemeColors(SelectedThemeIndex);
                        imgSettings.ColorMode = ExportColorMode.CustomPalette;
                        imgSettings.CustomBackgroundColor = ColorFromUint(bgUint);
                        imgSettings.CustomForegroundColor = ColorFromUint(fgUint);
                        _exportService?.Export(savePath, SlicedSprite, imgSettings);
                    }

                    _dialogService?.ShowMessage($"Successfully exported {SlicedSprite.Frames.Count} PNG frames!", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    _dialogService?.ShowMessage($"Failed to export PNG sequence:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        public void ExportSpriteSheet()
        {
            string? savePath = _dialogService?.ShowSaveFileDialog(
                "PNG Sprite Sheet (*.png)|*.png",
                "Export Sliced Sprite Sheet PNG",
                "spritesheet.png");

            if (!string.IsNullOrEmpty(savePath))
            {
                try
                {
                    string? dir = Path.GetDirectoryName(savePath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    var imgSettings = new ImageExportSettings
                    {
                        Format = ImageExportFormat.Png,
                        ExportAllFramesAsSpritesheet = true,
                        Scale = 1
                    };

                    if (SelectedPreviewModeIndex == 1) // Full Color
                    {
                        var colorFrames = GetCroppedColorFrames();
                        if (colorFrames.Count > 0)
                        {
                            _exportService?.ExportBitmaps(savePath, colorFrames, imgSettings);
                        }
                        else
                        {
                            _exportService?.Export(savePath, SlicedSprite, imgSettings);
                        }
                    }
                    else // 1-Bit with active theme
                    {
                        (uint bgUint, uint fgUint) = GetThemeColors(SelectedThemeIndex);
                        imgSettings.ColorMode = ExportColorMode.CustomPalette;
                        imgSettings.CustomBackgroundColor = ColorFromUint(bgUint);
                        imgSettings.CustomForegroundColor = ColorFromUint(fgUint);
                        _exportService?.Export(savePath, SlicedSprite, imgSettings);
                    }

                    _dialogService?.ShowMessage($"Successfully exported sprite sheet PNG to:\n{savePath}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    _dialogService?.ShowMessage($"Failed to export sprite sheet:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        public void ExportFlipper()
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
                    Directory.CreateDirectory(folder);

                    string animName = !string.IsNullOrWhiteSpace(SourceFileName)
                        ? CodeGeneratorService.SanitiseName(SourceFileName)
                        : "anim_sliced";

                    var exportSettings = new FlipperExportSettings
                    {
                        TargetFolder = folder,
                        AnimationName = animName,
                        FrameRate = Fps,
                        PassiveFrames = SlicedSprite.Frames.Count,
                        ActiveFrames = 0
                    };

                    _flipperExportService?.ExportAnimation(SlicedSprite, exportSettings);
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
            SliceOverlays.Clear();
            FrameThumbnails.Clear();
            _pixelBuffer = [];
            ColorPreviewBitmap = null;
            OnionSkinPrevBitmap = null;
            OnionSkinNextBitmap = null;
            RequestClose = null;
            SourceImage = null;
            SlicedSprite = new SpriteState(1, 1);
            GC.SuppressFinalize(this);
        }
    }
}
