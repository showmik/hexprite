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

            switch (preset)
            {
                case ImportPreset.Default:
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
                    settings.DitheringAlgorithm = BitmapDitheringAlgorithm.FloydSteinberg;
                    settings.Threshold = 128;
                    settings.Brightness = 0;
                    settings.Contrast = 5;
                    settings.DitherAmount = 100;
                    settings.Sharpen = true;
                    settings.UseSerpentineScanning = true;
                    settings.UseGammaCorrection = true;
                    settings.UseAdaptiveThresholding = true;
                    settings.PreserveEdges = false;
                    settings.Invert = false;
                    break;
                case ImportPreset.RetroMac:
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
                    settings.DitheringAlgorithm = BitmapDitheringAlgorithm.Binary;
                    settings.Threshold = 160;
                    settings.Brightness = 0;
                    settings.Contrast = -10;
                    settings.DitherAmount = 0;
                    settings.Sharpen = true;
                    settings.UseSerpentineScanning = false;
                    settings.UseGammaCorrection = false;
                    settings.UseAdaptiveThresholding = false;
                    settings.PreserveEdges = true;
                    settings.Invert = false;
                    break;
                case ImportPreset.SolidLogo:
                    settings.DitheringAlgorithm = BitmapDitheringAlgorithm.Binary;
                    settings.Threshold = 128;
                    settings.Brightness = 0;
                    settings.Contrast = 20;
                    settings.DitherAmount = 0;
                    settings.Sharpen = false;
                    settings.UseSerpentineScanning = false;
                    settings.UseGammaCorrection = false;
                    settings.UseAdaptiveThresholding = false;
                    settings.PreserveEdges = true;
                    settings.Invert = false;
                    break;
            }
        }
    }
}
