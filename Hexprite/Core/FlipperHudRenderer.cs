using System;
using Hexprite.Resources.Fonts;

namespace Hexprite.Core
{
    /// <summary>
    /// Renders the authentic Flipper Zero Desktop Status Bar / HUD overlay onto a 1-bit monochrome canvas.
    /// Includes Level badge, centered Mood label, Bluetooth status rune, and battery indicator.
    /// </summary>
    public static class FlipperHudRenderer
    {
        public const int HudHeight = 12;

        public static void Draw(bool[] canvas, int width, int height, int level, int mood)
        {
            if (canvas == null || width < 128 || height < HudHeight) return;

            // 1. Clear status bar background (y = 0..10) to white/knockout
            for (int y = 0; y < 11; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    canvas[y * width + x] = false;
                }
            }

            // 2. 1px Separator line along the bottom of the status bar at y = 11
            for (int x = 0; x < width; x++)
            {
                canvas[11 * width + x] = true;
            }

            // 3. Left Section: Level indicator (Lv.X)
            string levelText = $"Lv.{Math.Clamp(level, 1, 30)}";
            FlipperFonts.DrawString(canvas, width, height, 2, 2, levelText, FlipperFontType.FontSecondary);

            // 4. Center Section: Mood indicator (dynamically centered)
            string moodName = mood switch
            {
                0 => "ECSTATIC",
                <= 3 => "HAPPY",
                <= 7 => "IDLE",
                <= 11 => "ANNOYED",
                _ => "ANGRY"
            };
            var (moodW, _) = FlipperFonts.MeasureString(moodName, FlipperFontType.FontSecondary);
            int centerX = Math.Max(26, (width - moodW) / 2);
            FlipperFonts.DrawString(canvas, width, height, centerX, 2, moodName, FlipperFontType.FontSecondary);

            // 5. Right Section: BLE (Bluetooth) Status Rune at x = 104, y = 2..8
            DrawBluetoothIcon(canvas, width, height, 104, 2);

            // 6. Right Section: Battery Gauge at x = 112..124, y = 2..9 + terminal at x = 125
            DrawBatteryIcon(canvas, width, height, 112, 2);
        }

        private static void DrawBluetoothIcon(bool[] canvas, int width, int height, int x, int y)
        {
            // Vertical center spine
            for (int r = 0; r <= 6; r++)
            {
                SetPixel(canvas, width, height, x + 2, y + r, true);
            }

            // Top loop
            SetPixel(canvas, width, height, x + 3, y + 1, true);
            SetPixel(canvas, width, height, x + 4, y + 2, true);
            SetPixel(canvas, width, height, x + 3, y + 3, true);

            // Bottom loop
            SetPixel(canvas, width, height, x + 3, y + 4, true);
            SetPixel(canvas, width, height, x + 4, y + 5, true);
            SetPixel(canvas, width, height, x + 3, y + 6, true);

            // Diagonal cross tails
            SetPixel(canvas, width, height, x + 1, y + 2, true);
            SetPixel(canvas, width, height, x, y + 1, true);
            SetPixel(canvas, width, height, x + 1, y + 5, true);
            SetPixel(canvas, width, height, x, y + 6, true);
        }

        private static void DrawBatteryIcon(bool[] canvas, int width, int height, int x, int y)
        {
            // Outer frame (13px wide x 8px high)
            for (int c = 0; c < 13; c++)
            {
                SetPixel(canvas, width, height, x + c, y, true);
                SetPixel(canvas, width, height, x + c, y + 7, true);
            }
            for (int r = 0; r < 8; r++)
            {
                SetPixel(canvas, width, height, x, y + r, true);
                SetPixel(canvas, width, height, x + 12, y + r, true);
            }

            // Positive terminal nipple on right edge
            for (int r = 2; r <= 5; r++)
            {
                SetPixel(canvas, width, height, x + 13, y + r, true);
            }

            // Internal 3-bar charge level indicators
            for (int cx = x + 2; cx <= x + 10; cx += 3)
            {
                for (int cy = y + 2; cy <= y + 5; cy++)
                {
                    SetPixel(canvas, width, height, cx, cy, true);
                    SetPixel(canvas, width, height, cx + 1, cy, true);
                }
            }
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