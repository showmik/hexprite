namespace Hexprite.Services
{
    /// <summary>
    /// Minimal interface for pixel-level selection clipping.
    /// Used by DrawingService to mask operations to specific pixels.
    /// </summary>
    public interface IPixelClip
    {
        /// <summary>Returns true if the pixel at (x, y) is inside the clip region.</summary>
        bool IsPixelInClip(int x, int y);
    }
}
