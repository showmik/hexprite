using System;
using System.Collections.Generic;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels.Flipper;
using Xunit;

namespace Hexprite.Tests.ViewModels
{
    [Trait("Category", "Unit")]
    public class FlipperExportViewModelTests
    {
        private static SpriteState CreateSampleSprite(int frameCount = 4)
        {
            var sprite = new SpriteState(128, 64);
            sprite.Frames.Clear();
            for (int i = 0; i < frameCount; i++)
            {
                sprite.Frames.Add(new FrameState
                {
                    LayerPixels = [new MonochromePixelBuffer(new bool[128 * 64])]
                });
            }
            return sprite;
        }

        [Fact]
        public void InitialState_WithoutCycle_CalculatesActiveFrames()
        {
            var sprite = CreateSampleSprite(4);
            var vm = new FlipperExportViewModel(sprite);

            Assert.Equal(4, vm.TotalFrames);
            Assert.Equal(0, vm.PassiveFrames);
            Assert.Equal(4, vm.ActiveFrames);
            Assert.False(vm.HasCycle);
            Assert.True(vm.IsPassiveFramesEditable);
            Assert.NotEmpty(vm.ManifestSnippet);
        }

        [Fact]
        public void InitialState_WithCycle_PopulatesCycleProperties()
        {
            var sprite = CreateSampleSprite(6);
            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                PassiveFrameCount = 2,
                ActiveFrameCount = 4,
                ActiveCycles = 3,
                Duration = 1800,
                ActiveCooldown = 5,
                FramesOrder = [0, 1, 2, 3, 4, 5],
                SpeechBubble = new FlipperSpeechBubble(1, 10, 15, "Hello Flipper", SpeechBubbleTailPosition.BottomLeft)
            };

            var vm = new FlipperExportViewModel(sprite);

            Assert.Equal(6, vm.TotalFrames);
            Assert.Equal(2, vm.PassiveFrames);
            Assert.Equal(4, vm.ActiveFrames);
            Assert.True(vm.HasCycle);
            Assert.False(vm.IsPassiveFramesEditable);
            Assert.Equal(3, vm.ActiveCycles);
            Assert.Equal(1800, vm.Duration);
            Assert.Equal(5, vm.ActiveCooldown);
            Assert.Equal("Hello Flipper", vm.SpeechBubbleText);
            Assert.Equal(10, vm.SpeechBubbleX);
            Assert.Equal(15, vm.SpeechBubbleY);
        }

        [Fact]
        public void UncheckingPreserveCycle_EnablesPassiveFramesEditing()
        {
            var sprite = CreateSampleSprite(4);
            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                PassiveFrameCount = 1,
                ActiveFrameCount = 3,
                FramesOrder = [0, 1, 2, 3]
            };

            var vm = new FlipperExportViewModel(sprite);
            Assert.False(vm.IsPassiveFramesEditable);

            vm.PreserveCycle = false;
            Assert.True(vm.IsPassiveFramesEditable);
        }

        [Fact]
        public void InvalidPassiveFrames_ShowsValidationError()
        {
            var sprite = CreateSampleSprite(4);
            var vm = new FlipperExportViewModel(sprite);

            vm.PassiveFrames = 10; // Exceeds total frames (4)
            Assert.True(vm.HasValidationError);
            Assert.Contains("must be between 0 and 4", vm.ValidationStatus);
        }

        [Fact]
        public void TryValidateInputs_ValidatesAndPopulatesSettings()
        {
            var sprite = CreateSampleSprite(4);
            var vm = new FlipperExportViewModel(sprite)
            {
                AnimationName = "Walk_Cycle",
                FrameRate = 10,
                PassiveFrames = 1,
                MinLevel = 2,
                MaxLevel = 15,
                MinButthurt = 1,
                MaxButthurt = 8,
                Weight = 4
            };

            bool ok = vm.TryValidateInputs("C:/out", out var settings, out var entry, out var exportSprite);

            Assert.True(ok);
            Assert.NotNull(settings);
            Assert.Equal("Walk_Cycle", settings.AnimationName);
            Assert.Equal(10, settings.FrameRate);
            Assert.Equal(1, settings.PassiveFrames);
            Assert.Equal(3, settings.ActiveFrames);
            Assert.Equal(2, entry.MinLevel);
            Assert.Equal(15, entry.MaxLevel);
        }

        private class MockWindowManager : IFlipperWindowManager
        {
            public bool SimulatorShown { get; set; }
            public bool DeployShown { get; set; }

            public void ShowSimulator(SpriteState? initialSprite = null) => SimulatorShown = true;
            public void ShowSimulator(
                System.Collections.Generic.IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> animations,
                string packName = "AssetPack") => SimulatorShown = true;
            public void ShowSimulator(
                System.Collections.Generic.IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> animations,
                string packName,
                FlipperSimulatorSettings? settings,
                Action<FlipperSimulatorSettings>? onSettingsChanged = null) => SimulatorShown = true;
            public void ShowMediaSlicer(SpriteState? initialSprite = null) { }
            public void ShowScheduleMatrix(System.Collections.Generic.IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>? pack = null, string packName = "Flipper Asset Pack") { }
            public void ShowScreenMirror() { }
            public void ShowDeploy(SpriteState? sprite = null) => DeployShown = true;
            public void ShowDeploy(IReadOnlyList<(string RelativePath, byte[] Data)> files, string packName = "AssetPack") => DeployShown = true;
            public void ShowDeploy(IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> animations, string packName = "AssetPack") => DeployShown = true;
            public bool? ShowExportDialog(SpriteState sprite, FlipperExportSettings settings) => true;
        }

        [Fact]
        public void DeployUsbCommand_WithWindowManager_InvokesShowDeploy()
        {
            var sprite = CreateSampleSprite(4);
            var winManager = new MockWindowManager();
            var vm = new FlipperExportViewModel(sprite, windowManager: winManager);

            vm.DeployUsbCommand.Execute(null);

            Assert.True(winManager.DeployShown);
        }

        [Fact]
        public void SimulateMatrixCommand_WithWindowManager_InvokesShowSimulator()
        {
            var sprite = CreateSampleSprite(4);
            var winManager = new MockWindowManager();
            var vm = new FlipperExportViewModel(sprite, windowManager: winManager);

            vm.SimulateMatrixCommand.Execute(null);

            Assert.True(winManager.SimulatorShown);
        }

        [Theory]
        [InlineData(0, FlipperExportTargetMode.SingleAnimation)]
        [InlineData(1, FlipperExportTargetMode.MomentumAssetPack)]
        [InlineData(2, FlipperExportTargetMode.StockDolphin)]
        public void TargetModes_MapProperlyInTryValidate(int index, FlipperExportTargetMode expectedMode)
        {
            var sprite = CreateSampleSprite(4);
            var vm = new FlipperExportViewModel(sprite)
            {
                SelectedTargetModeIndex = index
            };

            bool ok = vm.TryValidateInputs("C:/test", out var settings, out _, out _);

            Assert.True(ok);
            Assert.Equal(expectedMode, settings.TargetMode);
        }
    }
}
