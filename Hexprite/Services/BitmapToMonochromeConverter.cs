using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Globalization;

namespace Hexprite.Services
{
    /// <summary>
    /// Specifies the algorithm used for dithering images during monochrome conversion.
    /// </summary>
    public enum BitmapDitheringAlgorithm
    {
        FloydSteinberg = 0,
        Binary = 1,
        Bayer = 2,
        Atkinson = 3,
        Stucki = 4,
        SierraLite = 5,
    }

    /// <summary>
    /// Specifies the interpolation mode for scaling images during import.
    /// </summary>
    public enum BitmapScalingMode
    {
        NearestNeighbor = 0,
        HighQualityBicubic = 1,
        Fant = 2,
    }

    /// <summary>
    /// Defines configuration parameters for bitmap-to-monochrome conversion.
    /// </summary>
    public class BitmapImportSettings
    {
        public int MaxDimension { get; set; } = 128;
        public int Threshold { get; set; } = 128;
        public int AlphaThreshold { get; set; } = 128;
        public bool Invert { get; set; }
        public BitmapDitheringAlgorithm DitheringAlgorithm { get; set; } = BitmapDitheringAlgorithm.FloydSteinberg;
        public BitmapScalingMode ScalingMode { get; set; } = BitmapScalingMode.Fant;
        public bool UseSerpentineScanning { get; set; }
        public bool UseGammaCorrection { get; set; }
        public bool UseAdaptiveThresholding { get; set; }
        public bool PreserveEdges { get; set; }

        // Image Enhancement
        public bool Sharpen { get; set; }
        public int Brightness { get; set; }  // -100 to 100
        public int Contrast { get; set; }    // -100 to 100
        public int DitherAmount { get; set; } = 100; // 0 to 100
    }

    /// <summary>
    /// Converts an arbitrary bitmap image into a 1-bit (on/off) pixel grid using
    /// high-quality interpolation scaling and configurable dithering.
    /// </summary>
    public static class BitmapToMonochromeConverter
    {
        /// <summary>
        /// Converts the image at <paramref name="path"/> into monochrome pixels using default settings.
        /// </summary>
        /// <param name="path">The file path to the source image.</param>
        /// <param name="maxDimension">The maximum width or height of the result.</param>
        /// <returns>A tuple containing pixel array, width, height, and a boolean indicating if scaling was applied.</returns>
        public static (bool[] Pixels, int Width, int Height, bool WasScaled) ConvertTo1Bit(
            string path,
            int maxDimension)
        {
            return ConvertTo1Bit(path, new BitmapImportSettings
            {
                MaxDimension = maxDimension,
            });
        }

        /// <summary>
        /// Converts the image at <paramref name="path"/> into monochrome pixels using provided <paramref name="settings"/>.
        /// </summary>
        /// <param name="path">The file path to the source image.</param>
        /// <param name="settings">The conversion settings.</param>
        /// <returns>A tuple containing pixel array, width, height, and a boolean indicating if scaling was applied.</returns>
        public static (bool[] Pixels, int Width, int Height, bool WasScaled) ConvertTo1Bit(
            string path,
            BitmapImportSettings settings)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be empty.", nameof(path));

            if (!File.Exists(path))
                throw new FileNotFoundException("Image file not found.", path);

            ArgumentNullException.ThrowIfNull(settings);

            if (settings.MaxDimension <= 0)
                throw new ArgumentOutOfRangeException(nameof(settings), "MaxDimension must be > 0.");

            // Robustness: read header to get original dimensions for scaling calculations.
            int origW, origH;
            using (var headerStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var headerDecoder = BitmapDecoder.Create(
                    headerStream,
                    BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile,
                    BitmapCacheOption.OnLoad); // M3: OnLoad forces immediate read so the FileStream can be safely closed

                if (headerDecoder.Frames == null || headerDecoder.Frames.Count == 0)
                    throw new InvalidOperationException("Image file has no frames.");

                var headerFrame = headerDecoder.Frames[0];
                origW = headerFrame.PixelWidth;
                origH = headerFrame.PixelHeight;
            }

            if (origW <= 0 || origH <= 0)
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"Invalid image dimensions: {origW}x{origH}"));

            int origMax = Math.Max(origW, origH);
            bool wasScaled = false;
            int threshold = Math.Clamp(settings.Threshold, 0, 255);
            int alphaThreshold = Math.Clamp(settings.AlphaThreshold, 0, 255);

            // Calculate target dimensions
            int targetW = origW;
            int targetH = origH;
            if (origMax != settings.MaxDimension)
            {
                double scale = settings.MaxDimension / (double)origMax;
                targetW = Math.Max(1, (int)Math.Round(origW * scale, MidpointRounding.AwayFromZero));
                targetH = Math.Max(1, (int)Math.Round(origH * scale, MidpointRounding.AwayFromZero));
                wasScaled = targetW != origW || targetH != origH;
            }

            // Decode at full resolution or reasonable intermediate size for quality scaling
            int decodeW = origW;
            int decodeH = origH;

            // If scaling down significantly, decode at 2x target to reduce memory while maintaining quality
            if (wasScaled)
            {
                decodeW = Math.Min(origW, targetW * 2);
                decodeH = Math.Min(origH, targetH * 2);
            }

            BitmapSource decoded;
            using (var decodeStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = decodeStream;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                if (decodeW != origW)
                    bitmap.DecodePixelWidth = decodeW;
                if (decodeH != origH)
                    bitmap.DecodePixelHeight = decodeH;
                bitmap.EndInit();
                bitmap.Freeze();
                decoded = bitmap;
            }

            // Apply high-quality scaling if needed
            if (wasScaled && (decoded.PixelWidth != targetW || decoded.PixelHeight != targetH))
            {
                double scaleX = targetW / (double)decoded.PixelWidth;
                double scaleY = targetH / (double)decoded.PixelHeight;

                var transform = new ScaleTransform(scaleX, scaleY);
                BitmapSource scaled;

                switch (settings.ScalingMode)
                {
                    case BitmapScalingMode.NearestNeighbor:
                        // Use simple TransformedBitmap for nearest neighbor
                        scaled = new TransformedBitmap(decoded, transform);
                        break;

                    case BitmapScalingMode.HighQualityBicubic:
                    case BitmapScalingMode.Fant:
                    default:
                        // Use RenderTargetBitmap with high-quality rendering for best results
                        // Fant (Lanczos) provides the best quality for downscaling
                        var renderTarget = new RenderTargetBitmap(targetW, targetH, 96, 96, PixelFormats.Pbgra32);
                        var visual = new System.Windows.Media.DrawingVisual();
                        using (var context = visual.RenderOpen())
                        {
                            var brush = new ImageBrush(decoded)
                            {
                                Stretch = Stretch.Fill,
                            };
                            // Set high-quality bitmap scaling mode
                            var wpfScalingMode = settings.ScalingMode == BitmapScalingMode.Fant
                                ? System.Windows.Media.BitmapScalingMode.Fant
                                : System.Windows.Media.BitmapScalingMode.HighQuality;
                            RenderOptions.SetBitmapScalingMode(brush, wpfScalingMode);
                            context.DrawRectangle(brush, pen: null, new System.Windows.Rect(0, 0, targetW, targetH));
                        }
                        renderTarget.Render(visual);
                        renderTarget.Freeze();
                        scaled = renderTarget;
                        break;
                }

                // M1: Freeze the scaled result to release WPF dispatcher back-references
                // (TransformedBitmap in the NearestNeighbor path is never frozen otherwise).
                if (scaled is Freezable freezableScaled && freezableScaled.CanFreeze)
                    freezableScaled.Freeze();
                decoded = scaled;
            }

            return ProcessBgraBitmap(decoded, settings, wasScaled);
        }

        public static (bool[] Pixels, int Width, int Height, bool WasScaled) ConvertBitmapSource(
            BitmapSource source,
            BitmapImportSettings settings)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(settings);

            int origW = source.PixelWidth;
            int origH = source.PixelHeight;
            if (origW <= 0 || origH <= 0)
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"Invalid image dimensions: {origW}x{origH}"));

            int origMax = Math.Max(origW, origH);
            bool wasScaled = false;
            int targetW = origW;
            int targetH = origH;
            if (settings.MaxDimension > 0 && origMax != settings.MaxDimension)
            {
                double scale = settings.MaxDimension / (double)origMax;
                targetW = Math.Max(1, (int)Math.Round(origW * scale, MidpointRounding.AwayFromZero));
                targetH = Math.Max(1, (int)Math.Round(origH * scale, MidpointRounding.AwayFromZero));
                wasScaled = targetW != origW || targetH != origH;
            }

            BitmapSource decoded = source;
            if (wasScaled && (decoded.PixelWidth != targetW || decoded.PixelHeight != targetH))
            {
                double scaleX = targetW / (double)decoded.PixelWidth;
                double scaleY = targetH / (double)decoded.PixelHeight;
                var transform = new ScaleTransform(scaleX, scaleY);
                var scaled = new TransformedBitmap(decoded, transform);
                if (scaled.CanFreeze) scaled.Freeze();
                decoded = scaled;
            }

            return ProcessBgraBitmap(decoded, settings, wasScaled);
        }

        private static (bool[] Pixels, int Width, int Height, bool WasScaled) ProcessBgraBitmap(
            BitmapSource decoded,
            BitmapImportSettings settings,
            bool wasScaled)
        {
            int threshold = Math.Clamp(settings.Threshold, 0, 255);
            int alphaThreshold = Math.Clamp(settings.AlphaThreshold, 0, 255);

            // Normalize to BGRA32 for consistent CopyPixels reads.
            var bgra32 = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, destinationPalette: null, 0);
            bgra32.Freeze();

            int srcW = bgra32.PixelWidth;
            int srcH = bgra32.PixelHeight;
            if (srcW <= 0 || srcH <= 0)
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"Invalid image dimensions: {srcW}x{srcH}"));

            // After decode downscaling (if any), keep the decoded resolution as-is.
            int dstW = srcW;
            int dstH = srcH;

            int srcStride = srcW * 4;
            byte[] srcPixels = new byte[srcStride * srcH];
            bgra32.CopyPixels(srcPixels, srcStride, 0);

            // Build a grayscale buffer. Because we already decode at the target size,
            // we can read pixels 1:1 without per-pixel sampling math.
            bool[] isTransparent = new bool[dstW * dstH];
            byte[] gray = new byte[dstW * dstH];
            
            double[]? srgbToLinear = null;
            if (settings.UseGammaCorrection)
            {
                srgbToLinear = new double[256];
                for (int i = 0; i < 256; i++)
                    srgbToLinear[i] = Math.Pow(i / 255.0, 2.2);
            }

            double contrastFactor = 1.0;
            if (settings.Contrast != 0)
            {
                double c = Math.Clamp(settings.Contrast, -100, 100) * 2.55;
                contrastFactor = (259.0 * (c + 255.0)) / (255.0 * (259.0 - c));
            }
            int brightnessOffset = Math.Clamp(settings.Brightness, -100, 100);

            for (int y = 0; y < dstH; y++)
            {
                int row = y * dstW;
                int srcRow = y * srcStride;

                for (int x = 0; x < dstW; x++)
                {
                    int srcIdx = srcRow + (x * 4);
                    byte b = srcPixels[srcIdx + 0];
                    byte g = srcPixels[srcIdx + 1];
                    byte r = srcPixels[srcIdx + 2];
                    byte a = srcPixels[srcIdx + 3];

                    // Transparent pixels treated as background (0) so they don't push noise during dithering
                    if (a < alphaThreshold)
                    {
                        isTransparent[row + x] = true;
                        gray[row + x] = 0;
                        continue;
                    }

                    int luma;
                    if (settings.UseGammaCorrection)
                    {
                        // Gamma-corrected luma for perceptually accurate grayscale.
                        // Uses precalculated LUT for extreme performance.
                        double lumaLinear = 0.299 * srgbToLinear![r] + 
                                            0.587 * srgbToLinear[g] + 
                                            0.114 * srgbToLinear[b];

                        // Convert back to sRGB (only 1 Pow required per pixel instead of 4)
                        luma = (int)(Math.Pow(lumaLinear, 1.0 / 2.2) * 255.0 + 0.5);
                    }
                    else
                    {
                        // Standard linear luma formula (fast, backward compatible)
                        luma = (int)(0.299 * r + 0.587 * g + 0.114 * b + 0.5);
                    }

                    if (settings.Contrast != 0 || settings.Brightness != 0)
                    {
                        double adjusted = ((luma - 128) * contrastFactor) + 128 + brightnessOffset;
                        luma = (int)Math.Clamp(adjusted, 0, 255);
                    }

                    if (settings.Invert)
                        luma = 255 - luma;
                    gray[row + x] = (byte)Math.Clamp(luma, 0, 255);
                }
            }

            if (settings.Sharpen)
                gray = ApplySharpenFilter(gray, dstW, dstH);

            bool[] pixels = new bool[dstW * dstH];

            // M5: Array.Fill avoids the throwaway Enumerable iterator allocation.
            int[] thresholds;
            if (settings.UseAdaptiveThresholding)
                thresholds = ComputeAdaptiveThresholds(gray, dstW, dstH);
            else
            {
                thresholds = new int[dstW * dstH];
                Array.Fill(thresholds, threshold);
            }

            // M6: Array.Empty avoids a heap allocation when edge-preservation is disabled.
            // All dithering kernels guard isEdge access with a length check.
            bool[] isEdge = settings.PreserveEdges ? DetectEdgesSobel(gray, dstW, dstH) : [];
            int ditherAmount = Math.Clamp(settings.DitherAmount, 0, 100);

            switch (settings.DitheringAlgorithm)
            {
                case BitmapDitheringAlgorithm.Binary:
                    ApplyBinary(gray, pixels, thresholds);
                    break;
                case BitmapDitheringAlgorithm.Bayer:
                    ApplyBayer(gray, pixels, dstW, dstH, thresholds, ditherAmount);
                    break;
                case BitmapDitheringAlgorithm.Atkinson:
                    ApplyAtkinson(gray, pixels, dstW, dstH, thresholds, isEdge, ditherAmount);
                    break;
                case BitmapDitheringAlgorithm.Stucki:
                    ApplyStucki(gray, pixels, dstW, dstH, thresholds, isEdge, settings.UseSerpentineScanning, ditherAmount);
                    break;
                case BitmapDitheringAlgorithm.SierraLite:
                    ApplySierraLite(gray, pixels, dstW, dstH, thresholds, isEdge, settings.UseSerpentineScanning, ditherAmount);
                    break;
                default:
                    ApplyFloydSteinberg(gray, pixels, dstW, dstH, thresholds, isEdge, settings.UseSerpentineScanning, ditherAmount);
                    break;
            }

            for (int i = 0; i < pixels.Length; i++)
            {
                if (isTransparent[i])
                    pixels[i] = false;
            }

            return (pixels, dstW, dstH, wasScaled);
        }

        
        private static int[] ComputeAdaptiveThresholds(byte[] gray, int w, int h)
        {
            int[] thresholds = new int[w * h];
            long[] integral = new long[w * h];

            // Build integral image
            for (int y = 0; y < h; y++)
            {
                long sum = 0;
                for (int x = 0; x < w; x++)
                {
                    sum += gray[y * w + x];
                    integral[y * w + x] = (y == 0) ? sum : integral[(y - 1) * w + x] + sum;
                }
            }

            // Bradley-Roth adaptive threshold
            int s = Math.Max(w / 8, 1);
            int s2 = s / 2;
            
            // The previous buggy count calculation effectively raised the threshold by ~11% 
            // for standard sizes, making the effective t around 4 instead of 15.
            // We use t = 4 to restore the highly sensitive behavior that users liked.
            int t = 4; // 4% darker than average

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int x1 = Math.Max(x - s2, 0);
                    int x2 = Math.Min(x + s2, w - 1);
                    int y1 = Math.Max(y - s2, 0);
                    int y2 = Math.Min(y + s2, h - 1);

                    int count = (x2 - x1 + 1) * (y2 - y1 + 1);

                    // B1: Guard divide-by-zero when the image is 1px wide/tall and the
                    // window degenerates (x1==x2 or y1==y2 → count==0).
                    if (count <= 0)
                    {
                        thresholds[y * w + x] = 128;
                        continue;
                    }

                    long sum = integral[y2 * w + x2];
                    if (y1 > 0 && x1 > 0) sum += integral[(y1 - 1) * w + (x1 - 1)];
                    if (y1 > 0) sum -= integral[(y1 - 1) * w + x2];
                    if (x1 > 0) sum -= integral[y2 * w + (x1 - 1)];

                    thresholds[y * w + x] = (int)(sum * (100 - t) / (100 * count));
                }
            }
            return thresholds;
        }

        private static bool[] DetectEdgesSobel(byte[] gray, int w, int h)
        {
            bool[] isEdge = new bool[w * h];
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    int gx = -gray[(y - 1) * w + (x - 1)] + gray[(y - 1) * w + (x + 1)]
                             - 2 * gray[y * w + (x - 1)] + 2 * gray[y * w + (x + 1)]
                             - gray[(y + 1) * w + (x - 1)] + gray[(y + 1) * w + (x + 1)];

                    int gy = -gray[(y - 1) * w + (x - 1)] - 2 * gray[(y - 1) * w + x] - gray[(y - 1) * w + (x + 1)]
                             + gray[(y + 1) * w + (x - 1)] + 2 * gray[(y + 1) * w + x] + gray[(y + 1) * w + (x + 1)];

                    int mag = Math.Abs(gx) + Math.Abs(gy);
                    if (mag > 128) isEdge[y * w + x] = true;
                }
            }
            return isEdge;
        }

        private static void ApplyBinary(byte[] gray, bool[] pixels, int[] thresholds)
        {
            for (int i = 0; i < gray.Length; i++)
            {
                int quantized = gray[i] < thresholds[i] ? 0 : 255;
                pixels[i] = quantized != 0;
            }
        }

        // 8x8 Bayer matrix for smoother dithering patterns
        private static readonly byte[,] Bayer8x8 =
        {
            {  0, 48, 12, 60,  3, 51, 15, 63 },
            { 32, 16, 44, 28, 35, 19, 47, 31 },
            {  8, 56,  4, 52, 11, 59,  7, 55 },
            { 40, 24, 36, 20, 43, 27, 39, 23 },
            {  2, 50, 14, 62,  1, 49, 13, 61 },
            { 34, 18, 46, 30, 33, 17, 45, 29 },
            { 10, 58,  6, 54,  9, 57,  5, 53 },
            { 42, 26, 38, 22, 41, 25, 37, 21 },
        };

        private static byte[] ApplySharpenFilter(byte[] gray, int w, int h)
        {
            byte[] sharpened = new byte[w * h];
            // Simple 3x3 Sharpen matrix:
            //  0 -1  0
            // -1  5 -1
            //  0 -1  0
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (x == 0 || x == w - 1 || y == 0 || y == h - 1)
                    {
                        sharpened[y * w + x] = gray[y * w + x];
                        continue;
                    }

                    int sum = 5 * gray[y * w + x] 
                              - gray[(y - 1) * w + x] 
                              - gray[(y + 1) * w + x] 
                              - gray[y * w + (x - 1)] 
                              - gray[y * w + (x + 1)];

                    sharpened[y * w + x] = (byte)Math.Clamp(sum, 0, 255);
                }
            }
            return sharpened;
        }

        private static void ApplyBayer(byte[] gray, bool[] pixels, int width, int height, int[] thresholds, int ditherAmount)
        {
            float amount = ditherAmount / 100f;
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                int my = y & 7;  // modulo 8
                for (int x = 0; x < width; x++)
                {
                    int i = row + x;
                    int mx = x & 7;  // modulo 8
                    // Map 0-63 to -32 to +31 offset range
                    int bayerOffset = (int)(((Bayer8x8[my, mx] * 4) - 126) * amount);
                    int localThreshold = Math.Clamp(thresholds[i] + bayerOffset, 0, 255);
                    int quantized = gray[i] < localThreshold ? 0 : 255;
                    pixels[i] = quantized != 0;
                }
            }
        }

        private static void ApplyAtkinson(byte[] gray, bool[] pixels, int width, int height, int[] thresholds, bool[] isEdge, int ditherAmount)
        {
            int[] work = new int[gray.Length];
            for (int i = 0; i < gray.Length; i++)
                work[i] = gray[i];

            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    int i = row + x;
                    int oldVal = Math.Clamp(work[i], 0, 255);
                    int newVal = oldVal < thresholds[i] ? 0 : 255;
                    int err = oldVal - newVal;
                    if (isEdge.Length > 0 && isEdge[i]) err = 0; // M6: guard empty array
                    if (ditherAmount < 100) err = (err * ditherAmount) / 100;
                    pixels[i] = newVal != 0;

                    if (err == 0)
                        continue;

                    // B2: Atkinson distributes 1/8 of the error to each of 6 neighbours
                    // (total propagated = 6/8). The original code propagated raw `err` x6,
                    // causing catastrophic contrast blow-up on near-gray pixels.
                    int e = err >> 3;
                    if (x + 1 < width) work[i + 1] += e;
                    if (x + 2 < width) work[i + 2] += e;
                    if (y + 1 < height)
                    {
                        int row1 = i + width;
                        if (x > 0) work[row1 - 1] += e;
                        work[row1] += e;
                        if (x + 1 < width) work[row1 + 1] += e;
                    }
                    if (y + 2 < height) work[i + (2 * width)] += e;
                }
            }
        }

        private static void ApplyStucki(byte[] gray, bool[] pixels, int width, int height, int[] thresholds, bool[] isEdge, bool useSerpentine, int ditherAmount)
        {
            // Stucki dithering with optional serpentine scanning for reduced directional artifacts.
            // Uses 12-pixel diffusion matrix. Coefficients sum to 42.
            int[] errRow = new int[width + 4];      // Current row errors (with padding)
            int[] errNext = new int[width + 4];     // Next row errors
            int[] errNext2 = new int[width + 4];    // Row after next (for Stucki)

            for (int y = 0; y < height; y++)
            {
                Array.Clear(errNext2, 0, errNext2.Length);
                int row = y * width;
                bool leftToRight = !useSerpentine || (y % 2) == 0;  // Serpentine: alternate direction each row

                int startX = leftToRight ? 0 : width - 1;
                int endX = leftToRight ? width : -1;
                int step = leftToRight ? 1 : -1;

                for (int x = startX; x != endX; x += step)
                {
                    int i = row + x;

                    // Apply propagated error (keep in int domain).
                    int oldVal = gray[i] + errRow[x + 2];
                    if (oldVal < 0) oldVal = 0;
                    else if (oldVal > 255) oldVal = 255;

                    int newVal = oldVal < thresholds[i] ? 0 : 255;
                    int err = oldVal - newVal;
                    if (isEdge.Length > 0 && isEdge[i]) err = 0; // M6: guard empty array
                    if (ditherAmount < 100) err = (err * ditherAmount) / 100;
                    pixels[i] = newVal != 0;

                    if (err == 0) continue;

                    // Stucki error diffusion matrix (coefficients/42):
                    //              [current]  8/42   4/42
                    //   2/42  4/42    8/42   4/42   2/42
                    //   1/42  2/42    4/42   2/42   1/42
                    // Mirror coefficients when scanning right-to-left

                    if (leftToRight)
                    {
                        // Current row (shifted by 2 for padding)
                        if (x + 1 < width) errRow[x + 3] += (err * 8) / 42;   // +1
                        if (x + 2 < width) errRow[x + 4] += (err * 4) / 42;   // +2

                        // Next row
                        if (y + 1 < height)
                        {
                            if (x >= 2) errNext[x] += (err * 2) / 42;         // -2
                            if (x >= 1) errNext[x + 1] += (err * 4) / 42;     // -1
                            errNext[x + 2] += (err * 8) / 42;                  // 0
                            if (x + 1 < width) errNext[x + 3] += (err * 4) / 42;  // +1
                            if (x + 2 < width) errNext[x + 4] += (err * 2) / 42;  // +2
                        }

                        // Row after next (y+2)
                        if (y + 2 < height)
                        {
                            if (x >= 2) errNext2[x] += (err * 1) / 42;        // -2
                            if (x >= 1) errNext2[x + 1] += (err * 2) / 42;    // -1
                            errNext2[x + 2] += (err * 4) / 42;               // 0
                            if (x + 1 < width) errNext2[x + 3] += (err * 2) / 42; // +1
                            if (x + 2 < width) errNext2[x + 4] += (err * 1) / 42; // +2
                        }
                    }
                    else
                    {
                        // Scanning right-to-left: mirror the diffusion pattern
                        // Current row
                        if (x - 1 >= 0) errRow[x + 1] += (err * 8) / 42;   // -1
                        if (x - 2 >= 0) errRow[x] += (err * 4) / 42;   // -2

                        // Next row
                        if (y + 1 < height)
                        {
                            if (x + 2 < width) errNext[x + 4] += (err * 2) / 42;         // +2
                            if (x + 1 < width) errNext[x + 3] += (err * 4) / 42;     // +1
                            errNext[x + 2] += (err * 8) / 42;                  // 0
                            if (x - 1 >= 0) errNext[x + 1] += (err * 4) / 42;  // -1
                            if (x - 2 >= 0) errNext[x] += (err * 2) / 42;  // -2
                        }

                        // Row after next (y+2)
                        if (y + 2 < height)
                        {
                            if (x + 2 < width) errNext2[x + 4] += (err * 1) / 42;        // +2
                            if (x + 1 < width) errNext2[x + 3] += (err * 2) / 42;    // +1
                            errNext2[x + 2] += (err * 4) / 42;               // 0
                            if (x - 1 >= 0) errNext2[x + 1] += (err * 2) / 42; // -1
                            if (x - 2 >= 0) errNext2[x] += (err * 1) / 42; // -2
                        }
                    }
                }

                // Advance error buffers: current <- next, next <- next+2
                var tmp = errRow;
                errRow = errNext;
                errNext = errNext2;
                errNext2 = tmp;
            }
        }

        private static void ApplySierraLite(byte[] gray, bool[] pixels, int width, int height, int[] thresholds, bool[] isEdge, bool useSerpentine, int ditherAmount)
        {
            // Sierra-Lite (Sierra-2-4A) dithering.
            // Coefficients sum to 4. Matrix:
            //   *   2
            // 1 1   0  (divided by 4)
            int[] errRow = new int[width + 2];
            int[] errNext = new int[width + 2];

            for (int y = 0; y < height; y++)
            {
                Array.Clear(errNext, 0, errNext.Length);
                int row = y * width;
                bool leftToRight = !useSerpentine || (y % 2) == 0;

                int startX = leftToRight ? 0 : width - 1;
                int endX = leftToRight ? width : -1;
                int step = leftToRight ? 1 : -1;

                for (int x = startX; x != endX; x += step)
                {
                    int i = row + x;

                    int oldVal = gray[i] + errRow[x + 1];
                    if (oldVal < 0) oldVal = 0;
                    else if (oldVal > 255) oldVal = 255;

                    int newVal = oldVal < thresholds[i] ? 0 : 255;
                    int err = oldVal - newVal;
                    if (isEdge.Length > 0 && isEdge[i]) err = 0;
                    if (ditherAmount < 100) err = (err * ditherAmount) / 100;
                    pixels[i] = newVal != 0;

                    if (err == 0) continue;

                    if (leftToRight)
                    {
                        if (x + 1 < width) errRow[x + 2] += (err * 2) / 4;
                        if (x > 0) errNext[x] += (err * 1) / 4;
                        errNext[x + 1] += (err * 1) / 4;
                    }
                    else
                    {
                        if (x - 1 >= 0) errRow[x] += (err * 2) / 4;
                        if (x + 1 < width) errNext[x + 2] += (err * 1) / 4;
                        errNext[x + 1] += (err * 1) / 4;
                    }
                }
                (errNext, errRow) = (errRow, errNext);
            }
        }

        private static void ApplyFloydSteinberg(byte[] gray, bool[] pixels, int width, int height, int[] thresholds, bool[] isEdge, bool useSerpentine, int ditherAmount)
        {
            // Floyd-Steinberg dithering with optional serpentine scanning.
            // Coefficients: 7/16, 5/16, 3/16, 1/16
            int[] errRow = new int[width + 2];
            int[] errNext = new int[width + 2];

            for (int y = 0; y < height; y++)
            {
                Array.Clear(errNext, 0, errNext.Length);
                int row = y * width;
                bool leftToRight = !useSerpentine || (y % 2) == 0;  // Serpentine: alternate direction each row

                int startX = leftToRight ? 0 : width - 1;
                int endX = leftToRight ? width : -1;
                int step = leftToRight ? 1 : -1;

                for (int x = startX; x != endX; x += step)
                {
                    int i = row + x;

                    // Apply propagated error (keep in int domain).
                    int oldVal = gray[i] + errRow[x + 1];
                    if (oldVal < 0) oldVal = 0;
                    else if (oldVal > 255) oldVal = 255;

                    int newVal = oldVal < thresholds[i] ? 0 : 255;
                    int err = oldVal - newVal;
                    if (isEdge.Length > 0 && isEdge[i]) err = 0; // M6: guard empty array
                    if (ditherAmount < 100) err = (err * ditherAmount) / 100;
                    pixels[i] = newVal != 0;

                    // Distribute quantization error (integer Floyd-Steinberg).
                    // Mirror coefficients when scanning right-to-left
                    if (leftToRight)
                    {
                        if (x + 1 < width) errRow[x + 2] += (err * 7) / 16;
                        errNext[x + 1] += (err * 5) / 16;
                        if (x > 0) errNext[x] += (err * 3) / 16;
                        if (x + 1 < width) errNext[x + 2] += (err * 1) / 16;
                    }
                    else
                    {
                        // Right-to-left: mirror the diffusion
                        if (x - 1 >= 0) errRow[x] += (err * 7) / 16;  // left
                        errNext[x + 1] += (err * 5) / 16;  // below
                        if (x + 1 < width) errNext[x + 2] += (err * 3) / 16;  // below-right
                        if (x - 1 >= 0) errNext[x] += (err * 1) / 16;  // below-left
                    }
                }

                // Advance error buffers.
                (errNext, errRow) = (errRow, errNext);
            }
        }
    }
}

