using System;
using Hexprite.Core;
using Hexprite.Rendering;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FontPreviewRendererTests
    {
        // ── Helper Methods ───────────────────────────────────────────────────

        private static FontDocument CreateSimpleTestFont(int cellWidth = 8, int cellHeight = 8)
        {
            var doc = FontDocument.CreateNew(cellWidth, cellHeight, 32, 126);
            doc.FontName = "TestFont";
            doc.Baseline = 6;
            doc.YAdvance = cellHeight;
            return doc;
        }

        private static void FillGlyphBox(GlyphState glyph, int x, int y, int w, int h)
        {
            for (int r = y; r < y + h && r < glyph.Height; r++)
            {
                for (int c = x; c < x + w && c < glyph.Width; c++)
                {
                    glyph.Pixels[r * glyph.Width + c] = true;
                }
            }
        }

        private static int CountInkPixels(bool[,] bitmap)
        {
            int count = 0;
            int w = bitmap.GetLength(0);
            int h = bitmap.GetLength(1);
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    if (bitmap[x, y]) count++;
                }
            }
            return count;
        }

        // ── Tier 1: Feature Coverage ─────────────────────────────────────────

        [Fact]
        public void RenderPreviewText_NullFont_ReturnsEmptyArray()
        {
            var result = FontPreviewRenderer.RenderPreviewText(null!, "Hello");
            Assert.NotNull(result);
            Assert.Equal(0, result.GetLength(0));
            Assert.Equal(0, result.GetLength(1));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void RenderPreviewText_NullOrEmptyText_ReturnsEmptyArray(string? text)
        {
            var font = CreateSimpleTestFont();
            var result = FontPreviewRenderer.RenderPreviewText(font, text!);
            Assert.NotNull(result);
            Assert.Equal(0, result.GetLength(0));
            Assert.Equal(0, result.GetLength(1));
        }

        [Fact]
        public void RenderPreviewText_SingleGlyph_RendersExactPixelLocations()
        {
            var font = CreateSimpleTestFont(8, 8);
            var glyph = font.GetGlyph('A')!;
            glyph.XAdvance = 8;
            glyph.XOffset = 0;
            glyph.YOffset = 0;

            // Draw a single diagonal cross: (1,1) and (2,2)
            glyph.Pixels[1 * 8 + 1] = true;
            glyph.Pixels[2 * 8 + 2] = true;

            bool[,] result = FontPreviewRenderer.RenderPreviewText(font, "A");

            Assert.True(result.GetLength(0) >= 8, "Render width should accommodate glyph width and advance");
            Assert.True(result.GetLength(1) >= 8, "Render height should accommodate cell height");

            // Verify specific pixels are true
            Assert.True(result[1, 1], "Pixel (1,1) must be ink");
            Assert.True(result[2, 2], "Pixel (2,2) must be ink");

            // Verify surrounding pixels are false
            Assert.False(result[0, 0], "Pixel (0,0) should be empty");
            Assert.False(result[1, 2], "Pixel (1,2) should be empty");
            Assert.False(result[2, 1], "Pixel (2,1) should be empty");
            Assert.Equal(2, CountInkPixels(result));
        }

        [Fact]
        public void RenderPreviewText_TwoGlyphs_AdvancesCursorHorizontally()
        {
            var font = CreateSimpleTestFont(8, 8);
            var glyphA = font.GetGlyph('A')!;
            glyphA.Width = 6;
            glyphA.XAdvance = 7; // advance cursor by 7px
            glyphA.Pixels[0] = true; // ink at (0,0) of 'A'

            var glyphB = font.GetGlyph('B')!;
            glyphB.Width = 6;
            glyphB.XAdvance = 7;
            glyphB.Pixels[0] = true; // ink at (0,0) of 'B'

            bool[,] result = FontPreviewRenderer.RenderPreviewText(font, "AB");

            // 'A' pixel at (0,0)
            Assert.True(result[0, 0], "'A' ink should be at x=0");
            // 'B' pixel at cursor advance (7,0)
            Assert.True(result[7, 0], "'B' ink should be at x=7");
            // Space in between (1..6, 0) should be empty
            for (int x = 1; x < 7; x++)
            {
                Assert.False(result[x, 0], $"Space at x={x} should be false");
            }
            Assert.Equal(2, CountInkPixels(result));
        }

        [Fact]
        public void RenderPreviewText_MultilineText_RespectsBaselineAndYAdvance()
        {
            var font = CreateSimpleTestFont(8, 10);
            font.Baseline = 8;
            font.YAdvance = 12; // vertical line spacing

            var glyphA = font.GetGlyph('A')!;
            glyphA.Pixels[0] = true; // top-left pixel
            glyphA.XAdvance = 8;

            var glyphB = font.GetGlyph('B')!;
            glyphB.Pixels[0] = true; // top-left pixel
            glyphB.XAdvance = 8;

            bool[,] result = FontPreviewRenderer.RenderPreviewText(font, "A\nB");

            // First line 'A' ink at (0, 0)
            Assert.True(result[0, 0], "Line 1 'A' pixel should be at (0,0)");
            // Second line 'B' ink at (0, YAdvance = 12)
            Assert.True(result[0, 12], "Line 2 'B' pixel should be at (0,12)");
            // Total ink should be exactly 2 pixels
            Assert.Equal(2, CountInkPixels(result));
            // Total height should be at least 12 + cell descent
            Assert.True(result.GetLength(1) >= 12 + (font.CellHeight - font.Baseline));
        }

        [Fact]
        public void RenderPreviewText_KerningPairs_AppliesHorizontalOffset()
        {
            var font = CreateSimpleTestFont(8, 8);
            var glyphA = font.GetGlyph('A')!;
            glyphA.Width = 6;
            glyphA.XAdvance = 8;
            glyphA.Pixels[0] = true;

            var glyphV = font.GetGlyph('V')!;
            glyphV.Width = 6;
            glyphV.XAdvance = 8;
            glyphV.Pixels[0] = true;

            // Without kerning: 'V' should be drawn at x = 8
            var resultNoKern = FontPreviewRenderer.RenderPreviewText(font, "AV");
            Assert.True(resultNoKern[8, 0]);

            // Add negative kerning: -2 between 'A' and 'V'
            font.KerningPairs.Add(new KerningPair { Left = 'A', Right = 'V', Adjustment = -2 });
            var resultWithKern = FontPreviewRenderer.RenderPreviewText(font, "AV");
            Assert.True(resultWithKern[6, 0], "'V' should be shifted 2px left to x=6");
            Assert.False(resultWithKern[8, 0], "Original position x=8 should now be empty");

            // Positive kerning: +3 between 'V' and 'A'
            font.KerningPairs.Add(new KerningPair { Left = 'V', Right = 'A', Adjustment = 3 });
            var resultPosKern = FontPreviewRenderer.RenderPreviewText(font, "VA");
            // 'V' at 0, 'A' at 8 + 3 = 11
            Assert.True(resultPosKern[0, 0]);
            Assert.True(resultPosKern[11, 0], "'A' should be shifted right to x=11");
        }

        [Fact]
        public void RenderPreviewText_GlyphOffsets_ShiftsRenderingPosition()
        {
            var font = CreateSimpleTestFont(8, 8);
            var glyph = font.GetGlyph('C')!;
            glyph.Pixels[0] = true; // Top-left pixel of glyph
            glyph.XOffset = 3;
            glyph.YOffset = 2;
            glyph.XAdvance = 8;

            bool[,] result = FontPreviewRenderer.RenderPreviewText(font, "C");

            // Rendered at (XOffset, YOffset) = (3, 2)
            Assert.True(result[3, 2], "Ink should be shifted by XOffset=3, YOffset=2");
            Assert.False(result[0, 0], "Position (0,0) should be empty");
            Assert.Equal(1, CountInkPixels(result));
        }

        // ── Tier 2: Boundary & Corner Cases ──────────────────────────────────

        [Fact]
        public void RenderPreviewText_1x1Glyph_RendersSinglePixelBitmap()
        {
            var font = CreateSimpleTestFont(1, 1);
            font.Baseline = 1;
            font.YAdvance = 1;
            var glyph = font.GetGlyph('A')!;
            glyph.Width = 1;
            glyph.Height = 1;
            glyph.Pixels = [true];
            glyph.XAdvance = 1;

            bool[,] result = FontPreviewRenderer.RenderPreviewText(font, "A");

            Assert.True(result.GetLength(0) >= 1);
            Assert.True(result.GetLength(1) >= 1);
            Assert.True(result[0, 0], "1x1 ink pixel should be set");
            Assert.Equal(1, CountInkPixels(result));
        }

        [Fact]
        public void RenderPreviewText_BlankSpaceGlyph_AdvancesCursorWithoutInk()
        {
            var font = CreateSimpleTestFont(8, 8);
            var glyphSpace = font.GetGlyph(' ')!;
            glyphSpace.Width = 4;
            glyphSpace.XAdvance = 4;
            // Space has 0 ink pixels

            var glyphA = font.GetGlyph('A')!;
            glyphA.Width = 5;
            glyphA.XAdvance = 6;
            glyphA.Pixels[0] = true;

            var glyphB = font.GetGlyph('B')!;
            glyphB.Width = 5;
            glyphB.XAdvance = 6;
            glyphB.Pixels[0] = true;

            bool[,] result = FontPreviewRenderer.RenderPreviewText(font, "A B");

            // 'A' ink at x=0
            Assert.True(result[0, 0]);
            // Space advances by 4, so 'B' is at 6 ('A' advance) + 4 (' ' advance) = 10
            Assert.True(result[10, 0], "'B' should appear at x=10 after space advance");
            Assert.Equal(2, CountInkPixels(result));
        }

        [Fact]
        public void RenderPreviewText_MissingGlyphs_GracefulFallThroughWithoutCrash()
        {
            // Font only covers code points 65..67 ('A'..'C')
            var font = FontDocument.CreateNew(8, 8, 65, 67);
            font.GetGlyph('A')!.Pixels[0] = true;
            font.GetGlyph('C')!.Pixels[0] = true;

            // 'Z' (90) is missing from the font
            bool[,] result = FontPreviewRenderer.RenderPreviewText(font, "AZC");

            // Should not crash, and valid glyphs should still be processed
            Assert.True(result.GetLength(0) > 0);
            Assert.True(result.GetLength(1) > 0);
            Assert.Equal(2, CountInkPixels(result));
        }

        [Fact]
        public void RenderPreviewText_NegativeOffsets_ShiftsOriginCorrectlyWithoutCrash()
        {
            var font = CreateSimpleTestFont(8, 8);
            var glyph = font.GetGlyph('A')!;
            glyph.Pixels[0] = true;
            // Negative offsets shift bounding box left/up
            glyph.XOffset = -3;
            glyph.YOffset = -4;
            glyph.XAdvance = 8;

            bool[,] result = FontPreviewRenderer.RenderPreviewText(font, "A");

            // ShiftX and ShiftY must normalize negative coordinates to non-negative array indices
            Assert.True(result.GetLength(0) >= 8);
            Assert.True(result.GetLength(1) >= 8);
            // With shiftX = 3, shiftY = 4, drawX = 0 + (-3) + 3 = 0, drawY = 0 + (-4) + 4 = 0
            Assert.True(result[0, 0], "Top-left ink pixel must be safely shifted to (0,0)");
            Assert.Equal(1, CountInkPixels(result));
        }

        [Fact]
        public void RenderPreviewText_ConsecutiveNewlines_CreatesEmptyLines()
        {
            var font = CreateSimpleTestFont(8, 8);
            font.Baseline = 6;
            font.YAdvance = 10;

            font.GetGlyph('A')!.Pixels[0] = true;
            font.GetGlyph('B')!.Pixels[0] = true;

            // Triple newline: line 0 = 'A', line 1 = empty, line 2 = empty, line 3 = 'B'
            bool[,] result = FontPreviewRenderer.RenderPreviewText(font, "A\n\n\nB");

            Assert.True(result[0, 0], "'A' at (0,0)");
            // 'B' should be at line 3: 3 * YAdvance = 30
            Assert.True(result[0, 30], "'B' should be at y=30");
            Assert.True(result.GetLength(1) >= 30 + 8);
            Assert.Equal(2, CountInkPixels(result));
        }

        [Fact]
        public void RenderPreviewText_NonAsciiCodePoints_RendersAccurately()
        {
            // Font created for Greek range 0x0390..0x0399
            var font = FontDocument.CreateNew(8, 8, 0x0390, 0x0399);
            var alpha = font.GetGlyph(0x0391)!; // Greek Capital Alpha 'Α'
            alpha.Pixels[0] = true;
            alpha.Pixels[7] = true;

            string text = "\u0391"; // 'Α'
            bool[,] result = FontPreviewRenderer.RenderPreviewText(font, text);

            Assert.True(result.GetLength(0) >= 8);
            Assert.True(result.GetLength(1) >= 8);
            Assert.True(result[0, 0]);
            Assert.True(result[7, 0]);
            Assert.Equal(2, CountInkPixels(result));
        }

        [Fact]
        public void RenderPreviewText_XAdvanceGreaterThanWidth_AllocatesFullBuffer()
        {
            var font = CreateSimpleTestFont(5, 8);
            var glyph = font.GetGlyph('X')!;
            glyph.Width = 5;
            glyph.XAdvance = 12; // 7px of trailing whitespace
            glyph.Pixels[0] = true;

            bool[,] result = FontPreviewRenderer.RenderPreviewText(font, "X");

            // Width should account for trailing cursor advance (at least 12)
            Assert.True(result.GetLength(0) >= 12, "Result width should reserve space for XAdvance");
            Assert.True(result[0, 0]);
            Assert.Equal(1, CountInkPixels(result));
        }

        // ── Tier 3: Cross-Feature Interactions ───────────────────────────────

        [Fact]
        public void RenderPreviewText_MonospaceVsProportional_TextWidthReflectsAdvances()
        {
            var font = CreateSimpleTestFont(8, 8);
            var glyphI = font.GetGlyph('I')!;
            glyphI.Width = 3;
            glyphI.XAdvance = 4;
            glyphI.Pixels[0] = true;

            var glyphW = font.GetGlyph('W')!;
            glyphW.Width = 7;
            glyphW.XAdvance = 8;
            glyphW.Pixels[0] = true;

            // Proportional: "IW" width is 4 + 8 = 12
            bool[,] propResult = FontPreviewRenderer.RenderPreviewText(font, "IW");
            Assert.True(propResult.GetLength(0) >= 12);
            Assert.True(propResult[0, 0], "'I' at x=0");
            Assert.True(propResult[4, 0], "'W' at x=4");

            // Monospaced: enforce uniform advance of 8
            glyphI.XAdvance = 8;
            glyphW.XAdvance = 8;
            bool[,] monoResult = FontPreviewRenderer.RenderPreviewText(font, "IW");
            Assert.True(monoResult.GetLength(0) >= 16);
            Assert.True(monoResult[0, 0], "'I' at x=0");
            Assert.True(monoResult[8, 0], "'W' at x=8");
        }

        [Fact]
        public void RenderPreviewText_MixedGlyphSizes_AccommodatesTallestAndWidest()
        {
            var font = CreateSimpleTestFont(8, 8);
            var small = font.GetGlyph('s')!;
            small.Width = 4;
            small.Height = 4;
            small.Pixels = new bool[small.Width * small.Height];
            small.XAdvance = 5;
            FillGlyphBox(small, 0, 0, 4, 4);

            var tall = font.GetGlyph('t')!;
            tall.Width = 6;
            tall.Height = 12;
            tall.Pixels = new bool[tall.Width * tall.Height];
            tall.XAdvance = 7;
            FillGlyphBox(tall, 0, 0, 6, 12);

            bool[,] result = FontPreviewRenderer.RenderPreviewText(font, "st");

            Assert.True(result.GetLength(0) >= 5 + 7);
            Assert.True(result.GetLength(1) >= 12);
            // Verify all ink pixels from both glyphs are rendered (16 + 72 = 88)
            Assert.Equal(4 * 4 + 6 * 12, CountInkPixels(result));
        }

        [Fact]
        public void RenderPreviewText_KerningAndOffsetsCombined_AccuratelyComputesCoordinates()
        {
            var font = CreateSimpleTestFont(8, 8);
            var g1 = font.GetGlyph('A')!;
            g1.XAdvance = 10;
            g1.XOffset = 1;
            g1.Pixels[0] = true;

            var g2 = font.GetGlyph('B')!;
            g2.XAdvance = 10;
            g2.XOffset = 2;
            g2.Pixels[0] = true;

            // Kerning of -3 between A and B
            font.KerningPairs.Add(new KerningPair { Left = 'A', Right = 'B', Adjustment = -3 });

            bool[,] result = FontPreviewRenderer.RenderPreviewText(font, "AB");

            // g1 ink at: cursorX(0) + XOffset(1) = 1
            Assert.True(result[1, 0], "g1 should be at x=1");
            // g2 ink at: cursorX(10) + Kerning(-3) + XOffset(2) = 9
            Assert.True(result[9, 0], "g2 should be at x=9");
            Assert.Equal(2, CountInkPixels(result));
        }

        // ── Tier 4: Real-World Scenarios ─────────────────────────────────────

        [Fact]
        public void RenderPreviewText_RealisticSentence_CalculatesFullLayoutCleanly()
        {
            var font = CreateSimpleTestFont(6, 8);
            font.Baseline = 6;
            font.YAdvance = 9;

            // Populate simple recognizable glyphs for sentence
            string sentence = "Hex 42!";
            foreach (char c in sentence)
            {
                var g = font.GetGlyph(c);
                if (g != null && c != ' ')
                {
                    g.Width = 5;
                    g.XAdvance = 6;
                    // Draw a 3x3 block in each character
                    FillGlyphBox(g, 1, 1, 3, 3);
                }
                else if (g != null && c == ' ')
                {
                    g.Width = 3;
                    g.XAdvance = 4;
                }
            }

            bool[,] result = FontPreviewRenderer.RenderPreviewText(font, sentence);

            // Sentence has 6 visible characters: H, e, x, 4, 2, ! (6 * 9 = 54 pixels)
            Assert.Equal(6 * 9, CountInkPixels(result));
            // Total advance = 6 * 6 + 1 * 4 = 40
            Assert.True(result.GetLength(0) >= 40);
            Assert.True(result.GetLength(1) >= 8);
        }

        [Fact]
        public void RenderPreviewText_MultilineBanner_PreservesConsistentLineGrid()
        {
            var font = CreateSimpleTestFont(8, 8);
            font.Baseline = 6;
            font.YAdvance = 10;

            // Draw 2x2 square for test characters
            foreach (char c in "ABC123XYZ")
            {
                var g = font.GetGlyph(c);
                if (g != null)
                {
                    g.Width = 6;
                    g.XAdvance = 7;
                    FillGlyphBox(g, 0, 0, 2, 2);
                }
            }

            string banner = "ABC\n123\nXYZ";
            bool[,] result = FontPreviewRenderer.RenderPreviewText(font, banner);

            // 3 lines * 3 chars * 4 pixels = 36 pixels
            Assert.Equal(36, CountInkPixels(result));

            // Line 0 ink at y=0,1
            Assert.True(result[0, 0]);
            Assert.True(result[0, 1]);
            // Line 1 ink at y=10,11
            Assert.True(result[0, 10]);
            Assert.True(result[0, 11]);
            // Line 2 ink at y=20,21
            Assert.True(result[0, 20]);
            Assert.True(result[0, 21]);

            // Ensure vertical separation between lines is blank
            for (int y = 2; y < 10; y++)
            {
                Assert.False(result[0, y], $"Row {y} between line 1 and 2 should be empty");
            }
        }
    }
}
