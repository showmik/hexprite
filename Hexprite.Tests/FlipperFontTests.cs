using Hexprite.Resources.Fonts;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FlipperFontTests
    {
        [Fact]
        public void CreateSecondaryFontDocument_PopulatesStandardGlyphs()
        {
            var doc = FlipperFonts.CreateSecondaryFontDocument();

            Assert.Equal("FontSecondary", doc.FontName);
            Assert.Equal(5, doc.MaxCellWidth);
            Assert.Equal(7, doc.CellHeight);
            Assert.True(doc.Glyphs.Count >= 95);

            var glyphA = doc.Glyphs.Find(g => g.CodePoint == 'A');
            Assert.NotNull(glyphA);
            Assert.True(glyphA.IsCustomized);
            Assert.NotEmpty(glyphA.Pixels);
        }

        [Fact]
        public void CreatePrimaryFontDocument_PopulatesBoldGlyphs()
        {
            var doc = FlipperFonts.CreatePrimaryFontDocument();

            Assert.Equal("FontPrimary", doc.FontName);
            Assert.Equal(7, doc.MaxCellWidth);
            Assert.Equal(10, doc.CellHeight);
            Assert.True(doc.Glyphs.Count >= 95);
        }

        [Fact]
        public void CreateBigNumbersFontDocument_PopulatesDigitGlyphs()
        {
            var doc = FlipperFonts.CreateBigNumbersFontDocument();

            Assert.Equal("FontBigNumbers", doc.FontName);
            Assert.Equal(11, doc.MaxCellWidth);
            Assert.Equal(15, doc.CellHeight);
        }

        [Fact]
        public void MeasureString_ReturnsExpectedDimensions()
        {
            var (w, h) = FlipperFonts.MeasureString("Flipper", FlipperFontType.FontSecondary);

            Assert.Equal(42, w); // 7 chars * 6px
            Assert.Equal(7, h);  // 7px single line

            var (mw, mh) = FlipperFonts.MeasureString("Line1\nLine2", FlipperFontType.FontSecondary);
            Assert.Equal(30, mw); // 5 chars * 6px
            Assert.Equal(15, mh); // 7px + 1px + 7px
        }

        [Fact]
        public void DrawString_DrawsPixelsOnBuffer()
        {
            var pixels = new bool[128 * 64];
            FlipperFonts.DrawString(pixels, 128, 64, 10, 10, "Hello", FlipperFontType.FontSecondary);

            bool hasPixels = false;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i]) { hasPixels = true; break; }
            }

            Assert.True(hasPixels);
        }

        [Fact]
        public void DrawString_PrimaryFont_DrawsBoldPixels()
        {
            var pixels = new bool[128 * 64];
            FlipperFonts.DrawString(pixels, 128, 64, 5, 5, "FLIPPER", FlipperFontType.FontPrimary);

            int pixelCount = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i]) pixelCount++;
            }

            Assert.True(pixelCount > 50);
        }

        [Fact]
        public void DrawString_BigNumbersFont_DrawsLargeDigits()
        {
            var pixels = new bool[128 * 64];
            FlipperFonts.DrawString(pixels, 128, 64, 0, 0, "12:34", FlipperFontType.FontBigNumbers);

            int pixelCount = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i]) pixelCount++;
            }

            Assert.True(pixelCount > 100);
        }

        [Fact]
        public void MeasureString_PrimaryFont_ReturnsProportionalDimensions()
        {
            var (w, h) = FlipperFonts.MeasureString("Flipper", FlipperFontType.FontPrimary);
            Assert.Equal(56, w); // 7 chars * 8px
            Assert.Equal(10, h);
        }

        [Fact]
        public void DrawCenteredString_CentersTextWithinBounds()
        {
            var pixels = new bool[128 * 64];
            // "FLIPPER" is 56px wide. (128 - 56) / 2 = 36.
            FlipperFonts.DrawCenteredString(pixels, 128, 64, 10, "FLIPPER", FlipperFontType.FontPrimary);

            // Verify no pixels drawn before X = 36
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 36; x++)
                {
                    Assert.False(pixels[y * 128 + x], $"Pixel at ({x},{y}) should not be drawn before centered start.");
                }
            }

            // Verify pixels are drawn in the expected centered band [36..92]
            bool hasDrawnPixels = false;
            for (int y = 10; y < 21; y++)
            {
                for (int x = 36; x < 92; x++)
                {
                    if (pixels[y * 128 + x])
                    {
                        hasDrawnPixels = true;
                        break;
                    }
                }
            }
            Assert.True(hasDrawnPixels);
        }

        [Fact]
        public void DrawCenteredString_MultiLine_CentersEachLineIndividually()
        {
            var pixels = new bool[128 * 64];
            // Line 1: "Hi" (2 chars * 6 = 12px -> centered at X = (128-12)/2 = 58)
            // Line 2: "Flipper Zero" (11 non-space * 6 + 1 space * 3 = 69px -> centered at X = (128-69)/2 = 29)
            FlipperFonts.DrawCenteredString(pixels, 128, 64, 10, "Hi\nFlipper Zero", FlipperFontType.FontSecondary);

            // Line 1 (Y in 10..17): should not have pixels before X = 58
            for (int y = 10; y < 17; y++)
            {
                for (int x = 0; x < 58; x++)
                {
                    Assert.False(pixels[y * 128 + x], $"Line 1 should not have pixel before X=58 at ({x},{y}).");
                }
            }

            // Line 2 (Y in 19..26): should have pixels starting around X = 29, but not before X = 29
            for (int y = 19; y < 26; y++)
            {
                for (int x = 0; x < 29; x++)
                {
                    Assert.False(pixels[y * 128 + x], $"Line 2 should not have pixel before X=29 at ({x},{y}).");
                }
            }
        }

        [Fact]
        public void DrawCenteredString_AutoVerticalCentering_CentersVertically()
        {
            var pixels = new bool[128 * 64];
            // startY = -1 activates auto-vertical centering
            FlipperFonts.DrawCenteredString(pixels, 128, 64, -1, "Test Message", FlipperFontType.FontSecondary);

            // 1 line of FontSecondary is 7px height -> centered vertically at (64 - 7) / 2 = 28
            // No pixels before Y = 28 or after Y = 35
            for (int y = 0; y < 28; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    Assert.False(pixels[y * 128 + x]);
                }
            }
            for (int y = 36; y < 64; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    Assert.False(pixels[y * 128 + x]);
                }
            }
        }
    }
}
