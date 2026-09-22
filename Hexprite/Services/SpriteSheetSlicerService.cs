using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hexprite.Core;

namespace Hexprite.Services
{
    public class SpriteSheetSlicerService : ISpriteSheetSlicerService
    {
        public IReadOnlyList<Int32Rect> CalculateSliceRects(BitmapSource source, SpriteSheetSliceSettings settings)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(settings);

            int imgW = Math.Max(1, source.PixelWidth);
            int imgH = Math.Max(1, source.PixelHeight);

            int frameW = Math.Clamp(settings.FrameWidth, 1, imgW);
            int frameH = Math.Clamp(settings.FrameHeight, 1, imgH);

            int offX = Math.Clamp(settings.OffsetX, 0, Math.Max(0, imgW - 1));
            int offY = Math.Clamp(settings.OffsetY, 0, Math.Max(0, imgH - 1));
            int spaceX = Math.Max(0, settings.SpacingX);
            int spaceY = Math.Max(0, settings.SpacingY);

            int cols = 1;
            int rows = 1;

            if (settings.Layout == SpriteSheetLayout.AutoDetect)
            {
                int alphaThresh = Math.Clamp(settings.AlphaThreshold > 0 ? settings.AlphaThreshold : 10, 1, 255);
                var islandRects = DetectAlphaIslands(source, alphaThresh);
                int maxF = Math.Clamp(settings.MaxFrames, 1, 512);
                if (islandRects.Count > maxF)
                {
                    islandRects = [.. System.Linq.Enumerable.Take(islandRects, maxF)];
                }
                return islandRects;
            }

            switch (settings.Layout)
            {
                case SpriteSheetLayout.HorizontalStrip:
                    int availW = Math.Max(0, imgW - offX);
                    int stepX = frameW + spaceX;
                    cols = stepX > 0 ? Math.Max(1, (availW + spaceX) / stepX) : 1;
                    rows = 1;
                    break;

                case SpriteSheetLayout.VerticalStrip:
                    int availH = Math.Max(0, imgH - offY);
                    int stepY = frameH + spaceY;
                    cols = 1;
                    rows = stepY > 0 ? Math.Max(1, (availH + spaceY) / stepY) : 1;
                    break;

                case SpriteSheetLayout.Grid:
                    int gAvailW = Math.Max(0, imgW - offX);
                    int gAvailH = Math.Max(0, imgH - offY);
                    int gStepX = frameW + spaceX;
                    int gStepY = frameH + spaceY;

                    if (settings.Columns > 0)
                    {
                        cols = settings.Columns;
                        if (settings.FrameWidth <= 0)
                        {
                            frameW = Math.Max(1, (gAvailW - (cols - 1) * spaceX) / cols);
                        }
                    }
                    else
                    {
                        cols = gStepX > 0 ? Math.Max(1, (gAvailW + spaceX) / gStepX) : 1;
                    }

                    if (settings.Rows > 0)
                    {
                        rows = settings.Rows;
                        if (settings.FrameHeight <= 0)
                        {
                            frameH = Math.Max(1, (gAvailH - (rows - 1) * spaceY) / rows);
                        }
                    }
                    else
                    {
                        rows = gStepY > 0 ? Math.Max(1, (gAvailH + spaceY) / gStepY) : 1;
                    }
                    break;
            }

            int maxFrames = Math.Clamp(settings.MaxFrames, 1, 512);
            var rects = new List<Int32Rect>();

            if (settings.Order == SliceOrder.ColumnMajor)
            {
                for (int c = 0; c < cols && rects.Count < maxFrames; c++)
                {
                    for (int r = 0; r < rows && rects.Count < maxFrames; r++)
                    {
                        int x = offX + c * (frameW + spaceX);
                        int y = offY + r * (frameH + spaceY);
                        if (x + frameW <= imgW && y + frameH <= imgH)
                        {
                            rects.Add(new Int32Rect(x, y, frameW, frameH));
                        }
                    }
                }
            }
            else
            {
                for (int r = 0; r < rows && rects.Count < maxFrames; r++)
                {
                    for (int c = 0; c < cols && rects.Count < maxFrames; c++)
                    {
                        int x = offX + c * (frameW + spaceX);
                        int y = offY + r * (frameH + spaceY);
                        if (x + frameW <= imgW && y + frameH <= imgH)
                        {
                            rects.Add(new Int32Rect(x, y, frameW, frameH));
                        }
                    }
                }
            }

            if (settings.SkipEmptyFrames)
            {
                rects = [.. PruneEmptyRects(source, rects, 0)];
            }

            if (rects.Count > maxFrames)
            {
                rects = [.. System.Linq.Enumerable.Take(rects, maxFrames)];
            }

            return rects;
        }

        public SpriteState SliceToAnimationSprite(BitmapSource source, SpriteSheetSliceSettings settings)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(settings);

            var rects = CalculateSliceRects(source, settings);
            int frameW = Math.Clamp(settings.FrameWidth, 1, Math.Max(1, source.PixelWidth));
            int frameH = Math.Clamp(settings.FrameHeight, 1, Math.Max(1, source.PixelHeight));

            int targetW;
            int targetH;

            if (settings.CanvasMode == SliceCanvasMode.FitFrame)
            {
                if (rects.Count > 0)
                {
                    targetW = Math.Clamp(rects.Max(r => r.Width), 1, SpriteState.MaxDimension);
                    targetH = Math.Clamp(rects.Max(r => r.Height), 1, SpriteState.MaxDimension);
                }
                else
                {
                    targetW = frameW;
                    targetH = frameH;
                }
            }
            else
            {
                targetW = Math.Clamp(settings.CanvasWidth, 1, SpriteState.MaxDimension);
                targetH = Math.Clamp(settings.CanvasHeight, 1, SpriteState.MaxDimension);
            }

            var sprite = new SpriteState(targetW, targetH)
            {
                IsAnimationEnabled = true,
                FrameRateFps = Math.Clamp(settings.Fps, 1, 60),
            };
            sprite.Frames.Clear();

            var formattedBmp = new FormatConvertedBitmap(source, PixelFormats.Bgra32, destinationPalette: null, 0);

            var convSettings = new BitmapImportSettings
            {
                MaxDimension = Math.Max(targetW, targetH),
                DitheringAlgorithm = settings.DitheringAlgorithm,
                DitherAmount = Math.Clamp(settings.DitherAmount, 0, 100),
                Threshold = Math.Clamp(settings.BrightnessThreshold, 0, 255),
                AlphaThreshold = Math.Clamp(settings.AlphaThreshold, 0, 255),
                Invert = settings.InvertColors,
                ScalingMode = BitmapScalingMode.NearestNeighbor,
                UseSerpentineScanning = settings.UseSerpentineScanning,
                UseGammaCorrection = settings.UseGammaCorrection,
                Contrast = Math.Clamp(settings.Contrast, -100, 100),
                Brightness = Math.Clamp(settings.Brightness, -100, 100),
            };

            int frameIdx = 0;
            foreach (var rect in rects)
            {
                BitmapSource sliceSource = new CroppedBitmap(formattedBmp, rect);

                // Apply aspect scaling transformation if requested in fixed-canvas mode
                if (settings.CanvasMode == SliceCanvasMode.FixedCanvas && settings.ScalingMode != SliceScalingMode.Crop1To1)
                {
                    sliceSource = ApplySliceScaling(sliceSource, rect.Width, rect.Height, targetW, targetH, settings.ScalingMode);
                }

                convSettings.MaxDimension = Math.Max(sliceSource.PixelWidth, sliceSource.PixelHeight);
                var (Pixels, Width, Height, WasScaled) = BitmapToMonochromeConverter.ConvertBitmapSource(sliceSource, convSettings);

                var frame = new FrameState
                {
                    Name = string.Create(CultureInfo.InvariantCulture, $"Frame {frameIdx + 1}"),
                };

                bool[] framePixels = new bool[targetW * targetH];
                int srcW = Width;
                int srcH = Height;

                int copyW = Math.Min(targetW, srcW);
                int copyH = Math.Min(targetH, srcH);

                int destStartX = 0;
                int srcStartX = 0;
                int destStartY = 0;
                int srcStartY = 0;

                switch (settings.Alignment)
                {
                    case SliceCanvasAlignment.Center:
                        destStartX = Math.Max(0, (targetW - srcW) / 2);
                        srcStartX = Math.Max(0, (srcW - targetW) / 2);
                        destStartY = Math.Max(0, (targetH - srcH) / 2);
                        srcStartY = Math.Max(0, (srcH - targetH) / 2);
                        break;
                    case SliceCanvasAlignment.TopLeft:
                        destStartX = 0;
                        srcStartX = 0;
                        destStartY = 0;
                        srcStartY = 0;
                        break;
                    case SliceCanvasAlignment.TopRight:
                        destStartX = Math.Max(0, targetW - srcW);
                        srcStartX = Math.Max(0, srcW - targetW);
                        destStartY = 0;
                        srcStartY = 0;
                        break;
                    case SliceCanvasAlignment.BottomLeft:
                        destStartX = 0;
                        srcStartX = 0;
                        destStartY = Math.Max(0, targetH - srcH);
                        srcStartY = Math.Max(0, srcH - targetH);
                        break;
                    case SliceCanvasAlignment.BottomRight:
                        destStartX = Math.Max(0, targetW - srcW);
                        srcStartX = Math.Max(0, srcW - targetW);
                        destStartY = Math.Max(0, targetH - srcH);
                        srcStartY = Math.Max(0, srcH - targetH);
                        break;
                }

                for (int py = 0; py < copyH; py++)
                {
                    for (int px = 0; px < copyW; px++)
                    {
                        int destIdx = (destStartY + py) * targetW + (destStartX + px);
                        int srcIdx = (srcStartY + py) * srcW + (srcStartX + px);
                        if (destIdx >= 0 && destIdx < framePixels.Length && srcIdx >= 0 && srcIdx < Pixels.Length)
                        {
                            framePixels[destIdx] = Pixels[srcIdx];
                        }
                    }
                }

                frame.LayerPixels.Add(new MonochromePixelBuffer(framePixels));
                sprite.Frames.Add(frame);
                frameIdx++;
            }

            // Auto-trim trailing completely empty frames if enabled
            if (settings.AutoTrimEmptyFrames && sprite.Frames.Count > 1)
            {
                while (sprite.Frames.Count > 1)
                {
                    var lastFrame = sprite.Frames[^1];
                    bool hasPixels = false;
                    foreach (var buf in lastFrame.LayerPixels)
                    {
                        var d = buf.GetMonochromeData();
                        for (int i = 0; i < d.Length; i++)
                        {
                            if (d[i]) { hasPixels = true; break; }
                        }
                        if (hasPixels) break;
                    }

                    if (!hasPixels)
                    {
                        sprite.Frames.RemoveAt(sprite.Frames.Count - 1);
                    }
                    else
                    {
                        break;
                    }
                }
            }

            if (sprite.Frames.Count == 0)
            {
                var fallback = new FrameState { Name = "Frame 1" };
                fallback.LayerPixels.Add(new MonochromePixelBuffer(targetW * targetH));
                sprite.Frames.Add(fallback);
            }

            sprite.NormalizeLayerState();
            sprite.ActiveFrameIndex = 0;
            return sprite;
        }

        private static BitmapSource ApplySliceScaling(BitmapSource slice, int srcW, int srcH, int targetW, int targetH, SliceScalingMode mode)
        {
            if (srcW <= 0 || srcH <= 0 || targetW <= 0 || targetH <= 0) return slice;

            double sx = 1.0;
            double sy = 1.0;

            switch (mode)
            {
                case SliceScalingMode.FitAspect:
                    double fitScale = Math.Min((double)targetW / srcW, (double)targetH / srcH);
                    sx = fitScale;
                    sy = fitScale;
                    break;

                case SliceScalingMode.FillAspect:
                    double fillScale = Math.Max((double)targetW / srcW, (double)targetH / srcH);
                    sx = fillScale;
                    sy = fillScale;
                    break;

                case SliceScalingMode.Stretch:
                    sx = (double)targetW / srcW;
                    sy = (double)targetH / srcH;
                    break;

                default:
                    return slice;
            }

            if (Math.Abs(sx - 1.0) < 0.001 && Math.Abs(sy - 1.0) < 0.001)
            {
                return slice;
            }

            var transform = new ScaleTransform(sx, sy);
            var transformed = new TransformedBitmap(slice, transform);
            if (transformed.CanFreeze) transformed.Freeze();
            return transformed;
        }

        public IReadOnlyList<SpriteState> SliceToIndividualSprites(BitmapSource source, SpriteSheetSliceSettings settings)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(settings);

            var animSprite = SliceToAnimationSprite(source, settings);
            var result = new List<SpriteState>();

            for (int i = 0; i < animSprite.Frames.Count; i++)
            {
                var single = new SpriteState(animSprite.Width, animSprite.Height)
                {
                    IsAnimationEnabled = false,
                };
                single.Frames.Clear();

                var frameClone = animSprite.Frames[i].Clone();
                frameClone.Name = string.Create(CultureInfo.InvariantCulture, $"Sprite {i + 1}");
                single.Frames.Add(frameClone);
                single.NormalizeLayerState();
                result.Add(single);
            }

            return result;
        }

        public (int SuggestedFrameWidth, int SuggestedFrameHeight, int Columns, int Rows) DetectGrid(BitmapSource source)
        {
            ArgumentNullException.ThrowIfNull(source);

            int w = source.PixelWidth;
            int h = source.PixelHeight;
            if (w <= 0 || h <= 0)
            {
                return (32, 32, 1, 1);
            }

            // 1. Check if horizontal strip (w > h and aspect is whole number)
            if (w > h && w % h == 0)
            {
                return (h, h, w / h, 1);
            }

            // 2. Check if vertical strip (h > w and aspect is whole number)
            if (h > w && h % w == 0)
            {
                return (w, w, 1, h / w);
            }

            int[] descendingSizes = [128, 64, 48, 32, 24, 16, 8];

            // 3. If square (w == h), check standard square cell sizes in descending order
            if (w == h)
            {
                foreach (int size in descendingSizes)
                {
                    if (w % size == 0 && (w > size))
                    {
                        return (size, size, w / size, h / size);
                    }
                }
            }

            // 4. Check for Flipper Zero 128x64 or standard 2:1 aspect
            if (w >= 128 && h >= 64 && w % 128 == 0 && h % 64 == 0)
            {
                return (128, 64, w / 128, h / 64);
            }

            // 5. Check standard square cell sizes for any grid
            foreach (int size in descendingSizes)
            {
                if (w % size == 0 && h % size == 0 && (w > size || h > size))
                {
                    return (size, size, w / size, h / size);
                }
            }

            return (w, h, 1, 1);
        }

        public BitmapSource? CreateStripFromGif(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return null;

            try
            {
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);

                if (decoder.Frames.Count <= 1)
                    return null;

                var composited = GifToMonochromeConverter.CompositeGifFrames(decoder.Frames);
                if (composited.Count <= 1)
                    return null;

                int frameW = composited[0].PixelWidth;
                int frameH = composited[0].PixelHeight;
                int totalW = frameW * composited.Count;

                var writeable = new WriteableBitmap(totalW, frameH, 96, 96, PixelFormats.Bgra32, palette: null);

                for (int i = 0; i < composited.Count; i++)
                {
                    var frame = new FormatConvertedBitmap(composited[i], PixelFormats.Bgra32, destinationPalette: null, 0);
                    int stride = frameW * 4;
                    byte[] pixels = new byte[stride * frameH];
                    frame.CopyPixels(pixels, stride, 0);

                    writeable.WritePixels(
                        new Int32Rect(i * frameW, 0, frameW, frameH),
                        pixels,
                        stride,
                        0);
                }

                if (writeable.CanFreeze) writeable.Freeze();
                return writeable;
            }
            catch
            {
                return null;
            }
        }

        public (int FrameWidth, int FrameHeight) ComputeFrameSizeFromCount(int imageWidth, int imageHeight, int columns, int rows, int offsetX, int offsetY, int spacingX, int spacingY)
        {
            int availW = Math.Max(0, imageWidth - Math.Max(0, offsetX));
            int availH = Math.Max(0, imageHeight - Math.Max(0, offsetY));
            int frameW = columns > 0 ? Math.Max(1, (availW - (columns - 1) * Math.Max(0, spacingX)) / columns) : availW;
            int frameH = rows > 0 ? Math.Max(1, (availH - (rows - 1) * Math.Max(0, spacingY)) / rows) : availH;
            return (frameW, frameH);
        }

        public IReadOnlyList<Int32Rect> DetectAlphaIslands(BitmapSource source, int alphaThreshold = 10, int minIslandWidth = 2, int minIslandHeight = 2)
        {
            ArgumentNullException.ThrowIfNull(source);

            int w = source.PixelWidth;
            int h = source.PixelHeight;
            if (w <= 0 || h <= 0) return [];

            // Hardening: limit excessive memory allocation for gigantic images
            if (w > 4096 || h > 4096 || (long)w * h > 16_000_000)
            {
                return [new(0, 0, Math.Min(w, 128), Math.Min(h, 64))];
            }

            var formatted = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, destinationPalette: null, 0);

            int stride = w * 4;
            byte[] pixels = new byte[stride * h];
            formatted.CopyPixels(pixels, stride, 0);

            // Check if image has real alpha variance or is fully opaque (e.g. solid background)
            bool isFullyOpaque = true;
            for (int i = 0; i < w * h; i++)
            {
                if (pixels[i * 4 + 3] <= alphaThreshold)
                {
                    isFullyOpaque = false;
                    break;
                }
            }

            byte bgB = pixels[0];
            byte bgG = pixels[1];
            byte bgR = pixels[2];

            bool IsForeground(int pIdx)
            {
                byte a = pixels[pIdx * 4 + 3];
                if (a <= alphaThreshold) return false;
                if (isFullyOpaque)
                {
                    byte b = pixels[pIdx * 4 + 0];
                    byte g = pixels[pIdx * 4 + 1];
                    byte r = pixels[pIdx * 4 + 2];
                    int colorDiff = Math.Abs(b - bgB) + Math.Abs(g - bgG) + Math.Abs(r - bgR);
                    return colorDiff > 25;
                }
                return true;
            }

            bool[] visited = new bool[w * h];
            var islands = new List<Int32Rect>();
            var queue = new Queue<int>();

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    if (visited[idx]) continue;

                    if (!IsForeground(idx))
                    {
                        visited[idx] = true;
                        continue;
                    }

                    visited[idx] = true;
                    queue.Enqueue(idx);

                    int minX = x, maxX = x;
                    int minY = y, maxY = y;

                    while (queue.Count > 0)
                    {
                        int curr = queue.Dequeue();
                        int cx = curr % w;
                        int cy = curr / w;

                        if (cx < minX) minX = cx;
                        if (cx > maxX) maxX = cx;
                        if (cy < minY) minY = cy;
                        if (cy > maxY) maxY = cy;

                        // Check 4 neighbors
                        if (cy > 0)
                        {
                            int nIdx = (cy - 1) * w + cx;
                            if (!visited[nIdx])
                            {
                                visited[nIdx] = true;
                                if (IsForeground(nIdx)) queue.Enqueue(nIdx);
                            }
                        }
                        if (cy < h - 1)
                        {
                            int sIdx = (cy + 1) * w + cx;
                            if (!visited[sIdx])
                            {
                                visited[sIdx] = true;
                                if (IsForeground(sIdx)) queue.Enqueue(sIdx);
                            }
                        }
                        if (cx > 0)
                        {
                            int wIdx = cy * w + (cx - 1);
                            if (!visited[wIdx])
                            {
                                visited[wIdx] = true;
                                if (IsForeground(wIdx)) queue.Enqueue(wIdx);
                            }
                        }
                        if (cx < w - 1)
                        {
                            int eIdx = cy * w + (cx + 1);
                            if (!visited[eIdx])
                            {
                                visited[eIdx] = true;
                                if (IsForeground(eIdx)) queue.Enqueue(eIdx);
                            }
                        }
                    }

                    int islandW = maxX - minX + 1;
                    int islandH = maxY - minY + 1;

                    if (islandW >= minIslandWidth && islandH >= minIslandHeight)
                    {
                        islands.Add(new Int32Rect(minX, minY, islandW, islandH));
                    }
                }
            }

            // Sort islands in reading order (top-to-bottom, left-to-right) with row tolerance
            islands.Sort((a, b) =>
            {
                int rowTol = Math.Max(2, Math.Min(a.Height, b.Height) / 2);
                if (Math.Abs(a.Y - b.Y) <= rowTol)
                {
                    return a.X.CompareTo(b.X);
                }
                return a.Y.CompareTo(b.Y);
            });

            return islands;
        }

        public IReadOnlyList<Int32Rect> PruneEmptyRects(BitmapSource source, IReadOnlyList<Int32Rect> rects, int alphaThreshold = 0)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(rects);
            if (rects.Count == 0) return rects;

            int w = source.PixelWidth;
            int h = source.PixelHeight;
            if (w <= 0 || h <= 0) return rects;

            var formatted = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, destinationPalette: null, 0);

            int stride = w * 4;
            byte[] pixels = new byte[stride * h];
            formatted.CopyPixels(pixels, stride, 0);

            bool isFullyOpaque = true;
            for (int i = 0; i < w * h; i++)
            {
                if (pixels[i * 4 + 3] <= alphaThreshold)
                {
                    isFullyOpaque = false;
                    break;
                }
            }

            byte bgB = pixels[0];
            byte bgG = pixels[1];
            byte bgR = pixels[2];

            bool IsForeground(int pIdx)
            {
                byte a = pixels[pIdx * 4 + 3];
                if (a <= alphaThreshold) return false;
                if (isFullyOpaque)
                {
                    byte b = pixels[pIdx * 4 + 0];
                    byte g = pixels[pIdx * 4 + 1];
                    byte r = pixels[pIdx * 4 + 2];
                    int colorDiff = Math.Abs(b - bgB) + Math.Abs(g - bgG) + Math.Abs(r - bgR);
                    return colorDiff > 25;
                }
                return true;
            }

            var nonEmpties = new List<Int32Rect>(rects.Count);

            foreach (var rect in rects)
            {
                bool hasContent = false;
                int rx = Math.Clamp(rect.X, 0, w - 1);
                int ry = Math.Clamp(rect.Y, 0, h - 1);
                int rw = Math.Clamp(rect.Width, 1, w - rx);
                int rh = Math.Clamp(rect.Height, 1, h - ry);

                for (int y = ry; y < ry + rh && !hasContent; y++)
                {
                    for (int x = rx; x < rx + rw; x++)
                    {
                        int pIdx = y * w + x;
                        if (IsForeground(pIdx))
                        {
                            hasContent = true;
                            break;
                        }
                    }
                }

                if (hasContent)
                {
                    nonEmpties.Add(rect);
                }
            }

            return nonEmpties;
        }
    }
}
