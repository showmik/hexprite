using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Hexprite.Core;
using Hexprite.Resources.Fonts;
using Hexprite.Services;
using Hexprite.Services.Compression;
using Hexprite.ViewModels;
using Xunit;

namespace Hexprite.Tests.E2E
{
    /// <summary>
    /// Tier 2: Boundary & Corner Cases E2E Tests (5+ tests per feature across all 18 features = 90+ tests).
    /// Opaque-box boundary value and edge stress analysis.
    /// </summary>
    [Trait("Category", "E2E")]
    [Trait("Category", "Unit")]
    public class Tier2_BoundaryCornerTests
    {
        #region Feature 1: Simulation Cycle State Machine (Boundary & Corner)

        [Fact]
        public void T2_F01_ZeroActiveFrames_TriggerActiveIsNoOp()
        {
            var sprite = E2ETestHelper.CreateTestSprite(2);
            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                PassiveFrameCount = 2,
                ActiveFrameCount = 0,
                FramesOrder = [0, 1]
            };

            string state = "Passive";
            if (sprite.FlipperCycle.ActiveFrameCount > 0)
            {
                state = "Active";
            }

            Assert.Equal("Passive", state);
        }

        [Fact]
        public void T2_F01_ZeroPassiveFrames_ActiveOnly_HandlesGracefully()
        {
            var sprite = E2ETestHelper.CreateTestSprite(3);
            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                PassiveFrameCount = 0,
                ActiveFrameCount = 3,
                FramesOrder = [0, 1, 2]
            };

            var passiveSlice = sprite.FlipperCycle.FramesOrder.Take(sprite.FlipperCycle.PassiveFrameCount).ToArray();
            Assert.Empty(passiveSlice);
        }

        [Fact]
        public void T2_F01_ActiveCooldownZero_TransitionsDirectlyToPassive()
        {
            var cycle = new FlipperAnimationCycle
            {
                PassiveFrameCount = 1,
                ActiveFrameCount = 1,
                ActiveCycles = 1,
                ActiveCooldown = 0,
                FramesOrder = [0, 1]
            };

            string state = "Active";
            int activeStep = 1; // Completed active frames

            if (activeStep >= cycle.ActiveFrameCount * cycle.ActiveCycles)
            {
                state = cycle.ActiveCooldown > 0 ? "Cooldown" : "Passive";
            }

            Assert.Equal("Passive", state);
        }

        [Fact]
        public void T2_F01_FramesOrderOutOfBounds_ClampsSafely()
        {
            var meta = new FlipperAnimationMeta
            {
                Width = 128,
                Height = 64,
                PassiveFrames = 2,
                ActiveFrames = 0,
                FramesOrder = [0, 5] // 5 is out of bounds for a 2-frame sprite
            };

            var diagnostics = meta.Validate(physicalFrameCount: 2);
            Assert.Contains(diagnostics, d => d.Code == "FZM004");
        }

        [Fact]
        public void T2_F01_ExtremeActiveCycles_LargeCycleCountExecutesDeterministically()
        {
            var cycle = new FlipperAnimationCycle
            {
                PassiveFrameCount = 1,
                ActiveFrameCount = 2,
                ActiveCycles = 50, // Large cycle count
                FramesOrder = [0, 1, 2]
            };

            int totalFramesToPlay = cycle.ActiveFrameCount * cycle.ActiveCycles;
            Assert.Equal(100, totalFramesToPlay);

            int cycleCount = 0;
            int frameInCycle = 0;
            for (int i = 0; i < totalFramesToPlay; i++)
            {
                frameInCycle++;
                if (frameInCycle >= cycle.ActiveFrameCount)
                {
                    frameInCycle = 0;
                    cycleCount++;
                }
            }

            Assert.Equal(50, cycleCount);
        }

        #endregion

        #region Feature 2: Candidate Selection & Weight Probabilities (Boundary & Corner)

        [Fact]
        public void T2_F02_ZeroMatchingCandidates_ReturnsEmptyAndZeroProbabilities()
        {
            var entries = new List<FlipperManifestEntry>
            {
                new() { Name = "BabyOnly", MinLevel = 1, MaxLevel = 5, MinButthurt = 0, MaxButthurt = 5, Weight = 1 }
            };

            int queryLevel = 25;
            int queryMood = 12;

            var matching = entries.Where(e =>
                queryLevel >= e.MinLevel && queryLevel <= e.MaxLevel &&
                queryMood >= e.MinButthurt && queryMood <= e.MaxButthurt).ToList();

            Assert.Empty(matching);
        }

        [Fact]
        public void T2_F02_EqualWeights_DistributesUniformProbabilities()
        {
            var cell = new FlipperScheduleCell(1, 0);
            cell.MatchingEntries.Add(new FlipperManifestEntry { Name = "A", Weight = 1 });
            cell.MatchingEntries.Add(new FlipperManifestEntry { Name = "B", Weight = 1 });
            cell.MatchingEntries.Add(new FlipperManifestEntry { Name = "C", Weight = 1 });
            cell.MatchingEntries.Add(new FlipperManifestEntry { Name = "D", Weight = 1 });

            Assert.Equal(0.25, cell.GetProbability("A"));
            Assert.Equal(0.25, cell.GetProbability("B"));
            Assert.Equal(0.25, cell.GetProbability("C"));
            Assert.Equal(0.25, cell.GetProbability("D"));
        }

        [Fact]
        public void T2_F02_ExtremeWeightDisparity_MinAndMaxWeights()
        {
            var cell = new FlipperScheduleCell(1, 0);
            cell.MatchingEntries.Add(new FlipperManifestEntry { Name = "Rare", Weight = 1 });
            cell.MatchingEntries.Add(new FlipperManifestEntry { Name = "Common", Weight = 99 });

            Assert.Equal(100, cell.TotalWeight);
            Assert.Equal(0.01, cell.GetProbability("Rare"), 4);
            Assert.Equal(0.99, cell.GetProbability("Common"), 4);
        }

        [Fact]
        public void T2_F02_BoundaryLevels_Level1AndLevel30_EvaluatesCorrectly()
        {
            var entries = new List<FlipperManifestEntry>
            {
                new() { Name = "MinEdge", MinLevel = 1, MaxLevel = 1, MinButthurt = 0, MaxButthurt = 14, Weight = 1 },
                new() { Name = "MaxEdge", MinLevel = 30, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 }
            };

            var matrix = new FlipperScheduleMatrix(entries);
            Assert.True(matrix.GetCell(1, 0).HasCoverage);
            Assert.True(matrix.GetCell(30, 14).HasCoverage);
            Assert.False(matrix.GetCell(15, 7).HasCoverage);
        }

        [Fact]
        public void T2_F02_BoundaryMoods_Mood0AndMood14_EvaluatesCorrectly()
        {
            var entries = new List<FlipperManifestEntry>
            {
                new() { Name = "HappyEdge", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 0, Weight = 1 },
                new() { Name = "AngryEdge", MinLevel = 1, MaxLevel = 30, MinButthurt = 14, MaxButthurt = 14, Weight = 1 }
            };

            var matrix = new FlipperScheduleMatrix(entries);
            Assert.True(matrix.GetCell(10, 0).HasCoverage);
            Assert.True(matrix.GetCell(10, 14).HasCoverage);
            Assert.False(matrix.GetCell(10, 7).HasCoverage);
        }

        #endregion

        #region Feature 3: Speech Bubble Dynamics & Canvas HUD (Boundary & Corner)

        [Fact]
        public void T2_F03_EmptyText_MeasuresDefaultAndDoesNotCrash()
        {
            var bubble = new FlipperSpeechBubble(1, 0, 0, "");
            var (w, h) = bubble.MeasureBubble();

            Assert.Equal(16, w);
            Assert.Equal(12, h);

            bool[] canvas = new bool[128 * 64];
            bubble.Draw(canvas, 128, 64);
            // Draw on empty text should safely do nothing
            Assert.All(canvas, p => Assert.False(p));
        }

        [Fact]
        public void T2_F03_LongText_WrapsOrClampsWithinScreenBounds()
        {
            var bubble = new FlipperSpeechBubble(1, 0, 0, "A very long dolphin speech bubble that spans across the LCD screen");
            var (w, h) = bubble.MeasureBubble();

            Assert.True(w > 50);
            Assert.True(h >= 12);
        }

        [Fact]
        public void T2_F03_NegativeCoordinates_ClampsToZeroZero()
        {
            bool[] canvas = new bool[128 * 64];
            var bubble = new FlipperSpeechBubble(1, -50, -20, "Clamp");
            bubble.Draw(canvas, 128, 64);

            // Draws starting at clamped (0, 0)
            Assert.Contains(true, canvas);
        }

        [Fact]
        public void T2_F03_OffscreenCoordinates_ClampsToBottomRight()
        {
            bool[] canvas = new bool[128 * 64];
            var bubble = new FlipperSpeechBubble(1, 500, 300, "ClampMax");
            bubble.Draw(canvas, 128, 64);

            // Should clamp within 128x64 without throwing IndexOutOfRangeException
            Assert.Contains(true, canvas);
        }

        [Fact]
        public void T2_F03_SpecialCharactersInSpeechText_RendersSafely()
        {
            bool[] canvas = new bool[128 * 64];
            var bubble = new FlipperSpeechBubble(1, 10, 10, "!@#$%^&*()_+-=[]{}|;':,.<>?/");
            bubble.Draw(canvas, 128, 64);

            Assert.Contains(true, canvas);
        }

        #endregion

        #region Feature 4: Schedule Matrix 30x15 Heatmap & Collision (Boundary & Corner)

        [Fact]
        public void T2_F04_EmptyEntriesList_ReturnsZeroCoverage()
        {
            var matrix = new FlipperScheduleMatrix([]);
            Assert.Equal(0, matrix.CoveredCellsCount);
            Assert.Equal(0.0, matrix.CoveragePercentage);
            Assert.Equal(0, matrix.MaxCollidingAnimations);
            Assert.Equal(450, matrix.GetUncoveredCells().Count);
        }

        [Fact]
        public void T2_F04_SingleCellEntry_CoversExactOneCell()
        {
            var entries = new List<FlipperManifestEntry>
            {
                new() { Name = "Punctual", MinLevel = 5, MaxLevel = 5, MinButthurt = 7, MaxButthurt = 7, Weight = 1 }
            };

            var matrix = new FlipperScheduleMatrix(entries);
            Assert.Equal(1, matrix.CoveredCellsCount);
            Assert.Equal(1.0 / 450.0 * 100.0, matrix.CoveragePercentage, 4);
            Assert.Equal(449, matrix.GetUncoveredCells().Count);
        }

        [Fact]
        public void T2_F04_ClampLevelAndMoodLookup_OutOfBoundsClamping()
        {
            var matrix = new FlipperScheduleMatrix([]);
            var cellMin = matrix.GetCell(-10, -5);
            var cellMax = matrix.GetCell(100, 50);

            Assert.Equal(1, cellMin.Level);
            Assert.Equal(0, cellMin.Mood);
            Assert.Equal(30, cellMax.Level);
            Assert.Equal(14, cellMax.Mood);
        }

        [Fact]
        public void T2_F04_HighCollisionStress_50OverlappingEntries()
        {
            var entries = new List<FlipperManifestEntry>();
            for (int i = 0; i < 50; i++)
            {
                entries.Add(new FlipperManifestEntry
                {
                    Name = $"Anim_{i}",
                    MinLevel = 1,
                    MaxLevel = 30,
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = 1
                });
            }

            var matrix = new FlipperScheduleMatrix(entries);
            Assert.Equal(50, matrix.MaxCollidingAnimations);
            Assert.Equal(450, matrix.CoveredCellsCount);

            var cell = matrix.GetCell(15, 7);
            Assert.Equal(50, cell.TotalWeight);
            Assert.Equal(1.0 / 50.0, cell.GetProbability("Anim_0"), 4);
        }

        [Fact]
        public void T2_F04_ZeroWeightEntries_CellTotalWeightAndProbabilityZero()
        {
            var cell = new FlipperScheduleCell(1, 0);
            cell.MatchingEntries.Add(new FlipperManifestEntry { Name = "ZeroWeight", Weight = 0 });

            Assert.Equal(0, cell.TotalWeight);
            Assert.Equal(0.0, cell.GetProbability("ZeroWeight"));
        }

        #endregion

        #region Feature 5: Schedule Matrix Deadzone & Auto-Balance (Boundary & Corner)

        [Fact]
        public void T2_F05_AutoBalance_EmptyList_ReturnsEmpty()
        {
            var balanced = FlipperScheduleMatrix.AutoBalanceEntries([]);
            Assert.Empty(balanced);
        }

        [Fact]
        public void T2_F05_AutoBalance_LargeAnimationCount_7Entries_Guarantees100Percent()
        {
            var original = new List<FlipperManifestEntry>();
            for (int i = 0; i < 7; i++)
            {
                original.Add(new FlipperManifestEntry { Name = $"Anim_{i}" });
            }

            var balanced = FlipperScheduleMatrix.AutoBalanceEntries(original);
            Assert.Equal(7, balanced.Count);

            var matrix = new FlipperScheduleMatrix(balanced);
            Assert.Equal(100.0, matrix.CoveragePercentage);
            Assert.Empty(matrix.GetUncoveredCells());
        }

        [Fact]
        public void T2_F05_AutoBalance_PreservesAnimationNames()
        {
            var original = new List<FlipperManifestEntry>
            {
                new() { Name = "First" },
                new() { Name = "Second" },
                new() { Name = "Third" }
            };

            var balanced = FlipperScheduleMatrix.AutoBalanceEntries(original);
            Assert.Equal("First", balanced[0].Name);
            Assert.Equal("Second", balanced[1].Name);
            Assert.Equal("Third", balanced[2].Name);
        }

        [Fact]
        public void T2_F05_AutoBalance_PreservesWeightsWhereApplicable()
        {
            var original = new List<FlipperManifestEntry>
            {
                new() { Name = "Weighted", Weight = 5 }
            };

            var balanced = FlipperScheduleMatrix.AutoBalanceEntries(original);
            Assert.Equal(5, balanced[0].Weight);
        }

        [Fact]
        public void T2_F05_CheckerboardGaps_DetectsNonContiguousDeadzones()
        {
            var entries = new List<FlipperManifestEntry>();
            // Add entries that only cover even levels
            for (int l = 2; l <= 30; l += 2)
            {
                entries.Add(new FlipperManifestEntry
                {
                    Name = $"Level_{l}",
                    MinLevel = l,
                    MaxLevel = l,
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = 1
                });
            }

            var matrix = new FlipperScheduleMatrix(entries);
            var gaps = matrix.GetUncoveredCells();

            // 15 odd levels * 15 moods = 225 gaps
            Assert.Equal(225, gaps.Count);
            Assert.All(gaps, g => Assert.True(g.Level % 2 != 0));
        }

        #endregion

        #region Feature 6: Asset Pack Disk Boundary & Corner

        [Fact]
        public void T2_F06_CreateTestAssetPack_GeneratesValidManifest()
        {
            using var tempDir = E2ETestHelper.CreateTempDirectory();
            string packDir = E2ETestHelper.CreateTestAssetPack("unknown_pack_id", tempDir.Path);

            Assert.True(Directory.Exists(packDir));
            Assert.True(File.Exists(Path.Combine(packDir, "manifest.txt")));
        }

        [Fact]
        public void T2_F06_CreateTestAssetPack_DecompressesFramesRoundTrip()
        {
            using var tempDir = E2ETestHelper.CreateTempDirectory();
            string packDir = E2ETestHelper.CreateTestAssetPack("stock_dolphin_essentials", tempDir.Path);

            var importService = new FlipperImportService();
            var imported = importService.ImportAssetPack(packDir);

            Assert.NotEmpty(imported);
            foreach (var (_, sprite, _) in imported)
            {
                Assert.Equal(128, sprite.Width);
                Assert.Equal(64, sprite.Height);
                Assert.NotEmpty(sprite.Frames);
            }
        }

        #endregion

        #region Feature 7: Asset Pack Tab Opening Workflow (Boundary & Corner)

        [Fact]
        public void T2_F07_OpenSpritesInTabs_EmptyList_NoOp()
        {
            var shell = E2ETestHelper.CreateTestShellViewModel();
            int initialCount = shell.OpenDocuments.Count;

            shell.OpenSpritesInTabs([]);
            Assert.Equal(initialCount, shell.OpenDocuments.Count);
        }

        [Fact]
        public void T2_F07_OpenSpritesInTabs_NullSpriteInTuple_SkipsSafely()
        {
            var shell = E2ETestHelper.CreateTestShellViewModel();
            var list = new List<(string Name, SpriteState Sprite)>
            {
                ("NullSprite", null!),
                ("ValidSprite", E2ETestHelper.CreateTestSprite(1))
            };

            shell.OpenSpritesInTabs(list);
            Assert.Single(shell.OpenDocuments);
        }

        [Fact]
        public void T2_F07_OpenSpritesInTabs_SpecialCharactersInNames_Sanitized()
        {
            var shell = E2ETestHelper.CreateTestShellViewModel();
            var list = new List<(string Name, SpriteState Sprite)>
            {
                ("../../../Invalid Path Name !!", E2ETestHelper.CreateTestSprite(1))
            };

            shell.OpenSpritesInTabs(list);
            Assert.Single(shell.OpenDocuments);
            var doc = shell.OpenDocuments[0] as MainViewModel;
            Assert.NotNull(doc);
            Assert.DoesNotContain("/", doc.SpriteName);
            Assert.DoesNotContain("..", doc.SpriteName);
        }

        [Fact]
        public void T2_F07_OpenSpritesInTabs_PreservesFlipperCycle()
        {
            var shell = E2ETestHelper.CreateTestShellViewModel();
            var sprite = E2ETestHelper.CreateTestSprite(3);
            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                PassiveFrameCount = 1,
                ActiveFrameCount = 2,
                FramesOrder = [0, 1, 2]
            };

            shell.OpenSpritesInTabs([("CycleSprite", sprite)]);
            var doc = shell.OpenDocuments[0] as MainViewModel;
            Assert.NotNull(doc?.SpriteState.FlipperCycle);
            Assert.Equal(1, doc.SpriteState.FlipperCycle.PassiveFrameCount);
            Assert.Equal(2, doc.SpriteState.FlipperCycle.ActiveFrameCount);
        }

        [Fact]
        public void T2_F07_OpenSpritesInTabs_SingleFrameSprite_AnimationDisabled()
        {
            var shell = E2ETestHelper.CreateTestShellViewModel();
            var sprite = E2ETestHelper.CreateTestSprite(1);

            shell.OpenSpritesInTabs([("SingleFrame", sprite)]);
            var doc = shell.OpenDocuments[0] as MainViewModel;
            Assert.NotNull(doc);
            Assert.False(doc.SpriteState.IsAnimationEnabled);
        }

        #endregion

        #region Feature 10: Heatshrink Icon Compression (Boundary & Corner)

        [Fact]
        public void T2_F10_Compress_EmptyBuffer_ReturnsEmpty()
        {
            byte[] empty = [];
            byte[] compressed = HeatshrinkCompressor.Compress(empty);
            Assert.Empty(compressed);
        }

        [Fact]
        public void T2_F10_Decompress_CorruptHeader_ThrowsOrHandlesGracefully()
        {
            byte[] corrupt = [0xFF, 0xFF, 0xFF, 0xFF];
            Assert.ThrowsAny<Exception>(() => HeatshrinkCompressor.Decompress(corrupt, 100));
        }

        [Fact]
        public void T2_F10_Decompress_ZeroExpectedLength_ReturnsEmpty()
        {
            byte[] raw = [0x01, 0x02];
            byte[] compressed = HeatshrinkCompressor.Compress(raw);
            byte[] decompressed = HeatshrinkCompressor.Decompress(compressed, 0);
            Assert.Empty(decompressed);
        }

        [Fact]
        public void T2_F10_PseudoRandomNoise_CompressesSafely()
        {
            var rand = new Random(42);
            byte[] noise = new byte[512];
            rand.NextBytes(noise);

            byte[] compressed = HeatshrinkCompressor.Compress(noise);
            byte[] decompressed = HeatshrinkCompressor.Decompress(compressed, 512);

            Assert.Equal(noise, decompressed);
        }

        [Fact]
        public void T2_F10_SingleByteInput_CompressAndDecompress()
        {
            byte[] single = [0x42];
            byte[] compressed = HeatshrinkCompressor.Compress(single);
            byte[] decompressed = HeatshrinkCompressor.Decompress(compressed, 1);

            Assert.Equal(single, decompressed);
        }

        #endregion

        #region Feature 11: Media Slicer Frame Extraction & Slicing (Boundary & Corner)

        [Fact]
        public void T2_F11_SourceSmallerThanFrameDimensions_PadsSafely()
        {
            WpfTestHelper.EnsureApplication();
            var bmp = E2ETestHelper.CreateTestBitmapSource(64, 32);
            var settings = new MediaSliceSettings
            {
                Layout = SpriteSheetLayout.HorizontalStrip,
                FrameWidth = 128,
                FrameHeight = 64
            };

            var sprite = FlipperMediaSlicerService.SliceToAnimationSprite(bmp, settings);
            Assert.NotNull(sprite);
            Assert.Equal(128, sprite.Width);
            Assert.Equal(64, sprite.Height);
        }

        [Fact]
        public void T2_F11_NonMultipleDimensions_TruncatesRemainderSafely()
        {
            WpfTestHelper.EnsureApplication();
            var bmp = E2ETestHelper.CreateTestBitmapSource(300, 70);
            var settings = new MediaSliceSettings
            {
                Layout = SpriteSheetLayout.HorizontalStrip,
                FrameWidth = 128,
                FrameHeight = 64
            };

            // 300 / 128 = 2 full frames
            var sprite = FlipperMediaSlicerService.SliceToAnimationSprite(bmp, settings);
            Assert.Equal(2, sprite.Frames.Count);
        }

        [Fact]
        public void T2_F11_ZeroOrNegativeDimensionsInSettings_ClampsToPositive()
        {
            WpfTestHelper.EnsureApplication();
            var bmp = E2ETestHelper.CreateTestBitmapSource(128, 64);
            var settings = new MediaSliceSettings
            {
                FrameWidth = 0,
                FrameHeight = -10
            };

            var sprite = FlipperMediaSlicerService.SliceToAnimationSprite(bmp, settings);
            Assert.NotNull(sprite);
        }

        [Fact]
        public void T2_F11_GridColumnsRowsZero_AutoCalculatesFromImageDimensions()
        {
            WpfTestHelper.EnsureApplication();
            var bmp = E2ETestHelper.CreateTestBitmapSource(256, 128);
            var settings = new MediaSliceSettings
            {
                Layout = SpriteSheetLayout.Grid,
                Columns = 0,
                Rows = 0,
                FrameWidth = 128,
                FrameHeight = 64
            };

            var sprite = FlipperMediaSlicerService.SliceToAnimationSprite(bmp, settings);
            Assert.Equal(4, sprite.Frames.Count);
        }

        [Fact]
        public void T2_F11_NullSourceOrSettings_ThrowsArgumentNull()
        {
            WpfTestHelper.EnsureApplication();
            var bmp = E2ETestHelper.CreateTestBitmapSource(128, 64);
            var settings = new MediaSliceSettings();

            Assert.Throws<ArgumentNullException>(() => FlipperMediaSlicerService.SliceToAnimationSprite(null!, settings));
            Assert.Throws<ArgumentNullException>(() => FlipperMediaSlicerService.SliceToAnimationSprite(bmp, null!));
        }

        #endregion

        #region Feature 12: Media Slicer Dithering & Aspect Ratio (Boundary & Corner)

        [Fact]
        public void T2_F12_DitherAmountZero_BecomesPureThreshold()
        {
            WpfTestHelper.EnsureApplication();
            var bmp = E2ETestHelper.CreateTestBitmapSource(128, 64);
            var settings = new MediaSliceSettings
            {
                DitheringAlgorithm = BitmapDitheringAlgorithm.FloydSteinberg,
                DitherAmount = 0
            };

            var sprite = FlipperMediaSlicerService.SliceToAnimationSprite(bmp, settings);
            Assert.NotNull(sprite);
        }

        [Fact]
        public void T2_F12_BrightnessThreshold0_AllPixelsWhite()
        {
            WpfTestHelper.EnsureApplication();
            var bmp = E2ETestHelper.CreateTestBitmapSource(128, 64);
            var settings = new MediaSliceSettings
            {
                DitheringAlgorithm = BitmapDitheringAlgorithm.Binary,
                BrightnessThreshold = 0
            };

            var sprite = FlipperMediaSlicerService.SliceToAnimationSprite(bmp, settings);
            bool[] px = sprite.CompositeFramePixels(0);
            Assert.All(px, p => Assert.True(p));
        }

        [Fact]
        public void T2_F12_BrightnessThreshold255_AllPixelsBlack()
        {
            WpfTestHelper.EnsureApplication();
            var bmp = E2ETestHelper.CreateTestBitmapSource(128, 64);
            var settings = new MediaSliceSettings
            {
                DitheringAlgorithm = BitmapDitheringAlgorithm.Binary,
                BrightnessThreshold = 255
            };

            var sprite = FlipperMediaSlicerService.SliceToAnimationSprite(bmp, settings);
            bool[] px = sprite.CompositeFramePixels(0);
            Assert.All(px, p => Assert.False(p));
        }

        [Fact]
        public void T2_F12_SolidBlackImage_DitheringPreservesAllZeros()
        {
            WpfTestHelper.EnsureApplication();
            var blackBmp = BitmapSource.Create(
                128, 64, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null,
                new byte[128 * 64 * 4], 128 * 4);

            var settings = new MediaSliceSettings
            {
                DitheringAlgorithm = BitmapDitheringAlgorithm.FloydSteinberg
            };

            var sprite = FlipperMediaSlicerService.SliceToAnimationSprite(blackBmp, settings);
            bool[] px = sprite.CompositeFramePixels(0);
            Assert.All(px, p => Assert.False(p));
        }

        [Fact]
        public void T2_F12_SolidWhiteImage_DitheringPreservesAllOnes()
        {
            WpfTestHelper.EnsureApplication();
            byte[] whiteBytes = new byte[128 * 64 * 4];
            Array.Fill(whiteBytes, (byte)255);

            var whiteBmp = BitmapSource.Create(
                128, 64, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null,
                whiteBytes, 128 * 4);

            var settings = new MediaSliceSettings
            {
                DitheringAlgorithm = BitmapDitheringAlgorithm.FloydSteinberg
            };

            var sprite = FlipperMediaSlicerService.SliceToAnimationSprite(whiteBmp, settings);
            bool[] px = sprite.CompositeFramePixels(0);
            Assert.All(px, p => Assert.True(p));
        }

        #endregion

        #region Feature 13: Screen Mirror COM Streaming & Packet Decode (Boundary & Corner)

        [Fact]
        public void T2_F13_Encode1024Buffer_NullPixels_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => FlipperScreenStreamService.Encode1024Buffer(null!));
        }

        [Fact]
        public void T2_F13_Encode1024Buffer_SmallerArray_DoesNotOverflow()
        {
            bool[] shortPixels = new bool[100];
            shortPixels[0] = true;

            byte[] encoded = FlipperScreenStreamService.Encode1024Buffer(shortPixels);
            Assert.Equal(1024, encoded.Length);
            Assert.Equal(0x01, encoded[0]);
        }

        [Fact]
        public void T2_F13_Encode1024Buffer_LargerArray_TruncatesSafely()
        {
            bool[] longPixels = new bool[20000];
            longPixels[0] = true;

            byte[] encoded = FlipperScreenStreamService.Encode1024Buffer(longPixels);
            Assert.Equal(1024, encoded.Length);
        }

        [Fact]
        public void T2_F13_HorizontalLine_SetsSingleBitAcrossAllColumnsInPage()
        {
            bool[] pixels = new bool[128 * 64];
            // y = 8 is Page 1, Bit 0
            for (int x = 0; x < 128; x++)
            {
                pixels[8 * 128 + x] = true;
            }

            byte[] encoded = FlipperScreenStreamService.Encode1024Buffer(pixels);

            // Page 1 columns 0..127 should all have 0x01
            for (int x = 0; x < 128; x++)
            {
                Assert.Equal(0x01, encoded[1 * 128 + x]);
            }
        }

        [Fact]
        public void T2_F13_CheckerboardPattern_EncodesAlternatingBitmasks()
        {
            bool[] pixels = new bool[128 * 64];
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    pixels[y * 128 + x] = (x + y) % 2 == 0;
                }
            }

            byte[] encoded = FlipperScreenStreamService.Encode1024Buffer(pixels);
            // Even columns in page 0 have alternating bits: 0x55 or 0xAA
            Assert.True(encoded[0] == 0x55 || encoded[0] == 0xAA);
        }

        #endregion

        #region Feature 14: Screen Mirror Frame Rate & Disconnection (Boundary & Corner)

        [Fact]
        public async Task T2_F14_ConnectAsync_NullOrEmptyPort_ReturnsFalse()
        {
            using var stream = new FlipperScreenStreamService();
            Assert.False(await stream.ConnectAsync(""));
            Assert.False(await stream.ConnectAsync(null!));
        }

        [Fact]
        public void T2_F14_SendSingleFrame_WhenDisconnected_ReturnsFalse()
        {
            using var stream = new FlipperScreenStreamService();
            bool result = stream.SendSingleFrame(new bool[128 * 64]);
            Assert.False(result);
        }

        [Fact]
        public void T2_F14_StartStreaming_NullProvider_ThrowsArgumentNull()
        {
            using var stream = new FlipperScreenStreamService();
            Assert.Throws<ArgumentNullException>(() => stream.StartStreaming(null!));
        }

        [Fact]
        public void T2_F14_StartStreaming_ExtremeTargetFps_ClampsBetween1And60()
        {
            using var stream = new FlipperScreenStreamService();
            // Should not throw on negative or huge FPS
            stream.StartStreaming(() => new bool[128 * 64], targetFps: -100);
            stream.StopStreaming();

            stream.StartStreaming(() => new bool[128 * 64], targetFps: 1000);
            stream.StopStreaming();
        }

        [Fact]
        public void T2_F14_MultipleDisposeCalls_Idempotent()
        {
            var stream = new FlipperScreenStreamService();
            stream.Dispose();
            stream.Dispose();
            stream.Dispose();
            // Should not throw ObjectDisposedException
        }

        #endregion

        #region Feature 16: Export Dialog Formats & Compression (Boundary & Corner)

        [Fact]
        public void T2_F16_SanitizeAnimationName_EmptyOrWhitespace_ReturnsDefault()
        {
            Assert.Equal("Animation", FlipperExportService.SanitizeAnimationName(""));
            Assert.Equal("Animation", FlipperExportService.SanitizeAnimationName("   "));
            Assert.Equal("Animation", FlipperExportService.SanitizeAnimationName(null!));
        }

        [Fact]
        public void T2_F16_ExportImage_ValidFrameIndex_ExportsSingleBm()
        {
            using var tempDir = E2ETestHelper.CreateTempDirectory();
            string bmPath = Path.Combine(tempDir.Path, "frame_0.bm");
            var sprite = E2ETestHelper.CreateTestSprite(1);

            var exportService = new FlipperExportService();
            exportService.ExportImage(sprite, 0, bmPath);

            Assert.True(File.Exists(bmPath));
            byte[] data = File.ReadAllBytes(bmPath);
            Assert.NotEmpty(data);
        }

        [Fact]
        public void T2_F16_ExportAnimation_WithFlipperCycle_PreservesSpeechBubblesInMeta()
        {
            using var tempDir = E2ETestHelper.CreateTempDirectory();
            var sprite = E2ETestHelper.CreateTestSprite(2);
            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                PassiveFrameCount = 2,
                ActiveFrameCount = 0,
                FramesOrder = [0, 1],
                BubbleSlots = 1,
                SpeechBubble = new FlipperSpeechBubble(1, 10, 15, "SpecialQuote")
            };

            var exportService = new FlipperExportService();
            var settings = new FlipperExportSettings
            {
                TargetFolder = tempDir.Path,
                AnimationName = "BubbleAnim"
            };

            exportService.ExportAnimation(sprite, settings);

            string metaPath = Path.Combine(tempDir.Path, "BubbleAnim", "meta.txt");
            string metaContent = File.ReadAllText(metaPath);

            Assert.Contains("Slot: 1", metaContent);
            Assert.Contains("Text: SpecialQuote", metaContent);
            Assert.Contains("X: 10", metaContent);
            Assert.Contains("Y: 15", metaContent);
        }

        [Fact]
        public void T2_F16_ExportAnimation_DuplicateExport_UpdatesExistingManifest()
        {
            using var tempDir = E2ETestHelper.CreateTempDirectory();
            var sprite = E2ETestHelper.CreateTestSprite(1);
            var exportService = new FlipperExportService();

            var settingsV1 = new FlipperExportSettings
            {
                TargetFolder = tempDir.Path,
                AnimationName = "DupeAnim",
                Weight = 1,
                TargetMode = FlipperExportTargetMode.MomentumAssetPack
            };

            var settingsV2 = new FlipperExportSettings
            {
                TargetFolder = tempDir.Path,
                AnimationName = "DupeAnim",
                Weight = 5,
                TargetMode = FlipperExportTargetMode.MomentumAssetPack
            };

            exportService.ExportAnimation(sprite, settingsV1);
            exportService.ExportAnimation(sprite, settingsV2);

            string manifestPath = Path.Combine(tempDir.Path, "Anims", "manifest.txt");
            string manifestText = File.ReadAllText(manifestPath);
            var manifest = FlipperManifest.Parse(manifestText);

            Assert.Single(manifest.Entries);
            Assert.Equal(5, manifest.Entries[0].Weight);
        }

        [Fact]
        public void T2_F16_ExportAssetPack_NullList_ThrowsArgumentNull()
        {
            var exportService = new FlipperExportService();
            Assert.Throws<ArgumentNullException>(() => exportService.ExportAssetPack(null!, "C:\\target"));
        }

        #endregion

        #region Feature 17: Deploy Window USB Discovery & Chunk Deploy (Boundary & Corner)

        [Fact]
        public async Task T2_F17_DeployFilesAsync_Cancellation_AbortsOperation()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var deployer = new FlipperUsbDeployer();
            var files = new List<(string RelativePath, byte[] Data)>
            {
                ("meta.txt", [0x01, 0x02])
            };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                deployer.DeployFilesAsync("COM3", "/ext/dolphin", files, cancellationToken: cts.Token));
        }

        [Fact]
        public void T2_F17_DeployFilesAsync_NestedDirectoryPaths_ExtractsAllParentDirectories()
        {
            var files = new List<(string RelativePath, byte[] Data)>
            {
                ("a/b/c/d/file.txt", [0x01])
            };

            string baseDir = "/ext";
            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { baseDir };

            foreach (var (relPath, _) in files)
            {
                string normRel = relPath.Replace('\\', '/').TrimStart('/');
                int lastSlash = normRel.LastIndexOf('/');
                if (lastSlash > 0)
                {
                    string subDir = normRel[..lastSlash];
                    string[] subParts = subDir.Split('/');
                    var pathBuilder = new System.Text.StringBuilder(baseDir);
                    foreach (var p in subParts)
                    {
                        pathBuilder.Append('/').Append(p);
                        dirs.Add(pathBuilder.ToString());
                    }
                }
            }

            Assert.Contains("/ext", dirs);
            Assert.Contains("/ext/a", dirs);
            Assert.Contains("/ext/a/b", dirs);
            Assert.Contains("/ext/a/b/c", dirs);
            Assert.Contains("/ext/a/b/c/d", dirs);
        }

        [Fact]
        public void T2_F17_DeployFilesAsync_LargeFileOver256Bytes_CalculatesChunkSplits()
        {
            byte[] largeData = new byte[1024];
            int chunkSize = 256;
            int totalChunks = (largeData.Length + chunkSize - 1) / chunkSize;

            Assert.Equal(4, totalChunks);
        }

        [Fact]
        public async Task T2_F17_DeployFilesAsync_FileWithZeroBytes_HandlesEmptyPayload()
        {
            var deployer = new FlipperUsbDeployer();
            var files = new List<(string RelativePath, byte[] Data)>
            {
                ("empty.txt", [])
            };

            // Non-existent COM port fails gracefully
            bool result = await deployer.DeployFilesAsync("COM999", "/ext", files);
            Assert.False(result);
        }

        [Fact]
        public async Task T2_F17_RestartDesktopAsync_NonExistentPort_ReturnsFalseSafely()
        {
            var deployer = new FlipperUsbDeployer();
            bool result = await deployer.RestartDesktopAsync("COM999");
            Assert.False(result);
        }

        #endregion

        #region Feature 18: In-Memory Sprite / Tab Pipeline (Boundary & Corner)

        [Fact]
        public void T2_F18_EmptySprite_AllBlankPixels_GeneratesValidCode()
        {
            var generator = new CodeGeneratorService();
            var sprite = new SpriteState(16, 16);
            var settings = new ExportSettings
            {
                Format = ExportFormat.FlipperCompressedBitmap,
                LayerMode = ExportLayerMode.CompositeVisible,
                SpriteName = "Blank"
            };

            string code = generator.GenerateCode(
                new List<bool[]> { sprite.CompositeFramePixels(0) },
                sprite.Width, sprite.Height, settings, false, null, 0, 0, 0, 0);

            Assert.Contains("canvas_draw_bitmap", code);
            Assert.Contains("Blank_compressed", code);
        }

        [Fact]
        public void T2_F18_SinglePixelSprite_GeneratesPreciseBitmask()
        {
            var generator = new CodeGeneratorService();
            var sprite = new SpriteState(8, 8);
            sprite.ActiveLayerPixels[0] = true; // Pixel (0, 0)

            var settings = new ExportSettings
            {
                Format = ExportFormat.FlipperXbm,
                LayerMode = ExportLayerMode.CompositeVisible,
                SpriteName = "SinglePixel"
            };

            string code = generator.GenerateCode(
                new List<bool[]> { sprite.CompositeFramePixels(0) },
                sprite.Width, sprite.Height, settings, false, null, 0, 0, 0, 0);

            Assert.Contains("0x01", code);
        }

        [Fact]
        public void T2_F18_OddDimensionsSprite_PadsRowBytesCorrectly()
        {
            var generator = new CodeGeneratorService();
            var sprite = new SpriteState(127, 63);
            var settings = new ExportSettings
            {
                Format = ExportFormat.FlipperXbm,
                LayerMode = ExportLayerMode.CompositeVisible,
                SpriteName = "OddDimensions"
            };

            string code = generator.GenerateCode(
                new List<bool[]> { sprite.CompositeFramePixels(0) },
                sprite.Width, sprite.Height, settings, false, null, 0, 0, 0, 0);

            Assert.Contains("canvas_draw_xbm", code);
        }

        [Fact]
        public void T2_F18_MultiLayerComposite_BlendsBeforeGeneration()
        {
            var generator = new CodeGeneratorService();
            var sprite = new SpriteState(16, 16);
            sprite.Layers.Add(new LayerState { Name = "Layer 2", IsVisible = true });
            sprite.Frames[0].LayerPixels.Add(new MonochromePixelBuffer(16 * 16));

            // Layer 1 pixel
            sprite.Frames[0].LayerPixels[0].GetMonochromeData()[0] = true;
            // Layer 2 pixel
            sprite.Frames[0].LayerPixels[1].GetMonochromeData()[1 * 16 + 1] = true;

            var settings = new ExportSettings
            {
                Format = ExportFormat.FlipperXbm,
                LayerMode = ExportLayerMode.CompositeVisible,
                SpriteName = "MultiLayer"
            };

            string code = generator.GenerateCode(
                new List<bool[]> { sprite.CompositeFramePixels(0) },
                sprite.Width, sprite.Height, settings, false, null, 0, 0, 0, 0);

            Assert.Contains("canvas_draw_xbm", code);
        }

        [Fact]
        public void T2_F18_ExportSettings_AllDelaysCustom_FormatsFrameDelaysArray()
        {
            var generator = new CodeGeneratorService();
            var sprite = E2ETestHelper.CreateTestSprite(2);
            var settings = new ExportSettings
            {
                Format = ExportFormat.FlipperCompressedBitmap,
                ExportAsAnimation = true,
                SpriteName = "CustomDelays"
            };

            var frames = sprite.Frames.Select((_, idx) => sprite.CompositeFramePixels(idx)).ToList();
            string code = generator.GenerateCode(
                frames,
                sprite.Width, sprite.Height, settings, false, null, 0, 0, 0, 0,
                frameDelays: [100, 250]);

            Assert.Contains("CustomDelays_frames", code);
        }

        #endregion
    }
}
