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
    public class FlipperM3EmpiricalChallengeTests
    {
        private sealed class MockTabServiceForEmpirical : IWorkspaceTabService
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
            public (string Title, SpriteState Sprite)? GetActiveSprite() => ActiveSprite != null ? (ActiveSpriteTitle ?? "ActiveTab", ActiveSprite) : null;
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

        #region Objective 1: Animation Preview Playback State Transitions, Frame Index Bounds & Scrubber Behavior

        [Fact]
        public void Challenge_Preview_ExtremeIndexClamping_MaintainsTelemetryAndZeroCrash()
        {
            var sprite = new SpriteState(128, 64) { FrameRateFps = 12 };
            sprite.Frames.Clear();
            for (int i = 0; i < 5; i++) sprite.Frames.Add(new FrameState { Name = $"Frame {i + 1}" });

            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("test_anim", sprite, new FlipperManifestEntry { Name = "test_anim", MinLevel = 1, MaxLevel = 30, Weight = 1 })
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);

            Assert.Equal(5, vm.PreviewTotalFrames);
            Assert.Equal(4, vm.PreviewMaxFrameIndex);
            Assert.True(vm.PreviewHasMultipleFrames);
            Assert.Equal("FPS: 12", vm.PreviewFpsBadgeText);
            Assert.Equal("128 × 64", vm.PreviewDimensionsText);

            // Test extreme positive integer
            vm.CurrentPreviewFrameIndex = 999999;
            Assert.Equal(4, vm.CurrentPreviewFrameIndex);
            Assert.Equal(4, vm.PreviewFrameIndex);
            Assert.Equal("Frame 5 / 5", vm.PreviewFrameCountText);

            // Test extreme negative integer
            vm.CurrentPreviewFrameIndex = -999999;
            Assert.Equal(0, vm.CurrentPreviewFrameIndex);
            Assert.Equal(0, vm.PreviewFrameIndex);
            Assert.Equal("Frame 1 / 5", vm.PreviewFrameCountText);

            // Test int.MaxValue and int.MinValue via property
            vm.CurrentPreviewFrameIndex = int.MaxValue;
            Assert.Equal(4, vm.CurrentPreviewFrameIndex);
            vm.CurrentPreviewFrameIndex = int.MinValue;
            Assert.Equal(0, vm.CurrentPreviewFrameIndex);
        }

        [Fact]
        public void Challenge_Preview_NonLoopingPlayback_StopsAtEndAndDoesNotWrap()
        {
            var sprite = new SpriteState(128, 64) { FrameRateFps = 10 };
            sprite.Frames.Clear();
            sprite.Frames.Add(new FrameState { Name = "F1" });
            sprite.Frames.Add(new FrameState { Name = "F2" });
            sprite.Frames.Add(new FrameState { Name = "F3" });

            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_nonloop", sprite, new FlipperManifestEntry { Name = "anim_nonloop", MinLevel = 1, MaxLevel = 30, Weight = 1 })
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);
            vm.IsPreviewLooping = false;
            Assert.Equal("➡️ Loop: Off", vm.PreviewLoopButtonText);

            // Start playing at frame index 0
            vm.CurrentPreviewFrameIndex = 0;
            vm.TogglePreviewPlayCommand.Execute(null);
            Assert.True(vm.IsPreviewPlaying);
            Assert.Equal("⏸ Pause", vm.PreviewPlayButtonText);

            // Advance to frame 1
            vm.PreviewNextFrameCommand.Execute(null);
            Assert.Equal(1, vm.CurrentPreviewFrameIndex);
            Assert.True(vm.IsPreviewPlaying);

            // Advance to frame 2 (last frame)
            vm.PreviewNextFrameCommand.Execute(null);
            Assert.Equal(2, vm.CurrentPreviewFrameIndex);
            Assert.True(vm.IsPreviewPlaying);

            // Advancing past last frame when non-looping should stop playback and clamp
            vm.PreviewNextFrameCommand.Execute(null);
            Assert.Equal(2, vm.CurrentPreviewFrameIndex);
            Assert.False(vm.IsPreviewPlaying);
            Assert.Equal("▶ Play", vm.PreviewPlayButtonText);

            // If user presses Play while at the end of non-looping animation, it should reset to frame 0 and play
            vm.TogglePreviewPlayCommand.Execute(null);
            Assert.True(vm.IsPreviewPlaying);
            Assert.Equal(0, vm.CurrentPreviewFrameIndex);
        }

        [Fact]
        public void Challenge_Preview_SpeedMultiplier_BoundsAndCycleFidelity()
        {
            var vm = new FlipperScheduleMatrixViewModel();

            // Set speed outside valid [0.25, 4.0] range
            vm.PreviewSpeedMultiplier = 100.0;
            Assert.Equal(4.0, vm.PreviewSpeedMultiplier);
            Assert.Equal("4x", vm.PreviewSpeedText);

            vm.PreviewSpeedMultiplier = -10.0;
            Assert.Equal(0.25, vm.PreviewSpeedMultiplier);
            Assert.Equal("0.3x", vm.PreviewSpeedText);

            // String parsing edge cases
            vm.SetPreviewSpeed("1.5");
            Assert.Equal(1.5, vm.PreviewSpeedMultiplier);

            vm.SetPreviewSpeed("invalid_string");
            Assert.Equal(1.5, vm.PreviewSpeedMultiplier); // Unchanged

            vm.SetPreviewSpeed(3);
            Assert.Equal(3.0, vm.PreviewSpeedMultiplier);
        }

        [Fact]
        public void Challenge_Preview_SwitchSelectionAcrossVariedDimensionsAndCounts()
        {
            var s1 = new SpriteState(64, 32) { FrameRateFps = 20 };
            s1.Frames.Clear();
            for (int i = 0; i < 8; i++) s1.Frames.Add(new FrameState { Name = $"F{i}" });

            var s2 = new SpriteState(128, 64) { FrameRateFps = 5 };
            s2.Frames.Clear();
            s2.Frames.Add(new FrameState { Name = "OnlyFrame" });

            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_multi", s1, new FlipperManifestEntry { Name = "anim_multi", MinLevel = 1, MaxLevel = 15, Weight = 1 }),
                ("anim_single", s2, new FlipperManifestEntry { Name = "anim_single", MinLevel = 16, MaxLevel = 30, Weight = 1 })
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);

            // Select anim_multi
            vm.SelectedEntry = vm.Entries[0];
            Assert.Equal(8, vm.PreviewTotalFrames);
            Assert.Equal("64 × 32", vm.PreviewDimensionsText);
            Assert.Equal("FPS: 20", vm.PreviewFpsBadgeText);
            Assert.Equal("Frame 1 / 8", vm.PreviewFrameCountText);

            vm.CurrentPreviewFrameIndex = 7;
            Assert.Equal(7, vm.CurrentPreviewFrameIndex);

            // Switch to anim_single (has only 1 frame)
            vm.SelectedEntry = vm.Entries[1];
            Assert.Equal(0, vm.CurrentPreviewFrameIndex);
            Assert.Equal(1, vm.PreviewTotalFrames);
            Assert.Equal("128 × 64", vm.PreviewDimensionsText);
            Assert.Equal("FPS: 5", vm.PreviewFpsBadgeText);
            Assert.Equal("Frame 1 / 1", vm.PreviewFrameCountText);
            Assert.False(vm.PreviewHasMultipleFrames);

            // Deselect all
            vm.SelectedEntry = null;
            Assert.Equal("No Animation Selected", vm.PreviewFrameCountText);
            Assert.Equal(1, vm.PreviewTotalFrames);
        }

        #endregion

        #region Objective 2: Search Filter Query Matching, Stage Filters, Mood Filters & Selection Maintenance

        [Fact]
        public void Challenge_Filter_MultiCriteriaAndCornerCases()
        {
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("baby_hap_1", new SpriteState(128, 64), new FlipperManifestEntry { Name = "baby_hap_1", MinLevel = 1, MaxLevel = 3, MinButthurt = 0, MaxButthurt = 4, Weight = 1 }),
                ("baby_neu_1", new SpriteState(128, 64), new FlipperManifestEntry { Name = "baby_neu_1", MinLevel = 4, MaxLevel = 9, MinButthurt = 5, MaxButthurt = 8, Weight = 1 }),
                ("teen_ang_1", new SpriteState(128, 64), new FlipperManifestEntry { Name = "teen_ang_1", MinLevel = 10, MaxLevel = 19, MinButthurt = 9, MaxButthurt = 14, Weight = 1 }),
                ("adult_hap_1", new SpriteState(128, 64), new FlipperManifestEntry { Name = "adult_hap_1", MinLevel = 20, MaxLevel = 25, MinButthurt = 0, MaxButthurt = 4, Weight = 1 }),
                ("adult_ang_1", new SpriteState(128, 64), new FlipperManifestEntry { Name = "adult_ang_1", MinLevel = 26, MaxLevel = 30, MinButthurt = 10, MaxButthurt = 14, Weight = 1 }),
                ("spanning_bridge", new SpriteState(128, 64), new FlipperManifestEntry { Name = "spanning_bridge", MinLevel = 8, MaxLevel = 22, MinButthurt = 0, MaxButthurt = 14, Weight = 1 })
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);
            Assert.Equal(6, vm.Entries.Count);
            Assert.Equal(6, vm.FilteredEntries.Count);
            Assert.Equal("6 Animations", vm.FilterCountText);

            // 1. Search filter with whitespace and case variations
            vm.SearchFilterText = "  BABY_  ";
            Assert.Equal(2, vm.FilteredEntries.Count);
            Assert.Equal("2 of 6 Animations", vm.FilterCountText);
            Assert.True(vm.HasActiveFilters);

            // 2. Search matching DetailsText directly (e.g. "M5-8")
            vm.SearchFilterText = "M5-8";
            Assert.Single(vm.FilteredEntries);
            Assert.Equal("baby_neu_1", vm.FilteredEntries[0].Name);

            // 3. Spanning stage filter accurately isolates cross-stage entries
            vm.SearchFilterText = string.Empty;
            vm.SelectedStageFilter = "Spanning";
            Assert.Single(vm.FilteredEntries);
            Assert.Equal("spanning_bridge", vm.FilteredEntries[0].Name);

            // 4. IssuesOnly filter when no issues exist vs when issues introduced
            vm.SelectedStageFilter = "IssuesOnly";
            Assert.Empty(vm.FilteredEntries);
            Assert.Equal("0 of 6 Animations", vm.FilterCountText);

            // Introduce validation issue on baby_hap_1 (out of bounds)
            vm.Entries[0].Entry.MinLevel = 99;
            vm.RecalculateMatrix();

            vm.SelectedStageFilter = "IssuesOnly";
            Assert.Single(vm.FilteredEntries);
            Assert.Equal("baby_hap_1", vm.FilteredEntries[0].Name);

            // 5. Zero-result query selection maintenance
            vm.SearchFilterText = "nonexistent_query_xyz";
            Assert.Empty(vm.FilteredEntries);
            Assert.Equal("0 of 6 Animations", vm.FilterCountText);

            // Navigation commands when 0 filtered items exist do not throw
            var ex1 = Record.Exception(() => vm.SelectNextEntryCommand.Execute(null));
            var ex2 = Record.Exception(() => vm.SelectPreviousEntryCommand.Execute(null));
            Assert.Null(ex1);
            Assert.Null(ex2);

            // 6. Reset Filters restoration
            vm.ResetFiltersCommand.Execute(null);
            Assert.Equal(6, vm.FilteredEntries.Count);
            Assert.False(vm.HasActiveFilters);
        }

        [Fact]
        public void Challenge_Filter_StockMode_StageFilteringBoundaries()
        {
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("stock_b", new SpriteState(128, 64), new FlipperManifestEntry { Name = "stock_b", MinLevel = 1, MaxLevel = 1, MinButthurt = 0, MaxButthurt = 14, Weight = 1 }),
                ("stock_t", new SpriteState(128, 64), new FlipperManifestEntry { Name = "stock_t", MinLevel = 2, MaxLevel = 2, MinButthurt = 0, MaxButthurt = 14, Weight = 1 }),
                ("stock_a", new SpriteState(128, 64), new FlipperManifestEntry { Name = "stock_a", MinLevel = 3, MaxLevel = 3, MinButthurt = 0, MaxButthurt = 14, Weight = 1 }),
                ("stock_span", new SpriteState(128, 64), new FlipperManifestEntry { Name = "stock_span", MinLevel = 1, MaxLevel = 3, MinButthurt = 0, MaxButthurt = 14, Weight = 1 })
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);
            Assert.True(vm.IsStockMode);
            Assert.Equal(3, vm.MaxAllowedLevel);

            vm.SelectedStageFilter = "Baby";
            Assert.Equal(2, vm.FilteredEntries.Count); // stock_b and stock_span

            vm.SelectedStageFilter = "Teen";
            Assert.Equal(2, vm.FilteredEntries.Count); // stock_t and stock_span

            vm.SelectedStageFilter = "Adult";
            Assert.Equal(2, vm.FilteredEntries.Count); // stock_a and stock_span

            vm.SelectedStageFilter = "Spanning";
            Assert.Single(vm.FilteredEntries);
            Assert.Equal("stock_span", vm.FilteredEntries[0].Name);
        }

        #endregion

        #region Objective 3: Individual Quick Fixes & Batch FixAllDiagnostics

        [Fact]
        public void Challenge_Diagnostics_IndividualQuickFixes_AllCodes()
        {
            var mockTabs = new MockTabServiceForEmpirical();
            var vm = new FlipperScheduleMatrixViewModel(tabService: mockTabs);

            // 1. FZ001: Blank Name
            var entry1 = vm.Entries[0];
            entry1.Name = "   ";
            vm.ExecuteQuickFix("FZ001", entry1);
            Assert.False(string.IsNullOrWhiteSpace(entry1.Name));
            Assert.StartsWith("anim_", entry1.Name);

            // 2. FZ002 / FZ003: Level Inversion and Out of Bounds
            var entry2 = vm.Entries[1];
            entry2.Entry.MinLevel = 100;
            entry2.Entry.MaxLevel = 0;
            vm.ExecuteQuickFix("FZ002", entry2);
            Assert.InRange(entry2.MinLevel, 1, 30);
            Assert.InRange(entry2.MaxLevel, 1, 30);
            Assert.True(entry2.MinLevel <= entry2.MaxLevel);

            // 3. FZ004 / FZ005: Mood Inversion and Out of Bounds
            entry2.Entry.MinButthurt = 50;
            entry2.Entry.MaxButthurt = -5;
            vm.ExecuteQuickFix("FZ004", entry2);
            Assert.InRange(entry2.MinButthurt, 0, 14);
            Assert.InRange(entry2.MaxButthurt, 0, 14);
            Assert.True(entry2.MinButthurt <= entry2.MaxButthurt);

            // 4. FZ006: Zero weight
            entry2.Entry.Weight = -10;
            vm.ExecuteQuickFix("FZ006", entry2);
            Assert.Equal(1, entry2.Weight);

            // 5. FZ007: Excessive weight
            entry2.Entry.Weight = 9999;
            vm.ExecuteQuickFix("FZ007", entry2);
            Assert.Equal(10, entry2.Weight);

            // 6. FZ011: Duplicate Names
            vm.Entries[2].Name = vm.Entries[0].Name;
            vm.RecalculateMatrix();
            Assert.Contains(vm.ActionableDiagnostics, d => d.Code == "FZ011");
            vm.ExecuteQuickFix("FZ011", vm.Entries[2]);
            Assert.NotEqual(vm.Entries[0].Name, vm.Entries[2].Name);

            // 7. FZM001: Dimension Mismatch (> 128x64)
            string animName = vm.Entries[0].Name;
            var bigSprite = new SpriteState(256, 128);
            (vm.AnimationSprites as Dictionary<string, SpriteState>)![animName] = bigSprite;
            vm.RecalculateMatrix();
            Assert.Contains(vm.ActionableDiagnostics, d => d.Code == "FZM001");
            vm.ExecuteQuickFix("FZM001", vm.Entries[0]);
            Assert.Equal(128, vm.AnimationSprites[animName].Width);
            Assert.Equal(64, vm.AnimationSprites[animName].Height);

            // 8. FZM004: FramesOrder OOB
            var cycleSprite = new SpriteState(128, 64);
            cycleSprite.Frames.Clear();
            cycleSprite.Frames.Add(new FrameState { Name = "F0" });
            cycleSprite.FlipperCycle = new FlipperAnimationCycle { FramesOrder = [0, 50, -10] };
            (vm.AnimationSprites as Dictionary<string, SpriteState>)![animName] = cycleSprite;
            vm.RecalculateMatrix();
            Assert.Contains(vm.ActionableDiagnostics, d => d.Code == "FZM004");
            vm.ExecuteQuickFix("FZM004", vm.Entries[0]);
            Assert.All(cycleSprite.FlipperCycle.FramesOrder, idx => Assert.Equal(0, idx));

            // 9. FZ_MISSING_SPRITE: Missing Sprite Tab creation
            var ghostEntry = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "ghost_sprite_anim" });
            vm.Entries.Add(ghostEntry);
            vm.RecalculateMatrix();
            Assert.Contains(vm.ActionableDiagnostics, d => d.Code == "FZ_MISSING_SPRITE");
            vm.ExecuteQuickFix("FZ_MISSING_SPRITE", ghostEntry);
            Assert.True(vm.AnimationSprites.ContainsKey("ghost_sprite_anim"));
            Assert.Equal(128, vm.AnimationSprites["ghost_sprite_anim"].Width);
            Assert.Equal(64, vm.AnimationSprites["ghost_sprite_anim"].Height);
        }

        [Fact]
        public void Challenge_Diagnostics_FixAllDiagnostics_BatchResolvesCompoundAnomalies()
        {
            var mockTabs = new MockTabServiceForEmpirical();
            var vm = new FlipperScheduleMatrixViewModel(tabService: mockTabs);

            // Inject catastrophic compound errors across entries:
            // - Entry 0: Blank name, min > max level, negative weight
            // - Entry 1: Duplicate name with entry 2, mood out of bounds (100)
            // - Entry 2: High weight (500), gap creation
            // - Entry 3: Unregistered ghost animation with no sprite
            vm.Entries.Clear();
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "", MinLevel = 25, MaxLevel = 5, MinButthurt = 0, MaxButthurt = 14, Weight = -5 }));
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "clash_name", MinLevel = 1, MaxLevel = 5, MinButthurt = 0, MaxButthurt = 99, Weight = 1 }));
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "clash_name", MinLevel = 6, MaxLevel = 10, MinButthurt = 0, MaxButthurt = 14, Weight = 500 }));
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "unbacked_ghost", MinLevel = 11, MaxLevel = 15, MinButthurt = 0, MaxButthurt = 14, Weight = 1 }));

            vm.RecalculateMatrix();

            Assert.True(vm.HasActionableDiagnostics);
            Assert.True(vm.ActionableDiagnostics.Count >= 4);
            Assert.True(vm.Matrix.UncoveredStatesCount > 0);

            // Execute Batch Fix All
            vm.FixAllDiagnosticsCommand.Execute(null);

            // Verify full restoration:
            // 1. All names non-empty and unique
            var names = vm.Entries.Select(e => e.Name).ToList();
            Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
            Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());

            // 2. All level bounds within 1..30 and min <= max
            Assert.All(vm.Entries, e =>
            {
                Assert.InRange(e.MinLevel, 1, 30);
                Assert.InRange(e.MaxLevel, 1, 30);
                Assert.True(e.MinLevel <= e.MaxLevel);
            });

            // 3. All mood bounds within 0..14 and min <= max
            Assert.All(vm.Entries, e =>
            {
                Assert.InRange(e.MinButthurt, 0, 14);
                Assert.InRange(e.MaxButthurt, 0, 14);
                Assert.True(e.MinButthurt <= e.MaxButthurt);
            });

            // 4. All weights valid (1..100)
            Assert.All(vm.Entries, e => Assert.InRange(e.Weight, 1, 100));

            // 5. 100% matrix coverage restored
            Assert.Equal(0, vm.Matrix.UncoveredStatesCount);
            Assert.Equal(450, vm.Matrix.CoveredCellsCount);
            Assert.Equal(100.0, vm.Matrix.CoveragePercentage);
            Assert.False(vm.HasValidationIssues);
            Assert.Equal("✅ Valid Manifest", vm.ValidationStatusText);

            // 6. Undo restores prior corrupted state without error
            vm.Undo();
            Assert.True(vm.HasValidationIssues);

            // 7. Redo re-applies the fix
            vm.Redo();
            Assert.False(vm.HasValidationIssues);
            Assert.Equal(100.0, vm.Matrix.CoveragePercentage);
        }

        #endregion
    }
}
