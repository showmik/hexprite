using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hexprite.Core;

namespace Hexprite.Services
{
    public class ExportService : IExportService
    {
        public void Export(string targetPath, SpriteState spriteState, ImageExportSettings settings)
        {
            if (settings.Format == ImageExportFormat.PngSequence)
            {
                ExportPngSequence(targetPath, spriteState, settings);
                return;
            }

            if (settings.Format == ImageExportFormat.Gif)
            {
                ExportGif(targetPath, spriteState, settings);
                return;
            }

            // Image Export (PNG / BMP)
            if (settings.ExportAllFramesAsSeparateFiles)
            {
                ExportSeparateImages(targetPath, spriteState, settings);
            }
            else
            {
                var bmp = CreateBitmapForImageExport(spriteState, settings);
                SaveBitmap(bmp, targetPath, settings.Format);
            }
        }

        public void ExportBitmaps(string targetPath, IReadOnlyList<BitmapSource> frames, ImageExportSettings settings)
        {
            ArgumentNullException.ThrowIfNull(frames);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
            if (frames.Count == 0) return;

            if (settings.Format == ImageExportFormat.PngSequence)
            {
                ExportColorPngSequence(targetPath, frames);
                return;
            }

            if (settings.Format == ImageExportFormat.Gif)
            {
                ExportColorGif(targetPath, frames, settings);
                return;
            }

            if (settings.ExportAllFramesAsSpritesheet && frames.Count > 1)
            {
                var stitched = StitchColorFrames(frames);
                SaveBitmap(stitched, targetPath, settings.Format);
            }
            else
            {
                SaveBitmap(frames[0], targetPath, settings.Format);
            }
        }

        private static void ExportColorPngSequence(string baseTargetPath, IReadOnlyList<BitmapSource> frames)
        {
            string dir = Path.GetDirectoryName(baseTargetPath) ?? "";
            string name = Path.GetFileNameWithoutExtension(baseTargetPath);

            for (int i = 0; i < frames.Count; i++)
            {
                string path = Path.Combine(dir, string.Create(CultureInfo.InvariantCulture, $"{name}_{i + 1:D3}.png"));
                SaveBitmap(frames[i], path, ImageExportFormat.Png);
            }
        }

        private static BitmapSource StitchColorFrames(IReadOnlyList<BitmapSource> frames)
        {
            int totalW = 0;
            int maxH = 0;
            foreach (var frame in frames)
            {
                totalW += frame.PixelWidth;
                maxH = Math.Max(maxH, frame.PixelHeight);
            }

            if (totalW <= 0 || maxH <= 0) return frames[0];

            var visual = new DrawingVisual();
            using (var ctx = visual.RenderOpen())
            {
                double currentX = 0;
                foreach (var frame in frames)
                {
                    ctx.DrawImage(frame, new System.Windows.Rect(currentX, 0, frame.PixelWidth, frame.PixelHeight));
                    currentX += frame.PixelWidth;
                }
            }

            var rtb = new RenderTargetBitmap(totalW, maxH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        private static void ExportColorGif(string path, IReadOnlyList<BitmapSource> frames, ImageExportSettings settings)
        {
            if (frames.Count == 0) return;

            int w = 0;
            int h = 0;
            foreach (var f in frames)
            {
                w = Math.Max(w, f.PixelWidth);
                h = Math.Max(h, f.PixelHeight);
            }
            if (w <= 0 || h <= 0) return;

            // Convert all frames to standard Bgra32 byte buffers
            var rawFrames = new System.Collections.Generic.List<byte[]>(frames.Count);
            var uniqueColors = new System.Collections.Generic.HashSet<uint>();

            foreach (var frame in frames)
            {
                var formatted = frame.Format == PixelFormats.Bgra32
                    ? frame
                    : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

                int fw = frame.PixelWidth;
                int fh = frame.PixelHeight;
                int fStride = fw * 4;

                byte[] raw = new byte[w * h * 4];
                if (fw == w && fh == h)
                {
                    formatted.CopyPixels(raw, w * 4, 0);
                }
                else
                {
                    byte[] fRaw = new byte[fStride * fh];
                    formatted.CopyPixels(fRaw, fStride, 0);
                    for (int y = 0; y < fh; y++)
                    {
                        Buffer.BlockCopy(fRaw, y * fStride, raw, y * (w * 4), fStride);
                    }
                }

                rawFrames.Add(raw);

                for (int p = 0; p < raw.Length; p += 4)
                {
                    uint color = ((uint)raw[p + 3] << 24) | ((uint)raw[p + 2] << 16) | ((uint)raw[p + 1] << 8) | raw[p];
                    uniqueColors.Add(color);
                }
            }

            // Build palette (max 256 colors)
            var paletteList = new System.Collections.Generic.List<Color>();
            var colorToIndex = new System.Collections.Generic.Dictionary<uint, byte>();

            bool hasTransparentPixel = uniqueColors.Any(u => (byte)(u >> 24) == 0);
            if (hasTransparentPixel)
            {
                paletteList.Add(Colors.Transparent);
            }

            foreach (uint u in uniqueColors)
            {
                byte a = (byte)(u >> 24);
                if (a == 0)
                {
                    colorToIndex[u] = 0;
                    continue;
                }

                if (paletteList.Count < 256)
                {
                    var c = Color.FromArgb(a, (byte)(u >> 16), (byte)(u >> 8), (byte)u);
                    colorToIndex[u] = (byte)paletteList.Count;
                    paletteList.Add(c);
                }
            }

            // Fallback if empty
            if (paletteList.Count == 0)
            {
                paletteList.Add(Colors.Transparent);
                paletteList.Add(Colors.Black);
            }

            SafeFileIo.WriteStreamAtomic(path, fs =>
            {
                using var encoder = new GifEncoder(fs, (ushort)w, (ushort)h);
                encoder.SetPalette([.. paletteList]);

                if (rawFrames.Count > 1)
                {
                    if (settings.GifLoopInfinite)
                        encoder.SetLoop(0);
                    else if (settings.GifLoopCount > 1)
                        encoder.SetLoop((ushort)Math.Clamp(settings.GifLoopCount, 1, 65535));
                }

                int fps = Math.Clamp(settings.GifFps, 1, 100);
                int delayMs = 1000 / fps;
                ushort delayCentiseconds = (ushort)Math.Clamp((int)Math.Round(delayMs / 10.0, MidpointRounding.AwayFromZero), 1, 65535);
                byte? transIdx = hasTransparentPixel ? (byte)0 : null;

                foreach (var raw in rawFrames)
                {
                    byte[] indexed = new byte[w * h];
                    for (int i = 0; i < indexed.Length; i++)
                    {
                        int p = i * 4;
                        uint u = ((uint)raw[p + 3] << 24) | ((uint)raw[p + 2] << 16) | ((uint)raw[p + 1] << 8) | raw[p];

                        if (colorToIndex.TryGetValue(u, out byte idx))
                        {
                            indexed[i] = idx;
                        }
                        else
                        {
                            // Nearest color match for 256+ colors
                            byte bestIdx = 0;
                            int bestDist = int.MaxValue;
                            byte cr = (byte)(u >> 16);
                            byte cg = (byte)(u >> 8);
                            byte cb = (byte)u;

                            int startIndex = hasTransparentPixel ? 1 : 0;
                            for (int palIdx = startIndex; palIdx < paletteList.Count; palIdx++)
                            {
                                var pc = paletteList[palIdx];
                                int dist = Math.Abs(cr - pc.R) + Math.Abs(cg - pc.G) + Math.Abs(cb - pc.B);
                                if (dist < bestDist)
                                {
                                    bestDist = dist;
                                    bestIdx = (byte)palIdx;
                                }
                            }
                            indexed[i] = bestIdx;
                        }
                    }

                    encoder.AddFrame(indexed, delayCentiseconds, transparentIndex: transIdx);
                }
            });
        }

        private static void ExportSeparateImages(string baseTargetPath, SpriteState spriteState, ImageExportSettings settings)
        {
            string dir = Path.GetDirectoryName(baseTargetPath) ?? "";
            string name = Path.GetFileNameWithoutExtension(baseTargetPath);
            string ext = Path.GetExtension(baseTargetPath);
            if (string.IsNullOrEmpty(ext)) ext = settings.Format == ImageExportFormat.Bmp ? ".bmp" : ".png";

            for (int i = 0; i < spriteState.Frames.Count; i++)
            {
                var bmp = CreateBitmapForFrame(spriteState, i, settings);
                string path = Path.Combine(dir, string.Create(CultureInfo.InvariantCulture, $"{name}_{i + 1:D3}{ext}"));
                SaveBitmap(bmp, path, settings.Format);
            }
        }

        private static void ExportPngSequence(string baseTargetPath, SpriteState spriteState, ImageExportSettings settings)
        {
            string dir = Path.GetDirectoryName(baseTargetPath) ?? "";
            string name = Path.GetFileNameWithoutExtension(baseTargetPath);
            
            for (int i = 0; i < spriteState.Frames.Count; i++)
            {
                var bmp = CreateBitmapForFrame(spriteState, i, settings);
                string path = Path.Combine(dir, string.Create(CultureInfo.InvariantCulture, $"{name}_{i + 1:D3}.png"));
                SaveBitmap(bmp, path, ImageExportFormat.Png);
            }
        }

        private static void SaveBitmap(BitmapSource bmp, string path, ImageExportFormat format)
        {
            BitmapEncoder encoder = format == ImageExportFormat.Bmp 
                ? new BmpBitmapEncoder() 
                : new PngBitmapEncoder();

            encoder.Frames.Add(BitmapFrame.Create(bmp));

            SafeFileIo.WriteStreamAtomic(path, stream => encoder.Save(stream));
        }

        private static BitmapSource CreateBitmapForImageExport(SpriteState spriteState, ImageExportSettings settings)
        {
            if (settings.ExportAllFramesAsSpritesheet)
            {
                int w = spriteState.Width;
                int h = spriteState.Height;
                int scale = settings.Scale;
                int frames = spriteState.Frames.Count;

                int finalW = w * scale * frames;
                int finalH = h * scale;

                var pixels = new uint[finalW * finalH];
                
                (Color bgColor, Color fgColor, Color gridColor) = GetColors(settings);

                for (int i = 0; i < frames; i++)
                {
                    bool[] frameData = spriteState.CompositeFramePixels(i, isExport: true);
                    int offsetX = i * w * scale;
                    RenderPixelsToBuffer(frameData, w, h, scale, settings.ShowGrid, bgColor, fgColor, gridColor, pixels, finalW, finalH, offsetX, 0);
                }

                return BitmapSource.Create(finalW, finalH, 96, 96, PixelFormats.Bgra32, palette: null, pixels, finalW * 4);
            }

            return CreateBitmapForFrame(spriteState, spriteState.ActiveFrameIndex, settings);
        }

        private static BitmapSource CreateBitmapForFrame(SpriteState spriteState, int frameIndex, ImageExportSettings settings)
        {
            int w = spriteState.Width;
            int h = spriteState.Height;
            int scale = settings.Scale;

            int finalW = w * scale;
            int finalH = h * scale;

            var pixels = new uint[finalW * finalH];
            (Color bgColor, Color fgColor, Color gridColor) = GetColors(settings);

            bool[] frameData = spriteState.CompositeFramePixels(frameIndex, isExport: true);
            RenderPixelsToBuffer(frameData, w, h, scale, settings.ShowGrid, bgColor, fgColor, gridColor, pixels, finalW, finalH, 0, 0);

            return BitmapSource.Create(finalW, finalH, 96, 96, PixelFormats.Bgra32, palette: null, pixels, finalW * 4);
        }

        private static void ExportGif(string path, SpriteState spriteState, ImageExportSettings settings)
        {
            int w = Math.Max(1, spriteState.Width);
            int h = Math.Max(1, spriteState.Height);
            int scale = Math.Max(1, settings.Scale);
            int finalW = w * scale;
            int finalH = h * scale;

            (Color bgColor, Color fgColor, Color gridColor) = GetColors(settings);
            bool renderGrid = settings.ShowGrid && scale >= 2;
            Color[] palette = renderGrid
                ? [bgColor, fgColor, Blend(bgColor, gridColor)]
                : [bgColor, fgColor];

            SafeFileIo.WriteStreamAtomic(path, fs =>
            {
                using var encoder = new GifEncoder(fs, (ushort)finalW, (ushort)finalH);

                // Set palette
                encoder.SetPalette(palette);

                int frameCount = spriteState.Frames?.Count ?? 0;
                int totalFrames = settings.GifExportAllFrames && frameCount > 0 ? frameCount : 1;

                if (totalFrames > 1)
                {
                    if (settings.GifLoopInfinite)
                        encoder.SetLoop(0);
                    else if (settings.GifLoopCount > 1)
                        encoder.SetLoop((ushort)Math.Clamp(settings.GifLoopCount, 1, 65535));
                }

                int fps = Math.Clamp(settings.GifFps, 1, 100);
                int delayMs = 1000 / fps;

                if (settings.GifExportAllFrames && frameCount > 0)
                {
                    var frameIndices = GetExportFrameSequence(frameCount, spriteState.PlaybackDirection);
                    byte[]? prevFrameIndexed = null;

                    foreach (int i in frameIndices)
                    {
                        int multiplier = Math.Max(1, spriteState.Frames![i].DelayMultiplier);
                        int frameDelayMs = delayMs * multiplier;
                        ushort delayCentiseconds = (ushort)Math.Clamp((int)Math.Round(frameDelayMs / 10.0, MidpointRounding.AwayFromZero), 1, 65535);

                        bool[] frameData = spriteState.CompositeFramePixels(i, isExport: true);
                        byte[] currIndexed = RenderToIndexBuffer(frameData, w, h, scale, renderGrid);

                        if (settings.GifEnableDeltaOptimization && prevFrameIndexed != null && !settings.GifTransparentBackground)
                        {
                            var dirtyBounds = ComputeDirtyBounds(prevFrameIndexed, currIndexed, finalW, finalH);
                            if (dirtyBounds.HasValue)
                            {
                                var (minX, minY, subW, subH) = dirtyBounds.Value;
                                byte[] subPixels = ExtractSubRegion(currIndexed, finalW, minX, minY, subW, subH);
                                encoder.AddFrame(
                                    subPixels,
                                    delayCentiseconds,
                                    left: (ushort)minX,
                                    top: (ushort)minY,
                                    frameWidth: (ushort)subW,
                                    frameHeight: (ushort)subH,
                                    disposalMethod: 1); // 1 = Keep frame
                            }
                            else
                            {
                                // Completely identical frame: write 1x1 sub-region to advance timer
                                encoder.AddFrame(
                                    [currIndexed[0]],
                                    delayCentiseconds,
                                    left: 0,
                                    top: 0,
                                    frameWidth: 1,
                                    frameHeight: 1,
                                    disposalMethod: 1);
                            }
                        }
                        else
                        {
                            byte disposal = settings.GifEnableDeltaOptimization && !settings.GifTransparentBackground ? (byte)1 : (byte)2;
                            encoder.AddFrame(currIndexed, delayCentiseconds, disposalMethod: disposal);
                        }

                        prevFrameIndexed = currIndexed;
                    }
                }
                else
                {
                    int activeIdx = frameCount > 0 ? Math.Clamp(spriteState.ActiveFrameIndex, 0, frameCount - 1) : 0;
                    int multiplier = frameCount > 0 ? Math.Max(1, spriteState.Frames![activeIdx].DelayMultiplier) : 1;
                    int frameDelayMs = delayMs * multiplier;
                    ushort delayCentiseconds = (ushort)Math.Clamp((int)Math.Round(frameDelayMs / 10.0, MidpointRounding.AwayFromZero), 1, 65535);

                    bool[] frameData = frameCount > 0
                        ? spriteState.CompositeFramePixels(activeIdx, isExport: true)
                        : new bool[w * h];
                    byte[] indexed = RenderToIndexBuffer(frameData, w, h, scale, renderGrid);
                    encoder.AddFrame(indexed, delayCentiseconds);
                }

                encoder.Finish();
            });
        }

        internal static (int minX, int minY, int width, int height)? ComputeDirtyBounds(byte[] prev, byte[] curr, int w, int h)
        {
            int minX = w, maxX = -1, minY = h, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                int rowOffset = y * w;
                for (int x = 0; x < w; x++)
                {
                    int idx = rowOffset + x;
                    if (prev[idx] != curr[idx])
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            if (maxX < 0) return null; // No difference
            return (minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        internal static byte[] ExtractSubRegion(byte[] source, int srcW, int minX, int minY, int subW, int subH)
        {
            byte[] sub = new byte[subW * subH];
            for (int y = 0; y < subH; y++)
            {
                int srcOffset = (minY + y) * srcW + minX;
                int dstOffset = y * subW;
                Array.Copy(source, srcOffset, sub, dstOffset, subW);
            }
            return sub;
        }

        internal static List<int> GetExportFrameSequence(int frameCount, PlaybackDirection direction)
        {
            var frameIndices = new List<int>();
            if (frameCount > 0)
            {
                if (direction == PlaybackDirection.Reverse)
                {
                    for (int i = frameCount - 1; i >= 0; i--) frameIndices.Add(i);
                }
                else if (direction == PlaybackDirection.PingPong && frameCount > 2)
                {
                    for (int i = 0; i < frameCount; i++) frameIndices.Add(i);
                    for (int i = frameCount - 2; i > 0; i--) frameIndices.Add(i);
                }
                else
                {
                    for (int i = 0; i < frameCount; i++) frameIndices.Add(i);
                }
            }
            return frameIndices;
        }

        private static (Color bg, Color fg, Color grid) GetColors(ImageExportSettings settings)
        {
            Color bg, fg, grid;
            if (settings.ColorMode == ExportColorMode.CustomPalette && settings.CustomForegroundColor.HasValue)
            {
                bg = settings.CustomBackgroundColor ?? (settings.Format == ImageExportFormat.Gif && settings.GifTransparentBackground ? Colors.Transparent : Colors.Black);
                fg = settings.CustomForegroundColor.Value;
                grid = Color.FromArgb(40, fg.R, fg.G, fg.B);
            }
            else if (settings.ColorMode == ExportColorMode.DisplayPreview)
            {
                bg = Color.FromRgb(0, 0, 0); // Black
                fg = Color.FromRgb(0, 210, 255); // OLED Blue
                grid = Color.FromArgb(40, 0, 210, 255);
            }
            else
            {
                if (settings.Format == ImageExportFormat.Gif)
                {
                    bg = settings.GifTransparentBackground ? Colors.Transparent : Colors.White;
                }
                else
                {
                    bg = settings.Format == ImageExportFormat.Bmp ? Colors.White : Colors.Transparent;
                }
                fg = Colors.Black;
                grid = Color.FromArgb(40, 0, 0, 0);
            }
            return (bg, fg, grid);
        }

        private static void RenderPixelsToBuffer(bool[] source, int w, int h, int scale, bool showGrid, Color bg, Color fg, Color grid, uint[] target, int targetW, int targetH, int offsetX, int offsetY)
        {
            uint bgArgb = ToArgb(bg);
            uint fgArgb = ToArgb(fg);
            uint gridArgb = ToArgb(Blend(bg, grid)); // Simple pre-blend over bg

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool isSet = source[y * w + x];
                    uint color = isSet ? fgArgb : bgArgb;

                    // Fill scaled rect
                    for (int sy = 0; sy < scale; sy++)
                    {
                        for (int sx = 0; sx < scale; sx++)
                        {
                            int ty = offsetY + y * scale + sy;
                            int tx = offsetX + x * scale + sx;

                            if (showGrid && scale >= 2 && !isSet && (sx == 0 || sy == 0))
                            {
                                target[ty * targetW + tx] = gridArgb;
                            }
                            else
                            {
                                target[ty * targetW + tx] = color;
                            }
                        }
                    }
                }
            }
        }

        private static byte[] RenderToIndexBuffer(bool[] source, int w, int h, int scale, bool showGrid = false)
        {
            int finalW = w * scale;
            int finalH = h * scale;
            byte[] indexed = new byte[finalW * finalH];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool isSet = source[y * w + x];
                    byte colorIndex = isSet ? (byte)1 : (byte)0;

                    for (int sy = 0; sy < scale; sy++)
                    {
                        for (int sx = 0; sx < scale; sx++)
                        {
                            int ty = y * scale + sy;
                            int tx = x * scale + sx;

                            if (showGrid && scale >= 2 && !isSet && (sx == 0 || sy == 0))
                            {
                                indexed[ty * finalW + tx] = 2;
                            }
                            else
                            {
                                indexed[ty * finalW + tx] = colorIndex;
                            }
                        }
                    }
                }
            }
            return indexed;
        }

        private static uint ToArgb(Color c)
        {
            return (uint)((c.A << 24) | (c.R << 16) | (c.G << 8) | c.B);
        }

        private static Color Blend(Color bg, Color overlay)
        {
            if (overlay.A == 0) return bg;
            if (overlay.A == 255) return overlay;

            float alpha = overlay.A / 255f;
            float invAlpha = 1f - alpha;

            byte r = (byte)(overlay.R * alpha + bg.R * invAlpha);
            byte g = (byte)(overlay.G * alpha + bg.G * invAlpha);
            byte b = (byte)(overlay.B * alpha + bg.B * invAlpha);
            byte a = (byte)Math.Min(255, bg.A + overlay.A);

            return Color.FromArgb(a, r, g, b);
        }
    }
}
