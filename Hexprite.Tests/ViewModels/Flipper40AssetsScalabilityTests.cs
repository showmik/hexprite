using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Hexprite.ViewModels.Flipper;
using Xunit;

namespace Hexprite.Tests.ViewModels
{
    [Trait("Category", "Unit")]
    public class Flipper40AssetsScalabilityTests
    {
        private static FlipperScheduleMatrixViewModel Create40AssetMatrixViewModel()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.Entries.Clear();

            for (int i = 1; i <= 40; i++)
            {
                var sprite = new SpriteState(128, 64) { FrameRateFps = 10, IsAnimationEnabled = true };
                sprite.Frames.Clear();
                for (int f = 0; f < 5; f++)
                {
                    sprite.Frames.Add(new FrameState { Name = $"F{f}", LayerPixels = [new MonochromePixelBuffer(128 * 64)] });
                }

                string animName = $"anim_{i:D2}";
                int minL = ((i - 1) % 30) + 1;
                int maxL = Math.Min(30, minL + (i % 4));
                int minM = (i - 1) % 15;
                int maxM = Math.Min(14, minM + (i % 3));

                var entry = new FlipperManifestEntry
                {
                    Name = animName,
                    MinLevel = minL,
                    MaxLevel = maxL,
                    MinButthurt = minM,
                    MaxButthurt = maxM,
                    Weight = (i % 5) + 1
                };

                vm.Entries.Add(new FlipperScheduleEntryViewModel(entry));
                vm.SetAnimationSprite(animName, sprite);
            }

            vm.RecalculateMatrix();
            return vm;
        }

        [Fact]
        public void Scalability_40Assets_RecalculateMatrixAndFilterPerformance()
        {
            var vm = Create40AssetMatrixViewModel();
            Assert.Equal(40, vm.Entries.Count);
            Assert.Equal(40, vm.FilteredEntries.Count);

            // 1. Search filter
            vm.SearchFilterText = "anim_0";
            Assert.Equal(9, vm.FilteredEntries.Count);

            vm.SearchFilterText = string.Empty;
            Assert.Equal(40, vm.FilteredEntries.Count);

            // 2. Stage filter
            vm.SelectedStageFilter = "Baby";
            Assert.True(vm.FilteredEntries.Count > 0 && vm.FilteredEntries.Count < 40);

            vm.SelectedStageFilter = "All";
            Assert.Equal(40, vm.FilteredEntries.Count);

            // 3. Sorting
            vm.SelectedSortOption = "Name (A-Z)";
            Assert.Equal("anim_01", vm.FilteredEntries[0].Name);
            Assert.Equal("anim_40", vm.FilteredEntries[^1].Name);
        }

        [Theory]
        [InlineData(FlipperAutoBalanceStrategy.LinearLevels, false)]
        [InlineData(FlipperAutoBalanceStrategy.LinearLevels, true)]
        [InlineData(FlipperAutoBalanceStrategy.MoodTiers, false)]
        [InlineData(FlipperAutoBalanceStrategy.MoodTiers, true)]
        [InlineData(FlipperAutoBalanceStrategy.StageEvolution, false)]
        [InlineData(FlipperAutoBalanceStrategy.StageEvolution, true)]
        [InlineData(FlipperAutoBalanceStrategy.FillGapsOnly, false)]
        [InlineData(FlipperAutoBalanceStrategy.FillGapsOnly, true)]
        public void Scalability_40Assets_AutoBalanceStrategies_AllAchieveZeroDeadzoneGaps(FlipperAutoBalanceStrategy strategy, bool isStockMode)
        {
            var vm = Create40AssetMatrixViewModel();
            vm.IsStockMode = isStockMode;
            Assert.Equal(40, vm.Entries.Count);

            vm.AutoBalanceWithStrategy(strategy);
            Assert.Equal(40, vm.Entries.Count);

            // Verify matrix has 100% coverage and 0 deadzone gaps
            int maxLvl = isStockMode ? 3 : 30;
            for (int lvl = 1; lvl <= maxLvl; lvl++)
            {
                for (int mood = 0; mood <= 14; mood++)
                {
                    var cell = vm.Matrix.GetCell(lvl, mood);
                    Assert.True(cell.HasCoverage, $"Gap detected at Level {lvl}, Mood {mood} for strategy {strategy} (StockMode={isStockMode})");
                }
            }

            Assert.Equal(100.0, vm.Matrix.CoveragePercentage);
            Assert.Empty(vm.Matrix.GetUncoveredCells());
        }

        [Fact]
        public void Scalability_40Assets_ExportGeneratesSingleMasterIcon()
        {
            var vm = Create40AssetMatrixViewModel();
            string tempDir = Path.Combine(Path.GetTempPath(), "Hexprite_CleanExport40_" + Guid.NewGuid().ToString("N"));

            try
            {
                vm.ExportAssetPackFolder(tempDir);

                // Verify folder structure
                string animsDir = Path.Combine(tempDir, "Anims");
                string iconsDir = Path.Combine(tempDir, "Icons");
                Assert.True(Directory.Exists(animsDir));
                Assert.True(Directory.Exists(iconsDir));

                // 40 animation folders in Anims/
                var animDirs = Directory.GetDirectories(animsDir);
                Assert.Equal(40, animDirs.Length);

                // Exactly 1 master icon in Icons/ (not 40 redundant icons)
                var iconFiles = Directory.GetFiles(iconsDir, "*.bm");
                Assert.Single(iconFiles);
                Assert.Equal("I_anim_01_10x10.bm", Path.GetFileName(iconFiles[0]));
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public void Scalability_40Assets_LazyThumbnailGenerationAndHoverPlayback()
        {
            var vm = Create40AssetMatrixViewModel();
            var firstEntry = vm.Entries[0];

            // Verify frame 0 thumbnail rendered statically
            Assert.NotNull(firstEntry.SpriteThumbnail);
            Assert.True(firstEntry.HasMultipleFrames);

            // Starting hover playback triggers lazy generation of full sequence
            firstEntry.IsPreviewPlaying = true;
            Assert.NotNull(firstEntry.CachedFrameThumbnails);
            Assert.Equal(5, firstEntry.CachedFrameThumbnails.Count);

            // Stepping preview frame advances smoothly
            firstEntry.StepNextPreviewFrame();
            Assert.Equal(1, firstEntry.CurrentPreviewFrame);

            firstEntry.ResetPreviewFrame();
            Assert.Equal(0, firstEntry.CurrentPreviewFrame);
            Assert.False(firstEntry.IsPreviewPlaying);
            Assert.Equal(firstEntry.CachedFrameThumbnails[0], firstEntry.ActivePreviewThumbnail);
        }

        [Fact]
        public void Scalability_40Assets_UndoRedo_UpdatesInPlaceWithoutVMRecreation()
        {
            var vm = Create40AssetMatrixViewModel();
            var originalVmReference = vm.Entries[0];
            string originalName = originalVmReference.Name;

            // Make a modification
            vm.SelectedEntry = originalVmReference;
            vm.SelectedWeight = 50;
            Assert.Equal(50, originalVmReference.Weight);

            // Undo
            vm.Undo();

            // The collection maintains instance references without recreating all 40 VMs
            Assert.Equal(40, vm.Entries.Count);
            Assert.Same(originalVmReference, vm.Entries[0]);
            Assert.Equal(originalName, vm.Entries[0].Name);

            // Redo
            vm.Redo();
            Assert.Same(originalVmReference, vm.Entries[0]);
            Assert.Equal(50, vm.Entries[0].Weight);
        }
    }
}
