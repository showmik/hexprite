using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hexprite.Core;
using Hexprite.Resources.Fonts;
using Hexprite.Services;
using System.Globalization;

namespace Hexprite.ViewModels.Flipper
{
    public enum AssetPackViewMode
    {
        MatrixStudio,
        DataGridRoster,
        SpriteGallery,
        DeviceSimulator,
    }

    public enum FlipperMatrixDragMode
    {
        AssignTarget,
        InspectRegion,
    }

    public class FlipperScheduleMatrixViewModel : ObservableObject, IDisposable
    {
        private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

        private readonly IFlipperWindowManager? _windowManager;
        private readonly IDialogService? _dialogService;
        private readonly IUserFeedbackService? _feedbackService;
        private readonly IClipboardService? _clipboardService;
        private readonly IWorkspaceTabService? _tabService;
        private readonly IFlipperExportService _exportService;
        private readonly IFlipperImportService _importService;

        private readonly Stack<FlipperMatrixStateSnapshot> _undoStack = new();
        private readonly Stack<FlipperMatrixStateSnapshot> _redoStack = new();
        private readonly Dictionary<string, SpriteState> _animationSprites = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _animationFilePaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>? _importedPack;
        private string _packName = "Flipper Asset Pack";
        private FlipperScheduleMatrix _matrix;
        private FlipperScheduleEntryViewModel? _selectedEntry;
        private FlipperMatrixDragMode _dragMode = FlipperMatrixDragMode.AssignTarget;
        private bool _hasSelectedRegion;
        private int _selectedRegionMinLevel = 1;
        private int _selectedRegionMaxLevel = 1;
        private int _selectedRegionMinMood;
        private int _selectedRegionMaxMood;
        private int _selectedRegionCellCount;
        private int _selectedRegionGapCount;
        private int _selectedRegionAnimationCount;
        private double _selectedRegionCoveragePercent;
        private string _selectedRegionSummaryText = string.Empty;
        private ObservableCollection<FlipperCellProbability> _selectedRegionProbabilities = [];
        private string _coverageBadgeText = "100% Coverage";
        private string _matrixStatsText = "450 / 450 States Covered • 0 Gaps";
        private string _cellHoverInfoText = "Click & drag on the grid to set range, or click to inspect.";
        private string _statusMessage = string.Empty;
        private string _validationStatusText = "✅ Valid Manifest";
        private bool _hasValidationIssues;
        private List<FlipperValidationDiagnostic> _validationDiagnostics = [];
        private bool _isUpdating;
        private bool _isStockMode;
        private int _selectedCellLevel = 1;
        private int _selectedCellMood;
        private string _selectedCellSummaryText = "Level 1 (Baby) • Mood 0 (Happy)";
        private ObservableCollection<FlipperCellProbability> _selectedCellProbabilities = [];

        // ── Search & Filter Fields ─────────────────────────────────────────
        private string _searchFilterText = string.Empty;
        private string _selectedStageFilter = "All";
        private string _selectedMoodFilter = "All";

        // ── Inline Canvas Animation Preview Fields ─────────────────────────
        private readonly WriteableBitmap _previewBitmap = new(128, 64, 96, 96, PixelFormats.Bgra32, palette: null);
        private readonly uint[] _previewPixelBuffer = new uint[128 * 64];
        private readonly bool[] _blankTextPixelBuffer = new bool[128 * 64];
        private bool[] _monoPreviewBuffer = new bool[128 * 64];
        private DispatcherTimer? _previewTimer;
        private FlipperScheduleEntryViewModel? _activeHoverPlayingEntry;
        private SpriteState? _currentPreviewSprite;
        private int _previewFrameIndex;
        private bool _isPreviewPlaying;
        private bool _isPreviewLooping = true;
        private double _previewSpeedMultiplier = 1.0;
        private string _previewFrameCountText = "0 / 0";
        private string _previewDimensionsText = "128 × 64";
        private bool _isDisposed;

        public event EventHandler? MatrixRedrawRequested;
        public event EventHandler? DocumentModified;

        protected virtual void OnDocumentModified()
        {
            if (!_isUpdating)
            {
                DocumentModified?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public WriteableBitmap PreviewBitmap => _previewBitmap;
        public SpriteState? CurrentPreviewSprite => _currentPreviewSprite;

        public bool IsPreviewPlaying
        {
            get => _isPreviewPlaying;
            private set
            {
                if (SetProperty(ref _isPreviewPlaying, value))
                {
                    OnPropertyChanged(nameof(PreviewPlayButtonText));
                }
            }
        }

        public string PreviewPlayButtonText => _isPreviewPlaying ? "⏸ Pause" : "▶ Play";

        public bool IsPreviewLooping
        {
            get => _isPreviewLooping;
            set
            {
                if (SetProperty(ref _isPreviewLooping, value))
                {
                    OnPropertyChanged(nameof(PreviewLoopButtonText));
                }
            }
        }

        public string PreviewLoopButtonText => _isPreviewLooping ? "🔁 Loop: On" : "➡️ Loop: Off";

        public double PreviewSpeedMultiplier
        {
            get => _previewSpeedMultiplier;
            set
            {
                double clamped = Math.Clamp(value, 0.25, 4.0);
                if (SetProperty(ref _previewSpeedMultiplier, clamped))
                {
                    OnPropertyChanged(nameof(PreviewSpeedText));
                    UpdateTimerInterval();
                }
            }
        }

        public string PreviewSpeedText => string.Create(CultureInfo.InvariantCulture, $"{_previewSpeedMultiplier:0.#}x");

        public int PreviewFps => Math.Max(1, _currentPreviewSprite?.FrameRateFps ?? 10);

        public string PreviewFpsBadgeText => string.Create(CultureInfo.InvariantCulture, $"FPS: {PreviewFps}");

        public int PreviewTotalFrames
        {
            get
            {
                if (_currentPreviewSprite?.FlipperCycle?.FramesOrder != null && _currentPreviewSprite.FlipperCycle.FramesOrder.Length > 0)
                {
                    return _currentPreviewSprite.FlipperCycle.FramesOrder.Length;
                }
                if (_currentPreviewSprite != null && _currentPreviewSprite.Frames.Count > 0)
                {
                    return _currentPreviewSprite.Frames.Count;
                }
                return 1;
            }
        }

        public int PreviewMaxFrameIndex => Math.Max(0, PreviewTotalFrames - 1);

        public bool PreviewHasMultipleFrames => PreviewTotalFrames > 1;

        public int PreviewFrameIndex
        {
            get => _previewFrameIndex;
            set
            {
                int maxIdx = PreviewMaxFrameIndex;
                int clamped = Math.Clamp(value, 0, maxIdx);
                if (_previewFrameIndex != clamped)
                {
                    _previewFrameIndex = clamped;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentPreviewFrameIndex));
                    UpdatePreviewFrameCountTextOnly();
                    RenderPreviewFrame();
                }
            }
        }

        public int CurrentPreviewFrameIndex
        {
            get => PreviewFrameIndex;
            set => PreviewFrameIndex = value;
        }

        public string PreviewFrameCountText
        {
            get => _previewFrameCountText;
            private set => SetProperty(ref _previewFrameCountText, value);
        }

        public string PreviewDimensionsText
        {
            get => _previewDimensionsText;
            private set => SetProperty(ref _previewDimensionsText, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public string ValidationStatusText
        {
            get => _validationStatusText;
            private set => SetProperty(ref _validationStatusText, value);
        }

        public bool HasValidationIssues
        {
            get => _hasValidationIssues;
            private set => SetProperty(ref _hasValidationIssues, value);
        }

        public IReadOnlyList<FlipperValidationDiagnostic> ValidationDiagnostics => _validationDiagnostics;

        public ObservableCollection<FlipperDiagnosticItemViewModel> ActionableDiagnostics { get; } = [];

        public bool HasActionableDiagnostics => ActionableDiagnostics.Count > 0;

        public string ActionableDiagnosticsSummaryText => string.Create(CultureInfo.InvariantCulture, $"{ActionableDiagnostics.Count} Diagnostic Issue{(ActionableDiagnostics.Count == 1 ? "" : "s")}");

        public string PackName
        {
            get => _packName;
            set
            {
                string val = value ?? string.Empty;
                if (_packName != val)
                {
                    if (!_isUpdating)
                    {
                        PushUndoState();
                    }
                    SetProperty(ref _packName, val);
                }
            }
        }

        public string? PackFilePath { get; set; }

        public bool IsStockMode
        {
            get => _isStockMode;
            set
            {
                if (_isStockMode != value)
                {
                    if (!_isUpdating)
                    {
                        PushUndoState();
                    }
                    _isStockMode = value;
                    OnPropertyChanged(nameof(IsStockMode));
                    OnPropertyChanged(nameof(IsMomentumMode));
                    OnPropertyChanged(nameof(MaxAllowedLevel));
                    OnPropertyChanged(nameof(ModeBadgeText));
                    OnModeChanged();
                }
            }
        }

        public bool IsMomentumMode
        {
            get => !_isStockMode;
            set => IsStockMode = !value;
        }

        public int MaxAllowedLevel => _isStockMode ? 3 : 30;

        public string ModeBadgeText => _isStockMode ? "Stock Mode (L1-3)" : "Extended Mode (L1-30)";

        public IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>? ImportedPack => _importedPack;

        public IReadOnlyDictionary<string, SpriteState> AnimationSprites => _animationSprites;
        public IReadOnlyDictionary<string, string> AnimationFilePaths => _animationFilePaths;

        // ── View Mode & Layout Scalability ──────────────────────────────────
        private AssetPackViewMode _currentViewMode = AssetPackViewMode.MatrixStudio;
        public AssetPackViewMode CurrentViewMode
        {
            get => _currentViewMode;
            set
            {
                if (SetProperty(ref _currentViewMode, value))
                {
                    OnPropertyChanged(nameof(IsMatrixStudioView));
                    OnPropertyChanged(nameof(IsDataGridRosterView));
                    OnPropertyChanged(nameof(IsSpriteGalleryView));
                    OnPropertyChanged(nameof(IsDeviceSimulatorView));
                    if (value == AssetPackViewMode.SpriteGallery)
                    {
                        _simulatorViewModel?.StopPlayback();
                        EnsurePreviewTimer();
                        UpdateTimerInterval();
                        _previewTimer?.Start();
                    }
                    else if (value == AssetPackViewMode.DeviceSimulator)
                    {
                        _previewTimer?.Stop();
                        InitializeSimulatorViewModelIfNeeded();
                        SyncToSimulator();
                        _simulatorViewModel?.StartPlayback();
                    }
                    else
                    {
                        _simulatorViewModel?.StopPlayback();
                        if (!_isPreviewPlaying)
                        {
                            _previewTimer?.Stop();
                        }
                    }
                    MatrixRedrawRequested?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public bool IsMatrixStudioView => CurrentViewMode == AssetPackViewMode.MatrixStudio;
        public bool IsDataGridRosterView => CurrentViewMode == AssetPackViewMode.DataGridRoster;
        public bool IsSpriteGalleryView => CurrentViewMode == AssetPackViewMode.SpriteGallery;
        public bool IsDeviceSimulatorView => CurrentViewMode == AssetPackViewMode.DeviceSimulator;

        private FlipperSimulatorViewModel? _simulatorViewModel;
        public FlipperSimulatorViewModel SimulatorViewModel
        {
            get
            {
                if (_simulatorViewModel == null)
                {
                    InitializeSimulatorViewModel();
                }
                return _simulatorViewModel!;
            }
        }

        // ── Sorting & Grouping Engine ───────────────────────────────────────
        private string _selectedSortOption = "Default";
        public string SelectedSortOption
        {
            get => _selectedSortOption;
            set
            {
                if (SetProperty(ref _selectedSortOption, value ?? "Default"))
                {
                    ApplyEntryFilter();
                }
            }
        }

        public IReadOnlyList<string> SortOptions { get; } =
        [
            "Default",
            "Name (A-Z)",
            "Level (Low-High)",
            "Mood (Low-High)",
            "Weight (High-Low)",
            "Coverage (Cells)",
            "Issues First",
        ];

        private string _currentSortColumn = "Default";
        public string CurrentSortColumn => _currentSortColumn;

        private bool _isSortAscending = true;
        public bool IsSortAscending => _isSortAscending;

        public string NameSortGlyph => GetColumnSortGlyph("Name");
        public string LevelSortGlyph => GetColumnSortGlyph("Level");
        public string MoodSortGlyph => GetColumnSortGlyph("Mood");
        public string WeightSortGlyph => GetColumnSortGlyph("Weight");
        public string CoverageSortGlyph => GetColumnSortGlyph("Coverage");
        public string StageSortGlyph => GetColumnSortGlyph("Stage");
        public string StatusSortGlyph => GetColumnSortGlyph("Status");

        public bool IsAllSelected => Entries.Count > 0 && Entries.All(e => e.IsSelected);
        public bool IsAnySelected => Entries.Any(e => e.IsSelected);
        public bool? MasterSelectionState => Entries.Count == 0 ? false : (Entries.All(e => e.IsSelected) ? true : (Entries.Any(e => e.IsSelected) ? (bool?)null : false));

        public bool HasSelection => SelectedEntries.Count > 0 || SelectedEntry != null;
        public bool HasMultiSelection => SelectedEntries.Count > 1;

        public string SelectedEntriesCountText => SelectedEntries.Count > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{SelectedEntries.Count} Selected")
            : string.Empty;

        public string SelectedEntriesCountPillText
        {
            get
            {
                int count = SelectedEntries.Count;
                if (count == 0 && SelectedEntry != null) count = 1;
                if (count == 0) return "0 Selected";
                return string.Create(CultureInfo.InvariantCulture, $"{count} of {Entries.Count} Selected");
            }
        }

        private string _selectedGroupOption = "None";
        public string SelectedGroupOption
        {
            get => _selectedGroupOption;
            set
            {
                if (SetProperty(ref _selectedGroupOption, value ?? "None"))
                {
                    ApplyEntryFilter();
                }
            }
        }

        public IReadOnlyList<string> GroupOptions { get; } =
        [
            "None",
            "Stage (Baby/Teen/Adult)",
            "Mood (Happy/Neutral/Angry)",
            "Health Status",
        ];

        // ── Gallery Card Sizing & Empty States ──────────────────────────────
        private string _galleryCardSize = "Standard";
        public string GalleryCardSize
        {
            get => _galleryCardSize;
            set
            {
                if (SetProperty(ref _galleryCardSize, value ?? "Standard"))
                {
                    OnPropertyChanged(nameof(GalleryCardWidth));
                    OnPropertyChanged(nameof(GalleryCardHeight));
                    OnPropertyChanged(nameof(GalleryThumbnailHeight));
                    OnPropertyChanged(nameof(IsCompactGallerySize));
                    OnPropertyChanged(nameof(IsStandardGallerySize));
                    OnPropertyChanged(nameof(IsLargeGallerySize));
                }
            }
        }

        public double GalleryCardWidth => GalleryCardSize switch
        {
            "Compact" => 200,
            "Large" => 330,
            _ => 260,
        };

        public double GalleryCardHeight => GalleryCardSize switch
        {
            "Compact" => 195,
            "Large" => 300,
            _ => 245,
        };

        public double GalleryThumbnailHeight => GalleryCardSize switch
        {
            "Compact" => 70,
            "Large" => 135,
            _ => 90,
        };

        public bool IsCompactGallerySize => string.Equals(GalleryCardSize, "Compact", StringComparison.OrdinalIgnoreCase);
        public bool IsStandardGallerySize => string.Equals(GalleryCardSize, "Standard", StringComparison.OrdinalIgnoreCase);
        public bool IsLargeGallerySize => string.Equals(GalleryCardSize, "Large", StringComparison.OrdinalIgnoreCase);

        public bool HasFilteredEntries => FilteredEntries.Count > 0;
        public bool HasNoFilteredEntries => FilteredEntries.Count == 0;
        public string FilteredEntriesCountText => string.Create(CultureInfo.InvariantCulture, $"Showing {FilteredEntries.Count} of {Entries.Count} animations");

        // ── LCD Theme & Hardware Simulation ─────────────────────────────────
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance property bound by WPF XAML Views")]
        public IReadOnlyList<ThemePaletteInfo> ThemePalettes => FlipperThemeService.Palettes;

        private int _selectedPaletteIndex;
        public int SelectedPaletteIndex
        {
            get => _selectedPaletteIndex;
            set
            {
                if (SetProperty(ref _selectedPaletteIndex, Math.Clamp(value, 0, ThemePalettes.Count - 1)))
                {
                    RenderPreviewFrame();
                }
            }
        }

        // ── Solo & Visual Isolation Mode ────────────────────────────────────
        private bool _isSoloModeActive;
        public bool IsSoloModeActive
        {
            get => _isSoloModeActive;
            set
            {
                if (SetProperty(ref _isSoloModeActive, value))
                {
                    MatrixRedrawRequested?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        // ── Multi-Selection & Bulk Operations ───────────────────────────────
        public ObservableCollection<FlipperScheduleEntryViewModel> SelectedEntries { get; } = [];

        public ObservableCollection<FlipperScheduleEntryViewModel> Entries { get; } = [];

        public ObservableCollection<FlipperScheduleEntryViewModel> FilteredEntries { get; } = [];

        public string SearchFilterText
        {
            get => _searchFilterText;
            set
            {
                if (SetProperty(ref _searchFilterText, value ?? string.Empty))
                {
                    ApplyEntryFilter();
                }
            }
        }

        public string SelectedStageFilter
        {
            get => _selectedStageFilter;
            set
            {
                if (SetProperty(ref _selectedStageFilter, value ?? "All"))
                {
                    ApplyEntryFilter();
                }
            }
        }

        public string SelectedMoodFilter
        {
            get => _selectedMoodFilter;
            set
            {
                if (SetProperty(ref _selectedMoodFilter, value ?? "All"))
                {
                    ApplyEntryFilter();
                }
            }
        }

        public bool HasActiveFilters =>
            !string.IsNullOrWhiteSpace(SearchFilterText) ||
            !string.Equals(SelectedStageFilter, "All", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(SelectedMoodFilter, "All", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(SelectedSortOption, "Default", StringComparison.OrdinalIgnoreCase);

        public string FilterCountText => Entries.Count == FilteredEntries.Count
            ? string.Create(CultureInfo.InvariantCulture, $"{Entries.Count} Animation{(Entries.Count == 1 ? "" : "s")}")
            : string.Create(CultureInfo.InvariantCulture, $"{FilteredEntries.Count} of {Entries.Count} Animation{(Entries.Count == 1 ? "" : "s")}");

        public IReadOnlyList<string> StageFilters { get; } = ["All", "Baby", "Teen", "Adult", "Spanning", "IssuesOnly"];
        public IReadOnlyList<string> MoodFilters { get; } = ["All", "Happy", "Neutral", "Angry"];

        public FlipperScheduleMatrix Matrix => _matrix;

        public FlipperMatrixDragMode DragMode
        {
            get => _dragMode;
            set
            {
                if (SetProperty(ref _dragMode, value))
                {
                    OnPropertyChanged(nameof(IsAssignDragMode));
                    OnPropertyChanged(nameof(IsInspectDragMode));
                    OnPropertyChanged(nameof(DragModeBadgeText));
                    OnPropertyChanged(nameof(DragModeTooltip));
                    MatrixRedrawRequested?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public bool IsAssignDragMode => DragMode == FlipperMatrixDragMode.AssignTarget;
        public bool IsInspectDragMode => DragMode == FlipperMatrixDragMode.InspectRegion;

        public string DragModeBadgeText => IsAssignDragMode ? "🎯 Assign Target" : "🔍 Inspect Region";
        public string DragModeTooltip => IsAssignDragMode
            ? "Assign Mode: Dragging on grid sets level/mood bounds for the selected animation (Click to switch to Region Inspect)"
            : "Inspect Mode: Dragging on grid selects a cell region to view metrics without modifying any animation (Click to switch to Assign Target)";

        public FlipperScheduleEntryViewModel? SelectedEntry
        {
            get => _selectedEntry;
            set
            {
                if (_isUpdating && value == null && _selectedEntry != null)
                {
                    return;
                }

                if (SetProperty(ref _selectedEntry, value))
                {
                    OnPropertyChanged(nameof(HasSelectedEntry));
                    OnPropertyChanged(nameof(HasNoSelectedEntry));
                    OnPropertyChanged(nameof(SelectedName));
                    OnPropertyChanged(nameof(SelectedMinLevel));
                    OnPropertyChanged(nameof(SelectedMaxLevel));
                    OnPropertyChanged(nameof(SelectedMinButthurt));
                    OnPropertyChanged(nameof(SelectedMaxButthurt));
                    OnPropertyChanged(nameof(SelectedWeight));
                    OnPropertyChanged(nameof(ActiveTargetBannerText));
                    OnPropertyChanged(nameof(ActiveTargetDetailsText));
                    RefreshCurrentPreviewSprite();
                    MatrixRedrawRequested?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public bool HasSelectedEntry => SelectedEntry != null;
        public bool HasNoSelectedEntry => SelectedEntry == null;

        public string ActiveTargetBannerText => SelectedEntry != null
            ? $"🎯 Target: {SelectedEntry.Name}"
            : "🔍 Region Inspect Mode";

        public string ActiveTargetDetailsText => SelectedEntry != null
            ? $"{SelectedEntry.BoundsText} • Drag grid to reassign range"
            : "No animation selected. Drag on grid to select region for inspection, or click an animation to edit.";

        public bool HasSelectedRegion
        {
            get => _hasSelectedRegion;
            private set
            {
                if (SetProperty(ref _hasSelectedRegion, value))
                {
                    OnPropertyChanged(nameof(HasSelectedRegionGap));
                }
            }
        }

        public int SelectedRegionMinLevel
        {
            get => _selectedRegionMinLevel;
            private set => SetProperty(ref _selectedRegionMinLevel, value);
        }

        public int SelectedRegionMaxLevel
        {
            get => _selectedRegionMaxLevel;
            private set => SetProperty(ref _selectedRegionMaxLevel, value);
        }

        public int SelectedRegionMinMood
        {
            get => _selectedRegionMinMood;
            private set => SetProperty(ref _selectedRegionMinMood, value);
        }

        public int SelectedRegionMaxMood
        {
            get => _selectedRegionMaxMood;
            private set => SetProperty(ref _selectedRegionMaxMood, value);
        }

        public int SelectedRegionCellCount
        {
            get => _selectedRegionCellCount;
            private set => SetProperty(ref _selectedRegionCellCount, value);
        }

        public int SelectedRegionGapCount
        {
            get => _selectedRegionGapCount;
            private set
            {
                if (SetProperty(ref _selectedRegionGapCount, value))
                {
                    OnPropertyChanged(nameof(HasSelectedRegionGap));
                }
            }
        }

        public int SelectedRegionAnimationCount
        {
            get => _selectedRegionAnimationCount;
            private set => SetProperty(ref _selectedRegionAnimationCount, value);
        }

        public double SelectedRegionCoveragePercent
        {
            get => _selectedRegionCoveragePercent;
            private set => SetProperty(ref _selectedRegionCoveragePercent, value);
        }

        public string SelectedRegionSummaryText
        {
            get => _selectedRegionSummaryText;
            private set => SetProperty(ref _selectedRegionSummaryText, value);
        }

        public ObservableCollection<FlipperCellProbability> SelectedRegionProbabilities
        {
            get => _selectedRegionProbabilities;
            private set => SetProperty(ref _selectedRegionProbabilities, value);
        }

        public bool HasSelectedRegionGap => HasSelectedRegion && SelectedRegionGapCount > 0;

        public string SelectedName
        {
            get => SelectedEntry?.Name ?? string.Empty;
            set
            {
                if (SelectedEntry != null && SelectedEntry.Name != value)
                {
                    PushUndoState();
                    SelectedEntry.Name = value;
                    OnPropertyChanged();
                }
            }
        }

        public string CoverageBadgeText
        {
            get => _coverageBadgeText;
            private set => SetProperty(ref _coverageBadgeText, value);
        }

        public string MatrixStatsText
        {
            get => _matrixStatsText;
            private set => SetProperty(ref _matrixStatsText, value);
        }

        public string CellHoverInfoText
        {
            get => _cellHoverInfoText;
            private set => SetProperty(ref _cellHoverInfoText, value);
        }

        public int SelectedCellLevel
        {
            get => _selectedCellLevel;
            private set => SetProperty(ref _selectedCellLevel, value);
        }

        public int SelectedCellMood
        {
            get => _selectedCellMood;
            private set => SetProperty(ref _selectedCellMood, value);
        }

        public int InspectedLevel => SelectedCellLevel;
        public int InspectedMood => SelectedCellMood;

        private string _inspectedCellTotalWeightText = "Total Weight: 0";
        public string InspectedCellTotalWeightText
        {
            get => _inspectedCellTotalWeightText;
            private set => SetProperty(ref _inspectedCellTotalWeightText, value);
        }

        private string _inspectedCellBreakdownText = "No animations scheduled";
        public string InspectedCellBreakdownText
        {
            get => _inspectedCellBreakdownText;
            private set => SetProperty(ref _inspectedCellBreakdownText, value);
        }

        private int _inspectedCellTotalWeight;
        public int InspectedCellTotalWeight
        {
            get => _inspectedCellTotalWeight;
            private set => SetProperty(ref _inspectedCellTotalWeight, value);
        }

        private int _inspectedCellAnimationCount;
        public int InspectedCellAnimationCount
        {
            get => _inspectedCellAnimationCount;
            private set => SetProperty(ref _inspectedCellAnimationCount, value);
        }

        public string SelectedCellSummaryText
        {
            get => _selectedCellSummaryText;
            private set => SetProperty(ref _selectedCellSummaryText, value);
        }

        public ObservableCollection<FlipperCellProbability> SelectedCellProbabilities
        {
            get => _selectedCellProbabilities;
            private set => SetProperty(ref _selectedCellProbabilities, value);
        }

        public bool HasSelectedCellCoverage => SelectedCellProbabilities != null && SelectedCellProbabilities.Count > 0;

        public bool HasSelectedCellGap => !HasSelectedCellCoverage;

        public int SelectedMinLevel
        {
            get => SelectedEntry?.MinLevel ?? 1;
            set
            {
                if (SelectedEntry != null)
                {
                    int clamped = Math.Clamp(value, 1, MaxAllowedLevel);
                    if (SelectedEntry.MinLevel != clamped)
                    {
                        PushUndoState();
                        _isUpdating = true;
                        try
                        {
                            SelectedEntry.MinLevel = clamped;
                        }
                        finally
                        {
                            _isUpdating = false;
                        }
                        OnPropertyChanged();
                        OnPropertyChanged(nameof(SelectedMaxLevel));
                        RecalculateMatrix();
                        OnDocumentModified();
                    }
                }
            }
        }

        public int SelectedMaxLevel
        {
            get => SelectedEntry?.MaxLevel ?? MaxAllowedLevel;
            set
            {
                if (SelectedEntry != null)
                {
                    int clamped = Math.Clamp(value, 1, MaxAllowedLevel);
                    if (SelectedEntry.MaxLevel != clamped)
                    {
                        PushUndoState();
                        _isUpdating = true;
                        try
                        {
                            SelectedEntry.MaxLevel = clamped;
                        }
                        finally
                        {
                            _isUpdating = false;
                        }
                        OnPropertyChanged();
                        OnPropertyChanged(nameof(SelectedMinLevel));
                        RecalculateMatrix();
                        OnDocumentModified();
                    }
                }
            }
        }

        public int SelectedMinButthurt
        {
            get => SelectedEntry?.MinButthurt ?? 0;
            set
            {
                if (SelectedEntry != null)
                {
                    int clamped = Math.Clamp(value, 0, 14);
                    if (SelectedEntry.MinButthurt != clamped)
                    {
                        PushUndoState();
                        _isUpdating = true;
                        try
                        {
                            SelectedEntry.MinButthurt = clamped;
                        }
                        finally
                        {
                            _isUpdating = false;
                        }
                        OnPropertyChanged();
                        OnPropertyChanged(nameof(SelectedMaxButthurt));
                        RecalculateMatrix();
                        OnDocumentModified();
                    }
                }
            }
        }

        public int SelectedMaxButthurt
        {
            get => SelectedEntry?.MaxButthurt ?? 14;
            set
            {
                if (SelectedEntry != null)
                {
                    int clamped = Math.Clamp(value, 0, 14);
                    if (SelectedEntry.MaxButthurt != clamped)
                    {
                        PushUndoState();
                        _isUpdating = true;
                        try
                        {
                            SelectedEntry.MaxButthurt = clamped;
                        }
                        finally
                        {
                            _isUpdating = false;
                        }
                        OnPropertyChanged();
                        OnPropertyChanged(nameof(SelectedMinButthurt));
                        RecalculateMatrix();
                        OnDocumentModified();
                    }
                }
            }
        }

        public int SelectedWeight
        {
            get => SelectedEntry?.Weight ?? 1;
            set
            {
                if (SelectedEntry != null)
                {
                    int clamped = Math.Clamp(value, 1, 100);
                    if (SelectedEntry.Weight != clamped)
                    {
                        PushUndoState();
                        _isUpdating = true;
                        try
                        {
                            SelectedEntry.Weight = clamped;
                        }
                        finally
                        {
                            _isUpdating = false;
                        }
                        OnPropertyChanged();
                        RecalculateMatrix();
                        OnDocumentModified();
                    }
                }
            }
        }

        public string EntryCountBadgeText => string.Create(CultureInfo.InvariantCulture, $"{Entries.Count} Animation{(Entries.Count == 1 ? "" : "s")}");

        // ── Commands ────────────────────────────────────────────────────────
        public IRelayCommand UndoCommand { get; }
        public IRelayCommand RedoCommand { get; }
        public IRelayCommand SelectNextEntryCommand { get; }
        public IRelayCommand SelectPreviousEntryCommand { get; }
        public IRelayCommand AutoBalanceCommand { get; }
        public IRelayCommand<object> AutoBalanceStrategyCommand { get; }
        public IRelayCommand OpenSimulatorCommand { get; }
        public IRelayCommand AddEntryCommand { get; }
        public IRelayCommand<FlipperScheduleEntryViewModel?> DeleteEntryCommand { get; }
        public IRelayCommand<FlipperScheduleEntryViewModel?> DuplicateEntryCommand { get; }
        public IRelayCommand ImportManifestCommand { get; }
        public IRelayCommand ExportManifestCommand { get; }
        public IRelayCommand ImportFolderCommand { get; }
        public IRelayCommand ImportZipCommand { get; }
        public IRelayCommand ImportHexpCommand { get; }
        public IRelayCommand ExportSelectedHexpCommand { get; }
        public IRelayCommand ExportAllHexpCommand { get; }
        public IRelayCommand ExportAssetPackDirectoryCommand { get; }
        public IRelayCommand ExportAssetPackZipCommand { get; }
        public IRelayCommand CopyManifestCommand { get; }
        public IRelayCommand ToggleModeCommand { get; }
        public IRelayCommand SyncFromWorkspaceCommand { get; }
        public IRelayCommand<FlipperScheduleEntryViewModel> NavigateToEntryTabCommand { get; }
        public IRelayCommand<string> NavigateToEntryByNameCommand { get; }
        public IRelayCommand InspectClickedCellCommand { get; }
        public IRelayCommand SetBoundsToClickedCellCommand { get; }
        public IRelayCommand<object> SetStagePresetCommand { get; }
        public IRelayCommand<object> SetMoodPresetCommand { get; }
        public IRelayCommand<object> StepMinLevelCommand { get; }
        public IRelayCommand<object> StepMaxLevelCommand { get; }
        public IRelayCommand<object> StepMinMoodCommand { get; }
        public IRelayCommand<object> StepMaxMoodCommand { get; }
        public IRelayCommand<object> StepWeightCommand { get; }
        public IRelayCommand<FlipperCellProbability> IncreaseProbabilityWeightCommand { get; }
        public IRelayCommand<FlipperCellProbability> DecreaseProbabilityWeightCommand { get; }
        public IRelayCommand TogglePreviewPlayCommand { get; }
        public IRelayCommand TogglePreviewLoopCommand { get; }
        public IRelayCommand CyclePreviewSpeedCommand { get; }
        public IRelayCommand<object> SetPreviewSpeedCommand { get; }
        public IRelayCommand PreviewNextFrameCommand { get; }
        public IRelayCommand PreviewPrevFrameCommand { get; }
        public IRelayCommand OpenSelectedInCanvasCommand { get; }
        public IRelayCommand OpenCellAnimationCommand { get; }
        public IRelayCommand AddActiveCanvasTabCommand { get; }
        public IRelayCommand CreateTabForGapCommand { get; }
        public IRelayCommand DeselectEntryCommand { get; }
        public IRelayCommand ToggleDragModeCommand { get; }
        public IRelayCommand<string> SetDragModeCommand { get; }
        public IRelayCommand AssignSelectedEntryToRegionCommand { get; }
        public IRelayCommand CreateTabForRegionGapsCommand { get; }
        public IRelayCommand ClearRegionSelectionCommand { get; }
        public IRelayCommand TestCellInSimulatorCommand { get; }
        public IRelayCommand<object> OpenCellInSimulatorCommand { get; }

        // ── View Mode, Solo & Bulk Operations Commands ───────────────────────
        public IRelayCommand<object> SetViewModeCommand { get; }
        public IRelayCommand ToggleSoloModeCommand { get; }
        public IRelayCommand<object> BulkShiftLevelCommand { get; }
        public IRelayCommand<object> BulkExpandLevelCommand { get; }
        public IRelayCommand<object> BulkShrinkLevelCommand { get; }
        public IRelayCommand<object> BulkShiftMoodCommand { get; }
        public IRelayCommand<object> BulkExpandMoodCommand { get; }
        public IRelayCommand<object> BulkShrinkMoodCommand { get; }
        public IRelayCommand<object> BulkShiftWeightCommand { get; }
        public IRelayCommand<object> BulkSetWeightCommand { get; }
        public IRelayCommand<object> BulkSetStageCommand { get; }
        public IRelayCommand BulkDeleteCommand { get; }
        public IRelayCommand BulkDuplicateCommand { get; }
        public IRelayCommand SelectAllCommand { get; }
        public IRelayCommand ClearSelectionCommand { get; }
        public IRelayCommand ToggleSelectAllCommand { get; }
        public IRelayCommand<object> SelectAllInStageCommand { get; }
        public IRelayCommand<string> SortByColumnCommand { get; }

        // ── Search, Filter & Diagnostic Commands ───────────────────────────
        public IRelayCommand ClearSearchFilterCommand { get; }
        public IRelayCommand<string> SetStageFilterCommand { get; }
        public IRelayCommand<string> SetMoodFilterCommand { get; }
        public IRelayCommand ResetFiltersCommand { get; }
        public IRelayCommand<string> SetGalleryCardSizeCommand { get; }
        public IRelayCommand<FlipperDiagnosticItemViewModel> ResolveDiagnosticCommand { get; }
        public IRelayCommand FixAllDiagnosticsCommand { get; }
        public IRelayCommand<string> QuickFixDiagnosticCommand { get; }

        public FlipperScheduleMatrixViewModel(
            IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>? pack = null,
            string packName = "Flipper Asset Pack",
            IFlipperWindowManager? windowManager = null,
            IDialogService? dialogService = null,
            IUserFeedbackService? feedbackService = null,
            IClipboardService? clipboardService = null,
            IWorkspaceTabService? tabService = null,
            IFlipperExportService? exportService = null,
            IFlipperImportService? importService = null)
        {
            _importedPack = pack;
            _packName = packName;
            _windowManager = windowManager;
            _dialogService = dialogService;
            _feedbackService = feedbackService;
            _clipboardService = clipboardService;
            _tabService = tabService;
            _exportService = exportService ?? new FlipperExportService();
            _importService = importService ?? new FlipperImportService();

            var rawEntries = new List<FlipperManifestEntry>();
            if (pack != null && pack.Count > 0)
            {
                foreach (var (Name, Sprite, ManifestEntry) in pack)
                {
                    string clean = Name?.Trim().TrimStart('*').Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(clean) && Sprite != null)
                    {
                        _animationSprites[clean] = Sprite;
                    }
                }

                rawEntries = [.. pack.Select(p => new FlipperManifestEntry
                {
                    Name = p.ManifestEntry.Name,
                    MinLevel = p.ManifestEntry.MinLevel,
                    MaxLevel = p.ManifestEntry.MaxLevel,
                    MinButthurt = p.ManifestEntry.MinButthurt,
                    MaxButthurt = p.ManifestEntry.MaxButthurt,
                    Weight = p.ManifestEntry.Weight,
                })];

                if (rawEntries.TrueForAll(e => e.MaxLevel <= 3))
                {
                    _isStockMode = true;
                }
            }
            else
            {
                var openSprites = _tabService?.GetAllOpenSprites();
                if (openSprites != null && openSprites.Count > 0)
                {
                    var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var uniqueEntries = new List<FlipperManifestEntry>();
                    foreach (var (Title, Sprite) in openSprites)
                    {
                        string cleanName = string.IsNullOrWhiteSpace(Title) ? "Animation" : Title.Trim().TrimStart('*').Trim();
                        if (string.IsNullOrWhiteSpace(cleanName)) cleanName = "Animation";
                        if (Sprite != null)
                        {
                            _animationSprites[cleanName] = Sprite;
                        }

                        if (seenNames.Add(cleanName))
                        {
                            uniqueEntries.Add(new FlipperManifestEntry
                            {
                                Name = cleanName,
                                MinLevel = 1,
                                MaxLevel = 30,
                                MinButthurt = 0,
                                MaxButthurt = 14,
                                Weight = 1,
                            });
                        }
                    }
                    rawEntries = FlipperScheduleMatrix.AutoBalanceEntries(uniqueEntries, FlipperAutoBalanceStrategy.LinearLevels, 30);
                }
                else
                {
                    rawEntries =
                    [
                        new FlipperManifestEntry { Name = "anim_baby", MinLevel = 1, MaxLevel = 10, MinButthurt = 0, MaxButthurt = 14, Weight = 1 },
                        new FlipperManifestEntry { Name = "anim_teen", MinLevel = 11, MaxLevel = 20, MinButthurt = 0, MaxButthurt = 14, Weight = 1 },
                        new FlipperManifestEntry { Name = "anim_adult", MinLevel = 21, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 },
                    ];
                    _animationSprites["anim_baby"] = new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome, FrameRateFps = 10 };
                    _animationSprites["anim_teen"] = new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome, FrameRateFps = 10 };
                    _animationSprites["anim_adult"] = new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome, FrameRateFps = 10 };
                }
            }

            _matrix = new FlipperScheduleMatrix(rawEntries, MaxAllowedLevel);

            Entries.CollectionChanged += OnEntriesCollectionChanged;

            foreach (var entry in rawEntries)
            {
                var evm = new FlipperScheduleEntryViewModel(entry);
                Entries.Add(evm);
            }

            if (Entries.Count > 0)
            {
                SelectedEntry = Entries[0];
            }

            UndoCommand = new RelayCommand(Undo, () => CanUndo);
            RedoCommand = new RelayCommand(Redo, () => CanRedo);
            SelectNextEntryCommand = new RelayCommand(SelectNextEntry);
            SelectPreviousEntryCommand = new RelayCommand(SelectPreviousEntry);

            AutoBalanceCommand = new RelayCommand(AutoBalance);
            AutoBalanceStrategyCommand = new RelayCommand<object>(param =>
            {
                string? strategyStr = param?.ToString();
                if (!string.IsNullOrEmpty(strategyStr) && Enum.TryParse<FlipperAutoBalanceStrategy>(strategyStr, ignoreCase: true, out var strategy))
                {
                    AutoBalanceWithStrategy(strategy);
                }
                else
                {
                    AutoBalance();
                }
            });

            OpenSimulatorCommand = new RelayCommand(OpenSimulator);
            AddEntryCommand = new RelayCommand(AddEntry);
            DeleteEntryCommand = new RelayCommand<FlipperScheduleEntryViewModel?>(DeleteEntry);
            DuplicateEntryCommand = new RelayCommand<FlipperScheduleEntryViewModel?>(DuplicateEntry);
            ImportManifestCommand = new RelayCommand(ImportManifest);
            ExportManifestCommand = new RelayCommand(ExportManifest);
            ImportFolderCommand = new RelayCommand(() => ImportAssetPackFolder());
            ImportZipCommand = new RelayCommand(() => ImportAssetPackZip());
            ImportHexpCommand = new RelayCommand(() => ImportHexpAnimations());
            ExportSelectedHexpCommand = new RelayCommand(() => ExportSelectedToHexp());
            ExportAllHexpCommand = new RelayCommand(() => ExportAllToHexp());
            ExportAssetPackDirectoryCommand = new RelayCommand(() => ExportAssetPackFolder());
            ExportAssetPackZipCommand = new RelayCommand(() => ExportAssetPackZip());
            CopyManifestCommand = new RelayCommand(CopyManifest);
            ToggleModeCommand = new RelayCommand(ToggleMode);
            SyncFromWorkspaceCommand = new RelayCommand(() => SyncFromWorkspace(silent: false));
            NavigateToEntryTabCommand = new RelayCommand<FlipperScheduleEntryViewModel>(NavigateToEntryTab);
            NavigateToEntryByNameCommand = new RelayCommand<string>(NavigateToEntryByName);
            InspectClickedCellCommand = new RelayCommand(() => InspectCell(SelectedCellLevel, SelectedCellMood));
            SetBoundsToClickedCellCommand = new RelayCommand(() => SetBoundsToCell(SelectedCellLevel, SelectedCellMood));

            SetStagePresetCommand = new RelayCommand<object>(param => ApplyStagePreset(param?.ToString()));
            SetMoodPresetCommand = new RelayCommand<object>(param => ApplyMoodPreset(param?.ToString()));

            StepMinLevelCommand = new RelayCommand<object>(param => SelectedMinLevel = Math.Clamp(SelectedMinLevel + ParseDelta(param), 1, MaxAllowedLevel));
            StepMaxLevelCommand = new RelayCommand<object>(param => SelectedMaxLevel = Math.Clamp(SelectedMaxLevel + ParseDelta(param), 1, MaxAllowedLevel));
            StepMinMoodCommand = new RelayCommand<object>(param => SelectedMinButthurt = Math.Clamp(SelectedMinButthurt + ParseDelta(param), 0, 14));
            StepMaxMoodCommand = new RelayCommand<object>(param => SelectedMaxButthurt = Math.Clamp(SelectedMaxButthurt + ParseDelta(param), 0, 14));
            StepWeightCommand = new RelayCommand<object>(param => SelectedWeight = Math.Clamp(SelectedWeight + ParseDelta(param), 1, 100));
            IncreaseProbabilityWeightCommand = new RelayCommand<FlipperCellProbability>(prob => StepEntryWeight(prob?.Name, 1));
            DecreaseProbabilityWeightCommand = new RelayCommand<FlipperCellProbability>(prob => StepEntryWeight(prob?.Name, -1));

            TogglePreviewPlayCommand = new RelayCommand(TogglePreviewPlay);
            TogglePreviewLoopCommand = new RelayCommand(TogglePreviewLoop);
            CyclePreviewSpeedCommand = new RelayCommand(CyclePreviewSpeed);
            SetPreviewSpeedCommand = new RelayCommand<object>(param => SetPreviewSpeed(param));
            PreviewNextFrameCommand = new RelayCommand(PreviewNextFrame);
            PreviewPrevFrameCommand = new RelayCommand(PreviewPrevFrame);
            OpenSelectedInCanvasCommand = new RelayCommand(OpenSelectedInCanvas);
            OpenCellAnimationCommand = new RelayCommand(OpenCellAnimation);
            AddActiveCanvasTabCommand = new RelayCommand(AddActiveCanvasTab);
            CreateTabForGapCommand = new RelayCommand(CreateTabForGap);
            DeselectEntryCommand = new RelayCommand(DeselectEntry);
            ToggleDragModeCommand = new RelayCommand(ToggleDragMode);
            SetDragModeCommand = new RelayCommand<string>(SetDragModeByString);
            AssignSelectedEntryToRegionCommand = new RelayCommand(AssignSelectedEntryToRegion, () => HasSelectedRegion && HasSelectedEntry);
            CreateTabForRegionGapsCommand = new RelayCommand(CreateTabForRegionGaps, () => HasSelectedRegionGap);
            ClearRegionSelectionCommand = new RelayCommand(ClearRegionSelection);
            TestCellInSimulatorCommand = new RelayCommand(() => OpenCellInSimulator(SelectedCellLevel, SelectedCellMood));
            OpenCellInSimulatorCommand = new RelayCommand<object>(param =>
            {
                if (param is (int lvl, int mood))
                {
                    OpenCellInSimulator(lvl, mood);
                }
                else if (param is FlipperCellProbability prob && !string.IsNullOrEmpty(prob.Name))
                {
                    var entry = Entries.FirstOrDefault(e => e.Name.Equals(prob.Name, StringComparison.OrdinalIgnoreCase));
                    if (entry != null)
                    {
                        OpenCellInSimulator(entry.MinLevel, entry.MinButthurt);
                    }
                    else
                    {
                        OpenCellInSimulator(SelectedCellLevel, SelectedCellMood);
                    }
                }
                else
                {
                    OpenCellInSimulator(SelectedCellLevel, SelectedCellMood);
                }
            });

            SetViewModeCommand = new RelayCommand<object>(param =>
            {
                if (param is AssetPackViewMode mode) CurrentViewMode = mode;
                else if (param is string s && Enum.TryParse<AssetPackViewMode>(s, ignoreCase: true, out var parsedMode))
                {
                    CurrentViewMode = parsedMode;
                }
            });

            ToggleSoloModeCommand = new RelayCommand(() => IsSoloModeActive = !IsSoloModeActive);

            BulkShiftLevelCommand = new RelayCommand<object>(param => BulkShiftLevel(ParseDelta(param)));
            BulkExpandLevelCommand = new RelayCommand<object>(param => BulkExpandLevel(ParseDelta(param, 1)));
            BulkShrinkLevelCommand = new RelayCommand<object>(param => BulkShrinkLevel(ParseDelta(param, 1)));
            BulkShiftMoodCommand = new RelayCommand<object>(param => BulkShiftMood(ParseDelta(param)));
            BulkExpandMoodCommand = new RelayCommand<object>(param => BulkExpandMood(ParseDelta(param, 1)));
            BulkShrinkMoodCommand = new RelayCommand<object>(param => BulkShrinkMood(ParseDelta(param, 1)));
            BulkShiftWeightCommand = new RelayCommand<object>(param => BulkShiftWeight(ParseDelta(param)));
            BulkSetWeightCommand = new RelayCommand<object>(param =>
            {
                if (param is int w) BulkSetWeight(w);
                else if (param != null && int.TryParse(param.ToString(), out int parsedW)) BulkSetWeight(parsedW);
            });
            BulkSetStageCommand = new RelayCommand<object>(param => BulkSetStage(param?.ToString() ?? string.Empty));
            BulkDeleteCommand = new RelayCommand(BulkDelete);
            BulkDuplicateCommand = new RelayCommand(BulkDuplicate);
            SelectAllCommand = new RelayCommand(SelectAll);
            ClearSelectionCommand = new RelayCommand(ClearSelection);
            ToggleSelectAllCommand = new RelayCommand(ToggleSelectAll);
            SelectAllInStageCommand = new RelayCommand<object>(param => SelectAllInStage(param?.ToString() ?? string.Empty));
            SortByColumnCommand = new RelayCommand<string>(col => SortByColumn(col));

            ClearSearchFilterCommand = new RelayCommand(() => SearchFilterText = string.Empty);
            SetStageFilterCommand = new RelayCommand<string>(stage => SelectedStageFilter = stage ?? "All");
            SetMoodFilterCommand = new RelayCommand<string>(mood => SelectedMoodFilter = mood ?? "All");
            ResetFiltersCommand = new RelayCommand(ResetFilters);
            SetGalleryCardSizeCommand = new RelayCommand<string>(size => GalleryCardSize = size ?? "Standard");
            ResolveDiagnosticCommand = new RelayCommand<FlipperDiagnosticItemViewModel>(diag => diag?.ExecuteQuickFix());
            FixAllDiagnosticsCommand = new RelayCommand(FixAllDiagnostics, () => ActionableDiagnostics.Count > 0);
            QuickFixDiagnosticCommand = new RelayCommand<string>(code => ExecuteQuickFix(code ?? string.Empty, SelectedEntry));

            try
            {
                if (Application.Current?.Dispatcher != null)
                {
                    _previewTimer = new DispatcherTimer();
                    _previewTimer.Tick += OnPreviewTimerTick;
                }
            }
            catch
            {
                // Headless test fallback
            }

            RecalculateMatrix();
            InspectCell(1, 0);
            RefreshCurrentPreviewSprite();
        }

        private void OnPreviewTimerTick(object? sender, EventArgs e)
        {
            if (CurrentViewMode == AssetPackViewMode.SpriteGallery)
            {
                if (_activeHoverPlayingEntry != null && _activeHoverPlayingEntry.IsPreviewPlaying)
                {
                    _activeHoverPlayingEntry.StepNextPreviewFrame();
                }
                else
                {
                    for (int i = 0; i < FilteredEntries.Count; i++)
                    {
                        var entry = FilteredEntries[i];
                        if (entry.IsPreviewPlaying)
                        {
                            _activeHoverPlayingEntry = entry;
                            entry.StepNextPreviewFrame();
                            break;
                        }
                    }
                }
            }
            else
            {
                PreviewNextFrame();
            }
        }

        private static int ParseDelta(object? param, int defaultDelta = 1)
        {
            if (param is int i) return i;
            if (param is string s && int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsed)) return parsed;
            if (param is IConvertible c)
            {
                try { return Convert.ToInt32(c, System.Globalization.CultureInfo.InvariantCulture); } catch { }
            }
            return defaultDelta;
        }

        public void ToggleMode()
        {
            IsStockMode = !IsStockMode;
        }

        public void SetBoundsToCell(int level, int mood)
        {
            if (SelectedEntry == null) return;
            SetSelectedEntryBounds(level, level, mood, mood);
        }

        public void NavigateToEntryTab(FlipperScheduleEntryViewModel? entry)
        {
            entry ??= SelectedEntry;
            if (entry == null) return;

            string animName = entry.Name.Trim().TrimStart('*').Trim();
            if (string.IsNullOrWhiteSpace(animName)) animName = "Animation";

            if (_tabService != null)
            {
                bool activated = _tabService.ActivateTabByTitle(animName);
                if (activated)
                {
                    SetStatus($"📍 Focused workspace tab for '{animName}'.");
                    return;
                }
            }

            SpriteState? targetSprite = null;
            if (_animationSprites.TryGetValue(animName, out var cachedSprite) && cachedSprite != null && cachedSprite.Frames.Count > 0)
            {
                targetSprite = cachedSprite;
            }
            else if (_importedPack != null)
            {
                var (Name, Sprite, ManifestEntry) = _importedPack.FirstOrDefault(p => p.Name.Equals(animName, StringComparison.OrdinalIgnoreCase));
                if (Sprite != null && Sprite.Frames.Count > 0)
                {
                    targetSprite = Sprite;
                }
            }

            targetSprite ??= new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome, FrameRateFps = 10 };

            targetSprite.IsAnimationEnabled = true;
            _animationSprites[animName] = targetSprite;

            string? linkedPath = null;
            if (_animationFilePaths.TryGetValue(animName, out var p) && !string.IsNullOrEmpty(p))
            {
                linkedPath = p;
            }

            if (_tabService != null)
            {
                if (!string.IsNullOrEmpty(linkedPath))
                {
                    _tabService.OpenSpriteInTab(targetSprite, animName, linkedPath);
                }
                else
                {
                    _tabService.OpenSpriteInTab(targetSprite, animName);
                }
            }

            SetStatus($"🎨 Opened canvas tab for '{animName}'.");
        }

        public void NavigateToEntryByName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            string cleanName = name.Trim().TrimStart('*').Trim();
            var match = Entries.FirstOrDefault(e => e.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase) || e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                SelectedEntry = match;
                NavigateToEntryTab(match);
            }
            else
            {
                var tempEntry = new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = cleanName });
                NavigateToEntryTab(tempEntry);
            }
        }

        public void SetSelectedEntryBounds(int minLvl, int maxLvl, int minMood, int maxMood)
        {
            if (SelectedEntry == null) return;
            int safeMinL = Math.Clamp(minLvl, 1, MaxAllowedLevel);
            int safeMaxL = Math.Clamp(maxLvl, 1, MaxAllowedLevel);
            int safeMinM = Math.Clamp(minMood, 0, 14);
            int safeMaxM = Math.Clamp(maxMood, 0, 14);

            if (safeMinL > safeMaxL) (safeMinL, safeMaxL) = (safeMaxL, safeMinL);
            if (safeMinM > safeMaxM) (safeMinM, safeMaxM) = (safeMaxM, safeMinM);

            if (SelectedEntry.MinLevel == safeMinL &&
                SelectedEntry.MaxLevel == safeMaxL &&
                SelectedEntry.MinButthurt == safeMinM &&
                SelectedEntry.MaxButthurt == safeMaxM)
            {
                return;
            }

            PushUndoState();

            SelectedEntry.Entry.MinLevel = safeMinL;
            SelectedEntry.Entry.MaxLevel = safeMaxL;
            SelectedEntry.Entry.MinButthurt = safeMinM;
            SelectedEntry.Entry.MaxButthurt = safeMaxM;

            SelectedEntry.NotifyBoundsChanged();
            OnPropertyChanged(nameof(SelectedMinLevel));
            OnPropertyChanged(nameof(SelectedMaxLevel));
            OnPropertyChanged(nameof(SelectedMinButthurt));
            OnPropertyChanged(nameof(SelectedMaxButthurt));

            RecalculateMatrix();
            OnDocumentModified();
        }

        private void ApplyStagePreset(string? stage)
        {
            if (SelectedEntry == null || string.IsNullOrEmpty(stage)) return;

            if (_isStockMode)
            {
                switch (stage.ToLowerInvariant())
                {
                    case "baby":
                        SetSelectedEntryBounds(1, 1, SelectedMinButthurt, SelectedMaxButthurt);
                        break;
                    case "teen":
                        SetSelectedEntryBounds(2, 2, SelectedMinButthurt, SelectedMaxButthurt);
                        break;
                    case "adult":
                        SetSelectedEntryBounds(3, 3, SelectedMinButthurt, SelectedMaxButthurt);
                        break;
                    case "all":
                        SetSelectedEntryBounds(1, 3, SelectedMinButthurt, SelectedMaxButthurt);
                        break;
                }
            }
            else
            {
                switch (stage.ToLowerInvariant())
                {
                    case "baby":
                        SetSelectedEntryBounds(1, 9, SelectedMinButthurt, SelectedMaxButthurt);
                        break;
                    case "teen":
                        SetSelectedEntryBounds(10, 19, SelectedMinButthurt, SelectedMaxButthurt);
                        break;
                    case "adult":
                        SetSelectedEntryBounds(20, 30, SelectedMinButthurt, SelectedMaxButthurt);
                        break;
                    case "all":
                        SetSelectedEntryBounds(1, 30, SelectedMinButthurt, SelectedMaxButthurt);
                        break;
                }
            }
        }

        private void ApplyMoodPreset(string? mood)
        {
            if (SelectedEntry == null || string.IsNullOrEmpty(mood)) return;

            switch (mood.ToLowerInvariant())
            {
                case "happy":
                    SetSelectedEntryBounds(SelectedMinLevel, SelectedMaxLevel, 0, 4);
                    break;
                case "neutral":
                    SetSelectedEntryBounds(SelectedMinLevel, SelectedMaxLevel, 5, 8);
                    break;
                case "angry":
                    SetSelectedEntryBounds(SelectedMinLevel, SelectedMaxLevel, 9, 14);
                    break;
                case "all":
                    SetSelectedEntryBounds(SelectedMinLevel, SelectedMaxLevel, 0, 14);
                    break;
            }
        }

        private void OnModeChanged()
        {
            int maxLvl = MaxAllowedLevel;
            foreach (var entry in Entries)
            {
                entry.SwitchMode(_isStockMode);
            }

            if (HasSelectedRegion)
            {
                SelectRegion(
                    Math.Clamp(SelectedRegionMinLevel, 1, maxLvl),
                    Math.Clamp(SelectedRegionMaxLevel, 1, maxLvl),
                    SelectedRegionMinMood,
                    SelectedRegionMaxMood
                );
            }

            RecalculateMatrix();
            InspectCell(Math.Clamp(SelectedCellLevel, 1, maxLvl), SelectedCellMood);
        }

        public void RecalculateMatrix()
        {
            if (_isUpdating) return;
            _isUpdating = true;
            try
            {
                var manifestEntries = Entries.Select(e => e.Entry).ToList();
                _matrix = new FlipperScheduleMatrix(manifestEntries, MaxAllowedLevel);
                UpdateStats();
                UpdateInspectedProbabilities();

                var manifest = new FlipperManifest { Entries = manifestEntries };
                _validationDiagnostics = manifest.Validate(isMomentum: !_isStockMode);
                var errors = _validationDiagnostics.Where(d => d.Severity == FlipperValidationSeverity.Error).ToList();
                var warnings = _validationDiagnostics.Where(d => d.Severity == FlipperValidationSeverity.Warning).ToList();

                if (errors.Count > 0)
                {
                    HasValidationIssues = true;
                    ValidationStatusText = string.Create(CultureInfo.InvariantCulture, $"❌ {errors.Count} Error(s): {errors[0].Message}");
                }
                else if (warnings.Count > 0)
                {
                    HasValidationIssues = true;
                    ValidationStatusText = string.Create(CultureInfo.InvariantCulture, $"⚠️ {warnings.Count} Warning(s): {warnings[0].Message}");
                }
                else
                {
                    HasValidationIssues = false;
                    ValidationStatusText = "✅ Valid Manifest";
                }
                OnPropertyChanged(nameof(ValidationStatusText));
                OnPropertyChanged(nameof(HasValidationIssues));
                OnPropertyChanged(nameof(ValidationDiagnostics));
                OnPropertyChanged(nameof(EntryCountBadgeText));

                // Populate per-entry diagnostics and actionable diagnostics list
                ActionableDiagnostics.Clear();
                var nameCounts = manifestEntries
                    .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

                var importedMap = _importedPack != null
                    ? _importedPack.Where(p => p.Sprite != null && !string.IsNullOrWhiteSpace(p.Name))
                                   .GroupBy(p => p.Name.Trim().TrimStart('*').Trim(), StringComparer.OrdinalIgnoreCase)
                                   .ToDictionary(g => g.Key, g => g.First().Sprite, StringComparer.OrdinalIgnoreCase)
                    : null;

                foreach (var entryVm in Entries)
                {
                    var entryDiags = entryVm.Entry.Validate(!_isStockMode);
                    if (!string.IsNullOrWhiteSpace(entryVm.Name) && nameCounts.TryGetValue(entryVm.Name, out int cnt) && cnt > 1)
                    {
                        entryDiags.Add(new FlipperValidationDiagnostic(
                            FlipperValidationSeverity.Error,
                            "FZ011",
                            $"Duplicate animation name '{entryVm.Name}' found in manifest.",
                            nameof(entryVm.Name)));
                    }

                    string cleanName = entryVm.Name.Trim().TrimStart('*').Trim();
                    bool hasCachedSprite = !string.IsNullOrWhiteSpace(cleanName) && _animationSprites.TryGetValue(cleanName, out var sp) && sp != null;
                    bool hasImportedSprite = importedMap != null && !string.IsNullOrWhiteSpace(cleanName) && importedMap.ContainsKey(cleanName);

                    if (!string.IsNullOrWhiteSpace(cleanName) && !hasCachedSprite && !hasImportedSprite)
                    {
                        entryDiags.Add(new FlipperValidationDiagnostic(
                            FlipperValidationSeverity.Warning,
                            "FZ_MISSING_SPRITE",
                            $"No sprite asset found for '{entryVm.Name}'. Click quick fix to generate canvas sprite.",
                            nameof(entryVm.Name)));
                    }
                    else if (hasCachedSprite && _animationSprites.TryGetValue(cleanName, out var sprite) && sprite != null)
                    {
                        if (sprite.Width > 128 || sprite.Height > 64 || sprite.Width <= 0 || sprite.Height <= 0)
                        {
                            entryDiags.Add(new FlipperValidationDiagnostic(
                                FlipperValidationSeverity.Error,
                                "FZM001",
                                string.Create(CultureInfo.InvariantCulture, $"Sprite dimensions {sprite.Width}x{sprite.Height} exceed 128x64 limits."),
                                nameof(entryVm.Name)));
                        }

                        if (sprite.FlipperCycle?.FramesOrder != null && sprite.FlipperCycle.FramesOrder.Length > 0 && sprite.Frames.Count > 0)
                        {
                            for (int i = 0; i < sprite.FlipperCycle.FramesOrder.Length; i++)
                            {
                                if (sprite.FlipperCycle.FramesOrder[i] < 0 || sprite.FlipperCycle.FramesOrder[i] >= sprite.Frames.Count)
                                {
                                    entryDiags.Add(new FlipperValidationDiagnostic(
                                        FlipperValidationSeverity.Error,
                                        "FZM004",
                                        string.Create(CultureInfo.InvariantCulture, $"Frames order index {sprite.FlipperCycle.FramesOrder[i]} out of bounds."),
                                        nameof(entryVm.Name)));
                                    break;
                                }
                            }
                        }
                    }

                    SpriteState? activeSprite = null;
                    if (hasCachedSprite && _animationSprites.TryGetValue(cleanName, out var spObj)) activeSprite = spObj;
                    else if (hasImportedSprite && importedMap != null && importedMap.TryGetValue(cleanName, out var impSp)) activeSprite = impSp;

                    string spriteSig = activeSprite == null
                        ? "null"
                        : string.Create(CultureInfo.InvariantCulture, $"{cleanName}:{activeSprite.Width}x{activeSprite.Height}:{activeSprite.Frames.Count}:{activeSprite.Layers.Count}:{activeSprite.FlipperCycle?.FramesOrder?.Length ?? 0}");

                    if (activeSprite != null)
                    {
                        entryVm.HasSprite = true;
                        entryVm.DimensionsText = string.Create(CultureInfo.InvariantCulture, $"{activeSprite.Width}×{activeSprite.Height}");
                        entryVm.FrameCountText = string.Create(CultureInfo.InvariantCulture, $"{activeSprite.Frames.Count} frame{(activeSprite.Frames.Count == 1 ? "" : "s")}");
                        entryVm.FrameCount = activeSprite.Frames.Count;
                        entryVm.FrameThumbnailProvider = () => RenderAllSpriteFrames(activeSprite, entryVm.SpriteThumbnail);

                        if (entryVm.SpriteThumbnail == null || entryVm.CachedSpriteSignature != spriteSig)
                        {
                            entryVm.CachedFrameThumbnails = null;
                            entryVm.SpriteThumbnail = RenderSpriteThumbnail(activeSprite, 0);
                            entryVm.CachedSpriteSignature = spriteSig;
                        }
                    }
                    else
                    {
                        entryVm.HasSprite = false;
                        entryVm.DimensionsText = "No Sprite";
                        entryVm.FrameCountText = "0 frames";
                        entryVm.FrameCount = 0;
                        entryVm.FrameThumbnailProvider = null;

                        if (entryVm.SpriteThumbnail == null || entryVm.CachedSpriteSignature != spriteSig)
                        {
                            entryVm.CachedFrameThumbnails = null;
                            entryVm.SpriteThumbnail = RenderSpriteThumbnail(sprite: null);
                            entryVm.CachedSpriteSignature = spriteSig;
                        }
                    }

                    entryVm.SetValidationDiagnostics(entryDiags);

                    foreach (var diag in entryDiags)
                    {
                        ActionableDiagnostics.Add(new FlipperDiagnosticItemViewModel(diag, entryVm, ExecuteQuickFix));
                    }
                }

                if (_matrix.UncoveredStatesCount > 0)
                {
                    var gapDiag = new FlipperValidationDiagnostic(
                        FlipperValidationSeverity.Warning,
                        "GAP",
                        string.Create(CultureInfo.InvariantCulture, $"{_matrix.UncoveredStatesCount} uncovered matrix state(s) detected (deadzone gaps)."),
                        "Matrix");
                    ActionableDiagnostics.Add(new FlipperDiagnosticItemViewModel(gapDiag, targetEntry: null, ExecuteQuickFix));
                }

                OnPropertyChanged(nameof(ActionableDiagnostics));
                OnPropertyChanged(nameof(HasActionableDiagnostics));
                OnPropertyChanged(nameof(ActionableDiagnosticsSummaryText));
                (FixAllDiagnosticsCommand as RelayCommand)?.NotifyCanExecuteChanged();

                ApplyEntryFilter();

                MatrixRedrawRequested?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                _isUpdating = false;
            }
        }

        public void UpdateStats()
        {
            int covered = _matrix.CoveredCellsCount;
            double pct = _matrix.CoveragePercentage;
            int total = _matrix.TotalCells;
            int gaps = _matrix.UncoveredCellsCount;

            CoverageBadgeText = string.Create(CultureInfo.InvariantCulture, $"{pct:F0}% Coverage");
            MatrixStatsText = string.Create(CultureInfo.InvariantCulture, $"{covered} / {total} States Covered • {gaps} Gaps • Max Overlap: {_matrix.MaxCollidingAnimations}");
        }

        public void HoverCell(int level, int mood)
        {
            int safeLvl = Math.Clamp(level, 1, MaxAllowedLevel);
            int safeMood = Math.Clamp(mood, 0, 14);

            string stage = _isStockMode
                ? (safeLvl == 1 ? "👶 Baby (L1)" : (safeLvl == 2 ? "👦 Teen (L2)" : "🐬 Adult (L3)"))
                : (safeLvl <= 9 ? "👶 Baby (L1-9)" : (safeLvl <= 19 ? "👦 Teen (L10-19)" : "🐬 Adult (L20-30)"));

            string moodDesc = safeMood <= 4 ? "😊 Happy" : (safeMood <= 9 ? "😐 Neutral" : "😡 Angry");

            var cell = _matrix.GetCell(safeLvl, safeMood);
            if (!cell.HasCoverage)
            {
                CellHoverInfoText = string.Create(CultureInfo.InvariantCulture, $"Level {safeLvl} ({stage}) • Mood {safeMood} ({moodDesc}) ➔ ⚠️ NO COVERAGE (Gap)");
            }
            else
            {
                var probsList = cell.MatchingEntries.Select(m =>
                {
                    double p = cell.GetProbability(m.Name) * 100.0;
                    bool isSelected = SelectedEntry != null && m.Name.Equals(SelectedEntry.Name, StringComparison.OrdinalIgnoreCase);
                    return isSelected ? string.Create(CultureInfo.InvariantCulture, $"🎯 [{m.Name}: {p:F0}%]") : string.Create(CultureInfo.InvariantCulture, $"{m.Name} ({p:F0}%)");
                }).ToList();

                if (probsList.Count > 4)
                {
                    var topProbs = probsList.Take(3);
                    int remaining = probsList.Count - 3;
                    CellHoverInfoText = string.Create(CultureInfo.InvariantCulture, $"Level {safeLvl} ({stage}) • Mood {safeMood} ({moodDesc}) ➔ {probsList.Count} anims: {string.Join(", ", topProbs)} (+{remaining} more)");
                }
                else
                {
                    CellHoverInfoText = string.Create(CultureInfo.InvariantCulture, $"Level {safeLvl} ({stage}) • Mood {safeMood} ({moodDesc}) ➔ {string.Join(", ", probsList)}");
                }
            }
        }

        public void ClearHover()
        {
            CellHoverInfoText = "Hover over any state on the grid to inspect scheduled animations.";
        }

        public void DeselectEntry()
        {
            SelectedEntry = null;
        }

        public void ToggleDragMode()
        {
            DragMode = DragMode == FlipperMatrixDragMode.AssignTarget
                ? FlipperMatrixDragMode.InspectRegion
                : FlipperMatrixDragMode.AssignTarget;
        }

        public void SetDragModeByString(string? mode)
        {
            if (string.Equals(mode, "Inspect", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, "InspectRegion", StringComparison.OrdinalIgnoreCase))
            {
                DragMode = FlipperMatrixDragMode.InspectRegion;
            }
            else
            {
                DragMode = FlipperMatrixDragMode.AssignTarget;
            }
        }

        public void SelectRegion(int minL, int maxL, int minM, int maxM)
        {
            int safeMinL = Math.Clamp(Math.Min(minL, maxL), 1, MaxAllowedLevel);
            int safeMaxL = Math.Clamp(Math.Max(minL, maxL), 1, MaxAllowedLevel);
            int safeMinM = Math.Clamp(Math.Min(minM, maxM), 0, 14);
            int safeMaxM = Math.Clamp(Math.Max(minM, maxM), 0, 14);

            SelectedRegionMinLevel = safeMinL;
            SelectedRegionMaxLevel = safeMaxL;
            SelectedRegionMinMood = safeMinM;
            SelectedRegionMaxMood = safeMaxM;

            int totalCells = (safeMaxL - safeMinL + 1) * (safeMaxM - safeMinM + 1);
            int gapCells = 0;
            var animWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int l = safeMinL; l <= safeMaxL; l++)
            {
                for (int m = safeMinM; m <= safeMaxM; m++)
                {
                    var cell = _matrix.GetCell(l, m);
                    if (!cell.HasCoverage)
                    {
                        gapCells++;
                    }
                    else
                    {
                        foreach (var match in cell.MatchingEntries)
                        {
                            animWeights.TryAdd(match.Name, match.Weight);
                        }
                    }
                }
            }

            int coveredCells = totalCells - gapCells;
            double covPct = totalCells > 0 ? (coveredCells * 100.0 / totalCells) : 0;

            SelectedRegionCellCount = totalCells;
            SelectedRegionGapCount = gapCells;
            SelectedRegionAnimationCount = animWeights.Count;
            SelectedRegionCoveragePercent = covPct;
            HasSelectedRegion = true;

            SelectedRegionSummaryText = string.Create(CultureInfo.InvariantCulture, $"Region: Level {safeMinL}–{safeMaxL} • Mood {safeMinM}–{safeMaxM} ({totalCells} cell{(totalCells == 1 ? "" : "s")}, {covPct:F0}% covered, {gapCells} gap{(gapCells == 1 ? "" : "s")})");
            CellHoverInfoText = SelectedRegionSummaryText;

            int totalWeight = animWeights.Values.Sum();
            var probs = animWeights.Select(kv =>
            {
                double prob = totalWeight > 0 ? ((double)kv.Value / totalWeight) : 0;
                double pct = prob * 100.0;
                return new FlipperCellProbability(kv.Key, kv.Value, prob, pct);
            }).OrderByDescending(p => p.Percentage).ToList();

            SelectedRegionProbabilities = new ObservableCollection<FlipperCellProbability>(probs);

            OnPropertyChanged(nameof(HasSelectedRegionGap));
            MatrixRedrawRequested?.Invoke(this, EventArgs.Empty);
        }

        public void ClearRegionSelection()
        {
            if (HasSelectedRegion)
            {
                HasSelectedRegion = false;
                SelectedRegionProbabilities.Clear();
                OnPropertyChanged(nameof(HasSelectedRegionGap));
                MatrixRedrawRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        public void AssignSelectedEntryToRegion()
        {
            if (!HasSelectedRegion || SelectedEntry == null) return;
            SetSelectedEntryBounds(SelectedRegionMinLevel, SelectedRegionMaxLevel, SelectedRegionMinMood, SelectedRegionMaxMood);
            SetStatus(string.Create(CultureInfo.InvariantCulture, $"🎯 Assigned '{SelectedEntry.Name}' to Region Level {SelectedRegionMinLevel}–{SelectedRegionMaxLevel}, Mood {SelectedRegionMinMood}–{SelectedRegionMaxMood}."));
        }

        public void CreateTabForRegion(int minL, int maxL, int minM, int maxM)
        {
            int safeMinL = Math.Clamp(Math.Min(minL, maxL), 1, MaxAllowedLevel);
            int safeMaxL = Math.Clamp(Math.Max(minL, maxL), 1, MaxAllowedLevel);
            int safeMinM = Math.Clamp(Math.Min(minM, maxM), 0, 14);
            int safeMaxM = Math.Clamp(Math.Max(minM, maxM), 0, 14);

            string animName = safeMinL == safeMaxL && safeMinM == safeMaxM
                ? string.Create(CultureInfo.InvariantCulture, $"anim_L{safeMinL}_M{safeMinM}")
                : string.Create(CultureInfo.InvariantCulture, $"anim_L{safeMinL}_{safeMaxL}_M{safeMinM}_{safeMaxM}");
            int suffix = 1;
            while (Entries.Any(e => e.Name.Equals(animName, StringComparison.OrdinalIgnoreCase)))
            {
                animName = string.Create(CultureInfo.InvariantCulture, $"{animName}_{suffix++}");
            }

            PushUndoState();

            var newSprite = new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome, FrameRateFps = 10, IsAnimationEnabled = true };
            _animationSprites[animName] = newSprite;

            if (_tabService != null)
            {
                _tabService.OpenSpriteInTab(newSprite, animName);
            }

            var newEntry = new FlipperManifestEntry
            {
                Name = animName,
                MinLevel = safeMinL,
                MaxLevel = safeMaxL,
                MinButthurt = safeMinM,
                MaxButthurt = safeMaxM,
                Weight = 1,
            };

            var vm = new FlipperScheduleEntryViewModel(newEntry);
            Entries.Add(vm);
            SelectedEntry = vm;
            RecalculateMatrix();
            SelectRegion(safeMinL, safeMaxL, safeMinM, safeMaxM);

            SetStatus(string.Create(CultureInfo.InvariantCulture, $"✨ Created canvas tab '{animName}' covering Level {safeMinL}–{safeMaxL}, Mood {safeMinM}–{safeMaxM}."));
        }

        public void CreateTabForRegionGaps()
        {
            if (!HasSelectedRegion || SelectedRegionGapCount == 0) return;
            CreateTabForRegion(SelectedRegionMinLevel, SelectedRegionMaxLevel, SelectedRegionMinMood, SelectedRegionMaxMood);
        }

        public void UpdateDragSelectionTelemetry(int minLevel, int maxLevel, int minMood, int maxMood)
        {
            int minL = Math.Clamp(Math.Min(minLevel, maxLevel), 1, MaxAllowedLevel);
            int maxL = Math.Clamp(Math.Max(minLevel, maxLevel), 1, MaxAllowedLevel);
            int minM = Math.Clamp(Math.Min(minMood, maxMood), 0, 14);
            int maxM = Math.Clamp(Math.Max(minMood, maxMood), 0, 14);

            int cellCount = (maxL - minL + 1) * (maxM - minM + 1);
            if (IsAssignDragMode && SelectedEntry != null)
            {
                CellHoverInfoText = string.Create(CultureInfo.InvariantCulture, $"🎯 Setting '{SelectedEntry.Name}' bounds → Level {minL}–{maxL} • Mood {minM}–{maxM} ({cellCount} cell{(cellCount == 1 ? "" : "s")})");
            }
            else
            {
                CellHoverInfoText = string.Create(CultureInfo.InvariantCulture, $"🔍 Selecting region: Level {minL}–{maxL} • Mood {minM}–{maxM} ({cellCount} cell{(cellCount == 1 ? "" : "s")})");
            }
        }

        public void InspectCell(int level, int mood)
        {
            SelectedCellLevel = Math.Clamp(level, 1, MaxAllowedLevel);
            SelectedCellMood = Math.Clamp(mood, 0, 14);

            string stage = _isStockMode
                ? (SelectedCellLevel == 1 ? "Baby (L1)" : (SelectedCellLevel == 2 ? "Teen (L2)" : "Adult (L3)"))
                : (SelectedCellLevel <= 9 ? "Baby (L1-9)" : (SelectedCellLevel <= 19 ? "Teen (L10-19)" : "Adult (L20-30)"));

            string moodDesc = SelectedCellMood <= 4 ? "Happy (0-4)" : (SelectedCellMood <= 9 ? "Neutral (5-9)" : "Angry (10-14)");

            SelectedCellSummaryText = string.Create(CultureInfo.InvariantCulture, $"Level {SelectedCellLevel} ({stage}) • Mood {SelectedCellMood} ({moodDesc})");
            UpdateInspectedProbabilities();
            MatrixRedrawRequested?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateInspectedProbabilities()
        {
            var cell = _matrix.GetCell(SelectedCellLevel, SelectedCellMood);
            var probs = _matrix.GetCellProbabilities(SelectedCellLevel, SelectedCellMood);
            SelectedCellProbabilities = new ObservableCollection<FlipperCellProbability>(probs);

            int totalWeight = cell.TotalWeight;
            int count = cell.MatchingEntries.Count;
            InspectedCellTotalWeight = totalWeight;
            InspectedCellAnimationCount = count;

            if (count == 0)
            {
                InspectedCellTotalWeightText = "0% Coverage (Gap)";
                InspectedCellBreakdownText = "No animations scheduled";
            }
            else
            {
                InspectedCellTotalWeightText = string.Create(CultureInfo.InvariantCulture, $"Total Weight: {totalWeight} ({count} anim{(count == 1 ? "" : "s")})");
                InspectedCellBreakdownText = string.Join(" • ", probs.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Name}: {p.Percentage:F0}% (W:{p.Weight})")));
            }

            OnPropertyChanged(nameof(SelectedCellProbabilities));
            OnPropertyChanged(nameof(HasSelectedCellCoverage));
            OnPropertyChanged(nameof(HasSelectedCellGap));
            OnPropertyChanged(nameof(InspectedLevel));
            OnPropertyChanged(nameof(InspectedMood));
            OnPropertyChanged(nameof(InspectedCellTotalWeightText));
            OnPropertyChanged(nameof(InspectedCellBreakdownText));
            OnPropertyChanged(nameof(InspectedCellTotalWeight));
            OnPropertyChanged(nameof(InspectedCellAnimationCount));
        }

        public void StepEntryWeight(string? entryName, int delta)
        {
            if (string.IsNullOrWhiteSpace(entryName)) return;
            var entry = Entries.FirstOrDefault(e => e.Name.Equals(entryName, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
            {
                int newWeight = Math.Clamp(entry.Weight + delta, 1, 100);
                if (entry.Weight != newWeight)
                {
                    PushUndoState();
                    entry.Weight = newWeight;
                    if (SelectedEntry == entry)
                    {
                        OnPropertyChanged(nameof(SelectedWeight));
                    }
                    RecalculateMatrix();
                }
            }
        }

        public void ExpandSelectedEntryMaxLevel()
        {
            if (SelectedEntry == null) return;
            if (SelectedEntry.MaxLevel < MaxAllowedLevel)
            {
                SetSelectedEntryBounds(SelectedEntry.MinLevel, SelectedEntry.MaxLevel + 1, SelectedEntry.MinButthurt, SelectedEntry.MaxButthurt);
            }
        }

        public void ShrinkSelectedEntryMaxLevel()
        {
            if (SelectedEntry == null) return;
            if (SelectedEntry.MaxLevel > SelectedEntry.MinLevel)
            {
                SetSelectedEntryBounds(SelectedEntry.MinLevel, SelectedEntry.MaxLevel - 1, SelectedEntry.MinButthurt, SelectedEntry.MaxButthurt);
            }
        }

        public void ExpandSelectedEntryMaxMood()
        {
            if (SelectedEntry == null) return;
            if (SelectedEntry.MaxButthurt < 14)
            {
                SetSelectedEntryBounds(SelectedEntry.MinLevel, SelectedEntry.MaxLevel, SelectedEntry.MinButthurt, SelectedEntry.MaxButthurt + 1);
            }
        }

        public void ShrinkSelectedEntryMaxMood()
        {
            if (SelectedEntry == null) return;
            if (SelectedEntry.MaxButthurt > SelectedEntry.MinButthurt)
            {
                SetSelectedEntryBounds(SelectedEntry.MinLevel, SelectedEntry.MaxLevel, SelectedEntry.MinButthurt, SelectedEntry.MaxButthurt - 1);
            }
        }

        public void ExpandSelectedEntryMinLevel()
        {
            if (SelectedEntry == null) return;
            if (SelectedEntry.MinLevel > 1)
            {
                SetSelectedEntryBounds(SelectedEntry.MinLevel - 1, SelectedEntry.MaxLevel, SelectedEntry.MinButthurt, SelectedEntry.MaxButthurt);
            }
        }

        public void ShrinkSelectedEntryMinLevel()
        {
            if (SelectedEntry == null) return;
            if (SelectedEntry.MinLevel < SelectedEntry.MaxLevel)
            {
                SetSelectedEntryBounds(SelectedEntry.MinLevel + 1, SelectedEntry.MaxLevel, SelectedEntry.MinButthurt, SelectedEntry.MaxButthurt);
            }
        }

        public void ExpandSelectedEntryMinMood()
        {
            if (SelectedEntry == null) return;
            if (SelectedEntry.MinButthurt > 0)
            {
                SetSelectedEntryBounds(SelectedEntry.MinLevel, SelectedEntry.MaxLevel, SelectedEntry.MinButthurt - 1, SelectedEntry.MaxButthurt);
            }
        }

        public void ShrinkSelectedEntryMinMood()
        {
            if (SelectedEntry == null) return;
            if (SelectedEntry.MinButthurt < SelectedEntry.MaxButthurt)
            {
                SetSelectedEntryBounds(SelectedEntry.MinLevel, SelectedEntry.MaxLevel, SelectedEntry.MinButthurt + 1, SelectedEntry.MaxButthurt);
            }
        }

        public void AdjustSelectedEntryBounds(int dMinLevel, int dMaxLevel, int dMinMood, int dMaxMood)
        {
            if (SelectedEntry == null) return;
            int newMinL = Math.Clamp(SelectedEntry.MinLevel + dMinLevel, 1, MaxAllowedLevel);
            int newMaxL = Math.Clamp(SelectedEntry.MaxLevel + dMaxLevel, 1, MaxAllowedLevel);
            int newMinM = Math.Clamp(SelectedEntry.MinButthurt + dMinMood, 0, 14);
            int newMaxM = Math.Clamp(SelectedEntry.MaxButthurt + dMaxMood, 0, 14);

            if (newMinL > newMaxL)
            {
                if (dMinLevel != 0) newMaxL = newMinL;
                else newMinL = newMaxL;
            }
            if (newMinM > newMaxM)
            {
                if (dMinMood != 0) newMaxM = newMinM;
                else newMinM = newMaxM;
            }

            SetSelectedEntryBounds(newMinL, newMaxL, newMinM, newMaxM);
        }

        public void PushUndoState()
        {
            const int MaxUndoDepth = 500;
            var snapshot = CreateCurrentSnapshot();
            _undoStack.Push(snapshot);
            if (_undoStack.Count > MaxUndoDepth)
            {
                var items = _undoStack.ToArray();
                _undoStack.Clear();
                for (int i = MaxUndoDepth - 1; i >= 0; i--)
                {
                    _undoStack.Push(items[i]);
                }
            }
            _redoStack.Clear();
            (UndoCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (RedoCommand as RelayCommand)?.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
        }

        private FlipperMatrixStateSnapshot CreateCurrentSnapshot()
        {
            int? selectedIdx = SelectedEntry != null ? Entries.IndexOf(SelectedEntry) : null;
            if (selectedIdx < 0) selectedIdx = null;

            var spritesCopy = new Dictionary<string, SpriteState>(_animationSprites, StringComparer.OrdinalIgnoreCase);
            var pathsCopy = new Dictionary<string, string>(_animationFilePaths, StringComparer.OrdinalIgnoreCase);
            var boundsCopy = Entries.Select(e => (e.SavedExtendedMinLevel, e.SavedExtendedMaxLevel, e.SavedStockMinLevel, e.SavedStockMaxLevel)).ToList();

            return new FlipperMatrixStateSnapshot(
                [.. Entries.Select(e => e.Entry.Clone())],
                _isStockMode,
                _packName,
                selectedIdx,
                spritesCopy,
                pathsCopy,
                boundsCopy,
                SelectedCellLevel,
                SelectedCellMood,
                HasSelectedRegion,
                SelectedRegionMinLevel,
                SelectedRegionMaxLevel,
                SelectedRegionMinMood,
                SelectedRegionMaxMood
            );
        }

        public void Undo()
        {
            if (_undoStack.Count == 0) return;
            var currentSnapshot = CreateCurrentSnapshot();
            _redoStack.Push(currentSnapshot);

            var state = _undoStack.Pop();
            ApplySnapshot(state);
            (UndoCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (RedoCommand as RelayCommand)?.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            SetStatus("↩️ Undo performed.");
        }

        public void Redo()
        {
            if (_redoStack.Count == 0) return;
            var currentSnapshot = CreateCurrentSnapshot();
            _undoStack.Push(currentSnapshot);

            var state = _redoStack.Pop();
            ApplySnapshot(state);
            (UndoCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (RedoCommand as RelayCommand)?.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            SetStatus("↪️ Redo performed.");
        }

        private void ApplySnapshot(FlipperMatrixStateSnapshot snapshot)
        {
            _isUpdating = true;
            try
            {
                _isStockMode = snapshot.IsStockMode;
                _packName = snapshot.PackName;
                OnPropertyChanged(nameof(IsStockMode));
                OnPropertyChanged(nameof(IsMomentumMode));
                OnPropertyChanged(nameof(MaxAllowedLevel));
                OnPropertyChanged(nameof(ModeBadgeText));
                OnPropertyChanged(nameof(PackName));

                // In-place VM updates to preserve thumbnail caches and reduce GC churn
                while (Entries.Count > snapshot.Entries.Count)
                {
                    var removed = Entries[^1];
                    removed.PropertyChanged -= OnEntryPropertyChanged;
                    Entries.RemoveAt(Entries.Count - 1);
                }

                for (int i = 0; i < snapshot.Entries.Count; i++)
                {
                    var snapEntry = snapshot.Entries[i];
                    (int? ExtMin, int? ExtMax, int? StockMin, int? StockMax) bounds = default;
                    if (snapshot.EntrySavedBounds != null && i < snapshot.EntrySavedBounds.Count)
                    {
                        bounds = snapshot.EntrySavedBounds[i];
                    }

                    if (i < Entries.Count)
                    {
                        var existing = Entries[i];
                        existing.Entry.Name = snapEntry.Name;
                        existing.Entry.MinLevel = snapEntry.MinLevel;
                        existing.Entry.MaxLevel = snapEntry.MaxLevel;
                        existing.Entry.MinButthurt = snapEntry.MinButthurt;
                        existing.Entry.MaxButthurt = snapEntry.MaxButthurt;
                        existing.Entry.Weight = snapEntry.Weight;
                        existing.SetSavedBounds(bounds.ExtMin, bounds.ExtMax, bounds.StockMin, bounds.StockMax);
                        existing.NotifyBoundsChanged();
                        existing.AcknowledgeNameChange();
                    }
                    else
                    {
                        var evm = new FlipperScheduleEntryViewModel(snapEntry.Clone());
                        evm.SetSavedBounds(bounds.ExtMin, bounds.ExtMax, bounds.StockMin, bounds.StockMax);
                        evm.PropertyChanged += OnEntryPropertyChanged;
                        Entries.Add(evm);
                    }
                }

                _animationSprites.Clear();
                if (snapshot.AnimationSprites != null)
                {
                    foreach (var (k, v) in snapshot.AnimationSprites)
                    {
                        if (v != null)
                        {
                            _animationSprites[k] = v;
                        }
                    }
                }

                _animationFilePaths.Clear();
                if (snapshot.AnimationFilePaths != null)
                {
                    foreach (var (k, v) in snapshot.AnimationFilePaths)
                    {
                        if (!string.IsNullOrEmpty(v))
                        {
                            _animationFilePaths[k] = v;
                        }
                    }
                }

                if (snapshot.SelectedEntryIndex.HasValue &&
                    snapshot.SelectedEntryIndex.Value >= 0 &&
                    snapshot.SelectedEntryIndex.Value < Entries.Count)
                {
                    SelectedEntry = Entries[snapshot.SelectedEntryIndex.Value];
                }
                else
                {
                    SelectedEntry = Entries.Count > 0 ? Entries[0] : null;
                }

                if (snapshot.HasSelectedRegion)
                {
                    SelectRegion(
                        snapshot.SelectedRegionMinLevel,
                        snapshot.SelectedRegionMaxLevel,
                        snapshot.SelectedRegionMinMood,
                        snapshot.SelectedRegionMaxMood
                    );
                }
                else
                {
                    ClearRegionSelection();
                }
            }
            finally
            {
                _isUpdating = false;
            }
            RecalculateMatrix();
            RefreshCurrentPreviewSprite();
            InspectCell(Math.Clamp(snapshot.SelectedCellLevel, 1, MaxAllowedLevel), Math.Clamp(snapshot.SelectedCellMood, 0, 14));
            OnDocumentModified();
        }

        public void SelectNextEntry()
        {
            var list = FilteredEntries.Count > 0 ? FilteredEntries : Entries;
            if (list.Count == 0) return;
            if (SelectedEntry == null || !list.Contains(SelectedEntry))
            {
                SelectedEntry = list[0];
                return;
            }
            int idx = list.IndexOf(SelectedEntry);
            int next = (idx + 1) % list.Count;
            SelectedEntry = list[next];
        }

        public void SelectPreviousEntry()
        {
            var list = FilteredEntries.Count > 0 ? FilteredEntries : Entries;
            if (list.Count == 0) return;
            if (SelectedEntry == null || !list.Contains(SelectedEntry))
            {
                SelectedEntry = list[0];
                return;
            }
            int idx = list.IndexOf(SelectedEntry);
            int prev = (idx - 1 + list.Count) % list.Count;
            SelectedEntry = list[prev];
        }

        public void AddEntry()
        {
            PushUndoState();

            // Find lowest available unique name
            int num = 1;
            string baseName;
            do
            {
                baseName = string.Create(CultureInfo.InvariantCulture, $"anim_{num++}");
            } while (Entries.Any(e => e.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase)));

            int minL = 1;
            int maxL = MaxAllowedLevel;
            int minB = 0;
            int maxB = 14;

            if (HasSelectedCellGap)
            {
                minL = Math.Clamp(SelectedCellLevel, 1, MaxAllowedLevel);
                maxL = minL;
                minB = Math.Clamp(SelectedCellMood, 0, 14);
                maxB = minB;
            }

            var newEntry = new FlipperManifestEntry
            {
                Name = baseName,
                MinLevel = minL,
                MaxLevel = maxL,
                MinButthurt = minB,
                MaxButthurt = maxB,
                Weight = 1,
            };

            var vm = new FlipperScheduleEntryViewModel(newEntry);
            Entries.Add(vm);
            SelectedEntry = vm;
            RecalculateMatrix();
            SetStatus($"➕ Added animation '{baseName}' to schedule.");
        }

        public void DeleteEntry(FlipperScheduleEntryViewModel? target = null)
        {
            var entryToDelete = target ?? SelectedEntry;
            if (entryToDelete == null || Entries.Count <= 1) return;

            PushUndoState();

            string cleanName = entryToDelete.Name.Trim().TrimStart('*').Trim();
            _animationSprites.Remove(cleanName);
            _animationFilePaths.Remove(cleanName);

            int idx = Entries.IndexOf(entryToDelete);
            Entries.Remove(entryToDelete);

            if (SelectedEntry == entryToDelete)
            {
                if (idx >= Entries.Count) idx = Entries.Count - 1;
                SelectedEntry = idx >= 0 ? Entries[idx] : null;
            }

            RecalculateMatrix();
            SetStatus($"🗑️ Removed animation '{entryToDelete.Name}' from asset pack.");
        }

        public void DuplicateEntry(FlipperScheduleEntryViewModel? target = null)
        {
            var entryToClone = target ?? SelectedEntry;
            if (entryToClone == null) return;

            PushUndoState();

            var copy = entryToClone.Entry.Clone();
            copy.Name += "_copy";

            string originalCleanName = entryToClone.Name.Trim().TrimStart('*').Trim();
            string copyCleanName = copy.Name.Trim().TrimStart('*').Trim();
            if (_animationSprites.TryGetValue(originalCleanName, out var originalSprite) && originalSprite != null)
            {
                var spriteCopy = originalSprite.Clone();
                _animationSprites[copyCleanName] = spriteCopy;
            }

            var vm = new FlipperScheduleEntryViewModel(copy);
            int idx = Entries.IndexOf(entryToClone);
            if (idx >= 0 && idx < Entries.Count)
            {
                Entries.Insert(idx + 1, vm);
            }
            else
            {
                Entries.Add(vm);
            }
            SelectedEntry = vm;
            RecalculateMatrix();
            SetStatus($"📋 Cloned animation '{entryToClone.Name}' -> '{copy.Name}'.");
        }

        private void OnEntryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is FlipperScheduleEntryViewModel entry)
            {
                if (e.PropertyName == nameof(FlipperScheduleEntryViewModel.IsSelected))
                {
                    if (entry.IsSelected && !SelectedEntries.Contains(entry))
                    {
                        SelectedEntries.Add(entry);
                    }
                    else if (!entry.IsSelected && SelectedEntries.Contains(entry))
                    {
                        SelectedEntries.Remove(entry);
                    }
                    OnPropertyChanged(nameof(SelectedEntries));
                    OnPropertyChanged(nameof(HasSelection));
                    OnPropertyChanged(nameof(HasMultiSelection));
                    OnPropertyChanged(nameof(SelectedEntriesCountText));
                    OnPropertyChanged(nameof(SelectedEntriesCountPillText));
                    OnPropertyChanged(nameof(IsAllSelected));
                    OnPropertyChanged(nameof(IsAnySelected));
                    OnPropertyChanged(nameof(MasterSelectionState));
                }
                else if (e.PropertyName == nameof(FlipperScheduleEntryViewModel.Name))
                {
                    MigrateSpriteKey(entry.PreviousName, entry.Name);
                    entry.AcknowledgeNameChange();
                    if (!_isUpdating)
                    {
                        RecalculateMatrix();
                        RefreshCurrentPreviewSprite();
                        OnDocumentModified();
                    }
                }
                else if (e.PropertyName is nameof(FlipperScheduleEntryViewModel.MinLevel)
                                        or nameof(FlipperScheduleEntryViewModel.MaxLevel)
                                        or nameof(FlipperScheduleEntryViewModel.MinButthurt)
                                        or nameof(FlipperScheduleEntryViewModel.MaxButthurt)
                                        or nameof(FlipperScheduleEntryViewModel.Weight))
                {
                    if (!_isUpdating)
                    {
                        RecalculateMatrix();
                        OnDocumentModified();
                    }
                }
                else if (e.PropertyName == nameof(FlipperScheduleEntryViewModel.IsPreviewPlaying))
                {
                    if (entry.IsPreviewPlaying)
                    {
                        _activeHoverPlayingEntry = entry;
                    }
                    else if (_activeHoverPlayingEntry == entry)
                    {
                        _activeHoverPlayingEntry = null;
                    }
                }
            }
        }

        private void MigrateSpriteKey(string? oldName, string? newName)
        {
            string cleanOld = oldName?.Trim().TrimStart('*').Trim() ?? string.Empty;
            string cleanNew = newName?.Trim().TrimStart('*').Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(cleanOld) && !cleanOld.Equals(cleanNew, StringComparison.OrdinalIgnoreCase))
            {
                if (_animationSprites.Remove(cleanOld, out var sprite) && !string.IsNullOrEmpty(cleanNew))
                {
                    _animationSprites[cleanNew] = sprite;
                }
                if (_animationFilePaths.Remove(cleanOld, out var filePath) && !string.IsNullOrEmpty(cleanNew) && !string.IsNullOrEmpty(filePath))
                {
                    _animationFilePaths[cleanNew] = filePath;
                }
                if (_tabService != null && !string.IsNullOrEmpty(cleanNew))
                {
                    _tabService.RenameTab(cleanOld, cleanNew);
                }
            }
        }

        private void SetStatus(string message)
        {
            StatusMessage = message;
            CellHoverInfoText = message;
        }

        public void SetAnimationSprite(string name, SpriteState sprite)
        {
            if (string.IsNullOrWhiteSpace(name) || sprite == null) return;
            string clean = name.Trim().TrimStart('*').Trim();
            _animationSprites[clean] = sprite;
            OnDocumentModified();
        }

        public void SetAnimationFilePath(string name, string? path)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            string clean = name.Trim().TrimStart('*').Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                _animationFilePaths.Remove(clean);
            }
            else
            {
                _animationFilePaths[clean] = path;
            }
            OnDocumentModified();
        }

        public string? GetAnimationFilePath(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string clean = name.Trim().TrimStart('*').Trim();
            return _animationFilePaths.TryGetValue(clean, out var p) ? p : null;
        }

        public void SyncFromWorkspace() => SyncFromWorkspace(silent: false);

        public void SyncFromWorkspace(bool silent)
        {
            var openSprites = _tabService?.GetAllOpenSpritesWithPaths();
            if ((openSprites == null || openSprites.Count == 0) && _tabService != null)
            {
                var fallback = _tabService.GetAllOpenSprites();
                if (fallback != null && fallback.Count > 0)
                {
                    openSprites = fallback.Select(f => (f.Title, f.Sprite, (string?)null)).ToList();
                }
            }

            if (openSprites == null || openSprites.Count == 0)
            {
                if (!silent) SetStatus("ℹ️ No open sprite tabs found in Hexprite workspace.");
                RefreshCurrentPreviewSprite(openSprites);
                return;
            }

            if (!silent) PushUndoState();

            int addedCount = 0;
            int updatedCount = 0;
            var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (Title, Sprite, FilePath) in openSprites)
            {
                string cleanTitle = Title?.Trim().TrimStart('*').Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(cleanTitle) || !seenTitles.Add(cleanTitle))
                {
                    continue;
                }

                if (Sprite != null)
                {
                    _animationSprites[cleanTitle] = Sprite;
                }
                if (!string.IsNullOrEmpty(FilePath))
                {
                    _animationFilePaths[cleanTitle] = FilePath;
                }

                var existing = Entries.FirstOrDefault(e => e.Name.Equals(cleanTitle, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    updatedCount++;
                }
                else if (!silent)
                {
                    var newEntry = new FlipperManifestEntry
                    {
                        Name = cleanTitle,
                        MinLevel = 1,
                        MaxLevel = MaxAllowedLevel,
                        MinButthurt = 0,
                        MaxButthurt = 14,
                        Weight = 1,
                    };
                    Entries.Add(new FlipperScheduleEntryViewModel(newEntry));
                    addedCount++;
                }
            }

            if (addedCount > 0 || updatedCount > 0)
            {
                RecalculateMatrix();
            }
            RefreshCurrentPreviewSprite(openSprites);

            if (!silent)
            {
                if (addedCount > 0)
                {
                    SetStatus(string.Create(CultureInfo.InvariantCulture, $"✅ Synced tabs: {addedCount} new animation(s) added, {updatedCount} existing updated ({Entries.Count} total)."));
                }
                else if (updatedCount > 0)
                {
                    SetStatus(string.Create(CultureInfo.InvariantCulture, $"✅ Synced tabs: {updatedCount} open tab(s) refreshed ({Entries.Count} total)."));
                }
                else
                {
                    SetStatus("ℹ️ All open workspace tabs are already in sync.");
                }
            }
        }

        public void ImportHexpAnimations(IEnumerable<string>? filePaths = null)
        {
            if (filePaths == null || !filePaths.Any())
            {
                string filter = "Hexprite Sprite (*.hexp)|*.hexp;*.json|All Files (*.*)|*.*";
                string[]? selectedFiles = _dialogService?.ShowOpenFilesDialog(filter, "Import Animations (.hexp)");
                if (selectedFiles == null || selectedFiles.Length == 0)
                {
                    string? single = _dialogService?.ShowOpenFileDialog(filter, "Import Animation (.hexp)");
                    if (string.IsNullOrEmpty(single)) return;
                    filePaths = [single];
                }
                else
                {
                    filePaths = selectedFiles;
                }
            }

            PushUndoState();
            int importedCount = 0;
            int updatedCount = 0;

            foreach (var path in filePaths)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;

                try
                {
                    string json = SafeFileIo.ReadAllTextWithRetry(path);
                    if (string.IsNullOrWhiteSpace(json)) continue;

                    var loaded = JsonSerializer.Deserialize<SpriteState>(json);
                    if (loaded == null) continue;

                    // Bounds and dimension sanity validation (1 to 512)
                    if (loaded.Width <= 0 || loaded.Height <= 0 || loaded.Width > 512 || loaded.Height > 512)
                        continue;

                    loaded.NormalizeLayerState();

                    string animName = Path.GetFileNameWithoutExtension(path);
                    animName = FlipperExportService.SanitizeAnimationName(animName);

                    _animationSprites[animName] = loaded;
                    _animationFilePaths[animName] = path;

                    var existing = Entries.FirstOrDefault(e => e.Name.Equals(animName, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        updatedCount++;
                    }
                    else
                    {
                        var newEntry = new FlipperManifestEntry
                        {
                            Name = animName,
                            MinLevel = 1,
                            MaxLevel = MaxAllowedLevel,
                            MinButthurt = 0,
                            MaxButthurt = 14,
                            Weight = 1,
                        };
                        var vm = new FlipperScheduleEntryViewModel(newEntry);
                        Entries.Add(vm);
                        SelectedEntry = vm;
                        importedCount++;
                    }
                }
                catch (Exception ex)
                {
                    HandledErrorReporter.Error(ex, "FlipperScheduleMatrixViewModel.ImportHexpAnimations", new { path });
                }
            }

            RecalculateMatrix();
            RefreshCurrentPreviewSprite();

            if (importedCount > 0 || updatedCount > 0)
            {
                SetStatus(string.Create(CultureInfo.InvariantCulture, $"📥 Imported {importedCount} new, {updatedCount} updated animation(s) from .hexp."));
            }
            else
            {
                SetStatus("⚠️ No valid .hexp animations could be imported.");
            }
        }

        public void ExportSelectedToHexp(string? targetPath = null)
        {
            if (SelectedEntry == null)
            {
                SetStatus("ℹ️ No animation selected to export.");
                return;
            }

            string cleanName = SelectedEntry.Name.Trim().TrimStart('*').Trim();
            if (!_animationSprites.TryGetValue(cleanName, out var sprite) || sprite == null)
            {
                if (_importedPack != null)
                {
                    var (Name, Sprite, ManifestEntry) = _importedPack.FirstOrDefault(p => p.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase));
                    sprite = Sprite;
                }
            }

            sprite ??= new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome, FrameRateFps = 10 };
            sprite.NormalizeLayerState();

            if (string.IsNullOrEmpty(targetPath))
            {
                string defaultExt = ".hexp";
                targetPath = _dialogService?.ShowSaveFileDialog("Hexprite Sprite (*.hexp)|*.hexp|All Files (*.*)|*.*", "Export Animation (.hexp)", defaultExt);
                if (string.IsNullOrEmpty(targetPath)) return;
            }

            targetPath = SafeFileIo.EnsureExtension(targetPath, ".hexp");

            try
            {
                string json = JsonSerializer.Serialize(sprite, IndentedJsonOptions);
                SafeFileIo.WriteAllTextAtomic(targetPath, json, maxRetries: 5, createBackup: true);
                _animationFilePaths[cleanName] = targetPath;
                SetStatus($"💾 Exported '{cleanName}' to {Path.GetFileName(targetPath)}.");
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Error(ex, "FlipperScheduleMatrixViewModel.ExportSelectedToHexp", new { targetPath });
                _dialogService?.ShowMessage($"Failed to export .hexp animation:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ExportAllToHexp(string? targetDirectory = null)
        {
            if (Entries.Count == 0)
            {
                SetStatus("ℹ️ No animations in pack to export.");
                return;
            }

            if (string.IsNullOrEmpty(targetDirectory))
            {
                targetDirectory = _dialogService?.ShowOpenFolderDialog("Select Export Directory for .hexp Animations");
                if (string.IsNullOrEmpty(targetDirectory)) return;
            }

            Directory.CreateDirectory(targetDirectory);
            int exportedCount = 0;
            var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in Entries)
            {
                string cleanName = entry.Name.Trim().TrimStart('*').Trim();
                if (!_animationSprites.TryGetValue(cleanName, out var sprite) || sprite == null)
                {
                    if (_importedPack != null)
                    {
                        var (Name, Sprite, ManifestEntry) = _importedPack.FirstOrDefault(p => p.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase));
                        sprite = Sprite;
                    }
                }
                sprite ??= new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome, FrameRateFps = 10 };
                sprite.NormalizeLayerState();

                string sanitized = FlipperExportService.SanitizeAnimationName(cleanName);
                string candidateName = $"{sanitized}.hexp";
                int disambiguation = 1;
                while (usedFileNames.Contains(candidateName))
                {
                    candidateName = $"{sanitized}_{disambiguation++}.hexp";
                }
                usedFileNames.Add(candidateName);

                string outPath = Path.Combine(targetDirectory, candidateName);

                try
                {
                    string json = JsonSerializer.Serialize(sprite, IndentedJsonOptions);
                    SafeFileIo.WriteAllTextAtomic(outPath, json, maxRetries: 3, createBackup: false);
                    _animationFilePaths[cleanName] = outPath;
                    exportedCount++;
                }
                catch (Exception ex)
                {
                    HandledErrorReporter.Error(ex, "FlipperScheduleMatrixViewModel.ExportAllToHexp", new { outPath });
                }
            }

            SetStatus(string.Create(CultureInfo.InvariantCulture, $"💾 Exported {exportedCount} of {Entries.Count} animation(s) as .hexp to {Path.GetFileName(targetDirectory)}."));
        }

        public void AutoBalance() => AutoBalanceWithStrategy(FlipperAutoBalanceStrategy.LinearLevels);

        public void AutoBalanceWithStrategy(FlipperAutoBalanceStrategy strategy)
        {
            PushUndoState();

            string? prevSelectedName = SelectedEntry?.Name;

            var raw = Entries.Select(e => e.Entry).ToList();
            var balanced = FlipperScheduleMatrix.AutoBalanceEntries(raw, strategy, MaxAllowedLevel);

            // In-place VM updates to preserve thumbnail caches and reduce GC churn
            while (Entries.Count > balanced.Count)
            {
                var removed = Entries[^1];
                removed.PropertyChanged -= OnEntryPropertyChanged;
                Entries.RemoveAt(Entries.Count - 1);
            }

            for (int i = 0; i < balanced.Count; i++)
            {
                var bEntry = balanced[i];
                if (i < Entries.Count)
                {
                    var existing = Entries[i];
                    existing.Entry.Name = bEntry.Name;
                    existing.Entry.MinLevel = bEntry.MinLevel;
                    existing.Entry.MaxLevel = bEntry.MaxLevel;
                    existing.Entry.MinButthurt = bEntry.MinButthurt;
                    existing.Entry.MaxButthurt = bEntry.MaxButthurt;
                    existing.Entry.Weight = bEntry.Weight;

                    if (_isStockMode)
                    {
                        var (eMin, eMax) = FlipperScheduleMatrix.ConvertStockToExtended(bEntry.MinLevel, bEntry.MaxLevel);
                        existing.SetSavedBounds(eMin, eMax, bEntry.MinLevel, bEntry.MaxLevel);
                    }
                    else
                    {
                        var (sMin, sMax) = FlipperScheduleMatrix.ConvertExtendedToStock(bEntry.MinLevel, bEntry.MaxLevel);
                        existing.SetSavedBounds(bEntry.MinLevel, bEntry.MaxLevel, sMin, sMax);
                    }

                    existing.NotifyBoundsChanged();
                    existing.AcknowledgeNameChange();
                }
                else
                {
                    var evm = new FlipperScheduleEntryViewModel(bEntry);
                    if (_isStockMode)
                    {
                        var (eMin, eMax) = FlipperScheduleMatrix.ConvertStockToExtended(bEntry.MinLevel, bEntry.MaxLevel);
                        evm.SetSavedBounds(eMin, eMax, bEntry.MinLevel, bEntry.MaxLevel);
                    }
                    else
                    {
                        var (sMin, sMax) = FlipperScheduleMatrix.ConvertExtendedToStock(bEntry.MinLevel, bEntry.MaxLevel);
                        evm.SetSavedBounds(bEntry.MinLevel, bEntry.MaxLevel, sMin, sMax);
                    }
                    evm.PropertyChanged += OnEntryPropertyChanged;
                    Entries.Add(evm);
                }
            }

            if (!string.IsNullOrWhiteSpace(prevSelectedName))
            {
                SelectedEntry = Entries.FirstOrDefault(e => e.Name.Equals(prevSelectedName, StringComparison.OrdinalIgnoreCase))
                                ?? (Entries.Count > 0 ? Entries[0] : null);
            }
            else if (Entries.Count > 0)
            {
                SelectedEntry = Entries[0];
            }

            RecalculateMatrix();

            string stratName = strategy switch
            {
                FlipperAutoBalanceStrategy.MoodTiers => "Mood Tiers (Happy/Neutral/Angry)",
                FlipperAutoBalanceStrategy.StageEvolution => "Stage Evolution (Baby/Teen/Adult)",
                FlipperAutoBalanceStrategy.FillGapsOnly => "Gap Filling",
                _ => "Linear Level Progression",
            };

            SetStatus($"⚡ Matrix auto-balanced using {stratName}!");
            OnDocumentModified();
        }

        public void ImportManifest()
        {
            string? filePath = _dialogService?.ShowOpenFileDialog(
                "Flipper Manifest (*.txt)|*.txt|All Files (*.*)|*.*",
                "Import Flipper Animation Manifest (manifest.txt)");

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

            try
            {
                List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> imported;
                try
                {
                    imported = _importService.ImportAssetPack(filePath);
                }
                catch (FileNotFoundException)
                {
                    // Fallback: file exists but no animation folders found alongside it.
                    // Parse manifest entries only (original behavior) so scheduling
                    // metadata is still usable.
                    string content = File.ReadAllText(filePath);
                    var manifest = FlipperManifest.Parse(content);
                    if (manifest.Entries.Count == 0)
                    {
                        _dialogService?.ShowMessage(
                            "The selected file does not contain valid animation entries.",
                            "Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    imported = [.. manifest.Entries.Select(e => (e.Name,
                        Sprite: CreatePlaceholderSprite(),
                        ManifestEntry: e
                    ))];
                }

                if (imported.Count == 0)
                {
                    _dialogService?.ShowMessage(
                        "The selected file does not contain valid animation entries.",
                        "Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                PushUndoState();

                bool shouldBeStock = imported.TrueForAll(i => i.ManifestEntry.MaxLevel <= 3);
                if (_isStockMode != shouldBeStock)
                {
                    _isStockMode = shouldBeStock;
                    OnPropertyChanged(nameof(IsStockMode));
                    OnPropertyChanged(nameof(IsMomentumMode));
                    OnPropertyChanged(nameof(MaxAllowedLevel));
                    OnPropertyChanged(nameof(ModeBadgeText));
                }

                Entries.Clear();
                foreach (var (name, sprite, entry) in imported)
                {
                    Entries.Add(new FlipperScheduleEntryViewModel(entry));

                    string cleanName = name.Trim().TrimStart('*').Trim();
                    if (sprite != null && !string.IsNullOrWhiteSpace(cleanName))
                    {
                        sprite.NormalizeLayerState();
                        _animationSprites[cleanName] = sprite;
                    }
                }

                if (Entries.Count > 0) SelectedEntry = Entries[0];
                RecalculateMatrix();
                RefreshCurrentPreviewSprite();

                int spriteCount = imported.Count(i =>
                    i.Sprite?.Frames.Count > 0 &&
                    i.Sprite.Frames.Exists(f => f.LayerPixels.Count > 0));
                string spriteSuffix = spriteCount > 0
                    ? string.Create(CultureInfo.InvariantCulture, $" ({spriteCount} with animation data)")
                    : " (manifest entries only, no animation folders found)";

                SetStatus(string.Create(CultureInfo.InvariantCulture, $"📂 Imported {imported.Count} animations from {Path.GetFileName(filePath)}{spriteSuffix}."));
            }
            catch (Exception ex)
            {
                _dialogService?.ShowMessage(
                    $"Failed to import manifest:\n{ex.Message}",
                    "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ImportAssetPackFolder(string? folderPath = null, bool merge = false)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                folderPath = _dialogService?.ShowOpenFolderDialog("Select Flipper Asset Pack / Animations Folder");
                if (string.IsNullOrWhiteSpace(folderPath)) return;
            }

            try
            {
                var imported = _importService.ImportAssetPack(folderPath);
                if (imported.Count == 0)
                {
                    _dialogService?.ShowMessage("No valid animations found in selected folder.", "Import Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (Entries.Count > 0 && !merge)
                {
                    merge = _dialogService?.ShowConfirmation(
                        $"Do you want to MERGE these {imported.Count} animation(s) into your existing pack?\n\nClick 'Yes' to Merge/Append.\nClick 'No' to Replace the current pack.",
                        "Import Folder") ?? false;
                }

                if (!merge)
                {
                    Entries.Clear();
                    _animationSprites.Clear();
                    _animationFilePaths.Clear();
                    PackName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    bool shouldBeStock = imported.TrueForAll(i => i.ManifestEntry.MaxLevel <= 3);
                    if (shouldBeStock != _isStockMode)
                    {
                        _isStockMode = shouldBeStock;
                        OnPropertyChanged(nameof(IsStockMode));
                        OnPropertyChanged(nameof(IsMomentumMode));
                        OnPropertyChanged(nameof(MaxAllowedLevel));
                        OnPropertyChanged(nameof(ModeBadgeText));
                    }
                }

                int addedCount = 0;
                int updatedCount = 0;

                foreach (var (name, sprite, entry) in imported)
                {
                    string cleanName = name.Trim().TrimStart('*').Trim();
                    var existing = Entries.FirstOrDefault(e => e.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        existing.Entry.MinLevel = entry.MinLevel;
                        existing.Entry.MaxLevel = entry.MaxLevel;
                        existing.Entry.MinButthurt = entry.MinButthurt;
                        existing.Entry.MaxButthurt = entry.MaxButthurt;
                        existing.Entry.Weight = entry.Weight;
                        updatedCount++;
                    }
                    else
                    {
                        Entries.Add(new FlipperScheduleEntryViewModel(entry));
                        addedCount++;
                    }

                    if (sprite != null && !string.IsNullOrWhiteSpace(cleanName))
                    {
                        sprite.NormalizeLayerState();
                        _animationSprites[cleanName] = sprite;
                    }
                }

                if (Entries.Count > 0 && SelectedEntry == null) SelectedEntry = Entries[0];
                RecalculateMatrix();
                RefreshCurrentPreviewSprite();
                SyncToSimulator();
                OnDocumentModified();

                SetStatus(string.Create(CultureInfo.InvariantCulture, $"📂 Imported folder: {addedCount} added, {updatedCount} updated from '{Path.GetFileName(folderPath)}'."));
            }
            catch (Exception ex)
            {
                _dialogService?.ShowMessage($"Failed to import folder:\n{ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ImportAssetPackZip(string? zipPath = null, bool merge = false)
        {
            if (string.IsNullOrWhiteSpace(zipPath))
            {
                string filter = "ZIP Archives (*.zip)|*.zip|All Files (*.*)|*.*";
                zipPath = _dialogService?.ShowOpenFileDialog(filter, "Import Flipper Asset Pack (.zip)");
                if (string.IsNullOrWhiteSpace(zipPath)) return;
            }

            try
            {
                var imported = _importService.ImportAssetPack(zipPath);
                if (imported.Count == 0)
                {
                    _dialogService?.ShowMessage("No valid animations found in ZIP archive.", "Import ZIP", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (Entries.Count > 0 && !merge)
                {
                    merge = _dialogService?.ShowConfirmation(
                        $"Do you want to MERGE these {imported.Count} animation(s) into your existing pack?\n\nClick 'Yes' to Merge/Append.\nClick 'No' to Replace the current pack.",
                        "Import ZIP") ?? false;
                }

                if (!merge)
                {
                    Entries.Clear();
                    _animationSprites.Clear();
                    _animationFilePaths.Clear();
                    PackName = Path.GetFileNameWithoutExtension(zipPath);
                    bool shouldBeStock = imported.TrueForAll(i => i.ManifestEntry.MaxLevel <= 3);
                    if (shouldBeStock != _isStockMode)
                    {
                        _isStockMode = shouldBeStock;
                        OnPropertyChanged(nameof(IsStockMode));
                        OnPropertyChanged(nameof(IsMomentumMode));
                        OnPropertyChanged(nameof(MaxAllowedLevel));
                        OnPropertyChanged(nameof(ModeBadgeText));
                    }
                }

                int addedCount = 0;
                int updatedCount = 0;

                foreach (var (name, sprite, entry) in imported)
                {
                    string cleanName = name.Trim().TrimStart('*').Trim();
                    var existing = Entries.FirstOrDefault(e => e.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        existing.Entry.MinLevel = entry.MinLevel;
                        existing.Entry.MaxLevel = entry.MaxLevel;
                        existing.Entry.MinButthurt = entry.MinButthurt;
                        existing.Entry.MaxButthurt = entry.MaxButthurt;
                        existing.Entry.Weight = entry.Weight;
                        updatedCount++;
                    }
                    else
                    {
                        Entries.Add(new FlipperScheduleEntryViewModel(entry));
                        addedCount++;
                    }

                    if (sprite != null && !string.IsNullOrWhiteSpace(cleanName))
                    {
                        sprite.NormalizeLayerState();
                        _animationSprites[cleanName] = sprite;
                    }
                }

                if (Entries.Count > 0 && SelectedEntry == null) SelectedEntry = Entries[0];
                RecalculateMatrix();
                RefreshCurrentPreviewSprite();
                SyncToSimulator();
                OnDocumentModified();

                SetStatus(string.Create(CultureInfo.InvariantCulture, $"🗜️ Imported ZIP: {addedCount} added, {updatedCount} updated from '{Path.GetFileName(zipPath)}'."));
            }
            catch (Exception ex)
            {
                _dialogService?.ShowMessage($"Failed to import ZIP archive:\n{ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static SpriteState CreatePlaceholderSprite()
        {
            var sprite = new SpriteState(128, 64)
            {
                ColorMode = ColorMode.Monochrome,
                FrameRateFps = 5,
            };
            return sprite;
        }

        public void ExportManifest()
        {
            var manifest = new FlipperManifest
            {
                Entries = [.. Entries.Select(e => e.Entry)],
            };

            var diags = manifest.Validate(isMomentum: !_isStockMode);
            var errors = diags.Where(d => d.Severity == FlipperValidationSeverity.Error).ToList();
            if (errors.Count > 0)
            {
                string errorMsg = string.Join('\n', errors.Select(e => $"• {e.Message}"));
                _dialogService?.ShowMessage(
                    $"Cannot export manifest with validation errors:\n\n{errorMsg}",
                    "Validation Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string? filePath = _dialogService?.ShowSaveFileDialog(
                "Flipper Manifest (*.txt)|*.txt|All Files (*.*)|*.*",
                "Export Flipper Animation Manifest (manifest.txt)",
                "txt");

            if (string.IsNullOrEmpty(filePath)) return;

            try
            {
                string content = manifest.Serialize();
                File.WriteAllText(filePath, content);

                SetStatus($"💾 Manifest saved to {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                _dialogService?.ShowMessage($"Failed to save manifest:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public IReadOnlyList<(SpriteState Sprite, FlipperManifestEntry ManifestEntry, FlipperExportSettings Settings)> BuildExportAnimationList(string targetFolder)
        {
            var openSprites = new Dictionary<string, SpriteState>(StringComparer.OrdinalIgnoreCase);
            if (_tabService != null)
            {
                foreach (var (Title, Sprite) in _tabService.GetAllOpenSprites())
                {
                    string cleanTitle = Title?.Trim().TrimStart('*').Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(cleanTitle) && Sprite != null)
                    {
                        openSprites.TryAdd(cleanTitle, Sprite);
                        _animationSprites[cleanTitle] = Sprite;
                    }
                }
            }

            var list = new List<(SpriteState Sprite, FlipperManifestEntry ManifestEntry, FlipperExportSettings Settings)>();

            foreach (var entryVm in Entries)
            {
                SpriteState sprite;
                string cleanEntryName = entryVm.Name.Trim().TrimStart('*').Trim();

                if (openSprites.TryGetValue(cleanEntryName, out var foundSprite) ||
                    openSprites.TryGetValue(entryVm.Name, out foundSprite))
                {
                    sprite = foundSprite;
                }
                else if (_animationSprites.TryGetValue(cleanEntryName, out var cachedSprite) ||
                         _animationSprites.TryGetValue(entryVm.Name, out cachedSprite))
                {
                    sprite = cachedSprite;
                }
                else if (_importedPack != null && _importedPack.FirstOrDefault(p => p.Name.Equals(entryVm.Name, StringComparison.OrdinalIgnoreCase)).Sprite is SpriteState packSprite)
                {
                    sprite = packSprite;
                    _animationSprites[cleanEntryName] = sprite;
                }
                else
                {
                    sprite = new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome, FrameRateFps = 10 };
                    _animationSprites[cleanEntryName] = sprite;
                }

                var animSettings = new FlipperExportSettings
                {
                    TargetFolder = targetFolder,
                    AnimationName = entryVm.Name,
                    FrameRate = sprite.FrameRateFps > 0 ? sprite.FrameRateFps : 5,
                    PassiveFrames = sprite.FlipperCycle?.PassiveFrameCount ?? 0,
                    ActiveFrames = (sprite.FlipperCycle?.ActiveFrameCount > 0) ? sprite.FlipperCycle.ActiveFrameCount : sprite.Frames.Count,
                    MinLevel = entryVm.MinLevel,
                    MaxLevel = entryVm.MaxLevel,
                    MinButthurt = entryVm.MinButthurt,
                    MaxButthurt = entryVm.MaxButthurt,
                    Weight = entryVm.Weight,
                    CreateManifestTxt = false,
                    TargetMode = _isStockMode ? FlipperExportTargetMode.StockDolphin : FlipperExportTargetMode.MomentumAssetPack,
                };

                list.Add((sprite, entryVm.Entry.Clone(), animSettings));
            }

            return list;
        }

        public void ExportAssetPackFolder(string? targetFolder = null)
        {
            if (string.IsNullOrEmpty(targetFolder))
            {
                targetFolder = _dialogService?.ShowOpenFolderDialog("Select Target Directory for Flipper Asset Pack");
            }

            if (string.IsNullOrEmpty(targetFolder)) return;

            try
            {
                var animations = BuildExportAnimationList(targetFolder);
                _exportService.ExportAssetPack(animations, targetFolder, isMomentum: !_isStockMode);
                SetStatus($"📦 Asset pack exported successfully to {Path.GetFileName(targetFolder)}.");
                _dialogService?.ShowMessage($"Asset pack exported successfully to:\n{targetFolder}", "Export Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SetStatus($"❌ Export failed: {ex.Message}");
                _dialogService?.ShowMessage($"Failed to export asset pack:\n\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task ExportAssetPackFolderAsync(string? targetFolder = null)
        {
            if (string.IsNullOrEmpty(targetFolder))
            {
                targetFolder = _dialogService?.ShowOpenFolderDialog("Select Target Directory for Flipper Asset Pack");
            }

            if (string.IsNullOrEmpty(targetFolder)) return;

            try
            {
                SetStatus("⏳ Exporting asset pack in background...");
                var animations = BuildExportAnimationList(targetFolder);
                await _exportService.ExportAssetPackAsync(animations, targetFolder, isMomentum: !_isStockMode);
                SetStatus($"📦 Asset pack exported successfully to {Path.GetFileName(targetFolder)}.");
                _dialogService?.ShowMessage($"Asset pack exported successfully to:\n{targetFolder}", "Export Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SetStatus($"❌ Export failed: {ex.Message}");
                _dialogService?.ShowMessage($"Failed to export asset pack:\n\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ExportAssetPackZip(string? targetZipPath = null)
        {
            if (string.IsNullOrEmpty(targetZipPath))
            {
                targetZipPath = _dialogService?.ShowSaveFileDialog(
                    "ZIP Archive (*.zip)|*.zip|All Files (*.*)|*.*",
                    "Export Flipper Asset Pack Archive (.zip)",
                    "zip");
            }

            if (string.IsNullOrEmpty(targetZipPath)) return;

            try
            {
                string baseDir = Path.GetDirectoryName(targetZipPath) ?? Path.GetTempPath();
                var animations = BuildExportAnimationList(baseDir);
                _exportService.ExportAssetPackZip(animations, targetZipPath, isMomentum: !_isStockMode);
                SetStatus($"🗜️ Asset pack archive saved to {Path.GetFileName(targetZipPath)}.");
                _dialogService?.ShowMessage($"Asset pack archive exported successfully to:\n{targetZipPath}", "Export Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SetStatus($"❌ Export failed: {ex.Message}");
                _dialogService?.ShowMessage($"Failed to export asset pack archive:\n\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task ExportAssetPackZipAsync(string? targetZipPath = null)
        {
            if (string.IsNullOrEmpty(targetZipPath))
            {
                targetZipPath = _dialogService?.ShowSaveFileDialog(
                    "ZIP Archive (*.zip)|*.zip|All Files (*.*)|*.*",
                    "Export Flipper Asset Pack Archive (.zip)",
                    "zip");
            }

            if (string.IsNullOrEmpty(targetZipPath)) return;

            try
            {
                SetStatus("⏳ Exporting and compressing asset pack archive...");
                string baseDir = Path.GetDirectoryName(targetZipPath) ?? Path.GetTempPath();
                var animations = BuildExportAnimationList(baseDir);
                await _exportService.ExportAssetPackZipAsync(animations, targetZipPath, isMomentum: !_isStockMode);
                SetStatus($"🗜️ Asset pack archive saved to {Path.GetFileName(targetZipPath)}.");
                _dialogService?.ShowMessage($"Asset pack archive exported successfully to:\n{targetZipPath}", "Export Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SetStatus($"❌ Export failed: {ex.Message}");
                _dialogService?.ShowMessage($"Failed to export asset pack archive:\n\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void CopyManifest()
        {
            var manifest = new FlipperManifest
            {
                Entries = [.. Entries.Select(e => e.Entry)],
            };
            string text = manifest.Serialize();

            if (_clipboardService != null)
            {
                _clipboardService.SetText(text);
            }
            else
            {
                try
                {
                    Clipboard.SetText(text);
                }
                catch
                {
                    // Headless fallback
                }
            }

            var diags = manifest.Validate(isMomentum: !_isStockMode);
            var errors = diags.Where(d => d.Severity == FlipperValidationSeverity.Error).ToList();
            if (errors.Count > 0)
            {
                SetStatus(string.Create(CultureInfo.InvariantCulture, $"⚠️ Manifest copied (with {errors.Count} validation error(s))."));
            }
            else
            {
                SetStatus("📋 Manifest contents copied to clipboard!");
            }
        }

        public List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> BuildCurrentAnimationList()
        {
            var openSprites = new Dictionary<string, SpriteState>(StringComparer.OrdinalIgnoreCase);
            if (_tabService != null)
            {
                foreach (var (Title, Sprite) in _tabService.GetAllOpenSprites())
                {
                    string cleanTitle = Title?.Trim().TrimStart('*').Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(cleanTitle) && Sprite != null)
                    {
                        openSprites.TryAdd(cleanTitle, Sprite);
                        _animationSprites[cleanTitle] = Sprite;
                    }
                }
            }

            var animList = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>();

            foreach (var entryVm in Entries)
            {
                SpriteState sprite;
                string cleanEntryName = entryVm.Name.Trim().TrimStart('*').Trim();

                if (openSprites.TryGetValue(cleanEntryName, out var foundSprite) ||
                    openSprites.TryGetValue(entryVm.Name, out foundSprite))
                {
                    sprite = foundSprite;
                }
                else if (_animationSprites.TryGetValue(cleanEntryName, out var cachedSprite) ||
                         _animationSprites.TryGetValue(entryVm.Name, out cachedSprite))
                {
                    sprite = cachedSprite;
                }
                else if (_importedPack != null && _importedPack.FirstOrDefault(p => p.Name.Equals(entryVm.Name, StringComparison.OrdinalIgnoreCase)).Sprite is SpriteState packSprite)
                {
                    sprite = packSprite;
                    _animationSprites[cleanEntryName] = sprite;
                }
                else
                {
                    sprite = new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome, FrameRateFps = 10 };
                    _animationSprites[cleanEntryName] = sprite;
                }

                animList.Add((entryVm.Name, sprite, entryVm.Entry.Clone()));
            }

            return animList;
        }

        public void InitializeSimulatorViewModelIfNeeded()
        {
            if (_simulatorViewModel == null)
            {
                InitializeSimulatorViewModel();
                OnPropertyChanged(nameof(SimulatorViewModel));
            }
        }

        private void InitializeSimulatorViewModel()
        {
            var animList = BuildCurrentAnimationList();
            _simulatorViewModel = new FlipperSimulatorViewModel(
                animList,
                PackName,
                _importService,
                _exportService,
                _windowManager,
                _tabService,
                _dialogService,
                _feedbackService);

            _simulatorViewModel.DocumentModified += (s, e) => OnDocumentModified();
            _simulatorViewModel.LocateEntryRequested += name =>
            {
                var entry = Entries.FirstOrDefault(e => e.Name == name);
                if (entry != null)
                {
                    SelectedEntry = entry;
                    InspectCell(entry.MinLevel, entry.MinButthurt);
                    CurrentViewMode = AssetPackViewMode.MatrixStudio;
                }
            };
        }

        public void SyncToSimulator()
        {
            if (_simulatorViewModel != null)
            {
                var animList = BuildCurrentAnimationList();
                _simulatorViewModel.SetAnimations(animList, PackName);
            }
        }

        public void OpenCellInSimulator(int level, int mood)
        {
            InspectCell(level, mood);
            InitializeSimulatorViewModelIfNeeded();
            SyncToSimulator();
            SimulatorViewModel.JumpToState(level, mood);
            CurrentViewMode = AssetPackViewMode.DeviceSimulator;
        }

        public void OpenSimulator()
        {
            InitializeSimulatorViewModelIfNeeded();
            SyncToSimulator();
            CurrentViewMode = AssetPackViewMode.DeviceSimulator;
        }

        public FlipperSimulatorSettings? GetCurrentSimulatorSettings()
        {
            InitializeSimulatorViewModelIfNeeded();
            return _simulatorViewModel?.GetCurrentSimulatorSettings();
        }

        public void ApplySimulatorSettings(FlipperSimulatorSettings settings)
        {
            if (settings == null) return;
            InitializeSimulatorViewModelIfNeeded();
            _simulatorViewModel?.ApplySimulatorSettings(settings);
        }

        // ── Canvas Integration & Preview Methods ────────────────────────────

        public void OpenSelectedInCanvas()
        {
            if (SelectedEntry == null) return;
            NavigateToEntryTab(SelectedEntry);
        }

        public void OpenCellAnimation()
        {
            var cell = _matrix.GetCell(SelectedCellLevel, SelectedCellMood);
            if (cell.HasCoverage && cell.MatchingEntries.Count > 0)
            {
                var match = (SelectedEntry != null && cell.MatchingEntries.Exists(m => m.Name.Equals(SelectedEntry.Name, StringComparison.OrdinalIgnoreCase)))
                    ? SelectedEntry.Name
                    : cell.MatchingEntries[0].Name;
                NavigateToEntryByName(match);
            }
            else
            {
                CreateTabForGap();
            }
        }

        public void AddActiveCanvasTab()
        {
            if (_tabService == null)
            {
                SetStatus("ℹ️ Tab service is not available.");
                return;
            }

            var active = _tabService.GetActiveSprite();
            string animName = active?.Title?.Trim().TrimStart('*').Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(animName))
            {
                var openSprites = _tabService.GetAllOpenSprites();
                if (openSprites.Count > 0)
                {
                    animName = openSprites[0].Title.Trim().TrimStart('*').Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(animName)) animName = "canvas_anim";

            if (active?.Sprite != null)
            {
                _animationSprites[animName] = active.Value.Sprite;
            }

            // Check if already present
            var existing = Entries.FirstOrDefault(e => e.Name.Equals(animName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                SelectedEntry = existing;
                SetStatus($"ℹ️ '{animName}' is already in schedule. Selected entry.");
                return;
            }

            PushUndoState();

            var newEntry = new FlipperManifestEntry
            {
                Name = animName,
                MinLevel = 1,
                MaxLevel = MaxAllowedLevel,
                MinButthurt = 0,
                MaxButthurt = 14,
                Weight = 1,
            };

            var vm = new FlipperScheduleEntryViewModel(newEntry);
            Entries.Add(vm);
            SelectedEntry = vm;
            RecalculateMatrix();
            SetStatus($"➕ Added active canvas tab '{animName}' to schedule.");
        }

        public void CreateTabForGap()
        {
            int lvl = Math.Clamp(SelectedCellLevel, 1, MaxAllowedLevel);
            int mood = Math.Clamp(SelectedCellMood, 0, 14);

            string animName = string.Create(CultureInfo.InvariantCulture, $"anim_L{lvl}_M{mood}");
            int suffix = 1;
            while (Entries.Any(e => e.Name.Equals(animName, StringComparison.OrdinalIgnoreCase)))
            {
                animName = string.Create(CultureInfo.InvariantCulture, $"anim_L{lvl}_M{mood}_{suffix++}");
            }

            PushUndoState();

            var newSprite = new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome, FrameRateFps = 10, IsAnimationEnabled = true };
            _animationSprites[animName] = newSprite;

            if (_tabService != null)
            {
                _tabService.OpenSpriteInTab(newSprite, animName);
            }

            var newEntry = new FlipperManifestEntry
            {
                Name = animName,
                MinLevel = lvl,
                MaxLevel = lvl,
                MinButthurt = mood,
                MaxButthurt = mood,
                Weight = 1,
            };

            var vm = new FlipperScheduleEntryViewModel(newEntry);
            Entries.Add(vm);
            SelectedEntry = vm;
            RecalculateMatrix();
            InspectCell(lvl, mood);

            SetStatus(string.Create(CultureInfo.InvariantCulture, $"✨ Created canvas tab '{animName}' to cover gap at Level {lvl}, Mood {mood}."));
        }

        public void RefreshCurrentPreviewSprite(IReadOnlyList<(string Title, SpriteState Sprite, string? FilePath)>? prefetchedSprites = null)
        {
            if (SelectedEntry == null)
            {
                _currentPreviewSprite = null;
                _previewFrameIndex = 0;
                UpdatePreviewTelemetry();
                RenderBlankPreview("Select animation");
                return;
            }

            string cleanSelectedName = SelectedEntry.Name.Trim().TrimStart('*').Trim();
            SpriteState? found = null;
            if (prefetchedSprites != null && prefetchedSprites.Count > 0)
            {
                var (Title, Sprite, FilePath) = prefetchedSprites.FirstOrDefault(s =>
                    s.Title.Equals(cleanSelectedName, StringComparison.OrdinalIgnoreCase) ||
                    s.Title.Equals(SelectedEntry.Name, StringComparison.OrdinalIgnoreCase));
                if (Sprite != null)
                {
                    found = Sprite;
                    _animationSprites[cleanSelectedName] = found;
                }
            }
            else if (_tabService != null)
            {
                var openSprites = _tabService.GetAllOpenSprites();
                if (openSprites != null)
                {
                    var (Title, Sprite) = openSprites.FirstOrDefault(s =>
                        s.Title.Equals(cleanSelectedName, StringComparison.OrdinalIgnoreCase) ||
                        s.Title.Equals(SelectedEntry.Name, StringComparison.OrdinalIgnoreCase));
                    if (Sprite != null)
                    {
                        found = Sprite;
                        _animationSprites[cleanSelectedName] = found;
                    }
                }
            }

            if (found == null)
            {
                if (_animationSprites.TryGetValue(cleanSelectedName, out var cachedSprite) ||
                    _animationSprites.TryGetValue(SelectedEntry.Name, out cachedSprite))
                {
                    found = cachedSprite;
                }
            }

            if (found == null && _importedPack != null)
            {
                var (Name, Sprite, ManifestEntry) = _importedPack.FirstOrDefault(p => p.Name.Equals(SelectedEntry.Name, StringComparison.OrdinalIgnoreCase));
                if (Sprite != null)
                {
                    found = Sprite;
                    _animationSprites[cleanSelectedName] = found;
                }
            }

            _currentPreviewSprite = found;
            _previewFrameIndex = 0;
            UpdatePreviewTelemetry();

            if (_currentPreviewSprite != null && _currentPreviewSprite.Frames.Count > 0)
            {
                UpdateTimerInterval();
                RenderPreviewFrame();
            }
            else
            {
                RenderBlankPreview(SelectedEntry.Name);
            }
        }

        public void SetPreviewSpriteForTest(SpriteState? sprite)
        {
            _currentPreviewSprite = sprite;
            _previewFrameIndex = 0;
            UpdatePreviewTelemetry();
            if (_currentPreviewSprite != null && _currentPreviewSprite.Frames.Count > 0)
            {
                RenderPreviewFrame();
            }
            else
            {
                RenderBlankPreview(SelectedEntry?.Name ?? "No Animation");
            }
        }

        private void UpdatePreviewFrameCountTextOnly()
        {
            int totalSteps = PreviewTotalFrames;
            if (_currentPreviewSprite != null && _currentPreviewSprite.Frames.Count > 0)
            {
                PreviewFrameCountText = string.Create(CultureInfo.InvariantCulture, $"Frame {_previewFrameIndex + 1} / {totalSteps}");
            }
            else if (SelectedEntry != null)
            {
                PreviewFrameCountText = "1 Frame (Placeholder)";
            }
            else
            {
                PreviewFrameCountText = "No Animation Selected";
            }
        }

        private void UpdatePreviewTelemetry()
        {
            UpdatePreviewFrameCountTextOnly();

            if (_currentPreviewSprite != null && _currentPreviewSprite.Frames.Count > 0)
            {
                PreviewDimensionsText = string.Create(CultureInfo.InvariantCulture, $"{_currentPreviewSprite.Width} × {_currentPreviewSprite.Height}");
            }
            else
            {
                PreviewDimensionsText = "128 × 64";
            }

            OnPropertyChanged(nameof(PreviewFps));
            OnPropertyChanged(nameof(PreviewFpsBadgeText));
            OnPropertyChanged(nameof(PreviewTotalFrames));
            OnPropertyChanged(nameof(PreviewMaxFrameIndex));
            OnPropertyChanged(nameof(PreviewHasMultipleFrames));
            OnPropertyChanged(nameof(PreviewFrameIndex));
            OnPropertyChanged(nameof(CurrentPreviewFrameIndex));
        }

        private void UpdateTimerInterval()
        {
            if (_previewTimer == null) return;
            int fps = PreviewFps;
            double effectiveFps = fps * _previewSpeedMultiplier;
            double intervalMs = 1000.0 / Math.Max(0.5, effectiveFps);
            if (double.IsNaN(intervalMs) || double.IsInfinity(intervalMs) || intervalMs <= 0)
            {
                intervalMs = 100.0;
            }
            _previewTimer.Interval = TimeSpan.FromMilliseconds(intervalMs);
        }

        public void TogglePreviewPlay()
        {
            IsPreviewPlaying = !IsPreviewPlaying;
            if (IsPreviewPlaying)
            {
                if (!_isPreviewLooping && _previewFrameIndex >= PreviewMaxFrameIndex)
                {
                    PreviewFrameIndex = 0;
                }
                UpdateTimerInterval();
                _previewTimer?.Start();
            }
            else
            {
                _previewTimer?.Stop();
            }
        }

        public void TogglePreviewLoop()
        {
            IsPreviewLooping = !IsPreviewLooping;
        }

        public void CyclePreviewSpeed()
        {
            if (_previewSpeedMultiplier < 0.9)
            {
                PreviewSpeedMultiplier = 1.0;
            }
            else if (_previewSpeedMultiplier < 1.4)
            {
                PreviewSpeedMultiplier = 1.5;
            }
            else if (_previewSpeedMultiplier < 1.9)
            {
                PreviewSpeedMultiplier = 2.0;
            }
            else
            {
                PreviewSpeedMultiplier = 0.5;
            }
        }

        public void SetPreviewSpeed(object? speedParam)
        {
            if (speedParam is double d)
            {
                PreviewSpeedMultiplier = d;
            }
            else if (speedParam is float f)
            {
                PreviewSpeedMultiplier = f;
            }
            else if (speedParam is int i)
            {
                PreviewSpeedMultiplier = i;
            }
            else if (speedParam is string s && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
            {
                PreviewSpeedMultiplier = parsed;
            }
        }

        public void PreviewNextFrame()
        {
            if (_currentPreviewSprite == null || _currentPreviewSprite.Frames.Count == 0) return;
            int totalSteps = PreviewTotalFrames;
            if (totalSteps <= 1) return;

            int nextIndex = _previewFrameIndex + 1;
            if (nextIndex >= totalSteps)
            {
                if (_isPreviewLooping)
                {
                    PreviewFrameIndex = 0;
                }
                else
                {
                    PreviewFrameIndex = PreviewMaxFrameIndex;
                    if (IsPreviewPlaying)
                    {
                        IsPreviewPlaying = false;
                        _previewTimer?.Stop();
                    }
                }
            }
            else
            {
                PreviewFrameIndex = nextIndex;
            }
        }

        public void PreviewPrevFrame()
        {
            if (_currentPreviewSprite == null || _currentPreviewSprite.Frames.Count == 0) return;
            int totalSteps = PreviewTotalFrames;
            if (totalSteps <= 1) return;

            int prevIndex = _previewFrameIndex - 1;
            if (prevIndex < 0)
            {
                if (_isPreviewLooping)
                {
                    PreviewFrameIndex = PreviewMaxFrameIndex;
                }
                else
                {
                    PreviewFrameIndex = 0;
                }
            }
            else
            {
                PreviewFrameIndex = prevIndex;
            }
        }

        public void RenderPreviewFrame()
        {
            if (_previewBitmap == null) return;
            var palette = FlipperThemeService.GetPalette(_selectedPaletteIndex);
            uint bgCol = palette.BgColor;
            uint fgCol = palette.FgColor;

            if (_currentPreviewSprite == null || _currentPreviewSprite.Frames.Count == 0)
            {
                RenderBlankPreview(SelectedEntry?.Name ?? "No Animation");
                return;
            }

            var sprite = _currentPreviewSprite;
            int totalSteps = (sprite.FlipperCycle?.FramesOrder != null && sprite.FlipperCycle.FramesOrder.Length > 0)
                ? sprite.FlipperCycle.FramesOrder.Length
                : sprite.Frames.Count;

            int stepIdx = Math.Clamp(_previewFrameIndex, 0, Math.Max(0, totalSteps - 1));
            int physicalIdx = stepIdx;
            if (sprite.FlipperCycle?.FramesOrder != null && sprite.FlipperCycle.FramesOrder.Length > 0)
            {
                physicalIdx = sprite.FlipperCycle.FramesOrder[stepIdx];
            }
            physicalIdx = Math.Clamp(physicalIdx, 0, sprite.Frames.Count - 1);

            int requiredPixels = sprite.Width * sprite.Height;
            if (_monoPreviewBuffer.Length < requiredPixels)
            {
                _monoPreviewBuffer = new bool[requiredPixels];
            }

            sprite.CompositeFramePixels(physicalIdx, _monoPreviewBuffer.AsSpan(0, requiredPixels), isExport: false);

            int sw = Math.Min(128, sprite.Width);
            int sh = Math.Min(64, sprite.Height);
            int offsetX = (128 - sw) / 2;
            int offsetY = (64 - sh) / 2;
            int spriteWidth = sprite.Width;

            Array.Fill(_previewPixelBuffer, bgCol);

            for (int y = 0; y < sh; y++)
            {
                int srcRow = y * spriteWidth;
                int destRow = (offsetY + y) * 128 + offsetX;
                for (int x = 0; x < sw; x++)
                {
                    if (_monoPreviewBuffer[srcRow + x])
                    {
                        _previewPixelBuffer[destRow + x] = fgCol;
                    }
                }
            }

            try
            {
                _previewBitmap.WritePixels(
                    new Int32Rect(0, 0, 128, 64),
                    _previewPixelBuffer,
                    128 * sizeof(uint),
                    0);
            }
            catch
            {
                // Headless test fallback
            }
        }

        public void RenderBlankPreview(string text)
        {
            var palette = FlipperThemeService.GetPalette(_selectedPaletteIndex);
            uint bgCol = palette.BgColor;
            uint fgCol = palette.FgColor;

            Array.Fill(_previewPixelBuffer, bgCol);
            Array.Clear(_blankTextPixelBuffer);

            if (!string.IsNullOrWhiteSpace(text))
            {
                FlipperFonts.DrawCenteredString(_blankTextPixelBuffer, 128, 64, -1, text, FlipperFontType.FontSecondary);
            }

            for (int i = 0; i < 128 * 64; i++)
            {
                if (_blankTextPixelBuffer[i])
                {
                    _previewPixelBuffer[i] = fgCol;
                }
            }

            try
            {
                _previewBitmap.WritePixels(
                    new Int32Rect(0, 0, 128, 64),
                    _previewPixelBuffer,
                    128 * sizeof(uint),
                    0);
            }
            catch
            {
                // Headless test fallback
            }
        }

        // ── Search & Filter Logic ──────────────────────────────────────────
        public void ResetFilters()
        {
            _searchFilterText = string.Empty;
            _selectedStageFilter = "All";
            _selectedMoodFilter = "All";
            OnPropertyChanged(nameof(SearchFilterText));
            OnPropertyChanged(nameof(SelectedStageFilter));
            OnPropertyChanged(nameof(SelectedMoodFilter));
            ApplyEntryFilter();
        }

        public void ApplyEntryFilter()
        {
            var previousSelection = _selectedEntry;

            string search = (_searchFilterText ?? string.Empty).Trim();
            string stage = (_selectedStageFilter ?? "All").Trim();
            string mood = (_selectedMoodFilter ?? "All").Trim();

            var filtered = Entries.Where(e =>
            {
                if (!string.IsNullOrEmpty(search))
                {
                    bool nameMatch = e.Name.Contains(search, StringComparison.OrdinalIgnoreCase);
                    bool detailsMatch = e.DetailsText.Contains(search, StringComparison.OrdinalIgnoreCase);
                    if (!nameMatch && !detailsMatch) return false;
                }

                if (!string.Equals(stage, "All", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(stage, "IssuesOnly", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(stage, "ErrorsOnly", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(stage, "Issues", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!e.HasValidationIssues) return false;
                    }
                    else if (string.Equals(stage, "Baby", StringComparison.OrdinalIgnoreCase))
                    {
                        int maxBaby = _isStockMode ? 1 : 9;
                        if (!(e.MinLevel <= maxBaby && e.MaxLevel >= 1)) return false;
                    }
                    else if (string.Equals(stage, "Teen", StringComparison.OrdinalIgnoreCase))
                    {
                        int minTeen = _isStockMode ? 2 : 10;
                        int maxTeen = _isStockMode ? 2 : 19;
                        if (!(e.MinLevel <= maxTeen && e.MaxLevel >= minTeen)) return false;
                    }
                    else if (string.Equals(stage, "Adult", StringComparison.OrdinalIgnoreCase))
                    {
                        int minAdult = _isStockMode ? 3 : 20;
                        int maxAdult = _isStockMode ? 3 : 30;
                        if (!(e.MinLevel <= maxAdult && e.MaxLevel >= minAdult)) return false;
                    }
                    else if (string.Equals(stage, "Spanning", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_isStockMode)
                        {
                            if (e.MinLevel >= e.MaxLevel) return false;
                        }
                        else
                        {
                            bool isPureBaby = e.MinLevel >= 1 && e.MaxLevel <= 9;
                            bool isPureTeen = e.MinLevel >= 10 && e.MaxLevel <= 19;
                            bool isPureAdult = e.MinLevel >= 20 && e.MaxLevel <= 30;
                            if (isPureBaby || isPureTeen || isPureAdult) return false;
                        }
                    }
                }

                if (!string.Equals(mood, "All", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(mood, "Happy", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!(e.MinButthurt <= 4 && e.MaxButthurt >= 0)) return false;
                    }
                    else if (string.Equals(mood, "Neutral", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!(e.MinButthurt <= 9 && e.MaxButthurt >= 5)) return false;
                    }
                    else if (string.Equals(mood, "Angry", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!(e.MinButthurt <= 14 && e.MaxButthurt >= 10)) return false;
                    }
                }

                return true;
            }).ToList();

            // Apply Sorting
            if (!string.Equals(_currentSortColumn, "Default", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(_currentSortColumn, "Name", StringComparison.OrdinalIgnoreCase))
                {
                    filtered = _isSortAscending
                        ? [.. filtered.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)]
                        : [.. filtered.OrderByDescending(e => e.Name, StringComparer.OrdinalIgnoreCase)];
                }
                else if (string.Equals(_currentSortColumn, "Level", StringComparison.OrdinalIgnoreCase))
                {
                    filtered = _isSortAscending
                        ? [.. filtered.OrderBy(e => e.MinLevel).ThenBy(e => e.MaxLevel)]
                        : [.. filtered.OrderByDescending(e => e.MaxLevel).ThenByDescending(e => e.MinLevel)];
                }
                else if (string.Equals(_currentSortColumn, "Mood", StringComparison.OrdinalIgnoreCase))
                {
                    filtered = _isSortAscending
                        ? [.. filtered.OrderBy(e => e.MinButthurt).ThenBy(e => e.MaxButthurt)]
                        : [.. filtered.OrderByDescending(e => e.MaxButthurt).ThenByDescending(e => e.MinButthurt)];
                }
                else if (string.Equals(_currentSortColumn, "Weight", StringComparison.OrdinalIgnoreCase))
                {
                    filtered = _isSortAscending
                        ? [.. filtered.OrderBy(e => e.Weight)]
                        : [.. filtered.OrderByDescending(e => e.Weight)];
                }
                else if (string.Equals(_currentSortColumn, "Coverage", StringComparison.OrdinalIgnoreCase))
                {
                    filtered = _isSortAscending
                        ? [.. filtered.OrderBy(e => e.CoverageCells)]
                        : [.. filtered.OrderByDescending(e => e.CoverageCells)];
                }
                else if (string.Equals(_currentSortColumn, "Stage", StringComparison.OrdinalIgnoreCase))
                {
                    filtered = _isSortAscending
                        ? [.. filtered.OrderBy(e => e.MinLevel).ThenBy(e => e.MaxLevel)]
                        : [.. filtered.OrderByDescending(e => e.MinLevel).ThenByDescending(e => e.MaxLevel)];
                }
                else if (string.Equals(_currentSortColumn, "Status", StringComparison.OrdinalIgnoreCase))
                {
                    filtered = _isSortAscending
                        ? [.. filtered.OrderByDescending(e => e.HasValidationIssues).ThenBy(e => e.Name)]
                        : [.. filtered.OrderBy(e => e.HasValidationIssues).ThenBy(e => e.Name)];
                }
            }
            else if (!string.Equals(SelectedSortOption, "Default", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(SelectedSortOption, "Name (A-Z)", StringComparison.OrdinalIgnoreCase))
                {
                    filtered = [.. filtered.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];
                }
                else if (string.Equals(SelectedSortOption, "Level (Low-High)", StringComparison.OrdinalIgnoreCase))
                {
                    filtered = [.. filtered.OrderBy(e => e.MinLevel).ThenBy(e => e.MaxLevel)];
                }
                else if (string.Equals(SelectedSortOption, "Mood (Low-High)", StringComparison.OrdinalIgnoreCase))
                {
                    filtered = [.. filtered.OrderBy(e => e.MinButthurt).ThenBy(e => e.MaxButthurt)];
                }
                else if (string.Equals(SelectedSortOption, "Weight (High-Low)", StringComparison.OrdinalIgnoreCase))
                {
                    filtered = [.. filtered.OrderByDescending(e => e.Weight)];
                }
                else if (string.Equals(SelectedSortOption, "Coverage (Cells)", StringComparison.OrdinalIgnoreCase))
                {
                    filtered = [.. filtered.OrderByDescending(e => e.CoverageCells)];
                }
                else if (string.Equals(SelectedSortOption, "Issues First", StringComparison.OrdinalIgnoreCase))
                {
                    filtered = [.. filtered.OrderByDescending(e => e.HasValidationIssues).ThenBy(e => e.Name)];
                }
            }

            // Apply Grouping
            if (!string.Equals(SelectedGroupOption, "None", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(SelectedGroupOption, "Stage (Baby/Teen/Adult)", StringComparison.OrdinalIgnoreCase))
                {
                    filtered = [.. filtered.OrderBy(e => e.MinLevel).ThenBy(e => e.Name)];
                }
                else if (string.Equals(SelectedGroupOption, "Mood (Happy/Neutral/Angry)", StringComparison.OrdinalIgnoreCase))
                {
                    filtered = [.. filtered.OrderBy(e => e.MinButthurt).ThenBy(e => e.Name)];
                }
                else if (string.Equals(SelectedGroupOption, "Health Status", StringComparison.OrdinalIgnoreCase))
                {
                    filtered = [.. filtered.OrderByDescending(e => e.HasErrors).ThenByDescending(e => e.HasWarnings).ThenBy(e => e.Name)];
                }
            }

            // Differential synchronization to prevent WPF ListBox collection resets
            var filteredSet = new HashSet<FlipperScheduleEntryViewModel>(filtered);
            for (int i = FilteredEntries.Count - 1; i >= 0; i--)
            {
                if (!filteredSet.Contains(FilteredEntries[i]))
                {
                    FilteredEntries.RemoveAt(i);
                }
            }

            for (int i = 0; i < filtered.Count; i++)
            {
                var item = filtered[i];
                int currentIndex = FilteredEntries.IndexOf(item);
                if (currentIndex == -1)
                {
                    if (i < FilteredEntries.Count)
                    {
                        FilteredEntries.Insert(i, item);
                    }
                    else
                    {
                        FilteredEntries.Add(item);
                    }
                }
                else if (currentIndex != i)
                {
                    FilteredEntries.Move(currentIndex, i);
                }
            }

            if (FilteredEntries.Count > 0)
            {
                if (previousSelection != null && FilteredEntries.Contains(previousSelection))
                {
                    SelectedEntry = previousSelection;
                }
                else if (previousSelection != null)
                {
                    SelectedEntry = FilteredEntries[0];
                }
            }
            else
            {
                SelectedEntry = null;
            }

            OnPropertyChanged(nameof(FilterCountText));
            OnPropertyChanged(nameof(FilteredEntriesCountText));
            OnPropertyChanged(nameof(HasActiveFilters));
            OnPropertyChanged(nameof(HasFilteredEntries));
            OnPropertyChanged(nameof(HasNoFilteredEntries));
            NotifySelectionProperties();
        }

        public void SortByColumn(string? column)
        {
            if (string.IsNullOrWhiteSpace(column)) return;

            if (string.Equals(_currentSortColumn, column, StringComparison.OrdinalIgnoreCase))
            {
                _isSortAscending = !_isSortAscending;
            }
            else
            {
                _currentSortColumn = column;
                _isSortAscending = true;
            }

            ApplyEntryFilter();
            NotifySortGlyphsChanged();
        }

        private void NotifySortGlyphsChanged()
        {
            OnPropertyChanged(nameof(CurrentSortColumn));
            OnPropertyChanged(nameof(IsSortAscending));
            OnPropertyChanged(nameof(NameSortGlyph));
            OnPropertyChanged(nameof(LevelSortGlyph));
            OnPropertyChanged(nameof(MoodSortGlyph));
            OnPropertyChanged(nameof(WeightSortGlyph));
            OnPropertyChanged(nameof(CoverageSortGlyph));
            OnPropertyChanged(nameof(StageSortGlyph));
            OnPropertyChanged(nameof(StatusSortGlyph));
        }

        public string GetColumnSortGlyph(string column)
        {
            if (!string.Equals(_currentSortColumn, column, StringComparison.OrdinalIgnoreCase))
                return string.Empty;
            return _isSortAscending ? " ▲" : " ▼";
        }

        // ── Bulk Operations Engine for High-Scale Asset Packs ───────────────
        public void ToggleSelectAll()
        {
            if (IsAllSelected)
            {
                ClearSelection();
            }
            else
            {
                SelectAll();
            }
        }

        private void NotifySelectionProperties()
        {
            OnPropertyChanged(nameof(SelectedEntries));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(HasMultiSelection));
            OnPropertyChanged(nameof(SelectedEntriesCountText));
            OnPropertyChanged(nameof(SelectedEntriesCountPillText));
            OnPropertyChanged(nameof(IsAllSelected));
            OnPropertyChanged(nameof(IsAnySelected));
            OnPropertyChanged(nameof(MasterSelectionState));
        }

        public void SelectAll()
        {
            SelectedEntries.Clear();
            foreach (var e in FilteredEntries)
            {
                e.IsSelected = true;
            }
            if (FilteredEntries.Count > 0)
            {
                SelectedEntry = FilteredEntries[0];
            }
            NotifySelectionProperties();
        }

        public void ClearSelection()
        {
            foreach (var e in Entries)
            {
                e.IsSelected = false;
            }
            SelectedEntries.Clear();
            NotifySelectionProperties();
        }

        public void SelectAllInStage(string stage)
        {
            ClearSelection();
            if (string.IsNullOrWhiteSpace(stage)) return;

            int minL = 1, maxL = MaxAllowedLevel;
            if (_isStockMode)
            {
                if (stage.Equals("Baby", StringComparison.OrdinalIgnoreCase)) { minL = 1; maxL = 1; }
                else if (stage.Equals("Teen", StringComparison.OrdinalIgnoreCase)) { minL = 2; maxL = 2; }
                else if (stage.Equals("Adult", StringComparison.OrdinalIgnoreCase)) { minL = 3; maxL = 3; }
            }
            else
            {
                if (stage.Equals("Baby", StringComparison.OrdinalIgnoreCase)) { minL = 1; maxL = 9; }
                else if (stage.Equals("Teen", StringComparison.OrdinalIgnoreCase)) { minL = 10; maxL = 19; }
                else if (stage.Equals("Adult", StringComparison.OrdinalIgnoreCase)) { minL = 20; maxL = 30; }
            }

            var matching = Entries.Where(e => e.MinLevel <= maxL && e.MaxLevel >= minL).ToList();
            foreach (var item in matching)
            {
                item.IsSelected = true;
            }
            if (matching.Count > 0)
            {
                SelectedEntry = matching[0];
            }
            NotifySelectionProperties();
        }

        public List<FlipperScheduleEntryViewModel> GetSelectedTargets()
        {
            var selected = Entries.Where(e => e.IsSelected).ToList();
            if (selected.Count > 0)
            {
                return selected;
            }
            return SelectedEntry != null ? [SelectedEntry] : [];
        }

        public void BulkShiftLevel(int delta)
        {
            var targets = GetSelectedTargets();
            if (targets.Count == 0 || delta == 0) return;

            PushUndoState();
            int maxLvl = MaxAllowedLevel;
            foreach (var entry in targets)
            {
                int minL = entry.MinLevel;
                int maxL = entry.MaxLevel;
                int span = maxL - minL;

                int newMinL = Math.Clamp(minL + delta, 1, maxLvl);
                int newMaxL = Math.Clamp(newMinL + span, 1, maxLvl);
                if (newMaxL == maxLvl && span <= maxLvl - 1)
                {
                    newMinL = Math.Max(1, newMaxL - span);
                }

                entry.MinLevel = newMinL;
                entry.MaxLevel = newMaxL;
                entry.NotifyBoundsChanged();
            }

            RecalculateMatrix();
            SetStatus(string.Create(CultureInfo.InvariantCulture, $"⚡ Shifted {targets.Count} animation level window(s) by {(delta > 0 ? "+" : "")}{delta}."));
        }

        public void BulkExpandLevel(int delta = 1)
        {
            var targets = GetSelectedTargets();
            if (targets.Count == 0 || delta <= 0) return;

            PushUndoState();
            int maxLvl = MaxAllowedLevel;
            foreach (var entry in targets)
            {
                int newMaxL = Math.Clamp(entry.MaxLevel + delta, entry.MinLevel, maxLvl);
                entry.MaxLevel = newMaxL;
                entry.NotifyBoundsChanged();
            }

            RecalculateMatrix();
            SetStatus(string.Create(CultureInfo.InvariantCulture, $"➕ Expanded level coverage for {targets.Count} animation(s) by +{delta}."));
        }

        public void BulkShrinkLevel(int delta = 1)
        {
            var targets = GetSelectedTargets();
            if (targets.Count == 0 || delta <= 0) return;

            PushUndoState();
            foreach (var entry in targets)
            {
                int newMaxL = Math.Max(entry.MinLevel, entry.MaxLevel - delta);
                entry.MaxLevel = newMaxL;
                entry.NotifyBoundsChanged();
            }

            RecalculateMatrix();
            SetStatus(string.Create(CultureInfo.InvariantCulture, $"➖ Shrunk level coverage for {targets.Count} animation(s) by -{delta}."));
        }

        public void BulkShiftMood(int delta)
        {
            var targets = GetSelectedTargets();
            if (targets.Count == 0 || delta == 0) return;

            PushUndoState();
            foreach (var entry in targets)
            {
                int minM = entry.MinButthurt;
                int maxM = entry.MaxButthurt;
                int span = maxM - minM;

                int newMinM = Math.Clamp(minM + delta, 0, 14);
                int newMaxM = Math.Clamp(newMinM + span, 0, 14);
                if (newMaxM == 14 && span <= 14)
                {
                    newMinM = Math.Max(0, newMaxM - span);
                }

                entry.MinButthurt = newMinM;
                entry.MaxButthurt = newMaxM;
                entry.NotifyBoundsChanged();
            }

            RecalculateMatrix();
            SetStatus(string.Create(CultureInfo.InvariantCulture, $"⚡ Shifted {targets.Count} animation mood window(s) by {(delta > 0 ? "+" : "")}{delta}."));
        }

        public void BulkExpandMood(int delta = 1)
        {
            var targets = GetSelectedTargets();
            if (targets.Count == 0 || delta <= 0) return;

            PushUndoState();
            foreach (var entry in targets)
            {
                int newMaxM = Math.Clamp(entry.MaxButthurt + delta, entry.MinButthurt, 14);
                entry.MaxButthurt = newMaxM;
                entry.NotifyBoundsChanged();
            }

            RecalculateMatrix();
            SetStatus(string.Create(CultureInfo.InvariantCulture, $"➕ Expanded mood coverage for {targets.Count} animation(s) by +{delta}."));
        }

        public void BulkShrinkMood(int delta = 1)
        {
            var targets = GetSelectedTargets();
            if (targets.Count == 0 || delta <= 0) return;

            PushUndoState();
            foreach (var entry in targets)
            {
                int newMaxM = Math.Max(entry.MinButthurt, entry.MaxButthurt - delta);
                entry.MaxButthurt = newMaxM;
                entry.NotifyBoundsChanged();
            }

            RecalculateMatrix();
            SetStatus(string.Create(CultureInfo.InvariantCulture, $"➖ Shrunk mood coverage for {targets.Count} animation(s) by -{delta}."));
        }

        public void BulkShiftWeight(int delta)
        {
            var targets = GetSelectedTargets();
            if (targets.Count == 0 || delta == 0) return;

            PushUndoState();
            foreach (var entry in targets)
            {
                int newW = Math.Clamp(entry.Weight + delta, 1, 100);
                entry.Weight = newW;
                entry.NotifyBoundsChanged();
            }

            RecalculateMatrix();
            SetStatus(string.Create(CultureInfo.InvariantCulture, $"⚡ Shifted {targets.Count} animation weight(s) by {(delta > 0 ? "+" : "")}{delta}."));
        }

        public void BulkSetWeight(int weight)
        {
            var targets = GetSelectedTargets();
            if (targets.Count == 0) return;

            int clampedWeight = Math.Clamp(weight, 1, 100);
            PushUndoState();
            foreach (var entry in targets)
            {
                entry.Weight = clampedWeight;
                entry.NotifyBoundsChanged();
            }

            RecalculateMatrix();
            SetStatus(string.Create(CultureInfo.InvariantCulture, $"⚡ Set weight for {targets.Count} animation(s) to {clampedWeight}."));
        }

        public void BulkSetStage(string stage)
        {
            var targets = GetSelectedTargets();
            if (targets.Count == 0 || string.IsNullOrWhiteSpace(stage)) return;

            PushUndoState();
            int minL = 1, maxL = MaxAllowedLevel;
            if (_isStockMode)
            {
                if (stage.Equals("Baby", StringComparison.OrdinalIgnoreCase)) { minL = 1; maxL = 1; }
                else if (stage.Equals("Teen", StringComparison.OrdinalIgnoreCase)) { minL = 2; maxL = 2; }
                else if (stage.Equals("Adult", StringComparison.OrdinalIgnoreCase)) { minL = 3; maxL = 3; }
            }
            else
            {
                if (stage.Equals("Baby", StringComparison.OrdinalIgnoreCase)) { minL = 1; maxL = 9; }
                else if (stage.Equals("Teen", StringComparison.OrdinalIgnoreCase)) { minL = 10; maxL = 19; }
                else if (stage.Equals("Adult", StringComparison.OrdinalIgnoreCase)) { minL = 20; maxL = 30; }
            }

            foreach (var entry in targets)
            {
                entry.MinLevel = minL;
                entry.MaxLevel = maxL;
                entry.NotifyBoundsChanged();
            }

            RecalculateMatrix();
            SetStatus(string.Create(CultureInfo.InvariantCulture, $"⚡ Assigned {targets.Count} animation(s) to {stage} stage (L{minL}-L{maxL})."));
        }

        public void BulkDelete()
        {
            var targets = GetSelectedTargets();
            if (targets.Count == 0) return;

            PushUndoState();
            int count = targets.Count;

            // Invariant: Asset pack must never have 0 entries
            if (targets.Count >= Entries.Count)
            {
                var entryToKeep = Entries[0];
                var toRemove = targets.Where(e => e != entryToKeep).ToList();
                foreach (var entry in toRemove)
                {
                    string cleanName = entry.Name.Trim().TrimStart('*').Trim();
                    _animationSprites.Remove(cleanName);
                    _animationFilePaths.Remove(cleanName);
                    Entries.Remove(entry);
                }
                SelectedEntries.Clear();
                SelectedEntry = entryToKeep;
                RecalculateMatrix();
                NotifySelectionProperties();
                SetStatus(string.Create(CultureInfo.InvariantCulture, $"🗑️ Deleted {toRemove.Count} animation(s). Retained '{entryToKeep.Name}' to maintain valid pack."));
                return;
            }

            foreach (var entry in targets)
            {
                string cleanName = entry.Name.Trim().TrimStart('*').Trim();
                _animationSprites.Remove(cleanName);
                _animationFilePaths.Remove(cleanName);
                Entries.Remove(entry);
            }
            SelectedEntries.Clear();
            SelectedEntry = Entries.Count > 0 ? Entries[0] : null;

            RecalculateMatrix();
            NotifySelectionProperties();
            SetStatus(string.Create(CultureInfo.InvariantCulture, $"🗑️ Deleted {count} animation(s)."));
        }

        public void BulkDuplicate()
        {
            var targets = GetSelectedTargets();
            if (targets.Count == 0) return;

            PushUndoState();
            var newEntries = new List<FlipperScheduleEntryViewModel>();
            foreach (var item in targets)
            {
                item.IsSelected = false;
                var cloneEntry = item.Entry.Clone();
                string baseName = cloneEntry.Name;
                int counter = 1;
                while (Entries.Any(e => e.Name.Equals(string.Create(CultureInfo.InvariantCulture, $"{baseName}_{counter}"), StringComparison.OrdinalIgnoreCase)) ||
                       newEntries.Exists(e => e.Name.Equals(string.Create(CultureInfo.InvariantCulture, $"{baseName}_{counter}"), StringComparison.OrdinalIgnoreCase)))
                {
                    counter++;
                }
                cloneEntry.Name = string.Create(CultureInfo.InvariantCulture, $"{baseName}_{counter}");

                string origClean = item.Name.Trim().TrimStart('*').Trim();
                string cloneClean = cloneEntry.Name.Trim().TrimStart('*').Trim();
                if (_animationSprites.TryGetValue(origClean, out var origSprite) && origSprite != null)
                {
                    _animationSprites[cloneClean] = origSprite.Clone();
                }
                if (_animationFilePaths.TryGetValue(origClean, out var origPath) && !string.IsNullOrEmpty(origPath))
                {
                    _animationFilePaths[cloneClean] = origPath;
                }

                var newVm = new FlipperScheduleEntryViewModel(cloneEntry);
                newEntries.Add(newVm);
                Entries.Add(newVm);
            }

            SelectedEntries.Clear();
            foreach (var ne in newEntries)
            {
                ne.IsSelected = true;
            }
            if (newEntries.Count > 0)
            {
                SelectedEntry = newEntries[0];
            }

            RecalculateMatrix();
            SetStatus(string.Create(CultureInfo.InvariantCulture, $"📋 Duplicated {newEntries.Count} animation(s)."));
        }

        public static BitmapSource RenderSpriteThumbnail(SpriteState? sprite, int frameIndex = 0)
        {
            var bmp = new WriteableBitmap(128, 64, 96, 96, PixelFormats.Bgra32, palette: null);
            uint[] rented = System.Buffers.ArrayPool<uint>.Shared.Rent(128 * 64);
            try
            {
                const uint bgCol = 0xFFFF8200; // Flipper Orange LCD
                const uint fgCol = 0xFF000000; // Black pixels
                rented.AsSpan(0, 128 * 64).Fill(bgCol);

                if (sprite != null && sprite.Frames.Count > 0)
                {
                    int actualFrame = Math.Clamp(frameIndex, 0, sprite.Frames.Count - 1);
                    int reqMono = sprite.Width * sprite.Height;
                    Span<bool> monoSpan = reqMono <= 128 * 64 ? stackalloc bool[reqMono] : new bool[reqMono];
                    sprite.CompositeFramePixels(actualFrame, monoSpan, isExport: false);

                    int srcW = sprite.Width;
                    int srcH = sprite.Height;
                    int offX = Math.Max(0, (128 - srcW) / 2);
                    int offY = Math.Max(0, (64 - srcH) / 2);
                    int drawW = Math.Min(srcW, 128);
                    int drawH = Math.Min(srcH, 64);

                    for (int y = 0; y < drawH; y++)
                    {
                        int srcRow = y * srcW;
                        int dstRow = (offY + y) * 128 + offX;
                        for (int x = 0; x < drawW; x++)
                        {
                            if (monoSpan[srcRow + x])
                            {
                                rented[dstRow + x] = fgCol;
                            }
                        }
                    }
                }

                bmp.WritePixels(new Int32Rect(0, 0, 128, 64), rented, 128 * 4, 0);
                bmp.Freeze();
                return bmp;
            }
            finally
            {
                System.Buffers.ArrayPool<uint>.Shared.Return(rented);
            }
        }

        public static List<ImageSource> RenderAllSpriteFrames(SpriteState? sprite, ImageSource? cachedFrame0 = null)
        {
            var list = new List<ImageSource>();
            if (sprite == null || sprite.Frames.Count == 0)
            {
                list.Add(cachedFrame0 ?? RenderSpriteThumbnail(sprite: null));
                return list;
            }

            if (sprite.FlipperCycle?.FramesOrder != null && sprite.FlipperCycle.FramesOrder.Length > 0)
            {
                var physicalCache = new Dictionary<int, ImageSource>();
                if (cachedFrame0 != null)
                {
                    physicalCache[0] = cachedFrame0;
                }
                foreach (int physIdx in sprite.FlipperCycle.FramesOrder)
                {
                    int clamped = Math.Clamp(physIdx, 0, sprite.Frames.Count - 1);
                    if (!physicalCache.TryGetValue(clamped, out var thumb))
                    {
                        thumb = RenderSpriteThumbnail(sprite, clamped);
                        physicalCache[clamped] = thumb;
                    }
                    list.Add(thumb);
                }
            }
            else
            {
                for (int i = 0; i < sprite.Frames.Count; i++)
                {
                    if (i == 0 && cachedFrame0 != null)
                    {
                        list.Add(cachedFrame0);
                    }
                    else
                    {
                        list.Add(RenderSpriteThumbnail(sprite, i));
                    }
                }
            }

            return list;
        }

        private void EnsurePreviewTimer()
        {
            if (_previewTimer == null)
            {
                try
                {
                    if (Application.Current?.Dispatcher != null)
                    {
                        _previewTimer = new DispatcherTimer();
                        _previewTimer.Tick += OnPreviewTimerTick;
                    }
                }
                catch
                {
                    // Headless test fallback
                }
            }
        }

        // ── Actionable Validation Diagnostics & Quick Fixes ─────────────────
        public void ExecuteQuickFix(string code, FlipperScheduleEntryViewModel? targetEntry)
        {
            if (string.IsNullOrWhiteSpace(code)) return;

            if (code.Equals("GAP", StringComparison.OrdinalIgnoreCase) || code.Equals("FZ_GAP", StringComparison.OrdinalIgnoreCase))
            {
                AutoBalanceWithStrategy(FlipperAutoBalanceStrategy.FillGapsOnly);
                return;
            }

            PushUndoState();

            switch (code)
            {
                case "FZ001":
                    if (targetEntry != null)
                    {
                        string oldName = targetEntry.Name;
                        string baseName = "anim";
                        int idx = 1;
                        while (Entries.Any(e => e.Name.Equals(string.Create(CultureInfo.InvariantCulture, $"{baseName}_{idx}"), StringComparison.OrdinalIgnoreCase)))
                        {
                            idx++;
                        }
                        string newName = string.Create(CultureInfo.InvariantCulture, $"{baseName}_{idx}");
                        targetEntry.Name = newName;
                        MigrateSpriteKey(oldName, newName);
                        SetStatus($"⚡ Set default name '{targetEntry.Name}'.");
                    }
                    break;

                case "FZ002":
                case "FZ003":
                    if (targetEntry != null)
                    {
                        int maxLvl = MaxAllowedLevel;
                        int minL = Math.Clamp(targetEntry.MinLevel, 1, maxLvl);
                        int maxL = Math.Clamp(targetEntry.MaxLevel, 1, maxLvl);
                        if (minL > maxL) (minL, maxL) = (maxL, minL);
                        targetEntry.MinLevel = minL;
                        targetEntry.MaxLevel = maxL;
                        targetEntry.NotifyBoundsChanged();
                        SetStatus(string.Create(CultureInfo.InvariantCulture, $"⚡ Clamped level range for '{targetEntry.Name}' to L{minL}-L{maxL}."));
                    }
                    break;

                case "FZ004":
                case "FZ005":
                    if (targetEntry != null)
                    {
                        int minM = Math.Clamp(targetEntry.MinButthurt, 0, 14);
                        int maxM = Math.Clamp(targetEntry.MaxButthurt, 0, 14);
                        if (minM > maxM) (minM, maxM) = (maxM, minM);
                        targetEntry.MinButthurt = minM;
                        targetEntry.MaxButthurt = maxM;
                        targetEntry.NotifyBoundsChanged();
                        SetStatus(string.Create(CultureInfo.InvariantCulture, $"⚡ Clamped mood range for '{targetEntry.Name}' to M{minM}-M{maxM}."));
                    }
                    break;

                case "FZ006":
                    if (targetEntry != null)
                    {
                        targetEntry.Weight = 1;
                        SetStatus($"⚡ Set weight for '{targetEntry.Name}' to 1.");
                    }
                    break;

                case "FZ007":
                    if (targetEntry != null)
                    {
                        targetEntry.Weight = 10;
                        SetStatus($"⚡ Normalized weight for '{targetEntry.Name}' to 10.");
                    }
                    break;

                case "FZ011":
                    if (targetEntry != null)
                    {
                        string origName = targetEntry.Name;
                        int suffix = 2;
                        string candidate = string.Create(CultureInfo.InvariantCulture, $"{origName}_{suffix}");
                        while (Entries.Any(e => e != targetEntry && e.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                        {
                            suffix++;
                            candidate = string.Create(CultureInfo.InvariantCulture, $"{origName}_{suffix}");
                        }
                        targetEntry.Name = candidate;
                        MigrateSpriteKey(origName, candidate);
                        SetStatus($"⚡ Renamed duplicate to '{candidate}'.");
                    }
                    break;

                case "MISSING_SPRITE":
                case "FZ_MISSING_SPRITE":
                    if (targetEntry != null)
                    {
                        string cName = targetEntry.Name.Trim().TrimStart('*').Trim();
                        if (string.IsNullOrEmpty(cName)) cName = "Animation";
                        if (!_animationSprites.ContainsKey(cName))
                        {
                            var newSprite = new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome, FrameRateFps = 10 };
                            _animationSprites[cName] = newSprite;
                        }
                        if (_tabService != null)
                        {
                            NavigateToEntryTab(targetEntry);
                        }
                        SetStatus($"⚡ Created canvas sprite tab for '{targetEntry.Name}'.");
                    }
                    break;

                case "FZM001":
                    if (targetEntry != null)
                    {
                        string cName = targetEntry.Name.Trim().TrimStart('*').Trim();
                        if (_animationSprites.TryGetValue(cName, out var sp) && sp != null)
                        {
                            var normalized = new SpriteState(128, 64)
                            {
                                ColorMode = sp.ColorMode,
                                FrameRateFps = sp.FrameRateFps,
                                IsAnimationEnabled = sp.IsAnimationEnabled,
                                FlipperCycle = sp.FlipperCycle?.Clone(),
                            };
                            normalized.Frames.Clear();

                            int srcW = sp.Width;
                            int srcH = sp.Height;
                            int offX = Math.Max(0, (128 - srcW) / 2);
                            int offY = Math.Max(0, (64 - srcH) / 2);
                            int copyW = Math.Min(srcW, 128);
                            int copyH = Math.Min(srcH, 64);
                            bool[] monoBuffer = new bool[srcW * srcH];

                            for (int f = 0; f < sp.Frames.Count; f++)
                            {
                                var srcFrame = sp.Frames[f];
                                var newFrame = new FrameState
                                {
                                    Name = srcFrame.Name,
                                    DelayMultiplier = srcFrame.DelayMultiplier,
                                };

                                sp.CompositeFramePixels(f, monoBuffer.AsSpan(), isExport: false);

                                var newPixels = new bool[128 * 64];
                                for (int y = 0; y < copyH; y++)
                                {
                                    int srcRow = y * srcW;
                                    int destRow = (offY + y) * 128 + offX;
                                    for (int x = 0; x < copyW; x++)
                                    {
                                        if (monoBuffer[srcRow + x])
                                        {
                                            newPixels[destRow + x] = true;
                                        }
                                    }
                                }

                                newFrame.LayerPixels = [new MonochromePixelBuffer(newPixels)];
                                normalized.Frames.Add(newFrame);
                            }

                            if (normalized.Frames.Count == 0)
                            {
                                normalized.Frames.Add(new FrameState { Name = "Frame 1" });
                            }

                            normalized.NormalizeLayerState();
                            _animationSprites[cName] = normalized;
                            targetEntry.InvalidateThumbnail();
                            SetStatus($"⚡ Normalized sprite dimensions for '{targetEntry.Name}' to 128x64 (preserved existing artwork).");
                        }
                    }
                    break;

                case "FZM004":
                    if (targetEntry != null)
                    {
                        string cName = targetEntry.Name.Trim().TrimStart('*').Trim();
                        if (_animationSprites.TryGetValue(cName, out var sp) && sp?.FlipperCycle != null && sp.Frames.Count > 0)
                        {
                            int maxValidIdx = Math.Max(0, sp.Frames.Count - 1);
                            var fixedOrder = sp.FlipperCycle.FramesOrder.Select(idx => Math.Clamp(idx, 0, maxValidIdx)).ToArray();
                            sp.FlipperCycle.FramesOrder = fixedOrder;
                            SetStatus($"⚡ Normalized frame cycle indices for '{targetEntry.Name}'.");
                        }
                    }
                    break;
            }

            RecalculateMatrix();
        }

        public void FixAllDiagnostics()
        {
            if (ActionableDiagnostics.Count == 0) return;

            PushUndoState();

            // 1. Fix names (empty and duplicate)
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int animIdx = 1;
            foreach (var entry in Entries)
            {
                string oldName = entry.Name;
                if (string.IsNullOrWhiteSpace(entry.Name))
                {
                    while (seenNames.Contains(string.Create(CultureInfo.InvariantCulture, $"anim_{animIdx}")) || Entries.Any(e => e != entry && e.Name.Equals(string.Create(CultureInfo.InvariantCulture, $"anim_{animIdx}"), StringComparison.OrdinalIgnoreCase)))
                    {
                        animIdx++;
                    }
                    string newName = string.Create(CultureInfo.InvariantCulture, $"anim_{animIdx++}");
                    entry.Name = newName;
                    MigrateSpriteKey(oldName, newName);
                    seenNames.Add(entry.Name);
                }
                else if (!seenNames.Add(entry.Name))
                {
                    int suffix = 2;
                    string newName = string.Create(CultureInfo.InvariantCulture, $"{entry.Name}_{suffix}");
                    while (seenNames.Contains(newName) || Entries.Any(e => e != entry && e.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                    {
                        suffix++;
                        newName = string.Create(CultureInfo.InvariantCulture, $"{entry.Name}_{suffix}");
                    }
                    entry.Name = newName;
                    MigrateSpriteKey(oldName, newName);
                    seenNames.Add(newName);
                }
            }

            // 2. Fix level, mood bounds and weights
            int maxLvl = MaxAllowedLevel;
            foreach (var entry in Entries)
            {
                int minL = Math.Clamp(entry.MinLevel, 1, maxLvl);
                int maxL = Math.Clamp(entry.MaxLevel, 1, maxLvl);
                if (minL > maxL) (minL, maxL) = (maxL, minL);
                entry.MinLevel = minL;
                entry.MaxLevel = maxL;

                int minM = Math.Clamp(entry.MinButthurt, 0, 14);
                int maxM = Math.Clamp(entry.MaxButthurt, 0, 14);
                if (minM > maxM) (minM, maxM) = (maxM, minM);
                entry.MinButthurt = minM;
                entry.MaxButthurt = maxM;

                if (entry.Weight <= 0) entry.Weight = 1;
                else if (entry.Weight > 100) entry.Weight = 10;

                entry.NotifyBoundsChanged();
            }

            // 3. Fix missing sprites and dimensions
            foreach (var entry in Entries)
            {
                string cName = entry.Name.Trim().TrimStart('*').Trim();
                if (!string.IsNullOrEmpty(cName))
                {
                    if (!_animationSprites.TryGetValue(cName, out var sp) || sp == null)
                    {
                        if (_importedPack == null || !_importedPack.Any(p => p.Name.Equals(cName, StringComparison.OrdinalIgnoreCase)))
                        {
                            _animationSprites[cName] = new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome, FrameRateFps = 10 };
                        }
                    }
                    else
                    {
                        if (sp.Width > 128 || sp.Height > 64 || sp.Width <= 0 || sp.Height <= 0)
                        {
                            var normalized = new SpriteState(128, 64) { ColorMode = sp.ColorMode, FrameRateFps = sp.FrameRateFps };
                            normalized.Frames.Clear();
                            foreach (var f in sp.Frames)
                            {
                                normalized.Frames.Add(new FrameState { Name = f.Name });
                            }
                            if (normalized.Frames.Count == 0) normalized.Frames.Add(new FrameState { Name = "Frame 1" });
                            _animationSprites[cName] = normalized;
                        }

                        if (sp.FlipperCycle?.FramesOrder != null && sp.FlipperCycle.FramesOrder.Length > 0 && sp.Frames.Count > 0)
                        {
                            int maxValidIdx = Math.Max(0, sp.Frames.Count - 1);
                            var fixedOrder = sp.FlipperCycle.FramesOrder.Select(idx => Math.Clamp(idx, 0, maxValidIdx)).ToArray();
                            sp.FlipperCycle.FramesOrder = fixedOrder;
                        }
                    }
                }
            }

            // 4. Fix gaps if present
            var manifestEntries = Entries.Select(e => e.Entry).ToList();
            var tempMatrix = new FlipperScheduleMatrix(manifestEntries, maxLvl);
            if (tempMatrix.UncoveredStatesCount > 0 && Entries.Count > 0)
            {
                var balanced = FlipperScheduleMatrix.AutoBalanceEntries(manifestEntries, FlipperAutoBalanceStrategy.LinearLevels, maxLvl);
                for (int i = 0; i < Math.Min(Entries.Count, balanced.Count); i++)
                {
                    Entries[i].Entry.MinLevel = balanced[i].MinLevel;
                    Entries[i].Entry.MaxLevel = balanced[i].MaxLevel;
                    Entries[i].Entry.MinButthurt = balanced[i].MinButthurt;
                    Entries[i].Entry.MaxButthurt = balanced[i].MaxButthurt;
                    Entries[i].NotifyBoundsChanged();
                }
            }

            RecalculateMatrix();
            SetStatus("⚡ Applied quick fixes to resolve all diagnostic issues.");
        }

        private void OnEntriesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (FlipperScheduleEntryViewModel item in e.OldItems)
                {
                    item.PropertyChanged -= OnEntryPropertyChanged;
                    SelectedEntries.Remove(item);
                }
            }
            if (e.NewItems != null)
            {
                foreach (FlipperScheduleEntryViewModel item in e.NewItems)
                {
                    item.PropertyChanged += OnEntryPropertyChanged;
                    if (item.IsSelected && !SelectedEntries.Contains(item))
                    {
                        SelectedEntries.Add(item);
                    }
                }
            }
            OnPropertyChanged(nameof(SelectedEntries));
            OnPropertyChanged(nameof(HasMultiSelection));
            OnPropertyChanged(nameof(SelectedEntriesCountText));
            OnPropertyChanged(nameof(EntryCountBadgeText));

            if (!_isUpdating)
            {
                OnDocumentModified();
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (_previewTimer != null)
            {
                _previewTimer.Stop();
                _previewTimer.Tick -= OnPreviewTimerTick;
                _previewTimer = null;
            }

            Entries.CollectionChanged -= OnEntriesCollectionChanged;

            foreach (var entry in Entries)
            {
                entry.PropertyChanged -= OnEntryPropertyChanged;
            }
            Entries.Clear();
            FilteredEntries.Clear();
            SelectedEntries.Clear();

            MatrixRedrawRequested = null;
            DocumentModified = null;
            _undoStack.Clear();
            _redoStack.Clear();
            _animationSprites.Clear();
            _animationFilePaths.Clear();
            _currentPreviewSprite = null;

            if (_simulatorViewModel != null)
            {
                _simulatorViewModel.Dispose();
                _simulatorViewModel = null;
            }

            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Represents an actionable validation diagnostic item in the Flipper Asset Pack Inspector.
    /// Supports one-click resolution via QuickFixCommand.
    /// </summary>
    public class FlipperDiagnosticItemViewModel : ObservableObject
    {
        private readonly Action<string, FlipperScheduleEntryViewModel?>? _quickFixAction;

        public FlipperValidationDiagnostic Diagnostic { get; }
        public FlipperScheduleEntryViewModel? TargetEntry { get; }

        public string Code => Diagnostic.Code;
        public string Message => Diagnostic.Message;
        public FlipperValidationSeverity Severity => Diagnostic.Severity;

        public string SeverityIcon => Severity switch
        {
            FlipperValidationSeverity.Error => "❌",
            FlipperValidationSeverity.Warning => "⚠️",
            _ => "ℹ️",
        };

        public string SeverityBadgeText => Severity.ToString().ToUpperInvariant();

        public string TargetName => TargetEntry?.Name ?? (string.IsNullOrEmpty(Diagnostic.PropertyName) ? "Manifest" : Diagnostic.PropertyName);

        public bool HasQuickFix => Diagnostic.Code switch
        {
            "FZ001" => true,
            "FZ002" or "FZ003" => true,
            "FZ004" or "FZ005" => true,
            "FZ006" => true,
            "FZ007" => true,
            "FZ011" => true,
            "GAP" or "FZ_GAP" => true,
            "MISSING_SPRITE" or "FZ_MISSING_SPRITE" => true,
            "FZM001" => true,
            "FZM004" => true,
            _ => false,
        };

        public string QuickFixLabel => Diagnostic.Code switch
        {
            "FZ001" => "⚡ Set Default Name",
            "FZ002" or "FZ003" => "⚡ Clamp Levels",
            "FZ004" or "FZ005" => "⚡ Clamp Moods",
            "FZ006" => "⚡ Set Weight = 1",
            "FZ007" => "⚡ Normalize Weight",
            "FZ011" => "⚡ Rename Unique",
            "GAP" or "FZ_GAP" => "⚡ Auto-Balance Gaps",
            "MISSING_SPRITE" or "FZ_MISSING_SPRITE" => "⚡ Create Sprite Tab",
            "FZM001" => "⚡ Fix 128x64 Bounds",
            "FZM004" => "⚡ Fix Frames Order",
            _ => "⚡ Quick Fix",
        };

        public IRelayCommand QuickFixCommand { get; }

        public FlipperDiagnosticItemViewModel(
            FlipperValidationDiagnostic diagnostic,
            FlipperScheduleEntryViewModel? targetEntry,
            Action<string, FlipperScheduleEntryViewModel?>? quickFixAction)
        {
            Diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
            TargetEntry = targetEntry;
            _quickFixAction = quickFixAction;
            QuickFixCommand = new RelayCommand(ExecuteQuickFix, () => HasQuickFix);
        }

        public void ExecuteQuickFix()
        {
            _quickFixAction?.Invoke(Diagnostic.Code, TargetEntry);
        }
    }

    /// <summary>
    /// Represents a complete snapshot of the Flipper schedule matrix state for undo/redo.
    /// </summary>
    public record FlipperMatrixStateSnapshot(
        List<FlipperManifestEntry> Entries,
        bool IsStockMode,
        string PackName,
        int? SelectedEntryIndex,
        Dictionary<string, SpriteState>? AnimationSprites = null,
        Dictionary<string, string>? AnimationFilePaths = null,
        List<(int? ExtMin, int? ExtMax, int? StockMin, int? StockMax)>? EntrySavedBounds = null,
        int SelectedCellLevel = 1,
        int SelectedCellMood = 0,
        bool HasSelectedRegion = false,
        int SelectedRegionMinLevel = 1,
        int SelectedRegionMaxLevel = 1,
        int SelectedRegionMinMood = 0,
        int SelectedRegionMaxMood = 0
    );
}

