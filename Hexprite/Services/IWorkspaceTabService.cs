using System.Collections.Generic;
using Hexprite.Core;

namespace Hexprite.Services
{
    public interface IWorkspaceTabService
    {
        void OpenSpritesInTabs(IEnumerable<SpriteState> sprites, string tabNamePrefix = "Imported");
        void OpenSpritesInTabs(IEnumerable<(string Name, SpriteState Sprite)> sprites);
        void OpenSpritesInTabsWithPaths(IEnumerable<(string Name, SpriteState Sprite, string? FilePath)> sprites)
            => OpenSpritesInTabs(System.Linq.Enumerable.Select(sprites, s => (s.Name, s.Sprite)));
        void OpenSpritesInTabsWithPaths(IEnumerable<(string Name, SpriteState Sprite, string? FilePath, string? ParentPackPath, string? ParentPackName, string? PackEntryName)> sprites)
            => OpenSpritesInTabsWithPaths(System.Linq.Enumerable.Select(sprites, s => (s.Name, s.Sprite, s.FilePath)));
        void OpenSpriteInTab(SpriteState sprite, string title);
        void OpenSpriteInTab(SpriteState sprite, string title, string? filePath)
            => OpenSpriteInTab(sprite, title);
        void OpenSpriteInTab(SpriteState sprite, string title, string? filePath, string? parentPackPath, string? parentPackName, string? packEntryName)
            => OpenSpriteInTab(sprite, title, filePath);

        /// <summary>
        /// Retrieves the currently active sprite state from the active document, if any.
        /// </summary>
        SpriteState? GetActiveSpriteState();

        /// <summary>
        /// Retrieves the currently active sprite and its sanitized title from the active document, if any.
        /// </summary>
        (string Title, SpriteState Sprite)? GetActiveSprite();

        /// <summary>
        /// Gets the current frame pixels (monochrome 128x64) from the active canvas document.
        /// </summary>
        bool[]? GetActiveFramePixels(bool animated = false);

        /// <summary>
        /// Gets all sprite states from currently open workspace tabs.
        /// </summary>
        IReadOnlyList<(string Title, SpriteState Sprite)> GetAllOpenSprites();

        /// <summary>
        /// Gets all sprite states along with their document file paths (if any) from currently open workspace tabs.
        /// </summary>
        IReadOnlyList<(string Title, SpriteState Sprite, string? FilePath)> GetAllOpenSpritesWithPaths()
            => System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(GetAllOpenSprites(), s => (s.Title, s.Sprite, (string?)null)));

        /// <summary>
        /// Activates (focuses) the workspace tab whose title matches the given name.
        /// Returns true if the tab was found and activated.
        /// </summary>
        bool ActivateTabByTitle(string title);

        /// <summary>
        /// Renames an open workspace tab matching oldTitle to newTitle.
        /// Returns true if a matching tab was found and renamed.
        /// </summary>
        bool RenameTab(string oldTitle, string newTitle) => false;

        /// <summary>
        /// Opens or focuses an Asset Pack (Schedule Matrix) document tab in the workspace.
        /// </summary>
        void OpenAssetPackInTab(
            IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>? pack = null,
            string packName = "Flipper Asset Pack");

        /// <summary>
        /// Opens a Font Editor document tab in the workspace.
        /// </summary>
        void OpenFontInTab(string fontName, int glyphWidth = 8, int glyphHeight = 8) { }
    }
}
