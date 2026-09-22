using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
public sealed class BitmapToMonochromeConverterTests
{
    [Fact]
    public void ConvertTo1Bit_AsymmetricDimensions_PreservesAspectAndDecodesCorrectly()
    {
        // 100x500 image
        const int origW = 100;
        const int origH = 500;
        var tempPath = Path.Combine(Path.GetTempPath(), $"asymm_{Guid.NewGuid():N}.png");

        try
        {
            var wb = new WriteableBitmap(origW, origH, 96, 96, PixelFormats.Bgra32, null);
            byte[] pixels = new byte[origW * origH * 4];
            // Fill with a vertical bar in the center
            for (int y = 0; y < origH; y++)
            {
                for (int x = 40; x < 60; x++)
                {
                    int p = (y * origW + x) * 4;
                    pixels[p] = 255;
                    pixels[p + 1] = 255;
                    pixels[p + 2] = 255;
                    pixels[p + 3] = 255;
                }
            }
            wb.WritePixels(new System.Windows.Int32Rect(0, 0, origW, origH), pixels, origW * 4, 0);

            using (var fs = File.OpenWrite(tempPath))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(wb));
                encoder.Save(fs);
            }

            var settings = new BitmapImportSettings
            {
                MaxDimension = 64,
                ScalingMode = Hexprite.Services.BitmapScalingMode.NearestNeighbor,
                Threshold = 128
            };

            var (monoPixels, w, h, wasScaled) = BitmapToMonochromeConverter.ConvertTo1Bit(tempPath, settings);

            Assert.True(wasScaled);
            // origMax is 500, scale is 64/500 = 0.128
            // targetW = Math.Round(100 * 0.128) = 13
            // targetH = Math.Round(500 * 0.128) = 64
            Assert.Equal(13, w);
            Assert.Equal(64, h);
            Assert.Equal(13 * 64, monoPixels.Length);

            // Verify the vertical bar is centered in the scaled 13-wide image (around x = 5..7)
            bool foundWhiteInCenter = false;
            for (int y = 0; y < h; y++)
            {
                if (monoPixels[y * w + 6])
                {
                    foundWhiteInCenter = true;
                    break;
                }
            }
            Assert.True(foundWhiteInCenter, "The center vertical bar should be preserved in the 13x64 scaled output.");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void ConvertBitmapSource_ScalingMode_NearestNeighbor_PreservesSharpEdges()
    {
        // 2x2 bitmap:
        // [ White, Black ]
        // [ Black, White ]
        var wb = new WriteableBitmap(2, 2, 96, 96, PixelFormats.Bgra32, null);
        byte[] pixels = [
            255, 255, 255, 255,   0,   0,   0, 255,
              0,   0,   0, 255, 255, 255, 255, 255
        ];
        wb.WritePixels(new System.Windows.Int32Rect(0, 0, 2, 2), pixels, 2 * 4, 0);

        var settings = new BitmapImportSettings
        {
            MaxDimension = 4,
            ScalingMode = Hexprite.Services.BitmapScalingMode.NearestNeighbor,
            Threshold = 128,
            DitheringAlgorithm = BitmapDitheringAlgorithm.Binary
        };

        var (monoPixels, w, h, wasScaled) = BitmapToMonochromeConverter.ConvertBitmapSource(wb, settings);

        Assert.True(wasScaled);
        Assert.Equal(4, w);
        Assert.Equal(4, h);

        // In 4x4 nearest neighbor, (0,0), (1,0), (0,1), (1,1) should all be white (true)
        Assert.True(monoPixels[0 * 4 + 0]);
        Assert.True(monoPixels[0 * 4 + 1]);
        Assert.True(monoPixels[1 * 4 + 0]);
        Assert.True(monoPixels[1 * 4 + 1]);

        // (2,0), (3,0), (2,1), (3,1) should all be black (false)
        Assert.False(monoPixels[0 * 4 + 2]);
        Assert.False(monoPixels[0 * 4 + 3]);
        Assert.False(monoPixels[1 * 4 + 2]);
        Assert.False(monoPixels[1 * 4 + 3]);
    }
}
