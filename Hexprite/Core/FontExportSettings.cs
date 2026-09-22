namespace Hexprite.Core
{
    /// <summary>
    /// User-configurable options for font code export.
    /// Persisted inside the <c>.hexp</c> file alongside the <see cref="FontDocument"/>.
    /// </summary>
    public class FontExportSettings
    {
        /// <summary>Target font format / library.</summary>
        public FontExportFormat Format { get; set; } = FontExportFormat.AdafruitGfx;

        /// <summary>
        /// C identifier name for the generated font.
        /// Sanitized to a valid identifier before use.
        /// </summary>
        public string FontName { get; set; } = "myFont";

        /// <summary>Emit a usage comment showing how to use the font in code.</summary>
        public bool IncludeUsageComment { get; set; } = true;

        /// <summary>Emit hex digits in upper-case (0xFF) vs lower-case (0xff).</summary>
        public bool UppercaseHex { get; set; } = true;

        /// <summary>
        /// Include an ASCII art preview of each glyph as a comment above its data.
        /// Adds readability but increases file size.
        /// </summary>
        public bool IncludeGlyphPreview { get; set; } = true;

        /// <summary>Include per-glyph metric comments (width, height, offsets).</summary>
        public bool IncludeMetricComments { get; set; } = true;

        /// <summary>Returns a shallow clone (all fields are value types or strings).</summary>
        public FontExportSettings Clone() => (FontExportSettings)MemberwiseClone();
    }
}
