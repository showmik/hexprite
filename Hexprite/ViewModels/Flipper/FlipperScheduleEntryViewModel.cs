using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Hexprite.Core;

namespace Hexprite.ViewModels.Flipper
{
    public class FlipperScheduleEntryViewModel : ObservableObject
    {
        private string _previousName = string.Empty;

        public FlipperManifestEntry Entry { get; }

        public string PreviousName => _previousName;

        public void AcknowledgeNameChange() => _previousName = Entry.Name;

        public string Name
        {
            get => Entry.Name;
            set
            {
                string val = value ?? string.Empty;
                if (Entry.Name != val)
                {
                    _previousName = Entry.Name;
                    Entry.Name = val;
                    OnPropertyChanged();
                }
            }
        }

        public string DetailsText => string.Create(CultureInfo.InvariantCulture, $"L{Entry.MinLevel}-{Entry.MaxLevel} • M{Entry.MinButthurt}-{Entry.MaxButthurt}");
        public string BoundsText => DetailsText;

        public string WeightText => string.Create(CultureInfo.InvariantCulture, $"Weight: {Entry.Weight}");

        public int MinLevel
        {
            get => Entry.MinLevel;
            set
            {
                int clamped = Math.Clamp(value, 1, 30);
                if (Entry.MinLevel != clamped)
                {
                    Entry.MinLevel = clamped;
                    if (Entry.MaxLevel < clamped) Entry.MaxLevel = clamped;
                    NotifyBoundsChanged();
                }
            }
        }

        public int MaxLevel
        {
            get => Entry.MaxLevel;
            set
            {
                int clamped = Math.Clamp(value, 1, 30);
                if (Entry.MaxLevel != clamped)
                {
                    Entry.MaxLevel = clamped;
                    if (Entry.MinLevel > clamped) Entry.MinLevel = clamped;
                    NotifyBoundsChanged();
                }
            }
        }

        public int MinButthurt
        {
            get => Entry.MinButthurt;
            set
            {
                int clamped = Math.Clamp(value, 0, 14);
                if (Entry.MinButthurt != clamped)
                {
                    Entry.MinButthurt = clamped;
                    if (Entry.MaxButthurt < clamped) Entry.MaxButthurt = clamped;
                    NotifyBoundsChanged();
                }
            }
        }

        public int MaxButthurt
        {
            get => Entry.MaxButthurt;
            set
            {
                int clamped = Math.Clamp(value, 0, 14);
                if (Entry.MaxButthurt != clamped)
                {
                    Entry.MaxButthurt = clamped;
                    if (Entry.MinButthurt > clamped) Entry.MinButthurt = clamped;
                    NotifyBoundsChanged();
                }
            }
        }

        public int Weight
        {
            get => Entry.Weight;
            set
            {
                int clamped = Math.Clamp(value, 1, 100);
                if (Entry.Weight != clamped)
                {
                    Entry.Weight = clamped;
                    NotifyBoundsChanged();
                }
            }
        }

        private int? _savedExtendedMinLevel;
        private int? _savedExtendedMaxLevel;
        private int? _savedStockMinLevel;
        private int? _savedStockMaxLevel;

        public int? SavedExtendedMinLevel => _savedExtendedMinLevel;
        public int? SavedExtendedMaxLevel => _savedExtendedMaxLevel;
        public int? SavedStockMinLevel => _savedStockMinLevel;
        public int? SavedStockMaxLevel => _savedStockMaxLevel;

        public int CoverageCells => Math.Max(0, MaxLevel - MinLevel + 1) * Math.Max(0, MaxButthurt - MinButthurt + 1);

        public string CoverageText => string.Create(CultureInfo.InvariantCulture, $"{CoverageCells} cell{(CoverageCells == 1 ? "" : "s")}");

        public FlipperStageCategory StageCategory
        {
            get
            {
                // Stock mode boundaries (L1-3)
                if (MinLevel == 1 && MaxLevel == 1) return FlipperStageCategory.Baby;
                if (MinLevel == 2 && MaxLevel == 2) return FlipperStageCategory.Teen;
                if (MinLevel == 3 && MaxLevel == 3) return FlipperStageCategory.Adult;
                if (MinLevel == 1 && MaxLevel == 3) return FlipperStageCategory.AllStages;
                if (MinLevel == 1 && MaxLevel == 2) return FlipperStageCategory.BabyTeen;
                if (MinLevel == 2 && MaxLevel == 3) return FlipperStageCategory.TeenAdult;

                // Extended mode boundaries (L1-30)
                if (MinLevel >= 1 && MaxLevel <= 9) return FlipperStageCategory.Baby;
                if (MinLevel >= 10 && MaxLevel <= 19) return FlipperStageCategory.Teen;
                if (MinLevel >= 20 && MaxLevel <= 30) return FlipperStageCategory.Adult;
                if (MinLevel == 1 && MaxLevel == 30) return FlipperStageCategory.AllStages;
                if (MinLevel >= 1 && MaxLevel <= 19 && MinLevel <= 9 && MaxLevel >= 10) return FlipperStageCategory.BabyTeen;
                if (MinLevel >= 10 && MaxLevel <= 30 && MinLevel <= 19 && MaxLevel >= 20) return FlipperStageCategory.TeenAdult;

                return FlipperStageCategory.Spanning;
            }
        }

        public string EntryColorHex => StageCategory switch
        {
            FlipperStageCategory.Baby => "#00E5FF",      // Electric Cyan
            FlipperStageCategory.Teen => "#E040FB",      // Hot Magenta
            FlipperStageCategory.Adult => "#FFD600",     // Radiant Gold
            FlipperStageCategory.BabyTeen => "#8B5CF6",  // Vivid Violet
            FlipperStageCategory.TeenAdult => "#FF6D00", // Vivid Orange
            FlipperStageCategory.AllStages => "#E2E8F0", // Silver Platinum
            _ => "#94A3B8",                              // Spanning Slate
        };

        public string StageEmoji => StageCategory switch
        {
            FlipperStageCategory.Baby => "👶",
            FlipperStageCategory.Teen => "👦",
            FlipperStageCategory.Adult => "🐬",
            FlipperStageCategory.BabyTeen => "👶👦",
            FlipperStageCategory.TeenAdult => "👦🐬",
            _ => "🌐",
        };

        public string MoodEmoji =>
            (MinButthurt >= 0 && MaxButthurt <= 4) ? "😊" :
            (MinButthurt >= 5 && MaxButthurt <= 9) ? "😐" :
            (MinButthurt >= 10 && MaxButthurt <= 14) ? "😡" : "🎭";

        public string SummarySubtitleText =>
$"{StageEmoji} {StageName} • {MoodEmoji} {MoodRangeName}";

        private System.Collections.Generic.List<FlipperValidationDiagnostic> _validationDiagnostics = [];

        public System.Collections.Generic.IReadOnlyList<FlipperValidationDiagnostic> ValidationDiagnostics => _validationDiagnostics;

        public bool HasErrors => _validationDiagnostics.Exists(d => d.Severity == FlipperValidationSeverity.Error);

        public bool HasWarnings => _validationDiagnostics.Exists(d => d.Severity == FlipperValidationSeverity.Warning);

        public bool HasValidationIssues => _validationDiagnostics.Count > 0;

        public string ValidationBadgeIcon => HasErrors ? "❌" : (HasWarnings ? "⚠️" : string.Empty);

        public string ValidationBadgeText => HasErrors
            ? string.Create(CultureInfo.InvariantCulture, $"{_validationDiagnostics.FindAll(d => d.Severity == FlipperValidationSeverity.Error).Count} Error(s)")
            : (HasWarnings ? string.Create(CultureInfo.InvariantCulture, $"{_validationDiagnostics.FindAll(d => d.Severity == FlipperValidationSeverity.Warning).Count} Warning(s)") : string.Empty);

        public string ValidationBadgeTooltip => HasValidationIssues
            ? string.Join(Environment.NewLine, System.Linq.Enumerable.Select(_validationDiagnostics, d => $"[{d.Code}] {d.Message}"))
            : "Valid entry";

        public FlipperScheduleEntryViewModel() : this(new FlipperManifestEntry())
        {
        }

        public FlipperScheduleEntryViewModel(FlipperManifestEntry entry)
        {
            Entry = entry ?? throw new ArgumentNullException(nameof(entry));
            _previousName = Entry.Name;

            if (Entry.MaxLevel > 3)
            {
                _savedExtendedMinLevel = Entry.MinLevel;
                _savedExtendedMaxLevel = Entry.MaxLevel;
                var (sMin, sMax) = FlipperScheduleMatrix.ConvertExtendedToStock(Entry.MinLevel, Entry.MaxLevel);
                _savedStockMinLevel = sMin;
                _savedStockMaxLevel = sMax;
            }
            else
            {
                _savedStockMinLevel = Entry.MinLevel;
                _savedStockMaxLevel = Entry.MaxLevel;
                var (eMin, eMax) = FlipperScheduleMatrix.ConvertStockToExtended(Entry.MinLevel, Entry.MaxLevel);
                _savedExtendedMinLevel = eMin;
                _savedExtendedMaxLevel = eMax;
            }
        }

        /// <summary>
        /// Transitions the entry between Stock Mode (L1-3) and Extended Mode (L1-30) losslessly.
        /// Preserves fine-grained custom level numbers when round-tripping.
        /// </summary>
        public void SwitchMode(bool isStockMode)
        {
            if (isStockMode)
            {
                // Switching from Extended to Stock Mode (L1-3)
                if (Entry.MaxLevel > 3 || !_savedExtendedMinLevel.HasValue)
                {
                    _savedExtendedMinLevel = Entry.MinLevel;
                    _savedExtendedMaxLevel = Entry.MaxLevel;
                }

                var (sMin, sMax) = FlipperScheduleMatrix.ConvertExtendedToStock(_savedExtendedMinLevel ?? Entry.MinLevel, _savedExtendedMaxLevel ?? Entry.MaxLevel);
                Entry.MinLevel = sMin;
                Entry.MaxLevel = sMax;
                _savedStockMinLevel = sMin;
                _savedStockMaxLevel = sMax;
            }
            else
            {
                // Switching from Stock to Extended Mode (L1-30)
                _savedStockMinLevel = Entry.MinLevel;
                _savedStockMaxLevel = Entry.MaxLevel;

                if (_savedExtendedMinLevel.HasValue && _savedExtendedMaxLevel.HasValue)
                {
                    var (expectedSMin, expectedSMax) = FlipperScheduleMatrix.ConvertExtendedToStock(_savedExtendedMinLevel.Value, _savedExtendedMaxLevel.Value);
                    if (expectedSMin == Entry.MinLevel && expectedSMax == Entry.MaxLevel)
                    {
                        // Stock stage unchanged -> restore exact precision extended bounds!
                        Entry.MinLevel = _savedExtendedMinLevel.Value;
                        Entry.MaxLevel = _savedExtendedMaxLevel.Value;
                    }
                    else
                    {
                        // Stock stage was modified while in Stock mode -> expand to new stage bounds!
                        var (eMin, eMax) = FlipperScheduleMatrix.ConvertStockToExtended(Entry.MinLevel, Entry.MaxLevel);
                        Entry.MinLevel = eMin;
                        Entry.MaxLevel = eMax;
                        _savedExtendedMinLevel = eMin;
                        _savedExtendedMaxLevel = eMax;
                    }
                }
                else
                {
                    var (eMin, eMax) = FlipperScheduleMatrix.ConvertStockToExtended(Entry.MinLevel, Entry.MaxLevel);
                    Entry.MinLevel = eMin;
                    Entry.MaxLevel = eMax;
                    _savedExtendedMinLevel = eMin;
                    _savedExtendedMaxLevel = eMax;
                }
            }

            NotifyBoundsChanged();
        }

        public void SetSavedBounds(int? extMin, int? extMax, int? stockMin, int? stockMax)
        {
            _savedExtendedMinLevel = extMin;
            _savedExtendedMaxLevel = extMax;
            _savedStockMinLevel = stockMin;
            _savedStockMaxLevel = stockMax;
        }

        public void SetValidationDiagnostics(System.Collections.Generic.IEnumerable<FlipperValidationDiagnostic>? diagnostics)
        {
            _validationDiagnostics = diagnostics != null ? [.. diagnostics] : [];
            OnPropertyChanged(nameof(ValidationDiagnostics));
            OnPropertyChanged(nameof(HasErrors));
            OnPropertyChanged(nameof(HasWarnings));
            OnPropertyChanged(nameof(HasValidationIssues));
            OnPropertyChanged(nameof(ValidationBadgeIcon));
            OnPropertyChanged(nameof(ValidationBadgeText));
            OnPropertyChanged(nameof(ValidationBadgeTooltip));
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string StageName => StageCategory switch
        {
            FlipperStageCategory.Baby when MinLevel == 1 && MaxLevel == 1 => "Baby (L1)",
            FlipperStageCategory.Baby => string.Create(CultureInfo.InvariantCulture, $"Baby (L{MinLevel}-{MaxLevel})"),
            FlipperStageCategory.Teen when MinLevel == 2 && MaxLevel == 2 => "Teen (L2)",
            FlipperStageCategory.Teen => string.Create(CultureInfo.InvariantCulture, $"Teen (L{MinLevel}-{MaxLevel})"),
            FlipperStageCategory.Adult when MinLevel == 3 && MaxLevel == 3 => "Adult (L3)",
            FlipperStageCategory.Adult => string.Create(CultureInfo.InvariantCulture, $"Adult (L{MinLevel}-{MaxLevel})"),
            FlipperStageCategory.BabyTeen when MinLevel == 1 && MaxLevel == 2 => "Baby & Teen (L1-2)",
            FlipperStageCategory.BabyTeen => string.Create(CultureInfo.InvariantCulture, $"Baby & Teen (L{MinLevel}-{MaxLevel})"),
            FlipperStageCategory.TeenAdult when MinLevel == 2 && MaxLevel == 3 => "Teen & Adult (L2-3)",
            FlipperStageCategory.TeenAdult => string.Create(CultureInfo.InvariantCulture, $"Teen & Adult (L{MinLevel}-{MaxLevel})"),
            FlipperStageCategory.AllStages when MaxLevel <= 3 => "All Stages (L1-3)",
            FlipperStageCategory.AllStages => "All Stages (L1-30)",
            _ => "Spanning Stages",
        };

        public string MoodRangeName
        {
            get
            {
                if (MinButthurt >= 0 && MaxButthurt <= 4) return "Happy (0-4)";
                if (MinButthurt >= 5 && MaxButthurt <= 9) return "Neutral (5-9)";
                if (MinButthurt >= 10 && MaxButthurt <= 14) return "Angry (10-14)";
                return "Spanning (0-14)";
            }
        }

        public string MoodColorHex
        {
            get
            {
                if (MinButthurt >= 0 && MaxButthurt <= 4) return "#39FF14"; // Green
                if (MinButthurt >= 5 && MaxButthurt <= 9) return "#FFB300"; // Amber
                if (MinButthurt >= 10 && MaxButthurt <= 14) return "#FF5252"; // Red
                return "#FF8200"; // Orange
            }
        }

        public void SetStage(string stage, bool isStockMode = false)
        {
            if (string.IsNullOrWhiteSpace(stage)) return;

            if (isStockMode)
            {
                if (stage.Equals("Baby", StringComparison.OrdinalIgnoreCase)) { MinLevel = 1; MaxLevel = 1; }
                else if (stage.Equals("Teen", StringComparison.OrdinalIgnoreCase)) { MinLevel = 2; MaxLevel = 2; }
                else if (stage.Equals("Adult", StringComparison.OrdinalIgnoreCase)) { MinLevel = 3; MaxLevel = 3; }
                else if (stage.Equals("All", StringComparison.OrdinalIgnoreCase) || stage.Contains("All", StringComparison.OrdinalIgnoreCase)) { MinLevel = 1; MaxLevel = 3; }
            }
            else
            {
                if (stage.Equals("Baby", StringComparison.OrdinalIgnoreCase)) { MinLevel = 1; MaxLevel = 9; }
                else if (stage.Equals("Teen", StringComparison.OrdinalIgnoreCase)) { MinLevel = 10; MaxLevel = 19; }
                else if (stage.Equals("Adult", StringComparison.OrdinalIgnoreCase)) { MinLevel = 20; MaxLevel = 30; }
                else if (stage.Equals("All", StringComparison.OrdinalIgnoreCase) || stage.Contains("All", StringComparison.OrdinalIgnoreCase)) { MinLevel = 1; MaxLevel = 30; }
            }
            NotifyBoundsChanged();
        }

        public void SetMoodPreset(string mood)
        {
            if (string.IsNullOrWhiteSpace(mood)) return;

            if (mood.Equals("Happy", StringComparison.OrdinalIgnoreCase)) { MinButthurt = 0; MaxButthurt = 4; }
            else if (mood.Equals("Neutral", StringComparison.OrdinalIgnoreCase)) { MinButthurt = 5; MaxButthurt = 9; }
            else if (mood.Equals("Angry", StringComparison.OrdinalIgnoreCase)) { MinButthurt = 10; MaxButthurt = 14; }
            else if (mood.Equals("All", StringComparison.OrdinalIgnoreCase) || mood.Contains("All", StringComparison.OrdinalIgnoreCase)) { MinButthurt = 0; MaxButthurt = 14; }
            NotifyBoundsChanged();
        }

        public string HealthStatusText => HasErrors ? "Error" : (HasWarnings ? "Warning" : "Valid");

        public string? CachedSpriteSignature { get; set; }

        private System.Windows.Media.ImageSource? _spriteThumbnail;
        public System.Windows.Media.ImageSource? SpriteThumbnail
        {
            get => _spriteThumbnail;
            set
            {
                if (SetProperty(ref _spriteThumbnail, value))
                {
                    OnPropertyChanged(nameof(ActivePreviewThumbnail));
                }
            }
        }

        private System.Windows.Media.ImageSource? _activePreviewThumbnail;
        public System.Windows.Media.ImageSource? ActivePreviewThumbnail
        {
            get => _activePreviewThumbnail ?? SpriteThumbnail;
            set => SetProperty(ref _activePreviewThumbnail, value);
        }

        private int _frameCount;
        public int FrameCount
        {
            get => _frameCount;
            set
            {
                if (SetProperty(ref _frameCount, value))
                {
                    OnPropertyChanged(nameof(HasMultipleFrames));
                }
            }
        }

        public Func<System.Collections.Generic.List<System.Windows.Media.ImageSource>>? FrameThumbnailProvider { get; set; }

        public bool HasMultipleFrames => FrameCount > 1 || (CachedFrameThumbnails != null && CachedFrameThumbnails.Count > 1);

        private bool _isPreviewPlaying;
        public bool IsPreviewPlaying
        {
            get => _isPreviewPlaying;
            set
            {
                bool canPlay = value && HasMultipleFrames;
                if (canPlay && CachedFrameThumbnails == null && FrameThumbnailProvider != null)
                {
                    CachedFrameThumbnails = FrameThumbnailProvider();
                }

                if (SetProperty(ref _isPreviewPlaying, canPlay))
                {
                    OnPropertyChanged(nameof(IsPreviewPlaying));
                }
            }
        }

        private int _currentPreviewFrame;
        public int CurrentPreviewFrame
        {
            get => _currentPreviewFrame;
            set => SetProperty(ref _currentPreviewFrame, value);
        }

        private System.Collections.Generic.List<System.Windows.Media.ImageSource>? _cachedFrameThumbnails;
        public System.Collections.Generic.List<System.Windows.Media.ImageSource>? CachedFrameThumbnails
        {
            get
            {
                if (_cachedFrameThumbnails == null && FrameThumbnailProvider != null)
                {
                    _cachedFrameThumbnails = FrameThumbnailProvider();
                }
                return _cachedFrameThumbnails;
            }
            set
            {
                if (SetProperty(ref _cachedFrameThumbnails, value))
                {
                    OnPropertyChanged(nameof(HasMultipleFrames));
                }
            }
        }

        public void StepNextPreviewFrame()
        {
            if (CachedFrameThumbnails == null && FrameThumbnailProvider != null)
            {
                CachedFrameThumbnails = FrameThumbnailProvider();
            }

            if (CachedFrameThumbnails == null || CachedFrameThumbnails.Count <= 1) return;
            _currentPreviewFrame = (_currentPreviewFrame + 1) % CachedFrameThumbnails.Count;
            ActivePreviewThumbnail = CachedFrameThumbnails[_currentPreviewFrame];
        }

        public void ResetPreviewFrame()
        {
            _currentPreviewFrame = 0;
            ActivePreviewThumbnail = null;
            IsPreviewPlaying = false;
        }

        public void InvalidateThumbnail()
        {
            CachedSpriteSignature = null;
            SpriteThumbnail = null;
            _activePreviewThumbnail = null;
            CachedFrameThumbnails = null;
            _currentPreviewFrame = 0;
            _isPreviewPlaying = false;
            OnPropertyChanged(nameof(ActivePreviewThumbnail));
            OnPropertyChanged(nameof(HasMultipleFrames));
        }

        private string _frameCountText = "0 frames";
        public string FrameCountText
        {
            get => _frameCountText;
            set => SetProperty(ref _frameCountText, value);
        }

        private string _dimensionsText = "128x64";
        public string DimensionsText
        {
            get => _dimensionsText;
            set => SetProperty(ref _dimensionsText, value);
        }

        private bool _hasSprite;
        public bool HasSprite
        {
            get => _hasSprite;
            set => SetProperty(ref _hasSprite, value);
        }

        public void NotifyBoundsChanged()
        {
            OnPropertyChanged(nameof(MinLevel));
            OnPropertyChanged(nameof(MaxLevel));
            OnPropertyChanged(nameof(MinButthurt));
            OnPropertyChanged(nameof(MaxButthurt));
            OnPropertyChanged(nameof(Weight));
            OnPropertyChanged(nameof(DetailsText));
            OnPropertyChanged(nameof(WeightText));
            OnPropertyChanged(nameof(CoverageCells));
            OnPropertyChanged(nameof(CoverageText));
            OnPropertyChanged(nameof(StageCategory));
            OnPropertyChanged(nameof(EntryColorHex));
            OnPropertyChanged(nameof(StageName));
            OnPropertyChanged(nameof(StageEmoji));
            OnPropertyChanged(nameof(MoodRangeName));
            OnPropertyChanged(nameof(MoodEmoji));
            OnPropertyChanged(nameof(MoodColorHex));
            OnPropertyChanged(nameof(SummarySubtitleText));
            OnPropertyChanged(nameof(HealthStatusText));
            OnPropertyChanged(nameof(ValidationDiagnostics));
            OnPropertyChanged(nameof(HasErrors));
            OnPropertyChanged(nameof(HasWarnings));
            OnPropertyChanged(nameof(HasValidationIssues));
            OnPropertyChanged(nameof(ValidationBadgeIcon));
            OnPropertyChanged(nameof(ValidationBadgeText));
            OnPropertyChanged(nameof(ValidationBadgeTooltip));
        }
    }
}
