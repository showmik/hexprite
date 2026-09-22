namespace Hexprite.Core
{
    /// <summary>
    /// Defines the available dither patterns for the Dither drawing tool.
    /// Patterns tile across absolute canvas coordinates so overlapping
    /// strokes produce consistent results.
    /// </summary>
    public enum DitherPattern
    {
        /// <summary>50% density — classic alternating on/off.</summary>
        Checkerboard,
        /// <summary>25% density — sparse dots.</summary>
        Light,
        /// <summary>75% density — mostly filled.</summary>
        Dense,
        /// <summary>45° stripe pattern.</summary>
        DiagonalLines,
        /// <summary>Crossing diagonal lines.</summary>
        CrossHatch,
    }
}
