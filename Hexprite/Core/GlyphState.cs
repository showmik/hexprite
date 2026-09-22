using System;
using System.Text.Json.Serialization;

namespace Hexprite.Core
{
    /// <summary>
    /// Represents a single glyph (character) in a font document.
    /// Stores the pixel bitmap and per-character metrics that map directly
    /// to Adafruit GFX <c>GFXglyph</c> fields and LVGL glyph descriptors.
    /// </summary>
    public class GlyphState
    {
        /// <summary>Unicode code point this glyph represents (e.g., 65 for 'A').</summary>
        public int CodePoint { get; set; }

        /// <summary>Display character derived from <see cref="CodePoint"/>.</summary>
        [JsonIgnore]
        public char Character => (char)CodePoint;

        /// <summary>Glyph bitmap width in pixels.</summary>
        public int Width { get; set; }

        /// <summary>Glyph bitmap height in pixels.</summary>
        public int Height { get; set; }

        /// <summary>
        /// Pixel data — <c>bool[Width * Height]</c>, row-major order.
        /// <see langword="true"/> = ink pixel, <see langword="false"/> = empty.
        /// </summary>
        public bool[] Pixels { get; set; } = [];

        // ── Metrics (matching Adafruit GFX GFXglyph fields) ──────────────

        /// <summary>
        /// X offset from cursor position to glyph top-left corner.
        /// Positive shifts the glyph to the right of the cursor.
        /// Maps to <c>GFXglyph.xOffset</c>.
        /// </summary>
        public int XOffset { get; set; }

        /// <summary>
        /// Y offset from cursor position to glyph top-left corner.
        /// Negative values shift the glyph above the baseline (typical).
        /// Maps to <c>GFXglyph.yOffset</c>.
        /// </summary>
        public int YOffset { get; set; }

        /// <summary>
        /// Horizontal advance — distance to move cursor right after drawing this glyph.
        /// Maps to <c>GFXglyph.xAdvance</c>.
        /// </summary>
        public int XAdvance { get; set; }

        /// <summary>
        /// Whether this glyph has been manually edited by the user.
        /// Used to visually distinguish customized vs. auto-imported glyphs in the Glyph Map.
        /// </summary>
        public bool IsCustomized { get; set; }

        /// <summary>Creates a deep copy of this glyph state.</summary>
        public GlyphState Clone() => new()
        {
            CodePoint = CodePoint,
            Width = Width,
            Height = Height,
            Pixels = (bool[])Pixels.Clone(),
            XOffset = XOffset,
            YOffset = YOffset,
            XAdvance = XAdvance,
            IsCustomized = IsCustomized,
        };

        /// <summary>
        /// Returns the tight bounding box of ink pixels (auto-cropped).
        /// Used during export to minimize bitmap size.
        /// Returns (cropX, cropY, cropWidth, cropHeight). If empty, returns (0,0,0,0).
        /// </summary>
        public (int x, int y, int w, int h) GetTightBounds()
        {
            int minX = Width, minY = Height, maxX = -1, maxY = -1;

            if (Pixels == null || Pixels.Length == 0) return (0, 0, 0, 0);

            int pixelCount = Pixels.Length;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int idx = y * Width + x;
                    if (idx < pixelCount && Pixels[idx])
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (maxX < 0) return (0, 0, 0, 0); // empty glyph

            return (minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        /// <summary>
        /// Extracts the tight-cropped pixel data for export.
        /// Returns a new bool[] containing only the pixels within the tight bounding box.
        /// </summary>
        public bool[] GetCroppedPixels()
        {
            var (cx, cy, cw, ch) = GetTightBounds();
            if (cw == 0 || ch == 0) return [];

            var cropped = new bool[cw * ch];
            for (int y = 0; y < ch; y++)
            {
                for (int x = 0; x < cw; x++)
                {
                    cropped[y * cw + x] = Pixels[(cy + y) * Width + (cx + x)];
                }
            }
            return cropped;
        }
    }
}
