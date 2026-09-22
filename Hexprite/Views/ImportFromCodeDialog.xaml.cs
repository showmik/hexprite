using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Hexprite.Core;
using Hexprite.Services;

namespace Hexprite.Views
{
    /// <summary>
    /// Modal dialog for importing sprite data from pasted code.
    /// Auto-detects width/height from NAME_WIDTH / NAME_HEIGHT constants
    /// and lets the user override them before importing.
    /// </summary>
    public sealed partial class ImportFromCodeDialog : Window
    {
        [GeneratedRegex(@"^[0-9]+$", RegexOptions.None, matchTimeoutMilliseconds: 250)]
        private static partial Regex DigitsOnlyRegex { get; }

        private enum PreviewPalette
        {
            WhiteOled,
            AmberGlow,
            MatrixGreen,
            CyanBlue,
        }

        /// <summary>
        /// The result of a successful import.
        /// Width and Height are the resolved canvas dimensions;
        /// Code is the raw pasted text for the service to parse;
        /// SpriteName is the auto-detected variable name (may be null);
        /// Invert indicates whether to invert bit polarity.
        /// </summary>
        public (int Width, int Height, string Code, string? SpriteName, ExportFormat Format, bool Invert)? Result { get; private set; }

        private ExportFormat _detectedFormat;
        private string? _detectedSpriteName;
        private bool _suppressAutoDetect;
        private bool _suppressFormatEvent;
        private bool _formatManuallyOverridden;
        private PreviewPalette _selectedPalette = PreviewPalette.WhiteOled;

        public ImportFromCodeDialog()
        {
            _suppressFormatEvent = true;
            InitializeComponent();
            _suppressFormatEvent = false;
            _formatManuallyOverridden = false;
            var prefs = UserPreferencesService.Get();
            _suppressAutoDetect = true;
            TxtWidth.Text = prefs.ImportFromCodeWidth.ToString(CultureInfo.InvariantCulture);
            TxtHeight.Text = prefs.ImportFromCodeHeight.ToString(CultureInfo.InvariantCulture);
            _suppressAutoDetect = false;
            if (TxtDetectionHint != null)
                TxtDetectionHint.Text = "(last used)";
            UpdateStats();
        }

        // ── Auto-detect dimensions from pasted code ──────────────────────

        private void TxtCode_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs? e)
        {
            string code = TxtCode.Text;

            if (string.IsNullOrWhiteSpace(code))
            {
                _formatManuallyOverridden = false;
            }

            if (ImportFromCodeDetector.TryParseExplicitDimensions(code, out int parsedW, out int parsedH, out string detectionSource))
            {
                _suppressAutoDetect = true;
                TxtWidth.Text  = parsedW.ToString(CultureInfo.InvariantCulture);
                TxtHeight.Text = parsedH.ToString(CultureInfo.InvariantCulture);
                _suppressAutoDetect = false;
                TxtDetectionHint.Text = $"(auto-detected from {detectionSource})";
            }
            else
            {
                int byteCount = ImportFromCodeDetector.CountDataBytes(code);

                if (byteCount > 0)
                {
                    if (ImportFromCodeDetector.TryInferDimensionsFromData(code, byteCount, out int iw, out int ih,
                            out ImportDimensionInferHint inferHint))
                    {
                        _suppressAutoDetect = true;
                        TxtWidth.Text  = iw.ToString(CultureInfo.InvariantCulture);
                        TxtHeight.Text = ih.ToString(CultureInfo.InvariantCulture);
                        _suppressAutoDetect = false;
                        TxtDetectionHint.Text = inferHint switch
                        {
                            ImportDimensionInferHint.FromLineStructure =>
                                string.Create(CultureInfo.InvariantCulture, $"(detected from code structure — {byteCount} bytes)"),
                            ImportDimensionInferHint.AmbiguousLineStructure =>
                                "(line structure ambiguous — guessed from byte count)",
                            _ =>
                                string.Create(CultureInfo.InvariantCulture, $"(guessed from {byteCount} bytes — adjust if needed)"),
                        };
                    }
                }
                else
                {
                    TxtDetectionHint.Text = "";
                }
            }

            _detectedSpriteName = ImportFromCodeDetector.DetectVariableName(code);
            
            if (!_formatManuallyOverridden)
            {
                if (ImportFromCodeDetector.IsLikelyLiquidCrystalFormat(code))
                    SetSelectedFormat(ExportFormat.LiquidCrystalChar);
                else if (ImportFromCodeDetector.IsLikely2DMatrixFormat(code))
                    SetSelectedFormat(ExportFormat.Indexed2D);
                else if (ImportFromCodeDetector.IsLikelyXbmFormat(code))
                    SetSelectedFormat(ExportFormat.U8g2DrawXBM);
                else if (ImportFromCodeDetector.IsLikelyBinaryFormat(code))
                    SetSelectedFormat(ExportFormat.RawBinary);
                else
                    SetSelectedFormat(ExportFormat.AdafruitGfx);
            }

            UpdateStats();

            if (_detectedFormat == ExportFormat.LiquidCrystalChar && TxtDetectionHint != null)
            {
                if (string.IsNullOrWhiteSpace(TxtDetectionHint.Text))
                    TxtDetectionHint.Text = "(LiquidCrystal 1602 — 5×8)";
                else if (!TxtDetectionHint.Text.Contains("LiquidCrystal", StringComparison.OrdinalIgnoreCase))
                    TxtDetectionHint.Text += " • LiquidCrystal (5×8)";
            }
            else if (_detectedFormat == ExportFormat.Indexed2D && TxtDetectionHint != null)
            {
                if (string.IsNullOrWhiteSpace(TxtDetectionHint.Text))
                    TxtDetectionHint.Text = "(2D Matrix — 1 byte/px)";
                else if (!TxtDetectionHint.Text.Contains("2D", StringComparison.OrdinalIgnoreCase))
                    TxtDetectionHint.Text += " • 2D Matrix";
            }
            else if (_detectedFormat == ExportFormat.U8g2DrawXBM && TxtDetectionHint != null)
            {
                if (string.IsNullOrWhiteSpace(TxtDetectionHint.Text))
                    TxtDetectionHint.Text = "(XBM layout — bit-reversed)";
                else if (!TxtDetectionHint.Text.Contains("XBM", StringComparison.OrdinalIgnoreCase))
                    TxtDetectionHint.Text += " • XBM";
            }
            else if (_detectedFormat == ExportFormat.RawBinary && TxtDetectionHint != null)
            {
                if (string.IsNullOrWhiteSpace(TxtDetectionHint.Text))
                    TxtDetectionHint.Text = "(Raw Binary / Bit Grid)";
                else if (!TxtDetectionHint.Text.Contains("Binary", StringComparison.OrdinalIgnoreCase))
                    TxtDetectionHint.Text += " • Binary";
            }
        }

        private void SetSelectedFormat(ExportFormat format)
        {
            _detectedFormat = format;
            if (CboFormat == null) return;
            _suppressFormatEvent = true;
            foreach (System.Windows.Controls.ComboBoxItem item in CboFormat.Items)
            {
                if (item.Tag is string tag && Enum.TryParse<ExportFormat>(tag, out var itemFormat) && itemFormat == format)
                {
                    CboFormat.SelectedItem = item;
                    break;
                }
            }
            _suppressFormatEvent = false;
        }

        private void CboFormat_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_suppressFormatEvent || CboFormat == null) return;
            if (CboFormat.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
                item.Tag is string tag &&
                Enum.TryParse<ExportFormat>(tag, out var format))
            {
                if (IsLoaded && (CboFormat.IsDropDownOpen || CboFormat.IsKeyboardFocusWithin || CboFormat.IsMouseOver))
                {
                    _formatManuallyOverridden = true;
                }
                _detectedFormat = format;
                UpdateStats();
            }
        }

        private void ChkDisplayOption_Changed(object sender, RoutedEventArgs e)
        {
            UpdatePreview();
        }

        private void Dimension_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_suppressAutoDetect || TxtDetectionHint == null) return;
            TxtDetectionHint.Text = "(manually set)";
            UpdateStats();
        }

        // ── Quick Presets & Helpers ─────────────────────────────────────

        private void Preset8x8_Click(object sender, RoutedEventArgs e) => ApplyDimensions(8, 8, "preset");
        private void Preset16x16_Click(object sender, RoutedEventArgs e) => ApplyDimensions(16, 16, "preset");
        private void Preset32x32_Click(object sender, RoutedEventArgs e) => ApplyDimensions(32, 32, "preset");
        private void Preset128x64_Click(object sender, RoutedEventArgs e) => ApplyDimensions(128, 64, "preset");

        private void PresetAuto_Click(object sender, RoutedEventArgs e)
        {
            _formatManuallyOverridden = false;
            TxtCode_TextChanged(this, e: null);
        }

        private void ApplyDimensions(int w, int h, string source)
        {
            _suppressAutoDetect = true;
            TxtWidth.Text = w.ToString(CultureInfo.InvariantCulture);
            TxtHeight.Text = h.ToString(CultureInfo.InvariantCulture);
            _suppressAutoDetect = false;
            if (TxtDetectionHint != null)
                TxtDetectionHint.Text = $"({source})";
            UpdateStats();
        }

        private void BtnSwap_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtWidth.Text, out int currentWidth) && int.TryParse(TxtHeight.Text, out int currentHeight))
            {
                ApplyDimensions(w: currentHeight, h: currentWidth, "swapped");
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _animationTimer?.Stop();
            _formatManuallyOverridden = false;
            TxtCode.Text = "";
            TxtCode.Focus();
        }

        private void Palette_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string tagStr && Enum.TryParse<PreviewPalette>(tagStr, out var pal))
            {
                _selectedPalette = pal;
                UpdatePreview();
            }
        }

        private int _previewFrameIndex;
        private System.Windows.Threading.DispatcherTimer? _animationTimer;

        // ── Live Preview Rendering ──────────────────────────────────────

        private void UpdatePreview()
        {
            if (ImgPreview == null || TxtCode == null) return;

            string code = TxtCode.Text;
            if (string.IsNullOrWhiteSpace(code))
            {
                ImgPreview.Source = null;
                if (TxtNoPreview != null) TxtNoPreview.Visibility = Visibility.Visible;
                if (TxtPreviewDimBadge != null) TxtPreviewDimBadge.Text = "0 × 0 px";
                if (BorderFrameNav != null) BorderFrameNav.Visibility = Visibility.Collapsed;
                return;
            }

            if (!int.TryParse(TxtWidth.Text, out int w) || w < 1 || w > 512 ||
                !int.TryParse(TxtHeight.Text, out int h) || h < 1 || h > 512)
            {
                ImgPreview.Source = null;
                if (TxtNoPreview != null) TxtNoPreview.Visibility = Visibility.Visible;
                if (TxtPreviewDimBadge != null) TxtPreviewDimBadge.Text = "— × — px";
                if (BorderFrameNav != null) BorderFrameNav.Visibility = Visibility.Collapsed;
                return;
            }

            try
            {
                var tempState = new SpriteState(w, h);
                var codeGen = new CodeGeneratorService();

                if (_detectedFormat == ExportFormat.Indexed2D)
                    codeGen.ParseIndexed2DToState(code, tempState);
                else if (_detectedFormat == ExportFormat.U8g2DrawXBM)
                    codeGen.ParseXbmToState(code, tempState);
                else if (_detectedFormat == ExportFormat.RawBinary)
                    codeGen.ParseBinaryToState(code, tempState);
                else if (_detectedFormat == ExportFormat.LiquidCrystalChar)
                    codeGen.ParseLiquidCrystalToState(code, tempState);
                else
                    codeGen.ParseAdafruitGfxToState(code, tempState);

                int frameCount = tempState.Frames.Count;
                if (frameCount > 1)
                {
                    _previewFrameIndex = ((_previewFrameIndex % frameCount) + frameCount) % frameCount;
                    if (BorderFrameNav != null) BorderFrameNav.Visibility = Visibility.Visible;
                    if (TxtFrameNavIndicator != null) TxtFrameNavIndicator.Text = string.Create(CultureInfo.InvariantCulture, $"Frame {_previewFrameIndex + 1} of {frameCount}");
                    if (TxtPreviewDimBadge != null) TxtPreviewDimBadge.Text = string.Create(CultureInfo.InvariantCulture, $"{w} × {h} px • {frameCount} frames");
                }
                else
                {
                    _previewFrameIndex = 0;
                    if (_animationTimer != null && _animationTimer.IsEnabled)
                    {
                        _animationTimer.Stop();
                        SetPlayButtonState(isPlaying: false);
                    }
                    if (BorderFrameNav != null) BorderFrameNav.Visibility = Visibility.Collapsed;
                    if (TxtPreviewDimBadge != null) TxtPreviewDimBadge.Text = string.Create(CultureInfo.InvariantCulture, $"{w} × {h} px");
                }

                bool[] activePixels = tempState.Frames[_previewFrameIndex].LayerPixels[0].GetMonochromeData();

                bool invert = ChkInvert?.IsChecked == true;
                bool showGrid = ChkPixelGrid?.IsChecked == true && w <= 64 && h <= 64;

                (uint onColor, uint offColor, uint gridColor) = _selectedPalette switch
                {
                    PreviewPalette.AmberGlow => (0xFFFFB000, 0xFF181000, 0xFF2D1E00),
                    PreviewPalette.MatrixGreen => (0xFF00FF66, 0xFF001808, 0xFF002E10),
                    PreviewPalette.CyanBlue => (0xFF00E5FF, 0xFF001420, 0xFF002B40),
                    _ => (0xFFFFFFFF, 0xFF0E1217, 0xFF1E2630),
                };

                int scale = showGrid ? Math.Max(2, Math.Min(16, 240 / Math.Max(w, h))) : 1;
                int bmpW = w * scale;
                int bmpH = h * scale;

                var wbm = new System.Windows.Media.Imaging.WriteableBitmap(
                    bmpW, bmpH, 96, 96, System.Windows.Media.PixelFormats.Bgra32, palette: null);

                uint[] pixels32 = new uint[bmpW * bmpH];

                if (scale == 1)
                {
                    for (int i = 0; i < activePixels.Length; i++)
                    {
                        bool on = activePixels[i];
                        if (invert) on = !on;
                        pixels32[i] = on ? onColor : offColor;
                    }
                }
                else
                {
                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            bool on = activePixels[y * w + x];
                            if (invert) on = !on;
                            uint cellColor = on ? onColor : offColor;

                            for (int dy = 0; dy < scale; dy++)
                            {
                                int py = y * scale + dy;
                                for (int dx = 0; dx < scale; dx++)
                                {
                                    int px = x * scale + dx;
                                    bool isGridLine = (dx == scale - 1 || dy == scale - 1);
                                    pixels32[py * bmpW + px] = isGridLine ? gridColor : cellColor;
                                }
                            }
                        }
                    }
                }

                wbm.WritePixels(new Int32Rect(0, 0, bmpW, bmpH), pixels32, bmpW * 4, 0);

                ImgPreview.Source = wbm;
                if (TxtNoPreview != null) TxtNoPreview.Visibility = Visibility.Collapsed;
            }
            catch
            {
                ImgPreview.Source = null;
                if (TxtNoPreview != null) TxtNoPreview.Visibility = Visibility.Visible;
            }
        }

        internal static readonly System.Windows.Media.Geometry PlayGeometry = System.Windows.Media.Geometry.Parse("M 4,3 L 13,8.5 L 4,14 Z");
        internal static readonly System.Windows.Media.Geometry PauseGeometry = System.Windows.Media.Geometry.Parse("M 3,3 H 6 V 14 H 3 Z M 9,3 H 12 V 14 H 9 Z");

        private void SetPlayButtonState(bool isPlaying)
        {
            if (IconPlayPause != null)
            {
                IconPlayPause.Data = isPlaying ? PauseGeometry : PlayGeometry;
            }
        }

        private void BtnPrevFrame_Click(object sender, RoutedEventArgs e)
        {
            _animationTimer?.Stop();
            SetPlayButtonState(isPlaying: false);
            _previewFrameIndex--;
            UpdatePreview();
        }

        private void BtnNextFrame_Click(object sender, RoutedEventArgs e)
        {
            _animationTimer?.Stop();
            SetPlayButtonState(isPlaying: false);
            _previewFrameIndex++;
            UpdatePreview();
        }

        private void BtnPlayAnimation_Click(object sender, RoutedEventArgs e)
        {
            if (_animationTimer == null)
            {
                _animationTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(200),
                };
                _animationTimer.Tick += (s, ev) =>
                {
                    _previewFrameIndex++;
                    UpdatePreview();
                };
            }

            if (_animationTimer.IsEnabled)
            {
                _animationTimer.Stop();
                SetPlayButtonState(isPlaying: false);
            }
            else
            {
                _animationTimer.Start();
                SetPlayButtonState(isPlaying: true);
            }
        }

        private void UpdateStats()
        {
            if (TxtCode == null || TxtStats == null || BtnImport == null) return;

            string code = TxtCode.Text;
            if (TxtWatermark != null)
                TxtWatermark.Visibility = string.IsNullOrEmpty(code) ? Visibility.Visible : Visibility.Collapsed;

            int byteCount = ImportFromCodeDetector.CountDataBytes(code);

            bool validWidth  = int.TryParse(TxtWidth.Text, out int w) && w > 0 && w <= 512;
            bool validHeight = int.TryParse(TxtHeight.Text, out int h) && h > 0 && h <= 512;

            UpdatePreview();

            if (byteCount == 0)
            {
                TxtStats.Text = "Paste code above to begin.";
                if (TxtStatusIcon != null) { TxtStatusIcon.Text = "ℹ"; TxtStatusIcon.Foreground = TryFindResource("Brush.Text.Muted") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray; }
                if (BorderStatusCard != null) BorderStatusCard.BorderBrush = TryFindResource("Brush.Border.Base") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.DarkGray;
                BtnImport.IsEnabled = false;
                return;
            }

            int expectedBytes = validWidth && validHeight ? ImportFromCodeDetector.ExpectedByteCount(w, h, _detectedFormat) : 0;

            if (!validWidth || !validHeight)
            {
                TxtStats.Text = string.Create(CultureInfo.InvariantCulture, $"Found {byteCount} bytes. Enter valid dimensions (1–512).");
                if (TxtStatusIcon != null) { TxtStatusIcon.Text = "⚠"; TxtStatusIcon.Foreground = TryFindResource("Brush.Status.Warning") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Orange; }
                if (BorderStatusCard != null) BorderStatusCard.BorderBrush = TryFindResource("Brush.Status.Warning") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Orange;
                BtnImport.IsEnabled = false;
            }
            else if (expectedBytes > 0 && byteCount >= expectedBytes)
            {
                if (byteCount == expectedBytes)
                {
                    TxtStats.Text = string.Create(CultureInfo.InvariantCulture, $"Found {byteCount} bytes — perfect match for {w}×{h}.");
                    if (TxtStatusIcon != null) { TxtStatusIcon.Text = "✓"; TxtStatusIcon.Foreground = TryFindResource("Brush.Status.Success") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Green; }
                    if (BorderStatusCard != null) BorderStatusCard.BorderBrush = TryFindResource("Brush.Status.Success") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Green;
                    BtnImport.IsEnabled = true;
                }
                else if (byteCount % expectedBytes == 0)
                {
                    int frames = byteCount / expectedBytes;
                    TxtStats.Text = string.Create(CultureInfo.InvariantCulture, $"Found {byteCount} bytes — perfect match for {frames} animation frames ({w}×{h} each).");
                    if (TxtStatusIcon != null) { TxtStatusIcon.Text = "✓"; TxtStatusIcon.Foreground = TryFindResource("Brush.Status.Success") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Green; }
                    if (BorderStatusCard != null) BorderStatusCard.BorderBrush = TryFindResource("Brush.Status.Success") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Green;
                    BtnImport.IsEnabled = true;
                }
                else
                {
                    int fullFrames = byteCount / expectedBytes;
                    int extra = byteCount % expectedBytes;
                    TxtStats.Text = string.Create(CultureInfo.InvariantCulture, $"Found {byteCount}B ({fullFrames} full frames + {extra}B partial for {w}×{h}).");
                    if (TxtStatusIcon != null) { TxtStatusIcon.Text = "ℹ"; TxtStatusIcon.Foreground = TryFindResource("Brush.Accent.Base") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.DodgerBlue; }
                    if (BorderStatusCard != null) BorderStatusCard.BorderBrush = TryFindResource("Brush.Border.Base") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.DarkGray;
                    BtnImport.IsEnabled = true;
                }
            }
            else
            {
                TxtStats.Text = string.Create(CultureInfo.InvariantCulture, $"Found {byteCount}B (needs {expectedBytes}B for {w}×{h}). Missing data will be zeroed.");
                if (TxtStatusIcon != null) { TxtStatusIcon.Text = "⚠"; TxtStatusIcon.Foreground = TryFindResource("Brush.Status.Warning") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Orange; }
                if (BorderStatusCard != null) BorderStatusCard.BorderBrush = TryFindResource("Brush.Status.Warning") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Orange;
                BtnImport.IsEnabled = true;
            }

            // Append detected name info
            if (_detectedSpriteName != null)
                TxtStats.Text += $"  Name: \"{_detectedSpriteName}\"";
        }

        // ── Input validation ─────────────────────────────────────────────

        private void NumberOnly(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !DigitsOnlyRegex.IsMatch(e.Text);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (BtnImport != null && BtnImport.IsEnabled)
                {
                    Import_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
            }
        }

        // ── Buttons ──────────────────────────────────────────────────────

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            _animationTimer?.Stop();
            if (!int.TryParse(TxtWidth.Text, out int w) || w < 1 || w > 512) return;
            if (!int.TryParse(TxtHeight.Text, out int h) || h < 1 || h > 512) return;

            Result = (w, h, TxtCode.Text, _detectedSpriteName, _detectedFormat, ChkInvert?.IsChecked == true);
            UserPreferencesService.Update(p =>
            {
                p.ImportFromCodeWidth = w;
                p.ImportFromCodeHeight = h;
            });
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _animationTimer?.Stop();
            DialogResult = false;
        }

        private void CaptionClose_Click(object sender, RoutedEventArgs e)
        {
            _animationTimer?.Stop();
            DialogResult = false;
        }
    }
}
