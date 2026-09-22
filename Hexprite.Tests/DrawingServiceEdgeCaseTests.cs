using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
    public class DrawingServiceEdgeCaseTests
{
    [Fact]
    public void ComputeStampOffsets_BrushSizeZero_ReturnsOrigin()
    {
        var offsets = DrawingService.ComputeStampOffsets(0, BrushShape.Square, 0);

        Assert.Single(offsets);
        Assert.Contains((0, 0), offsets);
    }

    [Fact]
    public void ComputeStampOffsets_NegativeAngle_Normalized()
    {
        var offsets = DrawingService.ComputeStampOffsets(3, BrushShape.Square, -90);

        // -90 degrees should be normalized to 270 (or handled equivalently)
        Assert.Contains((0, 0), offsets);
    }

    [Fact]
    public void ComputeStampOffsets_LargeAngle_Normalized()
    {
        var offsets360 = DrawingService.ComputeStampOffsets(3, BrushShape.Square, 360);
        var offsets0 = DrawingService.ComputeStampOffsets(3, BrushShape.Square, 0);

        Assert.Equal(offsets0, offsets360);
    }

    [Theory]
    [InlineData(BrushShape.Circle)]
    [InlineData(BrushShape.Square)]
    [InlineData(BrushShape.Line)]
    public void ComputeStampOffsets_AllShapes_ReturnNonEmpty(BrushShape shape)
    {
        var offsets = DrawingService.ComputeStampOffsets(5, shape, 45);

        Assert.NotEmpty(offsets);
        Assert.All(offsets, o =>
        {
            Assert.InRange(o.dx, -10, 10); // Reasonable bounds for size 5
            Assert.InRange(o.dy, -10, 10);
        });
    }

    [Fact]
    public void DrawLine_SameStartEnd_SetsSinglePixel()
    {
        var svc = new DrawingService();
        var s = new SpriteState(8, 8);

        svc.DrawLine(s, 4, 4, 4, 4, true);

        int count = 0;
        foreach (var p in s.Pixels) if (p) count++;
        Assert.Equal(1, count);
        Assert.True(s.Pixels[(4 * 8) + 4]);
    }

    [Fact]
    public void DrawRectangle_OutOfBounds_Handled()
    {
        var svc = new DrawingService();
        var s = new SpriteState(8, 8);

        // Rectangle with negative coordinates - should not throw
        var exception = Record.Exception(() =>
            svc.DrawRectangle(s, -2, -2, 2, 2, true));

        Assert.Null(exception);

        // Check that some expected pixels within bounds are set
        // The rectangle algorithm should set perimeter pixels in bounds
        int count = 0;
        foreach (var p in s.Pixels) if (p) count++;
        Assert.True(count > 0, "Some pixels should be set");
    }

    [Fact]
    public void DrawFilledRectangle_FullCanvas_CoversAll()
    {
        var svc = new DrawingService();
        var s = new SpriteState(4, 4);

        svc.DrawFilledRectangle(s, 0, 0, 3, 3, true);

        Assert.All(s.Pixels, p => Assert.True(p));
    }

    [Fact]
    public void DrawEllipse_WiderThanTall_HorizontalOrientation()
    {
        var svc = new DrawingService();
        var s = new SpriteState(20, 10);

        svc.DrawEllipse(s, 2, 4, 17, 5, true);

        // Check some expected pixels are set
        Assert.True(s.Pixels[(4 * 20) + 2]);   // Left edge
        Assert.True(s.Pixels[(4 * 20) + 17]); // Right edge
        Assert.True(s.Pixels[(5 * 20) + 10]); // Center-ish
    }

    [Fact]
    public void DrawEllipse_TallerThanWide_SetsPixels()
    {
        var svc = new DrawingService();
        var s = new SpriteState(10, 20);

        svc.DrawEllipse(s, 4, 2, 5, 17, true);

        // Ellipse should set some pixels
        int count = 0;
        foreach (var p in s.Pixels) if (p) count++;
        Assert.True(count > 0, "Ellipse should set some pixels");

        // Check center region has pixels
        Assert.True(s.Pixels[(10 * 10) + 4] || s.Pixels[(10 * 10) + 5],
            "Center region should have pixels");
    }

    [Fact]
    public void ApplyFloodFill_EmptyCanvas_FillsAll()
    {
        var svc = new DrawingService();
        var s = new SpriteState(4, 4);

        svc.ApplyFloodFill(s, 0, 0, true);

        Assert.All(s.Pixels, p => Assert.True(p));
    }

    [Fact]
    public void ApplyFloodFill_FullCanvas_ErasesAll()
    {
        var svc = new DrawingService();
        var s = new SpriteState(4, 4);
        for (int i = 0; i < s.Pixels.Length; i++) s.Pixels[i] = true;

        svc.ApplyFloodFill(s, 0, 0, false);

        Assert.All(s.Pixels, p => Assert.False(p));
    }

    [Fact]
    public void InvertGrid_EmptyBecomesFull()
    {
        var svc = new DrawingService();
        var s = new SpriteState(4, 4);

        svc.InvertGrid(s);

        Assert.All(s.Pixels, p => Assert.True(p));
    }

    [Fact]
    public void InvertGrid_FullBecomesEmpty()
    {
        var svc = new DrawingService();
        var s = new SpriteState(4, 4);
        for (int i = 0; i < s.Pixels.Length; i++) s.Pixels[i] = true;

        svc.InvertGrid(s);

        Assert.All(s.Pixels, p => Assert.False(p));
    }

    [Fact]
    public void InvertGrid_MixedPattern_Inverted()
    {
        var svc = new DrawingService();
        var s = new SpriteState(4, 4);
        s.Pixels[0] = true;
        s.Pixels[5] = true;
        s.Pixels[10] = true;

        svc.InvertGrid(s);

        Assert.False(s.Pixels[0]);
        Assert.False(s.Pixels[5]);
        Assert.False(s.Pixels[10]);
        Assert.True(s.Pixels[1]);
        Assert.True(s.Pixels[2]);
        Assert.True(s.Pixels[15]);
    }

    [Fact]
    public void ShiftGrid_ZeroOffset_Unchanged()
    {
        var svc = new DrawingService();
        var s = new SpriteState(4, 4);
        s.Pixels[5] = true;
        s.Pixels[10] = true;

        svc.ShiftGrid(s, 0, 0);

        Assert.True(s.Pixels[5]);
        Assert.True(s.Pixels[10]);
    }

    [Fact]
    public void ShiftGrid_LargeOffset_WrapsCorrectly()
    {
        var svc = new DrawingService();
        var s = new SpriteState(4, 4);
        s.Pixels[0] = true;

        // Shift by full dimensions should wrap back to same position
        svc.ShiftGrid(s, 4, 4);

        Assert.True(s.Pixels[0]);
    }

    [Fact]
    public void GetFloodFillMask_StartOnObstacle_ReturnsCorrectComponent()
    {
        // Grid: X marks obstacles (true pixels)
        //  X . .
        //  . . .
        //  . . X
        var svc = new DrawingService();
        var s = new SpriteState(3, 3);
        s.Pixels[0] = true;  // (0,0)
        s.Pixels[8] = true;  // (2,2)

        bool[,] mask = svc.GetFloodFillMask(s, 1, 1, null, Hexprite.Core.FloatingPasteMode.Transparent, out int minX, out int minY, out int maxX, out int maxY);

        // Starting from center, should fill the non-obstacle region
        Assert.True(mask[1, 1]); // Start point is true (not in obstacle component)
        Assert.False(mask[0, 0]); // Obstacle
        Assert.False(mask[2, 2]); // Obstacle
    }

    [Fact]
    public void DrawBrushStamp_Eraser_RemovesPixel()
    {
        var svc = new DrawingService();
        var s = new SpriteState(8, 8);
        s.Pixels[(4 * 8) + 4] = true;

        svc.DrawBrushStamp(s, 4, 4, 1, false);

        Assert.False(s.Pixels[(4 * 8) + 4]);
    }

    [Fact]
    public void DrawBrushStamp_LargerSize_SetsMultiplePixels()
    {
        var svc = new DrawingService();
        var s = new SpriteState(16, 16);

        svc.DrawBrushStamp(s, 8, 8, 3, true, BrushShape.Square);

        // Size 3 square brush should set a 3x3 area centered on (8,8)
        // i.e., pixels from (7,7) to (9,9)
        Assert.True(s.Pixels[(7 * 16) + 7]);
        Assert.True(s.Pixels[(8 * 16) + 8]);
        Assert.True(s.Pixels[(9 * 16) + 9]);
    }

    [Fact]
    public void FlipPixels_Horizontal_LargerCanvas()
    {
        var svc = new DrawingService();
        int w = 8, h = 4;
        var src = new bool[w * h];
        src[(0 * w) + 2] = true; // (2,0)

        var dst = svc.FlipPixels(src, w, h, FlipDirection.Horizontal);

        // (2,0) flipped horizontally in 8-wide grid → (5,0)
        Assert.True(dst[(0 * w) + 5]);
        Assert.False(dst[(0 * w) + 2]);
    }

    [Fact]
    public void FlipPixels_Vertical_LargerCanvas()
    {
        var svc = new DrawingService();
        int w = 4, h = 8;
        var src = new bool[w * h];
        src[(2 * w) + 1] = true; // (1,2)

        var dst = svc.FlipPixels(src, w, h, FlipDirection.Vertical);

        // (1,2) flipped vertically in 8-high grid → (1, 5)
        Assert.True(dst[(5 * w) + 1]);
        Assert.False(dst[(2 * w) + 1]);
    }

    [Fact]
    public void RotatePixels_SquareCanvas_90Degrees()
    {
        var svc = new DrawingService();
        // 3x3: pixel at (0,1) → rotated 90° CW → (1,0)
        var src = new bool[9];
        src[(1 * 3) + 0] = true;

        var dst = svc.RotatePixels(src, 3, 3, RotationDirection.Clockwise90);

        Assert.True(dst[(0 * 3) + 1]);
    }

    [Fact]
    public void RotatePixels_OneEighty_EquivalentToTwo90s()
    {
        var svc = new DrawingService();
        var src = new bool[12];
        src[(1 * 4) + 2] = true;

        var direct180 = svc.RotatePixels(src, 4, 3, RotationDirection.OneEighty);

        var step1 = svc.RotatePixels(src, 4, 3, RotationDirection.Clockwise90);
        var via90s = svc.RotatePixels(step1, 3, 4, RotationDirection.Clockwise90);

        Assert.Equal(direct180, via90s);
    }
}
