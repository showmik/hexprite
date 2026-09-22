using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels.Flipper;
using Moq;
using Xunit;

namespace Hexprite.Tests.ViewModels
{
    [Trait("Category", "Unit")]
    public class FlipperScheduleMatrixViewModelTests
    {
        [Fact]
        public void Constructor_DefaultEntries_InitializesCoverageAndStats()
        {
            var vm = new FlipperScheduleMatrixViewModel();

            Assert.Equal(3, vm.Entries.Count);
            Assert.NotNull(vm.SelectedEntry);
            Assert.Equal("anim_baby", vm.SelectedEntry.Name);
            Assert.Equal(450, vm.Matrix.CoveredCellsCount);
            Assert.Equal(100.0, vm.Matrix.CoveragePercentage);
            Assert.Contains("100% Coverage", vm.CoverageBadgeText);
            Assert.Contains("450 / 450", vm.MatrixStatsText);
        }

        [Fact]
        public void Constructor_CustomPackEntries_PopulatesCorrectly()
        {
            var entry1 = new FlipperManifestEntry { Name = "test_baby", MinLevel = 1, MaxLevel = 15, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var entry2 = new FlipperManifestEntry { Name = "test_adult", MinLevel = 16, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 2 };

            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("test_baby", new SpriteState(128, 64), entry1),
                ("test_adult", new SpriteState(128, 64), entry2)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack, "CustomPack");

            Assert.Equal("CustomPack", vm.PackName);
            Assert.Equal(2, vm.Entries.Count);
            Assert.Equal(100.0, vm.Matrix.CoveragePercentage);
        }

        [Fact]
        public void HoverCell_CoveredCell_ShowsProbabilitiesAndStage()
        {
            var vm = new FlipperScheduleMatrixViewModel();

            // Hover level 1, mood 0 (Happy baby)
            vm.HoverCell(1, 0);
            Assert.Contains("Level 1", vm.CellHoverInfoText);
            Assert.Contains("Baby", vm.CellHoverInfoText);
            Assert.Contains("Happy", vm.CellHoverInfoText);
            Assert.Contains("anim_baby", vm.CellHoverInfoText);

            // Hover level 25, mood 12 (Angry adult)
            vm.HoverCell(25, 12);
            Assert.Contains("Level 25", vm.CellHoverInfoText);
            Assert.Contains("Adult", vm.CellHoverInfoText);
            Assert.Contains("Angry", vm.CellHoverInfoText);
            Assert.Contains("anim_adult", vm.CellHoverInfoText);
        }

        [Fact]
        public void ClearHover_ResetsHoverText()
        {
            var vm = new FlipperScheduleMatrixViewModel();

            vm.HoverCell(5, 5);
            Assert.DoesNotContain("Hover over any state", vm.CellHoverInfoText);

            vm.ClearHover();
            Assert.Contains("Hover over any state", vm.CellHoverInfoText);
        }

        [Fact]
        public void EditEntryBounds_UpdatesMatrixAndStats()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            bool redrawFired = false;
            vm.MatrixRedrawRequested += (s, e) => redrawFired = true;

            // Change baby max level to 5 (creating gap between L6 and L10)
            vm.SelectedEntry = vm.Entries[0];
            vm.SelectedMaxLevel = 5;

            Assert.True(redrawFired);
            Assert.True(vm.Matrix.CoveredCellsCount < 450);
            Assert.Contains("Gaps", vm.MatrixStatsText);
        }

        [Fact]
        public void AutoBalance_Achieves100PercentCoverage()
        {
            var entry = new FlipperManifestEntry { Name = "solo_anim", MinLevel = 1, MaxLevel = 5, MinButthurt = 0, MaxButthurt = 5, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("solo_anim", new SpriteState(128, 64), entry)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);
            Assert.True(vm.Matrix.CoveredCellsCount < 450);

            bool redrawFired = false;
            vm.MatrixRedrawRequested += (s, e) => redrawFired = true;

            vm.AutoBalanceCommand.Execute(null);

            Assert.True(redrawFired);
            Assert.Equal(450, vm.Matrix.CoveredCellsCount);
            Assert.Equal(100.0, vm.Matrix.CoveragePercentage);
            Assert.Contains("100% Coverage", vm.CoverageBadgeText);
        }

        [Fact]
        public void ToggleMode_SwitchesBetweenStockAndExtendedModes()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.False(vm.IsStockMode);
            Assert.True(vm.IsMomentumMode);
            Assert.Equal(30, vm.MaxAllowedLevel);
            Assert.Equal(450, vm.Matrix.TotalCells);

            vm.ToggleModeCommand.Execute(null);

            Assert.True(vm.IsStockMode);
            Assert.False(vm.IsMomentumMode);
            Assert.Equal(3, vm.MaxAllowedLevel);
            Assert.Equal(45, vm.Matrix.TotalCells);
            Assert.Contains("Stock Mode", vm.ModeBadgeText);

            // Bounds on entries should be clamped to 3
            Assert.All(vm.Entries, e => Assert.InRange(e.MaxLevel, 1, 3));
        }

        [Fact]
        public void AddEntryCommand_AppendsNewEntryAndRecalculates()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            int initialCount = vm.Entries.Count;

            vm.AddEntryCommand.Execute(null);

            Assert.Equal(initialCount + 1, vm.Entries.Count);
            Assert.NotNull(vm.SelectedEntry);
            Assert.StartsWith("anim_", vm.SelectedEntry.Name);
        }

        [Fact]
        public void DuplicateEntryCommand_DuplicatesCurrentSelection()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.SelectedEntry = vm.Entries[0];
            string originalName = vm.SelectedEntry.Name;

            vm.DuplicateEntryCommand.Execute(null);

            Assert.NotNull(vm.SelectedEntry);
            Assert.Equal(originalName + "_copy", vm.SelectedEntry.Name);
            Assert.Equal(4, vm.Entries.Count);
        }

        [Fact]
        public void DeleteEntryCommand_RemovesSelectedEntry()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.Equal(3, vm.Entries.Count);

            vm.SelectedEntry = vm.Entries[1];
            vm.DeleteEntryCommand.Execute(null);

            Assert.Equal(2, vm.Entries.Count);
            Assert.NotNull(vm.SelectedEntry);
        }

        [Fact]
        public void InspectCell_UpdatesSelectedCellAndProbabilities()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.InspectCell(1, 0);

            Assert.Equal(1, vm.SelectedCellLevel);
            Assert.Equal(0, vm.SelectedCellMood);
            Assert.Contains("Baby", vm.SelectedCellSummaryText);
            Assert.Contains("Happy", vm.SelectedCellSummaryText);
            Assert.NotEmpty(vm.SelectedCellProbabilities);
            Assert.Equal("anim_baby", vm.SelectedCellProbabilities[0].Name);
        }

        [Fact]
        public void AutoBalanceStrategyCommand_ExecutesRequestedStrategy()
        {
            var vm = new FlipperScheduleMatrixViewModel();

            vm.AutoBalanceStrategyCommand.Execute("MoodTiers");

            Assert.Equal(3, vm.Entries.Count);
            Assert.Equal(0, vm.Entries[0].MinButthurt);
            Assert.Equal(4, vm.Entries[0].MaxButthurt);
        }

        [Fact]
        public void InspectCell_CoveredCell_SetsCoverageTrueAndGapFalse()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.InspectCell(1, 0);

            Assert.True(vm.HasSelectedCellCoverage);
            Assert.False(vm.HasSelectedCellGap);
            Assert.NotEmpty(vm.SelectedCellProbabilities);
        }

        [Fact]
        public void InspectCell_DeadzoneGapCell_SetsCoverageFalseAndGapTrue()
        {
            var entry = new FlipperManifestEntry { Name = "solo_anim", MinLevel = 1, MaxLevel = 5, MinButthurt = 0, MaxButthurt = 5, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("solo_anim", new SpriteState(128, 64), entry)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);

            // Inspect state (Level 20, Mood 10) which is outside the solo_anim bounds
            vm.InspectCell(20, 10);

            Assert.False(vm.HasSelectedCellCoverage);
            Assert.True(vm.HasSelectedCellGap);
            Assert.Empty(vm.SelectedCellProbabilities);
        }

        [Fact]
        public void AutoBalanceStrategyCommand_StageEvolution_DistributesAcrossLifeStages()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.AutoBalanceStrategyCommand.Execute("StageEvolution");

            Assert.Equal(3, vm.Entries.Count);
            Assert.Equal(1, vm.Entries[0].MinLevel);
            Assert.Equal(9, vm.Entries[0].MaxLevel);
            Assert.Equal(10, vm.Entries[1].MinLevel);
            Assert.Equal(19, vm.Entries[1].MaxLevel);
            Assert.Equal(20, vm.Entries[2].MinLevel);
            Assert.Equal(30, vm.Entries[2].MaxLevel);
        }

        [Fact]
        public void AutoBalanceStrategyCommand_FillGapsOnly_PreservesExistingAndFillsGaps()
        {
            var entry = new FlipperManifestEntry { Name = "corner_anim", MinLevel = 1, MaxLevel = 5, MinButthurt = 0, MaxButthurt = 5, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("corner_anim", new SpriteState(128, 64), entry)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);
            Assert.True(vm.Matrix.CoveredCellsCount < 450);

            vm.AutoBalanceStrategyCommand.Execute("FillGapsOnly");

            Assert.Equal(450, vm.Matrix.CoveredCellsCount);
            Assert.Equal(100.0, vm.Matrix.CoveragePercentage);
        }

        [Fact]
        public void SelectedProperties_TwoWayUpdates_SyncsWithSelectedEntry()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.SelectedEntry = vm.Entries[0];

            vm.SelectedName = "renamed_anim";
            Assert.Equal("renamed_anim", vm.SelectedEntry.Name);

            vm.SelectedMinLevel = 3;
            Assert.Equal(3, vm.SelectedEntry.MinLevel);

            vm.SelectedMaxLevel = 8;
            Assert.Equal(8, vm.SelectedEntry.MaxLevel);

            vm.SelectedMinButthurt = 2;
            Assert.Equal(2, vm.SelectedEntry.MinButthurt);

            vm.SelectedMaxButthurt = 11;
            Assert.Equal(11, vm.SelectedEntry.MaxButthurt);

            vm.SelectedWeight = 7;
            Assert.Equal(7, vm.SelectedEntry.Weight);
            Assert.Equal("Weight: 7", vm.SelectedEntry.WeightText);
        }

        [Fact]
        public void EntryBoundsClamping_MinMaxMutualClamping_MaintainsValidRange()
        {
            var entry = new FlipperManifestEntry { Name = "test", MinLevel = 5, MaxLevel = 15, MinButthurt = 3, MaxButthurt = 10, Weight = 1 };
            var entryVm = new FlipperScheduleEntryViewModel(entry);

            // Raising MinLevel above MaxLevel pushes MaxLevel up
            entryVm.MinLevel = 20;
            Assert.Equal(20, entryVm.MinLevel);
            Assert.Equal(20, entryVm.MaxLevel);

            // Lowering MaxLevel below MinLevel pushes MinLevel down
            entryVm.MaxLevel = 10;
            Assert.Equal(10, entryVm.MinLevel);
            Assert.Equal(10, entryVm.MaxLevel);

            // Raising MinButthurt above MaxButthurt pushes MaxButthurt up
            entryVm.MinButthurt = 12;
            Assert.Equal(12, entryVm.MinButthurt);
            Assert.Equal(12, entryVm.MaxButthurt);

            // Lowering MaxButthurt below MinButthurt pushes MinButthurt down
            entryVm.MaxButthurt = 4;
            Assert.Equal(4, entryVm.MinButthurt);
            Assert.Equal(4, entryVm.MaxButthurt);

            // Weight clamping (1..100)
            entryVm.Weight = 0;
            Assert.Equal(1, entryVm.Weight);
            entryVm.Weight = 99;
            Assert.Equal(99, entryVm.Weight);
            entryVm.Weight = 100;
            Assert.Equal(100, entryVm.Weight);
            entryVm.Weight = 150;
            Assert.Equal(100, entryVm.Weight);
        }

        [Fact]
        public void InspectCell_ClampingBoundaryValues_RestrictsToValidMatrixDimensions()
        {
            var vm = new FlipperScheduleMatrixViewModel();

            // Negative coordinates clamped to (1, 0)
            vm.InspectCell(-10, -5);
            Assert.Equal(1, vm.SelectedCellLevel);
            Assert.Equal(0, vm.SelectedCellMood);

            // Out-of-upper-bound coordinates clamped to (30, 14) in Extended mode
            vm.InspectCell(100, 50);
            Assert.Equal(30, vm.SelectedCellLevel);
            Assert.Equal(14, vm.SelectedCellMood);

            // In Stock mode, clamped to (3, 14)
            vm.IsStockMode = true;
            vm.InspectCell(10, 12);
            Assert.Equal(3, vm.SelectedCellLevel);
            Assert.Equal(12, vm.SelectedCellMood);
        }

        [Fact]
        public void HoverCell_ClampingBoundaryValues_RestrictsToValidDimensionsWithoutThrowing()
        {
            var vm = new FlipperScheduleMatrixViewModel();

            // Negative coordinates
            vm.HoverCell(-5, -2);
            Assert.Contains("Level 1", vm.CellHoverInfoText);
            Assert.Contains("Mood 0", vm.CellHoverInfoText);

            // Excess coordinates
            vm.HoverCell(50, 20);
            Assert.Contains("Level 30", vm.CellHoverInfoText);
            Assert.Contains("Mood 14", vm.CellHoverInfoText);
        }

        [Fact]
        public void ModeSwitching_ClampsInspectedCell_ToNewMaxAllowedLevel()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.InspectCell(25, 8);
            Assert.Equal(25, vm.SelectedCellLevel);

            // Switch to stock mode (Max = 3)
            vm.IsStockMode = true;
            Assert.Equal(3, vm.SelectedCellLevel);
            Assert.Equal(8, vm.SelectedCellMood);
            Assert.Contains("Adult (L3)", vm.SelectedCellSummaryText);
        }

        [Fact]
        public void EmptyPack_FallsBackToDefaultThreeEntries()
        {
            var emptyPack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>();
            var vm = new FlipperScheduleMatrixViewModel(emptyPack);

            Assert.Equal(3, vm.Entries.Count);
            Assert.NotNull(vm.SelectedEntry);
            Assert.Equal(450, vm.Matrix.CoveredCellsCount);
        }

        [Fact]
        public void DeleteEntry_WhenOnlyOneEntryLeft_DoesNotDelete()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            while (vm.Entries.Count > 1)
            {
                vm.DeleteEntryCommand.Execute(null);
            }

            Assert.Single(vm.Entries);
            var lastEntry = vm.Entries[0];

            // Attempting to delete the last remaining entry is safely ignored
            vm.DeleteEntryCommand.Execute(null);
            Assert.Single(vm.Entries);
            Assert.Same(lastEntry, vm.SelectedEntry);
        }

        [Fact]
        public void ViewModel_LargeAnimationPack100Items_HandlesRecalculationAndProbabilities()
        {
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>();
            for (int i = 0; i < 120; i++)
            {
                var entry = new FlipperManifestEntry
                {
                    Name = $"crowd_anim_{i}",
                    MinLevel = 1,
                    MaxLevel = 30,
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = (i % 10) + 1
                };
                pack.Add(($"crowd_anim_{i}", new SpriteState(128, 64), entry));
            }

            var vm = new FlipperScheduleMatrixViewModel(pack, "CrowdedPack");
            Assert.Equal(120, vm.Entries.Count);
            Assert.Equal(450, vm.Matrix.CoveredCellsCount);
            Assert.Equal(120, vm.Matrix.MaxCollidingAnimations);

            // Probability distribution for inspected cell
            vm.InspectCell(15, 7);
            Assert.Equal(120, vm.SelectedCellProbabilities.Count);
            Assert.True(vm.HasSelectedCellCoverage);
            Assert.False(vm.HasSelectedCellGap);

            // Auto-balance 120 items with MoodTiers
            vm.AutoBalanceStrategyCommand.Execute("MoodTiers");
            Assert.Equal(120, vm.Entries.Count);
            Assert.Equal(450, vm.Matrix.CoveredCellsCount);

            // Mode switch to Stock mode (L1-3)
            vm.ToggleModeCommand.Execute(null);
            Assert.True(vm.IsStockMode);
            Assert.Equal(45, vm.Matrix.CoveredCellsCount);
            Assert.All(vm.Entries, e => Assert.InRange(e.MaxLevel, 1, 3));
        }

        [Fact]
        public void RapidRecalculation_DoesNotDeadlockOrCorruptState()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            int redrawCount = 0;
            vm.MatrixRedrawRequested += (s, e) => redrawCount++;

            for (int i = 0; i < 50; i++)
            {
                vm.SelectedMinLevel = (i % 3) + 1;
                vm.SelectedMaxLevel = (i % 25) + 5;
                vm.SelectedMinButthurt = (i % 5);
                vm.SelectedMaxButthurt = (i % 10) + 5;
                vm.SelectedWeight = (i % 10) + 1;
            }

            Assert.True(redrawCount > 0);
            Assert.NotNull(vm.SelectedEntry);
            Assert.NotNull(vm.CoverageBadgeText);
            Assert.NotNull(vm.MatrixStatsText);
        }

        [Fact]
        public void SetSelectedEntryBounds_SetsAllCoordinatesAndRecalculates()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            bool redrawFired = false;
            vm.MatrixRedrawRequested += (s, e) => redrawFired = true;

            vm.SetSelectedEntryBounds(5, 12, 2, 8);

            Assert.True(redrawFired);
            Assert.Equal(5, vm.SelectedMinLevel);
            Assert.Equal(12, vm.SelectedMaxLevel);
            Assert.Equal(2, vm.SelectedMinButthurt);
            Assert.Equal(8, vm.SelectedMaxButthurt);

            // Inverted input order should be safely normalized
            vm.SetSelectedEntryBounds(20, 10, 10, 4);
            Assert.Equal(10, vm.SelectedMinLevel);
            Assert.Equal(20, vm.SelectedMaxLevel);
            Assert.Equal(4, vm.SelectedMinButthurt);
            Assert.Equal(10, vm.SelectedMaxButthurt);
        }

        private class MockTabServiceForMatrix : IWorkspaceTabService
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

        [Fact]
        public void SyncFromWorkspaceCommand_PopulatesEntriesFromOpenTabs()
        {
            var mockTabService = new MockTabServiceForMatrix();
            mockTabService.OpenSprites.Add(("idle_anim", new SpriteState(128, 64)));
            mockTabService.OpenSprites.Add(("jump_anim", new SpriteState(128, 64)));
            mockTabService.OpenSprites.Add(("sleep_anim", new SpriteState(128, 64)));

            var vm = new FlipperScheduleMatrixViewModel(tabService: mockTabService);

            // Constructor should auto-populate from open tabs
            Assert.Equal(3, vm.Entries.Count);
            Assert.Contains(vm.Entries, e => e.Name == "idle_anim");
            Assert.Contains(vm.Entries, e => e.Name == "jump_anim");
            Assert.Contains(vm.Entries, e => e.Name == "sleep_anim");

            // Adding a new tab and triggering SyncFromWorkspaceCommand
            mockTabService.OpenSprites.Add(("run_anim", new SpriteState(128, 64)));
            vm.SyncFromWorkspaceCommand.Execute(null);

            Assert.Equal(4, vm.Entries.Count);
            Assert.Contains(vm.Entries, e => e.Name == "run_anim");
            Assert.Contains("Synced tabs", vm.CellHoverInfoText);
            Assert.Contains("1 new animation(s) added", vm.CellHoverInfoText);
        }

        [Fact]
        public void SetStagePresetCommand_SetsExpectedLevelRanges()
        {
            var vm = new FlipperScheduleMatrixViewModel();

            // Extended mode (L1-30)
            vm.SetStagePresetCommand.Execute("Baby");
            Assert.Equal(1, vm.SelectedMinLevel);
            Assert.Equal(9, vm.SelectedMaxLevel);

            vm.SetStagePresetCommand.Execute("Teen");
            Assert.Equal(10, vm.SelectedMinLevel);
            Assert.Equal(19, vm.SelectedMaxLevel);

            vm.SetStagePresetCommand.Execute("Adult");
            Assert.Equal(20, vm.SelectedMinLevel);
            Assert.Equal(30, vm.SelectedMaxLevel);

            vm.SetStagePresetCommand.Execute("All");
            Assert.Equal(1, vm.SelectedMinLevel);
            Assert.Equal(30, vm.SelectedMaxLevel);

            // Stock mode (L1-3)
            vm.ToggleModeCommand.Execute(null);
            Assert.True(vm.IsStockMode);

            vm.SetStagePresetCommand.Execute("Baby");
            Assert.Equal(1, vm.SelectedMinLevel);
            Assert.Equal(1, vm.SelectedMaxLevel);

            vm.SetStagePresetCommand.Execute("Teen");
            Assert.Equal(2, vm.SelectedMinLevel);
            Assert.Equal(2, vm.SelectedMaxLevel);

            vm.SetStagePresetCommand.Execute("Adult");
            Assert.Equal(3, vm.SelectedMinLevel);
            Assert.Equal(3, vm.SelectedMaxLevel);
        }

        [Fact]
        public void SetMoodPresetCommand_SetsExpectedMoodRanges()
        {
            var vm = new FlipperScheduleMatrixViewModel();

            vm.SetMoodPresetCommand.Execute("Happy");
            Assert.Equal(0, vm.SelectedMinButthurt);
            Assert.Equal(4, vm.SelectedMaxButthurt);

            vm.SetMoodPresetCommand.Execute("Neutral");
            Assert.Equal(5, vm.SelectedMinButthurt);
            Assert.Equal(8, vm.SelectedMaxButthurt);

            vm.SetMoodPresetCommand.Execute("Angry");
            Assert.Equal(9, vm.SelectedMinButthurt);
            Assert.Equal(14, vm.SelectedMaxButthurt);

            vm.SetMoodPresetCommand.Execute("All");
            Assert.Equal(0, vm.SelectedMinButthurt);
            Assert.Equal(14, vm.SelectedMaxButthurt);
        }

        [Fact]
        public void StepCommands_IncrementAndDecrementSafely()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.SelectedMinLevel = 10;
            vm.SelectedMaxLevel = 20;
            vm.SelectedMinButthurt = 5;
            vm.SelectedMaxButthurt = 10;
            vm.SelectedWeight = 5;

            // Increment
            vm.StepMinLevelCommand.Execute(1);
            Assert.Equal(11, vm.SelectedMinLevel);

            vm.StepMaxLevelCommand.Execute(1);
            Assert.Equal(21, vm.SelectedMaxLevel);

            vm.StepMinMoodCommand.Execute(1);
            Assert.Equal(6, vm.SelectedMinButthurt);

            vm.StepMaxMoodCommand.Execute(1);
            Assert.Equal(11, vm.SelectedMaxButthurt);

            vm.StepWeightCommand.Execute(1);
            Assert.Equal(6, vm.SelectedWeight);

            // Decrement
            vm.StepMinLevelCommand.Execute(-1);
            Assert.Equal(10, vm.SelectedMinLevel);

            vm.StepMaxLevelCommand.Execute(-1);
            Assert.Equal(20, vm.SelectedMaxLevel);

            vm.StepMinMoodCommand.Execute(-1);
            Assert.Equal(5, vm.SelectedMinButthurt);

            vm.StepMaxMoodCommand.Execute(-1);
            Assert.Equal(10, vm.SelectedMaxButthurt);

            vm.StepWeightCommand.Execute(-1);
            Assert.Equal(5, vm.SelectedWeight);
        }

        [Fact]
        public void ImportManifest_MomentumManifest_WhileInStockMode_SwitchesToMomentum()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "Hexprite_Test_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            string manifestPath = Path.Combine(tempDir, "manifest.txt");
            try
            {
                string manifestContent = "Filetype: Flipper Animation Manifest\nVersion: 1\n\nName: anim1\nMin butthurt: 0\nMax butthurt: 14\nMin level: 1\nMax level: 30\nWeight: 1\n";
                File.WriteAllText(manifestPath, manifestContent);

                var dialogMock = new Mock<IDialogService>();
                dialogMock.Setup(d => d.ShowOpenFileDialog(It.IsAny<string>(), It.IsAny<string>()))
                    .Returns(manifestPath);

                var vm = new FlipperScheduleMatrixViewModel(dialogService: dialogMock.Object);
                vm.IsStockMode = true;
                Assert.Equal(3, vm.MaxAllowedLevel);

                vm.ImportManifestCommand.Execute(null);

                Assert.False(vm.IsStockMode);
                Assert.True(vm.IsMomentumMode);
                Assert.Equal(30, vm.MaxAllowedLevel);
                Assert.Single(vm.Entries);
                Assert.Equal(30, vm.Entries[0].MaxLevel);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ImportManifest_WithAnimationFolders_LoadsSpritesAndPopulatesAnimationSprites()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "Hexprite_Test_" + Guid.NewGuid());
            string animsDir = Path.Combine(tempDir, "Anims");
            Directory.CreateDirectory(animsDir);
            string manifestPath = Path.Combine(animsDir, "manifest.txt");

            try
            {
                string manifestContent = "Filetype: Flipper Animation Manifest\nVersion: 1\n\nName: test_anim\nMin butthurt: 0\nMax butthurt: 14\nMin level: 1\nMax level: 30\nWeight: 1\n";
                File.WriteAllText(manifestPath, manifestContent);

                string animFolder = Path.Combine(animsDir, "test_anim");
                Directory.CreateDirectory(animFolder);
                File.WriteAllText(Path.Combine(animFolder, "meta.txt"),
                    "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\n" +
                    "Passive frames: 1\nActive frames: 0\nFrames order: 0\n" +
                    "Active cycles: 1\nFrame rate: 5\nDuration: 3600\nActive cooldown: 0\nBubble slots: 0\n");

                byte[] frameData = new byte[128 * 64 / 8];
                frameData[0] = 0xFF; // Set some pixels on
                File.WriteAllBytes(Path.Combine(animFolder, "frame_0.bm"), frameData);

                var dialogMock = new Mock<IDialogService>();
                dialogMock.Setup(d => d.ShowOpenFileDialog(It.IsAny<string>(), It.IsAny<string>()))
                    .Returns(manifestPath);

                var vm = new FlipperScheduleMatrixViewModel(dialogService: dialogMock.Object);

                vm.ImportManifestCommand.Execute(null);

                Assert.Single(vm.Entries);
                Assert.Equal("test_anim", vm.Entries[0].Name);

                Assert.True(vm.AnimationSprites.ContainsKey("test_anim"));
                var sprite = vm.AnimationSprites["test_anim"];
                Assert.NotNull(sprite);
                Assert.Single(sprite.Frames);
                Assert.Equal(5, sprite.FrameRateFps);

                // Verify preview frame buffer is valid and non-blank
                Assert.NotNull(vm.CurrentPreviewSprite);
                Assert.Equal("test_anim", vm.SelectedEntry?.Name);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void SyncFromWorkspace_CaseInsensitiveMatching_PreservesExistingBounds()
        {
            var mockTabService = new MockTabServiceForMatrix();
            mockTabService.OpenSprites.Add(("idle_anim", new SpriteState(128, 64)));

            var existingEntry = new FlipperManifestEntry { Name = "IDLE_ANIM", MinLevel = 5, MaxLevel = 15, MinButthurt = 2, MaxButthurt = 8, Weight = 3 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("IDLE_ANIM", new SpriteState(128, 64), existingEntry)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack: pack, tabService: mockTabService);
            Assert.Single(vm.Entries);

            vm.SyncFromWorkspaceCommand.Execute(null);

            Assert.Single(vm.Entries);
            Assert.Equal("IDLE_ANIM", vm.Entries[0].Name, ignoreCase: true);
            Assert.Equal(5, vm.Entries[0].MinLevel);
            Assert.Equal(15, vm.Entries[0].MaxLevel);
            Assert.Equal(2, vm.Entries[0].MinButthurt);
            Assert.Equal(8, vm.Entries[0].MaxButthurt);
            Assert.Equal(3, vm.Entries[0].Weight);
        }

        [Fact]
        public void SyncFromWorkspace_DuplicateTabNames_DoesNotThrowAndDeduplicates()
        {
            var mockTabService = new MockTabServiceForMatrix();
            mockTabService.OpenSprites.Add(("walk", new SpriteState(128, 64)));
            mockTabService.OpenSprites.Add(("walk", new SpriteState(128, 64)));
            mockTabService.OpenSprites.Add(("WALK", new SpriteState(128, 64)));

            var vm = new FlipperScheduleMatrixViewModel(tabService: mockTabService);
            vm.SyncFromWorkspaceCommand.Execute(null);

            Assert.Single(vm.Entries);
            Assert.Equal("walk", vm.Entries[0].Name, ignoreCase: true);
        }

        [Fact]
        public void ExportManifest_WithValidationErrors_ShowsWarningAndBlocksExport()
        {
            var dialogMock = new Mock<IDialogService>();
            var vm = new FlipperScheduleMatrixViewModel(dialogService: dialogMock.Object);

            // Set empty name which is a validation error
            vm.SelectedName = "";

            vm.ExportManifestCommand.Execute(null);

            // Ensure error dialog was displayed and ShowSaveFileDialog was NEVER called
            dialogMock.Verify(d => d.ShowMessage(It.Is<string>(s => s.Contains("Cannot export")), "Validation Failed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning), Times.Once);
            dialogMock.Verify(d => d.ShowSaveFileDialog(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void UndoRedo_AddDeleteModify_RestoresPreviousState()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            int initialCount = vm.Entries.Count; // 3
            Assert.False(vm.CanUndo);
            Assert.False(vm.CanRedo);

            // 1. Add Entry
            vm.AddEntryCommand.Execute(null);
            Assert.Equal(initialCount + 1, vm.Entries.Count);
            Assert.True(vm.CanUndo);
            Assert.False(vm.CanRedo);

            // 2. Undo Add
            vm.UndoCommand.Execute(null);
            Assert.Equal(initialCount, vm.Entries.Count);
            Assert.True(vm.CanRedo);

            // 3. Redo Add
            vm.RedoCommand.Execute(null);
            Assert.Equal(initialCount + 1, vm.Entries.Count);

            // 4. Modify bounds with Undo
            vm.SetSelectedEntryBounds(5, 15, 2, 6);
            Assert.Equal(5, vm.SelectedMinLevel);
            Assert.Equal(15, vm.SelectedMaxLevel);

            vm.UndoCommand.Execute(null);
            Assert.NotEqual(5, vm.SelectedMinLevel);
        }

        [Fact]
        public void SelectNextAndPreviousEntry_CyclesThroughEntries()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.Equal(3, vm.Entries.Count);
            Assert.Same(vm.Entries[0], vm.SelectedEntry);

            vm.SelectNextEntryCommand.Execute(null);
            Assert.Same(vm.Entries[1], vm.SelectedEntry);

            vm.SelectNextEntryCommand.Execute(null);
            Assert.Same(vm.Entries[2], vm.SelectedEntry);

            // Wrap around to 0
            vm.SelectNextEntryCommand.Execute(null);
            Assert.Same(vm.Entries[0], vm.SelectedEntry);

            // Wrap backwards to 2
            vm.SelectPreviousEntryCommand.Execute(null);
            Assert.Same(vm.Entries[2], vm.SelectedEntry);
        }

        [Fact]
        public void RecalculateMatrix_ValidatesManifest_UpdatesValidationStatusAndDiagnostics()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.False(vm.HasValidationIssues);
            Assert.Contains("Valid", vm.ValidationStatusText);

            // Invalidate entry
            vm.SelectedName = "";
            Assert.True(vm.HasValidationIssues);
            Assert.Contains("Error", vm.ValidationStatusText);
            Assert.NotEmpty(vm.ValidationDiagnostics);

            // Fix entry
            vm.SelectedName = "valid_name";
            Assert.False(vm.HasValidationIssues);
            Assert.Contains("Valid", vm.ValidationStatusText);
        }

        [Fact]
        public void CopyManifest_CopiesToInjectedClipboardService_WithValidationWarningIfErrors()
        {
            var clipMock = new Mock<IClipboardService>();
            var vm = new FlipperScheduleMatrixViewModel(clipboardService: clipMock.Object);

            vm.CopyManifestCommand.Execute(null);

            clipMock.Verify(c => c.SetText(It.Is<string>(s => s.Contains("Filetype: Flipper Animation Manifest"))), Times.Once);
            Assert.Contains("copied", vm.StatusMessage);

            // If entry is invalid, still copies and sets warning status
            vm.SelectedName = "";
            vm.CopyManifestCommand.Execute(null);
            Assert.Contains("validation error", vm.StatusMessage);
        }

        [Fact]
        public void NavigateToEntryTab_ExistingTab_FocusesTabAndUpdatesStatus()
        {
            var mockTabService = new MockTabServiceForMatrix();
            mockTabService.OpenSprites.Add(("anim_baby", new SpriteState(128, 64)));

            var vm = new FlipperScheduleMatrixViewModel(tabService: mockTabService);
            Assert.NotNull(vm.SelectedEntry);

            vm.NavigateToEntryTabCommand.Execute(vm.SelectedEntry);

            Assert.Equal("anim_baby", mockTabService.LastActivatedTab);
            Assert.Contains("Focused workspace tab", vm.StatusMessage);
        }

        [Fact]
        public void NavigateToEntryTab_NonExistingTab_CreatesAndOpens128x64SpriteTab()
        {
            var mockTabService = new MockTabServiceForMatrix();
            var vm = new FlipperScheduleMatrixViewModel(tabService: mockTabService);

            var entry = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "unknown_sprite" });
            vm.NavigateToEntryTabCommand.Execute(entry);

            Assert.Equal("unknown_sprite", mockTabService.LastOpenedTab);
            Assert.Contains("Opened canvas tab", vm.StatusMessage);
            Assert.True(vm.AnimationSprites.ContainsKey("unknown_sprite"));
            Assert.Equal(128, vm.AnimationSprites["unknown_sprite"].Width);
            Assert.Equal(64, vm.AnimationSprites["unknown_sprite"].Height);
            Assert.Single(vm.AnimationSprites["unknown_sprite"].Frames);
        }

        [Fact]
        public void InspectClickedCell_And_SetBoundsToClickedCell_WorkCorrectly()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.InspectCell(15, 8);

            Assert.Equal(15, vm.SelectedCellLevel);
            Assert.Equal(8, vm.SelectedCellMood);

            vm.InspectClickedCellCommand.Execute(null);
            Assert.Equal(15, vm.SelectedCellLevel);
            Assert.Equal(8, vm.SelectedCellMood);

            vm.SetBoundsToClickedCellCommand.Execute(null);
            Assert.Equal(15, vm.SelectedMinLevel);
            Assert.Equal(15, vm.SelectedMaxLevel);
            Assert.Equal(8, vm.SelectedMinButthurt);
            Assert.Equal(8, vm.SelectedMaxButthurt);
        }

        [Fact]
        public void EntryCountBadgeText_ReflectsCountAndPluralization()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.Equal($"{vm.Entries.Count} Animations", vm.EntryCountBadgeText);

            while (vm.Entries.Count > 1)
            {
                vm.DeleteEntry();
            }

            Assert.Equal("1 Animation", vm.EntryCountBadgeText);
        }

        [Fact]
        public void FlipperScheduleEntryViewModel_CoverageAndColor_CalculatedCorrectly()
        {
            var entry = new FlipperManifestEntry
            {
                Name = "test_anim",
                MinLevel = 1,
                MaxLevel = 5,
                MinButthurt = 0,
                MaxButthurt = 2
            };

            var vm = new FlipperScheduleEntryViewModel(entry);

            // CoverageCells = (5 - 1 + 1) * (2 - 0 + 1) = 5 * 3 = 15
            Assert.Equal(15, vm.CoverageCells);
            Assert.Equal("15 cells", vm.CoverageText);
            Assert.Equal("#00E5FF", vm.EntryColorHex); // Baby tier

            // Change to Adult range
            vm.MinLevel = 25;
            vm.MaxLevel = 30;
            Assert.Equal("#FFD600", vm.EntryColorHex); // Adult Gold

            // Change to Teen range
            vm.MinLevel = 12;
            vm.MaxLevel = 18;
            Assert.Equal("#E040FB", vm.EntryColorHex); // Teen Magenta
        }

        [Fact]
        public void OpenSimulator_WithDuplicateAndDirtyTabTitles_DoesNotThrow()
        {
            var mockTabService = new MockTabServiceForMatrix();
            mockTabService.OpenSprites.Add(("*Untitled", new SpriteState(128, 64)));
            mockTabService.OpenSprites.Add(("*Untitled", new SpriteState(128, 64)));
            mockTabService.OpenSprites.Add(("Untitled", new SpriteState(128, 64)));

            var mockWinManager = new Mock<IFlipperWindowManager>();

            var vm = new FlipperScheduleMatrixViewModel(
                windowManager: mockWinManager.Object,
                tabService: mockTabService);

            // Should not throw ArgumentException duplicate key
            var exception = Record.Exception(() => vm.OpenSimulator());
            Assert.Null(exception);

            Assert.Equal(AssetPackViewMode.DeviceSimulator, vm.CurrentViewMode);
            Assert.True(vm.IsDeviceSimulatorView);
            Assert.NotNull(vm.SimulatorViewModel);
            Assert.NotEmpty(vm.SimulatorViewModel.Animations);
        }

        [Fact]
        public void PreviewPlayback_StepsFramesAndUpdatesFrameCountText()
        {
            var mockTabService = new MockTabServiceForMatrix();
            var multiFrameSprite = new SpriteState(128, 64);
            multiFrameSprite.Frames.Add(new FrameState { Name = "Frame 1" });
            multiFrameSprite.Frames.Add(new FrameState { Name = "Frame 2" });
            multiFrameSprite.Frames.Add(new FrameState { Name = "Frame 3" });
            mockTabService.OpenSprites.Add(("anim_baby", multiFrameSprite));

            var vm = new FlipperScheduleMatrixViewModel(tabService: mockTabService);
            Assert.Equal("Frame 1 / 4", vm.PreviewFrameCountText);
            Assert.Equal("128 × 64", vm.PreviewDimensionsText);

            vm.PreviewNextFrameCommand.Execute(null);
            Assert.Equal(1, vm.PreviewFrameIndex);
            Assert.Equal("Frame 2 / 4", vm.PreviewFrameCountText);

            vm.PreviewNextFrameCommand.Execute(null);
            Assert.Equal(2, vm.PreviewFrameIndex);
            Assert.Equal("Frame 3 / 4", vm.PreviewFrameCountText);

            vm.PreviewPrevFrameCommand.Execute(null);
            Assert.Equal(1, vm.PreviewFrameIndex);
            Assert.Equal("Frame 2 / 4", vm.PreviewFrameCountText);
        }

        [Fact]
        public void TogglePreviewPlay_TogglesIsPreviewPlaying()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.False(vm.IsPreviewPlaying);
            Assert.Equal("▶ Play", vm.PreviewPlayButtonText);

            vm.TogglePreviewPlayCommand.Execute(null);
            Assert.True(vm.IsPreviewPlaying);
            Assert.Equal("⏸ Pause", vm.PreviewPlayButtonText);

            vm.TogglePreviewPlayCommand.Execute(null);
            Assert.False(vm.IsPreviewPlaying);
            Assert.Equal("▶ Play", vm.PreviewPlayButtonText);
        }

        [Fact]
        public void AddActiveCanvasTab_AddsActiveSpriteToSchedule()
        {
            var mockTabService = new MockTabServiceForMatrix();
            mockTabService.OpenSprites.Add(("anim_baby", new SpriteState(128, 64)));

            var vm = new FlipperScheduleMatrixViewModel(tabService: mockTabService);
            int initialCount = vm.Entries.Count;

            // Now a new sprite is drawn on the active canvas
            var activeSprite = new SpriteState(128, 64);
            mockTabService.ActiveSprite = activeSprite;
            mockTabService.ActiveSpriteTitle = "my_dolphin_walk";
            mockTabService.OpenSprites.Add(("my_dolphin_walk", activeSprite));

            vm.AddActiveCanvasTabCommand.Execute(null);

            Assert.Equal(initialCount + 1, vm.Entries.Count);
            Assert.Contains(vm.Entries, e => e.Name == "my_dolphin_walk");
            Assert.Equal("my_dolphin_walk", vm.SelectedName);
            Assert.Contains("Added active canvas tab", vm.StatusMessage);

            // Re-adding existing selects it without duplicate
            vm.AddActiveCanvasTabCommand.Execute(null);
            Assert.Equal(initialCount + 1, vm.Entries.Count);
            Assert.Contains("already in schedule", vm.StatusMessage);
        }

        [Fact]
        public void CreateTabForGap_CreatesTabAndAddsCoveringEntry()
        {
            var mockTabService = new MockTabServiceForMatrix();
            var vm = new FlipperScheduleMatrixViewModel(tabService: mockTabService);

            // Set matrix to have a gap at level 15, mood 8
            vm.InspectCell(15, 8);
            int initialCount = vm.Entries.Count;

            vm.CreateTabForGapCommand.Execute(null);

            Assert.Equal(initialCount + 1, vm.Entries.Count);
            Assert.Equal("anim_L15_M8", mockTabService.LastOpenedTab);
            Assert.NotNull(vm.SelectedEntry);
            Assert.Equal("anim_L15_M8", vm.SelectedEntry.Name);
            Assert.Equal(15, vm.SelectedEntry.MinLevel);
            Assert.Equal(15, vm.SelectedEntry.MaxLevel);
            Assert.Equal(8, vm.SelectedEntry.MinButthurt);
            Assert.Equal(8, vm.SelectedEntry.MaxButthurt);
            Assert.Contains("Created canvas tab", vm.StatusMessage);
        }

        [Fact]
        public void OpenSelectedInCanvas_ActivatesOrOpensTab()
        {
            var mockTabService = new MockTabServiceForMatrix();
            mockTabService.OpenSprites.Add(("anim_baby", new SpriteState(128, 64)));

            var vm = new FlipperScheduleMatrixViewModel(tabService: mockTabService);
            Assert.NotNull(vm.SelectedEntry);

            vm.OpenSelectedInCanvasCommand.Execute(null);

            Assert.Equal("anim_baby", mockTabService.LastActivatedTab);
            Assert.Contains("Focused workspace tab", vm.StatusMessage);
        }

        [Fact]
        public void Dispose_StopsPreviewTimerWithoutThrowing()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var exception = Record.Exception(() => vm.Dispose());
            Assert.Null(exception);
        }

        [Fact]
        public void SyncFromWorkspace_NonDestructive_PreservesNonTabEntries()
        {
            var mockTabService = new MockTabServiceForMatrix();
            // User opened matrix with custom pack/file entries
            var vm = new FlipperScheduleMatrixViewModel(tabService: mockTabService);
            Assert.True(vm.Entries.Count >= 1);
            int initialCount = vm.Entries.Count;
            string existingName = vm.Entries[0].Name;

            // Only 1 tab is open in Hexprite workspace
            mockTabService.OpenSprites.Add(("brand_new_tab", new SpriteState(128, 64)));

            vm.SyncFromWorkspaceCommand.Execute(null);

            // Both the existing entry and the new tab are preserved
            Assert.Equal(initialCount + 1, vm.Entries.Count);
            Assert.Contains(vm.Entries, e => e.Name == existingName);
            Assert.Contains(vm.Entries, e => e.Name == "brand_new_tab");
        }

        [Fact]
        public void AddEntry_GeneratesGuaranteedUniqueNames_EvenAfterDeletions()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.Entries.Clear();

            // Add anim_1, anim_2, anim_3
            vm.AddEntryCommand.Execute(null);
            Assert.Equal("anim_1", vm.SelectedName);

            vm.AddEntryCommand.Execute(null);
            Assert.Equal("anim_2", vm.SelectedName);

            vm.AddEntryCommand.Execute(null);
            Assert.Equal("anim_3", vm.SelectedName);

            // Delete anim_2
            var anim2 = vm.Entries.First(e => e.Name == "anim_2");
            vm.SelectedEntry = anim2;
            vm.DeleteEntryCommand.Execute(null);
            Assert.Equal(2, vm.Entries.Count);
            Assert.DoesNotContain(vm.Entries, e => e.Name == "anim_2");

            // Adding a new entry should fill the available unique name anim_2
            vm.AddEntryCommand.Execute(null);
            Assert.Equal("anim_2", vm.SelectedName);
            Assert.Equal(3, vm.Entries.Count);

            // Adding another should generate anim_4
            vm.AddEntryCommand.Execute(null);
            Assert.Equal("anim_4", vm.SelectedName);
            Assert.Equal(4, vm.Entries.Count);
        }

        [Fact]
        public void AddEntry_WhenInspectingGap_UsesInspectedCellBounds()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.Entries.Clear();
            vm.RecalculateMatrix();
            vm.InspectCell(18, 5);
            Assert.True(vm.HasSelectedCellGap);

            vm.AddEntryCommand.Execute(null);

            Assert.NotNull(vm.SelectedEntry);
            Assert.Equal(18, vm.SelectedEntry.MinLevel);
            Assert.Equal(18, vm.SelectedEntry.MaxLevel);
            Assert.Equal(5, vm.SelectedEntry.MinButthurt);
            Assert.Equal(5, vm.SelectedEntry.MaxButthurt);
        }

        [Fact]
        public void EntryViewModel_PropertySetters_NotifyCoverageAndColorHex()
        {
            var entry = new FlipperManifestEntry
            {
                Name = "test",
                MinLevel = 1,
                MaxLevel = 5,
                MinButthurt = 0,
                MaxButthurt = 4,
                Weight = 1
            };
            var vm = new FlipperScheduleEntryViewModel(entry);

            var notifiedProperties = new List<string>();
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != null) notifiedProperties.Add(e.PropertyName);
            };

            vm.MinLevel = 15;
            Assert.Contains(nameof(vm.CoverageCells), notifiedProperties);
            Assert.Contains(nameof(vm.CoverageText), notifiedProperties);
            Assert.Contains(nameof(vm.EntryColorHex), notifiedProperties);
            Assert.Equal("#E040FB", vm.EntryColorHex); // Teen Magenta

            notifiedProperties.Clear();
            vm.MinLevel = 25;
            vm.MaxLevel = 30;
            Assert.Contains(nameof(vm.EntryColorHex), notifiedProperties);
            Assert.Equal("#FFD600", vm.EntryColorHex); // Adult Gold
        }

        [Fact]
        public void OpenSelectedInCanvas_WhenTabNotOpen_Creates128x64TabWithCorrectNameAndFrames()
        {
            var mockTabService = new MockTabServiceForMatrix();
            var vm = new FlipperScheduleMatrixViewModel(tabService: mockTabService);
            Assert.NotNull(vm.SelectedEntry);
            string selectedName = vm.SelectedEntry.Name;

            vm.OpenSelectedInCanvasCommand.Execute(null);

            Assert.Equal(selectedName, mockTabService.LastOpenedTab);
            Assert.True(vm.AnimationSprites.ContainsKey(selectedName));
            var sprite = vm.AnimationSprites[selectedName];
            Assert.Equal(128, sprite.Width);
            Assert.Equal(64, sprite.Height);
            Assert.Single(sprite.Frames);
        }

        [Fact]
        public void CreateTabForGap_OpensTabAndPreservesSpriteInCache()
        {
            var mockTabService = new MockTabServiceForMatrix();
            var vm = new FlipperScheduleMatrixViewModel(tabService: mockTabService);
            vm.Entries.Clear();
            vm.RecalculateMatrix();

            vm.InspectCell(15, 8);
            Assert.True(vm.HasSelectedCellGap);

            vm.CreateTabForGapCommand.Execute(null);

            Assert.NotNull(mockTabService.LastOpenedTab);
            Assert.StartsWith("anim_L15_M8", mockTabService.LastOpenedTab);
            Assert.True(vm.AnimationSprites.ContainsKey(mockTabService.LastOpenedTab));
            var sprite = vm.AnimationSprites[mockTabService.LastOpenedTab];
            Assert.Equal(128, sprite.Width);
            Assert.Equal(64, sprite.Height);
            Assert.Single(sprite.Frames);
            Assert.False(vm.HasSelectedCellGap);
        }

        [Fact]
        public void RefreshCurrentPreviewSprite_WhenCanvasTabEdited_DisplaysUpdatedPixels()
        {
            var mockTabService = new MockTabServiceForMatrix();
            var editedSprite = new SpriteState(128, 64);
            editedSprite.Frames[0].LayerPixels[0].GetMonochromeData()[0] = true;

            mockTabService.OpenSprites.Add(("anim_baby", editedSprite));

            var vm = new FlipperScheduleMatrixViewModel(tabService: mockTabService);
            Assert.NotNull(vm.SelectedEntry);
            Assert.Equal("anim_baby", vm.SelectedEntry.Name);

            vm.RefreshCurrentPreviewSprite();

            Assert.True(vm.AnimationSprites.ContainsKey("anim_baby"));
            Assert.Same(editedSprite, vm.AnimationSprites["anim_baby"]);
            Assert.Equal("Frame 1 / 1", vm.PreviewFrameCountText);
        }

        [Fact]
        public void RenamingEntry_UpdatesCacheKeyAndOpenTabTitle()
        {
            var mockTabService = new MockTabServiceForMatrix();
            var sprite = new SpriteState(128, 64);
            mockTabService.OpenSprites.Add(("anim_baby", sprite));

            var vm = new FlipperScheduleMatrixViewModel(tabService: mockTabService);
            Assert.True(vm.AnimationSprites.ContainsKey("anim_baby"));

            vm.SelectedName = "anim_toddler";

            Assert.False(vm.AnimationSprites.ContainsKey("anim_baby"));
            Assert.True(vm.AnimationSprites.ContainsKey("anim_toddler"));
            Assert.Equal("anim_baby", mockTabService.LastRenamedOldTitle);
            Assert.Equal("anim_toddler", mockTabService.LastRenamedNewTitle);
        }

        [Fact]
        public void PreviewPlayback_WithFlipperCycleFramesOrder_PlaysCorrectSequence()
        {
            var sprite = new SpriteState(128, 64);
            sprite.Frames.Clear();
            sprite.Frames.Add(new FrameState { Name = "F0" });
            sprite.Frames.Add(new FrameState { Name = "F1" });
            sprite.Frames.Add(new FrameState { Name = "F2" });
            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                FramesOrder = [0, 2, 1, 0, 2],
                PassiveFrameCount = 3,
                ActiveFrameCount = 2
            };

            var entry = new FlipperManifestEntry { Name = "custom_cycle", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("custom_cycle", sprite, entry)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);
            Assert.Equal("Frame 1 / 5", vm.PreviewFrameCountText);

            vm.PreviewNextFrameCommand.Execute(null);
            Assert.Equal("Frame 2 / 5", vm.PreviewFrameCountText);
            Assert.Equal(1, vm.PreviewFrameIndex);

            vm.PreviewNextFrameCommand.Execute(null);
            Assert.Equal("Frame 3 / 5", vm.PreviewFrameCountText);
            Assert.Equal(2, vm.PreviewFrameIndex);

            vm.PreviewPrevFrameCommand.Execute(null);
            Assert.Equal("Frame 2 / 5", vm.PreviewFrameCountText);
            Assert.Equal(1, vm.PreviewFrameIndex);
        }

        [Fact]
        public void NavigateToEntryByName_SelectsAndNavigates()
        {
            var mockTabService = new MockTabServiceForMatrix();
            var vm = new FlipperScheduleMatrixViewModel(tabService: mockTabService);

            vm.NavigateToEntryByNameCommand.Execute("anim_adult");

            Assert.NotNull(vm.SelectedEntry);
            Assert.Equal("anim_adult", vm.SelectedEntry.Name);
            Assert.Equal("anim_adult", mockTabService.LastOpenedTab);
            Assert.Contains("Opened canvas tab", vm.StatusMessage);
        }

        [Fact]
        public void AnimationSprites_PreservesSpriteStateAcrossTabLifecycle()
        {
            var mockTabService = new MockTabServiceForMatrix();
            var vm = new FlipperScheduleMatrixViewModel(tabService: mockTabService);

            // Open tab for anim_teen
            var teenEntry = vm.Entries.First(e => e.Name == "anim_teen");
            vm.NavigateToEntryTab(teenEntry);

            Assert.True(vm.AnimationSprites.ContainsKey("anim_teen"));
            var cachedSprite = vm.AnimationSprites["anim_teen"];

            // Add a second frame to the cached sprite as if edited on canvas
            cachedSprite.Frames.Add(new FrameState { Name = "Frame 2" });

            // Simulate tab close by clearing OpenSprites
            mockTabService.OpenSprites.Clear();

            // Refresh preview
            vm.SelectedEntry = teenEntry;
            vm.RefreshCurrentPreviewSprite();

            Assert.Equal("Frame 1 / 2", vm.PreviewFrameCountText);
            Assert.Equal(2, vm.AnimationSprites["anim_teen"].Frames.Count);
        }

        [Fact]
        public void ExportAssetPackDirectoryCommand_CallsExportServiceWithResolvedSprites()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "Hexprite_ExportDirTest_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            try
            {
                var dialogMock = new Mock<IDialogService>();
                dialogMock.Setup(d => d.ShowOpenFolderDialog(It.IsAny<string>()))
                    .Returns(tempDir);

                var exportMock = new Mock<IFlipperExportService>();

                var vm = new FlipperScheduleMatrixViewModel(
                    dialogService: dialogMock.Object,
                    exportService: exportMock.Object);

                vm.ExportAssetPackDirectoryCommand.Execute(null);

                dialogMock.Verify(d => d.ShowOpenFolderDialog(It.IsAny<string>()), Times.Once);
                exportMock.Verify(e => e.ExportAssetPack(
                    It.Is<IReadOnlyList<(SpriteState Sprite, FlipperManifestEntry ManifestEntry, FlipperExportSettings Settings)>>(list => list.Count == 3),
                    tempDir,
                    true), Times.Once);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ExportAssetPackZipCommand_CallsExportServiceWithResolvedSprites()
        {
            string tempZip = Path.Combine(Path.GetTempPath(), "Hexprite_ExportZipTest_" + Guid.NewGuid() + ".zip");

            var dialogMock = new Mock<IDialogService>();
            dialogMock.Setup(d => d.ShowSaveFileDialog(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(tempZip);

            var exportMock = new Mock<IFlipperExportService>();

            var vm = new FlipperScheduleMatrixViewModel(
                dialogService: dialogMock.Object,
                exportService: exportMock.Object);

            vm.ExportAssetPackZipCommand.Execute(null);

            dialogMock.Verify(d => d.ShowSaveFileDialog(It.IsAny<string>(), It.IsAny<string>(), "zip"), Times.Once);
            exportMock.Verify(e => e.ExportAssetPackZip(
                It.Is<IReadOnlyList<(SpriteState Sprite, FlipperManifestEntry ManifestEntry, FlipperExportSettings Settings)>>(list => list.Count == 3),
                tempZip,
                true), Times.Once);
        }

        [Fact]
        public void ExportAssetPackFolder_WithExplicitTargetFolder_ExportsSuccessfully()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "Hexprite_ExplicitDirTest_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            try
            {
                var dialogMock = new Mock<IDialogService>();
                var exportService = new FlipperExportService();

                var vm = new FlipperScheduleMatrixViewModel(
                    dialogService: dialogMock.Object,
                    exportService: exportService);

                vm.ExportAssetPackFolder(tempDir);

                Assert.True(File.Exists(Path.Combine(tempDir, "Anims", "manifest.txt")));
                Assert.True(File.Exists(Path.Combine(tempDir, "Anims", "anim_baby", "meta.txt")));
                Assert.True(File.Exists(Path.Combine(tempDir, "Icons", "I_anim_baby_10x10.bm")));
                dialogMock.Verify(d => d.ShowMessage(It.Is<string>(s => s.Contains("exported successfully")), "Export Success", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information), Times.Once);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ExportAssetPackZip_WithExplicitTargetZip_ExportsSuccessfully()
        {
            string tempZip = Path.Combine(Path.GetTempPath(), "Hexprite_ExplicitZipTest_" + Guid.NewGuid() + ".zip");

            try
            {
                var dialogMock = new Mock<IDialogService>();
                var exportService = new FlipperExportService();

                var vm = new FlipperScheduleMatrixViewModel(
                    dialogService: dialogMock.Object,
                    exportService: exportService);

                vm.ExportAssetPackZip(tempZip);

                Assert.True(File.Exists(tempZip));

                string extractDir = Path.Combine(Path.GetTempPath(), "Hexprite_ExplicitZipExtracted_" + Guid.NewGuid());
                System.IO.Compression.ZipFile.ExtractToDirectory(tempZip, extractDir);

                Assert.True(File.Exists(Path.Combine(extractDir, "Anims", "manifest.txt")));
                Assert.True(File.Exists(Path.Combine(extractDir, "Anims", "anim_baby", "meta.txt")));

                if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            }
            finally
            {
                if (File.Exists(tempZip)) File.Delete(tempZip);
            }
        }

        [Fact]
        public void ExportAssetPackFolder_WithValidationErrors_ShowsErrorDialogAndDoesNotThrow()
        {
            var dialogMock = new Mock<IDialogService>();
            var vm = new FlipperScheduleMatrixViewModel(dialogService: dialogMock.Object);

            // Set empty name on selected entry to produce validation error
            vm.SelectedName = "";

            vm.ExportAssetPackFolder("dummy_folder");

            dialogMock.Verify(d => d.ShowMessage(
                It.Is<string>(s => s.Contains("Failed to export asset pack")),
                "Export Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error), Times.Once);
        }

        [Fact]
        public void ExportAssetPackZip_WithValidationErrors_ShowsErrorDialogAndDoesNotThrow()
        {
            var dialogMock = new Mock<IDialogService>();
            var vm = new FlipperScheduleMatrixViewModel(dialogService: dialogMock.Object);

            // Set empty name on selected entry to produce validation error
            vm.SelectedName = "";

            vm.ExportAssetPackZip("dummy_file.zip");

            dialogMock.Verify(d => d.ShowMessage(
                It.Is<string>(s => s.Contains("Failed to export asset pack archive")),
                "Export Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error), Times.Once);
        }

        [Fact]
        public void SearchFilter_FiltersEntriesByNameAndDetails()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.Equal(3, vm.FilteredEntries.Count);

            vm.SearchFilterText = "baby";
            Assert.Single(vm.FilteredEntries);
            Assert.Equal("anim_baby", vm.FilteredEntries[0].Name);

            vm.ResetFiltersCommand.Execute(null);
            Assert.Equal(3, vm.FilteredEntries.Count);
            Assert.Empty(vm.SearchFilterText);
        }

        [Fact]
        public void StageAndMoodFilters_FilterEntriesAccurately()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.Contains("Baby", vm.StageFilters);
            Assert.Contains("Happy", vm.MoodFilters);

            // Configure standard non-overlapping stages
            vm.Entries[0].MinLevel = 1;  vm.Entries[0].MaxLevel = 9;
            vm.Entries[1].MinLevel = 10; vm.Entries[1].MaxLevel = 19;
            vm.Entries[2].MinLevel = 20; vm.Entries[2].MaxLevel = 30;

            vm.SelectedStageFilter = "Adult";
            Assert.Single(vm.FilteredEntries);
            Assert.Equal("anim_adult", vm.FilteredEntries[0].Name);

            vm.ResetFiltersCommand.Execute(null);
            Assert.Equal(3, vm.FilteredEntries.Count);
            Assert.Equal("All", vm.SelectedStageFilter);
        }

        [Fact]
        public void ActionableDiagnostics_ProducesQuickFixesAndResolvesErrors()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            // Introduce duplicate name error
            vm.Entries[1].Name = vm.Entries[0].Name;
            vm.RecalculateMatrix();

            Assert.True(vm.HasActionableDiagnostics);
            Assert.NotEmpty(vm.ActionableDiagnostics);

            var duplicateDiag = vm.ActionableDiagnostics.FirstOrDefault(d => d.Code == "FZ011");
            Assert.NotNull(duplicateDiag);
            Assert.True(duplicateDiag.HasQuickFix);

            // Execute QuickFix on duplicate
            duplicateDiag.QuickFixCommand.Execute(null);

            // Verify unique names restored
            Assert.NotEqual(vm.Entries[0].Name, vm.Entries[1].Name);
        }

        [Fact]
        public void FixAllDiagnostics_ResolvesAllErrorsAndGaps()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            // Introduce out of bounds and duplicate error
            vm.Entries[0].MinLevel = 50;
            vm.Entries[1].Name = vm.Entries[2].Name;
            vm.RecalculateMatrix();

            Assert.True(vm.HasActionableDiagnostics);
            vm.FixAllDiagnosticsCommand.Execute(null);

            // Should have valid entries and no validation issues
            Assert.True(vm.Entries.All(e => e.MinLevel >= 1 && e.MinLevel <= 30));
        }

        [Fact]
        public void PreviewTransportAndScrubber_ControlsOperateCorrectly()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.NotNull(vm.PreviewFpsBadgeText);
            Assert.NotNull(vm.PreviewDimensionsText);

            // Test speed cycle
            double initialSpeed = vm.PreviewSpeedMultiplier;
            vm.CyclePreviewSpeedCommand.Execute(null);
            Assert.NotEqual(initialSpeed, vm.PreviewSpeedMultiplier);

            // Test loop toggle
            bool initialLoop = vm.IsPreviewLooping;
            vm.TogglePreviewLoopCommand.Execute(null);
            Assert.NotEqual(initialLoop, vm.IsPreviewLooping);
        }

        [Fact]
        public void CompositeUndoState_PreservesModeAndPackNameAcrossUndoRedo()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            string originalPackName = vm.PackName;
            bool originalMode = vm.IsStockMode;

            // Make edits
            vm.PushUndoState();
            vm.PackName = "Modified Pack Name";
            vm.ToggleModeCommand.Execute(null);

            Assert.NotEqual(originalMode, vm.IsStockMode);

            // Undo
            vm.Undo();
            Assert.Equal(originalMode, vm.IsStockMode);

            // Redo
            vm.Redo();
            Assert.NotEqual(originalMode, vm.IsStockMode);
        }

        #region Milestone 3 (R3) Comprehensive Feature Tests

        [Fact]
        public void F3_1_PreviewScrubber_CurrentPreviewFrameIndex_ClampsAndRendersCorrectly()
        {
            var sprite = new SpriteState(128, 64) { FrameRateFps = 15 };
            sprite.Frames.Clear();
            sprite.Frames.Add(new FrameState { Name = "Frame 1" });
            sprite.Frames.Add(new FrameState { Name = "Frame 2" });
            sprite.Frames.Add(new FrameState { Name = "Frame 3" });
            sprite.Frames.Add(new FrameState { Name = "Frame 4" });

            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("scrub_anim", sprite, new FlipperManifestEntry { Name = "scrub_anim", MinLevel = 1, MaxLevel = 30, Weight = 1 })
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);

            Assert.Equal(4, vm.PreviewTotalFrames);
            Assert.Equal(3, vm.PreviewMaxFrameIndex);
            Assert.True(vm.PreviewHasMultipleFrames);
            Assert.Equal(15, vm.PreviewFps);
            Assert.Equal("FPS: 15", vm.PreviewFpsBadgeText);
            Assert.Equal("128 × 64", vm.PreviewDimensionsText);
            Assert.Equal("Frame 1 / 4", vm.PreviewFrameCountText);

            // Direct two-way scrubber update
            vm.CurrentPreviewFrameIndex = 2;
            Assert.Equal(2, vm.PreviewFrameIndex);
            Assert.Equal(2, vm.CurrentPreviewFrameIndex);
            Assert.Equal("Frame 3 / 4", vm.PreviewFrameCountText);

            // Out-of-bounds clamping
            vm.CurrentPreviewFrameIndex = 100;
            Assert.Equal(3, vm.CurrentPreviewFrameIndex);
            Assert.Equal("Frame 4 / 4", vm.PreviewFrameCountText);

            vm.CurrentPreviewFrameIndex = -10;
            Assert.Equal(0, vm.CurrentPreviewFrameIndex);
            Assert.Equal("Frame 1 / 4", vm.PreviewFrameCountText);
        }

        [Fact]
        public void F3_1_PreviewPlayback_LoopingAndNonLoopingNavigation()
        {
            var sprite = new SpriteState(128, 64);
            sprite.Frames.Clear();
            sprite.Frames.Add(new FrameState { Name = "F1" });
            sprite.Frames.Add(new FrameState { Name = "F2" });
            sprite.Frames.Add(new FrameState { Name = "F3" });

            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("step_anim", sprite, new FlipperManifestEntry { Name = "step_anim", MinLevel = 1, MaxLevel = 30, Weight = 1 })
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);
            vm.IsPreviewLooping = true;
            Assert.Equal("🔁 Loop: On", vm.PreviewLoopButtonText);

            // Forward step looping
            vm.CurrentPreviewFrameIndex = 2;
            vm.PreviewNextFrameCommand.Execute(null);
            Assert.Equal(0, vm.CurrentPreviewFrameIndex); // Wraps to 0

            // Backward step looping
            vm.PreviewPrevFrameCommand.Execute(null);
            Assert.Equal(2, vm.CurrentPreviewFrameIndex); // Wraps to 2

            // Non-looping behavior
            vm.IsPreviewLooping = false;
            Assert.Equal("➡️ Loop: Off", vm.PreviewLoopButtonText);

            vm.CurrentPreviewFrameIndex = 1;
            vm.TogglePreviewPlay();

            vm.PreviewNextFrame();
            Assert.Equal(2, vm.CurrentPreviewFrameIndex);
            Assert.True(vm.IsPreviewPlaying);

            vm.PreviewNextFrame();
            Assert.Equal(2, vm.CurrentPreviewFrameIndex); // Stays at last frame
            Assert.False(vm.IsPreviewPlaying); // Stops playing when loop is off

            vm.CurrentPreviewFrameIndex = 0;
            vm.PreviewPrevFrameCommand.Execute(null);
            Assert.Equal(0, vm.CurrentPreviewFrameIndex); // Clamped at 0
        }

        [Fact]
        public void F3_1_PreviewSpeedControls_AcceptsNumericParametersAndCycles()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.Equal(1.0, vm.PreviewSpeedMultiplier);
            Assert.Equal("1x", vm.PreviewSpeedText);

            // Set speed with various types
            vm.SetPreviewSpeed(1.5);
            Assert.Equal(1.5, vm.PreviewSpeedMultiplier);
            Assert.Equal("1.5x", vm.PreviewSpeedText);

            vm.SetPreviewSpeed("2.0");
            Assert.Equal(2.0, vm.PreviewSpeedMultiplier);

            vm.SetPreviewSpeed(0.5f);
            Assert.Equal(0.5, vm.PreviewSpeedMultiplier);

            // Cycle speed sequence: 0.5x -> 1.0x -> 1.5x -> 2.0x -> 0.5x
            vm.PreviewSpeedMultiplier = 0.5;
            vm.CyclePreviewSpeedCommand.Execute(null);
            Assert.Equal(1.0, vm.PreviewSpeedMultiplier);
            vm.CyclePreviewSpeedCommand.Execute(null);
            Assert.Equal(1.5, vm.PreviewSpeedMultiplier);
            vm.CyclePreviewSpeedCommand.Execute(null);
            Assert.Equal(2.0, vm.PreviewSpeedMultiplier);
            vm.CyclePreviewSpeedCommand.Execute(null);
            Assert.Equal(0.5, vm.PreviewSpeedMultiplier);
        }

        [Fact]
        public void F3_2_ManifestSearchAndFiltering_CombinedCriteria_FiltersAccurately()
        {
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("baby_happy", new SpriteState(128, 64), new FlipperManifestEntry { Name = "baby_happy", MinLevel = 1, MaxLevel = 5, MinButthurt = 0, MaxButthurt = 2, Weight = 1 }),
                ("baby_angry", new SpriteState(128, 64), new FlipperManifestEntry { Name = "baby_angry", MinLevel = 1, MaxLevel = 5, MinButthurt = 10, MaxButthurt = 14, Weight = 1 }),
                ("teen_happy", new SpriteState(128, 64), new FlipperManifestEntry { Name = "teen_happy", MinLevel = 12, MaxLevel = 18, MinButthurt = 1, MaxButthurt = 3, Weight = 1 }),
                ("adult_angry", new SpriteState(128, 64), new FlipperManifestEntry { Name = "adult_angry", MinLevel = 22, MaxLevel = 28, MinButthurt = 11, MaxButthurt = 14, Weight = 1 }),
                ("spanning_all", new SpriteState(128, 64), new FlipperManifestEntry { Name = "spanning_all", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 })
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);
            Assert.Equal(5, vm.Entries.Count);
            Assert.Equal(5, vm.FilteredEntries.Count);
            Assert.Equal("5 Animations", vm.FilterCountText);
            Assert.False(vm.HasActiveFilters);

            // 1. Filter by Name Substring
            vm.SearchFilterText = "angry";
            Assert.Equal(2, vm.FilteredEntries.Count);
            Assert.True(vm.HasActiveFilters);
            Assert.Equal("2 of 5 Animations", vm.FilterCountText);
            Assert.Contains(vm.FilteredEntries, e => e.Name == "baby_angry");
            Assert.Contains(vm.FilteredEntries, e => e.Name == "adult_angry");

            // 2. Filter by Details substring (Level/Mood details)
            vm.SearchFilterText = "L12-18";
            Assert.Single(vm.FilteredEntries);
            Assert.Equal("teen_happy", vm.FilteredEntries[0].Name);

            // 3. Filter by Stage
            vm.SearchFilterText = string.Empty;
            vm.SelectedStageFilter = "Baby";
            Assert.Equal(3, vm.FilteredEntries.Count); // baby_happy, baby_angry, spanning_all

            vm.SelectedStageFilter = "Teen";
            Assert.Equal(2, vm.FilteredEntries.Count); // teen_happy, spanning_all

            vm.SelectedStageFilter = "Adult";
            Assert.Equal(2, vm.FilteredEntries.Count); // adult_angry, spanning_all

            vm.SelectedStageFilter = "Spanning";
            Assert.Single(vm.FilteredEntries);
            Assert.Equal("spanning_all", vm.FilteredEntries[0].Name);

            // 4. Filter by Mood
            vm.SelectedStageFilter = "All";
            vm.SelectedMoodFilter = "Happy";
            Assert.Equal(3, vm.FilteredEntries.Count); // baby_happy, teen_happy, spanning_all

            vm.SelectedMoodFilter = "Angry";
            Assert.Equal(3, vm.FilteredEntries.Count); // baby_angry, adult_angry, spanning_all

            // 5. Combined Search + Stage + Mood
            vm.SearchFilterText = "happy";
            vm.SelectedStageFilter = "Baby";
            vm.SelectedMoodFilter = "Happy";
            Assert.Single(vm.FilteredEntries);
            Assert.Equal("baby_happy", vm.FilteredEntries[0].Name);

            // 6. Reset Filters
            vm.ResetFiltersCommand.Execute(null);
            Assert.Equal(5, vm.FilteredEntries.Count);
            Assert.Empty(vm.SearchFilterText);
            Assert.Equal("All", vm.SelectedStageFilter);
            Assert.Equal("All", vm.SelectedMoodFilter);
            Assert.False(vm.HasActiveFilters);
        }

        [Fact]
        public void F3_2_FilteredEntries_NavigationAndSelectionSynchronization()
        {
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_alpha", new SpriteState(128, 64), new FlipperManifestEntry { Name = "anim_alpha", MinLevel = 1, MaxLevel = 10 }),
                ("anim_beta", new SpriteState(128, 64), new FlipperManifestEntry { Name = "anim_beta", MinLevel = 11, MaxLevel = 20 }),
                ("anim_gamma", new SpriteState(128, 64), new FlipperManifestEntry { Name = "anim_gamma", MinLevel = 21, MaxLevel = 30 })
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);
            Assert.Same(vm.Entries[0], vm.SelectedEntry);

            // Filter out the selected entry
            vm.SearchFilterText = "gamma";
            Assert.Single(vm.FilteredEntries);
            Assert.Equal("anim_gamma", vm.SelectedEntry?.Name);

            // Navigate Next/Prev within filtered list
            vm.SelectNextEntryCommand.Execute(null);
            Assert.Equal("anim_gamma", vm.SelectedEntry?.Name);

            vm.ResetFilters();
            Assert.Equal(3, vm.FilteredEntries.Count);

            // SelectNext cycles across all entries
            vm.SelectNextEntryCommand.Execute(null);
            Assert.Equal("anim_alpha", vm.SelectedEntry?.Name);
        }

        [Fact]
        public void F3_3_PerItemValidationBadges_ReflectDiagnosticsCorrectly()
        {
            var entry = new FlipperManifestEntry { Name = "valid_entry", MinLevel = 1, MaxLevel = 10, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var entryVm = new FlipperScheduleEntryViewModel(entry);

            Assert.False(entryVm.HasValidationIssues);
            Assert.False(entryVm.HasErrors);
            Assert.False(entryVm.HasWarnings);
            Assert.Empty(entryVm.ValidationBadgeIcon);

            // Introduce Error
            entryVm.SetValidationDiagnostics([
                new FlipperValidationDiagnostic(FlipperValidationSeverity.Error, "FZ003", "Max level out of range.", nameof(entry.MaxLevel))
            ]);

            Assert.True(entryVm.HasValidationIssues);
            Assert.True(entryVm.HasErrors);
            Assert.False(entryVm.HasWarnings);
            Assert.Equal("❌", entryVm.ValidationBadgeIcon);
            Assert.Contains("1 Error", entryVm.ValidationBadgeText);
            Assert.Contains("FZ003", entryVm.ValidationBadgeTooltip);

            // Introduce Warning
            entryVm.SetValidationDiagnostics([
                new FlipperValidationDiagnostic(FlipperValidationSeverity.Warning, "FZ007", "Weight is high.", nameof(entry.Weight))
            ]);

            Assert.True(entryVm.HasValidationIssues);
            Assert.False(entryVm.HasErrors);
            Assert.True(entryVm.HasWarnings);
            Assert.Equal("⚠️", entryVm.ValidationBadgeIcon);
            Assert.Contains("1 Warning", entryVm.ValidationBadgeText);
        }

        [Fact]
        public void F3_3_ExecuteQuickFix_IndividualCodes_RepairsAnomalies()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var target = vm.Entries[0];

            // 1. FZ001 (Empty Name)
            target.Name = "";
            vm.ExecuteQuickFix("FZ001", target);
            Assert.False(string.IsNullOrWhiteSpace(target.Name));
            Assert.StartsWith("anim_", target.Name);

            // 2. FZ002 / FZ003 (Level Range Inversion / Bounds)
            target.Entry.MinLevel = 25;
            target.Entry.MaxLevel = 5;
            vm.ExecuteQuickFix("FZ002", target);
            Assert.True(target.MinLevel <= target.MaxLevel);
            Assert.InRange(target.MinLevel, 1, 30);
            Assert.InRange(target.MaxLevel, 1, 30);

            // 3. FZ004 / FZ005 (Mood Range Inversion / Bounds)
            target.Entry.MinButthurt = 12;
            target.Entry.MaxButthurt = 2;
            vm.ExecuteQuickFix("FZ004", target);
            Assert.True(target.MinButthurt <= target.MaxButthurt);
            Assert.InRange(target.MinButthurt, 0, 14);
            Assert.InRange(target.MaxButthurt, 0, 14);

            // 4. FZ006 (Zero Weight)
            target.Entry.Weight = 0;
            vm.ExecuteQuickFix("FZ006", target);
            Assert.Equal(1, target.Weight);

            // 5. FZ007 (Excessive Weight)
            target.Entry.Weight = 500;
            vm.ExecuteQuickFix("FZ007", target);
            Assert.Equal(10, target.Weight);

            // 6. FZ011 (Duplicate Name)
            vm.Entries[1].Name = vm.Entries[0].Name;
            vm.ExecuteQuickFix("FZ011", vm.Entries[1]);
            Assert.NotEqual(vm.Entries[0].Name, vm.Entries[1].Name);

            // 7. GAP (Deadzone Gaps)
            target.MinLevel = 1; target.MaxLevel = 5;
            vm.Entries[1].MinLevel = 6; vm.Entries[1].MaxLevel = 10;
            vm.Entries[2].MinLevel = 11; vm.Entries[2].MaxLevel = 15;
            vm.RecalculateMatrix();
            Assert.True(vm.Matrix.UncoveredStatesCount > 0);

            vm.ExecuteQuickFix("GAP", null);
            Assert.Equal(0, vm.Matrix.UncoveredStatesCount);
            Assert.Equal(100.0, vm.Matrix.CoveragePercentage);
        }

        [Fact]
        public void F3_3_MissingSpriteAndDimensions_QuickFixesProperly()
        {
            var mockTabService = new MockTabServiceForMatrix();
            var vm = new FlipperScheduleMatrixViewModel(tabService: mockTabService);

            // Add an entry with no sprite in cache or tabs
            var newEntry = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "custom_ghost_anim" });
            vm.Entries.Add(newEntry);
            vm.RecalculateMatrix();

            var missingDiag = vm.ActionableDiagnostics.FirstOrDefault(d => d.Code == "FZ_MISSING_SPRITE" && d.TargetEntry == newEntry);
            Assert.NotNull(missingDiag);
            Assert.True(missingDiag.HasQuickFix);
            Assert.Equal("⚡ Create Sprite Tab", missingDiag.QuickFixLabel);

            // Execute QuickFix to generate sprite
            missingDiag.ExecuteQuickFix();

            Assert.True(vm.AnimationSprites.ContainsKey("custom_ghost_anim"));
            Assert.Equal(128, vm.AnimationSprites["custom_ghost_anim"].Width);
        }
        #endregion

        #region Milestone 4: Defect Resolution, Undo/Redo & State Integrity (R4)

        [Fact]
        public void F4_1_UndoRedo_CompositeSnapshot_PreservesAllFieldsAndIndependence()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.False(vm.CanUndo);
            Assert.False(vm.CanRedo);
            Assert.Equal("Flipper Asset Pack", vm.PackName);
            Assert.False(vm.IsStockMode);

            // Operation 1: Modify PackName
            vm.PackName = "My Dolphin Pack";
            Assert.True(vm.CanUndo);

            // Operation 2: Modify SelectedMinLevel
            vm.SelectedEntry = vm.Entries[0];
            vm.SelectedMinLevel = 3;

            // Operation 3: Toggle Stock Mode
            vm.ToggleMode();
            Assert.True(vm.IsStockMode);
            Assert.Equal(3, vm.MaxAllowedLevel);

            // ── Verify Undo Step 3 (revert Stock Mode toggle) ──
            vm.Undo();
            Assert.False(vm.IsStockMode);
            Assert.Equal(30, vm.MaxAllowedLevel);
            Assert.Equal(3, vm.Entries[0].MinLevel);
            Assert.Equal("My Dolphin Pack", vm.PackName);
            Assert.False(vm.HasValidationIssues);

            // ── Verify Undo Step 2 (revert MinLevel) ──
            vm.Undo();
            Assert.Equal(1, vm.Entries[0].MinLevel);
            Assert.Equal("My Dolphin Pack", vm.PackName);

            // ── Verify Undo Step 1 (revert PackName) ──
            vm.Undo();
            Assert.Equal("Flipper Asset Pack", vm.PackName);
            Assert.False(vm.CanUndo);
            Assert.True(vm.CanRedo);

            // ── Verify Full Redo Cycle ──
            vm.Redo(); // Redo PackName
            Assert.Equal("My Dolphin Pack", vm.PackName);

            vm.Redo(); // Redo MinLevel
            Assert.Equal(3, vm.Entries[0].MinLevel);

            vm.Redo(); // Redo Stock Mode
            Assert.True(vm.IsStockMode);
            Assert.Equal(3, vm.MaxAllowedLevel);
            Assert.False(vm.CanRedo);
        }

        [Fact]
        public void F4_1_ModeToggle_UndoRedo_RestoresDimensionsAndBoundsWithoutFalseDiagnostics()
        {
            var e1 = new FlipperManifestEntry { Name = "baby", MinLevel = 1, MaxLevel = 9, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var e2 = new FlipperManifestEntry { Name = "teen", MinLevel = 10, MaxLevel = 19, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var e3 = new FlipperManifestEntry { Name = "adult", MinLevel = 20, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };

            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("baby", new SpriteState(128, 64), e1),
                ("teen", new SpriteState(128, 64), e2),
                ("adult", new SpriteState(128, 64), e3)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack, "TierPack");
            Assert.False(vm.IsStockMode);
            Assert.Equal(30, vm.MaxAllowedLevel);
            Assert.Equal(450, vm.Matrix.TotalCells);
            Assert.False(vm.HasValidationIssues);

            // Toggle to Stock Mode -> entries clamped to MaxLevel 3
            vm.ToggleMode();
            Assert.True(vm.IsStockMode);
            Assert.Equal(3, vm.MaxAllowedLevel);
            Assert.Equal(45, vm.Matrix.TotalCells);
            Assert.All(vm.Entries, entry => Assert.InRange(entry.MaxLevel, 1, 3));

            // Undo Stock Mode toggle -> Restores original 1..30 boundaries without FZ003 bounds errors
            vm.Undo();
            Assert.False(vm.IsStockMode);
            Assert.Equal(30, vm.MaxAllowedLevel);
            Assert.Equal(450, vm.Matrix.TotalCells);
            Assert.Equal(1, vm.Entries[0].MinLevel);
            Assert.Equal(9, vm.Entries[0].MaxLevel);
            Assert.Equal(10, vm.Entries[1].MinLevel);
            Assert.Equal(19, vm.Entries[1].MaxLevel);
            Assert.Equal(20, vm.Entries[2].MinLevel);
            Assert.Equal(30, vm.Entries[2].MaxLevel);
            Assert.False(vm.HasValidationIssues);
            Assert.Equal("✅ Valid Manifest", vm.ValidationStatusText);
        }

        [Fact]
        public void F4_2_InspectorProperties_DirectEdits_PushUndoStateAndIgnoreNoOps()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.SelectedEntry = vm.Entries[0];

            // Direct edits on MinLevel, MaxLevel, MinButthurt, MaxButthurt, Weight
            vm.SelectedMinLevel = 2;
            Assert.True(vm.CanUndo);
            Assert.Equal(2, vm.SelectedEntry.MinLevel);

            // No-op edit with identical value should not push duplicate undo
            vm.SelectedMinLevel = 2;
            vm.Undo();
            Assert.False(vm.CanUndo); // Should have only had 1 undo step!
            Assert.Equal(1, vm.SelectedEntry.MinLevel);

            // Test MinMood & MaxMood
            vm.SelectedMinButthurt = 2;
            vm.SelectedMaxButthurt = 10;
            vm.SelectedWeight = 5;

            Assert.Equal(2, vm.SelectedEntry.MinButthurt);
            Assert.Equal(10, vm.SelectedEntry.MaxButthurt);
            Assert.Equal(5, vm.SelectedEntry.Weight);

            vm.Undo(); // Reverts Weight = 1
            Assert.Equal(1, vm.SelectedEntry.Weight);

            vm.Undo(); // Reverts MaxMood = 14
            Assert.Equal(14, vm.SelectedEntry.MaxButthurt);

            vm.Undo(); // Reverts MinMood = 0
            Assert.Equal(0, vm.SelectedEntry.MinButthurt);
        }

        [Fact]
        public void F4_2_StepCommands_AndPresets_TriggerUndo()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.SelectedEntry = vm.Entries[0];

            int initMinL = vm.SelectedMinLevel;
            vm.StepMinLevelCommand.Execute(1);
            Assert.Equal(initMinL + 1, vm.SelectedMinLevel);
            Assert.True(vm.CanUndo);

            int initWeight = vm.SelectedWeight;
            vm.StepWeightCommand.Execute(2);
            Assert.Equal(initWeight + 2, vm.SelectedWeight);

            // Preset command
            vm.SetStagePresetCommand.Execute("Teen");
            Assert.Equal(10, vm.SelectedMinLevel);
            Assert.Equal(19, vm.SelectedMaxLevel);

            // Undo Preset
            vm.Undo();
            Assert.Equal(initMinL + 1, vm.SelectedMinLevel);
            Assert.Equal(initWeight + 2, vm.SelectedWeight);

            // Undo Weight Step
            vm.Undo();
            Assert.Equal(initWeight, vm.SelectedWeight);

            // Undo Level Step
            vm.Undo();
            Assert.Equal(initMinL, vm.SelectedMinLevel);
            Assert.False(vm.CanUndo);
        }

        [Fact]
        public void F4_3_StateClamping_InvertedRangesAndExtremeValues_ClampedSafely()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.SelectedEntry = vm.Entries[0];

            // 1. Extreme out-of-range MinLevel
            vm.SelectedMinLevel = -99;
            Assert.Equal(1, vm.SelectedMinLevel);

            vm.SelectedMaxLevel = 999;
            Assert.Equal(30, vm.SelectedMaxLevel);

            // 2. Setting MinLevel > MaxLevel automatically adjusts MaxLevel
            vm.SelectedMaxLevel = 15;
            vm.SelectedMinLevel = 20;
            Assert.Equal(20, vm.SelectedMinLevel);
            Assert.Equal(20, vm.SelectedMaxLevel);

            // 3. Setting MaxLevel < MinLevel automatically adjusts MinLevel
            vm.SelectedMaxLevel = 5;
            Assert.Equal(5, vm.SelectedMinLevel);
            Assert.Equal(5, vm.SelectedMaxLevel);

            // 4. SetSelectedEntryBounds with inverted parameters
            vm.SetSelectedEntryBounds(25, 5, 12, 2);
            Assert.Equal(5, vm.SelectedMinLevel);
            Assert.Equal(25, vm.SelectedMaxLevel);
            Assert.Equal(2, vm.SelectedMinButthurt);
            Assert.Equal(12, vm.SelectedMaxButthurt);

            // 5. Weight bounds clamping
            vm.SelectedWeight = -10;
            Assert.Equal(1, vm.SelectedWeight);
            vm.SelectedWeight = 500;
            Assert.Equal(100, vm.SelectedWeight);
        }

        [Fact]
        public void F4_3_DuplicateEntry_PreservesSpriteCacheAndAllowsIndependentUndo()
        {
            var mockTabService = new MockTabServiceForMatrix();
            var vm = new FlipperScheduleMatrixViewModel(tabService: mockTabService);
            vm.SelectedEntry = vm.Entries[0];
            string origName = vm.SelectedEntry.Name;

            Assert.True(vm.AnimationSprites.ContainsKey(origName));
            int initialCount = vm.Entries.Count;

            // Duplicate entry
            vm.DuplicateEntryCommand.Execute(null);
            Assert.Equal(initialCount + 1, vm.Entries.Count);
            Assert.NotNull(vm.SelectedEntry);
            Assert.Equal(origName + "_copy", vm.SelectedEntry.Name);

            // Sprite cache should contain entry for the duplicated animation
            Assert.True(vm.AnimationSprites.ContainsKey(origName + "_copy"));
            Assert.Equal(128, vm.AnimationSprites[origName + "_copy"].Width);
            Assert.Equal(64, vm.AnimationSprites[origName + "_copy"].Height);

            // Undo duplication
            vm.Undo();
            Assert.Equal(initialCount, vm.Entries.Count);
            Assert.Equal(origName, vm.SelectedEntry?.Name);
        }

        [Fact]
        public void F4_3_AutoBalance_AllStrategies_MaintainClampingInBothModes()
        {
            var vm = new FlipperScheduleMatrixViewModel();

            // Test in Extended Mode (1..30)
            foreach (FlipperAutoBalanceStrategy strategy in Enum.GetValues<FlipperAutoBalanceStrategy>())
            {
                vm.AutoBalanceWithStrategy(strategy);
                Assert.All(vm.Entries, e =>
                {
                    Assert.InRange(e.MinLevel, 1, 30);
                    Assert.InRange(e.MaxLevel, 1, 30);
                    Assert.True(e.MinLevel <= e.MaxLevel);
                    Assert.InRange(e.MinButthurt, 0, 14);
                    Assert.InRange(e.MaxButthurt, 0, 14);
                    Assert.True(e.MinButthurt <= e.MaxButthurt);
                    Assert.InRange(e.Weight, 1, 100);
                });
                Assert.Equal(30, vm.Matrix.MaxLevel);
            }

            // Switch to Stock Mode (1..3)
            vm.ToggleMode();
            Assert.True(vm.IsStockMode);

            foreach (FlipperAutoBalanceStrategy strategy in Enum.GetValues<FlipperAutoBalanceStrategy>())
            {
                vm.AutoBalanceWithStrategy(strategy);
                Assert.All(vm.Entries, e =>
                {
                    Assert.InRange(e.MinLevel, 1, 3);
                    Assert.InRange(e.MaxLevel, 1, 3);
                    Assert.True(e.MinLevel <= e.MaxLevel);
                    Assert.InRange(e.MinButthurt, 0, 14);
                    Assert.InRange(e.MaxButthurt, 0, 14);
                    Assert.True(e.MinButthurt <= e.MaxButthurt);
                    Assert.InRange(e.Weight, 1, 100);
                });
                Assert.Equal(3, vm.Matrix.MaxLevel);
            }
        }

        #region Selection Stability & Retention Tests

        [Fact]
        public void SelectedEntry_Retained_WhenMinMaxLevelChanged()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var target = vm.Entries[1]; // anim_teen
            vm.SelectedEntry = target;
            Assert.Equal(target, vm.SelectedEntry);

            vm.SelectedMinLevel = 12;
            Assert.Equal(target, vm.SelectedEntry);
            Assert.Equal(12, target.MinLevel);

            vm.SelectedMaxLevel = 22;
            Assert.Equal(target, vm.SelectedEntry);
            Assert.Equal(22, target.MaxLevel);
        }

        [Fact]
        public void SelectedEntry_Retained_WhenMinMaxMoodChanged()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var target = vm.Entries[0]; // anim_baby
            vm.SelectedEntry = target;
            Assert.Equal(target, vm.SelectedEntry);

            vm.SelectedMinButthurt = 3;
            Assert.Equal(target, vm.SelectedEntry);
            Assert.Equal(3, target.MinButthurt);

            vm.SelectedMaxButthurt = 11;
            Assert.Equal(target, vm.SelectedEntry);
            Assert.Equal(11, target.MaxButthurt);
        }

        [Fact]
        public void SelectedEntry_Retained_WhenWeightAndNameChanged()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var target = vm.Entries[2]; // anim_adult
            vm.SelectedEntry = target;
            Assert.Equal(target, vm.SelectedEntry);

            vm.SelectedWeight = 25;
            Assert.Equal(target, vm.SelectedEntry);
            Assert.Equal(25, target.Weight);

            vm.SelectedName = "anim_adult_renamed";
            Assert.Equal(target, vm.SelectedEntry);
            Assert.Equal("anim_adult_renamed", target.Name);
        }

        [Fact]
        public void SelectedEntry_Retained_WhenBoundsSetViaDragOrAdjust()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var target = vm.Entries[1]; // anim_teen
            vm.SelectedEntry = target;

            vm.SetSelectedEntryBounds(5, 25, 2, 12);
            Assert.Equal(target, vm.SelectedEntry);
            Assert.Equal(5, target.MinLevel);
            Assert.Equal(25, target.MaxLevel);
            Assert.Equal(2, target.MinButthurt);
            Assert.Equal(12, target.MaxButthurt);

            vm.AdjustSelectedEntryBounds(1, -1, 1, -1);
            Assert.Equal(target, vm.SelectedEntry);
            Assert.Equal(6, target.MinLevel);
            Assert.Equal(24, target.MaxLevel);
        }

        [Fact]
        public void SelectedEntry_Retained_WhenPresetsAndStepCommandsExecuted()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var target = vm.Entries[0];
            vm.SelectedEntry = target;

            vm.SetStagePresetCommand.Execute("Teen");
            Assert.Equal(target, vm.SelectedEntry);
            Assert.Equal(10, target.MinLevel);
            Assert.Equal(19, target.MaxLevel);

            vm.SetMoodPresetCommand.Execute("Happy");
            Assert.Equal(target, vm.SelectedEntry);
            Assert.Equal(0, target.MinButthurt);
            Assert.Equal(4, target.MaxButthurt);

            vm.StepMinLevelCommand.Execute(1);
            Assert.Equal(target, vm.SelectedEntry);

            vm.StepWeightCommand.Execute(5);
            Assert.Equal(target, vm.SelectedEntry);
        }

        [Fact]
        public void SelectedEntry_FilteredEntries_DifferentialSyncMaintainsActiveItem()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            // Configure standard non-overlapping stages
            vm.Entries[0].MinLevel = 1;  vm.Entries[0].MaxLevel = 9;
            vm.Entries[1].MinLevel = 10; vm.Entries[1].MaxLevel = 19;
            vm.Entries[2].MinLevel = 20; vm.Entries[2].MaxLevel = 30;

            var baby = vm.Entries[0];
            vm.SelectedEntry = baby;

            // Trigger re-filtering while Baby is selected
            vm.ApplyEntryFilter();
            Assert.Equal(baby, vm.SelectedEntry);
            Assert.Contains(baby, vm.FilteredEntries);

            // Filter to only Adult - baby is not in filtered list, so first visible item is selected
            vm.SelectedStageFilter = "Adult";
            Assert.NotEqual(baby, vm.SelectedEntry);
            Assert.Equal(vm.FilteredEntries[0], vm.SelectedEntry);
            Assert.Equal("anim_adult", vm.SelectedEntry.Name);

            // Reset filters - selection remains valid and stable
            vm.ResetFiltersCommand.Execute(null);
            Assert.NotNull(vm.SelectedEntry);
            Assert.Equal(3, vm.FilteredEntries.Count);
        }

        #region 40+ Animation Scaling & Large-Scale Management Tests

        [Fact]
        public void ViewMode_Switching_UpdatesPropertiesAndCommands()
        {
            var vm = new FlipperScheduleMatrixViewModel();

            Assert.Equal(AssetPackViewMode.MatrixStudio, vm.CurrentViewMode);
            Assert.True(vm.IsMatrixStudioView);
            Assert.False(vm.IsDataGridRosterView);
            Assert.False(vm.IsSpriteGalleryView);

            vm.SetViewModeCommand.Execute(AssetPackViewMode.DataGridRoster);
            Assert.Equal(AssetPackViewMode.DataGridRoster, vm.CurrentViewMode);
            Assert.False(vm.IsMatrixStudioView);
            Assert.True(vm.IsDataGridRosterView);
            Assert.False(vm.IsSpriteGalleryView);

            vm.SetViewModeCommand.Execute("SpriteGallery");
            Assert.Equal(AssetPackViewMode.SpriteGallery, vm.CurrentViewMode);
            Assert.False(vm.IsMatrixStudioView);
            Assert.False(vm.IsDataGridRosterView);
            Assert.True(vm.IsSpriteGalleryView);
        }

        [Fact]
        public void SoloMode_Toggle_TogglesAndRequestsRedraw()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            bool redrawTriggered = false;
            vm.MatrixRedrawRequested += (s, e) => redrawTriggered = true;

            Assert.False(vm.IsSoloModeActive);

            vm.ToggleSoloModeCommand.Execute(null);
            Assert.True(vm.IsSoloModeActive);
            Assert.True(redrawTriggered);

            redrawTriggered = false;
            vm.IsSoloModeActive = false;
            Assert.False(vm.IsSoloModeActive);
            Assert.True(redrawTriggered);
        }

        [Fact]
        public void HighDensity_Sorting_OrdersEntriesCorrectly()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.Entries.Clear();

            var a = new FlipperScheduleEntryViewModel { Name = "charlie", MinLevel = 10, MaxLevel = 15, MinButthurt = 5, MaxButthurt = 8, Weight = 1 };
            var b = new FlipperScheduleEntryViewModel { Name = "alpha", MinLevel = 1, MaxLevel = 5, MinButthurt = 0, MaxButthurt = 4, Weight = 10 };
            var c = new FlipperScheduleEntryViewModel { Name = "bravo", MinLevel = 20, MaxLevel = 25, MinButthurt = 9, MaxButthurt = 14, Weight = 5 };

            vm.Entries.Add(a);
            vm.Entries.Add(b);
            vm.Entries.Add(c);

            // Sort Name (A-Z)
            vm.SelectedSortOption = "Name (A-Z)";
            Assert.Equal("alpha", vm.FilteredEntries[0].Name);
            Assert.Equal("bravo", vm.FilteredEntries[1].Name);
            Assert.Equal("charlie", vm.FilteredEntries[2].Name);

            // Sort Level (Low-High)
            vm.SelectedSortOption = "Level (Low-High)";
            Assert.Equal("alpha", vm.FilteredEntries[0].Name);
            Assert.Equal("charlie", vm.FilteredEntries[1].Name);
            Assert.Equal("bravo", vm.FilteredEntries[2].Name);

            // Sort Weight (High-Low)
            vm.SelectedSortOption = "Weight (High-Low)";
            Assert.Equal("alpha", vm.FilteredEntries[0].Name);   // Weight 10
            Assert.Equal("bravo", vm.FilteredEntries[1].Name);   // Weight 5
            Assert.Equal("charlie", vm.FilteredEntries[2].Name); // Weight 1

            // Sort Mood (Low-High)
            vm.SelectedSortOption = "Mood (Low-High)";
            Assert.Equal("alpha", vm.FilteredEntries[0].Name);
            Assert.Equal("charlie", vm.FilteredEntries[1].Name);
            Assert.Equal("bravo", vm.FilteredEntries[2].Name);
        }

        [Fact]
        public void MultiSelection_SelectAll_ClearSelection_SelectAllInStage()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.Entries.Clear();

            var baby1 = new FlipperScheduleEntryViewModel { Name = "baby_1", MinLevel = 1, MaxLevel = 5, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var baby2 = new FlipperScheduleEntryViewModel { Name = "baby_2", MinLevel = 6, MaxLevel = 9, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var adult1 = new FlipperScheduleEntryViewModel { Name = "adult_1", MinLevel = 20, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };

            vm.Entries.Add(baby1);
            vm.Entries.Add(baby2);
            vm.Entries.Add(adult1);

            Assert.Empty(vm.SelectedEntries);
            Assert.False(vm.HasMultiSelection);

            // Select All
            vm.SelectAllCommand.Execute(null);
            Assert.Equal(3, vm.SelectedEntries.Count);
            Assert.True(vm.HasMultiSelection);
            Assert.Equal("3 Selected", vm.SelectedEntriesCountText);

            // Clear Selection
            vm.ClearSelectionCommand.Execute(null);
            Assert.Empty(vm.SelectedEntries);
            Assert.False(vm.HasMultiSelection);

            // Select Stage Baby
            vm.SelectAllInStageCommand.Execute("Baby");
            Assert.Equal(2, vm.SelectedEntries.Count);
            Assert.Contains(baby1, vm.SelectedEntries);
            Assert.Contains(baby2, vm.SelectedEntries);
            Assert.DoesNotContain(adult1, vm.SelectedEntries);
        }

        [Fact]
        public void BulkOperations_ShiftLevel_ClampsBoundsWithinAllowedRange()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.Entries.Clear();

            var anim1 = new FlipperScheduleEntryViewModel { Name = "anim1", MinLevel = 5, MaxLevel = 10, MinButthurt = 0, MaxButthurt = 4, Weight = 1, IsSelected = true };
            var anim2 = new FlipperScheduleEntryViewModel { Name = "anim2", MinLevel = 28, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 4, Weight = 1, IsSelected = true };
            vm.Entries.Add(anim1);
            vm.Entries.Add(anim2);

            // Shift up by 1 (anim2 was 28..30, span is preserved at upper bound 30 -> 28..30)
            vm.BulkShiftLevelCommand.Execute(1);
            Assert.Equal(6, anim1.MinLevel);
            Assert.Equal(11, anim1.MaxLevel);
            Assert.Equal(28, anim2.MinLevel);
            Assert.Equal(30, anim2.MaxLevel); // Clamped at 30 with span preserved

            // Shift down by 10 (anim1 was 6..11, span 5 is preserved at lower bound 1 -> 1..6)
            vm.BulkShiftLevelCommand.Execute(-10);
            Assert.Equal(1, anim1.MinLevel);
            Assert.Equal(6, anim1.MaxLevel);
        }

        [Fact]
        public void BulkOperations_ShiftMood_ClampsBoundsWithinAllowedRange()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.Entries.Clear();

            var anim1 = new FlipperScheduleEntryViewModel { Name = "anim1", MinLevel = 1, MaxLevel = 5, MinButthurt = 2, MaxButthurt = 6, Weight = 1, IsSelected = true };
            vm.Entries.Add(anim1);

            // Shift mood +3
            vm.BulkShiftMoodCommand.Execute(3);
            Assert.Equal(5, anim1.MinButthurt);
            Assert.Equal(9, anim1.MaxButthurt);

            // Shift mood +10 (clamped at 14 with span 4 preserved -> 10..14)
            vm.BulkShiftMoodCommand.Execute(10);
            Assert.Equal(10, anim1.MinButthurt);
            Assert.Equal(14, anim1.MaxButthurt);

            // Shift mood -20 (clamped at 0 with span 4 preserved -> 0..4)
            vm.BulkShiftMoodCommand.Execute(-20);
            Assert.Equal(0, anim1.MinButthurt);
            Assert.Equal(4, anim1.MaxButthurt);
        }

        [Fact]
        public void BulkOperations_SetWeight_And_Stage()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.Entries.Clear();

            var anim1 = new FlipperScheduleEntryViewModel { Name = "anim1", MinLevel = 1, MaxLevel = 5, MinButthurt = 0, MaxButthurt = 4, Weight = 1, IsSelected = true };
            var anim2 = new FlipperScheduleEntryViewModel { Name = "anim2", MinLevel = 1, MaxLevel = 5, MinButthurt = 0, MaxButthurt = 4, Weight = 2, IsSelected = true };
            vm.Entries.Add(anim1);
            vm.Entries.Add(anim2);

            // Bulk Set Weight
            vm.BulkSetWeightCommand.Execute(7);
            Assert.Equal(7, anim1.Weight);
            Assert.Equal(7, anim2.Weight);

            // Bulk Set Stage to Teen
            vm.BulkSetStageCommand.Execute("Teen");
            Assert.Equal(10, anim1.MinLevel);
            Assert.Equal(19, anim1.MaxLevel);
            Assert.Equal(10, anim2.MinLevel);
            Assert.Equal(19, anim2.MaxLevel);

            // Bulk Set Stage to Adult
            vm.BulkSetStageCommand.Execute("Adult");
            Assert.Equal(20, anim1.MinLevel);
            Assert.Equal(30, anim1.MaxLevel);
            Assert.Equal(20, anim2.MinLevel);
            Assert.Equal(30, anim2.MaxLevel);
        }

        [Fact]
        public void BulkOperations_Duplicate_And_Delete()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.Entries.Clear();

            var anim1 = new FlipperScheduleEntryViewModel { Name = "test_a", MinLevel = 1, MaxLevel = 5, MinButthurt = 0, MaxButthurt = 4, Weight = 3, IsSelected = true };
            var anim2 = new FlipperScheduleEntryViewModel { Name = "test_b", MinLevel = 10, MaxLevel = 15, MinButthurt = 5, MaxButthurt = 8, Weight = 4, IsSelected = true };
            vm.Entries.Add(anim1);
            vm.Entries.Add(anim2);

            // Duplicate selected
            vm.BulkDuplicateCommand.Execute(null);
            Assert.Equal(4, vm.Entries.Count);
            var cloneA = vm.Entries.FirstOrDefault(e => e.Name == "test_a_1");
            var cloneB = vm.Entries.FirstOrDefault(e => e.Name == "test_b_1");
            Assert.NotNull(cloneA);
            Assert.NotNull(cloneB);

            // Bulk Delete selected (the newly cloned items are selected)
            vm.BulkDeleteCommand.Execute(null);
            Assert.Equal(2, vm.Entries.Count);
            Assert.Contains(anim1, vm.Entries);
            Assert.Contains(anim2, vm.Entries);
            Assert.DoesNotContain(cloneA, vm.Entries);
            Assert.DoesNotContain(cloneB, vm.Entries);
        }

        [Fact]
        public void LargeScale_40Animations_StressAndTelemetry()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.Entries.Clear();

            // Populate 40 distinct animations across the matrix
            for (int i = 1; i <= 40; i++)
            {
                int minL = ((i - 1) % 30) + 1;
                int maxL = Math.Min(30, minL + 2);
                int minM = (i % 15);
                int maxM = Math.Min(14, minM + 1);

                vm.Entries.Add(new FlipperScheduleEntryViewModel
                {
                    Name = $"anim_manifest_{i:D2}",
                    MinLevel = minL,
                    MaxLevel = maxL,
                    MinButthurt = minM,
                    MaxButthurt = maxM,
                    Weight = (i % 5) + 1
                });
            }

            Assert.Equal(40, vm.Entries.Count);

            // Test filtering
            vm.SearchFilterText = "anim_manifest_0";
            Assert.Equal(9, vm.FilteredEntries.Count); // 01..09

            vm.ResetFiltersCommand.Execute(null);
            Assert.Equal(40, vm.FilteredEntries.Count);

            // Test sorting by coverage
            vm.SelectedSortOption = "Coverage (Cells)";
            Assert.Equal(40, vm.FilteredEntries.Count);

            // Test Select All in Stage Adult
            vm.SelectAllInStageCommand.Execute("Adult");
            Assert.True(vm.SelectedEntries.Count > 0);
            Assert.All(vm.SelectedEntries, e => Assert.True(e.MaxLevel >= 20));

            // Verify telemetry properties on entries
            var first = vm.Entries[0];
            Assert.NotNull(first.StageName);
            Assert.NotNull(first.MoodRangeName);
            Assert.NotNull(first.HealthStatusText);
            Assert.NotNull(first.CoverageText);
        }

        #region Bug Fix Hardening Tests

        [Fact]
        public void DeleteEntry_WithTargetParameter_DeletesSpecifiedEntry_NotSelectedEntry()
        {
            var vm = new FlipperScheduleMatrixViewModel();

            Assert.Equal(3, vm.Entries.Count);
            vm.SelectedEntry = vm.Entries[0]; // anim_baby is selected

            var targetToDelete = vm.Entries[2]; // anim_adult
            vm.DeleteEntryCommand.Execute(targetToDelete);

            Assert.Equal(2, vm.Entries.Count);
            Assert.DoesNotContain(vm.Entries, e => e.Name == "anim_adult");
            Assert.Equal("anim_baby", vm.SelectedEntry?.Name); // Selection was preserved
        }

        [Fact]
        public void DuplicateEntry_WithTargetParameter_ClonesSpecifiedEntry()
        {
            var vm = new FlipperScheduleMatrixViewModel();

            vm.SelectedEntry = vm.Entries[0]; // anim_baby is selected
            var targetToClone = vm.Entries[1]; // anim_teen

            vm.DuplicateEntryCommand.Execute(targetToClone);

            Assert.Equal(4, vm.Entries.Count);
            Assert.Contains(vm.Entries, e => e.Name == "anim_teen_copy");
            Assert.Equal("anim_teen_copy", vm.SelectedEntry?.Name);
        }

        [Fact]
        public void DirectEntryPropertyChange_RecalculatesMatrixAutomatically()
        {
            var vm = new FlipperScheduleMatrixViewModel();

            bool matrixRedrawFired = false;
            vm.MatrixRedrawRequested += (s, e) => matrixRedrawFired = true;

            // Direct change to property on entry (simulating Data Table cell edits)
            vm.Entries[0].MaxLevel = 15;

            Assert.True(matrixRedrawFired);
            Assert.Equal(15, vm.Matrix.GetCell(15, 0).MatchingEntries.FirstOrDefault(e => e.Name == "anim_baby")?.MaxLevel);
        }

        [Fact]
        public void DirectEntryIsSelectedToggle_UpdatesSelectedEntriesCollectionAndTelemetry()
        {
            var vm = new FlipperScheduleMatrixViewModel();

            Assert.Empty(vm.SelectedEntries);
            Assert.False(vm.HasMultiSelection);
            Assert.Empty(vm.SelectedEntriesCountText);

            // Toggle first entry
            vm.Entries[0].IsSelected = true;
            Assert.Single(vm.SelectedEntries);
            Assert.False(vm.HasMultiSelection);
            Assert.Equal("1 Selected", vm.SelectedEntriesCountText);

            // Toggle second entry
            vm.Entries[1].IsSelected = true;
            Assert.Equal(2, vm.SelectedEntries.Count);
            Assert.True(vm.HasMultiSelection);
            Assert.Equal("2 Selected", vm.SelectedEntriesCountText);

            // Uncheck first entry
            vm.Entries[0].IsSelected = false;
            Assert.Single(vm.SelectedEntries);
            Assert.Contains(vm.Entries[1], vm.SelectedEntries);
        }

        [Fact]
        public void QuickFix_FZ011_RenamesDuplicateAndMigratesSpriteDictionaryKey()
        {
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("duplicate", new SpriteState(128, 64), new FlipperManifestEntry { Name = "duplicate", MinLevel = 1, MaxLevel = 10, MinButthurt = 0, MaxButthurt = 14, Weight = 1 }),
                ("duplicate", new SpriteState(128, 64), new FlipperManifestEntry { Name = "duplicate", MinLevel = 11, MaxLevel = 20, MinButthurt = 0, MaxButthurt = 14, Weight = 1 })
            };
            var vm = new FlipperScheduleMatrixViewModel(pack: pack);

            var second = vm.Entries[1];
            vm.ExecuteQuickFix("FZ011", second);

            Assert.Equal("duplicate_2", second.Name);
            Assert.True(vm.AnimationSprites.ContainsKey("duplicate_2"));
        }

        #endregion

        #region Non-Destructive Stock & Extended Mode Switching Tests

        [Fact]
        public void ToggleMode_RoundTrip_IsNonDestructive_PreservesOriginalExtendedBounds()
        {
            var e1 = new FlipperManifestEntry { Name = "anim_baby", MinLevel = 1, MaxLevel = 9, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var e2 = new FlipperManifestEntry { Name = "anim_teen", MinLevel = 10, MaxLevel = 19, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var e3 = new FlipperManifestEntry { Name = "anim_adult", MinLevel = 20, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };

            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_baby", new SpriteState(128, 64), e1),
                ("anim_teen", new SpriteState(128, 64), e2),
                ("anim_adult", new SpriteState(128, 64), e3)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack: pack);
            Assert.False(vm.IsStockMode);
            Assert.Equal(30, vm.MaxAllowedLevel);

            // 1. Toggle to Stock Mode
            vm.ToggleModeCommand.Execute(null);
            Assert.True(vm.IsStockMode);
            Assert.Equal(3, vm.MaxAllowedLevel);

            // Verify mapping to 3 stock stages (L1, L2, L3)
            var babyVm = vm.Entries[0];
            var teenVm = vm.Entries[1];
            var adultVm = vm.Entries[2];

            Assert.Equal(1, babyVm.MinLevel);
            Assert.Equal(1, babyVm.MaxLevel);
            Assert.Equal(2, teenVm.MinLevel);
            Assert.Equal(2, teenVm.MaxLevel);
            Assert.Equal(3, adultVm.MinLevel);
            Assert.Equal(3, adultVm.MaxLevel);

            // 2. Toggle back to Extended Mode
            vm.ToggleModeCommand.Execute(null);
            Assert.False(vm.IsStockMode);
            Assert.Equal(30, vm.MaxAllowedLevel);

            // Verify 100% lossless restoration of original bounds
            Assert.Equal(1, babyVm.MinLevel);
            Assert.Equal(9, babyVm.MaxLevel);
            Assert.Equal(10, teenVm.MinLevel);
            Assert.Equal(19, teenVm.MaxLevel);
            Assert.Equal(20, adultVm.MinLevel);
            Assert.Equal(30, adultVm.MaxLevel);
        }

        [Fact]
        public void ToggleMode_CustomFineGrainedRanges_PreservedLosslessly()
        {
            var e1 = new FlipperManifestEntry { Name = "walk", MinLevel = 5, MaxLevel = 8, MinButthurt = 2, MaxButthurt = 6, Weight = 2 };
            var e2 = new FlipperManifestEntry { Name = "run", MinLevel = 12, MaxLevel = 16, MinButthurt = 4, MaxButthurt = 10, Weight = 3 };
            var e3 = new FlipperManifestEntry { Name = "jump", MinLevel = 22, MaxLevel = 27, MinButthurt = 8, MaxButthurt = 14, Weight = 1 };

            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("walk", new SpriteState(128, 64), e1),
                ("run", new SpriteState(128, 64), e2),
                ("jump", new SpriteState(128, 64), e3)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack: pack);

            // Switch to Stock Mode
            vm.ToggleMode();
            Assert.True(vm.IsStockMode);
            Assert.Equal(1, vm.Entries[0].MinLevel); // Walk -> Baby Stage 1
            Assert.Equal(1, vm.Entries[0].MaxLevel);
            Assert.Equal(2, vm.Entries[1].MinLevel); // Run -> Teen Stage 2
            Assert.Equal(2, vm.Entries[1].MaxLevel);
            Assert.Equal(3, vm.Entries[2].MinLevel); // Jump -> Adult Stage 3
            Assert.Equal(3, vm.Entries[2].MaxLevel);

            // Switch back to Extended Mode
            vm.ToggleMode();
            Assert.False(vm.IsStockMode);

            // Exact fine-grained level ranges are preserved losslessly
            Assert.Equal(5, vm.Entries[0].MinLevel);
            Assert.Equal(8, vm.Entries[0].MaxLevel);
            Assert.Equal(12, vm.Entries[1].MinLevel);
            Assert.Equal(16, vm.Entries[1].MaxLevel);
            Assert.Equal(22, vm.Entries[2].MinLevel);
            Assert.Equal(27, vm.Entries[2].MaxLevel);
        }

        [Fact]
        public void ToggleMode_StockToExtended_ExpandsOfficialStages()
        {
            var e1 = new FlipperManifestEntry { Name = "stock_baby", MinLevel = 1, MaxLevel = 1, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var e2 = new FlipperManifestEntry { Name = "stock_teen", MinLevel = 2, MaxLevel = 2, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var e3 = new FlipperManifestEntry { Name = "stock_adult", MinLevel = 3, MaxLevel = 3, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };

            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("stock_baby", new SpriteState(128, 64), e1),
                ("stock_teen", new SpriteState(128, 64), e2),
                ("stock_adult", new SpriteState(128, 64), e3)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack: pack);
            // Initially forced to Stock Mode since all entries <= 3
            vm.IsStockMode = true;
            Assert.True(vm.IsStockMode);

            // Toggle to Extended Mode (Momentum L1-30)
            vm.ToggleMode();
            Assert.False(vm.IsStockMode);
            Assert.Equal(30, vm.MaxAllowedLevel);

            // Verify clean expansion across all 30 levels
            Assert.Equal(1, vm.Entries[0].MinLevel);
            Assert.Equal(9, vm.Entries[0].MaxLevel);
            Assert.Equal(10, vm.Entries[1].MinLevel);
            Assert.Equal(19, vm.Entries[1].MaxLevel);
            Assert.Equal(20, vm.Entries[2].MinLevel);
            Assert.Equal(30, vm.Entries[2].MaxLevel);
        }

        [Fact]
        public void ToggleMode_ModifyingStageInStockMode_ExpandsToNewStageInExtendedMode()
        {
            var e1 = new FlipperManifestEntry { Name = "anim_test", MinLevel = 1, MaxLevel = 9, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_test", new SpriteState(128, 64), e1)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack: pack);

            // Switch to Stock Mode
            vm.ToggleMode();
            Assert.True(vm.IsStockMode);
            Assert.Equal(1, vm.Entries[0].MinLevel);

            // User explicitly reassigns animation to Adult stage (Level 3) while in Stock mode
            vm.Entries[0].MinLevel = 3;
            vm.Entries[0].MaxLevel = 3;

            // Switch back to Extended Mode
            vm.ToggleMode();
            Assert.False(vm.IsStockMode);

            // Recognizes the stage modification and expands to Adult bracket (20..30)
            Assert.Equal(20, vm.Entries[0].MinLevel);
            Assert.Equal(30, vm.Entries[0].MaxLevel);
        }

        [Fact]
        public void RenameEntry_MigratesSpriteAndFilePath_AndNotifiesTabService()
        {
            var mockTabService = new Moq.Mock<Hexprite.Services.IWorkspaceTabService>();
            mockTabService.Setup(t => t.RenameTab(Moq.It.IsAny<string>(), Moq.It.IsAny<string>())).Returns(true);

            var e1 = new FlipperManifestEntry { Name = "walk", MinLevel = 1, MaxLevel = 9, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("walk", new SpriteState(128, 64), e1)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack: pack, tabService: mockTabService.Object);
            vm.SetAnimationFilePath("walk", @"C:\anims\walk.hexp");

            Assert.True(vm.AnimationSprites.ContainsKey("walk"));
            Assert.Equal(@"C:\anims\walk.hexp", vm.AnimationFilePaths["walk"]);

            // Act: Rename "walk" -> "run"
            vm.Entries[0].Name = "run";

            // Assert
            Assert.False(vm.AnimationSprites.ContainsKey("walk"));
            Assert.True(vm.AnimationSprites.ContainsKey("run"));

            Assert.False(vm.AnimationFilePaths.ContainsKey("walk"));
            Assert.True(vm.AnimationFilePaths.ContainsKey("run"));
            Assert.Equal(@"C:\anims\walk.hexp", vm.AnimationFilePaths["run"]);

            mockTabService.Verify(t => t.RenameTab("walk", "run"), Moq.Times.Once);
        }

        [Fact]
        public void DeleteEntryCommand_WithTargetParameter_SafelyRemovesFromPack_AndPreservesFilePathInUndo()
        {
            var e1 = new FlipperManifestEntry { Name = "anim_1", MinLevel = 1, MaxLevel = 10, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var e2 = new FlipperManifestEntry { Name = "anim_2", MinLevel = 11, MaxLevel = 20, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_1", new SpriteState(128, 64), e1),
                ("anim_2", new SpriteState(128, 64), e2)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack: pack);
            vm.SetAnimationFilePath("anim_2", @"C:\anims\anim_2.hexp");
            Assert.Equal(2, vm.Entries.Count);

            // Act: Execute DeleteEntryCommand with target parameter (simulating ContextMenu click)
            var target = vm.Entries[1]; // anim_2
            vm.DeleteEntryCommand.Execute(target);

            // Assert: Removed from pack, but undo stack retains info
            Assert.Single(vm.Entries);
            Assert.Equal("anim_1", vm.Entries[0].Name);
            Assert.False(vm.AnimationSprites.ContainsKey("anim_2"));
            Assert.False(vm.AnimationFilePaths.ContainsKey("anim_2"));
            Assert.Contains("Removed animation 'anim_2' from asset pack", vm.StatusMessage);

            // Undo restoration
            vm.Undo();
            Assert.Equal(2, vm.Entries.Count);
            Assert.True(vm.AnimationSprites.ContainsKey("anim_2"));
            Assert.Equal(@"C:\anims\anim_2.hexp", vm.AnimationFilePaths["anim_2"]);
        }

        [Fact]
        public void DeleteEntryCommand_WhenOnlyOneEntryLeft_DoesNotRemoveLastEntry()
        {
            var e1 = new FlipperManifestEntry { Name = "sole_anim", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("sole_anim", new SpriteState(128, 64), e1)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack: pack);
            Assert.Single(vm.Entries);

            // Act
            vm.DeleteEntryCommand.Execute(vm.Entries[0]);

            // Assert: Invariant preserved, pack cannot have 0 entries
            Assert.Single(vm.Entries);
            Assert.Equal("sole_anim", vm.Entries[0].Name);
        }

        [Fact]
        public void SortByColumn_TogglesAscendingAndDescending_AndUpdatesGlyphs()
        {
            var e1 = new FlipperManifestEntry { Name = "bravo", MinLevel = 10, MaxLevel = 20, MinButthurt = 5, MaxButthurt = 10, Weight = 5 };
            var e2 = new FlipperManifestEntry { Name = "alpha", MinLevel = 1, MaxLevel = 9, MinButthurt = 0, MaxButthurt = 4, Weight = 10 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("bravo", new SpriteState(128, 64), e1),
                ("alpha", new SpriteState(128, 64), e2)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack: pack);

            // Initial: default order
            Assert.Equal("bravo", vm.FilteredEntries[0].Name);

            // Click "Name" -> Ascending A-Z
            vm.SortByColumnCommand.Execute("Name");
            Assert.Equal("alpha", vm.FilteredEntries[0].Name);
            Assert.Equal("bravo", vm.FilteredEntries[1].Name);
            Assert.Equal(" ▲", vm.NameSortGlyph);

            // Click "Name" again -> Descending Z-A
            vm.SortByColumnCommand.Execute("Name");
            Assert.Equal("bravo", vm.FilteredEntries[0].Name);
            Assert.Equal("alpha", vm.FilteredEntries[1].Name);
            Assert.Equal(" ▼", vm.NameSortGlyph);

            // Click "Weight" -> Ascending 5, 10
            vm.SortByColumnCommand.Execute("Weight");
            Assert.Equal("bravo", vm.FilteredEntries[0].Name);
            Assert.Equal("alpha", vm.FilteredEntries[1].Name);
            Assert.Equal(" ▲", vm.WeightSortGlyph);
            Assert.Equal(string.Empty, vm.NameSortGlyph);

            // Click "Weight" again -> Descending 10, 5
            vm.SortByColumnCommand.Execute("Weight");
            Assert.Equal("alpha", vm.FilteredEntries[0].Name);
            Assert.Equal("bravo", vm.FilteredEntries[1].Name);
            Assert.Equal(" ▼", vm.WeightSortGlyph);
        }

        [Fact]
        public void MasterSelectionState_AndToggleSelectAll_UpdatesCorrectly()
        {
            var e1 = new FlipperManifestEntry { Name = "anim_1", MinLevel = 1, MaxLevel = 10, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var e2 = new FlipperManifestEntry { Name = "anim_2", MinLevel = 11, MaxLevel = 20, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_1", new SpriteState(128, 64), e1),
                ("anim_2", new SpriteState(128, 64), e2)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack: pack);

            // Initial: None selected
            Assert.False(vm.IsAllSelected);
            Assert.False(vm.IsAnySelected);
            Assert.Equal(false, vm.MasterSelectionState);

            // Toggle select all
            vm.ToggleSelectAllCommand.Execute(null);
            Assert.True(vm.IsAllSelected);
            Assert.True(vm.IsAnySelected);
            Assert.Equal(true, vm.MasterSelectionState);
            Assert.All(vm.Entries, e => Assert.True(e.IsSelected));

            // Deselect one entry -> Indeterminate state (null)
            vm.Entries[0].IsSelected = false;
            Assert.False(vm.IsAllSelected);
            Assert.True(vm.IsAnySelected);
            Assert.Null(vm.MasterSelectionState);

            // Toggle again -> selects all
            vm.ToggleSelectAllCommand.Execute(null);
            Assert.True(vm.IsAllSelected);
            Assert.Equal(true, vm.MasterSelectionState);

            // Toggle when all selected -> deselects all
            vm.ToggleSelectAllCommand.Execute(null);
            Assert.False(vm.IsAllSelected);
            Assert.False(vm.IsAnySelected);
            Assert.Equal(false, vm.MasterSelectionState);
        }

        [Fact]
        public void BulkDelete_WhenAllEntriesSelected_PreservesOneEntryToKeepPackValid()
        {
            var e1 = new FlipperManifestEntry { Name = "anim_1", MinLevel = 1, MaxLevel = 10, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var e2 = new FlipperManifestEntry { Name = "anim_2", MinLevel = 11, MaxLevel = 20, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_1", new SpriteState(128, 64), e1),
                ("anim_2", new SpriteState(128, 64), e2)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack: pack);
            vm.SelectAll();
            Assert.Equal(2, vm.SelectedEntries.Count);

            // Act
            vm.BulkDeleteCommand.Execute(null);

            // Assert: 1 entry is preserved
            Assert.Single(vm.Entries);
            Assert.Equal("anim_1", vm.Entries[0].Name);
            Assert.Contains("Retained 'anim_1' to maintain valid pack", vm.StatusMessage);
        }

        [Fact]
        public void Entry_SetStage_InExtendedAndStockMode_UpdatesBoundsProperly()
        {
            var entry = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "test", MinLevel = 1, MaxLevel = 30 });

            // Extended mode (default)
            entry.SetStage("Baby", isStockMode: false);
            Assert.Equal(1, entry.MinLevel);
            Assert.Equal(9, entry.MaxLevel);
            Assert.Equal("Baby (L1-9)", entry.StageName);

            entry.SetStage("Teen", isStockMode: false);
            Assert.Equal(10, entry.MinLevel);
            Assert.Equal(19, entry.MaxLevel);
            Assert.Equal("Teen (L10-19)", entry.StageName);

            entry.SetStage("Adult", isStockMode: false);
            Assert.Equal(20, entry.MinLevel);
            Assert.Equal(30, entry.MaxLevel);
            Assert.Equal("Adult (L20-30)", entry.StageName);

            // Stock mode (L1-3)
            entry.SetStage("Baby", isStockMode: true);
            Assert.Equal(1, entry.MinLevel);
            Assert.Equal(1, entry.MaxLevel);
            Assert.Equal("Baby (L1)", entry.StageName);

            entry.SetStage("Teen", isStockMode: true);
            Assert.Equal(2, entry.MinLevel);
            Assert.Equal(2, entry.MaxLevel);
            Assert.Equal("Teen (L2)", entry.StageName);

            entry.SetStage("Adult", isStockMode: true);
            Assert.Equal(3, entry.MinLevel);
            Assert.Equal(3, entry.MaxLevel);
            Assert.Equal("Adult (L3)", entry.StageName);
        }

        [Fact]
        public void Entry_SetMoodPreset_UpdatesButthurtBoundsAndColors()
        {
            var entry = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "test", MinButthurt = 0, MaxButthurt = 14 });

            entry.SetMoodPreset("Happy");
            Assert.Equal(0, entry.MinButthurt);
            Assert.Equal(4, entry.MaxButthurt);
            Assert.Equal("Happy (0-4)", entry.MoodRangeName);
            Assert.Equal("#39FF14", entry.MoodColorHex);

            entry.SetMoodPreset("Neutral");
            Assert.Equal(5, entry.MinButthurt);
            Assert.Equal(9, entry.MaxButthurt);
            Assert.Equal("Neutral (5-9)", entry.MoodRangeName);
            Assert.Equal("#FFB300", entry.MoodColorHex);

            entry.SetMoodPreset("Angry");
            Assert.Equal(10, entry.MinButthurt);
            Assert.Equal(14, entry.MaxButthurt);
            Assert.Equal("Angry (10-14)", entry.MoodRangeName);
            Assert.Equal("#FF5252", entry.MoodColorHex);
        }

        [Fact]
        public void BulkShiftWeightCommand_ShiftsSelectedWeights_WithinValidBounds()
        {
            var e1 = new FlipperManifestEntry { Name = "anim_1", Weight = 5 };
            var e2 = new FlipperManifestEntry { Name = "anim_2", Weight = 10 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_1", new SpriteState(128, 64), e1),
                ("anim_2", new SpriteState(128, 64), e2)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack: pack);
            vm.SelectAll();

            // Shift weight +1
            vm.BulkShiftWeightCommand.Execute(1);
            Assert.Equal(6, vm.Entries[0].Weight);
            Assert.Equal(11, vm.Entries[1].Weight);

            // Shift weight -5
            vm.BulkShiftWeightCommand.Execute(-5);
            Assert.Equal(1, vm.Entries[0].Weight);
            Assert.Equal(6, vm.Entries[1].Weight);
        }

        [Fact]
        public void SelectedEntriesCountPillText_FormatsCorrectly()
        {
            var e1 = new FlipperManifestEntry { Name = "anim_1" };
            var e2 = new FlipperManifestEntry { Name = "anim_2" };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_1", new SpriteState(128, 64), e1),
                ("anim_2", new SpriteState(128, 64), e2)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack: pack);
            vm.ClearSelection();
            vm.SelectedEntry = null;
            Assert.Equal("0 Selected", vm.SelectedEntriesCountPillText);

            vm.SelectedEntry = vm.Entries[0];
            Assert.Equal("1 of 2 Selected", vm.SelectedEntriesCountPillText);

            vm.SelectAll();
            Assert.Equal("2 of 2 Selected", vm.SelectedEntriesCountPillText);
        }

        [Fact]
        public void BulkExpandAndShrinkLevel_AdjustsMaxLevel_WhilePreservingMinLevel()
        {
            var e1 = new FlipperManifestEntry { Name = "anim_1", MinLevel = 1, MaxLevel = 5 };
            var e2 = new FlipperManifestEntry { Name = "anim_2", MinLevel = 10, MaxLevel = 15 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_1", new SpriteState(128, 64), e1),
                ("anim_2", new SpriteState(128, 64), e2)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack: pack);
            vm.SelectAll();

            // Expand level coverage +2
            vm.BulkExpandLevelCommand.Execute(2);
            Assert.Equal(1, vm.Entries[0].MinLevel);
            Assert.Equal(7, vm.Entries[0].MaxLevel);
            Assert.Equal(10, vm.Entries[1].MinLevel);
            Assert.Equal(17, vm.Entries[1].MaxLevel);

            // Shrink level coverage -1
            vm.BulkShrinkLevelCommand.Execute(1);
            Assert.Equal(1, vm.Entries[0].MinLevel);
            Assert.Equal(6, vm.Entries[0].MaxLevel);
            Assert.Equal(10, vm.Entries[1].MinLevel);
            Assert.Equal(16, vm.Entries[1].MaxLevel);

            // Shrink past MinLevel clamps to MinLevel
            vm.BulkShrinkLevelCommand.Execute(10);
            Assert.Equal(1, vm.Entries[0].MinLevel);
            Assert.Equal(1, vm.Entries[0].MaxLevel);
        }

        [Fact]
        public void BulkExpandAndShrinkMood_AdjustsMaxMood_WhilePreservingMinMood()
        {
            var e1 = new FlipperManifestEntry { Name = "anim_1", MinButthurt = 0, MaxButthurt = 4 };
            var e2 = new FlipperManifestEntry { Name = "anim_2", MinButthurt = 5, MaxButthurt = 8 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_1", new SpriteState(128, 64), e1),
                ("anim_2", new SpriteState(128, 64), e2)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack: pack);
            vm.SelectAll();

            // Expand mood coverage +2
            vm.BulkExpandMoodCommand.Execute(2);
            Assert.Equal(0, vm.Entries[0].MinButthurt);
            Assert.Equal(6, vm.Entries[0].MaxButthurt);
            Assert.Equal(5, vm.Entries[1].MinButthurt);
            Assert.Equal(10, vm.Entries[1].MaxButthurt);

            // Shrink mood coverage -1
            vm.BulkShrinkMoodCommand.Execute(1);
            Assert.Equal(0, vm.Entries[0].MinButthurt);
            Assert.Equal(5, vm.Entries[0].MaxButthurt);
            Assert.Equal(5, vm.Entries[1].MinButthurt);
            Assert.Equal(9, vm.Entries[1].MaxButthurt);

            // Expand past 14 clamps to 14
            vm.BulkExpandMoodCommand.Execute(10);
            Assert.Equal(14, vm.Entries[0].MaxButthurt);
            Assert.Equal(14, vm.Entries[1].MaxButthurt);
        }

        [Fact]
        public void GalleryCardSize_UpdatesDimensionsAndBooleansCorrectly()
        {
            var vm = new FlipperScheduleMatrixViewModel();

            // Default: Standard
            Assert.Equal("Standard", vm.GalleryCardSize);
            Assert.Equal(260, vm.GalleryCardWidth);
            Assert.Equal(245, vm.GalleryCardHeight);
            Assert.Equal(90, vm.GalleryThumbnailHeight);
            Assert.True(vm.IsStandardGallerySize);
            Assert.False(vm.IsCompactGallerySize);
            Assert.False(vm.IsLargeGallerySize);

            // Switch to Compact
            vm.SetGalleryCardSizeCommand.Execute("Compact");
            Assert.Equal("Compact", vm.GalleryCardSize);
            Assert.Equal(200, vm.GalleryCardWidth);
            Assert.Equal(195, vm.GalleryCardHeight);
            Assert.Equal(70, vm.GalleryThumbnailHeight);
            Assert.True(vm.IsCompactGallerySize);

            // Switch to Large
            vm.SetGalleryCardSizeCommand.Execute("Large");
            Assert.Equal("Large", vm.GalleryCardSize);
            Assert.Equal(330, vm.GalleryCardWidth);
            Assert.Equal(300, vm.GalleryCardHeight);
            Assert.Equal(135, vm.GalleryThumbnailHeight);
            Assert.True(vm.IsLargeGallerySize);
        }

        [Fact]
        public void HasFilteredEntries_ReflectsFilterResults()
        {
            var e1 = new FlipperManifestEntry { Name = "anim_baby", MinLevel = 1, MaxLevel = 9 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_baby", new SpriteState(128, 64), e1)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack: pack);
            Assert.True(vm.HasFilteredEntries);
            Assert.False(vm.HasNoFilteredEntries);
            Assert.Equal("Showing 1 of 1 animations", vm.FilteredEntriesCountText);

            // Filter with unmatched query
            vm.SearchFilterText = "non_existent_xyz";
            Assert.False(vm.HasFilteredEntries);
            Assert.True(vm.HasNoFilteredEntries);
            Assert.Equal("Showing 0 of 1 animations", vm.FilteredEntriesCountText);

            // Reset filters
            vm.ResetFiltersCommand.Execute(null);
            Assert.True(vm.HasFilteredEntries);
            Assert.False(vm.HasNoFilteredEntries);
        }

        [Fact]
        public void Entry_StepNextPreviewFrame_And_ResetPreviewFrame_CyclesThumbnails()
        {
            var sprite = new SpriteState(128, 64);
            sprite.Frames.Add(new FrameState { Name = "F1" }); // 2 frames total
            var entry = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "anim_multi" });

            var f0 = FlipperScheduleMatrixViewModel.RenderSpriteThumbnail(sprite, 0);
            var f1 = FlipperScheduleMatrixViewModel.RenderSpriteThumbnail(sprite, 1);
            entry.CachedFrameThumbnails = [f0, f1];
            entry.SpriteThumbnail = f0;

            Assert.Equal(f0, entry.ActivePreviewThumbnail);
            Assert.False(entry.IsPreviewPlaying);

            // Start playing & step
            entry.IsPreviewPlaying = true;
            entry.StepNextPreviewFrame();
            Assert.Equal(1, entry.CurrentPreviewFrame);
            Assert.Equal(f1, entry.ActivePreviewThumbnail);

            // Step back around to frame 0
            entry.StepNextPreviewFrame();
            Assert.Equal(0, entry.CurrentPreviewFrame);
            Assert.Equal(f0, entry.ActivePreviewThumbnail);

            // Reset
            entry.StepNextPreviewFrame();
            entry.ResetPreviewFrame();
            Assert.Equal(0, entry.CurrentPreviewFrame);
            Assert.False(entry.IsPreviewPlaying);
            Assert.Equal(f0, entry.ActivePreviewThumbnail);
        }

        [Fact]
        public void RecalculateMatrix_MultiFrameSprite_PopulatesCachedFrameThumbnailsAutomatically()
        {
            var multiSprite = new SpriteState(128, 64) { FrameRateFps = 10 };
            multiSprite.Frames.Add(new FrameState { Name = "F2" });
            multiSprite.Frames.Add(new FrameState { Name = "F3" }); // 3 frames total

            var entry = new FlipperManifestEntry { Name = "anim_triple", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_triple", multiSprite, entry)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack: pack);
            Assert.Single(vm.Entries);

            var entryVm = vm.Entries[0];
            Assert.NotNull(entryVm.CachedFrameThumbnails);
            Assert.Equal(3, entryVm.CachedFrameThumbnails.Count);
            Assert.True(entryVm.HasMultipleFrames);

            // Verify hover play
            entryVm.IsPreviewPlaying = true;
            Assert.True(entryVm.IsPreviewPlaying);

            entryVm.StepNextPreviewFrame();
            Assert.Equal(1, entryVm.CurrentPreviewFrame);
            Assert.Equal(entryVm.CachedFrameThumbnails[1], entryVm.ActivePreviewThumbnail);

            entryVm.ResetPreviewFrame();
            Assert.Equal(0, entryVm.CurrentPreviewFrame);
            Assert.False(entryVm.IsPreviewPlaying);
            Assert.Equal(entryVm.CachedFrameThumbnails[0], entryVm.ActivePreviewThumbnail);
        }

        [Fact]
        public void RecalculateMatrix_SingleFrameSprite_GuardsIsPreviewPlaying()
        {
            var singleSprite = new SpriteState(128, 64); // 1 frame default

            var entry = new FlipperManifestEntry { Name = "anim_single", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_single", singleSprite, entry)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack: pack);
            Assert.Single(vm.Entries);

            var entryVm = vm.Entries[0];
            Assert.False(entryVm.HasMultipleFrames);

            // Attempting to start hover playing on a single frame should stay false
            entryVm.IsPreviewPlaying = true;
            Assert.False(entryVm.IsPreviewPlaying);
        }

        [Fact]
        public void RecalculateMatrix_CustomFramesOrder_RendersCorrectSequence()
        {
            var multiSprite = new SpriteState(128, 64)
            {
                FlipperCycle = new FlipperAnimationCycle
                {
                    FramesOrder = [0, 1, 0, 1, 0]
                }
            };
            multiSprite.Frames.Add(new FrameState { Name = "F2" }); // 2 physical frames

            var entry = new FlipperManifestEntry { Name = "anim_custom_order", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_custom_order", multiSprite, entry)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack: pack);
            var entryVm = vm.Entries[0];

            Assert.NotNull(entryVm.CachedFrameThumbnails);
            Assert.Equal(5, entryVm.CachedFrameThumbnails.Count);
            Assert.True(entryVm.HasMultipleFrames);

            // Frame 0 and 2 should refer to the same physical thumbnail (cached)
            Assert.Same(entryVm.CachedFrameThumbnails[0], entryVm.CachedFrameThumbnails[2]);
            Assert.Same(entryVm.CachedFrameThumbnails[1], entryVm.CachedFrameThumbnails[3]);
        }

        #endregion

        #region Bug Fixes & Refinements Verification

        private class TestTabService : IWorkspaceTabService
        {
            public List<(string Title, SpriteState Sprite)> OpenSprites { get; } = [];
            public List<string> ActivatedTabs { get; } = [];
            public List<(string OldTitle, string NewTitle)> RenamedTabs { get; } = [];
            public string? LastOpenedTabName { get; private set; }
            public SpriteState? LastOpenedSprite { get; private set; }

            public void OpenSpritesInTabs(IEnumerable<SpriteState> sprites, string tabNamePrefix = "Imported")
            {
                int idx = 1;
                foreach (var s in sprites)
                {
                    OpenSpriteInTab(s, $"{tabNamePrefix} {idx++}");
                }
            }

            public void OpenSpritesInTabs(IEnumerable<(string Name, SpriteState Sprite)> sprites)
            {
                foreach (var (name, sprite) in sprites)
                {
                    OpenSpriteInTab(sprite, name);
                }
            }

            public void OpenSpriteInTab(SpriteState sprite, string title)
            {
                LastOpenedSprite = sprite;
                LastOpenedTabName = title;
                OpenSprites.Add((title, sprite));
            }

            public void OpenSpriteInTab(SpriteState sprite, string title, string? filePath)
            {
                OpenSpriteInTab(sprite, title);
            }

            public SpriteState? GetActiveSpriteState() =>
                OpenSprites.Count > 0 ? OpenSprites[0].Sprite : null;

            public bool[]? GetActiveFramePixels(bool animated = false) => null;

            public void OpenAssetPackInTab(
                IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>? pack = null,
                string packName = "Flipper Asset Pack")
            {
            }

            public bool ActivateTabByTitle(string title)
            {
                ActivatedTabs.Add(title);
                return OpenSprites.Any(s => s.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            }

            public bool RenameTab(string oldTitle, string newTitle)
            {
                RenamedTabs.Add((oldTitle, newTitle));
                for (int i = 0; i < OpenSprites.Count; i++)
                {
                    if (OpenSprites[i].Title.Equals(oldTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        OpenSprites[i] = (newTitle, OpenSprites[i].Sprite);
                    }
                }
                return true;
            }

            public IReadOnlyList<(string Title, SpriteState Sprite)> GetAllOpenSprites() => OpenSprites;

            public IReadOnlyList<(string Title, SpriteState Sprite, string? FilePath)> GetAllOpenSpritesWithPaths() =>
                OpenSprites.Select(s => (s.Title, s.Sprite, (string?)null)).ToList();

            public (string Title, SpriteState Sprite)? GetActiveSprite() =>
                OpenSprites.Count > 0 ? OpenSprites[0] : null;
        }

        [Fact]
        public void SyncFromWorkspace_WhenExistingTabUpdated_RecalculatesMatrixAndUpdatesThumbnails()
        {
            var tabService = new TestTabService();
            var initialSprite = new SpriteState(128, 64);
            tabService.OpenSprites.Add(("anim_baby", initialSprite));

            var vm = new FlipperScheduleMatrixViewModel(tabService: tabService);
            Assert.Contains(":1:1", vm.Entries[0].CachedSpriteSignature);

            // Modify sprite in open tab to have 3 frames
            var updatedSprite = new SpriteState(128, 64);
            updatedSprite.Frames.Add(new FrameState { Name = "Frame 2" });
            updatedSprite.Frames.Add(new FrameState { Name = "Frame 3" });
            tabService.OpenSprites.Clear();
            tabService.OpenSprites.Add(("anim_baby", updatedSprite));

            // Sync from workspace
            vm.SyncFromWorkspace(silent: true);

            Assert.Equal(3, vm.AnimationSprites["anim_baby"].Frames.Count);
            Assert.Contains(":3:1", vm.Entries[0].CachedSpriteSignature);
        }

        [Fact]
        public void BulkDuplicate_PreservesSpriteArtworkAndFilePaths()
        {
            var initialSprite = new SpriteState(128, 64);
            var entry = new FlipperManifestEntry { Name = "anim_baby", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_baby", initialSprite, entry)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);
            var baby = vm.Entries[0];
            baby.IsSelected = true;

            vm.BulkDuplicate();

            var duplicated = vm.Entries.FirstOrDefault(e => e.Name.StartsWith("anim_baby_"));
            Assert.NotNull(duplicated);
            Assert.True(vm.AnimationSprites.ContainsKey(duplicated.Name));
            Assert.NotNull(vm.AnimationSprites[duplicated.Name]);
            Assert.Equal(128, vm.AnimationSprites[duplicated.Name].Width);
            Assert.Equal(64, vm.AnimationSprites[duplicated.Name].Height);
        }

        [Fact]
        public void SelectedName_DirectRename_MigratesSpriteAndPathKeys()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.SelectedEntry = vm.Entries[0];
            Assert.Equal("anim_baby", vm.SelectedName);

            vm.SelectedName = "anim_swimming";

            Assert.Equal("anim_swimming", vm.SelectedEntry.Name);
            Assert.False(vm.AnimationSprites.ContainsKey("anim_baby"));
            Assert.True(vm.AnimationSprites.ContainsKey("anim_swimming"));
        }

        [Fact]
        public void FZM001_QuickFix_PreservesExistingFramePixelsCentered()
        {
            // Create non-128x64 sprite (64x32) with a specific pixel drawn at (10, 10)
            var smallSprite = new SpriteState(64, 32);
            smallSprite.Frames[0].LayerPixels[0].GetMonochromeData()[10 * 64 + 10] = true;

            var entry = new FlipperManifestEntry { Name = "test_dimension_fix", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("test_dimension_fix", smallSprite, entry)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);
            var targetEntry = vm.Entries[0];

            // Trigger FZM001 quick fix
            vm.ExecuteQuickFix("FZM001", targetEntry);

            var normalized = vm.AnimationSprites["test_dimension_fix"];
            Assert.Equal(128, normalized.Width);
            Assert.Equal(64, normalized.Height);

            // Centering math: offX = (128 - 64) / 2 = 32, offY = (64 - 32) / 2 = 16
            // Original pixel at (10, 10) -> (32 + 10, 16 + 10) = (42, 26)
            int expectedPixelIdx = 26 * 128 + 42;
            Assert.True(normalized.Frames[0].LayerPixels[0].GetMonochromeData()[expectedPixelIdx]);
        }

        [Fact]
        public void MediaSlicer_Sub128x64Frames_CentersOnCanvas()
        {
            // Create a 64x64 bitmap with a white pixel at (0, 0)
            var wb = new System.Windows.Media.Imaging.WriteableBitmap(64, 64, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
            int[] pixels = new int[64 * 64];
            pixels[0] = unchecked((int)0xFFFFFFFF); // White at (0, 0)
            wb.WritePixels(new System.Windows.Int32Rect(0, 0, 64, 64), pixels, 64 * 4, 0);

            var settings = new MediaSliceSettings
            {
                FrameWidth = 64,
                FrameHeight = 64,
                Layout = SpriteSheetLayout.HorizontalStrip,
                DitheringAlgorithm = BitmapDitheringAlgorithm.Binary,
                BrightnessThreshold = 128
            };

            var sprite = FlipperMediaSlicerService.SliceToAnimationSprite(wb, settings);
            Assert.NotNull(sprite);
            Assert.Single(sprite.Frames);
            Assert.Equal(128, sprite.Width);
            Assert.Equal(64, sprite.Height);

            // Centering math: offX = (128 - 64) / 2 = 32, offY = (64 - 64) / 2 = 0
            // Pixel (0, 0) in 64x64 should now be at (32, 0) in 128x64
            int expectedIdx = 0 * 128 + 32;
            Assert.True(sprite.Frames[0].LayerPixels[0].GetMonochromeData()[expectedIdx]);
        }

        [Fact]
        public void Lifecycle_Dispose_DetachesCollectionChangedAndReleasesReferences()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.Equal(3, vm.Entries.Count);

            // Add an entry to ensure CollectionChanged event fired
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "extra_anim" }));
            Assert.Equal(4, vm.Entries.Count);

            vm.Dispose();

            Assert.Empty(vm.Entries);
            Assert.Empty(vm.FilteredEntries);
            Assert.Empty(vm.SelectedEntries);
            Assert.Empty(vm.AnimationSprites);
        }

        [Fact]
        public void StageEvolution_SingleAnimation_CoversAllLevelsWithoutGaps()
        {
            var entry = new FlipperManifestEntry { Name = "solo_anim", MinLevel = 5, MaxLevel = 5, MinButthurt = 2, MaxButthurt = 2, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("solo_anim", new SpriteState(128, 64), entry)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);
            Assert.Single(vm.Entries);

            vm.AutoBalanceWithStrategy(FlipperAutoBalanceStrategy.StageEvolution);

            Assert.Equal(1, vm.Entries[0].MinLevel);
            Assert.Equal(30, vm.Entries[0].MaxLevel);
            Assert.Equal(0, vm.Entries[0].MinButthurt);
            Assert.Equal(14, vm.Entries[0].MaxButthurt);
            Assert.Equal(100.0, vm.Matrix.CoveragePercentage);
            Assert.Empty(vm.Matrix.GetUncoveredCells());
        }

        [Fact]
        public void StageEvolution_TwoAnimations_CoversAllLevelsWithoutGaps()
        {
            var e1 = new FlipperManifestEntry { Name = "anim1", MinLevel = 1, MaxLevel = 1, MinButthurt = 0, MaxButthurt = 0, Weight = 1 };
            var e2 = new FlipperManifestEntry { Name = "anim2", MinLevel = 2, MaxLevel = 2, MinButthurt = 0, MaxButthurt = 0, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim1", new SpriteState(128, 64), e1),
                ("anim2", new SpriteState(128, 64), e2)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);
            Assert.Equal(2, vm.Entries.Count);

            // Extended mode (L1-30)
            vm.IsStockMode = false;
            vm.AutoBalanceWithStrategy(FlipperAutoBalanceStrategy.StageEvolution);

            Assert.Equal(1, vm.Entries[0].MinLevel);
            Assert.Equal(14, vm.Entries[0].MaxLevel);
            Assert.Equal(15, vm.Entries[1].MinLevel);
            Assert.Equal(30, vm.Entries[1].MaxLevel);
            Assert.Equal(100.0, vm.Matrix.CoveragePercentage);
            Assert.Empty(vm.Matrix.GetUncoveredCells());

            // Stock mode (L1-3)
            vm.IsStockMode = true;
            vm.AutoBalanceWithStrategy(FlipperAutoBalanceStrategy.StageEvolution);

            Assert.Equal(1, vm.Entries[0].MinLevel);
            Assert.Equal(1, vm.Entries[0].MaxLevel);
            Assert.Equal(2, vm.Entries[1].MinLevel);
            Assert.Equal(3, vm.Entries[1].MaxLevel);
            Assert.Equal(100.0, vm.Matrix.CoveragePercentage);
            Assert.Empty(vm.Matrix.GetUncoveredCells());
        }

        [Fact]
        public void BalanceByFillingGaps_AnchorsCeilingAndProvidesFullCoverage()
        {
            var e1 = new FlipperManifestEntry { Name = "anim_low", MinLevel = 5, MaxLevel = 10, MinButthurt = 2, MaxButthurt = 10, Weight = 1 };
            var e2 = new FlipperManifestEntry { Name = "anim_mid", MinLevel = 12, MaxLevel = 22, MinButthurt = 3, MaxButthurt = 12, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_low", new SpriteState(128, 64), e1),
                ("anim_mid", new SpriteState(128, 64), e2)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);
            vm.AutoBalanceWithStrategy(FlipperAutoBalanceStrategy.FillGapsOnly);

            Assert.Equal(1, vm.Entries[0].MinLevel);
            Assert.Equal(0, vm.Entries[0].MinButthurt);
            Assert.Equal(14, vm.Entries[0].MaxButthurt);
            Assert.Equal(30, vm.Entries[1].MaxLevel);
            Assert.Equal(0, vm.Entries[1].MinButthurt);
            Assert.Equal(14, vm.Entries[1].MaxButthurt);
            Assert.Equal(100.0, vm.Matrix.CoveragePercentage);
        }

        [Fact]
        public void CurrentViewMode_Transitions_ManageViewState()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.True(vm.IsMatrixStudioView);
            Assert.False(vm.IsSpriteGalleryView);

            vm.CurrentViewMode = AssetPackViewMode.SpriteGallery;
            Assert.False(vm.IsMatrixStudioView);
            Assert.True(vm.IsSpriteGalleryView);

            vm.CurrentViewMode = AssetPackViewMode.DataGridRoster;
            Assert.True(vm.IsDataGridRosterView);
            Assert.False(vm.IsSpriteGalleryView);
        }

        [Fact]
        public void ExportAllToHexp_WithCollidingAnimationNames_DisambiguatesFilenames()
        {
            var e1 = new FlipperManifestEntry { Name = "anim test", MinLevel = 1, MaxLevel = 10 };
            var e2 = new FlipperManifestEntry { Name = "anim_test", MinLevel = 11, MaxLevel = 20 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim test", new SpriteState(128, 64), e1),
                ("anim_test", new SpriteState(128, 64), e2)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);
            string outDir = Path.Combine(Path.GetTempPath(), $"hexp_export_test_{Guid.NewGuid():N}");

            try
            {
                vm.ExportAllToHexp(outDir);

                var files = Directory.GetFiles(outDir, "*.hexp");
                Assert.Equal(2, files.Length);
                var filenames = files.Select(Path.GetFileName).ToList();
                Assert.Contains("anim_test.hexp", filenames);
                Assert.Contains("anim_test_1.hexp", filenames);
            }
            finally
            {
                if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            }
        }

        [Fact]
        public void ExportSelectedToHexp_WithoutExtension_AppendsHexpExtension()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.SelectedEntry = vm.Entries[0];

            string outPath = Path.Combine(Path.GetTempPath(), $"single_export_{Guid.NewGuid():N}"); // No extension

            try
            {
                vm.ExportSelectedToHexp(outPath);

                string expectedFile = outPath + ".hexp";
                Assert.True(File.Exists(expectedFile));
            }
            finally
            {
                string expectedFile = outPath + ".hexp";
                if (File.Exists(expectedFile)) File.Delete(expectedFile);
                if (File.Exists(outPath)) File.Delete(outPath);
            }
        }

        [Fact]
        public void DeviceSimulator_ViewMode_SwitchesCorrectlyAndSyncsSimulator()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.False(vm.IsDeviceSimulatorView);

            vm.SetViewModeCommand.Execute("DeviceSimulator");
            Assert.Equal(AssetPackViewMode.DeviceSimulator, vm.CurrentViewMode);
            Assert.True(vm.IsDeviceSimulatorView);
            Assert.NotNull(vm.SimulatorViewModel);
            Assert.NotEmpty(vm.SimulatorViewModel.Animations);
        }

        [Fact]
        public void TestCellInSimulatorCommand_JumpsToStateAndSwitchesView()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.InspectCell(15, 7);

            vm.TestCellInSimulatorCommand.Execute(null);

            Assert.Equal(AssetPackViewMode.DeviceSimulator, vm.CurrentViewMode);
            Assert.True(vm.IsDeviceSimulatorView);
            Assert.Equal(15, vm.SimulatorViewModel.Level);
            Assert.Equal(7, vm.SimulatorViewModel.Mood);
        }

        [Fact]
        public void OpenCellInSimulatorCommand_WithTuple_JumpsToExactLevelAndMood()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.OpenCellInSimulatorCommand.Execute((25, 12));

            Assert.Equal(AssetPackViewMode.DeviceSimulator, vm.CurrentViewMode);
            Assert.Equal(25, vm.SimulatorViewModel.Level);
            Assert.Equal(12, vm.SimulatorViewModel.Mood);
        }

        [Fact]
        public void OpenSimulatorCommand_SwitchesToDeviceSimulatorModeAndSyncs()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.Equal(AssetPackViewMode.MatrixStudio, vm.CurrentViewMode);

            vm.OpenSimulatorCommand.Execute(null);

            Assert.Equal(AssetPackViewMode.DeviceSimulator, vm.CurrentViewMode);
            Assert.True(vm.IsDeviceSimulatorView);
            Assert.NotNull(vm.SimulatorViewModel);
        }

        [Fact]
        public void SwitchingAwayFromDeviceSimulator_PausesPlaybackTimer()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.CurrentViewMode = AssetPackViewMode.DeviceSimulator;
            Assert.True(vm.SimulatorViewModel.IsPlaying);

            vm.CurrentViewMode = AssetPackViewMode.MatrixStudio;
            Assert.False(vm.SimulatorViewModel.IsPlaying);
        }

        [Fact]
        public void ThemePalettes_ContainsDefaultPalettes_AndCanChangeTheme()
        {
            var vm = new FlipperScheduleMatrixViewModel();

            Assert.NotEmpty(vm.ThemePalettes);
            Assert.Equal(0, vm.SelectedPaletteIndex);

            // Change theme to Inverted OLED
            vm.SelectedPaletteIndex = 1;
            Assert.Equal(1, vm.SelectedPaletteIndex);

            // Change to Game Boy Green
            vm.SelectedPaletteIndex = 2;
            Assert.Equal(2, vm.SelectedPaletteIndex);
        }

        [Fact]
        public void RenderPreviewFrame_WithCustomSpriteAndTheme_RendersWithoutErrors()
        {
            var sprite = new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome, FrameRateFps = 10 };
            sprite.Frames.Clear();
            sprite.Frames.Add(new FrameState { Name = "Frame 1" });
            sprite.Frames.Add(new FrameState { Name = "Frame 2" });

            var vm = new FlipperScheduleMatrixViewModel();
            vm.SetPreviewSpriteForTest(sprite);

            vm.SelectedPaletteIndex = 3; // Cyberpunk Neon
            vm.RenderPreviewFrame();

            Assert.Equal(2, vm.PreviewTotalFrames);
            Assert.True(vm.PreviewHasMultipleFrames);
        }

        #endregion
        #endregion
        #endregion
        #endregion
    }
}
