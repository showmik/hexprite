using System;

namespace Hexprite.Services
{
    /// <summary>
    /// Configuration for importing animated image sequences.
    /// </summary>
    public class AnimationImportSettings : BitmapImportSettings
    {
        /// <summary>Target frames per second for the animation.</summary>
        public int TargetFps { get; set; } = 8;
        /// <summary>Maximum number of frames allowed.</summary>
        public int MaxFrames { get; set; } = 32;
        /// <summary>If true, uniformly samples the source frames to match the target FPS.</summary>
        public bool UniformSampling { get; set; } = true;

        public static AnimationImportSettings FromBase(BitmapImportSettings source)
        {
            return new AnimationImportSettings
            {
                Preset = source.Preset,
                MaxDimension = source.MaxDimension,
                Threshold = source.Threshold,
                AlphaThreshold = source.AlphaThreshold,
                Invert = source.Invert,
                DitheringAlgorithm = source.DitheringAlgorithm,
                ScalingMode = source.ScalingMode,
                UseSerpentineScanning = source.UseSerpentineScanning,
                UseGammaCorrection = source.UseGammaCorrection,
                UseAdaptiveThresholding = source.UseAdaptiveThresholding,
                PreserveEdges = source.PreserveEdges,
                Brightness = source.Brightness,
                Contrast = source.Contrast,
                DitherAmount = source.DitherAmount,
                Sharpen = source.Sharpen,
            };
        }
    }
}
