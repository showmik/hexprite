using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.Tests.E2E;
using Hexprite.ViewModels;
using Hexprite.ViewModels.Flipper;
using Hexprite.Views;
using Moq;
using Xunit;

namespace Hexprite.Tests
{
    [Collection("WindowLayoutSettingsFile")]
    [Trait("Category", "Unit")]
    public class WindowChromeConsistencyTests
    {
        public WindowChromeConsistencyTests()
        {
            WpfTestHelper.EnsureApplication();
            WpfTestHelper.RunOnSta(() =>
            {
                if (Application.Current != null && Application.Current.Resources.MergedDictionaries.Count == 0)
                {
                    Application.Current.Resources.MergedDictionaries.Add(
                        new ResourceDictionary { Source = new Uri("/Hexprite;component/Themes/Dim.xaml", UriKind.RelativeOrAbsolute) });
                    Application.Current.Resources.MergedDictionaries.Add(
                        new ResourceDictionary { Source = new Uri("/Hexprite;component/Themes/Styles.xaml", UriKind.RelativeOrAbsolute) });
                }
            });
        }

        [Fact]
        public void DialogWindowBorderStyle_ContainsMaximizedOverhangTrigger()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var style = Application.Current?.TryFindResource("DialogWindowBorderStyle") as Style;
                Assert.NotNull(style);

                var trigger = style.Triggers
                    .OfType<DataTrigger>()
                    .FirstOrDefault(t => t.Value?.ToString() == "Maximized" || (t.Value is WindowState state && state == WindowState.Maximized));

                Assert.NotNull(trigger);
                var paddingSetter = trigger.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == Border.PaddingProperty);
                Assert.NotNull(paddingSetter);
                Assert.Equal(new Thickness(8), paddingSetter.Value);

                var borderThicknessSetter = trigger.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == Border.BorderThicknessProperty);
                Assert.NotNull(borderThicknessSetter);
                Assert.Equal(new Thickness(0), borderThicknessSetter.Value);
            });
        }

        [Fact]
        public void FlipperMediaSlicerWindow_HasLayoutRounding_AndCloseButtonZeroVerticalPadding()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var win = new FlipperMediaSlicerWindow();
                Assert.NotNull(win);
                Assert.True(win.UseLayoutRounding, "UseLayoutRounding must be true");
                Assert.True(win.SnapsToDevicePixels, "SnapsToDevicePixels must be true");

                var closeBtn = FindButtonByContent(win, "Close");
                Assert.NotNull(closeBtn);
                Assert.Equal(0, closeBtn.Padding.Top);
                Assert.Equal(0, closeBtn.Padding.Bottom);
                Assert.True(closeBtn.Height >= 28);
                win.Close();
            });
        }

        [Fact]
        public void SpriteSheetSlicerWindow_HasLayoutRounding_AndCloseButtonZeroVerticalPadding()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var win = new SpriteSheetSlicerWindow();
                Assert.NotNull(win);
                Assert.True(win.UseLayoutRounding, "UseLayoutRounding must be true");
                Assert.True(win.SnapsToDevicePixels, "SnapsToDevicePixels must be true");

                var closeBtn = FindButtonByContent(win, "Close");
                Assert.NotNull(closeBtn);
                Assert.Equal(0, closeBtn.Padding.Top);
                Assert.Equal(0, closeBtn.Padding.Bottom);
                Assert.True(closeBtn.Height >= 28);
                win.Close();
            });
        }

        [Fact]
        public void FlipperScreenMirrorWindow_HasLayoutRounding_AndCloseButtonZeroVerticalPadding()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var win = new FlipperScreenMirrorWindow();
                Assert.NotNull(win);
                Assert.True(win.UseLayoutRounding, "UseLayoutRounding must be true");
                Assert.True(win.SnapsToDevicePixels, "SnapsToDevicePixels must be true");

                var closeBtn = FindButtonByContent(win, "Close");
                Assert.NotNull(closeBtn);
                Assert.Equal(0, closeBtn.Padding.Top);
                Assert.Equal(0, closeBtn.Padding.Bottom);
                Assert.True(closeBtn.Height >= 28);
                win.Close();
            });
        }

        [Fact]
        public void CodeOutputWindow_AndDisplaySimulationWindow_HaveLayoutRounding()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var shell = E2ETestHelper.CreateTestShellViewModel();
                shell.NewDocumentCommand.Execute("128x64");
                var mvm = shell.ActiveDocument as MainViewModel;
                Assert.NotNull(mvm);

                var codeWin = new CodeOutputWindow(mvm);
                Assert.NotNull(codeWin);
                Assert.True(codeWin.UseLayoutRounding, "CodeOutputWindow must have UseLayoutRounding");
                Assert.True(codeWin.SnapsToDevicePixels, "CodeOutputWindow must have SnapsToDevicePixels");
                codeWin.Close();

                var simWin = new DisplaySimulationWindow(mvm);
                Assert.NotNull(simWin);
                Assert.True(simWin.UseLayoutRounding, "DisplaySimulationWindow must have UseLayoutRounding");
                Assert.True(simWin.SnapsToDevicePixels, "DisplaySimulationWindow must have SnapsToDevicePixels");
                simWin.Close();

                mvm.Detach();
                shell.Detach();
            });
        }

        [Fact]
        public void ImportDialogs_HaveLayoutRoundingAndPixelSnapping()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var fromCode = new ImportFromCodeDialog();
                Assert.NotNull(fromCode);
                Assert.True(fromCode.UseLayoutRounding, "ImportFromCodeDialog must have UseLayoutRounding");
                Assert.True(fromCode.SnapsToDevicePixels, "ImportFromCodeDialog must have SnapsToDevicePixels");
                fromCode.Close();

                var mockImport = new Mock<IFileImportExportService>();
                var mockCodeGen = new Mock<ICodeGeneratorService>();
                var fromFile = new ImportFromFileDialog(mockImport.Object, mockCodeGen.Object);
                Assert.NotNull(fromFile);
                Assert.True(fromFile.UseLayoutRounding, "ImportFromFileDialog must have UseLayoutRounding");
                Assert.True(fromFile.SnapsToDevicePixels, "ImportFromFileDialog must have SnapsToDevicePixels");
                fromFile.Close();
            });
        }

        private static Button? FindButtonByContent(DependencyObject parent, string content)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(parent))
            {
                if (child is Button btn && string.Equals(btn.Content?.ToString(), content, StringComparison.OrdinalIgnoreCase))
                {
                    return btn;
                }

                if (child is DependencyObject d)
                {
                    var found = FindButtonByContent(d, content);
                    if (found != null) return found;
                }
            }
            return null;
        }
    }
}
