using System.Windows;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;

namespace Hexprite.Views
{
    public partial class HardwarePreviewWiringDialog : Window
    {
        public HardwarePreviewWiringViewModel ViewModel => (HardwarePreviewWiringViewModel)DataContext;

        public HardwarePreviewWiringDialog(HardwarePreviewWiringViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void CaptionClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CaptionMaximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void OpenSketchFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var config = ViewModel.GetConfig();
                var validation = config.Validate();
                if (validation.IsValid)
                {
                    HardwarePreviewSketchGenerator.UpdateStandaloneSketchInAppData(config, ViewModel.SelectedBaudRate);
                    HardwarePreviewSketchGenerator.UpdatePlatformIOConfigInAppData(config, ViewModel.SelectedBaudRate);
                }

                string? path = Services.AssetsPathService.ResolveHexpritePreviewStandalonePath();
                if (path != null)
                {
                    string explorerPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows), "explorer.exe");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(explorerPath, path) { UseShellExecute = true });
                }
            }
            catch (System.Exception ex)
            {
                ViewModel.ValidationError = $"Could not open sketch folder: {ex.Message}";
            }
        }

        private void Done_Click(object sender, RoutedEventArgs e)
        {
            var config = ViewModel.GetConfig();
            var validation = config.Validate();
            if (!validation.IsValid)
            {
                ViewModel.ValidationError = validation.Error;
                return;
            }

            ViewModel.ExecuteSaveAsDefault();
            DialogResult = true;
            Close();
        }

        private void I2cRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (DataContext is HardwarePreviewWiringViewModel vm)
            {
                vm.InterfaceType = "I2C";
            }
        }

        private void SpiRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (DataContext is HardwarePreviewWiringViewModel vm)
            {
                vm.InterfaceType = "SPI";
            }
        }
    }
}
