using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Hexprite.ViewModels.Flipper;
using Xunit;

namespace Hexprite.Tests.E2E
{
    [Trait("Category", "Integration")]
    public class Milestone4ChallengerStressTests : IDisposable
    {
        private readonly string _testRoot;
        private readonly FlipperImportService _importService;
        private readonly FlipperExportService _exportService;

        public Milestone4ChallengerStressTests()
        {
            WpfTestHelper.EnsureApplication();
            _testRoot = Path.Combine(Path.GetTempPath(), "Hexprite_M4_Challenger_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testRoot);
            _importService = new FlipperImportService();
            _exportService = new FlipperExportService();
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testRoot))
                {
                    Directory.Delete(_testRoot, true);
                }
            }
            catch
            {
                // Ignore cleanup errors on temp root
            }
        }

        #region 1. Extreme Workload: Large Asset Pack (100+ Animations)

        [Fact]
        public void StressTest_LargeAssetPack_100Animations_IngestsAndExportsWithFullFidelity()
        {
            string packDir = Path.Combine(_testRoot, "MassivePack");
            string animsDir = Path.Combine(packDir, "Anims");
            Directory.CreateDirectory(animsDir);

            var manifestSb = new StringBuilder();
            manifestSb.AppendLine("Filetype: Flipper Animation Manifest");
            manifestSb.AppendLine("Version: 1");
            manifestSb.AppendLine();

            int animCount = 100;
            var expectedAnimations = new List<(string Name, int Frames, int MinLvl, int MaxLvl, int MinMood, int MaxMood)>();

            for (int i = 0; i < animCount; i++)
            {
                string name = $"DenseAnim_{i:D3}";
                int frameCount = (i % 5) + 2; // 2 to 6 frames
                int minLvl = (i % 30) + 1;
                int maxLvl = Math.Min(30, minLvl + (i % 3));
                int minMood = i % 15;
                int maxMood = Math.Min(14, minMood + (i % 2));
                int weight = (i % 10) + 1;

                expectedAnimations.Add((name, frameCount, minLvl, maxLvl, minMood, maxMood));

                // Write manifest entry
                manifestSb.AppendLine($"Name: {name}");
                manifestSb.AppendLine($"Min butthurt: {minMood}");
                manifestSb.AppendLine($"Max butthurt: {maxMood}");
                manifestSb.AppendLine($"Min level: {minLvl}");
                manifestSb.AppendLine($"Max level: {maxLvl}");
                manifestSb.AppendLine($"Weight: {weight}");
                manifestSb.AppendLine();

                // Create animation folder and files
                string folder = Path.Combine(animsDir, name);
                Directory.CreateDirectory(folder);

                string metaContent = $"Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nFrame rate: 10\n";
                File.WriteAllText(Path.Combine(folder, "meta.txt"), metaContent);

                for (int f = 0; f < frameCount; f++)
                {
                    byte[] bmData = CreateTestBmFile(128, 64, (byte)(i + f));
                    File.WriteAllBytes(Path.Combine(folder, $"frame_{f}.bm"), bmData);
                }
            }

            File.WriteAllText(Path.Combine(animsDir, "manifest.txt"), manifestSb.ToString());

            // 1. Ingest large pack
            var imported = _importService.ImportAssetPack(packDir);
            Assert.Equal(animCount, imported.Count);

            // 2. Validate all sprites and manifest entries
            for (int i = 0; i < animCount; i++)
            {
                var exp = expectedAnimations[i];
                var item = imported.FirstOrDefault(x => x.Name == exp.Name);
                Assert.NotNull(item.Sprite);
                Assert.NotNull(item.ManifestEntry);
                Assert.Equal(exp.Frames, item.Sprite.Frames.Count);
                Assert.Equal(128, item.Sprite.Width);
                Assert.Equal(64, item.Sprite.Height);
                Assert.Equal(exp.MinLvl, item.ManifestEntry.MinLevel);
                Assert.Equal(exp.MaxLvl, item.ManifestEntry.MaxLevel);
                Assert.Equal(exp.MinMood, item.ManifestEntry.MinButthurt);
                Assert.Equal(exp.MaxMood, item.ManifestEntry.MaxButthurt);
            }

            // 3. Export all 100 animations to a new folder
            string exportTarget = Path.Combine(_testRoot, "MassiveExported");
            var exportList = imported.Select(x => (
                x.Sprite,
                x.ManifestEntry,
                new FlipperExportSettings
                {
                    AnimationName = x.Name,
                    TargetMode = FlipperExportTargetMode.MomentumAssetPack,
                    FrameRate = 10,
                    MinLevel = x.ManifestEntry.MinLevel,
                    MaxLevel = x.ManifestEntry.MaxLevel,
                    MinButthurt = x.ManifestEntry.MinButthurt,
                    MaxButthurt = x.ManifestEntry.MaxButthurt,
                    Weight = x.ManifestEntry.Weight
                }
            )).ToList();

            _exportService.ExportAssetPack(exportList, exportTarget, isMomentum: true);

            // 4. Re-ingest exported pack and verify complete roundtrip
            var reimported = _importService.ImportAssetPack(exportTarget);
            Assert.Equal(animCount, reimported.Count);
            for (int i = 0; i < animCount; i++)
            {
                var exp = expectedAnimations[i];
                var reitem = reimported.FirstOrDefault(x => x.Name == exp.Name);
                Assert.NotNull(reitem.Sprite);
                Assert.Equal(exp.Frames, reitem.Sprite.Frames.Count);
            }
        }

        #endregion

        #region 2. High-Concurrency Parallel Export & Ingestion

        [Fact]
        public void StressTest_ParallelZipAndFolderExport_ThreadSafeAndZeroCorruptions()
        {
            int parallelWorkers = 16;
            var exceptions = new ConcurrentBag<Exception>();

            Parallel.For(0, parallelWorkers, new ParallelOptions { MaxDegreeOfParallelism = parallelWorkers }, workerId =>
            {
                try
                {
                    string workerDir = Path.Combine(_testRoot, $"Worker_{workerId}");
                    Directory.CreateDirectory(workerDir);

                    // Create test sprite and settings
                    var sprite = E2ETestHelper.CreateTestSprite(width: 128, height: 64, frameCount: 4);
                    var manifestEntry = new FlipperManifestEntry
                    {
                        Name = $"ParallelAnim_{workerId}",
                        MinLevel = 1,
                        MaxLevel = 30,
                        MinButthurt = 0,
                        MaxButthurt = 14,
                        Weight = 3
                    };

                    var settings = new FlipperExportSettings
                    {
                        AnimationName = manifestEntry.Name,
                        TargetMode = FlipperExportTargetMode.MomentumAssetPack,
                        FrameRate = 8,
                        MinLevel = 1,
                        MaxLevel = 30,
                        MinButthurt = 0,
                        MaxButthurt = 14,
                        Weight = 3
                    };

                    var animList = new List<(SpriteState, FlipperManifestEntry, FlipperExportSettings)>
                    {
                        (sprite, manifestEntry, settings)
                    };

                    // 1. Export as Directory Tree
                    string folderExport = Path.Combine(workerDir, "ExportedFolder");
                    _exportService.ExportAssetPack(animList, folderExport, isMomentum: true);

                    // 2. Export as Zip Archive
                    string zipExport = Path.Combine(workerDir, "ExportedPack.zip");
                    _exportService.ExportAssetPackZip(animList, zipExport, isMomentum: true);

                    // 3. Ingest from exported folder
                    var folderIngested = _importService.ImportAssetPack(folderExport);
                    Assert.Single(folderIngested);
                    Assert.Equal($"ParallelAnim_{workerId}", folderIngested[0].Name);

                    // 4. Ingest from exported zip
                    var zipIngested = _importService.ImportAssetPack(zipExport);
                    Assert.Single(zipIngested);
                    Assert.Equal($"ParallelAnim_{workerId}", zipIngested[0].Name);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            Assert.Empty(exceptions);
        }

        #endregion

        #region 3. Corrupted Archives & Boundary Conditions

        [Fact]
        public void StressTest_CorruptedZipWithTruncatedCentralDirectory_ThrowsGracefully()
        {
            string corruptZipPath = Path.Combine(_testRoot, "Truncated.zip");
            // Valid local file header signature but truncated stream
            byte[] truncatedBytes = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00];
            File.WriteAllBytes(corruptZipPath, truncatedBytes);

            Assert.ThrowsAny<Exception>(() => _importService.ImportAssetPack(corruptZipPath));
        }

        [Fact]
        public void StressTest_CorruptedZipWithZeroLength_ThrowsGracefully()
        {
            string emptyZip = Path.Combine(_testRoot, "Empty.zip");
            File.WriteAllBytes(emptyZip, []);

            Assert.ThrowsAny<Exception>(() => _importService.ImportAssetPack(emptyZip));
        }

        [Fact]
        public void StressTest_ManifestWithNonNumericAndMalformedLines_DoesNotCrash()
        {
            string sourceDir = Path.Combine(_testRoot, "MalformedManifestPack");
            string animsDir = Path.Combine(sourceDir, "Anims");
            Directory.CreateDirectory(animsDir);

            // Manifest containing invalid non-numeric fields, missing colons, binary nulls, invalid headers
            string malformedManifest = @"
Filetype: Flipper Animation Manifest
Version: 1

Name: ValidOne
Min butthurt: 0
Max butthurt: 14
Min level: 1
Max level: 30
Weight: 1

Name: CorruptFields
Min butthurt: NOT_A_NUMBER
Max butthurt: ???
Min level: -999999999999999999999999999999
Max level: infinity
Weight: NaN

GarbageLineWithoutColon
:::DoubleColon:::
Name: 
Min level: 5

Name: ValidTwo
Min butthurt: 2
Max butthurt: 5
Min level: 10
Max level: 20
Weight: 3
";
            File.WriteAllText(Path.Combine(animsDir, "manifest.txt"), malformedManifest);

            // Even with corrupted manifest entries, parser should fall back gracefully
            var result = _importService.ImportAssetPack(sourceDir);
            Assert.NotNull(result);
            // ValidOne and ValidTwo or fallback entries should be parsed without uncaught crash
            Assert.Contains(result, r => r.Name == "ValidOne" || r.Name == "ValidTwo");
        }

        [Fact]
        public void StressTest_ZipPathTraversal_ConfinesExtractedFilesSecurely()
        {
            string evilZipPath = Path.Combine(_testRoot, "EvilTraversal.zip");

            using (var zip = ZipFile.Open(evilZipPath, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("../../outside_victim.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.WriteLine("Malicious payload");
            }

            // Ingesting should handle or reject traversal without writing outside temp staging
            try
            {
                _importService.ImportAssetPack(evilZipPath);
            }
            catch
            {
                // Rejection is valid behavior
            }

            string victimFile = Path.Combine(Path.GetTempPath(), "outside_victim.txt");
            Assert.False(File.Exists(victimFile), "Zip extraction must never write files outside target directory");
        }

        #endregion

        #region 4. Pre-Flight Hardware Validation Rule Diagnostic Assertions

        [Theory]
        [InlineData(129, 64, "FZM001")]  // Invalid width > 128
        [InlineData(128, 65, "FZM001")]  // Invalid height > 64
        [InlineData(0, 0, "FZM001")]      // Zero dimensions
        [InlineData(256, 128, "FZM001")]  // Exceeds 128x64 bounds
        public void Validation_ScreenDimensionViolations_EmitFZM001(int w, int h, string expectedCode)
        {
            var sprite = E2ETestHelper.CreateTestSprite(frameCount: 1, width: w, height: h);
            var entry = new FlipperManifestEntry { Name = "DimTest", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var settings = new FlipperExportSettings { AnimationName = "DimTest", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };

            var errors = _exportService.ValidateAssetPackForExport([(sprite, entry, settings)], isMomentum: true);
            Assert.Contains(errors, e => e.Code == expectedCode);
        }

        [Fact]
        public void Validation_DuplicateAnimationNames_EmitFZ011()
        {
            var sprite1 = E2ETestHelper.CreateTestSprite(frameCount: 1, width: 128, height: 64);
            var sprite2 = E2ETestHelper.CreateTestSprite(frameCount: 1, width: 128, height: 64);

            var item1 = (sprite1, new FlipperManifestEntry { Name = "Duplicate_Name", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 },
                                 new FlipperExportSettings { AnimationName = "Duplicate_Name", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 });

            var item2 = (sprite2, new FlipperManifestEntry { Name = "  duplicate_name  ", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 },
                                 new FlipperExportSettings { AnimationName = "duplicate_name", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 });

            var errors = _exportService.ValidateAssetPackForExport([item1, item2], isMomentum: true);
            Assert.Contains(errors, e => e.Code == "FZ011");
        }

        [Theory]
        [InlineData(0, 10, "FZ002")]   // MinLevel < 1
        [InlineData(31, 30, "FZ002")]  // MinLevel > 30
        [InlineData(10, 5, "FZ003")]   // MinLevel > MaxLevel
        [InlineData(1, 31, "FZ003")]   // MaxLevel > 30 (Momentum)
        [InlineData(1, 4, "FZ003", false)] // MaxLevel > 3 (Stock)
        public void Validation_LevelViolations_EmitFZ002OrFZ003(int minLvl, int maxLvl, string expectedCode, bool isMomentum = true)
        {
            var sprite = E2ETestHelper.CreateTestSprite(frameCount: 1, width: 128, height: 64);
            var entry = new FlipperManifestEntry { Name = "LevelTest", MinLevel = minLvl, MaxLevel = maxLvl, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var settings = new FlipperExportSettings { AnimationName = "LevelTest", MinLevel = minLvl, MaxLevel = maxLvl, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };

            var errors = _exportService.ValidateAssetPackForExport([(sprite, entry, settings)], isMomentum);
            Assert.Contains(errors, e => e.Code == expectedCode);
        }

        [Theory]
        [InlineData(-1, 10, "FZ004")]  // MinMood < 0
        [InlineData(15, 14, "FZ004")]  // MinMood > 14
        [InlineData(5, 2, "FZ005")]    // MinMood > MaxMood
        [InlineData(0, 15, "FZ005")]   // MaxMood > 14
        public void Validation_MoodViolations_EmitFZ004OrFZ005(int minMood, int maxMood, string expectedCode)
        {
            var sprite = E2ETestHelper.CreateTestSprite(frameCount: 1, width: 128, height: 64);
            var entry = new FlipperManifestEntry { Name = "MoodTest", MinLevel = 1, MaxLevel = 30, MinButthurt = minMood, MaxButthurt = maxMood, Weight = 1 };
            var settings = new FlipperExportSettings { AnimationName = "MoodTest", MinLevel = 1, MaxLevel = 30, MinButthurt = minMood, MaxButthurt = maxMood, Weight = 1 };

            var errors = _exportService.ValidateAssetPackForExport([(sprite, entry, settings)], isMomentum: true);
            Assert.Contains(errors, e => e.Code == expectedCode);
        }

        [Fact]
        public void Validation_ZeroOrNegativeWeight_EmitsFZ006()
        {
            var sprite = E2ETestHelper.CreateTestSprite(frameCount: 1, width: 128, height: 64);
            var entry = new FlipperManifestEntry { Name = "WeightTest", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 0 };
            var settings = new FlipperExportSettings { AnimationName = "WeightTest", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 0 };

            var errors = _exportService.ValidateAssetPackForExport([(sprite, entry, settings)], isMomentum: true);
            Assert.Contains(errors, e => e.Code == "FZ006");
        }

        #endregion

        #region Helper Methods

        private static byte[] CreateTestBmFile(int width, int height, byte pattern)
        {
            // Simple uncompressed 1-bit monochrome bitmap frame (.bm file)
            // 128x64 = 1024 bytes
            int byteCount = (width * height) / 8;
            byte[] bytes = new byte[byteCount];
            Array.Fill(bytes, pattern);
            return bytes;
        }

        #endregion
    }
}
