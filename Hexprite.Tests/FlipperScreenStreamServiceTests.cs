using System;
using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FlipperScreenStreamServiceTests
    {
        [Fact]
        public void Encode1024Buffer_ThrowsOnNull()
        {
            Assert.Throws<ArgumentNullException>(() => FlipperScreenStreamService.Encode1024Buffer(null!));
        }

        [Fact]
        public void Encode1024Buffer_ReturnsExact1024Bytes()
        {
            bool[] pixels = new bool[128 * 64];
            byte[] buffer = FlipperScreenStreamService.Encode1024Buffer(pixels);

            Assert.NotNull(buffer);
            Assert.Equal(1024, buffer.Length);
            Assert.All(buffer, b => Assert.Equal(0, b));
        }

        [Fact]
        public void Encode1024Buffer_EncodesVertical8PixelPagesCorrectly()
        {
            bool[] pixels = new bool[128 * 64];

            // Set top-left pixel (x=0, y=0) -> Page 0, col 0, bit 0
            pixels[0 * 128 + 0] = true;

            // Set pixel (x=0, y=7) -> Page 0, col 0, bit 7
            pixels[7 * 128 + 0] = true;

            // Set pixel (x=1, y=8) -> Page 1, col 1, bit 0
            pixels[8 * 128 + 1] = true;

            // Set bottom-right pixel (x=127, y=63) -> Page 7, col 127, bit 7
            pixels[63 * 128 + 127] = true;

            byte[] buffer = FlipperScreenStreamService.Encode1024Buffer(pixels);

            // Page 0, col 0 = buffer[0] -> bit 0 and bit 7 set = 0x81 (129)
            Assert.Equal(0x81, buffer[0]);

            // Page 1, col 1 = buffer[1 * 128 + 1] = buffer[129] -> bit 0 set = 0x01 (1)
            Assert.Equal(0x01, buffer[129]);

            // Page 7, col 127 = buffer[7 * 128 + 127] = buffer[1023] -> bit 7 set = 0x80 (128)
            Assert.Equal(0x80, buffer[1023]);
        }

        [Fact]
        public void Encode1024Buffer_FullWhiteScreen_ReturnsAll0xFF()
        {
            bool[] pixels = new bool[128 * 64];
            Array.Fill(pixels, true);

            byte[] buffer = FlipperScreenStreamService.Encode1024Buffer(pixels);

            Assert.Equal(1024, buffer.Length);
            Assert.All(buffer, b => Assert.Equal(0xFF, b));
        }

        [Fact]
        public void StopStreaming_WhenNotStreaming_DoesNotThrow()
        {
            using var service = new FlipperScreenStreamService();
            var ex = Record.Exception(() => service.StopStreaming());
            Assert.Null(ex);
            Assert.False(service.IsStreaming);
        }

        [Fact]
        public void SendSingleFrame_WhenNotConnected_ReturnsFalse()
        {
            using var service = new FlipperScreenStreamService();
            bool result = service.SendSingleFrame(new bool[128 * 64]);
            Assert.False(result);
        }

        [Fact]
        public void Disconnect_WhenNotConnected_DoesNotThrow()
        {
            using var service = new FlipperScreenStreamService();
            var ex = Record.Exception(() => service.Disconnect());
            Assert.Null(ex);
            Assert.False(service.IsConnected);
        }

        [Fact]
        public void AutoReconnect_Property_DefaultsToTrueAndCanBeToggled()
        {
            using var service = new FlipperScreenStreamService();
            Assert.True(service.AutoReconnect);

            service.AutoReconnect = false;
            Assert.False(service.AutoReconnect);
        }

        [Fact]
        public void NormalizeTo128x64_InPlace_ExactSize_CopiesDirectly()
        {
            bool[] src = new bool[128 * 64];
            src[0] = true;
            src[128 * 64 - 1] = true;

            bool[] dst = new bool[128 * 64];
            FlipperScreenStreamService.NormalizeTo128x64(src, 128, 64, dst);

            Assert.True(dst[0]);
            Assert.True(dst[128 * 64 - 1]);
            Assert.False(dst[1]);
        }

        [Fact]
        public void NormalizeTo128x64_InPlace_SmallerDimension_CentersCorrectly()
        {
            // 32x32 src inside 128x64 -> offsetX = (128 - 32) / 2 = 48, offsetY = (64 - 32) / 2 = 16
            bool[] src = new bool[32 * 32];
            src[0] = true; // (0,0) in src

            bool[] dst = new bool[128 * 64];
            FlipperScreenStreamService.NormalizeTo128x64(src, 32, 32, dst);

            int expectedIndex = 16 * 128 + 48;
            Assert.True(dst[expectedIndex]);
            Assert.False(dst[0]);
        }

        [Fact]
        public void NormalizeTo128x64_InPlace_ThrowsOnInvalidParameters()
        {
            Assert.Throws<ArgumentNullException>(() => FlipperScreenStreamService.NormalizeTo128x64(null!, 128, 64, new bool[128 * 64]));
            Assert.Throws<ArgumentNullException>(() => FlipperScreenStreamService.NormalizeTo128x64(new bool[128 * 64], 128, 64, null!));
            Assert.Throws<ArgumentException>(() => FlipperScreenStreamService.NormalizeTo128x64(new bool[128 * 64], 128, 64, new bool[100]));
        }
    }
}
