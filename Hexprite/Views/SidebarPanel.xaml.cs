using Hexprite.Core;
using Hexprite.ViewModels;
using Microsoft.Extensions.Configuration;
using System;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Globalization;

namespace Hexprite.Views
{
    public partial class SidebarPanel : UserControl, IDisposable
    {
        // ── Syntax colours (picked up from theme resources at runtime) ──────
        private SolidColorBrush _keywordBrush = new(Color.FromRgb(0x56, 0x9C, 0xD6));
        private SolidColorBrush _literalBrush = new(Color.FromRgb(0xB5, 0xCE, 0xA8));
        private SolidColorBrush _commentBrush = new(Color.FromRgb(0x6A, 0x99, 0x55));
        private SolidColorBrush _identifierBrush = new(Color.FromRgb(0x9C, 0xDC, 0xFE));
        private SolidColorBrush _defaultBrush = new(Color.FromRgb(0xD4, 0xD4, 0xD4));

        // Compiled once via source generator — avoids re-parsing the regex pattern on every export update
        [GeneratedRegex(
            @"((?<!\\)""(?>(?:[^""\\]+|\\.)*)""|(?<!\\)'(?>(?:[^'\\]+|\\.)*)')" + // Group 1: string or char literal
            @"|(0[xX][0-9a-fA-F]+|B[01]{5,8}\b|\d+)" +                 // Group 2: hex OR binary OR decimal literal
            @"|(const|uint8_t|byte|PROGMEM|U8X8_PROGMEM|bytearray|framebuf|display|u8g2|include|define|pragma|ifdef|ifndef|endif|else|elif|undef)" + // Group 3: keywords
            @"|([a-zA-Z_][a-zA-Z0-9_]*)" +               // Group 4: identifiers
            @"|([^a-zA-Z0-9_""'#]+|[^a-zA-Z0-9_])",       // Group 5: punctuation / whitespace
            RegexOptions.None,
            matchTimeoutMilliseconds: 1000)]
        internal static partial Regex TokenRegex { get; }

        private const int HighlightDebounceMs = 180;
        private const int DefaultSyntaxHighlightMaxChars = 20000;
        private const int MinSyntaxHighlightMaxChars = 2000;
        private const int MaxSyntaxHighlightMaxChars = 500000;

        private MainViewModel? _vm;
        private readonly DispatcherTimer _syntaxDebounceTimer;
        private readonly DispatcherTimer _copyFeedbackTimer;
        private string _pendingCode = string.Empty;
        private System.Threading.CancellationTokenSource? _syntaxCts;
        private readonly Lock _syntaxCtsLock = new();
        private readonly int _syntaxHighlightMaxChars = LoadSyntaxHighlightMaxChars();

        public SidebarPanel()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += SidebarPanel_Loaded;
            Unloaded += SidebarPanel_Unloaded;

            _syntaxDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(HighlightDebounceMs),
            };
            _syntaxDebounceTimer.Tick += (_, _) =>
            {
                _syntaxDebounceTimer.Stop();
                PerformSyntaxOutputUpdate(_pendingCode);
            };

            _copyFeedbackTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1500),
            };
            _copyFeedbackTimer.Tick += (_, _) =>
            {
                _copyFeedbackTimer.Stop();
                ResetCopyButtonVisual();
            };
        }

        private ShellViewModel? _shell;

        private void SidebarPanel_Loaded(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow?.DataContext is ShellViewModel shell)
            {
                _shell = shell;
                _shell.ThemeChanged += Shell_ThemeChanged;
            }
        }

        public void Dispose()
        {
            _syntaxDebounceTimer.Stop();
            _copyFeedbackTimer.Stop();

            if (_shell != null)
            {
                _shell.ThemeChanged -= Shell_ThemeChanged;
                _shell = null;
            }

            lock (_syntaxCtsLock)
            {
                _syntaxCts?.Cancel();
                _syntaxCts?.Dispose();
                _syntaxCts = null;
            }

            GC.SuppressFinalize(this);
        }

        private void SidebarPanel_Unloaded(object sender, RoutedEventArgs e)
        {
            Dispose();
        }

        private void Shell_ThemeChanged(object? sender, EventArgs e)
        {
            if (_vm != null)
            {
                UpdateSyntaxOutput(_vm.ExportedCode);
            }
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null)
                _vm.PropertyChanged -= Vm_PropertyChanged;

            _vm = e.NewValue as MainViewModel;

            if (_vm != null)
            {
                _vm.PropertyChanged += Vm_PropertyChanged;
                SyncChipsFromViewModel();
                UpdateSyntaxOutput(_vm.ExportedCode);
            }
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.ExportedCode))
            {
                ScheduleSyntaxOutputUpdate(_vm?.ExportedCode ?? string.Empty);
            }
            else if (e.PropertyName == nameof(MainViewModel.ExportFormat))
            {
                Dispatcher.BeginInvoke(SyncFormatCombo);
            }
            else if (e.PropertyName == nameof(MainViewModel.BytesPerLine))
            {
                Dispatcher.BeginInvoke(SyncBplChip);
            }
            else if (e.PropertyName == nameof(MainViewModel.UppercaseHex))
            {
                Dispatcher.BeginInvoke(SyncHexCaseChip);
            }
            else if (e.PropertyName == nameof(MainViewModel.Compression) ||
                     e.PropertyName == nameof(MainViewModel.IsCompressionVisible))
            {
                Dispatcher.BeginInvoke(SyncCompressionCombo);
            }
        }

        // ── Format ComboBox ──────────────────────────────────────────────────

        private bool _suppressFormatChange;

        private void CboFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressFormatChange || _vm == null) return;
            if (CboFormat.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                if (Enum.TryParse<ExportFormat>(tag, out var fmt))
                    _vm.ExportFormat = fmt;
            }
        }

        private void SyncFormatCombo()
        {
            if (_vm == null) return;
            _suppressFormatChange = true;
            string target = _vm.ExportFormat.ToString();
            foreach (ComboBoxItem item in CboFormat.Items)
            {
                if (item.Tag is string t && t == target)
                {
                    CboFormat.SelectedItem = item;
                    break;
                }
            }
            _suppressFormatChange = false;
        }

        // ── Bytes-per-line chips ─────────────────────────────────────────────

        private bool _suppressBplChange;

        private void BplChip_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressBplChange || _vm == null) return;
            if (sender is RadioButton rb && rb.Tag is string t && int.TryParse(t, out int v))
                _vm.BytesPerLine = v;
        }

        private void SyncBplChip()
        {
            if (_vm == null) return;
            _suppressBplChange = true;
            string tag = _vm.BytesPerLine.ToString(CultureInfo.InvariantCulture);
            foreach (var rb in new[] { RbBpl0, RbBpl4, RbBpl8, RbBpl16 })
            {
                if (rb.Tag is string t && t == tag)
                { rb.IsChecked = true; break; }
            }
            _suppressBplChange = false;
        }

        // ── Hex-case chips ───────────────────────────────────────────────────

        private bool _suppressHexCaseChange;

        private void HexCase_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressHexCaseChange || _vm == null) return;
            _vm.UppercaseHex = (sender is RadioButton rb && rb.Tag is string t && t == "upper");
        }

        private void SyncHexCaseChip()
        {
            if (_vm == null) return;
            _suppressHexCaseChange = true;
            RbHexUpper.IsChecked = _vm.UppercaseHex;
            RbHexLower.IsChecked = !_vm.UppercaseHex;
            _suppressHexCaseChange = false;
        }

        // ── Master sync ──────────────────────────────────────────────────────

        private void SyncChipsFromViewModel()
        {
            if (_vm == null) return;
            SyncFormatCombo();
            SyncBplChip();
            SyncHexCaseChip();
            SyncCompressionCombo();
        }

        // ── Compression ComboBox ─────────────────────────────────────────────

        private bool _suppressCompressionChange;

        private void CboCompression_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressCompressionChange || _vm == null) return;
            if (CboCompression.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                if (Enum.TryParse<CompressionMode>(tag, out var mode))
                    _vm.Compression = mode;
            }
        }

        private void SyncCompressionCombo()
        {
            if (_vm == null) return;
            _suppressCompressionChange = true;
            string target = _vm.Compression.ToString();
            foreach (ComboBoxItem item in CboCompression.Items)
            {
                if (item.Tag is string t && t == target)
                {
                    CboCompression.SelectedItem = item;
                    break;
                }
            }
            _suppressCompressionChange = false;
        }

        // ── Syntax highlighting ──────────────────────────────────────────────

        private void UpdateSyntaxOutput(string code)
            => ScheduleSyntaxOutputUpdate(code);

        private void ScheduleSyntaxOutputUpdate(string code)
        {
            _pendingCode = code;
            _syntaxDebounceTimer.Stop();
            _syntaxDebounceTimer.Start();
        }

        private void PerformSyntaxOutputUpdate(string code)
        {
            // Resolve brushes from theme resources (so they respect theme switches)
            TryLoadBrushes();

            System.Threading.CancellationToken token;
            lock (_syntaxCtsLock)
            {
                _syntaxCts?.Cancel();
                _syntaxCts?.Dispose();
                _syntaxCts = new System.Threading.CancellationTokenSource();
                token = _syntaxCts.Token;
            }

            if (string.IsNullOrEmpty(code))
            {
                CodeOutputBox.UpdateCode("(no output)", [new Rendering.TokenSpan(0, 11, Rendering.TokenType.Comment)],
                    _defaultBrush, _keywordBrush, _literalBrush, _commentBrush, _identifierBrush);
                return;
            }

            // Large output fallback: skip syntax tokenization and truncate preview in UI text box to keep UI responsive.
            if (code.Length > _syntaxHighlightMaxChars)
            {
                string preview = string.Concat(code.AsSpan(0, _syntaxHighlightMaxChars), string.Create(CultureInfo.InvariantCulture, $"\n\n// ... Output truncated for UI preview ({code.Length:N0} total chars). Full code is retained for clipboard export."));
                CodeOutputBox.UpdateCode(preview, [new Rendering.TokenSpan(0, 11, Rendering.TokenType.Comment)],
                    _defaultBrush, _keywordBrush, _literalBrush, _commentBrush, _identifierBrush);
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    var spans = TokenizeCode(code, Rendering.SyntaxHighlightBox.MaxFormattedSpans, token);
                    if (token.IsCancellationRequested) return;

                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        CodeOutputBox.UpdateCode(code, spans, _defaultBrush, _keywordBrush, _literalBrush, _commentBrush, _identifierBrush);
                    }));
                }
                catch (OperationCanceledException)
                {
                    // Task cancelled gracefully
                }
            }, token);
        }

        internal static List<Rendering.TokenSpan> TokenizeCode(
            string code,
            System.Threading.CancellationToken cancellationToken = default)
            => TokenizeCode(code, int.MaxValue, cancellationToken);

        internal static List<Rendering.TokenSpan> TokenizeCode(
            string code, 
            int maxSpans,
            System.Threading.CancellationToken cancellationToken = default)
        {
            var spans = new List<Rendering.TokenSpan>();
            if (string.IsNullOrEmpty(code)) return spans;

            int absolutePos = 0;

            while (absolutePos < code.Length && spans.Count < maxSpans)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int endLine = code.IndexOf('\n', absolutePos);
                int lineLen = (endLine == -1) ? code.Length - absolutePos : endLine - absolutePos;
                string line = code.Substring(absolutePos, lineLen);

                if (IsFullLineComment(line))
                {
                    spans.Add(new Rendering.TokenSpan(absolutePos, line.Length, Rendering.TokenType.Comment));
                }
                else
                {
                    int commentIdx = FindInlineComment(line);
                    string codePart = commentIdx >= 0 ? line[..commentIdx] : line;
                    string commentPart = commentIdx >= 0 ? line[commentIdx..] : string.Empty;

                    foreach (Match m in TokenRegex.Matches(codePart))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        Rendering.TokenType type = Rendering.TokenType.Default;
                        if (m.Groups[1].Success)
                            type = Rendering.TokenType.Literal;
                        else if (m.Groups[2].Success)
                            type = Rendering.TokenType.Literal;
                        else if (m.Groups[3].Success)
                            type = Rendering.TokenType.Keyword;
                        else if (m.Groups[4].Success)
                            type = Rendering.TokenType.Identifier;

                        if (type != Rendering.TokenType.Default)
                        {
                            int tokenStart = absolutePos + m.Index;
                            int tokenLen = m.Length;

                            // Merge consecutive literals on the same line separated by array punctuation (e.g. hex byte arrays "0x00, 0x01")
                            if (type == Rendering.TokenType.Literal && spans.Count > 0)
                            {
                                var prevSpan = spans[^1];
                                if (prevSpan.Type == Rendering.TokenType.Literal && prevSpan.Start >= absolutePos)
                                {
                                    int gapStart = prevSpan.Start + prevSpan.Length;
                                    int gapLen = tokenStart - gapStart;
                                    if (gapLen > 0 && IsArraySeparator(code, gapStart, gapLen))
                                    {
                                        spans[^1] = new Rendering.TokenSpan(prevSpan.Start, (tokenStart + tokenLen) - prevSpan.Start, Rendering.TokenType.Literal);
                                        continue;
                                    }
                                }
                            }

                            spans.Add(new Rendering.TokenSpan(tokenStart, tokenLen, type));
                            if (spans.Count >= maxSpans)
                                break;
                        }
                    }

                    if (commentIdx >= 0 && spans.Count < maxSpans)
                    {
                        spans.Add(new Rendering.TokenSpan(absolutePos + commentIdx, commentPart.Length, Rendering.TokenType.Comment));
                    }
                }

                absolutePos += lineLen;
                if (endLine != -1) absolutePos += 1; // skip \n
            }

            return spans;
        }

        internal static bool IsArraySeparator(string text, int start, int length)
        {
            int end = start + length;
            for (int i = start; i < end; i++)
            {
                char c = text[i];
                if (c != ',' && c != ';' && c != ' ' && c != '\t' && c != '\r' && c != '\n')
                    return false;
            }
            return true;
        }

        internal static bool IsPreprocessorKeyword(string kw)
        {
            return kw switch
            {
                "include" or "define" or "pragma" or "ifdef" or "ifndef" or
                "endif" or "else" or "elif" or "undef" or "if" or "error" or "line" => true,
                _ => false,
            };
        }

        internal static bool IsFullLineComment(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;

            int i = 0;
            while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
            if (i >= line.Length) return false;

            if (line[i] == '/' && i + 1 < line.Length && line[i + 1] == '/')
                return true;

            if (line[i] == '#')
            {
                int p = i + 1;
                while (p < line.Length && char.IsWhiteSpace(line[p])) p++;
                int kwStart = p;
                while (p < line.Length && char.IsLetter(line[p])) p++;
                if (p > kwStart)
                {
                    string kw = line[kwStart..p];
                    if (IsPreprocessorKeyword(kw))
                        return false;
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns the index of the first '//' or '#' that isn't inside a string literal or preprocessor directive.
        /// Returns -1 if there is no inline comment.
        /// </summary>
        internal static int FindInlineComment(string line)
        {
            if (string.IsNullOrEmpty(line)) return -1;

            int scanStart = 0;
            int firstNonWs = 0;
            while (firstNonWs < line.Length && char.IsWhiteSpace(line[firstNonWs]))
            {
                firstNonWs++;
            }

            if (firstNonWs < line.Length && line[firstNonWs] == '#')
            {
                int p = firstNonWs + 1;
                while (p < line.Length && char.IsWhiteSpace(line[p]))
                {
                    p++;
                }
                int kwStart = p;
                while (p < line.Length && char.IsLetter(line[p]))
                {
                    p++;
                }
                if (p > kwStart)
                {
                    string kw = line[kwStart..p];
                    if (IsPreprocessorKeyword(kw))
                    {
                        scanStart = p;
                    }
                }
            }

            bool inDoubleQuote = false;
            bool inSingleQuote = false;
            bool escaped = false;

            for (int i = scanStart; i < line.Length; i++)
            {
                char c = line[i];

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\' && (inDoubleQuote || inSingleQuote))
                {
                    escaped = true;
                    continue;
                }

                if (c == '"' && !inSingleQuote)
                {
                    inDoubleQuote = !inDoubleQuote;
                    continue;
                }

                if (c == '\'' && !inDoubleQuote)
                {
                    inSingleQuote = !inSingleQuote;
                    continue;
                }

                if (!inDoubleQuote && !inSingleQuote)
                {
                    if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
                    {
                        return i;
                    }
                    if (c == '#')
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private void TryLoadBrushes()
        {
            try
            {
                _keywordBrush = (SolidColorBrush)FindResource("Brush.Code.Keyword");
                _literalBrush = (SolidColorBrush)FindResource("Brush.Code.Literal");
                _commentBrush = (SolidColorBrush)FindResource("Brush.Code.Comment");
                _identifierBrush = (SolidColorBrush)FindResource("Brush.Code.Identifier");
                _defaultBrush = (SolidColorBrush)FindResource("Brush.Code.Punctuation");
            }
            catch { /* fallback colours already set in field initialisers */ }
        }

        private static int LoadSyntaxHighlightMaxChars()
        {
            try
            {
                string configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                IConfigurationRoot config = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile(configPath, optional: true, reloadOnChange: false)
                    .AddEnvironmentVariables(prefix: "HEXEL_")
                    .Build();

                string? configured = config["Editor:SyntaxHighlightMaxChars"];
                int value = int.TryParse(configured, out int parsed)
                    ? parsed
                    : DefaultSyntaxHighlightMaxChars;

                return Math.Clamp(value, MinSyntaxHighlightMaxChars, MaxSyntaxHighlightMaxChars);
            }
            catch
            {
                return DefaultSyntaxHighlightMaxChars;
            }
        }

        // ── Import button ────────────────────────────────────────────────────

        private void ImportFromCode_Click(object sender, RoutedEventArgs e)
        {
            // Reach up to the ShellViewModel (MainWindow's DataContext) and invoke its command
            if (Application.Current.MainWindow?.DataContext is ShellViewModel shell &&
                shell.ImportFromCodeMenuCommand.CanExecute(parameter: null))
            {
                shell.ImportFromCodeMenuCommand.Execute(parameter: null);
            }
        }

        private void ImportFromFile_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow?.DataContext is ShellViewModel shell &&
                shell.ImportFromFileMenuCommand.CanExecute(parameter: null))
            {
                shell.ImportFromFileMenuCommand.Execute(parameter: null);
            }
        }

        private void ImportBitmap_Click(object sender, RoutedEventArgs e)
        {
            // Reach up to the ShellViewModel (MainWindow's DataContext) and invoke its command
            if (Application.Current.MainWindow?.DataContext is ShellViewModel shell &&
                shell.ImportBitmapMenuCommand.CanExecute(parameter: null))
            {
                shell.ImportBitmapMenuCommand.Execute(parameter: null);
            }
        }

        private void ImportFlipper_Click(object sender, RoutedEventArgs e)
        {
            // Reach up to the ShellViewModel (MainWindow's DataContext) and invoke its command
            if (Application.Current.MainWindow?.DataContext is ShellViewModel shell &&
                shell.ImportFlipperMenuCommand.CanExecute(parameter: null))
            {
                shell.ImportFlipperMenuCommand.Execute(parameter: null);
            }
        }

        private void OpenDisplaySimulation_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            DisplaySimulationWindow.ShowOrActivate(vm, Window.GetWindow(this));
        }

        private CodeOutputWindow? _codeWindow;

        private void PopOutCode_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;

            if (_codeWindow != null && _codeWindow.IsLoaded)
            {
                _codeWindow.Activate();
                return;
            }

            _codeWindow = new CodeOutputWindow(vm)
            {
                Owner = Window.GetWindow(this),
            };
            _codeWindow.Show();
        }

        // ── Code Box Resize Handle ──────────────────────────────────────────
        private bool _isResizingCodeBox;
        private double _resizeStartY;
        private double _resizeStartHeight;

        private void CodeResizeHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && sender is UIElement el)
            {
                _isResizingCodeBox = true;
                _resizeStartY = PointToScreen(e.GetPosition(this)).Y;
                _resizeStartHeight = CodeOutputBorder.ActualHeight;
                el.CaptureMouse();
                e.Handled = true;
            }
        }

        private void CodeResizeHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isResizingCodeBox) return;

            double currentScreenY = PointToScreen(e.GetPosition(this)).Y;
            double delta = currentScreenY - _resizeStartY;
            double newHeight = Math.Clamp(_resizeStartHeight + delta, CodeOutputBorder.MinHeight, CodeOutputBorder.MaxHeight);
            CodeOutputBorder.Height = newHeight;
        }

        private void CodeResizeHandle_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isResizingCodeBox && sender is UIElement el)
            {
                _isResizingCodeBox = false;
                el.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        // ── Copy Button Visual Feedback ─────────────────────────────────────
        private void BtnCopyCode_Click(object sender, RoutedEventArgs e)
        {
            ShowCopyFeedback();
        }

        private void ShowCopyFeedback()
        {
            IconCopyCode.Visibility = Visibility.Collapsed;
            IconCopiedCheck.Visibility = Visibility.Visible;
            TxtCopyCode.Text = "Copied!";
            _copyFeedbackTimer.Stop();
            _copyFeedbackTimer.Start();
        }

        private void ResetCopyButtonVisual()
        {
            IconCopyCode.Visibility = Visibility.Visible;
            IconCopiedCheck.Visibility = Visibility.Collapsed;
            TxtCopyCode.Text = "Copy";
        }

    }
}
