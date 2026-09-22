using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels.Flipper;
using Moq;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FlipperM4AdversarialChallengerTests
    {
        #region 1. Mode Switching & Undo/Redo Invariants (No False FZ003)

        [Fact]
        public void ModeToggle_ExtendedToStockAndRevert_NoFalseFZ003Diagnostics()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            // Default has anim_baby (1-10), anim_teen (11-20), anim_adult (21-30)
            Assert.False(vm.IsStockMode);
            Assert.Equal(30, vm.MaxAllowedLevel);
            Assert.False(vm.HasValidationIssues);

            // Toggle to Stock Mode (L1-3)
            vm.ToggleMode();
            Assert.True(vm.IsStockMode);
            Assert.Equal(3, vm.MaxAllowedLevel);
            Assert.All(vm.Entries, e =>
            {
                Assert.InRange(e.MinLevel, 1, 3);
                Assert.InRange(e.MaxLevel, 1, 3);
            });

            // Modify in Stock Mode
            vm.SelectedEntry = vm.Entries[0];
            vm.SelectedWeight = 50;

            // Undo weight edit
            vm.Undo();
            Assert.Equal(1, vm.SelectedWeight);

            // Undo mode toggle -> Reverts to Extended Mode (L1-30)
            vm.Undo();
            Assert.False(vm.IsStockMode);
            Assert.Equal(30, vm.MaxAllowedLevel);
            Assert.Equal(1, vm.Entries[0].MinLevel);
            Assert.Equal(10, vm.Entries[0].MaxLevel);
            Assert.Equal(11, vm.Entries[1].MinLevel);
            Assert.Equal(20, vm.Entries[1].MaxLevel);
            Assert.Equal(21, vm.Entries[2].MinLevel);
            Assert.Equal(30, vm.Entries[2].MaxLevel);

            // Crucial check: verify NO false FZ003 diagnostics exist for entries with Level > 3
            var fz003Errors = vm.ValidationDiagnostics.Where(d => d.Code == "FZ003").ToList();
            Assert.Empty(fz003Errors);
            Assert.False(vm.HasValidationIssues);
        }

        [Fact]
        public void ModeToggle_ViaPropertyBinding_PushesUndoStateLosslessly()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.False(vm.CanUndo);

            // Set via property (simulate two-way UI binding)
            vm.IsStockMode = true;
            Assert.True(vm.CanUndo);
            Assert.Equal(3, vm.MaxAllowedLevel);

            // Undo back to extended mode
            vm.Undo();
            Assert.False(vm.IsStockMode);
            Assert.Equal(30, vm.MaxAllowedLevel);

            // Redo forward to stock mode
            vm.Redo();
            Assert.True(vm.IsStockMode);
            Assert.Equal(3, vm.MaxAllowedLevel);
        }

        [Fact]
        public void ModeToggle_RestoresSelectedCellClamping_NoOutOfBounds()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            // Inspect Level 25, Mood 10 in Extended Mode
            vm.InspectCell(25, 10);
            Assert.Equal(25, vm.SelectedCellLevel);
            Assert.Equal(10, vm.SelectedCellMood);

            // Toggle to Stock Mode (max level is 3)
            vm.ToggleMode();
            Assert.True(vm.IsStockMode);
            Assert.Equal(3, vm.MaxAllowedLevel);
            // Selected cell must be clamped to <= 3
            Assert.InRange(vm.SelectedCellLevel, 1, 3);

            // Undo back to Extended Mode
            vm.Undo();
            Assert.False(vm.IsStockMode);
            Assert.Equal(30, vm.MaxAllowedLevel);
            Assert.InRange(vm.SelectedCellLevel, 1, 30);
        }

        #endregion

        #region 2. Inspector Property Undo Recording & No-Op Resistance

        [Fact]
        public void InspectorPropertySetters_MinMaxLevel_ClampsAndPushesUndoExactlyOnce()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.SelectedEntry = vm.Entries[0]; // anim_baby (1-10)
            Assert.False(vm.CanUndo);

            // 1. Valid change: MinLevel from 1 to 4
            vm.SelectedMinLevel = 4;
            Assert.Equal(4, vm.SelectedMinLevel);
            Assert.True(vm.CanUndo);

            // 2. No-op: Setting same value
            vm.SelectedMinLevel = 4;
            vm.Undo(); // should undo step 1 back to 1
            Assert.Equal(1, vm.SelectedMinLevel);
            Assert.False(vm.CanUndo); // Stack is empty, no extra snapshot from step 2

            // 3. Valid change: MaxLevel from 10 to 15
            vm.SelectedMaxLevel = 15;
            Assert.Equal(15, vm.SelectedMaxLevel);
            Assert.True(vm.CanUndo);

            // 4. No-op: Setting clamped value that matches current MaxLevel
            vm.SelectedMaxLevel = 15;
            vm.Undo(); // undoes step 3 back to 10
            Assert.Equal(10, vm.SelectedMaxLevel);
            Assert.False(vm.CanUndo);
        }

        [Fact]
        public void InspectorPropertySetters_MoodsAndWeight_NoDuplicateSnapshots()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.SelectedEntry = vm.Entries[0]; // MinMood=0, MaxMood=14, Weight=1

            // 1. MinMood change
            vm.SelectedMinButthurt = 3;
            Assert.Equal(3, vm.SelectedMinButthurt);
            Assert.True(vm.CanUndo);

            // No-op MinMood
            vm.SelectedMinButthurt = 3;
            vm.SelectedMinButthurt = -100; // clamps to 0, which is different from 3, so changes to 0
            Assert.Equal(0, vm.SelectedMinButthurt);
            // Now set to -50 (clamps to 0 == current 0 -> no-op)
            vm.SelectedMinButthurt = -50;
            // Undo count should be 2 (from 1->3, then from 3->0)
            vm.Undo();
            Assert.Equal(3, vm.SelectedMinButthurt);
            vm.Undo();
            Assert.Equal(0, vm.SelectedMinButthurt);
            Assert.False(vm.CanUndo);

            // 2. Weight step buttons
            vm.StepEntryWeight(vm.SelectedEntry.Name, -5); // already 1, clamped to 1 -> no-op
            Assert.False(vm.CanUndo, "Stepping weight at boundary below 1 must not push undo");

            vm.StepEntryWeight(vm.SelectedEntry.Name, 10); // 1 -> 11
            Assert.Equal(11, vm.SelectedWeight);
            Assert.True(vm.CanUndo);

            vm.Undo();
            Assert.Equal(1, vm.SelectedWeight);
            Assert.False(vm.CanUndo);
        }

        [Fact]
        public void StagePresets_PushSingleUndoState_ReversibleInSingleUndo()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.SelectedEntry = vm.Entries[0]; // anim_baby (1-10)

            // Apply "Teen" preset
            vm.SetStagePresetCommand.Execute("Teen");
            Assert.Equal(10, vm.SelectedMinLevel);
            Assert.Equal(19, vm.SelectedMaxLevel);
            Assert.True(vm.CanUndo);

            // Re-apply same "Teen" preset -> no-op
            vm.SetStagePresetCommand.Execute("Teen");

            // Single undo should restore back to 1-10
            vm.Undo();
            Assert.Equal(1, vm.SelectedMinLevel);
            Assert.Equal(10, vm.SelectedMaxLevel);
            Assert.False(vm.CanUndo);
        }

        #endregion

        #region 3. State Clamping, Cache Fidelity & Quick Fix Verification

        [Fact]
        public void QuickFix_Gap_DelegatesToAutoBalance_SingleUndoReversible()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            // Clear entries to create 100% gap
            vm.Entries.Clear();
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "lone_anim", MinLevel = 5, MaxLevel = 5, MinButthurt = 5, MaxButthurt = 5, Weight = 1 }));
            vm.RecalculateMatrix();

            Assert.True(vm.Matrix.UncoveredCellsCount > 0);

            // Execute GAP quick fix
            vm.ExecuteQuickFix("GAP", null);
            Assert.True(vm.Matrix.CoveragePercentage > 0);

            // Single undo should cleanly restore lone_anim
            vm.Undo();
            Assert.Single(vm.Entries);
            Assert.Equal("lone_anim", vm.Entries[0].Name);
            Assert.Equal(5, vm.Entries[0].MinLevel);
        }

        [Fact]
        public void DuplicateEntry_IndependentSpriteStateDeepClone()
        {
            var sprite = new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome, FrameRateFps = 12 };
            sprite.Frames.Clear();
            sprite.Frames.Add(new FrameState { Name = "F1" });
            sprite.Frames.Add(new FrameState { Name = "F2" });
            sprite.FlipperCycle = new FlipperAnimationCycle { FramesOrder = [0, 1, 0], ActiveCycles = 2 };

            var entry = new FlipperManifestEntry { Name = "hero", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 10 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("hero", sprite, entry)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);
            Assert.Single(vm.Entries);

            // Duplicate
            vm.DuplicateEntry();
            Assert.Equal(2, vm.Entries.Count);
            Assert.Equal("hero_copy", vm.Entries[1].Name);

            // Verify independent sprite clone
            Assert.True(vm.AnimationSprites.ContainsKey("hero"));
            Assert.True(vm.AnimationSprites.ContainsKey("hero_copy"));

            var origSp = vm.AnimationSprites["hero"];
            var copySp = vm.AnimationSprites["hero_copy"];
            Assert.NotSame(origSp, copySp);
            Assert.Equal(2, copySp.Frames.Count);
            Assert.Equal(12, copySp.FrameRateFps);
            Assert.Equal(3, copySp.FlipperCycle.FramesOrder.Length);

            // Modifying copy should NOT mutate original
            copySp.FrameRateFps = 24;
            copySp.FlipperCycle.FramesOrder = [1, 0];
            Assert.Equal(12, origSp.FrameRateFps);
            Assert.Equal(3, origSp.FlipperCycle.FramesOrder.Length);
        }

        [Fact]
        public void ExportAssetPack_MatchesDocumentAndModeState()
        {
            var mockExport = new Mock<IFlipperExportService>();
            var vm = new FlipperScheduleMatrixViewModel(exportService: mockExport.Object)
            {
                PackName = "TestChallengerPack"
            };
            vm.SelectedEntry = vm.Entries[0];
            vm.SelectedWeight = 25;

            // Export to folder
            vm.ExportAssetPackFolder("C:\\FakeFolder");

            // Verify export service was called with proper momentum flag (true in extended mode)
            mockExport.Verify(e => e.ExportAssetPack(
                It.Is<IReadOnlyList<(SpriteState, FlipperManifestEntry, FlipperExportSettings)>>(list =>
                    list.Count == 3 &&
                    list.Any(a => a.Item2.Name == "anim_baby" && a.Item2.Weight == 25)),
                "C:\\FakeFolder",
                true),
                Times.Once);
        }

        #endregion
    }
}
