using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using System;
using System.Text.RegularExpressions;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FontCodeGeneratorCorrectnessTests
    {
        [Fact]
        public void AdafruitGfx_ShouldTightlyCropAndPackBitsCorrectly()
        {
            var service = new FontCodeGeneratorService();
            var doc = FontDocument.CreateNew(8, 8, 65, 65); // 'A'
            doc.FontName = "TestFont";
            doc.Baseline = 6;
            doc.YAdvance = 10;
            
            // Draw a 3x3 square in the middle of the 8x8 grid:
            // x, y = 2 to 4
            // 00000000 (y=0)
            // 00000000 (y=1)
            // 00111000 (y=2) - cy = 2, top is 4px above baseline (Baseline=6) -> yOffset = -4
            // 00101000 (y=3)
            // 00111000 (y=4) - bottom is at y=4 -> 2px above baseline
            // 00000000 (y=5)
            // 00000000 (y=6) - Baseline
            // 00000000 (y=7) - Descent
            
            var glyph = doc.Glyphs[0];
            glyph.XAdvance = 8;
            
            // Row 2
            glyph.Pixels[2 * 8 + 2] = true;
            glyph.Pixels[2 * 8 + 3] = true;
            glyph.Pixels[2 * 8 + 4] = true;
            // Row 3
            glyph.Pixels[3 * 8 + 2] = true;
            glyph.Pixels[3 * 8 + 4] = true;
            // Row 4
            glyph.Pixels[4 * 8 + 2] = true;
            glyph.Pixels[4 * 8 + 3] = true;
            glyph.Pixels[4 * 8 + 4] = true;

            var settings = new FontExportSettings
            {
                Format = FontExportFormat.AdafruitGfx,
                FontName = "TestFont",
                IncludeUsageComment = true,
                IncludeGlyphPreview = false,
                IncludeMetricComments = false,
                UppercaseHex = true
            };

            string code = service.GenerateCode(doc, settings);

            // 1. Check include guard and headers
            Assert.Contains("#pragma once", code);
            Assert.Contains("#include <stdint.h>", code);
            Assert.Contains("Adafruit_GFX.h", code);

            // 2. The cropped bounding box should be 3x3.
            // Total bits = 9. Bytes needed = 2.
            // Bits: 111 101 111 -> 1111 0111  1000 0000 -> 0xF7, 0x80
            Assert.Contains("0xF7, 0x80", code);

            // 3. The GFXglyph struct should be:
            // { bitmapOffset, width, height, xAdvance, xOffset, yOffset }
            // { 0, 3, 3, 8, 2, -4 }
            Assert.Contains("{     0,   3,   3,   8,   2,  -4 }", code);
        }

        [Fact]
        public void Lvgl_ShouldPackBitsCorrectly_With1Bpp()
        {
            var service = new FontCodeGeneratorService();
            var doc = FontDocument.CreateNew(8, 8, 65, 65); // 'A'
            doc.FontName = "LvglFont";
            doc.Baseline = 6;
            
            // Draw a 2x2 square in top left:
            // 11000000 (y=0) -> cy = 0, ch = 2.
            // 11000000 (y=1) -> bottom is at row 1.
            // Baseline is at 6.
            // ofs_y = distance from baseline to bottom of glyph = 6 - 2 = 4.
            var glyph = doc.Glyphs[0];
            glyph.Pixels[0] = true;
            glyph.Pixels[1] = true;
            glyph.Pixels[8] = true;
            glyph.Pixels[9] = true;
            glyph.XAdvance = 4;

            var settings = new FontExportSettings
            {
                Format = FontExportFormat.Lvgl,
                FontName = "LvglFont",
                IncludeGlyphPreview = false
            };

            string code = service.GenerateCode(doc, settings);

            // Check include guard
            Assert.Contains("#pragma once", code);
            Assert.Contains("#include \"lvgl.h\"", code);

            // LVGL 1bpp continuous bit stream packing. Bounding box is 2x2 (4 bits: 1111 => 0xF0).
            Assert.Contains("0xf0", code.ToLower());

            // Reserved dummy missing glyph at index 0
            Assert.Contains("{ .bitmap_index = 0, .adv_w = 0, .box_w = 0, .box_h = 0, .ofs_x = 0, .ofs_y = 0 }, /* Reserved missing glyph */", code);

            // Character descriptor at index 1:
            // adv_w = 4 * 16 = 64. ofs_y = 4.
            Assert.Contains(".bitmap_index = 0, .adv_w = 64, .box_w = 2, .box_h = 2, .ofs_x = 0, .ofs_y = 4", code);

            // CMAP starts at glyph_id 1
            Assert.Contains(".glyph_id_start = 1,", code);
        }

        [Fact]
        public void RawCArray_ShouldGenerateLookupArrayAndStruct()
        {
            var service = new FontCodeGeneratorService();
            var doc = FontDocument.CreateNew(8, 8, 65, 66); // 'A' and 'B'
            doc.FontName = "RawTest";

            var settings = new FontExportSettings
            {
                Format = FontExportFormat.RawCArray,
                FontName = "RawTest"
            };

            string code = service.GenerateCode(doc, settings);

            Assert.Contains("#pragma once", code);
            Assert.Contains("#include <stdint.h>", code);
            Assert.Contains("static const uint8_t* const RawTest_bitmaps[] = {", code);
            Assert.Contains("RawTest_41,", code);
            Assert.Contains("RawTest_42", code);
            Assert.Contains("typedef struct {", code);
            Assert.Contains("const RawFont_RawTest RawTest_font = {", code);
        }

        [Fact]
        public void FontViewModel_GlyphFilter_ShouldUpdateResultsWhenTyping()
        {
            var vm = new FontViewModel();
            var doc = FontDocument.CreateNew(8, 8, 65, 70); // 'A', 'B', 'C', 'D', 'E', 'F'
            vm.Document = doc;

            // Initially full list
            Assert.Equal(6, vm.FilteredGlyphMap.Count);

            // Type "A"
            vm.GlyphFilter = "A";
            Assert.Single(vm.FilteredGlyphMap);
            Assert.Equal('A', vm.FilteredGlyphMap[0].Character);

            // Type "B" (should clear cache and update immediately)
            vm.GlyphFilter = "B";
            Assert.Single(vm.FilteredGlyphMap);
            Assert.Equal('B', vm.FilteredGlyphMap[0].Character);

            // Clear filter
            vm.GlyphFilter = "";
            Assert.Equal(6, vm.FilteredGlyphMap.Count);
        }

        [Fact]
        public void Lvgl_SparseCmap_ShouldUseRelativeOffsetsFromRangeStart()
        {
            var service = new FontCodeGeneratorService();
            // Non-continuous glyphs: 'A' (65) and 'C' (67)
            var doc = new FontDocument
            {
                CellHeight = 8,
                MaxCellWidth = 8,
                Baseline = 6,
                YAdvance = 8,
                FirstChar = 65,
                LastChar = 67,
                FontName = "SparseTest",
                Glyphs =
                [
                    new GlyphState { CodePoint = 65, Width = 8, Height = 8, Pixels = new bool[64] },
                    new GlyphState { CodePoint = 67, Width = 8, Height = 8, Pixels = new bool[64] }
                ]
            };

            var settings = new FontExportSettings
            {
                Format = FontExportFormat.Lvgl,
                FontName = "SparseTest"
            };

            string code = service.GenerateCode(doc, settings);

            // range_start should be 0x0041 (65 in hex)
            Assert.Contains(".range_start = 0x0041,", code);
            // range_length = 67 - 65 + 1 = 3
            Assert.Contains(".range_length = 3,", code);
            // unicode_list must contain relative offsets 0 and 2, NOT 65 and 67
            Assert.Contains("static const uint16_t SparseTest_unicode_list[] = {", code);
            Assert.Contains("0x0000, 0x0002", code);
            Assert.DoesNotContain("0x0041, 0x0043", code);
        }

        [Fact]
        public void Generators_ShouldSortGlyphs_WhenInputListIsOutOfOrder()
        {
            var service = new FontCodeGeneratorService();
            // Out-of-order glyphs: 'Z' (90), 'A' (65), 'M' (77)
            var doc = new FontDocument
            {
                CellHeight = 8,
                MaxCellWidth = 8,
                Baseline = 6,
                YAdvance = 8,
                FontName = "SortTest",
                Glyphs =
                [
                    new GlyphState { CodePoint = 90, Width = 8, Height = 8, Pixels = new bool[64] },
                    new GlyphState { CodePoint = 65, Width = 8, Height = 8, Pixels = new bool[64] },
                    new GlyphState { CodePoint = 77, Width = 8, Height = 8, Pixels = new bool[64] }
                ]
            };

            // 1. Adafruit GFX
            string adaCode = service.GenerateCode(doc, new FontExportSettings { Format = FontExportFormat.AdafruitGfx, FontName = "SortTest", IncludeMetricComments = true });
            int idxA = adaCode.IndexOf("0x41 'A'", StringComparison.Ordinal);
            int idxM = adaCode.IndexOf("0x4D 'M'", StringComparison.Ordinal);
            int idxZ = adaCode.IndexOf("0x5A 'Z'", StringComparison.Ordinal);
            Assert.True(idxA < idxM && idxM < idxZ, "Adafruit GFX glyphs must be ordered ascending by codepoint");

            // 2. Raw C Array
            string rawCode = service.GenerateCode(doc, new FontExportSettings { Format = FontExportFormat.RawCArray, FontName = "SortTest" });
            int rawIdxA = rawCode.IndexOf("SortTest_41", StringComparison.Ordinal); // 0x41 = 65
            int rawIdxM = rawCode.IndexOf("SortTest_4D", StringComparison.Ordinal); // 0x4D = 77
            int rawIdxZ = rawCode.IndexOf("SortTest_5A", StringComparison.Ordinal); // 0x5A = 90
            Assert.True(rawIdxA < rawIdxM && rawIdxM < rawIdxZ, "Raw C Array glyphs must be ordered ascending by codepoint");

            // 3. U8g2 BDF
            string bdfCode = service.GenerateCode(doc, new FontExportSettings { Format = FontExportFormat.U8g2Bdf, FontName = "SortTest" });
            int bdfIdxA = bdfCode.IndexOf("STARTCHAR A", StringComparison.Ordinal);
            int bdfIdxM = bdfCode.IndexOf("STARTCHAR M", StringComparison.Ordinal);
            int bdfIdxZ = bdfCode.IndexOf("STARTCHAR Z", StringComparison.Ordinal);
            Assert.True(bdfIdxA < bdfIdxM && bdfIdxM < bdfIdxZ, "BDF glyphs must be ordered ascending by codepoint");
        }

        [Fact]
        public void CommentEscaping_ShouldEscapeControlCharactersAndQuotes()
        {
            Assert.Equal("\\n", FontCodeGeneratorService.GetPrintableCharDisplay('\n'));
            Assert.Equal("\\r", FontCodeGeneratorService.GetPrintableCharDisplay('\r'));
            Assert.Equal("\\t", FontCodeGeneratorService.GetPrintableCharDisplay('\t'));
            Assert.Equal("\\0", FontCodeGeneratorService.GetPrintableCharDisplay('\0'));
            Assert.Equal("\\'", FontCodeGeneratorService.GetPrintableCharDisplay('\''));
            Assert.Equal("\\\\", FontCodeGeneratorService.GetPrintableCharDisplay('\\'));
            Assert.Equal("space", FontCodeGeneratorService.GetPrintableCharDisplay(' '));
            Assert.Equal("A", FontCodeGeneratorService.GetPrintableCharDisplay('A'));
        }

        [Theory]
        [InlineData("", "myFont")]
        [InlineData("   ", "myFont")]
        [InlineData(null, "myFont")]
        [InlineData("sprite", "myFont")]
        [InlineData("CustomFont", "CustomFont")]
        [InlineData("123Font", "_123Font")]
        [InlineData("My Font!", "My_Font_")]
        public void SanitiseFontName_ShouldDefaultToMyFont_WhenEmptyOrSprite(string? input, string expected)
        {
            Assert.Equal(expected, FontCodeGeneratorService.SanitiseFontName(input));
        }

        [Fact]
        public void Bdf_BoundingBoxYOffset_ShouldNotBePositive_WhenBaselineExceedsCellHeight()
        {
            var service = new FontCodeGeneratorService();
            var doc = FontDocument.CreateNew(8, 8, 65, 65);
            doc.FontName = "BdfTest";
            doc.Baseline = 10; // Baseline > CellHeight (8) -> Descent = -2

            string code = service.GenerateCode(doc, new FontExportSettings { Format = FontExportFormat.U8g2Bdf, FontName = "BdfTest" });

            // FONTBOUNDINGBOX <width> <height> <x_offset> <y_offset>
            // y_offset must be clamped to <= 0, never positive
            Assert.Contains("FONTBOUNDINGBOX 8 8 0 0", code);
            Assert.DoesNotContain("FONTBOUNDINGBOX 8 8 0 2", code);
        }
    }
}
