using System;
using System.Collections.Generic;
using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FlipperScheduleMatrixEditorTests
    {
        [Fact]
        public void AutoBalanceEntries_SingleAnimation_Covers100Percent()
        {
            var entries = new List<FlipperManifestEntry>
            {
                new() { Name = "anim_solo", MinLevel = 5, MaxLevel = 10, MinButthurt = 2, MaxButthurt = 4, Weight = 1 }
            };

            var balanced = FlipperScheduleMatrix.AutoBalanceEntries(entries);
            var matrix = new FlipperScheduleMatrix(balanced);

            Assert.Equal(450, matrix.CoveredCellsCount);
            Assert.Equal(100.0, matrix.CoveragePercentage);
            Assert.Empty(matrix.GetUncoveredCells());
        }

        [Fact]
        public void AutoBalanceEntries_ThreeAnimations_CoversBabyTeenAdult100Percent()
        {
            var entries = new List<FlipperManifestEntry>
            {
                new() { Name = "baby", MinLevel = 1, MaxLevel = 5, MinButthurt = 0, MaxButthurt = 5, Weight = 1 },
                new() { Name = "teen", MinLevel = 6, MaxLevel = 10, MinButthurt = 0, MaxButthurt = 5, Weight = 1 },
                new() { Name = "adult", MinLevel = 11, MaxLevel = 15, MinButthurt = 0, MaxButthurt = 5, Weight = 1 }
            };

            var balanced = FlipperScheduleMatrix.AutoBalanceEntries(entries);
            var matrix = new FlipperScheduleMatrix(balanced);

            Assert.Equal(450, matrix.CoveredCellsCount);
            Assert.Equal(100.0, matrix.CoveragePercentage);
            Assert.Empty(matrix.GetUncoveredCells());
        }

        [Fact]
        public void AutoBalanceEntries_FourAnimations_IncludesHighMoodOverride100Percent()
        {
            var entries = new List<FlipperManifestEntry>
            {
                new() { Name = "anim_1", MinLevel = 1, MaxLevel = 1, MinButthurt = 0, MaxButthurt = 0 },
                new() { Name = "anim_2", MinLevel = 1, MaxLevel = 1, MinButthurt = 0, MaxButthurt = 0 },
                new() { Name = "anim_3", MinLevel = 1, MaxLevel = 1, MinButthurt = 0, MaxButthurt = 0 },
                new() { Name = "anim_4", MinLevel = 1, MaxLevel = 1, MinButthurt = 0, MaxButthurt = 0 }
            };

            var balanced = FlipperScheduleMatrix.AutoBalanceEntries(entries);
            var matrix = new FlipperScheduleMatrix(balanced);

            Assert.Equal(450, matrix.CoveredCellsCount);
            Assert.Equal(100.0, matrix.CoveragePercentage);
            Assert.Empty(matrix.GetUncoveredCells());
        }

        [Theory]
        [InlineData(35)]
        [InlineData(50)]
        [InlineData(100)]
        public void AutoBalanceEntries_LargeCountBeyond30_GuaranteesValidRangesAndCoverage(int count)
        {
            var entries = new List<FlipperManifestEntry>();
            for (int i = 0; i < count; i++)
            {
                entries.Add(new FlipperManifestEntry { Name = $"anim_{i}", MinLevel = 1, MaxLevel = 1, MinButthurt = 0, MaxButthurt = 0 });
            }

            var balanced = FlipperScheduleMatrix.AutoBalanceEntries(entries);
            Assert.Equal(count, balanced.Count);

            var matrix = new FlipperScheduleMatrix(balanced);
            Assert.Equal(450, matrix.CoveredCellsCount);
            Assert.Equal(100.0, matrix.CoveragePercentage);
            Assert.Empty(matrix.GetUncoveredCells());

            foreach (var e in balanced)
            {
                Assert.InRange(e.MinLevel, 1, 30);
                Assert.InRange(e.MaxLevel, 1, 30);
                Assert.True(e.MinLevel <= e.MaxLevel, $"MinLevel {e.MinLevel} must be <= MaxLevel {e.MaxLevel}");
                Assert.InRange(e.MinButthurt, 0, 14);
                Assert.InRange(e.MaxButthurt, 0, 14);
                Assert.True(e.MinButthurt <= e.MaxButthurt, $"MinButthurt {e.MinButthurt} must be <= MaxButthurt {e.MaxButthurt}");
            }
        }

        [Fact]
        public void AutoBalanceEntries_MoodTiers_PartitionsByHappyNeutralAngry()
        {
            var entries = new List<FlipperManifestEntry>
            {
                new() { Name = "happy" },
                new() { Name = "neutral" },
                new() { Name = "angry" }
            };

            var balanced = FlipperScheduleMatrix.AutoBalanceEntries(entries, FlipperAutoBalanceStrategy.MoodTiers, 30);
            Assert.Equal(3, balanced.Count);

            Assert.Equal(0, balanced[0].MinButthurt);
            Assert.Equal(4, balanced[0].MaxButthurt);

            Assert.Equal(5, balanced[1].MinButthurt);
            Assert.Equal(9, balanced[1].MaxButthurt);

            Assert.Equal(10, balanced[2].MinButthurt);
            Assert.Equal(14, balanced[2].MaxButthurt);
        }

        [Fact]
        public void AutoBalanceEntries_StageEvolution_PartitionsByLifeStages()
        {
            var entries = new List<FlipperManifestEntry>
            {
                new() { Name = "egg" },
                new() { Name = "kid" },
                new() { Name = "flipper" }
            };

            var balanced = FlipperScheduleMatrix.AutoBalanceEntries(entries, FlipperAutoBalanceStrategy.StageEvolution, 30);
            Assert.Equal(3, balanced.Count);

            Assert.Equal(1, balanced[0].MinLevel);
            Assert.Equal(9, balanced[0].MaxLevel);

            Assert.Equal(10, balanced[1].MinLevel);
            Assert.Equal(19, balanced[1].MaxLevel);

            Assert.Equal(20, balanced[2].MinLevel);
            Assert.Equal(30, balanced[2].MaxLevel);
        }

        [Fact]
        public void AutoBalanceEntries_StockMode_BalancesAcross3Levels()
        {
            var entries = new List<FlipperManifestEntry>
            {
                new() { Name = "stock1" },
                new() { Name = "stock2" },
                new() { Name = "stock3" }
            };

            var balanced = FlipperScheduleMatrix.AutoBalanceEntries(entries, FlipperAutoBalanceStrategy.LinearLevels, 3);
            var matrix = new FlipperScheduleMatrix(balanced, 3);

            Assert.Equal(3, matrix.MaxLevel);
            Assert.Equal(45, matrix.CoveredCellsCount);
            Assert.Equal(100.0, matrix.CoveragePercentage);
            Assert.Empty(matrix.GetUncoveredCells());
        }

        [Theory]
        [InlineData(12, 3)]
        [InlineData(50, 3)]
        [InlineData(100, 30)]
        public void AutoBalanceEntries_MoodTiers_LargeCountsAndStockMode_NeverUnderflowsMinMaxLevels(int count, int maxLevel)
        {
            var entries = new List<FlipperManifestEntry>();
            for (int i = 0; i < count; i++)
            {
                entries.Add(new FlipperManifestEntry { Name = $"mood_anim_{i}" });
            }

            var balanced = FlipperScheduleMatrix.AutoBalanceEntries(entries, FlipperAutoBalanceStrategy.MoodTiers, maxLevel);
            Assert.Equal(count, balanced.Count);

            foreach (var e in balanced)
            {
                Assert.InRange(e.MinLevel, 1, maxLevel);
                Assert.InRange(e.MaxLevel, 1, maxLevel);
                Assert.True(e.MinLevel <= e.MaxLevel, $"MinLevel {e.MinLevel} must be <= MaxLevel {e.MaxLevel}");
                Assert.InRange(e.MinButthurt, 0, 14);
                Assert.InRange(e.MaxButthurt, 0, 14);
                Assert.True(e.MinButthurt <= e.MaxButthurt, $"MinButthurt {e.MinButthurt} must be <= MaxButthurt {e.MaxButthurt}");

                // Confirm Flipper manifest validation passes with zero errors
                var diags = e.Validate(isMomentum: maxLevel > 3);
                Assert.DoesNotContain(diags, d => d.Severity == FlipperValidationSeverity.Error);
            }
        }

        [Fact]
        public void AutoBalanceEntries_FillGapsOnly_WithInternalDisjointGaps_AchievesFullCoverage()
        {
            var entries = new List<FlipperManifestEntry>
            {
                new() { Name = "baby", MinLevel = 1, MaxLevel = 5, MinButthurt = 0, MaxButthurt = 5, Weight = 1 },
                new() { Name = "teen", MinLevel = 12, MaxLevel = 18, MinButthurt = 2, MaxButthurt = 8, Weight = 1 },
                new() { Name = "adult", MinLevel = 25, MaxLevel = 28, MinButthurt = 4, MaxButthurt = 12, Weight = 1 }
            };

            var balanced = FlipperScheduleMatrix.AutoBalanceEntries(entries, FlipperAutoBalanceStrategy.FillGapsOnly, 30);
            var matrix = new FlipperScheduleMatrix(balanced, 30);

            Assert.Equal(3, balanced.Count);
            Assert.Equal(450, matrix.CoveredCellsCount);
            Assert.Equal(100.0, matrix.CoveragePercentage);
            Assert.Empty(matrix.GetUncoveredCells());
        }

        [Fact]
        public void AutoBalanceEntries_FillGapsOnly_UnsortedEntries_AchievesFullCoverage()
        {
            // Entries intentionally in reversed/unsorted level order
            var entries = new List<FlipperManifestEntry>
            {
                new() { Name = "adult", MinLevel = 25, MaxLevel = 28, MinButthurt = 4, MaxButthurt = 12, Weight = 1 },
                new() { Name = "baby", MinLevel = 1, MaxLevel = 5, MinButthurt = 0, MaxButthurt = 5, Weight = 1 },
                new() { Name = "teen", MinLevel = 12, MaxLevel = 18, MinButthurt = 2, MaxButthurt = 8, Weight = 1 }
            };

            var balanced = FlipperScheduleMatrix.AutoBalanceEntries(entries, FlipperAutoBalanceStrategy.FillGapsOnly, 30);
            var matrix = new FlipperScheduleMatrix(balanced, 30);

            Assert.Equal(3, balanced.Count);
            Assert.Equal(450, matrix.CoveredCellsCount);
            Assert.Equal(100.0, matrix.CoveragePercentage);
            Assert.Empty(matrix.GetUncoveredCells());
        }
    }
}
