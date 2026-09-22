using System.Text.RegularExpressions;
using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
public sealed partial class CodeGeneratorServiceTests
{
    private static byte[] OrderedHexLiterals(string code)
    {
        MatchCollection matches = HexLiteralRegex().Matches(code);
        var bytes = new byte[matches.Count];
        for (int i = 0; i < matches.Count; i++)
            bytes[i] = Convert.ToByte(matches[i].Groups["h"].Value, 16);
        return bytes;
    }

    [GeneratedRegex(@"0[xX](?<h>[0-9a-fA-F]{2})")]
    private static partial Regex HexLiteralRegex();
    private static ExportSettings MinimalExport(Action<ExportSettings>? configure = null)
    {
        var s = new ExportSettings
        {
            Format = ExportFormat.AdafruitGfx,
            SpriteName = "testSprite",
            IncludeUsageComment = false,
            IncludeDimensionConstants = false,
            IncludeArraySize = false,
            UseCommaSeparator = true,
            BytesPerLine = 0,
            UppercaseHex = true,
            IncludeRowComments = false,
        };
        configure?.Invoke(s);
        return s;
    }

    private static void SetPixelsRowMajor(SpriteState state, ReadOnlySpan<bool> values)
    {
        Assert.Equal(state.Width * state.Height, values.Length);
        for (int i = 0; i < values.Length; i++)
            state.Pixels[i] = values[i];
    }

    private static bool[] ParseRawHexLines(string rawHexOutput, int width, int height)
    {
        var result = new bool[width * height];
        string[] lines = rawHexOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(height, lines.Length);
        int bytesPerRow = (int)Math.Ceiling(width / 8.0);

        for (int row = 0; row < height; row++)
        {
            string[] tokens = lines[row].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.Equal(bytesPerRow, tokens.Length);
            for (int chunk = 0; chunk < bytesPerRow; chunk++)
            {
                string t = tokens[chunk].Trim();
                Assert.StartsWith("0x", t, StringComparison.OrdinalIgnoreCase);
                byte b = Convert.ToByte(t[2..], 16);
                for (int bit = 7; bit >= 0; bit--)
                {
                    int col = (chunk * 8) + (7 - bit);
                    if (col < width)
                        result[(row * width) + col] = ((b >> bit) & 1) == 1;
                }
            }
        }

        return result;
    }

    [Theory]
    [InlineData("", "sprite")]
    [InlineData("   ", "sprite")]
    [InlineData("my sprite", "my_sprite")]
    [InlineData("123", "_123")]
    [InlineData("9abc", "_9abc")]
    [InlineData("___", "___")]
    [InlineData("!@#", "___")]
    public void SanitiseName_NormalisesIdentifiers(string? input, string expected)
    {
        Assert.Equal(expected, CodeGeneratorService.SanitiseName(input));
    }

    [Fact]
    public void SanitiseName_Null_ReturnsSprite()
    {
        Assert.Equal("sprite", CodeGeneratorService.SanitiseName(null));
    }

    [Fact]
    public void GenerateCode_UnknownFormat_ReturnsEmptyString()
    {
        var state = new SpriteState(8, 1);
        var svc = new CodeGeneratorService();
        var settings = new ExportSettings { Format = (ExportFormat)999 };
        Assert.Equal(string.Empty, svc.GenerateCode(new System.Collections.Generic.List<bool[]> { state.Pixels }, state.Width, state.Height, settings, false, null, 0, 0, 0, 0));
    }

    [Theory]
    [InlineData(ExportFormat.AdafruitGfx, "PROGMEM", "0x80")]
    [InlineData(ExportFormat.U8g2DrawBitmap, "U8X8_PROGMEM", "0x80")]
    [InlineData(ExportFormat.U8g2DrawXBM, "PROGMEM", "0x01")]
    [InlineData(ExportFormat.PlainCArray, "const uint8_t testSprite[]", "0x80")]
    [InlineData(ExportFormat.MicroPython, "bytearray", "0x80")]
    public void GenerateCode_EachFormat_IncludesDistinctMarker(
        ExportFormat format, string expectedSubstring, string expectedFirstByte)
    {
        var state = new SpriteState(8, 1);
        state.Pixels[0] = true;
        var svc = new CodeGeneratorService();
        ExportSettings settings = MinimalExport(s =>
        {
            s.Format = format;
            s.SpriteName = "testSprite";
        });

        string code = svc.GenerateCode(new System.Collections.Generic.List<bool[]> { state.Pixels }, state.Width, state.Height, settings, false, null, 0, 0, 0, 0);
        Assert.Contains(expectedSubstring, code);
        Assert.Contains(expectedFirstByte, code);
    }

    [Fact]
    public void GenerateCode_RawHex_RespectsSeparatorAndHexCase()
    {
        // Two byte columns so comma-separated output actually appears between chunks.
        var state = new SpriteState(9, 1);
        state.Pixels[0] = true;
        var svc = new CodeGeneratorService();

        var commaUpper = new ExportSettings { Format = ExportFormat.RawHex, UseCommaSeparator = true, UppercaseHex = true };
        Assert.Contains(", ", svc.GenerateCode(new System.Collections.Generic.List<bool[]> { state.Pixels }, state.Width, state.Height, commaUpper, false, null, 0, 0, 0, 0));
        Assert.Contains("0x80", svc.GenerateCode(new System.Collections.Generic.List<bool[]> { state.Pixels }, state.Width, state.Height, commaUpper, false, null, 0, 0, 0, 0));

        var spaceLower = new ExportSettings { Format = ExportFormat.RawHex, UseCommaSeparator = false, UppercaseHex = false };
        string lower = svc.GenerateCode(new System.Collections.Generic.List<bool[]> { state.Pixels }, state.Width, state.Height, spaceLower, false, null, 0, 0, 0, 0);
        Assert.Contains("0x80", lower);
        Assert.DoesNotContain("0X", lower);
    }

    [Fact]
    public void GenerateCode_RawBinary_MatchesPixelPattern()
    {
        var state = new SpriteState(4, 1);
        // MSB-first within the single byte: columns 0..3 map to bits 7..4
        state.Pixels[0] = true;
        state.Pixels[1] = false;
        state.Pixels[2] = true;
        state.Pixels[3] = false;
        var svc = new CodeGeneratorService();
        var settings = new ExportSettings { Format = ExportFormat.RawBinary, UseCommaSeparator = false };
        string line = svc.GenerateCode(new System.Collections.Generic.List<bool[]> { state.Pixels }, state.Width, state.Height, settings, false, null, 0, 0, 0, 0);
        Assert.Equal("10100000", line);
    }

    [Fact]
    public void GenerateCode_IncludeOptions_EmitExpectedStructure()
    {
        var state = new SpriteState(8, 1);
        var svc = new CodeGeneratorService();
        var settings = new ExportSettings
        {
            Format = ExportFormat.AdafruitGfx,
            SpriteName = "foo",
            IncludeUsageComment = true,
            IncludeDimensionConstants = true,
            IncludeArraySize = true,
            IncludeRowComments = true,
            BytesPerLine = 0,
            UseCommaSeparator = true,
            UppercaseHex = true,
        };

        string code = svc.GenerateCode(new System.Collections.Generic.List<bool[]> { state.Pixels }, state.Width, state.Height, settings, false, null, 0, 0, 0, 0);
        Assert.Contains("display.drawBitmap", code);
        Assert.Contains("FOO_WIDTH", code);
        Assert.Contains("FOO_HEIGHT", code);
        Assert.Contains("foo[1]", code);
        Assert.Contains("// row 0", code);
    }

    [Fact]
    public void RoundTrip_AdafruitGfx_MsbFirst_PreservesPixels()
    {
        var original = new SpriteState(8, 2);
        SetPixelsRowMajor(original, new[]
        {
            true, false, true, false, false, false, false, false,
            false, false, false, false, false, false, false, true,
        });

        var svc = new CodeGeneratorService();
        ExportSettings settings = MinimalExport(s =>
        {
            s.Format = ExportFormat.AdafruitGfx;
            s.SpriteName = "rt";
        });
        string code = svc.GenerateCode(new System.Collections.Generic.List<bool[]> { original.Pixels }, original.Width, original.Height, settings, false, null, 0, 0, 0, 0);

        var roundTrip = new SpriteState(8, 2);
        svc.ParseAdafruitGfxToState(code, roundTrip);

        Assert.Equal(original.Pixels, roundTrip.Pixels);
    }

    [Fact]
    public void RoundTrip_PlainCArray_ParseAdafruitGfx_PreservesPixels()
    {
        var original = new SpriteState(5, 1);
        SetPixelsRowMajor(original, new[] { true, true, false, true, false });

        var svc = new CodeGeneratorService();
        ExportSettings settings = MinimalExport(s => s.Format = ExportFormat.PlainCArray);
        string code = svc.GenerateCode(new System.Collections.Generic.List<bool[]> { original.Pixels }, original.Width, original.Height, settings, false, null, 0, 0, 0, 0);

        var roundTrip = new SpriteState(5, 1);
        svc.ParseAdafruitGfxToState(code, roundTrip);

        Assert.Equal(original.Pixels, roundTrip.Pixels);
    }

    [Fact]
    public void RoundTrip_U8g2DrawXbm_PreservesPixels()
    {
        var original = new SpriteState(8, 2);
        SetPixelsRowMajor(original, new[]
        {
            false, true, true, false, false, true, true, false,
            true, true, true, true, false, false, false, false,
        });

        var svc = new CodeGeneratorService();
        ExportSettings settings = MinimalExport(s =>
        {
            s.Format = ExportFormat.U8g2DrawXBM;
            s.SpriteName = "xbm_rt";
        });
        string code = svc.GenerateCode(new System.Collections.Generic.List<bool[]> { original.Pixels }, original.Width, original.Height, settings, false, null, 0, 0, 0, 0);

        var roundTrip = new SpriteState(8, 2);
        svc.ParseXbmToState(code, roundTrip);

        Assert.Equal(original.Pixels, roundTrip.Pixels);
    }

    [Fact]
    public void RoundTrip_RawHex_MatchesDirectEncode()
    {
        var original = new SpriteState(8, 2);
        SetPixelsRowMajor(original, new[]
        {
            true, false, false, false, false, false, false, false,
            false, true, false, false, false, false, false, false,
        });

        var svc = new CodeGeneratorService();
        var settings = new ExportSettings { Format = ExportFormat.RawHex, UseCommaSeparator = true, UppercaseHex = true };
        string raw = svc.GenerateCode(new System.Collections.Generic.List<bool[]> { original.Pixels }, original.Width, original.Height, settings, false, null, 0, 0, 0, 0);

        bool[] decoded = ParseRawHexLines(raw, original.Width, original.Height);
        Assert.Equal(original.Pixels, decoded);
    }

    [Fact]
    public void RoundTrip_RawBinary_WithColumnPrefix_PreservesPixels()
    {
        var original = new SpriteState(3, 2);
        SetPixelsRowMajor(original, new[]
        {
            true, false, true,
            false, true, false,
        });

        var svc = new CodeGeneratorService();
        var settings = new ExportSettings { Format = ExportFormat.RawBinary, UseCommaSeparator = true };
        string raw = svc.GenerateCode(new System.Collections.Generic.List<bool[]> { original.Pixels }, original.Width, original.Height, settings, false, null, 0, 0, 0, 0);
        string[] rows = raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, rows.Length);
        string withPrefix = $"0: {rows[0]}{Environment.NewLine}1: {rows[1]}";

        var restored = new SpriteState(3, 2);
        svc.ParseBinaryToState(withPrefix, restored);

        Assert.Equal(original.Pixels, restored.Pixels);
    }

    [Fact]
    public void ParseBinaryToState_NoRowPrefix_SpacesCommasSkippedBetweenDigits()
    {
        var state = new SpriteState(4, 2);
        var svc = new CodeGeneratorService();
        svc.ParseBinaryToState("1010 , 0\n 010  1", state);

        Assert.True(state.Pixels[0]); Assert.False(state.Pixels[1]); Assert.True(state.Pixels[2]); Assert.False(state.Pixels[3]);
        Assert.False(state.Pixels[4]); Assert.True(state.Pixels[5]); Assert.False(state.Pixels[6]); Assert.True(state.Pixels[7]);
    }

    [Fact]
    public void ParseBinaryToState_TruncatesWhenFewerBitsThanWidthPerRow()
    {
        var state = new SpriteState(8, 1);
        var svc = new CodeGeneratorService();
        svc.ParseBinaryToState("111", state);
        Assert.True(state.Pixels[0]); Assert.True(state.Pixels[1]); Assert.True(state.Pixels[2]);
        Assert.All(state.Pixels.AsSpan(3).ToArray(), Assert.False);
    }

    [Fact]
    public void RoundTrip_U8g2DrawBitmap_ParseAdafruitGfx_PreservesPixels()
    {
        var original = new SpriteState(8, 1);
        original.Pixels[0] = true;
        original.Pixels[7] = true;

        var svc = new CodeGeneratorService();
        ExportSettings settings = MinimalExport(s =>
        {
            s.Format = ExportFormat.U8g2DrawBitmap;
            s.SpriteName = "bmp_u8";
        });
        string code = svc.GenerateCode(new System.Collections.Generic.List<bool[]> { original.Pixels }, original.Width, original.Height, settings, false, null, 0, 0, 0, 0);

        var roundTrip = new SpriteState(8, 1);
        svc.ParseAdafruitGfxToState(code, roundTrip);
        Assert.Equal(original.Pixels, roundTrip.Pixels);
    }

    [Fact]
    public void RoundTrip_MicroPython_ParseAdafruitGfx_PreservesPixels()
    {
        var original = new SpriteState(8, 1);
        SetPixelsRowMajor(original, new[] { false, true, false, true, false, true, false, true });

        var svc = new CodeGeneratorService();
        ExportSettings settings = MinimalExport(s =>
        {
            s.Format = ExportFormat.MicroPython;
            s.SpriteName = "pybits";
        });
        string code = svc.GenerateCode(new System.Collections.Generic.List<bool[]> { original.Pixels }, original.Width, original.Height, settings, false, null, 0, 0, 0, 0);

        var roundTrip = new SpriteState(8, 1);
        svc.ParseAdafruitGfxToState(code, roundTrip);
        Assert.Equal(original.Pixels, roundTrip.Pixels);
    }

    [Fact]
    public void ParseAdafruitGfxToState_WithDimensionsDefined_ReadsOnlyBraceLiterals()
    {
        var state = new SpriteState(8, 1);
        var svc = new CodeGeneratorService();
        const string code =
            """
            #define ICON_WIDTH 8
            #define ICON_HEIGHT 1
            const uint8_t PROGMEM icon[] = { /* mask */ 0x81 };
            """;

        svc.ParseAdafruitGfxToState(code, state);

        Assert.True(state.Pixels[0]); Assert.False(state.Pixels[6]); Assert.True(state.Pixels[7]);
        Assert.All(state.Pixels.AsSpan(1, 6).ToArray(), Assert.False);
    }

    [Fact]
    public void ParseHexToState_StripsComments_AndHandlesCase()
    {
        var state = new SpriteState(8, 1);
        var svc = new CodeGeneratorService();
        string code = """
            // leading
            0xaA /* mid */ ,
            """; // MSB stripe: alternating pattern in first byte → 10101010
        svc.ParseHexToState(code, state);

        for (int col = 0; col < state.Width; col++)
            Assert.Equal(col % 2 == 0, state.Pixels[col]);
    }

    [Fact]
    public void ParseHexToState_PartialInput_StopsGracefullyWithoutThrowing()
    {
        var state = new SpriteState(16, 1);
        var svc = new CodeGeneratorService();
        svc.ParseHexToState("0x80", state);
        Assert.True(state.Pixels[0]);
        Assert.False(state.Pixels[15]);
    }

    [Fact]
    public void GenerateCode_Floating_OverridesBaseTransparentPixel()
    {
        var state = new SpriteState(8, 1);
        Array.Clear(state.Pixels, 0, state.Pixels.Length);
        bool[,] floatSel = new bool[2, 1];
        floatSel[0, 0] = false;
        floatSel[1, 0] = true;

        var svc = new CodeGeneratorService();
        var settings = new ExportSettings { Format = ExportFormat.RawHex, UseCommaSeparator = true };

        string code = svc.GenerateCode(new System.Collections.Generic.List<bool[]> { state.Pixels }, state.Width, state.Height, settings, true, floatSel, 6, 0, 2, 1);
        Assert.Contains("0x01", code);
    }

    [Fact]
    public async Task GenerateCodeAsync_DoesNotMutateOriginalStatePixels()
    {
        var state = new SpriteState(8, 1);
        state.Layers.Clear();
        state.Layers.Add(new LayerState
        {
            Name = "L1",
            IsVisible = true,
            Pixels = new[] { false, false, false, false, false, false, false, false },
        });
        state.ActiveLayerIndex = 0;
        state.NormalizeLayerState();

        bool[] exported = new[] { true, false, false, false, false, false, false, false };
        state.Pixels = exported;
        var snapshotBefore = (bool[])exported.Clone();

        var svc = new CodeGeneratorService();
        var settings = new ExportSettings { Format = ExportFormat.RawHex, UseCommaSeparator = true };

        string code = await svc.GenerateCodeAsync(new System.Collections.Generic.List<bool[]> { state.Pixels }, state.Width, state.Height, settings, false, null, 0, 0, 0, 0);
        Assert.Contains("0x80", code);

        Assert.True(ReferenceEquals(exported, state.Pixels));
        Assert.Equal(snapshotBefore, state.Pixels);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public void GenerateCode_BytesPerLine_DoesNotChangeEncodedByteSequence(int bytesPerLine)
    {
        var state = new SpriteState(24, 1);
        state.Pixels[0] = true;
        state.Pixels[16] = true;

        var reference = new byte[] { 0x80, 0x00, 0x80 };
        var svc = new CodeGeneratorService();
        ExportSettings settings = MinimalExport(s =>
        {
            s.Format = ExportFormat.PlainCArray;
            s.BytesPerLine = bytesPerLine;
        });

        byte[] parsed = OrderedHexLiterals(svc.GenerateCode(new System.Collections.Generic.List<bool[]> { state.Pixels }, state.Width, state.Height, settings, false, null, 0, 0, 0, 0));
        Assert.Equal(reference, parsed);
    }

    [Fact]
    public void GenerateCode_IsDisplayInverted_DoesNotChangeOutput()
    {
        var state = new SpriteState(8, 1);
        state.Pixels[0] = true;
        var svc = new CodeGeneratorService();
        ExportSettings settings = MinimalExport(s => s.Format = ExportFormat.RawHex);

        string normal = svc.GenerateCode(new System.Collections.Generic.List<bool[]> { state.Pixels }, state.Width, state.Height, settings, false, null, 0, 0, 0, 0);
        state.IsDisplayInverted = true;
        string flagged = svc.GenerateCode(new System.Collections.Generic.List<bool[]> { state.Pixels }, state.Width, state.Height, settings, false, null, 0, 0, 0, 0);
        Assert.Equal(normal, flagged);
    }

    [Fact]
    public void GenerateCode_StructuredFormats_IgnoresUseCommaToggle_StillCommaBetweenIntermediateBytes()
    {
        var state = new SpriteState(16, 1);
        state.Pixels[0] = true;
        var svc = new CodeGeneratorService();
        ExportSettings settings = MinimalExport(s =>
        {
            s.Format = ExportFormat.PlainCArray;
            s.UseCommaSeparator = false;
        });

        string code = svc.GenerateCode(new System.Collections.Generic.List<bool[]> { state.Pixels }, state.Width, state.Height, settings, false, null, 0, 0, 0, 0);
        Assert.Contains(", ", code);
        Assert.Contains("0x80,", code);
    }

    [Fact]
    public void GenerateCode_RawHex_UseCommaSeparatorFalse_UsesSpacesBetweenChunks()
    {
        var state = new SpriteState(9, 1);
        state.Pixels[0] = true;
        var svc = new CodeGeneratorService();
        var settings = new ExportSettings
        {
            Format = ExportFormat.RawHex,
            UseCommaSeparator = false,
            UppercaseHex = true,
        };

        string line = svc.GenerateCode(new System.Collections.Generic.List<bool[]> { state.Pixels }, state.Width, state.Height, settings, false, null, 0, 0, 0, 0);
        Assert.Contains("0x80 0x00", line);
        Assert.DoesNotContain(", ", line);
    }

    [Fact]
    public void GenerateCode_FloatingPixelsIgnored_WhenNotFloating()
    {
        var state = new SpriteState(8, 1);
        Array.Clear(state.Pixels, 0, state.Pixels.Length);
        bool[,] floatSel = new bool[1, 1];
        floatSel[0, 0] = true;

        var svc = new CodeGeneratorService();
        var settings = new ExportSettings { Format = ExportFormat.RawHex, UseCommaSeparator = true };
        string code = svc.GenerateCode(new System.Collections.Generic.List<bool[]> { state.Pixels }, state.Width, state.Height, settings, false, floatSel, 0, 0, 1, 1);
        Assert.Contains("0x00", code);
        Assert.DoesNotContain("0x80", code);
    }

    [Fact]
    public void GenerateCode_RowComments_AttachOnlyToEndOfCanvasRow_WhenBytesPerLineSplitsRow()
    {
        var state = new SpriteState(16, 2);
        state.Pixels[0] = true;
        state.Pixels[state.Width + 1] = true;

        var svc = new CodeGeneratorService();
        ExportSettings settings = MinimalExport(s =>
        {
            s.Format = ExportFormat.PlainCArray;
            s.IncludeRowComments = true;
            s.BytesPerLine = 1;
        });

        string code = svc.GenerateCode(new System.Collections.Generic.List<bool[]> { state.Pixels }, state.Width, state.Height, settings, false, null, 0, 0, 0, 0);
        Assert.Single(Regex.Matches(code, @"// row 0\b"));
        Assert.Single(Regex.Matches(code, @"// row 1\b"));
    }

    [Fact]
    public void ParseHexToState_EmptyInput_LeavesCanvasClear()
    {
        var state = new SpriteState(8, 1);
        Array.Fill(state.Pixels, true);
        var svc = new CodeGeneratorService();
        svc.ParseHexToState("", state);
        Assert.All(state.Pixels, Assert.False);
    }

    [Fact]
    public void ParseHexToState_BlockCommentCanSpanLines()
    {
        var state = new SpriteState(8, 2);
        var svc = new CodeGeneratorService();
        string code =
            "0xAA/* line1\n" +
            "line2 */0x55";
        svc.ParseHexToState(code, state);

        // One byte consumed per raster row — second literal must not share the row striping test with the first byte.
        for (int c = 0; c < 8; c++)
            Assert.Equal(c % 2 == 0, state.Pixels[c]);
        for (int c = 0; c < 8; c++)
            Assert.Equal(c % 2 == 1, state.Pixels[8 + c]);
    }

    [Fact]
    public void ParseXbmToState_IgnoresPreambleOutsideBracedBody()
    {
        var original = new SpriteState(8, 1);
        original.Pixels[0] = true;
        original.Pixels[3] = true;

        var svc = new CodeGeneratorService();
        ExportSettings settings = MinimalExport(s => s.Format = ExportFormat.U8g2DrawXBM);
        string export = svc.GenerateCode(new System.Collections.Generic.List<bool[]> { original.Pixels }, original.Width, original.Height, settings, false, null, 0, 0, 0, 0);

        string wrapped = "// noise 0xFF 0xFE (must not confuse import)\n#pragma once\n" + export;

        var roundTrip = new SpriteState(8, 1);
        svc.ParseXbmToState(wrapped, roundTrip);
        Assert.Equal(original.Pixels, roundTrip.Pixels);
    }

    [Fact]
    public async Task GenerateCodeAsync_LeavesActiveLayerBufferUnchanged_WhenProjectionDiffersFromLayer()
    {
        var state = new SpriteState(8, 1);
        state.Layers.Clear();
        bool[] layerBuf = new bool[8]; // stays all false while export uses overridden projection
        state.Layers.Add(new LayerState { Name = "L1", IsVisible = true, Pixels = layerBuf });
        state.ActiveLayerIndex = 0;
        state.NormalizeLayerState();

        bool[] exported = new[] { true, false, false, false, false, false, false, false };
        state.Pixels = exported;

        var svc = new CodeGeneratorService();
        var settings = new ExportSettings { Format = ExportFormat.RawHex, UseCommaSeparator = true };

        await svc.GenerateCodeAsync(new System.Collections.Generic.List<bool[]> { state.Pixels }, state.Width, state.Height, settings, false, null, 0, 0, 0, 0);
        Assert.All(layerBuf, Assert.False);

        Assert.Contains("0x80", svc.GenerateCode(new System.Collections.Generic.List<bool[]> { state.Pixels }, state.Width, state.Height, settings, false, null, 0, 0, 0, 0));
    }

    [Fact]
    public void SanitiseName_ReplacesUnicodeAndSymbolsWithUnderscores()
    {
        Assert.Equal("__a_b_c", CodeGeneratorService.SanitiseName("🙂a⚡b⋯c"));
        // 🙂 is UTF-16 surrogate pair → Regex.Replace substitutes two underscores
        Assert.Equal("sprite__", CodeGeneratorService.SanitiseName("sprite🙂"));
    }

    [Fact]
    public void GenerateCode_AnimationWithOneIncompressibleFrame_DisablesCompressionForWholeExport()
    {
        // Frame A: all-off pixels → compresses very well under RLE.
        bool[] frameA = new bool[64];

        // Frame B: exactly one distinct bit set per 8-pixel byte chunk → every packed byte
        // is unique, so no run of ≥3 identical bytes exists and RLE.Compress falls back to
        // returning the original byte array unchanged for this frame.
        bool[] frameB = new bool[64];
        for (int chunk = 0; chunk < 8; chunk++)
            frameB[(chunk * 8) + chunk] = true;

        var svc = new CodeGeneratorService(new Hexprite.Services.Compression.CompressionService());
        var settings = MinimalExport(s =>
        {
            s.Compression = CompressionMode.Rle;
            s.ExportAsAnimation = true;
            s.AnimationLayout = AnimationExportLayout.ArrayOfFrames;
        });

        string code = svc.GenerateCode(
            new System.Collections.Generic.List<bool[]> { frameA, frameB },
            64, 1, settings, false, null, 0, 0, 0, 0);

        // Because one frame couldn't be compressed, compression must be disabled for the
        // whole export — otherwise frame B's raw bytes would be embedded under the shared
        // RLE decompressor and misdecoded on-device.
        Assert.DoesNotContain("Compressed with", code);
    }

    [Theory]
    [InlineData(AnimationExportLayout.VerticalSpriteSheet)]
    [InlineData(AnimationExportLayout.HorizontalSpriteSheet)]
    public void CalculateByteCount_SpriteSheetAnimationWithCompression_DoesNotThrow(AnimationExportLayout layout)
    {
        // Each frame is small (4x4 = 2 bytes/row * 4 rows = 8 bytes) but the sprite-sheet
        // layout flattens all frames into one large canvas before sizing/compressing.
        // CalculateByteCount must mirror that flattening instead of indexing the original
        // (small) per-frame arrays with the enlarged sheet dimensions.
        var frames = new System.Collections.Generic.List<bool[]>
        {
            new bool[16], new bool[16], new bool[16]
        };
        var compression = new Hexprite.Services.Compression.CompressionService();
        var settings = MinimalExport(s =>
        {
            s.Compression = CompressionMode.Rle;
            s.ExportAsAnimation = true;
            s.AnimationLayout = layout;
        });

        int byteCount = CodeGeneratorService.CalculateByteCount(frames, 4, 4, settings, compression);

        Assert.True(byteCount > 0);
    }

    [Fact]
    public void GenerateCode_LargeSprite_EmitsUint16DimensionConstants()
    {
        // Sprite canvas can be up to 512px (SpriteState.MaxDimension); a uint8_t constant
        // would silently wrap (e.g. 300 -> 44) once baked into the generated C code.
        var state = new SpriteState(300, 1);
        var svc = new CodeGeneratorService();
        var settings = MinimalExport(s =>
        {
            s.Format = ExportFormat.AdafruitGfx;
            s.IncludeDimensionConstants = true;
        });

        string code = svc.GenerateCode(new System.Collections.Generic.List<bool[]> { state.Pixels }, state.Width, state.Height, settings, false, null, 0, 0, 0, 0);

        Assert.Contains("const uint16_t TESTSPRITE_WIDTH  = 300;", code);
        Assert.DoesNotContain("const uint8_t TESTSPRITE_WIDTH", code);
    }

    [Fact]
    public void ParseBinaryToState_ReimportsExportedRawBinaryAnimationFrame_WithoutCorruption()
    {
        bool[] frame1 = { true, false, true, false, false, false, false, false }; // 0xA0 -> "10100000"
        bool[] frame2 = { false, true, false, true, false, false, false, false }; // 0x50 -> "01010000"

        var svc = new CodeGeneratorService();
        var settings = MinimalExport(s =>
        {
            s.Format = ExportFormat.RawBinary;
            s.ExportAsAnimation = true;
            s.AnimationLayout = AnimationExportLayout.ArrayOfFrames;
        });

        string code = svc.GenerateCode(
            new System.Collections.Generic.List<bool[]> { frame1, frame2 },
            8, 1, settings, false, null, 0, 0, 0, 0);

        // The animation header/frame-marker lines must be stripped as comments before
        // parsing, or a stray digit inside them (e.g. the "1" in "1 frames") gets misread
        // as pixel data and shifts/corrupts the real frame content.
        var state = new SpriteState(8, 1);
        svc.ParseBinaryToState(code, state);

        Assert.Equal(frame1, state.Pixels);
    }

    [Fact]
    public void GenerateCode_CompressedArrayOfFramesAnimation_EmitsDelays()
    {
        bool[] frameA = new bool[64]; // all-off -> compresses well under RLE
        bool[] frameB = new bool[64];
        frameB[0] = true; // one differing pixel so frames aren't identical

        var svc = new CodeGeneratorService(new Hexprite.Services.Compression.CompressionService());
        var settings = MinimalExport(s =>
        {
            s.Compression = CompressionMode.Rle;
            s.ExportAsAnimation = true;
            s.AnimationLayout = AnimationExportLayout.ArrayOfFrames;
        });
        var delays = new System.Collections.Generic.List<int> { 1, 5 };

        string code = svc.GenerateCode(
            new System.Collections.Generic.List<bool[]> { frameA, frameB },
            64, 1, settings, false, null, 0, 0, 0, 0,
            frameDelays: delays);

        Assert.Contains("_DELAYS", code);
    }

    [Theory]
    [InlineData("for", "for_")]
    [InlineData("class", "class_")]
    [InlineData("int", "int_")]
    [InlineData("None", "None_")]
    public void SanitiseName_RejectsReservedKeywords(string input, string expected)
    {
        Assert.Equal(expected, CodeGeneratorService.SanitiseName(input));
    }

    [Fact]
    public void GenerateCode_LargeFrameDelay_ClampsInsteadOfWrapping()
    {
        bool[] frameA = new bool[8];
        bool[] frameB = new bool[8];
        frameB[0] = true;

        var svc = new CodeGeneratorService();
        var settings = MinimalExport(s =>
        {
            s.ExportAsAnimation = true;
            s.AnimationLayout = AnimationExportLayout.VerticalSpriteSheet;
        });
        // 256 would wrap to 0 under "& 0xFF"; clamping should saturate to 255 (0xFF) instead.
        var delays = new System.Collections.Generic.List<int> { 1, 256 };

        string code = svc.GenerateCode(
            new System.Collections.Generic.List<bool[]> { frameA, frameB },
            8, 1, settings, false, null, 0, 0, 0, 0,
            frameDelays: delays);

        Assert.Contains("0xFF", code);
    }

    [Fact]
    public void GenerateSketch_AdafruitGfx_SingleSprite_GeneratesCompleteInoSketch()
    {
        bool[] frame = new bool[64];
        frame[0] = true;

        var svc = new CodeGeneratorService();
        var settings = MinimalExport(s =>
        {
            s.Format = ExportFormat.AdafruitGfx;
            s.SpriteName = "testPlayer";
        });

        string sketch = svc.GenerateSketch(
            new System.Collections.Generic.List<bool[]> { frame },
            8, 8, settings, false, null, 0, 0, 0, 0);

        Assert.Contains("#include <Adafruit_GFX.h>", sketch);
        Assert.Contains("#include <Adafruit_SSD1306.h>", sketch);
        Assert.Contains("Adafruit_SSD1306 display(", sketch);
        Assert.Contains("void setup()", sketch);
        Assert.Contains("(int16_t)max(0, ((int)SCREEN_WIDTH - (int)TESTPLAYER_WIDTH) / 2)", sketch);
        Assert.Contains("(int16_t)max(0, ((int)SCREEN_HEIGHT - (int)TESTPLAYER_HEIGHT) / 2)", sketch);
        Assert.Contains("display.drawBitmap(x, y, testPlayer, TESTPLAYER_WIDTH, TESTPLAYER_HEIGHT, SSD1306_WHITE);", sketch);
        Assert.Contains("void loop()", sketch);
    }

    [Fact]
    public void GenerateSketch_AdafruitGfx_Animation_GeneratesFrameLoop()
    {
        bool[] frameA = new bool[64];
        bool[] frameB = new bool[64];
        frameB[0] = true;

        var svc = new CodeGeneratorService();
        var settings = MinimalExport(s =>
        {
            s.Format = ExportFormat.AdafruitGfx;
            s.SpriteName = "walkAnim";
            s.ExportAsAnimation = true;
            s.AnimationLayout = AnimationExportLayout.ArrayOfFrames;
            s.FrameRateFps = 10;
        });

        string sketch = svc.GenerateSketch(
            new System.Collections.Generic.List<bool[]> { frameA, frameB },
            8, 8, settings, false, null, 0, 0, 0, 0);

        Assert.Contains("for (int i = 0; i < 2; i++)", sketch);
        Assert.Contains("display.drawBitmap(x, y, walkAnim[i],", sketch);
        Assert.Contains("delay(1000 / WALKANIM_FPS);", sketch);
    }

    [Fact]
    public void GenerateSketch_AdafruitGfx_SpriteSheet_UsesFpsConstant()
    {
        bool[] frameA = new bool[64];
        bool[] frameB = new bool[64];

        var svc = new CodeGeneratorService();
        var settings = MinimalExport(s =>
        {
            s.Format = ExportFormat.AdafruitGfx;
            s.SpriteName = "sheetAnim";
            s.ExportAsAnimation = true;
            s.AnimationLayout = AnimationExportLayout.HorizontalSpriteSheet;
            s.FrameRateFps = 15;
        });

        string sketch = svc.GenerateSketch(
            new System.Collections.Generic.List<bool[]> { frameA, frameB },
            8, 8, settings, false, null, 0, 0, 0, 0);

        Assert.Contains("delay(1000 / SHEETANIM_FPS);", sketch);
    }

    [Fact]
    public void GenerateSketch_U8g2DrawBitmap_GeneratesValidU8g2Sketch()
    {
        bool[] frame = new bool[64];
        frame[0] = true;

        var svc = new CodeGeneratorService();
        var settings = MinimalExport(s =>
        {
            s.Format = ExportFormat.U8g2DrawBitmap;
            s.SpriteName = "u8g2Sprite";
        });

        string sketch = svc.GenerateSketch(
            new System.Collections.Generic.List<bool[]> { frame },
            8, 8, settings, false, null, 0, 0, 0, 0);

        Assert.Contains("#include <U8g2lib.h>", sketch);
        Assert.Contains("U8G2_SSD1306_128X64_NONAME_F_HW_I2C u8g2(", sketch);
        Assert.Contains("u8g2.begin();", sketch);
        Assert.Contains("(int16_t)max(0, ((int)u8g2.getDisplayWidth() - (int)U8G2SPRITE_WIDTH) / 2)", sketch);
        Assert.Contains("u8g2.drawBitmap(x, y, 1, U8G2SPRITE_HEIGHT, u8g2Sprite);", sketch);
        Assert.Contains("u8g2.sendBuffer();", sketch);
    }

    [Fact]
    public void GenerateSketch_U8g2DrawXBM_Uncompressed_UsesDrawXBMP()
    {
        bool[] frame = new bool[64];
        frame[0] = true;

        var svc = new CodeGeneratorService();
        var settings = MinimalExport(s =>
        {
            s.Format = ExportFormat.U8g2DrawXBM;
            s.SpriteName = "xbmIcon";
        });

        string sketch = svc.GenerateSketch(
            new System.Collections.Generic.List<bool[]> { frame },
            8, 8, settings, false, null, 0, 0, 0, 0);

        // Flash PROGMEM data must use drawXBMP
        Assert.Contains("u8g2.drawXBMP(x, y, XBMICON_WIDTH, XBMICON_HEIGHT, xbmIcon);", sketch);
        Assert.DoesNotContain("u8g2.drawXBM(x, y, XBMICON_WIDTH, XBMICON_HEIGHT, xbmIcon);", sketch);
    }

    [Fact]
    public void GenerateSketch_U8g2DrawXBM_Animation_UsesDrawXBMP()
    {
        bool[] frameA = new bool[64];
        bool[] frameB = new bool[64];

        var svc = new CodeGeneratorService();
        var settings = MinimalExport(s =>
        {
            s.Format = ExportFormat.U8g2DrawXBM;
            s.SpriteName = "xbmAnim";
            s.ExportAsAnimation = true;
            s.AnimationLayout = AnimationExportLayout.ArrayOfFrames;
        });

        string sketch = svc.GenerateSketch(
            new System.Collections.Generic.List<bool[]> { frameA, frameB },
            8, 8, settings, false, null, 0, 0, 0, 0);

        Assert.Contains("u8g2.drawXBMP(x, y, XBMANIM_WIDTH, XBMANIM_HEIGHT, xbmAnim[i]);", sketch);
    }

    [Fact]
    public void GenerateSketch_U8g2DrawXBM_Compressed_UsesDrawXBMForRamBuffer()
    {
        bool[] frame = new bool[128];
        frame[0] = true;

        var svc = new CodeGeneratorService(new Hexprite.Services.Compression.CompressionService());
        var settings = MinimalExport(s =>
        {
            s.Format = ExportFormat.U8g2DrawXBM;
            s.SpriteName = "compXbm";
            s.Compression = CompressionMode.Rle;
        });

        string sketch = svc.GenerateSketch(
            new System.Collections.Generic.List<bool[]> { frame },
            16, 8, settings, false, null, 0, 0, 0, 0);

        // RAM decompressed buffer must use drawXBM
        Assert.Contains("u8g2.drawXBM(x, y, COMPXBM_WIDTH, COMPXBM_HEIGHT, buffer);", sketch);
    }

    [Fact]
    public void GenerateSketch_MicroPython_GeneratesCompletePythonScript()
    {
        bool[] frameA = new bool[64];
        bool[] frameB = new bool[64];
        frameB[1] = true;

        var svc = new CodeGeneratorService();
        var settings = MinimalExport(s =>
        {
            s.Format = ExportFormat.MicroPython;
            s.SpriteName = "pyAlien";
            s.ExportAsAnimation = true;
            s.AnimationLayout = AnimationExportLayout.ArrayOfFrames;
            s.FrameRateFps = 8;
        });

        string script = svc.GenerateSketch(
            new System.Collections.Generic.List<bool[]> { frameA, frameB },
            8, 8, settings, false, null, 0, 0, 0, 0);

        Assert.Contains("import ssd1306", script);
        Assert.Contains("import framebuf", script);
        Assert.Contains("from machine import Pin, I2C", script);
        Assert.Contains("oled.blit(fb, x, y)", script);
        Assert.Contains("while True:", script);
        Assert.Contains("framebuf.FrameBuffer(frame, PYALIEN_WIDTH, PYALIEN_HEIGHT, framebuf.MONO_HLSB, ((PYALIEN_WIDTH + 7) // 8) * 8)", script);
    }

    [Fact]
    public void GenerateSketch_MicroPython_ArbitraryWidth_SuppliesByteAlignedStride()
    {
        // 10x10 sprite: width is not multiple of 8, so stride calculation ((10 + 7) // 8) * 8 = 16
        bool[] frame = new bool[100];
        var svc = new CodeGeneratorService();
        var settings = MinimalExport(s =>
        {
            s.Format = ExportFormat.MicroPython;
            s.SpriteName = "oddSprite";
        });

        string script = svc.GenerateSketch(
            new System.Collections.Generic.List<bool[]> { frame },
            10, 10, settings, false, null, 0, 0, 0, 0);

        Assert.Contains("framebuf.FrameBuffer(oddSprite, ODDSPRITE_WIDTH, ODDSPRITE_HEIGHT, framebuf.MONO_HLSB, ((ODDSPRITE_WIDTH + 7) // 8) * 8)", script);
    }

    [Fact]
    public void GenerateSketch_PlainCArray_GeneratesHarness()
    {
        bool[] frame = new bool[64];
        var svc = new CodeGeneratorService();
        var settings = MinimalExport(s =>
        {
            s.Format = ExportFormat.PlainCArray;
            s.SpriteName = "cData";
        });

        string harness = svc.GenerateSketch(
            new System.Collections.Generic.List<bool[]> { frame },
            8, 8, settings, false, null, 0, 0, 0, 0);

        Assert.Contains("#include <stdint.h>", harness);
        Assert.Contains("#if defined(ARDUINO)", harness);
        Assert.Contains("(unsigned int)CDATA_WIDTH", harness);
        Assert.Contains("void setup()", harness);
        Assert.Contains("int main(void)", harness);
    }

    [Fact]
    public void GenerateSketch_WithCompression_GeneratesDecompressorCall()
    {
        bool[] frame = new bool[64];
        frame[0] = true;

        var svc = new CodeGeneratorService(new Hexprite.Services.Compression.CompressionService());
        var settings = MinimalExport(s =>
        {
            s.Format = ExportFormat.AdafruitGfx;
            s.SpriteName = "compSprite";
            s.Compression = CompressionMode.Rle;
        });

        string sketch = svc.GenerateSketch(
            new System.Collections.Generic.List<bool[]> { frame },
            8, 8, settings, false, null, 0, 0, 0, 0);

        Assert.Contains("hexprite_rle_decode", sketch);
        Assert.Contains("uint8_t buffer[COMPSPRITE_UNCOMPRESSED_SIZE];", sketch);
    }

    [Fact]
    public void GenerateSketch_WithIncludeDimensionConstantsFalse_StillEmitsRequiredConstants()
    {
        bool[] frame = new bool[64];
        frame[0] = true;

        var svc = new CodeGeneratorService();
        var settings = MinimalExport(s =>
        {
            s.Format = ExportFormat.AdafruitGfx;
            s.SpriteName = "noDim";
            s.IncludeDimensionConstants = false;
        });

        string sketch = svc.GenerateSketch(
            new System.Collections.Generic.List<bool[]> { frame },
            8, 8, settings, false, null, 0, 0, 0, 0);

        Assert.Contains("const uint16_t NODIM_WIDTH  = 8;", sketch);
        Assert.Contains("const uint16_t NODIM_HEIGHT = 8;", sketch);
    }

    [Fact]
    public void GenerateSketch_RawHexAndRawBinary_EmitsHeaderAndData()
    {
        bool[] frame = new bool[16];
        frame[0] = true;

        var svc = new CodeGeneratorService();
        var settingsHex = MinimalExport(s =>
        {
            s.Format = ExportFormat.RawHex;
            s.SpriteName = "rawH";
        });
        var settingsBin = MinimalExport(s =>
        {
            s.Format = ExportFormat.RawBinary;
            s.SpriteName = "rawB";
        });

        string hexSketch = svc.GenerateSketch(
            new System.Collections.Generic.List<bool[]> { frame },
            4, 4, settingsHex, false, null, 0, 0, 0, 0);
        string binSketch = svc.GenerateSketch(
            new System.Collections.Generic.List<bool[]> { frame },
            4, 4, settingsBin, false, null, 0, 0, 0, 0);

        Assert.Contains("Generated by Hexprite — Raw Hex", hexSketch);
        Assert.Contains("Generated by Hexprite — Raw Binary", binSketch);
        Assert.Contains("Width: 4px, Height: 4px", hexSketch);
        Assert.Contains("Width: 4px, Height: 4px", binSketch);
    }

    [Fact]
    public void GenerateCode_WithGenerateFullSketchTrue_EqualsGenerateSketch()
    {
        bool[] frame = new bool[64];
        frame[0] = true;

        var svc = new CodeGeneratorService();
        var settingsA = MinimalExport(s =>
        {
            s.Format = ExportFormat.AdafruitGfx;
            s.SpriteName = "matchTest";
            s.GenerateFullSketch = true;
        });
        var settingsB = MinimalExport(s =>
        {
            s.Format = ExportFormat.AdafruitGfx;
            s.SpriteName = "matchTest";
            s.GenerateFullSketch = false;
        });

        string codeFromGenerateCode = svc.GenerateCode(
            new System.Collections.Generic.List<bool[]> { frame },
            8, 8, settingsA, false, null, 0, 0, 0, 0);

        string codeFromGenerateSketch = svc.GenerateSketch(
            new System.Collections.Generic.List<bool[]> { frame },
            8, 8, settingsB, false, null, 0, 0, 0, 0);

        Assert.Equal(codeFromGenerateSketch, codeFromGenerateCode);
    }

    [Fact]
    public void ParseBinaryToState_UserSmileySpriteSnippet_ImportsPixelPerfect()
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

        var state = new SpriteState(8, 8);
        var svc = new CodeGeneratorService();
        svc.ParseBinaryToState(code, state);

        // Row 0: 0b00111100 -> ..****..
        Assert.False(state.Pixels[0]); Assert.False(state.Pixels[1]);
        Assert.True(state.Pixels[2]); Assert.True(state.Pixels[3]); Assert.True(state.Pixels[4]); Assert.True(state.Pixels[5]);
        Assert.False(state.Pixels[6]); Assert.False(state.Pixels[7]);

        // Row 1: 0b01000010 -> . *....* .
        Assert.False(state.Pixels[8 + 0]); Assert.True(state.Pixels[8 + 1]);
        Assert.False(state.Pixels[8 + 2]); Assert.False(state.Pixels[8 + 3]); Assert.False(state.Pixels[8 + 4]); Assert.False(state.Pixels[8 + 5]);
        Assert.True(state.Pixels[8 + 6]); Assert.False(state.Pixels[8 + 7]);

        // Row 2: 0b10100101 -> *.*..*.*
        Assert.True(state.Pixels[16 + 0]); Assert.False(state.Pixels[16 + 1]); Assert.True(state.Pixels[16 + 2]);
        Assert.False(state.Pixels[16 + 3]); Assert.False(state.Pixels[16 + 4]);
        Assert.True(state.Pixels[16 + 5]); Assert.False(state.Pixels[16 + 6]); Assert.True(state.Pixels[16 + 7]);

        // Row 3: 0b10000001 -> *......*
        Assert.True(state.Pixels[24 + 0]); Assert.False(state.Pixels[24 + 1]);
        Assert.False(state.Pixels[24 + 6]); Assert.True(state.Pixels[24 + 7]);

        // Row 4: 0b10100101 -> *.*..*.*
        Assert.True(state.Pixels[32 + 0]); Assert.False(state.Pixels[32 + 1]); Assert.True(state.Pixels[32 + 2]);
        Assert.False(state.Pixels[32 + 3]); Assert.False(state.Pixels[32 + 4]);
        Assert.True(state.Pixels[32 + 5]); Assert.False(state.Pixels[32 + 6]); Assert.True(state.Pixels[32 + 7]);

        // Row 5: 0b10011001 -> *..**..*
        Assert.True(state.Pixels[40 + 0]); Assert.False(state.Pixels[40 + 1]); Assert.False(state.Pixels[40 + 2]);
        Assert.True(state.Pixels[40 + 3]); Assert.True(state.Pixels[40 + 4]);
        Assert.False(state.Pixels[40 + 5]); Assert.False(state.Pixels[40 + 6]); Assert.True(state.Pixels[40 + 7]);

        // Row 6: 0b01000010 -> .*....*.
        Assert.False(state.Pixels[48 + 0]); Assert.True(state.Pixels[48 + 1]);
        Assert.True(state.Pixels[48 + 6]); Assert.False(state.Pixels[48 + 7]);

        // Row 7: 0b00111100 -> ..****..
        Assert.False(state.Pixels[56 + 0]); Assert.False(state.Pixels[56 + 1]);
        Assert.True(state.Pixels[56 + 2]); Assert.True(state.Pixels[56 + 3]); Assert.True(state.Pixels[56 + 4]); Assert.True(state.Pixels[56 + 5]);
        Assert.False(state.Pixels[56 + 6]); Assert.False(state.Pixels[56 + 7]);
    }

    [Fact]
    public void ParseAdafruitGfxToState_BinaryLiterals_ImportsPixelPerfect()
    {
        const string code = "const uint8_t icon[] PROGMEM = { 0b10000001, 0b01000010 };";
        var state = new SpriteState(8, 2);
        var svc = new CodeGeneratorService();
        svc.ParseAdafruitGfxToState(code, state);

        Assert.True(state.Pixels[0]); Assert.True(state.Pixels[7]);
        Assert.True(state.Pixels[8 + 1]); Assert.True(state.Pixels[8 + 6]);
    }

    [Fact]
    public void ParseBinaryToState_SingleLineBinaryLiterals_PreservesPixels()
    {
        const string code = "const uint8_t icon[] = { 0b11000000, 0b00110000, 0b00001100, 0b00000011 };";
        var state = new SpriteState(8, 4);
        var svc = new CodeGeneratorService();
        svc.ParseBinaryToState(code, state);

        Assert.True(state.Pixels[0]); Assert.True(state.Pixels[1]);
        Assert.True(state.Pixels[8 + 2]); Assert.True(state.Pixels[8 + 3]);
        Assert.True(state.Pixels[16 + 4]); Assert.True(state.Pixels[16 + 5]);
        Assert.True(state.Pixels[24 + 6]); Assert.True(state.Pixels[24 + 7]);
    }

    [Fact]
    public void ParseAdafruitGfxToState_DecimalByteLiterals_ImportsPixelPerfect()
    {
        // 60 = 0b00111100, 66 = 0b01000010
        const string code = "const uint8_t heart[] = { 60, 66 };";
        var state = new SpriteState(8, 2);
        var svc = new CodeGeneratorService();
        svc.ParseAdafruitGfxToState(code, state);

        // Row 0 (60 = 00111100)
        Assert.False(state.Pixels[0]);
        Assert.True(state.Pixels[2]); Assert.True(state.Pixels[3]); Assert.True(state.Pixels[4]); Assert.True(state.Pixels[5]);
        Assert.False(state.Pixels[7]);

        // Row 1 (66 = 01000010)
        Assert.False(state.Pixels[8]);
        Assert.True(state.Pixels[8 + 1]);
        Assert.True(state.Pixels[8 + 6]);
        Assert.False(state.Pixels[8 + 7]);
    }

    [Fact]
    public void ParseAdafruitGfxToState_16BitHexWords_SplitsIntoHighAndLowBytes()
    {
        // 0x3C42 = 0x3C (00111100) followed by 0x42 (01000010)
        const string code = "const uint16_t sprite[] = { 0x3C42 };";
        var state = new SpriteState(8, 2);
        var svc = new CodeGeneratorService();
        svc.ParseAdafruitGfxToState(code, state);

        // Row 0 (0x3C = 00111100)
        Assert.False(state.Pixels[0]);
        Assert.True(state.Pixels[2]); Assert.True(state.Pixels[3]); Assert.True(state.Pixels[4]); Assert.True(state.Pixels[5]);
        Assert.False(state.Pixels[7]);

        // Row 1 (0x42 = 01000010)
        Assert.False(state.Pixels[8]);
        Assert.True(state.Pixels[8 + 1]);
        Assert.True(state.Pixels[8 + 6]);
        Assert.False(state.Pixels[8 + 7]);
    }

    [Fact]
    public void ParseAdafruitGfxToState_UserInvaderArrayWithExplicitSize_ImportsPixelPerfect()
    {
        const string code = @"
const unsigned char sprite_invader[8] = {
    0x3C, // 00111100
    0x7E, // 01111110
    0xFF, // 11111111
    0xDB, // 11011011
    0xFF, // 11111111
    0x24, // 00100100
    0x5A, // 01011010
    0xA5  // 10100101
};";
        var state = new SpriteState(8, 8);
        var svc = new CodeGeneratorService();
        svc.ParseAdafruitGfxToState(code, state);

        // Row 0: 0x3C = 00111100 (..****..)
        Assert.False(state.Pixels[0]); Assert.False(state.Pixels[1]);
        Assert.True(state.Pixels[2]); Assert.True(state.Pixels[3]); Assert.True(state.Pixels[4]); Assert.True(state.Pixels[5]);
        Assert.False(state.Pixels[6]); Assert.False(state.Pixels[7]);

        // Row 1: 0x7E = 01111110 (.******.)
        Assert.False(state.Pixels[8]);
        Assert.True(state.Pixels[9]); Assert.True(state.Pixels[10]); Assert.True(state.Pixels[11]);
        Assert.True(state.Pixels[12]); Assert.True(state.Pixels[13]); Assert.True(state.Pixels[14]);
        Assert.False(state.Pixels[15]);

        // Row 2: 0xFF = 11111111 (********)
        for (int i = 16; i < 24; i++) Assert.True(state.Pixels[i]);

        // Row 3: 0xDB = 11011011 (**.**.**)
        Assert.True(state.Pixels[24]); Assert.True(state.Pixels[25]);
        Assert.False(state.Pixels[26]); // Eye
        Assert.True(state.Pixels[27]); Assert.True(state.Pixels[28]);
        Assert.False(state.Pixels[29]); // Eye
        Assert.True(state.Pixels[30]); Assert.True(state.Pixels[31]);

        // Row 4: 0xFF = 11111111 (********)
        for (int i = 32; i < 40; i++) Assert.True(state.Pixels[i]);

        // Row 5: 0x24 = 00100100 (..*..*..)
        Assert.False(state.Pixels[40]); Assert.False(state.Pixels[41]);
        Assert.True(state.Pixels[42]);
        Assert.False(state.Pixels[43]); Assert.False(state.Pixels[44]);
        Assert.True(state.Pixels[45]);
        Assert.False(state.Pixels[46]); Assert.False(state.Pixels[47]);

        // Row 6: 0x5A = 01011010 (.*.**.*.)
        Assert.False(state.Pixels[48]);
        Assert.True(state.Pixels[49]);
        Assert.False(state.Pixels[50]);
        Assert.True(state.Pixels[51]); Assert.True(state.Pixels[52]);
        Assert.False(state.Pixels[53]);
        Assert.True(state.Pixels[54]);
        Assert.False(state.Pixels[55]);

        // Row 7: 0xA5 = 10100101 (*.*..*.*)
        Assert.True(state.Pixels[56]);
        Assert.False(state.Pixels[57]);
        Assert.True(state.Pixels[58]);
        Assert.False(state.Pixels[59]); Assert.False(state.Pixels[60]);
        Assert.True(state.Pixels[61]);
        Assert.False(state.Pixels[62]);
        Assert.True(state.Pixels[63]);
    }

    [Fact]
    public void ParseAdafruitGfxToState_DecimalArrayWithBracketSize_IgnoresBracketSize()
    {
        const string code = "const uint8_t sprite[2] = { 60, 66 };";
        var state = new SpriteState(8, 2);
        var svc = new CodeGeneratorService();
        svc.ParseAdafruitGfxToState(code, state);

        // Row 0 should be 60 = 00111100 (NOT the 2 from [2])
        Assert.False(state.Pixels[0]);
        Assert.True(state.Pixels[2]); Assert.True(state.Pixels[3]); Assert.True(state.Pixels[4]); Assert.True(state.Pixels[5]);
        Assert.False(state.Pixels[7]);

        // Row 1 should be 66 = 01000010
        Assert.False(state.Pixels[8]);
        Assert.True(state.Pixels[8 + 1]);
        Assert.True(state.Pixels[8 + 6]);
        Assert.False(state.Pixels[8 + 7]);
    }

    [Fact]
    public void ParseAdafruitGfxToState_CommentsContainingBraces_DoesNotHijackArrayExtraction()
    {
        const string code = @"
// Example: { 0x00, 0x00 }
/* Multi-line example:
   { 0x11, 0x22 } */
# Python-style comment: { 0x33 }
const uint8_t heart[] = { 0x3C, 0x42 };";

        var state = new SpriteState(8, 2);
        var svc = new CodeGeneratorService();
        svc.ParseAdafruitGfxToState(code, state);

        // Row 0: 0x3C = 00111100
        Assert.False(state.Pixels[0]);
        Assert.True(state.Pixels[2]); Assert.True(state.Pixels[3]); Assert.True(state.Pixels[4]); Assert.True(state.Pixels[5]);
        Assert.False(state.Pixels[7]);

        // Row 1: 0x42 = 01000010
        Assert.False(state.Pixels[8]);
        Assert.True(state.Pixels[8 + 1]);
        Assert.True(state.Pixels[8 + 6]);
        Assert.False(state.Pixels[8 + 7]);
    }

    [Fact]
    public void ParseAdafruitGfxToState_PythonHashCommentsWithNumbers_DoesNotInjectDataBytes()
    {
        const string code = @"
sprite = bytearray([
    60, # Row 0: width is 8 pixels and 123 is a number
    66  # Row 1: height is 2 pixels
])";

        var state = new SpriteState(8, 2);
        var svc = new CodeGeneratorService();
        svc.ParseAdafruitGfxToState(code, state);

        // Row 0: 60 = 00111100
        Assert.False(state.Pixels[0]);
        Assert.True(state.Pixels[2]); Assert.True(state.Pixels[3]); Assert.True(state.Pixels[4]); Assert.True(state.Pixels[5]);
        Assert.False(state.Pixels[7]);

        // Row 1: 66 = 01000010
        Assert.False(state.Pixels[8]);
        Assert.True(state.Pixels[8 + 1]);
        Assert.True(state.Pixels[8 + 6]);
        Assert.False(state.Pixels[8 + 7]);
    }

    [Fact]
    public void ParseBinaryToState_AsciiBitGrid16Wide_ParsesLineByLine()
    {
        const string code = @"
1111000011110000
0000111100001111";

        var state = new SpriteState(16, 2);
        var svc = new CodeGeneratorService();
        svc.ParseBinaryToState(code, state);

        // Row 0: 1111000011110000
        for (int i = 0; i < 4; i++) Assert.True(state.Pixels[i]);
        for (int i = 4; i < 8; i++) Assert.False(state.Pixels[i]);
        for (int i = 8; i < 12; i++) Assert.True(state.Pixels[i]);
        for (int i = 12; i < 16; i++) Assert.False(state.Pixels[i]);

        // Row 1: 0000111100001111
        for (int i = 16; i < 20; i++) Assert.False(state.Pixels[i]);
        for (int i = 20; i < 24; i++) Assert.True(state.Pixels[i]);
        for (int i = 24; i < 28; i++) Assert.False(state.Pixels[i]);
        for (int i = 28; i < 32; i++) Assert.True(state.Pixels[i]);
    }

    [Fact]
    public void ParseIndexed2DToState_PlayerSpriteWithPalette_ImportsPixelPerfect()
    {
        const string code = @"
// Define a small 4-color palette (RGB values)
const uint8_t palette[4][3] = {
    {0,   0,   0},   // 0: Transparent/Black
    {255, 0,   0},   // 1: Red
    {255, 255, 255}, // 2: White
    {0,   0,   255}  // 3: Blue
};

// A 4x4 pixel sprite using the palette indexes above
const uint8_t player_sprite[4][4] = {
    {0, 1, 1, 0},
    {1, 2, 2, 1},
    {1, 3, 3, 1},
    {0, 1, 1, 0}
};";

        var state = new SpriteState(4, 4);
        var svc = new CodeGeneratorService();
        svc.ParseIndexed2DToState(code, state);

        // Row 0: {0, 1, 1, 0} -> . * * .
        Assert.False(state.Pixels[0]);
        Assert.True(state.Pixels[1]);
        Assert.True(state.Pixels[2]);
        Assert.False(state.Pixels[3]);

        // Row 1: {1, 2, 2, 1} -> * * * *
        Assert.True(state.Pixels[4]);
        Assert.True(state.Pixels[5]);
        Assert.True(state.Pixels[6]);
        Assert.True(state.Pixels[7]);

        // Row 2: {1, 3, 3, 1} -> * * * *
        Assert.True(state.Pixels[8]);
        Assert.True(state.Pixels[9]);
        Assert.True(state.Pixels[10]);
        Assert.True(state.Pixels[11]);

        // Row 3: {0, 1, 1, 0} -> . * * .
        Assert.False(state.Pixels[12]);
        Assert.True(state.Pixels[13]);
        Assert.True(state.Pixels[14]);
        Assert.False(state.Pixels[15]);
    }

    [Fact]
    public void ParseIndexed2DToState_PythonNestedList_ImportsPixelPerfect()
    {
        const string code = @"
player = [
    [0, 1, 1, 0],
    [1, 2, 2, 1],
    [1, 3, 3, 1],
    [0, 1, 1, 0]
]";

        var state = new SpriteState(4, 4);
        var svc = new CodeGeneratorService();
        svc.ParseIndexed2DToState(code, state);

        Assert.False(state.Pixels[0]);
        Assert.True(state.Pixels[1]);
        Assert.True(state.Pixels[2]);
        Assert.False(state.Pixels[3]);

        Assert.True(state.Pixels[4]);
        Assert.True(state.Pixels[5]);
        Assert.True(state.Pixels[6]);
        Assert.True(state.Pixels[7]);
    }

    [Fact]
    public void ParseIndexed2DToState_COpenFirstDimension_ImportsPixelPerfect()
    {
        const string code = @"
const uint8_t sprite[][4] = {
    {1, 0, 0, 1},
    {0, 1, 1, 0}
};";

        var state = new SpriteState(4, 2);
        var svc = new CodeGeneratorService();
        svc.ParseIndexed2DToState(code, state);

        Assert.True(state.Pixels[0]);
        Assert.False(state.Pixels[1]);
        Assert.False(state.Pixels[2]);
        Assert.True(state.Pixels[3]);

        Assert.False(state.Pixels[4]);
        Assert.True(state.Pixels[5]);
        Assert.True(state.Pixels[6]);
        Assert.False(state.Pixels[7]);
    }

    [Fact]
    public void ParseAdafruitGfxToState_WideShipSpriteRowPacked_ImportsPixelPerfect()
    {
        const string code = @"
const uint8_t wide_ship_sprite[8][2] = {
    {0x01, 0x80}, // Row 0: 00000001 10000000
    {0x03, 0xC0}, // Row 1: 00000011 11000000
    {0x1F, 0xF8}, // Row 2: 00011111 11111000
    {0x7F, 0xFE}, // Row 3: 01111111 11111110
    {0xFF, 0xFF}, // Row 4: 11111111 11111111
    {0xCC, 0x33}, // Row 5: 11001100 00110011
    {0x0C, 0x30}, // Row 6: 00001100 00110000
    {0x03, 0xC0}  // Row 7: 00000011 11000000
};";

        var state = new SpriteState(16, 8);
        var svc = new CodeGeneratorService();
        svc.ParseAdafruitGfxToState(code, state);

        // Row 0: 0x01 0x80 -> 00000001 10000000
        Assert.False(state.Pixels[0]); // bit 7
        Assert.True(state.Pixels[7]);  // bit 0 of byte 0
        Assert.True(state.Pixels[8]);  // bit 7 of byte 1
        Assert.False(state.Pixels[15]);// bit 0 of byte 1

        // Row 4: 0xFF 0xFF -> all 16 pixels on
        for (int c = 0; c < 16; c++)
        {
            Assert.True(state.Pixels[4 * 16 + c]);
        }
    }

    [Fact]
    public void ParseAdafruitGfxToState_EyeAnimation2DArray_ImportsPixelPerfect()
    {
        const string code = @"
const uint8_t eye_animation[2][8] = {
    // Frame 0: Eye Open
    { 0x3C, 0x42, 0x81, 0x99, 0x99, 0x81, 0x42, 0x3C },
    
    // Frame 1: Eye Closed (Blinking)
    { 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0x00 }
};";

        var state = new SpriteState(8, 8);
        var svc = new CodeGeneratorService();
        svc.ParseAdafruitGfxToState(code, state);

        // Frame 0 Row 0: 0x3C -> 00111100
        Assert.False(state.Pixels[0]);
        Assert.False(state.Pixels[1]);
        Assert.True(state.Pixels[2]);
        Assert.True(state.Pixels[3]);
        Assert.True(state.Pixels[4]);
        Assert.True(state.Pixels[5]);
        Assert.False(state.Pixels[6]);
        Assert.False(state.Pixels[7]);

        // Frame 0 Row 2: 0x81 -> 10000001
        Assert.True(state.Pixels[16]);
        Assert.False(state.Pixels[17]);
        Assert.True(state.Pixels[23]);
    }

    [Fact]
    public void ParseAdafruitGfxToState_BlockAnimation_ImportsPixelPerfectFrame0()
    {
        const string code = @"
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
// unsigned char *current_frame = block_animation[frame_counter % 3];";

        var state = new SpriteState(8, 8);
        var svc = new CodeGeneratorService();
        svc.ParseAdafruitGfxToState(code, state);

        // Frame 0 Row 0: 0xFF -> all 8 pixels on
        for (int c = 0; c < 8; c++)
        {
            Assert.True(state.Pixels[c]);
        }

        // Frame 0 Row 1: 0x81 -> 10000001
        Assert.True(state.Pixels[8]);
        Assert.False(state.Pixels[9]);
        Assert.False(state.Pixels[14]);
        Assert.True(state.Pixels[15]);

        // Frame 0 Row 7: 0xFF -> all 8 pixels on
        for (int c = 0; c < 8; c++)
        {
            Assert.True(state.Pixels[7 * 8 + c]);
        }
    }

    [Fact]
    public void ParseIndexed2DToState_SwordSpriteWithPalette_ImportsPixelPerfect()
    {
        const string code = @"
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
};";

        var state = new SpriteState(4, 4);
        var svc = new CodeGeneratorService();
        svc.ParseIndexed2DToState(code, state);

        // Row 0: 0, 1, 1, 0
        Assert.False(state.Pixels[0]);
        Assert.True(state.Pixels[1]);
        Assert.True(state.Pixels[2]);
        Assert.False(state.Pixels[3]);

        // Row 1: 1, 3, 3, 1 (3 is > 0 -> on)
        Assert.True(state.Pixels[4]);
        Assert.True(state.Pixels[5]);
        Assert.True(state.Pixels[6]);
        Assert.True(state.Pixels[7]);

        // Row 2: 1, 2, 2, 1 (2 is > 0 -> on)
        Assert.True(state.Pixels[8]);
        Assert.True(state.Pixels[9]);
        Assert.True(state.Pixels[10]);
        Assert.True(state.Pixels[11]);

        // Row 3: 0, 1, 1, 0
        Assert.False(state.Pixels[12]);
        Assert.True(state.Pixels[13]);
        Assert.True(state.Pixels[14]);
        Assert.False(state.Pixels[15]);
    }

    [Fact]
    public void ParseAdafruitGfxToState_BlockAnimation_PopulatesAll3Frames()
    {
        const string code = @"
#define FRAME_SIZE 8

// 3 Frames of an alternating 8x8 flashing brick animation (1-bit)
const unsigned char block_animation[3][FRAME_SIZE] = {
    // Frame 0: Border outline
    { 0xFF, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0xFF },
    
    // Frame 1: Cross hatch pattern
    { 0xAA, 0x55, 0xAA, 0x55, 0xAA, 0x55, 0xAA, 0x55 },
    
    // Frame 2: Solid square
    { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }
};";

        var state = new SpriteState(8, 8);
        var svc = new CodeGeneratorService();
        svc.ParseAdafruitGfxToState(code, state);

        Assert.Equal(3, state.Frames.Count);
        Assert.Equal("Frame 1", state.Frames[0].Name);
        Assert.Equal("Frame 2", state.Frames[1].Name);
        Assert.Equal("Frame 3", state.Frames[2].Name);

        // Frame 1: Border outline (0xFF top and bottom, 0x81 middle)
        var f1 = state.Frames[0].LayerPixels[0].GetMonochromeData();
        for (int c = 0; c < 8; c++) Assert.True(f1[c]); // Row 0
        Assert.True(f1[8]); Assert.False(f1[9]); Assert.True(f1[15]); // Row 1: 0x81
        for (int c = 0; c < 8; c++) Assert.True(f1[7 * 8 + c]); // Row 7

        // Frame 2: Cross hatch (0xAA row 0, 0x55 row 1)
        var f2 = state.Frames[1].LayerPixels[0].GetMonochromeData();
        Assert.True(f2[0]); Assert.False(f2[1]); Assert.True(f2[2]); // Row 0: 0xAA (10101010)
        Assert.False(f2[8]); Assert.True(f2[9]); Assert.False(f2[10]); // Row 1: 0x55 (01010101)

        // Frame 3: Solid square (0xFF all rows)
        var f3 = state.Frames[2].LayerPixels[0].GetMonochromeData();
        for (int i = 0; i < 64; i++) Assert.True(f3[i]);
    }

    [Fact]
    public void ParseXbmToState_MultiFrameAnimation_PopulatesAllFrames()
    {
        const string code = @"
static unsigned char anim_bits[] = {
   0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
   0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
};";

        var state = new SpriteState(8, 8);
        var svc = new CodeGeneratorService();
        svc.ParseXbmToState(code, state);

        Assert.Equal(2, state.Frames.Count);
        var f1 = state.Frames[0].LayerPixels[0].GetMonochromeData();
        Assert.True(f1[0]); Assert.False(f1[7]); // 0x01 in XBM (LSB-first) -> bit 0 on (pixel 0)

        var f2 = state.Frames[1].LayerPixels[0].GetMonochromeData();
        Assert.False(f2[0]); Assert.True(f2[7]); // 0x80 in XBM (LSB-first) -> bit 7 on (pixel 7)
    }

    [Fact]
    public void ParseIndexed2DToState_MultiFrameAnimation_PopulatesAllFrames()
    {
        const string code = @"
const uint8_t anim[2][16] = {
    { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
    { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2 }
};";

        var state = new SpriteState(4, 4);
        var svc = new CodeGeneratorService();
        svc.ParseIndexed2DToState(code, state);

        Assert.Equal(2, state.Frames.Count);
        var f1 = state.Frames[0].LayerPixels[0].GetMonochromeData();
        Assert.True(f1[0]); Assert.False(f1[15]);

        var f2 = state.Frames[1].LayerPixels[0].GetMonochromeData();
        Assert.False(f2[0]); Assert.True(f2[15]);
    }
}
