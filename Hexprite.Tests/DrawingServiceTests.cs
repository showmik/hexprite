using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
    public class DrawingServiceTests
{
    private static SpriteState MakeState(int w, int h, params (int x, int y)[] onPixels)
    {
        var s = new SpriteState(w, h);
        foreach (var (x, y) in onPixels)
            s.Pixels[(y * w) + x] = true;
        return s;
    }

    private static (SpriteState State, OverflowPixelBuffer Buffer) MakeOverflowState(
        int w, int h, params (int x, int y)[] onPixels)
    {
        var state = new SpriteState(w, h);
        var buffer = new OverflowPixelBuffer(w, h, initialMargin: 8);
        foreach (var (x, y) in onPixels)
            buffer.SetPixel(x, y, true);

        state.Frames[0].LayerPixels[0] = buffer;
        state.Layers[0].PreserveOverflow = true;
        state.Pixels = buffer.GetMonochromeData();
        return (state, buffer);
    }

    private static int CountOn(SpriteState state)
    {
        int count = 0;
        for (int i = 0; i < state.Pixels.Length; i++)
            if (state.Pixels[i]) count++;
        return count;
    }

    private sealed class PixelClipStub : IPixelClip
    {
        public required Func<int, int, bool> Selector { get; init; }
        public bool IsPixelInClip(int x, int y) => Selector(x, y);
    }

    private sealed class ClipSelectionStub : ISelectionService
    {
        public required Func<int, int, bool> Selector { get; init; }
        public bool HasActiveSelection { get; set; } = true;
        public bool IsSelecting => false;
        public bool IsFloating => false;
        public bool IsDragging => false;
        public bool IsTransforming => false;
        public TransformHandle ActiveTransformHandle => TransformHandle.None;

        public int MinX => 0;
        public int MaxX => 0;
        public int MinY => 0;
        public int MaxY => 0;
        public int DragMinX => 0;
        public int DragMaxX => 0;
        public int DragMinY => 0;
        public int DragMaxY => 0;
        public bool[,]? Mask => null;
        public bool[,]? BaseMask => null;
        public int BaseMinX => 0;
        public int BaseMinY => 0;
        public int BaseMaxX => 0;
        public int BaseMaxY => 0;
        public bool[,]? FloatingPixels { get; set; }
        public int FloatingX => 0;
        public int FloatingY => 0;
        public int FloatingWidth => 0;
        public int FloatingHeight => 0;
        public IReadOnlyList<PixelPoint> LassoPoints => [];
        public event EventHandler? SelectionChanged { add { } remove { } }
        public void BeginRectangleSelection(int x, int y, SelectionMode mode = SelectionMode.Replace) { }
        public void UpdateRectangleSelection(int currentX, int currentY, bool isAltDown = false) { }
        public void BeginEllipseSelection(int x, int y, SelectionMode mode = SelectionMode.Replace) { }
        public void UpdateEllipseSelection(int currentX, int currentY, bool isAltDown = false) { }
        public void BeginLassoSelection(int x, int y, SelectionMode mode = SelectionMode.Replace) { }
        public void AddLassoPoint(int x, int y) { }
        public void FinalizeSelection() { }
        public void ApplyMask(bool[,] mask, int minX, int minY, int maxX, int maxY, SelectionMode mode) { }
        public bool IsPixelInSelection(int x, int y) => Selector(x, y);
        public bool IsPointInLasso(int x, int y) => false;
        public bool HasAnyPixelInSelection(SpriteState state) => false;
        public int CountPixelsInSelection(SpriteState state) => 0;
        public bool CanReselect => false;
        public void Reselect() { }
        public void NudgeSelection(int dx, int dy, int canvasWidth, int canvasHeight) { }
        public void LiftSelection(SpriteState state) { }
        public void CommitSelection(SpriteState state, FloatingPasteMode pasteMode = FloatingPasteMode.Transparent) { }
        public void ReplaceFloatingData(bool[,] pixels, int x, int y, int w, int h) { }
        public void DeleteSelection(SpriteState state) { }
        public void Cancel() { }
        public void CancelSelectionDrag() { }
        public PixelClipboardData? CopySelection(SpriteState state) => null;
        public void PasteAsFloating(PixelClipboardData data, int canvasWidth, int canvasHeight) { }
        public void PasteAsFloatingAt(PixelClipboardData data, int x, int y) { }
        public void BeginDrag() { }
        public void MoveFloatingTo(int newX, int newY) { }
        public void EndDrag() { }
        public SelectionSnapshot CreateSnapshot() => new();
        public void RestoreSnapshot(SelectionSnapshot snapshot) { }
        public void BeginTransform(TransformHandle handle) { }
        public void UpdateTransform(int newX, int newY, int newW, int newH, bool flipX = false, bool flipY = false) { }
        public void CommitTransform() { }
        public void CancelTransform() { }
        public void FlipFloatingHorizontally() { }
        public void FlipFloatingVertically() { }
        public double RotationAngle => 0;
        public int OriginalFloatingX => 0;
        public int OriginalFloatingY => 0;
        public int OriginalFloatingW => 0;
        public int OriginalFloatingH => 0;
        public bool IsPointInSelectionBounds(int x, int y) => Selector(x, y);
        public void UpdateRotation(double angleDeg) { }
        public (bool[,] pixels, bool[,]? mask, int x, int y, int w, int h) GetEffectiveFloating() => (new bool[0, 0], null, 0, 0, 0, 0);
    }

    [Fact]
    public void ComputeStampOffsets_SizeOne_ReturnsOnlyOrigin()
    {
        var offsets = DrawingService.ComputeStampOffsets(1, BrushShape.Circle, 0);
        Assert.Single(offsets);
        Assert.Contains((0, 0), offsets);
    }

    [Fact]
    public void ComputeStampOffsets_Square90_HasExpectedCardinalPoints()
    {
        var offsets = DrawingService.ComputeStampOffsets(3, BrushShape.Square, 90);
        Assert.Contains((0, 0), offsets);
        Assert.Contains((-1, 0), offsets);
        Assert.Contains((1, 0), offsets);
        Assert.Contains((0, -1), offsets);
        Assert.Contains((0, 1), offsets);
    }

    [Fact]
    public void DrawBrushStamp_SizeOne_SetsSinglePixel()
    {
        var svc = new DrawingService();
        var s = MakeState(8, 8);

        svc.DrawBrushStamp(s, 3, 4, 1, true);

        Assert.True(s.Pixels[(4 * 8) + 3]);
        Assert.Equal(1, CountOn(s));
    }

    [Fact]
    public void DrawBrushStamp_ClippedSelection_OnlyWritesAllowedPixel()
    {
        var svc = new DrawingService();
        var s = MakeState(8, 8);
        var clip = new PixelClipStub { Selector = static (x, y) => x == 3 && y == 3 };

        svc.DrawBrushStamp(s, 3, 3, 3, true, BrushShape.Square, 0, clip);

        Assert.True(s.Pixels[(3 * 8) + 3]);
        Assert.Equal(1, CountOn(s));
    }

    [Fact]
    public void DrawBrushStamp_OutOfBoundsCenter_DrawsFootprintOnCanvas()
    {
        var svc = new DrawingService();
        var s = MakeState(10, 10);
        
        // Brush size 5, square shape, center at (-2, 5)
        // Offset for square size 5 is dx: -2..2, dy: -2..2
        // px = cx + dx => px: -4..0, py: 3..7
        // So only x=0 should be drawn.
        svc.DrawBrushStamp(s, -2, 5, 5, true, BrushShape.Square, 0);

        Assert.True(s.Pixels[(5 * 10) + 0]); // center y=5, x=0 should be drawn
        Assert.True(s.Pixels[(3 * 10) + 0]);
        Assert.True(s.Pixels[(7 * 10) + 0]);
        Assert.False(s.Pixels[(5 * 10) + 1]); // x=1 should not be drawn
        
        // Total pixels drawn: y=3..7 at x=0 => 5 pixels
        Assert.Equal(5, CountOn(s));
    }

    [Fact]
    public void DrawLine_Diagonal_SetsExpectedPixels()
    {
        var svc = new DrawingService();
        var s = MakeState(6, 6);

        svc.DrawLine(s, 0, 0, 3, 3, true);

        Assert.True(s.Pixels[(0 * 6) + 0]);
        Assert.True(s.Pixels[(1 * 6) + 1]);
        Assert.True(s.Pixels[(2 * 6) + 2]);
        Assert.True(s.Pixels[(3 * 6) + 3]);
        Assert.Equal(4, CountOn(s));
    }

    [Fact]
    public void DrawLine_ReverseOrder_ProducesSameResult()
    {
        var svc = new DrawingService();
        var a = MakeState(6, 6);
        var b = MakeState(6, 6);

        svc.DrawLine(a, 0, 3, 5, 3, true);
        svc.DrawLine(b, 5, 3, 0, 3, true);

        Assert.Equal(a.Pixels, b.Pixels);
    }

    [Fact]
    public void DrawLine_WithBrushSize_DrawsThickerThanThinLine()
    {
        var svc = new DrawingService();
        var thin = MakeState(10, 10);
        var thick = MakeState(10, 10);

        svc.DrawLine(thin, 1, 1, 8, 1, true);
        svc.DrawLine(thick, 1, 1, 8, 1, true, 3, BrushShape.Circle);

        Assert.True(CountOn(thick) > CountOn(thin));
    }

    [Fact]
    public void DrawRectangle_DegenerateToPoint_SetsSinglePixel()
    {
        var svc = new DrawingService();
        var s = MakeState(5, 5);

        svc.DrawRectangle(s, 2, 2, 2, 2, true);

        Assert.True(s.Pixels[(2 * 5) + 2]);
        Assert.Equal(1, CountOn(s));
    }

    [Fact]
    public void DrawRectangle_OnlyPerimeterIsSet()
    {
        var svc = new DrawingService();
        var s = MakeState(6, 6);

        svc.DrawRectangle(s, 1, 1, 4, 4, true);

        Assert.True(s.Pixels[(1 * 6) + 1]);
        Assert.True(s.Pixels[(1 * 6) + 4]);
        Assert.True(s.Pixels[(4 * 6) + 1]);
        Assert.True(s.Pixels[(4 * 6) + 4]);
        Assert.False(s.Pixels[(2 * 6) + 2]); // interior stays off
    }

    [Fact]
    public void DrawFilledRectangle_FillsInterior()
    {
        var svc = new DrawingService();
        var s = MakeState(6, 6);

        svc.DrawFilledRectangle(s, 1, 1, 3, 3, true);

        Assert.Equal(9, CountOn(s));
        Assert.True(s.Pixels[(2 * 6) + 2]);
    }

    [Fact]
    public void DrawFilledRectangle_ClippedSelection_RespectsClip()
    {
        var svc = new DrawingService();
        var s = MakeState(6, 6);
        var clip = new PixelClipStub { Selector = static (x, y) => x == 2 && y == 2 };

        svc.DrawFilledRectangle(s, 0, 0, 5, 5, true, clip: clip);

        Assert.Equal(1, CountOn(s));
        Assert.True(s.Pixels[(2 * 6) + 2]);
    }

    [Fact]
    public void DrawEllipse_DegenerateToPoint_SetsSinglePixel()
    {
        var svc = new DrawingService();
        var s = MakeState(8, 8);

        svc.DrawEllipse(s, 4, 4, 4, 4, true);

        Assert.True(s.Pixels[(4 * 8) + 4]);
        Assert.Equal(1, CountOn(s));
    }

    [Fact]
    public void DrawFilledEllipse_FillsMorePixelsThanOutlineEllipse()
    {
        var svc = new DrawingService();
        var outline = MakeState(12, 12);
        var filled = MakeState(12, 12);

        svc.DrawEllipse(outline, 2, 3, 9, 8, true);
        svc.DrawFilledEllipse(filled, 2, 3, 9, 8, true);

        Assert.True(CountOn(filled) > CountOn(outline));
    }

    [Fact]
    public void DrawFilledEllipse_FixedCrossoverPath_FillsCenter()
    {
        var svc = new DrawingService();
        var s = MakeState(10, 10);

        svc.DrawFilledEllipse(s, 1, 1, 8, 5, true);

        // Sanity around the old row crossover bug: center area should be filled.
        Assert.True(s.Pixels[(3 * 10) + 4]);
    }

    [Fact]
    public void ApplyFloodFill_FillsOnlyConnectedRegion()
    {
        // 5x5 with a vertical barrier at x=2 except gap at y=4.
        var s = MakeState(5, 5,
            (2, 0), (2, 1), (2, 2), (2, 3));
        var svc = new DrawingService();

        svc.ApplyFloodFill(s, 0, 0, true);

        // Left upper side becomes true.
        Assert.True(s.Pixels[(0 * 5) + 0]);
        Assert.True(s.Pixels[(3 * 5) + 1]);
        // Barrier cells remain true as originally (newState=true still true).
        Assert.True(s.Pixels[(1 * 5) + 2]);
    }

    [Fact]
    public void ApplyFloodFill_StartOutOfBounds_NoChange()
    {
        var s = MakeState(4, 4, (1, 1));
        bool[] before = (bool[])s.Pixels.Clone();
        var svc = new DrawingService();

        svc.ApplyFloodFill(s, -1, 0, true);

        Assert.Equal(before, s.Pixels);
    }

    [Fact]
    public void ApplyFloodFill_StartPixelAlreadyTarget_NoChange()
    {
        var s = MakeState(4, 4, (0, 0));
        bool[] before = (bool[])s.Pixels.Clone();
        var svc = new DrawingService();

        svc.ApplyFloodFill(s, 0, 0, true);

        Assert.Equal(before, s.Pixels);
    }

    [Fact]
    public void ApplyFloodFill_ClippedSelection_FillsOnlySelectedConnectedPixels()
    {
        var s = MakeState(6, 6);
        var svc = new DrawingService();
        var clip = new PixelClipStub
        {
            Selector = static (x, y) => x >= 1 && x <= 3 && y >= 1 && y <= 3
        };

        svc.ApplyFloodFill(s, 2, 2, true, true, clip);

        for (int y = 0; y < 6; y++)
        {
            for (int x = 0; x < 6; x++)
            {
                bool expected = x >= 1 && x <= 3 && y >= 1 && y <= 3;
                Assert.Equal(expected, s.Pixels[(y * 6) + x]);
            }
        }
    }

    [Fact]
    public void GetFloodFillMask_ReturnsCroppedMaskAndBounds()
    {
        var s = MakeState(6, 6, (3, 3), (3, 4), (4, 3), (4, 4));
        var svc = new DrawingService();

        bool[,] mask = svc.GetFloodFillMask(s, 0, 0, null, Hexprite.Core.FloatingPasteMode.Transparent, out int minX, out int minY, out int maxX, out int maxY);

        Assert.Equal(0, minX);
        Assert.Equal(0, minY);
        Assert.Equal(5, maxX);
        Assert.Equal(5, maxY);
        Assert.Equal(6, mask.GetLength(0));
        Assert.Equal(6, mask.GetLength(1));
        // start region is false-valued component, so obstacle points should be false in mask
        Assert.False(mask[3, 3]);
        Assert.True(mask[0, 0]);
    }

    [Fact]
    public void GetFloodFillMask_OutOfBoundsStart_ReturnsFullSizeFalseMask()
    {
        var s = MakeState(3, 2);
        var svc = new DrawingService();

        bool[,] mask = svc.GetFloodFillMask(s, 99, 99, null, Hexprite.Core.FloatingPasteMode.Transparent, out _, out _, out _, out _);

        Assert.Equal(3, mask.GetLength(0));
        Assert.Equal(2, mask.GetLength(1));
        Assert.False(mask[0, 0]);
        Assert.False(mask[2, 1]);
    }

    [Fact]
    public void GetFloodFillMask_OverflowStart_SelectsOffCanvasComponent()
    {
        var (state, _) = MakeOverflowState(4, 4, (-3, -2), (-2, -2));
        var svc = new DrawingService();

        bool[,] mask = svc.GetFloodFillMask(state, -3, -2, null, FloatingPasteMode.Transparent,
            out int minX, out int minY, out int maxX, out int maxY);

        Assert.Equal(-3, minX);
        Assert.Equal(-2, minY);
        Assert.Equal(-2, maxX);
        Assert.Equal(-2, maxY);
        Assert.Equal(2, mask.GetLength(0));
        Assert.Equal(1, mask.GetLength(1));
        Assert.True(mask[0, 0]);
        Assert.True(mask[1, 0]);
    }

    [Fact]
    public void GetFloodFillMask_OverflowComponentCrossesCanvasBoundary()
    {
        var (state, _) = MakeOverflowState(4, 4, (-1, 1), (0, 1), (1, 1));
        var svc = new DrawingService();

        bool[,] mask = svc.GetFloodFillMask(state, -1, 1, null, FloatingPasteMode.Transparent,
            out int minX, out int minY, out int maxX, out int maxY);

        Assert.Equal(-1, minX);
        Assert.Equal(1, minY);
        Assert.Equal(1, maxX);
        Assert.Equal(1, maxY);
        Assert.Equal(3, mask.GetLength(0));
        Assert.True(mask[0, 0]);
        Assert.True(mask[1, 0]);
        Assert.True(mask[2, 0]);
    }

    [Fact]
    public void ShiftGrid_PositiveOffset_WrapsAround()
    {
        var s = MakeState(4, 3, (0, 0), (3, 2));
        var svc = new DrawingService();

        svc.ShiftGrid(s, 1, 1);

        Assert.True(s.Pixels[(1 * 4) + 1]); // from (0,0)
        Assert.True(s.Pixels[(0 * 4) + 0]); // from (3,2) wraps
        Assert.Equal(2, CountOn(s));
    }

    [Fact]
    public void ShiftGrid_NegativeOffset_WrapsAround()
    {
        var s = MakeState(4, 3, (0, 0), (1, 1));
        var svc = new DrawingService();

        svc.ShiftGrid(s, -1, -1);

        Assert.True(s.Pixels[(2 * 4) + 3]); // (0,0) -> (3,2)
        Assert.True(s.Pixels[(0 * 4) + 0]); // (1,1) -> (0,0)
        Assert.Equal(2, CountOn(s));
    }

    [Fact]
    public void InvertGrid_TogglesAllPixels()
    {
        var s = MakeState(3, 2, (0, 0), (2, 1));
        var svc = new DrawingService();

        svc.InvertGrid(s);

        Assert.False(s.Pixels[(0 * 3) + 0]);
        Assert.False(s.Pixels[(1 * 3) + 2]);
        Assert.True(s.Pixels[(0 * 3) + 1]);
        Assert.True(s.Pixels[(1 * 3) + 0]);
        Assert.True(s.Pixels[(1 * 3) + 1]);
    }

    [Fact]
    public void RotatePixels_Clockwise90_On2x3_SwapsTo3x2()
    {
        var svc = new DrawingService();
        var src = new bool[6]; // 2×3
        src[(1 * 2) + 0] = true; // (0,1)

        var dst = svc.RotatePixels(src, 2, 3, RotationDirection.Clockwise90);

        Assert.Equal(6, dst.Length);
        // (0,1) in 2×3 → CW90 → destination (1,0) in 3×2 → flat index 1
        Assert.True(dst[(0 * 3) + 1]);
    }

    [Fact]
    public void RotatePixels_CounterClockwise_IsInverseOfClockwise90()
    {
        var svc = new DrawingService();
        var src = new bool[6];
        src[(0 * 2) + 0] = true;
        src[(2 * 2) + 1] = true;

        var cw = svc.RotatePixels(src, 2, 3, RotationDirection.Clockwise90);
        var back = svc.RotatePixels(cw, 3, 2, RotationDirection.CounterClockwise90);

        Assert.Equal(src, back);
    }

    [Fact]
    public void RotatePixels_OneEightyTwice_IsIdentity()
    {
        var svc = new DrawingService();
        var src = new bool[12];
        src[(0 * 4) + 1] = true;
        src[(2 * 4) + 3] = true;

        var once = svc.RotatePixels(src, 4, 3, RotationDirection.OneEighty);
        var twice = svc.RotatePixels(once, 4, 3, RotationDirection.OneEighty);

        Assert.Equal(src, twice);
    }

    [Fact]
    public void RotatePixels_TwoClockwise90_EqualsOneEighty()
    {
        var svc = new DrawingService();
        var src = new bool[12];
        for (int i = 0; i < 12; i++)
            src[i] = i % 3 == 0;

        var step1 = svc.RotatePixels(src, 4, 3, RotationDirection.Clockwise90);
        var step2 = svc.RotatePixels(step1, 3, 4, RotationDirection.Clockwise90);
        var direct = svc.RotatePixels(src, 4, 3, RotationDirection.OneEighty);

        Assert.Equal(direct, step2);
    }

    [Fact]
    public void FlipPixels_Horizontal_2x3_MirrorsX()
    {
        var svc = new DrawingService();
        int w = 2, h = 3;
        var src = new bool[w * h];
        src[(0 * w) + 0] = true; // (0,0)

        var dst = svc.FlipPixels(src, w, h, FlipDirection.Horizontal);

        for (int i = 0; i < dst.Length; i++)
        {
            if (i == 1) Assert.True(dst[i]); // (1,0)
            else Assert.False(dst[i]);
        }
    }

    [Fact]
    public void FlipPixels_Vertical_2x3_MirrorsY()
    {
        var svc = new DrawingService();
        int w = 2, h = 3;
        var src = new bool[w * h];
        src[(0 * w) + 0] = true; // (0,0)

        var dst = svc.FlipPixels(src, w, h, FlipDirection.Vertical);

        int expectedIndex = (h - 1) * w + 0; // (0,2) => 4
        for (int i = 0; i < dst.Length; i++)
        {
            if (i == expectedIndex) Assert.True(dst[i]);
            else Assert.False(dst[i]);
        }
    }

    [Fact]
    public void FlipPixels_DoubleHorizontal_IsIdentity()
    {
        var svc = new DrawingService();
        int w = 4, h = 3;
        var src = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                src[(y * w) + x] = (x + y) % 3 == 0;

        var once = svc.FlipPixels(src, w, h, FlipDirection.Horizontal);
        var twice = svc.FlipPixels(once, w, h, FlipDirection.Horizontal);

        Assert.Equal(src, twice);
    }

    [Fact]
    public void FlipPixels_DoubleVertical_IsIdentity()
    {
        var svc = new DrawingService();
        int w = 4, h = 3;
        var src = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                src[(y * w) + x] = (x * 2 + y) % 4 == 0;

        var once = svc.FlipPixels(src, w, h, FlipDirection.Vertical);
        var twice = svc.FlipPixels(once, w, h, FlipDirection.Vertical);

        Assert.Equal(src, twice);
    }

    [Fact]
    public void DrawDitherGradient_AppliesPatternNonDestructively()
    {
        var s = MakeState(16, 16);
        // Pre-fill some pixels to ensure they are not destroyed by false values
        s.Pixels[0] = true;
        s.Pixels[15] = true;

        var svc = new DrawingService();
        // Draw a gradient over the entire canvas. newState=true.
        svc.DrawDitherGradient(s, 0, 0, 16, 16, true, null);

        // Pre-existing pixels should still be true (they might be hit by the pattern, 
        // or they might be missed, but either way they shouldn't be set to false).
        // Actually, DrawDitherGradient only writes `newState` if `pixelState` is true.
        // It never writes `!newState` or `false`.
        Assert.True(s.Pixels[0]);
        Assert.True(s.Pixels[15]);

        // At least some pixels should be set to true by the dither pattern
        int trueCount = 0;
        foreach (var p in s.Pixels)
        {
            if (p) trueCount++;
        }
        Assert.True(trueCount > 2); // More than just the 2 we set
    }

    [Fact]
    public void DrawDitherGradient_EraserMode_AppliesFalseNonDestructively()
    {
        var s = MakeState(16, 16);
        // Pre-fill the whole canvas
        for (int i = 0; i < s.Pixels.Length; i++) s.Pixels[i] = true;

        var svc = new DrawingService();
        // Draw an eraser gradient over the entire canvas. newState=false.
        svc.DrawDitherGradient(s, 0, 0, 16, 16, false, null);

        // Some pixels should be erased
        int trueCount = 0;
        foreach (var p in s.Pixels)
        {
            if (p) trueCount++;
        }
        Assert.True(trueCount < 256); // Some pixels were erased (set to false)
        Assert.True(trueCount > 0);   // But not all of them (it's a dither)
    }
    [Fact]
    public void OutlineLayer_OutsideSquare_ProducesCorrectRing()
    {
        var svc = new DrawingService();
        var state = MakeState(10, 10, (5, 5));

        var settings = new OutlineSettings
        {
            Shape = OutlineShape.Square,
            Placement = OutlinePlacement.Outside,
            Padding = 1,
            Thickness = 1
        };

        svc.OutlineLayer(state, settings);

        // Center should still be true
        Assert.True(state.Pixels[5 * 10 + 5]);

        // With Padding 1 and Thickness 1, we expect a ring at Chebyshev distance 2
        // So pixels at (3,3)-(7,7) boundary should be true, but not inside (4,4)-(6,6) except the center

        Assert.True(state.Pixels[3 * 10 + 3]); // distance 2 corner
        Assert.True(state.Pixels[5 * 10 + 3]); // distance 2 edge
        Assert.False(state.Pixels[4 * 10 + 4]); // distance 1 corner (should be padding)
    }

    [Fact]
    public void OutlineLayer_OutsideCircle_ProducesCorrectRing()
    {
        var svc = new DrawingService();
        var state = MakeState(10, 10, (5, 5));

        var settings = new OutlineSettings
        {
            Shape = OutlineShape.Circle,
            Placement = OutlinePlacement.Outside,
            Padding = 1,
            Thickness = 2
        };

        svc.OutlineLayer(state, settings);

        Assert.True(state.Pixels[5 * 10 + 5]);

        // Padding 1 means distance > 1. Thickness 2 means dist <= 3.
        // Pixel at (2,5) -> dist 3 (Outline)
        Assert.True(state.Pixels[5 * 10 + 2]);

        // Pixel at (4,5) -> dist 1 (Padding, so empty)
        Assert.False(state.Pixels[5 * 10 + 4]);
    }

    [Fact]
    public void OutlineLayer_InsideSquare_SubtractsFromShape()
    {
        var svc = new DrawingService();
        // Create a 5x5 filled square in a 10x10 canvas
        var state = new SpriteState(10, 10);
        for(int y=2; y<=6; y++)
            for(int x=2; x<=6; x++)
                state.Pixels[y*10+x] = true;

        var settings = new OutlineSettings
        {
            Shape = OutlineShape.Square,
            Placement = OutlinePlacement.Inside,
            Padding = 0,
            Thickness = 1
        };

        svc.OutlineLayer(state, settings);

        // Padding 0, thickness 1 means the outermost pixel ring of the 5x5 square is subtracted.
        // Center (4,4) should remain true
        Assert.True(state.Pixels[4 * 10 + 4]);

        // Edges (2,2) should be false
        Assert.False(state.Pixels[2 * 10 + 2]);
        Assert.False(state.Pixels[6 * 10 + 6]);
    }

    [Fact]
    public void ShouldDitherPixel_Checkerboard_MatchesAlternatingGrid()
    {
        for (int y = -10; y <= 10; y++)
        {
            for (int x = -10; x <= 10; x++)
            {
                bool expected = Math.Abs((x + y) % 2) == 0;
                Assert.Equal(expected, DrawingService.ShouldDitherPixel(x, y, DitherPattern.Checkerboard));
            }
        }
    }

    [Fact]
    public void ShouldDitherPixel_LightAndDense_AreExactInverses()
    {
        for (int y = -8; y <= 8; y++)
        {
            for (int x = -8; x <= 8; x++)
            {
                bool light = DrawingService.ShouldDitherPixel(x, y, DitherPattern.Light);
                bool dense = DrawingService.ShouldDitherPixel(x, y, DitherPattern.Dense);
                
                if (x % 2 == 0 && y % 2 == 0)
                {
                    Assert.True(light);
                    Assert.True(dense);
                }
                else if (x % 2 != 0 && y % 2 != 0)
                {
                    Assert.False(light);
                    Assert.False(dense);
                }
            }
        }
    }

    [Fact]
    public void ShouldDitherPixel_DiagonalLines_TilesEveryFourPixels()
    {
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                bool expected = (x + y) % 4 < 2;
                Assert.Equal(expected, DrawingService.ShouldDitherPixel(x, y, DitherPattern.DiagonalLines));
            }
        }
    }

    [Fact]
    public void GetStampOffsetsArray_ReturnsCachedReference()
    {
        var offsets1 = DrawingService.GetStampOffsetsArray(32, BrushShape.Circle, 0);
        var offsets2 = DrawingService.GetStampOffsetsArray(32, BrushShape.Circle, 0);

        Assert.Same(offsets1, offsets2);
        Assert.NotEmpty(offsets1);
    }

    [Theory]
    [InlineData(DitherPattern.Checkerboard)]
    [InlineData(DitherPattern.Light)]
    [InlineData(DitherPattern.Dense)]
    [InlineData(DitherPattern.DiagonalLines)]
    [InlineData(DitherPattern.CrossHatch)]
    public void DrawLineDithered_LargeCanvasBigBrush_EverySetPixelMatchesPattern(DitherPattern pattern)
    {
        var svc = new DrawingService();
        var (state, buffer) = MakeOverflowState(128, 64);

        // Draw a long diagonal stroke across the canvas with a large brush (32px)
        svc.DrawLineDithered(state, 10, 10, 110, 50, true, 32, pattern, BrushShape.Circle, 0);

        int count = 0;
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 128; x++)
            {
                if (state.Pixels[y * 128 + x])
                {
                    count++;
                    Assert.True(DrawingService.ShouldDitherPixel(x, y, pattern),
                        $"Pixel at ({x}, {y}) was set but does not match pattern {pattern}");
                }
            }
        }

        Assert.True(count > 0, $"Expected some pixels to be set for pattern {pattern}");
    }
}
