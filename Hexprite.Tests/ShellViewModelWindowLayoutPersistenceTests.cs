using System;
using System.IO;
using Hexprite.Controllers;
using Hexprite.Services;
using Hexprite.ViewModels;
using Moq;
using Xunit;

namespace Hexprite.Tests
{
    // All ShellViewModel constructions read/write the single real
    // %APPDATA%\Hexprite\window-layout.json file. Any test class that touches it (this one,
    // ShellViewModelTests, ShellViewModelChaosFuzzerTests) must share this collection so xUnit
    // never runs them concurrently — otherwise their file reads/writes race each other.
    [CollectionDefinition("WindowLayoutSettingsFile", DisableParallelization = true)]
    public class WindowLayoutSettingsFileCollection { }

    // Covers the load/save round-trip for the persisted panel-visibility ("window layout")
    // feature in ShellViewModel, including the crash-safe write and corrupted-file recovery
    // paths that had no coverage before.
    [Collection("WindowLayoutSettingsFile")]
    [Trait("Category", "Integration")]
    public class ShellViewModelWindowLayoutPersistenceTests : IDisposable
    {
        private static readonly string UserSettingsDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hexprite");
        private static readonly string WindowLayoutFile = Path.Combine(UserSettingsDirectory, "window-layout.json");
        private static readonly string WindowLayoutTempFile = WindowLayoutFile + ".tmp";

        private readonly string? _originalContent;
        private readonly bool _originalFileExisted;

        public ShellViewModelWindowLayoutPersistenceTests()
        {
            WpfTestHelper.EnsureApplication();

            // Back up whatever real layout the developer/CI machine already has so this test
            // can freely overwrite window-layout.json and restore it afterward.
            _originalFileExisted = File.Exists(WindowLayoutFile);
            _originalContent = _originalFileExisted ? File.ReadAllText(WindowLayoutFile) : null;
        }

        public void Dispose()
        {
            Directory.CreateDirectory(UserSettingsDirectory);
            if (File.Exists(WindowLayoutTempFile))
                File.Delete(WindowLayoutTempFile);

            if (_originalFileExisted)
                File.WriteAllText(WindowLayoutFile, _originalContent);
            else if (File.Exists(WindowLayoutFile))
                File.Delete(WindowLayoutFile);
        }

        private static ShellViewModel CreateShellViewModel()
        {
            var autosaveMock = new Mock<IAutosaveService>();
            var serviceProviderMock = new Mock<IServiceProvider>();
            serviceProviderMock.Setup(sp => sp.GetService(typeof(IAutosaveService))).Returns(autosaveMock.Object);

            return new ShellViewModel(
                new Mock<ICodeGeneratorService>().Object,
                new Mock<IDrawingService>().Object,
                new Mock<IClipboardService>().Object,
                new Mock<IPixelClipboardService>().Object,
                new Mock<IDialogService>().Object,
                new Mock<IThemeService>().Object,
                new Mock<IBugReportService>().Object,
                new Mock<IUserFeedbackService>().Object,
                new ControllerFactory(),
                new Mock<IExportService>().Object,
                new Mock<IFileImportExportService>().Object,
                new Mock<IHardwarePreviewService>().Object,
                serviceProviderMock.Object);
        }

        [Fact]
        public void ToggleAndReload_RoundTripsAllFiveVisibilityFlags()
        {
            if (File.Exists(WindowLayoutFile)) File.Delete(WindowLayoutFile);

            var shell = CreateShellViewModel();
            shell.IsToolSidebarVisible = false;
            shell.IsLayersPanelVisible = false;
            shell.IsRightSidebarVisible = false;
            shell.IsTimelineVisible = false;
            shell.IsStatusBarVisible = false;

            Assert.True(File.Exists(WindowLayoutFile));
            Assert.False(File.Exists(WindowLayoutTempFile));

            var reloaded = CreateShellViewModel();
            Assert.False(reloaded.IsToolSidebarVisible);
            Assert.False(reloaded.IsLayersPanelVisible);
            Assert.False(reloaded.IsRightSidebarVisible);
            Assert.False(reloaded.IsTimelineVisible);
            Assert.False(reloaded.IsStatusBarVisible);
        }

        [Fact]
        public void Load_WithNoSettingsFile_DefaultsAllPanelsVisible()
        {
            if (File.Exists(WindowLayoutFile)) File.Delete(WindowLayoutFile);

            var shell = CreateShellViewModel();

            Assert.True(shell.IsToolSidebarVisible);
            Assert.True(shell.IsLayersPanelVisible);
            Assert.True(shell.IsRightSidebarVisible);
            Assert.True(shell.IsTimelineVisible);
            Assert.True(shell.IsStatusBarVisible);
        }

        [Fact]
        public void Load_WithCorruptedSettingsFile_FallsBackToDefaultsWithoutThrowing()
        {
            Directory.CreateDirectory(UserSettingsDirectory);
            File.WriteAllText(WindowLayoutFile, "{ not valid json ");

            var shell = CreateShellViewModel();

            Assert.True(shell.IsToolSidebarVisible);
            Assert.True(shell.IsLayersPanelVisible);
            Assert.True(shell.IsRightSidebarVisible);
            Assert.True(shell.IsTimelineVisible);
            Assert.True(shell.IsStatusBarVisible);
        }

        [Fact]
        public void Load_WithPartialSettingsFile_DefaultsMissingPropertiesToTrue()
        {
            Directory.CreateDirectory(UserSettingsDirectory);
            File.WriteAllText(WindowLayoutFile, "{ \"IsLayersPanelVisible\": false }");

            var shell = CreateShellViewModel();

            Assert.False(shell.IsLayersPanelVisible);
            Assert.True(shell.IsToolSidebarVisible);
            Assert.True(shell.IsRightSidebarVisible);
            Assert.True(shell.IsTimelineVisible);
            Assert.True(shell.IsStatusBarVisible);
        }

        [Fact]
        public void Save_NeverLeavesTempFileBehind()
        {
            if (File.Exists(WindowLayoutFile)) File.Delete(WindowLayoutFile);

            var shell = CreateShellViewModel();
            shell.IsTimelineVisible = false;
            shell.IsTimelineVisible = true;

            Assert.False(File.Exists(WindowLayoutTempFile));
            Assert.True(File.Exists(WindowLayoutFile));
        }

        [Fact]
        public void ChangeAndReload_RoundTripsPanelWidths()
        {
            if (File.Exists(WindowLayoutFile)) File.Delete(WindowLayoutFile);

            var shell = CreateShellViewModel();
            shell.LayersPanelWidth = 275;
            shell.RightSidebarWidth = 410;

            var reloaded = CreateShellViewModel();
            Assert.Equal(275, reloaded.LayersPanelWidth);
            Assert.Equal(410, reloaded.RightSidebarWidth);
        }

        [Fact]
        public void Load_WithNoSettingsFile_DefaultsToStandardPanelWidths()
        {
            if (File.Exists(WindowLayoutFile)) File.Delete(WindowLayoutFile);

            var shell = CreateShellViewModel();

            Assert.Equal(232, shell.LayersPanelWidth);
            Assert.Equal(300, shell.RightSidebarWidth);
        }

        [Fact]
        public void Load_WithNegativeOrNonNumericWidths_FallsBackToDefaultsWithoutThrowing()
        {
            Directory.CreateDirectory(UserSettingsDirectory);
            File.WriteAllText(WindowLayoutFile,
                "{ \"LayersPanelWidth\": -50, \"RightSidebarWidth\": \"not-a-number\" }");

            var shell = CreateShellViewModel();

            // A negative or non-numeric width must never reach GridLength (it throws on
            // negative/NaN), so both fall back to the documented defaults.
            Assert.Equal(232, shell.LayersPanelWidth);
            Assert.Equal(300, shell.RightSidebarWidth);
        }

        [Fact]
        public void Load_WithInfinityWidths_FallsBackToDefaultsWithoutThrowing()
        {
            Directory.CreateDirectory(UserSettingsDirectory);
            File.WriteAllText(WindowLayoutFile,
                "{ \"LayersPanelWidth\": \"Infinity\", \"RightSidebarWidth\": \"-Infinity\" }");

            var shell = CreateShellViewModel();

            Assert.Equal(232, shell.LayersPanelWidth);
            Assert.Equal(300, shell.RightSidebarWidth);
        }

        [Fact]
        public void Load_WithNaNWidths_FallsBackToDefaultsWithoutThrowing()
        {
            Directory.CreateDirectory(UserSettingsDirectory);
            File.WriteAllText(WindowLayoutFile,
                "{ \"LayersPanelWidth\": \"NaN\", \"RightSidebarWidth\": \"NaN\" }");

            var shell = CreateShellViewModel();

            Assert.Equal(232, shell.LayersPanelWidth);
            Assert.Equal(300, shell.RightSidebarWidth);
        }

        [Fact]
        public void Load_WithOversizedWidths_ClampsToMaxBoundsWithoutThrowing()
        {
            Directory.CreateDirectory(UserSettingsDirectory);
            File.WriteAllText(WindowLayoutFile,
                "{ \"LayersPanelWidth\": 900, \"RightSidebarWidth\": 1200 }");

            var shell = CreateShellViewModel();

            Assert.Equal(450, shell.LayersPanelWidth);
            Assert.Equal(460, shell.RightSidebarWidth);
        }

        [Fact]
        public void Load_WithUndersizedWidths_ClampsToMinBoundsWithoutThrowing()
        {
            Directory.CreateDirectory(UserSettingsDirectory);
            File.WriteAllText(WindowLayoutFile,
                "{ \"LayersPanelWidth\": 50, \"RightSidebarWidth\": 100 }");

            var shell = CreateShellViewModel();

            Assert.Equal(150, shell.LayersPanelWidth);
            Assert.Equal(240, shell.RightSidebarWidth);
        }

        [Fact]
        public void SaveFailure_ReportsWarningOnActiveDocumentStatusBar()
        {
            if (Directory.Exists(WindowLayoutFile)) Directory.Delete(WindowLayoutFile);
            if (File.Exists(WindowLayoutFile)) File.Delete(WindowLayoutFile);

            var shell = CreateShellViewModel();
            shell.NewDocumentCommand.Execute("16x16");
            var activeMvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

            try
            {
                // Force SaveWindowLayoutSettings to fail deterministically: File.Move can't
                // replace a plain file with a directory sitting at the destination path.
                Directory.CreateDirectory(WindowLayoutFile);

                shell.IsTimelineVisible = false;

                Assert.Contains("Couldn't save window layout", activeMvm.StatusMessage);
            }
            finally
            {
                if (Directory.Exists(WindowLayoutFile))
                    Directory.Delete(WindowLayoutFile, recursive: true);
            }
        }
    }
}
