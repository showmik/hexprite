using Hexprite.Core;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
    public class LayerStateTests
{
    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        var original = new LayerState
        {
            Name = "Test Layer",
            IsVisible = true,
            IsLocked = true,
            Pixels = new bool[] { true, false, true, false }
        };

        var clone = original.Clone();

        Assert.Equal(original.Name, clone.Name);
        Assert.Equal(original.IsVisible, clone.IsVisible);
        Assert.Equal(original.IsLocked, clone.IsLocked);
        Assert.Equal(original.Pixels, clone.Pixels);

        // Modify clone and verify original is unchanged
        clone.Name = "Modified";
        Assert.NotNull(clone.Pixels);
        clone.Pixels[0] = false;

        Assert.Equal("Test Layer", original.Name);
        Assert.NotNull(original.Pixels);
        Assert.True(original.Pixels[0]);
    }

    [Fact]
    public void Clone_PixelsArray_IsDeepCopy()
    {
        var original = new LayerState
        {
            Pixels = new bool[] { true, false, true }
        };

        var clone = original.Clone();

        // Pixels arrays should be different references
        Assert.NotSame(original.Pixels, clone.Pixels);

        // But have same values
        Assert.Equal(original.Pixels, clone.Pixels);
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var layer = new LayerState();

        Assert.Equal("Layer", layer.Name);
        Assert.True(layer.IsVisible);
        Assert.False(layer.IsLocked);
        Assert.Null(layer.Pixels);
    }
}
