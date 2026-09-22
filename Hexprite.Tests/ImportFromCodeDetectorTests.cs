using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
    public sealed class ImportFromCodeDetectorTests
{
    [Fact]
    public void TryParseExplicitDimensions_FromWidthHeightConstants()
    {
        const string code = """
            const uint8_t ICON_WIDTH  = 12;
            const uint8_t ICON_HEIGHT = 8;
            const uint8_t PROGMEM icon[] = { 0x00 };
            """;

        Assert.True(ImportFromCodeDetector.TryParseExplicitDimensions(code, out int w, out int h, out string src));
        Assert.Equal(12, w);
        Assert.Equal(8, h);
        Assert.Equal("constants", src);
    }

    [Fact]
    public void TryParseExplicitDimensions_FromAdafruitDrawBitmapComment()
    {
        const string code = "// display.drawBitmap(x, y, s, 9, 3)";
        Assert.True(ImportFromCodeDetector.TryParseExplicitDimensions(code, out int w, out int h, out string src));
        Assert.Equal(9, w);
        Assert.Equal(3, h);
        Assert.Equal("usage comment", src);
    }

    [Fact]
    public void TryParseExplicitDimensions_FromDrawXbmComment()
    {
        const string code = "// u8g2.drawXBM(x, y, 18, 4, bits)";
        Assert.True(ImportFromCodeDetector.TryParseExplicitDimensions(code, out int w, out int h, out string src));
        Assert.Equal(18, w);
        Assert.Equal(4, h);
        Assert.Equal("usage comment", src);
    }

    [Fact]
    public void TryParseExplicitDimensions_FromU8g2DrawBitmap_ComputesWidthFromBytesPerRow()
    {
        const string code = "// u8g2.drawBitmap(x, y, 2, 5, bmp)";
        Assert.True(ImportFromCodeDetector.TryParseExplicitDimensions(code, out int w, out int h, out string src));
        Assert.Equal(16, w);
        Assert.Equal(5, h);
        Assert.Equal("usage comment", src);
    }

    [Fact]
    public void TryParseExplicitDimensions_FromPlainCUsageComment()
    {
        const string code = "// Use bmp as a 7×11 bitmap (MSB first)";
        Assert.True(ImportFromCodeDetector.TryParseExplicitDimensions(code, out int w, out int h, out string src));
        Assert.Equal(7, w);
        Assert.Equal(11, h);
        Assert.Equal("usage comment", src);
    }

    [Fact]
    public void TryParseExplicitDimensions_FromFrameBufferComment()
    {
        const string code = "# fb = framebuf.FrameBuffer(sprite, 20, 6, framebuf.MONO_HLSB)";
        Assert.True(ImportFromCodeDetector.TryParseExplicitDimensions(code, out int w, out int h, out string src));
        Assert.Equal(20, w);
        Assert.Equal(6, h);
        Assert.Equal("usage comment", src);
    }

    [Fact]
    public void TryParseExplicitDimensions_NoMatch_ReturnsFalse()
    {
        const string code = "random text without hints";
        Assert.False(ImportFromCodeDetector.TryParseExplicitDimensions(code, out _, out _, out string src));
        Assert.Equal("", src);
    }

    [Fact]
    public void DetectVariableName_CArray_ReturnsIdentifier()
    {
        const string code = "static const uint8_t PROGMEM dinogame_icon[32] = {\n  0xFF,\n};";
        Assert.Equal("dinogame_icon", ImportFromCodeDetector.DetectVariableName(code));
    }

    [Fact]
    public void DetectVariableName_PythonBytearray_ReturnsIdentifier()
    {
        const string code = "my_sprite = bytearray([\n    0x80,\n])";
        Assert.Equal("my_sprite", ImportFromCodeDetector.DetectVariableName(code));
    }

    [Fact]
    public void DetectVariableName_NoDeclaration_ReturnsNull()
    {
        Assert.Null(ImportFromCodeDetector.DetectVariableName("0x01 0x02"));
    }

    [Fact]
    public void DetectBytesPerRow_FindsMostCommonHexCountPerDataLine()
    {
        string code =
            "const uint8_t x[] = {\n" +
            "  0x01, 0x02,\n" +
            "  0x03, 0x04,\n" +
            "};";
        Assert.Equal(2, ImportFromCodeDetector.DetectBytesPerRow(code));
    }

    [Fact]
    public void CountDataBytes_StripsComments()
    {
        const string code = """
            // 0xEE skipped (comment)
            0xAA /* 0xBB masked */
            0xCC
            """;
        Assert.Equal(2, ImportFromCodeDetector.CountDataBytes(code));
    }

    [Fact]
    public void TryInferDimensionsFromData_LineStructureTwoBytesPerRow_16WideCanvas()
    {
        string code =
            "const uint8_t x[] = {\n" +
            "  0xFF, 0xFF,\n" +
            "  0xFF, 0xFF,\n" +
            "  0xFF, 0xFF,\n" +
            "};";
        Assert.True(ImportFromCodeDetector.TryInferDimensionsFromData(code, byteCount: 6, out int w, out int h,
            out ImportDimensionInferHint hint));
        Assert.Equal(16, w);
        Assert.Equal(3, h);
        Assert.Equal(ImportDimensionInferHint.FromLineStructure, hint);
    }

    [Fact]
    public void TryInferDimensionsFromData_AmbiguousLineStructure_FallsBackToByteCount()
    {
        // Two hex tokens per line, but total bytes (5) not divisible by 2 → ambiguous branch
        string code =
            "{\n" +
            "  0x01, 0x02,\n" +
            "  0x03, 0x04,\n" +
            "  0x05,\n" +
            "}";
        Assert.True(ImportFromCodeDetector.TryInferDimensionsFromData(code, byteCount: 5, out int w, out int h,
            out ImportDimensionInferHint hint));
        Assert.Equal(ImportDimensionInferHint.AmbiguousLineStructure, hint);
        Assert.True(w > 0 && h > 0);
    }

    [Fact]
    public void TryInferDimensionsFromData_Wrapped8x8Array_PrefersSquareOverLineStructure()
    {
        string code = """
            static const uint8_t PROGMEM spr_icon_heart[8] = {
                0x66, 0xFF, 0xFF, 0x7E,
                0x3C, 0x18, 0x00, 0x00
            };
            """;
        Assert.True(ImportFromCodeDetector.TryInferDimensionsFromData(code, byteCount: 8, out int w, out int h,
            out ImportDimensionInferHint hint));
        Assert.Equal(8, w);
        Assert.Equal(8, h);
        Assert.Equal(ImportDimensionInferHint.FromByteCount, hint);
    }

    [Fact]
    public void TryInferDimensionsFromData_Vertical4x4Array_PrefersSquareOverLineStructure()
    {
        string code = """
            static const uint8_t PROGMEM spr_snake_poison[4] = {
                0x90,
                0x60,
                0x60,
                0x90,
            };
            """;
        Assert.True(ImportFromCodeDetector.TryInferDimensionsFromData(code, byteCount: 4, out int w, out int h,
            out ImportDimensionInferHint hint));
        Assert.Equal(4, w);
        Assert.Equal(4, h);
        Assert.Equal(ImportDimensionInferHint.FromByteCount, hint);
    }

    [Fact]
    public void TryGuessDimensionsFromByteCount_PrefersSquareWhenPossible()
    {
        // 8×8 → 64 px → ceil(8/8)=1 byte/row → 8 rows * 1 byte = 8 bytes.
        Assert.True(ImportFromCodeDetector.TryGuessDimensionsFromByteCount(8, out int w, out int h));
        Assert.Equal(8, w);
        Assert.Equal(8, h);

        // 4×4 → 16 px → ceil(4/8)=1 byte/row → 4 rows * 1 byte = 4 bytes.
        Assert.True(ImportFromCodeDetector.TryGuessDimensionsFromByteCount(4, out int w4, out int h4));
        Assert.Equal(4, w4);
        Assert.Equal(4, h4);
    }

    [Theory]
    [InlineData("// drawXBM(", true)]
    [InlineData("XBM dump", true)]
    [InlineData("const uint8_t bmp[] = { 0x01 };", false)]
    public void IsLikelyXbmFormat(string fragment, bool expected) =>
        Assert.Equal(expected, ImportFromCodeDetector.IsLikelyXbmFormat(fragment));

    [Fact]
    public void ExpectedByteCount_MatchesCeilingBytesPerRow()
    {
        Assert.Equal(2 * 2, ImportFromCodeDetector.ExpectedByteCount(width: 9, height: 2)); // ceil(9/8)=2
        Assert.Equal(1 * 1, ImportFromCodeDetector.ExpectedByteCount(width: 8, height: 1));
    }

    // ── StripComments ────────────────────────────────────────────────────

    [Fact]
    public void StripComments_RemovesSingleLineComments()
    {
        const string code = "0xAA // this is a comment\n0xBB";
        string result = ImportFromCodeDetector.StripComments(code);
        Assert.Contains("0xAA", result);
        Assert.Contains("0xBB", result);
        Assert.DoesNotContain("this is a comment", result);
    }

    [Fact]
    public void StripComments_RemovesBlockComments()
    {
        const string code = "0xAA /* block comment with 0xEE */ 0xBB";
        string result = ImportFromCodeDetector.StripComments(code);
        Assert.Contains("0xAA", result);
        Assert.Contains("0xBB", result);
        Assert.DoesNotContain("0xEE", result);
    }

    [Fact]
    public void StripComments_RemovesMultilineBlockComments()
    {
        const string code = "0xAA\n/* multi\nline\n0xEE\ncomment */\n0xBB";
        string result = ImportFromCodeDetector.StripComments(code);
        Assert.Contains("0xAA", result);
        Assert.Contains("0xBB", result);
        Assert.DoesNotContain("0xEE", result);
    }

    [Fact]
    public void StripComments_EmptyOrNull_ReturnsSame()
    {
        Assert.Equal("", ImportFromCodeDetector.StripComments(""));
        Assert.Null(ImportFromCodeDetector.StripComments(null!));
    }

    [Fact]
    public void StripComments_NoComments_Unchanged()
    {
        const string code = "const uint8_t x[] = { 0x01, 0x02 };";
        Assert.Equal(code, ImportFromCodeDetector.StripComments(code));
    }

    // ── CountDataBytes uses StripComments ─────────────────────────────

    [Fact]
    public void CountDataBytes_IgnoresHexInBlockComment()
    {
        const string code = "/* 0xAA 0xBB 0xCC */ 0x01 0x02";
        Assert.Equal(2, ImportFromCodeDetector.CountDataBytes(code));
    }

    // ── IsLikelyBinaryFormat uses StripComments ──────────────────────

    [Fact]
    public void IsLikelyBinaryFormat_IgnoresCommentedHex()
    {
        // Without comment stripping, the hex inside the comment would make this return false
        const string code = "/* 0xFF */ 01010101 11110000";
        Assert.True(ImportFromCodeDetector.IsLikelyBinaryFormat(code));
    }

    [Theory]
    [InlineData("01010101 11110000", true)]
    [InlineData("0b01010101, 0b11110000", true)]
    [InlineData("B01010101, B11110000", true)]
    [InlineData("0xFF, 0x00", false)]
    [InlineData("hello world", false)]
    public void IsLikelyBinaryFormat_DetectsCorrectly(string code, bool expected) =>
        Assert.Equal(expected, ImportFromCodeDetector.IsLikelyBinaryFormat(code));

    [Fact]
    public void CountDataBytes_BinaryWithPrefixes_CountsCorrectly()
    {
        const string code = "0b01010101, B11110000, 00001111";
        Assert.Equal(3, ImportFromCodeDetector.CountDataBytes(code));
    }

    [Fact]
    public void TryParseExplicitDimensions_WithoutLeadingUnderscore_WIDTH()
    {
        const string code = """
            const int WIDTH = 8;
            const int HEIGHT = 8;
            const uint8_t data[] = { 0x00 };
            """;
        Assert.True(ImportFromCodeDetector.TryParseExplicitDimensions(code, out int w, out int h, out string src));
        Assert.Equal(8, w);
        Assert.Equal(8, h);
        Assert.Equal("constants", src);
    }

    [Fact]
    public void TryParseExplicitDimensions_DefineWidthHeight()
    {
        const string code = """
            #define WIDTH 16
            #define HEIGHT 16
            """;
        Assert.True(ImportFromCodeDetector.TryParseExplicitDimensions(code, out int w, out int h, out string src));
        Assert.Equal(16, w);
        Assert.Equal(16, h);
        Assert.Equal("constants", src);
    }

    [Fact]
    public void TryParseExplicitDimensions_CamelCaseHeartWidth()
    {
        const string code = """
            int heartWidth = 8;
            int heartHeight = 8;
            """;
        Assert.True(ImportFromCodeDetector.TryParseExplicitDimensions(code, out int w, out int h, out string src));
        Assert.Equal(8, w);
        Assert.Equal(8, h);
        Assert.Equal("constants", src);
    }

    [Fact]
    public void DetectBytesPerRow_StripsTrailingCommentsWithHex()
    {
        const string code = """
            const uint8_t data[] = {
                0x01, // 0x02 0x03 0x04 ignored
                0x05, // 0x06 0x07 0x08 ignored
            };
            """;
        Assert.Equal(1, ImportFromCodeDetector.DetectBytesPerRow(code));
    }

    [Fact]
    public void CountDataBytes_UserSmileySnippet_Counts8Bytes()
    {
        const string code = """
            const uint8_t sprite_width = 8;
            const uint8_t sprite_height = 8;

            const uint8_t smiley_sprite[] = {
                0b00111100, //   ****  
                0b01000010, //  *    * 
                0b10100101, // * *  * *
                0b10000001, // *      *
                0b10100101, // * *  * *
                0b10011001, // *  **  *
                0b01000010, //  *    * 
                0b00111100  //   ****  
            };
            """;
        Assert.Equal(8, ImportFromCodeDetector.CountDataBytes(code));
    }

    [Fact]
    public void TryParseExplicitDimensions_HexConstants()
    {
        const string code = """
            #define WIDTH 0x20
            #define HEIGHT 0x10
            """;
        Assert.True(ImportFromCodeDetector.TryParseExplicitDimensions(code, out int w, out int h, out string src));
        Assert.Equal(32, w);
        Assert.Equal(16, h);
        Assert.Equal("constants", src);
    }

    [Fact]
    public void TryParseExplicitDimensions_CommentDimension()
    {
        const string code = """
            // 16x16 icon bitmap
            const uint8_t icon[] = { 0x00, 0x01 };
            """;
        Assert.True(ImportFromCodeDetector.TryParseExplicitDimensions(code, out int w, out int h, out string src));
        Assert.Equal(16, w);
        Assert.Equal(16, h);
        Assert.Equal("comment dimension", src);
    }

    [Fact]
    public void CountDataBytes_DecimalArray_CountsAllBytes()
    {
        const string code = """
            const uint8_t heart[] = {
                60, 66, 165, 129, 165, 153, 66, 60
            };
            """;
        Assert.Equal(8, ImportFromCodeDetector.CountDataBytes(code));
    }

    [Fact]
    public void CountDataBytes_ArrayWithExplicitSizeBracket_CountsExactlyDataBytes()
    {
        const string code = """
            const unsigned char sprite_invader[8] = {
                0x3C, // 00111100
                0x7E, // 01111110
                0xFF, // 11111111
                0xDB, // 11011011
                0xFF, // 11111111
                0x24, // 00100100
                0x5A, // 01011010
                0xA5  // 10100101
            };
            """;
        Assert.Equal(8, ImportFromCodeDetector.CountDataBytes(code));
    }

    [Fact]
    public void TryInferDimensionsFromData_SingleLineArrayWithBrackets_InfersDimensionsCorrectly()
    {
        const string code = "uint8_t sprite[8] = { 0x3C, 0x7E, 0xFF, 0xDB, 0xFF, 0x24, 0x5A, 0xA5 };";
        Assert.True(ImportFromCodeDetector.TryInferDimensionsFromData(code, 8, out int w, out int h, out var hint));
        Assert.Equal(8, w);
        Assert.Equal(8, h);
    }

    [Fact]
    public void CountDataBytes_16BitHexWords_CountsEachWordAsTwoBytes()
    {
        const string code = "const uint16_t sprite[] = { 0x3C42, 0x7EDB };";
        // 2 16-bit words = 4 bytes
        Assert.Equal(4, ImportFromCodeDetector.CountDataBytes(code));
    }

    [Fact]
    public void CountDataBytes_CommentsWithBraces_DoesNotCountCommentBytes()
    {
        const string code = """
            // Example: { 0x00, 0x00, 0x00 }
            /* Multi-line:
               { 0x11, 0x22 } */
            const uint8_t s[] = { 0xFF, 0xEE };
            """;
        Assert.Equal(2, ImportFromCodeDetector.CountDataBytes(code));
    }

    [Fact]
    public void CountDataBytes_PythonHashCommentsWithNumbers_DoesNotCountCommentNumbers()
    {
        const string code = """
            s = bytearray([
                60, # Row 0 has 8 pixels and 123 is a number
                66  # Row 1 has 8 pixels
            ])
            """;
        Assert.Equal(2, ImportFromCodeDetector.CountDataBytes(code));
    }

    [Fact]
    public void TryParseExplicitDimensions_2DArray_ExtractsWidthAndHeight()
    {
        const string code = """
            const uint8_t palette[4][3] = {
                {0, 0, 0}, {255, 0, 0}, {255, 255, 255}, {0, 0, 255}
            };
            const uint8_t player_sprite[4][4] = {
                {0, 1, 1, 0},
                {1, 2, 2, 1},
                {1, 3, 3, 1},
                {0, 1, 1, 0}
            };
            """;
        Assert.True(ImportFromCodeDetector.TryParseExplicitDimensions(code, out int w, out int h, out string src));
        Assert.Equal(4, w);
        Assert.Equal(4, h);
        Assert.Equal("2D array [H][W]", src);
    }

    [Fact]
    public void DetectVariableName_WithPaletteAndSprite_ExtractsSpriteName()
    {
        const string code = """
            const uint8_t palette[4][3] = { {0, 0, 0} };
            const uint8_t player_sprite[4][4] = { {0, 1, 1, 0} };
            """;
        Assert.Equal("player_sprite", ImportFromCodeDetector.DetectVariableName(code));
    }

    [Fact]
    public void IsLikely2DMatrixFormat_ReturnsTrueFor2DArray()
    {
        const string code = "const uint8_t sprite[8][8] = { {0} };";
        Assert.True(ImportFromCodeDetector.IsLikely2DMatrixFormat(code));
    }

    [Fact]
    public void ExpectedByteCount_Indexed2D_ReturnsWidthTimesHeight()
    {
        Assert.Equal(16, ImportFromCodeDetector.ExpectedByteCount(4, 4, ExportFormat.Indexed2D));
        Assert.Equal(64, ImportFromCodeDetector.ExpectedByteCount(8, 8, ExportFormat.Indexed2D));
    }

    [Fact]
    public void CountDataBytes_PaletteAndSprite_IsolatesSpriteAndReturns16Bytes()
    {
        const string code = """
            const uint8_t palette[4][3] = {
                {0, 0, 0}, {255, 0, 0}, {255, 255, 255}, {0, 0, 255}
            };
            const uint8_t player_sprite[4][4] = {
                {0, 1, 1, 0},
                {1, 2, 2, 1},
                {1, 3, 3, 1},
                {0, 1, 1, 0}
            };
            """;
        Assert.Equal(16, ImportFromCodeDetector.CountDataBytes(code));
    }

    [Fact]
    public void TryParseExplicitDimensions_PythonNested2DList_ExtractsDimensions()
    {
        const string code = """
            player = [
                [0, 1, 1, 0],
                [1, 2, 2, 1],
                [1, 3, 3, 1],
                [0, 1, 1, 0]
            ]
            """;
        Assert.True(ImportFromCodeDetector.TryParseExplicitDimensions(code, out int w, out int h, out string src));
        Assert.Equal(4, w);
        Assert.Equal(4, h);
        Assert.Equal("nested rows (4×4)", src);
        Assert.Equal("player", ImportFromCodeDetector.DetectVariableName(code));
        Assert.True(ImportFromCodeDetector.IsLikely2DMatrixFormat(code));
    }

    [Fact]
    public void TryParseExplicitDimensions_COpenFirstDimension_ExtractsDimensions()
    {
        const string code = """
            const uint8_t sprite[][8] = {
                {0, 1, 1, 0, 0, 1, 1, 0},
                {1, 1, 1, 1, 1, 1, 1, 1}
            };
            """;
        Assert.True(ImportFromCodeDetector.TryParseExplicitDimensions(code, out int w, out int h, out string src));
        Assert.Equal(8, w);
        Assert.Equal(2, h);
        Assert.Equal("2D array [][8]", src);
        Assert.Equal("sprite", ImportFromCodeDetector.DetectVariableName(code));
    }

    [Fact]
    public void TryParseExplicitDimensions_NestedRowsWithoutBracketDimensions_ExtractsDimensions()
    {
        const string code = """
            const uint8_t heart[] = {
                {0, 1, 0, 1, 0},
                {1, 1, 1, 1, 1},
                {1, 1, 1, 1, 1},
                {0, 1, 1, 1, 0},
                {0, 0, 1, 0, 0}
            };
            """;
        Assert.True(ImportFromCodeDetector.TryParseExplicitDimensions(code, out int w, out int h, out string src));
        Assert.Equal(5, w);
        Assert.Equal(5, h);
        Assert.Equal("nested rows (5×5)", src);
    }

    [Fact]
    public void DetectVariableName_JavaScript2DArray_ExtractsName()
    {
        const string code = """
            const icon_sprite = [
                [1, 0, 1],
                [0, 1, 0]
            ];
            """;
        Assert.Equal("icon_sprite", ImportFromCodeDetector.DetectVariableName(code));
        Assert.True(ImportFromCodeDetector.IsLikely2DMatrixFormat(code));
    }

    [Fact]
    public void TryGuess2DMatrixDimensionsFromByteCount_ReturnsExpectedDimensions()
    {
        Assert.True(ImportFromCodeDetector.TryGuess2DMatrixDimensionsFromByteCount(16, out int w16, out int h16));
        Assert.Equal(4, w16);
        Assert.Equal(4, h16);

        Assert.True(ImportFromCodeDetector.TryGuess2DMatrixDimensionsFromByteCount(64, out int w64, out int h64));
        Assert.Equal(8, w64);
        Assert.Equal(8, h64);

        Assert.True(ImportFromCodeDetector.TryGuess2DMatrixDimensionsFromByteCount(20, out int w20, out int h20));
        Assert.True((w20 == 5 && h20 == 4) || (w20 == 4 && h20 == 5));
    }

    [Fact]
    public void TryParseExplicitDimensions_WideShipSpriteRowPacked_Extracts16x8()
    {
        const string code = """
            // Width: 16 pixels, Height: 8 pixels (Total: 16 bytes)
            // Each row consists of 2 consecutive bytes: [Byte 0, Byte 1]
            const uint8_t wide_ship_sprite[8][2] = {
                {0x01, 0x80}, // Row 0: 00000001 10000000
                {0x03, 0xC0}, // Row 1: 00000011 11000000
                {0x1F, 0xF8}, // Row 2: 00011111 11111000
                {0x7F, 0xFE}, // Row 3: 01111111 11111110
                {0xFF, 0xFF}, // Row 4: 11111111 11111111
                {0xCC, 0x33}, // Row 5: 11001100 00110011
                {0x0C, 0x30}, // Row 6: 00001100 00110000
                {0x03, 0xC0}  // Row 7: 00000011 11000000
            };
            """;

        Assert.True(ImportFromCodeDetector.TryParseExplicitDimensions(code, out int w, out int h, out string src));
        Assert.Equal(16, w);
        Assert.Equal(8, h);
        Assert.False(ImportFromCodeDetector.IsLikely2DMatrixFormat(code));
        Assert.Equal(16, ImportFromCodeDetector.CountDataBytes(code));
        Assert.Equal("wide_ship_sprite", ImportFromCodeDetector.DetectVariableName(code));
    }

    [Fact]
    public void TryParseExplicitDimensions_WideShipSpriteWithoutComments_Extracts16x8()
    {
        const string code = """
            const uint8_t wide_ship_sprite[8][2] = {
                {0x01, 0x80},
                {0x03, 0xC0},
                {0x1F, 0xF8},
                {0x7F, 0xFE},
                {0xFF, 0xFF},
                {0xCC, 0x33},
                {0x0C, 0x30},
                {0x03, 0xC0}
            };
            """;

        Assert.True(ImportFromCodeDetector.TryParseExplicitDimensions(code, out int w, out int h, out string src));
        Assert.Equal(16, w);
        Assert.Equal(8, h);
        Assert.False(ImportFromCodeDetector.IsLikely2DMatrixFormat(code));
        Assert.Equal(16, ImportFromCodeDetector.CountDataBytes(code));
    }

    [Fact]
    public void TryParseExplicitDimensions_EyeAnimation2DArray_Extracts8x8Frame()
    {
        const string code = """
            const uint8_t eye_animation[2][8] = {
                // Frame 0: Eye Open
                { 0x3C, 0x42, 0x81, 0x99, 0x99, 0x81, 0x42, 0x3C },
                
                // Frame 1: Eye Closed (Blinking)
                { 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0x00 }
            };
            """;

        Assert.True(ImportFromCodeDetector.TryParseExplicitDimensions(code, out int w, out int h, out string src));
        Assert.Equal(8, w);
        Assert.Equal(8, h);
        Assert.False(ImportFromCodeDetector.IsLikely2DMatrixFormat(code));
        Assert.Equal("eye_animation", ImportFromCodeDetector.DetectVariableName(code));
    }

    [Fact]
    public void TryParseExplicitDimensions_BlockAnimationWithDefineMacro_Extracts8x8()
    {
        const string code = """
            #define FRAME_SIZE 8

            // 3 Frames of an alternating 8x8 flashing brick animation (1-bit)
            const unsigned char block_animation[3][FRAME_SIZE] = {
                // Frame 0: Border outline
                { 0xFF, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0xFF },
                
                // Frame 1: Cross hatch pattern
                { 0xAA, 0x55, 0xAA, 0x55, 0xAA, 0x55, 0xAA, 0x55 },
                
                // Frame 2: Solid square
                { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }
            };

            // Accessing code inside game loops:
            // unsigned char *current_frame = block_animation[frame_counter % 3];
            """;

        Assert.True(ImportFromCodeDetector.TryParseExplicitDimensions(code, out int w, out int h, out string src));
        Assert.Equal(8, w);
        Assert.Equal(8, h);
        Assert.False(ImportFromCodeDetector.IsLikely2DMatrixFormat(code));
        Assert.Equal(24, ImportFromCodeDetector.CountDataBytes(code));
        Assert.Equal("block_animation", ImportFromCodeDetector.DetectVariableName(code));
    }

    [Fact]
    public void TryParseExplicitDimensions_ConstVariableDimension_ExtractsDimensions()
    {
        const string code = """
            const int SPRITE_BYTES = 8;
            const uint8_t anim[4][SPRITE_BYTES] = {
                { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
                { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }
            };
            """;

        Assert.True(ImportFromCodeDetector.TryParseExplicitDimensions(code, out int w, out int h, out string src));
        Assert.Equal(8, w);
        Assert.Equal(8, h);
        Assert.False(ImportFromCodeDetector.IsLikely2DMatrixFormat(code));
    }

    [Fact]
    public void IsLikely2DMatrixFormat_SwordSpriteWithPalette_ReturnsTrue()
    {
        const string code = """
            // Define the system color palette (RGB values)
            const unsigned long palette[4] = {
                0x000000, // Index 0: Black
                0xFF0000, // Index 1: Red
                0x00FF00, // Index 2: Green
                0xFFFF00  // Index 3: Yellow
            };

            // 4x4 Multi-color Sprite
            // 1 byte per pixel. Each number is an index referencing the palette above.
            const unsigned char sword_sprite[16] = {
                0, 1, 1, 0,
                1, 3, 3, 1,
                1, 2, 2, 1,
                0, 1, 1, 0
            };
            """;

        Assert.True(ImportFromCodeDetector.IsLikely2DMatrixFormat(code));
        Assert.True(ImportFromCodeDetector.TryParseExplicitDimensions(code, out int w, out int h, out _));
        Assert.Equal(4, w);
        Assert.Equal(4, h);
        Assert.Equal(16, ImportFromCodeDetector.CountDataBytes(code));
        Assert.Equal("sword_sprite", ImportFromCodeDetector.DetectVariableName(code));
    }

    [Fact]
    public void IsLikely2DMatrixFormat_1DPaletteMappedArrayWithoutCommentKeywords_ReturnsTrue()
    {
        const string code = """
            const uint32_t colors[4] = { 0x000000, 0xFF0000, 0x00FF00, 0x0000FF };

            // 8x8 icon
            const uint8_t player[64] = {
                0, 0, 1, 1, 1, 1, 0, 0,
                0, 1, 2, 2, 2, 2, 1, 0,
                1, 2, 3, 2, 2, 3, 2, 1,
                1, 2, 2, 2, 2, 2, 2, 1,
                1, 2, 3, 3, 3, 3, 2, 1,
                1, 2, 2, 2, 2, 2, 2, 1,
                0, 1, 2, 2, 2, 2, 1, 0,
                0, 0, 1, 1, 1, 1, 0, 0
            };
            """;

        Assert.True(ImportFromCodeDetector.IsLikely2DMatrixFormat(code));
        Assert.True(ImportFromCodeDetector.TryParseExplicitDimensions(code, out int w, out int h, out _));
        Assert.Equal(8, w);
        Assert.Equal(8, h);
        Assert.Equal(64, ImportFromCodeDetector.CountDataBytes(code));
    }
}
