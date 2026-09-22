using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Hexprite.Tests.E2E;
using Moq;
using Xunit;

namespace Hexprite.Tests;

[Collection("AutosaveDirectory")]
[Trait("Category", "Integration")]
public sealed class ShellAutosaveRecoveryTests : IDisposable
{
    private readonly string _testAutosavesDir;

    public ShellAutosaveRecoveryTests()
    {
        _testAutosavesDir = Path.Combine(
            Path.GetTempPath(),
            "Hexprite_AutosaveRecovery_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testAutosavesDir);
        AutosaveService.SetCustomAutosaveDirectory(_testAutosavesDir);
    }

    public void Dispose()
    {
        AutosaveService.SetCustomAutosaveDirectory(null);
        try
        {
            if (Directory.Exists(_testAutosavesDir))
            {
                Directory.Delete(_testAutosavesDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void CheckAutosaves_RecoversMultiTabSession_AssetPackAndAnimation_WithFullFidelity()
    {
        // 1. Arrange mock dialog service confirming recovery
        var mockDialog = new Mock<IDialogService>();
        mockDialog.Setup(d => d.ShowConfirmation(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        // 2. Prepare AssetPack envelope
        var packDoc = AssetPackDocument.CreateNew("Flipper Game Pack");
        packDoc.Entries.Clear();
        packDoc.Entries.Add(new FlipperManifestEntry
        {
            Name = "anim_dolphin",
            MinLevel = 1,
            MaxLevel = 3,
            Weight = 5,
        });
        var packFile = Path.Combine(_testAutosavesDir, "recovery_test_pack.json");
        var packEnvelope = new AutosaveEnvelope
        {
            DocumentId = "test_pack",
            Mode = DocumentMode.AssetPack,
            Title = "Flipper Game Pack (Pack)",
            FilePath = @"C:\dev\game.hexpack",
            IsDirty = true,
            AssetPackDocument = packDoc,
        };
        File.WriteAllText(packFile, JsonSerializer.Serialize(packEnvelope));

        // 3. Prepare Animation envelope opened from pack
        var animSprite = new SpriteState(128, 64)
        {
            ColorMode = ColorMode.Monochrome,
            IsAnimationEnabled = true,
            FrameRateFps = 12,
            PlaybackDirection = PlaybackDirection.PingPong,
            FlipperCycle = new FlipperAnimationCycle
            {
                PassiveFrameCount = 2,
                ActiveFrameCount = 1,
                Duration = 3,
                FramesOrder = [0, 1, 0],
            },
            ExportSettings = new ExportSettings
            {
                SpriteName = "anim_dolphin",
                Format = ExportFormat.AdafruitGfx,
            }
        };
        animSprite.Frames =
        [
            new FrameState { Name = "Frame 1" },
            new FrameState { Name = "Frame 2" }
        ];
        animSprite.Layers =
        [
            new LayerState { Name = "Base Layer", IsVisible = true },
            new LayerState { Name = "Detail Layer", IsVisible = true, OpacityMode = LayerOpacityMode.Sparse }
        ];
        animSprite.EnsureLayers();
        animSprite.Frames[0].LayerPixels[0].GetMonochromeData()[0] = true;
        animSprite.Frames[1].LayerPixels[1].GetMonochromeData()[10] = true;

        var animFile = Path.Combine(_testAutosavesDir, "recovery_test_anim.json");
        var animEnvelope = new AutosaveEnvelope
        {
            DocumentId = "test_anim",
            Mode = DocumentMode.Sprite,
            Title = "*anim_dolphin",
            ParentPackName = "Flipper Game Pack",
            ParentPackPath = @"C:\dev\game.hexpack",
            PackEntryName = "anim_dolphin",
            IsDirty = true,
            SpriteState = animSprite,
        };
        File.WriteAllText(animFile, JsonSerializer.Serialize(animEnvelope));

        // 4. Create ShellViewModel and perform CheckAutosaves
        var shell = E2ETestHelper.CreateTestShellViewModel(
            autosaveService: new AutosaveService(),
            dialogService: mockDialog.Object);

        shell.CheckAutosaves();

        // 5. Assert: Both documents recovered
        Assert.Equal(2, shell.OpenDocuments.Count);

        var recoveredPack = shell.OpenDocuments.OfType<AssetPackViewModel>().FirstOrDefault();
        Assert.NotNull(recoveredPack);
        Assert.Equal("Flipper Game Pack", recoveredPack.MatrixViewModel.PackName);
        Assert.Single(recoveredPack.MatrixViewModel.Entries);
        Assert.Equal("anim_dolphin", recoveredPack.MatrixViewModel.Entries[0].Name);

        var recoveredSpriteDoc = shell.OpenDocuments.OfType<MainViewModel>().FirstOrDefault();
        Assert.NotNull(recoveredSpriteDoc);
        Assert.Equal("anim_dolphin", recoveredSpriteDoc.SpriteName);
        Assert.True(recoveredSpriteDoc.IsAnimationEnabled);
        Assert.Equal(12, recoveredSpriteDoc.FrameRateFps);
        Assert.Equal(PlaybackDirection.PingPong, recoveredSpriteDoc.PlaybackDirection);
        Assert.NotNull(recoveredSpriteDoc.SpriteState?.FlipperCycle);
        Assert.Equal(2, recoveredSpriteDoc.SpriteState.FlipperCycle.PassiveFrameCount);
        Assert.Equal(2, recoveredSpriteDoc.Layers.Count);
        Assert.Equal("Base Layer", recoveredSpriteDoc.Layers[0].Name);
        Assert.Equal("Detail Layer", recoveredSpriteDoc.Layers[1].Name);
        Assert.Equal(@"C:\dev\game.hexpack", recoveredSpriteDoc.ParentPackPath);
        Assert.Equal("anim_dolphin", recoveredSpriteDoc.PackEntryName);

        // 6. Verify workspace sync: The pack matrix should have the edited animation sprite synced
        Assert.True(recoveredPack.MatrixViewModel.AnimationSprites.ContainsKey("anim_dolphin"));
        var syncedSprite = recoveredPack.MatrixViewModel.AnimationSprites["anim_dolphin"];
        Assert.NotNull(syncedSprite);
        Assert.Equal(2, syncedSprite.Frames.Count);

        // 7. Cleanup tabs & shell to cancel background autosave timers
        foreach (var doc in shell.OpenDocuments.ToList())
        {
            if (doc is MainViewModel mvm) mvm.Detach();
            else if (doc is IDisposable d) d.Dispose();
        }
        shell.Detach();
    }

    [Fact]
    public void CheckAutosaves_WhenUserDeclines_ArchivesAutosavesWithoutOpeningTabs()
    {
        string promptShown = string.Empty;
        var mockDialog = new Mock<IDialogService>();
        mockDialog.Setup(d => d.ShowConfirmation(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((msg, _) => promptShown = msg)
            .Returns(false);

        var packDoc = AssetPackDocument.CreateNew("Declined Pack");
        var packFile = Path.Combine(_testAutosavesDir, "recovery_test_declined.json");
        var packEnvelope = new AutosaveEnvelope
        {
            DocumentId = "test_declined",
            Mode = DocumentMode.AssetPack,
            Title = "Declined Pack",
            IsDirty = true,
            AssetPackDocument = packDoc,
        };
        File.WriteAllText(packFile, JsonSerializer.Serialize(packEnvelope));

        var shell = E2ETestHelper.CreateTestShellViewModel(
            autosaveService: new AutosaveService(),
            dialogService: mockDialog.Object);

        shell.CheckAutosaves();

        Assert.Empty(shell.OpenDocuments);
        Assert.Contains("Declined Pack", promptShown);
        Assert.False(File.Exists(packFile)); // Successfully moved to Archive
        shell.Detach();
    }
}
