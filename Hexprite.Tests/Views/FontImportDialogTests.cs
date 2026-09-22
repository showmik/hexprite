using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.Views;
using Moq;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FontImportDialogTests
    {
        [Fact]
        public void FontImportDialog_Initializes_WithCustomChromeAndDefaultSelection()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var mockService = new Mock<IFontImportService>();
                var dlg = new FontImportDialog(mockService.Object);

                try
                {
                    Assert.NotNull(dlg);
                    Assert.Equal(WindowStyle.None, dlg.WindowStyle);
                    Assert.False(dlg.AllowsTransparency);
                    Assert.Equal(FontImportMode.System, dlg.SelectedMode);

                    // Default font selection
                    Assert.NotNull(dlg.SelectedFontFamily);
                    Assert.NotNull(dlg.FontListBox.SelectedItem);
                    Assert.True(dlg.ImportButton.IsEnabled, "Import button should be enabled by default when a system font is auto-selected");

                    // Check default metric values
                    Assert.Equal(8, dlg.SelectedHeight);
                    Assert.Equal(32, dlg.SelectedFirstChar);
                    Assert.Equal(126, dlg.SelectedLastChar);
                    Assert.Equal(1, dlg.LetterSpacing);
                    Assert.Equal(128, dlg.Threshold);
                }
                finally
                {
                    dlg.Close();
                }
            });
        }

        [Fact]
        public void FontImportDialog_ModeSwitching_TogglesVisibilityAndPanels()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var mockService = new Mock<IFontImportService>();
                var dlg = new FontImportDialog(mockService.Object);

                try
                {
                    // 1. File Mode
                    dlg.SourceModeCombo.SelectedIndex = 1;
                    Assert.Equal(FontImportMode.File, dlg.SelectedMode);
                    Assert.Equal(Visibility.Collapsed, dlg.SystemFontPanel.Visibility);
                    Assert.Equal(Visibility.Visible, dlg.FilePickerPanel.Visibility);
                    Assert.Equal(Visibility.Collapsed, dlg.SpriteSettingsPanel.Visibility);
                    Assert.Equal(Visibility.Visible, dlg.CommonSettingsPanel.Visibility);
                    Assert.Equal(Visibility.Visible, dlg.HeightPanel.Visibility);

                    // 2. Sprite Sheet Mode
                    dlg.SourceModeCombo.SelectedIndex = 2;
                    Assert.Equal(FontImportMode.Sprite, dlg.SelectedMode);
                    Assert.Equal(Visibility.Collapsed, dlg.SystemFontPanel.Visibility);
                    Assert.Equal(Visibility.Visible, dlg.FilePickerPanel.Visibility);
                    Assert.Equal(Visibility.Visible, dlg.SpriteSettingsPanel.Visibility);
                    Assert.Equal(Visibility.Visible, dlg.CommonSettingsPanel.Visibility);
                    Assert.Equal(Visibility.Collapsed, dlg.HeightPanel.Visibility);

                    // 3. BMFont Mode
                    dlg.SourceModeCombo.SelectedIndex = 3;
                    Assert.Equal(FontImportMode.BMFont, dlg.SelectedMode);
                    Assert.Equal(Visibility.Collapsed, dlg.SystemFontPanel.Visibility);
                    Assert.Equal(Visibility.Visible, dlg.FilePickerPanel.Visibility);
                    Assert.Equal(Visibility.Collapsed, dlg.CommonSettingsPanel.Visibility);

                    // 4. Back to System Mode
                    dlg.SourceModeCombo.SelectedIndex = 0;
                    Assert.Equal(FontImportMode.System, dlg.SelectedMode);
                    Assert.Equal(Visibility.Visible, dlg.SystemFontPanel.Visibility);
                    Assert.Equal(Visibility.Collapsed, dlg.FilePickerPanel.Visibility);
                    Assert.Equal(Visibility.Visible, dlg.CommonSettingsPanel.Visibility);
                    Assert.Equal(Visibility.Visible, dlg.HeightPanel.Visibility);
                }
                finally
                {
                    dlg.Close();
                }
            });
        }

        [Fact]
        public void FontImportDialog_CheckImportReady_ValidatesFileExistence()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var mockService = new Mock<IFontImportService>();
                var dlg = new FontImportDialog(mockService.Object);

                try
                {
                    dlg.SourceModeCombo.SelectedIndex = 1; // File mode
                    Assert.False(dlg.ImportButton.IsEnabled, "Import button must be disabled when no file path is specified");

                    dlg.FilePathBox.Text = "C:\\non_existent_path_xyz_123.ttf";
                    Assert.False(dlg.ImportButton.IsEnabled, "Import button must be disabled when file does not exist");

                    var tempFile = Path.Combine(Path.GetTempPath(), $"font_test_{Guid.NewGuid():N}.ttf");
                    File.WriteAllText(tempFile, "fake font data");
                    try
                    {
                        dlg.FilePathBox.Text = tempFile;
                        Assert.True(dlg.ImportButton.IsEnabled, "Import button must be enabled when file exists");
                    }
                    finally
                    {
                        if (File.Exists(tempFile)) File.Delete(tempFile);
                    }
                }
                finally
                {
                    dlg.Close();
                }
            });
        }

        [Fact]
        public void FontImportDialog_Presets_UpdatePreviewText()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var mockService = new Mock<IFontImportService>();
                var dlg = new FontImportDialog(mockService.Object);

                try
                {
                    // Trigger preset buttons via reflection or direct call
                    var foxMethod = typeof(FontImportDialog).GetMethod("BtnPresetFox_Click", BindingFlags.NonPublic | BindingFlags.Instance);
                    Assert.NotNull(foxMethod);
                    foxMethod.Invoke(dlg, [dlg, new RoutedEventArgs()]);
                    Assert.Contains("fox", dlg.PreviewTextBox.Text, StringComparison.OrdinalIgnoreCase);

                    var digitsMethod = typeof(FontImportDialog).GetMethod("BtnPresetDigits_Click", BindingFlags.NonPublic | BindingFlags.Instance);
                    Assert.NotNull(digitsMethod);
                    digitsMethod.Invoke(dlg, [dlg, new RoutedEventArgs()]);
                    Assert.Contains("0123456789", dlg.PreviewTextBox.Text, StringComparison.Ordinal);

                    var symMethod = typeof(FontImportDialog).GetMethod("BtnPresetSymbols_Click", BindingFlags.NonPublic | BindingFlags.Instance);
                    Assert.NotNull(symMethod);
                    symMethod.Invoke(dlg, [dlg, new RoutedEventArgs()]);
                    Assert.Contains("!@#$", dlg.PreviewTextBox.Text, StringComparison.Ordinal);
                }
                finally
                {
                    dlg.Close();
                }
            });
        }

        [Fact]
        public void FontImportDialog_HandleDroppedFile_AutoSwitchesMode()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var mockService = new Mock<IFontImportService>();
                var dlg = new FontImportDialog(mockService.Object);

                try
                {
                    var dropMethod = typeof(FontImportDialog).GetMethod("HandleDroppedFile", BindingFlags.NonPublic | BindingFlags.Instance);
                    Assert.NotNull(dropMethod);

                    dropMethod.Invoke(dlg, ["test_font.ttf"]);
                    Assert.Equal(FontImportMode.File, dlg.SelectedMode);
                    Assert.Equal("test_font.ttf", dlg.FilePathBox.Text);

                    dropMethod.Invoke(dlg, ["sprites.png"]);
                    Assert.Equal(FontImportMode.Sprite, dlg.SelectedMode);
                    Assert.Equal("sprites.png", dlg.FilePathBox.Text);

                    dropMethod.Invoke(dlg, ["font.fnt"]);
                    Assert.Equal(FontImportMode.BMFont, dlg.SelectedMode);
                    Assert.Equal("font.fnt", dlg.FilePathBox.Text);
                }
                finally
                {
                    dlg.Close();
                }
            });
        }

        [Fact]
        public void FontImportDialog_InvertToggle_TogglesStateAndRenders()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var mockService = new Mock<IFontImportService>();
                var doc = FontDocument.CreateNew(8, 8, 32, 126);
                mockService.Setup(s => s.ImportFromTrueType(It.IsAny<System.Windows.Media.FontFamily>(), It.IsAny<FontImportOptions>()))
                           .Returns(doc);

                var dlg = new FontImportDialog(mockService.Object);

                try
                {
                    var invertMethod = typeof(FontImportDialog).GetMethod("InvertToggle_Click", BindingFlags.NonPublic | BindingFlags.Instance);
                    Assert.NotNull(invertMethod);

                    dlg.InvertToggle.IsChecked = true;
                    invertMethod.Invoke(dlg, [dlg, new RoutedEventArgs()]);

                    var invertedField = typeof(FontImportDialog).GetField("_previewInverted", BindingFlags.NonPublic | BindingFlags.Instance);
                    Assert.NotNull(invertedField);
                    Assert.True((bool)invertedField.GetValue(dlg)!);
                }
                finally
                {
                    dlg.Close();
                }
            });
        }

        [Fact]
        public void FontImportDialog_ClosesAndStopsTimerCleanly()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var mockService = new Mock<IFontImportService>();
                var dlg = new FontImportDialog(mockService.Object);

                var timerField = typeof(FontImportDialog).GetField("_previewTimer", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(timerField);
                var timer = (System.Windows.Threading.DispatcherTimer)timerField.GetValue(dlg)!;
                Assert.NotNull(timer);

                dlg.Close();

                Assert.False(timer.IsEnabled, "DispatcherTimer must be stopped when dialog closes");
            });
        }
    }
}
