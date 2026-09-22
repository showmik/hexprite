using System.Windows.Media;
using Hexprite.Core;

namespace Hexprite.Services
{
    public class FontImportOptions
    {
        public int TargetHeight { get; set; } = 8;
        public int FirstChar { get; set; } = 32;
        public int LastChar { get; set; } = 126;
        public int BaselineOffset { get; set; }
        public int LetterSpacing { get; set; } = 1;
        public int Threshold { get; set; } = 128;
        public bool AntiAlias { get; set; }
    }

    /// <summary>
    /// Service for importing fonts into Hexprite FontDocuments.
    /// </summary>
    public interface IFontImportService
    {
        /// <summary>
        /// Imports a TrueType or system font into a new FontDocument.
        /// </summary>
        FontDocument ImportFromTrueType(FontFamily fontFamily, FontImportOptions options);

        /// <summary>
        /// Imports a font from a local .ttf or .otf file.
        /// </summary>
        FontDocument ImportFromFontFile(string filePath, FontImportOptions options);

        /// <summary>
        /// Imports a bitmap font from a sprite sheet image, sliced into a regular grid.
        /// </summary>
        FontDocument ImportFromSpriteSheet(string imagePath, int cellWidth, int cellHeight, FontImportOptions options);

        /// <summary>
        /// Imports an AngelCode BMFont (.fnt) format font and its associated texture.
        /// </summary>
        FontDocument ImportFromBMFont(string fntPath, FontImportOptions options);
    }
}
