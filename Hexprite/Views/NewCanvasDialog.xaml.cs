using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;

namespace Hexprite.Views
{
    public sealed partial class NewCanvasDialog : Window
    {
        [GeneratedRegex(@"[^0-9]+", RegexOptions.None, matchTimeoutMilliseconds: 250)]
        private static partial Regex NonDigitsRegex { get; }

        private static readonly string[] FontPresets = ["8×8 Custom", "5×7 Custom", "16×16 Custom"];
        private static readonly char[] DimensionSeparators = ['×', 'x', 'X'];

        public int CanvasWidth { get; private set; }
        public int CanvasHeight { get; private set; }
        public ColorMode ColorMode { get; private set; } = ColorMode.Monochrome;
        public DocumentMode DocumentMode { get; private set; } = DocumentMode.Sprite;

        public NewCanvasDialog()
        {
            InitializeComponent();
            var prefs = UserPreferencesService.Get();
            TxtWidth.Text = prefs.NewCanvasWidth.ToString(CultureInfo.InvariantCulture);
            TxtHeight.Text = prefs.NewCanvasHeight.ToString(CultureInfo.InvariantCulture);

            int presetIndex = Math.Clamp(
                prefs.NewCanvasPresetIndex,
                0,
                Math.Max(0, MainViewModel.DisplayPresets.Count - 1));
            PresetComboBox.SelectedIndex = presetIndex;
            
            // Set initial mode to trigger the setup logic
            DocumentModeComboBox.SelectedIndex = 0;
        }

        private void DocumentMode_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (DocumentModeComboBox == null || PresetComboBox == null || TxtWidth == null || TxtHeight == null || SizeLabel == null) return;
            
            if (DocumentModeComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string tag)
            {
                if (tag.StartsWith("Sprite", StringComparison.OrdinalIgnoreCase))
                {
                    DocumentMode = DocumentMode.Sprite;
                    ColorMode = tag == "SpriteColor" ? ColorMode.Rgb : ColorMode.Monochrome;
                    
                    PresetComboBox.ItemsSource = MainViewModel.DisplayPresets;
                    PresetComboBox.IsEnabled = true;
                    SizeLabel.Text = "Size";
                    
                    // Set max bounds
                    Hexprite.Behaviors.NumericScrubBehavior.SetMaxValue(TxtWidth, 512);
                    Hexprite.Behaviors.NumericScrubBehavior.SetMaxValue(TxtHeight, 512);
                }
                else if (tag.StartsWith("Font", StringComparison.OrdinalIgnoreCase))
                {
                    DocumentMode = DocumentMode.Font;
                    ColorMode = ColorMode.Monochrome;
                    
                    PresetComboBox.ItemsSource = FontPresets;
                    PresetComboBox.IsEnabled = true;
                    PresetComboBox.SelectedIndex = 0;
                    TxtWidth.IsEnabled = true;
                    TxtHeight.IsEnabled = true;
                    SizeLabel.Text = "Default Glyph";
                    
                    // Set max bounds
                    Hexprite.Behaviors.NumericScrubBehavior.SetMaxValue(TxtWidth, 64);
                    Hexprite.Behaviors.NumericScrubBehavior.SetMaxValue(TxtHeight, 64);
                }
                else if (tag.Equals("AssetPack", StringComparison.OrdinalIgnoreCase))
                {
                    DocumentMode = DocumentMode.AssetPack;
                    ColorMode = ColorMode.Monochrome;

                    PresetComboBox.IsEnabled = false;
                    TxtWidth.IsEnabled = false;
                    TxtHeight.IsEnabled = false;
                    SizeLabel.Text = "Matrix (30×15)";
                    TxtWidth.Text = "128";
                    TxtHeight.Text = "64";
                }
            }
        }

        private void Preset_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (PresetComboBox.SelectedItem is not string preset ||
                (!preset.Contains("Custom", StringComparison.OrdinalIgnoreCase) &&
                 !preset.Contains('x', StringComparison.OrdinalIgnoreCase) &&
                 !preset.Contains('×', StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var label = preset.Split(' ')[0];
            var parts = label.Split(DimensionSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out int pw) &&
                int.TryParse(parts[1], out int ph))
            {
                TxtWidth.Text = pw.ToString(CultureInfo.InvariantCulture);
                TxtHeight.Text = ph.ToString(CultureInfo.InvariantCulture);
            }
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            if (DocumentMode == DocumentMode.AssetPack)
            {
                CanvasWidth = 128;
                CanvasHeight = 64;
                DialogResult = true;
                return;
            }

            if (!int.TryParse(TxtWidth.Text, out int w) || w <= 0 ||
                !int.TryParse(TxtHeight.Text, out int h) || h <= 0)
            {
                MessageDialog.Show("Please enter valid positive dimensions.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int maxDim = DocumentMode == DocumentMode.Sprite ? SpriteState.MaxDimension : 64;
            if (w > maxDim || h > maxDim)
            {
                MessageDialog.Show(string.Create(CultureInfo.InvariantCulture, $"Maximum size is {maxDim}×{maxDim}."),
                    "Too Large", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CanvasWidth = w;
            CanvasHeight = h;
            
            if (DocumentMode == DocumentMode.Sprite)
            {
                UserPreferencesService.Update(p =>
                {
                    p.NewCanvasWidth = w;
                    p.NewCanvasHeight = h;
                    p.NewCanvasPresetIndex = Math.Max(0, PresetComboBox.SelectedIndex);
                    p.NewCanvasColorMode = ColorMode;
                });
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

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.FocusedElement is System.Windows.Controls.TextBox textBox)
            {
                textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));

                // If Enter navigation landed on the Cancel button, skip it to prevent accidental cancellation
                if (Keyboard.FocusedElement is System.Windows.Controls.Button btn && btn.Content is "Cancel")
                {
                    btn.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                }

                e.Handled = true;
            }
        }

        private void NumberOnly(object sender, TextCompositionEventArgs e)
        {
            e.Handled = NonDigitsRegex.IsMatch(e.Text);
        }
    }
}
