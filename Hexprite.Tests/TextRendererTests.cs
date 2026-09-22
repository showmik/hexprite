using System.Windows.Media;
using Hexprite.Core;
using Hexprite.Rendering;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class TextRendererTests
    {
        private readonly FontFamily _testFont = new("Arial");

        [Fact]
        public void RenderTextToMonochromeMask_NullOrEmpty_ReturnsEmptyMask()
        {
            var maskNull = TextRenderer.RenderTextToMonochromeMask(null!, _testFont, 12, false, false);
            var maskEmpty = TextRenderer.RenderTextToMonochromeMask("", _testFont, 12, false, false);

            Assert.Equal(0, maskNull.GetLength(0));
            Assert.Equal(0, maskNull.GetLength(1));
            Assert.Equal(0, maskEmpty.GetLength(0));
            Assert.Equal(0, maskEmpty.GetLength(1));
        }

        [Fact]
        public void RenderTextToMonochromeMask_SingleLine_ProducesNonEmptyMask()
        {
            var mask = TextRenderer.RenderTextToMonochromeMask("Test", _testFont, 16, false, false);

            Assert.True(mask.GetLength(0) > 0);
            Assert.True(mask.GetLength(1) > 0);
        }

        [Fact]
        public void RenderTextToMonochromeMask_CrlfNormalized_MatchesLfOnly()
        {
            var maskCrlf = TextRenderer.RenderTextToMonochromeMask("Line1\r\nLine2", _testFont, 16, false, false);
            var maskLf = TextRenderer.RenderTextToMonochromeMask("Line1\nLine2", _testFont, 16, false, false);

            Assert.Equal(maskLf.GetLength(0), maskCrlf.GetLength(0));
            Assert.Equal(maskLf.GetLength(1), maskCrlf.GetLength(1));
        }

        [Fact]
        public void RenderTextToMonochromeMask_EmptyLine_PreservesLineHeight()
        {
            var maskSingle = TextRenderer.RenderTextToMonochromeMask("Line1", _testFont, 16, false, false);
            var maskDouble = TextRenderer.RenderTextToMonochromeMask("Line1\nLine2", _testFont, 16, false, false, lineHeight: 0);
            var maskWithEmptyLine = TextRenderer.RenderTextToMonochromeMask("Line1\n\nLine2", _testFont, 16, false, false, lineHeight: 0);

            // Empty line between Line1 and Line2 should make total height strictly greater than without empty line
            Assert.True(maskWithEmptyLine.GetLength(1) > maskDouble.GetLength(1));
        }

        [Fact]
        public void RenderTextToMonochromeMask_UnicodeCharacters_RendersWithoutCrashing()
        {
            var mask = TextRenderer.RenderTextToMonochromeMask("100°C ★ Café", _testFont, 16, false, false);

            Assert.True(mask.GetLength(0) > 0);
            Assert.True(mask.GetLength(1) > 0);
        }

        [Fact]
        public void RenderTextToMonochromeMask_Alignments_ProduceEqualBoundingBox()
        {
            var maskLeft = TextRenderer.RenderTextToMonochromeMask("Short\nLongerLine", _testFont, 16, false, false, alignment: TextToolAlignment.Left);
            var maskCenter = TextRenderer.RenderTextToMonochromeMask("Short\nLongerLine", _testFont, 16, false, false, alignment: TextToolAlignment.Center);
            var maskRight = TextRenderer.RenderTextToMonochromeMask("Short\nLongerLine", _testFont, 16, false, false, alignment: TextToolAlignment.Right);

            Assert.Equal(maskLeft.GetLength(0), maskCenter.GetLength(0));
            Assert.Equal(maskLeft.GetLength(1), maskCenter.GetLength(1));
            Assert.Equal(maskLeft.GetLength(0), maskRight.GetLength(0));
            Assert.Equal(maskLeft.GetLength(1), maskRight.GetLength(1));
        }

        [Fact]
        public void GetCaretPosition_EmptyText_ReturnsClickOrigin()
        {
            var (cx, cy, ch) = TextRenderer.GetCaretPosition(
                "", _testFont, 16, false, false, 1, 1, 1, TextToolAlignment.Left, 10, 20);

            Assert.Equal(10, cx);
            Assert.Equal(20, cy);
            Assert.True(ch > 0);
        }

        [Fact]
        public void GetCaretPosition_SingleLineLeft_AdvancesX()
        {
            var (cx, cy, ch) = TextRenderer.GetCaretPosition(
                "ABC", _testFont, 16, false, false, 1, 1, 1, TextToolAlignment.Left, 10, 20);

            Assert.True(cx > 10);
            Assert.Equal(20, cy);
            Assert.True(ch > 0);
        }

        [Fact]
        public void GetCaretPosition_SingleLineRight_AnchorsXToOrigin()
        {
            var (cx, cy, ch) = TextRenderer.GetCaretPosition(
                "ABC", _testFont, 16, false, false, 1, 1, 1, TextToolAlignment.Right, 50, 20);

            // In right-aligned text, the end of the text is always at the anchor X
            Assert.Equal(50, cx);
            Assert.Equal(20, cy);
        }

        [Fact]
        public void GetCaretPosition_MultiLine_AdvancesYToLastLine()
        {
            var (cx1, cy1, ch1) = TextRenderer.GetCaretPosition(
                "Line 1", _testFont, 16, false, false, 1, 1, 2, TextToolAlignment.Left, 10, 20);

            var (cx2, cy2, ch2) = TextRenderer.GetCaretPosition(
                "Line 1\nLine 2", _testFont, 16, false, false, 1, 1, 2, TextToolAlignment.Left, 10, 20);

            Assert.True(cy2 > cy1);
        }

        [Fact]
        public void GetCaretPosition_WithExplicitCaretIndex_ComputesCorrectOffset()
        {
            var (cxStart, cyStart, _) = TextRenderer.GetCaretPosition(
                "ABCDEF", 0, _testFont, 16, false, false, 1, 1, 1, TextToolAlignment.Left, 10, 20);

            var (cxMid, cyMid, _) = TextRenderer.GetCaretPosition(
                "ABCDEF", 3, _testFont, 16, false, false, 1, 1, 1, TextToolAlignment.Left, 10, 20);

            var (cxEnd, cyEnd, _) = TextRenderer.GetCaretPosition(
                "ABCDEF", 6, _testFont, 16, false, false, 1, 1, 1, TextToolAlignment.Left, 10, 20);

            Assert.Equal(10, cxStart);
            Assert.Equal(20, cyStart);
            Assert.True(cxMid > cxStart);
            Assert.True(cxEnd > cxMid);
            Assert.Equal(cyStart, cyMid);
            Assert.Equal(cyStart, cyEnd);
        }

        [Fact]
        public void GetCaretPosition_Multiline_MiddleOfLine_ComputesLineAndColumnOffset()
        {
            // "Line1\nLine2" (Length = 11, line1 has 5 chars, newline at 5, line2 starts at 6)
            var (cxLine1Start, cyLine1Start, _) = TextRenderer.GetCaretPosition(
                "Line1\nLine2", 0, _testFont, 16, false, false, 1, 1, 2, TextToolAlignment.Left, 10, 20);

            var (cxLine2Start, cyLine2Start, _) = TextRenderer.GetCaretPosition(
                "Line1\nLine2", 6, _testFont, 16, false, false, 1, 1, 2, TextToolAlignment.Left, 10, 20);

            var (cxLine2Mid, cyLine2Mid, _) = TextRenderer.GetCaretPosition(
                "Line1\nLine2", 9, _testFont, 16, false, false, 1, 1, 2, TextToolAlignment.Left, 10, 20);

            Assert.Equal(10, cxLine1Start);
            Assert.Equal(20, cyLine1Start);

            Assert.Equal(10, cxLine2Start);
            Assert.True(cyLine2Start > cyLine1Start);

            Assert.True(cxLine2Mid > cxLine2Start);
            Assert.Equal(cyLine2Start, cyLine2Mid);
        }

        [Fact]
        public void GetCaretPosition_RightAlignment_IndexZero_StartsAtLeftEdge()
        {
            var (cxStart, _, _) = TextRenderer.GetCaretPosition(
                "Hello", 0, _testFont, 16, false, false, 1, 1, 1, TextToolAlignment.Right, 100, 20);

            var (cxEnd, _, _) = TextRenderer.GetCaretPosition(
                "Hello", 5, _testFont, 16, false, false, 1, 1, 1, TextToolAlignment.Right, 100, 20);

            // In right-aligned text at textToolX = 100, index 5 is at 100, index 0 is at (100 - width)
            Assert.Equal(100, cxEnd);
            Assert.True(cxStart < cxEnd);
        }

        [Fact]
        public void ClearCache_CanBeCalledRepeatedlyWithoutErrors()
        {
            TextRenderer.ClearCache();
            var mask1 = TextRenderer.RenderTextToMonochromeMask("Hello", _testFont, 16, false, false);
            TextRenderer.ClearCache();
            var mask2 = TextRenderer.RenderTextToMonochromeMask("Hello", _testFont, 16, false, false);

            Assert.Equal(mask1.GetLength(0), mask2.GetLength(0));
            Assert.Equal(mask1.GetLength(1), mask2.GetLength(1));
        }
    }
}
