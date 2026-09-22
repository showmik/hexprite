using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace Hexprite.Services
{
    /// <summary>
    /// Quantizes 32-bit ARGB/BGRA pixel buffers into indexed 256-color palettes
    /// with optional Floyd-Steinberg error diffusion dithering.
    /// </summary>
    public static class ColorQuantizer
    {
        private record struct RgbColor(byte R, byte G, byte B, byte A)
        {
            public static RgbColor FromBgra(uint bgra)
            {
                byte b = (byte)(bgra & 0xFF);
                byte g = (byte)((bgra >> 8) & 0xFF);
                byte r = (byte)((bgra >> 16) & 0xFF);
                byte a = (byte)((bgra >> 24) & 0xFF);
                return new RgbColor(r, g, b, a);
            }

            public readonly Color ToMediaColor() => Color.FromArgb(A, R, G, B);
        }

        /// <summary>
        /// Quantizes a 32-bit BGRA image into a palette of up to 256 colors.
        /// </summary>
        public static (Color[] Palette, byte[] IndexedPixels) Quantize(
            uint[] bgraPixels,
            int width,
            int height,
            bool enableDithering = false,
            int maxColors = 256)
        {
            ArgumentNullException.ThrowIfNull(bgraPixels);
            if (width <= 0 || height <= 0)
            {
                return ([], []);
            }

            int count = width * height;
            if (bgraPixels.Length < count)
            {
                throw new ArgumentException("Pixel buffer length is smaller than width * height.", nameof(bgraPixels));
            }

            // 1. Gather color histogram and unique colors
            var uniqueMap = new Dictionary<uint, int>();
            for (int i = 0; i < count; i++)
            {
                uint pixel = bgraPixels[i];
                if (uniqueMap.TryGetValue(pixel, out int currentCount))
                {
                    uniqueMap[pixel] = currentCount + 1;
                }
                else
                {
                    uniqueMap[pixel] = 1;
                }
            }

            Color[] palette;
            if (uniqueMap.Count <= maxColors)
            {
                // Exact lossless mapping
                palette = [.. uniqueMap.Keys.Select(k => RgbColor.FromBgra(k).ToMediaColor())];
            }
            else
            {
                // Median cut quantization
                palette = MedianCut(uniqueMap, maxColors);
            }

            // Ensure palette has at least 1 color
            if (palette.Length == 0)
            {
                palette = [Colors.Black];
            }

            // 2. Map pixels to indices (with or without Floyd-Steinberg dithering)
            byte[] indexed = enableDithering
                ? DitherFloydSteinberg(bgraPixels, width, height, palette)
                : MapNearest(bgraPixels, count, palette);

            return (palette, indexed);
        }

        private static byte[] MapNearest(uint[] bgraPixels, int count, Color[] palette)
        {
            byte[] indexed = new byte[count];
            var lookupCache = new Dictionary<uint, byte>();

            for (int i = 0; i < count; i++)
            {
                uint bgra = bgraPixels[i];
                if (!lookupCache.TryGetValue(bgra, out byte index))
                {
                    var color = RgbColor.FromBgra(bgra);
                    index = (byte)FindNearestColor(color.R, color.G, color.B, palette);
                    lookupCache[bgra] = index;
                }
                indexed[i] = index;
            }

            return indexed;
        }

        private static byte[] DitherFloydSteinberg(uint[] bgraPixels, int width, int height, Color[] palette)
        {
            byte[] indexed = new byte[width * height];

            // Work buffers for floating-point error diffusion
            float[] rBuf = new float[width * height];
            float[] gBuf = new float[width * height];
            float[] bBuf = new float[width * height];

            for (int i = 0; i < width * height; i++)
            {
                var c = RgbColor.FromBgra(bgraPixels[i]);
                rBuf[i] = c.R;
                gBuf[i] = c.G;
                bBuf[i] = c.B;
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    byte curR = (byte)Math.Clamp((int)Math.Round(rBuf[idx], MidpointRounding.AwayFromZero), 0, 255);
                    byte curG = (byte)Math.Clamp((int)Math.Round(gBuf[idx], MidpointRounding.AwayFromZero), 0, 255);
                    byte curB = (byte)Math.Clamp((int)Math.Round(bBuf[idx], MidpointRounding.AwayFromZero), 0, 255);

                    int bestIdx = FindNearestColor(curR, curG, curB, palette);
                    indexed[idx] = (byte)bestIdx;

                    Color chosen = palette[bestIdx];
                    float errR = rBuf[idx] - chosen.R;
                    float errG = gBuf[idx] - chosen.G;
                    float errB = bBuf[idx] - chosen.B;

                    // Diffuse errors: (x+1, y) 7/16, (x-1, y+1) 3/16, (x, y+1) 5/16, (x+1, y+1) 1/16
                    void AddError(int px, int py, float factor)
                    {
                        if (px >= 0 && px < width && py >= 0 && py < height)
                        {
                            int targetIdx = py * width + px;
                            rBuf[targetIdx] += errR * factor;
                            gBuf[targetIdx] += errG * factor;
                            bBuf[targetIdx] += errB * factor;
                        }
                    }

                    AddError(x + 1, y, 7f / 16f);
                    AddError(x - 1, y + 1, 3f / 16f);
                    AddError(x, y + 1, 5f / 16f);
                    AddError(x + 1, y + 1, 1f / 16f);
                }
            }

            return indexed;
        }

        public static int FindNearestColor(byte r, byte g, byte b, Color[] palette)
        {
            int bestIndex = 0;
            int bestDistance = int.MaxValue;

            for (int i = 0; i < palette.Length; i++)
            {
                Color c = palette[i];
                int dr = r - c.R;
                int dg = g - c.G;
                int db = b - c.B;
                // Weighted Euclidean color difference for human eye perception
                int dist = (dr * dr * 2) + (dg * dg * 4) + (db * db * 3);
                if (dist < bestDistance)
                {
                    bestDistance = dist;
                    bestIndex = i;
                    if (dist == 0) break;
                }
            }

            return bestIndex;
        }

        private static Color[] MedianCut(Dictionary<uint, int> histogram, int maxColors)
        {
            var initialBox = new ColorBox([.. histogram.Keys]);
            var boxes = new List<ColorBox> { initialBox };

            while (boxes.Count < maxColors)
            {
                ColorBox? boxToSplit = boxes
                    .Where(b => b.Colors.Count > 1)
                    .OrderByDescending(b => b.Volume)
                    .FirstOrDefault();

                if (boxToSplit == null) break;

                boxes.Remove(boxToSplit);
                var (b1, b2) = boxToSplit.Split();
                boxes.Add(b1);
                boxes.Add(b2);
            }

            return [.. boxes.Select(b => b.AverageColor())];
        }

        private class ColorBox(List<uint> colors)
        {
            public List<uint> Colors { get; } = colors;

            private byte _minR, _maxR, _minG, _maxG, _minB, _maxB;
            private bool _boundsCalculated;

            private void EnsureBounds()
            {
                if (_boundsCalculated) return;
                _minR = _minG = _minB = 255;
                _maxR = _maxG = _maxB = 0;

                foreach (uint c in Colors)
                {
                    var rgb = RgbColor.FromBgra(c);
                    if (rgb.R < _minR) _minR = rgb.R;
                    if (rgb.R > _maxR) _maxR = rgb.R;
                    if (rgb.G < _minG) _minG = rgb.G;
                    if (rgb.G > _maxG) _maxG = rgb.G;
                    if (rgb.B < _minB) _minB = rgb.B;
                    if (rgb.B > _maxB) _maxB = rgb.B;
                }
                _boundsCalculated = true;
            }

            public int Volume
            {
                get
                {
                    EnsureBounds();
                    return (_maxR - _minR + 1) * (_maxG - _minG + 1) * (_maxB - _minB + 1);
                }
            }

            public (ColorBox, ColorBox) Split()
            {
                EnsureBounds();
                int rangeR = _maxR - _minR;
                int rangeG = _maxG - _minG;
                int rangeB = _maxB - _minB;

                int sortChannel = 0; // 0 = R, 1 = G, 2 = B
                if (rangeG >= rangeR && rangeG >= rangeB) sortChannel = 1;
                else if (rangeB >= rangeR && rangeB >= rangeG) sortChannel = 2;

                Colors.Sort((a, b) =>
                {
                    var ca = RgbColor.FromBgra(a);
                    var cb = RgbColor.FromBgra(b);
                    return sortChannel switch
                    {
                        1 => ca.G.CompareTo(cb.G),
                        2 => ca.B.CompareTo(cb.B),
                        _ => ca.R.CompareTo(cb.R),
                    };
                });

                int median = Colors.Count / 2;
                var list1 = Colors.GetRange(0, median);
                var list2 = Colors.GetRange(median, Colors.Count - median);

                return (new ColorBox(list1), new ColorBox(list2));
            }

            public Color AverageColor()
            {
                if (Colors.Count == 0) return System.Windows.Media.Colors.Black;

                long totalR = 0, totalG = 0, totalB = 0;
                foreach (uint c in Colors)
                {
                    var rgb = RgbColor.FromBgra(c);
                    totalR += rgb.R;
                    totalG += rgb.G;
                    totalB += rgb.B;
                }

                byte avgR = (byte)(totalR / Colors.Count);
                byte avgG = (byte)(totalG / Colors.Count);
                byte avgB = (byte)(totalB / Colors.Count);

                return Color.FromRgb(avgR, avgG, avgB);
            }
        }
    }
}
