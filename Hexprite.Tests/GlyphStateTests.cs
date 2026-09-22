using Hexprite.Core;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class GlyphStateTests
    {
        [Fact]
        public void Clone_CreatesDeepCopy()
        {
            var original = new GlyphState
            {
                CodePoint = 65,
                Width = 2,
                Height = 2,
                Pixels = new[] { true, false, false, true },
                XOffset = 1,
                YOffset = -2,
                XAdvance = 3,
                IsCustomized = true
            };

            var clone = original.Clone();

            Assert.NotSame(original, clone);
            Assert.NotSame(original.Pixels, clone.Pixels);
            
            Assert.Equal(original.CodePoint, clone.CodePoint);
            Assert.Equal(original.Width, clone.Width);
            Assert.Equal(original.Height, clone.Height);
            Assert.Equal(original.Pixels, clone.Pixels);
            Assert.Equal(original.XOffset, clone.XOffset);
            Assert.Equal(original.YOffset, clone.YOffset);
            Assert.Equal(original.XAdvance, clone.XAdvance);
            Assert.Equal(original.IsCustomized, clone.IsCustomized);
            
            // Verify deep copy of pixel array
            clone.Pixels[0] = false;
            Assert.True(original.Pixels[0]);
        }

        [Fact]
        public void GetTightBounds_EmptyGlyph_ReturnsZeros()
        {
            var glyph = new GlyphState
            {
                Width = 3,
                Height = 3,
                Pixels = new bool[9]
            };

            var (x, y, w, h) = glyph.GetTightBounds();

            Assert.Equal(0, x);
            Assert.Equal(0, y);
            Assert.Equal(0, w);
            Assert.Equal(0, h);
        }

        [Fact]
        public void GetTightBounds_FullyFilledGlyph_ReturnsFullBounds()
        {
            var glyph = new GlyphState
            {
                Width = 3,
                Height = 3,
                Pixels = new[]
                {
                    true, true, true,
                    true, true, true,
                    true, true, true
                }
            };

            var (x, y, w, h) = glyph.GetTightBounds();

            Assert.Equal(0, x);
            Assert.Equal(0, y);
            Assert.Equal(3, w);
            Assert.Equal(3, h);
        }

        [Fact]
        public void GetTightBounds_PartiallyFilledGlyph_ReturnsCorrectBounds()
        {
            // . . . .
            // . X X .
            // . X X .
            // . . . .
            var glyph = new GlyphState
            {
                Width = 4,
                Height = 4,
                Pixels = new[]
                {
                    false, false, false, false,
                    false, true,  true,  false,
                    false, true,  true,  false,
                    false, false, false, false
                }
            };

            var (x, y, w, h) = glyph.GetTightBounds();

            Assert.Equal(1, x);
            Assert.Equal(1, y);
            Assert.Equal(2, w);
            Assert.Equal(2, h);
        }
        
        [Fact]
        public void GetTightBounds_SinglePixel_ReturnsOneByOne()
        {
            // . . .
            // . . X
            // . . .
            var glyph = new GlyphState
            {
                Width = 3,
                Height = 3,
                Pixels = new[]
                {
                    false, false, false,
                    false, false, true,
                    false, false, false
                }
            };

            var (x, y, w, h) = glyph.GetTightBounds();

            Assert.Equal(2, x);
            Assert.Equal(1, y);
            Assert.Equal(1, w);
            Assert.Equal(1, h);
        }

        [Fact]
        public void GetCroppedPixels_EmptyGlyph_ReturnsEmptyArray()
        {
            var glyph = new GlyphState
            {
                Width = 2,
                Height = 2,
                Pixels = new bool[4]
            };

            var cropped = glyph.GetCroppedPixels();

            Assert.Empty(cropped);
        }

        [Fact]
        public void GetCroppedPixels_PartiallyFilled_ReturnsOnlyFilledArea()
        {
            // . . . .
            // . X X .
            // . . X .
            // . . . .
            var glyph = new GlyphState
            {
                Width = 4,
                Height = 4,
                Pixels = new[]
                {
                    false, false, false, false,
                    false, true,  true,  false,
                    false, false, true,  false,
                    false, false, false, false
                }
            };

            var cropped = glyph.GetCroppedPixels();

            Assert.Equal(4, cropped.Length);
            // X X
            // . X
            Assert.True(cropped[0]);
            Assert.True(cropped[1]);
            Assert.False(cropped[2]);
            Assert.True(cropped[3]);
        }
    }
}
