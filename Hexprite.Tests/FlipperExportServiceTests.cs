using Xunit;
using Hexprite.Core;
using Hexprite.Services;
using System;
using System.IO;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FlipperExportServiceTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly FlipperExportService _service;

        public FlipperExportServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "HexpriteFlipperTests_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_tempDir);
            _service = new FlipperExportService();
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        [Fact]
        public void ExportAnimation_GeneratesCorrectMetaFile()
        {
            var sprite = new SpriteState(128, 64);
            var frame2 = new FrameState { LayerPixels = new System.Collections.Generic.List<IPixelBuffer> { new MonochromePixelBuffer(new bool[128 * 64]) } };
            sprite.Frames.Add(frame2); // Now has 2 frames

            var settings = new FlipperExportSettings(_tempDir, "TestAnim", 10, 1, 1);
            
            _service.ExportAnimation(sprite, settings);

            string metaPath = Path.Combine(_tempDir, "TestAnim", "meta.txt");
            Assert.True(File.Exists(metaPath));

            string metaContent = File.ReadAllText(metaPath);
            Assert.Contains("Width: 128", metaContent);
            Assert.Contains("Height: 64", metaContent);
            Assert.Contains("Passive frames: 1", metaContent);
            Assert.Contains("Active frames: 1", metaContent);
            Assert.Contains("Frames order: 0 1", metaContent);
            Assert.Contains("Frame rate: 10", metaContent);
        }

        [Fact]
        public void ExportAnimation_GeneratesCorrectBitmapData()
        {
            var sprite = new SpriteState(4, 8);
            var pixels = sprite.ActiveLayerPixels;
            // Draw a diagonal line: (0,0), (1,1), (2,2), (3,3)
            pixels[0 * 4 + 0] = true;
            pixels[1 * 4 + 1] = true;
            pixels[2 * 4 + 2] = true;
            pixels[3 * 4 + 3] = true;

            var settings = new FlipperExportSettings(_tempDir, "TestAnimData", 5, 1, 0);
            
            _service.ExportAnimation(sprite, settings);

            string framePath = Path.Combine(_tempDir, "TestAnimData", "frame_0.bm");
            Assert.True(File.Exists(framePath));

            byte[] bmData = File.ReadAllBytes(framePath);
            
            // Expected length: rowBytes (1) * height (8) = 8 bytes
            Assert.Equal(8, bmData.Length);
            
            // Row 0: x=0 is true, bit 0 = 1
            Assert.Equal(1, bmData[0]);
            // Row 1: x=1 is true, bit 1 = 2
            Assert.Equal(2, bmData[1]);
            // Row 2: x=2 is true, bit 2 = 4
            Assert.Equal(4, bmData[2]);
            // Row 3: x=3 is true, bit 3 = 8
            Assert.Equal(8, bmData[3]);
            // Rows 4-7 are empty
            Assert.Equal(0, bmData[4]);
            Assert.Equal(0, bmData[7]);
        }
        [Fact]
        public void ExportImage_GeneratesCorrectBitmapData()
        {
            var sprite = new SpriteState(4, 8);
            var pixels = sprite.ActiveLayerPixels;
            // Draw a diagonal line: (0,0), (1,1), (2,2), (3,3)
            pixels[0 * 4 + 0] = true;
            pixels[1 * 4 + 1] = true;
            pixels[2 * 4 + 2] = true;
            pixels[3 * 4 + 3] = true;

            string targetPath = Path.Combine(_tempDir, "TestImage.bm");
            _service.ExportImage(sprite, 0, targetPath);

            Assert.True(File.Exists(targetPath));

            byte[] bmData = File.ReadAllBytes(targetPath);
            
            // Expected length: rowBytes (1) * height (8) = 8 bytes
            Assert.Equal(8, bmData.Length);
            
            // Row 0: x=0 is true, bit 0 = 1
            Assert.Equal(1, bmData[0]);
            // Row 1: x=1 is true, bit 1 = 2
            Assert.Equal(2, bmData[1]);
            // Row 2: x=2 is true, bit 2 = 4
            Assert.Equal(4, bmData[2]);
            // Row 3: x=3 is true, bit 3 = 8
            Assert.Equal(8, bmData[3]);
            // Rows 4-7 are empty
            Assert.Equal(0, bmData[4]);
            Assert.Equal(0, bmData[7]);
        }

        [Fact]
        public void ExportAnimation_MomentumAssetPack_GeneratesAnimsAndManifest()
        {
            var sprite = new SpriteState(128, 64);
            var settings = new FlipperExportSettings(
                _tempDir,
                "CyberCat",
                8,
                1,
                0,
                minLevel: 1,
                maxLevel: 30,
                minButthurt: 0,
                maxButthurt: 14,
                weight: 5,
                createManifestTxt: true,
                targetMode: FlipperExportTargetMode.MomentumAssetPack
            );

            _service.ExportAnimation(sprite, settings);

            string manifestPath = Path.Combine(_tempDir, "Anims", "manifest.txt");
            string metaPath = Path.Combine(_tempDir, "Anims", "CyberCat", "meta.txt");
            string iconsDir = Path.Combine(_tempDir, "Icons");

            Assert.True(File.Exists(manifestPath));
            Assert.True(File.Exists(metaPath));
            Assert.True(Directory.Exists(iconsDir));

            string manifestContent = File.ReadAllText(manifestPath);
            Assert.Contains("Name: CyberCat", manifestContent);
            Assert.Contains("Min level: 1", manifestContent);
            Assert.Contains("Max level: 30", manifestContent);
            Assert.Contains("Weight: 5", manifestContent);
        }

        [Fact]
        public void ExportAssetPack_BatchExportsAllAnimations()
        {
            var anim1 = new SpriteState(128, 64);
            var anim2 = new SpriteState(128, 64);

            var entry1 = new FlipperManifestEntry { Name = "AnimOne", MinLevel = 1, MaxLevel = 10, MinButthurt = 0, MaxButthurt = 5, Weight = 1 };
            var entry2 = new FlipperManifestEntry { Name = "AnimTwo", MinLevel = 11, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 3 };

            var set1 = new FlipperExportSettings(_tempDir, "AnimOne", 5, 1, 0, minLevel: 1, maxLevel: 10, minButthurt: 0, maxButthurt: 5, weight: 1);
            var set2 = new FlipperExportSettings(_tempDir, "AnimTwo", 8, 1, 0, minLevel: 11, maxLevel: 30, minButthurt: 0, maxButthurt: 14, weight: 3);

            var items = new System.Collections.Generic.List<(SpriteState Sprite, FlipperManifestEntry ManifestEntry, FlipperExportSettings Settings)>
            {
                (anim1, entry1, set1),
                (anim2, entry2, set2)
            };

            _service.ExportAssetPack(items, _tempDir, isMomentum: true);

            string manifestPath = Path.Combine(_tempDir, "Anims", "manifest.txt");
            Assert.True(File.Exists(manifestPath));

            string manifestContent = File.ReadAllText(manifestPath);
            Assert.Contains("Name: AnimOne", manifestContent);
            Assert.Contains("Name: AnimTwo", manifestContent);
            Assert.True(File.Exists(Path.Combine(_tempDir, "Anims", "AnimOne", "meta.txt")));
            Assert.True(File.Exists(Path.Combine(_tempDir, "Anims", "AnimTwo", "meta.txt")));
        }

        [Fact]
        public void ExportAnimation_WithValidCycleAndBubbleSlots_WritesAllFieldsAndBubbles()
        {
            var sprite = new SpriteState(128, 64);
            sprite.Frames.Clear();
            sprite.Frames.Add(new FrameState { LayerPixels = [new MonochromePixelBuffer(new bool[128 * 64])] });
            sprite.Frames.Add(new FrameState { LayerPixels = [new MonochromePixelBuffer(new bool[128 * 64])] });

            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                PassiveFrameCount = 1,
                ActiveFrameCount = 1,
                ActiveCycles = 2,
                Duration = 2400,
                ActiveCooldown = 5,
                BubbleSlots = 1,
                FramesOrder = [0, 1],
                SpeechBubbles =
                [
                    new FlipperSpeechBubble
                    {
                        SlotIndex = 0,
                        StartFrame = 1,
                        EndFrame = 1,
                        Text = "Dolphin Hello",
                        X = 10,
                        Y = 5,
                        AlignH = "Center",
                        AlignV = "Bottom"
                    }
                ]
            };

            var settings = new FlipperExportSettings(_tempDir, "BubbleAnim", 10, 1, 1);
            _service.ExportAnimation(sprite, settings);

            string metaPath = Path.Combine(_tempDir, "BubbleAnim", "meta.txt");
            Assert.True(File.Exists(metaPath));

            string metaContent = File.ReadAllText(metaPath);
            Assert.Contains("Duration: 2400", metaContent);
            Assert.Contains("Active cooldown: 5", metaContent);
            Assert.Contains("Bubble slots: 1", metaContent);
            Assert.Contains("Slot: 0", metaContent);
            Assert.Contains("Text: Dolphin Hello", metaContent);
            Assert.Contains("X: 10", metaContent);
            Assert.Contains("Y: 5", metaContent);
        }

        [Fact]
        public void ExportAnimation_WithoutCycle_WritesDefaultDurationCooldownAndBubbleSlots()
        {
            var sprite = new SpriteState(128, 64);
            var settings = new FlipperExportSettings(_tempDir, "NoCycleAnim", 5, 1, 0);

            _service.ExportAnimation(sprite, settings);

            string metaPath = Path.Combine(_tempDir, "NoCycleAnim", "meta.txt");
            Assert.True(File.Exists(metaPath));

            string metaContent = File.ReadAllText(metaPath);
            Assert.Contains("Duration: 3600", metaContent);
            Assert.Contains("Active cooldown: 0", metaContent);
            Assert.Contains("Bubble slots: 0", metaContent);
        }

        [Fact]
        public void ExportAnimation_MomentumAssetPack_Generates10x10IconInIconsDir()
        {
            var sprite = new SpriteState(128, 64);
            var pixels = sprite.ActiveLayerPixels;
            // Draw a small 4x4 square
            pixels[0] = true;
            pixels[1] = true;
            pixels[128] = true;
            pixels[129] = true;

            var settings = new FlipperExportSettings(
                _tempDir,
                "IconTestPack",
                5,
                1,
                0,
                minLevel: 1,
                maxLevel: 30,
                minButthurt: 0,
                maxButthurt: 14,
                weight: 1,
                createManifestTxt: true,
                targetMode: FlipperExportTargetMode.MomentumAssetPack);

            _service.ExportAnimation(sprite, settings);

            string iconPath = Path.Combine(_tempDir, "Icons", "I_IconTestPack_10x10.bm");
            Assert.True(File.Exists(iconPath));

            byte[] iconBytes = File.ReadAllBytes(iconPath);
            Assert.True(iconBytes.Length > 0);
        }

        [Theory]
        [InlineData("My Cool Anim #1", "My_Cool_Anim_1")]
        [InlineData("  dolphin.test+anim  ", "dolphin_test_anim")]
        [InlineData("Valid-Name_123", "Valid-Name_123")]
        [InlineData("   ", "Animation")]
        public void SanitizeAnimationName_CleansInputSafely(string input, string expected)
        {
            string actual = FlipperExportService.SanitizeAnimationName(input);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void ExportAssetPackZip_GeneratesValidZipArchive()
        {
            var anim = new SpriteState(128, 64);
            var entry = new FlipperManifestEntry { Name = "ZipAnim", MinLevel = 1, MaxLevel = 30, Weight = 1 };
            var set = new FlipperExportSettings(_tempDir, "ZipAnim", 5, 1, 0);

            string zipPath = Path.Combine(_tempDir, "MyAssetPack.zip");
            _service.ExportAssetPackZip([(anim, entry, set)], zipPath, isMomentum: true);

            Assert.True(File.Exists(zipPath));

            string extractDir = Path.Combine(_tempDir, "ZipExtracted");
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir);

            Assert.True(File.Exists(Path.Combine(extractDir, "Anims", "manifest.txt")));
            Assert.True(File.Exists(Path.Combine(extractDir, "Anims", "ZipAnim", "meta.txt")));
            Assert.True(File.Exists(Path.Combine(extractDir, "Icons", "I_ZipAnim_10x10.bm")));
        }

        [Fact]
        public void GenerateDeploymentFiles_MomentumAssetPack_ProducesInMemoryFilesWithoutDiskIo()
        {
            var sprite = new SpriteState(128, 64);
            var frame2 = new FrameState { LayerPixels = [new MonochromePixelBuffer(new bool[128 * 64])] };
            sprite.Frames.Add(frame2);

            var settings = new FlipperExportSettings(
                "",
                "CyberCat",
                8,
                1,
                1,
                minLevel: 1,
                maxLevel: 30,
                minButthurt: 0,
                maxButthurt: 14,
                weight: 5,
                createManifestTxt: true,
                targetMode: FlipperExportTargetMode.MomentumAssetPack
            );

            var files = _service.GenerateDeploymentFiles(sprite, settings);

            Assert.NotNull(files);
            Assert.Equal(5, files.Count); // meta.txt, frame_0.bm, frame_1.bm, manifest.txt, I_CyberCat_10x10.bm

            Assert.Contains(files, f => f.RelativePath == "Anims/CyberCat/meta.txt" && f.Data.Length > 0);
            Assert.Contains(files, f => f.RelativePath == "Anims/CyberCat/frame_0.bm" && f.Data.Length > 0);
            Assert.Contains(files, f => f.RelativePath == "Anims/CyberCat/frame_1.bm" && f.Data.Length > 0);
            Assert.Contains(files, f => f.RelativePath == "Anims/manifest.txt" && f.Data.Length > 0);
            Assert.Contains(files, f => f.RelativePath == "Icons/I_CyberCat_10x10.bm" && f.Data.Length > 0);
        }

        [Fact]
        public void GenerateDeploymentFiles_StockDolphin_ProducesDolphinPaths()
        {
            var sprite = new SpriteState(128, 64);
            var settings = new FlipperExportSettings(
                "",
                "StockAnim",
                5,
                1,
                0,
                minLevel: 1,
                maxLevel: 3,
                minButthurt: 0,
                maxButthurt: 14,
                weight: 1,
                createManifestTxt: true,
                targetMode: FlipperExportTargetMode.StockDolphin
            );

            var files = _service.GenerateDeploymentFiles(sprite, settings);

            Assert.NotNull(files);
            Assert.Contains(files, f => f.RelativePath == "dolphin/StockAnim/meta.txt");
            Assert.Contains(files, f => f.RelativePath == "dolphin/StockAnim/frame_0.bm");
            Assert.Contains(files, f => f.RelativePath == "dolphin/manifest.txt");
        }

        [Fact]
        public void ExportAssetPack_StockMode_ExportsDolphinDirectoryWithoutIconsFolder()
        {
            var anim = new SpriteState(128, 64);
            var entry = new FlipperManifestEntry { Name = "StockDolphinAnim", MinLevel = 1, MaxLevel = 3, Weight = 1 };
            var set = new FlipperExportSettings(_tempDir, "StockDolphinAnim", 5, 1, 0, minLevel: 1, maxLevel: 3, minButthurt: 0, maxButthurt: 14, weight: 1);

            _service.ExportAssetPack([(anim, entry, set)], _tempDir, isMomentum: false);

            string dolphinDir = Path.Combine(_tempDir, "dolphin");
            string manifestPath = Path.Combine(dolphinDir, "manifest.txt");
            string metaPath = Path.Combine(dolphinDir, "StockDolphinAnim", "meta.txt");
            string framePath = Path.Combine(dolphinDir, "StockDolphinAnim", "frame_0.bm");
            string iconsDir = Path.Combine(_tempDir, "Icons");

            Assert.True(File.Exists(manifestPath));
            Assert.True(File.Exists(metaPath));
            Assert.True(File.Exists(framePath));
            Assert.False(Directory.Exists(iconsDir)); // Stock mode must NOT generate Icons/ directory
        }

        [Fact]
        public void ExportAssetPack_EmptyAnimationsList_ThrowsInvalidOperationException()
        {
            var emptyList = new System.Collections.Generic.List<(SpriteState Sprite, FlipperManifestEntry ManifestEntry, FlipperExportSettings Settings)>();

            var ex = Assert.Throws<InvalidOperationException>(() => _service.ExportAssetPack(emptyList, _tempDir, isMomentum: true));
            Assert.Contains("FZ010", ex.Message);
        }

        [Fact]
        public void ExportAssetPack_EmptyAnimationName_ThrowsInvalidOperationException()
        {
            var anim = new SpriteState(128, 64);
            var entry = new FlipperManifestEntry { Name = "  ", MinLevel = 1, MaxLevel = 30, Weight = 1 };
            var set = new FlipperExportSettings(_tempDir, "", 5, 1, 0);

            var ex = Assert.Throws<InvalidOperationException>(() => _service.ExportAssetPack([(anim, entry, set)], _tempDir, isMomentum: true));
            Assert.Contains("FZ001", ex.Message);
        }

        [Fact]
        public void ExportAssetPack_DuplicateAnimationNames_ThrowsInvalidOperationException()
        {
            var anim1 = new SpriteState(128, 64);
            var anim2 = new SpriteState(128, 64);
            var entry1 = new FlipperManifestEntry { Name = "DupeAnim", MinLevel = 1, MaxLevel = 10, Weight = 1 };
            var entry2 = new FlipperManifestEntry { Name = "dupeanim", MinLevel = 11, MaxLevel = 30, Weight = 1 };
            var set1 = new FlipperExportSettings(_tempDir, "DupeAnim", 5, 1, 0);
            var set2 = new FlipperExportSettings(_tempDir, "dupeanim", 5, 1, 0);

            var ex = Assert.Throws<InvalidOperationException>(() => _service.ExportAssetPack([(anim1, entry1, set1), (anim2, entry2, set2)], _tempDir, isMomentum: true));
            Assert.Contains("FZ011", ex.Message);
        }

        [Fact]
        public void ExportAssetPack_SpriteWithZeroFrames_ThrowsInvalidOperationException()
        {
            var anim = new SpriteState(128, 64);
            anim.Frames.Clear(); // 0 frames
            var entry = new FlipperManifestEntry { Name = "ZeroFrameAnim", MinLevel = 1, MaxLevel = 30, Weight = 1 };
            var set = new FlipperExportSettings(_tempDir, "ZeroFrameAnim", 5, 0, 0);

            var ex = Assert.Throws<InvalidOperationException>(() => _service.ExportAssetPack([(anim, entry, set)], _tempDir, isMomentum: true));
            Assert.Contains("FZS002", ex.Message);
        }

        [Fact]
        public void ExportAssetPack_SpriteExceeding128x64Dimensions_ThrowsInvalidOperationException()
        {
            var anim = new SpriteState(256, 128); // Exceeds 128x64 hardware screen
            var entry = new FlipperManifestEntry { Name = "OversizedAnim", MinLevel = 1, MaxLevel = 30, Weight = 1 };
            var set = new FlipperExportSettings(_tempDir, "OversizedAnim", 5, 1, 0);

            var ex = Assert.Throws<InvalidOperationException>(() => _service.ExportAssetPack([(anim, entry, set)], _tempDir, isMomentum: true));
            Assert.Contains("FZM001", ex.Message);
        }

        [Fact]
        public void ExportAssetPack_InvalidFrameOrderIndex_ThrowsInvalidOperationException()
        {
            var anim = new SpriteState(128, 64); // 1 physical frame (index 0)
            anim.FlipperCycle = new FlipperAnimationCycle
            {
                FramesOrder = [0, 1, 5] // Frame 1 and 5 do not exist
            };

            var entry = new FlipperManifestEntry { Name = "BrokenCycleAnim", MinLevel = 1, MaxLevel = 30, Weight = 1 };
            var set = new FlipperExportSettings(_tempDir, "BrokenCycleAnim", 5, 1, 0);

            var ex = Assert.Throws<InvalidOperationException>(() => _service.ExportAssetPack([(anim, entry, set)], _tempDir, isMomentum: true));
            Assert.Contains("FZM004", ex.Message);
        }

        [Fact]
        public void ExportAssetPack_InvalidManifestRanges_ThrowsInvalidOperationException()
        {
            var anim = new SpriteState(128, 64);
            var entry = new FlipperManifestEntry { Name = "BadRangeAnim", MinLevel = 20, MaxLevel = 10, Weight = -1 }; // MinLevel > MaxLevel & negative weight
            var set = new FlipperExportSettings(_tempDir, "BadRangeAnim", 5, 1, 0);

            var ex = Assert.Throws<InvalidOperationException>(() => _service.ExportAssetPack([(anim, entry, set)], _tempDir, isMomentum: true));
            Assert.Contains("FZ003", ex.Message);
            Assert.Contains("FZ006", ex.Message);
        }

        [Fact]
        public void ExportAssetPackZip_StockMode_GeneratesZipWithDolphinLayout()
        {
            var anim = new SpriteState(128, 64);
            var entry = new FlipperManifestEntry { Name = "ZipStockAnim", MinLevel = 1, MaxLevel = 3, Weight = 1 };
            var set = new FlipperExportSettings(_tempDir, "ZipStockAnim", 5, 1, 0, minLevel: 1, maxLevel: 3, minButthurt: 0, maxButthurt: 14, weight: 1);

            string zipPath = Path.Combine(_tempDir, "StockAssetPack.zip");
            _service.ExportAssetPackZip([(anim, entry, set)], zipPath, isMomentum: false);

            Assert.True(File.Exists(zipPath));

            string extractDir = Path.Combine(_tempDir, "StockZipExtracted");
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir);

            Assert.True(File.Exists(Path.Combine(extractDir, "dolphin", "manifest.txt")));
            Assert.True(File.Exists(Path.Combine(extractDir, "dolphin", "ZipStockAnim", "meta.txt")));
            Assert.True(File.Exists(Path.Combine(extractDir, "dolphin", "ZipStockAnim", "frame_0.bm")));
            Assert.False(Directory.Exists(Path.Combine(extractDir, "Icons")));
        }

        [Fact]
        public void ExportAssetPackZip_WithValidationErrors_ThrowsBeforeCreatingZipFile()
        {
            var anim = new SpriteState(200, 200); // Invalid dimensions
            var entry = new FlipperManifestEntry { Name = "InvalidZipAnim", MinLevel = 1, MaxLevel = 30, Weight = 1 };
            var set = new FlipperExportSettings(_tempDir, "InvalidZipAnim", 5, 1, 0);

            string zipPath = Path.Combine(_tempDir, "ShouldNotExist.zip");

            var ex = Assert.Throws<InvalidOperationException>(() => _service.ExportAssetPackZip([(anim, entry, set)], zipPath, isMomentum: true));
            Assert.Contains("FZM001", ex.Message);
            Assert.False(File.Exists(zipPath));
        }
    }
}
