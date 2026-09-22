using System;
using Hexprite.Core;

namespace Hexprite.Rendering
{
    /// <summary>
    /// Renders sample text using a FontDocument's own glyph data.
    /// Used by the font preview panel.
    /// </summary>
    public static class FontPreviewRenderer
    {
        /// <summary>
        /// Renders the given text to a 2D boolean array (width x height) using the font's glyphs.
        /// Handles cursor advancement, kerning, and line breaks.
        /// </summary>
        /// <param name="font">The font document containing glyphs and metrics.</param>
        /// <param name="text">The text to render.</param>
        /// <returns>A 2D array where true represents ink pixels, false represents empty space.</returns>
        public static bool[,] RenderPreviewText(FontDocument font, string text)
        {
            if (font == null || string.IsNullOrEmpty(text))
            {
                return new bool[0, 0];
            }

            // Pass 1: Measure text bounds
            int cursorX = 0;
            int cursorY = font.Baseline;
            
            int minX = 0;
            int minY = 0;
            int maxX = 0;
            int maxY = font.Baseline + font.Descent;

            int prevCodePoint = -1;

            foreach (char c in text)
            {
                if (c == '\n')
                {
                    cursorX = 0;
                    cursorY += font.YAdvance;
                    maxY = Math.Max(maxY, cursorY + font.Descent);
                    prevCodePoint = -1;
                    continue;
                }

                int codePoint = c;
                var glyph = font.GetGlyph(codePoint);
                if (glyph == null)
                {
                    int fallbackAdvance = GetFallbackAdvance(font, c);
                    cursorX += fallbackAdvance;
                    maxX = Math.Max(maxX, cursorX);
                    prevCodePoint = -1;
                    continue;
                }

                // Apply kerning
                if (prevCodePoint != -1)
                {
                    cursorX += font.GetKerning(prevCodePoint, codePoint);
                }

                // Bounding box of the glyph
                int glyphLeft = cursorX + glyph.XOffset;
                int glyphTop = cursorY - font.Baseline + glyph.YOffset;
                int glyphRight = glyphLeft + glyph.Width;
                int glyphBottom = glyphTop + glyph.Height;

                minX = Math.Min(minX, glyphLeft);
                minY = Math.Min(minY, glyphTop);
                maxX = Math.Max(maxX, glyphRight);
                maxY = Math.Max(maxY, glyphBottom);

                // Advance cursor
                cursorX += glyph.XAdvance;
                maxX = Math.Max(maxX, cursorX); // ensure space for trailing advance
                prevCodePoint = codePoint;
            }

            int shiftX = minX < 0 ? -minX : 0;
            int shiftY = minY < 0 ? -minY : 0;

            int finalWidth = maxX + shiftX;
            int finalHeight = maxY + shiftY;

            if (finalWidth <= 0 || finalHeight <= 0)
            {
                return new bool[0, 0];
            }

            // Add a small padding to prevent clipping on the right edge if any
            bool[,] result = new bool[finalWidth, finalHeight];

            // Pass 2: Render pixels
            cursorX = 0;
            cursorY = font.Baseline;
            prevCodePoint = -1;

            foreach (char c in text)
            {
                if (c == '\n')
                {
                    cursorX = 0;
                    cursorY += font.YAdvance;
                    prevCodePoint = -1;
                    continue;
                }

                int codePoint = c;
                var glyph = font.GetGlyph(codePoint);
                if (glyph == null)
                {
                    int fallbackAdvance = GetFallbackAdvance(font, c);
                    cursorX += fallbackAdvance;
                    prevCodePoint = -1;
                    continue;
                }

                if (prevCodePoint != -1)
                {
                    cursorX += font.GetKerning(prevCodePoint, codePoint);
                }

                int drawX = cursorX + glyph.XOffset + shiftX;
                int drawY = cursorY - font.Baseline + glyph.YOffset + shiftY;

                // Draw glyph pixels
                for (int gy = 0; gy < glyph.Height; gy++)
                {
                    for (int gx = 0; gx < glyph.Width; gx++)
                    {
                        if (glyph.Pixels[gy * glyph.Width + gx])
                        {
                            int px = drawX + gx;
                            int py = drawY + gy;

                            // Bounds check
                            if (px >= 0 && px < finalWidth && py >= 0 && py < finalHeight)
                            {
                                result[px, py] = true;
                            }
                        }
                    }
                }

                cursorX += glyph.XAdvance;
                prevCodePoint = codePoint;
            }

            return result;
        }

        private static int GetFallbackAdvance(FontDocument font, char c)
        {
            if (c == ' ')
            {
                return font.IsMonospaced && font.MaxCellWidth > 0
                    ? font.MaxCellWidth
                    : (font.CellHeight / 2 > 0 ? font.CellHeight / 2 : (font.MaxCellWidth > 0 ? font.MaxCellWidth : 4));
            }

            return font.MaxCellWidth > 0
                ? font.MaxCellWidth
                : (font.CellHeight / 2 > 0 ? font.CellHeight / 2 : 4);
        }
    }
}
