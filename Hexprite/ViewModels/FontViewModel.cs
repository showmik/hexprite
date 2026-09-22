using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hexprite.Core;
using Hexprite.Rendering;
using Hexprite.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hexprite.ViewModels
{
    /// <summary>
    /// Central orchestrator for Font Mode.
    /// Manages the font document, active glyph, undo/redo, and rendering state.
    /// </summary>
    public class FontViewModel : ObservableObject, IDocumentTab, IDisposable
    {
        private FontDocument _document = null!;
        private readonly IAutosaveService? _autosaveService;

        /// <summary>
        /// The font document being edited.
        /// </summary>
        public event EventHandler? DocumentLoaded;

        public FontDocument Document
        {
            get => _document;
            set
            {
                if (SetProperty(ref _document, value))
                {
                    _undoStack.Clear();
                    _redoStack.Clear();
                    _nextRevisionId = 0;
                    _currentRevisionId = 0;
                    _savedRevisionId = 0;
                    IsDirty = false;
                    IsNewlyCreated = false;
                    
                    RebuildGlyphMap();
                    SetActiveGlyph(value.ActiveGlyphIndex);
                    
                    OnPropertyChanged(nameof(Baseline));
                    OnPropertyChanged(nameof(YAdvance));
                    OnPropertyChanged(nameof(FontName));
                    OnPropertyChanged(nameof(IsMonospaced));
                    OnPropertyChanged(nameof(CellHeight));
                    OnPropertyChanged(nameof(MaxCellWidth));
                    OnPropertyChanged(nameof(CanUndo));
                    OnPropertyChanged(nameof(CanRedo));
                    (UndoCommand as RelayCommand)?.NotifyCanExecuteChanged();
                    (RedoCommand as RelayCommand)?.NotifyCanExecuteChanged();
                    UpdateEstimatedFlashBytes();
                    
                    DocumentLoaded?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public FontViewModel(IAutosaveService? autosaveService = null)
        {
            // Initialize with a default document to prevent null reference but don't trigger events
            _document = FontDocument.CreateNew(8, 8);
            _nextRevisionId = 0;
            _currentRevisionId = 0;
            _savedRevisionId = 0;

            RebuildGlyphMap();
            SetActiveGlyph(0);
            
            ImportFontCommand = new RelayCommand(ExecuteImportFont);
            ExportFontCommand = new RelayCommand(ExecuteExportFont);
            UndoCommand = new RelayCommand(Undo);
            RedoCommand = new RelayCommand(Redo);
            
            ScrubStartedCommand = new RelayCommand(ExecuteScrubStarted);
            ScrubEndedCommand = new RelayCommand(ExecuteScrubEnded);
            KeyboardScrubStartedCommand = new RelayCommand(ExecuteKeyboardScrubStarted);
            KeyboardScrubEndedCommand = new RelayCommand(ExecuteKeyboardScrubEnded);

            NudgeLeftCommand = new RelayCommand(() => ShiftActiveGlyph(-1, 0));
            NudgeRightCommand = new RelayCommand(() => ShiftActiveGlyph(1, 0));
            NudgeUpCommand = new RelayCommand(() => ShiftActiveGlyph(0, -1));
            NudgeDownCommand = new RelayCommand(() => ShiftActiveGlyph(0, 1));
            ClearFilterCommand = new RelayCommand(() => GlyphFilter = string.Empty);
            SetPreviewPresetCommand = new RelayCommand<string>(ExecuteSetPreviewPreset);
            CopyPreviewTextCommand = new RelayCommand(ExecuteCopyPreviewText);

            _autosaveService = autosaveService;
            if (_autosaveService != null)
            {
                string documentId = Guid.NewGuid().ToString();
                _autosaveService.StartFontAutosaveLoop(
                    documentId,
                    () => _document?.Clone(),
                    () => IsDirty,
                    () => new AutosaveMetadata { Title = Title, FilePath = FilePath, IsActiveTab = IsActive });
            }
        }

        public bool IsNewlyCreated { get; set; } = true;

        private bool _isScrubbing;
        public bool IsScrubbing
        {
            get => _isScrubbing;
            set => SetProperty(ref _isScrubbing, value);
        }

        /// <summary>
        /// When true, SaveStateForUndo() calls are suppressed.
        /// Used during Undo/Redo to prevent Keyboard.ClearFocus() from
        /// triggering LostFocus bindings that would corrupt the undo stack.
        /// </summary>
        private bool _isSuppressingUndoSaves;

        /// <summary>
        /// When true, arrow key increments are being batched into a single undo entry.
        /// </summary>
        private bool _isKeyboardScrubbing;

        public IRelayCommand ScrubStartedCommand { get; }
        public IRelayCommand ScrubEndedCommand { get; }
        public IRelayCommand KeyboardScrubStartedCommand { get; }
        public IRelayCommand KeyboardScrubEndedCommand { get; }

        private void ExecuteScrubStarted()
        {
            if (!_isScrubbing)
            {
                // End any in-progress keyboard scrub first
                if (_isKeyboardScrubbing) _isKeyboardScrubbing = false;
                SaveStateForUndo();
                IsScrubbing = true;
            }
        }

        private void ExecuteScrubEnded()
        {
            IsScrubbing = false;
            IsDirty = (_currentRevisionId != _savedRevisionId);
        }

        private void ExecuteKeyboardScrubStarted()
        {
            if (!_isKeyboardScrubbing && !_isScrubbing)
            {
                SaveStateForUndo();
                _isKeyboardScrubbing = true;
            }
        }

        private void ExecuteKeyboardScrubEnded()
        {
            _isKeyboardScrubbing = false;
            IsDirty = (_currentRevisionId != _savedRevisionId);
        }

        public IRelayCommand ImportFontCommand { get; }
        public IRelayCommand ExportFontCommand { get; }
        public IRelayCommand UndoCommand { get; }
        public IRelayCommand RedoCommand { get; }

        public IRelayCommand NudgeLeftCommand { get; }
        public IRelayCommand NudgeRightCommand { get; }
        public IRelayCommand NudgeUpCommand { get; }
        public IRelayCommand NudgeDownCommand { get; }
        public IRelayCommand ClearFilterCommand { get; }
        public IRelayCommand<string> SetPreviewPresetCommand { get; }
        public IRelayCommand CopyPreviewTextCommand { get; }

        private void ExecuteSetPreviewPreset(string? text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                PreviewText = text;
            }
        }

        private void ExecuteCopyPreviewText()
        {
            try
            {
                if (!string.IsNullOrEmpty(PreviewText))
                {
                    Clipboard.SetText(PreviewText);
                }
            }
            catch
            {
                // Ignore transient clipboard access errors
            }
        }

        private void ExecuteImportFont()
        {
            try
            {
                if (Application.Current is not App app) return;

                if (app.Services.GetService(typeof(Hexprite.Services.IFontImportService)) is not Hexprite.Services.IFontImportService importService) return;

                // Show a font import dialog
                var dialog = new Views.FontImportDialog(importService)
                {
                    Owner = Application.Current.MainWindow,
                };
                if (dialog.ShowDialog() == true)
                {
                    Hexprite.Core.FontDocument? imported = null;

                    var options = new Hexprite.Services.FontImportOptions
                    {
                        TargetHeight = dialog.SelectedHeight,
                        FirstChar = dialog.SelectedFirstChar,
                        LastChar = dialog.SelectedLastChar,
                        BaselineOffset = dialog.BaselineOffset,
                        LetterSpacing = dialog.LetterSpacing,
                        Threshold = dialog.Threshold,
                        AntiAlias = dialog.AntiAlias,
                    };

                    switch (dialog.SelectedMode)
                    {
                        case Views.FontImportMode.System:
                            imported = importService.ImportFromTrueType(
                                dialog.SelectedFontFamily,
                                options);
                            imported.FontName = dialog.SelectedFontFamily.Source.Replace(" ", "", StringComparison.Ordinal);
                            break;

                        case Views.FontImportMode.File:
                            imported = importService.ImportFromFontFile(
                                dialog.SelectedFilePath,
                                options);
                            break;

                        case Views.FontImportMode.Sprite:
                            imported = importService.ImportFromSpriteSheet(
                                dialog.SelectedFilePath,
                                dialog.SpriteCellWidth,
                                dialog.SpriteCellHeight,
                                options);
                            break;

                        case Views.FontImportMode.BMFont:
                            imported = importService.ImportFromBMFont(dialog.SelectedFilePath, options);
                            break;
                    }

                    if (imported != null)
                    {
                        Document = imported;
                        IsDirty = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Hexprite.Views.MessageDialog.Show($"Import failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExecuteExportFont()
        {
            try
            {
                if (_document == null) return;

                var oldFormat = _document.ExportSettings?.Format;
                var oldFontName = _document.FontName;
                var oldSettingsFontName = _document.ExportSettings?.FontName;
                var oldGlyphPreview = _document.ExportSettings?.IncludeGlyphPreview;
                var oldMetricComments = _document.ExportSettings?.IncludeMetricComments;
                var oldUsageComment = _document.ExportSettings?.IncludeUsageComment;
                var oldUppercaseHex = _document.ExportSettings?.UppercaseHex;

                var service = (Application.Current as App)?.Services.GetService(typeof(Hexprite.Services.IFontCodeGeneratorService)) as Hexprite.Services.IFontCodeGeneratorService
                              ?? new Hexprite.Services.FontCodeGeneratorService();

                var dialog = new Hexprite.Views.FontExportDialog(_document, service)
                {
                    Owner = Application.Current?.MainWindow,
                    InitialDirectory = !string.IsNullOrWhiteSpace(_filePath) ? System.IO.Path.GetDirectoryName(_filePath) : null
                };
                dialog.ShowDialog();

                if (_document.ExportSettings?.Format != oldFormat ||
                    _document.FontName != oldFontName ||
                    _document.ExportSettings?.FontName != oldSettingsFontName ||
                    _document.ExportSettings?.IncludeGlyphPreview != oldGlyphPreview ||
                    _document.ExportSettings?.IncludeMetricComments != oldMetricComments ||
                    _document.ExportSettings?.IncludeUsageComment != oldUsageComment ||
                    _document.ExportSettings?.UppercaseHex != oldUppercaseHex)
                {
                    IsDirty = true;
                }

                UpdateEstimatedFlashBytes();
                OnPropertyChanged(nameof(ExportFormat));
                OnPropertyChanged(nameof(IncludeGlyphPreview));
                OnPropertyChanged(nameof(IncludeMetricComments));
                OnPropertyChanged(nameof(UppercaseHex));
            }
            catch (Exception ex)
            {
                Hexprite.Views.MessageDialog.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── IDocumentTab Implementation ───────────────────────────────────────
        
        private string? _filePath;
        public string? FilePath
        {
            get => _filePath;
            set { if (SetProperty(ref _filePath, value)) OnPropertyChanged(nameof(Title)); }
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
                    OnPropertyChanged(nameof(HasUnsavedChanges));
                    if (!value)
                    {
                        _autosaveService?.ClearCurrentAutosave();
                    }
                }
            }
        }

        public string Title => IsDirty ? $"*{FontName} (Font)" : $"{FontName} (Font)";
        
        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        public DocumentMode Mode => DocumentMode.Font;
        public bool HasUnsavedChanges => IsDirty;
        public bool IsLinked => false;

        private static readonly System.Text.Json.JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

        public void Save()
        {
            if (FilePath != null)
            {
                SaveToPath(FilePath);
            }
        }

        public void SaveAs(string path)
        {
            path = SafeFileIo.EnsureExtension(path, ".hexfont");
            SaveToPath(path);
        }

        private void SaveToPath(string path)
        {
            try
            {
                string json = System.Text.Json.JsonSerializer.Serialize(_document, IndentedJsonOptions);
                SafeFileIo.WriteAllTextAtomic(path, json, maxRetries: 5, createBackup: true);
                FilePath = path;
                MarkAsClean();
                Hexprite.Services.UserPreferencesService.AddRecentFile(path);
            }
            catch (Exception ex)
            {
                Hexprite.Views.MessageDialog.Show($"Save failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void MarkAsClean()
        {
            _savedRevisionId = _currentRevisionId;
            IsDirty = false;
            _autosaveService?.ClearCurrentAutosave();
        }

        public void Dispose()
        {
            _autosaveService?.StopAutosaveLoop();
            _autosaveService?.ClearCurrentAutosave();
            (_autosaveService as IDisposable)?.Dispose();
            GC.SuppressFinalize(this);
        }

        // ── Data Binding Properties ──────────────────────────────────────────

        public string FontName
        {
            get => _document.FontName;
            set
            {
                if (_document.FontName != value)
                {
                    SaveStateForUndo();
                    _document.FontName = value;
                    IsDirty = true;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Title));
                }
            }
        }

        public int Baseline
        {
            get => _document.Baseline;
            set
            {
                if (_document.Baseline != value)
                {
                    SaveStateForUndo();
                    _document.Baseline = value;
                    IsDirty = true;
                    OnPropertyChanged();
                    RedrawGlyphCanvas();
                    UpdatePreviewBitmap();
                }
            }
        }

        public int YAdvance
        {
            get => _document.YAdvance;
            set
            {
                if (_document.YAdvance != value)
                {
                    SaveStateForUndo();
                    _document.YAdvance = value;
                    IsDirty = true;
                    OnPropertyChanged();
                    UpdatePreviewBitmap();
                }
            }
        }

        public bool IsMonospaced
        {
            get => _document.IsMonospaced;
            set
            {
                if (_document.IsMonospaced != value)
                {
                    SaveStateForUndo();
                    _document.IsMonospaced = value;
                    IsDirty = true;
                    OnPropertyChanged();
                    
                    if (value)
                    {
                        // Cache current widths before forcing monospaced
                        _document.PreMonoWidths.Clear();
                        foreach (var g in _document.Glyphs)
                        {
                            _document.PreMonoWidths[g.CodePoint] = g.Width;
                        }
                        
                        // Enforce monospaced constraint — resize ALL glyphs directly
                        foreach (var g in _document.Glyphs)
                        {
                            if (g.Width != _document.MaxCellWidth)
                            {
                                InternalResizeGlyph(g, _document.MaxCellWidth);
                            }
                        }
                        
                        OnPropertyChanged(nameof(GlyphWidth));
                        InitCanvas();
                        RedrawGlyphCanvas();
                        for (int i = 0; i < _document.Glyphs.Count; i++)
                        {
                            UpdateMiniPreview(i);
                        }
                        UpdateEstimatedFlashBytes();
                    }
                    else
                    {
                        // Restore previous widths
                        if (_document.PreMonoWidths.Count > 0)
                        {
                            foreach (var g in _document.Glyphs)
                            {
                                if (_document.PreMonoWidths.TryGetValue(g.CodePoint, out int originalWidth))
                                {
                                    if (originalWidth != g.Width)
                                    {
                                        InternalResizeGlyph(g, originalWidth);
                                    }
                                }
                            }
                            
                            // Clear cache
                            _document.PreMonoWidths.Clear();
                            
                            // Re-init canvas and previews
                            OnPropertyChanged(nameof(GlyphWidth));
                            InitCanvas();
                            RedrawGlyphCanvas();
                            for (int i = 0; i < _document.Glyphs.Count; i++)
                            {
                                UpdateMiniPreview(i);
                            }
                            UpdateEstimatedFlashBytes();
                        }
                    }
                    UpdatePreviewBitmap();
                }
            }
        }

        public int CellHeight
        {
            get => _document.CellHeight;
            set
            {
                if (_document.CellHeight != value && value > 0)
                {
                    SaveStateForUndo();
                    _document.CellHeight = value;
                    IsDirty = true;
                    if (_document.Baseline > value)
                    {
                        _document.Baseline = value;
                        OnPropertyChanged(nameof(Baseline));
                    }
                    OnPropertyChanged();
                    ResizeAllGlyphsHeight(value);
                    RedrawGlyphCanvas();
                }
            }
        }

        private void ResizeAllGlyphsHeight(int newHeight)
        {
            if (_document == null || newHeight <= 0) return;

            foreach (var g in _document.Glyphs)
            {
                if (g == null) continue;
                int w = Math.Clamp(g.Width, 1, 512);
                bool[] newPixels = new bool[w * newHeight];
                if (g.Pixels != null)
                {
                    int copyHeight = Math.Min(Math.Max(0, g.Height), newHeight);
                    int copyWidth = Math.Min(w, g.Width);
                    for (int y = 0; y < copyHeight; y++)
                    {
                        for (int x = 0; x < copyWidth; x++)
                        {
                            int srcIdx = y * g.Width + x;
                            if (srcIdx < g.Pixels.Length)
                            {
                                newPixels[y * w + x] = g.Pixels[srcIdx];
                            }
                        }
                    }
                }

                g.Width = w;
                g.Height = newHeight;
                g.Pixels = newPixels;
            }
            
            InitCanvas();
            for (int i = 0; i < _document.Glyphs.Count; i++)
            {
                UpdateMiniPreview(i);
            }
            UpdateEstimatedFlashBytes();
            UpdatePreviewBitmap();
        }

        public int MaxCellWidth
        {
            get => _document.MaxCellWidth;
            set
            {
                if (_document.MaxCellWidth != value && value > 0)
                {
                    SaveStateForUndo();
                    _document.MaxCellWidth = value;
                    IsDirty = true;
                    OnPropertyChanged();
                    if (IsMonospaced)
                    {
                        ResizeActiveGlyph(value);
                    }
                    UpdateEstimatedFlashBytes();
                    UpdatePreviewBitmap();
                }
            }
        }

        // ── Active Glyph Properties ──────────────────────────────────────────

        public GlyphState? ActiveGlyph => _document?.ActiveGlyph;
        public char ActiveGlyphChar => _document?.ActiveGlyph?.Character ?? ' ';
        public int ActiveGlyphCodePoint => _document?.ActiveGlyph?.CodePoint ?? 0;

        public int GlyphWidth
        {
            get => _document?.ActiveGlyph?.Width ?? 0;
            set
            {
                if (_document?.ActiveGlyph != null && _document.ActiveGlyph.Width != value)
                {
                    ResizeActiveGlyph(value);
                }
            }
        }

        public int GlyphHeight => _document?.ActiveGlyph?.Height ?? 0;

        public int XAdvance
        {
            get => _document?.ActiveGlyph?.XAdvance ?? 0;
            set
            {
                if (_document?.ActiveGlyph != null && _document.ActiveGlyph.XAdvance != value)
                {
                    SaveStateForUndo();
                    _document.ActiveGlyph.XAdvance = value;
                    IsDirty = true;
                    OnPropertyChanged();
                    InitCanvas();
                    RedrawGlyphCanvas();
                    UpdatePreviewBitmap();
                }
            }
        }

        public int XOffset
        {
            get => _document?.ActiveGlyph?.XOffset ?? 0;
            set
            {
                if (_document?.ActiveGlyph != null && _document.ActiveGlyph.XOffset != value)
                {
                    SaveStateForUndo();
                    _document.ActiveGlyph.XOffset = value;
                    IsDirty = true;
                    OnPropertyChanged();
                    UpdatePreviewBitmap();
                }
            }
        }

        public int YOffset
        {
            get => _document?.ActiveGlyph?.YOffset ?? 0;
            set
            {
                if (_document?.ActiveGlyph != null && _document.ActiveGlyph.YOffset != value)
                {
                    SaveStateForUndo();
                    _document.ActiveGlyph.YOffset = value;
                    IsDirty = true;
                    OnPropertyChanged();
                    UpdatePreviewBitmap();
                }
            }
        }

        private bool _autoAdvance = true;
        public bool AutoAdvance
        {
            get => _autoAdvance;
            set => SetProperty(ref _autoAdvance, value);
        }

        // ── Status Bar Properties ────────────────────────────────────────

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

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "WPF data binding property")]
        public string CurrentTool => "Pencil/Eraser";

        public string CanvasDimensionText => _document?.ActiveGlyph != null ? string.Create(CultureInfo.InvariantCulture, $"{_document.ActiveGlyph.Width}×{_document.ActiveGlyph.Height}") : "";

        // CellSize 16 is "1.0x" for the status bar, since 16px is typical cell scale? 
        // Actually zoom is typically 1:1 if 1 logic pixel = 1 screen pixel. Wait, CellSize IS the zoom factor in logic pixels.
        // If cell size = 16, zoom is 16x. Let's return CellSize.
        public double ZoomLevel
        {
            get => CellSize;
            set => CellSize = (int)Math.Max(1, Math.Round(value, MidpointRounding.AwayFromZero));
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "WPF data binding property")]
        public string SelectionInfo => "";
        
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "WPF data binding property")]
        public string LayerHoverInfo => "";

        // ─────────────────────────────────────────────────────────────


        private bool _showGridLines = true;
        /// <summary>Whether to show grid lines on the glyph canvas.</summary>
        public bool ShowGridLines
        {
            get => _showGridLines;
            set { if (SetProperty(ref _showGridLines, value)) RedrawGlyphCanvas(); }
        }

        private bool _showMetricsGuides = true;
        /// <summary>Whether to show baseline, ascent, descent, and cell boundary lines.</summary>
        public bool ShowMetricsGuides
        {
            get => _showMetricsGuides;
            set { if (SetProperty(ref _showMetricsGuides, value)) RedrawGlyphCanvas(); }
        }

        private bool _showAdvanceGuide = true;
        /// <summary>Whether to show X-advance guide line.</summary>
        public bool ShowAdvanceGuide
        {
            get => _showAdvanceGuide;
            set { if (SetProperty(ref _showAdvanceGuide, value)) RedrawGlyphCanvas(); }
        }

        // ── Text Preview ─────────────────────────────────────────────────

        private string _previewText = "Hello World";
        /// <summary>Sample text for the live preview panel.</summary>
        public string PreviewText
        {
            get => _previewText;
            set { if (SetProperty(ref _previewText, value)) UpdatePreviewBitmap(); }
        }

        private DisplayType _previewDisplayType = DisplayType.GenericWhite;
        /// <summary>Simulated hardware display type for preview panel.</summary>
        public DisplayType PreviewDisplayType
        {
            get => _previewDisplayType;
            set
            {
                if (SetProperty(ref _previewDisplayType, value))
                {
                    UpdatePreviewBitmap();
                }
            }
        }

        private int _previewScale; // 0 = Auto, 1 = 1x, 2 = 2x, 4 = 4x
        /// <summary>Zoom multiplier for live preview (0 for auto-scale).</summary>
        public int PreviewScale
        {
            get => _previewScale;
            set
            {
                if (SetProperty(ref _previewScale, value))
                {
                    UpdatePreviewBitmap();
                }
            }
        }

        private bool _previewInverted;
        /// <summary>Whether to invert background and ink colors in the preview.</summary>
        public bool PreviewInverted
        {
            get => _previewInverted;
            set
            {
                if (SetProperty(ref _previewInverted, value))
                {
                    UpdatePreviewBitmap();
                }
            }
        }

        private WriteableBitmap? _previewBitmap;
        /// <summary>Rendered preview bitmap of the sample text.</summary>
        public WriteableBitmap? PreviewBitmap
        {
            get => _previewBitmap;
            private set => SetProperty(ref _previewBitmap, value);
        }

        // ── Export Settings Binding ──────────────────────────────────────

        public FontExportFormat ExportFormat
        {
            get => _document?.ExportSettings?.Format ?? FontExportFormat.AdafruitGfx;
            set
            {
                if (_document != null)
                {
                    _document.ExportSettings ??= new FontExportSettings();
                    if (_document.ExportSettings.Format != value)
                    {
                        SaveStateForUndo();
                        _document.ExportSettings.Format = value;
                        OnPropertyChanged();
                        UpdateEstimatedFlashBytes();
                    }
                }
            }
        }

        public bool IncludeGlyphPreview
        {
            get => _document?.ExportSettings?.IncludeGlyphPreview ?? true;
            set
            {
                if (_document != null)
                {
                    _document.ExportSettings ??= new FontExportSettings();
                    if (_document.ExportSettings.IncludeGlyphPreview != value)
                    {
                        SaveStateForUndo();
                        _document.ExportSettings.IncludeGlyphPreview = value;
                        OnPropertyChanged();
                    }
                }
            }
        }

        public bool IncludeMetricComments
        {
            get => _document?.ExportSettings?.IncludeMetricComments ?? true;
            set
            {
                if (_document != null)
                {
                    _document.ExportSettings ??= new FontExportSettings();
                    if (_document.ExportSettings.IncludeMetricComments != value)
                    {
                        SaveStateForUndo();
                        _document.ExportSettings.IncludeMetricComments = value;
                        OnPropertyChanged();
                    }
                }
            }
        }

        public bool UppercaseHex
        {
            get => _document?.ExportSettings?.UppercaseHex ?? true;
            set
            {
                if (_document != null)
                {
                    _document.ExportSettings ??= new FontExportSettings();
                    if (_document.ExportSettings.UppercaseHex != value)
                    {
                        SaveStateForUndo();
                        _document.ExportSettings.UppercaseHex = value;
                        OnPropertyChanged();
                    }
                }
            }
        }

        // ── Glyph Filter ─────────────────────────────────────────────────

        private string _glyphFilter = string.Empty;
        /// <summary>Filter text for narrowing the glyph list.</summary>
        public string GlyphFilter
        {
            get => _glyphFilter;
            set
            {
                if (SetProperty(ref _glyphFilter, value))
                {
                    _cachedFilteredGlyphMap = null;
                    OnPropertyChanged(nameof(FilteredGlyphMap));
                    OnPropertyChanged(nameof(FilteredGlyphCount));
                }
            }
        }

        private GlyphCategoryFilter _selectedCategoryFilter = GlyphCategoryFilter.All;
        /// <summary>Category filter (Letters, Numbers, Symbols, Edited, or All).</summary>
        public GlyphCategoryFilter SelectedCategoryFilter
        {
            get => _selectedCategoryFilter;
            set
            {
                if (SetProperty(ref _selectedCategoryFilter, value))
                {
                    _cachedFilteredGlyphMap = null;
                    OnPropertyChanged(nameof(FilteredGlyphMap));
                    OnPropertyChanged(nameof(FilteredGlyphCount));
                }
            }
        }

        public int TotalGlyphCount => GlyphMap.Count;
        public int FilteredGlyphCount => FilteredGlyphMap.Count;

        private IList<GlyphItemViewModel>? _cachedFilteredGlyphMap;
        
        /// <summary>Filtered view of the glyph map based on GlyphFilter and category.</summary>
        public IList<GlyphItemViewModel> FilteredGlyphMap
        {
            get
            {
                if (_cachedFilteredGlyphMap != null)
                    return _cachedFilteredGlyphMap;

                IEnumerable<GlyphItemViewModel> query = GlyphMap;

                if (SelectedCategoryFilter != GlyphCategoryFilter.All)
                {
                    query = SelectedCategoryFilter switch
                    {
                        GlyphCategoryFilter.Letters => query.Where(g => char.IsLetter(g.Character)),
                        GlyphCategoryFilter.Numbers => query.Where(g => char.IsDigit(g.Character)),
                        GlyphCategoryFilter.Symbols => query.Where(g => !char.IsLetterOrDigit(g.Character) && !char.IsControl(g.Character) && !char.IsWhiteSpace(g.Character)),
                        GlyphCategoryFilter.CustomizedOnly => query.Where(g => g.IsCustomized),
                        _ => query
                    };
                }

                if (!string.IsNullOrWhiteSpace(_glyphFilter))
                {
                    string filter = _glyphFilter.Trim();
                    query = query.Where(g =>
                        g.DisplayLabel.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        g.Character.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        g.CodePoint.ToString("X2", CultureInfo.InvariantCulture).Contains(filter, StringComparison.OrdinalIgnoreCase));
                }

                _cachedFilteredGlyphMap = [.. query];
                return _cachedFilteredGlyphMap;
            }
        }

        // ── Character Range ──────────────────────────────────────────────

        public int FirstChar
        {
            get => _document?.FirstChar ?? 32;
            set
            {
                if (_document != null && _document.FirstChar != value && value >= 0 && value < _document.LastChar)
                {
                    SaveStateForUndo();
                    _document.FirstChar = value;
                    IsDirty = true;
                    _document.NormalizeGlyphs();
                    RebuildGlyphMap();
                    SetActiveGlyph(0);
                    OnPropertyChanged();
                    UpdateEstimatedFlashBytes();
                }
            }
        }

        public int LastChar
        {
            get => _document?.LastChar ?? 126;
            set
            {
                if (_document != null && _document.LastChar != value && value > _document.FirstChar && value <= 65535)
                {
                    SaveStateForUndo();
                    _document.LastChar = value;
                    IsDirty = true;
                    _document.NormalizeGlyphs();
                    RebuildGlyphMap();
                    SetActiveGlyph(Math.Min(_document.ActiveGlyphIndex, _document.Glyphs.Count - 1));
                    OnPropertyChanged();
                    UpdateEstimatedFlashBytes();
                }
            }
        }

        private int _estimatedFlashBytes;
        public int EstimatedFlashBytes
        {
            get => _estimatedFlashBytes;
            private set => SetProperty(ref _estimatedFlashBytes, value);
        }

        // ── Collections & State ──────────────────────────────────────────────

        public ObservableCollection<GlyphItemViewModel> GlyphMap { get; } = [];

        private GlyphItemViewModel? _selectedGlyphItem;
        public GlyphItemViewModel? SelectedGlyphItem
        {
            get => _selectedGlyphItem;
            set
            {
                if (SetProperty(ref _selectedGlyphItem, value))
                {
                    if (value != null)
                    {
                        int index = GlyphMap.IndexOf(value);
                        if (index >= 0)
                        {
                            SetActiveGlyph(index);
                        }
                    }
                }
            }
        }

        private int _nextRevisionId;
        private int _currentRevisionId;
        private int _savedRevisionId;
        private readonly List<(FontDocument Document, int Revision)> _undoStack = [];
        private readonly List<(FontDocument Document, int Revision)> _redoStack = [];
        private const int MaxUndoSteps = 50;

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        private void RebuildGlyphMap()
        {
            GlyphMap.Clear();
            _cachedFilteredGlyphMap = null;
            if (_document == null) return;
            for (int i = 0; i < _document.Glyphs.Count; i++)
            {
                int index = i;
                var vm = new GlyphItemViewModel(_document.Glyphs[i], (item) => UpdateMiniPreview(index, fromGetter: true));
                GlyphMap.Add(vm);
            }
            OnPropertyChanged(nameof(FilteredGlyphMap));
            OnPropertyChanged(nameof(TotalGlyphCount));
            OnPropertyChanged(nameof(FilteredGlyphCount));
        }

        private void UpdateMiniPreview(int index, bool fromGetter = false)
        {
            if (_document == null || index < 0 || index >= _document.Glyphs.Count || index >= GlyphMap.Count) return;
            var glyph = _document.Glyphs[index];
            var vm = GlyphMap[index];

            if (glyph.Width <= 0 || glyph.Height <= 0) return;

            var currentBmp = vm.PeekMiniPreviewBitmap();
            if (currentBmp == null || currentBmp.PixelWidth != glyph.Width || currentBmp.PixelHeight != glyph.Height)
            {
                if (fromGetter)
                {
                    vm.SetMiniPreviewBitmapSilent(new WriteableBitmap(glyph.Width, glyph.Height, 96, 96, PixelFormats.Bgra32, palette: null));
                }
                else
                {
                    vm.MiniPreviewBitmap = new WriteableBitmap(glyph.Width, glyph.Height, 96, 96, PixelFormats.Bgra32, palette: null);
                }
                currentBmp = vm.PeekMiniPreviewBitmap()!;
            }

            uint[] buffer = new uint[glyph.Width * glyph.Height];
            uint ink = 0xFFFFFFFF; // White
            uint bg = 0x00000000;  // Transparent

            for (int i = 0; i < glyph.Pixels.Length; i++)
            {
                buffer[i] = glyph.Pixels[i] ? ink : bg;
            }

            currentBmp.WritePixels(
                new Int32Rect(0, 0, glyph.Width, glyph.Height),
                buffer,
                glyph.Width * 4,
                0);
        }

        private void UpdateEstimatedFlashBytes()
        {
            if (_document != null)
            {
                EstimatedFlashBytes = _document.EstimateFlashBytes(ExportFormat);
            }
        }

        // ── Undo / Redo ──────────────────────────────────────────────────────

        public void SaveStateForUndo()
        {
            if (_document == null || _isScrubbing || _isKeyboardScrubbing || _isSuppressingUndoSaves) return;
            _undoStack.Add((_document.Clone(), _currentRevisionId));
            if (_undoStack.Count > MaxUndoSteps)
            {
                _undoStack.RemoveAt(0);
            }
            _redoStack.Clear();
            _currentRevisionId = ++_nextRevisionId;
            IsDirty = (_currentRevisionId != _savedRevisionId);
            UpdateEstimatedFlashBytes();
            (UndoCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (RedoCommand as RelayCommand)?.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
        }

        public void Undo()
        {
            // Suppress undo saves to prevent any binding round-trips
            // from pushing spurious entries onto the stacks.
            _isSuppressingUndoSaves = true;
            try
            {
                // End any in-progress scrub
                _isKeyboardScrubbing = false;
                _isScrubbing = false;
                
                // Cancel any in-progress drawing stroke before undoing
                if (_isDrawingStroke) EndDrawing();
                
                if (_undoStack.Count > 0)
                {
                    _redoStack.Add((_document.Clone(), _currentRevisionId));
                    var (doc, rev) = _undoStack[^1];
                    _undoStack.RemoveAt(_undoStack.Count - 1);
                    _currentRevisionId = rev;
                    LoadStateSilent(doc);
                    IsDirty = (_currentRevisionId != _savedRevisionId);
                    (UndoCommand as RelayCommand)?.NotifyCanExecuteChanged();
                    (RedoCommand as RelayCommand)?.NotifyCanExecuteChanged();
                    OnPropertyChanged(nameof(CanUndo));
                    OnPropertyChanged(nameof(CanRedo));
                }
            }
            finally
            {
                _isSuppressingUndoSaves = false;
            }
        }

        public void Redo()
        {
            // Suppress undo saves to prevent any binding round-trips
            // from pushing spurious entries onto the stacks.
            _isSuppressingUndoSaves = true;
            try
            {
                // End any in-progress scrub
                _isKeyboardScrubbing = false;
                _isScrubbing = false;
                
                // Cancel any in-progress drawing stroke before redoing
                if (_isDrawingStroke) EndDrawing();
                
                if (_redoStack.Count > 0)
                {
                    _undoStack.Add((_document.Clone(), _currentRevisionId));
                    var (doc, rev) = _redoStack[^1];
                    _redoStack.RemoveAt(_redoStack.Count - 1);
                    _currentRevisionId = rev;
                    LoadStateSilent(doc);
                    IsDirty = (_currentRevisionId != _savedRevisionId);
                    (UndoCommand as RelayCommand)?.NotifyCanExecuteChanged();
                    (RedoCommand as RelayCommand)?.NotifyCanExecuteChanged();
                    OnPropertyChanged(nameof(CanUndo));
                    OnPropertyChanged(nameof(CanRedo));
                }
            }
            finally
            {
                _isSuppressingUndoSaves = false;
            }
        }

        private void LoadStateSilent(FontDocument doc)
        {
            _document = doc;
            RebuildGlyphMap();
            SetActiveGlyph(_document.ActiveGlyphIndex);

            OnPropertyChanged(nameof(Document));
            OnPropertyChanged(nameof(Baseline));
            OnPropertyChanged(nameof(YAdvance));
            OnPropertyChanged(nameof(FontName));
            OnPropertyChanged(nameof(IsMonospaced));
            OnPropertyChanged(nameof(CellHeight));
            OnPropertyChanged(nameof(MaxCellWidth));
            
            // Additional properties that should update on undo/redo
            OnPropertyChanged(nameof(FirstChar));
            OnPropertyChanged(nameof(LastChar));
            OnPropertyChanged(nameof(ExportFormat));
            OnPropertyChanged(nameof(IncludeGlyphPreview));
            OnPropertyChanged(nameof(IncludeMetricComments));
            OnPropertyChanged(nameof(UppercaseHex));
            OnPropertyChanged(nameof(ActiveGlyph));
            OnPropertyChanged(nameof(ActiveGlyphChar));
            OnPropertyChanged(nameof(ActiveGlyphCodePoint));
            OnPropertyChanged(nameof(GlyphWidth));
            OnPropertyChanged(nameof(GlyphHeight));
            OnPropertyChanged(nameof(XAdvance));
            OnPropertyChanged(nameof(XOffset));
            OnPropertyChanged(nameof(YOffset));
            OnPropertyChanged(nameof(CanvasDimensionText));

            UpdateEstimatedFlashBytes();
        }

        // ── Navigation ───────────────────────────────────────────────────────

        public void SetActiveGlyph(int index)
        {
            if (_document == null || _document.Glyphs.Count == 0) return;

            if (index < 0) index = 0;
            if (index >= _document.Glyphs.Count) index = _document.Glyphs.Count - 1;

            _document.ActiveGlyphIndex = index;

            for (int i = 0; i < GlyphMap.Count; i++)
            {
                GlyphMap[i].IsActive = (i == index);
            }

            if (index >= 0 && index < GlyphMap.Count)
            {
                if (_selectedGlyphItem != GlyphMap[index])
                {
                    _selectedGlyphItem = GlyphMap[index];
                    OnPropertyChanged(nameof(SelectedGlyphItem));
                }
            }

            InitCanvas();
            RedrawGlyphCanvas();
            UpdatePreviewBitmap();

            OnPropertyChanged(nameof(ActiveGlyph));
            OnPropertyChanged(nameof(ActiveGlyphChar));
            OnPropertyChanged(nameof(ActiveGlyphCodePoint));
            OnPropertyChanged(nameof(GlyphWidth));
            OnPropertyChanged(nameof(GlyphHeight));
            OnPropertyChanged(nameof(XAdvance));
            OnPropertyChanged(nameof(XOffset));
            OnPropertyChanged(nameof(YOffset));
            OnPropertyChanged(nameof(CanvasDimensionText));
        }

        public void NextGlyph()
        {
            if (_document != null)
            {
                SetActiveGlyph(_document.ActiveGlyphIndex + 1);
            }
        }

        public void PreviousGlyph()
        {
            if (_document != null)
            {
                SetActiveGlyph(_document.ActiveGlyphIndex - 1);
            }
        }

        public void JumpToChar(char c)
        {
            if (_document != null)
            {
                int idx = _document.GetGlyphIndex(c);
                if (idx >= 0)
                {
                    SetActiveGlyph(idx);
                }
            }
        }

        // ── Rendering & Canvas ───────────────────────────────────────────────

        private int _cellSize = 16;
        public int CellSize
        {
            get => _cellSize;
            set
            {
                if (SetProperty(ref _cellSize, Math.Clamp(value, 4, 128)))
                {
                    OnPropertyChanged(nameof(ZoomLevel));
                    InitCanvas();
                    RedrawGlyphCanvas();
                }
            }
        }

        private WriteableBitmap? _canvasBitmap;
        public WriteableBitmap? CanvasBitmap
        {
            get => _canvasBitmap;
            private set => SetProperty(ref _canvasBitmap, value);
        }

        public uint[]? CanvasBuffer { get; private set; }

        private void InitCanvas()
        {
            var glyph = _document?.ActiveGlyph;
            if (glyph == null || glyph.Width <= 0 || glyph.Height <= 0) return;

            int pw = Math.Max(1, Math.Max(glyph.Width, Math.Max(0, glyph.XAdvance))) * CellSize;
            int ph = Math.Max(1, glyph.Height) * CellSize;

            if (_canvasBitmap == null || _canvasBitmap.PixelWidth != pw || _canvasBitmap.PixelHeight != ph)
            {
                _canvasBitmap = new WriteableBitmap(pw, ph, 96, 96, PixelFormats.Bgra32, palette: null);
                CanvasBuffer = new uint[pw * ph];
                OnPropertyChanged(nameof(CanvasBitmap));
            }
        }

        public void RedrawGlyphCanvas()
        {
            if (_document == null) return;
            var glyph = _document.ActiveGlyph;
            if (glyph == null) return;

            InitCanvas();
            if (_canvasBitmap == null || CanvasBuffer == null) return;

            uint bg1 = 0xFF2A2A2A; // Dark gray
            uint bg2 = 0xFF353535; // Slightly lighter gray
            uint ink = 0xFFE0E0E0; // Almost white

            int w = _canvasBitmap.PixelWidth;
            int h = _canvasBitmap.PixelHeight;

            int maxLogicX = Math.Max(glyph.Width, Math.Max(0, glyph.XAdvance));
            int logicHeight = glyph.Height;

            for (int logicY = 0; logicY < logicHeight; logicY++)
            {
                int startY = logicY * CellSize;
                if (startY >= h) continue;
                int endY = Math.Min(startY + CellSize, h);
                if (endY <= startY) continue;
                
                for (int logicX = 0; logicX < maxLogicX; logicX++)
                {
                    int pixelIdx = (logicY * glyph.Width) + logicX;
                    bool isInk = (logicX < glyph.Width) && glyph.Pixels != null && pixelIdx < glyph.Pixels.Length && glyph.Pixels[pixelIdx];
                    bool isChecker = ((logicX + logicY) % 2) == 0;
                    
                    uint color;
                    if (isInk) color = ink;
                    else if (logicX >= glyph.Width) color = isChecker ? 0xFF202020 : 0xFF282828; // Darker checkerboard for out-of-bounds XAdvance area
                    else color = isChecker ? bg1 : bg2;
                    
                    int startX = logicX * CellSize;
                    if (startX >= w) continue;
                    int endX = Math.Min(startX + CellSize, w);
                    if (endX <= startX) continue;
                    
                    for (int y = startY; y < endY; y++)
                    {
                        int rowOffset = y * w;
                        if (rowOffset + endX <= CanvasBuffer.Length)
                        {
                            Array.Fill(CanvasBuffer, color, rowOffset + startX, endX - startX);
                        }
                    }
                }
            }

            // Draw grid lines
            if (ShowGridLines && CellSize >= 4)
            {
                uint gridColor = 0x40FFFFFF; // Semi-transparent white
                
                // Vertical grid lines
                for (int gx = 1; gx < maxLogicX; gx++)
                {
                    int px = gx * CellSize;
                    if (px < w)
                    {
                        for (int y = 0; y < h; y++)
                        {
                            CanvasBuffer[y * w + px] = Rendering.GlyphGuideRenderer.AlphaBlend(gridColor, CanvasBuffer[y * w + px]);
                        }
                    }
                }
                
                // Horizontal grid lines
                for (int gy = 1; gy < glyph.Height; gy++)
                {
                    int py = gy * CellSize;
                    if (py < h)
                    {
                        int rowOffset = py * w;
                        for (int x = 0; x < w; x++)
                        {
                            CanvasBuffer[rowOffset + x] = Rendering.GlyphGuideRenderer.AlphaBlend(gridColor, CanvasBuffer[rowOffset + x]);
                        }
                    }
                }
            }

            // Draw guides
            GlyphGuideRenderer.RenderGuides(CanvasBuffer, w, h, CellSize, _document, glyph, ShowMetricsGuides, ShowAdvanceGuide);

            // Commit to bitmap
            _canvasBitmap.WritePixels(
                new Int32Rect(0, 0, w, h),
                CanvasBuffer,
                w * 4,
                0);
        }

        // ── Editing & Pixel Operations ───────────────────────────────────────

        public void SetPixel(int logicX, int logicY, bool value)
        {
            if (_document == null) return;
            var glyph = _document.ActiveGlyph;
            if (glyph == null) return;

            if (logicX >= 0 && logicX < glyph.Width && logicY >= 0 && logicY < glyph.Height)
            {
                int index = logicY * glyph.Width + logicX;
                if (glyph.Pixels != null && index < glyph.Pixels.Length && glyph.Pixels[index] != value)
                {
                    glyph.Pixels[index] = value;
                    IsDirty = true;
                    glyph.IsCustomized = true;
                    if (_document.ActiveGlyphIndex >= 0 && _document.ActiveGlyphIndex < GlyphMap.Count)
                    {
                        GlyphMap[_document.ActiveGlyphIndex].IsCustomized = true;
                        UpdateMiniPreview(_document.ActiveGlyphIndex);
                    }
                    RedrawGlyphCanvas();
                    UpdateEstimatedFlashBytes();
                    UpdatePreviewBitmap();
                }
            }
        }

        /// <summary>
        /// Toggles a single pixel. Caller is responsible for calling SaveStateForUndo() beforehand.
        /// </summary>
        public void TogglePixel(int logicX, int logicY)
        {
            if (_document == null) return;
            var glyph = _document.ActiveGlyph;
            if (glyph == null) return;

            if (logicX >= 0 && logicX < glyph.Width && logicY >= 0 && logicY < glyph.Height)
            {
                int index = logicY * glyph.Width + logicX;
                if (glyph.Pixels != null && index < glyph.Pixels.Length)
                {
                    SetPixel(logicX, logicY, !glyph.Pixels[index]);
                }
            }
        }

        private void InternalResizeGlyph(Core.GlyphState g, int width)
        {
            if (g == null || width <= 0 || width == g.Width) return;
            
            width = Math.Clamp(width, 1, 512);
            int h = Math.Clamp(g.Height > 0 ? g.Height : _document.CellHeight, 1, 512);
            bool[] newPixels = new bool[width * h];
            int copyWidth = Math.Min(Math.Max(0, g.Width), width);
            int copyHeight = h;

            if (g.Pixels != null)
            {
                int srcW = g.Width > 0 ? g.Width : width;
                for (int y = 0; y < copyHeight; y++)
                {
                    for (int x = 0; x < copyWidth; x++)
                    {
                        int srcIdx = y * srcW + x;
                        if (srcIdx < g.Pixels.Length)
                        {
                            newPixels[y * width + x] = g.Pixels[srcIdx];
                        }
                    }
                }
            }

            g.Width = width;
            g.Height = h;
            g.Pixels = newPixels;
            
            if (AutoAdvance)
            {
                g.XAdvance = width + 1;
            }
        }

        public void ResizeActiveGlyph(int newWidth)
        {
            if (_document == null) return;
            var glyph = _document.ActiveGlyph;
            if (glyph == null || newWidth <= 0 || newWidth == glyph.Width) return;

            SaveStateForUndo();

            InternalResizeGlyph(glyph, newWidth);

            if (AutoAdvance)
            {
                OnPropertyChanged(nameof(XAdvance));
            }

            if (IsMonospaced)
            {
                _document.MaxCellWidth = newWidth;
                OnPropertyChanged(nameof(MaxCellWidth));

                foreach (var g in _document.Glyphs)
                {
                    if (g != null && g != glyph)
                    {
                        InternalResizeGlyph(g, newWidth);
                    }
                }
                
                // Update all previews
                for (int i = 0; i < _document.Glyphs.Count; i++)
                {
                    UpdateMiniPreview(i);
                }
            }
            else
            {
                UpdateMiniPreview(_document.ActiveGlyphIndex);
            }

            IsDirty = true;
            OnPropertyChanged(nameof(GlyphWidth));
            InitCanvas();
            RedrawGlyphCanvas();
            UpdateEstimatedFlashBytes();
            UpdatePreviewBitmap();
        }

        public void ShiftActiveGlyph(int dx, int dy)
        {
            if (_document == null || _document.ActiveGlyph == null) return;
            var glyph = _document.ActiveGlyph;
            int w = glyph.Width;
            int h = glyph.Height;
            if (w <= 0 || h <= 0 || glyph.Pixels == null || glyph.Pixels.Length < w * h) return;

            // Prevent shifting if it pushes active pixels out of bounds
            if (dx < 0) // Shifting Left
            {
                for (int y = 0; y < h; y++) if (glyph.Pixels[y * w]) return;
            }
            else if (dx > 0) // Shifting Right
            {
                for (int y = 0; y < h; y++) if (glyph.Pixels[y * w + (w - 1)]) return;
            }
            
            if (dy < 0) // Shifting Up
            {
                for (int x = 0; x < w; x++) if (glyph.Pixels[x]) return;
            }
            else if (dy > 0) // Shifting Down
            {
                int bottomRowStart = (h - 1) * w;
                for (int x = 0; x < w; x++) if (glyph.Pixels[bottomRowStart + x]) return;
            }

            SaveStateForUndo();

            bool[] newPixels = new bool[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int srcX = x - dx;
                    int srcY = y - dy;
                    if (srcX >= 0 && srcX < w && srcY >= 0 && srcY < h)
                    {
                        newPixels[y * w + x] = glyph.Pixels[srcY * w + srcX];
                    }
                }
            }

            glyph.Pixels = newPixels;
            glyph.IsCustomized = true;
            IsDirty = true;
            if (_document.ActiveGlyphIndex >= 0 && _document.ActiveGlyphIndex < GlyphMap.Count)
            {
                GlyphMap[_document.ActiveGlyphIndex].IsCustomized = true;
                UpdateMiniPreview(_document.ActiveGlyphIndex);
            }
            RedrawGlyphCanvas();
            UpdatePreviewBitmap();
        }

        public void NudgeActiveGlyph(int dx, int dy) => ShiftActiveGlyph(dx, dy);

        // ── Drawing ──────────────────────────────────────────────────────────

        private bool _isDrawingStroke;
        private bool _strokeHasMutatedPixel;

        public void BeginDrawing()
        {
            _isDrawingStroke = true;
            _strokeHasMutatedPixel = false;
        }

        public void EndDrawing()
        {
            _isDrawingStroke = false;
            _strokeHasMutatedPixel = false;
            UpdateEstimatedFlashBytes();
        }

        public void DrawPixel(int x, int y, bool erase = false)
        {
            if (!_isDrawingStroke) return;
            var glyph = ActiveGlyph;
            if (glyph == null) return;
            if (x < 0 || x >= glyph.Width || y < 0 || y >= glyph.Height) return;

            int index = y * glyph.Width + x;
            if (glyph.Pixels == null || index >= glyph.Pixels.Length) return;

            bool targetValue = !erase;
            
            if (glyph.Pixels[index] != targetValue)
            {
                if (!_strokeHasMutatedPixel)
                {
                    SaveStateForUndo();
                    _strokeHasMutatedPixel = true;
                }

                glyph.Pixels[index] = targetValue;
                IsDirty = true;
                glyph.IsCustomized = true;
                if (_document.ActiveGlyphIndex >= 0 && _document.ActiveGlyphIndex < GlyphMap.Count)
                {
                    GlyphMap[_document.ActiveGlyphIndex].IsCustomized = true;
                }
                RedrawGlyphCanvas();
                UpdateMiniPreview(_document.ActiveGlyphIndex);
                UpdatePreviewBitmap();
            }
        }

        // ── Preview ──────────────────────────────────────────────────────────

        public bool[,] RenderPreviewText(string text)
        {
            if (_document == null) return new bool[0, 0];
            return FontPreviewRenderer.RenderPreviewText(_document, text);
        }

        public void UpdatePreviewBitmap()
        {
            if (_document == null || string.IsNullOrEmpty(_previewText))
            {
                PreviewBitmap = null;
                return;
            }

            try
            {
                bool[,] preview = FontPreviewRenderer.RenderPreviewText(_document, _previewText);
                int pw = preview.GetLength(0);
                int ph = preview.GetLength(1);
                if (pw <= 0 || ph <= 0)
                {
                    PreviewBitmap = null;
                    return;
                }

                // Scale up for visibility
                int scale = _previewScale > 0 ? _previewScale : Math.Max(1, Math.Min(4, 256 / Math.Max(pw, ph)));
                int sw = pw * scale;
                int sh = ph * scale;

                var bitmap = new WriteableBitmap(sw, sh, 96, 96, PixelFormats.Bgra32, palette: null);
                uint[] buffer = new uint[sw * sh];

                uint ink;
                uint bg;

                switch (PreviewDisplayType)
                {
                    case DisplayType.SSD1306Blue:
                        ink = 0xFF00B4FF; // Cyan/Blue OLED
                        bg = 0xFF000000;
                        break;
                    case DisplayType.SSD1306Green:
                        ink = 0xFF00E676; // Green OLED
                        bg = 0xFF000000;
                        break;
                    case DisplayType.ePaper:
                        ink = 0xFF141414; // Ink Black
                        bg = 0xFFE6E4DD; // Paper White
                        break;
                    case DisplayType.FlipperZero:
                        ink = 0xFF000000; // Black LCD
                        bg = 0xFFFF8200; // Flipper Orange
                        break;
                    case DisplayType.GenericWhite:
                    default:
                        ink = 0xFFE0E0E0; // Off-white
                        bg = 0xFF1A1A1A; // Dark panel
                        break;
                }

                if (_previewInverted)
                {
                    (ink, bg) = (bg, ink);
                }

                for (int y = 0; y < sh; y++)
                {
                    int srcY = y / scale;
                    for (int x = 0; x < sw; x++)
                    {
                        int srcX = x / scale;
                        buffer[y * sw + x] = (srcX < pw && srcY < ph && preview[srcX, srcY]) ? ink : bg;
                    }
                }

                bitmap.WritePixels(new Int32Rect(0, 0, sw, sh), buffer, sw * 4, 0);
                PreviewBitmap = bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FontViewModel] UpdatePreviewBitmap failed: {ex.Message}");
                PreviewBitmap = null;
            }
        }

    }
}
