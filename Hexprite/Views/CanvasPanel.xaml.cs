using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Hexprite.Views
{
    public partial class CanvasPanel : UserControl
    {
        [GeneratedRegex(@"^[0-9]+$", RegexOptions.None, matchTimeoutMilliseconds: 250)]
        private static partial Regex DigitsOnlyRegex { get; }

        public CanvasPanel()
        {
            InitializeComponent();
        }

        private ViewModels.MainViewModel? ViewModel => DataContext as ViewModels.MainViewModel;

        // ── Symmetry Line Dragging ────────────────────────────────────────

        private void VerticalSymmetryHandle_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (ViewModel == null || ViewModel.CellSize <= 0) return;
            var pos = Mouse.GetPosition(PixelGridContainer);
            double newAxisX = Math.Clamp(pos.X / ViewModel.CellSize, 0, ViewModel.SpriteState.Width);
            ViewModel.SymmetryAxisX = Math.Round(newAxisX * 2, MidpointRounding.AwayFromZero) / 2;
            e.Handled = true;
        }

        private void HorizontalSymmetryHandle_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (ViewModel == null || ViewModel.CellSize <= 0) return;
            var pos = Mouse.GetPosition(PixelGridContainer);
            double newAxisY = Math.Clamp(pos.Y / ViewModel.CellSize, 0, ViewModel.SpriteState.Height);
            ViewModel.SymmetryAxisY = Math.Round(newAxisY * 2, MidpointRounding.AwayFromZero) / 2;
            e.Handled = true;
        }

        // ── Zoom Events for MainWindow ────────────────────────────────────

        public event RoutedEventHandler ZoomInClicked
        {
            add { AddHandler(ZoomInClickedEvent, value); }
            remove { RemoveHandler(ZoomInClickedEvent, value); }
        }
        public static readonly RoutedEvent ZoomInClickedEvent = EventManager.RegisterRoutedEvent("ZoomInClicked", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(CanvasPanel));

        public event RoutedEventHandler ZoomOutClicked
        {
            add { AddHandler(ZoomOutClickedEvent, value); }
            remove { RemoveHandler(ZoomOutClickedEvent, value); }
        }
        public static readonly RoutedEvent ZoomOutClickedEvent = EventManager.RegisterRoutedEvent("ZoomOutClicked", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(CanvasPanel));

        public event RoutedEventHandler ZoomResetClicked
        {
            add { AddHandler(ZoomResetClickedEvent, value); }
            remove { RemoveHandler(ZoomResetClickedEvent, value); }
        }
        public static readonly RoutedEvent ZoomResetClickedEvent = EventManager.RegisterRoutedEvent("ZoomResetClicked", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(CanvasPanel));

        private void BtnZoomIn_Click(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(ZoomInClickedEvent, sender));
        private void BtnZoomOut_Click(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(ZoomOutClickedEvent, sender));
        private void BtnZoomReset_Click(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(ZoomResetClickedEvent, sender));

        // ── Brush Options ─────────────────────────────────────────────────

        private void BrushShape_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton rb || rb.Tag is null || ViewModel is null) return;
            ViewModel.BrushShape = rb.Tag.ToString() switch
            {
                "Square" => Core.BrushShape.Square,
                "Line" => Core.BrushShape.Line,
                _ => Core.BrushShape.Circle,
            };
        }

        private void DitherPattern_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag && ViewModel != null)
            {
                if (Enum.TryParse<Core.DitherPattern>(tag, out var pattern))
                    ViewModel.DitherPattern = pattern;
            }
        }

        private void BtnBrushDown_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null) ViewModel.BrushSize--;
        }

        private void BtnBrushUp_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null) ViewModel.BrushSize++;
        }

        // ── Text Validation ───────────────────────────────────────────────

        private void NumericTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !DigitsOnlyRegex.IsMatch(e.Text);
        }

        private void BrushSizeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && ViewModel != null)
            {
                if (int.TryParse(tb.Text, out int val))
                    ViewModel.BrushSize = Math.Clamp(val, 1, 64);
                tb.Text = ViewModel.BrushSize.ToString(CultureInfo.InvariantCulture);
            }
        }

        private void BrushAngleTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && ViewModel != null)
            {
                if (int.TryParse(tb.Text, out int val))
                    ViewModel.BrushAngle = ((val % 360) + 360) % 360;
                tb.Text = ViewModel.BrushAngle.ToString(CultureInfo.InvariantCulture);
            }
        }

        private void FontSizeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && ViewModel != null)
            {
                if (int.TryParse(tb.Text, out int val))
                    ViewModel.FontSize = Math.Clamp(val, 4, 144);
                tb.Text = ViewModel.FontSize.ToString(CultureInfo.InvariantCulture);
            }
        }
        // ── Font ComboBox ─────────────────────────────────────────────────

        private void FontComboBox_DropDownClosed(object sender, EventArgs e)
        {
            // After selecting a font from the dropdown during text editing,
            // move focus back to the ScrollViewer so keystrokes resume going
            // to the text tool instead of the ComboBox's type-ahead search.
            if (ViewModel != null && ViewModel.IsTextEditing)
            {
                Keyboard.Focus(MainScrollViewer);
            }
        }

        // ── Font buttons ──────────────────────────────────────────────────

        private void OpenFontsFolder_Click(object sender, RoutedEventArgs e)
        {
            Services.FontService.OpenCustomFontsFolder();
        }

        private void RefreshFonts_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.RefreshFonts();
        }

        // ── Text alignment ────────────────────────────────────────────────

        private void TextAlignLeft_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null) ViewModel.TextAlignment = Core.TextToolAlignment.Left;
        }

        private void TextAlignCenter_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null) ViewModel.TextAlignment = Core.TextToolAlignment.Center;
        }

        private void TextAlignRight_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null) ViewModel.TextAlignment = Core.TextToolAlignment.Right;
        }
    }
}
