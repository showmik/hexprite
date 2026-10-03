using System;
using System.Collections.Generic;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.Services.Compression;
using Xunit;

namespace Hexprite.Tests;

public class AnimationLayoutTests
{
    private readonly CodeGeneratorService _codeGen = new(new CompressionService());

    private static (List<bool[]> frames, int width, int height) CreateTestFrames(int count = 3, int width = 16, int height = 16)
    {
        var frames = new List<bool[]>();
        for (int i = 0; i < count; i++)
        {
            var f = new bool[width * height];
            f[i * width + i] = true;
            frames.Add(f);
        }
        return (frames, width, height);
    }

    [Theory]
    [InlineData(ExportFormat.MicroPython)]
    [InlineData(ExportFormat.RawHex)]
    [InlineData(ExportFormat.RawBinary)]
    [InlineData(ExportFormat.Indexed2D)]
    [InlineData(ExportFormat.FlipperCompressedBitmap)]
    [InlineData(ExportFormat.FlipperXbm)]
    [InlineData(ExportFormat.FlipperCanvasIcon)]
    public void DeltaPatches_WithUnsupportedFormat_FallsBackToArrayOfFrames_PreservingAllFrames(ExportFormat format)
    {
        var (frames, w, h) = CreateTestFrames(3, 16, 16);
        var settings = new ExportSettings
        {
            Format = format,
            ExportAsAnimation = true,
            AnimationLayout = AnimationExportLayout.DeltaPatches,
            SpriteName = "multiAnim",
        };

        string code = _codeGen.GenerateCode(frames, w, h, settings, false, null, 0, 0, 0, 0);

        if (format == ExportFormat.MicroPython)
        {
            Assert.Contains("MULTIANIM_FRAMES = 3", code);
            Assert.Contains("multiAnim = [", code);
            Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(code, "bytearray\\(\\[").Count);
        }
        else if (format == ExportFormat.RawHex || format == ExportFormat.RawBinary)
        {
            Assert.Contains("// Frame 1:", code);
            Assert.Contains("// Frame 2:", code);
            Assert.Contains("// Frame 3:", code);
        }
        else if (format == ExportFormat.Indexed2D)
        {
            Assert.Contains("const uint8_t multiAnim[3][16][16] = {", code);
            Assert.Contains("MULTIANIM_FRAMES = 3", code);
        }
        else if (format == ExportFormat.FlipperCompressedBitmap)
        {
            Assert.Contains("multiAnim_frame_0", code);
            Assert.Contains("multiAnim_frame_1", code);
            Assert.Contains("multiAnim_frame_2", code);
            Assert.Contains("multiAnim_frames[3]", code);
        }
        else if (format == ExportFormat.FlipperXbm)
        {
            Assert.Contains("multiAnim_xbm_frame_0", code);
            Assert.Contains("multiAnim_xbm_frame_1", code);
            Assert.Contains("multiAnim_xbm_frame_2", code);
            Assert.Contains("multiAnim_xbm_frames[3]", code);
        }
        else if (format == ExportFormat.FlipperCanvasIcon)
        {
            Assert.Contains("A_multiAnim_16x16", code);
            Assert.Contains("MULTIANIM_FRAMES = 3", code);
        }
    }

    [Fact]
    public void DeltaPatches_WithDimensionsExceeding255_FallsBackToArrayOfFrames()
    {
        int w = 256, h = 16;
        var f0 = new bool[w * h];
        var f1 = new bool[w * h];
        f0[0] = true;
        f1[w * h - 1] = true;

        var settings = new ExportSettings
        {
            Format = ExportFormat.AdafruitGfx,
            ExportAsAnimation = true,
            AnimationLayout = AnimationExportLayout.DeltaPatches,
            SpriteName = "wideAnim",
        };

        string code = _codeGen.GenerateCode([f0, f1], w, h, settings, false, null, 0, 0, 0, 0);

        Assert.Contains("const uint8_t PROGMEM wideAnim[", code);
        Assert.Contains("WIDEANIM_FRAMES = 2", code);
        Assert.DoesNotContain("wideAnim_DELTAS", code);
    }

    [Fact]
    public void CalculateByteCount_DeltaPatches_CalculatesActualByteCount()
    {
        var (frames, w, h) = CreateTestFrames(3, 16, 16);
        var settings = new ExportSettings
        {
            Format = ExportFormat.AdafruitGfx,
            ExportAsAnimation = true,
            AnimationLayout = AnimationExportLayout.DeltaPatches,
            SpriteName = "testDelta",
        };

        int singleFrameBytes = h * CodeGeneratorService.BytesPerRow(w); // 16 * 2 = 32 bytes
        int calculatedBytes = _codeGen.CalculateByteCount(frames, w, h, settings);

        // Single frame is 32 bytes. Keyframe 0 is 32 bytes, plus DELTAS (at least a few bytes per frame) plus OFFSETS (2 * 2 = 4 bytes)
        Assert.True(calculatedBytes > singleFrameBytes, $"Calculated bytes ({calculatedBytes}) should exceed single frame size ({singleFrameBytes})");
    }

    [Fact]
    public void CalculateByteCount_DeltaPatches_WithCompression_DoesNotCrashOrCompressDelta()
    {
        var (frames, w, h) = CreateTestFrames(3, 16, 16);
        var settingsRaw = new ExportSettings
        {
            Format = ExportFormat.AdafruitGfx,
            ExportAsAnimation = true,
            AnimationLayout = AnimationExportLayout.DeltaPatches,
            Compression = CompressionMode.None,
        };
        var settingsComp = new ExportSettings
        {
            Format = ExportFormat.AdafruitGfx,
            ExportAsAnimation = true,
            AnimationLayout = AnimationExportLayout.DeltaPatches,
            Compression = CompressionMode.Rle,
        };

        int bytesRaw = _codeGen.CalculateByteCount(frames, w, h, settingsRaw);
        int bytesComp = _codeGen.CalculateByteCount(frames, w, h, settingsComp);

        Assert.Equal(bytesRaw, bytesComp);
    }

    [Theory]
    [InlineData(AnimationExportLayout.VerticalSpriteSheet)]
    [InlineData(AnimationExportLayout.HorizontalSpriteSheet)]
    public void SpriteSheet_WithCompression_DisallowsCompressionInFullSketch(AnimationExportLayout layout)
    {
        var (frames, w, h) = CreateTestFrames(3, 16, 16);
        var settings = new ExportSettings
        {
            Format = ExportFormat.AdafruitGfx,
            ExportAsAnimation = true,
            AnimationLayout = layout,
            Compression = CompressionMode.Rle,
            GenerateFullSketch = true,
            SpriteName = "sheetAnim",
        };

        string sketch = _codeGen.GenerateSketch(frames, w, h, settings, false, null, 0, 0, 0, 0);

        // Should not reference missing offsets table
        Assert.DoesNotContain("SHEETANIM_FRAME_OFFSETS", sketch);
        Assert.DoesNotContain("SHEETANIM_FRAME_SIZES", sketch);
    }

    [Fact]
    public void HorizontalSpriteSheet_AdafruitGfxSketch_RendersSingleFrameWithoutBleed()
    {
        var (frames, w, h) = CreateTestFrames(3, 16, 16);
        var settings = new ExportSettings
        {
            Format = ExportFormat.AdafruitGfx,
            ExportAsAnimation = true,
            AnimationLayout = AnimationExportLayout.HorizontalSpriteSheet,
            GenerateFullSketch = true,
            SpriteName = "hSheet",
        };

        string sketch = _codeGen.GenerateSketch(frames, w, h, settings, false, null, 0, 0, 0, 0);

        // Must not call display.drawBitmap with total width HSHEET_WIDTH which bleeds all frames
        Assert.DoesNotContain("display.drawBitmap(x - (i * HSHEET_FRAME_WIDTH), y, hSheet, HSHEET_WIDTH", sketch);
        Assert.Contains("drawHorizontalFrame", sketch);
    }

    [Fact]
    public void HorizontalSpriteSheet_U8g2Sketch_UsesClipWindowAndTotalStride()
    {
        var (frames, w, h) = CreateTestFrames(3, 16, 16);
        var settings = new ExportSettings
        {
            Format = ExportFormat.U8g2DrawBitmap,
            ExportAsAnimation = true,
            AnimationLayout = AnimationExportLayout.HorizontalSpriteSheet,
            GenerateFullSketch = true,
            SpriteName = "u8Sheet",
        };

        string sketch = _codeGen.GenerateSketch(frames, w, h, settings, false, null, 0, 0, 0, 0);

        Assert.Contains("setClipWindow", sketch);
        Assert.Contains("setMaxClipWindow", sketch);
        // Stride must be total sheet bytes per row, not single frame width
        int totalWidthBytes = CodeGeneratorService.BytesPerRow(16 * 3);
        Assert.Contains($"u8g2.drawBitmap(x - (i * U8SHEET_FRAME_WIDTH), y, {totalWidthBytes}, U8SHEET_HEIGHT, u8Sheet)", sketch);
    }

    [Fact]
    public void HorizontalSpriteSheet_MicroPython_CropsFrameBufferToAvoidBleed()
    {
        var (frames, w, h) = CreateTestFrames(3, 16, 16);
        var settings = new ExportSettings
        {
            Format = ExportFormat.MicroPython,
            ExportAsAnimation = true,
            AnimationLayout = AnimationExportLayout.HorizontalSpriteSheet,
            GenerateFullSketch = true,
            SpriteName = "pySheet",
        };

        string script = _codeGen.GenerateSketch(frames, w, h, settings, false, null, 0, 0, 0, 0);

        Assert.Contains("frame_fb", script);
        Assert.Contains("frame_fb.blit(fb", script);
        Assert.Contains("oled.blit(frame_fb, x, y)", script);
    }

    [Theory]
    [InlineData(AnimationExportLayout.VerticalSpriteSheet)]
    [InlineData(AnimationExportLayout.HorizontalSpriteSheet)]
    [InlineData(AnimationExportLayout.DeltaPatches)]
    public void FlipperFormats_WithAnyAnimationLayout_PreservesAllFrames(AnimationExportLayout layout)
    {
        var (frames, w, h) = CreateTestFrames(3, 16, 16);
        var settings = new ExportSettings
        {
            Format = ExportFormat.FlipperCompressedBitmap,
            ExportAsAnimation = true,
            AnimationLayout = layout,
            GenerateFullSketch = true,
            SpriteName = "flipAnim",
        };

        string sketch = _codeGen.GenerateSketch(frames, w, h, settings, false, null, 0, 0, 0, 0);

        Assert.Contains("flipAnim_frames[app->current_frame % FLIPANIM_FRAMES]", sketch);
        Assert.Contains("const uint8_t* const flipAnim_frames[3] = {", sketch);
    }

    [Fact]
    public void DeltaPatches_AdafruitGfxAndU8g2_AvrProgmemAccess_UsesPgmReadWord()
    {
        var (frames, w, h) = CreateTestFrames(3, 16, 16);
        var settings = new ExportSettings
        {
            Format = ExportFormat.AdafruitGfx,
            ExportAsAnimation = true,
            AnimationLayout = AnimationExportLayout.DeltaPatches,
            GenerateFullSketch = true,
            SpriteName = "avrDelta",
        };

        string sketch = _codeGen.GenerateSketch(frames, w, h, settings, false, null, 0, 0, 0, 0);

        Assert.Contains("pgm_read_word(&AVRDELTA_FRAME_OFFSETS[", sketch);
    }
}
