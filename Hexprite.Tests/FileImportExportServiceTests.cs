using System;
using System.IO;
using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Integration")]
public sealed class FileImportExportServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileImportExportService _svc;

    public FileImportExportServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HexpriteTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _svc = new FileImportExportService();
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

    // ── ExtractSpritesFromFile ───────────────────────────────────────────

    [Fact]
    public void ExtractSprites_CommentedOutArray_IsIgnored()
    {
        // A sprite array inside a block comment should NOT be extracted.
        string code = """
            /* const uint8_t PROGMEM old_sprite[] = { 0xFF, 0x00 }; */
            const uint8_t PROGMEM real_sprite[] = { 0xAA, 0xBB };
            """;
        string path = WriteTempFile("commented.c", code);

        var sprites = _svc.ExtractSpritesFromFile(path);

        Assert.Single(sprites);
        Assert.Equal("real_sprite", sprites[0].Name);
    }

    [Fact]
    public void ExtractSprites_LineCommentedArray_IsIgnored()
    {
        string code = """
            // const uint8_t PROGMEM ghost[] = { 0xFF };
            const uint8_t PROGMEM visible[] = { 0x01 };
            """;
        string path = WriteTempFile("line_commented.c", code);

        var sprites = _svc.ExtractSpritesFromFile(path);

        Assert.Single(sprites);
        Assert.Equal("visible", sprites[0].Name);
    }

    [Fact]
    public void ExtractSprites_CustomTypedef_IsDetected()
    {
        // A user-defined typedef like `u8` should be matched by the loosened regex.
        string code = "u8 my_icon[] = { 0xAA, 0xBB, 0xCC, 0xDD };";
        string path = WriteTempFile("typedef.c", code);

        var sprites = _svc.ExtractSpritesFromFile(path);

        Assert.Single(sprites);
        Assert.Equal("my_icon", sprites[0].Name);
    }

    [Fact]
    public void ExtractSprites_NestedBraces_IsDetected()
    {
        // Two levels of nesting: outer { inner { ... } }
        string code = """
            const uint8_t PROGMEM tileset[2][4] = {
                { 0x01, 0x02, 0x03, 0x04 },
                { 0x05, 0x06, 0x07, 0x08 }
            };
            """;
        string path = WriteTempFile("nested.c", code);

        var sprites = _svc.ExtractSpritesFromFile(path);

        Assert.Single(sprites);
        Assert.Equal("tileset", sprites[0].Name);
    }

    [Fact]
    public void ExtractSprites_PythonBytearray_IsDetected()
    {
        string code = "my_py = bytearray([ 0x01, 0x02 ])";
        string path = WriteTempFile("py.py", code);

        var sprites = _svc.ExtractSpritesFromFile(path);

        Assert.Single(sprites);
        Assert.Equal("my_py", sprites[0].Name);
        Assert.Equal(ExportFormat.MicroPython, sprites[0].Format);
    }

    // ── UpdateSpriteInFile / RestoreSpriteInFile ────────────────────────

    [Fact]
    public void UpdateAndRestore_RoundTrips_Correctly()
    {
        string original = """
            const uint8_t PROGMEM heart[] = { 0x00, 0x00 };
            """;
        string path = WriteTempFile("roundtrip.c", original);

        string updated = "const uint8_t PROGMEM heart[] = { 0xFF, 0xFF };";
        _svc.UpdateSpriteInFile(path, "heart", updated);

        // File should now contain the updated bytes
        string afterUpdate = File.ReadAllText(path);
        Assert.Contains("0xFF", afterUpdate);
        Assert.DoesNotContain("0x00", afterUpdate);

        // Restore should bring the original back
        _svc.RestoreSpriteInFile(path);
        string afterRestore = File.ReadAllText(path);
        Assert.Contains("0x00", afterRestore);
    }

    [Fact]
    public void Restore_WithoutBackup_Throws()
    {
        string path = WriteTempFile("nobackup.c", "const uint8_t x[] = { 0x01 };");
        Assert.Throws<InvalidOperationException>(() => _svc.RestoreSpriteInFile(path));
    }

    [Fact]
    public void UpdateSprite_UnknownVariable_Throws()
    {
        string code = "const uint8_t PROGMEM real[] = { 0xAA };";
        string path = WriteTempFile("unknown.c", code);

        Assert.Throws<InvalidOperationException>(() =>
            _svc.UpdateSpriteInFile(path, "does_not_exist", "const uint8_t does_not_exist[] = { 0xFF };"));
    }

    [Fact]
    public void UpdateSprite_PreservesRowComments_WhenUpdating()
    {
        string original = """
            const uint8_t PROGMEM sprite[] = {
                0x00, 0x00, // row 1
                0x11, 0x11, // row 2
                0x22, 0x22  // row 3
            };
            """;
        string path = WriteTempFile("rowcomments.c", original);

        string newSnippet = """
            const uint8_t PROGMEM sprite[] = {
                0xFF, 0xFF,
                0xAA, 0xAA,
                0xBB, 0xBB
            };
            """;
        
        _svc.UpdateSpriteInFile(path, "sprite", newSnippet);

        string afterUpdate = File.ReadAllText(path);
        
        Assert.Contains("0xFF, 0xFF, // row 1", afterUpdate);
        Assert.Contains("0xAA, 0xAA, // row 2", afterUpdate);
        Assert.Contains("0xBB, 0xBB // row 3", afterUpdate);
    }

    [Fact]
    public void UpdateSprite_CustomType_Works()
    {
        string code = "byte my_icon[] = { 0x00 };";
        string path = WriteTempFile("custom_update.c", code);

        string updated = "byte my_icon[] = { 0xFF };";
        _svc.UpdateSpriteInFile(path, "my_icon", updated);

        string result = File.ReadAllText(path);
        Assert.Contains("0xFF", result);
        Assert.DoesNotContain("0x00", result);
    }

    // ── Deterministic backup path ──────────────────────────────────────

    [Fact]
    public void HasBackup_ReturnsFalse_WhenNoUpdatePerformed()
    {
        string path = WriteTempFile("pristine.c", "const uint8_t x[] = { 0x01 };");
        Assert.False(_svc.HasBackup(path));
    }

    [Fact]
    public void HasBackup_ReturnsTrue_AfterUpdate()
    {
        string code = "const uint8_t x[] = { 0x01 };";
        string path = WriteTempFile("backed.c", code);

        _svc.UpdateSpriteInFile(path, "x", "const uint8_t x[] = { 0xFF };");

        Assert.True(_svc.HasBackup(path));
    }

    // ── Dimension constant updating ────────────────────────────────────

    [Fact]
    public void UpdateSprite_UpdatesCDefineWidth_WhenDimensionsProvided()
    {
        string code = """
            #define ICON_WIDTH  16
            #define ICON_HEIGHT 16
            const uint8_t PROGMEM icon[] = { 0x00, 0xFF };
            """;
        string path = WriteTempFile("defines.c", code);

        _svc.UpdateSpriteInFile(path, "icon",
            "const uint8_t PROGMEM icon[] = { 0xAA, 0xBB, 0xCC, 0xDD };",
            newWidth: 32, newHeight: 32);

        string result = File.ReadAllText(path);
        Assert.Contains("#define ICON_WIDTH  32", result);
        Assert.Contains("#define ICON_HEIGHT 32", result);
        Assert.Contains("0xAA", result);
    }

    [Fact]
    public void UpdateSprite_UpdatesXbmStyleDefines()
    {
        string code = """
            #define icon_width  16
            #define icon_height 16
            const uint8_t icon[] PROGMEM = { 0x00 };
            """;
        string path = WriteTempFile("xbm.c", code);

        _svc.UpdateSpriteInFile(path, "icon",
            "const uint8_t icon[] PROGMEM = { 0xFF };",
            newWidth: 24, newHeight: 8);

        string result = File.ReadAllText(path);
        Assert.Contains("#define icon_width  24", result);
        Assert.Contains("#define icon_height 8", result);
    }

    [Fact]
    public void UpdateSprite_UpdatesPythonDimensionConstants()
    {
        string code = """
            SPRITE_WIDTH = 16
            SPRITE_HEIGHT = 16
            sprite = bytearray([ 0x00, 0xFF ])
            """;
        string path = WriteTempFile("pydefines.py", code);

        _svc.UpdateSpriteInFile(path, "sprite",
            "sprite = bytearray([ 0xAA, 0xBB ])",
            newWidth: 24, newHeight: 12);

        string result = File.ReadAllText(path);
        Assert.Contains("SPRITE_WIDTH = 24", result);
        Assert.Contains("SPRITE_HEIGHT = 12", result);
    }

    [Fact]
    public void UpdateSprite_WithoutDimensions_LeavesDefinesUnchanged()
    {
        string code = """
            #define ICON_WIDTH  16
            #define ICON_HEIGHT 16
            const uint8_t PROGMEM icon[] = { 0x00 };
            """;
        string path = WriteTempFile("nodims.c", code);

        // Call without dimensions (backward compat)
        _svc.UpdateSpriteInFile(path, "icon",
            "const uint8_t PROGMEM icon[] = { 0xFF };");

        string result = File.ReadAllText(path);
        Assert.Contains("#define ICON_WIDTH  16", result);
        Assert.Contains("#define ICON_HEIGHT 16", result);
    }

    [Fact]
    public void UpdateSprite_OnlyUpdatesMatchingVariable_NotOthers()
    {
        string code = """
            #define PLAY_WIDTH  8
            #define PLAY_HEIGHT 8
            const uint8_t PROGMEM play[] = { 0x00 };

            #define PAUSE_WIDTH  16
            #define PAUSE_HEIGHT 16
            const uint8_t PROGMEM pause[] = { 0x00, 0xFF };
            """;
        string path = WriteTempFile("multi.c", code);

        _svc.UpdateSpriteInFile(path, "play",
            "const uint8_t PROGMEM play[] = { 0xFF };",
            newWidth: 32, newHeight: 32);

        string result = File.ReadAllText(path);
        // play's defines should be updated
        Assert.Contains("#define PLAY_WIDTH  32", result);
        Assert.Contains("#define PLAY_HEIGHT 32", result);
        // pause's defines should be untouched
        Assert.Contains("#define PAUSE_WIDTH  16", result);
        Assert.Contains("#define PAUSE_HEIGHT 16", result);
    }
}
