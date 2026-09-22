using System;
using System.Collections.Generic;

namespace Hexprite.Services.Compression
{
    /// <summary>
    /// Run-Length Encoding compressor using a dynamic escape byte.
    /// The least-frequent byte in the input is chosen as the escape marker,
    /// minimising overhead for data that contains all 256 byte values.
    ///
    /// Compressed format:
    ///   Byte 0        = escape marker (E)
    ///   Bytes 1..N    = encoded data
    ///     [E, count, value]  → emit 'value' repeated 'count' times  (count ≥ 1)
    ///     [any other byte]   → emit verbatim
    ///   All occurrences of byte E in the original data MUST be encoded as [E, count, E].
    /// </summary>
    internal static class RleCompressor
    {
        /// <summary>
        /// Compress <paramref name="data"/> with dynamic-escape RLE.
        /// Returns the original array reference if compression does not reduce size.
        /// </summary>
        public static byte[] Compress(byte[] data)
        {
            if (data == null || data.Length == 0)
                return data ?? [];

            byte escape = FindLeastFrequentByte(data);

            // Worst case: every byte is the escape → 3× expansion + 1 header byte.
            // We'll use a list and check at the end.
            var output = new List<byte>(data.Length + 1) { escape };

            int i = 0;
            while (i < data.Length)
            {
                byte current = data[i];

                // Count run length
                int runLen = 1;
                while (i + runLen < data.Length && data[i + runLen] == current && runLen < 255)
                    runLen++;

                if (current == escape)
                {
                    // Escape byte MUST always be encoded as a triple, regardless of run length.
                    // Split runs > 255 into multiple triples.
                    while (runLen > 0)
                    {
                        int chunk = Math.Min(runLen, 255);
                        output.Add(escape);
                        output.Add((byte)chunk);
                        output.Add(escape);
                        runLen -= chunk;
                        i += chunk;
                    }
                }
                else if (runLen >= 3)
                {
                    // Encode run: [escape, count, value]
                    output.Add(escape);
                    output.Add((byte)runLen);
                    output.Add(current);
                    i += runLen;
                }
                else
                {
                    // Emit verbatim (1 or 2 identical bytes — not worth encoding)
                    for (int j = 0; j < runLen; j++)
                        output.Add(current);
                    i += runLen;
                }
            }

            // Only return compressed if it's actually smaller
            if (output.Count >= data.Length)
                return data;

            return [.. output];
        }

        /// <summary>
        /// Decompress data produced by <see cref="Compress"/>.
        /// </summary>
        public static byte[] Decompress(byte[] compressed, int originalSize)
        {
            if (compressed == null || compressed.Length == 0)
                return [];

            byte escape = compressed[0];
            var output = new byte[originalSize];
            int si = 1, di = 0;

            while (si < compressed.Length && di < originalSize)
            {
                if (compressed[si] == escape && si + 2 < compressed.Length)
                {
                    byte count = compressed[si + 1];
                    byte value = compressed[si + 2];
                    for (int j = 0; j < count && di < originalSize; j++)
                        output[di++] = value;
                    si += 3;
                }
                else
                {
                    output[di++] = compressed[si++];
                }
            }

            return output;
        }

        /// <summary>
        /// Finds the byte value that appears least often in <paramref name="data"/>.
        /// If multiple bytes are tied, returns the first one found.
        /// </summary>
        private static byte FindLeastFrequentByte(byte[] data)
        {
            var freq = new int[256];
            foreach (byte b in data)
                freq[b]++;

            byte best = 0;
            int bestCount = int.MaxValue;
            for (int i = 0; i < 256; i++)
            {
                if (freq[i] < bestCount)
                {
                    bestCount = freq[i];
                    best = (byte)i;
                    if (bestCount == 0) break; // can't do better than 0
                }
            }

            return best;
        }
    }
}
