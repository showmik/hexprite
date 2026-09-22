using Hexprite.Controllers;
using Hexprite.Core;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class SelectionInputControllerTests
    {
        [Fact]
        public void ComputeResizeRect_SoutheastHandle_ExpandsWidthAndHeight()
        {
            // Original rect: (10, 10, 20, 20). Dragging SE by (+5, +10)
            var (x, y, w, h, flipX, flipY) = SelectionInputController.ComputeResizeRect(
                TransformHandle.SE,
                ox: 10, oy: 10, ow: 20, oh: 20,
                dx: 5, dy: 10,
                shiftAspect: false, altFromCenter: false);

            Assert.Equal(10, x);
            Assert.Equal(10, y);
            Assert.Equal(25, w);
            Assert.Equal(30, h);
            Assert.False(flipX);
            Assert.False(flipY);
        }

        [Fact]
        public void ComputeResizeRect_NorthwestHandle_ModifiesOrigin()
        {
            // Original rect: (10, 10, 20, 20). Dragging NW by (+5, +5) (shrinks from top-left)
            var (x, y, w, h, flipX, flipY) = SelectionInputController.ComputeResizeRect(
                TransformHandle.NW,
                ox: 10, oy: 10, ow: 20, oh: 20,
                dx: 5, dy: 5,
                shiftAspect: false, altFromCenter: false);

            Assert.Equal(15, x);
            Assert.Equal(15, y);
            Assert.Equal(15, w);
            Assert.Equal(15, h);
            Assert.False(flipX);
            Assert.False(flipY);
        }

        [Fact]
        public void ComputeResizeRect_AltFromCenter_ScalesSymmetrically()
        {
            // Original rect: (10, 10, 20, 20). Dragging East handle by +5 from center
            var (x, y, w, h, flipX, flipY) = SelectionInputController.ComputeResizeRect(
                TransformHandle.E,
                ox: 10, oy: 10, ow: 20, oh: 20,
                dx: 5, dy: 0,
                shiftAspect: false, altFromCenter: true);

            // Left should decrease by 5, right increases by 5 -> width becomes 30, x becomes 5
            Assert.Equal(5, x);
            Assert.Equal(10, y);
            Assert.Equal(30, w);
            Assert.Equal(20, h);
            Assert.False(flipX);
            Assert.False(flipY);
        }

        [Fact]
        public void ComputeResizeRect_ShiftConstrainedAspect_MaintainsAspectRatio()
        {
            // Original rect: (0, 0, 10, 20) (1:2 aspect ratio). Dragging SE by (+10, +5)
            var (x, y, w, h, flipX, flipY) = SelectionInputController.ComputeResizeRect(
                TransformHandle.SE,
                ox: 0, oy: 0, ow: 10, oh: 20,
                dx: 10, dy: 5, // width increases by 10 (scale = 20/10 = 2x)
                shiftAspect: true, altFromCenter: false);

            Assert.Equal(0, x);
            Assert.Equal(0, y);
            Assert.Equal(20, w); // 10 * 2 = 20
            Assert.Equal(40, h); // 20 * 2 = 40 (aspect preserved!)
            Assert.False(flipX);
            Assert.False(flipY);
        }

        [Fact]
        public void ComputeResizeRect_SideHandleWithShiftAspect_ScalesCorrectDimension()
        {
            // Original rect: (0, 0, 10, 20). Dragging North handle by -10 (increasing height by 10 -> h=30, scale = 1.5)
            var (x, y, w, h, flipX, flipY) = SelectionInputController.ComputeResizeRect(
                TransformHandle.N,
                ox: 0, oy: 0, ow: 10, oh: 20,
                dx: 0, dy: -10,
                shiftAspect: true, altFromCenter: false);

            Assert.Equal(30, h);
            Assert.Equal(15, w); // 10 * 1.5 = 15
            Assert.False(flipX);
            Assert.False(flipY);
        }

        [Fact]
        public void ComputeResizeRect_DragPastOppositeEdge_FlagsFlips()
        {
            // Original rect: (10, 10, 20, 20). Dragging West handle to the right past the right edge (+30)
            var (x, y, w, h, flipX, flipY) = SelectionInputController.ComputeResizeRect(
                TransformHandle.W,
                ox: 10, oy: 10, ow: 20, oh: 20,
                dx: 30, dy: 0,
                shiftAspect: false, altFromCenter: false);

            Assert.True(flipX);
            Assert.False(flipY);
            Assert.True(w >= 1);
        }
    }
}
