using System.Collections.Generic;
using Hexprite.Core;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FlipperScheduleMatrixTests
    {
        [Fact]
        public void FlipperScheduleMatrix_EmptyEntries_ReturnsZeroCoverage()
        {
            var matrix = new FlipperScheduleMatrix(null);

            Assert.Equal(0, matrix.CoveredCellsCount);
            Assert.Equal(0.0, matrix.CoveragePercentage);
            Assert.Equal(450, matrix.GetUncoveredCells().Count);
            Assert.Equal(0, matrix.MaxCollidingAnimations);
        }

        [Fact]
        public void FlipperScheduleMatrix_FullCoverageEntry_Returns100Percent()
        {
            var fullEntry = new FlipperManifestEntry
            {
                Name = "MasterAnim",
                MinLevel = 1,
                MaxLevel = 30,
                MinButthurt = 0,
                MaxButthurt = 14,
                Weight = 1,
            };

            var matrix = new FlipperScheduleMatrix([fullEntry]);

            Assert.Equal(450, matrix.CoveredCellsCount);
            Assert.Equal(100.0, matrix.CoveragePercentage);
            Assert.Empty(matrix.GetUncoveredCells());
            Assert.Equal(1, matrix.MaxCollidingAnimations);

            var cell = matrix.GetCell(15, 7);
            Assert.True(cell.HasCoverage);
            Assert.Single(cell.MatchingEntries);
            Assert.Equal(1.0, cell.GetProbability("MasterAnim"));
        }

        [Fact]
        public void FlipperScheduleMatrix_MultipleEntries_CalculatesWeightedProbabilities()
        {
            var anim1 = new FlipperManifestEntry
            {
                Name = "Happy1",
                MinLevel = 1,
                MaxLevel = 10,
                MinButthurt = 0,
                MaxButthurt = 4,
                Weight = 3,
            };

            var anim2 = new FlipperManifestEntry
            {
                Name = "Happy2",
                MinLevel = 1,
                MaxLevel = 10,
                MinButthurt = 0,
                MaxButthurt = 4,
                Weight = 1,
            };

            var matrix = new FlipperScheduleMatrix([anim1, anim2]);

            // Level 5, Mood 2 matches both
            var cell = matrix.GetCell(5, 2);
            Assert.True(cell.HasCoverage);
            Assert.Equal(2, cell.MatchingEntries.Count);
            Assert.Equal(4, cell.TotalWeight);
            Assert.Equal(0.75, cell.GetProbability("Happy1"));
            Assert.Equal(0.25, cell.GetProbability("Happy2"));

            // Level 20, Mood 10 has no coverage
            var gapCell = matrix.GetCell(20, 10);
            Assert.False(gapCell.HasCoverage);
            Assert.Empty(gapCell.MatchingEntries);
        }

        [Fact]
        public void FlipperScheduleMatrix_StockMode_Evaluates45Cells()
        {
            var animStock = new FlipperManifestEntry
            {
                Name = "StockBaby",
                MinLevel = 1,
                MaxLevel = 1,
                MinButthurt = 0,
                MaxButthurt = 14,
                Weight = 2
            };

            var matrix = new FlipperScheduleMatrix([animStock], maxLevel: 3);

            Assert.Equal(3, matrix.MaxLevel);
            Assert.Equal(45, matrix.TotalCells); // 3 * 15 = 45
            Assert.Equal(15, matrix.CoveredCellsCount);
            Assert.Equal(30, matrix.GetUncoveredCells().Count);
            Assert.Equal(33.333333333333336, matrix.CoveragePercentage, precision: 2);
        }

        [Fact]
        public void GetCellProbabilities_ReturnsSortedBreakdownWithPercentages()
        {
            var anim1 = new FlipperManifestEntry { Name = "MainAnim", MinLevel = 1, MaxLevel = 10, MinButthurt = 0, MaxButthurt = 5, Weight = 3 };
            var anim2 = new FlipperManifestEntry { Name = "RareAnim", MinLevel = 1, MaxLevel = 10, MinButthurt = 0, MaxButthurt = 5, Weight = 1 };

            var matrix = new FlipperScheduleMatrix([anim1, anim2]);
            var probs = matrix.GetCellProbabilities(3, 2);

            Assert.Equal(2, probs.Count);
            Assert.Equal("MainAnim", probs[0].Name);
            Assert.Equal(3, probs[0].Weight);
            Assert.Equal(0.75, probs[0].Probability);
            Assert.Equal(75.0, probs[0].Percentage);

            Assert.Equal("RareAnim", probs[1].Name);
            Assert.Equal(1, probs[1].Weight);
            Assert.Equal(0.25, probs[1].Probability);
            Assert.Equal(25.0, probs[1].Percentage);
        }
    }
}
