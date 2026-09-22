using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Hexprite.Services.Compression
{
    internal static class HeatshrinkCompressor
    {
        private const int WindowBits = 8;
        private const int LookaheadBits = 4;
        private const int WindowSize = 1 << WindowBits;
        // heatshrink's bitstream always encodes length as (length - 1) in LookaheadBits bits,
        // regardless of any minimum-match heuristic. MinMatchLength below is purely this
        // encoder's own efficiency threshold for choosing a backref over a literal
        // (a backref token costs 1+WindowBits+LookaheadBits=13 bits, so it only pays off
        // once it replaces more than ~13/9 literal bytes) — it is NOT part of the bit encoding.
        private const int MinMatchLength = 2;
        private const int MaxMatchLength = 1 << LookaheadBits; // 16: largest length the 4-bit field can encode (code 15 -> length 16)

        public static byte[] Compress(byte[] data)
        {
            if (data == null || data.Length == 0)
                return [];

            var writer = new BitWriter();
            int pos = 0;

            while (pos < data.Length)
            {
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
                    }

                    if (len > bestLength)
                    {
                        bestLength = len;
                        bestOffset = pos - s;
                        if (len == maxLook) break;
                    }
                }

                if (bestLength >= MinMatchLength)
                {
                    // Emit backreference: 0 + offset(8 bits) + length(4 bits)
                    writer.WriteBit(0);
                    writer.WriteBits((uint)(bestOffset - 1), WindowBits);
                    writer.WriteBits((uint)(bestLength - 1), LookaheadBits); // heatshrink stores length - 1
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

            return writer.ToArray();
        }

        public static byte[] Decompress(byte[] compressed, int originalSize)
        {
            if (compressed == null || compressed.Length == 0)
                return new byte[originalSize];

            var output = new byte[originalSize];
            int di = 0;
            int totalBits = compressed.Length * 8;
            int bitPos = 0;

            // Real heatshrink decoders read backreferences out of a bounded sliding
            // window (2^WindowBits bytes), not out of the full output-so-far. Once
            // decoded output exceeds WindowSize bytes, indexing directly into
            // `output[di - offset]` silently stops matching what a real
            // window-bounded encoder referenced, so we mirror that bounded window
            // explicitly here via a small circular buffer. The window starts
            // zero-initialized; a valid encoder can legitimately emit a backref whose
            // distance reaches before the start of the stream (referencing this
            // zero-filled "virtual padding"), so — matching the real algorithm —
            // there is deliberately no bounds check on the offset here.
            var window = new byte[WindowSize];
            int head = 0; // next write position in the circular window, mod WindowSize

            void PushWindow(byte b)
            {
                window[head & (WindowSize - 1)] = b;
                head++;
            }

            int ReadBit()
            {
                int byteIndex = bitPos >> 3;
                if (byteIndex >= compressed.Length)
                    throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"heatshrink stream truncated at bit {bitPos} (byte {byteIndex} >= {compressed.Length})"));
                int bitIndex = 7 - (bitPos & 7);
                bitPos++;
                return (compressed[byteIndex] >> bitIndex) & 1;
            }

            int ReadBits(int count)
            {
                int value = 0;
                for (int i = 0; i < count; i++)
                    value = (value << 1) | ReadBit();
                return value;
            }

            while (di < originalSize)
            {
                if (bitPos + 1 > totalBits) break;
                int flag = ReadBit();

                if (flag == 1)
                {
                    if (bitPos + 8 > totalBits)
                        throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"heatshrink literal truncated at bit {bitPos} (di={di})"));
                    byte b = (byte)ReadBits(8);
                    output[di++] = b;
                    PushWindow(b);
                }
                else
                {
                    if (bitPos + WindowBits + LookaheadBits > totalBits)
                        throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"heatshrink backref header truncated at bit {bitPos} (di={di})"));
                    int offset = ReadBits(WindowBits) + 1;
                    int length = ReadBits(LookaheadBits) + 1;

                    for (int j = 0; j < length && di < originalSize; j++)
                    {
                        byte b = window[(head - offset) & (WindowSize - 1)];
                        output[di] = b;
                        PushWindow(b);
                        di++;
                    }
                }
            }

            if (di < originalSize)
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"heatshrink decode incomplete: produced {di}/{originalSize} bytes (stream truncated or corrupt)"));

            return output;
        }

        private sealed class BitWriter
        {
            private readonly List<byte> _buffer = [];
            private byte _currentByte;
            private int _bitPos = 7;

            public void WriteBit(int bit)
            {
                if (bit != 0) _currentByte |= (byte)(1 << _bitPos);
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
                if (_bitPos < 7) _buffer.Add(_currentByte);
                return [.. _buffer];
            }
        }
    }
}
