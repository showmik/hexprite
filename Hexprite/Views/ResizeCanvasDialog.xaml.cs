using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;

namespace Hexprite.Views
{
    public sealed partial class ResizeCanvasDialog : Window
    {
        [GeneratedRegex(@"[^0-9]+", RegexOptions.None, matchTimeoutMilliseconds: 250)]
        private static partial Regex NonDigitsRegex { get; }

        private static readonly char[] DimensionSeparators = ['×', 'x', 'X'];

        public (int Width, int Height, ResizeAnchor Anchor)? Result { get; private set; }

        private ResizeAnchor _selectedAnchor = ResizeAnchor.TopLeft;

        public ResizeCanvasDialog(int currentWidth, int currentHeight)
        {
            InitializeComponent();
            TxtCurrentSize.Text = string.Create(CultureInfo.InvariantCulture, $"{currentWidth} × {currentHeight}");
            TxtWidth.Text = currentWidth.ToString(CultureInfo.InvariantCulture);
            TxtHeight.Text = currentHeight.ToString(CultureInfo.InvariantCulture);

            var prefs = UserPreferencesService.Get();
            int presetIndex = Math.Clamp(
                prefs.ResizePresetIndex,
                0,
                Math.Max(0, MainViewModel.DisplayPresets.Count - 1));
            PresetComboBox.SelectedIndex = presetIndex;
            SetAnchorSelection(prefs.ResizeAnchor);
        }

        private void Preset_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (PresetComboBox.SelectedItem is not string preset || preset == "Custom") return;
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

        private void Anchor_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.RadioButton rb || rb.Tag is not string tag) return;
            if (Enum.TryParse<ResizeAnchor>(tag, out var anchor))
                _selectedAnchor = anchor;
        }

        private void Resize_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TxtWidth.Text, out int w) || w <= 0 ||
                !int.TryParse(TxtHeight.Text, out int h) || h <= 0)
            {
                MessageDialog.Show("Please enter valid positive dimensions.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (w > SpriteState.MaxDimension || h > SpriteState.MaxDimension)
            {
                MessageDialog.Show(string.Create(CultureInfo.InvariantCulture, $"Maximum canvas size is {SpriteState.MaxDimension}×{SpriteState.MaxDimension}."),
                    "Too Large", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Result = (w, h, _selectedAnchor);
            UserPreferencesService.Update(p =>
            {
                p.ResizePresetIndex = Math.Max(0, PresetComboBox.SelectedIndex);
                p.ResizeAnchor = _selectedAnchor;
            });
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

        private void NumberOnly(object sender, TextCompositionEventArgs e)
        {
            e.Handled = NonDigitsRegex.IsMatch(e.Text);
        }

        private void SetAnchorSelection(ResizeAnchor anchor)
        {
            _selectedAnchor = anchor;
            var selected = AnchorGrid.Children
                .OfType<RadioButton>()
                .FirstOrDefault(rb => string.Equals(rb.Tag as string, anchor.ToString(), StringComparison.Ordinal));

            if (selected != null)
                selected.IsChecked = true;
        }
    }
}
