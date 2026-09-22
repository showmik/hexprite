using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace Hexprite.Services
{
    public record ThemePaletteInfo(
        int Index,
        string Name,
        uint BgColor,
        uint FgColor,
        uint GridDotColor,
        string Description)
    {
        public SolidColorBrush BackgroundBrush => new(Color.FromArgb(
            (byte)((BgColor >> 24) & 0xFF),
            (byte)((BgColor >> 16) & 0xFF),
            (byte)((BgColor >> 8) & 0xFF),
            (byte)(BgColor & 0xFF)));

        public SolidColorBrush ForegroundBrush => new(Color.FromArgb(
            (byte)((FgColor >> 24) & 0xFF),
            (byte)((FgColor >> 16) & 0xFF),
            (byte)((FgColor >> 8) & 0xFF),
            (byte)(FgColor & 0xFF)));
    }

    public static class FlipperThemeService
    {
        public record ThemePalette(uint BgColor, uint FgColor, uint GridDotColor);

        public static readonly IReadOnlyList<ThemePaletteInfo> Palettes =
        [
            new(0, "Classic Amber (OEM)",      0xFFFF8200, 0xFF000000, 0xFFE07300, "Factory Flipper Zero orange LCD backlight"),
            new(1, "Dark OLED / Stealth",      0xFF000000, 0xFFFFFFFF, 0xFF181818, "Inverted black screen with crisp white pixels"),
            new(2, "Game Boy Green",           0xFF9BBC0F, 0xFF0F380F, 0xFF8BAC0F, "Retro handheld monochromatic olive matrix"),
            new(3, "Cyberpunk Neon",           0xFF051105, 0xFF39FF14, 0xFF0D220D, "Vibrant high-contrast hacker phosphor green"),
            new(4, "Momentum Orange",          0xFFFF5C00, 0xFF1A0A00, 0xFFE04800, "Vivid high-saturation custom firmware orange"),
            new(5, "Unleashed Cyan",           0xFF00E5FF, 0xFF002233, 0xFF00B8CC, "Electric cold ice cyan LCD simulation"),
            new(6, "Rogue Crimson",            0xFFFF1744, 0xFF220008, 0xFFCC002B, "Deep menacing red tactical display mode"),
            new(7, "Paper White",              0xFFFFFFFF, 0xFF000000, 0xFFDDDDDD, "Clean e-ink style monochromatic display"),
        ];

        public static ThemePalette GetPalette(int index)
        {
            int safeIndex = Math.Abs(index) % Palettes.Count;
            var p = Palettes[safeIndex];
            return new ThemePalette(p.BgColor, p.FgColor, p.GridDotColor);
        }
    }
}
