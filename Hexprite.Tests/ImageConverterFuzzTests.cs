using FsCheck;
using FsCheck.Xunit;
using Hexprite.Services;
using Xunit;
using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hexprite.Tests
{
    [Trait("Category", "Fuzz")]
    public class ImageConverterFuzzTests
    {
        [Property(MaxTest = 100)]
        public void ConvertTo1Bit_ShouldHandleRandomImagesAndSettingsWithoutCrashing(
            int w, int h, 
            int threshold, int alphaThreshold, int brightness, int contrast, int ditherAmount,
            bool invert, bool useSerpentine, bool useGamma, bool useAdaptive, bool preserveEdges, bool sharpen,
            BitmapDitheringAlgorithm algorithm, Hexprite.Services.BitmapScalingMode scalingMode)
        {
            // Constrain width and height to 1..64 for fast fuzzing
            w = Math.Abs(w % 64) + 1;
            h = Math.Abs(h % 64) + 1;
            
            var settings = new BitmapImportSettings
            {
                MaxDimension = 128, // Don't crash on huge rescales
                Threshold = threshold,
                AlphaThreshold = alphaThreshold,
                Brightness = brightness,
                Contrast = contrast,
                DitherAmount = ditherAmount,
                Invert = invert,
                UseSerpentineScanning = useSerpentine,
                UseGammaCorrection = useGamma,
                UseAdaptiveThresholding = useAdaptive,
                PreserveEdges = preserveEdges,
                Sharpen = sharpen,
                DitheringAlgorithm = algorithm,
                ScalingMode = scalingMode
            };

            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".png");

            try
            {
                GenerateRandomPng(tempFile, w, h);

                // Run the converter
                var result = BitmapToMonochromeConverter.ConvertTo1Bit(tempFile, settings);

                // Properties:
                // 1. Output must not be null
                if (result.Pixels == null) throw new Exception("Converter returned null pixels!");
                
                // 2. Output dimension must match returned width/height
                if (result.Pixels.Length != result.Width * result.Height)
                {
                    throw new Exception($"Pixel array length {result.Pixels.Length} does not match {result.Width}x{result.Height}");
                }
            }
            catch (Exception ex)
            {
                // Rethrow FsCheck failure
                throw new Exception($"Converter crashed on {w}x{h} image with algorithm {algorithm}!", ex);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
        }

        private void GenerateRandomPng(string path, int width, int height)
        {
            var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            var random = new Random(width * height);
            
            byte[] pixels = new byte[width * height * 4];
            random.NextBytes(pixels); // Fill with complete garbage color data, including random alphas

            wb.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), pixels, width * 4, 0);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(wb));
            
            using (var stream = File.Create(path))
            {
                encoder.Save(stream);
            }
        }
    }
}
