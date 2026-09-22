using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;
using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class ExportServiceTests
    {
        private static SpriteState CreateSampleSprite(int width = 8, int height = 8, int frameCount = 2)
        {
            var state = new SpriteState(width, height);
            while (state.Frames.Count < frameCount)
            {
                state.Frames.Add(new FrameState
                {
                    Name = $"Frame {state.Frames.Count + 1}",
                    LayerPixels = new List<IPixelBuffer> { new MonochromePixelBuffer(width * height) }
                });
            }

            // Put a pixel in frame 0 and frame 1
            state.Frames[0].LayerPixels[0].GetMonochromeData()[0] = true;
            if (frameCount > 1)
            {
                state.Frames[1].LayerPixels[0].GetMonochromeData()[1] = true;
            }

            return state;
        }

        [Fact]
        public void Export_SinglePng_CreatesValidFile()
        {
            var svc = new ExportService();
            var state = CreateSampleSprite(8, 8, 1);
            var tempPath = Path.Combine(Path.GetTempPath(), $"export_test_{Guid.NewGuid():N}.png");

            try
            {
                var settings = new ImageExportSettings
                {
                    Format = ImageExportFormat.Png,
                    Scale = 2,
                    ShowGrid = true
                };

                svc.Export(tempPath, state, settings);

                Assert.True(File.Exists(tempPath));
                Assert.True(new FileInfo(tempPath).Length > 0);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void Export_SingleBmp_CreatesValidFile()
        {
            var svc = new ExportService();
            var state = CreateSampleSprite(8, 8, 1);
            var tempPath = Path.Combine(Path.GetTempPath(), $"export_test_{Guid.NewGuid():N}.bmp");

            try
            {
                var settings = new ImageExportSettings
                {
                    Format = ImageExportFormat.Bmp,
                    Scale = 1,
                    ShowGrid = false
                };

                svc.Export(tempPath, state, settings);

                Assert.True(File.Exists(tempPath));
                Assert.True(new FileInfo(tempPath).Length > 0);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void Export_Spritesheet_CreatesSingleImageContainingAllFrames()
        {
            var svc = new ExportService();
            var state = CreateSampleSprite(8, 8, 3);
            var tempPath = Path.Combine(Path.GetTempPath(), $"spritesheet_test_{Guid.NewGuid():N}.png");

            try
            {
                var settings = new ImageExportSettings
                {
                    Format = ImageExportFormat.Png,
                    ExportAllFramesAsSpritesheet = true,
                    Scale = 1
                };

                svc.Export(tempPath, state, settings);

                Assert.True(File.Exists(tempPath));
                Assert.True(new FileInfo(tempPath).Length > 0);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void Export_PngSequence_CreatesNumberedFiles()
        {
            var svc = new ExportService();
            var state = CreateSampleSprite(8, 8, 2);
            var dir = Path.Combine(Path.GetTempPath(), $"seq_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            var basePath = Path.Combine(dir, "anim.png");

            try
            {
                var settings = new ImageExportSettings
                {
                    Format = ImageExportFormat.PngSequence,
                    Scale = 1
                };

                svc.Export(basePath, state, settings);

                string frame1 = Path.Combine(dir, "anim_001.png");
                string frame2 = Path.Combine(dir, "anim_002.png");

                Assert.True(File.Exists(frame1));
                Assert.True(File.Exists(frame2));
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    try { Directory.Delete(dir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Export_AnimatedGif_CreatesValidGif()
        {
            var svc = new ExportService();
            var state = CreateSampleSprite(8, 8, 2);
            var tempPath = Path.Combine(Path.GetTempPath(), $"anim_test_{Guid.NewGuid():N}.gif");

            try
            {
                var settings = new ImageExportSettings
                {
                    Format = ImageExportFormat.Gif,
                    Scale = 2,
                    GifFps = 10
                };

                svc.Export(tempPath, state, settings);

                Assert.True(File.Exists(tempPath));
                Assert.True(new FileInfo(tempPath).Length > 0);

                using var fs = File.OpenRead(tempPath);
                var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                Assert.Equal(2, decoder.Frames.Count);
                Assert.Equal(16, decoder.Frames[0].PixelWidth);
                Assert.Equal(16, decoder.Frames[0].PixelHeight);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void Export_AnimatedGif_WithGridOverlay_CreatesValidGif()
        {
            var svc = new ExportService();
            var state = CreateSampleSprite(8, 8, 2);
            var tempPath = Path.Combine(Path.GetTempPath(), $"grid_gif_test_{Guid.NewGuid():N}.gif");

            try
            {
                var settings = new ImageExportSettings
                {
                    Format = ImageExportFormat.Gif,
                    Scale = 4,
                    ShowGrid = true,
                    GifFps = 12
                };

                svc.Export(tempPath, state, settings);

                Assert.True(File.Exists(tempPath));
                using var fs = File.OpenRead(tempPath);
                var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                Assert.Equal(2, decoder.Frames.Count);
                Assert.Equal(32, decoder.Frames[0].PixelWidth);
                Assert.Equal(32, decoder.Frames[0].PixelHeight);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Theory]
        [InlineData(PlaybackDirection.Forward, 3)]
        [InlineData(PlaybackDirection.Reverse, 3)]
        [InlineData(PlaybackDirection.PingPong, 4)] // 0, 1, 2, 1
        public void Export_AnimatedGif_PlaybackDirections_CreatesExpectedFrameCount(PlaybackDirection direction, int expectedFrames)
        {
            var svc = new ExportService();
            var state = CreateSampleSprite(4, 4, 3);
            state.PlaybackDirection = direction;
            var tempPath = Path.Combine(Path.GetTempPath(), $"dir_gif_test_{Guid.NewGuid():N}.gif");

            try
            {
                var settings = new ImageExportSettings
                {
                    Format = ImageExportFormat.Gif,
                    Scale = 1,
                    GifExportAllFrames = true
                };

                svc.Export(tempPath, state, settings);

                Assert.True(File.Exists(tempPath));
                using var fs = File.OpenRead(tempPath);
                var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                Assert.Equal(expectedFrames, decoder.Frames.Count);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void Export_AnimatedGif_SingleFrame_OnlyExportsActiveFrame()
        {
            var svc = new ExportService();
            var state = CreateSampleSprite(8, 8, 3);
            state.ActiveFrameIndex = 1;
            var tempPath = Path.Combine(Path.GetTempPath(), $"single_gif_test_{Guid.NewGuid():N}.gif");

            try
            {
                var settings = new ImageExportSettings
                {
                    Format = ImageExportFormat.Gif,
                    Scale = 2,
                    GifExportAllFrames = false
                };

                svc.Export(tempPath, state, settings);

                Assert.True(File.Exists(tempPath));
                using var fs = File.OpenRead(tempPath);
                var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                Assert.Single(decoder.Frames);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void Export_AnimatedGif_DisplayPreviewMode_CreatesValidGif()
        {
            var svc = new ExportService();
            var state = CreateSampleSprite(8, 8, 2);
            var tempPath = Path.Combine(Path.GetTempPath(), $"display_gif_test_{Guid.NewGuid():N}.gif");

            try
            {
                var settings = new ImageExportSettings
                {
                    Format = ImageExportFormat.Gif,
                    ColorMode = ExportColorMode.DisplayPreview,
                    Scale = 2
                };

                svc.Export(tempPath, state, settings);

                Assert.True(File.Exists(tempPath));
                using var fs = File.OpenRead(tempPath);
                var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                Assert.Equal(2, decoder.Frames.Count);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void ComputeDirtyBounds_IdentifiesChangedRegion()
        {
            byte[] prev = new byte[16 * 16];
            byte[] curr = new byte[16 * 16];

            // Change a 4x3 block from (2, 5) to (5, 7)
            for (int y = 5; y <= 7; y++)
            {
                for (int x = 2; x <= 5; x++)
                {
                    curr[y * 16 + x] = 1;
                }
            }

            var bounds = ExportService.ComputeDirtyBounds(prev, curr, 16, 16);
            Assert.NotNull(bounds);
            Assert.Equal(2, bounds.Value.minX);
            Assert.Equal(5, bounds.Value.minY);
            Assert.Equal(4, bounds.Value.width);
            Assert.Equal(3, bounds.Value.height);
        }

        [Fact]
        public void ComputeDirtyBounds_IdenticalBuffers_ReturnsNull()
        {
            byte[] prev = new byte[8 * 8];
            byte[] curr = new byte[8 * 8];
            Assert.Null(ExportService.ComputeDirtyBounds(prev, curr, 8, 8));
        }

        [Fact]
        public void ExtractSubRegion_ExtractsAccuratePixelMatrix()
        {
            byte[] source = [
                0, 1, 2, 3,
                4, 5, 6, 7,
                8, 9, 10, 11,
                12, 13, 14, 15
            ];

            // Sub-region 2x2 at (1, 1) -> rows (1, 2), cols (1, 2) -> [5, 6, 9, 10]
            byte[] sub = ExportService.ExtractSubRegion(source, srcW: 4, minX: 1, minY: 1, subW: 2, subH: 2);
            Assert.Equal([5, 6, 9, 10], sub);
        }

        [Fact]
        public void Export_AnimatedGif_WithDeltaOptimization_CreatesValidGif()
        {
            var svc = new ExportService();
            var state = CreateSampleSprite(16, 16, 4);
            var tempPath = Path.Combine(Path.GetTempPath(), $"delta_gif_test_{Guid.NewGuid():N}.gif");

            try
            {
                var settings = new ImageExportSettings
                {
                    Format = ImageExportFormat.Gif,
                    Scale = 2,
                    GifTransparentBackground = false,
                    GifEnableDeltaOptimization = true,
                    GifFps = 10
                };

                svc.Export(tempPath, state, settings);

                Assert.True(File.Exists(tempPath));
                using var fs = File.OpenRead(tempPath);
                var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                Assert.Equal(4, decoder.Frames.Count);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
    }
}
