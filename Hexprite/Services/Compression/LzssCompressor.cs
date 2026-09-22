using System;
using System.Collections.Generic;

namespace Hexprite.Services.Compression
{
    /// <summary>
    /// LZSS compressor with window=256 (8 bits) and lookahead=16 (4 bits).
    ///
    /// Bitstream format (MSB-first packing within bytes):
    ///   1 + 8 bits  = literal byte              (9 bits)
    ///   0 + 8 bits  + 4 bits = backreference    (13 bits)
    ///     offset: stored as (actual_offset - 1), so 0 = 1 byte back, 255 = 256 bytes back
    ///     length: stored as (actual_length - 2), so 0 = 2 bytes, 15 = 17 bytes
    ///   Minimum useful match length = 2 (9 bits literal vs 13 bits backref,
    ///     but 2×9=18 &gt; 13, so backref wins for length ≥ 2).
    ///   Final byte is zero-padded.
    /// </summary>
    internal static class LzssCompressor
    {
        private const int WindowBits = 8;
        private const int LookaheadBits = 4;
        private const int WindowSize = 1 << WindowBits;     // 256
        private const int MaxMatchLength = (1 << LookaheadBits) + 1; // 17 (stored 0..15 → length 2..17)
        private const int MinMatchLength = 2;

        public static byte[] Compress(byte[] data)
        {
            if (data == null || data.Length == 0)
                return data ?? [];

            var writer = new BitWriter();
            int pos = 0;

            while (pos < data.Length)
            {
                // Search for the longest match in the sliding window
                int bestOffset = 0;
                int bestLength = 0;

                int searchStart = Math.Max(0, pos - WindowSize);
                int maxLook = Math.Min(MaxMatchLength, data.Length - pos);

                for (int s = pos - 1; s >= searchStart; s--)
                {
                    int len = 0;
                    while (len < maxLook && data[s + len] == data[pos + len])
                    {
                        len++;
                        // Allow matching into the lookahead (copy-from-self pattern)
                        // This is safe because we check s + len, and for self-referencing
                        // matches, s + len may equal pos + earlier bytes we've already matched.
                    }

                    if (len > bestLength)
                    {
                        bestLength = len;
                        bestOffset = pos - s;  // 1-based offset (1..256)
                        if (len == maxLook) break; // can't do better
                    }
                }

                if (bestLength >= MinMatchLength)
                {
                    // Emit backreference: 0 + offset(8 bits) + length(4 bits)
                    writer.WriteBit(0);
                    writer.WriteBits((uint)(bestOffset - 1), WindowBits);    // offset stored as offset-1
                    writer.WriteBits((uint)(bestLength - 2), LookaheadBits); // length stored as length-2
                    pos += bestLength;
                }
                else
                {
                    // Emit literal: 1 + byte(8 bits)
                    writer.WriteBit(1);
                    writer.WriteBits(data[pos], 8);
                    pos++;
                }
            }

            byte[] compressed = writer.ToArray();

            // Only return compressed if it's actually smaller
            if (compressed.Length >= data.Length)
                return data;

            return compressed;
        }

        public static byte[] Decompress(byte[] compressed, int originalSize)
        {
            if (compressed == null || compressed.Length == 0)
                return [];

            var reader = new BitReader(compressed);
            var output = new byte[originalSize];
            int di = 0;
            int totalBits = compressed.Length * 8;

            while (di < originalSize)
            {
                // Need at least 1 bit for the flag
                if (reader.BitsRead + 1 > totalBits)
                    break;

                int flag = reader.ReadBit();

                if (flag == 1)
                {
                    // Literal: 8 bits
                    if (reader.BitsRead + 8 > totalBits) break;
                    output[di++] = (byte)reader.ReadBits(8);
                }
                else
                {
                    // Backreference: 8-bit offset + 4-bit length
                    if (reader.BitsRead + 12 > totalBits) break;
                    int offset = reader.ReadBits(WindowBits) + 1;
                    int length = reader.ReadBits(LookaheadBits) + 2;

                    // Guard against corrupt/malformed input referencing before the output start.
                    if (offset > di) break;

                    for (int j = 0; j < length && di < originalSize; j++)
                    {
                        output[di] = output[di - offset];
                        di++;
                    }
                }
            }

            return output;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Bit I/O helpers
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Packs bits MSB-first into a byte buffer.</summary>
        private sealed class BitWriter
        {
            private readonly List<byte> _buffer = [];
            private byte _currentByte;
            private int _bitPos = 7; // MSB-first: start at bit 7

            public void WriteBit(int bit)
            {
                if (bit != 0)
                    _currentByte |= (byte)(1 << _bitPos);

                _bitPos--;
                if (_bitPos < 0)
                {
                    _buffer.Add(_currentByte);
                    _currentByte = 0;
                    _bitPos = 7;
                }
            }

            public void WriteBits(uint value, int count)
            {
                for (int i = count - 1; i >= 0; i--)
                    WriteBit((int)((value >> i) & 1));
            }

            public byte[] ToArray()
            {
                // Flush partial byte (zero-padded)
                if (_bitPos < 7)
                    _buffer.Add(_currentByte);

                return [.. _buffer];
            }
        }

        /// <summary>Reads bits MSB-first from a byte buffer.</summary>
        private sealed class BitReader(byte[] data)
        {
            private readonly byte[] _data = data;
            private int _bitPos;

            public int BitsRead => _bitPos;

            public int ReadBit()
            {
                int byteIndex = _bitPos >> 3;
                int bitIndex = 7 - (_bitPos & 7);
                _bitPos++;
                return (_data[byteIndex] >> bitIndex) & 1;
            }

            public int ReadBits(int count)
            {
                int value = 0;
                for (int i = 0; i < count; i++)
                    value = (value << 1) | ReadBit();
                return value;
            }
        }
    }
}
