namespace Hexprite.Core
{
    /// <summary>
    /// Defines the color mode for a sprite document.
    /// Drives behavior at rendering and export layers.
    /// There are exactly TWO color modes: Monochrome and Rgb.
    /// Indexed mode is deliberately omitted - palette quantization is an export option.
    /// </summary>
    public enum ColorMode
    {
        /// <summary>
        /// Monochrome 1-bit: bool[] pixels where false=black, white=true.
        /// No transparency in data - false means black pixel, not transparent.
        /// </summary>
        Monochrome,

        /// <summary>
        /// True color: Color[] with full RGBA.
        /// Alpha channel determines transparency.
        /// </summary>
        Rgb,
    }
}
