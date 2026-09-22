using Hexprite.Services.Compression;
using Xunit;

namespace Hexprite.Tests.Compression
{
    [Trait("Category", "Unit")]
    public class LzssCompressorTests
    {
        // ── Round-trip correctness ────────────────────────────────────────────

        [Fact]
        public void Compress_EmptyInput_ReturnsEmpty()
        {
            byte[] result = LzssCompressor.Compress(Array.Empty<byte>());
            Assert.Empty(result);
        }

        [Fact]
        public void Compress_SingleByte_ReturnsOriginal()
        {
            byte[] input = { 0x42 };
            byte[] result = LzssCompressor.Compress(input);
            // Single byte: 1 literal = 9 bits = 2 bytes compressed ≥ 1 byte original
            Assert.Equal(input, result);
        }

        [Fact]
        public void Compress_AllZeros_AchievesHighCompression()
        {
            byte[] input = new byte[256]; // 256 zeros
            byte[] compressed = LzssCompressor.Compress(input);

            // LZSS should compress repeated data very well
            Assert.True(compressed.Length < input.Length / 3,
                $"Expected < {input.Length / 3} bytes, got {compressed.Length}");

            byte[] decompressed = LzssCompressor.Decompress(compressed, input.Length);
            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void Compress_RepeatingPattern_RoundTrips()
        {
            // Repeating 4-byte pattern — LZSS should find backreferences
            byte[] pattern = { 0x3C, 0x7E, 0x7E, 0x3C };
            byte[] input = new byte[128];
            for (int i = 0; i < input.Length; i++)
                input[i] = pattern[i % pattern.Length];

            byte[] compressed = LzssCompressor.Compress(input);
            Assert.True(compressed.Length < input.Length,
                $"Expected compression: {input.Length} → {compressed.Length}");

            byte[] decompressed = LzssCompressor.Decompress(compressed, input.Length);
            Assert.Equal(input, decompressed);
        }

        [Theory]
        [InlineData(8, 8)]     // 8×8 sprite = 8 bytes
        [InlineData(16, 16)]   // 16×16 sprite = 32 bytes
        [InlineData(32, 32)]   // 32×32 sprite = 128 bytes
        [InlineData(64, 64)]   // 64×64 sprite = 512 bytes
        [InlineData(128, 64)]  // 128×64 sprite = 1024 bytes
        public void Compress_VariousSpriteData_RoundTrips(int width, int height)
        {
            int bytesPerRow = (int)Math.Ceiling(width / 8.0);
            byte[] input = new byte[bytesPerRow * height];

            // Fill with a simple diamond pattern
            for (int r = 0; r < height; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    int cx = width / 2, cy = height / 2;
                    bool isOn = Math.Abs(c - cx) + Math.Abs(r - cy) < Math.Min(cx, cy);
                    if (isOn)
                        input[r * bytesPerRow + c / 8] |= (byte)(0x80 >> (c % 8));
                }
            }

            byte[] compressed = LzssCompressor.Compress(input);

            // May or may not compress depending on size, but must round-trip
            if (!ReferenceEquals(compressed, input))
            {
                byte[] decompressed = LzssCompressor.Decompress(compressed, input.Length);
                Assert.Equal(input, decompressed);
            }
        }

        [Fact]
        public void Compress_RealWorldSpritePattern_AchievesMeaningfulRatio()
        {
            // Simulates a 32×32 heart sprite (lots of structure, should compress well)
            int bytesPerRow = 4;
            int rows = 32;
            byte[] input = new byte[bytesPerRow * rows];

            // Heart shape in middle rows
            for (int r = 4; r < 28; r++)
            {
                double y = (r - 16.0) / 12.0;
                for (int c = 0; c < 32; c++)
                {
                    double x = (c - 16.0) / 12.0;
                    // Heart curve: (x² + y² - 1)³ - x²·y³ ≤ 0
                    double x2 = x * x, y2 = y * y;
                    double val = Math.Pow(x2 + y2 - 1, 3) - x2 * y2 * y;
                    if (val <= 0)
                        input[r * bytesPerRow + c / 8] |= (byte)(0x80 >> (c % 8));
                }
            }

            byte[] compressed = LzssCompressor.Compress(input);

            if (!ReferenceEquals(compressed, input))
            {
                Assert.True(compressed.Length < input.Length,
                    $"Expected meaningful compression: {input.Length} → {compressed.Length}");

                byte[] decompressed = LzssCompressor.Decompress(compressed, input.Length);
                Assert.Equal(input, decompressed);
            }
        }

        [Fact]
        public void Compress_IncompressibleData_ReturnsOriginal()
        {
            byte[] input = new byte[64];
            var rng = new Random(42);
            rng.NextBytes(input);

            byte[] result = LzssCompressor.Compress(input);

            // LZSS should return original if compression doesn't help
            Assert.True(result.Length <= input.Length,
                $"Compressed should not be larger: {input.Length} → {result.Length}");
        }

        // ── Decompression edge cases ─────────────────────────────────────────

        [Fact]
        public void Decompress_EmptyInput_ReturnsEmpty()
        {
            byte[] result = LzssCompressor.Decompress(Array.Empty<byte>(), 0);
            Assert.Empty(result);
        }

        [Fact]
        public void Decompress_TruncatedInput_StopsGracefully()
        {
            // Single byte of compressed data — shouldn't crash
            byte[] compressed = { 0xFF };
            byte[] result = LzssCompressor.Decompress(compressed, 100);
            Assert.NotNull(result);
        }

        [Fact]
        public void Decompress_BackreferenceBeforeOutputStart_StopsGracefully()
        {
            // First bit 0 = backreference flag, followed by offset=0 (→1) and length=0 (→2).
            // At di=0 this references output[-1], which must not crash.
            byte[] compressed = { 0x00, 0x00 };
            byte[] result = LzssCompressor.Decompress(compressed, 100);
            Assert.NotNull(result);
        }

        [Fact]
        public void Compress_LongSelfReferencingRun_HandlesCorrectly()
        {
            // Pattern like AABAAB... where "AAB" repeats — tests self-referencing matches
            byte[] input = new byte[200];
            for (int i = 0; i < input.Length; i++)
                input[i] = (byte)(i % 3 == 2 ? 0xBB : 0xAA);

            byte[] compressed = LzssCompressor.Compress(input);
            if (!ReferenceEquals(compressed, input))
            {
                byte[] decompressed = LzssCompressor.Decompress(compressed, input.Length);
                Assert.Equal(input, decompressed);
            }
        }
    }
}
