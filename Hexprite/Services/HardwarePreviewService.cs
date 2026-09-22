using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Serilog;

namespace Hexprite.Services
{
    public class HardwarePreviewService : IHardwarePreviewService, IDisposable
    {
        public event EventHandler<bool>? EnabledChanged;
        public event EventHandler<HardwarePreviewConnectionState>? ConnectionStateChanged;

        private SerialPort? _serialPort;
        private volatile bool _isEnabled;
        private string? _portName;
        private int _baudRate = 115200;
        private readonly Lock _lock = new();
        private volatile bool _isDisposed;
        private volatile bool _awaitingBootloader;

        private CancellationTokenSource? _portCts;
        private readonly SemaphoreSlim _portReconfigSemaphore = new(1, 1);
        private int _reconfigGeneration;

        internal DateTime? ConnectingStartedUtc { get; set; }
        internal DateTime? LastFrameSentUtc { get; set; }

        private readonly AckByteScanner _ackScanner = new();
        private Timer? _ackWatchdogTimer;
        private volatile HardwarePreviewConnectionState _connectionState = HardwarePreviewConnectionState.Disconnected;
        private CancellationTokenSource? _reconnectCts;

        public HardwarePreviewConnectionState ConnectionState => _connectionState;

        public HardwarePreviewPlacement Placement { get; set; } = HardwarePreviewPlacement.Center;
        public HardwarePreviewScale Scale { get; set; } = HardwarePreviewScale.Scale1x;
        public (int Width, int Height) TargetDisplaySize { get; set; } = (128, 64);

        public event EventHandler<(bool[] Pixels, int Width, int Height)>? FrameTransmitted;
        public (bool[]? Pixels, int Width, int Height) LastTransmittedFrame { get; private set; }

        /// <summary>
        /// When the service auto-disables due to an error, this contains a
        /// user-readable reason string. Reset to null on successful open.
        /// </summary>
        public string? LastDisableReason { get; private set; }

        private readonly Channel<byte> _frameChannel;
        private readonly CancellationTokenSource _cts;

        private readonly Lock _snapshotLock = new();
        private bool[]? _snapshotBuffer;
        private int _snapshotWidth;
        private int _snapshotHeight;

        public HardwarePreviewService()
        {
            _cts = new CancellationTokenSource();
            _frameChannel = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
            });

            // Start the background processing loop
            Task.Run(ProcessQueueAsync, _cts.Token);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    lock (_lock)
                    {
                        _isEnabled = value;
                        if (!value)
                        {
                            _reconnectCts?.Cancel();
                            _reconnectCts?.Dispose();
                            _reconnectCts = null;
                        }
                        _portCts?.Cancel();
                        _portCts?.Dispose();
                        _portCts = null;
                    }
                    
                    EnabledChanged?.Invoke(this, _isEnabled);
                    TriggerPortReconfiguration();
                }
            }
        }

        public string? PortName
        {
            get => _portName;
            set
            {
                if (_portName != value)
                {
                    _portName = value;
                    lock (_lock)
                    {
                        _reconnectCts?.Cancel();
                        _reconnectCts?.Dispose();
                        _reconnectCts = null;
                        _portCts?.Cancel();
                        _portCts?.Dispose();
                        _portCts = null;
                    }
                    TriggerPortReconfiguration();
                }
            }
        }

        public int BaudRate
        {
            get => _baudRate;
            set
            {
                if (_baudRate != value)
                {
                    _baudRate = value;
                    lock (_lock)
                    {
                        if (_serialPort != null && _serialPort.IsOpen)
                        {
                            try
                            {
                                _serialPort.BaudRate = value;
                                _ackScanner.Reset();
                                ConnectingStartedUtc = DateTime.UtcNow;
                                SetConnectionState(HardwarePreviewConnectionState.Connecting);
                                Log.Information("Hardware preview baud rate changed to {BaudRate} on open port {Port}", value, _serialPort.PortName);
                                return;
                            }
                            catch (Exception ex)
                            {
                                Log.Debug(ex, "Could not set baud rate on open port; falling back to full reopen");
                            }
                        }

                        _portCts?.Cancel();
                        _portCts?.Dispose();
                        _portCts = null;
                    }
                    TriggerPortReconfiguration();
                }
            }
        }

        public IEnumerable<string> GetAvailablePorts()
        {
            try
            {
                return SerialPort.GetPortNames().Order();
            }
            catch
            {
                return [];
            }
        }

        public IEnumerable<HardwarePreviewPortOption> GetAvailablePortOptions()
        {
            string[] portNames = [.. GetAvailablePorts()];
            if (portNames.Length == 0) return [];

            Dictionary<string, string> friendlyNames = TryGetFriendlyPortNames();
            return [.. portNames
                .Select(p => new HardwarePreviewPortOption(
                    p, friendlyNames.TryGetValue(p, out string? friendly) ? friendly : p))];
        }

        private static Dictionary<string, string> TryGetFriendlyPortNames()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
                foreach (System.Management.ManagementBaseObject device in searcher.Get())
                {
                    if (device["Name"] is not string name || string.IsNullOrEmpty(name)) continue;

                    int start = name.LastIndexOf("(COM", StringComparison.OrdinalIgnoreCase);
                    int end = start >= 0 ? name.IndexOf(')', start) : -1;
                    if (start < 0 || end < 0) continue;

                    string portName = name.Substring(start + 1, end - start - 1); // "COM5"
                    string caption = name[..start].Trim();
                    result[portName] = string.IsNullOrEmpty(caption) ? portName : $"{caption} ({portName})";
                }
            }
            catch (Exception ex)
            {
                // WMI can be unavailable, slow, or permission-restricted in locked-down
                // environments — fall back to bare port names rather than fail the refresh.
                Log.Debug(ex, "Friendly serial port name lookup failed");
            }
            return result;
        }

        internal void SetConnectionState(HardwarePreviewConnectionState state)
        {
            bool changed;
            lock (_lock)
            {
                changed = _connectionState != state;
                if (changed) _connectionState = state;
            }
            if (changed)
            {
                ConnectionStateChanged?.Invoke(this, state);
            }
        }

        private async Task ReadLoopAsync(SerialPort port, CancellationToken token)
        {
            byte[] buffer = new byte[64];
            while (!token.IsCancellationRequested && !_isDisposed && port.IsOpen)
            {
                try
                {
                    int bytesRead = await port.BaseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
                    if (bytesRead > 0)
                    {
                        ProcessIncomingBytes(buffer, 0, bytesRead);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Hardware preview read loop terminated on {Port}", port.PortName);
                    break;
                }
            }
        }

        internal void ProcessIncomingBytes(byte[] buffer, int offset, int count)
        {
            var scanResult = _ackScanner.Feed(buffer, offset, count);
            ProcessScanResult(scanResult);
        }

        internal void ProcessScanResult(AckScanResult scanResult)
        {
            if (scanResult == AckScanResult.I2cError)
            {
                // Guard against false positives: only process I2C error if a frame was actually sent recently (within 1.5s)
                // and we are not still waiting for the bootloader. Otherwise it is bootloader/ROM noise or user serial text.
                if (_awaitingBootloader || !LastFrameSentUtc.HasValue || (DateTime.UtcNow - LastFrameSentUtc.Value) > TimeSpan.FromMilliseconds(1500))
                {
                    Log.Debug("Ignoring unsolicited I2C error byte on {Port} (no recent frame sent)", PortName);
                    return;
                }

                LastDisableReason = "Display not responding (check SDA/SCL pins & wiring)";
                SetConnectionState(HardwarePreviewConnectionState.Error);
            }
            else if (scanResult == AckScanResult.ChecksumError)
            {
                if (_awaitingBootloader || !LastFrameSentUtc.HasValue || (DateTime.UtcNow - LastFrameSentUtc.Value) > TimeSpan.FromMilliseconds(1500))
                {
                    return;
                }
                Log.Warning("Hardware preview received checksum error response from device on {Port}", PortName);
            }
            else if (scanResult == AckScanResult.Ack)
            {
                LastDisableReason = null;
                SetConnectionState(HardwarePreviewConnectionState.Connected);
            }
        }

        internal void AckWatchdogTick(object? state)
        {
            if (_isDisposed) return;

            DateTime now = DateTime.UtcNow;

            if (_connectionState == HardwarePreviewConnectionState.Connecting)
            {
                if (ConnectingStartedUtc.HasValue && now - ConnectingStartedUtc.Value > TimeSpan.FromSeconds(5))
                {
                    LastDisableReason = $"No response on {PortName} (check port or baud rate)";
                    SetConnectionState(HardwarePreviewConnectionState.Error);
                }
                return;
            }

            if (_connectionState != HardwarePreviewConnectionState.Connected) return;

            DateTime lastAckConnected = _ackScanner.LastAckUtc ?? DateTime.MinValue;

            // Only downgrade to Streaming if a frame was actually sent and unacknowledged
            // for longer than the timeout window. If the user is just idle between edits, remain Connected!
            if (LastFrameSentUtc.HasValue && LastFrameSentUtc.Value > lastAckConnected && (now - LastFrameSentUtc.Value) > TimeSpan.FromSeconds(3))
            {
                SetConnectionState(HardwarePreviewConnectionState.Streaming);
            }
        }

        public void SendFrame(bool[] pixels, int width, int height)
        {
            if (!_isEnabled || _isDisposed) return;
            if (width <= 0 || height <= 0 || width > 65535 || height > 65535) return;
            if (pixels.Length < width * height) return;

            lock (_snapshotLock)
            {
                int len = width * height;
                if (_snapshotBuffer == null || _snapshotBuffer.Length < len)
                {
                    _snapshotBuffer = new bool[len];
                }
                Array.Copy(pixels, _snapshotBuffer, len);
                _snapshotWidth = width;
                _snapshotHeight = height;
            }

            // Using TryWrite with DropOldest mode ensures we always have the most recent frame
            // queued up, without needing manual locking or creating new tasks.
            _frameChannel.Writer.TryWrite(0);
        }

        public void ResetConnectionForBaudProbe()
        {
            lock (_lock)
            {
                _ackScanner.Reset();
                ConnectingStartedUtc = DateTime.UtcNow;
                SetConnectionState(HardwarePreviewConnectionState.Connecting);
            }
        }

        private async Task ProcessQueueAsync()
        {
            try
            {
                bool[]? localBuffer = null;
                await foreach (var _ in _frameChannel.Reader.ReadAllAsync(_cts.Token))
                {
                    if (!_isEnabled || _isDisposed) continue;

                    int localW, localH;
                    lock (_snapshotLock)
                    {
                        localW = _snapshotWidth;
                        localH = _snapshotHeight;
                        int len = localW * localH;

                        if (localBuffer == null || localBuffer.Length < len)
                        {
                            localBuffer = new bool[len];
                        }
                        if (_snapshotBuffer != null)
                        {
                            Array.Copy(_snapshotBuffer, localBuffer, len);
                        }
                    }

                    if (localW <= 0 || localH <= 0 || localW > 65535 || localH > 65535) continue;

                    try
                    {
                        bool skipFrame = false;
                        bool shouldWaitForDrain = false;

                        lock (_lock)
                        {
                            if (_isDisposed) return;

                            if (_serialPort == null || !_serialPort.IsOpen)
                            {
                                skipFrame = true;
                            }
                            else if (_serialPort.BytesToWrite > 0)
                            {
                                // If newer frames are already in the channel, skip this intermediate frame.
                                // Otherwise, if this is the final/only frame, wait briefly for buffer to clear.
                                if (_frameChannel.Reader.Count == 0)
                                {
                                    shouldWaitForDrain = true;
                                }
                                else
                                {
                                    skipFrame = true;
                                }
                            }
                        }

                        if (shouldWaitForDrain)
                        {
                            for (int i = 0; i < 8; i++)
                            {
                                await Task.Delay(10, _cts.Token);
                                lock (_lock)
                                {
                                    if (_serialPort == null || !_serialPort.IsOpen || _serialPort.BytesToWrite == 0)
                                    {
                                        break;
                                    }
                                }
                            }
                            lock (_lock)
                            {
                                if (_serialPort != null && _serialPort.IsOpen && _serialPort.BytesToWrite > 0)
                                {
                                    skipFrame = true;
                                }
                            }
                        }

                        if (skipFrame)
                        {
                            continue;
                        }

                        // Apply placement, scaling, and target display dimension mapping
                        (bool[] transformedBuffer, int outW, int outH) = TransformFrame(localBuffer, localW, localH);

                        // Perform bitmap to byte conversion on the background thread
                        byte[] data = CodeGeneratorService.BuildByteArray(transformedBuffer, outW, outH, 
                            lsbFirst: true, 
                            isFloating: false, 
                            floatingPixels: null, 0, 0, 0, 0, 
                            Core.FloatingPasteMode.Transparent, _cts.Token);

                        CancellationToken portToken;
                        lock (_lock)
                        {
                            portToken = _portCts?.Token ?? CancellationToken.None;
                        }

                        // Wait for Arduino bootloader to finish before sending first frame.
                        // Opening with DtrEnable resets the board; the bootloader occupies
                        // the serial line for ~2s before handing off to the user's sketch.
                        if (_awaitingBootloader)
                        {
                            _awaitingBootloader = false;
                            try
                            {
                                using var bootloaderCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, portToken);
                                await Task.Delay(2000, bootloaderCts.Token);
                            }
                            catch (OperationCanceledException)
                            {
                                if (_cts.IsCancellationRequested) break;
                                continue;
                            }
                        }

                        // Protocol v2: [0xAA, 0x55] [Width_L, Width_H] [Height_L, Height_H] [Data...] [Checksum: XOR]
                        // Uses 2-byte little-endian width/height to support canvases > 255px.
                        byte[] packet = new byte[data.Length + 7];
                        packet[0] = 0xAA;
                        packet[1] = 0x55;
                        packet[2] = (byte)(outW & 0xFF);
                        packet[3] = (byte)((outW >> 8) & 0xFF);
                        packet[4] = (byte)(outH & 0xFF);
                        packet[5] = (byte)((outH >> 8) & 0xFF);

                        Array.Copy(data, 0, packet, 6, data.Length);

                        byte checksum = 0;
                        for (int i = 0; i < packet.Length - 1; i++)
                        {
                            checksum ^= packet[i];
                        }
                        packet[^1] = checksum;

                        SerialPort? portSnapshot;
                        lock (_lock)
                        {
                            if (_isDisposed) return;
                            portSnapshot = _serialPort;
                            portToken = _portCts?.Token ?? CancellationToken.None;
                        }

                        if (portSnapshot != null && portSnapshot.IsOpen)
                        {
                            using var writeCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, portToken);
                            writeCts.CancelAfter(1000);

                            await portSnapshot.BaseStream.WriteAsync(packet.AsMemory(), writeCts.Token);
                            LastFrameSentUtc = DateTime.UtcNow;
                            LastTransmittedFrame = (transformedBuffer, outW, outH);
                            FrameTransmitted?.Invoke(this, (transformedBuffer, outW, outH));

                            // A successful write proves the port accepts data; don't downgrade
                            // an ACK-confirmed "Connected" status back down to "Streaming" though.
                            if (_connectionState != HardwarePreviewConnectionState.Connected)
                            {
                                SetConnectionState(HardwarePreviewConnectionState.Streaming);
                            }
                        }
                    }
                    catch (OperationCanceledException ex)
                    {
                        if (_cts.IsCancellationRequested) break;
                        // Cancelled by port change or 1000ms write timeout. Drop frame and continue.
                        Log.Debug(ex, "Hardware preview write cancelled or timed out. Dropping frame to {Port}", PortName);
                    }
                    catch (TimeoutException ex)
                    {
                        // The serial buffer is full or the device isn't reading fast enough.
                        // Drop this frame to catch up. Do NOT disable the service.
                        Log.Debug(ex, "Hardware preview write timeout. Dropping frame to {Port}", PortName);
                    }
                    catch (ObjectDisposedException)
                    {
                        // Port was closed from another thread while writing.
                        // Break out if disposed/cancelled; otherwise continue to next frame.
                        if (_isDisposed || _cts.IsCancellationRequested) break;
                    }
                    catch (InvalidOperationException ex)
                    {
                        // Port was closed by the OS (device unplugged or board reset during upload)
                        Log.Warning(ex, "Hardware preview device disconnected: {Port}", PortName);
                        HandlePortDisconnection($"Device disconnected ({PortName})");
                    }
                    catch (System.IO.IOException ex)
                    {
                        // I/O error on the port (cable pulled, driver crash, or board reset during upload)
                        Log.Warning(ex, "Hardware preview I/O error on {Port}", PortName);
                        HandlePortDisconnection($"Connection lost ({PortName})");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to send hardware preview frame to {Port}", PortName);
                        LastDisableReason = $"Error: {ex.Message}";
                        lock (_lock)
                        {
                            _isEnabled = false;
                        }
                        SetConnectionState(HardwarePreviewConnectionState.Error);
                        EnabledChanged?.Invoke(this, e: false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected exit when disposed
            }
        }

        internal void SetPortNameForTesting(string? portName)
        {
            _portName = portName;
        }

        private void TriggerPortReconfiguration()
        {
            lock (_lock)
            {
                if (!_isEnabled || !string.IsNullOrEmpty(_portName) || _serialPort != null)
                {
                    int currentGen = Interlocked.Increment(ref _reconfigGeneration);
                    _ = Task.Run(() => ReconfigurePortAsync(currentGen), _cts.Token);
                }
            }
        }

        private async Task ReconfigurePortAsync(int gen)
        {
            try
            {
                await _portReconfigSemaphore.WaitAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                if (gen != Volatile.Read(ref _reconfigGeneration) || _isDisposed)
                {
                    return;
                }

                SerialPort? oldPort;
                lock (_lock)
                {
                    oldPort = _serialPort;
                    _serialPort = null;
                    _ackWatchdogTimer?.Dispose();
                    _ackWatchdogTimer = null;
                }

                if (oldPort != null)
                {
                    try
                    {
                        if (oldPort.IsOpen) oldPort.Close();
                        oldPort.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Error closing serial port during reconfiguration");
                    }
                }

                if (gen != Volatile.Read(ref _reconfigGeneration) || _isDisposed)
                {
                    return;
                }

                bool shouldOpen;
                string? portToOpen;
                int baud;
                lock (_lock)
                {
                    shouldOpen = _isEnabled && !string.IsNullOrEmpty(_portName);
                    portToOpen = _portName;
                    baud = _baudRate;
                }

                if (!shouldOpen || string.IsNullOrEmpty(portToOpen))
                {
                    SetConnectionState(HardwarePreviewConnectionState.Disconnected);
                    return;
                }

                lock (_lock)
                {
                    _portCts?.Cancel();
                    _portCts?.Dispose();
                    _portCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                    ConnectingStartedUtc = DateTime.UtcNow;
                }

                SetConnectionState(HardwarePreviewConnectionState.Connecting);

                try
                {
                    var newPort = new SerialPort(portToOpen, baud, Parity.None, 8, StopBits.One)
                    {
                        DtrEnable = true,
                        RtsEnable = true,
                        WriteTimeout = 1000,
                    };
                    newPort.Open();

                    lock (_lock)
                    {
                        if (gen != Volatile.Read(ref _reconfigGeneration) || _isDisposed)
                        {
                            try
                            {
                                newPort.Close();
                                newPort.Dispose();
                            }
                            catch { }
                            return;
                        }

                        _serialPort = newPort;
                        _awaitingBootloader = true;
                        _ackScanner.Reset();
                        LastDisableReason = null;
                        _ackWatchdogTimer = new Timer(AckWatchdogTick, state: null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                    }

                    // Dedicated asynchronous background read loop directly on BaseStream
                    CancellationToken portToken = _portCts?.Token ?? CancellationToken.None;
                    _ = Task.Run(() => ReadLoopAsync(newPort, portToken), portToken);

                    Log.Information("Hardware preview port opened: {Port} at {BaudRate}", portToOpen, baud);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to open hardware preview port: {Port}", portToOpen);
                    LastDisableReason = $"Error: {ex.Message}";
                    SetConnectionState(HardwarePreviewConnectionState.Error);
                }
            }
            finally
            {
                try
                {
                    _portReconfigSemaphore.Release();
                }
                catch (ObjectDisposedException) { }
            }
        }

        private void HandlePortDisconnection(string reason)
        {
            LastDisableReason = reason;
            string? portToRetry = PortName;

            CancellationTokenSource reconnectCts;
            lock (_lock)
            {
                _portCts?.Cancel();
                _portCts?.Dispose();
                _portCts = null;

                _ackWatchdogTimer?.Dispose();
                _ackWatchdogTimer = null;

                ConnectingStartedUtc = DateTime.UtcNow;

                _reconnectCts?.Cancel();
                _reconnectCts?.Dispose();
                _reconnectCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                reconnectCts = _reconnectCts;
            }

            SetConnectionState(HardwarePreviewConnectionState.Connecting);

            // Attempt auto-reconnect asynchronously (3 attempts: 1.5s, 3.0s, 4.5s)
            // This handles board resets during firmware uploads when updating SDA/SCL pins
            _ = Task.Run(async () =>
            {
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        await Task.Delay(attempt * 1500, reconnectCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    lock (_lock)
                    {
                        if (!_isEnabled || _isDisposed || reconnectCts.IsCancellationRequested) return;
                    }

                    if (!string.IsNullOrEmpty(portToRetry) && GetAvailablePorts().Contains(portToRetry, StringComparer.OrdinalIgnoreCase))
                    {
                        Log.Information("Attempting auto-reconnect to {Port} (attempt {Attempt}/3)", portToRetry, attempt);
                        TriggerPortReconfiguration();

                        try
                        {
                            await Task.Delay(1000, reconnectCts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }

                        lock (_lock)
                        {
                            if (_serialPort != null && _serialPort.IsOpen)
                            {
                                Log.Information("Successfully auto-reconnected hardware preview to {Port}", portToRetry);
                                _frameChannel.Writer.TryWrite(0);
                                return;
                            }
                        }
                    }
                }

                // If all retries failed, permanently disable and notify
                lock (_lock)
                {
                    if (reconnectCts.IsCancellationRequested) return;
                    _isEnabled = false;
                }
                SetConnectionState(HardwarePreviewConnectionState.Error);
                EnabledChanged?.Invoke(this, e: false);
            }, reconnectCts.Token);
        }

        internal (bool[] TransformedBuffer, int OutW, int OutH) TransformFrame(bool[] srcPixels, int srcW, int srcH)
        {
            HardwarePreviewPlacement placement = Placement;
            HardwarePreviewScale scaleOption = Scale;
            (int targetW, int targetH) = TargetDisplaySize;

            int scale = scaleOption switch
            {
                HardwarePreviewScale.Scale2x => 2,
                HardwarePreviewScale.Scale4x => 4,
                HardwarePreviewScale.ScaleToFit => targetW > 0 && targetH > 0 
                    ? Math.Max(1, Math.Min(targetW / srcW, targetH / srcH)) 
                    : 1,
                _ => 1
            };

            // If 1x, TopLeft, and no target display dimension mapping, send original
            if (scale == 1 && placement == HardwarePreviewPlacement.TopLeft && (targetW <= 0 || (srcW == targetW && srcH == targetH)))
            {
                return (srcPixels, srcW, srcH);
            }

            int outW = targetW > 0 ? targetW : srcW * scale;
            int outH = targetH > 0 ? targetH : srcH * scale;

            // Ensure destination buffer is at least as large as the scaled sprite
            int scaledW = srcW * scale;
            int scaledH = srcH * scale;
            if (outW < scaledW) outW = scaledW;
            if (outH < scaledH) outH = scaledH;

            bool[] dest = new bool[outW * outH];

            int offsetX = (placement == HardwarePreviewPlacement.Center && outW > scaledW) ? (outW - scaledW) / 2 : 0;
            int offsetY = (placement == HardwarePreviewPlacement.Center && outH > scaledH) ? (outH - scaledH) / 2 : 0;

            for (int y = 0; y < srcH; y++)
            {
                int srcRow = y * srcW;
                for (int x = 0; x < srcW; x++)
                {
                    if (srcPixels[srcRow + x])
                    {
                        for (int dy = 0; dy < scale; dy++)
                        {
                            int destY = offsetY + y * scale + dy;
                            if (destY < 0 || destY >= outH) continue;
                            int destRow = destY * outW;
                            for (int dx = 0; dx < scale; dx++)
                            {
                                int destX = offsetX + x * scale + dx;
                                if (destX >= 0 && destX < outW)
                                {
                                    dest[destRow + destX] = true;
                                }
                            }
                        }
                    }
                }
            }

            return (dest, outW, outH);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            
            _isDisposed = true;
            _isEnabled = false;

            _cts.Cancel();
            _frameChannel.Writer.TryComplete();

            SerialPort? portToClose = null;
            lock (_lock)
            {
                try
                {
                    _portCts?.Cancel();
                    _portCts?.Dispose();
                    _ackWatchdogTimer?.Dispose();
                    _ackWatchdogTimer = null;

                    portToClose = _serialPort;
                    _serialPort = null;
                }
                catch
                {
                    // Ignore dispose errors
                }
            }

            if (portToClose != null)
            {
                try
                {
                    if (portToClose.IsOpen) portToClose.Close();
                    portToClose.Dispose();
                }
                catch
                {
                    // Ignore dispose errors
                }
            }

            try
            {
                _portReconfigSemaphore.Dispose();
            }
            catch { }

            _cts.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
