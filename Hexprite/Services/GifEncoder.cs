using System;
using System.Globalization;
using System.IO;
using System.Windows.Media;

namespace Hexprite.Services
{
    /// <summary>
    /// Encodes a sequence of pixel buffers into a standard GIF file format (GIF89a).
    /// Expected usage sequence: Constructor -> SetPalette -> SetLoop (optional) -> AddFrame (multiple) -> Finish/Dispose.
    /// </summary>
    public class GifEncoder : IDisposable
    {
        private const int HashTableSize = 5003;

        private readonly Stream _stream;
        private readonly ushort _width;
        private readonly ushort _height;
        private Color[] _palette = [];
        private bool _isDisposed;
        private bool _isFinished;
        private bool _gctWritten;
        private bool _netscapeWritten;
        private ushort? _loopCount;

        // Reusable buffers to minimize GC allocations across animation frames
        private readonly int[] _hashTable = new int[HashTableSize];
        private readonly int[] _codeTable = new int[HashTableSize];
        private readonly byte[] _block = new byte[255];

        /// <summary>
        /// Initializes a new instance of the GIF encoder.
        /// </summary>
        /// <param name="stream">The target writable stream for the GIF data.</param>
        /// <param name="width">Canvas width (greater than 0).</param>
        /// <param name="height">Canvas height (greater than 0).</param>
        public GifEncoder(Stream stream, ushort width, ushort height)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (!stream.CanWrite)
            {
                throw new ArgumentException("Target stream must be writable.", nameof(stream));
            }
            if (width == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Width must be greater than zero.");
            }
            if (height == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height), "Height must be greater than zero.");
            }

            _stream = stream;
            _width = width;
            _height = height;

            // Header
            WriteString("GIF89a");

            // Logical Screen Descriptor
            WriteShort(_width);
            WriteShort(_height);
            _stream.WriteByte(0xF7); // GCT present, 8-bit color res, 256 colors
            _stream.WriteByte(0);    // Background color index
            _stream.WriteByte(0);    // Pixel aspect ratio
        }

        /// <summary>
        /// Sets the global color table for the GIF.
        /// </summary>
        /// <param name="colors">Array of up to 256 colors.</param>
        public void SetPalette(Color[] colors)
        {
            ArgumentNullException.ThrowIfNull(colors);
            _palette = colors.Length == 0 ? [Colors.Transparent, Colors.Black] : colors;

            EnsureGlobalColorTableWritten();
        }

        private void EnsureGlobalColorTableWritten()
        {
            if (_gctWritten) return;
            _gctWritten = true;

            // Write Global Color Table (always 256 colors for maximum compatibility)
            for (int i = 0; i < 256; i++)
            {
                if (i < _palette.Length)
                {
                    _stream.WriteByte(_palette[i].R);
                    _stream.WriteByte(_palette[i].G);
                    _stream.WriteByte(_palette[i].B);
                }
                else
                {
                    _stream.WriteByte(0);
                    _stream.WriteByte(0);
                    _stream.WriteByte(0);
                }
            }

            if (_loopCount.HasValue && !_netscapeWritten)
            {
                WriteNetscapeExtension(_loopCount.Value);
            }
        }

        /// <summary>
        /// Sets the animation loop count via Netscape Application Extension.
        /// </summary>
        /// <param name="loopCount">Number of loops (0 for infinite).</param>
        public void SetLoop(ushort loopCount)
        {
            _loopCount = loopCount;
            if (_gctWritten && !_netscapeWritten)
            {
                WriteNetscapeExtension(loopCount);
            }
        }

        private void WriteNetscapeExtension(ushort loopCount)
        {
            if (_netscapeWritten) return;
            _netscapeWritten = true;

            // Application Extension for Netscape Looping
            _stream.WriteByte(0x21); // Extension Introducer
            _stream.WriteByte(0xFF); // Application Extension Label
            _stream.WriteByte(11);   // Block Size
            WriteString("NETSCAPE2.0");
            _stream.WriteByte(3);    // Sub-block size
            _stream.WriteByte(1);    // Sub-block ID
            WriteShort(loopCount);   // Loop count (0 = infinite)
            _stream.WriteByte(0);    // Block Terminator
        }

        /// <summary>
        /// Adds an indexed pixel frame to the GIF.
        /// Must be called after <see cref="SetPalette"/> and before <see cref="Finish"/>.
        /// </summary>
        /// <param name="indexedPixels">Pixel data mapped to the palette.</param>
        /// <param name="delayCentiseconds">Delay time in 1/100ths of a second (clamped to at least 1cs = 10ms).</param>
        /// <param name="left">Sub-rectangle horizontal offset from left.</param>
        /// <param name="top">Sub-rectangle vertical offset from top.</param>
        /// <param name="frameWidth">Sub-rectangle width (defaults to canvas width).</param>
        /// <param name="frameHeight">Sub-rectangle height (defaults to canvas height).</param>
        /// <param name="transparentIndex">Optional explicit transparent color index (defaults to auto-detecting index 0 if alpha is 0).</param>
        /// <param name="disposalMethod">Disposal method (2 = Restore to background color, 1 = Do not dispose / keep).</param>
        /// <param name="localPalette">Optional local color table for this specific frame.</param>
        public void AddFrame(
            byte[] indexedPixels,
            ushort delayCentiseconds,
            ushort left = 0,
            ushort top = 0,
            ushort? frameWidth = null,
            ushort? frameHeight = null,
            byte? transparentIndex = null,
            byte disposalMethod = 2,
            Color[]? localPalette = null)
        {
            ArgumentNullException.ThrowIfNull(indexedPixels);
            ushort fw = frameWidth ?? _width;
            ushort fh = frameHeight ?? _height;

            if (fw == 0 || fh == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frameWidth), "Frame dimensions must be greater than zero.");
            }

            int expectedLength = fw * fh;
            if (indexedPixels.Length != expectedLength)
            {
                throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"Indexed pixel buffer length ({indexedPixels.Length}) must equal frame width * height ({expectedLength})."), nameof(indexedPixels));
            }

            EnsureGlobalColorTableWritten();

            var activePalette = localPalette ?? _palette;

            // Graphic Control Extension
            _stream.WriteByte(0x21); // Extension Introducer
            _stream.WriteByte(0xF9); // Graphic Control Label
            _stream.WriteByte(4);    // Block Size

            bool hasTransparency;
            byte transIdx;
            if (transparentIndex.HasValue)
            {
                hasTransparency = true;
                transIdx = transparentIndex.Value;
            }
            else
            {
                hasTransparency = activePalette.Length > 0 && activePalette[0].A == 0;
                transIdx = 0;
            }

            // Packed Fields:
            // Reserved: 3 bits
            // Disposal Method: 3 bits (1 = Keep, 2 = Restore to background color)
            // User Input Flag: 1 bit
            // Transparent Color Flag: 1 bit
            byte packed = (byte)(((disposalMethod & 0x07) << 2) | (hasTransparency ? 1 : 0));
            _stream.WriteByte(packed);

            // Clamp delay to at least 1 centisecond (10ms) to avoid browser freeze/zero-delay bugs
            ushort delay = Math.Clamp(delayCentiseconds, (ushort)1, ushort.MaxValue);
            WriteShort(delay);

            _stream.WriteByte(transIdx); // Transparent Color Index
            _stream.WriteByte(0);        // Block Terminator

            // Image Descriptor
            _stream.WriteByte(0x2C);   // Image Separator
            WriteShort(left);          // Image Left
            WriteShort(top);           // Image Top
            WriteShort(fw);            // Image Width
            WriteShort(fh);            // Image Height

            if (localPalette != null && localPalette.Length > 0)
            {
                _stream.WriteByte(0x87); // LCT present, 256 colors (8 bits/pixel)
                WriteColorTable(localPalette);
            }
            else
            {
                _stream.WriteByte(0x00); // No Local Color Table
            }

            // LZW Image Data
            WriteLzw(indexedPixels);
        }

        private void WriteColorTable(Color[] colors)
        {
            for (int i = 0; i < 256; i++)
            {
                if (i < colors.Length)
                {
                    _stream.WriteByte(colors[i].R);
                    _stream.WriteByte(colors[i].G);
                    _stream.WriteByte(colors[i].B);
                }
                else
                {
                    _stream.WriteByte(0);
                    _stream.WriteByte(0);
                    _stream.WriteByte(0);
                }
            }
        }

        /// <summary>
        /// Finalizes the GIF structure and flushes the stream.
        /// </summary>
        public void Finish()
        {
            if (_isFinished)
            {
                return;
            }

            _isFinished = true;
            EnsureGlobalColorTableWritten();
            _stream.WriteByte(0x3B); // Trailer
            _stream.Flush();
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                Finish();
                _isDisposed = true;
            }
            GC.SuppressFinalize(this);
        }

        private void WriteString(string str)
        {
            foreach (char c in str)
            {
                _stream.WriteByte((byte)c);
            }
        }

        private void WriteShort(ushort value)
        {
            _stream.WriteByte((byte)(value & 0xFF));
            _stream.WriteByte((byte)((value >> 8) & 0xFF));
        }

        private void WriteLzw(byte[] pixels)
        {
            const int colorDepth = 8; // Force 8-bit LZW minimum code size for maximum compatibility
            _stream.WriteByte(colorDepth);

            var encoder = new LzwEncoder(pixels, colorDepth, _hashTable, _codeTable, _block);
            encoder.Encode(_stream);

            _stream.WriteByte(0); // Block Terminator
        }

        /// <summary>
        /// Internal encoder implementing LZW compression as specified in GIF89a.
        /// </summary>
        private class LzwEncoder(byte[] pixels, int initCodeSize, int[] hashTable, int[] codeTable, byte[] block)
        {
            private readonly byte[] _pixels = pixels;
            private readonly int _initCodeSize = Math.Max(2, initCodeSize);
            private readonly int[] _hashTable = hashTable;
            private readonly int[] _codeTable = codeTable;
            private readonly byte[] _block = block;

            private int _currentCodeSize;
            private int _nextCode;
            private int _clearCode;
            private int _eofCode;

            private int _currentBits;
            private int _currentBitsCount;

            public void Encode(Stream stream)
            {
                _clearCode = 1 << _initCodeSize;
                _eofCode = _clearCode + 1;
                _nextCode = _clearCode + 2;
                _currentCodeSize = _initCodeSize + 1;

                Array.Fill(_hashTable, -1);

                int blockSize = 0;
                bool clearFlag = false;

                void WriteCode(int code)
                {
                    _currentBits |= code << _currentBitsCount;
                    _currentBitsCount += _currentCodeSize;
                    while (_currentBitsCount >= 8)
                    {
                        _block[blockSize++] = (byte)(_currentBits & 0xFF);
                        if (blockSize == 255)
                        {
                            stream.WriteByte(255);
                            stream.Write(_block, 0, 255);
                            blockSize = 0;
                        }
                        _currentBits >>= 8;
                        _currentBitsCount -= 8;
                    }

                    if (clearFlag)
                    {
                        _currentCodeSize = _initCodeSize + 1;
                        clearFlag = false;
                    }
                    else if (_nextCode > ((1 << _currentCodeSize) - 1))
                    {
                        if (_currentCodeSize < 12)
                            _currentCodeSize++;
                    }
                }

                WriteCode(_clearCode);

                int prefix = _pixels.Length > 0 ? _pixels[0] : 0;

                for (int i = 1; i < _pixels.Length; i++)
                {
                    byte suffix = _pixels[i];
                    int hashKey = (suffix << 12) ^ prefix;
                    int hashIdx = (hashKey & 0x7FFFFFFF) % HashTableSize;
                    int step = 1;
                    if (hashIdx == 0) step = HashTableSize - 1;

                    bool found = false;
                    while (_hashTable[hashIdx] != -1)
                    {
                        if (_hashTable[hashIdx] == ((prefix << 8) | suffix))
                        {
                            prefix = _codeTable[hashIdx];
                            found = true;
                            break;
                        }
                        hashIdx -= step;
                        if (hashIdx < 0) hashIdx += HashTableSize;
                    }

                    if (!found)
                    {
                        WriteCode(prefix);

                        if (_nextCode < 4096)
                        {
                            _hashTable[hashIdx] = (prefix << 8) | suffix;
                            _codeTable[hashIdx] = _nextCode++;
                        }
                        else
                        {
                            clearFlag = true;
                            WriteCode(_clearCode);
                            _nextCode = _clearCode + 2;
                            Array.Fill(_hashTable, -1);
                        }

                        prefix = suffix;
                    }
                }

                WriteCode(prefix);
                WriteCode(_eofCode);

                if (_currentBitsCount > 0)
                {
                    _block[blockSize++] = (byte)(_currentBits & 0xFF);
                }

                if (blockSize > 0)
                {
                    stream.WriteByte((byte)blockSize);
                    stream.Write(_block, 0, blockSize);
                }
            }
        }
    }
}