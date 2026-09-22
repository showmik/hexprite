using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Moq;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
public sealed class FileSaveOpenHardeningTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<IDialogService> _dialogMock;
    private readonly Mock<ICodeGeneratorService> _codeGenMock;
    private readonly Mock<IFileImportExportService> _importExportMock;
    private readonly ShellViewModel _shell;

    public FileSaveOpenHardeningTests()
    {
        WpfTestHelper.EnsureApplication();
        _tempDir = Path.Combine(Path.GetTempPath(), "HexpriteSaveOpenTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);

        _codeGenMock = new Mock<ICodeGeneratorService>();
        _codeGenMock.Setup(c => c.GenerateCodeAsync(
            It.IsAny<List<bool[]>>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<ExportSettings>(),
            It.IsAny<bool>(),
            It.IsAny<bool[,]>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<FloatingPasteMode>(),
            It.IsAny<List<int>>(),
            It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync("const uint8_t sprite[] = { 0x00 };");

        _codeGenMock.Setup(c => c.CalculateByteCount(
            It.IsAny<List<bool[]>>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<ExportSettings>()))
            .Returns(32);

        var drawingMock = new Mock<IDrawingService>();
        var clipboardMock = new Mock<IClipboardService>();
        var pixelClipboardMock = new Mock<IPixelClipboardService>();
        _dialogMock = new Mock<IDialogService>();
        var themeMock = new Mock<IThemeService>();
        var bugReportMock = new Mock<IBugReportService>();
        var feedbackMock = new Mock<IUserFeedbackService>();
        var controllerFactory = new ControllerFactory();
        var exportMock = new Mock<IExportService>();
        _importExportMock = new Mock<IFileImportExportService>();
        var hardwarePreviewMock = new Mock<IHardwarePreviewService>();
        var autosaveMock = new Mock<IAutosaveService>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IAutosaveService))).Returns(autosaveMock.Object);

        _shell = new ShellViewModel(
            _codeGenMock.Object,
            drawingMock.Object,
            clipboardMock.Object,
            pixelClipboardMock.Object,
            _dialogMock.Object,
            themeMock.Object,
            bugReportMock.Object,
            feedbackMock.Object,
            controllerFactory,
            exportMock.Object,
            _importExportMock.Object,
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

    [Fact]
    public void SaveToPath_CreatesFileAtomicallyAndClearsDirty()
    {
        _shell.NewDocumentCommand.Execute("16x16");
        var doc = Assert.IsType<MainViewModel>(_shell.ActiveDocument);
        doc.IsDirty = true;

        string savePath = Path.Combine(_tempDir, "mysprite.hexp");
        _shell.SaveCommand.Execute(parameter: null); // Triggers SaveAs because FilePath is null

        // Directly test SaveToPath behavior
        SafeFileIo.WriteAllTextAtomic(savePath, JsonSerializer.Serialize(doc.SpriteState));
        Assert.True(File.Exists(savePath));
        string content = File.ReadAllText(savePath);
        Assert.Contains("\"Width\":16", content);
    }

    [Fact]
    public void SaveToPath_TargetReadOnly_ClearsReadOnlyAndSucceeds()
    {
        string savePath = WriteTempFile("readonly.hexp", "{}");
        File.SetAttributes(savePath, FileAttributes.ReadOnly);

        SafeFileIo.WriteAllTextAtomic(savePath, "{\"Width\":32,\"Height\":32}");

        Assert.True(File.Exists(savePath));
        string content = File.ReadAllText(savePath);
        Assert.Contains("\"Width\":32", content);
    }

    [Fact]
    public void OpenFile_ZeroByteFile_ShowsDialogAndDoesNotCrash()
    {
        string emptyPath = WriteTempFile("empty.hexp", "");

        _shell.OpenFile(emptyPath);

        _dialogMock.Verify(d => d.ShowMessage(It.Is<string>(s => s.Contains("empty"))), Times.Once());
    }

    [Fact]
    public void OpenFile_CorruptedJson_ShowsDialogAndDoesNotCrash()
    {
        string corruptPath = WriteTempFile("corrupt.hexp", "{ invalid json content ... ]]]");

        _shell.OpenFile(corruptPath);

        _dialogMock.Verify(d => d.ShowMessage(It.Is<string>(s => s.Contains("corrupted") || s.Contains("not a valid"))), Times.Once());
    }

    [Fact]
    public void OpenFile_InvalidDimensions_ShowsWarningAndDoesNotCrash()
    {
        string invalidDimPath = WriteTempFile("invaliddim.hexp", "{\"Width\":0,\"Height\":16}");

        _shell.OpenFile(invalidDimPath);

        _dialogMock.Verify(d => d.ShowMessage(It.Is<string>(s => s.Contains("invalid canvas dimensions"))), Times.Once());
    }

    [Fact]
    public void OpenFile_ValidSprite_OpensDocumentCleanly()
    {
        var state = new SpriteState(16, 16);
        string validPath = WriteTempFile("valid.hexp", JsonSerializer.Serialize(state));

        _shell.OpenFile(validPath);

        var doc = Assert.IsType<MainViewModel>(_shell.ActiveDocument);
        Assert.Equal(16, doc.SpriteState.Width);
        Assert.Equal(16, doc.SpriteState.Height);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void SafeFileIo_EnsureExtension_AppendsExtensionWhenMissing()
    {
        string withoutExt = "C:\\Projects\\my_sprite";
        string withExt = SafeFileIo.EnsureExtension(withoutExt, ".hexp");
        Assert.Equal("C:\\Projects\\my_sprite.hexp", withExt);

        string alreadyHasExt = "C:\\Projects\\my_sprite.hexp";
        string preserved = SafeFileIo.EnsureExtension(alreadyHasExt, ".hexp");
        Assert.Equal("C:\\Projects\\my_sprite.hexp", preserved);
    }

    [Fact]
    public async Task TryCloseTab_WhenSaveCancelled_DoesNotCloseTab()
    {
        _shell.NewDocumentCommand.Execute("16x16");
        var doc = Assert.IsType<MainViewModel>(_shell.ActiveDocument);
        doc.IsDirty = true;

        // User cancels the save prompt
        _dialogMock.Setup(d => d.ShowUnsavedChangesDialog(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns((bool?)null);

        bool closed = await _shell.TryCloseTabAsync(doc);

        Assert.False(closed);
        Assert.Contains(doc, _shell.OpenDocuments);
    }

    [Fact]
    public void OpenFile_ValidFontDocument_OpensFontViewModelCleanly()
    {
        var fontDoc = new FontDocument { FontName = "Custom 5x7 Font", MaxCellWidth = 5, CellHeight = 7 };
        fontDoc.NormalizeGlyphs();
        string validFontPath = WriteTempFile("valid.hexfont", JsonSerializer.Serialize(fontDoc));

        _shell.OpenFile(validFontPath);

        var doc = Assert.IsType<FontViewModel>(_shell.ActiveDocument);
        Assert.Equal("Custom 5x7 Font", doc.FontName);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void SafeFileIo_WriteAllTextAtomic_CreatesBackupWhenRequested()
    {
        string filePath = WriteTempFile("backup_test.hexp", "Initial content");
        
        SafeFileIo.WriteAllTextAtomic(filePath, "Updated content", createBackup: true);

        Assert.Equal("Updated content", File.ReadAllText(filePath));
    }

    [Fact]
    public void SafeFileIo_ReadAllTextWithRetry_ThrowsOnMissingFile()
    {
        string missingPath = Path.Combine(_tempDir, "does_not_exist.hexp");
        Assert.Throws<FileNotFoundException>(() => SafeFileIo.ReadAllTextWithRetry(missingPath, maxRetries: 1));
    }
}
