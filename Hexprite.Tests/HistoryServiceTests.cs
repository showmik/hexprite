using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
    public class HistoryServiceTests
{
    [Fact]
    public void Undo_WhenEmpty_ReturnsSameState()
    {
        var history = new HistoryService();
        var state = new SpriteState(4, 4);

        var result = history.Undo(state);

        Assert.Same(state, result);
    }

    [Fact]
    public void Redo_WhenEmpty_ReturnsSameState()
    {
        var history = new HistoryService();
        var state = new SpriteState(4, 4);

        var result = history.Redo(state);

        Assert.Same(state, result);
    }

    [Fact]
    public void SaveState_NullState_DoesNotThrow()
    {
        var history = new HistoryService();

        var exception = Record.Exception(() => history.SaveState(null!));

        Assert.Null(exception);
    }

    [Fact]
    public void UndoRedo_RoundTrip_RestoresOriginal()
    {
        var history = new HistoryService();
        var original = new SpriteState(4, 4);
        original.Pixels[0] = true;
        original.Pixels[5] = true;
        history.SaveState(original);

        // Modify current state
        var modified = new SpriteState(4, 4);
        modified.Pixels[15] = true;

        // Undo restores original
        var restored = history.Undo(modified);
        Assert.Equal(original.Pixels, restored.Pixels);

        // Redo restores modified
        var redone = history.Redo(restored);
        Assert.Equal(modified.Pixels, redone.Pixels);
    }

    [Fact]
    public void SaveState_AfterUndo_ClearsRedoStack()
    {
        var history = new HistoryService();
        var state1 = new SpriteState(4, 4);
        state1.Pixels[0] = true;
        history.SaveState(state1);

        var state2 = new SpriteState(4, 4);
        state2.Pixels[1] = true;
        history.SaveState(state2);

        // Undo to state1
        var current = new SpriteState(4, 4);
        var restored = history.Undo(current);

        // Save new state - should clear redo stack
        var state3 = new SpriteState(4, 4);
        state3.Pixels[2] = true;
        history.SaveState(state3);

        // Redo should return same state (redo stack cleared)
        var afterRedo = history.Redo(restored);
        Assert.Same(restored, afterRedo);
    }

    [Fact]
    public void SaveState_MaxHistory_Enforced()
    {
        var history = new HistoryService();

        // Save 101 states (exceeds MaxHistory of 100)
        for (int i = 0; i < 101; i++)
        {
            var state = new SpriteState(4, 4);
            state.Pixels[i % 16] = true;
            history.SaveState(state);
        }

        // Undo 100 times should still work, but 101st should return same
        var current = new SpriteState(4, 4);
        SpriteState? restored = null;
        for (int i = 0; i < 100; i++)
        {
            restored = history.Undo(current);
        }

        // 101st undo should return same state
        var finalUndo = history.Undo(restored ?? current);
        Assert.Same(restored, finalUndo);
    }

    [Fact]
    public void Undo_RestoresLayerState()
    {
        var history = new HistoryService();
        var state = new SpriteState(4, 4);
        state.Layers[0].Name = "Original Name";
        history.SaveState(state);

        var modified = new SpriteState(4, 4);
        modified.Layers[0].Name = "Modified Name";

        var restored = history.Undo(modified);

        Assert.Equal("Original Name", restored.Layers[0].Name);
    }

    [Fact]
    public void Undo_RestoresActiveLayerIndex()
    {
        var history = new HistoryService();
        var state = new SpriteState(4, 4);
        state.Layers.Add(new LayerState { Name = "Layer 2", Pixels = new bool[16] });
        state.ActiveLayerIndex = 1;
        history.SaveState(state);

        var modified = new SpriteState(4, 4);
        modified.ActiveLayerIndex = 0;

        var restored = history.Undo(modified);

        Assert.Equal(1, restored.ActiveLayerIndex);
    }

    [Fact]
    public void Undo_RestoresDisplayInvertedState()
    {
        var history = new HistoryService();
        var state = new SpriteState(4, 4) { IsDisplayInverted = true };
        history.SaveState(state);

        var modified = new SpriteState(4, 4) { IsDisplayInverted = false };

        var restored = history.Undo(modified);

        Assert.True(restored.IsDisplayInverted);
    }

    [Fact]
    public void Undo_EnsuresLayers_AfterRestore()
    {
        var history = new HistoryService();
        var state = new SpriteState(4, 4);
        history.SaveState(state);

        var modified = new SpriteState(4, 4);
        modified.Layers.Clear();

        var restored = history.Undo(modified);

        // Should have normalized layers
        Assert.NotEmpty(restored.Layers);
    }

    [Fact]
    public void SaveState_CloneIsolation_MutatingOriginalDoesNotCorruptHistory()
    {
        var history = new HistoryService();
        var state = new SpriteState(4, 4);
        state.Pixels[0] = true;
        state.Pixels[5] = true;
        state.Layers[0].Name = "Before mutation";
        history.SaveState(state);

        // Mutate the original after saving — history must be unaffected
        state.Pixels[0] = false;
        state.Pixels[5] = false;
        state.Pixels[15] = true;
        state.Layers[0].Name = "After mutation";

        var restored = history.Undo(state);

        Assert.True(restored.Pixels[0], "Pixel [0] should reflect pre-mutation value");
        Assert.True(restored.Pixels[5], "Pixel [5] should reflect pre-mutation value");
        Assert.False(restored.Pixels[15], "Pixel [15] should not exist in the saved snapshot");
        Assert.Equal("Before mutation", restored.Layers[0].Name);
    }

    [Fact]
    public void UndoRedo_PreservesMultiFrameAnimationData()
    {
        var history = new HistoryService();

        // Build a state with 2 frames, each with distinct pixel data
        var state = new SpriteState(4, 4);
        state.IsAnimationEnabled = true;
        state.FrameRateFps = 24;
        state.Frames[0].LayerPixels[0].GetMonochromeData()[0] = true;
        state.Frames[0].DelayMultiplier = 2;

        var frame2 = new FrameState
        {
            Name = "Frame 2",
            DelayMultiplier = 3,
            LayerPixels = new System.Collections.Generic.List<IPixelBuffer> { new MonochromePixelBuffer(new bool[16]) }
        };
        frame2.LayerPixels[0].GetMonochromeData()[7] = true;
        state.Frames.Add(frame2);
        state.ActiveFrameIndex = 1;
        state.EnsureLayers();

        history.SaveState(state);

        // Current state is completely different
        var current = new SpriteState(4, 4);

        // Undo → should get back the 2-frame state
        var restored = history.Undo(current);

        Assert.True(restored.IsAnimationEnabled);
        Assert.Equal(24, restored.FrameRateFps);
        Assert.Equal(2, restored.Frames.Count);
        Assert.True(restored.Frames[0].LayerPixels[0].GetMonochromeData()[0]);
        Assert.Equal(2, restored.Frames[0].DelayMultiplier);
        Assert.Equal("Frame 2", restored.Frames[1].Name);
        Assert.True(restored.Frames[1].LayerPixels[0].GetMonochromeData()[7]);
        Assert.Equal(3, restored.Frames[1].DelayMultiplier);
        Assert.Equal(1, restored.ActiveFrameIndex);

        // Redo → should get back the single-frame current state
        var redone = history.Redo(restored);
        Assert.Single(redone.Frames);
    }

    [Fact]
    public void Clear_ResetsCanUndoAndCanRedo()
    {
        var history = new HistoryService();

        var state1 = new SpriteState(4, 4);
        state1.Pixels[0] = true;
        history.SaveState(state1);
        Assert.True(history.CanUndo);

        // Undo to populate redo stack
        var current = new SpriteState(4, 4);
        history.Undo(current);
        Assert.True(history.CanRedo);

        history.Clear();

        Assert.False(history.CanUndo, "CanUndo should be false after Clear");
        Assert.False(history.CanRedo, "CanRedo should be false after Clear");
    }
}
