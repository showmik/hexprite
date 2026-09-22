using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    [Collection("WindowLayoutSettingsFile")]
    public class FontEditorM3ChallengerExportAndFlashTests
    {
        public FontEditorM3ChallengerExportAndFlashTests()
        {
            WpfTestHelper.EnsureApplication();
        }

        private static FontDocument CreateTestDoc(int firstChar, int lastChar, int width = 8, int height = 8, int baseline = 6)
        {
            var doc = FontDocument.CreateNew(width, height, firstChar, lastChar);
            doc.FontName = "TestFont";
            doc.Baseline = baseline;
            doc.YAdvance = height;
            return doc;
        }

        private static void FillGlyph(GlyphState glyph, bool[] pattern, int w, int h)
        {
            for (int y = 0; y < h && y < glyph.Height; y++)
            {
                for (int x = 0; x < w && x < glyph.Width; x++)
                {
                    glyph.Pixels[y * glyph.Width + x] = pattern[y * w + x];
                }
            }
        }

        private static List<byte> ExtractHexBytes(string code, string arrayName)
        {
            var match = Regex.Match(code, $@"const uint8_t {Regex.Escape(arrayName)}\[\]\s*(?:PROGMEM\s*)?=\s*\{{([^}}]+)\}};", RegexOptions.Singleline);
            if (!match.Success)
            {
                match = Regex.Match(code, $@"static const uint8_t {Regex.Escape(arrayName)}\[\]\s*=\s*\{{([^}}]+)\}};", RegexOptions.Singleline);
            }
            if (!match.Success) return new List<byte>();

            string body = match.Groups[1].Value;
            body = Regex.Replace(body, @"/\*.*?\*/", "", RegexOptions.Singleline);
            body = Regex.Replace(body, @"//.*", "");

            var byteMatches = Regex.Matches(body, @"0x([0-9a-fA-F]{2})");
            var bytes = new List<byte>();
            foreach (Match m in byteMatches)
            {
                bytes.Add(byte.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
            return bytes;
        }

        // =====================================================================
        // CHALLENGE GROUP 1: Flipper Zero Export Syntax & Column-Major Encoding (F14)
        // =====================================================================

        [Fact]
        public void Challenge_FlipperZero_MacroDefinitionsAndSanitization()
        {
            var service = new FontCodeGeneratorService();
            var doc = CreateTestDoc(0x20, 0x7E, 6, 10, baseline: 8);
            doc.FontName = "flipper-awesome_font 123";

            var settings = new FontExportSettings
            {
                Format = FontExportFormat.FlipperZero,
                FontName = doc.FontName,
                UppercaseHex = true,
                IncludeUsageComment = true,
                IncludeMetricComments = true
            };

            string code = service.GenerateCode(doc, settings);

            // Sanitized upper name should replace non-alphanumeric with underscores
            string expectedUpperName = CodeGeneratorService.SanitiseName(doc.FontName).ToUpperInvariant();
            Assert.Contains($"#define {expectedUpperName}_WIDTH 6", code);
            Assert.Contains($"#define {expectedUpperName}_HEIGHT 10", code);
            Assert.Contains($"#define {expectedUpperName}_FIRST_CHAR 0x20", code);
            Assert.Contains($"#define {expectedUpperName}_LAST_CHAR 0x7E", code);
            Assert.Contains($"#define {expectedUpperName}_GLYPH_COUNT 95", code);

            // Structure sections
            string sanitizedName = CodeGeneratorService.SanitiseName(doc.FontName);
            Assert.Contains($"static const uint8_t {sanitizedName}_data[] = {{", code);
            Assert.Contains($"static const uint8_t {sanitizedName}_widths[] = {{", code);
            Assert.Contains($"static const uint8_t {sanitizedName}_advances[] = {{", code);
        }

        [Theory]
        [InlineData(1, 1)]   // 1x1: 1 page, 1 col -> 1 byte
        [InlineData(8, 8)]   // 8x8: 1 page, 8 cols -> 8 bytes
        [InlineData(5, 7)]   // 5x7: 1 page, 5 cols -> 5 bytes
        [InlineData(6, 12)]  // 6x12: 2 pages, 6 cols -> 12 bytes
        [InlineData(10, 16)] // 10x16: 2 pages, 10 cols -> 20 bytes
        [InlineData(7, 21)]  // 7x21: 3 pages, 7 cols -> 21 bytes
        public void Challenge_FlipperZero_ColumnMajorEncoding_MatchesIndependentOracle(int w, int h)
        {
            var service = new FontCodeGeneratorService();
            var doc = CreateTestDoc(65, 65, w, h);
            var glyph = doc.Glyphs[0];

            // Deterministic pseudo-random pattern based on (x, y)
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    glyph.Pixels[y * w + x] = ((x * 3 + y * 7 + 1) % 5) > 2;
                }
            }

            var settings = new FontExportSettings
            {
                Format = FontExportFormat.FlipperZero,
                FontName = "OracleFont",
                UppercaseHex = true
            };

            string code = service.GenerateCode(doc, settings);
            var emittedBytes = ExtractHexBytes(code, "OracleFont_data");

            // Independent Oracle: Column-major, LSB at top
            int bytesPerCol = (h + 7) / 8;
            var expectedBytes = new List<byte>();
            for (int col = 0; col < w; col++)
            {
                for (int page = 0; page < bytesPerCol; page++)
                {
                    byte b = 0;
                    for (int bit = 0; bit < 8; bit++)
                    {
                        int row = page * 8 + bit;
                        if (row < h && glyph.Pixels[row * w + col])
                        {
                            b |= (byte)(1 << bit);
                        }
                    }
                    expectedBytes.Add(b);
                }
            }

            Assert.Equal(expectedBytes.Count, emittedBytes.Count);
            Assert.Equal(expectedBytes, emittedBytes);
        }

        [Fact]
        public void Challenge_FlipperZero_ProportionalFont_WidthsAndAdvancesFidelity()
        {
            var service = new FontCodeGeneratorService();
            var doc = CreateTestDoc(65, 68, 12, 10);
            doc.IsMonospaced = false;

            // Vary widths and advances for each glyph
            doc.Glyphs[0].Width = 3; doc.Glyphs[0].XAdvance = 4; // 'A'
            doc.Glyphs[1].Width = 10; doc.Glyphs[1].XAdvance = 12; // 'B'
            doc.Glyphs[2].Width = 7; doc.Glyphs[2].XAdvance = 8; // 'C'
            doc.Glyphs[3].Width = 5; doc.Glyphs[3].XAdvance = 6; // 'D'

            var settings = new FontExportSettings
            {
                Format = FontExportFormat.FlipperZero,
                FontName = "PropFont"
            };

            string code = service.GenerateCode(doc, settings);

            // Verify widths array has exact values
            var widthsMatch = Regex.Match(code, @"static const uint8_t PropFont_widths\[\] = \{\s*([^}]+)\};");
            Assert.True(widthsMatch.Success);
            Assert.Contains("3, 10, 7, 5", widthsMatch.Groups[1].Value);

            // Verify advances array has exact values
            var advancesMatch = Regex.Match(code, @"static const uint8_t PropFont_advances\[\] = \{\s*([^}]+)\};");
            Assert.True(advancesMatch.Success);
            Assert.Contains("4, 12, 8, 6", advancesMatch.Groups[1].Value);

            // Total bytes in data array = sum(width * ceil(height / 8))
            // Height is 10, so bytesPerCol = (10+7)/8 = 2
            // Expected total bytes = (3 + 10 + 7 + 5) * 2 = 25 * 2 = 50 bytes
            var dataBytes = ExtractHexBytes(code, "PropFont_data");
            Assert.Equal(50, dataBytes.Count);
        }

        // =====================================================================
        // CHALLENGE GROUP 2: LVGL 1bpp Continuous Bit Packing (F15)
        // =====================================================================

        [Theory]
        [InlineData(1, 1, 1)]     // 1x1: 1 bit -> 1 byte (0x80)
        [InlineData(2, 2, 1)]     // 2x2: 4 bits -> 1 byte (0xF0)
        [InlineData(3, 3, 2)]     // 3x3: 9 bits -> 2 bytes (0xFF, 0x80) -- NOT 3 bytes with row padding!
        [InlineData(4, 4, 2)]     // 4x4: 16 bits -> 2 bytes (0xFF, 0xFF)
        [InlineData(5, 7, 5)]     // 5x7: 35 bits -> 5 bytes (0xFF, 0xFF, 0xFF, 0xFF, 0xE0) -- NOT 7 bytes!
        [InlineData(7, 5, 5)]     // 7x5: 35 bits -> 5 bytes -- NOT 5 bytes row padded (each row 1 byte = 5 bytes)
        [InlineData(8, 8, 8)]     // 8x8: 64 bits -> 8 bytes
        [InlineData(11, 13, 18)]  // 11x13: 143 bits -> 18 bytes (143 = 17*8 + 7) -- NOT 26 bytes!
        [InlineData(16, 16, 32)]  // 16x16: 256 bits -> 32 bytes
        public void Challenge_Lvgl_ContinuousBitPacking_ByteCountAndNoRowPadding(int w, int h, int expectedBytes)
        {
            var service = new FontCodeGeneratorService();
            var doc = CreateTestDoc(65, 65, w, h);
            var glyph = doc.Glyphs[0];

            // Fill all pixels with 1s
            for (int i = 0; i < w * h; i++) glyph.Pixels[i] = true;

            var settings = new FontExportSettings
            {
                Format = FontExportFormat.Lvgl,
                FontName = "LvglTest",
                UppercaseHex = true
            };

            string code = service.GenerateCode(doc, settings);
            var bytes = ExtractHexBytes(code, "LvglTest_glyph_bitmap");

            Assert.Equal(expectedBytes, bytes.Count);

            // Check final byte bitmask: total bits is w * h
            int totalBits = w * h;
            int fullBytes = totalBits / 8;
            int remBits = totalBits % 8;

            for (int i = 0; i < fullBytes; i++)
            {
                Assert.Equal(0xFF, bytes[i]);
            }

            if (remBits > 0)
            {
                byte expectedRem = (byte)(((1 << remBits) - 1) << (8 - remBits));
                Assert.Equal(expectedRem, bytes[fullBytes]);
            }
        }

        [Fact]
        public void Challenge_Lvgl_ContinuousBitstream_AcrossRowTransitions_WithArbitraryPattern()
        {
            var service = new FontCodeGeneratorService();
            int w = 5, h = 5;
            var doc = CreateTestDoc(65, 65, w, h);
            var glyph = doc.Glyphs[0];

            // Set specific pixels that cross byte boundaries:
            // Row 0 (bits 0..4): pixels at x=0, x=4 (bits 0, 4)
            // Row 1 (bits 5..9): pixels at x=1, x=3 (bits 6, 8) -> bit 8 is in Byte 1!
            // Row 2 (bits 10..14): pixel at x=2 (bit 12)
            // Row 3 (bits 15..19): pixels at x=0, x=4 (bits 15, 19)
            // Row 4 (bits 20..24): pixels at x=1, x=2, x=3 (bits 21, 22, 23)
            glyph.Pixels[0 * 5 + 0] = true; // bit 0
            glyph.Pixels[0 * 5 + 4] = true; // bit 4
            glyph.Pixels[1 * 5 + 1] = true; // bit 6
            glyph.Pixels[1 * 5 + 3] = true; // bit 8
            glyph.Pixels[2 * 5 + 2] = true; // bit 12
            glyph.Pixels[3 * 5 + 0] = true; // bit 15
            glyph.Pixels[3 * 5 + 4] = true; // bit 19
            glyph.Pixels[4 * 5 + 1] = true; // bit 21
            glyph.Pixels[4 * 5 + 2] = true; // bit 22
            glyph.Pixels[4 * 5 + 3] = true; // bit 23

            // Byte 0: bits 0..7:
            // bit 0: 1, bit 1: 0, bit 2: 0, bit 3: 0, bit 4: 1, bit 5: 0, bit 6: 1, bit 7: 0
            // Byte 0 = 0b10001010 = 0x8A
            // Byte 1: bits 8..15:
            // bit 8: 1, bit 9: 0, bit 10: 0, bit 11: 0, bit 12: 1, bit 13: 0, bit 14: 0, bit 15: 1
            // Byte 1 = 0b10001001 = 0x89
            // Byte 2: bits 16..23:
            // bit 16: 0, bit 17: 0, bit 18: 0, bit 19: 1, bit 20: 0, bit 21: 1, bit 22: 1, bit 23: 1
            // Byte 2 = 0b00010111 = 0x17
            // Byte 3: bit 24:
            // bit 24: 0
            // Total bits = 25 -> 4 bytes: 0x8A, 0x89, 0x17, 0x00

            var settings = new FontExportSettings
            {
                Format = FontExportFormat.Lvgl,
                FontName = "CrossRowFont",
                UppercaseHex = true
            };

            string code = service.GenerateCode(doc, settings);
            var bytes = ExtractHexBytes(code, "CrossRowFont_glyph_bitmap");

            Assert.Equal(4, bytes.Count);
            Assert.Equal(0x8A, bytes[0]);
            Assert.Equal(0x89, bytes[1]);
            Assert.Equal(0x17, bytes[2]);
            Assert.Equal(0x00, bytes[3]);
        }

        [Fact]
        public void Challenge_Lvgl_EmptyAndSpaceGlyphs_ConsumeZeroBitmapBytes()
        {
            var service = new FontCodeGeneratorService();
            var doc = CreateTestDoc(32, 33, 8, 8); // Space (32, empty) and '!' (33, populated)
            doc.Glyphs[0].XAdvance = 4; // Space has advance but zero pixels
            doc.Glyphs[1].XAdvance = 8; // Explicit 8px advance

            // Fill '!' with a 1x4 vertical line
            doc.Glyphs[1].Pixels[1 * 8 + 3] = true;
            doc.Glyphs[1].Pixels[2 * 8 + 3] = true;
            doc.Glyphs[1].Pixels[3 * 8 + 3] = true;
            doc.Glyphs[1].Pixels[5 * 8 + 3] = true;

            var settings = new FontExportSettings
            {
                Format = FontExportFormat.Lvgl,
                FontName = "SpaceFont",
                UppercaseHex = true
            };

            string code = service.GenerateCode(doc, settings);

            // Space: cw=0, ch=0 -> 0 bytes
            // '!': tight bounds cw=1, ch=5 -> (1*5+7)/8 = 1 byte
            var bytes = ExtractHexBytes(code, "SpaceFont_glyph_bitmap");
            Assert.Single(bytes);

            // In glyph_dsc:
            // Space should have box_w = 0, box_h = 0, bitmap_index = 0
            Assert.Contains(".bitmap_index = 0, .adv_w = 64, .box_w = 0, .box_h = 0", code);
            // '!' should also start at bitmap_index = 0 since space consumed 0 bytes
            var dscMatch = Regex.Match(code, @"\{\s*\.bitmap_index\s*=\s*(\d+),\s*\.adv_w\s*=\s*(\d+),\s*\.box_w\s*=\s*(\d+),\s*\.box_h\s*=\s*(\d+)[^}]+\}\s*,\s*/\*\s*'!'\s*\*/");
            Assert.True(dscMatch.Success, "DSC match failed: " + code);
            Assert.Equal("0", dscMatch.Groups[1].Value); // bitmap_index = 0
            Assert.Equal("1", dscMatch.Groups[3].Value); // box_w = 1
            Assert.Equal("5", dscMatch.Groups[4].Value); // box_h = 5
        }

        // =====================================================================
        // CHALLENGE GROUP 3: LVGL CMAP & Baseline Specification (F16)
        // =====================================================================

        [Fact]
        public void Challenge_Lvgl_ContiguousRange_EmitsFormat0TinyWithNullUnicodeList()
        {
            var service = new FontCodeGeneratorService();
            var doc = CreateTestDoc(65, 90, 8, 12, baseline: 9); // Contiguous A-Z (26 glyphs)

            var settings = new FontExportSettings
            {
                Format = FontExportFormat.Lvgl,
                FontName = "ContigFont"
            };

            string code = service.GenerateCode(doc, settings);

            // CMAP format0_tiny verification
            Assert.Contains("LV_FONT_FMT_TXT_CMAP_FORMAT0_TINY", code);
            Assert.Contains(".unicode_list = NULL,", code);
            Assert.Contains(".range_start = 0x0041,", code);
            Assert.Contains(".range_length = 26,", code);
            Assert.Contains(".glyph_id_start = 1,", code);
            Assert.Contains(".list_length = 0,", code);
            // Must NOT have unicode_list array defined
            Assert.DoesNotContain("ContigFont_unicode_list", code);
        }

        [Fact]
        public void Challenge_Lvgl_SparseNonContiguousRange_EmitsSparseTinyWithUnicodeList()
        {
            var service = new FontCodeGeneratorService();
            // Create non-contiguous doc with gaps
            var doc = FontDocument.CreateNew(8, 12, 65, 65);
            doc.Glyphs.Add(new GlyphState { CodePoint = 67, Width = 8, Height = 12, Pixels = new bool[96] }); // 'C' (gap: B missing)
            doc.Glyphs.Add(new GlyphState { CodePoint = 75, Width = 8, Height = 12, Pixels = new bool[96] }); // 'K'

            var settings = new FontExportSettings
            {
                Format = FontExportFormat.Lvgl,
                FontName = "SparseFont"
            };

            string code = service.GenerateCode(doc, settings);

            Assert.Contains("LV_FONT_FMT_TXT_CMAP_SPARSE_TINY", code);
            Assert.Contains(".unicode_list = SparseFont_unicode_list,", code);
            Assert.Contains("static const uint16_t SparseFont_unicode_list[] = {", code);
            // LVGL SPARSE_TINY expects relative offsets from range_start (doc.FirstChar = 65): 0, 2, 10
            Assert.Contains("0x0000, 0x0002, 0x000A,", code);
        }

        [Theory]
        [InlineData(16, 12, "-4")]  // Descender depth = 16 - 12 = 4 -> base_line = -(4)
        [InlineData(8, 6, "-2")]    // Descender depth = 8 - 6 = 2 -> base_line = -(2)
        [InlineData(10, 10, "-0")]  // Baseline sits at bottom -> base_line = -(0)
        [InlineData(12, 15, "-0")]  // Baseline sits beyond cell height -> clamped to 0 -> -(0)
        public void Challenge_Lvgl_NegativeDescenderDepthBaselineCalculation(int cellHeight, int baseline, string expectedDescenderOffset)
        {
            var service = new FontCodeGeneratorService();
            var doc = CreateTestDoc(65, 65, 8, cellHeight, baseline: baseline);

            var settings = new FontExportSettings
            {
                Format = FontExportFormat.Lvgl,
                FontName = "BaselineTest"
            };

            string code = service.GenerateCode(doc, settings);

            // In LVGL: .base_line = -({Math.Max(0, doc.CellHeight - doc.Baseline)})
            string expectedToken = $".base_line = -({expectedDescenderOffset.TrimStart('-')}),";
            Assert.Contains(expectedToken, code);
        }

        // =====================================================================
        // CHALLENGE GROUP 4: Raw C Array Empty Initializer Syntax (F17)
        // =====================================================================

        [Fact]
        public void Challenge_RawCArray_EmptyAndZeroDimensionGlyphs_EmitsZeroByteInitializers()
        {
            var service = new FontCodeGeneratorService();
            var doc = CreateTestDoc(32, 34, 8, 8); // Space (32, empty), '!' (33, filled), '"' (34, empty)
            doc.Glyphs[1].Pixels[0] = true; // Make '!' non-empty

            var settings = new FontExportSettings
            {
                Format = FontExportFormat.RawCArray,
                FontName = "RawSyntaxTest"
            };

            string code = service.GenerateCode(doc, settings);

            // Verify empty glyph 0x20 and 0x22 emit { 0x00 }
            Assert.Contains("static const uint8_t RawSyntaxTest_20[] = { 0x00 };", code);
            Assert.Contains("static const uint8_t RawSyntaxTest_22[] = { 0x00 };", code);

            // Strict ISO C check: There must be NO occurrence of empty braces "{ }"
            Assert.DoesNotContain("{ }", code);
            Assert.DoesNotContain("{\n}", code);
            Assert.DoesNotContain("{\r\n}", code);

            // Pointers array references all 3 glyphs
            Assert.Contains("RawSyntaxTest_20,", code);
            Assert.Contains("RawSyntaxTest_21,", code);
            Assert.Contains("RawSyntaxTest_22", code);
        }

        // =====================================================================
        // CHALLENGE GROUP 5: Dynamic Flash Estimation Invariants & Mathematical Accuracy (F18)
        // =====================================================================

        [Fact]
        public void Challenge_EstimateFlashBytes_EmptyDocument_ReturnsExactStructOverhead()
        {
            // Null glyphs
            var docNull = new FontDocument();
            docNull.Glyphs = null!;
            Assert.Equal(16, docNull.EstimateFlashBytes(FontExportFormat.AdafruitGfx));
            Assert.Equal(44, docNull.EstimateFlashBytes(FontExportFormat.Lvgl));
            Assert.Equal(16, docNull.EstimateFlashBytes(FontExportFormat.FlipperZero));
            Assert.Equal(16, docNull.EstimateFlashBytes(FontExportFormat.RawCArray));
            Assert.Equal(24, docNull.EstimateFlashBytes(FontExportFormat.U8g2Bdf));

            // Empty glyph list (Count == 0)
            var docEmpty = new FontDocument();
            docEmpty.Glyphs = new List<GlyphState>();
            Assert.Equal(16, docEmpty.EstimateFlashBytes(FontExportFormat.AdafruitGfx));
            Assert.Equal(44, docEmpty.EstimateFlashBytes(FontExportFormat.Lvgl));
            Assert.Equal(16, docEmpty.EstimateFlashBytes(FontExportFormat.FlipperZero));
            Assert.Equal(16, docEmpty.EstimateFlashBytes(FontExportFormat.RawCArray));
            Assert.Equal(24, docEmpty.EstimateFlashBytes(FontExportFormat.U8g2Bdf));
        }

        [Fact]
        public void Challenge_EstimateFlashBytes_SingleEmptyGlyph_Formulas()
        {
            var doc = CreateTestDoc(32, 32, 8, 8); // 1 glyph, completely empty

            // Adafruit GFX: 0 bitmap + 1*7 + 16 = 23
            Assert.Equal(23, doc.EstimateFlashBytes(FontExportFormat.AdafruitGfx));

            // LVGL: 0 bitmap + 1*12 + 8 + 36 = 56
            Assert.Equal(56, doc.EstimateFlashBytes(FontExportFormat.Lvgl));

            // Flipper Zero: (8 * 1) bitmap + 1*4 + 16 = 28
            Assert.Equal(28, doc.EstimateFlashBytes(FontExportFormat.FlipperZero));

            // Raw C Array: 1 byte for { 0x00 } + 1*8 + 16 = 25
            Assert.Equal(25, doc.EstimateFlashBytes(FontExportFormat.RawCArray));

            // U8g2 Bdf: 0 bitmap + 1*4 + 24 = 28
            Assert.Equal(28, doc.EstimateFlashBytes(FontExportFormat.U8g2Bdf));
        }

        [Fact]
        public void Challenge_EstimateFlashBytes_SingleFilledGlyph_Formulas()
        {
            var doc = CreateTestDoc(65, 65, 8, 8);
            for (int i = 0; i < 64; i++) doc.Glyphs[0].Pixels[i] = true;

            // 8x8 filled:
            // cw=8, ch=8
            // Bitpacked: (64 + 7)/8 = 8 bytes
            // Row-padded: 8 * ((8+7)/8) = 8 bytes
            // Column-major: 8 * ((8+7)/8) = 8 bytes

            // Adafruit GFX: 8 + 7 + 16 = 31
            Assert.Equal(31, doc.EstimateFlashBytes(FontExportFormat.AdafruitGfx));

            // LVGL: 8 + 12 + 8 + 36 = 64
            Assert.Equal(64, doc.EstimateFlashBytes(FontExportFormat.Lvgl));

            // Flipper Zero: 8 + 4 + 16 = 28
            Assert.Equal(28, doc.EstimateFlashBytes(FontExportFormat.FlipperZero));

            // Raw C Array: 8 + 8 + 16 = 32
            Assert.Equal(32, doc.EstimateFlashBytes(FontExportFormat.RawCArray));

            // U8g2 Bdf: 8 + 4 + 24 = 36
            Assert.Equal(36, doc.EstimateFlashBytes(FontExportFormat.U8g2Bdf));
        }

        [Fact]
        public void Challenge_EstimateFlashBytes_FullAsciiRange_DynamicUpdateInViewModel()
        {
            var vm = new FontViewModel();
            var doc = CreateTestDoc(32, 126, 8, 8); // 95 glyphs, all initially empty
            vm.Document = doc;

            // Verify initial estimates across all formats
            vm.ExportFormat = FontExportFormat.AdafruitGfx;
            Assert.Equal(95 * 7 + 16, vm.EstimatedFlashBytes); // 681

            vm.ExportFormat = FontExportFormat.Lvgl;
            Assert.Equal(95 * 12 + 44, vm.EstimatedFlashBytes); // 1184

            vm.ExportFormat = FontExportFormat.FlipperZero;
            Assert.Equal(95 * 8 + 95 * 4 + 16, vm.EstimatedFlashBytes); // 1156

            vm.ExportFormat = FontExportFormat.RawCArray;
            Assert.Equal(95 * 1 + 95 * 8 + 16, vm.EstimatedFlashBytes); // 871

            vm.ExportFormat = FontExportFormat.U8g2Bdf;
            Assert.Equal(95 * 4 + 24, vm.EstimatedFlashBytes); // 404

            // Now mutate a glyph by drawing on it:
            // Paint a 2x2 rect on glyph 65 ('A')
            vm.SetActiveGlyph(33); // 'A' (offset 33 from 32)
            vm.BeginDrawing();
            vm.DrawPixel(0, 0, erase: false);
            vm.DrawPixel(1, 0, erase: false);
            vm.DrawPixel(0, 1, erase: false);
            vm.DrawPixel(1, 1, erase: false);
            vm.EndDrawing();

            // Bounding box for 'A' is now 2x2 -> (4+7)/8 = 1 byte
            vm.ExportFormat = FontExportFormat.AdafruitGfx;
            Assert.Equal(1 + 95 * 7 + 16, vm.EstimatedFlashBytes); // 682

            vm.ExportFormat = FontExportFormat.Lvgl;
            Assert.Equal(1 + 95 * 12 + 44, vm.EstimatedFlashBytes); // 1185
        }

        [Fact]
        public void Challenge_EstimateFlashBytes_LargeRangeMonotonicScaling()
        {
            var doc500 = CreateTestDoc(100, 599, 16, 16); // 500 glyphs

            int prevGfx = 0, prevLvgl = 0, prevFlipper = 0, prevRaw = 0, prevU8g2 = 0;

            for (int count = 50; count <= 500; count += 50)
            {
                var doc = FontDocument.CreateNew(16, 16, 1, count);
                int gfx = doc.EstimateFlashBytes(FontExportFormat.AdafruitGfx);
                int lvgl = doc.EstimateFlashBytes(FontExportFormat.Lvgl);
                int flipper = doc.EstimateFlashBytes(FontExportFormat.FlipperZero);
                int raw = doc.EstimateFlashBytes(FontExportFormat.RawCArray);
                int u8g2 = doc.EstimateFlashBytes(FontExportFormat.U8g2Bdf);

                // Strictly positive
                Assert.True(gfx > 0);
                Assert.True(lvgl > 0);
                Assert.True(flipper > 0);
                Assert.True(raw > 0);
                Assert.True(u8g2 > 0);

                // Strictly monotonically increasing with character count
                Assert.True(gfx > prevGfx);
                Assert.True(lvgl > prevLvgl);
                Assert.True(flipper > prevFlipper);
                Assert.True(raw > prevRaw);
                Assert.True(u8g2 > prevU8g2);

                prevGfx = gfx;
                prevLvgl = lvgl;
                prevFlipper = flipper;
                prevRaw = raw;
                prevU8g2 = u8g2;
            }
        }
    }
}
