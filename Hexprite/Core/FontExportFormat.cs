namespace Hexprite.Core
{
    /// <summary>
    /// Target font format for code export.
    /// Each format produces a different C struct/array layout.
    /// </summary>
    public enum FontExportFormat
    {
        /// <summary>
        /// Adafruit GFX: <c>GFXglyph[]</c> + concatenated bitmap + <c>GFXfont</c> struct.
        /// Usage: <c>display.setFont(&amp;myFont);</c>
        /// </summary>
        AdafruitGfx,

        /// <summary>
        /// u8g2: BDF intermediate export for use with <c>bdfconv</c> tool.
        /// Produces a <c>.bdf</c> file that can be converted to u8g2 C array.
        /// Usage: <c>u8g2.setFont(myFont);</c>
        /// </summary>
        U8g2Bdf,

        /// <summary>
        /// LVGL: <c>lv_font_t</c> with glyph descriptors, bitmap, and character map.
        /// Usage: <c>lv_obj_set_style_text_font(label, &amp;myFont, 0);</c>
        /// </summary>
        Lvgl,

        /// <summary>
        /// Raw C array — one <c>const uint8_t[]</c> per glyph with metric comments.
        /// For custom rendering engines that don't use a standard font library.
        /// </summary>
        RawCArray,

        /// <summary>
        /// Flipper Zero: 1-bit column-major packed font data for Flipper Zero canvas/firmware.
        /// </summary>
        FlipperZero,
    }
}
