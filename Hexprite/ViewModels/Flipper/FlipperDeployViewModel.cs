using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hexprite.Core;
using Hexprite.Services;

namespace Hexprite.ViewModels.Flipper
{
    public record DeployFileItem(
        string RelativePath,
        int ByteCount,
        string FormattedSize,
        string FileIcon
    );

    public partial class FlipperDeployViewModel : ObservableObject, IDisposable
    {
        private readonly IFlipperUsbDeployer _deployer;
        private readonly IReadOnlyList<(string RelativePath, byte[] Data)> _files;
        private readonly IDialogService? _dialogService;
        private CancellationTokenSource? _cts;
        private bool _disposed;

        public ObservableCollection<FlipperDeviceInfo> Devices { get; } = [];
        public ObservableCollection<DeployFileItem> FileItems { get; } = [];

        [ObservableProperty]
        private string _packName = "MyAssetPack";

        [ObservableProperty]
        private string _remotePath = "/ext/asset_packs/MyAssetPack";

        [ObservableProperty]
        private string _fileSummaryText = "Ready to deploy files...";

        [ObservableProperty]
        private string _fileListText = string.Empty;

        [ObservableProperty]
        private FlipperDeviceInfo? _selectedDevice;

        [ObservableProperty]
        private string _statusMessage = "Plug in Flipper Zero via USB and click Push.";

        [ObservableProperty]
        private double _uploadProgressPercent;

        [ObservableProperty]
        private bool _isScanning;

        [ObservableProperty]
        private bool _isDeploying;

        [ObservableProperty]
        private bool _canDeploy;

        [ObservableProperty]
        private bool _restartDesktop = true;

        public bool IsMomentumPresetActive => RemotePath.Equals($"/ext/asset_packs/{PackName}", StringComparison.OrdinalIgnoreCase);
        public bool IsDolphinAnimsPresetActive => RemotePath.Equals($"/ext/dolphin/anims/{PackName}", StringComparison.OrdinalIgnoreCase);
        public bool IsStockPresetActive => RemotePath.Equals("/ext/dolphin", StringComparison.OrdinalIgnoreCase);

        public IReadOnlyList<(string RelativePath, byte[] Data)> Files => _files;

        public Action? RequestClose { get; set; }

        public FlipperDeployViewModel(
            IFlipperUsbDeployer? deployer = null,
            IReadOnlyList<(string RelativePath, byte[] Data)>? files = null,
            string packName = "MyAssetPack",
            IDialogService? dialogService = null)
        {
            _deployer = deployer ?? new FlipperUsbDeployer();
            _files = files ?? [];
            _packName = string.IsNullOrWhiteSpace(packName) ? "MyAssetPack" : packName.Trim();
            _dialogService = dialogService;

            RemotePath = $"/ext/asset_packs/{_packName}";
            UpdateFileSummary();
            _ = ScanDevicesAsync();
        }

        public void UpdateFileSummary()
        {
            FileItems.Clear();
            if (_files == null || _files.Count == 0)
            {
                FileSummaryText = "No files selected for deployment.";
                FileListText = string.Empty;
                CanDeploy = false;
                return;
            }

            int totalBytes = _files.Sum(f => f.Data.Length);
            int estimatedChunks = _files.Sum(f => f.Data.Length == 0 ? 1 : (f.Data.Length + 511) / 512);
            string formattedTotal = totalBytes switch
            {
                >= 1024 * 1024 => $"{totalBytes / (1024.0 * 1024.0):F1} MB",
                >= 1024 => $"{totalBytes / 1024.0:F1} KB",
                _ => $"{totalBytes} B",
            };
            FileSummaryText = $"Selected: {_files.Count} files ({formattedTotal}, ~{estimatedChunks} chunks)";
            FileListText = string.Join(", ", _files.Select(f => f.RelativePath));

            foreach (var (relPath, data) in _files)
            {
                string ext = System.IO.Path.GetExtension(relPath).ToLowerInvariant();
                string icon = ext switch
                {
                    ".bm" => "🖼",
                    ".txt" => "📄",
                    ".png" or ".bmp" => "🎨",
                    _ => "📦",
                };

                string sizeStr = data.Length switch
                {
                    >= 1024 * 1024 => $"{data.Length / (1024.0 * 1024.0):F1} MB",
                    >= 1024 => $"{data.Length / 1024.0:F1} KB",
                    _ => $"{data.Length} B",
                };

                FileItems.Add(new DeployFileItem(relPath, data.Length, sizeStr, icon));
            }

            CanDeploy = SelectedDevice != null && !IsDeploying;
        }

        partial void OnRemotePathChanged(string value)
        {
            OnPropertyChanged(nameof(IsMomentumPresetActive));
            OnPropertyChanged(nameof(IsDolphinAnimsPresetActive));
            OnPropertyChanged(nameof(IsStockPresetActive));
        }

        partial void OnPackNameChanged(string value)
        {
            OnPropertyChanged(nameof(IsMomentumPresetActive));
            OnPropertyChanged(nameof(IsDolphinAnimsPresetActive));
        }

        public async Task ScanDevicesAsync()
        {
            IsScanning = true;
            StatusMessage = "Scanning for Flipper Zero on USB ports...";
            string? prevPort = SelectedDevice?.PortName;

            try
            {
                var discovered = await _deployer.ScanDevicesAsync(CancellationToken.None);
                Devices.Clear();
                foreach (var dev in discovered)
                {
                    Devices.Add(dev);
                }

                if (Devices.Count > 0)
                {
                    var matching = !string.IsNullOrEmpty(prevPort)
                        ? Devices.FirstOrDefault(d => d.PortName.Equals(prevPort, StringComparison.OrdinalIgnoreCase))
                        : null;

                    var flipper = matching ?? Devices.FirstOrDefault(d => d.IsConnected) ?? Devices[0];
                    SelectedDevice = flipper;
                    StatusMessage = $"Found: {flipper.DisplayName}";
                }
                else
                {
                    StatusMessage = "No COM ports found. Connect Flipper Zero via USB.";
                    SelectedDevice = null;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Scan failed: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
                CanDeploy = SelectedDevice != null && _files != null && _files.Count > 0 && !IsDeploying;
            }
        }

        partial void OnSelectedDeviceChanged(FlipperDeviceInfo? value)
        {
            CanDeploy = value != null && _files != null && _files.Count > 0 && !IsDeploying;
        }

        [RelayCommand]
        public async Task RefreshDevices()
        {
            await ScanDevicesAsync();
        }

        [RelayCommand]
        public async Task Deploy()
        {
            if (SelectedDevice == null || string.IsNullOrWhiteSpace(SelectedDevice.PortName))
            {
                _dialogService?.ShowMessage("Please select a target Flipper device COM port.", "Deployment Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string targetPath = RemotePath.Trim();
            if (string.IsNullOrEmpty(targetPath))
            {
                _dialogService?.ShowMessage("Remote path cannot be empty.", "Deployment Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsDeploying = true;
            CanDeploy = false;
            UploadProgressPercent = 0;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            var progress = new Progress<FlipperDeployProgress>(p =>
            {
                UploadProgressPercent = p.Percent;
                StatusMessage = p.StatusMessage;
            });

            try
            {
                bool success = await _deployer.DeployFilesAsync(SelectedDevice.PortName, targetPath, _files, progress, _cts.Token);
                if (success)
                {
                    if (RestartDesktop)
                    {
                        StatusMessage = "Reloading desktop on Flipper...";
                        await _deployer.RestartDesktopAsync(SelectedDevice.PortName, _cts.Token);
                    }

                    _dialogService?.ShowMessage($"Successfully deployed {_files.Count} files to:\n{targetPath}", "Deployment Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    RequestClose?.Invoke();
                }
                else
                {
                    _dialogService?.ShowMessage($"Deployment failed:\n{StatusMessage}\n\nVerify Flipper Zero is unlocked, SD card is inserted, and device is not running another application.", "Deployment Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Deployment cancelled.";
            }
            catch (Exception ex)
            {
                _dialogService?.ShowMessage($"Deployment error:\n{ex.Message}", "Deployment Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsDeploying = false;
                CanDeploy = SelectedDevice != null && _files != null && _files.Count > 0;
            }
        }

        [RelayCommand]
        public void CancelDeploy()
        {
            _cts?.Cancel();
            RequestClose?.Invoke();
        }

        [RelayCommand]
        public void SetMomentumPreset()
        {
            RemotePath = $"/ext/asset_packs/{PackName}";
        }

        [RelayCommand]
        public void SetDolphinAnimsPreset()
        {
            RemotePath = $"/ext/dolphin/anims/{PackName}";
        }

        [RelayCommand]
        public void SetStockPreset()
        {
            RemotePath = "/ext/dolphin";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            RequestClose = null;
            Devices.Clear();
            GC.SuppressFinalize(this);
        }
    }
}
