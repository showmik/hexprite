using Hexprite.Core;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
    public class PixelClipboardDataTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        bool[,] pixels = new bool[,] { { true, false }, { false, true } };
        var data = new PixelClipboardData(pixels, 8, 16);

        Assert.Same(pixels, data.Pixels);
        Assert.Equal(8, data.Width);
        Assert.Equal(16, data.Height);
    }

    [Fact]
    public void Constructor_StoresReference_NotCopy()
    {
        bool[,] pixels = new bool[,] { { true, false } };
        var data = new PixelClipboardData(pixels, 2, 1);

        // Same reference stored
        Assert.Same(pixels, data.Pixels);

        // Modifying original affects stored reference
        pixels[0, 0] = false;
        Assert.False(data.Pixels[0, 0]);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(8, 8)]
    [InlineData(128, 64)]
    public void Dimensions_CanBeAnyPositiveValue(int width, int height)
    {
        bool[,] pixels = new bool[height, width];
        var data = new PixelClipboardData(pixels, width, height);

        Assert.Equal(width, data.Width);
        Assert.Equal(height, data.Height);
        Assert.Equal(height, data.Pixels.GetLength(0));
        Assert.Equal(width, data.Pixels.GetLength(1));
    }
}
