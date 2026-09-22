using System;
using System.Collections.Generic;
using System.Windows;
using Hexprite.Core;

namespace Hexprite.Services
{
    public sealed class BugReportInput
    {
        public string Summary { get; set; } = string.Empty;
        public string StepsToReproduce { get; set; } = string.Empty;
        public string ExpectedBehavior { get; set; } = string.Empty;
        public string ActualBehavior { get; set; } = string.Empty;
        public string? ContactEmail { get; set; }
        public bool IncludeContactEmail { get; set; }
        public bool IncludeRecentLogs { get; set; } = true;
    }

    public sealed class UserFeedbackInput
    {
        public string Category { get; set; } = "General";
        public string Message { get; set; } = string.Empty;
        public string? ContactEmail { get; set; }
        public bool IncludeContactEmail { get; set; }
        public bool IncludeRecentLogs { get; set; }
    }

    public interface IClipboardService
    {
        void SetText(string text);
    }

    public interface IDialogService
    {
        void ShowMessage(string message);
        void ShowMessage(string message, string title, MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None);

        /// <summary>
        /// Shows a confirmation dialog. Returns true if Yes/OK is clicked, false otherwise.
        /// </summary>
        bool ShowConfirmation(string message, string title);

        /// <summary>
        /// Chooses how to localize a global layer. Null means the conversion was cancelled.
        /// </summary>
        GlobalLayerLocalizeMode? ShowGlobalLayerLocalizeDialog(bool canRestore);

        /// <summary>
        /// Shows the "New Document" dialog and returns (width, height, colorMode, documentMode) if the user
        /// clicks Create, or null if they cancel.
        /// </summary>
        (int Width, int Height, ColorMode ColorMode, DocumentMode DocumentMode)? ShowNewDocumentDialog();

        /// <summary>
        /// Shows the "Resize Canvas" dialog seeded with the current dimensions.
        /// Returns (width, height, anchor) if accepted, null if cancelled.
        /// </summary>
        (int Width, int Height, ResizeAnchor Anchor)? ShowResizeCanvasDialog(int currentWidth, int currentHeight);

        /// <summary>
        /// Shows an "Open File" dialog. Returns the selected file path, or null if cancelled.
        /// </summary>
        string? ShowOpenFileDialog(string filter, string title);

        /// <summary>
        /// Shows an "Open Files" dialog supporting multiple selection. Returns the selected file paths, or null if cancelled.
        /// </summary>
        string[]? ShowOpenFilesDialog(string filter, string title);

        /// <summary>
        /// Shows an "Open Folder" dialog. Returns the selected folder path, or null if cancelled.
        /// </summary>
        string? ShowOpenFolderDialog(string title);

        /// <summary>
        /// Shows a "Save File" dialog. Returns the selected file path, or null if cancelled.
        /// </summary>
        string? ShowSaveFileDialog(string filter, string title, string defaultExt);

        /// <summary>
        /// Shows an "Unsaved changes" confirmation. Returns true (save), false (discard),
        /// or null (cancel/abort the operation).
        /// </summary>
        bool? ShowUnsavedChangesDialog(string documentName, bool isLinkedSource = false);

        /// <summary>
        /// Shows the "About Hexprite" dialog.
        /// </summary>
        void ShowAboutDialog();

        /// <summary>
        /// Shows the "Keyboard Shortcuts" cheat sheet dialog.
        /// </summary>
        void ShowKeyboardShortcutsDialog();

        /// <summary>
        /// Shows the "Import from Code" dialog.
        /// Returns (width, height, code, spriteName, format, invert) if the user clicks Import, or null if cancelled.
        /// </summary>
        (int Width, int Height, string Code, string? SpriteName, ExportFormat Format, bool Invert)? ShowImportFromCodeDialog();

        /// <summary>
        /// Shows the "Import from File" dialog.
        /// Returns a tuple containing the selected file path and a list of detected sprites to import, or null if cancelled.
        /// </summary>
        (string FilePath, List<DetectedSprite> SelectedSprites)? ShowImportFromFileDialog(string? initialFilePath = null);

        /// <summary>
        /// Shows the bitmap import settings dialog.
        /// Returns selected settings, or null if cancelled.
        /// </summary>
        BitmapImportSettings? ShowImportBitmapDialog(string fileName, BitmapImportSettings initialSettings);
        AnimationImportSettings? ShowImportAnimationDialog(string fileName, AnimationImportSettings initialSettings);
        void ShowSpriteSheetSlicerDialog(System.Windows.Media.Imaging.BitmapSource? initialImage = null, SpriteState? initialSprite = null, SpriteSheetSliceSettings? initialSettings = null, string? initialFilePath = null);

        /// <summary>
        /// Shows the "Report a Bug" dialog and returns user-entered data, or null if cancelled.
        /// </summary>
        BugReportInput? ShowBugReportDialog();

        /// <summary>
        /// Shows confirmation after a bug report was submitted, with optional copyable reference ID.
        /// </summary>
        /// <param name="successWindowTitle">Optional window title; default is for bug reports.</param>
        void ShowBugReportSuccessDialog(string message, string? reportId, string? successWindowTitle = null);



        /// <summary>
        /// Shows the "Send Feedback" dialog and returns user-entered data, or null if cancelled.
        /// </summary>
        UserFeedbackInput? ShowUserFeedbackDialog();

        /// <summary>
        /// Shows the Privacy Settings dialog and persists changes when accepted.
        /// </summary>
        bool ShowPrivacySettingsDialog();

        /// <summary>
        /// Shows the Export Image dialog. Returns the configured settings or null if cancelled.
        /// </summary>
        ImageExportSettings? ShowExportImageDialog(ImageExportSettings initialSettings, SpriteState spriteState);

        /// <summary>
        /// Shows the Outline settings dialog. Returns configured outline settings or null if cancelled.
        /// The optional <paramref name="previewCallback"/> is invoked on every setting change for live preview.
        /// </summary>
        OutlineSettings? ShowOutlineDialog(Action<OutlineSettings>? previewCallback = null);

        /// <summary>
        /// Shows the Hardware Preview Wiring and Pinout configuration dialog.
        /// Returns true if accepted, false if cancelled.
        /// </summary>
        bool ShowHardwarePreviewWiringDialog(HardwarePreviewWiringConfig config, int baudRate = 115200, int canvasWidth = 0, int canvasHeight = 0, IHardwarePreviewService? hardwarePreview = null);
    }

}
