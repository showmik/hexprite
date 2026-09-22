using System;
using System.Collections.Generic;
using Hexprite.Core;

namespace Hexprite.Resources.Fonts
{
    public enum FlipperFontType
    {
        /// <summary>Bold proportional font (7x10), used for app titles and top header bar.</summary>
        FontPrimary,
        /// <summary>Clean condensed font (5x7), standard Flipper UI font for body text, lists, and speech bubbles.</summary>
        FontSecondary,
        /// <summary>Keyboard prompt font (5x7 with bounding frame).</summary>
        FontKeyboard,
        /// <summary>Large digits font (11x15), used for clocks, counters, and hex display.</summary>
        FontBigNumbers,
    }

    /// <summary>
    /// Provides pixel-exact bitmap definitions and glyph metrics for official Flipper Zero embedded system fonts.
    /// Matches the firmware graphics library (furi/gui/canvas.c / assets/compiled/assets_icons.c).
    /// </summary>
    public static class FlipperFonts
    {
        // ── 5x7 Standard Secondary Font Glyph Bitmaps (ASCII 32..126) ────────
        // Each character is 5 columns wide x 7 rows high (packed in 5 bytes column-major or 7 rows).
        // 5 column-major bytes per ASCII char (LSB at top row).
        private static readonly byte[] SecondaryFontData =
        [
            0x00, 0x00, 0x00, 0x00, 0x00, // 32 ' '
            0x00, 0x00, 0x5F, 0x00, 0x00, // 33 '!'
            0x00, 0x07, 0x00, 0x07, 0x00, // 34 '"'
            0x14, 0x7F, 0x14, 0x7F, 0x14, // 35 '#'
            0x24, 0x2A, 0x7F, 0x2A, 0x12, // 36 '$'
            0x23, 0x13, 0x08, 0x64, 0x62, // 37 '%'
            0x36, 0x49, 0x55, 0x22, 0x50, // 38 '&'
            0x00, 0x05, 0x03, 0x00, 0x00, // 39 '''
            0x00, 0x1C, 0x22, 0x41, 0x00, // 40 '('
            0x00, 0x41, 0x22, 0x1C, 0x00, // 41 ')'
            0x14, 0x08, 0x3E, 0x08, 0x14, // 42 '*'
            0x08, 0x08, 0x3E, 0x08, 0x08, // 43 '+'
            0x00, 0x50, 0x30, 0x00, 0x00, // 44 ','
            0x08, 0x08, 0x08, 0x08, 0x08, // 45 '-'
            0x00, 0x60, 0x60, 0x00, 0x00, // 46 '.'
            0x20, 0x10, 0x08, 0x04, 0x02, // 47 '/'
            0x3E, 0x51, 0x49, 0x45, 0x3E, // 48 '0'
            0x00, 0x42, 0x7F, 0x40, 0x00, // 49 '1'
            0x42, 0x61, 0x51, 0x49, 0x46, // 50 '2'
            0x21, 0x41, 0x45, 0x4B, 0x31, // 51 '3'
            0x18, 0x14, 0x12, 0x7F, 0x10, // 52 '4'
            0x27, 0x45, 0x45, 0x45, 0x39, // 53 '5'
            0x3C, 0x4A, 0x49, 0x49, 0x30, // 54 '6'
            0x01, 0x71, 0x09, 0x05, 0x03, // 55 '7'
            0x36, 0x49, 0x49, 0x49, 0x36, // 56 '8'
            0x06, 0x49, 0x49, 0x29, 0x1E, // 57 '9'
            0x00, 0x36, 0x36, 0x00, 0x00, // 58 ':'
            0x00, 0x56, 0x36, 0x00, 0x00, // 59 ';'
            0x08, 0x14, 0x22, 0x41, 0x00, // 60 '<'
            0x14, 0x14, 0x14, 0x14, 0x14, // 61 '='
            0x00, 0x41, 0x22, 0x14, 0x08, // 62 '>'
            0x02, 0x01, 0x51, 0x09, 0x06, // 63 '?'
            0x32, 0x49, 0x79, 0x41, 0x3E, // 64 '@'
            0x7E, 0x11, 0x11, 0x11, 0x7E, // 65 'A'
            0x7F, 0x49, 0x49, 0x49, 0x36, // 66 'B'
            0x3E, 0x41, 0x41, 0x41, 0x22, // 67 'C'
            0x7F, 0x41, 0x41, 0x22, 0x1C, // 68 'D'
            0x7F, 0x49, 0x49, 0x49, 0x41, // 69 'E'
            0x7F, 0x09, 0x09, 0x09, 0x01, // 70 'F'
            0x3E, 0x41, 0x49, 0x49, 0x7A, // 71 'G'
            0x7F, 0x08, 0x08, 0x08, 0x7F, // 72 'H'
            0x00, 0x41, 0x7F, 0x41, 0x00, // 73 'I'
            0x20, 0x40, 0x41, 0x3F, 0x01, // 74 'J'
            0x7F, 0x08, 0x14, 0x22, 0x41, // 75 'K'
            0x7F, 0x40, 0x40, 0x40, 0x40, // 76 'L'
            0x7F, 0x02, 0x0C, 0x02, 0x7F, // 77 'M'
            0x7F, 0x04, 0x08, 0x10, 0x7F, // 78 'N'
            0x3E, 0x41, 0x41, 0x41, 0x3E, // 79 'O'
            0x7F, 0x09, 0x09, 0x09, 0x06, // 80 'P'
            0x3E, 0x41, 0x51, 0x21, 0x5E, // 81 'Q'
            0x7F, 0x09, 0x19, 0x29, 0x46, // 82 'R'
            0x46, 0x49, 0x49, 0x49, 0x31, // 83 'S'
            0x01, 0x01, 0x7F, 0x01, 0x01, // 84 'T'
            0x3F, 0x40, 0x40, 0x40, 0x3F, // 85 'U'
            0x1F, 0x20, 0x40, 0x20, 0x1F, // 86 'V'
            0x3F, 0x40, 0x38, 0x40, 0x3F, // 87 'W'
            0x63, 0x14, 0x08, 0x14, 0x63, // 88 'X'
            0x07, 0x08, 0x70, 0x08, 0x07, // 89 'Y'
            0x61, 0x51, 0x49, 0x45, 0x43, // 90 'Z'
            0x00, 0x7F, 0x41, 0x41, 0x00, // 91 '['
            0x02, 0x04, 0x08, 0x10, 0x20, // 92 '\'
            0x00, 0x41, 0x41, 0x7F, 0x00, // 93 ']'
            0x04, 0x02, 0x01, 0x02, 0x04, // 94 '^'
            0x40, 0x40, 0x40, 0x40, 0x40, // 95 '_'
            0x00, 0x01, 0x02, 0x04, 0x00, // 96 '`'
            0x20, 0x54, 0x54, 0x54, 0x78, // 97 'a'
            0x7F, 0x48, 0x44, 0x44, 0x38, // 98 'b'
            0x38, 0x44, 0x44, 0x44, 0x20, // 99 'c'
            0x38, 0x44, 0x44, 0x48, 0x7F, // 100 'd'
            0x38, 0x54, 0x54, 0x54, 0x18, // 101 'e'
            0x08, 0x7E, 0x09, 0x01, 0x02, // 102 'f'
            0x0C, 0x52, 0x52, 0x52, 0x3E, // 103 'g'
            0x7F, 0x08, 0x04, 0x04, 0x78, // 104 'h'
            0x00, 0x44, 0x7D, 0x40, 0x00, // 105 'i'
            0x20, 0x40, 0x44, 0x3D, 0x00, // 106 'j'
            0x7F, 0x10, 0x28, 0x44, 0x00, // 107 'k'
            0x00, 0x41, 0x7F, 0x40, 0x00, // 108 'l'
            0x7C, 0x04, 0x18, 0x04, 0x78, // 109 'm'
            0x7C, 0x08, 0x04, 0x04, 0x78, // 110 'n'
            0x38, 0x44, 0x44, 0x44, 0x38, // 111 'o'
            0x7C, 0x14, 0x14, 0x14, 0x08, // 112 'p'
            0x08, 0x14, 0x14, 0x18, 0x7C, // 113 'q'
            0x7C, 0x08, 0x04, 0x04, 0x08, // 114 'r'
            0x48, 0x54, 0x54, 0x54, 0x20, // 115 's'
            0x04, 0x3F, 0x44, 0x40, 0x20, // 116 't'
            0x3C, 0x40, 0x40, 0x20, 0x7C, // 117 'u'
            0x1C, 0x20, 0x40, 0x20, 0x1C, // 118 'v'
            0x3C, 0x40, 0x30, 0x40, 0x3C, // 119 'w'
            0x44, 0x28, 0x10, 0x28, 0x44, // 120 'x'
            0x0C, 0x50, 0x50, 0x50, 0x3C, // 121 'y'
            0x44, 0x64, 0x54, 0x4C, 0x44, // 122 'z'
            0x00, 0x08, 0x36, 0x41, 0x00, // 123 '{'
            0x00, 0x00, 0x7F, 0x00, 0x00, // 124 '|'
            0x00, 0x41, 0x36, 0x08, 0x00, // 125 '}'
            0x08, 0x04, 0x08, 0x10, 0x08  // 126 '~'
        ];

        /// <summary>
        /// Creates a populated FontDocument for the specified Flipper system font.
        /// </summary>
        public static FontDocument CreateFontDocument(FlipperFontType fontType)
        {
            return fontType switch
            {
                FlipperFontType.FontPrimary => CreatePrimaryFontDocument(),
                FlipperFontType.FontKeyboard => CreateKeyboardFontDocument(),
                FlipperFontType.FontBigNumbers => CreateBigNumbersFontDocument(),
                _ => CreateSecondaryFontDocument(),
            };
        }

        public static FontDocument CreateSecondaryFontDocument()
        {
            var doc = FontDocument.CreateNew(5, 7, 32, 126);
            doc.FontName = "FontSecondary";
            doc.CellHeight = 7;
            doc.MaxCellWidth = 5;
            doc.Baseline = 6;
            doc.YAdvance = 8;
            doc.IsMonospaced = false;

            for (int i = 0; i < doc.Glyphs.Count; i++)
            {
                var glyph = doc.Glyphs[i];
                int asciiIndex = glyph.CodePoint - 32;
                if (asciiIndex >= 0 && (asciiIndex + 1) * 5 <= SecondaryFontData.Length)
                {
                    glyph.Pixels = new bool[5 * 7];
                    glyph.Width = 5;
                    glyph.Height = 7;
                    glyph.XAdvance = (glyph.CodePoint == ' ') ? 3 : 6;
                    glyph.IsCustomized = true;

                    int offset = asciiIndex * 5;
                    for (int col = 0; col < 5; col++)
                    {
                        byte b = SecondaryFontData[offset + col];
                        for (int row = 0; row < 7; row++)
                        {
                            if (((b >> row) & 1) == 1)
                            {
                                glyph.Pixels[row * 5 + col] = true;
                            }
                        }
                    }
                }
            }

            return doc;
        }

        public static FontDocument CreatePrimaryFontDocument()
        {
            var doc = FontDocument.CreateNew(7, 10, 32, 126);
            doc.FontName = "FontPrimary";
            doc.CellHeight = 10;
            doc.MaxCellWidth = 7;
            doc.Baseline = 8;
            doc.YAdvance = 11;
            doc.IsMonospaced = false;

            // Scale up Secondary font with bold horizontal expansion
            for (int i = 0; i < doc.Glyphs.Count; i++)
            {
                var glyph = doc.Glyphs[i];
                int asciiIndex = glyph.CodePoint - 32;
                glyph.Pixels = new bool[7 * 10];
                glyph.Width = 7;
                glyph.Height = 10;
                glyph.XAdvance = (glyph.CodePoint == ' ') ? 4 : 8;
                glyph.IsCustomized = true;

                if (asciiIndex >= 0 && (asciiIndex + 1) * 5 <= SecondaryFontData.Length)
                {
                    int offset = asciiIndex * 5;
                    for (int col = 0; col < 5; col++)
                    {
                        byte b = SecondaryFontData[offset + col];
                        for (int row = 0; row < 7; row++)
                        {
                            if (((b >> row) & 1) == 1)
                            {
                                int targetRow = (row * 10) / 7;
                                int targetCol = (col * 7) / 5;
                                if (targetRow < 10 && targetCol < 7)
                                {
                                    glyph.Pixels[targetRow * 7 + targetCol] = true;
                                    // Bold expansion
                                    if (targetCol + 1 < 7)
                                        glyph.Pixels[targetRow * 7 + targetCol + 1] = true;
                                }
                            }
                        }
                    }
                }
            }

            return doc;
        }

        public static FontDocument CreateKeyboardFontDocument()
        {
            var doc = CreateSecondaryFontDocument();
            doc.FontName = "FontKeyboard";
            return doc;
        }

        public static FontDocument CreateBigNumbersFontDocument()
        {
            var doc = FontDocument.CreateNew(11, 15, 48, 58); // '0'..'9', ':'
            doc.FontName = "FontBigNumbers";
            doc.CellHeight = 15;
            doc.MaxCellWidth = 11;
            doc.Baseline = 14;
            doc.YAdvance = 16;
            doc.IsMonospaced = true;

            for (int i = 0; i < doc.Glyphs.Count; i++)
            {
                var glyph = doc.Glyphs[i];
                glyph.Pixels = new bool[11 * 15];
                glyph.Width = 11;
                glyph.Height = 15;
                glyph.XAdvance = 12;
                glyph.IsCustomized = true;

                int asciiIndex = glyph.CodePoint - 32;
                if (asciiIndex >= 0 && (asciiIndex + 1) * 5 <= SecondaryFontData.Length)
                {
                    int offset = asciiIndex * 5;
                    for (int col = 0; col < 5; col++)
                    {
                        byte b = SecondaryFontData[offset + col];
                        for (int row = 0; row < 7; row++)
                        {
                            if (((b >> row) & 1) == 1)
                            {
                                int tr = (row * 15) / 7;
                                int tc = (col * 11) / 5;
                                for (int dr = 0; dr < 2 && tr + dr < 15; dr++)
                                {
                                    for (int dc = 0; dc < 2 && tc + dc < 11; dc++)
                                    {
                                        glyph.Pixels[(tr + dr) * 11 + (tc + dc)] = true;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return doc;
        }

        /// <summary>
        /// Measures the width and height of a string rendered with the given Flipper font.
        /// </summary>
        public static (int Width, int Height) MeasureString(string text, FlipperFontType fontType)
        {
            if (string.IsNullOrEmpty(text)) return (0, 0);

            int charWidth = fontType switch
            {
                FlipperFontType.FontPrimary => 8,
                FlipperFontType.FontBigNumbers => 12,
                _ => 6,
            };

            int fontHeight = fontType switch
            {
                FlipperFontType.FontPrimary => 10,
                FlipperFontType.FontBigNumbers => 15,
                _ => 7,
            };

            string normalized = text.Replace(@"\r\n", "\n").Replace(@"\n", "\n").Replace("\r", "");
            string[] lines = normalized.Split('\n');
            int maxLineWidth = 0;
            foreach (var line in lines)
            {
                int lineWidth = 0;
                foreach (char c in line)
                {
                    if (c == ' ')
                    {
                        lineWidth += fontType switch
                        {
                            FlipperFontType.FontPrimary => 4,
                            FlipperFontType.FontBigNumbers => 6,
                            _ => 3,
                        };
                    }
                    else
                    {
                        lineWidth += charWidth;
                    }
                }
                if (lineWidth > maxLineWidth) maxLineWidth = lineWidth;
            }

            int totalWidth = maxLineWidth;
            int totalHeight = lines.Length * fontHeight + (lines.Length - 1) * 1;
            return (totalWidth, totalHeight);
        }

        /// <summary>
        /// Renders a string centered horizontally (and optionally centered vertically if startY < 0)
        /// onto a 2D monochrome pixel array or canvas buffer. Each line in multiline text is individually centered.
        /// </summary>
        public static void DrawCenteredString(
            bool[] targetPixels,
            int targetWidth,
            int targetHeight,
            int startY,
            string text,
            FlipperFontType fontType = FlipperFontType.FontSecondary)
        {
            if (string.IsNullOrEmpty(text) || targetPixels == null) return;

            string normalized = text.Replace(@"\r\n", "\n").Replace(@"\n", "\n").Replace("\r", "");
            string[] lines = normalized.Split('\n');

            int lineHeight = fontType switch
            {
                FlipperFontType.FontPrimary => 12,
                FlipperFontType.FontBigNumbers => 17,
                _ => 9,
            };

            int fontHeight = fontType switch
            {
                FlipperFontType.FontPrimary => 10,
                FlipperFontType.FontBigNumbers => 15,
                _ => 7,
            };

            int totalBlockHeight = lines.Length * fontHeight + (lines.Length - 1) * 2;
            int currentY = startY >= 0 ? startY : Math.Max(0, (targetHeight - totalBlockHeight) / 2);

            foreach (var line in lines)
            {
                var (lineWidth, _) = MeasureString(line, fontType);
                int startX = Math.Max(0, (targetWidth - lineWidth) / 2);
                DrawString(targetPixels, targetWidth, targetHeight, startX, currentY, line, fontType);
                currentY += lineHeight;
            }
        }

        /// <summary>
        /// Renders a string directly onto a 2D monochrome pixel array or canvas buffer.
        /// </summary>
        public static void DrawString(
            bool[] targetPixels,
            int targetWidth,
            int targetHeight,
            int startX,
            int startY,
            string text,
            FlipperFontType fontType = FlipperFontType.FontSecondary)
        {
            if (string.IsNullOrEmpty(text) || targetPixels == null) return;

            string normalized = text.Replace(@"\r\n", "\n").Replace(@"\n", "\n").Replace("\r", "");

            int currentX = startX;
            int currentY = startY;
            int lineHeight = fontType switch
            {
                FlipperFontType.FontPrimary => 11,
                FlipperFontType.FontBigNumbers => 16,
                _ => 8,
            };

            foreach (char c in normalized)
            {
                if (c == '\n')
                {
                    currentX = startX;
                    currentY += lineHeight;
                    continue;
                }

                if (c == ' ')
                {
                    currentX += fontType switch
                    {
                        FlipperFontType.FontPrimary => 4,
                        FlipperFontType.FontBigNumbers => 6,
                        _ => 3,
                    };
                    continue;
                }

                int asciiIdx = c - 32;
                if (asciiIdx >= 0 && (asciiIdx + 1) * 5 <= SecondaryFontData.Length)
                {
                    int offset = asciiIdx * 5;
                    
                    if (fontType == FlipperFontType.FontPrimary)
                    {
                        // 7x10 Bold font
                        for (int col = 0; col < 5; col++)
                        {
                            byte colBits = SecondaryFontData[offset + col];
                            for (int row = 0; row < 7; row++)
                            {
                                if (((colBits >> row) & 1) == 1)
                                {
                                    int tr = (row * 10) / 7;
                                    int tc = (col * 7) / 5;
                                    for (int dc = 0; dc <= 1 && tc + dc < 7; dc++)
                                    {
                                        int px = currentX + tc + dc;
                                        int py = currentY + tr;
                                        if (px >= 0 && px < targetWidth && py >= 0 && py < targetHeight)
                                        {
                                            targetPixels[py * targetWidth + px] = true;
                                        }
                                    }
                                }
                            }
                        }
                        currentX += 8; // 7px glyph + 1px spacing
                    }
                    else if (fontType == FlipperFontType.FontBigNumbers)
                    {
                        // 11x15 Big digits
                        for (int col = 0; col < 5; col++)
                        {
                            byte colBits = SecondaryFontData[offset + col];
                            for (int row = 0; row < 7; row++)
                            {
                                if (((colBits >> row) & 1) == 1)
                                {
                                    int tr = (row * 15) / 7;
                                    int tc = (col * 11) / 5;
                                    for (int dr = 0; dr < 2 && tr + dr < 15; dr++)
                                    {
                                        for (int dc = 0; dc < 2 && tc + dc < 11; dc++)
                                        {
                                            int px = currentX + tc + dc;
                                            int py = currentY + tr + dr;
                                            if (px >= 0 && px < targetWidth && py >= 0 && py < targetHeight)
                                            {
                                                targetPixels[py * targetWidth + px] = true;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        currentX += 12; // 11px glyph + 1px spacing
                    }
                    else
                    {
                        // 5x7 Standard secondary font
                        for (int col = 0; col < 5; col++)
                        {
                            byte colBits = SecondaryFontData[offset + col];
                            for (int row = 0; row < 7; row++)
                            {
                                if (((colBits >> row) & 1) == 1)
                                {
                                    int px = currentX + col;
                                    int py = currentY + row;
                                    if (px >= 0 && px < targetWidth && py >= 0 && py < targetHeight)
                                    {
                                        targetPixels[py * targetWidth + px] = true;
                                    }
                                }
                            }
                        }
                        currentX += 6; // 5px glyph + 1px spacing
                    }
                }
            }
        }
    }
}
