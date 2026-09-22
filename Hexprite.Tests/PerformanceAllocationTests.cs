using System;
using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Performance")]
public class PerformanceAllocationTests
{
    [Fact]
    public void HistoryService_SaveState_DoesNotDoubleCloneSelectionSnapshot()
    {
        var history = new HistoryService();
        var selection = new SelectionSnapshot
        {
            HasActiveSelection = true,
            Mask = new bool[128, 128],
            FloatingPixels = new bool[128, 128],
            OriginalFloatingPixels = new bool[128, 128]
        };
        var state = new SpriteState(128, 128) { SelectionSnapshot = selection };

        long before = GC.GetAllocatedBytesForCurrentThread();
        history.SaveState(state);
        long after = GC.GetAllocatedBytesForCurrentThread();

        long allocated = after - before;
        // Guard against regressions that reintroduce expensive duplicate snapshot clones.
        Assert.True(allocated < 3_000_000, $"Unexpected allocation spike: {allocated:N0} bytes.");
    }

    [Fact]
    public void SelectionService_RepeatedCombine_StaysWithinAllocationBudget()
    {
        var svc = new SelectionService();
        svc.BeginRectangleSelection(10, 10, SelectionMode.Replace);
        svc.UpdateRectangleSelection(80, 80);
        svc.FinalizeSelection();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            svc.BeginRectangleSelection(5, 5, SelectionMode.Add);
            svc.UpdateRectangleSelection(90, 90);
            svc.FinalizeSelection();
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        long allocated = after - before;
        Assert.True(allocated < 20_000_000, $"Combine path allocated too much: {allocated:N0} bytes.");
    }

    [Fact]
    public void OverflowPixelBuffer_RepeatedGetMonochromeData_ReusesViewCacheWithoutAllocations()
    {
        var buffer = new OverflowPixelBuffer(128, 64);
        buffer.SetPixel(10, 10, true);

        // Warm up / prime cache
        var initial = buffer.GetMonochromeData();
        Assert.NotNull(initial);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            var data = buffer.GetMonochromeData();
            Assert.Same(initial, data);
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        long allocated = after - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void DrawingService_DrawLineDithered_RepeatedStrokes_StaysWithinAllocationBudget()
    {
        var svc = new DrawingService();
        var state = new SpriteState(128, 64);
        var buffer = new OverflowPixelBuffer(128, 64);
        state.Frames[0].LayerPixels[0] = buffer;
        state.Layers[0].PreserveOverflow = true;
        state.Pixels = buffer.GetMonochromeData();

        // Warm-up cache
        svc.DrawLineDithered(state, 10, 10, 110, 50, true, 32, DitherPattern.Checkerboard, BrushShape.Circle, 0);

        long before = GC.GetAllocatedBytesForCurrentThread();
        // Simulate continuous drawing across 50 frames
        for (int i = 0; i < 50; i++)
        {
            svc.DrawLineDithered(state, 10 + (i % 20), 10, 110, 50 - (i % 20), true, 32, DitherPattern.Checkerboard, BrushShape.Circle, 0);
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        long allocated = after - before;
        // 50 frames of 32px line drawing should allocate almost nothing (< 100 KB total)
        Assert.True(allocated < 100_000, $"Expected minimal allocations across 50 frames, but allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void DrawingService_DrawLineDithered_LargeCanvasBigBrush_ExecutesUnderBudget()
    {
        var svc = new DrawingService();
        var state = new SpriteState(128, 64);
        var buffer = new OverflowPixelBuffer(128, 64);
        state.Frames[0].LayerPixels[0] = buffer;
        state.Layers[0].PreserveOverflow = true;
        state.Pixels = buffer.GetMonochromeData();

        // Warm up JIT
        svc.DrawLineDithered(state, 10, 10, 110, 50, true, 64, DitherPattern.Checkerboard, BrushShape.Circle, 0);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 20; i++)
        {
            svc.DrawLineDithered(state, 10, 10, 110, 50, true, 64, DitherPattern.Checkerboard, BrushShape.Circle, 0);
        }
        sw.Stop();

        double msPerCall = sw.Elapsed.TotalMilliseconds / 20.0;
        // With Swept Capsule rasterization, a 100px move with 64px brush on 128x64 runs in under 0.5ms (budget: < 2.0ms)
        Assert.True(msPerCall < 2.0, $"Expected execution under 2ms per stroke, but took {msPerCall:F3}ms");
    }
}

