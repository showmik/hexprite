using System;
using System.IO;
using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class GifAnimationExportServiceTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly GifAnimationExportService _service;

        public GifAnimationExportServiceTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "HexpriteGifTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
            _service = new GifAnimationExportService();
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        [Fact]
        public void ExportGif_ProducesValidGifFileWithNetscapeLoopAndFrames()
        {
            var sprite = new SpriteState(128, 64);
            sprite.Frames.Clear();

            // 2 frames
            var frame0 = new FrameState { Name = "Frame 1" };
            bool[] p0 = new bool[128 * 64];
            p0[0] = true;
            frame0.LayerPixels.Add(new MonochromePixelBuffer(p0));
            sprite.Frames.Add(frame0);

            var frame1 = new FrameState { Name = "Frame 2" };
            bool[] p1 = new bool[128 * 64];
            p1[128 * 64 - 1] = true;
            frame1.LayerPixels.Add(new MonochromePixelBuffer(p1));
            sprite.Frames.Add(frame1);

            string gifPath = Path.Combine(_tempDirectory, "test_anim.gif");
            _service.ExportGif(sprite, gifPath, scale: 2, fps: 5);

            Assert.True(File.Exists(gifPath));
            byte[] bytes = File.ReadAllBytes(gifPath);

            // GIF89a header
            Assert.True(bytes.Length > 32);
            Assert.Equal((byte)'G', bytes[0]);
            Assert.Equal((byte)'I', bytes[1]);
            Assert.Equal((byte)'F', bytes[2]);
            Assert.Equal((byte)'8', bytes[3]);
            Assert.Equal((byte)'9', bytes[4]);
            Assert.Equal((byte)'a', bytes[5]);

            // Width = 128 * 2 = 256
            ushort width = (ushort)(bytes[6] | (bytes[7] << 8));
            // Height = 64 * 2 = 128
            ushort height = (ushort)(bytes[8] | (bytes[9] << 8));

            Assert.Equal(256, width);
            Assert.Equal(128, height);

            // Trailer
            Assert.Equal(0x3B, bytes[^1]);
        }

        [Fact]
        public void ExportGif_WithSpeechBubbleAndHud_GeneratesValidFile()
        {
            var sprite = new SpriteState(128, 64);
            sprite.Frames.Clear();
            var frame0 = new FrameState { Name = "Frame 1" };
            frame0.LayerPixels.Add(new MonochromePixelBuffer(new bool[128 * 64]));
            sprite.Frames.Add(frame0);

            var bubble = new FlipperSpeechBubble(1, 10, 4, "Hello Flipper!", SpeechBubbleTailPosition.BottomLeft);
            string gifPath = Path.Combine(_tempDirectory, "bubble_hud.gif");

            _service.ExportGif(
                sprite,
                gifPath,
                scale: 1,
                fps: 10,
                includeSpeechBubble: true,
                bubble: bubble,
                includeHud: true,
                hudLevel: 5,
                hudMood: 2);

            Assert.True(File.Exists(gifPath));
            byte[] bytes = File.ReadAllBytes(gifPath);
            Assert.True(bytes.Length > 100);

            ushort width = (ushort)(bytes[6] | (bytes[7] << 8));
            ushort height = (ushort)(bytes[8] | (bytes[9] << 8));
            Assert.Equal(128, width);
            Assert.Equal(64, height);
        }

        [Fact]
        public void ExportGif_MultiLayerSprite_CompositesAllVisibleLayers()
        {
            var sprite = new SpriteState(128, 64);
            sprite.Frames.Clear();

            var frame = new FrameState { Name = "Frame 1" };
            // Layer 0: Background
            bool[] l0 = new bool[128 * 64];
            l0[0] = true;
            frame.LayerPixels.Add(new MonochromePixelBuffer(l0));
            // Layer 1: Foreground
            bool[] l1 = new bool[128 * 64];
            l1[1] = true;
            frame.LayerPixels.Add(new MonochromePixelBuffer(l1));

            sprite.Frames.Add(frame);
            sprite.Layers.Clear();
            sprite.Layers.Add(new LayerState { Name = "Bg", IsVisible = true });
            sprite.Layers.Add(new LayerState { Name = "Fg", IsVisible = true });

            string gifPath = Path.Combine(_tempDirectory, "multilayer.gif");
            _service.ExportGif(sprite, gifPath, scale: 1, fps: 5);

            Assert.True(File.Exists(gifPath));
            byte[] bytes = File.ReadAllBytes(gifPath);
            Assert.True(bytes.Length > 0);
        }

        [Fact]
        public void ExportGif_LargeAnimationWithComplexPixels_DecodesCorrectly()
        {
            // Create a sprite with 4 frames of 128x64 with varying pixel patterns
            var sprite = new SpriteState(128, 64);
            sprite.Frames.Clear();

            var rand = new Random(42);
            for (int f = 0; f < 4; f++)
            {
                var frame = new FrameState { Name = $"Frame {f + 1}" };
                bool[] pixels = new bool[128 * 64];
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = rand.Next(2) == 1;
                }
                frame.LayerPixels.Add(new MonochromePixelBuffer(pixels));
                sprite.Frames.Add(frame);
            }

            string gifPath = Path.Combine(_tempDirectory, "complex_anim.gif");
            _service.ExportGif(sprite, gifPath, scale: 1, fps: 10);

            Assert.True(File.Exists(gifPath));

            // Verify it decodes cleanly through WPF BitmapDecoder
            using var fs = File.OpenRead(gifPath);
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                fs,
                System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);

            Assert.Equal(4, decoder.Frames.Count);
            foreach (var frame in decoder.Frames)
            {
                Assert.Equal(128, frame.PixelWidth);
                Assert.Equal(64, frame.PixelHeight);
            }
        }
    }
}