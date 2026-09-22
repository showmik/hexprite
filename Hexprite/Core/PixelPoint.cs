namespace Hexprite.Core
{
    /// <summary>
    /// A platform-agnostic pixel coordinate. Replaces System.Windows.Point in all
    /// selection and drawing logic so those code paths stay portable.
    /// </summary>
    /// <remarks>Initializes a new pixel point.</remarks>
    public readonly struct PixelPoint(int x, int y)
    {
        /// <summary>The X coordinate.</summary>
        public int X { get; } = x;         /// <summary>The Y coordinate.</summary>
        public int Y { get; } = y;

        /// <summary>Tests equality with another point.</summary>
        public bool Equals(PixelPoint other) => X == other.X && Y == other.Y;
        /// <summary>Returns string representation.</summary>
        public override string ToString() => $"({X}, {Y})";
    }
}
