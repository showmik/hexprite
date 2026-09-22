using System;
using System.Linq;
using System.Windows.Media;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
public sealed class ColorQuantizerTests
{
    [Fact]
    public void Quantize_NullBuffer_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ColorQuantizer.Quantize(null!, 16, 16));
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(16, 0)]
    public void Quantize_ZeroDimensions_ReturnsEmpty(int w, int h)
    {
        var (palette, indexed) = ColorQuantizer.Quantize([0xFF000000], w, h);
        Assert.Empty(palette);
        Assert.Empty(indexed);
    }

    [Fact]
    public void Quantize_FewUniqueColors_PerformsExactLosslessMapping()
    {
        // 4 distinct colors in BGRA format
        uint black = 0xFF000000;
        uint red = 0xFFFF0000;
        uint green = 0xFF00FF00;
        uint blue = 0xFF0000FF;

        uint[] buffer = [black, red, green, blue];

        var (palette, indexed) = ColorQuantizer.Quantize(buffer, 2, 2, enableDithering: false, maxColors: 256);

        Assert.Equal(4, palette.Length);
        Assert.Equal(4, indexed.Length);

        // Every pixel maps to its exact color
        for (int i = 0; i < buffer.Length; i++)
        {
            Color mapped = palette[indexed[i]];
            uint original = buffer[i];
            byte b = (byte)(original & 0xFF);
            byte g = (byte)((original >> 8) & 0xFF);
            byte r = (byte)((original >> 16) & 0xFF);
            byte a = (byte)((original >> 24) & 0xFF);

            Assert.Equal(r, mapped.R);
            Assert.Equal(g, mapped.G);
            Assert.Equal(b, mapped.B);
            Assert.Equal(a, mapped.A);
        }
    }

    [Fact]
    public void Quantize_ManyColors_ReducesToMaxColors()
    {
        // Generate gradient with 500 distinct colors
        uint[] buffer = new uint[500];
        for (int i = 0; i < buffer.Length; i++)
        {
            byte r = (byte)(i % 256);
            byte g = (byte)((i * 3) % 256);
            byte b = (byte)((i * 7) % 256);
            buffer[i] = (uint)((0xFF << 24) | (r << 16) | (g << 8) | b);
        }

        var (palette, indexed) = ColorQuantizer.Quantize(buffer, 25, 20, enableDithering: false, maxColors: 64);

        Assert.True(palette.Length <= 64);
        Assert.Equal(500, indexed.Length);
        Assert.All(indexed, idx => Assert.True(idx < palette.Length));
    }

    [Fact]
    public void Quantize_WithFloydSteinbergDithering_ProducesValidIndices()
    {
        uint[] buffer = new uint[64 * 64];
        // Smooth gradient
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                byte val = (byte)((x + y) * 2);
                buffer[y * 64 + x] = (uint)((0xFF << 24) | (val << 16) | (val << 8) | val);
            }
        }

        var (palette, indexed) = ColorQuantizer.Quantize(buffer, 64, 64, enableDithering: true, maxColors: 16);

        Assert.True(palette.Length <= 16);
        Assert.Equal(64 * 64, indexed.Length);
        Assert.All(indexed, idx => Assert.True(idx < palette.Length));
    }

    [Fact]
    public void FindNearestColor_FindsExactOrClosestMatch()
    {
        Color[] palette = [Colors.Black, Colors.White, Colors.Red, Colors.Green, Colors.Blue];

        Assert.Equal(0, ColorQuantizer.FindNearestColor(0, 0, 0, palette));       // Black
        Assert.Equal(1, ColorQuantizer.FindNearestColor(255, 255, 255, palette)); // White
        Assert.Equal(2, ColorQuantizer.FindNearestColor(250, 10, 10, palette));   // Near Red
        Assert.Equal(3, ColorQuantizer.FindNearestColor(10, 240, 10, palette));   // Near Green
        Assert.Equal(4, ColorQuantizer.FindNearestColor(5, 5, 250, palette));     // Near Blue
    }
}
