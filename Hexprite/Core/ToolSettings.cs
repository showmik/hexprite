using CommunityToolkit.Mvvm.ComponentModel;

namespace Hexprite.Core
{
    /// <summary>
    /// Encapsulates tool settings (brush size, shape, pixel-perfect mode, etc.)
    /// that persist per document. Extracted from MainViewModel to reduce class size
    /// and improve separation of concerns.
    /// </summary>
    public partial class ToolSettings : ObservableObject
    {
        [ObservableProperty]
        public partial ToolMode CurrentTool { get; set; } = ToolMode.Pencil;
        [ObservableProperty]
        public partial int BrushSize { get; set; } = 1;
        [ObservableProperty]
        public partial BrushShape BrushShape { get; set; } = BrushShape.Circle;
        [ObservableProperty]
        public partial int BrushAngle { get; set; } = 0;

        [ObservableProperty]
        public partial bool IsPixelPerfectEnabled { get; set; }
        [ObservableProperty]
        public partial DitherPattern DitherPattern { get; set; } = DitherPattern.Checkerboard;

        // ── Text Tool Settings ────────────────────────────────────────────
        [ObservableProperty]
        public partial string FontFamily { get; set; } = "Arial";
        [ObservableProperty]
        public partial int FontSize { get; set; } = 12;

        [ObservableProperty]
        public partial bool IsBold { get; set; }

        [ObservableProperty]
        public partial bool IsItalic { get; set; }

        /// <summary>
        /// Returns whether pixel-perfect mode is available for the current tool configuration.
        /// Pixel-perfect works with the Pencil or Eraser tool at any brush size.
        /// </summary>
        public bool IsPixelPerfectAvailable => CurrentTool == ToolMode.Pencil || CurrentTool == ToolMode.Eraser || CurrentTool == ToolMode.Dither;

        partial void OnCurrentToolChanged(ToolMode value)
        {
            OnPropertyChanged(nameof(IsPixelPerfectAvailable));
        }

        partial void OnBrushSizeChanged(int value)
        {
            BrushSize = System.Math.Clamp(value, 1, 64);
            OnPropertyChanged(nameof(IsPixelPerfectAvailable));
        }

        partial void OnBrushAngleChanged(int value)
        {
            BrushAngle = ((value % 360) + 360) % 360;
        }

        partial void OnFontSizeChanged(int value)
        {
            FontSize = System.Math.Clamp(value, 4, 144);
        }
    }
}
