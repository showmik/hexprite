using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Integration")]
    public class FlipperImportAdversarialChallengerTests : IDisposable
    {
        private readonly FlipperImportService _importService;
        private readonly string _tempDirectory;

        public FlipperImportAdversarialChallengerTests()
        {
            _importService = new FlipperImportService();
            _tempDirectory = Path.Combine(Path.GetTempPath(), "HexpriteChallengerTests_" + Guid.NewGuid().ToString("N"));
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

        #region 1. Natural Frame Sorting Stress Tests

        [Fact]
        public void NaturalSorting_LargeNumbersUpTo999_SortedInStrictNumericOrder()
        {
            // Test frame ordering up to 999 with large gaps and varied prefixes
            string animFolder = Path.Combine(_tempDirectory, "LargeNumbersAnim");
            Directory.CreateDirectory(animFolder);

            int width = 8;
            int height = 8;
            int bytesPerFrame = 8;

            int[] indices = [0, 1, 2, 9, 10, 11, 42, 99, 100, 101, 500, 998, 999];
            // Write them in shuffle/scrambled order to filesystem
            var shuffled = indices.OrderBy(_ => Guid.NewGuid()).ToArray();

            foreach (var idx in shuffled)
            {
                byte[] frameBytes = new byte[bytesPerFrame];
                frameBytes[0] = (byte)(idx & 0xFF);
                frameBytes[1] = (byte)((idx >> 8) & 0xFF);
                File.WriteAllBytes(Path.Combine(animFolder, $"frame_{idx}.bm"), frameBytes);
            }

            string meta = $"Filetype: Flipper Animation\nVersion: 1\nWidth: {width}\nHeight: {height}\nFrame rate: 5\n";
            File.WriteAllText(Path.Combine(animFolder, "meta.txt"), meta);

            var sprite = _importService.ImportAnimation(Path.Combine(animFolder, "meta.txt"));

            Assert.Equal(indices.Length, sprite.Frames.Count);
            for (int i = 0; i < indices.Length; i++)
            {
                int expectedIdx = indices[i];
                Assert.Equal($"Frame {i + 1}", sprite.Frames[i].Name);

                bool[] pixels = sprite.Frames[i].LayerPixels[0].GetMonochromeData();
                int lowByte = 0;
                for (int b = 0; b < 8; b++)
                {
                    if (pixels[b]) lowByte |= (1 << b);
                }
                Assert.Equal(expectedIdx & 0xFF, lowByte);
            }
        }

        [Fact]
        public void NaturalSorting_VariedZeroPaddingAndPrefixes_SortedCorrectly()
        {
            string animFolder = Path.Combine(_tempDirectory, "ZeroPaddingAnim");
            Directory.CreateDirectory(animFolder);

            int width = 8;
            int height = 8;

            // Frame filenames with irregular zero-padding: frame_000, frame_01, frame_2, frame_003, frame_010, frame_100
            (string filename, int expectedVal)[] frames =
            [
                ("frame_000.bm", 0),
                ("frame_01.bm", 1),
                ("frame_2.bm", 2),
                ("frame_003.bm", 3),
                ("frame_010.bm", 10),
                ("frame_100.bm", 100)
            ];

            foreach (var (filename, expectedVal) in frames.OrderBy(_ => Guid.NewGuid()))
            {
                byte[] frameBytes = new byte[8];
                frameBytes[0] = (byte)expectedVal;
                File.WriteAllBytes(Path.Combine(animFolder, filename), frameBytes);
            }

            string meta = $"Filetype: Flipper Animation\nVersion: 1\nWidth: {width}\nHeight: {height}\nFrame rate: 5\n";
            File.WriteAllText(Path.Combine(animFolder, "meta.txt"), meta);

            var sprite = _importService.ImportAnimation(Path.Combine(animFolder, "meta.txt"));

            Assert.Equal(frames.Length, sprite.Frames.Count);
            for (int i = 0; i < frames.Length; i++)
            {
                int expected = frames[i].expectedVal;
                bool[] pixels = sprite.Frames[i].LayerPixels[0].GetMonochromeData();
                int val = 0;
                for (int b = 0; b < 8; b++)
                {
                    if (pixels[b]) val |= (1 << b);
                }
                Assert.Equal(expected, val);
            }
        }

        [Fact]
        public void NaturalSorting_DiverseFilenamePatterns_ExtractsCorrectNumbers()
        {
            Assert.Equal(0, FlipperImportService.ExtractFrameNumber("0.bm"));
            Assert.Equal(5, FlipperImportService.ExtractFrameNumber("frame_5.bm"));
            Assert.Equal(12, FlipperImportService.ExtractFrameNumber("dolphin_jump_frame_12.bm"));
            Assert.Equal(42, FlipperImportService.ExtractFrameNumber("walk_128x64_42.bm"));
            Assert.Equal(999, FlipperImportService.ExtractFrameNumber("anim_000999.bm"));
            Assert.Equal(int.MaxValue, FlipperImportService.ExtractFrameNumber("static_background.bm"));
        }

        #endregion

        #region 2. Corrupted / Truncated Bitstream Fuzzing & Stress Tests

        [Theory]
        [InlineData(0)]                   // Empty file
        [InlineData(1)]                   // Single byte
        [InlineData(2)]                   // 2 bytes
        [InlineData(3)]                   // 3 bytes
        [InlineData(4)]                   // 4 bytes with compLen claiming 500
        [InlineData(10)]                  // Partial header + partial payload
        [InlineData(50)]                  // Random garbage payload
        [InlineData(1023)]                // 1023 bytes (1 byte short of 1024 raw)
        [InlineData(1025)]                // 1025 bytes (1 byte over raw)
        [InlineData(5000)]                // Large garbage payload
        public void CorruptedBitstream_FuzzByteLengths_DoesNotThrowAndProducesValidSprite(int payloadLength)
        {
            string animFolder = Path.Combine(_tempDirectory, $"FuzzAnim_{payloadLength}");
            Directory.CreateDirectory(animFolder);

            int width = 128;
            int height = 64;

            byte[] corruptData = new byte[payloadLength];
            if (payloadLength >= 4)
            {
                corruptData[0] = 0x01; // Compressed flag
                corruptData[1] = 0x00;
                corruptData[2] = 0xFF; // Claim 65535 bytes
                corruptData[3] = 0xFF;
                var rng = new Random(payloadLength);
                for (int i = 4; i < payloadLength; i++)
                {
                    corruptData[i] = (byte)rng.Next(256);
                }
            }
            else if (payloadLength > 0)
            {
                corruptData[0] = 0x01;
            }

            File.WriteAllBytes(Path.Combine(animFolder, "frame_0.bm"), corruptData);

            string meta = $"Filetype: Flipper Animation\nVersion: 1\nWidth: {width}\nHeight: {height}\nFrame rate: 5\n";
            File.WriteAllText(Path.Combine(animFolder, "meta.txt"), meta);

            var sprite = _importService.ImportAnimation(Path.Combine(animFolder, "meta.txt"));

            Assert.NotNull(sprite);
            Assert.Single(sprite.Frames);
            Assert.Equal("Frame 1", sprite.Frames[0].Name);
            bool[] pixels = sprite.Frames[0].LayerPixels[0].GetMonochromeData();
            Assert.Equal(128 * 64, pixels.Length);

            // For truncated compressed headers (>= 4 bytes claiming 65535), it should return blank placeholder
            if (payloadLength >= 4)
            {
                Assert.All(pixels, p => Assert.False(p));
            }
        }

        [Fact]
        public void CorruptedBitstream_MixedGoodAndBadFrames_PreservesGoodFrames()
        {
            string animFolder = Path.Combine(_tempDirectory, "MixedFramesAnim");
            Directory.CreateDirectory(animFolder);

            int width = 8;
            int height = 8;

            // Frame 0: Valid raw frame (LSB = 1)
            byte[] frame0 = new byte[8];
            frame0[0] = 0x01;
            File.WriteAllBytes(Path.Combine(animFolder, "frame_0.bm"), frame0);

            // Frame 1: Corrupted compressed header
            byte[] frame1 = [0x01, 0x00, 0x50, 0x00, 0xDE, 0xAD];
            File.WriteAllBytes(Path.Combine(animFolder, "frame_1.bm"), frame1);

            // Frame 2: Valid raw frame (LSB = 1)
            byte[] frame2 = new byte[8];
            frame2[0] = 0x03;
            File.WriteAllBytes(Path.Combine(animFolder, "frame_2.bm"), frame2);

            string meta = $"Filetype: Flipper Animation\nVersion: 1\nWidth: {width}\nHeight: {height}\nFrame rate: 5\n";
            File.WriteAllText(Path.Combine(animFolder, "meta.txt"), meta);

            var sprite = _importService.ImportAnimation(Path.Combine(animFolder, "meta.txt"));

            Assert.Equal(3, sprite.Frames.Count);

            // Frame 0 intact
            Assert.True(sprite.Frames[0].LayerPixels[0].GetMonochromeData()[0]);
            // Frame 1 placeholder (all false)
            Assert.All(sprite.Frames[1].LayerPixels[0].GetMonochromeData(), p => Assert.False(p));
            // Frame 2 intact
            Assert.True(sprite.Frames[2].LayerPixels[0].GetMonochromeData()[0]);
            Assert.True(sprite.Frames[2].LayerPixels[0].GetMonochromeData()[1]);
        }

        #endregion

        #region 3. Missing Manifest Folders & Layouts

        [Fact]
        public void MissingManifestFolders_MultipleMissingAndMalformedEntries_RetainsAllEntries()
        {
            string packDir = Path.Combine(_tempDirectory, "PartialPack");
            string validDir = Path.Combine(packDir, "Anims", "RealAnim");
            Directory.CreateDirectory(validDir);

            File.WriteAllBytes(Path.Combine(validDir, "frame_0.bm"), new byte[1024]);
            File.WriteAllText(Path.Combine(validDir, "meta.txt"), "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nFrame rate: 5\n");

            // Folder with invalid meta.txt
            string brokenMetaDir = Path.Combine(packDir, "Anims", "BrokenMetaAnim");
            Directory.CreateDirectory(brokenMetaDir);
            File.WriteAllText(Path.Combine(brokenMetaDir, "meta.txt"), "This is total garbage not a meta file\n");

            string manifestContent = @"Filetype: Flipper Animation Manifest
Version: 1

Name: RealAnim
Min butthurt: 0
Max butthurt: 3
Min level: 1
Max level: 5
Weight: 1

Name: CompletelyMissingFolderAnim
Min butthurt: 4
Max butthurt: 8
Min level: 6
Max level: 15
Weight: 2

Name: BrokenMetaAnim
Min butthurt: 9
Max butthurt: 14
Min level: 16
Max level: 30
Weight: 3
";
            File.WriteAllText(Path.Combine(packDir, "Anims", "manifest.txt"), manifestContent);

            var result = _importService.ImportAssetPack(packDir);

            Assert.Equal(3, result.Count);
            Assert.Equal("RealAnim", result[0].Name);
            Assert.Equal("CompletelyMissingFolderAnim", result[1].Name);
            Assert.Equal("BrokenMetaAnim", result[2].Name);

            // All entries have valid 128x64 sprites with at least 1 frame
            foreach (var item in result)
            {
                Assert.NotNull(item.Sprite);
                Assert.Equal(128, item.Sprite.Width);
                Assert.Equal(64, item.Sprite.Height);
                Assert.NotEmpty(item.Sprite.Frames);
            }
        }

        [Fact]
        public void ImportAssetPack_DeeplyNestedDirectoryStructure_ResolvesCorrectly()
        {
            string packDir = Path.Combine(_tempDirectory, "DeepPack");
            string nestedAnimDir = Path.Combine(packDir, "sub1", "sub2", "Anims", "NestedDance");
            Directory.CreateDirectory(nestedAnimDir);

            File.WriteAllBytes(Path.Combine(nestedAnimDir, "frame_0.bm"), new byte[1024]);
            File.WriteAllText(Path.Combine(nestedAnimDir, "meta.txt"), "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nFrame rate: 5\n");

            string manifestContent = @"Filetype: Flipper Animation Manifest
Version: 1

Name: NestedDance
Min butthurt: 0
Max butthurt: 14
Min level: 1
Max level: 30
Weight: 10
";
            File.WriteAllText(Path.Combine(packDir, "sub1", "sub2", "Anims", "manifest.txt"), manifestContent);

            var result = _importService.ImportAssetPack(packDir);

            Assert.Single(result);
            Assert.Equal("NestedDance", result[0].Name);
            Assert.Equal(10, result[0].ManifestEntry.Weight);
        }

        [Fact]
        public void ImportAssetPack_EmptyDirectory_ThrowsFileNotFoundException()
        {
            string emptyDir = Path.Combine(_tempDirectory, "EmptyDir");
            Directory.CreateDirectory(emptyDir);

            var ex = Assert.Throws<FileNotFoundException>(() => _importService.ImportAssetPack(emptyDir));
            Assert.Contains("No manifest.txt or animation meta.txt found", ex.Message);
        }

        #endregion

        #region 4. Archive (.zip) Ingestion & Resource Cleanup

        [Fact]
        public void ImportAssetPack_FromZip_CleansUpTemporaryExtractionFolder()
        {
            string sourceDir = Path.Combine(_tempDirectory, "ZipSource");
            string animDir = Path.Combine(sourceDir, "Anims", "ZipAnim");
            Directory.CreateDirectory(animDir);

            File.WriteAllBytes(Path.Combine(animDir, "frame_0.bm"), new byte[1024]);
            File.WriteAllText(Path.Combine(animDir, "meta.txt"), "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nFrame rate: 5\n");

            string manifestContent = "Filetype: Flipper Animation Manifest\nVersion: 1\n\nName: ZipAnim\nMin butthurt: 0\nMax butthurt: 14\nMin level: 1\nMax level: 30\nWeight: 1\n";
            File.WriteAllText(Path.Combine(sourceDir, "Anims", "manifest.txt"), manifestContent);

            string zipPath = Path.Combine(_tempDirectory, "TestPack.zip");
            ZipFile.CreateFromDirectory(sourceDir, zipPath);

            // Check temp directories before and after
            string tempRoot = Path.GetTempPath();
            var beforeDirs = Directory.GetDirectories(tempRoot, "HexpritePackZip_*").ToHashSet(StringComparer.OrdinalIgnoreCase);

            var result = _importService.ImportAssetPack(zipPath);

            // Allow brief moment for file handles if needed and verify no persisted leaked dirs from this test.
            // Concurrent test executions in parallel test classes may briefly create/delete temporary folders.
            List<string> leakedDirs = new();
            for (int i = 0; i < 10; i++)
            {
                var afterDirs = Directory.GetDirectories(tempRoot, "HexpritePackZip_*");
                leakedDirs = afterDirs.Where(d => !beforeDirs.Contains(d) && Directory.Exists(d)).ToList();
                if (leakedDirs.Count == 0) break;
                Thread.Sleep(50);
            }

            Assert.Single(result);
            Assert.Equal("ZipAnim", result[0].Name);
            // Verify no temp directories leaked
            Assert.Empty(leakedDirs);
        }

        [Fact]
        public void ImportAssetPack_CorruptZipArchive_ThrowsAndCleansUp()
        {
            string tempRoot = Path.GetTempPath();
            var beforeDirs = Directory.GetDirectories(tempRoot, "HexpritePackZip_*").ToHashSet(StringComparer.OrdinalIgnoreCase);

            string badZipPath = Path.Combine(_tempDirectory, "Corrupt.zip");
            File.WriteAllBytes(badZipPath, [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0xFF, 0xFF]);

            Assert.ThrowsAny<Exception>(() => _importService.ImportAssetPack(badZipPath));

            // Verify no leaked temp directory
            var afterDirs = Directory.GetDirectories(tempRoot, "HexpritePackZip_*");
            var leakedDirs = afterDirs.Where(d => !beforeDirs.Contains(d) && Directory.Exists(d)).ToList();
            Assert.Empty(leakedDirs);
        }

        #endregion

        #region 5. End-to-End ViewModel Tab Integration

        [Fact]
        public void AssetPackViewModel_FullImportToMatrixAndPreviewLifecycle()
        {
            // Verify that imported pack flows seamlessly through AssetPackViewModel, Matrix, and LCD Preview
            string packDir = Path.Combine(_tempDirectory, "LifecyclePack");
            string animDir = Path.Combine(packDir, "Anims", "DolphinJump");
            Directory.CreateDirectory(animDir);

            // Write 3 distinct frames
            for (int f = 0; f < 3; f++)
            {
                byte[] frame = new byte[1024];
                frame[0] = (byte)(f + 1); // distinguish frames
                File.WriteAllBytes(Path.Combine(animDir, $"frame_{f}.bm"), frame);
            }

            string metaContent = "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nPassive frames: 2\nActive frames: 1\nFrames order: 0 1 2\nActive cycles: 1\nFrame rate: 8\nDuration: 3600\nActive cooldown: 0\nBubble slots: 0\n";
            File.WriteAllText(Path.Combine(animDir, "meta.txt"), metaContent);

            string manifestContent = "Filetype: Flipper Animation Manifest\nVersion: 1\n\nName: DolphinJump\nMin butthurt: 2\nMax butthurt: 6\nMin level: 5\nMax level: 12\nWeight: 4\n";
            File.WriteAllText(Path.Combine(packDir, "Anims", "manifest.txt"), manifestContent);

            var pack = _importService.ImportAssetPack(packDir);

            // Act - Instantiate AssetPackViewModel
            var vm = new AssetPackViewModel(pack, "LifecyclePack");

            // Assert
            Assert.Equal("LifecyclePack (Pack)", vm.Title);
            Assert.Single(vm.MatrixViewModel.Entries);
            var entry = vm.MatrixViewModel.Entries[0];
            Assert.Equal("DolphinJump", entry.Name);
            Assert.Equal(5, entry.MinLevel);
            Assert.Equal(12, entry.MaxLevel);
            Assert.Equal(2, entry.MinButthurt);
            Assert.Equal(6, entry.MaxButthurt);
            Assert.Equal(4, entry.Weight);

            // Select entry
            vm.MatrixViewModel.SelectedEntry = entry;
            Assert.Equal("DolphinJump", vm.MatrixViewModel.SelectedName);
            Assert.Equal("Frame 1 / 3", vm.MatrixViewModel.PreviewFrameCountText);

            // Cycle through preview frames
            vm.MatrixViewModel.PreviewNextFrame();
            Assert.Equal("Frame 2 / 3", vm.MatrixViewModel.PreviewFrameCountText);
            vm.MatrixViewModel.PreviewNextFrame();
            Assert.Equal("Frame 3 / 3", vm.MatrixViewModel.PreviewFrameCountText);
            vm.MatrixViewModel.PreviewNextFrame();
            Assert.Equal("Frame 1 / 3", vm.MatrixViewModel.PreviewFrameCountText);
        }

        #endregion

        #region 6. Extreme Stress Tests & Layout Variations

        [Fact]
        public void NaturalSorting_StressTest_1000Frames_MonotonicallyOrdered()
        {
            string animFolder = Path.Combine(_tempDirectory, "ThousandFramesAnim");
            Directory.CreateDirectory(animFolder);

            int width = 8;
            int height = 8;
            int bytesPerFrame = 8;
            int totalFrames = 1000;

            // Generate 1000 frames: frame_0.bm .. frame_999.bm in random order
            var indices = Enumerable.Range(0, totalFrames).OrderBy(_ => Guid.NewGuid()).ToList();
            foreach (var idx in indices)
            {
                byte[] frame = new byte[bytesPerFrame];
                frame[0] = (byte)(idx & 0xFF);
                frame[1] = (byte)((idx >> 8) & 0xFF);
                File.WriteAllBytes(Path.Combine(animFolder, $"frame_{idx}.bm"), frame);
            }

            string meta = $"Filetype: Flipper Animation\nVersion: 1\nWidth: {width}\nHeight: {height}\nFrame rate: 30\n";
            File.WriteAllText(Path.Combine(animFolder, "meta.txt"), meta);

            var sprite = _importService.ImportAnimation(Path.Combine(animFolder, "meta.txt"));

            Assert.Equal(totalFrames, sprite.Frames.Count);
            for (int i = 0; i < totalFrames; i++)
            {
                Assert.Equal($"Frame {i + 1}", sprite.Frames[i].Name);
                bool[] pixels = sprite.Frames[i].LayerPixels[0].GetMonochromeData();
                int lowByte = 0;
                int highByte = 0;
                for (int b = 0; b < 8; b++)
                {
                    if (pixels[b]) lowByte |= (1 << b);
                    if (pixels[8 + b]) highByte |= (1 << b);
                }
                int decodedIdx = lowByte | (highByte << 8);
                Assert.Equal(i, decodedIdx);
            }
        }

        [Fact]
        public void ImportAnimation_InvalidMetaProperties_FallbacksSafely()
        {
            string animFolder = Path.Combine(_tempDirectory, "InvalidMetaPropsAnim");
            Directory.CreateDirectory(animFolder);

            File.WriteAllBytes(Path.Combine(animFolder, "frame_0.bm"), new byte[1024]);

            // Negative / zero width, height, framerate; mismatched active/passive counts vs order; out of bounds order
            string metaContent = @"Filetype: Flipper Animation
Version: 1
Width: -128
Height: 0
Frame rate: -10
Passive frames: 10
Active frames: 5
Frames order: 0 999
";
            File.WriteAllText(Path.Combine(animFolder, "meta.txt"), metaContent);

            var sprite = _importService.ImportAnimation(Path.Combine(animFolder, "meta.txt"));

            Assert.Equal(128, sprite.Width);
            Assert.Equal(64, sprite.Height);
            Assert.Equal(5, sprite.FrameRateFps);
            Assert.Single(sprite.Frames);
            // Cycle should safely be null because FramesOrder index 999 is out of bounds
            Assert.Null(sprite.FlipperCycle);
        }

        [Fact]
        public void ImportAssetPack_StockDolphinLayout_ResolvesCorrectly()
        {
            string rootDir = Path.Combine(_tempDirectory, "StockPack");
            string dolphinFolder = Path.Combine(rootDir, "dolphin", "HappyDolphin");
            Directory.CreateDirectory(dolphinFolder);

            File.WriteAllBytes(Path.Combine(dolphinFolder, "frame_0.bm"), new byte[1024]);
            File.WriteAllText(Path.Combine(dolphinFolder, "meta.txt"), "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nFrame rate: 5\n");

            string manifestContent = @"Filetype: Flipper Animation Manifest
Version: 1

Name: HappyDolphin
Min butthurt: 0
Max butthurt: 4
Min level: 1
Max level: 3
Weight: 2
";
            File.WriteAllText(Path.Combine(rootDir, "dolphin", "manifest.txt"), manifestContent);

            var pack = _importService.ImportAssetPack(rootDir);

            Assert.Single(pack);
            Assert.Equal("HappyDolphin", pack[0].Name);
            Assert.Equal(3, pack[0].ManifestEntry.MaxLevel);
        }

        #endregion
    }
}
