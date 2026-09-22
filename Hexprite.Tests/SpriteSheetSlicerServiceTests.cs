using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class SpriteSheetSlicerServiceTests
    {
        private readonly SpriteSheetSlicerService _slicer = new();

        private static BitmapSource CreateTestBitmap(int width, int height)
        {
            var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            uint[] pixels = new uint[width * height];
            Array.Fill(pixels, 0xFFFFFFFF);
            bmp.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            return bmp;
        }

        [Fact]
        public void SliceToAnimationSprite_HorizontalStrip_CreatesExpectedFrames()
        {
            // 256x32 strip -> 8 frames of 32x32
            var source = CreateTestBitmap(256, 32);
            var settings = new SpriteSheetSliceSettings
            {
                Layout = SpriteSheetLayout.HorizontalStrip,
                FrameWidth = 32,
                FrameHeight = 32,
                CanvasMode = SliceCanvasMode.FitFrame
            };

            var sprite = _slicer.SliceToAnimationSprite(source, settings);

            Assert.NotNull(sprite);
            Assert.Equal(8, sprite.Frames.Count);
            Assert.Equal(32, sprite.Width);
            Assert.Equal(32, sprite.Height);
            Assert.True(sprite.IsAnimationEnabled);
        }

        [Fact]
        public void SliceToAnimationSprite_VerticalStrip_CreatesExpectedFrames()
        {
            // 16x96 strip -> 6 frames of 16x16
            var source = CreateTestBitmap(16, 96);
            var settings = new SpriteSheetSliceSettings
            {
                Layout = SpriteSheetLayout.VerticalStrip,
                FrameWidth = 16,
                FrameHeight = 16,
                CanvasMode = SliceCanvasMode.FitFrame
            };

            var sprite = _slicer.SliceToAnimationSprite(source, settings);

            Assert.NotNull(sprite);
            Assert.Equal(6, sprite.Frames.Count);
            Assert.Equal(16, sprite.Width);
            Assert.Equal(16, sprite.Height);
        }

        [Fact]
        public void SliceToAnimationSprite_GridAtlas_CreatesExpectedFrames()
        {
            // 128x128 atlas (4 cols x 4 rows) -> 16 frames of 32x32
            var source = CreateTestBitmap(128, 128);
            var settings = new SpriteSheetSliceSettings
            {
                Layout = SpriteSheetLayout.Grid,
                FrameWidth = 32,
                FrameHeight = 32,
                Columns = 4,
                Rows = 4,
                CanvasMode = SliceCanvasMode.FitFrame
            };

            var sprite = _slicer.SliceToAnimationSprite(source, settings);

            Assert.NotNull(sprite);
            Assert.Equal(16, sprite.Frames.Count);
            Assert.Equal(32, sprite.Width);
            Assert.Equal(32, sprite.Height);
        }

        [Fact]
        public void SliceToAnimationSprite_WithOffsetsAndSpacing_CalculatesCorrectRects()
        {
            // 100x100 source: OffsetX=10, OffsetY=10, SpacingX=5, SpacingY=5, FrameW=20, FrameH=20
            var source = CreateTestBitmap(100, 100);
            var settings = new SpriteSheetSliceSettings
            {
                Layout = SpriteSheetLayout.Grid,
                FrameWidth = 20,
                FrameHeight = 20,
                OffsetX = 10,
                OffsetY = 10,
                SpacingX = 5,
                SpacingY = 5,
                CanvasMode = SliceCanvasMode.FitFrame
            };

            var rects = _slicer.CalculateSliceRects(source, settings);

            Assert.Equal(9, rects.Count);
            Assert.Equal(10, rects[0].X);
            Assert.Equal(10, rects[0].Y);
            Assert.Equal(35, rects[1].X);
            Assert.Equal(10, rects[1].Y);
            Assert.Equal(60, rects[2].X);
            Assert.Equal(10, rects[2].Y);
            Assert.Equal(10, rects[3].X);
            Assert.Equal(35, rects[3].Y);
        }

        [Fact]
        public void SliceToAnimationSprite_FixedCanvasMode_PadsToTargetCanvas()
        {
            // 32x32 frame placed into 128x64 target canvas
            var source = CreateTestBitmap(64, 32);
            var settings = new SpriteSheetSliceSettings
            {
                Layout = SpriteSheetLayout.HorizontalStrip,
                FrameWidth = 32,
                FrameHeight = 32,
                CanvasMode = SliceCanvasMode.FixedCanvas,
                CanvasWidth = 128,
                CanvasHeight = 64,
                Alignment = SliceCanvasAlignment.Center
            };

            var sprite = _slicer.SliceToAnimationSprite(source, settings);

            Assert.Equal(2, sprite.Frames.Count);
            Assert.Equal(128, sprite.Width);
            Assert.Equal(64, sprite.Height);
        }

        [Fact]
        public void SliceToAnimationSprite_SlicesLargerThanTargetCanvas_CentersSymmetrically()
        {
            // 160x160 source image with single 160x160 frame into 128x64 target canvas with Center alignment
            var source = CreateTestBitmap(160, 160);
            var settings = new SpriteSheetSliceSettings
            {
                Layout = SpriteSheetLayout.HorizontalStrip,
                FrameWidth = 160,
                FrameHeight = 160,
                CanvasMode = SliceCanvasMode.FixedCanvas,
                CanvasWidth = 128,
                CanvasHeight = 64,
                Alignment = SliceCanvasAlignment.Center
            };

            var sprite = _slicer.SliceToAnimationSprite(source, settings);

            Assert.Single(sprite.Frames);
            Assert.Equal(128, sprite.Width);
            Assert.Equal(64, sprite.Height);
        }

        [Fact]
        public void SliceToAnimationSprite_Custom128x64FramesOn256Atlas_DoesNotCollide()
        {
            // 256x256 atlas with 128x64 frames -> 2 cols x 4 rows = 8 frames
            var source = CreateTestBitmap(256, 256);
            var settings = new SpriteSheetSliceSettings
            {
                Layout = SpriteSheetLayout.Grid,
                FrameWidth = 128,
                FrameHeight = 64,
                Columns = 2,
                Rows = 4,
                CanvasMode = SliceCanvasMode.FitFrame
            };

            var rects = _slicer.CalculateSliceRects(source, settings);

            Assert.Equal(8, rects.Count);
            Assert.Equal(128, rects[0].Width);
            Assert.Equal(64, rects[0].Height);
        }

        [Fact]
        public void SliceToAnimationSprite_ColumnMajorOrder_IteratesVerticalFirst()
        {
            // 64x64 grid (2x2) of 32x32
            var source = CreateTestBitmap(64, 64);
            var settings = new SpriteSheetSliceSettings
            {
                Layout = SpriteSheetLayout.Grid,
                FrameWidth = 32,
                FrameHeight = 32,
                Columns = 2,
                Rows = 2,
                Order = SliceOrder.ColumnMajor
            };

            var rects = _slicer.CalculateSliceRects(source, settings);

            Assert.Equal(4, rects.Count);
            // Column 0
            Assert.Equal(0, rects[0].X); Assert.Equal(0, rects[0].Y);
            Assert.Equal(0, rects[1].X); Assert.Equal(32, rects[1].Y);
            // Column 1
            Assert.Equal(32, rects[2].X); Assert.Equal(0, rects[2].Y);
            Assert.Equal(32, rects[3].X); Assert.Equal(32, rects[3].Y);
        }

        [Fact]
        public void SliceToIndividualSprites_ReturnsSingleFrameSprites()
        {
            var source = CreateTestBitmap(128, 32);
            var settings = new SpriteSheetSliceSettings
            {
                Layout = SpriteSheetLayout.HorizontalStrip,
                FrameWidth = 32,
                FrameHeight = 32,
                CanvasMode = SliceCanvasMode.FitFrame
            };

            var individual = _slicer.SliceToIndividualSprites(source, settings);

            Assert.Equal(4, individual.Count);
            Assert.All(individual, s =>
            {
                Assert.False(s.IsAnimationEnabled);
                Assert.Single(s.Frames);
                Assert.Equal(32, s.Width);
                Assert.Equal(32, s.Height);
            });
        }

        [Theory]
        [InlineData(512, 64, 64, 64, 8, 1)]
        [InlineData(256, 256, 128, 128, 2, 2)]
        [InlineData(128, 64, 64, 64, 2, 1)]
        public void DetectGrid_ReturnsSensibleGridGeometry(int imgW, int imgH, int expW, int expH, int expCols, int expRows)
        {
            var source = CreateTestBitmap(imgW, imgH);
            var (fw, fh, cols, rows) = _slicer.DetectGrid(source);

            Assert.Equal(expW, fw);
            Assert.Equal(expH, fh);
            Assert.Equal(expCols, cols);
            Assert.Equal(expRows, rows);
        }

        [Fact]
        public void DetectAlphaIslands_FindsContiguousRegions()
        {
            // 64x64 transparent image with two distinct 10x10 white squares at (5,5) and (30,30)
            var source = CreateAlphaTestBitmap(64, 64, pixels =>
            {
                // Square 1: (5,5) to (14,14) -> 10x10
                for (int y = 5; y < 15; y++)
                    for (int x = 5; x < 15; x++)
                        pixels[y * 64 + x] = 0xFFFFFFFF;

                // Square 2: (30,30) to (39,39) -> 10x10
                for (int y = 30; y < 40; y++)
                    for (int x = 30; x < 40; x++)
                        pixels[y * 64 + x] = 0xFFFFFFFF;
            });

            var islands = _slicer.DetectAlphaIslands(source, alphaThreshold: 10, minIslandWidth: 2, minIslandHeight: 2);

            Assert.Equal(2, islands.Count);
            Assert.Equal(5, islands[0].X);
            Assert.Equal(5, islands[0].Y);
            Assert.Equal(10, islands[0].Width);
            Assert.Equal(10, islands[0].Height);

            Assert.Equal(30, islands[1].X);
            Assert.Equal(30, islands[1].Y);
            Assert.Equal(10, islands[1].Width);
            Assert.Equal(10, islands[1].Height);
        }

        private static BitmapSource CreateAlphaTestBitmap(int width, int height, Action<uint[]> draw)
        {
            var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            uint[] pixels = new uint[width * height];
            draw(pixels);
            bmp.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            return bmp;
        }

        [Fact]
        public void PruneEmptyRects_RemovesFullyTransparentRegions()
        {
            // 64x32: left 32x32 has content, right 32x32 is transparent
            var source = CreateAlphaTestBitmap(64, 32, pixels =>
            {
                for (int y = 0; y < 32; y++)
                    for (int x = 0; x < 32; x++)
                        pixels[y * 64 + x] = 0xFFFFFFFF;
            });

            var rects = new[]
            {
                new Int32Rect(0, 0, 32, 32),
                new Int32Rect(32, 0, 32, 32)
            };

            var pruned = _slicer.PruneEmptyRects(source, rects, alphaThreshold: 0);

            Assert.Single(pruned);
            Assert.Equal(0, pruned[0].X);
            Assert.Equal(0, pruned[0].Y);
        }

        [Fact]
        public void ComputeFrameSizeFromCount_CalculatesCorrectDimensions()
        {
            // 100x100 image, 2 cols x 2 rows, offset 0, spacing 10 -> (100 - 10)/2 = 45
            var (fw, fh) = _slicer.ComputeFrameSizeFromCount(100, 100, columns: 2, rows: 2, offsetX: 0, offsetY: 0, spacingX: 10, spacingY: 10);

            Assert.Equal(45, fw);
            Assert.Equal(45, fh);
        }

        [Fact]
        public void CalculateSliceRects_AutoDetectLayout_ReturnsIslands()
        {
            var source = CreateAlphaTestBitmap(64, 64, pixels =>
            {
                for (int y = 2; y < 10; y++)
                    for (int x = 2; x < 10; x++)
                        pixels[y * 64 + x] = 0xFFFFFFFF;
            });

            var settings = new SpriteSheetSliceSettings
            {
                Layout = SpriteSheetLayout.AutoDetect,
                AlphaThreshold = 10
            };

            var rects = _slicer.CalculateSliceRects(source, settings);

            Assert.Single(rects);
            Assert.Equal(2, rects[0].X);
            Assert.Equal(2, rects[0].Y);
            Assert.Equal(8, rects[0].Width);
            Assert.Equal(8, rects[0].Height);
        }

        [Fact]
        public void CalculateSliceRects_SkipEmptyFrames_FiltersEmptyCells()
        {
            var source = CreateAlphaTestBitmap(64, 32, pixels =>
            {
                for (int y = 0; y < 32; y++)
                    for (int x = 0; x < 32; x++)
                        pixels[y * 64 + x] = 0xFFFFFFFF;
            });

            var settings = new SpriteSheetSliceSettings
            {
                Layout = SpriteSheetLayout.HorizontalStrip,
                FrameWidth = 32,
                FrameHeight = 32,
                SkipEmptyFrames = true
            };

            var rects = _slicer.CalculateSliceRects(source, settings);

            Assert.Single(rects);
            Assert.Equal(0, rects[0].X);
        }

        [Fact]
        public void SliceToAnimationSprite_VariableIslandSizes_SetsCanvasToMaxBoundingBox()
        {
            // Island 1: 10x20 at (0,0), Island 2: 25x15 at (30,0)
            var source = CreateAlphaTestBitmap(64, 64, pixels =>
            {
                for (int y = 0; y < 20; y++)
                    for (int x = 0; x < 10; x++)
                        pixels[y * 64 + x] = 0xFFFFFFFF;

                for (int y = 0; y < 15; y++)
                    for (int x = 30; x < 55; x++)
                        pixels[y * 64 + x] = 0xFFFFFFFF;
            });

            var settings = new SpriteSheetSliceSettings
            {
                Layout = SpriteSheetLayout.AutoDetect,
                CanvasMode = SliceCanvasMode.FitFrame,
                AlphaThreshold = 10
            };

            var sprite = _slicer.SliceToAnimationSprite(source, settings);

            Assert.Equal(2, sprite.Frames.Count);
            // Max width: 25, Max height: 20
            Assert.Equal(25, sprite.Width);
            Assert.Equal(20, sprite.Height);
        }

        [Fact]
        public void DetectAlphaIslands_OpaqueBackgroundWithCornerChromaKey_DetectsIslands()
        {
            // Opaque magenta background (0xFFFF00FF) with a 10x10 black box (0xFF000000) inside
            var source = CreateAlphaTestBitmap(64, 64, pixels =>
            {
                // Fill entire image with opaque magenta
                Array.Fill(pixels, 0xFFFF00FF);

                // Island: (10,10) to (19,19)
                for (int y = 10; y < 20; y++)
                    for (int x = 10; x < 20; x++)
                        pixels[y * 64 + x] = 0xFF000000;
            });

            var islands = _slicer.DetectAlphaIslands(source, alphaThreshold: 10, minIslandWidth: 2, minIslandHeight: 2);

            Assert.Single(islands);
            Assert.Equal(10, islands[0].X);
            Assert.Equal(10, islands[0].Y);
            Assert.Equal(10, islands[0].Width);
            Assert.Equal(10, islands[0].Height);
        }
    }
}
