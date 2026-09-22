namespace Hexprite.Core
{
    /// <summary>
    /// Pixel operations that can be applied to every frame selected in the timeline.
    /// </summary>
    public enum FrameBatchOperation
    {
        InvertActiveLayer,
        ClearActiveLayer,
        ShiftLeft,
        ShiftRight,
        ShiftUp,
        ShiftDown,
        FlipActiveLayerHorizontal,
        FlipActiveLayerVertical,
        ClearEntireFrame,
    }
}
