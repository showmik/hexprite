using System;
using System.Collections.Generic;
using System.Linq;
using Hexprite.Core;
using Hexprite.ViewModels.Flipper;
using Xunit;

namespace Hexprite.Tests.ViewModels
{
    [Trait("Category", "Unit")]
    public class FlipperSelectionAndWeightChallengerTests
    {
        #region 1. Selection Bounding Boxes at Boundary Extremes (L1, L30, M0, M14)

        [Theory]
        [InlineData(1, 1, 0, 0)]      // Top-Left corner (L1, M0)
        [InlineData(1, 1, 14, 14)]    // Bottom-Left corner (L1, M14)
        [InlineData(30, 30, 0, 0)]    // Top-Right corner (L30, M0)
        [InlineData(30, 30, 14, 14)]  // Bottom-Right corner (L30, M14)
        [InlineData(1, 30, 0, 0)]     // Entire Top Row (L1-30, M0)
        [InlineData(1, 30, 14, 14)]   // Entire Bottom Row (L1-30, M14)
        [InlineData(1, 1, 0, 14)]     // Entire Left Column (L1, M0-14)
        [InlineData(30, 30, 0, 14)]   // Entire Right Column (L30, M0-14)
        [InlineData(1, 30, 0, 14)]    // Entire 450-cell Matrix
        public void SetSelectedEntryBounds_ExtendedMode_ExactBoundaryExtremes(int minL, int maxL, int minM, int maxM)
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var entry = vm.Entries.First();
            vm.SelectedEntry = entry;

            vm.SetSelectedEntryBounds(minL, maxL, minM, maxM);

            Assert.Equal(minL, entry.MinLevel);
            Assert.Equal(maxL, entry.MaxLevel);
            Assert.Equal(minM, entry.MinButthurt);
            Assert.Equal(maxM, entry.MaxButthurt);
            Assert.Equal(minL, vm.SelectedMinLevel);
            Assert.Equal(maxL, vm.SelectedMaxLevel);
            Assert.Equal(minM, vm.SelectedMinButthurt);
            Assert.Equal(maxM, vm.SelectedMaxButthurt);

            int expectedCells = (maxL - minL + 1) * (maxM - minM + 1);
            Assert.Equal(expectedCells, entry.CoverageCells);
        }

        [Theory]
        [InlineData(30, 1, 14, 0, 1, 30, 0, 14)] // Inverted both axes
        [InlineData(1, 30, 14, 0, 1, 30, 0, 14)] // Inverted Y only
        [InlineData(30, 1, 0, 14, 1, 30, 0, 14)] // Inverted X only
        [InlineData(25, 10, 12, 4, 10, 25, 4, 12)]
        public void SetSelectedEntryBounds_AutoNormalizesInvertedCoordinates(
            int inputMinL, int inputMaxL, int inputMinM, int inputMaxM,
            int expectedMinL, int expectedMaxL, int expectedMinM, int expectedMaxM)
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var entry = vm.Entries.First();
            vm.SelectedEntry = entry;

            vm.SetSelectedEntryBounds(inputMinL, inputMaxL, inputMinM, inputMaxM);

            Assert.Equal(expectedMinL, entry.MinLevel);
            Assert.Equal(expectedMaxL, entry.MaxLevel);
            Assert.Equal(expectedMinM, entry.MinButthurt);
            Assert.Equal(expectedMaxM, entry.MaxButthurt);
        }

        [Theory]
        [InlineData(-100, 100, -50, 50, 1, 30, 0, 14)] // Extreme over/under-flow
        [InlineData(0, 0, -1, -1, 1, 1, 0, 0)]          // Underflow
        [InlineData(31, 50, 15, 20, 30, 30, 14, 14)]    // Overflow
        [InlineData(-5, 0, -2, -1, 1, 1, 0, 0)]
        [InlineData(100, 200, 50, 100, 30, 30, 14, 14)]
        public void SetSelectedEntryBounds_ClampsOutOfBoundsCoordinates(
            int inputMinL, int inputMaxL, int inputMinM, int inputMaxM,
            int expectedMinL, int expectedMaxL, int expectedMinM, int expectedMaxM)
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var entry = vm.Entries.First();
            vm.SelectedEntry = entry;

            vm.SetSelectedEntryBounds(inputMinL, inputMaxL, inputMinM, inputMaxM);

            Assert.Equal(expectedMinL, entry.MinLevel);
            Assert.Equal(expectedMaxL, entry.MaxLevel);
            Assert.Equal(expectedMinM, entry.MinButthurt);
            Assert.Equal(expectedMaxM, entry.MaxButthurt);
        }

        [Fact]
        public void KeyboardRangeExpansionAndShrinkage_AtBoundaryExtremes_DoesNotViolateBounds()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var entry = vm.Entries.First();
            vm.SelectedEntry = entry;

            // Pin to Max corner (30, 14)
            vm.SetSelectedEntryBounds(30, 30, 14, 14);

            // Expanding max at boundary does nothing
            vm.ExpandSelectedEntryMaxLevel();
            Assert.Equal(30, entry.MaxLevel);
            vm.ExpandSelectedEntryMaxMood();
            Assert.Equal(14, entry.MaxButthurt);

            // Pin to Min corner (1, 0)
            vm.SetSelectedEntryBounds(1, 1, 0, 0);

            // Expanding min at boundary does nothing
            vm.ExpandSelectedEntryMinLevel();
            Assert.Equal(1, entry.MinLevel);
            vm.ExpandSelectedEntryMinMood();
            Assert.Equal(0, entry.MinButthurt);

            // Shrinking when Min == Max adjusts opposite edge gracefully
            vm.SetSelectedEntryBounds(5, 5, 5, 5);
            vm.ShrinkSelectedEntryMaxLevel();
            Assert.True(entry.MinLevel <= entry.MaxLevel);
            Assert.True(entry.MinLevel >= 1 && entry.MaxLevel <= 30);

            vm.SetSelectedEntryBounds(5, 5, 5, 5);
            vm.ShrinkSelectedEntryMaxMood();
            Assert.True(entry.MinButthurt <= entry.MaxButthurt);
            Assert.True(entry.MinButthurt >= 0 && entry.MaxButthurt <= 14);
        }

        #endregion

        #region 2. Stock Mode Boundaries (L1-3, M0-14, M0)

        [Theory]
        [InlineData(1, 1, 0, 0)]    // L1, M0
        [InlineData(2, 2, 0, 0)]    // L2, M0
        [InlineData(3, 3, 0, 0)]    // L3, M0
        [InlineData(1, 3, 0, 0)]    // L1-3, M0 (Stock row)
        [InlineData(1, 3, 0, 14)]   // L1-3, M0-14 (Entire Stock 45-state matrix)
        public void StockMode_BoundaryExtremes_EnforcesMaxLevel3(int minL, int maxL, int minM, int maxM)
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.IsStockMode = true;
            var entry = vm.Entries.First();
            vm.SelectedEntry = entry;

            Assert.Equal(3, vm.MaxAllowedLevel);
            Assert.Equal("Stock Mode (L1-3)", vm.ModeBadgeText);

            vm.SetSelectedEntryBounds(minL, maxL, minM, maxM);

            Assert.Equal(minL, entry.MinLevel);
            Assert.Equal(maxL, entry.MaxLevel);
            Assert.Equal(minM, entry.MinButthurt);
            Assert.Equal(maxM, entry.MaxButthurt);
            Assert.True(entry.MaxLevel <= 3);
        }

        [Fact]
        public void StockMode_OutOfBoundsAttempt_ClampedToLevel3()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.IsStockMode = true;
            var entry = vm.Entries.First();
            vm.SelectedEntry = entry;

            // Attempt to set bounds to level 30 while in stock mode
            vm.SetSelectedEntryBounds(1, 30, 0, 14);

            Assert.Equal(1, entry.MinLevel);
            Assert.Equal(3, entry.MaxLevel);
            Assert.Equal(0, entry.MinButthurt);
            Assert.Equal(14, entry.MaxButthurt);

            // Step beyond 3 in stock mode
            vm.SelectedMaxLevel = 10;
            Assert.Equal(3, vm.SelectedMaxLevel);
            Assert.Equal(3, entry.MaxLevel);
        }

        [Fact]
        public void ModeToggle_ClampsExistingEntriesToStockLimit()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var adult = vm.Entries.First(e => e.Name == "anim_adult");
            adult.MinLevel = 21;
            adult.MaxLevel = 30;

            Assert.False(vm.IsStockMode);
            vm.ToggleMode(); // Switch to stock mode

            Assert.True(vm.IsStockMode);
            Assert.Equal(3, vm.MaxAllowedLevel);
            Assert.True(adult.MinLevel <= 3);
            Assert.True(adult.MaxLevel <= 3);
            Assert.Equal(3, adult.MaxLevel);
        }

        [Theory]
        [InlineData("baby", 1, 1, true)]
        [InlineData("teen", 2, 2, true)]
        [InlineData("adult", 3, 3, true)]
        [InlineData("all", 1, 3, true)]
        [InlineData("baby", 1, 9, false)]
        [InlineData("teen", 10, 19, false)]
        [InlineData("adult", 20, 30, false)]
        [InlineData("all", 1, 30, false)]
        public void StagePresets_RespectActiveMode(string preset, int expectedMinL, int expectedMaxL, bool isStockMode)
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.IsStockMode = isStockMode;
            var entry = vm.Entries.First();
            vm.SelectedEntry = entry;

            vm.SetStagePresetCommand.Execute(preset);

            Assert.Equal(expectedMinL, entry.MinLevel);
            Assert.Equal(expectedMaxL, entry.MaxLevel);
        }

        #endregion

        #region 3. Weight Stress Testing (1, 10, 50, 100, 101, negative, zero)

        [Theory]
        [InlineData(1, 1)]
        [InlineData(10, 10)]
        [InlineData(50, 50)]
        [InlineData(100, 100)]
        [InlineData(101, 100)]
        [InlineData(9999, 100)]
        [InlineData(int.MaxValue, 100)]
        [InlineData(0, 1)]
        [InlineData(-1, 1)]
        [InlineData(-100, 1)]
        [InlineData(int.MinValue, 1)]
        public void Weight_Setters_StrictlyClampedTo1To100(int inputWeight, int expectedWeight)
        {
            var manifestEntry = new FlipperManifestEntry { Name = "test", Weight = 5 };
            var entryVm = new FlipperScheduleEntryViewModel(manifestEntry);

            entryVm.Weight = inputWeight;
            Assert.Equal(expectedWeight, entryVm.Weight);
            Assert.Equal(expectedWeight, manifestEntry.Weight);

            var matrixVm = new FlipperScheduleMatrixViewModel();
            matrixVm.SelectedEntry = entryVm;
            matrixVm.SelectedWeight = inputWeight;
            Assert.Equal(expectedWeight, matrixVm.SelectedWeight);
            Assert.Equal(expectedWeight, entryVm.Weight);
        }

        [Fact]
        public void WeightStepCommands_PredictableBehaviorAtExtremes()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var entry = vm.Entries.First();
            vm.SelectedEntry = entry;

            // Pin at upper bound 100
            entry.Weight = 100;
            vm.StepWeightCommand.Execute(1);
            Assert.Equal(100, entry.Weight);
            vm.StepWeightCommand.Execute(50);
            Assert.Equal(100, entry.Weight);

            // Step down from 100
            vm.StepWeightCommand.Execute(-10);
            Assert.Equal(90, entry.Weight);

            // Pin at lower bound 1
            entry.Weight = 1;
            vm.StepWeightCommand.Execute(-1);
            Assert.Equal(1, entry.Weight);
            vm.StepWeightCommand.Execute(-50);
            Assert.Equal(1, entry.Weight);

            // Step up from 1
            vm.StepWeightCommand.Execute(5);
            Assert.Equal(6, entry.Weight);
        }

        [Fact]
        public void InlineProbabilityStepButtons_UpdateCorrectEntryWeight()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var baby = vm.Entries.First(e => e.Name == "anim_baby");
            baby.Weight = 10;
            vm.RecalculateMatrix();

            vm.InspectCell(1, 0);
            var prob = vm.SelectedCellProbabilities.First(p => p.Name == "anim_baby");

            // Increase
            vm.IncreaseProbabilityWeightCommand.Execute(prob);
            Assert.Equal(11, baby.Weight);

            // Decrease
            vm.DecreaseProbabilityWeightCommand.Execute(prob);
            Assert.Equal(10, baby.Weight);

            // Bound at 1
            baby.Weight = 1;
            vm.RecalculateMatrix();
            vm.InspectCell(1, 0);
            prob = vm.SelectedCellProbabilities.First(p => p.Name == "anim_baby");
            vm.DecreaseProbabilityWeightCommand.Execute(prob);
            Assert.Equal(1, baby.Weight);
        }

        #endregion

        #region 4. Probability Distribution Sum to 100% Invariants

        [Theory]
        [InlineData(new[] { 1 })]
        [InlineData(new[] { 10 })]
        [InlineData(new[] { 100 })]
        [InlineData(new[] { 1, 1 })]
        [InlineData(new[] { 50, 50 })]
        [InlineData(new[] { 1, 99 })]
        [InlineData(new[] { 1, 10, 50, 100 })]
        [InlineData(new[] { 33, 33, 34 })]
        [InlineData(new[] { 5, 15, 25, 55 })]
        [InlineData(new[] { 10, 10, 10, 10, 10, 10, 10, 10, 10, 10 })] // 10 overlapping entries
        public void CellProbabilityDistribution_SumsTo100Percent(int[] weights)
        {
            var entries = new List<FlipperManifestEntry>();
            for (int i = 0; i < weights.Length; i++)
            {
                entries.Add(new FlipperManifestEntry
                {
                    Name = $"anim_{i}",
                    MinLevel = 1,
                    MaxLevel = 30,
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = weights[i]
                });
            }

            var matrix = new FlipperScheduleMatrix(entries, 30);
            var cell = matrix.GetCell(1, 0);
            var probs = cell.GetProbabilities();

            Assert.Equal(weights.Length, probs.Count);

            double sumProbability = probs.Sum(p => p.Probability);
            double sumPercentage = probs.Sum(p => p.Percentage);

            Assert.Equal(1.0, sumProbability, 6);
            Assert.Equal(100.0, sumPercentage, 4);

            int expectedTotalWeight = weights.Sum();
            Assert.Equal(expectedTotalWeight, cell.TotalWeight);
        }

        [Fact]
        public void EmptyCell_Probability_SafelyHandledWithoutDivisionByZero()
        {
            // Empty schedule matrix
            var matrix = new FlipperScheduleMatrix([], 30);
            var cell = matrix.GetCell(1, 0);

            Assert.False(cell.HasCoverage);
            Assert.Equal(0, cell.TotalWeight);
            Assert.Equal(0.0, cell.GetProbability("non_existent"));
            Assert.Empty(cell.GetProbabilities());

            var vm = new FlipperScheduleMatrixViewModel(pack: []);
            vm.Entries.Clear();
            vm.RecalculateMatrix();
            vm.InspectCell(1, 0);

            Assert.Empty(vm.SelectedCellProbabilities);
            Assert.Equal(0, vm.InspectedCellTotalWeight);
            Assert.Equal(0, vm.InspectedCellAnimationCount);
            Assert.Contains("0% Coverage (Gap)", vm.InspectedCellTotalWeightText);
            Assert.Contains("No animations scheduled", vm.InspectedCellBreakdownText);
        }

        [Fact]
        public void ProbabilityOrdering_SortedByProbabilityDescending_ThenByName()
        {
            var entries = new List<FlipperManifestEntry>
            {
                new FlipperManifestEntry { Name = "zeta", MinLevel = 1, MaxLevel = 1, MinButthurt = 0, MaxButthurt = 0, Weight = 20 },
                new FlipperManifestEntry { Name = "alpha", MinLevel = 1, MaxLevel = 1, MinButthurt = 0, MaxButthurt = 0, Weight = 20 },
                new FlipperManifestEntry { Name = "beta", MinLevel = 1, MaxLevel = 1, MinButthurt = 0, MaxButthurt = 0, Weight = 50 },
                new FlipperManifestEntry { Name = "gamma", MinLevel = 1, MaxLevel = 1, MinButthurt = 0, MaxButthurt = 0, Weight = 10 }
            };

            var matrix = new FlipperScheduleMatrix(entries, 30);
            var probs = matrix.GetCellProbabilities(1, 0);

            Assert.Equal(4, probs.Count);
            Assert.Equal("beta", probs[0].Name);  // 50%
            Assert.Equal("alpha", probs[1].Name); // 20% (alphabetical before zeta)
            Assert.Equal("zeta", probs[2].Name);  // 20%
            Assert.Equal("gamma", probs[3].Name); // 10%
        }

        [Fact]
        public void FullMatrix_RandomOverlappingWeights_AllCellsSatisfyProbabilityInvariants()
        {
            var rng = new Random(42);
            var entries = new List<FlipperManifestEntry>();

            // Generate 25 entries with randomized bounds and weights in 1..100
            for (int i = 0; i < 25; i++)
            {
                int l1 = rng.Next(1, 31);
                int l2 = rng.Next(1, 31);
                int m1 = rng.Next(0, 15);
                int m2 = rng.Next(0, 15);
                int weight = rng.Next(1, 101);

                entries.Add(new FlipperManifestEntry
                {
                    Name = $"anim_{i:D2}",
                    MinLevel = Math.Min(l1, l2),
                    MaxLevel = Math.Max(l1, l2),
                    MinButthurt = Math.Min(m1, m2),
                    MaxButthurt = Math.Max(m1, m2),
                    Weight = weight
                });
            }

            var matrix = new FlipperScheduleMatrix(entries, 30);

            for (int lvl = 1; lvl <= 30; lvl++)
            {
                for (int mood = 0; mood <= 14; mood++)
                {
                    var cell = matrix.GetCell(lvl, mood);
                    var probs = cell.GetProbabilities();

                    if (!cell.HasCoverage)
                    {
                        Assert.Empty(probs);
                        Assert.Equal(0, cell.TotalWeight);
                    }
                    else
                    {
                        Assert.NotEmpty(probs);
                        int expectedTotalWeight = cell.MatchingEntries.Sum(e => e.Weight);
                        Assert.Equal(expectedTotalWeight, cell.TotalWeight);

                        double sumProb = probs.Sum(p => p.Probability);
                        double sumPct = probs.Sum(p => p.Percentage);

                        Assert.Equal(1.0, sumProb, 5);
                        Assert.Equal(100.0, sumPct, 3);

                        // Ensure probabilities are ordered descending
                        for (int k = 0; k < probs.Count - 1; k++)
                        {
                            Assert.True(probs[k].Probability >= probs[k + 1].Probability);
                        }
                    }
                }
            }
        }

        #endregion

        #region 5. Keyboard and Multi-Step Boundary Sequences

        [Fact]
        public void MultiStepKeyboardSequences_StayWithinMatrixBounds()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var entry = vm.Entries.First();
            vm.SelectedEntry = entry;

            // Start at center (15, 15, 7, 7)
            vm.SetSelectedEntryBounds(15, 15, 7, 7);

            // Expand Max Level 20 times (should hit 30 and stay at 30)
            for (int i = 0; i < 20; i++)
            {
                vm.ExpandSelectedEntryMaxLevel();
            }
            Assert.Equal(30, entry.MaxLevel);

            // Expand Max Mood 20 times (should hit 14 and stay at 14)
            for (int i = 0; i < 20; i++)
            {
                vm.ExpandSelectedEntryMaxMood();
            }
            Assert.Equal(14, entry.MaxButthurt);

            // Expand Min Level downwards 20 times (should hit 1 and stay at 1)
            for (int i = 0; i < 20; i++)
            {
                vm.ExpandSelectedEntryMinLevel();
            }
            Assert.Equal(1, entry.MinLevel);

            // Expand Min Mood downwards 20 times (should hit 0 and stay at 0)
            for (int i = 0; i < 20; i++)
            {
                vm.ExpandSelectedEntryMinMood();
            }
            Assert.Equal(0, entry.MinButthurt);

            Assert.Equal(1, entry.MinLevel);
            Assert.Equal(30, entry.MaxLevel);
            Assert.Equal(0, entry.MinButthurt);
            Assert.Equal(14, entry.MaxButthurt);
            Assert.Equal(450, entry.CoverageCells);
        }

        [Fact]
        public void AdjustSelectedEntryBounds_ArbitraryDeltas_ClampsCorrectly()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var entry = vm.Entries.First();
            vm.SelectedEntry = entry;

            // Move bounds by large deltas
            vm.AdjustSelectedEntryBounds(-100, 100, -50, 50);
            Assert.Equal(1, entry.MinLevel);
            Assert.Equal(30, entry.MaxLevel);
            Assert.Equal(0, entry.MinButthurt);
            Assert.Equal(14, entry.MaxButthurt);

            // Collapse to right/bottom
            vm.AdjustSelectedEntryBounds(50, 50, 50, 50);
            Assert.Equal(30, entry.MinLevel);
            Assert.Equal(30, entry.MaxLevel);
            Assert.Equal(14, entry.MinButthurt);
            Assert.Equal(14, entry.MaxButthurt);

            // Collapse to left/top
            vm.AdjustSelectedEntryBounds(-50, -50, -50, -50);
            Assert.Equal(1, entry.MinLevel);
            Assert.Equal(1, entry.MaxLevel);
            Assert.Equal(0, entry.MinButthurt);
            Assert.Equal(0, entry.MaxButthurt);
        }

        #endregion
    }
}
