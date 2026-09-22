using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Hexprite.Services
{
    /// <summary>
    /// Implements direct USB CDC communications with Flipper Zero devices for 1-click SD card deployment.
    /// Uses Flipper CLI storage commands over virtual serial COM port.
    /// </summary>
    public class FlipperUsbDeployer : IFlipperUsbDeployer
    {
        public Task<List<FlipperDeviceInfo>> ScanDevicesAsync(CancellationToken cancellationToken = default)
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
                        cancellationToken.ThrowIfCancellationRequested();
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
                    // Fallback to basic port names if WMI is unavailable
                }

                // If WMI found nothing or wasn't available, add any remaining ports as generic options
                if (devices.Count == 0)
                {
                    foreach (var port in portNames)
                    {
                        devices.Add(new FlipperDeviceInfo(port, $"Serial Device ({port})", IsConnected: false));
                    }
                }

                return devices;
            }, cancellationToken);
        }

        public async Task<bool> DeployFilesAsync(
            string portName,
            string targetBaseDirectory,
            IReadOnlyList<(string RelativePath, byte[] Data)> files,
            IProgress<FlipperDeployProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(portName) || files == null || files.Count == 0)
                return false;

            return await Task.Run(() =>
            {
                using var serial = new SerialPort(portName, 230400, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 3000,
                    WriteTimeout = 3000,
                    DtrEnable = true,
                    RtsEnable = true,
                };

                try
                {
                    serial.Open();
                    Thread.Sleep(100);

                    // 1. Enter CLI prompt cleanly by sending Ctrl+C then newline
                    serial.DiscardInBuffer();
                    serial.DiscardOutBuffer();
                    serial.Write("\x03\r\n");
                    WaitForCliPrompt(serial, 500);

                    // Pre-flight SD card check
                    progress?.Report(new FlipperDeployProgress(0.0, "", "Checking SD card status..."));
                    if (!CheckSdCardMounted(serial))
                    {
                        progress?.Report(new FlipperDeployProgress(0.0, "", "SD card (/ext) not detected or not mounted on Flipper Zero."));
                        return false;
                    }

                    // Normalize target base directory (Flipper paths use forward slashes)
                    string baseDir = targetBaseDirectory.Replace('\\', '/').TrimEnd('/');
                    if (!baseDir.StartsWith('/')) baseDir = "/" + baseDir;

                    // 2. Collect and create all unique directories
                    var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { baseDir };

                    foreach (var (relPath, _) in files)
                    {
                        string normRel = relPath.Replace('\\', '/').TrimStart('/');
                        int lastSlash = normRel.LastIndexOf('/');
                        if (lastSlash > 0)
                        {
                            string subDir = normRel[..lastSlash];
                            string[] subParts = subDir.Split('/');
                            var pathBuilder = new StringBuilder(baseDir);
                            foreach (var p in subParts)
                            {
                                pathBuilder.Append('/').Append(p);
                                directories.Add(pathBuilder.ToString());
                            }
                        }
                    }

                    // Sort directories by depth so parents are created first
                    var sortedDirs = directories.OrderBy(d => d.Length).ToList();
                    for (int i = 0; i < sortedDirs.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string dir = sortedDirs[i];
                        progress?.Report(new FlipperDeployProgress(
                            (double)i / (sortedDirs.Count + files.Count) * 100.0,
                            dir,
                            $"Creating directory: {dir}"));

                        serial.Write($"storage mkdir {dir}\r\n");
                        WaitForCliPrompt(serial, 400);
                    }

                    // 3. Upload files using binary-safe storage write_chunk (max 512 bytes per chunk)
                    const int MaxChunkSize = 512;

                    for (int i = 0; i < files.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var (relPath, data) = files[i];
                        string normRel = relPath.Replace('\\', '/').TrimStart('/');
                        string fullRemotePath = $"{baseDir}/{normRel}";

                        progress?.Report(new FlipperDeployProgress(
                            (double)(sortedDirs.Count + i) / (sortedDirs.Count + files.Count) * 100.0,
                            normRel,
                            $"Writing file: {normRel} ({data.Length} bytes)"));

                        // Remove existing file first
                        serial.Write($"storage remove {fullRemotePath}\r\n");
                        string removeResponse = WaitForCliPrompt(serial, 400);
                        if (removeResponse.Contains("error", StringComparison.OrdinalIgnoreCase)
                            && !removeResponse.Contains("not exist", StringComparison.OrdinalIgnoreCase)
                            && !removeResponse.Contains("not found", StringComparison.OrdinalIgnoreCase)
                            && !removeResponse.Contains("no such", StringComparison.OrdinalIgnoreCase))
                        {
                            // Log or report non-standard removal errors
                        }

                        // Write chunks
                        int offset = 0;
                        if (data.Length == 0)
                        {
                            // Empty file creation
                            serial.Write($"storage write_chunk {fullRemotePath} 0\r\n");
                            WaitForCliPrompt(serial, 500);
                        }
                        else
                        {
                            while (offset < data.Length)
                            {
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    try
                                    {
                                        serial.Write("\x03\r\n");
                                        Thread.Sleep(50);
                                    }
                                    catch { }
                                    cancellationToken.ThrowIfCancellationRequested();
                                }

                                int chunkSize = Math.Min(MaxChunkSize, data.Length - offset);
                                serial.Write($"storage write_chunk {fullRemotePath} {chunkSize}\r\n");

                                // Wait for Flipper to be ready for the binary chunk
                                WaitForCliPrompt(serial, 800);

                                // Send the raw binary chunk (binary-safe — no control char interpretation)
                                serial.Write(data, offset, chunkSize);
                                offset += chunkSize;

                                // Wait for write completion prompt
                                string chunkResponse = WaitForCliPrompt(serial, 2000);
                                if (chunkResponse.Contains("error", StringComparison.OrdinalIgnoreCase)
                                    || chunkResponse.Contains("failed", StringComparison.OrdinalIgnoreCase))
                                {
                                    progress?.Report(new FlipperDeployProgress(0.0, normRel, $"Storage write error on chunk at offset {offset - chunkSize} for: {normRel}"));
                                    return false;
                                }
                            }
                        }

                        // Verify file size via storage stat
                        serial.Write($"storage stat {fullRemotePath}\r\n");
                        string statResponse = WaitForCliPrompt(serial, 800);
                        if (!VerifyFileSize(statResponse, data.Length))
                        {
                            progress?.Report(new FlipperDeployProgress(0.0, normRel, $"Verification failed: file size mismatch on {normRel}"));
                            return false;
                        }
                    }

                    progress?.Report(new FlipperDeployProgress(100.0, "", "Deployment complete!"));
                    return true;
                }
                catch (OperationCanceledException)
                {
                    if (serial.IsOpen)
                    {
                        try
                        {
                            serial.Write("\x03\r\n");
                            Thread.Sleep(50);
                        }
                        catch { }
                    }
                    progress?.Report(new FlipperDeployProgress(0.0, "", "Deployment cancelled."));
                    throw;
                }
                catch (Exception ex)
                {
                    if (serial.IsOpen)
                    {
                        try
                        {
                            serial.Write("\x03\r\n");
                            Thread.Sleep(50);
                        }
                        catch { }
                    }
                    progress?.Report(new FlipperDeployProgress(0.0, "", $"Error: {ex.Message}"));
                    return false;
                }
                finally
                {
                    if (serial.IsOpen)
                    {
                        try { serial.Close(); } catch { }
                    }
                }
            }, cancellationToken);
        }

        public static bool CheckSdCardMounted(SerialPort serial)
        {
            try
            {
                serial.Write("storage info /ext\r\n");
                string response = WaitForCliPrompt(serial, 1000);
                return response.Contains("total", StringComparison.OrdinalIgnoreCase)
                    && !response.Contains("error", StringComparison.OrdinalIgnoreCase)
                    && !response.Contains("Storage error", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static bool VerifyFileSize(string statResponse, int expectedSize)
        {
            if (string.IsNullOrWhiteSpace(statResponse)) return false;
            if (statResponse.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                statResponse.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var match = System.Text.RegularExpressions.Regex.Match(
                statResponse,
                @"(?:size|length)[\s:]+(\d+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(500));

            if (match.Success && int.TryParse(match.Groups[1].Value, out int actualSize))
            {
                return actualSize == expectedSize;
            }

            // Fallback: If stat succeeded without errors
            return !statResponse.Contains("error", StringComparison.OrdinalIgnoreCase)
                && !statResponse.Contains("failed", StringComparison.OrdinalIgnoreCase);
        }

        public static string WaitForCliPrompt(SerialPort serial, int timeoutMs)
        {
            var sb = new StringBuilder();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    int avail = serial.BytesToRead;
                    if (avail > 0)
                    {
                        byte[] buf = new byte[avail];
                        int read = serial.Read(buf, 0, avail);
                        if (read > 0)
                        {
                            sb.Append(Encoding.ASCII.GetString(buf, 0, read));
                            string text = sb.ToString();
                            if (text.Contains(">:") || text.TrimEnd().EndsWith('>'))
                            {
                                break;
                            }
                        }
                    }
                    else
                    {
                        Thread.Sleep(10);
                    }
                }
                catch
                {
                    break;
                }
            }
            return sb.ToString();
        }

        public async Task<bool> RestartDesktopAsync(string portName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(portName)) return false;

            return await Task.Run(() =>
            {
                for (int retry = 0; retry < 2; retry++)
                {
                    try
                    {
                        using var serial = new SerialPort(portName, 230400, Parity.None, 8, StopBits.One)
                        {
                            ReadTimeout = 2000,
                            WriteTimeout = 2000,
                            DtrEnable = true,
                            RtsEnable = true,
                        };

                        serial.Open();
                        Thread.Sleep(50);
                        serial.DiscardInBuffer();
                        serial.Write("\x03\r\n");
                        WaitForCliPrompt(serial, 500);

                        serial.Write("loader open Desktop\r\n");
                        WaitForCliPrompt(serial, 1000);
                        return true;
                    }
                    catch
                    {
                        if (retry == 0) Thread.Sleep(200);
                    }
                }
                return false;
            }, cancellationToken);
        }
    }
}
