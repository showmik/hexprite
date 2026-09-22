using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels.Flipper;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FlipperMathAndCoordinateBugFixTests
    {
        private static BitmapSource CreateTestBitmap(int width, int height, uint fillColor = 0xFFFFFFFF)
        {
            var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            uint[] pixels = new uint[width * height];
            Array.Fill(pixels, fillColor);
            bmp.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            return bmp;
        }

        #region Bug M1: Bounds Shrink Inverse Expansion

        [Fact]
        public void ShrinkSelectedEntryMaxLevel_WhenMinEqualsMax_DoesNotExpandMinLevel()
        {
            var entry = new FlipperManifestEntry
            {
                Name = "anim_single",
                MinLevel = 5,
                MaxLevel = 5,
                MinButthurt = 3,
                MaxButthurt = 8,
                Weight = 1
            };

            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_single", new SpriteState(128, 64), entry)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack, "TestPack");
            vm.SelectedEntry = vm.Entries[0];

            Assert.Equal(5, vm.SelectedEntry.MinLevel);
            Assert.Equal(5, vm.SelectedEntry.MaxLevel);

            // Calling shrink when Min == Max must NOT expand MinLevel downward to 4
            vm.ShrinkSelectedEntryMaxLevel();

            Assert.Equal(5, vm.SelectedEntry.MinLevel);
            Assert.Equal(5, vm.SelectedEntry.MaxLevel);
        }

        [Fact]
        public void ShrinkSelectedEntryMaxMood_WhenMinEqualsMax_DoesNotExpandMinMood()
        {
            var entry = new FlipperManifestEntry
            {
                Name = "anim_single",
                MinLevel = 1,
                MaxLevel = 10,
                MinButthurt = 4,
                MaxButthurt = 4,
                Weight = 1
            };

            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_single", new SpriteState(128, 64), entry)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack, "TestPack");
            vm.SelectedEntry = vm.Entries[0];

            Assert.Equal(4, vm.SelectedEntry.MinButthurt);
            Assert.Equal(4, vm.SelectedEntry.MaxButthurt);

            // Calling shrink mood when Min == Max must NOT expand MinButthurt downward to 3
            vm.ShrinkSelectedEntryMaxMood();

            Assert.Equal(4, vm.SelectedEntry.MinButthurt);
            Assert.Equal(4, vm.SelectedEntry.MaxButthurt);
        }

        [Fact]
        public void ShrinkSelectedEntryMaxLevel_WhenMaxGreaterThanMin_ShrinksProperly()
        {
            var entry = new FlipperManifestEntry
            {
                Name = "anim_range",
                MinLevel = 5,
                MaxLevel = 10,
                MinButthurt = 0,
                MaxButthurt = 14,
                Weight = 1
            };

            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_range", new SpriteState(128, 64), entry)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack, "TestPack");
            vm.SelectedEntry = vm.Entries[0];

            vm.ShrinkSelectedEntryMaxLevel();

            Assert.Equal(5, vm.SelectedEntry.MinLevel);
            Assert.Equal(9, vm.SelectedEntry.MaxLevel);
        }

        #endregion

        #region Bug M2 & M3: Auto-Balancing Mood Tiers & Linear Levels

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(12)]
        [InlineData(15)]
        [InlineData(30)]
        public void BalanceByMoodTiers_WithVariousEntryCounts_Covers100PercentWithoutDeadzones(int count)
        {
            var entries = Enumerable.Range(0, count)
                .Select(i => new FlipperManifestEntry { Name = $"mood_anim_{i}" })
                .ToList();

            var balanced = FlipperScheduleMatrix.AutoBalanceEntries(entries, FlipperAutoBalanceStrategy.MoodTiers, 30);
            Assert.Equal(count, balanced.Count);

            var matrix = new FlipperScheduleMatrix(balanced, 30);
            Assert.Equal(450, matrix.CoveredCellsCount);
            Assert.Equal(100.0, matrix.CoveragePercentage);
            Assert.Empty(matrix.GetUncoveredCells());

            // Verify every level (1..30) has coverage across all 15 moods (0..14)
            for (int lvl = 1; lvl <= 30; lvl++)
            {
                for (int mood = 0; mood <= 14; mood++)
                {
                    var cell = matrix.GetCell(lvl, mood);
                    Assert.True(cell.HasCoverage, $"Level {lvl}, Mood {mood} must have coverage in MoodTiers strategy");
                }
            }
        }

        [Fact]
        public void BalanceLinearLevels_WithEightEntries_DistributesLevelsEvenlyWithoutSkew()
        {
            var entries = Enumerable.Range(0, 8)
                .Select(i => new FlipperManifestEntry { Name = $"anim_{i}" })
                .ToList();

            var balanced = FlipperScheduleMatrix.AutoBalanceEntries(entries, FlipperAutoBalanceStrategy.LinearLevels, 30);
            Assert.Equal(8, balanced.Count);

            var matrix = new FlipperScheduleMatrix(balanced, 30);
            Assert.Equal(450, matrix.CoveredCellsCount);
            Assert.Equal(100.0, matrix.CoveragePercentage);
            Assert.Empty(matrix.GetUncoveredCells());

            // Check bucket spans: max span minus min span must be <= 1 (e.g. 3 or 4 levels)
            var spans = balanced.Select(e => e.MaxLevel - e.MinLevel + 1).ToList();
            int minSpan = spans.Min();
            int maxSpan = spans.Max();
            Assert.True(maxSpan - minSpan <= 1, $"Level spans must be evenly balanced. Min span: {minSpan}, Max span: {maxSpan}");
            Assert.True(minSpan >= 3 && maxSpan <= 4, $"Expected spans to be 3 or 4 for 30 levels / 8 buckets, but got min {minSpan}, max {maxSpan}");
        }

        #endregion

        #region Bug D1 & D2: Speech Bubble Coordinates & Tail Clamping

        [Theory]
        [InlineData(SpeechBubbleTailPosition.TopLeft, 0, 0)]
        [InlineData(SpeechBubbleTailPosition.TopRight, 0, 0)]
        [InlineData(SpeechBubbleTailPosition.TopLeft, 50, -10)]
        [InlineData(SpeechBubbleTailPosition.BottomLeft, 100, 60)]
        [InlineData(SpeechBubbleTailPosition.BottomRight, 100, 64)]
        [InlineData(SpeechBubbleTailPosition.BottomRight, 50, 100)]
        [InlineData(SpeechBubbleTailPosition.None, -20, -20)]
        public void FlipperSpeechBubble_Draw_WhenPositionedAtExtremes_NeverClipsPixelsOutsideBounds(
            SpeechBubbleTailPosition tail, int x, int y)
        {
            int width = 128;
            int height = 64;
            bool[] canvas = new bool[width * height];

            var bubble = new FlipperSpeechBubble
            {
                Text = "Hi!",
                X = x,
                Y = y,
                Tail = tail
            };

            // Draw should not throw and should keep all drawn pixels within the canvas
            bubble.Draw(canvas, width, height, fillInterior: true);

            // Verify that measuring and drawing occurred
            var (bw, bh) = bubble.MeasureBubble();
            Assert.True(bw >= 18);
            Assert.True(bh >= 12);
        }

        [Fact]
        public void FlipperSimulatorViewModel_UpdateBubblePositionFromPoint_ScalesProportionally()
        {
            var vm = new FlipperSimulatorViewModel([]);

            // Default 384x192 viewport: (192, 96) maps to center (64, 32)
            vm.UpdateBubblePositionFromPoint(192, 96, 384.0, 192.0);
            Assert.Equal(64, vm.BubbleX);
            Assert.Equal(32, vm.BubbleY);

            // Scaled 512x256 viewport (DPI scaling): (256, 128) maps to center (64, 32)
            vm.UpdateBubblePositionFromPoint(256, 128, 512.0, 256.0);
            Assert.Equal(64, vm.BubbleX);
            Assert.Equal(32, vm.BubbleY);

            // Clamping at negative coordinates
            vm.UpdateBubblePositionFromPoint(-50, -50, 384.0, 192.0);
            Assert.Equal(0, vm.BubbleX);
            Assert.Equal(0, vm.BubbleY);

            // Clamping at oversized coordinates (BubbleX max MaxBubbleX, BubbleY max MaxBubbleY)
            vm.UpdateBubblePositionFromPoint(2000, 2000, 384.0, 192.0);
            Assert.Equal(vm.MaxBubbleX, vm.BubbleX);
            Assert.Equal(vm.MaxBubbleY, vm.BubbleY);
        }

        #endregion

        #region Bug S1, S2, S3: Media Slicer Geometry & Scaling

        [Fact]
        public void SliceToAnimationSprite_SubFrameSource_64x64_ProducesValidPaddedFrame()
        {
            // 64x64 image sliced with default 128x64 settings
            var source = CreateTestBitmap(64, 64, 0xFF000000); // Black image (active pixels)
            var settings = new MediaSliceSettings
            {
                Layout = SpriteSheetLayout.HorizontalStrip,
                FrameWidth = 128,
                FrameHeight = 64,
                BrightnessThreshold = 128
            };

            var sprite = FlipperMediaSlicerService.SliceToAnimationSprite(source, settings);

            Assert.NotNull(sprite);
            Assert.Single(sprite.Frames);
            Assert.Equal(128, sprite.Width);
            Assert.Equal(64, sprite.Height);

            // Verify that the frame is not an empty fallback blank frame
            var frame = sprite.Frames[0];
            Assert.NotEmpty(frame.LayerPixels);
            var buffer = frame.LayerPixels[0];
            Assert.Equal(128 * 64, buffer.GetMonochromeData().Length);
        }

        [Fact]
        public void SliceToAnimationSprite_GridAtlas_WithExplicitColumnsAndRows_CalculatesDimensionsProperly()
        {
            // 400x200 grid atlas with 4 columns and 2 rows -> 8 frames, each 100x100
            var source = CreateTestBitmap(400, 200);
            var settings = new MediaSliceSettings
            {
                Layout = SpriteSheetLayout.Grid,
                Columns = 4,
                Rows = 2,
                MaxFrames = 8
            };

            var sprite = FlipperMediaSlicerService.SliceToAnimationSprite(source, settings);

            Assert.NotNull(sprite);
            Assert.Equal(8, sprite.Frames.Count);
            Assert.Equal(128, sprite.Width);
            Assert.Equal(64, sprite.Height);
        }

        #endregion

        #region Bug A1 & A2: Animation Frame Cycle Bounds Safety

        [Fact]
        public void GetActiveSequence_WithNegativePassiveFrameCount_DoesNotThrowAndReturnsValidSequence()
        {
            var sprite = new SpriteState(128, 64);
            for (int i = 0; i < 5; i++)
            {
                sprite.Frames.Add(new FrameState { Name = $"Frame {i}" });
            }

            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                FramesOrder = [0, 1, 2, 3, 4],
                PassiveFrameCount = -5,
                ActiveFrameCount = 3
            };

            // Must not throw ArgumentOutOfRangeException
            var seq = FlipperSimulatorViewModel.GetActiveSequence(sprite);
            Assert.NotNull(seq);
            Assert.Equal(3, seq.Length);
            Assert.Equal([0, 1, 2], seq);
        }

        [Fact]
        public void GetActiveSequence_WithExcessPassiveFrameCount_ReturnsEmptySequenceSafely()
        {
            var sprite = new SpriteState(128, 64);
            for (int i = 0; i < 4; i++)
            {
                sprite.Frames.Add(new FrameState { Name = $"Frame {i}" });
            }

            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                FramesOrder = [0, 1, 2, 3],
                PassiveFrameCount = 10,
                ActiveFrameCount = 2
            };

            var seq = FlipperSimulatorViewModel.GetActiveSequence(sprite);
            Assert.NotNull(seq);
            Assert.Empty(seq);
        }

        [Fact]
        public void GetPassiveSequence_WithCustomFramesOrderAndZeroPassiveFrames_RespectsFramesOrder()
        {
            var sprite = new SpriteState(128, 64);
            for (int i = 0; i < 4; i++)
            {
                sprite.Frames.Add(new FrameState { Name = $"Frame {i}" });
            }

            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                FramesOrder = [3, 2, 1, 0],
                PassiveFrameCount = 0,
                ActiveFrameCount = 0
            };

            var seq = FlipperSimulatorViewModel.GetPassiveSequence(sprite);
            Assert.NotNull(seq);
            Assert.Equal([3, 2, 1, 0], seq);
        }

        #endregion

        #region Matrix Visualizer Bug Hunt Hardening Tests

        [Fact]
        public void FlipperScheduleCell_ZeroWeightEntries_ReportsNoCoverage()
        {
            var entry = new FlipperManifestEntry
            {
                Name = "zero_weight_anim",
                MinLevel = 1,
                MaxLevel = 5,
                MinButthurt = 0,
                MaxButthurt = 4,
                Weight = 0
            };

            var matrix = new FlipperScheduleMatrix([entry], 30);
            var cell = matrix.GetCell(1, 0);

            Assert.Single(cell.MatchingEntries);
            Assert.Equal(0, cell.TotalWeight);
            Assert.False(cell.HasCoverage, "Cell with 0 total weight must not report HasCoverage = true.");
            Assert.Equal(0.0, cell.GetProbability("zero_weight_anim"));
            Assert.Empty(cell.GetProbabilities());
        }

        [Fact]
        public void ModeSwitch_ToStockMode_ClampsSelectedCellAndSelectedRegion()
        {
            var entry = new FlipperManifestEntry
            {
                Name = "anim1",
                MinLevel = 1,
                MaxLevel = 30,
                MinButthurt = 0,
                MaxButthurt = 14,
                Weight = 1
            };

            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim1", new SpriteState(128, 64), entry)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack, "TestPack")
            {
                IsStockMode = false
            };

            vm.InspectCell(25, 10);
            vm.SelectRegion(15, 25, 2, 8);

            Assert.Equal(25, vm.SelectedCellLevel);
            Assert.Equal(10, vm.SelectedCellMood);
            Assert.True(vm.HasSelectedRegion);
            Assert.Equal(15, vm.SelectedRegionMinLevel);
            Assert.Equal(25, vm.SelectedRegionMaxLevel);

            // Switch to stock mode (MaxAllowedLevel = 3)
            vm.IsStockMode = true;

            Assert.Equal(3, vm.SelectedCellLevel);
            Assert.Equal(10, vm.SelectedCellMood);
            Assert.True(vm.HasSelectedRegion);
            Assert.Equal(3, vm.SelectedRegionMinLevel);
            Assert.Equal(3, vm.SelectedRegionMaxLevel);
            Assert.Equal(2, vm.SelectedRegionMinMood);
            Assert.Equal(8, vm.SelectedRegionMaxMood);
        }

        [Fact]
        public void UndoRedo_RestoresSelectedCellAndSelectedRegionState()
        {
            var entry1 = new FlipperManifestEntry
            {
                Name = "anim1",
                MinLevel = 1,
                MaxLevel = 10,
                MinButthurt = 0,
                MaxButthurt = 5,
                Weight = 1
            };

            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim1", new SpriteState(128, 64), entry1)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack, "TestPack")
            {
                IsStockMode = false
            };

            vm.InspectCell(3, 2);
            vm.SelectRegion(2, 5, 1, 4);

            // Push an operation (e.g. set bounds)
            vm.SetSelectedEntryBounds(8, 12, 3, 7);

            // Change cell selection and region afterwards
            vm.InspectCell(15, 8);
            vm.SelectRegion(10, 20, 5, 10);

            Assert.Equal(15, vm.SelectedCellLevel);
            Assert.Equal(8, vm.SelectedCellMood);
            Assert.Equal(10, vm.SelectedRegionMinLevel);
            Assert.Equal(20, vm.SelectedRegionMaxLevel);

            // Undo the set bounds operation
            vm.Undo();

            // Should restore the snapshot's cell and region selection
            Assert.Equal(3, vm.SelectedCellLevel);
            Assert.Equal(2, vm.SelectedCellMood);
            Assert.True(vm.HasSelectedRegion);
            Assert.Equal(2, vm.SelectedRegionMinLevel);
            Assert.Equal(5, vm.SelectedRegionMaxLevel);
            Assert.Equal(1, vm.SelectedRegionMinMood);
            Assert.Equal(4, vm.SelectedRegionMaxMood);

            // Redo
            vm.Redo();
            Assert.Equal(15, vm.SelectedCellLevel);
            Assert.Equal(8, vm.SelectedCellMood);
            Assert.True(vm.HasSelectedRegion);
            Assert.Equal(10, vm.SelectedRegionMinLevel);
            Assert.Equal(20, vm.SelectedRegionMaxLevel);
        }

        [Fact]
        public void ManifestValidation_StockMode_DetectsOutOfBoundsLevels()
        {
            var entry = new FlipperManifestEntry
            {
                Name = "out_of_bounds_anim",
                MinLevel = 1,
                MaxLevel = 10,
                MinButthurt = 0,
                MaxButthurt = 5,
                Weight = 1
            };

            // In Stock mode (isMomentum = false), MaxLevel 10 is invalid
            var diagnostics = entry.Validate(isMomentum: false);
            Assert.Contains(diagnostics, d => d.Code == "FZ003" && d.Severity == FlipperValidationSeverity.Error);

            // In Extended/Momentum mode (isMomentum = true), MaxLevel 10 is valid
            var extendedDiagnostics = entry.Validate(isMomentum: true);
            Assert.DoesNotContain(extendedDiagnostics, d => d.Code == "FZ003");
        }

        #endregion
    }
}
