using System.Windows;
using System.Windows.Controls;
using Hexprite.Core;

namespace Hexprite.Views
{
    public partial class ToolSidebarPanel : UserControl
    {
        /// <summary>
        /// When true, the Tool_Checked handler is suppressed.
        /// Used by <see cref="SyncToTool"/> to avoid executing SelectToolCommand
        /// when programmatically syncing radio buttons.
        /// </summary>
        private bool _suppressChecked;

        public ToolSidebarPanel()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Programmatically sets the correct tool radio button to match the
        /// given <paramref name="tool"/> without triggering the SelectToolCommand.
        /// Called when the global tool changes.
        /// </summary>
        public void SyncToTool(ToolMode tool)
        {
            _suppressChecked = true;
            try
            {
                var target = tool switch
                {
                    ToolMode.Pencil => RbPencil,
                    ToolMode.Eraser => RbEraser,
                    ToolMode.Dither => RbDither,
                    ToolMode.Line => RbLine,
                    ToolMode.Rectangle => RbRectangle,
                    ToolMode.Ellipse => RbEllipse,
                    ToolMode.FilledRectangle => RbFilledRectangle,
                    ToolMode.FilledEllipse => RbFilledEllipse,
                    ToolMode.Fill => RbFill,
                    ToolMode.Text => RbText,
                    ToolMode.Move => RbMove,
                    ToolMode.Marquee => RbMarquee,
                    ToolMode.EllipticalMarquee => RbEllipticalMarquee,
                    ToolMode.Lasso => RbLasso,
                    ToolMode.MagicWand => RbMagicWand,
                    ToolMode.Gradient => RbGradient,
                    _ => RbPencil,
                };
                target.IsChecked = true;
            }
            finally
            {
                _suppressChecked = false;
            }
        }

        /// <summary>
        /// Delegates tool selection to the ShellViewModel's SelectToolCommand.
        /// The RadioButton's Tag carries the tool name string.
        /// </summary>
        private void Tool_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressChecked) return;
            if (sender is not RadioButton rb || rb.Tag is null) return;
            // Tool state is now global, so use ShellViewModel
            if (DataContext is not ViewModels.ShellViewModel shell) return;
            if (shell.SelectToolCommand.CanExecute(rb.Tag.ToString()))
                shell.SelectToolCommand.Execute(rb.Tag.ToString());
        }
    }
}
