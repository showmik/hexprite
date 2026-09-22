using System;

namespace Hexprite.Core
{
    public enum ImageExportFormat
    {
        Png,
        Bmp,
        Gif,
        PngSequence,
    }

    public enum ExportColorMode
    {
        EditorColors,
        DisplayPreview,
        CustomPalette,
    }

    public class ImageExportSettings
    {
        public ImageExportFormat Format { get; set; } = ImageExportFormat.Png;
        
        public int Scale { get; set; } = 4;
        
        public ExportColorMode ColorMode { get; set; } = ExportColorMode.EditorColors;

        public System.Windows.Media.Color? CustomBackgroundColor { get; set; }
        public System.Windows.Media.Color? CustomForegroundColor { get; set; }

        public bool ShowGrid { get; set; }

        // Single Image specific
        public bool ExportAllFramesAsSpritesheet { get; set; }
        public bool ExportAllFramesAsSeparateFiles { get; set; }

        // GIF specific
        public int GifFps { get; set; } = 12;
        public bool GifLoopInfinite { get; set; } = true;
        public int GifLoopCount { get; set; } = 1;
        public bool GifEnableDithering { get; set; }
        public bool GifExportAllFrames { get; set; } = true;
        public bool GifEnableDeltaOptimization { get; set; }
        public bool GifTransparentBackground { get; set; } = true;
        
        public ImageExportSettings Clone() => (ImageExportSettings)MemberwiseClone();
    }
}
