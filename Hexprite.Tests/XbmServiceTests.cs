using System;
using System.IO;
using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class XbmServiceTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly XbmService _service;

        public XbmServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "HexpriteXbmTests_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_tempDir);
            _service = new XbmService();
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }

        [Fact]
        public void ExportImage_WritesValidXbmSyntax()
        {
            var sprite = new SpriteState(4, 8);
            var pixels = sprite.ActiveLayerPixels;
            // Diagonal line: (0,0), (1,1), (2,2), (3,3)
            pixels[0 * 4 + 0] = true;
            pixels[1 * 4 + 1] = true;
            pixels[2 * 4 + 2] = true;
            pixels[3 * 4 + 3] = true;
            sprite.SetActiveLayerPixels(pixels);

            string targetPath = Path.Combine(_tempDir, "diagonal.xbm");
            _service.ExportImage(sprite, 0, targetPath);

            Assert.True(File.Exists(targetPath));
            string content = File.ReadAllText(targetPath);

            Assert.Contains("#define diagonal_width 4", content);
            Assert.Contains("#define diagonal_height 8", content);
            Assert.Contains("static unsigned char diagonal_bits[] = {", content);
            // Row 0: x=0 set -> bit 0 -> 0x01; row 1: x=1 set -> bit 1 -> 0x02
            Assert.Contains("0x01", content);
            Assert.Contains("0x02", content);
            Assert.Contains("0x04", content);
            Assert.Contains("0x08", content);
        }

        [Fact]
        public void ParseFile_ValidXbm_ExtractsDimensionsAndBody()
        {
            string path = Path.Combine(_tempDir, "icon.xbm");
            File.WriteAllText(path,
                "#define icon_width 8\n" +
                "#define icon_height 8\n" +
                "static unsigned char icon_bits[] = {\n" +
                "  0xff, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0xff };\n");

            var (width, height, body) = _service.ParseFile(path);

            Assert.Equal(8, width);
            Assert.Equal(8, height);
            Assert.Contains("0xff", body);
        }

        [Theory]
        [InlineData("static unsigned char icon_bits[] = { 0xff };")] // missing both #defines
        [InlineData("#define icon_width 8\nstatic unsigned char icon_bits[] = { 0xff };")] // missing height
        [InlineData("#define icon_width 0\n#define icon_height 8\nstatic unsigned char icon_bits[] = { 0xff };")] // zero width
        public void ParseFile_MissingOrInvalidDimensions_ThrowsFormatException(string content)
        {
            string path = Path.Combine(_tempDir, "bad.xbm");
            File.WriteAllText(path, content);

            Assert.Throws<FormatException>(() => _service.ParseFile(path));
        }

        [Fact]
        public void ExportThenParseThenUnpack_RoundTripsPixelsExactly()
        {
            // Arrange — a 16x16 sprite with a distinctive pattern.
            var sprite = new SpriteState(16, 16);
            var pixels = sprite.ActiveLayerPixels;
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    pixels[y * 16 + x] = (x + y) % 3 == 0;
                }
            }
            sprite.SetActiveLayerPixels(pixels);

            string path = Path.Combine(_tempDir, "pattern.xbm");

            // Act — export via XbmService, then parse dimensions + unpack via the
            // same CodeGeneratorService.ParseXbmToState the real import path uses.
            _service.ExportImage(sprite, 0, path);
            var (width, height, body) = _service.ParseFile(path);

            var reimported = new SpriteState(width, height);
            var codeGen = new CodeGeneratorService();
            codeGen.ParseXbmToState(body, reimported);

            // Assert
            Assert.Equal(16, width);
            Assert.Equal(16, height);
            bool[] original = sprite.CompositeFramePixels(0);
            bool[] roundTripped = reimported.Pixels;
            Assert.Equal(original, roundTripped);
        }
    }
}
