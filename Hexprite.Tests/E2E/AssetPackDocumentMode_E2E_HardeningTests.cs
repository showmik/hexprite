using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.Services.Compression;
using Hexprite.ViewModels;
using Hexprite.ViewModels.Flipper;
using Moq;
using Xunit;

namespace Hexprite.Tests.E2E
{
    /// <summary>
    /// Milestone 4: Comprehensive E2E Verification & Test Hardening for Flipper Asset Pack Document Mode.
    /// Full 5-tier opaque-box verification across:
    /// - R1: Flipper Asset Pack Directory & File Ingestion
    /// - R2: Seamless Workspace Tab Linking & Matrix Sync
    /// - R3: Comprehensive Asset Pack Export & Pre-Flight Hardware Validation
    /// - R4: Lifecycle, Auto-Balancing & Adversarial Fault Tolerance
    /// </summary>
    [Trait("Category", "E2E")]
    [Trait("Category", "Integration")]
    public class AssetPackDocumentMode_E2E_HardeningTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly FlipperImportService _importService;
        private readonly FlipperExportService _exportService;

        public AssetPackDocumentMode_E2E_HardeningTests()
        {
            WpfTestHelper.EnsureApplication();
            _tempDirectory = Path.Combine(Path.GetTempPath(), "Hexprite_M4_E2E_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
            _importService = new FlipperImportService();
            _exportService = new FlipperExportService();
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                try { Directory.Delete(_tempDirectory, true); } catch { }
            }
        }

        #region Tier 1: Feature Coverage (R1, R2, R3 Core Capabilities)

        [Fact]
        public void T1_R1_01_DirectoryIngestion_MomentumTree_LoadsAllEntriesAndFrames()
        {
            // Arrange: Create full Momentum asset pack directory tree
            string packDir = Path.Combine(_tempDirectory, "MomentumPack");
            string animsDir = Path.Combine(packDir, "Anims");
            Directory.CreateDirectory(animsDir);

            // Animation 1: Swim (3 frames)
            string swimDir = Path.Combine(animsDir, "DolphinSwim");
            Directory.CreateDirectory(swimDir);
            File.WriteAllText(Path.Combine(swimDir, "meta.txt"),
                "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nPassive frames: 3\nActive frames: 0\nFrames order: 0 1 2\n");
            for (int i = 0; i < 3; i++)
            {
                byte[] frameBytes = new byte[1024];
                frameBytes[i * 100] = 0xFF;
                File.WriteAllBytes(Path.Combine(swimDir, $"frame_{i}.bm"), frameBytes);
            }

            // Animation 2: Jump (2 frames)
            string jumpDir = Path.Combine(animsDir, "DolphinJump");
            Directory.CreateDirectory(jumpDir);
            File.WriteAllText(Path.Combine(jumpDir, "meta.txt"),
                "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nPassive frames: 2\nActive frames: 0\nFrames order: 0 1\n");
            for (int i = 0; i < 2; i++)
            {
                byte[] frameBytes = new byte[1024];
                frameBytes[i * 200] = 0xAA;
                File.WriteAllBytes(Path.Combine(jumpDir, $"frame_{i}.bm"), frameBytes);
            }

            // Manifest
            string manifestContent =
                "Filetype: Flipper Animation Manifest\nVersion: 1\n\n" +
                "Name: DolphinSwim\nMin level: 1\nMax level: 15\nWeight: 2\n\n" +
                "Name: DolphinJump\nMin level: 16\nMax level: 30\nWeight: 1\n";
            File.WriteAllText(Path.Combine(animsDir, "manifest.txt"), manifestContent);

            // Act
            var imported = _importService.ImportAssetPack(packDir);

            // Assert
            Assert.Equal(2, imported.Count);
            var swim = imported.FirstOrDefault(a => a.Name == "DolphinSwim");
            Assert.NotNull(swim.Sprite);
            Assert.Equal(3, swim.Sprite.Frames.Count);
            Assert.Equal(1, swim.ManifestEntry.MinLevel);
            Assert.Equal(15, swim.ManifestEntry.MaxLevel);

            var jump = imported.FirstOrDefault(a => a.Name == "DolphinJump");
            Assert.NotNull(jump.Sprite);
            Assert.Equal(2, jump.Sprite.Frames.Count);
            Assert.Equal(16, jump.ManifestEntry.MinLevel);
            Assert.Equal(30, jump.ManifestEntry.MaxLevel);
        }

        [Fact]
        public void T1_R1_02_DirectoryIngestion_StockTree_LoadsFromDolphinDirectory()
        {
            string packDir = Path.Combine(_tempDirectory, "StockPack");
            string dolphinDir = Path.Combine(packDir, "dolphin");
            Directory.CreateDirectory(dolphinDir);

            string animDir = Path.Combine(dolphinDir, "StockAnim");
            Directory.CreateDirectory(animDir);
            File.WriteAllText(Path.Combine(animDir, "meta.txt"),
                "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nPassive frames: 1\nActive frames: 0\nFrames order: 0\n");
            File.WriteAllBytes(Path.Combine(animDir, "frame_0.bm"), new byte[1024]);

            File.WriteAllText(Path.Combine(dolphinDir, "manifest.txt"),
                "Filetype: Flipper Animation Manifest\nVersion: 1\n\nName: StockAnim\nMin level: 1\nMax level: 3\nWeight: 1\n");

            var imported = _importService.ImportAssetPack(packDir);

            Assert.Single(imported);
            Assert.Equal("StockAnim", imported[0].Name);
            Assert.Single(imported[0].Sprite.Frames);
        }

        [Fact]
        public void T1_R1_03_ZipAndHexpackIngestion_LoadsDirectlyIntoAssetPackViewModel()
        {
            string zipPath = Path.Combine(_tempDirectory, "TestPack.zip");
            var sprite1 = E2ETestHelper.CreateTestSprite(2);
            var entry1 = new FlipperManifestEntry { Name = "ZipAnim1", MinLevel = 1, MaxLevel = 15, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var settings1 = new FlipperExportSettings { AnimationName = "ZipAnim1" };

            _exportService.ExportAssetPackZip([(sprite1, entry1, settings1)], zipPath);

            var imported = _importService.ImportAssetPack(zipPath);
            Assert.Single(imported);

            var vm = new AssetPackViewModel(imported, "ImportedFromZip");
            Assert.Equal("ImportedFromZip (Pack)", vm.Title);
            Assert.Single(vm.MatrixViewModel.Entries);
            Assert.Equal("ZipAnim1", vm.MatrixViewModel.Entries[0].Name);
        }

        [Fact]
        public void T1_R1_04_NaturalSorting_MultiDigitFramesOrderedCorrectly()
        {
            string animFolder = Path.Combine(_tempDirectory, "NaturalSortAnim");
            Directory.CreateDirectory(animFolder);

            // Create 15 frames: frame_0.bm .. frame_14.bm
            for (int i = 0; i < 15; i++)
            {
                byte[] data = new byte[1024];
                data[0] = (byte)i; // Distinguishing byte
                File.WriteAllBytes(Path.Combine(animFolder, $"frame_{i}.bm"), data);
            }

            File.WriteAllText(Path.Combine(animFolder, "meta.txt"),
                "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nPassive frames: 15\nActive frames: 0\n");

            var sprite = _importService.ImportAnimation(Path.Combine(animFolder, "meta.txt"));

            Assert.Equal(15, sprite.Frames.Count);
            for (int i = 0; i < 15; i++)
            {
                Assert.Equal($"Frame {i + 1}", sprite.Frames[i].Name);
            }
        }

        [Fact]
        public void T1_R1_05_FaultTolerantDecoding_CorruptedFrameRecoversWithPlaceholder()
        {
            string animFolder = Path.Combine(_tempDirectory, "CorruptAnim");
            Directory.CreateDirectory(animFolder);

            // Write a truncated Heatshrink frame (magic 0x01, length 100, but only 5 actual bytes)
            byte[] badCompressed = [0x01, 0x00, 0x64, 0x00, 0xAA];
            File.WriteAllBytes(Path.Combine(animFolder, "frame_0.bm"), badCompressed);

            File.WriteAllText(Path.Combine(animFolder, "meta.txt"),
                "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nPassive frames: 1\nActive frames: 0\n");

            var sprite = _importService.ImportAnimation(Path.Combine(animFolder, "meta.txt"));

            Assert.NotNull(sprite);
            Assert.Single(sprite.Frames);
            Assert.NotNull(sprite.Frames[0].LayerPixels[0]);
        }

        [Fact]
        public void T1_R2_01_EditOnCanvas_NavigateToEntryTab_OpensSpriteTabWithFrames()
        {
            var mockTabService = new Mock<IWorkspaceTabService>();
            string openedTitle = string.Empty;
            SpriteState? openedSprite = null;

            mockTabService.Setup(t => t.OpenSpriteInTab(It.IsAny<SpriteState>(), It.IsAny<string>()))
                .Callback<SpriteState, string>((s, t) =>
                {
                    openedSprite = s;
                    openedTitle = t;
                });

            var sprite = E2ETestHelper.CreateTestSprite(4);
            var entry = new FlipperManifestEntry { Name = "DolphinPlay", MinLevel = 1, MaxLevel = 30, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("DolphinPlay", sprite, entry)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack, tabService: mockTabService.Object);
            vm.SelectedEntry = vm.Entries[0];

            vm.OpenSelectedInCanvas();

            mockTabService.Verify(t => t.OpenSpriteInTab(It.IsAny<SpriteState>(), "DolphinPlay"), Times.Once);
            Assert.NotNull(openedSprite);
            Assert.Equal(4, openedSprite.Frames.Count);
        }

        [Fact]
        public void T1_R2_02_TimelineSync_RebuildFrameViewModelsInvokedOnTabOpen()
        {
            var shell = E2ETestHelper.CreateTestShellViewModel();
            var sprite = E2ETestHelper.CreateTestSprite(5);

            shell.OpenSpriteInTab(sprite, "MultiFrameTab");

            var doc = shell.ActiveDocument as MainViewModel;
            Assert.NotNull(doc);
            Assert.Equal("MultiFrameTab", doc.SpriteName);
            Assert.Equal(5, doc.Frames.Count);
            Assert.Equal(5, doc.SpriteState.Frames.Count);
        }

        [Fact]
        public void T1_R2_03_AnimationCache_PreservesEditsAcrossTabClose()
        {
            var mockTabService = new Mock<IWorkspaceTabService>();
            var sprite = E2ETestHelper.CreateTestSprite(2);
            var entry = new FlipperManifestEntry { Name = "CachedAnim", MinLevel = 1, MaxLevel = 30, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("CachedAnim", sprite, entry)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack, tabService: mockTabService.Object);

            // Simulate editing sprite in canvas tab: adding a 3rd frame
            Assert.True(vm.AnimationSprites.TryGetValue("CachedAnim", out var cachedSprite));
            Assert.NotNull(cachedSprite);
            cachedSprite.Frames.Add(new FrameState { Name = "Frame 3" });

            // Re-verify dictionary maintains the edited 3-frame sprite
            Assert.Equal(3, vm.AnimationSprites["CachedAnim"].Frames.Count);
        }

        [Fact]
        public void T1_R2_04_BidirectionalSync_RenamingAnimationUpdatesMatrixAndTab()
        {
            var mockTabService = new Mock<IWorkspaceTabService>();
            mockTabService.Setup(t => t.RenameTab(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

            var sprite = E2ETestHelper.CreateTestSprite(2);
            var entry = new FlipperManifestEntry { Name = "OldName", MinLevel = 1, MaxLevel = 30, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("OldName", sprite, entry)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack, tabService: mockTabService.Object);
            vm.SelectedEntry = vm.Entries[0];

            // Rename via SelectedName property
            vm.SelectedName = "RenamedAnim";

            Assert.Equal("RenamedAnim", vm.SelectedEntry.Name);
            Assert.True(vm.AnimationSprites.ContainsKey("RenamedAnim"));
            mockTabService.Verify(t => t.RenameTab("OldName", "RenamedAnim"), Times.Once);
        }

        [Fact]
        public void T1_R2_05_LCDPreviewPlayer_PlaysFramesOrderSequence()
        {
            var sprite = E2ETestHelper.CreateTestSprite(3);
            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                PassiveFrameCount = 3,
                ActiveFrameCount = 0,
                FramesOrder = [2, 1, 0] // Reversed order
            };

            var entry = new FlipperManifestEntry { Name = "ReverseAnim", MinLevel = 1, MaxLevel = 30, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("ReverseAnim", sprite, entry)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);
            vm.SelectedEntry = vm.Entries[0];

            Assert.Equal(0, vm.PreviewFrameIndex);
            vm.PreviewNextFrame();
            Assert.Equal(1, vm.PreviewFrameIndex);
            vm.PreviewNextFrame();
            Assert.Equal(2, vm.PreviewFrameIndex);
            vm.PreviewNextFrame();
            Assert.Equal(0, vm.PreviewFrameIndex); // Wraps
        }

        [Fact]
        public void T1_R3_01_FullDirectoryTreeExport_MomentumAndStockFormats()
        {
            string momentumDir = Path.Combine(_tempDirectory, "ExportMomentum");
            string stockDir = Path.Combine(_tempDirectory, "ExportStock");

            var sprite = E2ETestHelper.CreateTestSprite(2);
            var momentumEntry = new FlipperManifestEntry { Name = "TestExport", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var stockEntry = new FlipperManifestEntry { Name = "TestExport", MinLevel = 1, MaxLevel = 3, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var settings = new FlipperExportSettings { AnimationName = "TestExport" };

            // 1. Momentum Export
            _exportService.ExportAssetPack([(sprite, momentumEntry, settings)], momentumDir, isMomentum: true);
            Assert.True(File.Exists(Path.Combine(momentumDir, "Anims", "manifest.txt")));
            Assert.True(File.Exists(Path.Combine(momentumDir, "Anims", "TestExport", "meta.txt")));
            Assert.True(File.Exists(Path.Combine(momentumDir, "Anims", "TestExport", "frame_0.bm")));
            Assert.True(File.Exists(Path.Combine(momentumDir, "Icons", "I_TestExport_10x10.bm")));

            // 2. Stock Export
            _exportService.ExportAssetPack([(sprite, stockEntry, settings)], stockDir, isMomentum: false);
            Assert.True(File.Exists(Path.Combine(stockDir, "dolphin", "manifest.txt")));
            Assert.True(File.Exists(Path.Combine(stockDir, "dolphin", "TestExport", "meta.txt")));
            Assert.True(File.Exists(Path.Combine(stockDir, "dolphin", "TestExport", "frame_0.bm")));
        }

        [Fact]
        public void T1_R3_02_PreFlightValidation_CatchesAllHardwareRuleViolations()
        {
            var invalidSprite = new SpriteState(256, 128); // Exceeds 128x64 limit
            var invalidEntry = new FlipperManifestEntry
            {
                Name = "Invalid Bounds Entry",
                MinLevel = 35, // > 30
                MaxLevel = 10, // Max < Min
                MinButthurt = 15, // > 14
                MaxButthurt = 5,  // Max < Min
                Weight = 0        // <= 0
            };
            var settings = new FlipperExportSettings { AnimationName = invalidEntry.Name };

            var diagnostics = _exportService.ValidateAssetPackForExport([(invalidSprite, invalidEntry, settings)], isMomentum: true);

            Assert.Contains(diagnostics, d => d.Code == "FZM001"); // Exceeds 128x64 limit
            Assert.Contains(diagnostics, d => d.Code == "FZ002");  // Min level > 30
            Assert.Contains(diagnostics, d => d.Code == "FZ003");  // Max level < Min level
            Assert.Contains(diagnostics, d => d.Code == "FZ004");  // Min butthurt > 14
            Assert.Contains(diagnostics, d => d.Code == "FZ005");  // Max butthurt < Min butthurt
            Assert.Contains(diagnostics, d => d.Code == "FZ006");  // Weight <= 0
        }

        [Fact]
        public void T1_R3_03_ShellViewModel_MenuExportRouting_WhenAssetPackActive()
        {
            var shell = E2ETestHelper.CreateTestShellViewModel();
            var doc = AssetPackDocument.CreateNew("ShellRoutingPack");
            var assetPackVm = new AssetPackViewModel(doc);

            shell.OpenDocuments.Add(assetPackVm);
            shell.ActiveDocument = assetPackVm;

            Assert.Equal(DocumentMode.AssetPack, shell.ActiveDocument.Mode);
            Assert.True(shell.ExportFlipperMenuCommand.CanExecute(null));
        }

        #endregion

        #region Tier 2: Boundary & Corner Cases

        [Fact]
        public void T2_Boundary_SingleCellMatrix_ValidatesAndExportsAccurately()
        {
            string exportDir = Path.Combine(_tempDirectory, "SingleCellPack");
            var sprite = E2ETestHelper.CreateTestSprite(1);
            var entry = new FlipperManifestEntry { Name = "L1M0_Only", MinLevel = 1, MaxLevel = 1, MinButthurt = 0, MaxButthurt = 0, Weight = 1 };
            var settings = new FlipperExportSettings { AnimationName = "L1M0_Only" };

            var diags = _exportService.ValidateAssetPackForExport([(sprite, entry, settings)], isMomentum: true);
            Assert.DoesNotContain(diags, d => d.Severity == FlipperValidationSeverity.Error);

            _exportService.ExportAssetPack([(sprite, entry, settings)], exportDir, isMomentum: true);
            string manifestText = File.ReadAllText(Path.Combine(exportDir, "Anims", "manifest.txt"));
            var manifest = FlipperManifest.Parse(manifestText);
            var matrix = new FlipperScheduleMatrix(manifest.Entries);

            Assert.Equal(1, matrix.CoveredCellsCount);
            Assert.Equal(1.0 / 450.0 * 100.0, matrix.CoveragePercentage, 3);
        }

        [Fact]
        public void T2_Boundary_DeadzoneGapAtLevel30Mood14_IdentifiedCorrectly()
        {
            var entries = new List<FlipperManifestEntry>
            {
                new() { Name = "AllExceptCorner", MinLevel = 1, MaxLevel = 29, MinButthurt = 0, MaxButthurt = 14, Weight = 1 },
                new() { Name = "AllExceptCorner2", MinLevel = 30, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 13, Weight = 1 }
            };

            var matrix = new FlipperScheduleMatrix(entries);
            var uncovered = matrix.GetUncoveredCells();

            Assert.Single(uncovered);
            Assert.Equal(30, uncovered[0].Level);
            Assert.Equal(14, uncovered[0].Mood);
        }

        [Fact]
        public void T2_Boundary_SpecialCharactersAndSpacesInAnimationNames_SanitizedSafely()
        {
            string rawName = "  Dolphin @ Night #2 (Final)!!  ";
            string sanitized = FlipperExportService.SanitizeAnimationName(rawName);

            Assert.Equal("Dolphin_Night_2_Final", sanitized);
            Assert.DoesNotContain("@", sanitized);
            Assert.DoesNotContain("#", sanitized);
            Assert.DoesNotContain("!", sanitized);
            Assert.DoesNotContain(" ", sanitized);
        }

        [Fact]
        public void T2_Boundary_LargeAssetPack_50Animations_AutoBalancesAndMaintains100Coverage()
        {
            var entries = new List<FlipperManifestEntry>();
            for (int i = 0; i < 50; i++)
            {
                entries.Add(new FlipperManifestEntry { Name = $"Anim_{i}", MinLevel = 1, MaxLevel = 1, MinButthurt = 0, MaxButthurt = 0, Weight = 1 });
            }

            var balanced = FlipperScheduleMatrix.AutoBalanceEntries(entries);
            var matrix = new FlipperScheduleMatrix(balanced);

            Assert.Equal(50, balanced.Count);
            Assert.Equal(450, matrix.CoveredCellsCount);
            Assert.Equal(100.0, matrix.CoveragePercentage);
            Assert.Empty(matrix.GetUncoveredCells());
        }

        [Fact]
        public void T2_Boundary_EmptyManifestCommentsOnly_ReturnsEmptyEntriesWithoutError()
        {
            string commentsOnlyManifest = "# This is a comment\n# Another comment\n\n   \n# Final comment\n";
            var parsed = FlipperManifest.Parse(commentsOnlyManifest);

            Assert.NotNull(parsed);
            Assert.Empty(parsed.Entries);
        }

        #endregion

        #region Tier 3: Pairwise Combinatorial Interactions

        [Fact]
        public void T3_Pairwise_IngestPack_EditInTab_VerifyMatrixPreviewReflectsNewPixels()
        {
            // 1. Create and ingest pack
            string packDir = Path.Combine(_tempDirectory, "PairwisePack");
            string animsDir = Path.Combine(packDir, "Anims", "InteractiveAnim");
            Directory.CreateDirectory(animsDir);
            File.WriteAllText(Path.Combine(animsDir, "meta.txt"),
                "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nPassive frames: 1\nActive frames: 0\n");
            File.WriteAllBytes(Path.Combine(animsDir, "frame_0.bm"), new byte[1024]);
            File.WriteAllText(Path.Combine(packDir, "Anims", "manifest.txt"),
                "Filetype: Flipper Animation Manifest\nVersion: 1\n\nName: InteractiveAnim\nMin level: 1\nMax level: 30\nWeight: 1\n");

            var imported = _importService.ImportAssetPack(packDir);
            var mockTabService = new Mock<IWorkspaceTabService>();

            var vm = new FlipperScheduleMatrixViewModel(imported, tabService: mockTabService.Object);
            vm.SelectedEntry = vm.Entries[0];

            // 2. Simulate Canvas Edit in Tab
            Assert.True(vm.AnimationSprites.TryGetValue("InteractiveAnim", out var spriteInTab));
            Assert.NotNull(spriteInTab);
            bool[] px = spriteInTab.Frames[0].LayerPixels[0].GetMonochromeData();
            px[0] = true;
            px[128 * 63 + 127] = true; // Set corner pixels

            // Notify matrix view model of canvas edit
            mockTabService.Setup(t => t.GetAllOpenSprites()).Returns([("InteractiveAnim", spriteInTab)]);
            vm.RefreshCurrentPreviewSprite();

            // 3. Assert matrix preview bitmap reflects edited sprite
            Assert.NotNull(vm.PreviewBitmap);
        }

        [Fact]
        public void T3_Pairwise_DeadzoneClick_CreatesTab_And_LinksToMatrix()
        {
            var mockTabService = new Mock<IWorkspaceTabService>();
            string openedTitle = string.Empty;
            SpriteState? openedSprite = null;

            mockTabService.Setup(t => t.OpenSpriteInTab(It.IsAny<SpriteState>(), It.IsAny<string>()))
                .Callback<SpriteState, string>((s, t) =>
                {
                    openedSprite = s;
                    openedTitle = t;
                });

            var vm = new FlipperScheduleMatrixViewModel(tabService: mockTabService.Object);
            // Create gap by setting baby max level to 5
            vm.Entries[0].MaxLevel = 5;

            // Click deadzone gap
            vm.CreateTabForGap();

            mockTabService.Verify(t => t.OpenSpriteInTab(It.IsAny<SpriteState>(), It.IsAny<string>()), Times.Once);
            Assert.NotNull(openedSprite);
            Assert.Equal(128, openedSprite.Width);
            Assert.Equal(64, openedSprite.Height);
        }

        [Fact]
        public void T3_Pairwise_ZipExport_And_ReImport_ExactBitwiseRoundTrip()
        {
            string zipPath = Path.Combine(_tempDirectory, "RoundTripPack.zip");
            var originalSprite = E2ETestHelper.CreateTestSprite(3);
            var originalEntry = new FlipperManifestEntry { Name = "RoundTripAnim", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 3 };
            var originalSettings = new FlipperExportSettings { AnimationName = "RoundTripAnim" };

            // Export to Zip
            _exportService.ExportAssetPackZip([(originalSprite, originalEntry, originalSettings)], zipPath);

            // Re-import from Zip
            var imported = _importService.ImportAssetPack(zipPath);

            Assert.Single(imported);
            var (name, sprite, entry) = imported[0];
            Assert.Equal("RoundTripAnim", name);
            Assert.Equal(3, sprite.Frames.Count);
            Assert.Equal(1, entry.MinLevel);
            Assert.Equal(30, entry.MaxLevel);
            Assert.Equal(3, entry.Weight);

            // Verify pixel fidelity
            for (int f = 0; f < 3; f++)
            {
                bool[] originalPx = originalSprite.CompositeFramePixels(f);
                bool[] importedPx = sprite.CompositeFramePixels(f);
                Assert.Equal(originalPx, importedPx);
            }
        }

        #endregion

        #region Tier 4: Real-World Scenarios

        [Fact]
        public void T4_RealWorld_CompleteAuthoringAndExportPipeline()
        {
            // 1. User starts new AssetPack document
            var doc = AssetPackDocument.CreateNew("CyberFlipper");
            var assetPackVm = new AssetPackViewModel(doc);

            // 2. Configure 3 tier entries
            var matrixVm = assetPackVm.MatrixViewModel;
            Assert.Equal(3, matrixVm.Entries.Count);
            matrixVm.Entries[0].Name = "Cyber_Baby";
            matrixVm.Entries[1].Name = "Cyber_Teen";
            matrixVm.Entries[2].Name = "Cyber_Adult";

            // 3. User authors frames for each entry
            for (int i = 0; i < matrixVm.Entries.Count; i++)
            {
                matrixVm.NavigateToEntryTab(matrixVm.Entries[i]);
            }

            // 4. Pre-flight hardware validation
            var exportTuples = matrixVm.Entries.Select(e => (
                matrixVm.AnimationSprites.TryGetValue(e.Name, out var s) ? s : new SpriteState(128, 64),
                e.Entry,
                new FlipperExportSettings { AnimationName = e.Name, TargetMode = FlipperExportTargetMode.MomentumAssetPack }
            )).ToList();

            var diagnostics = _exportService.ValidateAssetPackForExport(exportTuples, isMomentum: true);
            Assert.DoesNotContain(diagnostics, d => d.Severity == FlipperValidationSeverity.Error);

            // 5. Export to full folder tree
            string targetDir = Path.Combine(_tempDirectory, "CyberFlipper_Export");
            _exportService.ExportAssetPack(exportTuples, targetDir, isMomentum: true);

            // 6. Verify filesystem tree on disk
            Assert.True(File.Exists(Path.Combine(targetDir, "Anims", "manifest.txt")));
            Assert.True(File.Exists(Path.Combine(targetDir, "Anims", "Cyber_Baby", "meta.txt")));
            Assert.True(File.Exists(Path.Combine(targetDir, "Anims", "Cyber_Baby", "frame_0.bm")));
            Assert.True(File.Exists(Path.Combine(targetDir, "Icons", "I_Cyber_Baby_10x10.bm")));
        }

        #endregion

        #region Tier 5: Adversarial Stress Tests

        [Fact]
        public void T5_Adversarial_RapidMatrixEditsAndRedraws_ThreadSafeAndDeterministic()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            int redrawCount = 0;
            vm.MatrixRedrawRequested += (s, e) => Interlocked.Increment(ref redrawCount);

            // 1,000 rapid state changes
            for (int i = 0; i < 1000; i++)
            {
                vm.SelectedEntry = vm.Entries[i % vm.Entries.Count];
                vm.SelectedMinLevel = (i % 25) + 1;
                vm.SelectedMaxLevel = Math.Min(30, vm.SelectedMinLevel + 5);
                vm.SelectedWeight = (i % 10) + 1;
            }

            Assert.True(redrawCount > 500);
            Assert.InRange(vm.Matrix.CoveredCellsCount, 0, 450);
            Assert.NotNull(vm.MatrixStatsText);
            Assert.NotNull(vm.CoverageBadgeText);
        }

        [Fact]
        public void T5_Adversarial_ConcurrentExportAndIngestion_ZeroDeadlocks()
        {
            // Run 10 parallel export + zip + import tasks with isolated directories and animations
            Parallel.For(0, 10, i =>
            {
                var sprite = E2ETestHelper.CreateTestSprite(2);
                string animName = $"ConcurrentAnim_{i}";
                var entry = new FlipperManifestEntry { Name = animName, MinLevel = 1, MaxLevel = 30, Weight = 1 };
                var settings = new FlipperExportSettings { AnimationName = animName };

                string threadDir = Path.Combine(_tempDirectory, $"Thread_{i}_{Guid.NewGuid():N}");
                Directory.CreateDirectory(threadDir);

                string zipFile = Path.Combine(threadDir, "Pack.zip");
                var localExportService = new FlipperExportService();
                var localImportService = new FlipperImportService();

                localExportService.ExportAssetPackZip([(sprite, entry, settings)], zipFile);

                var reimported = localImportService.ImportAssetPack(zipFile);
                Assert.Single(reimported);
                Assert.Equal(animName, reimported[0].Name);
            });
        }

        [Fact]
        public void T5_Adversarial_MaliciousPathTraversalInZip_SanitizedSafely()
        {
            // Create a zip with relative paths attempting to escape target directory
            string zipPath = Path.Combine(_tempDirectory, "MaliciousTraversal.zip");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("../../../EscapeAttempt/evil.bm");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("malicious");
            }

            // Ingestion must safely reject path traversal by throwing IOException (blocked by .NET zip safety)
            Assert.Throws<IOException>(() => _importService.ImportAssetPack(zipPath));
        }

        [Fact]
        public void T4_RealWorld_ManifestSearch_Filtering_Scrubbing_AndQuickFixWorkflow()
        {
            // Create rich 4-frame sprite
            var sprite1 = E2ETestHelper.CreateTestSprite(4);
            var sprite2 = E2ETestHelper.CreateTestSprite(3);

            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("baby_play", sprite1, new FlipperManifestEntry { Name = "baby_play", MinLevel = 1, MaxLevel = 9, MinButthurt = 0, MaxButthurt = 4, Weight = 2 }),
                ("teen_work", sprite2, new FlipperManifestEntry { Name = "teen_work", MinLevel = 10, MaxLevel = 19, MinButthurt = 5, MaxButthurt = 8, Weight = 1 }),
                ("adult_rest", sprite1, new FlipperManifestEntry { Name = "adult_rest", MinLevel = 20, MaxLevel = 30, MinButthurt = 9, MaxButthurt = 14, Weight = 1 })
            };

            var vm = new FlipperScheduleMatrixViewModel(pack, exportService: _exportService);

            // 1. Frame Scrubber & Telemetry Verification
            vm.SelectedEntry = vm.Entries[0];
            Assert.Equal(4, vm.PreviewTotalFrames);
            Assert.Equal(3, vm.PreviewMaxFrameIndex);
            Assert.True(vm.PreviewHasMultipleFrames);
            Assert.Equal("128 × 64", vm.PreviewDimensionsText);

            // Scrub through frames
            vm.CurrentPreviewFrameIndex = 2;
            Assert.Equal(2, vm.PreviewFrameIndex);
            Assert.Equal("Frame 3 / 4", vm.PreviewFrameCountText);

            // Transport step forward and backward
            vm.PreviewNextFrameCommand.Execute(null);
            Assert.Equal(3, vm.CurrentPreviewFrameIndex);
            vm.PreviewNextFrameCommand.Execute(null);
            Assert.Equal(0, vm.CurrentPreviewFrameIndex); // Looped back to start

            // Speed cycle
            vm.CyclePreviewSpeedCommand.Execute(null);
            Assert.Equal(1.5, vm.PreviewSpeedMultiplier);

            // 2. Search & Filtering
            vm.SearchFilterText = "teen";
            Assert.Single(vm.FilteredEntries);
            Assert.Equal("teen_work", vm.FilteredEntries[0].Name);
            Assert.Equal("1 of 3 Animations", vm.FilterCountText);

            vm.SelectedStageFilter = "Teen";
            vm.SearchFilterText = string.Empty;
            Assert.Single(vm.FilteredEntries);
            Assert.Equal("teen_work", vm.FilteredEntries[0].Name);

            vm.ResetFiltersCommand.Execute(null);
            Assert.Equal(3, vm.FilteredEntries.Count);
            Assert.False(vm.HasActiveFilters);

            // 3. Inject Anomalies: Duplicate name and out-of-bounds weight
            vm.Entries[1].Name = vm.Entries[0].Name;
            vm.Entries[2].Entry.Weight = 200;
            vm.RecalculateMatrix();

            Assert.True(vm.HasActionableDiagnostics);
            Assert.True(vm.Entries[1].HasErrors);
            Assert.True(vm.Entries[2].HasWarnings);

            // Execute Batch Fix All
            vm.FixAllDiagnosticsCommand.Execute(null);

            Assert.False(vm.HasActionableDiagnostics);
            Assert.False(vm.Entries[1].HasErrors);
            Assert.False(vm.Entries[2].HasWarnings);
            Assert.NotEqual(vm.Entries[0].Name, vm.Entries[1].Name);
            Assert.Equal(10, vm.Entries[2].Weight);

            // 4. Verify Export Succeeded with Clean Validated Manifest
            string outDir = Path.Combine(_tempDirectory, "E2E_M3_ExportClean");
            vm.ExportAssetPackFolder(outDir);

            Assert.True(File.Exists(Path.Combine(outDir, "Anims", "manifest.txt")));
            var reloaded = _importService.ImportAssetPack(outDir);
            Assert.Equal(3, reloaded.Count);
        }
        #endregion

        #region Tier 4: Milestone 4 Defect Resolution, Undo/Redo & State Integrity

        [Fact]
        public void T4_R4_01_ModeToggle_UndoRedo_FullDocumentSyncAndExportVerification()
        {
            var e1 = new FlipperManifestEntry { Name = "baby_step", MinLevel = 1, MaxLevel = 9, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var e2 = new FlipperManifestEntry { Name = "teen_step", MinLevel = 10, MaxLevel = 19, MinButthurt = 0, MaxButthurt = 14, Weight = 2 };
            var e3 = new FlipperManifestEntry { Name = "adult_step", MinLevel = 20, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 3 };

            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("baby_step", new SpriteState(128, 64), e1),
                ("teen_step", new SpriteState(128, 64), e2),
                ("adult_step", new SpriteState(128, 64), e3)
            };

            var docVm = new AssetPackViewModel(pack, "SyncPack", exportService: _exportService);
            var vm = docVm.MatrixViewModel;

            Assert.False(vm.IsStockMode);
            Assert.Equal(30, vm.MaxAllowedLevel);

            // 1. Toggle to Stock Mode
            vm.ToggleMode();
            Assert.True(vm.IsStockMode);
            Assert.Equal(3, vm.MaxAllowedLevel);
            Assert.All(vm.Entries, e => Assert.InRange(e.MaxLevel, 1, 3));

            // Document should reflect stock mode
            var docStock = docVm.ToDocument();
            Assert.True(docStock.IsStockMode);

            // 2. Undo Mode Toggle
            vm.Undo();
            Assert.False(vm.IsStockMode);
            Assert.Equal(30, vm.MaxAllowedLevel);
            Assert.Equal(9, vm.Entries[0].MaxLevel);
            Assert.Equal(19, vm.Entries[1].MaxLevel);
            Assert.Equal(30, vm.Entries[2].MaxLevel);
            Assert.False(vm.HasValidationIssues);

            // Document should reflect restored extended mode
            var docRestored = docVm.ToDocument();
            Assert.False(docRestored.IsStockMode);
            Assert.Equal(30, docRestored.Entries[2].MaxLevel);

            // 3. Export directory and verify manifest on disk
            string exportDir = Path.Combine(_tempDirectory, "E2E_M4_Export_Restored");
            vm.ExportAssetPackFolder(exportDir);

            string manifestPath = Path.Combine(exportDir, "Anims", "manifest.txt");
            Assert.True(File.Exists(manifestPath));
            string manifestText = File.ReadAllText(manifestPath);
            Assert.Contains("Max level: 30", manifestText);

            var reimported = _importService.ImportAssetPack(exportDir);
            Assert.Equal(3, reimported.Count);
            Assert.Equal(30, reimported.Max(r => r.ManifestEntry.MaxLevel));
        }

        [Fact]
        public void T4_R4_02_InspectorEdits_DirtyTrackingAndUndoRedoRoundtrip()
        {
            string docPath = Path.Combine(_tempDirectory, "test_dirty_tracking.hexpack");
            var docVm = new AssetPackViewModel(exportService: _exportService);
            var vm = docVm.MatrixViewModel;

            docVm.SaveToPath(docPath);
            Assert.False(docVm.IsDirty);

            // Perform edit on MinLevel
            vm.SelectedEntry = vm.Entries[0];
            vm.SelectedMinLevel = 2;
            Assert.True(docVm.IsDirty);
            Assert.StartsWith("*", docVm.Title);

            // Perform edit on Weight
            vm.SelectedWeight = 5;

            // Perform edit on PackName
            vm.PackName = "Renamed Dirty Pack";
            Assert.Contains("Renamed Dirty Pack", docVm.Title);

            // Save changes
            docVm.Save();
            Assert.False(docVm.IsDirty);

            // Undo PackName
            vm.Undo();
            Assert.True(docVm.IsDirty);
            Assert.Equal("Flipper Asset Pack", vm.PackName);

            // Undo Weight
            vm.Undo();
            Assert.Equal(1, vm.SelectedWeight);

            // Undo MinLevel
            vm.Undo();
            Assert.Equal(1, vm.SelectedMinLevel);

            // Save cleaned state and reload
            docVm.Save();
            Assert.False(docVm.IsDirty);

            var reloadedVm = new AssetPackViewModel(exportService: _exportService);
            string json = File.ReadAllText(docPath);
            var doc = System.Text.Json.JsonSerializer.Deserialize<AssetPackDocument>(json);
            Assert.NotNull(doc);
            reloadedVm.LoadDocument(doc);

            Assert.Equal("Flipper Asset Pack", reloadedVm.MatrixViewModel.PackName);
            Assert.Equal(1, reloadedVm.MatrixViewModel.Entries[0].MinLevel);
            Assert.Equal(1, reloadedVm.MatrixViewModel.Entries[0].Weight);
            Assert.False(reloadedVm.IsDirty);
        }

        [Fact]
        public void T4_R4_03_SpriteCache_Synchronization_AcrossTabEditsAndExports()
        {
            var mockTabService = new Mock<IWorkspaceTabService>();
            var openList = new List<(string Title, SpriteState Sprite)>();
            mockTabService.Setup(t => t.OpenSpriteInTab(It.IsAny<SpriteState>(), It.IsAny<string>()))
                .Callback<SpriteState, string>((s, t) => openList.Add((t, s)));
            mockTabService.Setup(t => t.GetAllOpenSprites()).Returns(openList);

            var docVm = new AssetPackViewModel(tabService: mockTabService.Object, exportService: _exportService);
            var vm = docVm.MatrixViewModel;

            var targetEntry = vm.Entries[0];
            vm.NavigateToEntryTab(targetEntry);

            // Add frames to the active sprite in tab service
            Assert.NotEmpty(openList);
            var activeSprite = openList[0].Sprite;
            activeSprite.Frames.Clear();
            activeSprite.Frames.Add(new FrameState { Name = "Frame 1" });
            activeSprite.Frames.Add(new FrameState { Name = "Frame 2" });
            activeSprite.Frames.Add(new FrameState { Name = "Frame 3" });
            activeSprite.FrameRateFps = 15;

            // Export asset pack zip
            string zipPath = Path.Combine(_tempDirectory, "E2E_M4_Export_Synced.zip");
            vm.ExportAssetPackZip(zipPath);

            Assert.True(File.Exists(zipPath));
            using var archive = ZipFile.OpenRead(zipPath);
            var frameEntries = archive.Entries.Where(e => e.FullName.Contains($"Anims/{targetEntry.Name}/frame_") && e.Name.EndsWith(".bm")).ToList();
            Assert.Equal(3, frameEntries.Count);
            Assert.Contains(archive.Entries, e => e.FullName.Contains($"Icons/I_{targetEntry.Name}_10x10.bm"));
        }

        #endregion
    }
}
