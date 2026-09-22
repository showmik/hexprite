using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.Services.Compression;
using Hexprite.ViewModels;
using Xunit;

namespace Hexprite.Tests.E2E
{
    [Trait("Category", "Unit")]
    public class Challenger2AdversarialTests
    {
        public Challenger2AdversarialTests()
        {
            WpfTestHelper.EnsureApplication();
        }

        #region 1. In-Memory Deployment File Generation Tests

        [Fact]
        public void GenerateDeploymentFiles_ZeroDiskTempLeaks_AndCorrectContents()
        {
            var exportService = new FlipperExportService();
            var sprite = E2ETestHelper.CreateTestSprite(width: 128, height: 64, frameCount: 3);

            var settings = new FlipperExportSettings
            {
                AnimationName = "Adversarial_Test_Anim!@#$",
                TargetMode = FlipperExportTargetMode.MomentumAssetPack,
                FrameRate = 12,
                MinLevel = 1,
                MaxLevel = 30,
                MinButthurt = 0,
                MaxButthurt = 14,
                Weight = 5,
                CreateManifestTxt = true
            };

            string tempDir = Path.GetTempPath();
            var filesBefore = Directory.GetFiles(tempDir);

            // Execute in-memory deployment file generation
            var deployedFiles = exportService.GenerateDeploymentFiles(sprite, settings);

            var filesAfter = Directory.GetFiles(tempDir);

            // 1. Verify zero disk temp leaks
            var createdFiles = filesAfter.Except(filesBefore).Where(f => f.Contains("Adversarial") || f.Contains("Hexprite")).ToList();
            Assert.Empty(createdFiles);

            // 2. Verify returned files collection
            Assert.NotNull(deployedFiles);
            Assert.NotEmpty(deployedFiles);

            string sanitizedName = FlipperExportService.SanitizeAnimationName(settings.AnimationName);
            Assert.Equal("Adversarial_Test_Anim", sanitizedName);

            // Expect: meta.txt, 3 frame bm files, manifest.txt, 10x10 icon
            Assert.Equal(6, deployedFiles.Count);

            // Verify meta.txt
            var metaFile = deployedFiles.FirstOrDefault(f => f.RelativePath == $"Anims/{sanitizedName}/meta.txt");
            Assert.NotNull(metaFile.Data);
            string metaContent = Encoding.UTF8.GetString(metaFile.Data);
            Assert.Contains("Filetype: Flipper Animation", metaContent);
            Assert.Contains("Width: 128", metaContent);
            Assert.Contains("Height: 64", metaContent);
            Assert.Contains("Frame rate: 12", metaContent);

            // Verify frame bm files
            for (int i = 0; i < 3; i++)
            {
                var frameFile = deployedFiles.FirstOrDefault(f => f.RelativePath == $"Anims/{sanitizedName}/frame_{i}.bm");
                Assert.NotNull(frameFile.Data);
                Assert.True(frameFile.Data.Length > 0);
            }

            // Verify manifest.txt
            var manifestFile = deployedFiles.FirstOrDefault(f => f.RelativePath == "Anims/manifest.txt");
            Assert.NotNull(manifestFile.Data);
            string manifestContent = Encoding.UTF8.GetString(manifestFile.Data);
            var parsedManifest = FlipperManifest.Parse(manifestContent);
            Assert.Single(parsedManifest.Entries);
            var entry = parsedManifest.Entries[0];
            Assert.Equal(sanitizedName, entry.Name);
            Assert.Equal(1, entry.MinLevel);
            Assert.Equal(30, entry.MaxLevel);
            Assert.Equal(0, entry.MinButthurt);
            Assert.Equal(14, entry.MaxButthurt);
            Assert.Equal(5, entry.Weight);

            // Verify 10x10 pack icon
            var iconFile = deployedFiles.FirstOrDefault(f => f.RelativePath == $"Icons/I_{sanitizedName}_10x10.bm");
            Assert.NotNull(iconFile.Data);
            Assert.True(iconFile.Data.Length > 0);
        }

        [Theory]
        [InlineData(FlipperExportTargetMode.MomentumAssetPack, "Anims/MyAnim/meta.txt", "Anims/manifest.txt", "Icons/I_MyAnim_10x10.bm")]
        [InlineData(FlipperExportTargetMode.StockDolphin, "dolphin/MyAnim/meta.txt", "dolphin/manifest.txt", null)]
        [InlineData(FlipperExportTargetMode.SingleAnimation, "MyAnim/meta.txt", "manifest.txt", null)]
        public void GenerateDeploymentFiles_PathPrefixes_MatchTargetMode(
            FlipperExportTargetMode mode,
            string expectedMetaPath,
            string expectedManifestPath,
            string? expectedIconPath)
        {
            var exportService = new FlipperExportService();
            var sprite = E2ETestHelper.CreateTestSprite(width: 64, height: 32, frameCount: 2);

            var settings = new FlipperExportSettings
            {
                AnimationName = "MyAnim",
                TargetMode = mode,
                CreateManifestTxt = true
            };

            var deployedFiles = exportService.GenerateDeploymentFiles(sprite, settings);

            Assert.Contains(deployedFiles, f => f.RelativePath == expectedMetaPath);
            Assert.Contains(deployedFiles, f => f.RelativePath == expectedManifestPath);

            if (expectedIconPath != null)
            {
                Assert.Contains(deployedFiles, f => f.RelativePath == expectedIconPath);
            }
            else
            {
                Assert.DoesNotContain(deployedFiles, f => f.RelativePath.StartsWith("Icons/"));
            }
        }

        #endregion

        #region 2. Multi-Document Tab Isolation & Deep Cloning Tests

        [Fact]
        public void MultiDocumentTab_DeepCloning_PreventsCrossTabAndSourceMutation()
        {
            var shellVm = E2ETestHelper.CreateTestShellViewModel();
            var tabService = new WorkspaceTabService(shellVm);

            // 1. Create a source sprite with known pixel pattern
            int width = 16;
            int height = 16;
            var sourceSprite = new SpriteState(width, height)
            {
                FrameRateFps = 10,
                ColorMode = ColorMode.Monochrome
            };

            var initialPixels = new bool[width * height];
            initialPixels[0] = true;
            initialPixels[10] = true;
            initialPixels[25] = true;

            var frame0 = new FrameState
            {
                Name = "Frame 1",
                LayerPixels = [new MonochromePixelBuffer((bool[])initialPixels.Clone())]
            };
            sourceSprite.Frames = [frame0];
            sourceSprite.Layers = [new LayerState { Name = "Base", IsVisible = true }];

            // 2. Open Tab 1 and Tab 2 with the same source sprite
            tabService.OpenSpriteInTab(sourceSprite, "Tab 1");
            tabService.OpenSpriteInTab(sourceSprite, "Tab 2");

            Assert.Equal(2, shellVm.OpenDocuments.Count);
            var doc1 = Assert.IsAssignableFrom<MainViewModel>(shellVm.OpenDocuments[0]);
            var doc2 = Assert.IsAssignableFrom<MainViewModel>(shellVm.OpenDocuments[1]);

            // 3. Mutate Tab 1's pixels
            var doc1Buffer = doc1.SpriteState.Frames[0].LayerPixels[0];
            var doc1Data = doc1Buffer.GetMonochromeData();
            doc1Data[0] = false; // change pixel 0
            doc1Data[1] = true;  // new pixel 1
            doc1Data[50] = true; // new pixel 50

            // 4. Verify Source Sprite was NOT mutated
            var srcData = sourceSprite.Frames[0].LayerPixels[0].GetMonochromeData();
            Assert.True(srcData[0], "Source pixel 0 must remain true");
            Assert.False(srcData[1], "Source pixel 1 must remain false");
            Assert.False(srcData[50], "Source pixel 50 must remain false");
            Assert.True(srcData[10], "Source pixel 10 must remain true");
            Assert.True(srcData[25], "Source pixel 25 must remain true");

            // 5. Verify Tab 2 was NOT mutated by Tab 1's edits
            var doc2Buffer = doc2.SpriteState.Frames[0].LayerPixels[0];
            var doc2Data = doc2Buffer.GetMonochromeData();
            Assert.True(doc2Data[0], "Tab 2 pixel 0 must remain true");
            Assert.False(doc2Data[1], "Tab 2 pixel 1 must remain false");
            Assert.False(doc2Data[50], "Tab 2 pixel 50 must remain false");
            Assert.True(doc2Data[10], "Tab 2 pixel 10 must remain true");
            Assert.True(doc2Data[25], "Tab 2 pixel 25 must remain true");

            // 6. Mutate Tab 2 by adding a new frame and changing existing pixels
            doc2.SpriteState.Frames.Add(new FrameState
            {
                Name = "Frame 2",
                LayerPixels = [new MonochromePixelBuffer(width * height)]
            });
            doc2Data[10] = false;

            // 7. Verify Tab 1 is unaffected by Tab 2 frame addition or pixel change
            Assert.Single(doc1.SpriteState.Frames);
            Assert.True(doc1Data[10], "Tab 1 pixel 10 must remain true");
        }

        #endregion

        #region 4. Heatshrink Compression Round-Tripping Stress Tests

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(16)]
        [InlineData(255)]
        [InlineData(256)]
        [InlineData(257)]
        [InlineData(512)]
        [InlineData(1024)]
        [InlineData(4096)]
        public void Heatshrink_AllZeros_RoundTrips(int size)
        {
            byte[] original = new byte[size];
            byte[] compressed = HeatshrinkCompressor.Compress(original);
            byte[] decompressed = HeatshrinkCompressor.Decompress(compressed, size);

            Assert.Equal(original, decompressed);
            if (size > 16)
            {
                Assert.True(compressed.Length < original.Length, "All-zeros stream should achieve significant compression");
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(16)]
        [InlineData(256)]
        [InlineData(1024)]
        public void Heatshrink_AllOnes_RoundTrips(int size)
        {
            byte[] original = new byte[size];
            Array.Fill(original, (byte)0xFF);

            byte[] compressed = HeatshrinkCompressor.Compress(original);
            byte[] decompressed = HeatshrinkCompressor.Decompress(compressed, size);

            Assert.Equal(original, decompressed);
        }

        [Theory]
        [InlineData(16)]
        [InlineData(256)]
        [InlineData(512)]
        [InlineData(1024)]
        public void Heatshrink_AlternatingPattern_RoundTrips(int size)
        {
            byte[] original = new byte[size];
            for (int i = 0; i < size; i++)
            {
                original[i] = (byte)(i % 2 == 0 ? 0xAA : 0x55);
            }

            byte[] compressed = HeatshrinkCompressor.Compress(original);
            byte[] decompressed = HeatshrinkCompressor.Decompress(compressed, size);

            Assert.Equal(original, decompressed);
        }

        [Fact]
        public void Heatshrink_SlidingWindowCrossing_RoundTrips()
        {
            // Test data larger than the 256-byte sliding window with periodic repetitions
            int size = 1200;
            byte[] original = new byte[size];
            for (int i = 0; i < size; i++)
            {
                original[i] = (byte)((i % 73) ^ (i / 13));
            }

            byte[] compressed = HeatshrinkCompressor.Compress(original);
            byte[] decompressed = HeatshrinkCompressor.Decompress(compressed, size);

            Assert.Equal(original, decompressed);
        }

        [Fact]
        public void Heatshrink_HighEntropyRandomData_RoundTrips()
        {
            // Pseudo-random bitstream testing
            var seeds = new[] { 1337, 42, 9999, 123456 };
            foreach (var seed in seeds)
            {
                var rnd = new Random(seed);
                foreach (var size in new[] { 17, 63, 128, 255, 256, 500, 1024 })
                {
                    byte[] original = new byte[size];
                    rnd.NextBytes(original);

                    byte[] compressed = HeatshrinkCompressor.Compress(original);
                    byte[] decompressed = HeatshrinkCompressor.Decompress(compressed, size);

                    Assert.Equal(original, decompressed);
                }
            }
        }

        [Fact]
        public void Heatshrink_LongRunChunking_Beyond4BitLookahead_RoundTrips()
        {
            // Run length 500 exceeds the 16-byte max match length of 4-bit lookahead
            byte[] original = new byte[750];
            Array.Fill(original, (byte)0x3C, 0, 500);
            Array.Fill(original, (byte)0x7E, 500, 250);

            byte[] compressed = HeatshrinkCompressor.Compress(original);
            byte[] decompressed = HeatshrinkCompressor.Decompress(compressed, original.Length);

            Assert.Equal(original, decompressed);
        }

        [Fact]
        public void Heatshrink_TruncatedOrCorruptBitstreams_ThrowsGracefully()
        {
            // Truncated literal
            Assert.Throws<InvalidDataException>(() => HeatshrinkCompressor.Decompress([0x80], 4));

            // Truncated backreference
            Assert.Throws<InvalidDataException>(() => HeatshrinkCompressor.Decompress([0x00], 10));

            // Incomplete decode
            Assert.Throws<InvalidDataException>(() => HeatshrinkCompressor.Decompress([0x80, 0x00], 100));
        }

        #endregion
    }
}
