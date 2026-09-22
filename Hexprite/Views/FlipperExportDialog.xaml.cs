using System;
using System.Windows;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels.Flipper;

namespace Hexprite.Views
{
    public partial class FlipperExportDialog : Window
    {
        public FlipperExportViewModel ViewModel => (FlipperExportViewModel)DataContext;

        public FlipperExportDialog(FlipperExportViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();

            viewModel.RequestClose = (result) =>
            {
                DialogResult = result;
                Close();
            };

            Closed += (s, e) =>
            {
                viewModel.RequestClose = null;
                viewModel.Dispose();
            };

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    Close();
                }
            };
        }

        public FlipperExportDialog(IFlipperExportService exportService, SpriteState spriteState)
            : this(new FlipperExportViewModel(
                spriteState,
                exportService,
                null,
                new DialogService()))
        {
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
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

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
