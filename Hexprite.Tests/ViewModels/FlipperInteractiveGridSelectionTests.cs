using System;
using System.Collections.Generic;
using System.Linq;
using Hexprite.Core;
using Hexprite.ViewModels.Flipper;
using Xunit;

namespace Hexprite.Tests.ViewModels
{
    [Trait("Category", "Unit")]
    public class FlipperInteractiveGridSelectionTests
    {
        #region 1. Single-Click Inspection vs Selection Bounds Invariants

        [Theory]
        [InlineData(1, 0)]
        [InlineData(5, 4)]
        [InlineData(15, 8)]
        [InlineData(25, 12)]
        [InlineData(30, 14)]
        public void InspectCell_UpdatesInspectionProperties_WithoutAlteringSelectedEntryBounds(int lvl, int mood)
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var baby = vm.Entries.First(e => e.Name == "anim_baby");
            vm.SelectedEntry = baby;

            int origMinL = baby.MinLevel;
            int origMaxL = baby.MaxLevel;
            int origMinM = baby.MinButthurt;
            int origMaxM = baby.MaxButthurt;

            vm.InspectCell(lvl, mood);

            Assert.Equal(lvl, vm.SelectedCellLevel);
            Assert.Equal(mood, vm.SelectedCellMood);
            Assert.Equal(lvl, vm.InspectedLevel);
            Assert.Equal(mood, vm.InspectedMood);

            // Active selected entry bounds remain strictly untouched
            Assert.Equal(origMinL, baby.MinLevel);
            Assert.Equal(origMaxL, baby.MaxLevel);
            Assert.Equal(origMinM, baby.MinButthurt);
            Assert.Equal(origMaxM, baby.MaxButthurt);
            Assert.Same(baby, vm.SelectedEntry);
        }

        [Fact]
        public void UpdateDragSelectionTelemetry_FormatsAccurateCoordinatesAndCellCount()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.SelectedEntry = vm.Entries.First(e => e.Name == "anim_baby");

            // Drag 1: L1-10, M0-4 => 10 * 5 = 50 cells
            vm.UpdateDragSelectionTelemetry(1, 10, 0, 4);
            Assert.Contains("Setting 'anim_baby' bounds", vm.CellHoverInfoText);
            Assert.Contains("Level 1–10", vm.CellHoverInfoText);
            Assert.Contains("Mood 0–4", vm.CellHoverInfoText);
            Assert.Contains("50 cells", vm.CellHoverInfoText);

            // Drag 2: inverted coordinates L15 to L10, M8 to M5 => 6 * 4 = 24 cells
            vm.UpdateDragSelectionTelemetry(15, 10, 8, 5);
            Assert.Contains("Level 10–15", vm.CellHoverInfoText);
            Assert.Contains("Mood 5–8", vm.CellHoverInfoText);
            Assert.Contains("24 cells", vm.CellHoverInfoText);

            // Drag 3: single cell L5 to L5, M3 to M3 => 1 cell
            vm.UpdateDragSelectionTelemetry(5, 5, 3, 3);
            Assert.Contains("Level 5–5", vm.CellHoverInfoText);
            Assert.Contains("Mood 3–3", vm.CellHoverInfoText);
            Assert.Contains("1 cell", vm.CellHoverInfoText);

            // Drag 4: In InspectRegion mode
            vm.DragMode = FlipperMatrixDragMode.InspectRegion;
            vm.UpdateDragSelectionTelemetry(2, 4, 1, 3);
            Assert.Contains("Selecting region", vm.CellHoverInfoText);
            Assert.Contains("Level 2–4", vm.CellHoverInfoText);
            Assert.Contains("Mood 1–3", vm.CellHoverInfoText);
            Assert.Contains("9 cells", vm.CellHoverInfoText);
        }

        #endregion

        #region 2. Weight Range Alignment (1..100)

        [Fact]
        public void FlipperScheduleEntryViewModel_WeightClamping_Enforces1To100Range()
        {
            var manifestEntry = new FlipperManifestEntry { Name = "test", Weight = 1 };
            var entryVm = new FlipperScheduleEntryViewModel(manifestEntry);

            // Below 1 clamped to 1
            entryVm.Weight = 0;
            Assert.Equal(1, entryVm.Weight);
            Assert.Equal(1, manifestEntry.Weight);

            entryVm.Weight = -10;
            Assert.Equal(1, entryVm.Weight);

            // Valid mid-range and high weights (1..100)
            entryVm.Weight = 25;
            Assert.Equal(25, entryVm.Weight);
            Assert.Equal(25, manifestEntry.Weight);
            Assert.Contains("Weight: 25", entryVm.WeightText);

            entryVm.Weight = 100;
            Assert.Equal(100, entryVm.Weight);
            Assert.Equal(100, manifestEntry.Weight);

            // Above 100 clamped to 100
            entryVm.Weight = 105;
            Assert.Equal(100, entryVm.Weight);
            Assert.Equal(100, manifestEntry.Weight);
        }

        [Fact]
        public void SelectedWeight_Supports1To100Range_AndRecordsUndo()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var baby = vm.Entries.First();
            vm.SelectedEntry = baby;

            Assert.False(vm.CanUndo);

            vm.SelectedWeight = 50;
            Assert.Equal(50, baby.Weight);
            Assert.Equal(50, vm.SelectedWeight);
            Assert.True(vm.CanUndo);

            vm.Undo();
            Assert.Equal(1, vm.SelectedWeight);
            Assert.Equal(1, vm.SelectedEntry?.Weight);
            Assert.True(vm.CanRedo);

            vm.Redo();
            Assert.Equal(50, vm.SelectedWeight);
            Assert.Equal(50, vm.SelectedEntry?.Weight);
        }

        [Fact]
        public void StepWeightCommand_StepsWithin1To100Limits()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var baby = vm.Entries.First();
            vm.SelectedEntry = baby;
            baby.Weight = 98;

            vm.StepWeightCommand.Execute(1);
            Assert.Equal(99, vm.SelectedWeight);

            vm.StepWeightCommand.Execute(1);
            Assert.Equal(100, vm.SelectedWeight);

            // Clamps at 100
            vm.StepWeightCommand.Execute(1);
            Assert.Equal(100, vm.SelectedWeight);

            // Steps down
            vm.StepWeightCommand.Execute(-5);
            Assert.Equal(95, vm.SelectedWeight);
        }

        #endregion

        #region 3. Cell Inspection, Total Weight & Probability Breakdown

        [Fact]
        public void CellInspection_BreakdownAndTotalWeight_ComputeCorrectly()
        {
            var entry1 = new FlipperManifestEntry { Name = "anim_a", MinLevel = 1, MaxLevel = 10, MinButthurt = 0, MaxButthurt = 5, Weight = 30 };
            var entry2 = new FlipperManifestEntry { Name = "anim_b", MinLevel = 5, MaxLevel = 15, MinButthurt = 0, MaxButthurt = 5, Weight = 70 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_a", new SpriteState(128, 64), entry1),
                ("anim_b", new SpriteState(128, 64), entry2)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);

            // Inspect L1, M0 (only anim_a)
            vm.InspectCell(1, 0);
            Assert.Equal(1, vm.InspectedCellAnimationCount);
            Assert.Equal(30, vm.InspectedCellTotalWeight);
            Assert.Contains("Total Weight: 30 (1 anim)", vm.InspectedCellTotalWeightText);
            Assert.Single(vm.SelectedCellProbabilities);
            Assert.Equal(100.0, vm.SelectedCellProbabilities[0].Percentage);

            // Inspect L8, M2 (both anim_a and anim_b overlap, total weight 100)
            vm.InspectCell(8, 2);
            Assert.Equal(2, vm.InspectedCellAnimationCount);
            Assert.Equal(100, vm.InspectedCellTotalWeight);
            Assert.Contains("Total Weight: 100 (2 anims)", vm.InspectedCellTotalWeightText);
            Assert.Equal(2, vm.SelectedCellProbabilities.Count);
            Assert.Equal("anim_b", vm.SelectedCellProbabilities[0].Name);
            Assert.Equal(70.0, vm.SelectedCellProbabilities[0].Percentage);
            Assert.Equal("anim_a", vm.SelectedCellProbabilities[1].Name);
            Assert.Equal(30.0, vm.SelectedCellProbabilities[1].Percentage);
        }

        [Fact]
        public void CellInspection_DeadzoneGap_ReportsZeroCoverage()
        {
            var entry1 = new FlipperManifestEntry { Name = "anim_a", MinLevel = 1, MaxLevel = 5, MinButthurt = 0, MaxButthurt = 5, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_a", new SpriteState(128, 64), entry1)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);

            // Inspect uncovered state L20, M10
            vm.InspectCell(20, 10);
            Assert.Equal(0, vm.InspectedCellAnimationCount);
            Assert.Equal(0, vm.InspectedCellTotalWeight);
            Assert.Contains("0% Coverage (Gap)", vm.InspectedCellTotalWeightText);
            Assert.Contains("No animations scheduled", vm.InspectedCellBreakdownText);
            Assert.False(vm.HasSelectedCellCoverage);
            Assert.True(vm.HasSelectedCellGap);
        }

        [Fact]
        public void StepEntryWeight_UpdatesWeight_AndRecalculatesCellProbabilities()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var baby = vm.Entries.First(e => e.Name == "anim_baby");
            var teen = vm.Entries.First(e => e.Name == "anim_teen");

            // Make baby and teen overlap on L5, M0
            baby.MinLevel = 1;
            baby.MaxLevel = 10;
            baby.Weight = 10;

            teen.MinLevel = 5;
            teen.MaxLevel = 15;
            teen.Weight = 10;
            vm.RecalculateMatrix();

            vm.InspectCell(5, 0);
            Assert.Equal(20, vm.InspectedCellTotalWeight);

            // Increase baby's weight by 10 via StepEntryWeight
            vm.StepEntryWeight("anim_baby", 10);
            Assert.Equal(20, baby.Weight);
            Assert.Equal(30, vm.InspectedCellTotalWeight);

            var babyProb = vm.SelectedCellProbabilities.First(p => p.Name == "anim_baby");
            var teenProb = vm.SelectedCellProbabilities.First(p => p.Name == "anim_teen");

            // 20/30 = 66.67%, 10/30 = 33.33%
            Assert.True(babyProb.Percentage > 66.0);
            Assert.True(teenProb.Percentage < 34.0);

            // Undo restores prior weights
            vm.Undo();
            Assert.Equal(10, vm.Entries.First(e => e.Name == "anim_baby").Weight);
            Assert.Equal(20, vm.InspectedCellTotalWeight);
        }

        [Fact]
        public void IncreaseAndDecreaseProbabilityWeightCommands_OperateCorrectly()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var baby = vm.Entries.First(e => e.Name == "anim_baby");
            baby.Weight = 5;
            vm.RecalculateMatrix();
            vm.InspectCell(1, 0);

            var prob = vm.SelectedCellProbabilities.First(p => p.Name == "anim_baby");

            vm.IncreaseProbabilityWeightCommand.Execute(prob);
            Assert.Equal(6, baby.Weight);

            vm.DecreaseProbabilityWeightCommand.Execute(prob);
            Assert.Equal(5, baby.Weight);
        }

        #endregion

        #region 4. Keyboard Range Navigation & Shortcuts

        [Fact]
        public void ExpandAndShrinkMaxLevel_AdjustsSelectedEntryBoundsCorrectly()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var baby = vm.Entries.First(e => e.Name == "anim_baby");
            vm.SelectedEntry = baby;
            baby.MinLevel = 1;
            baby.MaxLevel = 9;

            // Expand MaxLevel
            vm.ExpandSelectedEntryMaxLevel();
            Assert.Equal(10, baby.MaxLevel);
            Assert.Equal(1, baby.MinLevel);

            // Shrink MaxLevel
            vm.ShrinkSelectedEntryMaxLevel();
            Assert.Equal(9, baby.MaxLevel);
            Assert.Equal(1, baby.MinLevel);
        }

        [Fact]
        public void ExpandAndShrinkMinLevel_AdjustsSelectedEntryBoundsCorrectly()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var teen = vm.Entries.First(e => e.Name == "anim_teen");
            vm.SelectedEntry = teen;
            teen.MinLevel = 10;
            teen.MaxLevel = 19;

            // Expand to the left (decrement MinLevel)
            vm.ExpandSelectedEntryMinLevel();
            Assert.Equal(9, teen.MinLevel);
            Assert.Equal(19, teen.MaxLevel);

            // Shrink from the left (increment MinLevel)
            vm.ShrinkSelectedEntryMinLevel();
            Assert.Equal(10, teen.MinLevel);
            Assert.Equal(19, teen.MaxLevel);
        }

        [Fact]
        public void ExpandAndShrinkMood_AdjustsSelectedEntryBoundsCorrectly()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var baby = vm.Entries.First(e => e.Name == "anim_baby");
            vm.SelectedEntry = baby;
            baby.MinButthurt = 2;
            baby.MaxButthurt = 8;

            // Expand MaxMood
            vm.ExpandSelectedEntryMaxMood();
            Assert.Equal(9, baby.MaxButthurt);

            // Shrink MaxMood
            vm.ShrinkSelectedEntryMaxMood();
            Assert.Equal(8, baby.MaxButthurt);

            // Expand MinMood (decrement)
            vm.ExpandSelectedEntryMinMood();
            Assert.Equal(1, baby.MinButthurt);

            // Shrink MinMood (increment)
            vm.ShrinkSelectedEntryMinMood();
            Assert.Equal(2, baby.MinButthurt);
        }

        [Fact]
        public void AdjustSelectedEntryBounds_ClampsWithinAllowedMatrixLimits()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var adult = vm.Entries.First(e => e.Name == "anim_adult");
            vm.SelectedEntry = adult;
            adult.MinLevel = 20;
            adult.MaxLevel = 30;
            adult.MinButthurt = 0;
            adult.MaxButthurt = 14;

            // Try to expand beyond 30 and 14
            vm.AdjustSelectedEntryBounds(0, 10, 0, 10);
            Assert.Equal(30, adult.MaxLevel);
            Assert.Equal(14, adult.MaxButthurt);

            // Try to expand below 1 and 0
            vm.AdjustSelectedEntryBounds(-50, 0, -50, 0);
            Assert.Equal(1, adult.MinLevel);
            Assert.Equal(0, adult.MinButthurt);
        }

        #endregion

        #region 5. State Integrity & Undo/Redo across Inspector Property Edits

        [Fact]
        public void InspectorPropertyEdits_MinMaxLevelAndMood_RecordUndoSnapshots()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var entry = vm.Entries.First();
            vm.SelectedEntry = entry;

            Assert.False(vm.CanUndo);

            // 1. MinLevel edit
            vm.SelectedMinLevel = 3;
            Assert.True(vm.CanUndo);
            Assert.Equal(3, entry.MinLevel);

            // 2. MaxLevel edit
            vm.SelectedMaxLevel = 7;
            Assert.Equal(7, entry.MaxLevel);

            // 3. MinButthurt edit
            vm.SelectedMinButthurt = 2;
            Assert.Equal(2, entry.MinButthurt);

            // 4. MaxButthurt edit
            vm.SelectedMaxButthurt = 10;
            Assert.Equal(10, entry.MaxButthurt);

            // 5. Name edit
            vm.SelectedName = "renamed_anim";
            Assert.Equal("renamed_anim", entry.Name);

            // Undo all 5 edits
            vm.Undo(); // Reverts Name
            Assert.Equal("anim_baby", vm.SelectedName);

            vm.Undo(); // Reverts MaxButthurt
            Assert.Equal(14, vm.SelectedMaxButthurt);

            vm.Undo(); // Reverts MinButthurt
            Assert.Equal(0, vm.SelectedMinButthurt);

            vm.Undo(); // Reverts MaxLevel
            Assert.Equal(10, vm.SelectedMaxLevel);

            vm.Undo(); // Reverts MinLevel
            Assert.Equal(1, vm.SelectedMinLevel);

            // Redo restores them
            vm.Redo();
            Assert.Equal(3, vm.SelectedMinLevel);
        }

        #endregion

        #region 11. Nullable Selection, Drag Modes & Region Inspection

        [Fact]
        public void DeselectEntry_ClearsSelection_AndEnablesRegionMode()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.NotNull(vm.SelectedEntry);
            Assert.True(vm.HasSelectedEntry);
            Assert.False(vm.HasNoSelectedEntry);

            // Execute Deselect
            vm.DeselectEntryCommand.Execute(null);

            Assert.Null(vm.SelectedEntry);
            Assert.False(vm.HasSelectedEntry);
            Assert.True(vm.HasNoSelectedEntry);
            Assert.Contains("Region Inspect Mode", vm.ActiveTargetBannerText);
            Assert.Contains("No animation selected", vm.ActiveTargetDetailsText);
        }

        [Fact]
        public void ApplyEntryFilter_WhenSelectionExplicitlyNull_MaintainsNullSelection()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.DeselectEntry();
            Assert.Null(vm.SelectedEntry);

            // Apply filter changes
            vm.SearchFilterText = "teen";
            Assert.Null(vm.SelectedEntry);

            vm.SelectedStageFilter = "Teen";
            Assert.Null(vm.SelectedEntry);

            vm.ResetFilters();
            Assert.Null(vm.SelectedEntry);
        }

        [Fact]
        public void DragMode_ToggleAndCommand_SwitchesCorrectly()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.Equal(FlipperMatrixDragMode.AssignTarget, vm.DragMode);
            Assert.True(vm.IsAssignDragMode);
            Assert.False(vm.IsInspectDragMode);

            // Toggle
            vm.ToggleDragModeCommand.Execute(null);
            Assert.Equal(FlipperMatrixDragMode.InspectRegion, vm.DragMode);
            Assert.False(vm.IsAssignDragMode);
            Assert.True(vm.IsInspectDragMode);

            // Toggle back
            vm.ToggleDragModeCommand.Execute(null);
            Assert.Equal(FlipperMatrixDragMode.AssignTarget, vm.DragMode);

            // Set by string
            vm.SetDragModeCommand.Execute("Inspect");
            Assert.Equal(FlipperMatrixDragMode.InspectRegion, vm.DragMode);

            vm.SetDragModeCommand.Execute("Assign");
            Assert.Equal(FlipperMatrixDragMode.AssignTarget, vm.DragMode);
        }

        [Fact]
        public void SelectRegion_CalculatesCellCountAndProbabilitiesAccurately()
        {
            var vm = new FlipperScheduleMatrixViewModel();

            // Select region Level 1 to 5, Mood 0 to 4 => 5 * 5 = 25 cells
            vm.SelectRegion(1, 5, 0, 4);

            Assert.True(vm.HasSelectedRegion);
            Assert.Equal(1, vm.SelectedRegionMinLevel);
            Assert.Equal(5, vm.SelectedRegionMaxLevel);
            Assert.Equal(0, vm.SelectedRegionMinMood);
            Assert.Equal(4, vm.SelectedRegionMaxMood);
            Assert.Equal(25, vm.SelectedRegionCellCount);
            Assert.True(vm.SelectedRegionCoveragePercent >= 0);
            Assert.NotEmpty(vm.SelectedRegionSummaryText);
            Assert.NotEmpty(vm.SelectedRegionProbabilities);
        }

        [Fact]
        public void AssignSelectedEntryToRegion_UpdatesTargetAnimationBounds()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var baby = vm.Entries.First(e => e.Name == "anim_baby");
            vm.SelectedEntry = baby;

            // Select region Level 2 to 7, Mood 1 to 3
            vm.SelectRegion(2, 7, 1, 3);

            // Execute Assign Target to Region
            vm.AssignSelectedEntryToRegionCommand.Execute(null);

            Assert.Equal(2, baby.MinLevel);
            Assert.Equal(7, baby.MaxLevel);
            Assert.Equal(1, baby.MinButthurt);
            Assert.Equal(3, baby.MaxButthurt);
        }

        [Fact]
        public void ClearRegionSelection_ResetsRegionState()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.SelectRegion(3, 8, 2, 6);
            Assert.True(vm.HasSelectedRegion);

            vm.ClearRegionSelectionCommand.Execute(null);
            Assert.False(vm.HasSelectedRegion);
            Assert.Empty(vm.SelectedRegionProbabilities);
        }

        #endregion
    }
}
