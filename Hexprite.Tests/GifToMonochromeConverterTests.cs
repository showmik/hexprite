using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class GifToMonochromeConverterTests
    {
        private static string CreateTestGif(int width, int height, int frameCount = 2)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"test_anim_{Guid.NewGuid():N}.gif");
            var encoder = new GifBitmapEncoder();

            for (int f = 0; f < frameCount; f++)
            {
                var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                byte[] pixels = new byte[width * height * 4];
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte val = (byte)(f == 0 ? 0 : 255); // Frame 0 black, Frame 1 white
                    pixels[i] = val;
                    pixels[i + 1] = val;
                    pixels[i + 2] = val;
                    pixels[i + 3] = 255;
                }
                wb.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), pixels, width * 4, 0);
                encoder.Frames.Add(BitmapFrame.Create(wb));
            }

            using (var fs = File.OpenWrite(tempPath))
            {
                encoder.Save(fs);
            }

            return tempPath;
        }

        [Fact]
        public void ConvertAnimatedGif_InvalidArguments_ThrowsExpectedExceptions()
        {
            var settings = new AnimationImportSettings();

            Assert.Throws<ArgumentException>(() => GifToMonochromeConverter.ConvertAnimatedGif("", settings));
            Assert.Throws<ArgumentException>(() => GifToMonochromeConverter.ConvertAnimatedGif("   ", settings));
            Assert.Throws<FileNotFoundException>(() => GifToMonochromeConverter.ConvertAnimatedGif("non_existent_file_123.gif", settings));
            Assert.Throws<ArgumentNullException>(() => GifToMonochromeConverter.ConvertAnimatedGif(Path.GetTempFileName(), null!));
        }

        [Fact]
        public void ConvertAnimatedGif_ValidGif_ConvertsFrames()
        {
            var gifPath = CreateTestGif(16, 16, frameCount: 2);

            try
            {
                var settings = new AnimationImportSettings
                {
                    MaxDimension = 16,
                    TargetFps = 10,
                    MaxFrames = 5,
                    UniformSampling = true,
                    Threshold = 128
                };

                var (frames, width, height, wasScaled) = GifToMonochromeConverter.ConvertAnimatedGif(gifPath, settings);

                Assert.NotEmpty(frames);
                Assert.Equal(16, width);
                Assert.Equal(16, height);
                Assert.Equal(16 * 16, frames[0].Length);
            }
            finally
            {
                if (File.Exists(gifPath)) File.Delete(gifPath);
            }
        }

        [Fact]
        public void ConvertAnimatedGif_NonUniformSampling_ReturnsFrames()
        {
            var gifPath = CreateTestGif(8, 8, frameCount: 3);

            try
            {
                var settings = new AnimationImportSettings
                {
                    MaxDimension = 8,
                    TargetFps = 10,
                    MaxFrames = 2,
                    UniformSampling = false,
                    Threshold = 128
                };

                var (frames, width, height, _) = GifToMonochromeConverter.ConvertAnimatedGif(gifPath, settings);

                Assert.Equal(2, frames.Count);
                Assert.Equal(8, width);
                Assert.Equal(8, height);
            }
            finally
            {
                if (File.Exists(gifPath)) File.Delete(gifPath);
            }
        }

        [Fact]
        public void CompositeGifFrames_EmptyFrames_ReturnsEmptyList()
        {
            var empty = new List<BitmapFrame>();
            var result = GifToMonochromeConverter.CompositeGifFrames(empty);

            Assert.Empty(result);
        }
    }
}
