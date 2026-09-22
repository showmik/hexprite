using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Moq;
using Xunit;

namespace Hexprite.Tests;

[Collection("WindowLayoutSettingsFile")]
[Trait("Category", "Unit")]
public sealed class FileSaveOpenRefinementTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _workDir;
    private readonly string _testSettingsFile;
    private readonly Mock<IDialogService> _dialogMock;
    private readonly Mock<ICodeGeneratorService> _codeGenMock;
    private readonly Mock<IAutosaveService> _autosaveMock;
    private readonly ShellViewModel _shell;

    public FileSaveOpenRefinementTests()
    {
        WpfTestHelper.EnsureApplication();
        _tempDir = Path.Combine(Path.GetTempPath(), "HexpriteRefinementTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);

        _workDir = Path.Combine(AppContext.BaseDirectory, "RefineWorkspace_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_workDir);

        _testSettingsFile = Path.Combine(AppContext.BaseDirectory, "RefineSettings_" + Guid.NewGuid().ToString("N")[..8] + ".json");
        UserPreferencesService.SetCustomSettingsPath(_testSettingsFile);

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
        var importExportMock = new Mock<IFileImportExportService>();
        var hardwarePreviewMock = new Mock<IHardwarePreviewService>();
        _autosaveMock = new Mock<IAutosaveService>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IAutosaveService))).Returns(_autosaveMock.Object);

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
            importExportMock.Object,
            hardwarePreviewMock.Object,
            serviceProviderMock.Object);
    }

    public void Dispose()
    {
        UserPreferencesService.SetCustomSettingsPath(null);
        try { if (File.Exists(_testSettingsFile)) File.Delete(_testSettingsFile); } catch { }
        try { if (File.Exists(_testSettingsFile + ".bak")) File.Delete(_testSettingsFile + ".bak"); } catch { }
        try { if (Directory.Exists(_workDir)) Directory.Delete(_workDir, recursive: true); } catch { }
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string WriteTempFile(string name, string content)
    {
        string path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string WriteWorkFile(string name, string content)
    {
        string path = Path.Combine(_workDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void SafeFileIo_WriteBytesAtomic_WritesBytesAndOverwritesReadOnly()
    {
        string filePath = Path.Combine(_tempDir, "test.bin");
        byte[] original = [0x01, 0x02, 0x03];
        File.WriteAllBytes(filePath, original);
        File.SetAttributes(filePath, FileAttributes.ReadOnly);

        byte[] updated = [0xAA, 0xBB, 0xCC, 0xDD];
        SafeFileIo.WriteBytesAtomic(filePath, updated);

        Assert.True(File.Exists(filePath));
        byte[] readBack = File.ReadAllBytes(filePath);
        Assert.Equal(updated, readBack);
    }

    [Fact]
    public void SafeFileIo_WriteStreamAtomic_WritesStreamAtomically()
    {
        string filePath = Path.Combine(_tempDir, "stream_test.dat");
        byte[] data = [0x10, 0x20, 0x30, 0x40];

        SafeFileIo.WriteStreamAtomic(filePath, stream => stream.Write(data, 0, data.Length));

        Assert.True(File.Exists(filePath));
        byte[] readBack = File.ReadAllBytes(filePath);
        Assert.Equal(data, readBack);
    }

    [Fact]
    public void UserPreferencesService_RecentFiles_AddsAndCapsAtLimit()
    {
        UserPreferencesService.ClearRecentFiles();

        for (int i = 1; i <= 15; i++)
        {
            string path = string.Create(CultureInfo.InvariantCulture, $@"C:\HexpriteProjects\Artwork\sample_{i}.hexp");
            UserPreferencesService.AddRecentFile(path);
        }

        var recents = UserPreferencesService.GetRecentFiles(pruneMissing: false);
        Assert.Equal(UserPreferencesService.MaxRecentFiles, recents.Count);
        // The most recently added (sample_15) should be at index 0
        Assert.Contains("sample_15.hexp", recents[0]);
    }

    [Fact]
    public void UserPreferencesService_RecentFiles_PrunesMissingFiles()
    {
        UserPreferencesService.ClearRecentFiles();

        string existingFile = WriteWorkFile("existing.hexp", "{}");
        string deletedFile = WriteWorkFile("deleted.hexp", "{}");

        UserPreferencesService.AddRecentFile(existingFile);
        UserPreferencesService.AddRecentFile(deletedFile);

        // Delete one file from disk
        File.Delete(deletedFile);

        var recents = UserPreferencesService.GetRecentFiles(pruneMissing: true);
        Assert.Single(recents);
        Assert.Equal(Path.GetFullPath(existingFile), recents[0]);
    }

    [Fact]
    public void UserPreferencesService_RecentFiles_RejectsNonNativeFilesAndDirectories()
    {
        UserPreferencesService.ClearRecentFiles();

        UserPreferencesService.AddRecentFile(@"C:\HexpriteProjects\Artwork\flipper_anim_folder");
        UserPreferencesService.AddRecentFile(@"C:\HexpriteProjects\Artwork\image.png");
        UserPreferencesService.AddRecentFile(@"C:\HexpriteProjects\Artwork\code.c");
        UserPreferencesService.AddRecentFile(@"C:\HexpriteProjects\Artwork\meta.txt");
        UserPreferencesService.AddRecentFile(@"C:\HexpriteProjects\Artwork\file.tmp");
        UserPreferencesService.AddRecentFile(@"C:\HexpriteProjects\Artwork\backup.bak");
        UserPreferencesService.AddRecentFile(@"C:\HexpriteProjects\Artwork\project.hexp");
        UserPreferencesService.AddRecentFile(@"C:\HexpriteProjects\Artwork\pack.hexpack");
        UserPreferencesService.AddRecentFile(@"C:\HexpriteProjects\Artwork\font.hexfont");

        var recents = UserPreferencesService.GetRecentFiles(pruneMissing: false);
        Assert.Equal(3, recents.Count);
        Assert.Contains(recents, r => r.EndsWith("project.hexp", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(recents, r => r.EndsWith("pack.hexpack", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(recents, r => r.EndsWith("font.hexfont", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UserPreferencesService_RecentFiles_RejectsTempPathsAndPrefixes()
    {
        UserPreferencesService.ClearRecentFiles();

        UserPreferencesService.AddRecentFile(Path.Combine(Path.GetTempPath(), "temp_drawing.hexp"));
        UserPreferencesService.AddRecentFile(@"C:\HexpriteProjects\Artwork\shell_test_abc123.hexp");
        UserPreferencesService.AddRecentFile(@"C:\HexpriteProjects\Artwork\.hexp_tmp_xyz.hexp");
        UserPreferencesService.AddRecentFile(@"C:\HexpriteProjects\Artwork\recovery_doc1.hexp");
        UserPreferencesService.AddRecentFile(@"C:\HexpriteProjects\Artwork\tmp_canvas.hexp");
        UserPreferencesService.AddRecentFile(@"C:\HexpriteProjects\Artwork\temp_canvas.hexp");
        UserPreferencesService.AddRecentFile(@"C:\HexpriteProjects\Artwork\~autosave.hexp");
        UserPreferencesService.AddRecentFile(@"C:\HexpriteProjects\Artwork\valid_project.hexp");

        var recents = UserPreferencesService.GetRecentFiles(pruneMissing: false);
        Assert.Single(recents);
        Assert.Contains("valid_project.hexp", recents[0]);
    }

    [Fact]
    public void UserPreferencesService_RecentFiles_DoesNotEagerlyPruneByDefault()
    {
        UserPreferencesService.ClearRecentFiles();

        string disconnectedDrivePath = @"Z:\removable_drive\project.hexp";
        UserPreferencesService.AddRecentFile(disconnectedDrivePath);

        var recents = UserPreferencesService.GetRecentFiles(); // default pruneMissing = false
        Assert.Single(recents);
        Assert.Contains("project.hexp", recents[0]);
    }

    [Fact]
    public void ShellViewModel_CheckAutosaves_HandlesCorruptedAutosaveWithoutCrashing()
    {
        string corruptedAutosave = WriteTempFile("corrupt_auto.json", "{}");
        string validAutosave = WriteTempFile("valid_auto.json", "{}");

        _autosaveMock.Setup(a => a.GetAvailableAutosaves()).Returns([corruptedAutosave, validAutosave]);
        _dialogMock.Setup(d => d.ShowConfirmation(It.IsAny<string>(), It.IsAny<string>())).Returns(value: true);

        // Corrupted state with 0 dimension
        var corruptState = new SpriteState(0, 0);
        // Valid state
        var validState = new SpriteState(16, 16);

        _autosaveMock.Setup(a => a.LoadAutosave(corruptedAutosave)).Returns(corruptState);
        _autosaveMock.Setup(a => a.LoadAutosave(validAutosave)).Returns(validState);

        // Act
        _shell.CheckAutosaves();

        // Assert: Valid document was opened, corrupted was skipped cleanly
        Assert.Single(_shell.OpenDocuments);
        var doc = Assert.IsType<MainViewModel>(_shell.ActiveDocument);
        Assert.Equal(16, doc.SpriteState.Width);
    }
}
