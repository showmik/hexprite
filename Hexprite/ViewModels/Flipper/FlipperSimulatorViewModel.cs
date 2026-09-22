using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hexprite.Core;
using Hexprite.Resources.Fonts;
using Hexprite.Services;

namespace Hexprite.ViewModels.Flipper
{
    public enum FlipperPlaybackMode
    {
        Passive,
        Active,
        Cooldown
    }

    public class CandidateAnimationVm : ObservableObject
    {
        private string _name = string.Empty;
        private int _weight = 1;
        private double _probabilityPercent;
        private bool _isAllMode;
        private SpriteState? _sprite;
        private FlipperManifestEntry? _manifestEntry;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public int Weight
        {
            get => _weight;
            set => SetProperty(ref _weight, value);
        }

        public bool IsAllMode
        {
            get => _isAllMode;
            set
            {
                if (SetProperty(ref _isAllMode, value))
                {
                    OnPropertyChanged(nameof(ProbabilitySummary));
                }
            }
        }

        public double ProbabilityPercent
        {
            get => _probabilityPercent;
            set
            {
                if (SetProperty(ref _probabilityPercent, value))
                {
                    OnPropertyChanged(nameof(ProbabilitySummary));
                }
            }
        }

        public string ProbabilitySummary => IsAllMode
            ? $"Weight: {Weight}"
            : $"{ProbabilityPercent:F1}% (W:{Weight})";

        public string LevelMoodSummary
        {
            get
            {
                if (ManifestEntry == null) return string.Empty;
                return $"L{ManifestEntry.MinLevel}-{ManifestEntry.MaxLevel} • M{ManifestEntry.MinButthurt}-{ManifestEntry.MaxButthurt}";
            }
        }

        public string FrameSummary
        {
            get
            {
                if (Sprite == null) return "-";
                return $"{Sprite.Frames.Count}f • {Sprite.FrameRateFps}fps";
            }
        }

        public string CycleSummary
        {
            get
            {
                if (Sprite?.FlipperCycle == null) return "Passive";
                var cycle = Sprite.FlipperCycle;
                if (cycle.ActiveFrameCount > 0)
                {
                    return $"⚡ Active ({cycle.ActiveFrameCount}f ×{cycle.ActiveCycles})";
                }
                return "Passive";
            }
        }

        public bool HasActiveFrames => Sprite?.FlipperCycle?.ActiveFrameCount > 0;

        public SpriteState? Sprite
        {
            get => _sprite;
            set
            {
                if (SetProperty(ref _sprite, value))
                {
                    OnPropertyChanged(nameof(FrameSummary));
                    OnPropertyChanged(nameof(CycleSummary));
                    OnPropertyChanged(nameof(HasActiveFrames));
                }
            }
        }

        public FlipperManifestEntry? ManifestEntry
        {
            get => _manifestEntry;
            set
            {
                if (SetProperty(ref _manifestEntry, value))
                {
                    OnPropertyChanged(nameof(LevelMoodSummary));
                }
            }
        }
    }

    public partial class FlipperSimulatorViewModel : ObservableObject, IDisposable
    {
        private readonly IFlipperImportService _importService;
        private readonly IFlipperExportService _exportService;
        private readonly IFlipperWindowManager? _windowManager;
        private readonly IWorkspaceTabService? _tabService;
        private readonly IDialogService? _dialogService;
        private readonly IUserFeedbackService? _feedbackService;

        private IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> _animations;
        private DispatcherTimer? _animTimer;
        private readonly WriteableBitmap _screenBitmap;
        private readonly uint[] _pixelBuffer = new uint[128 * 64];
        private readonly bool[] _screenPixels = new bool[128 * 64];
        private readonly Random _rng = new();

        private string _packName = "AssetPack";
        private int _level = 1;
        private int _mood;
        private bool _isAllAnimationsMode;
        private CandidateAnimationVm? _selectedCandidate;
        private FlipperPlaybackMode _playbackMode = FlipperPlaybackMode.Passive;
        private int _sequenceIndex;
        private int _activeCycleCurrent = 1;
        private int _cooldownRemaining;
        private bool _isPlaying = true;
        private double _speedMultiplier = 1.0;
        private int _selectedPaletteIndex;
        private bool _showLcdGrid = true;
        private bool _showDesktopHud = true;
        private bool _showSpeechBubble = true;
        private string _customBubbleText = "Feed me!";
        private int _bubbleX = 14;
        private int _bubbleY = 4;
        private SpeechBubbleTailPosition _bubbleTail = SpeechBubbleTailPosition.BottomLeft;
        private string _rngRollFeedbackText = string.Empty;
        private bool _isUpdatingCandidates;
        private bool _isDisposed;

        // ── Performance & Zero-Allocation Playback Caching ──────────────────
        private int[] _cachedPassiveSequence = [];
        private int[] _cachedActiveSequence = [];
        private FlipperSpeechBubble? _cachedBubble;
        private bool _isBubbleCacheDirty = true;

        public WriteableBitmap ScreenBitmap => _screenBitmap;

        public bool[] GetScreenPixelsCopy()
        {
            var copy = new bool[128 * 64];
            Array.Copy(_screenPixels, copy, copy.Length);
            return copy;
        }

        public string PackName
        {
            get => _packName;
            set
            {
                if (SetProperty(ref _packName, value))
                {
                    OnPropertyChanged(nameof(ManifestInfo));
                }
            }
        }

        public bool HasAnimations => _animations.Count > 0;

        public IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> Animations
        {
            get => _animations;
            set
            {
                if (SetProperty(ref _animations, value))
                {
                    OnPropertyChanged(nameof(HasAnimations));
                    OnPropertyChanged(nameof(ManifestInfo));
                    UpdateCandidates();
                }
            }
        }

        public int Level
        {
            get => _level;
            set
            {
                int clamped = Math.Clamp(value, 1, 30);
                if (SetProperty(ref _level, clamped))
                {
                    OnPropertyChanged(nameof(LevelDescription));
                    UpdateCandidates();
                }
            }
        }

        public int Mood
        {
            get => _mood;
            set
            {
                int clamped = Math.Clamp(value, 0, 14);
                if (SetProperty(ref _mood, clamped))
                {
                    OnPropertyChanged(nameof(MoodDescription));
                    UpdateCandidates();
                }
            }
        }

        public string LevelDescription
        {
            get
            {
                string levelTier = Level switch
                {
                    <= 3 => "🐣 Baby (L1-3)",
                    <= 9 => "🌿 Growing (L4-9)",
                    <= 19 => "⚡ Advanced (L10-19)",
                    _ => "👑 Master (L20-30)"
                };
                return $"Level {Level} - {levelTier}";
            }
        }

        public string MoodDescription
        {
            get
            {
                return Mood switch
                {
                    0 => "0 - 😄 Ecstatic",
                    <= 3 => $"{Mood} - 😊 Happy",
                    <= 7 => $"{Mood} - 😐 Idle",
                    <= 11 => $"{Mood} - 😠 Annoyed",
                    _ => $"{Mood} - 🤬 Enraged"
                };
            }
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    UpdateCandidates();
                }
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "WPF data binding target on ViewModel instance")]
        public IReadOnlyList<ThemePaletteInfo> ThemePalettes => FlipperThemeService.Palettes;

        public bool IsAllAnimationsMode
        {
            get => _isAllAnimationsMode;
            set
            {
                if (SetProperty(ref _isAllAnimationsMode, value))
                {
                    OnPropertyChanged(nameof(CandidateModeIndex));
                    OnPropertyChanged(nameof(CandidatesHeader));
                    UpdateCandidates();
                }
            }
        }

        public int CandidateModeIndex
        {
            get => _isAllAnimationsMode ? 1 : 0;
            set => IsAllAnimationsMode = (value == 1);
        }

        public string CandidatesHeader => _isAllAnimationsMode
            ? $"All Animations in Pack ({_animations.Count})"
            : "Matching Scheduled in Pack";

        public ObservableCollection<CandidateAnimationVm> Candidates { get; } = [];

        private string _lastButtonPressedText = string.Empty;
        private bool _exportFullCycle = true;
        private string _bubbleSavedFeedbackText = string.Empty;
        private int _selectedInspectorTabIndex;

        public int SelectedInspectorTabIndex
        {
            get => _selectedInspectorTabIndex;
            set => SetProperty(ref _selectedInspectorTabIndex, Math.Clamp(value, 0, 1));
        }

        public event EventHandler? DocumentModified;
        public event EventHandler? RequestClose;
        public event Action<string>? LocateEntryRequested;

        private string? _currentFilePath;
        public string? CurrentFilePath
        {
            get => _currentFilePath;
            set => SetProperty(ref _currentFilePath, value);
        }

        public string LastButtonPressedText
        {
            get => _lastButtonPressedText;
            private set => SetProperty(ref _lastButtonPressedText, value);
        }

        public string RngRollFeedbackText
        {
            get => _rngRollFeedbackText;
            private set => SetProperty(ref _rngRollFeedbackText, value);
        }

        public bool HasNoMatchingCandidates => _selectedCandidate == null;

        public bool ExportFullCycle
        {
            get => _exportFullCycle;
            set => SetProperty(ref _exportFullCycle, value);
        }

        public string BubbleSavedFeedbackText
        {
            get => _bubbleSavedFeedbackText;
            private set => SetProperty(ref _bubbleSavedFeedbackText, value);
        }

        public CandidateAnimationVm? SelectedCandidate
        {
            get => _selectedCandidate;
            set
            {
                if (SetProperty(ref _selectedCandidate, value))
                {
                    RefreshCachedSequences();
                    _isBubbleCacheDirty = true;
                    if (value != null)
                    {
                        if (!_isUpdatingCandidates && _isAllAnimationsMode && value.ManifestEntry != null)
                        {
                            if (Level < value.ManifestEntry.MinLevel || Level > value.ManifestEntry.MaxLevel)
                            {
                                Level = value.ManifestEntry.MinLevel;
                            }
                            if (Mood < value.ManifestEntry.MinButthurt || Mood > value.ManifestEntry.MaxButthurt)
                            {
                                Mood = value.ManifestEntry.MinButthurt;
                            }
                        }

                        if (value.Sprite?.FlipperCycle?.SpeechBubble is FlipperSpeechBubble b)
                        {
                            _customBubbleText = b.Text;
                            _bubbleX = b.X;
                            _bubbleY = b.Y;
                            _bubbleTail = b.Tail;
                            _isBubbleCacheDirty = true;
                            OnPropertyChanged(nameof(CustomBubbleText));
                            OnPropertyChanged(nameof(BubbleX));
                            OnPropertyChanged(nameof(BubbleY));
                            OnPropertyChanged(nameof(BubbleTail));
                            OnPropertyChanged(nameof(BubbleTailIndex));
                        }

                        BubbleSavedFeedbackText = string.Empty;
                        ResetAnimationToPassive();
                    }
                }
            }
        }

        public FlipperPlaybackMode PlaybackMode
        {
            get => _playbackMode;
            private set
            {
                if (SetProperty(ref _playbackMode, value))
                {
                    OnPropertyChanged(nameof(StateBadgeText));
                }
            }
        }

        public int SequenceIndex
        {
            get => _sequenceIndex;
            set
            {
                if (SetProperty(ref _sequenceIndex, value))
                {
                    OnPropertyChanged(nameof(FrameCounterText));
                    RenderCurrentFrame();
                }
            }
        }

        public int MaxSequenceIndex
        {
            get
            {
                if (_selectedCandidate?.Sprite == null) return 0;
                int[] currentSeq = _playbackMode == FlipperPlaybackMode.Passive
                    ? _cachedPassiveSequence
                    : _cachedActiveSequence;
                return Math.Max(0, currentSeq.Length - 1);
            }
        }

        public string StateBadgeText
        {
            get
            {
                if (_selectedCandidate == null) return "[⛔ Idle / None]";
                var cycle = _selectedCandidate.Sprite?.FlipperCycle;
                return _playbackMode switch
                {
                    FlipperPlaybackMode.Passive => "[🟢 Passive Loop]",
                    FlipperPlaybackMode.Active => $"[⚡ Active ({_activeCycleCurrent}/{cycle?.ActiveCycles ?? 1})]",
                    FlipperPlaybackMode.Cooldown => $"[⏳ Cooldown: {_cooldownRemaining} ticks]",
                    _ => "[Idle]"
                };
            }
        }

        public string CurrentAnimTitle => _selectedCandidate != null
            ? $"Current: {_selectedCandidate.Name}"
            : "Current: (No Matching Animation)";

        public string CurrentAnimDetails
        {
            get
            {
                if (_selectedCandidate?.Sprite == null)
                    return "Adjust level or mood or switch to 'All Animations' mode.";
                var sprite = _selectedCandidate.Sprite;
                return $"FPS: {sprite.FrameRateFps} | Total Frames: {sprite.Frames.Count} | Weight: {_selectedCandidate.Weight}";
            }
        }

        public string CycleDetails
        {
            get
            {
                if (_selectedCandidate?.Sprite == null)
                    return "Passive: - | Active: - | Cooldown: -";
                var cycle = _selectedCandidate.Sprite.FlipperCycle;
                if (cycle != null)
                {
                    string durationInfo = cycle.Duration > 0 ? $" | Duration: {cycle.Duration}" : "";
                    return $"Passive: {cycle.PassiveFrameCount} | Active: {cycle.ActiveFrameCount} (×{cycle.ActiveCycles}) | Cooldown: {cycle.ActiveCooldown}{durationInfo} | Bubble Slots: {cycle.BubbleSlots}";
                }
                return $"Passive: {_selectedCandidate.Sprite.Frames.Count} | Active: 0 | Cooldown: 0 | Bubble Slots: 0";
            }
        }

        public string FrameCounterText
        {
            get
            {
                if (_selectedCandidate?.Sprite == null) return "Frame 0 / 0";
                var sprite = _selectedCandidate.Sprite;
                if (sprite.Frames.Count == 0) return "Frame 0 / 0";
                int[] currentSeq = _playbackMode == FlipperPlaybackMode.Passive
                    ? _cachedPassiveSequence
                    : _cachedActiveSequence;
                if (currentSeq.Length == 0) return "Frame 0 / 0";
                int safeSeq = Math.Clamp(_sequenceIndex, 0, currentSeq.Length - 1);
                int frameIdx = currentSeq[safeSeq];
                return $"Frame {_sequenceIndex + 1} / {currentSeq.Length} (#{frameIdx})";
            }
        }

        public string ManifestInfo => $"Loaded Pack: {PackName} ({_animations.Count} {(_animations.Count == 1 ? "animation" : "animations")})";

        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (SetProperty(ref _isPlaying, value))
                {
                    if (_isPlaying)
                    {
                        UpdateTimerInterval();
                        _animTimer?.Start();
                    }
                    else
                    {
                        _animTimer?.Stop();
                    }
                    OnPropertyChanged(nameof(PlayPauseText));
                    OnPropertyChanged(nameof(PlayPauseToolTip));
                    OnPropertyChanged(nameof(PlayPauseIconData));
                }
            }
        }

        public string PlayPauseText => IsPlaying ? "Pause" : "Play";
        public string PlayPauseToolTip => IsPlaying ? "Pause Animation (Space)" : "Play Animation (Space)";
        public string PlayPauseIconData => IsPlaying
            ? "M 3,2 H 6 V 14 H 3 Z M 10,2 H 13 V 14 H 10 Z"
            : "M 4,2 L 14,8 L 4,14 Z";

        public double SpeedMultiplier
        {
            get => _speedMultiplier;
            set
            {
                if (SetProperty(ref _speedMultiplier, value))
                {
                    UpdateTimerInterval();
                }
            }
        }

        public int SelectedPaletteIndex
        {
            get => _selectedPaletteIndex;
            set
            {
                if (SetProperty(ref _selectedPaletteIndex, value))
                {
                    RenderCurrentFrame();
                }
            }
        }

        public bool ShowLcdGrid
        {
            get => _showLcdGrid;
            set
            {
                if (SetProperty(ref _showLcdGrid, value))
                {
                    RenderCurrentFrame();
                }
            }
        }

        public bool ShowDesktopHud
        {
            get => _showDesktopHud;
            set
            {
                if (SetProperty(ref _showDesktopHud, value))
                {
                    RenderCurrentFrame();
                }
            }
        }

        public bool ShowSpeechBubble
        {
            get => _showSpeechBubble;
            set
            {
                if (SetProperty(ref _showSpeechBubble, value))
                {
                    RenderCurrentFrame();
                }
            }
        }

        public string CustomBubbleText
        {
            get => _customBubbleText;
            set
            {
                if (SetProperty(ref _customBubbleText, value))
                {
                    _isBubbleCacheDirty = true;
                    OnPropertyChanged(nameof(MaxBubbleX));
                    OnPropertyChanged(nameof(MaxBubbleY));
                    BubbleX = Math.Clamp(_bubbleX, 0, MaxBubbleX);
                    BubbleY = Math.Clamp(_bubbleY, 0, MaxBubbleY);
                    RenderCurrentFrame();
                }
            }
        }

        public int MaxBubbleX
        {
            get
            {
                string text = _customBubbleText?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(text)) return 110;
                var dummy = new FlipperSpeechBubble(1, 0, 0, text, _bubbleTail);
                var (bw, _) = dummy.MeasureBubble();
                return Math.Max(0, 128 - bw);
            }
        }

        public int MaxBubbleY
        {
            get
            {
                string text = _customBubbleText?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(text)) return 52;
                var dummy = new FlipperSpeechBubble(1, 0, 0, text, _bubbleTail);
                var (_, bh) = dummy.MeasureBubble();
                return Math.Max(0, 64 - bh);
            }
        }

        public int BubbleX
        {
            get => _bubbleX;
            set
            {
                int clamped = Math.Clamp(value, 0, MaxBubbleX);
                if (SetProperty(ref _bubbleX, clamped))
                {
                    _isBubbleCacheDirty = true;
                    RenderCurrentFrame();
                }
            }
        }

        public int BubbleY
        {
            get => _bubbleY;
            set
            {
                int clamped = Math.Clamp(value, 0, MaxBubbleY);
                if (SetProperty(ref _bubbleY, clamped))
                {
                    _isBubbleCacheDirty = true;
                    RenderCurrentFrame();
                }
            }
        }

        public SpeechBubbleTailPosition BubbleTail
        {
            get => _bubbleTail;
            set
            {
                if (SetProperty(ref _bubbleTail, value))
                {
                    _isBubbleCacheDirty = true;
                    OnPropertyChanged(nameof(BubbleTailIndex));
                    OnPropertyChanged(nameof(MaxBubbleX));
                    OnPropertyChanged(nameof(MaxBubbleY));
                    BubbleX = Math.Clamp(_bubbleX, 0, MaxBubbleX);
                    BubbleY = Math.Clamp(_bubbleY, 0, MaxBubbleY);
                    RenderCurrentFrame();
                }
            }
        }

        public int BubbleTailIndex
        {
            get => _bubbleTail switch
            {
                SpeechBubbleTailPosition.BottomLeft => 0,
                SpeechBubbleTailPosition.BottomRight => 1,
                SpeechBubbleTailPosition.TopLeft => 2,
                SpeechBubbleTailPosition.TopRight => 3,
                _ => 4
            };
            set
            {
                BubbleTail = value switch
                {
                    0 => SpeechBubbleTailPosition.BottomLeft,
                    1 => SpeechBubbleTailPosition.BottomRight,
                    2 => SpeechBubbleTailPosition.TopLeft,
                    3 => SpeechBubbleTailPosition.TopRight,
                    _ => SpeechBubbleTailPosition.None
                };
            }
        }

        // ── Commands ────────────────────────────────────────────────────────
        public IRelayCommand PlayPauseCommand { get; }
        public IRelayCommand StepBackCommand { get; }
        public IRelayCommand StepForwardCommand { get; }
        public IRelayCommand ResetCommand { get; }
        public IRelayCommand TriggerActiveCommand { get; }
        public IRelayCommand RollRngCommand { get; }
        public IRelayCommand<string> SetBubbleAlignmentCommand { get; }
        public IRelayCommand LoadPackCommand { get; }
        public IRelayCommand ExportGifCommand { get; }
        public IRelayCommand OpenInTabsCommand { get; }
        public IRelayCommand FlashUsbCommand { get; }
        public IRelayCommand OpenScheduleMatrixCommand { get; }
        public IRelayCommand OpenLiveMirrorCommand { get; }
        public IRelayCommand OpenMediaSlicerCommand { get; }
        public IRelayCommand TriggerUpCommand { get; }
        public IRelayCommand TriggerDownCommand { get; }
        public IRelayCommand TriggerLeftCommand { get; }
        public IRelayCommand TriggerRightCommand { get; }
        public IRelayCommand TriggerOkCommand { get; }
        public IRelayCommand TriggerBackCommand { get; }
        public IRelayCommand ApplyBubbleToSelectedCommand { get; }
        public IRelayCommand<object> JumpToStateCommand { get; }
        public IRelayCommand JumpToNearestCoveredStateCommand { get; }
        public IRelayCommand PopOutWindowCommand { get; }
        public IRelayCommand NavigateToSelectedTabCommand { get; }
        public IRelayCommand LocateSelectedInMatrixCommand { get; }
        public IRelayCommand SaveAsHexpackCommand { get; }
        public IRelayCommand OpenInAssetPackStudioCommand { get; }
        public IRelayCommand LoadPackFileCommand { get; }
        public IRelayCommand LoadPackFolderCommand { get; }

        public FlipperSimulatorViewModel(
            IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> animations,
            string packName = "AssetPack",
            IFlipperImportService? importService = null,
            IFlipperExportService? exportService = null,
            IFlipperWindowManager? windowManager = null,
            IWorkspaceTabService? tabService = null,
            IDialogService? dialogService = null,
            IUserFeedbackService? feedbackService = null)
        {
            _animations = animations ?? [];
            _packName = packName;
            _importService = importService ?? new FlipperImportService();
            _exportService = exportService ?? new FlipperExportService();
            _windowManager = windowManager;
            _tabService = tabService;
            _dialogService = dialogService;
            _feedbackService = feedbackService;

            _screenBitmap = new WriteableBitmap(128, 64, 96, 96, PixelFormats.Bgra32, null);

            // Setup timer for UI playback if dispatcher is available with Render priority for smooth pacing
            try
            {
                if (Application.Current?.Dispatcher != null)
                {
                    _animTimer = new DispatcherTimer(DispatcherPriority.Render);
                    _animTimer.Tick += OnAnimTimerTick;
                }
            }
            catch
            {
                // In headless tests Dispatcher may not be initialized
            }

            PlayPauseCommand = new RelayCommand(TogglePlayPause);
            StepBackCommand = new RelayCommand(StepBack);
            StepForwardCommand = new RelayCommand(AdvanceFrame);
            ResetCommand = new RelayCommand(ResetAnimationToPassive);
            TriggerActiveCommand = new RelayCommand(TriggerActive);
            RollRngCommand = new RelayCommand(RollRng);
            SetBubbleAlignmentCommand = new RelayCommand<string>(SetBubbleAlignment);
            LoadPackCommand = new RelayCommand(LoadPackFolder);
            LoadPackFolderCommand = new RelayCommand(LoadPackFolder);
            LoadPackFileCommand = new RelayCommand(() => LoadPackFile());
            ExportGifCommand = new RelayCommand(ExportGif);
            OpenInTabsCommand = new RelayCommand(OpenInTabs);
            FlashUsbCommand = new RelayCommand(FlashUsb);
            OpenScheduleMatrixCommand = new RelayCommand(OpenScheduleMatrix);
            OpenLiveMirrorCommand = new RelayCommand(OpenLiveMirror);
            OpenMediaSlicerCommand = new RelayCommand(OpenMediaSlicer);

            TriggerUpCommand = new RelayCommand(() => HandleDpadPress("UP"));
            TriggerDownCommand = new RelayCommand(() => HandleDpadPress("DOWN"));
            TriggerLeftCommand = new RelayCommand(() => HandleDpadPress("LEFT"));
            TriggerRightCommand = new RelayCommand(() => HandleDpadPress("RIGHT"));
            TriggerOkCommand = new RelayCommand(() => HandleDpadPress("OK"));
            TriggerBackCommand = new RelayCommand(() => HandleDpadPress("BACK"));
            ApplyBubbleToSelectedCommand = new RelayCommand(ApplyBubbleToSelected);
            JumpToNearestCoveredStateCommand = new RelayCommand(JumpToNearestCoveredState);
            PopOutWindowCommand = new RelayCommand(PopOutWindow);
            NavigateToSelectedTabCommand = new RelayCommand(NavigateToSelectedTab);
            LocateSelectedInMatrixCommand = new RelayCommand(LocateSelectedInMatrix);
            SaveAsHexpackCommand = new RelayCommand(() => SaveAsHexpack());
            OpenInAssetPackStudioCommand = new RelayCommand(OpenInAssetPackStudio);
            JumpToStateCommand = new RelayCommand<object>(param =>
            {
                if (param is (int lvl, int mood))
                {
                    JumpToState(lvl, mood);
                }
            });

            if (_animations.Count > 0)
            {
                var first = _animations[0];
                bool matchesAny = _animations.Any(a =>
                    a.ManifestEntry.MinLevel <= _level && a.ManifestEntry.MaxLevel >= _level &&
                    a.ManifestEntry.MinButthurt <= _mood && a.ManifestEntry.MaxButthurt >= _mood);

                if (!matchesAny)
                {
                    _level = first.ManifestEntry.MinLevel;
                    _mood = first.ManifestEntry.MinButthurt;
                }
            }

            UpdateCandidates();
        }

        private void OnAnimTimerTick(object? sender, EventArgs e) => AdvanceFrame();

        private void RefreshCachedSequences()
        {
            if (_selectedCandidate?.Sprite == null)
            {
                _cachedPassiveSequence = [];
                _cachedActiveSequence = [];
                return;
            }

            _cachedPassiveSequence = GetPassiveSequence(_selectedCandidate.Sprite);
            _cachedActiveSequence = GetActiveSequence(_selectedCandidate.Sprite);
        }

        private FlipperSpeechBubble? GetOrUpdateCachedBubble()
        {
            if (_isBubbleCacheDirty)
            {
                string bubbleText = _customBubbleText?.Trim() ?? string.Empty;
                _cachedBubble = !string.IsNullOrEmpty(bubbleText)
                    ? new FlipperSpeechBubble(1, _bubbleX, _bubbleY, bubbleText, _bubbleTail)
                    : null;
                _isBubbleCacheDirty = false;
            }
            return _cachedBubble;
        }

        public void UpdateCandidates()
        {
            if (_isUpdatingCandidates) return;
            _isUpdatingCandidates = true;
            try
            {
                string? prevSelectedName = _selectedCandidate?.Name;
                var items = new List<CandidateAnimationVm>();

                if (_isAllAnimationsMode)
                {
                    foreach (var (name, sprite, entry) in _animations)
                    {
                        items.Add(new CandidateAnimationVm
                        {
                            Name = name,
                            Weight = entry.Weight,
                            IsAllMode = true,
                            ProbabilityPercent = 100.0,
                            Sprite = sprite,
                            ManifestEntry = entry,
                        });
                    }
                }
                else
                {
                    int totalWeight = 0;
                    foreach (var (name, sprite, entry) in _animations)
                    {
                        if (entry.MinLevel <= Level && entry.MaxLevel >= Level &&
                            entry.MinButthurt <= Mood && entry.MaxButthurt >= Mood)
                        {
                            totalWeight += entry.Weight;
                            items.Add(new CandidateAnimationVm
                            {
                                Name = name,
                                Weight = entry.Weight,
                                IsAllMode = false,
                                Sprite = sprite,
                                ManifestEntry = entry,
                            });
                        }
                    }

                    foreach (var c in items)
                    {
                        c.ProbabilityPercent = totalWeight > 0 ? ((double)c.Weight / totalWeight) * 100.0 : 0.0;
                    }
                }

                // Filter by SearchText if specified
                if (!string.IsNullOrWhiteSpace(_searchText))
                {
                    items = items.Where(c => c.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                Candidates.Clear();
                foreach (var item in items)
                {
                    Candidates.Add(item);
                }

                if (items.Count > 0)
                {
                    var retained = items.Find(c => c.Name == prevSelectedName)
                        ?? items.Find(c => c.ManifestEntry.MinLevel <= _level && c.ManifestEntry.MaxLevel >= _level && c.ManifestEntry.MinButthurt <= _mood && c.ManifestEntry.MaxButthurt >= _mood);
                    SelectedCandidate = retained ?? items[0];
                }
                else
                {
                    _selectedCandidate = null;
                    RefreshCachedSequences();
                    _animTimer?.Stop();
                    if (_animations.Count == 0)
                    {
                        RenderBlankScreen("No animations in pack\nOpen or import pack");
                    }
                    else
                    {
                        RenderBlankScreen("No animation matches\ncurrent Level & Mood");
                    }
                    OnPropertyChanged(nameof(SelectedCandidate));
                    OnPropertyChanged(nameof(CurrentAnimTitle));
                    OnPropertyChanged(nameof(CurrentAnimDetails));
                    OnPropertyChanged(nameof(CycleDetails));
                    OnPropertyChanged(nameof(StateBadgeText));
                    OnPropertyChanged(nameof(MaxSequenceIndex));
                    OnPropertyChanged(nameof(FrameCounterText));
                }
                OnPropertyChanged(nameof(HasNoMatchingCandidates));
            }
            finally
            {
                _isUpdatingCandidates = false;
            }
        }

        public void RollRng()
        {
            if (Candidates.Count == 0)
            {
                RngRollFeedbackText = "⚠️ No animations available to roll in current state.";
                return;
            }
            int totalWeight = Candidates.Sum(x => x.Weight);
            if (totalWeight <= 0) return;

            int pick = _rng.Next(totalWeight);
            int cumulative = 0;
            foreach (var item in Candidates)
            {
                cumulative += item.Weight;
                if (pick < cumulative)
                {
                    SelectedCandidate = item;
                    RngRollFeedbackText = $"🎲 Rolled {pick + 1}/{totalWeight} ➜ Selected '{item.Name}' ({item.ProbabilitySummary})";
                    break;
                }
            }
        }

        public void ResetAnimationToPassive()
        {
            if (_selectedCandidate?.Sprite == null) return;

            RefreshCachedSequences();
            PlaybackMode = FlipperPlaybackMode.Passive;
            _sequenceIndex = 0;
            _activeCycleCurrent = 1;
            _cooldownRemaining = 0;

            OnPropertyChanged(nameof(SequenceIndex));
            OnPropertyChanged(nameof(CurrentAnimTitle));
            OnPropertyChanged(nameof(CurrentAnimDetails));
            OnPropertyChanged(nameof(CycleDetails));
            OnPropertyChanged(nameof(MaxSequenceIndex));
            OnPropertyChanged(nameof(FrameCounterText));
            OnPropertyChanged(nameof(StateBadgeText));

            UpdateTimerInterval();

            if (_isPlaying)
            {
                _animTimer?.Start();
            }

            RenderCurrentFrame();
        }

        public void TriggerActive()
        {
            if (_selectedCandidate?.Sprite == null) return;
            RefreshCachedSequences();
            var activeSeq = _cachedActiveSequence;
            if (activeSeq.Length == 0) return;

            PlaybackMode = FlipperPlaybackMode.Active;
            _sequenceIndex = 0;
            _activeCycleCurrent = 1;
            _cooldownRemaining = 0;

            OnPropertyChanged(nameof(SequenceIndex));
            OnPropertyChanged(nameof(MaxSequenceIndex));
            OnPropertyChanged(nameof(FrameCounterText));
            OnPropertyChanged(nameof(StateBadgeText));

            if (_isPlaying)
            {
                _animTimer?.Start();
            }

            RenderCurrentFrame();
        }

        public void AdvanceFrame()
        {
            if (_selectedCandidate?.Sprite == null || _selectedCandidate.Sprite.Frames.Count == 0) return;
            var sprite = _selectedCandidate.Sprite;
            var cycle = sprite.FlipperCycle;

            if (_playbackMode == FlipperPlaybackMode.Passive)
            {
                var passiveSeq = _cachedPassiveSequence;
                _sequenceIndex = (passiveSeq.Length > 0) ? (_sequenceIndex + 1) % passiveSeq.Length : 0;
            }
            else if (_playbackMode == FlipperPlaybackMode.Active)
            {
                var activeSeq = _cachedActiveSequence;
                _sequenceIndex++;
                if (_sequenceIndex >= activeSeq.Length)
                {
                    int maxCycles = cycle != null ? Math.Max(1, cycle.ActiveCycles) : 1;
                    if (_activeCycleCurrent < maxCycles)
                    {
                        _activeCycleCurrent++;
                        _sequenceIndex = 0;
                    }
                    else
                    {
                        int cooldown = cycle != null ? cycle.ActiveCooldown : 0;
                        if (cooldown > 0)
                        {
                            PlaybackMode = FlipperPlaybackMode.Cooldown;
                            _cooldownRemaining = cooldown;
                            _sequenceIndex = Math.Max(0, activeSeq.Length - 1);
                        }
                        else
                        {
                            PlaybackMode = FlipperPlaybackMode.Passive;
                            _sequenceIndex = 0;
                        }
                    }
                }
            }
            else if (_playbackMode == FlipperPlaybackMode.Cooldown)
            {
                _cooldownRemaining--;
                if (_cooldownRemaining <= 0)
                {
                    PlaybackMode = FlipperPlaybackMode.Passive;
                    _sequenceIndex = 0;
                }
                else
                {
                    // During cooldown hold, update state badge without redundant re-compositing
                    OnPropertyChanged(nameof(StateBadgeText));
                    return;
                }
            }

            OnPropertyChanged(nameof(SequenceIndex));
            OnPropertyChanged(nameof(MaxSequenceIndex));
            OnPropertyChanged(nameof(FrameCounterText));
            OnPropertyChanged(nameof(StateBadgeText));
            RenderCurrentFrame();
        }

        public void StepBack()
        {
            if (_selectedCandidate?.Sprite == null || _selectedCandidate.Sprite.Frames.Count == 0) return;

            int[] seq = _playbackMode == FlipperPlaybackMode.Passive
                ? _cachedPassiveSequence
                : _cachedActiveSequence;
            if (seq.Length > 0)
            {
                _sequenceIndex = (_sequenceIndex - 1 + seq.Length) % seq.Length;
            }

            OnPropertyChanged(nameof(SequenceIndex));
            OnPropertyChanged(nameof(FrameCounterText));
            RenderCurrentFrame();
        }

        public void TogglePlayPause()
        {
            IsPlaying = !IsPlaying;
        }

        public void StopPlayback()
        {
            IsPlaying = false;
            _animTimer?.Stop();
        }

        public void StartPlayback()
        {
            IsPlaying = true;
            _animTimer?.Start();
        }

        public void SetSpeedMultiplier(double mult)
        {
            SpeedMultiplier = mult;
        }

        public void HandleDpadPress(string direction)
        {
            LastButtonPressedText = direction switch
            {
                "UP" => "⬆️ D-Pad UP",
                "DOWN" => "⬇️ D-Pad DOWN",
                "LEFT" => "⬅️ D-Pad LEFT",
                "RIGHT" => "➡️ D-Pad RIGHT",
                "OK" => "🔘 OK / Trigger Active",
                "BACK" => "↩️ BACK / Reset Passive",
                _ => direction
            };

            if (direction == "OK")
            {
                TriggerActive();
            }
            else if (direction == "BACK")
            {
                ResetAnimationToPassive();
            }
            else if (direction == "UP")
            {
                // Shift Mood up (happier / lower butthurt)
                Mood = Math.Max(0, Mood - 1);
            }
            else if (direction == "DOWN")
            {
                // Shift Mood down (angrier / higher butthurt)
                Mood = Math.Min(14, Mood + 1);
            }
            else if (direction == "LEFT")
            {
                // Step Frame Back
                StepBack();
            }
            else if (direction == "RIGHT")
            {
                // Step Frame Forward
                AdvanceFrame();
            }
        }

        public void ApplyBubbleToSelected()
        {
            if (_selectedCandidate?.Sprite == null) return;
            var sprite = _selectedCandidate.Sprite;
            if (sprite.FlipperCycle == null)
            {
                sprite.FlipperCycle = new FlipperAnimationCycle
                {
                    PassiveFrameCount = sprite.Frames.Count,
                    ActiveFrameCount = 0,
                    ActiveCycles = 1,
                    ActiveCooldown = 0,
                    FramesOrder = Enumerable.Range(0, sprite.Frames.Count).ToArray()
                };
            }

            string text = _customBubbleText?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(text))
            {
                sprite.FlipperCycle.SpeechBubble = new FlipperSpeechBubble(1, _bubbleX, _bubbleY, text, _bubbleTail);
                sprite.FlipperCycle.BubbleSlots = 1;
            }
            else
            {
                sprite.FlipperCycle.SpeechBubble = null;
                sprite.FlipperCycle.BubbleSlots = 0;
            }

            BubbleSavedFeedbackText = "✅ Saved to sprite metadata!";
            OnPropertyChanged(nameof(CycleDetails));
            DocumentModified?.Invoke(this, EventArgs.Empty);
        }

        public void JumpToState(int level, int mood)
        {
            Level = Math.Clamp(level, 1, 30);
            Mood = Math.Clamp(mood, 0, 14);
        }

        public void SetAnimations(IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> animations, string? packName = null)
        {
            _animations = animations ?? [];
            if (!string.IsNullOrEmpty(packName))
            {
                PackName = packName;
            }
            OnPropertyChanged(nameof(HasAnimations));
            OnPropertyChanged(nameof(ManifestInfo));
            UpdateCandidates();
            RenderCurrentFrame();
        }

        public void SetBubbleAlignment(string? tag)
        {
            switch (tag)
            {
                case "TL":
                    BubbleTail = SpeechBubbleTailPosition.BottomLeft;
                    BubbleX = 4;
                    BubbleY = 4;
                    break;
                case "TR":
                    BubbleTail = SpeechBubbleTailPosition.BottomRight;
                    BubbleX = Math.Max(0, MaxBubbleX - 4);
                    BubbleY = 4;
                    break;
                case "BL":
                    BubbleTail = SpeechBubbleTailPosition.TopLeft;
                    BubbleX = 4;
                    BubbleY = Math.Max(0, MaxBubbleY - 4);
                    break;
                case "BR":
                    BubbleTail = SpeechBubbleTailPosition.TopRight;
                    BubbleX = Math.Max(0, MaxBubbleX - 4);
                    BubbleY = Math.Max(0, MaxBubbleY - 4);
                    break;
            }
        }

        public void UpdateBubblePositionFromPoint(double x, double y, double actualWidth = 384.0, double actualHeight = 192.0)
        {
            double scaleX = actualWidth > 0 ? 128.0 / actualWidth : 1.0;
            double scaleY = actualHeight > 0 ? 64.0 / actualHeight : 1.0;
            BubbleX = (int)Math.Round(x * scaleX);
            BubbleY = (int)Math.Round(y * scaleY);
        }

        private void UpdateTimerInterval()
        {
            if (_animTimer == null || _selectedCandidate?.Sprite == null) return;
            int fps = Math.Max(1, _selectedCandidate.Sprite.FrameRateFps);
            double intervalMs = (1000.0 / fps) / Math.Max(0.1, _speedMultiplier);
            if (double.IsNaN(intervalMs) || double.IsInfinity(intervalMs) || intervalMs <= 0)
            {
                intervalMs = 100.0;
            }
            _animTimer.Interval = TimeSpan.FromMilliseconds(intervalMs);
        }

        public void RenderCurrentFrame()
        {
            if (_screenBitmap == null || _selectedCandidate?.Sprite == null) return;
            var sprite = _selectedCandidate.Sprite;
            var cycle = sprite.FlipperCycle;

            int[] currentSeq = _playbackMode == FlipperPlaybackMode.Passive
                ? _cachedPassiveSequence
                : _cachedActiveSequence;
            int frameIdx = 0;
            if (currentSeq.Length > 0)
            {
                int safeSeq = Math.Clamp(_sequenceIndex, 0, currentSeq.Length - 1);
                frameIdx = currentSeq[safeSeq];
            }
            if (frameIdx < 0 || frameIdx >= sprite.Frames.Count) frameIdx = 0;

            bool[] monoPixels = (sprite.Frames.Count > 0 && currentSeq.Length > 0)
                ? sprite.CompositeFramePixels(frameIdx, isExport: false)
                : [];
            Array.Clear(_screenPixels, 0, _screenPixels.Length);

            int sw = Math.Min(128, sprite.Width);
            int sh = Math.Min(64, sprite.Height);
            int offsetX = (128 - sw) / 2;
            int offsetY = (64 - sh) / 2;

            for (int y = 0; y < sh; y++)
            {
                for (int x = 0; x < sw; x++)
                {
                    int srcIdx = y * sprite.Width + x;
                    if (srcIdx < monoPixels.Length && monoPixels[srcIdx])
                    {
                        int destX = offsetX + x;
                        int destY = offsetY + y;
                        if (destX >= 0 && destX < 128 && destY >= 0 && destY < 64)
                        {
                            _screenPixels[destY * 128 + destX] = true;
                        }
                    }
                }
            }

            // Draw Live Speech Bubble if enabled
            if (_showSpeechBubble)
            {
                var bubble = GetOrUpdateCachedBubble();
                if (bubble != null)
                {
                    bubble.Draw(_screenPixels, 128, 64);
                }
                else if (cycle?.SpeechBubble != null)
                {
                    cycle.SpeechBubble.Draw(_screenPixels, 128, 64);
                }
            }

            // Draw Desktop Mode HUD if enabled
            if (_showDesktopHud)
            {
                FlipperHudRenderer.Draw(_screenPixels, 128, 64, Level, Mood);
            }

            var (bgCol, fgCol, gridDotCol) = GetPaletteColors(_selectedPaletteIndex);

            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    int idx = y * 128 + x;
                    if (_screenPixels[idx])
                    {
                        _pixelBuffer[idx] = fgCol;
                    }
                    else
                    {
                        _pixelBuffer[idx] = (_showLcdGrid && (x % 2 == 1 || y % 2 == 1)) ? gridDotCol : bgCol;
                    }
                }
            }

            try
            {
                _screenBitmap.WritePixels(
                    new Int32Rect(0, 0, 128, 64),
                    _pixelBuffer,
                    128 * sizeof(uint),
                    0);
            }
            catch
            {
                // In headless tests WritePixels might throw if uninitialized
            }
        }

        public void RenderBlankScreen(string message)
        {
            var (bgCol, fgCol, gridDotCol) = GetPaletteColors(_selectedPaletteIndex);
            Array.Clear(_screenPixels, 0, _screenPixels.Length);

            if (!string.IsNullOrWhiteSpace(message))
            {
                FlipperFonts.DrawCenteredString(_screenPixels, 128, 64, -1, message, FlipperFontType.FontSecondary);
            }

            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    int idx = y * 128 + x;
                    if (_screenPixels[idx])
                    {
                        _pixelBuffer[idx] = fgCol;
                    }
                    else
                    {
                        _pixelBuffer[idx] = (_showLcdGrid && (x % 2 == 1 || y % 2 == 1)) ? gridDotCol : bgCol;
                    }
                }
            }

            try
            {
                _screenBitmap.WritePixels(
                    new Int32Rect(0, 0, 128, 64),
                    _pixelBuffer,
                    128 * sizeof(uint),
                    0);
            }
            catch
            {
                // Headless test fallback
            }
        }

        private static (uint Bg, uint Fg, uint GridDot) GetPaletteColors(int paletteIndex)
        {
            var p = FlipperThemeService.GetPalette(paletteIndex);
            return (p.BgColor, p.FgColor, p.GridDotColor);
        }

        public static int[] GetPassiveSequence(SpriteState sprite)
        {
            if (sprite.FlipperCycle != null && sprite.FlipperCycle.FramesOrder != null && sprite.FlipperCycle.FramesOrder.Length > 0)
            {
                int pCount = Math.Clamp(sprite.FlipperCycle.PassiveFrameCount, 0, sprite.FlipperCycle.FramesOrder.Length);
                if (pCount > 0)
                {
                    var seq = new int[pCount];
                    Array.Copy(sprite.FlipperCycle.FramesOrder, 0, seq, 0, pCount);
                    return seq;
                }
                if (sprite.FlipperCycle.PassiveFrameCount == 0 && sprite.FlipperCycle.ActiveFrameCount == 0)
                {
                    return (int[])sprite.FlipperCycle.FramesOrder.Clone();
                }
            }
            return Enumerable.Range(0, sprite.Frames.Count).ToArray();
        }

        public static int[] GetActiveSequence(SpriteState sprite)
        {
            if (sprite.FlipperCycle != null && sprite.FlipperCycle.FramesOrder != null && sprite.FlipperCycle.FramesOrder.Length > 0)
            {
                int pCount = Math.Clamp(sprite.FlipperCycle.PassiveFrameCount, 0, sprite.FlipperCycle.FramesOrder.Length);
                int aCount = Math.Clamp(sprite.FlipperCycle.ActiveFrameCount, 0, sprite.FlipperCycle.FramesOrder.Length - pCount);
                if (aCount > 0)
                {
                    var seq = new int[aCount];
                    Array.Copy(sprite.FlipperCycle.FramesOrder, pCount, seq, 0, aCount);
                    return seq;
                }
            }
            return [];
        }

        public void LoadPackFolder()
        {
            string? folderPath = null;
            if (_dialogService != null)
            {
                folderPath = _dialogService.ShowOpenFolderDialog("Select Flipper Zero Asset Pack or Dolphin Folder");
            }
            else
            {
                var dlg = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Select Flipper Zero Asset Pack or Dolphin Folder"
                };
                if (dlg.ShowDialog() == true)
                {
                    folderPath = dlg.FolderName;
                }
            }

            if (!string.IsNullOrWhiteSpace(folderPath))
            {
                LoadPackFromPath(folderPath);
            }
        }

        public void LoadPackFile(string? filePath = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                string filter = "Supported Packs (*.hexpack;*.zip)|*.hexpack;*.zip|Hexprite Asset Pack (*.hexpack)|*.hexpack|ZIP Archives (*.zip)|*.zip|All Files (*.*)|*.*";
                if (_dialogService != null)
                {
                    filePath = _dialogService.ShowOpenFileDialog(filter, "Open Flipper Asset Pack (.hexpack / .zip)");
                }
                else
                {
                    var openDlg = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Open Flipper Asset Pack (.hexpack / .zip)",
                        Filter = filter
                    };
                    if (openDlg.ShowDialog() == true)
                    {
                        filePath = openDlg.FileName;
                    }
                }
                if (string.IsNullOrWhiteSpace(filePath)) return;
            }

            LoadPackFromPath(filePath);
        }

        public void LoadPackFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                if (File.Exists(path) && path.EndsWith(".hexpack", StringComparison.OrdinalIgnoreCase))
                {
                    string json = File.ReadAllText(path);
                    var doc = System.Text.Json.JsonSerializer.Deserialize<AssetPackDocument>(json);
                    if (doc != null)
                    {
                        var list = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>();
                        foreach (var entry in doc.Entries)
                        {
                            if (doc.Animations.TryGetValue(entry.Name, out var sp) && sp != null)
                            {
                                list.Add((entry.Name, sp, entry.Clone()));
                            }
                            else
                            {
                                var placeholder = new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome };
                                list.Add((entry.Name, placeholder, entry.Clone()));
                            }
                        }

                        _animations = list;
                        CurrentFilePath = path;
                        PackName = !string.IsNullOrWhiteSpace(doc.PackName) ? doc.PackName : Path.GetFileNameWithoutExtension(path);
                        OnPropertyChanged(nameof(HasAnimations));
                        OnPropertyChanged(nameof(ManifestInfo));

                        if (doc.SimulatorSettings != null)
                        {
                            ApplySimulatorSettings(doc.SimulatorSettings);
                        }
                        else
                        {
                            UpdateCandidates();
                            RenderCurrentFrame();
                        }

                        _dialogService?.ShowMessage($"Successfully loaded .hexpack:\n{_animations.Count} animation(s) loaded.", "Load Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                }

                var imported = _importService.ImportAssetPack(path);
                if (imported.Count > 0)
                {
                    _animations = imported;
                    CurrentFilePath = path.EndsWith(".hexpack", StringComparison.OrdinalIgnoreCase) ? path : null;
                    PackName = Path.GetFileNameWithoutExtension(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    OnPropertyChanged(nameof(HasAnimations));
                    OnPropertyChanged(nameof(ManifestInfo));
                    UpdateCandidates();
                    RenderCurrentFrame();
                    _dialogService?.ShowMessage($"Successfully loaded asset pack:\n{_animations.Count} animation(s) loaded.", "Load Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    _dialogService?.ShowMessage("No valid Flipper Zero animations found in selected path.", "Simulator", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                _dialogService?.ShowMessage($"Failed to load animation pack:\n{ex.Message}", "Simulator Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ExportGif()
        {
            if (_selectedCandidate?.Sprite == null || _selectedCandidate.Sprite.Frames.Count == 0)
            {
                _dialogService?.ShowMessage("No active animation available to export.", "Export GIF", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string animName = string.Join("_", (string.IsNullOrWhiteSpace(_selectedCandidate.Name) ? "FlipperAnimation" : _selectedCandidate.Name).Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            if (string.IsNullOrWhiteSpace(animName)) animName = "FlipperAnimation";

            string? targetPath = null;
            string filter = "Animated GIF (*.gif)|*.gif";
            if (_dialogService != null)
            {
                targetPath = _dialogService.ShowSaveFileDialog(filter, "Export Animated GIF", animName + ".gif");
            }
            else
            {
                var saveDlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export Animated GIF",
                    Filter = filter,
                    FileName = $"{animName}.gif"
                };
                if (saveDlg.ShowDialog() == true)
                {
                    targetPath = saveDlg.FileName;
                }
            }

            if (string.IsNullOrWhiteSpace(targetPath)) return;

            try
            {
                var (bgCol, fgCol, _) = GetPaletteColors(_selectedPaletteIndex);
                FlipperSpeechBubble? bubble = null;

                if (_showSpeechBubble)
                {
                    string bubbleText = _customBubbleText?.Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(bubbleText))
                    {
                        bubble = new FlipperSpeechBubble(1, _bubbleX, _bubbleY, bubbleText, _bubbleTail);
                    }
                }

                int fps = _selectedCandidate.Sprite.FrameRateFps > 0 ? _selectedCandidate.Sprite.FrameRateFps : 5;
                var gifService = new GifAnimationExportService();
                gifService.ExportGif(
                    _selectedCandidate.Sprite,
                    targetPath,
                    scale: 3,
                    fgColor: fgCol,
                    bgColor: bgCol,
                    fps: fps,
                    includeSpeechBubble: _showSpeechBubble,
                    bubble: bubble,
                    includeHud: _showDesktopHud,
                    hudLevel: Level,
                    hudMood: Mood,
                    fullCycle: _exportFullCycle);

                _dialogService?.ShowMessage($"Successfully exported animated GIF to:\n{targetPath}", "Export Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _dialogService?.ShowMessage($"Failed to export animated GIF:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void OpenInTabs()
        {
            if (_animations.Count == 0)
            {
                _dialogService?.ShowMessage("No animations in active simulator to open in tabs.", "Editor Tabs", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (_tabService != null)
                {
                    _tabService.OpenSpritesInTabs(_animations.Select(i => (i.Name, i.Sprite)));
                    _dialogService?.ShowMessage($"Loaded {_animations.Count} animation(s) into workspace tabs!", "Editor Tabs", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (Application.Current?.MainWindow?.DataContext is IWorkspaceTabService ws)
                {
                    ws.OpenSpritesInTabs(_animations.Select(i => (i.Name, i.Sprite)));
                    _dialogService?.ShowMessage($"Loaded {_animations.Count} animation(s) into workspace tabs!", "Editor Tabs", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    _dialogService?.ShowMessage("Main workspace is unavailable.", "Workspace Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                _dialogService?.ShowMessage($"Failed to open animations in editor tabs:\n{ex.Message}", "Editor Tabs", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void FlashUsb()
        {
            if (_animations.Count == 0)
            {
                _dialogService?.ShowMessage("No animations in active pack to deploy to Flipper Zero.", "USB Flash", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string? tempDir = null;
            try
            {
                tempDir = Path.Combine(Path.GetTempPath(), "HexpriteSimFlash_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                string animsDir = Path.Combine(tempDir, "Anims");
                Directory.CreateDirectory(animsDir);

                var manifestEntries = new List<FlipperManifestEntry>();

                foreach (var anim in _animations)
                {
                    manifestEntries.Add(anim.ManifestEntry);
                    var settings = new FlipperExportSettings(
                        animsDir,
                        anim.Name,
                        frameRate: anim.Sprite.FrameRateFps,
                        passiveFrames: anim.Sprite.FlipperCycle?.PassiveFrameCount ?? anim.Sprite.Frames.Count,
                        activeFrames: anim.Sprite.FlipperCycle?.ActiveFrameCount ?? 0,
                        minLevel: anim.ManifestEntry.MinLevel,
                        maxLevel: anim.ManifestEntry.MaxLevel,
                        minButthurt: anim.ManifestEntry.MinButthurt,
                        maxButthurt: anim.ManifestEntry.MaxButthurt,
                        weight: anim.ManifestEntry.Weight,
                        createManifestTxt: false
                    );
                    _exportService.ExportAnimation(anim.Sprite, settings);
                }

                string manifestPath = Path.Combine(animsDir, "manifest.txt");
                var manifest = new FlipperManifest { Entries = manifestEntries };
                SafeFileIo.WriteAllTextAtomic(manifestPath, manifest.Serialize(), maxRetries: 3, createBackup: false);

                var fileEntries = new List<(string RelativePath, byte[] Data)>();
                foreach (var file in Directory.GetFiles(tempDir, "*.*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(tempDir, file).Replace('\\', '/');
                    fileEntries.Add((rel, File.ReadAllBytes(file)));
                }

                if (fileEntries.Count > 0)
                {
                    string safeDeployName = string.Join("_", PackName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
                    if (string.IsNullOrWhiteSpace(safeDeployName)) safeDeployName = "AssetPack";

                    var deployer = new FlipperUsbDeployer();
                    var deployWin = new Views.FlipperDeployWindow(deployer, fileEntries, safeDeployName);
                    deployWin.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                _dialogService?.ShowMessage($"Failed to prepare USB deployment:\n{ex.Message}", "USB Flash Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (tempDir != null && Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, recursive: true); } catch { }
                }
            }
        }

        public void OpenScheduleMatrix()
        {
            if (_animations.Count == 0)
            {
                _dialogService?.ShowMessage("No animations in active simulator to display in Matrix View.", "Matrix View", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_windowManager != null)
            {
                _windowManager.ShowScheduleMatrix(_animations, PackName);
            }
            else if (_tabService != null)
            {
                _tabService.OpenAssetPackInTab(_animations, PackName);
            }
        }

        public void OpenLiveMirror()
        {
            if (_windowManager != null)
            {
                _windowManager.ShowScreenMirror();
            }
            else
            {
                var mirrorWin = new Views.FlipperScreenMirrorWindow();
                mirrorWin.Show();
            }
        }

        public void OpenMediaSlicer()
        {
            if (_windowManager != null)
            {
                _windowManager.ShowMediaSlicer();
            }
            else
            {
                var slicerWin = new Views.FlipperMediaSlicerWindow();
                slicerWin.ShowDialog();
            }
        }

        public void JumpToNearestCoveredState()
        {
            if (_animations.Count == 0) return;

            (string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)? closest = null;
            double minDistance = double.MaxValue;

            foreach (var anim in _animations)
            {
                var entry = anim.ManifestEntry;
                int dl = 0;
                if (Level < entry.MinLevel) dl = entry.MinLevel - Level;
                else if (Level > entry.MaxLevel) dl = Level - entry.MaxLevel;

                int dm = 0;
                if (Mood < entry.MinButthurt) dm = entry.MinButthurt - Mood;
                else if (Mood > entry.MaxButthurt) dm = Mood - entry.MaxButthurt;

                double dist = (dl * dl) + (dm * dm);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    closest = anim;
                }
            }

            if (closest.HasValue)
            {
                var entry = closest.Value.ManifestEntry;
                int targetLevel = Math.Clamp(Level, entry.MinLevel, entry.MaxLevel);
                int targetMood = Math.Clamp(Mood, entry.MinButthurt, entry.MaxButthurt);
                JumpToState(targetLevel, targetMood);
            }
        }

        public void PopOutWindow()
        {
            if (_windowManager != null)
            {
                _windowManager.ShowSimulator(_animations, PackName, GetCurrentSimulatorSettings());
            }
            else
            {
                var win = new Views.FlipperMatrixSimulatorWindow(_animations, PackName);
                win.Show();
            }
        }

        public void NavigateToSelectedTab()
        {
            if (_selectedCandidate?.Sprite == null) return;
            if (_tabService != null)
            {
                _tabService.OpenSpriteInTab(_selectedCandidate.Sprite, _selectedCandidate.Name);
            }
            else if (Application.Current?.MainWindow?.DataContext is IWorkspaceTabService ws)
            {
                ws.OpenSpriteInTab(_selectedCandidate.Sprite, _selectedCandidate.Name);
            }
        }

        public void LocateSelectedInMatrix()
        {
            if (_selectedCandidate != null)
            {
                LocateEntryRequested?.Invoke(_selectedCandidate.Name);
            }
        }

        public FlipperSimulatorSettings GetCurrentSimulatorSettings()
        {
            return new FlipperSimulatorSettings
            {
                Level = Level,
                Mood = Mood,
                SelectedPaletteIndex = SelectedPaletteIndex,
                ShowLcdGrid = ShowLcdGrid,
                ShowDesktopHud = ShowDesktopHud,
                ShowSpeechBubble = ShowSpeechBubble,
                BubbleX = BubbleX,
                BubbleY = BubbleY,
                CustomBubbleText = CustomBubbleText,
                BubbleTail = BubbleTail,
                SpeedMultiplier = SpeedMultiplier,
                CandidateModeIndex = CandidateModeIndex,
            };
        }

        public void ApplySimulatorSettings(FlipperSimulatorSettings settings)
        {
            if (settings == null) return;
            _level = Math.Clamp(settings.Level, 1, 30);
            _mood = Math.Clamp(settings.Mood, 0, 14);
            _selectedPaletteIndex = Math.Clamp(settings.SelectedPaletteIndex, 0, FlipperThemeService.Palettes.Count - 1);
            _showLcdGrid = settings.ShowLcdGrid;
            _showDesktopHud = settings.ShowDesktopHud;
            _showSpeechBubble = settings.ShowSpeechBubble;
            _bubbleX = Math.Clamp(settings.BubbleX, 0, 110);
            _bubbleY = Math.Clamp(settings.BubbleY, 0, 52);
            _customBubbleText = settings.CustomBubbleText ?? "Feed me!";
            _bubbleTail = settings.BubbleTail;
            _speedMultiplier = settings.SpeedMultiplier > 0 ? settings.SpeedMultiplier : 1.0;
            _isAllAnimationsMode = settings.CandidateModeIndex == 1;

            OnPropertyChanged(nameof(Level));
            OnPropertyChanged(nameof(LevelDescription));
            OnPropertyChanged(nameof(Mood));
            OnPropertyChanged(nameof(MoodDescription));
            OnPropertyChanged(nameof(SelectedPaletteIndex));
            OnPropertyChanged(nameof(ShowLcdGrid));
            OnPropertyChanged(nameof(ShowDesktopHud));
            OnPropertyChanged(nameof(ShowSpeechBubble));
            OnPropertyChanged(nameof(BubbleX));
            OnPropertyChanged(nameof(BubbleY));
            OnPropertyChanged(nameof(CustomBubbleText));
            OnPropertyChanged(nameof(BubbleTail));
            OnPropertyChanged(nameof(BubbleTailIndex));
            OnPropertyChanged(nameof(SpeedMultiplier));
            OnPropertyChanged(nameof(CandidateModeIndex));
            OnPropertyChanged(nameof(IsAllAnimationsMode));
            OnPropertyChanged(nameof(CandidatesHeader));

            UpdateTimerInterval();
            UpdateCandidates();
            RenderCurrentFrame();
        }

        private static readonly System.Text.Json.JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

        public void SaveAsHexpack(string? targetPath = null)
        {
            if (_animations.Count == 0)
            {
                _dialogService?.ShowMessage("No animations in active simulator to save.", "Save Pack", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                if (!string.IsNullOrWhiteSpace(CurrentFilePath))
                {
                    targetPath = CurrentFilePath;
                }
                else
                {
                    string safePackName = string.Join("_", PackName.Split(System.IO.Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
                    if (string.IsNullOrWhiteSpace(safePackName)) safePackName = "AssetPack";

                    string filter = "Hexprite Asset Pack (*.hexpack)|*.hexpack|All Files (*.*)|*.*";
                    if (_dialogService != null)
                    {
                        targetPath = _dialogService.ShowSaveFileDialog(filter, "Save Asset Pack (.hexpack)", safePackName + ".hexpack");
                    }
                    else
                    {
                        var saveDlg = new Microsoft.Win32.SaveFileDialog
                        {
                            Title = "Save Asset Pack (.hexpack)",
                            Filter = filter,
                            FileName = safePackName + ".hexpack"
                        };
                        if (saveDlg.ShowDialog() == true)
                        {
                            targetPath = saveDlg.FileName;
                        }
                    }
                    if (string.IsNullOrWhiteSpace(targetPath)) return;
                }
            }

            targetPath = SafeFileIo.EnsureExtension(targetPath, ".hexpack");
            try
            {
                var doc = new AssetPackDocument
                {
                    SchemaVersion = 2,
                    PackName = PackName,
                    IsStockMode = _animations.All(a => a.ManifestEntry.MaxLevel <= 3),
                    Entries = _animations.Select(a => a.ManifestEntry.Clone()).ToList(),
                    SimulatorSettings = GetCurrentSimulatorSettings(),
                };

                foreach (var (name, sprite, _) in _animations)
                {
                    if (sprite != null && !string.IsNullOrWhiteSpace(name))
                    {
                        var clone = sprite.Clone();
                        clone.NormalizeLayerState();
                        doc.Animations[name] = clone;
                    }
                }

                string json = System.Text.Json.JsonSerializer.Serialize(doc, IndentedJsonOptions);
                SafeFileIo.WriteAllTextAtomic(targetPath, json, maxRetries: 3, createBackup: true);
                CurrentFilePath = targetPath;
                UserPreferencesService.AddRecentFile(targetPath);
                DocumentModified?.Invoke(this, EventArgs.Empty);
                _dialogService?.ShowMessage($"Successfully saved .hexpack to:\n{targetPath}", "Save Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _dialogService?.ShowMessage($"Failed to save .hexpack:\n{ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void OpenInAssetPackStudio()
        {
            if (_animations.Count == 0)
            {
                _dialogService?.ShowMessage("No animations in active simulator to open in Asset Pack Studio.", "Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (_tabService != null)
                {
                    _tabService.OpenAssetPackInTab(_animations, PackName);
                }
                else if (Application.Current?.MainWindow?.DataContext is IWorkspaceTabService ws)
                {
                    ws.OpenAssetPackInTab(_animations, PackName);
                }
                else
                {
                    OpenInTabs();
                }

                RequestClose?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _dialogService?.ShowMessage($"Failed to open in Asset Pack Studio:\n{ex.Message}", "Studio Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            if (_animTimer != null)
            {
                _animTimer.Stop();
                _animTimer.Tick -= OnAnimTimerTick;
                _animTimer = null;
            }
            Candidates.Clear();
            _animations = Array.Empty<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>();
            GC.SuppressFinalize(this);
        }
    }
}
