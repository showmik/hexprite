using System;
using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FontCodeGeneratorServiceTests
    {
        private FontDocument CreateTestDocument()
        {
            var doc = FontDocument.CreateNew(8, 8, 65, 65); // Just 'A'
            doc.FontName = "TestFont";
            doc.YAdvance = 10;
            
            // Set some pixels for 'A'
            var glyph = doc.Glyphs[0];
            glyph.Pixels[0] = true; // Top-left pixel
            glyph.XAdvance = 6;
            
            return doc;
        }

        [Fact]
        public void GenerateCode_AdafruitGfx_ProducesExpectedFormat()
        {
            var service = new FontCodeGeneratorService();
            var doc = CreateTestDocument();
            var settings = new FontExportSettings
            {
                Format = FontExportFormat.AdafruitGfx,
                FontName = "TestFont",
                IncludeUsageComment = true,
                IncludeGlyphPreview = true,
                IncludeMetricComments = true
            };

            string code = service.GenerateCode(doc, settings);

            Assert.Contains("const uint8_t TestFontBitmaps[] PROGMEM = {", code);
            Assert.Contains("const GFXglyph TestFontGlyphs[] PROGMEM = {", code);
            Assert.Contains("const GFXfont TestFont PROGMEM = {", code);
            Assert.Contains("TestFontBitmaps,", code);
            Assert.Contains("TestFontGlyphs,", code);
            Assert.Contains("0x41, 0x41, 10", code); // FirstChar, LastChar, YAdvance
            // 0x41 is 65 which is 'A'
        }

        [Fact]
        public void GenerateCode_Lvgl_ProducesExpectedFormat()
        {
            var service = new FontCodeGeneratorService();
            var doc = CreateTestDocument();
            var settings = new FontExportSettings
            {
                Format = FontExportFormat.Lvgl,
                FontName = "TestFont"
            };

            string code = service.GenerateCode(doc, settings);

            Assert.Contains("#include \"lvgl.h\"", code);
            Assert.Contains("static const uint8_t TestFont_glyph_bitmap[] = {", code);
            Assert.Contains("static const lv_font_fmt_txt_glyph_dsc_t TestFont_glyph_dsc[] = {", code);
            Assert.Contains("static const lv_font_fmt_txt_cmap_t TestFont_cmap[] = {", code);
            Assert.Contains("const lv_font_t TestFont = {", code);
            Assert.Contains(".line_height = 10", code);
        }

        [Fact]
        public void GenerateCode_U8g2Bdf_ProducesExpectedFormat()
        {
            var service = new FontCodeGeneratorService();
            var doc = CreateTestDocument();
            var settings = new FontExportSettings
            {
                Format = FontExportFormat.U8g2Bdf,
                FontName = "TestFont"
            };

            string code = service.GenerateCode(doc, settings);

            Assert.Contains("STARTFONT 2.1", code);
            Assert.Contains("CHARS 1", code);
            Assert.Contains("STARTCHAR A", code);
            Assert.Contains("ENCODING 65", code);
            Assert.Contains("BITMAP", code);
            Assert.Contains("ENDCHAR", code);
            Assert.Contains("ENDFONT", code);
        }

        [Fact]
        public void GenerateCode_RawCArray_ProducesExpectedFormat()
        {
            var service = new FontCodeGeneratorService();
            var doc = CreateTestDocument();
            var settings = new FontExportSettings
            {
                Format = FontExportFormat.RawCArray,
                FontName = "TestFont"
            };

            string code = service.GenerateCode(doc, settings);

            Assert.Contains("TestFont_41[] = {", code);
        }

        [Fact]
        public void GenerateCode_AdafruitGfx_NonContiguousGlyphs_EmitsAllSlotsInRange()
        {
            // BUG-CG-07: When Glyphs has gaps (e.g. 65 'A' and 67 'C', missing 66 'B'),
            // Adafruit GFX requires GFXglyph array to contain all entries in [FirstChar..LastChar]
            // so c - first indexing works correctly.
            var service = new FontCodeGeneratorService();
            var doc = new FontDocument
            {
                CellHeight = 8,
                MaxCellWidth = 8,
                FirstChar = 65,
                LastChar = 67,
                FontName = "TestFont",
                Glyphs =
                [
                    new GlyphState { CodePoint = 65, Width = 8, Height = 8, Pixels = new bool[64], XAdvance = 8 },
                    new GlyphState { CodePoint = 67, Width = 8, Height = 8, Pixels = new bool[64], XAdvance = 8 },
                ]
            };

            var settings = new FontExportSettings
            {
                Format = FontExportFormat.AdafruitGfx,
                FontName = "TestFont",
                IncludeMetricComments = true,
            };

            string code = service.GenerateCode(doc, settings);

            // Should have 3 entries in TestFontGlyphs for 65 ('A'), 66 ('B' - placeholder), 67 ('C')
            Assert.Contains("0x41 'A'", code);
            Assert.Contains("0x42 'B'", code);
            Assert.Contains("0x43 'C'", code);
        }

        [Fact]
        public void GenerateCode_RawCArray_NonContiguousGlyphs_EmitsAllSlotsInRange()
        {
            // BUG-CG-07: Raw C Array bitmaps array requires all entries in [FirstChar..LastChar]
            var service = new FontCodeGeneratorService();
            var doc = new FontDocument
            {
                CellHeight = 8,
                MaxCellWidth = 8,
                FirstChar = 65,
                LastChar = 67,
                FontName = "TestFont",
                Glyphs =
                [
                    new GlyphState { CodePoint = 65, Width = 8, Height = 8, Pixels = new bool[64], XAdvance = 8 },
                    new GlyphState { CodePoint = 67, Width = 8, Height = 8, Pixels = new bool[64], XAdvance = 8 },
                ]
            };

            var settings = new FontExportSettings
            {
                Format = FontExportFormat.RawCArray,
                FontName = "TestFont",
                IncludeMetricComments = true,
            };

            string code = service.GenerateCode(doc, settings);

            // Should have 3 entries in TestFont_bitmaps
            Assert.Contains("TestFont_41", code);
            Assert.Contains("TestFont_42", code);
            Assert.Contains("TestFont_43", code);
        }
    }
}

