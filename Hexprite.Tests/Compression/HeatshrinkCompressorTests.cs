using System;
using System.IO;
using System.Security.Cryptography;
using Hexprite.Services.Compression;
using Xunit;

namespace Hexprite.Tests.Compression
{
    [Trait("Category", "Unit")]
    public class HeatshrinkCompressorTests
    {
        // ── Bit-exact fixtures (window_sz2=8, lookahead_sz2=4) ──────────────────
        // Hand-crafted and independently verified bit-by-bit against the real
        // heatshrink framing (1 tag bit; literal = 8 bits; backref = 8-bit
        // (offset-1) + 4-bit (length-1), MSB-first) before being embedded here.
        // Cross-checked against the reference `heatshrink2` Python package.

        [Fact]
        public void Decompress_LiteralThenBackref_ExpandsRun()
        {
            // literal 'A' (0x41), then backref offset=1 length=3 -> "AAAA"
            byte[] compressed = { 0xA0, 0x80, 0x08 };
            byte[] result = HeatshrinkCompressor.Decompress(compressed, 4);
            Assert.Equal(new byte[] { 0x41, 0x41, 0x41, 0x41 }, result);
        }

        [Fact]
        public void Decompress_TwoLiteralsThenBackref_RepeatsPair()
        {
            // literals 'A' 'B', then backref offset=2 length=4 -> "ABABAB"
            byte[] compressed = { 0xA0, 0xD0, 0x80, 0x26 };
            byte[] result = HeatshrinkCompressor.Decompress(compressed, 6);
            Assert.Equal(System.Text.Encoding.ASCII.GetBytes("ABABAB"), result);
        }

        [Fact]
        public void Decompress_TruncatedLiteral_ThrowsInvalidDataException()
        {
            // tag bit says literal, but only 7 of the 8 literal bits are present.
            byte[] compressed = { 0b1000_0000 };
            Assert.Throws<InvalidDataException>(() => HeatshrinkCompressor.Decompress(compressed, 4));
        }

        [Fact]
        public void Decompress_BackrefBeforeStreamStart_ReadsZeroPadding_DoesNotThrow()
        {
            // A backref referencing distance before the first output byte is valid
            // real heatshrink behavior (the window starts zero-initialized, and a
            // real encoder can legitimately reference that "virtual padding") — it
            // must not throw, and must not crash the way the original unbounded
            // `output[di - offset]` implementation did.
            // flag=0 (backref), offset bits=0 -> offset=1, length bits=0 -> length=1.
            byte[] compressed = { 0x00, 0x00 };
            byte[] result = HeatshrinkCompressor.Decompress(compressed, 1);
            Assert.Equal(new byte[] { 0x00 }, result);
        }

        [Fact]
        public void Decompress_NullOrEmptyInput_ReturnsZeroedBuffer()
        {
            Assert.Equal(new byte[5], HeatshrinkCompressor.Decompress(null!, 5));
            Assert.Equal(new byte[5], HeatshrinkCompressor.Decompress(Array.Empty<byte>(), 5));
        }

        // ── Round-trip via the app's own encoder ─────────────────────────────

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(300)]
        [InlineData(1024)]
        public void CompressThenDecompress_RoundTripsForVariedSizes(int size)
        {
            byte[] data = new byte[size];
            var rnd = new Random(42);
            rnd.NextBytes(data);

            byte[] compressed = HeatshrinkCompressor.Compress(data);
            byte[] decompressed = HeatshrinkCompressor.Decompress(compressed, size);

            Assert.Equal(data, decompressed);
        }

        [Fact]
        public void CompressThenDecompress_LongRepeatedRun_RoundTrips()
        {
            // Exercises backreferences that must be split across multiple tokens
            // once a run exceeds the 4-bit length field's encodable range (1..16).
            byte[] data = new byte[500];
            for (int i = 0; i < data.Length; i++) data[i] = 0x5A;

            byte[] compressed = HeatshrinkCompressor.Compress(data);
            byte[] decompressed = HeatshrinkCompressor.Decompress(compressed, data.Length);

            Assert.Equal(data, decompressed);
            Assert.True(compressed.Length < data.Length / 4, "Highly repetitive data should compress well");
        }

        // ── Real Flipper firmware-exported data ──────────────────────────────
        // These are genuine device-exported heatshrink streams (not produced by
        // this app's own Compress()), bundled at
        // FlipperZero_TestAssets\Kuronons_Misc_Earth_Arcadia_128x64. They are the
        // ground truth this decoder must match; the app's own encoder is never
        // involved here.

        private static byte[] ReadFramePayload(string fileName)
        {
            string bmPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..",
                "FlipperZero_TestAssets", "Kuronons_Misc_Earth_Arcadia_128x64", fileName));
            Assert.True(File.Exists(bmPath), $"Fixture not found at {bmPath}");

            byte[] buffer = File.ReadAllBytes(bmPath);
            Assert.Equal(0x01, buffer[0]); // is_compressed
            int compLen = BitConverter.ToUInt16(buffer, 2);
            byte[] compData = new byte[compLen];
            Array.Copy(buffer, 4, compData, 0, compLen);
            return compData;
        }

        [Theory]
        [InlineData("frame_1.bm")]
        [InlineData("frame_72.bm")]
        public void Decompress_RealFlipperCompressedFrame_ProducesFullNonDegenerateOutput(string fileName)
        {
            const int expectedBytes = 128 * 64 / 8; // 1024
            byte[] decoded = HeatshrinkCompressor.Decompress(ReadFramePayload(fileName), expectedBytes);

            Assert.Equal(expectedBytes, decoded.Length);
            Assert.Contains(decoded, b => b != 0x00); // not a degenerate all-zero decode
        }

        [Theory]
        // Expected SHA-256 of the correctly decoded 1024-byte frame, computed with
        // the reference `heatshrink2` Python package (window_sz2=8, lookahead_sz2=4)
        // against these exact bundled fixture files — bit-exact ground truth, not a
        // weaker "looks non-degenerate" check.
        [InlineData("frame_0.bm", "0f8059abe18fb7945f86c983ec0fd671a99f4d378a122dcfa8277a4b67663027")]
        [InlineData("frame_36.bm", "cd47bac3a1c28974fcfa0946e3b136df1763968ed556a50b4e98dd97c6ec603c")]
        public void Decompress_RealFlipperCompressedFrame_MatchesReferenceHeatshrinkExactly(string fileName, string expectedSha256)
        {
            const int expectedBytes = 128 * 64 / 8; // 1024
            byte[] decoded = HeatshrinkCompressor.Decompress(ReadFramePayload(fileName), expectedBytes);

            string actualSha256 = Convert.ToHexStringLower(SHA256.HashData(decoded));
            Assert.Equal(expectedSha256, actualSha256);
        }
    }
}
