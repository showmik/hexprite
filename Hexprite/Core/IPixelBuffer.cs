using System;

namespace Hexprite.Core
{
    /// <summary>
    /// Abstracts the underlying pixel data array to allow Hexprite to support multiple color modes 
    /// (e.g. 1-bit Monochrome, RGB) without breaking the core tool architecture.
    /// </summary>
    public interface IPixelBuffer
    {
        /// <summary>
        /// Gets the underlying 1-bit boolean array for backward compatibility and monochrome rendering.
        /// Throws if the underlying buffer cannot be represented as a bool array.
        /// </summary>
        bool[] GetMonochromeData();

        /// <summary>
        /// Writes canvas-sized pixel data back into the buffer.
        /// For MonochromePixelBuffer, this replaces the internal array.
        /// For OverflowPixelBuffer, this writes into the canvas window of the extended buffer.
        /// </summary>
        void WriteMonochromeData(bool[] data);

        /// <summary>True if this buffer preserves off-canvas pixel data.</summary>
        bool PreserveOverflow { get; }

        /// <summary>
        /// Creates a deep copy of the pixel buffer.
        /// </summary>
        IPixelBuffer Clone();
    }

    public class MonochromePixelBuffer : IPixelBuffer
    {
        private readonly bool[] _pixels;

        public MonochromePixelBuffer(int size)
        {
            _pixels = new bool[size];
        }

        public MonochromePixelBuffer(bool[] pixels)
        {
            _pixels = pixels;
        }

        public bool PreserveOverflow => false;

        public bool[] GetMonochromeData() => _pixels;

        public void WriteMonochromeData(bool[] data)
        {
            if (data.Length != _pixels.Length)
                throw new ArgumentException("Data length mismatch");
            Array.Copy(data, _pixels, _pixels.Length);
        }

        public IPixelBuffer Clone()
        {
            return new MonochromePixelBuffer((bool[])_pixels.Clone());
        }
    }
}
