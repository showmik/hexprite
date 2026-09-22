using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests;

/// <summary>
/// Tests for AutosaveService: lifecycle management, .tmp recovery, round-trip
/// serialization, and clear/stop behavior. Uses an isolated temp directory to
/// avoid interfering with the real autosave folder.
/// </summary>
[Collection("AutosaveDirectory")]
[Trait("Category", "Integration")]
public sealed class AutosaveServiceTests : IDisposable
{
    private readonly string _testDir;

    public AutosaveServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "HexpriteAutosaveTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testDir);
        AutosaveService.SetCustomAutosaveDirectory(_testDir);
    }

    public void Dispose()
    {
        AutosaveService.SetCustomAutosaveDirectory(null);
        try { Directory.Delete(_testDir, recursive: true); } catch { }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a SpriteState with known pixel data for verifying round-trip fidelity.
    /// </summary>
    private static SpriteState CreateTestState(int width = 8, int height = 8)
    {
        var state = new SpriteState(width, height);
        state.EnsureLayers();

        // Set some pixels so we can verify they survive serialization
        state.Pixels[0] = true;
        state.Pixels[3] = true;
        state.Pixels[width * height - 1] = true;
        state.Frames[0].LayerPixels[0].GetMonochromeData()[0] = true;
        state.Frames[0].LayerPixels[0].GetMonochromeData()[3] = true;
        state.Frames[0].LayerPixels[0].GetMonochromeData()[width * height - 1] = true;

        state.ColorMode = ColorMode.Monochrome;
        state.IsDisplayInverted = true;
        state.IsAnimationEnabled = true;
        state.FrameRateFps = 24;
        return state;
    }

    /// <summary>
    /// Serializes a SpriteState to a .json file in the test directory, simulating
    /// what the autosave timer would write.
    /// </summary>
    private string WriteAutosaveJson(string fileName, SpriteState state)
    {
        string path = Path.Combine(_testDir, fileName);
        string json = JsonSerializer.Serialize(state);
        File.WriteAllText(path, json);
        return path;
    }

    // ── SpriteState Round-Trip Tests ─────────────────────────────────────

    [Fact]
    public void SpriteState_RoundTrips_ThroughJson_PreservesPixelData()
    {
        var original = CreateTestState(16, 8);
        string json = JsonSerializer.Serialize(original);

        var restored = JsonSerializer.Deserialize<SpriteState>(json);
        Assert.NotNull(restored);
        restored.EnsureLayers();

        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);
        Assert.Equal(original.Pixels.Length, restored.Pixels.Length);
        Assert.True(restored.Pixels[0], "Pixel [0] should survive round-trip");
        Assert.True(restored.Pixels[3], "Pixel [3] should survive round-trip");
        Assert.True(restored.Pixels[16 * 8 - 1], "Last pixel should survive round-trip");
    }

    [Fact]
    public void SpriteState_RoundTrips_ThroughJson_PreservesColorMode()
    {
        var original = CreateTestState();
        original.ColorMode = ColorMode.Monochrome;
        string json = JsonSerializer.Serialize(original);

        var restored = JsonSerializer.Deserialize<SpriteState>(json);
        Assert.NotNull(restored);
        Assert.Equal(ColorMode.Monochrome, restored.ColorMode);
    }

    [Fact]
    public void SpriteState_RoundTrips_ThroughJson_PreservesDisplayInverted()
    {
        var original = CreateTestState();
        original.IsDisplayInverted = true;
        string json = JsonSerializer.Serialize(original);

        var restored = JsonSerializer.Deserialize<SpriteState>(json);
        Assert.NotNull(restored);
        Assert.True(restored.IsDisplayInverted);
    }

    [Fact]
    public void SpriteState_RoundTrips_ThroughJson_PreservesAnimationSettings()
    {
        var original = CreateTestState();
        original.IsAnimationEnabled = true;
        original.FrameRateFps = 30;
        string json = JsonSerializer.Serialize(original);

        var restored = JsonSerializer.Deserialize<SpriteState>(json);
        Assert.NotNull(restored);
        Assert.True(restored.IsAnimationEnabled);
        Assert.Equal(30, restored.FrameRateFps);
    }

    [Fact]
    public void SpriteState_RoundTrips_ThroughJson_PreservesFrameData()
    {
        var original = CreateTestState();
        // Add a second frame
        var frame2 = new FrameState
        {
            Name = "Frame 2",
            LayerPixels = [new MonochromePixelBuffer(new bool[64])]
        };
        frame2.LayerPixels[0].GetMonochromeData()[7] = true;
        original.Frames.Add(frame2);

        string json = JsonSerializer.Serialize(original);

        var restored = JsonSerializer.Deserialize<SpriteState>(json);
        Assert.NotNull(restored);
        restored.EnsureLayers();

        Assert.Equal(2, restored.Frames.Count);
        Assert.Equal("Frame 2", restored.Frames[1].Name);
        Assert.True(restored.Frames[1].LayerPixels[0].GetMonochromeData()[7]);
    }

    // ── LoadAutosave Tests ──────────────────────────────────────────────

    [Fact]
    public void LoadAutosave_ValidFile_ReturnsState()
    {
        var svc = new AutosaveService();
        var original = CreateTestState();
        string path = WriteAutosaveJson("recovery_test.json", original);

        var loaded = svc.LoadAutosave(path);

        Assert.NotNull(loaded);
        Assert.Equal(original.Width, loaded.Width);
        Assert.Equal(original.Height, loaded.Height);
        Assert.True(loaded.Pixels[0]);
        Assert.True(loaded.Pixels[3]);
    }

    [Fact]
    public void LoadAutosave_MissingFile_ReturnsNull()
    {
        var svc = new AutosaveService();
        var result = svc.LoadAutosave(Path.Combine(_testDir, "nonexistent.json"));
        Assert.Null(result);
    }

    [Fact]
    public void LoadAutosave_CorruptedJson_ReturnsNull()
    {
        var svc = new AutosaveService();
        string path = Path.Combine(_testDir, "corrupt.json");
        File.WriteAllText(path, "{{not valid json}}");

        var result = svc.LoadAutosave(path);
        Assert.Null(result);
    }

    [Fact]
    public void LoadAutosave_EmptyFile_ReturnsNull()
    {
        var svc = new AutosaveService();
        string path = Path.Combine(_testDir, "empty.json");
        File.WriteAllText(path, "");

        var result = svc.LoadAutosave(path);
        Assert.Null(result);
    }

    // ── StopAutosaveLoop / Dispose Tests ─────────────────────────────────

    [Fact]
    public void StopAutosaveLoop_PreventsSubsequentTimerTicks()
    {
        var svc = new AutosaveService();
        int callCount = 0;

        // Start with a very short interval to verify stop actually works
        svc.StartAutosaveLoop(
            "stop-test",
            () => { Interlocked.Increment(ref callCount); return CreateTestState(); },
            () => true);

        svc.StopAutosaveLoop();

        // Wait long enough that at least one tick would have fired
        Thread.Sleep(200);
        int countAfterStop = callCount;
        Thread.Sleep(200);

        // After stopping, no new ticks should fire
        Assert.Equal(countAfterStop, callCount);

        svc.Dispose();
    }

    [Fact]
    public void Dispose_StopsTimer_NoExceptions()
    {
        var svc = new AutosaveService();
        svc.StartAutosaveLoop("dispose-test", () => CreateTestState(), () => false);

        // Dispose should not throw and should stop the timer
        var ex = Record.Exception(() => svc.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_BeforeStart_NoExceptions()
    {
        // Disposing without ever starting should be safe
        var svc = new AutosaveService();
        var ex = Record.Exception(() => svc.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void StopAutosaveLoop_BeforeStart_NoExceptions()
    {
        var svc = new AutosaveService();
        var ex = Record.Exception(() => svc.StopAutosaveLoop());
        Assert.Null(ex);
    }

    // ── ClearCurrentAutosave Tests ───────────────────────────────────────

    [Fact]
    public void ClearCurrentAutosave_DeletesFile()
    {
        var svc = new AutosaveService();
        svc.StartAutosaveLoop("clear-test", () => CreateTestState(), () => false);

        // Manually write a file where the service expects it
        string autosaveDir = _testDir;
        string expectedFile = Path.Combine(autosaveDir, "recovery_clear-test.json");

        try
        {
            Directory.CreateDirectory(autosaveDir);
            File.WriteAllText(expectedFile, "{}");
            Assert.True(File.Exists(expectedFile));

            svc.ClearCurrentAutosave();

            Assert.False(File.Exists(expectedFile));
        }
        finally
        {
            // Cleanup in case test fails
            try { File.Delete(expectedFile); } catch { }
            svc.Dispose();
        }
    }

    [Fact]
    public void ClearCurrentAutosave_WhenNoFile_DoesNotThrow()
    {
        var svc = new AutosaveService();
        svc.StartAutosaveLoop("no-file-test", () => CreateTestState(), () => false);

        var ex = Record.Exception(() => svc.ClearCurrentAutosave());
        Assert.Null(ex);
        svc.Dispose();
    }

    // ── GetAvailableAutosaves .tmp Recovery Tests ────────────────────────
    // These tests exercise the .tmp promotion logic in the isolated test directory.

    [Fact]
    public void GetAvailableAutosaves_PromotesOrphanedTmpFile()
    {
        var svc = new AutosaveService();
        string autosaveDir = _testDir;
        Directory.CreateDirectory(autosaveDir);

        string tmpFile = Path.Combine(autosaveDir, "recovery_tmp-test-promote.json.tmp");
        string jsonFile = Path.Combine(autosaveDir, "recovery_tmp-test-promote.json");

        try
        {
            // Simulate a mid-write crash: only .tmp exists
            var state = CreateTestState();
            File.WriteAllText(tmpFile, JsonSerializer.Serialize(state));
            // Ensure no .json exists
            if (File.Exists(jsonFile)) File.Delete(jsonFile);

            var available = svc.GetAvailableAutosaves().ToList();

            // The .tmp should have been promoted to .json
            Assert.False(File.Exists(tmpFile), ".tmp file should be renamed");
            Assert.True(File.Exists(jsonFile), ".json file should exist after promotion");
            Assert.Contains(jsonFile, available);
        }
        finally
        {
            try { File.Delete(tmpFile); } catch { }
            try { File.Delete(jsonFile); } catch { }
            svc.Dispose();
        }
    }

    [Fact]
    public void GetAvailableAutosaves_CleansUpStaleTmpWhenJsonExists()
    {
        var svc = new AutosaveService();
        string autosaveDir = _testDir;
        Directory.CreateDirectory(autosaveDir);

        string tmpFile = Path.Combine(autosaveDir, "recovery_tmp-test-stale.json.tmp");
        string jsonFile = Path.Combine(autosaveDir, "recovery_tmp-test-stale.json");

        try
        {
            // Both .json and .tmp exist (the .tmp is stale)
            var state = CreateTestState();
            string json = JsonSerializer.Serialize(state);
            File.WriteAllText(jsonFile, json);
            File.WriteAllText(tmpFile, json);

            var available = svc.GetAvailableAutosaves().ToList();

            // The stale .tmp should be cleaned up
            Assert.False(File.Exists(tmpFile), "Stale .tmp file should be deleted");
            Assert.True(File.Exists(jsonFile), ".json file should remain");
            Assert.Contains(jsonFile, available);
        }
        finally
        {
            try { File.Delete(tmpFile); } catch { }
            try { File.Delete(jsonFile); } catch { }
            svc.Dispose();
        }
    }

    [Fact]
    public void GetAvailableAutosaves_IgnoresEmptyTmpFile()
    {
        var svc = new AutosaveService();
        string autosaveDir = _testDir;
        Directory.CreateDirectory(autosaveDir);

        string tmpFile = Path.Combine(autosaveDir, "recovery_tmp-test-empty.json.tmp");
        string jsonFile = Path.Combine(autosaveDir, "recovery_tmp-test-empty.json");

        try
        {
            // Empty .tmp (crash happened before any data was written)
            File.WriteAllText(tmpFile, "");
            if (File.Exists(jsonFile)) File.Delete(jsonFile);

            svc.GetAvailableAutosaves().ToList();

            // Empty .tmp should NOT be promoted
            Assert.False(File.Exists(jsonFile), "Empty .tmp should not be promoted to .json");
        }
        finally
        {
            try { File.Delete(tmpFile); } catch { }
            try { File.Delete(jsonFile); } catch { }
            svc.Dispose();
        }
    }

    [Fact]
    public void GetAvailableAutosaves_IgnoresEmptyJsonFiles()
    {
        var svc = new AutosaveService();
        string autosaveDir = _testDir;
        Directory.CreateDirectory(autosaveDir);

        string emptyJson = Path.Combine(autosaveDir, "recovery_empty-test.json");

        try
        {
            File.WriteAllText(emptyJson, "");

            var available = svc.GetAvailableAutosaves().ToList();

            Assert.DoesNotContain(emptyJson, available);
        }
        finally
        {
            try { File.Delete(emptyJson); } catch { }
            svc.Dispose();
        }
    }

    // ── Full lifecycle integration test ──────────────────────────────────

    [Fact]
    public void FullLifecycle_StartLoop_ClearAutosave_StopLoop_Dispose()
    {
        var svc = new AutosaveService();
        string autosaveDir = _testDir;
        string expectedFile = Path.Combine(autosaveDir, "recovery_lifecycle-test.json");

        try
        {
            svc.StartAutosaveLoop("lifecycle-test", () => CreateTestState(), () => true);

            // Manually create the autosave file
            Directory.CreateDirectory(autosaveDir);
            File.WriteAllText(expectedFile, "{}");

            // Simulate Detach() sequence: stop → clear → dispose
            svc.StopAutosaveLoop();
            svc.ClearCurrentAutosave();
            (svc as IDisposable).Dispose();

            Assert.False(File.Exists(expectedFile), "Autosave should be deleted after Detach sequence");
        }
        finally
        {
            try { File.Delete(expectedFile); } catch { }
        }
    }

    // ── Multi-Document & Envelope Tests ──────────────────────────────────

    [Fact]
    public void AutosaveEnvelope_SpriteState_RoundTrips_WithFullMetadata()
    {
        var original = CreateTestState(16, 16);
        original.PlaybackDirection = PlaybackDirection.PingPong;
        original.FlipperCycle = new FlipperAnimationCycle
        {
            PassiveFrameCount = 3,
            ActiveFrameCount = 2,
            Duration = 5,
            FramesOrder = [0, 1, 2, 1, 0],
        };
        original.ExportSettings = new ExportSettings
        {
            SpriteName = "custom_dolphin_anim",
            Format = ExportFormat.AdafruitGfx,
        };

        var envelope = new AutosaveEnvelope
        {
            DocumentId = "test-doc-1",
            Mode = DocumentMode.Sprite,
            Title = "custom_dolphin_anim",
            FilePath = @"C:\dev\anims\custom_dolphin_anim.hexp",
            ParentPackName = "Dolphin Pack",
            ParentPackPath = @"C:\dev\my_pack.hexpack",
            PackEntryName = "custom_dolphin_anim",
            IsDirty = true,
            SpriteState = original,
        };

        string json = JsonSerializer.Serialize(envelope);
        string path = Path.Combine(_testDir, "envelope_sprite.json");
        File.WriteAllText(path, json);

        var svc = new AutosaveService();
        var restored = svc.LoadAutosaveEnvelope(path);

        Assert.NotNull(restored);
        Assert.Equal(DocumentMode.Sprite, restored.Mode);
        Assert.Equal("custom_dolphin_anim", restored.Title);
        Assert.Equal(@"C:\dev\anims\custom_dolphin_anim.hexp", restored.FilePath);
        Assert.Equal("Dolphin Pack", restored.ParentPackName);
        Assert.Equal(@"C:\dev\my_pack.hexpack", restored.ParentPackPath);
        Assert.Equal("custom_dolphin_anim", restored.PackEntryName);
        Assert.NotNull(restored.SpriteState);
        Assert.Equal(PlaybackDirection.PingPong, restored.SpriteState.PlaybackDirection);
        Assert.NotNull(restored.SpriteState.FlipperCycle);
        Assert.Equal(3, restored.SpriteState.FlipperCycle.PassiveFrameCount);
        Assert.Equal(2, restored.SpriteState.FlipperCycle.ActiveFrameCount);
        Assert.Equal("custom_dolphin_anim", restored.SpriteState.ExportSettings?.SpriteName);
    }

    [Fact]
    public void AutosaveEnvelope_AssetPackDocument_RoundTrips_WithEntriesAndAnimations()
    {
        var doc = AssetPackDocument.CreateNew("Custom Pack");
        doc.Entries.Add(new FlipperManifestEntry
        {
            Name = "anim_special",
            MinLevel = 1,
            MaxLevel = 30,
            Weight = 3,
        });
        var animState = CreateTestState(8, 8);
        doc.Animations["anim_special"] = animState;

        var envelope = new AutosaveEnvelope
        {
            DocumentId = "pack-doc-1",
            Mode = DocumentMode.AssetPack,
            Title = "Custom Pack (Pack)",
            FilePath = @"C:\dev\custom_pack.hexpack",
            IsDirty = true,
            AssetPackDocument = doc,
        };

        string json = JsonSerializer.Serialize(envelope);
        string path = Path.Combine(_testDir, "envelope_pack.json");
        File.WriteAllText(path, json);

        var svc = new AutosaveService();
        var restored = svc.LoadAutosaveEnvelope(path);

        Assert.NotNull(restored);
        Assert.Equal(DocumentMode.AssetPack, restored.Mode);
        Assert.Equal("Custom Pack (Pack)", restored.Title);
        Assert.Equal(@"C:\dev\custom_pack.hexpack", restored.FilePath);
        Assert.NotNull(restored.AssetPackDocument);
        Assert.Equal("Custom Pack", restored.AssetPackDocument.PackName);
        Assert.True(restored.AssetPackDocument.Animations.ContainsKey("anim_special"));
    }

    [Fact]
    public void AutosaveEnvelope_FontDocument_RoundTrips_WithGlyphsAndMetrics()
    {
        var doc = FontDocument.CreateNew(8, 12);
        doc.FontName = "Custom Monospace";
        doc.Baseline = 10;
        doc.YAdvance = 14;

        var envelope = new AutosaveEnvelope
        {
            DocumentId = "font-doc-1",
            Mode = DocumentMode.Font,
            Title = "*Custom Monospace (Font)",
            FilePath = @"C:\dev\custom_font.hexfont",
            IsDirty = true,
            FontDocument = doc,
        };

        string json = JsonSerializer.Serialize(envelope);
        string path = Path.Combine(_testDir, "envelope_font.json");
        File.WriteAllText(path, json);

        var svc = new AutosaveService();
        var restored = svc.LoadAutosaveEnvelope(path);

        Assert.NotNull(restored);
        Assert.Equal(DocumentMode.Font, restored.Mode);
        Assert.Equal("*Custom Monospace (Font)", restored.Title);
        Assert.Equal(@"C:\dev\custom_font.hexfont", restored.FilePath);
        Assert.NotNull(restored.FontDocument);
        Assert.Equal("Custom Monospace", restored.FontDocument.FontName);
        Assert.Equal(10, restored.FontDocument.Baseline);
        Assert.Equal(14, restored.FontDocument.YAdvance);
    }

    [Fact]
    public void AutosaveService_LegacyRawJson_LoadsAsSpriteEnvelope()
    {
        var original = CreateTestState(12, 12);
        string path = WriteAutosaveJson("legacy_raw.json", original);

        var svc = new AutosaveService();
        var envelope = svc.LoadAutosaveEnvelope(path);

        Assert.NotNull(envelope);
        Assert.Equal(DocumentMode.Sprite, envelope.Mode);
        Assert.NotNull(envelope.SpriteState);
        Assert.Equal(12, envelope.SpriteState.Width);
        Assert.Equal(12, envelope.SpriteState.Height);
        Assert.True(envelope.SpriteState.Pixels[0]);
    }

    [Fact]
    public async Task AutosaveService_SaveImmediatelyAsync_WritesEnvelopeImmediately()
    {
        using var svc = new AutosaveService();
        var state = CreateTestState(16, 16);
        bool isDirty = true;
        string docId = Guid.NewGuid().ToString("N");

        svc.StartAutosaveLoop(docId, () => state, () => isDirty);
        await svc.SaveImmediatelyAsync();

        var available = svc.GetAvailableAutosaves().ToList();
        Assert.Contains(available, f => f.Contains(docId));

        var loaded = svc.LoadAutosaveEnvelope(available.First(f => f.Contains(docId)));
        Assert.NotNull(loaded);
        Assert.Equal(16, loaded.SpriteState?.Width);

        svc.ClearCurrentAutosave();
    }

    [Fact]
    public void AutosaveService_ArchiveAllAutosaves_PreservesFilesSafely()
    {
        using var svc = new AutosaveService();
        var state = CreateTestState();
        string docId = Guid.NewGuid().ToString("N");

        svc.StartAutosaveLoop(docId, () => state, () => true);
        svc.TriggerImmediateAutosave();
        Thread.Sleep(200);

        svc.ArchiveAllAutosaves("UserDeclined");

        var availableAfterArchive = svc.GetAvailableAutosaves().ToList();
        Assert.Empty(availableAfterArchive);
    }
}
