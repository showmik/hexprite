namespace Hexprite.Core
{
    /// <summary>
    /// Determines how floating selection pixels are applied to the canvas.
    /// </summary>
    public enum FloatingPasteMode
    {
        /// <summary>
        /// False pixels in the floating selection are skipped.
        /// Only true pixels overwrite the canvas beneath.
        /// Default - matches user intuition when moving drawings.
        /// </summary>
        Transparent,

        /// <summary>
        /// False pixels in the floating selection are written as false.
        /// Entire bounding box fully stamps onto the canvas.
        /// Use when deliberately stamping a solid rectangular block.
        /// </summary>
        Opaque,
    }
}
