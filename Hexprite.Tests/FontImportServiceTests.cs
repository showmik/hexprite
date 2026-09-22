using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FontImportServiceTests
    {
        private static string CreateTestPng(int width, int height, Action<WriteableBitmap>? draw = null)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"hexprite_test_{Guid.NewGuid():N}.png");
            var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);

            byte[] pixels = new byte[width * height * 4];
            // default fill with white transparent/opaque
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 0;       // B
                pixels[i + 1] = 0;   // G
                pixels[i + 2] = 0;   // R
                pixels[i + 3] = 255; // A
            }
            wb.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), pixels, width * 4, 0);

            draw?.Invoke(wb);

            using var fs = File.OpenWrite(tempPath);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(wb));
            encoder.Save(fs);

            return tempPath;
        }

        [Fact]
        public void ImportFromBMFont_XmlFormat_ThrowsException()
        {
            var service = new FontImportService();
            var tempFnt = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.fnt");
            File.WriteAllText(tempFnt, "<?xml version=\"1.0\"?>\n<font></font>");

            try
            {
                var ex = Assert.Throws<NotSupportedException>(() => service.ImportFromBMFont(tempFnt, new FontImportOptions()));
                Assert.Contains("XML-based BMFont files are not currently supported", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (File.Exists(tempFnt)) File.Delete(tempFnt);
            }
        }

        [Fact]
        public void ImportFromBMFont_MissingTexture_ThrowsException()
        {
            var service = new FontImportService();
            var tempFnt = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.fnt");
            File.WriteAllText(tempFnt, "info face=\"Test\"\ncommon lineHeight=16 base=12\npage id=0 file=\"non_existent_texture_xyz.png\"\nchar id=65 x=0 y=0 width=8 height=8 xoffset=0 yoffset=0 xadvance=8");

            try
            {
                var ex = Assert.Throws<FileNotFoundException>(() => service.ImportFromBMFont(tempFnt, new FontImportOptions()));
                Assert.Contains("texture file not found", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (File.Exists(tempFnt)) File.Delete(tempFnt);
            }
        }

        [Fact]
        public void ImportFromBMFont_NoTextureReference_ThrowsException()
        {
            var service = new FontImportService();
            var tempFnt = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.fnt");
            File.WriteAllText(tempFnt, "info face=\"Test\"\ncommon lineHeight=16 base=12\nchar id=65 x=0 y=0 width=8 height=8 xoffset=0 yoffset=0 xadvance=8");

            try
            {
                var ex = Assert.Throws<InvalidDataException>(() => service.ImportFromBMFont(tempFnt, new FontImportOptions()));
                Assert.Contains("Could not find texture file reference", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (File.Exists(tempFnt)) File.Delete(tempFnt);
            }
        }

        [Fact]
        public void ImportFromBMFont_ValidFile_ParsesGlyphsCorrectly()
        {
            var service = new FontImportService();
            var dir = Path.Combine(Path.GetTempPath(), $"bmfont_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);

            var texName = "font_atlas.png";
            var texPath = Path.Combine(dir, texName);

            // Create 32x32 texture
            var wb = new WriteableBitmap(32, 32, 96, 96, PixelFormats.Bgra32, null);
            byte[] raw = new byte[32 * 32 * 4];
            // Black foreground pixel at (2, 2)
            int idx = (2 * 32 + 2) * 4;
            raw[idx] = 0; raw[idx + 1] = 0; raw[idx + 2] = 0; raw[idx + 3] = 255;
            wb.WritePixels(new System.Windows.Int32Rect(0, 0, 32, 32), raw, 32 * 4, 0);

            using (var fs = File.OpenWrite(texPath))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(wb));
                encoder.Save(fs);
            }

            var fntContent = $@"info face=""TestFont"" size=16
common lineHeight=16 base=12
page id=0 file=""{texName}""
char id=65 x=0 y=0 width=8 height=10 xoffset=1 yoffset=2 xadvance=9
char id=66 x=8 y=0 width=8 height=10 xoffset=0 yoffset=1 xadvance=8";

            var fntPath = Path.Combine(dir, "test.fnt");
            File.WriteAllText(fntPath, fntContent);

            try
            {
                var options = new FontImportOptions
                {
                    Threshold = 128,
                    BaselineOffset = 1,
                    LetterSpacing = 2
                };

                var doc = service.ImportFromBMFont(fntPath, options);

                Assert.NotNull(doc);
                Assert.Equal(16, doc.CellHeight);
                Assert.Equal(8, doc.MaxCellWidth);
                Assert.Equal(65, doc.FirstChar);
                Assert.Equal(66, doc.LastChar);
                Assert.Equal(13, doc.Baseline); // base (12) + offset (1)
                Assert.Equal(2, doc.Glyphs.Count);

                var glyphA = doc.Glyphs.First(g => g.CodePoint == 65);
                Assert.Equal(8, glyphA.Width);
                Assert.Equal(16, glyphA.Height); // Normalized to doc.CellHeight
                Assert.Equal(1, glyphA.XOffset);
                Assert.Equal(2, glyphA.YOffset);
                Assert.Equal(11, glyphA.XAdvance); // 9 + letterSpacing(2)
                Assert.True(glyphA.IsCustomized);
                Assert.True(glyphA.Pixels[2 * 8 + 2]); // pixel at (2,2) should be on
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    try { Directory.Delete(dir, true); } catch { }
                }
            }
        }

        [Fact]
        public void ImportFromSpriteSheet_SlicesGridAndSetsProperties()
        {
            var service = new FontImportService();
            var pngPath = CreateTestPng(32, 16);

            try
            {
                var options = new FontImportOptions
                {
                    FirstChar = 65,
                    LastChar = 66,
                    Threshold = 128,
                    BaselineOffset = 2,
                    LetterSpacing = 1
                };

                var doc = service.ImportFromSpriteSheet(pngPath, 16, 16, options);

                Assert.NotNull(doc);
                Assert.Equal(16, doc.CellHeight);
                Assert.Equal(16, doc.MaxCellWidth);
                Assert.Equal(65, doc.FirstChar);
                Assert.Equal(66, doc.LastChar);
                Assert.Equal(14, doc.Baseline); // 16 * 0.75 (12) + 2 = 14
                Assert.Equal(2, doc.Glyphs.Count);

                var firstGlyph = doc.Glyphs[0];
                Assert.Equal(65, firstGlyph.CodePoint);
                Assert.Equal(16, firstGlyph.Width);
                Assert.Equal(16, firstGlyph.Height);
            }
            finally
            {
                if (File.Exists(pngPath)) File.Delete(pngPath);
            }
        }

        [Fact]
        public void ImportFromTrueType_GeneratesValidFontDocument()
        {
            var service = new FontImportService();
            var fontFamily = new FontFamily("Arial");
            var options = new FontImportOptions
            {
                FirstChar = 65,
                LastChar = 67,
                TargetHeight = 12,
                Threshold = 128,
                AntiAlias = false,
                BaselineOffset = 0,
                LetterSpacing = 1
            };

            var doc = service.ImportFromTrueType(fontFamily, options);

            Assert.NotNull(doc);
            Assert.True(doc.CellHeight >= 12);
            Assert.Equal(65, doc.FirstChar);
            Assert.Equal(67, doc.LastChar);
            Assert.Equal(3, doc.Glyphs.Count);
            Assert.All(doc.Glyphs, g => Assert.True(g.Pixels.Length == g.Width * g.Height));
        }

        [Fact]
        public void ImportFromBMFont_EmptyCharDefinitions_ThrowsInvalidDataException()
        {
            var service = new FontImportService();
            var dir = Path.Combine(Path.GetTempPath(), $"bmfont_empty_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);

            var texName = "font_atlas.png";
            var texPath = Path.Combine(dir, texName);
            File.WriteAllBytes(texPath, new byte[100]); // Dummy file

            var fntContent = $@"info face=""TestFont"" size=16
common lineHeight=16 base=12
page id=0 file=""{texName}""";

            var fntPath = Path.Combine(dir, "empty.fnt");
            File.WriteAllText(fntPath, fntContent);

            try
            {
                var ex = Assert.Throws<InvalidDataException>(() => service.ImportFromBMFont(fntPath, new FontImportOptions()));
                Assert.Contains("No character definitions found", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    try { Directory.Delete(dir, true); } catch { }
                }
            }
        }

        [Fact]
        public void ImportFromSpriteSheet_ZeroOrNegativeCellDimensions_ClampsSafely()
        {
            var service = new FontImportService();
            var pngPath = CreateTestPng(32, 16);

            try
            {
                var options = new FontImportOptions
                {
                    FirstChar = 65,
                    LastChar = 66,
                    Threshold = 128,
                };

                // cellWidth 0, cellHeight -5 should clamp to 1
                var doc = service.ImportFromSpriteSheet(pngPath, 0, -5, options);

                Assert.NotNull(doc);
                Assert.Equal(1, doc.CellHeight);
                Assert.Equal(1, doc.MaxCellWidth);
            }
            finally
            {
                if (File.Exists(pngPath)) File.Delete(pngPath);
            }
        }

        [Fact]
        public void ImportFromSpriteSheet_WhiteOnOpaqueBgra32_DoesNotTreatWhiteAsInk()
        {
            var service = new FontImportService();
            // Create a PNG that is all pure white (255, 255, 255, 255)
            var tempPath = Path.Combine(Path.GetTempPath(), $"hexprite_white_{Guid.NewGuid():N}.png");
            var wb = new WriteableBitmap(16, 16, 96, 96, PixelFormats.Bgra32, null);
            byte[] pixels = new byte[16 * 16 * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;     // B
                pixels[i + 1] = 255; // G
                pixels[i + 2] = 255; // R
                pixels[i + 3] = 255; // A (opaque white)
            }
            wb.WritePixels(new System.Windows.Int32Rect(0, 0, 16, 16), pixels, 16 * 4, 0);
            using (var fs = File.OpenWrite(tempPath))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(wb));
                encoder.Save(fs);
            }

            try
            {
                var options = new FontImportOptions
                {
                    FirstChar = 65,
                    LastChar = 65,
                    Threshold = 128
                };

                var doc = service.ImportFromSpriteSheet(tempPath, 16, 16, options);
                Assert.NotNull(doc);
                Assert.Single(doc.Glyphs);
                // All pixels were white, so no pixels should be ink
                Assert.DoesNotContain(true, doc.Glyphs[0].Pixels);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void ImportFromSpriteSheet_BlackOnWhiteOpaque_CorrectlyIdentifiesInkAndBackground()
        {
            var service = new FontImportService();
            // Create a PNG with opaque white background and black ink at (2, 3)
            var tempPath = Path.Combine(Path.GetTempPath(), $"hexprite_bw_{Guid.NewGuid():N}.png");
            var wb = new WriteableBitmap(16, 16, 96, 96, PixelFormats.Bgra32, null);
            byte[] pixels = new byte[16 * 16 * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;     // B
                pixels[i + 1] = 255; // G
                pixels[i + 2] = 255; // R
                pixels[i + 3] = 255; // A
            }
            // Draw black pixel at (2, 3)
            int inkIdx = (3 * 16 + 2) * 4;
            pixels[inkIdx] = 0;
            pixels[inkIdx + 1] = 0;
            pixels[inkIdx + 2] = 0;
            pixels[inkIdx + 3] = 255;

            wb.WritePixels(new System.Windows.Int32Rect(0, 0, 16, 16), pixels, 16 * 4, 0);
            using (var fs = File.OpenWrite(tempPath))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(wb));
                encoder.Save(fs);
            }

            try
            {
                var options = new FontImportOptions
                {
                    FirstChar = 65,
                    LastChar = 65,
                    Threshold = 128
                };

                var doc = service.ImportFromSpriteSheet(tempPath, 16, 16, options);
                Assert.NotNull(doc);
                Assert.Single(doc.Glyphs);
                var glyph = doc.Glyphs[0];
                Assert.True(glyph.Pixels[3 * 16 + 2], "Black pixel at (2, 3) must be detected as ink on white background");
                // Corner pixel at (0, 0) should NOT be ink
                Assert.False(glyph.Pixels[0], "White background pixel must not be detected as ink");
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void ImportFromSpriteSheet_WhiteOnTransparent_CorrectlyIdentifiesInkAndBackground()
        {
            var service = new FontImportService();
            // Create a PNG with transparent background and white ink at (4, 4)
            var tempPath = Path.Combine(Path.GetTempPath(), $"hexprite_trans_{Guid.NewGuid():N}.png");
            var wb = new WriteableBitmap(16, 16, 96, 96, PixelFormats.Bgra32, null);
            byte[] pixels = new byte[16 * 16 * 4]; // All zeros (transparent)
            // White ink at (4, 4)
            int inkIdx = (4 * 16 + 4) * 4;
            pixels[inkIdx] = 255;
            pixels[inkIdx + 1] = 255;
            pixels[inkIdx + 2] = 255;
            pixels[inkIdx + 3] = 255;

            wb.WritePixels(new System.Windows.Int32Rect(0, 0, 16, 16), pixels, 16 * 4, 0);
            using (var fs = File.OpenWrite(tempPath))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(wb));
                encoder.Save(fs);
            }

            try
            {
                var options = new FontImportOptions
                {
                    FirstChar = 65,
                    LastChar = 65,
                    Threshold = 128
                };

                var doc = service.ImportFromSpriteSheet(tempPath, 16, 16, options);
                Assert.NotNull(doc);
                Assert.Single(doc.Glyphs);
                var glyph = doc.Glyphs[0];
                Assert.True(glyph.Pixels[4 * 16 + 4], "Opaque white pixel must be detected as ink on transparent background");
                Assert.False(glyph.Pixels[0], "Transparent pixel must not be detected as ink");
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void ImportFromBMFont_WhiteOnOpaqueBgra32_DoesNotTreatWhiteAsInk()
        {
            var service = new FontImportService();
            var dir = Path.Combine(Path.GetTempPath(), $"bmfont_white_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);

            var texName = "font_atlas_white.png";
            var texPath = Path.Combine(dir, texName);

            // Create 32x32 texture filled with pure white
            var wb = new WriteableBitmap(32, 32, 96, 96, PixelFormats.Bgra32, null);
            byte[] raw = new byte[32 * 32 * 4];
            for (int i = 0; i < raw.Length; i += 4)
            {
                raw[i] = 255; raw[i + 1] = 255; raw[i + 2] = 255; raw[i + 3] = 255;
            }
            // Draw one black pixel at (2, 2)
            int idx = (2 * 32 + 2) * 4;
            raw[idx] = 0; raw[idx + 1] = 0; raw[idx + 2] = 0; raw[idx + 3] = 255;

            wb.WritePixels(new System.Windows.Int32Rect(0, 0, 32, 32), raw, 32 * 4, 0);

            using (var fs = File.OpenWrite(texPath))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(wb));
                encoder.Save(fs);
            }

            var fntContent = $@"info face=""TestFont"" size=16
common lineHeight=16 base=12
page id=0 file=""{texName}""
char id=65 x=0 y=0 width=8 height=8 xoffset=0 yoffset=0 xadvance=8";

            var fntPath = Path.Combine(dir, "test_white.fnt");
            File.WriteAllText(fntPath, fntContent);

            try
            {
                var options = new FontImportOptions { Threshold = 128 };
                var doc = service.ImportFromBMFont(fntPath, options);

                Assert.NotNull(doc);
                var glyphA = doc.Glyphs.First(g => g.CodePoint == 65);
                Assert.True(glyphA.Pixels[2 * 8 + 2], "Black pixel at (2, 2) should be ink");
                Assert.False(glyphA.Pixels[0], "White background pixel must not be ink");
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    try { Directory.Delete(dir, true); } catch { }
                }
            }
        }
    }
}
