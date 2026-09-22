using Hexprite.Controllers;
using Hexprite.Core;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
    public class SelectionInputControllerResizeTests
{
    [Fact]
    public void BuildInverseMask_UsesNegativeOverflowDomainCoordinates()
    {
        var fillMask = new bool[2, 1];
        fillMask[0, 0] = true;
        fillMask[1, 0] = true;

        bool[,] inverse = SelectionInputController.BuildInverseMask(
            fillMask,
            fillMinX: -2,
            fillMinY: 0,
            domainMinX: -3,
            domainMinY: -1,
            domainWidth: 6,
            domainHeight: 3);

        Assert.Equal(6, inverse.GetLength(0));
        Assert.Equal(3, inverse.GetLength(1));
        Assert.False(inverse[1, 1]); // global (-2, 0)
        Assert.False(inverse[2, 1]); // global (-1, 0)
        Assert.True(inverse[0, 1]);  // global (-3, 0)
        Assert.True(inverse[3, 1]);  // global (0, 0)
    }

    [Fact]
    public void ComputeResizeRect_EastPastWest_FlipsX_AndNormalizesRect()
    {
        var (x, y, w, h, flipX, flipY) = SelectionInputController.ComputeResizeRect(
            TransformHandle.E,
            ox: 10, oy: 20, ow: 5, oh: 3,
            dx: -10, dy: 0,
            shiftAspect: false, altFromCenter: false);

        Assert.Equal(4, x);
        Assert.Equal(20, y);
        Assert.Equal(7, w);
        Assert.Equal(3, h);
        Assert.True(flipX);
        Assert.False(flipY);
    }

    [Fact]
    public void ComputeResizeRect_SEPastNW_FlipsBothAxes()
    {
        var (x, y, w, h, flipX, flipY) = SelectionInputController.ComputeResizeRect(
            TransformHandle.SE,
            ox: 0, oy: 0, ow: 4, oh: 3,
            dx: -10, dy: -10,
            shiftAspect: false, altFromCenter: false);

        Assert.Equal(-7, x);
        Assert.Equal(-8, y);
        Assert.Equal(8, w);
        Assert.Equal(9, h);
        Assert.True(flipX);
        Assert.True(flipY);
    }

    [Fact]
    public void ComputeResizeRect_AltFromCenter_CornerCrossesCenter_FlipsAndKeepsCenterFixed()
    {
        // Original box: x=0..3 (w=4) => center at 2.0 with this code's convention (ox + ow/2).
        var (x, y, w, h, flipX, flipY) = SelectionInputController.ComputeResizeRect(
            TransformHandle.SE,
            ox: 0, oy: 0, ow: 4, oh: 4,
            dx: -3, dy: -3,
            shiftAspect: false, altFromCenter: true);

        Assert.Equal(0, x);
        Assert.Equal(0, y);
        Assert.Equal(4, w);
        Assert.Equal(4, h);
        Assert.True(flipX);
        Assert.True(flipY);
    }

    [Fact]
    public void ComputeResizeRect_ShiftAspect_EastPastWest_PreservesAspect_AndKeepsFixedEdge()
    {
        // Original aspect: 4x2. Drag E far past W so flipX becomes true; Shift should scale both axes.
        var (x, y, w, h, flipX, flipY) = SelectionInputController.ComputeResizeRect(
            TransformHandle.E,
            ox: 0, oy: 0, ow: 4, oh: 2,
            dx: -10, dy: 0,
            shiftAspect: true, altFromCenter: false);

        Assert.Equal(-7, x);
        Assert.Equal(-1, y);
        Assert.Equal(8, w);
        Assert.Equal(4, h);
        Assert.True(flipX);
        Assert.False(flipY);
    }
}

