using System;
using Hexprite.Controllers;
using Hexprite.Core;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class ToolInputControllerTests
    {
        [Fact]
        public void GetConstrainedShapeBounds_Rectangle_NoModifiers_ReturnsExactBounds()
        {
            var (x0, y0, x1, y1) = ToolInputController.GetConstrainedShapeBounds(
                startX: 10, startY: 10, currentX: 30, currentY: 20,
                tool: ToolMode.Rectangle, isShift: false, isAlt: false);

            Assert.Equal(10, x0);
            Assert.Equal(10, y0);
            Assert.Equal(30, x1);
            Assert.Equal(20, y1);
        }

        [Fact]
        public void GetConstrainedShapeBounds_Rectangle_ShiftConstrainsSquare()
        {
            // dx = 20, dy = 10 -> side = 20 -> square 10..30 x 10..30
            var (x0, y0, x1, y1) = ToolInputController.GetConstrainedShapeBounds(
                startX: 10, startY: 10, currentX: 30, currentY: 20,
                tool: ToolMode.Rectangle, isShift: true, isAlt: false);

            Assert.Equal(10, x0);
            Assert.Equal(10, y0);
            Assert.Equal(30, x1);
            Assert.Equal(30, y1);
        }

        [Fact]
        public void GetConstrainedShapeBounds_Rectangle_AltDrawsFromCenter()
        {
            // Center is (10, 10), dragged to (15, 20) -> x0 = 2*10 - 15 = 5, y0 = 2*10 - 20 = 0
            var (x0, y0, x1, y1) = ToolInputController.GetConstrainedShapeBounds(
                startX: 10, startY: 10, currentX: 15, currentY: 20,
                tool: ToolMode.Rectangle, isShift: false, isAlt: true);

            Assert.Equal(5, x0);
            Assert.Equal(0, y0);
            Assert.Equal(15, x1);
            Assert.Equal(20, y1);
        }

        [Fact]
        public void GetConstrainedShapeBounds_Line_ShiftSnapsAngle()
        {
            // Angle near 45 degrees
            var (x0, y0, x1, y1) = ToolInputController.GetConstrainedShapeBounds(
                startX: 0, startY: 0, currentX: 10, currentY: 10,
                tool: ToolMode.Line, isShift: true, isAlt: false);

            Assert.Equal(0, x0);
            Assert.Equal(0, y0);
            Assert.Equal(x1, y1); // 45 deg line has x1 == y1
        }

        [Fact]
        public void GetConstrainedShapeBounds_Line_AltDrawsFromCenter()
        {
            var (x0, y0, x1, y1) = ToolInputController.GetConstrainedShapeBounds(
                startX: 10, startY: 10, currentX: 20, currentY: 15,
                tool: ToolMode.Line, isShift: false, isAlt: true);

            Assert.Equal(0, x0);  // 2 * 10 - 20 = 0
            Assert.Equal(5, y0);  // 2 * 10 - 15 = 5
            Assert.Equal(20, x1);
            Assert.Equal(15, y1);
        }

        [Fact]
        public void IsSymmetricIntersection_PrimaryPointWithinTolerance_ReturnsTrue()
        {
            bool hit = ToolInputController.IsSymmetricIntersection(
                px: 10, py: 10, bx: 11, by: 10, maxDx: 1, maxDy: 1,
                horz: false, vert: false, axisX: 0, axisY: 0);

            Assert.True(hit);
        }

        [Fact]
        public void IsSymmetricIntersection_VerticalSymmetryAxis_DetectsMirroredHit()
        {
            // Axis at X = 8 (canvas width 16: center axis = 8.0)
            // px = 2 -> mirroredX = 2 * 8 - 2 - 1 = 13. bx = 13
            bool hit = ToolInputController.IsSymmetricIntersection(
                px: 2, py: 5, bx: 13, by: 5, maxDx: 1, maxDy: 1,
                horz: false, vert: true, axisX: 8.0, axisY: 8.0);

            Assert.True(hit);
        }

        [Fact]
        public void IsSymmetricIntersection_HorizontalSymmetryAxis_DetectsMirroredHit()
        {
            // Axis at Y = 8.0
            // py = 3 -> mirroredY = 2 * 8 - 3 - 1 = 12. by = 12
            bool hit = ToolInputController.IsSymmetricIntersection(
                px: 5, py: 3, bx: 5, by: 12, maxDx: 1, maxDy: 1,
                horz: true, vert: false, axisX: 8.0, axisY: 8.0);

            Assert.True(hit);
        }

        [Fact]
        public void IsSymmetricIntersection_FarPoint_ReturnsFalse()
        {
            bool hit = ToolInputController.IsSymmetricIntersection(
                px: 2, py: 2, bx: 50, by: 50, maxDx: 1, maxDy: 1,
                horz: true, vert: true, axisX: 8.0, axisY: 8.0);

            Assert.False(hit);
        }
    }
}
