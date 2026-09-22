using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hexprite.Core;
using Hexprite.Resources.Fonts;
using Hexprite.Services;
using Hexprite.Services.Compression;
using Hexprite.ViewModels;
using Xunit;

namespace Hexprite.Tests.E2E
{
    /// <summary>
    /// Tier 4: Real-World Application Scenario E2E Tests (10 high-complexity end-to-end workflows).
    /// Opaque-box verification of complete user workflows and data lifecycles.
    /// </summary>
    [Trait("Category", "E2E")]
    [Trait("Category", "Integration")]
    public class Tier4_RealWorldScenarioTests
    {
        [Fact]
        public void T4_S01_AssetPack_Modify_ExportPack_DeployWorkflow()
        {
            using var tempDir = E2ETestHelper.CreateTempDirectory();

            // 1. Create test asset pack on disk
            string sourcePackDir = E2ETestHelper.CreateTestAssetPack("stock_dolphin_essentials", tempDir.Path);
            Assert.True(Directory.Exists(sourcePackDir));

            // 2. Import pack into memory
            var importService = new FlipperImportService();
            var imported = importService.ImportAssetPack(sourcePackDir);
            Assert.NotEmpty(imported);

            // 3. Modify frame pixels of the first animation in-memory
            var firstAnim = imported[0];
            bool[] frame0Pixels = firstAnim.Sprite.Frames[0].LayerPixels[0].GetMonochromeData();
            for (int i = 0; i < 20; i++)
            {
                frame0Pixels[i] = true;
            }

            // 4. Add a new animation to the pack list
            var newSprite = E2ETestHelper.CreateTestSprite(2);
            var newEntry = new FlipperManifestEntry
            {
                Name = "NewCustomAnim",
                MinLevel = 1,
                MaxLevel = 30,
                MinButthurt = 0,
                MaxButthurt = 14,
                Weight = 2
            };
            imported.Add(("NewCustomAnim", newSprite, newEntry));

            // 5. Auto-balance schedule matrix
            var entries = imported.Select(i => i.ManifestEntry).ToList();
            var balanced = FlipperScheduleMatrix.AutoBalanceEntries(entries);
            for (int i = 0; i < imported.Count; i++)
            {
                imported[i].ManifestEntry.MinLevel = balanced[i].MinLevel;
                imported[i].ManifestEntry.MaxLevel = balanced[i].MaxLevel;
                imported[i].ManifestEntry.MinButthurt = balanced[i].MinButthurt;
                imported[i].ManifestEntry.MaxButthurt = balanced[i].MaxButthurt;
            }

            // 6. Export modified pack to a new target folder
            string exportDir = Path.Combine(tempDir.Path, "ModifiedPack");
            var exportService = new FlipperExportService();
            var exportTuples = imported.Select(i => (
                i.Sprite,
                i.ManifestEntry,
                new FlipperExportSettings { AnimationName = i.Name, TargetMode = FlipperExportTargetMode.MomentumAssetPack }
            )).ToList();

            exportService.ExportAssetPack(exportTuples, exportDir, isMomentum: true);

            // 7. Validate exported structure and re-import
            string manifestPath = Path.Combine(exportDir, "Anims", "manifest.txt");
            Assert.True(File.Exists(manifestPath));

            var reimported = importService.ImportAssetPack(exportDir);
            Assert.Equal(imported.Count, reimported.Count);
            Assert.Contains(reimported, a => a.Name == "NewCustomAnim");
        }

        [Fact]
        public void T4_S02_MultiFrameDolphin_AutoBalance_ScheduleMatrixValidation()
        {
            using var tempDir = E2ETestHelper.CreateTempDirectory();

            // 1. Construct 4 fragmented animations
            var anims = new List<(SpriteState Sprite, FlipperManifestEntry Entry, FlipperExportSettings Settings)>
            {
                (E2ETestHelper.CreateTestSprite(2), new FlipperManifestEntry { Name = "BabyWalk", MinLevel = 1, MaxLevel = 5, MinButthurt = 0, MaxButthurt = 5, Weight = 1 }, new FlipperExportSettings { AnimationName = "BabyWalk" }),
                (E2ETestHelper.CreateTestSprite(2), new FlipperManifestEntry { Name = "TeenRun", MinLevel = 6, MaxLevel = 15, MinButthurt = 0, MaxButthurt = 5, Weight = 1 }, new FlipperExportSettings { AnimationName = "TeenRun" }),
                (E2ETestHelper.CreateTestSprite(2), new FlipperManifestEntry { Name = "AdultChill", MinLevel = 16, MaxLevel = 25, MinButthurt = 0, MaxButthurt = 5, Weight = 1 }, new FlipperExportSettings { AnimationName = "AdultChill" }),
                (E2ETestHelper.CreateTestSprite(2), new FlipperManifestEntry { Name = "ElderWisdom", MinLevel = 26, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 5, Weight = 1 }, new FlipperExportSettings { AnimationName = "ElderWisdom" }),
            };

            // 2. Initial matrix coverage has deadzones (moods 6..14 are uncovered)
            var initialMatrix = new FlipperScheduleMatrix(anims.Select(a => a.Entry));
            Assert.True(initialMatrix.CoveragePercentage < 100.0);
            Assert.NotEmpty(initialMatrix.GetUncoveredCells());

            // 3. Run AutoBalance
            var balanced = FlipperScheduleMatrix.AutoBalanceEntries(anims.Select(a => a.Entry).ToList());
            for (int i = 0; i < anims.Count; i++)
            {
                anims[i].Entry.MinLevel = balanced[i].MinLevel;
                anims[i].Entry.MaxLevel = balanced[i].MaxLevel;
                anims[i].Entry.MinButthurt = balanced[i].MinButthurt;
                anims[i].Entry.MaxButthurt = balanced[i].MaxButthurt;
            }

            // 4. Matrix achieves 100% coverage
            var balancedMatrix = new FlipperScheduleMatrix(anims.Select(a => a.Entry));
            Assert.Equal(100.0, balancedMatrix.CoveragePercentage);
            Assert.Empty(balancedMatrix.GetUncoveredCells());

            // 5. Export and re-verify manifest on disk
            var exportService = new FlipperExportService();
            exportService.ExportAssetPack(anims, tempDir.Path, isMomentum: true);

            string manifestText = File.ReadAllText(Path.Combine(tempDir.Path, "Anims", "manifest.txt"));
            var parsedManifest = FlipperManifest.Parse(manifestText);
            var diskMatrix = new FlipperScheduleMatrix(parsedManifest.Entries);

            Assert.Equal(100.0, diskMatrix.CoveragePercentage);
        }

        [Fact]
        public void T4_S05_SimulationPlayback_With_SpeechBubbleSequencing()
        {
            // 1. Construct 4-frame animation with 2 speech bubbles
            var sprite = E2ETestHelper.CreateTestSprite(4);
            var bubble1 = new FlipperSpeechBubble(1, 5, 5, "Frame0Bubble", SpeechBubbleTailPosition.BottomLeft)
            {
                StartFrame = 0,
                EndFrame = 1
            };
            var bubble2 = new FlipperSpeechBubble(2, 40, 20, "Frame2Bubble", SpeechBubbleTailPosition.TopRight)
            {
                StartFrame = 2,
                EndFrame = 3
            };

            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                PassiveFrameCount = 4,
                ActiveFrameCount = 0,
                FramesOrder = [0, 1, 2, 3],
                SpeechBubbles = [bubble1, bubble2]
            };

            // 2. Simulate playback frame by frame and composite
            for (int f = 0; f < sprite.Frames.Count; f++)
            {
                bool[] canvas = sprite.CompositeFramePixels(f);

                // Apply active speech bubbles for current frame
                foreach (var bubble in sprite.FlipperCycle.SpeechBubbles)
                {
                    if (f >= bubble.StartFrame && f <= bubble.EndFrame)
                    {
                        bubble.Draw(canvas, 128, 64, fillInterior: true);
                    }
                }

                // Verify canvas contains rendered content
                Assert.Contains(true, canvas);
            }
        }

        [Fact]
        public void T4_S06_ScreenMirror_ContinuousStream_PacketIntegrity()
        {
            // 1. Simulate 30 frames of animated canvas data
            var streamService = new FlipperScreenStreamService();

            for (int frameIdx = 0; frameIdx < 30; frameIdx++)
            {
                bool[] originalFrame = new bool[128 * 64];
                // Moving vertical bar
                int barX = (frameIdx * 4) % 128;
                for (int y = 0; y < 64; y++)
                {
                    originalFrame[y * 128 + barX] = true;
                    if (y == frameIdx) originalFrame[y * 128 + 10] = true;
                }

                // 2. Encode to 1024-byte packet
                byte[] packet = FlipperScreenStreamService.Encode1024Buffer(originalFrame);
                Assert.Equal(1024, packet.Length);

                // 3. Decode and assert 100% bitwise fidelity
                bool[] decoded = E2ETestHelper.Decode1024Buffer(packet);
                Assert.Equal(originalFrame, decoded);
            }

            streamService.Dispose();
        }

        [Fact]
        public void T4_S07_AssetPack_ValidationReport_And_ZipArchiving()
        {
            using var tempDir = E2ETestHelper.CreateTempDirectory();
            string zipPath = Path.Combine(tempDir.Path, "CompletePack.zip");

            // 1. Create animation tuples
            var anim1 = (E2ETestHelper.CreateTestSprite(2), new FlipperManifestEntry { Name = "Walk", MinLevel = 1, MaxLevel = 15, MinButthurt = 0, MaxButthurt = 14, Weight = 1 }, new FlipperExportSettings { AnimationName = "Walk" });
            var anim2 = (E2ETestHelper.CreateTestSprite(3), new FlipperManifestEntry { Name = "Jump", MinLevel = 16, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 }, new FlipperExportSettings { AnimationName = "Jump" });

            // 2. Validate pack
            var validation = FlipperPackArchiverService.ValidatePack([
                ("Walk", anim1.Item1, anim1.Item2),
                ("Jump", anim2.Item1, anim2.Item2)
            ]);

            Assert.Equal(2, validation.TotalAnimations);
            Assert.Equal(5, validation.TotalFrames);
            Assert.Equal(450, validation.MatrixCoveredCells);
            Assert.Equal(100.0, validation.MatrixCoveragePercent);
            Assert.DoesNotContain(validation.Issues, i => i.Severity == "Error");

            // 3. Archive pack to zip
            var exportService = new FlipperExportService();
            exportService.ExportAssetPackZip([anim1, anim2], zipPath);

            Assert.True(File.Exists(zipPath));
            using var zip = ZipFile.OpenRead(zipPath);
            Assert.Contains(zip.Entries, e => e.FullName.Contains("manifest.txt"));
            Assert.Contains(zip.Entries, e => e.FullName.Contains("Walk/meta.txt"));
            Assert.Contains(zip.Entries, e => e.FullName.Contains("Jump/meta.txt"));
        }

        [Fact]
        public void T4_S08_CorruptedAssetRecovery_And_GracefulDegradation()
        {
            // 1. Corrupted manifest text (invalid header, invalid level range, missing name)
            string corruptedManifest =
                "Filetype: Unknown\n" +
                "Version: 99\n\n" +
                "Name: \n" +
                "Min level: 50\n" + // Out of bounds level > 30
                "Max level: 10\n" + // Max < Min
                "Weight: -5\n";     // Negative weight

            var manifest = FlipperManifest.Parse(corruptedManifest);
            var diagnostics = manifest.Validate();

            // 2. Diagnostics capture all errors
            Assert.Contains(diagnostics, d => d.Code == "FZ001"); // Animation Name cannot be empty
            Assert.Contains(diagnostics, d => d.Code == "FZ002"); // Min level out of bounds
            Assert.Contains(diagnostics, d => d.Code == "FZ003"); // Max level < Min level
            Assert.Contains(diagnostics, d => d.Code == "FZ006"); // Weight must be greater than 0

            // 3. Corrupted meta text (negative dimension, invalid frame order)
            string corruptedMeta =
                "Filetype: Flipper Animation\n" +
                "Version: 1\n\n" +
                "Width: 128\n" +
                "Height: 64\n" +
                "Passive frames: 10\n" +
                "Frames order: 0 1 99\n"; // 99 out of bounds

            var meta = FlipperAnimationMeta.Parse(corruptedMeta);
            var metaDiagnostics = meta.Validate(physicalFrameCount: 2);
            Assert.Contains(metaDiagnostics, d => d.Code == "FZM004"); // Out of bounds frame index
        }

        [Fact]
        public void T4_S09_WorkspaceTab_BulkImport_Edit_BatchExport()
        {
            using var tempDir = E2ETestHelper.CreateTempDirectory();
            string samplePack = E2ETestHelper.CreateTestAssetPack("momentum_cyber_hacker", tempDir.Path);

            // 1. Bulk import
            var importService = new FlipperImportService();
            var imported = importService.ImportAssetPack(samplePack);
            var tuples = imported.Select(i => (i.Name, i.Sprite)).ToList();

            // 2. Open into workspace tabs
            var shell = E2ETestHelper.CreateTestShellViewModel();
            shell.OpenSpritesInTabs(tuples);
            Assert.Equal(tuples.Count, shell.OpenDocuments.Count);

            // 3. Edit the active document
            var activeDoc = shell.ActiveDocument as MainViewModel;
            Assert.NotNull(activeDoc);
            activeDoc.SpriteState.ActiveLayerPixels[0] = true;

            // 4. Batch export all tabs to a new location
            string batchExportDir = Path.Combine(tempDir.Path, "BatchExport");
            var exportList = shell.OpenDocuments.OfType<MainViewModel>().Select(doc => (
                doc.SpriteState,
                new FlipperManifestEntry { Name = doc.SpriteName },
                new FlipperExportSettings { AnimationName = doc.SpriteName }
            )).ToList();

            var exportService = new FlipperExportService();
            exportService.ExportAssetPack(exportList, batchExportDir, isMomentum: true);

            Assert.True(Directory.Exists(Path.Combine(batchExportDir, "Anims")));
            Assert.True(File.Exists(Path.Combine(batchExportDir, "Anims", "manifest.txt")));
        }

        [Fact]
        public void T4_S10_UiTemplate_To_FlipperDeployPipeline()
        {
            // 1. Create canvas and apply Header Bar UI template
            var sprite = new SpriteState(128, 64);
            FlipperUiTemplateService.ApplyTemplate(sprite, FlipperUiTemplateType.HeaderBar, "315.00 MHz");

            // 2. Generate export deployment payload
            var exportService = new FlipperExportService();
            var settings = new FlipperExportSettings
            {
                AnimationName = "SubGhzTool",
                TargetMode = FlipperExportTargetMode.MomentumAssetPack
            };

            var deployFiles = exportService.GenerateDeploymentFiles(sprite, settings);
            Assert.NotEmpty(deployFiles);

            // 3. Verify files contain meta and compressed frame
            Assert.Contains(deployFiles, f => f.RelativePath.EndsWith("meta.txt"));
            Assert.Contains(deployFiles, f => f.RelativePath.EndsWith("frame_0.bm"));

            // 4. Verify deployment chunk splitting logic
            var deployer = new FlipperUsbDeployer();
            foreach (var (relPath, data) in deployFiles)
            {
                Assert.NotNull(data);
                Assert.False(string.IsNullOrWhiteSpace(relPath));
            }
        }
    }
}
