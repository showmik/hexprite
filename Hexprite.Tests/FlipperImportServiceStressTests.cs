using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
    [Trait("Category", "Integration")]
    public class FlipperImportServiceStressTests : IDisposable
    {
        private readonly FlipperImportService _importService;
        private readonly FlipperExportService _exportService;
        private readonly string _tempDirectory;

        public FlipperImportServiceStressTests()
        {
            WpfTestHelper.EnsureApplication();
            _importService = new FlipperImportService();
            _exportService = new FlipperExportService();
            _tempDirectory = Path.Combine(Path.GetTempPath(), "HexpriteStressTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                try
                {
                    Directory.Delete(_tempDirectory, true);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }

        private static byte[] CreateMockBmFrame(int width = 128, int height = 64, byte fill = 0xAA)
        {
            int bytes = (width * height) / 8;
            byte[] data = new byte[bytes];
            Array.Fill(data, fill);
            return data;
        }

        #region 1. Non-Standard Folder Layouts Stress Tests

        [Fact]
        public void ImportAssetPack_RootLayout_LoadsAllAnimationsAndFrames()
        {
            // Layout:
            // temp/
            //   manifest.txt
            //   anim_a/
            //     meta.txt
            //     frame_0.bm
            //     frame_1.bm
            //   anim_b/
            //     meta.txt
            //     frame_0.bm
            string packDir = Path.Combine(_tempDirectory, "RootLayoutPack");
            Directory.CreateDirectory(packDir);

            string animADir = Path.Combine(packDir, "anim_a");
            string animBDir = Path.Combine(packDir, "anim_b");
            Directory.CreateDirectory(animADir);
            Directory.CreateDirectory(animBDir);

            File.WriteAllBytes(Path.Combine(animADir, "frame_0.bm"), CreateMockBmFrame());
            File.WriteAllBytes(Path.Combine(animADir, "frame_1.bm"), CreateMockBmFrame());
            File.WriteAllText(Path.Combine(animADir, "meta.txt"), "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nFrame rate: 8\n");

            File.WriteAllBytes(Path.Combine(animBDir, "frame_0.bm"), CreateMockBmFrame());
            File.WriteAllText(Path.Combine(animBDir, "meta.txt"), "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nFrame rate: 12\n");

            string manifest = "Filetype: Flipper Animation Manifest\nVersion: 1\n\nName: anim_a\nMin butthurt: 0\nMax butthurt: 5\nMin level: 1\nMax level: 10\nWeight: 2\n\nName: anim_b\nMin butthurt: 6\nMax butthurt: 14\nMin level: 11\nMax level: 30\nWeight: 4\n";
            File.WriteAllText(Path.Combine(packDir, "manifest.txt"), manifest);

            var result = _importService.ImportAssetPack(packDir);

            Assert.Equal(2, result.Count);
            var a = result.Find(x => x.Name == "anim_a");
            Assert.Equal(2, a.Sprite.Frames.Count);
            Assert.Equal(8, a.Sprite.FrameRateFps);
            Assert.Equal(10, a.ManifestEntry.MaxLevel);

            var b = result.Find(x => x.Name == "anim_b");
            Assert.Single(b.Sprite.Frames);
            Assert.Equal(12, b.Sprite.FrameRateFps);
            Assert.Equal(30, b.ManifestEntry.MaxLevel);
        }

        [Fact]
        public void ImportAssetPack_AnimsSubfolderLayout_LoadsAllAnimationsAndFrames()
        {
            // Momentum layout: pack/Anims/manifest.txt + pack/Anims/anim_x/...
            string packDir = Path.Combine(_tempDirectory, "MomentumPack");
            string animsDir = Path.Combine(packDir, "Anims");
            string animDir = Path.Combine(animsDir, "swim");
            Directory.CreateDirectory(animDir);

            File.WriteAllBytes(Path.Combine(animDir, "frame_0.bm"), CreateMockBmFrame());
            File.WriteAllText(Path.Combine(animDir, "meta.txt"), "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nFrame rate: 5\n");

            string manifest = "Filetype: Flipper Animation Manifest\nVersion: 1\n\nName: swim\nMin butthurt: 0\nMax butthurt: 14\nMin level: 1\nMax level: 30\nWeight: 1\n";
            File.WriteAllText(Path.Combine(animsDir, "manifest.txt"), manifest);

            var result = _importService.ImportAssetPack(packDir);

            Assert.Single(result);
            Assert.Equal("swim", result[0].Name);
            Assert.Single(result[0].Sprite.Frames);
        }

        [Fact]
        public void ImportAssetPack_LowercaseAnimsSubfolder_LoadsSuccessfully()
        {
            string packDir = Path.Combine(_tempDirectory, "LowercaseAnimsPack");
            string animsDir = Path.Combine(packDir, "anims");
            string animDir = Path.Combine(animsDir, "fly");
            Directory.CreateDirectory(animDir);

            File.WriteAllBytes(Path.Combine(animDir, "frame_0.bm"), CreateMockBmFrame());
            File.WriteAllText(Path.Combine(animDir, "meta.txt"), "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nFrame rate: 6\n");

            string manifest = "Filetype: Flipper Animation Manifest\nVersion: 1\n\nName: fly\nMin butthurt: 0\nMax butthurt: 14\nMin level: 1\nMax level: 30\nWeight: 1\n";
            File.WriteAllText(Path.Combine(animsDir, "manifest.txt"), manifest);

            var result = _importService.ImportAssetPack(packDir);

            Assert.Single(result);
            Assert.Equal("fly", result[0].Name);
        }

        [Fact]
        public void ImportAssetPack_StockDolphinSubfolderLayout_LoadsSuccessfully()
        {
            // Stock layout: pack/dolphin/manifest.txt + pack/dolphin/anim_x/...
            string packDir = Path.Combine(_tempDirectory, "StockPack");
            string dolphinDir = Path.Combine(packDir, "dolphin");
            string animDir = Path.Combine(dolphinDir, "idle");
            Directory.CreateDirectory(animDir);

            File.WriteAllBytes(Path.Combine(animDir, "frame_0.bm"), CreateMockBmFrame());
            File.WriteAllText(Path.Combine(animDir, "meta.txt"), "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nFrame rate: 4\n");

            string manifest = "Filetype: Flipper Animation Manifest\nVersion: 1\n\nName: idle\nMin butthurt: 0\nMax butthurt: 3\nMin level: 1\nMax level: 3\nWeight: 1\n";
            File.WriteAllText(Path.Combine(dolphinDir, "manifest.txt"), manifest);

            var result = _importService.ImportAssetPack(packDir);

            Assert.Single(result);
            Assert.Equal("idle", result[0].Name);
            Assert.Equal(3, result[0].ManifestEntry.MaxLevel);
        }

        [Fact]
        public void ImportAssetPack_DeeplyNestedLayout_DiscoversManifestAndAnimations()
        {
            string packDir = Path.Combine(_tempDirectory, "DeepRoot", "level1", "level2", "level3");
            string animsDir = Path.Combine(packDir, "Anims");
            string animDir = Path.Combine(animsDir, "nested_wave");
            Directory.CreateDirectory(animDir);

            File.WriteAllBytes(Path.Combine(animDir, "frame_0.bm"), CreateMockBmFrame());
            File.WriteAllText(Path.Combine(animDir, "meta.txt"), "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nFrame rate: 10\n");

            string manifest = "Filetype: Flipper Animation Manifest\nVersion: 1\n\nName: nested_wave\nMin butthurt: 0\nMax butthurt: 14\nMin level: 1\nMax level: 30\nWeight: 1\n";
            File.WriteAllText(Path.Combine(animsDir, "manifest.txt"), manifest);

            var result = _importService.ImportAssetPack(Path.Combine(_tempDirectory, "DeepRoot"));

            Assert.Single(result);
            Assert.Equal("nested_wave", result[0].Name);
            Assert.Equal(10, result[0].Sprite.FrameRateFps);
        }

        [Fact]
        public void ImportAssetPack_CasingDifferences_MatchesFoldersInsensitively()
        {
            // Manifest has "DolphinWalk", folder on disk is "dolphinwalk", frame is "FRAME_0.BM"
            string packDir = Path.Combine(_tempDirectory, "CasingPack");
            string animsDir = Path.Combine(packDir, "Anims");
            string animDir = Path.Combine(animsDir, "dolphinwalk");
            Directory.CreateDirectory(animDir);

            File.WriteAllBytes(Path.Combine(animDir, "FRAME_0.BM"), CreateMockBmFrame());
            File.WriteAllBytes(Path.Combine(animDir, "fRaMe_01.bM"), CreateMockBmFrame());
            File.WriteAllText(Path.Combine(animDir, "META.TXT"), "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nFrame rate: 7\n");

            string manifest = "Filetype: Flipper Animation Manifest\nVersion: 1\n\nName: DOLPHINWALK\nMin butthurt: 0\nMax butthurt: 14\nMin level: 1\nMax level: 30\nWeight: 1\n";
            File.WriteAllText(Path.Combine(animsDir, "MANIFEST.txt"), manifest);

            var result = _importService.ImportAssetPack(packDir);

            Assert.Single(result);
            Assert.Equal("DOLPHINWALK", result[0].Name);
            Assert.Equal(2, result[0].Sprite.Frames.Count);
            Assert.Equal(7, result[0].Sprite.FrameRateFps);
        }

        [Fact]
        public void ImportAssetPack_DirectSingleAnimationFolder_LoadsWithoutManifest()
        {
            string animDir = Path.Combine(_tempDirectory, "SoloAnimation");
            Directory.CreateDirectory(animDir);

            File.WriteAllBytes(Path.Combine(animDir, "frame_0.bm"), CreateMockBmFrame());
            File.WriteAllText(Path.Combine(animDir, "meta.txt"), "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nFrame rate: 5\n");

            var result = _importService.ImportAssetPack(animDir);

            Assert.Single(result);
            Assert.Equal("SoloAnimation", result[0].Name);
            Assert.Equal(1, result[0].ManifestEntry.MinLevel);
            Assert.Equal(30, result[0].ManifestEntry.MaxLevel);
        }

        [Fact]
        public void ImportAssetPack_DirectMetaTxtPath_LoadsSingleAnimation()
        {
            string animDir = Path.Combine(_tempDirectory, "MetaDirectAnim");
            Directory.CreateDirectory(animDir);

            File.WriteAllBytes(Path.Combine(animDir, "frame_0.bm"), CreateMockBmFrame());
            string metaPath = Path.Combine(animDir, "meta.txt");
            File.WriteAllText(metaPath, "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nFrame rate: 5\n");

            var result = _importService.ImportAssetPack(metaPath);

            Assert.Single(result);
            Assert.Equal("MetaDirectAnim", result[0].Name);
        }

        [Fact]
        public void ImportAssetPack_DirectManifestTxtPath_LoadsPack()
        {
            string packDir = Path.Combine(_tempDirectory, "ManifestDirectPack");
            string animDir = Path.Combine(packDir, "dance");
            Directory.CreateDirectory(animDir);

            File.WriteAllBytes(Path.Combine(animDir, "frame_0.bm"), CreateMockBmFrame());
            File.WriteAllText(Path.Combine(animDir, "meta.txt"), "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nFrame rate: 8\n");

            string manifestPath = Path.Combine(packDir, "manifest.txt");
            File.WriteAllText(manifestPath, "Filetype: Flipper Animation Manifest\nVersion: 1\n\nName: dance\nMin butthurt: 0\nMax butthurt: 14\nMin level: 1\nMax level: 30\nWeight: 1\n");

            var result = _importService.ImportAssetPack(manifestPath);

            Assert.Single(result);
            Assert.Equal("dance", result[0].Name);
        }

        [Fact]
        public void ImportAssetPack_ZipArchive_MomentumLayout_ExtractsAndLoads()
        {
            string zipPath = Path.Combine(_tempDirectory, "TestPack.zip");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var manifestEntry = zip.CreateEntry("Anims/manifest.txt");
                using (var writer = new StreamWriter(manifestEntry.Open()))
                {
                    writer.Write("Filetype: Flipper Animation Manifest\nVersion: 1\n\nName: zip_anim\nMin butthurt: 0\nMax butthurt: 14\nMin level: 1\nMax level: 30\nWeight: 1\n");
                }

                var metaEntry = zip.CreateEntry("Anims/zip_anim/meta.txt");
                using (var writer = new StreamWriter(metaEntry.Open()))
                {
                    writer.Write("Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nFrame rate: 5\n");
                }

                var frameEntry = zip.CreateEntry("Anims/zip_anim/frame_0.bm");
                using (var stream = frameEntry.Open())
                {
                    byte[] data = CreateMockBmFrame();
                    stream.Write(data, 0, data.Length);
                }
            }

            var result = _importService.ImportAssetPack(zipPath);

            Assert.Single(result);
            Assert.Equal("zip_anim", result[0].Name);
            Assert.Single(result[0].Sprite.Frames);
        }

        #endregion

        #region 2. Malformed & Empty Files Stress Tests

        [Fact]
        public void ImportAssetPack_EmptyManifestTxt_ReturnsEmptyEntries_DoesNotThrow()
        {
            string packDir = Path.Combine(_tempDirectory, "EmptyManifestPack");
            Directory.CreateDirectory(packDir);
            File.WriteAllText(Path.Combine(packDir, "manifest.txt"), string.Empty);

            var result = _importService.ImportAssetPack(packDir);

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void ImportAnimation_EmptyMetaTxt_ReturnsDefaultSprite_DoesNotThrow()
        {
            string animDir = Path.Combine(_tempDirectory, "EmptyMetaAnim");
            Directory.CreateDirectory(animDir);
            string metaPath = Path.Combine(animDir, "meta.txt");
            File.WriteAllText(metaPath, string.Empty);

            var sprite = _importService.ImportAnimation(metaPath);

            Assert.NotNull(sprite);
            Assert.Equal(128, sprite.Width);
            Assert.Equal(64, sprite.Height);
            Assert.Equal(5, sprite.FrameRateFps);
            Assert.Single(sprite.Frames);
        }

        [Fact]
        public void ImportAnimation_MalformedMetaTxt_InvalidNumbersAndKeys_UsesFallbacks()
        {
            string animDir = Path.Combine(_tempDirectory, "MalformedMetaAnim");
            Directory.CreateDirectory(animDir);
            string metaPath = Path.Combine(animDir, "meta.txt");

            string malformedMeta = @"# Corrupt meta file with noise
UnknownHeader: True
Width: NotANumber
Height: -12345
Frame rate: 999999999999999999999999999999999999
InvalidLineWithoutColon
Passive frames: text
Active frames: text
Frames order: not integers here
";
            File.WriteAllText(metaPath, malformedMeta);

            var sprite = _importService.ImportAnimation(metaPath);

            Assert.NotNull(sprite);
            Assert.Equal(128, sprite.Width);
            Assert.Equal(64, sprite.Height);
            Assert.Equal(5, sprite.FrameRateFps);
            Assert.Single(sprite.Frames);
        }

        [Fact]
        public void ImportAnimation_FramesOrder_WithOutOfBoundsIndices_IgnoresCycleGracefully()
        {
            string animDir = Path.Combine(_tempDirectory, "OobCycleAnim");
            Directory.CreateDirectory(animDir);

            File.WriteAllBytes(Path.Combine(animDir, "frame_0.bm"), CreateMockBmFrame());
            File.WriteAllBytes(Path.Combine(animDir, "frame_1.bm"), CreateMockBmFrame());

            // Order references index 99 which does not exist in 2-frame sprite
            string meta = @"Filetype: Flipper Animation
Version: 1
Width: 128
Height: 64
Passive frames: 2
Active frames: 1
Frames order: 0 1 99
Active cycles: 1
Frame rate: 5
Duration: 3600
Active cooldown: 0
Bubble slots: 0
";
            string metaPath = Path.Combine(animDir, "meta.txt");
            File.WriteAllText(metaPath, meta);

            var sprite = _importService.ImportAnimation(metaPath);

            Assert.Equal(2, sprite.Frames.Count);
            // Cycle should be null because indices were out of bounds
            Assert.Null(sprite.FlipperCycle);
        }

        [Fact]
        public void ImportAnimation_FramesOrder_NegativeAndNonNumericTokens_HandledSafely()
        {
            string animDir = Path.Combine(_tempDirectory, "NegativeCycleAnim");
            Directory.CreateDirectory(animDir);

            File.WriteAllBytes(Path.Combine(animDir, "frame_0.bm"), CreateMockBmFrame());

            string meta = @"Filetype: Flipper Animation
Version: 1
Width: 128
Height: 64
Passive frames: 1
Active frames: 0
Frames order: -1 abc 0
Active cycles: 1
Frame rate: 5
";
            string metaPath = Path.Combine(animDir, "meta.txt");
            File.WriteAllText(metaPath, meta);

            var sprite = _importService.ImportAnimation(metaPath);

            Assert.Single(sprite.Frames);
            Assert.Null(sprite.FlipperCycle);
        }

        [Fact]
        public void ImportAnimation_ZeroByteBmFile_ReturnsBlankPlaceholderFrame_DoesNotThrow()
        {
            string animDir = Path.Combine(_tempDirectory, "ZeroByteFrameAnim");
            Directory.CreateDirectory(animDir);

            File.WriteAllBytes(Path.Combine(animDir, "frame_0.bm"), []);
            string metaPath = Path.Combine(animDir, "meta.txt");
            File.WriteAllText(metaPath, "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nFrame rate: 5\n");

            var sprite = _importService.ImportAnimation(metaPath);

            Assert.Single(sprite.Frames);
            Assert.Equal("Frame 1", sprite.Frames[0].Name);
            bool[] pixels = sprite.Frames[0].LayerPixels[0].GetMonochromeData();
            Assert.All(pixels, p => Assert.False(p));
        }

        [Fact]
        public void ImportAnimation_TruncatedBmFiles_1To3Bytes_ReturnsBlankPlaceholderFrame()
        {
            string animDir = Path.Combine(_tempDirectory, "TruncatedBmAnim");
            Directory.CreateDirectory(animDir);

            File.WriteAllBytes(Path.Combine(animDir, "frame_0.bm"), [0x01]);
            File.WriteAllBytes(Path.Combine(animDir, "frame_1.bm"), [0x01, 0x00]);
            File.WriteAllBytes(Path.Combine(animDir, "frame_2.bm"), [0x01, 0x00, 0x50]);

            string metaPath = Path.Combine(animDir, "meta.txt");
            File.WriteAllText(metaPath, "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nFrame rate: 5\n");

            var sprite = _importService.ImportAnimation(metaPath);

            Assert.Equal(3, sprite.Frames.Count);
            Assert.Equal("Frame 1", sprite.Frames[0].Name);
            Assert.Equal("Frame 2", sprite.Frames[1].Name);
            Assert.Equal("Frame 3", sprite.Frames[2].Name);
        }

        [Fact]
        public void ImportAnimation_CorruptedHeatshrinkPayload_ReturnsBlankPlaceholderFrame()
        {
            string animDir = Path.Combine(_tempDirectory, "CorruptedHeatshrinkAnim");
            Directory.CreateDirectory(animDir);

            // Valid 4-byte header: 0x01 (compressed), 0x00, 0x000A (10 bytes length)
            // Followed by 10 garbage bytes that cannot decompress with heatshrink
            byte[] corruptPayload = [0x01, 0x00, 0x0A, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0xAA, 0x55, 0x12, 0x34];
            File.WriteAllBytes(Path.Combine(animDir, "frame_0.bm"), corruptPayload);

            string metaPath = Path.Combine(animDir, "meta.txt");
            File.WriteAllText(metaPath, "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nFrame rate: 5\n");

            var sprite = _importService.ImportAnimation(metaPath);

            Assert.Single(sprite.Frames);
            Assert.Equal("Frame 1", sprite.Frames[0].Name);
            bool[] pixels = sprite.Frames[0].LayerPixels[0].GetMonochromeData();
            Assert.All(pixels, p => Assert.False(p));
        }

        [Fact]
        public void ImportAnimation_EmptyFolderWithMetaTxt_CreatesSingleBlankFrame()
        {
            string animDir = Path.Combine(_tempDirectory, "NoBmFilesAnim");
            Directory.CreateDirectory(animDir);

            string metaPath = Path.Combine(animDir, "meta.txt");
            File.WriteAllText(metaPath, "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nFrame rate: 5\n");

            var sprite = _importService.ImportAnimation(metaPath);

            Assert.Single(sprite.Frames);
            Assert.Equal("Frame 1", sprite.Frames[0].Name);
            Assert.Equal(128 * 64, sprite.Frames[0].LayerPixels[0].GetMonochromeData().Length);
        }

        [Fact]
        public void ImportAssetPack_ManifestWithMissingAnimationFolders_CreatesPlaceholdersForAll()
        {
            string packDir = Path.Combine(_tempDirectory, "GhostAnimationsPack");
            Directory.CreateDirectory(packDir);

            string manifest = @"Filetype: Flipper Animation Manifest
Version: 1

Name: Ghost1
Min butthurt: 0
Max butthurt: 3
Min level: 1
Max level: 5
Weight: 1

Name: Ghost2
Min butthurt: 4
Max butthurt: 8
Min level: 6
Max level: 15
Weight: 2

Name: Ghost3
Min butthurt: 9
Max butthurt: 14
Min level: 16
Max level: 30
Weight: 3
";
            File.WriteAllText(Path.Combine(packDir, "manifest.txt"), manifest);

            var result = _importService.ImportAssetPack(packDir);

            Assert.Equal(3, result.Count);
            foreach (var item in result)
            {
                Assert.NotNull(item.Sprite);
                Assert.Equal(128, item.Sprite.Width);
                Assert.Equal(64, item.Sprite.Height);
                Assert.Single(item.Sprite.Frames);
            }
        }

        #endregion

        #region 3. ShellViewModel & AssetPackViewModel State Preservation Tests

        private static ShellViewModel CreateTestShell()
        {
            var codeGenMock = new Mock<ICodeGeneratorService>();
            var drawingMock = new Mock<IDrawingService>();
            var clipboardMock = new Mock<IClipboardService>();
            var pixelClipboardMock = new Mock<IPixelClipboardService>();
            var dialogMock = new Mock<IDialogService>();
            var themeMock = new Mock<IThemeService>();
            var bugReportMock = new Mock<IBugReportService>();
            var feedbackMock = new Mock<IUserFeedbackService>();
            var controllerFactory = new ControllerFactory();
            var exportMock = new Mock<IExportService>();
            var importExportMock = new Mock<IFileImportExportService>();
            var hardwarePreviewMock = new Mock<IHardwarePreviewService>();
            var winManagerMock = new Mock<IFlipperWindowManager>();
            var updateMock = new Mock<IUpdateService>();

            var autosaveMock = new Mock<IAutosaveService>();
            var spMock = new Mock<IServiceProvider>();
            spMock.Setup(sp => sp.GetService(typeof(IAutosaveService))).Returns(autosaveMock.Object);
            spMock.Setup(sp => sp.GetService(typeof(IFlipperWindowManager))).Returns(winManagerMock.Object);

            return new ShellViewModel(
                codeGenMock.Object,
                drawingMock.Object,
                clipboardMock.Object,
                pixelClipboardMock.Object,
                dialogMock.Object,
                themeMock.Object,
                bugReportMock.Object,
                feedbackMock.Object,
                controllerFactory,
                exportMock.Object,
                importExportMock.Object,
                hardwarePreviewMock.Object,
                spMock.Object,
                updateMock.Object,
                winManagerMock.Object);
        }

        [Fact]
        public void OpenAssetPackInTab_WithMultiFrameSprites_PreservesSpriteFramesInViewModel()
        {
            // Arrange - Build 2 animated sprites with unique pixel markers
            var swimSprite = new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome, FrameRateFps = 6 };
            swimSprite.Frames.Clear();
            var swimF1 = new FrameState { Name = "Frame 1" };
            bool[] swimPix1 = new bool[128 * 64];
            swimPix1[42] = true;
            swimF1.LayerPixels.Add(new MonochromePixelBuffer(swimPix1));
            swimSprite.Frames.Add(swimF1);

            var swimF2 = new FrameState { Name = "Frame 2" };
            bool[] swimPix2 = new bool[128 * 64];
            swimPix2[99] = true;
            swimF2.LayerPixels.Add(new MonochromePixelBuffer(swimPix2));
            swimSprite.Frames.Add(swimF2);

            var swimEntry = new FlipperManifestEntry { Name = "DolphinSwim", MinLevel = 1, MaxLevel = 10, MinButthurt = 0, MaxButthurt = 5, Weight = 3 };

            var jumpSprite = new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome, FrameRateFps = 12 };
            jumpSprite.Frames.Clear();
            var jumpF1 = new FrameState { Name = "Frame 1" };
            bool[] jumpPix1 = new bool[128 * 64];
            jumpPix1[500] = true;
            jumpF1.LayerPixels.Add(new MonochromePixelBuffer(jumpPix1));
            jumpSprite.Frames.Add(jumpF1);

            var jumpEntry = new FlipperManifestEntry { Name = "DolphinJump", MinLevel = 11, MaxLevel = 30, MinButthurt = 6, MaxButthurt = 14, Weight = 5 };

            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("DolphinSwim", swimSprite, swimEntry),
                ("DolphinJump", jumpSprite, jumpEntry)
            };

            var shell = CreateTestShell();

            // Act
            shell.OpenAssetPackInTab(pack, "MarinePack");

            // Assert
            Assert.Single(shell.OpenDocuments);
            Assert.NotNull(shell.ActiveDocument);
            var apvm = Assert.IsType<AssetPackViewModel>(shell.ActiveDocument);
            Assert.Equal("MarinePack (Pack)", apvm.Title);
            Assert.Equal(2, apvm.MatrixViewModel.Entries.Count);
            Assert.NotNull(apvm.MatrixViewModel.ImportedPack);
            Assert.Equal(2, apvm.MatrixViewModel.ImportedPack.Count);

            // Verify Swim Entry
            apvm.MatrixViewModel.SelectedEntry = apvm.MatrixViewModel.Entries[0];
            Assert.Equal("DolphinSwim", apvm.MatrixViewModel.SelectedName);
            Assert.Equal("Frame 1 / 2", apvm.MatrixViewModel.PreviewFrameCountText);
            Assert.Equal("128 × 64", apvm.MatrixViewModel.PreviewDimensionsText);

            // Verify Jump Entry
            apvm.MatrixViewModel.SelectedEntry = apvm.MatrixViewModel.Entries[1];
            Assert.Equal("DolphinJump", apvm.MatrixViewModel.SelectedName);
            Assert.Equal("Frame 1 / 1", apvm.MatrixViewModel.PreviewFrameCountText);
        }

        [Fact]
        public void OpenAssetPackInTab_DuplicateName_ActivatesExistingTab()
        {
            var shell = CreateTestShell();
            var entry = new FlipperManifestEntry { Name = "idle", MinLevel = 1, MaxLevel = 3, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("idle", new SpriteState(128, 64), entry)
            };

            shell.OpenAssetPackInTab(pack, "UniquePack");
            Assert.Single(shell.OpenDocuments);

            // Open another document tab
            shell.NewDocumentCommand.Execute("128x64");
            Assert.Equal(2, shell.OpenDocuments.Count);
            Assert.IsType<MainViewModel>(shell.ActiveDocument);

            // Calling OpenAssetPackInTab with matching name and pack=null reactivates existing tab
            shell.OpenAssetPackInTab(null, "UniquePack");

            Assert.Equal(2, shell.OpenDocuments.Count);
            Assert.IsType<AssetPackViewModel>(shell.ActiveDocument);
            Assert.Equal("UniquePack (Pack)", shell.ActiveDocument.Title);
        }

        [Fact]
        public void OpenAssetPackInTab_StockModeDetection_SetsIsStockModeCorrectly()
        {
            var shell = CreateTestShell();

            // All entries <= level 3 -> Stock Mode
            var stockPack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("stock_1", new SpriteState(128, 64), new FlipperManifestEntry { Name = "stock_1", MinLevel = 1, MaxLevel = 3, MinButthurt = 0, MaxButthurt = 14, Weight = 1 }),
                ("stock_2", new SpriteState(128, 64), new FlipperManifestEntry { Name = "stock_2", MinLevel = 2, MaxLevel = 3, MinButthurt = 0, MaxButthurt = 5, Weight = 1 })
            };

            shell.OpenAssetPackInTab(stockPack, "StockPack");
            var stockVm = Assert.IsType<AssetPackViewModel>(shell.ActiveDocument);
            Assert.True(stockVm.MatrixViewModel.IsStockMode);
            Assert.Equal("Stock Mode (L1-3)", stockVm.MatrixViewModel.ModeBadgeText);

            // Entry with level > 3 -> Momentum Mode
            var momentumPack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("mom_1", new SpriteState(128, 64), new FlipperManifestEntry { Name = "mom_1", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 })
            };

            shell.OpenAssetPackInTab(momentumPack, "MomentumPack");
            var momVm = Assert.IsType<AssetPackViewModel>(shell.ActiveDocument);
            Assert.False(momVm.MatrixViewModel.IsStockMode);
            Assert.True(momVm.MatrixViewModel.IsMomentumMode);
            Assert.Equal("Extended Mode (L1-30)", momVm.MatrixViewModel.ModeBadgeText);
        }

        [Fact]
        public void ImportAssetPack_ThrowsArgumentException_OnNullOrWhitespace()
        {
            Assert.Throws<ArgumentException>(() => _importService.ImportAssetPack(null!));
            Assert.Throws<ArgumentException>(() => _importService.ImportAssetPack(string.Empty));
            Assert.Throws<ArgumentException>(() => _importService.ImportAssetPack("   "));
        }

        [Fact]
        public void ImportAssetPack_ThrowsDirectoryNotFoundException_OnNonExistentPath()
        {
            string nonExistent = Path.Combine(_tempDirectory, "DoesNotExist_" + Guid.NewGuid().ToString("N"));
            Assert.Throws<DirectoryNotFoundException>(() => _importService.ImportAssetPack(nonExistent));
        }

        [Fact]
        public void ImportAssetPack_ThrowsFileNotFoundException_WhenNoMetaOrManifestInDirectory()
        {
            string emptyDir = Path.Combine(_tempDirectory, "TotallyEmptyPackDir");
            Directory.CreateDirectory(emptyDir);

            Assert.Throws<FileNotFoundException>(() => _importService.ImportAssetPack(emptyDir));
        }

        [Fact]
        public void ImportAnimation_ThrowsFileNotFoundException_OnMissingFile()
        {
            string nonExistentMeta = Path.Combine(_tempDirectory, "GhostMeta", "meta.txt");
            Assert.Throws<FileNotFoundException>(() => _importService.ImportAnimation(nonExistentMeta));
        }

        [Fact]
        public void ImportAssetPack_MultipleManifestsInSubfolders_PrefersAnimsManifest()
        {
            string packDir = Path.Combine(_tempDirectory, "MultiManifestPack");
            string rootAnimDir = Path.Combine(packDir, "root_anim");
            string animsAnimDir = Path.Combine(packDir, "Anims", "anims_anim");
            Directory.CreateDirectory(rootAnimDir);
            Directory.CreateDirectory(animsAnimDir);

            File.WriteAllBytes(Path.Combine(rootAnimDir, "frame_0.bm"), CreateMockBmFrame());
            File.WriteAllText(Path.Combine(rootAnimDir, "meta.txt"), "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nFrame rate: 5\n");

            File.WriteAllBytes(Path.Combine(animsAnimDir, "frame_0.bm"), CreateMockBmFrame());
            File.WriteAllText(Path.Combine(animsAnimDir, "meta.txt"), "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nFrame rate: 5\n");

            File.WriteAllText(Path.Combine(packDir, "manifest.txt"), "Filetype: Flipper Animation Manifest\nVersion: 1\n\nName: root_anim\nMin butthurt: 0\nMax butthurt: 14\nMin level: 1\nMax level: 30\nWeight: 1\n");
            File.WriteAllText(Path.Combine(packDir, "Anims", "manifest.txt"), "Filetype: Flipper Animation Manifest\nVersion: 1\n\nName: anims_anim\nMin butthurt: 0\nMax butthurt: 14\nMin level: 1\nMax level: 30\nWeight: 1\n");

            var result = _importService.ImportAssetPack(packDir);

            Assert.Single(result);
            Assert.Equal("anims_anim", result[0].Name);
        }

        #endregion
    }
}
