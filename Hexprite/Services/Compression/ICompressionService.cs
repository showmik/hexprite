using Hexprite.Core;

namespace Hexprite.Services.Compression
{
    /// <summary>
    /// Service that compresses byte arrays for embedded export and generates
    /// matching C decompression function source code.
    /// </summary>
    public interface ICompressionService
    {
        /// <summary>
        /// Compresses <paramref name="data"/> using the specified <paramref name="mode"/>.
        /// Returns the original data unchanged if compression does not reduce size.
        /// </summary>
        byte[] Compress(byte[] data, CompressionMode mode);

        /// <summary>
        /// Decompresses data that was compressed with <see cref="Compress"/>.
        /// Used for round-trip testing.
        /// </summary>
        byte[] Decompress(byte[] compressed, int originalSize, CompressionMode mode);

        /// <summary>
        /// Returns the C decompression function source code for the given compression mode.
        /// This function is emitted alongside compressed data so users can decompress at runtime.
        /// </summary>
        string GenerateDecompressorCode(CompressionMode mode);

        /// <summary>
        /// Returns the estimated Flash memory cost (in bytes) of the decompressor
        /// function on a typical AVR microcontroller.
        /// </summary>
        int GetDecompressorFlashCost(CompressionMode mode);
    }
}
