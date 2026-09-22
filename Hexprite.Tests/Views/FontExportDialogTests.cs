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
    public class FontExportDialogTests
    {
        private static FontDocument CreateSampleFontDocument()
        {
            var doc = FontDocument.CreateNew(8, 12, 65, 67); // 'A', 'B', 'C'
            doc.FontName = "RetroSans";
            doc.Baseline = 10;
            doc.YAdvance = 12;

            var glyphA = doc.Glyphs[0];
            glyphA.Pixels[0] = true;
            glyphA.XAdvance = 7;

            return doc;
        }

        [Fact]
        public void FontExportDialog_Throws_OnNullDocumentOrService()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var service = new FontCodeGeneratorService();
                Assert.Throws<ArgumentNullException>(() => new FontExportDialog(null!, service));
                Assert.Throws<ArgumentNullException>(() => new FontExportDialog(CreateSampleFontDocument(), null!));
            });
        }

        [Fact]
        public void FontExportDialog_Initializes_WithCustomChromeAndDefaultSettings()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var doc = CreateSampleFontDocument();
                var service = new FontCodeGeneratorService();
                var dlg = new FontExportDialog(doc, service);

                try
                {
                    Assert.NotNull(dlg);
                    Assert.Equal(WindowStyle.None, dlg.WindowStyle);
                    Assert.False(dlg.AllowsTransparency);
                    Assert.Equal(FontExportFormat.AdafruitGfx, dlg.CurrentSettings.Format);
                    Assert.Equal("RetroSans", dlg.CurrentSettings.FontName);

                    // Verify Code Box
                    Assert.False(string.IsNullOrWhiteSpace(dlg.GeneratedCode));
                    Assert.Contains("RetroSans", dlg.GeneratedCode);
                    Assert.Contains("GFXglyph", dlg.GeneratedCode);
                    Assert.Contains("GFXfont", dlg.GeneratedCode);

                    // Verify Title and Output Filename
                    Assert.Equal("RetroSans.h", dlg.TxtOutputFileName.Text);
                    Assert.Contains("EXPORT FONT", dlg.TitleTextBlock.Text);

                    // Verify Metrics & Flash Budget
                    Assert.Equal("3", dlg.GlyphCountRun.Text);
                    Assert.Equal("8×12 px", dlg.CellDimensionsRun.Text);
                    Assert.False(string.IsNullOrWhiteSpace(dlg.FlashBytesRun.Text));
                    Assert.NotEqual("0", dlg.FlashBytesRun.Text);

                    // Verify Integration Snippet
                    Assert.Contains("display.setFont(&RetroSans);", dlg.SnippetTextBox.Text);
                }
                finally
                {
                    dlg.Close();
                }
            });
        }

        [Fact]
        public void FontExportDialog_FormatSwitching_RegeneratesCodeAndBadge()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var doc = CreateSampleFontDocument();
                var service = new FontCodeGeneratorService();
                var dlg = new FontExportDialog(doc, service);

                try
                {
                    // 1. Switch to U8g2 BDF
                    dlg.FormatComboBox.SelectedItem = FontExportFormat.U8g2Bdf;
                    Assert.Equal(FontExportFormat.U8g2Bdf, dlg.CurrentSettings.Format);
                    Assert.Equal("RetroSans.bdf", dlg.TxtOutputFileName.Text);
                    Assert.Contains("BDF", dlg.TxtFormatBadge.Text);
                    Assert.Contains("STARTFONT", dlg.GeneratedCode);
                    Assert.Contains("ENDFONT", dlg.GeneratedCode);
                    Assert.Contains("bdfconv", dlg.SnippetTextBox.Text);

                    // 2. Switch to LVGL
                    dlg.FormatComboBox.SelectedItem = FontExportFormat.Lvgl;
                    Assert.Equal(FontExportFormat.Lvgl, dlg.CurrentSettings.Format);
                    Assert.Contains("LVGL", dlg.TxtFormatBadge.Text);
                    Assert.Contains("lv_font_t", dlg.GeneratedCode);
                    Assert.Contains("lv_obj_set_style_text_font", dlg.SnippetTextBox.Text);

                    // 3. Switch to Raw C Array
                    dlg.FormatComboBox.SelectedItem = FontExportFormat.RawCArray;
                    Assert.Equal(FontExportFormat.RawCArray, dlg.CurrentSettings.Format);
                    Assert.Contains("const uint8_t", dlg.GeneratedCode);
                    Assert.Contains("RetroSans_glyphs", dlg.SnippetTextBox.Text);

                    // 4. Switch to Flipper Zero
                    dlg.FormatComboBox.SelectedItem = FontExportFormat.FlipperZero;
                    Assert.Equal(FontExportFormat.FlipperZero, dlg.CurrentSettings.Format);
                    Assert.Contains("Flipper", dlg.TxtFormatBadge.Text);
                    Assert.Contains("canvas_set_font_custom", dlg.SnippetTextBox.Text);
                }
                finally
                {
                    dlg.Close();
                }
            });
        }

        [Fact]
        public void FontExportDialog_FontNameChange_SanitisesAndUpdatesIdentifier()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var doc = CreateSampleFontDocument();
                var service = new FontCodeGeneratorService();
                var dlg = new FontExportDialog(doc, service);

                try
                {
                    dlg.FontNameTextBox.Text = "Custom Font 123";
                    // Trigger LostFocus to trigger sanitisation
                    var lostFocusMethod = dlg.GetType().GetMethod("FontNameTextBox_LostFocus", BindingFlags.NonPublic | BindingFlags.Instance);
                    lostFocusMethod?.Invoke(dlg, [dlg.FontNameTextBox, new RoutedEventArgs()]);

                    Assert.Equal("Custom_Font_123", dlg.FontNameTextBox.Text);
                    Assert.Equal("Custom_Font_123.h", dlg.TxtOutputFileName.Text);
                    Assert.Contains("Custom_Font_123", dlg.GeneratedCode);
                    Assert.Contains("Custom_Font_123", dlg.SnippetTextBox.Text);
                    Assert.Equal("Custom_Font_123", doc.FontName);
                }
                finally
                {
                    dlg.Close();
                }
            });
        }

        [Fact]
        public void FontExportDialog_ToggleOptions_UpdatesGeneratedCode()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var doc = CreateSampleFontDocument();
                var service = new FontCodeGeneratorService();
                var dlg = new FontExportDialog(doc, service);

                try
                {
                    // Toggle Uppercase Hex off (switch to Lowercase)
                    dlg.RbHexLower.IsChecked = true;
                    Assert.False(dlg.CurrentSettings.UppercaseHex);

                    // Toggle Glyph previews off
                    dlg.IncludeGlyphPreviewCheckBox.IsChecked = false;
                    Assert.False(dlg.CurrentSettings.IncludeGlyphPreview);

                    // Toggle Metric comments off
                    dlg.IncludeMetricCommentsCheckBox.IsChecked = false;
                    Assert.False(dlg.CurrentSettings.IncludeMetricComments);

                    // Toggle Usage guide off
                    dlg.IncludeUsageCommentCheckBox.IsChecked = false;
                    Assert.False(dlg.CurrentSettings.IncludeUsageComment);

                    // Code was regenerated with new settings
                    Assert.DoesNotContain("USAGE GUIDE", dlg.GeneratedCode);
                }
                finally
                {
                    dlg.Close();
                }
            });
        }

        [Fact]
        public void FontExportDialog_SearchInCode_FindsAndNavigatesMatches()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var doc = CreateSampleFontDocument();
                var service = new FontCodeGeneratorService();
                var dlg = new FontExportDialog(doc, service);

                try
                {
                    // Open Find Bar
                    dlg.BtnToggleFind.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(Visibility.Visible, dlg.FindBar.Visibility);

                    // Search for "RetroSans"
                    dlg.TxtSearch.Text = "RetroSans";
                    Assert.Contains("1 of", dlg.TxtMatchCount.Text);

                    // Cycle to next match
                    var findNextMethod = dlg.GetType().GetMethod("FindNext_Click", BindingFlags.NonPublic | BindingFlags.Instance);
                    findNextMethod?.Invoke(dlg, [dlg, new RoutedEventArgs()]);
                    Assert.Contains("2 of", dlg.TxtMatchCount.Text);

                    // Close Find Bar
                    var closeFindMethod = dlg.GetType().GetMethod("CloseFind_Click", BindingFlags.NonPublic | BindingFlags.Instance);
                    closeFindMethod?.Invoke(dlg, [dlg, new RoutedEventArgs()]);
                    Assert.Equal(Visibility.Collapsed, dlg.FindBar.Visibility);
                }
                finally
                {
                    dlg.Close();
                }
            });
        }

        [Fact]
        public void FontExportDialog_WordWrap_TogglesTextWrapping()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var doc = CreateSampleFontDocument();
                var service = new FontCodeGeneratorService();
                var dlg = new FontExportDialog(doc, service);

                try
                {
                    Assert.Equal(TextWrapping.NoWrap, dlg.CodeTextBox.TextWrapping);

                    dlg.ChkWordWrap.IsChecked = true;
                    Assert.Equal(TextWrapping.Wrap, dlg.CodeTextBox.TextWrapping);

                    dlg.ChkWordWrap.IsChecked = false;
                    Assert.Equal(TextWrapping.NoWrap, dlg.CodeTextBox.TextWrapping);
                }
                finally
                {
                    dlg.Close();
                }
            });
        }

        [Fact]
        public void FontExportDialog_PreviewMouseWheel_ScrollsAndHandlesEvents()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var doc = CreateSampleFontDocument();
                var service = new FontCodeGeneratorService();
                var dlg = new FontExportDialog(doc, service);

                try
                {
                    Assert.Equal(12.0, dlg.CodeTextBox.FontSize);

                    // 1. Test Wheel Down -> LineDown
                    var wheelDown = new System.Windows.Input.MouseWheelEventArgs(
                        System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120)
                    {
                        RoutedEvent = UIElement.PreviewMouseWheelEvent,
                        Source = dlg.CodeTextBox
                    };
                    dlg.CodeTextBox.RaiseEvent(wheelDown);
                    Assert.True(wheelDown.Handled, "Mouse wheel down should be handled and command LineDown on CodeTextBox");

                    // 2. Test Wheel Up -> LineUp
                    var wheelUp = new System.Windows.Input.MouseWheelEventArgs(
                        System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, 120)
                    {
                        RoutedEvent = UIElement.PreviewMouseWheelEvent,
                        Source = dlg.CodeTextBox
                    };
                    dlg.CodeTextBox.RaiseEvent(wheelUp);
                    Assert.True(wheelUp.Handled, "Mouse wheel up should be handled and command LineUp on CodeTextBox");

                    // 3. Test Container Border also catches PreviewMouseWheel
                    var borderWheel = new System.Windows.Input.MouseWheelEventArgs(
                        System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120)
                    {
                        RoutedEvent = UIElement.PreviewMouseWheelEvent
                    };
                    var border = (Border)dlg.CodeTextBox.Parent;
                    border.RaiseEvent(borderWheel);
                    Assert.True(borderWheel.Handled, "Container border should also handle PreviewMouseWheel for margin hover scrolling");
                }
                finally
                {
                    dlg.Close();
                }
            });
        }

        [Fact]
        public void FontExportDialog_LvglFormat_DefaultsToDotCFileAndDeclaresFont()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var doc = CreateSampleFontDocument();
                var service = new FontCodeGeneratorService();
                var dlg = new FontExportDialog(doc, service);

                try
                {
                    dlg.FormatComboBox.SelectedItem = FontExportFormat.Lvgl;
                    Assert.Equal(FontExportFormat.Lvgl, dlg.CurrentSettings.Format);
                    Assert.Equal("RetroSans.c", dlg.TxtOutputFileName.Text);
                    Assert.Contains("LV_FONT_DECLARE(RetroSans);", dlg.SnippetTextBox.Text);
                }
                finally
                {
                    dlg.Close();
                }
            });
        }

        [Fact]
        public void FontExportDialog_EmptyFontName_FallsBackToMyFont()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var doc = CreateSampleFontDocument();
                var service = new FontCodeGeneratorService();
                var dlg = new FontExportDialog(doc, service);

                try
                {
                    dlg.FontNameTextBox.Text = "";
                    var lostFocusMethod = dlg.GetType().GetMethod("FontNameTextBox_LostFocus", BindingFlags.NonPublic | BindingFlags.Instance);
                    lostFocusMethod?.Invoke(dlg, [dlg.FontNameTextBox, new RoutedEventArgs()]);

                    Assert.Equal("myFont", dlg.FontNameTextBox.Text);
                    Assert.Equal("myFont.h", dlg.TxtOutputFileName.Text);
                    Assert.Equal("myFont", doc.FontName);
                }
                finally
                {
                    dlg.Close();
                }
            });
        }

        [Fact]
        public void FontExportDialog_InitialDirectory_CanBeSet()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var doc = CreateSampleFontDocument();
                var service = new FontCodeGeneratorService();
                var dlg = new FontExportDialog(doc, service)
                {
                    InitialDirectory = @"C:\Fonts"
                };

                try
                {
                    Assert.Equal(@"C:\Fonts", dlg.InitialDirectory);
                }
                finally
                {
                    dlg.Close();
                }
            });
        }
    }
}
