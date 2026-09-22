using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Hexprite.Services;

namespace Hexprite.ViewModels
{
    /// <summary>
    /// Represents an individual keyboard shortcut item for display in the cheat sheet.
    /// </summary>
    public sealed class ShortcutItemViewModel
    {
        public string ActionId { get; }
        public string DisplayName { get; }
        public string Category { get; }
        public string Description { get; }
        public string KeyGesture { get; }
        public IReadOnlyList<string> KeyBadges { get; }
        public ShortcutScope Scope { get; }

        public ShortcutItemViewModel(
            string actionId,
            string displayName,
            string category,
            string description,
            string keyGesture,
            IReadOnlyList<string> keyBadges,
            ShortcutScope scope)
        {
            ActionId = actionId;
            DisplayName = displayName;
            Category = category;
            Description = description;
            KeyGesture = keyGesture;
            KeyBadges = keyBadges;
            Scope = scope;
        }
    }

    /// <summary>
    /// Represents a categorized group of shortcuts in the cheat sheet.
    /// </summary>
    public sealed class ShortcutCategoryGroup
    {
        public string CategoryName { get; }
        public string CategoryIcon { get; }
        public ObservableCollection<ShortcutItemViewModel> Items { get; }

        public ShortcutCategoryGroup(string categoryName, string categoryIcon, IEnumerable<ShortcutItemViewModel> items)
        {
            CategoryName = categoryName;
            CategoryIcon = categoryIcon;
            Items = new ObservableCollection<ShortcutItemViewModel>(items);
        }
    }

    /// <summary>
    /// ViewModel for the Keyboard Shortcuts Cheat Sheet overlay/dialog.
    /// </summary>
    public sealed partial class KeyboardShortcutsViewModel : ObservableObject
    {
        private readonly IHexpriteShortcutManager? _shortcutManager;
        private readonly List<ShortcutItemViewModel> _allShortcuts = new();

        [ObservableProperty]
        private string _searchText = string.Empty;

        public ObservableCollection<ShortcutCategoryGroup> FilteredGroups { get; } = new();

        public int TotalShortcutCount => _allShortcuts.Count;

        public bool HasResults => FilteredGroups.Count > 0;
        public bool HasNoResults => FilteredGroups.Count == 0;
        public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);
        public bool IsSearchEmpty => string.IsNullOrWhiteSpace(SearchText);

        public KeyboardShortcutsViewModel(IHexpriteShortcutManager? shortcutManager = null)
        {
            _shortcutManager = shortcutManager;
            LoadShortcuts();
            ApplyFilter();
        }

        partial void OnSearchTextChanged(string value)
        {
            ApplyFilter();
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(HasNoResults));
            OnPropertyChanged(nameof(HasSearchText));
            OnPropertyChanged(nameof(IsSearchEmpty));
        }

        private void LoadShortcuts()
        {
            _allShortcuts.Clear();

            // Metadata dictionary describing known Hexprite actions
            var metadata = KnownActionMetadata;

            // 1. Gather shortcuts from the live shortcut manager if available
            var liveShortcuts = _shortcutManager?.Shortcuts ?? Array.Empty<ShortcutDefinition>();
            var processedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var def in liveShortcuts)
            {
                // Skip debug tracer bullet from cheat sheet display
                if (def.ActionId.Equals(HexpriteShortcutManager.TracerBulletActionId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (metadata.TryGetValue(def.ActionId, out var meta))
                {
                    var badges = FormatKeyBadges(def.Key, def.ModifierKeys);
                    var gestureText = string.Join(" + ", badges);

                    _allShortcuts.Add(new ShortcutItemViewModel(
                        def.ActionId,
                        meta.DisplayName,
                        meta.Category,
                        meta.Description,
                        gestureText,
                        badges,
                        def.Scope));

                    processedActions.Add(def.ActionId);
                }
            }

            // 2. Fallback: if shortcut manager was null or had unregistered actions from metadata, load defaults
            foreach (var (actionId, meta) in metadata)
            {
                if (!processedActions.Contains(actionId))
                {
                    var badges = meta.DefaultBadges;
                    var gestureText = string.Join(" + ", badges);

                    _allShortcuts.Add(new ShortcutItemViewModel(
                        actionId,
                        meta.DisplayName,
                        meta.Category,
                        meta.Description,
                        gestureText,
                        badges,
                        meta.DefaultScope));
                }
            }
        }

        private void ApplyFilter()
        {
            FilteredGroups.Clear();

            var query = SearchText?.Trim() ?? string.Empty;
            IEnumerable<ShortcutItemViewModel> matched = _allShortcuts;

            if (!string.IsNullOrWhiteSpace(query))
            {
                string compactQuery = query.Replace(" ", string.Empty, StringComparison.Ordinal);
                matched = _allShortcuts.Where(s =>
                    s.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    s.KeyGesture.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(compactQuery) && s.KeyGesture.Replace(" ", string.Empty, StringComparison.Ordinal).Contains(compactQuery, StringComparison.OrdinalIgnoreCase)) ||
                    s.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    s.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    s.KeyBadges.Any(b => b.Contains(query, StringComparison.OrdinalIgnoreCase)));
            }

            var groups = matched
                .GroupBy(s => s.Category)
                .OrderBy(g =>
                {
                    int idx = Array.IndexOf(CategoryOrder, g.Key);
                    return idx >= 0 ? idx : 99;
                });

            foreach (var g in groups)
            {
                string icon = GetCategoryIcon(g.Key);
                FilteredGroups.Add(new ShortcutCategoryGroup(g.Key, icon, g));
            }

            OnPropertyChanged(nameof(HasResults));
        }

        public static IReadOnlyList<string> FormatKeyBadges(Key key, ModifierKeys modifiers)
        {
            var badges = new List<string>();

            if (modifiers.HasFlag(ModifierKeys.Control))
            {
                badges.Add("Ctrl");
            }

            if (modifiers.HasFlag(ModifierKeys.Alt))
            {
                badges.Add("Alt");
            }

            if (modifiers.HasFlag(ModifierKeys.Shift))
            {
                badges.Add("Shift");
            }

            if (modifiers.HasFlag(ModifierKeys.Windows))
            {
                badges.Add("Win");
            }

            string keyText = key switch
            {
                Key.OemOpenBrackets => "[",
                Key.OemCloseBrackets => "]",
                Key.OemComma => ",",
                Key.OemPeriod => ".",
                Key.OemQuestion => "/",
                Key.Divide => "Num /",
                Key.OemPlus => "+",
                Key.Add => "Num +",
                Key.OemMinus => "-",
                Key.Subtract => "Num -",
                Key.D0 => "0",
                Key.NumPad0 => "Num 0",
                Key.Space => "Space",
                Key.Delete => "Del",
                Key.Back => "Backspace",
                Key.Up => "↑",
                Key.Down => "↓",
                Key.Left => "←",
                Key.Right => "→",
                Key.PageUp => "Page Up",
                Key.PageDown => "Page Down",
                Key.Home => "Home",
                Key.End => "End",
                Key.Escape => "Esc",
                Key.Return => "Enter",
                _ => key.ToString()
            };

            badges.Add(keyText);
            return badges.AsReadOnly();
        }

        private static string GetCategoryIcon(string category) => category switch
        {
            "Tools" => "🎨",
            "Canvas & Drawing" => "🖌️",
            "Animation" => "⏱️",
            "Edit" => "✂️",
            "Layers" => "📑",
            "View & Zoom" => "🔍",
            "File" => "📄",
            "Utilities" => "⚙️",
            _ => "⌨️"
        };

        private record ActionMeta(
            string DisplayName,
            string Category,
            string Description,
            IReadOnlyList<string> DefaultBadges,
            ShortcutScope DefaultScope);

        private static readonly string[] CategoryOrder =
        {
            "Tools",
            "Canvas & Drawing",
            "Animation",
            "Edit",
            "Layers",
            "View & Zoom",
            "File",
            "Utilities"
        };

        internal static IReadOnlyCollection<string> KnownActionIds => KnownActionMetadata.Keys;

        private static readonly Dictionary<string, ActionMeta> KnownActionMetadata = new(StringComparer.OrdinalIgnoreCase)
        {
                // ── Tools ──────────────────────────────────────────────────────────
                ["Tool.Pencil"] = new("Pencil Tool", "Tools", "Select pencil drawing tool", new[] { "B" }, ShortcutScope.EditorCanvas),
                ["Tool.Eraser"] = new("Eraser Tool", "Tools", "Select eraser tool", new[] { "E" }, ShortcutScope.EditorCanvas),
                ["Tool.Line"] = new("Line Tool", "Tools", "Select straight line tool", new[] { "L" }, ShortcutScope.EditorCanvas),
                ["Tool.Rectangle"] = new("Rectangle Tool", "Tools", "Select hollow rectangle tool", new[] { "R" }, ShortcutScope.EditorCanvas),
                ["Tool.FilledRectangle"] = new("Filled Rectangle Tool", "Tools", "Select filled rectangle tool", new[] { "Shift", "R" }, ShortcutScope.EditorCanvas),
                ["Tool.Ellipse"] = new("Ellipse Tool", "Tools", "Select hollow ellipse tool", new[] { "C" }, ShortcutScope.EditorCanvas),
                ["Tool.FilledEllipse"] = new("Filled Ellipse Tool", "Tools", "Select filled ellipse tool", new[] { "Shift", "C" }, ShortcutScope.EditorCanvas),
                ["Tool.Fill"] = new("Fill Bucket Tool", "Tools", "Flood fill contiguous area", new[] { "F" }, ShortcutScope.EditorCanvas),
                ["Tool.Gradient"] = new("Gradient Tool", "Tools", "Dithered gradient fill tool", new[] { "G" }, ShortcutScope.EditorCanvas),
                ["Tool.Text"] = new("Text Tool", "Tools", "Render bitmap typography", new[] { "T" }, ShortcutScope.EditorCanvas),
                ["Tool.Move"] = new("Move Tool", "Tools", "Move layer or floating selection", new[] { "V" }, ShortcutScope.EditorCanvas),
                ["Tool.Marquee"] = new("Marquee Select", "Tools", "Rectangular pixel selection", new[] { "M" }, ShortcutScope.EditorCanvas),
                ["Tool.EllipticalMarquee"] = new("Elliptical Marquee Select", "Tools", "Elliptical pixel selection", new[] { "Shift", "M" }, ShortcutScope.EditorCanvas),
                ["Tool.Lasso"] = new("Lasso Select", "Tools", "Freeform polygon pixel selection", new[] { "Q" }, ShortcutScope.EditorCanvas),
                ["Tool.MagicWand"] = new("Magic Wand Select", "Tools", "Select contiguous matching color", new[] { "W" }, ShortcutScope.EditorCanvas),
                ["Tool.Dither"] = new("Dither Tool", "Tools", "Checkerboard pattern brush", new[] { "D" }, ShortcutScope.EditorCanvas),

                // ── Canvas & Drawing ────────────────────────────────────────────────
                ["Canvas.BrushSize.Decrement"] = new("Decrease Brush Size", "Canvas & Drawing", "Reduce brush diameter by 1px", new[] { "[" }, ShortcutScope.EditorCanvas),
                ["Canvas.BrushSize.Increment"] = new("Increase Brush Size", "Canvas & Drawing", "Enlarge brush diameter by 1px", new[] { "]" }, ShortcutScope.EditorCanvas),
                ["Canvas.DeleteSelection"] = new("Delete Selection", "Canvas & Drawing", "Clear pixels within active selection", new[] { "Del" }, ShortcutScope.EditorCanvas),
                ["Canvas.DeleteSelection.Backspace"] = new("Delete Selection (Backspace)", "Canvas & Drawing", "Clear pixels within active selection", new[] { "Backspace" }, ShortcutScope.EditorCanvas),
                ["Canvas.Selection.NudgeUp"] = new("Nudge Selection Up", "Canvas & Drawing", "Nudge selection 1px up", new[] { "↑" }, ShortcutScope.EditorCanvas),
                ["Canvas.Selection.NudgeDown"] = new("Nudge Selection Down", "Canvas & Drawing", "Nudge selection 1px down", new[] { "↓" }, ShortcutScope.EditorCanvas),
                ["Canvas.Selection.NudgeLeft"] = new("Nudge Selection Left", "Canvas & Drawing", "Nudge selection 1px left", new[] { "←" }, ShortcutScope.EditorCanvas),
                ["Canvas.Selection.NudgeRight"] = new("Nudge Selection Right", "Canvas & Drawing", "Nudge selection 1px right", new[] { "→" }, ShortcutScope.EditorCanvas),
                ["Canvas.Selection.NudgeUpFast"] = new("Nudge Selection Up (Fast)", "Canvas & Drawing", "Nudge selection 10px up", new[] { "Shift", "↑" }, ShortcutScope.EditorCanvas),
                ["Canvas.Selection.NudgeDownFast"] = new("Nudge Selection Down (Fast)", "Canvas & Drawing", "Nudge selection 10px down", new[] { "Shift", "↓" }, ShortcutScope.EditorCanvas),
                ["Canvas.Selection.NudgeLeftFast"] = new("Nudge Selection Left (Fast)", "Canvas & Drawing", "Nudge selection 10px left", new[] { "Shift", "←" }, ShortcutScope.EditorCanvas),
                ["Canvas.Selection.NudgeRightFast"] = new("Nudge Selection Right (Fast)", "Canvas & Drawing", "Nudge selection 10px right", new[] { "Shift", "→" }, ShortcutScope.EditorCanvas),
                ["Canvas.GridShift.Up"] = new("Shift Canvas Up", "Canvas & Drawing", "Nudge canvas pixels up by 1 grid unit", new[] { "Ctrl", "↑" }, ShortcutScope.EditorCanvas),
                ["Canvas.GridShift.Down"] = new("Shift Canvas Down", "Canvas & Drawing", "Nudge canvas pixels down by 1 grid unit", new[] { "Ctrl", "↓" }, ShortcutScope.EditorCanvas),
                ["Canvas.GridShift.Left"] = new("Shift Canvas Left", "Canvas & Drawing", "Nudge canvas pixels left by 1 grid unit", new[] { "Ctrl", "←" }, ShortcutScope.EditorCanvas),
                ["Canvas.GridShift.Right"] = new("Shift Canvas Right", "Canvas & Drawing", "Nudge canvas pixels right by 1 grid unit", new[] { "Ctrl", "→" }, ShortcutScope.EditorCanvas),

                // ── Animation ───────────────────────────────────────────────────────
                ["Animation.TogglePlayback"] = new("Play / Pause Animation", "Animation", "Toggle timeline animation playback", new[] { "Space" }, ShortcutScope.EditorCanvas),
                ["Animation.PreviousFrame"] = new("Previous Frame", "Animation", "Navigate to previous animation frame", new[] { "," }, ShortcutScope.EditorCanvas),
                ["Animation.NextFrame"] = new("Next Frame", "Animation", "Navigate to next animation frame", new[] { "." }, ShortcutScope.EditorCanvas),
                ["Animation.FirstFrame"] = new("First Frame", "Animation", "Jump to first animation frame", new[] { "Home" }, ShortcutScope.EditorCanvas),
                ["Animation.LastFrame"] = new("Last Frame", "Animation", "Jump to last animation frame", new[] { "End" }, ShortcutScope.EditorCanvas),
                ["Animation.ToggleOnionSkin"] = new("Toggle Onion Skin", "Animation", "Show previous frame ghosting", new[] { "O" }, ShortcutScope.EditorCanvas),
                ["Animation.AddFrame"] = new("Add Animation Frame", "Animation", "Insert a new blank animation frame", new[] { "Alt", "N" }, ShortcutScope.EditorCanvas),

                // ── Edit Operations ────────────────────────────────────────────────
                ["Edit.Undo"] = new("Undo", "Edit", "Revert last canvas or document change", new[] { "Ctrl", "Z" }, ShortcutScope.Window),
                ["Edit.Redo"] = new("Redo", "Edit", "Re-apply reverted change", new[] { "Ctrl", "Y" }, ShortcutScope.Window),
                ["Edit.Redo.CtrlShiftZ"] = new("Redo (Ctrl+Shift+Z)", "Edit", "Universal modern redo shortcut", new[] { "Ctrl", "Shift", "Z" }, ShortcutScope.Window),
                ["Edit.Cut"] = new("Cut", "Edit", "Cut selection to pixel clipboard", new[] { "Ctrl", "X" }, ShortcutScope.Window),
                ["Edit.Copy"] = new("Copy", "Edit", "Copy selection to pixel clipboard", new[] { "Ctrl", "C" }, ShortcutScope.Window),
                ["Edit.Paste"] = new("Paste", "Edit", "Paste clipboard pixels to active canvas", new[] { "Ctrl", "V" }, ShortcutScope.Window),
                ["Edit.SelectAll"] = new("Select All", "Edit", "Select entire active canvas bounds", new[] { "Ctrl", "A" }, ShortcutScope.Window),
                ["Edit.Deselect"] = new("Deselect", "Edit", "Clear active selection boundary", new[] { "Ctrl", "D" }, ShortcutScope.Window),
                ["Edit.Reselect"] = new("Reselect", "Edit", "Restore previous selection mask", new[] { "Ctrl", "Shift", "D" }, ShortcutScope.Window),
                ["Edit.Transform"] = new("Transform Selection", "Edit", "Begin freeform pixel transform", new[] { "Ctrl", "T" }, ShortcutScope.Window),
                ["Edit.MergeLayer"] = new("Merge Layer Down", "Edit", "Combine active layer with layer below", new[] { "Ctrl", "E" }, ShortcutScope.Window),
                ["Edit.Invert"] = new("Invert Colors", "Edit", "Invert black/white pixels in canvas or selection", new[] { "Ctrl", "I" }, ShortcutScope.Window),

                // ── Layers ──────────────────────────────────────────────────────────
                ["Layer.Add"] = new("New Layer", "Layers", "Add a new drawing layer", new[] { "Ctrl", "Shift", "N" }, ShortcutScope.Window),
                ["Layer.Duplicate"] = new("Duplicate Layer", "Layers", "Duplicate active layer", new[] { "Ctrl", "J" }, ShortcutScope.Window),
                ["Layer.NewFromSelection"] = new("New Layer from Selection", "Layers", "Create a new layer containing selected pixels", new[] { "Ctrl", "Shift", "J" }, ShortcutScope.Window),
                ["Layer.Delete"] = new("Delete Layer", "Layers", "Delete active layer", new[] { "Ctrl", "Shift", "Del" }, ShortcutScope.Window),
                ["Layer.Rename"] = new("Rename Layer", "Layers", "Rename active layer", new[] { "F2" }, ShortcutScope.Window),

                // ── View & Zoom ─────────────────────────────────────────────────────
                ["View.ZoomIn.OemPlus"] = new("Zoom In", "View & Zoom", "Zoom in towards viewport center", new[] { "Ctrl", "+" }, ShortcutScope.Window),
                ["View.ZoomIn.ShiftOemPlus"] = new("Zoom In (Shift)", "View & Zoom", "Zoom in (Ctrl + Shift + =)", new[] { "Ctrl", "Shift", "+" }, ShortcutScope.Window),
                ["View.ZoomIn.Add"] = new("Zoom In (Numpad)", "View & Zoom", "Zoom in using numeric keypad", new[] { "Ctrl", "Num +" }, ShortcutScope.Window),
                ["View.ZoomOut.OemMinus"] = new("Zoom Out", "View & Zoom", "Zoom out from viewport center", new[] { "Ctrl", "-" }, ShortcutScope.Window),
                ["View.ZoomOut.Subtract"] = new("Zoom Out (Numpad)", "View & Zoom", "Zoom out using numeric keypad", new[] { "Ctrl", "Num -" }, ShortcutScope.Window),
                ["View.ZoomReset.D0"] = new("Reset Zoom", "View & Zoom", "Reset zoom scale to 100%", new[] { "Ctrl", "0" }, ShortcutScope.Window),
                ["View.ZoomReset.NumPad0"] = new("Reset Zoom (Numpad)", "View & Zoom", "Reset zoom scale to 100%", new[] { "Ctrl", "Num 0" }, ShortcutScope.Window),

                // ── File Operations ────────────────────────────────────────────────
                ["File.New"] = new("New Document", "File", "Create a new sprite document", new[] { "Ctrl", "N" }, ShortcutScope.Window),
                ["File.Open"] = new("Open Document", "File", "Open existing sprite file", new[] { "Ctrl", "O" }, ShortcutScope.Window),
                ["File.Save"] = new("Save Document", "File", "Save active document", new[] { "Ctrl", "S" }, ShortcutScope.Window),
                ["File.SaveAs"] = new("Save As", "File", "Save active document under new name", new[] { "Ctrl", "Shift", "S" }, ShortcutScope.Window),
                ["File.CloseTab"] = new("Close Tab", "File", "Close currently active document tab", new[] { "Ctrl", "W" }, ShortcutScope.Window),

                // ── Utilities ───────────────────────────────────────────────────────
                ["Help.KeyboardShortcuts"] = new("Keyboard Shortcuts", "Utilities", "Open keyboard shortcut cheat sheet", new[] { "Ctrl", "/" }, ShortcutScope.Global),
                ["Help.KeyboardShortcuts.NumPad"] = new("Keyboard Shortcuts (Numpad)", "Utilities", "Open keyboard shortcut cheat sheet", new[] { "Ctrl", "Num /" }, ShortcutScope.Global),
                ["Utility.RefreshTheme"] = new("Refresh Theme", "Utilities", "Reload active UI theme resources", new[] { "F5" }, ShortcutScope.Window),
                ["Utility.OpenDocumentation"] = new("Documentation", "Utilities", "Open Hexprite online manual", new[] { "F1" }, ShortcutScope.Global),
            };
    }
}
