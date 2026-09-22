using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
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

            // 1. Composite all frames to ensure uniform size and handle delta-encoding
            var compositedFrames = CompositeGifFrames(decoder.Frames);

            // 2. Select the subset of frames based on Target FPS
            var framesToKeep = SelectFrames(compositedFrames, decoder.Frames, settings.TargetFps, settings.MaxFrames, settings.UniformSampling);

            List<bool[]> resultFrames = [];
            int finalW = 0, finalH = 0;
            bool wasScaled = false;

            // 3. Convert each selected frame to 1-bit monochrome in-memory (zero disk I/O)
            foreach (var frame in framesToKeep)
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
            var result = new List<BitmapSource>();
            if (rawFrames.Count == 0) return result;

            int width = rawFrames[0].PixelWidth;
            int height = rawFrames[0].PixelHeight;

            // Maintain the accumulated image. We use a DrawingVisual to draw into it.
            var accumTarget = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            RenderTargetBitmap? previousState = null;
            
            for (int i = 0; i < rawFrames.Count; i++)
            {
                var frame = rawFrames[i];
                int left = 0, top = 0;
                int disposalMethod = 0;

                if (frame.Metadata is BitmapMetadata meta)
                {
                    try { left = Convert.ToUInt16(meta.GetQuery("/imgdesc/Left"), CultureInfo.InvariantCulture); } catch { }
                    try { top = Convert.ToUInt16(meta.GetQuery("/imgdesc/Top"), CultureInfo.InvariantCulture); } catch { }
                    try { disposalMethod = Convert.ToByte(meta.GetQuery("/grctlext/Disposal"), CultureInfo.InvariantCulture); } catch { }
                }

                if (disposalMethod == 3)
                {
                    // Save the current state BEFORE drawing this frame, to restore it later.
                    previousState = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    var copyDv = new DrawingVisual();
                    using (var copyDc = copyDv.RenderOpen()) { copyDc.DrawImage(accumTarget, new Rect(0, 0, width, height)); }
                    previousState.Render(copyDv);
                }

                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawImage(accumTarget, new Rect(0, 0, width, height));
                    dc.DrawImage(frame, new Rect(left, top, frame.PixelWidth, frame.PixelHeight));
                }

                var nextTarget = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                nextTarget.Render(dv);
                
                var frozenFrame = new FormatConvertedBitmap(nextTarget, PixelFormats.Bgra32, destinationPalette: null, 0);
                frozenFrame.Freeze();
                result.Add(frozenFrame);

                // Apply disposal for the next frame
                if (disposalMethod == 2)
                {
                    // Restore to background (clear the area where the frame was drawn)
                    accumTarget = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    var clearDv = new DrawingVisual();
                    using (var clearDc = clearDv.RenderOpen())
                    {
                        var fullRect = new Rect(0, 0, width, height);
                        var frameRect = new Rect(left, top, frame.PixelWidth, frame.PixelHeight);
                        var clipGeometry = new CombinedGeometry(GeometryCombineMode.Exclude, 
                            new RectangleGeometry(fullRect), 
                            new RectangleGeometry(frameRect));
                        
                        clearDc.PushClip(clipGeometry);
                        clearDc.DrawImage(nextTarget, fullRect);
                        clearDc.Pop();
                    }
                    accumTarget.Render(clearDv);
                }
                else if (disposalMethod == 3 && previousState != null)
                {
                    // Restore to previous state
                    accumTarget = previousState;
                }
                else
                {
                    // Keep the frame
                    accumTarget = nextTarget;
                }
            }
            return result;
        }

        private static List<BitmapSource> SelectFrames(List<BitmapSource> composited, ReadOnlyCollection<BitmapFrame> rawFrames, int targetFps, int maxFrames, bool uniformSampling)
        {
            if (composited.Count == 0)
                return [];

            if (composited.Count == 1)
                return [composited[0]];

            // For GIF, metadata has delay in 10ms units. If not present, assume 100ms (10 fps).
            double totalDurationSec = 0;
            List<double> frameTimes = [];

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
            
            List<BitmapSource> selected = [];
            
            if (!uniformSampling)
            {
                int take = Math.Clamp(Math.Min(rawFrames.Count, maxFrames), 1, composited.Count);
                for (int i = 0; i < take; i++)
                    selected.Add(composited[i]);
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
                closestIdx = Math.Clamp(closestIdx, 0, composited.Count - 1);
                selected.Add(composited[closestIdx]);
            }

            return selected;
        }
    }
}