using System;
using System.Collections.Generic;
using System.Linq;
using Hexprite.Core;
using Hexprite.ViewModels.Flipper;
using Xunit;

namespace Hexprite.Tests.ViewModels
{
    [Trait("Category", "Unit")]
    public class AutoBalanceRefinementTests
    {
        [Fact]
        public void MoodTiers_3Entries_AlignWithFlipperFirmwareTiers()
        {
            var entries = new List<FlipperManifestEntry>
            {
                new() { Name = "happy_anim" },
                new() { Name = "neutral_anim" },
                new() { Name = "angry_anim" }
            };

            var balanced = FlipperScheduleMatrix.AutoBalanceEntries(entries, FlipperAutoBalanceStrategy.MoodTiers, 30);
            Assert.Equal(3, balanced.Count);

            // Happy: 0..4 (5 mood levels)
            Assert.Equal(0, balanced[0].MinButthurt);
            Assert.Equal(4, balanced[0].MaxButthurt);

            // Neutral: 5..9 (5 mood levels - matches Flipper firmware)
            Assert.Equal(5, balanced[1].MinButthurt);
            Assert.Equal(9, balanced[1].MaxButthurt);

            // Angry: 10..14 (5 mood levels - matches Flipper firmware)
            Assert.Equal(10, balanced[2].MinButthurt);
            Assert.Equal(14, balanced[2].MaxButthurt);
        }

        [Fact]
        public void FillGapsOnly_PreservesEntryOrder()
        {
            var entries = new List<FlipperManifestEntry>
            {
                new() { Name = "z_last", MinLevel = 25, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14 },
                new() { Name = "a_first", MinLevel = 1, MaxLevel = 5, MinButthurt = 0, MaxButthurt = 14 },
                new() { Name = "m_middle", MinLevel = 10, MaxLevel = 15, MinButthurt = 0, MaxButthurt = 14 }
            };

            var balanced = FlipperScheduleMatrix.AutoBalanceEntries(entries, FlipperAutoBalanceStrategy.FillGapsOnly, 30);
            Assert.Equal(3, balanced.Count);

            // Verify names remain in original input order
            Assert.Equal("z_last", balanced[0].Name);
            Assert.Equal("a_first", balanced[1].Name);
            Assert.Equal("m_middle", balanced[2].Name);

            // Verify matrix achieves 100% coverage
            var matrix = new FlipperScheduleMatrix(balanced, 30);
            Assert.Equal(100.0, matrix.CoveragePercentage);
            Assert.Empty(matrix.GetUncoveredCells());
        }

        [Fact]
        public void FillGapsOnly_FixesInvertedBounds()
        {
            var entries = new List<FlipperManifestEntry>
            {
                new() { Name = "inverted_levels", MinLevel = 20, MaxLevel = 5, MinButthurt = 10, MaxButthurt = 2, Weight = 3 },
                new() { Name = "normal_entry", MinLevel = 22, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 }
            };

            var balanced = FlipperScheduleMatrix.AutoBalanceEntries(entries, FlipperAutoBalanceStrategy.FillGapsOnly, 30);
            Assert.Equal(2, balanced.Count);

            foreach (var e in balanced)
            {
                Assert.True(e.MinLevel <= e.MaxLevel, $"MinLevel {e.MinLevel} must be <= MaxLevel {e.MaxLevel}");
                Assert.True(e.MinButthurt <= e.MaxButthurt, $"MinButthurt {e.MinButthurt} must be <= MaxButthurt {e.MaxButthurt}");
            }

            var matrix = new FlipperScheduleMatrix(balanced, 30);
            Assert.Equal(100.0, matrix.CoveragePercentage);
            Assert.Empty(matrix.GetUncoveredCells());
        }

        [Fact]
        public void QuickFix_Gap_UsesFillGapsOnly_PreservesExistingCustomLayout()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.Entries.Clear();

            // Set up a custom layout with a gap between L10 and L20
            var e1 = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "custom_baby", MinLevel = 1, MaxLevel = 10, MinButthurt = 0, MaxButthurt = 14 });
            var e2 = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "custom_adult", MinLevel = 20, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14 });
            vm.Entries.Add(e1);
            vm.Entries.Add(e2);
            vm.RecalculateMatrix();

            Assert.True(vm.Matrix.GetUncoveredCells().Count > 0, "Initial state should have gaps.");

            // Execute QuickFix for GAP
            vm.ExecuteQuickFix("GAP", null);

            // Verify 100% coverage achieved
            Assert.Empty(vm.Matrix.GetUncoveredCells());

            // Verify that entry names and order were preserved
            Assert.Equal(2, vm.Entries.Count);
            Assert.Equal("custom_baby", vm.Entries[0].Name);
            Assert.Equal("custom_adult", vm.Entries[1].Name);

            // Verify baby was bridged to 19 instead of resetting to a 50/50 LinearLevels split (L1-15 / L16-30)
            Assert.Equal(1, vm.Entries[0].MinLevel);
            Assert.Equal(19, vm.Entries[0].MaxLevel);
            Assert.Equal(20, vm.Entries[1].MinLevel);
            Assert.Equal(30, vm.Entries[1].MaxLevel);
        }

        [Fact]
        public void AutoBalance_ReusesViewModels_PreservesThumbnails()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.Entries.Clear();

            var sprite = new SpriteState(128, 64) { FrameRateFps = 10 };
            sprite.Frames.Clear();
            sprite.Frames.Add(new FrameState { Name = "F0", LayerPixels = [new MonochromePixelBuffer(128 * 64)] });

            var evm1 = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "anim1", MinLevel = 1, MaxLevel = 10 });
            var evm2 = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "anim2", MinLevel = 20, MaxLevel = 30 });
            vm.Entries.Add(evm1);
            vm.Entries.Add(evm2);
            vm.SetAnimationSprite("anim1", sprite);
            vm.SetAnimationSprite("anim2", sprite);
            vm.RecalculateMatrix();

            var origThumb1 = evm1.SpriteThumbnail;
            Assert.NotNull(origThumb1);

            // AutoBalance
            vm.AutoBalanceWithStrategy(FlipperAutoBalanceStrategy.LinearLevels);

            // ViewModels must be updated in-place (same references)
            Assert.Equal(2, vm.Entries.Count);
            Assert.Same(evm1, vm.Entries[0]);
            Assert.Same(evm2, vm.Entries[1]);
            Assert.Same(origThumb1, vm.Entries[0].SpriteThumbnail);
        }

        [Theory]
        [InlineData(FlipperAutoBalanceStrategy.LinearLevels, 3)]
        [InlineData(FlipperAutoBalanceStrategy.LinearLevels, 30)]
        [InlineData(FlipperAutoBalanceStrategy.MoodTiers, 3)]
        [InlineData(FlipperAutoBalanceStrategy.MoodTiers, 30)]
        [InlineData(FlipperAutoBalanceStrategy.StageEvolution, 3)]
        [InlineData(FlipperAutoBalanceStrategy.StageEvolution, 30)]
        [InlineData(FlipperAutoBalanceStrategy.FillGapsOnly, 3)]
        [InlineData(FlipperAutoBalanceStrategy.FillGapsOnly, 30)]
        public void AllStrategies_VariousCounts_Guarantee100Coverage(FlipperAutoBalanceStrategy strategy, int maxLevel)
        {
            int[] testCounts = [1, 2, 3, 4, 5, 7, 10, 15, 25, 40];

            foreach (int count in testCounts)
            {
                var entries = Enumerable.Range(1, count)
                    .Select(i => new FlipperManifestEntry
                    {
                        Name = $"anim_{i:D2}",
                        MinLevel = ((i - 1) % maxLevel) + 1,
                        MaxLevel = Math.Min(maxLevel, ((i - 1) % maxLevel) + 2),
                        MinButthurt = (i - 1) % 15,
                        MaxButthurt = Math.Min(14, ((i - 1) % 15) + 2),
                        Weight = (i % 3) + 1
                    })
                    .ToList();

                var balanced = FlipperScheduleMatrix.AutoBalanceEntries(entries, strategy, maxLevel);
                Assert.Equal(count, balanced.Count);

                var matrix = new FlipperScheduleMatrix(balanced, maxLevel);
                Assert.Equal(100.0, matrix.CoveragePercentage);
                Assert.Empty(matrix.GetUncoveredCells());

                foreach (var e in balanced)
                {
                    Assert.InRange(e.MinLevel, 1, maxLevel);
                    Assert.InRange(e.MaxLevel, 1, maxLevel);
                    Assert.True(e.MinLevel <= e.MaxLevel, $"MinLevel {e.MinLevel} must be <= MaxLevel {e.MaxLevel} for {strategy}");
                    Assert.InRange(e.MinButthurt, 0, 14);
                    Assert.InRange(e.MaxButthurt, 0, 14);
                    Assert.True(e.MinButthurt <= e.MaxButthurt, $"MinButthurt {e.MinButthurt} must be <= MaxButthurt {e.MaxButthurt} for {strategy}");
                }
            }
        }
        [Fact]
        public void AutoBalance_RetainsSelectedEntryAcrossRebalance()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.Entries.Clear();

            for (int i = 1; i <= 5; i++)
            {
                vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = $"dolphin_{i:D2}", MinLevel = i, MaxLevel = i }));
            }
            vm.RecalculateMatrix();

            // Explicitly select 4th animation
            vm.SelectedEntry = vm.Entries[3]; // "dolphin_04"
            Assert.Equal("dolphin_04", vm.SelectedEntry.Name);

            // Execute AutoBalance
            vm.AutoBalanceWithStrategy(FlipperAutoBalanceStrategy.MoodTiers);

            // Verify that dolphin_04 is STILL selected (not reset to dolphin_01)
            Assert.NotNull(vm.SelectedEntry);
            Assert.Equal("dolphin_04", vm.SelectedEntry.Name);
        }

        [Fact]
        public void AutoBalance_SynchronizesSavedBoundsAcrossModeToggles()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.Entries.Clear();

            // Start in Stock mode (L1-3)
            vm.IsStockMode = true;

            var e1 = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "baby_anim", MinLevel = 1, MaxLevel = 1 });
            var e2 = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "teen_anim", MinLevel = 2, MaxLevel = 2 });
            var e3 = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "adult_anim", MinLevel = 3, MaxLevel = 3 });
            vm.Entries.Add(e1);
            vm.Entries.Add(e2);
            vm.Entries.Add(e3);

            // AutoBalance in Stock mode with StageEvolution
            vm.AutoBalanceWithStrategy(FlipperAutoBalanceStrategy.StageEvolution);

            Assert.Equal(1, vm.Entries[0].MinLevel);
            Assert.Equal(1, vm.Entries[0].MaxLevel);
            Assert.Equal(2, vm.Entries[1].MinLevel);
            Assert.Equal(2, vm.Entries[1].MaxLevel);
            Assert.Equal(3, vm.Entries[2].MinLevel);
            Assert.Equal(3, vm.Entries[2].MaxLevel);

            // Toggle to Extended mode (L1-30)
            vm.ToggleMode();
            Assert.False(vm.IsStockMode);

            // Bounds must be projected cleanly to Extended mode ranges (L1-9, L10-19, L20-30)
            Assert.Equal(1, vm.Entries[0].MinLevel);
            Assert.Equal(9, vm.Entries[0].MaxLevel);
            Assert.Equal(10, vm.Entries[1].MinLevel);
            Assert.Equal(19, vm.Entries[1].MaxLevel);
            Assert.Equal(20, vm.Entries[2].MinLevel);
            Assert.Equal(30, vm.Entries[2].MaxLevel);

            // Coverage in Extended mode must still be 100%
            Assert.Equal(100.0, vm.Matrix.CoveragePercentage);
            Assert.Empty(vm.Matrix.GetUncoveredCells());
        }

        [Fact]
        public void MoodPresetsAndFilters_AlignWithUnifiedThreeTierSystem()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.Entries.Clear();

            var happyEntry = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "happy" });
            var neutralEntry = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "neutral" });
            var angryEntry = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "angry" });

            happyEntry.SetMoodPreset("Happy");
            neutralEntry.SetMoodPreset("Neutral");
            angryEntry.SetMoodPreset("Angry");

            // Check presets
            Assert.Equal(0, happyEntry.MinButthurt);
            Assert.Equal(4, happyEntry.MaxButthurt);
            Assert.Equal("Happy (0-4)", happyEntry.MoodRangeName);

            Assert.Equal(5, neutralEntry.MinButthurt);
            Assert.Equal(9, neutralEntry.MaxButthurt);
            Assert.Equal("Neutral (5-9)", neutralEntry.MoodRangeName);

            Assert.Equal(10, angryEntry.MinButthurt);
            Assert.Equal(14, angryEntry.MaxButthurt);
            Assert.Equal("Angry (10-14)", angryEntry.MoodRangeName);

            vm.Entries.Add(happyEntry);
            vm.Entries.Add(neutralEntry);
            vm.Entries.Add(angryEntry);
            vm.RecalculateMatrix();

            // Filter: Neutral
            vm.SelectedMoodFilter = "Neutral";
            Assert.Single(vm.FilteredEntries);
            Assert.Equal("neutral", vm.FilteredEntries[0].Name);

            // Filter: Angry
            vm.SelectedMoodFilter = "Angry";
            Assert.Single(vm.FilteredEntries);
            Assert.Equal("angry", vm.FilteredEntries[0].Name);

            // Filter: Happy
            vm.SelectedMoodFilter = "Happy";
            Assert.Single(vm.FilteredEntries);
            Assert.Equal("happy", vm.FilteredEntries[0].Name);

            // Hover and Inspect check at Mood 9 (must be Neutral)
            vm.InspectCell(1, 9);
            Assert.Contains("Neutral (5-9)", vm.SelectedCellSummaryText);

            vm.HoverCell(1, 9);
            Assert.Contains("Neutral", vm.CellHoverInfoText);
        }

        [Fact]
        public void StageColorsAndSummarySubtitle_ProvideNonCollidingVisualEncoding()
        {
            var baby = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "baby", MinLevel = 1, MaxLevel = 9, MinButthurt = 0, MaxButthurt = 4 });
            var babyPartial = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "babyPartial", MinLevel = 3, MaxLevel = 9, MinButthurt = 0, MaxButthurt = 4 });
            var teen = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "teen", MinLevel = 10, MaxLevel = 19, MinButthurt = 5, MaxButthurt = 9 });
            var adult = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "adult", MinLevel = 20, MaxLevel = 30, MinButthurt = 10, MaxButthurt = 14 });
            var babyTeen = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "babyTeen", MinLevel = 1, MaxLevel = 15, MinButthurt = 0, MaxButthurt = 4 });
            var teenAdult = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "teenAdult", MinLevel = 15, MaxLevel = 25, MinButthurt = 5, MaxButthurt = 9 });
            var stockBabyTeen = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "stockBabyTeen", MinLevel = 1, MaxLevel = 2, MinButthurt = 0, MaxButthurt = 4 });
            var stockTeenAdult = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "stockTeenAdult", MinLevel = 2, MaxLevel = 3, MinButthurt = 5, MaxButthurt = 9 });
            var spanning = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "spanning", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14 });

            // Distinct non-colliding Stage colors
            Assert.Equal(Hexprite.Core.FlipperStageCategory.Baby, baby.StageCategory);
            Assert.Equal("#00E5FF", baby.EntryColorHex);      // Cyan
            Assert.Equal(Hexprite.Core.FlipperStageCategory.Baby, babyPartial.StageCategory);
            Assert.Equal("#00E5FF", babyPartial.EntryColorHex); // Cyan for partial baby sub-range

            Assert.Equal(Hexprite.Core.FlipperStageCategory.Teen, teen.StageCategory);
            Assert.Equal("#E040FB", teen.EntryColorHex);      // Hot Magenta

            Assert.Equal(Hexprite.Core.FlipperStageCategory.Adult, adult.StageCategory);
            Assert.Equal("#FFD600", adult.EntryColorHex);     // Radiant Gold

            // 2-Stage combinations get blend colors
            Assert.Equal(Hexprite.Core.FlipperStageCategory.BabyTeen, babyTeen.StageCategory);
            Assert.Equal("#8B5CF6", babyTeen.EntryColorHex);  // Vivid Violet
            Assert.Equal(Hexprite.Core.FlipperStageCategory.BabyTeen, stockBabyTeen.StageCategory);
            Assert.Equal("#8B5CF6", stockBabyTeen.EntryColorHex);

            Assert.Equal(Hexprite.Core.FlipperStageCategory.TeenAdult, teenAdult.StageCategory);
            Assert.Equal("#FF6D00", teenAdult.EntryColorHex); // Vivid Orange
            Assert.Equal(Hexprite.Core.FlipperStageCategory.TeenAdult, stockTeenAdult.StageCategory);
            Assert.Equal("#FF6D00", stockTeenAdult.EntryColorHex);

            Assert.Equal(Hexprite.Core.FlipperStageCategory.AllStages, spanning.StageCategory);
            Assert.Equal("#E2E8F0", spanning.EntryColorHex);  // Silver Platinum

            // Rich Subtitle summaries with dual emojis
            Assert.Contains("👶", baby.SummarySubtitleText);
            Assert.Contains("😊", baby.SummarySubtitleText);
            Assert.Contains("Baby", baby.SummarySubtitleText);
            Assert.Contains("Happy", baby.SummarySubtitleText);

            Assert.Contains("👦", teen.SummarySubtitleText);
            Assert.Contains("😐", teen.SummarySubtitleText);
            Assert.Contains("Teen", teen.SummarySubtitleText);
            Assert.Contains("Neutral", teen.SummarySubtitleText);

            Assert.Contains("🐬", adult.SummarySubtitleText);
            Assert.Contains("😡", adult.SummarySubtitleText);
            Assert.Contains("Adult", adult.SummarySubtitleText);
            Assert.Contains("Angry", adult.SummarySubtitleText);

            Assert.Contains("👶👦", babyTeen.SummarySubtitleText);
            Assert.Contains("👦🐬", teenAdult.SummarySubtitleText);
        }
    }
}
