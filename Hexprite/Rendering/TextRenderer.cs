using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hexprite.Rendering
{
    public static class TextRenderer
    {
        /// <summary>
        /// Large WPF font size used for rendering pixel fonts before downsampling.
        /// At this size, each font-pixel becomes a large block (~30-60px) so WPF's
        /// rasterization produces correct shapes regardless of hinting/grid-fitting.
        /// </summary>
        private const int LargeRenderSize = 500;

        /// <summary>
        /// Thread-safe per-font glyph cache with on-demand lazy downsampling for any character.
        /// </summary>
        private sealed class FontGlyphCache
        {
            public double CellSize { get; }
            public int CellsPerEm { get; }
            public double CellPx { get; }
            public GlyphTypeface GlyphTypeface { get; }
            public Typeface Typeface { get; }
            public ConcurrentDictionary<char, bool[,]> Glyphs { get; } = new();

            public FontGlyphCache(double cellSize, GlyphTypeface gt, Typeface typeface)
            {
                CellSize = cellSize;
                GlyphTypeface = gt;
                Typeface = typeface;
                CellsPerEm = (int)System.Math.Round(1.0 / cellSize, MidpointRounding.AwayFromZero);
                CellPx = (double)LargeRenderSize / CellsPerEm;
            }

            public bool[,] GetOrCreateGlyph(char c)
            {
                return Glyphs.GetOrAdd(c, static (ch, arg) =>
                {
                    if (ch == ' ')
                    {
                        // Space: no pixels, just advance width
                        int spaceCols = arg.GlyphTypeface.CharacterToGlyphMap.TryGetValue(' ', out ushort spGi)
                            ? System.Math.Max(1, (int)System.Math.Round(arg.GlyphTypeface.AdvanceWidths[spGi] * arg.CellsPerEm, MidpointRounding.AwayFromZero))
                            : System.Math.Max(1, arg.CellsPerEm / 4);
                        int spaceRows = (int)System.Math.Round(arg.GlyphTypeface.Height * arg.CellsPerEm, MidpointRounding.AwayFromZero);
                        if (spaceRows <= 0) spaceRows = arg.CellsPerEm;
                        return new bool[spaceCols, spaceRows];
                    }

                    double pixelsPerDip = VisualTreeHelper.GetDpi(new DrawingVisual()).PixelsPerDip;
                    int expectedCols = -1;
                    if (arg.GlyphTypeface.CharacterToGlyphMap.TryGetValue(ch, out ushort glyphIdx) && glyphIdx > 0)
                        expectedCols = System.Math.Max(1, (int)System.Math.Round(arg.GlyphTypeface.AdvanceWidths[glyphIdx] * arg.CellsPerEm, MidpointRounding.AwayFromZero));

                    var ft = new FormattedText(
                        ch.ToString(),
                        CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight,
                        arg.Typeface,
                        LargeRenderSize,
                        Brushes.Black,
                        pixelsPerDip);

                    int overhangPad = (int)System.Math.Ceiling(
                        System.Math.Abs(ft.OverhangLeading) + System.Math.Abs(ft.OverhangTrailing) + arg.CellPx * 2);
                    int bmpW = (int)System.Math.Ceiling(ft.WidthIncludingTrailingWhitespace) + overhangPad;
                    int bmpH = (int)System.Math.Ceiling(ft.Height) + (int)System.Math.Ceiling(arg.CellPx);

                    if (bmpW <= 0 || bmpH <= 0)
                        return new bool[expectedCols > 0 ? expectedCols : 1, arg.CellsPerEm];

                    var dv = new DrawingVisual();
                    TextOptions.SetTextFormattingMode(dv, TextFormattingMode.Display);
                    TextOptions.SetTextRenderingMode(dv, TextRenderingMode.Aliased);

                    using (var dc = dv.RenderOpen())
                    {
                        dc.DrawRectangle(Brushes.White, pen: null, new Rect(0, 0, bmpW, bmpH));
                        dc.DrawText(ft, new Point(0, 0));
                    }

                    var rtb = new RenderTargetBitmap(bmpW, bmpH, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(dv);

                    var native = DownsampleGlyph(rtb, bmpW, bmpH, arg.CellPx, expectedCols);
                    return native ?? new bool[expectedCols > 0 ? expectedCols : 1, arg.CellsPerEm];
                }, this);
            }

            /// <summary>
            /// Downsamples a large-rendered glyph bitmap to the native pixel grid.
            /// Each cell in the native grid corresponds to a cellPx×cellPx block in the bitmap.
            /// Samples the center pixel of each block to determine if the cell is filled.
            /// Trims trailing empty columns/rows from padding.
            /// </summary>
            private static bool[,]? DownsampleGlyph(RenderTargetBitmap rtb, int bmpW, int bmpH, double cellPx, int expectedNativeCols = -1)
            {
                int stride = bmpW * 4;
                byte[] pixels = new byte[bmpH * stride];
                rtb.CopyPixels(pixels, stride, 0);

                // Downsample the full padded bitmap
                int fullCols = (int)System.Math.Round(bmpW / cellPx, MidpointRounding.AwayFromZero);
                int fullRows = (int)System.Math.Round(bmpH / cellPx, MidpointRounding.AwayFromZero);

                if (fullCols <= 0 || fullRows <= 0)
                    return null;

                var grid = new bool[fullCols, fullRows];

                for (int row = 0; row < fullRows; row++)
                {
                    for (int col = 0; col < fullCols; col++)
                    {
                        // Sample center of this cell's block in the large bitmap
                        int cx = (int)(col * cellPx + cellPx * 0.5);
                        int cy = (int)(row * cellPx + cellPx * 0.5);

                        if (cx >= 0 && cx < bmpW && cy >= 0 && cy < bmpH)
                        {
                            int idx = cy * stride + cx * 4;
                            byte r = pixels[idx + 2];
                            // Black pixel = font pixel is filled
                            if (r < 128)
                                grid[col, row] = true;
                        }
                    }
                }

                // Find the rightmost filled column (trim padding on right)
                int lastFilledCol = -1;
                for (int col = fullCols - 1; col >= 0; col--)
                {
                    for (int row = 0; row < fullRows; row++)
                    {
                        if (grid[col, row])
                        {
                            lastFilledCol = col;
                            goto foundCol;
                        }
                    }
                }
                foundCol:

                // Use the wider of: advance width or visual extent (for script fonts)
                int trimmedCols;
                if (lastFilledCol < 0)
                {
                    // Empty glyph — use advance width or minimum 1
                    trimmedCols = expectedNativeCols > 0 ? expectedNativeCols : 1;
                }
                else
                {
                    int visualCols = lastFilledCol + 1;
                    trimmedCols = expectedNativeCols > 0
                        ? System.Math.Max(expectedNativeCols, visualCols)
                        : visualCols;
                }

                if (trimmedCols == fullCols && trimmedCols > 0)
                    return grid;

                // Create trimmed grid
                var result = new bool[trimmedCols, fullRows];
                for (int col = 0; col < trimmedCols; col++)
                    for (int row = 0; row < fullRows; row++)
                        if (col < fullCols)
                            result[col, row] = grid[col, row];

                return result;
            }
        }

        /// <summary>
        /// Per-font glyph cache: fontKey → FontGlyphCache.
        /// Built lazily on first use. Each glyph is stored at native pixel resolution.
        /// </summary>
        private static readonly ConcurrentDictionary<string, FontGlyphCache> _glyphCaches = new();

        /// <summary>
        /// Clears all cached font glyphs. Called when fonts are refreshed or modified.
        /// </summary>
        public static void ClearCache()
        {
            _glyphCaches.Clear();
        }

        /// <summary>
        /// Returns the native pixel height for a pixel font (e.g. 6 for Tiny5, 9 for Pixelify Sans).
        /// Returns 0 if the font is not a pixel font or detection fails.
        /// This is used by the ViewModel to sync the SIZE slider with the SCALE multiplier.
        /// </summary>
        public static int GetNativePixelHeight(FontFamily fontFamily, bool isBold = false, bool isItalic = false)
        {
            var typeface = new Typeface(
                fontFamily,
                isItalic ? FontStyles.Italic : FontStyles.Normal,
                isBold ? FontWeights.Bold : FontWeights.Normal,
                FontStretches.Normal);

            if (!typeface.TryGetGlyphTypeface(out GlyphTypeface? gt))
                return 0;

            var cache = GetOrCreateGlyphCache(gt, typeface, fontFamily);
            if (cache == null)
                return 0;

            var glyph = cache.GetOrCreateGlyph('E');
            if (glyph != null)
            {
                int w = glyph.GetLength(0);
                int h = glyph.GetLength(1);
                int topRow = -1, bottomRow = -1;

                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        if (glyph[x, y])
                        {
                            if (topRow < 0) topRow = y;
                            bottomRow = y;
                        }

                if (topRow >= 0)
                    return bottomRow - topRow + 1;
            }

            // Fallback for fonts where 'E' is blank or absent (e.g. icon/numeric fonts)
            foreach (char ch in "HABX0123456789")
            {
                var g = cache.GetOrCreateGlyph(ch);
                if (g == null) continue;
                int gw = g.GetLength(0), gh = g.GetLength(1);
                int tr = -1, br = -1;
                for (int y = 0; y < gh; y++)
                    for (int x = 0; x < gw; x++)
                        if (g[x, y]) { if (tr < 0) tr = y; br = y; }
                if (tr >= 0) return br - tr + 1;
            }

            return cache.CellsPerEm > 0 ? cache.CellsPerEm : 0;
        }

        /// <summary>
        /// Calculates the exact caret anchor (in sprite pixel coordinates) and height for the given text and caret index.
        /// When caretIndex is negative or greater than text.Length, the caret is positioned at the end of the text.
        /// </summary>
        public static (int CaretX, int CaretY, int CaretHeight) GetCaretPosition(
            string text, int caretIndex, FontFamily fontFamily, int fontSize,
            bool isBold, bool isItalic, int letterSpacing, int pixelScale, int lineHeight,
            Core.TextToolAlignment alignment, int textToolX, int textToolY)
        {
            int nativeHeight = GetNativePixelHeight(fontFamily, isBold, isItalic);
            int defaultLineH = nativeHeight > 0 ? nativeHeight * pixelScale : fontSize * pixelScale;
            if (defaultLineH <= 0) defaultLineH = 8 * pixelScale;

            if (string.IsNullOrEmpty(text))
            {
                return (textToolX, textToolY, defaultLineH);
            }

            text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            if (caretIndex < 0 || caretIndex > text.Length)
                caretIndex = text.Length;

            var lines = text.Split('\n');
            var lineMasks = new List<bool[,]>();
            int maxWidth = 0;

            foreach (var line in lines)
            {
                var lm = RenderSingleLine(line, fontFamily, fontSize, isBold, isItalic, letterSpacing, pixelScale, antiAlias: false, threshold: 170);
                lineMasks.Add(lm);
                maxWidth = System.Math.Max(maxWidth, lm.GetLength(0));
            }

            // Find target line and column index for caretIndex
            int targetLine = 0;
            int colIndexInLine = 0;
            int runningLen = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                int lineLen = lines[i].Length;
                if (caretIndex <= runningLen + lineLen || i == lines.Length - 1)
                {
                    targetLine = i;
                    colIndexInLine = System.Math.Clamp(caretIndex - runningLen, 0, lineLen);
                    break;
                }
                runningLen += lineLen + 1; // +1 for the '\n'
            }

            int lineSpacing = lineHeight * pixelScale;
            int yOffset = 0;

            for (int i = 0; i < targetLine; i++)
            {
                int h = lineMasks[i].GetLength(1);
                yOffset += (h > 0 ? h : defaultLineH) + lineSpacing;
            }

            string targetLineText = lines[targetLine];
            string prefix = targetLineText[..colIndexInLine];
            int prefixW = 0;
            if (!string.IsNullOrEmpty(prefix))
            {
                var prefixMask = RenderSingleLine(prefix, fontFamily, fontSize, isBold, isItalic, letterSpacing, pixelScale, antiAlias: false, threshold: 170);
                prefixW = prefixMask.GetLength(0);
            }

            int targetLineW = lineMasks[targetLine].GetLength(0);
            int targetLineH = lineMasks[targetLine].GetLength(1);
            int caretH = targetLineH > 0 ? targetLineH : defaultLineH;

            int placeX = textToolX;
            if (alignment == Core.TextToolAlignment.Center)
                placeX = textToolX - maxWidth / 2;
            else if (alignment == Core.TextToolAlignment.Right)
                placeX = textToolX - maxWidth;

            int lineOffset = alignment switch
            {
                Core.TextToolAlignment.Center => (maxWidth - targetLineW) / 2,
                Core.TextToolAlignment.Right => maxWidth - targetLineW,
                _ => 0,
            };

            int caretX = placeX + lineOffset + prefixW;
            int caretY = textToolY + yOffset;

            return (caretX, caretY, caretH);
        }

        /// <summary>
        /// Backwards compatibility overload: positions caret at the end of the text.
        /// </summary>
        public static (int CaretX, int CaretY, int CaretHeight) GetCaretPosition(
            string text, FontFamily fontFamily, int fontSize,
            bool isBold, bool isItalic, int letterSpacing, int pixelScale, int lineHeight,
            Core.TextToolAlignment alignment, int textToolX, int textToolY)
        {
            return GetCaretPosition(
                text, -1, fontFamily, fontSize, isBold, isItalic, letterSpacing, pixelScale, lineHeight,
                alignment, textToolX, textToolY);
        }

        /// <summary>
        /// Renders text to a monochrome pixel mask.
        /// For pixel fonts: uses cached native-resolution glyphs built by rendering
        /// at a large size then downsampling. Guarantees pixel-perfect shapes.
        /// For non-pixel fonts: falls back to WPF rendering at the requested fontSize.
        /// </summary>
        public static bool[,] RenderTextToMonochromeMask(
            string text, FontFamily fontFamily, int fontSize,
            bool isBold, bool isItalic, int letterSpacing = 1, int pixelScale = 1, int lineHeight = 1,
            bool antiAlias = false, int threshold = 170,
            Core.TextToolAlignment alignment = Core.TextToolAlignment.Left)
        {
            if (string.IsNullOrEmpty(text))
                return new bool[0, 0];

            text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

            // Handle multi-line text: split, render each line, stack vertically
            if (text.Contains('\n', StringComparison.Ordinal))
            {
                var lines = text.Split('\n');
                var lineMasks = new List<bool[,]>();
                int maxWidth = 0;
                int totalHeight = 0;
                int nativeHeight = GetNativePixelHeight(fontFamily, isBold, isItalic);
                int defaultLineH = nativeHeight > 0 ? nativeHeight * pixelScale : fontSize * pixelScale;
                if (defaultLineH <= 0) defaultLineH = 8 * pixelScale;

                foreach (var line in lines)
                {
                    var lineMask = RenderSingleLine(line, fontFamily, fontSize, isBold, isItalic, letterSpacing, pixelScale, antiAlias, threshold);
                    lineMasks.Add(lineMask);
                    maxWidth = System.Math.Max(maxWidth, lineMask.GetLength(0));
                    int lh = lineMask.GetLength(1);
                    totalHeight += (lh > 0 ? lh : defaultLineH);
                }

                // Line spacing: user-controlled lineHeight (0 = touching, 1 = 1px gap, etc.)
                int lineSpacing = lineHeight * pixelScale;
                totalHeight += lineSpacing * (lines.Length - 1);

                if (maxWidth <= 0 || totalHeight <= 0)
                    return new bool[0, 0];

                var result = new bool[maxWidth, totalHeight];
                int yOffset = 0;

                for (int i = 0; i < lineMasks.Count; i++)
                {
                    var lm = lineMasks[i];
                    int lw = lm.GetLength(0);
                    int lh = lm.GetLength(1);
                    int effectiveLh = lh > 0 ? lh : defaultLineH;

                    int lineOffset = alignment switch
                    {
                        Core.TextToolAlignment.Center => (maxWidth - lw) / 2,
                        Core.TextToolAlignment.Right => maxWidth - lw,
                        _ => 0,
                    };

                    for (int y = 0; y < lh; y++)
                        for (int x = 0; x < lw; x++)
                        {
                            int destY = yOffset + y;
                            int destX = lineOffset + x;
                            if (lm[x, y] && destY >= 0 && destY < totalHeight && destX >= 0 && destX < maxWidth)
                                result[destX, destY] = true;
                        }

                    yOffset += effectiveLh + lineSpacing;
                }

                return result;
            }

            return RenderSingleLine(text, fontFamily, fontSize, isBold, isItalic, letterSpacing, pixelScale, antiAlias, threshold);
        }

        private static bool[,] RenderSingleLine(
            string text, FontFamily fontFamily, int fontSize,
            bool isBold, bool isItalic, int letterSpacing, int pixelScale,
            bool antiAlias, int threshold)
        {
            if (string.IsNullOrEmpty(text))
                return new bool[0, 0];

            text = text.Replace("\r\n", "", StringComparison.Ordinal)
                       .Replace("\r", "", StringComparison.Ordinal)
                       .Replace("\n", "", StringComparison.Ordinal);
            if (string.IsNullOrEmpty(text))
                return new bool[0, 0];

            var typeface = new Typeface(
                fontFamily,
                isItalic ? FontStyles.Italic : FontStyles.Normal,
                isBold ? FontWeights.Bold : FontWeights.Normal,
                FontStretches.Normal);

            // Try pixel-perfect path: cached native-resolution glyphs
            // If anti-aliasing is requested, we bypass the pixel-perfect downsampler
            // because we want the native OS renderer's subpixel hints.
            if (!antiAlias && typeface.TryGetGlyphTypeface(out GlyphTypeface? gt))
            {
                var cache = GetOrCreateGlyphCache(gt, typeface, fontFamily);
                if (cache != null && cache.CellsPerEm > 0)
                {
                    var result = CompositeFromCache(text, cache, letterSpacing);
                    if (result.GetLength(0) > 0 && result.GetLength(1) > 0)
                    {
                        if (pixelScale > 1)
                            result = NearestNeighborScale(result, pixelScale);
                        return result;
                    }
                }
            }

            // Fallback: direct WPF rendering
            var mask = RenderViaWpf(text, typeface, fontSize, letterSpacing, antiAlias, threshold);
            if (pixelScale > 1)
                mask = NearestNeighborScale(mask, pixelScale);
            return mask;
        }

        // ── Glyph Cache ──────────────────────────────────────────────────────

        private static FontGlyphCache? GetOrCreateGlyphCache(
            GlyphTypeface gt, Typeface typeface, FontFamily fontFamily)
        {
            string cacheKey = string.Create(
                CultureInfo.InvariantCulture,
                $"{fontFamily.Source}|{typeface.Style}|{typeface.Weight}");

            return _glyphCaches.GetOrAdd(cacheKey, static (_, arg) => BuildGlyphCache(arg.gt, arg.typeface), (gt, typeface));
        }

        /// <summary>
        /// Builds the glyph cache by calculating the pixel grid cell size and pre-populating printable ASCII.
        /// </summary>
        private static FontGlyphCache? BuildGlyphCache(
            GlyphTypeface gt, Typeface typeface)
        {
            double cellSize = ComputeCellSize(gt);
            double pixelsPerDip = VisualTreeHelper.GetDpi(new DrawingVisual()).PixelsPerDip;

            // If advance-width analysis failed, detect cell size from rendered bitmap
            if (cellSize < 0.001)
                cellSize = DetectCellSizeFromBitmap(typeface, pixelsPerDip);

            if (cellSize < 0.001)
                return null;

            var cache = new FontGlyphCache(cellSize, gt, typeface);

            // Pre-populate printable ASCII + common symbols
            string chars = " !\"#$%&'()*+,-./0123456789:;<=>?@"
                         + "ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`"
                         + "abcdefghijklmnopqrstuvwxyz{|}~";

            foreach (char c in chars)
            {
                cache.GetOrCreateGlyph(c);
            }

            return cache;
        }

        // ── Cell Size Detection ──────────────────────────────────────────────

        /// <summary>
        /// Computes the pixel grid cell size (in em units) by finding the GCD
        /// of all glyph advance widths. Works for strict pixel fonts where all
        /// advances are exact integer multiples of the cell size.
        /// Returns 0 if the font doesn't conform to a strict pixel grid.
        /// </summary>
        private static double ComputeCellSize(GlyphTypeface gt)
        {
            var widths = new List<double>();
            foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.!,")
            {
                if (gt.CharacterToGlyphMap.TryGetValue(c, out ushort gi) && gi > 0)
                {
                    double w = gt.AdvanceWidths[gi];
                    if (w > 0.001)
                        widths.Add(w);
                }
            }

            if (widths.Count < 5)
                return 0;

            double gcd = widths[0];
            for (int i = 1; i < widths.Count; i++)
                gcd = ApproxGCD(gcd, widths[i]);

            if (gcd < 0.01)
                return 0;

            double cellsPerEm = 1.0 / gcd;
            if (System.Math.Abs(cellsPerEm - System.Math.Round(cellsPerEm, MidpointRounding.AwayFromZero)) > 0.05)
                return 0;

            int cells = (int)System.Math.Round(cellsPerEm, MidpointRounding.AwayFromZero);
            if (cells < 4 || cells > 64)
                return 0;

            // Verify ALL widths are integer multiples
            foreach (double w in widths)
            {
                double pw = w * cells;
                if (System.Math.Abs(pw - System.Math.Round(pw, MidpointRounding.AwayFromZero)) > 0.05)
                    return 0;
            }

            return gcd;
        }

        private static double ApproxGCD(double a, double b)
        {
            a = System.Math.Abs(a);
            b = System.Math.Abs(b);
            while (b > 0.0005)
            {
                double temp = b;
                b = a % b;
                a = temp;
            }
            return a;
        }

        /// <summary>
        /// Fallback: detects pixel grid cell size by rendering 'E' at 500px and
        /// analyzing horizontal color transitions to find the dominant grid spacing.
        /// Works for any pixel font regardless of advance width patterns.
        /// </summary>
        private static double DetectCellSizeFromBitmap(Typeface typeface, double pixelsPerDip)
        {
            var ft = new FormattedText(
                "E", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                typeface, LargeRenderSize, Brushes.Black, pixelsPerDip);

            int bmpW = (int)System.Math.Ceiling(ft.WidthIncludingTrailingWhitespace);
            int bmpH = (int)System.Math.Ceiling(ft.Height);
            if (bmpW <= 0 || bmpH <= 0) return 0;

            var dv = new DrawingVisual();
            TextOptions.SetTextFormattingMode(dv, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(dv, TextRenderingMode.Aliased);

            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, pen: null, new Rect(0, 0, bmpW, bmpH));
                dc.DrawText(ft, new Point(0, 0));
            }

            var rtb = new RenderTargetBitmap(bmpW, bmpH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);

            int stride = bmpW * 4;
            byte[] pixels = new byte[bmpH * stride];
            rtb.CopyPixels(pixels, stride, 0);

            // Detect cell size by finding vertical row repetition.
            // For pixel fonts at 500px, each "pixel row" is cellPx identical scan lines.
            // When the pattern changes, we get a transition. The run length = cellPx.
            var runLengths = new Dictionary<int, int>(); // length → count
            int currentRun = 1;

            for (int y = 1; y < bmpH; y++)
            {
                // Compare this row with the previous row
                bool identical = true;
                for (int x = 0; x < bmpW; x++)
                {
                    int idx1 = (y - 1) * stride + x * 4;
                    int idx2 = y * stride + x * 4;
                    bool black1 = pixels[idx1 + 2] < 128;
                    bool black2 = pixels[idx2 + 2] < 128;
                    if (black1 != black2)
                    {
                        identical = false;
                        break;
                    }
                }

                if (identical)
                {
                    currentRun++;
                }
                else
                {
                    if (currentRun >= 5 && currentRun <= 200)
                    {
                        runLengths.TryGetValue(currentRun, out int cnt);
                        runLengths[currentRun] = cnt + 1;
                    }
                    currentRun = 1;
                }
            }
            // Don't forget the last run
            if (currentRun >= 5 && currentRun <= 200)
            {
                runLengths.TryGetValue(currentRun, out int cnt);
                runLengths[currentRun] = cnt + 1;
            }

            if (runLengths.Count == 0)
                return 0;

            // Find the most common run length — this is the cell height in pixels
            int bestRunLen = 0;
            int bestCount = 0;
            foreach (var kvp in runLengths)
            {
                if (kvp.Value > bestCount)
                {
                    bestCount = kvp.Value;
                    bestRunLen = kvp.Key;
                }
            }

            if (bestRunLen < 5)
                return 0;

            double cellsPerEm = (double)LargeRenderSize / bestRunLen;
            int cells = (int)System.Math.Round(cellsPerEm, MidpointRounding.AwayFromZero);

            if (cells < 4 || cells > 64)
                return 0;

            return 1.0 / cells;
        }

        // ── Compositing ──────────────────────────────────────────────────────

        /// <summary>
        /// Composites cached native-resolution glyphs into a single mask
        /// with the user's letter spacing.
        /// </summary>
        private static bool[,] CompositeFromCache(
            string text, FontGlyphCache cache, int letterSpacing)
        {
            // Collect glyph bitmaps for each character
            var charBitmaps = new List<bool[,]>();
            int totalWidth = 0;
            int maxHeight = 0;

            foreach (char c in text)
            {
                var glyph = cache.GetOrCreateGlyph(c);
                charBitmaps.Add(glyph);
                totalWidth += glyph.GetLength(0);
                maxHeight = System.Math.Max(maxHeight, glyph.GetLength(1));
            }

            totalWidth += letterSpacing * (text.Length - 1);

            if (totalWidth <= 0 || maxHeight <= 0)
                return new bool[0, 0];

            var result = new bool[totalWidth, maxHeight];
            int xOffset = 0;

            for (int i = 0; i < charBitmaps.Count; i++)
            {
                var bmp = charBitmaps[i];
                int cw = bmp.GetLength(0);
                int ch = bmp.GetLength(1);

                for (int row = 0; row < System.Math.Min(ch, maxHeight); row++)
                {
                    for (int col = 0; col < cw; col++)
                    {
                        int destX = xOffset + col;
                        if (destX >= 0 && destX < totalWidth && bmp[col, row])
                            result[destX, row] = true;
                    }
                }

                xOffset += cw + letterSpacing;
            }

            return result;
        }

        // ── WPF Fallback ─────────────────────────────────────────────────────

        /// <summary>
        /// Fallback: WPF FormattedText rendering with Display + Aliased mode.
        /// Used for non-pixel fonts where cell size detection fails.
        /// </summary>
        private static bool[,] RenderViaWpf(
            string text, Typeface typeface, int fontSize, int letterSpacing, bool antiAlias, int threshold)
        {
            double pixelsPerDip = VisualTreeHelper.GetDpi(new DrawingVisual()).PixelsPerDip;

            var charWidths = new int[text.Length];
            int totalWidth = 0;
            int maxHeight = 0;

            for (int i = 0; i < text.Length; i++)
            {
                var ct = new FormattedText(
                    text[i].ToString(), CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, typeface, fontSize,
                    Brushes.Black, pixelsPerDip);

                charWidths[i] = (int)System.Math.Ceiling(ct.WidthIncludingTrailingWhitespace);
                maxHeight = System.Math.Max(maxHeight, (int)System.Math.Ceiling(ct.Height));
                totalWidth += charWidths[i];
            }

            totalWidth += letterSpacing * (text.Length - 1);
            if (totalWidth <= 0 || maxHeight <= 0)
                return new bool[0, 0];

            var dv = new DrawingVisual();
            TextOptions.SetTextFormattingMode(dv, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(dv, antiAlias ? TextRenderingMode.Auto : TextRenderingMode.Aliased);

            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, pen: null, new Rect(0, 0, totalWidth, maxHeight));
                int xOff = 0;
                for (int i = 0; i < text.Length; i++)
                {
                    var ft = new FormattedText(
                        text[i].ToString(), CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight, typeface, fontSize,
                        Brushes.Black, pixelsPerDip);
                    dc.DrawText(ft, new Point(xOff, 0));
                    xOff += charWidths[i] + letterSpacing;
                }
            }

            var rtb = new RenderTargetBitmap(totalWidth, maxHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);

            int stride = totalWidth * 4;
            byte[] pixels = new byte[maxHeight * stride];
            rtb.CopyPixels(pixels, stride, 0);

            var mask = new bool[totalWidth, maxHeight];
            for (int y = 0; y < maxHeight; y++)
                for (int x = 0; x < totalWidth; x++)
                {
                    int idx = y * stride + x * 4;
                    // White bg, black text. Luminance ~0 = solid black text.
                    // Lower threshold = requires darker pixels to be solid.
                    int lum = (int)(0.299 * pixels[idx + 2] + 0.587 * pixels[idx + 1] + 0.114 * pixels[idx]);
                    if (lum <= threshold) mask[x, y] = true;
                }

            return mask;
        }

        // ── Scaling ──────────────────────────────────────────────────────────

        private static bool[,] NearestNeighborScale(bool[,] source, int scale)
        {
            int sw = source.GetLength(0), sh = source.GetLength(1);
            var result = new bool[sw * scale, sh * scale];
            for (int sy = 0; sy < sh; sy++)
                for (int sx = 0; sx < sw; sx++)
                    if (source[sx, sy])
                        for (int dy = 0; dy < scale; dy++)
                            for (int dx = 0; dx < scale; dx++)
                                result[sx * scale + dx, sy * scale + dy] = true;
            return result;
        }
    }
}
