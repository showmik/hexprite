using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Hexprite.Views
{
    public enum FontImportMode
    {
        System,
        File,
        Sprite,
        BMFont,
    }

    public partial class FontImportDialog : Window
    {
        private readonly List<FontFamily> _allFonts = [];
        private bool _previewInverted;

        public FontImportMode SelectedMode { get; private set; } = FontImportMode.System;

        public FontFamily SelectedFontFamily { get; private set; } = new FontFamily("Consolas");
        public string SelectedFilePath { get; private set; } = "";
        
        public int SelectedHeight { get; private set; } = 8;
        public int SelectedFirstChar { get; private set; } = 32;
        public int SelectedLastChar { get; private set; } = 126;

        public int SpriteCellWidth { get; private set; } = 8;
        public int SpriteCellHeight { get; private set; } = 8;

        public int BaselineOffset { get; private set; }
        public int LetterSpacing { get; private set; } = 1;
        public int Threshold { get; private set; } = 128;
        public bool AntiAlias { get; private set; }

        private readonly Hexprite.Services.IFontImportService _importService;
        private readonly DispatcherTimer _previewTimer;

        public FontImportDialog(Hexprite.Services.IFontImportService importService)
        {
            InitializeComponent();
            _importService = importService;

            _previewTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250),
            };
            _previewTimer.Tick += PreviewTimer_Tick;

            Closing += FontImportDialog_Closing;
            Closed += FontImportDialog_Closed;

            try
            {
                _allFonts = [.. Fonts.SystemFontFamilies.OrderBy(f => f.Source)];
                FontListBox.ItemsSource = _allFonts.Select(f => f.Source).ToList();

                // Select Consolas by default, or fallback to first available font
                var defaultFont = _allFonts.FirstOrDefault(f => string.Equals(f.Source, "Consolas", StringComparison.OrdinalIgnoreCase))
                               ?? _allFonts.FirstOrDefault();
                if (defaultFont != null)
                {
                    SelectedFontFamily = defaultFont;
                    FontListBox.SelectedItem = defaultFont.Source;
                    FontListBox.ScrollIntoView(defaultFont.Source);
                }
            }
            catch
            {
                // Fallback if system fonts enumeration fails
                _allFonts = [new FontFamily("Consolas")];
                FontListBox.ItemsSource = _allFonts.Select(f => f.Source).ToList();
            }

            UpdateUI();
        }

        private void FontImportDialog_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            StopTimer();
        }

        private void FontImportDialog_Closed(object? sender, EventArgs e)
        {
            StopTimer();
        }

        private void StopTimer()
        {
            if (_previewTimer != null)
            {
                _previewTimer.Stop();
                _previewTimer.Tick -= PreviewTimer_Tick;
            }
        }

        private void CaptionClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter && Keyboard.FocusedElement is TextBox tb)
            {
                if (tb == PreviewTextBox || tb == FontSearchBox)
                {
                    RequestPreviewUpdate();
                    e.Handled = true;
                    return;
                }

                tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    HandleDroppedFile(files[0]);
                    e.Handled = true;
                }
            }
        }

        private void HandleDroppedFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext is ".ttf" or ".otf")
            {
                SetSourceMode(FontImportMode.File);
            }
            else if (ext is ".png" or ".bmp")
            {
                SetSourceMode(FontImportMode.Sprite);
            }
            else if (ext is ".fnt")
            {
                SetSourceMode(FontImportMode.BMFont);
            }

            FilePathBox.Text = filePath;
        }

        private void SetSourceMode(FontImportMode mode)
        {
            if (SourceModeCombo == null) return;
            foreach (ComboBoxItem item in SourceModeCombo.Items)
            {
                if (item.Tag is string tag && Enum.TryParse<FontImportMode>(tag, out var m) && m == mode)
                {
                    SourceModeCombo.SelectedItem = item;
                    break;
                }
            }
        }

        private void SourceModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SourceModeCombo == null) return;
            if (SourceModeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                if (Enum.TryParse<FontImportMode>(tag, out var mode))
                {
                    SelectedMode = mode;
                    UpdateUI();
                }
            }
        }

        private void UpdateUI()
        {
            if (SystemFontPanel == null) return;

            SystemFontPanel.Visibility = SelectedMode == FontImportMode.System ? Visibility.Visible : Visibility.Collapsed;
            FilePickerPanel.Visibility = SelectedMode != FontImportMode.System ? Visibility.Visible : Visibility.Collapsed;
            SpriteSettingsPanel.Visibility = SelectedMode == FontImportMode.Sprite ? Visibility.Visible : Visibility.Collapsed;

            if (SelectedMode == FontImportMode.BMFont)
            {
                CommonSettingsPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                CommonSettingsPanel.Visibility = Visibility.Visible;
                bool showHeight = SelectedMode == FontImportMode.System || SelectedMode == FontImportMode.File;
                HeightPanel.Visibility = showHeight ? Visibility.Visible : Visibility.Collapsed;
                AntiAliasPanel.Visibility = showHeight ? Visibility.Visible : Visibility.Collapsed;
            }

            CheckImportReady();
            RequestPreviewUpdate();
        }

        private void Input_CheckChanged(object sender, RoutedEventArgs e)
        {
            RequestPreviewUpdate();
        }

        private void ClearFontSearch_Click(object sender, RoutedEventArgs e)
        {
            FontSearchBox.Text = string.Empty;
        }

        private void FontSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var filter = FontSearchBox.Text;
            if (string.IsNullOrWhiteSpace(filter))
            {
                FontListBox.ItemsSource = _allFonts.Select(f => f.Source).ToList();
                EmptyFontSearchText.Visibility = Visibility.Collapsed;
            }
            else
            {
                var filtered = _allFonts
                    .Where(f => f.Source.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .Select(f => f.Source)
                    .ToList();
                FontListBox.ItemsSource = filtered;
                EmptyFontSearchText.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void FontListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontListBox.SelectedItem is string fontName)
            {
                var family = _allFonts.FirstOrDefault(f => f.Source == fontName);
                if (family != null)
                {
                    SelectedFontFamily = family;
                    RequestPreviewUpdate();
                }
            }
            CheckImportReady();
        }

        private void FilePathBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SelectedFilePath = FilePathBox.Text.Trim();
            RequestPreviewUpdate();
            CheckImportReady();
        }

        private void BrowseFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            
            if (SelectedMode == FontImportMode.File)
                dlg.Filter = "Font Files (*.ttf;*.otf)|*.ttf;*.otf|All Files (*.*)|*.*";
            else if (SelectedMode == FontImportMode.Sprite)
                dlg.Filter = "Image Files (*.png;*.bmp)|*.png;*.bmp|All Files (*.*)|*.*";
            else if (SelectedMode == FontImportMode.BMFont)
                dlg.Filter = "AngelCode BMFont (*.fnt)|*.fnt|All Files (*.*)|*.*";

            if (dlg.ShowDialog() == true)
            {
                FilePathBox.Text = dlg.FileName;
            }
        }

        private void CheckImportReady()
        {
            if (ImportButton == null) return;

            if (SelectedMode == FontImportMode.System)
            {
                ImportButton.IsEnabled = FontListBox != null && FontListBox.SelectedItem != null;
            }
            else
            {
                ImportButton.IsEnabled = !string.IsNullOrWhiteSpace(FilePathBox?.Text)
                    && File.Exists(FilePathBox.Text.Trim());
            }
        }

        private void SetValidationStatus(string message, bool isWarning)
        {
            if (ValidationStatusText == null) return;
            ValidationStatusText.Text = message;
            ValidationStatusText.Foreground = isWarning
                ? (Brush)FindResource("Brush.Status.Warning")
                : (Brush)FindResource("Brush.Text.Muted");
        }

        private void BtnPresetFox_Click(object sender, RoutedEventArgs e)
        {
            PreviewTextBox.Text = "The quick brown fox jumps over the lazy dog";
        }

        private void BtnPresetDigits_Click(object sender, RoutedEventArgs e)
        {
            PreviewTextBox.Text = "0123456789 +-/*= (%)";
        }

        private void BtnPresetAa_Click(object sender, RoutedEventArgs e)
        {
            PreviewTextBox.Text = "ABCDEFGHIJKLMNOPQRSTUVWXYZ abcdefghijklmnopqrstuvwxyz";
        }

        private void BtnPresetSymbols_Click(object sender, RoutedEventArgs e)
        {
            PreviewTextBox.Text = "!@#$%^&*()_+-=[]{}|;:'\",.<>?/`~";
        }

        private void InvertToggle_Click(object sender, RoutedEventArgs e)
        {
            _previewInverted = InvertToggle.IsChecked == true;
            GeneratePreview();
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedMode == FontImportMode.System)
            {
                if (FontListBox.SelectedItem is not string fontName) return;
                var family = _allFonts.FirstOrDefault(f => f.Source == fontName);
                if (family == null)
                {
                    MessageDialog.Show("Could not find the selected font.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                SelectedFontFamily = family;
            }
            else
            {
                SelectedFilePath = FilePathBox.Text.Trim();
                if (string.IsNullOrEmpty(SelectedFilePath) || !File.Exists(SelectedFilePath))
                {
                    MessageDialog.Show("File does not exist.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            if (SelectedMode != FontImportMode.BMFont)
            {
                if (!int.TryParse(FirstCharBox.Text, out int firstChar) || firstChar < 0)
                {
                    MessageDialog.Show("First char must be a non-negative integer.", "Invalid Range", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                SelectedFirstChar = firstChar;

                if (!int.TryParse(LastCharBox.Text, out int lastChar) || lastChar <= firstChar || lastChar > 65535)
                {
                    MessageDialog.Show("Last char must be greater than first char and up to 65535.", "Invalid Range", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                SelectedLastChar = lastChar;
                
                if (lastChar - firstChar > 2048)
                {
                    MessageDialog.Show("You can only import up to 2048 characters at a time.", "Range Too Large", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (SelectedMode == FontImportMode.System || SelectedMode == FontImportMode.File)
                {
                    if (!int.TryParse(HeightBox.Text, out int height) || height < 4 || height > 128)
                    {
                        MessageDialog.Show("Height must be between 4 and 128.", "Invalid Height", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    SelectedHeight = height;
                }
                else if (SelectedMode == FontImportMode.Sprite)
                {
                    if (!int.TryParse(CellWidthBox.Text, out int cellW) || cellW < 1 || cellW > 128)
                    {
                        MessageDialog.Show("Cell width must be between 1 and 128.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (!int.TryParse(CellHeightBox.Text, out int cellH) || cellH < 1 || cellH > 128)
                    {
                        MessageDialog.Show("Cell height must be between 1 and 128.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    SpriteCellWidth = cellW;
                    SpriteCellHeight = cellH;
                }

                if (!int.TryParse(BaselineOffsetBox.Text, out int bOffset) || bOffset < -128 || bOffset > 128)
                {
                    MessageDialog.Show("Baseline offset must be an integer between -128 and 128.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                BaselineOffset = bOffset;

                if (!int.TryParse(LetterSpacingBox.Text, out int lSpace) || lSpace < -100 || lSpace > 100)
                {
                    MessageDialog.Show("Letter spacing must be an integer between -100 and 100.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                LetterSpacing = lSpace;

                if (!int.TryParse(ThresholdBox.Text, out int thresh) || thresh < 1 || thresh > 255)
                {
                    MessageDialog.Show("Threshold must be an integer between 1 and 255.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                Threshold = thresh;
                AntiAlias = AntiAliasBox.IsChecked == true;
            }
            else
            {
                Threshold = 128;
                AntiAlias = false;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Input_TextChanged(object sender, TextChangedEventArgs e)
        {
            RequestPreviewUpdate();
        }

        private void RequestPreviewUpdate()
        {
            if (_previewTimer == null) return;
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        private void PreviewTimer_Tick(object? sender, EventArgs e)
        {
            _previewTimer.Stop();
            GeneratePreview();
        }

        private void GeneratePreview()
        {
            if (_importService == null || PreviewImage == null) return;

            // Safe parsing and bounded clamps
            int height = int.TryParse(HeightBox?.Text, out int parsedH) ? Math.Clamp(parsedH, 4, 128) : 8;
            int firstChar = int.TryParse(FirstCharBox?.Text, out int parsedFc) ? parsedFc : 32;
            int lastChar = int.TryParse(LastCharBox?.Text, out int parsedLc) ? parsedLc : 126;
            int bOffset = int.TryParse(BaselineOffsetBox?.Text, out int parsedBo) ? Math.Clamp(parsedBo, -128, 128) : 0;
            int lSpace = int.TryParse(LetterSpacingBox?.Text, out int parsedLs) ? Math.Clamp(parsedLs, -100, 100) : 1;
            int thresh = int.TryParse(ThresholdBox?.Text, out int parsedTh) ? Math.Clamp(parsedTh, 1, 255) : 128;
            bool antiAlias = AntiAliasBox?.IsChecked == true;

            int cellW = int.TryParse(CellWidthBox?.Text, out int parsedW) ? Math.Clamp(parsedW, 1, 128) : 8;
            int cellH = int.TryParse(CellHeightBox?.Text, out int parsedCh) ? Math.Clamp(parsedCh, 1, 128) : 8;

            // Input Validation Guard: check bounds to prevent UI freezes
            if (SelectedMode != FontImportMode.BMFont)
            {
                if (firstChar < 0 || lastChar < firstChar || (lastChar - firstChar) > 2048)
                {
                    SetValidationStatus("Invalid character range: Last Char must be ≥ First Char (max 2048 glyphs).", isWarning: true);
                    PreviewImage.Source = null;
                    return;
                }
            }

            if (SelectedMode != FontImportMode.System)
            {
                if (string.IsNullOrWhiteSpace(SelectedFilePath) || !File.Exists(SelectedFilePath))
                {
                    SetValidationStatus("Selected file does not exist.", isWarning: true);
                    PreviewImage.Source = null;
                    return;
                }
            }

            var options = new Hexprite.Services.FontImportOptions
            {
                TargetHeight = height,
                FirstChar = firstChar,
                LastChar = lastChar,
                BaselineOffset = bOffset,
                LetterSpacing = lSpace,
                Threshold = thresh,
                AntiAlias = antiAlias,
            };

            Hexprite.Core.FontDocument? doc = null;

            try
            {
                switch (SelectedMode)
                {
                    case FontImportMode.System:
                        if (SelectedFontFamily != null)
                            doc = _importService.ImportFromTrueType(SelectedFontFamily, options);
                        break;
                    case FontImportMode.File:
                        if (File.Exists(SelectedFilePath))
                            doc = _importService.ImportFromFontFile(SelectedFilePath, options);
                        break;
                    case FontImportMode.Sprite:
                        if (File.Exists(SelectedFilePath) && cellW > 0 && cellH > 0)
                            doc = _importService.ImportFromSpriteSheet(SelectedFilePath, cellW, cellH, options);
                        break;
                    case FontImportMode.BMFont:
                        if (File.Exists(SelectedFilePath))
                            doc = _importService.ImportFromBMFont(SelectedFilePath, options);
                        break;
                }
            }
            catch (Exception ex)
            {
                SetValidationStatus($"Preview unavailable: {ex.Message}", isWarning: true);
                PreviewImage.Source = null;
                return;
            }

            if (doc == null || doc.Glyphs == null || doc.Glyphs.Count == 0)
            {
                SetValidationStatus("No glyphs extracted from source.", isWarning: true);
                PreviewImage.Source = null;
                return;
            }

            string previewText = PreviewTextBox?.Text ?? "";
            if (string.IsNullOrEmpty(previewText))
            {
                SetValidationStatus("Type text above to preview.", isWarning: false);
                PreviewImage.Source = null;
                return;
            }

            var glyphList = new List<Hexprite.Core.GlyphState>();
            int totalWidth = 0;
            int minYOffset = 0;
            int maxYExtent = doc.CellHeight;

            foreach (char c in previewText)
            {
                int cp = (int)c;
                var g = doc.Glyphs.FirstOrDefault(x => x.CodePoint == cp);
                if (g != null)
                {
                    glyphList.Add(g);
                    totalWidth += g.XAdvance;

                    if (g.YOffset < minYOffset) minYOffset = g.YOffset;
                    int glyphBottom = g.YOffset + g.Height;
                    if (glyphBottom > maxYExtent) maxYExtent = glyphBottom;
                }
            }

            int renderHeight = Math.Max(1, maxYExtent - minYOffset);

            if (totalWidth <= 0 || renderHeight <= 0 || glyphList.Count == 0)
            {
                SetValidationStatus($"No matching glyphs in font for preview text ({doc.Glyphs.Count} available).", isWarning: true);
                PreviewImage.Source = null;
                return;
            }

            // Draw WriteableBitmap (crisp 1-bit rendered to 32-bit display)
            var wb = new WriteableBitmap(totalWidth, renderHeight, 96, 96, PixelFormats.Bgra32, palette: null);
            int stride = totalWidth * 4;
            byte[] pixels = new byte[renderHeight * stride];

            // If inverted, fill background with white (255, 255, 255, 255)
            if (_previewInverted)
            {
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    pixels[i] = 255;
                    pixels[i + 1] = 255;
                    pixels[i + 2] = 255;
                    pixels[i + 3] = 255;
                }
            }

            int currentX = 0;
            foreach (var g in glyphList)
            {
                int drawX = currentX + g.XOffset;
                int drawY = g.YOffset - minYOffset;

                for (int y = 0; y < g.Height; y++)
                {
                    for (int x = 0; x < g.Width; x++)
                    {
                        if (g.Pixels[y * g.Width + x])
                        {
                            int px = drawX + x;
                            int py = drawY + y;
                            if (px >= 0 && px < totalWidth && py >= 0 && py < renderHeight)
                            {
                                int idx = py * stride + px * 4;
                                if (_previewInverted)
                                {
                                    pixels[idx] = 0;     // B
                                    pixels[idx + 1] = 0; // G
                                    pixels[idx + 2] = 0; // R
                                    pixels[idx + 3] = 255;
                                }
                                else
                                {
                                    pixels[idx] = 255;     // B
                                    pixels[idx + 1] = 255; // G
                                    pixels[idx + 2] = 255; // R
                                    pixels[idx + 3] = 255;
                                }
                            }
                        }
                    }
                }
                currentX += g.XAdvance;
            }

            wb.WritePixels(new Int32Rect(0, 0, totalWidth, renderHeight), pixels, stride, 0);
            PreviewImage.Source = wb;

            SetValidationStatus($"Ready: {doc.Glyphs.Count} glyphs mapped (Cell: {doc.MaxCellWidth}×{doc.CellHeight}px, Baseline: {doc.Baseline}px).", isWarning: false);
        }
    }
}

