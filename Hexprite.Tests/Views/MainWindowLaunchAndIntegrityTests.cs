using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    public class MainWindowLaunchAndIntegrityTests
    {
        public MainWindowLaunchAndIntegrityTests()
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
        public void MainWindow_InstantiatesWithoutXamlException_AndMenuExcludesCommunityHub()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var shell = E2ETestHelper.CreateTestShellViewModel();
                var mainWindow = new MainWindow(shell);

                Assert.NotNull(mainWindow);

                var menu = FindLogicalOrVisualChildren<Menu>(mainWindow).FirstOrDefault();
                Assert.NotNull(menu);

                // Find Tools menu
                var toolsMenu = menu.Items
                    .OfType<MenuItem>()
                    .FirstOrDefault(m => (m.Header as string)?.Contains("Tools", StringComparison.OrdinalIgnoreCase) == true);

                Assert.NotNull(toolsMenu);

                var toolHeaders = toolsMenu.Items
                    .OfType<MenuItem>()
                    .Select(m => m.Header as string ?? "")
                    .ToList();

                // R2 verification: Community Hub menu item must NOT exist
                Assert.DoesNotContain(toolHeaders, h => h.Contains("Community", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(toolHeaders, h => h.Contains("Hub", StringComparison.OrdinalIgnoreCase));

                // Verify File menu does not contain Community Hub
                var fileMenu = menu.Items
                    .OfType<MenuItem>()
                    .FirstOrDefault(m => (m.Header as string)?.Contains("File", StringComparison.OrdinalIgnoreCase) == true);
                Assert.NotNull(fileMenu);

                var fileSubItems = fileMenu.Items.OfType<MenuItem>().ToList();
                var fileHeaders = fileSubItems.Select(m => m.Header as string ?? "").ToList();
                Assert.DoesNotContain(fileHeaders, h => h.Contains("Community", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(fileHeaders, h => h.Contains("Hub", StringComparison.OrdinalIgnoreCase));

                // Verify File -> Import subitems do not contain Community Hub
                var importMenu = fileSubItems.FirstOrDefault(m => (m.Header as string)?.Contains("Import", StringComparison.OrdinalIgnoreCase) == true);
                Assert.NotNull(importMenu);
                var importHeaders = importMenu.Items.OfType<MenuItem>().Select(m => m.Header as string ?? "").ToList();
                Assert.DoesNotContain(importHeaders, h => h.Contains("Community", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(importHeaders, h => h.Contains("Hub", StringComparison.OrdinalIgnoreCase));

                // Verify input key bindings do not contain Community Hub commands
                foreach (var inputBinding in mainWindow.InputBindings.OfType<KeyBinding>())
                {
                    var cmdString = inputBinding.Command?.ToString() ?? "";
                    Assert.DoesNotContain("Community", cmdString, StringComparison.OrdinalIgnoreCase);
                }

                // Verify other Flipper tools are present and commands are properly bound
                Assert.NotNull(shell.OpenFlipperMatrixSimulatorCommand);
                Assert.NotNull(shell.OpenFlipperScreenMirrorCommand);
                Assert.NotNull(shell.DeployFlipperUsbCommand);
                Assert.NotNull(shell.OpenSpriteSheetSlicerCommand);

                var matrixItem = toolsMenu.Items.OfType<MenuItem>().First(m => (m.Header as string)?.Contains("Matrix Simulator") == true);
                var matrixBinding = System.Windows.Data.BindingOperations.GetBinding(matrixItem, MenuItem.CommandProperty);
                Assert.NotNull(matrixBinding);
                Assert.Equal(nameof(ShellViewModel.OpenFlipperMatrixSimulatorCommand), matrixBinding.Path.Path);

                var mirrorItem = toolsMenu.Items.OfType<MenuItem>().First(m => (m.Header as string)?.Contains("Live Screen Mirror") == true);
                var mirrorBinding = System.Windows.Data.BindingOperations.GetBinding(mirrorItem, MenuItem.CommandProperty);
                Assert.NotNull(mirrorBinding);
                Assert.Equal(nameof(ShellViewModel.OpenFlipperScreenMirrorCommand), mirrorBinding.Path.Path);

                var deployItem = toolsMenu.Items.OfType<MenuItem>().First(m => (m.Header as string)?.Contains("Push to Flipper Zero") == true);
                var deployBinding = System.Windows.Data.BindingOperations.GetBinding(deployItem, MenuItem.CommandProperty);
                Assert.NotNull(deployBinding);
                Assert.Equal(nameof(ShellViewModel.DeployFlipperUsbCommand), deployBinding.Path.Path);

                var slicerItem = toolsMenu.Items.OfType<MenuItem>().First(m => (m.Header as string)?.Contains("Sprite Sheet") == true);
                var slicerBinding = System.Windows.Data.BindingOperations.GetBinding(slicerItem, MenuItem.CommandProperty);
                Assert.NotNull(slicerBinding);
                Assert.Equal(nameof(ShellViewModel.OpenSpriteSheetSlicerCommand), slicerBinding.Path.Path);
            });
        }

        [Fact]
        public void AssetPackEditorPanel_InstantiatesWithoutXamlException_AndExcludesShareToCommunity()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var vm = new FlipperScheduleMatrixViewModel();
                var panel = new AssetPackEditorPanel
                {
                    DataContext = vm
                };

                Assert.NotNull(panel);

                panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

                var buttons = FindLogicalOrVisualChildren<Button>(panel).ToList();
                var buttonTooltipsAndContent = buttons.Select(b => $"{b.Content} | {b.ToolTip}").ToList();

                // R2 verification: Share to Community must NOT exist in the panel
                Assert.DoesNotContain(buttonTooltipsAndContent, s => s.Contains("Community", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(buttonTooltipsAndContent, s => s.Contains("Share", StringComparison.OrdinalIgnoreCase));

                // Verify standard export buttons are present
                Assert.Contains(buttonTooltipsAndContent, s => s.Contains("Export Manifest", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(buttonTooltipsAndContent, s => s.Contains("Export Pack Folder", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(buttonTooltipsAndContent, s => s.Contains("Export Pack (.zip)", StringComparison.OrdinalIgnoreCase));

                panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                panel.DataContext = null;
                vm.Dispose();
            });
        }

        [Fact]
        public void FlipperTools_AllRemainInstantiableWithoutCommunityHub()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                // 1. Matrix Simulator Window
                var sprite = E2ETestHelper.CreateTestSprite(2);
                var entry = new FlipperManifestEntry { Name = "TestAnim", Weight = 1 };
                var simWin = new FlipperMatrixSimulatorWindow([( "TestAnim", sprite, entry )], "TestPack");
                Assert.NotNull(simWin);
                simWin.Close();

                // 2. Media Slicer Window
                var slicerWin = new FlipperMediaSlicerWindow(initialSprite: sprite);
                Assert.NotNull(slicerWin);
                slicerWin.Close();

                // 3. Screen Mirror Window
                var mirrorWin = new FlipperScreenMirrorWindow();
                Assert.NotNull(mirrorWin);
                mirrorWin.Close();

                // 4. Deploy Window
                var mockDeployer = new Mock<IFlipperUsbDeployer>();
                var deployWin = new FlipperDeployWindow(mockDeployer.Object, [("test.png", new byte[10])], "TestPack");
                Assert.NotNull(deployWin);
                deployWin.Close();

                // 5. Sprite Sheet Slicer Studio Window
                var spriteSheetWin = new SpriteSheetSlicerWindow();
                Assert.NotNull(spriteSheetWin);
                spriteSheetWin.Close();

                // 6. 1-Bit Font Editor Panel
                var fontVm = new FontViewModel();
                var fontPanel = new FontEditorPanel { DataContext = fontVm };
                Assert.NotNull(fontPanel);
                fontPanel.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                fontPanel.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                fontVm.Dispose();

                // 7. Font Import Dialog
                var mockFontImport = new Mock<IFontImportService>();
                var fontImportDialog = new FontImportDialog(mockFontImport.Object);
                Assert.NotNull(fontImportDialog);
                fontImportDialog.Close();

                // 8. Pop-Out Code Viewer Window
                var shell = E2ETestHelper.CreateTestShellViewModel();
                shell.NewDocumentCommand.Execute("128x64");
                var mainVm = (MainViewModel)shell.ActiveDocument!;
                var codeWin = new CodeOutputWindow(mainVm);
                Assert.NotNull(codeWin);
                codeWin.Close();
            });
        }

        [Fact]
        public void CodeOutputWindow_LazySelectableText_OnlyPopulatesOnSelectableMode()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var shell = E2ETestHelper.CreateTestShellViewModel();
                shell.NewDocumentCommand.Execute("128x64");
                var mainVm = (MainViewModel)shell.ActiveDocument!;
                mainVm.GenerateCodeCommand.Execute(null);

                var codeWin = new CodeOutputWindow(mainVm);

                // Initially in highlighted mode, CodeSelectableBox.Text should be lazily deferred
                Assert.True(codeWin.RbModeSyntax.IsChecked == true);
                Assert.True(string.IsNullOrEmpty(codeWin.CodeSelectableBox.Text));

                // Switch to selectable mode
                codeWin.RbModeSelectable.IsChecked = true;
                Assert.Equal(mainVm.ExportedCode, codeWin.CodeSelectableBox.Text);

                codeWin.Close();
            });
        }

        private static System.Collections.Generic.IEnumerable<T> FindLogicalOrVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;

            foreach (var rawChild in LogicalTreeHelper.GetChildren(depObj))
            {
                if (rawChild is DependencyObject child)
                {
                    if (child is T t)
                    {
                        yield return t;
                    }
                    foreach (var grandchild in FindLogicalOrVisualChildren<T>(child))
                    {
                        yield return grandchild;
                    }
                }
            }
        }
    }
}
