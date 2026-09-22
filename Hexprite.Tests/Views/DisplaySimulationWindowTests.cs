using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hexprite.Core;
using Hexprite.Rendering;
using Hexprite.Tests.E2E;
using Hexprite.ViewModels;
using Hexprite.Views;
using Xunit;

namespace Hexprite.Tests
{
    [Collection("WindowLayoutSettingsFile")]
    [Trait("Category", "Unit")]
    public class DisplaySimulationWindowTests
    {
        public DisplaySimulationWindowTests()
        {
            WpfTestHelper.EnsureApplication();
            WpfTestHelper.RunOnSta(() =>
            {
                if (Application.Current != null && Application.Current.Resources.MergedDictionaries.Count == 0)
                {
                    Application.Current.Resources.MergedDictionaries.Add(
                        new ResourceDictionary { Source = new Uri("/Hexprite;component/Themes/Dim.xaml", UriKind.RelativeOrAbsolute) });
                    Application.Current.Resources.MergedDictionaries.Add(
                        new ResourceDictionary { Source = new Uri("/Hexprite;component/Themes/Styles.xaml", UriKind.RelativeOrAbsolute) });
                }
            });
        }
        [Fact]
        public void ShowOrActivate_SingleInstance_ReusesExistingWindow()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var shell = E2ETestHelper.CreateTestShellViewModel();
                shell.NewDocumentCommand.Execute("128x64");
                var mvm = shell.ActiveDocument as MainViewModel;
                Assert.NotNull(mvm);

                var win1 = DisplaySimulationWindow.ShowOrActivate(mvm);
                Assert.NotNull(win1);
                Assert.True(win1.IsLoaded);

                var win2 = DisplaySimulationWindow.ShowOrActivate(mvm);
                Assert.Same(win1, win2);

                win1.Close();
                mvm.Detach();
                shell.Detach();
            });
        }

        [Fact]
        public void ShowOrActivate_WhenMinimized_RestoresToNormal()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var shell = E2ETestHelper.CreateTestShellViewModel();
                shell.NewDocumentCommand.Execute("128x64");
                var mvm = shell.ActiveDocument as MainViewModel;
                Assert.NotNull(mvm);

                var win = DisplaySimulationWindow.ShowOrActivate(mvm);
                Assert.NotNull(win);

                win.WindowState = WindowState.Minimized;
                Assert.Equal(WindowState.Minimized, win.WindowState);

                DisplaySimulationWindow.ShowOrActivate(mvm);
                Assert.Equal(WindowState.Normal, win.WindowState);

                win.Close();
                mvm.Detach();
                shell.Detach();
            });
        }

        [Fact]
        public void ShowOrActivate_AfterClose_CreatesNewInstance()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var shell = E2ETestHelper.CreateTestShellViewModel();
                shell.NewDocumentCommand.Execute("128x64");
                var mvm = shell.ActiveDocument as MainViewModel;
                Assert.NotNull(mvm);

                var win1 = DisplaySimulationWindow.ShowOrActivate(mvm);
                Assert.NotNull(win1);
                win1.Close();

                var win2 = DisplaySimulationWindow.ShowOrActivate(mvm);
                Assert.NotNull(win2);
                Assert.NotSame(win1, win2);

                win2.Close();
                mvm.Detach();
                shell.Detach();
            });
        }

        [Fact]
        public void DisplaySimulationWindow_ContainsEnhancedControls()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var shell = E2ETestHelper.CreateTestShellViewModel();
                shell.NewDocumentCommand.Execute("128x64");
                var mvm = shell.ActiveDocument as MainViewModel;
                Assert.NotNull(mvm);

                var win = new DisplaySimulationWindow(mvm);
                Assert.NotNull(win.CboDisplayType);
                Assert.Equal(5, win.CboDisplayType.Items.Count);

                Assert.NotNull(win.BtnFit);
                Assert.NotNull(win.BtnCopyImage);
                Assert.NotNull(win.SliderStrength);
                Assert.NotNull(win.CboQuality);
                Assert.NotNull(win.ViewportScrollViewer);

                win.Close();
                mvm.Detach();
                shell.Detach();
            });
        }

        [Fact]
        public void DisplaySimulationRenderer_WithFewerLayerPixelsThanLayers_DoesNotThrow()
        {
            // Defensive hardening test: 3 layers declared, but only 1 pixel buffer provided
            var layers = new List<LayerState>
            {
                new() { Name = "Layer 1", IsVisible = true, OpacityMode = LayerOpacityMode.Solid, BlendMode = LayerBlendMode.Normal },
                new() { Name = "Layer 2", IsVisible = true, OpacityMode = LayerOpacityMode.Solid, BlendMode = LayerBlendMode.Normal },
                new() { Name = "Layer 3", IsVisible = true, OpacityMode = LayerOpacityMode.Solid, BlendMode = LayerBlendMode.Normal },
            };
            var pixels = new bool[16 * 16];
            pixels[0] = true;
            var layerPixels = new List<IPixelBuffer>
            {
                new MonochromePixelBuffer(pixels)
            };

            var outBgra = new uint[32 * 32];

            // Should complete cleanly without throwing IndexOutOfRangeException or ArgumentOutOfRangeException
            DisplaySimulationRenderer.Render(
                16, 16, layers, layerPixels, ColorMode.Monochrome, null,
                FloatingPasteMode.Transparent, 32, 32, Colors.Black, Colors.White,
                DisplaySimulationPreset.GenericLcd, PreviewQuality.Balanced, 0.65, 2.0, false, outBgra);

            Assert.True(outBgra.Length == 1024);
        }

        [Fact]
        public void DisplaySimulationRenderer_WithNullPixelBuffer_DoesNotThrow()
        {
            var layers = new List<LayerState>
            {
                new() { Name = "Layer 1", IsVisible = true, OpacityMode = LayerOpacityMode.Solid, BlendMode = LayerBlendMode.Normal },
            };
            var layerPixels = new List<IPixelBuffer?> { null! };

            var outBgra = new uint[32 * 32];

            DisplaySimulationRenderer.Render(
                16, 16, layers, layerPixels!, ColorMode.Monochrome, null,
                FloatingPasteMode.Transparent, 32, 32, Colors.Black, Colors.White,
                DisplaySimulationPreset.EPaper, PreviewQuality.Fast, 0.5, 2.0, false, outBgra);

            Assert.True(outBgra.Length == 1024);
        }

        [Theory]
        [InlineData(DisplaySimulationPreset.GenericLcd)]
        [InlineData(DisplaySimulationPreset.Ssd1306OledWhite)]
        [InlineData(DisplaySimulationPreset.Ssd1306OledBlue)]
        [InlineData(DisplaySimulationPreset.Ssd1306OledGreen)]
        [InlineData(DisplaySimulationPreset.EPaper)]
        [InlineData(DisplaySimulationPreset.FlipperZeroLcd)]
        public void DisplaySimulationRenderer_AllPresets_RenderValidPixels(DisplaySimulationPreset preset)
        {
            var layers = new List<LayerState>
            {
                new() { Name = "Base", IsVisible = true, OpacityMode = LayerOpacityMode.Solid, BlendMode = LayerBlendMode.Normal },
            };
            var pixels = new bool[8 * 8];
            pixels[0] = true;
            pixels[7] = true;
            pixels[63] = true;
            var layerPixels = new List<IPixelBuffer> { new MonochromePixelBuffer(pixels) };

            var outBgra = new uint[24 * 24];

            DisplaySimulationRenderer.Render(
                8, 8, layers, layerPixels, ColorMode.Monochrome, null,
                FloatingPasteMode.Transparent, 24, 24, Colors.Black, Colors.White,
                preset, PreviewQuality.High, 0.75, 3.0, false, outBgra);

            // Verify that at least some pixels are non-zero (rendered output exists)
            bool hasNonZero = Array.Exists(outBgra, p => p != 0);
            Assert.True(hasNonZero, $"Preset {preset} should produce non-zero rendered pixels");
        }

        [Fact]
        public void DisplaySimulationRenderer_WhiteOled_HasBloomAndNoRainbowStriping()
        {
            var layers = new List<LayerState>
            {
                new() { Name = "Base", IsVisible = true, OpacityMode = LayerOpacityMode.Solid, BlendMode = LayerBlendMode.Normal },
            };
            var pixels = new bool[8 * 8];
            pixels[0] = true; // Only top-left pixel is ON
            var layerPixels = new List<IPixelBuffer> { new MonochromePixelBuffer(pixels) };

            var outBgra = new uint[24 * 24]; // 3x zoom
            var (bg, fg) = MainViewModel.GetSimulationColors(DisplayType.GenericWhite);

            DisplaySimulationRenderer.Render(
                8, 8, layers, layerPixels, ColorMode.Monochrome, null,
                FloatingPasteMode.Transparent, 24, 24, bg, fg,
                DisplaySimulationPreset.Ssd1306OledWhite, PreviewQuality.High, 0.85, 3.0, false, outBgra);

            // Center of lit diode (1, 1) should be clean, bright white without rainbow striping
            uint centerPixel = outBgra[(1 * 24) + 1];
            byte cR = (byte)((centerPixel >> 16) & 0xFF);
            byte cG = (byte)((centerPixel >> 8) & 0xFF);
            byte cB = (byte)(centerPixel & 0xFF);

            Assert.True(cR >= 220 && cG >= 220 && cB >= 220, "White OLED diode center should be bright white");
            // Verify neutral white (no severe RGB sub-pixel stripe skew)
            int maxDiff = Math.Max(Math.Abs(cR - cG), Math.Max(Math.Abs(cG - cB), Math.Abs(cR - cB)));
            Assert.True(maxDiff <= 15, $"White OLED should not have chromatic RGB striping; max channel delta was {maxDiff}");

            // Adjacent unlit pixel (e.g. (3, 1)) should have received unchoked bloom halo
            uint haloPixel = outBgra[(1 * 24) + 3];
            byte hR = (byte)((haloPixel >> 16) & 0xFF);
            byte hG = (byte)((haloPixel >> 8) & 0xFF);
            byte hB = (byte)(haloPixel & 0xFF);

            Assert.True(hR > 0 && hG > 0 && hB > 0, "Unlit pixel adjacent to lit diode should show bloom halo");
        }

        [Fact]
        public void DisplaySimulationRenderer_BlueOled_BloomHaloRadiatesIntoUnlitPixels()
        {
            var layers = new List<LayerState>
            {
                new() { Name = "Base", IsVisible = true, OpacityMode = LayerOpacityMode.Solid, BlendMode = LayerBlendMode.Normal },
            };
            var pixels = new bool[8 * 8];
            pixels[0] = true;
            var layerPixels = new List<IPixelBuffer> { new MonochromePixelBuffer(pixels) };

            var outBgra = new uint[24 * 24];
            var (bg, fg) = MainViewModel.GetSimulationColors(DisplayType.SSD1306Blue);

            DisplaySimulationRenderer.Render(
                8, 8, layers, layerPixels, ColorMode.Monochrome, null,
                FloatingPasteMode.Transparent, 24, 24, bg, fg,
                DisplaySimulationPreset.Ssd1306OledBlue, PreviewQuality.High, 0.85, 3.0, false, outBgra);

            // Adjacent unlit pixel at (3, 1) should show blue bloom halo
            uint haloPixel = outBgra[(1 * 24) + 3];
            byte hR = (byte)((haloPixel >> 16) & 0xFF);
            byte hB = (byte)(haloPixel & 0xFF);

            Assert.True(hB > 0, "Blue OLED halo in unlit pixel must be non-zero (unchoked bloom)");
            Assert.True(hB > hR, $"Blue OLED halo should be blue-dominant (B={hB} > R={hR})");
        }

        [Fact]
        public void DisplaySimulationRenderer_GreenOled_BloomHaloRadiatesIntoUnlitPixels()
        {
            var layers = new List<LayerState>
            {
                new() { Name = "Base", IsVisible = true, OpacityMode = LayerOpacityMode.Solid, BlendMode = LayerBlendMode.Normal },
            };
            var pixels = new bool[8 * 8];
            pixels[0] = true;
            var layerPixels = new List<IPixelBuffer> { new MonochromePixelBuffer(pixels) };

            var outBgra = new uint[24 * 24];
            var (bg, fg) = MainViewModel.GetSimulationColors(DisplayType.SSD1306Green);

            DisplaySimulationRenderer.Render(
                8, 8, layers, layerPixels, ColorMode.Monochrome, null,
                FloatingPasteMode.Transparent, 24, 24, bg, fg,
                DisplaySimulationPreset.Ssd1306OledGreen, PreviewQuality.High, 0.85, 3.0, false, outBgra);

            // Adjacent unlit pixel at (3, 1) should show green bloom halo
            uint haloPixel = outBgra[(1 * 24) + 3];
            byte hR = (byte)((haloPixel >> 16) & 0xFF);
            byte hG = (byte)((haloPixel >> 8) & 0xFF);

            Assert.True(hG > 0, "Green OLED halo in unlit pixel must be non-zero (unchoked bloom)");
            Assert.True(hG > hR, $"Green OLED halo should be green-dominant (G={hG} > R={hR})");
        }

        [Fact]
        public void DisplaySimulationRenderer_EPaper_HasWarmPaperSubstrateAndNoEmissiveBloom()
        {
            var layers = new List<LayerState>
            {
                new() { Name = "Base", IsVisible = true, OpacityMode = LayerOpacityMode.Solid, BlendMode = LayerBlendMode.Normal },
            };
            var pixels = new bool[8 * 8];
            pixels[0] = true;
            var layerPixels = new List<IPixelBuffer> { new MonochromePixelBuffer(pixels) };

            var outBgra = new uint[24 * 24];
            var (bg, fg) = MainViewModel.GetSimulationColors(DisplayType.ePaper);

            DisplaySimulationRenderer.Render(
                8, 8, layers, layerPixels, ColorMode.Monochrome, null,
                FloatingPasteMode.Transparent, 24, 24, bg, fg,
                DisplaySimulationPreset.EPaper, PreviewQuality.High, 0.75, 3.0, false, outBgra);

            // Far background pixel (15, 15) should be warm pearl white
            uint bgPixel = outBgra[(15 * 24) + 15];
            byte bgR = (byte)((bgPixel >> 16) & 0xFF);
            byte bgG = (byte)((bgPixel >> 8) & 0xFF);
            byte bgB = (byte)(bgPixel & 0xFF);

            Assert.True(bgR >= 215 && bgG >= 215 && bgB >= 200, $"EPaper substrate should be warm pearl white ({bgR}, {bgG}, {bgB})");

            // Ink pixel (1, 1) should be dark carbon ink
            uint inkPixel = outBgra[(1 * 24) + 1];
            byte inkR = (byte)((inkPixel >> 16) & 0xFF);
            byte inkG = (byte)((inkPixel >> 8) & 0xFF);
            byte inkB = (byte)(inkPixel & 0xFF);

            Assert.True(inkR <= 50 && inkG <= 50 && inkB <= 50, $"EPaper ink should be carbon dark ({inkR}, {inkG}, {inkB})");
        }

        [Fact]
        public void DisplaySimulationRenderer_FlipperZero_HasLeftToRightBacklightGradient()
        {
            var layers = new List<LayerState>
            {
                new() { Name = "Base", IsVisible = true, OpacityMode = LayerOpacityMode.Solid, BlendMode = LayerBlendMode.Normal },
            };
            var pixels = new bool[8 * 8]; // all unlit (all orange)
            var layerPixels = new List<IPixelBuffer> { new MonochromePixelBuffer(pixels) };

            var outBgra = new uint[24 * 24];
            var (bg, fg) = MainViewModel.GetSimulationColors(DisplayType.FlipperZero);

            DisplaySimulationRenderer.Render(
                8, 8, layers, layerPixels, ColorMode.Monochrome, null,
                FloatingPasteMode.Transparent, 24, 24, bg, fg,
                DisplaySimulationPreset.FlipperZeroLcd, PreviewQuality.High, 0.85, 3.0, false, outBgra);

            // Compare left edge pixel (1, 12) vs right edge pixel (22, 12)
            uint leftPixel = outBgra[(12 * 24) + 1];
            uint rightPixel = outBgra[(12 * 24) + 22];

            byte leftR = (byte)((leftPixel >> 16) & 0xFF);
            byte rightR = (byte)((rightPixel >> 16) & 0xFF);

            Assert.True(leftR >= rightR, $"Left edge ({leftR}) should be brighter than or equal to right edge ({rightR}) due to edge LEDs");
        }

        [Theory]
        [InlineData(DisplayType.GenericWhite, 0x00, 0x00, 0x00, 0xF0, 0xF6, 0xFC)]
        [InlineData(DisplayType.SSD1306Blue, 0x00, 0x00, 0x00, 0x00, 0xB4, 0xFF)]
        [InlineData(DisplayType.SSD1306Green, 0x00, 0x00, 0x00, 0x00, 0xE6, 0x76)]
        [InlineData(DisplayType.ePaper, 0xE6, 0xE4, 0xDD, 0x14, 0x14, 0x14)]
        [InlineData(DisplayType.FlipperZero, 0xFF, 0x82, 0x00, 0x00, 0x00, 0x00)]
        public void GetSimulationColors_AllDisplayTypes_ThemeImmune(
            DisplayType displayType,
            byte expBgR, byte expBgG, byte expBgB,
            byte expFgR, byte expFgG, byte expFgB)
        {
            var (bg, fg) = MainViewModel.GetSimulationColors(displayType);

            Assert.Equal(expBgR, bg.R);
            Assert.Equal(expBgG, bg.G);
            Assert.Equal(expBgB, bg.B);

            Assert.Equal(expFgR, fg.R);
            Assert.Equal(expFgG, fg.G);
            Assert.Equal(expFgB, fg.B);
        }

        [Fact]
        public void DisplaySimulationRenderer_FlipperZero_PreservesVibrantOrangeBackground()
        {
            var layers = new List<LayerState>
            {
                new() { Name = "Base", IsVisible = true, OpacityMode = LayerOpacityMode.Solid, BlendMode = LayerBlendMode.Normal },
            };
            var pixels = new bool[8 * 8];
            pixels[0] = true; // Only pixel (0,0) is dark
            var layerPixels = new List<IPixelBuffer> { new MonochromePixelBuffer(pixels) };

            var outBgra = new uint[24 * 24];
            var flipperBg = System.Windows.Media.Color.FromRgb(0xFF, 0x82, 0x00);
            var flipperFg = System.Windows.Media.Colors.Black;

            // Render at 3x zoom (cellMin >= 2.5f triggers aperture detail regime)
            DisplaySimulationRenderer.Render(
                8, 8, layers, layerPixels, ColorMode.Monochrome, null,
                FloatingPasteMode.Transparent, 24, 24, flipperBg, flipperFg,
                DisplaySimulationPreset.FlipperZeroLcd, PreviewQuality.High, 0.75, 3.0, false, outBgra);

            // Pixel at (12, 12) is in the unlit background and should be vibrant orange
            int bgIdx = (12 * 24) + 12;
            uint bgPixel = outBgra[bgIdx];
            byte bgR = (byte)((bgPixel >> 16) & 0xFF);
            byte bgG = (byte)((bgPixel >> 8) & 0xFF);
            byte bgB = (byte)(bgPixel & 0xFF);

            Assert.True(bgR >= 230, $"Background R ({bgR}) should remain high vibrant orange, not darkened");
            Assert.True(bgG >= 115, $"Background G ({bgG}) should remain high vibrant orange, not darkened");
            Assert.True(bgB <= 25, $"Background B ({bgB}) should remain low for orange backlight");

            // Pixel at center of dark pixel (1, 1) should be deep dark charcoal/black
            int darkIdx = (1 * 24) + 1;
            uint darkPixel = outBgra[darkIdx];
            byte darkR = (byte)((darkPixel >> 16) & 0xFF);
            byte darkG = (byte)((darkPixel >> 8) & 0xFF);
            byte darkB = (byte)(darkPixel & 0xFF);

            Assert.True(darkR <= 30, $"Dark pixel R ({darkR}) should be deep dark");
            Assert.True(darkG <= 30, $"Dark pixel G ({darkG}) should be deep dark");
            Assert.True(darkB <= 30, $"Dark pixel B ({darkB}) should be deep dark");
        }

        [Fact]
        public void DisplaySimulationRenderer_FlipperZero_LowScaleFallback_PreservesOrange()
        {
            var layers = new List<LayerState>
            {
                new() { Name = "Base", IsVisible = true, OpacityMode = LayerOpacityMode.Solid, BlendMode = LayerBlendMode.Normal },
            };
            var pixels = new bool[8 * 8];
            pixels[0] = true;
            var layerPixels = new List<IPixelBuffer> { new MonochromePixelBuffer(pixels) };

            var outBgra = new uint[16 * 16];
            var flipperBg = System.Windows.Media.Color.FromRgb(0xFF, 0x82, 0x00);
            var flipperFg = System.Windows.Media.Colors.Black;

            // Render at 2x zoom (cellMin < 2.5f triggers low-scale fallback)
            DisplaySimulationRenderer.Render(
                8, 8, layers, layerPixels, ColorMode.Monochrome, null,
                FloatingPasteMode.Transparent, 16, 16, flipperBg, flipperFg,
                DisplaySimulationPreset.FlipperZeroLcd, PreviewQuality.Balanced, 0.75, 2.0, false, outBgra);

            int bgIdx = (10 * 16) + 10;
            uint bgPixel = outBgra[bgIdx];
            byte bgR = (byte)((bgPixel >> 16) & 0xFF);
            byte bgG = (byte)((bgPixel >> 8) & 0xFF);

            Assert.True(bgR >= 230, $"Fallback background R ({bgR}) should remain vibrant orange");
            Assert.True(bgG >= 115, $"Fallback background G ({bgG}) should remain vibrant orange");
        }
    }
}
