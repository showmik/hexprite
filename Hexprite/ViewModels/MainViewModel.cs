using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Rendering;
using Hexprite.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Hexprite.ViewModels
{
    public partial class MainViewModel : ObservableObject, IBitmapBufferContext, IDocumentTab, IDisposable
    {
        // ── Services ──────────────────────────────────────────────────────
        private readonly ICodeGeneratorService _codeGen;
        private readonly IDrawingService _drawingService;
        public IDrawingService DrawingService => _drawingService;
        private readonly IHistoryService _historyService;
        public IHistoryService HistoryService => _historyService;
        private readonly ISelectionService _selectionService;
        public ISelectionService SelectionService => _selectionService;

        /// <summary>
        /// Wrapper for SelectionService.IsFloating that notifies property changes.
        /// Used by XAML bindings since SelectionService itself doesn't implement INotifyPropertyChanged.
        /// </summary>
        public bool IsSelectionFloating => _selectionService.IsFloating;
        private readonly IClipboardService _clipboardService;
        private readonly IPixelClipboardService _pixelClipboard;
        private readonly IDialogService _dialogService;
        private readonly IExportService _exportService;
        private readonly IFileImportExportService _importExportService;
        private readonly IHardwarePreviewService _hardwarePreview;
        private EventHandler<bool>? _hwPreviewEnabledHandler;
        private EventHandler<HardwarePreviewConnectionState>? _hwPreviewConnectionStateHandler;
        private DispatcherTimer? _portAutoRefreshTimer;
        public static event EventHandler<string>? GlobalPullRequested;
        private readonly SynchronizationContext _uiContext;
        
        private FileSystemWatcher? _linkedFileWatcher;

        // ── Document identity ─────────────────────────────────────────────
        private string? _filePath;
        public string? FilePath
        {
            get => _filePath;
            set { if (SetProperty(ref _filePath, value)) OnPropertyChanged(nameof(Title)); }
        }

        public string? ParentPackPath { get; set; }
        public string? ParentPackName { get; set; }
        public string? PackEntryName { get; set; }

        private string? _cleanStateHash;

        public void MarkAsClean()
        {
            if (SpriteState != null)
            {
                _cleanStateHash = SpriteState.ComputeHash();
            }
            IsDirty = false;
        }

        private void UpdateDirtyState()
        {
            if (_cleanStateHash != null && SpriteState != null)
            {
                IsDirty = SpriteState.ComputeHash() != _cleanStateHash;
            }
        }

        private bool _isDirty;
        public bool IsDirty
        {
            get => _isDirty;
            set 
            { 
                if (SetProperty(ref _isDirty, value)) 
                {
                    OnPropertyChanged(nameof(Title)); 
                    OnPropertyChanged(nameof(IsLinkedSourceStale));
                    if (!value)
                    {
                        _autosaveService?.ClearCurrentAutosave();
                    }
                }
            }
        }

        public string Title => IsDirty
            ? $"*{DisplayName}"
            : DisplayName;

        private readonly IAutosaveService _autosaveService;

        private string DisplayName
        {
            get
            {
                if (FilePath != null)
                    return Path.GetFileNameWithoutExtension(FilePath);

                if (!string.IsNullOrWhiteSpace(SpriteName) && SpriteName != "mySprite" && SpriteName != "sprite")
                    return SpriteName;

                return "Untitled";
            }
        }

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (SetProperty(ref _isActive, value))
                {
                    if (!value)
                    {
                        _playbackTimer?.Stop();
                    }
                    else
                    {
                        if (IsPlaying)
                        {
                            _playbackTimer?.Start();
                        }
                        
                        if (IsHardwarePreviewEnabled)
                        {
                            RedrawGridFromMemory();
                        }
                    }
                }
            }
        }

        public DocumentMode Mode => DocumentMode.Sprite;

        private static readonly System.Text.Json.JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

        public void Save()
        {
            // ShellViewModel calls SaveCommand which calls SaveFile
        }

        public void SaveAs(string path)
        {
            path = SafeFileIo.EnsureExtension(path, ".hexp");
            SuspendLinkedFileWatcher();
            try
            {
                SpriteState.NormalizeLayerState();
                SpriteState.ExportSettings = ExportSettings;
                string json = System.Text.Json.JsonSerializer.Serialize(SpriteState, IndentedJsonOptions);
                SafeFileIo.WriteAllTextAtomic(path, json, maxRetries: 5, createBackup: true);
                FilePath = path;
                MarkAsClean();
                ClearAutosave();
                UpdateLinkedFileHashIfMatches(path);
                UpdateSpriteNameFromFile();
                UserPreferencesService.AddRecentFile(path);
            }
            finally
            {
                ResumeLinkedFileWatcher();
            }
        }

        public bool HasUnsavedChanges => IsDirty;

        // ── Display preset (static, shared with dialogs) ──────────────────

        /// <summary>
        /// Common OLED/embedded display presets. Each entry is "Label|WxH".
        /// The View binds to this list to populate the preset ComboBox.
        /// </summary>
        public static IReadOnlyList<string> DisplayPresets { get; } =
        [
            "Custom",
            "5×8 1602 LCD Custom Char",
            "8×8 Icon",
            "10×10 Flipper App Icon",
            "14×14 Flipper Small Icon",
            "16×16 Sprite",
            "24×24 Sprite",
            "32×32 Tile",
            "48×48 Tile",
            "64×64 Large Sprite",
            "72×40 SSD1306 Micro",
            "84×48 Nokia 5110",
            "96×64 SSD1331 Color",
            "128×32 SSD1306 Mini",
            "128×64 Flipper Zero",
            "128×64 SSD1306",
            "128×128 ST7735 / Pico-8",
            "160×128 ST7735",
            "160×144 Game Boy",
            "240×135 ST7789 Mini",
            "240×160 Game Boy Advance",
            "240×240 ST7789",
            "256×64 SSD1322",
            "296×128 e-Paper",
            "320×200 DOS",
            "320×240 ILI9341",
            "400×240 Playdate",
            "400×300 e-Paper",
            "480×320 ILI9488",
        ];

        public string CanvasDimensionText => string.Create(CultureInfo.InvariantCulture, $"{SpriteState?.Width ?? 16}×{SpriteState?.Height ?? 16}");

        private int _cursorX;
        public int CursorX
        {
            get => _cursorX;
            set => SetProperty(ref _cursorX, value);
        }

        private int _cursorY;
        public int CursorY
        {
            get => _cursorY;
            set => SetProperty(ref _cursorY, value);
        }

        // ── Core state ────────────────────────────────────────────────────
        private SpriteState _spriteState = null!;
        public SpriteState SpriteState
        {
            get => _spriteState;
            private set
            {
                if (SetProperty(ref _spriteState, value))
                {
                    _spriteState.EnsureLayers();
                    RebuildLayerViewModels();
                    RebuildFrameViewModels();
                }
            }
        }

        public void LoadState(SpriteState state)
        {
            if (state == null) return;
            state.NormalizeLayerState();
            SpriteState = state;

            ReloadLayersFromState();
            RebuildFrameViewModels();

            bool isAnim = state.IsAnimationEnabled || (state.Frames != null && state.Frames.Count > 1) || state.FlipperCycle != null;
            IsAnimationEnabled = isAnim;
            SpriteState.IsAnimationEnabled = isAnim;
            FrameRateFps = state.FrameRateFps;
            PlaybackDirection = state.PlaybackDirection;
            IsDisplayInverted = state.IsDisplayInverted;

            if (state.ExportSettings != null)
            {
                ApplyExportSettings(state.ExportSettings);
            }

            _historyService.Clear();
            NotifyLinkChanged();
            IsDirty = true;
        }

        public ObservableCollection<LayerItemViewModel> Layers { get; } = [];

        // ── Tool state ────────────────────────────────────────────────────
        // Tool state is now global (managed by ShellViewModel). Each document
        // accesses the tool through callbacks to ensure consistency across tabs.
        private readonly Func<ToolMode> _getCurrentTool;
        private readonly Action<ToolMode> _setCurrentTool;
        public ToolMode CurrentTool
        {
            get => _getCurrentTool();
            set
            {
                var current = _getCurrentTool();
                if (current != value)
                {
                    _setCurrentTool(value);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsPixelPerfectAvailable));
                    SaveEditorPreferences();
                }
            }
        }

        private double _zoomLevel = 1.0;
        public double ZoomLevel
        {
            get => _zoomLevel;
            set => SetProperty(ref _zoomLevel, value);
        }

        internal Dictionary<ToolMode, PerToolSettings> _toolSettingsMap = [];

        public int BrushSize
        {
            get => _toolSettingsMap.TryGetValue(CurrentTool, out var ts) ? ts.BrushSize : 1;
            set
            {
                if (!_toolSettingsMap.ContainsKey(CurrentTool)) _toolSettingsMap[CurrentTool] = new PerToolSettings();
                var clamped = Math.Clamp(value, 1, 64);
                if (_toolSettingsMap[CurrentTool].BrushSize != clamped)
                {
                    _toolSettingsMap[CurrentTool].BrushSize = clamped;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsPixelPerfectAvailable));
                    SaveEditorPreferences();
                }
            }
        }

        public bool IsPixelPerfectEnabled
        {
            get => _toolSettingsMap.TryGetValue(CurrentTool, out var ts) && ts.IsPixelPerfectEnabled;
            set
            {
                if (!_toolSettingsMap.ContainsKey(CurrentTool)) _toolSettingsMap[CurrentTool] = new PerToolSettings();
                if (_toolSettingsMap[CurrentTool].IsPixelPerfectEnabled != value)
                {
                    _toolSettingsMap[CurrentTool].IsPixelPerfectEnabled = value;
                    OnPropertyChanged();
                    SaveEditorPreferences();
                }
            }
        }

        public bool IsContiguousFillEnabled
        {
            get => !_toolSettingsMap.TryGetValue(CurrentTool, out var ts) || ts.IsContiguousFillEnabled;
            set
            {
                if (!_toolSettingsMap.ContainsKey(CurrentTool)) _toolSettingsMap[CurrentTool] = new PerToolSettings();
                if (_toolSettingsMap[CurrentTool].IsContiguousFillEnabled != value)
                {
                    _toolSettingsMap[CurrentTool].IsContiguousFillEnabled = value;
                    OnPropertyChanged();
                    SaveEditorPreferences();
                }
            }
        }

        public bool IsPixelPerfectAvailable => CurrentTool == ToolMode.Pencil;

        public BrushShape BrushShape
        {
            get => _toolSettingsMap.TryGetValue(CurrentTool, out var ts) ? ts.BrushShape : BrushShape.Circle;
            set
            {
                if (!_toolSettingsMap.ContainsKey(CurrentTool)) _toolSettingsMap[CurrentTool] = new PerToolSettings();
                if (_toolSettingsMap[CurrentTool].BrushShape != value)
                {
                    _toolSettingsMap[CurrentTool].BrushShape = value;
                    OnPropertyChanged();
                    SaveEditorPreferences();
                }
            }
        }

        public int BrushAngle
        {
            get => _toolSettingsMap.TryGetValue(CurrentTool, out var ts) ? ts.BrushAngle : 0;
            set
            {
                if (!_toolSettingsMap.ContainsKey(CurrentTool)) _toolSettingsMap[CurrentTool] = new PerToolSettings();
                var clamped = ((value % 360) + 360) % 360;
                if (_toolSettingsMap[CurrentTool].BrushAngle != clamped)
                {
                    _toolSettingsMap[CurrentTool].BrushAngle = clamped;
                    OnPropertyChanged();
                    SaveEditorPreferences();
                }
            }
        }

        private DitherPattern _ditherPattern = DitherPattern.Checkerboard;
        public DitherPattern DitherPattern
        {
            get => _ditherPattern;
            set => SetProperty(ref _ditherPattern, value);
        }

        private bool _isTextEditing;
        /// <summary>True once SaveStateForUndo has been called for the current text editing session.</summary>
        private bool _textToolUndoSaved;
        public bool IsTextEditing
        {
            get => _isTextEditing;
            set => SetProperty(ref _isTextEditing, value);
        }

        private string _textToolContent = "";
        public string TextToolContent
        {
            get => _textToolContent;
            set
            {
                if (SetProperty(ref _textToolContent, value ?? ""))
                {
                    _textToolCaretIndex = Math.Clamp(_textToolCaretIndex, 0, _textToolContent.Length);
                    OnPropertyChanged(nameof(TextToolCaretIndex));
                    UpdateTextFloatingSelection();
                }
            }
        }

        private int _textToolCaretIndex;
        public int TextToolCaretIndex
        {
            get => _textToolCaretIndex;
            set
            {
                int clamped = Math.Clamp(value, 0, _textToolContent.Length);
                if (_textToolCaretIndex != clamped)
                {
                    _textToolCaretIndex = clamped;
                    OnPropertyChanged(nameof(TextToolCaretIndex));
                    UpdateCaretPositionOnly();
                }
            }
        }

        public void InsertText(string text)
        {
            if (!IsTextEditing || string.IsNullOrEmpty(text)) return;

            int insertAt = Math.Clamp(_textToolCaretIndex, 0, _textToolContent.Length);
            _textToolContent = _textToolContent.Insert(insertAt, text);
            _textToolCaretIndex = insertAt + text.Length;
            OnPropertyChanged(nameof(TextToolContent));
            OnPropertyChanged(nameof(TextToolCaretIndex));
            UpdateTextFloatingSelection();
        }

        public void DeleteBackward(bool word = false)
        {
            if (!IsTextEditing || _textToolContent.Length == 0 || _textToolCaretIndex <= 0) return;

            int current = _textToolCaretIndex;
            int target = current - 1;

            if (word)
            {
                while (target > 0 && char.IsWhiteSpace(_textToolContent[target - 1]))
                    target--;
                while (target > 0 && !char.IsWhiteSpace(_textToolContent[target - 1]))
                    target--;
            }

            int count = current - target;
            _textToolContent = _textToolContent.Remove(target, count);
            _textToolCaretIndex = target;
            OnPropertyChanged(nameof(TextToolContent));
            OnPropertyChanged(nameof(TextToolCaretIndex));
            UpdateTextFloatingSelection();
        }

        public void DeleteForward(bool word = false)
        {
            if (!IsTextEditing || _textToolContent.Length == 0 || _textToolCaretIndex >= _textToolContent.Length) return;

            int current = _textToolCaretIndex;
            int target = current + 1;

            if (word)
            {
                while (target < _textToolContent.Length && !char.IsWhiteSpace(_textToolContent[target]))
                    target++;
                while (target < _textToolContent.Length && char.IsWhiteSpace(_textToolContent[target]))
                    target++;
            }

            int count = target - current;
            _textToolContent = _textToolContent.Remove(current, count);
            OnPropertyChanged(nameof(TextToolContent));
            UpdateTextFloatingSelection();
        }

        public void MoveCaretLeft(bool ctrl = false)
        {
            if (!IsTextEditing) return;
            if (!ctrl)
            {
                TextToolCaretIndex--;
            }
            else
            {
                int target = _textToolCaretIndex;
                while (target > 0 && char.IsWhiteSpace(_textToolContent[target - 1]))
                    target--;
                while (target > 0 && !char.IsWhiteSpace(_textToolContent[target - 1]))
                    target--;
                TextToolCaretIndex = target;
            }
        }

        public void MoveCaretRight(bool ctrl = false)
        {
            if (!IsTextEditing) return;
            if (!ctrl)
            {
                TextToolCaretIndex++;
            }
            else
            {
                int target = _textToolCaretIndex;
                while (target < _textToolContent.Length && !char.IsWhiteSpace(_textToolContent[target]))
                    target++;
                while (target < _textToolContent.Length && char.IsWhiteSpace(_textToolContent[target]))
                    target++;
                TextToolCaretIndex = target;
            }
        }

        public void MoveCaretUp()
        {
            if (!IsTextEditing || string.IsNullOrEmpty(_textToolContent)) return;

            var (lineIdx, colIdx, _) = GetCaretLineAndCol();
            if (lineIdx <= 0)
            {
                TextToolCaretIndex = 0;
                return;
            }

            var lines = _textToolContent.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
            int prevLineLen = lines[lineIdx - 1].Length;
            int prevLineStart = 0;
            for (int i = 0; i < lineIdx - 1; i++)
                prevLineStart += lines[i].Length + 1;

            int targetCol = Math.Min(colIdx, prevLineLen);
            TextToolCaretIndex = prevLineStart + targetCol;
        }

        public void MoveCaretDown()
        {
            if (!IsTextEditing || string.IsNullOrEmpty(_textToolContent)) return;

            var (lineIdx, colIdx, _) = GetCaretLineAndCol();
            var lines = _textToolContent.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
            if (lineIdx >= lines.Length - 1)
            {
                TextToolCaretIndex = _textToolContent.Length;
                return;
            }

            int nextLineLen = lines[lineIdx + 1].Length;
            int nextLineStart = 0;
            for (int i = 0; i <= lineIdx; i++)
                nextLineStart += lines[i].Length + 1;

            int targetCol = Math.Min(colIdx, nextLineLen);
            TextToolCaretIndex = nextLineStart + targetCol;
        }

        public void MoveCaretHome()
        {
            if (!IsTextEditing || string.IsNullOrEmpty(_textToolContent)) return;
            var (_, _, lineStart) = GetCaretLineAndCol();
            TextToolCaretIndex = lineStart;
        }

        public void MoveCaretEnd()
        {
            if (!IsTextEditing || string.IsNullOrEmpty(_textToolContent)) return;
            var (lineIdx, _, lineStart) = GetCaretLineAndCol();
            var lines = _textToolContent.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
            TextToolCaretIndex = lineStart + lines[lineIdx].Length;
        }

        private (int lineIdx, int colIdx, int lineStart) GetCaretLineAndCol()
        {
            var lines = _textToolContent.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
            int runningLen = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                int lineLen = lines[i].Length;
                if (_textToolCaretIndex <= runningLen + lineLen || i == lines.Length - 1)
                {
                    return (i, Math.Clamp(_textToolCaretIndex - runningLen, 0, lineLen), runningLen);
                }
                runningLen += lineLen + 1;
            }
            return (0, 0, 0);
        }

        private void UpdateCaretPositionOnly()
        {
            var fontFamily = _selectedFont?.FontFamily ?? new System.Windows.Media.FontFamily("Arial");
            var (cx, cy, ch) = Rendering.TextRenderer.GetCaretPosition(
                TextToolContent, _textToolCaretIndex, fontFamily, FontSize, IsBold, IsItalic, LetterSpacing, PixelScale, LineHeight,
                _textAlignment, _textToolX, _textToolY);

            _textCaretPixelX = cx;
            _textCaretPixelY = cy;
            _textCaretPixelHeight = ch;
            OnPropertyChanged(nameof(TextToolCaretX));
            OnPropertyChanged(nameof(TextToolCaretY));
            OnPropertyChanged(nameof(TextToolCaretHeight));
        }

        private int _textToolX;
        public int TextToolX
        {
            get => _textToolX;
            private set
            {
                if (SetProperty(ref _textToolX, value))
                {
                    OnPropertyChanged(nameof(TextToolCaretX));
                }
            }
        }

        private int _textToolY;
        public int TextToolY
        {
            get => _textToolY;
            private set
            {
                if (SetProperty(ref _textToolY, value))
                {
                    OnPropertyChanged(nameof(TextToolCaretY));
                }
            }
        }

        private int _textCaretPixelX;
        private int _textCaretPixelY;
        private int _textCaretPixelHeight;

        public double TextToolCaretX => _textCaretPixelX * CellSize;
        public double TextToolCaretY => _textCaretPixelY * CellSize;
        public double TextToolCaretHeight => _textCaretPixelHeight * CellSize;

        public void StartTextEditing(int x, int y)
        {
            if (IsTextEditing)
            {
                if (!string.IsNullOrEmpty(_textToolContent))
                {
                    // Commit previous text (undo was already saved on first keystroke)
                    StopTextEditing();
                }
                else
                {
                    // Empty text — discard the editing session without committing
                    IsTextEditing = false;
                    if (SelectionService.IsFloating)
                    {
                        SelectionService.Cancel();
                        RedrawGridFromMemory();
                    }
                }
            }

            TextToolX = x;
            TextToolY = y;
            _textToolContent = "";
            _textToolCaretIndex = 0;
            _textToolUndoSaved = false;
            _textCaretPixelX = x;
            _textCaretPixelY = y;
            int nativeHeight = _selectedFont != null
                ? Rendering.TextRenderer.GetNativePixelHeight(_selectedFont.FontFamily, IsBold, IsItalic)
                : 0;
            _textCaretPixelHeight = nativeHeight > 0 ? nativeHeight * PixelScale : FontSize;
            if (_textCaretPixelHeight <= 0) _textCaretPixelHeight = 8 * PixelScale;
            OnPropertyChanged(nameof(TextToolCaretX));
            OnPropertyChanged(nameof(TextToolCaretY));
            OnPropertyChanged(nameof(TextToolCaretHeight));
            IsTextEditing = true;
            OnPropertyChanged(nameof(TextToolContent));
            OnPropertyChanged(nameof(TextToolCaretIndex));
        }

        public void StopTextEditing()
        {
            if (!IsTextEditing) return;

            IsTextEditing = false;
            _textToolUndoSaved = false;
            
            if (SelectionService.IsFloating)
            {
                SelectionService.CommitSelection(SpriteState);
                SelectionService.Cancel();
                RedrawGridFromMemory();
                MarkCodeStale();
            }
        }

        private void UpdateTextFloatingSelection()
        {
            if (!IsTextEditing) return;

            // Push undo on the first real keystroke, not on click-to-start,
            // so that an empty text session doesn't leave a spurious undo entry.
            if (!_textToolUndoSaved && !string.IsNullOrEmpty(TextToolContent))
            {
                SaveStateForUndo();
                _textToolUndoSaved = true;
            }

            // Generate mask from text
            var fontFamily = _selectedFont?.FontFamily ?? new System.Windows.Media.FontFamily("Arial");
            var mask = Rendering.TextRenderer.RenderTextToMonochromeMask(
                TextToolContent, fontFamily, FontSize, IsBold, IsItalic, LetterSpacing, PixelScale, LineHeight,
                alignment: _textAlignment);

            int w = mask.GetLength(0);
            int h = mask.GetLength(1);

            // Update caret position dynamically
            var (cx, cy, ch) = Rendering.TextRenderer.GetCaretPosition(
                TextToolContent, _textToolCaretIndex, fontFamily, FontSize, IsBold, IsItalic, LetterSpacing, PixelScale, LineHeight,
                _textAlignment, _textToolX, _textToolY);

            _textCaretPixelX = cx;
            _textCaretPixelY = cy;
            _textCaretPixelHeight = ch;
            OnPropertyChanged(nameof(TextToolCaretX));
            OnPropertyChanged(nameof(TextToolCaretY));
            OnPropertyChanged(nameof(TextToolCaretHeight));

            // If empty, clear floating selection
            if (w == 0 || h == 0)
            {
                if (SelectionService.IsFloating)
                {
                    SelectionService.Cancel();
                    RedrawGridFromMemory();
                }
                return;
            }

            SelectionService.Cancel(); // Clear previous

            // Compute X offset based on text alignment
            int placeX = _textToolX;
            if (_textAlignment == Core.TextToolAlignment.Center)
                placeX = _textToolX - w / 2;
            else if (_textAlignment == Core.TextToolAlignment.Right)
                placeX = _textToolX - w;

            SelectionService.PasteAsFloatingAt(new Core.PixelClipboardData(mask, w, h), placeX, _textToolY);
            RedrawGridFromMemory(updateHardware: false);
        }

        // ── Font management ───────────────────────────────────────────────

        private List<Core.FontEntry> _availableFonts = [];
        public List<Core.FontEntry> AvailableFonts
        {
            get => _availableFonts;
            private set => SetProperty(ref _availableFonts, value);
        }

        private Core.FontEntry? _selectedFont;
        public Core.FontEntry? SelectedFont
        {
            get => _selectedFont;
            set
            {
                if (SetProperty(ref _selectedFont, value))
                {
                    OnPropertyChanged(nameof(FontFamily));
                    // Cache native pixel height for SIZE ↔ SCALE sync
                    _nativePixelHeight = value != null
                        ? Rendering.TextRenderer.GetNativePixelHeight(value.FontFamily, IsBold, IsItalic)
                        : 0;
                    // Always default to SCALE=1 when switching fonts
                    if (_nativePixelHeight > 0)
                    {
                        _syncingScaleSize = true;
                        _pixelScale = 1;
                        _fontSize = _nativePixelHeight;
                        OnPropertyChanged(nameof(FontSize));
                        OnPropertyChanged(nameof(PixelScale));
                        _syncingScaleSize = false;
                    }
                    UpdateTextFloatingSelection();
                    SaveEditorPreferences();
                }
            }
        }

        public string FontFamily => _selectedFont?.Name ?? "Arial";

        // Native pixel height of the current font (0 = not a pixel font)
        private int _nativePixelHeight;
        // Guard flag to prevent infinite SIZE ↔ SCALE update loops
        private bool _syncingScaleSize;

        public void InitializeFonts()
        {
            AvailableFonts = Services.FontService.LoadAllFonts();
            var prefs = Services.UserPreferencesService.Get();
            _pixelScale = Math.Clamp(prefs.TextPixelScale, 1, 10);
            _letterSpacing = Math.Clamp(prefs.TextLetterSpacing, -5, 20);
            _lineHeight = Math.Clamp(prefs.TextLineHeight, -10, 20);
            _textAlignment = prefs.TextAlignment;
            if (!string.IsNullOrEmpty(prefs.TextFontName))
            {
                SelectedFont = AvailableFonts.FirstOrDefault(f => f.Name == prefs.TextFontName) ?? AvailableFonts.FirstOrDefault();
            }
            else
            {
                SelectedFont = AvailableFonts.FirstOrDefault();
            }
        }

        public void RefreshFonts()
        {
            Rendering.TextRenderer.ClearCache();
            var previousName = _selectedFont?.Name;
            AvailableFonts = Services.FontService.LoadAllFonts();
            // Try to reselect the same font, fall back to first
            SelectedFont = AvailableFonts.FirstOrDefault(f => f.Name == previousName)
                           ?? AvailableFonts.FirstOrDefault();
        }

        private int _fontSize = 12;
        public int FontSize
        {
            get => _fontSize;
            set
            {
                if (SetProperty(ref _fontSize, Math.Clamp(value, 4, 144)))
                {
                    // For pixel fonts: auto-compute SCALE from SIZE
                    if (_nativePixelHeight > 0 && !_syncingScaleSize)
                    {
                        _syncingScaleSize = true;
                        _pixelScale = Math.Clamp(
                            Math.Max(1, (int)Math.Round((double)_fontSize / _nativePixelHeight, MidpointRounding.AwayFromZero)),
                            1, 10);
                        OnPropertyChanged(nameof(PixelScale));
                        _syncingScaleSize = false;
                    }
                    UpdateTextFloatingSelection();
                    SaveEditorPreferences();
                }
            }
        }

        private bool _isBold;
        public bool IsBold
        {
            get => _isBold;
            set
            {
                if (SetProperty(ref _isBold, value))
                    UpdateTextFloatingSelection();
            }
        }

        private bool _isItalic;
        public bool IsItalic
        {
            get => _isItalic;
            set
            {
                if (SetProperty(ref _isItalic, value))
                    UpdateTextFloatingSelection();
            }
        }

        private int _letterSpacing = 1;
        public int LetterSpacing
        {
            get => _letterSpacing;
            set
            {
                if (SetProperty(ref _letterSpacing, Math.Clamp(value, -5, 20)))
                {
                    UpdateTextFloatingSelection();
                    SaveEditorPreferences();
                }
            }
        }

        private int _pixelScale = 1;
        public int PixelScale
        {
            get => _pixelScale;
            set
            {
                if (SetProperty(ref _pixelScale, Math.Clamp(value, 1, 10)))
                {
                    // For pixel fonts: auto-update SIZE from SCALE
                    if (_nativePixelHeight > 0 && !_syncingScaleSize)
                    {
                        _syncingScaleSize = true;
                        _fontSize = Math.Clamp(_nativePixelHeight * _pixelScale, 4, 144);
                        OnPropertyChanged(nameof(FontSize));
                        _syncingScaleSize = false;
                    }
                    UpdateTextFloatingSelection();
                    SaveEditorPreferences();
                }
            }
        }

        private int _lineHeight = 1;
        public int LineHeight
        {
            get => _lineHeight;
            set
            {
                if (SetProperty(ref _lineHeight, Math.Clamp(value, -10, 20)))
                    UpdateTextFloatingSelection();
            }
        }

        private Core.TextToolAlignment _textAlignment = Core.TextToolAlignment.Left;
        public Core.TextToolAlignment TextAlignment
        {
            get => _textAlignment;
            set
            {
                if (SetProperty(ref _textAlignment, value))
                    UpdateTextFloatingSelection();
            }
        }

        // ── Symmetry state ────────────────────────────────────────────────
        private bool _isSymmetryHorizontalEnabled;
        public bool IsSymmetryHorizontalEnabled
        {
            get => _isSymmetryHorizontalEnabled;
            set
            {
                if (SetProperty(ref _isSymmetryHorizontalEnabled, value))
                {
                    SaveEditorPreferences();
                    OnPropertyChanged(nameof(IsSymmetryVisible));
                }
            }
        }

        private bool _isSymmetryVerticalEnabled;
        public bool IsSymmetryVerticalEnabled
        {
            get => _isSymmetryVerticalEnabled;
            set
            {
                if (SetProperty(ref _isSymmetryVerticalEnabled, value))
                {
                    SaveEditorPreferences();
                    OnPropertyChanged(nameof(IsSymmetryVisible));
                }
            }
        }

        private double _symmetryAxisX;
        public double SymmetryAxisX
        {
            get => _symmetryAxisX;
            set
            {
                if (SetProperty(ref _symmetryAxisX, value))
                    OnPropertyChanged(nameof(VerticalSymmetryLinePosition));
            }
        }

        private double _symmetryAxisY;
        public double SymmetryAxisY
        {
            get => _symmetryAxisY;
            set
            {
                if (SetProperty(ref _symmetryAxisY, value))
                    OnPropertyChanged(nameof(HorizontalSymmetryLinePosition));
            }
        }

        public bool IsSymmetryVisible =>
            (IsSymmetryHorizontalEnabled || IsSymmetryVerticalEnabled) &&
            (CurrentTool == ToolMode.Pencil ||
             CurrentTool == ToolMode.Eraser ||
             CurrentTool == ToolMode.Line ||
             CurrentTool == ToolMode.Rectangle ||
             CurrentTool == ToolMode.Ellipse ||
             CurrentTool == ToolMode.FilledRectangle ||
             CurrentTool == ToolMode.FilledEllipse ||
             CurrentTool == ToolMode.Fill);

        public double VerticalSymmetryLinePosition => SymmetryAxisX * CellSize;
        public double HorizontalSymmetryLinePosition => SymmetryAxisY * CellSize;

        // Flags read by the View to know whether a shape preview is in progress
        public bool IsDrawingLine => _toolInput.IsDrawingLine;
        public bool IsDrawingRectangle => _toolInput.IsDrawingRectangle;
        public bool IsDrawingEllipse => _toolInput.IsDrawingEllipse;
        public bool IsDrawingFilledRectangle => _toolInput.IsDrawingFilledRectangle;
        public bool IsDrawingFilledEllipse => _toolInput.IsDrawingFilledEllipse;
        public bool IsDrawingGradient => _toolInput.IsDrawingGradient;
        public bool IsMoving => _toolInput != null && _toolInput.IsMoving;

        // ── Status message (data-bound, replaces direct label manipulation) ──
        private string _statusMessage = string.Empty;
        private CancellationTokenSource? _statusCts;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// Shows a temporary status message that auto-clears after a delay.
        /// Replaces the old CopyHexExecuted event + DispatcherTimer approach.
        /// Fire-and-forget with exception handling to prevent async void crash behavior.
        /// </summary>
        public void ShowStatus(string message, int delayMs = 2000)
        {
            StatusMessage = message;

            // Cancel and dispose any pending clear operation (Bug 3: CTS was never disposed)
            _statusCts?.Cancel();
            _statusCts?.Dispose();
            _statusCts = new CancellationTokenSource();
            var token = _statusCts.Token;

            _ = ClearStatusAsync(message, delayMs, token);
        }

        private async System.Threading.Tasks.Task ClearStatusAsync(string message, int delayMs, CancellationToken token)
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(delayMs, token);
                // Only clear if no newer message replaced this one
                if (StatusMessage == message)
                    StatusMessage = string.Empty;
            }
            catch (OperationCanceledException)
            {
                // Expected when a new status message is shown
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Error(ex, "MainViewModel.ClearStatusAsync");
            }
        }

        // ── Status bar: Selection info ────────────────────────────────────
        private string _selectionInfo = string.Empty;
        public string SelectionInfo
        {
            get => _selectionInfo;
            private set => SetProperty(ref _selectionInfo, value);
        }

        // ── Status bar: Layer hover info ─────────────────────────────────
        private string _layerHoverInfo = string.Empty;
        public string LayerHoverInfo
        {
            get => _layerHoverInfo;
            set => SetProperty(ref _layerHoverInfo, value);
        }

        // ── Status bar: Keyboard modifiers ────────────────────────────────
        private bool _isShiftPressed;
        public bool IsShiftPressed
        {
            get => _isShiftPressed;
            set => SetProperty(ref _isShiftPressed, value);
        }

        private bool _isCtrlPressed;
        public bool IsCtrlPressed
        {
            get => _isCtrlPressed;
            set => SetProperty(ref _isCtrlPressed, value);
        }

        private bool _isAltPressed;
        public bool IsAltPressed
        {
            get => _isAltPressed;
            set => SetProperty(ref _isAltPressed, value);
        }


        private bool _isProcessing;
        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                if (SetProperty(ref _isProcessing, value))
                {
                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                    });
                }
            }
        }

        // ── Focus Mode (Display Only) ────────────────────────────────────────
        /// <summary>
        /// When enabled, inactive layers are rendered at reduced opacity.
        /// This is DISPLAY ONLY - it never affects pixel data or export.
        /// </summary>
        private bool _isFocusModeEnabled;
        public bool IsFocusModeEnabled
        {
            get => _isFocusModeEnabled;
            set
            {
                if (SetProperty(ref _isFocusModeEnabled, value))
                {
                    RedrawGridFromMemory();
                    SaveEditorPreferences();
                }
            }
        }

        /// <summary>
        /// Opacity multiplier for inactive layers when Focus Mode is enabled.
        /// Default is 0.3 (30% opacity). Range 0.1-0.5.
        /// </summary>
        private float _inactiveFocusOpacity = 0.3f;
        public float InactiveFocusOpacity
        {
            get => _inactiveFocusOpacity;
            set
            {
                float clamped = Math.Clamp(value, 0.1f, 0.5f);
                if (SetProperty(ref _inactiveFocusOpacity, clamped))
                {
                    if (_isFocusModeEnabled)
                        RedrawGridFromMemory();
                    SaveEditorPreferences();
                }
            }
        }

        // ── Floating Paste Mode ─────────────────────────────────────────────
        /// <summary>
        /// Determines how floating selection pixels are applied to the canvas.
        /// Transparent: false pixels are skipped (default).
        /// Opaque: false pixels overwrite canvas (full stamp).
        /// Persisted as a user preference.
        /// </summary>
        private FloatingPasteMode _floatingPasteMode = FloatingPasteMode.Transparent;
        public FloatingPasteMode FloatingPasteMode
        {
            get => _floatingPasteMode;
            set
            {
                if (SetProperty(ref _floatingPasteMode, value))
                {
                    RedrawGridFromMemory();
                    SaveEditorPreferences();
                }
            }
        }

        // ── Linked Source properties ──────────────────────────────────────
        public bool IsLinked => SpriteState?.IsLinked ?? false;
        public bool IsNotLinked => !IsLinked;
        public bool IsLinkedSourceStale => IsLinked && IsDirty;

        private bool _linkedFileChangedExternally;
        public bool LinkedFileChangedExternally
        {
            get => _linkedFileChangedExternally;
            set
            {
                if (_linkedFileChangedExternally != value)
                {
                    _linkedFileChangedExternally = value;
                    OnPropertyChanged(nameof(LinkedFileChangedExternally));
                }
            }
        }

        private bool _isLinkedFileMissing;
        public bool IsLinkedFileMissing
        {
            get => _isLinkedFileMissing;
            set
            {
                if (_isLinkedFileMissing != value)
                {
                    _isLinkedFileMissing = value;
                    OnPropertyChanged(nameof(IsLinkedFileMissing));
                    ((RelayCommand?)UpdateLinkedSourceCommand)?.NotifyCanExecuteChanged();
                    ((RelayCommand?)PullLinkedSourceCommand)?.NotifyCanExecuteChanged();
                }
            }
        }

        public string? LinkedSourceFileName => SpriteState?.LinkedSourceFileName;
        public string? LinkedVariableName => SpriteState?.LinkedVariableName;

        public void NotifyLinkChanged()
        {
            if (!string.IsNullOrEmpty(SpriteState?.LinkedSourceFile))
            {
                try
                {
                    SpriteState.LinkedSourceFile = Path.GetFullPath(SpriteState.LinkedSourceFile);
                }
                catch { }
            }

            OnPropertyChanged(nameof(IsLinked));
            OnPropertyChanged(nameof(IsNotLinked));
            OnPropertyChanged(nameof(IsLinkedSourceStale));
            OnPropertyChanged(nameof(LinkedSourceFileName));
            OnPropertyChanged(nameof(LinkedVariableName));
            if (IsLinked)
            {
                _isLinkedSourceExpandedOverride = null;
            }
            OnPropertyChanged(nameof(IsLinkedSourceExpanded));
            ((RelayCommand)UpdateLinkedSourceCommand).NotifyCanExecuteChanged();
            ((RelayCommand)PullLinkedSourceCommand).NotifyCanExecuteChanged();
            ((RelayCommand)RestoreLinkedSourceCommand).NotifyCanExecuteChanged();
            ((RelayCommand)UnlinkSourceCommand).NotifyCanExecuteChanged();
            ((RelayCommand)RelinkSourceCommand).NotifyCanExecuteChanged();

            // Start or stop watching the linked file for external changes
            if (IsLinked)
                StartWatchingLinkedFile();
            else
                StopWatchingLinkedFile();
        }

        /// <summary>
        /// Centralised code snippet parser supporting all export formats (Adafruit GFX, U8g2 XBM, Flipper XBM, Raw Binary, Raw Hex, Indexed 2D).
        /// </summary>
        public static void ParseCodeToState(ICodeGeneratorService codeGen, ExportFormat? format, string codeSnippet, SpriteState state)
        {
            if (format == ExportFormat.Indexed2D)
                codeGen.ParseIndexed2DToState(codeSnippet, state);
            else if (format == ExportFormat.U8g2DrawXBM || format == ExportFormat.FlipperXbm)
                codeGen.ParseXbmToState(codeSnippet, state);
            else if (format == ExportFormat.RawBinary)
                codeGen.ParseBinaryToState(codeSnippet, state);
            else if (format == ExportFormat.RawHex)
                codeGen.ParseHexToState(codeSnippet, state);
            else if (format == ExportFormat.LiquidCrystalChar)
                codeGen.ParseLiquidCrystalToState(codeSnippet, state);
            else
                codeGen.ParseAdafruitGfxToState(codeSnippet, state);
        }

        private void StartWatchingLinkedFile()
        {
            StopWatchingLinkedFile();
            if (SpriteState?.LinkedSourceFile == null) return;

            string? dir = Path.GetDirectoryName(SpriteState.LinkedSourceFile);
            string? name = Path.GetFileName(SpriteState.LinkedSourceFile);
            if (dir == null || name == null || !Directory.Exists(dir)) return;

            try
            {
                string hash = ComputeFileHash(SpriteState.LinkedSourceFile);
                lock (_linkedFileLock)
                {
                    if (!string.IsNullOrEmpty(hash))
                    {
                        _documentSyncedHash = hash;
                        _globalLastSavedHashes[SpriteState.LinkedSourceFile] = hash;
                    }
                    _isShowingChangeDialog = false;
                    LinkedFileChangedExternally = false;
                }

                ReattachLinkedFileWatcher();
            }
            catch { /* best-effort: don't crash if watcher fails (e.g., network drives) */ }
        }

        private void ReattachLinkedFileWatcher()
        {
            if (SpriteState?.LinkedSourceFile == null) return;
            string? dir = Path.GetDirectoryName(SpriteState.LinkedSourceFile);
            string? name = Path.GetFileName(SpriteState.LinkedSourceFile);
            if (dir == null || name == null || !Directory.Exists(dir)) return;

            try
            {
                if (_linkedFileWatcher != null)
                {
                    _linkedFileWatcher.Changed -= OnLinkedFileChanged;
                    _linkedFileWatcher.Created -= OnLinkedFileChanged;
                    _linkedFileWatcher.Deleted -= OnLinkedFileChanged;
                    _linkedFileWatcher.Renamed -= OnLinkedFileChanged;
                    _linkedFileWatcher.EnableRaisingEvents = false;
                    _linkedFileWatcher.Dispose();
                    _linkedFileWatcher = null;
                }

                _linkedFileWatcher = new FileSystemWatcher(dir, name)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
                    InternalBufferSize = 65536,
                    EnableRaisingEvents = true,
                };
                _linkedFileWatcher.Changed += OnLinkedFileChanged;
                _linkedFileWatcher.Created += OnLinkedFileChanged;
                _linkedFileWatcher.Deleted += OnLinkedFileChanged;
                _linkedFileWatcher.Renamed += OnLinkedFileChanged;
            }
            catch { /* best-effort */ }
        }

        private void StopWatchingLinkedFile()
        {
            if (_linkedFileWatcher != null)
            {
                _linkedFileWatcher.Changed -= OnLinkedFileChanged;
                _linkedFileWatcher.Created -= OnLinkedFileChanged;
                _linkedFileWatcher.Deleted -= OnLinkedFileChanged;
                _linkedFileWatcher.Renamed -= OnLinkedFileChanged;
                _linkedFileWatcher.EnableRaisingEvents = false;
                _linkedFileWatcher.Dispose();
                _linkedFileWatcher = null;
            }
            lock (_linkedFileLock)
            {
                _isShowingChangeDialog = false;
            }
            LinkedFileChangedExternally = false;
            IsLinkedFileMissing = false;
        }

        private readonly Lock _linkedFileLock = new();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _globalLastSavedHashes = new(StringComparer.OrdinalIgnoreCase);
        private string? _documentSyncedHash;
        private bool _isShowingChangeDialog;
        private static bool _isAnySyncDialogShowing;

        private static bool TryBeginSyncDialog()
        {
            if (_isAnySyncDialogShowing) return false;
            _isAnySyncDialogShowing = true;
            return true;
        }

        private static void EndSyncDialog()
        {
            _isAnySyncDialogShowing = false;
        }

        private int _linkedFileWatcherSuspendCount;

        public void SuspendLinkedFileWatcher()
        {
            lock (_linkedFileLock)
            {
                _linkedFileWatcherSuspendCount++;
                if (_linkedFileWatcher != null)
                    _linkedFileWatcher.EnableRaisingEvents = false;
            }
        }

        public void ResumeLinkedFileWatcher()
        {
            lock (_linkedFileLock)
            {
                _linkedFileWatcherSuspendCount--;
                if (_linkedFileWatcherSuspendCount <= 0)
                {
                    _linkedFileWatcherSuspendCount = 0;
                    if (_linkedFileWatcher != null)
                        _linkedFileWatcher.EnableRaisingEvents = true;
                }
            }
        }

        public void UpdateLinkedFileHashIfMatches(string path)
        {
            if (SpriteState != null && string.Equals(SpriteState.LinkedSourceFile, path, StringComparison.OrdinalIgnoreCase))
            {
                string hash = ComputeFileHash(path);
                if (!string.IsNullOrEmpty(hash))
                {
                    lock (_linkedFileLock)
                    {
                        _documentSyncedHash = hash;
                        _globalLastSavedHashes[path] = hash;
                        _isShowingChangeDialog = false;
                        LinkedFileChangedExternally = false;
                    }
                }
            }
        }

        [GeneratedRegex(@"[\s,]+", RegexOptions.None, matchTimeoutMilliseconds: 250)]
        private static partial Regex HashStrippedTextRegex { get; }

        private static string ComputeHashFromString(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            // Strip whitespace and commas, and ignore casing, so formatters don't trigger external change warnings
            string strippedText = HashStrippedTextRegex.Replace(text, "").ToLowerInvariant();
            byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(strippedText));
            return Convert.ToBase64String(hash);
        }

        private static string ComputeFileHash(string filePath, int maxRetries = 10)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return string.Empty;
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    string text = reader.ReadToEnd();
                    return ComputeHashFromString(text);
                }
                catch (Exception)
                {
                    if (attempt + 1 < maxRetries)
                    {
                        int delay = Math.Min(200, 10 * (1 << Math.Min(attempt, 4)));
                        System.Threading.Thread.Sleep(delay);
                    }
                }
            }
            return string.Empty;
        }

        internal void OnLinkedFileChanged(object sender, FileSystemEventArgs e)
        {
            lock (_linkedFileLock)
            {
                if (_linkedFileWatcherSuspendCount > 0)
                    return;
            }

            string? targetFile = SpriteState?.LinkedSourceFile;
            if (string.IsNullOrEmpty(targetFile)) return;

            string? targetName = Path.GetFileName(targetFile);
            if (targetName == null) return;

            bool isMatch = string.Equals(e.Name, targetName, StringComparison.OrdinalIgnoreCase);
            if (!isMatch && e is RenamedEventArgs re)
            {
                isMatch = string.Equals(re.OldName, targetName, StringComparison.OrdinalIgnoreCase) || 
                          string.Equals(re.Name, targetName, StringComparison.OrdinalIgnoreCase);
            }
            if (!isMatch) return;

            if (!File.Exists(targetFile))
            {
                IsLinkedFileMissing = true;
                return;
            }

            bool wasMissing = IsLinkedFileMissing;
            IsLinkedFileMissing = false;
            if (wasMissing)
            {
                ReattachLinkedFileWatcher();
            }

            string currentHash = ComputeFileHash(targetFile);
            if (string.IsNullOrEmpty(currentHash))
            {
                // File is locked or currently being written by external process — ignore transient state
                return;
            }

            lock (_linkedFileLock)
            {
                if ((!string.IsNullOrEmpty(_documentSyncedHash) && string.Equals(currentHash, _documentSyncedHash, StringComparison.OrdinalIgnoreCase)) ||
                    (_globalLastSavedHashes.TryGetValue(targetFile, out string? lastSaved) && string.Equals(currentHash, lastSaved, StringComparison.OrdinalIgnoreCase)))
                {
                    return; // File content hasn't changed from what this document is synced with or what Hexprite wrote
                }

                if (LinkedFileChangedExternally || _isShowingChangeDialog)
                    return; // Already marked as changed or dialog is active/queued

                _isShowingChangeDialog = true;
                LinkedFileChangedExternally = true;
            }

            void uiAction()
            {
                try
                {
                    if (!TryBeginSyncDialog())
                    {
                        ShowStatus($"⚠ Linked file '{targetName}' was modified externally — use Pull to sync", 10000);
                        return;
                    }

                    try
                    {
                        bool? result = _dialogService.ShowConfirmation($"The linked source file '{targetName}' was modified by another program.\n\nWould you like to pull the latest changes? (This will overwrite your canvas, but can be undone)", "Source File Changed");
                        if (result == true)
                        {
                            GlobalPullRequested?.Invoke(sender: null, targetFile);
                        }
                        else
                        {
                            ShowStatus("⚠ Source file changed externally — use Pull to fetch changes", 15000);
                        }
                    }
                    finally
                    {
                        EndSyncDialog();
                    }
                }
                finally
                {
                    lock (_linkedFileLock)
                    {
                        _isShowingChangeDialog = false;
                    }
                }
            }

            if (SynchronizationContext.Current == _uiContext || _uiContext == null || _uiContext.GetType() == typeof(SynchronizationContext))
            {
                uiAction();
            }
            else
            {
                _uiContext.Post(_ => uiAction(), state: null);
            }
        }

        /// <summary>
        /// Manually health-checks the linked file for external modifications, missing file status,
        /// and updates sync indicators (called on window activation and tab switching).
        /// </summary>
        public void CheckForExternalChanges()
        {
            lock (_linkedFileLock)
            {
                if (_linkedFileWatcherSuspendCount > 0) return;
            }
            if (!IsLinked || string.IsNullOrEmpty(SpriteState?.LinkedSourceFile)) return;

            string targetFile = SpriteState.LinkedSourceFile;
            if (!File.Exists(targetFile))
            {
                IsLinkedFileMissing = true;
                return;
            }

            bool wasMissing = IsLinkedFileMissing;
            IsLinkedFileMissing = false;
            if (wasMissing)
            {
                ReattachLinkedFileWatcher();
            }

            string currentHash = ComputeFileHash(targetFile, maxRetries: 1);
            if (string.IsNullOrEmpty(currentHash)) return;

            lock (_linkedFileLock)
            {
                if ((!string.IsNullOrEmpty(_documentSyncedHash) && string.Equals(currentHash, _documentSyncedHash, StringComparison.OrdinalIgnoreCase)) ||
                    (_globalLastSavedHashes.TryGetValue(targetFile, out string? lastSaved) && string.Equals(currentHash, lastSaved, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                if (LinkedFileChangedExternally || _isShowingChangeDialog)
                    return;

                _isShowingChangeDialog = true;
                LinkedFileChangedExternally = true;
            }

            string targetName = Path.GetFileName(targetFile) ?? targetFile;
            void uiAction()
            {
                try
                {
                    if (!TryBeginSyncDialog())
                    {
                        ShowStatus($"⚠ Linked file '{targetName}' was modified externally — use Pull to sync", 10000);
                        return;
                    }

                    try
                    {
                        bool? result = _dialogService.ShowConfirmation($"The linked source file '{targetName}' was modified by another program.\n\nWould you like to pull the latest changes? (This will overwrite your canvas, but can be undone)", "Source File Changed");
                        if (result == true)
                        {
                            GlobalPullRequested?.Invoke(sender: null, targetFile);
                        }
                        else
                        {
                            ShowStatus("⚠ Source file changed externally — use Pull to fetch changes", 15000);
                        }
                    }
                    finally
                    {
                        EndSyncDialog();
                    }
                }
                finally
                {
                    lock (_linkedFileLock)
                    {
                        _isShowingChangeDialog = false;
                    }
                }
            }

            if (SynchronizationContext.Current == _uiContext || _uiContext == null || _uiContext.GetType() == typeof(SynchronizationContext))
            {
                uiAction();
            }
            else
            {
                _uiContext.Post(_ => uiAction(), state: null);
            }
        }

        // ── Canvas display helpers ────────────────────────────────────────
        private const double CanvasTargetPx = 400.0;

        public double CellSize
        {
            get => SpriteState != null
                ? Math.Min(CanvasTargetPx / SpriteState.Width, CanvasTargetPx / SpriteState.Height)
                : 25.0;
            set { /* Dummy setter to prevent WPF TwoWay binding crash when FontEditorPanel is collapsed but bound */ }
        }

        public double CanvasDisplayWidth => (SpriteState?.Width ?? 16) * CellSize;
        public double CanvasDisplayHeight => (SpriteState?.Height ?? 16) * CellSize;

        public Rect GridViewport => new(0, 0, CellSize, CellSize);
        /// <summary>
        /// Grid stroke thickness as a constant fraction of cell size (4%).
        /// No minimum floor — this keeps the grid proportionally identical
        /// at every resolution. At very high resolutions the grid naturally
        /// fades as cells become sub-pixel.
        /// </summary>
        public double DynamicStrokeThickness => SpriteState != null
            ? CellSize * 0.04
            : 1.0;

        /// <summary>
        /// Stroke thickness for selection overlays (marquee, lasso) that scales
        /// proportionally with the cell size so it remains visible at all resolutions.
        /// When the entire canvas is selected, the stroke is drawn thicker to prevent
        /// it from becoming hard to see against the canvas borders.
        /// </summary>
        public double SelectionStrokeThickness
        {
            get
            {
                if (SpriteState == null) return 2.0;
                double thickness = Math.Max(0.5, CellSize * 0.08);
                bool isFullCanvas = _selectionService != null && 
                                    _selectionService.HasActiveSelection &&
                                    !_selectionService.IsFloating &&
                                    _selectionService.MinX == 0 &&
                                    _selectionService.MinY == 0 &&
                                    _selectionService.MaxX == SpriteState.Width - 1 &&
                                    _selectionService.MaxY == SpriteState.Height - 1;
                // Add a constant 1.5 pixels in screen space so it doesn't get huge when zoomed in
                return isFullCanvas ? thickness + (1.5 / ZoomLevel) : thickness;
            }
        }

        public void NotifySelectionBoundsChanged()
        {
            OnPropertyChanged(nameof(SelectionStrokeThickness));
        }

        // Keep the sidebar preview usable even for large canvases.
        // Without this, `PreviewWidth/PreviewHeight` become enormous and WPF layout
        // can get very slow / overflow horizontally.
        private const int MaxPreviewDimensionPx = 160;

        private double PreviewScaleEffective
        {
            get
            {
                if (SpriteState == null) return PreviewScale;

                int maxDim = Math.Max(SpriteState.Width, SpriteState.Height);
                if (maxDim <= 0) return PreviewScale;

                double requestedMax = maxDim * (double)PreviewScale;
                if (requestedMax <= MaxPreviewDimensionPx) return PreviewScale;

                // Cap to max dimension, allowing fractional scales for large canvases.
                // Integer scales are preferred for smaller canvases to avoid banding,
                // but large canvases need fractional scaling to fit in the sidebar.
                return MaxPreviewDimensionPx / (double)maxDim;
            }
        }

        public double PreviewEffectiveScale => PreviewScaleEffective;
        public bool IsPreviewScaleCapped => PreviewScaleEffective < (PreviewScale - 0.01);
        public int MaxUsefulPreviewScale
        {
            get
            {
                if (SpriteState == null) return int.MaxValue;
                int maxDim = Math.Max(SpriteState.Width, SpriteState.Height);
                if (maxDim <= 0) return 1;
                // For large canvases that exceed MaxPreviewDimensionPx, only scale 1 is useful
                // since the display will be capped. For smaller canvases, calculate max useful integer scale.
                int maxScale = (int)Math.Floor(MaxPreviewDimensionPx / (double)maxDim);
                return maxScale >= 1 ? maxScale : 1;
            }
        }
        public bool CanIncreasePreviewScale => PreviewScale < MaxUsefulPreviewScale;
        public bool CanDecreasePreviewScale => PreviewScale > 1;
        public string PreviewScaleStatusText => IsPreviewScaleCapped
            ? string.Create(CultureInfo.InvariantCulture, $"Requested {PreviewScale}x, capped to {PreviewScaleEffective:F2}x (preview limit)."
)
            : !CanIncreasePreviewScale
                ? string.Create(CultureInfo.InvariantCulture, $"Max useful scale reached ({MaxUsefulPreviewScale}x) for this preview size."
)
            : "Requested scale is fully applied.";

        public int PreviewWidth =>
            Math.Max(1, (int)Math.Round((SpriteState?.Width ?? 16) * PreviewScaleEffective, MidpointRounding.AwayFromZero));

        public int PreviewHeight =>
            Math.Max(1, (int)Math.Round((SpriteState?.Height ?? 16) * PreviewScaleEffective, MidpointRounding.AwayFromZero));

        // ── Display preview frame (bezel) dimensions ──────────────────────
        // Scale the frame proportionally to the preview size so it shrinks
        // at low zoom and stays slightly thinner than the old fixed values.
        private double PreviewFrameScaleFactor =>
            Math.Clamp(PreviewScaleEffective / 3.5, 0.4, 1.0);

        public CornerRadius PreviewFrameOuterCornerRadius => new(8.0 * PreviewFrameScaleFactor);
        public Thickness PreviewFrameOuterPadding => new(6.0 * PreviewFrameScaleFactor);
        public CornerRadius PreviewFrameInnerCornerRadius => new(5.0 * PreviewFrameScaleFactor);
        public Thickness PreviewFrameInnerPadding => new(4.0 * PreviewFrameScaleFactor);
        public Thickness PreviewFrameInnerBorderThickness => new(Math.Max(0.4, 0.8 * PreviewFrameScaleFactor));
        public CornerRadius PreviewFrameHighlightCornerRadius => new(3.5 * PreviewFrameScaleFactor);
        public double PreviewFrameHighlightHeight => 8.0 * PreviewFrameScaleFactor;
        public Thickness PreviewFrameScreenBorderThickness => new(Math.Max(0.5, 1.5 * PreviewFrameScaleFactor));
        public CornerRadius PreviewFrameScreenCornerRadius => new(2.5 * PreviewFrameScaleFactor);

        private int _previewScale = 2;
        public int PreviewScale
        {
            get => _previewScale;
            set
            {
                int clamped = Math.Clamp(value, 1, Math.Max(1, MaxUsefulPreviewScale));
                if (SetProperty(ref _previewScale, clamped))
                {
                    OnPropertyChanged(nameof(PreviewWidth));
                    OnPropertyChanged(nameof(PreviewHeight));
                    OnPropertyChanged(nameof(PreviewScaleText));
                    OnPropertyChanged(nameof(PreviewEffectiveScale));
                    OnPropertyChanged(nameof(IsPreviewScaleCapped));
                    OnPropertyChanged(nameof(MaxUsefulPreviewScale));
                    OnPropertyChanged(nameof(CanIncreasePreviewScale));
                    OnPropertyChanged(nameof(CanDecreasePreviewScale));
                    OnPropertyChanged(nameof(PreviewScaleStatusText));
                    NotifyPreviewFramePropertiesChanged();
                    EnsurePreviewSimBitmap();
                    UpdatePreviewSimulation();
                    SaveEditorPreferences();
                }
            }
        }

        public string PreviewScaleText =>
            string.Create(CultureInfo.InvariantCulture, $"({PreviewScale}× scale, fit {PreviewScaleEffective:F2}×)");

        private void NotifyPreviewFramePropertiesChanged()
        {
            OnPropertyChanged(nameof(PreviewFrameOuterCornerRadius));
            OnPropertyChanged(nameof(PreviewFrameOuterPadding));
            OnPropertyChanged(nameof(PreviewFrameInnerCornerRadius));
            OnPropertyChanged(nameof(PreviewFrameInnerPadding));
            OnPropertyChanged(nameof(PreviewFrameInnerBorderThickness));
            OnPropertyChanged(nameof(PreviewFrameHighlightCornerRadius));
            OnPropertyChanged(nameof(PreviewFrameHighlightHeight));
            OnPropertyChanged(nameof(PreviewFrameScreenBorderThickness));
            OnPropertyChanged(nameof(PreviewFrameScreenCornerRadius));
        }

        private DisplayType _previewDisplayType = DisplayType.GenericWhite;
        public int PreviewDisplayTypeIndex
        {
            get => (int)_previewDisplayType;
            set
            {
                if (_previewDisplayType != (DisplayType)value)
                {
                    _previewDisplayType = (DisplayType)value;
                    OnPropertyChanged();
                    RefreshCanvasColors();
                    UpdatePreviewSimulation();
                    SaveEditorPreferences();
                }
            }
        }

        private bool _useRealisticPreview;
        public bool UseRealisticPreview
        {
            get => _useRealisticPreview;
            set
            {
                if (SetProperty(ref _useRealisticPreview, value))
                {
                    OnPropertyChanged(nameof(DisplayPreviewBitmap));
                    UpdatePreviewSimulation();
                    SaveEditorPreferences();
                }
            }
        }

        private int _previewRealismStrength = 65;
        public int PreviewRealismStrength
        {
            get => _previewRealismStrength;
            set
            {
                if (SetProperty(ref _previewRealismStrength, Math.Clamp(value, 0, 100)))
                {
                    UpdatePreviewSimulation();
                    SaveEditorPreferences();
                }
            }
        }

        private PreviewQuality _previewQuality = PreviewQuality.Balanced;
        public int PreviewQualityIndex
        {
            get => (int)_previewQuality;
            set
            {
                var v = (PreviewQuality)Math.Clamp(value, 0, 2);
                if (_previewQuality != v)
                {
                    _previewQuality = v;
                    OnPropertyChanged();
                    UpdatePreviewSimulation();
                    SaveEditorPreferences();
                }
            }
        }

        // ── WritableBitmaps ───────────────────────────────────────────────
        private WriteableBitmap _canvasBitmap = null!;
        public WriteableBitmap CanvasBitmap
        {
            get => _canvasBitmap;
            set => SetProperty(ref _canvasBitmap, value);
        }

        private WriteableBitmap _previewBitmap = null!;
        public WriteableBitmap PreviewBitmap
        {
            get => _previewBitmap;
            set => SetProperty(ref _previewBitmap, value);
        }

        private WriteableBitmap? _previewSimBitmap;
        public WriteableBitmap PreviewSimBitmap
        {
            get
            {
                _previewSimBitmap ??= new WriteableBitmap(
                    Math.Max(1, PreviewWidth),
                    Math.Max(1, PreviewHeight),
                    96, 96,
                    PixelFormats.Bgra32,
                    palette: null);
                return _previewSimBitmap;
            }
            private set => SetProperty(ref _previewSimBitmap, value);
        }

        public ImageSource DisplayPreviewBitmap => IsPlaying ? PreviewSimBitmap : PreviewBitmap;

        private uint[] _canvasBuffer = [];
        private uint[] _previewBuffer = [];
        private bool[] _hardwareBuffer = [];
        private uint[] _previewSimBuffer = [];
        private long _lastPreviewSimulationTicks;
        private const long PreviewSimulationMinIntervalTicks = TimeSpan.TicksPerMillisecond * 33;
        private bool _isStrokeRenderingActive;
        private bool _pendingPreviewSimulationAfterStroke;

#if DEBUG
        private readonly DrawPerfCollector _drawPerf = new();
#endif

        private uint _colorOffUint, _colorOnUint, _previewOffUint, _previewOnUint;
        /// <summary>
        /// Packs a WPF <see cref="Color"/> into a 32-bit value suitable for
        /// <see cref="PixelFormats.Bgra32"/> <see cref="WriteableBitmap"/> buffers.
        /// </summary>
        /// <remarks>
        /// The formula (A&lt;&lt;24)|(R&lt;&lt;16)|(G&lt;&lt;8)|B looks like ARGB when read as
        /// an integer, but on little-endian x86/x64 the CPU writes the
        /// <em>least-significant byte first</em>, so the in-memory layout is:
        ///   address+0 = B, +1 = G, +2 = R, +3 = A — exactly BGRA32.
        /// Do NOT reorder the channels; the formula is correct as written.
        /// </remarks>
        private static uint ToBgra32(Color c) =>
            (uint)((c.A << 24) | (c.R << 16) | (c.G << 8) | c.B);

        // ── IBitmapBufferContext implementation ────────────────────────────
        // Exposes bitmap buffer data through a narrow interface so
        // BitmapPreviewRenderer doesn't need to depend on the full ViewModel.
        uint[] IBitmapBufferContext.CanvasBuffer => _canvasBuffer;
        uint[] IBitmapBufferContext.PreviewBuffer => _previewBuffer;
        uint IBitmapBufferContext.ColorOnUint => _colorOnUint;
        uint IBitmapBufferContext.ColorOffUint => _colorOffUint;
        uint IBitmapBufferContext.PreviewOnUint => _previewOnUint;
        uint IBitmapBufferContext.PreviewOffUint => _previewOffUint;
        ISelectionService IBitmapBufferContext.SelectionService => _selectionService;
        void IBitmapBufferContext.UpdatePreviewSimulation() => UpdatePreviewSimulation();

        // ── Extracted subsystems ──────────────────────────────────────────
        // These are initialized via InitializeControllers after construction
        // because they require the MainViewModel instance itself.
        private IToolInputController _toolInput = null!;
        private BitmapPreviewRenderer _previewRenderer = null!;
        private ISelectionInputController _selectionInput = null!;

        // ── Events ────────────────────────────────────────────────────────
        /// <summary>
        /// Raised after Undo/Redo so the View can clear any in-progress selection
        /// overlays that may now be invalid.
        /// </summary>
        public event EventHandler? HistoryRestored;

        /// <summary>
        /// Raised after the tool changes so the View can update UI-only concerns
        /// (brush cursor visibility, cursor shape). Not a domain event.
        /// </summary>
        public event EventHandler? ToolChanged;

        /// <summary>
        /// Notifies this document that the global tool has changed.
        /// Called by ShellViewModel when CurrentTool changes.
        /// </summary>
        public void NotifyToolChanged()
        {
            OnPropertyChanged(nameof(CurrentTool));
            OnPropertyChanged(nameof(BrushSize));
            OnPropertyChanged(nameof(BrushShape));
            OnPropertyChanged(nameof(BrushAngle));
            OnPropertyChanged(nameof(IsPixelPerfectEnabled));
            OnPropertyChanged(nameof(IsContiguousFillEnabled));
            OnPropertyChanged(nameof(IsPixelPerfectAvailable));
            OnPropertyChanged(nameof(IsSymmetryVisible));
            ToolChanged?.Invoke(this, EventArgs.Empty);
        }

        // ── Animation commands ─────────────────────────────────────────────
        public IRelayCommand ToggleAnimationCommand { get; }
        public IRelayCommand TogglePlaybackCommand { get; }
        public IRelayCommand ToggleOnionSkinCommand { get; }
        public IRelayCommand AddFrameCommand { get; }
        public IRelayCommand<FrameItemViewModel?> DuplicateFrameCommand { get; }
        public IRelayCommand<FrameItemViewModel?> DeleteFrameCommand { get; }
        public IRelayCommand NextFrameCommand { get; }
        public IRelayCommand PreviousFrameCommand { get; }
        public IRelayCommand ClearFrameCommand { get; }
        public IRelayCommand<FrameBatchOperation> BatchFrameOperationCommand { get; }
        public IRelayCommand SelectAllFramesCommand { get; }
        public IRelayCommand SelectActiveFrameOnlyCommand { get; }

        // ── Linked Source commands ───────────────────────────────────────
        public IRelayCommand UpdateLinkedSourceCommand { get; }
        public IRelayCommand PullLinkedSourceCommand { get; }
        public IRelayCommand RestoreLinkedSourceCommand { get; }
        public IRelayCommand UnlinkSourceCommand { get; }
        public IRelayCommand RelinkSourceCommand { get; }

        // ── Constructor ───────────────────────────────────────────────────
        private void HandleGlobalPullRequested(object? sender, string targetFile)
        {
            if (sender == this) return;
            if (IsLinked && string.Equals(SpriteState?.LinkedSourceFile, targetFile, StringComparison.OrdinalIgnoreCase))
            {
                _uiContext.Post(async _ => await ExecutePullLinkedSourceAsync(), state: null);
            }
        }

        public MainViewModel(
            ICodeGeneratorService codeGen,
            IDrawingService drawingService,
            IHistoryService historyService,
            ISelectionService selectionService,
            IClipboardService clipboardService,
            IPixelClipboardService pixelClipboard,
            IDialogService dialogService,
            IExportService exportService,
            IFileImportExportService importExportService,
            IHardwarePreviewService hardwarePreview,
            IAutosaveService autosaveService,
            Func<ToolMode> getCurrentTool,
            Action<ToolMode> setCurrentTool,
            bool applyToolPreferences)
        {
            _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();
            _codeGen = codeGen ?? throw new ArgumentNullException(nameof(codeGen));
            _drawingService = drawingService ?? throw new ArgumentNullException(nameof(drawingService));
            _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
            _selectionService = selectionService ?? throw new ArgumentNullException(nameof(selectionService));
            _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
            _pixelClipboard = pixelClipboard ?? throw new ArgumentNullException(nameof(pixelClipboard));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
            _importExportService = importExportService ?? throw new ArgumentNullException(nameof(importExportService));
            _hardwarePreview = hardwarePreview ?? throw new ArgumentNullException(nameof(hardwarePreview));
            _autosaveService = autosaveService ?? throw new ArgumentNullException(nameof(autosaveService));
            
            string documentId = Guid.NewGuid().ToString();
            _autosaveService.StartAutosaveLoop(
                documentId,
                () =>
                {
                    if (SpriteState == null) return null;
                    if (_exportSettings != null)
                    {
                        SpriteState.ExportSettings = _exportSettings.Clone();
                    }
                    return SpriteState;
                },
                () => IsDirty,
                () => new AutosaveMetadata
                {
                    Title = Title,
                    FilePath = FilePath,
                    IsActiveTab = IsActive,
                    ParentPackPath = ParentPackPath,
                    ParentPackName = ParentPackName,
                    PackEntryName = PackEntryName,
                });

            _hwPreviewEnabledHandler = (s, e) =>
            {
                _uiContext.Post(_ =>
                {
                    OnPropertyChanged(nameof(IsHardwarePreviewEnabled));
                    OnPropertyChanged(nameof(HardwarePreviewConnectionButtonText));
                    if (!e)
                    {
                        // Service auto-disabled due to an error — show why
                        string reason = _hardwarePreview.LastDisableReason ?? "Unknown error";
                        ShowStatus($"⚠ Hardware preview disabled: {reason}", 5000);
                    }
                }, state: null);
            };
            _hardwarePreview.EnabledChanged += _hwPreviewEnabledHandler;

            _hwPreviewConnectionStateHandler = (s, e) =>
            {
                _uiContext.Post(_ =>
                {
                    OnPropertyChanged(nameof(HardwarePreviewStatusText));
                    OnPropertyChanged(nameof(HardwarePreviewStatusBrush));
                    OnPropertyChanged(nameof(HardwarePreviewConnectionButtonText));
                    OnPropertyChanged(nameof(IsHardwarePreviewError));
                    OnPropertyChanged(nameof(TroubleshootingGuide));
                }, state: null);
            };
            _hardwarePreview.ConnectionStateChanged += _hwPreviewConnectionStateHandler;

            GlobalPullRequested += HandleGlobalPullRequested;

            _getCurrentTool = getCurrentTool ?? throw new ArgumentNullException(nameof(getCurrentTool));
            _setCurrentTool = setCurrentTool ?? throw new ArgumentNullException(nameof(setCurrentTool));

            // ── Hardware Preview Properties ─────────────────────────────────
            // Populate instantly with bare port names (fast, synchronous), then upgrade to
            // friendly device names in the background — the WMI lookup that provides those
            // names is too slow to do on the UI thread, especially on the 2s auto-refresh tick.
            AvailablePorts = new ObservableCollection<HardwarePreviewPortOption>(
                _hardwarePreview.GetAvailablePorts().Select(p => new HardwarePreviewPortOption(p, p)));
            RefreshPortsCommand = new AsyncRelayCommand(RefreshPortsAsync);
            _ = RefreshPortsCommand.ExecuteAsync(parameter: null);

            _portAutoRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(2),
            };
            _portAutoRefreshTimer.Tick += PortAutoRefreshTimer_Tick;
            _portAutoRefreshTimer.Start();

            ToggleHardwarePreviewConnectionCommand = new RelayCommand(() =>
            {
                if (_hardwarePreview.ConnectionState == HardwarePreviewConnectionState.Error)
                {
                    _hardwarePreview.IsEnabled = false;
                    IsHardwarePreviewEnabled = true;
                }
                else
                {
                    IsHardwarePreviewEnabled = !IsHardwarePreviewEnabled;
                }
            });

            ToggleTroubleshootingCommand = new RelayCommand(() =>
            {
                IsTroubleshootingVisible = !IsTroubleshootingVisible;
            });

            OpenSketchFolderCommand = new RelayCommand(() =>
            {
                try
                {
                    string? path = AssetsPathService.ResolveHexpritePreviewLibraryPath();
                    if (path != null)
                    {
                        string explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                        Process.Start(new ProcessStartInfo(explorerPath, path) { UseShellExecute = true });
                    }
                    else
                    {
                        _dialogService.ShowMessage(
                            "Could not locate HexpritePreview library files.\n" +
                            "Reinstall Hexprite or copy HexpritePreview from the GitHub repo into your project's lib/ folder.");
                    }
                }
                catch (Exception ex)
                {
                    HandledErrorReporter.Error(ex, "MainViewModel.OpenSketchFolder");
                }
            });

            OpenStandaloneSketchCommand = new RelayCommand(() =>
            {
                try
                {
                    // Ensure the sketch file reflects current configuration before opening
                    if (HardwarePreviewWiringConfig != null)
                    {
                        HardwarePreviewSketchGenerator.UpdateStandaloneSketchInAppData(HardwarePreviewWiringConfig, HardwarePreviewBaudRate);
                    }

                    string? path = AssetsPathService.ResolveHexpritePreviewStandalonePath();
                    if (path != null)
                    {
                        string explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                        Process.Start(new ProcessStartInfo(explorerPath, path) { UseShellExecute = true });
                    }
                    else
                    {
                        _dialogService.ShowMessage(
                            "Could not locate the standalone HexpritePreview sketch.\n" +
                            "Reinstall Hexprite or download HexpritePreview-Standalone-Arduino from the GitHub repo.");
                    }
                }
                catch (Exception ex)
                {
                    HandledErrorReporter.Error(ex, "MainViewModel.OpenStandaloneSketch");
                }
            });

            OpenInArduinoIdeCommand = new RelayCommand(() =>
            {
                try
                {
                    if (HardwarePreviewWiringConfig != null)
                    {
                        HardwarePreviewSketchGenerator.UpdateStandaloneSketchInAppData(HardwarePreviewWiringConfig, HardwarePreviewBaudRate);
                    }
                    string? path = AssetsPathService.ResolveHexpritePreviewStandalonePath();
                    if (path != null)
                    {
                        string inoFile = Path.Combine(path, AssetsPathService.StandaloneSketchFileName);
                        if (File.Exists(inoFile))
                        {
                            Process.Start(new ProcessStartInfo(inoFile) { UseShellExecute = true });
                            ShowStatus("✓ Opening Arduino sketch in IDE...", 4000);
                        }
                        else
                        {
                            string explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                            Process.Start(new ProcessStartInfo(explorerPath, path) { UseShellExecute = true });
                        }
                    }
                }
                catch (Exception ex)
                {
                    HandledErrorReporter.Error(ex, "MainViewModel.OpenInArduinoIde");
                }
            });

            OpenPlatformIOCommand = new RelayCommand(() =>
            {
                try
                {
                    if (HardwarePreviewWiringConfig != null)
                    {
                        HardwarePreviewSketchGenerator.UpdatePlatformIOConfigInAppData(HardwarePreviewWiringConfig, HardwarePreviewBaudRate);
                    }
                    string? path = AssetsPathService.ResolveHexpritePreviewPlatformIOPath();
                    if (path != null)
                    {
                        string explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                        Process.Start(new ProcessStartInfo(explorerPath, path) { UseShellExecute = true });
                        ShowStatus("✓ PlatformIO project folder opened", 4000);
                    }
                }
                catch (Exception ex)
                {
                    HandledErrorReporter.Error(ex, "MainViewModel.OpenPlatformIO");
                }
            });

            var prefs = UserPreferencesService.Get();
            HardwarePreviewWiringConfig = new HardwarePreviewWiringConfig
            {
                BoardPreset = prefs.HardwarePreviewBoardPreset,
                InterfaceType = prefs.HardwarePreviewInterfaceType,
                DisplayModel = prefs.HardwarePreviewDisplayModel,
                SdaPin = prefs.HardwarePreviewSdaPin,
                SclPin = prefs.HardwarePreviewSclPin,
                I2cAddress = prefs.HardwarePreviewI2cAddress,
                UseSoftwareI2c = prefs.HardwarePreviewUseSoftwareI2c,
                CsPin = prefs.HardwarePreviewCsPin,
                DcPin = prefs.HardwarePreviewDcPin,
                RstPin = prefs.HardwarePreviewRstPin,
                ClkPin = prefs.HardwarePreviewClkPin,
                MosiPin = prefs.HardwarePreviewMosiPin,
            };

            if (Enum.TryParse<HardwarePreviewPlacement>(prefs.HardwarePreviewPlacement, out var placement))
            {
                _hardwarePreview.Placement = placement;
            }
            if (Enum.TryParse<HardwarePreviewScale>(prefs.HardwarePreviewScale, out var scale))
            {
                _hardwarePreview.Scale = scale;
            }
            _hardwarePreview.TargetDisplaySize = HardwarePreviewWiringConfig.GetDisplayDimensions(HardwarePreviewWiringConfig.DisplayModel);

            if (prefs.HardwarePreviewBaudRate > 0)
            {
                _hardwarePreview.BaudRate = prefs.HardwarePreviewBaudRate;
            }
            if (!string.IsNullOrEmpty(prefs.HardwarePreviewPort))
            {
                var availablePorts = _hardwarePreview.GetAvailablePorts();
                if (availablePorts.Contains(prefs.HardwarePreviewPort, StringComparer.OrdinalIgnoreCase))
                {
                    _hardwarePreview.PortName = prefs.HardwarePreviewPort;
                    if (prefs.HardwarePreviewAutoConnect)
                    {
                        IsHardwarePreviewEnabled = true;
                    }
                }
            }
            else if (AvailablePorts.Count == 1)
            {
                _hardwarePreview.PortName = AvailablePorts[0].PortName;
            }

            ConfigureHardwarePreviewWiringCommand = new RelayCommand(() =>
            {
                try
                {
                    int w = SpriteState?.Width ?? 0;
                    int h = SpriteState?.Height ?? 0;
                    bool applied = _dialogService.ShowHardwarePreviewWiringDialog(HardwarePreviewWiringConfig, HardwarePreviewBaudRate, w, h, _hardwarePreview);
                    if (applied)
                    {
                        if (HardwarePreviewWiringConfig.BaudRate > 0 && HardwarePreviewWiringConfig.BaudRate != HardwarePreviewBaudRate)
                        {
                            HardwarePreviewBaudRate = HardwarePreviewWiringConfig.BaudRate;
                        }
                        _hardwarePreview.TargetDisplaySize = HardwarePreviewWiringConfig.GetDisplayDimensions(HardwarePreviewWiringConfig.DisplayModel);
                        OnPropertyChanged(nameof(TargetDisplayInfo));
                        NotifyHardwarePreviewWiringChanged();
                        if (IsHardwarePreviewEnabled)
                        {
                            TriggerHardwarePreviewUpdate();
                        }
                        ShowStatus($"✓ Hardware preview configured for {HardwarePreviewWiringConfig.BoardPreset} — re-upload sketch to apply pin changes", 8000);
                    }
                }
                catch (Exception ex)
                {
                    HandledErrorReporter.Error(ex, "MainViewModel.ConfigureHardwarePreviewWiring");
                }
            });

            OpenPinoutGuideCommand = new RelayCommand(() =>
            {
                ConfigureHardwarePreviewWiringCommand.Execute(parameter: null);
            });

            AutoDetectBaudRateCommand = new AsyncRelayCommand(AutoDetectBaudRateAsync);

            // ── Controllers are initialized via InitializeControllers after construction
            // since they require the MainViewModel instance itself
            _toolInput = null!;
            _selectionInput = null!;
            _previewRenderer = null!;

            // ── Animation initialization ─────────────────────────────────────
            _playbackTimer = new DispatcherTimer(DispatcherPriority.Render);
            _playbackTimer.Tick += PlaybackTimer_Tick;
            UpdatePlaybackTimerInterval();

            ApplySavedEditorPreferences(applyToolPreferences);

            // ── Commands ──────────────────────────────────────────────────

            CopyExportedCodeCommand = new RelayCommand(() =>
            {
                _clipboardService.SetText(ExportedCode);
                ShowStatus("✓ Copied to clipboard");
            });

            CopyExportedSketchCommand = new RelayCommand(async () =>
            {
                try
                {
                    int w = SpriteState.Width;
                    int h = SpriteState.Height;
                    var settingsSnapshot = ExportSettings.Clone();
                    settingsSnapshot.GenerateFullSketch = true;
                    var frames = GetExportFrames(settingsSnapshot.ExportAsAnimation);
                    var delays = settingsSnapshot.ExportAsAnimation ? SpriteState.Frames.Select(f => f.DelayMultiplier).ToList() : null;
                    string sketch = await _codeGen.GenerateSketchAsync(frames, w, h, settingsSnapshot, isFloating: false,
                                                                       floatingPixels: null, 0, 0, 0, 0,
                                                                       _floatingPasteMode, delays, CancellationToken.None);
                    _clipboardService.SetText(sketch);
                    ShowStatus("✓ Sketch copied to clipboard");
                }
                catch (Exception ex)
                {
                    HandledErrorReporter.Error(ex, "MainViewModel.CopyExportedSketch");
                    ShowStatus("⚠ Could not generate sketch");
                }
            });

            ExportArduinoSketchFolderCommand = new AsyncRelayCommand(ExecuteExportArduinoSketchFolderAsync);

            GenerateCodeCommand = new RelayCommand(() =>
            {
                UpdateTextOutputs();
            });

            ExportImageCommand = new RelayCommand(ExecuteExportImage);

            UpdateLinkedSourceCommand = new RelayCommand(async () => await ExecuteUpdateLinkedSourceAsync(), () => IsLinked && !IsLinkedFileMissing);

            PullLinkedSourceCommand = new RelayCommand(async () => await ExecutePullLinkedSourceAsync(), () => IsLinked && !IsLinkedFileMissing);

            RestoreLinkedSourceCommand = new RelayCommand(async () =>
            {
                await ExecuteRestoreLinkedSourceAsync(skipConfirmation: false);
            }, () => IsLinked);

            UnlinkSourceCommand = new RelayCommand(() =>
            {
                if (SpriteState == null || !SpriteState.IsLinked) return;
                
                SaveStateForUndo();

                SpriteState.LinkedSourceFile = null;
                SpriteState.LinkedVariableName = null;
                SpriteState.LinkedFormat = null;
                
                NotifyLinkChanged();
                ShowStatus("✓ Unlinked from source file");
            }, () => IsLinked);

            RelinkSourceCommand = new RelayCommand(() =>
            {
                if (SpriteState == null) return;
                
                var result = _dialogService.ShowImportFromFileDialog();
                if (result == null) return;

                var (filePath, selectedSprites) = result.Value;
                if (selectedSprites.Count > 0)
                {
                    SaveStateForUndo();

                    var sprite = selectedSprites[0];
                    
                    SpriteState.LinkedSourceFile = filePath;
                    SpriteState.LinkedVariableName = sprite.Name;
                    SpriteState.LinkedFormat = sprite.Format;
                    
                    NotifyLinkChanged();
                    
                    bool pullResult = _dialogService.ShowConfirmation($"Would you like to pull the current data from '{LinkedSourceFileName}' to update the canvas?", "Pull Linked Source");
                    if (pullResult)
                    {
                        if (sprite.Width != SpriteState.Width || sprite.Height != SpriteState.Height)
                            ResizeCanvas(sprite.Width, sprite.Height, ResizeAnchor.TopLeft);

                        var backupState = SpriteState.Clone();
                        try
                        {
                            ParseCodeToState(_codeGen, sprite.Format, sprite.CodeSnippet, SpriteState);
                            SpriteState.NormalizeLayerState();
                        }
                        catch
                        {
                            RestoreState(backupState);
                            throw;
                        }
                        
                        ReloadLayersFromState();
                        RebuildFrameViewModels();
                        IsAnimationEnabled = SpriteState.IsAnimationEnabled || (SpriteState.Frames != null && SpriteState.Frames.Count > 1);
                        RedrawGridFromMemory();
                        MarkCodeStale();
                        UpdateTextOutputs();
                        MarkAsClean();
                    }

                    ShowStatus($"✓ Relinked to {LinkedSourceFileName}");
                }
            });

            UndoCommand = new RelayCommand(() => 
            {
                if (IsTextEditing)
                {
                    bool hadContent = _textToolUndoSaved;
                    IsTextEditing = false;
                    _textToolUndoSaved = false;
                    if (_selectionService.IsFloating)
                    {
                        _selectionService.Cancel();
                        RedrawGridFromMemory();
                    }
                    // No text was typed → no undo entry was pushed, just cancel text mode
                    if (!hadContent) return;
                }

                CancelInProgressDrawing();
                // Do not cancel selection transform here, otherwise we lose the current state for Redo
                // if (_selectionService.IsTransforming) CancelSelectionTransformIfActive();
                if (_selectionService.IsSelecting) _selectionService.Cancel();

                SpriteState.SelectionSnapshot = _selectionService.CreateSnapshot();
                RestoreState(_historyService.Undo(SpriteState));
                UndoCommand?.NotifyCanExecuteChanged();
                RedoCommand?.NotifyCanExecuteChanged();
            }, () => CanUndo);
            RedoCommand = new RelayCommand(() => 
            {
                if (IsTextEditing)
                {
                    IsTextEditing = false;
                    _textToolUndoSaved = false;
                    if (_selectionService.IsFloating)
                    {
                        _selectionService.Cancel();
                        RedrawGridFromMemory();
                    }
                }

                CancelInProgressDrawing();
                // Do not cancel selection transform here, otherwise we lose the current state for Undo
                // if (_selectionService.IsTransforming) CancelSelectionTransformIfActive();
                if (_selectionService.IsSelecting) _selectionService.Cancel();

                SpriteState.SelectionSnapshot = _selectionService.CreateSnapshot();
                RestoreState(_historyService.Redo(SpriteState));
                UndoCommand?.NotifyCanExecuteChanged();
                RedoCommand?.NotifyCanExecuteChanged();
            }, () => CanRedo);

            ClearCommand = new RelayCommand(() =>
            {
                StopTextEditing();
                if (!CanModifyActiveLayer)
                {
                    ShowStatus("Cannot clear: layer is locked, hidden, or multiple layers selected.", 3000);
                    return;
                }
                SaveStateForUndo();
                // Drop floating layer explicitly (user intent is clear canvas = clear everything)
                if (_selectionService.HasActiveSelection)
                    _selectionService.Cancel();
                Array.Clear(SpriteState.Pixels, 0, SpriteState.Pixels.Length);
                RedrawGridFromMemory();
                MarkCodeStale();
            },
            () => CanModifyActiveLayer);

            InvertCommand = new RelayCommand(
            () =>
            {
                StopTextEditing();
                SaveStateForUndo();
                if (_selectionService.IsFloating && _selectionService.FloatingPixels != null)
                {
                    int w = _selectionService.FloatingWidth;
                    int h = _selectionService.FloatingHeight;
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                            _selectionService.FloatingPixels[x, y] = !_selectionService.FloatingPixels[x, y];
                }
                else if (_selectionService.HasActiveSelection)
                {
                    int w = SpriteState.Width;
                    for (int i = 0; i < SpriteState.Pixels.Length; i++)
                    {
                        int x = i % w;
                        int y = i / w;
                        if (_selectionService.IsPixelInSelection(x, y))
                            SpriteState.Pixels[i] = !SpriteState.Pixels[i];
                    }
                }
                else
                {
                    _drawingService.InvertGrid(SpriteState);
                }
                RedrawGridFromMemory();
                MarkCodeStale();
            },
            () => CanModifyActiveLayer);

            OutlineCommand = new RelayCommand(
            () =>
            {
                StopTextEditing();

                bool isFloating = _selectionService.IsFloating && _selectionService.FloatingPixels != null;

                // Snapshot the original pixels so we can restore on each preview tick and on cancel
                bool[] originalPixels;
                bool[,]? originalFloating = null;
                int floatW = 0, floatH = 0;
                IPixelClip? clip = null;

                if (isFloating)
                {
                    floatW = _selectionService.FloatingWidth;
                    floatH = _selectionService.FloatingHeight;
                    originalFloating = (bool[,])_selectionService.FloatingPixels!.Clone();
                    originalPixels = []; // not used for floating path
                }
                else
                {
                    originalPixels = (bool[])SpriteState.Pixels.Clone();
                    clip = _selectionService.HasActiveSelection ? new SelectionClipAdapter(_selectionService) : null;
                }

                // Preview callback: restore originals, apply outline with new settings, redraw
                void PreviewOutline(OutlineSettings previewSettings)
                {
                    if (isFloating && originalFloating != null)
                    {
                        // Restore floating pixels to original
                        bool[] flat = new bool[floatW * floatH];
                        for (int fy = 0; fy < floatH; fy++)
                            for (int fx = 0; fx < floatW; fx++)
                                flat[fy * floatW + fx] = originalFloating[fx, fy];

                        var tempState = new SpriteState(floatW, floatH);
                        Array.Copy(flat, tempState.Pixels, flat.Length);
                        _drawingService.OutlineLayer(tempState, previewSettings);

                        bool[,] previewPixels = new bool[floatW, floatH];
                        for (int fy = 0; fy < floatH; fy++)
                            for (int fx = 0; fx < floatW; fx++)
                                previewPixels[fx, fy] = tempState.Pixels[fy * floatW + fx];
                        _selectionService.FloatingPixels = previewPixels;
                    }
                    else
                    {
                        // Restore layer pixels to original, then apply outline
                        Array.Copy(originalPixels, SpriteState.Pixels, originalPixels.Length);
                        _drawingService.OutlineLayer(SpriteState, previewSettings, clip);
                    }
                    RedrawGridFromMemory();
                }

                var settings = _dialogService.ShowOutlineDialog(PreviewOutline);

                if (settings != null)
                {
                    // User confirmed — push undo BEFORE the change, then apply final result
                    // First restore originals so undo captures the pre-outline state
                    if (isFloating && originalFloating != null)
                    {
                        _selectionService.FloatingPixels = (bool[,])originalFloating.Clone();
                    }
                    else
                    {
                        Array.Copy(originalPixels, SpriteState.Pixels, originalPixels.Length);
                    }

                    SaveStateForUndo();

                    // Now apply the final outline
                    if (isFloating && originalFloating != null)
                    {
                        bool[] flat = new bool[floatW * floatH];
                        for (int fy = 0; fy < floatH; fy++)
                            for (int fx = 0; fx < floatW; fx++)
                                flat[fy * floatW + fx] = originalFloating[fx, fy];

                        var tempState = new SpriteState(floatW, floatH);
                        Array.Copy(flat, tempState.Pixels, flat.Length);
                        _drawingService.OutlineLayer(tempState, settings);

                        bool[,] finalPixels = new bool[floatW, floatH];
                        for (int fy = 0; fy < floatH; fy++)
                            for (int fx = 0; fx < floatW; fx++)
                                finalPixels[fx, fy] = tempState.Pixels[fy * floatW + fx];
                        _selectionService.FloatingPixels = finalPixels;
                    }
                    else
                    {
                        _drawingService.OutlineLayer(SpriteState, settings, clip);
                    }
                    RedrawGridFromMemory();
                    MarkCodeStale();
                }
                else
                {
                    // User cancelled — restore original pixels
                    if (isFloating && originalFloating != null)
                    {
                        _selectionService.FloatingPixels = originalFloating;
                    }
                    else
                    {
                        Array.Copy(originalPixels, SpriteState.Pixels, originalPixels.Length);
                    }
                    RedrawGridFromMemory();
                }
            },
            () => CanModifyActiveLayer);

            CopySelectionCommand = new RelayCommand(() =>
            {
                if (!_selectionService.HasActiveSelection) return;
                var data = _selectionService.CopySelection(SpriteState);
                if (data != null)
                {
                    _pixelClipboard.Store(data);
                    ShowStatus("Selection copied");
                }
            });

            CutSelectionCommand = new RelayCommand(
            () =>
            {
                StopTextEditing();
                if (!_selectionService.HasActiveSelection) return;
                var data = _selectionService.CopySelection(SpriteState);
                if (data != null)
                {
                    _pixelClipboard.Store(data);
                    SaveStateForUndo();
                    _selectionService.DeleteSelection(SpriteState);
                    RedrawGridFromMemory();
                    MarkCodeStale();
                    ShowStatus("Selection cut");
                }
            },
            () => _selectionService.HasActiveSelection && CanModifyActiveLayer);

            PasteCommand = new RelayCommand(
            () =>
            {
                StopTextEditing();
                if (!_pixelClipboard.HasData || _pixelClipboard.Data == null) return;

                SaveStateForUndo(); // Push state BEFORE flattening the current floating layer
                _selectionInput.CommitIfActive(saveHistory: false);

                if (_pixelClipboard.Data.Width > SpriteState.Width || _pixelClipboard.Data.Height > SpriteState.Height)
                {
                    SetLayerOverflow(SpriteState.ActiveLayerIndex, value: true);
                }

                _selectionService.PasteAsFloating(
                    _pixelClipboard.Data,
                    SpriteState.Width,
                    SpriteState.Height);
                RedrawGridFromMemory();
                ShowStatus("Pasted from clipboard");
            },
            () => _pixelClipboard.HasData && _pixelClipboard.Data != null && CanModifyActiveLayer);

            DeleteSelectionCommand = new RelayCommand(
            () =>
            {
                StopTextEditing();
                if (!_selectionService.HasActiveSelection) return;

                _selectionInput.ResetControllerState();

                // SaveStateForUndo only captures SpriteState.Pixels, not FloatingPixels.
                // This correctly matches Aseprite behavior: when undoing a deletion of a floating
                // selection, it discards the floating pixels and restores the hole from the original lift.
                SaveStateForUndo();

                _selectionService.DeleteSelection(SpriteState);
                RedrawGridFromMemory();
                MarkCodeStale();
            },
            () => _selectionService.HasActiveSelection && CanModifyActiveLayer);

            DeselectCommand = new RelayCommand(() =>
            {
                StopTextEditing();
                _selectionInput.CommitIfActive();
                _selectionInput.ResetControllerState();
                _selectionService.Cancel();
            });

            SelectAllCommand = new RelayCommand(() =>
            {
                StopTextEditing();
                if (SpriteState == null || SpriteState.Width <= 0 || SpriteState.Height <= 0) return;

                // Preserve any "lifted" (floating) pixels by committing before replacing
                // the selection with the whole-canvas marquee.
                _selectionInput.CommitIfActive();
                _selectionInput.ResetControllerState();
                _selectionService.Cancel();

                int w = SpriteState.Width;
                int h = SpriteState.Height;

                _selectionService.BeginRectangleSelection(0, 0, SelectionMode.Replace);
                _selectionService.UpdateRectangleSelection(w - 1, h - 1);
                _selectionService.FinalizeSelection();
            });

            ReselectCommand = new RelayCommand(() =>
            {
                StopTextEditing();
                if (!_selectionService.CanReselect) return;
                _selectionInput.CommitIfActive();
                _selectionInput.ResetControllerState();
                _selectionService.Reselect();
                RedrawGridFromMemory();
                ShowStatus("Selection restored");
            }, () => _selectionService.CanReselect);

            NewLayerFromSelectionCommand = new RelayCommand(
                CreateNewLayerFromSelection,
                () => _selectionService.HasActiveSelection &&
                      _selectionService.HasAnyPixelInSelection(SpriteState) &&
                      CanModifyActiveLayer);

            SelectToolCommand = new RelayCommand<string>(ExecuteSelectTool);
            IncreasePreviewScaleCommand = new RelayCommand(() =>
            {
                if (CanIncreasePreviewScale) PreviewScale++;
            });
            DecreasePreviewScaleCommand = new RelayCommand(() =>
            {
                if (CanDecreasePreviewScale) PreviewScale--;
            });
            AddLayerCommand = new RelayCommand(AddLayer);
            DeleteLayerCommand = new RelayCommand(DeleteSelectedLayers, () => CanDeleteSelectedLayers);
            DuplicateLayerCommand = new RelayCommand(DuplicateActiveLayer, () => CanDuplicateActiveLayer);
            MergeLayerCommand = new RelayCommand(MergeLayers, () => CanMergeLayers);
            MoveLayerUpCommand = new RelayCommand(MoveActiveLayerUp, () => CanMoveActiveLayerUp);
            MoveLayerDownCommand = new RelayCommand(MoveActiveLayerDown, () => CanMoveActiveLayerDown);
            RenameLayerCommand = new RelayCommand(() =>
            {
                if (SpriteState != null)
                {
                    SpriteState.EnsureLayers();
                    if (SpriteState.ActiveLayerIndex >= 0 && SpriteState.ActiveLayerIndex < Layers.Count)
                    {
                        BeginLayerRename(SpriteState.ActiveLayerIndex);
                    }
                }
            }, () => SpriteState != null && Layers.Count > 0);

            IncreaseFrameDelayCommand = new RelayCommand<FrameItemViewModel?>(param =>
            {
                var target = param ?? GetSelectedFrame();
                if (target == null || SpriteState == null) return;
                
                var models = GetSelectedFrameViewModels();
                bool canIncrease = false;
                if (models.Count > 0 && models.Contains(target))
                {
                    canIncrease = models.Exists(m => { int idx = Frames.IndexOf(m); return idx >= 0 && idx < SpriteState.Frames.Count && SpriteState.Frames[idx].DelayMultiplier < 100; });
                }
                else
                {
                    int targetIdx = Frames.IndexOf(target);
                    canIncrease = targetIdx >= 0 && targetIdx < SpriteState.Frames.Count && SpriteState.Frames[targetIdx].DelayMultiplier < 100;
                }
                if (!canIncrease) return;

                SaveStateForUndo();
                if (models.Count > 0 && models.Contains(target))
                {
                    foreach (var m in models)
                    {
                        int idx = Frames.IndexOf(m);
                        if (idx >= 0 && idx < SpriteState.Frames.Count)
                        {
                            SpriteState.Frames[idx].DelayMultiplier = Math.Min(SpriteState.Frames[idx].DelayMultiplier + 1, 100);
                            m.DelayMultiplier = SpriteState.Frames[idx].DelayMultiplier;
                        }
                    }
                }
                else
                {
                    int idx = Frames.IndexOf(target);
                    if (idx >= 0 && idx < SpriteState.Frames.Count)
                    {
                        SpriteState.Frames[idx].DelayMultiplier = Math.Min(SpriteState.Frames[idx].DelayMultiplier + 1, 100);
                        target.DelayMultiplier = SpriteState.Frames[idx].DelayMultiplier;
                    }
                }
                IsDirty = true;
                MarkCodeStale();
                UpdateTextOutputs();
                if (IsAnimationEnabled && _playbackTimer?.IsEnabled == true)
                {
                    UpdatePlaybackTimerIntervalForCurrentFrame();
                }
            });

            DecreaseFrameDelayCommand = new RelayCommand<FrameItemViewModel?>(param =>
            {
                var target = param ?? GetSelectedFrame();
                if (target == null || SpriteState == null) return;
                
                var models = GetSelectedFrameViewModels();
                bool canDecrease = false;
                if (models.Count > 0 && models.Contains(target))
                {
                    canDecrease = models.Exists(m => { int idx = Frames.IndexOf(m); return idx >= 0 && idx < SpriteState.Frames.Count && SpriteState.Frames[idx].DelayMultiplier > 1; });
                }
                else
                {
                    int targetIdx = Frames.IndexOf(target);
                    canDecrease = targetIdx >= 0 && targetIdx < SpriteState.Frames.Count && SpriteState.Frames[targetIdx].DelayMultiplier > 1;
                }
                if (!canDecrease) return;

                SaveStateForUndo();
                if (models.Count > 0 && models.Contains(target))
                {
                    foreach (var m in models)
                    {
                        int idx = Frames.IndexOf(m);
                        if (idx >= 0 && idx < SpriteState.Frames.Count && SpriteState.Frames[idx].DelayMultiplier > 1)
                        {
                            SpriteState.Frames[idx].DelayMultiplier--;
                            m.DelayMultiplier = SpriteState.Frames[idx].DelayMultiplier;
                        }
                    }
                }
                else
                {
                    int idx = Frames.IndexOf(target);
                    if (idx >= 0 && idx < SpriteState.Frames.Count && SpriteState.Frames[idx].DelayMultiplier > 1)
                    {
                        SpriteState.Frames[idx].DelayMultiplier--;
                        target.DelayMultiplier = SpriteState.Frames[idx].DelayMultiplier;
                    }
                }
                IsDirty = true;
                MarkCodeStale();
                UpdateTextOutputs();
                if (IsAnimationEnabled && _playbackTimer?.IsEnabled == true)
                {
                    UpdatePlaybackTimerIntervalForCurrentFrame();
                }
            });

            SetFrameDelayCommand = new RelayCommand<string>(param =>
            {
                if (SpriteState == null || string.IsNullOrWhiteSpace(param)) return;
                if (!int.TryParse(param, out int multiplier)) return;
                multiplier = Math.Clamp(multiplier, 1, 100);

                var target = GetSelectedFrame();
                if (target == null) return;

                var models = GetSelectedFrameViewModels();
                bool hasChange = false;
                if (models.Count > 0 && models.Contains(target))
                {
                    hasChange = models.Exists(m => { int idx = Frames.IndexOf(m); return idx >= 0 && idx < SpriteState.Frames.Count && SpriteState.Frames[idx].DelayMultiplier != multiplier; });
                }
                else
                {
                    int targetIdx = Frames.IndexOf(target);
                    hasChange = targetIdx >= 0 && targetIdx < SpriteState.Frames.Count && SpriteState.Frames[targetIdx].DelayMultiplier != multiplier;
                }
                if (!hasChange) return;

                SaveStateForUndo();
                if (models.Count > 0 && models.Contains(target))
                {
                    foreach (var m in models)
                    {
                        int idx = Frames.IndexOf(m);
                        if (idx >= 0 && idx < SpriteState.Frames.Count)
                        {
                            SpriteState.Frames[idx].DelayMultiplier = multiplier;
                            m.DelayMultiplier = multiplier;
                        }
                    }
                }
                else
                {
                    int idx = Frames.IndexOf(target);
                    if (idx >= 0 && idx < SpriteState.Frames.Count)
                    {
                        SpriteState.Frames[idx].DelayMultiplier = multiplier;
                        target.DelayMultiplier = multiplier;
                    }
                }
                IsDirty = true;
                MarkCodeStale();
                UpdateTextOutputs();
                if (IsAnimationEnabled && _playbackTimer?.IsEnabled == true)
                {
                    UpdatePlaybackTimerIntervalForCurrentFrame();
                }
            });

            RotateCanvasCWCommand = new AsyncRelayCommand(async () => await RotateCanvasAsync(RotationDirection.Clockwise90));
            RotateCanvasCCWCommand = new AsyncRelayCommand(async () => await RotateCanvasAsync(RotationDirection.CounterClockwise90));
            RotateCanvas180Command = new AsyncRelayCommand(async () => await RotateCanvasAsync(RotationDirection.OneEighty));

            FlipCanvasHorizontalCommand = new AsyncRelayCommand(async () => await FlipCanvasAsync(FlipDirection.Horizontal));
            FlipCanvasVerticalCommand = new AsyncRelayCommand(async () => await FlipCanvasAsync(FlipDirection.Vertical));

            FlipSelectionHorizontalCommand = new RelayCommand(
                () => FlipSelection(FlipDirection.Horizontal),
                () => _selectionService.HasActiveSelection && _selectionService.IsFloating && !_selectionService.IsTransforming && CanModifyActiveLayer);
            FlipSelectionVerticalCommand = new RelayCommand(
                () => FlipSelection(FlipDirection.Vertical),
                () => _selectionService.HasActiveSelection && _selectionService.IsFloating && !_selectionService.IsTransforming && CanModifyActiveLayer);

            BeginSelectionTransformCommand = new RelayCommand(
                () => _selectionInput.EnterTransformMode(),
                () => _selectionService.HasActiveSelection && !_selectionService.IsTransforming && CanModifyActiveLayer);

            ResetSymmetryCommand = new RelayCommand(() =>
            {
                SymmetryAxisX = SpriteState.Width / 2.0;
                SymmetryAxisY = SpriteState.Height / 2.0;
                ShowStatus("Symmetry center reset");
            });

            ResetBrushCommand = new RelayCommand(() =>
            {
                BrushSize = 1;
                BrushShape = Core.BrushShape.Circle;
                BrushAngle = 0;
                DitherPattern = Core.DitherPattern.Checkerboard;
                IsPixelPerfectEnabled = false;
                IsContiguousFillEnabled = true;
                ShowStatus("Tool settings reset");
            });

            // ── Animation commands ─────────────────────────────────────────
            ToggleAnimationCommand = new RelayCommand(() =>
            {
                IsAnimationEnabled = !IsAnimationEnabled;
                AddFrameCommand?.NotifyCanExecuteChanged();
                DuplicateFrameCommand?.NotifyCanExecuteChanged();
                DeleteFrameCommand?.NotifyCanExecuteChanged();
                NextFrameCommand?.NotifyCanExecuteChanged();
                PreviousFrameCommand?.NotifyCanExecuteChanged();
                SelectAllFramesCommand?.NotifyCanExecuteChanged();
                SelectActiveFrameOnlyCommand?.NotifyCanExecuteChanged();
                BatchFrameOperationCommand?.NotifyCanExecuteChanged();
                ShowStatus(IsAnimationEnabled ? "Animation mode enabled" : "Animation mode disabled");
            });

            TogglePlaybackCommand = new RelayCommand(() =>
            {
                IsPlaying = !IsPlaying;
            });

            ToggleOnionSkinCommand = new RelayCommand(() =>
            {
                if (IsOnionSkinPrevEnabled || IsOnionSkinNextEnabled)
                {
                    IsOnionSkinPrevEnabled = false;
                    IsOnionSkinNextEnabled = false;
                    ShowStatus("Onion skins disabled");
                }
                else
                {
                    IsOnionSkinPrevEnabled = true;
                    IsOnionSkinNextEnabled = true;
                    ShowStatus("Onion skins enabled");
                }
            });

            AddFrameCommand = new RelayCommand(() =>
            {
                if (SpriteState == null) return;
                if (_selectionService.IsFloating)
                {
                    _selectionInput.CommitIfActive();
                }

                SaveStateForUndo();
                var newFrame = new FrameState
                {
                    Name = string.Create(CultureInfo.InvariantCulture, $"Frame {SpriteState.Frames.Count + 1}"),
                    LayerPixels = [],
                };
                
                for (int i = 0; i < SpriteState.Layers.Count; i++)
                {
                    if (SpriteState.Layers[i].IsGlobal && SpriteState.Frames.Count > 0)
                        newFrame.LayerPixels.Add(SpriteState.Frames[0].LayerPixels[i]);
                    else if (SpriteState.Layers[i].PreserveOverflow)
                        newFrame.LayerPixels.Add(new Hexprite.Core.OverflowPixelBuffer(SpriteState.Width, SpriteState.Height));
                    else
                        newFrame.LayerPixels.Add(new Hexprite.Core.MonochromePixelBuffer(new bool[SpriteState.Width * SpriteState.Height]));
                }
                
                int insertIndex = SpriteState.ActiveFrameIndex + 1;
                int backupSourceIndex = SpriteState.ActiveFrameIndex;
                SpriteState.Frames.Insert(insertIndex, newFrame);
                SpriteState.InsertGlobalBackupFromFrame(insertIndex, backupSourceIndex);
                SpriteState.NormalizeLayerState();
                
                RenumberFrames();
                SpriteState.SetActiveFrame(insertIndex);
                RebuildFrameViewModels([newFrame]);
                _selectedFrameIndex = SpriteState.ActiveFrameIndex;
                OnPropertyChanged(nameof(SelectedFrameIndex));
                OnPropertyChanged(nameof(EstimatedMemoryUsage));
                
                RebuildLayerViewModels();
                UpdateOnionSkinCache();
                if (IsPlaying)
                {
                    RebuildPlaybackCache();
                    UpdatePlaybackTimerIntervalForCurrentFrame();
                    for (int i = 0; i < Frames.Count; i++)
                        Frames[i].IsPlayingBack = (i == _playbackFrameIndex);
                    UpdatePlaybackPreview();
                }
                RedrawGridFromMemory();
                MarkCodeStale();
                
                DeleteFrameCommand?.NotifyCanExecuteChanged();
                NextFrameCommand?.NotifyCanExecuteChanged();
                PreviousFrameCommand?.NotifyCanExecuteChanged();
                ShowStatus("Frame added");
            }, () => IsAnimationEnabled);

            DuplicateFrameCommand = new RelayCommand<FrameItemViewModel?>(targetFrame =>
            {
                if (SpriteState == null) return;

                List<int> selectedIndices;
                if (targetFrame != null && !targetFrame.IsSelected)
                {
                    int targetIdx = Frames.IndexOf(targetFrame);
                    selectedIndices = (targetIdx >= 0 && targetIdx < SpriteState.Frames.Count)
                        ? [targetIdx]
                        : [SpriteState.ActiveFrameIndex];
                }
                else
                {
                    selectedIndices = [.. Frames
                        .Select((f, i) => new { f, i })
                        .Where(x => x.f.IsSelected)
                        .Select(x => x.i)
                        .Order()];
                        
                    if (selectedIndices.Count == 0)
                    {
                        selectedIndices.Add(SpriteState.ActiveFrameIndex);
                    }
                }

                if (_selectionService.IsFloating)
                {
                    _selectionInput.CommitIfActive();
                }

                SaveStateForUndo();
                
                var clonedFrames = new List<FrameState>();
                selectedIndices.Reverse(); // Process from back to front to not mess up insertion indices
                foreach (var idx in selectedIndices)
                {
                    var cloned = SpriteState.Frames[idx].Clone();
                    int insertIndex = idx + 1;
                    SpriteState.Frames.Insert(insertIndex, cloned);
                    SpriteState.InsertGlobalBackupFromFrame(insertIndex, idx);
                    clonedFrames.Add(cloned);
                }
                SpriteState.NormalizeLayerState();
                
                RenumberFrames();
                SpriteState.SetActiveFrame(Math.Max(0, SpriteState.Frames.IndexOf(clonedFrames[0])));
                RebuildFrameViewModels(clonedFrames);
                _selectedFrameIndex = SpriteState.ActiveFrameIndex;
                OnPropertyChanged(nameof(SelectedFrameIndex));
                OnPropertyChanged(nameof(EstimatedMemoryUsage));
                
                RebuildLayerViewModels();
                UpdateOnionSkinCache();
                if (IsPlaying)
                {
                    RebuildPlaybackCache();
                    UpdatePlaybackTimerIntervalForCurrentFrame();
                    for (int i = 0; i < Frames.Count; i++)
                        Frames[i].IsPlayingBack = (i == _playbackFrameIndex);
                    UpdatePlaybackPreview();
                }
                RedrawGridFromMemory();
                MarkCodeStale();
                
                DeleteFrameCommand?.NotifyCanExecuteChanged();
                NextFrameCommand?.NotifyCanExecuteChanged();
                PreviousFrameCommand?.NotifyCanExecuteChanged();
                ShowStatus("Frame duplicated");
            }, _ => IsAnimationEnabled);

            DeleteFrameCommand = new RelayCommand<FrameItemViewModel?>(targetFrame =>
            {
                if (SpriteState == null || SpriteState.Frames.Count <= 1) return;
                
                List<int> selectedIndices;
                if (targetFrame != null && !targetFrame.IsSelected)
                {
                    int targetIdx = Frames.IndexOf(targetFrame);
                    selectedIndices = (targetIdx >= 0 && targetIdx < SpriteState.Frames.Count)
                        ? [targetIdx]
                        : [SpriteState.ActiveFrameIndex];
                }
                else
                {
                    selectedIndices = [.. Frames
                        .Select((f, i) => new { f, i })
                        .Where(x => x.f.IsSelected)
                        .Select(x => x.i)
                        .OrderDescending()];
                        
                    if (selectedIndices.Count == 0)
                    {
                        selectedIndices.Add(SpriteState.ActiveFrameIndex);
                    }
                }
                
                if (selectedIndices.Count == SpriteState.Frames.Count)
                {
                    selectedIndices.RemoveAt(0); // Ensure at least one frame remains
                }

                if (selectedIndices.Count == 0) return;

                if (_selectionService.IsFloating)
                {
                    _selectionInput.CommitIfActive();
                }

                SaveStateForUndo();
                foreach (var idx in selectedIndices.OrderDescending())
                {
                    SpriteState.Frames.RemoveAt(idx);
                    SpriteState.RemoveGlobalBackupAt(idx);
                }
                SpriteState.NormalizeLayerState();
                
                int newIndex = Math.Clamp(SpriteState.ActiveFrameIndex, 0, SpriteState.Frames.Count - 1);
                
                RenumberFrames();
                SpriteState.SetActiveFrame(newIndex);
                RebuildFrameViewModels([SpriteState.Frames[newIndex]]);
                _selectedFrameIndex = SpriteState.ActiveFrameIndex;
                OnPropertyChanged(nameof(SelectedFrameIndex));
                OnPropertyChanged(nameof(EstimatedMemoryUsage));
                
                RebuildLayerViewModels();
                UpdateOnionSkinCache();

                if (IsPlaying)
                {
                    if (SpriteState.Frames.Count <= 1)
                    {
                        IsPlaying = false;
                    }
                    else
                    {
                        _playbackFrameIndex = Math.Clamp(_playbackFrameIndex, 0, SpriteState.Frames.Count - 1);
                        RebuildPlaybackCache();
                        UpdatePlaybackTimerIntervalForCurrentFrame();
                        for (int i = 0; i < Frames.Count; i++)
                            Frames[i].IsPlayingBack = (i == _playbackFrameIndex);
                        UpdatePlaybackPreview();
                    }
                }

                RedrawGridFromMemory();
                MarkCodeStale();
                
                DeleteFrameCommand?.NotifyCanExecuteChanged();
                NextFrameCommand?.NotifyCanExecuteChanged();
                PreviousFrameCommand?.NotifyCanExecuteChanged();
                ShowStatus("Frame deleted");
            }, _ => IsAnimationEnabled && SpriteState?.Frames.Count > 1);

            NextFrameCommand = new RelayCommand(NextFrame, () => IsAnimationEnabled && SpriteState?.Frames.Count > 1);
            PreviousFrameCommand = new RelayCommand(PreviousFrame, () => IsAnimationEnabled && SpriteState?.Frames.Count > 1);

            BatchFrameOperationCommand = new RelayCommand<FrameBatchOperation>(
                ExecuteBatchFrameOperation,
                CanExecuteBatchFrameOperation);
            SelectAllFramesCommand = new RelayCommand(SelectAllFrames, () => IsAnimationEnabled && Frames.Count > 0);
            SelectActiveFrameOnlyCommand = new RelayCommand(SelectActiveFrameOnly, () => IsAnimationEnabled && Frames.Count > 0);
            ClearFrameCommand = new RelayCommand(
                () => ExecuteBatchFrameOperation(FrameBatchOperation.ClearEntireFrame),
                () => CanExecuteBatchFrameOperation(FrameBatchOperation.ClearEntireFrame));

            // Keep menu enabled/disabled state in sync with selection/floating/transform status.
            // Otherwise WPF can keep stale CanExecute values and the command won't execute.
            // Bug 3: Store the delegate so it can be unsubscribed in Detach(), preventing
            // a GC root from SelectionService → anonymous lambda → MainViewModel (entire tab graph).
            _selectionChangedHandler = (_, _) =>
            {
                ((RelayCommand)FlipSelectionHorizontalCommand).NotifyCanExecuteChanged();
                ((RelayCommand)FlipSelectionVerticalCommand).NotifyCanExecuteChanged();
                ((RelayCommand)BeginSelectionTransformCommand).NotifyCanExecuteChanged();
                ((RelayCommand)ReselectCommand).NotifyCanExecuteChanged();
                ((RelayCommand)NewLayerFromSelectionCommand).NotifyCanExecuteChanged();
                UpdateSelectionInfo();
                OnPropertyChanged(nameof(IsSelectionFloating));
            };
            _selectionService.SelectionChanged += _selectionChangedHandler;

            // ── Initialization ────────────────────────────────────────────
            InitializeBrushColors();
        }

        public async System.Threading.Tasks.Task<bool> ExecuteUpdateLinkedSourceAsync()
        {
            if (SpriteState == null || !SpriteState.IsLinked) return false;

            using var operation = LoggingService.BeginOperation("MainViewModel.UpdateLinkedSource", new 
            { 
                file = SpriteState.LinkedSourceFile, 
                variable = SpriteState.LinkedVariableName,
                format = SpriteState.LinkedFormat,
                width = SpriteState.Width,
                height = SpriteState.Height,
                frames = SpriteState.Frames?.Count ?? 1,
            });

            // Guard: linked file must still exist on disk
            if (!File.Exists(SpriteState.LinkedSourceFile))
            {
                _dialogService.ShowMessage($"The linked source file no longer exists:\n{SpriteState.LinkedSourceFile}");
                return false;
            }
            if (IsCodeStale)
            {
                await UpdateTextOutputsAsync();
            }

            if (LinkedFileChangedExternally)
            {
                bool confirm = _dialogService.ShowConfirmation(
                    "The linked source file was modified externally. If you update now, those external changes will be overwritten. Proceed anyway?",
                    "Overwrite External Changes");
                if (!confirm) return false;
            }

            try
            {
                var cleanSettings = ExportSettings.Clone();
                cleanSettings.IncludeUsageComment = false;
                cleanSettings.IncludeDimensionConstants = false;
                if (SpriteState.LinkedFormat.HasValue)
                {
                    cleanSettings.Format = SpriteState.LinkedFormat.Value;
                }
                if (!string.IsNullOrWhiteSpace(SpriteState.LinkedVariableName))
                {
                    cleanSettings.SpriteName = SpriteState.LinkedVariableName;
                }
                if (SpriteState.IsAnimationEnabled || (SpriteState.Frames != null && SpriteState.Frames.Count > 1))
                {
                    cleanSettings.ExportAsAnimation = true;
                }

                var frames = GetExportFrames(cleanSettings.ExportAsAnimation);
                var delays = cleanSettings.ExportAsAnimation && SpriteState.Frames != null 
                    ? SpriteState.Frames.Select(f => f.DelayMultiplier).ToList() 
                    : null;
                string cleanCode = await _codeGen.GenerateCodeAsync(frames, SpriteState.Width, SpriteState.Height,
                                                                    cleanSettings, isFloating: false,
                                                                    floatingPixels: null, 0, 0, 0, 0, _floatingPasteMode,
                                                                    delays, CancellationToken.None);

                SuspendLinkedFileWatcher();
                try
                {
                    string newHash = string.Empty;
                    await System.Threading.Tasks.Task.Run(() =>
                    {
                        int? frameCount = cleanSettings.ExportAsAnimation && SpriteState.Frames != null 
                            ? SpriteState.Frames.Count 
                            : 1;
                        string writtenText = _importExportService.UpdateSpriteInFile(SpriteState.LinkedSourceFile, SpriteState.LinkedVariableName!, cleanCode,
                            SpriteState.Width, SpriteState.Height, frameCount);
                        newHash = ComputeHashFromString(writtenText);
                        lock (_linkedFileLock)
                        {
                            if (!string.IsNullOrEmpty(newHash))
                            {
                                _globalLastSavedHashes[SpriteState.LinkedSourceFile] = newHash;
                                _documentSyncedHash = newHash;
                            }
                        }
                    }, CancellationToken.None);
                }
                finally
                {
                    ResumeLinkedFileWatcher();
                }
                LinkedFileChangedExternally = false;
                MarkAsClean();
                _autosaveService.ClearCurrentAutosave();
                ShowStatus($"✓ Updated {SpriteState.LinkedSourceFileName}");
                return true;
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Error(ex, "MainViewModel.UpdateLinkedSource");
                _dialogService.ShowMessage($"Error updating linked source: {ex.Message}");
                return false;
            }
        }

        private FrameItemViewModel? GetSelectedFrame()
        {
            return Frames.FirstOrDefault(f => f.IsSelected);
        }

        public void ClearAutosave()
        {
            _autosaveService.ClearCurrentAutosave();
        }

        private List<FrameItemViewModel> GetSelectedFrameViewModels()
        {
            return [.. Frames.Where(f => f.IsSelected)];
        }

        private void ExecuteExportImage()
        {
            var settings = SpriteState.ImageExportSettings ?? new ImageExportSettings();
            var newSettings = _dialogService.ShowExportImageDialog(settings, SpriteState);
            if (newSettings == null) return;

            SpriteState.ImageExportSettings = newSettings;
            IsDirty = true;

            string filter = newSettings.Format switch
            {
                ImageExportFormat.Png => "PNG Image (*.png)|*.png|All Files (*.*)|*.*",
                ImageExportFormat.Bmp => "BMP Image (*.bmp)|*.bmp|All Files (*.*)|*.*",
                ImageExportFormat.Gif => "Animated GIF (*.gif)|*.gif|All Files (*.*)|*.*",
                ImageExportFormat.PngSequence => "PNG Sequence (*.png)|*.png|All Files (*.*)|*.*",
                _ => "All Files (*.*)|*.*",
            };

            string defaultExt = newSettings.Format switch
            {
                ImageExportFormat.Png => ".png",
                ImageExportFormat.Bmp => ".bmp",
                ImageExportFormat.Gif => ".gif",
                ImageExportFormat.PngSequence => ".png",
                _ => "",
            };

            var path = _dialogService.ShowSaveFileDialog(filter, "Export Image", defaultExt);
            if (path != null)
            {
                try
                {
                    _exportService.Export(path, SpriteState, newSettings);
                    ShowStatus($"Exported to {System.IO.Path.GetFileName(path)}", 3000);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to export image");
                    _dialogService.ShowMessage($"Error exporting image: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Stops animation playback, cancels pending status messages, and
        /// unsubscribes all event handlers so this document can be GC'd
        /// after tab close. Called by <see cref="ShellViewModel"/> (Bug 4).
        /// </summary>
        public void Detach()
        {
            // Stop the autosave timer before clearing the file to prevent the
            // orphaned timer from recreating the autosave on its next tick.
            _autosaveService.StopAutosaveLoop();
            _autosaveService.ClearCurrentAutosave();
            (_autosaveService as IDisposable)?.Dispose();
            
            // Commit any in-progress text editing before teardown
            StopTextEditing();

            // Stop the DispatcherTimer and unsubscribe Tick to fully break the
            // reference cycle MVM._playbackTimer → timer → Tick delegate → MVM.
            //
            // Why both steps matter (verified via reflection):
            //   Stop()  — nulls the Dispatcher's internal _operation, releasing the
            //              Dispatcher's strong hold on the timer itself. Without Stop(),
            //              the running timer would fire indefinitely into a dead tab.
            //   -= Tick — the Tick delegate is stored on the timer, not on the
            //              Dispatcher. Stop() does NOT clear it. Without this line, the
            //              cycle MVM ↔ timer survives until Gen2 GC cycle collection,
            //              keeping each closed tab's entire object graph in memory
            //              longer than necessary.
            if (_playbackTimer != null)
            {
                _playbackTimer.Stop();
                _playbackTimer.Tick -= PlaybackTimer_Tick;
                _playbackTimer = null;
            }

            // Cancel and dispose any pending status auto-clear task.
            _statusCts?.Cancel();
            _statusCts?.Dispose();
            _statusCts = null;

            // Dispose linked file watcher and unsubscribe global pull
            StopWatchingLinkedFile();
            GlobalPullRequested -= HandleGlobalPullRequested;

            // Unsubscribe the stored SelectionChanged delegate so that
            // SelectionService no longer holds a root into this tab's object graph.
            if (_selectionChangedHandler != null)
            {
                _selectionService.SelectionChanged -= _selectionChangedHandler;
                _selectionChangedHandler = null;
            }

            // Unsubscribe hardware preview events so the singleton service doesn't
            // hold a reference chain into this dead tab's object graph.
            if (_hwPreviewEnabledHandler != null)
            {
                _hardwarePreview.EnabledChanged -= _hwPreviewEnabledHandler;
                _hwPreviewEnabledHandler = null;
            }
            if (_hwPreviewConnectionStateHandler != null)
            {
                _hardwarePreview.ConnectionStateChanged -= _hwPreviewConnectionStateHandler;
                _hwPreviewConnectionStateHandler = null;
            }

            // Same Stop()+unsubscribe reasoning as _playbackTimer above.
            if (_portAutoRefreshTimer != null)
            {
                _portAutoRefreshTimer.Stop();
                _portAutoRefreshTimer.Tick -= PortAutoRefreshTimer_Tick;
                _portAutoRefreshTimer = null;
            }
        }

        private bool _disposed;

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Detach();
                    lock (_textUpdateCtsLock)
                    {
                        _textUpdateCts?.Cancel();
                        _textUpdateCts?.Dispose();
                        _textUpdateCts = null;
                    }
                    _portRefreshLock.Dispose();
                }
                _disposed = true;
            }
        }

        /// <summary>
        /// Initializes the document-scoped controllers after construction.
        /// This is called by ShellViewModel.CreateDocument after the document
        /// is created but before InitializeGrid is called.
        /// </summary>
        public void InitializeControllers(
            IToolInputController toolInput,
            ISelectionInputController selectionInput,
            BitmapPreviewRenderer previewRenderer)
        {
            _toolInput = toolInput ?? throw new ArgumentNullException(nameof(toolInput));
            _selectionInput = selectionInput ?? throw new ArgumentNullException(nameof(selectionInput));
            _previewRenderer = previewRenderer ?? throw new ArgumentNullException(nameof(previewRenderer));
        }

        // ── Public methods called by the View ─────────────────────────────

        public void InitializeGrid(int width, int height)
            => InitializeGrid(width, height, redrawImmediately: true);

        public void InitializeGrid(int width, int height, bool redrawImmediately)
        {
            SpriteState = new SpriteState(width, height);
            SpriteState.EnsureLayers();
            RebuildBitmaps(width, height);

            // Initialize symmetry axes to canvas center
            SymmetryAxisX = width / 2.0;
            SymmetryAxisY = height / 2.0;

            // Select the first layer (only layer) for new canvas
            SetSingleLayerSelection(0);

            AutoSelectPreviewDisplayTypeForCanvasSize(width, height);
            AutoSelectPreviewScaleForCanvasSize(width, height);

            MarkCodeStale();
            if (redrawImmediately)
                RedrawGridFromMemory();

            NotifyCanvasLayoutChanged();

            // Refresh undo/redo button states for new document
            UndoCommand?.NotifyCanExecuteChanged();
            RedoCommand?.NotifyCanExecuteChanged();
        }

        private void AutoSelectPreviewDisplayTypeForCanvasSize(int width, int height)
        {
            // Only auto-select when the user hasn't explicitly chosen a preview style.
            // This keeps the app feeling "smart" for common presets (SSD1306 / ePaper)
            // while still respecting the user's preference if they changed it.
            if (_previewDisplayType != DisplayType.GenericWhite)
                return;

            var inferred = InferPreviewDisplayType(width, height);
            if (inferred == _previewDisplayType)
                return;

            _previewDisplayType = inferred;
            OnPropertyChanged(nameof(PreviewDisplayTypeIndex));

            // Update theme-backed brushes + cached pixel colors (no redraw here; caller will redraw if needed).
            UpdatePreviewColors();
            InitializeBrushColors();
        }

        private static DisplayType InferPreviewDisplayType(int width, int height)
        {
            // SSD1306 family
            if (width == 128 && (height == 64 || height == 32))
                return DisplayType.SSD1306Blue;

            // Common monochrome e-paper preset
            if (width == 296 && height == 128)
                return DisplayType.ePaper;

            return DisplayType.GenericWhite;
        }

        /// <summary>
        /// Selects the largest integer preview scale that fits within
        /// <see cref="MaxPreviewDimensionPx"/> for the given canvas dimensions.
        /// Called once during <see cref="InitializeGrid"/> so the preview
        /// starts at a useful zoom rather than being immediately "capped".
        /// </summary>
        private void AutoSelectPreviewScaleForCanvasSize(int width, int height)
        {
            int maxDim = Math.Max(width, height);
            if (maxDim <= 0) return;
            int optimalScale = (int)Math.Floor(MaxPreviewDimensionPx / (double)maxDim);
            optimalScale = Math.Clamp(optimalScale, 1, 2);
            // Use the property setter so all dependent properties, bitmaps, and
            // preference persistence are updated in one shot.
            PreviewScale = optimalScale;
        }

        public void SetActiveLayer(int index, bool shouldRedraw)
        {
            if (IsProcessing) return;
            if (SpriteState == null) return;
            if (SpriteState.ActiveLayerIndex == index) return;

            // Sync overflow data back before switching away from this layer
            SpriteState.SyncActiveLayer();

            // Commit floating pixels to the CURRENT layer before switching away
            if (_selectionService.IsFloating)
            {
                _selectionInput.CommitIfActive();
            }

            _selectionInput.ResetControllerState();
            _toolInput.CancelInProgressDrawing();

            SpriteState.SetActiveLayer(index);
            _selectedLayerIndex = SpriteState.ActiveLayerIndex;
            OnPropertyChanged(nameof(SelectedLayerIndex));
            OnPropertyChanged(nameof(HasSelectedLayer));
            OnPropertyChanged(nameof(ActiveLayerBlendMode));
            OnPropertyChanged(nameof(ActiveLayerOpacityMode));
            SyncLayerActiveFlags();
            UpdateSelectionInfo();
            ((RelayCommand)NewLayerFromSelectionCommand).NotifyCanExecuteChanged();
            if (shouldRedraw)
                RedrawGridFromMemory();
        }

        /// <summary>
        /// Switches to the specified frame and updates all dependent state.
        /// Called when user clicks on a frame in the timeline.
        /// </summary>
        public void SetActiveFrame(int index)
        {
            if (IsProcessing) return;
            if (SpriteState == null) return;
            if (index < 0 || index >= SpriteState.Frames.Count) return;
            if (SpriteState.ActiveFrameIndex == index) return;

            // Commit floating pixels before switching frames
            // Sync overflow data back before switching frames
            SpriteState.SyncActiveLayer();

            if (_selectionService.IsFloating)
            {
                _selectionInput.CommitIfActive();
            }

            _selectionInput.ResetControllerState();
            _toolInput.CancelInProgressDrawing();

            SpriteState.SetActiveFrame(index);
            _selectedFrameIndex = SpriteState.ActiveFrameIndex;
            OnPropertyChanged(nameof(SelectedFrameIndex));
            OnPropertyChanged(nameof(CurrentFrameDisplay));
            
            RebuildLayerViewModels();
            if (!_suppressSelectedFrameBindingToState)
            {
                for (int i = 0; i < Frames.Count; i++)
                {
                    Frames[i].IsActive = i == SpriteState.ActiveFrameIndex;
                    Frames[i].IsSelected = i == SpriteState.ActiveFrameIndex;
                }
                NotifyFrameSelectionChanged();
            }
            else
            {
                SyncFrameActiveFlags();
            }
            
            UpdateOnionSkinCache();
            RedrawGridFromMemory();
            MarkCodeStale();
        }

        private void RenumberFrames()
        {
            for (int i = 0; i < SpriteState.Frames.Count; i++)
            {
                SpriteState.Frames[i].Name = string.Create(CultureInfo.InvariantCulture, $"Frame {i + 1}");
            }
        }

        public void MoveFrame(int fromIndex, int toIndex)
        {
            if (SpriteState == null) return;
            if (fromIndex == toIndex) return;
            if (fromIndex < 0 || fromIndex >= SpriteState.Frames.Count) return;
            if (toIndex < 0 || toIndex >= SpriteState.Frames.Count) return;

            if (_selectionService.IsFloating)
            {
                _selectionInput.CommitIfActive();
            }

            SaveStateForUndo();
            var previousFrameOrder = SpriteState.Frames.ToList();
            var moved = SpriteState.Frames[fromIndex];
            SpriteState.Frames.RemoveAt(fromIndex);
            SpriteState.Frames.Insert(toIndex, moved);
            SpriteState.ReorderGlobalBackups(previousFrameOrder);

            int newActiveIndex = SpriteState.ActiveFrameIndex;
            if (newActiveIndex == fromIndex)
                newActiveIndex = toIndex;
            else if (newActiveIndex > fromIndex && newActiveIndex <= toIndex)
                newActiveIndex--;
            else if (newActiveIndex < fromIndex && newActiveIndex >= toIndex)
                newActiveIndex++;

            RenumberFrames();
            SpriteState.SetActiveFrame(newActiveIndex);

            RebuildFrameViewModels([moved]);
            _selectedFrameIndex = SpriteState.ActiveFrameIndex;
            OnPropertyChanged(nameof(SelectedFrameIndex));
            
            UpdateOnionSkinCache();
            if (IsPlaying)
            {
                _playbackFrameIndex = Math.Clamp(_playbackFrameIndex, 0, Math.Max(0, SpriteState.Frames.Count - 1));
                RebuildPlaybackCache();
                UpdatePlaybackTimerIntervalForCurrentFrame();
                for (int i = 0; i < Frames.Count; i++)
                    Frames[i].IsPlayingBack = (i == _playbackFrameIndex);
                UpdatePlaybackPreview();
            }
            RedrawGridFromMemory();
            MarkCodeStale();
        }

        public void MoveFrames(List<FrameItemViewModel> payload, int targetIndex)
        {
            if (SpriteState == null) return;
            if (payload == null || payload.Count == 0) return;
            
            var indicesToMove = new List<int>();
            foreach(var item in payload)
            {
                int idx = Frames.IndexOf(item);
                if (idx >= 0 && !indicesToMove.Contains(idx))
                    indicesToMove.Add(idx);
            }
            if (indicesToMove.Count == 0) return;
            indicesToMove.Sort();

            int adjustedTarget = targetIndex;
            for (int i = indicesToMove.Count - 1; i >= 0; i--)
            {
                if (indicesToMove[i] < adjustedTarget) adjustedTarget--;
            }
            adjustedTarget = Math.Clamp(adjustedTarget, 0, Math.Max(0, SpriteState.Frames.Count - indicesToMove.Count));
            bool isNoOp = true;
            for (int i = 0; i < indicesToMove.Count; i++)
            {
                if (indicesToMove[i] != adjustedTarget + i)
                {
                    isNoOp = false;
                    break;
                }
            }
            if (isNoOp) return;

            if (_selectionService.IsFloating)
            {
                _selectionInput.CommitIfActive();
            }
            SaveStateForUndo();

            var previousFrameOrder = SpriteState.Frames.ToList();
            var movedFrames = new List<FrameState>();
            foreach (var idx in indicesToMove)
                movedFrames.Add(SpriteState.Frames[idx]);

            var activeFrame = SpriteState.Frames[SpriteState.ActiveFrameIndex];

            for (int i = indicesToMove.Count - 1; i >= 0; i--)
            {
                int idx = indicesToMove[i];
                SpriteState.Frames.RemoveAt(idx);
                if (idx < targetIndex) targetIndex--;
            }

            targetIndex = Math.Clamp(targetIndex, 0, SpriteState.Frames.Count);
            for (int i = 0; i < movedFrames.Count; i++)
            {
                SpriteState.Frames.Insert(targetIndex + i, movedFrames[i]);
            }
            SpriteState.ReorderGlobalBackups(previousFrameOrder);

            int newActiveIndex = SpriteState.Frames.IndexOf(activeFrame);
            if (newActiveIndex < 0) newActiveIndex = 0;

            RenumberFrames();
            SpriteState.SetActiveFrame(newActiveIndex);

            RebuildFrameViewModels(movedFrames);

            var movedFrameSet = new HashSet<FrameState>(movedFrames);
            for (int i = 0; i < Frames.Count; i++)
            {
                Frames[i].IsSelected = i < SpriteState.Frames.Count && movedFrameSet.Contains(SpriteState.Frames[i]);
            }

            _selectedFrameIndex = SpriteState.ActiveFrameIndex;
            OnPropertyChanged(nameof(SelectedFrameIndex));
            NotifyFrameSelectionChanged();
            
            UpdateOnionSkinCache();
            if (IsPlaying)
            {
                _playbackFrameIndex = Math.Clamp(_playbackFrameIndex, 0, Math.Max(0, SpriteState.Frames.Count - 1));
                RebuildPlaybackCache();
                UpdatePlaybackTimerIntervalForCurrentFrame();
                for (int i = 0; i < Frames.Count; i++)
                    Frames[i].IsPlayingBack = (i == _playbackFrameIndex);
                UpdatePlaybackPreview();
            }
            RedrawGridFromMemory();
            MarkCodeStale();
        }

        /// <summary>
        /// Selects all non-transparent pixels of the specified layer.
        /// Called when CTRL+clicking on a layer in the layers panel.
        /// </summary>
        public void SelectLayerContent(int layerIndex)
        {
            if (SpriteState == null || layerIndex < 0 || layerIndex >= SpriteState.Layers.Count)
                return;

            // Commit any active selection first
            _selectionInput.CommitIfActive();
            _selectionService.Cancel();

            // Make the clicked layer active (Photoshop-style behavior)
            SetSingleLayerSelection(layerIndex);

            bool[] layerPixels;
            int widthToUse = SpriteState.Width;
            int dx = 0;
            int dy = 0;

            if (SpriteState.Frames == null || SpriteState.Frames.Count == 0 ||
                SpriteState.ActiveFrameIndex < 0 || SpriteState.ActiveFrameIndex >= SpriteState.Frames.Count)
                return;

            var frame = SpriteState.Frames[SpriteState.ActiveFrameIndex];
            if (frame.LayerPixels == null || layerIndex < 0 || layerIndex >= frame.LayerPixels.Count)
                return;

            var buffer = frame.LayerPixels[layerIndex];
            if (buffer == null)
                return;
            if (buffer is Hexprite.Core.OverflowPixelBuffer ovf)
            {
                layerPixels = ovf.GetExtendedData();
                widthToUse = ovf.ExtendedWidth;
                dx = -ovf.MarginX;
                dy = -ovf.MarginY;
            }
            else
            {
                layerPixels = buffer.GetMonochromeData();
            }

            if (layerPixels == null)
                return;

            // Find the bounding box of all set pixels in the layer
            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue;

            for (int i = 0; i < layerPixels.Length; i++)
            {
                if (layerPixels[i])
                {
                    int x = (i % widthToUse) + dx;
                    int y = (i / widthToUse) + dy;
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }

            // No pixels to select
            if (minX == int.MaxValue)
                return;

            // Create a mask from the layer pixels
            int maskW = maxX - minX + 1;
            int maskH = maxY - minY + 1;
            var mask = new bool[maskW, maskH];

            for (int my = 0; my < maskH; my++)
            {
                for (int mx = 0; mx < maskW; mx++)
                {
                    int canvasX = minX + mx;
                    int canvasY = minY + my;
                    
                    int extX = canvasX - dx;
                    int extY = canvasY - dy;
                    
                    if (extX >= 0 && extX < widthToUse && extY >= 0 && extY < layerPixels.Length / widthToUse)
                    {
                        int idx = (extY * widthToUse) + extX;
                        if (layerPixels[idx])
                        {
                            mask[mx, my] = true;
                        }
                    }
                }
            }

            // Apply the selection
            _selectionService.ApplyMask(mask, minX, minY, maxX, maxY, SelectionMode.Replace);
            RedrawGridFromMemory();
        }

        public void ReloadLayersFromState()
        {
            SpriteState.EnsureLayers();
            RebuildLayerViewModels();
            RebuildFrameViewModels();
        }

        public void MoveLayer(int fromIndex, int toIndex)
        {
            SpriteState.EnsureLayers();
            if (fromIndex == toIndex) return;
            if (fromIndex < 0 || fromIndex >= SpriteState.Layers.Count) return;
            if (toIndex < 0 || toIndex >= SpriteState.Layers.Count) return;

            if (_selectionService.IsFloating)
            {
                _selectionInput.CommitIfActive();
            }

            ApplyLayerMutation(() =>
            {
                var moved = SpriteState.Layers[fromIndex];
                SpriteState.Layers.RemoveAt(fromIndex);
                SpriteState.Layers.Insert(toIndex, moved);

                foreach (var frame in SpriteState.Frames)
                {
                    var movedPixels = frame.LayerPixels[fromIndex];
                    frame.LayerPixels.RemoveAt(fromIndex);
                    frame.LayerPixels.Insert(toIndex, movedPixels);
                }

                if (SpriteState.ActiveLayerIndex == fromIndex)
                    SpriteState.ActiveLayerIndex = toIndex;
                else if (SpriteState.ActiveLayerIndex > fromIndex && SpriteState.ActiveLayerIndex <= toIndex)
                    SpriteState.ActiveLayerIndex--;
                else if (SpriteState.ActiveLayerIndex < fromIndex && SpriteState.ActiveLayerIndex >= toIndex)
                    SpriteState.ActiveLayerIndex++;

                SpriteState.SetActiveLayer(SpriteState.ActiveLayerIndex);

                // Update multi-selection to track the active layer
                SelectedLayerIndices.Clear();
                SelectedLayerIndices.Add(SpriteState.ActiveLayerIndex);
            });
        }

        public void BeginLayerRename(int index)
        {
            SpriteState.EnsureLayers();
            if (index < 0 || index >= Layers.Count) return;
            for (int i = 0; i < Layers.Count; i++)
                Layers[i].IsRenaming = i == index;
        }

        public void UpdateLayerName(int index, string? name)
        {
            SpriteState.EnsureLayers();
            if (index < 0 || index >= SpriteState.Layers.Count) return;
            string trimmed = string.IsNullOrWhiteSpace(name) ? string.Create(CultureInfo.InvariantCulture, $"Layer {index + 1}") : name.Trim();
            if (SpriteState.Layers[index].Name == trimmed) return;

            SaveStateForUndo();
            SpriteState.Layers[index].Name = trimmed;
            Layers[index].Name = trimmed;
            IsDirty = true;
        }

        public void SetLayerVisibility(int index, bool isVisible)
        {
            SpriteState.EnsureLayers();
            if (index < 0 || index >= SpriteState.Layers.Count) return;
            if (SpriteState.Layers[index].IsVisible == isVisible) return;
            if (!isVisible)
            {
                int visibleCount = 0;
                foreach (var layer in SpriteState.Layers)
                    if (layer.IsVisible) visibleCount++;
                if (visibleCount <= 1)
                {
                    _dialogService.ShowMessage("At least one layer must remain visible.");
                    // IsChecked TwoWay-binding may have flipped the row VM already; SpriteState stays visible.
                    if (index < Layers.Count)
                        Layers[index].IsVisible = SpriteState.Layers[index].IsVisible;
                    return;
                }
            }

            // If the user hides the active layer, stamp its floating pixels down first
            if (!isVisible && index == SpriteState.ActiveLayerIndex && _selectionService.IsFloating)
            {
                _selectionInput.CommitIfActive();
            }

            ApplyLayerMutation(() =>
            {
                SpriteState.Layers[index].IsVisible = isVisible;
                Layers[index].IsVisible = isVisible;
            }, rebuildLayerList: false);

            // Notify modification state changes when the active layer's visibility changes
            if (index == SpriteState.ActiveLayerIndex)
            {
                OnPropertyChanged(nameof(IsActiveLayerVisible));
                OnPropertyChanged(nameof(CanModifyActiveLayer));
                NotifyLayerModificationCommandsChanged();
            }
        }

        public void SetLayerLocked(int index, bool isLocked)
        {
            SpriteState.EnsureLayers();
            if (index < 0 || index >= SpriteState.Layers.Count) return;
            if (SpriteState.Layers[index].IsLocked == isLocked) return;

            // If locking the active layer, commit any floating selection first
            if (isLocked && index == SpriteState.ActiveLayerIndex && _selectionService.IsFloating)
            {
                _selectionInput.CommitIfActive();
            }

            SaveStateForUndo();
            SpriteState.Layers[index].IsLocked = isLocked;
            Layers[index].IsLocked = isLocked;

            // Notify modification state changes only when the active layer's lock state changes
            if (index == SpriteState.ActiveLayerIndex)
            {
                OnPropertyChanged(nameof(IsActiveLayerLocked));
                OnPropertyChanged(nameof(CanModifyActiveLayer));
                NotifyLayerModificationCommandsChanged();
            }
        }

        public bool TrySetLayerGlobal(int index, bool value)
        {
            SpriteState.EnsureLayers();
            if (index < 0 || index >= SpriteState.Layers.Count) return false;
            if (SpriteState.Layers[index].IsGlobal == value) return true;

            if (_selectionService.IsFloating && index == SpriteState.ActiveLayerIndex)
                _selectionInput.CommitIfActive(saveHistory: false);

            if (value)
            {
                if (SpriteState.Frames == null || SpriteState.Frames.Count == 0) return false;
                int sourceFrame = Math.Clamp(SpriteState.ActiveFrameIndex, 0, SpriteState.Frames.Count - 1);
                var sourceFrameState = SpriteState.Frames[sourceFrame];
                if (sourceFrameState.LayerPixels == null || index < 0 || index >= sourceFrameState.LayerPixels.Count) return false;
                var source = sourceFrameState.LayerPixels[index];
                bool differs = SpriteState.Frames.Exists(frame => frame.LayerPixels != null && index < frame.LayerPixels.Count && !PixelBuffersEqual(source, frame.LayerPixels[index]));
                if (differs && SpriteState.Frames.Count > 1 &&
                    !_dialogService.ShowConfirmation(
                        "Make this layer global using the current frame? Its current content will appear on every animation frame. You can restore the previous frame contents when localizing.",
                        "Make Global Layer"))
                {
                    if (index < Layers.Count)
                        Layers[index].IsGlobal = false;
                    return false;
                }

                ApplyLayerMutation(() =>
                {
                    SpriteState.GlobalizeLayer(index, sourceFrame);
                    Layers[index].IsGlobal = true;
                }, rebuildLayerList: false);
                ShowStatus("Layer is now global across all frames");
                return true;
            }

            bool canRestore = SpriteState.Layers[index].PreGlobalFramePixels is { Count: > 0 } backups &&
                backups.Count == SpriteState.Frames.Count;
            var mode = _dialogService.ShowGlobalLayerLocalizeDialog(canRestore);
            if (!mode.HasValue)
            {
                if (index < Layers.Count)
                    Layers[index].IsGlobal = true;
                return false;
            }

            ApplyLayerMutation(() =>
            {
                SpriteState.LocalizeLayer(index, mode.Value);
                Layers[index].IsGlobal = false;
            }, rebuildLayerList: false);
            // Localizing can restore different content in every frame. The layer is
            // no longer global by the time ApplyLayerMutation marks the document
            // stale, so explicitly invalidate the entire thumbnail strip here.
            RefreshFrameThumbnails(Enumerable.Range(0, SpriteState.Frames.Count));
            ShowStatus(mode.Value == GlobalLayerLocalizeMode.RestorePreviousContent
                ? "Layer restored as frame-local content"
                : "Layer copied to independent frame content");
            return true;
        }

        // Retain the old name for callers outside the Layers panel while routing
        // all interactive toggles through the guarded conversion workflow.
        public void SetLayerGlobal(int index, bool value) => TrySetLayerGlobal(index, value);

        private static bool PixelBuffersEqual(IPixelBuffer left, IPixelBuffer right)
        {
            if (left is OverflowPixelBuffer leftOverflow && right is OverflowPixelBuffer rightOverflow)
            {
                return leftOverflow.ExtendedWidth == rightOverflow.ExtendedWidth &&
                    leftOverflow.ExtendedHeight == rightOverflow.ExtendedHeight &&
                    leftOverflow.MarginX == rightOverflow.MarginX &&
                    leftOverflow.MarginY == rightOverflow.MarginY &&
                    leftOverflow.GetExtendedData().SequenceEqual(rightOverflow.GetExtendedData());
            }

            if (left.PreserveOverflow != right.PreserveOverflow)
                return false;
            return left.GetMonochromeData().SequenceEqual(right.GetMonochromeData());
        }

        public void SetLayerExcludeFromExport(int index, bool value)
        {
            SpriteState.EnsureLayers();
            if (index < 0 || index >= SpriteState.Layers.Count) return;
            if (SpriteState.Layers[index].ExcludeFromExport == value) return;
            ApplyLayerMutation(() =>
            {
                SpriteState.Layers[index].ExcludeFromExport = value;
                Layers[index].ExcludeFromExport = value;
            }, rebuildLayerList: false);
        }

        public void SetLayerOverflow(int index, bool value)
        {
            SpriteState.EnsureLayers();
            if (index < 0 || index >= SpriteState.Layers.Count) return;
            if (SpriteState.Layers[index].PreserveOverflow == value) return;
            ApplyLayerMutation(() =>
            {
                SpriteState.Layers[index].PreserveOverflow = value;
                Layers[index].PreserveOverflow = value;

                // Update the buffers across all frames for this layer
                foreach (var frame in SpriteState.Frames)
                {
                    var oldBuffer = frame.LayerPixels[index];
                    if (value && oldBuffer is not OverflowPixelBuffer)
                    {
                        var newBuffer = new Core.OverflowPixelBuffer(SpriteState.Width, SpriteState.Height);
                        newBuffer.WriteMonochromeData(oldBuffer.GetMonochromeData());
                        frame.LayerPixels[index] = newBuffer;
                    }
                    else if (!value && oldBuffer is Core.OverflowPixelBuffer)
                    {
                        var newBuffer = new Core.MonochromePixelBuffer(SpriteState.Width * SpriteState.Height);
                        newBuffer.WriteMonochromeData(oldBuffer.GetMonochromeData());
                        frame.LayerPixels[index] = newBuffer;
                    }
                }
                
                // Active layer pixels may have changed instance
                if (index == SpriteState.ActiveLayerIndex)
                {
                    SpriteState.Pixels = SpriteState.Frames[SpriteState.ActiveFrameIndex].LayerPixels[index].GetMonochromeData();
                }
            }, rebuildLayerList: false);
        }

        public bool HasSelectedLayer => SpriteState != null && _selectedLayerIndex >= 0 && _selectedLayerIndex < SpriteState.Layers.Count;

        public Core.LayerBlendMode ActiveLayerBlendMode
        {
            get => SpriteState != null && _selectedLayerIndex >= 0 && _selectedLayerIndex < SpriteState.Layers.Count
                ? SpriteState.Layers[_selectedLayerIndex].BlendMode
                : Core.LayerBlendMode.Normal;
            set
            {
                if (SpriteState != null && _selectedLayerIndex >= 0 && _selectedLayerIndex < SpriteState.Layers.Count)
                {
                    SetLayerBlendMode(_selectedLayerIndex, value);
                }
            }
        }

        public Core.LayerOpacityMode ActiveLayerOpacityMode
        {
            get => SpriteState != null && _selectedLayerIndex >= 0 && _selectedLayerIndex < SpriteState.Layers.Count
                ? SpriteState.Layers[_selectedLayerIndex].OpacityMode
                : Core.LayerOpacityMode.Solid;
            set
            {
                if (SpriteState != null && _selectedLayerIndex >= 0 && _selectedLayerIndex < SpriteState.Layers.Count)
                {
                    SetLayerOpacityMode(_selectedLayerIndex, value);
                }
            }
        }

        public void SetLayerBlendMode(int index, Core.LayerBlendMode value)
        {
            SpriteState.EnsureLayers();
            if (index < 0 || index >= SpriteState.Layers.Count) return;
            if (SpriteState.Layers[index].BlendMode == value) return;
            ApplyLayerMutation(() =>
            {
                SpriteState.Layers[index].BlendMode = value;
                Layers[index].BlendMode = value;
                if (index == _selectedLayerIndex) OnPropertyChanged(nameof(ActiveLayerBlendMode));
            }, rebuildLayerList: false);
        }

        public void SetLayerOpacityMode(int index, Core.LayerOpacityMode value)
        {
            SpriteState.EnsureLayers();
            if (index < 0 || index >= SpriteState.Layers.Count) return;
            if (SpriteState.Layers[index].OpacityMode == value) return;
            ApplyLayerMutation(() =>
            {
                SpriteState.Layers[index].OpacityMode = value;
                Layers[index].OpacityMode = value;
                if (index == _selectedLayerIndex) OnPropertyChanged(nameof(ActiveLayerOpacityMode));
            }, rebuildLayerList: false);
        }

        public static Core.LayerBlendMode[] LayerBlendModes => Enum.GetValues<Core.LayerBlendMode>();
        public static Core.LayerOpacityMode[] LayerOpacityModes =>
        [
            Core.LayerOpacityMode.Solid,
            Core.LayerOpacityMode.Dense,
            Core.LayerOpacityMode.Checkerboard,
            Core.LayerOpacityMode.Sparse,
        ];

        [RelayCommand]
        private void SetLayerBlendModeFromItem(LayerItemViewModel layer)
        {
            if (layer == null) return;
            int index = Layers.IndexOf(layer);
            if (index >= 0)
                SetLayerBlendMode(index, layer.BlendMode);
        }

        [RelayCommand]
        private void SetLayerOpacityModeFromItem(LayerItemViewModel layer)
        {
            if (layer == null) return;
            int index = Layers.IndexOf(layer);
            if (index >= 0)
                SetLayerOpacityMode(index, layer.OpacityMode);
        }

        /// <summary>
        /// Resizes the canvas to <paramref name="newW"/>×<paramref name="newH"/>,
        /// preserving existing pixel data positioned according to the specified
        /// <paramref name="anchor"/>. Pixels falling outside the new bounds are cropped.
        /// </summary>
        private static IPixelBuffer ResizeGlobalBackup(IPixelBuffer source, int oldW, int oldH, int newW, int newH, int dx, int dy)
        {
            if (source is OverflowPixelBuffer overflow)
                return overflow.ResizeCanvas(newW, newH, dx, dy);

            var oldPixels = source.GetMonochromeData();
            var resized = new bool[newW * newH];
            for (int y = 0; y < oldH; y++)
                for (int x = 0; x < oldW; x++)
                {
                    int nx = x + dx;
                    int ny = y + dy;
                    if (nx >= 0 && nx < newW && ny >= 0 && ny < newH)
                        resized[ny * newW + nx] = oldPixels[y * oldW + x];
                }
            return new MonochromePixelBuffer(resized);
        }

        private IPixelBuffer RotateGlobalBackup(IPixelBuffer source, int oldW, int oldH, int newW, int newH, RotationDirection direction)
        {
            if (source is OverflowPixelBuffer overflow)
            {
                var pixels = _drawingService.RotatePixels(overflow.GetExtendedData(), overflow.ExtendedWidth, overflow.ExtendedHeight, direction);
                int marginX = direction == RotationDirection.Clockwise90
                    ? overflow.ExtendedHeight - overflow.MarginY - oldH
                    : direction == RotationDirection.CounterClockwise90
                        ? overflow.MarginY
                        : overflow.ExtendedWidth - overflow.MarginX - oldW;
                int marginY = direction == RotationDirection.Clockwise90
                    ? overflow.MarginX
                    : direction == RotationDirection.CounterClockwise90
                        ? overflow.ExtendedWidth - overflow.MarginX - oldW
                        : overflow.ExtendedHeight - overflow.MarginY - oldH;
                int extW = direction == RotationDirection.OneEighty ? overflow.ExtendedWidth : overflow.ExtendedHeight;
                int extH = direction == RotationDirection.OneEighty ? overflow.ExtendedHeight : overflow.ExtendedWidth;
                return new OverflowPixelBuffer(pixels, newW, newH, extW, extH, marginX, marginY);
            }
            return new MonochromePixelBuffer(_drawingService.RotatePixels(source.GetMonochromeData(), oldW, oldH, direction));
        }

        private IPixelBuffer FlipGlobalBackup(IPixelBuffer source, int canvasW, int canvasH, FlipDirection direction)
        {
            if (source is OverflowPixelBuffer overflow)
            {
                var pixels = _drawingService.FlipPixels(overflow.GetExtendedData(), overflow.ExtendedWidth, overflow.ExtendedHeight, direction);
                int marginX = direction == FlipDirection.Horizontal
                    ? overflow.ExtendedWidth - overflow.MarginX - canvasW
                    : overflow.MarginX;
                int marginY = direction == FlipDirection.Vertical
                    ? overflow.ExtendedHeight - overflow.MarginY - canvasH
                    : overflow.MarginY;
                return new OverflowPixelBuffer(pixels, canvasW, canvasH, overflow.ExtendedWidth, overflow.ExtendedHeight, marginX, marginY);
            }
            return new MonochromePixelBuffer(_drawingService.FlipPixels(source.GetMonochromeData(), canvasW, canvasH, direction));
        }

        public void ResizeCanvas(int newW, int newH, ResizeAnchor anchor)
        {
            var oldState = SpriteState;
            oldState.EnsureLayers();
            int oldW = oldState.Width;
            int oldH = oldState.Height;

            // Commit any floating selection into the canvas before resizing.
            // The committed result is then included in the undo snapshot below.
            if (_selectionService.IsFloating)
                _selectionService.CommitSelection(oldState, _floatingPasteMode);
            _selectionService.Cancel();

            // Push current state for undo
            oldState.SelectionSnapshot = _selectionService.CreateSnapshot();
            _historyService.SaveState(oldState);
            IsDirty = true;

            // Compute where the old content should be placed in the new canvas
            var (dx, dy) = ComputeAnchorOffset(oldW, oldH, newW, newH, anchor);

            // Create the new state and copy pixels
            var newState = new SpriteState(newW, newH)
            {
                IsDisplayInverted = oldState.IsDisplayInverted,
                ExportSettings = oldState.ExportSettings,
                IsAnimationEnabled = oldState.IsAnimationEnabled,
                FrameRateFps = oldState.FrameRateFps,
                PlaybackDirection = oldState.PlaybackDirection,
            };
            newState.Frames.Clear();
            newState.Layers.Clear();

            foreach (var oldLayer in oldState.Layers)
            {
                newState.Layers.Add(oldLayer.Clone());
            }

            foreach (var oldFrame in oldState.Frames)
            {
                var newFrame = new FrameState
                {
                    Name = oldFrame.Name,
                    DelayMultiplier = oldFrame.DelayMultiplier,
                    LayerPixels = [],
                };
                for (int i = 0; i < oldFrame.LayerPixels.Count; i++)
                {
                    var oldBuffer = oldFrame.LayerPixels[i];
                    if (oldBuffer is Hexprite.Core.OverflowPixelBuffer oldOvf)
                    {
                        var newOvf = oldOvf.ResizeCanvas(newW, newH, dx, dy);
                        newFrame.LayerPixels.Add(newOvf);
                    }
                    else
                    {
                        var oldPixels = oldBuffer.GetMonochromeData();
                        var newPixels = new bool[newW * newH];

                        for (int y = 0; y < oldH; y++)
                        {
                            for (int x = 0; x < oldW; x++)
                            {
                                int ny = y + dy;
                                int nx = x + dx;
                                if (nx >= 0 && nx < newW && ny >= 0 && ny < newH)
                                {
                                    newPixels[(ny * newW) + nx] = oldPixels[(y * oldW) + x];
                                }
                            }
                        }
                        newFrame.LayerPixels.Add(new Hexprite.Core.MonochromePixelBuffer(newPixels));
                    }
                }
                newState.Frames.Add(newFrame);
            }
            for (int i = 0; i < oldState.Layers.Count; i++)
            {
                var backups = oldState.Layers[i].PreGlobalFramePixels;
                if (backups != null)
                    newState.Layers[i].PreGlobalFramePixels = backups
                        .ConvertAll(buffer => ResizeGlobalBackup(buffer, oldW, oldH, newW, newH, dx, dy));
            }
            newState.ActiveFrameIndex = oldState.ActiveFrameIndex;
            newState.EnsureLayers();

            // Re-initialize bitmaps for the new size and apply the new state
            SpriteState = newState;
            UpdateOnionSkinCache();
            RebuildBitmaps(newW, newH);

            // Clamp or recenter symmetry axes if they fall outside new bounds
            if (SymmetryAxisX < 0 || SymmetryAxisX >= newW)
                SymmetryAxisX = newW / 2.0;
            if (SymmetryAxisY < 0 || SymmetryAxisY >= newH)
                SymmetryAxisY = newH / 2.0;

            RedrawGridFromMemory();
            MarkCodeStale();
            NotifyCanvasLayoutChanged();
        }

        /// <summary>
        /// Rotates the entire canvas (all layers) by 90° CW/CCW or 180°. Width and height swap for 90° rotations.
        /// </summary>
        public async System.Threading.Tasks.Task RotateCanvasAsync(RotationDirection dir)
        {
            if (IsProcessing) return;
            IsProcessing = true;
            StatusMessage = "Rotating...";

            try
            {
                var oldState = SpriteState;
            oldState.EnsureLayers();
            int oldW = oldState.Width;
            int oldH = oldState.Height;

            if (_selectionService.IsFloating)
                _selectionService.CommitSelection(oldState, _floatingPasteMode);
            _selectionService.Cancel();

            oldState.SelectionSnapshot = _selectionService.CreateSnapshot();
            _historyService.SaveState(oldState);
            IsDirty = true;

            bool swapDims = dir is RotationDirection.Clockwise90 or RotationDirection.CounterClockwise90;
            int newW = swapDims ? oldH : oldW;
            int newH = swapDims ? oldW : oldH;

            var newState = new SpriteState(newW, newH)
            {
                IsDisplayInverted = oldState.IsDisplayInverted,
                ExportSettings = oldState.ExportSettings,
                IsAnimationEnabled = oldState.IsAnimationEnabled,
                FrameRateFps = oldState.FrameRateFps,
                PlaybackDirection = oldState.PlaybackDirection,
            };
            newState.Frames.Clear();
            newState.Layers.Clear();
            foreach (var oldLayer in oldState.Layers)
            {
                var newLayer = oldLayer.Clone();
                newLayer.Pixels = null;
                newState.Layers.Add(newLayer);
            }

            await System.Threading.Tasks.Task.Run(() =>
            {
                foreach (var oldFrame in oldState.Frames)
                {
                    var newFrame = new FrameState
                    {
                        Name = oldFrame.Name,
                        DelayMultiplier = oldFrame.DelayMultiplier,
                        LayerPixels = [],
                    };

                    for (int i = 0; i < oldState.Layers.Count; i++)
                    {
                        var oldBuffer = oldFrame.LayerPixels[i];
                        if (oldBuffer is Hexprite.Core.OverflowPixelBuffer oldOvf)
                        {
                            var rotatedExtended = _drawingService.RotatePixels(oldOvf.GetExtendedData(), oldOvf.ExtendedWidth, oldOvf.ExtendedHeight, dir);
                            
                            int newExtW = dir == Hexprite.Core.RotationDirection.OneEighty ? oldOvf.ExtendedWidth : oldOvf.ExtendedHeight;
                            int newExtH = dir == Hexprite.Core.RotationDirection.OneEighty ? oldOvf.ExtendedHeight : oldOvf.ExtendedWidth;
                            
                            int newMarginX = 0;
                            int newMarginY = 0;
                            switch (dir)
                            {
                                case Hexprite.Core.RotationDirection.Clockwise90:
                                    newMarginX = oldOvf.ExtendedHeight - oldOvf.MarginY - oldH;
                                    newMarginY = oldOvf.MarginX;
                                    break;
                                case Hexprite.Core.RotationDirection.CounterClockwise90:
                                    newMarginX = oldOvf.MarginY;
                                    newMarginY = oldOvf.ExtendedWidth - oldOvf.MarginX - oldW;
                                    break;
                                case Hexprite.Core.RotationDirection.OneEighty:
                                    newMarginX = oldOvf.ExtendedWidth - oldOvf.MarginX - oldW;
                                    newMarginY = oldOvf.ExtendedHeight - oldOvf.MarginY - oldH;
                                    break;
                            }

                            var newOvf = new Hexprite.Core.OverflowPixelBuffer(rotatedExtended, newW, newH, newExtW, newExtH, newMarginX, newMarginY);
                            newFrame.LayerPixels.Add(newOvf);
                        }
                        else
                        {
                            var rotatedPixels = _drawingService.RotatePixels(oldBuffer.GetMonochromeData(), oldW, oldH, dir);
                            newFrame.LayerPixels.Add(new Hexprite.Core.MonochromePixelBuffer(rotatedPixels));
                        }
                    }
                    newState.Frames.Add(newFrame);
                }

                for (int i = 0; i < oldState.Layers.Count; i++)
                {
                    var backups = oldState.Layers[i].PreGlobalFramePixels;
                    if (backups != null)
                        newState.Layers[i].PreGlobalFramePixels = backups.ConvertAll(buffer =>
                            RotateGlobalBackup(buffer, oldW, oldH, newW, newH, dir));
                }
            }, CancellationToken.None);

            newState.ActiveFrameIndex = oldState.ActiveFrameIndex;
            newState.EnsureLayers();

            SpriteState = newState;
            UpdateOnionSkinCache();
            RebuildBitmaps(newW, newH);

            RedrawGridFromMemory();
            MarkCodeStale();
            NotifyCanvasLayoutChanged();
            }
            finally
            {
                IsProcessing = false;
                StatusMessage = "";
            }
        }

        public async System.Threading.Tasks.Task FlipCanvasAsync(FlipDirection dir)
        {
            if (IsProcessing) return;
            IsProcessing = true;
            StatusMessage = "Flipping...";

            try
            {
                var oldState = SpriteState;
            oldState.EnsureLayers();

            // Match Resize/Rotate UX: if there is a floating selection, commit it
            // to the canvas before flipping everything.
            if (_selectionService.IsFloating)
                _selectionService.CommitSelection(oldState, _floatingPasteMode);
            _selectionService.Cancel();

            // Push current state for undo (selection is now cleared/committed).
            oldState.SelectionSnapshot = _selectionService.CreateSnapshot();
            _historyService.SaveState(oldState);
            IsDirty = true;

            int w = oldState.Width;
            int h = oldState.Height;

            var newState = new SpriteState(w, h)
            {
                IsDisplayInverted = oldState.IsDisplayInverted,
                ExportSettings = oldState.ExportSettings,
                IsAnimationEnabled = oldState.IsAnimationEnabled,
                FrameRateFps = oldState.FrameRateFps,
            };
            newState.Frames.Clear();
            newState.Layers.Clear();
            foreach (var oldLayer in oldState.Layers)
            {
                var newLayer = oldLayer.Clone();
                newLayer.Pixels = null;
                newState.Layers.Add(newLayer);
            }

            await System.Threading.Tasks.Task.Run(() =>
            {
                foreach (var oldFrame in oldState.Frames)
                {
                    var newFrame = new FrameState
                    {
                        Name = oldFrame.Name,
                        DelayMultiplier = oldFrame.DelayMultiplier,
                        LayerPixels = [],
                    };

                    for (int i = 0; i < oldState.Layers.Count; i++)
                    {
                        var oldBuffer = oldFrame.LayerPixels[i];
                        if (oldBuffer is Hexprite.Core.OverflowPixelBuffer oldOvf)
                        {
                            var flippedExtended = _drawingService.FlipPixels(oldOvf.GetExtendedData(), oldOvf.ExtendedWidth, oldOvf.ExtendedHeight, dir);
                            
                            int newMarginX = dir == Hexprite.Core.FlipDirection.Horizontal 
                                ? oldOvf.ExtendedWidth - oldOvf.MarginX - w 
                                : oldOvf.MarginX;
                                
                            int newMarginY = dir == Hexprite.Core.FlipDirection.Vertical 
                                ? oldOvf.ExtendedHeight - oldOvf.MarginY - h 
                                : oldOvf.MarginY;

                            var newOvf = new Hexprite.Core.OverflowPixelBuffer(flippedExtended, w, h, oldOvf.ExtendedWidth, oldOvf.ExtendedHeight, newMarginX, newMarginY);
                            newFrame.LayerPixels.Add(newOvf);
                        }
                        else
                        {
                            var flippedPixels = _drawingService.FlipPixels(oldBuffer.GetMonochromeData(), w, h, dir);
                            newFrame.LayerPixels.Add(new Hexprite.Core.MonochromePixelBuffer(flippedPixels));
                        }
                    }
                    newState.Frames.Add(newFrame);
                }

                for (int i = 0; i < oldState.Layers.Count; i++)
                {
                    var backups = oldState.Layers[i].PreGlobalFramePixels;
                    if (backups != null)
                        newState.Layers[i].PreGlobalFramePixels = backups.ConvertAll(buffer =>
                            FlipGlobalBackup(buffer, w, h, dir));
                }
            }, CancellationToken.None);

            newState.ActiveFrameIndex = oldState.ActiveFrameIndex;
            newState.EnsureLayers();

            SpriteState = newState;
            UpdateOnionSkinCache();
            RebuildBitmaps(w, h);

            RedrawGridFromMemory();
            MarkCodeStale();
            NotifyCanvasLayoutChanged();
            }
            finally
            {
                IsProcessing = false;
                StatusMessage = "";
            }
        }

        public void FlipSelection(FlipDirection dir)
        {
            if (!CanModifyActiveLayer)
            {
                ShowStatus("Cannot modify: layer is locked, hidden, or multiple layers selected.", 3000);
                return;
            }
            if (!_selectionService.HasActiveSelection || !_selectionService.IsFloating)
                return;
            if (_selectionService.FloatingPixels == null)
                return;

            // Ensure undo captures the current floating pixels.
            SaveStateForUndo();

            if (dir == FlipDirection.Horizontal)
                _selectionService.FlipFloatingHorizontally();
            else if (dir == FlipDirection.Vertical)
                _selectionService.FlipFloatingVertically();

            RedrawGridFromMemory();
        }

        /// <summary>
        /// Computes the pixel offset at which the old content should be placed
        /// within the new canvas, based on the anchor position.
        /// </summary>
        /// <remarks>
        /// Center anchors use C# integer division (truncates toward zero), which
        /// is equivalent to <c>Math.Floor</c> for positive deltas. This is
        /// intentional: when the size change is an odd number of pixels and the
        /// space cannot be split evenly, the extra pixel(s) always go to the
        /// <strong>right/bottom</strong> — matching the convention used by
        /// Aseprite, Photoshop, and other raster editors.
        /// Do NOT change to ceiling division; that would flip the bias to the
        /// left/top, which is non-standard for a pixel art editor.
        /// </remarks>
        private static (int offsetX, int offsetY) ComputeAnchorOffset(
            int oldW, int oldH, int newW, int newH, ResizeAnchor anchor)
        {
            int dx = newW - oldW;
            int dy = newH - oldH;

            return anchor switch
            {
                ResizeAnchor.TopLeft => (0, 0),
                ResizeAnchor.TopCenter => (dx / 2, 0),
                ResizeAnchor.TopRight => (dx, 0),
                ResizeAnchor.CenterLeft => (0, dy / 2),
                ResizeAnchor.Center => (dx / 2, dy / 2),
                ResizeAnchor.CenterRight => (dx, dy / 2),
                ResizeAnchor.BottomLeft => (0, dy),
                ResizeAnchor.BottomCenter => (dx / 2, dy),
                ResizeAnchor.BottomRight => (dx, dy),
                _ => (0, 0),
            };
        }
    }
}
