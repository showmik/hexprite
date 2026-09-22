using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class BitmapImportSettingsPersistenceTests
    {
        [Fact]
        public void LoadBitmapImportSettings_WhenJsonContainsSmallMaxDimension_ResetsToSpriteStateMaxDimension()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid():N}.json");
            try
            {
                var customSettings = new BitmapImportSettings
                {
                    Preset = ImportPreset.RetroMac,
                    MaxDimension = 16,
                    Threshold = 140,
                    Brightness = 10,
                    Contrast = 20
                };
                File.WriteAllText(tempFile, JsonSerializer.Serialize(customSettings));

                var loaded = ShellViewModel.LoadBitmapImportSettings(tempFile);

                // Algorithmic preferences are loaded from JSON
                Assert.Equal(ImportPreset.RetroMac, loaded.Preset);
                Assert.Equal(140, loaded.Threshold);
                Assert.Equal(10, loaded.Brightness);
                Assert.Equal(20, loaded.Contrast);

                // MaxDimension MUST NOT be loaded as 16; it must default to SpriteState.MaxDimension
                // so subsequent imports are not poisoned by previous file sizes.
                Assert.Equal(SpriteState.MaxDimension, loaded.MaxDimension);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void SaveBitmapImportSettings_NormalizesMaxDimension_ToSpriteStateMaxDimension()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid():N}.json");
            try
            {
                var settingsToSave = new BitmapImportSettings
                {
                    Preset = ImportPreset.PixelArt,
                    MaxDimension = 16, // E.g., user imported a 16x16 icon
                    Threshold = 150
                };

                ShellViewModel.SaveBitmapImportSettings(settingsToSave, tempFile);

                var loaded = ShellViewModel.LoadBitmapImportSettings(tempFile);
                Assert.Equal(ImportPreset.PixelArt, loaded.Preset);
                Assert.Equal(150, loaded.Threshold);
                Assert.Equal(SpriteState.MaxDimension, loaded.MaxDimension);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
    }
}
