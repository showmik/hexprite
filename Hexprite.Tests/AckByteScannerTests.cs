using System;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class AckByteScannerTests
    {
        [Fact]
        public void Feed_AckByte_ReturnsAckAndRecordsTimestamp()
        {
            var scanner = new AckByteScanner();
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            AckScanResult result = scanner.Feed(AckByteScanner.AckByte, now);

            Assert.Equal(AckScanResult.Ack, result);
            Assert.Equal(now, scanner.LastAckUtc);
        }

        [Fact]
        public void Feed_NonAckByte_ReturnsNoneAndDoesNotRecord()
        {
            var scanner = new AckByteScanner();

            AckScanResult result = scanner.Feed(0x00, DateTime.UtcNow);

            Assert.Equal(AckScanResult.None, result);
            Assert.Null(scanner.LastAckUtc);
            Assert.Null(scanner.LastI2cErrorUtc);
            Assert.Null(scanner.LastChecksumErrorUtc);
        }

        [Fact]
        public void Feed_I2cErrorByte_ReturnsI2cErrorAndRecordsTimestamp()
        {
            var scanner = new AckByteScanner();
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            AckScanResult result = scanner.Feed(AckByteScanner.I2cErrorByte, now);

            Assert.Equal(AckScanResult.I2cError, result);
            Assert.Equal(now, scanner.LastI2cErrorUtc);
            Assert.Null(scanner.LastAckUtc);
        }

        [Fact]
        public void Feed_ChecksumErrorByte_ReturnsChecksumErrorAndRecordsTimestamp()
        {
            var scanner = new AckByteScanner();
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            AckScanResult result = scanner.Feed(AckByteScanner.ChecksumErrorByte, now);

            Assert.Equal(AckScanResult.ChecksumError, result);
            Assert.Equal(now, scanner.LastChecksumErrorUtc);
            Assert.Null(scanner.LastAckUtc);
        }

        [Fact]
        public void Feed_Buffer_DetectsAckAmongOtherBytes()
        {
            var scanner = new AckByteScanner();
            byte[] buffer = { 0x01, 0x02, AckByteScanner.AckByte, 0x03 };

            AckScanResult result = scanner.Feed(buffer, 0, buffer.Length);

            Assert.Equal(AckScanResult.Ack, result);
            Assert.NotNull(scanner.LastAckUtc);
        }

        [Fact]
        public void Feed_BufferWithNoAck_ReturnsNone()
        {
            var scanner = new AckByteScanner();
            byte[] buffer = { 0x01, 0x02, 0x03 };

            AckScanResult result = scanner.Feed(buffer, 0, buffer.Length);

            Assert.Equal(AckScanResult.None, result);
        }

        [Fact]
        public void Feed_Buffer_DetectsI2cErrorWithPriority()
        {
            var scanner = new AckByteScanner();
            // Buffer containing both ACK, checksum error, and I2C error. I2C error should have highest priority.
            byte[] buffer = { 0x01, AckByteScanner.AckByte, AckByteScanner.ChecksumErrorByte, AckByteScanner.I2cErrorByte };

            AckScanResult result = scanner.Feed(buffer, 0, buffer.Length);

            Assert.Equal(AckScanResult.I2cError, result);
            Assert.NotNull(scanner.LastI2cErrorUtc);
        }

        [Fact]
        public void Feed_Buffer_DetectsChecksumErrorOverAck()
        {
            var scanner = new AckByteScanner();
            byte[] buffer = { AckByteScanner.AckByte, AckByteScanner.ChecksumErrorByte };

            AckScanResult result = scanner.Feed(buffer, 0, buffer.Length);

            Assert.Equal(AckScanResult.ChecksumError, result);
            Assert.NotNull(scanner.LastChecksumErrorUtc);
        }

        [Fact]
        public void Reset_ClearsAllTimestamps()
        {
            var scanner = new AckByteScanner();
            scanner.Feed(AckByteScanner.AckByte, DateTime.UtcNow);
            scanner.Feed(AckByteScanner.I2cErrorByte, DateTime.UtcNow);
            scanner.Feed(AckByteScanner.ChecksumErrorByte, DateTime.UtcNow);

            scanner.Reset();

            Assert.Null(scanner.LastAckUtc);
            Assert.Null(scanner.LastI2cErrorUtc);
            Assert.Null(scanner.LastChecksumErrorUtc);
        }

        [Fact]
        public void IsRecentlyAcked_WithinWindow_ReturnsTrue()
        {
            var scanner = new AckByteScanner();
            var ackTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            scanner.Feed(AckByteScanner.AckByte, ackTime);

            bool result = scanner.IsRecentlyAcked(TimeSpan.FromSeconds(3), ackTime.AddSeconds(2));

            Assert.True(result);
        }

        [Fact]
        public void IsRecentlyAcked_OutsideWindow_ReturnsFalse()
        {
            var scanner = new AckByteScanner();
            var ackTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            scanner.Feed(AckByteScanner.AckByte, ackTime);

            bool result = scanner.IsRecentlyAcked(TimeSpan.FromSeconds(3), ackTime.AddSeconds(4));

            Assert.False(result);
        }

        [Fact]
        public void IsRecentlyAcked_NeverAcked_ReturnsFalse()
        {
            var scanner = new AckByteScanner();

            bool result = scanner.IsRecentlyAcked(TimeSpan.FromSeconds(3), DateTime.UtcNow);

            Assert.False(result);
        }

        [Fact]
        public void IsRecentI2cError_WithinWindow_ReturnsTrue()
        {
            var scanner = new AckByteScanner();
            var errTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            scanner.Feed(AckByteScanner.I2cErrorByte, errTime);

            bool result = scanner.IsRecentI2cError(TimeSpan.FromSeconds(3), errTime.AddSeconds(2));

            Assert.True(result);
        }

        [Fact]
        public void IsRecentI2cError_OutsideWindow_ReturnsFalse()
        {
            var scanner = new AckByteScanner();
            var errTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            scanner.Feed(AckByteScanner.I2cErrorByte, errTime);

            bool result = scanner.IsRecentI2cError(TimeSpan.FromSeconds(3), errTime.AddSeconds(4));

            Assert.False(result);
        }

        [Fact]
        public void IsRecentI2cError_NeverError_ReturnsFalse()
        {
            var scanner = new AckByteScanner();

            bool result = scanner.IsRecentI2cError(TimeSpan.FromSeconds(3), DateTime.UtcNow);

            Assert.False(result);
        }
    }
}
