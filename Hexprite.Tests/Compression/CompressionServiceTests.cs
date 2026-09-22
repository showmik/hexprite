using Hexprite.Core;
using Hexprite.Services.Compression;
using Xunit;

namespace Hexprite.Tests.Compression
{
    [Trait("Category", "Unit")]
    public class CompressionServiceTests
    {
        private readonly CompressionService _service = new();

        [Fact]
        public void Compress_None_ReturnsOriginalData()
        {
            byte[] input = [1, 2, 3, 4, 5];
            var result = _service.Compress(input, CompressionMode.None);
            Assert.Equal(input, result);
        }

        [Fact]
        public void CompressAndDecompress_Rle_RoundTrips()
        {
            byte[] input = [0, 0, 0, 0, 1, 1, 1, 2, 3, 4, 4, 4, 4];
            var compressed = _service.Compress(input, CompressionMode.Rle);
            var decompressed = _service.Decompress(compressed, input.Length, CompressionMode.Rle);
            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void CompressAndDecompress_Lzss_RoundTrips()
        {
            byte[] input = [10, 20, 30, 10, 20, 30, 10, 20, 30, 40, 50];
            var compressed = _service.Compress(input, CompressionMode.Lzss);
            var decompressed = _service.Decompress(compressed, input.Length, CompressionMode.Lzss);
            Assert.Equal(input, decompressed);
        }

        [Fact]
        public void Decompress_None_ReturnsSameData()
        {
            byte[] input = [1, 2, 3];
            var result = _service.Decompress(input, input.Length, CompressionMode.None);
            Assert.Equal(input, result);
        }

        [Theory]
        [InlineData(CompressionMode.Rle, "hexprite_rle_decode", 120)]
        [InlineData(CompressionMode.Lzss, "hexprite_lzss_decode", 200)]
        [InlineData(CompressionMode.None, "", 0)]
        public void DecompressorCodeAndFlashCost_ReturnsExpected(CompressionMode mode, string expectedSnippet, int expectedCost)
        {
            var code = _service.GenerateDecompressorCode(mode);
            int cost = _service.GetDecompressorFlashCost(mode);

            if (!string.IsNullOrEmpty(expectedSnippet))
                Assert.Contains(expectedSnippet, code);
            else
                Assert.Empty(code);

            Assert.Equal(expectedCost, cost);
        }
    }
}
