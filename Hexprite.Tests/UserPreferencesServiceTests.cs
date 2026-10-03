using Hexprite.Core;
using Hexprite.Services;
using System;
using System.IO;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
public class UserPreferencesServiceTests : IDisposable
{
    private readonly string _tempFile;

    public UserPreferencesServiceTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"user_prefs_test_{Guid.NewGuid():N}.json");
        UserPreferencesService.SetCustomSettingsPath(_tempFile);
    }

    public void Dispose()
    {
        UserPreferencesService.SetCustomSettingsPath(null);
        try { if (File.Exists(_tempFile)) File.Delete(_tempFile); } catch { }
        try { if (File.Exists(_tempFile + ".bak")) File.Delete(_tempFile + ".bak"); } catch { }
    }

    [Fact]
    public void GetDefaultSettings_ReturnsIsolatedClones()
    {
        var exp1 = UserPreferencesService.GetDefaultExportSettings();
        exp1.BytesPerLine = 32;
        var exp2 = UserPreferencesService.GetDefaultExportSettings();
        Assert.NotEqual(32, exp2.BytesPerLine);

        var img1 = UserPreferencesService.GetDefaultImageExportSettings();
        img1.Scale = 8;
        var img2 = UserPreferencesService.GetDefaultImageExportSettings();
        Assert.NotEqual(8, img2.Scale);

        var bmp1 = UserPreferencesService.GetDefaultBitmapImportSettings();
        bmp1.Threshold = 200;
        var bmp2 = UserPreferencesService.GetDefaultBitmapImportSettings();
        Assert.NotEqual(200, bmp2.Threshold);

        var anim1 = UserPreferencesService.GetDefaultAnimationImportSettings();
        anim1.TargetFps = 20;
        var anim2 = UserPreferencesService.GetDefaultAnimationImportSettings();
        Assert.NotEqual(20, anim2.TargetFps);
    }

    [Fact]
    public void SaveImportSettings_PreservesAnimationPropertiesAndSyncsBase()
    {
        var anim = new AnimationImportSettings
        {
            Threshold = 180,
            DitheringAlgorithm = BitmapDitheringAlgorithm.Atkinson,
            TargetFps = 15,
            MaxFrames = 64,
            UniformSampling = false,
            TrimTrailingBlankFrames = false,
        };

        UserPreferencesService.SaveImportSettings(anim);

        var savedAnim = UserPreferencesService.GetDefaultAnimationImportSettings();
        Assert.Equal(180, savedAnim.Threshold);
        Assert.Equal(BitmapDitheringAlgorithm.Atkinson, savedAnim.DitheringAlgorithm);
        Assert.Equal(15, savedAnim.TargetFps);
        Assert.Equal(64, savedAnim.MaxFrames);
        Assert.False(savedAnim.UniformSampling);
        Assert.False(savedAnim.TrimTrailingBlankFrames);

        var savedBmp = UserPreferencesService.GetDefaultBitmapImportSettings();
        Assert.Equal(180, savedBmp.Threshold);
        Assert.Equal(BitmapDitheringAlgorithm.Atkinson, savedBmp.DitheringAlgorithm);
    }

    [Fact]
    public void ResetDefaults_RestoresFactorySettings()
    {
        UserPreferencesService.Update(p =>
        {
            p.DefaultExportSettings.BytesPerLine = 32;
            p.DefaultImageExportSettings.Scale = 8;
            p.DefaultBitmapImportSettings.Threshold = 40;
            p.DefaultAnimationImportSettings.TargetFps = 24;
        });

        UserPreferencesService.ResetDefaultExportSettings();
        Assert.Equal(0, UserPreferencesService.GetDefaultExportSettings().BytesPerLine);

        UserPreferencesService.ResetDefaultImageExportSettings();
        Assert.Equal(4, UserPreferencesService.GetDefaultImageExportSettings().Scale);

        UserPreferencesService.ResetDefaultImportSettings();
        Assert.Equal(128, UserPreferencesService.GetDefaultBitmapImportSettings().Threshold);
        Assert.Equal(8, UserPreferencesService.GetDefaultAnimationImportSettings().TargetFps);
    }

    [Fact]
    public void MigrateLegacyBitmapImportSettings_ImportsValuesAndRemovesLegacyFile()
    {
        string legacyFile = Path.Combine(Path.GetTempPath(), $"legacy_import_{Guid.NewGuid():N}.json");
        try
        {
            var legacy = new BitmapImportSettings
            {
                Threshold = 142,
                DitheringAlgorithm = BitmapDitheringAlgorithm.Bayer,
                Contrast = 25,
                Invert = true
            };
            File.WriteAllText(legacyFile, System.Text.Json.JsonSerializer.Serialize(legacy));

            bool migrated = UserPreferencesService.MigrateLegacyImportSettings(legacyFile);
            Assert.True(migrated);

            var loadedBmp = UserPreferencesService.GetDefaultBitmapImportSettings();
            Assert.Equal(142, loadedBmp.Threshold);
            Assert.Equal(BitmapDitheringAlgorithm.Bayer, loadedBmp.DitheringAlgorithm);
            Assert.Equal(25, loadedBmp.Contrast);
            Assert.True(loadedBmp.Invert);

            var loadedAnim = UserPreferencesService.GetDefaultAnimationImportSettings();
            Assert.Equal(142, loadedAnim.Threshold);
            Assert.True(loadedAnim.Invert);

            Assert.False(File.Exists(legacyFile));
        }
        finally
        {
            try { if (File.Exists(legacyFile)) File.Delete(legacyFile); } catch { }
        }
    }

    [Fact]
    public void SaveImportSettings_PersistsMaxDimension()
    {
        var settings = new BitmapImportSettings { MaxDimension = 64 };
        UserPreferencesService.SaveImportSettings(settings);
        var loaded = UserPreferencesService.GetDefaultBitmapImportSettings();
        Assert.Equal(64, loaded.MaxDimension);

        var anim = new AnimationImportSettings { MaxDimension = 96 };
        UserPreferencesService.SaveImportSettings(anim);
        var loadedAnim = UserPreferencesService.GetDefaultAnimationImportSettings();
        Assert.Equal(96, loadedAnim.MaxDimension);
    }
}


