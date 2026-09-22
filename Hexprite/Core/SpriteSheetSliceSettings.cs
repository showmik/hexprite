using System;
using Hexprite.Services;

namespace Hexprite.Core
{
    public enum SpriteSheetLayout
    {
        HorizontalStrip,
        VerticalStrip,
        Grid,
        AutoDetect,
    }

    public enum SliceCanvasMode
    {
        /// <summary>
        /// Sliced frames define the exact sprite canvas size (e.g. 16x16 frame -> 16x16 canvas).
        /// </summary>
        FitFrame,

        /// <summary>
        /// Slices are placed onto a fixed-size target canvas (e.g. 128x64 for Flipper Zero).
        /// </summary>
        FixedCanvas,
    }

    public enum SliceScalingMode
    {
        /// <summary>Places slice at 1:1 pixel size into canvas using Alignment without resizing.</summary>
        Crop1To1,

        /// <summary>Scales slice to fit inside canvas preserving aspect ratio.</summary>
        FitAspect,

        /// <summary>Scales slice to completely fill canvas preserving aspect ratio (crop edges if necessary).</summary>
        FillAspect,

        /// <summary>Stretches slice to fill entire canvas dimensions.</summary>
        Stretch,
    }

    public enum SliceCanvasAlignment
    {
        Center,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    public enum SliceOrder
    {
        /// <summary>Left-to-right then top-to-bottom.</summary>
        RowMajor,

        /// <summary>Top-to-bottom then left-to-right.</summary>
        ColumnMajor,
    }

    public enum PlaybackLoopMode
    {
        /// <summary>Continuous looping from frame 0 to N-1 then back to 0.</summary>
        Loop,

        /// <summary>Plays forward then backward (0 -> N-1 -> 0).</summary>
        PingPong,

        /// <summary>Plays once from 0 to N-1 and stops on the last frame.</summary>
        Once,
    }

    public class SpriteSheetSliceSettings
    {
        public SpriteSheetLayout Layout { get; set; } = SpriteSheetLayout.HorizontalStrip;
        public int FrameWidth { get; set; } = 32;
        public int FrameHeight { get; set; } = 32;
        public int Columns { get; set; }
        public int Rows { get; set; }
        public int OffsetX { get; set; }
        public int OffsetY { get; set; }
        public int SpacingX { get; set; }
        public int SpacingY { get; set; }
        public int MaxFrames { get; set; } = 64;
        public SliceOrder Order { get; set; } = SliceOrder.RowMajor;

        // ── Canvas Target Sizing & Scaling ───────────────────────────────
        public SliceCanvasMode CanvasMode { get; set; } = SliceCanvasMode.FitFrame;
        public SliceScalingMode ScalingMode { get; set; } = SliceScalingMode.Crop1To1;
        public int CanvasWidth { get; set; } = 128;
        public int CanvasHeight { get; set; } = 64;
        public SliceCanvasAlignment Alignment { get; set; } = SliceCanvasAlignment.Center;

        // ── Dithering, Color & Image Pre-processing ─────────────────────
        public BitmapDitheringAlgorithm DitheringAlgorithm { get; set; } = BitmapDitheringAlgorithm.FloydSteinberg;
        public int DitherAmount { get; set; } = 100;
        public int BrightnessThreshold { get; set; } = 128;
        public int AlphaThreshold { get; set; } = 128;
        public bool InvertColors { get; set; }
        public bool UseSerpentineScanning { get; set; }
        public bool UseGammaCorrection { get; set; }
        public int Contrast { get; set; }
        public int Brightness { get; set; }
        public bool AutoTrimEmptyFrames { get; set; }
        public bool SkipEmptyFrames { get; set; }

        // ── Playback Configuration ───────────────────────────────────────
        public int Fps { get; set; } = 12;
        public PlaybackLoopMode LoopMode { get; set; } = PlaybackLoopMode.Loop;

        public SpriteSheetSliceSettings Clone()
        {
            return (SpriteSheetSliceSettings)MemberwiseClone();
        }
    }
}
