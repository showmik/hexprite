using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels.Flipper;
using Moq;
using Xunit;

namespace Hexprite.Tests.ViewModels
{
    [Trait("Category", "Unit")]
    public class FlipperMediaSlicerViewModelTests
    {
        private class TestTabService : IWorkspaceTabService
        {
            public List<(string Name, SpriteState Sprite)> OpenedTabs { get; } = [];

            public void OpenSpritesInTabs(IEnumerable<SpriteState> sprites, string tabNamePrefix = "Imported")
            {
                int idx = 1;
                foreach (var s in sprites)
                {
                    OpenedTabs.Add(($"{tabNamePrefix} {idx++}", s));
                }
            }

            public void OpenSpritesInTabs(IEnumerable<(string Name, SpriteState Sprite)> sprites)
            {
                OpenedTabs.AddRange(sprites);
            }

            public void OpenSpriteInTab(SpriteState sprite, string title)
            {
                OpenedTabs.Add((title, sprite));
            }

            public SpriteState? GetActiveSpriteState() => null;
            public (string Title, SpriteState Sprite)? GetActiveSprite() => null;
            public bool[]? GetActiveFramePixels(bool animated = false) => null;
            public IReadOnlyList<(string Title, SpriteState Sprite)> GetAllOpenSprites() => [];
            public bool ActivateTabByTitle(string title) => false;
            public void OpenAssetPackInTab(IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>? pack = null, string packName = "Flipper Asset Pack") { }
        }

        [Fact]
        public void InitialState_CreatesSampleGrid_AndSlices4Frames()
        {
            var tabService = new TestTabService();
            using var vm = new FlipperMediaSlicerViewModel(tabService: tabService);

            Assert.NotNull(vm.SourceImage);
            Assert.Equal(512, vm.SourceImage.PixelWidth);
            Assert.Equal(64, vm.SourceImage.PixelHeight);
            Assert.Equal(4, vm.TotalFrames);
            Assert.Equal(0, vm.CurrentFrameIndex);
            Assert.Equal("Frame 1 / 4", vm.FrameCounterText);
            Assert.True(vm.IsPlaying);
            Assert.Equal("8 FPS", vm.FpsLabel);
        }

        [Fact]
        public void PlaybackControls_StepForwardAndBackward_WrapsCorrectly()
        {
            using var vm = new FlipperMediaSlicerViewModel();
            Assert.Equal(0, vm.CurrentFrameIndex);

            vm.StepForward();
            Assert.Equal(1, vm.CurrentFrameIndex);
            Assert.False(vm.IsPlaying);
            Assert.Equal("Frame 2 / 4", vm.FrameCounterText);

            vm.StepBackward();
            Assert.Equal(0, vm.CurrentFrameIndex);

            vm.StepBackward();
            Assert.Equal(3, vm.CurrentFrameIndex); // Wrapped to end
            Assert.Equal("Frame 4 / 4", vm.FrameCounterText);

            vm.AdvanceFrame(); // Will not advance when IsPlaying is false
            Assert.Equal(3, vm.CurrentFrameIndex);

            vm.IsPlaying = true;
            vm.AdvanceFrame();
            Assert.Equal(0, vm.CurrentFrameIndex);
        }

        [Fact]
        public void FpsChange_UpdatesFpsLabel()
        {
            using var vm = new FlipperMediaSlicerViewModel();
            vm.Fps = 15;
            Assert.Equal("15 FPS", vm.FpsLabel);
        }

        [Fact]
        public void LayoutAndDitherChanges_TriggerReslice()
        {
            using var vm = new FlipperMediaSlicerViewModel();
            vm.SelectedLayoutIndex = 1; // Vertical Strip
            Assert.NotNull(vm.SlicedSprite);

            vm.SelectedDitherIndex = 2; // Bayer8x8
            Assert.NotNull(vm.SlicedSprite);

            vm.BrightnessThreshold = 200;
            vm.DitherAmount = 50;
            vm.InvertColors = true;
            Assert.NotNull(vm.SlicedSprite);
        }

        [Fact]
        public void OpenInCanvasCommand_InvokesTabServiceAndRequestClose()
        {
            var tabService = new TestTabService();
            bool closed = false;
            using var vm = new FlipperMediaSlicerViewModel(tabService: tabService)
            {
                RequestClose = () => closed = true
            };

            vm.OpenInCanvasCommand.Execute(null);

            Assert.Single(tabService.OpenedTabs);
            Assert.Equal("Sliced Animation", tabService.OpenedTabs[0].Name);
            Assert.True(closed);
        }

        [Fact]
        public void LoadCustomImage_Horizontal_AutoSelectsHorizontalLayout()
        {
            using var vm = new FlipperMediaSlicerViewModel();
            var bmp = new WriteableBitmap(256, 64, 96, 96, PixelFormats.Bgra32, null);
            vm.LoadImage(bmp, "sheet.png");

            Assert.Equal(0, vm.SelectedLayoutIndex);
            Assert.Contains("sheet.png", vm.SourceInfoText);
        }

        [Fact]
        public void LoadCustomImage_Vertical_AutoSelectsVerticalLayout()
        {
            using var vm = new FlipperMediaSlicerViewModel();
            var bmp = new WriteableBitmap(64, 256, 96, 96, PixelFormats.Bgra32, null);
            vm.LoadImage(bmp, "sheet_vert.png");

            Assert.Equal(1, vm.SelectedLayoutIndex);
            Assert.Contains("sheet_vert.png", vm.SourceInfoText);
        }



        [Fact]
        public void InitialSprite_Constructor_LoadsSpriteDirectly()
        {
            var customSprite = new SpriteState(128, 64);
            customSprite.Frames.Clear();
            customSprite.Frames.Add(new FrameState { LayerPixels = [new MonochromePixelBuffer(new bool[128 * 64])] });
            customSprite.Frames.Add(new FrameState { LayerPixels = [new MonochromePixelBuffer(new bool[128 * 64])] });

            using var vm = new FlipperMediaSlicerViewModel(initialSprite: customSprite);

            Assert.Equal(2, vm.TotalFrames);
            Assert.Contains("Active Canvas", vm.SourceInfoText);
        }

        [Fact]
        public void TogglePlayPauseCommand_TogglesPlaybackAndLabel()
        {
            using var vm = new FlipperMediaSlicerViewModel();
            Assert.True(vm.IsPlaying);
            Assert.Equal("⏸ Pause", vm.PlayPauseButtonText);

            vm.TogglePlayPauseCommand.Execute(null);
            Assert.False(vm.IsPlaying);
            Assert.Equal("▶ Play", vm.PlayPauseButtonText);

            vm.TogglePlayPauseCommand.Execute(null);
            Assert.True(vm.IsPlaying);
            Assert.Equal("⏸ Pause", vm.PlayPauseButtonText);
        }

        [Fact]
        public void PrevFrameAndNextFrameCommands_StepPlayback()
        {
            using var vm = new FlipperMediaSlicerViewModel();
            Assert.Equal(0, vm.CurrentFrameIndex);

            vm.NextFrameCommand.Execute(null);
            Assert.Equal(1, vm.CurrentFrameIndex);
            Assert.False(vm.IsPlaying);

            vm.PrevFrameCommand.Execute(null);
            Assert.Equal(0, vm.CurrentFrameIndex);
        }

        [Fact]
        public void ExportFolderCommand_WithTargetFolder_ExportsAnimation()
        {
            var exportMock = new Mock<IFlipperExportService>();
            var dialogMock = new Mock<IDialogService>();
            dialogMock.Setup(d => d.ShowOpenFolderDialog(It.IsAny<string>())).Returns("C:/FlipperExports");

            using var vm = new FlipperMediaSlicerViewModel(exportService: exportMock.Object, dialogService: dialogMock.Object);
            vm.ExportFolderCommand.Execute(null);

            exportMock.Verify(e => e.ExportAnimation(It.IsAny<SpriteState>(), It.Is<FlipperExportSettings>(s => s.TargetFolder == "C:/FlipperExports")));
            dialogMock.Verify(d => d.ShowMessage(It.Is<string>(s => s.Contains("Successfully exported")), "Export Complete", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information));
        }

        [Fact]
        public void ThemePalettes_ContainsPredefinedThemes_AndCanSwitchPalette()
        {
            using var vm = new FlipperMediaSlicerViewModel();
            Assert.NotEmpty(vm.ThemePalettes);
            Assert.Equal(0, vm.SelectedPaletteIndex);

            vm.SelectedPaletteIndex = 1; // Amber CRT
            Assert.Equal(1, vm.SelectedPaletteIndex);

            vm.ShowLcdGrid = false;
            Assert.False(vm.ShowLcdGrid);
        }

        [Fact]
        public void FirstFrameAndLastFrameCommands_JumpToBoundaries()
        {
            using var vm = new FlipperMediaSlicerViewModel();
            Assert.Equal(0, vm.CurrentFrameIndex);
            Assert.Equal(4, vm.TotalFrames);
            Assert.Equal(3, vm.MaxFrameIndex);

            vm.LastFrameCommand.Execute(null);
            Assert.Equal(3, vm.CurrentFrameIndex);
            Assert.False(vm.IsPlaying);

            vm.FirstFrameCommand.Execute(null);
            Assert.Equal(0, vm.CurrentFrameIndex);
            Assert.False(vm.IsPlaying);
        }

        [Fact]
        public void TimelineScrubber_DirectIndexChange_UpdatesFrameCounter()
        {
            using var vm = new FlipperMediaSlicerViewModel();
            vm.CurrentFrameIndex = 2;

            Assert.Equal("Frame 3 / 4", vm.FrameCounterText);
        }
    }
}
