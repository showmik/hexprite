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

            // 1. Detect effective active frames if trimming trailing blank frames
            int rawFrameLimit = decoder.Frames.Count;
            if (settings.TrimTrailingBlankFrames && rawFrameLimit > 1)
            {
                int lastActive = FindLastActiveFrameIndex(decoder.Frames);
                if (lastActive >= 0 && lastActive < decoder.Frames.Count - 1)
                {
                    rawFrameLimit = lastActive + 1;
                }
            }

            IReadOnlyList<BitmapFrame> effectiveRawFrames = rawFrameLimit < decoder.Frames.Count
                ? decoder.Frames.Take(rawFrameLimit).ToList()
                : decoder.Frames;

            // 2. Pre-select the frames to keep based on Target FPS and MaxFrames
            var selectedIndices = GetSelectedFrameIndices(effectiveRawFrames, settings.TargetFps, settings.MaxFrames, settings.UniformSampling);

            // 3. Composite frames using a lightweight software buffer, only constructing BitmapSource for selected frames
            var compositedFrames = CompositeGifFramesInternal(decoder.Frames, selectedIndices);

            List<bool[]> resultFrames = [];
            int finalW = 0, finalH = 0;
            bool wasScaled = false;

            // 4. Convert each selected frame to 1-bit monochrome in-memory (zero disk I/O)
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

        public static List<BitmapSource> CompositeGifFrames(IReadOnlyList<BitmapFrame> rawFrames, int? maxFramesToProcess = null)
        {
            if (rawFrames.Count == 0) return [];
            return CompositeGifFramesInternal(rawFrames, selectedIndices: null, maxFramesToProcess: maxFramesToProcess);
        }

        private static List<BitmapSource> CompositeGifFramesInternal(
            IReadOnlyList<BitmapFrame> rawFrames,
            List<int>? selectedIndices,
            int? maxFramesToProcess = null)
        {
            var result = new List<BitmapSource>();
            if (rawFrames.Count == 0) return result;

            int maxProcessIndex = rawFrames.Count - 1;
            if (selectedIndices != null && selectedIndices.Count > 0)
            {
                maxProcessIndex = Math.Min(maxProcessIndex, selectedIndices.Max());
            }
            else if (maxFramesToProcess.HasValue && maxFramesToProcess.Value > 0)
            {
                maxProcessIndex = Math.Min(maxProcessIndex, maxFramesToProcess.Value - 1);
            }

            int canvasWidth = rawFrames[0].PixelWidth;
            int canvasHeight = rawFrames[0].PixelHeight;

            for (int i = 0; i <= maxProcessIndex; i++)
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
            for (int i = 0; i <= maxProcessIndex; i++)
            {
                int count = rawFrames[i].PixelWidth * rawFrames[i].PixelHeight;
                if (count > maxFramePixels) maxFramePixels = count;
            }
            uint[] framePixelBuffer = new uint[Math.Max(1, maxFramePixels)];

            for (int i = 0; i <= maxProcessIndex; i++)
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

        /// <summary>
        /// Finds the 0-based index of the last non-blank active frame in a GIF sequence.
        /// Returns (frames.Count - 1) if no trailing blank padding is detected (e.g. less than 2 trailing blank frames,
        /// or the entire sequence is blank).
        /// </summary>
        public static int FindLastActiveFrameIndex(IReadOnlyList<BitmapFrame> frames)
        {
            if (frames == null || frames.Count <= 1) return (frames?.Count ?? 0) - 1;

            int maxPixels = 0;
            for (int i = 0; i < frames.Count; i++)
            {
                int count = frames[i].PixelWidth * frames[i].PixelHeight;
                if (count > maxPixels) maxPixels = count;
            }
            byte[] buffer = new byte[Math.Max(1, maxPixels * 4)];

            // If the very last frame is not blank, no trailing blank frames exist
            if (!IsFrameBlank(frames[^1], buffer))
                return frames.Count - 1;

            int foundActive = -1;

            if (frames.Count <= 64)
            {
                // For small frame counts, scan directly backwards without skipping
                for (int i = frames.Count - 2; i >= 0; i--)
                {
                    if (!IsFrameBlank(frames[i], buffer))
                    {
                        foundActive = i;
                        break;
                    }
                }
            }
            else
            {
                const int Step = 16;
                // Step backwards from the end
                for (int i = frames.Count - 2; i >= 0; i -= Step)
                {
                    if (!IsFrameBlank(frames[i], buffer))
                    {
                        foundActive = i;
                        break;
                    }
                }

                if (foundActive < 0)
                {
                    // Check remainder between 0 and Step - 1
                    for (int i = Math.Min(Step - 1, frames.Count - 2); i >= 0; i--)
                    {
                        if (!IsFrameBlank(frames[i], buffer))
                        {
                            foundActive = i;
                            break;
                        }
                    }
                }

                if (foundActive >= 0)
                {
                    // Scan forward to find exact last active frame
                    for (int i = foundActive + 1; i < frames.Count; i++)
                    {
                        if (IsFrameBlank(frames[i], buffer))
                            break;
                        foundActive = i;
                    }
                }
            }

            if (foundActive < 0)
            {
                // Entire animation is blank (or no active frames detected) -> do not trim
                return frames.Count - 1;
            }

            int trailingBlanks = frames.Count - 1 - foundActive;
            // Only trim if there are at least 2 trailing blank frames (empty tail padding)
            return trailingBlanks >= 2 ? foundActive : frames.Count - 1;
        }

        private static bool IsFrameBlank(BitmapFrame frame, byte[] buffer)
        {
            int w = frame.PixelWidth;
            int h = frame.PixelHeight;
            if (w <= 0 || h <= 0) return true;

            int pixelCount = w * h;

            if (frame.Format == PixelFormats.Indexed8 || frame.Format == PixelFormats.Indexed4 || frame.Format == PixelFormats.Indexed2 || frame.Format == PixelFormats.Indexed1)
            {
                int stride = (w * frame.Format.BitsPerPixel + 7) / 8;
                int byteCount = stride * h;
                if (buffer.Length < byteCount) buffer = new byte[byteCount];

                frame.CopyPixels(buffer, stride, 0);

                byte firstByte = buffer[0];
                bool allSame = true;
                for (int i = 1; i < byteCount; i++)
                {
                    if (buffer[i] != firstByte)
                    {
                        allSame = false;
                        break;
                    }
                }

                if (allSame)
                {
                    var pal = frame.Palette;
                    if (pal != null && firstByte < pal.Colors.Count)
                    {
                        var col = pal.Colors[firstByte];
                        if (col.A == 0 || (col.R == 0 && col.G == 0 && col.B == 0))
                            return true;
                    }
                    else
                    {
                        return true;
                    }
                }
                return false;
            }

            int bgraBytes = pixelCount * 4;
            if (buffer.Length < bgraBytes) buffer = new byte[bgraBytes];

            BitmapSource source = frame;
            if (frame.Format != PixelFormats.Bgra32)
            {
                source = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            }
            source.CopyPixels(buffer, w * 4, 0);

            for (int i = 0; i < bgraBytes; i += 4)
            {
                byte r = buffer[i + 2];
                byte g = buffer[i + 1];
                byte b = buffer[i];
                byte a = buffer[i + 3];

                if (a > 0 && (r > 0 || g > 0 || b > 0))
                {
                    return false;
                }
            }

            return true;
        }
    }
}