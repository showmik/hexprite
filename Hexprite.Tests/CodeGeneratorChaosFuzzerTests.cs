using Hexprite.Core;
using Hexprite.Services;
using Hexprite.Services.Compression;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Fuzz")]
    public class CodeGeneratorChaosFuzzerTests
    {
        [Fact]
        public void SpriteCodeGenerator_ShouldNotCrashUnderChaoticInput()
        {
            var compressionMock = new Moq.Mock<ICompressionService>();
            var codeGen = new CodeGeneratorService(compressionMock.Object);
            var random = new Random(444);
            int iterations = FuzzTestHelper.GetIterationCount(defaultFastCount: 10000, deepCount: 10000);

            for (int i = 0; i < iterations; i++)
            {
                int w = random.Next(1, 129);
                int h = random.Next(1, 129);
                int framesCount = random.Next(1, 5);

                var frames = new List<bool[]>();
                for (int f = 0; f < framesCount; f++)
                {
                    var pixels = new bool[w * h];
                    for (int p = 0; p < pixels.Length; p++)
                    {
                        pixels[p] = random.Next(2) == 0;
                    }
                    frames.Add(pixels);
                }

                var settings = new ExportSettings
                {
                    Format = (ExportFormat)random.Next(0, 11),
                    SpriteName = $"TestVar_{random.Next()}",
                    IncludeUsageComment = random.Next(2) == 0,
                    IncludeDimensionConstants = random.Next(2) == 0,
                    Compression = (CompressionMode)random.Next(0, 3)
                };

                try
                {
                    // Fuzz generation
                    string code = codeGen.GenerateCode(
                        frames, w, h, settings,
                        isFloating: random.Next(2) == 0,
                        floatingPixels: null,
                        floatX: random.Next(-10, 10),
                        floatY: random.Next(-10, 10),
                        floatW: random.Next(0, 10),
                        floatH: random.Next(0, 10)
                    );

                    Assert.NotNull(code);

                    // Fuzz parsing with random strings and the generated code itself
                    var state = new SpriteState(w, h);
                    
                    int parserAction = random.Next(0, 4);
                    string fuzzString = random.Next(2) == 0 ? code : GenerateRandomGarbageString(random);
                    
                    switch (parserAction)
                    {
                        case 0:
                            codeGen.ParseAdafruitGfxToState(fuzzString, state);
                            break;
                        case 1:
                            codeGen.ParseHexToState(fuzzString, state);
                            break;
                        case 2:
                            codeGen.ParseXbmToState(fuzzString, state);
                            break;
                        case 3:
                            codeGen.ParseBinaryToState(fuzzString, state);
                            break;
                    }
                }
                catch (FormatException) { }
                catch (ArgumentException) { }
                catch (Exception ex)
                {
                    throw new Exception($"Crash during CodeGenerator chaos at iteration {i}: {ex.Message}", ex);
                }
            }

            Assert.True(true);
        }

        [Fact]
        public void FontCodeGenerator_ShouldNotCrashUnderChaoticInput()
        {
            var fontCodeGen = new FontCodeGeneratorService();
            var random = new Random(555);
            int iterations = FuzzTestHelper.GetIterationCount(defaultFastCount: 10000, deepCount: 10000);

            for (int i = 0; i < iterations; i++)
            {
                var doc = new FontDocument();
                doc.FontName = $"ChaosFont_{random.Next()}";
                doc.CellHeight = random.Next(4, 32);
                doc.MaxCellWidth = random.Next(4, 32);

                int glyphCount = random.Next(1, 100);
                for (int g = 0; g < glyphCount; g++)
                {
                    int gw = random.Next(1, 32);
                    int gh = random.Next(1, 32);
                    var glyph = new GlyphState
                    {
                        CodePoint = random.Next(32, 127),
                        Width = gw,
                        Height = gh,
                        Pixels = new bool[gw * gh],
                        XAdvance = random.Next(1, 32),
                        XOffset = random.Next(-5, 5),
                        YOffset = random.Next(-20, 5)
                    };
                    
                    for (int p = 0; p < glyph.Pixels.Length; p++)
                        glyph.Pixels[p] = random.Next(2) == 0;

                    doc.Glyphs.Add(glyph);
                }

                var settings = new FontExportSettings
                {
                    FontName = doc.FontName,
                    Format = (FontExportFormat)random.Next(0, 5)
                };

                try
                {
                    string code = fontCodeGen.GenerateCode(doc, settings);
                    Assert.NotNull(code);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Crash during FontCodeGenerator at iteration {i}: {ex.Message}", ex);
                }
            }
            Assert.True(true);
        }

        private static string GenerateRandomGarbageString(Random random)
        {
            int len = random.Next(10, 1000);
            char[] chars = new char[len];
            for (int i = 0; i < len; i++)
            {
                chars[i] = (char)random.Next(32, 127);
            }
            return new string(chars);
        }
    }
}
