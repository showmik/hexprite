using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using Hexprite.Services;
using Hexprite.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Hexprite.Views
{
    public partial class AboutDialog : Window
    {
        public bool HasUpdate { get; }
        public string UpdateVersionText { get; } = string.Empty;
        public string UpdateUrl { get; } = string.Empty;

        public AboutDialog()
        {
            InitializeComponent();

            // Pull version from assembly metadata
            string currentVersion = UpdateService.GetCurrentVersion();
            TxtVersion.Text = $"Version {currentVersion}";

            if (Application.Current is App app && app.Services != null)
            {
                var shell = app.Services.GetService<ShellViewModel>();
                if (shell?.AvailableUpdate is { } update)
                {
                    HasUpdate = true;
                    UpdateVersionText = $"v{update.LatestVersionDisplay} available";
                    UpdateUrl = update.ReleasePageUrl;
                }
            }

            DataContext = this;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void CaptionClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Error(ex, "AboutDialog.OpenHyperlink", new { e.Uri.AbsoluteUri });
                MessageDialog.Show(
                    "Could not open the link.",
                    "Hexel",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            e.Handled = true;
        }
    }
}
