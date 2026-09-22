using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
    public class CodeGenerationLayerTests
{
    [Fact]
    public void GenerateCode_UsesVisibleLayerComposite_HiddenLayersExcluded()
    {
        var state = new SpriteState(8, 1);
        state.Layers.Clear();
        state.Frames[0].LayerPixels.Clear();
        state.Layers.Add(new LayerState { Name = "Visible", IsVisible = true });
        state.Frames[0].LayerPixels.Add(new MonochromePixelBuffer(new[] { true, false, false, false, false, false, false, false }));
        state.Layers.Add(new LayerState { Name = "Hidden", IsVisible = false });
        state.Frames[0].LayerPixels.Add(new MonochromePixelBuffer(new[] { false, true, false, false, false, false, false, false }));
        state.ActiveLayerIndex = 0;
        state.NormalizeLayerState();
        state.Pixels = state.CompositeVisiblePixels();

        var svc = new CodeGeneratorService();
        var settings = new ExportSettings
        {
            Format = ExportFormat.RawHex,
            UseCommaSeparator = true
        };

        string code = svc.GenerateCode(new System.Collections.Generic.List<bool[]> { state.Pixels }, state.Width, state.Height, settings, false, null, 0, 0, 0, 0);
        Assert.Contains("0x80", code);
        Assert.DoesNotContain("0x40", code);
    }

    [Fact]
    public void CompositeVisiblePixels_MergesVisibleLayers()
    {
        var state = new SpriteState(8, 1);
        state.Layers.Clear();
        state.Frames[0].LayerPixels.Clear();
        state.Layers.Add(new LayerState { Name = "Bottom", IsVisible = true });
        state.Frames[0].LayerPixels.Add(new MonochromePixelBuffer(new[] { true, false, false, false, false, false, false, false }));
        state.Layers.Add(new LayerState { Name = "Top", IsVisible = true });
        state.Frames[0].LayerPixels.Add(new MonochromePixelBuffer(new[] { false, true, false, false, false, false, false, false }));

        bool[] composite = state.CompositeVisiblePixels();
        Assert.True(composite[0]);
        Assert.True(composite[1]);
    }

    [Fact]
    public void ParseHexToState_WritesOnlyToActiveLayer()
    {
        var state = new SpriteState(8, 1);
        state.Layers.Clear();
        state.Frames[0].LayerPixels.Clear();
        state.Layers.Add(new LayerState { Name = "Bottom", IsVisible = true });
        state.Frames[0].LayerPixels.Add(new MonochromePixelBuffer(new bool[8]));
        state.Layers.Add(new LayerState { Name = "Top", IsVisible = true });
        state.Frames[0].LayerPixels.Add(new MonochromePixelBuffer(new bool[8]));
        state.ActiveLayerIndex = 1;
        state.NormalizeLayerState();

        var svc = new CodeGeneratorService();
        svc.ParseHexToState("0x80", state);

        Assert.False(state.Frames[0].LayerPixels[0].GetMonochromeData()[0]);
        Assert.True(state.Frames[0].LayerPixels[1].GetMonochromeData()[0]);
    }

    [Fact]
    public void GenerateCode_OverlaysFloatingSelectionPixels()
    {
        var state = new SpriteState(8, 1);
        Array.Clear(state.Pixels, 0, state.Pixels.Length);

        var svc = new CodeGeneratorService();
        var settings = new ExportSettings
        {
            Format = ExportFormat.RawHex,
            UseCommaSeparator = true
        };

        bool[,] floating = new bool[1, 1];
        floating[0, 0] = true;

        string code = svc.GenerateCode(new System.Collections.Generic.List<bool[]> { state.Pixels }, state.Width, state.Height, settings, true, floating, 0, 0, 1, 1);
        Assert.Contains("0x80", code);
    }

    [Fact]
    public async Task GenerateCodeAsync_RespectsProvidedProjectedPixels()
    {
        var state = new SpriteState(8, 1);
        state.Layers.Clear();
        state.Layers.Add(new LayerState
        {
            Name = "Layer 1",
            IsVisible = true,
            Pixels = new[] { false, false, false, false, false, false, false, false }
        });
        state.ActiveLayerIndex = 0;
        state.NormalizeLayerState();

        // Simulate VM-projected export pixels (merged visible result) that differ
        // from active-layer pixels.
        state.Pixels = new[] { true, false, false, false, false, false, false, false };

        var svc = new CodeGeneratorService();
        var settings = new ExportSettings
        {
            Format = ExportFormat.RawHex,
            UseCommaSeparator = true
        };

        string code = await svc.GenerateCodeAsync(new System.Collections.Generic.List<bool[]> { state.Pixels }, state.Width, state.Height, settings, false, null, 0, 0, 0, 0);
        Assert.Contains("0x80", code);
    }
}
