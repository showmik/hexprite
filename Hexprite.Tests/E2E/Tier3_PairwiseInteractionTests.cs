using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Hexprite.Core;
using Hexprite.Resources.Fonts;
using Hexprite.Services;
using Hexprite.Services.Compression;
using Hexprite.ViewModels;
using Xunit;

namespace Hexprite.Tests.E2E
{
    /// <summary>
    /// Tier 3: Pairwise Combinatorial Interaction E2E Tests (20 tests covering cross-feature interactions).
    /// Opaque-box verification of multi-component interaction contracts.
    /// </summary>
    [Trait("Category", "E2E")]
    [Trait("Category", "Integration")]
    public class Tier3_PairwiseInteractionTests
    {
        [Fact]
        public void T3_P01_Simulation_And_SpeechBubbles()
        {
            // Simulation cycle + speech bubble rendering
            var sprite = E2ETestHelper.CreateTestSprite(4);
            var bubble = new FlipperSpeechBubble(1, 10, 10, "SimBubble", SpeechBubbleTailPosition.BottomLeft)
            {
                StartFrame = 1,
                EndFrame = 2
            };

            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                PassiveFrameCount = 2,
                ActiveFrameCount = 2,
                FramesOrder = [0, 1, 2, 3],
                SpeechBubble = bubble
            };

            // On frame 1 (within StartFrame..EndFrame), draw bubble onto composite pixels
            bool[] frame1Pixels = sprite.CompositeFramePixels(1);
            bubble.Draw(frame1Pixels, 128, 64);

            Assert.Contains(true, frame1Pixels);
            Assert.Contains(false, frame1Pixels);
        }

        [Fact]
        public void T3_P02_Simulation_And_ScheduleMatrix()
        {
            // Filter candidates through schedule matrix and simulate candidate selection
            var entries = new List<FlipperManifestEntry>
            {
                new() { Name = "BabyWalk", MinLevel = 1, MaxLevel = 10, MinButthurt = 0, MaxButthurt = 14, Weight = 2 },
                new() { Name = "AdultRun", MinLevel = 11, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 }
            };

            var matrix = new FlipperScheduleMatrix(entries);
            var babyCell = matrix.GetCell(5, 0);
            var adultCell = matrix.GetCell(20, 0);

            Assert.Equal("BabyWalk", babyCell.MatchingEntries[0].Name);
            Assert.Equal("AdultRun", adultCell.MatchingEntries[0].Name);
        }

        [Fact]
        public void T3_P03_AssetPack_And_ImportService()
        {
            using var tempDir = E2ETestHelper.CreateTempDirectory();
            string packDir = E2ETestHelper.CreateTestAssetPack("momentum_cyber_hacker", tempDir.Path);

            var importService = new FlipperImportService();
            var imported = importService.ImportAssetPack(packDir);

            Assert.True(imported.Count >= 3);
            Assert.Contains(imported, a => a.Name.Contains("momentum_cyber_hacker"));
        }

        [Fact]
        public void T3_P04_AssetPack_And_ScheduleMatrix()
        {
            using var tempDir = E2ETestHelper.CreateTempDirectory();
            string packDir = E2ETestHelper.CreateTestAssetPack("retro_arcade_8bit", tempDir.Path);

            var importService = new FlipperImportService();
            var imported = importService.ImportAssetPack(packDir);
            var manifestEntries = imported.Select(i => i.ManifestEntry).ToList();

            var matrix = new FlipperScheduleMatrix(manifestEntries);
            Assert.True(matrix.CoveredCellsCount > 0);

            var balanced = FlipperScheduleMatrix.AutoBalanceEntries(manifestEntries);
            var balancedMatrix = new FlipperScheduleMatrix(balanced);
            Assert.Equal(100.0, balancedMatrix.CoveragePercentage);
        }

        [Fact]
        public void T3_P05_MediaSlicer_And_HeatshrinkCompression()
        {
            WpfTestHelper.EnsureApplication();
            var bmp = E2ETestHelper.CreateTestBitmapSource(256, 64);
            var sliceSettings = new MediaSliceSettings
            {
                Layout = SpriteSheetLayout.HorizontalStrip,
                FrameWidth = 128,
                FrameHeight = 64
            };

            var sprite = FlipperMediaSlicerService.SliceToAnimationSprite(bmp, sliceSettings);
            Assert.Equal(2, sprite.Frames.Count);

            // Compress both frames using Heatshrink and decompress back
            for (int f = 0; f < sprite.Frames.Count; f++)
            {
                bool[] px = sprite.CompositeFramePixels(f);
                byte[] raw = new byte[1024];
                for (int y = 0; y < 64; y++)
                {
                    for (int x = 0; x < 128; x++)
                    {
                        if (px[y * 128 + x])
                        {
                            raw[(y / 8) * 128 + x] |= (byte)(1 << (y % 8));
                        }
                    }
                }

                byte[] compressed = HeatshrinkCompressor.Compress(raw);
                byte[] decompressed = HeatshrinkCompressor.Decompress(compressed, 1024);
                Assert.Equal(raw, decompressed);
            }
        }

        [Fact]
        public void T3_P08_ScreenMirror_And_MediaSlicer()
        {
            WpfTestHelper.EnsureApplication();
            var bmp = E2ETestHelper.CreateTestBitmapSource(256, 64);
            var sliceSettings = new MediaSliceSettings
            {
                Layout = SpriteSheetLayout.HorizontalStrip,
                FrameWidth = 128,
                FrameHeight = 64
            };

            var sprite = FlipperMediaSlicerService.SliceToAnimationSprite(bmp, sliceSettings);
            for (int f = 0; f < sprite.Frames.Count; f++)
            {
                bool[] px = sprite.CompositeFramePixels(f);
                byte[] streamBuffer = FlipperScreenStreamService.Encode1024Buffer(px);
                Assert.Equal(1024, streamBuffer.Length);

                bool[] decoded = E2ETestHelper.Decode1024Buffer(streamBuffer);
                Assert.Equal(px, decoded);
            }
        }

        [Fact]
        public void T3_P10_ExportService_And_ImportService()
        {
            using var tempDir = E2ETestHelper.CreateTempDirectory();
            var original = E2ETestHelper.CreateTestSprite(3);
            var exportService = new FlipperExportService();
            var settings = new FlipperExportSettings
            {
                TargetFolder = tempDir.Path,
                AnimationName = "RoundtripAnim",
                FrameRate = 10,
                PassiveFrames = 3,
                ActiveFrames = 0
            };

            exportService.ExportAnimation(original, settings);

            var importService = new FlipperImportService();
            string metaPath = Path.Combine(tempDir.Path, "RoundtripAnim", "meta.txt");
            var imported = importService.ImportAnimation(metaPath);

            Assert.Equal(original.Width, imported.Width);
            Assert.Equal(original.Height, imported.Height);
            Assert.Equal(original.Frames.Count, imported.Frames.Count);
        }

        [Fact]
        public void T3_P11_ExportService_And_AssetPackZipImport()
        {
            using var tempDir = E2ETestHelper.CreateTempDirectory();
            string zipPath = Path.Combine(tempDir.Path, "Pack.zip");
            var sprite = E2ETestHelper.CreateTestSprite(2);
            var entry = new FlipperManifestEntry { Name = "PackAnim", Weight = 1 };
            var settings = new FlipperExportSettings { AnimationName = "PackAnim" };

            var exportService = new FlipperExportService();
            exportService.ExportAssetPackZip([(sprite, entry, settings)], zipPath);

            // Import back the zip archive
            var importService = new FlipperImportService();
            var packAnimations = importService.ImportAssetPack(zipPath);

            Assert.Single(packAnimations);
            Assert.Equal("PackAnim", packAnimations[0].Name);
        }

        [Fact]
        public void T3_P12_ScheduleMatrix_And_ExportService()
        {
            using var tempDir = E2ETestHelper.CreateTempDirectory();
            var anim1 = (E2ETestHelper.CreateTestSprite(2), new FlipperManifestEntry { Name = "Anim1" }, new FlipperExportSettings { AnimationName = "Anim1" });
            var anim2 = (E2ETestHelper.CreateTestSprite(2), new FlipperManifestEntry { Name = "Anim2" }, new FlipperExportSettings { AnimationName = "Anim2" });

            var entries = new List<FlipperManifestEntry> { anim1.Item2, anim2.Item2 };
            var balancedEntries = FlipperScheduleMatrix.AutoBalanceEntries(entries);

            anim1.Item2.MinLevel = balancedEntries[0].MinLevel;
            anim1.Item2.MaxLevel = balancedEntries[0].MaxLevel;
            anim2.Item2.MinLevel = balancedEntries[1].MinLevel;
            anim2.Item2.MaxLevel = balancedEntries[1].MaxLevel;

            var exportService = new FlipperExportService();
            exportService.ExportAssetPack([anim1, anim2], tempDir.Path, isMomentum: true);

            string manifestPath = Path.Combine(tempDir.Path, "Anims", "manifest.txt");
            var manifest = FlipperManifest.Parse(File.ReadAllText(manifestPath));
            var matrix = new FlipperScheduleMatrix(manifest.Entries);

            Assert.Equal(100.0, matrix.CoveragePercentage);
        }

        [Fact]
        public void T3_P13_DeployWindow_And_ExportService()
        {
            var sprite = E2ETestHelper.CreateTestSprite(2);
            var exportService = new FlipperExportService();
            var settings = new FlipperExportSettings
            {
                AnimationName = "DeployAnim",
                TargetMode = FlipperExportTargetMode.MomentumAssetPack
            };

            var files = exportService.GenerateDeploymentFiles(sprite, settings);
            Assert.NotEmpty(files);
            Assert.Contains(files, f => f.RelativePath.Contains("meta.txt"));
            Assert.Contains(files, f => f.RelativePath.Contains("frame_0.bm"));
        }

        [Fact]
        public void T3_P14_UiTemplate_And_ScreenMirror()
        {
            var sprite = new SpriteState(128, 64);
            FlipperUiTemplateService.ApplyTemplate(sprite, FlipperUiTemplateType.HeaderBar, "433.92 MHz");

            bool[] px = sprite.ActiveLayerPixels;
            byte[] encoded = FlipperScreenStreamService.Encode1024Buffer(px);
            bool[] decoded = E2ETestHelper.Decode1024Buffer(encoded);

            Assert.Equal(px, decoded);
        }

        [Fact]
        public void T3_P16_SpeechBubble_And_ExportService()
        {
            using var tempDir = E2ETestHelper.CreateTempDirectory();
            var sprite = E2ETestHelper.CreateTestSprite(2);
            var bubble = new FlipperSpeechBubble(1, 15, 20, "PairwiseBubble", SpeechBubbleTailPosition.BottomRight);

            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                PassiveFrameCount = 2,
                ActiveFrameCount = 0,
                FramesOrder = [0, 1],
                BubbleSlots = 1,
                SpeechBubbles = [bubble]
            };

            var exportService = new FlipperExportService();
            var settings = new FlipperExportSettings { TargetFolder = tempDir.Path, AnimationName = "BubblePair" };
            exportService.ExportAnimation(sprite, settings);

            var importService = new FlipperImportService();
            string metaPath = Path.Combine(tempDir.Path, "BubblePair", "meta.txt");
            var imported = importService.ImportAnimation(metaPath);

            Assert.NotNull(imported.FlipperCycle);
            Assert.NotEmpty(imported.FlipperCycle.SpeechBubbles);
            Assert.Equal("PairwiseBubble", imported.FlipperCycle.SpeechBubbles[0].Text);
        }

        [Fact]
        public void T3_P17_InvertColors_And_HeatshrinkCompression()
        {
            WpfTestHelper.EnsureApplication();
            var bmp = E2ETestHelper.CreateTestBitmapSource(128, 64);
            var sliceSettings = new MediaSliceSettings
            {
                DitheringAlgorithm = BitmapDitheringAlgorithm.Binary,
                InvertColors = true
            };

            var sprite = FlipperMediaSlicerService.SliceToAnimationSprite(bmp, sliceSettings);
            bool[] px = sprite.CompositeFramePixels(0);
            byte[] encoded = FlipperScreenStreamService.Encode1024Buffer(px);

            byte[] compressed = HeatshrinkCompressor.Compress(encoded);
            byte[] decompressed = HeatshrinkCompressor.Decompress(compressed, 1024);

            Assert.Equal(encoded, decompressed);
        }

        [Fact]
        public void T3_P18_ScreenMirror_And_InvertPalette()
        {
            bool[] normalPx = new bool[128 * 64];
            normalPx[0] = true;
            normalPx[100] = true;

            bool[] invertedPx = normalPx.Select(p => !p).ToArray();

            byte[] normalBuffer = FlipperScreenStreamService.Encode1024Buffer(normalPx);
            byte[] invertedBuffer = FlipperScreenStreamService.Encode1024Buffer(invertedPx);

            for (int i = 0; i < normalBuffer.Length; i++)
            {
                Assert.Equal(normalBuffer[i], (byte)~invertedBuffer[i]);
            }
        }

        [Fact]
        public void T3_P19_PackArchiver_And_ScheduleMatrix()
        {
            var sprite1 = E2ETestHelper.CreateTestSprite(2);
            var entry1 = new FlipperManifestEntry { Name = "Anim1", MinLevel = 1, MaxLevel = 15, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };

            var report = FlipperPackArchiverService.ValidatePack([("Anim1", sprite1, entry1)]);
            Assert.Equal(1, report.TotalAnimations);
            Assert.Equal(2, report.TotalFrames);
            Assert.Equal(225, report.MatrixCoveredCells);
            Assert.Equal(50.0, report.MatrixCoveragePercent);
        }

        [Fact]
        public void T3_P20_ShellViewModel_And_FlipperImport()
        {
            using var tempDir = E2ETestHelper.CreateTempDirectory();
            string packDir = E2ETestHelper.CreateTestAssetPack("minimalist_tech_suite", tempDir.Path);

            var importService = new FlipperImportService();
            var imported = importService.ImportAssetPack(packDir);
            var tuples = imported.Select(i => (i.Name, i.Sprite)).ToList();

            var shell = E2ETestHelper.CreateTestShellViewModel();
            shell.OpenSpritesInTabs(tuples);

            Assert.Equal(tuples.Count, shell.OpenDocuments.Count);
            Assert.NotNull(shell.ActiveDocument);
        }
    }
}
