using Hexprite.Core;

namespace Hexprite.Services
{
    public interface IXbmService
    {
        /// <summary>
        /// Reads a standalone .xbm file and extracts its declared width/height
        /// (from the file's #define ..._width / #define ..._height lines) plus
        /// the raw file text, ready to be unpacked via
        /// <see cref="ICodeGeneratorService.ParseXbmToState"/> into a freshly
        /// created document's pixel buffer.
        /// Throws <see cref="System.FormatException"/> if the file lacks valid
        /// dimension declarations.
        /// </summary>
        (int Width, int Height, string Body) ParseFile(string path);

        /// <summary>
        /// Writes a single frame as a standalone standard-XBM (.xbm) file:
        /// #define name_width/_height plus a static unsigned char name_bits[] array.
        /// No compression, no container header — plain XBM has neither.
        /// </summary>
        void ExportImage(SpriteState spriteState, int frameIndex, string targetFilePath);
    }
}
