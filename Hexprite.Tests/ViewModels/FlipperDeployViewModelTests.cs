using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels.Flipper;
using Moq;
using Xunit;

namespace Hexprite.Tests.ViewModels
{
    [Trait("Category", "Unit")]
    public class FlipperDeployViewModelTests
    {
        private class MockDeployer : IFlipperUsbDeployer
        {
            public List<(string RelativePath, byte[] Data)> DeployedFiles { get; } = [];
            public string? DeployedTargetFolder { get; set; }
            public bool DesktopRestarted { get; set; }

            public Task<List<FlipperDeviceInfo>> ScanDevicesAsync(CancellationToken cancellationToken = default)
            {
                return Task.FromResult(new List<FlipperDeviceInfo>
                {
                    new("COM4", "Flipper Zero (COM4)", true)
                });
            }

            public Task<bool> DeployFilesAsync(
                string portName,
                string targetFolder,
                IReadOnlyList<(string RelativePath, byte[] Data)> files,
                IProgress<FlipperDeployProgress>? progress = null,
                CancellationToken cancellationToken = default)
            {
                DeployedTargetFolder = targetFolder;
                DeployedFiles.AddRange(files);
                progress?.Report(new FlipperDeployProgress(100.0, "manifest.txt", "Finished"));
                return Task.FromResult(true);
            }

            public Task<bool> RestartDesktopAsync(string portName, CancellationToken cancellationToken = default)
            {
                DesktopRestarted = true;
                return Task.FromResult(true);
            }
        }

        [Fact]
        public async Task InitialState_UpdatesSummaryAndScansDevices()
        {
            var deployer = new MockDeployer();
            var files = new List<(string RelativePath, byte[] Data)>
            {
                ("meta.txt", [1, 2, 3]),
                ("frame_0.bm", [4, 5])
            };

            using var vm = new FlipperDeployViewModel(deployer, files, "TestPack");
            await vm.ScanDevicesAsync();

            Assert.Equal("TestPack", vm.PackName);
            Assert.Equal("/ext/asset_packs/TestPack", vm.RemotePath);
            Assert.Contains("2 files", vm.FileSummaryText);
            Assert.True(vm.CanDeploy);
            Assert.NotNull(vm.SelectedDevice);
            Assert.Equal("COM4", vm.SelectedDevice.PortName);
        }

        [Fact]
        public void PresetButtons_UpdateRemotePathAndActiveStates()
        {
            var deployer = new MockDeployer();
            using var vm = new FlipperDeployViewModel(deployer, [], "MyPack");

            vm.SetMomentumPresetCommand.Execute(null);
            Assert.Equal("/ext/asset_packs/MyPack", vm.RemotePath);
            Assert.True(vm.IsMomentumPresetActive);
            Assert.False(vm.IsDolphinAnimsPresetActive);
            Assert.False(vm.IsStockPresetActive);

            vm.SetDolphinAnimsPresetCommand.Execute(null);
            Assert.Equal("/ext/dolphin/anims/MyPack", vm.RemotePath);
            Assert.True(vm.IsDolphinAnimsPresetActive);
            Assert.False(vm.IsMomentumPresetActive);

            vm.SetStockPresetCommand.Execute(null);
            Assert.Equal("/ext/dolphin", vm.RemotePath);
            Assert.True(vm.IsStockPresetActive);
            Assert.False(vm.IsMomentumPresetActive);
        }

        [Fact]
        public void FileItems_PopulatedWithFormattedSizesAndIcons()
        {
            var deployer = new MockDeployer();
            var files = new List<(string RelativePath, byte[] Data)>
            {
                ("Anims/MyAnim/meta.txt", [1, 2, 3]),
                ("Anims/MyAnim/frame_0.bm", new byte[1024]),
                ("Icons/pack_icon.png", new byte[2048])
            };

            using var vm = new FlipperDeployViewModel(deployer, files, "TestPack");

            Assert.Equal(3, vm.FileItems.Count);
            Assert.Equal("📄", vm.FileItems[0].FileIcon);
            Assert.Equal("3 B", vm.FileItems[0].FormattedSize);
            Assert.Equal("🖼", vm.FileItems[1].FileIcon);
            Assert.Equal("1.0 KB", vm.FileItems[1].FormattedSize);
            Assert.Equal("🎨", vm.FileItems[2].FileIcon);
            Assert.Equal("2.0 KB", vm.FileItems[2].FormattedSize);
        }

        [Fact]
        public async Task RescanDevices_PreservesSelectedPort()
        {
            var mock = new Mock<IFlipperUsbDeployer>();
            mock.Setup(d => d.ScanDevicesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([
                    new FlipperDeviceInfo("COM3", "Serial Device (COM3)", false),
                    new FlipperDeviceInfo("COM4", "Flipper Zero (COM4)", true)
                ]);

            using var vm = new FlipperDeployViewModel(mock.Object, [("test.txt", [1, 2])], "TestPack");
            await vm.ScanDevicesAsync();

            vm.SelectedDevice = vm.Devices[0]; // Select COM3 manually
            Assert.Equal("COM3", vm.SelectedDevice.PortName);

            // Re-scan
            await vm.ScanDevicesAsync();
            Assert.NotNull(vm.SelectedDevice);
            Assert.Equal("COM3", vm.SelectedDevice.PortName); // Retains COM3
        }

        [Fact]
        public async Task DeployCommand_ExecutesDeploymentAndRestartsDesktop()
        {
            var deployer = new MockDeployer();
            var files = new List<(string RelativePath, byte[] Data)>
            {
                ("meta.txt", [1, 2, 3])
            };

            bool closed = false;
            using var vm = new FlipperDeployViewModel(deployer, files, "TestPack")
            {
                RequestClose = () => closed = true
            };

            await vm.ScanDevicesAsync();
            await vm.Deploy();

            Assert.Single(deployer.DeployedFiles);
            Assert.Equal("/ext/asset_packs/TestPack", deployer.DeployedTargetFolder);
            Assert.True(deployer.DesktopRestarted);
            Assert.True(closed);
        }

        [Fact]
        public void CancelDeployCommand_InvokesRequestClose()
        {
            var deployer = new MockDeployer();
            bool closed = false;
            using var vm = new FlipperDeployViewModel(deployer, [], "TestPack")
            {
                RequestClose = () => closed = true
            };

            vm.CancelDeployCommand.Execute(null);

            Assert.True(closed);
        }

        [Fact]
        public async Task Deploy_WithoutSelectedDevice_ShowsMessage()
        {
            var deployer = new MockDeployer();
            var dialogMock = new Mock<IDialogService>();
            var files = new List<(string RelativePath, byte[] Data)> { ("test.txt", [1]) };

            using var vm = new FlipperDeployViewModel(deployer, files, "TestPack", dialogService: dialogMock.Object)
            {
                SelectedDevice = null
            };

            await vm.Deploy();

            dialogMock.Verify(d => d.ShowMessage(It.Is<string>(s => s.Contains("select a target Flipper device")), "Deployment Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning));
        }

        [Fact]
        public void EmptyFilesList_SetsCanDeployToFalse()
        {
            var deployer = new MockDeployer();
            using var vm = new FlipperDeployViewModel(deployer, [], "TestPack");

            Assert.False(vm.CanDeploy);
            Assert.Contains("No files selected", vm.FileSummaryText);
        }

        [Theory]
        [InlineData("File, size: 1024b\r\n>: ", 1024, true)]
        [InlineData("Storage stat: size 256\r\n>: ", 256, true)]
        [InlineData("File, size: 500b\r\n>: ", 1000, false)]
        [InlineData("Storage error: file not found\r\n>: ", 100, false)]
        [InlineData("", 100, false)]
        public void FlipperUsbDeployer_VerifyFileSize_EvaluatesCorrectly(string statResponse, int expectedSize, bool expectedResult)
        {
            bool result = FlipperUsbDeployer.VerifyFileSize(statResponse, expectedSize);
            Assert.Equal(expectedResult, result);
        }

        [Fact]
        public async Task Deploy_WithFilesContaining0x03Bytes_PassesCleanly()
        {
            var deployer = new MockDeployer();
            // Data with 0x03 ETX bytes (critical regression test)
            byte[] binaryData = [0x01, 0x00, 0x03, 0x03, 0xFF, 0x03, 0xAA];
            var files = new List<(string RelativePath, byte[] Data)>
            {
                ("frame_0.bm", binaryData)
            };

            using var vm = new FlipperDeployViewModel(deployer, files, "TestPack");
            await vm.ScanDevicesAsync();
            await vm.Deploy();

            Assert.Single(deployer.DeployedFiles);
            Assert.Equal(binaryData, deployer.DeployedFiles[0].Data);
        }

        [Fact]
        public async Task Deploy_WhenDeployerFails_ShowsDetailedStatusInErrorMessage()
        {
            var mockDeployer = new Mock<IFlipperUsbDeployer>();
            mockDeployer.Setup(d => d.ScanDevicesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([new FlipperDeviceInfo("COM4", "Flipper Zero (COM4)", true)]);
            mockDeployer.Setup(d => d.DeployFilesAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<(string RelativePath, byte[] Data)>>(),
                    It.IsAny<IProgress<FlipperDeployProgress>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, string, IReadOnlyList<(string, byte[])>, IProgress<FlipperDeployProgress>, CancellationToken>(
                    (port, target, files, prog, ct) =>
                    {
                        prog.Report(new FlipperDeployProgress(0.0, "", "SD card (/ext) not detected or not mounted on Flipper Zero."));
                    })
                .ReturnsAsync(false);

            var dialogMock = new Mock<IDialogService>();
            var files = new List<(string RelativePath, byte[] Data)> { ("frame_0.bm", [1, 2, 3]) };

            using var vm = new FlipperDeployViewModel(mockDeployer.Object, files, "TestPack", dialogService: dialogMock.Object);
            await vm.ScanDevicesAsync();
            await vm.Deploy();

            dialogMock.Verify(d => d.ShowMessage(
                It.Is<string>(msg => msg.Contains("SD card (/ext) not detected")),
                "Deployment Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error),
                Times.Once);
        }
    }
}
