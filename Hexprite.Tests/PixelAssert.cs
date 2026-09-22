using System;
using Hexprite.Core;
using Xunit;

namespace Hexprite.Tests
{
    /// <summary>
    /// Reusable domain-specific test assertion helpers for Hexprite.
    /// Provides clear, actionable error messages with exact coordinates on failure.
    /// </summary>
    public static class PixelAssert
    {
        /// <summary>
        /// Asserts that two 2D boolean pixel grids are identical in dimension and pixel values.
        /// </summary>
        public static void GridsEqual(bool[,] expected, bool[,] actual, string? message = null)
        {
            Assert.NotNull(expected);
            Assert.NotNull(actual);

            int expW = expected.GetLength(0);
            int expH = expected.GetLength(1);
            int actW = actual.GetLength(0);
            int actH = actual.GetLength(1);

            Assert.True(expW == actW && expH == actH,
                $"{message ?? "Grid dimension mismatch."} Expected: {expW}x{expH}, Actual: {actW}x{actH}");

            for (int y = 0; y < expH; y++)
            {
                for (int x = 0; x < expW; x++)
                {
                    if (expected[x, y] != actual[x, y])
                    {
                        Assert.Fail(
                            $"{message ?? "Pixel mismatch"} at coordinate (X: {x}, Y: {y}) - " +
                            $"Expected: {expected[x, y]}, Actual: {actual[x, y]} (Canvas size: {expW}x{expH})");
                    }
                }
            }
        }

        /// <summary>
        /// Asserts that two 1D flat pixel buffers are identical.
        /// </summary>
        public static void BuffersEqual(bool[] expected, bool[] actual, int width, int height, string? message = null)
        {
            Assert.NotNull(expected);
            Assert.NotNull(actual);
            Assert.Equal(expected.Length, actual.Length);

            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i] != actual[i])
                {
                    int x = width > 0 ? i % width : i;
                    int y = width > 0 ? i / width : 0;
                    Assert.Fail(
                        $"{message ?? "Buffer mismatch"} at index {i} (X: {x}, Y: {y}) - " +
                        $"Expected: {expected[i]}, Actual: {actual[i]}");
                }
            }
        }

        /// <summary>
        /// Asserts that a SpriteState has the expected canvas dimensions.
        /// </summary>
        public static void CanvasDimensions(SpriteState state, int expectedWidth, int expectedHeight)
        {
            Assert.NotNull(state);
            Assert.True(state.Width == expectedWidth && state.Height == expectedHeight,
                $"Canvas dimension mismatch. Expected: {expectedWidth}x{expectedHeight}, Actual: {state.Width}x{state.Height}");
            Assert.Equal(expectedWidth * expectedHeight, state.Pixels.Length);
        }

        /// <summary>
        /// Asserts that a SelectionSnapshot matches the expected Min/Max bounding coordinates.
        /// </summary>
        public static void SelectionBounds(SelectionSnapshot snapshot, int expectedMinX, int expectedMinY, int expectedMaxX, int expectedMaxY)
        {
            Assert.NotNull(snapshot);
            Assert.True(snapshot.HasActiveSelection, "Expected active selection, but HasActiveSelection is false.");
            Assert.Equal(expectedMinX, snapshot.MinX);
            Assert.Equal(expectedMinY, snapshot.MinY);
            Assert.Equal(expectedMaxX, snapshot.MaxX);
            Assert.Equal(expectedMaxY, snapshot.MaxY);
        }

        /// <summary>
        /// Asserts that a SelectionSnapshot floating selection matches expected position and size.
        /// </summary>
        public static void FloatingBounds(SelectionSnapshot snapshot, int expectedX, int expectedY, int expectedW, int expectedH)
        {
            Assert.NotNull(snapshot);
            Assert.True(snapshot.IsFloating, "Expected floating selection, but IsFloating is false.");
            Assert.Equal(expectedX, snapshot.FloatingX);
            Assert.Equal(expectedY, snapshot.FloatingY);
            Assert.Equal(expectedW, snapshot.FloatingWidth);
            Assert.Equal(expectedH, snapshot.FloatingHeight);
        }
    }
}
