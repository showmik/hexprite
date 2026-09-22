using System;
using Hexprite.Resources.Fonts;

namespace Hexprite.Core
{
    public enum SpeechBubbleTailPosition
    {
        BottomLeft,
        BottomRight,
        TopLeft,
        TopRight,
        None
    }

    /// <summary>
    /// Represents a speech bubble slot in a Flipper Zero dolphin animation.
    /// Matches the bubble slot format defined in official Flipper firmware meta.txt files.
    /// </summary>
    public class FlipperSpeechBubble
    {
        public int SlotIndex { get; set; }
        public int StartFrame { get; set; }
        public int EndFrame { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public string Text { get; set; } = string.Empty;
        public SpeechBubbleTailPosition Tail { get; set; } = SpeechBubbleTailPosition.BottomLeft;
        public string AlignH { get; set; } = "Center";
        public string AlignV { get; set; } = "Bottom";

        public FlipperSpeechBubble() { }

        public FlipperSpeechBubble(int slotIndex, int x, int y, string text, SpeechBubbleTailPosition tail = SpeechBubbleTailPosition.BottomLeft)
        {
            SlotIndex = slotIndex;
            X = x;
            Y = y;
            Text = text;
            Tail = tail;
        }

        public FlipperSpeechBubble Clone() => new()
        {
            SlotIndex = SlotIndex,
            StartFrame = StartFrame,
            EndFrame = EndFrame,
            X = X,
            Y = Y,
            Text = Text,
            Tail = Tail,
            AlignH = AlignH,
            AlignV = AlignV,
        };

        /// <summary>
        /// Measures the width and height of this speech bubble including padding and borders.
        /// </summary>
        public (int Width, int Height) MeasureBubble()
        {
            if (string.IsNullOrEmpty(Text)) return (16, 12);

            string normalized = Text.Replace(@"\r\n", "\n").Replace(@"\n", "\n").Replace("\r", "");
            var (textW, textH) = FlipperFonts.MeasureString(normalized, FlipperFontType.FontSecondary);
            int bubbleW = textW + 6; // 3px padding on left and right
            int bubbleH = textH + 4; // 2px padding on top and bottom
            if (bubbleW < 18) bubbleW = 18;
            if (bubbleH < 12) bubbleH = 12;
            return (bubbleW, bubbleH);
        }

        /// <summary>
        /// Renders this speech bubble onto a 1-bit monochrome canvas buffer.
        /// Draws rounded border, white background knockout, black text, and tail.
        /// </summary>
        public void Draw(bool[] canvas, int canvasWidth, int canvasHeight, bool fillInterior = true)
        {
            if (canvas == null || string.IsNullOrEmpty(Text)) return;

            var (w, h) = MeasureBubble();
            int minY = (Tail is SpeechBubbleTailPosition.TopLeft or SpeechBubbleTailPosition.TopRight) ? 2 : 0;
            int maxY = (Tail is SpeechBubbleTailPosition.BottomLeft or SpeechBubbleTailPosition.BottomRight)
                ? Math.Max(minY, canvasHeight - h - 2)
                : Math.Max(minY, canvasHeight - h);

            int bx = Math.Clamp(X, 0, Math.Max(0, canvasWidth - w));
            int by = Math.Clamp(Y, minY, maxY);

            // 1. Fill bubble interior with white/knockout (clear pixels)
            if (fillInterior)
            {
                for (int r = 0; r < h; r++)
                {
                    for (int c = 0; c < w; c++)
                    {
                        int px = bx + c;
                        int py = by + r;
                        if (px >= 0 && px < canvasWidth && py >= 0 && py < canvasHeight)
                        {
                            canvas[py * canvasWidth + px] = false;
                        }
                    }
                }
            }

            // 2. Draw 1px border with rounded corners
            // Top and bottom horizontal lines (skip corner pixels)
            for (int c = 1; c < w - 1; c++)
            {
                SetPixel(canvas, canvasWidth, canvasHeight, bx + c, by, true);
                SetPixel(canvas, canvasWidth, canvasHeight, bx + c, by + h - 1, true);
            }

            // Left and right vertical lines (skip corner pixels)
            for (int r = 1; r < h - 1; r++)
            {
                SetPixel(canvas, canvasWidth, canvasHeight, bx, by + r, true);
                SetPixel(canvas, canvasWidth, canvasHeight, bx + w - 1, by + r, true);
            }

            // 3. Draw tail, open border connecting pixels, and clear tail cavity
            switch (Tail)
            {
                case SpeechBubbleTailPosition.BottomLeft:
                {
                    int tx = bx + 3;
                    int ty = by + h - 1;
                    // Open border connection
                    SetPixel(canvas, canvasWidth, canvasHeight, tx, ty, false);
                    SetPixel(canvas, canvasWidth, canvasHeight, tx + 1, ty, false);

                    // Clear tail cavity
                    if (fillInterior)
                    {
                        SetPixel(canvas, canvasWidth, canvasHeight, tx, ty + 1, false);
                        SetPixel(canvas, canvasWidth, canvasHeight, tx - 1, ty + 2, false);
                    }

                    // Draw tail outline downward
                    SetPixel(canvas, canvasWidth, canvasHeight, tx - 1, ty + 1, true);
                    SetPixel(canvas, canvasWidth, canvasHeight, tx + 1, ty + 1, true);
                    SetPixel(canvas, canvasWidth, canvasHeight, tx - 2, ty + 2, true);
                    SetPixel(canvas, canvasWidth, canvasHeight, tx, ty + 2, true);
                    break;
                }
                case SpeechBubbleTailPosition.BottomRight:
                {
                    int tx = bx + w - 5;
                    int ty = by + h - 1;
                    // Open border connection
                    SetPixel(canvas, canvasWidth, canvasHeight, tx, ty, false);
                    SetPixel(canvas, canvasWidth, canvasHeight, tx + 1, ty, false);

                    // Clear tail cavity
                    if (fillInterior)
                    {
                        SetPixel(canvas, canvasWidth, canvasHeight, tx + 1, ty + 1, false);
                        SetPixel(canvas, canvasWidth, canvasHeight, tx + 2, ty + 2, false);
                    }

                    // Draw tail outline downward
                    SetPixel(canvas, canvasWidth, canvasHeight, tx, ty + 1, true);
                    SetPixel(canvas, canvasWidth, canvasHeight, tx + 2, ty + 1, true);
                    SetPixel(canvas, canvasWidth, canvasHeight, tx + 1, ty + 2, true);
                    SetPixel(canvas, canvasWidth, canvasHeight, tx + 3, ty + 2, true);
                    break;
                }
                case SpeechBubbleTailPosition.TopLeft:
                {
                    int tx = bx + 3;
                    int ty = by;
                    // Open border connection
                    SetPixel(canvas, canvasWidth, canvasHeight, tx, ty, false);
                    SetPixel(canvas, canvasWidth, canvasHeight, tx + 1, ty, false);

                    // Clear tail cavity
                    if (fillInterior)
                    {
                        SetPixel(canvas, canvasWidth, canvasHeight, tx, ty - 1, false);
                        SetPixel(canvas, canvasWidth, canvasHeight, tx - 1, ty - 2, false);
                    }

                    // Draw tail outline upward
                    SetPixel(canvas, canvasWidth, canvasHeight, tx - 1, ty - 1, true);
                    SetPixel(canvas, canvasWidth, canvasHeight, tx + 1, ty - 1, true);
                    SetPixel(canvas, canvasWidth, canvasHeight, tx - 2, ty - 2, true);
                    SetPixel(canvas, canvasWidth, canvasHeight, tx, ty - 2, true);
                    break;
                }
                case SpeechBubbleTailPosition.TopRight:
                {
                    int tx = bx + w - 5;
                    int ty = by;
                    // Open border connection
                    SetPixel(canvas, canvasWidth, canvasHeight, tx, ty, false);
                    SetPixel(canvas, canvasWidth, canvasHeight, tx + 1, ty, false);

                    // Clear tail cavity
                    if (fillInterior)
                    {
                        SetPixel(canvas, canvasWidth, canvasHeight, tx + 1, ty - 1, false);
                        SetPixel(canvas, canvasWidth, canvasHeight, tx + 2, ty - 2, false);
                    }

                    // Draw tail outline upward
                    SetPixel(canvas, canvasWidth, canvasHeight, tx, ty - 1, true);
                    SetPixel(canvas, canvasWidth, canvasHeight, tx + 2, ty - 1, true);
                    SetPixel(canvas, canvasWidth, canvasHeight, tx + 1, ty - 2, true);
                    SetPixel(canvas, canvasWidth, canvasHeight, tx + 3, ty - 2, true);
                    break;
                }
                case SpeechBubbleTailPosition.None:
                default:
                    break;
            }

            // 4. Render text inside
            FlipperFonts.DrawString(canvas, canvasWidth, canvasHeight, bx + 3, by + 2, Text, FlipperFontType.FontSecondary);
        }

        private static void SetPixel(bool[] canvas, int width, int height, int x, int y, bool value)
        {
            if (x >= 0 && x < width && y >= 0 && y < height)
            {
                canvas[y * width + x] = value;
            }
        }
    }
}
