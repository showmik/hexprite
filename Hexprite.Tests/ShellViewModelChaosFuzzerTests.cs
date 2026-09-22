using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Moq;
using System;
using System.Windows;
using System.Windows.Input;
using Xunit;

namespace Hexprite.Tests
{
    // Shares a collection with other tests that touch the real
    // %APPDATA%\Hexprite\window-layout.json file (see ShellViewModelWindowLayoutPersistenceTests)
    // so they never run concurrently and race each other's reads/writes of that file.
    [Collection("WindowLayoutSettingsFile")]
    [Trait("Category", "Fuzz")]
    public class ShellViewModelChaosFuzzerTests : IDisposable
    {
        private readonly string _testDir;
        private readonly string _testLayoutFile;

        public ShellViewModelChaosFuzzerTests()
        {
            WpfTestHelper.EnsureApplication();
            _testDir = Path.Combine(AppContext.BaseDirectory, "ChaosLayoutData_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_testDir);
            _testLayoutFile = Path.Combine(_testDir, "window-layout.json");
            ShellViewModel.SetCustomWindowLayoutPath(_testLayoutFile);
        }

        public void Dispose()
        {
            ShellViewModel.SetCustomWindowLayoutPath(null);
            try
            {
                if (Directory.Exists(_testDir))
                    Directory.Delete(_testDir, recursive: true);
            }
            catch { }
        }

        [Fact]
        public void ShellViewModel_ShouldNotCrashUnderChaoticInput()
        {
            var codeGenMock = new Mock<ICodeGeneratorService>();
            var drawingMock = new Mock<IDrawingService>();
            var clipboardMock = new Mock<IClipboardService>();
            var pixelClipboardMock = new Mock<IPixelClipboardService>();
            var dialogMock = new Mock<IDialogService>();
            
            // Mock DialogService to return false on close to prevent hangs during tests
            dialogMock.Setup(d => d.ShowConfirmation(It.IsAny<string>(), It.IsAny<string>()))
                      .Returns(false); // Discard

            var themeMock = new Mock<IThemeService>();
            var bugReportMock = new Mock<IBugReportService>();
            var feedbackMock = new Mock<IUserFeedbackService>();
            var controllerFactory = new ControllerFactory();
            var exportMock = new Mock<IExportService>();
            var importExportMock = new Mock<IFileImportExportService>();
            var hardwarePreviewMock = new Mock<IHardwarePreviewService>();
            var autosaveMock = new Mock<IAutosaveService>();
            var serviceProviderMock = new Mock<IServiceProvider>();
            
            // Need to return a MainViewModel/FontViewModel from DI to make NewDocument work
            serviceProviderMock.Setup(sp => sp.GetService(typeof(IAutosaveService))).Returns(autosaveMock.Object);

            var shell = new ShellViewModel(
                codeGenMock.Object,
                drawingMock.Object,
                clipboardMock.Object,
                pixelClipboardMock.Object,
                dialogMock.Object,
                themeMock.Object,
                bugReportMock.Object,
                feedbackMock.Object,
                controllerFactory,
                exportMock.Object,
                importExportMock.Object,
                hardwarePreviewMock.Object,
                serviceProviderMock.Object);

            var random = new Random(999);
            int iterations = FuzzTestHelper.GetIterationCount(defaultFastCount: 500, deepCount: 10000);

            for (int i = 0; i < iterations; i++)
            {
                int action = random.Next(18);
                try
                {
                    switch (action)
                    {
                        case 0:
                            SafeExecute(shell.NewDocumentCommand, "16x16");
                            break;
                        case 1:
                            SafeExecute(shell.NewDocumentCommand, "32x32");
                            break;
                        case 2:
                            // We don't see NewFontCommand in ShellViewModel snippet, let's just create more docs
                            SafeExecute(shell.NewDocumentCommand, "64x64");
                            break;
                        case 3:
                            if (shell.OpenDocuments.Count > 0)
                            {
                                int tabIdx = random.Next(0, shell.OpenDocuments.Count);
                                shell.ActiveDocument = shell.OpenDocuments[tabIdx];
                            }
                            break;
                        case 4:
                            if (shell.ActiveDocument != null)
                            {
                                SafeExecute(shell.CloseTabCommand, shell.ActiveDocument);
                            }
                            break;
                        case 5:
                            SafeExecute(shell.SelectToolCommand, "Pen");
                            break;
                        case 6:
                            SafeExecute(shell.SelectToolCommand, "Eraser");
                            break;
                        case 7:
                            SafeExecute(shell.SelectToolCommand, "Pan");
                            break;
                        case 8:
                            SafeExecute(shell.SwitchThemeCommand, "Dark");
                            break;
                        case 9:
                            SafeExecute(shell.SwitchThemeCommand, "Light");
                            break;
                        case 10:
                            shell.IsLayersPanelVisible = !shell.IsLayersPanelVisible;
                            break;
                        case 11:
                            shell.IsTimelineVisible = !shell.IsTimelineVisible;
                            break;
                        case 12:
                            shell.IsToolSidebarVisible = !shell.IsToolSidebarVisible;
                            break;
                        case 13:
                            shell.IsStatusBarVisible = !shell.IsStatusBarVisible;
                            break;
                        case 14:
                            shell.IsRightSidebarVisible = !shell.IsRightSidebarVisible;
                            break;
                        case 15:
                            // Try closing a random document (not necessarily the active one)
                            if (shell.OpenDocuments.Count > 0)
                            {
                                int tabIdx = random.Next(0, shell.OpenDocuments.Count);
                                SafeExecute(shell.CloseTabCommand, shell.OpenDocuments[tabIdx]);
                            }
                            break;
                        case 16:
                            // Fuzz panel widths with boundary values
                            shell.LayersPanelWidth = random.Next(-50, 800);
                            break;
                        case 17:
                            shell.RightSidebarWidth = random.Next(-50, 1200);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Crash during Shell chaos action {action} at iteration {i}: {ex.Message}", ex);
                }
            }

            Assert.True(true);
        }

        private static void SafeExecute(ICommand command, object? parameter = null)
        {
            if (command != null && command.CanExecute(parameter))
            {
                command.Execute(parameter);
            }
        }
    }
}
