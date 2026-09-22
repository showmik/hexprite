using System.Windows;
using System.Windows.Input;
using Hexprite.Services;

namespace Hexprite.Views
{
    public partial class FirstRunDialog : Window
    {
        public FirstRunDialog()
        {
            InitializeComponent();
        }

        private void CaptionClose_Click(object sender, RoutedEventArgs e)
        {
            // Allow them to close it, they still complete first run
            DialogResult = true;
            Close();
        }

        private void GetStarted_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                if (WindowState == WindowState.Maximized)
                    WindowState = WindowState.Normal;
                else
                    WindowState = WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        }
    }
}
