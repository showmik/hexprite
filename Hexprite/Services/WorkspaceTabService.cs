using System;
using System.Collections.Generic;
using Hexprite.Core;
using Hexprite.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Hexprite.Services
{
    /// <summary>
    /// Service that coordinates opening imported sprites and accessing active document canvas frames across multi-document tabs.
    /// Delegates to the application ShellViewModel.
    /// </summary>
    public class WorkspaceTabService : IWorkspaceTabService
    {
        private readonly IServiceProvider? _serviceProvider;
        private ShellViewModel? _shellViewModel;

        public WorkspaceTabService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public WorkspaceTabService(ShellViewModel shellViewModel)
        {
            _shellViewModel = shellViewModel ?? throw new ArgumentNullException(nameof(shellViewModel));
        }

        private ShellViewModel? ResolveShell()
        {
            if (_shellViewModel != null) return _shellViewModel;
            if (_serviceProvider != null)
            {
                _shellViewModel = _serviceProvider.GetService<ShellViewModel>();
                if (_shellViewModel != null) return _shellViewModel;
            }
            if (System.Windows.Application.Current?.MainWindow?.DataContext is ShellViewModel svm)
            {
                _shellViewModel = svm;
                return _shellViewModel;
            }
            return null;
        }

        public void OpenSpritesInTabs(IEnumerable<SpriteState> sprites, string tabNamePrefix = "Imported")
        {
            ResolveShell()?.OpenSpritesInTabs(sprites, tabNamePrefix);
        }

        public void OpenSpritesInTabs(IEnumerable<(string Name, SpriteState Sprite)> sprites)
        {
            ResolveShell()?.OpenSpritesInTabs(sprites);
        }

        public void OpenSpritesInTabsWithPaths(IEnumerable<(string Name, SpriteState Sprite, string? FilePath)> sprites)
        {
            ResolveShell()?.OpenSpritesInTabsWithPaths(sprites);
        }

        public void OpenSpritesInTabsWithPaths(IEnumerable<(string Name, SpriteState Sprite, string? FilePath, string? ParentPackPath, string? ParentPackName, string? PackEntryName)> sprites)
        {
            ResolveShell()?.OpenSpritesInTabsWithPaths(sprites);
        }

        public void OpenSpriteInTab(SpriteState sprite, string title)
        {
            ResolveShell()?.OpenSpriteInTab(sprite, title, null);
        }

        public void OpenSpriteInTab(SpriteState sprite, string title, string? filePath)
        {
            ResolveShell()?.OpenSpriteInTab(sprite, title, filePath);
        }

        public void OpenSpriteInTab(SpriteState sprite, string title, string? filePath, string? parentPackPath, string? parentPackName, string? packEntryName)
        {
            ResolveShell()?.OpenSpriteInTab(sprite, title, filePath, parentPackPath, parentPackName, packEntryName);
        }

        public SpriteState? GetActiveSpriteState()
        {
            return ResolveShell()?.GetActiveSpriteState();
        }

        public (string Title, SpriteState Sprite)? GetActiveSprite()
        {
            return ResolveShell()?.GetActiveSprite();
        }

        public bool[]? GetActiveFramePixels(bool animated = false)
        {
            return ResolveShell()?.GetActiveFramePixels(animated);
        }

        public IReadOnlyList<(string Title, SpriteState Sprite)> GetAllOpenSprites()
        {
            var shell = ResolveShell();
            if (shell == null) return [];
            var list = new List<(string Title, SpriteState Sprite)>();
            foreach (var doc in shell.OpenDocuments)
            {
                if (doc is MainViewModel mvm && mvm.SpriteState != null)
                {
                    string title = string.IsNullOrWhiteSpace(doc.Title) ? "Animation" : doc.Title.Trim();
                    title = title.TrimStart('*').Trim();
                    if (title.EndsWith(".hexel", StringComparison.OrdinalIgnoreCase))
                        title = title[..^6];
                    if (title.EndsWith(".hexp", StringComparison.OrdinalIgnoreCase))
                        title = title[..^5];
                    if (string.IsNullOrWhiteSpace(title))
                        title = "Animation";
                    list.Add((title, mvm.SpriteState));
                }
            }
            return list;
        }

        public IReadOnlyList<(string Title, SpriteState Sprite, string? FilePath)> GetAllOpenSpritesWithPaths()
        {
            var shell = ResolveShell();
            if (shell == null) return [];
            var list = new List<(string Title, SpriteState Sprite, string? FilePath)>();
            foreach (var doc in shell.OpenDocuments)
            {
                if (doc is MainViewModel mvm && mvm.SpriteState != null)
                {
                    string title = string.IsNullOrWhiteSpace(doc.Title) ? "Animation" : doc.Title.Trim();
                    title = title.TrimStart('*').Trim();
                    if (title.EndsWith(".hexel", StringComparison.OrdinalIgnoreCase))
                        title = title[..^6];
                    if (title.EndsWith(".hexp", StringComparison.OrdinalIgnoreCase))
                        title = title[..^5];
                    if (string.IsNullOrWhiteSpace(title))
                        title = "Animation";
                    list.Add((title, mvm.SpriteState, mvm.FilePath));
                }
            }
            return list;
        }

        public bool ActivateTabByTitle(string title)
        {
            var shell = ResolveShell();
            if (shell == null || string.IsNullOrWhiteSpace(title)) return false;

            string cleanTarget = title.Trim().TrimStart('*').Trim();

            foreach (var doc in shell.OpenDocuments)
            {
                string docTitle = string.IsNullOrWhiteSpace(doc.Title) ? string.Empty : doc.Title.Trim().TrimStart('*').Trim();
                if (docTitle.EndsWith(".hexel", StringComparison.OrdinalIgnoreCase))
                    docTitle = docTitle[..^6];
                if (docTitle.EndsWith(".hexp", StringComparison.OrdinalIgnoreCase))
                    docTitle = docTitle[..^5];

                if (string.Equals(docTitle, cleanTarget, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(doc.Title?.Trim().TrimStart('*').Trim(), cleanTarget, StringComparison.OrdinalIgnoreCase))
                {
                    shell.ActiveDocument = doc;
                    return true;
                }
            }
            return false;
        }

        public bool RenameTab(string oldTitle, string newTitle)
        {
            return ResolveShell()?.RenameTab(oldTitle, newTitle) ?? false;
        }

        public void OpenAssetPackInTab(
            IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>? pack = null,
            string packName = "Flipper Asset Pack")
        {
            ResolveShell()?.OpenAssetPackInTab(pack, packName);
        }

        public void OpenFontInTab(string fontName, int glyphWidth = 8, int glyphHeight = 8)
        {
            ResolveShell()?.OpenFontInTab(fontName, glyphWidth, glyphHeight);
        }
    }
}
