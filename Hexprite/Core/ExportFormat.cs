namespace Hexprite.Core
{
    /// <summary>
    /// Identifies the target platform/library for which code will be generated.
    /// </summary>
    public enum ExportFormat
    {
        /// <summary>const uint8_t PROGMEM name[] = {...}; — Adafruit GFX, MSB first</summary>
        AdafruitGfx,

        /// <summary>const uint8_t U8X8_PROGMEM name[] = {...}; — u8g2 drawBitmap, MSB first</summary>
        U8g2DrawBitmap,

        /// <summary>XBM layout: PROGMEM, LSB first (leftmost pixel → bit 0)</summary>
        U8g2DrawXBM,

        /// <summary>Plain C array, no PROGMEM — ESP32 / STM32 / Pico</summary>
        PlainCArray,

        /// <summary>MicroPython bytearray — framebuf.MONO_HLSB</summary>
        MicroPython,

        /// <summary>Raw hex values, one row per line, no wrapper</summary>
        RawHex,

        /// <summary>Raw binary strings, one row per line, no wrapper</summary>
        RawBinary,

        /// <summary>2D pixel array or indexed palette matrix (1 byte/element per pixel)</summary>
        Indexed2D,

        /// <summary>Flipper Zero compressed bitmap (Heatshrink header + canvas_draw_bitmap)</summary>
        FlipperCompressedBitmap,

        /// <summary>Flipper Zero XBM (LSB first + canvas_draw_xbm)</summary>
        FlipperXbm,

        /// <summary>Flipper Zero Icon asset (I_name_WxH + canvas_draw_icon)</summary>
        FlipperCanvasIcon,

        /// <summary>LiquidCrystal 5x8 custom character for HD44780 1602/2004 LCDs (byte name[8] = { B..., ... }; lcd.createChar)</summary>
        LiquidCrystalChar,
    }
}
