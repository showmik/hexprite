using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Hexprite.Core
{
    /// <summary>
    /// Root document model for Font Mode.
    /// Contains all glyph data, font metrics, kerning pairs, and export settings.
    /// Serialized as part of the <c>.hexp</c> file when <see cref="DocumentMode.Font"/> is active.
    /// </summary>
    public class FontDocument
    {
        // ── Font dimensions ──────────────────────────────────────────────────

        /// <summary>
        /// Height of the editing cell (em-square height) in pixels.
        /// All glyphs are edited at this height. On export, glyphs are auto-cropped
        /// to their tight bounding box for memory efficiency.
        /// </summary>
        public int CellHeight { get; set; } = 8;

        /// <summary>
        /// Maximum / default cell width in pixels.
        /// For monospaced fonts, all glyphs use this width.
        /// For proportional fonts, this is the initial width for new glyphs.
        /// </summary>
        public int MaxCellWidth { get; set; } = 8;

        // ── Font identity ────────────────────────────────────────────────────

        /// <summary>
        /// User-defined font name. Sanitized to a valid C identifier for export.
        /// </summary>
        public string FontName { get; set; } = "myFont";

        // ── Global metrics ───────────────────────────────────────────────────

        /// <summary>
        /// Baseline position measured from the top of the cell (in pixels).
        /// Used as a canvas guide and to compute <c>GFXglyph.yOffset</c> on export.
        /// Example: CellHeight=8, Baseline=6 means the baseline is 6px from top
        /// (leaving 2px below for descenders).
        /// </summary>
        public int Baseline { get; set; } = 6;

        /// <summary>
        /// Line height / vertical advance — distance between baselines of consecutive lines.
        /// Maps to <c>GFXfont.yAdvance</c> and LVGL <c>line_height</c>.
        /// </summary>
        public int YAdvance { get; set; } = 8;

        /// <summary>
        /// Whether this is a monospaced font (all glyphs forced to the same width).
        /// When enabled, changing any glyph's width changes all glyphs.
        /// </summary>
        public bool IsMonospaced { get; set; }

        /// <summary>
        /// Caches the widths of glyphs before IsMonospaced was turned on,
        /// so they can be restored when IsMonospaced is turned off.
        /// Key is CodePoint, Value is original Width.
        /// </summary>
        public Dictionary<int, int> PreMonoWidths { get; set; } = [];

        // ── Character range ──────────────────────────────────────────────────

        /// <summary>
        /// First character code point in the font range (inclusive).
        /// Default: 32 (space) — standard printable ASCII start.
        /// Maps to <c>GFXfont.first</c>.
        /// </summary>
        public int FirstChar { get; set; } = 32;

        /// <summary>
        /// Last character code point in the font range (inclusive).
        /// Default: 126 ('~') — standard printable ASCII end.
        /// Maps to <c>GFXfont.last</c>.
        /// </summary>
        public int LastChar { get; set; } = 126;

        // ── Glyph data ──────────────────────────────────────────────────────

        /// <summary>
        /// Ordered list of all glyph definitions in the font.
        /// One entry per character in the [FirstChar, LastChar] range.
        /// </summary>
        public List<GlyphState> Glyphs { get; set; } = [];

        /// <summary>
        /// Kerning pairs for fine-tuned inter-character spacing.
        /// Each pair specifies a horizontal adjustment between two specific characters.
        /// </summary>
        public List<KerningPair> KerningPairs { get; set; } = [];

        // ── Export settings ──────────────────────────────────────────────────

        /// <summary>Font export configuration. Nullable for backward compatibility.</summary>
        public FontExportSettings? ExportSettings { get; set; }

        // ── Editor state (not persisted) ─────────────────────────────────────

        /// <summary>Index of the currently selected glyph for editing.</summary>
        [JsonIgnore]
        public int ActiveGlyphIndex { get; set; } = 0;

        // ── Computed properties ──────────────────────────────────────────────

        /// <summary>Total number of characters in the font range.</summary>
        [JsonIgnore]
        public int GlyphCount => LastChar - FirstChar + 1;

        /// <summary>
        /// Descent in pixels (distance from baseline to bottom of cell).
        /// Computed as <c>CellHeight - Baseline</c>.
        /// </summary>
        [JsonIgnore]
        public int Descent => CellHeight - Baseline;

        /// <summary>The currently active glyph, or null if none.</summary>
        [JsonIgnore]
        public GlyphState? ActiveGlyph =>
            ActiveGlyphIndex >= 0 && ActiveGlyphIndex < Glyphs.Count
                ? Glyphs[ActiveGlyphIndex]
                : null;

        // ── Factory ──────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a new FontDocument with empty glyphs for the entire character range.
        /// </summary>
        public static FontDocument CreateNew(int cellWidth, int cellHeight, int firstChar = 32, int lastChar = 126)
        {
            cellWidth = Math.Clamp(cellWidth, 1, 512);
            cellHeight = Math.Clamp(cellHeight, 1, 512);

            firstChar = Math.Clamp(firstChar, 0, 0x10FFFF);
            lastChar = Math.Clamp(lastChar, 0, 0x10FFFF);
            if (firstChar > lastChar)
            {
                firstChar = 32;
                lastChar = 126;
            }

            if (lastChar - firstChar > 2048)
            {
                lastChar = firstChar + 2048;
            }

            var doc = new FontDocument
            {
                CellHeight = cellHeight,
                MaxCellWidth = cellWidth,
                Baseline = Math.Clamp((int)(cellHeight * 0.75), 0, cellHeight),
                YAdvance = cellHeight,
                FirstChar = firstChar,
                LastChar = lastChar,
                ExportSettings = new FontExportSettings(),
                Glyphs = new List<GlyphState>(lastChar - firstChar + 1),
                KerningPairs = [],
                PreMonoWidths = [],
            };

            // Initialize all glyphs with empty pixel data
            for (int cp = firstChar; cp <= lastChar; cp++)
            {
                doc.Glyphs.Add(new GlyphState
                {
                    CodePoint = cp,
                    Width = cellWidth,
                    Height = cellHeight,
                    Pixels = new bool[cellWidth * cellHeight],
                    XOffset = 0,
                    YOffset = 0,
                    XAdvance = cellWidth + 1, // default: glyph width + 1px spacing
                    IsCustomized = false,
                });
            }

            doc.NormalizeGlyphs();
            return doc;
        }

        // ── Glyph lookup ─────────────────────────────────────────────────────

        /// <summary>
        /// Finds the glyph for a given code point, or null if not in range.
        /// </summary>
        public GlyphState? GetGlyph(int codePoint)
        {
            if (Glyphs == null || codePoint < FirstChar || codePoint > LastChar) return null;
            int index = codePoint - FirstChar;
            if (index >= 0 && index < Glyphs.Count)
            {
                var direct = Glyphs[index];
                if (direct != null && direct.CodePoint == codePoint)
                {
                    return direct;
                }
            }
            return Glyphs.FirstOrDefault(g => g != null && g.CodePoint == codePoint);
        }

        /// <summary>
        /// Gets the index of a glyph by code point, or -1 if not found.
        /// </summary>
        public int GetGlyphIndex(int codePoint)
        {
            if (Glyphs == null || codePoint < FirstChar || codePoint > LastChar) return -1;
            int index = codePoint - FirstChar;
            if (index >= 0 && index < Glyphs.Count)
            {
                var direct = Glyphs[index];
                if (direct != null && direct.CodePoint == codePoint)
                {
                    return index;
                }
            }
            return Glyphs.FindIndex(g => g != null && g.CodePoint == codePoint);
        }

        /// <summary>
        /// Finds the kerning adjustment between two characters.
        /// Returns 0 if no kerning pair is defined.
        /// </summary>
        public int GetKerning(int leftCodePoint, int rightCodePoint)
        {
            if (KerningPairs == null) return 0;
            return KerningPairs.FirstOrDefault(k =>
                k != null && k.Left == leftCodePoint && k.Right == rightCodePoint)?.Adjustment ?? 0;
        }

        // ── Cloning ──────────────────────────────────────────────────────────

        /// <summary>Creates a deep copy of this font document.</summary>
        public FontDocument Clone()
        {
            return new FontDocument
            {
                CellHeight = CellHeight,
                MaxCellWidth = MaxCellWidth,
                FontName = FontName ?? "myFont",
                Baseline = Baseline,
                YAdvance = YAdvance,
                IsMonospaced = IsMonospaced,
                FirstChar = FirstChar,
                LastChar = LastChar,
                Glyphs = Glyphs != null ? Glyphs.Where(g => g != null).Select(g => g.Clone()).ToList() : [],
                KerningPairs = KerningPairs != null ? KerningPairs.Where(k => k != null).Select(k => k.Clone()).ToList() : [],
                PreMonoWidths = PreMonoWidths != null ? new Dictionary<int, int>(PreMonoWidths) : [],
                ExportSettings = ExportSettings?.Clone() ?? new FontExportSettings(),
                ActiveGlyphIndex = ActiveGlyphIndex,
            };
        }

        // ── Normalization ────────────────────────────────────────────────────

        /// <summary>
        /// Ensures the Glyphs list matches the [FirstChar, LastChar] range.
        /// Adds empty glyphs for missing code points, removes out-of-range glyphs.
        /// Returns true if any changes were made.
        /// </summary>
        public bool NormalizeGlyphs()
        {
            bool changed = false;

            // Ensure collections are non-null
            if (Glyphs == null)
            {
                Glyphs = [];
                changed = true;
            }
            if (KerningPairs == null)
            {
                KerningPairs = [];
                changed = true;
            }
            else
            {
                int beforeCount = KerningPairs.Count;
                KerningPairs.RemoveAll(k => k == null);
                if (KerningPairs.Count != beforeCount) changed = true;
            }
            if (PreMonoWidths == null)
            {
                PreMonoWidths = [];
                changed = true;
            }
            if (ExportSettings == null)
            {
                ExportSettings = new FontExportSettings();
                changed = true;
            }

            // 1. Sanitize font dimensions and metrics
            int clampedCellHeight = Math.Clamp(CellHeight, 1, 512);
            if (CellHeight != clampedCellHeight)
            {
                CellHeight = clampedCellHeight;
                changed = true;
            }

            int clampedMaxCellWidth = Math.Clamp(MaxCellWidth, 1, 512);
            if (MaxCellWidth != clampedMaxCellWidth)
            {
                MaxCellWidth = clampedMaxCellWidth;
                changed = true;
            }

            int clampedBaseline = Math.Clamp(Baseline, 0, CellHeight);
            if (Baseline != clampedBaseline)
            {
                Baseline = clampedBaseline;
                changed = true;
            }

            int clampedYAdvance = Math.Clamp(YAdvance, 1, 512);
            if (YAdvance != clampedYAdvance)
            {
                YAdvance = clampedYAdvance;
                changed = true;
            }

            // 2. Sanitize character range (ensure 0 <= FirstChar <= LastChar <= 0x10FFFF)
            int clampedFirst = Math.Clamp(FirstChar, 0, 0x10FFFF);
            int clampedLast = Math.Clamp(LastChar, 0, 0x10FFFF);
            if (clampedLast < clampedFirst)
            {
                clampedFirst = 32;
                clampedLast = 126;
            }

            // Protect against memory exhaustion: limit maximum range to 2048 glyphs
            if (clampedLast - clampedFirst > 2048)
            {
                clampedLast = clampedFirst + 2048;
            }

            if (FirstChar != clampedFirst)
            {
                FirstChar = clampedFirst;
                changed = true;
            }
            if (LastChar != clampedLast)
            {
                LastChar = clampedLast;
                changed = true;
            }

            int expectedCount = GlyphCount;
            if (expectedCount <= 0)
            {
                FirstChar = 32;
                LastChar = 126;
                expectedCount = GlyphCount;
                changed = true;
            }

            // Build a lookup of existing glyphs by code point
            var existing = new Dictionary<int, GlyphState>();
            foreach (var g in Glyphs)
            {
                if (g != null)
                {
                    existing[g.CodePoint] = g;
                }
            }

            var normalized = new List<GlyphState>(expectedCount);
            for (int cp = FirstChar; cp <= LastChar; cp++)
            {
                if (existing.TryGetValue(cp, out var glyph))
                {
                    int targetWidth = Math.Clamp(glyph.Width, 1, 512);
                    int targetHeight = CellHeight;
                    int expectedPixels = targetWidth * targetHeight;

                    bool needsResize = glyph.Width != targetWidth ||
                                       glyph.Height != targetHeight ||
                                       glyph.Pixels == null ||
                                       glyph.Pixels.Length != expectedPixels;

                    if (needsResize)
                    {
                        bool[] newPixels = new bool[expectedPixels];
                        if (glyph.Pixels != null && glyph.Pixels.Length > 0)
                        {
                            // Determine original source width and height for 2D stride copying
                            int srcW = glyph.Width;
                            int srcH = glyph.Height;

                            if (srcH > 0 && glyph.Pixels.Length % srcH == 0 && (glyph.Pixels.Length / srcH) > 0)
                            {
                                srcW = glyph.Pixels.Length / srcH;
                            }
                            else if (srcW > 0 && glyph.Pixels.Length % srcW == 0)
                            {
                                srcH = glyph.Pixels.Length / srcW;
                            }
                            else if (srcW <= 0 || srcH <= 0)
                            {
                                srcW = targetWidth;
                                srcH = Math.Max(1, glyph.Pixels.Length / srcW);
                            }

                            int copyW = Math.Min(srcW, targetWidth);
                            int copyH = Math.Min(srcH, targetHeight);

                            for (int y = 0; y < copyH; y++)
                            {
                                int srcRow = y * srcW;
                                int dstRow = y * targetWidth;
                                for (int x = 0; x < copyW; x++)
                                {
                                    int srcIdx = srcRow + x;
                                    if (srcIdx < glyph.Pixels.Length)
                                    {
                                        newPixels[dstRow + x] = glyph.Pixels[srcIdx];
                                    }
                                }
                            }
                        }

                        glyph.Width = targetWidth;
                        glyph.Height = targetHeight;
                        glyph.Pixels = newPixels;
                        changed = true;
                    }
                    normalized.Add(glyph);
                }
                else
                {
                    // Create empty glyph for missing code point
                    normalized.Add(new GlyphState
                    {
                        CodePoint = cp,
                        Width = MaxCellWidth,
                        Height = CellHeight,
                        Pixels = new bool[MaxCellWidth * CellHeight],
                        XOffset = 0,
                        YOffset = 0,
                        XAdvance = MaxCellWidth + 1,
                        IsCustomized = false,
                    });
                    changed = true;
                }
            }

            if (normalized.Count != Glyphs.Count || changed)
            {
                Glyphs = normalized;
                changed = true;
            }

            // Clamp active index
            int clampedActiveIdx = Math.Clamp(ActiveGlyphIndex, 0, Math.Max(0, Glyphs.Count - 1));
            if (ActiveGlyphIndex != clampedActiveIdx)
            {
                ActiveGlyphIndex = clampedActiveIdx;
                changed = true;
            }

            return changed;
        }

        // ── Memory estimation ────────────────────────────────────────────────

        /// <summary>
        /// Estimates the Flash memory size in bytes for the specified export format.
        /// </summary>
        public int EstimateFlashBytes(FontExportFormat format)
        {
            return format switch
            {
                FontExportFormat.AdafruitGfx => EstimateAdafruitGfxFlashBytes(),
                FontExportFormat.U8g2Bdf     => EstimateU8g2BdfBytes(),
                FontExportFormat.Lvgl        => EstimateLvglBytes(),
                FontExportFormat.RawCArray   => EstimateRawCArrayBytes(),
                FontExportFormat.FlipperZero => EstimateFlipperZeroBytes(),
                _                            => EstimateAdafruitGfxFlashBytes(),
            };
        }

        /// <summary>
        /// Estimates the Flash memory size in bytes for Adafruit GFX export on 32-bit MCUs:
        /// Bitmap bytes + GFXglyph array (7 bytes per glyph) + GFXfont struct (16 bytes: 8 bytes pointers + 8 bytes metrics).
        /// </summary>
        public int EstimateAdafruitGfxFlashBytes()
        {
            if (Glyphs == null) return 16;
            int bitmapBytes = 0;
            int count = 0;
            foreach (var glyph in Glyphs)
            {
                if (glyph == null) continue;
                count++;
                var (_, _, cw, ch) = glyph.GetTightBounds();
                if (cw > 0 && ch > 0)
                {
                    bitmapBytes += (cw * ch + 7) / 8;
                }
            }

            int glyphArrayBytes = count * 7; // sizeof(GFXglyph) = 7
            int fontStructBytes = 16; // sizeof(GFXfont) = 16 (8 bytes pointers + 8 bytes metrics)

            return bitmapBytes + glyphArrayBytes + fontStructBytes;
        }

        /// <summary>
        /// Estimates the Flash memory size in bytes for Adafruit GFX export.
        /// Bitmap bytes + GFXglyph array (7 bytes per glyph) + GFXfont struct (13 bytes).
        /// </summary>
        public int EstimateAdafruitGfxBytes()
        {
            if (Glyphs == null) return 13;
            int bitmapBytes = 0;
            int count = 0;
            foreach (var glyph in Glyphs)
            {
                if (glyph == null) continue;
                count++;
                var (_, _, cw, ch) = glyph.GetTightBounds();
                if (cw > 0 && ch > 0)
                {
                    // Adafruit GFX packs bits contiguously across rows, no row padding
                    bitmapBytes += (cw * ch + 7) / 8;
                }
            }

            int glyphArrayBytes = count * 7; // sizeof(GFXglyph) = 7
            int fontStructBytes = 13; // sizeof(GFXfont) = 13

            return bitmapBytes + glyphArrayBytes + fontStructBytes;
        }

        public int EstimateLvglBytes()
        {
            if (Glyphs == null) return 44; // 8 (cmap) + 36 (font_dsc)
            int bitmapBytes = 0;
            int count = 0;
            foreach (var glyph in Glyphs)
            {
                if (glyph == null) continue;
                count++;
                var (_, _, cw, ch) = glyph.GetTightBounds();
                if (cw > 0 && ch > 0)
                {
                    bitmapBytes += (cw * ch + 7) / 8;
                }
            }

            int glyphDscBytes = count * 12;
            int cmapBytes = 8;
            int fontDscBytes = 36;

            return bitmapBytes + glyphDscBytes + cmapBytes + fontDscBytes;
        }

        public int EstimateRawCArrayBytes()
        {
            if (Glyphs == null) return 16;
            int bitmapBytes = 0;
            int count = 0;
            foreach (var glyph in Glyphs)
            {
                if (glyph == null) continue;
                count++;
                var (_, _, cw, ch) = glyph.GetTightBounds();
                if (cw > 0 && ch > 0)
                {
                    bitmapBytes += ch * ((cw + 7) / 8);
                }
                else
                {
                    bitmapBytes += 1;
                }
            }

            int glyphTableBytes = count * 8;
            int structBytes = 16;

            return bitmapBytes + glyphTableBytes + structBytes;
        }

        public int EstimateFlipperZeroBytes()
        {
            if (Glyphs == null) return 16;
            int bitmapBytes = 0;
            int count = 0;
            foreach (var glyph in Glyphs)
            {
                if (glyph == null) continue;
                count++;
                int bytesPerCol = (glyph.Height + 7) / 8;
                bitmapBytes += glyph.Width * bytesPerCol;
            }

            int glyphMetricsBytes = count * 4;
            int headerBytes = 16;

            return bitmapBytes + glyphMetricsBytes + headerBytes;
        }

        public int EstimateU8g2BdfBytes()
        {
            if (Glyphs == null) return 24;
            int bitmapBytes = 0;
            int count = 0;
            foreach (var glyph in Glyphs)
            {
                if (glyph == null) continue;
                count++;
                var (_, _, cw, ch) = glyph.GetTightBounds();
                if (cw > 0 && ch > 0)
                {
                    bitmapBytes += ch * ((cw + 7) / 8);
                }
            }

            int glyphHeaders = count * 4;
            int fontHeader = 24;

            return bitmapBytes + glyphHeaders + fontHeader;
        }
    }
}
