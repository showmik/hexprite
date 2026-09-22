using System.Collections.Generic;
using Hexprite.Core;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FontDocumentTests
    {
        [Fact]
        public void CreateNew_InitializesCorrectly()
        {
            var doc = FontDocument.CreateNew(6, 8, 32, 126);

            Assert.Equal(8, doc.CellHeight);
            Assert.Equal(6, doc.MaxCellWidth);
            Assert.Equal(32, doc.FirstChar);
            Assert.Equal(126, doc.LastChar);
            Assert.Equal(95, doc.Glyphs.Count);
            
            var firstGlyph = doc.Glyphs[0];
            Assert.Equal(32, firstGlyph.CodePoint);
            Assert.Equal(6, firstGlyph.Width);
            Assert.Equal(8, firstGlyph.Height);
            Assert.Equal(48, firstGlyph.Pixels.Length);
            Assert.Equal(0, firstGlyph.YOffset);
            Assert.Equal(7, firstGlyph.XAdvance); // Width + 1
        }

        [Fact]
        public void GetGlyph_ValidCodePoint_ReturnsGlyph()
        {
            var doc = FontDocument.CreateNew(8, 8, 32, 126);
            
            var glyph = doc.GetGlyph(65); // 'A'
            
            Assert.NotNull(glyph);
            Assert.Equal(65, glyph.CodePoint);
        }

        [Fact]
        public void GetGlyph_InvalidCodePoint_ReturnsNull()
        {
            var doc = FontDocument.CreateNew(8, 8, 32, 126);
            
            var glyph1 = doc.GetGlyph(31);
            var glyph2 = doc.GetGlyph(127);
            
            Assert.Null(glyph1);
            Assert.Null(glyph2);
        }

        [Fact]
        public void GetGlyphIndex_ValidCodePoint_ReturnsIndex()
        {
            var doc = FontDocument.CreateNew(8, 8, 32, 126);
            
            int index = doc.GetGlyphIndex(32);
            
            Assert.Equal(0, index);
        }

        [Fact]
        public void GetGlyphIndex_InvalidCodePoint_ReturnsMinusOne()
        {
            var doc = FontDocument.CreateNew(8, 8, 32, 126);
            
            Assert.Equal(-1, doc.GetGlyphIndex(31));
            Assert.Equal(-1, doc.GetGlyphIndex(127));
        }

        [Fact]
        public void NormalizeGlyphs_ExpandsRange_AddsMissingGlyphs()
        {
            var doc = FontDocument.CreateNew(8, 8, 32, 33);
            
            // Expand range
            doc.LastChar = 34;
            
            bool changed = doc.NormalizeGlyphs();
            
            Assert.True(changed);
            Assert.Equal(3, doc.Glyphs.Count);
            Assert.Equal(32, doc.Glyphs[0].CodePoint);
            Assert.Equal(33, doc.Glyphs[1].CodePoint);
            Assert.Equal(34, doc.Glyphs[2].CodePoint);
        }

        [Fact]
        public void NormalizeGlyphs_ShrinksRange_RemovesExtraGlyphs()
        {
            var doc = FontDocument.CreateNew(8, 8, 32, 34);
            
            // Shrink range
            doc.LastChar = 33;
            
            bool changed = doc.NormalizeGlyphs();
            
            Assert.True(changed);
            Assert.Equal(2, doc.Glyphs.Count);
            Assert.Equal(32, doc.Glyphs[0].CodePoint);
            Assert.Equal(33, doc.Glyphs[1].CodePoint);
        }

        [Fact]
        public void NormalizeGlyphs_FixesInvalidActiveGlyphIndex()
        {
            var doc = FontDocument.CreateNew(8, 8, 32, 34);
            doc.ActiveGlyphIndex = 5; // Out of bounds
            
            bool changed = doc.NormalizeGlyphs();
            
            Assert.True(changed);
            Assert.Equal(2, doc.ActiveGlyphIndex); // Max valid index is 2
        }

        [Fact]
        public void Clone_CreatesDeepCopy()
        {
            var doc = FontDocument.CreateNew(8, 8, 32, 33);
            doc.FontName = "TestFont";
            doc.KerningPairs.Add(new KerningPair { Left = 32, Right = 33, Adjustment = -1 });
            
            var clone = doc.Clone();
            
            Assert.NotSame(doc, clone);
            Assert.Equal("TestFont", clone.FontName);
            
            Assert.NotSame(doc.Glyphs, clone.Glyphs);
            Assert.Equal(2, clone.Glyphs.Count);
            Assert.NotSame(doc.Glyphs[0], clone.Glyphs[0]);
            
            Assert.NotSame(doc.KerningPairs, clone.KerningPairs);
            Assert.Single(clone.KerningPairs);
            Assert.Equal(-1, clone.KerningPairs[0].Adjustment);
        }

        [Fact]
        public void EstimateAdafruitGfxBytes_ReturnsReasonableEstimate()
        {
            var doc = FontDocument.CreateNew(8, 8, 32, 33);
            // Glyph 1 (32): empty
            // Glyph 2 (33): one pixel
            doc.Glyphs[1].Pixels[0] = true;
            
            int estimated = doc.EstimateAdafruitGfxBytes();
            
            // 2 glyphs * 7 bytes = 14 bytes
            // Font struct = 13 bytes
            // Bitmap bytes: 1 pixel -> 1x1 bounding box -> 1 byte
            // Total: 14 + 13 + 1 = 28
            
            Assert.Equal(28, estimated);
        }

        [Fact]
        public void NormalizeGlyphs_ReversedCharacterRange_ResetsToAsciiRange()
        {
            var doc = FontDocument.CreateNew(8, 8);
            doc.FirstChar = 100;
            doc.LastChar = 50; // Invalid / reversed

            bool changed = doc.NormalizeGlyphs();

            Assert.True(changed);
            Assert.Equal(32, doc.FirstChar);
            Assert.Equal(126, doc.LastChar);
            Assert.Equal(95, doc.Glyphs.Count);
        }

        [Fact]
        public void NormalizeGlyphs_NegativeAndExtremeDimensions_ClampsSafely()
        {
            var doc = FontDocument.CreateNew(8, 8);
            doc.CellHeight = -10;
            doc.MaxCellWidth = 1000;
            doc.Baseline = 50;
            doc.YAdvance = -5;

            bool changed = doc.NormalizeGlyphs();

            Assert.True(changed);
            Assert.Equal(1, doc.CellHeight);
            Assert.Equal(512, doc.MaxCellWidth);
            Assert.Equal(1, doc.Baseline);
            Assert.Equal(1, doc.YAdvance);
        }

        [Fact]
        public void NormalizeGlyphs_CorruptedGlyphPixelArrays_RepairsPixelsToExactSize()
        {
            var doc = FontDocument.CreateNew(8, 8, 32, 33);
            doc.Glyphs[0].Pixels = null!; // Corrupt null array
            doc.Glyphs[1].Pixels = [true]; // Corrupt truncated array

            bool changed = doc.NormalizeGlyphs();

            Assert.True(changed);
            Assert.NotNull(doc.Glyphs[0].Pixels);
            Assert.Equal(64, doc.Glyphs[0].Pixels.Length);
            Assert.NotNull(doc.Glyphs[1].Pixels);
            Assert.Equal(64, doc.Glyphs[1].Pixels.Length);
        }

        [Fact]
        public void NormalizeGlyphs_2DStride_PreservesRowsOnWidthExpansion()
        {
            // 4x8 glyph with specific pixels set on row 0, row 1, and row 2
            var doc = FontDocument.CreateNew(4, 8, 65, 65);
            var glyph = doc.Glyphs[0];
            glyph.Width = 4;
            glyph.Height = 8;
            glyph.Pixels = new bool[32];

            // Set pixel on row 0 at x=1
            glyph.Pixels[0 * 4 + 1] = true;
            // Set pixel on row 1 at x=2
            glyph.Pixels[1 * 4 + 2] = true;
            // Set pixel on row 2 at x=3
            glyph.Pixels[2 * 4 + 3] = true;

            // Expand glyph width to 8
            glyph.Width = 8;
            bool changed = doc.NormalizeGlyphs();

            Assert.True(changed);
            Assert.Equal(8, glyph.Width);
            Assert.Equal(8, glyph.Height);
            Assert.Equal(64, glyph.Pixels.Length);

            // Row 0, x=1 should be true
            Assert.True(glyph.Pixels[0 * 8 + 1], "Row 0 pixel at x=1 must be preserved");
            // Row 1, x=2 should be true
            Assert.True(glyph.Pixels[1 * 8 + 2], "Row 1 pixel at x=2 must be preserved");
            // Row 2, x=3 should be true
            Assert.True(glyph.Pixels[2 * 8 + 3], "Row 2 pixel at x=3 must be preserved");

            // Verify that 1D linear copy did NOT scramble row 1 into row 0 (which would be index 6 = x=6, y=0)
            Assert.False(glyph.Pixels[0 * 8 + 6], "1D linear copy corruption detected at index 6");
            // Empty columns 4..7 on row 0, 1, 2 must remain false
            for (int x = 4; x < 8; x++)
            {
                Assert.False(glyph.Pixels[0 * 8 + x]);
                Assert.False(glyph.Pixels[1 * 8 + x]);
                Assert.False(glyph.Pixels[2 * 8 + x]);
            }
        }

        [Fact]
        public void NormalizeGlyphs_2DStride_PreservesRowsOnWidthShrink()
        {
            // 8x8 glyph with pixels on row 0 (x=2) and row 1 (x=3 and x=6)
            var doc = FontDocument.CreateNew(8, 8, 65, 65);
            var glyph = doc.Glyphs[0];
            glyph.Pixels[0 * 8 + 2] = true;
            glyph.Pixels[1 * 8 + 3] = true;
            glyph.Pixels[1 * 8 + 6] = true; // should be clipped when shrinking to 4

            // Shrink glyph width to 4
            glyph.Width = 4;
            bool changed = doc.NormalizeGlyphs();

            Assert.True(changed);
            Assert.Equal(4, glyph.Width);
            Assert.Equal(8, glyph.Height);
            Assert.Equal(32, glyph.Pixels.Length);

            // Row 0 at x=2 preserved
            Assert.True(glyph.Pixels[0 * 4 + 2]);
            // Row 1 at x=3 preserved
            Assert.True(glyph.Pixels[1 * 4 + 3]);
            // Row 1 at x=0, 1, 2 should be false
            Assert.False(glyph.Pixels[1 * 4 + 0]);
            Assert.False(glyph.Pixels[1 * 4 + 1]);
            Assert.False(glyph.Pixels[1 * 4 + 2]);
        }

        [Fact]
        public void NormalizeGlyphs_2DStride_PreservesRowsOnHeightChange()
        {
            // 6x8 glyph, cell height changes to 12
            var doc = FontDocument.CreateNew(6, 8, 65, 65);
            var glyph = doc.Glyphs[0];
            glyph.Pixels[7 * 6 + 4] = true; // Last row (row 7) at x=4

            doc.CellHeight = 12;
            bool changed = doc.NormalizeGlyphs();

            Assert.True(changed);
            Assert.Equal(6, glyph.Width);
            Assert.Equal(12, glyph.Height);
            Assert.Equal(72, glyph.Pixels.Length);

            // Row 7 at x=4 must be preserved
            Assert.True(glyph.Pixels[7 * 6 + 4]);
            // Newly padded rows 8..11 must be false
            for (int y = 8; y < 12; y++)
            {
                for (int x = 0; x < 6; x++)
                {
                    Assert.False(glyph.Pixels[y * 6 + x]);
                }
            }
        }

        [Fact]
        public void CreateNew_NegativeOrZeroDimensions_ClampedToValidValues()
        {
            var doc1 = FontDocument.CreateNew(0, -5);
            Assert.Equal(1, doc1.MaxCellWidth);
            Assert.Equal(1, doc1.CellHeight);
            Assert.NotEmpty(doc1.Glyphs);
            Assert.Single(doc1.Glyphs[0].Pixels);

            var doc2 = FontDocument.CreateNew(600, 1000);
            Assert.Equal(512, doc2.MaxCellWidth);
            Assert.Equal(512, doc2.CellHeight);
        }

        [Fact]
        public void CreateNew_ReversedRange_ResetsToAsciiPrintable()
        {
            var doc = FontDocument.CreateNew(8, 8, 120, 40);
            Assert.Equal(32, doc.FirstChar);
            Assert.Equal(126, doc.LastChar);
            Assert.Equal(95, doc.Glyphs.Count);
        }

        [Fact]
        public void CreateNew_ExcessiveRange_CappedToPreventMemoryExhaustion()
        {
            var doc = FontDocument.CreateNew(8, 8, 0, 10000);
            Assert.True(doc.Glyphs.Count <= 2049);
            Assert.Equal(0, doc.FirstChar);
            Assert.Equal(2048, doc.LastChar);
        }

        [Fact]
        public void Clone_NullCollectionsAndNullItems_SafelyHandledWithoutException()
        {
            var doc = new FontDocument
            {
                Glyphs = null!,
                KerningPairs = null!,
                PreMonoWidths = null!,
                ExportSettings = null,
                FontName = null!,
            };

            var clone = doc.Clone();

            Assert.NotNull(clone);
            Assert.NotNull(clone.Glyphs);
            Assert.Empty(clone.Glyphs);
            Assert.NotNull(clone.KerningPairs);
            Assert.Empty(clone.KerningPairs);
            Assert.NotNull(clone.PreMonoWidths);
            Assert.Empty(clone.PreMonoWidths);
            Assert.NotNull(clone.ExportSettings);
            Assert.Equal("myFont", clone.FontName);
        }

        [Fact]
        public void Clone_CollectionsContainingNullElements_FiltersOutNulls()
        {
            var doc = new FontDocument
            {
                Glyphs = [new GlyphState { CodePoint = 65, Width = 8, Height = 8, Pixels = new bool[64] }, null!],
                KerningPairs = [new KerningPair { Left = 65, Right = 66, Adjustment = -1 }, null!],
            };

            var clone = doc.Clone();

            Assert.Single(clone.Glyphs);
            Assert.Equal(65, clone.Glyphs[0].CodePoint);
            Assert.Single(clone.KerningPairs);
            Assert.Equal(-1, clone.KerningPairs[0].Adjustment);
        }

        [Fact]
        public void NormalizeGlyphs_NullCollections_RepairsCleanly()
        {
            var doc = new FontDocument
            {
                Glyphs = null!,
                KerningPairs = null!,
                PreMonoWidths = null!,
                ExportSettings = null,
                FirstChar = 65,
                LastChar = 66,
            };

            bool changed = doc.NormalizeGlyphs();

            Assert.True(changed);
            Assert.NotNull(doc.Glyphs);
            Assert.Equal(2, doc.Glyphs.Count);
            Assert.NotNull(doc.KerningPairs);
            Assert.NotNull(doc.PreMonoWidths);
            Assert.NotNull(doc.ExportSettings);
        }

        [Fact]
        public void GetGlyph_SparseOrMisorderedCollection_ResolvesCorrectGlyph()
        {
            var doc = new FontDocument
            {
                FirstChar = 32,
                LastChar = 126,
                // Sparse glyphs where index does not match code point
                Glyphs =
                [
                    new GlyphState { CodePoint = 66, Width = 8, Height = 8 },
                    new GlyphState { CodePoint = 65, Width = 8, Height = 8 }
                ]
            };

            var glyphA = doc.GetGlyph(65);
            var glyphB = doc.GetGlyph(66);
            var glyphMissing = doc.GetGlyph(67);

            Assert.NotNull(glyphA);
            Assert.Equal(65, glyphA.CodePoint);
            Assert.NotNull(glyphB);
            Assert.Equal(66, glyphB.CodePoint);
            Assert.Null(glyphMissing);

            Assert.Equal(1, doc.GetGlyphIndex(65));
            Assert.Equal(0, doc.GetGlyphIndex(66));
            Assert.Equal(-1, doc.GetGlyphIndex(67));
        }

        [Fact]
        public void GetKerning_NullKerningPairs_ReturnsZero()
        {
            var doc = new FontDocument { KerningPairs = null! };
            Assert.Equal(0, doc.GetKerning(65, 66));
        }

        [Fact]
        public void EstimateAdafruitGfxBytes_NullGlyphsAndNullItems_HandlesSafely()
        {
            var doc1 = new FontDocument { Glyphs = null! };
            Assert.Equal(13, doc1.EstimateAdafruitGfxBytes());

            var doc2 = new FontDocument { Glyphs = [null!, null!] };
            Assert.Equal(13, doc2.EstimateAdafruitGfxBytes());
        }

        [Fact]
        public void FontGlyph_ConstructorsAndFactoryMethods_FunctionCorrectly()
        {
            var g1 = new FontGlyph();
            Assert.Equal(0, g1.CodePoint);

            var g2 = new FontGlyph(65, 8, 12);
            Assert.Equal(65, g2.CodePoint);
            Assert.Equal(8, g2.Width);
            Assert.Equal(12, g2.Height);
            Assert.Equal(96, g2.Pixels.Length);
            Assert.Equal(9, g2.XAdvance);

            var baseGlyph = new GlyphState { CodePoint = 66, Width = 6, Height = 8, Pixels = new bool[48], IsCustomized = true };
            var g3 = FontGlyph.FromGlyphState(baseGlyph);
            Assert.Equal(66, g3.CodePoint);
            Assert.Equal(6, g3.Width);
            Assert.True(g3.IsCustomized);
            Assert.NotSame(baseGlyph.Pixels, g3.Pixels);

            var g4 = FontGlyph.FromGlyphState(null!);
            Assert.NotNull(g4);
            Assert.Equal(0, g4.CodePoint);
        }
    }
}
