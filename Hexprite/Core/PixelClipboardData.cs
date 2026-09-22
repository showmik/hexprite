namespace Hexprite.Core
{
    /// <summary>
    /// Immutable snapshot of copied pixel data.
    /// Stored in the application-level pixel clipboard so it can be pasted
    /// into any document tab.
    /// </summary>
    /// <remarks>Creates a new instance of clipboard data.</remarks>
    public class PixelClipboardData(bool[,] pixels, int width, int height)
    {
        /// <summary>The pixel grid data.</summary>
        public bool[,] Pixels { get; } = pixels;
        /// <summary>The width of the clipboard data.</summary>
        public int Width { get; } = width;
        /// <summary>The height of the clipboard data.</summary>
        public int Height { get; } = height;
    }
}
