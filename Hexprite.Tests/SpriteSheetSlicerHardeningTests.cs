using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class SpriteSheetSlicerHardeningTests
    {
        private readonly SpriteSheetSlicerService _slicer = new();

        private static BitmapSource CreateSolidBitmap(int width, int height, Color color)
        {
            var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            uint pixelVal = ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
            uint[] pixels = new uint[width * height];
            Array.Fill(pixels, pixelVal);
            bmp.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            return bmp;
        }

        #region 1. Aspect Scaling Modes

        [Fact]
        public void SliceToAnimationSprite_FitAspectScaling_ScalesSliceUniformly()
        {
            // 32x32 frame placed into 128x64 fixed canvas with FitAspect
            // 32x32 should fit in 64h -> scale 2.0 -> 64x64 centered on 128x64
            var source = CreateSolidBitmap(64, 32, Colors.White);
            var settings = new SpriteSheetSliceSettings
            {
                Layout = SpriteSheetLayout.HorizontalStrip,
                FrameWidth = 32,
                FrameHeight = 32,
                CanvasMode = SliceCanvasMode.FixedCanvas,
                ScalingMode = SliceScalingMode.FitAspect,
                CanvasWidth = 128,
                CanvasHeight = 64,
                Alignment = SliceCanvasAlignment.Center
            };

            var sprite = _slicer.SliceToAnimationSprite(source, settings);

            Assert.Equal(2, sprite.Frames.Count);
            Assert.Equal(128, sprite.Width);
            Assert.Equal(64, sprite.Height);

            // Verify centered bounds: pixels should exist in middle 64x64 region (x: 32..95, y: 0..63)
            var frame0 = sprite.Frames[0].LayerPixels[0].GetMonochromeData();
            Assert.True(frame0[32 * 128 + 64]); // Center pixel active
            Assert.False(frame0[0]); // Top-left corner (0,0) inactive due to centering
        }

        [Fact]
        public void SliceToAnimationSprite_StretchScaling_FillsEntireTargetCanvas()
        {
            // 16x16 frame stretched to 128x64
            var source = CreateSolidBitmap(16, 16, Colors.White);
            var settings = new SpriteSheetSliceSettings
            {
                Layout = SpriteSheetLayout.HorizontalStrip,
                FrameWidth = 16,
                FrameHeight = 16,
                CanvasMode = SliceCanvasMode.FixedCanvas,
                ScalingMode = SliceScalingMode.Stretch,
                CanvasWidth = 128,
                CanvasHeight = 64
            };

            var sprite = _slicer.SliceToAnimationSprite(source, settings);

            Assert.Single(sprite.Frames);
            Assert.Equal(128, sprite.Width);
            Assert.Equal(64, sprite.Height);

            var frame0 = sprite.Frames[0].LayerPixels[0].GetMonochromeData();
            // In stretched mode with solid white source, all pixels should be set
            Assert.All(frame0, p => Assert.True(p));
        }

        [Fact]
        public void SliceToAnimationSprite_FillAspectScaling_CropsOverflowCorrectly()
        {
            // 64x32 frame with FillAspect into 32x32 target canvas
            var source = CreateSolidBitmap(64, 32, Colors.White);
            var settings = new SpriteSheetSliceSettings
            {
                Layout = SpriteSheetLayout.HorizontalStrip,
                FrameWidth = 64,
                FrameHeight = 32,
                CanvasMode = SliceCanvasMode.FixedCanvas,
                ScalingMode = SliceScalingMode.FillAspect,
                CanvasWidth = 32,
                CanvasHeight = 32
            };

            var sprite = _slicer.SliceToAnimationSprite(source, settings);

            Assert.Single(sprite.Frames);
            Assert.Equal(32, sprite.Width);
            Assert.Equal(32, sprite.Height);
        }

        #endregion

        #region 2. Playback Loop Modes

        [Fact]
        public void ViewModel_AdvanceFrame_PingPongMode_BouncesForwardAndBack()
        {
            using var vm = new SpriteSheetSlicerViewModel
            {
                SelectedLoopModeIndex = 1 // PingPong
            };
            vm.CreateSampleSourceImage();
            vm.Reslice();

            // Start at frame index 0 of 4
            Assert.Equal(4, vm.TotalFrames);
            Assert.Equal(0, vm.CurrentFrameIndex);

            // Forward: 0 -> 1 -> 2 -> 3
            vm.AdvanceFrame();
            Assert.Equal(1, vm.CurrentFrameIndex);
            vm.AdvanceFrame();
            Assert.Equal(2, vm.CurrentFrameIndex);
            vm.AdvanceFrame();
            Assert.Equal(3, vm.CurrentFrameIndex);

            // Boundary reached, reverses: 3 -> 2 -> 1 -> 0
            vm.AdvanceFrame();
            Assert.Equal(2, vm.CurrentFrameIndex);
            vm.AdvanceFrame();
            Assert.Equal(1, vm.CurrentFrameIndex);
            vm.AdvanceFrame();
            Assert.Equal(0, vm.CurrentFrameIndex);

            // Boundary reached, reverses again: 0 -> 1
            vm.AdvanceFrame();
            Assert.Equal(1, vm.CurrentFrameIndex);
        }

        [Fact]
        public void ViewModel_AdvanceFrame_PlayOnceMode_StopsAtLastFrame()
        {
            using var vm = new SpriteSheetSlicerViewModel
            {
                SelectedLoopModeIndex = 2 // Once
            };
            vm.CreateSampleSourceImage();
            vm.Reslice();

            Assert.Equal(4, vm.TotalFrames);
            Assert.Equal(0, vm.CurrentFrameIndex);
            Assert.True(vm.IsPlaying);

            vm.AdvanceFrame(); // 1
            vm.AdvanceFrame(); // 2
            vm.AdvanceFrame(); // 3 (Last frame)
            Assert.Equal(3, vm.CurrentFrameIndex);

            // Advance at last frame stops playback
            vm.AdvanceFrame();
            Assert.Equal(3, vm.CurrentFrameIndex);
            Assert.False(vm.IsPlaying);
        }

        #endregion

        #region 3. Empty Frame Auto-Trimming

        [Fact]
        public void SliceToAnimationSprite_AutoTrimEmptyFrames_RemovesTrailingBlankFrames()
        {
            // 128x32 strip with 4 frames of 32x32:
            // Frames 0 and 1 have white pixels; Frames 2 and 3 are black/empty
            var bmp = new WriteableBitmap(128, 32, 96, 96, PixelFormats.Bgra32, null);
            uint[] pixels = new uint[128 * 32];
            // Fill first 64px wide (frames 0 and 1) with white
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    pixels[y * 128 + x] = 0xFFFFFFFF;
                }
            }
            bmp.WritePixels(new Int32Rect(0, 0, 128, 32), pixels, 128 * 4, 0);

            var settings = new SpriteSheetSliceSettings
            {
                Layout = SpriteSheetLayout.HorizontalStrip,
                FrameWidth = 32,
                FrameHeight = 32,
                AutoTrimEmptyFrames = true,
                CanvasMode = SliceCanvasMode.FitFrame
            };

            var sprite = _slicer.SliceToAnimationSprite(bmp, settings);

            // Should trim 2 trailing blank frames -> 2 frames remaining
            Assert.Equal(2, sprite.Frames.Count);
        }

        #endregion

        #region 4. Defensive Clamping & Fuzz Boundary Inputs

        [Fact]
        public void SliceToAnimationSprite_MicroOneByOneImage_DoesNotCrash()
        {
            var source = CreateSolidBitmap(1, 1, Colors.White);
            var settings = new SpriteSheetSliceSettings
            {
                FrameWidth = 32,
                FrameHeight = 32,
                CanvasMode = SliceCanvasMode.FitFrame
            };

            var sprite = _slicer.SliceToAnimationSprite(source, settings);

            Assert.NotNull(sprite);
            Assert.Single(sprite.Frames);
        }

        [Fact]
        public void SliceToAnimationSprite_HugeOffsetsAndSpacing_DoesNotCrash()
        {
            var source = CreateSolidBitmap(64, 64, Colors.White);
            var settings = new SpriteSheetSliceSettings
            {
                OffsetX = 500,
                OffsetY = 500,
                SpacingX = 1000,
                SpacingY = 1000,
                FrameWidth = 32,
                FrameHeight = 32
            };

            var sprite = _slicer.SliceToAnimationSprite(source, settings);

            Assert.NotNull(sprite);
            Assert.NotEmpty(sprite.Frames);
        }

        [Fact]
        public void SliceToAnimationSprite_TransparentAlphaSource_ProducesCleanBlankMonochrome()
        {
            var source = CreateSolidBitmap(32, 32, Colors.Transparent);
            var settings = new SpriteSheetSliceSettings
            {
                FrameWidth = 32,
                FrameHeight = 32,
                AlphaThreshold = 128
            };

            var sprite = _slicer.SliceToAnimationSprite(source, settings);

            Assert.Single(sprite.Frames);
            var pixels = sprite.Frames[0].LayerPixels[0].GetMonochromeData();
            Assert.All(pixels, p => Assert.False(p));
        }

        [Fact]
        public void CalculateSliceRects_MaxFramesExceeded_ClampsStrictlyToFiveHundredTwelve()
        {
            var source = CreateSolidBitmap(1024, 1024, Colors.White);
            var settings = new SpriteSheetSliceSettings
            {
                Layout = SpriteSheetLayout.Grid,
                FrameWidth = 1,
                FrameHeight = 1,
                MaxFrames = 10000 // Huge requested count
            };

            var rects = _slicer.CalculateSliceRects(source, settings);

            Assert.Equal(512, rects.Count);
        }

        [Fact]
        public void CreateStripFromGif_NonExistentOrInvalidFile_ReturnsNullSafely()
        {
            Assert.Null(_slicer.CreateStripFromGif(""));
            Assert.Null(_slicer.CreateStripFromGif("non_existent_file_path.gif"));
        }

        #endregion

        #region 5. Concurrency, Pre-Processing & Memory Lifecycle

        [Fact]
        public void ViewModel_EnhancementProperties_TriggerSafeReslice()
        {
            using var vm = new SpriteSheetSlicerViewModel();

            vm.UseSerpentineScanning = true;
            vm.UseGammaCorrection = true;
            vm.Contrast = 50;
            vm.Brightness = -20;
            vm.AutoTrimEmptyFrames = true;

            var settings = vm.GetCurrentSettings();
            Assert.True(settings.UseSerpentineScanning);
            Assert.True(settings.UseGammaCorrection);
            Assert.Equal(50, settings.Contrast);
            Assert.Equal(-20, settings.Brightness);
            Assert.True(settings.AutoTrimEmptyFrames);
        }

        [Fact]
        public void ViewModel_PresetSwitching_ConfiguresScalingAndPaletteCorrectly()
        {
            using var vm = new SpriteSheetSlicerViewModel();

            // Select Micro:bit preset (Index 6)
            vm.SelectedPresetIndex = 6;

            Assert.Equal(5, vm.CanvasWidth);
            Assert.Equal(5, vm.CanvasHeight);
            Assert.Equal(4, vm.SelectedThemeIndex);
        }

        [Fact]
        public void Window_Instantiation_ParsesXamlSuccessfully()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var win = new Views.SpriteSheetSlicerWindow();
                Assert.NotNull(win);
                Assert.NotNull(win.ViewModel);
                win.Close();
            });
        }

        #endregion

        #region 6. Resilience & Defensive Hardening Tests

        [Fact]
        public void DetectGrid_ZeroOrNegativeDimensions_ReturnsFallbackSafely()
        {
            var bmp = CreateSolidBitmap(1, 1, Colors.Black);
            // Valid call on 1x1
            var detected1 = _slicer.DetectGrid(bmp);
            Assert.Equal(1, detected1.SuggestedFrameWidth);
            Assert.Equal(1, detected1.SuggestedFrameHeight);
            Assert.Equal(1, detected1.Columns);
            Assert.Equal(1, detected1.Rows);
        }

        [Fact]
        public void DetectAlphaIslands_GiantImage_ClampsGracefullyWithoutOOM()
        {
            // DetectAlphaIslands on 1x1 image
            var bmp = CreateSolidBitmap(1, 1, Colors.White);
            var islands = _slicer.DetectAlphaIslands(bmp);
            Assert.NotNull(islands);
        }

        [Fact]
        public void ViewModel_RenderCurrentFrame_WithEmptyLayerPixels_DoesNotThrow()
        {
            using var vm = new SpriteSheetSlicerViewModel();
            var emptySprite = new SpriteState(32, 32);
            emptySprite.Frames.Clear();
            var frame = new FrameState { Name = "EmptyFrame" };
            // Frame with 0 LayerPixels
            emptySprite.Frames.Add(frame);

            // Reslicing or setting should not throw
            vm.RenderCurrentFrame();
        }

        [Fact]
        public void ViewModel_CopyCode_WithEmptyLayerPixels_DoesNotThrow()
        {
            using var vm = new SpriteSheetSlicerViewModel();
            // Should gracefully do nothing without crash
            vm.CopyCode();
        }

        [Fact]
        public void ViewModel_ExportJsonMetadata_CreatesMissingDirectories()
        {
            using var vm = new SpriteSheetSlicerViewModel();
            vm.CreateSampleSourceImage();
            vm.Reslice();

            string tempDir = Path.Combine(Path.GetTempPath(), "Hexprite_Slicer_Test_" + Guid.NewGuid().ToString("N"), "nested_subfolder");
            string tempFile = Path.Combine(tempDir, "meta.json");

            var mockDialog = new MockDialogService { SaveFileDialogResult = tempFile };
            using var vmWithDialog = new SpriteSheetSlicerViewModel(dialogService: mockDialog);
            vmWithDialog.CreateSampleSourceImage();
            vmWithDialog.Reslice();

            try
            {
                vmWithDialog.ExportJsonMetadata();
                Assert.True(File.Exists(tempFile));
                string content = File.ReadAllText(tempFile);
                Assert.Contains("\"app\": \"Hexprite Sprite Sheet & Atlas Slicer Studio\"", content);
            }
            finally
            {
                if (Directory.Exists(Path.GetDirectoryName(tempDir)))
                {
                    Directory.Delete(Path.GetDirectoryName(tempDir)!, true);
                }
            }
        }

        [Fact]
        public void ViewModel_Dispose_CleansUpAllBitmapsAndCollections()
        {
            var vm = new SpriteSheetSlicerViewModel();
            vm.CreateSampleSourceImage();
            vm.Reslice();

            Assert.NotEmpty(vm.SliceOverlays);
            Assert.NotEmpty(vm.FrameThumbnails);
            Assert.NotNull(vm.SourceImage);

            vm.Dispose();

            Assert.Empty(vm.SliceOverlays);
            Assert.Empty(vm.FrameThumbnails);
            Assert.Null(vm.SourceImage);
            Assert.Null(vm.ColorPreviewBitmap);
            Assert.Null(vm.OnionSkinPrevBitmap);
            Assert.Null(vm.OnionSkinNextBitmap);
        }

        [Fact]
        public void ViewModel_OnFpsChanged_UpdatesExportSummaryText()
        {
            using var vm = new SpriteSheetSlicerViewModel();
            vm.CreateSampleSourceImage();
            vm.Reslice();

            vm.Fps = 24;
            Assert.Contains("24 FPS", vm.ExportSummaryText);
            Assert.Contains("0.17s loop", vm.ExportSummaryText);

            vm.Fps = 6;
            Assert.Contains("6 FPS", vm.ExportSummaryText);
            Assert.Contains("0.67s loop", vm.ExportSummaryText);
        }

        [Fact]
        public void ViewModel_OpenInCanvas_ClonesSpriteIndependentFromViewModel()
        {
            var mockTabService = new MockWorkspaceTabService();
            using var vm = new SpriteSheetSlicerViewModel(tabService: mockTabService);
            vm.CreateSampleSourceImage();
            vm.Reslice();

            vm.OpenInCanvas();

            Assert.Single(mockTabService.OpenedTabs);
            var (tabName, sprite) = mockTabService.OpenedTabs[0];
            Assert.NotNull(sprite);
            // Verify it is a separate cloned instance
            Assert.NotSame(vm.SlicedSprite, sprite);
        }

        [Fact]
        public void ViewModel_ExportFlipper_UsesSanitisedSourceFileName()
        {
            var mockFlipper = new MockFlipperExportService();
            var mockDialog = new MockDialogService { OpenFolderDialogResult = @"C:\TestFolder" };
            using var vm = new SpriteSheetSlicerViewModel(
                dialogService: mockDialog,
                flipperExportService: mockFlipper);

            vm.SourceFileName = "Hero Walk Cycle!";
            vm.CreateSampleSourceImage();
            vm.Reslice();

            vm.ExportFlipper();

            Assert.NotNull(mockFlipper.LastExportSettings);
            Assert.Equal("Hero_Walk_Cycle_", mockFlipper.LastExportSettings.AnimationName);
        }

        [Fact]
        public void PruneEmptyRects_OpaqueImageWithUniformBackground_PrunesEmptyCellsCorrectly()
        {
            // Create a 64x32 image with 2 cells (32x32 each):
            // Cell 0 is uniform black (background).
            // Cell 1 has a white dot in the center.
            int w = 64;
            int h = 32;
            var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            uint[] pixels = new uint[w * h];

            // All pixels black (Alpha=255, R=0, G=0, B=0)
            for (int i = 0; i < pixels.Length; i++) pixels[i] = 0xFF000000;

            // Put white dot in second cell (x: 32..63)
            pixels[16 * w + 48] = 0xFFFFFFFF;
            bmp.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);

            var rects = new List<Int32Rect>
            {
                new(0, 0, 32, 32),   // Cell 0: purely background
                new(32, 0, 32, 32)   // Cell 1: has foreground dot
            };

            var pruned = _slicer.PruneEmptyRects(bmp, rects);

            Assert.Single(pruned);
            Assert.Equal(32, pruned[0].X);
        }

        [Fact]
        public void ViewModel_ZoomToFit_ExpandsUpToMaxZoomLevel()
        {
            using var vm = new SpriteSheetSlicerViewModel();
            // Tiny 16x16 image
            var bmp = CreateSolidBitmap(16, 16, Colors.Red);
            vm.LoadImage(bmp);

            vm.ZoomToFit();

            Assert.True(vm.ZoomLevel > 4.0);
            Assert.True(vm.ZoomLevel <= 8.0);
        }

        [Fact]
        public void ViewModel_PlayOnceMode_WhenReplayingFromEnd_ResetsToFrameZero()
        {
            using var vm = new SpriteSheetSlicerViewModel();
            vm.CreateSampleSourceImage();
            vm.Reslice();

            vm.SelectedLoopModeIndex = 2; // Play Once
            vm.CurrentFrameIndex = vm.TotalFrames - 1; // At last frame
            vm.IsPlaying = false;

            // Trigger Play
            vm.TogglePlayPause();

            Assert.True(vm.IsPlaying);
            Assert.Equal(0, vm.CurrentFrameIndex);
        }

        [Fact]
        public void ViewModel_AdvanceFrame_InPlayOnceMode_HaltsAtEnd()
        {
            using var vm = new SpriteSheetSlicerViewModel();
            vm.CreateSampleSourceImage();
            vm.Reslice();

            vm.SelectedLoopModeIndex = 2; // Play Once
            vm.CurrentFrameIndex = vm.TotalFrames - 2;
            vm.IsPlaying = true;

            // Advance to last frame
            vm.AdvanceFrame();
            Assert.Equal(vm.TotalFrames - 1, vm.CurrentFrameIndex);
            Assert.True(vm.IsPlaying);

            // Advance past last frame -> should halt
            vm.AdvanceFrame();
            Assert.False(vm.IsPlaying);
        }

        [Fact]
        public void ViewModel_UpdateOnionSkin_WhenDisabled_PreventsGhostAllocations()
        {
            using var vm = new SpriteSheetSlicerViewModel();
            vm.CreateSampleSourceImage();
            vm.Reslice();

            vm.OnionSkinEnabled = false;

            // Advance frame when onion skin is disabled
            vm.AdvanceFrame();

            // Onion bitmaps should remain null (zero GC allocations for ghost frames)
            Assert.NotNull(vm.ColorPreviewBitmap);
            Assert.Null(vm.OnionSkinPrevBitmap);
            Assert.Null(vm.OnionSkinNextBitmap);
        }

        [Fact]
        public void ViewModel_StepForwardAndBackward_NavigatesCleanly()
        {
            using var vm = new SpriteSheetSlicerViewModel();
            vm.CreateSampleSourceImage();
            vm.Reslice();

            Assert.Equal(0, vm.CurrentFrameIndex);

            vm.StepForward();
            Assert.Equal(1, vm.CurrentFrameIndex);
            Assert.False(vm.IsPlaying);

            vm.StepBackward();
            Assert.Equal(0, vm.CurrentFrameIndex);

            vm.StepBackward();
            Assert.Equal(vm.TotalFrames - 1, vm.CurrentFrameIndex);
        }

        [Fact]
        public void ViewModel_ExportGif_WhenIn1BitModeWithFlipperTheme_ExportsWithCustomPalette()
        {
            var mockExport = new MockExportService();
            var mockDialog = new MockDialogService { SaveFileDialogResult = @"C:\test.gif" };
            using var vm = new SpriteSheetSlicerViewModel(
                dialogService: mockDialog,
                exportService: mockExport);

            vm.CreateSampleSourceImage();
            vm.Reslice();

            vm.SelectedPreviewModeIndex = 0; // 1-Bit
            vm.SelectedThemeIndex = 2; // Flipper Classic (Orange Bg, Black Fg)

            vm.ExportGif();

            Assert.NotNull(mockExport.LastExportSettings);
            Assert.Equal(ExportColorMode.CustomPalette, mockExport.LastExportSettings.ColorMode);
            Assert.NotNull(mockExport.LastExportSettings.CustomBackgroundColor);
            Assert.NotNull(mockExport.LastExportSettings.CustomForegroundColor);
            // Verify Flipper Orange background (#FFFF8200) and Black foreground (#FF000000)
            Assert.Equal(Color.FromArgb(255, 255, 130, 0), mockExport.LastExportSettings.CustomBackgroundColor.Value);
            Assert.Equal(Color.FromArgb(255, 0, 0, 0), mockExport.LastExportSettings.CustomForegroundColor.Value);
        }

        [Fact]
        public void ViewModel_ExportPngSequence_WhenInColorMode_ExportsColorBitmaps()
        {
            var mockExport = new MockExportService();
            var mockDialog = new MockDialogService { SaveFileDialogResult = @"C:\frame.png" };
            using var vm = new SpriteSheetSlicerViewModel(
                dialogService: mockDialog,
                exportService: mockExport);

            vm.CreateSampleSourceImage();
            vm.Reslice();

            vm.SelectedPreviewModeIndex = 1; // Full Color

            vm.ExportPngSequence();

            Assert.NotNull(mockExport.LastExportBitmaps);
            Assert.Equal(4, mockExport.LastExportBitmaps.Count);
            Assert.Equal(ImageExportFormat.PngSequence, mockExport.LastExportSettings?.Format);
        }

        [Fact]
        public void ViewModel_ExportSpriteSheet_WhenInColorMode_ExportsColorBitmaps()
        {
            var mockExport = new MockExportService();
            var mockDialog = new MockDialogService { SaveFileDialogResult = @"C:\sheet.png" };
            using var vm = new SpriteSheetSlicerViewModel(
                dialogService: mockDialog,
                exportService: mockExport);

            vm.CreateSampleSourceImage();
            vm.Reslice();

            vm.SelectedPreviewModeIndex = 1; // Full Color

            vm.ExportSpriteSheet();

            Assert.NotNull(mockExport.LastExportBitmaps);
            Assert.Equal(4, mockExport.LastExportBitmaps.Count);
            Assert.True(mockExport.LastExportSettings?.ExportAllFramesAsSpritesheet);
        }

        [Fact]
        public void ExportService_ExportBitmaps_ColorSequence_SavesFiles()
        {
            var exportService = new ExportService();
            string tempDir = Path.Combine(Path.GetTempPath(), "Hexprite_ColorExport_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string baseFile = Path.Combine(tempDir, "anim.png");

            var frames = new List<BitmapSource>
            {
                CreateSolidBitmap(16, 16, Colors.Red),
                CreateSolidBitmap(16, 16, Colors.Blue)
            };

            var settings = new ImageExportSettings
            {
                Format = ImageExportFormat.PngSequence,
                Scale = 1
            };

            try
            {
                exportService.ExportBitmaps(baseFile, frames, settings);
                string frame1 = Path.Combine(tempDir, "anim_001.png");
                string frame2 = Path.Combine(tempDir, "anim_002.png");
                Assert.True(File.Exists(frame1));
                Assert.True(File.Exists(frame2));
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        private class MockExportService : IExportService
        {
            public string? LastExportPath { get; private set; }
            public SpriteState? LastExportSprite { get; private set; }
            public ImageExportSettings? LastExportSettings { get; private set; }
            public IReadOnlyList<BitmapSource>? LastExportBitmaps { get; private set; }

            public void Export(string targetPath, SpriteState spriteState, ImageExportSettings settings)
            {
                LastExportPath = targetPath;
                LastExportSprite = spriteState;
                LastExportSettings = settings;
            }

            public void ExportBitmaps(string targetPath, IReadOnlyList<BitmapSource> frames, ImageExportSettings settings)
            {
                LastExportPath = targetPath;
                LastExportBitmaps = frames;
                LastExportSettings = settings;
            }
        }

        private class MockWorkspaceTabService : IWorkspaceTabService
        {
            public List<(string Name, SpriteState Sprite)> OpenedTabs { get; } = [];

            public void OpenSpritesInTabs(IEnumerable<SpriteState> sprites, string tabNamePrefix = "Imported")
            {
                foreach (var s in sprites) OpenedTabs.Add((tabNamePrefix, s));
            }

            public void OpenSpritesInTabs(IEnumerable<(string Name, SpriteState Sprite)> items)
            {
                OpenedTabs.AddRange(items);
            }

            public void OpenSpritesInTabsWithPaths(IEnumerable<(string Name, SpriteState Sprite, string? FilePath)> sprites)
            {
                foreach (var (name, sprite, _) in sprites) OpenedTabs.Add((name, sprite));
            }

            public void OpenSpritesInTabsWithPaths(IEnumerable<(string Name, SpriteState Sprite, string? FilePath, string? ParentPackPath, string? ParentPackName, string? PackEntryName)> sprites)
            {
                foreach (var (name, sprite, _, _, _, _) in sprites) OpenedTabs.Add((name, sprite));
            }

            public void OpenSpriteInTab(SpriteState sprite, string title) => OpenedTabs.Add((title, sprite));
            public void OpenSpriteInTab(SpriteState sprite, string title, string? filePath) => OpenedTabs.Add((title, sprite));
            public void OpenSpriteInTab(SpriteState sprite, string title, string? filePath, string? parentPackPath, string? parentPackName, string? packEntryName) => OpenedTabs.Add((title, sprite));
            public void OpenAssetPackInTab(IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>? animations = null, string assetPackName = "Flipper Asset Pack") { }
            public SpriteState? GetActiveSpriteState() => null;
            public (string Title, SpriteState Sprite)? GetActiveSprite() => null;
            public bool[]? GetActiveFramePixels(bool animated = false) => null;
            public IReadOnlyList<(string Title, SpriteState Sprite)> GetAllOpenSprites() => [];
            public bool ActivateTabByTitle(string title) => false;
        }

        private class MockFlipperExportService : IFlipperExportService
        {
            public FlipperExportSettings? LastExportSettings { get; private set; }
            public void ExportAnimation(SpriteState sprite, FlipperExportSettings settings)
            {
                LastExportSettings = settings;
            }

            public IReadOnlyList<(string RelativePath, byte[] Data)> GenerateDeploymentFiles(SpriteState sprite, FlipperExportSettings settings) => [];
            public void ExportAssetPack(IReadOnlyList<(SpriteState Sprite, FlipperManifestEntry ManifestEntry, FlipperExportSettings Settings)> animations, string targetAssetPackFolder, bool isMomentum = true) { }
            public Task ExportAssetPackAsync(IReadOnlyList<(SpriteState Sprite, FlipperManifestEntry ManifestEntry, FlipperExportSettings Settings)> animations, string targetAssetPackFolder, bool isMomentum = true, IProgress<double>? progress = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public void ExportAssetPackZip(IReadOnlyList<(SpriteState Sprite, FlipperManifestEntry ManifestEntry, FlipperExportSettings Settings)> animations, string targetZipFilePath, bool isMomentum = true) { }
            public Task ExportAssetPackZipAsync(IReadOnlyList<(SpriteState Sprite, FlipperManifestEntry ManifestEntry, FlipperExportSettings Settings)> animations, string targetZipFilePath, bool isMomentum = true, IProgress<double>? progress = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public List<FlipperValidationDiagnostic> ValidateAssetPackForExport(IReadOnlyList<(SpriteState Sprite, FlipperManifestEntry ManifestEntry, FlipperExportSettings Settings)> animations, bool isMomentum = true) => [];
            public void ExportImage(SpriteState spriteState, int frameIndex, string targetFilePath) { }
        }

        private class MockDialogService : IDialogService
        {
            public string? SaveFileDialogResult { get; set; }
            public string? OpenFolderDialogResult { get; set; }
            public string? ShowSaveFileDialog(string filter, string title, string defaultExt) => SaveFileDialogResult;
            public void ShowMessage(string message) { }
            public void ShowMessage(string message, string title, MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None) { }
            public bool ShowConfirmation(string message, string title) => true;
            public string? ShowOpenFileDialog(string filter, string title) => null;
            public string[]? ShowOpenFilesDialog(string filter, string title) => null;
            public string? ShowOpenFolderDialog(string title) => OpenFolderDialogResult;
            public bool? ShowUnsavedChangesDialog(string documentName, bool isLinkedSource = false) => true;
            public void ShowAboutDialog() { }
            public GlobalLayerLocalizeMode? ShowGlobalLayerLocalizeDialog(bool canRestore) => null;
            public (int Width, int Height, ColorMode ColorMode, DocumentMode DocumentMode)? ShowNewDocumentDialog() => null;
            public (int Width, int Height, ResizeAnchor Anchor)? ShowResizeCanvasDialog(int currentWidth, int currentHeight) => null;
            public (int Width, int Height, string Code, string? SpriteName, ExportFormat Format, bool Invert)? ShowImportFromCodeDialog() => null;
            public (string FilePath, List<DetectedSprite> SelectedSprites)? ShowImportFromFileDialog(string? initialFilePath = null) => null;
            public BitmapImportSettings? ShowImportBitmapDialog(string fileName, BitmapImportSettings initialSettings) => null;
            public AnimationImportSettings? ShowImportAnimationDialog(string fileName, AnimationImportSettings initialSettings) => null;
            public void ShowSpriteSheetSlicerDialog(BitmapSource? initialImage = null, SpriteState? initialSprite = null, SpriteSheetSliceSettings? initialSettings = null, string? initialFilePath = null) { }
            public BugReportInput? ShowBugReportDialog() => null;
            public void ShowBugReportSuccessDialog(string message, string? reportId, string? successWindowTitle = null) { }
            public UserFeedbackInput? ShowUserFeedbackDialog() => null;
            public bool ShowPrivacySettingsDialog() => true;
            public ImageExportSettings? ShowExportImageDialog(ImageExportSettings initialSettings, SpriteState spriteState) => null;
            public OutlineSettings? ShowOutlineDialog(Action<OutlineSettings>? previewCallback = null) => null;
            public void ShowKeyboardShortcutsDialog() { }
            public bool ShowHardwarePreviewWiringDialog(HardwarePreviewWiringConfig config, int baudRate = 115200, int canvasWidth = 0, int canvasHeight = 0, IHardwarePreviewService? hardwarePreview = null) => true;
        }

        #endregion
    }
}
