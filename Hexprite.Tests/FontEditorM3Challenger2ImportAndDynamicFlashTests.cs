using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    [Collection("WindowLayoutSettingsFile")]
    public class FontEditorM3Challenger2ImportAndDynamicFlashTests
    {
        public FontEditorM3Challenger2ImportAndDynamicFlashTests()
        {
            WpfTestHelper.EnsureApplication();
        }

        private static string CreateCustomPng(int width, int height, Action<byte[]> populateBgraPixels)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"hexprite_m3_test_{Guid.NewGuid():N}.png");
            var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            byte[] pixels = new byte[width * height * 4];

            populateBgraPixels(pixels);

            wb.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), pixels, width * 4, 0);

            using (var fs = File.OpenWrite(tempPath))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(wb));
                encoder.Save(fs);
            }

            return tempPath;
        }

        private static FontDocument CreateTestFont(int firstChar = 65, int lastChar = 66, int width = 8, int height = 8)
        {
            var doc = FontDocument.CreateNew(width, height, firstChar, lastChar);
            doc.FontName = "TestFont";
            doc.Baseline = 6;
            doc.YAdvance = height;
            doc.MaxCellWidth = width;

            // Fill 3x3 block in first glyph
            var g1 = doc.Glyphs[0];
            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 3; x++)
                {
                    g1.Pixels[y * g1.Width + x] = true;
                }
            }

            return doc;
        }

        // =========================================================================
        // SECTION 1: Sprite Sheet Bgra32 White-Pixel Inversion (F19)
        // =========================================================================

        [Fact]
        public void ImportFromSpriteSheet_TransparentBackground_WhiteGlyph_TreatedAsInk()
        {
            var service = new FontImportService();
            // Transparent background (A=0), pure white pixel (255, 255, 255, 255) at (3, 3)
            var tempPath = CreateCustomPng(16, 16, pixels =>
            {
                // Everything is default 0 (transparent)
                int inkIdx = (3 * 16 + 3) * 4;
                pixels[inkIdx] = 255;     // B
                pixels[inkIdx + 1] = 255; // G
                pixels[inkIdx + 2] = 255; // R
                pixels[inkIdx + 3] = 255; // A
            });

            try
            {
                var options = new FontImportOptions { FirstChar = 65, LastChar = 65, Threshold = 128 };
                var doc = service.ImportFromSpriteSheet(tempPath, 16, 16, options);

                Assert.NotNull(doc);
                Assert.Single(doc.Glyphs);
                var glyph = doc.Glyphs[0];

                Assert.True(glyph.Pixels[3 * 16 + 3], "Pure white pixel on transparent background must be detected as ink");
                Assert.False(glyph.Pixels[0], "Transparent background pixel at (0, 0) must not be ink");
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void ImportFromSpriteSheet_TransparentBackground_ColoredGlyphs_TreatedAsInk()
        {
            var service = new FontImportService();
            // Transparent background (A=0), colored pixels (Red, Green, Blue) at (1, 1), (2, 2), (3, 3)
            var tempPath = CreateCustomPng(16, 16, pixels =>
            {
                // Red
                int redIdx = (1 * 16 + 1) * 4;
                pixels[redIdx + 2] = 255; // R
                pixels[redIdx + 3] = 255; // A

                // Green
                int greenIdx = (2 * 16 + 2) * 4;
                pixels[greenIdx + 1] = 255; // G
                pixels[greenIdx + 3] = 255; // A

                // Blue
                int blueIdx = (3 * 16 + 3) * 4;
                pixels[blueIdx] = 255;     // B
                pixels[blueIdx + 3] = 255; // A
            });

            try
            {
                var options = new FontImportOptions { FirstChar = 65, LastChar = 65, Threshold = 128 };
                var doc = service.ImportFromSpriteSheet(tempPath, 16, 16, options);

                Assert.NotNull(doc);
                var glyph = doc.Glyphs[0];
                Assert.True(glyph.Pixels[1 * 16 + 1], "Red pixel on transparent background must be detected as ink");
                Assert.True(glyph.Pixels[2 * 16 + 2], "Green pixel on transparent background must be detected as ink");
                Assert.True(glyph.Pixels[3 * 16 + 3], "Blue pixel on transparent background must be detected as ink");
                Assert.False(glyph.Pixels[0], "Transparent pixel at (0, 0) must not be ink");
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void ImportFromSpriteSheet_OpaqueWhiteBackground_DarkGlyphs_PureWhiteNeverTreatedAsInk()
        {
            var service = new FontImportService();
            // Opaque white background (255, 255, 255, 255)
            // Black pixel at (2, 2)
            // Dark gray pixel at (4, 4)
            // Pure white pixel explicitly at (6, 6)
            var tempPath = CreateCustomPng(16, 16, pixels =>
            {
                // Fill all with opaque white
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    pixels[i] = 255;     // B
                    pixels[i + 1] = 255; // G
                    pixels[i + 2] = 255; // R
                    pixels[i + 3] = 255; // A
                }

                // Black pixel
                int blackIdx = (2 * 16 + 2) * 4;
                pixels[blackIdx] = 0;
                pixels[blackIdx + 1] = 0;
                pixels[blackIdx + 2] = 0;
                pixels[blackIdx + 3] = 255;

                // Dark gray (luminance < 128)
                int darkGrayIdx = (4 * 16 + 4) * 4;
                pixels[darkGrayIdx] = 50;
                pixels[darkGrayIdx + 1] = 50;
                pixels[darkGrayIdx + 2] = 50;
                pixels[darkGrayIdx + 3] = 255;

                // Pure white pixel at (6, 6)
                int whiteIdx = (6 * 16 + 6) * 4;
                pixels[whiteIdx] = 255;
                pixels[whiteIdx + 1] = 255;
                pixels[whiteIdx + 2] = 255;
                pixels[whiteIdx + 3] = 255;
            });

            try
            {
                var options = new FontImportOptions { FirstChar = 65, LastChar = 65, Threshold = 128 };
                var doc = service.ImportFromSpriteSheet(tempPath, 16, 16, options);

                Assert.NotNull(doc);
                var glyph = doc.Glyphs[0];

                Assert.True(glyph.Pixels[2 * 16 + 2], "Black pixel must be ink on white background");
                Assert.True(glyph.Pixels[4 * 16 + 4], "Dark gray pixel must be ink on white background");
                Assert.False(glyph.Pixels[6 * 16 + 6], "Pure white pixel must NOT be ink on white background (F19 inversion prevention)");
                Assert.False(glyph.Pixels[0], "White corner background pixel must NOT be ink");
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void ImportFromSpriteSheet_OpaqueDarkBackground_LightGlyphs_PureWhiteTreatedAsInk()
        {
            var service = new FontImportService();
            // Opaque black background (0, 0, 0, 255)
            // Pure white pixel at (3, 3)
            // Light gray pixel at (5, 5)
            // Pure black background pixel at (0, 0)
            var tempPath = CreateCustomPng(16, 16, pixels =>
            {
                // Fill all with opaque black
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    pixels[i] = 0;       // B
                    pixels[i + 1] = 0;   // G
                    pixels[i + 2] = 0;   // R
                    pixels[i + 3] = 255; // A
                }

                // Pure white pixel
                int whiteIdx = (3 * 16 + 3) * 4;
                pixels[whiteIdx] = 255;
                pixels[whiteIdx + 1] = 255;
                pixels[whiteIdx + 2] = 255;
                pixels[whiteIdx + 3] = 255;

                // Light gray pixel (lum >= 128)
                int lightGrayIdx = (5 * 16 + 5) * 4;
                pixels[lightGrayIdx] = 200;
                pixels[lightGrayIdx + 1] = 200;
                pixels[lightGrayIdx + 2] = 200;
                pixels[lightGrayIdx + 3] = 255;
            });

            try
            {
                var options = new FontImportOptions { FirstChar = 65, LastChar = 65, Threshold = 128 };
                var doc = service.ImportFromSpriteSheet(tempPath, 16, 16, options);

                Assert.NotNull(doc);
                var glyph = doc.Glyphs[0];

                Assert.True(glyph.Pixels[3 * 16 + 3], "Pure white pixel must be ink on dark background");
                Assert.True(glyph.Pixels[5 * 16 + 5], "Light gray pixel must be ink on dark background");
                Assert.False(glyph.Pixels[0], "Black corner background pixel must NOT be ink on dark background");
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void ImportFromSpriteSheet_CornerNoise_ThreeWhiteCornersOneDark_ResilientlyDetectsLightBackground()
        {
            var service = new FontImportService();
            // 16x16 image: White background, but top-left corner (0, 0) has a single black pixel noise
            var tempPath = CreateCustomPng(16, 16, pixels =>
            {
                // Fill opaque white
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    pixels[i] = 255;
                    pixels[i + 1] = 255;
                    pixels[i + 2] = 255;
                    pixels[i + 3] = 255;
                }

                // Corrupt top-left corner (0, 0) with black noise
                int noisyCorner = 0;
                pixels[noisyCorner] = 0;
                pixels[noisyCorner + 1] = 0;
                pixels[noisyCorner + 2] = 0;
                pixels[noisyCorner + 3] = 255;

                // Dark ink at (5, 5)
                int inkIdx = (5 * 16 + 5) * 4;
                pixels[inkIdx] = 0;
                pixels[inkIdx + 1] = 0;
                pixels[inkIdx + 2] = 0;
                pixels[inkIdx + 3] = 255;
            });

            try
            {
                var options = new FontImportOptions { FirstChar = 65, LastChar = 65, Threshold = 128 };
                var doc = service.ImportFromSpriteSheet(tempPath, 16, 16, options);

                Assert.NotNull(doc);
                var glyph = doc.Glyphs[0];
                // Average luminance of 4 corners: (0 + 255 + 255 + 255) / 4 = 191 > 128
                // So background is still correctly identified as LightBg!
                Assert.True(glyph.Pixels[5 * 16 + 5], "Black ink at (5, 5) must be detected as ink");
                // White pixel at (1, 1) should not be ink
                Assert.False(glyph.Pixels[1 * 16 + 1], "White pixels must not be ink despite corner noise");
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void ImportFromSpriteSheet_CornerNoise_ThreeDarkCornersOneLight_ResilientlyDetectsDarkBackground()
        {
            var service = new FontImportService();
            // 16x16 image: Dark background, but top-right corner (15, 0) has a single white pixel noise
            var tempPath = CreateCustomPng(16, 16, pixels =>
            {
                // Fill opaque black
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    pixels[i] = 0;
                    pixels[i + 1] = 0;
                    pixels[i + 2] = 0;
                    pixels[i + 3] = 255;
                }

                // Corrupt top-right corner (15, 0) with white noise
                int noisyCorner = 15 * 4;
                pixels[noisyCorner] = 255;
                pixels[noisyCorner + 1] = 255;
                pixels[noisyCorner + 2] = 255;
                pixels[noisyCorner + 3] = 255;

                // White ink at (5, 5)
                int inkIdx = (5 * 16 + 5) * 4;
                pixels[inkIdx] = 255;
                pixels[inkIdx + 1] = 255;
                pixels[inkIdx + 2] = 255;
                pixels[inkIdx + 3] = 255;
            });

            try
            {
                var options = new FontImportOptions { FirstChar = 65, LastChar = 65, Threshold = 128 };
                var doc = service.ImportFromSpriteSheet(tempPath, 16, 16, options);

                Assert.NotNull(doc);
                var glyph = doc.Glyphs[0];
                // Average luminance of corners: (0 + 255 + 0 + 0) / 4 = 63 <= 128 -> Dark background
                Assert.True(glyph.Pixels[5 * 16 + 5], "White ink must be detected as ink on dark background with noisy corner");
                Assert.False(glyph.Pixels[0], "Black pixel at (0, 0) must not be ink");
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void ImportFromSpriteSheet_CornerNoise_OneTransparentCornerThreeOpaqueWhite_DetectsLightBackground()
        {
            var service = new FontImportService();
            // 1 corner is transparent (0, 0), other 3 are opaque white
            var tempPath = CreateCustomPng(16, 16, pixels =>
            {
                // Fill opaque white
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    pixels[i] = 255;
                    pixels[i + 1] = 255;
                    pixels[i + 2] = 255;
                    pixels[i + 3] = 255;
                }

                // Corner (0, 0) is transparent
                pixels[0] = 0;
                pixels[1] = 0;
                pixels[2] = 0;
                pixels[3] = 0;

                // Black ink at (7, 7)
                int inkIdx = (7 * 16 + 7) * 4;
                pixels[inkIdx] = 0;
                pixels[inkIdx + 1] = 0;
                pixels[inkIdx + 2] = 0;
                pixels[inkIdx + 3] = 255;
            });

            try
            {
                var options = new FontImportOptions { FirstChar = 65, LastChar = 65, Threshold = 128 };
                var doc = service.ImportFromSpriteSheet(tempPath, 16, 16, options);

                Assert.NotNull(doc);
                var glyph = doc.Glyphs[0];
                // transparentCorners = 1 < (4 / 2 = 2) -> falls back to opaqueCorners = 3 with avg lum 255 > 128
                Assert.True(glyph.Pixels[7 * 16 + 7], "Black pixel at (7, 7) must be detected as ink");
                Assert.False(glyph.Pixels[1 * 16 + 1], "White pixel must not be detected as ink");
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void ImportFromSpriteSheet_Minimal1x1Image_DoesNotThrow()
        {
            var service = new FontImportService();
            var tempPath = CreateCustomPng(1, 1, pixels =>
            {
                pixels[0] = 255;
                pixels[1] = 255;
                pixels[2] = 255;
                pixels[3] = 255;
            });

            try
            {
                var options = new FontImportOptions { FirstChar = 65, LastChar = 65, Threshold = 128 };
                var doc = service.ImportFromSpriteSheet(tempPath, 1, 1, options);
                Assert.NotNull(doc);
                Assert.Single(doc.Glyphs);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        // =========================================================================
        // SECTION 2: FontViewModel Dynamic Flash Calculation Updates (F18)
        // =========================================================================

        [Fact]
        public void FontViewModel_SwitchExportFormat_AllFiveFormats_UpdatesEstimatedFlashBytesImmediately()
        {
            var vm = new FontViewModel();
            var doc = CreateTestFont(65, 66, 8, 8);
            vm.Document = doc;

            // 1. AdafruitGfx
            vm.ExportFormat = FontExportFormat.AdafruitGfx;
            int adafruitBytes = vm.EstimatedFlashBytes;
            Assert.Equal(doc.EstimateFlashBytes(FontExportFormat.AdafruitGfx), adafruitBytes);

            // 2. Lvgl
            vm.ExportFormat = FontExportFormat.Lvgl;
            int lvglBytes = vm.EstimatedFlashBytes;
            Assert.Equal(doc.EstimateFlashBytes(FontExportFormat.Lvgl), lvglBytes);
            Assert.NotEqual(adafruitBytes, lvglBytes);

            // 3. FlipperZero
            vm.ExportFormat = FontExportFormat.FlipperZero;
            int flipperBytes = vm.EstimatedFlashBytes;
            Assert.Equal(doc.EstimateFlashBytes(FontExportFormat.FlipperZero), flipperBytes);

            // 4. RawCArray
            vm.ExportFormat = FontExportFormat.RawCArray;
            int rawCBytes = vm.EstimatedFlashBytes;
            Assert.Equal(doc.EstimateFlashBytes(FontExportFormat.RawCArray), rawCBytes);

            // 5. U8g2Bdf
            vm.ExportFormat = FontExportFormat.U8g2Bdf;
            int u8g2Bytes = vm.EstimatedFlashBytes;
            Assert.Equal(doc.EstimateFlashBytes(FontExportFormat.U8g2Bdf), u8g2Bytes);
        }

        [Fact]
        public void FontViewModel_SwitchExportFormat_RaisesPropertyChangedForEstimatedFlashBytesImmediately()
        {
            var vm = new FontViewModel();
            var doc = CreateTestFont(65, 66, 8, 8);
            vm.Document = doc;

            var formatsToTest = new[]
            {
                FontExportFormat.AdafruitGfx,
                FontExportFormat.Lvgl,
                FontExportFormat.FlipperZero,
                FontExportFormat.RawCArray,
                FontExportFormat.U8g2Bdf,
                FontExportFormat.AdafruitGfx
            };

            foreach (var targetFormat in formatsToTest)
            {
                if (vm.ExportFormat == targetFormat) continue;

                var changedProps = new List<string>();
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName != null)
                    {
                        changedProps.Add(e.PropertyName);
                    }
                };

                int prevFlashBytes = vm.EstimatedFlashBytes;
                vm.ExportFormat = targetFormat;

                // Verify ExportFormat was updated
                Assert.Equal(targetFormat, vm.ExportFormat);
                Assert.Contains(nameof(FontViewModel.ExportFormat), changedProps);

                // If the flash calculation changed, EstimatedFlashBytes PropertyChanged must be raised
                int newFlashBytes = vm.EstimatedFlashBytes;
                if (prevFlashBytes != newFlashBytes)
                {
                    Assert.Contains(nameof(FontViewModel.EstimatedFlashBytes), changedProps);
                }

                // And EstimatedFlashBytes must match doc.EstimateFlashBytes
                Assert.Equal(doc.EstimateFlashBytes(targetFormat), vm.EstimatedFlashBytes);
            }
        }

        [Fact]
        public void FontViewModel_DimensionChanges_DynamicallyUpdateEstimatedFlashBytes()
        {
            var vm = new FontViewModel();
            var doc = CreateTestFont(65, 66, 8, 8);
            vm.Document = doc;
            vm.ExportFormat = FontExportFormat.FlipperZero;

            int initialFlash = vm.EstimatedFlashBytes;

            // Resize CellHeight
            var changedProps = new List<string>();
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != null) changedProps.Add(e.PropertyName);
            };

            vm.CellHeight = 16;

            Assert.True(vm.EstimatedFlashBytes > initialFlash, "Estimated flash bytes should increase when CellHeight increases");
            Assert.Contains(nameof(FontViewModel.EstimatedFlashBytes), changedProps);
        }

        [Fact]
        public void FontViewModel_UndoRedo_RestoresEstimatedFlashBytesAccurately()
        {
            var vm = new FontViewModel();
            var doc = CreateTestFont(65, 66, 8, 8);
            vm.Document = doc;

            vm.ExportFormat = FontExportFormat.AdafruitGfx;
            int adafruitBytes = vm.EstimatedFlashBytes;

            vm.ExportFormat = FontExportFormat.Lvgl;
            int lvglBytes = vm.EstimatedFlashBytes;
            Assert.NotEqual(adafruitBytes, lvglBytes);

            // Undo back to Adafruit
            Assert.True(vm.CanUndo);
            vm.Undo();
            Assert.Equal(FontExportFormat.AdafruitGfx, vm.ExportFormat);
            Assert.Equal(adafruitBytes, vm.EstimatedFlashBytes);

            // Redo forward to Lvgl
            Assert.True(vm.CanRedo);
            vm.Redo();
            Assert.Equal(FontExportFormat.Lvgl, vm.ExportFormat);
            Assert.Equal(lvglBytes, vm.EstimatedFlashBytes);
        }

        [Fact]
        public void FontViewModel_EstimateFlashBytes_AllFiveFormulas_ProduceExpectedValues()
        {
            // Document with 2 glyphs of 8x8
            // Glyph 0: 3x3 block of ink -> tight bounds: cw=3, ch=3
            // Glyph 1: empty -> tight bounds: cw=0, ch=0
            var doc = CreateTestFont(65, 66, 8, 8);

            // 1. AdafruitGfx:
            // bitmapBytes: (3*3 + 7) / 8 = 2 bytes. Empty glyph = 0 bytes. Total = 2
            // glyphArray: 2 * 7 = 14
            // struct: 16
            // Expected = 2 + 14 + 16 = 32
            Assert.Equal(32, doc.EstimateFlashBytes(FontExportFormat.AdafruitGfx));

            // 2. Lvgl:
            // bitmapBytes: (3*3 + 7) / 8 = 2 bytes. Total = 2
            // glyphDsc: 2 * 12 = 24
            // cmap: 8
            // fontDsc: 36
            // Expected = 2 + 24 + 8 + 36 = 70
            Assert.Equal(70, doc.EstimateFlashBytes(FontExportFormat.Lvgl));

            // 3. FlipperZero:
            // Column-major bitmapBytes:
            // Glyph 0: Width (8) * bytesPerCol ((8+7)/8 = 1) = 8 bytes
            // Glyph 1: Width (8) * bytesPerCol (1) = 8 bytes
            // Total bitmap = 16
            // glyphMetrics: 2 * 4 = 8
            // header: 16
            // Expected = 16 + 8 + 16 = 40
            Assert.Equal(40, doc.EstimateFlashBytes(FontExportFormat.FlipperZero));

            // 4. RawCArray:
            // Glyph 0: ch(3) * ((cw(3)+7)/8 = 1) = 3 bytes
            // Glyph 1: empty -> 1 byte
            // Total bitmap = 4
            // glyphTable: 2 * 8 = 16
            // struct: 16
            // Expected = 4 + 16 + 16 = 36
            Assert.Equal(36, doc.EstimateFlashBytes(FontExportFormat.RawCArray));

            // 5. U8g2Bdf:
            // Glyph 0: ch(3) * ((cw(3)+7)/8 = 1) = 3 bytes
            // Glyph 1: empty -> 0 bytes
            // Total bitmap = 3
            // glyphHeaders: 2 * 4 = 8
            // fontHeader: 24
            // Expected = 3 + 8 + 24 = 35
            Assert.Equal(35, doc.EstimateFlashBytes(FontExportFormat.U8g2Bdf));
        }
    }
}
