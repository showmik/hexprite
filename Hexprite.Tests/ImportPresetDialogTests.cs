using Hexprite.Services;
using Hexprite.Views;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;
using BitmapScalingMode = Hexprite.Services.BitmapScalingMode;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class ImportPresetDialogTests
    {
        private static string CreateTempPng()
        {
            string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
            var bitmap = new WriteableBitmap(2, 2, 96, 96, PixelFormats.Bgra32, null);
            using var stream = File.OpenWrite(path);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
            return path;
        }

        [Fact]
        public void ImportBitmapDialog_InitializesWithPreset_AndPopulatesTooltips()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                string tempFile = CreateTempPng();
                try
                {
                    var initialSettings = new BitmapImportSettings
                    {
                        Preset = ImportPreset.RetroMac,
                        DitheringAlgorithm = BitmapDitheringAlgorithm.Atkinson,
                        Contrast = 15,
                        Sharpen = true
                    };

                    using var dlg = new ImportBitmapDialog(tempFile, initialSettings);
                    var presetCombo = (ComboBox)dlg.FindName("PresetCombo");
                    var scalingCombo = (ComboBox)dlg.FindName("ScalingCombo");

                    Assert.NotNull(presetCombo);
                    Assert.NotNull(scalingCombo);

                    Assert.Equal(ImportPreset.RetroMac, presetCombo.SelectedValue);
                    Assert.Equal(ImportPresetHelper.GetPresetDescription(ImportPreset.RetroMac), presetCombo.ToolTip);

                    // Verify items have descriptions
                    var items = presetCombo.ItemsSource?.Cast<object>().ToList();
                    Assert.NotNull(items);
                    Assert.NotEmpty(items);
                    foreach (var item in items)
                    {
                        var descProp = item.GetType().GetProperty("Description");
                        Assert.NotNull(descProp);
                        string? descVal = descProp.GetValue(item) as string;
                        Assert.False(string.IsNullOrWhiteSpace(descVal));
                    }
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
            });
        }

        [Fact]
        public void ImportBitmapDialog_SelectingPixelArtPreset_UpdatesScalingToNearestNeighbor()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                string tempFile = CreateTempPng();
                try
                {
                    var initialSettings = new BitmapImportSettings
                    {
                        Preset = ImportPreset.Default,
                        ScalingMode = BitmapScalingMode.Fant
                    };

                    using var dlg = new ImportBitmapDialog(tempFile, initialSettings);
                    var presetCombo = (ComboBox)dlg.FindName("PresetCombo");
                    var scalingCombo = (ComboBox)dlg.FindName("ScalingCombo");

                    Assert.Equal(BitmapScalingMode.Fant, scalingCombo.SelectedItem);

                    // Switch preset to PixelArt
                    presetCombo.SelectedValue = ImportPreset.PixelArt;

                    Assert.Equal(ImportPreset.PixelArt, presetCombo.SelectedValue);
                    Assert.Equal(BitmapScalingMode.NearestNeighbor, scalingCombo.SelectedItem);
                    Assert.Equal(ImportPresetHelper.GetPresetDescription(ImportPreset.PixelArt), presetCombo.ToolTip);

                    // TryReadSettings reflects active preset
                    var tryReadMethod = typeof(ImportBitmapDialog).GetMethod("TryReadSettings", BindingFlags.NonPublic | BindingFlags.Instance);
                    Assert.NotNull(tryReadMethod);
                    object?[] args = new object?[] { null };
                    bool success = (bool)tryReadMethod.Invoke(dlg, args)!;
                    Assert.True(success);
                    var settings = args[0] as BitmapImportSettings;
                    Assert.NotNull(settings);
                    Assert.Equal(ImportPreset.PixelArt, settings.Preset);
                    Assert.Equal(BitmapScalingMode.NearestNeighbor, settings.ScalingMode);
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
            });
        }

        [Fact]
        public void ImportBitmapDialog_TweakingSetting_SwitchesToCustom()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                string tempFile = CreateTempPng();
                try
                {
                    var initialSettings = new BitmapImportSettings
                    {
                        Preset = ImportPreset.Default
                    };

                    using var dlg = new ImportBitmapDialog(tempFile, initialSettings);
                    var presetCombo = (ComboBox)dlg.FindName("PresetCombo");
                    var sldBrightness = (Slider)dlg.FindName("SldBrightness");

                    Assert.Equal(ImportPreset.Default, presetCombo.SelectedValue);

                    // User tweaks slider
                    sldBrightness.Value = 35;

                    Assert.Equal(ImportPreset.Custom, presetCombo.SelectedValue);
                    Assert.Equal(ImportPresetHelper.GetPresetDescription(ImportPreset.Custom), presetCombo.ToolTip);
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
            });
        }

        [Fact]
        public void ImportAnimationDialog_InitializesWithPreset_AndUpdatesScalingOnPresetChange()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                string tempFile = CreateTempPng();
                try
                {
                    var initialSettings = new AnimationImportSettings
                    {
                        Preset = ImportPreset.Photo,
                        DitheringAlgorithm = BitmapDitheringAlgorithm.FloydSteinberg,
                        ScalingMode = BitmapScalingMode.Fant,
                        Contrast = 5,
                        Sharpen = true
                    };

                    using var dlg = new ImportAnimationDialog(tempFile, initialSettings);
                    var presetCombo = (ComboBox)dlg.FindName("PresetCombo");
                    var scalingCombo = (ComboBox)dlg.FindName("ScalingCombo");

                    Assert.Equal(ImportPreset.Photo, presetCombo.SelectedValue);
                    Assert.Equal(ImportPresetHelper.GetPresetDescription(ImportPreset.Photo), presetCombo.ToolTip);

                    // Switch preset to PixelArt
                    presetCombo.SelectedValue = ImportPreset.PixelArt;

                    Assert.Equal(ImportPreset.PixelArt, presetCombo.SelectedValue);
                    Assert.Equal(BitmapScalingMode.NearestNeighbor, scalingCombo.SelectedItem);
                    Assert.Equal(ImportPresetHelper.GetPresetDescription(ImportPreset.PixelArt), presetCombo.ToolTip);

                    // TryReadSettings reflects active preset
                    var tryReadMethod = typeof(ImportAnimationDialog).GetMethod("TryReadSettings", BindingFlags.NonPublic | BindingFlags.Instance);
                    Assert.NotNull(tryReadMethod);
                    object?[] args = new object?[] { null };
                    bool success = (bool)tryReadMethod.Invoke(dlg, args)!;
                    Assert.True(success);
                    var settings = args[0] as AnimationImportSettings;
                    Assert.NotNull(settings);
                    Assert.Equal(ImportPreset.PixelArt, settings.Preset);
                    Assert.Equal(BitmapScalingMode.NearestNeighbor, settings.ScalingMode);
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
            });
        }

        [Fact]
        public void ImportAnimationDialog_TweakingSetting_SwitchesToCustom()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                string tempFile = CreateTempPng();
                try
                {
                    var initialSettings = new AnimationImportSettings
                    {
                        Preset = ImportPreset.RetroMac
                    };

                    using var dlg = new ImportAnimationDialog(tempFile, initialSettings);
                    var presetCombo = (ComboBox)dlg.FindName("PresetCombo");
                    var sldContrast = (Slider)dlg.FindName("SldContrast");

                    Assert.Equal(ImportPreset.RetroMac, presetCombo.SelectedValue);

                    // User tweaks slider
                    sldContrast.Value = 50;

                    Assert.Equal(ImportPreset.Custom, presetCombo.SelectedValue);
                    Assert.Equal(ImportPresetHelper.GetPresetDescription(ImportPreset.Custom), presetCombo.ToolTip);
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
            });
        }

        [Fact]
        public void ImportBitmapDialog_ResetClick_ResetsToDefaultPreset()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                string tempFile = CreateTempPng();
                try
                {
                    var initialSettings = new BitmapImportSettings
                    {
                        Preset = ImportPreset.Photo
                    };

                    using var dlg = new ImportBitmapDialog(tempFile, initialSettings);
                    var presetCombo = (ComboBox)dlg.FindName("PresetCombo");

                    Assert.Equal(ImportPreset.Photo, presetCombo.SelectedValue);

                    var resetMethod = typeof(ImportBitmapDialog).GetMethod("Reset_Click", BindingFlags.NonPublic | BindingFlags.Instance);
                    Assert.NotNull(resetMethod);
                    resetMethod.Invoke(dlg, new object?[] { null, new System.Windows.RoutedEventArgs() });

                    Assert.Equal(ImportPreset.Default, presetCombo.SelectedValue);
                    Assert.Equal(ImportPresetHelper.GetPresetDescription(ImportPreset.Default), presetCombo.ToolTip);
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
            });
        }

        [Fact]
        public void ImportAnimationDialog_ResetClick_ResetsToDefaultPreset()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                string tempFile = CreateTempPng();
                try
                {
                    var initialSettings = new AnimationImportSettings
                    {
                        Preset = ImportPreset.Photo
                    };

                    using var dlg = new ImportAnimationDialog(tempFile, initialSettings);
                    var presetCombo = (ComboBox)dlg.FindName("PresetCombo");

                    Assert.Equal(ImportPreset.Photo, presetCombo.SelectedValue);

                    var resetMethod = typeof(ImportAnimationDialog).GetMethod("Reset_Click", BindingFlags.NonPublic | BindingFlags.Instance);
                    Assert.NotNull(resetMethod);
                    resetMethod.Invoke(dlg, new object?[] { null, new System.Windows.RoutedEventArgs() });

                    Assert.Equal(ImportPreset.Default, presetCombo.SelectedValue);
                    Assert.Equal(ImportPresetHelper.GetPresetDescription(ImportPreset.Default), presetCombo.ToolTip);
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
            });
        }
    }
}
