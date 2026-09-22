using System;
using System.Linq;
using System.Windows.Input;
using Hexprite.Services;
using Hexprite.ViewModels;
using Xunit;

namespace Hexprite.Tests;

[Collection("WindowLayoutSettingsFile")]
[Trait("Category", "Unit")]
public class KeyboardShortcutsViewModelTests
{
    [Fact]
    public void Constructor_WithNullManager_LoadsFallbackMetadataShortcuts()
    {
        var vm = new KeyboardShortcutsViewModel(shortcutManager: null);

        Assert.True(vm.TotalShortcutCount > 0);
        Assert.True(vm.FilteredGroups.Count > 0);
        Assert.True(vm.HasResults);

        // Verify key categories exist
        var categoryNames = vm.FilteredGroups.Select(g => g.CategoryName).ToList();
        Assert.Contains("Tools", categoryNames);
        Assert.Contains("Canvas & Drawing", categoryNames);
        Assert.Contains("Animation", categoryNames);
        Assert.Contains("Edit", categoryNames);
        Assert.Contains("Layers", categoryNames);
        Assert.Contains("View & Zoom", categoryNames);
        Assert.Contains("File", categoryNames);
        Assert.Contains("Utilities", categoryNames);
    }

    [Fact]
    public void Constructor_WithLiveShortcutManager_PopulatesLiveBindingsAndExcludesTracerBullet()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: true);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();

        EditorCanvasShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);
        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        var vm = new KeyboardShortcutsViewModel(manager);

        Assert.True(vm.TotalShortcutCount > 0);
        Assert.True(vm.HasResults);

        // Ensure tracer bullet is excluded from cheat sheet
        var allItems = vm.FilteredGroups.SelectMany(g => g.Items).ToList();
        Assert.DoesNotContain(allItems, item => item.ActionId.Equals(HexpriteShortcutManager.TracerBulletActionId, StringComparison.OrdinalIgnoreCase));

        // Check specific known shortcuts
        var pencilItem = allItems.FirstOrDefault(i => i.ActionId == "Tool.Pencil");
        Assert.NotNull(pencilItem);
        Assert.Equal("Pencil Tool", pencilItem.DisplayName);
        Assert.Equal("Tools", pencilItem.Category);
        Assert.Equal("B", pencilItem.KeyGesture);
        Assert.Contains("B", pencilItem.KeyBadges);

        var undoItem = allItems.FirstOrDefault(i => i.ActionId == "Edit.Undo");
        Assert.NotNull(undoItem);
        Assert.Equal("Ctrl + Z", undoItem.KeyGesture);

        var redoModernItem = allItems.FirstOrDefault(i => i.ActionId == "Edit.Redo.CtrlShiftZ");
        Assert.NotNull(redoModernItem);
        Assert.Equal("Ctrl + Shift + Z", redoModernItem.KeyGesture);

        var shortcutsCheatSheet = allItems.FirstOrDefault(i => i.ActionId == "Help.KeyboardShortcuts");
        Assert.NotNull(shortcutsCheatSheet);
        Assert.Equal("Ctrl + /", shortcutsCheatSheet.KeyGesture);
    }

    [Theory]
    [InlineData("pencil", "Tool.Pencil")]
    [InlineData("eraser", "Tool.Eraser")]
    [InlineData("gradient", "Tool.Gradient")]
    [InlineData("marquee", "Tool.Marquee")]
    public void SearchFilter_ByToolName_FiltersCorrectly(string query, string expectedActionId)
    {
        var vm = new KeyboardShortcutsViewModel();

        vm.SearchText = query;

        Assert.True(vm.HasResults);
        var matchedItems = vm.FilteredGroups.SelectMany(g => g.Items).ToList();
        Assert.Contains(matchedItems, item => item.ActionId == expectedActionId);
    }

    [Fact]
    public void SearchFilter_ByKeyGesture_FiltersCorrectly()
    {
        var vm = new KeyboardShortcutsViewModel();

        vm.SearchText = "ctrl+z";

        Assert.True(vm.HasResults);
        var matchedItems = vm.FilteredGroups.SelectMany(g => g.Items).ToList();
        Assert.Contains(matchedItems, item => item.ActionId == "Edit.Undo" || item.ActionId == "Edit.Redo.CtrlShiftZ");
    }

    [Fact]
    public void SearchFilter_ByCategoryName_FiltersCorrectly()
    {
        var vm = new KeyboardShortcutsViewModel();

        vm.SearchText = "Layers";

        Assert.True(vm.HasResults);
        Assert.All(vm.FilteredGroups, g => Assert.Equal("Layers", g.CategoryName));
    }

    [Fact]
    public void SearchFilter_NoMatch_SetsHasResultsFalseAndEmptyGroups()
    {
        var vm = new KeyboardShortcutsViewModel();

        vm.SearchText = "NonExistentFeatureXYZ987";

        Assert.False(vm.HasResults);
        Assert.Empty(vm.FilteredGroups);
    }

    [Fact]
    public void SearchFilter_ClearingSearchText_RestoresAllShortcuts()
    {
        var vm = new KeyboardShortcutsViewModel();
        int initialTotal = vm.TotalShortcutCount;

        vm.SearchText = "pencil";
        Assert.True(vm.FilteredGroups.SelectMany(g => g.Items).Count() < initialTotal);

        vm.SearchText = string.Empty;
        Assert.Equal(initialTotal, vm.FilteredGroups.SelectMany(g => g.Items).Count());
    }

    [Fact]
    public void FormatKeyBadges_StandardKeysAndModifiers_ProducesExpectedTokens()
    {
        Assert.Equal(new[] { "Ctrl", "Z" }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.Z, ModifierKeys.Control));
        Assert.Equal(new[] { "Ctrl", "Shift", "Z" }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.Equal(new[] { "Ctrl", "Alt", "Del" }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.Delete, ModifierKeys.Control | ModifierKeys.Alt));
        Assert.Equal(new[] { "Ctrl", "/" }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.OemQuestion, ModifierKeys.Control));
        Assert.Equal(new[] { "Ctrl", "Num /" }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.Divide, ModifierKeys.Control));
        Assert.Equal(new[] { "[" }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.OemOpenBrackets, ModifierKeys.None));
        Assert.Equal(new[] { "]" }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.OemCloseBrackets, ModifierKeys.None));
        Assert.Equal(new[] { "," }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.OemComma, ModifierKeys.None));
        Assert.Equal(new[] { "." }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.OemPeriod, ModifierKeys.None));
        Assert.Equal(new[] { "Del" }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.Delete, ModifierKeys.None));
        Assert.Equal(new[] { "Backspace" }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.Back, ModifierKeys.None));
        Assert.Equal(new[] { "Space" }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.Space, ModifierKeys.None));
        Assert.Equal(new[] { "Home" }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.Home, ModifierKeys.None));
        Assert.Equal(new[] { "End" }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.End, ModifierKeys.None));
        Assert.Equal(new[] { "Esc" }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.Escape, ModifierKeys.None));
        Assert.Equal(new[] { "Enter" }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.Return, ModifierKeys.None));
        Assert.Equal(new[] { "Ctrl", "↑" }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.Up, ModifierKeys.Control));
        Assert.Equal(new[] { "Ctrl", "↓" }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.Down, ModifierKeys.Control));
        Assert.Equal(new[] { "Ctrl", "←" }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.Left, ModifierKeys.Control));
        Assert.Equal(new[] { "Ctrl", "→" }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.Right, ModifierKeys.Control));
        Assert.Equal(new[] { "Ctrl", "+" }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.OemPlus, ModifierKeys.Control));
        Assert.Equal(new[] { "Ctrl", "Num +" }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.Add, ModifierKeys.Control));
        Assert.Equal(new[] { "Ctrl", "-" }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.OemMinus, ModifierKeys.Control));
        Assert.Equal(new[] { "Ctrl", "Num -" }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.Subtract, ModifierKeys.Control));
        Assert.Equal(new[] { "Ctrl", "0" }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.D0, ModifierKeys.Control));
        Assert.Equal(new[] { "Ctrl", "Num 0" }, KeyboardShortcutsViewModel.FormatKeyBadges(Key.NumPad0, ModifierKeys.Control));
    }

    [Fact]
    public void SearchStateFlags_ToggleCorrectlyWithSearchText()
    {
        var vm = new KeyboardShortcutsViewModel();

        Assert.True(vm.IsSearchEmpty);
        Assert.False(vm.HasSearchText);
        Assert.True(vm.HasResults);
        Assert.False(vm.HasNoResults);

        vm.SearchText = "pencil";
        Assert.False(vm.IsSearchEmpty);
        Assert.True(vm.HasSearchText);
        Assert.True(vm.HasResults);
        Assert.False(vm.HasNoResults);

        vm.SearchText = "NonExistentFeatureXYZ";
        Assert.False(vm.IsSearchEmpty);
        Assert.True(vm.HasSearchText);
        Assert.False(vm.HasResults);
        Assert.True(vm.HasNoResults);

        vm.SearchText = "";
        Assert.True(vm.IsSearchEmpty);
        Assert.False(vm.HasSearchText);
        Assert.True(vm.HasResults);
        Assert.False(vm.HasNoResults);
    }

    [Fact]
    public void BooleanToInverseVisibilityConverter_ConvertsValuesCorrectly()
    {
        var converter = new Hexprite.Converters.BooleanToInverseVisibilityConverter();

        Assert.Equal(System.Windows.Visibility.Collapsed, converter.Convert(true, typeof(System.Windows.Visibility), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(System.Windows.Visibility.Visible, converter.Convert(false, typeof(System.Windows.Visibility), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(System.Windows.Visibility.Visible, converter.Convert(null, typeof(System.Windows.Visibility), null, System.Globalization.CultureInfo.InvariantCulture));

        Assert.False((bool)converter.ConvertBack(System.Windows.Visibility.Visible, typeof(bool), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.True((bool)converter.ConvertBack(System.Windows.Visibility.Collapsed, typeof(bool), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.True((bool)converter.ConvertBack(System.Windows.Visibility.Hidden, typeof(bool), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void KeyboardShortcutsDialog_InitializesWithoutXamlParseException()
    {
        WpfTestHelper.EnsureApplication();
        WpfTestHelper.RunOnSta(() =>
        {
            if (System.Windows.Application.Current != null && System.Windows.Application.Current.Resources.MergedDictionaries.Count == 0)
            {
                System.Windows.Application.Current.Resources.MergedDictionaries.Add(
                    new System.Windows.ResourceDictionary { Source = new Uri("/Hexprite;component/Themes/Dim.xaml", UriKind.RelativeOrAbsolute) });
                System.Windows.Application.Current.Resources.MergedDictionaries.Add(
                    new System.Windows.ResourceDictionary { Source = new Uri("/Hexprite;component/Themes/Styles.xaml", UriKind.RelativeOrAbsolute) });
            }

            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            var dlg = new Hexprite.Views.KeyboardShortcutsDialog(manager);

            Assert.NotNull(dlg);
            Assert.NotNull(dlg.ViewModel);
            Assert.NotNull(dlg.TxtSearch);
        });
    }

    [Fact]
    public void AllRegisteredShortcuts_HaveMatchingMetadata_AndViceVersa()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();

        EditorCanvasShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);
        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        var registeredActionIds = manager.Shortcuts
            .Select(s => s.ActionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var metadataActionIds = KeyboardShortcutsViewModel.KnownActionIds
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Every registered shortcut must have a corresponding metadata entry
        var missingInMetadata = registeredActionIds.Except(metadataActionIds).ToList();
        Assert.Empty(missingInMetadata);

        // Every metadata entry must have a corresponding registered shortcut
        var missingInRegistrar = metadataActionIds.Except(registeredActionIds).ToList();
        Assert.Empty(missingInRegistrar);
    }
}
