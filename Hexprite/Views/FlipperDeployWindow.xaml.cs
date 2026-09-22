using System;
using System.Collections.Generic;
using System.Windows;
using Hexprite.Services;
using Hexprite.ViewModels.Flipper;

namespace Hexprite.Views
{
    public sealed partial class FlipperDeployWindow : Window, IDisposable
    {
        public FlipperDeployViewModel ViewModel => (FlipperDeployViewModel)DataContext;

        public FlipperDeployWindow(FlipperDeployViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.RequestClose = Close;
            Closed += (s, e) =>
            {
                viewModel.RequestClose = null;
                Dispose();
            };
            PreviewKeyDown += Window_PreviewKeyDown;
        }

        public FlipperDeployWindow(
            IFlipperUsbDeployer deployer,
            IReadOnlyList<(string RelativePath, byte[] Data)> files,
            string packName = "MyAssetPack")
            : this(new FlipperDeployViewModel(
                deployer,
                files,
                packName,
                new DialogService()))
        {
        }

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                ViewModel.CancelDeployCommand.Execute(parameter: null);
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.F5)
            {
                if (ViewModel.RefreshDevicesCommand.CanExecute(parameter: null))
                {
                    ViewModel.RefreshDevicesCommand.Execute(parameter: null);
                    e.Handled = true;
                }
            }
            else if (e.Key == System.Windows.Input.Key.Enter && !ViewModel.IsDeploying && ViewModel.CanDeploy)
            {
                ViewModel.DeployCommand.Execute(parameter: null);
                e.Handled = true;
            }
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
            ViewModel.CancelDeployCommand.Execute(parameter: null);
        }

        public void Dispose()
        {
            if (DataContext is FlipperDeployViewModel vm)
            {
                vm.RequestClose = null;
            }
            if (DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }
            GC.SuppressFinalize(this);
        }
    }
}
