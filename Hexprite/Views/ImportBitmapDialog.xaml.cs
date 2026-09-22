using Hexprite.Core;
using Hexprite.Services;
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hexprite.Views
{
    public sealed partial class ImportBitmapDialog : Window, IDisposable
    {
        [GeneratedRegex(@"[^0-9]+", RegexOptions.None, matchTimeoutMilliseconds: 250)]
        private static partial Regex NonDigitsRegex { get; }
        public BitmapImportSettings? Result { get; private set; }

        private readonly string _sourceFileName;
        private CancellationTokenSource? _previewCts;
        private bool _isUpdatingFromCode;
        private bool _isApplyingPreset;
        private bool _shouldFitPreviewToFrame;

        public ImportBitmapDialog(string fileName, BitmapImportSettings initialSettings)
        {
            InitializeComponent();
            _sourceFileName = fileName;

            TxtSourceFile.Text = System.IO.Path.GetFileName(fileName);
            
            SldMaxDimension.Maximum = SpriteState.MaxDimension;

            _isUpdatingFromCode = true;
            PresetCombo.DisplayMemberPath = "Name";
            PresetCombo.SelectedValuePath = "Value";
            PresetCombo.ItemsSource = new[]
            {
                new { Name = "Custom", Value = ImportPreset.Custom, Description = ImportPresetHelper.GetPresetDescription(ImportPreset.Custom) },
                new { Name = "Default", Value = ImportPreset.Default, Description = ImportPresetHelper.GetPresetDescription(ImportPreset.Default) },
                new { Name = "Photo (Floyd-Steinberg)", Value = ImportPreset.Photo, Description = ImportPresetHelper.GetPresetDescription(ImportPreset.Photo) },
                new { Name = "Retro Mac (Atkinson)", Value = ImportPreset.RetroMac, Description = ImportPresetHelper.GetPresetDescription(ImportPreset.RetroMac) },
                new { Name = "Pixel Art (Bayer)", Value = ImportPreset.PixelArt, Description = ImportPresetHelper.GetPresetDescription(ImportPreset.PixelArt) },
                new { Name = "Line Art (Binary)", Value = ImportPreset.LineArt, Description = ImportPresetHelper.GetPresetDescription(ImportPreset.LineArt) },
                new { Name = "Solid Logo", Value = ImportPreset.SolidLogo, Description = ImportPresetHelper.GetPresetDescription(ImportPreset.SolidLogo) },
            };
            PresetCombo.SelectedValue = initialSettings.Preset;
            PresetCombo.ToolTip = ImportPresetHelper.GetPresetDescription(initialSettings.Preset);

            DitherCombo.ItemsSource = Enum.GetValues<BitmapDitheringAlgorithm>();
            DitherCombo.SelectedItem = initialSettings.DitheringAlgorithm;

            ScalingCombo.ItemsSource = Enum.GetValues<Hexprite.Services.BitmapScalingMode>();
            ScalingCombo.SelectedItem = initialSettings.ScalingMode;

            TxtThreshold.Text = Math.Clamp(initialSettings.Threshold, 0, 255).ToString(CultureInfo.InvariantCulture);
            SldThreshold.Value = Math.Clamp(initialSettings.Threshold, 0, 255);

            TxtAlphaThreshold.Text = Math.Clamp(initialSettings.AlphaThreshold, 0, 255).ToString(CultureInfo.InvariantCulture);
            SldAlphaThreshold.Value = Math.Clamp(initialSettings.AlphaThreshold, 0, 255);

            TxtBrightness.Text = Math.Clamp(initialSettings.Brightness, -100, 100).ToString(CultureInfo.InvariantCulture);
            SldBrightness.Value = Math.Clamp(initialSettings.Brightness, -100, 100);

            TxtContrast.Text = Math.Clamp(initialSettings.Contrast, -100, 100).ToString(CultureInfo.InvariantCulture);
            SldContrast.Value = Math.Clamp(initialSettings.Contrast, -100, 100);

            TxtDitherAmount.Text = Math.Clamp(initialSettings.DitherAmount, 0, 100).ToString(CultureInfo.InvariantCulture);
            SldDitherAmount.Value = Math.Clamp(initialSettings.DitherAmount, 0, 100);

            TxtMaxDimension.Text = Math.Clamp(initialSettings.MaxDimension, 1, SpriteState.MaxDimension).ToString(CultureInfo.InvariantCulture);
            SldMaxDimension.Value = Math.Clamp(initialSettings.MaxDimension, 1, SpriteState.MaxDimension);

            ChkInvert.IsChecked = initialSettings.Invert;
            ChkSerpentine.IsChecked = initialSettings.UseSerpentineScanning;
            ChkGammaCorrection.IsChecked = initialSettings.UseGammaCorrection;
            ChkAdaptiveThreshold.IsChecked = initialSettings.UseAdaptiveThresholding;
            ChkPreserveEdges.IsChecked = initialSettings.PreserveEdges;
            ChkSharpen.IsChecked = initialSettings.Sharpen;
            _isUpdatingFromCode = false;

            PresetCombo.SelectionChanged += PresetCombo_SelectionChanged;
            DitherCombo.SelectionChanged += AnySettingChanged;
            ScalingCombo.SelectionChanged += AnySettingChanged;

            UpdateThresholdUiState();
            _shouldFitPreviewToFrame = true;

            ChipPresetDefault.ToolTip = ImportPresetHelper.GetPresetDescription(ImportPreset.Default);
            ChipPresetPhoto.ToolTip = ImportPresetHelper.GetPresetDescription(ImportPreset.Photo);
            ChipPresetRetroMac.ToolTip = ImportPresetHelper.GetPresetDescription(ImportPreset.RetroMac);
            ChipPresetPixelArt.ToolTip = ImportPresetHelper.GetPresetDescription(ImportPreset.PixelArt);
            ChipPresetLineArt.ToolTip = ImportPresetHelper.GetPresetDescription(ImportPreset.LineArt);
            ChipPresetSolidLogo.ToolTip = ImportPresetHelper.GetPresetDescription(ImportPreset.SolidLogo);
            ChipPresetCustom.ToolTip = ImportPresetHelper.GetPresetDescription(ImportPreset.Custom);
            SyncPresetChips(initialSettings.Preset);

            _isApplyingPreset = true;
            RefreshImportEnabled();
            _isApplyingPreset = false;
        }

        private void ChkAdaptiveThreshold_CheckedChanged(object sender, RoutedEventArgs e)
        {
            UpdateThresholdUiState();
            AnySettingChanged(sender, e);
        }

        private void UpdateThresholdUiState()
        {
            bool isAdaptive = ChkAdaptiveThreshold.IsChecked == true;
            TxtThreshold.IsEnabled = !isAdaptive;
            SldThreshold.IsEnabled = !isAdaptive;
            TxtThreshold.Opacity = isAdaptive ? 0.4 : 1.0;
            SldThreshold.Opacity = isAdaptive ? 0.4 : 1.0;
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadSettings(out BitmapImportSettings? settings))
                return;

            Result = settings;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            _isApplyingPreset = true;
            _isUpdatingFromCode = true;

            PresetCombo.SelectedValue = ImportPreset.Default;
            PresetCombo.ToolTip = ImportPresetHelper.GetPresetDescription(ImportPreset.Default);
            SyncPresetChips(ImportPreset.Default);
            DitherCombo.SelectedItem = BitmapDitheringAlgorithm.Atkinson;
            ScalingCombo.SelectedItem = Hexprite.Services.BitmapScalingMode.Fant;
            
            SldThreshold.Value = 128;
            TxtThreshold.Text = "128";
            
            SldAlphaThreshold.Value = 128;
            TxtAlphaThreshold.Text = "128";
            
            SldBrightness.Value = 0;
            TxtBrightness.Text = "0";

            SldContrast.Value = 0;
            TxtContrast.Text = "0";

            SldDitherAmount.Value = 100;
            TxtDitherAmount.Text = "100";
            
            SldMaxDimension.Value = SpriteState.MaxDimension;
            TxtMaxDimension.Text = SpriteState.MaxDimension.ToString(CultureInfo.InvariantCulture);

            ChkInvert.IsChecked = false;
            ChkSerpentine.IsChecked = false;
            ChkGammaCorrection.IsChecked = false;
            ChkAdaptiveThreshold.IsChecked = false;
            ChkPreserveEdges.IsChecked = false;
            ChkSharpen.IsChecked = false;

            // Reset preview pan and zoom
            PreviewScaleTransform.ScaleX = 1;
            PreviewScaleTransform.ScaleY = 1;
            PreviewScrollViewer.ScrollToHorizontalOffset(0);
            PreviewScrollViewer.ScrollToVerticalOffset(0);

            _isUpdatingFromCode = false;

            UpdateThresholdUiState();
            _shouldFitPreviewToFrame = true;
            RefreshImportEnabled();
            _isApplyingPreset = false;
        }

        private void CaptionClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.FocusedElement is TextBox textBox)
            {
                textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));

                while (Keyboard.FocusedElement is Button btn && 
                      (btn.Content is "Cancel" or "Reset to defaults"))
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

        // Synchronize Slider -> TextBox
        private void SldThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingFromCode || TxtThreshold == null) return;
            _isUpdatingFromCode = true;
            TxtThreshold.Text = ((int)e.NewValue).ToString(CultureInfo.InvariantCulture);
            _isUpdatingFromCode = false;
            RefreshImportEnabled();
        }

        private void SldMaxDimension_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingFromCode || TxtMaxDimension == null) return;
            _isUpdatingFromCode = true;
            TxtMaxDimension.Text = ((int)e.NewValue).ToString(CultureInfo.InvariantCulture);
            _isUpdatingFromCode = false;
            _shouldFitPreviewToFrame = true;
            RefreshImportEnabled();
        }

        private void SldAlphaThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingFromCode || TxtAlphaThreshold == null) return;
            _isUpdatingFromCode = true;
            TxtAlphaThreshold.Text = ((int)e.NewValue).ToString(CultureInfo.InvariantCulture);
            _isUpdatingFromCode = false;
            RefreshImportEnabled();
        }

        // Synchronize TextBox -> Slider
        private void TxtThreshold_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFromCode) return;
            if (int.TryParse(TxtThreshold.Text, out int val))
            {
                _isUpdatingFromCode = true;
                SldThreshold.Value = Math.Clamp(val, 0, 255);
                _isUpdatingFromCode = false;
                RefreshImportEnabled();
            }
        }

        private void TxtMaxDimension_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFromCode) return;
            if (int.TryParse(TxtMaxDimension.Text, out int val))
            {
                _isUpdatingFromCode = true;
                SldMaxDimension.Value = Math.Clamp(val, 1, SpriteState.MaxDimension);
                _isUpdatingFromCode = false;
                _shouldFitPreviewToFrame = true;
                RefreshImportEnabled();
            }
        }

        private void TxtAlphaThreshold_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFromCode) return;
            if (int.TryParse(TxtAlphaThreshold.Text, out int val))
            {
                _isUpdatingFromCode = true;
                SldAlphaThreshold.Value = Math.Clamp(val, 0, 255);
                _isUpdatingFromCode = false;
                RefreshImportEnabled();
            }
        }

        private void SldBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingFromCode || TxtBrightness == null) return;
            _isUpdatingFromCode = true;
            TxtBrightness.Text = ((int)e.NewValue).ToString(CultureInfo.InvariantCulture);
            _isUpdatingFromCode = false;
            RefreshImportEnabled();
        }

        private void TxtBrightness_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFromCode) return;
            if (TxtBrightness.Text == "-" || string.IsNullOrWhiteSpace(TxtBrightness.Text)) return;
            if (int.TryParse(TxtBrightness.Text, out int val))
            {
                _isUpdatingFromCode = true;
                SldBrightness.Value = Math.Clamp(val, -100, 100);
                _isUpdatingFromCode = false;
                RefreshImportEnabled();
            }
        }

        private void SldContrast_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingFromCode || TxtContrast == null) return;
            _isUpdatingFromCode = true;
            TxtContrast.Text = ((int)e.NewValue).ToString(CultureInfo.InvariantCulture);
            _isUpdatingFromCode = false;
            RefreshImportEnabled();
        }

        private void TxtContrast_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFromCode) return;
            if (TxtContrast.Text == "-" || string.IsNullOrWhiteSpace(TxtContrast.Text)) return;
            if (int.TryParse(TxtContrast.Text, out int val))
            {
                _isUpdatingFromCode = true;
                SldContrast.Value = Math.Clamp(val, -100, 100);
                _isUpdatingFromCode = false;
                RefreshImportEnabled();
            }
        }

        private void SldDitherAmount_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingFromCode || TxtDitherAmount == null) return;
            _isUpdatingFromCode = true;
            TxtDitherAmount.Text = ((int)e.NewValue).ToString(CultureInfo.InvariantCulture);
            _isUpdatingFromCode = false;
            RefreshImportEnabled();
        }

        private void TxtDitherAmount_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFromCode) return;
            if (string.IsNullOrWhiteSpace(TxtDitherAmount.Text)) return;
            if (int.TryParse(TxtDitherAmount.Text, out int val))
            {
                _isUpdatingFromCode = true;
                SldDitherAmount.Value = Math.Clamp(val, 0, 100);
                _isUpdatingFromCode = false;
                RefreshImportEnabled();
            }
        }

        private void AnySettingChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingFromCode) return;
            RefreshImportEnabled();
        }

        private CancellationTokenSource? _debounceCts;
        private async void RefreshImportEnabled()
        {
            if (_isUpdatingFromCode) return;

            if (!_isApplyingPreset && PresetCombo != null && PresetCombo.SelectedValue is ImportPreset preset && preset != ImportPreset.Custom)
            {
                _isUpdatingFromCode = true;
                PresetCombo.SelectedValue = ImportPreset.Custom;
                PresetCombo.ToolTip = ImportPresetHelper.GetPresetDescription(ImportPreset.Custom);
                SyncPresetChips(ImportPreset.Custom);
                _isUpdatingFromCode = false;
            }

            bool isValid = TryReadSettings(out BitmapImportSettings? settings);
            if (isValid && settings != null)
            {
                var oldCts = _debounceCts;
                _debounceCts = new CancellationTokenSource();
                oldCts?.Cancel();
                oldCts?.Dispose();
                try
                {
                    await Task.Delay(300, _debounceCts.Token);
                    UpdatePreviewAsync(settings);
                }
                catch (TaskCanceledException) { }
            }
        }

        private static Task<T> RunOnStaThreadAsync<T>(Func<T> func)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            var thread = new Thread(() =>
            {
                try
                {
                    T result = func();
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            })
            {
                IsBackground = true,
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            return tcs.Task;
        }

        private async void UpdatePreviewAsync(BitmapImportSettings settings)
        {
            if (string.IsNullOrEmpty(_sourceFileName) || !System.IO.File.Exists(_sourceFileName))
            {
                return;
            }

            _previewCts?.Cancel();
            _previewCts?.Dispose();
            _previewCts = new CancellationTokenSource();
            var token = _previewCts.Token;

            LoadingOverlay.Visibility = Visibility.Visible;

            try
            {
                // Offload work to a background STA thread
                var result = await RunOnStaThreadAsync(() => 
                {
                    // ConvertTo1Bit is synchronous and CPU-bound, but requires STA for WPF imaging components
                    return BitmapToMonochromeConverter.ConvertTo1Bit(_sourceFileName, settings);
                });

                if (token.IsCancellationRequested)
                    return;

                // Create WriteableBitmap on the UI thread
                var bitmap = new WriteableBitmap(result.Width, result.Height, 96, 96, PixelFormats.Bgra32, palette: null);
                
                var res = Application.Current.Resources;
                var colorOff = (res["Brush.Canvas.Pixel"] as SolidColorBrush)?.Color ?? Colors.Transparent;
                var colorOn = (res["Brush.Canvas.Drawing"] as SolidColorBrush)?.Color ?? Colors.White;

                uint colorOffUint = (uint)(colorOff.B | (colorOff.G << 8) | (colorOff.R << 16) | (colorOff.A << 24));
                uint colorOnUint = (uint)(colorOn.B | (colorOn.G << 8) | (colorOn.R << 16) | (colorOn.A << 24));

                uint[] pixels = new uint[result.Width * result.Height];
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = result.Pixels[i] ? colorOnUint : colorOffUint;
                }

                bitmap.WritePixels(new Int32Rect(0, 0, result.Width, result.Height), pixels, result.Width * 4, 0);

                PreviewImage.Source = bitmap;
                
                if (_shouldFitPreviewToFrame)
                {
                    _shouldFitPreviewToFrame = false;
                    FitPreviewToFrame();
                }
            }
            catch (Exception)
            {
                // In case of error (e.g. file deleted while tweaking settings), we just clear the preview
                if (!token.IsCancellationRequested)
                {
                    PreviewImage.Source = null;
                }
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                }
            }
        }

        private bool _isPanning;
        private Point _lastPanPosition;

        private void Preview_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double zoomAmount = e.Delta > 0 ? 1.2 : 1.0 / 1.2;
            double oldScale = PreviewScaleTransform.ScaleX;
            double newScale = Math.Clamp(oldScale * zoomAmount, 0.1, 50.0);

            if (Math.Abs(newScale - oldScale) < 0.001) return;

            var mouseInContent = e.GetPosition(PreviewContainer);
            var mouseInViewer = e.GetPosition(PreviewScrollViewer);

            PreviewScaleTransform.ScaleX = newScale;
            PreviewScaleTransform.ScaleY = newScale;

            PreviewScrollViewer.UpdateLayout();

            var targetPointInViewer = PreviewContainer.TranslatePoint(mouseInContent, PreviewScrollViewer);

            PreviewScrollViewer.ScrollToHorizontalOffset(PreviewScrollViewer.HorizontalOffset + targetPointInViewer.X - mouseInViewer.X);
            PreviewScrollViewer.ScrollToVerticalOffset(PreviewScrollViewer.VerticalOffset + targetPointInViewer.Y - mouseInViewer.Y);

            e.Handled = true;
        }

        private void Preview_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left || e.ChangedButton == MouseButton.Middle)
            {
                _isPanning = true;
                _lastPanPosition = e.GetPosition(this);
                PreviewScrollViewer.CaptureMouse();
                Cursor = Cursors.SizeAll;
                e.Handled = true;
            }
        }

        private void Preview_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning && (e.ChangedButton == MouseButton.Left || e.ChangedButton == MouseButton.Middle))
            {
                _isPanning = false;
                PreviewScrollViewer.ReleaseMouseCapture();
                Cursor = Cursors.Arrow;
                e.Handled = true;
            }
        }

        private void Preview_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                var currentPos = e.GetPosition(this);
                var delta = currentPos - _lastPanPosition;
                
                PreviewScrollViewer.ScrollToHorizontalOffset(PreviewScrollViewer.HorizontalOffset - delta.X);
                PreviewScrollViewer.ScrollToVerticalOffset(PreviewScrollViewer.VerticalOffset - delta.Y);

                _lastPanPosition = currentPos;
                e.Handled = true;
            }
        }

        private bool TryReadSettings(out BitmapImportSettings? settings)
        {
            settings = null;

            // Even if TxtThreshold is disabled, parse it normally (or default to 128 if parsing fails)
            if (!int.TryParse(TxtThreshold.Text, out int threshold) || threshold < 0 || threshold > 255)
                threshold = 128;

            if (!int.TryParse(TxtAlphaThreshold.Text, out int alphaThreshold) || alphaThreshold < 0 || alphaThreshold > 255)
                return false;

            if (!int.TryParse(TxtMaxDimension.Text, out int maxDimension) || maxDimension < 1 || maxDimension > SpriteState.MaxDimension)
                return false;

            if (!int.TryParse(TxtBrightness.Text, out int brightness))
                brightness = 0;
            brightness = Math.Clamp(brightness, -100, 100);

            if (!int.TryParse(TxtContrast.Text, out int contrast))
                contrast = 0;
            contrast = Math.Clamp(contrast, -100, 100);

            if (!int.TryParse(TxtDitherAmount.Text, out int ditherAmount))
                ditherAmount = 100;
            ditherAmount = Math.Clamp(ditherAmount, 0, 100);

            if (DitherCombo.SelectedItem is not BitmapDitheringAlgorithm algorithm)
                return false;

            if (ScalingCombo.SelectedItem is not Hexprite.Services.BitmapScalingMode scalingMode)
                return false;

            var activePreset = PresetCombo?.SelectedValue is ImportPreset p ? p : ImportPreset.Custom;

            settings = new BitmapImportSettings
            {
                Preset = activePreset,
                DitheringAlgorithm = algorithm,
                ScalingMode = scalingMode,
                Threshold = threshold,
                AlphaThreshold = alphaThreshold,
                MaxDimension = maxDimension,
                Brightness = brightness,
                Contrast = contrast,
                DitherAmount = ditherAmount,
                Sharpen = ChkSharpen.IsChecked == true,
                Invert = ChkInvert.IsChecked == true,
                UseSerpentineScanning = ChkSerpentine.IsChecked == true,
                UseGammaCorrection = ChkGammaCorrection.IsChecked == true,
                UseAdaptiveThresholding = ChkAdaptiveThreshold.IsChecked == true,
                PreserveEdges = ChkPreserveEdges.IsChecked == true,
            };

            return true;
        }

        private void FitPreviewToFrame()
        {
            if (PreviewImage.Source == null) return;
            
            PreviewContainer.UpdateLayout();
            PreviewScrollViewer.UpdateLayout();

            double viewportWidth = PreviewScrollViewer.ViewportWidth;
            double viewportHeight = PreviewScrollViewer.ViewportHeight;
            double imageWidth = PreviewImage.Source.Width;
            double imageHeight = PreviewImage.Source.Height;

            if (viewportWidth == 0 || viewportHeight == 0 || imageWidth == 0 || imageHeight == 0) return;

            double padding = 20;
            double scaleX = (viewportWidth - padding) / imageWidth;
            double scaleY = (viewportHeight - padding) / imageHeight;
            
            double scale = Math.Min(scaleX, scaleY);
            scale = Math.Clamp(scale, 0.1, 50.0);

            PreviewScaleTransform.ScaleX = scale;
            PreviewScaleTransform.ScaleY = scale;
            
            PreviewScrollViewer.ScrollToHorizontalOffset(0);
            PreviewScrollViewer.ScrollToVerticalOffset(0);
        }

        private void ZoomActual_Click(object sender, RoutedEventArgs e)
        {
            PreviewScaleTransform.ScaleX = 1.0;
            PreviewScaleTransform.ScaleY = 1.0;
            PreviewScrollViewer.ScrollToHorizontalOffset(0);
            PreviewScrollViewer.ScrollToVerticalOffset(0);
        }

        private void ZoomFit_Click(object sender, RoutedEventArgs e)
        {
            FitPreviewToFrame();
        }

        private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingFromCode || _isApplyingPreset) return;
            if (PresetCombo.SelectedValue is not ImportPreset preset) return;

            PresetCombo.ToolTip = ImportPresetHelper.GetPresetDescription(preset);
            SyncPresetChips(preset);

            if (preset == ImportPreset.Custom) return;

            if (!TryReadSettings(out var currentSettings) || currentSettings == null) 
            {
                currentSettings = new BitmapImportSettings();
            }
            
            ImportPresetHelper.ApplyPreset(preset, currentSettings);
            
            _isApplyingPreset = true;
            _isUpdatingFromCode = true;

            DitherCombo.SelectedItem = currentSettings.DitheringAlgorithm;
            ScalingCombo.SelectedItem = currentSettings.ScalingMode;
            
            SldThreshold.Value = currentSettings.Threshold;
            TxtThreshold.Text = currentSettings.Threshold.ToString(CultureInfo.InvariantCulture);
            
            SldBrightness.Value = currentSettings.Brightness;
            TxtBrightness.Text = currentSettings.Brightness.ToString(CultureInfo.InvariantCulture);

            SldContrast.Value = currentSettings.Contrast;
            TxtContrast.Text = currentSettings.Contrast.ToString(CultureInfo.InvariantCulture);

            SldDitherAmount.Value = currentSettings.DitherAmount;
            TxtDitherAmount.Text = currentSettings.DitherAmount.ToString(CultureInfo.InvariantCulture);
            
            ChkInvert.IsChecked = currentSettings.Invert;
            ChkSerpentine.IsChecked = currentSettings.UseSerpentineScanning;
            ChkGammaCorrection.IsChecked = currentSettings.UseGammaCorrection;
            ChkAdaptiveThreshold.IsChecked = currentSettings.UseAdaptiveThresholding;
            ChkPreserveEdges.IsChecked = currentSettings.PreserveEdges;
            ChkSharpen.IsChecked = currentSettings.Sharpen;

            UpdateThresholdUiState();

            _isUpdatingFromCode = false;
            
            RefreshImportEnabled();

            _isApplyingPreset = false;
        }

        private void PresetChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag && Enum.TryParse<ImportPreset>(tag, out var preset))
            {
                PresetCombo.SelectedValue = preset;
            }
        }

        private void SyncPresetChips(ImportPreset preset)
        {
            if (ChipPresetDefault == null) return;

            ChipPresetDefault.IsChecked = preset == ImportPreset.Default;
            ChipPresetPhoto.IsChecked = preset == ImportPreset.Photo;
            ChipPresetRetroMac.IsChecked = preset == ImportPreset.RetroMac;
            ChipPresetPixelArt.IsChecked = preset == ImportPreset.PixelArt;
            ChipPresetLineArt.IsChecked = preset == ImportPreset.LineArt;
            ChipPresetSolidLogo.IsChecked = preset == ImportPreset.SolidLogo;
            ChipPresetCustom.IsChecked = preset == ImportPreset.Custom;

            if (TxtPresetDescription != null)
            {
                TxtPresetDescription.Text = ImportPresetHelper.GetPresetDescription(preset);
            }
        }

        public void Dispose()
        {
            _previewCts?.Cancel();
            _previewCts?.Dispose();
            _previewCts = null;

            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
        }

        protected override void OnClosed(EventArgs e)
        {
            Dispose();
            base.OnClosed(e);
        }
    }
}
