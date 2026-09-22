namespace Hexprite.Core
{
    /// <summary>
    /// Controls which layer data is projected into code generation.
    /// The UI currently exposes only the default <see cref="CompositeVisible"/> behavior.
    /// </summary>
    public enum ExportLayerMode
    {
        /// <summary>
        /// Flatten all visible layers into one bitmap before code generation.
        /// </summary>
        CompositeVisible = 0,

        /// <summary>
        /// Export only the active layer pixels.
        /// Reserved for future UI exposure.
        /// </summary>
        ActiveLayerOnly = 1,

        /// <summary>
        /// Reserved future mode for multi-asset export.
        /// Not currently emitted by code generation.
        /// </summary>
        PerLayer = 2,
    }
}
