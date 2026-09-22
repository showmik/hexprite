using System;
using System.Windows;
using System.Windows.Input;
using Hexprite.Services;
using Hexprite.ViewModels;

namespace Hexprite.Views
{
    /// <summary>
    /// Interaction logic for KeyboardShortcutsDialog.xaml.
    /// Displays a categorized and searchable cheat sheet of all available keyboard shortcuts.
    /// </summary>
    public partial class KeyboardShortcutsDialog : Window
    {
        public KeyboardShortcutsViewModel ViewModel { get; }

        public KeyboardShortcutsDialog(IHexpriteShortcutManager? shortcutManager = null)
        {
            InitializeComponent();
            ViewModel = new KeyboardShortcutsViewModel(shortcutManager);
            DataContext = ViewModel;

            Loaded += KeyboardShortcutsDialog_Loaded;
            PreviewKeyDown += KeyboardShortcutsDialog_PreviewKeyDown;
        }

        private void KeyboardShortcutsDialog_Loaded(object sender, RoutedEventArgs e)
        {
            TxtSearch.Focus();
        }

        private void KeyboardShortcutsDialog_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (!string.IsNullOrEmpty(ViewModel.SearchText))
                {
                    ViewModel.SearchText = string.Empty;
                    e.Handled = true;
                }
                else
                {
                    Close();
                    e.Handled = true;
                }
            }
        }

        private void CaptionClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SearchText = string.Empty;
            TxtSearch.Focus();
        }
    }
}
