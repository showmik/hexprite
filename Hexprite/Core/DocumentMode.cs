namespace Hexprite.Core
{
    /// <summary>
    /// Discriminator for the two workspace modes.
    /// Persisted in the .hexp file to determine which data model to deserialize.
    /// Existing files without this field default to <see cref="Sprite"/>.
    /// </summary>
    public enum DocumentMode
    {
        /// <summary>Standard pixel art / animation editing (current behavior).</summary>
        Sprite,

        /// <summary>Font glyph editing with per-character metrics and font export.</summary>
        Font,

        /// <summary>Flipper Zero dolphin animation asset pack manifest editor.</summary>
        AssetPack,
    }
}
