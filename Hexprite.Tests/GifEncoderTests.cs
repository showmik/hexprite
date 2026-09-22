using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
public sealed class GifEncoderTests
{
    [Fact]
    public void Constructor_NullStream_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new GifEncoder(null!, 16, 16));
    }

    [Fact]
    public void Constructor_NonWritableStream_ThrowsArgumentException()
    {
        using var readOnlyStream = new MemoryStream([], writable: false);
        Assert.Throws<ArgumentException>(() => new GifEncoder(readOnlyStream, 16, 16));
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(16, 0)]
    [InlineData(0, 0)]
    public void Constructor_ZeroDimensions_ThrowsArgumentOutOfRangeException(ushort width, ushort height)
    {
        using var ms = new MemoryStream();
        Assert.Throws<ArgumentOutOfRangeException>(() => new GifEncoder(ms, width, height));
    }

    [Fact]
    public void Constructor_WritesValidHeaderAndScreenDescriptor()
    {
        using var ms = new MemoryStream();
        using var encoder = new GifEncoder(ms, 32, 48);

        byte[] data = ms.ToArray();
        Assert.True(data.Length >= 13);

        // Header: GIF89a
        string header = System.Text.Encoding.ASCII.GetString(data, 0, 6);
        Assert.Equal("GIF89a", header);

        // Screen width: 32 (0x0020)
        Assert.Equal(32, data[6] | (data[7] << 8));
        // Screen height: 48 (0x0030)
        Assert.Equal(48, data[8] | (data[9] << 8));
        // Packed fields: 0xF7 (GCT present, 8-bit, 256 colors)
        Assert.Equal(0xF7, data[10]);
        // Background color index: 0
        Assert.Equal(0, data[11]);
        // Pixel aspect ratio: 0
        Assert.Equal(0, data[12]);
    }

    [Fact]
    public void SetPalette_NullColors_ThrowsArgumentNullException()
    {
        using var ms = new MemoryStream();
        using var encoder = new GifEncoder(ms, 16, 16);
        Assert.Throws<ArgumentNullException>(() => encoder.SetPalette(null!));
    }

    [Fact]
    public void SetPalette_Writes256ColorTable()
    {
        using var ms = new MemoryStream();
        using var encoder = new GifEncoder(ms, 8, 8);

        encoder.SetPalette([Colors.Red, Colors.Green, Colors.Blue]);

        byte[] data = ms.ToArray();
        // 13 bytes header/LSD + 768 bytes GCT (256 * 3)
        Assert.Equal(13 + 768, data.Length);

        // Color 0: Red (R=255, G=0, B=0)
        Assert.Equal(255, data[13]);
        Assert.Equal(0, data[14]);
        Assert.Equal(0, data[15]);

        // Color 1: Green (R=0, G=128, B=0 in WPF Colors.Green, or check R, G, B)
        Assert.Equal(Colors.Green.R, data[16]);
        Assert.Equal(Colors.Green.G, data[17]);
        Assert.Equal(Colors.Green.B, data[18]);

        // Color 2: Blue (R=0, G=0, B=255)
        Assert.Equal(0, data[19]);
        Assert.Equal(0, data[20]);
        Assert.Equal(255, data[21]);

        // Padding to 256 colors should be 0s
        for (int i = 13 + 9; i < 13 + 768; i++)
        {
            Assert.Equal(0, data[i]);
        }
    }

    [Fact]
    public void SetLoop_WritesNetscape2ApplicationExtension()
    {
        using var ms = new MemoryStream();
        using var encoder = new GifEncoder(ms, 8, 8);
        encoder.SetPalette([Colors.Black, Colors.White]);
        encoder.SetLoop(3);

        byte[] data = ms.ToArray();
        int offset = 13 + 768;

        // Extension introducer 0x21, App extension 0xFF, Block size 11
        Assert.Equal(0x21, data[offset]);
        Assert.Equal(0xFF, data[offset + 1]);
        Assert.Equal(11, data[offset + 2]);

        string appIdent = System.Text.Encoding.ASCII.GetString(data, offset + 3, 11);
        Assert.Equal("NETSCAPE2.0", appIdent);

        // Sub-block size 3, ID 1, Loop count 3 (ushort LE)
        Assert.Equal(3, data[offset + 14]);
        Assert.Equal(1, data[offset + 15]);
        Assert.Equal(3, data[offset + 16] | (data[offset + 17] << 8));
        Assert.Equal(0, data[offset + 18]); // Block terminator
    }

    [Fact]
    public void AddFrame_NullPixels_ThrowsArgumentNullException()
    {
        using var ms = new MemoryStream();
        using var encoder = new GifEncoder(ms, 8, 8);
        encoder.SetPalette([Colors.Black, Colors.White]);

        Assert.Throws<ArgumentNullException>(() => encoder.AddFrame(null!, 10));
    }

    [Fact]
    public void AddFrame_MismatchedLength_ThrowsArgumentException()
    {
        using var ms = new MemoryStream();
        using var encoder = new GifEncoder(ms, 8, 8);
        encoder.SetPalette([Colors.Black, Colors.White]);

        Assert.Throws<ArgumentException>(() => encoder.AddFrame(new byte[63], 10));
        Assert.Throws<ArgumentException>(() => encoder.AddFrame(new byte[65], 10));
    }

    [Fact]
    public void AddFrame_ClampsZeroDelayToOneCentisecond()
    {
        using var ms = new MemoryStream();
        using var encoder = new GifEncoder(ms, 4, 4);
        encoder.SetPalette([Colors.Black, Colors.White]);

        encoder.AddFrame(new byte[16], delayCentiseconds: 0);

        byte[] data = ms.ToArray();
        int gceOffset = 13 + 768; // after header + GCT

        Assert.Equal(0x21, data[gceOffset]);
        Assert.Equal(0xF9, data[gceOffset + 1]); // GCE
        Assert.Equal(4, data[gceOffset + 2]);    // Block size

        // Delay time (bytes 4 and 5 of GCE, at gceOffset + 4)
        int delay = data[gceOffset + 4] | (data[gceOffset + 5] << 8);
        Assert.Equal(1, delay); // Clamped from 0 to 1 centisecond
    }

    [Fact]
    public void Finish_WritesTrailerAndFlushes()
    {
        using var ms = new MemoryStream();
        var encoder = new GifEncoder(ms, 4, 4);
        encoder.SetPalette([Colors.Black, Colors.White]);
        encoder.AddFrame(new byte[16], 10);
        encoder.Finish();

        byte[] data = ms.ToArray();
        Assert.Equal(0x3B, data[^1]); // GIF trailer
    }

    [Fact]
    public void Finish_IsIdempotent()
    {
        using var ms = new MemoryStream();
        var encoder = new GifEncoder(ms, 4, 4);
        encoder.SetPalette([Colors.Black, Colors.White]);
        encoder.AddFrame(new byte[16], 10);
        encoder.Finish();
        long len1 = ms.Length;

        encoder.Finish();
        long len2 = ms.Length;

        Assert.Equal(len1, len2);
    }

    [Fact]
    public void Dispose_AutomaticallyFinishesGif()
    {
        byte[] data;
        using (var ms = new MemoryStream())
        {
            using (var encoder = new GifEncoder(ms, 4, 4))
            {
                encoder.SetPalette([Colors.Black, Colors.White]);
                encoder.AddFrame(new byte[16], 10);
            }
            data = ms.ToArray();
        }

        Assert.True(data.Length > 0);
        Assert.Equal(0x3B, data[^1]);
    }

    [Fact]
    public void MultiFrameAnimation_RoundTripsThroughBitmapDecoder()
    {
        byte[] gifBytes;
        const ushort width = 16;
        const ushort height = 16;
        const int frameCount = 3;

        using (var ms = new MemoryStream())
        {
            using (var encoder = new GifEncoder(ms, width, height))
            {
                encoder.SetPalette([Colors.Black, Colors.White, Colors.Red]);
                encoder.SetLoop(0);

                for (int f = 0; f < frameCount; f++)
                {
                    byte[] framePixels = new byte[width * height];
                    byte colorIdx = (byte)(f % 3);
                    Array.Fill(framePixels, colorIdx);
                    encoder.AddFrame(framePixels, 15);
                }
            }
            gifBytes = ms.ToArray();
        }

        using var readStream = new MemoryStream(gifBytes);
        var decoder = BitmapDecoder.Create(readStream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);

        Assert.Equal(frameCount, decoder.Frames.Count);
        for (int i = 0; i < frameCount; i++)
        {
            Assert.Equal(width, decoder.Frames[i].PixelWidth);
            Assert.Equal(height, decoder.Frames[i].PixelHeight);
        }
    }

    [Fact]
    public void AddFrame_SubRectangle_WritesCorrectImageDescriptorOffsetsAndDimensions()
    {
        using var ms = new MemoryStream();
        using var encoder = new GifEncoder(ms, 32, 32);
        encoder.SetPalette([Colors.Black, Colors.White]);

        // Sub-region 8x8 at offset (4, 6)
        byte[] subPixels = new byte[8 * 8];
        encoder.AddFrame(subPixels, 10, left: 4, top: 6, frameWidth: 8, frameHeight: 8, disposalMethod: 1);

        byte[] data = ms.ToArray();
        int gceOffset = 13 + 768;
        int imgDescOffset = gceOffset + 8; // GCE is 8 bytes

        Assert.Equal(0x2C, data[imgDescOffset]); // Image separator
        // Left: 4 (ushort LE)
        Assert.Equal(4, data[imgDescOffset + 1] | (data[imgDescOffset + 2] << 8));
        // Top: 6 (ushort LE)
        Assert.Equal(6, data[imgDescOffset + 3] | (data[imgDescOffset + 4] << 8));
        // Width: 8 (ushort LE)
        Assert.Equal(8, data[imgDescOffset + 5] | (data[imgDescOffset + 6] << 8));
        // Height: 8 (ushort LE)
        Assert.Equal(8, data[imgDescOffset + 7] | (data[imgDescOffset + 8] << 8));
        // No LCT (packed byte 0x00)
        Assert.Equal(0x00, data[imgDescOffset + 9]);
    }

    [Fact]
    public void AddFrame_LocalColorTable_WritesLocalColorTableBlock()
    {
        using var ms = new MemoryStream();
        using var encoder = new GifEncoder(ms, 16, 16);
        encoder.SetPalette([Colors.Black, Colors.White]);

        // Frame with custom local palette
        Color[] localPalette = [Colors.Red, Colors.Yellow, Colors.Cyan];
        byte[] pixels = new byte[16 * 16];
        encoder.AddFrame(pixels, 10, localPalette: localPalette);

        byte[] data = ms.ToArray();
        int imgDescOffset = 13 + 768 + 8;

        // Packed byte at imgDescOffset + 9 should have bit 7 set (0x87 for 256 color LCT)
        Assert.Equal(0x87, data[imgDescOffset + 9]);

        // Next 768 bytes should be LCT colors (Color 0 is Red: 255, 0, 0)
        int lctOffset = imgDescOffset + 10;
        Assert.Equal(255, data[lctOffset]);
        Assert.Equal(0, data[lctOffset + 1]);
        Assert.Equal(0, data[lctOffset + 2]);
    }

    [Fact]
    public void SetLoop_CalledBeforeSetPalette_WritesGlobalColorTableImmediatelyAfterScreenDescriptor()
    {
        using var ms = new MemoryStream();
        using (var encoder = new GifEncoder(ms, 8, 8))
        {
            // Call SetLoop before SetPalette
            encoder.SetLoop(0);
            encoder.SetPalette([Colors.Red, Colors.Green, Colors.Blue]);
            encoder.AddFrame(new byte[64], 10);
        }

        byte[] data = ms.ToArray();
        // Offset 13 to 13+767 MUST be the GCT
        // Color 0: Red (255, 0, 0)
        Assert.Equal(255, data[13]);
        Assert.Equal(0, data[14]);
        Assert.Equal(0, data[15]);

        // Color 1: Green
        Assert.Equal(Colors.Green.R, data[16]);
        Assert.Equal(Colors.Green.G, data[17]);
        Assert.Equal(Colors.Green.B, data[18]);

        // The Netscape application extension (0x21, 0xFF, 0x0B, "NETSCAPE2.0") must appear AFTER the GCT (offset 13 + 768 = 781)
        int netscapeOffset = 13 + 768;
        Assert.Equal(0x21, data[netscapeOffset]);
        Assert.Equal(0xFF, data[netscapeOffset + 1]);
        Assert.Equal(11, data[netscapeOffset + 2]);
        string appIdent = System.Text.Encoding.ASCII.GetString(data, netscapeOffset + 3, 11);
        Assert.Equal("NETSCAPE2.0", appIdent);
    }
}

