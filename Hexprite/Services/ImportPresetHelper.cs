using System;

namespace Hexprite.Services
{
    public enum ImportPreset
    {
        Custom = 0,
        Default = 1,
        Photo = 2,
        RetroMac = 3,
        PixelArt = 4,
        LineArt = 5,
        SolidLogo = 6,
    }

    public static class ImportPresetHelper
    {
        public static void ApplyPreset(ImportPreset preset, BitmapImportSettings settings)
        {
            if (preset == ImportPreset.Custom) return;

            settings.Preset = preset;

            switch (preset)
            {
                case ImportPreset.Default:
                    settings.ScalingMode = BitmapScalingMode.Fant;
                    settings.DitheringAlgorithm = BitmapDitheringAlgorithm.Atkinson;
                    settings.Threshold = 128;
                    settings.Brightness = 0;
                    settings.Contrast = 0;
                    settings.DitherAmount = 100;
                    settings.Sharpen = false;
                    settings.UseSerpentineScanning = false;
                    settings.UseGammaCorrection = false;
                    settings.UseAdaptiveThresholding = false;
                    settings.PreserveEdges = false;
                    settings.Invert = false;
                    break;
                case ImportPreset.Photo:
                    settings.ScalingMode = BitmapScalingMode.Fant;
                    settings.DitheringAlgorithm = BitmapDitheringAlgorithm.FloydSteinberg;
                    settings.Threshold = 128;
                    settings.Brightness = 0;
                    settings.Contrast = 5;
                    settings.DitherAmount = 100;
                    settings.Sharpen = true;
                    settings.UseSerpentineScanning = true;
                    settings.UseGammaCorrection = true;
                    settings.UseAdaptiveThresholding = false;
                    settings.PreserveEdges = false;
                    settings.Invert = false;
                    break;
                case ImportPreset.RetroMac:
                    settings.ScalingMode = BitmapScalingMode.Fant;
                    settings.DitheringAlgorithm = BitmapDitheringAlgorithm.Atkinson;
                    settings.Threshold = 128;
                    settings.Brightness = 0;
                    settings.Contrast = 15;
                    settings.DitherAmount = 100;
                    settings.Sharpen = true;
                    settings.UseSerpentineScanning = false;
                    settings.UseGammaCorrection = false;
                    settings.UseAdaptiveThresholding = false;
                    settings.PreserveEdges = false;
                    settings.Invert = false;
                    break;
                case ImportPreset.PixelArt:
                    settings.ScalingMode = BitmapScalingMode.NearestNeighbor;
                    settings.DitheringAlgorithm = BitmapDitheringAlgorithm.Bayer;
                    settings.Threshold = 128;
                    settings.Brightness = 0;
                    settings.Contrast = 10;
                    settings.DitherAmount = 100;
                    settings.Sharpen = false;
                    settings.UseSerpentineScanning = false;
                    settings.UseGammaCorrection = false;
                    settings.UseAdaptiveThresholding = false;
                    settings.PreserveEdges = false;
                    settings.Invert = false;
                    break;
                case ImportPreset.LineArt:
                    settings.ScalingMode = BitmapScalingMode.Fant;
                    settings.DitheringAlgorithm = BitmapDitheringAlgorithm.Binary;
                    settings.Threshold = 150;
                    settings.Brightness = 0;
                    settings.Contrast = 15;
                    settings.DitherAmount = 0;
                    settings.Sharpen = true;
                    settings.UseSerpentineScanning = false;
                    settings.UseGammaCorrection = false;
                    settings.UseAdaptiveThresholding = false;
                    settings.PreserveEdges = false;
                    settings.Invert = false;
                    break;
                case ImportPreset.SolidLogo:
                    settings.ScalingMode = BitmapScalingMode.Fant;
                    settings.DitheringAlgorithm = BitmapDitheringAlgorithm.Binary;
                    settings.Threshold = 128;
                    settings.Brightness = 0;
                    settings.Contrast = 20;
                    settings.DitherAmount = 0;
                    settings.Sharpen = false;
                    settings.UseSerpentineScanning = false;
                    settings.UseGammaCorrection = false;
                    settings.UseAdaptiveThresholding = false;
                    settings.PreserveEdges = false;
                    settings.Invert = false;
                    break;
            }
        }

        public static string GetPresetDescription(ImportPreset preset) => preset switch
        {
            ImportPreset.Default => "Balanced Atkinson dithering for general images and illustrations.",
            ImportPreset.Photo => "Floyd-Steinberg diffusion with gamma correction for continuous tones and photos.",
            ImportPreset.RetroMac => "Classic 1984 Macintosh high-contrast Atkinson aesthetic.",
            ImportPreset.PixelArt => "Nearest-neighbor scaling with Bayer ordered matrix for sprites and game art.",
            ImportPreset.LineArt => "High-contrast binary thresholding with sharpening for ink and line drawings.",
            ImportPreset.SolidLogo => "Sharp binary thresholding for solid vector logos, badges, and text stamps.",
            _ => "Custom manual configuration."
        };
    }
}
