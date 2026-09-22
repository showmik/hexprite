using System;
using System.IO;
using System.Linq;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.Services.Compression;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Integration")]
    public class FlipperImportServiceTests : IDisposable
    {
        private readonly FlipperImportService _importService;
        private readonly FlipperExportService _exportService;
        private readonly string _tempDirectory;

        public FlipperImportServiceTests()
        {
            _importService = new FlipperImportService();
            _exportService = new FlipperExportService();
            _tempDirectory = Path.Combine(Path.GetTempPath(), "HexpriteImportTests_" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        [Fact]
        public void ImportFrame_ParsesBinaryAccurately()
        {
            // 1. Arrange - Write a known byte array representing a specific pattern
            int width = 8; // Small test size
            int height = 8;
            string bmPath = Path.Combine(_tempDirectory, "test_frame.bm");
            
            // For an 8x8 image horizontally packed, there are 8 rows of 1 byte each.
            // Let's test Row 0: 0b11110000 (240). LSB first: 0 0 0 0 1 1 1 1
            // Let's test Row 1: 0b00001111 (15). LSB first: 1 1 1 1 0 0 0 0
            byte[] mockData = new byte[8];
            mockData[0] = 240; 
            mockData[1] = 15;
            for (int i = 2; i < 8; i++) mockData[i] = 0;
            
            File.WriteAllBytes(bmPath, mockData);

            // 2. Act
            var sprite = _importService.ImportFrame(bmPath, width, height);

            // 3. Assert
            Assert.NotNull(sprite);
            bool[] pixels = sprite.Frames[0].LayerPixels[0].GetMonochromeData();

            // Verify horizontal parsing
            // Row 0
            Assert.False(pixels[0]); // (0, 0)
            Assert.False(pixels[1]); // (1, 0)
            Assert.False(pixels[2]); // (2, 0)
            Assert.False(pixels[3]); // (3, 0)
            Assert.True(pixels[4]);  // (4, 0)
            Assert.True(pixels[5]);  // (5, 0)
            
            // Row 1
            Assert.True(pixels[1 * width + 0]);  // (0, 1)
            Assert.True(pixels[1 * width + 3]);  // (3, 1)
            Assert.False(pixels[1 * width + 4]); // (4, 1)
        }

        [Fact]
        public void ImportAnimation_HeatshrinkCompressionRoundTrip()
        {
            // 1. Arrange - Export a large uniform animation to trigger Heatshrink compression
            var originalSprite = new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome };
            originalSprite.Frames.Clear();
            var frame1 = new FrameState { Name = "Frame 1" };
            bool[] pixels = new bool[128 * 64];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = true; // All black (highly compressible)
            frame1.LayerPixels.Add(new MonochromePixelBuffer(pixels));
            originalSprite.Frames.Add(frame1);

            var settings = new FlipperExportSettings(_tempDirectory, "CompressAnim", 12, 0, 1);
            _exportService.ExportAnimation(originalSprite, settings);

            string animFolder = Path.Combine(_tempDirectory, "CompressAnim");
            string framePath = Path.Combine(animFolder, "frame_0.bm");

            // Verify it was actually compressed
            byte[] exportedBytes = File.ReadAllBytes(framePath);
            Assert.True(exportedBytes.Length < 1024, "File should have been compressed");
            Assert.Equal(0x01, exportedBytes[0]); // Magic header
            Assert.Equal(0x00, exportedBytes[1]);

            // 2. Act
            string metaTxtPath = Path.Combine(animFolder, "meta.txt");
            var importedSprite = _importService.ImportAnimation(metaTxtPath);

            // 3. Assert
            Assert.NotNull(importedSprite);
            bool[] importedPixels = importedSprite.Frames[0].LayerPixels[0].GetMonochromeData();
            
            // Verify decompression was perfect
            for (int i = 0; i < importedPixels.Length; i++)
            {
                Assert.True(importedPixels[i], $"Pixel {i} is false");
            }
        }

        [Fact]
        public void ImportAnimation_ReconstructsFolderAndMetaTxt()
        {
            // 1. Arrange - Export an animation to create a valid folder structure
            var originalSprite = new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome };
            originalSprite.FrameRateFps = 12;
            originalSprite.Frames.Clear();
            
            // Frame 1
            var frame1 = new FrameState { Name = "Frame 1" };
            bool[] pixels1 = new bool[128 * 64];
            pixels1[0] = true; // Set one pixel to verify later
            frame1.LayerPixels.Add(new MonochromePixelBuffer(pixels1));
            originalSprite.Frames.Add(frame1);

            // Frame 2
            var frame2 = new FrameState { Name = "Frame 2" };
            bool[] pixels2 = new bool[128 * 64];
            pixels2[128 * 64 - 1] = true; // Set last pixel
            frame2.LayerPixels.Add(new MonochromePixelBuffer(pixels2));
            originalSprite.Frames.Add(frame2);

            var settings = new FlipperExportSettings(_tempDirectory, "TestAnim", 12, 1, 1);
            _exportService.ExportAnimation(originalSprite, settings);

            string animFolder = Path.Combine(_tempDirectory, "TestAnim");
            string metaTxtPath = Path.Combine(animFolder, "meta.txt");

            // 2. Act
            var importedSprite = _importService.ImportAnimation(metaTxtPath);

            // 3. Assert
            Assert.NotNull(importedSprite);
            Assert.Equal(128, importedSprite.Width);
            Assert.Equal(64, importedSprite.Height);
            Assert.Equal(12, importedSprite.FrameRateFps);
            Assert.Equal(2, importedSprite.Frames.Count);

            bool[] importedPixels1 = importedSprite.Frames[0].LayerPixels[0].GetMonochromeData();
            Assert.True(importedPixels1[0]); // Verification of Frame 1 data
            Assert.False(importedPixels1[1]);

            bool[] importedPixels2 = importedSprite.Frames[1].LayerPixels[0].GetMonochromeData();
            Assert.False(importedPixels2[0]);
            Assert.True(importedPixels2[128 * 64 - 1]); // Verification of Frame 2 data
        }

        [Fact]
        public void ImportAnimation_NamesFramesSequentiallyFromOne()
        {
            // Arrange — export a 3-frame animation, then re-import it.
            var originalSprite = new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome };
            originalSprite.Frames.Clear();
            for (int f = 0; f < 3; f++)
            {
                var frame = new FrameState { Name = $"Frame {f + 1}" };
                frame.LayerPixels.Add(new MonochromePixelBuffer(new bool[128 * 64]));
                originalSprite.Frames.Add(frame);
            }

            var settings = new FlipperExportSettings(_tempDirectory, "NamingAnim", 12, 1, 2);
            _exportService.ExportAnimation(originalSprite, settings);

            string animFolder = Path.Combine(_tempDirectory, "NamingAnim");
            string metaTxtPath = Path.Combine(animFolder, "meta.txt");

            // Act
            var importedSprite = _importService.ImportAnimation(metaTxtPath);

            // Assert — frames are named Frame 1..N, not Frame N..2N-1 (the old frameIdx-reuse bug).
            Assert.Equal(3, importedSprite.Frames.Count);
            Assert.Equal("Frame 1", importedSprite.Frames[0].Name);
            Assert.Equal("Frame 2", importedSprite.Frames[1].Name);
            Assert.Equal("Frame 3", importedSprite.Frames[2].Name);
        }

        [Fact]
        public void ImportAnimation_HandlesUncompressedFlipperFrame()
        {
            // Arrange — write a Flipper-style uncompressed .bm: 0x00 header + raw pixels.
            int width = 8;
            int height = 8;
            int expectedBytes = (width * height) / 8; // 8
            string animFolder = Path.Combine(_tempDirectory, "UncompressedAnim");
            Directory.CreateDirectory(animFolder);

            // Raw pixel row: row0 = 0b11110000 (LSB-first: 0 0 0 0 1 1 1 1)
            byte[] raw = new byte[expectedBytes];
            raw[0] = 0xF0; // row 0
            raw[1] = 0x0F; // row 1
            byte[] bmData = new byte[1 + expectedBytes];
            bmData[0] = 0x00; // Flipper uncompressed marker
            Array.Copy(raw, 0, bmData, 1, expectedBytes);

            File.WriteAllBytes(Path.Combine(animFolder, "frame_0.bm"), bmData);

            // meta.txt
            string meta = $"Filetype: Flipper Animation\nVersion: 1\nWidth: {width}\nHeight: {height}\nFrame rate: 5\n";
            string metaPath = Path.Combine(animFolder, "meta.txt");
            File.WriteAllText(metaPath, meta);

            // Act
            var sprite = _importService.ImportAnimation(metaPath);

            // Assert — header byte is stripped, raw pixels decode correctly.
            bool[] pixels = sprite.Frames[0].LayerPixels[0].GetMonochromeData();
            Assert.False(pixels[0]);  // (0,0)
            Assert.True(pixels[4]);   // (4,0)
            Assert.True(pixels[width + 0]);  // (0,1)
            Assert.False(pixels[width + 4]); // (4,1)
            Assert.Equal("Frame 1", sprite.Frames[0].Name);
        }

        [Fact]
        public void ImportAnimation_InsertsPlaceholderForCorruptFrame_ImportsRest()
        {
            // Arrange — 3 frames: 0 and 2 are valid (headerless raw), 1 is a corrupt
            // compressed frame whose bitstream is truncated mid-token.
            int width = 8;
            int height = 8;
            int expectedBytes = (width * height) / 8; // 8
            string animFolder = Path.Combine(_tempDirectory, "CorruptAnim");
            Directory.CreateDirectory(animFolder);

            // Frame 0: valid headerless raw (all zero)
            File.WriteAllBytes(Path.Combine(animFolder, "frame_0.bm"), new byte[expectedBytes]);

            // Frame 1: corrupt — 0x01 0x00 magic, compLen=2, payload 0x00 0x00.
            // The 2-byte (16-bit) bitstream only has enough bits for one full backref
            // token (flag=0, offset=0+1=1, length=0+1=1, copying 1 zero byte from the
            // window's zero-initialized padding — that part is valid heatshrink
            // behavior, not corruption) before a second token starts and runs out of
            // input mid-field → InvalidDataException ("truncated") → blank placeholder.
            byte[] corrupt = new byte[] { 0x01, 0x00, 0x02, 0x00, 0x00, 0x00 };
            File.WriteAllBytes(Path.Combine(animFolder, "frame_1.bm"), corrupt);

            // Frame 2: valid headerless raw with one pixel set
            byte[] frame2Data = new byte[expectedBytes];
            frame2Data[0] = 0x01; // pixel (0,0) on
            File.WriteAllBytes(Path.Combine(animFolder, "frame_2.bm"), frame2Data);

            string meta = $"Filetype: Flipper Animation\nVersion: 1\nWidth: {width}\nHeight: {height}\nFrame rate: 5\n";
            string metaPath = Path.Combine(animFolder, "meta.txt");
            File.WriteAllText(metaPath, meta);

            // Act — must not throw; corrupt frame becomes a blank placeholder.
            var sprite = _importService.ImportAnimation(metaPath);

            // Assert — all 3 frames present, frame 1 is all-zero placeholder.
            Assert.Equal(3, sprite.Frames.Count);
            Assert.Equal("Frame 1", sprite.Frames[0].Name);
            Assert.Equal("Frame 2", sprite.Frames[1].Name);
            Assert.Equal("Frame 3", sprite.Frames[2].Name);

            bool[] frame1Pixels = sprite.Frames[1].LayerPixels[0].GetMonochromeData();
            for (int i = 0; i < frame1Pixels.Length; i++)
                Assert.False(frame1Pixels[i]); // blank placeholder

            // Frame 3 (the third file) is still intact.
            bool[] frame3Pixels = sprite.Frames[2].LayerPixels[0].GetMonochromeData();
            Assert.True(frame3Pixels[0]);
        }

        [Fact]
        public void ImportAnimation_RealFlipperFixture_DecodesAllFramesWithoutBlankPlaceholders()
        {
            // Arrange — a real device-exported animation (Heatshrink-compressed .bm frames),
            // bundled at FlipperZero_TestAssets\Kuronons_Misc_Earth_Arcadia_128x64.
            string metaTxtPath = Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..",
                "FlipperZero_TestAssets", "Kuronons_Misc_Earth_Arcadia_128x64", "meta.txt");
            metaTxtPath = Path.GetFullPath(metaTxtPath);
            Assert.True(File.Exists(metaTxtPath), $"Fixture not found at {metaTxtPath}");

            // Act
            var sprite = _importService.ImportAnimation(metaTxtPath);

            // Assert — 73 real frames (frame_0.bm..frame_72.bm), none of them a blank
            // placeholder inserted because the Heatshrink decoder failed to decompress it.
            Assert.Equal(73, sprite.Frames.Count);

            for (int i = 0; i < sprite.Frames.Count; i++)
            {
                bool[] pixels = sprite.Frames[i].LayerPixels[0].GetMonochromeData();
                bool anySet = false;
                for (int p = 0; p < pixels.Length; p++)
                {
                    if (pixels[p]) { anySet = true; break; }
                }
                Assert.True(anySet, $"Frame index {i} ({sprite.Frames[i].Name}) decoded as a blank placeholder");
            }

            // A decoder that desyncs mid-stream can still produce a full-length,
            // non-blank (but garbage) frame — "not blank" alone doesn't prove
            // correctness. Consecutive real animation frames are visually similar,
            // so pixel agreement between adjacent frames is a strong signal of a
            // genuinely correct decode; real desynced/garbage output measures close
            // to 50% (random). Real measured values for this fixture: ~77-100% per
            // adjacent pair, ~86% average.
            double totalAgreement = 0;
            int pairCount = sprite.Frames.Count - 1;
            for (int i = 0; i < pairCount; i++)
            {
                bool[] a = sprite.Frames[i].LayerPixels[0].GetMonochromeData();
                bool[] b = sprite.Frames[i + 1].LayerPixels[0].GetMonochromeData();
                int same = 0;
                for (int p = 0; p < a.Length; p++)
                {
                    if (a[p] == b[p]) same++;
                }
                double agreement = (double)same / a.Length;
                Assert.True(agreement > 0.7,
                    $"Frames {i} and {i + 1} only agree on {agreement:P0} of pixels — decoder likely desynced");
                totalAgreement += agreement;
            }
            Assert.True(totalAgreement / pairCount > 0.8, "Average adjacent-frame pixel agreement too low");
        }

        [Fact]
        public void ImportAnimation_RealFlipperFixture_ParsesCycleMetadata()
        {
            // Arrange — same real dolphin-style fixture as above; its meta.txt has
            // a non-trivial Frames order (96 entries reusing 73 physical frames,
            // split into a 24-frame passive prefix and a 72-frame active suffix).
            string metaTxtPath = Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..",
                "FlipperZero_TestAssets", "Kuronons_Misc_Earth_Arcadia_128x64", "meta.txt");
            metaTxtPath = Path.GetFullPath(metaTxtPath);
            Assert.True(File.Exists(metaTxtPath), $"Fixture not found at {metaTxtPath}");

            // Act
            var sprite = _importService.ImportAnimation(metaTxtPath);

            // Assert
            Assert.NotNull(sprite.FlipperCycle);
            var cycle = sprite.FlipperCycle;
            Assert.Equal(24, cycle.PassiveFrameCount);
            Assert.Equal(72, cycle.ActiveFrameCount);
            Assert.Equal(1, cycle.ActiveCycles);
            Assert.Equal(3600, cycle.Duration);
            Assert.Equal(4, cycle.ActiveCooldown);
            Assert.Equal(0, cycle.BubbleSlots);
            Assert.Equal(96, cycle.FramesOrder.Length);
            Assert.Equal(new[] { 0, 1, 2, 3 }, cycle.FramesOrder[..4]);
            Assert.Equal(0, cycle.FramesOrder[24]); // start of the repeated 0,1,2 run
        }

        [Fact]
        public void ImportThenExportAnimation_RealFlipperFixture_RoundTripsCycleMetadataExactly()
        {
            // Arrange — import the real fixture, then re-export it.
            string metaTxtPath = Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..",
                "FlipperZero_TestAssets", "Kuronons_Misc_Earth_Arcadia_128x64", "meta.txt");
            metaTxtPath = Path.GetFullPath(metaTxtPath);
            Assert.True(File.Exists(metaTxtPath), $"Fixture not found at {metaTxtPath}");
            string[] originalLines = File.ReadAllLines(metaTxtPath);

            var sprite = _importService.ImportAnimation(metaTxtPath);

            // Act
            var settings = new FlipperExportSettings(_tempDirectory, "RoundTrip", 6, 0, 0);
            _exportService.ExportAnimation(sprite, settings);

            // Assert — the cycle-metadata lines are reproduced exactly, not flattened.
            string exportedMetaPath = Path.Combine(_tempDirectory, "RoundTrip", "meta.txt");
            string[] exportedLines = File.ReadAllLines(exportedMetaPath);

            foreach (var prefix in new[] { "Passive frames:", "Active frames:", "Frames order:", "Active cycles:", "Duration:", "Active cooldown:", "Bubble slots:" })
            {
                string? originalLine = Array.Find(originalLines, l => l.StartsWith(prefix, StringComparison.Ordinal));
                string? exportedLine = Array.Find(exportedLines, l => l.StartsWith(prefix, StringComparison.Ordinal));
                Assert.Equal(originalLine, exportedLine);
            }
        }

        [Fact]
        public void ExportAnimation_StaleCycleMetadata_FallsBackToTrivialFrameOrder()
        {
            // Arrange — import the real fixture, then simulate a user edit
            // (removing a frame) that makes the stored FramesOrder's max index
            // fall out of range for the new physical frame count.
            string metaTxtPath = Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..",
                "FlipperZero_TestAssets", "Kuronons_Misc_Earth_Arcadia_128x64", "meta.txt");
            metaTxtPath = Path.GetFullPath(metaTxtPath);
            var sprite = _importService.ImportAnimation(metaTxtPath);
            Assert.NotNull(sprite.FlipperCycle);
            sprite.Frames.RemoveAt(sprite.Frames.Count - 1);

            // Act
            var settings = new FlipperExportSettings(_tempDirectory, "StaleCycle", 6, 0, sprite.Frames.Count);
            _exportService.ExportAnimation(sprite, settings);

            // Assert — falls back to the trivial 0..N-1 order rather than writing
            // the now-invalid stored FramesOrder.
            string exportedMetaPath = Path.Combine(_tempDirectory, "StaleCycle", "meta.txt");
            string metaContent = File.ReadAllText(exportedMetaPath);
            Assert.Contains($"Frames order: {string.Join(" ", Enumerable.Range(0, sprite.Frames.Count))}", metaContent);
        }

        [Fact]
        public void Decompress_BackrefBeforeStreamStart_ReadsZeroPadding_DoesNotThrow()
        {
            // A backref referencing distance before the first output byte is valid
            // heatshrink behavior (real encoders can emit these, referencing the
            // window's zero-initialized "virtual padding") — it must not throw and
            // must not crash with IndexOutOfRangeException the way the original
            // unbounded `output[di - offset]` implementation did.
            // Bitstream: flag=0 (backref), 8 offset bits=0 → offset=1, 4 length bits=0 → length=1.
            byte[] compressed = { 0x00, 0x00 };

            byte[] result = HeatshrinkCompressor.Decompress(compressed, 1);

            Assert.Equal(new byte[] { 0x00 }, result);
        }

        [Fact]
        public void Decompress_TruncatedStream_ThrowsInvalidDataException_NotIndexOutOfRange()
        {
            byte[] compressed = { 0x00, 0x00 };
            var ex = Assert.Throws<InvalidDataException>(() => HeatshrinkCompressor.Decompress(compressed, 16));
            Assert.Contains("truncated", ex.Message);
        }

        [Fact]
        public void ImportAssetPack_ReadsAllAnimationsAndManifest()
        {
            // Arrange - Create an asset pack directory with 2 animations and manifest.txt
            string assetPackDir = Path.Combine(_tempDirectory, "MyAwesomePack");
            var anim1 = new SpriteState(128, 64);
            var anim2 = new SpriteState(128, 64);

            var entry1 = new FlipperManifestEntry { Name = "FishSwim", MinLevel = 1, MaxLevel = 5, MinButthurt = 0, MaxButthurt = 3, Weight = 2 };
            var entry2 = new FlipperManifestEntry { Name = "FishJump", MinLevel = 6, MaxLevel = 30, MinButthurt = 4, MaxButthurt = 14, Weight = 5 };

            var set1 = new FlipperExportSettings(assetPackDir, "FishSwim", 6, 1, 0, minLevel: 1, maxLevel: 5, minButthurt: 0, maxButthurt: 3, weight: 2);
            var set2 = new FlipperExportSettings(assetPackDir, "FishJump", 10, 1, 0, minLevel: 6, maxLevel: 30, minButthurt: 4, maxButthurt: 14, weight: 5);

            var items = new System.Collections.Generic.List<(SpriteState Sprite, FlipperManifestEntry ManifestEntry, FlipperExportSettings Settings)>
            {
                (anim1, entry1, set1),
                (anim2, entry2, set2)
            };

            _exportService.ExportAssetPack(items, assetPackDir, isMomentum: true);

            // Act - Import the whole asset pack
            var importedList = _importService.ImportAssetPack(assetPackDir);

            // Assert
            Assert.Equal(2, importedList.Count);
            var importedSwim = importedList.Find(x => x.Name == "FishSwim");
            var importedJump = importedList.Find(x => x.Name == "FishJump");

            Assert.NotNull(importedSwim.Sprite);
            Assert.Equal(6, importedSwim.Sprite.FrameRateFps);
            Assert.Equal(1, importedSwim.ManifestEntry.MinLevel);
            Assert.Equal(5, importedSwim.ManifestEntry.MaxLevel);

            Assert.NotNull(importedJump.Sprite);
            Assert.Equal(10, importedJump.Sprite.FrameRateFps);
            Assert.Equal(6, importedJump.ManifestEntry.MinLevel);
            Assert.Equal(30, importedJump.ManifestEntry.MaxLevel);
        }

        [Fact]
        public void ImportAnimation_WithBubbleSlots_PreservesAllBubblePropertiesInCycle()
        {
            string animFolder = Path.Combine(_tempDirectory, "BubbleImportAnim");
            Directory.CreateDirectory(animFolder);

            // Write 2 dummy frames
            File.WriteAllBytes(Path.Combine(animFolder, "frame_0.bm"), new byte[1024]);
            File.WriteAllBytes(Path.Combine(animFolder, "frame_1.bm"), new byte[1024]);

            string metaContent = @"
Filetype: Flipper Animation
Version: 1
Width: 128
Height: 64
Passive frames: 1
Active frames: 1
Frames order: 0 1
Active cycles: 2
Frame rate: 5
Duration: 3600
Active cooldown: 3
Bubble slots: 1

Slot: 0
StartFrame: 1
EndFrame: 1
Text: Hey Flipper!
X: 16
Y: 8
AlignH: Center
AlignV: Bottom
";
            string metaPath = Path.Combine(animFolder, "meta.txt");
            File.WriteAllText(metaPath, metaContent);

            var sprite = _importService.ImportAnimation(metaPath);

            Assert.NotNull(sprite.FlipperCycle);
            Assert.Equal(1, sprite.FlipperCycle.BubbleSlots);
            Assert.Single(sprite.FlipperCycle.SpeechBubbles);
            Assert.NotNull(sprite.FlipperCycle.SpeechBubble);
            Assert.Equal("Hey Flipper!", sprite.FlipperCycle.SpeechBubble.Text);
            Assert.Equal(16, sprite.FlipperCycle.SpeechBubble.X);
            Assert.Equal(8, sprite.FlipperCycle.SpeechBubble.Y);
            Assert.Equal(1, sprite.FlipperCycle.SpeechBubble.StartFrame);
            Assert.Equal(1, sprite.FlipperCycle.SpeechBubble.EndFrame);
        }

        [Fact]
        public void ImportAssetPack_FromZipArchive_ExtractsAndParsesAllAnimations()
        {
            string sourcePackDir = Path.Combine(_tempDirectory, "SourcePackForZip");
            string animFolder = Path.Combine(sourcePackDir, "Anims", "MyTestAnim");
            Directory.CreateDirectory(animFolder);

            // Write 1 frame + meta.txt
            File.WriteAllBytes(Path.Combine(animFolder, "frame_0.bm"), new byte[1024]);
            string metaContent = "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nPassive frames: 1\nActive frames: 0\nFrames order: 0\nActive cycles: 1\nFrame rate: 5\nDuration: 3600\nActive cooldown: 0\nBubble slots: 0\n";
            File.WriteAllText(Path.Combine(animFolder, "meta.txt"), metaContent);

            // Write manifest.txt
            string manifestContent = "Filetype: Flipper Animation Manifest\nVersion: 1\n\nName: MyTestAnim\nMin butthurt: 0\nMax butthurt: 14\nMin level: 1\nMax level: 30\nWeight: 5\n";
            File.WriteAllText(Path.Combine(sourcePackDir, "Anims", "manifest.txt"), manifestContent);

            // Zip it
            string zipPath = Path.Combine(_tempDirectory, "PackArchive.zip");
            System.IO.Compression.ZipFile.CreateFromDirectory(sourcePackDir, zipPath);

            var imported = _importService.ImportAssetPack(zipPath);

            Assert.Single(imported);
            Assert.Equal("MyTestAnim", imported[0].Name);
            Assert.Equal(5, imported[0].ManifestEntry.Weight);
            Assert.Single(imported[0].Sprite.Frames);
        }

        [Fact]
        public void ImportAssetPack_DeepNestedZipArchive_FindsAndImportsAnimations()
        {
            string sourcePackDir = Path.Combine(_tempDirectory, "DeepNestedPack");
            string nestedFolder = Path.Combine(sourcePackDir, "Momentum-Packs", "CyberTheme", "Anims", "CyberGlow");
            Directory.CreateDirectory(nestedFolder);

            File.WriteAllBytes(Path.Combine(nestedFolder, "frame_0.bm"), new byte[1024]);
            string metaContent = "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nPassive frames: 1\nActive frames: 0\nFrames order: 0\nActive cycles: 1\nFrame rate: 6\nDuration: 3600\nActive cooldown: 0\nBubble slots: 0\n";
            File.WriteAllText(Path.Combine(nestedFolder, "meta.txt"), metaContent);

            string manifestContent = "Filetype: Flipper Animation Manifest\nVersion: 1\n\nName: CyberGlow\nMin butthurt: 0\nMax butthurt: 14\nMin level: 1\nMax level: 30\nWeight: 8\n";
            File.WriteAllText(Path.Combine(sourcePackDir, "Momentum-Packs", "CyberTheme", "Anims", "manifest.txt"), manifestContent);

            string zipPath = Path.Combine(_tempDirectory, "DeepNestedArchive.zip");
            System.IO.Compression.ZipFile.CreateFromDirectory(sourcePackDir, zipPath);

            var imported = _importService.ImportAssetPack(zipPath);

            Assert.Single(imported);
            Assert.Equal("CyberGlow", imported[0].Name);
            Assert.Equal(8, imported[0].ManifestEntry.Weight);
        }

        [Fact]
        public void ImportAssetPack_ZeroManifestFolderFallback_DiscoversMetaTxtDirectories()
        {
            string rootDir = Path.Combine(_tempDirectory, "NoManifestFolder");
            string anim1 = Path.Combine(rootDir, "DolphinDance");
            string anim2 = Path.Combine(rootDir, "DolphinWave");
            Directory.CreateDirectory(anim1);
            Directory.CreateDirectory(anim2);

            File.WriteAllBytes(Path.Combine(anim1, "frame_0.bm"), new byte[1024]);
            File.WriteAllBytes(Path.Combine(anim2, "frame_0.bm"), new byte[1024]);

            string meta = "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nPassive frames: 1\nActive frames: 0\nFrames order: 0\nActive cycles: 1\nFrame rate: 5\nDuration: 3600\nActive cooldown: 0\nBubble slots: 0\n";
            File.WriteAllText(Path.Combine(anim1, "meta.txt"), meta);
            File.WriteAllText(Path.Combine(anim2, "meta.txt"), meta);

            var imported = _importService.ImportAssetPack(rootDir);

            Assert.Equal(2, imported.Count);
            Assert.Contains(imported, x => x.Name == "DolphinDance");
            Assert.Contains(imported, x => x.Name == "DolphinWave");
        }

        [Fact]
        public void ImportAssetPack_CaseMismatchedDirectoryNames_ResolvesCorrectly()
        {
            string packDir = Path.Combine(_tempDirectory, "CaseMismatchPack");
            string animFolder = Path.Combine(packDir, "Anims", "lowercase_folder");
            Directory.CreateDirectory(animFolder);

            File.WriteAllBytes(Path.Combine(animFolder, "frame_0.bm"), new byte[1024]);
            string meta = "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nPassive frames: 1\nActive frames: 0\nFrames order: 0\nActive cycles: 1\nFrame rate: 5\nDuration: 3600\nActive cooldown: 0\nBubble slots: 0\n";
            File.WriteAllText(Path.Combine(animFolder, "meta.txt"), meta);

            // Manifest has uppercase entry name
            string manifestContent = "Filetype: Flipper Animation Manifest\nVersion: 1\n\nName: LOWERCASE_FOLDER\nMin butthurt: 0\nMax butthurt: 14\nMin level: 1\nMax level: 30\nWeight: 3\n";
            File.WriteAllText(Path.Combine(packDir, "Anims", "manifest.txt"), manifestContent);

            var imported = _importService.ImportAssetPack(packDir);

            Assert.Single(imported);
            Assert.Equal("LOWERCASE_FOLDER", imported[0].Name);
        }

        [Fact]
        public void ImportAssetPack_SingleAnimationDirectory_ImportsDirectly()
        {
            string singleAnimFolder = Path.Combine(_tempDirectory, "StandaloneAnim");
            Directory.CreateDirectory(singleAnimFolder);

            File.WriteAllBytes(Path.Combine(singleAnimFolder, "frame_0.bm"), new byte[1024]);
            string meta = "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nPassive frames: 1\nActive frames: 0\nFrames order: 0\nActive cycles: 1\nFrame rate: 5\nDuration: 3600\nActive cooldown: 0\nBubble slots: 0\n";
            File.WriteAllText(Path.Combine(singleAnimFolder, "meta.txt"), meta);

            var imported = _importService.ImportAssetPack(singleAnimFolder);

            Assert.Single(imported);
            Assert.Equal("StandaloneAnim", imported[0].Name);
        }

        [Fact]
        public void ImportAnimation_NaturalFrameNumberSorting_OrdersNumerically()
        {
            // Arrange - Create 13 frames: frame_0.bm .. frame_12.bm with unique pixel flags
            string animFolder = Path.Combine(_tempDirectory, "NaturalSortAnim");
            Directory.CreateDirectory(animFolder);

            int width = 8;
            int height = 8;
            int bytesPerFrame = (width * height) / 8; // 8 bytes

            for (int i = 0; i <= 12; i++)
            {
                byte[] frameBytes = new byte[bytesPerFrame];
                frameBytes[0] = (byte)i; // signature
                File.WriteAllBytes(Path.Combine(animFolder, $"frame_{i}.bm"), frameBytes);
            }

            string meta = $"Filetype: Flipper Animation\nVersion: 1\nWidth: {width}\nHeight: {height}\nFrame rate: 5\n";
            File.WriteAllText(Path.Combine(animFolder, "meta.txt"), meta);

            // Act
            var sprite = _importService.ImportAnimation(Path.Combine(animFolder, "meta.txt"));

            // Assert - exactly 13 frames, ordered 0, 1, 2, ..., 9, 10, 11, 12 (not 0, 1, 10, 11, 12, 2...)
            Assert.Equal(13, sprite.Frames.Count);
            for (int i = 0; i <= 12; i++)
            {
                Assert.Equal($"Frame {i + 1}", sprite.Frames[i].Name);
                bool[] pixels = sprite.Frames[i].LayerPixels[0].GetMonochromeData();
                for (int bit = 0; bit < 8; bit++)
                {
                    bool expectedBit = (i & (1 << bit)) != 0;
                    Assert.Equal(expectedBit, pixels[bit]);
                }
            }
        }

        [Fact]
        public void ImportAnimation_NaturalFrameNumberSorting_WithGapsAndZeroPadding()
        {
            string animFolder = Path.Combine(_tempDirectory, "PaddedAndGappedAnim");
            Directory.CreateDirectory(animFolder);

            int width = 8;
            int height = 8;
            int bytesPerFrame = 8;

            int[] indices = [1, 2, 10, 100];
            foreach (var idx in indices)
            {
                byte[] frameBytes = new byte[bytesPerFrame];
                frameBytes[0] = (byte)idx;
                string filename = idx < 10 ? $"frame_0{idx}.bm" : $"frame_{idx}.bm";
                File.WriteAllBytes(Path.Combine(animFolder, filename), frameBytes);
            }

            string meta = $"Filetype: Flipper Animation\nVersion: 1\nWidth: {width}\nHeight: {height}\nFrame rate: 5\n";
            File.WriteAllText(Path.Combine(animFolder, "meta.txt"), meta);

            var sprite = _importService.ImportAnimation(Path.Combine(animFolder, "meta.txt"));

            Assert.Equal(4, sprite.Frames.Count);
            for (int i = 0; i < indices.Length; i++)
            {
                int expectedVal = indices[i];
                bool[] pixels = sprite.Frames[i].LayerPixels[0].GetMonochromeData();
                for (int bit = 0; bit < 8; bit++)
                {
                    bool expectedBit = (expectedVal & (1 << bit)) != 0;
                    Assert.Equal(expectedBit, pixels[bit]);
                }
            }
        }

        [Fact]
        public void ExtractFrameNumber_ParsesVariousNamingFormats()
        {
            Assert.Equal(0, FlipperImportService.ExtractFrameNumber("frame_0.bm"));
            Assert.Equal(12, FlipperImportService.ExtractFrameNumber("frame_12.bm"));
            Assert.Equal(7, FlipperImportService.ExtractFrameNumber("anim_007.bm"));
            Assert.Equal(42, FlipperImportService.ExtractFrameNumber("walk_cycle_42.bm"));
            Assert.Equal(int.MaxValue, FlipperImportService.ExtractFrameNumber("icon_static.bm"));
        }

        [Fact]
        public void ImportAnimation_TruncatedFrameHeader_ReturnsBlankPlaceholder_DoesNotThrow()
        {
            string animFolder = Path.Combine(_tempDirectory, "TruncatedHeaderAnim");
            Directory.CreateDirectory(animFolder);

            int width = 8;
            int height = 8;

            byte[] truncatedFrame = [0x01, 0x00, 0xF4, 0x01, 0xAA, 0xBB];
            File.WriteAllBytes(Path.Combine(animFolder, "frame_0.bm"), truncatedFrame);

            string meta = $"Filetype: Flipper Animation\nVersion: 1\nWidth: {width}\nHeight: {height}\nFrame rate: 5\n";
            File.WriteAllText(Path.Combine(animFolder, "meta.txt"), meta);

            var sprite = _importService.ImportAnimation(Path.Combine(animFolder, "meta.txt"));

            Assert.Single(sprite.Frames);
            Assert.Equal("Frame 1", sprite.Frames[0].Name);
            bool[] pixels = sprite.Frames[0].LayerPixels[0].GetMonochromeData();
            Assert.All(pixels, p => Assert.False(p));
        }

        [Fact]
        public void ImportAssetPack_MissingFolderInManifest_RetainsEntryWithPlaceholderSprite()
        {
            string packDir = Path.Combine(_tempDirectory, "ManifestFallbackPack");
            string validAnimFolder = Path.Combine(packDir, "Anims", "ValidAnim");
            string missingMetaFolder = Path.Combine(packDir, "Anims", "MissingMetaAnim");
            Directory.CreateDirectory(validAnimFolder);
            Directory.CreateDirectory(missingMetaFolder);

            File.WriteAllBytes(Path.Combine(validAnimFolder, "frame_0.bm"), new byte[1024]);
            string meta = "Filetype: Flipper Animation\nVersion: 1\nWidth: 128\nHeight: 64\nPassive frames: 1\nActive frames: 0\nFrames order: 0\nActive cycles: 1\nFrame rate: 5\nDuration: 3600\nActive cooldown: 0\nBubble slots: 0\n";
            File.WriteAllText(Path.Combine(validAnimFolder, "meta.txt"), meta);

            string manifestContent = @"Filetype: Flipper Animation Manifest
Version: 1

Name: ValidAnim
Min butthurt: 0
Max butthurt: 3
Min level: 1
Max level: 5
Weight: 3

Name: MissingFolderAnim
Min butthurt: 4
Max butthurt: 8
Min level: 6
Max level: 15
Weight: 4

Name: MissingMetaAnim
Min butthurt: 9
Max butthurt: 14
Min level: 16
Max level: 30
Weight: 5
";
            File.WriteAllText(Path.Combine(packDir, "Anims", "manifest.txt"), manifestContent);

            var imported = _importService.ImportAssetPack(packDir);

            Assert.Equal(3, imported.Count);

            var valid = imported.Find(x => x.Name == "ValidAnim");
            Assert.NotNull(valid.Sprite);
            Assert.Equal(1, valid.ManifestEntry.MinLevel);
            Assert.Equal(5, valid.ManifestEntry.MaxLevel);

            var missingFolder = imported.Find(x => x.Name == "MissingFolderAnim");
            Assert.NotNull(missingFolder.Sprite);
            Assert.Equal(6, missingFolder.ManifestEntry.MinLevel);
            Assert.Equal(15, missingFolder.ManifestEntry.MaxLevel);
            Assert.Equal(128, missingFolder.Sprite.Width);
            Assert.Equal(64, missingFolder.Sprite.Height);
            Assert.Single(missingFolder.Sprite.Frames);

            var missingMeta = imported.Find(x => x.Name == "MissingMetaAnim");
            Assert.NotNull(missingMeta.Sprite);
            Assert.Equal(16, missingMeta.ManifestEntry.MinLevel);
            Assert.Equal(30, missingMeta.ManifestEntry.MaxLevel);
            Assert.Equal(128, missingMeta.Sprite.Width);
            Assert.Equal(64, missingMeta.Sprite.Height);
            Assert.Single(missingMeta.Sprite.Frames);
        }
    }
}
