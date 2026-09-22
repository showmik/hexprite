using System;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Integration")]
    public class HardwarePreviewServiceTests : IDisposable
    {
        private HardwarePreviewService _service;

        public HardwarePreviewServiceTests()
        {
            _service = new HardwarePreviewService();
            // Enable the service to bypass the early return in SendFrame.
            // Leaving PortName null prevents actual serial port initialization.
            _service.IsEnabled = true;
        }

        public void Dispose()
        {
            _service?.Dispose();
        }

        [Fact]
        public void SendFrame_ZeroWidth_DoesNotThrow()
        {
            var pixels = new bool[100];
            // If the regression occurs, this might throw or cause downstream issues.
            // With our fix, it immediately returns.
            _service.SendFrame(pixels, 0, 10);
        }

        [Fact]
        public void SendFrame_ZeroHeight_DoesNotThrow()
        {
            var pixels = new bool[100];
            _service.SendFrame(pixels, 10, 0);
        }

        [Fact]
        public void SendFrame_WidthExceeds16BitLimit_DoesNotThrow()
        {
            // 65536 is too large for the 16-bit protocol.
            // The method should safely ignore it.
            var pixels = new bool[1]; 
            _service.SendFrame(pixels, 65536, 1);
        }

        [Fact]
        public void SendFrame_HeightExceeds16BitLimit_DoesNotThrow()
        {
            var pixels = new bool[1]; 
            _service.SendFrame(pixels, 1, 65536);
        }

        [Fact]
        public void SendFrame_ArraySizeSmallerThanDimensions_SafelyIgnores()
        {
            // Providing an array of size 10 for a 10x10 canvas (requires 100).
            // Should not throw IndexOutOfRangeException during snapshot.
            var pixels = new bool[10];
            _service.SendFrame(pixels, 10, 10);
        }

        [Fact]
        public void SendFrame_ValidFrame_DoesNotThrow()
        {
            // A valid frame should successfully snapshot and signal the channel.
            var pixels = new bool[100];
            _service.SendFrame(pixels, 10, 10);
        }

        [Fact]
        public void ProcessScanResult_I2cError_SetsErrorStateAndReason_WhenFrameWasSent()
        {
            _service.LastFrameSentUtc = DateTime.UtcNow;
            _service.ProcessScanResult(AckScanResult.I2cError);

            Assert.Equal(HardwarePreviewConnectionState.Error, _service.ConnectionState);
            Assert.NotNull(_service.LastDisableReason);
            Assert.Contains("SDA/SCL", _service.LastDisableReason);
        }

        [Fact]
        public void ProcessScanResult_I2cError_WhenNoRecentFrame_IgnoresBootloaderOrNoiseBytes()
        {
            // No frame sent yet (e.g. bootloader noise or user serial logging)
            _service.LastFrameSentUtc = null;
            _service.ProcessScanResult(AckScanResult.I2cError);

            // Should remain in Disconnected state, not Error
            Assert.NotEqual(HardwarePreviewConnectionState.Error, _service.ConnectionState);
            Assert.Null(_service.LastDisableReason);
        }

        [Fact]
        public void ProcessScanResult_Ack_ClearsI2cErrorAndConnects()
        {
            // First trigger I2C error with recent frame
            _service.LastFrameSentUtc = DateTime.UtcNow;
            _service.ProcessScanResult(AckScanResult.I2cError);
            Assert.Equal(HardwarePreviewConnectionState.Error, _service.ConnectionState);

            // Now receive ACK
            _service.ProcessScanResult(AckScanResult.Ack);
            Assert.Equal(HardwarePreviewConnectionState.Connected, _service.ConnectionState);
            Assert.Null(_service.LastDisableReason);
        }

        [Fact]
        public void ProcessIncomingBytes_BufferWithI2cError_SetsErrorState()
        {
            _service.LastFrameSentUtc = DateTime.UtcNow;
            byte[] incoming = [0x00, AckByteScanner.I2cErrorByte, 0x01];
            _service.ProcessIncomingBytes(incoming, 0, incoming.Length);

            Assert.Equal(HardwarePreviewConnectionState.Error, _service.ConnectionState);
            Assert.Contains("Display not responding", _service.LastDisableReason ?? string.Empty);
        }

        [Fact]
        public void ProcessScanResult_ChecksumError_DoesNotCrashOrSetErrorState()
        {
            _service.ProcessScanResult(AckScanResult.Ack);
            Assert.Equal(HardwarePreviewConnectionState.Connected, _service.ConnectionState);

            _service.ProcessScanResult(AckScanResult.ChecksumError);
            Assert.Equal(HardwarePreviewConnectionState.Connected, _service.ConnectionState);
        }

        [Fact]
        public void AckWatchdogTick_WhenConnectedAndIdle_DoesNotDowngradeToStreaming()
        {
            // Connect via ACK
            _service.ProcessScanResult(AckScanResult.Ack);
            Assert.Equal(HardwarePreviewConnectionState.Connected, _service.ConnectionState);

            // Tick watchdog when no frames have been sent
            _service.AckWatchdogTick(null);

            // Should remain Connected because user was simply idle (no unacknowledged frames)
            Assert.Equal(HardwarePreviewConnectionState.Connected, _service.ConnectionState);
        }

        [Fact]
        public void PortName_Change_DoesNotBlockCallingThread()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // Switching port names should return immediately (< 100ms) without blocking the thread
            _service.PortName = "COM99";
            _service.PortName = "COM98";
            _service.PortName = "COM97";
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 500, $"PortName changes took {sw.ElapsedMilliseconds}ms, expected < 500ms");
        }

        [Fact]
        public void ConnectingState_TimesOutToError_WhenNoResponseWithinWindow()
        {
            _service.SetPortNameForTesting("COM42");
            _service.SetConnectionState(HardwarePreviewConnectionState.Connecting);
            _service.ConnectingStartedUtc = DateTime.UtcNow - TimeSpan.FromSeconds(6);

            _service.AckWatchdogTick(null);

            Assert.Equal(HardwarePreviewConnectionState.Error, _service.ConnectionState);
            Assert.NotNull(_service.LastDisableReason);
            Assert.Contains("No response on COM42", _service.LastDisableReason);
        }

        [Fact]
        public void ConnectingState_DoesNotTimeout_WhenWithinWindow()
        {
            _service.SetPortNameForTesting("COM42");
            _service.SetConnectionState(HardwarePreviewConnectionState.Connecting);
            _service.ConnectingStartedUtc = DateTime.UtcNow - TimeSpan.FromSeconds(2);

            _service.AckWatchdogTick(null);

            Assert.Equal(HardwarePreviewConnectionState.Connecting, _service.ConnectionState);
        }

        [Fact]
        public void ProcessScanResult_Ack_ClearsTimeoutErrorAndConnects()
        {
            _service.SetPortNameForTesting("COM42");
            _service.SetConnectionState(HardwarePreviewConnectionState.Connecting);
            _service.ConnectingStartedUtc = DateTime.UtcNow - TimeSpan.FromSeconds(6);
            _service.AckWatchdogTick(null);

            Assert.Equal(HardwarePreviewConnectionState.Error, _service.ConnectionState);
            Assert.NotNull(_service.LastDisableReason);

            // Device responds with ACK
            _service.ProcessScanResult(AckScanResult.Ack);

            Assert.Equal(HardwarePreviewConnectionState.Connected, _service.ConnectionState);
            Assert.Null(_service.LastDisableReason);
        }

        [Fact]
        public void SendFrame_DuringPortChange_DoesNotThrowOrDeadlock()
        {
            var pixels = new bool[100];
            for (int i = 0; i < 20; i++)
            {
                _service.PortName = $"COM{i}";
                _service.SendFrame(pixels, 10, 10);
            }
        }

        [Fact]
        public void StreamingState_ContinuesStreaming_WhenNoAckReceived()
        {
            _service.SetPortNameForTesting("COM42");
            _service.BaudRate = 115200;
            _service.SetConnectionState(HardwarePreviewConnectionState.Streaming);
            _service.LastFrameSentUtc = DateTime.UtcNow - TimeSpan.FromSeconds(6);

            _service.AckWatchdogTick(null);

            // Streaming must NOT be interrupted or downgraded to Error when no ACK is received;
            // one-way streams and sketches without ACK support must continue working.
            Assert.Equal(HardwarePreviewConnectionState.Streaming, _service.ConnectionState);
        }

        [Fact]
        public void StreamingState_UpgradesToConnected_WhenAckReceived()
        {
            _service.SetPortNameForTesting("COM42");
            _service.SetConnectionState(HardwarePreviewConnectionState.Streaming);

            _service.ProcessIncomingBytes([AckByteScanner.AckByte], 0, 1);

            Assert.Equal(HardwarePreviewConnectionState.Connected, _service.ConnectionState);
        }

        [Fact]
        public void StreamingState_DoesNotTimeout_WhenWithinWindow()
        {
            _service.SetPortNameForTesting("COM42");
            _service.SetConnectionState(HardwarePreviewConnectionState.Streaming);
            _service.LastFrameSentUtc = DateTime.UtcNow - TimeSpan.FromSeconds(2);

            _service.AckWatchdogTick(null);

            Assert.Equal(HardwarePreviewConnectionState.Streaming, _service.ConnectionState);
        }

        [Fact]
        public void TransformFrame_1x_TopLeft_ReturnsOriginal()
        {
            _service.Placement = HardwarePreviewPlacement.TopLeft;
            _service.Scale = HardwarePreviewScale.Scale1x;
            _service.TargetDisplaySize = (16, 16);

            bool[] src = new bool[16 * 16];
            src[0] = true;
            src[15] = true;

            var (transformed, outW, outH) = _service.TransformFrame(src, 16, 16);

            Assert.Equal(16, outW);
            Assert.Equal(16, outH);
            Assert.True(transformed[0]);
            Assert.True(transformed[15]);
        }

        [Fact]
        public void TransformFrame_2x_Center_ScalesAndCentersOnTargetDisplay()
        {
            _service.Placement = HardwarePreviewPlacement.Center;
            _service.Scale = HardwarePreviewScale.Scale2x;
            _service.TargetDisplaySize = (8, 8);

            // 2x2 sprite: top-left pixel is ON, bottom-right is ON
            // X .
            // . X
            bool[] src = [true, false, false, true];

            var (transformed, outW, outH) = _service.TransformFrame(src, 2, 2);

            Assert.Equal(8, outW);
            Assert.Equal(8, outH);

            // Scaled 2x2 with 2x scale becomes 4x4.
            // On 8x8 display centered:
            // OffsetX = (8 - 4) / 2 = 2
            // OffsetY = (8 - 4) / 2 = 2
            // Original (0,0) scaled 2x -> (2,2), (2,3), (3,2), (3,3)
            Assert.True(transformed[2 * 8 + 2]);
            Assert.True(transformed[2 * 8 + 3]);
            Assert.True(transformed[3 * 8 + 2]);
            Assert.True(transformed[3 * 8 + 3]);

            // Original (1,1) scaled 2x -> (4,4), (4,5), (5,4), (5,5)
            Assert.True(transformed[4 * 8 + 4]);
            Assert.True(transformed[4 * 8 + 5]);
            Assert.True(transformed[5 * 8 + 4]);
            Assert.True(transformed[5 * 8 + 5]);

            // Background pixels should be false
            Assert.False(transformed[0]); // (0,0)
            Assert.False(transformed[7 * 8 + 7]); // (7,7)
        }

        [Fact]
        public void TransformFrame_ScaleToFit_ComputesIntegerRatio()
        {
            _service.Placement = HardwarePreviewPlacement.Center;
            _service.Scale = HardwarePreviewScale.ScaleToFit;
            _service.TargetDisplaySize = (128, 64);

            // 16x16 sprite on 128x64 display.
            // Scale should be min(128/16, 64/16) = min(8, 4) = 4.
            bool[] src = new bool[16 * 16];
            src[0] = true;

            var (transformed, outW, outH) = _service.TransformFrame(src, 16, 16);

            Assert.Equal(128, outW);
            Assert.Equal(64, outH);

            // Scaled size is 64x64.
            // OffsetX = (128 - 64)/2 = 32, OffsetY = (64 - 64)/2 = 0.
            // Pixel (0,0) scaled 4x should cover X: [32..35], Y: [0..3].
            for (int dy = 0; dy < 4; dy++)
            {
                for (int dx = 0; dx < 4; dx++)
                {
                    Assert.True(transformed[dy * 128 + (32 + dx)]);
                }
            }
        }
    }
}
