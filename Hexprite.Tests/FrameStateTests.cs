using Hexprite.Core;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
    public class FrameStateTests
{
    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        var original = new FrameState
        {
            Name = "Frame 1",
            LayerPixels = new System.Collections.Generic.List<IPixelBuffer>
            {
                new MonochromePixelBuffer(new bool[] { true, false }),
                new MonochromePixelBuffer(new bool[] { false, true })
            }
        };

        var clone = original.Clone();

        Assert.Equal(original.Name, clone.Name);
        Assert.Equal(original.LayerPixels.Count, clone.LayerPixels.Count);

        // Modify clone and verify original is unchanged
        clone.Name = "Modified";
        clone.LayerPixels[0].GetMonochromeData()[0] = false;

        Assert.Equal("Frame 1", original.Name);
        Assert.True(original.LayerPixels[0].GetMonochromeData()[0]);
    }

    [Fact]
    public void Clone_LayerPixels_AreDeepCopied()
    {
        var original = new FrameState
        {
            LayerPixels = new System.Collections.Generic.List<IPixelBuffer>
            {
                new MonochromePixelBuffer(new bool[] { true, false })
            }
        };

        var clone = original.Clone();

        // Pixels should be different arrays
        Assert.NotSame(original.LayerPixels[0].GetMonochromeData(), clone.LayerPixels[0].GetMonochromeData());
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var frame = new FrameState();

        Assert.Equal("Frame 1", frame.Name);
        Assert.Null(frame.Layers);
        Assert.Empty(frame.LayerPixels);
    }
}
