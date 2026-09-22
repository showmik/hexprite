using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Hexprite.Core;
using Hexprite.Services;
using Microsoft.Win32;

namespace Hexprite.Views
{
    public partial class FontExportDialog : Window
    {
        private readonly FontDocument _doc;
        private readonly IFontCodeGeneratorService _codeGenService;
        private readonly DispatcherTimer _copyFeedbackTimer;
        private readonly DispatcherTimer _snippetFeedbackTimer;

        private bool _isInitializing = true;
        private readonly List<int> _matchIndices = [];
        private int _currentMatchIndex = -1;

        public FontDocument Document => _doc;
        public FontExportSettings CurrentSettings { get; }
        public string GeneratedCode => CodeTextBox.Text;
        public string? InitialDirectory { get; set; }

        public FontExportDialog(FontDocument doc)
            : this(doc, ResolveCodeGenService())
        {
        }

        public FontExportDialog(FontDocument doc, IFontCodeGeneratorService codeGenService)
        {
            InitializeComponent();

            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _codeGenService = codeGenService ?? throw new ArgumentNullException(nameof(codeGenService));

            _doc.ExportSettings ??= new FontExportSettings();
            CurrentSettings = _doc.ExportSettings.Clone();
            if (!string.IsNullOrWhiteSpace(_doc.FontName))
            {
                CurrentSettings.FontName = _doc.FontName;
            }
            else if (string.IsNullOrWhiteSpace(CurrentSettings.FontName))
            {
                CurrentSettings.FontName = "myFont";
            }

            _copyFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _copyFeedbackTimer.Tick += (_, _) =>
            {
                _copyFeedbackTimer.Stop();
                ResetCopyCodeButton();
            };

            _snippetFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _snippetFeedbackTimer.Tick += (_, _) =>
            {
                _snippetFeedbackTimer.Stop();
                ResetCopySnippetButton();
            };

            Loaded += FontExportDialog_Loaded;
            StateChanged += FontExportDialog_StateChanged;

            InitializeControlsFromSettings();
            _isInitializing = false;

            RegenerateCodeAndMetrics();
        }

        private static IFontCodeGeneratorService ResolveCodeGenService()
        {
            if (Application.Current is App app &&
                app.Services.GetService(typeof(IFontCodeGeneratorService)) is IFontCodeGeneratorService service)
            {
                return service;
            }

            return new FontCodeGeneratorService();
        }

        private void FontExportDialog_Loaded(object sender, RoutedEventArgs e)
        {
            FontNameTextBox.Focus();
        }

        private void FontExportDialog_StateChanged(object? sender, EventArgs e)
        {
            MaximizeIcon.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        }

        // ── Controls Initialization ──────────────────────────────────────────

        private void InitializeControlsFromSettings()
        {
            FormatComboBox.ItemsSource = Enum.GetValues<FontExportFormat>();
            FormatComboBox.SelectedItem = CurrentSettings.Format;

            FontNameTextBox.Text = CurrentSettings.FontName;
            IncludeGlyphPreviewCheckBox.IsChecked = CurrentSettings.IncludeGlyphPreview;
            IncludeMetricCommentsCheckBox.IsChecked = CurrentSettings.IncludeMetricComments;
            IncludeUsageCommentCheckBox.IsChecked = CurrentSettings.IncludeUsageComment;

            if (CurrentSettings.UppercaseHex)
            {
                RbHexUpper.IsChecked = true;
            }
            else
            {
                RbHexLower.IsChecked = true;
            }

            TitleTextBlock.Text = $"EXPORT FONT — {FontCodeGeneratorService.SanitiseFontName(CurrentSettings.FontName)}";
        }

        // ── Settings Change & Code Generation ────────────────────────────────

        private void OnSettingChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            SyncSettingsFromControls();
            RegenerateCodeAndMetrics();
        }

        private void FontNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;

            CurrentSettings.FontName = FontNameTextBox.Text;
            SyncSettingsFromControls();
            RegenerateCodeAndMetrics();
        }

        private void FontNameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string sanitised = FontCodeGeneratorService.SanitiseFontName(FontNameTextBox.Text);
            if (sanitised != FontNameTextBox.Text)
            {
                FontNameTextBox.Text = sanitised;
                CurrentSettings.FontName = sanitised;
            }
            SyncSettingsFromControls();
            RegenerateCodeAndMetrics();
        }

        private void SyncSettingsFromControls()
        {
            if (FormatComboBox.SelectedItem is FontExportFormat format)
            {
                CurrentSettings.Format = format;
            }

            CurrentSettings.FontName = FontNameTextBox.Text;
            CurrentSettings.IncludeGlyphPreview = IncludeGlyphPreviewCheckBox.IsChecked ?? true;
            CurrentSettings.IncludeMetricComments = IncludeMetricCommentsCheckBox.IsChecked ?? true;
            CurrentSettings.IncludeUsageComment = IncludeUsageCommentCheckBox.IsChecked ?? true;
            CurrentSettings.UppercaseHex = RbHexUpper.IsChecked ?? true;

            // Persist settings back to document
            _doc.ExportSettings = CurrentSettings.Clone();
            _doc.FontName = FontCodeGeneratorService.SanitiseFontName(CurrentSettings.FontName);
        }

        private void RegenerateCodeAndMetrics()
        {
            string sanitisedName = FontCodeGeneratorService.SanitiseFontName(CurrentSettings.FontName);

            // 1. Generate code
            try
            {
                string code = _codeGenService.GenerateCode(_doc, CurrentSettings);
                CodeTextBox.Text = code;

                int lineCount = string.IsNullOrEmpty(code) ? 0 : code.AsSpan().Count('\n') + 1;
                int charCount = code.Length;
                double kb = charCount / 1024.0;
                TxtCodeStats.Text = string.Create(CultureInfo.InvariantCulture, $"{lineCount:N0} lines  |  {charCount:N0} chars  |  {kb:F1} KB");
                TxtStatusMessage.Text = "Code generated successfully.";
            }
            catch (Exception ex)
            {
                CodeTextBox.Text = $"// Error generating font code:\n// {ex.Message}";
                TxtStatusMessage.Text = $"Generation error: {ex.Message}";
            }

            // 2. Update File Name & Badge
            var (_, defaultExt, defaultFileName) = GetFileFilter(CurrentSettings.Format, sanitisedName);
            TxtOutputFileName.Text = defaultFileName;
            TxtFormatBadge.Text = GetFormatBadgeText(CurrentSettings.Format);
            TitleTextBlock.Text = $"EXPORT FONT — {sanitisedName} [{CurrentSettings.Format}]";

            // 3. Update Flash & Metrics
            int flashBytes = _doc.EstimateFlashBytes(CurrentSettings.Format);
            FlashBytesRun.Text = flashBytes.ToString("N0", CultureInfo.InvariantCulture);

            int glyphCount = _doc.Glyphs?.Count ?? 0;
            GlyphCountRun.Text = glyphCount.ToString(CultureInfo.InvariantCulture);
            CellDimensionsRun.Text = $"{_doc.MaxCellWidth}×{_doc.CellHeight} px";
            CharRangeRun.Text = string.Create(CultureInfo.InvariantCulture, $"0x{_doc.FirstChar:X2} - 0x{_doc.LastChar:X2}");
            BaselineRun.Text = $"{_doc.Baseline} / {_doc.YAdvance} px";

            TxtFormatNote.Text = GetFormatNoteText(CurrentSettings.Format);

            // 4. Update Integration Snippet
            SnippetTextBox.Text = GetIntegrationSnippet(CurrentSettings.Format, sanitisedName);

            // 5. Re-run search if active
            if (FindBar.Visibility == Visibility.Visible && !string.IsNullOrEmpty(TxtSearch.Text))
            {
                PerformSearch(select: false);
            }
        }

        private static string GetFormatBadgeText(FontExportFormat format) => format switch
        {
            FontExportFormat.AdafruitGfx => "C/C++ Header (.h)",
            FontExportFormat.U8g2Bdf => "BDF Glyph File (.bdf)",
            FontExportFormat.Lvgl => "LVGL Font (.h / .c)",
            FontExportFormat.RawCArray => "C Header Buffer (.h)",
            FontExportFormat.FlipperZero => "Flipper Canvas Font (.h)",
            _ => "Source Code"
        };

        private static string GetFormatNoteText(FontExportFormat format) => format switch
        {
            FontExportFormat.AdafruitGfx => "Packed 1-bit bitmaps + GFXglyph descriptors table + GFXfont header struct.",
            FontExportFormat.U8g2Bdf => "Adobe Glyph Bitmap Distribution Format (BDF 2.1) ready for bdfconv compiler.",
            FontExportFormat.Lvgl => "LVGL lv_font_t descriptor with glyph_dsc array, unicode cmap, and font struct.",
            FontExportFormat.RawCArray => "Linear array of glyph bitmaps with character boundary table and metric comments.",
            FontExportFormat.FlipperZero => "1-bit column-major packed font bitmaps with byte-per-column encoding for Furi Canvas.",
            _ => "Embedded font definition."
        };

        private static string GetIntegrationSnippet(FontExportFormat format, string fontName) => format switch
        {
            FontExportFormat.AdafruitGfx =>
                $"#include \"{fontName}.h\"\n\n// Set font on your Adafruit GFX display:\ndisplay.setFont(&{fontName});",
            FontExportFormat.U8g2Bdf =>
                $"// Convert BDF to u8g2 C array using bdfconv:\n// bdfconv -v -f 1 -m '32-126' {fontName}.bdf -o {fontName}.c -n {fontName}\n\nu8g2.setFont({fontName});",
            FontExportFormat.Lvgl =>
                $"// Declare font in header or source:\nLV_FONT_DECLARE({fontName});\n\n// Apply font to an LVGL label:\nlv_obj_set_style_text_font(label, &{fontName}, 0);",
            FontExportFormat.RawCArray =>
                $"#include \"{fontName}.h\"\n\n// Access glyph bitmap directly:\nconst uint8_t* glyph = {fontName}_glyphs[char_code - {fontName}_first_char];",
            FontExportFormat.FlipperZero =>
                $"#include \"{fontName}.h\"\n\n// Set custom font on Flipper Canvas:\ncanvas_set_font_custom(canvas, &{fontName});",
            _ => string.Empty
        };

        private static (string filter, string defaultExt, string fileName) GetFileFilter(FontExportFormat format, string fontName) => format switch
        {
            FontExportFormat.U8g2Bdf => ("BDF Font (*.bdf)|*.bdf|All Files (*.*)|*.*", ".bdf", $"{fontName}.bdf"),
            FontExportFormat.Lvgl => ("C Source (*.c)|*.c|C/C++ Header (*.h)|*.h|All Files (*.*)|*.*", ".c", $"{fontName}.c"),
            FontExportFormat.FlipperZero => ("C/C++ Header (*.h)|*.h|C Source (*.c)|*.c|All Files (*.*)|*.*", ".h", $"{fontName}.h"),
            _ => ("C/C++ Header (*.h)|*.h|All Files (*.*)|*.*", ".h", $"{fontName}.h")
        };

        // ── Search / Find in Code ────────────────────────────────────────────

        private void ToggleFind_Click(object sender, RoutedEventArgs e)
        {
            if (FindBar.Visibility == Visibility.Visible)
            {
                HideFindBar();
            }
            else
            {
                ShowFindBar();
            }
        }

        private void ShowFindBar()
        {
            FindBar.Visibility = Visibility.Visible;
            if (CodeTextBox.SelectionLength > 0 && CodeTextBox.SelectionLength < 100)
            {
                TxtSearch.Text = CodeTextBox.SelectedText;
            }
            TxtSearch.Focus();
            TxtSearch.SelectAll();
            PerformSearch(select: true);
        }

        private void HideFindBar()
        {
            FindBar.Visibility = Visibility.Collapsed;
            _matchIndices.Clear();
            _currentMatchIndex = -1;
            TxtMatchCount.Text = string.Empty;
            CodeTextBox.Focus();
        }

        private void CloseFind_Click(object sender, RoutedEventArgs e) => HideFindBar();

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            PerformSearch(select: true);
        }

        private void PerformSearch(bool select)
        {
            _matchIndices.Clear();
            _currentMatchIndex = -1;

            string query = TxtSearch.Text;
            if (string.IsNullOrEmpty(query))
            {
                TxtMatchCount.Text = string.Empty;
                return;
            }

            string text = CodeTextBox.Text;
            int index = 0;
            while ((index = text.IndexOf(query, index, StringComparison.OrdinalIgnoreCase)) != -1)
            {
                _matchIndices.Add(index);
                index += query.Length;
            }

            if (_matchIndices.Count == 0)
            {
                TxtMatchCount.Text = "No matches";
                return;
            }

            _currentMatchIndex = 0;
            TxtMatchCount.Text = string.Create(CultureInfo.InvariantCulture, $"1 of {_matchIndices.Count}");

            if (select)
            {
                HighlightCurrentMatch();
            }
        }

        private void FindNext_Click(object sender, RoutedEventArgs e) => FindMatch(forward: true);

        private void FindPrev_Click(object sender, RoutedEventArgs e) => FindMatch(forward: false);

        private void FindMatch(bool forward)
        {
            if (_matchIndices.Count == 0)
            {
                PerformSearch(select: true);
                return;
            }

            if (forward)
            {
                _currentMatchIndex = (_currentMatchIndex + 1) % _matchIndices.Count;
            }
            else
            {
                _currentMatchIndex = (_currentMatchIndex - 1 + _matchIndices.Count) % _matchIndices.Count;
            }

            HighlightCurrentMatch();
        }

        private void HighlightCurrentMatch()
        {
            if (_currentMatchIndex < 0 || _currentMatchIndex >= _matchIndices.Count) return;

            int start = _matchIndices[_currentMatchIndex];
            int length = TxtSearch.Text.Length;

            CodeTextBox.Focus();
            CodeTextBox.Select(start, length);

            int lineIndex = CodeTextBox.GetLineIndexFromCharacterIndex(start);
            CodeTextBox.ScrollToLine(lineIndex);

            TxtMatchCount.Text = string.Create(CultureInfo.InvariantCulture, $"{_currentMatchIndex + 1} of {_matchIndices.Count}");
        }

        private void WordWrap_Changed(object sender, RoutedEventArgs e)
        {
            CodeTextBox.TextWrapping = ChkWordWrap.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap;
        }

        private void CodeTextBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            int caret = CodeTextBox.CaretIndex;
            string text = CodeTextBox.Text;
            int line = 1;
            int col = 1;
            for (int i = 0; i < caret && i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    col = 1;
                }
                else
                {
                    col++;
                }
            }

            int selLen = CodeTextBox.SelectionLength;
            TxtCursorPos.Text = selLen > 0
                ? string.Create(CultureInfo.InvariantCulture, $"Ln {line}, Col {col} ({selLen} sel)")
                : string.Create(CultureInfo.InvariantCulture, $"Ln {line}, Col {col}");
        }

        private const double DefaultCodeFontSize = 12.0;
        private const double MinCodeFontSize = 9.0;
        private const double MaxCodeFontSize = 24.0;

        private void CodeViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                double delta = e.Delta > 0 ? 1.0 : -1.0;
                CodeTextBox.FontSize = Math.Clamp(CodeTextBox.FontSize + delta, MinCodeFontSize, MaxCodeFontSize);
                e.Handled = true;
                return;
            }

            int lines = SystemParameters.WheelScrollLines;
            if (lines <= 0) lines = 3;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                if (e.Delta > 0)
                {
                    for (int i = 0; i < lines; i++) CodeTextBox.LineLeft();
                }
                else
                {
                    for (int i = 0; i < lines; i++) CodeTextBox.LineRight();
                }
                e.Handled = true;
                return;
            }

            if (e.Delta > 0)
            {
                for (int i = 0; i < lines; i++) CodeTextBox.LineUp();
            }
            else
            {
                for (int i = 0; i < lines; i++) CodeTextBox.LineDown();
            }
            e.Handled = true;
        }

        // ── Primary Actions: Copy & Save ─────────────────────────────────────

        private void CopyCode_Click(object sender, RoutedEventArgs e)
        {
            string code = CodeTextBox.Text;
            if (string.IsNullOrEmpty(code)) return;

            try
            {
                Clipboard.SetDataObject(code, true);
                ShowCopyCodeFeedback();
                TxtStatusMessage.Text = "Code copied to clipboard.";
            }
            catch (Exception ex)
            {
                TxtStatusMessage.Text = $"Failed to copy to clipboard: {ex.Message}";
            }
        }

        private void ShowCopyCodeFeedback()
        {
            TxtCopyIcon.Text = "✓";
            TxtCopyLabel.Text = "Copied!";
            _copyFeedbackTimer.Stop();
            _copyFeedbackTimer.Start();
        }

        private void ResetCopyCodeButton()
        {
            TxtCopyIcon.Text = "📋";
            TxtCopyLabel.Text = "Copy Code";
        }

        private void CopySnippet_Click(object sender, RoutedEventArgs e)
        {
            string snippet = SnippetTextBox.Text;
            if (string.IsNullOrEmpty(snippet)) return;

            try
            {
                Clipboard.SetDataObject(snippet, true);
                ShowCopySnippetFeedback();
                TxtStatusMessage.Text = "Snippet copied to clipboard.";
            }
            catch (Exception ex)
            {
                TxtStatusMessage.Text = $"Failed to copy snippet: {ex.Message}";
            }
        }

        private void ShowCopySnippetFeedback()
        {
            TxtCopySnippetIcon.Text = "✓";
            TxtCopySnippetLabel.Text = "Copied!";
            _snippetFeedbackTimer.Stop();
            _snippetFeedbackTimer.Start();
        }

        private void ResetCopySnippetButton()
        {
            TxtCopySnippetIcon.Text = "📋";
            TxtCopySnippetLabel.Text = "Copy Snippet";
        }

        private void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            string code = CodeTextBox.Text;
            if (string.IsNullOrEmpty(code)) return;

            string sanitisedName = FontCodeGeneratorService.SanitiseFontName(CurrentSettings.FontName);
            var (filter, defaultExt, defaultFileName) = GetFileFilter(CurrentSettings.Format, sanitisedName);

            var sfd = new SaveFileDialog
            {
                Filter = filter,
                DefaultExt = defaultExt,
                FileName = defaultFileName,
                Title = "Save Font Code",
            };

            if (!string.IsNullOrWhiteSpace(InitialDirectory) && Directory.Exists(InitialDirectory))
            {
                sfd.InitialDirectory = InitialDirectory;
            }

            if (sfd.ShowDialog(this) == true)
            {
                try
                {
                    SafeFileIo.WriteAllTextAtomic(sfd.FileName, code, maxRetries: 5, createBackup: false);
                    string fileName = Path.GetFileName(sfd.FileName);
                    TxtStatusMessage.Text = $"Successfully saved to {fileName}";
                }
                catch (Exception ex)
                {
                    TxtStatusMessage.Text = $"Save failed: {ex.Message}";
                    MessageDialog.Show($"Failed to save file:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        // ── Window Chrome & Navigation ───────────────────────────────────────

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                CaptionMaximize_Click(sender, e);
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CaptionMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CaptionMaximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CaptionClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (FindBar.Visibility == Visibility.Visible)
                {
                    HideFindBar();
                    e.Handled = true;
                    return;
                }

                Close();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                ShowFindBar();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                SaveAs_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (CodeTextBox.SelectionLength == 0 && !TxtSearch.IsFocused && !FontNameTextBox.IsFocused)
                {
                    CopyCode_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.F3)
            {
                bool forward = (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift;
                FindMatch(forward);
                e.Handled = true;
                return;
            }

            if ((e.Key == Key.D0 || e.Key == Key.NumPad0) && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                CodeTextBox.FontSize = DefaultCodeFontSize;
                e.Handled = true;
            }
        }
    }
}
