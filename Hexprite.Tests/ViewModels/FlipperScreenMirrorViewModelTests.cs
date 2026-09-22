using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels.Flipper;
using Moq;
using Xunit;

namespace Hexprite.Tests.ViewModels
{
    [Trait("Category", "Unit")]
    public class FlipperScreenMirrorViewModelTests
    {
        private class MockStreamService : IFlipperScreenStreamService
        {
            public bool IsConnected { get; set; }
            public bool IsStreaming { get; set; }
            public bool AutoReconnect { get; set; } = true;
            public string? CurrentPort { get; set; }
            public int CurrentFps { get; set; }
            public long TotalFramesSent { get; set; }

            public event EventHandler<bool>? ConnectionStateChanged;
            public event EventHandler<int>? FpsUpdated;

            public Task<List<FlipperDeviceInfo>> ScanDevicesAsync(CancellationToken ct = default)
            {
                return Task.FromResult(new List<FlipperDeviceInfo>
                {
                    new("COM3", "Flipper Zero (COM3)", true)
                });
            }

            public Task<bool> ConnectAsync(string portName, CancellationToken ct = default)
            {
                IsConnected = true;
                CurrentPort = portName;
                ConnectionStateChanged?.Invoke(this, true);
                return Task.FromResult(true);
            }

            public void Disconnect()
            {
                IsConnected = false;
                IsStreaming = false;
                CurrentPort = null;
                ConnectionStateChanged?.Invoke(this, false);
            }

            public void StartStreaming(Func<bool[]?> getFramePixels, int targetFps = 20)
            {
                IsStreaming = true;
                CurrentFps = targetFps;
                TotalFramesSent += 1;
                FpsUpdated?.Invoke(this, targetFps);
            }

            public void StopStreaming()
            {
                IsStreaming = false;
            }

            public bool SendSingleFrame(bool[] pixels128x64) => true;

            public bool SendSingleFrame(SpriteState sprite, int frameIndex = 0) => true;

            public void Dispose()
            {
                Disconnect();
            }
        }

        [Fact]
        public async Task InitialState_ScansAndFindsDevices()
        {
            var mock = new MockStreamService();
            using var vm = new FlipperScreenMirrorViewModel(mock);

            await vm.ScanDevicesAsync();

            Assert.Single(vm.Devices);
            Assert.NotNull(vm.SelectedDevice);
            Assert.Equal("COM3", vm.SelectedDevice.PortName);
            Assert.False(vm.IsConnected);
            Assert.Equal("Disconnected", vm.StatusText);
        }

        [Fact]
        public async Task ToggleConnect_ConnectsAndStreams()
        {
            var mock = new MockStreamService();
            using var vm = new FlipperScreenMirrorViewModel(mock);

            await vm.ScanDevicesAsync();
            await vm.ToggleConnect();

            Assert.True(vm.IsConnected);
            Assert.Contains("Streaming", vm.StatusText);
            Assert.Contains("Disconnect", vm.ConnectButtonText);

            await vm.ToggleConnect();
            Assert.False(vm.IsConnected);
            Assert.Equal("Disconnected", vm.StatusText);
        }

        [Fact]
        public void TargetFpsChange_UpdatesLabelAndStreamer()
        {
            var mock = new MockStreamService();
            using var vm = new FlipperScreenMirrorViewModel(mock);

            vm.TargetFps = 25;
            Assert.Equal("25 FPS", vm.TargetFpsLabel);
            Assert.Contains("USB Stream 25 FPS", vm.MetricsText);

            vm.SetFpsCommand.Execute("60");
            Assert.Equal(60, vm.TargetFps);
            Assert.Equal("60 FPS", vm.TargetFpsLabel);
            Assert.Contains("USB Stream 60 FPS", vm.MetricsText);
        }

        [Fact]
        public void SetSourceMode_UpdatesModeAndAnimationFlag()
        {
            var mock = new MockStreamService();
            using var vm = new FlipperScreenMirrorViewModel(mock);

            vm.SetSourceModeCommand.Execute("Animation");
            Assert.Equal(FlipperStreamSourceMode.AnimationLoop, vm.SourceMode);
            Assert.True(vm.IsAnimationSource);

            vm.SetSourceModeCommand.Execute("Studio");
            Assert.Equal(FlipperStreamSourceMode.AssetPackStudio, vm.SourceMode);

            vm.SetSourceModeCommand.Execute("Canvas");
            Assert.Equal(FlipperStreamSourceMode.ActiveCanvas, vm.SourceMode);
            Assert.False(vm.IsAnimationSource);
        }

        [Fact]
        public async Task TogglePauseStream_Connected_TogglesPauseState()
        {
            var mock = new MockStreamService();
            using var vm = new FlipperScreenMirrorViewModel(mock);

            await vm.ScanDevicesAsync();
            await vm.ToggleConnect();

            Assert.True(vm.IsConnected);
            Assert.False(vm.IsStreamPaused);
            Assert.Equal("⏸ Pause", vm.PauseButtonText);

            vm.TogglePauseStreamCommand.Execute(null);
            Assert.True(vm.IsStreamPaused);
            Assert.Equal("▶ Resume", vm.PauseButtonText);
            Assert.Contains("Paused", vm.StatusText);

            vm.TogglePauseStreamCommand.Execute(null);
            Assert.False(vm.IsStreamPaused);
            Assert.Equal("⏸ Pause", vm.PauseButtonText);
            Assert.Contains("Streaming", vm.StatusText);
        }

        [Fact]
        public void TogglePauseStream_Disconnected_TogglesOfflinePauseState()
        {
            var mock = new MockStreamService();
            using var vm = new FlipperScreenMirrorViewModel(mock);

            Assert.False(vm.IsConnected);
            Assert.False(vm.IsStreamPaused);
            Assert.Equal("⏸ Pause", vm.PauseButtonText);
            Assert.Equal("Disconnected", vm.StatusText);

            // Pause in offline mode
            vm.TogglePauseStreamCommand.Execute(null);
            Assert.True(vm.IsStreamPaused);
            Assert.Equal("▶ Resume", vm.PauseButtonText);
            Assert.Contains("Paused", vm.StatusText);

            // Resume in offline mode
            vm.TogglePauseStreamCommand.Execute(null);
            Assert.False(vm.IsStreamPaused);
            Assert.Equal("⏸ Pause", vm.PauseButtonText);
            Assert.Equal("Disconnected", vm.StatusText);
        }

        [Fact]
        public async Task SendSnapshot_Connected_SendsFrame()
        {
            var mock = new MockStreamService();
            var mockTab = new MockTabService { FramePixels = new bool[128 * 64] };
            mockTab.FramePixels[0] = true;

            using var vm = new FlipperScreenMirrorViewModel(mock, tabService: mockTab);

            await vm.ScanDevicesAsync();
            await vm.ToggleConnect();

            vm.SendSnapshotCommand.Execute(null);
            Assert.Contains("Sent frame snapshot", vm.SnapshotFeedbackText);
        }

        [Fact]
        public void PalettesAndGrid_RenderPreview_UpdatesState()
        {
            var mock = new MockStreamService();
            using var vm = new FlipperScreenMirrorViewModel(mock);

            Assert.NotEmpty(vm.ThemePalettes);
            Assert.True(vm.ShowLcdGrid);

            bool[] pixels = new bool[128 * 64];
            pixels[10] = true;

            vm.RenderPreview(pixels);
            vm.SelectedPaletteIndex = 1; // Dark OLED
            vm.ShowLcdGrid = false;

            Assert.Equal(1, vm.SelectedPaletteIndex);
            Assert.False(vm.ShowLcdGrid);
        }

        private class MockTabService : IWorkspaceTabService
        {
            public bool[]? FramePixels { get; set; }
            public SpriteState? ActiveSprite { get; set; }
            public void OpenSpritesInTabs(IEnumerable<SpriteState> sprites, string tabNamePrefix = "Imported") { }
            public void OpenSpritesInTabs(IEnumerable<(string Name, SpriteState Sprite)> sprites) { }
            public void OpenSpriteInTab(SpriteState sprite, string title) { }
            public SpriteState? GetActiveSpriteState() => ActiveSprite;
            public (string Title, SpriteState Sprite)? GetActiveSprite() => ActiveSprite != null ? ("Mock", ActiveSprite) : null;
            public bool[]? GetActiveFramePixels(bool animated = false) => FramePixels ?? ActiveSprite?.CompositeFramePixels(0);
            public IReadOnlyList<(string Title, SpriteState Sprite)> GetAllOpenSprites() => [];
            public bool ActivateTabByTitle(string title) => false;
            public void OpenAssetPackInTab(IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>? pack = null, string packName = "Flipper Asset Pack") { }
        }

        [Fact]
        public async Task ToggleConnect_WithoutSelectedDevice_ShowsWarning()
        {
            var mockStream = new MockStreamService();
            var dialogMock = new Mock<IDialogService>();

            using var vm = new FlipperScreenMirrorViewModel(mockStream, dialogService: dialogMock.Object)
            {
                SelectedDevice = null
            };

            await vm.ToggleConnect();

            dialogMock.Verify(d => d.ShowMessage(It.Is<string>(s => s.Contains("valid COM port")), "Screen Mirror", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning));
        }

        [Fact]
        public void NormalizeTo128x64_CentersNon128x64Pixels()
        {
            bool[] src32x32 = new bool[32 * 32];
            src32x32[0] = true; // Top-left pixel of 32x32

            var normalized = FlipperScreenStreamService.NormalizeTo128x64(src32x32, 32, 32);

            Assert.Equal(128 * 64, normalized.Length);
            // Center offsets: X = (128-32)/2 = 48, Y = (64-32)/2 = 16
            // Position = 16 * 128 + 48 = 2096
            Assert.True(normalized[16 * 128 + 48]);
            Assert.False(normalized[0]); // Top-left of 128x64 should remain false
        }

        [Fact]
        public void NormalizeTo128x64_CentersNonSquare64x32PixelsWithoutShearing()
        {
            // 64 wide x 32 high
            bool[] src64x32 = new bool[64 * 32];
            src64x32[0] = true; // Top-left (0,0)
            src64x32[1 * 64 + 0] = true; // (0, 1)

            var normalized = FlipperScreenStreamService.NormalizeTo128x64(src64x32, 64, 32);

            Assert.Equal(128 * 64, normalized.Length);
            // Centered offsets: X = (128 - 64)/2 = 32, Y = (64 - 32)/2 = 16
            int expected0 = 16 * 128 + 32;
            int expected1 = 17 * 128 + 32;

            Assert.True(normalized[expected0]);
            Assert.True(normalized[expected1]);
        }

        [Fact]
        public void ActiveStateIndicators_ReflectSelectedSourceAndFps()
        {
            var mock = new MockStreamService();
            using var vm = new FlipperScreenMirrorViewModel(mock);

            // Default
            Assert.True(vm.IsCanvasSourceActive);
            Assert.False(vm.IsAnimationSourceActive);
            Assert.False(vm.IsStudioSourceActive);
            Assert.True(vm.IsFps20Active);
            Assert.False(vm.IsFps60Active);

            // Change Source Mode
            vm.SetSourceModeCommand.Execute("Animation");
            Assert.False(vm.IsCanvasSourceActive);
            Assert.True(vm.IsAnimationSourceActive);
            Assert.False(vm.IsStudioSourceActive);

            vm.SetSourceModeCommand.Execute("Studio");
            Assert.False(vm.IsCanvasSourceActive);
            Assert.False(vm.IsAnimationSourceActive);
            Assert.True(vm.IsStudioSourceActive);

            // Change FPS
            vm.SetFpsCommand.Execute("60");
            Assert.False(vm.IsFps20Active);
            Assert.True(vm.IsFps60Active);

            vm.SetFpsCommand.Execute("10");
            Assert.True(vm.IsFps10Active);
            Assert.False(vm.IsFps60Active);
        }

        [Fact]
        public async Task ScanDevicesAsync_PreservesSelectedPort()
        {
            var mock = new MockStreamService();
            using var vm = new FlipperScreenMirrorViewModel(mock);

            vm.SelectedDevice = new FlipperDeviceInfo("COM9", "Custom Device", true);
            await vm.ScanDevicesAsync();

            // When COM9 is not in scan result, defaults to first found
            Assert.NotNull(vm.SelectedDevice);
            Assert.Equal("COM3", vm.SelectedDevice.PortName);
        }

        [Fact]
        public void GetFramePixels_Non128x64_NormalizesCleanly()
        {
            var mock = new MockStreamService();
            var sprite = new SpriteState(64, 32);
            var mockTab = new MockTabService { ActiveSprite = sprite };

            using var vm = new FlipperScreenMirrorViewModel(mock, tabService: mockTab);

            var px = vm.GetFramePixels();
            Assert.NotNull(px);
            Assert.Equal(128 * 64, px.Length);
        }

        [Fact]
        public void GetIdleScreenBuffer_ReturnsValid128x64Banner()
        {
            var idle = FlipperScreenMirrorViewModel.GetIdleScreenBuffer();
            Assert.NotNull(idle);
            Assert.Equal(128 * 64, idle.Length);
            // Has active pixels for text/border
            Assert.Contains(true, idle);
        }

        [Fact]
        public void GetIdleScreenBuffer_TextIsCenteredAndWithinBounds()
        {
            var idle = FlipperScreenMirrorViewModel.GetIdleScreenBuffer();
            Assert.NotNull(idle);

            // Verify that all active text pixels (excluding corner brackets) are within safe margins
            // Line 1: Y in [20..30], Line 2: Y in [36..43]
            for (int y = 10; y < 55; y++)
            {
                // Left margin check (no pixels before X = 5 for text lines)
                for (int x = 0; x < 5; x++)
                {
                    Assert.False(idle[y * 128 + x], $"Pixel at ({x},{y}) should be within text margin.");
                }

                // Right margin check (no pixels after X = 122 for text lines)
                for (int x = 123; x < 128; x++)
                {
                    Assert.False(idle[y * 128 + x], $"Pixel at ({x},{y}) should be within text margin.");
                }
            }
        }

        [Fact]
        public void Constructor_WithMultiFrameSprite_AutoSelectsAnimationLoop()
        {
            var mock = new MockStreamService();
            var sprite = new SpriteState(64, 32);
            sprite.Frames.Add(new FrameState { LayerPixels = [new MonochromePixelBuffer(64 * 32)] }); // 2 frames total
            var mockTab = new MockTabService { ActiveSprite = sprite };

            using var vm = new FlipperScreenMirrorViewModel(mock, tabService: mockTab);

            Assert.Equal(FlipperStreamSourceMode.AnimationLoop, vm.SourceMode);
            Assert.True(vm.IsAnimationSourceActive);
            Assert.Equal($"Native: {sprite.FrameRateFps} FPS", vm.NativeFpsText);
        }

        [Fact]
        public void SpeedMultiplier_Commands_UpdateActiveIndicatorsAndMetrics()
        {
            var mock = new MockStreamService();
            using var vm = new FlipperScreenMirrorViewModel(mock);

            // Default
            Assert.Equal(1.0, vm.SpeedMultiplier);
            Assert.True(vm.IsSpeed10xActive);
            Assert.False(vm.IsSpeed05xActive);
            Assert.False(vm.IsSpeed20xActive);

            // Set to 0.5x
            vm.SetSpeedMultiplierCommand.Execute("0.5");
            Assert.Equal(0.5, vm.SpeedMultiplier);
            Assert.True(vm.IsSpeed05xActive);
            Assert.False(vm.IsSpeed10xActive);

            // Set to 2.0x
            vm.SetSpeedMultiplierCommand.Execute("2.0");
            Assert.Equal(2.0, vm.SpeedMultiplier);
            Assert.True(vm.IsSpeed20xActive);
            Assert.False(vm.IsSpeed10xActive);

            // Set to 1.5x
            vm.SetSpeedMultiplierCommand.Execute("1.5");
            Assert.Equal(1.5, vm.SpeedMultiplier);
            Assert.True(vm.IsSpeed15xActive);
        }
    }
}
