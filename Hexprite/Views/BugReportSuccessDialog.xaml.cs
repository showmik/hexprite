using System.Windows;
using Hexprite.Services;

namespace Hexprite.Views
{
    public partial class BugReportSuccessDialog : Window
    {
        private readonly string? _reportId;

        public BugReportSuccessDialog(string message, string? reportId, string? windowTitle = null)
        {
            InitializeComponent();
            if (!string.IsNullOrWhiteSpace(windowTitle))
            {
                Title = windowTitle;
            }

            TxtMessage.Text = message;
            _reportId = reportId;

            if (!string.IsNullOrWhiteSpace(reportId))
            {
                LblReportId.Visibility = Visibility.Visible;
                TxtReportId.Visibility = Visibility.Visible;
                BtnCopyId.Visibility = Visibility.Visible;
                TxtReportId.Text = reportId;
            }
        }

        private void CopyId_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_reportId))
            {
                return;
            }

            try
            {
                Clipboard.SetText(_reportId);
                TxtCopiedHint.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Warning(ex, "BugReportSuccessDialog.CopyReportId");
                TxtCopiedHint.Text = "Could not copy to clipboard.";
                TxtCopiedHint.Visibility = Visibility.Visible;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CaptionClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
