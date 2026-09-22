using System.Windows.Media;

namespace Hexprite.Core
{
    /// <summary>
    /// Represents a font available in the Text Tool's font picker.
    /// Wraps a WPF FontFamily with metadata for display and categorization.
    /// </summary>
    public class FontEntry(string name, FontFamily fontFamily, string source,
        bool isBuiltIn, bool isPixelFont = true)
    {
        /// <summary>Display name shown in the ComboBox.</summary>
        public string Name { get; } = name;

        /// <summary>WPF FontFamily used for rendering.</summary>
        public FontFamily FontFamily { get; } = fontFamily;

        /// <summary>
        /// Source string used for binding and serialization.
        /// For system/bundled fonts this is the family name (e.g. "Arial").
        /// For directory-loaded fonts this may include a path URI.
        /// </summary>
        public string Source { get; } = source;

        /// <summary>True for fonts bundled with the application.</summary>
        public bool IsBuiltIn { get; } = isBuiltIn;

        /// <summary>
        /// True if this is a pixel/bitmap font that should use nearest-neighbor
        /// scaling instead of WPF's default outline scaling.
        /// </summary>
        public bool IsPixelFont { get; } = isPixelFont;

        public override string ToString() => Name;
    }
}
