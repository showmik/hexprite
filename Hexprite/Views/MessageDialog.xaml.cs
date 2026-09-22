using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Hexprite.Views
{
    public partial class MessageDialog : Window
    {
        public string DialogTitle
        {
            get => (string)GetValue(DialogTitleProperty);
            set => SetValue(DialogTitleProperty, value);
        }

        public static readonly DependencyProperty DialogTitleProperty =
            DependencyProperty.Register("DialogTitle", typeof(string), typeof(MessageDialog), new PropertyMetadata("Message"));

        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

        public MessageDialog(string message, string title, MessageBoxButton button, MessageBoxImage icon)
        {
            InitializeComponent();
            DataContext = this;
            DialogTitle = string.IsNullOrEmpty(title) ? "Message" : title;
            MessageTextBlock.Text = message;

            ConfigureIcon(icon);
            ConfigureButtons(button);
        }

        private void ConfigureIcon(MessageBoxImage icon)
        {
            if (icon == MessageBoxImage.None) return;

            IconTextBlock.Visibility = Visibility.Visible;
            switch (icon)
            {
                case MessageBoxImage.Information:
                    IconTextBlock.Text = "\uE946"; // Info
                    IconTextBlock.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent.Base");
                    break;
                case MessageBoxImage.Warning:
                    IconTextBlock.Text = "\uE7BA"; // Warning
                    IconTextBlock.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Status.Warning");
                    break;
                case MessageBoxImage.Error:
                    IconTextBlock.Text = "\uEA39"; // Error
                    IconTextBlock.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Status.Danger");
                    break;
                case MessageBoxImage.Question:
                    IconTextBlock.Text = "\uE9CE"; // Help/Question
                    IconTextBlock.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent.Base");
                    break;
            }
        }

        private void ConfigureButtons(MessageBoxButton button)
        {
            switch (button)
            {
                case MessageBoxButton.OK:
                    AddButton("OK", MessageBoxResult.OK, isDefault: true, isCancel: true);
                    break;
                case MessageBoxButton.OKCancel:
                    AddButton("OK", MessageBoxResult.OK, isDefault: true);
                    AddButton("Cancel", MessageBoxResult.Cancel, isCancel: true);
                    break;
                case MessageBoxButton.YesNo:
                    AddButton("Yes", MessageBoxResult.Yes, isDefault: true);
                    AddButton("No", MessageBoxResult.No, isCancel: true);
                    break;
                case MessageBoxButton.YesNoCancel:
                    AddButton("Yes", MessageBoxResult.Yes, isDefault: true);
                    AddButton("No", MessageBoxResult.No);
                    AddButton("Cancel", MessageBoxResult.Cancel, isCancel: true);
                    break;
            }
        }

        private void AddButton(string text, MessageBoxResult result, bool isDefault = false, bool isCancel = false)
        {
            var btn = new Button
            {
                Content = text,
                Width = 90,
                MinHeight = 30,
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = isDefault,
                IsCancel = isCancel,
                Tag = result,
            };

            if (isDefault)
            {
                btn.SetResourceReference(StyleProperty, "AccentButtonStyle");
            }
            else
            {
                btn.SetResourceReference(StyleProperty, "ModernButtonStyle");
            }

            btn.Click += (s, e) =>
            {
                Result = (MessageBoxResult)((Button)s).Tag;
                DialogResult = true;
                Close();
            };

            ButtonPanel.Children.Add(btn);
        }

        private void CaptionClose_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Cancel;
            DialogResult = false;
            Close();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Result = MessageBoxResult.Cancel;
                DialogResult = false;
                Close();
            }
        }

        public static MessageBoxResult Show(string messageBoxText, string caption = "", MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
        {
            return Show(owner: null, messageBoxText, caption, button, icon);
        }

        public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption = "", MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
        {
            var dialog = new MessageDialog(messageBoxText, caption, button, icon);
            
            Window? targetOwner = owner;
            if (targetOwner == null && Application.Current != null)
            {
                try
                {
                    targetOwner = System.Linq.Enumerable.FirstOrDefault(
                                      System.Linq.Enumerable.OfType<Window>(Application.Current.Windows),
                                      w => w != null && w.IsActive && w.IsLoaded)
                                  ?? Application.Current.MainWindow;
                }
                catch
                {
                    targetOwner = Application.Current?.MainWindow;
                }
            }

            if (targetOwner != null && targetOwner.IsLoaded && targetOwner != dialog)
            {
                try
                {
                    dialog.Owner = targetOwner;
                }
                catch
                {
                    // Ignore owner assignment errors if already closing/closed
                }
            }

            dialog.ShowDialog();
            
            return dialog.Result == MessageBoxResult.None ? MessageBoxResult.Cancel : dialog.Result;
        }
    }
}
