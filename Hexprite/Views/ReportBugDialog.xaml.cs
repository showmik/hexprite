using System.Text.RegularExpressions;
using System.Windows;
using Hexprite.Services;

namespace Hexprite.Views
{
    public sealed partial class ReportBugDialog : Window
    {
        [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.None, matchTimeoutMilliseconds: 250)]
        private static partial Regex EmailRegex { get; }
        public BugReportInput? Result { get; private set; }

        public ReportBugDialog()
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
            if (string.IsNullOrWhiteSpace(SummaryTextBox.Text))
            {
                MessageDialog.Show(this, "Please enter a short summary before submitting.", "Report a Bug", MessageBoxButton.OK, MessageBoxImage.Information);
                SummaryTextBox.Focus();
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

            Result = new BugReportInput
            {
                Summary = SummaryTextBox.Text.Trim(),
                StepsToReproduce = StepsTextBox.Text.Trim(),
                ExpectedBehavior = ExpectedTextBox.Text.Trim(),
                ActualBehavior = ActualTextBox.Text.Trim(),
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
