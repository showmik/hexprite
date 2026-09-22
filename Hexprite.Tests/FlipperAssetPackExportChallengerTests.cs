using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Hexprite.ViewModels.Flipper;
using Moq;
using Xunit;

namespace Hexprite.Tests
{
    [Collection("WindowLayoutSettingsFile")]
    [Trait("Category", "Unit")]
    public class FlipperAssetPackExportChallengerTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly FlipperExportService _service;

        public FlipperAssetPackExportChallengerTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "HexpriteChallengerTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _service = new FlipperExportService();
            WpfTestHelper.EnsureApplication();
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, true); } catch { }
            }
        }

        #region Pre-Flight Validation Stress Tests

        [Fact]
        public void ValidateAssetPackForExport_NullOrEmptyList_ReturnsFZ010Error()
        {
            var diagsNull = _service.ValidateAssetPackForExport(null!);
            Assert.Contains(diagsNull, d => d.Code == "FZ010" && d.Severity == FlipperValidationSeverity.Error);

            var diagsEmpty = _service.ValidateAssetPackForExport(new List<(SpriteState, FlipperManifestEntry, FlipperExportSettings)>());
            Assert.Contains(diagsEmpty, d => d.Code == "FZ010" && d.Severity == FlipperValidationSeverity.Error);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t\n")]
        public void ValidateAssetPackForExport_EmptyOrWhitespaceAnimationName_ReturnsFZ001Error(string? invalidName)
        {
            var sprite = new SpriteState(128, 64);
            var entry = new FlipperManifestEntry { Name = invalidName!, MinLevel = 1, MaxLevel = 30, Weight = 1 };
            var settings = new FlipperExportSettings(_tempDir, invalidName ?? "", 5, 1, 0);

            var diags = _service.ValidateAssetPackForExport([(sprite, entry, settings)], isMomentum: true);

            Assert.Contains(diags, d => d.Code == "FZ001" && d.Severity == FlipperValidationSeverity.Error);
        }

        [Fact]
        public void ValidateAssetPackForExport_DuplicateNamesDifferentCasingAndSpacing_ReturnsFZ011Error()
        {
            var sprite1 = new SpriteState(128, 64);
            var sprite2 = new SpriteState(128, 64);
            var entry1 = new FlipperManifestEntry { Name = "DolphinDance", MinLevel = 1, MaxLevel = 10, Weight = 1 };
            var entry2 = new FlipperManifestEntry { Name = "  dolphindance  ", MinLevel = 11, MaxLevel = 30, Weight = 1 };
            var settings1 = new FlipperExportSettings(_tempDir, "DolphinDance", 5, 1, 0);
            var settings2 = new FlipperExportSettings(_tempDir, "dolphindance", 5, 1, 0);

            var diags = _service.ValidateAssetPackForExport([(sprite1, entry1, settings1), (sprite2, entry2, settings2)], isMomentum: true);

            Assert.Contains(diags, d => d.Code == "FZ011" && d.Severity == FlipperValidationSeverity.Error);
        }

        [Fact]
        public void ValidateAssetPackForExport_NullSprite_ReturnsFZS001Error()
        {
            var entry = new FlipperManifestEntry { Name = "ValidAnim", MinLevel = 1, MaxLevel = 30, Weight = 1 };
            var settings = new FlipperExportSettings(_tempDir, "ValidAnim", 5, 1, 0);

            var diags = _service.ValidateAssetPackForExport([(null!, entry, settings)], isMomentum: true);

            Assert.Contains(diags, d => d.Code == "FZS001" && d.Severity == FlipperValidationSeverity.Error);
        }

        [Fact]
        public void ValidateAssetPackForExport_ZeroFrames_ReturnsFZS002Error()
        {
            var sprite = new SpriteState(128, 64);
            sprite.Frames.Clear();
            var entry = new FlipperManifestEntry { Name = "EmptyAnim", MinLevel = 1, MaxLevel = 30, Weight = 1 };
            var settings = new FlipperExportSettings(_tempDir, "EmptyAnim", 5, 0, 0);

            var diags = _service.ValidateAssetPackForExport([(sprite, entry, settings)], isMomentum: true);

            Assert.Contains(diags, d => d.Code == "FZS002" && d.Severity == FlipperValidationSeverity.Error);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(129, 64)]
        [InlineData(128, 65)]
        [InlineData(256, 128)]
        [InlineData(10, 100)]
        [InlineData(200, 50)]
        public void ValidateAssetPackForExport_InvalidDimensions_ReturnsFZM001Error(int width, int height)
        {
            var sprite = new SpriteState(width, height);

            var entry = new FlipperManifestEntry { Name = "BadDimAnim", MinLevel = 1, MaxLevel = 30, Weight = 1 };
            var settings = new FlipperExportSettings(_tempDir, "BadDimAnim", 5, 1, 0);

            var diags = _service.ValidateAssetPackForExport([(sprite, entry, settings)], isMomentum: true);

            Assert.Contains(diags, d => d.Code == "FZM001" && d.Severity == FlipperValidationSeverity.Error);
        }

        [Theory]
        [InlineData(new int[] { -1 })]
        [InlineData(new int[] { 0, 1, 2 })] // 2 physical frames available (indices 0, 1) -> 2 is out of range
        [InlineData(new int[] { 0, 99 })]
        public void ValidateAssetPackForExport_InvalidFramesOrderIndices_ReturnsFZM004Error(int[] invalidOrder)
        {
            var sprite = new SpriteState(128, 64);
            sprite.Frames.Clear();
            sprite.Frames.Add(new FrameState { LayerPixels = [new MonochromePixelBuffer(new bool[128 * 64])] });
            sprite.Frames.Add(new FrameState { LayerPixels = [new MonochromePixelBuffer(new bool[128 * 64])] }); // 2 frames (0 and 1)

            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                FramesOrder = invalidOrder
            };

            var entry = new FlipperManifestEntry { Name = "BadOrderAnim", MinLevel = 1, MaxLevel = 30, Weight = 1 };
            var settings = new FlipperExportSettings(_tempDir, "BadOrderAnim", 5, 1, 1);

            var diags = _service.ValidateAssetPackForExport([(sprite, entry, settings)], isMomentum: true);

            Assert.Contains(diags, d => d.Code == "FZM004" && d.Severity == FlipperValidationSeverity.Error);
        }

        [Theory]
        [InlineData(0, 10, true, "FZ002")]    // MinLevel < 1
        [InlineData(31, 30, true, "FZ002")]   // MinLevel > 30 in Momentum
        [InlineData(4, 3, false, "FZ002")]    // MinLevel > 3 in Stock
        [InlineData(15, 10, true, "FZ003")]   // MaxLevel < MinLevel
        [InlineData(1, 31, true, "FZ003")]    // MaxLevel > 30 in Momentum
        [InlineData(1, 5, false, "FZ003")]    // MaxLevel > 3 in Stock
        [InlineData(1, 10, true, null, -1, 10, "FZ004")]  // MinButthurt < 0
        [InlineData(1, 10, true, null, 15, 14, "FZ004")]  // MinButthurt > 14
        [InlineData(1, 10, true, null, 8, 4, "FZ005")]    // MaxButthurt < MinButthurt
        [InlineData(1, 10, true, null, 0, 15, "FZ005")]   // MaxButthurt > 14
        [InlineData(1, 10, true, null, 0, 14, null, 0, "FZ006")]  // Weight <= 0
        public void ValidateAssetPackForExport_ManifestBoundsViolations_ReturnsAppropriateErrorCode(
            int minLvl, int maxLvl, bool isMomentum, string? expectedLevelCode,
            int minBh = 0, int maxBh = 14, string? expectedMoodCode = null,
            int weight = 1, string? expectedWeightCode = null)
        {
            var sprite = new SpriteState(128, 64);
            var entry = new FlipperManifestEntry
            {
                Name = "BoundsAnim",
                MinLevel = minLvl,
                MaxLevel = maxLvl,
                MinButthurt = minBh,
                MaxButthurt = maxBh,
                Weight = weight
            };
            var settings = new FlipperExportSettings(_tempDir, "BoundsAnim", 5, 1, 0, minLvl, maxLvl, minBh, maxBh, weight);

            var diags = _service.ValidateAssetPackForExport([(sprite, entry, settings)], isMomentum: isMomentum);

            if (expectedLevelCode != null)
            {
                Assert.Contains(diags, d => d.Code == expectedLevelCode && d.Severity == FlipperValidationSeverity.Error);
            }
            if (expectedMoodCode != null)
            {
                Assert.Contains(diags, d => d.Code == expectedMoodCode && d.Severity == FlipperValidationSeverity.Error);
            }
            if (expectedWeightCode != null)
            {
                Assert.Contains(diags, d => d.Code == expectedWeightCode && d.Severity == FlipperValidationSeverity.Error);
            }
        }

        #endregion

        #region Momentum Directory & Zip Export Empirical Tests

        [Fact]
        public void ExportAssetPack_MomentumFormat_CreatesCompleteDirectoryTreeWithValidFiles()
        {
            string outDir = Path.Combine(_tempDir, "MomentumPack");
            var sprite1 = new SpriteState(128, 64);
            var sprite2 = new SpriteState(128, 64);
            sprite2.Frames.Add(new FrameState { LayerPixels = [new MonochromePixelBuffer(new bool[128 * 64])] });

            var entry1 = new FlipperManifestEntry { Name = "CatWalk", MinLevel = 1, MaxLevel = 15, MinButthurt = 0, MaxButthurt = 14, Weight = 2 };
            var entry2 = new FlipperManifestEntry { Name = "CatSleep", MinLevel = 16, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };

            var set1 = new FlipperExportSettings(outDir, "CatWalk", 8, 1, 0, 1, 15, 0, 14, 2);
            var set2 = new FlipperExportSettings(outDir, "CatSleep", 4, 1, 1, 16, 30, 0, 14, 1);

            _service.ExportAssetPack([(sprite1, entry1, set1), (sprite2, entry2, set2)], outDir, isMomentum: true);

            // Verify Directory Tree
            Assert.True(Directory.Exists(Path.Combine(outDir, "Anims")));
            Assert.True(Directory.Exists(Path.Combine(outDir, "Icons")));
            Assert.True(Directory.Exists(Path.Combine(outDir, "Anims", "CatWalk")));
            Assert.True(Directory.Exists(Path.Combine(outDir, "Anims", "CatSleep")));

            // Verify Manifest
            string manifestPath = Path.Combine(outDir, "Anims", "manifest.txt");
            Assert.True(File.Exists(manifestPath));
            string manifestText = File.ReadAllText(manifestPath);
            Assert.Contains("Name: CatWalk", manifestText);
            Assert.Contains("Min level: 1", manifestText);
            Assert.Contains("Max level: 15", manifestText);
            Assert.Contains("Name: CatSleep", manifestText);
            Assert.Contains("Min level: 16", manifestText);
            Assert.Contains("Max level: 30", manifestText);

            // Verify Meta.txt for CatWalk and CatSleep
            string meta1 = File.ReadAllText(Path.Combine(outDir, "Anims", "CatWalk", "meta.txt"));
            Assert.Contains("Width: 128", meta1);
            Assert.Contains("Height: 64", meta1);
            Assert.Contains("Frame rate: 8", meta1);

            string meta2 = File.ReadAllText(Path.Combine(outDir, "Anims", "CatSleep", "meta.txt"));
            Assert.Contains("Width: 128", meta2);
            Assert.Contains("Height: 64", meta2);
            Assert.Contains("Frames order: 0 1", meta2);

            // Verify Frame .bm files
            Assert.True(File.Exists(Path.Combine(outDir, "Anims", "CatWalk", "frame_0.bm")));
            Assert.True(File.Exists(Path.Combine(outDir, "Anims", "CatSleep", "frame_0.bm")));
            Assert.True(File.Exists(Path.Combine(outDir, "Anims", "CatSleep", "frame_1.bm")));

            // Verify Preview Icon in Icons/
            Assert.True(File.Exists(Path.Combine(outDir, "Icons", "I_CatWalk_10x10.bm")));
            byte[] iconBytes = File.ReadAllBytes(Path.Combine(outDir, "Icons", "I_CatWalk_10x10.bm"));
            Assert.NotEmpty(iconBytes);
        }

        [Fact]
        public void ExportAssetPackZip_MomentumFormat_GeneratesExtractableAndValidZip()
        {
            string zipPath = Path.Combine(_tempDir, "MomentumPack.zip");
            var sprite = new SpriteState(128, 64);
            var entry = new FlipperManifestEntry { Name = "ZipMomAnim", MinLevel = 1, MaxLevel = 30, Weight = 1 };
            var set = new FlipperExportSettings(_tempDir, "ZipMomAnim", 10, 1, 0);

            _service.ExportAssetPackZip([(sprite, entry, set)], zipPath, isMomentum: true);

            Assert.True(File.Exists(zipPath));

            string extractDir = Path.Combine(_tempDir, "MomentumZipExtracted");
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            Assert.True(File.Exists(Path.Combine(extractDir, "Anims", "manifest.txt")));
            Assert.True(File.Exists(Path.Combine(extractDir, "Anims", "ZipMomAnim", "meta.txt")));
            Assert.True(File.Exists(Path.Combine(extractDir, "Anims", "ZipMomAnim", "frame_0.bm")));
            Assert.True(File.Exists(Path.Combine(extractDir, "Icons", "I_ZipMomAnim_10x10.bm")));
        }

        #endregion

        #region Stock Directory & Zip Export Empirical Tests

        [Fact]
        public void ExportAssetPack_StockFormat_CreatesDolphinLayoutAndNoIconsDirectory()
        {
            string outDir = Path.Combine(_tempDir, "StockPack");
            var sprite1 = new SpriteState(128, 64);
            var sprite2 = new SpriteState(128, 64);

            var entry1 = new FlipperManifestEntry { Name = "BabyStock", MinLevel = 1, MaxLevel = 1, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var entry2 = new FlipperManifestEntry { Name = "AdultStock", MinLevel = 2, MaxLevel = 3, MinButthurt = 0, MaxButthurt = 14, Weight = 2 };

            var set1 = new FlipperExportSettings(outDir, "BabyStock", 5, 1, 0, 1, 1, 0, 14, 1);
            var set2 = new FlipperExportSettings(outDir, "AdultStock", 5, 1, 0, 2, 3, 0, 14, 2);

            _service.ExportAssetPack([(sprite1, entry1, set1), (sprite2, entry2, set2)], outDir, isMomentum: false);

            // Verify Directory Tree: dolphin/ exists, Anims/ and Icons/ must NOT exist
            Assert.True(Directory.Exists(Path.Combine(outDir, "dolphin")));
            Assert.False(Directory.Exists(Path.Combine(outDir, "Anims")));
            Assert.False(Directory.Exists(Path.Combine(outDir, "Icons")));

            // Verify Manifest in dolphin/
            string manifestPath = Path.Combine(outDir, "dolphin", "manifest.txt");
            Assert.True(File.Exists(manifestPath));
            string manifestText = File.ReadAllText(manifestPath);
            Assert.Contains("Name: BabyStock", manifestText);
            Assert.Contains("Max level: 1", manifestText);
            Assert.Contains("Name: AdultStock", manifestText);
            Assert.Contains("Max level: 3", manifestText);

            // Verify Meta and Frames
            Assert.True(File.Exists(Path.Combine(outDir, "dolphin", "BabyStock", "meta.txt")));
            Assert.True(File.Exists(Path.Combine(outDir, "dolphin", "BabyStock", "frame_0.bm")));
            Assert.True(File.Exists(Path.Combine(outDir, "dolphin", "AdultStock", "meta.txt")));
            Assert.True(File.Exists(Path.Combine(outDir, "dolphin", "AdultStock", "frame_0.bm")));
        }

        [Fact]
        public void ExportAssetPackZip_StockFormat_GeneratesZipWithDolphinLayoutAndNoIcons()
        {
            string zipPath = Path.Combine(_tempDir, "StockPack.zip");
            var sprite = new SpriteState(128, 64);
            var entry = new FlipperManifestEntry { Name = "StockZipAnim", MinLevel = 1, MaxLevel = 3, Weight = 1 };
            var set = new FlipperExportSettings(_tempDir, "StockZipAnim", 5, 1, 0, 1, 3, 0, 14, 1);

            _service.ExportAssetPackZip([(sprite, entry, set)], zipPath, isMomentum: false);

            Assert.True(File.Exists(zipPath));

            string extractDir = Path.Combine(_tempDir, "StockZipExtracted");
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            Assert.True(File.Exists(Path.Combine(extractDir, "dolphin", "manifest.txt")));
            Assert.True(File.Exists(Path.Combine(extractDir, "dolphin", "StockZipAnim", "meta.txt")));
            Assert.True(File.Exists(Path.Combine(extractDir, "dolphin", "StockZipAnim", "frame_0.bm")));
            Assert.False(Directory.Exists(Path.Combine(extractDir, "Icons")));
            Assert.False(Directory.Exists(Path.Combine(extractDir, "Anims")));
        }

        [Fact]
        public void ExportAssetPack_StockMode_WithMomentumLevels_ThrowsPreFlightValidationError()
        {
            var sprite = new SpriteState(128, 64);
            var entry = new FlipperManifestEntry { Name = "OverlevelStock", MinLevel = 1, MaxLevel = 15, Weight = 1 }; // MaxLevel 15 is illegal in Stock (max is 3)
            var set = new FlipperExportSettings(_tempDir, "OverlevelStock", 5, 1, 0, 1, 15, 0, 14, 1);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _service.ExportAssetPack([(sprite, entry, set)], _tempDir, isMomentum: false));

            Assert.Contains("FZ003", ex.Message);
        }

        #endregion

        #region ViewModel Sprite Resolution & Routing Empirical Tests

        [Fact]
        public void BuildExportAnimationList_ResolvesLiveSpriteFromOpenWorkspaceTab()
        {
            var tabServiceMock = new Mock<IWorkspaceTabService>();
            var modifiedSprite = new SpriteState(128, 64);
            modifiedSprite.Frames.Add(new FrameState { LayerPixels = [new MonochromePixelBuffer(new bool[128 * 64])] }); // 2 frames

            tabServiceMock.Setup(t => t.GetAllOpenSprites())
                .Returns([( "anim_baby", modifiedSprite )]);

            var vm = new FlipperScheduleMatrixViewModel(tabService: tabServiceMock.Object);

            var exportList = vm.BuildExportAnimationList(_tempDir);

            var babyExport = exportList.FirstOrDefault(e => e.ManifestEntry.Name == "anim_baby");
            Assert.NotNull(babyExport.Sprite);
            Assert.Equal(2, babyExport.Sprite.Frames.Count); // Should be live modified sprite with 2 frames
        }

        [Fact]
        public void ExportFolderAndZip_OnAssetPackViewModel_ForwardsToExportService()
        {
            var exportServiceMock = new Mock<IFlipperExportService>();
            var dialogMock = new Mock<IDialogService>();

            string targetFolder = Path.Combine(_tempDir, "ApvmExportFolder");
            string targetZip = Path.Combine(_tempDir, "ApvmExport.zip");

            var apvm = new AssetPackViewModel(
                dialogService: dialogMock.Object,
                exportService: exportServiceMock.Object);

            apvm.ExportFolder(targetFolder);
            exportServiceMock.Verify(e => e.ExportAssetPack(
                It.IsAny<IReadOnlyList<(SpriteState, FlipperManifestEntry, FlipperExportSettings)>>(),
                targetFolder,
                true), Times.Once);

            apvm.ExportZip(targetZip);
            exportServiceMock.Verify(e => e.ExportAssetPackZip(
                It.IsAny<IReadOnlyList<(SpriteState, FlipperManifestEntry, FlipperExportSettings)>>(),
                targetZip,
                true), Times.Once);
        }

        [Fact]
        public void ShellViewModel_ExportFlipperMenuCommand_WhenAssetPackTabActive_ExecutesFolderExport()
        {
            var winManagerMock = new Mock<IFlipperWindowManager>();
            var dialogMock = new Mock<IDialogService>();
            var exportServiceMock = new Mock<IFlipperExportService>();
            var autosaveMock = new Mock<IAutosaveService>();

            var spMock = new Mock<IServiceProvider>();
            spMock.Setup(sp => sp.GetService(typeof(IAutosaveService))).Returns(autosaveMock.Object);
            spMock.Setup(sp => sp.GetService(typeof(IFlipperWindowManager))).Returns(winManagerMock.Object);
            spMock.Setup(sp => sp.GetService(typeof(IFlipperExportService))).Returns(exportServiceMock.Object);

            var shell = new ShellViewModel(
                Mock.Of<ICodeGeneratorService>(),
                Mock.Of<IDrawingService>(),
                Mock.Of<IClipboardService>(),
                Mock.Of<IPixelClipboardService>(),
                dialogMock.Object,
                Mock.Of<IThemeService>(),
                Mock.Of<IBugReportService>(),
                Mock.Of<IUserFeedbackService>(),
                new ControllerFactory(),
                Mock.Of<IExportService>(),
                Mock.Of<IFileImportExportService>(),
                Mock.Of<IHardwarePreviewService>(),
                spMock.Object,
                Mock.Of<IUpdateService>(),
                winManagerMock.Object);

            shell.NewAssetPackCommand.Execute(null);

            Assert.True(shell.ExportFlipperMenuCommand.CanExecute(null));

            dialogMock.Setup(d => d.ShowOpenFolderDialog(It.IsAny<string>()))
                .Returns(_tempDir);

            shell.ExportFlipperMenuCommand.Execute(null);

            dialogMock.Verify(d => d.ShowOpenFolderDialog(It.IsAny<string>()), Times.Once);
            exportServiceMock.Verify(e => e.ExportAssetPack(
                It.IsAny<IReadOnlyList<(SpriteState, FlipperManifestEntry, FlipperExportSettings)>>(),
                _tempDir,
                true), Times.Once);
        }

        #endregion

        #region Adversarial Path Traversal & Name Sanitization

        [Theory]
        [InlineData("../../etc/evil", "etc_evil")]
        [InlineData("..\\..\\windows\\system32", "windows_system32")]
        [InlineData("anim:test*folder?123", "anim_test_folder_123")]
        [InlineData("///test///", "test")]
        public void ExportAssetPack_AdversarialAnimationNames_SanitizesAndConfinesToTargetFolder(string evilName, string expectedFolder)
        {
            string outDir = Path.Combine(_tempDir, "SanitizationTest_" + Guid.NewGuid().ToString("N"));
            var sprite = new SpriteState(128, 64);
            var entry = new FlipperManifestEntry { Name = evilName, MinLevel = 1, MaxLevel = 30, Weight = 1 };
            var settings = new FlipperExportSettings(outDir, evilName, 5, 1, 0);

            _service.ExportAssetPack([(sprite, entry, settings)], outDir, isMomentum: true);

            string expectedSubdir = Path.Combine(outDir, "Anims", expectedFolder);
            Assert.True(Directory.Exists(expectedSubdir));
            Assert.True(File.Exists(Path.Combine(expectedSubdir, "meta.txt")));
            Assert.True(File.Exists(Path.Combine(expectedSubdir, "frame_0.bm")));

            // Ensure no files leaked outside outDir
            string outsideDir = Path.GetFullPath(Path.Combine(outDir, ".."));
            Assert.False(File.Exists(Path.Combine(outsideDir, "meta.txt")));
            Assert.False(File.Exists(Path.Combine(outsideDir, "evil", "meta.txt")));
        }

        #endregion

        #region Round-Trip Ingestion & Export Fidelity Tests

        [Fact]
        public void ExportAssetPack_MomentumFormat_RoundTripsThroughFlipperImportServiceWithFullFidelity()
        {
            string packDir = Path.Combine(_tempDir, "RoundTripMomentum");
            var importService = new FlipperImportService();

            var sprite1 = new SpriteState(128, 64);
            // Draw pixel pattern in frame 0
            sprite1.ActiveLayerPixels[0] = true;
            sprite1.ActiveLayerPixels[128 * 63 + 127] = true;
            var sprite2 = new SpriteState(128, 64);
            sprite2.Frames.Add(new FrameState { LayerPixels = [new MonochromePixelBuffer(new bool[128 * 64])] });

            var entry1 = new FlipperManifestEntry { Name = "AlphaAnim", MinLevel = 1, MaxLevel = 10, MinButthurt = 0, MaxButthurt = 5, Weight = 2 };
            var entry2 = new FlipperManifestEntry { Name = "BetaAnim", MinLevel = 11, MaxLevel = 30, MinButthurt = 6, MaxButthurt = 14, Weight = 4 };

            var set1 = new FlipperExportSettings(packDir, "AlphaAnim", 10, 1, 0, 1, 10, 0, 5, 2);
            var set2 = new FlipperExportSettings(packDir, "BetaAnim", 8, 1, 1, 11, 30, 6, 14, 4);

            _service.ExportAssetPack([(sprite1, entry1, set1), (sprite2, entry2, set2)], packDir, isMomentum: true);

            // Import back via FlipperImportService
            var imported = importService.ImportAssetPack(packDir);

            Assert.NotNull(imported);
            Assert.Equal(2, imported.Count);

            var imp1 = imported.FirstOrDefault(i => i.Name == "AlphaAnim");
            Assert.Equal("AlphaAnim", imp1.Name);
            Assert.Equal(1, imp1.ManifestEntry.MinLevel);
            Assert.Equal(10, imp1.ManifestEntry.MaxLevel);
            Assert.Equal(0, imp1.ManifestEntry.MinButthurt);
            Assert.Equal(5, imp1.ManifestEntry.MaxButthurt);
            Assert.Equal(2, imp1.ManifestEntry.Weight);
            Assert.Equal(128, imp1.Sprite.Width);
            Assert.Equal(64, imp1.Sprite.Height);
            Assert.Single(imp1.Sprite.Frames);
            Assert.True(imp1.Sprite.ActiveLayerPixels[0]);
            Assert.True(imp1.Sprite.ActiveLayerPixels[128 * 63 + 127]);

            var imp2 = imported.FirstOrDefault(i => i.Name == "BetaAnim");
            Assert.Equal("BetaAnim", imp2.Name);
            Assert.Equal(11, imp2.ManifestEntry.MinLevel);
            Assert.Equal(30, imp2.ManifestEntry.MaxLevel);
            Assert.Equal(6, imp2.ManifestEntry.MinButthurt);
            Assert.Equal(14, imp2.ManifestEntry.MaxButthurt);
            Assert.Equal(4, imp2.ManifestEntry.Weight);
            Assert.Equal(2, imp2.Sprite.Frames.Count);
        }

        [Fact]
        public void ExportAssetPack_StockFormat_RoundTripsThroughFlipperImportServiceWithFullFidelity()
        {
            string packDir = Path.Combine(_tempDir, "RoundTripStock");
            var importService = new FlipperImportService();

            var sprite1 = new SpriteState(128, 64);
            var sprite2 = new SpriteState(128, 64);

            var entry1 = new FlipperManifestEntry { Name = "BabyDolphin", MinLevel = 1, MaxLevel = 1, MinButthurt = 0, MaxButthurt = 4, Weight = 1 };
            var entry2 = new FlipperManifestEntry { Name = "AdultDolphin", MinLevel = 2, MaxLevel = 3, MinButthurt = 5, MaxButthurt = 14, Weight = 3 };

            var set1 = new FlipperExportSettings(packDir, "BabyDolphin", 6, 1, 0, 1, 1, 0, 4, 1);
            var set2 = new FlipperExportSettings(packDir, "AdultDolphin", 6, 1, 0, 2, 3, 5, 14, 3);

            _service.ExportAssetPack([(sprite1, entry1, set1), (sprite2, entry2, set2)], packDir, isMomentum: false);

            // Import back via FlipperImportService
            var imported = importService.ImportAssetPack(packDir);

            Assert.NotNull(imported);
            Assert.Equal(2, imported.Count);

            var imp1 = imported.FirstOrDefault(i => i.Name == "BabyDolphin");
            Assert.Equal(1, imp1.ManifestEntry.MinLevel);
            Assert.Equal(1, imp1.ManifestEntry.MaxLevel);

            var imp2 = imported.FirstOrDefault(i => i.Name == "AdultDolphin");
            Assert.Equal(2, imp2.ManifestEntry.MinLevel);
            Assert.Equal(3, imp2.ManifestEntry.MaxLevel);
        }

        #endregion

        #region Multi-Tab Live Sync During Export Tests

        [Fact]
        public void BuildExportAnimationList_WithMultipleTabsAndUncachedEntries_CombinesLiveAndDefaultSprites()
        {
            var tabServiceMock = new Mock<IWorkspaceTabService>();

            var liveTab1 = new SpriteState(128, 64);
            liveTab1.Frames.Add(new FrameState { LayerPixels = [new MonochromePixelBuffer(new bool[128 * 64])] }); // 2 frames
            liveTab1.Frames.Add(new FrameState { LayerPixels = [new MonochromePixelBuffer(new bool[128 * 64])] }); // 3 frames

            // Pack with 3 entries: anim_baby, anim_teen, anim_adult
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_baby", new SpriteState(128, 64), new FlipperManifestEntry { Name = "anim_baby", MinLevel = 1, MaxLevel = 10, Weight = 1 }),
                ("anim_teen", new SpriteState(128, 64), new FlipperManifestEntry { Name = "anim_teen", MinLevel = 11, MaxLevel = 20, Weight = 1 }),
                ("anim_adult", new SpriteState(128, 64), new FlipperManifestEntry { Name = "anim_adult", MinLevel = 21, MaxLevel = 30, Weight = 1 })
            };

            var vm = new FlipperScheduleMatrixViewModel(pack, "MultiTabTest", tabService: tabServiceMock.Object);

            // Mock open workspace tabs returning modified anim_baby and an unrelated tab
            tabServiceMock.Setup(t => t.GetAllOpenSprites())
                .Returns([
                    ("anim_baby", liveTab1),
                    ("UnrelatedTab", new SpriteState(16, 16))
                ]);

            var list = vm.BuildExportAnimationList(_tempDir);

            Assert.Equal(3, list.Count);

            // anim_baby should have 3 frames from the live tab
            var baby = list.First(e => e.ManifestEntry.Name == "anim_baby");
            Assert.Equal(3, baby.Sprite.Frames.Count);

            // anim_teen and anim_adult should have 1 frame from pack
            var teen = list.First(e => e.ManifestEntry.Name == "anim_teen");
            Assert.NotNull(teen.Sprite);
            Assert.Equal(128, teen.Sprite.Width);
            Assert.Equal(64, teen.Sprite.Height);

            var adult = list.First(e => e.ManifestEntry.Name == "anim_adult");
            Assert.NotNull(adult.Sprite);
            Assert.Equal(128, adult.Sprite.Width);
            Assert.Equal(64, adult.Sprite.Height);
        }

        #endregion
    }
}
