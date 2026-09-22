using System.Windows;
using Hexprite.Services;

namespace Hexprite.Views
{
    public partial class PrivacySettingsDialog : Window
    {
        public PrivacySettingsDialog()
        {
            InitializeComponent();

            LoggingService.PrivacyOptions options = LoggingService.GetPrivacyOptions();
            TelemetryEnabledCheckBox.IsChecked = options.TelemetryEnabled;
            AttachLogsDefaultCheckBox.IsChecked = options.AttachLogsByDefault;
            AllowLogAttachmentsCheckBox.IsChecked = options.AllowLogAttachments;
            RedactPersonalDataCheckBox.IsChecked = options.RedactPersonalData;
            AllowContactEmailCheckBox.IsChecked = options.AllowContactEmailInTelemetry;
            ShareContactEmailDefaultCheckBox.IsChecked = options.ShareContactEmailByDefault;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var options = new LoggingService.PrivacyOptions(
                telemetryEnabled: TelemetryEnabledCheckBox.IsChecked == true,
                attachLogsByDefault: AttachLogsDefaultCheckBox.IsChecked == true,
                allowLogAttachments: AllowLogAttachmentsCheckBox.IsChecked == true,
                redactPersonalData: RedactPersonalDataCheckBox.IsChecked == true,
                shareContactEmailByDefault: ShareContactEmailDefaultCheckBox.IsChecked == true,
                allowContactEmailInTelemetry: AllowContactEmailCheckBox.IsChecked == true);

            bool saved = LoggingService.SavePrivacyOptions(options);
            if (!saved)
            {
                MessageDialog.Show(this, "Could not save privacy settings.", "Privacy Settings", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void CaptionClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
