namespace Hexprite.Core
{
    /// <summary>
    /// Compression algorithm applied to exported byte arrays.
    /// </summary>
    public enum CompressionMode
    {
        /// <summary>No compression — raw byte array (default, current behavior).</summary>
        None,

        /// <summary>Run-Length Encoding — simple, ~120B decoder on AVR, good for uniform sprites.</summary>
        Rle,

        /// <summary>LZSS — better compression ratio, ~200B decoder on AVR, handles mixed patterns.</summary>
        Lzss,
    }
}
