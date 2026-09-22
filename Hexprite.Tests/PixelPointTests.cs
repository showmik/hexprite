using Hexprite.Core;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
    public class PixelPointTests
{
    [Fact]
    public void Constructor_SetsXAndY()
    {
        var point = new PixelPoint(10, 20);

        Assert.Equal(10, point.X);
        Assert.Equal(20, point.Y);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, true)]
    [InlineData(5, 10, 5, 10, true)]
    [InlineData(5, 10, 5, 11, false)]
    [InlineData(5, 10, 6, 10, false)]
    [InlineData(-1, -1, -1, -1, true)]
    public void Equals_SameXAndY_AreEqual(int x1, int y1, int x2, int y2, bool expected)
    {
        var a = new PixelPoint(x1, y1);
        var b = new PixelPoint(x2, y2);

        Assert.Equal(expected, a.Equals(b));
    }

    [Fact]
    public void Equals_SameInstance_ReturnsTrue()
    {
        var point = new PixelPoint(5, 10);

        Assert.True(point.Equals(point));
    }

    [Fact]
    public void ToString_ReturnsExpectedFormat()
    {
        var point = new PixelPoint(10, 20);

        Assert.Equal("(10, 20)", point.ToString());
    }

    [Fact]
    public void ToString_NegativeValues_Works()
    {
        var point = new PixelPoint(-5, -10);

        Assert.Equal("(-5, -10)", point.ToString());
    }

    [Fact]
    public void Immutability_ModifyingValues_NotPossible()
    {
        var point = new PixelPoint(5, 10);

        // PixelPoint is a readonly struct - properties have no setters
        Assert.Equal(5, point.X);
        Assert.Equal(10, point.Y);
    }
}
