using Hexprite.Core;
using Hexprite.Services;
using Hexprite.Services.Compression;
using Moq;
using System;
using System.Collections.Generic;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class CodeGeneratorRoundTripTests
    {
        private readonly CodeGeneratorService _codeGen;

        public CodeGeneratorRoundTripTests()
        {
            var compressionMock = new Mock<ICompressionService>();
            // Since we're not testing compression here, we'll mock it to just pass through,
            // or simply set ExportSettings to not compress.
            _codeGen = new CodeGeneratorService(compressionMock.Object);
        }

        [Theory]
        [InlineData(ExportFormat.AdafruitGfx, 8, 8)]
        [InlineData(ExportFormat.RawHex, 16, 16)]
        [InlineData(ExportFormat.RawBinary, 10, 10)]
        [InlineData(ExportFormat.U8g2DrawXBM, 16, 16)]
        [InlineData(ExportFormat.Indexed2D, 16, 16)]
        [InlineData(ExportFormat.Indexed2D, 4, 4)]
        public void ExportAndImport_ShouldMatchIdentically(ExportFormat format, int width, int height)
        {
            var random = new Random(12345);
            var originalState = new SpriteState(width, height);
            
            // 1. Generate a random but deterministic pixel grid
            for (int i = 0; i < originalState.Pixels.Length; i++)
            {
                originalState.Pixels[i] = random.Next(2) == 0;
            }

            var settings = new ExportSettings
            {
                Format = format,
                SpriteName = "testSprite",
                Compression = CompressionMode.None,
                ExportAsAnimation = false
            };

            var frames = new List<bool[]> { originalState.Pixels };

            // 2. Export to string code
            string generatedCode = _codeGen.GenerateCode(
                frames, width, height, settings,
                isFloating: false, null, 0, 0, 0, 0);

            Assert.False(string.IsNullOrWhiteSpace(generatedCode));

            // 3. Import back into a new state
            var parsedState = new SpriteState(width, height);

            switch (format)
            {
                case ExportFormat.AdafruitGfx:
                    _codeGen.ParseAdafruitGfxToState(generatedCode, parsedState);
                    break;
                case ExportFormat.RawHex:
                    _codeGen.ParseHexToState(generatedCode, parsedState);
                    break;
                case ExportFormat.RawBinary:
                    _codeGen.ParseBinaryToState(generatedCode, parsedState);
                    break;
                case ExportFormat.U8g2DrawXBM:
                    _codeGen.ParseXbmToState(generatedCode, parsedState);
                    break;
                case ExportFormat.Indexed2D:
                    _codeGen.ParseIndexed2DToState(generatedCode, parsedState);
                    break;
            }

            // 4. Assert pixel-perfect match
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    bool expected = originalState.Pixels[index];
                    bool actual = parsedState.Pixels[index];

                    Assert.True(expected == actual, 
                        $"Pixel mismatch at ({x}, {y}). Expected {expected}, got {actual} for format {format}.");
                }
            }
        }
        
        [Fact]
        public void RoundTrip_Fuzzer_OverMultipleDimensions()
        {
            var random = new Random(5555);
            
            // Test 100 random sizes to ensure bit-packing logic holds across arbitrary bounds
            for (int iteration = 0; iteration < 100; iteration++)
            {
                int width = random.Next(1, 64);
                int height = random.Next(1, 64);
                
                var originalState = new SpriteState(width, height);
                for (int i = 0; i < originalState.Pixels.Length; i++)
                {
                    originalState.Pixels[i] = random.Next(2) == 0;
                }

                var settings = new ExportSettings
                {
                    Format = ExportFormat.AdafruitGfx,
                    SpriteName = "fuzzSprite",
                    Compression = CompressionMode.None,
                    ExportAsAnimation = false
                };

                var frames = new List<bool[]> { originalState.Pixels };

                string generatedCode = _codeGen.GenerateCode(
                    frames, width, height, settings,
                    isFloating: false, null, 0, 0, 0, 0);

                var parsedState = new SpriteState(width, height);
                _codeGen.ParseAdafruitGfxToState(generatedCode, parsedState);

                for (int i = 0; i < originalState.Pixels.Length; i++)
                {
                    Assert.True(originalState.Pixels[i] == parsedState.Pixels[i], 
                        $"Pixel mismatch on dimension {width}x{height} at index {i}");
                }
            }
        }
    }
}
