using System;
using System.Collections.Generic;
using System.Linq;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels.Flipper;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FlipperM3BoundaryAndStressChallengerTests
    {
        #region Mock Helper

        private class MockTabService : IWorkspaceTabService
        {
            public List<(string Title, SpriteState Sprite)> OpenSprites { get; } = [];
            public string? LastActivatedTab { get; private set; }
            public string? LastOpenedTab { get; private set; }
            public string? LastRenamedOldTitle { get; private set; }
            public string? LastRenamedNewTitle { get; private set; }
            public SpriteState? ActiveSprite { get; set; }
            public string? ActiveSpriteTitle { get; set; }

            public void OpenSpritesInTabs(IEnumerable<SpriteState> sprites, string tabNamePrefix = "Imported") { }
            public void OpenSpritesInTabs(IEnumerable<(string Name, SpriteState Sprite)> sprites) { }
            public void OpenSpriteInTab(SpriteState sprite, string title)
            {
                LastOpenedTab = title;
                OpenSprites.Add((title, sprite));
            }
            public SpriteState? GetActiveSpriteState() => ActiveSprite;
            public (string Title, SpriteState Sprite)? GetActiveSprite()
            {
                if (ActiveSprite != null)
                {
                    return (ActiveSpriteTitle ?? "ActiveTab", ActiveSprite);
                }
                return null;
            }
            public bool[]? GetActiveFramePixels(bool animated = false) => null;
            public IReadOnlyList<(string Title, SpriteState Sprite)> GetAllOpenSprites() => OpenSprites;
            public bool ActivateTabByTitle(string title)
            {
                LastActivatedTab = title;
                return OpenSprites.Any(s => string.Equals(s.Title, title, StringComparison.OrdinalIgnoreCase));
            }
            public bool RenameTab(string oldTitle, string newTitle)
            {
                LastRenamedOldTitle = oldTitle;
                LastRenamedNewTitle = newTitle;
                int idx = OpenSprites.FindIndex(s => string.Equals(s.Title, oldTitle, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    var sprite = OpenSprites[idx].Sprite;
                    OpenSprites[idx] = (newTitle, sprite);
                    return true;
                }
                return false;
            }
            public void OpenAssetPackInTab(IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>? pack = null, string packName = "Flipper Asset Pack") { }
        }

        #endregion

        #region 1. Animation Preview & Frame Scrubber Boundary & Stress Tests

        [Fact]
        public void Preview_ZeroFramesSprite_HandlesGracefullyWithoutThrowing()
        {
            var emptySprite = new SpriteState(128, 64);
            emptySprite.Frames.Clear();

            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("empty_sprite_anim", emptySprite, new FlipperManifestEntry { Name = "empty_sprite_anim", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 })
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);
            vm.SelectedEntry = vm.Entries.FirstOrDefault();

            // Invariants on empty sprite
            Assert.Equal(1, vm.PreviewTotalFrames);
            Assert.Equal(0, vm.PreviewMaxFrameIndex);
            Assert.False(vm.PreviewHasMultipleFrames);

            // Index Seeking Boundary
            vm.PreviewFrameIndex = 0;
            Assert.Equal(0, vm.PreviewFrameIndex);
            vm.PreviewFrameIndex = 5;
            Assert.Equal(0, vm.PreviewFrameIndex);
            vm.PreviewFrameIndex = -5;
            Assert.Equal(0, vm.PreviewFrameIndex);

            // Next / Prev commands on 0 frames
            vm.PreviewNextFrame();
            Assert.Equal(0, vm.PreviewFrameIndex);
            vm.PreviewPrevFrame();
            Assert.Equal(0, vm.PreviewFrameIndex);

            // Playback controls on 0 frames
            vm.TogglePreviewPlay();
            Assert.True(vm.IsPreviewPlaying);
            vm.TogglePreviewPlay();
            Assert.False(vm.IsPreviewPlaying);

            // Render Preview Frame
            var exception = Record.Exception(() => vm.RenderPreviewFrame());
            Assert.Null(exception);
        }

        [Fact]
        public void Preview_LargeFrameCount_500Frames_ScrubbingAndLoopingIntegrity()
        {
            var largeSprite = new SpriteState(128, 64);
            largeSprite.Frames.Clear();
            for (int i = 0; i < 500; i++)
            {
                largeSprite.Frames.Add(new FrameState { Name = $"Frame {i + 1}" });
            }

            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("large_anim", largeSprite, new FlipperManifestEntry { Name = "large_anim", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 })
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);
            vm.SelectedEntry = vm.Entries.FirstOrDefault();

            Assert.Equal(500, vm.PreviewTotalFrames);
            Assert.Equal(499, vm.PreviewMaxFrameIndex);
            Assert.True(vm.PreviewHasMultipleFrames);

            // Scrubbing to arbitrary points
            vm.PreviewFrameIndex = 250;
            Assert.Equal(250, vm.PreviewFrameIndex);
            Assert.Equal(250, vm.CurrentPreviewFrameIndex);
            Assert.Equal("Frame 251 / 500", vm.PreviewFrameCountText);

            // Out-of-bounds seeking clamping
            vm.PreviewFrameIndex = 9999;
            Assert.Equal(499, vm.PreviewFrameIndex);
            Assert.Equal("Frame 500 / 500", vm.PreviewFrameCountText);

            vm.PreviewFrameIndex = -9999;
            Assert.Equal(0, vm.PreviewFrameIndex);
            Assert.Equal("Frame 1 / 500", vm.PreviewFrameCountText);

            // Looping Next/Prev wrap-around
            vm.IsPreviewLooping = true;
            vm.PreviewFrameIndex = 499;
            vm.PreviewNextFrame();
            Assert.Equal(0, vm.PreviewFrameIndex);

            vm.PreviewPrevFrame();
            Assert.Equal(499, vm.PreviewFrameIndex);

            // Non-looping Next/Prev boundary halt
            vm.IsPreviewLooping = false;
            vm.PreviewFrameIndex = 499;
            vm.PreviewNextFrame();
            Assert.Equal(499, vm.PreviewFrameIndex);

            vm.PreviewFrameIndex = 0;
            vm.PreviewPrevFrame();
            Assert.Equal(0, vm.PreviewFrameIndex);
        }

        [Fact]
        public void Preview_CorruptedFlipperCycle_IndicesClampedSafelyDuringRender()
        {
            var corruptedSprite = new SpriteState(128, 64);
            corruptedSprite.Frames.Clear();
            corruptedSprite.Frames.Add(new FrameState { Name = "F1" });
            corruptedSprite.Frames.Add(new FrameState { Name = "F2" });

            // Cycle contains out-of-bounds indices [ -10, 50, 999, 1 ]
            corruptedSprite.FlipperCycle = new FlipperAnimationCycle
            {
                FramesOrder = [-10, 50, 999, 1]
            };

            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("corrupted_cycle", corruptedSprite, new FlipperManifestEntry { Name = "corrupted_cycle", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 })
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);
            vm.SelectedEntry = vm.Entries.FirstOrDefault();

            Assert.Equal(4, vm.PreviewTotalFrames);
            Assert.Equal(3, vm.PreviewMaxFrameIndex);

            // Render all frames in corrupted cycle — must not throw IndexOutOfRangeException
            for (int i = 0; i < 4; i++)
            {
                vm.PreviewFrameIndex = i;
                var ex = Record.Exception(() => vm.RenderPreviewFrame());
                Assert.Null(ex);
            }
        }

        [Fact]
        public void Preview_SpeedMultiplier_RapidCyclingAndAdversarialInputs()
        {
            var vm = new FlipperScheduleMatrixViewModel();

            // Rapid cycling 1000 times
            for (int i = 0; i < 1000; i++)
            {
                vm.CyclePreviewSpeed();
                Assert.InRange(vm.PreviewSpeedMultiplier, 0.25, 4.0);
            }

            // Adversarial values via SetPreviewSpeed
            vm.SetPreviewSpeed(-100.0);
            Assert.Equal(0.25, vm.PreviewSpeedMultiplier);

            vm.SetPreviewSpeed(0.0);
            Assert.Equal(0.25, vm.PreviewSpeedMultiplier);

            vm.SetPreviewSpeed(100000.0);
            Assert.Equal(4.0, vm.PreviewSpeedMultiplier);

            vm.SetPreviewSpeed(double.PositiveInfinity);
            Assert.Equal(4.0, vm.PreviewSpeedMultiplier);

            // String parsing
            vm.SetPreviewSpeed("2.5");
            Assert.Equal(2.5, vm.PreviewSpeedMultiplier);
            Assert.Equal("2.5x", vm.PreviewSpeedText);

            vm.SetPreviewSpeed("invalid_garbage");
            // Should remain 2.5 on invalid parse
            Assert.Equal(2.5, vm.PreviewSpeedMultiplier);
        }

        #endregion

        #region 2. Search & Filter Ergonomics Adversarial Tests

        [Theory]
        [InlineData(".*")]
        [InlineData("[a-z]+")]
        [InlineData(@"\d{3}")]
        [InlineData("(")]
        [InlineData(")")]
        [InlineData("\\")]
        [InlineData("?")]
        [InlineData("*")]
        [InlineData("+")]
        [InlineData("^")]
        [InlineData("$")]
        [InlineData("{")]
        [InlineData("}")]
        [InlineData("|")]
        [InlineData("!@#$%^&*()_+=-`~[]{}|;:'\",.<>/?")]
        [InlineData("🎨✨👾")]
        [InlineData("日本語テキスト")]
        public void SearchFilter_SpecialAndRegexCharacters_DoesNotThrowAndPerformsLiteralSearch(string specialPattern)
        {
            var vm = new FlipperScheduleMatrixViewModel();

            // Add an entry that actually contains special chars
            var specialEntry = new FlipperScheduleEntryViewModel(new FlipperManifestEntry
            {
                Name = $"anim_{specialPattern}",
                MinLevel = 1,
                MaxLevel = 10,
                MinButthurt = 0,
                MaxButthurt = 14
            });
            vm.Entries.Add(specialEntry);

            var ex = Record.Exception(() =>
            {
                vm.SearchFilterText = specialPattern;
            });
            Assert.Null(ex);

            // Must match the special entry
            Assert.Contains(specialEntry, vm.FilteredEntries);
        }

        [Fact]
        public void SearchFilter_EmptyDataset_AllOperationsRemainSafe()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.Entries.Clear();
            vm.ApplyEntryFilter();

            Assert.Empty(vm.FilteredEntries);
            Assert.Equal("0 Animations", vm.FilterCountText);
            Assert.False(vm.HasActiveFilters);

            // Searching on empty dataset
            vm.SearchFilterText = "anything";
            Assert.Empty(vm.FilteredEntries);
            Assert.True(vm.HasActiveFilters);
            Assert.Equal("0 Animations", vm.FilterCountText);

            // Next / Prev on empty list
            var ex1 = Record.Exception(() => vm.SelectNextEntryCommand.Execute(null));
            Assert.Null(ex1);
            var ex2 = Record.Exception(() => vm.SelectPreviousEntryCommand.Execute(null));
            Assert.Null(ex2);

            // Reset filters on empty list
            var ex3 = Record.Exception(() => vm.ResetFilters());
            Assert.Null(ex3);
            Assert.Empty(vm.FilteredEntries);
            Assert.Equal("0 Animations", vm.FilterCountText);
        }

        [Fact]
        public void SearchFilter_StressCyclingFilterCombinations_MaintainsInvariants()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.Entries.Clear();

            // Add 11 diverse entries across stages and moods
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "baby_happy", MinLevel = 1, MaxLevel = 9, MinButthurt = 0, MaxButthurt = 4 }));
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "baby_neutral", MinLevel = 1, MaxLevel = 9, MinButthurt = 5, MaxButthurt = 8 }));
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "baby_angry", MinLevel = 1, MaxLevel = 9, MinButthurt = 9, MaxButthurt = 14 }));

            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "teen_happy", MinLevel = 10, MaxLevel = 19, MinButthurt = 0, MaxButthurt = 4 }));
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "teen_neutral", MinLevel = 10, MaxLevel = 19, MinButthurt = 5, MaxButthurt = 8 }));
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "teen_angry", MinLevel = 10, MaxLevel = 19, MinButthurt = 9, MaxButthurt = 14 }));

            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "adult_happy", MinLevel = 20, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 4 }));
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "adult_neutral", MinLevel = 20, MaxLevel = 30, MinButthurt = 5, MaxButthurt = 8 }));
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "adult_angry", MinLevel = 20, MaxLevel = 30, MinButthurt = 9, MaxButthurt = 14 }));

            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "spanning_all", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14 }));
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "broken_entry", MinLevel = 50, MaxLevel = 2, MinButthurt = 20, MaxButthurt = 0 }));

            vm.RecalculateMatrix();

            var stages = vm.StageFilters;
            var moods = vm.MoodFilters;

            // Stress test 50 random combinations while changing selection
            var rng = new Random(42);
            for (int step = 0; step < 50; step++)
            {
                vm.SelectedStageFilter = stages[rng.Next(stages.Count)];
                vm.SelectedMoodFilter = moods[rng.Next(moods.Count)];
                vm.SearchFilterText = rng.Next(3) == 0 ? "happy" : (rng.Next(3) == 1 ? "anim" : "");

                if (vm.FilteredEntries.Count > 0)
                {
                    int selectIdx = rng.Next(vm.FilteredEntries.Count);
                    vm.SelectedEntry = vm.FilteredEntries[selectIdx];
                    Assert.Contains(vm.SelectedEntry, vm.FilteredEntries);

                    // Step next/prev
                    vm.SelectNextEntryCommand.Execute(null);
                    Assert.Contains(vm.SelectedEntry, vm.FilteredEntries);
                }
            }

            vm.ResetFilters();
            Assert.Equal(11, vm.FilteredEntries.Count);
        }

        #endregion

        #region 3. Quick-Fix Idempotency & Undo Stack Stress Tests

        [Fact]
        public void QuickFix_RepeatedExecution_BoundsAndWeights_AreIdempotent()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var target = vm.Entries[0];

            // Inverted levels & moods and zero weight
            target.Entry.MinLevel = 28;
            target.Entry.MaxLevel = 3;
            target.Entry.MinButthurt = 13;
            target.Entry.MaxButthurt = 1;
            target.Entry.Weight = 0;

            // Execute quick fixes 10 times consecutively
            for (int i = 0; i < 10; i++)
            {
                vm.ExecuteQuickFix("FZ002", target);
                vm.ExecuteQuickFix("FZ004", target);
                vm.ExecuteQuickFix("FZ006", target);
            }

            Assert.Equal(3, target.MinLevel);
            Assert.Equal(28, target.MaxLevel);
            Assert.Equal(1, target.MinButthurt);
            Assert.Equal(13, target.MaxButthurt);
            Assert.Equal(1, target.Weight);
        }

        [Fact]
        public void QuickFix_FixAllDiagnostics_RepeatedExecution_IsIdempotent()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.Entries.Clear();

            // Create several defective entries
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "", MinLevel = 25, MaxLevel = 5, MinButthurt = 14, MaxButthurt = 0, Weight = 0 }));
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "dup_name", MinLevel = 1, MaxLevel = 10, MinButthurt = 0, MaxButthurt = 5, Weight = 500 }));
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "dup_name", MinLevel = 11, MaxLevel = 20, MinButthurt = 6, MaxButthurt = 10, Weight = 5 }));

            vm.RecalculateMatrix();
            Assert.True(vm.HasActionableDiagnostics);

            // Apply FixAll 5 times
            for (int i = 0; i < 5; i++)
            {
                vm.FixAllDiagnostics();
            }

            // Invariants after repeated FixAll:
            Assert.All(vm.Entries, e =>
            {
                Assert.False(string.IsNullOrWhiteSpace(e.Name));
                Assert.True(e.MinLevel <= e.MaxLevel);
                Assert.InRange(e.MinLevel, 1, 30);
                Assert.InRange(e.MaxLevel, 1, 30);
                Assert.True(e.MinButthurt <= e.MaxButthurt);
                Assert.InRange(e.MinButthurt, 0, 14);
                Assert.InRange(e.MaxButthurt, 0, 14);
                Assert.InRange(e.Weight, 1, 100);
            });

            // Names must be unique
            var names = vm.Entries.Select(e => e.Name).ToList();
            Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Fact]
        public void QuickFix_UndoRedoIntegrity_RestoresExactStateStepByStep()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var target = vm.Entries[0];

            // Make corrupted
            target.Entry.MinLevel = 25;
            target.Entry.MaxLevel = 2;
            target.Entry.Weight = 0;

            // Fix 1: FZ002 (Levels)
            vm.ExecuteQuickFix("FZ002", target);
            Assert.Equal(2, vm.Entries[0].MinLevel);
            Assert.Equal(25, vm.Entries[0].MaxLevel);

            // Fix 2: FZ006 (Weight)
            vm.ExecuteQuickFix("FZ006", vm.Entries[0]);
            Assert.Equal(1, vm.Entries[0].Weight);

            // Undo Fix 2 (Weight)
            Assert.True(vm.CanUndo);
            vm.UndoCommand.Execute(null);
            Assert.Equal(0, vm.Entries[0].Weight);
            Assert.Equal(2, vm.Entries[0].MinLevel); // Level fix still active

            // Undo Fix 1 (Levels)
            Assert.True(vm.CanUndo);
            vm.UndoCommand.Execute(null);
            Assert.Equal(25, vm.Entries[0].MinLevel);
            Assert.Equal(2, vm.Entries[0].MaxLevel);

            // Redo Fix 1
            Assert.True(vm.CanRedo);
            vm.RedoCommand.Execute(null);
            Assert.Equal(2, vm.Entries[0].MinLevel);
            Assert.Equal(25, vm.Entries[0].MaxLevel);

            // Redo Fix 2
            Assert.True(vm.CanRedo);
            vm.RedoCommand.Execute(null);
            Assert.Equal(1, vm.Entries[0].Weight);
        }

        [Fact]
        public void QuickFix_FixAllDiagnostics_AtomicUndo_RestoresAllEntriesSimultaneously()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.Entries.Clear();

            // Add 3 broken entries
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "bad1", MinLevel = 20, MaxLevel = 5, MinButthurt = 10, MaxButthurt = 2, Weight = 0 }));
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "bad2", MinLevel = 30, MaxLevel = 1, MinButthurt = 14, MaxButthurt = 0, Weight = 300 }));
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "bad2", MinLevel = 10, MaxLevel = 15, MinButthurt = 5, MaxButthurt = 8, Weight = 5 }));

            vm.RecalculateMatrix();

            // Run FixAllDiagnostics (should push 1 undo state)
            vm.FixAllDiagnostics();

            // Verify all are repaired
            Assert.Equal(5, vm.Entries[0].MinLevel);
            Assert.Equal(20, vm.Entries[0].MaxLevel);
            Assert.Equal(1, vm.Entries[0].Weight);
            Assert.NotEqual(vm.Entries[1].Name, vm.Entries[2].Name);

            // Perform single Undo
            Assert.True(vm.CanUndo);
            vm.UndoCommand.Execute(null);

            // Verify exact broken state is restored
            Assert.Equal(20, vm.Entries[0].MinLevel);
            Assert.Equal(5, vm.Entries[0].MaxLevel);
            Assert.Equal(0, vm.Entries[0].Weight);
            Assert.Equal("bad2", vm.Entries[1].Name);
            Assert.Equal("bad2", vm.Entries[2].Name);

            // Perform single Redo
            Assert.True(vm.CanRedo);
            vm.RedoCommand.Execute(null);

            // Verify repaired state is re-applied
            Assert.Equal(5, vm.Entries[0].MinLevel);
            Assert.Equal(20, vm.Entries[0].MaxLevel);
            Assert.Equal(1, vm.Entries[0].Weight);
            Assert.NotEqual(vm.Entries[1].Name, vm.Entries[2].Name);
        }

        #endregion
    }
}
