using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Hexprite.Services;

namespace Hexprite.Views
{
    public sealed partial class UserFeedbackDialog : Window
    {
        [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.None, matchTimeoutMilliseconds: 250)]
        private static partial Regex EmailRegex { get; }
        public UserFeedbackInput? Result { get; private set; }

        public UserFeedbackDialog()
        {
            InitializeComponent();
            var privacy = LoggingService.GetPrivacyOptions();
            IncludeLogsCheckBox.IsChecked = privacy.AttachLogsByDefault && privacy.AllowLogAttachments;
            if (!privacy.AllowLogAttachments)
            {
                IncludeLogsCheckBox.IsEnabled = false;
                IncludeLogsCheckBox.ToolTip = "Log attachments are disabled in Privacy Settings.";
            }

            ShareEmailCheckBox.IsChecked = privacy.ShareContactEmailByDefault && privacy.AllowContactEmailInTelemetry;
            if (!privacy.AllowContactEmailInTelemetry)
            {
                ShareEmailCheckBox.IsEnabled = false;
                EmailTextBox.IsEnabled = false;
                ShareEmailCheckBox.ToolTip = "Contact email sharing is disabled in Privacy Settings.";
                EmailTextBox.ToolTip = "Contact email sharing is disabled in Privacy Settings.";
            }
        }

        private void Submit_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(MessageTextBox.Text))
            {
                MessageDialog.Show(this, "Please enter your feedback before submitting.", "Send Feedback", MessageBoxButton.OK, MessageBoxImage.Information);
                MessageTextBox.Focus();
                return;
            }

            if (ShareEmailCheckBox.IsChecked == true)
            {
                string email = EmailTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(email) || !EmailRegex.IsMatch(email))
                {
                    MessageDialog.Show(this, "Please enter a valid email address, or uncheck the 'Include my email' option.", "Invalid Email", MessageBoxButton.OK, MessageBoxImage.Warning);
                    EmailTextBox.Focus();
                    return;
                }
            }

            string category = "General";
            if (CategoryCombo.SelectedItem is ComboBoxItem selected && selected.Tag is string tag && !string.IsNullOrWhiteSpace(tag))
            {
                category = tag.Trim();
            }

            Result = new UserFeedbackInput
            {
                Category = category,
                Message = MessageTextBox.Text.Trim(),
                ContactEmail = string.IsNullOrWhiteSpace(EmailTextBox.Text) ? null : EmailTextBox.Text.Trim(),
                IncludeContactEmail = ShareEmailCheckBox.IsChecked == true,
                IncludeRecentLogs = IncludeLogsCheckBox.IsChecked == true,
            };

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
