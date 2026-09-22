using System.Text.Json;
using Hexprite.Core;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
    public class SpriteStateTests
{
    [Fact]
    public void Constructor_SetsDimensions_AndInitializesPixels()
    {
        var state = new SpriteState(8, 16);

        Assert.Equal(8, state.Width);
        Assert.Equal(16, state.Height);
        Assert.Equal(128, state.Pixels.Length);
        Assert.All(state.Pixels, p => Assert.False(p));
    }

    [Fact]
    public void Constructor_CreatesDefaultLayer()
    {
        var state = new SpriteState(4, 4);

        Assert.Single(state.Layers);
        Assert.Equal("Layer 1", state.Layers[0].Name);
        Assert.True(state.Layers[0].IsVisible);
        Assert.Same(state.Pixels, state.Frames[0].LayerPixels[0].GetMonochromeData());
        Assert.Equal(0, state.ActiveLayerIndex);
    }

    [Fact]
    public void ActiveLayerPixels_ReturnsCorrectLayer()
    {
        var state = new SpriteState(4, 4);
        state.Layers.Add(new LayerState { Name = "Layer 2" });
        state.Frames[0].LayerPixels.Add(new MonochromePixelBuffer(new bool[16]));
        state.ActiveLayerIndex = 1;

        Assert.Same(state.Frames[0].LayerPixels[1].GetMonochromeData(), state.ActiveLayerPixels);
    }

    [Fact]
    public void SetActiveLayer_UpdatesIndexAndPixels()
    {
        var state = new SpriteState(4, 4);
        state.Layers.Add(new LayerState { Name = "Layer 2" });
        state.Frames[0].LayerPixels.Add(new MonochromePixelBuffer(new bool[16]));

        state.SetActiveLayer(1);

        Assert.Equal(1, state.ActiveLayerIndex);
        Assert.Same(state.Frames[0].LayerPixels[1].GetMonochromeData(), state.Pixels);
    }

    [Fact]
    public void SetActiveLayer_ClampsToValidRange()
    {
        var state = new SpriteState(4, 4);

        state.SetActiveLayer(-1);
        Assert.Equal(0, state.ActiveLayerIndex);

        state.SetActiveLayer(100);
        Assert.Equal(0, state.ActiveLayerIndex);

        state.Layers.Add(new LayerState { Pixels = new bool[16] });
        state.SetActiveLayer(100);
        Assert.Equal(1, state.ActiveLayerIndex);
    }

    [Fact]
    public void SetActiveLayerPixels_UpdatesActiveLayer()
    {
        var state = new SpriteState(4, 4);
        var newPixels = new bool[16];
        newPixels[0] = true;

        state.SetActiveLayerPixels(newPixels);

        Assert.Same(newPixels, state.Frames[0].LayerPixels[0].GetMonochromeData());
        Assert.Same(newPixels, state.Pixels);
        Assert.True(state.Pixels[0]);
    }

    [Fact]
    public void SetActiveLayerPixels_InvalidLength_Throws()
    {
        var state = new SpriteState(4, 4);

        Assert.Throws<ArgumentException>(() => state.SetActiveLayerPixels(new bool[8]));
        Assert.Throws<ArgumentException>(() => state.SetActiveLayerPixels(new bool[32]));
        Assert.Throws<ArgumentException>(() => state.SetActiveLayerPixels(null!));
    }

    [Fact]
    public void CompositeVisiblePixels_MergesVisibleLayers()
    {
        var state = new SpriteState(4, 4);
        state.Layers.Clear();
        state.Frames[0].LayerPixels.Clear();

        var layer1 = new LayerState { Name = "Bottom", IsVisible = true };
        var pixels1 = new bool[16];
        pixels1[0] = true;

        var layer2 = new LayerState { Name = "Middle", IsVisible = true };
        var pixels2 = new bool[16];
        pixels2[1] = true;

        var layer3 = new LayerState { Name = "Hidden", IsVisible = false };
        var pixels3 = new bool[16];
        pixels3[2] = true;

        state.Layers.Add(layer1);
        state.Layers.Add(layer2);
        state.Layers.Add(layer3);
        state.Frames[0].LayerPixels.Add(new MonochromePixelBuffer(pixels1));
        state.Frames[0].LayerPixels.Add(new MonochromePixelBuffer(pixels2));
        state.Frames[0].LayerPixels.Add(new MonochromePixelBuffer(pixels3));
        state.NormalizeLayerState();

        var composite = state.CompositeVisiblePixels();

        Assert.True(composite[0]); // From visible layer 1
        Assert.True(composite[1]); // From visible layer 2
        Assert.False(composite[2]); // Hidden layer excluded
    }

    [Fact]
    public void CompositeVisiblePixels_NoVisibleLayers_ReturnsEmpty()
    {
        var state = new SpriteState(4, 4);
        state.Layers[0].IsVisible = false;

        var composite = state.CompositeVisiblePixels();

        Assert.All(composite, p => Assert.False(p));
    }

    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        var original = new SpriteState(4, 4);
        original.Pixels[0] = true;
        original.IsDisplayInverted = true;
        original.ExportSettings = new ExportSettings { SpriteName = "Test" };

        var clone = original.Clone();

        Assert.Equal(original.Width, clone.Width);
        Assert.Equal(original.Height, clone.Height);
        Assert.Equal(original.Pixels, clone.Pixels);
        Assert.Equal(original.IsDisplayInverted, clone.IsDisplayInverted);
        Assert.Equal(original.ActiveLayerIndex, clone.ActiveLayerIndex);

        // Modifying clone should not affect original
        clone.Pixels[1] = true;
        Assert.False(original.Pixels[1]);
    }

    [Fact]
    public void Clone_Layers_AreDeepCopied()
    {
        var original = new SpriteState(4, 4);
        original.Layers.Add(new LayerState { Name = "Layer 2", Pixels = new bool[16] });

        var clone = original.Clone();

        Assert.Equal(original.Layers.Count, clone.Layers.Count);
        Assert.NotSame(original.Layers[0], clone.Layers[0]);
        Assert.NotSame(original.Frames[0].LayerPixels[0], clone.Frames[0].LayerPixels[0]);
    }

    [Fact]
    public void Clone_CopiesExportSettings()
    {
        var original = new SpriteState(4, 4);
        original.ExportSettings = new ExportSettings { SpriteName = "MyCoolSprite" };
        original.ImageExportSettings = new ImageExportSettings { Scale = 10 };

        var clone = original.Clone();

        Assert.NotNull(clone.ExportSettings);
        Assert.Equal("MyCoolSprite", clone.ExportSettings.SpriteName);
        Assert.NotSame(original.ExportSettings, clone.ExportSettings);

        Assert.NotNull(clone.ImageExportSettings);
        Assert.Equal(10, clone.ImageExportSettings.Scale);
        Assert.NotSame(original.ImageExportSettings, clone.ImageExportSettings);
    }

    [Fact]
    public void Clone_WithSelectionSnapshot_ClonesSnapshot()
    {
        var original = new SpriteState(4, 4);
        original.SelectionSnapshot = new SelectionSnapshot
        {
            HasActiveSelection = true,
            MinX = 0,
            MaxX = 3
        };

        var clone = original.Clone(cloneSelectionSnapshot: true);

        Assert.NotNull(clone.SelectionSnapshot);
        Assert.Equal(original.SelectionSnapshot.HasActiveSelection, clone.SelectionSnapshot.HasActiveSelection);
        Assert.Equal(original.SelectionSnapshot.MinX, clone.SelectionSnapshot.MinX);
        Assert.NotSame(original.SelectionSnapshot, clone.SelectionSnapshot);
    }

    [Fact]
    public void Clone_WithoutSelectionSnapshot_KeepsReference()
    {
        var original = new SpriteState(4, 4);
        var snapshot = new SelectionSnapshot { HasActiveSelection = true };
        original.SelectionSnapshot = snapshot;

        var clone = original.Clone(cloneSelectionSnapshot: false);

        Assert.Same(snapshot, clone.SelectionSnapshot);
    }

    [Fact]
    public void NormalizeLayerState_CreatesDefaultLayer_WhenEmpty()
    {
        var state = new SpriteState(4, 4);
        state.Layers.Clear();
        state.ActiveLayerIndex = 99;

        var changed = state.NormalizeLayerState();

        Assert.True(changed);
        Assert.Single(state.Layers);
        Assert.Equal(0, state.ActiveLayerIndex);
        Assert.Equal("Layer 1", state.Layers[0].Name);
        Assert.Same(state.Frames[0].LayerPixels[0].GetMonochromeData(), state.Pixels);
    }

    [Fact]
    public void NormalizeLayerState_RepairsInvalidLayerNames()
    {
        var state = new SpriteState(4, 4);
        state.Layers[0].Name = "  ";

        state.NormalizeLayerState();

        Assert.Equal("Layer 1", state.Layers[0].Name);
    }

    [Fact]
    public void NormalizeLayerState_RepairsInvalidPixelArrays()
    {
        var state = new SpriteState(4, 4);
        state.Frames[0].LayerPixels[0] = new MonochromePixelBuffer(new bool[8]); // Wrong size

        state.NormalizeLayerState();

        Assert.Equal(16, state.Frames[0].LayerPixels[0].GetMonochromeData().Length);
    }

    [Fact]
    public void NormalizeLayerState_ClampsActiveLayerIndex()
    {
        var state = new SpriteState(4, 4);
        state.Layers.Add(new LayerState());
        state.Frames[0].LayerPixels.Add(new MonochromePixelBuffer(new bool[16]));
        state.ActiveLayerIndex = 10;

        state.NormalizeLayerState();

        Assert.Equal(1, state.ActiveLayerIndex);
    }

    [Fact]
    public void NormalizeLayerState_RepairsPixelsReference()
    {
        var state = new SpriteState(4, 4);
        var wrongPixels = new bool[16];
        state.Pixels = wrongPixels; // Wrong reference

        state.NormalizeLayerState();

        Assert.Same(state.Frames[state.ActiveFrameIndex].LayerPixels[state.ActiveLayerIndex].GetMonochromeData(), state.Pixels);
    }

    [Fact]
    public void JsonSerialization_RoundTrip_PreservesAllData()
    {
        var original = new SpriteState(8, 8);
        original.Pixels[0] = true;
        original.Pixels[63] = true;
        original.IsDisplayInverted = true;
        original.Layers[0].Name = "Test Layer";
        original.ExportSettings = new ExportSettings { SpriteName = "Test" };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<SpriteState>(json);

        Assert.NotNull(restored);
        restored.NormalizeLayerState();

        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);
        Assert.Equal(original.IsDisplayInverted, restored.IsDisplayInverted);
        Assert.Equal(original.Pixels, restored.Pixels);
        Assert.Equal(original.Layers[0].Name, restored.Layers[0].Name);
    }

    [Fact]
    public void MaxDimension_Is512()
    {
        Assert.Equal(512, SpriteState.MaxDimension);
    }

    [Fact]
    public void ComputeHash_ReturnsSameHashForIdenticalStates()
    {
        var state1 = new SpriteState(4, 4);
        var state2 = new SpriteState(4, 4);

        Assert.Equal(state1.ComputeHash(), state2.ComputeHash());
    }

    [Fact]
    public void ComputeHash_ReturnsDifferentHashWhenPixelsChange()
    {
        var state = new SpriteState(4, 4);
        string hash1 = state.ComputeHash();

        state.Pixels[0] = true;
        string hash2 = state.ComputeHash();
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void NormalizeLayerState_WithNullFramesAndLayers_RepairsStateGracefully()
    {
        var state = new SpriteState(8, 8);
        state.Frames = [null!, new FrameState { Name = "ValidFrame" }, null!];
        state.Layers = [null!, new LayerState { Name = "ValidLayer" }, null!];
        state.FrameRateFps = -10;

        bool changed = state.NormalizeLayerState();

        Assert.True(changed);
        Assert.Single(state.Frames);
        Assert.Single(state.Layers);
        Assert.Equal("ValidFrame", state.Frames[0].Name);
        Assert.Equal("ValidLayer", state.Layers[0].Name);
        Assert.Equal(1, state.FrameRateFps);
    }

    [Fact]
    public void NormalizeLayerState_WithOutOfBoundsFlipperCycleOrder_FiltersInvalidIndices()
    {
        var state = new SpriteState(8, 8);
        state.Frames = [new FrameState { Name = "F1" }, new FrameState { Name = "F2" }];
        state.FlipperCycle = new FlipperAnimationCycle
        {
            FramesOrder = [0, 1, 999, -5, 0]
        };

        bool changed = state.NormalizeLayerState();

        Assert.True(changed);
        Assert.Equal([0, 1, 0], state.FlipperCycle.FramesOrder);
    }
}
