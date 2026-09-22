using System;
using System.Windows;
using System.Windows.Input;
using Hexprite.Services;
using Hexprite.ViewModels;
using Hexprite.ViewModels.Flipper;

namespace Hexprite.Views
{
    public partial class FlipperScreenMirrorWindow : Window
    {
        public FlipperScreenMirrorViewModel ViewModel => (FlipperScreenMirrorViewModel)DataContext;

        public FlipperScreenMirrorWindow(FlipperScreenMirrorViewModel viewModel)
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

        public FlipperScreenMirrorWindow(IFlipperScreenStreamService? streamService = null, IWorkspaceTabService? tabService = null)
            : this(new FlipperScreenMirrorViewModel(
                streamService,
                tabService ?? (Application.Current?.MainWindow?.DataContext as IWorkspaceTabService),
                new DialogService()))
        {
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
            else if (e.Key == Key.F5)
            {
                e.Handled = true;
                _ = ViewModel.RefreshDevices();
            }
            else if (e.Key == Key.Space)
            {
                // Only process space shortcut when not focused on an interactive control (button, textbox, combobox)
                if (Keyboard.FocusedElement is not System.Windows.Controls.Primitives.ButtonBase and not System.Windows.Controls.TextBox and not System.Windows.Controls.ComboBox)
                {
                    e.Handled = true;
                    ViewModel.TogglePauseStream();
                }
            }
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
