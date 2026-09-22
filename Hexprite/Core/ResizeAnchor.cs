namespace Hexprite.Core
{
    /// <summary>
    /// Specifies where existing pixel content is anchored when the canvas is resized.
    /// The anchor determines the origin point — content stays fixed at that position
    /// while the canvas grows or shrinks around it.
    /// </summary>
    public enum ResizeAnchor
    {
        TopLeft,
        TopCenter,
        TopRight,
        CenterLeft,
        Center,
        CenterRight,
        BottomLeft,
        BottomCenter,
        BottomRight,
    }
}
