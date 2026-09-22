using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Hexprite.ViewModels.Flipper;
using Moq;
using Xunit;

namespace Hexprite.Tests.E2E
{
    [Collection("WindowLayoutSettingsFile")]
    [Trait("Category", "E2E")]
    [Trait("Category", "Integration")]
    public class ChallengerMilestone4E2ETests : IDisposable
    {
        private readonly string _testWorkspace;
        private readonly FlipperImportService _importService;
        private readonly FlipperExportService _exportService;

        public ChallengerMilestone4E2ETests()
        {
            WpfTestHelper.EnsureApplication();
            _testWorkspace = Path.Combine(Path.GetTempPath(), "Hexprite_Challenger_M4_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testWorkspace);
            _importService = new FlipperImportService();
            _exportService = new FlipperExportService();
        }

        public void Dispose()
        {
            if (Directory.Exists(_testWorkspace))
            {
                try { Directory.Delete(_testWorkspace, true); } catch { }
            }
        }

        [Fact]
        public void Challenger_CompleteLifecycle_Ingestion_To_TabEdit_To_MatrixSync_To_Export_To_ReIngest()
        {
            // ==========================================
            // Step 1: Ingestion of initial Asset Pack
            // ==========================================
            string initialPackDir = Path.Combine(_testWorkspace, "InitialPack");
            string animsDir = Path.Combine(initialPackDir, "Anims");
            Directory.CreateDirectory(animsDir);

            // Create initial animation: "DolphinWave" (3 frames)
            string waveDir = Path.Combine(animsDir, "DolphinWave");
            Directory.CreateDirectory(waveDir);
            File.WriteAllText(Path.Combine(waveDir, "meta.txt"),
                "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nPassive frames: 3\nActive frames: 0\nFrames order: 0 1 2\n");

            for (int f = 0; f < 3; f++)
            {
                byte[] frameData = new byte[1024];
                // Set distinct watermark pixels for each frame
                frameData[f * 50] = (byte)(0x80 >> (f % 8));
                File.WriteAllBytes(Path.Combine(waveDir, $"frame_{f}.bm"), frameData);
            }

            // Manifest with 1 entry covering Level 1-15, Mood 0-7
            File.WriteAllText(Path.Combine(animsDir, "manifest.txt"),
                "Filetype: Flipper Animation Manifest\nVersion: 1\n\n" +
                "Name: DolphinWave\nMin level: 1\nMax level: 15\nMin butthurt: 0\nMax butthurt: 7\nWeight: 2\n");

            var importedPack = _importService.ImportAssetPack(initialPackDir);
            Assert.Single(importedPack);
            Assert.Equal("DolphinWave", importedPack[0].Name);
            Assert.Equal(3, importedPack[0].Sprite.Frames.Count);

            // ==========================================
            // Step 2: Tab Linking & ViewModel Creation using real Shell and WorkspaceTabService
            // ==========================================
            var shell = E2ETestHelper.CreateTestShellViewModel();
            var tabService = new WorkspaceTabService(shell);

            var assetPackDoc = AssetPackDocument.CreateNew("ChallengerPack");
            var assetPackVm = new AssetPackViewModel(
                document: assetPackDoc,
                pack: importedPack,
                tabService: tabService);

            shell.OpenDocuments.Add(assetPackVm);
            shell.ActiveDocument = assetPackVm;

            var matrixVm = assetPackVm.MatrixViewModel;

            Assert.Single(matrixVm.Entries);
            Assert.Equal("DolphinWave", matrixVm.Entries[0].Name);
            Assert.True(matrixVm.AnimationSprites.ContainsKey("DolphinWave"));

            // Navigate to tab via "Edit on Canvas"
            matrixVm.SelectedEntry = matrixVm.Entries[0];
            matrixVm.OpenSelectedInCanvas();

            // Verify a Sprite tab was opened in Shell
            Assert.Equal(2, shell.OpenDocuments.Count);
            var mainVm = shell.ActiveDocument as MainViewModel;
            Assert.NotNull(mainVm);
            Assert.Equal("DolphinWave", mainVm.SpriteName);
            Assert.Equal(3, mainVm.Frames.Count);

            // ==========================================
            // Step 3: Canvas Editing & Tab Modifications
            // ==========================================
            // 3a. Add a 4th frame in sprite editor tab
            var newFrame = new FrameState { Name = "Frame 4" };
            bool[] frame4Pixels = new bool[128 * 64];
            frame4Pixels[0] = true; // Top-left pixel
            frame4Pixels[128 * 64 - 1] = true; // Bottom-right pixel
            newFrame.LayerPixels.Add(new MonochromePixelBuffer(frame4Pixels));
            mainVm.SpriteState.Frames.Add(newFrame);
            mainVm.SpriteState.FlipperCycle = new FlipperAnimationCycle { FramesOrder = [0, 1, 2, 3], PassiveFrameCount = 4 };
            mainVm.RebuildFrameViewModels();
            Assert.Equal(4, mainVm.Frames.Count);
            Assert.Equal(4, mainVm.SpriteState.Frames.Count);

            // 3b. Rename animation to "DolphinDive" in tab
            mainVm.SpriteName = "DolphinDive";
            Assert.Contains("DolphinDive", mainVm.Title);

            // Also rename in matrix
            matrixVm.SelectedName = "DolphinDive";
            Assert.Equal("DolphinDive", matrixVm.SelectedEntry.Name);

            // 3c. Refresh matrix preview from open tabs
            matrixVm.RefreshCurrentPreviewSprite();
            Assert.True(matrixVm.AnimationSprites.ContainsKey("DolphinDive"));
            Assert.Equal(4, matrixVm.AnimationSprites["DolphinDive"].Frames.Count);

            // ==========================================
            // Step 4: Schedule Matrix Sync & Preview Controls
            // ==========================================
            // Add a gap filler entry covering Level 16-30, Mood 8-14
            var gapSprite = new SpriteState(128, 64);
            gapSprite.Frames[0].LayerPixels[0].GetMonochromeData()[500] = true;
            shell.OpenSpriteInTab(gapSprite, "GapFiller");

            var gapEntry = new FlipperScheduleEntryViewModel(new FlipperManifestEntry
            {
                Name = "GapFiller",
                MinLevel = 16,
                MaxLevel = 30,
                MinButthurt = 8,
                MaxButthurt = 14,
                Weight = 1
            });
            matrixVm.Entries.Add(gapEntry);

            // Step through preview player frames
            matrixVm.SelectedEntry = matrixVm.Entries[0];
            Assert.Equal(0, matrixVm.PreviewFrameIndex);
            matrixVm.PreviewNextFrame();
            Assert.Equal(1, matrixVm.PreviewFrameIndex);
            matrixVm.PreviewNextFrame();
            Assert.Equal(2, matrixVm.PreviewFrameIndex);
            matrixVm.PreviewNextFrame();
            Assert.Equal(3, matrixVm.PreviewFrameIndex);
            matrixVm.PreviewNextFrame();
            Assert.Equal(0, matrixVm.PreviewFrameIndex); // Wraps around 4 frames

            // ==========================================
            // Step 5: Full Pack Export & Pre-Flight Validation
            // ==========================================
            matrixVm.SelectedEntry = matrixVm.Entries[1];
            matrixVm.RefreshCurrentPreviewSprite();
            matrixVm.SelectedEntry = matrixVm.Entries[0];
            matrixVm.RefreshCurrentPreviewSprite();

            var exportTuples = matrixVm.Entries.Select(e => (
                matrixVm.AnimationSprites.TryGetValue(e.Name, out var s) ? s : gapSprite,
                e.Entry,
                new FlipperExportSettings { AnimationName = e.Name, TargetMode = FlipperExportTargetMode.MomentumAssetPack }
            )).ToList();

            // 5a. Pre-flight validation
            var diagnostics = _exportService.ValidateAssetPackForExport(exportTuples, isMomentum: true);
            Assert.DoesNotContain(diagnostics, d => d.Severity == FlipperValidationSeverity.Error);

            // 5b. Full directory export
            string exportedFolder = Path.Combine(_testWorkspace, "ExportedMomentumTree");
            _exportService.ExportAssetPack(exportTuples, exportedFolder, isMomentum: true);

            // Verify filesystem tree
            Assert.True(File.Exists(Path.Combine(exportedFolder, "Anims", "manifest.txt")));
            Assert.True(File.Exists(Path.Combine(exportedFolder, "Anims", "DolphinDive", "meta.txt")));
            Assert.True(File.Exists(Path.Combine(exportedFolder, "Anims", "DolphinDive", "frame_0.bm")));
            Assert.True(File.Exists(Path.Combine(exportedFolder, "Anims", "DolphinDive", "frame_3.bm")));
            Assert.True(File.Exists(Path.Combine(exportedFolder, "Icons", "I_DolphinDive_10x10.bm")));
            Assert.True(File.Exists(Path.Combine(exportedFolder, "Anims", "GapFiller", "meta.txt")));
            Assert.True(File.Exists(Path.Combine(exportedFolder, "Anims", "GapFiller", "frame_0.bm")));

            // 5c. Zip export
            string exportedZip = Path.Combine(_testWorkspace, "ExportedPack.zip");
            _exportService.ExportAssetPackZip(exportTuples, exportedZip, isMomentum: true);
            Assert.True(File.Exists(exportedZip));

            // ==========================================
            // Step 6: Re-Ingestion & Bitwise Fidelity Verification
            // ==========================================
            // 6a. Re-ingest from folder
            var folderReimported = _importService.ImportAssetPack(exportedFolder);
            Assert.Equal(2, folderReimported.Count);

            var reimportedDive = folderReimported.First(a => a.Name == "DolphinDive");
            Assert.Equal(4, reimportedDive.Sprite.Frames.Count);
            Assert.Equal(1, reimportedDive.ManifestEntry.MinLevel);
            Assert.Equal(15, reimportedDive.ManifestEntry.MaxLevel);
            Assert.Equal(0, reimportedDive.ManifestEntry.MinButthurt);
            Assert.Equal(7, reimportedDive.ManifestEntry.MaxButthurt);

            // Check drawn pixels on Frame 4
            var reimportedFrame4Px = reimportedDive.Sprite.CompositeFramePixels(3);
            Assert.True(reimportedFrame4Px[0]);
            Assert.True(reimportedFrame4Px[128 * 64 - 1]);

            // 6b. Re-ingest from zip
            var zipReimported = _importService.ImportAssetPack(exportedZip);
            Assert.Equal(2, zipReimported.Count);
            var zipDive = zipReimported.First(a => a.Name == "DolphinDive");
            Assert.Equal(4, zipDive.Sprite.Frames.Count);

            // Verify bitwise fidelity between original edited sprite and zip re-imported sprite
            for (int f = 0; f < 4; f++)
            {
                bool[] originalPx = mainVm.SpriteState.CompositeFramePixels(f);
                bool[] zipPx = zipDive.Sprite.CompositeFramePixels(f);
                Assert.Equal(originalPx, zipPx);
            }
        }

        [Fact]
        public void Challenger_Stress_450SingleCellMatrix_FullExportAndReImportFidelity()
        {
            // Stress test: 450 distinct 1x1 cell animations covering complete 30 levels x 15 moods matrix
            var exportTuples = new List<(SpriteState Sprite, FlipperManifestEntry ManifestEntry, FlipperExportSettings Settings)>();

            for (int lvl = 1; lvl <= 30; lvl++)
            {
                for (int mood = 0; mood <= 14; mood++)
                {
                    string animName = $"A_L{lvl}_M{mood}";
                    var sprite = new SpriteState(128, 64);
                    // Encode level and mood in pixel coordinates (lvl, mood)
                    sprite.Frames[0].LayerPixels[0].GetMonochromeData()[mood * 128 + lvl] = true;

                    var entry = new FlipperManifestEntry
                    {
                        Name = animName,
                        MinLevel = lvl,
                        MaxLevel = lvl,
                        MinButthurt = mood,
                        MaxButthurt = mood,
                        Weight = 1
                    };
                    var settings = new FlipperExportSettings { AnimationName = animName };
                    exportTuples.Add((sprite, entry, settings));
                }
            }

            Assert.Equal(450, exportTuples.Count);

            // Validate
            var diags = _exportService.ValidateAssetPackForExport(exportTuples, isMomentum: true);
            Assert.DoesNotContain(diags, d => d.Severity == FlipperValidationSeverity.Error);

            // Export to Zip
            string packZip = Path.Combine(_testWorkspace, "Stress450Pack.zip");
            _exportService.ExportAssetPackZip(exportTuples, packZip, isMomentum: true);

            // Ingest Zip
            var reimported = _importService.ImportAssetPack(packZip);
            Assert.Equal(450, reimported.Count);

            // Verify matrix coverage is 100% (450/450 cells)
            var matrix = new FlipperScheduleMatrix(reimported.Select(r => r.ManifestEntry));
            Assert.Equal(450, matrix.CoveredCellsCount);
            Assert.Equal(100.0, matrix.CoveragePercentage);
            Assert.Empty(matrix.GetUncoveredCells());

            // Spot-check pixel coordinates for 10 random entries
            var rnd = new Random(42);
            for (int i = 0; i < 10; i++)
            {
                int testLvl = rnd.Next(1, 31);
                int testMood = rnd.Next(0, 15);
                string expectedName = $"A_L{testLvl}_M{testMood}";

                var item = reimported.First(x => x.Name == expectedName);
                bool[] px = item.Sprite.CompositeFramePixels(0);
                Assert.True(px[testMood * 128 + testLvl], $"Pixel watermark mismatch at L{testLvl} M{testMood}");
            }
        }

        [Fact]
        public void Challenger_Adversarial_MalformedManifestRecovery_PreservesAvailableFramesAndDiagnostics()
        {
            string packDir = Path.Combine(_testWorkspace, "MalformedPack");
            string animsDir = Path.Combine(packDir, "Anims");
            Directory.CreateDirectory(animsDir);

            // Good anim
            string goodDir = Path.Combine(animsDir, "GoodAnim");
            Directory.CreateDirectory(goodDir);
            File.WriteAllText(Path.Combine(goodDir, "meta.txt"),
                "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nPassive frames: 1\nActive frames: 0\n");
            File.WriteAllBytes(Path.Combine(goodDir, "frame_0.bm"), new byte[1024]);

            // Corrupted manifest with bad syntax, negative values, and missing animation folder
            string malformedManifest =
                "Filetype: Flipper Animation Manifest\nVersion: 1\n\n" +
                "Name: GoodAnim\nMin level: 1\nMax level: 10\nWeight: 1\n\n" +
                "Name: GhostAnimMissingFolder\nMin level: 11\nMax level: 20\nWeight: 2\n\n" +
                "InvalidKeyWithoutColon\n" +
                "Name: BadParamsAnim\nMin level: NOT_A_NUMBER\nMax level: -5\nWeight: 0\n";

            File.WriteAllText(Path.Combine(animsDir, "manifest.txt"), malformedManifest);

            // Ingest
            var results = _importService.ImportAssetPack(packDir);

            // Verify GoodAnim loaded
            var good = results.FirstOrDefault(r => r.Name == "GoodAnim");
            Assert.NotNull(good.Sprite);
            Assert.Single(good.Sprite.Frames);

            // Verify GhostAnimMissingFolder created a fallback placeholder 128x64 sprite without throwing
            var ghost = results.FirstOrDefault(r => r.Name == "GhostAnimMissingFolder");
            Assert.NotNull(ghost.Sprite);
            Assert.Equal(128, ghost.Sprite.Width);
            Assert.Equal(64, ghost.Sprite.Height);
        }
    }
}
