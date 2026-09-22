using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Hexprite.ViewModels
{
    /// <summary>
    /// Application-shell ViewModel that owns the tab collection.
    /// Each tab is a <see cref="MainViewModel"/> instance with its own
    /// undo history, selection state, and file path.
    /// </summary>
    public class ShellViewModel : ObservableObject, IWorkspaceTabService
    {
        // ── Services (shared across all documents) ────────────────────────
        private static readonly Serilog.ILogger Logger = Log.ForContext<ShellViewModel>();
        private readonly ICodeGeneratorService _codeGen;
        private readonly IDrawingService _drawingService;
        private readonly IClipboardService _clipboardService;
        private readonly IPixelClipboardService _pixelClipboard;
        private readonly IDialogService _dialogService;
        private readonly IThemeService _themeService;
        private readonly IBugReportService _bugReportService;
        private readonly IUserFeedbackService _userFeedbackService;
        private readonly IControllerFactory _controllerFactory;
        private readonly IExportService _exportService;
        private readonly IFileImportExportService _importExportService;
        private readonly IHardwarePreviewService _hardwarePreview;
        private readonly IUpdateService _updateService;
        private readonly IFlipperWindowManager _flipperWindowManager;
        // Stored as a named field so Detach() can unsubscribe it; an anonymous
        // lambda would pin the shell and all its tabs in memory via the singleton IThemeService.
        private EventHandler? _themeChangedHandler;

        private const string FileFilter = "Hexprite Documents (*.hexp, *.hexfont, *.hexpfont, *.hexpack, *.zip)|*.hexp;*.hexfont;*.hexpfont;*.hexpack;*.zip|Hexprite Sprite (*.hexp)|*.hexp|Hexprite Font (*.hexfont, *.hexpfont)|*.hexfont;*.hexpfont|Hexprite Asset Pack (*.hexpack, *.zip)|*.hexpack;*.zip|Flipper Manifest (manifest.txt)|manifest.txt;*.txt|JSON Files (*.json)|*.json|All Files (*.*)|*.*";

        // Shared options instance — JsonSerializerOptions is expensive to construct
        // (builds internal reflection caches). Microsoft recommends a static singleton.
        private static readonly JsonSerializerOptions IndentedJsonOptions =
            new() { WriteIndented = true };

        public const int MaxTabs = 50;

        // ── Window layout (panel visibility) ──────────────────────────────
        // Important: compute this lazily to avoid static init order issues.
        private static string? _customWindowLayoutSettingsPath;
        public static void SetCustomWindowLayoutPath(string? path) => _customWindowLayoutSettingsPath = path;
        private static string WindowLayoutSettingsFile => _customWindowLayoutSettingsPath ?? Path.Combine(UserSettingsDirectory, "window-layout.json");

        private bool _isLoadingWindowLayout;

        private bool _isImporting;
        public bool IsImporting
        {
            get => _isImporting;
            set => SetProperty(ref _isImporting, value);
        }

        private bool _isToolSidebarVisible = true;
        public bool IsToolSidebarVisible
        {
            get => _isToolSidebarVisible;
            set => SetProperty(ref _isToolSidebarVisible, value);
        }

        private bool _isLayersPanelVisible = true;
        public bool IsLayersPanelVisible
        {
            get => _isLayersPanelVisible;
            set => SetProperty(ref _isLayersPanelVisible, value);
        }

        private bool _isRightSidebarVisible = true;
        public bool IsRightSidebarVisible
        {
            get => _isRightSidebarVisible;
            set => SetProperty(ref _isRightSidebarVisible, value);
        }

        private bool _isTimelineVisible = true;
        public bool IsTimelineVisible
        {
            get => _isTimelineVisible;
            set => SetProperty(ref _isTimelineVisible, value);
        }

        private bool _isStatusBarVisible = true;
        public bool IsStatusBarVisible
        {
            get => _isStatusBarVisible;
            set => SetProperty(ref _isStatusBarVisible, value);
        }

        private double _layersPanelWidth = 232;
        public double LayersPanelWidth
        {
            get => _layersPanelWidth;
            set => SetProperty(ref _layersPanelWidth, value);
        }

        private double _rightSidebarWidth = 300;
        public double RightSidebarWidth
        {
            get => _rightSidebarWidth;
            set => SetProperty(ref _rightSidebarWidth, value);
        }

        // ── Update notification ──────────────────────────────────────────
        private UpdateInfo? _availableUpdate;
        public UpdateInfo? AvailableUpdate
        {
            get => _availableUpdate;
            set => SetProperty(ref _availableUpdate, value);
        }

        private bool _isUpdateBannerVisible;
        public bool IsUpdateBannerVisible
        {
            get => _isUpdateBannerVisible;
            set => SetProperty(ref _isUpdateBannerVisible, value);
        }

        private bool _isCheckingForUpdates;
        public bool IsCheckingForUpdates
        {
            get => _isCheckingForUpdates;
            private set
            {
                if (SetProperty(ref _isCheckingForUpdates, value))
                {
                    (CheckForUpdatesCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                }
            }
        }

        // ── Tab collection ────────────────────────────────────────────────
        public ObservableCollection<IDocumentTab> OpenDocuments { get; } = [];

        private IDocumentTab? _activeDocument;
        public IDocumentTab? ActiveDocument
        {
            get => _activeDocument;
            set
            {
                var old = _activeDocument;
                if (SetProperty(ref _activeDocument, value))
                {
                    if (old is { } prev) prev.IsActive = false;
                    if (value is { } next)
                    {
                        next.IsActive = true;
                        if (next is MainViewModel mvm)
                        {
                            // Defer disk I/O and hash checks so tab switching stays instantaneous on UI thread
                            var dispatcher = System.Windows.Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
                            dispatcher.BeginInvoke(
                                new Action(() => mvm.CheckForExternalChanges()),
                                System.Windows.Threading.DispatcherPriority.Background);

                            if (mvm.IsAnimationEnabled || (mvm.SpriteState?.Frames != null && mvm.SpriteState.Frames.Count > 1) || mvm.SpriteState?.IsAnimationEnabled == true || mvm.SpriteState?.FlipperCycle != null)
                            {
                                mvm.IsAnimationEnabled = true;
                                if (mvm.SpriteState != null) mvm.SpriteState.IsAnimationEnabled = true;
                                IsTimelineVisible = true;
                            }
                        }
                    }
                    RaiseActiveTabChanged();
                }
            }
        }

        public bool HasOpenDocument => ActiveDocument != null;

        /// <summary>
        /// Checks the active document for external file modifications (e.g. on window focus).
        /// </summary>
        public void CheckForExternalChanges()
        {
            if (ActiveDocument is MainViewModel mvm)
            {
                mvm.CheckForExternalChanges();
            }
        }

        // ── Global Tool State (shared across all documents) ─────────────────
        private ToolMode _currentTool = ToolMode.Pencil;
        public ToolMode CurrentTool
        {
            get => _currentTool;
            set
            {
                if (SetProperty(ref _currentTool, value))
                {
                    // Notify all open documents that the tool changed
                    foreach (var doc in OpenDocuments)
                    {
                        if (doc is MainViewModel mvmNotify) mvmNotify.NotifyToolChanged();
                    }
                }
            }
        }

        // ── Commands ──────────────────────────────────────────────────────
        public IRelayCommand NewDocumentCommand { get; }  // accepts optional "WxH" string param
        public IRelayCommand OpenCommand { get; }
        public bool HasAnyLinkedDocuments => OpenDocuments.Any(d => d.IsLinked);
        public IRelayCommand SaveCommand { get; }
        public IRelayCommand SaveAsCommand { get; }
        public IRelayCommand<IDocumentTab> CloseTabCommand { get; }
        public IRelayCommand CloseAllTabsCommand { get; }
        public IRelayCommand<IDocumentTab> CloseOtherTabsCommand { get; }
        public IRelayCommand ResizeCanvasCommand { get; }
        public IRelayCommand OpenDocumentationCommand { get; }
        public IRelayCommand OpenPrivacySettingsCommand { get; }
        public IRelayCommand OpenKeyboardShortcutsCommand { get; }
        public IRelayCommand ShowAboutCommand { get; }
        public IAsyncRelayCommand CheckForUpdatesCommand { get; }
        public IRelayCommand ViewReleaseCommand { get; }
        public IRelayCommand DownloadInstallerCommand { get; }
        public IRelayCommand DismissUpdateCommand { get; }
        public IRelayCommand ReportBugCommand { get; }
        public IRelayCommand SendFeedbackCommand { get; }
        /// <summary>Opens the Import from Code dialog and creates a new tab.</summary>
        public IRelayCommand ImportFromCodeMenuCommand { get; }
        /// <summary>Opens the Import from File dialog.</summary>
        public IRelayCommand ImportFromFileMenuCommand { get; }
        /// <summary>Updates all linked source files across all open tabs.</summary>
        public IRelayCommand UpdateAllLinkedSourcesCommand { get; }
        /// <summary>Pulls changes from all linked source files across all open tabs.</summary>
        public IRelayCommand PullAllLinkedSourcesCommand { get; }
        /// <summary>Restores all linked source files across all open tabs.</summary>
        public IRelayCommand RestoreAllLinkedSourcesCommand { get; }
        /// <summary>Imports a bitmap image into a new canvas tab.</summary>
        public IRelayCommand ImportBitmapMenuCommand { get; }
        /// <summary>Opens the Sprite Sheet Slicer Studio window.</summary>
        public IRelayCommand ImportSpriteSheetMenuCommand { get; }
        public IRelayCommand OpenSpriteSheetSlicerCommand { get; }
        /// <summary>Copies the active document's exported code to the clipboard.</summary>
        public IRelayCommand CopyExportCodeMenuCommand { get; }
        public IRelayCommand ExportAsMenuCommand { get; }
        public IRelayCommand ExportFlipperMenuCommand { get; }
        public IRelayCommand ImportFlipperMenuCommand { get; }
        public IRelayCommand DeployFlipperUsbCommand { get; }
        public IRelayCommand OpenFlipperMatrixSimulatorCommand { get; }
        public IRelayCommand OpenFlipperScreenMirrorCommand { get; }
        public IRelayCommand OpenDisplaySimulationCommand { get; }
        public IRelayCommand OpenCodeViewerCommand { get; }
        public IRelayCommand NewAssetPackCommand { get; }
        public IRelayCommand OpenFlipperScheduleMatrixCommand { get; }
        public IRelayCommand OpenFlipperMediaSlicerCommand { get; }
        public IRelayCommand<string> ApplyFlipperTemplateCommand { get; }
        public IRelayCommand ExportXbmMenuCommand { get; }
        public IRelayCommand ImportXbmMenuCommand { get; }
        
        // Welcome Screen Recent Files
        public System.Collections.ObjectModel.ObservableCollection<RecentFileItem> RecentFiles { get; } = [];
        public System.Collections.ObjectModel.ObservableCollection<RecentFileItem> WelcomeRecentFiles { get; } = [];
        public bool HasRecentFiles => RecentFiles.Count > 0;
        public IRelayCommand<string> OpenRecentFileCommand { get; }
        public IRelayCommand<string> RemoveRecentFileCommand { get; }
        public IRelayCommand<string> OpenContainingFolderCommand { get; }
        public IRelayCommand<string> CopyRecentFilePathCommand { get; }
        public IRelayCommand ClearRecentFilesCommand { get; }
        public IRelayCommand RefreshRecentFilesCommand { get; }
        
        // Font Commands
        /// <summary>Switches the theme between Dark and Light.</summary>
        public IRelayCommand<string> SwitchThemeCommand { get; }
        public IRelayCommand RefreshThemeCommand { get; }
        /// <summary>Selects the current tool (global across all documents).</summary>
        public IRelayCommand<string> SelectToolCommand { get; }

        // ── Events ────────────────────────────────────────────────────────
        /// <summary>Raised when a tab is added so the View can wire events.</summary>
        public event EventHandler<IDocumentTab>? TabAdded;
        /// <summary>Raised when a tab is about to be removed.</summary>
        public event EventHandler<IDocumentTab>? TabClosed;
        public event EventHandler? ActiveTabChanged;
        public event EventHandler? ThemeChanged;

        /// <summary>
        /// Gets whether the current theme is Dark. Used for menu radio-button binding.
        /// </summary>
        public bool IsDarkTheme => _themeService.CurrentTheme == "Dark";

        /// <summary>
        /// Gets whether the current theme is Light. Used for menu radio-button binding.
        /// </summary>
        public bool IsLightTheme => _themeService.CurrentTheme == "Light";

        /// <summary>
        /// Gets whether the current theme is Dim. Used for menu radio-button binding.
        /// </summary>
        public bool IsDimTheme => _themeService.CurrentTheme == "Dim";

        /// <summary>
        /// Gets whether the current theme is Flipper. Used for menu radio-button binding.
        /// </summary>
        public bool IsFlipperTheme => _themeService.CurrentTheme == "Flipper";

        /// <summary>
        /// Gets the current theme name for theme-aware asset binding.
        /// </summary>
        public string CurrentThemeName => _themeService.CurrentTheme;

        private readonly IServiceProvider _serviceProvider;

        public ShellViewModel(
            ICodeGeneratorService codeGen,
            IDrawingService drawingService,
            IClipboardService clipboardService,
            IPixelClipboardService pixelClipboard,
            IDialogService dialogService,
            IThemeService themeService,
            IBugReportService bugReportService,
            IUserFeedbackService userFeedbackService,
            IControllerFactory controllerFactory,
            IExportService exportService,
            IFileImportExportService importExportService,
            IHardwarePreviewService hardwarePreview,
            IServiceProvider serviceProvider,
            IUpdateService? updateService = null,
            IFlipperWindowManager? flipperWindowManager = null)
        {
            _codeGen = codeGen;
            _drawingService = drawingService;
            _clipboardService = clipboardService;
            _pixelClipboard = pixelClipboard;
            _serviceProvider = serviceProvider;
            _dialogService = dialogService;
            _themeService = themeService;
            _bugReportService = bugReportService;
            _userFeedbackService = userFeedbackService;
            _controllerFactory = controllerFactory;
            _exportService = exportService;
            _importExportService = importExportService;
            _hardwarePreview = hardwarePreview;
            _updateService = updateService ?? serviceProvider.GetService<IUpdateService>() ?? new UpdateService();
            _flipperWindowManager = flipperWindowManager
                ?? serviceProvider.GetService<IFlipperWindowManager>()
                ?? new FlipperWindowManager(serviceProvider);

            OpenDocuments.CollectionChanged += (s, e) =>
            {
                if (e.OldItems != null)
                {
                    foreach (IDocumentTab doc in e.OldItems)
                        doc.PropertyChanged -= OnDocumentPropertyChanged;
                }
                if (e.NewItems != null)
                {
                    foreach (IDocumentTab doc in e.NewItems)
                        doc.PropertyChanged += OnDocumentPropertyChanged;
                }
                (UpdateAllLinkedSourcesCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (PullAllLinkedSourcesCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (RestoreAllLinkedSourcesCommand as RelayCommand)?.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(HasAnyLinkedDocuments));
            };

            LoadWindowLayoutSettings();
            PropertyChanged += OnShellPropertyChanged;

            // Menu Commands
            NewDocumentCommand = new RelayCommand<string>(ExecuteNewDocument);
            OpenCommand = new RelayCommand(ExecuteOpen);
            SaveCommand = new RelayCommand(ExecuteSave, () => HasOpenDocument);
            SaveAsCommand = new RelayCommand(ExecuteSaveAs, () => HasOpenDocument);
            CloseTabCommand = new RelayCommand<IDocumentTab>(ExecuteCloseTab, _ => HasOpenDocument);
            CloseAllTabsCommand = new RelayCommand(ExecuteCloseAllTabs, () => HasOpenDocument);
            CloseOtherTabsCommand = new RelayCommand<IDocumentTab>(ExecuteCloseOtherTabs, _ => OpenDocuments.Count > 1);
            ResizeCanvasCommand = new RelayCommand(ExecuteResizeCanvas, () => HasOpenDocument);
            OpenDocumentationCommand = new RelayCommand(ExecuteOpenDocumentation);
            OpenPrivacySettingsCommand = new RelayCommand(ExecuteOpenPrivacySettings);
            OpenKeyboardShortcutsCommand = new RelayCommand(ExecuteOpenKeyboardShortcuts);
            ShowAboutCommand = new RelayCommand(ExecuteShowAbout);
            CheckForUpdatesCommand = new AsyncRelayCommand(ExecuteCheckForUpdatesAsync, () => !IsCheckingForUpdates);
            ViewReleaseCommand = new RelayCommand(ExecuteViewRelease, () => AvailableUpdate != null);
            DownloadInstallerCommand = new RelayCommand(ExecuteDownloadInstaller, () => AvailableUpdate != null);
            DismissUpdateCommand = new RelayCommand(ExecuteDismissUpdate);
            ReportBugCommand = new RelayCommand(ExecuteReportBug);
            SendFeedbackCommand = new RelayCommand(ExecuteSendFeedback);
            ImportFromCodeMenuCommand = new RelayCommand(ExecuteImportFromCode);
            ImportFromFileMenuCommand = new RelayCommand(() => ImportFromFile(initialFilePath: null));
            UpdateAllLinkedSourcesCommand = new RelayCommand(
                async () => await ExecuteUpdateAllLinkedSourcesAsync(),
                () => OpenDocuments.Any(d => d.IsLinked));
            PullAllLinkedSourcesCommand = new RelayCommand(
                async () => await ExecutePullAllLinkedSourcesAsync(),
                () => OpenDocuments.Any(d => d.IsLinked));
            RestoreAllLinkedSourcesCommand = new RelayCommand(
                async () => await ExecuteRestoreAllLinkedSourcesAsync(),
                () => OpenDocuments.Any(d => d.IsLinked));
            ImportBitmapMenuCommand = new RelayCommand(ExecuteImportBitmap);
            ImportSpriteSheetMenuCommand = new RelayCommand(ExecuteImportSpriteSheet);
            OpenSpriteSheetSlicerCommand = new RelayCommand(ExecuteOpenSpriteSheetSlicer);
            CopyExportCodeMenuCommand = new RelayCommand(
                () => { if (ActiveDocument is MainViewModel mvmExport && mvmExport.CopyExportedCodeCommand.CanExecute(parameter: null)) mvmExport.CopyExportedCodeCommand.Execute(parameter: null); },
                () => HasOpenDocument);
            ExportAsMenuCommand = new RelayCommand(
                () => { if (ActiveDocument is MainViewModel mvmExport && mvmExport.ExportImageCommand.CanExecute(parameter: null)) mvmExport.ExportImageCommand.Execute(parameter: null); },
                () => ActiveDocument is MainViewModel);

            ExportFlipperMenuCommand = new RelayCommand(() =>
            {
                if (ActiveDocument is MainViewModel mvm)
                {
                    bool isAnimation = mvm.IsAnimationEnabled && mvm.SpriteState.Frames.Count > 1;

                    if (isAnimation)
                    {
                        string animName = string.IsNullOrWhiteSpace(mvm.ExportSettings?.SpriteName)
                            ? (string.IsNullOrWhiteSpace(mvm.Title) ? "Animation" : mvm.Title)
                            : mvm.ExportSettings.SpriteName;

                        var settings = new Services.FlipperExportSettings
                        {
                            AnimationName = animName,
                            FrameRate = mvm.SpriteState.FrameRateFps > 0 ? mvm.SpriteState.FrameRateFps : 5,
                            PassiveFrames = mvm.SpriteState.FlipperCycle?.PassiveFrameCount ?? 0,
                            ActiveFrames = mvm.SpriteState.Frames.Count,
                            MinLevel = 1,
                            MaxLevel = 30,
                            MinButthurt = 0,
                            MaxButthurt = 14,
                            Weight = 1,
                            CreateManifestTxt = true,
                            TargetMode = Services.FlipperExportTargetMode.MomentumAssetPack,
                        };

                        _flipperWindowManager.ShowExportDialog(mvm.SpriteState, settings);
                    }
                    else
                    {
                        var exportService = _serviceProvider.GetService<Services.IFlipperExportService>() ?? new Services.FlipperExportService();
                        var dlg = new Microsoft.Win32.SaveFileDialog
                        {
                            Title = "Export Flipper Zero Bitmap",
                            Filter = "Flipper Bitmap (*.bm)|*.bm",
                            DefaultExt = ".bm",
                            FileName = "image.bm",
                        };

                        if (dlg.ShowDialog() == true)
                        {
                            try
                            {
                                exportService.ExportImage(mvm.SpriteState, mvm.SpriteState.ActiveFrameIndex, dlg.FileName);
                                _dialogService.ShowMessage("Successfully exported to Flipper Zero bitmap!", "Export Success", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                            }
                            catch (System.Exception ex)
                            {
                                _dialogService.ShowMessage($"Failed to export image:\n{ex.Message}", "Export Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                            }
                        }
                    }
                }
                else if (ActiveDocument is AssetPackViewModel apvm)
                {
                    apvm.MatrixViewModel.ExportAssetPackFolder();
                }
            }, () => HasOpenDocument && (ActiveDocument is MainViewModel || ActiveDocument is AssetPackViewModel));

            ImportFlipperMenuCommand = new RelayCommand(ExecuteImportFlipper);

            DeployFlipperUsbCommand = new RelayCommand(() =>
            {
                if (ActiveDocument is MainViewModel mvm)
                {
                    _flipperWindowManager.ShowDeploy(mvm.SpriteState);
                }
                else if (ActiveDocument is AssetPackViewModel apvm)
                {
                    var animList = apvm.MatrixViewModel.BuildCurrentAnimationList();
                    _flipperWindowManager.ShowDeploy(animList, apvm.MatrixViewModel.PackName);
                }
            }, () => HasOpenDocument && (ActiveDocument is MainViewModel || ActiveDocument is AssetPackViewModel));

            OpenFlipperMatrixSimulatorCommand = new RelayCommand(() =>
            {
                if (ActiveDocument is AssetPackViewModel apvm)
                {
                    var animList = apvm.MatrixViewModel.BuildCurrentAnimationList();
                    var settings = apvm.MatrixViewModel.GetCurrentSimulatorSettings();
                    _flipperWindowManager.ShowSimulator(
                        animList,
                        apvm.MatrixViewModel.PackName,
                        settings,
                        updatedSettings => apvm.MatrixViewModel.ApplySimulatorSettings(updatedSettings));
                }
                else if (ActiveDocument is MainViewModel mvm)
                {
                    _flipperWindowManager.ShowSimulator(mvm.SpriteState);
                }
                else
                {
                    var openPack = OpenDocuments.OfType<AssetPackViewModel>().FirstOrDefault();
                    if (openPack != null)
                    {
                        var animList = openPack.MatrixViewModel.BuildCurrentAnimationList();
                        var settings = openPack.MatrixViewModel.GetCurrentSimulatorSettings();
                        _flipperWindowManager.ShowSimulator(
                            animList,
                            openPack.MatrixViewModel.PackName,
                            settings,
                            updatedSettings => openPack.MatrixViewModel.ApplySimulatorSettings(updatedSettings));
                    }
                    else
                    {
                        var openCanvas = OpenDocuments.OfType<MainViewModel>().FirstOrDefault();
                        _flipperWindowManager.ShowSimulator(openCanvas?.SpriteState);
                    }
                }
            });


            OpenFlipperScreenMirrorCommand = new RelayCommand(() =>
            {
                _flipperWindowManager.ShowScreenMirror();
            });

            OpenDisplaySimulationCommand = new RelayCommand(() =>
            {
                if (ActiveDocument is MainViewModel mvm)
                {
                    Views.DisplaySimulationWindow.ShowOrActivate(mvm, System.Windows.Application.Current?.MainWindow);
                }
            }, () => HasOpenDocument && ActiveDocument is MainViewModel);

            OpenCodeViewerCommand = new RelayCommand(() =>
            {
                if (ActiveDocument is MainViewModel mvm)
                {
                    var win = new Views.CodeOutputWindow(mvm)
                    {
                        Owner = System.Windows.Application.Current?.MainWindow,
                    };
                    win.Show();
                }
            }, () => HasOpenDocument && ActiveDocument is MainViewModel);

            NewAssetPackCommand = new RelayCommand(ExecuteNewAssetPack);

            OpenFlipperScheduleMatrixCommand = new RelayCommand(() =>
            {
                _flipperWindowManager.ShowScheduleMatrix();
            });

            OpenFlipperMediaSlicerCommand = new RelayCommand(() =>
            {
                var sprite = (ActiveDocument as MainViewModel)?.SpriteState;
                _flipperWindowManager.ShowMediaSlicer(sprite);
            });

            ApplyFlipperTemplateCommand = new RelayCommand<string>(templateName =>
            {
                if (ActiveDocument is MainViewModel mvm && !string.IsNullOrEmpty(templateName))
                {
                    if (Enum.TryParse<Services.FlipperUiTemplateType>(templateName, ignoreCase: true, out var tType))
                    {
                        mvm.SaveStateForUndo();
                        string title = string.IsNullOrWhiteSpace(mvm.ExportSettings?.SpriteName) ? "Hexprite" : mvm.ExportSettings.SpriteName;
                        Services.FlipperUiTemplateService.ApplyTemplate(mvm.SpriteState, tType, title);
                        mvm.RedrawGridFromMemory();
                        mvm.MarkCodeStale();
                    }
                }
            }, _ => HasOpenDocument && ActiveDocument is MainViewModel);

            ExportXbmMenuCommand = new RelayCommand(() =>
            {
                if (ActiveDocument is MainViewModel mvm)
                {
                    var xbmService = _serviceProvider.GetService<Services.IXbmService>() ?? new Services.XbmService();

                    var dlg = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = "Export XBM Bitmap",
                        Filter = "XBM Bitmap (*.xbm)|*.xbm",
                        DefaultExt = ".xbm",
                        FileName = "image.xbm",
                    };

                    if (dlg.ShowDialog() == true)
                    {
                        try
                        {
                            xbmService.ExportImage(mvm.SpriteState, mvm.SpriteState.ActiveFrameIndex, dlg.FileName);
                            _dialogService.ShowMessage("Successfully exported to XBM bitmap!", "Export Success", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                        }
                        catch (System.Exception ex)
                        {
                            _dialogService.ShowMessage($"Failed to export image:\n{ex.Message}", "Export Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                        }
                    }
                }
            }, () => HasOpenDocument && ActiveDocument is MainViewModel);

            ImportXbmMenuCommand = new RelayCommand(ExecuteImportXbm);

            OpenRecentFileCommand = new RelayCommand<string>(path =>
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    if (File.Exists(path) || Directory.Exists(path))
                    {
                        OpenFile(path);
                    }
                    else
                    {
                        _dialogService.ShowMessage($"The file '{Path.GetFileName(path)}' could not be found on disk.");
                        UserPreferencesService.RemoveRecentFile(path);
                        RefreshRecentFiles();
                    }
                }
            });

            RemoveRecentFileCommand = new RelayCommand<string>(path =>
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    UserPreferencesService.RemoveRecentFile(path);
                    RefreshRecentFiles();
                }
            });

            OpenContainingFolderCommand = new RelayCommand<string>(path =>
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                try
                {
                    string explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                    if (File.Exists(path))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(explorerPath, $"/select,\"{path}\"") { UseShellExecute = true });
                    }
                    else if (Directory.Exists(path))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(explorerPath, $"\"{path}\"") { UseShellExecute = true });
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Failed to open containing folder for {Path}", path);
                }
            });

            CopyRecentFilePathCommand = new RelayCommand<string>(path =>
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                try
                {
                    System.Windows.Clipboard.SetText(path);
                }
                catch { }
            });

            ClearRecentFilesCommand = new RelayCommand(() =>
            {
                UserPreferencesService.ClearRecentFiles();
                RefreshRecentFiles();
            });

            RefreshRecentFilesCommand = new RelayCommand(RefreshRecentFiles);
            RefreshRecentFiles();

            SwitchThemeCommand = new RelayCommand<string>(ExecuteSwitchTheme);
            RefreshThemeCommand = new RelayCommand(ExecuteRefreshTheme);
            SelectToolCommand = new RelayCommand<string>(ExecuteSelectTool);

            // Bug 1: Store the delegate so Detach() can unsubscribe it. An anonymous lambda
            // here would prevent the shell — and all its OpenDocuments — from being GC'd
            // because the singleton IThemeService would hold a live root forever.
            _themeChangedHandler = (_, _) =>
            {
                OnPropertyChanged(nameof(IsDarkTheme));
                OnPropertyChanged(nameof(IsLightTheme));
                OnPropertyChanged(nameof(IsDimTheme));
                OnPropertyChanged(nameof(IsFlipperTheme));
                OnPropertyChanged(nameof(CurrentThemeName));

                // Re-read pixel colors from the new theme and redraw all open canvases
                foreach (var doc in OpenDocuments)
                {
                    if (doc is MainViewModel mvm) mvm.RefreshCanvasColors();
                }

                ThemeChanged?.Invoke(this, EventArgs.Empty);
            };
            _themeService.ThemeChanged += _themeChangedHandler;

            // No auto-created document — the welcome screen is shown instead
        }

        public void CheckAutosaves()
        {
            var autosaveService = _serviceProvider.GetRequiredService<IAutosaveService>();
            var available = System.Linq.Enumerable.ToList(autosaveService.GetAvailableAutosaves());
            if (available.Count > 0)
            {
                var recoveredEnvelopes = new List<AutosaveEnvelope>();
                foreach (var path in available)
                {
                    try
                    {
                        var envelope = autosaveService.LoadAutosaveEnvelope(path);
                        if (envelope == null)
                        {
                            var legacyState = autosaveService.LoadAutosave(path);
                            if (legacyState != null)
                            {
                                envelope = new AutosaveEnvelope
                                {
                                    Mode = DocumentMode.Sprite,
                                    SpriteState = legacyState,
                                    Title = legacyState.ExportSettings?.SpriteName ?? "Recovered Sprite",
                                };
                            }
                        }
                        if (envelope != null)
                        {
                            recoveredEnvelopes.Add(envelope);
                        }
                    }
                    catch (Exception ex)
                    {
                        HandledErrorReporter.Warning(ex, "ShellViewModel.CheckAutosaves.EnvelopeLoadFailed", new { path });
                    }
                }

                if (recoveredEnvelopes.Count == 0)
                {
                    autosaveService.ClearAllAutosaves();
                    return;
                }

                var docSummary = string.Join('\n', recoveredEnvelopes.Select(e =>
                {
                    string cleanTitle = !string.IsNullOrWhiteSpace(e.Title) ? e.Title.Trim().TrimStart('*').Trim() : "Untitled";
                    string type = e.Mode switch
                    {
                        DocumentMode.AssetPack => "Asset Pack",
                        DocumentMode.Font => "Font",
                        _ => !string.IsNullOrEmpty(e.ParentPackName) ? $"Animation, from {e.ParentPackName}" : "Sprite",
                    };
                    return $"• {cleanTitle} ({type})";
                }));

                string promptMessage = recoveredEnvelopes.Count == 1
                    ? $"Hexprite closed unexpectedly. The following unsaved progress was found:\n\n{docSummary}\n\nWould you like to recover your unsaved progress?"
                    : $"Hexprite closed unexpectedly. The following unsaved documents were found:\n\n{docSummary}\n\nWould you like to recover your unsaved progress?";

                if (_dialogService.ShowConfirmation(promptMessage, "Recover Autosave"))
                {
                    // Delete the recovered files from disk immediately so they cannot trigger another prompt on next launch
                    foreach (var path in available)
                    {
                        try { if (File.Exists(path)) File.Delete(path); } catch { }
                        try { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); } catch { }
                    }
                    autosaveService.ClearAllAutosaves();

                    // Order envelopes: AssetPacks first, then Fonts, then Sprites
                    var assetPackEnvelopes = recoveredEnvelopes.Where(e => e.Mode == DocumentMode.AssetPack).ToList();
                    var fontEnvelopes = recoveredEnvelopes.Where(e => e.Mode == DocumentMode.Font).ToList();
                    var spriteEnvelopes = recoveredEnvelopes.Where(e => e.Mode == DocumentMode.Sprite).ToList();

                    IDocumentTab? lastRestored = null;
                    IDocumentTab? activeRestored = null;
                    var restoredAssetPacks = new List<AssetPackViewModel>();

                    // 1. Restore AssetPack tabs
                    foreach (var env in assetPackEnvelopes)
                    {
                        try
                        {
                            if (env.AssetPackDocument != null)
                            {
                                var apvm = CreateAssetPackViewModel(env.AssetPackDocument);
                                if (!string.IsNullOrEmpty(env.FilePath))
                                {
                                    apvm.FilePath = env.FilePath;
                                }
                                apvm.IsDirty = true;
                                OpenDocuments.Add(apvm);
                                restoredAssetPacks.Add(apvm);
                                lastRestored = apvm;
                                if (env.IsActiveTab) activeRestored = apvm;
                                TabAdded?.Invoke(this, apvm);
                                Logger.Information("Recovered Asset Pack document: {Title}", apvm.Title);
                            }
                        }
                        catch (Exception ex)
                        {
                            HandledErrorReporter.Warning(ex, "ShellViewModel.CheckAutosaves.AssetPackRecoveryFailed", new { env.Title });
                        }
                    }

                    // 2. Restore Font tabs
                    foreach (var env in fontEnvelopes)
                    {
                        try
                        {
                            if (env.FontDocument != null)
                            {
                                var fvm = CreateFontViewModel(env.FontDocument);
                                if (!string.IsNullOrEmpty(env.FilePath))
                                {
                                    fvm.FilePath = env.FilePath;
                                }
                                fvm.IsDirty = true;
                                OpenDocuments.Add(fvm);
                                lastRestored = fvm;
                                if (env.IsActiveTab) activeRestored = fvm;
                                TabAdded?.Invoke(this, fvm);
                                Logger.Information("Recovered Font document: {Title}", fvm.Title);
                            }
                        }
                        catch (Exception ex)
                        {
                            HandledErrorReporter.Warning(ex, "ShellViewModel.CheckAutosaves.FontRecoveryFailed", new { env.Title });
                        }
                    }

                    // 3. Restore Sprite / Animation tabs
                    foreach (var env in spriteEnvelopes)
                    {
                        try
                        {
                            var state = env.SpriteState;
                            if (state != null)
                            {
                                if (state.Width <= 0 || state.Height <= 0 || state.Width > 512 || state.Height > 512)
                                {
                                    Logger.Warning("Skipping corrupted autosave with invalid dimensions for {Title}", env.Title);
                                    continue;
                                }

                                var doc = CreateDocument(state.Width, state.Height, state.ColorMode, redrawImmediately: false);
                                doc.LoadState(state);

                                if (!string.IsNullOrEmpty(env.FilePath))
                                {
                                    doc.FilePath = env.FilePath;
                                }
                                doc.ParentPackPath = env.ParentPackPath;
                                doc.ParentPackName = env.ParentPackName;
                                doc.PackEntryName = env.PackEntryName;

                                if (!string.IsNullOrWhiteSpace(env.Title))
                                {
                                    string cleanTitle = env.Title.Trim().TrimStart('*').Trim();
                                    doc.SetSpriteNameWithoutGenerating(Services.CodeGeneratorService.SanitiseName(cleanTitle));
                                }

                                doc.IsDirty = true;
                                doc.RedrawGridFromMemory();

                                // If this sprite belongs to a saved .hexpack on disk that isn't currently open, reopen the parent pack tab
                                if (!string.IsNullOrEmpty(doc.ParentPackPath) && File.Exists(doc.ParentPackPath))
                                {
                                    bool alreadyOpen = OpenDocuments.OfType<AssetPackViewModel>().Any(ap =>
                                        string.Equals(ap.FilePath, doc.ParentPackPath, StringComparison.OrdinalIgnoreCase));
                                    if (!alreadyOpen)
                                    {
                                        try
                                        {
                                            OpenHexpack(doc.ParentPackPath);
                                            var parentPack = OpenDocuments.OfType<AssetPackViewModel>().FirstOrDefault(ap =>
                                                string.Equals(ap.FilePath, doc.ParentPackPath, StringComparison.OrdinalIgnoreCase));
                                            if (parentPack != null)
                                            {
                                                restoredAssetPacks.Add(parentPack);
                                            }
                                        }
                                        catch { }
                                    }
                                }

                                OpenDocuments.Add(doc);
                                lastRestored = doc;
                                if (env.IsActiveTab) activeRestored = doc;
                                TabAdded?.Invoke(this, doc);
                                Logger.Information("Recovered Sprite document: {Title}", doc.Title);
                            }
                        }
                        catch (Exception ex)
                        {
                            HandledErrorReporter.Warning(ex, "ShellViewModel.CheckAutosaves.SpriteRecoveryFailed", new { env.Title });
                        }
                    }

                    // 4. Sync restored sprites into open AssetPack tabs
                    foreach (var apvm in OpenDocuments.OfType<AssetPackViewModel>())
                    {
                        try
                        {
                            apvm.MatrixViewModel.SyncFromWorkspace(silent: true);
                        }
                        catch { }
                    }

                    ActiveDocument = activeRestored ?? lastRestored;
                    autosaveService.ClearAllAutosaves();
                }
                else
                {
                    // If user declines, safely archive instead of deleting
                    autosaveService.ArchiveAllAutosaves("Declined");
                }
            }
        }

        /// <summary>
        /// Unsubscribes all external event handlers so the shell can be GC'd
        /// (important for test isolation and future multi-window scenarios).
        /// </summary>
        public void Detach()
        {
            // Bug 1: Unsubscribe the theme handler so the singleton IThemeService no
            // longer holds a reference chain into ShellViewModel and its tab graph.
            if (_themeChangedHandler != null)
            {
                _themeService.ThemeChanged -= _themeChangedHandler;
                _themeChangedHandler = null;
            }
            // Bug 2: Remove the self-subscription added in the constructor.
            PropertyChanged -= OnShellPropertyChanged;
        }

        private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IDocumentTab.IsLinked))
            {
                (UpdateAllLinkedSourcesCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (PullAllLinkedSourcesCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (RestoreAllLinkedSourcesCommand as RelayCommand)?.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(HasAnyLinkedDocuments));
            }
        }

        private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isLoadingWindowLayout) return;

            if (e.PropertyName == nameof(IsToolSidebarVisible) ||
                e.PropertyName == nameof(IsLayersPanelVisible) ||
                e.PropertyName == nameof(IsRightSidebarVisible) ||
                e.PropertyName == nameof(IsTimelineVisible) ||
                e.PropertyName == nameof(IsStatusBarVisible) ||
                e.PropertyName == nameof(LayersPanelWidth) ||
                e.PropertyName == nameof(RightSidebarWidth))
            {
                SaveWindowLayoutSettings();
            }
        }

        private sealed class WindowLayoutSettings
        {
            public bool IsToolSidebarVisible { get; set; } = true;
            public bool IsLayersPanelVisible { get; set; } = true;
            public bool IsRightSidebarVisible { get; set; } = true;
            public bool IsTimelineVisible { get; set; } = true;
            public bool IsStatusBarVisible { get; set; } = true;
            public double LayersPanelWidth { get; set; } = 232;
            public double RightSidebarWidth { get; set; } = 300;
        }

        // GridLength (which these widths ultimately feed) throws for negative, NaN, or
        // infinite values, so a corrupted/hand-edited settings file must never pass one through.
        private static double ReadPositiveFiniteDouble(JsonElement root, string propertyName, double fallback)
        {
            if (!root.TryGetProperty(propertyName, out var element) || !element.TryGetDouble(out double value))
                return fallback;

            return double.IsFinite(value) && value >= 0 ? value : fallback;
        }

        private void LoadWindowLayoutSettings()
        {
            _isLoadingWindowLayout = true;
            try
            {
                if (!File.Exists(WindowLayoutSettingsFile))
                    return;

                string json = File.ReadAllText(WindowLayoutSettingsFile);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                IsToolSidebarVisible = !root.TryGetProperty(nameof(IsToolSidebarVisible), out var e1) || e1.GetBoolean();
                IsLayersPanelVisible = !root.TryGetProperty(nameof(IsLayersPanelVisible), out var e2) || e2.GetBoolean();
                IsRightSidebarVisible = !root.TryGetProperty(nameof(IsRightSidebarVisible), out var e3) || e3.GetBoolean();
                IsTimelineVisible = !root.TryGetProperty(nameof(IsTimelineVisible), out var e4) || e4.GetBoolean();
                IsStatusBarVisible = !root.TryGetProperty(nameof(IsStatusBarVisible), out var e5) || e5.GetBoolean();
                double rawLayersWidth = ReadPositiveFiniteDouble(root, nameof(LayersPanelWidth), 232);
                LayersPanelWidth = Math.Max(150, Math.Min(rawLayersWidth, 450));

                double rawRightWidth = ReadPositiveFiniteDouble(root, nameof(RightSidebarWidth), 300);
                RightSidebarWidth = Math.Max(240, Math.Min(rawRightWidth, 460));
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Warning(ex, "ShellViewModel.LoadWindowLayoutSettings", new { WindowLayoutSettingsFile });
            }
            finally
            {
                _isLoadingWindowLayout = false;
            }
        }

        private void SaveWindowLayoutSettings()
        {
            try
            {
                string targetFile = WindowLayoutSettingsFile;
                string? dir = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var payload = new WindowLayoutSettings
                {
                    IsToolSidebarVisible = IsToolSidebarVisible,
                    IsLayersPanelVisible = IsLayersPanelVisible,
                    IsRightSidebarVisible = IsRightSidebarVisible,
                    IsTimelineVisible = IsTimelineVisible,
                    IsStatusBarVisible = IsStatusBarVisible,
                    LayersPanelWidth = LayersPanelWidth,
                    RightSidebarWidth = RightSidebarWidth,
                };
                string json = JsonSerializer.Serialize(payload, IndentedJsonOptions);

                // Write to a temp file and swap it in with File.Move so a crash or a
                // file lock mid-write can never leave window-layout.json truncated/corrupt.
                string tempFile = targetFile + ".tmp";
                File.WriteAllText(tempFile, json);
                File.Move(tempFile, targetFile, overwrite: true);
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Warning(ex, "ShellViewModel.SaveWindowLayoutSettings", new { WindowLayoutSettingsFile });

                // Logging alone leaves the user with no idea their layout preference silently
                // failed to persist. Surface it on the active document's status bar (existing
                // pattern used elsewhere for restore/pull feedback) when one is available.
                if (ActiveDocument is MainViewModel activeMvm)
                    activeMvm.ShowStatus("⚠ Couldn't save window layout settings", 5000);
            }
        }

        // ── New Canvas ────────────────────────────────────────────────────

        private void ExecuteNewDocument(object? parameter)
        {
            using var operation = LoggingService.BeginOperation("Shell.NewDocument", new { parameter });
            if (OpenDocuments.Count >= MaxTabs)
            {
                _dialogService.ShowMessage($"Maximum of {MaxTabs} tabs reached. Close a tab first.");
                Logger.Warning("New document blocked because max tabs reached. MaxTabs={MaxTabs}", MaxTabs);
                return;
            }

            // Quick-start: parameter format can be "WxH" or "Mode:ColorMode:WxH"
            if (parameter is string paramStr && paramStr.Contains('x', StringComparison.Ordinal))
            {
                string[] parts = paramStr.Split(':');
                string sizeStr = parts[^1];
                var sizeParts = sizeStr.Split('x');

                if (sizeParts.Length == 2 &&
                    int.TryParse(sizeParts[0], out int qw) &&
                    int.TryParse(sizeParts[1], out int qh) &&
                    qw > 0 && qh > 0)
                {
                    DocumentMode mode = DocumentMode.Sprite;
                    ColorMode colorMode = ColorMode.Monochrome;

                    if (parts.Length >= 3)
                    {
                        _ = Enum.TryParse(parts[0], out mode);
                        _ = Enum.TryParse(parts[1], out colorMode);
                    }

                    if (mode == DocumentMode.Sprite)
                    {
                        AddNewDocument(qw, qh, colorMode);
                        Logger.Information("Created quick-start canvas {Width}x{Height} ColorMode={ColorMode}", qw, qh, colorMode);
                    }
                    else if (mode == DocumentMode.Font)
                    {
                        var fvm = CreateFontViewModel(FontDocument.CreateNew(qw, qh));
                        fvm.FontName = "Untitled Font";
                        
                        OpenDocuments.Add(fvm);
                        ActiveDocument = fvm;
                        TabAdded?.Invoke(this, fvm);

                        Logger.Information("Created quick-start font {Width}x{Height}", qw, qh);
                    }
                    else if (mode == DocumentMode.AssetPack)
                    {
                        var apvm = CreateAssetPackViewModel(AssetPackDocument.CreateNew());
                        OpenDocuments.Add(apvm);
                        ActiveDocument = apvm;
                        TabAdded?.Invoke(this, apvm);

                        Logger.Information("Created quick-start asset pack");
                    }
                    return;
                }
            }

            var result = _dialogService.ShowNewDocumentDialog();
            if (result.HasValue)
            {
                if (result.Value.DocumentMode == DocumentMode.Sprite)
                {
                    AddNewDocument(result.Value.Width, result.Value.Height, result.Value.ColorMode);
                    Logger.Information("Created canvas from dialog {Width}x{Height} ColorMode={ColorMode}", result.Value.Width, result.Value.Height, result.Value.ColorMode);
                }
                else if (result.Value.DocumentMode == DocumentMode.Font)
                {
                    var fvm = CreateFontViewModel(FontDocument.CreateNew(result.Value.Width, result.Value.Height));
                    fvm.FontName = "Untitled Font";
                    
                    OpenDocuments.Add(fvm);
                    ActiveDocument = fvm;
                    TabAdded?.Invoke(this, fvm);

                    Logger.Information("Created new font document {Width}x{Height}", result.Value.Width, result.Value.Height);
                }
                else if (result.Value.DocumentMode == DocumentMode.AssetPack)
                {
                    var apvm = CreateAssetPackViewModel(AssetPackDocument.CreateNew());
                    OpenDocuments.Add(apvm);
                    ActiveDocument = apvm;
                    TabAdded?.Invoke(this, apvm);

                    Logger.Information("Created new asset pack document");
                }
            }
        }

        public FontViewModel CreateFontViewModel(FontDocument? doc = null)
        {
            var autosave = _serviceProvider?.GetService<IAutosaveService>() ?? new AutosaveService();
            var fvm = new FontViewModel(autosave);
            if (doc != null)
            {
                fvm.Document = doc;
            }
            return fvm;
        }

        public AssetPackViewModel CreateAssetPackViewModel(
            AssetPackDocument? doc = null,
            IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>? pack = null)
        {
            return new AssetPackViewModel(
                document: doc,
                pack: pack,
                windowManager: _flipperWindowManager,
                dialogService: _dialogService,
                feedbackService: _userFeedbackService,
                clipboardService: _clipboardService,
                tabService: this,
                exportService: _serviceProvider?.GetService<Services.IFlipperExportService>() ?? new Services.FlipperExportService(),
                importService: _serviceProvider?.GetService<Services.IFlipperImportService>() ?? new Services.FlipperImportService(),
                autosaveService: _serviceProvider?.GetService<IAutosaveService>() ?? new AutosaveService());
        }

        private void ExecuteNewAssetPack()
        {
            if (OpenDocuments.Count >= MaxTabs)
            {
                _dialogService.ShowMessage($"Maximum of {MaxTabs} tabs reached. Close a tab first.");
                Logger.Warning("New Asset Pack blocked because max tabs reached. MaxTabs={MaxTabs}", MaxTabs);
                return;
            }

            var apvm = CreateAssetPackViewModel(AssetPackDocument.CreateNew());
            OpenDocuments.Add(apvm);
            ActiveDocument = apvm;
            TabAdded?.Invoke(this, apvm);
            Logger.Information("Created new Asset Pack document tab");
        }

        public void OpenAssetPackInTab(
            IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>? pack = null,
            string packName = "Flipper Asset Pack")
        {
            if (OpenDocuments.Count >= MaxTabs)
            {
                _dialogService.ShowMessage($"Maximum of {MaxTabs} tabs reached. Close a tab first.");
                Logger.Warning("Open Asset Pack blocked because max tabs reached. MaxTabs={MaxTabs}", MaxTabs);
                return;
            }

            // If an existing Asset Pack tab matches this pack name, activate it
            var existing = OpenDocuments.OfType<AssetPackViewModel>().FirstOrDefault(ap =>
                string.Equals(ap.MatrixViewModel.PackName, packName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                ActiveDocument = existing;
                return;
            }

            AssetPackDocument doc;
            if (pack != null && pack.Count > 0)
            {
                doc = new AssetPackDocument
                {
                    PackName = packName,
                    IsStockMode = pack.All(p => p.ManifestEntry.MaxLevel <= 3),
                    Entries = [.. pack.Select(p => p.ManifestEntry.Clone())],
                };
            }
            else
            {
                doc = AssetPackDocument.CreateNew(packName);
            }

            var vm = CreateAssetPackViewModel(doc, pack);
            OpenDocuments.Add(vm);
            ActiveDocument = vm;
            TabAdded?.Invoke(this, vm);
            Logger.Information("Opened Asset Pack tab for {PackName}", packName);
        }

        public void OpenFontInTab(string fontName, int glyphWidth = 8, int glyphHeight = 8)
        {
            if (OpenDocuments.Count >= MaxTabs)
            {
                _dialogService.ShowMessage($"Maximum of {MaxTabs} tabs reached. Close a tab first.");
                Logger.Warning("Open Font blocked because max tabs reached. MaxTabs={MaxTabs}", MaxTabs);
                return;
            }

            var existing = OpenDocuments.OfType<FontViewModel>().FirstOrDefault(f =>
                string.Equals(f.FontName, fontName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                ActiveDocument = existing;
                return;
            }

            var doc = FontDocument.CreateNew(glyphWidth, glyphHeight);
            var fvm = CreateFontViewModel(doc);
            fvm.FontName = string.IsNullOrWhiteSpace(fontName) ? "Untitled Font" : fontName;

            OpenDocuments.Add(fvm);
            ActiveDocument = fvm;
            TabAdded?.Invoke(this, fvm);
            Logger.Information("Opened Font Editor tab for {FontName}", fontName);
        }

        // ── Open ──────────────────────────────────────────────────────────

        private void ExecuteOpen()
        {
            using var operation = LoggingService.BeginOperation("Shell.OpenDocument");
            if (OpenDocuments.Count >= MaxTabs)
            {
                _dialogService.ShowMessage($"Maximum of {MaxTabs} tabs reached. Close a tab first.");
                Logger.Warning("Open blocked because max tabs reached. MaxTabs={MaxTabs}", MaxTabs);
                return;
            }

            var path = _dialogService.ShowOpenFileDialog(FileFilter, "Open Sprite");
            if (path == null) return;

            OpenFile(path);
        }

        public void OpenFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!File.Exists(path) && !Directory.Exists(path)) return;

            // Focus existing tab if file is already open
            try
            {
                string targetFullPath = Path.GetFullPath(path);
                var existingDoc = OpenDocuments.FirstOrDefault(d =>
                    !string.IsNullOrWhiteSpace(d.FilePath) &&
                    string.Equals(Path.GetFullPath(d.FilePath), targetFullPath, StringComparison.OrdinalIgnoreCase));

                if (existingDoc != null)
                {
                    ActiveDocument = existingDoc;
                    UserPreferencesService.AddRecentFile(path);
                    RefreshRecentFiles();
                    return;
                }
            }
            catch { }

            string ext = Path.GetExtension(path).ToLowerInvariant();
            string fileName = Path.GetFileName(path);

            if (ext == ".zip")
            {
                OpenZipAssetPack(path);
                return;
            }
            if (ext == ".bm" || Directory.Exists(path) || string.Equals(fileName, "meta.txt", StringComparison.OrdinalIgnoreCase))
            {
                ImportFlipperFromPath(path);
                return;
            }
            if (ext == ".c" || ext == ".cpp" || ext == ".h" || ext == ".hpp" || ext == ".ino" || ext == ".py")
            {
                ImportFromFile(path);
                return;
            }
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif" || ext == ".tif" || ext == ".tiff")
            {
                ImportBitmapFromFile(path);
                return;
            }
            if (ext == ".xbm")
            {
                ImportXbmFromPath(path);
                return;
            }
            if (ext == ".hexpack")
            {
                OpenHexpack(path);
                return;
            }
            if (string.Equals(fileName, "manifest.txt", StringComparison.OrdinalIgnoreCase))
            {
                OpenManifestFile(path);
                return;
            }

            if (OpenDocuments.Count >= MaxTabs)
            {
                _dialogService.ShowMessage($"Maximum of {MaxTabs} tabs reached. Close a tab first.");
                Logger.Warning("Open blocked because max tabs reached. MaxTabs={MaxTabs}", MaxTabs);
                return;
            }

            try
            {
                string json = SafeFileIo.ReadAllTextWithRetry(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    _dialogService.ShowMessage($"The file '{Path.GetFileName(path)}' is empty and cannot be opened.");
                    return;
                }

                // Detect document type by peeking at the JSON structure.
                bool isFontDocument = false;
                bool isAssetPackDocument = false;
                using (var jsonDoc = JsonDocument.Parse(json))
                {
                    isFontDocument = jsonDoc.RootElement.TryGetProperty("Glyphs", out _) ||
                                     ext == ".hexfont" || ext == ".hexpfont";
                    isAssetPackDocument = jsonDoc.RootElement.TryGetProperty("PackName", out _) &&
                                          jsonDoc.RootElement.TryGetProperty("Entries", out _);
                }

                if (isAssetPackDocument)
                {
                    OpenHexpack(path);
                    return;
                }

                if (isFontDocument)
                {
                    var fontDoc = JsonSerializer.Deserialize<FontDocument>(json);
                    if (fontDoc == null)
                    {
                        _dialogService.ShowMessage($"The font file '{Path.GetFileName(path)}' could not be read.");
                        return;
                    }
                    fontDoc.NormalizeGlyphs();

                    var fvm = CreateFontViewModel(fontDoc);
                    fvm.IsNewlyCreated = false;
                    fvm.FilePath = path;
                    fvm.MarkAsClean();

                    OpenDocuments.Add(fvm);
                    ActiveDocument = fvm;
                    TabAdded?.Invoke(this, fvm);

                    UserPreferencesService.AddRecentFile(path);
                    RefreshRecentFiles();
                    Logger.Information("Opened font document at {Path}", path);
                }
                else
                {
                    var loaded = JsonSerializer.Deserialize<SpriteState>(json);
                    if (loaded == null)
                    {
                        _dialogService.ShowMessage($"The file '{Path.GetFileName(path)}' could not be deserialized.");
                        return;
                    }

                    // Bounds and dimension sanity validation (1 to 512)
                    if (loaded.Width <= 0 || loaded.Height <= 0 || loaded.Width > 512 || loaded.Height > 512)
                    {
                        _dialogService.ShowMessage(string.Create(CultureInfo.InvariantCulture, $"The file '{Path.GetFileName(path)}' has invalid canvas dimensions ({loaded.Width}x{loaded.Height}). Canvas dimensions must be between 1x1 and 512x512."));
                        return;
                    }

                    loaded.NormalizeLayerState();

                    var doc = CreateDocument(loaded.Width, loaded.Height, loaded.ColorMode);

                    // Apply the complete state so layer metadata (including global
                    // layers and their persisted local backups) is not discarded.
                    doc.LoadState(loaded);
                    bool isAnim = loaded.IsAnimationEnabled || (loaded.Frames != null && loaded.Frames.Count > 1) || loaded.FlipperCycle != null;
                    doc.IsAnimationEnabled = isAnim;
                    doc.SpriteState.IsAnimationEnabled = isAnim;
                    doc.FrameRateFps = loaded.FrameRateFps;
                    doc.PlaybackDirection = loaded.PlaybackDirection;
                    doc.IsDisplayInverted = loaded.IsDisplayInverted;
                    doc.FilePath = path;

                    if (isAnim)
                    {
                        IsTimelineVisible = true;
                    }

                    doc.MarkAsClean();
                    doc.RedrawGridFromMemory();

                    // Restore last-used export settings (null-safe for old files)
                    doc.ApplyExportSettings(loaded.ExportSettings);

                    OpenDocuments.Add(doc);
                    ActiveDocument = doc;
                    TabAdded?.Invoke(this, doc);
                    UserPreferencesService.AddRecentFile(path);
                    RefreshRecentFiles();
                    Logger.Information("Opened document at {Path} with size {Width}x{Height}", path, doc.SpriteState.Width, doc.SpriteState.Height);
                }
            }
            catch (JsonException jex)
            {
                HandledErrorReporter.Error(jex, "ShellViewModel.OpenFile.JsonError", new { path });
                _dialogService.ShowMessage($"Error opening file: The file '{Path.GetFileName(path)}' is not a valid Hexprite file or is corrupted.\n\nDetails: {jex.Message}");
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Error(ex, "ShellViewModel.OpenFile", new { path });
                _dialogService.ShowMessage($"Error opening file: {ex.Message}");
            }
        }

        private void OpenHexpack(string path)
        {
            try
            {
                string json = SafeFileIo.ReadAllTextWithRetry(path);
                var doc = JsonSerializer.Deserialize<AssetPackDocument>(json);
                if (doc == null)
                {
                    _dialogService.ShowMessage($"The asset pack file '{Path.GetFileName(path)}' could not be read.");
                    return;
                }
                doc.EnsureCaseInsensitiveAnimations();

                var vm = CreateAssetPackViewModel(doc);
                vm.FilePath = path;

                // Resolve animation file paths (from doc or companion .hexp files in same folder or anims/ subfolder)
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    foreach (var entry in doc.Entries)
                    {
                        string clean = entry.Name.Trim().TrimStart('*').Trim();
                        string? companionHexp = null;

                        if (doc.AnimationFilePaths != null && doc.AnimationFilePaths.TryGetValue(clean, out var savedPath) && !string.IsNullOrEmpty(savedPath))
                        {
                            companionHexp = Path.IsPathRooted(savedPath) ? savedPath : Path.GetFullPath(Path.Combine(dir, savedPath));
                        }

                        if (string.IsNullOrEmpty(companionHexp) || !File.Exists(companionHexp))
                        {
                            companionHexp = Path.Combine(dir, $"{clean}.hexp");
                            if (!File.Exists(companionHexp))
                            {
                                companionHexp = Path.Combine(dir, "anims", $"{clean}.hexp");
                            }
                        }

                        if (File.Exists(companionHexp))
                        {
                            vm.MatrixViewModel.SetAnimationFilePath(clean, companionHexp);

                            if (doc.Animations == null || !doc.Animations.ContainsKey(clean))
                            {
                                try
                                {
                                    string cJson = SafeFileIo.ReadAllTextWithRetry(companionHexp);
                                    var sp = JsonSerializer.Deserialize<SpriteState>(cJson);
                                    if (sp != null)
                                    {
                                        sp.NormalizeLayerState();
                                        vm.MatrixViewModel.SetAnimationSprite(clean, sp);
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    vm.MatrixViewModel.RefreshCurrentPreviewSprite();
                }

                vm.MarkAsClean();

                OpenDocuments.Add(vm);
                ActiveDocument = vm;
                TabAdded?.Invoke(this, vm);

                UserPreferencesService.AddRecentFile(path);
                RefreshRecentFiles();
                Logger.Information("Opened asset pack document at {Path}", path);
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Error(ex, "ShellViewModel.OpenHexpack", new { path });
                _dialogService.ShowMessage($"Error opening asset pack: {ex.Message}");
            }
        }

        private void OpenZipAssetPack(string path)
        {
            try
            {
                var importService = _serviceProvider?.GetService<Services.IFlipperImportService>() ?? new Services.FlipperImportService();
                var imported = importService.ImportAssetPack(path);
                if (imported.Count == 0)
                {
                    _dialogService.ShowMessage($"The zip archive '{Path.GetFileName(path)}' does not contain any valid Flipper animations or manifest entries.");
                    return;
                }

                string packName = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrWhiteSpace(packName)) packName = "Flipper Asset Pack";

                OpenAssetPackInTab(imported, packName);
                Logger.Information("Opened zip archive asset pack from {Path}", path);
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Error(ex, "ShellViewModel.OpenZipAssetPack", new { path });
                _dialogService.ShowMessage($"Error opening zip archive: {ex.Message}");
            }
        }

        private void OpenManifestFile(string path)
        {
            try
            {
                var importService = _serviceProvider?.GetService<Services.IFlipperImportService>() ?? new Services.FlipperImportService();
                var imported = importService.ImportAssetPack(path);
                if (imported.Count == 0)
                {
                    _dialogService.ShowMessage($"The manifest file '{Path.GetFileName(path)}' does not contain valid entries.");
                    return;
                }

                string dirName = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
                string packName = string.IsNullOrWhiteSpace(dirName) ? "Flipper Asset Pack" : dirName;

                OpenAssetPackInTab(imported, packName);
                Logger.Information("Opened manifest file into asset pack tab from {Path}", path);
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Error(ex, "ShellViewModel.OpenManifestFile", new { path });
                _dialogService.ShowMessage($"Error opening manifest: {ex.Message}");
            }
        }

        // ── Save / Save As ────────────────────────────────────────────────

        private void ExecuteSave()
        {
            if (ActiveDocument == null) return;
            using var operation = LoggingService.BeginOperation("Shell.Save", new { title = ActiveDocument.Title, hasFilePath = ActiveDocument.FilePath != null });

            if (ActiveDocument.FilePath != null)
            {
                if (ActiveDocument is MainViewModel mvm) SaveToPath(mvm, ActiveDocument.FilePath);
                else if (ActiveDocument is FontViewModel fvm) SaveFontToPath(fvm, ActiveDocument.FilePath);
                else if (ActiveDocument is AssetPackViewModel apvm) { apvm.Save(); RefreshRecentFiles(); }
            }
            else
                ExecuteSaveAs();
        }

        private void ExecuteSaveAs()
        {
            if (ActiveDocument == null) return;
            using var operation = LoggingService.BeginOperation("Shell.SaveAs", new { title = ActiveDocument.Title });

            string filter = ActiveDocument switch
            {
                FontViewModel => "Hexprite Font (*.hexfont)|*.hexfont|All Files (*.*)|*.*",
                AssetPackViewModel => "Hexprite Asset Pack (*.hexpack)|*.hexpack|All Files (*.*)|*.*",
                _ => "Hexprite Sprite (*.hexp)|*.hexp|All Files (*.*)|*.*",
            };
            string defaultExt = ActiveDocument switch
            {
                FontViewModel => ".hexfont",
                AssetPackViewModel => ".hexpack",
                _ => ".hexp",
            };
            
            var path = _dialogService.ShowSaveFileDialog(filter, "Save File", defaultExt);
            if (path != null)
            {
                path = SafeFileIo.EnsureExtension(path, defaultExt);
                if (ActiveDocument is MainViewModel mvm) SaveToPath(mvm, path);
                else if (ActiveDocument is FontViewModel fvm) SaveFontToPath(fvm, path);
                else if (ActiveDocument is AssetPackViewModel apvm) { apvm.SaveAs(path); RefreshRecentFiles(); }
            }
        }

        private void SaveToPath(MainViewModel doc, string path)
        {
            using var operation = LoggingService.BeginOperation("Shell.SaveToPath", new { path, width = doc.SpriteState.Width, height = doc.SpriteState.Height });
            
            doc.SuspendLinkedFileWatcher();
            try
            {
                // Persist the current export settings alongside the pixel data
                doc.SpriteState.NormalizeLayerState();
                doc.SpriteState.ExportSettings = doc.ExportSettings;

                string json = JsonSerializer.Serialize(doc.SpriteState, IndentedJsonOptions);
                SafeFileIo.WriteAllTextAtomic(path, json, maxRetries: 5, createBackup: true);
                doc.FilePath = path;
                doc.MarkAsClean();
                doc.ClearAutosave();

                doc.UpdateLinkedFileHashIfMatches(path);

                // Offer a sensible default sprite name derived from the filename
                doc.UpdateSpriteNameFromFile();
                UserPreferencesService.AddRecentFile(path);
                RefreshRecentFiles();
                Logger.Information("Saved document to {Path}", path);
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Error(ex, "ShellViewModel.SaveFile", new { path });
                _dialogService.ShowMessage($"Error saving: {ex.Message}");
            }
            finally
            {
                doc.ResumeLinkedFileWatcher();
            }
        }

        private void SaveFontToPath(FontViewModel doc, string path)
        {
            try
            {
                string json = JsonSerializer.Serialize(doc.Document, IndentedJsonOptions);
                SafeFileIo.WriteAllTextAtomic(path, json, maxRetries: 5, createBackup: true);
                doc.FilePath = path;
                doc.MarkAsClean();
                UserPreferencesService.AddRecentFile(path);
                RefreshRecentFiles();
                Logger.Information("Saved font document to {Path}", path);
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Error(ex, "ShellViewModel.SaveFontFile", new { path });
                _dialogService.ShowMessage($"Error saving font: {ex.Message}");
            }
        }

        // ── Close Tab ─────────────────────────────────────────────────────

        private async void ExecuteCloseTab(IDocumentTab? doc)
        {
            doc ??= ActiveDocument;
            if (doc != null)
                await TryCloseTabAsync(doc);
        }

        public async System.Threading.Tasks.Task<bool> TryCloseAllTabsAsync()
        {
            var docs = OpenDocuments.ToList();
            foreach (var doc in docs)
            {
                if (!await TryCloseTabAsync(doc))
                    return false;
            }
            return true;
        }

        private async void ExecuteCloseAllTabs()
        {
            await TryCloseAllTabsAsync();
        }

        private async void ExecuteCloseOtherTabs(IDocumentTab? docToKeep)
        {
            docToKeep ??= ActiveDocument;
            if (docToKeep == null) return;

            var docs = OpenDocuments.Where(d => d != docToKeep).ToList();
            foreach (var doc in docs)
            {
                if (!await TryCloseTabAsync(doc))
                    break;
            }
        }

        public async System.Threading.Tasks.Task<bool> TryCloseOtherTabsAsync(IDocumentTab? docToKeep)
        {
            docToKeep ??= ActiveDocument;
            if (docToKeep == null) return true;

            var docsToClose = OpenDocuments.Where(d => d != docToKeep).ToList();
            foreach (var doc in docsToClose)
            {
                if (!await TryCloseTabAsync(doc))
                    return false;
            }
            return true;
        }

        public async System.Threading.Tasks.Task<bool> TryCloseTabAsync(IDocumentTab doc)
        {
            if (doc.HasUnsavedChanges)
            {
                bool isLinked = doc is MainViewModel docMvm && docMvm.IsLinked;
                var saveChoice = _dialogService.ShowUnsavedChangesDialog(doc.Title.TrimStart('*'), isLinked);

                if (saveChoice == null) return false; // user cancelled

                if (saveChoice == true)
                {
                    if (doc is MainViewModel linkedMvm && linkedMvm.IsLinked)
                    {
                        // The user chose "Update Linked Source"
                        await linkedMvm.ExecuteUpdateLinkedSourceAsync();
                        if (linkedMvm.IsDirty) return false; // if it failed or was cancelled, don't close
                    }
                    else if (doc.FilePath != null)
                    {
                        if (doc is MainViewModel mvm2) SaveToPath(mvm2, doc.FilePath);
                        else if (doc is FontViewModel fvm) SaveFontToPath(fvm, doc.FilePath);
                        else if (doc is AssetPackViewModel apvm) apvm.Save();
                    }
                    else
                    {
                        string filter = doc switch
                        {
                            FontViewModel => "Hexprite Font (*.hexfont)|*.hexfont|All Files (*.*)|*.*",
                            AssetPackViewModel => "Hexprite Asset Pack (*.hexpack)|*.hexpack|All Files (*.*)|*.*",
                            _ => "Hexprite Sprite (*.hexp)|*.hexp|All Files (*.*)|*.*",
                        };
                        string defaultExt = doc switch
                        {
                            FontViewModel => ".hexfont",
                            AssetPackViewModel => ".hexpack",
                            _ => ".hexp",
                        };
                        var path = _dialogService.ShowSaveFileDialog(filter, "Save File", defaultExt);
                        if (path != null)
                        {
                            path = SafeFileIo.EnsureExtension(path, defaultExt);
                            if (doc is MainViewModel mvm3) SaveToPath(mvm3, path);
                            else if (doc is FontViewModel fvm2) SaveFontToPath(fvm2, path);
                            else if (doc is AssetPackViewModel apvm2) apvm2.SaveAs(path);
                        }
                        else
                            return false; // Cancelled
                    }

                    if (doc.HasUnsavedChanges) return false; // Save failed, abort tab closure
                }
            }

            int idx = OpenDocuments.IndexOf(doc);
            // Bug 4: Stop the playback timer and unsubscribe events BEFORE removing the
            // document from the collection. Without this, a non-active tab's DispatcherTimer
            // keeps ticking and the Dispatcher holds a GC root into the tab's entire object graph.
            doc.IsActive = false; // ensures playback stops via the IsActive setter
            if (doc is MainViewModel mvm) mvm.Detach(); // also unsubscribes SelectionChanged + disposes status CTS
            else if (doc is IDisposable disposable) disposable.Dispose();
            TabClosed?.Invoke(this, doc);
            OpenDocuments.Remove(doc);

            if (OpenDocuments.Count == 0)
            {
                ActiveDocument = null;
                Logger.Information("Closed tab {TabTitle}. No tabs remain open.", doc.Title);
            }
            else
            {
                ActiveDocument = OpenDocuments[Math.Min(idx, OpenDocuments.Count - 1)];
                Logger.Information("Closed tab {TabTitle}. RemainingTabs={RemainingTabs}", doc.Title, OpenDocuments.Count);
            }

            CloseOtherTabsCommand?.NotifyCanExecuteChanged();
            CloseAllTabsCommand?.NotifyCanExecuteChanged();
            return true;
        }

        // ── Resize Canvas ─────────────────────────────────────────────────

        private void ExecuteResizeCanvas()
        {
            if (ActiveDocument is not MainViewModel mvmDoc) return;
            using var operation = LoggingService.BeginOperation(
                "Shell.ResizeCanvas",
                new { width = mvmDoc.SpriteState.Width, height = mvmDoc.SpriteState.Height });

            var result = _dialogService.ShowResizeCanvasDialog(
                mvmDoc.SpriteState.Width,
                mvmDoc.SpriteState.Height);

            if (result.HasValue)
            {
                var (w, h, anchor) = result.Value;
                if (w == mvmDoc.SpriteState.Width &&
                    h == mvmDoc.SpriteState.Height) return;

                mvmDoc.ResizeCanvas(w, h, anchor);
                Logger.Information("Resized active canvas to {Width}x{Height} using anchor {Anchor}", w, h, anchor);
            }
        }

        // ── Import from Code ──────────────────────────────────────────────

        private void ExecuteImportFromCode()
        {
            using var operation = LoggingService.BeginOperation("Shell.ImportFromCode");
            if (OpenDocuments.Count >= MaxTabs)
            {
                _dialogService.ShowMessage($"Maximum of {MaxTabs} tabs reached. Close a tab first.");
                Logger.Warning("Import blocked because max tabs reached. MaxTabs={MaxTabs}", MaxTabs);
                return;
            }

            var result = _dialogService.ShowImportFromCodeDialog();
            if (result == null) return;

            var (w, h, code, spriteName, format, invert) = result.Value;

            try
            {
                var doc = CreateDocument(w, h, ColorMode.Monochrome, redrawImmediately: false);
                if (format == ExportFormat.Indexed2D)
                    _codeGen.ParseIndexed2DToState(code, doc.SpriteState);
                else if (format == ExportFormat.U8g2DrawXBM)
                    _codeGen.ParseXbmToState(code, doc.SpriteState);
                else if (format == ExportFormat.RawBinary)
                    _codeGen.ParseBinaryToState(code, doc.SpriteState);
                else if (format == ExportFormat.LiquidCrystalChar)
                    _codeGen.ParseLiquidCrystalToState(code, doc.SpriteState);
                else
                    _codeGen.ParseAdafruitGfxToState(code, doc.SpriteState);

                if (invert)
                {
                    foreach (var frame in doc.SpriteState.Frames)
                    {
                        foreach (var layerBuffer in frame.LayerPixels)
                        {
                            var data = layerBuffer.GetMonochromeData();
                            for (int i = 0; i < data.Length; i++)
                            {
                                data[i] = !data[i];
                            }
                        }
                    }
                }

                doc.SpriteState.NormalizeLayerState();
                doc.ReloadLayersFromState();
                doc.RebuildFrameViewModels();

                bool hasMultipleFrames = doc.SpriteState.Frames != null && doc.SpriteState.Frames.Count > 1;
                if (hasMultipleFrames)
                {
                    doc.SpriteState.IsAnimationEnabled = true;
                    doc.IsAnimationEnabled = true;
                    IsTimelineVisible = true;
                }

                doc.RedrawGridFromMemory();

                // Set the detected sprite name so the export panel picks it up
                if (!string.IsNullOrWhiteSpace(spriteName))
                    doc.SetSpriteNameWithoutGenerating(spriteName);

                doc.MarkCodeStale();
                doc.UpdateTextOutputs();

                OpenDocuments.Add(doc);
                ActiveDocument = doc;
                TabAdded?.Invoke(this, doc);

                if (hasMultipleFrames)
                {
                    doc.ShowStatus($"Imported {doc.SpriteState.Frames.Count} animation frames");
                }
                else
                {
                    doc.ShowStatus("Imported sprite from code");
                }

                Logger.Information("Imported sprite from code. Format={Format} Size={Width}x{Height}", format, w, h);
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Error(ex, "ShellViewModel.ImportFromCode", new { w, h, format });
                _dialogService.ShowMessage($"Error importing: {ex.Message}");
            }
        }

        // ── Import from File ──────────────────────────────────────────────

        public void ImportFromFile(string? initialFilePath = null)
        {
            using var operation = LoggingService.BeginOperation("Shell.ImportFromFile");
            if (OpenDocuments.Count >= MaxTabs)
            {
                _dialogService.ShowMessage($"Maximum of {MaxTabs} tabs reached. Close a tab first.");
                Logger.Warning("Import blocked because max tabs reached. MaxTabs={MaxTabs}", MaxTabs);
                return;
            }

            var result = _dialogService.ShowImportFromFileDialog(initialFilePath);
            if (result == null) return;

            var (filePath, selectedSprites) = result.Value;

            foreach (var sprite in selectedSprites)
            {
                try
                {
                    var doc = CreateDocument(sprite.Width, sprite.Height, ColorMode.Monochrome, redrawImmediately: false);
                    MainViewModel.ParseCodeToState(_codeGen, sprite.Format, sprite.CodeSnippet, doc.SpriteState);

                    doc.SpriteState.NormalizeLayerState();
                    doc.ReloadLayersFromState();
                    doc.RebuildFrameViewModels();

                    bool fileHasMultipleFrames = doc.SpriteState.Frames != null && doc.SpriteState.Frames.Count > 1;
                    if (fileHasMultipleFrames)
                    {
                        doc.SpriteState.IsAnimationEnabled = true;
                        doc.IsAnimationEnabled = true;
                        IsTimelineVisible = true;
                    }

                    doc.RedrawGridFromMemory();

                    if (!string.IsNullOrWhiteSpace(sprite.Name))
                        doc.SetSpriteNameWithoutGenerating(sprite.Name);

                    doc.MarkCodeStale();
                    doc.UpdateTextOutputs();

                    doc.SpriteState.LinkedSourceFile = filePath;
                    doc.SpriteState.LinkedVariableName = sprite.Name;
                    doc.SpriteState.LinkedFormat = sprite.Format;
                    doc.NotifyLinkChanged();
                    doc.MarkAsClean();

                    OpenDocuments.Add(doc);
                    ActiveDocument = doc;
                    TabAdded?.Invoke(this, doc);
                    Logger.Information("Imported sprite from file. Name={Name} Format={Format} Size={Width}x{Height}", sprite.Name, sprite.Format, sprite.Width, sprite.Height);
                }
                catch (Exception ex)
                {
                    HandledErrorReporter.Error(ex, "ShellViewModel.ImportFromFile", new { sprite.Name, sprite.Format });
                    _dialogService.ShowMessage($"Error importing '{sprite.Name}': {ex.Message}");
                }
            }
        }

        internal async System.Threading.Tasks.Task ExecuteUpdateAllLinkedSourcesAsync()
        {
            var linkedDocs = OpenDocuments.Where(d => d.IsLinked).ToList();
            if (linkedDocs.Count == 0) return;

            int updated = 0;
            foreach (var doc in linkedDocs)
            {
                try
                {
                    if (doc is MainViewModel mvm)
                    {
                        if (await mvm.ExecuteUpdateLinkedSourceAsync())
                        {
                            updated++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    var linkedVar = (doc as MainViewModel)?.LinkedVariableName;
                    HandledErrorReporter.Error(ex, "ShellViewModel.UpdateAllLinkedSources", new { linkedVar });
                }
            }

            if (ActiveDocument is MainViewModel activeMvm)
                activeMvm.ShowStatus(string.Create(CultureInfo.InvariantCulture, $"✓ Updated {updated} linked source{(updated == 1 ? "" : "s")}"));
        }

        internal async System.Threading.Tasks.Task ExecutePullAllLinkedSourcesAsync()
        {
            var linkedDocs = OpenDocuments.Where(d => d.IsLinked).ToList();
            if (linkedDocs.Count == 0) return;

            var confirm = _dialogService.ShowConfirmation(
                $"Pull external changes for {linkedDocs.Count} linked source{(linkedDocs.Count == 1 ? "" : "s")}?\n\nThis will overwrite the canvases with the contents from the external files.",
                "Pull All Linked Sources");
            if (!confirm) return;

            int pulled = 0;
            foreach (var doc in linkedDocs)
            {
                try
                {
                    if (doc is MainViewModel mvm)
                    {
                        await mvm.ExecutePullLinkedSourceAsync();
                        pulled++;
                    }
                }
                catch (Exception ex)
                {
                    var linkedVar = (doc as MainViewModel)?.LinkedVariableName;
                    HandledErrorReporter.Error(ex, "ShellViewModel.PullAllLinkedSources", new { linkedVar });
                }
            }

            if (ActiveDocument is MainViewModel activeMvm)
                activeMvm.ShowStatus(string.Create(CultureInfo.InvariantCulture, $"✓ Pulled {pulled} linked source{(pulled == 1 ? "" : "s")}"));
        }

        internal async System.Threading.Tasks.Task ExecuteRestoreAllLinkedSourcesAsync()
        {
            var linkedDocs = OpenDocuments.Where(d => d.IsLinked).ToList();
            if (linkedDocs.Count == 0) return;

            // Single confirmation for the entire batch
            var confirm = _dialogService.ShowConfirmation(
                $"Restore {linkedDocs.Count} linked source{(linkedDocs.Count == 1 ? "" : "s")} to their original state?\n\nThis will overwrite all linked files and canvases with their Hexprite backups.",
                "Restore All Linked Sources");
            if (!confirm) return;

            int restored = 0;
            foreach (var doc in linkedDocs)
            {
                try
                {
                    if (doc is MainViewModel mvm && mvm.IsLinked)
                    {
                        await mvm.ExecuteRestoreLinkedSourceAsync(skipConfirmation: true);
                        restored++;
                    }
                }
                catch (Exception ex)
                {
                    var linkedVar = (doc as MainViewModel)?.LinkedVariableName;
                    HandledErrorReporter.Error(ex, "ShellViewModel.RestoreAllLinkedSources", new { linkedVar });
                }
            }

            if (ActiveDocument is MainViewModel activeMvm2)
                activeMvm2.ShowStatus(string.Create(CultureInfo.InvariantCulture, $"✓ Restored {restored} linked source{(restored == 1 ? "" : "s")}"));
        }

        // ── Import bitmap ─────────────────────────────────────────────────

        private const string BitmapImageFileFilter =
            "Image Files (*.png;*.bmp;*.jpg;*.jpeg;*.tif;*.tiff;*.gif)|*.png;*.bmp;*.jpg;*.jpeg;*.tif;*.tiff;*.gif|All Files (*.*)|*.*";
        private static readonly string UserSettingsDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hexprite");
        private static readonly string BitmapImportSettingsFile =
            Path.Combine(UserSettingsDirectory, "bitmap-import-settings.json");

        private static Task<T> RunOnStaThreadAsync<T>(Func<T> func)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            var thread = new Thread(() =>
            {
                try
                {
                    T result = func();
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            })
            {
                IsBackground = true,
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            return tcs.Task;
        }

        private void ExecuteImportFlipper()
        {
            var selectedPath = _dialogService.ShowOpenFileDialog("Flipper Animation (meta.txt)|meta.txt|Flipper Bitmap (*.bm)|*.bm", "Import Flipper Zero Asset");
            if (selectedPath == null) return;

            ImportFlipperFromPath(selectedPath);
        }

        public async void ImportFlipperFromPath(string selectedPath)
        {
            await ImportFlipperFromPathAsync(selectedPath);
        }

        public async Task ImportFlipperFromPathAsync(string selectedPath)
        {
            if (IsImporting) return;

            if (OpenDocuments.Count >= MaxTabs)
            {
                _dialogService.ShowMessage($"Maximum of {MaxTabs} tabs reached. Close a tab first.");
                Logger.Warning("Import blocked because max tabs reached. MaxTabs={MaxTabs}", MaxTabs);
                return;
            }

            if (System.IO.Directory.Exists(selectedPath))
            {
                string candidate = System.IO.Path.Combine(selectedPath, "meta.txt");
                if (!System.IO.File.Exists(candidate))
                {
                    try
                    {
                        var importService = _serviceProvider?.GetService<Services.IFlipperImportService>() ?? new Services.FlipperImportService();
                        var imported = importService.ImportAssetPack(selectedPath);
                        if (imported.Count > 0)
                        {
                            string packName = System.IO.Path.GetFileName(selectedPath);
                            if (string.IsNullOrWhiteSpace(packName)) packName = "Flipper Asset Pack";
                            OpenAssetPackInTab(imported, packName);
                            return;
                        }
                    }
                    catch
                    {
                        // Fall through to show error message
                    }

                    _dialogService.ShowMessage($"No meta.txt or manifest.txt found in folder: {selectedPath}");
                    return;
                }
                selectedPath = candidate;
            }

            IsImporting = true;
            try
            {
                var importService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Services.IFlipperImportService>(_serviceProvider);
                bool isAnimation = System.IO.Path.GetFileName(selectedPath).Equals("meta.txt", StringComparison.OrdinalIgnoreCase);
                SpriteState sprite = await RunOnStaThreadAsync(() =>
                    isAnimation ? importService.ImportAnimation(selectedPath) : importService.ImportFrame(selectedPath));

                var doc = CreateDocument(sprite.Width, sprite.Height, ColorMode.Monochrome, redrawImmediately: false);
                doc.SpriteState.Frames = sprite.Frames;
                doc.SpriteState.FrameRateFps = sprite.FrameRateFps;
                doc.SpriteState.FlipperCycle = sprite.FlipperCycle;
                doc.FrameRateFps = sprite.FrameRateFps;

                doc.SpriteState.NormalizeLayerState();
                doc.ReloadLayersFromState();
                doc.RebuildFrameViewModels();

                bool hasMultipleFramesOrAnimation = isAnimation || sprite.Frames.Count > 1 || sprite.FlipperCycle != null;
                if (hasMultipleFramesOrAnimation)
                {
                    doc.SpriteState.IsAnimationEnabled = true;
                    doc.IsAnimationEnabled = true;
                    IsTimelineVisible = true;
                }

                doc.MarkAsClean();
                doc.RedrawGridFromMemory();

                OpenDocuments.Add(doc);
                ActiveDocument = doc;
                TabAdded?.Invoke(this, doc);
                doc.ShowStatus(sprite.FlipperCycle != null
                    ? string.Create(CultureInfo.InvariantCulture, $"Imported {sprite.Frames.Count} unique frames ({sprite.FlipperCycle.FramesOrder.Length}-frame cycle: {sprite.FlipperCycle.PassiveFrameCount} passive + {sprite.FlipperCycle.ActiveFrameCount} active)")
                    : string.Create(CultureInfo.InvariantCulture, $"Imported {sprite.Frames.Count} frame{(sprite.Frames.Count == 1 ? "" : "s")}"));
                Logger.Information("Imported Flipper asset from {Path}", selectedPath);
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Error(ex, "ShellViewModel.ImportFlipperFromPath", new { selectedPath });
                _dialogService.ShowMessage($"Error importing Flipper asset: {ex.Message}");
            }
            finally
            {
                IsImporting = false;
            }
        }

        private void ExecuteImportXbm()
        {
            var selectedPath = _dialogService.ShowOpenFileDialog("XBM Bitmap (*.xbm)|*.xbm", "Import XBM File");
            if (selectedPath == null) return;

            ImportXbmFromPath(selectedPath);
        }

        public async void ImportXbmFromPath(string selectedPath)
        {
            if (IsImporting) return;

            if (OpenDocuments.Count >= MaxTabs)
            {
                _dialogService.ShowMessage($"Maximum of {MaxTabs} tabs reached. Close a tab first.");
                Logger.Warning("Import blocked because max tabs reached. MaxTabs={MaxTabs}", MaxTabs);
                return;
            }

            IsImporting = true;
            try
            {
                var xbmService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Services.IXbmService>(_serviceProvider);
                var (width, height, body) = await RunOnStaThreadAsync(() => xbmService.ParseFile(selectedPath));

                var doc = CreateDocument(width, height, ColorMode.Monochrome, redrawImmediately: false);
                _codeGen.ParseXbmToState(body, doc.SpriteState);
                doc.SpriteState.NormalizeLayerState();
                doc.ReloadLayersFromState();
                doc.RebuildFrameViewModels();
                doc.MarkAsClean();
                doc.RedrawGridFromMemory();

                OpenDocuments.Add(doc);
                ActiveDocument = doc;
                TabAdded?.Invoke(this, doc);
                doc.ShowStatus(string.Create(CultureInfo.InvariantCulture, $"Imported {width}x{height} XBM image"));
                Logger.Information("Imported XBM file from {Path}", selectedPath);
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Error(ex, "ShellViewModel.ImportXbmFromPath", new { selectedPath });
                _dialogService.ShowMessage($"Error importing XBM file: {ex.Message}");
            }
            finally
            {
                IsImporting = false;
            }
        }

        private void ExecuteImportBitmap()
        {
            if (OpenDocuments.Count >= MaxTabs)
            {
                _dialogService.ShowMessage($"Maximum of {MaxTabs} tabs reached. Close a tab first.");
                Logger.Warning("Import blocked because max tabs reached. MaxTabs={MaxTabs}", MaxTabs);
                return;
            }

            var selectedPath = _dialogService.ShowOpenFileDialog(BitmapImageFileFilter, "Import Bitmap");
            if (selectedPath == null) return;

            ImportBitmapFromFile(selectedPath);
        }

        private void ExecuteImportSpriteSheet()
        {
            using var operation = LoggingService.BeginOperation("Shell.ImportSpriteSheet");
            var selectedPath = _dialogService.ShowOpenFileDialog(
                "Image Files (*.png;*.bmp;*.jpg;*.jpeg;*.gif;*.xbm)|*.png;*.bmp;*.jpg;*.jpeg;*.gif;*.xbm|All Files (*.*)|*.*",
                "Import Sprite Sheet / Animation Strip");
            if (selectedPath == null) return;

            _dialogService.ShowSpriteSheetSlicerDialog(initialFilePath: selectedPath);
        }

        private void ExecuteOpenSpriteSheetSlicer()
        {
            using var operation = LoggingService.BeginOperation("Shell.OpenSpriteSheetSlicer");
            var activeSprite = (ActiveDocument as MainViewModel)?.SpriteState;
            _dialogService.ShowSpriteSheetSlicerDialog(initialSprite: activeSprite);
        }

        public async void ImportBitmapFromFile(string selectedPath)
        {
            await ImportBitmapFromFileAsync(selectedPath);
        }

        public async Task ImportBitmapFromFileAsync(string selectedPath)
        {
            if (IsImporting) return;
            using var operation = LoggingService.BeginOperation("Shell.ImportBitmap");

            if (OpenDocuments.Count >= MaxTabs)
            {
                _dialogService.ShowMessage($"Maximum of {MaxTabs} tabs reached. Close a tab first.");
                Logger.Warning("Import blocked because max tabs reached. MaxTabs={MaxTabs}", MaxTabs);
                return;
            }

            try
            {
                var isGif = Path.GetExtension(selectedPath).Equals(".gif", StringComparison.OrdinalIgnoreCase);

                if (isGif)
                {
                    var baseSettings = LoadBitmapImportSettings();
                    var initSettings = AnimationImportSettings.FromBase(baseSettings);

                    var importSettings = _dialogService.ShowImportAnimationDialog(
                        selectedPath, initSettings);
                    if (importSettings == null) return;
                    SaveBitmapImportSettings(importSettings);

                    IsImporting = true;

                    var (frames, w, h, wasScaled) = await RunOnStaThreadAsync(() =>
                        GifToMonochromeConverter.ConvertAnimatedGif(selectedPath, importSettings));

                    if (frames.Count == 0)
                    {
                        _dialogService.ShowMessage("No frames could be extracted from the GIF animation.");
                        return;
                    }

                    var doc = CreateDocument(w, h, ColorMode.Monochrome, redrawImmediately: false);
                    doc.SpriteState.Frames.Clear();
                    doc.SpriteState.IsAnimationEnabled = true;
                    doc.SpriteState.FrameRateFps = importSettings.TargetFps;

                    for (int i = 0; i < frames.Count; i++)
                    {
                        doc.SpriteState.Frames.Add(new FrameState
                        {
                            Name = string.Create(CultureInfo.InvariantCulture, $"Frame {i + 1}"),
                            ActiveLayerIndex = 0,
                            LayerPixels = [new MonochromePixelBuffer(frames[i])],
                            Layers =
                            [
                                new()
                                {
                                    Name = "Layer 1",
                                    IsVisible = true,
                                    Pixels = frames[i],
                                },
                            ],
                        });
                    }
                    doc.SpriteState.ActiveFrameIndex = 0;
                    doc.SpriteState.ActiveLayerIndex = 0;
                    doc.SpriteState.Pixels = frames[0];

                    if (w == 128 && h == 64 && frames.Count > 0)
                    {
                        doc.SpriteState.FlipperCycle = new FlipperAnimationCycle
                        {
                            FramesOrder = [.. Enumerable.Range(0, frames.Count)],
                            PassiveFrameCount = frames.Count,
                            ActiveFrameCount = 0,
                            ActiveCycles = 1,
                            Duration = 3600,
                            ActiveCooldown = 0,
                            BubbleSlots = 0,
                        };
                    }
                    
                    doc.SpriteState.NormalizeLayerState();
                    doc.ReloadLayersFromState();
                    doc.FrameRateFps = importSettings.TargetFps;
                    doc.IsAnimationEnabled = true;
                    IsTimelineVisible = true;

                    doc.RedrawGridFromMemory();
                    doc.SetSpriteNameWithoutGenerating(CodeGeneratorService.SanitiseName(
                        Path.GetFileNameWithoutExtension(selectedPath)));

                    if (wasScaled)
                        doc.ShowStatus("Imported animation (scaled to max canvas size)");

                    doc.MarkCodeStale();

                    OpenDocuments.Add(doc);
                    ActiveDocument = doc;
                    TabAdded?.Invoke(this, doc);

                    Logger.Information(
                        "Imported animated gif. Frames={FrameCount} Fps={Fps} Scaled={WasScaled} Size={Width}x{Height}",
                        frames.Count, importSettings.TargetFps, wasScaled, w, h);
                }
                else
                {
                    var importSettings = _dialogService.ShowImportBitmapDialog(
                        selectedPath,
                        LoadBitmapImportSettings());
                    if (importSettings == null) return;
                    SaveBitmapImportSettings(importSettings);

                    IsImporting = true;

                    // WPF image decode pipelines are STA-affine; run conversion on a dedicated STA thread
                    // so large imports don't block the UI thread.
                    var (pixels, w, h, wasScaled) = await RunOnStaThreadAsync(() =>
                        BitmapToMonochromeConverter.ConvertTo1Bit(selectedPath, importSettings));

                    var doc = CreateDocument(w, h, ColorMode.Monochrome, redrawImmediately: false);
                    doc.SpriteState.Layers[0].PreserveOverflow = true;
                    var ovfBuffer = new Core.OverflowPixelBuffer(w, h);
                    ovfBuffer.WriteMonochromeData(pixels);
                    doc.SpriteState.Frames[0].LayerPixels[0] = ovfBuffer;
                    doc.SpriteState.EnsureLayers();
                    doc.ReloadLayersFromState(); // Sync the UI wrappers with the updated state
                    doc.SpriteState.Pixels = doc.SpriteState.Frames[0].LayerPixels[0].GetMonochromeData();

                    doc.RedrawGridFromMemory();

                    // Set name so export panel picks it up
                    doc.SetSpriteNameWithoutGenerating(CodeGeneratorService.SanitiseName(
                        Path.GetFileNameWithoutExtension(selectedPath)));

                    if (wasScaled)
                        doc.ShowStatus("Imported image (scaled to max canvas size)");

                    // Generating export code for large imports can be expensive and may
                    // cause UI stalls due to downstream syntax highlighting. Defer it
                    // until the user explicitly clicks "Generate Code".
                    doc.MarkCodeStale();

                    OpenDocuments.Add(doc);
                    ActiveDocument = doc;
                    TabAdded?.Invoke(this, doc);

                    Logger.Information(
                        "Imported bitmap image. Scaled={WasScaled} Size={Width}x{Height} Dither={Dither} ScaleMode={ScaleMode} Threshold={Threshold} Serpentine={Serpentine} GammaCorrect={GammaCorrect}",
                        wasScaled, w, h, importSettings.DitheringAlgorithm, importSettings.ScalingMode, importSettings.Threshold, importSettings.UseSerpentineScanning, importSettings.UseGammaCorrection);
                }
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Error(ex, "ShellViewModel.ImportBitmap", new { path = selectedPath });
                if (ex is NotSupportedException)
                    _dialogService.ShowMessage("This image format is not supported on your system.");
                else
                    _dialogService.ShowMessage($"Error importing image: {ex.Message}");
            }
            finally
            {
                IsImporting = false;
            }
        }

        private static BitmapImportSettings LoadBitmapImportSettings()
        {
            var defaults = new BitmapImportSettings
            {
                DitheringAlgorithm = BitmapDitheringAlgorithm.Atkinson,
                ScalingMode = BitmapScalingMode.Fant,
                Threshold = 128,
                AlphaThreshold = 128,
                MaxDimension = SpriteState.MaxDimension,
                Brightness = 0,
                Contrast = 0,
                DitherAmount = 100,
                Sharpen = false,
                UseSerpentineScanning = false,
                UseGammaCorrection = false,
                UseAdaptiveThresholding = false,
                PreserveEdges = false,
            };

            try
            {
                if (!File.Exists(BitmapImportSettingsFile))
                    return defaults;

                string json = File.ReadAllText(BitmapImportSettingsFile);
                BitmapImportSettings? saved = JsonSerializer.Deserialize<BitmapImportSettings>(json);
                if (saved == null)
                    return defaults;

                saved.Threshold = Math.Clamp(saved.Threshold, 0, 255);
                saved.AlphaThreshold = Math.Clamp(saved.AlphaThreshold, 0, 255);
                saved.MaxDimension = Math.Clamp(saved.MaxDimension, 1, SpriteState.MaxDimension);
                saved.Brightness = Math.Clamp(saved.Brightness, -100, 100);
                saved.Contrast = Math.Clamp(saved.Contrast, -100, 100);
                saved.DitherAmount = Math.Clamp(saved.DitherAmount, 0, 100);
                if (!Enum.IsDefined(saved.Preset))
                    saved.Preset = ImportPreset.Default;
                return saved;
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Warning(ex, "ShellViewModel.LoadBitmapImportSettings", new { BitmapImportSettingsFile });
                return defaults;
            }
        }

        private static void SaveBitmapImportSettings(BitmapImportSettings settings)
        {
            try
            {
                Directory.CreateDirectory(UserSettingsDirectory);
                string json = JsonSerializer.Serialize(settings, IndentedJsonOptions);
                File.WriteAllText(BitmapImportSettingsFile, json);
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Warning(ex, "ShellViewModel.SaveBitmapImportSettings", new { BitmapImportSettingsFile });
            }
        }

        // ── Help ──────────────────────────────────────────────────────────

        private void ExecuteOpenDocumentation()
        {
            using var operation = LoggingService.BeginOperation("Shell.OpenDocumentation", new { url = "https://hexprite.com" });
            try
            {
                Process.Start(new ProcessStartInfo("https://hexprite.com") { UseShellExecute = true });
                Logger.Information("Opened documentation URL");
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Error(ex, "ShellViewModel.OpenDocumentation", new { url = "https://hexprite.com" });
                _dialogService.ShowMessage("Could not open the documentation website.");
            }
        }

        private void ExecuteOpenPrivacySettings()
        {
            using var operation = LoggingService.BeginOperation("Shell.OpenPrivacySettings");
            bool saved = _dialogService.ShowPrivacySettingsDialog();
            Logger.Information("Privacy settings dialog closed. Saved={Saved}", saved);
        }

        private void ExecuteOpenKeyboardShortcuts()
        {
            using var operation = LoggingService.BeginOperation("Shell.OpenKeyboardShortcuts");
            Logger.Information("Opening Keyboard Shortcuts cheat sheet dialog");
            _dialogService.ShowKeyboardShortcutsDialog();
        }

        private void ExecuteShowAbout()
        {
            Logger.Information("Opening About dialog");
            _dialogService.ShowAboutDialog();
        }

        /// <summary>
        /// Called when a background or explicit update check finds a newer version.
        /// </summary>
        public void NotifyUpdateAvailable(UpdateInfo update)
        {
            ArgumentNullException.ThrowIfNull(update);
            AvailableUpdate = update;
            var prefs = UserPreferencesService.Get();
            IsUpdateBannerVisible = prefs.DismissedUpdateVersion != update.LatestVersion;
            (ViewReleaseCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (DownloadInstallerCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }

        private async Task ExecuteCheckForUpdatesAsync()
        {
            if (IsCheckingForUpdates)
            {
                return;
            }

            IsCheckingForUpdates = true;
            try
            {
                using var operation = LoggingService.BeginOperation("Shell.CheckForUpdates");
                var update = await _updateService.CheckForUpdateAsync(force: true).ConfigureAwait(true);
                UserPreferencesService.Update(p => p.LastUpdateCheckUtc = DateTimeOffset.UtcNow);

                if (update != null)
                {
                    NotifyUpdateAvailable(update);
                    // Also force banner visibility on explicit manual check
                    IsUpdateBannerVisible = true;
                    UserPreferencesService.Update(p => p.DismissedUpdateVersion = null);
                }
                else
                {
                    _dialogService.ShowMessage(
                        "You're running the latest version of Hexprite.",
                        "Check for Updates",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
            }
            finally
            {
                IsCheckingForUpdates = false;
            }
        }

        private void ExecuteViewRelease()
        {
            if (AvailableUpdate?.ReleasePageUrl is { Length: > 0 } url)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    HandledErrorReporter.Error(ex, "ShellViewModel.ViewRelease", new { url });
                }
            }
        }

        private void ExecuteDownloadInstaller()
        {
            string? url = AvailableUpdate?.InstallerDownloadUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                url = AvailableUpdate?.ReleasePageUrl;
            }

            if (url is { Length: > 0 })
            {
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    HandledErrorReporter.Error(ex, "ShellViewModel.DownloadInstaller", new { url });
                }
            }
        }

        private void ExecuteDismissUpdate()
        {
            IsUpdateBannerVisible = false;
            if (AvailableUpdate != null)
            {
                UserPreferencesService.Update(p =>
                    p.DismissedUpdateVersion = AvailableUpdate.LatestVersion);
            }
        }

        private void ExecuteReportBug()
        {
            using var operation = LoggingService.BeginOperation("Shell.ReportBug");
            BugReportInput? input = _dialogService.ShowBugReportDialog();
            if (input == null)
            {
                Logger.Information("Bug report dialog canceled by user");
                return;
            }

            BugReportResult result = _bugReportService.SubmitReport(input);
            if (result.Success)
            {
                _dialogService.ShowBugReportSuccessDialog(result.Message, result.EventId);
                Logger.Information("Bug report submitted successfully. EventId={EventId}", result.EventId);
                return;
            }

            _dialogService.ShowMessage(result.Message);
            Log.Warning("User-facing bug report submission failed: {Message}", result.Message);
        }

        private void ExecuteSendFeedback()
        {
            using var operation = LoggingService.BeginOperation("Shell.SendFeedback");
            UserFeedbackInput? input = _dialogService.ShowUserFeedbackDialog();
            if (input == null)
            {
                Logger.Information("Feedback dialog canceled by user");
                return;
            }

            FeedbackSubmitResult result = _userFeedbackService.SubmitFeedback(input);
            if (result.Success)
            {
                _dialogService.ShowBugReportSuccessDialog(result.Message, result.EventId, "Feedback sent");
                Logger.Information("Feedback submitted successfully. EventId={EventId}", result.EventId);
                return;
            }

            _dialogService.ShowMessage(result.Message);
            Log.Warning("User-facing feedback submission failed: {Message}", result.Message);
        }

        // ── Theme ─────────────────────────────────────────────────────────

        private void ExecuteSwitchTheme(string? themeName)
        {
            if (themeName != null)
            {
                _themeService.ApplyTheme(themeName);
                Logger.Information("Theme switched to {ThemeName}", themeName);
            }
        }

        private void ExecuteRefreshTheme()
        {
            using var operation = LoggingService.BeginOperation("Shell.RefreshTheme", new { openDocuments = OpenDocuments.Count });
            // Force redraw of all open documents with current theme colors
            foreach (var doc in OpenDocuments)
            {
                if (doc is MainViewModel mvm) mvm.RefreshCanvasColors();
            }

            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }

        // ── Tool Selection ────────────────────────────────────────────────

        private void ExecuteSelectTool(string? toolName)
        {
            if (toolName == null || ActiveDocument == null) return;

            // Determine the target tool mode
            var targetTool = toolName switch
            {
                "Eraser" => ToolMode.Eraser,
                "Dither" => ToolMode.Dither,
                "Fill" => ToolMode.Fill,
                "Marquee" => ToolMode.Marquee,
                "EllipticalMarquee" => ToolMode.EllipticalMarquee,
                "Lasso" => ToolMode.Lasso,
                "Rectangle" => ToolMode.Rectangle,
                "Ellipse" => ToolMode.Ellipse,
                "FilledRectangle" => ToolMode.FilledRectangle,
                "FilledEllipse" => ToolMode.FilledEllipse,
                "Gradient" => ToolMode.Gradient,
                "Line" => ToolMode.Line,
                "Text" => ToolMode.Text,
                "Move" => ToolMode.Move,
                "MagicWand" => ToolMode.MagicWand,
                _ => ToolMode.Pencil,
            };

            if (ActiveDocument is MainViewModel mvm)
            {
                // Commit any in-progress text editing before switching tools
                mvm.StopTextEditing();

                // Let the active document handle per-document cleanup (commit floating pixels, etc.)
                mvm.PrepareForToolChange(targetTool);
            }

            // Set the global tool (this notifies all documents)
            CurrentTool = targetTool;
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private void AddNewDocument(int width, int height, ColorMode colorMode)
        {
            var doc = CreateDocument(width, height, colorMode);
            OpenDocuments.Add(doc);
            ActiveDocument = doc;
            TabAdded?.Invoke(this, doc);
        }

        private MainViewModel CreateDocument(int width, int height, ColorMode colorMode, bool redrawImmediately = true)
        {
            var history = new HistoryService();
            var selection = new SelectionService();

            // Only apply tool preferences for the first document.
            // Subsequent documents inherit the current global tool.
            bool isFirstDocument = OpenDocuments.Count == 0;

            // Create document with deferred controller initialization
            // Controllers will be created and injected after doc is initialized
            // Pass callbacks to access global tool state from ShellViewModel
            var doc = new MainViewModel(
                _codeGen, _drawingService, history, selection,
                _clipboardService, _pixelClipboard, _dialogService, _exportService,
                _importExportService, _hardwarePreview, _serviceProvider.GetRequiredService<IAutosaveService>(),
                () => CurrentTool, t => CurrentTool = t,
                isFirstDocument);

            // Now create document-scoped controllers using the factory
            var previewRenderer = _controllerFactory.CreatePreviewRenderer(doc);
            var toolInput = _controllerFactory.CreateToolInputController(doc, _drawingService, previewRenderer);
            var selectionInput = _controllerFactory.CreateSelectionInputController(doc, selection, _drawingService);

            // Initialize the document with controllers
            doc.InitializeControllers(toolInput, selectionInput, previewRenderer);

            doc.InitializeFonts();
            doc.InitializeGrid(width, height, redrawImmediately);
            doc.SpriteState.ColorMode = colorMode;
            doc.MarkAsClean();
            return doc;
        }

        public void OpenSpritesInTabs(IEnumerable<(string Name, SpriteState Sprite)> sprites)
        {
            if (sprites == null) return;
            OpenSpritesInTabsWithPaths(sprites.Select(s => (s.Name, s.Sprite, (string?)null)));
        }

        public void OpenSpritesInTabs(IEnumerable<SpriteState> sprites, string tabNamePrefix = "Imported")
        {
            if (sprites == null) return;
            int idx = 1;
            var list = new List<(string Name, SpriteState Sprite, string? FilePath)>();
            foreach (var s in sprites)
            {
                list.Add((string.Create(CultureInfo.InvariantCulture, $"{tabNamePrefix} {idx++}"), s, null));
            }
            OpenSpritesInTabsWithPaths(list);
        }

        public void OpenSpritesInTabsWithPaths(IEnumerable<(string Name, SpriteState Sprite, string? FilePath)> sprites)
        {
            if (sprites == null) return;
            OpenSpritesInTabsWithPaths(sprites.Select(s => (s.Name, s.Sprite, s.FilePath, (string?)null, (string?)null, (string?)null)));
        }

        public void OpenSpritesInTabsWithPaths(IEnumerable<(string Name, SpriteState Sprite, string? FilePath, string? ParentPackPath, string? ParentPackName, string? PackEntryName)> sprites)
        {
            if (sprites == null) return;

            var activePack = ActiveDocument as AssetPackViewModel;
            string? fallbackPackPath = activePack?.FilePath;
            string? fallbackPackName = activePack?.MatrixViewModel.PackName;

            MainViewModel? lastDoc = null;
            foreach (var (name, sprite, filePath, parentPackPath, parentPackName, packEntryName) in sprites)
            {
                if (sprite == null) continue;

                var doc = CreateDocument(sprite.Width, sprite.Height, sprite.ColorMode, redrawImmediately: false);
                doc.LoadState(sprite.Clone());

                doc.ParentPackPath = parentPackPath ?? fallbackPackPath;
                doc.ParentPackName = parentPackName ?? fallbackPackName;
                doc.PackEntryName = packEntryName ?? name;

                doc.SetSpriteNameWithoutGenerating(Services.CodeGeneratorService.SanitiseName(name));
                if (!string.IsNullOrEmpty(filePath))
                {
                    doc.FilePath = filePath;
                }
                doc.MarkCodeStale();
                doc.MarkAsClean(); // Fix: Clear dirty state after initial load/rebuild
                doc.RedrawGridFromMemory();

                OpenDocuments.Add(doc);
                lastDoc = doc;
                TabAdded?.Invoke(this, doc);
            }

            if (lastDoc != null)
            {
                ActiveDocument = lastDoc;
                if (lastDoc.IsAnimationEnabled)
                {
                    IsTimelineVisible = true;
                }
            }
        }

        public void OpenSpriteInTab(SpriteState sprite, string title)
        {
            OpenSpriteInTab(sprite, title, filePath: null, parentPackPath: null, parentPackName: null, packEntryName: null);
        }

        public void OpenSpriteInTab(SpriteState sprite, string title, string? filePath)
        {
            OpenSpriteInTab(sprite, title, filePath, parentPackPath: null, parentPackName: null, packEntryName: null);
        }

        public void OpenSpriteInTab(SpriteState sprite, string title, string? filePath, string? parentPackPath, string? parentPackName, string? packEntryName)
        {
            if (sprite == null) return;
            OpenSpritesInTabsWithPaths([(title, sprite, filePath, parentPackPath, parentPackName, packEntryName)]);
        }

        public SpriteState? GetActiveSpriteState()
        {
            if (ActiveDocument is MainViewModel mvm)
                return mvm.SpriteState;

            if (ActiveDocument is AssetPackViewModel apvm)
            {
                var entryName = apvm.MatrixViewModel.SelectedEntry?.Name;
                if (!string.IsNullOrWhiteSpace(entryName) &&
                    apvm.MatrixViewModel.AnimationSprites.TryGetValue(entryName, out var sprite) && sprite != null)
                {
                    return sprite;
                }

                var simSprite = apvm.MatrixViewModel.SimulatorViewModel?.SelectedCandidate?.Sprite;
                if (simSprite != null)
                    return simSprite;

                return apvm.MatrixViewModel.AnimationSprites.Values.FirstOrDefault();
            }

            return null;
        }

        public (string Title, SpriteState Sprite)? GetActiveSprite()
        {
            if (ActiveDocument is MainViewModel mvm && mvm.SpriteState != null)
            {
                string title = string.IsNullOrWhiteSpace(mvm.Title) ? "Animation" : mvm.Title.Trim();
                title = title.TrimStart('*').Trim();
                if (title.EndsWith(".hexel", StringComparison.OrdinalIgnoreCase))
                    title = title[..^6];
                if (title.EndsWith(".hexp", StringComparison.OrdinalIgnoreCase))
                    title = title[..^5];
                if (string.IsNullOrWhiteSpace(title))
                    title = "Animation";
                return (title, mvm.SpriteState);
            }

            if (ActiveDocument is AssetPackViewModel apvm)
            {
                var sprite = GetActiveSpriteState();
                if (sprite != null)
                {
                    string title = apvm.MatrixViewModel.SelectedEntry?.Name
                        ?? apvm.MatrixViewModel.SimulatorViewModel?.SelectedCandidate?.Name
                        ?? apvm.MatrixViewModel.PackName;
                    return (title, sprite);
                }
            }

            return null;
        }

        public bool[]? GetActiveFramePixels(bool animated = false)
        {
            if (ActiveDocument is MainViewModel doc && doc.SpriteState != null)
            {
                int currentFrame = animated && doc.SpriteState.Frames.Count > 1
                    ? doc.CurrentDisplayFrameIndex
                    : doc.SpriteState.ActiveFrameIndex;

                return doc.SpriteState.CompositeFramePixels(Math.Clamp(currentFrame, 0, Math.Max(0, doc.SpriteState.Frames.Count - 1)));
            }

            if (ActiveDocument is AssetPackViewModel apvm)
            {
                // If simulator is active and selected in studio mode, directly provide full 128x64 simulator screen pixels
                if (apvm.MatrixViewModel.SimulatorViewModel != null && apvm.MatrixViewModel.SimulatorViewModel.SelectedCandidate != null)
                {
                    return apvm.MatrixViewModel.SimulatorViewModel.GetScreenPixelsCopy();
                }

                var entryName = apvm.MatrixViewModel.SelectedEntry?.Name;
                if (!string.IsNullOrWhiteSpace(entryName) &&
                    apvm.MatrixViewModel.AnimationSprites.TryGetValue(entryName, out var sprite) && sprite != null)
                {
                    int currentFrame = animated && sprite.Frames.Count > 1
                        ? Math.Clamp(sprite.ActiveFrameIndex, 0, Math.Max(0, sprite.Frames.Count - 1))
                        : 0;

                    return sprite.CompositeFramePixels(currentFrame);
                }

                var firstSprite = apvm.MatrixViewModel.AnimationSprites.Values.FirstOrDefault();
                if (firstSprite != null)
                {
                    int currentFrame = animated && firstSprite.Frames.Count > 1
                        ? Math.Clamp(firstSprite.ActiveFrameIndex, 0, Math.Max(0, firstSprite.Frames.Count - 1))
                        : 0;

                    return firstSprite.CompositeFramePixels(currentFrame);
                }
            }

            return null;
        }

        public IReadOnlyList<(string Title, SpriteState Sprite)> GetAllOpenSprites()
        {
            var list = new List<(string Title, SpriteState Sprite)>();
            foreach (var doc in OpenDocuments)
            {
                if (doc is MainViewModel mvm && mvm.SpriteState != null)
                {
                    string title = string.IsNullOrWhiteSpace(doc.Title) ? "Animation" : doc.Title.Trim();
                    title = title.TrimStart('*').Trim();
                    if (title.EndsWith(".hexel", StringComparison.OrdinalIgnoreCase))
                        title = title[..^6];
                    if (title.EndsWith(".hexp", StringComparison.OrdinalIgnoreCase))
                        title = title[..^5];
                    if (string.IsNullOrWhiteSpace(title))
                        title = "Animation";
                    list.Add((title, mvm.SpriteState));
                }
            }
            return list;
        }

        public IReadOnlyList<(string Title, SpriteState Sprite, string? FilePath)> GetAllOpenSpritesWithPaths()
        {
            var list = new List<(string Title, SpriteState Sprite, string? FilePath)>();
            foreach (var doc in OpenDocuments)
            {
                if (doc is MainViewModel mvm && mvm.SpriteState != null)
                {
                    string title = string.IsNullOrWhiteSpace(doc.Title) ? "Animation" : doc.Title.Trim();
                    title = title.TrimStart('*').Trim();
                    if (title.EndsWith(".hexel", StringComparison.OrdinalIgnoreCase))
                        title = title[..^6];
                    if (title.EndsWith(".hexp", StringComparison.OrdinalIgnoreCase))
                        title = title[..^5];
                    if (string.IsNullOrWhiteSpace(title))
                        title = "Animation";
                    list.Add((title, mvm.SpriteState, mvm.FilePath));
                }
            }
            return list;
        }

        public bool ActivateTabByTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return false;

            string cleanTarget = title.Trim().TrimStart('*').Trim();

            foreach (var doc in OpenDocuments)
            {
                string docTitle = string.IsNullOrWhiteSpace(doc.Title) ? string.Empty : doc.Title.Trim().TrimStart('*').Trim();
                if (docTitle.EndsWith(".hexel", StringComparison.OrdinalIgnoreCase))
                    docTitle = docTitle[..^6];
                if (docTitle.EndsWith(".hexp", StringComparison.OrdinalIgnoreCase))
                    docTitle = docTitle[..^5];

                if (string.Equals(docTitle, cleanTarget, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(doc.Title?.Trim().TrimStart('*').Trim(), cleanTarget, StringComparison.OrdinalIgnoreCase))
                {
                    ActiveDocument = doc;
                    return true;
                }
            }
            return false;
        }

        public bool RenameTab(string oldTitle, string newTitle)
        {
            if (string.IsNullOrWhiteSpace(oldTitle) || string.IsNullOrWhiteSpace(newTitle)) return false;
            string cleanOld = oldTitle.Trim().TrimStart('*').Trim();
            string cleanNew = newTitle.Trim().TrimStart('*').Trim();
            if (cleanOld.EndsWith(".hexel", StringComparison.OrdinalIgnoreCase)) cleanOld = cleanOld[..^6];
            if (cleanOld.EndsWith(".hexp", StringComparison.OrdinalIgnoreCase)) cleanOld = cleanOld[..^5];
            if (cleanNew.EndsWith(".hexel", StringComparison.OrdinalIgnoreCase)) cleanNew = cleanNew[..^6];
            if (cleanNew.EndsWith(".hexp", StringComparison.OrdinalIgnoreCase)) cleanNew = cleanNew[..^5];

            foreach (var doc in OpenDocuments)
            {
                if (doc is MainViewModel mvm)
                {
                    string docTitle = string.IsNullOrWhiteSpace(doc.Title) ? string.Empty : doc.Title.Trim().TrimStart('*').Trim();
                    if (docTitle.EndsWith(".hexel", StringComparison.OrdinalIgnoreCase)) docTitle = docTitle[..^6];
                    if (docTitle.EndsWith(".hexp", StringComparison.OrdinalIgnoreCase)) docTitle = docTitle[..^5];

                    if (string.Equals(docTitle, cleanOld, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(mvm.SpriteName, cleanOld, StringComparison.OrdinalIgnoreCase))
                    {
                        mvm.SetSpriteNameWithoutGenerating(cleanNew);
                        if (!string.IsNullOrEmpty(mvm.FilePath))
                        {
                            string dir = System.IO.Path.GetDirectoryName(mvm.FilePath) ?? string.Empty;
                            string ext = System.IO.Path.GetExtension(mvm.FilePath);
                            if (string.IsNullOrEmpty(ext)) ext = ".hexp";
                            mvm.FilePath = System.IO.Path.Combine(dir, cleanNew + ext);
                            mvm.IsDirty = true;
                        }
                        return true;
                    }
                }
            }
            return false;
        }

        private void RaiseActiveTabChanged()
        {
            NotifyAllDocumentCommandsCanExecuteChanged();
            ActiveTabChanged?.Invoke(this, EventArgs.Empty);
        }

        private void NotifyAllDocumentCommandsCanExecuteChanged()
        {
            OnPropertyChanged(nameof(HasOpenDocument));
            (SaveCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (SaveAsCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (ResizeCanvasCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (CopyExportCodeMenuCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (ExportAsMenuCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (ExportFlipperMenuCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (DeployFlipperUsbCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (OpenFlipperMatrixSimulatorCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (OpenDisplaySimulationCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (OpenCodeViewerCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (ApplyFlipperTemplateCommand as RelayCommand<string>)?.NotifyCanExecuteChanged();
            (ExportXbmMenuCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (CloseTabCommand as RelayCommand<IDocumentTab>)?.NotifyCanExecuteChanged();
            (CloseAllTabsCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (CloseOtherTabsCommand as RelayCommand<IDocumentTab>)?.NotifyCanExecuteChanged();

            if (!HasOpenDocument)
            {
                RefreshRecentFiles();
            }
        }

        public void RefreshRecentFiles()
        {
            RecentFiles.Clear();
            WelcomeRecentFiles.Clear();
            var recents = UserPreferencesService.GetRecentFiles(pruneMissing: false);
            foreach (var file in recents)
            {
                RecentFiles.Add(new RecentFileItem(file));
            }
            foreach (var item in RecentFiles.Take(5))
            {
                WelcomeRecentFiles.Add(item);
            }
            OnPropertyChanged(nameof(HasRecentFiles));
        }
    }
}
