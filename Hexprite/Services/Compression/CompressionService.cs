using System;
using Hexprite.Core;

namespace Hexprite.Services.Compression
{
    /// <summary>
    /// Orchestrates compression/decompression and provides the C source code
    /// for the matching decompressor function to be emitted alongside compressed data.
    /// </summary>
    public class CompressionService : ICompressionService
    {
        /// <inheritdoc/>
        public byte[] Compress(byte[] data, CompressionMode mode) => mode switch
        {
            CompressionMode.Rle  => RleCompressor.Compress(data),
            CompressionMode.Lzss => LzssCompressor.Compress(data),
            _                    => data,
        };

        /// <inheritdoc/>
        public byte[] Decompress(byte[] compressed, int originalSize, CompressionMode mode) => mode switch
        {
            CompressionMode.Rle  => RleCompressor.Decompress(compressed, originalSize),
            CompressionMode.Lzss => LzssCompressor.Decompress(compressed, originalSize),
            _                    => compressed,
        };

        /// <inheritdoc/>
        public string GenerateDecompressorCode(CompressionMode mode) => mode switch
        {
            CompressionMode.Rle  => RleDecompressorCode,
            CompressionMode.Lzss => LzssDecompressorCode,
            _                    => string.Empty,
        };

        /// <inheritdoc/>
        public int GetDecompressorFlashCost(CompressionMode mode) => mode switch
        {
            CompressionMode.Rle  => 120,
            CompressionMode.Lzss => 200,
            _                    => 0,
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  C decompressor templates
        // ═══════════════════════════════════════════════════════════════════════

        private const string RleDecompressorCode =
@"// ── Hexprite RLE Decompressor (paste once per project) ──────────────
// Decodes RLE-compressed sprite data. Compatible with PROGMEM on AVR.
#if defined(__AVR__)
  #include <avr/pgmspace.h>
  #define _hxp_pgm_read(ptr) pgm_read_byte(ptr)
#else
  #define _hxp_pgm_read(ptr) (*(const uint8_t*)(ptr))
#endif

static inline void hexprite_rle_decode(const uint8_t* src, uint16_t srcLen,
                                uint8_t* dst, uint16_t dstLen) {
    if (srcLen == 0) return;
    const uint8_t esc = _hxp_pgm_read(&src[0]);
    uint16_t si = 1, di = 0;
    while (si < srcLen && di < dstLen) {
        uint8_t current = _hxp_pgm_read(&src[si]);
        if (current == esc && si + 2 < srcLen) {
            uint8_t count = _hxp_pgm_read(&src[si + 1]);
            uint8_t value = _hxp_pgm_read(&src[si + 2]);
            for (uint16_t j = 0; j < count && di < dstLen; j++)
                dst[di++] = value;
            si += 3;
        } else {
            dst[di++] = current;
            si++;
        }
    }
}";

        private const string LzssDecompressorCode =
@"// ── Hexprite LZSS Decompressor (paste once per project) ─────────────
// Decodes LZSS-compressed sprite data (window=256, lookahead=16).
// Compatible with PROGMEM on AVR.
#if defined(__AVR__)
  #include <avr/pgmspace.h>
  #define _hxp_pgm_read(ptr) pgm_read_byte(ptr)
#else
  #define _hxp_pgm_read(ptr) (*(const uint8_t*)(ptr))
#endif

static uint16_t _hxp_bits(const uint8_t* s, uint16_t* p, uint8_t n) {
    uint16_t v = 0;
    for (uint8_t i = 0; i < n; i++) {
        uint8_t b = _hxp_pgm_read(&s[*p >> 3]);
        v = (v << 1) | ((b >> (7 - (*p & 7))) & 1);
        (*p)++;
    }
    return v;
}

static inline void hexprite_lzss_decode(const uint8_t* src, uint16_t srcLen,
                                 uint8_t* dst, uint16_t dstLen) {
    uint16_t sb = (uint16_t)(srcLen * 8), si = 0, di = 0;
    while (si + 9 <= sb && di < dstLen) {
        if (_hxp_bits(src, &si, 1)) {
            dst[di++] = (uint8_t)_hxp_bits(src, &si, 8);
        } else {
            if (si + 12 > sb) break;
            uint16_t off = _hxp_bits(src, &si, 8) + 1;
            uint8_t len = (uint8_t)_hxp_bits(src, &si, 4) + 2;
            if (off > di) break; // corrupt data: backreference before output start
            for (uint8_t j = 0; j < len && di < dstLen; j++) {
                dst[di] = dst[di - off]; di++;
            }
        }
    }
}";
    }
}
