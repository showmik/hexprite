using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hexprite.Services
{
    public record FlipperDeviceInfo(
        string PortName,
        string DisplayName,
        bool IsConnected
    );

    public record FlipperDeployProgress(
        double Percent,
        string CurrentFile,
        string StatusMessage
    );

    public interface IFlipperUsbDeployer
    {
        /// <summary>
        /// Scans connected USB Serial devices and identifies Flipper Zero hardware.
        /// </summary>
        Task<List<FlipperDeviceInfo>> ScanDevicesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Deploys an animation or asset pack file collection directly to the Flipper's SD card over USB Serial.
        /// </summary>
        Task<bool> DeployFilesAsync(
            string portName,
            string targetBaseDirectory,
            IReadOnlyList<(string RelativePath, byte[] Data)> files,
            IProgress<FlipperDeployProgress>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a reload or restart desktop notification to the Flipper device.
        /// </summary>
        Task<bool> RestartDesktopAsync(string portName, CancellationToken cancellationToken = default);
    }
}
