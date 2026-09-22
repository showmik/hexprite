using System;
using System.Collections.Generic;
using Hexprite.Core;
using Hexprite.ViewModels.Flipper;
using Hexprite.Views;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FlipperMatrixSimulatorWindowTests
    {
        [Fact]
        public void FlipperMatrixSimulatorWindow_InitializesWithoutResourceOrXamlException()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var sprite = new SpriteState(128, 64);
                var entry = new FlipperManifestEntry
                {
                    Name = "TestDolphin",
                    MinLevel = 1,
                    MaxLevel = 30,
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = 1
                };

                var list = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
                {
                    ("TestDolphin", sprite, entry)
                };

                var win = new FlipperMatrixSimulatorWindow(list, "TestPack");
                Assert.NotNull(win);
                Assert.Equal("Loaded Pack: TestPack (1 animation)", win.TxtManifestInfo.Text);
                Assert.NotNull(win.LstCandidates.ItemsSource);
            });
        }

        [Fact]
        public void GetPassiveSequence_NoCycle_ReturnsAllFrames()
        {
            var sprite = new SpriteState(128, 64);
            sprite.Frames.Clear();
            sprite.Frames.Add(new FrameState());
            sprite.Frames.Add(new FrameState());
            sprite.Frames.Add(new FrameState());

            var seq = FlipperMatrixSimulatorWindow.GetPassiveSequence(sprite);
            Assert.Equal([0, 1, 2], seq);
        }

        [Fact]
        public void GetActiveSequence_NoCycle_ReturnsEmpty()
        {
            var sprite = new SpriteState(128, 64);
            sprite.Frames.Clear();
            sprite.Frames.Add(new FrameState());
            sprite.Frames.Add(new FrameState());

            var seq = FlipperMatrixSimulatorWindow.GetActiveSequence(sprite);
            Assert.Empty(seq);
        }

        [Fact]
        public void GetPassiveSequence_WithCycle_ReturnsFirstNFrames()
        {
            var sprite = new SpriteState(128, 64);
            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                PassiveFrameCount = 3,
                ActiveFrameCount = 2,
                FramesOrder = [0, 1, 0, 2, 3]
            };

            var seq = FlipperMatrixSimulatorWindow.GetPassiveSequence(sprite);
            Assert.Equal([0, 1, 0], seq);
        }

        [Fact]
        public void GetActiveSequence_WithCycle_ReturnsActiveSlice()
        {
            var sprite = new SpriteState(128, 64);
            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                PassiveFrameCount = 2,
                ActiveFrameCount = 3,
                FramesOrder = [0, 1, 2, 3, 2]
            };

            var seq = FlipperMatrixSimulatorWindow.GetActiveSequence(sprite);
            Assert.Equal([2, 3, 2], seq);
        }

        [Fact]
        public void GetPassiveSequence_PassiveCountExceedsFramesOrder_Clamps()
        {
            var sprite = new SpriteState(128, 64);
            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                PassiveFrameCount = 10,
                FramesOrder = [0, 1, 2]
            };

            var seq = FlipperMatrixSimulatorWindow.GetPassiveSequence(sprite);
            Assert.Equal([0, 1, 2], seq);
        }

        [Fact]
        public void GetActiveSequence_ActiveCountExceedsRemaining_Clamps()
        {
            var sprite = new SpriteState(128, 64);
            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                PassiveFrameCount = 2,
                ActiveFrameCount = 10,
                FramesOrder = [0, 1, 2, 3]
            };

            var seq = FlipperMatrixSimulatorWindow.GetActiveSequence(sprite);
            Assert.Equal([2, 3], seq);
        }

        [Fact]
        public void GetPassiveSequence_EmptyFramesOrder_FallsBack()
        {
            var sprite = new SpriteState(128, 64);
            sprite.Frames.Clear();
            sprite.Frames.Add(new FrameState());
            sprite.Frames.Add(new FrameState());
            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                PassiveFrameCount = 2,
                FramesOrder = []
            };

            var seq = FlipperMatrixSimulatorWindow.GetPassiveSequence(sprite);
            Assert.Equal([0, 1], seq);
        }

        [Fact]
        public void TriggerActive_NoCycle_StaysPassive()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var sprite = new SpriteState(128, 64);
                sprite.Frames.Clear();
                sprite.Frames.Add(new FrameState { LayerPixels = new List<IPixelBuffer> { new MonochromePixelBuffer(new bool[128 * 64]) } });

                var entry = new FlipperManifestEntry
                {
                    Name = "NoCycleDolphin",
                    MinLevel = 1,
                    MaxLevel = 30,
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = 1
                };

                var list = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
                {
                    ("NoCycleDolphin", sprite, entry)
                };

                var win = new FlipperMatrixSimulatorWindow(list, "TestPack");
                Assert.Equal(FlipperPlaybackMode.Passive, win.PlaybackMode);

                // TriggerActive should be a no-op when active sequence is empty
                win.TriggerActive();
                Assert.Equal(FlipperPlaybackMode.Passive, win.PlaybackMode);
            });
        }

        [Fact]
        public void AdvanceFrame_SingleFramePassive_StaysAtZero()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var sprite = new SpriteState(128, 64);
                sprite.Frames.Clear();
                sprite.Frames.Add(new FrameState { LayerPixels = new List<IPixelBuffer> { new MonochromePixelBuffer(new bool[128 * 64]) } });

                var entry = new FlipperManifestEntry
                {
                    Name = "SingleFrame",
                    MinLevel = 1,
                    MaxLevel = 30,
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = 1
                };

                var list = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
                {
                    ("SingleFrame", sprite, entry)
                };

                var win = new FlipperMatrixSimulatorWindow(list, "TestPack");
                Assert.Equal(0, win.SequenceIndex);

                win.AdvanceFrame();
                Assert.Equal(0, win.SequenceIndex);
            });
        }

        [Fact]
        public void AdvanceFrame_ActiveWithMultipleCycles_LoopsCorrectly()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var sprite = new SpriteState(128, 64);
                sprite.Frames.Clear();
                for (int i = 0; i < 4; i++)
                    sprite.Frames.Add(new FrameState { LayerPixels = new List<IPixelBuffer> { new MonochromePixelBuffer(new bool[128 * 64]) } });

                sprite.FlipperCycle = new FlipperAnimationCycle
                {
                    PassiveFrameCount = 2,
                    ActiveFrameCount = 2,
                    ActiveCycles = 2,
                    ActiveCooldown = 1,
                    FramesOrder = [0, 1, 2, 3]
                };

                var entry = new FlipperManifestEntry
                {
                    Name = "MultiCycle",
                    MinLevel = 1,
                    MaxLevel = 30,
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = 1
                };

                var win = new FlipperMatrixSimulatorWindow(new[] { ("MultiCycle", sprite, entry) }, "TestPack");
                win.TriggerActive();
                Assert.Equal(FlipperPlaybackMode.Active, win.PlaybackMode);
                Assert.Equal(0, win.SequenceIndex);

                // Advance cycle 1 frame 1
                win.AdvanceFrame();
                Assert.Equal(FlipperPlaybackMode.Active, win.PlaybackMode);
                Assert.Equal(1, win.SequenceIndex);

                // Advance -> starts cycle 2 at frame 0
                win.AdvanceFrame();
                Assert.Equal(FlipperPlaybackMode.Active, win.PlaybackMode);
                Assert.Equal(0, win.SequenceIndex);

                // Advance cycle 2 frame 1
                win.AdvanceFrame();
                Assert.Equal(FlipperPlaybackMode.Active, win.PlaybackMode);
                Assert.Equal(1, win.SequenceIndex);

                // Advance -> completes cycles, transitions to Cooldown
                win.AdvanceFrame();
                Assert.Equal(FlipperPlaybackMode.Cooldown, win.PlaybackMode);

                // Advance cooldown -> back to Passive
                win.AdvanceFrame();
                Assert.Equal(FlipperPlaybackMode.Passive, win.PlaybackMode);
            });
        }

        [Fact]
        public void AdvanceFrame_ActiveZeroCooldown_SkipsCooldownToPassive()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var sprite = new SpriteState(128, 64);
                sprite.Frames.Clear();
                for (int i = 0; i < 4; i++)
                    sprite.Frames.Add(new FrameState { LayerPixels = new List<IPixelBuffer> { new MonochromePixelBuffer(new bool[128 * 64]) } });

                sprite.FlipperCycle = new FlipperAnimationCycle
                {
                    PassiveFrameCount = 2,
                    ActiveFrameCount = 2,
                    ActiveCycles = 1,
                    ActiveCooldown = 0,
                    FramesOrder = [0, 1, 2, 3]
                };

                var entry = new FlipperManifestEntry
                {
                    Name = "ZeroCooldown",
                    MinLevel = 1,
                    MaxLevel = 30,
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = 1
                };

                var win = new FlipperMatrixSimulatorWindow(new[] { ("ZeroCooldown", sprite, entry) }, "TestPack");
                win.TriggerActive();
                win.AdvanceFrame(); // Active frame 1
                win.AdvanceFrame(); // Completes active -> directly to Passive because cooldown is 0

                Assert.Equal(FlipperPlaybackMode.Passive, win.PlaybackMode);
                Assert.Equal(0, win.SequenceIndex);
            });
        }

        [Fact]
        public void CooldownAndStepBack_InCooldown_UsesActiveSequence()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var sprite = new SpriteState(128, 64);
                sprite.Frames.Clear();
                for (int i = 0; i < 4; i++)
                    sprite.Frames.Add(new FrameState { LayerPixels = new List<IPixelBuffer> { new MonochromePixelBuffer(new bool[128 * 64]) } });

                sprite.FlipperCycle = new FlipperAnimationCycle
                {
                    PassiveFrameCount = 1,
                    ActiveFrameCount = 3,
                    ActiveCycles = 1,
                    ActiveCooldown = 5,
                    FramesOrder = [0, 1, 2, 3]
                };

                var entry = new FlipperManifestEntry
                {
                    Name = "CooldownTest",
                    MinLevel = 1,
                    MaxLevel = 30,
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = 1
                };

                var win = new FlipperMatrixSimulatorWindow(new[] { ("CooldownTest", sprite, entry) }, "TestPack");
                win.TriggerActive();
                win.AdvanceFrame(); // active 1 (frame index 2)
                win.AdvanceFrame(); // active 2 (frame index 3)
                win.AdvanceFrame(); // transition to cooldown, index clamped to last active (2)

                Assert.Equal(FlipperPlaybackMode.Cooldown, win.PlaybackMode);
                Assert.Equal(2, win.SequenceIndex);

                // StepBack during Cooldown uses ActiveSequence (length 3, wraps to 1)
                win.StepBack();
                Assert.Equal(1, win.SequenceIndex);
            });
        }

        [Fact]
        public void FlipperSpeechBubble_RendersAllTailPositionsWithoutException()
        {
            var tails = new[]
            {
                SpeechBubbleTailPosition.BottomLeft,
                SpeechBubbleTailPosition.BottomRight,
                SpeechBubbleTailPosition.TopLeft,
                SpeechBubbleTailPosition.TopRight,
                SpeechBubbleTailPosition.None
            };

            foreach (var tail in tails)
            {
                var canvas = new bool[128 * 64];
                var bubble = new FlipperSpeechBubble(1, 10, 10, "Testing 123", tail);
                bubble.Draw(canvas, 128, 64, fillInterior: true);

                // Verify that at least some pixels were drawn on canvas
                bool hasPixels = false;
                for (int i = 0; i < canvas.Length; i++)
                {
                    if (canvas[i])
                    {
                        hasPixels = true;
                        break;
                    }
                }
                Assert.True(hasPixels, $"Tail position {tail} should draw pixels on canvas.");
            }
        }

        [Fact]
        public void FlipperMatrixSimulatorWindow_StateTransitions_FollowFlipperCycle()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var sprite = new SpriteState(128, 64);
                sprite.Frames.Add(new FrameState { LayerPixels = new List<IPixelBuffer> { new MonochromePixelBuffer(new bool[128 * 64]) } });
                sprite.Frames.Add(new FrameState { LayerPixels = new List<IPixelBuffer> { new MonochromePixelBuffer(new bool[128 * 64]) } });
                sprite.Frames.Add(new FrameState { LayerPixels = new List<IPixelBuffer> { new MonochromePixelBuffer(new bool[128 * 64]) } });

                sprite.FlipperCycle = new FlipperAnimationCycle
                {
                    PassiveFrameCount = 2,
                    ActiveFrameCount = 2,
                    ActiveCycles = 1,
                    ActiveCooldown = 2,
                    FramesOrder = new[] { 0, 1, 2, 3 }
                };

                var entry = new FlipperManifestEntry
                {
                    Name = "CycleDolphin",
                    MinLevel = 1,
                    MaxLevel = 30,
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = 1
                };

                var list = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
                {
                    ("CycleDolphin", sprite, entry)
                };

                var win = new FlipperMatrixSimulatorWindow(list, "TestPack");

                // Initially in Passive mode at frame 0
                Assert.Equal(FlipperPlaybackMode.Passive, win.PlaybackMode);
                Assert.Equal(0, win.SequenceIndex);

                // Advance in Passive loop
                win.AdvanceFrame();
                Assert.Equal(FlipperPlaybackMode.Passive, win.PlaybackMode);
                Assert.Equal(1, win.SequenceIndex);

                // Loops back to passive frame 0
                win.AdvanceFrame();
                Assert.Equal(FlipperPlaybackMode.Passive, win.PlaybackMode);
                Assert.Equal(0, win.SequenceIndex);

                // Trigger Active
                win.TriggerActive();
                Assert.Equal(FlipperPlaybackMode.Active, win.PlaybackMode);
                Assert.Equal(0, win.SequenceIndex);

                // Advance Active frame 1
                win.AdvanceFrame();
                Assert.Equal(FlipperPlaybackMode.Active, win.PlaybackMode);
                Assert.Equal(1, win.SequenceIndex);

                // Advance past active sequence -> transitions to Cooldown
                win.AdvanceFrame();
                Assert.Equal(FlipperPlaybackMode.Cooldown, win.PlaybackMode);

                // Advance cooldown ticks (cooldown = 2)
                win.AdvanceFrame();
                Assert.Equal(FlipperPlaybackMode.Cooldown, win.PlaybackMode);

                win.AdvanceFrame();
                // Finished cooldown -> returns to Passive
                Assert.Equal(FlipperPlaybackMode.Passive, win.PlaybackMode);
                Assert.Equal(0, win.SequenceIndex);
            });
        }

        [Fact]
        public void FlipperMatrixSimulatorWindow_PreservesSelection_WhenStillValid()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var sprite1 = new SpriteState(128, 64);
                var entry1 = new FlipperManifestEntry
                {
                    Name = "DolphinA",
                    MinLevel = 1,
                    MaxLevel = 10,
                    MinButthurt = 0,
                    MaxButthurt = 5,
                    Weight = 1
                };

                var sprite2 = new SpriteState(128, 64);
                var entry2 = new FlipperManifestEntry
                {
                    Name = "DolphinB",
                    MinLevel = 1,
                    MaxLevel = 30,
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = 1
                };

                var list = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
                {
                    ("DolphinA", sprite1, entry1),
                    ("DolphinB", sprite2, entry2)
                };

                var win = new FlipperMatrixSimulatorWindow(list, "TestPack");
                
                // Select DolphinB
                win.LstCandidates.SelectedIndex = 1;
                Assert.Equal("DolphinB", win.SelectedCandidate?.Name);

                // Adjust Level from 1 to 5 (both still match)
                win.SliderLevel.Value = 5;

                // Selection on DolphinB is preserved!
                Assert.Equal("DolphinB", win.SelectedCandidate?.Name);
            });
        }

        [Fact]
        public void FlipperMatrixSimulatorWindow_HudOverlay_RendersWithoutException()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                // Create a completely black frame (all pixels true)
                var solidBlackPixels = new bool[128 * 64];
                for (int i = 0; i < solidBlackPixels.Length; i++) solidBlackPixels[i] = true;

                var sprite = new SpriteState(128, 64);
                sprite.Frames.Add(new FrameState { LayerPixels = new List<IPixelBuffer> { new MonochromePixelBuffer(solidBlackPixels) } });

                var entry = new FlipperManifestEntry
                {
                    Name = "BlackDolphin",
                    MinLevel = 1,
                    MaxLevel = 30,
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = 1
                };

                var list = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
                {
                    ("BlackDolphin", sprite, entry)
                };

                var win = new FlipperMatrixSimulatorWindow(list, "TestPack");
                win.ChkDesktopHud.IsChecked = true;
                win.RenderCurrentFrame();

                // Verification that HUD rendering executes and updates the display cleanly
                Assert.NotNull(win);
            });
        }

        [Fact]
        public void FlipperMatrixSimulatorWindow_Inspector_DisplaysDurationWhenPresent()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var sprite = new SpriteState(128, 64);
                sprite.FlipperCycle = new FlipperAnimationCycle
                {
                    PassiveFrameCount = 2,
                    ActiveFrameCount = 2,
                    ActiveCycles = 1,
                    ActiveCooldown = 3,
                    Duration = 120,
                    BubbleSlots = 1,
                    FramesOrder = [0, 1, 0, 1]
                };

                var entry = new FlipperManifestEntry
                {
                    Name = "DurationDolphin",
                    MinLevel = 1,
                    MaxLevel = 30,
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = 1
                };

                var win = new FlipperMatrixSimulatorWindow(new[] { ("DurationDolphin", sprite, entry) }, "TestPack");
                Assert.Contains("Duration: 120", win.TxtCycleDetails.Text);
                Assert.Contains("Cooldown: 3", win.TxtCycleDetails.Text);
            });
        }

        [Fact]
        public void FlipperMatrixSimulatorWindow_WhenNoMatchingCandidates_RendersCenteredWithinBounds()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var sprite = new SpriteState(128, 64);
                var entry = new FlipperManifestEntry
                {
                    Name = "OnlyLevel1",
                    MinLevel = 1,
                    MaxLevel = 1,
                    MinButthurt = 0,
                    MaxButthurt = 0,
                    Weight = 1
                };

                var vm = new FlipperSimulatorViewModel(new[] { ("OnlyLevel1", sprite, entry) }, "TestPack");
                // Switch to Level 15 (where no candidate matches)
                vm.Level = 15;

                Assert.True(vm.HasNoMatchingCandidates);
                Assert.Null(vm.SelectedCandidate);

                // Verify screen pixels: all active text pixels are within safe horizontal margins [5..122]
                var screenPixels = vm.GetScreenPixelsCopy();
                for (int y = 0; y < 64; y++)
                {
                    for (int x = 0; x < 5; x++)
                    {
                        Assert.False(screenPixels[y * 128 + x], $"Pixel at ({x},{y}) should not be drawn in left margin.");
                    }
                    for (int x = 123; x < 128; x++)
                    {
                        Assert.False(screenPixels[y * 128 + x], $"Pixel at ({x},{y}) should not be drawn in right margin.");
                    }
                }
            });
        }

        [Fact]
        public void AdvanceFrame_RaisesSequenceIndexPropertyChanged_AndSyncsSlider()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var sprite = new SpriteState(128, 64);
                sprite.Frames.Add(new FrameState());
                sprite.Frames.Add(new FrameState());
                sprite.Frames.Add(new FrameState());

                var entry = new FlipperManifestEntry
                {
                    Name = "MultiFrame",
                    MinLevel = 1,
                    MaxLevel = 30,
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = 1
                };

                var vm = new FlipperSimulatorViewModel(new[] { ("MultiFrame", sprite, entry) }, "TestPack");
                var changedProperties = new List<string>();
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName != null) changedProperties.Add(e.PropertyName);
                };

                Assert.Equal(0, vm.SequenceIndex);
                vm.AdvanceFrame();

                Assert.Equal(1, vm.SequenceIndex);
                Assert.Contains(nameof(vm.SequenceIndex), changedProperties);
                Assert.Contains(nameof(vm.FrameCounterText), changedProperties);
            });
        }

        [Fact]
        public void StepBack_RaisesSequenceIndexPropertyChanged()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var sprite = new SpriteState(128, 64);
                sprite.Frames.Add(new FrameState());
                sprite.Frames.Add(new FrameState());
                sprite.Frames.Add(new FrameState());

                var entry = new FlipperManifestEntry
                {
                    Name = "StepBackTest",
                    MinLevel = 1,
                    MaxLevel = 30,
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = 1
                };

                var vm = new FlipperSimulatorViewModel(new[] { ("StepBackTest", sprite, entry) }, "TestPack");
                vm.SequenceIndex = 2;

                var changedProperties = new List<string>();
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName != null) changedProperties.Add(e.PropertyName);
                };

                vm.StepBack();
                Assert.Equal(1, vm.SequenceIndex);
                Assert.Contains(nameof(vm.SequenceIndex), changedProperties);
            });
        }

        [Fact]
        public void TriggerActive_And_CooldownTransition_MaintainsStateProperly()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var sprite = new SpriteState(128, 64);
                sprite.Frames.Add(new FrameState());
                sprite.Frames.Add(new FrameState());
                sprite.Frames.Add(new FrameState());
                sprite.FlipperCycle = new FlipperAnimationCycle
                {
                    PassiveFrameCount = 1,
                    ActiveFrameCount = 2,
                    ActiveCycles = 1,
                    ActiveCooldown = 2,
                    FramesOrder = [0, 1, 2]
                };

                var entry = new FlipperManifestEntry
                {
                    Name = "ActiveTest",
                    MinLevel = 1,
                    MaxLevel = 30,
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = 1
                };

                var vm = new FlipperSimulatorViewModel(new[] { ("ActiveTest", sprite, entry) }, "TestPack");
                Assert.Equal(FlipperPlaybackMode.Passive, vm.PlaybackMode);

                vm.TriggerActive();
                Assert.Equal(FlipperPlaybackMode.Active, vm.PlaybackMode);
                Assert.Equal(0, vm.SequenceIndex);

                // Advance frame 0 -> frame 1 (activeSeq length = 2)
                vm.AdvanceFrame();
                Assert.Equal(1, vm.SequenceIndex);
                Assert.Equal(FlipperPlaybackMode.Active, vm.PlaybackMode);

                // Advance frame 1 -> triggers cooldown (activeCycles = 1, cooldown = 2)
                vm.AdvanceFrame();
                Assert.Equal(FlipperPlaybackMode.Cooldown, vm.PlaybackMode);
                Assert.Contains("Cooldown: 2", vm.StateBadgeText);

                // Advance cooldown tick 2 -> 1
                vm.AdvanceFrame();
                Assert.Equal(FlipperPlaybackMode.Cooldown, vm.PlaybackMode);
                Assert.Contains("Cooldown: 1", vm.StateBadgeText);

                // Advance cooldown tick 1 -> 0 -> transitions back to Passive!
                vm.AdvanceFrame();
                Assert.Equal(FlipperPlaybackMode.Passive, vm.PlaybackMode);
                Assert.Equal(0, vm.SequenceIndex);
            });
        }

        [Fact]
        public void SpeechBubble_Caching_AndPositionUpdates_RendersCleanly()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var sprite = new SpriteState(128, 64);
                var entry = new FlipperManifestEntry
                {
                    Name = "BubbleTest",
                    MinLevel = 1,
                    MaxLevel = 30,
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = 1
                };

                var vm = new FlipperSimulatorViewModel(new[] { ("BubbleTest", sprite, entry) }, "TestPack");
                vm.CustomBubbleText = "Hello Dolphin!";
                vm.BubbleX = 20;
                vm.BubbleY = 10;
                vm.ShowSpeechBubble = true;

                // Screen bitmap should have drawn speech bubble pixels
                var screen = vm.GetScreenPixelsCopy();
                Assert.Contains(true, screen);

                // Moving bubble updates position
                vm.UpdateBubblePositionFromPoint(100, 50, 384, 192);
                Assert.Equal((int)Math.Round(100.0 * (128.0 / 384.0)), vm.BubbleX);
                Assert.Equal((int)Math.Round(50.0 * (64.0 / 192.0)), vm.BubbleY);
            });
        }

        [Fact]
        public void FlipperMatrixSimulatorWindow_CloseButton_HasAdequateHeightAndZeroVerticalPadding()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var sprite = new SpriteState(128, 64);
                var entry = new FlipperManifestEntry { Name = "Dolphin" };
                var win = new FlipperMatrixSimulatorWindow(new[] { ("Dolphin", sprite, entry) }, "TestPack");

                Assert.NotNull(win.BtnClose);
                Assert.True(win.BtnClose.Height >= 28, "Close button height should be at least 28 to prevent text clipping");
                Assert.Equal(0, win.BtnClose.Padding.Top);
                Assert.Equal(0, win.BtnClose.Padding.Bottom);
                Assert.True(win.UseLayoutRounding, "Window should have UseLayoutRounding enabled");
                Assert.True(win.SnapsToDevicePixels, "Window should have SnapsToDevicePixels enabled");
            });
        }

        [Fact]
        public void FlipperMatrixSimulatorWindow_BottomBar_HasClearanceAboveResizeBorder()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var sprite = new SpriteState(128, 64);
                var entry = new FlipperManifestEntry { Name = "Dolphin" };
                var win = new FlipperMatrixSimulatorWindow(new[] { ("Dolphin", sprite, entry) }, "TestPack");

                var parentGrid = win.BtnClose.Parent as System.Windows.Controls.Grid;
                Assert.NotNull(parentGrid);
                var bottomBarBorder = parentGrid.Parent as System.Windows.Controls.Border;
                Assert.NotNull(bottomBarBorder);
                Assert.True(bottomBarBorder.Padding.Bottom >= 10, "Status bar bottom padding must be >= 10 to clear the 5px resize border");
            });
        }
    }
}
