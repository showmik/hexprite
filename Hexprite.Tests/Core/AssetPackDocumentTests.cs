using System.Collections.Generic;
using System.Text.Json;
using Hexprite.Core;
using Xunit;

namespace Hexprite.Tests.Core
{
    [Trait("Category", "Unit")]
    public class AssetPackDocumentTests
    {
        [Fact]
        public void CreateNew_ReturnsValidDefaults()
        {
            var doc = AssetPackDocument.CreateNew("Custom Pack");

            Assert.Equal("Custom Pack", doc.PackName);
            Assert.False(doc.IsStockMode);
            Assert.Equal(3, doc.Entries.Count);
            Assert.Equal("anim_baby", doc.Entries[0].Name);
            Assert.Equal("anim_teen", doc.Entries[1].Name);
            Assert.Equal("anim_adult", doc.Entries[2].Name);
        }

        [Fact]
        public void FromManifest_ConvertsEntriesAndDetectsStockMode()
        {
            var manifest = new FlipperManifest
            {
                Entries =
                [
                    new FlipperManifestEntry { Name = "a1", MinLevel = 1, MaxLevel = 3, MinButthurt = 0, MaxButthurt = 5, Weight = 2 },
                    new FlipperManifestEntry { Name = "a2", MinLevel = 2, MaxLevel = 3, MinButthurt = 6, MaxButthurt = 14, Weight = 1 }
                ]
            };

            var doc = AssetPackDocument.FromManifest(manifest, "Stock Pack");

            Assert.Equal("Stock Pack", doc.PackName);
            Assert.True(doc.IsStockMode);
            Assert.Equal(2, doc.Entries.Count);
            Assert.Equal("a1", doc.Entries[0].Name);
            Assert.Equal(2, doc.Entries[0].Weight);
        }

        [Fact]
        public void FromManifest_DetectsExtendedMode_WhenMaxLevelAbove3()
        {
            var manifest = new FlipperManifest
            {
                Entries =
                [
                    new FlipperManifestEntry { Name = "a1", MinLevel = 1, MaxLevel = 15, MinButthurt = 0, MaxButthurt = 14, Weight = 1 },
                    new FlipperManifestEntry { Name = "a2", MinLevel = 16, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 }
                ]
            };

            var doc = AssetPackDocument.FromManifest(manifest);

            Assert.Equal("Imported Pack", doc.PackName);
            Assert.False(doc.IsStockMode);
            Assert.Equal(2, doc.Entries.Count);
        }

        [Fact]
        public void JsonSerialization_Roundtrip_PreservesAllData()
        {
            var original = new AssetPackDocument
            {
                PackName = "Roundtrip Pack",
                IsStockMode = true,
                Entries =
                [
                    new FlipperManifestEntry { Name = "anim_test", MinLevel = 1, MaxLevel = 3, MinButthurt = 2, MaxButthurt = 8, Weight = 5 }
                ]
            };

            string json = JsonSerializer.Serialize(original);
            var restored = JsonSerializer.Deserialize<AssetPackDocument>(json);

            Assert.NotNull(restored);
            Assert.Equal(original.PackName, restored.PackName);
            Assert.Equal(original.IsStockMode, restored.IsStockMode);
            Assert.Single(restored.Entries);
            Assert.Equal("anim_test", restored.Entries[0].Name);
            Assert.Equal(1, restored.Entries[0].MinLevel);
            Assert.Equal(3, restored.Entries[0].MaxLevel);
            Assert.Equal(2, restored.Entries[0].MinButthurt);
            Assert.Equal(8, restored.Entries[0].MaxButthurt);
            Assert.Equal(5, restored.Entries[0].Weight);
        }

        [Fact]
        public void SerializedPixelLayer_BitPackedData_RoundTrips_ExactPixels()
        {
            var buffer = new MonochromePixelBuffer(128 * 64);
            var raw = buffer.GetMonochromeData();
            // Set some pattern pixels
            for (int i = 0; i < 128 * 64; i += 3)
            {
                raw[i] = true;
            }

            var serialized = SerializedPixelLayer.From(buffer);
            Assert.Null(serialized.Pixels);
            Assert.False(string.IsNullOrEmpty(serialized.BitPackedData));
            Assert.Equal(128 * 64, serialized.PixelCount);

            // JSON serialize
            string json = JsonSerializer.Serialize(serialized);
            Assert.DoesNotContain("\"Pixels\"", json);
            Assert.Contains("\"Data\"", json);

            var deserialized = JsonSerializer.Deserialize<SerializedPixelLayer>(json);
            Assert.NotNull(deserialized);
            var restoredBuffer = deserialized.ToPixelBuffer();

            var origPixels = buffer.GetMonochromeData();
            var restPixels = restoredBuffer.GetMonochromeData();
            Assert.Equal(origPixels.Length, restPixels.Length);
            for (int i = 0; i < origPixels.Length; i++)
            {
                Assert.Equal(origPixels[i], restPixels[i]);
            }
        }

        [Fact]
        public void SerializedPixelLayer_LegacyBoolArray_DeserializesCorrectly()
        {
            // Legacy JSON format with raw bool array
            string legacyJson = "{\"Pixels\":[true,false,true,true],\"PreserveOverflow\":false,\"CanvasWidth\":2,\"CanvasHeight\":2}";

            var deserialized = JsonSerializer.Deserialize<SerializedPixelLayer>(legacyJson);
            Assert.NotNull(deserialized);
            Assert.NotNull(deserialized.Pixels);
            Assert.Equal(4, deserialized.Pixels.Length);

            var buffer = deserialized.ToPixelBuffer();
            var pixels = buffer.GetMonochromeData();
            Assert.Equal(4, pixels.Length);
            Assert.True(pixels[0]);
            Assert.False(pixels[1]);
            Assert.True(pixels[2]);
            Assert.True(pixels[3]);
        }

        [Fact]
        public void SafeFileIo_TransparentGZip_ReadsBothCompressedAndPlainFiles()
        {
            string tempCompressed = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"test_gz_{System.Guid.NewGuid():N}.txt");
            string tempPlain = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"test_plain_{System.Guid.NewGuid():N}.txt");

            string sampleText = "Hello Flipper Zero Asset Pack!";

            try
            {
                // Write compressed
                SafeFileIo.WriteCompressedTextAtomic(tempCompressed, sampleText);
                // Write plain
                SafeFileIo.WriteAllTextAtomic(tempPlain, sampleText);

                // Both should read identically via ReadAllTextWithRetry
                string readCompressed = SafeFileIo.ReadAllTextWithRetry(tempCompressed);
                string readPlain = SafeFileIo.ReadAllTextWithRetry(tempPlain);

                Assert.Equal(sampleText, readCompressed);
                Assert.Equal(sampleText, readPlain);
            }
            finally
            {
                if (System.IO.File.Exists(tempCompressed)) System.IO.File.Delete(tempCompressed);
                if (System.IO.File.Exists(tempPlain)) System.IO.File.Delete(tempPlain);
            }
        }

        [Fact]
        public void AssetPackViewModel_LargePack_BitPackedSerialization_ReducesSize()
        {
            string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"large_pack_test_{System.Guid.NewGuid():N}.hexpack");
            string tempGzFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"large_pack_test_gz_{System.Guid.NewGuid():N}.hexpack");

            try
            {
                var doc = AssetPackDocument.CreateNew("LargeTestPack");
                doc.Entries.Clear();
                doc.Animations.Clear();

                // Create 30 animations with 5 frames each = 150 frames total
                for (int a = 0; a < 30; a++)
                {
                    string animName = $"anim_{a:D2}";
                    var entry = new FlipperManifestEntry { Name = animName, MinLevel = a + 1, MaxLevel = a + 1, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
                    doc.Entries.Add(entry);

                    var sprite = new SpriteState(128, 64) { FrameRateFps = 10 };
                    for (int f = 1; f < 5; f++)
                    {
                        var frame = new FrameState { Name = $"Frame {f + 1}" };
                        var buf = new MonochromePixelBuffer(128 * 64);
                        buf.GetMonochromeData()[a * 4 + f * 10 * 128] = true;
                        frame.LayerPixels.Add(buf);
                        sprite.Frames.Add(frame);
                    }
                    doc.Animations[animName] = sprite;
                }

                // Phase 1: Bit-packed JSON (plain text)
                var vm = new Hexprite.ViewModels.AssetPackViewModel(doc);
                vm.SaveToPath(tempFile);

                Assert.True(System.IO.File.Exists(tempFile));
                var fileInfo = new System.IO.FileInfo(tempFile);

                // Bit-packed Base64 should be much smaller than legacy bool[] serialization.
                // Legacy would be ~50MB+ for 150 frames. Bit-packed JSON is ~4MB (metadata overhead).
                Assert.True(fileInfo.Length < 10_000_000, $"Expected bit-packed file size < 10 MB, but was {fileInfo.Length} bytes.");

                // Phase 2: GZip compressed version should be under 500 KB
                string json = System.IO.File.ReadAllText(tempFile);
                SafeFileIo.WriteCompressedTextAtomic(tempGzFile, json);
                var gzFileInfo = new System.IO.FileInfo(tempGzFile);
                Assert.True(gzFileInfo.Length < 500_000, $"Expected GZip file size < 500 KB, but was {gzFileInfo.Length} bytes.");

                // Verify roundtrip read of GZip-compressed file
                string readJson = SafeFileIo.ReadAllTextWithRetry(tempGzFile);
                var restoredDoc = JsonSerializer.Deserialize<AssetPackDocument>(readJson);
                Assert.NotNull(restoredDoc);
                Assert.Equal(30, restoredDoc.Entries.Count);
                Assert.Equal(30, restoredDoc.Animations.Count);

                var restoredVm = new Hexprite.ViewModels.AssetPackViewModel(restoredDoc);
                Assert.Equal(30, restoredVm.MatrixViewModel.Entries.Count);
                Assert.Equal(30, restoredVm.MatrixViewModel.AnimationSprites.Count);
            }
            finally
            {
                if (System.IO.File.Exists(tempFile)) System.IO.File.Delete(tempFile);
                if (System.IO.File.Exists(tempGzFile)) System.IO.File.Delete(tempGzFile);
            }
        }
    }
}
