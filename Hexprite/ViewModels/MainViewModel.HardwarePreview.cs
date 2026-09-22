using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Rendering;
using Hexprite.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Globalization;

namespace Hexprite.ViewModels
{
    public partial class MainViewModel
    {
        // ── Hardware Preview state ─────────────────────────────────────────
        public ObservableCollection<HardwarePreviewPortOption> AvailablePorts { get; }
        public IAsyncRelayCommand RefreshPortsCommand { get; }
        public IRelayCommand OpenSketchFolderCommand { get; }
        public IRelayCommand OpenStandaloneSketchCommand { get; }
        public IRelayCommand OpenPinoutGuideCommand { get; }
        public IRelayCommand ConfigureHardwarePreviewWiringCommand { get; }
        public IAsyncRelayCommand AutoDetectBaudRateCommand { get; }
        public IRelayCommand ToggleHardwarePreviewConnectionCommand { get; }
        public IRelayCommand OpenInArduinoIdeCommand { get; }
        public IRelayCommand OpenPlatformIOCommand { get; }
        public IRelayCommand ToggleTroubleshootingCommand { get; }

        public HardwarePreviewWiringConfig HardwarePreviewWiringConfig { get; }

        public string HardwarePreviewWiringSummary
        {
            get
            {
                if (HardwarePreviewWiringConfig.InterfaceType == "I2C")
                {
                    return $"{HardwarePreviewWiringConfig.BoardPreset} · SDA: {HardwarePreviewWiringConfig.SdaPin}, SCL: {HardwarePreviewWiringConfig.SclPin} · {HardwarePreviewWiringConfig.I2cAddress}";
                }
                else
                {
                    return $"{HardwarePreviewWiringConfig.BoardPreset} · SPI (CS: {HardwarePreviewWiringConfig.CsPin}, DC: {HardwarePreviewWiringConfig.DcPin})";
                }
            }
        }

        public void NotifyHardwarePreviewWiringChanged()
        {
            OnPropertyChanged(nameof(HardwarePreviewWiringSummary));
            OnPropertyChanged(nameof(TargetDisplayInfo));
        }

        private readonly SemaphoreSlim _portRefreshLock = new(1, 1);
        private bool _isApplyingPortOptions;

        private void PortAutoRefreshTimer_Tick(object? sender, EventArgs e)
        {
            // Never poll while connected — avoid disturbing an active link with WMI/registry churn.
            if (!IsHardwarePreviewEnabled)
            {
                _ = AutoRefreshPortsAsync();
            }
        }

        internal async System.Threading.Tasks.Task AutoRefreshPortsAsync()
        {
            // If a refresh is already in progress, skip this tick without waiting
            if (!await _portRefreshLock.WaitAsync(0, CancellationToken.None))
            {
                return;
            }

            try
            {
                // Fast-path: check bare port names first without querying WMI.
                // SerialPort.GetPortNames() is instantaneous (<1ms) via registry.
                string[] currentPorts = await System.Threading.Tasks.Task.Run(
                    () => _hardwarePreview.GetAvailablePorts().ToArray(), CancellationToken.None);

                string[] knownPorts = AvailablePorts.Select(p => p.PortName).ToArray();

                // If the set of port names hasn't changed, avoid heavy WMI queries and collection churn
                if (currentPorts.Length == knownPorts.Length &&
                    currentPorts.SequenceEqual(knownPorts, StringComparer.OrdinalIgnoreCase))
                {
                    return;
                }

                // Port list changed (hardware plugged in or unplugged) — perform full scan with friendly names
                List<HardwarePreviewPortOption> options = await System.Threading.Tasks.Task.Run(
                    () => _hardwarePreview.GetAvailablePortOptions().ToList(), CancellationToken.None);

                ApplyPortOptions(options);
            }
            finally
            {
                _portRefreshLock.Release();
            }
        }

        private async System.Threading.Tasks.Task RefreshPortsAsync()
        {
            await _portRefreshLock.WaitAsync(CancellationToken.None);
            try
            {
                List<HardwarePreviewPortOption> options = await System.Threading.Tasks.Task.Run(
                    () => _hardwarePreview.GetAvailablePortOptions().ToList(), CancellationToken.None);

                ApplyPortOptions(options);
            }
            finally
            {
                _portRefreshLock.Release();
            }
        }

        private void ApplyPortOptions(List<HardwarePreviewPortOption> options)
        {
            // Avoid collection churn and losing ComboBox selection if ports have not changed
            if (options.Count == AvailablePorts.Count && options.SequenceEqual(AvailablePorts))
            {
                return;
            }

            _isApplyingPortOptions = true;
            try
            {
                var prefs = UserPreferencesService.Get();
                string? targetAutoConnectPort = prefs.HardwarePreviewPort;
                bool wasTargetPortPresentBefore = !string.IsNullOrEmpty(targetAutoConnectPort) &&
                    AvailablePorts.Any(p => string.Equals(p.PortName, targetAutoConnectPort, StringComparison.OrdinalIgnoreCase));

                string? currentPort = HardwarePreviewPort;

                AvailablePorts.Clear();
                foreach (var option in options)
                    AvailablePorts.Add(option);

                // Restore previously selected port if still present
                if (currentPort != null && options.Any(o => string.Equals(o.PortName, currentPort, StringComparison.OrdinalIgnoreCase)))
                {
                    HardwarePreviewPort = currentPort;
                }
                else if (string.IsNullOrEmpty(HardwarePreviewPort))
                {
                    // Auto-select from UserPreferences or single available port
                    if (!string.IsNullOrEmpty(prefs.HardwarePreviewPort) && options.Any(o => string.Equals(o.PortName, prefs.HardwarePreviewPort, StringComparison.OrdinalIgnoreCase)))
                    {
                        HardwarePreviewPort = prefs.HardwarePreviewPort;
                    }
                    else if (options.Count == 1)
                    {
                        HardwarePreviewPort = options[0].PortName;
                    }
                }

                // Hotplug auto-connect: if configured, auto-connect when the saved port was newly plugged in
                if (prefs.HardwarePreviewAutoConnect && !IsHardwarePreviewEnabled && !string.IsNullOrEmpty(targetAutoConnectPort))
                {
                    bool isTargetPortPresentNow = options.Any(o => string.Equals(o.PortName, targetAutoConnectPort, StringComparison.OrdinalIgnoreCase));
                    if (isTargetPortPresentNow && !wasTargetPortPresentBefore)
                    {
                        HardwarePreviewPort = targetAutoConnectPort;
                        IsHardwarePreviewEnabled = true;
                        ShowStatus($"✓ Hardware preview auto-connected to {targetAutoConnectPort}");
                    }
                }
            }
            finally
            {
                _isApplyingPortOptions = false;
            }
        }

        public string HardwarePreviewConnectionButtonText => _hardwarePreview.ConnectionState switch
        {
            HardwarePreviewConnectionState.Connecting => "⏳ Connecting…",
            HardwarePreviewConnectionState.Streaming or HardwarePreviewConnectionState.Connected => "⏹ Disconnect",
            HardwarePreviewConnectionState.Error => "⚡ Reconnect",
            _ => "⚡ Connect Hardware"
        };

        public string HardwarePreviewStatusText => _hardwarePreview.ConnectionState switch
        {
            HardwarePreviewConnectionState.Connecting => "● Connecting…",
            HardwarePreviewConnectionState.Streaming => $"● Streaming ({HardwarePreviewBaudRate:N0} baud)",
            HardwarePreviewConnectionState.Connected => $"● Connected · Synced ({HardwarePreviewBaudRate:N0} baud)",
            HardwarePreviewConnectionState.Error => $"● {_hardwarePreview.LastDisableReason ?? "Error"}",
            _ => "○ Disconnected",
        };

        public Brush HardwarePreviewStatusBrush => _hardwarePreview.ConnectionState switch
        {
            HardwarePreviewConnectionState.Connecting => Brushes.Orange,
            HardwarePreviewConnectionState.Streaming => Brushes.DodgerBlue,
            HardwarePreviewConnectionState.Connected => Brushes.LimeGreen,
            HardwarePreviewConnectionState.Error => Brushes.OrangeRed,
            _ => Brushes.Gray,
        };

        public bool IsHardwarePreviewError => _hardwarePreview.ConnectionState == HardwarePreviewConnectionState.Error;

        private bool _isTroubleshootingVisible;
        public bool IsTroubleshootingVisible
        {
            get => _isTroubleshootingVisible;
            set => SetProperty(ref _isTroubleshootingVisible, value);
        }

        public string TroubleshootingGuide
        {
            get
            {
                string reason = _hardwarePreview.LastDisableReason ?? string.Empty;
                if (reason.Contains("NACK", StringComparison.OrdinalIgnoreCase) || reason.Contains("I2C", StringComparison.OrdinalIgnoreCase))
                {
                    return "• Swap SDA and SCL wires on your board\n• Verify display I2C address (0x3C or 0x3D)\n• Ensure VCC (3.3V/5V) and GND are connected";
                }
                if (reason.Contains("Access", StringComparison.OrdinalIgnoreCase) || reason.Contains("denied", StringComparison.OrdinalIgnoreCase))
                {
                    return "• Close Arduino IDE Serial Monitor / Plotter\n• Close VS Code PlatformIO serial monitor\n• Close 3D printer software (Cura, PrusaSlicer)";
                }
                if (reason.Contains("Checksum", StringComparison.OrdinalIgnoreCase) || reason.Contains("Noise", StringComparison.OrdinalIgnoreCase))
                {
                    return "• Ensure baud rate matches the sketch\n• Use a shorter or higher quality USB cable\n• Press Reset button on your board";
                }
                return "• Check USB cable connection\n• Verify correct COM port is selected\n• Re-upload the standalone sketch";
            }
        }

        public sealed record HardwarePreviewPlacementOption(HardwarePreviewPlacement Placement, string DisplayName);
        public sealed record HardwarePreviewScaleOption(HardwarePreviewScale Scale, string DisplayName);

        public static IReadOnlyList<HardwarePreviewPlacementOption> AvailablePlacements { get; } =
        [
            new(HardwarePreviewPlacement.TopLeft, "Top-Left"),
            new(HardwarePreviewPlacement.Center, "Center"),
        ];

        public static IReadOnlyList<HardwarePreviewScaleOption> AvailableScales { get; } =
        [
            new(HardwarePreviewScale.Scale1x, "1× Pixel"),
            new(HardwarePreviewScale.Scale2x, "2× Pixel"),
            new(HardwarePreviewScale.Scale4x, "4× Pixel"),
            new(HardwarePreviewScale.ScaleToFit, "Fit Display"),
        ];

        public HardwarePreviewPlacement HardwarePreviewPlacement
        {
            get => _hardwarePreview.Placement;
            set
            {
                if (_hardwarePreview.Placement != value)
                {
                    _hardwarePreview.Placement = value;
                    UserPreferencesService.Update(p => p.HardwarePreviewPlacement = value.ToString());
                    OnPropertyChanged();
                    if (IsHardwarePreviewEnabled) TriggerHardwarePreviewUpdate();
                }
            }
        }

        public HardwarePreviewScale HardwarePreviewScale
        {
            get => _hardwarePreview.Scale;
            set
            {
                if (_hardwarePreview.Scale != value)
                {
                    _hardwarePreview.Scale = value;
                    UserPreferencesService.Update(p => p.HardwarePreviewScale = value.ToString());
                    OnPropertyChanged();
                    if (IsHardwarePreviewEnabled) TriggerHardwarePreviewUpdate();
                }
            }
        }

        public string TargetDisplayInfo => $"{HardwarePreviewWiringConfig.DisplayModel} ({_hardwarePreview.TargetDisplaySize.Width}×{_hardwarePreview.TargetDisplaySize.Height})";

        public bool IsHardwarePreviewEnabled
        {
            get => _hardwarePreview.IsEnabled;
            set
            {
                if (_hardwarePreview.IsEnabled != value)
                {
                    _hardwarePreview.IsEnabled = value;
                    OnPropertyChanged();
                    if (value) RedrawGridFromMemory();
                }
            }
        }

        public bool HardwarePreviewAutoConnect
        {
            get => UserPreferencesService.Get().HardwarePreviewAutoConnect;
            set
            {
                if (UserPreferencesService.Get().HardwarePreviewAutoConnect != value)
                {
                    UserPreferencesService.Update(p => p.HardwarePreviewAutoConnect = value);
                    OnPropertyChanged();
                }
            }
        }

        public string? HardwarePreviewPort
        {
            get => _hardwarePreview.PortName;
            set
            {
                if (_isApplyingPortOptions && value == null) return;
                if (_hardwarePreview.PortName != value)
                {
                    _hardwarePreview.PortName = value;
                    OnPropertyChanged();
                    UserPreferencesService.Update(p => p.HardwarePreviewPort = value);
                    if (IsHardwarePreviewEnabled) TriggerHardwarePreviewUpdate();
                }
            }
        }

        public int HardwarePreviewBaudRate
        {
            get => _hardwarePreview.BaudRate;
            set
            {
                if (_hardwarePreview.BaudRate != value)
                {
                    _hardwarePreview.BaudRate = value;
                    OnPropertyChanged();
                    UserPreferencesService.Update(p => p.HardwarePreviewBaudRate = value);
                    HardwarePreviewSketchGenerator.UpdateStandaloneSketchInAppData(HardwarePreviewWiringConfig, value);
                    if (IsHardwarePreviewEnabled) TriggerHardwarePreviewUpdate();
                }
            }
        }

        public void RedrawGridFromMemory(bool updateHardware = true)
        {
            if (CanvasBitmap == null || _canvasBuffer == null || SpriteState?.Pixels == null) return;

            if (!CanvasBitmap.Dispatcher.CheckAccess())
            {
                if (CanvasBitmap.Dispatcher.HasShutdownStarted || CanvasBitmap.Dispatcher.HasShutdownFinished) return;
                CanvasBitmap.Dispatcher.BeginInvoke(() => RedrawGridFromMemory(updateHardware));
                return;
            }

            // Sync the current canvas state back to the active pixel buffer (if it preserves overflow)
            SpriteState.SyncActiveLayer();

            // Fix #7: Update onion skin cache before redraw so changes to global layers or undone states are reflected.
            if (_isAnimationEnabled && (_isOnionSkinPrevEnabled || _isOnionSkinNextEnabled))
            {
                UpdateOnionSkinCache();
            }

            using var perfScope = BeginDrawPerfScope("RedrawGridFromMemory");

            int w = SpriteState.Width;
            int h = SpriteState.Height;
            var visibleLayers = GetVisibleLayersData();
            int visibleLayerCount = visibleLayers.Count;
            bool hasFloating = _selectionService.IsFloating && _selectionService.FloatingPixels != null;
            int floatingX, floatingY, floatingW, floatingH;
            bool[,]? floatingPixels;
            bool[,]? floatingMask = null;
            if (hasFloating)
            {
                var eff = _selectionService.GetEffectiveFloating();
                floatingPixels = eff.pixels;
                floatingMask = eff.mask;
                floatingX = eff.x;
                floatingY = eff.y;
                floatingW = eff.w;
                floatingH = eff.h;
            }
            else
            {
                floatingPixels = null;
                floatingX = floatingY = floatingW = floatingH = 0;
            }

            // Focus mode: determine if we need per-layer opacity handling
            bool useFocusMode = _isFocusModeEnabled && visibleLayerCount > 1;
            int activeLayerIndexAmongVisible = useFocusMode ? GetActiveLayerIndexAmongVisible() : 0;

            // Onion skin: get previous/next frame pixels
            bool[]? prevFramePixels = _cachedPrevFramePixels;
            bool[]? nextFramePixels = _cachedNextFramePixels;

            if (visibleLayers.Count == 1 && visibleLayers[0].State.OpacityMode == LayerOpacityMode.Solid &&
                !hasFloating && !useFocusMode && prevFramePixels == null && nextFramePixels == null)
            {
                bool[] singlePixels = visibleLayers[0].Pixels;
                int totalPixels = Math.Min(singlePixels.Length, w * h);
                for (int i = 0; i < totalPixels; i++)
                {
                    bool isPixelOn = singlePixels[i];
                    _canvasBuffer[i] = isPixelOn ? _colorOnUint : _colorOffUint;
                    _previewBuffer[i] = isPixelOn ? _previewOnUint : _previewOffUint;
                    if (IsHardwarePreviewEnabled) _hardwareBuffer[i] = isPixelOn;
                }
            }
            else
            {
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int i = (y * w) + x;

                        // Determine pixel state (with focus mode awareness if enabled)
                        bool isPixelOn;
                        bool isFromActiveLayer = true;

                        if (useFocusMode)
                        {
                            var (on, fromActive) = ComposePixelStateWithFocus(i, visibleLayers, activeLayerIndexAmongVisible, w);
                            isPixelOn = on;
                            isFromActiveLayer = fromActive;
                        }
                        else
                        {
                            isPixelOn = ComposePixelState(i, visibleLayers, w);
                        }

                        // Overlay the floating selection layer if one is active
                        if (hasFloating && floatingPixels != null)
                        {
                            int fx = x - floatingX;
                            int fy = y - floatingY;
                            if (fx >= 0 && fx < floatingW &&
                                fy >= 0 && fy < floatingH)
                            {
                                if (floatingMask == null || floatingMask[fx, fy])
                                {
                                    bool floatingPixel = floatingPixels[fx, fy];
                                    // FloatingPasteMode determines how false pixels are handled
                                    if (_floatingPasteMode == FloatingPasteMode.Transparent)
                                    {
                                        // Transparent: only true pixels show (false = skip/transparent)
                                        if (floatingPixel)
                                        {
                                            isPixelOn = true;
                                            isFromActiveLayer = true;
                                        }
                                    }
                                    else // Opaque
                                    {
                                        // Opaque: all pixels overwrite canvas (full stamp)
                                        isPixelOn = floatingPixel;
                                        isFromActiveLayer = true;
                                    }
                                }
                            }
                        }

                        // Apply focus mode dimming and onion skin if needed
                        uint canvasColor;
                        uint previewColor;
                        if (isPixelOn)
                        {
                            if (useFocusMode && !isFromActiveLayer)
                            {
                                // Dimmed color for inactive layers
                                canvasColor = GetDimmedColor(_colorOnUint, _inactiveFocusOpacity);
                                previewColor = GetDimmedColor(_previewOnUint, _inactiveFocusOpacity);
                            }
                            else
                            {
                                // Full color for active layer or when focus mode is off
                                canvasColor = _colorOnUint;
                                previewColor = _previewOnUint;
                            }
                        }
                        else if (prevFramePixels != null && prevFramePixels[i] && nextFramePixels != null && nextFramePixels[i])
                        {
                            canvasColor = _onionSkinBothUint;
                            previewColor = _previewOffUint;
                        }
                        else if (prevFramePixels != null && prevFramePixels[i])
                        {
                            canvasColor = _onionSkinPrevUint;
                            previewColor = _previewOffUint; 
                        }
                        else if (nextFramePixels != null && nextFramePixels[i])
                        {
                            canvasColor = _onionSkinNextUint;
                            previewColor = _previewOffUint; 
                        }
                        else
                        {
                            canvasColor = _colorOffUint;
                            previewColor = _previewOffUint;
                        }

                        _canvasBuffer[i] = canvasColor;
                        _previewBuffer[i] = previewColor;
                        if (IsHardwarePreviewEnabled) _hardwareBuffer[i] = isPixelOn;
                    }
                }
            }

            var rect = new Int32Rect(0, 0, w, h);
            using (BeginDrawPerfScope("WritePixels.Full.Canvas"))
            {
                CanvasBitmap.WritePixels(rect, _canvasBuffer, w * 4, 0);
            }
            if (!_isStrokeRenderingActive)
            {
                using (BeginDrawPerfScope("WritePixels.Full.Preview"))
                {
                    PreviewBitmap.WritePixels(rect, _previewBuffer, w * 4, 0);
                }
            }

            if (IsHardwarePreviewEnabled && updateHardware)
            {
                TriggerHardwarePreviewUpdate();
            }

            if (_isPlaying && _playbackFrameCache != null && SpriteState != null && 
                SpriteState.ActiveFrameIndex >= 0 && SpriteState.ActiveFrameIndex < _playbackFrameCache.Length)
            {
                _playbackFrameCache[SpriteState.ActiveFrameIndex] = SpriteState.CompositeFramePixels(SpriteState.ActiveFrameIndex);
                if (_playbackFrameIndex == SpriteState.ActiveFrameIndex)
                {
                    UpdatePlaybackPreview();
                }
            }

            UpdatePreviewSimulation();
            UpdateAllLayersContentIndicator();
        }

        private bool _hwFrameTooLargeWarned;

        private void TriggerHardwarePreviewUpdate()
        {
            if (!IsHardwarePreviewEnabled || _hardwareBuffer == null) return;
            if (_isPlaying) return; // Hardware preview is updated by UpdatePlaybackPreview during animation

            if (IsFrameTooLargeForHardware())
            {
                if (!_hwFrameTooLargeWarned)
                {
                    ShowStatus("⚠ Canvas too large for hardware preview buffer. Resize canvas or use a board with more RAM.", 8000);
                    _hwFrameTooLargeWarned = true;
                }
                return;
            }
            _hwFrameTooLargeWarned = false;

            _hardwarePreview.SendFrame(_hardwareBuffer, SpriteState.Width, SpriteState.Height);
        }

        /// <summary>
        /// Checks if the current canvas XBM data exceeds the board's receive buffer.
        /// Board-aware buffer limits:
        /// Uno/Nano: 1,040 bytes; Mega: 8,200 bytes; ESP32/RP2040/STM32: 32,800 bytes; Custom: 4,200 bytes.
        /// Accounts for placement, scaling, and target display dimensions.
        /// </summary>
        private bool IsFrameTooLargeForHardware()
        {
            if (SpriteState == null) return false;
            int srcW = SpriteState.Width;
            int srcH = SpriteState.Height;
            if (srcW <= 0 || srcH <= 0) return false;

            int scale = HardwarePreviewScale switch
            {
                HardwarePreviewScale.Scale2x => 2,
                HardwarePreviewScale.Scale4x => 4,
                HardwarePreviewScale.ScaleToFit => _hardwarePreview.TargetDisplaySize.Width > 0 && _hardwarePreview.TargetDisplaySize.Height > 0
                    ? Math.Max(1, Math.Min(_hardwarePreview.TargetDisplaySize.Width / srcW, _hardwarePreview.TargetDisplaySize.Height / srcH))
                    : 1,
                _ => 1
            };

            int outW = _hardwarePreview.TargetDisplaySize.Width > 0 ? _hardwarePreview.TargetDisplaySize.Width : srcW * scale;
            int outH = _hardwarePreview.TargetDisplaySize.Height > 0 ? _hardwarePreview.TargetDisplaySize.Height : srcH * scale;
            int scaledW = srcW * scale;
            int scaledH = srcH * scale;
            if (outW < scaledW) outW = scaledW;
            if (outH < scaledH) outH = scaledH;

            int dataSize = ((outW + 7) / 8) * outH;
            int maxBuffer = HardwarePreviewWiringConfig.GetMaxBufferSize(HardwarePreviewWiringConfig.BoardPreset);
            return dataSize + 7 > maxBuffer;
        }

        private async System.Threading.Tasks.Task AutoDetectBaudRateAsync()
        {
            if (string.IsNullOrEmpty(HardwarePreviewPort))
            {
                ShowStatus("⚠ Please select a serial port before auto-detecting baud rate.", 5000);
                return;
            }

            // Ensure preview is enabled so the port is open
            if (!IsHardwarePreviewEnabled)
            {
                IsHardwarePreviewEnabled = true;
                // Allow time for the port to open and the microcontroller bootloader to finish (~2.2s)
                await System.Threading.Tasks.Task.Delay(2200, CancellationToken.None);
            }

            int originalBaud = HardwarePreviewBaudRate;
            int[] candidates = [115200, 921600, 230400, 57600, 19200, 9600];

            ShowStatus("⚡ Probing board baud rate…", 10000);

            try
            {
                foreach (int candidate in candidates)
                {
                    _hardwarePreview.ResetConnectionForBaudProbe();
                    _hardwarePreview.BaudRate = candidate;

                    // Send current frame to trigger an ACK response
                    if (_hardwareBuffer != null && SpriteState != null)
                    {
                        _hardwarePreview.SendFrame(_hardwareBuffer, SpriteState.Width, SpriteState.Height);
                    }

                    // Wait up to 600ms for ACK confirmation
                    DateTime probeStart = DateTime.UtcNow;
                    while (DateTime.UtcNow - probeStart < TimeSpan.FromMilliseconds(600))
                    {
                        if (_hardwarePreview.ConnectionState == HardwarePreviewConnectionState.Connected)
                        {
                            HardwarePreviewBaudRate = candidate;
                            ShowStatus($"✓ Auto-detected board baud rate: {candidate} baud", 6000);
                            return;
                        }
                        await System.Threading.Tasks.Task.Delay(50, CancellationToken.None);
                    }
                }

                // If no candidate succeeded, restore original baud rate
                _hardwarePreview.BaudRate = originalBaud;
                HardwarePreviewBaudRate = originalBaud;
                ShowStatus("⚠ No response at standard baud rates. Ensure HexpritePreview sketch is uploaded and running.", 8000);
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Error(ex, "MainViewModel.AutoDetectBaudRate");
                _hardwarePreview.BaudRate = originalBaud;
                HardwarePreviewBaudRate = originalBaud;
            }
        }

        private void UpdateAllLayersContentIndicator()
        {
            if (SpriteState?.Frames == null || SpriteState.Frames.Count == 0 || Layers == null ||
                SpriteState.ActiveFrameIndex < 0 || SpriteState.ActiveFrameIndex >= SpriteState.Frames.Count)
                return;

            var framePixels = SpriteState.Frames[SpriteState.ActiveFrameIndex].LayerPixels;
            if (framePixels == null) return;

            int limit = Math.Min(Math.Min(SpriteState.Layers.Count, Layers.Count), framePixels.Count);
            for (int i = 0; i < limit; i++)
            {
                if (framePixels[i] == null) continue;
                bool hasContent = Array.IndexOf(framePixels[i].GetMonochromeData(), value: true) >= 0;
                if (Layers[i].HasContent != hasContent)
                    Layers[i].HasContent = hasContent;
            }
        }

        /// <summary>
        /// Partial redraw: only updates the pixel buffers and bitmaps within the
        /// specified bounding box. Much faster than a full <see cref="RedrawGridFromMemory"/>
        /// for small brush strokes on large canvases.
        /// </summary>
        public void RedrawRegion(int minX, int minY, int maxX, int maxY, bool updatePreviewSimulation = true)
        {
            if (CanvasBitmap == null || _canvasBuffer == null || SpriteState?.Pixels == null || SpriteState.Width <= 0 || SpriteState.Height <= 0) return;
            using var perfScope = BeginDrawPerfScope("RedrawRegion");

            int w = SpriteState.Width;
            int h = SpriteState.Height;

            // Clamp to canvas bounds
            int x0 = Math.Clamp(minX, 0, w - 1);
            int y0 = Math.Clamp(minY, 0, h - 1);
            int x1 = Math.Clamp(maxX, 0, w - 1);
            int y1 = Math.Clamp(maxY, 0, h - 1);
            if (x0 > x1 || y0 > y1) return;

            var visibleLayers = GetVisibleLayersData();
            int visibleLayerCount = visibleLayers.Count;
            bool hasFloating = _selectionService.IsFloating && _selectionService.FloatingPixels != null;
            int floatingX, floatingY, floatingW, floatingH;
            bool[,]? floatingPixels;
            bool[,]? floatingMask = null;
            if (hasFloating)
            {
                var eff = _selectionService.GetEffectiveFloating();
                floatingPixels = eff.pixels;
                floatingMask = eff.mask;
                floatingX = eff.x;
                floatingY = eff.y;
                floatingW = eff.w;
                floatingH = eff.h;
            }
            else
            {
                floatingPixels = null;
                floatingX = floatingY = floatingW = floatingH = 0;
            }

            // Focus mode: determine if we need per-layer opacity handling
            bool useFocusMode = _isFocusModeEnabled && visibleLayerCount > 1;
            int activeLayerIndexAmongVisible = useFocusMode ? GetActiveLayerIndexAmongVisible() : 0;

            // Onion skin: get previous/next frame pixels
            bool[]? prevFramePixels = _cachedPrevFramePixels;
            bool[]? nextFramePixels = _cachedNextFramePixels;

            if (visibleLayers.Count == 1 && visibleLayers[0].State.OpacityMode == LayerOpacityMode.Solid &&
                !hasFloating && !useFocusMode && prevFramePixels == null && nextFramePixels == null)
            {
                bool[] singlePixels = visibleLayers[0].Pixels;
                uint colOn = _colorOnUint;
                uint colOff = _colorOffUint;
                uint prevOn = _previewOnUint;
                uint prevOff = _previewOffUint;
                bool hwEnabled = IsHardwarePreviewEnabled;

                for (int y = y0; y <= y1; y++)
                {
                    int rowStart = y * w;
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = rowStart + x;
                        bool isPixelOn = singlePixels[i];
                        _canvasBuffer[i] = isPixelOn ? colOn : colOff;
                        _previewBuffer[i] = isPixelOn ? prevOn : prevOff;
                        if (hwEnabled) _hardwareBuffer[i] = isPixelOn;
                    }
                }
            }
            else
            {
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = (y * w) + x;

                        // Determine pixel state (with focus mode awareness if enabled)
                        bool isPixelOn;
                        bool isFromActiveLayer = true;

                        if (useFocusMode)
                        {
                            var (on, fromActive) = ComposePixelStateWithFocus(i, visibleLayers, activeLayerIndexAmongVisible, w);
                            isPixelOn = on;
                            isFromActiveLayer = fromActive;
                        }
                        else
                        {
                            isPixelOn = ComposePixelState(i, visibleLayers, w);
                        }

                        if (hasFloating && floatingPixels != null)
                        {
                            int fx = x - floatingX;
                            int fy = y - floatingY;
                            if (fx >= 0 && fx < floatingW &&
                                fy >= 0 && fy < floatingH)
                            {
                                if (floatingMask == null || floatingMask[fx, fy])
                                {
                                    bool floatingPixel = floatingPixels[fx, fy];
                                    // FloatingPasteMode determines how false pixels are handled
                                    if (_floatingPasteMode == FloatingPasteMode.Transparent)
                                    {
                                        // Transparent: only true pixels show (false = skip/transparent)
                                        if (floatingPixel)
                                        {
                                            isPixelOn = true;
                                            isFromActiveLayer = true;
                                        }
                                    }
                                    else // Opaque
                                    {
                                        // Opaque: all pixels overwrite canvas (full stamp)
                                        isPixelOn = floatingPixel;
                                        isFromActiveLayer = true;
                                    }
                                }
                            }
                        }

                        // Apply focus mode dimming and onion skin if needed
                        uint canvasColor;
                        uint previewColor;
                        if (isPixelOn)
                        {
                            if (useFocusMode && !isFromActiveLayer)
                            {
                                // Dimmed color for inactive layers
                                canvasColor = GetDimmedColor(_colorOnUint, _inactiveFocusOpacity);
                                previewColor = GetDimmedColor(_previewOnUint, _inactiveFocusOpacity);
                            }
                            else
                            {
                                // Full color for active layer or when focus mode is off
                                canvasColor = _colorOnUint;
                                previewColor = _previewOnUint;
                            }
                        }
                        else if (prevFramePixels != null && prevFramePixels[i] && nextFramePixels != null && nextFramePixels[i])
                        {
                            canvasColor = _onionSkinBothUint;
                            previewColor = _previewOffUint;
                        }
                        else if (prevFramePixels != null && prevFramePixels[i])
                        {
                            canvasColor = _onionSkinPrevUint;
                            previewColor = _previewOffUint; 
                        }
                        else if (nextFramePixels != null && nextFramePixels[i])
                        {
                            canvasColor = _onionSkinNextUint;
                            previewColor = _previewOffUint; 
                        }
                        else
                        {
                            canvasColor = _colorOffUint;
                            previewColor = _previewOffUint;
                        }

                        _canvasBuffer[i] = canvasColor;
                        _previewBuffer[i] = previewColor;
                        if (IsHardwarePreviewEnabled) _hardwareBuffer[i] = isPixelOn;
                    }
                }
            }

            int regionW = x1 - x0 + 1;
            int regionH = y1 - y0 + 1;
            // Use the 5-param overload: sourceRect is the region within the source buffer,
            // destX/Y is where it lands in the bitmap. This avoids the buffer-size
            // validation issue with the offset-based overload.
            var srcRect = new Int32Rect(x0, y0, regionW, regionH);
            using (BeginDrawPerfScope("WritePixels.Region.Canvas"))
            {
                CanvasBitmap.WritePixels(srcRect, _canvasBuffer, w * 4, x0, y0);
            }
            if (!_isStrokeRenderingActive)
            {
                using (BeginDrawPerfScope("WritePixels.Region.Preview"))
                {
                    PreviewBitmap.WritePixels(srcRect, _previewBuffer, w * 4, x0, y0);
                }
            }
            if (updatePreviewSimulation)
                UpdatePreviewSimulation();

            if (IsHardwarePreviewEnabled)
                TriggerHardwarePreviewUpdate();

            if (_isPlaying && _playbackFrameCache != null && SpriteState != null && 
                SpriteState.ActiveFrameIndex >= 0 && SpriteState.ActiveFrameIndex < _playbackFrameCache.Length)
            {
                _playbackFrameCache[SpriteState.ActiveFrameIndex] = SpriteState.CompositeFramePixels(SpriteState.ActiveFrameIndex);
                if (_playbackFrameIndex == SpriteState.ActiveFrameIndex)
                {
                    UpdatePlaybackPreview();
                }
            }

            if (!_isStrokeRenderingActive && SpriteState.ActiveLayerIndex >= 0 && SpriteState.ActiveLayerIndex < Layers.Count)
            {
                bool hasContent = Array.IndexOf(SpriteState.Pixels, value: true) >= 0;
                if (Layers[SpriteState.ActiveLayerIndex].HasContent != hasContent)
                    Layers[SpriteState.ActiveLayerIndex].HasContent = hasContent;
            }

        }

        /// <summary>
        /// Main entry point for all non-selection tool input from the View.
        /// Delegates to the extracted ToolInputController.
        /// </summary>
        public void ProcessToolInput(int x, int y, ToolAction action, DrawMode mode, bool isShiftDown, bool isAltDown = false)
            => _toolInput.ProcessToolInput(x, y, action, mode, isShiftDown, isAltDown);

        /// <summary>
        /// Main entry point for selection tool input from the View.
        /// Delegates to the extracted SelectionInputController.
        /// </summary>
        public void ProcessSelectionInput(int x, int y, ToolAction action, bool isShiftDown, bool isAltDown, bool isInverse = false)
            => _selectionInput.ProcessInput(x, y, action, isShiftDown, isAltDown, isInverse);

        /// <summary>
        /// Notifies the selection controller that the mouse moved during a selection drag
        /// (even if pixel coordinates didn't change). Distinguishes click from drag.
        /// </summary>
        public void NotifySelectionMouseMoved()
            => _selectionInput.NotifyMouseMoved();

        /// <summary>
        /// Tries to begin a floating selection drag. Returns true if successful.
        /// </summary>
        public bool TryBeginSelectionDrag(int pixelX, int pixelY)
            => _selectionInput.TryBeginDrag(pixelX, pixelY);

        public TransformHandle HitTestSelectionHandle(double mouseImgX, double mouseImgY, double actualW, double actualH)
            => _selectionInput.HitTestHandle(mouseImgX, mouseImgY, actualW, actualH);

        public bool TryBeginSelectionTransform(TransformHandle handle)
            => _selectionInput.TryBeginTransform(handle);

        public void EnterSelectionTransformMode()
            => _selectionInput.EnterTransformMode();

        public void UpdateSelectionTransform(int deltaX, int deltaY, bool shiftAspect, bool altFromCenter)
            => _selectionInput.UpdateTransformFromDelta(deltaX, deltaY, shiftAspect, altFromCenter);

        public bool CommitSelectionIfActive(bool saveHistory = true)
            => _selectionInput.CommitIfActive(saveHistory);

        public void CommitSelectionTransformIfActive()
            => _selectionInput.CommitTransformIfActive();

        public void CancelSelectionTransformIfActive()
            => _selectionInput.CancelTransformIfActive();

        public void BeginSelectionRotation(double mouseImgX, double mouseImgY, double actualW, double actualH)
            => _selectionInput.BeginRotation(mouseImgX, mouseImgY, actualW, actualH);

        public void UpdateSelectionRotation(double mouseImgX, double mouseImgY, double actualW, double actualH, bool shiftConstrain)
            => _selectionInput.UpdateRotationFromMouse(mouseImgX, mouseImgY, actualW, actualH, shiftConstrain);

        public void UpdateSelectionDrag(int newX, int newY)
        {
            _selectionInput.UpdateDrag(newX, newY);
            if (IsTextEditing && _selectionService.IsFloating)
            {
                _textToolX = _selectionService.FloatingX;
                _textToolY = _selectionService.FloatingY;
                OnPropertyChanged(nameof(TextToolX));
                OnPropertyChanged(nameof(TextToolY));
                var fontFamily = _selectedFont?.FontFamily ?? new System.Windows.Media.FontFamily("Arial");
                var (cx, cy, ch) = Rendering.TextRenderer.GetCaretPosition(
                    TextToolContent, _textToolCaretIndex, fontFamily, FontSize, IsBold, IsItalic, LetterSpacing, PixelScale, LineHeight,
                    _textAlignment, _textToolX, _textToolY);
                _textCaretPixelX = cx;
                _textCaretPixelY = cy;
                _textCaretPixelHeight = ch;
                OnPropertyChanged(nameof(TextToolCaretX));
                OnPropertyChanged(nameof(TextToolCaretY));
                OnPropertyChanged(nameof(TextToolCaretHeight));
            }
        }

        public void CancelSelectionDragIfActive()
            => _selectionInput.CancelDragIfActive();

        public void SaveStateForUndo()
        {
            SpriteState.NormalizeLayerState();
            SpriteState.SelectionSnapshot = _selectionService.CreateSnapshot();
            _historyService.SaveState(SpriteState);
            IsDirty = true;
            UndoCommand?.NotifyCanExecuteChanged();
            RedoCommand?.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// Restores the linked source file and canvas from backup.
        /// When <paramref name="skipConfirmation"/> is true, no confirmation dialog is shown (used by batch restore).
        /// </summary>
        public async System.Threading.Tasks.Task ExecuteRestoreLinkedSourceAsync(bool skipConfirmation = false)
        {
            if (SpriteState == null || !SpriteState.IsLinked) return;

            using var operation = LoggingService.BeginOperation("MainViewModel.RestoreLinkedSource", new 
            { 
                file = SpriteState.LinkedSourceFile, 
                variable = SpriteState.LinkedVariableName,
                format = SpriteState.LinkedFormat,
                skipConfirmation
            });

            // Guard: linked file must still exist on disk
            if (!File.Exists(SpriteState.LinkedSourceFile))
            {
                _dialogService.ShowMessage($"The linked source file no longer exists:\n{SpriteState.LinkedSourceFile}");
                return;
            }

            // Guard: backup must exist to restore
            if (!_importExportService.HasBackup(SpriteState.LinkedSourceFile))
            {
                _dialogService.ShowMessage("No backup found. The linked source has not been modified by Hexprite yet.");
                return;
            }

            if (!skipConfirmation)
            {
                var confirm = _dialogService.ShowConfirmation(
                    $"Restore '{SpriteState.LinkedSourceFileName}' to its original state?\n\nThis will overwrite both the source file on disk and the current canvas with the Hexprite backup.",
                    "Restore Linked Source");
                if (!confirm) return;
            }

            try
            {
                SaveStateForUndo();

                SuspendLinkedFileWatcher();
                try
                {
                    string newHash = string.Empty;
                    await System.Threading.Tasks.Task.Run(() => 
                    {
                        string restoredText = _importExportService.RestoreSpriteInFile(SpriteState.LinkedSourceFile);
                        newHash = ComputeHashFromString(restoredText);
                        lock (_linkedFileLock)
                        {
                            if (!string.IsNullOrEmpty(newHash))
                            {
                                _globalLastSavedHashes[SpriteState.LinkedSourceFile] = newHash;
                                _documentSyncedHash = newHash;
                            }
                        }
                    }, CancellationToken.None);
                    
                    lock (_linkedFileLock)
                    {
                        if (!string.IsNullOrEmpty(newHash))
                        {
                            _globalLastSavedHashes[SpriteState.LinkedSourceFile] = newHash;
                            _documentSyncedHash = newHash;
                        }
                    }
                }
                finally
                {
                    ResumeLinkedFileWatcher();
                }

                var extracted = await System.Threading.Tasks.Task.Run(() => _importExportService.ExtractSpritesFromFile(SpriteState.LinkedSourceFile), CancellationToken.None);
                var match = extracted.FirstOrDefault(s => s.Name == SpriteState.LinkedVariableName && s.Format == SpriteState.LinkedFormat);
                if (match != null)
                {
                    if (match.Width != SpriteState.Width || match.Height != SpriteState.Height)
                    {
                        ResizeCanvas(match.Width, match.Height, ResizeAnchor.TopLeft);
                        SpriteState.NormalizeLayerState();
                    }

                    var backupState = SpriteState.Clone();
                    try
                    {
                        ParseCodeToState(_codeGen, match.Format, match.CodeSnippet, SpriteState);
                        SpriteState.NormalizeLayerState();
                    }
                    catch
                    {
                        RestoreState(backupState);
                        throw;
                    }
                    ReloadLayersFromState();
                    RebuildFrameViewModels();
                    IsAnimationEnabled = SpriteState.IsAnimationEnabled || (SpriteState.Frames != null && SpriteState.Frames.Count > 1);
                    RedrawGridFromMemory();
                    MarkCodeStale();
                    await UpdateTextOutputsAsync();
                    MarkAsClean();
                }
                else
                {
                    _dialogService.ShowMessage($"The restored backup of {SpriteState.LinkedSourceFileName} does not contain the linked sprite variable '{SpriteState.LinkedVariableName}'.");
                    return;
                }

                LinkedFileChangedExternally = false;
                ShowStatus($"✓ Restored {SpriteState.LinkedSourceFileName} to clean state");
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Error(ex, "MainViewModel.RestoreLinkedSource");
                _dialogService.ShowMessage($"Error restoring source: {ex.Message}");
            }
        }

        public async Task ExecutePullLinkedSourceAsync()
        {
            if (string.IsNullOrEmpty(SpriteState.LinkedSourceFile)) return;

            using var operation = LoggingService.BeginOperation("MainViewModel.PullLinkedSource", new 
            { 
                file = SpriteState.LinkedSourceFile, 
                variable = SpriteState.LinkedVariableName,
                format = SpriteState.LinkedFormat 
            });

            try
            {
                SaveStateForUndo();

                var extracted = await System.Threading.Tasks.Task.Run(() => _importExportService.ExtractSpritesFromFile(SpriteState.LinkedSourceFile), CancellationToken.None);
                var match = extracted.FirstOrDefault(s => s.Name == SpriteState.LinkedVariableName && s.Format == SpriteState.LinkedFormat);
                if (match != null)
                {
                    if (match.Width != SpriteState.Width || match.Height != SpriteState.Height)
                    {
                        ResizeCanvas(match.Width, match.Height, ResizeAnchor.TopLeft);
                        // Force Pixels to sync to the new canvas size before parsing
                        SpriteState.NormalizeLayerState();
                    }

                    var backupState = SpriteState.Clone();
                    try
                    {
                        ParseCodeToState(_codeGen, match.Format, match.CodeSnippet, SpriteState);
                        SpriteState.NormalizeLayerState();
                    }
                    catch
                    {
                        RestoreState(backupState);
                        throw;
                    }
                    ReloadLayersFromState();
                    RebuildFrameViewModels();
                    IsAnimationEnabled = SpriteState.IsAnimationEnabled || (SpriteState.Frames != null && SpriteState.Frames.Count > 1);
                    RedrawGridFromMemory();
                    MarkCodeStale();
                    await UpdateTextOutputsAsync();
                    MarkAsClean();
                    
                    string pulledHash = ComputeFileHash(SpriteState.LinkedSourceFile);
                    lock (_linkedFileLock)
                    {
                        if (!string.IsNullOrEmpty(pulledHash))
                            _documentSyncedHash = pulledHash;
                    }

                    LinkedFileChangedExternally = false;
                    ShowStatus($"✓ Pulled external changes from {SpriteState.LinkedSourceFileName}");
                }
                else
                {
                    _dialogService.ShowMessage($"Could not find a sprite named '{SpriteState.LinkedVariableName}' with format {SpriteState.LinkedFormat} in the file.");
                }
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Error(ex, "MainViewModel.PullLinkedSource");
                _dialogService.ShowMessage($"Error pulling external changes: {ex.Message}");
            }
        }

        public void ShiftGrid(int offsetX, int offsetY)
        {
            if (!CanModifyActiveLayer)
            {
                ShowStatus("Cannot modify: layer is locked, hidden, or multiple layers selected.", 3000);
                return;
            }
            SaveStateForUndo();
            var activeBuffer = SpriteState.ActivePixelBuffer;
            if (activeBuffer is Hexprite.Core.OverflowPixelBuffer ovf)
            {
                SpriteState.SyncActiveLayer(); // Write current canvas into extended buffer first
                ovf.ShiftContent(offsetX, offsetY);
                // Refresh state.Pixels from the shifted overflow buffer
                SpriteState.Pixels = ovf.GetMonochromeData();
            }
            else
            {
                _drawingService.ShiftGrid(SpriteState, offsetX, offsetY);
            }
            RedrawGridFromMemory();
            MarkCodeStale();
        }

        public void NudgeSelection(int offsetX, int offsetY)
        {
            if (IsProcessing || SpriteState == null || !_selectionService.HasActiveSelection) return;

            if (_selectionService.IsFloating)
            {
                if (!CanModifyActiveLayer)
                {
                    ShowStatus("Cannot modify: layer is locked, hidden, or multiple layers selected.", 3000);
                    return;
                }
                _selectionService.MoveFloatingTo(_selectionService.FloatingX + offsetX, _selectionService.FloatingY + offsetY);
            }
            else
            {
                _selectionService.NudgeSelection(offsetX, offsetY, SpriteState.Width, SpriteState.Height);
            }
            RedrawGridFromMemory();
        }

        // ── Private helpers ────────────────────────────────────────────────


        /// <summary>
        /// Cancels any in-progress shape drawing and resets tracking state.
        /// Called by the View when switching tools to prevent stale draw flags.
        /// </summary>
        public void CancelInProgressDrawing() => _toolInput.CancelInProgressDrawing();

        /// <summary>
        /// Prepares the document for a tool change by committing floating pixels
        /// and canceling in-progress drawing. Called by ShellViewModel when the
        /// global tool changes.
        /// </summary>
        public void PrepareForToolChange(ToolMode targetTool)
        {
            if (_selectionService.IsFloating && targetTool != ToolMode.Move)
            {
                _selectionInput.CommitIfActive();
            }

            _selectionInput.ResetControllerState();
            _toolInput.CancelInProgressDrawing();
        }

        // ── Private: tool selection ────────────────────────────────────────

        private void ExecuteSelectTool(string? toolName)
        {
            if (toolName == null) return;

            StopTextEditing();

            // Determine the target tool mode first to check if we're switching to Move.
            var targetTool = toolName switch
            {
                "Eraser" => ToolMode.Eraser,
                "Dither" => ToolMode.Dither,
                "Fill" => ToolMode.Fill,
                "Marquee" => ToolMode.Marquee,
                "EllipticalMarquee" => ToolMode.EllipticalMarquee,
                "Lasso" => ToolMode.Lasso,
                "Rectangle" => ToolMode.Rectangle,
                "Ellipse" => ToolMode.Ellipse,
                "FilledRectangle" => ToolMode.FilledRectangle,
                "FilledEllipse" => ToolMode.FilledEllipse,
                "Gradient" => ToolMode.Gradient,
                "Line" => ToolMode.Line,
                "Text" => ToolMode.Text,
                "Move" => ToolMode.Move,
                "MagicWand" => ToolMode.MagicWand,
                _ => ToolMode.Pencil,
            };

            // Use the shared preparation logic
            PrepareForToolChange(targetTool);

            CurrentTool = targetTool;

            ToolChanged?.Invoke(this, EventArgs.Empty);
        }

        // ── Private: history restore ──────────────────────────────────────

        private void RestoreState(SpriteState state)
        {
            if (state == null || ReferenceEquals(state, SpriteState)) return;

            // Preserve the current settings before assigning the restored state to SpriteState,
            // to ensure they are not reverted or wiped out by the restored state.
            var currentExportSettings = SpriteState.ExportSettings;
            var currentImageExportSettings = SpriteState.ImageExportSettings;

            bool canvasSizeChanged = SpriteState.Width != state.Width || SpriteState.Height != state.Height;
            if (canvasSizeChanged)
            {
                // Reallocate bitmaps and update layout-dependent parameters for the new dimensions
                // without creating a redundant temporary SpriteState (which would trigger double UI refreshes).
                RebuildBitmaps(state.Width, state.Height);

                // Initialize symmetry axes to canvas center
                SymmetryAxisX = state.Width / 2.0;
                SymmetryAxisY = state.Height / 2.0;

                AutoSelectPreviewDisplayTypeForCanvasSize(state.Width, state.Height);
                AutoSelectPreviewScaleForCanvasSize(state.Width, state.Height);
            }

            // Sync SelectedLayerIndices before assigning SpriteState to correctly initialize LayerItemViewModels
            SelectedLayerIndices.Clear();
            SelectedLayerIndices.Add(state.ActiveLayerIndex);

            SpriteState = state; // Triggers RebuildLayerViewModels()

            // Restore the preserved settings on the restored state
            SpriteState.ExportSettings = currentExportSettings;
            SpriteState.ImageExportSettings = currentImageExportSettings;
            
            NotifyLinkChanged();

            IsDisplayInverted = state.IsDisplayInverted; // restores the visual invert flag

            if (state.SelectionSnapshot != null)
                _selectionService.RestoreSnapshot(state.SelectionSnapshot);
            else
                _selectionService.Cancel();

            _selectionInput.ResetControllerState();
            _toolInput.CancelInProgressDrawing();
            
            UpdateOnionSkinCache();
            RedrawGridFromMemory();
            MarkCodeStale();
            if (canvasSizeChanged)
                NotifyCanvasLayoutChanged();
            UpdatePreviewSimulation(force: true);

            // Sync animation UI toggles
            IsAnimationEnabled = state.IsAnimationEnabled;
            FrameRateFps = state.FrameRateFps;
            PlaybackDirection = state.PlaybackDirection;
            OnPropertyChanged(nameof(EstimatedMemoryUsage));

            if (IsPlaying)
            {
                if (!IsAnimationEnabled || SpriteState.Frames.Count <= 1)
                {
                    IsPlaying = false;
                }
                else
                {
                    _playbackFrameIndex = Math.Clamp(_playbackFrameIndex, 0, SpriteState.Frames.Count - 1);
                    RebuildPlaybackCache();
                    UpdatePlaybackTimerIntervalForCurrentFrame();
                    for (int i = 0; i < Frames.Count; i++)
                        Frames[i].IsPlayingBack = (i == _playbackFrameIndex);
                    UpdatePlaybackPreview();
                }
            }

            DeleteFrameCommand?.NotifyCanExecuteChanged();
            AddFrameCommand?.NotifyCanExecuteChanged();
            DuplicateFrameCommand?.NotifyCanExecuteChanged();
            NextFrameCommand?.NotifyCanExecuteChanged();
            PreviousFrameCommand?.NotifyCanExecuteChanged();

            HistoryRestored?.Invoke(this, EventArgs.Empty);
        }

        private void AddLayer()
        {
            SpriteState.EnsureLayers();

            if (_selectionService.IsFloating)
            {
                _selectionInput.CommitIfActive();
            }

            if (_selectionService.HasActiveSelection)
            {
                _selectionService.Cancel();
            }

            _selectionInput.ResetControllerState();
            _toolInput.CancelInProgressDrawing();

            int activeIndex = SpriteState.ActiveLayerIndex;
            if (activeIndex < 0 || activeIndex > SpriteState.Layers.Count)
            {
                activeIndex = 0;
            }

            ApplyLayerMutation(() =>
            {
                int layerNumber = SpriteState.Layers.Count + 1;
                // Insert new layer above the current active layer
                int insertIndex = activeIndex;
                SpriteState.Layers.Insert(insertIndex, new LayerState
                {
                    Name = string.Create(CultureInfo.InvariantCulture, $"Layer {layerNumber}"),
                    IsVisible = true,
                });

                foreach (var frame in SpriteState.Frames)
                {
                    frame.LayerPixels.Insert(insertIndex, new Hexprite.Core.MonochromePixelBuffer(new bool[SpriteState.Width * SpriteState.Height]));
                }
                
                SpriteState.SetActiveLayer(insertIndex);
                SelectedLayerIndices.Clear();
                SelectedLayerIndices.Add(insertIndex);
            }); // Redraw must be true (default) so Focus Mode updates the opacity of the previous layer
        }

        private void DeleteSelectedLayers()
        {
            SpriteState.EnsureLayers();
            if (SpriteState.Layers.Count <= 1) return;

            // Prevent deletion if all layers are selected
            if (IsAllLayersSelected) return;

            // If the active layer is being deleted, discard its floating pixels
            if (SelectedLayerIndices.Contains(SpriteState.ActiveLayerIndex) && _selectionService.IsFloating)
            {
                _selectionService.Cancel();
            }

            // Sort indices in descending order to avoid index shifting when removing
            var sortedIndices = (SelectedLayerIndices.Count > 0
                ? SelectedLayerIndices.Distinct()
                : [SpriteState.ActiveLayerIndex])
                .Where(i => i >= 0 && i < SpriteState.Layers.Count)
                .OrderDescending()
                .ToList();
            if (sortedIndices.Count == 0) return;
            
            ApplyLayerMutation(() =>
            {
                foreach (var index in sortedIndices)
                {
                    if (index >= 0 && index < SpriteState.Layers.Count)
                    {
                        SpriteState.Layers.RemoveAt(index);
                        foreach (var frame in SpriteState.Frames)
                        {
                            frame.LayerPixels.RemoveAt(index);
                        }
                    }
                }

                // Find a new active layer - prefer the first visible layer
                int fallback = 0;
                if (SpriteState.Layers.Count > 0)
                {
                    // Try to find a visible layer
                    for (int i = 0; i < SpriteState.Layers.Count; i++)
                    {
                        if (SpriteState.Layers[i].IsVisible)
                        {
                            fallback = i;
                            break;
                        }
                    }
                }
                SpriteState.SetActiveLayer(fallback);
                
                SelectedLayerIndices.Clear();
                SelectedLayerIndices.Add(fallback);
            });
        }

        private void DuplicateActiveLayer()
        {
            SpriteState.EnsureLayers();

            int idx = SpriteState.ActiveLayerIndex;
            if (idx < 0 || idx >= SpriteState.Layers.Count) return;

            // Commit before duplicating so the clone doesn't have a hole
            if (_selectionService.IsFloating)
                _selectionInput.CommitIfActive();

            ApplyLayerMutation(() =>
            {
                var duplicated = SpriteState.Layers[idx].Clone();
                duplicated.Name = $"{duplicated.Name} Copy";
                SpriteState.Layers.Insert(idx, duplicated);
                foreach (var frame in SpriteState.Frames)
                {
                    if (frame.LayerPixels[idx] is Hexprite.Core.OverflowPixelBuffer ovf)
                    {
                        var dupExt = (bool[])ovf.GetExtendedData().Clone();
                        frame.LayerPixels.Insert(idx, new Hexprite.Core.OverflowPixelBuffer(dupExt, SpriteState.Width, SpriteState.Height, ovf.ExtendedWidth, ovf.ExtendedHeight, ovf.MarginX, ovf.MarginY));
                    }
                    else
                    {
                        frame.LayerPixels.Insert(idx, new Hexprite.Core.MonochromePixelBuffer((bool[])frame.LayerPixels[idx].GetMonochromeData().Clone()));
                    }
                }
                SpriteState.SetActiveLayer(idx);

                SelectedLayerIndices.Clear();
                SelectedLayerIndices.Add(idx);
            });
        }

        private void CreateNewLayerFromSelection()
        {
            SpriteState.EnsureLayers();
            if (!_selectionService.HasActiveSelection || SpriteState.Width <= 0 || SpriteState.Height <= 0) return;
            if (!_selectionService.HasAnyPixelInSelection(SpriteState))
            {
                ShowStatus("Selected area on active layer is empty.", 3000);
                return;
            }

            var data = _selectionService.CopySelection(SpriteState);
            if (data == null) return;

            int selMinX = _selectionService.MinX;
            int selMinY = _selectionService.MinY;

            SaveStateForUndo();
            _selectionInput.CommitIfActive(saveHistory: false);

            int activeIndex = SpriteState.ActiveLayerIndex;
            if (activeIndex < 0 || activeIndex > SpriteState.Layers.Count)
                activeIndex = 0;

            string baseName = (SpriteState.Layers != null && activeIndex >= 0 && activeIndex < SpriteState.Layers.Count)
                ? SpriteState.Layers[activeIndex].Name
                : "Layer";
            string newLayerName = $"{baseName} Selection";

            ApplyLayerMutation(() =>
            {
                int insertIndex = activeIndex;
                SpriteState.Layers.Insert(insertIndex, new LayerState
                {
                    Name = newLayerName,
                    IsVisible = true,
                });

                bool hasOverflow = selMinX < 0 || _selectionService.MaxX >= SpriteState.Width ||
                                   selMinY < 0 || _selectionService.MaxY >= SpriteState.Height;

                int activeFrameIdx = SpriteState.ActiveFrameIndex;
                for (int f = 0; f < SpriteState.Frames.Count; f++)
                {
                    var frame = SpriteState.Frames[f];
                    if (f == activeFrameIdx)
                    {
                        IPixelBuffer buffer;
                        if (hasOverflow || (SpriteState.ActivePixelBuffer is Hexprite.Core.OverflowPixelBuffer))
                        {
                            var ovf = new Hexprite.Core.OverflowPixelBuffer(SpriteState.Width, SpriteState.Height);
                            for (int fy = 0; fy < data.Height; fy++)
                            {
                                for (int fx = 0; fx < data.Width; fx++)
                                {
                                    if (data.Pixels[fx, fy])
                                    {
                                        ovf.SetPixelNoInvalidate(selMinX + fx, selMinY + fy, true);
                                    }
                                }
                            }
                            ovf.InvalidateViewCache();
                            buffer = ovf;
                        }
                        else
                        {
                            var mono = new bool[SpriteState.Width * SpriteState.Height];
                            for (int fy = 0; fy < data.Height; fy++)
                            {
                                int gy = selMinY + fy;
                                if (gy < 0 || gy >= SpriteState.Height) continue;
                                for (int fx = 0; fx < data.Width; fx++)
                                {
                                    int gx = selMinX + fx;
                                    if (gx < 0 || gx >= SpriteState.Width) continue;
                                    if (data.Pixels[fx, fy])
                                    {
                                        mono[gy * SpriteState.Width + gx] = true;
                                    }
                                }
                            }
                            buffer = new Hexprite.Core.MonochromePixelBuffer(mono);
                        }
                        frame.LayerPixels.Insert(insertIndex, buffer);
                    }
                    else
                    {
                        frame.LayerPixels.Insert(insertIndex, new Hexprite.Core.MonochromePixelBuffer(new bool[SpriteState.Width * SpriteState.Height]));
                    }
                }

                SpriteState.SetActiveLayer(insertIndex);
                SelectedLayerIndices.Clear();
                SelectedLayerIndices.Add(insertIndex);
            });

            ShowStatus($"Created layer '{newLayerName}' from selection");
        }

        private void MergeLayers()
        {
            SpriteState.EnsureLayers();
            if (SpriteState.Layers.Count <= 1) return;

            var indicesToMerge = SelectedLayerIndices
                .Where(i => i >= 0 && i < SpriteState.Layers.Count)
                .Distinct()
                .Order()
                .ToList();

            // If only one layer is selected, treat as "Merge Down"
            if (indicesToMerge.Count == 1)
            {
                int topIdx = indicesToMerge[0];
                if (topIdx >= SpriteState.Layers.Count - 1) return; // Cannot merge down from bottom
                indicesToMerge.Add(topIdx + 1);
            }

            if (indicesToMerge.Count <= 1) return;

            ApplyLayerMutation(() =>
            {
                // Commit floating selection before structural changes to layers
                if (_selectionService.IsFloating)
                {
                    _selectionInput.CommitIfActive(saveHistory: false);
                }

                // Target is the bottom-most layer in the selection (largest index)
                int targetIdx = indicesToMerge.Max();
                var targetLayer = SpriteState.Layers[targetIdx];
                // A merge composites frame-specific content, so the result is kept
                // local and never aliases every animation frame.

                bool anyVisible = false;

                // Sort indices descending to process from bottom to top (target is processed first)
                var sortedIndices = indicesToMerge.OrderDescending().ToList();

                // Find the maximum extended boundaries across all layers being merged
                int minExtX = 0, minExtY = 0, maxExtX = SpriteState.Width - 1, maxExtY = SpriteState.Height - 1;
                bool anyPreserve = false;
                foreach (int idx in indicesToMerge)
                {
                    if (SpriteState.Layers[idx].PreserveOverflow)
                        anyPreserve = true;
                    for (int f = 0; f < SpriteState.Frames.Count; f++)
                    {
                        if (SpriteState.Frames[f].LayerPixels[idx] is Hexprite.Core.OverflowPixelBuffer ovf)
                        {
                            int lX = -ovf.MarginX;
                            int lY = -ovf.MarginY;
                            int rX = ovf.ExtendedWidth - ovf.MarginX - 1;
                            int bY = ovf.ExtendedHeight - ovf.MarginY - 1;
                            if (lX < minExtX) minExtX = lX;
                            if (lY < minExtY) minExtY = lY;
                            if (rX > maxExtX) maxExtX = rX;
                            if (bY > maxExtY) maxExtY = bY;
                        }
                    }
                }

                int newExtW = maxExtX - minExtX + 1;
                int newExtH = maxExtY - minExtY + 1;
                int newMarginX = -minExtX;
                int newMarginY = -minExtY;

                for (int f = 0; f < SpriteState.Frames.Count; f++)
                {
                    var compositeExt = new bool[newExtW * newExtH];

                    for (int i = 0; i < sortedIndices.Count; i++)
                    {
                        int srcIdx = sortedIndices[i];
                        var srcLayer = SpriteState.Layers[srcIdx];
                        var srcBuffer = SpriteState.Frames[f].LayerPixels[srcIdx];
                        
                        if (srcBuffer is Hexprite.Core.OverflowPixelBuffer ovf)
                        {
                            var sPixels = ovf.GetExtendedData();
                            for (int y = 0; y < ovf.ExtendedHeight; y++)
                            {
                                for (int x = 0; x < ovf.ExtendedWidth; x++)
                                {
                                    int canvasX = x - ovf.MarginX;
                                    int canvasY = y - ovf.MarginY;
                                    int dstX = canvasX + newMarginX;
                                    int dstY = canvasY + newMarginY;
                                    int dstP = dstY * newExtW + dstX;
                                    int srcP = y * ovf.ExtendedWidth + x;
                                    
                                    int ditherIndex = (canvasY % SpriteState.Height) * SpriteState.Width + (canvasX % SpriteState.Width);
                                    if (ditherIndex < 0) ditherIndex = -ditherIndex;
                                    
                                    bool layerOn = SpriteState.IsPixelOnWithOpacity(sPixels[srcP], ditherIndex, srcLayer.OpacityMode);
                                    
                                    if (i == 0)
                                        compositeExt[dstP] = layerOn;
                                    else
                                        SpriteState.ApplyBlendMode(compositeExt, dstP, layerOn, srcLayer.BlendMode);
                                }
                            }
                        }
                        else
                        {
                            var sPixels = srcBuffer.GetMonochromeData();
                            for (int y = 0; y < SpriteState.Height; y++)
                            {
                                for (int x = 0; x < SpriteState.Width; x++)
                                {
                                    int dstX = x + newMarginX;
                                    int dstY = y + newMarginY;
                                    int dstP = dstY * newExtW + dstX;
                                    int srcP = y * SpriteState.Width + x;
                                    
                                    bool layerOn = SpriteState.IsPixelOnWithOpacity(sPixels[srcP], srcP, srcLayer.OpacityMode);
                                    
                                    if (i == 0)
                                        compositeExt[dstP] = layerOn;
                                    else
                                        SpriteState.ApplyBlendMode(compositeExt, dstP, layerOn, srcLayer.BlendMode);
                                }
                            }
                        }
                    }

                    if (anyPreserve)
                    {
                        SpriteState.Frames[f].LayerPixels[targetIdx] = new Hexprite.Core.OverflowPixelBuffer(compositeExt, SpriteState.Width, SpriteState.Height, newExtW, newExtH, newMarginX, newMarginY);
                    }
                    else
                    {
                        var canvasOnly = new bool[SpriteState.Width * SpriteState.Height];
                        for (int y = 0; y < SpriteState.Height; y++)
                        {
                            for (int x = 0; x < SpriteState.Width; x++)
                            {
                                int srcP = (y + newMarginY) * newExtW + (x + newMarginX);
                                int dstP = y * SpriteState.Width + x;
                                canvasOnly[dstP] = compositeExt[srcP];
                            }
                        }
                        SpriteState.Frames[f].LayerPixels[targetIdx] = new Hexprite.Core.MonochromePixelBuffer(canvasOnly);
                    }
                }

                // Check visibility of all merged layers
                foreach (int idx in indicesToMerge)
                {
                    if (SpriteState.Layers[idx].IsVisible)
                    {
                        anyVisible = true;
                        break;
                    }
                }

                targetLayer.IsVisible = anyVisible;
                targetLayer.OpacityMode = LayerOpacityMode.Solid;
                targetLayer.PreserveOverflow = anyPreserve;
                targetLayer.IsGlobal = false;
                targetLayer.PreGlobalFramePixels = null;
                // Target layer's blend mode is preserved

                // Remove all merged layers EXCEPT the target layer.
                // Remove from bottom to top to avoid shifting indices affecting removal.
                var toRemove = indicesToMerge.Where(i => i != targetIdx).OrderDescending().ToList();
                foreach (int idx in toRemove)
                {
                    SpriteState.Layers.RemoveAt(idx);
                    foreach (var frame in SpriteState.Frames)
                    {
                        frame.LayerPixels.RemoveAt(idx);
                    }
                }

                int newTargetIdx = targetIdx - toRemove.Count;
                SpriteState.SetActiveLayer(newTargetIdx);
                
                SelectedLayerIndices.Clear();
                SelectedLayerIndices.Add(newTargetIdx);
            });
        }

        private void MoveActiveLayerUp()
        {
            SpriteState.EnsureLayers();
            if (SpriteState.ActiveLayerIndex <= 0 || SpriteState.ActiveLayerIndex >= SpriteState.Layers.Count) return;
            MoveLayer(SpriteState.ActiveLayerIndex, SpriteState.ActiveLayerIndex - 1);
        }

        private void MoveActiveLayerDown()
        {
            SpriteState.EnsureLayers();
            if (SpriteState.ActiveLayerIndex < 0 || SpriteState.ActiveLayerIndex >= SpriteState.Layers.Count - 1) return;
            MoveLayer(SpriteState.ActiveLayerIndex, SpriteState.ActiveLayerIndex + 1);
        }

        private void ApplyLayerMutation(Action mutation, bool redraw = true, bool rebuildLayerList = true, bool markCodeStale = true)
        {
            SaveStateForUndo();
            mutation();
            SpriteState.NormalizeLayerState();
            if (rebuildLayerList)
                RebuildLayerViewModels();
            else
                SyncLayerActiveFlags();
            if (redraw)
                RedrawGridFromMemory();
            if (markCodeStale)
                MarkCodeStale();

            RefreshFrameThumbnails();
            if (_isPlaying)
            {
                RebuildPlaybackCache();
            }
        }

        private void RebuildLayerViewModels()
        {
            if (SpriteState == null) return;
            _suppressSelectedLayerBindingToState = true;
            try
            {
                int modelCount = SpriteState.Layers.Count;

                // 1. Update existing items in-place (no container churn)
                int overlap = Math.Min(Layers.Count, modelCount);
                for (int i = 0; i < overlap; i++)
                {
                    SyncLayerViewModel(Layers[i], i);
                }

                // 2. Append new items (model grew)
                for (int i = overlap; i < modelCount; i++)
                {
                    var vm = new LayerItemViewModel();
                    SyncLayerViewModel(vm, i);
                    Layers.Add(vm);
                }

                // 3. Trim excess items (model shrank) — remove from end
                for (int i = Layers.Count - 1; i >= modelCount; i--)
                {
                    Layers.RemoveAt(i);
                }

                _selectedLayerIndex = SpriteState.ActiveLayerIndex;
                OnPropertyChanged(nameof(SelectedLayerIndex));
            }
            finally
            {
                _suppressSelectedLayerBindingToState = false;
            }

            SyncLayerSelectedFlags();
            OnPropertyChanged(nameof(IsActiveLayerLocked));
            OnPropertyChanged(nameof(IsActiveLayerVisible));
        }

        private void SyncLayerViewModel(LayerItemViewModel vm, int index)
        {
            var layer = SpriteState.Layers[index];
            vm.Name = layer.Name;
            vm.IsVisible = layer.IsVisible;
            vm.IsLocked = layer.IsLocked;
            vm.IsGlobal = layer.IsGlobal;
            vm.ExcludeFromExport = layer.ExcludeFromExport;
            vm.PreserveOverflow = layer.PreserveOverflow;
            vm.BlendMode = layer.BlendMode;
            vm.OpacityMode = layer.OpacityMode;
            vm.IsActive = index == SpriteState.ActiveLayerIndex;
            vm.IsSelected = SelectedLayerIndices.Contains(index);

            bool hasContent = false;
            if (SpriteState.ActiveFrameIndex >= 0 && SpriteState.ActiveFrameIndex < SpriteState.Frames.Count)
            {
                var frame = SpriteState.Frames[SpriteState.ActiveFrameIndex];
                if (index >= 0 && index < frame.LayerPixels.Count)
                {
                    hasContent = Array.IndexOf(frame.LayerPixels[index].GetMonochromeData(), value: true) >= 0;
                }
            }
            vm.HasContent = hasContent;
            vm.IsRenaming = false;
        }

        private void SyncFrameViewModel(FrameItemViewModel vm, int index,
            HashSet<FrameState> selectedFrames, HashSet<int> selectedIndices,
            bool matchedFrameIdentity, bool hasExplicitSelection)
        {
            var frame = SpriteState.Frames[index];
            vm.Name = frame.Name;
            vm.IsActive = index == SpriteState.ActiveFrameIndex;
            vm.IsSelected = matchedFrameIdentity
                ? selectedFrames.Contains(frame)
                : !hasExplicitSelection && selectedIndices.Contains(index);
            vm.DelayMultiplier = frame.DelayMultiplier;
            vm.IsPlayingBack = _isPlaying && (index == _playbackFrameIndex);

            var thumbnail = GenerateFrameThumbnail(index, out bool hasContent);
            vm.Thumbnail = thumbnail;
            vm.HasContent = hasContent;
        }

        public void RebuildFrameViewModels(IEnumerable<FrameState>? selectedFrameStates = null)
        {
            if (SpriteState == null) return;
            bool hasExplicitSelection = selectedFrameStates != null;
            var selectedFrames = hasExplicitSelection
                ? [.. selectedFrameStates]
                : Frames
                    .Select((viewModel, index) => new { viewModel, index })
                    .Where(x => x.viewModel.IsSelected && x.index < SpriteState.Frames.Count)
                    .Select(x => SpriteState.Frames[x.index])
                    .ToHashSet();
            var selectedIndices = Frames
                .Select((viewModel, index) => new { viewModel, index })
                .Where(x => x.viewModel.IsSelected)
                .Select(x => x.index)
                .ToHashSet();
            bool matchedFrameIdentity = selectedFrames.Count > 0 && SpriteState.Frames.Exists(selectedFrames.Contains);
            _suppressSelectedFrameBindingToState = true;
            try
            {
                int modelCount = SpriteState.Frames.Count;

                // 1. Update existing items in-place (no container churn)
                int overlap = Math.Min(Frames.Count, modelCount);
                for (int i = 0; i < overlap; i++)
                {
                    SyncFrameViewModel(Frames[i], i, selectedFrames, selectedIndices, matchedFrameIdentity, hasExplicitSelection);
                }

                // 2. Append new items (model grew)
                for (int i = overlap; i < modelCount; i++)
                {
                    var vm = new FrameItemViewModel();
                    SyncFrameViewModel(vm, i, selectedFrames, selectedIndices, matchedFrameIdentity, hasExplicitSelection);
                    Frames.Add(vm);
                }

                // 3. Trim excess items (model shrank) — remove from end
                for (int i = Frames.Count - 1; i >= modelCount; i--)
                {
                    Frames.RemoveAt(i);
                }

                _selectedFrameIndex = SpriteState.ActiveFrameIndex;
                OnPropertyChanged(nameof(SelectedFrameIndex));
                OnPropertyChanged(nameof(CurrentFrameDisplay));
                if (!Frames.Any(frame => frame.IsSelected) && Frames.Count > 0 && SpriteState.ActiveFrameIndex >= 0 && SpriteState.ActiveFrameIndex < Frames.Count)
                    Frames[SpriteState.ActiveFrameIndex].IsSelected = true;
            }
            finally
            {
                _suppressSelectedFrameBindingToState = false;
            }
            if (_isPlaying)
            {
                RebuildPlaybackCache();
            }
            NotifyFrameSelectionChanged();
        }

        private void RefreshFrameThumbnails(IEnumerable<int> frameIndices)
        {
            if (SpriteState == null) return;
            foreach (int index in frameIndices.Distinct())
            {
                if (index < 0 || index >= Frames.Count) continue;
                Frames[index].Thumbnail = GenerateFrameThumbnail(index, out bool hasContent);
                Frames[index].HasContent = hasContent;
            }
        }

        /// <summary>
        /// Regenerates all frame thumbnail bitmaps using the current theme colors.
        /// </summary>
        private void RefreshFrameThumbnails()
        {
            if (Frames == null || Frames.Count == 0 || SpriteState == null) return;

            for (int i = 0; i < Frames.Count; i++)
            {
                if (i >= SpriteState.Frames.Count) break;
                var thumbnail = GenerateFrameThumbnail(i, out bool hasContent);
                Frames[i].Thumbnail = thumbnail;
                Frames[i].HasContent = hasContent;
            }
        }

        private System.Windows.Media.Imaging.BitmapSource? GenerateFrameThumbnail(int frameIndex, out bool hasContent)
        {
            hasContent = false;
            if (SpriteState == null) return null;

            int w = SpriteState.Width;
            int h = SpriteState.Height;
            bool[] composite = SpriteState.CompositeFramePixels(frameIndex);

            // Bgra32 byte array
            byte[] bgra = new byte[w * h * 4];

            // Extract bytes from our canvas UI colors
            byte fgB = (byte)(_colorOnUint);
            byte fgG = (byte)(_colorOnUint >> 8);
            byte fgR = (byte)(_colorOnUint >> 16);
            byte fgA = (byte)(_colorOnUint >> 24);

            // Extract bytes for the inactive (background) pixels
            byte bgB = (byte)(_colorOffUint);
            byte bgG = (byte)(_colorOffUint >> 8);
            byte bgR = (byte)(_colorOffUint >> 16);
            byte bgA = (byte)(_colorOffUint >> 24);

            for (int i = 0; i < composite.Length; i++)
            {
                int idx = i * 4;
                if (composite[i])
                {
                    hasContent = true;
                    bgra[idx] = fgB;
                    bgra[idx + 1] = fgG;
                    bgra[idx + 2] = fgR;
                    bgra[idx + 3] = fgA;
                }
                else
                {
                    bgra[idx] = bgB;
                    bgra[idx + 1] = bgG;
                    bgra[idx + 2] = bgR;
                    bgra[idx + 3] = bgA;
                }
            }

            var src = System.Windows.Media.Imaging.BitmapSource.Create(
                w, h, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32,
                palette: null,
                bgra,
                w * 4);
            src.Freeze();
            return src;
        }

        public void UpdateActiveFrameThumbnail()
        {
            if (Frames == null || SpriteState?.Frames == null ||
                SpriteState.ActiveFrameIndex < 0 ||
                SpriteState.ActiveFrameIndex >= Frames.Count ||
                SpriteState.ActiveFrameIndex >= SpriteState.Frames.Count)
                return;

            int activeLayerIndex = SpriteState.ActiveLayerIndex;
            bool activeLayerIsGlobal = activeLayerIndex >= 0 &&
                activeLayerIndex < SpriteState.Layers.Count &&
                SpriteState.Layers[activeLayerIndex].IsGlobal;

            if (activeLayerIsGlobal)
            {
                RefreshFrameThumbnails(Enumerable.Range(0, SpriteState.Frames.Count));
                return;
            }

            System.Windows.Media.ImageSource? thumbnail = GenerateFrameThumbnail(SpriteState.ActiveFrameIndex, out bool frameHasContent);
            Frames[SpriteState.ActiveFrameIndex].HasContent = frameHasContent;
            Frames[SpriteState.ActiveFrameIndex].Thumbnail = thumbnail;
        }

        private void SyncLayerActiveFlags()
        {
            for (int i = 0; i < Layers.Count; i++)
                Layers[i].IsActive = i == SpriteState.ActiveLayerIndex;
            OnPropertyChanged(nameof(IsActiveLayerLocked));
            OnPropertyChanged(nameof(IsActiveLayerVisible));
            OnPropertyChanged(nameof(CanModifyActiveLayer));
            NotifyLayerModificationCommandsChanged();
        }

        private void SyncFrameActiveFlags()
        {
            for (int i = 0; i < Frames.Count; i++)
            {
                Frames[i].IsActive = i == SpriteState.ActiveFrameIndex;
            }
        }

        public bool CanMoveActiveLayerUp =>
            SpriteState != null &&
            SpriteState.Layers.Count > 1 &&
            SpriteState.ActiveLayerIndex > 0;

        public bool CanMoveActiveLayerDown =>
            SpriteState != null &&
            SpriteState.Layers.Count > 1 &&
            SpriteState.ActiveLayerIndex >= 0 &&
            SpriteState.ActiveLayerIndex < SpriteState.Layers.Count - 1;

        public bool CanDeleteSelectedLayers =>
            SpriteState != null &&
            SpriteState.Layers.Count > 1 &&
            !IsAllLayersSelected &&
            (SelectedLayerIndices.Count > 0 || (SpriteState.ActiveLayerIndex >= 0 && SpriteState.ActiveLayerIndex < SpriteState.Layers.Count));

        public bool CanDuplicateActiveLayer =>
            SpriteState != null &&
            SpriteState.Layers.Count > 0 &&
            SpriteState.ActiveLayerIndex >= 0 &&
            SpriteState.ActiveLayerIndex < SpriteState.Layers.Count;

        public bool CanMergeLayers
        {
            get
            {
                if (SpriteState == null || SpriteState.Layers.Count <= 1) return false;
                if (SelectedLayerIndices.Count > 1) return true;
                int active = SelectedLayerIndices.Count == 1 ? SelectedLayerIndices[0] : SpriteState.ActiveLayerIndex;
                return active >= 0 && active < SpriteState.Layers.Count - 1;
            }
        }

        /// <summary>
        /// Notifies all commands whose CanExecute depends on layer modification state.
        /// </summary>
        private void NotifyLayerModificationCommandsChanged()
        {
            ClearCommand?.NotifyCanExecuteChanged();
            InvertCommand?.NotifyCanExecuteChanged();
            CutSelectionCommand?.NotifyCanExecuteChanged();
            PasteCommand?.NotifyCanExecuteChanged();
            DeleteSelectionCommand?.NotifyCanExecuteChanged();
            FlipSelectionHorizontalCommand?.NotifyCanExecuteChanged();
            FlipSelectionVerticalCommand?.NotifyCanExecuteChanged();
            BeginSelectionTransformCommand?.NotifyCanExecuteChanged();
            ReselectCommand?.NotifyCanExecuteChanged();
            NewLayerFromSelectionCommand?.NotifyCanExecuteChanged();
            BatchFrameOperationCommand?.NotifyCanExecuteChanged();
            NotifyLayerPanelCommandsChanged();
        }

        public void NotifyLayerPanelCommandsChanged()
        {
            DeleteLayerCommand?.NotifyCanExecuteChanged();
            DuplicateLayerCommand?.NotifyCanExecuteChanged();
            MergeLayerCommand?.NotifyCanExecuteChanged();
            MoveLayerUpCommand?.NotifyCanExecuteChanged();
            MoveLayerDownCommand?.NotifyCanExecuteChanged();
        }

        // ── Private: multi-layer selection ─────────────────────────────────

        /// <summary>
        /// Sets a single layer as selected (clears any multi-selection).
        /// Called on normal layer click.
        /// </summary>
        public void SetSingleLayerSelection(int index)
        {
            SelectedLayerIndices.Clear();
            if (SpriteState != null && index >= 0 && index < SpriteState.Layers.Count)
            {
                SelectedLayerIndices.Add(index);
                if (SpriteState.ActiveLayerIndex != index)
                {
                    SetActiveLayer(index, shouldRedraw: true);
                }
            }
            
            // Update SelectedLayerIndex to keep ListBox in sync
            if (_selectedLayerIndex != index)
            {
                _selectedLayerIndex = index;
                OnPropertyChanged(nameof(SelectedLayerIndex));
            }
            
            SyncLayerSelectedFlags();
        }

        /// <summary>
        /// Adds/removes a layer from the current multi-selection.
        /// Called on Shift+click or Ctrl+click for individual multi-select.
        /// </summary>
        public void ToggleLayerSelection(int index)
        {
            if (SpriteState == null || index < 0 || index >= SpriteState.Layers.Count) return;

            if (SelectedLayerIndices.Contains(index))
            {
                // Don't allow deselecting if it's the only selected layer
                // We always need at least one active layer for drawing
                if (SelectedLayerIndices.Count <= 1)
                    return;

                // Remove from selection
                SelectedLayerIndices.Remove(index);

                // If this was the active layer, activate another selected layer
                if (SpriteState.ActiveLayerIndex == index && SelectedLayerIndices.Count > 0)
                {
                    int newActiveIndex = SelectedLayerIndices[0];
                    SetActiveLayer(newActiveIndex, shouldRedraw: true);
                    _selectedLayerIndex = newActiveIndex;
                    OnPropertyChanged(nameof(SelectedLayerIndex));
                }
            }
            else
            {
                // Add to selection and make it the active layer
                SelectedLayerIndices.Add(index);
                SetActiveLayer(index, shouldRedraw: true);
                _selectedLayerIndex = index;
                OnPropertyChanged(nameof(SelectedLayerIndex));
            }

            SyncLayerSelectedFlags();
        }

        /// <summary>
        /// Updates the IsSelected property on all layer view models to match SelectedLayerIndices.
        /// </summary>
        private void SyncLayerSelectedFlags()
        {
            for (int i = 0; i < Layers.Count; i++)
                Layers[i].IsSelected = SelectedLayerIndices.Contains(i);

            OnPropertyChanged(nameof(IsMultipleLayersSelected));
            OnPropertyChanged(nameof(IsAllLayersSelected));
            OnPropertyChanged(nameof(CanModifyActiveLayer));
            NotifyLayerModificationCommandsChanged();
        }

        // ── Private: color initialization ─────────────────────────────────

        /// <summary>
        /// Reads the canvas pixel colors from the current theme resources.
        /// Called once at construction and again whenever the theme changes.
        /// </summary>
        public void InitializeBrushColors()
        {
            Color colorOff = Colors.Black;
            Color colorOn = Colors.White;
            Color prevOff = Colors.Black;
            Color prevOn = Colors.White;

            if (Application.Current != null && Application.Current.Dispatcher != null)
            {
                if (Application.Current.Dispatcher.CheckAccess())
                {
                    try
                    {
                        var res = Application.Current.Resources;
                        if (res["Brush.Canvas.Pixel"] is SolidColorBrush b1) colorOff = b1.Color;
                        if (res["Brush.Canvas.Drawing"] is SolidColorBrush b2) colorOn = b2.Color;
                        if (res["Brush.Preview.Base"] is SolidColorBrush b3) prevOff = b3.Color;
                        if (res["Brush.Preview.Drawing"] is SolidColorBrush b4) prevOn = b4.Color;
                    }
                    catch (InvalidOperationException)
                    {
                        // Fallback when called from non-UI test runner threads
                    }
                }
                else
                {
                    try
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                var res = Application.Current.Resources;
                                if (res["Brush.Canvas.Pixel"] is SolidColorBrush b1) colorOff = b1.Color;
                                if (res["Brush.Canvas.Drawing"] is SolidColorBrush b2) colorOn = b2.Color;
                                if (res["Brush.Preview.Base"] is SolidColorBrush b3) prevOff = b3.Color;
                                if (res["Brush.Preview.Drawing"] is SolidColorBrush b4) prevOn = b4.Color;
                            }
                            catch
                            {
                                // Fallback when resource lookup fails on Dispatcher
                            }
                        });
                    }
                    catch
                    {
                        // Fallback when Dispatcher invoke fails
                    }
                }
            }

            _colorOffUint = ToBgra32(colorOff);
            _colorOnUint = ToBgra32(colorOn);
            _previewOffUint = ToBgra32(prevOff);
            _previewOnUint = ToBgra32(prevOn);

            // Onion skin colors: semi-transparent red for previous, green for next
            _onionSkinPrevUint = ToBgra32(Color.FromArgb(100, 255, 50, 50)); // Red for previous
            _onionSkinNextUint = ToBgra32(Color.FromArgb(100, 50, 255, 50)); // Green for next
            _onionSkinBothUint = ToBgra32(Color.FromArgb(100, 200, 200, 50)); // Yellowish overlay for both
        }

        /// <summary>
        /// Re-reads theme colors and redraws the canvas. Call after a theme switch.
        /// </summary>
        public void RefreshCanvasColors()
        {
            UpdatePreviewColors();
            InitializeBrushColors();
            RedrawGridFromMemory();
            RefreshFrameThumbnails();
        }

        private void UpdatePreviewColors()
        {
            if (Application.Current == null) return;
            try
            {
                var res = Application.Current.Resources;
                string bgKey, fgKey;

                switch (_previewDisplayType)
                {
                    case DisplayType.SSD1306Blue:
                        bgKey = "Palette.Preview.OLED.Blue.BG";
                        fgKey = "Palette.Preview.OLED.Blue.FG";
                        break;
                    case DisplayType.SSD1306Green:
                        bgKey = "Palette.Preview.OLED.Green.BG";
                        fgKey = "Palette.Preview.OLED.Green.FG";
                        break;
                    case DisplayType.ePaper:
                        bgKey = "Palette.Preview.EPaper.BG";
                        fgKey = "Palette.Preview.EPaper.FG";
                        break;
                    case DisplayType.FlipperZero:
                        bgKey = "Palette.Preview.FlipperZero.BG";
                        fgKey = "Palette.Preview.FlipperZero.FG";
                        break;
                    case DisplayType.GenericWhite:
                    default:
                        // Keep Generic preview theme-independent so it remains visually
                        // consistent when the app theme changes.
                        res["Brush.Preview.Base"] = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00));
                        res["Brush.Preview.Drawing"] = new SolidColorBrush(Color.FromRgb(0xF0, 0xF6, 0xFC));
                        return;
                }

                if (res.Contains(bgKey) && res.Contains(fgKey))
                {
                    var bg = (Color)res[bgKey];
                    var fg = (Color)res[fgKey];
                    res["Brush.Preview.Base"] = new SolidColorBrush(bg);
                    res["Brush.Preview.Drawing"] = new SolidColorBrush(fg);
                }
            }
            catch (InvalidOperationException)
            {
                // Fallback when called from non-UI test runner threads
            }
        }

        // ── Private: bitmap lifecycle helpers ─────────────────────────────

        /// <summary>
        /// Allocates fresh WritableBitmaps and pixel buffers for the given dimensions.
        /// Called by both <see cref="InitializeGrid"/> and <see cref="ResizeCanvas"/>
        /// to avoid duplicating this initialization logic.
        /// </summary>
        private void RebuildBitmaps(int width, int height)
        {
            CanvasBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, palette: null);
            PreviewBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, palette: null);
            _canvasBuffer = new uint[width * height];
            _previewBuffer = new uint[width * height];
            _hardwareBuffer = new bool[width * height];
            EnsurePreviewSimBitmap();
        }

        /// <summary>
        /// Raises PropertyChanged for all layout-dependent computed properties.
        /// Called after canvas dimensions change to keep bindings in sync.
        /// </summary>
        private void NotifyCanvasLayoutChanged()
        {
            OnPropertyChanged(nameof(GridViewport));
            OnPropertyChanged(nameof(PreviewWidth));
            OnPropertyChanged(nameof(PreviewHeight));
            OnPropertyChanged(nameof(PreviewEffectiveScale));
            OnPropertyChanged(nameof(IsPreviewScaleCapped));
            OnPropertyChanged(nameof(MaxUsefulPreviewScale));
            OnPropertyChanged(nameof(CanIncreasePreviewScale));
            OnPropertyChanged(nameof(CanDecreasePreviewScale));
            OnPropertyChanged(nameof(PreviewScaleStatusText));
            OnPropertyChanged(nameof(CanvasDisplayWidth));
            OnPropertyChanged(nameof(CanvasDisplayHeight));
            OnPropertyChanged(nameof(DynamicStrokeThickness));
            OnPropertyChanged(nameof(SelectionStrokeThickness));
            OnPropertyChanged(nameof(PreviewScaleText));
            OnPropertyChanged(nameof(VerticalSymmetryLinePosition));
            OnPropertyChanged(nameof(HorizontalSymmetryLinePosition));
            OnPropertyChanged(nameof(CanvasDimensionText));
            OnPropertyChanged(nameof(EstimatedMemoryUsage));
            NotifyPreviewFramePropertiesChanged();
        }

        private void ApplySavedEditorPreferences(bool applyToolPreferences)
        {
            var prefs = UserPreferencesService.Get();
            // First document always defaults to Pencil for a consistent starting experience.
            // Subsequent documents inherit the current global tool.
            if (applyToolPreferences)
                _setCurrentTool(ToolMode.Pencil);
            _showGridLines = prefs.ShowGridLines;
            _isBrushCursorVisible = prefs.IsBrushCursorVisible;
            
            _toolSettingsMap.Clear();
            if (prefs.ToolSettings != null)
            {
                foreach (var kvp in prefs.ToolSettings)
                {
                    _toolSettingsMap[kvp.Key] = kvp.Value.Clone();
                }
            }
            
            _isOnionSkinPrevEnabled = prefs.IsOnionSkinPrevEnabled;
            _isOnionSkinNextEnabled = prefs.IsOnionSkinNextEnabled;
            _isSymmetryHorizontalEnabled = prefs.IsSymmetryHorizontalEnabled;
            _isSymmetryVerticalEnabled = prefs.IsSymmetryVerticalEnabled;
            _previewScale = Math.Max(1, prefs.PreviewScale);
            _previewDisplayType = (DisplayType)Math.Clamp(prefs.PreviewDisplayTypeIndex, 0, 4);
            _useRealisticPreview = prefs.UseRealisticPreview;
            _previewRealismStrength = Math.Clamp(prefs.PreviewRealismStrength, 0, 100);
            _previewQuality = (PreviewQuality)Math.Clamp(prefs.PreviewQuality, 0, 2);
            UpdatePreviewColors();
        }

        private void SaveEditorPreferences()
        {
            UserPreferencesService.Update(p =>
            {
                // Note: Tool is NOT persisted - always defaults to Pencil on restart
                p.ShowGridLines = _showGridLines;
                p.IsBrushCursorVisible = _isBrushCursorVisible;
                
                foreach (var kvp in _toolSettingsMap)
                {
                    if (!p.ToolSettings.ContainsKey(kvp.Key))
                        p.ToolSettings[kvp.Key] = new PerToolSettings();
                    
                    var ts = p.ToolSettings[kvp.Key];
                    ts.BrushSize = kvp.Value.BrushSize;
                    ts.BrushShape = kvp.Value.BrushShape;
                    ts.BrushAngle = kvp.Value.BrushAngle;
                    ts.IsPixelPerfectEnabled = kvp.Value.IsPixelPerfectEnabled;
                    ts.IsContiguousFillEnabled = kvp.Value.IsContiguousFillEnabled;
                }

                p.IsOnionSkinPrevEnabled = _isOnionSkinPrevEnabled;
                p.IsOnionSkinNextEnabled = _isOnionSkinNextEnabled;
                p.IsSymmetryHorizontalEnabled = _isSymmetryHorizontalEnabled;
                p.IsSymmetryVerticalEnabled = _isSymmetryVerticalEnabled;
                p.PreviewScale = _previewScale;
                p.PreviewDisplayTypeIndex = (int)_previewDisplayType;
                p.UseRealisticPreview = _useRealisticPreview;
                p.PreviewRealismStrength = _previewRealismStrength;
                p.PreviewQuality = (int)_previewQuality;
            });
        }

        internal DisplaySimulationPreset GetDisplaySimulationPreset()
        {
            return _previewDisplayType switch
            {
                DisplayType.SSD1306Blue   => DisplaySimulationPreset.Ssd1306OledBlue,
                DisplayType.SSD1306Green  => DisplaySimulationPreset.Ssd1306OledGreen,
                DisplayType.ePaper        => DisplaySimulationPreset.EPaper,
                DisplayType.FlipperZero   => DisplaySimulationPreset.FlipperZeroLcd,
                DisplayType.GenericWhite  => DisplaySimulationPreset.Ssd1306OledWhite,
                _ => DisplaySimulationPreset.Ssd1306OledWhite,
            };
        }

        private void EnsurePreviewSimBitmap()
        {
            int w = Math.Max(1, PreviewWidth);
            int h = Math.Max(1, PreviewHeight);

            if (_previewSimBitmap == null || _previewSimBitmap.PixelWidth != w || _previewSimBitmap.PixelHeight != h)
            {
                PreviewSimBitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, palette: null);
                _previewSimBuffer = new uint[w * h];
                OnPropertyChanged(nameof(DisplayPreviewBitmap));
            }
            // Note: the else-if "buffer size mismatch" branch was removed (L-3).
            // When the bitmap dimensions match, w*h is guaranteed to equal the buffer
            // length allocated in the if-body above, so a separate size check is unreachable.
        }

        public void UpdatePreviewSimulation(bool force = false)
        {
            if (SpriteState == null) return;
            if (_isPlaying) return; // Don't interrupt animation loops with drawing events
            
            UpdatePreviewSimulationForFrame(SpriteState.ActiveFrameIndex, force);
        }

        public void UpdatePreviewSimulationForFrame(int frameIndex, bool force = false)
        {
            if (!UseRealisticPreview)
                return;

            if (SpriteState?.Frames == null || frameIndex < 0 || frameIndex >= SpriteState.Frames.Count)
                return;

            var frame = SpriteState.Frames[frameIndex];
            if (frame.LayerPixels == null)
                return;

            long nowTicks = DateTime.UtcNow.Ticks;
            if (!force && (nowTicks - _lastPreviewSimulationTicks) < PreviewSimulationMinIntervalTicks)
                return;
            if (!force && _isStrokeRenderingActive)
            {
                _pendingPreviewSimulationAfterStroke = true;
                return;
            }
            using var perfScope = BeginDrawPerfScope("UpdatePreviewSimulation");

            EnsurePreviewSimBitmap();

            var (bg, fg) = GetSimulationColors(_previewDisplayType);

            var selectionSvc = frameIndex == SpriteState.ActiveFrameIndex ? _selectionService : null;

            Hexprite.Rendering.DisplaySimulationRenderer.Render(
                srcW: SpriteState.Width,
                srcH: SpriteState.Height,
                layers: SpriteState.Layers,
                layerPixels: frame.LayerPixels,
                colorMode: SpriteState.ColorMode,
                selectionSvc,
                _floatingPasteMode,
                PreviewSimBitmap.PixelWidth,
                PreviewSimBitmap.PixelHeight,
                bg,
                fg,
                GetDisplaySimulationPreset(),
                _previewQuality,
                PreviewRealismStrength / 100.0,
                PreviewScaleEffective,
                IsPreviewScaleCapped,
                _previewSimBuffer);

            var rect = new Int32Rect(0, 0, PreviewSimBitmap.PixelWidth, PreviewSimBitmap.PixelHeight);
            using (BeginDrawPerfScope("WritePixels.PreviewSim"))
            {
                PreviewSimBitmap.WritePixels(rect, _previewSimBuffer, PreviewSimBitmap.PixelWidth * 4, 0);
            }
            _lastPreviewSimulationTicks = nowTicks;
            OnPropertyChanged(nameof(DisplayPreviewBitmap));
        }

        public void BeginStrokeRenderSession()
        {
            _isStrokeRenderingActive = true;
        }

        public void EndStrokeRenderSession()
        {
            _isStrokeRenderingActive = false;
            if (PreviewBitmap != null && _previewBuffer != null && SpriteState != null)
            {
                var rect = new Int32Rect(0, 0, SpriteState.Width, SpriteState.Height);
                PreviewBitmap.WritePixels(rect, _previewBuffer, SpriteState.Width * 4, 0);
            }
            if (SpriteState != null && SpriteState.ActiveLayerIndex >= 0 && Layers != null && SpriteState.ActiveLayerIndex < Layers.Count)
            {
                bool hasContent = Array.IndexOf(SpriteState.Pixels, value: true) >= 0;
                if (Layers[SpriteState.ActiveLayerIndex].HasContent != hasContent)
                    Layers[SpriteState.ActiveLayerIndex].HasContent = hasContent;
            }
            UpdateOnionSkinCache();
            if (_pendingPreviewSimulationAfterStroke)
            {
                _pendingPreviewSimulationAfterStroke = false;
                UpdatePreviewSimulation(force: true);
            }

            if (IsHardwarePreviewEnabled)
            {
                TriggerHardwarePreviewUpdate();
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Public API instance method called via ViewModel reference in UI controllers; accesses instance telemetry in DEBUG builds")]
        public IDisposable BeginMovePerfScope() => BeginDrawPerfScope("HandleToolMove");

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsPixelOnWithOpacityHelper(bool pixelValue, int i, LayerOpacityMode opacityMode, int width)
        {
            if (!pixelValue) return false;
            if (opacityMode == LayerOpacityMode.Solid) return true;
            
            int x = i % width;
            int y = i / width;
            return opacityMode switch
            {
                LayerOpacityMode.Checkerboard => ((x + y) % 2) == 0,
                LayerOpacityMode.Sparse => (x % 2 == 0) && (y % 2 == 0),
                LayerOpacityMode.Dense => !((x % 2 != 0) && (y % 2 != 0)),
                _ => true,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ComposePixelState(int pixelIndex, List<(bool[] Pixels, LayerState State)> visibleLayers, int width)
        {
            if (visibleLayers.Count == 0) return false;

            bool composite = false;
            for (int li = visibleLayers.Count - 1; li >= 0; li--)
            {
                var (pixels, state) = visibleLayers[li];
                bool layerOn = state.OpacityMode == LayerOpacityMode.Solid ? pixels[pixelIndex] : IsPixelOnWithOpacityHelper(pixels[pixelIndex], pixelIndex, state.OpacityMode, width);
                
                var blendMode = state.BlendMode;
                if (blendMode == LayerBlendMode.Normal)
                {
                    if (layerOn) composite = true;
                }
                else if (blendMode == LayerBlendMode.Xor)
                {
                    if (layerOn) composite = !composite;
                }
                else if (blendMode == LayerBlendMode.Mask)
                {
                    if (!layerOn) composite = false;
                }
                else if (blendMode == LayerBlendMode.Subtract)
                {
                    if (layerOn) composite = false;
                }
            }
            return composite;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static (bool isOn, bool isFromActiveLayer) ComposePixelStateWithFocus(
            int pixelIndex, List<(bool[] Pixels, LayerState State)> visibleLayers, int activeLayerIndexAmongVisible, int width)
        {
            if (visibleLayers.Count == 0)
                return (false, false);

            bool isPixelOn = ComposePixelState(pixelIndex, visibleLayers, width);
            if (!isPixelOn)
                return (false, false);

            if (activeLayerIndexAmongVisible >= 0 && activeLayerIndexAmongVisible < visibleLayers.Count)
            {
                var (activePixels, activeState) = visibleLayers[activeLayerIndexAmongVisible];
                bool activeHasPixel = activeState.OpacityMode == LayerOpacityMode.Solid
                    ? activePixels[pixelIndex]
                    : IsPixelOnWithOpacityHelper(activePixels[pixelIndex], pixelIndex, activeState.OpacityMode, width);
                return (isPixelOn, isFromActiveLayer: activeHasPixel);
            }

            return (isPixelOn, isFromActiveLayer: false);
        }

        private readonly List<(bool[] Pixels, LayerState State)> _cachedVisibleLayers = [];

        private List<(bool[] Pixels, LayerState State)> GetVisibleLayersData()
        {
            var layers = SpriteState.Layers;
            _cachedVisibleLayers.Clear();
            if (layers == null || SpriteState.Frames == null || SpriteState.Frames.Count == 0 ||
                SpriteState.ActiveFrameIndex < 0 || SpriteState.ActiveFrameIndex >= SpriteState.Frames.Count)
            {
                return _cachedVisibleLayers;
            }

            var framePixels = SpriteState.Frames[SpriteState.ActiveFrameIndex].LayerPixels;
            if (framePixels == null) return _cachedVisibleLayers;

            int count = Math.Min(layers.Count, framePixels.Count);
            for (int i = 0; i < count; i++)
            {
                if (layers[i].IsVisible && framePixels[i] != null)
                    _cachedVisibleLayers.Add((framePixels[i].GetMonochromeData(), layers[i]));
            }
            return _cachedVisibleLayers;
        }

        /// <summary>
        /// Returns the index of the active layer within the visible layers list.
        /// Returns -1 if the active layer is not visible.
        /// </summary>
        private int GetActiveLayerIndexAmongVisible()
        {
            if (SpriteState.Layers == null) return -1;
            int activeIndex = SpriteState.ActiveLayerIndex;
            int visibleIndex = 0;
            for (int i = 0; i < SpriteState.Layers.Count; i++)
            {
                if (SpriteState.Layers[i].IsVisible)
                {
                    if (i == activeIndex)
                        return visibleIndex;
                    visibleIndex++;
                }
            }
            return -1;
        }

        /// <summary>
        /// Blends the "on" color with opacity for focus mode dimming.
        /// Returns a uint color value for the buffer.
        /// </summary>
        private uint GetDimmedColor(uint fullColor, float opacity)
        {
            // Extract components
            byte a = (byte)((fullColor >> 24) & 0xFF);
            byte r = (byte)((fullColor >> 16) & 0xFF);
            byte g = (byte)((fullColor >> 8) & 0xFF);
            byte b = (byte)(fullColor & 0xFF);

            // Apply opacity to alpha (or blend toward background)
            // For monochrome display simulation, we dim the color toward the "off" color
            uint offColor = _colorOffUint;
            byte offR = (byte)((offColor >> 16) & 0xFF);
            byte offG = (byte)((offColor >> 8) & 0xFF);
            byte offB = (byte)(offColor & 0xFF);

            // Bug 6: Use Math.Clamp to prevent byte wrap-around when the float blend
            // result strays just below 0 or above 255 due to floating-point rounding.
            // (r - offR) is an int in [-255, +255]; multiplied by a float opacity it can
            // round to a value just outside [0, 255] before the byte cast.
            byte newR = (byte)Math.Clamp((int)MathF.Round(offR + (r - offR) * opacity, MidpointRounding.AwayFromZero), 0, 255);
            byte newG = (byte)Math.Clamp((int)MathF.Round(offG + (g - offG) * opacity, MidpointRounding.AwayFromZero), 0, 255);
            byte newB = (byte)Math.Clamp((int)MathF.Round(offB + (b - offB) * opacity, MidpointRounding.AwayFromZero), 0, 255);

            return (uint)((a << 24) | (newR << 16) | (newG << 8) | newB);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Accesses _drawPerf instance field in DEBUG builds")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859:Use concrete types", Justification = "Returns polymorphic IDisposable between DEBUG PerfScope and Release NoopDisposable")]
        private IDisposable BeginDrawPerfScope(string scope)
        {
#if DEBUG
            return _drawPerf.Begin(scope);
#else
            return NoopDisposable.Instance;
#endif
        }

        private sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new();
            public void Dispose() { }
        }

#if DEBUG
        private sealed class DrawPerfCollector
        {
            private const int SampleWindow = 180;
            private readonly Dictionary<string, SampleBucket> _buckets = new(StringComparer.Ordinal);

            public IDisposable Begin(string scope) => new Scope(this, scope, Stopwatch.GetTimestamp());

            private void Commit(string scope, long startTimestamp)
            {
                long elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp;
                if (!_buckets.TryGetValue(scope, out var bucket))
                {
                    bucket = new SampleBucket();
                    _buckets[scope] = bucket;
                }
                bucket.Add(elapsedTicks);
                if (bucket.Count >= SampleWindow)
                {
                    Log.Debug("DrawPerf {Scope}: avg={AvgMs:F3}ms p95={P95Ms:F3}ms max={MaxMs:F3}ms n={Count}",
                        scope, bucket.GetAverageMs(), bucket.GetP95Ms(), bucket.GetMaxMs(), bucket.Count);
                    bucket.Reset();
                }
            }

            private sealed class Scope(MainViewModel.DrawPerfCollector owner, string scope, long startTimestamp) : IDisposable
            {
                private readonly DrawPerfCollector _owner = owner;
                private readonly string _scope = scope;
                private readonly long _startTimestamp = startTimestamp;
                private bool _disposed;

                public void Dispose()
                {
                    if (_disposed) return;
                    _disposed = true;
                    _owner.Commit(_scope, _startTimestamp);
                }
            }

            private sealed class SampleBucket
            {
                private readonly List<long> _samples = new(SampleWindow);
                private long _sumTicks;
                private long _maxTicks;
                public int Count => _samples.Count;

                public void Add(long ticks)
                {
                    _samples.Add(ticks);
                    _sumTicks += ticks;
                    if (ticks > _maxTicks) _maxTicks = ticks;
                }

                public double GetAverageMs() => TicksToMs(_sumTicks / (double)Math.Max(1, _samples.Count));

                public double GetP95Ms()
                {
                    if (_samples.Count == 0) return 0;
                    _samples.Sort();
                    int index = (int)Math.Ceiling(_samples.Count * 0.95) - 1;
                    index = Math.Clamp(index, 0, _samples.Count - 1);
                    return TicksToMs(_samples[index]);
                }

                public double GetMaxMs() => TicksToMs(_maxTicks);

                public void Reset()
                {
                    _samples.Clear();
                    _sumTicks = 0;
                    _maxTicks = 0;
                }

                private static double TicksToMs(double ticks) => ticks * 1000.0 / Stopwatch.Frequency;
            }
        }
#endif

        // ── Status bar helpers ────────────────────────────────────────────

        public void ClearLayerHoverInfo()
        {
            LayerHoverInfo = string.Empty;
        }

        private void UpdateSelectionInfo()
        {
            if (!_selectionService.HasActiveSelection || SpriteState == null)
            {
                SelectionInfo = string.Empty;
                return;
            }

            int width = _selectionService.MaxX - _selectionService.MinX + 1;
            int height = _selectionService.MaxY - _selectionService.MinY + 1;
            int pxCount = _selectionService.CountPixelsInSelection(SpriteState);
            string layerName = (SpriteState.Layers != null && SpriteState.ActiveLayerIndex >= 0 && SpriteState.ActiveLayerIndex < SpriteState.Layers.Count)
                ? SpriteState.Layers[SpriteState.ActiveLayerIndex].Name
                : $"Layer {SpriteState.ActiveLayerIndex + 1}";

            if (pxCount == 0)
            {
                SelectionInfo = string.Create(CultureInfo.InvariantCulture, $"Sel: {width}×{height} • Empty on '{layerName}'");
            }
            else
            {
                SelectionInfo = string.Create(CultureInfo.InvariantCulture, $"Sel: {width}×{height} • {pxCount} px on '{layerName}'");
            }
        }

        /// <summary>
        /// Captures canonical physical display colors for display simulation rendering.
        /// Colors represent real hardware optical emission/absorption characteristics and are
        /// completely theme-immune.
        /// </summary>
        internal static (Color bg, Color fg) GetSimulationColors(DisplayType displayType = DisplayType.GenericWhite)
        {
            return displayType switch
            {
                DisplayType.SSD1306Blue   => (Color.FromRgb(0x00, 0x00, 0x00), Color.FromRgb(0x00, 0xB4, 0xFF)),
                DisplayType.SSD1306Green  => (Color.FromRgb(0x00, 0x00, 0x00), Color.FromRgb(0x00, 0xE6, 0x76)),
                DisplayType.ePaper        => (Color.FromRgb(0xE6, 0xE4, 0xDD), Color.FromRgb(0x14, 0x14, 0x14)),
                DisplayType.FlipperZero   => (Color.FromRgb(0xFF, 0x82, 0x00), Color.FromRgb(0x00, 0x00, 0x00)),
                DisplayType.GenericWhite or _ => (Color.FromRgb(0x00, 0x00, 0x00), Color.FromRgb(0xF0, 0xF6, 0xFC)),
            };
        }

        // ── Theme helpers ───────────────────────────────────────────────

        internal static double GetRelativeLuminance(Color c)
        {
            static double SrgbToLinear(double v)
                => v <= 0.04045 ? (v / 12.92) : Math.Pow((v + 0.055) / 1.055, 2.4);

            double r = SrgbToLinear(c.R / 255.0);
            double g = SrgbToLinear(c.G / 255.0);
            double b = SrgbToLinear(c.B / 255.0);
            return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
        }

        internal static Color BoostTowardsWhite(Color c, double t)
        {
            t = Math.Clamp(t, 0.0, 1.0);
            byte r = (byte)Math.Clamp((int)Math.Round(c.R + (255 - c.R) * t, MidpointRounding.AwayFromZero), 0, 255);
            byte g = (byte)Math.Clamp((int)Math.Round(c.G + (255 - c.G) * t, MidpointRounding.AwayFromZero), 0, 255);
            byte b = (byte)Math.Clamp((int)Math.Round(c.B + (255 - c.B) * t, MidpointRounding.AwayFromZero), 0, 255);
            return Color.FromArgb(c.A, r, g, b);
        }

    }
}
