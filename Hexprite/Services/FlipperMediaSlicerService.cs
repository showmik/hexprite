using System;
using System.Windows.Media.Imaging;
using Hexprite.Core;

namespace Hexprite.Services
{
    public class MediaSliceSettings
    {
        public SpriteSheetLayout Layout { get; set; } = SpriteSheetLayout.HorizontalStrip;
        public int FrameWidth { get; set; } = 128;
        public int FrameHeight { get; set; } = 64;
        public int Columns { get; set; } = 1;
        public int Rows { get; set; } = 1;
        public int MaxFrames { get; set; } = 64;
        public BitmapDitheringAlgorithm DitheringAlgorithm { get; set; } = BitmapDitheringAlgorithm.FloydSteinberg;
        public int DitherAmount { get; set; } = 100;
        public int BrightnessThreshold { get; set; } = 128;
        public bool InvertColors { get; set; }
    }

    public static class FlipperMediaSlicerService
    {
        private static readonly SpriteSheetSlicerService Slicer = new();

        public static SpriteState SliceToAnimationSprite(BitmapSource source, MediaSliceSettings settings)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(settings);

            int fw = settings.FrameWidth;
            int fh = settings.FrameHeight;

            // When explicit grid columns and rows are provided in Flipper media slicer, auto-derive cell dimensions
            if (settings.Layout == SpriteSheetLayout.Grid && settings.Columns > 0 && settings.Rows > 0)
            {
                if (settings.FrameWidth == 128 && settings.FrameHeight == 64 && (source.PixelWidth != 128 || source.PixelHeight != 64))
                {
                    fw = Math.Max(1, source.PixelWidth / settings.Columns);
                    fh = Math.Max(1, source.PixelHeight / settings.Rows);
                }
            }

            var sliceSettings = new SpriteSheetSliceSettings
            {
                Layout = settings.Layout,
                FrameWidth = fw,
                FrameHeight = fh,
                Columns = settings.Columns,
                Rows = settings.Rows,
                MaxFrames = settings.MaxFrames,
                DitheringAlgorithm = settings.DitheringAlgorithm,
                DitherAmount = settings.DitherAmount,
                BrightnessThreshold = settings.BrightnessThreshold,
                InvertColors = settings.InvertColors,
                CanvasMode = SliceCanvasMode.FixedCanvas,
                CanvasWidth = 128,
                CanvasHeight = 64,
                Alignment = SliceCanvasAlignment.Center
            };

            return Slicer.SliceToAnimationSprite(source, sliceSettings);
        }
    }
}
