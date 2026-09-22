using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Hexprite.Core;
using Hexprite.ViewModels;
using Microsoft.Win32;

namespace Hexprite.Views
{
    public partial class CodeOutputWindow : Window, IDisposable
    {
        private readonly MainViewModel _vm;
        private readonly DispatcherTimer _syntaxDebounceTimer;
        private readonly DispatcherTimer _copyFeedbackTimer;
        private readonly DispatcherTimer _searchDebounceTimer;
        private string _pendingCode = string.Empty;
        private CancellationTokenSource? _syntaxCts;
        private readonly Lock _syntaxCtsLock = new();

        private double _currentFontSize = 12.0;
        private const double DefaultFontSize = 12.0;
        private const double MinFontSize = 9.0;
        private const double MaxFontSize = 24.0;

        // ── Syntax colours ───────────────────────────────────────────────────
        private SolidColorBrush _keywordBrush = new(Color.FromRgb(0x56, 0x9C, 0xD6));
        private SolidColorBrush _literalBrush = new(Color.FromRgb(0xB5, 0xCE, 0xA8));
        private SolidColorBrush _commentBrush = new(Color.FromRgb(0x6A, 0x99, 0x55));
        private SolidColorBrush _identifierBrush = new(Color.FromRgb(0x9C, 0xDC, 0xFE));
        private SolidColorBrush _defaultBrush = new(Color.FromRgb(0xD4, 0xD4, 0xD4));

        // ── Search State ─────────────────────────────────────────────────────
        private readonly List<int> _matchIndices = [];
        private int _currentMatchIndex = -1;

        public CodeOutputWindow(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

            _syntaxDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(120),
            };
            _syntaxDebounceTimer.Tick += (_, _) =>
            {
                _syntaxDebounceTimer.Stop();
                PerformSyntaxOutputUpdate(_pendingCode);
            };

            _searchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(80),
            };
            _searchDebounceTimer.Tick += (_, _) =>
            {
                _searchDebounceTimer.Stop();
                PerformSearch(select: true);
            };

            _copyFeedbackTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.5),
            };
            _copyFeedbackTimer.Tick += (_, _) =>
            {
                _copyFeedbackTimer.Stop();
                ResetCopyButtonVisual();
            };

            _vm.PropertyChanged += OnVmPropertyChanged;
            Loaded += CodeOutputWindow_Loaded;
            StateChanged += CodeOutputWindow_StateChanged;

            SyncAllSettingsFromViewModel();
            _pendingCode = _vm.ExportedCode ?? string.Empty;
            UpdateWindowDetails();
        }

        private void CodeOutputWindow_Loaded(object sender, RoutedEventArgs e)
        {
            TryLoadBrushes();
            UpdateCodeContent(_vm.ExportedCode);
        }

        private void CodeOutputWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                MaximizeIcon.Text = "\uE923"; // Restore icon
            }
            else
            {
                MaximizeIcon.Text = "\uE922"; // Maximize icon
            }
        }

        // ── ViewModel change listener ────────────────────────────────────────

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.ExportedCode):
                    Dispatcher.BeginInvoke(() => UpdateCodeContent(_vm.ExportedCode));
                    break;
                case nameof(MainViewModel.ExportStats):
                case nameof(MainViewModel.IsCodeStale):
                    Dispatcher.BeginInvoke(UpdateWindowDetails);
                    break;
                case nameof(MainViewModel.ExportFormat):
                    Dispatcher.BeginInvoke(() =>
                    {
                        SyncFormatCombo();
                        UpdateExportSketchButtonVisibility();
                    });
                    break;
                case nameof(MainViewModel.IsArduinoSketchExportable):
                    Dispatcher.BeginInvoke(UpdateExportSketchButtonVisibility);
                    break;
                case nameof(MainViewModel.BytesPerLine):
                    Dispatcher.BeginInvoke(SyncBplChip);
                    break;
                case nameof(MainViewModel.UppercaseHex):
                    Dispatcher.BeginInvoke(SyncHexCaseChip);
                    break;
                case nameof(MainViewModel.Compression):
                case nameof(MainViewModel.IsCompressionVisible):
                    Dispatcher.BeginInvoke(SyncCompressionCombo);
                    break;
                case nameof(MainViewModel.SpriteName):
                    Dispatcher.BeginInvoke(UpdateTitle);
                    break;
                case nameof(MainViewModel.GenerateFullSketch):
                    Dispatcher.BeginInvoke(SyncFullSketchCheck);
                    break;
            }
        }

        // ── Code updating & rendering ────────────────────────────────────────

        private void UpdateCodeContent(string code)
        {
            _pendingCode = code ?? string.Empty;

            // Only populate selectable box if the selectable mode is active
            if (RbModeSelectable.IsChecked == true && CodeSelectableBox.Text != _pendingCode)
            {
                CodeSelectableBox.Text = _pendingCode;
            }

            // Immediately display base text in SyntaxHighlightBox (zero perceived load latency)
            TryLoadBrushes();
            CodeHighlightBox.UpdateCode(_pendingCode, [],
                _defaultBrush, _keywordBrush, _literalBrush, _commentBrush, _identifierBrush);

            _syntaxDebounceTimer.Stop();
            _syntaxDebounceTimer.Start();
            UpdateWindowDetails();

            if (FindBar.Visibility == Visibility.Visible)
            {
                PerformSearch(select: false);
            }
        }

        private void PerformSyntaxOutputUpdate(string code)
        {
            TryLoadBrushes();

            CancellationToken token;
            lock (_syntaxCtsLock)
            {
                _syntaxCts?.Cancel();
                _syntaxCts?.Dispose();
                _syntaxCts = new CancellationTokenSource();
                token = _syntaxCts.Token;
            }

            if (string.IsNullOrEmpty(code))
            {
                CodeHighlightBox.UpdateCode("(no output)",
                    [new(0, 11, Rendering.TokenType.Comment)],
                    _defaultBrush, _keywordBrush, _literalBrush, _commentBrush, _identifierBrush);
                return;
            }

            // Fallback for extremely large outputs
            if (code.Length > Rendering.SyntaxHighlightBox.MaxRenderCharLength)
            {
                string charCount = code.Length.ToString("N0", CultureInfo.InvariantCulture);
                string preview = string.Concat(
                    code[..Rendering.SyntaxHighlightBox.MaxRenderCharLength],
                    "\n\n// ... Output truncated for UI syntax rendering (",
                    charCount,
                    " total chars). Use Selectable Text mode or Copy Code to inspect entire file.");
                CodeHighlightBox.UpdateCode(preview, [],
                    _defaultBrush, _keywordBrush, _literalBrush, _commentBrush, _identifierBrush);
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    var spans = SidebarPanel.TokenizeCode(code, Rendering.SyntaxHighlightBox.MaxFormattedSpans, token);
                    if (token.IsCancellationRequested) return;

                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        CodeHighlightBox.UpdateCode(code, spans, _defaultBrush, _keywordBrush, _literalBrush, _commentBrush, _identifierBrush);
                    }));
                }
                catch (OperationCanceledException)
                {
                    // Gracefully cancelled
                }
            }, token);
        }

        private void UpdateWindowDetails()
        {
            UpdateTitle();

            // Outdated badge
            BadgeStale.Visibility = _vm.IsCodeStale ? Visibility.Visible : Visibility.Collapsed;

            // Stats
            string code = _vm.ExportedCode ?? string.Empty;
            bool isEmpty = string.IsNullOrEmpty(code);
            EmptyStateBorder.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;

            int lineCount = 0;
            if (!isEmpty)
            {
                lineCount = code.AsSpan().Count('\n') + 1;
            }

            string baseStats = !string.IsNullOrWhiteSpace(_vm.ExportStats) ? $"{_vm.ExportStats}  |  " : string.Empty;
            TxtStats.Text = string.Create(CultureInfo.InvariantCulture, $"{baseStats}{lineCount:N0} lines, {code.Length:N0} chars");

            // Sprite Info
            var state = _vm.SpriteState;
            if (state != null)
            {
                int frameCount = state.Frames.Count;
                TxtSpriteInfo.Text = string.Create(CultureInfo.InvariantCulture, $"{state.Width}×{state.Height} px ({frameCount} frame{(frameCount != 1 ? "s" : "")})");
            }
            else
            {
                TxtSpriteInfo.Text = string.Empty;
            }
        }

        private void UpdateTitle()
        {
            string spriteName = string.IsNullOrWhiteSpace(_vm.SpriteName) ? "Untitled" : _vm.SpriteName;
            string formatName = _vm.ExportFormat.ToString();
            TxtTitle.Text = $"Generated Code — {spriteName} [{formatName}]";
            Title = $"Generated Code — {spriteName} [{formatName}]";
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
            catch { /* fallback brushes retained */ }
        }

        // ── Export Settings Sync ─────────────────────────────────────────────

        private bool _suppressSettingsChange;

        private void SyncAllSettingsFromViewModel()
        {
            SyncFormatCombo();
            SyncCompressionCombo();
            SyncBplChip();
            SyncHexCaseChip();
            SyncFullSketchCheck();
            UpdateExportSketchButtonVisibility();
        }

        private void UpdateExportSketchButtonVisibility()
        {
            if (BtnExportSketch != null && _vm != null)
            {
                BtnExportSketch.Visibility = _vm.IsArduinoSketchExportable ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void SyncFullSketchCheck()
        {
            _suppressSettingsChange = true;
            ChkFullSketch.IsChecked = _vm.GenerateFullSketch;
            _suppressSettingsChange = false;
        }

        private void FullSketch_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressSettingsChange || _vm == null) return;
            _vm.GenerateFullSketch = ChkFullSketch.IsChecked == true;
        }

        private void SyncFormatCombo()
        {
            _suppressSettingsChange = true;
            string target = _vm.ExportFormat.ToString();
            foreach (ComboBoxItem item in CboFormat.Items)
            {
                if (item.Tag is string t && t == target)
                {
                    CboFormat.SelectedItem = item;
                    break;
                }
            }
            PanelCompression.Visibility = _vm.IsCompressionVisible ? Visibility.Visible : Visibility.Collapsed;
            _suppressSettingsChange = false;
            UpdateTitle();
        }

        private void CboFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSettingsChange || _vm == null) return;
            if (CboFormat.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                if (Enum.TryParse<ExportFormat>(tag, out var fmt))
                {
                    _vm.ExportFormat = fmt;
                    PanelCompression.Visibility = _vm.IsCompressionVisible ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        private void SyncCompressionCombo()
        {
            _suppressSettingsChange = true;
            string target = _vm.Compression.ToString();
            foreach (ComboBoxItem item in CboCompression.Items)
            {
                if (item.Tag is string t && t == target)
                {
                    CboCompression.SelectedItem = item;
                    break;
                }
            }
            PanelCompression.Visibility = _vm.IsCompressionVisible ? Visibility.Visible : Visibility.Collapsed;
            _suppressSettingsChange = false;
        }

        private void CboCompression_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSettingsChange || _vm == null) return;
            if (CboCompression.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                if (Enum.TryParse<CompressionMode>(tag, out var mode))
                    _vm.Compression = mode;
            }
        }

        private void SyncBplChip()
        {
            _suppressSettingsChange = true;
            string tag = _vm.BytesPerLine.ToString(CultureInfo.InvariantCulture);
            foreach (var rb in new[] { RbBpl0, RbBpl4, RbBpl8, RbBpl16 })
            {
                if (rb.Tag is string t && t == tag)
                {
                    rb.IsChecked = true;
                    break;
                }
            }
            _suppressSettingsChange = false;
        }

        private void BplChip_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressSettingsChange || _vm == null) return;
            if (sender is RadioButton rb && rb.Tag is string t && int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                _vm.BytesPerLine = v;
        }

        private void SyncHexCaseChip()
        {
            _suppressSettingsChange = true;
            RbHexUpper.IsChecked = _vm.UppercaseHex;
            RbHexLower.IsChecked = !_vm.UppercaseHex;
            _suppressSettingsChange = false;
        }

        private void HexCase_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressSettingsChange || _vm == null) return;
            _vm.UppercaseHex = (sender is RadioButton rb && rb.Tag is string t && t == "upper");
        }

        // ── View Mode & Font Size ────────────────────────────────────────────

        private ScrollViewer? _selectableScrollViewer;
        private ScrollViewer? SelectableScroll => _selectableScrollViewer ??= (FindName("SelectableScrollViewer") as ScrollViewer);

        private void ViewMode_Changed(object sender, RoutedEventArgs e)
        {
            if (SyntaxScrollViewer == null || SelectableScroll == null || CodeSelectableBox == null) return;

            if (RbModeSyntax.IsChecked == true)
            {
                double offset = SelectableScroll.VerticalOffset;
                SyntaxScrollViewer.Visibility = Visibility.Visible;
                SelectableScroll.Visibility = Visibility.Collapsed;
                TxtCursorPos.Visibility = Visibility.Collapsed;
                SyntaxScrollViewer.ScrollToVerticalOffset(offset);
            }
            else
            {
                double offset = SyntaxScrollViewer.VerticalOffset;
                SyntaxScrollViewer.Visibility = Visibility.Collapsed;
                SelectableScroll.Visibility = Visibility.Visible;
                if (CodeSelectableBox.Text != _pendingCode)
                {
                    CodeSelectableBox.Text = _pendingCode;
                }
                SelectableScroll.ScrollToVerticalOffset(offset);
                CodeSelectableBox_SelectionChanged(this, new RoutedEventArgs());

                if (FindBar.Visibility == Visibility.Visible && _matchIndices.Count > 0)
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                    {
                        SelectableSearchOverlay.UpdateMatches(CodeSelectableBox, _matchIndices, TxtSearch.Text.Length, _currentMatchIndex);
                    }));
                }
            }
        }

        private void FontSizeDecrease_Click(object sender, RoutedEventArgs e)
        {
            SetCodeFontSize(_currentFontSize - 1.0);
        }

        private void FontSizeIncrease_Click(object sender, RoutedEventArgs e)
        {
            SetCodeFontSize(_currentFontSize + 1.0);
        }

        private void FontSizeReset_Click(object sender, RoutedEventArgs e)
        {
            SetCodeFontSize(DefaultFontSize);
        }

        private void SetCodeFontSize(double newSize)
        {
            _currentFontSize = Math.Clamp(newSize, MinFontSize, MaxFontSize);
            TxtFontSize.Text = string.Create(CultureInfo.InvariantCulture, $"{_currentFontSize:0}pt");
            CodeHighlightBox.FontSize = _currentFontSize;
            CodeSelectableBox.FontSize = _currentFontSize;

            if (FindBar.Visibility == Visibility.Visible && _matchIndices.Count > 0)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    SelectableSearchOverlay.UpdateMatches(CodeSelectableBox, _matchIndices, TxtSearch.Text.Length, _currentMatchIndex);
                }));
            }
        }

        private void WordWrap_Changed(object sender, RoutedEventArgs e)
        {
            if (CodeSelectableBox == null || SelectableScroll == null) return;
            bool isWrap = ChkWordWrap.IsChecked == true;
            CodeSelectableBox.TextWrapping = isWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
            SelectableScroll.HorizontalScrollBarVisibility = isWrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;

            if (FindBar.Visibility == Visibility.Visible && _matchIndices.Count > 0)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    SelectableSearchOverlay.UpdateMatches(CodeSelectableBox, _matchIndices, TxtSearch.Text.Length, _currentMatchIndex);
                }));
            }
        }

        private void CodeContent_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                double delta = e.Delta > 0 ? 1.0 : -1.0;
                SetCodeFontSize(_currentFontSize + delta);
                e.Handled = true;
            }
        }

        private void CodeSelectableBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                double delta = e.Delta > 0 ? 1.0 : -1.0;
                SetCodeFontSize(_currentFontSize + delta);
                e.Handled = true;
            }
            else if (SelectableScroll != null)
            {
                if (e.Delta > 0)
                {
                    SelectableScroll.LineUp();
                    SelectableScroll.LineUp();
                    SelectableScroll.LineUp();
                }
                else
                {
                    SelectableScroll.LineDown();
                    SelectableScroll.LineDown();
                    SelectableScroll.LineDown();
                }
                e.Handled = true;
            }
        }

        private void CodeSelectableBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (RbModeSelectable.IsChecked != true || string.IsNullOrEmpty(_pendingCode))
            {
                TxtCursorPos.Visibility = Visibility.Collapsed;
                return;
            }

            int caret = CodeSelectableBox.CaretIndex;
            int line = 1;
            int col = 1;
            for (int i = 0; i < caret && i < _pendingCode.Length; i++)
            {
                if (_pendingCode[i] == '\n')
                {
                    line++;
                    col = 1;
                }
                else
                {
                    col++;
                }
            }

            int selLen = CodeSelectableBox.SelectionLength;
            if (selLen > 0)
            {
                TxtCursorPos.Text = string.Create(CultureInfo.InvariantCulture, $"Ln {line}, Col {col} ({selLen} sel)");
            }
            else
            {
                TxtCursorPos.Text = string.Create(CultureInfo.InvariantCulture, $"Ln {line}, Col {col}");
            }
            TxtCursorPos.Visibility = Visibility.Visible;
        }

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
            if (RbModeSelectable.IsChecked == true && CodeSelectableBox.Text != _pendingCode)
            {
                CodeSelectableBox.Text = _pendingCode;
            }
            if (CodeSelectableBox.SelectionLength > 0 && CodeSelectableBox.SelectionLength < 100)
            {
                TxtSearch.Text = CodeSelectableBox.SelectedText;
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
            CodeHighlightBox.ClearSearchMatches();
            SelectableSearchOverlay.Clear();
            if (RbModeSelectable.IsChecked == true)
            {
                CodeSelectableBox.Focus();
            }
        }

        private void CloseFind_Click(object sender, RoutedEventArgs e) => HideFindBar();

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchDebounceTimer.Stop();
            if (string.IsNullOrEmpty(TxtSearch.Text))
            {
                PerformSearch(select: false);
            }
            else
            {
                _searchDebounceTimer.Start();
            }
        }

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _searchDebounceTimer.Stop();
                bool reverse = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                if (_matchIndices.Count == 0 && !string.IsNullOrEmpty(TxtSearch.Text))
                {
                    PerformSearch(select: true);
                }
                else
                {
                    FindNextMatch(forward: !reverse);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                _searchDebounceTimer.Stop();
                FindNextMatch(forward: true);
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                _searchDebounceTimer.Stop();
                FindNextMatch(forward: false);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                _searchDebounceTimer.Stop();
                HideFindBar();
                e.Handled = true;
            }
        }

        private void FindPrev_Click(object sender, RoutedEventArgs e)
        {
            _searchDebounceTimer.Stop();
            FindNextMatch(forward: false);
        }

        private void FindNext_Click(object sender, RoutedEventArgs e)
        {
            _searchDebounceTimer.Stop();
            FindNextMatch(forward: true);
        }

        private void PerformSearch(bool select = true)
        {
            _matchIndices.Clear();
            _currentMatchIndex = -1;

            string query = TxtSearch.Text;
            string code = _pendingCode;

            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(code))
            {
                TxtMatchCount.Text = string.Empty;
                CodeHighlightBox.ClearSearchMatches();
                SelectableSearchOverlay.Clear();
                return;
            }

            int idx = 0;
            while ((idx = code.IndexOf(query, idx, StringComparison.OrdinalIgnoreCase)) != -1)
            {
                _matchIndices.Add(idx);
                idx += query.Length;
            }

            if (_matchIndices.Count == 0)
            {
                TxtMatchCount.Text = "No matches";
                CodeHighlightBox.ClearSearchMatches();
                SelectableSearchOverlay.Clear();
                return;
            }

            _currentMatchIndex = 0;
            UpdateMatchCountLabel();

            if (select)
            {
                HighlightCurrentMatch();
            }
            else
            {
                CodeHighlightBox.SetSearchMatches(_matchIndices, query.Length, -1);
                SelectableSearchOverlay.UpdateMatches(CodeSelectableBox, _matchIndices, query.Length, -1);
            }
        }

        private void FindNextMatch(bool forward = true)
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

            UpdateMatchCountLabel();
            HighlightCurrentMatch();
        }

        private void UpdateMatchCountLabel()
        {
            if (_matchIndices.Count == 0)
            {
                TxtMatchCount.Text = "No matches";
            }
            else
            {
                TxtMatchCount.Text = string.Create(CultureInfo.InvariantCulture, $"{_currentMatchIndex + 1} of {_matchIndices.Count}");
            }
        }

        private void HighlightCurrentMatch()
        {
            if (_currentMatchIndex < 0 || _currentMatchIndex >= _matchIndices.Count) return;
            int start = _matchIndices[_currentMatchIndex];
            int length = TxtSearch.Text.Length;

            // 1. Highlight all matches and the active match in the syntax highlight view
            CodeHighlightBox.SetSearchMatches(_matchIndices, length, _currentMatchIndex);

            // 2. Highlight all matches and the active match in the selectable text view
            SelectableSearchOverlay.UpdateMatches(CodeSelectableBox, _matchIndices, length, _currentMatchIndex);
            CodeSelectableBox.Select(start, length);

            // 3. Scroll viewports to the active match
            Rect rect = CodeHighlightBox.GetRectFromCharacterIndex(start, length);
            if (rect == Rect.Empty)
            {
                try
                {
                    rect = CodeSelectableBox.GetRectFromCharacterIndex(start);
                }
                catch { }
            }

            if (rect != Rect.Empty)
            {
                double targetY = Math.Max(0, rect.Top - 50);
                double targetX = Math.Max(0, rect.Left - 30);

                SyntaxScrollViewer?.ScrollToVerticalOffset(targetY);
                SyntaxScrollViewer?.ScrollToHorizontalOffset(targetX);

                SelectableScroll?.ScrollToVerticalOffset(targetY);
                SelectableScroll?.ScrollToHorizontalOffset(targetX);
            }
            else
            {
                int lineIndex = CodeSelectableBox.GetLineIndexFromCharacterIndex(start);
                CodeSelectableBox.ScrollToLine(lineIndex);
            }
        }

        // ── Actions: Generate, Copy & Save ───────────────────────────────────

        private void GenerateCode_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.GenerateCodeCommand.CanExecute(parameter: null))
            {
                _vm.GenerateCodeCommand.Execute(parameter: null);
            }
        }

        private void BadgeStale_Click(object sender, MouseButtonEventArgs e)
        {
            GenerateCode_Click(sender, e);
        }

        private void CopyCode_Click(object sender, RoutedEventArgs e)
        {
            string code = _vm.ExportedCode ?? string.Empty;
            if (TrySetClipboardText(code))
            {
                ShowCopyFeedback();
            }
            else
            {
                TxtCopyIcon.Text = "⚠";
                TxtCopyLabel.Text = "Locked";
                _copyFeedbackTimer.Stop();
                _copyFeedbackTimer.Start();
            }
        }

        private static bool TrySetClipboardText(string text)
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    Clipboard.SetDataObject(text, true);
                    return true;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    System.Threading.Thread.Sleep(50);
                }
                catch (Exception)
                {
                    return false;
                }
            }
            return false;
        }

        private void ShowCopyFeedback()
        {
            TxtCopyIcon.Text = "✓";
            TxtCopyLabel.Text = "Copied!";
            _copyFeedbackTimer.Stop();
            _copyFeedbackTimer.Start();
        }

        private void ResetCopyButtonVisual()
        {
            TxtCopyIcon.Text = "📋";
            TxtCopyLabel.Text = "Copy Code";
        }

        private void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            string code = _vm.ExportedCode ?? string.Empty;
            if (string.IsNullOrEmpty(code)) return;

            string spriteName = string.IsNullOrWhiteSpace(_vm.SpriteName) ? "sprite" : _vm.SpriteName;
            var (filter, defaultExt, defaultName) = GetFileFilterForFormat(_vm.ExportFormat, spriteName, _vm.GenerateFullSketch);

            var sfd = new SaveFileDialog
            {
                Filter = filter,
                DefaultExt = defaultExt,
                FileName = defaultName,
                Title = "Save Generated Code",
            };

            if (sfd.ShowDialog(this) == true)
            {
                try
                {
                    File.WriteAllText(sfd.FileName, code);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Failed to save file:\n{ex.Message}", "Error Saving File", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void ExportSketch_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            await _vm.ExecuteExportArduinoSketchFolderAsync();
        }

        private static (string Filter, string DefaultExt, string DefaultName) GetFileFilterForFormat(ExportFormat format, string baseName, bool isFullSketch)
        {
            if (isFullSketch)
            {
                return format switch
                {
                    ExportFormat.AdafruitGfx or ExportFormat.U8g2DrawBitmap or ExportFormat.U8g2DrawXBM or ExportFormat.PlainCArray or ExportFormat.LiquidCrystalChar or ExportFormat.Indexed2D =>
                        ("Arduino Sketch (*.ino)|*.ino|C/C++ Source (*.cpp;*.c)|*.cpp;*.c|C/C++ Header (*.h)|*.h|Text File (*.txt)|*.txt|All Files (*.*)|*.*", ".ino", $"{baseName}.ino"),
                    ExportFormat.MicroPython =>
                        ("Python Source (*.py)|*.py|Text File (*.txt)|*.txt|All Files (*.*)|*.*", ".py", $"{baseName}.py"),
                    ExportFormat.RawHex =>
                        ("Hex / Text File (*.hex;*.txt)|*.hex;*.txt|All Files (*.*)|*.*", ".hex", $"{baseName}.hex"),
                    ExportFormat.RawBinary =>
                        ("Binary File (*.bin)|*.bin|Text File (*.txt)|*.txt|All Files (*.*)|*.*", ".bin", $"{baseName}.bin"),
                    _ =>
                        ("Text File (*.txt)|*.txt|All Files (*.*)|*.*", ".txt", $"{baseName}.txt"),
                };
            }

            return format switch
            {
                ExportFormat.AdafruitGfx or ExportFormat.U8g2DrawBitmap or ExportFormat.PlainCArray or ExportFormat.LiquidCrystalChar or ExportFormat.Indexed2D =>
                    ("C/C++ Header (*.h)|*.h|C/C++ Source (*.c;*.cpp)|*.c;*.cpp|Text File (*.txt)|*.txt|All Files (*.*)|*.*", ".h", $"{baseName}.h"),
                ExportFormat.U8g2DrawXBM =>
                    ("XBM / Header (*.xbm;*.h)|*.xbm;*.h|C Source (*.c)|*.c|Text File (*.txt)|*.txt|All Files (*.*)|*.*", ".xbm", $"{baseName}.xbm"),
                ExportFormat.MicroPython =>
                    ("Python Source (*.py)|*.py|Text File (*.txt)|*.txt|All Files (*.*)|*.*", ".py", $"{baseName}.py"),
                ExportFormat.RawHex =>
                    ("Hex / Text File (*.hex;*.txt)|*.hex;*.txt|All Files (*.*)|*.*", ".hex", $"{baseName}.hex"),
                ExportFormat.RawBinary =>
                    ("Binary File (*.bin)|*.bin|Text File (*.txt)|*.txt|All Files (*.*)|*.*", ".bin", $"{baseName}.bin"),
                _ =>
                    ("Text File (*.txt)|*.txt|All Files (*.*)|*.*", ".txt", $"{baseName}.txt"),
            };
        }

        // ── Window Chrome & Teardown ─────────────────────────────────────────

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else if (e.LeftButton == MouseButtonState.Pressed)
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

        private void CaptionClose_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.F)
                {
                    ShowFindBar();
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.S)
                {
                    SaveAs_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.R)
                {
                    GenerateCode_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                }
                if (e.Key is Key.OemPlus or Key.Add)
                {
                    FontSizeIncrease_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                }
                if (e.Key is Key.OemMinus or Key.Subtract)
                {
                    FontSizeDecrease_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                }
                if (e.Key is Key.D0 or Key.NumPad0)
                {
                    FontSizeReset_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                }
            }
            else if (Keyboard.Modifiers == ModifierKeys.None)
            {
                if (e.Key == Key.F5)
                {
                    GenerateCode_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.F3)
                {
                    FindNextMatch(forward: true);
                    e.Handled = true;
                    return;
                }
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
            }
            else if (Keyboard.Modifiers == ModifierKeys.Shift && e.Key == Key.F3)
            {
                FindNextMatch(forward: false);
                e.Handled = true;
                return;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _syntaxDebounceTimer.Stop();
            _searchDebounceTimer.Stop();
            _copyFeedbackTimer.Stop();

            lock (_syntaxCtsLock)
            {
                _syntaxCts?.Cancel();
                _syntaxCts?.Dispose();
                _syntaxCts = null;
            }

            base.OnClosed(e);
        }

        public void Dispose()
        {
            lock (_syntaxCtsLock)
            {
                _syntaxCts?.Cancel();
                _syntaxCts?.Dispose();
                _syntaxCts = null;
            }
            GC.SuppressFinalize(this);
        }
    }
}

