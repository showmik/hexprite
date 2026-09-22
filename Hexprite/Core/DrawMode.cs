namespace Hexprite.Core
{
    /// <summary>
    /// Represents the active tool action state for processing input events.
    /// </summary>
    public enum DrawMode
    {
        /// <summary>No active drawing or erasing action.</summary>
        None,
        /// <summary>Active drawing operation.</summary>
        Draw,
        /// <summary>Active erasing operation.</summary>
        Erase,
    }
}
