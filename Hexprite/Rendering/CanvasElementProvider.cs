using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace Hexprite.Rendering
{
    /// <summary>
    /// Groups the canvas UI element accessors needed by
    /// <see cref="BrushCursorManager"/> and <see cref="SelectionOverlayRenderer"/>.
    /// Replaces the fragile multi-Func constructor parameters with a single
    /// injectable object, making the wiring in MainWindow much cleaner.
    /// </summary>
    /// <remarks>Initializes a new provider with the required UI element accessors.</remarks>
    public class CanvasElementProvider(
        Func<Image?> getCanvasImage,
        Func<FrameworkElement> getPixelGridContainer,
        Func<Image?> getBrushCursorOverlay,
        Func<Line?> getCrosshairH,
        Func<Line?> getCrosshairV,
        Func<Rectangle?> getMarqueeOverlay,
        Func<Ellipse?> getEllipseOverlay,
        Func<Path?> getLassoOverlay,
        Func<Canvas?> getTransformHandlesLayer)
    {
        /// <summary>Accessor for the main canvas image element.</summary>
        public Func<Image?> GetCanvasImage { get; } = getCanvasImage ?? throw new ArgumentNullException(nameof(getCanvasImage));
        /// <summary>Accessor for the pixel grid container.</summary>
        public Func<FrameworkElement> GetPixelGridContainer { get; } = getPixelGridContainer ?? throw new ArgumentNullException(nameof(getPixelGridContainer));
        /// <summary>Accessor for the brush cursor overlay image.</summary>
        public Func<Image?> GetBrushCursorOverlay { get; } = getBrushCursorOverlay ?? throw new ArgumentNullException(nameof(getBrushCursorOverlay));
        /// <summary>Accessor for the crosshair horizontal line.</summary>
        public Func<Line?> GetCrosshairH { get; } = getCrosshairH ?? throw new ArgumentNullException(nameof(getCrosshairH));
        /// <summary>Accessor for the crosshair vertical line.</summary>
        public Func<Line?> GetCrosshairV { get; } = getCrosshairV ?? throw new ArgumentNullException(nameof(getCrosshairV));
        /// <summary>Accessor for the marquee overlay rectangle.</summary>
        public Func<Rectangle?> GetMarqueeOverlay { get; } = getMarqueeOverlay ?? throw new ArgumentNullException(nameof(getMarqueeOverlay));
        /// <summary>Accessor for the marquee overlay ellipse.</summary>
        public Func<Ellipse?> GetEllipseOverlay { get; } = getEllipseOverlay ?? throw new ArgumentNullException(nameof(getEllipseOverlay));
        /// <summary>Accessor for the lasso overlay path.</summary>
        public Func<Path?> GetLassoOverlay { get; } = getLassoOverlay ?? throw new ArgumentNullException(nameof(getLassoOverlay));
        /// <summary>Accessor for the canvas containing transformation handles.</summary>
        public Func<Canvas?> GetTransformHandlesLayer { get; } = getTransformHandlesLayer ?? throw new ArgumentNullException(nameof(getTransformHandlesLayer));
    }
}
