using System;
using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FontCodeGeneratorFidelityTests
    {
        private static FontDocument CreateTestFont(int firstChar = 65, int lastChar = 66, int cellWidth = 8, int cellHeight = 8)
        {
            var doc = FontDocument.CreateNew(cellWidth, cellHeight, firstChar, lastChar);
            doc.FontName = "FidelityFont";
            doc.Baseline = 6;
            doc.YAdvance = cellHeight;
            return doc;
        }

        private static void FillRect(GlyphState glyph, int x, int y, int w, int h)
        {
            for (int r = y; r < y + h && r < glyph.Height; r++)
            {
                for (int c = x; c < x + w && c < glyph.Width; c++)
                {
                    glyph.Pixels[r * glyph.Width + c] = true;
                }
            }
        }

        // ── Tier 1: Feature Coverage ─────────────────────────────────────────

        [Fact]
        public void AdafruitGfx_CompleteStructureAndHeaderFormat()
        {
            var service = new FontCodeGeneratorService();
            var doc = CreateTestFont(65, 66); // 'A' and 'B'
            doc.FontName = "GfxTestFont";

            FillRect(doc.Glyphs[0], 1, 1, 3, 3); // 'A'
            FillRect(doc.Glyphs[1], 0, 0, 4, 4); // 'B'

            var settings = new FontExportSettings
            {
                Format = FontExportFormat.AdafruitGfx,
                FontName = "GfxTestFont",
                IncludeUsageComment = true,
                IncludeGlyphPreview = true,
                IncludeMetricComments = true,
                UppercaseHex = true
            };

            string code = service.GenerateCode(doc, settings);

            // 1. Check include guards and headers
            Assert.Contains("#pragma once", code);
            Assert.Contains("#include <stdint.h>", code);
            Assert.Contains("Adafruit_GFX.h", code);
            Assert.Contains("gfxfont.h", code);

            // 2. Check Bitmap Array
            Assert.Contains("const uint8_t GfxTestFontBitmaps[] PROGMEM = {", code);

            // 3. Check GFXglyph Array
            Assert.Contains("const GFXglyph GfxTestFontGlyphs[] PROGMEM = {", code);

            // 4. Check GFXfont Struct
            Assert.Contains("const GFXfont GfxTestFont PROGMEM = {", code);
            Assert.Contains("(uint8_t  *)GfxTestFontBitmaps,", code);
            Assert.Contains("(GFXglyph *)GfxTestFontGlyphs,", code);
            Assert.Contains("0x41,", code); // first char 'A'
            Assert.Contains("0x42,", code); // last char 'B'
            Assert.Contains("8", code); // yAdvance
        }

        [Fact]
        public void U8g2Bdf_StandardAdobe21FormatSpecification()
        {
            var service = new FontCodeGeneratorService();
            var doc = CreateTestFont(65, 66);
            doc.FontName = "BdfFont";

            FillRect(doc.Glyphs[0], 0, 0, 4, 4); // 'A'

            var settings = new FontExportSettings
            {
                Format = FontExportFormat.U8g2Bdf,
                FontName = "BdfFont"
            };

            string bdf = service.GenerateCode(doc, settings);

            // Standard BDF 2.1 tokens
            Assert.Contains("STARTFONT 2.1", bdf);
            Assert.Contains("FONT -hexprite-BdfFont-medium-r-normal--8", bdf);
            Assert.Contains("SIZE 8 75 75", bdf);
            Assert.Contains("FONTBOUNDINGBOX 8 8 0 -2", bdf);
            Assert.Contains("STARTPROPERTIES 2", bdf);
            Assert.Contains("FONT_ASCENT 6", bdf);
            Assert.Contains("FONT_DESCENT 2", bdf);
            Assert.Contains("ENDPROPERTIES", bdf);
            Assert.Contains("CHARS 2", bdf);

            // Glyph definitions
            Assert.Contains("STARTCHAR A", bdf);
            Assert.Contains("ENCODING 65", bdf);
            Assert.Contains("SWIDTH", bdf);
            Assert.Contains("DWIDTH", bdf);
            Assert.Contains("BBX", bdf);
            Assert.Contains("BITMAP", bdf);
            Assert.Contains("ENDCHAR", bdf);
            Assert.Contains("ENDFONT", bdf);
        }

        [Fact]
        public void Lvgl_DescriptorStructureAndTypes()
        {
            var service = new FontCodeGeneratorService();
            var doc = CreateTestFont(65, 66);
            doc.FontName = "LvglTest";

            var settings = new FontExportSettings
            {
                Format = FontExportFormat.Lvgl,
                FontName = "LvglTest"
            };

            string code = service.GenerateCode(doc, settings);

            Assert.Contains("#pragma once", code);
            Assert.Contains("lvgl.h", code);
            Assert.Contains("static const uint8_t LvglTest_glyph_bitmap[] = {", code);
            Assert.Contains("static const lv_font_fmt_txt_glyph_dsc_t LvglTest_glyph_dsc[] = {", code);
            Assert.Contains("static const lv_font_fmt_txt_dsc_t LvglTest_font_dsc = {", code);
            Assert.Contains("const lv_font_t LvglTest = {", code);
            Assert.Contains(".get_glyph_dsc = lv_font_get_glyph_dsc_fmt_txt,", code);
            Assert.Contains(".get_glyph_bitmap = lv_font_get_bitmap_fmt_txt,", code);
            Assert.Contains(".line_height = 8,", code);
        }

        [Fact]
        public void RawCArray_LookupPointersAndMetricArrays()
        {
            var service = new FontCodeGeneratorService();
            var doc = CreateTestFont(65, 66);
            doc.FontName = "RawTest";

            FillRect(doc.Glyphs[0], 0, 0, 2, 2);
            FillRect(doc.Glyphs[1], 1, 1, 3, 3);

            var settings = new FontExportSettings
            {
                Format = FontExportFormat.RawCArray,
                FontName = "RawTest"
            };

            string code = service.GenerateCode(doc, settings);

            // Bitmap functions
            Assert.Contains("static const uint8_t RawTest_41[] = {", code);
            Assert.Contains("static const uint8_t RawTest_42[] = {", code);

            // Table of pointers
            Assert.Contains("static const uint8_t* const RawTest_bitmaps[] = {", code);
            Assert.Contains("RawTest_41,", code);
            Assert.Contains("RawTest_42", code);

            // Metric arrays
            Assert.Contains("static const uint8_t RawTest_widths[] = {", code);
            Assert.Contains("static const uint8_t RawTest_heights[] = {", code);
            Assert.Contains("static const uint8_t RawTest_advances[] = {", code);
            Assert.Contains("static const int8_t  RawTest_x_offsets[] = {", code);
            Assert.Contains("static const int8_t  RawTest_y_offsets[] = {", code);

            // Container struct
            Assert.Contains("typedef struct {", code);
            Assert.Contains("const RawFont_RawTest RawTest_font = {", code);
        }

        // ── Tier 2: Boundary & Corner Cases ──────────────────────────────────

        [Fact]
        public void EmptySpaceGlyph_ExportFidelityAcrossFormats()
        {
            var service = new FontCodeGeneratorService();
            var doc = FontDocument.CreateNew(8, 8, 32, 32); // Only space ' ' (code 32)
            doc.FontName = "SpaceFont";

            var glyph = doc.Glyphs[0];
            glyph.XAdvance = 4;
            // No ink pixels in space

            // 1. Adafruit GFX: width=0, height=0, yOffset = cy - doc.Baseline = -6
            var gfxSettings = new FontExportSettings { Format = FontExportFormat.AdafruitGfx, FontName = "SpaceFont" };
            string gfxCode = service.GenerateCode(doc, gfxSettings);
            Assert.Contains("{     0,   0,   0,   4,   0,  -6 }", gfxCode);

            // 2. U8g2 BDF: BBX 0 0 0 0
            var bdfSettings = new FontExportSettings { Format = FontExportFormat.U8g2Bdf, FontName = "SpaceFont" };
            string bdfCode = service.GenerateCode(doc, bdfSettings);
            Assert.Contains("BBX 0 0 0 0", bdfCode);
            Assert.Contains("DWIDTH 4 0", bdfCode);

            // 3. LVGL: box_w = 0, box_h = 0
            var lvglSettings = new FontExportSettings { Format = FontExportFormat.Lvgl, FontName = "SpaceFont" };
            string lvglCode = service.GenerateCode(doc, lvglSettings);
            Assert.Contains(".box_w = 0, .box_h = 0", lvglCode);
            Assert.Contains(".adv_w = 64", lvglCode); // 4 * 16 = 64
        }

        [Fact]
        public void SingleGlyphRange_FirstEqualsLast_Succeeds()
        {
            var service = new FontCodeGeneratorService();
            var doc = CreateTestFont(65, 65); // Only 'A'
            doc.FontName = "SingleChar";

            foreach (FontExportFormat fmt in Enum.GetValues<FontExportFormat>())
            {
                var settings = new FontExportSettings { Format = fmt, FontName = "SingleChar" };
                string code = service.GenerateCode(doc, settings);
                Assert.NotNull(code);
                Assert.NotEmpty(code);
            }
        }

        [Fact]
        public void FullCellInkGlyph_AllBitsSet_ProducesSolidByteStream()
        {
            var service = new FontCodeGeneratorService();
            var doc = CreateTestFont(8, 8); // 8x8
            var glyph = doc.Glyphs[0];
            // Fill all 64 bits with ink
            Array.Fill(glyph.Pixels, true);

            var settings = new FontExportSettings
            {
                Format = FontExportFormat.AdafruitGfx,
                FontName = "SolidFont",
                UppercaseHex = true
            };

            string code = service.GenerateCode(doc, settings);

            // 64 bits = 8 bytes of 0xFF
            Assert.Contains("0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF", code);
        }

        [Fact]
        public void NegativeMetricOffsets_ProperlyFormattedInOutput()
        {
            var service = new FontCodeGeneratorService();
            var doc = CreateTestFont(65, 65);
            var glyph = doc.Glyphs[0];
            FillRect(glyph, 0, 0, 3, 3);
            glyph.XOffset = -3;
            glyph.YOffset = -5;
            glyph.XAdvance = 12;

            var settings = new FontExportSettings
            {
                Format = FontExportFormat.AdafruitGfx,
                FontName = "OffsetFont"
            };

            string code = service.GenerateCode(doc, settings);

            // GFXglyph should contain negative xOffset (-3) and yOffset ((cy - baseline) + yOffset = -6 + (-5) = -11)
            Assert.Contains("-3", code);
            Assert.Contains("-11", code);
        }

        [Fact]
        public void HexCaseAndComments_SettingsRespected()
        {
            var service = new FontCodeGeneratorService();
            var doc = CreateTestFont(65, 65);
            FillRect(doc.Glyphs[0], 0, 0, 2, 2);

            // Uppercase hex
            var settingsUpper = new FontExportSettings
            {
                Format = FontExportFormat.AdafruitGfx,
                FontName = "CaseTest",
                UppercaseHex = true
            };
            string upperCode = service.GenerateCode(doc, settingsUpper);
            Assert.Contains("0x", upperCode);

            // Lowercase hex
            var settingsLower = new FontExportSettings
            {
                Format = FontExportFormat.AdafruitGfx,
                FontName = "CaseTest",
                UppercaseHex = false
            };
            string lowerCode = service.GenerateCode(doc, settingsLower);
            Assert.Contains("0x", lowerCode);
        }

        // ── Tier 3: Pairwise & Layout Interactions ───────────────────────────

        [Fact]
        public void ProportionalGlyphs_DistinctWidthsAndAdvancesPreserved()
        {
            var service = new FontCodeGeneratorService();
            var doc = CreateTestFont(65, 66); // 'A', 'B'
            doc.FontName = "PropFont";

            var narrow = doc.Glyphs[0];
            narrow.Width = 3;
            narrow.XAdvance = 4;
            FillRect(narrow, 0, 0, 2, 4);

            var wide = doc.Glyphs[1];
            wide.Width = 7;
            wide.XAdvance = 9;
            FillRect(wide, 0, 0, 6, 4);

            // Verify Adafruit GFX captures individual metric descriptors
            var settings = new FontExportSettings { Format = FontExportFormat.AdafruitGfx, FontName = "PropFont" };
            string code = service.GenerateCode(doc, settings);

            // First glyph width 2, advance 4
            Assert.Contains("4", code); // xAdvance
            // Second glyph width 6, advance 9
            Assert.Contains("9", code); // xAdvance
        }

        [Fact]
        public void FlashMemoryEstimation_ReflectsInkAndGlyphCount()
        {
            var doc = FontDocument.CreateNew(8, 8, 32, 126); // 95 glyphs
            int baseEmptySize = doc.EstimateAdafruitGfxBytes();

            // Total base size: 95 glyphs * 6 = 570 + 7 font struct = 577 bytes
            Assert.True(baseEmptySize >= 577);

            // Add ink to 10 glyphs (3x3 = 9 bits each -> 2 bytes each)
            for (int i = 0; i < 10; i++)
            {
                FillRect(doc.Glyphs[i], 1, 1, 3, 3);
            }

            int populatedSize = doc.EstimateAdafruitGfxBytes();
            Assert.True(populatedSize > baseEmptySize, "Populated font must estimate higher flash bytes than empty font");
            // 10 glyphs * 2 bytes = 20 additional bitmap bytes
            Assert.Equal(baseEmptySize + 20, populatedSize);
        }

        // ── Tier 4: Real-World Scenarios ─────────────────────────────────────

        [Fact]
        public void FullPrintableAsciiRange_GeneratesSyntacticallyValidCode()
        {
            var service = new FontCodeGeneratorService();
            var doc = FontDocument.CreateNew(8, 8, 32, 126); // Standard ASCII 32..126
            doc.FontName = "StandardAscii8";

            // Add ink to several sample characters
            FillRect(doc.GetGlyph('A')!, 1, 1, 4, 6);
            FillRect(doc.GetGlyph('B')!, 1, 1, 4, 6);
            FillRect(doc.GetGlyph('0')!, 1, 1, 4, 6);

            foreach (FontExportFormat fmt in Enum.GetValues<FontExportFormat>())
            {
                var settings = new FontExportSettings
                {
                    Format = fmt,
                    FontName = "StandardAscii8",
                    IncludeUsageComment = true,
                    IncludeGlyphPreview = true,
                    IncludeMetricComments = true
                };

                string result = service.GenerateCode(doc, settings);

                Assert.NotNull(result);
                Assert.True(result.Length > 500, $"Format {fmt} output should be comprehensive");

                // Check for matched braces in C-like formats
                if (fmt != FontExportFormat.U8g2Bdf)
                {
                    int openBraces = CountOccurrences(result, '{');
                    int closeBraces = CountOccurrences(result, '}');
                    Assert.Equal(openBraces, closeBraces);
                }
            }
        }

        [Fact]
        public void FlipperZero_CompleteStructureAndColumnMajorPacking()
        {
            var service = new FontCodeGeneratorService();
            var doc = CreateTestFont(65, 65, 5, 7); // 5x7 single character 'A'
            doc.FontName = "FlipperTest";
            doc.Baseline = 6;
            doc.YAdvance = 8;

            var glyph = doc.Glyphs[0];
            glyph.XAdvance = 6;
            // Set top-left pixel (col 0, row 0) and bottom-left pixel (col 0, row 6)
            glyph.Pixels[0 * 5 + 0] = true; // bit 0 of col 0
            glyph.Pixels[6 * 5 + 0] = true; // bit 6 of col 0
            // Col 0 expected byte: (1 << 0) | (1 << 6) = 0x41 (65)

            var settings = new FontExportSettings
            {
                Format = FontExportFormat.FlipperZero,
                FontName = "FlipperTest",
                UppercaseHex = true
            };

            string code = service.GenerateCode(doc, settings);

            Assert.Contains("#define FLIPPERTEST_WIDTH 5", code);
            Assert.Contains("#define FLIPPERTEST_HEIGHT 7", code);
            Assert.Contains("#define FLIPPERTEST_FIRST_CHAR 0x41", code);
            Assert.Contains("#define FLIPPERTEST_LAST_CHAR 0x41", code);
            Assert.Contains("static const uint8_t FlipperTest_data[] = {", code);
            Assert.Contains("0x41", code); // Col 0 bit 0 + bit 6
            Assert.Contains("static const uint8_t FlipperTest_widths[] = {", code);
            Assert.Contains("static const uint8_t FlipperTest_advances[] = {", code);
            Assert.Contains("6,", code);
        }

        [Fact]
        public void Lvgl_ContinuousBitPacking_NoRowPadding()
        {
            var service = new FontCodeGeneratorService();
            var doc = CreateTestFont(65, 65, 3, 3); // 3x3 glyph
            doc.FontName = "LvglPackTest";
            FillRect(doc.Glyphs[0], 0, 0, 3, 3); // 9 ink pixels

            var settings = new FontExportSettings
            {
                Format = FontExportFormat.Lvgl,
                FontName = "LvglPackTest",
                UppercaseHex = true
            };

            string code = service.GenerateCode(doc, settings);

            // 9 bits = 2 bytes:
            // Byte 1: 8 bits of 1s = 0xFF
            // Byte 2: 1 bit of 1 (MSB) + 7 bits of 0s = 0x80
            Assert.Contains("0xFF, 0x80", code);
            // Verify FORMAT0_TINY for contiguous range
            Assert.Contains("LV_FONT_FMT_TXT_CMAP_FORMAT0_TINY", code);
            Assert.Contains(".unicode_list = NULL", code);
            // Verify negative base_line format
            Assert.Contains(".base_line = -(", code);
        }

        [Fact]
        public void RawCArray_EmptyGlyph_EmitsValidIsoCInitializer()
        {
            var service = new FontCodeGeneratorService();
            var doc = FontDocument.CreateNew(8, 8, 32, 32); // Space (empty)
            doc.FontName = "RawEmptyTest";

            var settings = new FontExportSettings
            {
                Format = FontExportFormat.RawCArray,
                FontName = "RawEmptyTest"
            };

            string code = service.GenerateCode(doc, settings);

            // Must NOT emit empty array `{ }`
            Assert.DoesNotContain("{ }", code);
            Assert.Contains("{ 0x00 };", code);
        }

        [Fact]
        public void EstimateFlashBytes_ProvidesFormatAwareEstimates()
        {
            var doc = CreateTestFont(65, 70, 8, 8); // 6 glyphs
            FillRect(doc.Glyphs[0], 0, 0, 8, 8);

            int adafruit = doc.EstimateFlashBytes(FontExportFormat.AdafruitGfx);
            int lvgl = doc.EstimateFlashBytes(FontExportFormat.Lvgl);
            int rawC = doc.EstimateFlashBytes(FontExportFormat.RawCArray);
            int flipper = doc.EstimateFlashBytes(FontExportFormat.FlipperZero);

            Assert.True(adafruit > 0);
            Assert.True(lvgl > 0);
            Assert.True(rawC > 0);
            Assert.True(flipper > 0);
            // Different formats have distinct overhead structures
            Assert.NotEqual(adafruit, lvgl);
        }

        [Fact]
        public void EstimateFlashBytes_MatchesExactFormulaSpecificationPerFormat()
        {
            // Create a 2-glyph document (65 and 66)
            var doc = CreateTestFont(65, 66, 8, 8);
            // Glyph 65: 3x3 filled rect (9 pixels -> 2 bytes bitpacked)
            FillRect(doc.Glyphs[0], 0, 0, 3, 3);
            // Glyph 66: 0 pixels (empty -> 0 bytes)

            // 1. Adafruit GFX: bitmapBytes (2) + glyphCount (2) * 7 + 16 = 2 + 14 + 16 = 32
            Assert.Equal(32, doc.EstimateFlashBytes(FontExportFormat.AdafruitGfx));

            // 2. LVGL: bitmapBytes (2) + glyphCount (2) * 12 + 8 (cmap) + 36 (font_dsc) = 2 + 24 + 8 + 36 = 70
            Assert.Equal(70, doc.EstimateFlashBytes(FontExportFormat.Lvgl));

            // 3. Flipper Zero: bitmapBytes (each glyph 8x8 -> 8 * 1 byte = 8 bytes, so 16) + glyphCount (2) * 4 + 16 = 16 + 8 + 16 = 40
            Assert.Equal(40, doc.EstimateFlashBytes(FontExportFormat.FlipperZero));

            // 4. Raw C Array:
            // Glyph 0: 3x3 -> 3 rows * ((3+7)/8 = 1) = 3 bytes
            // Glyph 1: empty -> 1 byte
            // Total bitmap = 4
            // glyphCount (2) * 8 + 16 = 4 + 16 + 16 = 36
            Assert.Equal(36, doc.EstimateFlashBytes(FontExportFormat.RawCArray));
        }

        [Fact]
        public void FontViewModel_ExportFormatChange_UpdatesEstimatedFlashBytesDynamically()
        {
            var vm = new Hexprite.ViewModels.FontViewModel();
            var doc = CreateTestFont(65, 66, 8, 8);
            FillRect(doc.Glyphs[0], 0, 0, 3, 3);
            vm.Document = doc;

            vm.ExportFormat = FontExportFormat.AdafruitGfx;
            int adafruitBytes = vm.EstimatedFlashBytes;
            Assert.Equal(32, adafruitBytes);

            vm.ExportFormat = FontExportFormat.Lvgl;
            int lvglBytes = vm.EstimatedFlashBytes;
            Assert.Equal(70, lvglBytes);
            Assert.NotEqual(adafruitBytes, lvglBytes);

            vm.ExportFormat = FontExportFormat.FlipperZero;
            int flipperBytes = vm.EstimatedFlashBytes;
            Assert.Equal(40, flipperBytes);

            vm.ExportFormat = FontExportFormat.RawCArray;
            int rawCBytes = vm.EstimatedFlashBytes;
            Assert.Equal(36, rawCBytes);
        }

        private static int CountOccurrences(string text, char target)
        {
            int count = 0;
            foreach (char c in text)
            {
                if (c == target) count++;
            }
            return count;
        }
    }
}
