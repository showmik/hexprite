using System;
using System.Collections.Generic;
using System.Linq;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels.Flipper;
using Xunit;

namespace Hexprite.Tests.ViewModels
{
    [Trait("Category", "Unit")]
    public class FlipperSimulatorViewModelTests
    {
        private static (List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> animations, SpriteState sprite1, SpriteState sprite2) CreateSampleAnimations()
        {
            var sprite1 = new SpriteState(128, 64);
            sprite1.Frames.Clear();
            for (int i = 0; i < 4; i++)
            {
                sprite1.Frames.Add(new FrameState
                {
                    LayerPixels = [new MonochromePixelBuffer(new bool[128 * 64])]
                });
            }
            sprite1.FlipperCycle = new FlipperAnimationCycle
            {
                PassiveFrameCount = 2,
                ActiveFrameCount = 2,
                ActiveCycles = 2,
                ActiveCooldown = 2,
                FramesOrder = [0, 1, 2, 3]
            };

            var entry1 = new FlipperManifestEntry
            {
                Name = "BabyAnim",
                MinLevel = 1,
                MaxLevel = 10,
                MinButthurt = 0,
                MaxButthurt = 5,
                Weight = 3
            };

            var sprite2 = new SpriteState(128, 64);
            sprite2.Frames.Clear();
            for (int i = 0; i < 3; i++)
            {
                sprite2.Frames.Add(new FrameState
                {
                    LayerPixels = [new MonochromePixelBuffer(new bool[128 * 64])]
                });
            }
            sprite2.FlipperCycle = new FlipperAnimationCycle
            {
                PassiveFrameCount = 3,
                ActiveFrameCount = 0,
                ActiveCycles = 1,
                ActiveCooldown = 0,
                FramesOrder = [0, 1, 2]
            };

            var entry2 = new FlipperManifestEntry
            {
                Name = "AdultAnim",
                MinLevel = 11,
                MaxLevel = 30,
                MinButthurt = 0,
                MaxButthurt = 14,
                Weight = 1
            };

            var list = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("BabyAnim", sprite1, entry1),
                ("AdultAnim", sprite2, entry2)
            };

            return (list, sprite1, sprite2);
        }

        [Fact]
        public void Constructor_InitializesWithCorrectDefaultState()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "MyPack");

            Assert.Equal("MyPack", vm.PackName);
            Assert.Equal(FlipperPlaybackMode.Passive, vm.PlaybackMode);
            Assert.Equal(0, vm.SequenceIndex);
            Assert.NotNull(vm.SelectedCandidate);
            Assert.Equal("BabyAnim", vm.SelectedCandidate.Name);
            Assert.Contains("Baby", vm.LevelDescription);
            Assert.Contains("Ecstatic", vm.MoodDescription);
        }

        [Fact]
        public void CandidateFiltering_LevelChange_FiltersMatchingAnimations()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "MyPack");

            // Level 1 matches BabyAnim
            Assert.Single(vm.Candidates);
            Assert.Equal("BabyAnim", vm.Candidates[0].Name);
            Assert.Equal(100.0, vm.Candidates[0].ProbabilityPercent);

            // Change Level to 20 -> matches AdultAnim
            vm.Level = 20;
            Assert.Single(vm.Candidates);
            Assert.Equal("AdultAnim", vm.Candidates[0].Name);
            Assert.Equal("AdultAnim", vm.SelectedCandidate?.Name);
        }

        [Fact]
        public void Candidates_AllMode_ShowsAllAnimationsRegardlessOfLevel()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "MyPack");

            vm.IsAllAnimationsMode = true;
            Assert.Equal(2, vm.Candidates.Count);
            Assert.Equal(1, vm.CandidateModeIndex);
        }

        [Fact]
        public void WeightProbability_MultipleMatches_CalculatesCorrectPercentages()
        {
            var sprite = new SpriteState(128, 64);
            var entryA = new FlipperManifestEntry { Name = "A", MinLevel = 1, MaxLevel = 10, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var entryB = new FlipperManifestEntry { Name = "B", MinLevel = 1, MaxLevel = 10, MinButthurt = 0, MaxButthurt = 14, Weight = 3 };

            var list = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("A", sprite, entryA),
                ("B", sprite, entryB)
            };

            using var vm = new FlipperSimulatorViewModel(list);
            vm.Level = 5;

            Assert.Equal(2, vm.Candidates.Count);
            var candA = vm.Candidates.First(c => c.Name == "A");
            var candB = vm.Candidates.First(c => c.Name == "B");

            Assert.Equal(25.0, candA.ProbabilityPercent);
            Assert.Equal(75.0, candB.ProbabilityPercent);
        }

        [Fact]
        public void StateMachine_FullCycle_TransitionsPassiveActiveCooldownPassive()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "MyPack");

            // Passive advance
            Assert.Equal(FlipperPlaybackMode.Passive, vm.PlaybackMode);
            Assert.Equal(0, vm.SequenceIndex);

            vm.AdvanceFrame();
            Assert.Equal(FlipperPlaybackMode.Passive, vm.PlaybackMode);
            Assert.Equal(1, vm.SequenceIndex);

            vm.AdvanceFrame();
            // Wraps to 0 in passive loop (passive length 2)
            Assert.Equal(0, vm.SequenceIndex);

            // Trigger Active
            vm.TriggerActive();
            Assert.Equal(FlipperPlaybackMode.Active, vm.PlaybackMode);
            Assert.Equal(0, vm.SequenceIndex);

            // Cycle 1: Frame 0 -> Frame 1
            vm.AdvanceFrame();
            Assert.Equal(FlipperPlaybackMode.Active, vm.PlaybackMode);
            Assert.Equal(1, vm.SequenceIndex);

            // Cycle 1 -> Cycle 2 (ActiveCycles = 2): Frame 0
            vm.AdvanceFrame();
            Assert.Equal(FlipperPlaybackMode.Active, vm.PlaybackMode);
            Assert.Equal(0, vm.SequenceIndex);

            // Cycle 2: Frame 1
            vm.AdvanceFrame();
            Assert.Equal(FlipperPlaybackMode.Active, vm.PlaybackMode);
            Assert.Equal(1, vm.SequenceIndex);

            // Cycle 2 finished -> Cooldown mode (cooldown = 2)
            vm.AdvanceFrame();
            Assert.Equal(FlipperPlaybackMode.Cooldown, vm.PlaybackMode);

            // Cooldown tick 1
            vm.AdvanceFrame();
            Assert.Equal(FlipperPlaybackMode.Cooldown, vm.PlaybackMode);

            // Cooldown tick 2 -> returns to Passive
            vm.AdvanceFrame();
            Assert.Equal(FlipperPlaybackMode.Passive, vm.PlaybackMode);
            Assert.Equal(0, vm.SequenceIndex);
        }

        [Fact]
        public void StateMachine_StepBack_DecrementsCorrectly()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "MyPack");

            vm.SequenceIndex = 1;
            vm.StepBack();
            Assert.Equal(0, vm.SequenceIndex);

            // StepBack at 0 wraps to end of passive sequence (length 2 -> 1)
            vm.StepBack();
            Assert.Equal(1, vm.SequenceIndex);
        }

        [Fact]
        public void ResetCommand_ResetsPlaybackToPassiveAtZero()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "MyPack");

            vm.TriggerActive();
            vm.AdvanceFrame();
            Assert.Equal(FlipperPlaybackMode.Active, vm.PlaybackMode);

            vm.ResetCommand.Execute(null);
            Assert.Equal(FlipperPlaybackMode.Passive, vm.PlaybackMode);
            Assert.Equal(0, vm.SequenceIndex);
        }

        [Fact]
        public void SpeechBubble_AlignmentAndClamping_WorksCorrectly()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "MyPack");

            vm.SetBubbleAlignment("TL");
            Assert.Equal(4, vm.BubbleX);
            Assert.Equal(4, vm.BubbleY);
            Assert.Equal(SpeechBubbleTailPosition.BottomLeft, vm.BubbleTail);

            vm.SetBubbleAlignment("BR");
            Assert.Equal(Math.Max(0, vm.MaxBubbleX - 4), vm.BubbleX);
            Assert.Equal(Math.Max(0, vm.MaxBubbleY - 4), vm.BubbleY);
            Assert.Equal(SpeechBubbleTailPosition.TopRight, vm.BubbleTail);

            // Test mouse coordinate calculation
            vm.UpdateBubblePositionFromPoint(300, 150); // 300 / 3 = 100, 150 / 3 = 50
            Assert.Equal(Math.Clamp(100, 0, vm.MaxBubbleX), vm.BubbleX);
            Assert.Equal(Math.Clamp(50, 0, vm.MaxBubbleY), vm.BubbleY);

            // Test clamping with empty bubble
            vm.CustomBubbleText = string.Empty;
            vm.BubbleX = 500;
            Assert.Equal(vm.MaxBubbleX, vm.BubbleX);
            vm.BubbleY = 500;
            Assert.Equal(vm.MaxBubbleY, vm.BubbleY);
        }

        [Fact]
        public void StaticSequences_GetPassiveAndActive_HandlesNullAndCycles()
        {
            var sprite = new SpriteState(128, 64);
            sprite.Frames.Clear();
            sprite.Frames.Add(new FrameState());
            sprite.Frames.Add(new FrameState());

            // No cycle -> passive is all frames, active is empty
            Assert.Equal([0, 1], FlipperSimulatorViewModel.GetPassiveSequence(sprite));
            Assert.Empty(FlipperSimulatorViewModel.GetActiveSequence(sprite));

            // With cycle
            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                PassiveFrameCount = 1,
                ActiveFrameCount = 1,
                FramesOrder = [0, 1]
            };
            Assert.Equal([0], FlipperSimulatorViewModel.GetPassiveSequence(sprite));
            Assert.Equal([1], FlipperSimulatorViewModel.GetActiveSequence(sprite));
        }

        [Fact]
        public void SpeedMultiplier_UpdatesCorrectly()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "MyPack");

            vm.SetSpeedMultiplier(2.0);
            Assert.Equal(2.0, vm.SpeedMultiplier);

            vm.SetSpeedMultiplier(0.5);
            Assert.Equal(0.5, vm.SpeedMultiplier);
        }

        [Fact]
        public void RollRng_SelectsCandidateWithoutCrashing()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "MyPack");
            vm.IsAllAnimationsMode = true;

            vm.RollRngCommand.Execute(null);
            Assert.NotNull(vm.SelectedCandidate);
        }

        [Fact]
        public void FrameCounterText_ZeroFramesOrEmptySequence_ReturnsFrame0_0()
        {
            var emptySprite = new SpriteState(128, 64);
            emptySprite.Frames.Clear();
            var entry = new FlipperManifestEntry { Name = "Empty", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            using var vm = new FlipperSimulatorViewModel([("Empty", emptySprite, entry)]);

            Assert.Equal("Frame 0 / 0", vm.FrameCounterText);
        }

        [Fact]
        public void FrameCounterText_ValidFrames_ReturnsFormattedFrameCount()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "MyPack");

            Assert.Equal("Frame 1 / 2 (#0)", vm.FrameCounterText);
            vm.AdvanceFrame();
            Assert.Equal("Frame 2 / 2 (#1)", vm.FrameCounterText);
        }

        [Fact]
        public void Dpad_Commands_UpdateStateAndFeedback()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "MyPack");
            vm.Mood = 5;

            vm.TriggerUpCommand.Execute(null);
            Assert.Equal(4, vm.Mood);
            Assert.Contains("UP", vm.LastButtonPressedText);

            vm.TriggerDownCommand.Execute(null);
            Assert.Equal(5, vm.Mood);
            Assert.Contains("DOWN", vm.LastButtonPressedText);

            vm.TriggerRightCommand.Execute(null);
            Assert.Equal(1, vm.SequenceIndex);
            Assert.Contains("RIGHT", vm.LastButtonPressedText);

            vm.TriggerLeftCommand.Execute(null);
            Assert.Equal(0, vm.SequenceIndex);
            Assert.Contains("LEFT", vm.LastButtonPressedText);

            vm.TriggerOkCommand.Execute(null);
            Assert.Equal(FlipperPlaybackMode.Active, vm.PlaybackMode);
            Assert.Contains("OK", vm.LastButtonPressedText);

            vm.TriggerBackCommand.Execute(null);
            Assert.Equal(FlipperPlaybackMode.Passive, vm.PlaybackMode);
            Assert.Contains("BACK", vm.LastButtonPressedText);
        }

        [Fact]
        public void ApplyBubbleToSelected_PersistsBubbleToCandidateSprite()
        {
            var (list, sprite1, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "MyPack");
            bool docModifiedFired = false;
            vm.DocumentModified += (s, e) => docModifiedFired = true;

            vm.CustomBubbleText = "Hack The Planet!";
            vm.BubbleX = 20;
            vm.BubbleY = 10;
            vm.BubbleTail = SpeechBubbleTailPosition.TopRight;

            vm.ApplyBubbleToSelectedCommand.Execute(null);

            Assert.True(docModifiedFired);
            Assert.Contains("Saved", vm.BubbleSavedFeedbackText);
            Assert.NotNull(sprite1.FlipperCycle?.SpeechBubble);
            var bubble = sprite1.FlipperCycle.SpeechBubble;
            Assert.Equal("Hack The Planet!", bubble.Text);
            Assert.Equal(20, bubble.X);
            Assert.Equal(10, bubble.Y);
            Assert.Equal(SpeechBubbleTailPosition.TopRight, bubble.Tail);
        }

        [Fact]
        public void SelectedCandidate_AutoLoadsExistingBubbleMetadata()
        {
            var (list, sprite1, _) = CreateSampleAnimations();
            sprite1.FlipperCycle!.SpeechBubble = new FlipperSpeechBubble(1, 42, 18, "Loaded From Sprite", SpeechBubbleTailPosition.BottomRight);

            using var vm = new FlipperSimulatorViewModel(list, "MyPack");
            // Selected candidate initially set
            Assert.Equal("Loaded From Sprite", vm.CustomBubbleText);
            Assert.Equal(42, vm.BubbleX);
            Assert.Equal(18, vm.BubbleY);
            Assert.Equal(SpeechBubbleTailPosition.BottomRight, vm.BubbleTail);
        }

        [Fact]
        public void JumpToState_ClampsLevelAndMood()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "MyPack");

            vm.JumpToState(0, -5);
            Assert.Equal(1, vm.Level);
            Assert.Equal(0, vm.Mood);

            vm.JumpToState(100, 50);
            Assert.Equal(30, vm.Level);
            Assert.Equal(14, vm.Mood);
        }

        [Fact]
        public void SetAnimations_UpdatesPackNameAndCandidates()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel([], "OldPack");
            Assert.Empty(vm.Candidates);

            vm.SetAnimations(list, "NewPack");
            Assert.Equal("NewPack", vm.PackName);
            Assert.NotEmpty(vm.Candidates);
        }

        [Fact]
        public void CandidateAnimationVm_ProbabilitySummary_ReflectsAllModeVsFiltered()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "MyPack");

            // In filtered mode (BabyAnim only, 100%)
            Assert.Contains("%", vm.SelectedCandidate!.ProbabilitySummary);

            // In All Animations mode
            vm.CandidateModeIndex = 1;
            Assert.Contains("Weight: 3", vm.Candidates[0].ProbabilitySummary);
            Assert.Contains("Weight: 1", vm.Candidates[1].ProbabilitySummary);
        }

        [Fact]
        public void JumpToNearestCoveredState_WhenInDeadzone_JumpsToClosestManifestEntry()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "MyPack");

            // Set to uncovered state: Level 5, Mood 10 (BabyAnim max mood is 5, AdultAnim min level is 11)
            vm.Level = 5;
            vm.Mood = 10;
            Assert.True(vm.HasNoMatchingCandidates);

            vm.JumpToNearestCoveredState();
            // Should jump to BabyAnim (L5, M5) or AdultAnim (L11, M10)
            Assert.False(vm.HasNoMatchingCandidates);
            Assert.NotNull(vm.SelectedCandidate);
        }

        [Fact]
        public void RollRng_PopulatesRngRollFeedbackText()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "MyPack");

            Assert.Empty(vm.RngRollFeedbackText);
            vm.RollRng();
            Assert.Contains("Rolled", vm.RngRollFeedbackText);
            Assert.Contains("Selected", vm.RngRollFeedbackText);
        }

        [Fact]
        public void DynamicBubbleBounds_CalculatesMaxXAndMaxY()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "MyPack");

            vm.CustomBubbleText = "Very long test speech bubble text";
            Assert.True(vm.MaxBubbleX < 110);
            Assert.True(vm.MaxBubbleY <= 52);
        }

        [Fact]
        public void LocateSelectedInMatrix_InvokesLocateEntryRequested()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "MyPack");

            string? requested = null;
            vm.LocateEntryRequested += name => requested = name;

            vm.LocateSelectedInMatrix();
            Assert.Equal("BabyAnim", requested);
        }

        [Fact]
        public void SearchText_FiltersCandidatesByName()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "MyPack");
            vm.IsAllAnimationsMode = true;
            Assert.Equal(2, vm.Candidates.Count);

            vm.SearchText = "Adult";
            Assert.Single(vm.Candidates);
            Assert.Equal("AdultAnim", vm.Candidates[0].Name);

            vm.SearchText = "NonExistent";
            Assert.Empty(vm.Candidates);

            vm.SearchText = string.Empty;
            Assert.Equal(2, vm.Candidates.Count);
        }

        [Fact]
        public void SimulatorSettings_GetAndApply_RoundtripsAccurately()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "MyPack");

            vm.Level = 25;
            vm.Mood = 11;
            vm.SelectedPaletteIndex = 3;
            vm.ShowLcdGrid = false;
            vm.ShowDesktopHud = true;
            vm.ShowSpeechBubble = true;
            vm.BubbleX = 22;
            vm.BubbleY = 18;
            vm.CustomBubbleText = "Hack the planet!";
            vm.BubbleTail = SpeechBubbleTailPosition.TopRight;
            vm.SpeedMultiplier = 2.0;
            vm.CandidateModeIndex = 1;

            var settings = vm.GetCurrentSimulatorSettings();
            Assert.NotNull(settings);
            Assert.Equal(25, settings.Level);
            Assert.Equal(11, settings.Mood);
            Assert.Equal(3, settings.SelectedPaletteIndex);
            Assert.False(settings.ShowLcdGrid);
            Assert.True(settings.ShowDesktopHud);
            Assert.True(settings.ShowSpeechBubble);
            Assert.Equal(22, settings.BubbleX);
            Assert.Equal(18, settings.BubbleY);
            Assert.Equal("Hack the planet!", settings.CustomBubbleText);
            Assert.Equal(SpeechBubbleTailPosition.TopRight, settings.BubbleTail);
            Assert.Equal(2.0, settings.SpeedMultiplier);
            Assert.Equal(1, settings.CandidateModeIndex);

            using var vm2 = new FlipperSimulatorViewModel(list, "MyPack");
            vm2.ApplySimulatorSettings(settings);

            Assert.Equal(25, vm2.Level);
            Assert.Equal(11, vm2.Mood);
            Assert.Equal(3, vm2.SelectedPaletteIndex);
            Assert.False(vm2.ShowLcdGrid);
            Assert.True(vm2.ShowDesktopHud);
            Assert.True(vm2.ShowSpeechBubble);
            Assert.Equal(22, vm2.BubbleX);
            Assert.Equal(18, vm2.BubbleY);
            Assert.Equal("Hack the planet!", vm2.CustomBubbleText);
            Assert.Equal(SpeechBubbleTailPosition.TopRight, vm2.BubbleTail);
            Assert.Equal(2.0, vm2.SpeedMultiplier);
            Assert.True(vm2.IsAllAnimationsMode);
        }

        [Fact]
        public void ThemePalettes_ExposesAll8Themes()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "MyPack");

            Assert.Equal(8, vm.ThemePalettes.Count);
            Assert.Equal("Classic Amber (OEM)", vm.ThemePalettes[0].Name);
            Assert.Equal("Paper White", vm.ThemePalettes[7].Name);
        }

        [Fact]
        public void SaveAsHexpack_WritesValidDocument()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "TestPack");
            string tempFile = Path.Combine(Path.GetTempPath(), "test_sim_save_" + Guid.NewGuid().ToString("N") + ".hexpack");

            try
            {
                vm.SaveAsHexpack(tempFile);
                Assert.True(File.Exists(tempFile));

                string json = File.ReadAllText(tempFile);
                var doc = System.Text.Json.JsonSerializer.Deserialize<AssetPackDocument>(json);
                Assert.NotNull(doc);
                Assert.Equal("TestPack", doc.PackName);
                Assert.Equal(2, doc.Entries.Count);
                Assert.NotNull(doc.SimulatorSettings);
                Assert.Equal(vm.Level, doc.SimulatorSettings.Level);
                Assert.Equal(vm.Mood, doc.SimulatorSettings.Mood);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void LoadPackFromPath_WithHexpackFile_LoadsAnimationsAndSettings()
        {
            using var vm = new FlipperSimulatorViewModel([], "EmptyPack");
            Assert.False(vm.HasAnimations);

            var doc = new AssetPackDocument
            {
                PackName = "SavedPack",
                SimulatorSettings = new FlipperSimulatorSettings
                {
                    Level = 18,
                    Mood = 9,
                    CustomBubbleText = "SimLoaded!",
                    SelectedPaletteIndex = 2
                },
                Entries = [new FlipperManifestEntry { Name = "Dance", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 }]
            };
            var sprite = new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome };
            doc.Animations["Dance"] = sprite;

            string tempFile = Path.Combine(Path.GetTempPath(), "test_load_hexpack_" + Guid.NewGuid().ToString("N") + ".hexpack");
            try
            {
                string json = System.Text.Json.JsonSerializer.Serialize(doc);
                File.WriteAllText(tempFile, json);

                vm.LoadPackFromPath(tempFile);

                Assert.True(vm.HasAnimations);
                Assert.Equal("SavedPack", vm.PackName);
                Assert.Single(vm.Animations);
                Assert.Equal(18, vm.Level);
                Assert.Equal(9, vm.Mood);
                Assert.Equal(2, vm.SelectedPaletteIndex);
                Assert.Equal("SimLoaded!", vm.CustomBubbleText);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void EmptyPack_CommandsShowWarningMessage()
        {
            var mockDialog = new Moq.Mock<Hexprite.Services.IDialogService>();
            using var vm = new FlipperSimulatorViewModel([], "EmptyPack", dialogService: mockDialog.Object);

            // 1. FlashUsb
            vm.FlashUsb();
            mockDialog.Verify(d => d.ShowMessage(
                Moq.It.Is<string>(s => s.Contains("No animations in active pack")),
                "USB Flash",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning), Moq.Times.Once);

            // 2. OpenInTabs
            vm.OpenInTabs();
            mockDialog.Verify(d => d.ShowMessage(
                Moq.It.Is<string>(s => s.Contains("No animations in active simulator")),
                "Editor Tabs",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning), Moq.Times.Once);

            // 3. OpenInAssetPackStudio
            vm.OpenInAssetPackStudio();
            mockDialog.Verify(d => d.ShowMessage(
                Moq.It.Is<string>(s => s.Contains("No animations in active simulator")),
                "Studio",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning), Moq.Times.Once);

            // 4. OpenScheduleMatrix
            vm.OpenScheduleMatrix();
            mockDialog.Verify(d => d.ShowMessage(
                Moq.It.Is<string>(s => s.Contains("No animations in active simulator")),
                "Matrix View",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning), Moq.Times.Once);

            // 5. SaveAsHexpack
            vm.SaveAsHexpack();
            mockDialog.Verify(d => d.ShowMessage(
                Moq.It.Is<string>(s => s.Contains("No animations in active simulator")),
                "Save Pack",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning), Moq.Times.Once);

            // 6. ExportGif
            vm.ExportGif();
            mockDialog.Verify(d => d.ShowMessage(
                Moq.It.Is<string>(s => s.Contains("No active animation available")),
                "Export GIF",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning), Moq.Times.Once);
        }

        [Fact]
        public void CandidateAnimationVm_CycleSummaryAndHasActiveFrames_ReflectsState()
        {
            var (list, sprite1, sprite2) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "TestPack");

            vm.IsAllAnimationsMode = true;
            Assert.Equal(2, vm.Candidates.Count);

            var baby = vm.Candidates.First(c => c.Name == "BabyAnim");
            Assert.True(baby.HasActiveFrames);
            Assert.Contains("⚡ Active", baby.CycleSummary);
            Assert.Equal("L1-10 • M0-5", baby.LevelMoodSummary);

            var adult = vm.Candidates.First(c => c.Name == "AdultAnim");
            Assert.False(adult.HasActiveFrames);
            Assert.Equal("Passive", adult.CycleSummary);
            Assert.Equal("L11-30 • M0-14", adult.LevelMoodSummary);
        }

        [Fact]
        public void FlipperHudRenderer_Draw_RendersHeaderSeparatorLevelAndBattery()
        {
            bool[] canvas = new bool[128 * 64];
            FlipperHudRenderer.Draw(canvas, 128, 64, level: 5, mood: 2);

            // Separator line across y = 11
            for (int x = 0; x < 128; x++)
            {
                Assert.True(canvas[11 * 128 + x], $"Pixel at ({x}, 11) should be set on the separator line.");
            }

            // Top background area y = 0..10 has white knockouts and drawn text/icons
            // Level text at (2, 2)
            bool hasLevelPixels = false;
            for (int y = 2; y <= 9; y++)
            {
                for (int x = 2; x <= 25; x++)
                {
                    if (canvas[y * 128 + x]) hasLevelPixels = true;
                }
            }
            Assert.True(hasLevelPixels);

            // Battery at (112..125, 2..9)
            Assert.True(canvas[2 * 128 + 112]); // Top-left corner of battery
            Assert.True(canvas[9 * 128 + 112]); // Bottom-left corner of battery
            Assert.True(canvas[5 * 128 + 125]); // Terminal nipple
        }

        [Fact]
        public void SpeechBubble_DynamicClamping_And_PresetAnchors()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "TestPack");

            vm.CustomBubbleText = "Hello Flipper!";
            Assert.True(vm.MaxBubbleX > 0 && vm.MaxBubbleX < 128);
            Assert.True(vm.MaxBubbleY > 0 && vm.MaxBubbleY < 64);

            // Setting out of bounds clamps to MaxBubbleX / MaxBubbleY
            vm.BubbleX = 999;
            Assert.Equal(vm.MaxBubbleX, vm.BubbleX);

            vm.BubbleY = 999;
            Assert.Equal(vm.MaxBubbleY, vm.BubbleY);

            // Preset anchors
            vm.SetBubbleAlignment("TL");
            Assert.Equal(4, vm.BubbleX);
            Assert.Equal(4, vm.BubbleY);
            Assert.Equal(SpeechBubbleTailPosition.BottomLeft, vm.BubbleTail);

            vm.SetBubbleAlignment("TR");
            Assert.Equal(Math.Max(0, vm.MaxBubbleX - 4), vm.BubbleX);
            Assert.Equal(4, vm.BubbleY);
            Assert.Equal(SpeechBubbleTailPosition.BottomRight, vm.BubbleTail);

            vm.SetBubbleAlignment("BL");
            Assert.Equal(4, vm.BubbleX);
            Assert.Equal(Math.Max(0, vm.MaxBubbleY - 4), vm.BubbleY);
            Assert.Equal(SpeechBubbleTailPosition.TopLeft, vm.BubbleTail);

            vm.SetBubbleAlignment("BR");
            Assert.Equal(Math.Max(0, vm.MaxBubbleX - 4), vm.BubbleX);
            Assert.Equal(Math.Max(0, vm.MaxBubbleY - 4), vm.BubbleY);
            Assert.Equal(SpeechBubbleTailPosition.TopRight, vm.BubbleTail);
        }

        [Fact]
        public void FlipperFonts_MeasureStringAndDrawString_NormalizesCarriageReturns()
        {
            var (w1, h1) = Hexprite.Resources.Fonts.FlipperFonts.MeasureString("Line1\nLine2", Hexprite.Resources.Fonts.FlipperFontType.FontSecondary);
            var (w2, h2) = Hexprite.Resources.Fonts.FlipperFonts.MeasureString("Line1\r\nLine2", Hexprite.Resources.Fonts.FlipperFontType.FontSecondary);
            var (w3, h3) = Hexprite.Resources.Fonts.FlipperFonts.MeasureString(@"Line1\nLine2", Hexprite.Resources.Fonts.FlipperFontType.FontSecondary);

            Assert.Equal(w1, w2);
            Assert.Equal(w1, w3);
            Assert.Equal(h1, h2);
            Assert.Equal(h1, h3);

            bool[] c1 = new bool[128 * 64];
            bool[] c2 = new bool[128 * 64];
            bool[] c3 = new bool[128 * 64];
            Hexprite.Resources.Fonts.FlipperFonts.DrawString(c1, 128, 64, 0, 0, "Line1\nLine2");
            Hexprite.Resources.Fonts.FlipperFonts.DrawString(c2, 128, 64, 0, 0, "Line1\r\nLine2");
            Hexprite.Resources.Fonts.FlipperFonts.DrawString(c3, 128, 64, 0, 0, @"Line1\nLine2");

            Assert.Equal(c1, c2);
            Assert.Equal(c1, c3);
        }

        [Fact]
        public void FlipperSpeechBubble_MultiLine_SupportsEscapeSequencesAndIncreasesHeight()
        {
            var singleLineBubble = new FlipperSpeechBubble(1, 0, 0, "Hello Flipper");
            var multiLineBubble = new FlipperSpeechBubble(1, 0, 0, @"Hello\nFlipper");

            var (w1, h1) = singleLineBubble.MeasureBubble();
            var (w2, h2) = multiLineBubble.MeasureBubble();

            Assert.True(w2 < w1, "Multi-line bubble width should be shorter than single line width.");
            Assert.True(h2 > h1, "Multi-line bubble height should be taller than single line height.");

            bool[] canvas = new bool[128 * 64];
            multiLineBubble.Draw(canvas, 128, 64);

            // Verify pixels were drawn inside the bubble
            int setPixels = canvas.Count(p => p);
            Assert.True(setPixels > 0);
        }

        [Fact]
        public void PlayPause_StateTransitions_UpdatesTextIconAndToolTip()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "TestPack");

            // Initial state: IsPlaying = true
            Assert.True(vm.IsPlaying);
            Assert.Equal("Pause", vm.PlayPauseText);
            Assert.Equal("Pause Animation (Space)", vm.PlayPauseToolTip);
            Assert.Contains("M 3,2 H 6", vm.PlayPauseIconData);

            // Execute PlayPauseCommand -> Pause
            vm.PlayPauseCommand.Execute(null);
            Assert.False(vm.IsPlaying);
            Assert.Equal("Play", vm.PlayPauseText);
            Assert.Equal("Play Animation (Space)", vm.PlayPauseToolTip);
            Assert.Contains("M 4,2 L 14,8", vm.PlayPauseIconData);

            // Execute PlayPauseCommand -> Play again
            vm.PlayPauseCommand.Execute(null);
            Assert.True(vm.IsPlaying);
            Assert.Equal("Pause", vm.PlayPauseText);
            Assert.Equal("Pause Animation (Space)", vm.PlayPauseToolTip);
        }

        [Fact]
        public void ShowDesktopHud_DefaultsToTrue()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "TestPack");

            Assert.True(vm.ShowDesktopHud, "HUD overlay should be enabled by default.");

            var settings = new FlipperSimulatorSettings();
            Assert.True(settings.ShowDesktopHud, "FlipperSimulatorSettings.ShowDesktopHud should be true by default.");
        }

        [Fact]
        public void OpenInAssetPackStudio_RaisesRequestClose_AndCallsTabService()
        {
            var (list, _, _) = CreateSampleAnimations();
            var mockTabService = new Moq.Mock<IWorkspaceTabService>();
            using var vm = new FlipperSimulatorViewModel(list, "TestPack", tabService: mockTabService.Object);

            bool closeRequested = false;
            vm.RequestClose += (s, e) => closeRequested = true;

            vm.OpenInAssetPackStudio();

            mockTabService.Verify(ts => ts.OpenAssetPackInTab(
                Moq.It.Is<IReadOnlyList<(string, SpriteState, FlipperManifestEntry)>>(p => p.Count == 2),
                "TestPack"), Moq.Times.Once);

            Assert.True(closeRequested, "OpenInAssetPackStudio should raise RequestClose to close the modal simulator dialog.");
        }

        [Fact]
        public void SaveAsHexpack_WithCurrentFilePath_SavesDirectlyAndRaisesDocumentModified()
        {
            var (list, _, _) = CreateSampleAnimations();
            using var vm = new FlipperSimulatorViewModel(list, "TestPack");

            string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"HexpriteTestPack_{Guid.NewGuid():N}.hexpack");
            try
            {
                vm.CurrentFilePath = tempFile;

                bool modifiedRaised = false;
                vm.DocumentModified += (s, e) => modifiedRaised = true;

                vm.SaveAsHexpack();

                Assert.True(System.IO.File.Exists(tempFile));
                Assert.True(modifiedRaised, "SaveAsHexpack should raise DocumentModified.");
                Assert.Equal(tempFile, vm.CurrentFilePath);
            }
            finally
            {
                if (System.IO.File.Exists(tempFile))
                {
                    System.IO.File.Delete(tempFile);
                }
            }
        }
    }
}
