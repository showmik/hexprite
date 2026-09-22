using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using Hexprite.Core;

namespace Hexprite.Services
{
    public interface IFlipperScreenStreamService : IDisposable
    {
        bool IsConnected { get; }
        bool IsStreaming { get; }
        bool AutoReconnect { get; set; }
        string? CurrentPort { get; }
        int CurrentFps { get; }
        long TotalFramesSent { get; }

        event EventHandler<bool>? ConnectionStateChanged;
        event EventHandler<int>? FpsUpdated;

        Task<List<FlipperDeviceInfo>> ScanDevicesAsync(CancellationToken ct = default);
        Task<bool> ConnectAsync(string portName, CancellationToken ct = default);
        void Disconnect();
        void StartStreaming(Func<bool[]?> frameProvider, int targetFps = 20);
        void StopStreaming();
        bool SendSingleFrame(bool[] pixels128x64);
        bool SendSingleFrame(SpriteState sprite, int frameIndex = 0);
    }

    public class FlipperScreenStreamService : IFlipperScreenStreamService
    {
        private SerialPort? _serial;
        private CancellationTokenSource? _streamCts;
        private Task? _streamTask;
        private CancellationTokenSource? _reconnectCts;
        private readonly Lock _lock = new();

        private bool _isStreaming;
        private bool _autoReconnect = true;
        private string? _lastPortName;
        private Func<bool[]?>? _lastFrameProvider;
        private int _lastTargetFps = 20;
        private int _fps;
        private long _framesSent;
        private DateTime _lastFpsCalc = DateTime.UtcNow;
        private int _framesInCurrentSec;
        private readonly byte[] _encodeBuffer = new byte[1024];

        public bool IsConnected
        {
            get
            {
                lock (_lock)
                {
                    return _serial != null && _serial.IsOpen;
                }
            }
        }

        public bool IsStreaming
        {
            get
            {
                lock (_lock)
                {
                    return _isStreaming;
                }
            }
        }

        public bool AutoReconnect
        {
            get
            {
                lock (_lock)
                {
                    return _autoReconnect;
                }
            }
            set
            {
                lock (_lock)
                {
                    _autoReconnect = value;
                    if (!value)
                    {
                        CancelReconnect();
                    }
                }
            }
        }

        public string? CurrentPort
        {
            get
            {
                lock (_lock)
                {
                    return _serial?.PortName ?? _lastPortName;
                }
            }
        }

        public int CurrentFps
        {
            get
            {
                lock (_lock)
                {
                    return _fps;
                }
            }
        }

        public long TotalFramesSent
        {
            get
            {
                lock (_lock)
                {
                    return _framesSent;
                }
            }
        }

        public event EventHandler<bool>? ConnectionStateChanged;
        public event EventHandler<int>? FpsUpdated;

        public Task<List<FlipperDeviceInfo>> ScanDevicesAsync(CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var devices = new List<FlipperDeviceInfo>();
                var portNames = SerialPort.GetPortNames();

                try
                {
                    using var searcher = new ManagementObjectSearcher(
                        "SELECT Name, DeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");

                    foreach (ManagementBaseObject obj in searcher.Get())
                    {
                        ct.ThrowIfCancellationRequested();
                        if (obj["Name"] is not string name) continue;
                        string deviceId = obj["DeviceID"]?.ToString() ?? "";

                        int start = name.LastIndexOf("(COM", StringComparison.OrdinalIgnoreCase);
                        int end = start >= 0 ? name.IndexOf(')', start) : -1;
                        if (start < 0 || end < 0) continue;

                        string port = name.Substring(start + 1, end - start - 1);
                        bool isFlipper = name.Contains("Flipper", StringComparison.OrdinalIgnoreCase)
                            || deviceId.Contains("VID_0483&PID_5740", StringComparison.OrdinalIgnoreCase);

                        if (isFlipper)
                        {
                            devices.Add(new FlipperDeviceInfo(port, name, IsConnected: true));
                        }
                    }
                }
                catch
                {
                    // Fallback
                }

                if (devices.Count == 0)
                {
                    foreach (var port in portNames)
                    {
                        devices.Add(new FlipperDeviceInfo(port, $"Serial Device ({port})", IsConnected: false));
                    }
                }

                return devices;
            }, ct);
        }

        public async Task<bool> ConnectAsync(string portName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(portName)) return false;

            CancelReconnect();

            return await Task.Run(() =>
            {
                DisconnectInternal(notify: false);

                bool connected = false;
                lock (_lock)
                {
                    try
                    {
                        _serial = new SerialPort(portName, 230400, Parity.None, 8, StopBits.One)
                        {
                            ReadTimeout = 2000,
                            WriteTimeout = 2000,
                            DtrEnable = true,
                            RtsEnable = true,
                        };

                        _serial.Open();
                        Thread.Sleep(80);

                        // Wake CLI and cancel any prior running command
                        _serial.DiscardInBuffer();
                        _serial.DiscardOutBuffer();
                        _serial.Write("\x03\r\n");
                        Thread.Sleep(60);
                        _serial.Write("\x03\r\n");
                        Thread.Sleep(60);
                        _serial.DiscardInBuffer();

                        // Start screen streaming command
                        _serial.Write("screen_stream\r\n");
                        Thread.Sleep(80);
                        _serial.DiscardInBuffer();

                        _lastPortName = portName;
                        connected = true;
                    }
                    catch
                    {
                        connected = false;
                    }
                }

                if (connected)
                {
                    ConnectionStateChanged?.Invoke(this, e: true);
                    return true;
                }

                Disconnect();
                return false;
            }, ct);
        }

        public void Disconnect()
        {
            CancelReconnect();
            DisconnectInternal(notify: true);
        }

        private void DisconnectInternal(bool notify)
        {
            bool notifyDisconnected = false;

            StopStreaming();

            lock (_lock)
            {
                if (_serial != null)
                {
                    notifyDisconnected = true;
                    try
                    {
                        if (_serial.IsOpen)
                        {
                            _serial.Write("\x03\r\n"); // Exit streaming mode
                            _serial.Close();
                        }
                    }
                    catch
                    {
                        // Best effort
                    }
                    finally
                    {
                        _serial.Dispose();
                        _serial = null;
                    }
                }
            }

            if (notify && notifyDisconnected)
            {
                ConnectionStateChanged?.Invoke(this, e: false);
            }
        }

        public void StartStreaming(Func<bool[]?> frameProvider, int targetFps = 20)
        {
            ArgumentNullException.ThrowIfNull(frameProvider);
            int clampedFps = Math.Clamp(targetFps, 1, 60);

            StopStreaming();

            lock (_lock)
            {
                _lastFrameProvider = frameProvider;
                _lastTargetFps = clampedFps;

                if (_serial == null || !_serial.IsOpen) return;

                _isStreaming = true;
                _streamCts = new CancellationTokenSource();
                var token = _streamCts.Token;

                _streamTask = Task.Run(async () =>
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    long ticksPerFrame = (System.Diagnostics.Stopwatch.Frequency * 1000) / (clampedFps * 1000);
                    long nextFrameTicks = sw.ElapsedTicks;

                    while (!token.IsCancellationRequested && IsConnected)
                    {
                        try
                        {
                            long currentTicks = sw.ElapsedTicks;
                            if (currentTicks >= nextFrameTicks)
                            {
                                nextFrameTicks += ticksPerFrame;
                                if (currentTicks > nextFrameTicks + ticksPerFrame)
                                {
                                    // Reset pacer if lagged by more than 1 frame to prevent burst flooding
                                    nextFrameTicks = currentTicks + ticksPerFrame;
                                }

                                // Check serial output buffer backpressure (skip frame if buffer already has > 2048 bytes pending)
                                bool shouldSend = true;
                                lock (_lock)
                                {
                                    if (_serial != null && _serial.IsOpen && _serial.BytesToWrite > 2048)
                                    {
                                        shouldSend = false;
                                    }
                                }

                                if (shouldSend)
                                {
                                    var pixels = frameProvider();
                                    if (pixels != null && pixels.Length >= 128 * 64)
                                    {
                                        bool sent = SendSingleFrame(pixels);
                                        if (!sent && !IsConnected)
                                        {
                                            break;
                                        }
                                    }
                                }
                            }

                            long remainingTicks = nextFrameTicks - sw.ElapsedTicks;
                            if (remainingTicks > 0)
                            {
                                int delayMs = (int)((remainingTicks * 1000) / System.Diagnostics.Stopwatch.Frequency);
                                if (delayMs > 0)
                                {
                                    await Task.Delay(delayMs, token).ConfigureAwait(false);
                                }
                            }
                            else
                            {
                                await Task.Yield();
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (IOException)
                        {
                            HandleUnexpectedDisconnect();
                            break;
                        }
                        catch
                        {
                            // Ignore transient write errors during stream
                        }
                    }

                    lock (_lock)
                    {
                        _isStreaming = false;
                    }
                }, token);
            }
        }

        public void StopStreaming()
        {
            CancellationTokenSource? cts;
            Task? task;

            lock (_lock)
            {
                _isStreaming = false;
                cts = _streamCts;
                task = _streamTask;
                _streamCts = null;
                _streamTask = null;
            }

            if (cts != null)
            {
                try
                {
                    cts.Cancel();
                    if (task != null && task.Id != Task.CurrentId)
                    {
                        task.Wait(500, CancellationToken.None);
                    }
                }
                catch
                {
                    // Best effort cancellation
                }
                finally
                {
                    cts.Dispose();
                }
            }
        }

        private void HandleUnexpectedDisconnect()
        {
            DisconnectInternal(notify: true);

            lock (_lock)
            {
                if (!_autoReconnect || string.IsNullOrWhiteSpace(_lastPortName)) return;

                _reconnectCts = new CancellationTokenSource();
                var token = _reconnectCts.Token;

                _ = Task.Run(async () =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(1500, token);
                            if (token.IsCancellationRequested) break;

                            var portNames = SerialPort.GetPortNames();
                            bool portExists = Array.Exists(portNames, p => string.Equals(p, _lastPortName, StringComparison.OrdinalIgnoreCase));
                            if (portExists)
                            {
                                bool ok = await ConnectAsync(_lastPortName, token);
                                if (ok)
                                {
                                    if (_lastFrameProvider != null)
                                    {
                                        StartStreaming(_lastFrameProvider, _lastTargetFps);
                                    }
                                    break;
                                }
                            }
                        }
                        catch
                        {
                            // Retry on next interval
                        }
                    }
                }, token);
            }
        }

        private void CancelReconnect()
        {
            lock (_lock)
            {
                if (_reconnectCts != null)
                {
                    _reconnectCts.Cancel();
                    _reconnectCts.Dispose();
                    _reconnectCts = null;
                }
            }
        }

        public bool SendSingleFrame(bool[] pixels128x64)
        {
            if (pixels128x64 == null || pixels128x64.Length < 128 * 64)
                return false;

            lock (_lock)
            {
                Encode1024Buffer(pixels128x64, _encodeBuffer);
                return SendRawBufferInternal(_encodeBuffer);
            }
        }

        public bool SendSingleFrame(SpriteState sprite, int frameIndex = 0)
        {
            if (sprite == null) return false;
            bool[] px = sprite.CompositeFramePixels(Math.Clamp(frameIndex, 0, Math.Max(0, sprite.Frames.Count - 1)));
            if (sprite.Width != 128 || sprite.Height != 64)
            {
                px = NormalizeTo128x64(px, sprite.Width, sprite.Height);
            }
            return SendSingleFrame(px);
        }

        private bool SendRawBufferInternal(byte[] buffer)
        {
            if (_serial == null || !_serial.IsOpen) return false;

            int newFps = 0;
            bool fireFps = false;
            bool ioException = false;

            try
            {
                _serial.Write(buffer, 0, buffer.Length);
                _framesSent++;
                _framesInCurrentSec++;

                var now = DateTime.UtcNow;
                if ((now - _lastFpsCalc).TotalSeconds >= 1.0)
                {
                    _fps = _framesInCurrentSec;
                    _framesInCurrentSec = 0;
                    _lastFpsCalc = now;
                    newFps = _fps;
                    fireFps = true;
                }
            }
            catch (IOException)
            {
                ioException = true;
            }
            catch
            {
                return false;
            }

            if (fireFps)
            {
                FpsUpdated?.Invoke(this, newFps);
            }

            if (ioException)
            {
                HandleUnexpectedDisconnect();
                return false;
            }

            return true;
        }

        /// <summary>
        /// Normalizes arbitrary-dimension pixels onto a 128x64 monochrome Flipper LCD buffer (centered, zero-allocation).
        /// </summary>
        public static void NormalizeTo128x64(bool[] src, int srcWidth, int srcHeight, bool[] destination)
        {
            ArgumentNullException.ThrowIfNull(src);
            ArgumentNullException.ThrowIfNull(destination);
            if (destination.Length < 128 * 64)
                throw new ArgumentException("Destination buffer must have at least 128x64 elements.", nameof(destination));

            Array.Clear(destination, 0, 128 * 64);

            if (srcWidth == 128 && srcHeight == 64 && src.Length >= 128 * 64)
            {
                Array.Copy(src, destination, 128 * 64);
                return;
            }

            int offsetX = Math.Max(0, (128 - srcWidth) / 2);
            int offsetY = Math.Max(0, (64 - srcHeight) / 2);

            for (int y = 0; y < Math.Min(64, srcHeight); y++)
            {
                for (int x = 0; x < Math.Min(128, srcWidth); x++)
                {
                    int srcIdx = y * srcWidth + x;
                    int dstIdx = (offsetY + y) * 128 + (offsetX + x);
                    if (srcIdx < src.Length && dstIdx < destination.Length)
                    {
                        destination[dstIdx] = src[srcIdx];
                    }
                }
            }
        }

        /// <summary>
        /// Normalizes arbitrary-dimension pixels onto a 128x64 monochrome Flipper LCD buffer (centered).
        /// </summary>
        public static bool[] NormalizeTo128x64(bool[] src, int srcWidth, int srcHeight)
        {
            if (srcWidth == 128 && srcHeight == 64 && src.Length >= 128 * 64)
                return src;

            bool[] buffer = new bool[128 * 64];
            NormalizeTo128x64(src, srcWidth, srcHeight, buffer);
            return buffer;
        }

        /// <summary>
        /// Encodes 128x64 monochrome pixels into Flipper Zero's 1024-byte vertical-page framebuffer format (zero-allocation).
        /// Page 0..7 (8 pixels high) x Column 0..127.
        /// </summary>
        public static void Encode1024Buffer(bool[] pixels128x64, byte[] destination)
        {
            ArgumentNullException.ThrowIfNull(pixels128x64);
            ArgumentNullException.ThrowIfNull(destination);
            if (destination.Length < 1024)
                throw new ArgumentException("Destination buffer must have at least 1024 bytes.", nameof(destination));

            Array.Clear(destination, 0, 1024);

            for (int y = 0; y < 64; y++)
            {
                int page = y / 8;
                byte bitMask = (byte)(1 << (y % 8));

                for (int x = 0; x < 128; x++)
                {
                    int pixelIdx = y * 128 + x;
                    if (pixelIdx < pixels128x64.Length && pixels128x64[pixelIdx])
                    {
                        destination[page * 128 + x] |= bitMask;
                    }
                }
            }
        }

        /// <summary>
        /// Encodes 128x64 monochrome pixels into Flipper Zero's 1024-byte vertical-page framebuffer format.
        /// </summary>
        public static byte[] Encode1024Buffer(bool[] pixels128x64)
        {
            byte[] buffer = new byte[1024];
            Encode1024Buffer(pixels128x64, buffer);
            return buffer;
        }

        public void Dispose()
        {
            CancelReconnect();
            Disconnect();
            GC.SuppressFinalize(this);
        }
    }
}
