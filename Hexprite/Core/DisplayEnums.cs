namespace Hexprite.Core
{
    /// <summary>
    /// OLED/embedded display types for preview rendering.
    /// </summary>
    public enum DisplayType
    {
        GenericWhite,
        SSD1306Blue,
        SSD1306Green,
        ePaper,
        FlipperZero,
    }

    /// <summary>
    /// Display simulation presets for realistic preview rendering.
    /// </summary>
    public enum DisplaySimulationPreset
    {
        Flat = 0,
        GenericLcd = 1,
        Ssd1306OledBlue = 2,
        Ssd1306OledGreen = 3,
        EPaper = 4,
        FlipperZeroLcd = 5,
        Ssd1306OledWhite = 6,
    }

    /// <summary>
    /// Quality levels for preview simulation rendering.
    /// </summary>
    public enum PreviewQuality
    {
        Fast = 0,
        Balanced = 1,
        High = 2,
    }

    /// <summary>
    /// Category filter options for narrowing glyph map display in Font Mode.
    /// </summary>
    public enum GlyphCategoryFilter
    {
        All = 0,
        Letters = 1,
        Numbers = 2,
        Symbols = 3,
        CustomizedOnly = 4,
    }
}
