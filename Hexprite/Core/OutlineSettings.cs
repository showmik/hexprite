namespace Hexprite.Core
{
    /// <summary>
    /// Defines the shape kernel used for outline dilation/erosion.
    /// </summary>
    public enum OutlineShape
    {
        /// <summary>Circle-like outline using smart dilation (4-way + sharp-corner diagonals).</summary>
        Circle,
        /// <summary>Square outline using full 8-way dilation (all neighbors including diagonals).</summary>
        Square,
    }

    /// <summary>
    /// Defines whether the outline is placed outside or inside the existing shape.
    /// </summary>
    public enum OutlinePlacement
    {
        /// <summary>Outline is added around the existing pixels (expands outward).</summary>
        Outside,
        /// <summary>Outline is subtracted from existing pixels (erodes inward).</summary>
        Inside,
    }

    /// <summary>
    /// Holds all user-configurable settings for the outline operation.
    /// </summary>
    public sealed class OutlineSettings
    {
        /// <summary>Shape kernel for dilation/erosion.</summary>
        public OutlineShape Shape { get; set; } = OutlineShape.Circle;

        /// <summary>Gap in pixels between the original drawing and the outline ring (0–16).</summary>
        public int Padding { get; set; } = 1;

        /// <summary>Thickness of the outline ring in pixels (1–16).</summary>
        public int Thickness { get; set; } = 1;

        /// <summary>Whether to place the outline outside or inside the existing shape.</summary>
        public OutlinePlacement Placement { get; set; } = OutlinePlacement.Outside;
    }
}
