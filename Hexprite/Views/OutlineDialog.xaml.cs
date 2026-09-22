using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hexprite.Core;

namespace Hexprite.Views
{
    public sealed partial class OutlineDialog : Window
    {
        [GeneratedRegex(@"[^0-9]+", RegexOptions.None, matchTimeoutMilliseconds: 250)]
        private static partial Regex NonDigitsRegex { get; }
        public OutlineSettings? Result { get; private set; }

        private readonly Action<OutlineSettings>? _previewCallback;
        private bool _suppressSync;
        private readonly bool _initialized;

        public OutlineDialog(Action<OutlineSettings>? previewCallback = null)
        {
            _previewCallback = previewCallback;
            InitializeComponent();

            // Wire up Checked events for radio buttons after InitializeComponent
            RbCircle.Checked += OnSettingChanged;
            RbSquare.Checked += OnSettingChanged;
            RbOutside.Checked += OnSettingChanged;
            RbInside.Checked += OnSettingChanged;

            _initialized = true;

            // Fire initial preview so the user sees the default outline immediately
            RaisePreview();
        }

        // ── Build current settings ────────────────────────────────────

        private OutlineSettings BuildCurrentSettings()
        {
            return new OutlineSettings
            {
                Shape = RbSquare.IsChecked == true ? OutlineShape.Square : OutlineShape.Circle,
                Padding = (int)SliderPadding.Value,
                Thickness = (int)SliderThickness.Value,
                Placement = RbInside.IsChecked == true ? OutlinePlacement.Inside : OutlinePlacement.Outside,
            };
        }

        private void RaisePreview()
        {
            if (!_initialized || _previewCallback == null) return;
            _previewCallback(BuildCurrentSettings());
        }

        // ── Any radio button changed ──────────────────────────────────

        private void OnSettingChanged(object sender, RoutedEventArgs e)
        {
            RaisePreview();
        }

        // ── Slider → TextBox sync ─────────────────────────────────────

        private void SliderPadding_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressSync || TxtPadding == null) return;
            _suppressSync = true;
            TxtPadding.Text = ((int)SliderPadding.Value).ToString(CultureInfo.InvariantCulture);
            _suppressSync = false;
            RaisePreview();
        }

        private void SliderThickness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressSync || TxtThickness == null) return;
            _suppressSync = true;
            TxtThickness.Text = ((int)SliderThickness.Value).ToString(CultureInfo.InvariantCulture);
            _suppressSync = false;
            RaisePreview();
        }

        // ── TextBox → Slider sync ─────────────────────────────────────

        private void TxtPadding_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_suppressSync) return;
            if (int.TryParse(TxtPadding.Text, out int val))
            {
                val = Math.Clamp(val, 0, 16);
                _suppressSync = true;
                SliderPadding.Value = val;
                TxtPadding.Text = val.ToString(CultureInfo.InvariantCulture);
                _suppressSync = false;
            }
            else
            {
                TxtPadding.Text = ((int)SliderPadding.Value).ToString(CultureInfo.InvariantCulture);
            }
            // Preview is already raised by SliderPadding_ValueChanged when slider moves
        }

        private void TxtThickness_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_suppressSync) return;
            if (int.TryParse(TxtThickness.Text, out int val))
            {
                val = Math.Clamp(val, 1, 16);
                _suppressSync = true;
                SliderThickness.Value = val;
                TxtThickness.Text = val.ToString(CultureInfo.InvariantCulture);
                _suppressSync = false;
            }
            else
            {
                TxtThickness.Text = ((int)SliderThickness.Value).ToString(CultureInfo.InvariantCulture);
            }
            // Preview is already raised by SliderThickness_ValueChanged when slider moves
        }

        // ── Input filter ──────────────────────────────────────────────

        private void NumberOnly(object sender, TextCompositionEventArgs e)
        {
            e.Handled = NonDigitsRegex.IsMatch(e.Text);
        }

        // ── Buttons ───────────────────────────────────────────────────

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            Result = BuildCurrentSettings();
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
