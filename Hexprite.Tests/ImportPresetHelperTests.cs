using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class ImportPresetHelperTests
    {
        [Fact]
        public void ApplyPreset_Custom_DoesNotModifySettings()
        {
            var settings = new BitmapImportSettings
            {
                Threshold = 42,
                Brightness = 15,
                Contrast = 25,
                DitherAmount = 50,
                DitheringAlgorithm = BitmapDitheringAlgorithm.Stucki,
                Sharpen = true,
                UseSerpentineScanning = true,
                UseGammaCorrection = true,
                UseAdaptiveThresholding = true,
                PreserveEdges = true,
                Invert = true
            };

            ImportPresetHelper.ApplyPreset(ImportPreset.Custom, settings);

            Assert.Equal(42, settings.Threshold);
            Assert.Equal(15, settings.Brightness);
            Assert.Equal(25, settings.Contrast);
            Assert.Equal(50, settings.DitherAmount);
            Assert.Equal(BitmapDitheringAlgorithm.Stucki, settings.DitheringAlgorithm);
            Assert.True(settings.Sharpen);
            Assert.True(settings.UseSerpentineScanning);
            Assert.True(settings.UseGammaCorrection);
            Assert.True(settings.UseAdaptiveThresholding);
            Assert.True(settings.PreserveEdges);
            Assert.True(settings.Invert);
        }

        [Fact]
        public void ApplyPreset_Default_AppliesDefaultValues()
        {
            var settings = new BitmapImportSettings { Threshold = 999, Invert = true };

            ImportPresetHelper.ApplyPreset(ImportPreset.Default, settings);

            Assert.Equal(BitmapDitheringAlgorithm.Atkinson, settings.DitheringAlgorithm);
            Assert.Equal(128, settings.Threshold);
            Assert.Equal(0, settings.Brightness);
            Assert.Equal(0, settings.Contrast);
            Assert.Equal(100, settings.DitherAmount);
            Assert.False(settings.Sharpen);
            Assert.False(settings.UseSerpentineScanning);
            Assert.False(settings.UseGammaCorrection);
            Assert.False(settings.UseAdaptiveThresholding);
            Assert.False(settings.PreserveEdges);
            Assert.False(settings.Invert);
        }

        [Fact]
        public void ApplyPreset_Photo_AppliesPhotoValues()
        {
            var settings = new BitmapImportSettings();

            ImportPresetHelper.ApplyPreset(ImportPreset.Photo, settings);

            Assert.Equal(BitmapDitheringAlgorithm.FloydSteinberg, settings.DitheringAlgorithm);
            Assert.Equal(128, settings.Threshold);
            Assert.Equal(0, settings.Brightness);
            Assert.Equal(5, settings.Contrast);
            Assert.Equal(100, settings.DitherAmount);
            Assert.True(settings.Sharpen);
            Assert.True(settings.UseSerpentineScanning);
            Assert.True(settings.UseGammaCorrection);
            Assert.True(settings.UseAdaptiveThresholding);
            Assert.False(settings.PreserveEdges);
            Assert.False(settings.Invert);
        }

        [Fact]
        public void ApplyPreset_RetroMac_AppliesRetroMacValues()
        {
            var settings = new BitmapImportSettings();

            ImportPresetHelper.ApplyPreset(ImportPreset.RetroMac, settings);

            Assert.Equal(BitmapDitheringAlgorithm.Atkinson, settings.DitheringAlgorithm);
            Assert.Equal(128, settings.Threshold);
            Assert.Equal(0, settings.Brightness);
            Assert.Equal(15, settings.Contrast);
            Assert.Equal(100, settings.DitherAmount);
            Assert.True(settings.Sharpen);
            Assert.False(settings.UseSerpentineScanning);
            Assert.False(settings.UseGammaCorrection);
            Assert.False(settings.UseAdaptiveThresholding);
            Assert.False(settings.PreserveEdges);
            Assert.False(settings.Invert);
        }

        [Fact]
        public void ApplyPreset_PixelArt_AppliesPixelArtValues()
        {
            var settings = new BitmapImportSettings();

            ImportPresetHelper.ApplyPreset(ImportPreset.PixelArt, settings);

            Assert.Equal(BitmapDitheringAlgorithm.Bayer, settings.DitheringAlgorithm);
            Assert.Equal(128, settings.Threshold);
            Assert.Equal(0, settings.Brightness);
            Assert.Equal(10, settings.Contrast);
            Assert.Equal(100, settings.DitherAmount);
            Assert.False(settings.Sharpen);
            Assert.False(settings.UseSerpentineScanning);
            Assert.False(settings.UseGammaCorrection);
            Assert.False(settings.UseAdaptiveThresholding);
            Assert.False(settings.PreserveEdges);
            Assert.False(settings.Invert);
        }

        [Fact]
        public void ApplyPreset_LineArt_AppliesLineArtValues()
        {
            var settings = new BitmapImportSettings();

            ImportPresetHelper.ApplyPreset(ImportPreset.LineArt, settings);

            Assert.Equal(BitmapDitheringAlgorithm.Binary, settings.DitheringAlgorithm);
            Assert.Equal(160, settings.Threshold);
            Assert.Equal(0, settings.Brightness);
            Assert.Equal(-10, settings.Contrast);
            Assert.Equal(0, settings.DitherAmount);
            Assert.True(settings.Sharpen);
            Assert.False(settings.UseSerpentineScanning);
            Assert.False(settings.UseGammaCorrection);
            Assert.False(settings.UseAdaptiveThresholding);
            Assert.True(settings.PreserveEdges);
            Assert.False(settings.Invert);
        }

        [Fact]
        public void ApplyPreset_SolidLogo_AppliesSolidLogoValues()
        {
            var settings = new BitmapImportSettings();

            ImportPresetHelper.ApplyPreset(ImportPreset.SolidLogo, settings);

            Assert.Equal(BitmapDitheringAlgorithm.Binary, settings.DitheringAlgorithm);
            Assert.Equal(128, settings.Threshold);
            Assert.Equal(0, settings.Brightness);
            Assert.Equal(20, settings.Contrast);
            Assert.Equal(0, settings.DitherAmount);
            Assert.False(settings.Sharpen);
            Assert.False(settings.UseSerpentineScanning);
            Assert.False(settings.UseGammaCorrection);
            Assert.False(settings.UseAdaptiveThresholding);
            Assert.True(settings.PreserveEdges);
            Assert.False(settings.Invert);
        }
    }
}
