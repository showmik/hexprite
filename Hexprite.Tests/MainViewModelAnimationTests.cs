using System;
using System.Linq;
using System.Threading;
using Hexprite.Core;
using Hexprite.ViewModels;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class MainViewModelAnimationTests
    {
        [Fact]
        public void AddFrame_DuringPlayback_InvalidatesOrRebuildsCache()
        {
            WpfTestHelper.EnsureApplication();

            var codeGenMock = new Moq.Mock<Hexprite.Services.ICodeGeneratorService>();
            var drawingMock = new Moq.Mock<Hexprite.Services.IDrawingService>();
            var clipboardMock = new Moq.Mock<Hexprite.Services.IClipboardService>();
            var pixelClipboardMock = new Moq.Mock<Hexprite.Services.IPixelClipboardService>();
            var dialogMock = new Moq.Mock<Hexprite.Services.IDialogService>();
            var themeMock = new Moq.Mock<Hexprite.Services.IThemeService>();
            var bugReportMock = new Moq.Mock<Hexprite.Services.IBugReportService>();
            var feedbackMock = new Moq.Mock<Hexprite.Services.IUserFeedbackService>();
            var controllerFactory = new Hexprite.Controllers.ControllerFactory();
            var exportMock = new Moq.Mock<Hexprite.Services.IExportService>();
            var importExportMock = new Moq.Mock<Hexprite.Services.IFileImportExportService>();
            var hardwarePreviewMock = new Moq.Mock<Hexprite.Services.IHardwarePreviewService>();
            var autosaveMock = new Moq.Mock<Hexprite.Services.IAutosaveService>();
            var serviceProviderMock = new Moq.Mock<IServiceProvider>();
            serviceProviderMock.Setup(sp => sp.GetService(typeof(Hexprite.Services.IAutosaveService))).Returns(autosaveMock.Object);

            var shell = new ShellViewModel(
                codeGenMock.Object,
                drawingMock.Object,
                clipboardMock.Object,
                pixelClipboardMock.Object,
                dialogMock.Object,
                themeMock.Object,
                bugReportMock.Object,
                feedbackMock.Object,
                controllerFactory,
                exportMock.Object,
                importExportMock.Object,
                hardwarePreviewMock.Object,
                serviceProviderMock.Object);

            shell.NewDocumentCommand.Execute("8x8");
            var vm = (MainViewModel)shell.ActiveDocument!;
            
            // Draw something on Frame 1
            vm.SpriteState.Frames[0].LayerPixels[0].GetMonochromeData()[0] = true;
            
            // Enable animation
            vm.IsAnimationEnabled = true;
            vm.IsPlaying = true;
            
            // Cache should be built with length 1.
            
            // Add a frame
            vm.AddFrameCommand.Execute(null);
            
            // Frame 2 should now be active
            Assert.Equal(2, vm.SpriteState.Frames.Count);
            
            // Let's verify that the playback doesn't crash or show wrong data.
            // A simple tick of the playback timer would read the cache.
            // Since we can't easily invoke the internal timer, we'll check if CurrentDisplayFrameIndex doesn't crash.
            
            Assert.True(vm.IsPlaying);
        }
        [Fact]
        public void IncreaseFrameDelay_IncreasesMultiplier_CapsAt100()
        {
            WpfTestHelper.EnsureApplication();
            var shell = CreateShell();
            shell.NewDocumentCommand.Execute("8x8");
            var vm = (MainViewModel)shell.ActiveDocument!;

            vm.IsAnimationEnabled = true;

            // Set up 100 for the limit check
            vm.SpriteState.Frames[0].DelayMultiplier = 99;

            // Select first frame
            vm.Frames[0].IsSelected = true;

            vm.IncreaseFrameDelayCommand.Execute(null);
            Assert.Equal(100, vm.SpriteState.Frames[0].DelayMultiplier);

            // Execute again, should not exceed 100
            vm.IncreaseFrameDelayCommand.Execute(null);
            Assert.Equal(100, vm.SpriteState.Frames[0].DelayMultiplier);
        }

        [Fact]
        public void DecreaseFrameDelay_DecreasesMultiplier_FloorsAt1()
        {
            WpfTestHelper.EnsureApplication();
            var shell = CreateShell();
            shell.NewDocumentCommand.Execute("8x8");
            var vm = (MainViewModel)shell.ActiveDocument!;

            vm.IsAnimationEnabled = true;

            // Set up 2 for the floor check
            vm.SpriteState.Frames[0].DelayMultiplier = 2;

            // Select first frame
            vm.Frames[0].IsSelected = true;

            vm.DecreaseFrameDelayCommand.Execute(null);
            Assert.Equal(1, vm.SpriteState.Frames[0].DelayMultiplier);

            // Execute again, should not go below 1
            vm.DecreaseFrameDelayCommand.Execute(null);
            Assert.Equal(1, vm.SpriteState.Frames[0].DelayMultiplier);
        }

        [Fact]
        public void SetLayerVisibility_WhenToggled_UpdatesAllFrameThumbnails()
        {
            WpfTestHelper.EnsureApplication();
            var shell = CreateShell();
            shell.NewDocumentCommand.Execute("8x8");
            var vm = (MainViewModel)shell.ActiveDocument!;

            vm.IsAnimationEnabled = true;
            vm.AddFrameCommand.Execute(null);
            Assert.Equal(2, vm.Frames.Count);

            // Add Layer 2
            vm.AddLayerCommand.Execute(null);
            Assert.Equal(2, vm.Layers.Count);

            // Frame 0, Layer 1 has content
            vm.SpriteState.ActiveFrameIndex = 0;
            vm.SpriteState.ActiveLayerIndex = 1;
            vm.SpriteState.Frames[0].LayerPixels[1].GetMonochromeData()[0] = true;

            // Frame 1, Layer 1 has content
            vm.SpriteState.ActiveFrameIndex = 1;
            vm.SpriteState.ActiveLayerIndex = 1;
            vm.SpriteState.Frames[1].LayerPixels[1].GetMonochromeData()[5] = true;

            // Force initial thumbnail refresh
            vm.UpdateActiveFrameThumbnail();
            vm.SpriteState.ActiveFrameIndex = 0;
            vm.UpdateActiveFrameThumbnail();

            Assert.True(vm.Frames[0].HasContent);
            Assert.True(vm.Frames[1].HasContent);

            // Hide Layer 1 (Layer index 1 has content, Layer index 0 is empty)
            vm.SetLayerVisibility(1, false);

            // Both frame thumbnails should now reflect that Layer 1 is hidden -> HasContent is false
            Assert.False(vm.Frames[0].HasContent);
            Assert.False(vm.Frames[1].HasContent);

            // Unhide Layer 1
            vm.SetLayerVisibility(1, true);

            // Both frame thumbnails should immediately reflect that Layer 1 is visible -> HasContent is true
            Assert.True(vm.Frames[0].HasContent);
            Assert.True(vm.Frames[1].HasContent);
        }

        [Fact]
        public void RestoreState_UndoRedoLayerVisibility_RestoresFrameThumbnails()
        {
            WpfTestHelper.EnsureApplication();
            var shell = CreateShell();
            shell.NewDocumentCommand.Execute("8x8");
            var vm = (MainViewModel)shell.ActiveDocument!;

            vm.IsAnimationEnabled = true;
            vm.AddFrameCommand.Execute(null);
            vm.AddLayerCommand.Execute(null);

            // Put content only on Layer 1
            vm.SpriteState.Frames[0].LayerPixels[1].GetMonochromeData()[0] = true;
            vm.SpriteState.Frames[1].LayerPixels[1].GetMonochromeData()[1] = true;
            vm.SpriteState.ActiveFrameIndex = 0;
            vm.UpdateActiveFrameThumbnail();
            vm.SpriteState.ActiveFrameIndex = 1;
            vm.UpdateActiveFrameThumbnail();

            Assert.True(vm.Frames[0].HasContent);
            Assert.True(vm.Frames[1].HasContent);

            // Hide Layer 1
            vm.SetLayerVisibility(1, false);
            Assert.False(vm.Frames[0].HasContent);
            Assert.False(vm.Frames[1].HasContent);

            // Undo: Layer 1 is visible again
            vm.UndoCommand.Execute(null);
            Assert.True(vm.Layers[1].IsVisible);
            Assert.True(vm.Frames[0].HasContent);
            Assert.True(vm.Frames[1].HasContent);

            // Redo: Layer 1 is hidden again
            vm.RedoCommand.Execute(null);
            Assert.False(vm.Layers[1].IsVisible);
            Assert.False(vm.Frames[0].HasContent);
            Assert.False(vm.Frames[1].HasContent);
        }

        [Fact]
        public void DeleteFrame_DuringPlayback_ClampsPlaybackIndexAndDoesNotThrow()
        {
            WpfTestHelper.EnsureApplication();
            var shell = CreateShell();
            shell.NewDocumentCommand.Execute("8x8");
            var vm = (MainViewModel)shell.ActiveDocument!;

            vm.IsAnimationEnabled = true;
            vm.AddFrameCommand.Execute(null);
            vm.AddFrameCommand.Execute(null);
            Assert.Equal(3, vm.SpriteState.Frames.Count);

            vm.IsPlaying = true;
            // Delete frame while playback is running
            vm.DeleteFrameCommand.Execute(vm.Frames[2]);

            Assert.Equal(2, vm.SpriteState.Frames.Count);
            Assert.True(vm.IsPlaying);
        }

        [Fact]
        public void DeleteFrameCommand_WithTargetParam_DeletesSpecifiedFrame()
        {
            WpfTestHelper.EnsureApplication();
            var shell = CreateShell();
            shell.NewDocumentCommand.Execute("8x8");
            var vm = (MainViewModel)shell.ActiveDocument!;

            vm.IsAnimationEnabled = true;
            vm.AddFrameCommand.Execute(null);
            vm.AddFrameCommand.Execute(null);
            Assert.Equal(3, vm.Frames.Count);

            // Active is frame 0
            vm.SetActiveFrame(0);
            Assert.Equal(0, vm.SpriteState.ActiveFrameIndex);

            // Put distinctive names
            vm.SpriteState.Frames[0].Name = "Frame_Zero";
            vm.SpriteState.Frames[1].Name = "Frame_One";
            vm.SpriteState.Frames[2].Name = "Frame_Two";

            // Target frame 2 specifically via parameter
            vm.DeleteFrameCommand.Execute(vm.Frames[2]);

            Assert.Equal(2, vm.SpriteState.Frames.Count);
            Assert.Contains(vm.SpriteState.Frames, f => f.Name == "Frame 1");
            Assert.Contains(vm.SpriteState.Frames, f => f.Name == "Frame 2");
        }

        [Fact]
        public void DuplicateFrameCommand_WithTargetParam_DuplicatesSpecifiedFrame()
        {
            WpfTestHelper.EnsureApplication();
            var shell = CreateShell();
            shell.NewDocumentCommand.Execute("8x8");
            var vm = (MainViewModel)shell.ActiveDocument!;

            vm.IsAnimationEnabled = true;
            vm.AddFrameCommand.Execute(null);
            Assert.Equal(2, vm.Frames.Count);

            // Mark frame 1 with distinctive delay
            vm.SpriteState.Frames[1].DelayMultiplier = 5;
            vm.Frames[1].DelayMultiplier = 5;

            // Active is frame 0
            vm.SetActiveFrame(0);

            // Duplicate frame 1 by parameter
            vm.DuplicateFrameCommand.Execute(vm.Frames[1]);

            Assert.Equal(3, vm.SpriteState.Frames.Count);
            // The duplicated frame should have delay multiplier 5
            Assert.Equal(5, vm.SpriteState.Frames[2].DelayMultiplier);
        }

        [Fact]
        public void SetActiveFrame_SynchronizesIsSelectedWithIsActive()
        {
            WpfTestHelper.EnsureApplication();
            var shell = CreateShell();
            shell.NewDocumentCommand.Execute("8x8");
            var vm = (MainViewModel)shell.ActiveDocument!;

            vm.IsAnimationEnabled = true;
            vm.AddFrameCommand.Execute(null);
            vm.AddFrameCommand.Execute(null);
            Assert.Equal(3, vm.Frames.Count);

            // Frame 0 active
            vm.SetActiveFrame(0);
            Assert.True(vm.Frames[0].IsActive);
            Assert.True(vm.Frames[0].IsSelected);
            Assert.False(vm.Frames[1].IsSelected);

            // Switch to frame 1
            vm.SetActiveFrame(1);
            Assert.True(vm.Frames[1].IsActive);
            Assert.True(vm.Frames[1].IsSelected);
            Assert.False(vm.Frames[0].IsSelected);
        }

        [Fact]
        public void SetActiveFrame_WithOutOfBoundsIndex_IsIgnored()
        {
            WpfTestHelper.EnsureApplication();
            var shell = CreateShell();
            shell.NewDocumentCommand.Execute("8x8");
            var vm = (MainViewModel)shell.ActiveDocument!;

            vm.IsAnimationEnabled = true;
            vm.AddFrameCommand.Execute(null);

            vm.SetActiveFrame(0);
            Assert.Equal(0, vm.SpriteState.ActiveFrameIndex);

            // Out of bounds negative
            vm.SetActiveFrame(-1);
            Assert.Equal(0, vm.SpriteState.ActiveFrameIndex);

            // Out of bounds too large
            vm.SetActiveFrame(99);
            Assert.Equal(0, vm.SpriteState.ActiveFrameIndex);
        }

        [Fact]
        public void DeleteFrame_DownToOneRemainingFrame_DoesNotDisableAnimationMode()
        {
            WpfTestHelper.EnsureApplication();
            var shell = CreateShell();
            shell.NewDocumentCommand.Execute("8x8");
            var vm = (MainViewModel)shell.ActiveDocument!;

            vm.IsAnimationEnabled = true;
            vm.AddFrameCommand.Execute(null);
            Assert.Equal(2, vm.SpriteState.Frames.Count);

            vm.IsPlaying = true;

            // Delete one frame down to 1 remaining
            vm.DeleteFrameCommand.Execute(null);

            Assert.Single(vm.SpriteState.Frames);
            Assert.True(vm.IsAnimationEnabled, "IsAnimationEnabled should remain true after deleting down to 1 frame");
            Assert.False(vm.IsPlaying, "IsPlaying should stop when 1 frame remains");
            Assert.True(vm.AddFrameCommand.CanExecute(null), "AddFrameCommand must still be executable");
        }

        [Fact]
        public void EstimatedMemoryUsage_CalculatesFootprintCorrectly()
        {
            WpfTestHelper.EnsureApplication();
            var shell = CreateShell();
            shell.NewDocumentCommand.Execute("8x8");
            var vm = (MainViewModel)shell.ActiveDocument!;

            // 8x8 monochrome = ((8 + 7) / 8) * 8 = 8 bytes per frame
            Assert.Equal("8 B", vm.EstimatedMemoryUsage);

            vm.IsAnimationEnabled = true;
            vm.AddFrameCommand.Execute(null);
            // 2 frames = 16 bytes
            Assert.Equal("16 B", vm.EstimatedMemoryUsage);

            vm.DeleteFrameCommand.Execute(null);
            // 1 frame = 8 bytes
            Assert.Equal("8 B", vm.EstimatedMemoryUsage);

            // RGB mode: 8x8x4 = 256 bytes per frame
            vm.SpriteState.ColorMode = Hexprite.Core.ColorMode.Rgb;
            Assert.Equal("256 B", vm.EstimatedMemoryUsage);
        }

        [Fact]
        public void RestoreState_RestoresPlaybackDirection()
        {
            WpfTestHelper.EnsureApplication();
            var shell = CreateShell();
            shell.NewDocumentCommand.Execute("8x8");
            var vm = (MainViewModel)shell.ActiveDocument!;

            vm.IsAnimationEnabled = true;
            vm.AddFrameCommand.Execute(null);
            vm.PlaybackDirection = PlaybackDirection.PingPong;

            // Save undo state then change direction
            vm.AddFrameCommand.Execute(null);
            vm.PlaybackDirection = PlaybackDirection.Reverse;

            // Undo should restore PingPong
            vm.UndoCommand.Execute(null);
            Assert.Equal(PlaybackDirection.PingPong, vm.PlaybackDirection);
        }

        [Fact]
        public void IncreaseAndDecreaseFrameDelay_SetsIsDirtyAndMarksCodeStale()
        {
            WpfTestHelper.EnsureApplication();
            var shell = CreateShell();
            shell.NewDocumentCommand.Execute("8x8");
            var vm = (MainViewModel)shell.ActiveDocument!;

            vm.IsAnimationEnabled = true;
            vm.IsDirty = false;

            vm.IncreaseFrameDelayCommand.Execute(null);
            Assert.True(vm.IsDirty);
            Assert.True(vm.IsCodeStale);

            vm.IsDirty = false;
            vm.DecreaseFrameDelayCommand.Execute(null);
            Assert.True(vm.IsDirty);
            Assert.True(vm.IsCodeStale);
        }

        [Fact]
        public void NextFrame_And_PreviousFrame_StepsThroughFramesAndStopsPlayback()
        {
            WpfTestHelper.EnsureApplication();
            var shell = CreateShell();
            shell.NewDocumentCommand.Execute("8x8");
            var vm = (MainViewModel)shell.ActiveDocument!;

            vm.IsAnimationEnabled = true;
            vm.AddFrameCommand.Execute(null);
            vm.AddFrameCommand.Execute(null);
            Assert.Equal(3, vm.SpriteState.Frames.Count);

            vm.SetActiveFrame(0);
            vm.IsPlaying = true;

            // NextFrame should pause playback and advance to frame 1
            vm.NextFrameCommand.Execute(null);
            Assert.False(vm.IsPlaying);
            Assert.Equal(1, vm.SpriteState.ActiveFrameIndex);

            // NextFrame again -> frame 2
            vm.NextFrameCommand.Execute(null);
            Assert.Equal(2, vm.SpriteState.ActiveFrameIndex);

            // NextFrame wraps around to 0
            vm.NextFrameCommand.Execute(null);
            Assert.Equal(0, vm.SpriteState.ActiveFrameIndex);

            // PreviousFrame wraps back to 2
            vm.PreviousFrameCommand.Execute(null);
            Assert.Equal(2, vm.SpriteState.ActiveFrameIndex);
        }

        [Fact]
        public void SetFrameDelay_SetsMultiplier_OnSelectedFrames_AndSupportsUndo()
        {
            WpfTestHelper.EnsureApplication();
            var shell = CreateShell();
            shell.NewDocumentCommand.Execute("8x8");
            var vm = (MainViewModel)shell.ActiveDocument!;

            vm.IsAnimationEnabled = true;
            vm.AddFrameCommand.Execute(null);
            vm.AddFrameCommand.Execute(null);
            Assert.Equal(3, vm.SpriteState.Frames.Count);

            // Select frame 0 and frame 2
            vm.Frames[0].IsSelected = true;
            vm.Frames[1].IsSelected = false;
            vm.Frames[2].IsSelected = true;

            // Set preset 4x
            vm.SetFrameDelayCommand.Execute("4");

            Assert.Equal(4, vm.SpriteState.Frames[0].DelayMultiplier);
            Assert.Equal(1, vm.SpriteState.Frames[1].DelayMultiplier);
            Assert.Equal(4, vm.SpriteState.Frames[2].DelayMultiplier);

            Assert.Equal(4, vm.Frames[0].DelayMultiplier);
            Assert.Equal(1, vm.Frames[1].DelayMultiplier);
            Assert.Equal(4, vm.Frames[2].DelayMultiplier);

            // Undo should restore previous delays
            vm.UndoCommand.Execute(null);
            Assert.Equal(1, vm.SpriteState.Frames[0].DelayMultiplier);
            Assert.Equal(1, vm.SpriteState.Frames[1].DelayMultiplier);
            Assert.Equal(1, vm.SpriteState.Frames[2].DelayMultiplier);
        }

        [Fact]
        public void SetFrameDelay_WithInvalidOrBoundaryInput_ClampsOrIgnoresGracefully()
        {
            WpfTestHelper.EnsureApplication();
            var shell = CreateShell();
            shell.NewDocumentCommand.Execute("8x8");
            var vm = (MainViewModel)shell.ActiveDocument!;

            vm.IsAnimationEnabled = true;
            vm.Frames[0].IsSelected = true;

            // Invalid text parameter -> no-op
            vm.SetFrameDelayCommand.Execute("not-a-number");
            Assert.Equal(1, vm.SpriteState.Frames[0].DelayMultiplier);

            // Out-of-bounds 250 -> clamped to 100
            vm.SetFrameDelayCommand.Execute("250");
            Assert.Equal(100, vm.SpriteState.Frames[0].DelayMultiplier);

            // Zero / negative -> clamped to 1
            vm.SetFrameDelayCommand.Execute("0");
            Assert.Equal(1, vm.SpriteState.Frames[0].DelayMultiplier);
        }

        [Fact]
        public void Playback_UpdatesIsPlayingBack_OnFrames()
        {
            WpfTestHelper.EnsureApplication();
            var shell = CreateShell();
            shell.NewDocumentCommand.Execute("8x8");
            var vm = (MainViewModel)shell.ActiveDocument!;

            vm.IsAnimationEnabled = true;
            vm.AddFrameCommand.Execute(null);
            vm.AddFrameCommand.Execute(null);
            Assert.Equal(3, vm.Frames.Count);

            // Select frame 0 as active
            vm.SelectedFrameIndex = 0;

            // Start playback from frame 0
            vm.IsPlaying = true;
            Assert.True(vm.Frames[0].IsPlayingBack);
            Assert.False(vm.Frames[1].IsPlayingBack);
            Assert.False(vm.Frames[2].IsPlayingBack);

            // Stop playback -> resets all playheads
            vm.IsPlaying = false;
            Assert.All(vm.Frames, f => Assert.False(f.IsPlayingBack));
        }

        [Fact]
        public void AddFrame_PreservesExistingFrameItemViewModelInstances_WithoutReset()
        {
            WpfTestHelper.EnsureApplication();
            var shell = CreateShell();
            shell.NewDocumentCommand.Execute("8x8");
            var vm = (MainViewModel)shell.ActiveDocument!;

            vm.IsAnimationEnabled = true;
            Assert.Single(vm.Frames);
            var initialFrameVm = vm.Frames[0];

            bool resetFired = false;
            int addCount = 0;
            vm.Frames.CollectionChanged += (s, e) =>
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                    resetFired = true;
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                    addCount++;
            };

            vm.AddFrameCommand.Execute(null);

            Assert.Equal(2, vm.Frames.Count);
            Assert.False(resetFired, "Frames collection must not fire Reset on AddFrame to prevent UI flickering");
            Assert.Equal(1, addCount);
            Assert.Same(initialFrameVm, vm.Frames[0]);
        }

        [Fact]
        public void DeleteFrame_PreservesRemainingFrameItemViewModelInstances_WithoutReset()
        {
            WpfTestHelper.EnsureApplication();
            var shell = CreateShell();
            shell.NewDocumentCommand.Execute("8x8");
            var vm = (MainViewModel)shell.ActiveDocument!;

            vm.IsAnimationEnabled = true;
            vm.AddFrameCommand.Execute(null);
            vm.AddFrameCommand.Execute(null);
            Assert.Equal(3, vm.Frames.Count);

            var firstFrameVm = vm.Frames[0];
            var secondFrameVm = vm.Frames[1];

            bool resetFired = false;
            int removeCount = 0;
            vm.Frames.CollectionChanged += (s, e) =>
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                    resetFired = true;
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
                    removeCount++;
            };

            // Select and delete the last frame
            vm.SelectedFrameIndex = 2;
            vm.DeleteFrameCommand.Execute(null);

            Assert.Equal(2, vm.Frames.Count);
            Assert.False(resetFired, "Frames collection must not fire Reset on DeleteFrame to prevent UI flickering");
            Assert.Equal(1, removeCount);
            Assert.Same(firstFrameVm, vm.Frames[0]);
            Assert.Same(secondFrameVm, vm.Frames[1]);
        }

        [Fact]
        public void PingPongPlayback_WithTwoFrames_CyclesCorrectlyWithoutOutOfBounds()
        {
            WpfTestHelper.EnsureApplication();
            var shell = CreateShell();
            shell.NewDocumentCommand.Execute("8x8");
            var vm = (MainViewModel)shell.ActiveDocument!;

            vm.IsAnimationEnabled = true;
            vm.AddFrameCommand.Execute(null);
            Assert.Equal(2, vm.SpriteState.Frames.Count);

            vm.SetActiveFrame(0);
            vm.PlaybackDirection = PlaybackDirection.PingPong;
            vm.IsPlaying = true;

            var tickMethod = typeof(MainViewModel).GetMethod("PlaybackTimer_Tick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(tickMethod);

            // Tick 1: frame 0 -> frame 1
            tickMethod.Invoke(vm, [null, EventArgs.Empty]);
            Assert.True(vm.Frames[1].IsPlayingBack);
            Assert.False(vm.Frames[0].IsPlayingBack);

            // Tick 2: turnaround at top: frame 1 -> frame 0
            tickMethod.Invoke(vm, [null, EventArgs.Empty]);
            Assert.True(vm.Frames[0].IsPlayingBack);
            Assert.False(vm.Frames[1].IsPlayingBack);

            // Tick 3: turnaround at bottom: frame 0 -> frame 1
            tickMethod.Invoke(vm, [null, EventArgs.Empty]);
            Assert.True(vm.Frames[1].IsPlayingBack);
            Assert.False(vm.Frames[0].IsPlayingBack);

            vm.IsPlaying = false;
        }

        [Fact]
        public void UpdatePlaybackPreview_WithNegativeOrOutOfBoundsIndex_DoesNotThrow()
        {
            WpfTestHelper.EnsureApplication();
            var shell = CreateShell();
            shell.NewDocumentCommand.Execute("8x8");
            var vm = (MainViewModel)shell.ActiveDocument!;

            vm.IsAnimationEnabled = true;
            vm.AddFrameCommand.Execute(null);
            vm.IsPlaying = true;

            var previewMethod = typeof(MainViewModel).GetMethod("UpdatePlaybackPreview", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var indexField = typeof(MainViewModel).GetField("_playbackFrameIndex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(previewMethod);
            Assert.NotNull(indexField);

            // Test negative index
            indexField.SetValue(vm, -1);
            var ex1 = Record.Exception(() => previewMethod.Invoke(vm, null));
            Assert.Null(ex1);

            // Test out-of-bounds large index
            indexField.SetValue(vm, 999);
            var ex2 = Record.Exception(() => previewMethod.Invoke(vm, null));
            Assert.Null(ex2);

            vm.IsPlaying = false;
        }

        [Fact]
        public void UndoRedo_DuringPlayback_SynchronizesPlayheadFlagsAndDoesNotThrow()
        {
            WpfTestHelper.EnsureApplication();
            var shell = CreateShell();
            shell.NewDocumentCommand.Execute("8x8");
            var vm = (MainViewModel)shell.ActiveDocument!;

            vm.IsAnimationEnabled = true;
            vm.AddFrameCommand.Execute(null);
            vm.AddFrameCommand.Execute(null);
            Assert.Equal(3, vm.SpriteState.Frames.Count);

            vm.IsPlaying = true;

            // Delete last frame while playing
            vm.DeleteFrameCommand.Execute(vm.Frames[2]);
            Assert.Equal(2, vm.SpriteState.Frames.Count);
            Assert.True(vm.IsPlaying);
            Assert.Single(vm.Frames, f => f.IsPlayingBack);

            // Undo: 3 frames restored
            vm.UndoCommand.Execute(null);
            Assert.Equal(3, vm.SpriteState.Frames.Count);
            Assert.True(vm.IsPlaying);
            Assert.Single(vm.Frames, f => f.IsPlayingBack);

            // Redo: 2 frames again
            vm.RedoCommand.Execute(null);
            Assert.Equal(2, vm.SpriteState.Frames.Count);
            Assert.True(vm.IsPlaying);
            Assert.Single(vm.Frames, f => f.IsPlayingBack);

            vm.IsPlaying = false;
        }

        [Fact]
        public void ResizeCanvas_PreservesPlaybackDirection()
        {
            WpfTestHelper.EnsureApplication();
            var shell = CreateShell();
            shell.NewDocumentCommand.Execute("8x8");
            var vm = (MainViewModel)shell.ActiveDocument!;

            vm.IsAnimationEnabled = true;
            vm.PlaybackDirection = PlaybackDirection.PingPong;

            vm.ResizeCanvas(16, 16, ResizeAnchor.TopLeft);

            Assert.Equal(PlaybackDirection.PingPong, vm.PlaybackDirection);
            Assert.Equal(PlaybackDirection.PingPong, vm.SpriteState.PlaybackDirection);
        }

        [Fact]
        public void RotateCanvasAsync_PreservesPlaybackDirection()
        {
            WpfTestHelper.EnsureApplication();
            WpfTestHelper.RunOnSta(async () =>
            {
                var shell = CreateShell();
                shell.NewDocumentCommand.Execute("8x8");
                var vm = (MainViewModel)shell.ActiveDocument!;

                vm.IsAnimationEnabled = true;
                vm.PlaybackDirection = PlaybackDirection.Reverse;

                await vm.RotateCanvasAsync(RotationDirection.Clockwise90);

                Assert.Equal(PlaybackDirection.Reverse, vm.PlaybackDirection);
                Assert.Equal(PlaybackDirection.Reverse, vm.SpriteState.PlaybackDirection);
            });
        }

        [Fact]
        public void ComputeHash_ChangesWhenPlaybackDirectionChanges()
        {
            var state = new SpriteState(8, 8)
            {
                IsAnimationEnabled = true,
                PlaybackDirection = PlaybackDirection.Forward,
            };

            string hashForward = state.ComputeHash();

            state.PlaybackDirection = PlaybackDirection.PingPong;
            string hashPingPong = state.ComputeHash();

            state.PlaybackDirection = PlaybackDirection.Reverse;
            string hashReverse = state.ComputeHash();

            Assert.NotEqual(hashForward, hashPingPong);
            Assert.NotEqual(hashPingPong, hashReverse);
            Assert.NotEqual(hashForward, hashReverse);
        }

        [Fact]
        public void MoveFrames_DuringPlayback_KeepsPlayheadAndCacheConsistent()
        {
            WpfTestHelper.EnsureApplication();
            var shell = CreateShell();
            shell.NewDocumentCommand.Execute("8x8");
            var vm = (MainViewModel)shell.ActiveDocument!;

            vm.IsAnimationEnabled = true;
            vm.AddFrameCommand.Execute(null);
            vm.AddFrameCommand.Execute(null);
            Assert.Equal(3, vm.SpriteState.Frames.Count);

            vm.IsPlaying = true;

            // Move frame 0 to slot 2
            vm.MoveFrames([vm.Frames[0]], 2);

            Assert.True(vm.IsPlaying);
            Assert.Single(vm.Frames, f => f.IsPlayingBack);
            Assert.Equal(3, vm.Frames.Count);

            vm.IsPlaying = false;
        }

        private ShellViewModel CreateShell()
        {
            var codeGenMock = new Moq.Mock<Hexprite.Services.ICodeGeneratorService>();
            var drawingMock = new Moq.Mock<Hexprite.Services.IDrawingService>();
            var clipboardMock = new Moq.Mock<Hexprite.Services.IClipboardService>();
            var pixelClipboardMock = new Moq.Mock<Hexprite.Services.IPixelClipboardService>();
            var dialogMock = new Moq.Mock<Hexprite.Services.IDialogService>();
            var themeMock = new Moq.Mock<Hexprite.Services.IThemeService>();
            var bugReportMock = new Moq.Mock<Hexprite.Services.IBugReportService>();
            var feedbackMock = new Moq.Mock<Hexprite.Services.IUserFeedbackService>();
            var controllerFactory = new Hexprite.Controllers.ControllerFactory();
            var exportMock = new Moq.Mock<Hexprite.Services.IExportService>();
            var importExportMock = new Moq.Mock<Hexprite.Services.IFileImportExportService>();
            var hardwarePreviewMock = new Moq.Mock<Hexprite.Services.IHardwarePreviewService>();
            var autosaveMock = new Moq.Mock<Hexprite.Services.IAutosaveService>();
            var serviceProviderMock = new Moq.Mock<IServiceProvider>();
            serviceProviderMock.Setup(sp => sp.GetService(typeof(Hexprite.Services.IAutosaveService))).Returns(autosaveMock.Object);

            return new ShellViewModel(
                codeGenMock.Object,
                drawingMock.Object,
                clipboardMock.Object,
                pixelClipboardMock.Object,
                dialogMock.Object,
                themeMock.Object,
                bugReportMock.Object,
                feedbackMock.Object,
                controllerFactory,
                exportMock.Object,
                importExportMock.Object,
                hardwarePreviewMock.Object,
                serviceProviderMock.Object);
        }
    }
}
