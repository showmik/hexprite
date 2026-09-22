using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Hexprite.ViewModels.Flipper;

namespace Hexprite.Views
{
    public partial class FlipperMediaSlicerWindow : Window
    {
        public FlipperMediaSlicerViewModel ViewModel => (FlipperMediaSlicerViewModel)DataContext;

        public FlipperMediaSlicerWindow(FlipperMediaSlicerViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.RequestClose = Close;
            Closed += (s, e) =>
            {
                viewModel.RequestClose = null;
                viewModel.Dispose();
            };
        }

        public FlipperMediaSlicerWindow(IWorkspaceTabService? tabService = null, SpriteState? initialSprite = null)
            : this(new FlipperMediaSlicerViewModel(
                new FlipperExportService(),
                tabService ?? (Application.Current?.MainWindow?.DataContext as IWorkspaceTabService),
                new DialogService(),
                initialSprite))
        {
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else if (e.LeftButton == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch
                {
                    // Ignore DragMove exceptions if mouse is captured
                }
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
            else if (e.Key == Key.O && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                ViewModel.OpenImage();
            }
            else if (e.Key == Key.Space)
            {
                if (Keyboard.FocusedElement is not System.Windows.Controls.TextBox and not System.Windows.Controls.ComboBox)
                {
                    e.Handled = true;
                    ViewModel.TogglePlayPause();
                }
            }
            else if (e.Key == Key.Left)
            {
                if (Keyboard.FocusedElement is not System.Windows.Controls.TextBox and not System.Windows.Controls.ComboBox)
                {
                    e.Handled = true;
                    ViewModel.StepBackward();
                }
            }
            else if (e.Key == Key.Right)
            {
                if (Keyboard.FocusedElement is not System.Windows.Controls.TextBox and not System.Windows.Controls.ComboBox)
                {
                    e.Handled = true;
                    ViewModel.StepForward();
                }
            }
            else if (e.Key == Key.Home)
            {
                if (Keyboard.FocusedElement is not System.Windows.Controls.TextBox and not System.Windows.Controls.ComboBox)
                {
                    e.Handled = true;
                    ViewModel.FirstFrame();
                }
            }
            else if (e.Key == Key.End)
            {
                if (Keyboard.FocusedElement is not System.Windows.Controls.TextBox and not System.Windows.Controls.ComboBox)
                {
                    e.Handled = true;
                    ViewModel.LastFrame();
                }
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    string file = files[0];
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext is ".png" or ".gif" or ".bmp" or ".jpg" or ".jpeg")
                    {
                        ViewModel.LoadFromFile(file);
                        e.Handled = true;
                    }
                }
            }
        }

        private void CaptionClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
