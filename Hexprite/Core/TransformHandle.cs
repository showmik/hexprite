namespace Hexprite.Core
{
    /// <summary>
    /// Represents the available handles for interacting with a floating selection transformation (resize/rotate).
    /// </summary>
    public enum TransformHandle
    {
        /// <summary>No active transformation handle.</summary>
        None,
        /// <summary>North-West resize handle.</summary>
        NW,
        /// <summary>North resize handle.</summary>
        N,
        /// <summary>North-East resize handle.</summary>
        NE,
        /// <summary>East resize handle.</summary>
        E,
        /// <summary>South-East resize handle.</summary>
        SE,
        /// <summary>South resize handle.</summary>
        S,
        /// <summary>South-West resize handle.</summary>
        SW,
        /// <summary>West resize handle.</summary>
        W,
        /// <summary>Rotation handle.</summary>
        Rotate,
    }
}
