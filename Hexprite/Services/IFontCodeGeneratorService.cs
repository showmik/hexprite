using Hexprite.Core;

namespace Hexprite.Services
{
    /// <summary>
    /// Service for generating export code from a font document.
    /// </summary>
    public interface IFontCodeGeneratorService
    {
        /// <summary>
        /// Generates the font code based on the document and export settings.
        /// </summary>
        string GenerateCode(FontDocument doc, FontExportSettings settings);
    }
}
