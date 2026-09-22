using System;
using System.Collections.Generic;
using System.Linq;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Hexprite.ViewModels.Flipper;
using Moq;
using Xunit;

namespace Hexprite.Tests.ViewModels
{
    [Trait("Category", "Unit")]
    public class FlipperChallengerStressTests
    {
        private class TestStateRecord
        {
            public string PackName { get; init; } = string.Empty;
            public bool IsStockMode { get; init; }
            public int? SelectedIndex { get; init; }
            public List<(string Name, int MinL, int MaxL, int MinM, int MaxM, int Weight)> Entries { get; init; } = [];

            public static TestStateRecord Capture(FlipperScheduleMatrixViewModel vm)
            {
                return new TestStateRecord
                {
                    PackName = vm.PackName,
                    IsStockMode = vm.IsStockMode,
                    SelectedIndex = vm.SelectedEntry != null ? vm.Entries.IndexOf(vm.SelectedEntry) : null,
                    Entries = vm.Entries.Select(e => (e.Name, e.MinLevel, e.MaxLevel, e.MinButthurt, e.MaxButthurt, e.Weight)).ToList()
                };
            }

            public bool EqualsRecord(TestStateRecord other)
            {
                if (PackName != other.PackName || IsStockMode != other.IsStockMode || SelectedIndex != other.SelectedIndex || Entries.Count != other.Entries.Count)
                    return false;
                for (int i = 0; i < Entries.Count; i++)
                {
                    if (Entries[i] != other.Entries[i])
                        return false;
                }
                return true;
            }

            public void AssertMatches(FlipperScheduleMatrixViewModel vm, string stepDescription)
            {
                Assert.True(PackName == vm.PackName, $"[{stepDescription}] PackName mismatch: expected '{PackName}', got '{vm.PackName}'");
                Assert.True(IsStockMode == vm.IsStockMode, $"[{stepDescription}] IsStockMode mismatch: expected {IsStockMode}, got '{vm.IsStockMode}'");
                Assert.Equal(Entries.Count, vm.Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    var exp = Entries[i];
                    var act = vm.Entries[i];
                    Assert.True(exp.Name == act.Name, $"[{stepDescription}] Entry[{i}].Name mismatch: expected '{exp.Name}', got '{act.Name}'");
                    Assert.True(exp.MinL == act.MinLevel, $"[{stepDescription}] Entry[{i}].MinLevel mismatch: expected {exp.MinL}, got {act.MinLevel}");
                    Assert.True(exp.MaxL == act.MaxLevel, $"[{stepDescription}] Entry[{i}].MaxLevel mismatch: expected {exp.MaxL}, got {act.MaxLevel}");
                    Assert.True(exp.MinM == act.MinButthurt, $"[{stepDescription}] Entry[{i}].MinButthurt mismatch: expected {exp.MinM}, got {act.MinButthurt}");
                    Assert.True(exp.MaxM == act.MaxButthurt, $"[{stepDescription}] Entry[{i}].MaxButthurt mismatch: expected {exp.MaxM}, got {act.MaxButthurt}");
                    Assert.True(exp.Weight == act.Weight, $"[{stepDescription}] Entry[{i}].Weight mismatch: expected {exp.Weight}, got {act.Weight}");
                }
                if (SelectedIndex.HasValue && SelectedIndex.Value >= 0 && SelectedIndex.Value < vm.Entries.Count)
                {
                    Assert.Same(vm.Entries[SelectedIndex.Value], vm.SelectedEntry);
                }
            }
        }

        #region 1. Rapid Mixed Undo/Redo Cycles (50+ Operations)

        [Fact]
        public void RapidUndoRedo_60MixedMutations_ExactBidirectionalFidelity()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var history = new List<TestStateRecord> { TestStateRecord.Capture(vm) };

            for (int step = 1; step <= 60; step++)
            {
                var before = TestStateRecord.Capture(vm);
                switch (step % 10)
                {
                    case 0:
                        vm.AddEntry();
                        break;
                    case 1:
                        if (vm.SelectedEntry != null)
                            vm.SelectedName = $"mutated_step_{step}";
                        break;
                    case 2:
                        if (vm.SelectedEntry != null)
                            vm.SetSelectedEntryBounds(1, Math.Max(1, vm.MaxAllowedLevel / 2), 0, 7);
                        break;
                    case 3:
                        if (vm.SelectedEntry != null)
                            vm.SetSelectedEntryBounds(Math.Max(1, vm.MaxAllowedLevel / 2), vm.MaxAllowedLevel, 7, 14);
                        break;
                    case 4:
                        if (vm.SelectedEntry != null)
                            vm.SelectedWeight = (step * 7 % 99) + 1;
                        break;
                    case 5:
                        if (vm.SelectedEntry != null)
                            vm.DuplicateEntry();
                        break;
                    case 6:
                        if (vm.Entries.Count > 1)
                            vm.DeleteEntry();
                        else
                            vm.AddEntry();
                        break;
                    case 7:
                        vm.PackName = $"Pack_Version_{step}";
                        break;
                    case 8:
                        vm.ToggleMode();
                        break;
                    case 9:
                        vm.AutoBalanceWithStrategy(step % 2 == 0 ? FlipperAutoBalanceStrategy.StageEvolution : FlipperAutoBalanceStrategy.LinearLevels);
                        break;
                }

                var after = TestStateRecord.Capture(vm);
                Assert.False(before.EqualsRecord(after), $"Step {step} must perform a valid mutation.");
                history.Add(after);
            }

            Assert.True(vm.CanUndo);

            // Undo all steps back to initial state
            for (int i = history.Count - 1; i >= 1; i--)
            {
                vm.Undo();
                var expected = history[i - 1];
                expected.AssertMatches(vm, $"Undo step {i - 1}");
            }

            Assert.False(vm.CanUndo);
            Assert.True(vm.CanRedo);

            // Redo all steps forward back to latest state
            for (int i = 1; i < history.Count; i++)
            {
                vm.Redo();
                var expected = history[i];
                expected.AssertMatches(vm, $"Redo step {i}");
            }

            Assert.False(vm.CanRedo);
        }

        [Fact]
        public void UndoBranching_TruncatesRedoStackCorrectly()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.Equal(3, vm.Entries.Count);

            // 1. Perform 10 operations
            for (int i = 1; i <= 10; i++)
            {
                vm.AddEntry();
            }
            Assert.Equal(13, vm.Entries.Count);

            // 2. Undo 5 times
            for (int i = 0; i < 5; i++)
            {
                vm.Undo();
            }
            Assert.Equal(8, vm.Entries.Count);
            Assert.True(vm.CanRedo);

            // 3. Perform a new mutation (branch)
            vm.SelectedWeight = 88;
            Assert.False(vm.CanRedo, "Redo stack must be cleared after a new mutation!");

            // 4. Undo and verify it restores state before weight change
            vm.Undo();
            Assert.Equal(1, vm.SelectedWeight);
            Assert.Equal(8, vm.Entries.Count);
        }

        #endregion

        #region 2. Boundary Clamping & No-Op Stack Bloat Resistance

        [Theory]
        [InlineData(int.MinValue, 1)]
        [InlineData(-1000, 1)]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(15, 15)]
        [InlineData(30, 30)]
        [InlineData(31, 30)]
        [InlineData(100000, 30)]
        [InlineData(int.MaxValue, 30)]
        public void MinLevel_ExtremeInputs_ClampsSafely(int input, int expected)
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.SelectedMinLevel = input;
            Assert.Equal(expected, vm.SelectedMinLevel);
        }

        [Theory]
        [InlineData(int.MinValue, 1)]
        [InlineData(-1000, 1)]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(15, 15)]
        [InlineData(30, 30)]
        [InlineData(31, 30)]
        [InlineData(100000, 30)]
        [InlineData(int.MaxValue, 30)]
        public void MaxLevel_ExtremeInputs_ClampsSafely(int input, int expected)
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.SelectedMaxLevel = input;
            Assert.Equal(expected, vm.SelectedMaxLevel);
        }

        [Theory]
        [InlineData(int.MinValue, 0)]
        [InlineData(-100, 0)]
        [InlineData(0, 0)]
        [InlineData(7, 7)]
        [InlineData(14, 14)]
        [InlineData(15, 14)]
        [InlineData(int.MaxValue, 14)]
        public void Moods_ExtremeInputs_ClampsSafely(int input, int expected)
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.SelectedMinButthurt = input;
            Assert.Equal(expected, vm.SelectedMinButthurt);

            vm.SelectedMaxButthurt = input;
            Assert.Equal(expected, vm.SelectedMaxButthurt);
        }

        [Theory]
        [InlineData(int.MinValue, 1)]
        [InlineData(-500, 1)]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(50, 50)]
        [InlineData(100, 100)]
        [InlineData(101, 100)]
        [InlineData(int.MaxValue, 100)]
        public void Weight_ExtremeInputs_ClampsSafely(int input, int expected)
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.SelectedWeight = input;
            Assert.Equal(expected, vm.SelectedWeight);
        }

        [Fact]
        public void InvertedRanges_SetSelectedEntryBounds_NormalizesAndClamps()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            // Inverted: minL=28, maxL=3; minM=14, maxM=2
            vm.SetSelectedEntryBounds(28, 3, 14, 2);

            Assert.Equal(3, vm.SelectedMinLevel);
            Assert.Equal(28, vm.SelectedMaxLevel);
            Assert.Equal(2, vm.SelectedMinButthurt);
            Assert.Equal(14, vm.SelectedMaxButthurt);
        }

        [Fact]
        public void NoOpEdits_DoNotPushUndoSnapshots()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.False(vm.CanUndo);

            // 1. Setting same MinLevel
            int currentMinL = vm.SelectedMinLevel;
            vm.SelectedMinLevel = currentMinL;
            Assert.False(vm.CanUndo, "Setting identical MinLevel must not push undo state.");

            // 2. Setting out-of-range value that clamps to existing value
            vm.SelectedMinLevel = -999; // clamps to 1, which is currentMinL
            Assert.False(vm.CanUndo, "Setting out-of-range value that clamps to same value must not push undo state.");

            // 3. Setting same MaxLevel
            int currentMaxL = vm.SelectedMaxLevel;
            vm.SelectedMaxLevel = currentMaxL;
            Assert.False(vm.CanUndo, "Setting identical MaxLevel must not push undo state.");

            // Switch to anim_adult (where MaxLevel is 30) to test upper boundary clamping
            vm.SelectedEntry = vm.Entries[2];
            Assert.Equal(30, vm.SelectedMaxLevel);
            vm.SelectedMaxLevel = 99999; // clamps to 30, which is current MaxLevel
            Assert.False(vm.CanUndo, "Setting out-of-range MaxLevel that clamps to existing MaxLevel must not push undo state.");

            // 4. Setting same MinMood / MaxMood
            vm.SelectedMinButthurt = 0;
            vm.SelectedMinButthurt = -50;
            vm.SelectedMaxButthurt = 14;
            vm.SelectedMaxButthurt = 500;
            Assert.False(vm.CanUndo, "Setting identical/clamped Mood must not push undo state.");

            // 5. Setting same Weight
            vm.SelectedWeight = 1;
            vm.SelectedWeight = -10;
            Assert.False(vm.CanUndo, "Setting identical/clamped Weight must not push undo state.");

            // 6. Stepping weight beyond bounds (1 to 100)
            vm.StepEntryWeight(vm.SelectedEntry.Name, -1); // already 1
            Assert.False(vm.CanUndo, "Stepping weight below 1 must not push undo state.");

            vm.SelectedWeight = 100;
            Assert.True(vm.CanUndo); // One valid push
            vm.Undo();
            Assert.False(vm.CanUndo);

            // 7. Setting same PackName
            string currentPack = vm.PackName;
            vm.PackName = currentPack;
            Assert.False(vm.CanUndo, "Setting identical PackName must not push undo state.");

            // 8. Setting same IsStockMode
            bool currentStock = vm.IsStockMode;
            vm.IsStockMode = currentStock;
            Assert.False(vm.CanUndo, "Setting identical IsStockMode must not push undo state.");

            // 9. Setting identical bounds via SetSelectedEntryBounds
            vm.SetSelectedEntryBounds(vm.SelectedMinLevel, vm.SelectedMaxLevel, vm.SelectedMinButthurt, vm.SelectedMaxButthurt);
            Assert.False(vm.CanUndo, "SetSelectedEntryBounds with identical bounds must not push undo state.");
        }

        #endregion

        #region 3. Duplicate Entries & Multi-Entry Mutations Under High Load

        [Fact]
        public void DuplicateEntry_ClonesSpriteStateIndependently()
        {
            var sprite = new SpriteState(128, 64);
            sprite.Frames.Clear();
            sprite.Frames.Add(new FrameState { Name = "Frame A" });

            var entry = new FlipperManifestEntry { Name = "original_anim", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 5 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("original_anim", sprite, entry)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);
            Assert.Single(vm.Entries);

            // Duplicate entry
            vm.DuplicateEntry();
            Assert.Equal(2, vm.Entries.Count);
            Assert.Equal("original_anim_copy", vm.Entries[1].Name);
            Assert.Equal(5, vm.Entries[1].Weight);

            // Check that sprite cache has both entries and they are distinct instances
            Assert.True(vm.AnimationSprites.ContainsKey("original_anim"));
            Assert.True(vm.AnimationSprites.ContainsKey("original_anim_copy"));

            var originalSprite = vm.AnimationSprites["original_anim"];
            var copySprite = vm.AnimationSprites["original_anim_copy"];
            Assert.NotSame(originalSprite, copySprite);

            // Mutating copy sprite should not affect original
            copySprite.Frames.Add(new FrameState { Name = "Frame B" });
            Assert.Single(originalSprite.Frames);
            Assert.Equal(2, copySprite.Frames.Count);
        }

        [Fact]
        public void HighLoad_100Entries_AutoBalanceAndDiagnostics_PerformCleanly()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.Entries.Clear();

            // Populate 100 entries with mixed valid and invalid states
            for (int i = 1; i <= 100; i++)
            {
                vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry
                {
                    Name = i % 10 == 0 ? "duplicate_name" : $"anim_{i}",
                    MinLevel = i % 3 == 0 ? 0 : (i % 30) + 1, // some invalid min levels
                    MaxLevel = i % 4 == 0 ? 99 : (i % 30) + 1, // some invalid max levels
                    MinButthurt = i % 5 == 0 ? -5 : (i % 15),
                    MaxButthurt = i % 6 == 0 ? 25 : (i % 15),
                    Weight = i % 7 == 0 ? 0 : (i % 100) + 1
                }));
            }

            vm.RecalculateMatrix();

            // 1. Check that diagnostics correctly flagged anomalies
            Assert.True(vm.HasValidationIssues);
            Assert.True(vm.ValidationDiagnostics.Count > 0);

            // 2. Run FixAllDiagnostics
            vm.FixAllDiagnostics();

            // 3. Verify all bounds, weights, and names are sanitized
            Assert.All(vm.Entries, e =>
            {
                Assert.False(string.IsNullOrWhiteSpace(e.Name));
                Assert.InRange(e.MinLevel, 1, vm.MaxAllowedLevel);
                Assert.InRange(e.MaxLevel, 1, vm.MaxAllowedLevel);
                Assert.True(e.MinLevel <= e.MaxLevel);
                Assert.InRange(e.MinButthurt, 0, 14);
                Assert.InRange(e.MaxButthurt, 0, 14);
                Assert.True(e.MinButthurt <= e.MaxButthurt);
                Assert.InRange(e.Weight, 1, 100);
            });

            // Verify no duplicate names remain
            var uniqueNames = new HashSet<string>(vm.Entries.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
            Assert.Equal(vm.Entries.Count, uniqueNames.Count);

            // 4. Test AutoBalance with 100 entries across all 4 strategies
            var strategies = Enum.GetValues<FlipperAutoBalanceStrategy>();
            foreach (var strat in strategies)
            {
                vm.AutoBalanceWithStrategy(strat);
                Assert.Equal(100, vm.Entries.Count);
                Assert.All(vm.Entries, e =>
                {
                    Assert.InRange(e.MinLevel, 1, vm.MaxAllowedLevel);
                    Assert.InRange(e.MaxLevel, 1, vm.MaxAllowedLevel);
                    Assert.True(e.MinLevel <= e.MaxLevel);
                });
                Assert.InRange(vm.Matrix.CoveragePercentage, 0.0, 100.0);
            }

            // 5. Test Undo/Redo across autobalances
            Assert.True(vm.CanUndo);
            for (int i = 0; i < strategies.Length; i++)
            {
                vm.Undo();
            }
            Assert.Equal(100, vm.Entries.Count);
        }

        [Fact]
        public void StockMode_WithExtremeLevelEntries_AutoClampsAndRestoresOnUndo()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.Entries.Clear();
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "lvl1_9", MinLevel = 1, MaxLevel = 9, MinButthurt = 0, MaxButthurt = 14, Weight = 1 }));
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "lvl10_19", MinLevel = 10, MaxLevel = 19, MinButthurt = 0, MaxButthurt = 14, Weight = 1 }));
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "lvl20_30", MinLevel = 20, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 }));
            vm.RecalculateMatrix();

            // Toggle to stock mode
            vm.ToggleMode();
            Assert.True(vm.IsStockMode);
            Assert.Equal(3, vm.MaxAllowedLevel);
            Assert.All(vm.Entries, e => Assert.InRange(e.MaxLevel, 1, 3));

            // Undo back to extended mode
            vm.Undo();
            Assert.False(vm.IsStockMode);
            Assert.Equal(30, vm.MaxAllowedLevel);
            Assert.Equal(1, vm.Entries[0].MinLevel);
            Assert.Equal(9, vm.Entries[0].MaxLevel);
            Assert.Equal(10, vm.Entries[1].MinLevel);
            Assert.Equal(19, vm.Entries[1].MaxLevel);
            Assert.Equal(20, vm.Entries[2].MinLevel);
            Assert.Equal(30, vm.Entries[2].MaxLevel);
        }

        #endregion
    }
}
