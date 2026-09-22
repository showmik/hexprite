using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
public sealed class LinkedSourceRegressionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileImportExportService _fileSvc;
    private readonly CodeGeneratorService _codeGen;
    private readonly Mock<IDialogService> _dialogMock;
    private readonly Mock<IAutosaveService> _autosaveMock;
    private readonly ShellViewModel _shell;

    public LinkedSourceRegressionTests()
    {
        WpfTestHelper.EnsureApplication();
        _tempDir = Path.Combine(Path.GetTempPath(), "HexpriteRegression_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);

        _fileSvc = new FileImportExportService();
        _codeGen = new CodeGeneratorService();
        _dialogMock = new Mock<IDialogService>();
        _autosaveMock = new Mock<IAutosaveService>();

        var drawingMock = new Mock<IDrawingService>();
        var clipboardMock = new Mock<IClipboardService>();
        var pixelClipboardMock = new Mock<IPixelClipboardService>();
        var themeMock = new Mock<IThemeService>();
        var bugReportMock = new Mock<IBugReportService>();
        var feedbackMock = new Mock<IUserFeedbackService>();
        var controllerFactory = new ControllerFactory();
        var exportMock = new Mock<IExportService>();
        var hardwarePreviewMock = new Mock<IHardwarePreviewService>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IAutosaveService))).Returns(_autosaveMock.Object);

        _shell = new ShellViewModel(
            _codeGen,
            drawingMock.Object,
            clipboardMock.Object,
            pixelClipboardMock.Object,
            _dialogMock.Object,
            themeMock.Object,
            bugReportMock.Object,
            feedbackMock.Object,
            controllerFactory,
            exportMock.Object,
            _fileSvc,
            hardwarePreviewMock.Object,
            serviceProviderMock.Object);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string WriteTempFile(string name, string content)
    {
        string path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static void FlushDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new DispatcherOperationCallback(f =>
        {
            ((DispatcherFrame)f).Continue = false;
            return null;
        }), frame);
        Dispatcher.PushFrame(frame);
    }

    // ── 1. ShellViewModel Batch Commands CanExecute ──────────────────────

    [Fact]
    public void UpdateAllLinkedSourcesCommand_CanExecute_ReflectsPresenceOfLinkedDocuments()
    {
        _shell.OpenDocuments.Clear();

        Assert.False(_shell.UpdateAllLinkedSourcesCommand.CanExecute(null));
        Assert.False(_shell.PullAllLinkedSourcesCommand.CanExecute(null));
        Assert.False(_shell.RestoreAllLinkedSourcesCommand.CanExecute(null));
        Assert.False(_shell.HasAnyLinkedDocuments);

        // Open unlinked document
        _shell.NewDocumentCommand.Execute("16x16");
        var doc = (MainViewModel)_shell.ActiveDocument!;
        Assert.False(_shell.UpdateAllLinkedSourcesCommand.CanExecute(null));
        Assert.False(_shell.HasAnyLinkedDocuments);

        // Link document
        string path = WriteTempFile("spr1.c", "const uint8_t spr1[] = { 0x00 };");
        doc.SpriteState.LinkedSourceFile = path;
        doc.SpriteState.LinkedVariableName = "spr1";
        doc.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
        doc.NotifyLinkChanged();

        Assert.True(_shell.UpdateAllLinkedSourcesCommand.CanExecute(null));
        Assert.True(_shell.PullAllLinkedSourcesCommand.CanExecute(null));
        Assert.True(_shell.RestoreAllLinkedSourcesCommand.CanExecute(null));
        Assert.True(_shell.HasAnyLinkedDocuments);

        // Unlink document
        doc.SpriteState.LinkedSourceFile = null;
        doc.NotifyLinkChanged();

        Assert.False(_shell.UpdateAllLinkedSourcesCommand.CanExecute(null));
        Assert.False(_shell.PullAllLinkedSourcesCommand.CanExecute(null));
        Assert.False(_shell.RestoreAllLinkedSourcesCommand.CanExecute(null));
        Assert.False(_shell.HasAnyLinkedDocuments);
    }

    // ── 2. UpdateAllLinkedSources Batch Execution ────────────────────────

    [Fact]
    public async Task UpdateAllLinkedSourcesCommand_MultipleLinkedDocuments_UpdatesAllAndReportsStatus()
    {
        _shell.OpenDocuments.Clear();

        string path1 = WriteTempFile("spr1.c", "const uint8_t spr1[] = { 0x00, 0x00 };");
        _shell.NewDocumentCommand.Execute("16x16");
        var doc1 = (MainViewModel)_shell.ActiveDocument!;
        doc1.SpriteState.LinkedSourceFile = path1;
        doc1.SpriteState.LinkedVariableName = "spr1";
        doc1.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
        doc1.NotifyLinkChanged();
        doc1.SpriteState.Pixels[0] = true;

        string path2 = WriteTempFile("spr2.c", "const uint8_t spr2[] = { 0x00, 0x00 };");
        _shell.NewDocumentCommand.Execute("16x16");
        var doc2 = (MainViewModel)_shell.ActiveDocument!;
        doc2.SpriteState.LinkedSourceFile = path2;
        doc2.SpriteState.LinkedVariableName = "spr2";
        doc2.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
        doc2.NotifyLinkChanged();
        doc2.SpriteState.Pixels[0] = true;

        // Unlinked document 3
        _shell.NewDocumentCommand.Execute("16x16");
        var doc3 = (MainViewModel)_shell.ActiveDocument!;

        _shell.ActiveDocument = doc1;

        await _shell.ExecuteUpdateAllLinkedSourcesAsync();

        string content1 = File.ReadAllText(path1);
        string content2 = File.ReadAllText(path2);
        Assert.Contains("0x80", content1);
        Assert.Contains("0x80", content2);
        Assert.Equal("✓ Updated 2 linked sources", doc1.StatusMessage);
        Assert.False(doc3.IsLinked);
    }

    // ── 3. Partial Failure Tolerance ─────────────────────────────────────

    [Fact]
    public async Task UpdateAllLinkedSourcesCommand_OneDocumentFails_ContinuesUpdatingRemainingDocuments()
    {
        _shell.OpenDocuments.Clear();

        // Doc 1 has an invalid / missing directory
        _shell.NewDocumentCommand.Execute("16x16");
        var doc1 = (MainViewModel)_shell.ActiveDocument!;
        doc1.SpriteState.LinkedSourceFile = Path.Combine(_tempDir, "missing_dir", "missing.c");
        doc1.SpriteState.LinkedVariableName = "spr1";
        doc1.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
        doc1.NotifyLinkChanged();
        doc1.SpriteState.Pixels[0] = true;

        // Doc 2 has a valid file
        string path2 = WriteTempFile("spr2.c", "const uint8_t spr2[] = { 0x00, 0x00 };");
        _shell.NewDocumentCommand.Execute("16x16");
        var doc2 = (MainViewModel)_shell.ActiveDocument!;
        doc2.SpriteState.LinkedSourceFile = path2;
        doc2.SpriteState.LinkedVariableName = "spr2";
        doc2.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
        doc2.NotifyLinkChanged();
        doc2.SpriteState.Pixels[0] = true;

        _shell.ActiveDocument = doc2;

        await _shell.ExecuteUpdateAllLinkedSourcesAsync();

        string content2 = File.ReadAllText(path2);
        Assert.Contains("0x80", content2);
        Assert.Equal("✓ Updated 1 linked source", doc2.StatusMessage);
    }

    // ── 4. PullAllLinkedSources Confirmation Cancelled ───────────────────

    [Fact]
    public async Task PullAllLinkedSourcesCommand_UserCancelsConfirmation_DoesNotPull()
    {
        _shell.OpenDocuments.Clear();

        string path = WriteTempFile("spr.c", "const uint8_t spr[] = { 0x00, 0x00 };");
        _shell.NewDocumentCommand.Execute("16x16");
        var doc = (MainViewModel)_shell.ActiveDocument!;
        doc.SpriteState.LinkedSourceFile = path;
        doc.SpriteState.LinkedVariableName = "spr";
        doc.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
        doc.NotifyLinkChanged();

        // External file changes to 0x80 (pixel 0 = true)
        File.WriteAllText(path, "const uint8_t spr[] = { 0x80, 0x00 };");

        _dialogMock.Setup(d => d.ShowConfirmation(
            It.Is<string>(s => s.Contains("Pull external changes")),
            It.IsAny<string>())).Returns(false);

        await _shell.ExecutePullAllLinkedSourcesAsync();

        Assert.False(doc.SpriteState.Pixels[0], "Canvas pixels should remain unchanged when user cancels pull");
    }

    // ── 5. PullAllLinkedSources Confirmation Accepted ────────────────────

    [Fact]
    public async Task PullAllLinkedSourcesCommand_UserConfirms_PullsAllLinkedDocuments()
    {
        _shell.OpenDocuments.Clear();

        string content = """
            #define SPR_WIDTH 16
            #define SPR_HEIGHT 16
            const uint8_t spr[] = { 0x00, 0x00 };
            """;
        string path = WriteTempFile("spr.c", content);
        _shell.NewDocumentCommand.Execute("16x16");
        var doc = (MainViewModel)_shell.ActiveDocument!;
        doc.SpriteState.LinkedSourceFile = path;
        doc.SpriteState.LinkedVariableName = "spr";
        doc.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
        doc.NotifyLinkChanged();

        // External file changes to 0x80 (pixel 0 = true)
        string updatedContent = """
            #define SPR_WIDTH 16
            #define SPR_HEIGHT 16
            const uint8_t spr[] = { 0x80, 0x00 };
            """;
        File.WriteAllText(path, updatedContent);

        _dialogMock.Setup(d => d.ShowConfirmation(
            It.Is<string>(s => s.Contains("Pull external changes")),
            It.IsAny<string>())).Returns(true);

        await _shell.ExecutePullAllLinkedSourcesAsync();

        Assert.True(doc.SpriteState.Pixels[0], "Canvas pixels must update from external file on pull");
        Assert.Equal("✓ Pulled 1 linked source", doc.StatusMessage);
    }

    // ── 6. RestoreAllLinkedSources Confirmation Cancelled ────────────────

    [Fact]
    public async Task RestoreAllLinkedSourcesCommand_UserCancelsConfirmation_DoesNotRestore()
    {
        _shell.OpenDocuments.Clear();

        string original = "const uint8_t spr[] = { 0x00, 0x00 };";
        string path = WriteTempFile("spr.c", original);

        _shell.NewDocumentCommand.Execute("16x16");
        var doc = (MainViewModel)_shell.ActiveDocument!;
        doc.SpriteState.LinkedSourceFile = path;
        doc.SpriteState.LinkedVariableName = "spr";
        doc.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
        doc.NotifyLinkChanged();

        // Update creates backup
        doc.SpriteState.Pixels[0] = true;
        await doc.ExecuteUpdateLinkedSourceAsync();

        // Modify file externally
        File.WriteAllText(path, "const uint8_t spr[] = { 0xFF, 0xFF };");

        _dialogMock.Setup(d => d.ShowConfirmation(
            It.Is<string>(s => s.Contains("Restore")),
            It.IsAny<string>())).Returns(false);

        await _shell.ExecuteRestoreAllLinkedSourcesAsync();

        string diskContent = File.ReadAllText(path);
        Assert.Contains("0xFF, 0xFF", diskContent);
    }

    // ── 7. RestoreAllLinkedSources Restores Without Per-Doc Confirmation ─

    [Fact]
    public async Task RestoreAllLinkedSourcesCommand_UserConfirms_RestoresAllWithoutIndividualPrompts()
    {
        _shell.OpenDocuments.Clear();

        string original = "const uint8_t spr[] = { 0x00, 0x00 };";
        string path = WriteTempFile("spr.c", original);

        _shell.NewDocumentCommand.Execute("16x16");
        var doc = (MainViewModel)_shell.ActiveDocument!;
        doc.SpriteState.LinkedSourceFile = path;
        doc.SpriteState.LinkedVariableName = "spr";
        doc.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
        doc.NotifyLinkChanged();

        // Update creates backup
        doc.SpriteState.Pixels[0] = true;
        await doc.ExecuteUpdateLinkedSourceAsync();

        // Overwrite file on disk
        File.WriteAllText(path, "const uint8_t spr[] = { 0xAA, 0xAA };");

        _dialogMock.Setup(d => d.ShowConfirmation(
            It.Is<string>(s => s.Contains("Restore")),
            It.IsAny<string>())).Returns(true);

        await _shell.ExecuteRestoreAllLinkedSourcesAsync();

        string restoredContent = File.ReadAllText(path);
        Assert.Contains("0x00, 0x00", restoredContent);

        // Verify that only the single batch confirmation dialog was shown, not individual doc confirmations
        _dialogMock.Verify(d => d.ShowConfirmation(
            It.Is<string>(s => s.Contains("Restore 1 linked source")),
            "Restore All Linked Sources"), Times.Once);
    }

    // ── 8. Undo/Redo Lifecycle of UnlinkSourceCommand ────────────────────

    [Fact]
    public void UnlinkSourceCommand_Undo_RestoresLinkedStateAndWatcher()
    {
        _shell.OpenDocuments.Clear();
        string path = WriteTempFile("unlink_test.c", "const uint8_t spr[] = { 0x00, 0x00 };");

        _shell.NewDocumentCommand.Execute("16x16");
        var doc = (MainViewModel)_shell.ActiveDocument!;
        doc.SpriteState.LinkedSourceFile = path;
        doc.SpriteState.LinkedVariableName = "spr";
        doc.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
        doc.NotifyLinkChanged();

        Assert.True(doc.IsLinked);
        Assert.Equal("unlink_test.c", doc.LinkedSourceFileName);

        // Execute Unlink
        doc.UnlinkSourceCommand.Execute(null);

        Assert.False(doc.IsLinked);
        Assert.Null(doc.SpriteState.LinkedSourceFile);
        Assert.Null(doc.LinkedSourceFileName);

        // Undo Unlink
        doc.UndoCommand.Execute(null);

        Assert.True(doc.IsLinked);
        Assert.Equal(path, doc.SpriteState.LinkedSourceFile);
        Assert.Equal("spr", doc.SpriteState.LinkedVariableName);
        Assert.Equal("unlink_test.c", doc.LinkedSourceFileName);

        // Redo Unlink
        doc.RedoCommand.Execute(null);

        Assert.False(doc.IsLinked);
        Assert.Null(doc.SpriteState.LinkedSourceFile);
    }

    // ── 9. RelinkSourceCommand User Declines Pull ────────────────────────

    [Fact]
    public void RelinkSourceCommand_UserDeclinesPull_KeepsCurrentCanvasAndUpdatesLink()
    {
        _shell.OpenDocuments.Clear();
        string oldPath = WriteTempFile("old.c", "const uint8_t old_spr[] = { 0x00, 0x00 };");
        string newPath = WriteTempFile("new.c", "const uint8_t new_spr[] = { 0x00, 0x00 };");

        _shell.NewDocumentCommand.Execute("16x16");
        var doc = (MainViewModel)_shell.ActiveDocument!;
        doc.SpriteState.LinkedSourceFile = oldPath;
        doc.SpriteState.LinkedVariableName = "old_spr";
        doc.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
        doc.NotifyLinkChanged();

        // Draw a pixel on current canvas
        doc.SpriteState.Pixels[0] = true;

        var detectedSprite = new DetectedSprite
        {
            Name = "new_spr",
            Format = ExportFormat.AdafruitGfx,
            CodeSnippet = "0x00, 0x00",
            Width = 16,
            Height = 16,
            FrameCount = 1,
        };

        _dialogMock.Setup(d => d.ShowImportFromFileDialog(It.IsAny<string?>()))
            .Returns((newPath, new List<DetectedSprite> { detectedSprite }));

        // User declines pull
        _dialogMock.Setup(d => d.ShowConfirmation(
            It.Is<string>(s => s.Contains("Would you like to pull")),
            "Pull Linked Source")).Returns(false);

        doc.RelinkSourceCommand.Execute(null);

        Assert.Equal(newPath, doc.SpriteState.LinkedSourceFile);
        Assert.Equal("new_spr", doc.SpriteState.LinkedVariableName);
        Assert.True(doc.SpriteState.Pixels[0], "Canvas pixels must be preserved when user declines pull on relink");
    }

    // ── 10. Multi-Document GlobalPullRequested Event Isolation ───────────

    [Fact]
    public async Task GlobalPullRequested_TargetedFile_OnlyPullsMatchingDocuments()
    {
        _shell.OpenDocuments.Clear();
        string content1 = """
            #define SPR1_WIDTH 16
            #define SPR1_HEIGHT 16
            const uint8_t spr1[] = { 0x00, 0x00 };
            """;
        string content2 = """
            #define SPR2_WIDTH 16
            #define SPR2_HEIGHT 16
            const uint8_t spr2[] = { 0x00, 0x00 };
            """;
        string path1 = WriteTempFile("shared.c", content1);
        string path2 = WriteTempFile("other.c", content2);

        // Tab A linked to shared.c
        _shell.NewDocumentCommand.Execute("16x16");
        var docA = (MainViewModel)_shell.ActiveDocument!;
        docA.SpriteState.LinkedSourceFile = path1;
        docA.SpriteState.LinkedVariableName = "spr1";
        docA.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
        docA.NotifyLinkChanged();

        // Tab B also linked to shared.c
        _shell.NewDocumentCommand.Execute("16x16");
        var docB = (MainViewModel)_shell.ActiveDocument!;
        docB.SpriteState.LinkedSourceFile = path1;
        docB.SpriteState.LinkedVariableName = "spr1";
        docB.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
        docB.NotifyLinkChanged();

        // Tab C linked to other.c
        _shell.NewDocumentCommand.Execute("16x16");
        var docC = (MainViewModel)_shell.ActiveDocument!;
        docC.SpriteState.LinkedSourceFile = path2;
        docC.SpriteState.LinkedVariableName = "spr2";
        docC.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
        docC.NotifyLinkChanged();

        // Modify shared.c on disk (pixel 0 = true -> 0x80)
        string updated1 = """
            #define SPR1_WIDTH 16
            #define SPR1_HEIGHT 16
            const uint8_t spr1[] = { 0x80, 0x00 };
            """;
        File.WriteAllText(path1, updated1);

        // Fire GlobalPullRequested targeting shared.c
        var eventField = typeof(MainViewModel).GetField("GlobalPullRequested", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        var handler = eventField?.GetValue(null) as EventHandler<string>;
        handler?.Invoke(null, path1);

        // Wait for async handler on thread pool / dispatcher
        for (int i = 0; i < 50 && (!docA.SpriteState.Pixels[0] || !docB.SpriteState.Pixels[0]); i++)
        {
            await Task.Delay(20);
            FlushDispatcher();
        }

        Assert.True(docA.SpriteState.Pixels[0], "Tab A must pull external changes for shared.c");
        Assert.True(docB.SpriteState.Pixels[0], "Tab B must pull external changes for shared.c");
        Assert.False(docC.SpriteState.Pixels[0], "Tab C (linked to other.c) must remain unaffected");
    }
}
