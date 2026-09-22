using Hexprite.Services.Compression;
using Xunit;

namespace Hexprite.Tests.Compression
{
    [Trait("Category", "Unit")]
    public class RleCompressorTests
    {
        // ── Round-trip correctness ────────────────────────────────────────────

        [Fact]
        public void Compress_EmptyInput_ReturnsEmpty()
        {
            byte[] result = RleCompressor.Compress(Array.Empty<byte>());
            Assert.Empty(result);
        }

        [Fact]
        public void Compress_SingleByte_ReturnsVerbatim()
        {
            byte[] input = { 0x42 };
            byte[] result = RleCompressor.Compress(input);
            // Single byte can't be compressed smaller; returns original
            Assert.Equal(input, result);
        }

        [Fact]
        public void Compress_AllZeros_ProducesCompactOutput()
        {
            byte[] input = new byte[128]; // 128 zeros
            byte[] compressed = RleCompressor.Compress(input);

            // Should be MUCH smaller: escape(1) + [esc,128,0x00](3) = ~4 bytes
            Assert.True(compressed.Length < input.Length / 10,
                $"Expected < {input.Length / 10} bytes, got {compressed.Length}");

            // Round-trip
            byte[] decompressed = RleCompressor.Decompress(compressed, input.Length);
            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void Compress_AllOnes_ProducesCompactOutput()
        {
            byte[] input = Enumerable.Repeat((byte)0xFF, 128).ToArray();
            byte[] compressed = RleCompressor.Compress(input);

            Assert.True(compressed.Length < input.Length / 10,
                $"Expected < {input.Length / 10} bytes, got {compressed.Length}");

            byte[] decompressed = RleCompressor.Decompress(compressed, input.Length);
            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void Compress_AlternatingBytes_RoundTrips()
        {
            // No runs — compression shouldn't help, but must round-trip
            byte[] input = new byte[64];
            for (int i = 0; i < input.Length; i++)
                input[i] = (byte)(i % 2 == 0 ? 0xAA : 0x55);

            byte[] compressed = RleCompressor.Compress(input);
            if (!ReferenceEquals(compressed, input))
            {
                byte[] decompressed = RleCompressor.Decompress(compressed, input.Length);
                Assert.Equal(input, decompressed);
            }
        }

        [Fact]
        public void Compress_EscapeByteInData_HandledCorrectly()
        {
            // Create data where every byte value appears at least once
            // The escape byte will be whichever is least frequent
            byte[] input = new byte[512];
            for (int i = 0; i < 256; i++)
                input[i] = (byte)i;
            // Add runs of 0xFF to make compression worthwhile
            for (int i = 256; i < 512; i++)
                input[i] = 0xFF;

            byte[] compressed = RleCompressor.Compress(input);

            // Must round-trip even when escape byte appears in the data
            if (!ReferenceEquals(compressed, input))
            {
                byte[] decompressed = RleCompressor.Decompress(compressed, input.Length);
                Assert.Equal(input, decompressed);
            }
        }

        [Fact]
        public void Compress_MaxRunLength_SplitsCorrectly()
        {
            // 500 identical bytes — must split into runs of ≤ 255
            byte[] input = Enumerable.Repeat((byte)0x42, 500).ToArray();
            byte[] compressed = RleCompressor.Compress(input);

            Assert.True(compressed.Length < input.Length);

            byte[] decompressed = RleCompressor.Decompress(compressed, input.Length);
            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void Compress_TypicalSpriteData_RoundTrips()
        {
            // Simulates a 32×32 sprite with some filled rows and some empty rows
            int bytesPerRow = 4; // 32 pixels / 8
            int rows = 32;
            byte[] input = new byte[bytesPerRow * rows];

            // First 8 rows: all black (0x00)
            // Next 16 rows: some pattern
            for (int r = 8; r < 24; r++)
            {
                input[r * bytesPerRow + 0] = 0x3C;
                input[r * bytesPerRow + 1] = 0x7E;
                input[r * bytesPerRow + 2] = 0x7E;
                input[r * bytesPerRow + 3] = 0x3C;
            }
            // Last 8 rows: all black (0x00)

            byte[] compressed = RleCompressor.Compress(input);
            Assert.True(compressed.Length < input.Length,
                $"Expected compression to reduce size: {input.Length} → {compressed.Length}");

            byte[] decompressed = RleCompressor.Decompress(compressed, input.Length);
            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void Compress_IncompressibleData_ReturnsOriginal()
        {
            // Random-looking data that doesn't compress well
            byte[] input = new byte[32];
            var rng = new Random(42);
            rng.NextBytes(input);

            byte[] result = RleCompressor.Compress(input);

            // Should return the original if compression didn't help
            Assert.True(result.Length <= input.Length,
                $"Compressed should not be larger: {input.Length} → {result.Length}");
        }

        // ── Decompression edge cases ─────────────────────────────────────────

        [Fact]
        public void Decompress_EmptyInput_ReturnsEmpty()
        {
            byte[] result = RleCompressor.Decompress(Array.Empty<byte>(), 0);
            Assert.Empty(result);
        }

        [Fact]
        public void Decompress_TruncatedInput_StopsGracefully()
        {
            // Compressed data that's truncated mid-run
            byte[] compressed = { 0x00, 0x00, 10 }; // escape=0x00, run of 10×0x00, but only 3 bytes total
            // Shouldn't crash
            byte[] result = RleCompressor.Decompress(compressed, 100);
            Assert.NotNull(result);
        }
    }
}
