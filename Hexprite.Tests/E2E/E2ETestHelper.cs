using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Hexprite.Tests.E2E
{
    /// <summary>
    /// Shared test helpers and fixtures for opaque-box E2E test suites.
    /// </summary>
    public static class E2ETestHelper
    {
        /// <summary>
        /// Creates a 128x64 monochrome sprite with test pattern and specified frame count.
        /// </summary>
        public static SpriteState CreateTestSprite(int frameCount = 3, int width = 128, int height = 64)
        {
            var sprite = new SpriteState(width, height)
            {
                ColorMode = ColorMode.Monochrome,
                FrameRateFps = 5
            };
            sprite.Frames.Clear();

            for (int f = 0; f < frameCount; f++)
            {
                var frame = new FrameState { Name = $"Frame {f + 1}" };
                bool[] pixels = new bool[width * height];

                // Create deterministic distinct pixel pattern per frame
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if ((x + y + f * 4) % 8 == 0 || (x == f * 10 && y < 20))
                        {
                            pixels[y * width + x] = true;
                        }
                    }
                }

                frame.LayerPixels.Add(new MonochromePixelBuffer(pixels));
                sprite.Frames.Add(frame);
            }

            sprite.ActiveFrameIndex = 0;
            return sprite;
        }

        /// <summary>
        /// Creates a 10x10 monochrome app icon sprite.
        /// </summary>
        public static SpriteState CreateTestIconSprite()
        {
            var sprite = new SpriteState(10, 10)
            {
                ColorMode = ColorMode.Monochrome
            };
            sprite.Frames.Clear();

            var frame = new FrameState { Name = "Icon" };
            bool[] pixels = new bool[100];
            // Cross pattern
            for (int i = 0; i < 10; i++)
            {
                pixels[i * 10 + i] = true;
                pixels[i * 10 + (9 - i)] = true;
            }

            frame.LayerPixels.Add(new MonochromePixelBuffer(pixels));
            sprite.Frames.Add(frame);
            sprite.ActiveFrameIndex = 0;
            return sprite;
        }

        /// <summary>
        /// Creates a sample WriteableBitmap with dimensions and test gradient.
        /// </summary>
        public static BitmapSource CreateTestBitmapSource(int width, int height)
        {
            var pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = (y * width + x) * 4;
                    byte val = (byte)((x * 255) / Math.Max(1, width));
                    pixels[idx] = val;     // B
                    pixels[idx + 1] = val; // G
                    pixels[idx + 2] = val; // R
                    pixels[idx + 3] = 255; // A
                }
            }

            return BitmapSource.Create(
                width,
                height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                pixels,
                width * 4);
        }

        /// <summary>
        /// Decodes a 1024-byte vertical page framebuffer back into a 128x64 boolean array.
        /// </summary>
        public static bool[] Decode1024Buffer(byte[] buffer)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            bool[] pixels = new bool[128 * 64];

            for (int y = 0; y < 64; y++)
            {
                int page = y / 8;
                byte bitMask = (byte)(1 << (y % 8));

                for (int x = 0; x < 128; x++)
                {
                    int byteIdx = page * 128 + x;
                    if (byteIdx < buffer.Length)
                    {
                        pixels[y * 128 + x] = (buffer[byteIdx] & bitMask) != 0;
                    }
                }
            }

            return pixels;
        }

        /// <summary>
        /// Creates a fully configured ShellViewModel for headless testing.
        /// </summary>
        public static ShellViewModel CreateTestShellViewModel(
            IAutosaveService? autosaveService = null,
            IDialogService? dialogService = null)
        {
            WpfTestHelper.EnsureApplication();
            var services = new ServiceCollection();
            services.AddSingleton<ICodeGeneratorService, CodeGeneratorService>();
            services.AddSingleton<IDrawingService, DrawingService>();
            services.AddSingleton<IClipboardService, ClipboardService>();
            services.AddSingleton<IPixelClipboardService, PixelClipboardService>();
            services.AddSingleton<IDialogService>(dialogService ?? new Mock<IDialogService>().Object);
            services.AddSingleton<IThemeService>(new Mock<IThemeService>().Object);
            services.AddSingleton<IBugReportService>(new Mock<IBugReportService>().Object);
            services.AddSingleton<IUserFeedbackService>(new Mock<IUserFeedbackService>().Object);
            services.AddSingleton<IAutosaveService>(autosaveService ?? new Mock<IAutosaveService>().Object);
            services.AddSingleton<IControllerFactory, ControllerFactory>();
            services.AddSingleton<IExportService, ExportService>();
            services.AddSingleton<IFileImportExportService, FileImportExportService>();
            services.AddSingleton<IHardwarePreviewService, HardwarePreviewService>();
            var provider = services.BuildServiceProvider();

            return new ShellViewModel(
                provider.GetRequiredService<ICodeGeneratorService>(),
                provider.GetRequiredService<IDrawingService>(),
                provider.GetRequiredService<IClipboardService>(),
                provider.GetRequiredService<IPixelClipboardService>(),
                provider.GetRequiredService<IDialogService>(),
                provider.GetRequiredService<IThemeService>(),
                provider.GetRequiredService<IBugReportService>(),
                provider.GetRequiredService<IUserFeedbackService>(),
                provider.GetRequiredService<IControllerFactory>(),
                provider.GetRequiredService<IExportService>(),
                provider.GetRequiredService<IFileImportExportService>(),
                provider.GetRequiredService<IHardwarePreviewService>(),
                provider);
        }

        /// <summary>
        /// Generates an authentic test asset pack structure on disk strictly for testing purposes.
        /// </summary>
        public static string CreateTestAssetPack(string packId, string targetDir, int animCount = 3)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packId);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetDir);

            string safeName = string.Concat(packId.Split(System.IO.Path.GetInvalidFileNameChars())).Replace(' ', '_');
            string packRoot = System.IO.Path.Combine(targetDir, safeName);
            string animsDir = System.IO.Path.Combine(packRoot, "Anims");
            string iconsDir = System.IO.Path.Combine(packRoot, "Icons");

            Directory.CreateDirectory(animsDir);
            Directory.CreateDirectory(iconsDir);

            var exportService = new FlipperExportService();
            var manifestEntries = new List<FlipperManifestEntry>();

            int count = Math.Max(1, animCount);
            for (int a = 0; a < count; a++)
            {
                string animName = $"{packId}_anim_{a + 1}";
                string animFolder = System.IO.Path.Combine(animsDir, animName);
                Directory.CreateDirectory(animFolder);

                var sprite = CreateTestSprite(frameCount: 2, width: 128, height: 64);
                int minLvl = a == 0 ? 1 : (a == 1 ? 10 : (a == 2 ? 20 : 1));
                int maxLvl = a == 0 ? 10 : (a == 1 ? 20 : 30);
                int minBh = a == 3 ? 8 : 0;
                int maxBh = a == 0 ? 5 : (a == 1 ? 8 : 14);

                var entry = new FlipperManifestEntry
                {
                    Name = animName,
                    MinLevel = minLvl,
                    MaxLevel = maxLvl,
                    MinButthurt = minBh,
                    MaxButthurt = maxBh,
                    Weight = a + 1,
                };
                manifestEntries.Add(entry);

                var settings = new FlipperExportSettings(
                    animsDir,
                    animName,
                    frameRate: 5,
                    passiveFrames: 2,
                    activeFrames: 0,
                    minLevel: minLvl,
                    maxLevel: maxLvl,
                    minButthurt: minBh,
                    maxButthurt: maxBh,
                    weight: a + 1,
                    createManifestTxt: false,
                    targetMode: FlipperExportTargetMode.SingleAnimation
                );

                sprite.FlipperCycle = new FlipperAnimationCycle
                {
                    FramesOrder = [0, 1],
                    PassiveFrameCount = 2,
                    ActiveFrameCount = 0,
                    ActiveCycles = 1,
                    Duration = 3600,
                    ActiveCooldown = 0,
                    BubbleSlots = 1,
                    SpeechBubble = new FlipperSpeechBubble(1, 12, 14, $"Test #{a + 1}", SpeechBubbleTailPosition.BottomLeft)
                };

                exportService.ExportAnimation(sprite, settings);
            }

            string manifestPath = System.IO.Path.Combine(packRoot, "manifest.txt");
            var manifest = new FlipperManifest { Entries = manifestEntries };
            File.WriteAllText(manifestPath, manifest.Serialize());

            string packIconPath = System.IO.Path.Combine(iconsDir, $"I_{packId}_10x10.bm");
            var iconSprite = CreateTestIconSprite();
            exportService.ExportImage(iconSprite, 0, packIconPath);

            return packRoot;
        }

        /// <summary>
        /// Creates a temporary staging directory and returns disposable cleanup handle.
        /// </summary>
        public static TempDirectory CreateTempDirectory() => new();

        public sealed class TempDirectory : IDisposable
        {
            public string Path { get; }

            public TempDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Hexprite_E2E_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(Path))
                    {
                        Directory.Delete(Path, true);
                    }
                }
                catch
                {
                    // Best effort
                }
            }
        }
    }
}
