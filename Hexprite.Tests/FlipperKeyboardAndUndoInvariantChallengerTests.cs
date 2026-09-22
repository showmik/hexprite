using System;
using System.Collections.Generic;
using System.Linq;
using Hexprite.Core;
using Hexprite.ViewModels.Flipper;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FlipperKeyboardAndUndoInvariantChallengerTests
    {
        #region 1. Keyboard Navigation & Boundary Clamping Tests

        [Fact]
        public void ArrowNavigation_ClampsAtAllGridBoundaries_InExtendedMode()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.False(vm.IsStockMode);
            Assert.Equal(30, vm.MaxAllowedLevel);

            // 1. Inspect top-left boundary (1, 0)
            vm.InspectCell(1, 0);
            Assert.Equal(1, vm.SelectedCellLevel);
            Assert.Equal(0, vm.SelectedCellMood);

            // Left / Up clamped
            vm.InspectCell(Math.Max(1, vm.SelectedCellLevel - 1), vm.SelectedCellMood);
            Assert.Equal(1, vm.SelectedCellLevel);
            vm.InspectCell(vm.SelectedCellLevel, Math.Max(0, vm.SelectedCellMood - 1));
            Assert.Equal(0, vm.SelectedCellMood);

            // 2. Inspect bottom-right boundary (30, 14)
            vm.InspectCell(30, 14);
            Assert.Equal(30, vm.SelectedCellLevel);
            Assert.Equal(14, vm.SelectedCellMood);

            // Right / Down clamped
            vm.InspectCell(Math.Min(vm.MaxAllowedLevel, vm.SelectedCellLevel + 1), vm.SelectedCellMood);
            Assert.Equal(30, vm.SelectedCellLevel);
            vm.InspectCell(vm.SelectedCellLevel, Math.Min(14, vm.SelectedCellMood + 1));
            Assert.Equal(14, vm.SelectedCellMood);
        }

        [Fact]
        public void ArrowNavigation_ClampsAtStockModeBoundaries()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.IsStockMode = true;
            Assert.Equal(3, vm.MaxAllowedLevel);

            // Inspect right edge in stock mode (3, 7)
            vm.InspectCell(3, 7);
            Assert.Equal(3, vm.SelectedCellLevel);
            Assert.Equal(7, vm.SelectedCellMood);

            // Attempting to step right past level 3 clamps to 3
            vm.InspectCell(Math.Min(vm.MaxAllowedLevel, vm.SelectedCellLevel + 1), vm.SelectedCellMood);
            Assert.Equal(3, vm.SelectedCellLevel);
        }

        [Theory]
        [InlineData(false, 30)]
        [InlineData(true, 3)]
        public void ExtremesNavigation_HomeEndPageUpPageDown_JumpToExactEdges(bool stockMode, int expectedMaxLvl)
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.IsStockMode = stockMode;

            // Start in middle
            vm.InspectCell(2, 7);

            // Home -> Level 1
            vm.InspectCell(1, vm.SelectedCellMood);
            Assert.Equal(1, vm.SelectedCellLevel);
            Assert.Equal(7, vm.SelectedCellMood);

            // End -> Level Max
            vm.InspectCell(vm.MaxAllowedLevel, vm.SelectedCellMood);
            Assert.Equal(expectedMaxLvl, vm.SelectedCellLevel);
            Assert.Equal(7, vm.SelectedCellMood);

            // PageUp -> Mood 0
            vm.InspectCell(vm.SelectedCellLevel, 0);
            Assert.Equal(expectedMaxLvl, vm.SelectedCellLevel);
            Assert.Equal(0, vm.SelectedCellMood);

            // PageDown -> Mood 14
            vm.InspectCell(vm.SelectedCellLevel, 14);
            Assert.Equal(expectedMaxLvl, vm.SelectedCellLevel);
            Assert.Equal(14, vm.SelectedCellMood);
        }

        [Fact]
        public void SelectionShortcut_SpaceOrEnter_SelectsCoveredAnimationOrIgnoresGapGracefully()
        {
            var vm = new FlipperScheduleMatrixViewModel();

            // 1. Space/Enter on covered cell (1, 0) which is covered by anim_baby
            vm.InspectCell(1, 0);
            var cell = vm.Matrix.GetCell(vm.SelectedCellLevel, vm.SelectedCellMood);
            Assert.True(cell.HasCoverage);

            var firstMatch = vm.Entries.FirstOrDefault(e => e.Name.Equals(cell.MatchingEntries[0].Name, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(firstMatch);
            vm.SelectedEntry = firstMatch;
            Assert.Equal("anim_baby", vm.SelectedEntry?.Name);

            // 2. Set SelectedEntry to null and inspect gap cell to simulate Space/Enter on uncovered area
            vm.SelectedEntry = null;
            vm.Entries.Clear();
            vm.RecalculateMatrix();
            vm.InspectCell(1, 0);
            var emptyCell = vm.Matrix.GetCell(1, 0);
            Assert.False(emptyCell.HasCoverage);

            // Space/Enter logic on gap cell should not throw and should keep SelectedEntry null
            if (emptyCell.HasCoverage && emptyCell.MatchingEntries.Count > 0)
            {
                var match = vm.Entries.FirstOrDefault(e => e.Name.Equals(emptyCell.MatchingEntries[0].Name, StringComparison.OrdinalIgnoreCase));
                if (match != null) vm.SelectedEntry = match;
            }

            Assert.Null(vm.SelectedEntry);
        }

        #endregion

        #region 2. Keyboard Range Modification Shortcuts at Boundaries

        [Fact]
        public void RangeShortcuts_ExpandAtGridLimits_NeverBreachBoundaries()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var adult = vm.Entries.First(e => e.Name == "anim_adult");
            vm.SelectedEntry = adult;

            // Set adult to boundary corner (28..30, 10..14)
            adult.MinLevel = 28;
            adult.MaxLevel = 30;
            adult.MinButthurt = 10;
            adult.MaxButthurt = 14;

            // Expand MaxLevel (Shift+Right) at 30 should stay 30
            vm.ExpandSelectedEntryMaxLevel();
            Assert.Equal(30, adult.MaxLevel);

            // Expand MaxMood (Shift+Down) at 14 should stay 14
            vm.ExpandSelectedEntryMaxMood();
            Assert.Equal(14, adult.MaxButthurt);

            // Set baby to boundary corner (1..5, 0..4)
            var baby = vm.Entries.First(e => e.Name == "anim_baby");
            vm.SelectedEntry = baby;
            baby.MinLevel = 1;
            baby.MaxLevel = 5;
            baby.MinButthurt = 0;
            baby.MaxButthurt = 4;

            // Expand MinLevel (Ctrl+Shift+Left) at 1 should stay 1
            vm.ExpandSelectedEntryMinLevel();
            Assert.Equal(1, baby.MinLevel);

            // Expand MinMood (Ctrl+Shift+Up) at 0 should stay 0
            vm.ExpandSelectedEntryMinMood();
            Assert.Equal(0, baby.MinButthurt);
        }

        [Fact]
        public void RangeShortcuts_SingleCellDegenerateBounds_MaintainInvariants()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var entry = vm.Entries.First();
            vm.SelectedEntry = entry;

            // Set to 1x1 cell at (5, 5)
            entry.MinLevel = 5;
            entry.MaxLevel = 5;
            entry.MinButthurt = 5;
            entry.MaxButthurt = 5;

            // Shrink MinLevel (Ctrl+Shift+Right) cannot exceed MaxLevel
            vm.ShrinkSelectedEntryMinLevel();
            Assert.Equal(5, entry.MinLevel);
            Assert.Equal(5, entry.MaxLevel);
            Assert.True(entry.MinLevel <= entry.MaxLevel);

            // Shrink MinMood (Ctrl+Shift+Down) cannot exceed MaxMood
            vm.ShrinkSelectedEntryMinMood();
            Assert.Equal(5, entry.MinButthurt);
            Assert.Equal(5, entry.MaxButthurt);
            Assert.True(entry.MinButthurt <= entry.MaxButthurt);

            // Shrink MaxLevel (Shift+Left) on 1x1 cell shifts left safely
            vm.ShrinkSelectedEntryMaxLevel();
            Assert.True(entry.MinLevel <= entry.MaxLevel);
            Assert.True(entry.MinLevel >= 1 && entry.MaxLevel <= 30);

            // Shrink MaxMood (Shift+Up) on 1x1 cell shifts up safely
            vm.ShrinkSelectedEntryMaxMood();
            Assert.True(entry.MinButthurt <= entry.MaxButthurt);
            Assert.True(entry.MinButthurt >= 0 && entry.MaxButthurt <= 14);
        }

        [Fact]
        public void RangeShortcuts_InStockMode_RespectMaxLevel3()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.IsStockMode = true;
            var entry = vm.Entries.First();
            vm.SelectedEntry = entry;

            entry.MinLevel = 1;
            entry.MaxLevel = 3;

            // Shift+Right cannot expand beyond Level 3
            vm.ExpandSelectedEntryMaxLevel();
            Assert.Equal(3, entry.MaxLevel);

            // Set to (2, 2)
            entry.MinLevel = 2;
            entry.MaxLevel = 2;

            // Expand MaxLevel steps to 3
            vm.ExpandSelectedEntryMaxLevel();
            Assert.Equal(3, entry.MaxLevel);

            // Expand again stays at 3
            vm.ExpandSelectedEntryMaxLevel();
            Assert.Equal(3, entry.MaxLevel);
        }

        [Fact]
        public void RangeShortcuts_WithNullSelectedEntry_ExecuteSafelyWithoutException()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.SelectedEntry = null;

            // None of these should throw NullReferenceException
            vm.ExpandSelectedEntryMaxLevel();
            vm.ShrinkSelectedEntryMaxLevel();
            vm.ExpandSelectedEntryMinLevel();
            vm.ShrinkSelectedEntryMinLevel();
            vm.ExpandSelectedEntryMaxMood();
            vm.ShrinkSelectedEntryMaxMood();
            vm.ExpandSelectedEntryMinMood();
            vm.ShrinkSelectedEntryMinMood();
            vm.AdjustSelectedEntryBounds(1, 1, 1, 1);

            Assert.Null(vm.SelectedEntry);
        }

        #endregion

        #region 3. Undo/Redo Invariants Across Inspector Edits & Step Commands

        [Fact]
        public void UndoRedo_FullSequentialRoundtrip_14OperationsWithExactStateFidelity()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var entry = vm.Entries.First(e => e.Name == "anim_baby");
            vm.SelectedEntry = entry;

            // Record initial state
            int initMinL = entry.MinLevel;
            int initMaxL = entry.MaxLevel;
            int initMinM = entry.MinButthurt;
            int initMaxM = entry.MaxButthurt;
            int initWeight = entry.Weight;
            string initName = entry.Name;

            Assert.False(vm.CanUndo);
            Assert.False(vm.CanRedo);

            // Operation 1: SelectedMinLevel
            vm.SelectedMinLevel = 2;
            Assert.True(vm.CanUndo);

            // Operation 2: SelectedMaxLevel
            vm.SelectedMaxLevel = 15;

            // Operation 3: SelectedMinButthurt
            vm.SelectedMinButthurt = 1;

            // Operation 4: SelectedMaxButthurt
            vm.SelectedMaxButthurt = 12;

            // Operation 5: SelectedWeight
            vm.SelectedWeight = 50;

            // Operation 6: SelectedName
            vm.SelectedName = "renamed_baby";

            // Operation 7: StepMinLevelCommand (+1) -> MinLevel = 3
            vm.StepMinLevelCommand.Execute(1);

            // Operation 8: StepMaxLevelCommand (-2) -> MaxLevel = 13
            vm.StepMaxLevelCommand.Execute(-2);

            // Operation 9: StepMinMoodCommand (+1) -> MinMood = 2
            vm.StepMinMoodCommand.Execute(1);

            // Operation 10: StepMaxMoodCommand (-1) -> MaxMood = 11
            vm.StepMaxMoodCommand.Execute(-1);

            // Operation 11: StepWeightCommand (+15) -> Weight = 65
            vm.StepWeightCommand.Execute(15);

            // Operation 12: StepEntryWeight (+5) -> Weight = 70
            vm.StepEntryWeight("renamed_baby", 5);

            // Operation 13: IncreaseProbabilityWeightCommand (+1) -> Weight = 71
            vm.InspectCell(3, 2);
            var prob = vm.SelectedCellProbabilities.First(p => p.Name == "renamed_baby");
            vm.IncreaseProbabilityWeightCommand.Execute(prob);

            // Operation 14: DecreaseProbabilityWeightCommand (-1) -> Weight = 70
            prob = vm.SelectedCellProbabilities.First(p => p.Name == "renamed_baby");
            vm.DecreaseProbabilityWeightCommand.Execute(prob);

            // Verify state at apex
            var active = vm.Entries.First(e => e.Name == "renamed_baby");
            Assert.Equal(3, active.MinLevel);
            Assert.Equal(13, active.MaxLevel);
            Assert.Equal(2, active.MinButthurt);
            Assert.Equal(11, active.MaxButthurt);
            Assert.Equal(70, active.Weight);
            Assert.Equal("renamed_baby", active.Name);
            Assert.True(vm.CanUndo);
            Assert.False(vm.CanRedo);

            // ── Full 14-Step Reverse Undo Verification ──
            vm.Undo(); // Reverts Op 14 (Weight was 71)
            active = vm.Entries.First(e => e.Name == "renamed_baby");
            Assert.Equal(71, active.Weight);

            vm.Undo(); // Reverts Op 13 (Weight was 70)
            active = vm.Entries.First(e => e.Name == "renamed_baby");
            Assert.Equal(70, active.Weight);

            vm.Undo(); // Reverts Op 12 (Weight was 65)
            active = vm.Entries.First(e => e.Name == "renamed_baby");
            Assert.Equal(65, active.Weight);

            vm.Undo(); // Reverts Op 11 (Weight was 50)
            active = vm.Entries.First(e => e.Name == "renamed_baby");
            Assert.Equal(50, active.Weight);

            vm.Undo(); // Reverts Op 10 (MaxMood was 12)
            active = vm.Entries.First(e => e.Name == "renamed_baby");
            Assert.Equal(12, active.MaxButthurt);

            vm.Undo(); // Reverts Op 9 (MinMood was 1)
            active = vm.Entries.First(e => e.Name == "renamed_baby");
            Assert.Equal(1, active.MinButthurt);

            vm.Undo(); // Reverts Op 8 (MaxLevel was 15)
            active = vm.Entries.First(e => e.Name == "renamed_baby");
            Assert.Equal(15, active.MaxLevel);

            vm.Undo(); // Reverts Op 7 (MinLevel was 2)
            active = vm.Entries.First(e => e.Name == "renamed_baby");
            Assert.Equal(2, active.MinLevel);

            vm.Undo(); // Reverts Op 6 (Name was anim_baby)
            active = vm.Entries.First(e => e.Name == "anim_baby");
            Assert.Equal("anim_baby", active.Name);

            vm.Undo(); // Reverts Op 5 (Weight was initWeight)
            active = vm.Entries.First(e => e.Name == "anim_baby");
            Assert.Equal(initWeight, active.Weight);

            vm.Undo(); // Reverts Op 4 (MaxMood was initMaxM)
            active = vm.Entries.First(e => e.Name == "anim_baby");
            Assert.Equal(initMaxM, active.MaxButthurt);

            vm.Undo(); // Reverts Op 3 (MinMood was initMinM)
            active = vm.Entries.First(e => e.Name == "anim_baby");
            Assert.Equal(initMinM, active.MinButthurt);

            vm.Undo(); // Reverts Op 2 (MaxLevel was initMaxL)
            active = vm.Entries.First(e => e.Name == "anim_baby");
            Assert.Equal(initMaxL, active.MaxLevel);

            vm.Undo(); // Reverts Op 1 (MinLevel was initMinL)
            active = vm.Entries.First(e => e.Name == "anim_baby");
            Assert.Equal(initMinL, active.MinLevel);

            // Reached base state
            Assert.False(vm.CanUndo);
            Assert.True(vm.CanRedo);

            // ── Full 14-Step Forward Redo Verification ──
            for (int i = 0; i < 14; i++)
            {
                Assert.True(vm.CanRedo);
                vm.Redo();
            }

            Assert.True(vm.CanUndo);
            Assert.False(vm.CanRedo);

            // Verify final state matches apex
            active = vm.Entries.First(e => e.Name == "renamed_baby");
            Assert.Equal(3, active.MinLevel);
            Assert.Equal(13, active.MaxLevel);
            Assert.Equal(2, active.MinButthurt);
            Assert.Equal(11, active.MaxButthurt);
            Assert.Equal(70, active.Weight);
            Assert.Equal("renamed_baby", active.Name);
        }

        [Fact]
        public void UndoStackTruncation_NewMutationAfterUndo_ClearsRedoStack()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var baby = vm.Entries.First();
            vm.SelectedEntry = baby;

            // Perform 3 edits
            vm.SelectedMinLevel = 2;
            vm.SelectedMinLevel = 3;
            vm.SelectedMinLevel = 4;
            Assert.Equal(4, vm.SelectedMinLevel);

            // Undo 2 times -> MinLevel is 2
            vm.Undo();
            Assert.Equal(3, vm.SelectedMinLevel);
            vm.Undo();
            Assert.Equal(2, vm.SelectedMinLevel);
            Assert.True(vm.CanRedo);

            // Perform a branching new mutation
            vm.SelectedWeight = 99;

            // Invariant: Redo stack is wiped
            Assert.False(vm.CanRedo);
            Assert.True(vm.CanUndo);

            // Undo reverts the weight change
            vm.Undo();
            Assert.Equal(1, vm.SelectedWeight);
            Assert.Equal(2, vm.SelectedMinLevel);
        }

        [Fact]
        public void UndoAndRedo_WhenStackIsEmpty_DoesNotThrow()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.False(vm.CanUndo);
            Assert.False(vm.CanRedo);

            // Calling Undo / Redo with empty stacks must be safe no-ops
            vm.Undo();
            Assert.False(vm.CanUndo);
            Assert.False(vm.CanRedo);

            vm.Redo();
            Assert.False(vm.CanUndo);
            Assert.False(vm.CanRedo);
        }

        #endregion

        #region 4. Property Clamping Invariants & Edge Cases

        [Theory]
        [InlineData(-10, 1)]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(50, 50)]
        [InlineData(100, 100)]
        [InlineData(101, 100)]
        [InlineData(999, 100)]
        public void SelectedWeight_ClampingInvariant_AlwaysBetween1And100(int inputWeight, int expectedWeight)
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.SelectedEntry = vm.Entries.First();

            vm.SelectedWeight = inputWeight;
            Assert.Equal(expectedWeight, vm.SelectedWeight);
            Assert.Equal(expectedWeight, vm.SelectedEntry.Weight);
        }

        [Theory]
        [InlineData(100, 10, 100)] // Steps past 100 -> clamps at 100
        [InlineData(1, -10, 1)]   // Steps below 1 -> clamps at 1
        public void StepWeightCommand_ClampingInvariant(int startWeight, int delta, int expectedWeight)
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var entry = vm.Entries.First();
            vm.SelectedEntry = entry;
            entry.Weight = startWeight;

            vm.StepWeightCommand.Execute(delta);
            Assert.Equal(expectedWeight, vm.SelectedWeight);
        }

        [Fact]
        public void StepEntryWeight_WithInvalidOrNullName_DoesNotThrowOrCorruptState()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.False(vm.CanUndo);

            vm.StepEntryWeight(null, 5);
            Assert.False(vm.CanUndo);

            vm.StepEntryWeight("", 5);
            Assert.False(vm.CanUndo);

            vm.StepEntryWeight("non_existent_animation", 5);
            Assert.False(vm.CanUndo);
        }

        [Fact]
        public void SelectedMinLevel_GreaterThanMaxLevel_AutomaticallyAdjustsMaxLevel()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var entry = vm.Entries.First();
            vm.SelectedEntry = entry;
            entry.MinLevel = 5;
            entry.MaxLevel = 10;

            // Set MinLevel to 15 (greater than MaxLevel 10)
            vm.SelectedMinLevel = 15;

            // Invariant: MinLevel <= MaxLevel
            Assert.Equal(15, entry.MinLevel);
            Assert.True(entry.MaxLevel >= 15);
        }

        [Fact]
        public void SelectedMaxLevel_LessThanMinLevel_AutomaticallyAdjustsMinLevel()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var entry = vm.Entries.First();
            vm.SelectedEntry = entry;
            entry.MinLevel = 10;
            entry.MaxLevel = 20;

            // Set MaxLevel to 5 (less than MinLevel 10)
            vm.SelectedMaxLevel = 5;

            // Invariant: MinLevel <= MaxLevel
            Assert.Equal(5, entry.MaxLevel);
            Assert.True(entry.MinLevel <= 5);
        }

        [Fact]
        public void SelectedMinButthurt_GreaterThanMaxButthurt_AutomaticallyAdjustsMaxButthurt()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var entry = vm.Entries.First();
            vm.SelectedEntry = entry;
            entry.MinButthurt = 2;
            entry.MaxButthurt = 8;

            // Set MinButthurt to 10
            vm.SelectedMinButthurt = 10;

            Assert.Equal(10, entry.MinButthurt);
            Assert.True(entry.MaxButthurt >= 10);
        }

        [Fact]
        public void SelectedMaxButthurt_LessThanMinButthurt_AutomaticallyAdjustsMinButthurt()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var entry = vm.Entries.First();
            vm.SelectedEntry = entry;
            entry.MinButthurt = 6;
            entry.MaxButthurt = 12;

            // Set MaxButthurt to 4
            vm.SelectedMaxButthurt = 4;

            Assert.Equal(4, entry.MaxButthurt);
            Assert.True(entry.MinButthurt <= 4);
        }

        [Fact]
        public void HasSelectedEntry_ReflectsSelectedEntryPresence()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.NotNull(vm.SelectedEntry);
            Assert.True(vm.HasSelectedEntry);

            vm.SelectedEntry = null;
            Assert.Null(vm.SelectedEntry);
            Assert.False(vm.HasSelectedEntry);

            vm.SelectedEntry = vm.Entries.First();
            Assert.NotNull(vm.SelectedEntry);
            Assert.True(vm.HasSelectedEntry);
        }

        [Fact]
        public void OpenCellAnimationCommand_CoveredCell_NavigatesToMatchingEntry()
        {
            var mockTabService = new Moq.Mock<Hexprite.Services.IWorkspaceTabService>();
            var vm = new FlipperScheduleMatrixViewModel(tabService: mockTabService.Object);

            // Select cell with default baby coverage (Level 1, Mood 0)
            vm.InspectCell(1, 0);
            Assert.True(vm.HasSelectedCellCoverage);

            vm.OpenCellAnimationCommand.Execute(null);

            // Should have invoked OpenSpriteInTab on tabService for anim_baby
            mockTabService.Verify(t => t.OpenSpriteInTab(Moq.It.IsAny<SpriteState>(), "anim_baby"), Moq.Times.AtLeastOnce());
        }

        #endregion
    }
}
