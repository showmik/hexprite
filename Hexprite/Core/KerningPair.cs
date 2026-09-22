namespace Hexprite.Core
{
    /// <summary>
    /// Defines a kerning adjustment between two consecutive characters.
    /// The <see cref="Adjustment"/> shifts the right character horizontally
    /// (negative = tighter, positive = looser).
    /// </summary>
    public class KerningPair
    {
        /// <summary>Left character code point.</summary>
        public int Left { get; set; }

        /// <summary>Right character code point.</summary>
        public int Right { get; set; }

        /// <summary>
        /// Horizontal adjustment in pixels.
        /// Negative values bring characters closer together (e.g., AV, To).
        /// </summary>
        public int Adjustment { get; set; }

        public KerningPair Clone() => new()
        {
            Left = Left,
            Right = Right,
            Adjustment = Adjustment,
        };
    }
}
