using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hexprite.Services
{
    public static class GifToMonochromeConverter
    {
        public static (List<bool[]> Frames, int Width, int Height, bool WasScaled) ConvertAnimatedGif(
            string path,
            AnimationImportSettings settings)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be empty.", nameof(path));
            if (!File.Exists(path))
                throw new FileNotFoundException("Image file not found.", path);
            ArgumentNullException.ThrowIfNull(settings);

            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count == 0)
                throw new InvalidOperationException("Image has no frames.");

            // 1. Pre-select the frames to keep based on Target FPS and MaxFrames
            var selectedIndices = GetSelectedFrameIndices(decoder.Frames, settings.TargetFps, settings.MaxFrames, settings.UniformSampling);

            // 2. Composite frames using a lightweight software buffer, only constructing BitmapSource for selected frames
            var compositedFrames = CompositeGifFramesInternal(decoder.Frames, selectedIndices);

            List<bool[]> resultFrames = [];
            int finalW = 0, finalH = 0;
            bool wasScaled = false;

            // 3. Convert each selected frame to 1-bit monochrome in-memory (zero disk I/O)
            foreach (var frame in compositedFrames)
            {
                var (pixels, w, h, scaled) = BitmapToMonochromeConverter.ConvertBitmapSource(frame, settings);
                resultFrames.Add(pixels);
                finalW = w;
                finalH = h;
                wasScaled = scaled;
            }

            return (resultFrames, finalW, finalH, wasScaled);
        }

        public static List<BitmapSource> CompositeGifFrames(IReadOnlyList<BitmapFrame> rawFrames)
        {
            if (rawFrames.Count == 0) return [];
            return CompositeGifFramesInternal(rawFrames, selectedIndices: null);
        }

        private static List<BitmapSource> CompositeGifFramesInternal(
            IReadOnlyList<BitmapFrame> rawFrames,
            IReadOnlyList<int>? selectedIndices)
        {
            var result = new List<BitmapSource>();
            if (rawFrames.Count == 0) return result;

            int canvasWidth = rawFrames[0].PixelWidth;
            int canvasHeight = rawFrames[0].PixelHeight;

            for (int i = 0; i < rawFrames.Count; i++)
            {
                int left = 0, top = 0;
                if (rawFrames[i].Metadata is BitmapMetadata meta)
                {
                    try { left = Convert.ToInt32(meta.GetQuery("/imgdesc/Left"), CultureInfo.InvariantCulture); } catch { }
                    try { top = Convert.ToInt32(meta.GetQuery("/imgdesc/Top"), CultureInfo.InvariantCulture); } catch { }
                }
                int right = left + rawFrames[i].PixelWidth;
                int bottom = top + rawFrames[i].PixelHeight;
                if (right > canvasWidth) canvasWidth = right;
                if (bottom > canvasHeight) canvasHeight = bottom;
            }

            if (canvasWidth <= 0 || canvasHeight <= 0)
                return result;

            const int MaxSupportedDimension = 16384;
            if (canvasWidth > MaxSupportedDimension || canvasHeight > MaxSupportedDimension || (long)canvasWidth * canvasHeight > 32_000_000)
            {
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                    $"GIF dimensions ({canvasWidth}x{canvasHeight}) exceed the maximum supported size."));
            }

            HashSet<int>? selectedSet = selectedIndices != null ? [.. selectedIndices] : null;
            var cachedSelectedFrames = selectedIndices != null ? new BitmapSource?[rawFrames.Count] : null;

            uint[] canvas = new uint[canvasWidth * canvasHeight];
            uint[]? previousCanvas = null;

            int maxFramePixels = 0;
            for (int i = 0; i < rawFrames.Count; i++)
            {
                int count = rawFrames[i].PixelWidth * rawFrames[i].PixelHeight;
                if (count > maxFramePixels) maxFramePixels = count;
            }
            uint[] framePixelBuffer = new uint[Math.Max(1, maxFramePixels)];

            for (int i = 0; i < rawFrames.Count; i++)
            {
                var frame = rawFrames[i];
                int left = 0, top = 0;
                int disposalMethod = 0;

                if (frame.Metadata is BitmapMetadata meta)
                {
                    try { left = Convert.ToInt32(meta.GetQuery("/imgdesc/Left"), CultureInfo.InvariantCulture); } catch { }
                    try { top = Convert.ToInt32(meta.GetQuery("/imgdesc/Top"), CultureInfo.InvariantCulture); } catch { }
                    try { disposalMethod = Convert.ToByte(meta.GetQuery("/grctlext/Disposal"), CultureInfo.InvariantCulture); } catch { }
                }

                if (disposalMethod == 3)
                {
                    if (previousCanvas == null || previousCanvas.Length != canvas.Length)
                        previousCanvas = new uint[canvas.Length];
                    Array.Copy(canvas, previousCanvas, canvas.Length);
                }

                int frameW = frame.PixelWidth;
                int frameH = frame.PixelHeight;

                if (frameW > 0 && frameH > 0)
                {
                    BitmapSource bgraSource = frame;
                    if (frame.Format != PixelFormats.Bgra32)
                    {
                        bgraSource = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, destinationPalette: null, 0);
                    }

                    int frameStride = frameW * 4;
                    bgraSource.CopyPixels(framePixelBuffer, frameStride, 0);

                    for (int fy = 0; fy < frameH; fy++)
                    {
                        int cy = top + fy;
                        if (cy < 0 || cy >= canvasHeight) continue;

                        int canvasRow = cy * canvasWidth;
                        int frameRow = fy * frameW;

                        for (int fx = 0; fx < frameW; fx++)
                        {
                            int cx = left + fx;
                            if (cx < 0 || cx >= canvasWidth) continue;

                            uint srcPixel = framePixelBuffer[frameRow + fx];
                            byte srcA = (byte)(srcPixel >> 24);

                            if (srcA == 255)
                            {
                                canvas[canvasRow + cx] = srcPixel;
                            }
                            else if (srcA > 0)
                            {
                                uint dstPixel = canvas[canvasRow + cx];
                                byte dstA = (byte)(dstPixel >> 24);
                                if (dstA == 0)
                                {
                                    canvas[canvasRow + cx] = srcPixel;
                                }
                                else
                                {
                                    byte srcB = (byte)(srcPixel & 0xFF);
                                    byte srcG = (byte)((srcPixel >> 8) & 0xFF);
                                    byte srcR = (byte)((srcPixel >> 16) & 0xFF);

                                    byte dstB = (byte)(dstPixel & 0xFF);
                                    byte dstG = (byte)((dstPixel >> 8) & 0xFF);
                                    byte dstR = (byte)((dstPixel >> 16) & 0xFF);

                                    int invA = 255 - srcA;
                                    byte outA = (byte)(srcA + (dstA * invA) / 255);
                                    if (outA > 0)
                                    {
                                        byte outB = (byte)((srcB * srcA + dstB * dstA * invA / 255) / outA);
                                        byte outG = (byte)((srcG * srcA + dstG * dstA * invA / 255) / outA);
                                        byte outR = (byte)((srcR * srcA + dstR * dstA * invA / 255) / outA);
                                        canvas[canvasRow + cx] = (uint)(outB | (outG << 8) | (outR << 16) | (outA << 24));
                                    }
                                }
                            }
                        }
                    }
                }

                bool shouldEmit = selectedSet == null || selectedSet.Contains(i);
                if (shouldEmit)
                {
                    var frozenFrame = BitmapSource.Create(
                        canvasWidth,
                        canvasHeight,
                        96,
                        96,
                        PixelFormats.Bgra32,
                        null,
                        canvas,
                        canvasWidth * 4);
                    frozenFrame.Freeze();

                    if (cachedSelectedFrames != null)
                        cachedSelectedFrames[i] = frozenFrame;
                    else
                        result.Add(frozenFrame);
                }

                if (disposalMethod == 2)
                {
                    for (int fy = 0; fy < frameH; fy++)
                    {
                        int cy = top + fy;
                        if (cy < 0 || cy >= canvasHeight) continue;
                        int xStart = Math.Max(0, left);
                        int xEnd = Math.Min(canvasWidth, left + frameW);
                        if (xEnd > xStart)
                        {
                            Array.Clear(canvas, cy * canvasWidth + xStart, xEnd - xStart);
                        }
                    }
                }
                else if (disposalMethod == 3 && previousCanvas != null)
                {
                    Array.Copy(previousCanvas, canvas, canvas.Length);
                }
            }

            if (selectedIndices != null && cachedSelectedFrames != null)
            {
                foreach (int idx in selectedIndices)
                {
                    if (idx >= 0 && idx < cachedSelectedFrames.Length && cachedSelectedFrames[idx] != null)
                    {
                        result.Add(cachedSelectedFrames[idx]!);
                    }
                }
            }

            return result;
        }

        public static List<int> GetSelectedFrameIndices(
            IReadOnlyList<BitmapFrame> rawFrames,
            int targetFps,
            int maxFrames,
            bool uniformSampling)
        {
            if (rawFrames.Count == 0)
                return [];

            if (rawFrames.Count == 1)
                return [0];

            double totalDurationSec = 0;
            List<double> frameTimes = new(rawFrames.Count);

            for (int i = 0; i < rawFrames.Count; i++)
            {
                var f = rawFrames[i];
                double delaySec = 0.1; // default 100ms
                if (f.Metadata is BitmapMetadata meta)
                {
                    try
                    {
                        var delayObj = meta.GetQuery("/grctlext/Delay");
                        if (delayObj != null)
                        {
                            int delay10ms = Convert.ToInt32(delayObj, CultureInfo.InvariantCulture);
                            if (delay10ms == 0) delay10ms = 10; // 0 usually means default speed in browsers
                            delaySec = delay10ms * 0.01;
                        }
                    }
                    catch { }
                }
                frameTimes.Add(totalDurationSec);
                totalDurationSec += delaySec;
            }

            int desiredFrameCount = (int)Math.Round(totalDurationSec * targetFps, MidpointRounding.AwayFromZero);
            desiredFrameCount = Math.Clamp(desiredFrameCount, 1, Math.Max(1, maxFrames));

            List<int> selected = [];

            if (!uniformSampling)
            {
                int take = Math.Clamp(Math.Min(rawFrames.Count, maxFrames), 1, rawFrames.Count);
                for (int i = 0; i < take; i++)
                    selected.Add(i);
                return selected;
            }

            desiredFrameCount = Math.Min(desiredFrameCount, maxFrames);
            double targetTimeStep = desiredFrameCount > 1 && totalDurationSec > 0
                ? totalDurationSec / desiredFrameCount
                : 1.0 / Math.Max(1, targetFps);

            for (int i = 0; i < desiredFrameCount; i++)
            {
                double targetTime = i * targetTimeStep;

                // Find closest frame
                int closestIdx = 0;
                double minDiff = double.MaxValue;
                for (int j = 0; j < frameTimes.Count; j++)
                {
                    double diff = Math.Abs(frameTimes[j] - targetTime);
                    if (diff < minDiff)
                    {
                        minDiff = diff;
                        closestIdx = j;
                    }
                }
                closestIdx = Math.Clamp(closestIdx, 0, rawFrames.Count - 1);
                selected.Add(closestIdx);
            }

            return selected;
        }

        private static List<BitmapSource> SelectFrames(
            List<BitmapSource> composited,
            ReadOnlyCollection<BitmapFrame> rawFrames,
            int targetFps,
            int maxFrames,
            bool uniformSampling)
        {
            var indices = GetSelectedFrameIndices(rawFrames, targetFps, maxFrames, uniformSampling);
            return indices.Select(i => composited[Math.Clamp(i, 0, composited.Count - 1)]).ToList();
        }
    }
}