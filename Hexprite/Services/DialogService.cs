using System.Windows;
using Hexprite.Core;
using Hexprite.Views;
using Microsoft.Win32;

namespace Hexprite.Services
{
    /// <summary>
    /// Provides methods to show standard dialogs (open/save file, message boxes, settings).
    /// </summary>
    public class DialogService(
        IFileImportExportService? importExportService = null,
        ICodeGeneratorService? codeGen = null,
        IWorkspaceTabService? tabService = null,
        IHexpriteShortcutManager? shortcutManager = null) : IDialogService
    {
        private readonly IFileImportExportService? _importExportService = importExportService;
        private readonly ICodeGeneratorService? _codeGen = codeGen;
        private readonly IWorkspaceTabService? _tabService = tabService;
        private readonly IHexpriteShortcutManager? _shortcutManager = shortcutManager;

        /// <summary>Shows a message box.</summary>
        public void ShowMessage(string message)
        {
            MessageDialog.Show(message);
        }

        public void ShowMessage(string message, string title, MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
        {
            MessageDialog.Show(message, title, button, icon);
        }

        public bool ShowConfirmation(string message, string title)
        {
            return MessageDialog.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        public GlobalLayerLocalizeMode? ShowGlobalLayerLocalizeDialog(bool canRestore)
        {
            if (!canRestore)
            {
                return MessageDialog.Show(
                    "This layer has no saved per-frame content. Localize it by copying the current global content to every frame?",
                    "Localize Global Layer", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes
                    ? GlobalLayerLocalizeMode.CloneGlobalContent
                    : null;
            }

            var result = MessageDialog.Show(
                "Choose Yes to restore the frame contents from before this layer became global. Choose No to keep the current global content in every frame.",
                "Localize Global Layer", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            return result switch
            {
                MessageBoxResult.Yes => GlobalLayerLocalizeMode.RestorePreviousContent,
                MessageBoxResult.No => GlobalLayerLocalizeMode.CloneGlobalContent,
                _ => null,
            };
        }

        /// <summary>Opens dialog for creating a new document.</summary>
        public (int Width, int Height, ColorMode ColorMode, DocumentMode DocumentMode)? ShowNewDocumentDialog()
        {
            var dlg = new NewCanvasDialog { Owner = GetActiveWindow() };
            if (dlg.ShowDialog() == true)
                return (dlg.CanvasWidth, dlg.CanvasHeight, dlg.ColorMode, dlg.DocumentMode);
            return null;
        }

        /// <summary>Opens dialog for resizing the canvas.</summary>
        public (int Width, int Height, ResizeAnchor Anchor)? ShowResizeCanvasDialog(int currentWidth, int currentHeight)
        {
            var dlg = new ResizeCanvasDialog(currentWidth, currentHeight)
            {
                Owner = GetActiveWindow(),
            };
            if (dlg.ShowDialog() == true && dlg.Result.HasValue)
                return dlg.Result.Value;
            return null;
        }

        /// <summary>Opens a file selection dialog.</summary>
        public string? ShowOpenFileDialog(string filter, string title)
        {
            var dialog = new OpenFileDialog
            {
                Filter = filter,
                Title = title,
            };
            var activeWin = GetActiveWindow();
            return (activeWin != null ? dialog.ShowDialog(activeWin) : dialog.ShowDialog()) == true ? dialog.FileName : null;
        }

        /// <summary>Opens a file selection dialog supporting multiple files.</summary>
        public string[]? ShowOpenFilesDialog(string filter, string title)
        {
            var dialog = new OpenFileDialog
            {
                Filter = filter,
                Title = title,
                Multiselect = true,
            };
            var activeWin = GetActiveWindow();
            return (activeWin != null ? dialog.ShowDialog(activeWin) : dialog.ShowDialog()) == true ? dialog.FileNames : null;
        }

        /// <summary>Opens a folder selection dialog.</summary>
        public string? ShowOpenFolderDialog(string title)
        {
            var dialog = new OpenFolderDialog
            {
                Title = title,
            };
            var activeWin = GetActiveWindow();
            return (activeWin != null ? dialog.ShowDialog(activeWin) : dialog.ShowDialog()) == true ? dialog.FolderName : null;
        }

        /// <summary>Opens a save file dialog.</summary>
        public string? ShowSaveFileDialog(string filter, string title, string defaultExt)
        {
            var dialog = new SaveFileDialog
            {
                Filter = filter,
                Title = title,
                DefaultExt = defaultExt,
            };
            var activeWin = GetActiveWindow();
            return (activeWin != null ? dialog.ShowDialog(activeWin) : dialog.ShowDialog()) == true ? dialog.FileName : null;
        }

        /// <summary>Prompts user for unsaved changes.</summary>
        public bool? ShowUnsavedChangesDialog(string documentName, bool isLinkedSource = false)
        {
            string message = isLinkedSource
                ? $"\"{documentName}\" is linked to a source file.\n\nDo you want to update the linked source file before closing?"
                : $"Save changes to \"{documentName}\"?";

            var result = MessageDialog.Show(
                message,
                isLinkedSource ? "Update Linked Source" : "Unsaved Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            return result switch
            {
                MessageBoxResult.Yes => true,
                MessageBoxResult.No => false,
                _ => null, // Cancel
            };
        }

        /// <summary>Shows about dialog.</summary>
        public void ShowAboutDialog()
        {
            var dlg = new AboutDialog { Owner = GetActiveWindow() };
            dlg.ShowDialog();
        }

        /// <summary>Shows keyboard shortcuts cheat sheet dialog.</summary>
        public void ShowKeyboardShortcutsDialog()
        {
            var dlg = new KeyboardShortcutsDialog(_shortcutManager) { Owner = GetActiveWindow() };
            dlg.ShowDialog();
        }

        /// <summary>Opens dialog to import a sprite from source code.</summary>
        public (int Width, int Height, string Code, string? SpriteName, ExportFormat Format, bool Invert)? ShowImportFromCodeDialog()
        {
            var dlg = new ImportFromCodeDialog { Owner = GetActiveWindow() };
            if (dlg.ShowDialog() == true && dlg.Result.HasValue)
                return dlg.Result.Value;
            return null;
        }

        public (string FilePath, System.Collections.Generic.List<DetectedSprite> SelectedSprites)? ShowImportFromFileDialog(string? initialFilePath = null)
        {
            var importService = _importExportService ?? new FileImportExportService();
            var generatorService = _codeGen ?? new CodeGeneratorService(new Hexprite.Services.Compression.CompressionService());
            var dlg = new ImportFromFileDialog(importService, generatorService, initialFilePath) { Owner = GetActiveWindow() };
            if (dlg.ShowDialog() == true)
                return dlg.Result;
            return null;
        }
        /// <summary>Opens dialog to import from bitmap.</summary>
        public BitmapImportSettings? ShowImportBitmapDialog(string fileName, BitmapImportSettings initialSettings)
        {
            var dlg = new ImportBitmapDialog(fileName, initialSettings) { Owner = GetActiveWindow() };
            if (dlg.ShowDialog() == true)
                return dlg.Result;
            return null;
        }

        /// <summary>Opens dialog to import animation.</summary>
        public AnimationImportSettings? ShowImportAnimationDialog(string fileName, AnimationImportSettings initialSettings)
        {
            var dlg = new Hexprite.Views.ImportAnimationDialog(fileName, initialSettings) { Owner = GetActiveWindow() };
            if (dlg.ShowDialog() == true)
                return dlg.Result;
            return null;
        }

        /// <summary>Opens Sprite Sheet Slicer Studio window.</summary>
        public void ShowSpriteSheetSlicerDialog(
            System.Windows.Media.Imaging.BitmapSource? initialImage = null,
            SpriteState? initialSprite = null,
            SpriteSheetSliceSettings? initialSettings = null,
            string? initialFilePath = null)
        {
            var resolvedTabService = _tabService ?? TryResolveMainWindowTabService();
            var vm = new Hexprite.ViewModels.SpriteSheetSlicerViewModel(
                tabService: resolvedTabService,
                dialogService: this,
                initialSprite: initialSprite,
                initialSettings: initialSettings);

            if (!string.IsNullOrWhiteSpace(initialFilePath) && System.IO.File.Exists(initialFilePath))
            {
                vm.LoadImageFromFile(initialFilePath);
            }
            else if (initialImage != null)
            {
                vm.LoadImage(initialImage, initialFilePath);
            }

            var dlg = new Hexprite.Views.SpriteSheetSlicerWindow(vm);
            var activeWin = GetActiveWindow();
            if (activeWin != null && activeWin.IsLoaded)
            {
                dlg.Owner = activeWin;
            }
            dlg.ShowDialog();
        }
        /// <summary>Opens dialog for bug reports.</summary>
        public BugReportInput? ShowBugReportDialog()
        {
            var dlg = new ReportBugDialog { Owner = GetActiveWindow() };
            return dlg.ShowDialog() == true ? dlg.Result : null;
        }

        /// <summary>Shows dialog after successful bug report submission.</summary>
        public void ShowBugReportSuccessDialog(string message, string? reportId, string? successWindowTitle = null)
        {
            var dlg = new BugReportSuccessDialog(message, reportId, successWindowTitle)
            {
                Owner = GetActiveWindow(),
            };
            dlg.ShowDialog();
        }

        /// <summary>Opens dialog for user feedback.</summary>
        public UserFeedbackInput? ShowUserFeedbackDialog()
        {
            var dlg = new UserFeedbackDialog { Owner = GetActiveWindow() };
            return dlg.ShowDialog() == true ? dlg.Result : null;
        }

        /// <summary>Opens dialog for privacy settings.</summary>
        public bool ShowPrivacySettingsDialog()
        {
            var dlg = new PrivacySettingsDialog { Owner = GetActiveWindow() };
            return dlg.ShowDialog() == true;
        }

        /// <summary>Opens dialog for image export settings.</summary>
        public ImageExportSettings? ShowExportImageDialog(ImageExportSettings initialSettings, SpriteState spriteState)
        {
            var dlg = new ExportDialog(initialSettings, spriteState) { Owner = GetActiveWindow() };
            return dlg.ShowDialog() == true ? dlg.Result : null;
        }

        /// <summary>Opens dialog for outline settings with optional live preview.</summary>
        public OutlineSettings? ShowOutlineDialog(Action<OutlineSettings>? previewCallback = null)
        {
            var dlg = new OutlineDialog(previewCallback) { Owner = GetActiveWindow() };
            if (dlg.ShowDialog() == true)
                return dlg.Result;
            return null;
        }

        /// <summary>Opens dialog for configuring hardware preview wiring and pins.</summary>
        public bool ShowHardwarePreviewWiringDialog(HardwarePreviewWiringConfig config, int baudRate = 115200, int canvasWidth = 0, int canvasHeight = 0, IHardwarePreviewService? hardwarePreview = null)
        {
            var vm = new ViewModels.HardwarePreviewWiringViewModel(config, baudRate, canvasWidth, canvasHeight, dialogService: this, hardwarePreview: hardwarePreview);
            var dlg = new HardwarePreviewWiringDialog(vm) { Owner = GetActiveWindow() };
            if (dlg.ShowDialog() == true)
            {
                var result = vm.GetConfig();
                config.BoardPreset = result.BoardPreset;
                config.InterfaceType = result.InterfaceType;
                config.DisplayModel = result.DisplayModel;
                config.SdaPin = result.SdaPin;
                config.SclPin = result.SclPin;
                config.I2cAddress = result.I2cAddress;
                config.UseSoftwareI2c = result.UseSoftwareI2c;
                config.CsPin = result.CsPin;
                config.DcPin = result.DcPin;
                config.RstPin = result.RstPin;
                config.ClkPin = result.ClkPin;
                config.MosiPin = result.MosiPin;
                config.BaudRate = result.BaudRate;
                return true;
            }
            return false;
        }

        private static Window? GetActiveWindow()
        {
            try
            {
                if (Application.Current != null)
                {
                    return System.Linq.Enumerable.FirstOrDefault(
                               System.Linq.Enumerable.OfType<Window>(Application.Current.Windows),
                               w => w != null && w.IsActive && w.IsLoaded)
                           ?? Application.Current.MainWindow;
                }
            }
            catch
            {
                // Fallback for non-UI/test environments
            }
            return null;
        }

        private static IWorkspaceTabService? TryResolveMainWindowTabService()
        {
            try
            {
                if (Application.Current?.Dispatcher != null && Application.Current.Dispatcher.CheckAccess())
                {
                    return Application.Current.MainWindow?.DataContext as IWorkspaceTabService;
                }
            }
            catch
            {
                // Ignore cross-thread or headless test exceptions
            }
            return null;
        }
    }
}
