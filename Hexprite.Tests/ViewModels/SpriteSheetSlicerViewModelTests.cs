using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Xunit;

namespace Hexprite.Tests.ViewModels
{
    [Trait("Category", "Unit")]
    public class SpriteSheetSlicerViewModelTests
    {
        private class MockTabService : IWorkspaceTabService
        {
            public List<(string Name, SpriteState Sprite)> OpenedSprites { get; } = [];

            public void OpenSpritesInTabs(IEnumerable<SpriteState> sprites, string tabNamePrefix = "Imported")
            {
                foreach (var s in sprites) OpenedSprites.Add((tabNamePrefix, s));
            }

            public void OpenSpritesInTabs(IEnumerable<(string Name, SpriteState Sprite)> sprites)
            {
                OpenedSprites.AddRange(sprites);
            }

            public void OpenSpritesInTabsWithPaths(IEnumerable<(string Name, SpriteState Sprite, string? FilePath)> sprites)
            {
                foreach (var (name, sprite, _) in sprites) OpenedSprites.Add((name, sprite));
            }

            public void OpenSpritesInTabsWithPaths(IEnumerable<(string Name, SpriteState Sprite, string? FilePath, string? ParentPackPath, string? ParentPackName, string? PackEntryName)> sprites)
            {
                foreach (var (name, sprite, _, _, _, _) in sprites) OpenedSprites.Add((name, sprite));
            }

            public void OpenSpriteInTab(SpriteState sprite, string title) => OpenedSprites.Add((title, sprite));
            public void OpenSpriteInTab(SpriteState sprite, string title, string? filePath) => OpenedSprites.Add((title, sprite));
            public void OpenSpriteInTab(SpriteState sprite, string title, string? filePath, string? parentPackPath, string? parentPackName, string? packEntryName) => OpenedSprites.Add((title, sprite));
            public SpriteState? GetActiveSpriteState() => null;
            public (string Title, SpriteState Sprite)? GetActiveSprite() => null;
            public bool[]? GetActiveFramePixels(bool animated = false) => null;
            public IReadOnlyList<(string Title, SpriteState Sprite)> GetAllOpenSprites() => [];
            public IReadOnlyList<(string Title, SpriteState Sprite, string? FilePath)> GetAllOpenSpritesWithPaths() => [];
            public bool ActivateTabByTitle(string title) => false;
            public bool RenameTab(string oldTitle, string newTitle) => false;
            public void OpenAssetPackInTab(IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>? pack = null, string packName = "Flipper Asset Pack") { }
        }

        private class MockDialogService : IDialogService
        {
            public string? OpenFileDialogResult { get; set; }
            public int ShowOpenFileDialogCallCount { get; private set; }

            public string? ShowOpenFileDialog(string filter, string title)
            {
                ShowOpenFileDialogCallCount++;
                return OpenFileDialogResult;
            }

            public void ShowMessage(string message) { }
            public void ShowMessage(string message, string title, MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None) { }
            public bool ShowConfirmation(string message, string title) => true;
            public GlobalLayerLocalizeMode? ShowGlobalLayerLocalizeDialog(bool canRestore) => null;
            public (int Width, int Height, ColorMode ColorMode, DocumentMode DocumentMode)? ShowNewDocumentDialog() => null;
            public (int Width, int Height, ResizeAnchor Anchor)? ShowResizeCanvasDialog(int currentWidth, int currentHeight) => null;
            public string[]? ShowOpenFilesDialog(string filter, string title) => null;
            public string? ShowOpenFolderDialog(string title) => null;
            public string? ShowSaveFileDialog(string filter, string title, string defaultExt) => null;
            public bool? ShowUnsavedChangesDialog(string documentName, bool isLinkedSource = false) => true;
            public void ShowAboutDialog() { }
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

        private static BitmapSource CreateTestBitmap(int width, int height)
        {
            var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            uint[] pixels = new uint[width * height];
            Array.Fill(pixels, 0xFFFFFFFF);
            bmp.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            return bmp;
        }

        private static SpriteSheetSlicerViewModel CreateViewModelWithSample(
            ISpriteSheetSlicerService? slicer = null,
            IWorkspaceTabService? tabService = null,
            IDialogService? dialogService = null)
        {
            var vm = new SpriteSheetSlicerViewModel(slicer, tabService, dialogService);
            vm.CreateSampleSourceImage();
            vm.Reslice();
            return vm;
        }

        [Fact]
        public void InitialState_StartsInEmptyStateWithoutDefaultImage()
        {
            using var vm = new SpriteSheetSlicerViewModel();

            Assert.Null(vm.SourceImage);
            Assert.True(vm.HasNoSourceImage);
            Assert.False(vm.HasSourceImage);
            Assert.Equal(0, vm.TotalFrames);
            Assert.Equal(0, vm.CurrentFrameIndex);
            Assert.Equal("No Frames", vm.FrameCounterText);
            Assert.False(vm.IsPlaying);
            Assert.Equal("▶ Play", vm.PlayPauseButtonText);
            Assert.Empty(vm.SliceOverlays);
            Assert.Equal("No frames loaded", vm.ExportSummaryText);
        }

        [Fact]
        public void CreateSampleSourceImage_ExplicitlyPopulatesSampleGrid()
        {
            using var vm = CreateViewModelWithSample();

            Assert.NotNull(vm.SourceImage);
            Assert.Equal(4, vm.TotalFrames);
            Assert.Equal(4, vm.SliceOverlays.Count);
            Assert.Equal("Frame 1 / 4", vm.FrameCounterText);
        }

        [Fact]
        public void StepForwardAndBackward_WrapsCorrectlyAndUpdatesActiveSlice()
        {
            using var vm = CreateViewModelWithSample();

            Assert.Equal(4, vm.TotalFrames);
            Assert.Equal(0, vm.CurrentFrameIndex);
            Assert.True(vm.SliceOverlays[0].IsActive);

            // Step backward wraps to index 3 (frame 4)
            vm.StepBackward();
            Assert.Equal(3, vm.CurrentFrameIndex);
            Assert.False(vm.IsPlaying);
            Assert.Equal("▶ Play", vm.PlayPauseButtonText);
            Assert.True(vm.SliceOverlays[3].IsActive);
            Assert.False(vm.SliceOverlays[0].IsActive);

            // Step forward wraps to index 0 (frame 1)
            vm.StepForward();
            Assert.Equal(0, vm.CurrentFrameIndex);
            Assert.True(vm.SliceOverlays[0].IsActive);
            Assert.False(vm.SliceOverlays[3].IsActive);
        }

        [Fact]
        public void SelectSlice_JumpsDirectlyToFrame()
        {
            using var vm = CreateViewModelWithSample();

            vm.SelectSlice(2);

            Assert.Equal(2, vm.CurrentFrameIndex);
            Assert.True(vm.SliceOverlays[2].IsActive);
            Assert.Equal("Frame 3 / 4", vm.FrameCounterText);
        }

        [Fact]
        public void ZoomCommands_UpdateZoomLevelAndLabel()
        {
            using var vm = new SpriteSheetSlicerViewModel();

            Assert.Equal(1.0, vm.ZoomLevel);
            Assert.Equal("100%", vm.ZoomLevelLabel);

            vm.ZoomIn();
            Assert.Equal(2.0, vm.ZoomLevel);
            Assert.Equal("200%", vm.ZoomLevelLabel);

            vm.ZoomOut();
            Assert.Equal(1.0, vm.ZoomLevel);
            Assert.Equal("100%", vm.ZoomLevelLabel);

            vm.ZoomOut();
            Assert.Equal(0.5, vm.ZoomLevel);
            Assert.Equal("50%", vm.ZoomLevelLabel);

            vm.ResetZoom();
            Assert.Equal(1.0, vm.ZoomLevel);
            Assert.Equal("100%", vm.ZoomLevelLabel);
        }

        [Fact]
        public void LoadImage_UpdatesSourceDimensionsAndReslices()
        {
            using var vm = new SpriteSheetSlicerViewModel();
            var bmp = CreateTestBitmap(256, 32);

            vm.LoadImage(bmp, "custom_strip.png");

            Assert.Equal(bmp, vm.SourceImage);
            Assert.Contains("custom_strip.png", vm.SourceInfoText);
            Assert.Equal("256 × 32 px", vm.SourceDimensionsText);
            Assert.Equal(8, vm.TotalFrames);
            Assert.Equal(8, vm.SliceOverlays.Count);
        }

        [Fact]
        public void SelectedPresetIndex_FlipperPreset_ConfiguresFixed128x64()
        {
            using var vm = new SpriteSheetSlicerViewModel();

            // Select Flipper Zero preset (Index 1)
            vm.SelectedPresetIndex = 1;

            Assert.Equal(1, vm.SelectedCanvasModeIndex); // FixedCanvas
            Assert.Equal(128, vm.CanvasWidth);
            Assert.Equal(64, vm.CanvasHeight);
            Assert.Equal(0, vm.SelectedAlignmentIndex); // Center
            Assert.Equal(2, vm.SelectedThemeIndex); // Flipper Orange
        }

        [Fact]
        public void OpenInCanvas_DelegatesToWorkspaceTabService()
        {
            var tabService = new MockTabService();
            using var vm = CreateViewModelWithSample(tabService: tabService);

            vm.OpenInCanvas();

            Assert.Single(tabService.OpenedSprites);
            Assert.Equal("Sliced Animation", tabService.OpenedSprites[0].Name);
            Assert.Equal(4, tabService.OpenedSprites[0].Sprite.Frames.Count);
        }

        [Fact]
        public void OpenAsSeparateTabs_OpensAllSlicesAsIndividualTabs()
        {
            var tabService = new MockTabService();
            using var vm = CreateViewModelWithSample(tabService: tabService);

            vm.OpenAsSeparateTabs();

            Assert.Equal(4, tabService.OpenedSprites.Count);
            Assert.Equal("Slice 1", tabService.OpenedSprites[0].Name);
            Assert.Equal("Slice 4", tabService.OpenedSprites[3].Name);
            Assert.All(tabService.OpenedSprites, item => Assert.Single(item.Sprite.Frames));
        }

        [Fact]
        public void FirstAndLastFrameCommands_JumpToBoundaries()
        {
            using var vm = CreateViewModelWithSample();

            Assert.Equal(4, vm.TotalFrames);

            vm.LastFrame();
            Assert.Equal(3, vm.CurrentFrameIndex);
            Assert.False(vm.IsPlaying);

            vm.FirstFrame();
            Assert.Equal(0, vm.CurrentFrameIndex);
            Assert.False(vm.IsPlaying);
        }

        [Fact]
        public void SetFpsCommand_UpdatesFpsAndLabel()
        {
            using var vm = new SpriteSheetSlicerViewModel();

            vm.SetFps("24");
            Assert.Equal(24, vm.Fps);
            Assert.Equal("24 FPS", vm.FpsLabel);

            vm.SetFps("60");
            Assert.Equal(60, vm.Fps);
            Assert.Equal("60 FPS", vm.FpsLabel);
        }

        [Fact]
        public void SelectSidebarTabCommand_SwitchesActiveTabIndex()
        {
            using var vm = new SpriteSheetSlicerViewModel();

            Assert.Equal(0, vm.SelectedSidebarTabIndex);

            vm.SelectSidebarTab("1");
            Assert.Equal(1, vm.SelectedSidebarTabIndex);

            vm.SelectSidebarTab("2");
            Assert.Equal(2, vm.SelectedSidebarTabIndex);
        }

        [Fact]
        public void MaxFrameIndex_CalculatesCorrectly()
        {
            using var vm = CreateViewModelWithSample();

            Assert.Equal(4, vm.TotalFrames);
            Assert.Equal(3, vm.MaxFrameIndex);
        }

        [Fact]
        public void ExportSummaryText_ReflectsFramesAndSettings()
        {
            using var vm = CreateViewModelWithSample();

            Assert.Contains("4 Frames", vm.ExportSummaryText);
            Assert.Contains("12 FPS", vm.ExportSummaryText);

            vm.SetFps("24");
            Assert.Contains("24 FPS", vm.ExportSummaryText);
        }

        [Fact]
        public void ReactiveTabs_TwoWaySync_UpdatesCorrectly()
        {
            using var vm = new SpriteSheetSlicerViewModel();

            Assert.True(vm.IsGeometryTabSelected);
            Assert.False(vm.IsCanvasTabSelected);
            Assert.False(vm.IsDitherTabSelected);

            vm.IsCanvasTabSelected = true;
            Assert.Equal(1, vm.SelectedSidebarTabIndex);
            Assert.False(vm.IsGeometryTabSelected);
            Assert.True(vm.IsCanvasTabSelected);

            vm.IsDitherTabSelected = true;
            Assert.Equal(2, vm.SelectedSidebarTabIndex);
            Assert.False(vm.IsCanvasTabSelected);
            Assert.True(vm.IsDitherTabSelected);
        }

        [Fact]
        public void ResetContrastAndBrightness_ResetsToZero()
        {
            using var vm = new SpriteSheetSlicerViewModel();

            vm.Contrast = 45;
            vm.Brightness = -30;

            vm.ResetContrast();
            Assert.Equal(0, vm.Contrast);

            vm.ResetBrightness();
            Assert.Equal(0, vm.Brightness);
        }

        [Fact]
        public void ZoomToFit_CalculatesAppropriateZoomLevel()
        {
            using var vm = CreateViewModelWithSample();

            vm.ZoomToFit();
            Assert.True(vm.ZoomLevel > 0.0);
        }

        [Fact]
        public void FrameThumbnails_PopulatedAndSynchronizedWithCurrentFrame()
        {
            using var vm = CreateViewModelWithSample();

            Assert.Equal(4, vm.FrameThumbnails.Count);
            Assert.True(vm.FrameThumbnails[0].IsSelected);
            Assert.False(vm.FrameThumbnails[1].IsSelected);

            vm.StepForward();
            Assert.False(vm.FrameThumbnails[0].IsSelected);
            Assert.True(vm.FrameThumbnails[1].IsSelected);
        }

        [Fact]
        public void ColorPreviewBitmap_UpdatesWithCurrentFrame()
        {
            using var vm = CreateViewModelWithSample();

            Assert.NotNull(vm.ColorPreviewBitmap);

            vm.StepForward();
            Assert.NotNull(vm.ColorPreviewBitmap);
        }

        [Fact]
        public void OnionSkin_TogglesAndUpdatesGhostBitmaps()
        {
            using var vm = CreateViewModelWithSample();

            Assert.False(vm.OnionSkinEnabled);
            Assert.Null(vm.OnionSkinPrevBitmap);

            vm.ToggleOnionSkin();
            Assert.True(vm.OnionSkinEnabled);

            // Navigate to frame 1 (has prev frame 0 and next frame 2)
            vm.SelectSlice(1);
            Assert.NotNull(vm.OnionSkinPrevBitmap);
            Assert.NotNull(vm.OnionSkinNextBitmap);
        }

        [Fact]
        public void AutoDetectIslands_ResetsOffsetsAndSetsLayoutIndexToThree()
        {
            using var vm = CreateViewModelWithSample();
            vm.OffsetX = 15;
            vm.OffsetY = 10;

            vm.AutoDetectIslands();
            Assert.Equal(3, vm.SelectedLayoutIndex);
            Assert.Equal(0, vm.OffsetX);
            Assert.Equal(0, vm.OffsetY);
        }

        [Fact]
        public void AutoDetectGrid_SwitchesLayoutIndexAndReslices()
        {
            using var vm = CreateViewModelWithSample();
            vm.SelectedLayoutIndex = 0; // HorizontalStrip
            vm.OffsetX = 20;

            vm.AutoDetectGrid();

            Assert.Equal(0, vm.OffsetX);
            Assert.Equal(0, vm.OffsetY);
            Assert.True(vm.TotalFrames > 0);
        }

        [Fact]
        public void PreviewModeIndex_TogglesPreviewFlags()
        {
            using var vm = new SpriteSheetSlicerViewModel();

            Assert.Equal(0, vm.SelectedPreviewModeIndex);
            Assert.True(vm.IsLcdPreviewActive);
            Assert.False(vm.IsColorPreviewActive);
            Assert.True(vm.IsLcdPreviewMode);
            Assert.False(vm.IsColorPreviewMode);

            vm.IsColorPreviewMode = true;
            Assert.Equal(1, vm.SelectedPreviewModeIndex);
            Assert.False(vm.IsLcdPreviewActive);
            Assert.True(vm.IsColorPreviewActive);
            Assert.False(vm.IsLcdPreviewMode);
            Assert.True(vm.IsColorPreviewMode);

            vm.IsLcdPreviewMode = true;
            Assert.Equal(0, vm.SelectedPreviewModeIndex);
            Assert.Equal(0, vm.SelectedPreviewZoomIndex);
            Assert.True(vm.IsLcdPreviewActive);
            Assert.False(vm.IsColorPreviewActive);
        }

        [Fact]
        public void PreviewZoom_CycleAndMouseWheel_UpdatesZoomIndexCorrectly()
        {
            using var vm = new SpriteSheetSlicerViewModel();

            Assert.Equal(0, vm.SelectedPreviewZoomIndex);
            Assert.True(vm.IsPreviewZoomFit);
            Assert.False(vm.IsPreviewZoomFixed);
            Assert.Equal("🔍 Fit", vm.PreviewZoomLabel);
            Assert.Equal(1.0, vm.PreviewZoomScale);

            vm.CyclePreviewZoom(); // 1 (1x)
            Assert.Equal(1, vm.SelectedPreviewZoomIndex);
            Assert.True(vm.IsPreviewZoomFixed);
            Assert.Equal("1x", vm.PreviewZoomLabel);
            Assert.Equal(1.0, vm.PreviewZoomScale);

            vm.CyclePreviewZoom(); // 2 (2x)
            Assert.Equal(2, vm.SelectedPreviewZoomIndex);
            Assert.Equal("2x", vm.PreviewZoomLabel);
            Assert.Equal(2.0, vm.PreviewZoomScale);

            vm.CyclePreviewZoom(); // 3 (3x)
            Assert.Equal(3, vm.SelectedPreviewZoomIndex);
            Assert.Equal("3x", vm.PreviewZoomLabel);
            Assert.Equal(3.0, vm.PreviewZoomScale);

            vm.CyclePreviewZoom(); // 4 (4x)
            Assert.Equal(4, vm.SelectedPreviewZoomIndex);
            Assert.Equal("4x", vm.PreviewZoomLabel);
            Assert.Equal(4.0, vm.PreviewZoomScale);

            vm.CyclePreviewZoom(); // 5 (6x)
            Assert.Equal(5, vm.SelectedPreviewZoomIndex);
            Assert.Equal("6x", vm.PreviewZoomLabel);
            Assert.Equal(6.0, vm.PreviewZoomScale);

            vm.CyclePreviewZoom(); // 6 (8x)
            Assert.Equal(6, vm.SelectedPreviewZoomIndex);
            Assert.Equal("8x", vm.PreviewZoomLabel);
            Assert.Equal(8.0, vm.PreviewZoomScale);

            vm.CyclePreviewZoom(); // 0 (Fit)
            Assert.Equal(0, vm.SelectedPreviewZoomIndex);
            Assert.True(vm.IsPreviewZoomFit);
            Assert.Equal("🔍 Fit", vm.PreviewZoomLabel);
        }

        [Fact]
        public void ResetPreviewPanAndZoom_ResetsPanAndZoomToFit()
        {
            using var vm = new SpriteSheetSlicerViewModel();
            vm.SelectedPreviewZoomIndex = 3;
            vm.PreviewPanX = 45.5;
            vm.PreviewPanY = -20.0;

            vm.ResetPreviewPanAndZoom();

            Assert.Equal(0, vm.SelectedPreviewZoomIndex);
            Assert.Equal(0, vm.PreviewPanX);
            Assert.Equal(0, vm.PreviewPanY);
            Assert.True(vm.IsPreviewZoomFit);
        }

        [Fact]
        public void ZoomPreview_MouseWheelDelta_AdjustsZoomIndex()
        {
            using var vm = new SpriteSheetSlicerViewModel();

            Assert.Equal(0, vm.SelectedPreviewZoomIndex);

            // Wheel up zooms in
            vm.ZoomPreview(120);
            Assert.Equal(1, vm.SelectedPreviewZoomIndex);

            vm.ZoomPreview(120);
            Assert.Equal(2, vm.SelectedPreviewZoomIndex);

            // Wheel down zooms out
            vm.ZoomPreview(-120);
            Assert.Equal(1, vm.SelectedPreviewZoomIndex);

            vm.ZoomPreview(-120);
            Assert.Equal(0, vm.SelectedPreviewZoomIndex);

            // Clamps at 0
            vm.ZoomPreview(-120);
            Assert.Equal(0, vm.SelectedPreviewZoomIndex);
        }

        [Fact]
        public void ClearSourceImage_DiscardsCurrentSpriteAndReturnsToEmptyState()
        {
            using var vm = CreateViewModelWithSample();

            Assert.NotNull(vm.SourceImage);
            Assert.True(vm.TotalFrames > 0);
            Assert.False(vm.HasNoSourceImage);

            vm.ClearSourceImage();

            Assert.Null(vm.SourceImage);
            Assert.True(vm.HasNoSourceImage);
            Assert.False(vm.HasSourceImage);
            Assert.Equal(0, vm.TotalFrames);
            Assert.Equal(0, vm.CurrentFrameIndex);
            Assert.Equal("No Frames", vm.FrameCounterText);
            Assert.False(vm.IsPlaying);
            Assert.Equal("▶ Play", vm.PlayPauseButtonText);
            Assert.Empty(vm.SliceOverlays);
            Assert.Equal("No frames loaded", vm.ExportSummaryText);
        }

        [Fact]
        public void LoadImage_ReplacesPreviousSpriteSheetCompletely()
        {
            using var vm = CreateViewModelWithSample();
            Assert.Equal(4, vm.TotalFrames);

            var newBmp = CreateTestBitmap(128, 64);
            vm.LoadImage(newBmp, "new_sheet.png");

            Assert.Equal(newBmp, vm.SourceImage);
            Assert.Contains("new_sheet.png", vm.SourceInfoText);
            Assert.True(vm.IsPlaying);
            Assert.Equal("⏸ Pause", vm.PlayPauseButtonText);
        }

        [Fact]
        public void OpenImage_WhenUserCancels_OnlyCallsDialogServiceOnceAndDoesNotOpenSecondDialog()
        {
            var mockDialog = new MockDialogService { OpenFileDialogResult = null };
            using var vm = new SpriteSheetSlicerViewModel(dialogService: mockDialog);

            vm.OpenImage();

            Assert.Equal(1, mockDialog.ShowOpenFileDialogCallCount);
            Assert.Null(vm.SourceImage);
            Assert.True(vm.HasNoSourceImage);
        }

        [Fact]
        public void OpenInCanvas_WhenValidImageLoaded_OpensAnimationInTabAndClosesWindow()
        {
            var mockTabService = new MockTabService();
            using var vm = CreateViewModelWithSample(tabService: mockTabService);
            bool closeRequested = false;
            vm.RequestClose = () => closeRequested = true;

            vm.OpenInCanvas();

            Assert.Single(mockTabService.OpenedSprites);
            var (name, sprite) = mockTabService.OpenedSprites[0];
            Assert.Equal("Sliced Animation", name);
            Assert.True(sprite.IsAnimationEnabled);
            Assert.Equal(vm.Fps, sprite.FrameRateFps);
            Assert.Equal(4, sprite.Frames.Count);
            Assert.True(closeRequested);
        }

        [Fact]
        public void OpenInCanvas_UsesSourceFileNameForTabTitle()
        {
            var mockTabService = new MockTabService();
            using var vm = new SpriteSheetSlicerViewModel(tabService: mockTabService);
            var bmp = CreateTestBitmap(128, 64);
            vm.LoadImage(bmp, @"C:\Sprites\player_run.png");
            vm.Reslice();

            vm.OpenInCanvas();

            Assert.Single(mockTabService.OpenedSprites);
            Assert.Equal("player_run", mockTabService.OpenedSprites[0].Name);
        }

        [Fact]
        public void OpenAsSeparateTabs_WhenValidImageLoaded_OpensEachSliceInTabAndClosesWindow()
        {
            var mockTabService = new MockTabService();
            using var vm = CreateViewModelWithSample(tabService: mockTabService);
            bool closeRequested = false;
            vm.RequestClose = () => closeRequested = true;

            vm.OpenAsSeparateTabs();

            Assert.Equal(4, mockTabService.OpenedSprites.Count);
            Assert.Equal("Slice 1", mockTabService.OpenedSprites[0].Name);
            Assert.Equal("Slice 2", mockTabService.OpenedSprites[1].Name);
            Assert.Equal("Slice 3", mockTabService.OpenedSprites[2].Name);
            Assert.Equal("Slice 4", mockTabService.OpenedSprites[3].Name);
            Assert.True(closeRequested);
        }

        [Fact]
        public void OpenAsSeparateTabs_UsesSourceFileNameForPrefix()
        {
            var mockTabService = new MockTabService();
            using var vm = new SpriteSheetSlicerViewModel(tabService: mockTabService);
            var bmp = CreateTestBitmap(128, 64);
            vm.LoadImage(bmp, @"C:\Sprites\tileset.png");
            vm.Reslice();

            vm.OpenAsSeparateTabs();

            Assert.NotEmpty(mockTabService.OpenedSprites);
            Assert.StartsWith("tileset 1", mockTabService.OpenedSprites[0].Name);
        }

        [Fact]
        public void ParameterResetCommands_RestoreDefaultValues()
        {
            using var vm = CreateViewModelWithSample();

            vm.Contrast = 45;
            vm.Brightness = -30;
            vm.BrightnessThreshold = 200;
            vm.DitherAmount = 40;

            vm.ResetContrast();
            Assert.Equal(0, vm.Contrast);

            vm.ResetBrightness();
            Assert.Equal(0, vm.Brightness);

            vm.ResetThreshold();
            Assert.Equal(128, vm.BrightnessThreshold);

            vm.ResetDitherAmount();
            Assert.Equal(100, vm.DitherAmount);
        }

        [Fact]
        public void Dispose_CanBeCalledMultipleTimesSafely()
        {
            var vm = new SpriteSheetSlicerViewModel();
            vm.Dispose();
            vm.Dispose();
        }
    }
}
