using System;
using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class SelectionTransformationTests
    {
        private static bool[,] CreateGrid(int w, int h, params (int x, int y)[] onPixels)
        {
            var grid = new bool[w, h];
            foreach (var (x, y) in onPixels)
                grid[x, y] = true;
            return grid;
        }

        private static SelectionService CreateFloatingService(int w, int h, params (int x, int y)[] onPixels)
        {
            var svc = new SelectionService();
            var grid = CreateGrid(w, h, onPixels);
            svc.PasteAsFloating(new PixelClipboardData(grid, w, h), w * 2, h * 2);
            svc.MoveFloatingTo(0, 0);
            return svc;
        }

        [Fact]
        public void FlipFloatingHorizontally_FlipsPixelsAndMask()
        {
            // Two pixels at top-left: (0,0) and (1,0) in a 4x4 grid
            var svc = CreateFloatingService(4, 4, (0, 0), (1, 0));

            Assert.True(svc.FloatingPixels![0, 0]);
            Assert.True(svc.FloatingPixels[1, 0]);
            Assert.False(svc.FloatingPixels[3, 0]);

            svc.FlipFloatingHorizontally();

            // After horizontal flip across width 4: (0,0) -> (3,0), (1,0) -> (2,0)
            Assert.False(svc.FloatingPixels[0, 0]);
            Assert.True(svc.FloatingPixels[3, 0]);
            Assert.True(svc.FloatingPixels[2, 0]);
        }

        [Fact]
        public void FlipFloatingVertically_FlipsPixelsAndMask()
        {
            // Top row: (0,0) and (1,0) in a 4x4 grid
            var svc = CreateFloatingService(4, 4, (0, 0), (1, 0));

            Assert.True(svc.FloatingPixels![0, 0]);
            Assert.True(svc.FloatingPixels[1, 0]);
            Assert.False(svc.FloatingPixels[0, 3]);

            svc.FlipFloatingVertically();

            // After vertical flip across height 4: (0,0) -> (0,3), (1,0) -> (1,3)
            Assert.False(svc.FloatingPixels[0, 0]);
            Assert.False(svc.FloatingPixels[1, 0]);
            Assert.True(svc.FloatingPixels[0, 3]);
            Assert.True(svc.FloatingPixels[1, 3]);
        }

        [Fact]
        public void Transform_ScaleNx_IntegerUpscaling()
        {
            var svc = CreateFloatingService(2, 2, (0, 0));

            svc.BeginTransform(TransformHandle.SE);
            // 2x upscale (from 2x2 to 4x4)
            svc.UpdateTransform(0, 0, 4, 4);

            Assert.Equal(4, svc.FloatingWidth);
            Assert.Equal(4, svc.FloatingHeight);
            Assert.NotNull(svc.FloatingPixels);

            svc.CommitTransform();
            Assert.False(svc.IsTransforming);
        }

        [Fact]
        public void Transform_Scale3x_Upscaling()
        {
            var svc = CreateFloatingService(2, 2, (0, 0));

            svc.BeginTransform(TransformHandle.SE);
            // 3x upscale (from 2x2 to 6x6)
            svc.UpdateTransform(0, 0, 6, 6);

            Assert.Equal(6, svc.FloatingWidth);
            Assert.Equal(6, svc.FloatingHeight);

            svc.CommitTransform();
        }

        [Fact]
        public void Transform_NonIntegerResampling_ExecutesEdgeAwareSampling()
        {
            var svc = CreateFloatingService(3, 3, (0, 0), (1, 1), (2, 2));

            svc.BeginTransform(TransformHandle.SE);
            // Non-integer scale: from 3x3 to 7x7
            svc.UpdateTransform(0, 0, 7, 7);

            Assert.Equal(7, svc.FloatingWidth);
            Assert.Equal(7, svc.FloatingHeight);

            svc.CommitTransform();
            Assert.NotNull(svc.FloatingPixels);
        }

        [Fact]
        public void Transform_Cancel_RestoresOriginalPixelsAndBounds()
        {
            var svc = CreateFloatingService(4, 4, (0, 0), (1, 1));

            svc.BeginTransform(TransformHandle.SE);
            svc.UpdateTransform(0, 0, 8, 8);
            svc.UpdateRotation(45);

            Assert.Equal(8, svc.FloatingWidth);
            Assert.Equal(45, svc.RotationAngle);

            svc.CancelTransform();

            Assert.False(svc.IsTransforming);
            Assert.Equal(4, svc.FloatingWidth);
            Assert.Equal(4, svc.FloatingHeight);
            Assert.Equal(0, svc.RotationAngle);
            Assert.True(svc.FloatingPixels![0, 0]);
            Assert.True(svc.FloatingPixels[1, 1]);
        }

        [Fact]
        public void Rotation_GetEffectiveFloating_AppliesRotation2D()
        {
            var svc = CreateFloatingService(4, 4, (0, 0), (1, 1));

            svc.BeginTransform(TransformHandle.Rotate);
            svc.UpdateRotation(90.0);

            var (pixels, mask, x, y, w, h) = svc.GetEffectiveFloating();

            Assert.NotNull(pixels);
            Assert.True(w > 0);
            Assert.True(h > 0);

            svc.CommitTransform();
            Assert.Equal(90.0, svc.RotationAngle);
            Assert.False(svc.IsTransforming);
        }

        [Fact]
        public void IsPointInSelectionBounds_ReturnsAccurateHitTest()
        {
            var svc = CreateFloatingService(4, 4, (2, 2));
            svc.MoveFloatingTo(2, 2);

            Assert.True(svc.IsPointInSelectionBounds(2, 2));
            Assert.True(svc.IsPointInSelectionBounds(5, 5));
            Assert.False(svc.IsPointInSelectionBounds(1, 2));
            Assert.False(svc.IsPointInSelectionBounds(7, 7));
        }
    }
}
