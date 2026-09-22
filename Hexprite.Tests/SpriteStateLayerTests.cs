using System.Text.Json;
using System.Collections.Generic;
using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
    public class SpriteStateLayerTests
{
    [Fact]
    public void GlobalizeLayer_UsesActiveFrameAndPreservesPreviousContent()
    {
        var state = new SpriteState(2, 2);
        state.Frames.Add(new FrameState { LayerPixels = new List<IPixelBuffer> { new MonochromePixelBuffer(new bool[4]) } });
        state.Frames[0].LayerPixels[0].GetMonochromeData()[0] = true;
        state.Frames[1].LayerPixels[0].GetMonochromeData()[1] = true;
        state.ActiveFrameIndex = 1;

        state.GlobalizeLayer(0, state.ActiveFrameIndex);

        Assert.True(state.Layers[0].IsGlobal);
        Assert.Equal(2, state.Layers[0].PreGlobalFramePixels!.Count);
        Assert.True(state.Frames[0].LayerPixels[0].GetMonochromeData()[1]);
        Assert.Same(state.Frames[0].LayerPixels[0], state.Frames[1].LayerPixels[0]);
        Assert.True(state.Layers[0].PreGlobalFramePixels[0].GetMonochromeData()[0]);
    }

    [Fact]
    public void LocalizeLayer_RestoreRecoversOriginalContent()
    {
        var state = new SpriteState(2, 2);
        state.Frames.Add(new FrameState { LayerPixels = new List<IPixelBuffer> { new MonochromePixelBuffer(new bool[4]) } });
        state.Frames[0].LayerPixels[0].GetMonochromeData()[0] = true;
        state.Frames[1].LayerPixels[0].GetMonochromeData()[1] = true;
        state.GlobalizeLayer(0, 1);
        state.Frames[0].LayerPixels[0].GetMonochromeData()[2] = true;

        state.LocalizeLayer(0, GlobalLayerLocalizeMode.RestorePreviousContent);

        Assert.False(state.Layers[0].IsGlobal);
        Assert.False(ReferenceEquals(state.Frames[0].LayerPixels[0], state.Frames[1].LayerPixels[0]));
        Assert.True(state.Frames[0].LayerPixels[0].GetMonochromeData()[0]);
        Assert.True(state.Frames[1].LayerPixels[0].GetMonochromeData()[1]);
        Assert.False(state.Frames[0].LayerPixels[0].GetMonochromeData()[2]);
    }

    [Fact]
    public void PersistenceRoundTrip_PreservesGlobalLayerBackup()
    {
        var state = new SpriteState(2, 2);
        state.Frames.Add(new FrameState { LayerPixels = new List<IPixelBuffer> { new MonochromePixelBuffer(new bool[4]) } });
        state.Frames[0].LayerPixels[0].GetMonochromeData()[0] = true;
        state.Frames[1].LayerPixels[0].GetMonochromeData()[1] = true;
        state.GlobalizeLayer(0, 1);

        var loaded = JsonSerializer.Deserialize<SpriteState>(JsonSerializer.Serialize(state))!;
        loaded.NormalizeLayerState();

        Assert.True(loaded.Layers[0].IsGlobal);
        Assert.Equal(2, loaded.Layers[0].PreGlobalFramePixels!.Count);
        Assert.Same(loaded.Frames[0].LayerPixels[0], loaded.Frames[1].LayerPixels[0]);
        Assert.True(loaded.Layers[0].PreGlobalFramePixels[0].GetMonochromeData()[0]);
    }

    [Fact]
    public void NormalizeLayerState_CreatesDefaultLayer_AndFixesActiveReference()
    {
        var state = new SpriteState(8, 8);
        state.Frames[0].Layers = new List<LayerState>();
        state.ActiveLayerIndex = 99;
        state.Pixels = new bool[1];

        state.NormalizeLayerState();

        Assert.Single(state.Layers);
        Assert.Equal(0, state.ActiveLayerIndex);
        Assert.Same(state.Frames[0].LayerPixels[0].GetMonochromeData(), state.Pixels);
        Assert.Equal(64, state.Pixels.Length);
    }

    [Fact]
    public void NormalizeLayerState_RepairsInvalidLayerNameAndPixelLength()
    {
        var state = new SpriteState(4, 4);
        state.Layers[0].Name = " ";
        state.Frames[0].LayerPixels[0] = new MonochromePixelBuffer(new bool[3]);

        state.NormalizeLayerState();

        Assert.Equal("Layer 1", state.Layers[0].Name);
        Assert.Equal(16, state.Frames[0].LayerPixels[0].GetMonochromeData().Length);
        Assert.Same(state.Frames[0].LayerPixels[0].GetMonochromeData(), state.Pixels);
    }

    [Fact]
    public void HistoryService_UndoRedo_RestoresLayerMutations()
    {
        var history = new HistoryService();
        var state = new SpriteState(4, 4);
        state.Layers[0].Name = "Base";
        history.SaveState(state);

        state.Layers[0].Name = "Edited";
        var undone = history.Undo(state);
        Assert.Equal("Base", undone.Layers[0].Name);

        var redone = history.Redo(undone);
        Assert.Equal("Edited", redone.Layers[0].Name);
    }

    [Fact]
    public void PersistenceRoundTrip_PreservesLayerOrderAndActiveLayer()
    {
        var state = new SpriteState(4, 4);
        state.Layers.Clear();
        state.Frames[0].LayerPixels.Clear();
        state.Layers.Add(new LayerState
        {
            Name = "Bottom",
            IsVisible = true,
            IsLocked = false
        });
        state.Frames[0].LayerPixels.Add(new MonochromePixelBuffer(new bool[16]));
        state.Layers.Add(new LayerState
        {
            Name = "Top",
            IsVisible = false,
            IsLocked = true
        });
        state.Frames[0].LayerPixels.Add(new MonochromePixelBuffer(new bool[16]));
        state.ActiveLayerIndex = 1;
        state.NormalizeLayerState();

        string json = JsonSerializer.Serialize(state);
        var loaded = JsonSerializer.Deserialize<SpriteState>(json);

        Assert.NotNull(loaded);
        loaded.NormalizeLayerState();
        Assert.Equal(2, loaded.Layers.Count);
        Assert.Equal("Bottom", loaded.Layers[0].Name);
        Assert.Equal("Top", loaded.Layers[1].Name);
        Assert.Equal(1, loaded.ActiveLayerIndex);
        Assert.False(loaded.Layers[1].IsVisible);
        Assert.True(loaded.Layers[1].IsLocked);
    }
    [Fact]
    public void CompositeVisiblePixels_AppliesBlendModesInCorrectOrder()
    {
        var state = new SpriteState(2, 2);
        state.Layers.Clear();
        state.Frames[0].LayerPixels.Clear();

        // Bottom Layer: All pixels ON (Normal)
        state.Layers.Add(new LayerState { Name = "Bottom", IsVisible = true, BlendMode = LayerBlendMode.Normal });
        state.Frames[0].LayerPixels.Add(new MonochromePixelBuffer(new bool[] { true, true, true, true }));

        // Top Layer: First pixel ON (Subtract)
        state.Layers.Add(new LayerState { Name = "Top", IsVisible = true, BlendMode = LayerBlendMode.Subtract });
        state.Frames[0].LayerPixels.Add(new MonochromePixelBuffer(new bool[] { true, false, false, false }));

        state.NormalizeLayerState();

        var composite = state.CompositeVisiblePixels();

        // If Top is rendered after Bottom, the first pixel should be subtracted (false), and the rest true.
        Assert.False(composite[0]);
        Assert.True(composite[1]);
        Assert.True(composite[2]);
        Assert.True(composite[3]);
    }

    [Fact]
    public void GlobalLayer_HistoryUndoRedo_RestoresStateCleanly()
    {
        var history = new HistoryService();
        var state = new SpriteState(2, 2);
        state.Frames.Add(new FrameState { LayerPixels = [new MonochromePixelBuffer(new bool[4])] });
        state.Frames[0].LayerPixels[0].GetMonochromeData()[0] = true;
        state.Frames[1].LayerPixels[0].GetMonochromeData()[1] = true;

        // 1. Before globalizing, save state
        history.SaveState(state);

        // 2. Globalize layer (source frame 0)
        state.GlobalizeLayer(0, 0);

        // 3. Before draw operation 1, save state
        history.SaveState(state);
        state.Frames[0].LayerPixels[0].GetMonochromeData()[2] = true;

        // 4. Before draw operation 2, save state
        history.SaveState(state);
        state.Frames[1].LayerPixels[0].GetMonochromeData()[3] = true;

        // Undo operation 4 -> restores state before op 4 (pixel 2 is true, pixel 3 is false)
        state = history.Undo(state);
        Assert.True(state.Layers[0].IsGlobal);
        Assert.True(state.Frames[0].LayerPixels[0].GetMonochromeData()[2]);
        Assert.False(state.Frames[0].LayerPixels[0].GetMonochromeData()[3]);

        // Undo operation 3 -> restores state before op 3 (pixel 2 is false, pixel 3 is false)
        state = history.Undo(state);
        Assert.True(state.Layers[0].IsGlobal);
        Assert.False(state.Frames[0].LayerPixels[0].GetMonochromeData()[2]);
        Assert.False(state.Frames[0].LayerPixels[0].GetMonochromeData()[3]);

        // Undo operation 2 (globalize) -> restores pre-global state
        state = history.Undo(state);
        Assert.False(state.Layers[0].IsGlobal);
        Assert.True(state.Frames[0].LayerPixels[0].GetMonochromeData()[0]);
        Assert.False(state.Frames[0].LayerPixels[0].GetMonochromeData()[1]);
        Assert.False(state.Frames[1].LayerPixels[0].GetMonochromeData()[0]);
        Assert.True(state.Frames[1].LayerPixels[0].GetMonochromeData()[1]);

        // Redo operation 2 (globalize)
        state = history.Redo(state);
        Assert.True(state.Layers[0].IsGlobal);
        Assert.Same(state.Frames[0].LayerPixels[0], state.Frames[1].LayerPixels[0]);
    }

    [Fact]
    public void GlobalLayer_AddAndDuplicateFrame_SharesGlobalBuffer()
    {
        var state = new SpriteState(2, 2);
        state.Frames.Add(new FrameState { LayerPixels = [new MonochromePixelBuffer(new bool[4])] });
        state.GlobalizeLayer(0, 0);

        // Add frame
        var newFrame = new FrameState { LayerPixels = [] };
        for (int i = 0; i < state.Layers.Count; i++)
        {
            if (state.Layers[i].IsGlobal && state.Frames.Count > 0)
                newFrame.LayerPixels.Add(state.Frames[0].LayerPixels[i]);
            else
                newFrame.LayerPixels.Add(new MonochromePixelBuffer(new bool[4]));
        }
        state.Frames.Add(newFrame);
        state.NormalizeLayerState();

        Assert.Equal(3, state.Frames.Count);
        Assert.Same(state.Frames[0].LayerPixels[0], state.Frames[2].LayerPixels[0]);

        // Duplicate frame
        var dupFrame = state.Frames[1].Clone();
        state.Frames.Add(dupFrame);
        state.NormalizeLayerState();

        Assert.Equal(4, state.Frames.Count);
        Assert.Same(state.Frames[0].LayerPixels[0], state.Frames[3].LayerPixels[0]);
    }
}
