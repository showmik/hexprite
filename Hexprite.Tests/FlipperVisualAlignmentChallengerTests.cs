using System;
using System.Collections.Generic;
using System.Linq;
using Hexprite.Core;
using Hexprite.ViewModels.Flipper;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FlipperVisualAlignmentChallengerTests
    {
        #region 1. Coordinate Conversion & Invertibility Across Canvas Sizes

        public static TheoryData<double, double, bool> CanvasDimensionAndModeData => new()
        {
            // Standard
            { 600.0, 300.0, false }, // Extended mode
            { 600.0, 300.0, true },  // Stock mode
            // Small
            { 300.0, 150.0, false },
            { 300.0, 150.0, true },
            // Ultra-wide
            { 1200.0, 400.0, false },
            { 1200.0, 400.0, true },
            // Odd / prime sizes
            { 599.0, 299.0, false },
            { 601.0, 301.0, true },
            { 777.0, 333.0, false },
            // Extreme sizes
            { 100.0, 50.0, false },
            { 3840.0, 2160.0, false }
        };

        [Theory]
        [MemberData(nameof(CanvasDimensionAndModeData))]
        public void CellCenters_MapToExactLevelAndMood_WithoutDrift(double width, double height, bool isStockMode)
        {
            int maxLvl = isStockMode ? 3 : 30;
            double cellW = width / maxLvl;
            double cellH = height / 15.0;

            for (int lvl = 1; lvl <= maxLvl; lvl++)
            {
                for (int mood = 0; mood <= 14; mood++)
                {
                    // Compute center of cell
                    double centerX = (lvl - 0.5) * cellW;
                    double centerY = (mood + 0.5) * cellH;

                    // Apply formula from AssetPackEditorPanel.xaml.cs
                    int mappedLvl = Math.Clamp((int)Math.Floor(centerX / cellW) + 1, 1, maxLvl);
                    int mappedMood = Math.Clamp((int)Math.Floor(centerY / cellH), 0, 14);

                    Assert.Equal(lvl, mappedLvl);
                    Assert.Equal(mood, mappedMood);
                }
            }
        }

        [Theory]
        [MemberData(nameof(CanvasDimensionAndModeData))]
        public void CellCorners_MapToExactLevelAndMood(double width, double height, bool isStockMode)
        {
            int maxLvl = isStockMode ? 3 : 30;
            double cellW = width / maxLvl;
            double cellH = height / 15.0;
            const double eps = 1e-4;

            for (int lvl = 1; lvl <= maxLvl; lvl++)
            {
                for (int mood = 0; mood <= 14; mood++)
                {
                    // Top-left inside cell
                    double tlX = (lvl - 1) * cellW + eps;
                    double tlY = mood * cellH + eps;
                    int mappedLvlTl = Math.Clamp((int)Math.Floor(tlX / cellW) + 1, 1, maxLvl);
                    int mappedMoodTl = Math.Clamp((int)Math.Floor(tlY / cellH), 0, 14);
                    Assert.Equal(lvl, mappedLvlTl);
                    Assert.Equal(mood, mappedMoodTl);

                    // Bottom-right inside cell
                    double brX = lvl * cellW - eps;
                    double brY = (mood + 1) * cellH - eps;
                    int mappedLvlBr = Math.Clamp((int)Math.Floor(brX / cellW) + 1, 1, maxLvl);
                    int mappedMoodBr = Math.Clamp((int)Math.Floor(brY / cellH), 0, 14);
                    Assert.Equal(lvl, mappedLvlBr);
                    Assert.Equal(mood, mappedMoodBr);
                }
            }
        }

        [Theory]
        [InlineData(600.0, 300.0)]
        [InlineData(300.0, 150.0)]
        [InlineData(1200.0, 400.0)]
        [InlineData(599.0, 299.0)]
        public void OutOfBoundsCoordinates_ClampedToSafeMatrixBoundaries(double width, double height)
        {
            int maxLvl = 30;
            double cellW = width / maxLvl;
            double cellH = height / 15.0;

            // Far negative
            int negLvl = Math.Clamp((int)Math.Floor(-500.0 / cellW) + 1, 1, maxLvl);
            int negMood = Math.Clamp((int)Math.Floor(-200.0 / cellH), 0, 14);
            Assert.Equal(1, negLvl);
            Assert.Equal(0, negMood);

            // Far positive overflow
            int overLvl = Math.Clamp((int)Math.Floor((width + 500.0) / cellW) + 1, 1, maxLvl);
            int overMood = Math.Clamp((int)Math.Floor((height + 200.0) / cellH), 0, 14);
            Assert.Equal(maxLvl, overLvl);
            Assert.Equal(14, overMood);
        }

        #endregion

        #region 2. Tier and Mood Boundary Transitions & Alignment (L1, L9, L10, L19, L20, L30, M0, M4, M5, M8, M9, M14)

        [Theory]
        [InlineData(600.0, 300.0)]
        [InlineData(300.0, 150.0)]
        [InlineData(1200.0, 400.0)]
        public void ExtendedMode_TierBoundaries_AlignWithDividers(double width, double height)
        {
            int maxLvl = 30;
            double cellW = width / maxLvl;
            double cellH = height / 15.0;
            const double eps = 1e-6;

            Assert.True(cellH > 0);

            // Baby (1-9) -> Teen (10-19) boundary is at 9.0 * cellW
            double div1 = 9.0 * cellW;
            int babySideLvl = Math.Clamp((int)Math.Floor((div1 - eps) / cellW) + 1, 1, maxLvl);
            int teenSideLvl = Math.Clamp((int)Math.Floor((div1 + eps) / cellW) + 1, 1, maxLvl);
            Assert.Equal(9, babySideLvl);
            Assert.Equal(10, teenSideLvl);

            // Teen (10-19) -> Adult (20-30) boundary is at 19.0 * cellW
            double div2 = 19.0 * cellW;
            int teenSideLvl2 = Math.Clamp((int)Math.Floor((div2 - eps) / cellW) + 1, 1, maxLvl);
            int adultSideLvl2 = Math.Clamp((int)Math.Floor((div2 + eps) / cellW) + 1, 1, maxLvl);
            Assert.Equal(19, teenSideLvl2);
            Assert.Equal(20, adultSideLvl2);

            // Leftmost and Rightmost boundaries
            int minLvl = Math.Clamp((int)Math.Floor(0.0 / cellW) + 1, 1, maxLvl);
            int maxBoundaryLvl = Math.Clamp((int)Math.Floor((width - eps) / cellW) + 1, 1, maxLvl);
            Assert.Equal(1, minLvl);
            Assert.Equal(30, maxBoundaryLvl);

            // Header star ratios (9*, 10*, 11* of 30*)
            double babyRatio = 9.0 / 30.0;
            double teenRatio = 10.0 / 30.0;
            double adultRatio = 11.0 / 30.0;
            Assert.Equal(div1, width * babyRatio, 5);
            Assert.Equal(div2, width * (babyRatio + teenRatio), 5);
            Assert.Equal(width, width * (babyRatio + teenRatio + adultRatio), 5);
        }

        [Theory]
        [InlineData(600.0)]
        [InlineData(300.0)]
        [InlineData(1200.0)]
        public void StockMode_TierBoundaries_AlignWithDividers(double width)
        {
            int maxLvl = 3;
            double cellW = width / maxLvl;
            const double eps = 1e-6;

            // Baby (L1) -> Teen (L2) boundary is at 1.0 * cellW
            double div1 = 1.0 * cellW;
            int babySideLvl = Math.Clamp((int)Math.Floor((div1 - eps) / cellW) + 1, 1, maxLvl);
            int teenSideLvl = Math.Clamp((int)Math.Floor((div1 + eps) / cellW) + 1, 1, maxLvl);
            Assert.Equal(1, babySideLvl);
            Assert.Equal(2, teenSideLvl);

            // Teen (L2) -> Adult (L3) boundary is at 2.0 * cellW
            double div2 = 2.0 * cellW;
            int teenSideLvl2 = Math.Clamp((int)Math.Floor((div2 - eps) / cellW) + 1, 1, maxLvl);
            int adultSideLvl2 = Math.Clamp((int)Math.Floor((div2 + eps) / cellW) + 1, 1, maxLvl);
            Assert.Equal(2, teenSideLvl2);
            Assert.Equal(3, adultSideLvl2);

            // Stock header star ratios (1*, 1*, 1* of 3*)
            Assert.Equal(div1, width * (1.0 / 3.0), 5);
            Assert.Equal(div2, width * (2.0 / 3.0), 5);
        }

        [Theory]
        [InlineData(300.0)]
        [InlineData(150.0)]
        [InlineData(400.0)]
        public void MoodBoundaries_AlignWithHorizontalDividers(double height)
        {
            double cellH = height / 15.0;
            const double eps = 1e-6;

            // Happy (0-4) -> Neutral (5-9) boundary is at 5.0 * cellH
            double hdiv1 = 5.0 * cellH;
            int happySideMood = Math.Clamp((int)Math.Floor((hdiv1 - eps) / cellH), 0, 14);
            int neutralSideMood = Math.Clamp((int)Math.Floor((hdiv1 + eps) / cellH), 0, 14);
            Assert.Equal(4, happySideMood);
            Assert.Equal(5, neutralSideMood);

            // Neutral (5-9) -> Angry (10-14) boundary is at 10.0 * cellH
            double hdiv2 = 10.0 * cellH;
            int neutralSideMood2 = Math.Clamp((int)Math.Floor((hdiv2 - eps) / cellH), 0, 14);
            int angrySideMood2 = Math.Clamp((int)Math.Floor((hdiv2 + eps) / cellH), 0, 14);
            Assert.Equal(9, neutralSideMood2);
            Assert.Equal(10, angrySideMood2);

            // Topmost (M0) and Bottommost (M14)
            int minMood = Math.Clamp((int)Math.Floor(0.0 / cellH), 0, 14);
            int maxMood = Math.Clamp((int)Math.Floor((height - eps) / cellH), 0, 14);
            Assert.Equal(0, minMood);
            Assert.Equal(14, maxMood);

            // Emoji card row ratios (5*, 5*, 5* of 15*)
            double happyRatio = 5.0 / 15.0;
            double neutralRatio = 5.0 / 15.0;
            double angryRatio = 5.0 / 15.0;
            Assert.Equal(hdiv1, height * happyRatio, 5);
            Assert.Equal(hdiv2, height * (happyRatio + neutralRatio), 5);
            Assert.Equal(height, height * (happyRatio + neutralRatio + angryRatio), 5);
        }

        #endregion

        #region 3. Stock vs Extended Mode Switching & ViewModel State Integrity

        [Fact]
        public void StockModeToggle_SwitchesMaxAllowedLevel_AndClampsProperly()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.False(vm.IsStockMode);
            Assert.Equal(30, vm.MaxAllowedLevel);

            // Explicitly set an entry spanning L20-L30
            var entry = vm.Entries.First();
            entry.MinLevel = 20;
            entry.MaxLevel = 30;
            entry.MinButthurt = 0;
            entry.MaxButthurt = 14;

            vm.InspectCell(25, 10);
            Assert.Equal(25, vm.SelectedCellLevel);
            Assert.Equal(10, vm.SelectedCellMood);

            // Switch to Stock mode
            vm.IsStockMode = true;
            Assert.Equal(3, vm.MaxAllowedLevel);

            // In Stock mode, selected cell is clamped to L1..L3
            Assert.InRange(vm.SelectedCellLevel, 1, 3);

            // Switch back to Extended mode
            vm.IsStockMode = false;
            Assert.Equal(30, vm.MaxAllowedLevel);
        }

        [Theory]
        [InlineData(1, 0, "Baby", "Happy")]
        [InlineData(9, 4, "Baby", "Happy")]
        [InlineData(10, 5, "Teen", "Neutral")]
        [InlineData(19, 9, "Teen", "Neutral")]
        [InlineData(20, 10, "Adult", "Angry")]
        [InlineData(30, 14, "Adult", "Angry")]
        public void BoundaryCells_HoverTelemetry_MatchesExpectedTiers(int lvl, int mood, string expectedTier, string expectedMoodGroup)
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.HoverCell(lvl, mood);

            Assert.Contains($"Level {lvl}", vm.CellHoverInfoText);
            Assert.Contains(expectedTier, vm.CellHoverInfoText);
            Assert.Contains(expectedMoodGroup, vm.CellHoverInfoText);
        }

        [Theory]
        [InlineData(1, 1, 0, 0)]
        [InlineData(1, 9, 0, 4)]
        [InlineData(10, 19, 5, 9)]
        [InlineData(20, 30, 10, 14)]
        [InlineData(1, 30, 0, 14)]
        [InlineData(15, 25, 4, 10)]
        public void SelectionBoundingBox_CalculatesCorrectExtents_ForBoundaryRanges(int minL, int maxL, int minM, int maxM)
        {
            double width = 600.0;
            double height = 300.0;
            int maxLvl = 30;
            double cellW = width / maxLvl;
            double cellH = height / 15.0;

            double boxX = (minL - 1) * cellW;
            double boxY = minM * cellH;
            double boxW = Math.Max(1, (maxL - minL + 1) * cellW - 1);
            double boxH = Math.Max(1, (maxM - minM + 1) * cellH - 1);

            // Verify bounds stay within canvas
            Assert.True(boxX >= 0.0);
            Assert.True(boxY >= 0.0);
            Assert.True(boxX + boxW <= width);
            Assert.True(boxY + boxH <= height);

            // Verify width and height match cell count spans
            int spanL = maxL - minL + 1;
            int spanM = maxM - minM + 1;
            Assert.Equal(spanL * cellW - 1, boxW, 5);
            Assert.Equal(spanM * cellH - 1, boxH, 5);
        }

        #endregion

        #region 4. Color Contrast & Visual Distinction Calculations

        [Fact]
        public void BadgeTextBrush_AchievesHighContrast_OverAllHeatmapFills()
        {
            // Dark badge text: #0B0D14
            (double r, double g, double b) textRgb = (11.0 / 255.0, 13.0 / 255.0, 20.0 / 255.0);
            double lumText = ComputeRelativeLuminance(textRgb.r, textRgb.g, textRgb.b);

            // Solo (#00E5FF), Duo (#39FF14), Trio (#FFB300), Contended (#FF3366)
            var fills = new (string Name, double r, double g, double b)[]
            {
                ("Solo (Cyan)", 0.0 / 255.0, 229.0 / 255.0, 255.0 / 255.0),
                ("Duo (Green)", 57.0 / 255.0, 255.0 / 255.0, 20.0 / 255.0),
                ("Trio (Amber)", 255.0 / 255.0, 179.0 / 255.0, 0.0 / 255.0),
                ("Contended (Magenta)", 255.0 / 255.0, 51.0 / 255.0, 102.0 / 255.0)
            };

            foreach (var (name, r, g, b) in fills)
            {
                double lumBg = ComputeRelativeLuminance(r, g, b);
                double contrastRatio = (Math.Max(lumText, lumBg) + 0.05) / (Math.Min(lumText, lumBg) + 0.05);

                // Ensure WCAG AA compliance (>= 4.5:1) for badge overlay readability
                Assert.True(contrastRatio >= 4.5, $"Contrast ratio for {name} was {contrastRatio:F2}, expected >= 4.5");
            }
        }

        [Fact]
        public void DragStrokeColor_IsDistinctFrom_SoloFillColor()
        {
            // Drag stroke: #B388FF (Electric Violet)
            // Solo fill: #00E5FF (Electric Cyan)
            double r1 = 179.0, g1 = 136.0, b1 = 255.0;
            double r2 = 0.0, g2 = 229.0, b2 = 255.0;

            // Euclidean RGB distance
            double distance = Math.Sqrt(Math.Pow(r1 - r2, 2) + Math.Pow(g1 - g2, 2) + Math.Pow(b1 - b2, 2));

            // Ensure strong perceptual separation (> 150)
            Assert.True(distance > 150.0, $"Color distance was {distance:F2}, expected > 150.0 for distinct drag visual");
        }

        private static double ComputeRelativeLuminance(double r, double g, double b)
        {
            double rLin = r <= 0.03928 ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
            double gLin = g <= 0.03928 ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
            double bLin = b <= 0.03928 ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);
            return 0.2126 * rLin + 0.7152 * gLin + 0.0722 * bLin;
        }

        #endregion
    }
}
