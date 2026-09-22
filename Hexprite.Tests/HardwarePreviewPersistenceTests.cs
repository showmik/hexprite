using System;
using System.Collections.Generic;
using System.IO;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Moq;
using Xunit;

namespace Hexprite.Tests
{
    [CollectionDefinition("HardwarePreviewPreferences", DisableParallelization = true)]
    public class HardwarePreviewPreferencesCollection { }

    [Collection("HardwarePreviewPreferences")]
    [Trait("Category", "Unit")]
    public class HardwarePreviewPersistenceTests : IDisposable
    {
        private readonly string _testSettingsDir;
        private readonly string _prevAppData;

        public HardwarePreviewPersistenceTests()
        {
            WpfTestHelper.EnsureApplication();
            _testSettingsDir = Path.Combine(Path.GetTempPath(), "Hexprite_HwPrevTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testSettingsDir);
            _prevAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testSettingsDir))
                {
                    Directory.Delete(_testSettingsDir, true);
                }
            }
            catch
            {
                // Best effort cleanup
            }
        }

        [Fact]
        public void UserPreferences_HardwarePreviewAutoConnect_DefaultIsFalse()
        {
            var prefs = new UserPreferences();
            Assert.False(prefs.HardwarePreviewAutoConnect);
        }

        [Fact]
        public void UserPreferences_Clone_CopiesHardwarePreviewAutoConnect()
        {
            var prefs = new UserPreferences
            {
                HardwarePreviewAutoConnect = true,
                HardwarePreviewBoardPreset = "ESP32 DevKit",
                HardwarePreviewInterfaceType = "SPI",
                HardwarePreviewDisplayModel = "SH1106 (128x64)",
                HardwarePreviewSdaPin = "21",
                HardwarePreviewSclPin = "22",
                HardwarePreviewI2cAddress = "0x3D",
                HardwarePreviewUseSoftwareI2c = true,
                HardwarePreviewCsPin = "15",
                HardwarePreviewDcPin = "4",
                HardwarePreviewRstPin = "5",
                HardwarePreviewClkPin = "18",
                HardwarePreviewMosiPin = "23",
                HardwarePreviewPort = "COM7",
                HardwarePreviewBaudRate = 921600
            };

            // Call UserPreferencesService.Update to test roundtrip
            UserPreferencesService.Update(p =>
            {
                p.HardwarePreviewAutoConnect = prefs.HardwarePreviewAutoConnect;
                p.HardwarePreviewBoardPreset = prefs.HardwarePreviewBoardPreset;
                p.HardwarePreviewInterfaceType = prefs.HardwarePreviewInterfaceType;
                p.HardwarePreviewDisplayModel = prefs.HardwarePreviewDisplayModel;
                p.HardwarePreviewSdaPin = prefs.HardwarePreviewSdaPin;
                p.HardwarePreviewSclPin = prefs.HardwarePreviewSclPin;
                p.HardwarePreviewI2cAddress = prefs.HardwarePreviewI2cAddress;
                p.HardwarePreviewUseSoftwareI2c = prefs.HardwarePreviewUseSoftwareI2c;
                p.HardwarePreviewCsPin = prefs.HardwarePreviewCsPin;
                p.HardwarePreviewDcPin = prefs.HardwarePreviewDcPin;
                p.HardwarePreviewRstPin = prefs.HardwarePreviewRstPin;
                p.HardwarePreviewClkPin = prefs.HardwarePreviewClkPin;
                p.HardwarePreviewMosiPin = prefs.HardwarePreviewMosiPin;
                p.HardwarePreviewPort = prefs.HardwarePreviewPort;
                p.HardwarePreviewBaudRate = prefs.HardwarePreviewBaudRate;
            });

            var loaded = UserPreferencesService.Get();
            Assert.True(loaded.HardwarePreviewAutoConnect);
            Assert.Equal("ESP32 DevKit", loaded.HardwarePreviewBoardPreset);
            Assert.Equal("SPI", loaded.HardwarePreviewInterfaceType);
            Assert.Equal("SH1106 (128x64)", loaded.HardwarePreviewDisplayModel);
            Assert.Equal("21", loaded.HardwarePreviewSdaPin);
            Assert.Equal("22", loaded.HardwarePreviewSclPin);
            Assert.Equal("0x3D", loaded.HardwarePreviewI2cAddress);
            Assert.True(loaded.HardwarePreviewUseSoftwareI2c);
            Assert.Equal("15", loaded.HardwarePreviewCsPin);
            Assert.Equal("4", loaded.HardwarePreviewDcPin);
            Assert.Equal("5", loaded.HardwarePreviewRstPin);
            Assert.Equal("18", loaded.HardwarePreviewClkPin);
            Assert.Equal("23", loaded.HardwarePreviewMosiPin);
            Assert.Equal("COM7", loaded.HardwarePreviewPort);
            Assert.Equal(921600, loaded.HardwarePreviewBaudRate);
        }

        [Fact]
        public void HardwarePreviewWiringViewModel_ExecuteSaveAsDefault_PersistsToUserPreferences()
        {
            var initialConfig = new HardwarePreviewWiringConfig
            {
                BoardPreset = "Raspberry Pi Pico (RP2040)",
                InterfaceType = "I2C",
                DisplayModel = "SSD1306 (128x64)",
                SdaPin = "GP4",
                SclPin = "GP5",
                I2cAddress = "0x3C"
            };

            var vm = new HardwarePreviewWiringViewModel(initialConfig, 115200);
            vm.SelectedBoardPreset = "Arduino Uno / Nano";
            vm.SdaPin = "A4";
            vm.SclPin = "A5";

            vm.ExecuteSaveAsDefault();

            var prefs = UserPreferencesService.Get();
            Assert.Equal("Arduino Uno / Nano", prefs.HardwarePreviewBoardPreset);
            Assert.Equal("A4", prefs.HardwarePreviewSdaPin);
            Assert.Equal("A5", prefs.HardwarePreviewSclPin);
            Assert.Contains("saved as default", vm.StatusMessage);
        }

        [Fact]
        public void MainViewModel_AutoConnect_WhenConfiguredAndPortPresent_EnablesPreviewOnStartup()
        {
            // Configure auto-connect for COM9
            UserPreferencesService.Update(p =>
            {
                p.HardwarePreviewPort = "COM9";
                p.HardwarePreviewAutoConnect = true;
            });

            var hwPreviewMock = new Mock<IHardwarePreviewService>();
            hwPreviewMock.Setup(h => h.GetAvailablePorts()).Returns(["COM9", "COM1"]);
            hwPreviewMock.SetupProperty(h => h.PortName);
            hwPreviewMock.SetupProperty(h => h.IsEnabled);

            var vm = CreateTestMainViewModel(hwPreviewMock.Object);

            Assert.Equal("COM9", hwPreviewMock.Object.PortName);
            Assert.True(vm.IsHardwarePreviewEnabled);
        }

        [Fact]
        public void MainViewModel_AutoConnect_WhenConfiguredAndPortAbsent_DoesNotEnablePreview()
        {
            // Configure auto-connect for COM9, but COM9 is not connected
            UserPreferencesService.Update(p =>
            {
                p.HardwarePreviewPort = "COM9";
                p.HardwarePreviewAutoConnect = true;
            });

            var hwPreviewMock = new Mock<IHardwarePreviewService>();
            hwPreviewMock.Setup(h => h.GetAvailablePorts()).Returns(["COM1", "COM2"]);
            hwPreviewMock.SetupProperty(h => h.PortName);
            hwPreviewMock.SetupProperty(h => h.IsEnabled);

            var vm = CreateTestMainViewModel(hwPreviewMock.Object);

            Assert.False(vm.IsHardwarePreviewEnabled);
            Assert.NotEqual("COM9", hwPreviewMock.Object.PortName);
        }

        [Fact]
        public void MainViewModel_AutoConnect_WhenDisabled_DoesNotEnablePreviewEvenIfPortPresent()
        {
            // Auto-connect disabled
            UserPreferencesService.Update(p =>
            {
                p.HardwarePreviewPort = "COM9";
                p.HardwarePreviewAutoConnect = false;
            });

            var hwPreviewMock = new Mock<IHardwarePreviewService>();
            hwPreviewMock.Setup(h => h.GetAvailablePorts()).Returns(["COM9", "COM1"]);
            hwPreviewMock.SetupProperty(h => h.PortName);
            hwPreviewMock.SetupProperty(h => h.IsEnabled);

            var vm = CreateTestMainViewModel(hwPreviewMock.Object);

            Assert.Equal("COM9", hwPreviewMock.Object.PortName);
            Assert.False(vm.IsHardwarePreviewEnabled);
        }

        [Fact]
        public void MainViewModel_HardwarePreviewAutoConnect_Property_UpdatesUserPreferences()
        {
            var hwPreviewMock = new Mock<IHardwarePreviewService>();
            hwPreviewMock.Setup(h => h.GetAvailablePorts()).Returns([]);
            var vm = CreateTestMainViewModel(hwPreviewMock.Object);

            vm.HardwarePreviewAutoConnect = true;
            Assert.True(UserPreferencesService.Get().HardwarePreviewAutoConnect);

            vm.HardwarePreviewAutoConnect = false;
            Assert.False(UserPreferencesService.Get().HardwarePreviewAutoConnect);
        }

        [Fact]
        public async System.Threading.Tasks.Task MainViewModel_HotplugAutoConnect_WhenPortAppears_AutoConnects()
        {
            UserPreferencesService.Update(p =>
            {
                p.HardwarePreviewPort = "COM5";
                p.HardwarePreviewAutoConnect = true;
            });

            var availableOptions = new List<HardwarePreviewPortOption>();
            var hwPreviewMock = new Mock<IHardwarePreviewService>();
            hwPreviewMock.Setup(h => h.GetAvailablePorts()).Returns([]);
            hwPreviewMock.Setup(h => h.GetAvailablePortOptions()).Returns(() => availableOptions);
            hwPreviewMock.SetupProperty(h => h.PortName);
            hwPreviewMock.SetupProperty(h => h.IsEnabled);

            var vm = CreateTestMainViewModel(hwPreviewMock.Object);
            // Complete any initial background refresh with empty ports
            await vm.RefreshPortsCommand.ExecuteAsync(null);
            Assert.False(vm.IsHardwarePreviewEnabled);
            Assert.Empty(vm.AvailablePorts);

            // Simulate plugging in COM5
            availableOptions.Add(new HardwarePreviewPortOption("COM5", "COM5 (USB Serial)"));

            await vm.RefreshPortsCommand.ExecuteAsync(null);

            Assert.Equal("COM5", vm.HardwarePreviewPort);
            Assert.True(vm.IsHardwarePreviewEnabled);
        }

        [Fact]
        public async System.Threading.Tasks.Task AutoDetectBaudRate_WhenBoardRespondsAt115200_Selects115200()
        {
            var hwPreviewMock = new Mock<IHardwarePreviewService>();
            hwPreviewMock.Setup(h => h.GetAvailablePorts()).Returns(["COM5"]);
            hwPreviewMock.SetupProperty(h => h.PortName);
            hwPreviewMock.SetupProperty(h => h.BaudRate);
            hwPreviewMock.SetupProperty(h => h.IsEnabled);
            hwPreviewMock.Setup(h => h.ConnectionState).Returns(() =>
            {
                // Simulate board only responding when baud rate is 115200
                return hwPreviewMock.Object.BaudRate == 115200
                    ? HardwarePreviewConnectionState.Connected
                    : HardwarePreviewConnectionState.Streaming;
            });

            var vm = CreateTestMainViewModel(hwPreviewMock.Object);
            vm.HardwarePreviewPort = "COM5";
            vm.HardwarePreviewBaudRate = 9600; // start at wrong baud rate

            await vm.AutoDetectBaudRateCommand.ExecuteAsync(null);

            Assert.Equal(115200, vm.HardwarePreviewBaudRate);
            Assert.Equal(115200, hwPreviewMock.Object.BaudRate);
            Assert.Contains("115200", vm.StatusMessage);
        }

        [Fact]
        public async System.Threading.Tasks.Task AutoRefreshPortsAsync_WhenPortsUnchanged_DoesNotQueryGetAvailablePortOptions()
        {
            var hwPreviewMock = new Mock<IHardwarePreviewService>();
            hwPreviewMock.Setup(h => h.GetAvailablePorts()).Returns(["COM3"]);
            hwPreviewMock.Setup(h => h.GetAvailablePortOptions()).Returns([new HardwarePreviewPortOption("COM3", "COM3 (Device)")]);
            hwPreviewMock.SetupProperty(h => h.PortName);
            hwPreviewMock.SetupProperty(h => h.IsEnabled);

            var vm = CreateTestMainViewModel(hwPreviewMock.Object);
            // Run initial manual refresh to populate AvailablePorts
            await vm.RefreshPortsCommand.ExecuteAsync(null);

            // Reset invocation tracking
            hwPreviewMock.Invocations.Clear();

            // Auto-refresh should detect ports are unchanged and NOT query GetAvailablePortOptions
            await vm.AutoRefreshPortsAsync();

            hwPreviewMock.Verify(h => h.GetAvailablePortOptions(), Times.Never);
            Assert.True(vm.RefreshPortsCommand.CanExecute(null));
        }

        [Fact]
        public async System.Threading.Tasks.Task AutoRefreshPortsAsync_WhenPortsChanged_QueriesGetAvailablePortOptionsAndUpdatesAvailablePorts()
        {
            var availablePorts = new List<string> { "COM3" };
            var availableOptions = new List<HardwarePreviewPortOption>
            {
                new HardwarePreviewPortOption("COM3", "COM3 (Device)")
            };

            var hwPreviewMock = new Mock<IHardwarePreviewService>();
            hwPreviewMock.Setup(h => h.GetAvailablePorts()).Returns(() => availablePorts);
            hwPreviewMock.Setup(h => h.GetAvailablePortOptions()).Returns(() => availableOptions);
            hwPreviewMock.SetupProperty(h => h.PortName);
            hwPreviewMock.SetupProperty(h => h.IsEnabled);

            var vm = CreateTestMainViewModel(hwPreviewMock.Object);
            await vm.RefreshPortsCommand.ExecuteAsync(null);
            Assert.Single(vm.AvailablePorts);

            // Simulate plugging in a new device
            availablePorts.Add("COM7");
            availableOptions.Add(new HardwarePreviewPortOption("COM7", "ESP32 (COM7)"));

            // Auto-refresh should detect port list changed and query GetAvailablePortOptions
            await vm.AutoRefreshPortsAsync();

            Assert.Equal(2, vm.AvailablePorts.Count);
            Assert.Contains(vm.AvailablePorts, p => p.PortName == "COM7");
            Assert.True(vm.RefreshPortsCommand.CanExecute(null));
        }

        private static MainViewModel CreateTestMainViewModel(IHardwarePreviewService hwPreview)
        {
            var codeGenMock = new Mock<ICodeGeneratorService>();
            var drawingMock = new Mock<IDrawingService>();
            var clipboardMock = new Mock<IClipboardService>();
            var pixelClipboardMock = new Mock<IPixelClipboardService>();
            var dialogMock = new Mock<IDialogService>();
            var themeMock = new Mock<IThemeService>();
            var bugReportMock = new Mock<IBugReportService>();
            var feedbackMock = new Mock<IUserFeedbackService>();
            var controllerFactory = new ControllerFactory();
            var exportMock = new Mock<IExportService>();
            var importExportMock = new Mock<IFileImportExportService>();
            var autosaveMock = new Mock<IAutosaveService>();
            var serviceProviderMock = new Mock<IServiceProvider>();
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
                hwPreview,
                serviceProviderMock.Object);

            shell.NewDocumentCommand.Execute("16x16");
            return (MainViewModel)shell.ActiveDocument!;
        }
    }
}
