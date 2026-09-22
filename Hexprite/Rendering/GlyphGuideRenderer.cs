using System;
using Hexprite.Core;

namespace Hexprite.Rendering
{
    /// <summary>
    /// Renders guide overlays on the glyph canvas.
    /// </summary>
    public static class GlyphGuideRenderer
    {
        /// <summary>
        /// Renders guide lines for the active glyph into the provided pixel buffer.
        /// </summary>
        /// <param name="buffer">The BGRA32 pixel buffer representing the scaled canvas.</param>
        /// <param name="pixelWidth">Width of the buffer in physical pixels.</param>
        /// <param name="pixelHeight">Height of the buffer in physical pixels.</param>
        /// <param name="cellSize">Size of one logic pixel in physical pixels (zoom level).</param>
        /// <param name="document">The font document containing global metrics.</param>
        /// <param name="glyph">The currently active glyph being edited.</param>
        /// <param name="showMetricsGuides">Whether to render ascent, baseline, descent, and cell boundary lines.</param>
        /// <param name="showAdvanceGuide">Whether to render the X-advance guide line.</param>
        public static void RenderGuides(uint[] buffer, int pixelWidth, int pixelHeight, int cellSize, FontDocument document, GlyphState glyph, bool showMetricsGuides = true, bool showAdvanceGuide = true)
        {
            if (buffer == null || document == null || glyph == null) return;

            // Guide colors with 50% transparency (0x80)
            uint baselineColor     = 0x80FF0000; // Red
            uint ascentColor       = 0x800000FF; // Blue
            uint descentColor      = 0x80FF00FF; // Magenta
            uint advanceColor      = 0x8000FF00; // Green
            uint cellBoundaryColor = 0x80FFA500; // Amber / Orange (distinct cell boundary)

            int thickness = Math.Max(2, cellSize / 16);

            // Helper to draw a horizontal guide line
            void DrawHLine(int logicY, uint color, bool alignBottom = false)
            {
                int pixelY = logicY * cellSize;
                if (alignBottom) pixelY -= thickness;

                for (int offset = 0; offset < thickness; offset++)
                {
                    int py = pixelY + offset;
                    if (py >= 0 && py < pixelHeight)
                    {
                        for (int x = 0; x < pixelWidth; x++)
                        {
                            buffer[py * pixelWidth + x] = AlphaBlend(color, buffer[py * pixelWidth + x]);
                        }
                    }
                }
            }

            // Helper to draw a vertical guide line
            void DrawVLine(int logicX, uint color, bool dashed = false)
            {
                int pixelX = logicX * cellSize;
                int dashLength = Math.Max(4, cellSize / 2);
                for (int offset = 0; offset < thickness; offset++)
                {
                    int px = pixelX + offset;
                    if (px >= 0 && px < pixelWidth)
                    {
                        for (int y = 0; y < pixelHeight; y++)
                        {
                            if (dashed && ((y / dashLength) % 2 != 0)) continue;
                            buffer[y * pixelWidth + px] = AlphaBlend(color, buffer[y * pixelWidth + px]);
                        }
                    }
                }
            }

            if (showMetricsGuides)
            {
                // Ascent line (top of cell)
                DrawHLine(0, ascentColor);

                // Baseline
                DrawHLine(document.Baseline, baselineColor);

                // Descent line (bottom of cell)
                DrawHLine(document.CellHeight, descentColor, alignBottom: true);

                // Cell boundary line (right edge of editable glyph cell)
                if (glyph.Width > 0)
                {
                    DrawVLine(glyph.Width, cellBoundaryColor, dashed: true);
                }
            }

            if (showAdvanceGuide && glyph.XAdvance >= 0)
            {
                DrawVLine(glyph.XAdvance, advanceColor);
            }
        }

        public static uint AlphaBlend(uint fg, uint bg)
        {
            uint fgA = (fg >> 24) & 0xFF;
            if (fgA == 0) return bg;
            if (fgA == 255) return fg;
            
            uint fgR = (fg >> 16) & 0xFF;
            uint fgG = (fg >> 8) & 0xFF;
            uint fgB = fg & 0xFF;
            
            uint bgR = (bg >> 16) & 0xFF;
            uint bgG = (bg >> 8) & 0xFF;
            uint bgB = bg & 0xFF;
            
            uint outR = (fgR * fgA + bgR * (255 - fgA)) / 255;
            uint outG = (fgG * fgA + bgG * (255 - fgA)) / 255;
            uint outB = (fgB * fgA + bgB * (255 - fgA)) / 255;
            
            return 0xFF000000 | (outR << 16) | (outG << 8) | outB;
        }
    }
}
