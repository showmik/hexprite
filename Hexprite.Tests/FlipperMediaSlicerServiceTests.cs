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
    public class FlipperMediaSlicerServiceTests
    {
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
            // 384x64 strip -> 3 frames of 128x64
            var source = CreateTestBitmap(384, 64);
            var settings = new MediaSliceSettings
            {
                Layout = SpriteSheetLayout.HorizontalStrip,
                FrameWidth = 128,
                FrameHeight = 64
            };

            var sprite = FlipperMediaSlicerService.SliceToAnimationSprite(source, settings);

            Assert.NotNull(sprite);
            Assert.Equal(3, sprite.Frames.Count);
            Assert.Equal(128, sprite.Width);
            Assert.Equal(64, sprite.Height);
        }

        [Fact]
        public void SliceToAnimationSprite_VerticalStrip_CreatesExpectedFrames()
        {
            // 128x128 strip -> 2 frames of 128x64
            var source = CreateTestBitmap(128, 128);
            var settings = new MediaSliceSettings
            {
                Layout = SpriteSheetLayout.VerticalStrip,
                FrameWidth = 128,
                FrameHeight = 64
            };

            var sprite = FlipperMediaSlicerService.SliceToAnimationSprite(source, settings);

            Assert.NotNull(sprite);
            Assert.Equal(2, sprite.Frames.Count);
        }

        [Fact]
        public void SliceToAnimationSprite_GridAtlas_CreatesExpectedFrames()
        {
            // 256x128 atlas (2 cols x 2 rows) -> 4 frames
            var source = CreateTestBitmap(256, 128);
            var settings = new MediaSliceSettings
            {
                Layout = SpriteSheetLayout.Grid,
                FrameWidth = 128,
                FrameHeight = 64,
                Columns = 2,
                Rows = 2
            };

            var sprite = FlipperMediaSlicerService.SliceToAnimationSprite(source, settings);

            Assert.NotNull(sprite);
            Assert.Equal(4, sprite.Frames.Count);
        }
    }
}
