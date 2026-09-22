using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hexprite.Core;
using Hexprite.Services;

namespace Hexprite.ViewModels
{
    public partial class HardwarePreviewWiringViewModel : ObservableObject
    {
        private readonly HardwarePreviewWiringConfig _config;
        private readonly int _canvasWidth;
        private readonly int _canvasHeight;
        private readonly IClipboardService? _clipboard;
        private readonly IDialogService? _dialogService;
        private readonly IHardwarePreviewService? _hardwarePreview;

        [ObservableProperty]
        private string _selectedBoardPreset = HardwarePreviewWiringConfig.DefaultBoard;

        [ObservableProperty]
        private string _interfaceType = HardwarePreviewWiringConfig.DefaultInterface;

        [ObservableProperty]
        private string _selectedDisplayModel = HardwarePreviewWiringConfig.DefaultDisplayModel;

        [ObservableProperty]
        private string _sdaPin = HardwarePreviewWiringConfig.DefaultSdaPin;

        [ObservableProperty]
        private string _sclPin = HardwarePreviewWiringConfig.DefaultSclPin;

        [ObservableProperty]
        private string _i2cAddress = HardwarePreviewWiringConfig.DefaultI2cAddress;

        [ObservableProperty]
        private bool _useSoftwareI2c;

        [ObservableProperty]
        private string _csPin = "5";

        [ObservableProperty]
        private string _dcPin = "16";

        [ObservableProperty]
        private string _rstPin = "17";

        [ObservableProperty]
        private string _clkPin = "18";

        [ObservableProperty]
        private string _mosiPin = "23";

        [ObservableProperty]
        private int _selectedBaudRate = 115200;

        [ObservableProperty]
        private bool _isAutoDetectingBaudRate;

        [ObservableProperty]
        private string? _validationWarning;

        [ObservableProperty]
        private string? _validationError;

        [ObservableProperty]
        private string? _canvasBufferWarning;

        public bool HasValidationWarning => !string.IsNullOrEmpty(ValidationWarning);
        public bool HasValidationError => !string.IsNullOrEmpty(ValidationError);
        public bool HasCanvasBufferWarning => !string.IsNullOrEmpty(CanvasBufferWarning);

        [ObservableProperty]
        private string? _statusMessage;

        [ObservableProperty]
        private string _generatedSketch = string.Empty;

        [ObservableProperty]
        private string _generatedSnippet = string.Empty;

        [ObservableProperty]
        private string _generatedPlatformIOConfig = string.Empty;

        [ObservableProperty]
        private string _generatedPlatformIOIni = string.Empty;

        public ObservableCollection<string> BoardPresets { get; } = new(HardwarePreviewWiringConfig.GetBoardPresets());
        public ObservableCollection<string> DisplayModels { get; } = new(HardwarePreviewWiringConfig.GetDisplayModels());
        public ObservableCollection<int> AvailableBaudRates { get; } = [9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600];
        public ObservableCollection<WirePinMapping> WireMappings { get; } = [];

        public bool IsI2c => InterfaceType == "I2C";
        public bool IsSpi => InterfaceType == "SPI";

        public IRelayCommand CopySketchCommand { get; }
        public IRelayCommand CopySnippetCommand { get; }
        public IRelayCommand ExportSketchCommand { get; }
        public IRelayCommand SaveAsDefaultCommand { get; }
        public IRelayCommand CopyPlatformIOConfigCommand { get; }
        public IRelayCommand CopyPlatformIOIniCommand { get; }
        public IRelayCommand OpenPlatformIOFolderCommand { get; }
        public IRelayCommand OpenInArduinoIdeCommand { get; }
        public IAsyncRelayCommand AutoDetectBaudRateCommand { get; }

        public HardwarePreviewWiringViewModel(
            HardwarePreviewWiringConfig config,
            int baudRate = 115200,
            int canvasWidth = 0,
            int canvasHeight = 0,
            IClipboardService? clipboard = null,
            IDialogService? dialogService = null,
            IHardwarePreviewService? hardwarePreview = null)
        {
            _config = config.Clone();
            _selectedBaudRate = baudRate > 0 ? baudRate : (_config.BaudRate > 0 ? _config.BaudRate : 115200);
            _config.BaudRate = _selectedBaudRate;
            _canvasWidth = canvasWidth;
            _canvasHeight = canvasHeight;
            _clipboard = clipboard;
            _dialogService = dialogService;
            _hardwarePreview = hardwarePreview;

            _selectedBoardPreset = _config.BoardPreset;
            _interfaceType = _config.InterfaceType;
            _selectedDisplayModel = _config.DisplayModel;
            _sdaPin = _config.SdaPin;
            _sclPin = _config.SclPin;
            _i2cAddress = _config.I2cAddress;
            _useSoftwareI2c = _config.UseSoftwareI2c;
            _csPin = _config.CsPin;
            _dcPin = _config.DcPin;
            _rstPin = _config.RstPin;
            _clkPin = _config.ClkPin;
            _mosiPin = _config.MosiPin;

            CopySketchCommand = new RelayCommand(ExecuteCopySketch);
            CopySnippetCommand = new RelayCommand(ExecuteCopySnippet);
            ExportSketchCommand = new RelayCommand(ExecuteExportSketch);
            SaveAsDefaultCommand = new RelayCommand(ExecuteSaveAsDefault);
            CopyPlatformIOConfigCommand = new RelayCommand(ExecuteCopyPlatformIOConfig);
            CopyPlatformIOIniCommand = new RelayCommand(ExecuteCopyPlatformIOIni);
            OpenPlatformIOFolderCommand = new RelayCommand(ExecuteOpenPlatformIOFolder);
            OpenInArduinoIdeCommand = new RelayCommand(ExecuteOpenInArduinoIde);
            AutoDetectBaudRateCommand = new AsyncRelayCommand(ExecuteAutoDetectBaudRateAsync);

            RefreshMappingsAndCode();
        }

        partial void OnSelectedBaudRateChanged(int value)
        {
            _config.BaudRate = value;
            RefreshMappingsAndCode();
        }

        partial void OnSelectedBoardPresetChanged(string value)
        {
            if (value != "Custom Board")
            {
                _config.ApplyPreset(value);
                _interfaceType = _config.InterfaceType;
                _sdaPin = _config.SdaPin;
                _sclPin = _config.SclPin;
                _i2cAddress = _config.I2cAddress;
                _useSoftwareI2c = _config.UseSoftwareI2c;
                _csPin = _config.CsPin;
                _dcPin = _config.DcPin;
                _rstPin = _config.RstPin;
                _clkPin = _config.ClkPin;
                _mosiPin = _config.MosiPin;

                OnPropertyChanged(nameof(InterfaceType));
                OnPropertyChanged(nameof(IsI2c));
                OnPropertyChanged(nameof(IsSpi));
                OnPropertyChanged(nameof(SdaPin));
                OnPropertyChanged(nameof(SclPin));
                OnPropertyChanged(nameof(I2cAddress));
                OnPropertyChanged(nameof(UseSoftwareI2c));
                OnPropertyChanged(nameof(CsPin));
                OnPropertyChanged(nameof(DcPin));
                OnPropertyChanged(nameof(RstPin));
                OnPropertyChanged(nameof(ClkPin));
                OnPropertyChanged(nameof(MosiPin));
            }
            RefreshMappingsAndCode();
        }

        partial void OnInterfaceTypeChanged(string value)
        {
            _config.InterfaceType = value;

            // Re-apply the current board preset so I2C↔SPI pins update to match the board.
            // "Custom Board" preserves whatever the user has manually entered.
            if (SelectedBoardPreset != "Custom Board")
            {
                _config.ApplyPreset(SelectedBoardPreset);
                // ApplyPreset resets InterfaceType to the preset's default, so restore the user's choice.
                _config.InterfaceType = value;
                _sdaPin = _config.SdaPin;
                _sclPin = _config.SclPin;
                _i2cAddress = _config.I2cAddress;
                _useSoftwareI2c = _config.UseSoftwareI2c;
                _csPin = _config.CsPin;
                _dcPin = _config.DcPin;
                _rstPin = _config.RstPin;
                _clkPin = _config.ClkPin;
                _mosiPin = _config.MosiPin;

                OnPropertyChanged(nameof(SdaPin));
                OnPropertyChanged(nameof(SclPin));
                OnPropertyChanged(nameof(I2cAddress));
                OnPropertyChanged(nameof(UseSoftwareI2c));
                OnPropertyChanged(nameof(CsPin));
                OnPropertyChanged(nameof(DcPin));
                OnPropertyChanged(nameof(RstPin));
                OnPropertyChanged(nameof(ClkPin));
                OnPropertyChanged(nameof(MosiPin));
            }

            OnPropertyChanged(nameof(IsI2c));
            OnPropertyChanged(nameof(IsSpi));
            RefreshMappingsAndCode();
        }

        partial void OnSelectedDisplayModelChanged(string value)
        {
            _config.DisplayModel = value;
            RefreshMappingsAndCode();
        }

        private void CheckAndSwitchToCustomPreset(string pinName, string newValue)
        {
            if (SelectedBoardPreset != "Custom Board")
            {
                var temp = new HardwarePreviewWiringConfig();
                temp.ApplyPreset(SelectedBoardPreset);
                string expected = pinName switch
                {
                    nameof(SdaPin) => temp.SdaPin,
                    nameof(SclPin) => temp.SclPin,
                    nameof(CsPin) => temp.CsPin,
                    nameof(DcPin) => temp.DcPin,
                    nameof(RstPin) => temp.RstPin,
                    nameof(ClkPin) => temp.ClkPin,
                    nameof(MosiPin) => temp.MosiPin,
                    _ => string.Empty
                };

                if (!string.Equals(newValue.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    SelectedBoardPreset = "Custom Board";
                }
            }
        }

        partial void OnSdaPinChanged(string value)
        {
            string norm = HardwarePreviewWiringConfig.NormalizePin(value);
            _config.SdaPin = norm;
            CheckAndSwitchToCustomPreset(nameof(SdaPin), norm);
            RefreshMappingsAndCode();
        }

        partial void OnSclPinChanged(string value)
        {
            string norm = HardwarePreviewWiringConfig.NormalizePin(value);
            _config.SclPin = norm;
            CheckAndSwitchToCustomPreset(nameof(SclPin), norm);
            RefreshMappingsAndCode();
        }

        partial void OnI2cAddressChanged(string value)
        {
            _config.I2cAddress = value;
            RefreshMappingsAndCode();
        }

        partial void OnUseSoftwareI2cChanged(bool value)
        {
            _config.UseSoftwareI2c = value;
            RefreshMappingsAndCode();
        }

        partial void OnCsPinChanged(string value)
        {
            string norm = HardwarePreviewWiringConfig.NormalizePin(value);
            _config.CsPin = norm;
            CheckAndSwitchToCustomPreset(nameof(CsPin), norm);
            RefreshMappingsAndCode();
        }

        partial void OnDcPinChanged(string value)
        {
            string norm = HardwarePreviewWiringConfig.NormalizePin(value);
            _config.DcPin = norm;
            CheckAndSwitchToCustomPreset(nameof(DcPin), norm);
            RefreshMappingsAndCode();
        }

        partial void OnRstPinChanged(string value)
        {
            string norm = HardwarePreviewWiringConfig.NormalizePin(value);
            _config.RstPin = norm;
            CheckAndSwitchToCustomPreset(nameof(RstPin), norm);
            RefreshMappingsAndCode();
        }

        partial void OnClkPinChanged(string value)
        {
            string norm = HardwarePreviewWiringConfig.NormalizePin(value);
            _config.ClkPin = norm;
            CheckAndSwitchToCustomPreset(nameof(ClkPin), norm);
            RefreshMappingsAndCode();
        }

        partial void OnMosiPinChanged(string value)
        {
            string norm = HardwarePreviewWiringConfig.NormalizePin(value);
            _config.MosiPin = norm;
            CheckAndSwitchToCustomPreset(nameof(MosiPin), norm);
            RefreshMappingsAndCode();
        }

        public HardwarePreviewWiringConfig GetConfig()
        {
            _config.BoardPreset = SelectedBoardPreset;
            _config.InterfaceType = InterfaceType;
            _config.DisplayModel = SelectedDisplayModel;
            _config.SdaPin = HardwarePreviewWiringConfig.NormalizePin(SdaPin);
            _config.SclPin = HardwarePreviewWiringConfig.NormalizePin(SclPin);
            _config.I2cAddress = HardwarePreviewWiringConfig.NormalizeI2cAddress(I2cAddress);
            _config.UseSoftwareI2c = UseSoftwareI2c;
            _config.CsPin = HardwarePreviewWiringConfig.NormalizePin(CsPin);
            _config.DcPin = HardwarePreviewWiringConfig.NormalizePin(DcPin);
            _config.RstPin = HardwarePreviewWiringConfig.NormalizePin(RstPin);
            _config.ClkPin = HardwarePreviewWiringConfig.NormalizePin(ClkPin);
            _config.MosiPin = HardwarePreviewWiringConfig.NormalizePin(MosiPin);
            _config.BaudRate = SelectedBaudRate;
            return _config;
        }

        private void RefreshMappingsAndCode()
        {
            var cfg = GetConfig();
            var validation = cfg.Validate();
            ValidationWarning = validation.Warning;
            ValidationError = validation.Error;
            OnPropertyChanged(nameof(HasValidationWarning));
            OnPropertyChanged(nameof(HasValidationError));

            if (_canvasWidth > 0 && _canvasHeight > 0)
            {
                int dataSize = ((_canvasWidth + 7) / 8) * _canvasHeight;
                int totalPacketSize = 6 + dataSize + 1;
                int maxBuffer = HardwarePreviewWiringConfig.GetMaxBufferSize(SelectedBoardPreset);
                if (totalPacketSize > maxBuffer)
                {
                    CanvasBufferWarning = $"⚠ Current canvas ({_canvasWidth}×{_canvasHeight}, requires {totalPacketSize:N0} B) exceeds {SelectedBoardPreset}'s RAM buffer ({maxBuffer:N0} B). Resize canvas or select Mega/ESP32.";
                }
                else
                {
                    CanvasBufferWarning = null;
                }
            }
            else
            {
                CanvasBufferWarning = null;
            }
            OnPropertyChanged(nameof(HasCanvasBufferWarning));

            WireMappings.Clear();
            foreach (var m in HardwarePreviewSketchGenerator.GetWireMapping(cfg))
            {
                WireMappings.Add(m);
            }

            GeneratedSketch = HardwarePreviewSketchGenerator.GenerateArduinoSketch(cfg, SelectedBaudRate);
            GeneratedSnippet = HardwarePreviewSketchGenerator.GenerateLibrarySnippet(cfg);
            GeneratedPlatformIOConfig = HardwarePreviewSketchGenerator.GeneratePlatformIOConfig(cfg, SelectedBaudRate);
            GeneratedPlatformIOIni = HardwarePreviewSketchGenerator.GeneratePlatformIOIni(cfg.BoardPreset, SelectedBaudRate);
        }

        private void ExecuteCopySketch()
        {
            if (_clipboard != null)
            {
                _clipboard.SetText(GeneratedSketch);
            }
            else
            {
                System.Windows.Clipboard.SetText(GeneratedSketch);
            }
            StatusMessage = "✓ Arduino sketch copied to clipboard";
        }

        private void ExecuteCopySnippet()
        {
            if (_clipboard != null)
            {
                _clipboard.SetText(GeneratedSnippet);
            }
            else
            {
                System.Windows.Clipboard.SetText(GeneratedSnippet);
            }
            StatusMessage = "✓ Library snippet copied to clipboard";
        }

        private void ExecuteCopyPlatformIOConfig()
        {
            if (_clipboard != null)
            {
                _clipboard.SetText(GeneratedPlatformIOConfig);
            }
            else
            {
                System.Windows.Clipboard.SetText(GeneratedPlatformIOConfig);
            }
            StatusMessage = "✓ PlatformIO config.h copied to clipboard";
        }

        private void ExecuteCopyPlatformIOIni()
        {
            if (_clipboard != null)
            {
                _clipboard.SetText(GeneratedPlatformIOIni);
            }
            else
            {
                System.Windows.Clipboard.SetText(GeneratedPlatformIOIni);
            }
            StatusMessage = "✓ PlatformIO platformio.ini copied to clipboard";
        }

        private void ExecuteOpenPlatformIOFolder()
        {
            try
            {
                var cfg = GetConfig();
                HardwarePreviewSketchGenerator.UpdatePlatformIOConfigInAppData(cfg, SelectedBaudRate);
                string? path = AssetsPathService.ResolveHexpritePreviewPlatformIOPath();
                if (path != null)
                {
                    string explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(explorerPath, path) { UseShellExecute = true });
                    StatusMessage = "✓ PlatformIO project folder opened";
                }
                else
                {
                    ValidationError = "Could not locate PlatformIO project folder.";
                }
            }
            catch (Exception ex)
            {
                ValidationError = $"Failed to open PlatformIO folder: {ex.Message}";
            }
        }

        private void ExecuteOpenInArduinoIde()
        {
            try
            {
                var cfg = GetConfig();
                HardwarePreviewSketchGenerator.UpdateStandaloneSketchInAppData(cfg, SelectedBaudRate);
                string? path = AssetsPathService.ResolveHexpritePreviewStandalonePath();
                if (path != null)
                {
                    string inoFile = Path.Combine(path, AssetsPathService.StandaloneSketchFileName);
                    if (File.Exists(inoFile))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(inoFile) { UseShellExecute = true });
                        StatusMessage = "✓ Opening Arduino sketch in IDE...";
                    }
                    else
                    {
                        string explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(explorerPath, path) { UseShellExecute = true });
                    }
                }
                else
                {
                    ValidationError = "Could not locate standalone Arduino sketch.";
                }
            }
            catch (Exception ex)
            {
                ValidationError = $"Failed to open Arduino IDE: {ex.Message}";
            }
        }

        private async System.Threading.Tasks.Task ExecuteAutoDetectBaudRateAsync()
        {
            if (_hardwarePreview == null || !_hardwarePreview.IsEnabled)
            {
                StatusMessage = "⚡ Auto-detect requires an active hardware preview connection.";
                return;
            }

            IsAutoDetectingBaudRate = true;
            StatusMessage = "⚡ Probing baud rates (115200, 9600, 57600...)...";

            int[] standardBauds = [115200, 9600, 57600, 38400, 19200, 230400, 460800, 921600];
            int originalBaud = SelectedBaudRate;
            int detectedBaud = 0;

            int w = _canvasWidth > 0 ? _canvasWidth : 128;
            int h = _canvasHeight > 0 ? _canvasHeight : 64;
            bool[] probePixels = _hardwarePreview.LastTransmittedFrame.Pixels ?? new bool[w * h];
            int probeW = _hardwarePreview.LastTransmittedFrame.Width > 0 ? _hardwarePreview.LastTransmittedFrame.Width : w;
            int probeH = _hardwarePreview.LastTransmittedFrame.Height > 0 ? _hardwarePreview.LastTransmittedFrame.Height : h;

            try
            {
                foreach (int baud in standardBauds)
                {
                    _hardwarePreview.ResetConnectionForBaudProbe();
                    _hardwarePreview.BaudRate = baud;
                    _hardwarePreview.SendFrame(probePixels, probeW, probeH);

                    DateTime probeStart = DateTime.UtcNow;
                    while (DateTime.UtcNow - probeStart < TimeSpan.FromMilliseconds(600))
                    {
                        if (_hardwarePreview.ConnectionState == HardwarePreviewConnectionState.Connected)
                        {
                            detectedBaud = baud;
                            break;
                        }
                        await System.Threading.Tasks.Task.Delay(50);
                    }

                    if (detectedBaud > 0) break;
                }

                if (detectedBaud > 0)
                {
                    SelectedBaudRate = detectedBaud;
                    StatusMessage = $"✓ Detected board responding at {detectedBaud} baud!";
                }
                else
                {
                    _hardwarePreview.BaudRate = originalBaud;
                    StatusMessage = "⚡ Auto-detect finished. No board ACK seen. Restored previous baud rate.";
                }
            }
            catch (Exception ex)
            {
                _hardwarePreview.BaudRate = originalBaud;
                StatusMessage = $"⚠ Auto-detect error: {ex.Message}";
            }
            finally
            {
                IsAutoDetectingBaudRate = false;
            }
        }

        private void ExecuteExportSketch()
        {
            string? folder = _dialogService?.ShowOpenFolderDialog("Select folder to export HexpritePreview sketch");
            if (string.IsNullOrEmpty(folder)) return;

            try
            {
                string sketchDir = Path.Combine(folder, "HexpritePreview");
                Directory.CreateDirectory(sketchDir);
                string sketchFile = Path.Combine(sketchDir, "HexpritePreview.ino");
                File.WriteAllText(sketchFile, GeneratedSketch);
                StatusMessage = $"✓ Exported to {Path.GetFileName(sketchDir)}/HexpritePreview.ino";
            }
            catch (Exception ex)
            {
                ValidationError = $"Export failed: {ex.Message}";
            }
        }

        public void ExecuteSaveAsDefault()
        {
            var cfg = GetConfig();
            UserPreferencesService.Update(p =>
            {
                p.HardwarePreviewBoardPreset = cfg.BoardPreset;
                p.HardwarePreviewInterfaceType = cfg.InterfaceType;
                p.HardwarePreviewDisplayModel = cfg.DisplayModel;
                p.HardwarePreviewSdaPin = cfg.SdaPin;
                p.HardwarePreviewSclPin = cfg.SclPin;
                p.HardwarePreviewI2cAddress = cfg.I2cAddress;
                p.HardwarePreviewUseSoftwareI2c = cfg.UseSoftwareI2c;
                p.HardwarePreviewCsPin = cfg.CsPin;
                p.HardwarePreviewDcPin = cfg.DcPin;
                p.HardwarePreviewRstPin = cfg.RstPin;
                p.HardwarePreviewClkPin = cfg.ClkPin;
                p.HardwarePreviewMosiPin = cfg.MosiPin;
                p.HardwarePreviewBaudRate = cfg.BaudRate;
            });

            HardwarePreviewSketchGenerator.UpdateStandaloneSketchInAppData(cfg, SelectedBaudRate);
            HardwarePreviewSketchGenerator.UpdatePlatformIOConfigInAppData(cfg, SelectedBaudRate);
            StatusMessage = "✓ Settings saved as default and sketch updated";
        }
    }
}
