using System;
using Hexprite.Core;
using Hexprite.Resources.Fonts;

namespace Hexprite.Services
{
    public enum FlipperUiTemplateType
    {
        HeaderBar,
        DialogBox,
        ListView,
        AppIconGuide,
    }

    /// <summary>
    /// Generates standard, pixel-perfect Flipper Zero user interface layouts and layout guides.
    /// Matches official Flipper GUI components (gui/modules/widget.c, gui/canvas.c).
    /// </summary>
    public static class FlipperUiTemplateService
    {
        /// <summary>
        /// Applies a standard Flipper UI template stamp onto the given sprite canvas.
        /// </summary>
        public static void ApplyTemplate(SpriteState sprite, FlipperUiTemplateType templateType, string title = "Hexprite")
        {
            if (sprite == null) return;
            bool[] pixels = sprite.ActiveLayerPixels;
            int w = sprite.Width;
            int h = sprite.Height;

            switch (templateType)
            {
                case FlipperUiTemplateType.HeaderBar:
                    DrawHeaderBar(pixels, w, h, title);
                    break;
                case FlipperUiTemplateType.DialogBox:
                    DrawDialogBox(pixels, w, h, title, "Operation Complete");
                    break;
                case FlipperUiTemplateType.ListView:
                    DrawListView(pixels, w, h, title);
                    break;
                case FlipperUiTemplateType.AppIconGuide:
                    DrawAppIconGuide(pixels, w, h, title);
                    break;
            }
        }

        public static void DrawHeaderBar(bool[] pixels, int width, int height, string title = "Hexprite")
        {
            if (pixels == null || width < 20 || height < 12) return;

            // Title string
            FlipperFonts.DrawString(pixels, width, height, 2, 2, title, FlipperFontType.FontSecondary);

            // Horizontal header divider line at y=11
            for (int x = 0; x < width; x++)
            {
                SetPixel(pixels, width, height, x, 11, val: true);
            }

            // Battery Icon on right side (e.g. at width - 20)
            int bx = width - 18;
            int by = 2;
            if (bx > 30)
            {
                // Battery body 12x7
                for (int x = 0; x < 12; x++)
                {
                    SetPixel(pixels, width, height, bx + x, by, val: true);
                    SetPixel(pixels, width, height, bx + x, by + 6, val: true);
                }
                for (int y = 0; y < 7; y++)
                {
                    SetPixel(pixels, width, height, bx, by + y, val: true);
                    SetPixel(pixels, width, height, bx + 11, by + y, val: true);
                }
                // Battery terminal pip (+ bump)
                SetPixel(pixels, width, height, bx + 12, by + 2, val: true);
                SetPixel(pixels, width, height, bx + 12, by + 3, val: true);
                SetPixel(pixels, width, height, bx + 12, by + 4, val: true);

                // Charge bars (half full)
                for (int y = by + 2; y <= by + 4; y++)
                {
                    SetPixel(pixels, width, height, bx + 2, y, val: true);
                    SetPixel(pixels, width, height, bx + 3, y, val: true);
                    SetPixel(pixels, width, height, bx + 5, y, val: true);
                    SetPixel(pixels, width, height, bx + 6, y, val: true);
                }
            }
        }

        public static void DrawDialogBox(bool[] pixels, int width, int height, string title = "Alert", string message = "Done!")
        {
            if (pixels == null || width < 40 || height < 30) return;

            int dw = Math.Min(104, width - 8);
            int dh = Math.Min(44, height - 8);
            int dx = (width - dw) / 2;
            int dy = (height - dh) / 2;

            // Clear interior
            for (int r = 0; r < dh; r++)
            {
                for (int c = 0; c < dw; c++)
                {
                    SetPixel(pixels, width, height, dx + c, dy + r, val: false);
                }
            }

            // Draw rounded frame
            for (int c = 2; c < dw - 2; c++)
            {
                SetPixel(pixels, width, height, dx + c, dy, val: true);
                SetPixel(pixels, width, height, dx + c, dy + dh - 1, val: true);
            }
            for (int r = 2; r < dh - 2; r++)
            {
                SetPixel(pixels, width, height, dx, dy + r, val: true);
                SetPixel(pixels, width, height, dx + dw - 1, dy + r, val: true);
            }
            // Corners
            SetPixel(pixels, width, height, dx + 1, dy + 1, val: true);
            SetPixel(pixels, width, height, dx + dw - 2, dy + 1, val: true);
            SetPixel(pixels, width, height, dx + 1, dy + dh - 2, val: true);
            SetPixel(pixels, width, height, dx + dw - 2, dy + dh - 2, val: true);

            // Title
            FlipperFonts.DrawString(pixels, width, height, dx + 8, dy + 4, title, FlipperFontType.FontPrimary);
            // Title underline
            for (int c = 6; c < dw - 6; c++)
            {
                SetPixel(pixels, width, height, dx + c, dy + 16, val: true);
            }

            // Message
            FlipperFonts.DrawString(pixels, width, height, dx + 8, dy + 20, message, FlipperFontType.FontSecondary);

            // [ OK ] Button
            FlipperFonts.DrawString(pixels, width, height, dx + dw - 32, dy + dh - 11, "[ OK ]", FlipperFontType.FontSecondary);
        }

        public static void DrawListView(bool[] pixels, int width, int height, string title = "Menu")
        {
            DrawHeaderBar(pixels, width, height, title);

            // 3 List items
            FlipperFonts.DrawString(pixels, width, height, 8, 16, "1. Option One", FlipperFontType.FontSecondary);
            
            // Selected item 2 with highlight box
            int hlY = 28;
            for (int r = 0; r < 12; r++)
            {
                for (int c = 4; c < width - 10; c++)
                {
                    SetPixel(pixels, width, height, c, hlY + r, val: true);
                }
            }
            // Inverted text for selected item
            // (When drawn, on Flipper the canvas_invert_color or clearing creates crisp inverted text)
            for (int c = 6; c < width - 12; c++)
            {
                SetPixel(pixels, width, height, c, hlY + 2, val: false);
                SetPixel(pixels, width, height, c, hlY + 9, val: false);
            }

            FlipperFonts.DrawString(pixels, width, height, 8, 44, "3. Option Three", FlipperFontType.FontSecondary);

            // Scrollbar on right
            int sbX = width - 4;
            for (int y = 14; y < height - 2; y++)
            {
                SetPixel(pixels, width, height, sbX, y, val: true);
            }
            // Scrollbar thumb (at item 2)
            for (int y = 24; y < 40; y++)
            {
                SetPixel(pixels, width, height, sbX - 1, y, val: true);
                SetPixel(pixels, width, height, sbX, y, val: true);
                SetPixel(pixels, width, height, sbX + 1, y, val: true);
            }
        }

        public static void DrawAppIconGuide(bool[] pixels, int width, int height, string iconName = "App")
        {
            // Center 10x10 bounding guide box
            int cx = (width - 10) / 2;
            int cy = (height - 10) / 2;

            for (int x = cx - 1; x <= cx + 10; x++)
            {
                SetPixel(pixels, width, height, x, cy - 1, val: true);
                SetPixel(pixels, width, height, x, cy + 10, val: true);
            }
            for (int y = cy - 1; y <= cy + 10; y++)
            {
                SetPixel(pixels, width, height, cx - 1, y, val: true);
                SetPixel(pixels, width, height, cx + 10, y, val: true);
            }

            // Name label below
            FlipperFonts.DrawString(pixels, width, height, cx - 6, cy + 14, iconName, FlipperFontType.FontSecondary);
        }

        private static void SetPixel(bool[] pixels, int width, int height, int x, int y, bool val)
        {
            if (x >= 0 && x < width && y >= 0 && y < height)
            {
                pixels[y * width + x] = val;
            }
        }
    }
}
