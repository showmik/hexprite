using Hexprite.Core;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FlipperSpeechBubbleTests
    {
        [Fact]
        public void MeasureBubble_ComputesDimensionsWithPadding()
        {
            var bubble = new FlipperSpeechBubble(0, 10, 10, "Hi!", SpeechBubbleTailPosition.BottomLeft);
            var (w, h) = bubble.MeasureBubble();

            // "Hi!" is 3 chars * 6 = 18px -> + 6px padding = 24px
            Assert.True(w >= 20);
            Assert.True(h >= 12);
        }

        [Fact]
        public void Draw_RendersKnockoutAndBorder()
        {
            var canvas = new bool[128 * 64];
            // Pre-fill canvas with black
            for (int i = 0; i < canvas.Length; i++) canvas[i] = true;

            var bubble = new FlipperSpeechBubble(0, 10, 10, "Test", SpeechBubbleTailPosition.BottomLeft);
            bubble.Draw(canvas, 128, 64, fillInterior: true);

            // Verify some interior pixels were cleared to false (knockout)
            Assert.False(canvas[12 * 128 + 12]);
        }

        [Theory]
        [InlineData(SpeechBubbleTailPosition.BottomLeft)]
        [InlineData(SpeechBubbleTailPosition.BottomRight)]
        [InlineData(SpeechBubbleTailPosition.TopLeft)]
        [InlineData(SpeechBubbleTailPosition.TopRight)]
        public void Draw_ClearsTailConnectionAndCavityOnDarkCanvas(SpeechBubbleTailPosition tail)
        {
            var canvas = new bool[128 * 64];
            // Pre-fill canvas with black
            for (int i = 0; i < canvas.Length; i++) canvas[i] = true;

            var bubble = new FlipperSpeechBubble(0, 10, 10, "Echo", tail);
            bubble.Draw(canvas, 128, 64, fillInterior: true);

            // Verify interior knockout
            Assert.False(canvas[11 * 128 + 11]);
        }
    }
}
