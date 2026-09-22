using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Moq;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
public sealed class LinkedSourceHardeningTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileImportExportService _fileSvc;
    private readonly Mock<IDialogService> _dialogMock;
    private readonly Mock<ICodeGeneratorService> _codeGenMock;
    private readonly Mock<IFileImportExportService> _importExportMock;
    private readonly ShellViewModel _shell;
    private readonly MainViewModel _vm;

    public LinkedSourceHardeningTests()
    {
        WpfTestHelper.EnsureApplication();
        _tempDir = Path.Combine(Path.GetTempPath(), "HexpriteHardening_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);

        _fileSvc = new FileImportExportService();
        _codeGenMock = new Mock<ICodeGeneratorService>();
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

        _shell.NewDocumentCommand.Execute("16x16");
        _vm = (MainViewModel)_shell.ActiveDocument!;
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

    private void FlushDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new DispatcherOperationCallback(f =>
        {
            ((DispatcherFrame)f).Continue = false;
            return null;
        }), frame);
        Dispatcher.PushFrame(frame);
    }

    // ── 1. Memory Leak & Detach ──────────────────────────────────────────

    [Fact]
    public void Detach_UnsubscribesFromGlobalPullRequested()
    {
        string filePath = WriteTempFile("detach_test.c", "const uint8_t sprite[] = { 0x00 };");
        _vm.SpriteState.LinkedSourceFile = filePath;
        _vm.SpriteState.LinkedVariableName = "sprite";
        _vm.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
        _vm.NotifyLinkChanged();

        // Detach the document tab
        _vm.Detach();

        // Simulate global pull event firing
        var eventArgs = new FileSystemEventArgs(WatcherChangeTypes.Changed, _tempDir, "detach_test.c");
        // Ensure no exception or zombie handler runs
        _vm.OnLinkedFileChanged(this, eventArgs);
        FlushDispatcher();

        Assert.False(_vm.IsLinkedFileMissing);
    }

    // ── 2. LoadState Watcher Initialization ──────────────────────────────

    [Fact]
    public void LoadState_WithLinkedFile_InitializesIsLinkedAndWatcher()
    {
        string filePath = WriteTempFile("load_state.c", "const uint8_t player[] = { 0xAA };");
        var state = new SpriteState(16, 16)
        {
            LinkedSourceFile = filePath,
            LinkedVariableName = "player",
            LinkedFormat = ExportFormat.AdafruitGfx,
        };

        _vm.LoadState(state);

        Assert.True(_vm.IsLinked);
        Assert.Equal("load_state.c", _vm.LinkedSourceFileName);
        Assert.Equal("player", _vm.LinkedVariableName);
    }

    // ── 3. Comment Alignment in Context Extraction ───────────────────────

    [Fact]
    public void ExtractSprites_WithLargeLeadingComment_ExtractsCorrectDimensions()
    {
        // 2000-character comment header followed by dimensions and array
        string header = "/*" + new string('*', 2000) + "*/\n";
        string code = header + """
            #define PLAYER_WIDTH 32
            #define PLAYER_HEIGHT 8
            const uint8_t player[] = { 0x00, 0x01, 0x02, 0x03 };
            """;
        string path = WriteTempFile("large_header.c", code);

        var sprites = _fileSvc.ExtractSpritesFromFile(path);

        Assert.Single(sprites);
        Assert.Equal("player", sprites[0].Name);
        Assert.Equal(32, sprites[0].Width);
        Assert.Equal(8, sprites[0].Height);
    }

    // ── 4. C++ Qualifiers and Namespace Extraction ───────────────────────

    [Fact]
    public void ExtractSprites_CppNamespaceAndAlignas_IsDetected()
    {
        string code = """
            alignas(4) const std::uint8_t hero_sprite[] = { 0x11, 0x22, 0x33, 0x44 };
            """;
        string path = WriteTempFile("cpp_types.cpp", code);

        var sprites = _fileSvc.ExtractSpritesFromFile(path);

        Assert.Single(sprites);
        Assert.Equal("hero_sprite", sprites[0].Name);
    }

    [Fact]
    public void UpdateSprite_CppNamespaceAndAlignas_PreservesDeclaration()
    {
        string code = """
            alignas(4) const std::uint8_t hero_sprite[] = {
                0x00, 0x00
            };
            """;
        string path = WriteTempFile("cpp_update.cpp", code);

        string newSnippet = "const uint8_t hero_sprite[] = {\n    0xFF, 0xFF\n};";
        _fileSvc.UpdateSpriteInFile(path, "hero_sprite", newSnippet);

        string updated = File.ReadAllText(path);
        Assert.Contains("alignas(4) const std::uint8_t hero_sprite[]", updated);
        Assert.Contains("0xFF, 0xFF", updated);
    }

    // ── 5. Python bytes([ ... ]) Support ─────────────────────────────────

    [Fact]
    public void ExtractAndModify_PythonBytes_WorksCorrectly()
    {
        string code = """
            ICON_WIDTH = 16
            ICON_HEIGHT = 16
            icon = bytes([
                0x00, 0x11
            ])
            """;
        string path = WriteTempFile("test_bytes.py", code);

        var sprites = _fileSvc.ExtractSpritesFromFile(path);
        Assert.Single(sprites);
        Assert.Equal("icon", sprites[0].Name);
        Assert.Equal(ExportFormat.MicroPython, sprites[0].Format);

        string newSnippet = "icon = bytes([\n    0xAA, 0xBB\n])";
        _fileSvc.UpdateSpriteInFile(path, "icon", newSnippet, newWidth: 24, newHeight: 24);

        string updated = File.ReadAllText(path);
        Assert.Contains("0xAA, 0xBB", updated);
        Assert.Contains("ICON_WIDTH = 24", updated);
        Assert.Contains("ICON_HEIGHT = 24", updated);
    }

    // ── 6. Line Ending Preservation ─────────────────────────────────────

    [Fact]
    public void UpdateSprite_PreservesLfLineEndings()
    {
        string lfContent = "const uint8_t PROGMEM sprite[] = {\n    0x00, 0x00 // row 1\n};\n";
        string path = WriteTempFile("lf_file.c", lfContent);

        string newSnippet = "const uint8_t PROGMEM sprite[] = {\n    0xFF, 0xFF\n};";
        _fileSvc.UpdateSpriteInFile(path, "sprite", newSnippet);

        string updated = File.ReadAllText(path);
        Assert.DoesNotContain("\r\n", updated);
        Assert.Contains("\n", updated);
        Assert.Contains("0xFF, 0xFF // row 1", updated);
    }

    // ── 7. Multi-Format Detection (Indexed2D & XBM) ──────────────────────

    [Fact]
    public void ExtractSprites_DetectsXbmAnd2DMatrixFormats()
    {
        string code = """
            #define LOGO_WIDTH 16
            #define LOGO_HEIGHT 8
            static const unsigned char logo_bits[] = { 0x01, 0x80 };

            const uint8_t PROGMEM matrix_sprite[4][4] = {
                { 0, 1, 2, 3 },
                { 4, 5, 6, 7 },
                { 8, 9, 10, 11 },
                { 12, 13, 14, 15 }
            };
            """;
        string path = WriteTempFile("multi_format.c", code);

        var sprites = _fileSvc.ExtractSpritesFromFile(path);

        Assert.Equal(2, sprites.Count);
        var xbm = sprites.Find(s => s.Name == "logo_bits");
        Assert.NotNull(xbm);
        Assert.Equal(ExportFormat.U8g2DrawXBM, xbm.Format);

        var matrix = sprites.Find(s => s.Name == "matrix_sprite");
        Assert.NotNull(matrix);
        Assert.Equal(ExportFormat.Indexed2D, matrix.Format);
        Assert.Equal(4, matrix.Width);
        Assert.Equal(4, matrix.Height);
    }

    [Fact]
    public void ExtractSprites_TinyRogueCover_Extracts128x45()
    {
        string code = """
            const uint8_t PROGMEM spr_tinyrogue_cover[720] = {
              0xB1, 0x81, 0xC0, 0x8A, 0x8F, 0xF0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0xA0, 0x18, 0x1F,
              0x42, 0x0E, 0x00, 0x8A, 0x80, 0x87, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xF0, 0x02, 0xA0, 0x05, 0xE3,
              0xA7, 0xB0, 0x0F, 0x8A, 0x80, 0x87, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xF0, 0x02, 0xA0, 0x0F, 0x0D,
              0x10, 0x50, 0x00, 0x8A, 0x80, 0x86, 0x00, 0x00, 0x00, 0x00, 0x00, 0x37, 0xE2, 0xA0, 0x32, 0x12,
              0x2C, 0x80, 0x00, 0x8A, 0x8F, 0xE6, 0xFF, 0xBF, 0xB8, 0x1D, 0xC7, 0x30, 0x82, 0xA1, 0xC2, 0x62,
              0x63, 0x20, 0x7F, 0x8A, 0x80, 0x06, 0xFF, 0xBF, 0xBC, 0x1D, 0xC7, 0x30, 0x82, 0xA0, 0x62, 0x84,
              0x25, 0x00, 0x08, 0x0A, 0x80, 0x06, 0x1C, 0x0E, 0x3E, 0x1D, 0xC7, 0x30, 0x82, 0xA0, 0x13, 0xB4,
              0xA8, 0xA0, 0x08, 0x0A, 0x80, 0x07, 0xDC, 0x0E, 0x3F, 0x1D, 0xC7, 0x37, 0xE2, 0xA0, 0x0C, 0x0A,
              0x10, 0x80, 0x08, 0x1A, 0xC1, 0xF7, 0xDC, 0x0E, 0x3B, 0x9D, 0xC7, 0x30, 0x06, 0xB0, 0x14, 0x10,
              0x1B, 0xC0, 0x00, 0x2A, 0xA0, 0x00, 0xDC, 0x0E, 0x39, 0xDC, 0xEE, 0x30, 0x0A, 0xA8, 0x64, 0x10,
              0x20, 0x00, 0x00, 0x3F, 0xE0, 0x00, 0xDC, 0x0E, 0x38, 0xFC, 0x7C, 0x30, 0x0F, 0xF8, 0x84, 0x21,
              0x40, 0x00, 0x07, 0x9F, 0xCF, 0xFC, 0xDC, 0x0E, 0x38, 0x7C, 0x38, 0x77, 0xE7, 0xF3, 0xFF, 0xFF,
              0x80, 0x07, 0xC0, 0x00, 0x00, 0x00, 0xDC, 0x0E, 0x38, 0x3C, 0x38, 0x60, 0x00, 0x00, 0x00, 0x00,
              0x00, 0x1F, 0xE0, 0x00, 0x00, 0x00, 0xDC, 0x3F, 0xB8, 0x1C, 0x38, 0xE0, 0x00, 0x00, 0x00, 0x00,
              0x00, 0x3F, 0xF0, 0x00, 0x00, 0x00, 0xDC, 0x3F, 0xB8, 0x1C, 0x39, 0x80, 0x00, 0x00, 0x00, 0x00,
              0x00, 0x78, 0x70, 0x00, 0xFE, 0x00, 0xC0, 0x00, 0x00, 0x00, 0x01, 0x80, 0x00, 0x00, 0x80, 0x20,
              0x00, 0x77, 0x30, 0x01, 0xFF, 0x00, 0xFC, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x40, 0x40,
              0x00, 0x60, 0xB0, 0x07, 0xF0, 0x80, 0xFD, 0xE3, 0x8F, 0x65, 0xF7, 0x00, 0x00, 0x00, 0x20, 0x40,
              0x00, 0x20, 0xA0, 0x0F, 0xE0, 0x40, 0x0D, 0x96, 0x58, 0x65, 0x86, 0x00, 0x00, 0x00, 0x00, 0x80,
              0x00, 0xC0, 0x10, 0x0D, 0xC0, 0x40, 0x0D, 0x96, 0x58, 0x65, 0x86, 0x3E, 0x00, 0x04, 0x00, 0x02,
              0x00, 0xE0, 0xB0, 0x01, 0x91, 0x40, 0x05, 0xF6, 0x5B, 0x65, 0xE6, 0x7F, 0x80, 0x02, 0x0F, 0x84,
              0x00, 0xE0, 0xB0, 0x01, 0x0E, 0x40, 0x19, 0xC6, 0x59, 0x65, 0x86, 0x7F, 0x80, 0x01, 0x1B, 0x60,
              0x00, 0xC0, 0xB0, 0x00, 0xBF, 0x40, 0x39, 0xA6, 0x59, 0x65, 0x86, 0x19, 0x80, 0x00, 0x36, 0xD0,
              0x00, 0x20, 0xA0, 0x01, 0xBF, 0x40, 0x75, 0x93, 0x8E, 0x39, 0xF6, 0x19, 0x80, 0x00, 0x2A, 0x88,
              0x00, 0xE0, 0x10, 0x0E, 0xCC, 0xE0, 0xE4, 0x00, 0x00, 0x00, 0x06, 0x7F, 0x80, 0x00, 0x2E, 0x88,
              0x02, 0xE0, 0xB0, 0x07, 0x7F, 0x01, 0xCF, 0xFF, 0xFF, 0xFF, 0xFE, 0x6F, 0x00, 0x00, 0x2B, 0xF8,
              0x0C, 0xC0, 0xB0, 0x17, 0x00, 0x43, 0x8F, 0xFF, 0xFF, 0xFF, 0xFE, 0x3E, 0x00, 0x0F, 0xA7, 0x8B,
              0x10, 0x20, 0xA0, 0x18, 0x7B, 0x2F, 0x00, 0x00, 0x00, 0x00, 0x80, 0x28, 0xE0, 0x0F, 0x98, 0x8B,
              0x20, 0xE0, 0x10, 0x02, 0x77, 0x67, 0x00, 0x00, 0x00, 0x00, 0xC0, 0x01, 0x10, 0x0B, 0xE6, 0x8B,
              0x00, 0xE0, 0xB0, 0x18, 0x76, 0x92, 0x00, 0x00, 0x00, 0x00, 0x60, 0x3E, 0x08, 0x09, 0xF1, 0x8B,
              0x00, 0x00, 0xB0, 0x1C, 0x76, 0x24, 0x00, 0x00, 0x00, 0x00, 0x30, 0x42, 0x88, 0x08, 0x7C, 0x7B,
              0x00, 0xE0, 0xB7, 0xDE, 0x00, 0x09, 0xBE, 0xFD, 0xC0, 0x07, 0x3C, 0x9F, 0x11, 0xF8, 0x3F, 0x87,
              0x04, 0xE0, 0xAF, 0xDC, 0x7E, 0x53, 0x7D, 0xF8, 0x00, 0x00, 0x1B, 0x02, 0x91, 0xFC, 0x1F, 0xFF,
              0x08, 0xC0, 0xDF, 0x80, 0x00, 0x26, 0x00, 0xC3, 0x00, 0x00, 0xD7, 0x0F, 0x01, 0xFF, 0x07, 0xFF,
              0x30, 0x20, 0x00, 0x7C, 0xF3, 0x9E, 0xFB, 0x30, 0x00, 0x00, 0x44, 0x82, 0x81, 0x7F, 0x83, 0xFF,
              0x40, 0xE0, 0x7E, 0x00, 0xF1, 0xDD, 0xF7, 0xE0, 0x00, 0x00, 0x20, 0x84, 0x9D, 0x1F, 0xC2, 0x00,
              0x80, 0xE0, 0xFD, 0xF8, 0x00, 0x58, 0x0F, 0xCC, 0x80, 0x03, 0x9E, 0x48, 0x81, 0x0F, 0xF2, 0x00,
              0x00, 0xE3, 0xFB, 0xF0, 0xDD, 0xD7, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x30, 0x5F, 0x07, 0xFA, 0x00,
              0x00, 0xE7, 0xF7, 0xE2, 0xDC, 0x97, 0x9F, 0xB0, 0x00, 0x00, 0x1F, 0x93, 0x47, 0x81, 0xFE, 0x00,
              0x02, 0xCF, 0xF0, 0x1F, 0x01, 0x8F, 0x3F, 0x00, 0x00, 0x00, 0xDF, 0x13, 0x7B, 0xC1, 0x00, 0x00,
              0x04, 0x80, 0x0F, 0x8C, 0x01, 0xC6, 0x70, 0x58, 0x00, 0x00, 0x0F, 0x7B, 0x09, 0xE1, 0x00, 0x00,
              0x08, 0x3F, 0xDF, 0x4E, 0x01, 0xF0, 0x87, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF9, 0x00, 0x00,
              0x10, 0x7F, 0xBF, 0x40, 0x00, 0x04, 0xF3, 0x00, 0x00, 0x00, 0x6F, 0xFD, 0xFF, 0x7D, 0x00, 0x00,
              0x20, 0xFF, 0x00, 0xFF, 0xBF, 0x79, 0xF8, 0x40, 0x00, 0x00, 0x2F, 0xFC, 0xFF, 0x3F, 0x00, 0x00,
              0x41, 0xFE, 0xFD, 0xFF, 0x7E, 0xF3, 0xF4, 0x00, 0x00, 0x00, 0x07, 0xFE, 0xFF, 0x9F, 0x00, 0x00
            };
            """;
        string path = WriteTempFile("tinyrogue.c", code);

        var sprites = _fileSvc.ExtractSpritesFromFile(path);

        Assert.Single(sprites);
        Assert.Equal("spr_tinyrogue_cover", sprites[0].Name);
        Assert.Equal(128, sprites[0].Width);
        Assert.Equal(45, sprites[0].Height);
    }

    [Fact]
    public void ExtractSprites_MultiSpriteFile_DoesNotBleedPrecedingMacroDimensions()
    {
        string code = """
            #define TILE_WIDTH 16
            #define TILE_HEIGHT 16
            const uint8_t PROGMEM tile_grass[32] = {
                0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
                0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
                0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
                0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F
            };

            // Subsequent sprite with 8x8 line structure without its own macros
            const uint8_t PROGMEM hero_icon[8] = {
                0x3C,
                0x42,
                0x81,
                0x99,
                0x99,
                0x81,
                0x42,
                0x3C
            };
            """;
        string path = WriteTempFile("multi_sprite.c", code);

        var sprites = _fileSvc.ExtractSpritesFromFile(path);

        Assert.Equal(2, sprites.Count);
        
        var tile = sprites.Find(s => s.Name == "tile_grass");
        Assert.NotNull(tile);
        Assert.Equal(16, tile.Width);
        Assert.Equal(16, tile.Height);

        var hero = sprites.Find(s => s.Name == "hero_icon");
        Assert.NotNull(hero);
        Assert.Equal(8, hero.Width);
        Assert.Equal(8, hero.Height);
    }

    [Fact]
    public void ExtractSprites_ModernCppQualifiers_DetectsSpriteName()
    {
        string code = """
            alignas(4) const std::uint8_t spr_player[8] = {
                0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08
            };
            """;
        string path = WriteTempFile("modern_cpp.cpp", code);

        var sprites = _fileSvc.ExtractSpritesFromFile(path);

        Assert.Single(sprites);
        Assert.Equal("spr_player", sprites[0].Name);
        Assert.Equal(8, sprites[0].Width);
        Assert.Equal(8, sprites[0].Height);
    }

    [Fact]
    public void UpdateSpriteInFile_XbmBitsSuffix_UpdatesBaseNameDimensionDefines()
    {
        string code = """
            #define cursor_width 8
            #define cursor_height 8
            static const unsigned char cursor_bits[] = {
                0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08
            };
            """;
        string path = WriteTempFile("cursor.c", code);

        string newSnippet = """
            static const unsigned char cursor_bits[] = {
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF
            };
            """;

        _fileSvc.UpdateSpriteInFile(path, "cursor_bits", newSnippet, newWidth: 16, newHeight: 16);

        string updated = File.ReadAllText(path);
        Assert.Contains("#define cursor_width 16", updated);
        Assert.Contains("#define cursor_height 16", updated);
        Assert.Contains("0xFF, 0xFF", updated);
    }

    [Fact]
    public void RestoreSpriteInFile_ReadOnlyTarget_ClearsReadOnlyAndRestores()
    {
        string original = "const uint8_t spr[] = { 0x00, 0x00 };";
        string path = WriteTempFile("readonly_test.c", original);

        // Perform an update to create a backup
        string modified = "const uint8_t spr[] = { 0xFF, 0xFF };";
        _fileSvc.UpdateSpriteInFile(path, "spr", modified);

        // Mark target file Read-Only
        File.SetAttributes(path, FileAttributes.ReadOnly);

        // Restore should succeed and overwrite read-only file
        string restored = _fileSvc.RestoreSpriteInFile(path);
        Assert.Contains("0x00, 0x00", restored);

        string diskContent = File.ReadAllText(path);
        Assert.Contains("0x00, 0x00", diskContent);
    }

    [Fact]
    public void UpdateSpriteInFile_MultiFrameAnimation_UpdatesFrameCountMacro()
    {
        string code = """
            #define COIN_WIDTH 16
            #define COIN_HEIGHT 16
            #define COIN_FRAMES 2
            const uint8_t PROGMEM spr_coin[2][32] = {
                { 0x01, 0x02 },
                { 0x03, 0x04 }
            };
            """;
        string path = WriteTempFile("coin.c", code);

        string newSnippet = """
            const uint8_t PROGMEM spr_coin[4][32] = {
                { 0x01, 0x02 },
                { 0x03, 0x04 },
                { 0x05, 0x06 },
                { 0x07, 0x08 }
            };
            """;

        _fileSvc.UpdateSpriteInFile(path, "spr_coin", newSnippet, newWidth: 16, newHeight: 16, newFrameCount: 4);

        string updated = File.ReadAllText(path);
        Assert.Contains("#define COIN_FRAMES 4", updated);
        Assert.Contains("spr_coin[4][32]", updated);
    }

    [Fact]
    public void ExtractSprites_AnimatedSpriteWithFrameDefine_ExtractsFrameCount()
    {
        string code = """
            #define SPARKLE_WIDTH 8
            #define SPARKLE_HEIGHT 8
            #define SPARKLE_FRAME_COUNT 3
            const uint8_t spr_sparkle[24] = {
                0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
                0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28
            };
            """;
        string path = WriteTempFile("sparkle.c", code);

        var sprites = _fileSvc.ExtractSpritesFromFile(path);

        Assert.Single(sprites);
        Assert.Equal("spr_sparkle", sprites[0].Name);
        Assert.Equal(8, sprites[0].Width);
        Assert.Equal(8, sprites[0].Height);
        Assert.Equal(3, sprites[0].FrameCount);
    }

    [Fact]
    public void ExtractSprites_AnimatedSpriteFrom2DBrackets_ExtractsFrameCount()
    {
        string code = """
            const uint8_t spr_walk[4][32] = {
                { 0x00 }, { 0x00 }, { 0x00 }, { 0x00 }
            };
            """;
        string path = WriteTempFile("walk.c", code);

        var sprites = _fileSvc.ExtractSpritesFromFile(path);

        Assert.Single(sprites);
        Assert.Equal("spr_walk", sprites[0].Name);
        Assert.Equal(4, sprites[0].FrameCount);
    }

    [Fact]
    public void CheckForExternalChanges_WhenFileModifiedExternally_DetectsHashChangeAndMarksDirty()
    {
        string path = WriteTempFile("check_mod.c", "const uint8_t spr[] = { 0x01 };");
        _vm.SpriteState.LinkedSourceFile = path;
        _vm.SpriteState.LinkedVariableName = "spr";
        _vm.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
        _vm.UpdateLinkedFileHashIfMatches(path);

        _dialogMock.Setup(d => d.ShowConfirmation(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        // External process writes to file
        File.WriteAllText(path, "const uint8_t spr[] = { 0xFF };");

        // Act: Window focus / Tab switch triggers CheckForExternalChanges
        _vm.CheckForExternalChanges();
        FlushDispatcher();

        // Assert: External change is detected and flagged
        Assert.True(_vm.LinkedFileChangedExternally);
        _dialogMock.Verify(d => d.ShowConfirmation(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
    }

    [Fact]
    public void OnLinkedFileChanged_WhenSyncDialogActive_DoesNotDeadlockFutureDetection()
    {
        string path = WriteTempFile("deadlock_guard.c", "const uint8_t spr[] = { 0x10 };");
        _vm.SpriteState.LinkedSourceFile = path;
        _vm.SpriteState.LinkedVariableName = "spr";
        _vm.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
        _vm.UpdateLinkedFileHashIfMatches(path);

        _dialogMock.Setup(d => d.ShowConfirmation(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        // Modify file
        File.WriteAllText(path, "const uint8_t spr[] = { 0x20 };");

        var eventArgs = new FileSystemEventArgs(WatcherChangeTypes.Changed, _tempDir, "deadlock_guard.c");
        _vm.OnLinkedFileChanged(this, eventArgs);
        FlushDispatcher();

        Assert.True(_vm.LinkedFileChangedExternally);

        // Clear flag and simulate another modification
        _vm.LinkedFileChangedExternally = false;
        File.WriteAllText(path, "const uint8_t spr[] = { 0x30 };");

        _vm.OnLinkedFileChanged(this, eventArgs);
        FlushDispatcher();

        // Must still be detected (no deadlock on _isShowingChangeDialog)
        Assert.True(_vm.LinkedFileChangedExternally);
    }

    [Fact]
    public void ComputeFileHash_WithConcurrentReadWriteShare_ReadsFileCleanly()
    {
        string path = WriteTempFile("share_test.c", "const uint8_t spr[] = { 0xAA };");
        _vm.SpriteState.LinkedSourceFile = path;
        _vm.SpriteState.LinkedVariableName = "spr";
        _vm.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
        _vm.UpdateLinkedFileHashIfMatches(path);

        _dialogMock.Setup(d => d.ShowConfirmation(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        // Keep file open in another handle with ReadWrite|Delete sharing
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
        writer.Write("const uint8_t spr[] = { 0xBB };");
        writer.Flush();

        _vm.CheckForExternalChanges();
        FlushDispatcher();

        Assert.True(_vm.LinkedFileChangedExternally);
    }

    [Fact]
    public void CheckForExternalChanges_WhenFileDeletedAndRecreated_DetectsStateTransitions()
    {
        string path = WriteTempFile("lifecycle.c", "const uint8_t spr[] = { 0x00 };");
        _vm.SpriteState.LinkedSourceFile = path;
        _vm.SpriteState.LinkedVariableName = "spr";
        _vm.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
        _vm.UpdateLinkedFileHashIfMatches(path);

        // Step 1: File is deleted
        File.Delete(path);
        _vm.CheckForExternalChanges();
        Assert.True(_vm.IsLinkedFileMissing);

        // Step 2: File is recreated
        File.WriteAllText(path, "const uint8_t spr[] = { 0x00 };");
        _vm.CheckForExternalChanges();
        Assert.False(_vm.IsLinkedFileMissing);
    }

    // ── 8. Multi-Document External Change Detection ──────────────────────

    [Fact]
    public void MultiDocument_ExternalChange_BothDocumentsDetectChange()
    {
        string code = "const uint8_t spr1[] = { 0x01 };\nconst uint8_t spr2[] = { 0x02 };";
        string path = WriteTempFile("shared_multi.c", code);

        // Tab 1 linked to spr1
        _vm.SpriteState.LinkedSourceFile = path;
        _vm.SpriteState.LinkedVariableName = "spr1";
        _vm.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
        _vm.UpdateLinkedFileHashIfMatches(path);

        // Tab 2 linked to spr2
        _shell.NewDocumentCommand.Execute("16x16");
        var vm2 = (MainViewModel)_shell.OpenDocuments[1];
        vm2.SpriteState.LinkedSourceFile = path;
        vm2.SpriteState.LinkedVariableName = "spr2";
        vm2.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
        vm2.UpdateLinkedFileHashIfMatches(path);

        _dialogMock.Setup(d => d.ShowConfirmation(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        // External process writes new content
        File.WriteAllText(path, "const uint8_t spr1[] = { 0xFF };\nconst uint8_t spr2[] = { 0xEE };");

        // Act: Both documents check for external changes
        _vm.CheckForExternalChanges();
        vm2.CheckForExternalChanges();
        FlushDispatcher();

        // Assert: NEITHER document is blinded by the other
        Assert.True(_vm.LinkedFileChangedExternally, "Tab 1 must detect external modification");
        Assert.True(vm2.LinkedFileChangedExternally, "Tab 2 must detect external modification and not be blinded by Tab 1");
    }

    // ── 9. Atomic-Save Watcher Swallowing Guard ──────────────────────────

    [Fact]
    public void OnLinkedFileChanged_AtomicSave_DoesNotSwallowExternalChange()
    {
        string path = WriteTempFile("atomic_save.c", "const uint8_t spr[] = { 0x11 };");
        _vm.SpriteState.LinkedSourceFile = path;
        _vm.SpriteState.LinkedVariableName = "spr";
        _vm.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
        _vm.UpdateLinkedFileHashIfMatches(path);

        _dialogMock.Setup(d => d.ShowConfirmation(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        // Simulate external editor atomic save: delete target, then recreate with new content
        File.Delete(path);
        var deleteArgs = new FileSystemEventArgs(WatcherChangeTypes.Deleted, _tempDir, "atomic_save.c");
        _vm.OnLinkedFileChanged(this, deleteArgs);
        FlushDispatcher();
        Assert.True(_vm.IsLinkedFileMissing);

        File.WriteAllText(path, "const uint8_t spr[] = { 0x99 };");
        var createArgs = new FileSystemEventArgs(WatcherChangeTypes.Created, _tempDir, "atomic_save.c");
        _vm.OnLinkedFileChanged(this, createArgs);
        FlushDispatcher();

        // Must NOT be swallowed by wasMissing re-attaching watcher
        Assert.False(_vm.IsLinkedFileMissing);
        Assert.True(_vm.LinkedFileChangedExternally);
    }

    // ── 10. Hex Dimension Constant Regex Protection ──────────────────────

    [Fact]
    public void UpdateDimensionConstants_WithHexConstants_UpdatesWithoutSyntaxCorruption()
    {
        string code = """
            #define ICON_WIDTH 0x10
            #define ICON_HEIGHT 0x10
            const uint8_t icon[] = { 0x00 };
            """;
        string path = WriteTempFile("hex_constants.c", code);

        _fileSvc.UpdateSpriteInFile(path, "icon", "const uint8_t icon[] = { 0xFF };", newWidth: 24, newHeight: 32);

        string updated = File.ReadAllText(path);
        Assert.Contains("#define ICON_WIDTH 24", updated);
        Assert.Contains("#define ICON_HEIGHT 32", updated);
        Assert.DoesNotContain("24x10", updated);
        Assert.DoesNotContain("32x10", updated);
    }

    [Fact]
    public void UpdateDimensionConstants_ShorthandAndConstCpp_UpdatesCorrectly()
    {
        string code = """
            #define SPR_W 16
            #define SPR_H 16
            const int SPR_WIDTH = 16;
            constexpr int SPR_HEIGHT = 16;
            const uint8_t spr[] = { 0x00 };
            """;
        string path = WriteTempFile("shorthand_const.cpp", code);

        _fileSvc.UpdateSpriteInFile(path, "spr", "const uint8_t spr[] = { 0xFF };", newWidth: 32, newHeight: 64);

        string updated = File.ReadAllText(path);
        Assert.Contains("#define SPR_W 32", updated);
        Assert.Contains("#define SPR_H 64", updated);
        Assert.Contains("const int SPR_WIDTH = 32;", updated);
        Assert.Contains("constexpr int SPR_HEIGHT = 64;", updated);
    }

    // ── 11. BOM (Byte Order Mark) Preservation ───────────────────────────

    [Fact]
    public void SafeFileIo_PreservesUtf8BomOrNoBom()
    {
        // Case A: File created without BOM
        string noBomPath = Path.Combine(_tempDir, "nobom.c");
        File.WriteAllText(noBomPath, "const uint8_t spr[] = { 0x00 };", new System.Text.UTF8Encoding(false));
        Assert.False(SafeFileIo.HasUtf8Bom(noBomPath));

        _fileSvc.UpdateSpriteInFile(noBomPath, "spr", "const uint8_t spr[] = { 0xFF };");
        Assert.False(SafeFileIo.HasUtf8Bom(noBomPath), "File originally without BOM must remain without BOM after update");

        // Case B: File created with BOM
        string withBomPath = Path.Combine(_tempDir, "withbom.c");
        File.WriteAllText(withBomPath, "const uint8_t spr[] = { 0x00 };", new System.Text.UTF8Encoding(true));
        Assert.True(SafeFileIo.HasUtf8Bom(withBomPath));

        _fileSvc.UpdateSpriteInFile(withBomPath, "spr", "const uint8_t spr[] = { 0xFF };");
        Assert.True(SafeFileIo.HasUtf8Bom(withBomPath), "File originally with BOM must retain BOM after update");
    }

    // ── 12. ParseCodeToState FlipperXbm and RawHex ────────────────────────

    [Fact]
    public void ParseCodeToState_FlipperXbmAndRawHex_ParsesWithCorrectLayout()
    {
        var codeGen = new CodeGeneratorService();

        // FlipperXbm: 0x01 in XBM is LSB-first -> pixel (0, 0) is true, (7, 0) is false
        var stateXbm = new SpriteState(8, 8);
        MainViewModel.ParseCodeToState(codeGen, ExportFormat.FlipperXbm, "{ 0x01 }", stateXbm);
        Assert.True(stateXbm.Pixels[0], "XBM pixel at (0,0) must be true for 0x01 (LSB-first)");
        Assert.False(stateXbm.Pixels[7], "XBM pixel at (7,0) must be false for 0x01 (LSB-first)");

        // RawHex: 0x80 is MSB-first -> pixel (0, 0) is true
        var stateHex = new SpriteState(8, 8);
        MainViewModel.ParseCodeToState(codeGen, ExportFormat.RawHex, "0x80", stateHex);
        Assert.True(stateHex.Pixels[0], "RawHex pixel at (0,0) must be true for 0x80");
        Assert.False(stateHex.Pixels[7], "RawHex pixel at (7,0) must be false for 0x80");
    }

    // ── 13. Multi-Sprite Isolation & Read-Only Updates ───────────────────

    [Fact]
    public void UpdateSpriteInFile_MultiSpriteFile_LeavesOtherSpritesAndDefinesUntouched()
    {
        string code = """
            // Hero Sprite Header
            #define HERO_WIDTH 16
            #define HERO_HEIGHT 16
            const uint8_t hero_sprite[] = { 0x11, 0x22 };

            // Enemy Sprite Header
            #define ENEMY_WIDTH 8
            #define ENEMY_HEIGHT 8
            const uint8_t enemy_sprite[] = { 0x33, 0x44 };

            // Coin Sprite Header
            #define COIN_WIDTH 4
            #define COIN_HEIGHT 4
            const uint8_t coin_sprite[] = { 0x55, 0x66 };
            """;
        string path = WriteTempFile("multi_sprite.c", code);

        string newEnemy = "const uint8_t enemy_sprite[] = { 0xAA, 0xBB, 0xCC, 0xDD };";
        _fileSvc.UpdateSpriteInFile(path, "enemy_sprite", newEnemy, newWidth: 16, newHeight: 32);

        string updated = File.ReadAllText(path);

        // Enemy updated
        Assert.Contains("#define ENEMY_WIDTH 16", updated);
        Assert.Contains("#define ENEMY_HEIGHT 32", updated);
        Assert.Contains("0xAA, 0xBB, 0xCC, 0xDD", updated);

        // Hero untouched
        Assert.Contains("// Hero Sprite Header", updated);
        Assert.Contains("#define HERO_WIDTH 16", updated);
        Assert.Contains("#define HERO_HEIGHT 16", updated);
        Assert.Contains("const uint8_t hero_sprite[] = { 0x11, 0x22 };", updated);

        // Coin untouched
        Assert.Contains("// Coin Sprite Header", updated);
        Assert.Contains("#define COIN_WIDTH 4", updated);
        Assert.Contains("#define COIN_HEIGHT 4", updated);
        Assert.Contains("const uint8_t coin_sprite[] = { 0x55, 0x66 };", updated);
    }

    [Fact]
    public void UpdateSpriteInFile_ReadOnlyTarget_ClearsReadOnlyAndSucceeds()
    {
        string original = "const uint8_t spr[] = { 0x00, 0x00 };";
        string path = WriteTempFile("readonly_update_test.c", original);

        // Mark target file Read-Only
        File.SetAttributes(path, FileAttributes.ReadOnly);

        // Update should succeed and overwrite read-only file
        string modified = "const uint8_t spr[] = { 0xFF, 0xFF };";
        string updated = _fileSvc.UpdateSpriteInFile(path, "spr", modified);
        Assert.Contains("0xFF, 0xFF", updated);

        string diskContent = File.ReadAllText(path);
        Assert.Contains("0xFF, 0xFF", diskContent);
    }

    // ── 14. Code Formatter Whitespace & Data Change Detection ────────────

    [Fact]
    public void CheckForExternalChanges_WhitespaceAndFormattingChanges_DoesNotTriggerWarning()
    {
        string path = WriteTempFile("format_test.c", "const uint8_t spr[] = {\n    0x01, 0x02\n};\n");
        _vm.SpriteState.LinkedSourceFile = path;
        _vm.SpriteState.LinkedVariableName = "spr";
        _vm.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
        _vm.NotifyLinkChanged();

        Assert.False(_vm.LinkedFileChangedExternally);

        // Change formatting only: add extra whitespace, trailing commas, indentation, and newlines
        File.WriteAllText(path, "\n\n  const   uint8_t   spr[]   =   {\n\t\t0x01,\n\t\t0x02,\n  };\n\n");

        _vm.CheckForExternalChanges();

        Assert.False(_vm.LinkedFileChangedExternally, "Whitespace, newline, and comma-only changes should not trigger external change warning");
    }

    [Fact]
    public void CheckForExternalChanges_DataByteChange_TriggersWarning()
    {
        string path = WriteTempFile("data_change_test.c", "const uint8_t spr[] = { 0x01, 0x02 };");
        _vm.SpriteState.LinkedSourceFile = path;
        _vm.SpriteState.LinkedVariableName = "spr";
        _vm.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
        _vm.NotifyLinkChanged();

        Assert.False(_vm.LinkedFileChangedExternally);

        // Change data byte
        File.WriteAllText(path, "const uint8_t spr[] = { 0x01, 0xFF };");

        _vm.CheckForExternalChanges();

        Assert.True(_vm.LinkedFileChangedExternally, "Data byte modification must trigger external change warning");
    }

    // ── 15. State & ViewModel Validation (BUG-LS-01, BUG-LS-02, BUG-LS-03) ──

    [Fact]
    public void SpriteState_IsLinked_RequiresBothFileAndVariableName()
    {
        var state = new SpriteState(16, 16);
        Assert.False(state.IsLinked);

        state.LinkedSourceFile = "test.c";
        Assert.False(state.IsLinked, "IsLinked must be false when LinkedVariableName is null or empty");

        state.LinkedVariableName = "";
        Assert.False(state.IsLinked, "IsLinked must be false when LinkedVariableName is empty");

        state.LinkedVariableName = "   ";
        Assert.False(state.IsLinked, "IsLinked must be false when LinkedVariableName is whitespace");

        state.LinkedVariableName = "my_sprite";
        Assert.True(state.IsLinked, "IsLinked must be true when both file and variable are set");

        state.LinkedSourceFile = "   ";
        Assert.False(state.IsLinked, "IsLinked must be false when LinkedSourceFile is whitespace");
    }

    [Fact]
    public async Task ExecutePullLinkedSourceAsync_WhenNotLinkedOrStateNull_ReturnsSafelyWithoutException()
    {
        _vm.SpriteState.LinkedSourceFile = null;
        _vm.SpriteState.LinkedVariableName = null;
        _vm.SpriteState.LinkedFormat = null;

        var exception = await Record.ExceptionAsync(async () => await _vm.ExecutePullLinkedSourceAsync());
        Assert.Null(exception);

        _vm.SpriteState.LinkedSourceFile = "test.c";
        _vm.SpriteState.LinkedVariableName = null;

        exception = await Record.ExceptionAsync(async () => await _vm.ExecutePullLinkedSourceAsync());
        Assert.Null(exception);
    }

    [Fact]
    public async Task ExecutePullLinkedSourceAsync_WhenLinkedFormatIsNull_MatchesByNameAndPullsSuccessfully()
    {
        string path = WriteTempFile("null_format_pull.c", """
            #define SPR_TEST_WIDTH 16
            #define SPR_TEST_HEIGHT 16
            const uint8_t spr_test[] = { 0x80, 0x00 };
            """);

        _importExportMock.Setup(m => m.ExtractSpritesFromFile(It.IsAny<string>()))
            .Returns<string>(p => _fileSvc.ExtractSpritesFromFile(p));
        _codeGenMock.Setup(c => c.ParseAdafruitGfxToState(It.IsAny<string>(), It.IsAny<SpriteState>()))
            .Callback<string, SpriteState>((snippet, state) => new CodeGeneratorService().ParseAdafruitGfxToState(snippet, state));

        _vm.SpriteState.LinkedSourceFile = path;
        _vm.SpriteState.LinkedVariableName = "spr_test";
        _vm.SpriteState.LinkedFormat = null;
        _vm.NotifyLinkChanged();

        var exception = await Record.ExceptionAsync(async () => await _vm.ExecutePullLinkedSourceAsync());
        Assert.Null(exception);
        Assert.True(_vm.SpriteState.Pixels[0], "Pull should successfully match by variable name when LinkedFormat is null");
    }

    [Fact]
    public async Task ExecuteRestoreLinkedSourceAsync_WhenLinkedFormatIsNull_MatchesByNameAndRestoresSuccessfully()
    {
        string path = WriteTempFile("null_format_restore.c", """
            #define SPR_RESTORE_WIDTH 16
            #define SPR_RESTORE_HEIGHT 16
            const uint8_t spr_restore[] = { 0x80, 0x00 };
            """);

        _importExportMock.Setup(m => m.ExtractSpritesFromFile(It.IsAny<string>()))
            .Returns<string>(p => _fileSvc.ExtractSpritesFromFile(p));
        _importExportMock.Setup(m => m.RestoreSpriteInFile(It.IsAny<string>()))
            .Returns<string>(p => _fileSvc.RestoreSpriteInFile(p));
        _importExportMock.Setup(m => m.HasBackup(It.IsAny<string>()))
            .Returns<string>(p => _fileSvc.HasBackup(p));
        _importExportMock.Setup(m => m.UpdateSpriteInFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .Returns<string, string, string, int?, int?, int?>((p, v, c, w, h, f) => _fileSvc.UpdateSpriteInFile(p, v, c, w, h, f));
        _codeGenMock.Setup(c => c.ParseAdafruitGfxToState(It.IsAny<string>(), It.IsAny<SpriteState>()))
            .Callback<string, SpriteState>((snippet, state) => new CodeGeneratorService().ParseAdafruitGfxToState(snippet, state));

        _vm.SpriteState.LinkedSourceFile = path;
        _vm.SpriteState.LinkedVariableName = "spr_restore";
        _vm.SpriteState.LinkedFormat = null;
        _vm.NotifyLinkChanged();

        // Create backup by updating first
        _vm.SpriteState.Pixels[0] = false;
        await _vm.ExecuteUpdateLinkedSourceAsync();

        // Modify file on disk to simulate corrupted state
        File.WriteAllText(path, """
            #define SPR_RESTORE_WIDTH 16
            #define SPR_RESTORE_HEIGHT 16
            const uint8_t spr_restore[] = { 0x00, 0x00 };
            """);

        // Restore
        var exception = await Record.ExceptionAsync(async () => await _vm.ExecuteRestoreLinkedSourceAsync(skipConfirmation: true));
        Assert.Null(exception);
        Assert.True(_vm.SpriteState.Pixels[0], "Restore should successfully match by variable name when LinkedFormat is null");
    }

    [Fact]
    public void CheckForExternalChanges_WhenFileLocked_DoesNotBlockUIThread()
    {
        string path = WriteTempFile("locked_file.c", "const uint8_t spr[] = { 0xAA };");
        _vm.SpriteState.LinkedSourceFile = path;
        _vm.SpriteState.LinkedVariableName = "spr";
        _vm.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
        _vm.NotifyLinkChanged();

        // Lock file exclusively to simulate an external process writing or holding the file
        using var lockStream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        _vm.CheckForExternalChanges();
        sw.Stop();

        // Should return promptly without blocking UI thread through multi-attempt sleeping
        Assert.True(sw.ElapsedMilliseconds < 500, $"CheckForExternalChanges blocked UI thread for {sw.ElapsedMilliseconds}ms");
    }
}


