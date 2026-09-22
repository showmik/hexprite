using System;
using System.Collections.Generic;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.Services.Compression;
using Hexprite.ViewModels;
using Hexprite.Tests.E2E;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
public sealed class ArduinoFeaturesTests
{
    private readonly CodeGeneratorService _codeGen = new();

    [Fact]
    public void ExportFormat_LiquidCrystalChar_SingleFrame_GeneratesByteArray()
    {
        // Arrange
        int w = 5, h = 8;
        bool[] pixels = new bool[w * h];
        // Draw a heart shape:
        // row 0: . . . . . -> B00000
        // row 1: . X . X . -> B01010
        // row 2: X X X X X -> B11111
        // row 3: X X X X X -> B11111
        // row 4: . X X X . -> B01110
        // row 5: . . X . . -> B00100
        // row 6: . . . . . -> B00000
        // row 7: . . . . . -> B00000
        pixels[1 * w + 1] = true;
        pixels[1 * w + 3] = true;
        for (int x = 0; x < 5; x++) pixels[2 * w + x] = true;
        for (int x = 0; x < 5; x++) pixels[3 * w + x] = true;
        for (int x = 1; x < 4; x++) pixels[4 * w + x] = true;
        pixels[5 * w + 2] = true;

        var settings = new ExportSettings
        {
            Format = ExportFormat.LiquidCrystalChar,
            SpriteName = "heart",
            IncludeUsageComment = true,
            IncludeDimensionConstants = true,
        };

        // Act
        string code = _codeGen.GenerateCode([pixels], w, h, settings, isFloating: false, floatingPixels: null, 0, 0, 0, 0);

        // Assert
        Assert.Contains("byte heart[8] = {", code);
        Assert.Contains("B01010", code);
        Assert.Contains("B11111", code);
        Assert.Contains("B01110", code);
        Assert.Contains("B00100", code);
        Assert.Contains("lcd.createChar", code);
        Assert.Contains("lcd.write((byte)0)", code);
        Assert.Contains("HEART_WIDTH  = 5", code);
        Assert.Contains("HEART_HEIGHT = 8", code);
    }

    [Fact]
    public void ExportFormat_LiquidCrystalChar_MultiFrame_GeneratesAnimationArray()
    {
        // Arrange
        int w = 5, h = 8;
        bool[] frame0 = new bool[w * h];
        bool[] frame1 = new bool[w * h];
        frame0[0] = true; // Top-left pixel
        frame1[w * h - 1] = true; // Bottom-right pixel

        var settings = new ExportSettings
        {
            Format = ExportFormat.LiquidCrystalChar,
            SpriteName = "heartAnim",
            ExportAsAnimation = true,
            IncludeUsageComment = true,
        };

        // Act
        string code = _codeGen.GenerateCode([frame0, frame1], w, h, settings, isFloating: false, floatingPixels: null, 0, 0, 0, 0);

        // Assert
        Assert.Contains("byte heartAnim_0[8] = {", code);
        Assert.Contains("byte heartAnim_1[8] = {", code);
        Assert.Contains("byte* const heartAnim_frames[2] = {", code);
        Assert.Contains("lcd.createChar(i, heartAnim_frames[i])", code);
    }

    [Fact]
    public void ExportFormat_LiquidCrystalChar_FullSketch_GeneratesWorkingI2CLcdSketch()
    {
        // Arrange
        int w = 5, h = 8;
        bool[] pixels = new bool[w * h];
        pixels[2 * w + 2] = true;

        var settings = new ExportSettings
        {
            Format = ExportFormat.LiquidCrystalChar,
            SpriteName = "myChar",
            GenerateFullSketch = true,
        };

        // Act
        string code = _codeGen.GenerateCode([pixels], w, h, settings, isFloating: false, floatingPixels: null, 0, 0, 0, 0);

        // Assert
        Assert.Contains("#include <Wire.h>", code);
        Assert.Contains("#include <LiquidCrystal_I2C.h>", code);
        Assert.Contains("LiquidCrystal_I2C lcd(0x27, 16, 2);", code);
        Assert.Contains("lcd.init();", code);
        Assert.Contains("lcd.backlight();", code);
        Assert.Contains("lcd.createChar(0, myChar);", code);
        Assert.Contains("lcd.write((byte)0);", code);
    }

    [Fact]
    public void ParseLiquidCrystalToState_SingleFrameBinary_ParsesAccurately()
    {
        // Arrange
        string snippet = """
            byte heart[8] = {
              B00000,
              B01010,
              B11111,
              B11111,
              B01110,
              B00100,
              B00000,
              B00000
            };
            """;
        var state = new SpriteState(5, 8);

        // Act
        _codeGen.ParseLiquidCrystalToState(snippet, state);

        // Assert
        bool[] pixels = state.Frames[0].LayerPixels[0].GetMonochromeData();
        int w = 5;

        // Row 1 should have pixels at col 1 and col 3
        Assert.False(pixels[1 * w + 0]);
        Assert.True(pixels[1 * w + 1]);
        Assert.False(pixels[1 * w + 2]);
        Assert.True(pixels[1 * w + 3]);
        Assert.False(pixels[1 * w + 4]);

        // Row 2 should be all true
        for (int x = 0; x < 5; x++)
        {
            Assert.True(pixels[2 * w + x]);
        }

        // Row 5 should have only center pixel
        Assert.False(pixels[5 * w + 1]);
        Assert.True(pixels[5 * w + 2]);
        Assert.False(pixels[5 * w + 3]);
    }

    [Fact]
    public void ParseLiquidCrystalToState_SingleFrameHex_ParsesAccurately()
    {
        // Arrange
        string snippet = """
            byte customIcon[8] = {
              0x00,
              0x0A,
              0x1F,
              0x1F,
              0x0E,
              0x04,
              0x00,
              0x00
            };
            """;
        var state = new SpriteState(5, 8);

        // Act
        _codeGen.ParseLiquidCrystalToState(snippet, state);

        // Assert
        bool[] pixels = state.Frames[0].LayerPixels[0].GetMonochromeData();
        int w = 5;

        Assert.True(pixels[1 * w + 1]);
        Assert.True(pixels[1 * w + 3]);
        Assert.True(pixels[2 * w + 0]);
        Assert.True(pixels[5 * w + 2]);
    }

    [Fact]
    public void ParseLiquidCrystalToState_MultiFrame_ParsesAllFrames()
    {
        // Arrange
        string snippet = """
            byte heartAnim[2][8] = {
              {
                B10000,
                B00000,
                B00000,
                B00000,
                B00000,
                B00000,
                B00000,
                B00000
              },
              {
                B00000,
                B00000,
                B00000,
                B00000,
                B00000,
                B00000,
                B00000,
                B00001
              }
            };
            """;
        var state = new SpriteState(5, 8);

        // Act
        _codeGen.ParseLiquidCrystalToState(snippet, state);

        // Assert
        Assert.Equal(2, state.Frames.Count);
        bool[] f0 = state.Frames[0].LayerPixels[0].GetMonochromeData();
        bool[] f1 = state.Frames[1].LayerPixels[0].GetMonochromeData();

        Assert.True(f0[0]); // row 0 col 0
        Assert.False(f0[5 * 8 - 1]);

        Assert.False(f1[0]);
        Assert.True(f1[5 * 8 - 1]); // row 7 col 4
    }

    [Fact]
    public void GenerateStandaloneSketchFiles_SplitsIntoInoAndSpritesH()
    {
        // Arrange
        var state = new SpriteState(128, 64);
        var settings = new ExportSettings
        {
            Format = ExportFormat.AdafruitGfx,
            SpriteName = "player_sprite",
            GenerateFullSketch = true,
        };

        // Act
        var files = _codeGen.GenerateStandaloneSketchFiles(state, settings);

        // Assert
        Assert.Equal("player_sprite", files.SketchName);
        Assert.Contains("#pragma once", files.HeaderContent);
        Assert.Contains("#include <Arduino.h>", files.HeaderContent);
        Assert.Contains("const uint8_t PROGMEM player_sprite[]", files.HeaderContent);

        Assert.Contains("#include \"sprites.h\"", files.InoContent);
        Assert.Contains("void setup()", files.InoContent);
        Assert.Contains("void loop()", files.InoContent);
        // The InoContent should NOT contain the raw byte array data itself because it was split out into sprites.h
        Assert.DoesNotContain("const uint8_t PROGMEM player_sprite[]", files.InoContent);
    }

    [Fact]
    public void ImportFromCodeDetector_DetectsLiquidCrystalAndInfersDimensions()
    {
        // Arrange
        string snippet1 = "byte heart[8] = { B00000, B01010, B11111, B11111, B01110, B00100, B00000, B00000 };";
        string snippet2 = "lcd.createChar(0, custom_char);";

        // Act & Assert
        Assert.True(ImportFromCodeDetector.IsLikelyLiquidCrystalFormat(snippet1));
        Assert.True(ImportFromCodeDetector.IsLikelyLiquidCrystalFormat(snippet2));

        int byteCount = ImportFromCodeDetector.CountDataBytes(snippet1);
        Assert.Equal(8, byteCount);

        bool inferred = ImportFromCodeDetector.TryInferDimensionsFromData(
            snippet1, byteCount, out int w, out int h, out var hint);
        Assert.True(inferred);
        Assert.Equal(5, w);
        Assert.Equal(8, h);

        int expected = ImportFromCodeDetector.ExpectedByteCount(w, h, ExportFormat.LiquidCrystalChar);
        Assert.Equal(8, expected);
    }

    [Fact]
    public void ReservedIdentifiers_SanitizesArduinoKeywords()
    {
        string[] arduinoKeywords = ["setup", "loop", "lcd", "display", "u8g2", "Serial", "Wire", "SPI", "byte", "word", "boolean"];
        foreach (string keyword in arduinoKeywords)
        {
            string sanitized = CodeGeneratorService.SanitiseName(keyword);
            Assert.Equal(keyword + "_", sanitized);
        }
    }

    [Fact]
    public void CompressionDecompressors_DeclareStaticInlineVoid_ToPreventLinkerCollisions()
    {
        var compression = new CompressionService();
        string rleCode = compression.GenerateDecompressorCode(CompressionMode.Rle);
        string lzssCode = compression.GenerateDecompressorCode(CompressionMode.Lzss);

        Assert.Contains("static inline void hexprite_rle_decode", rleCode);
        Assert.Contains("static inline void hexprite_lzss_decode", lzssCode);
    }

    [Fact]
    public void ExportFormat_LiquidCrystalChar_Animation_WithFrameDelays_EmitsDelaysArrayAndTimedLoop()
    {
        // Arrange
        int w = 5, h = 8;
        bool[] frame0 = new bool[w * h];
        bool[] frame1 = new bool[w * h];
        frame0[0] = true;
        frame1[w * h - 1] = true;

        var settings = new ExportSettings
        {
            Format = ExportFormat.LiquidCrystalChar,
            SpriteName = "heartAnim",
            ExportAsAnimation = true,
            GenerateFullSketch = true,
            IncludeUsageComment = true,
            IncludeDimensionConstants = true,
            FrameRateFps = 10,
        };
        var delays = new List<int> { 1, 3 };

        // Act
        string code = _codeGen.GenerateCode(
            [frame0, frame1], w, h, settings,
            isFloating: false, floatingPixels: null, 0, 0, 0, 0,
            frameDelays: delays);

        // Assert
        Assert.Contains("const uint8_t HEARTANIM_DELAYS[2] = {", code);
        Assert.Contains("0x01, 0x03", code);
        Assert.Contains("unsigned long frameDuration = (1000UL / 10) * HEARTANIM_DELAYS[currentFrame];", code);
        Assert.Contains("lcd.setCursor(15, 0);", code);
        Assert.Contains("lcd.write((byte)0);", code);
        Assert.Contains("replace lcd.init()/backlight() with lcd.begin(16, 2);", code);
    }

    [Fact]
    public void GenerateStandaloneSketchFiles_PropagatesFrameDelays_AndHandlesEmptyOutput()
    {
        // Arrange
        var state = new SpriteState(128, 64);
        var f0 = new FrameState();
        f0.DelayMultiplier = 1;
        var f1 = new FrameState();
        f1.DelayMultiplier = 4;
        state.Frames.Clear();
        state.Frames.Add(f0);
        state.Frames.Add(f1);

        var settings = new ExportSettings
        {
            Format = ExportFormat.AdafruitGfx,
            SpriteName = "runner",
            ExportAsAnimation = true,
            GenerateFullSketch = true,
            FrameRateFps = 8,
        };

        // Act
        var files = _codeGen.GenerateStandaloneSketchFiles(state, settings);

        // Assert
        Assert.Equal("runner", files.SketchName);
        Assert.Contains("RUNNER_DELAYS[2]", files.HeaderContent);
        Assert.Contains("#include \"sprites.h\"", files.InoContent);
        Assert.Contains("RUNNER_DELAYS[i]", files.InoContent);
    }

    [Fact]
    public void MainViewModel_IsArduinoSketchExportable_ReturnsTrueOnlyForArduinoFormats()
    {
        // Arrange
        var shell = E2ETestHelper.CreateTestShellViewModel();
        shell.NewDocumentCommand.Execute("128x64");
        var doc = Assert.IsAssignableFrom<Hexprite.ViewModels.MainViewModel>(shell.ActiveDocument);

        // Arduino compatible formats
        doc.ExportFormat = ExportFormat.AdafruitGfx;
        Assert.True(doc.IsArduinoSketchExportable);

        doc.ExportFormat = ExportFormat.U8g2DrawBitmap;
        Assert.True(doc.IsArduinoSketchExportable);

        doc.ExportFormat = ExportFormat.U8g2DrawXBM;
        Assert.True(doc.IsArduinoSketchExportable);

        doc.ExportFormat = ExportFormat.LiquidCrystalChar;
        Assert.True(doc.IsArduinoSketchExportable);

        // Incompatible formats
        doc.ExportFormat = ExportFormat.PlainCArray;
        Assert.False(doc.IsArduinoSketchExportable);

        doc.ExportFormat = ExportFormat.MicroPython;
        Assert.False(doc.IsArduinoSketchExportable);

        doc.ExportFormat = ExportFormat.RawHex;
        Assert.False(doc.IsArduinoSketchExportable);

        doc.ExportFormat = ExportFormat.RawBinary;
        Assert.False(doc.IsArduinoSketchExportable);

        doc.ExportFormat = ExportFormat.FlipperCompressedBitmap;
        Assert.False(doc.IsArduinoSketchExportable);

        doc.ExportFormat = ExportFormat.FlipperXbm;
        Assert.False(doc.IsArduinoSketchExportable);

        doc.ExportFormat = ExportFormat.FlipperCanvasIcon;
        Assert.False(doc.IsArduinoSketchExportable);
    }

    [Fact]
    public void ExportFormat_MicroPython_ByteComments_UseHashInsteadOfSlash()
    {
        // Arrange
        int w = 8, h = 2;
        bool[] pixels = new bool[w * h];
        pixels[0] = true;
        pixels[w] = true;

        var settings = new ExportSettings
        {
            Format = ExportFormat.MicroPython,
            SpriteName = "icon",
            IncludeRowComments = true,
            BytesPerLine = 1,
        };

        // Act
        string code = _codeGen.GenerateCode([pixels], w, h, settings, isFloating: false, floatingPixels: null, 0, 0, 0, 0);

        // Assert
        Assert.Contains("# row 0", code);
        Assert.Contains("# row 1", code);
        Assert.DoesNotContain("// row", code);
    }

    [Fact]
    public void GenerateStandaloneSketchFiles_AlwaysEnforcesDimensionConstants_EvenIfUnchecked()
    {
        // Arrange
        var state = new SpriteState(16, 16);
        var settings = new ExportSettings
        {
            Format = ExportFormat.AdafruitGfx,
            SpriteName = "logo",
            IncludeDimensionConstants = false, // User unchecked it
            GenerateFullSketch = true,
        };

        // Act
        var files = _codeGen.GenerateStandaloneSketchFiles(state, settings);

        // Assert
        Assert.Contains("LOGO_WIDTH", files.HeaderContent);
        Assert.Contains("LOGO_HEIGHT", files.HeaderContent);
    }

    [Fact]
    public void GenerateStandaloneSketchFiles_SingleFrame_UsesActiveFramePixels()
    {
        // Arrange
        var state = new SpriteState(8, 8);
        var f0 = new FrameState();
        f0.LayerPixels.Add(new MonochromePixelBuffer(new bool[64]));
        var f1 = new FrameState();
        var f1Pixels = new bool[64];
        f1Pixels[0] = true; // Frame 1 has pixel at (0, 0)
        f1.LayerPixels.Add(new MonochromePixelBuffer(f1Pixels));
        state.Frames.Clear();
        state.Frames.Add(f0);
        state.Frames.Add(f1);
        state.ActiveFrameIndex = 1;

        var settings = new ExportSettings
        {
            Format = ExportFormat.AdafruitGfx,
            SpriteName = "player",
            ExportAsAnimation = false, // User exporting only active frame
            GenerateFullSketch = true,
        };

        // Act
        var files = _codeGen.GenerateStandaloneSketchFiles(state, settings);

        // Assert - f1 has top-left pixel set, so first byte should be 0x80
        Assert.Contains("0x80", files.HeaderContent);
    }

    [Fact]
    public void BuildAdafruitGfxSketch_AdaptsTo128x32Display()
    {
        // Arrange
        int w = 128, h = 32;
        bool[] pixels = new bool[w * h];
        var settings = new ExportSettings
        {
            Format = ExportFormat.AdafruitGfx,
            SpriteName = "banner",
            GenerateFullSketch = true,
        };

        // Act
        string code = _codeGen.GenerateCode([pixels], w, h, settings, isFloating: false, floatingPixels: null, 0, 0, 0, 0);

        // Assert
        Assert.Contains("#define SCREEN_WIDTH 128", code);
        Assert.Contains("#define SCREEN_HEIGHT 32", code);
        Assert.Contains("Adafruit_SSD1306 display(SCREEN_WIDTH, SCREEN_HEIGHT, &Wire, OLED_RESET);", code);
    }

    [Fact]
    public void ExportFormat_Indexed2D_GeneratesFullSketchHarness()
    {
        // Arrange
        int w = 8, h = 8;
        bool[] pixels = new bool[w * h];
        pixels[0] = true;

        var settings = new ExportSettings
        {
            Format = ExportFormat.Indexed2D,
            SpriteName = "tile",
            GenerateFullSketch = true,
            IncludeUsageComment = true,
        };

        // Act
        string code = _codeGen.GenerateCode([pixels], w, h, settings, isFloating: false, floatingPixels: null, 0, 0, 0, 0);

        // Assert
        Assert.Contains("#include <Arduino.h>", code);
        Assert.Contains("uint8_t pixel = tile[y][x];", code);
        Assert.Contains("Serial.println(F(\"Loaded 2D sprite: tile (8x8)\"));", code);
    }

    [Fact]
    public void MainViewModel_ExportStats_ShowsMemoryTargetBreakdown()
    {
        WpfTestHelper.RunOnSta(async () =>
        {
            var shell = E2ETestHelper.CreateTestShellViewModel();
            shell.NewDocumentCommand.Execute("16x16");
            var doc = Assert.IsAssignableFrom<Hexprite.ViewModels.MainViewModel>(shell.ActiveDocument);

            // Act & Assert - AdafruitGfx (Flash)
            doc.ExportFormat = ExportFormat.AdafruitGfx;
            await doc.UpdateTextOutputsAsync();
            Assert.Contains("Flash (PROGMEM)", doc.ExportStats);

            // Act & Assert - PlainCArray (RAM/Flash)
            doc.ExportFormat = ExportFormat.PlainCArray;
            await doc.UpdateTextOutputsAsync();
            Assert.Contains("RAM/Flash", doc.ExportStats);

            // Act & Assert - LiquidCrystalChar (CGRAM)
            doc.ExportFormat = ExportFormat.LiquidCrystalChar;
            await doc.UpdateTextOutputsAsync();
            Assert.Contains("CGRAM", doc.ExportStats);
        });
    }
}
