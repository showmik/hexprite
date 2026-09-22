using System;

namespace Hexprite.Services
{
    public enum AckScanResult
    {
        None,
        Ack,
        I2cError,
        ChecksumError
    }

    /// <summary>
    /// Detects the HexpritePreview firmware's single-byte frame ACK (0xA5) and diagnostic
    /// response bytes (0xE1 for I2C NACK, 0xE2 for checksum error) in an incoming serial byte stream.
    /// Has no dependency on SerialPort so it can be unit tested with plain byte arrays instead of physical hardware.
    /// </summary>
    public class AckByteScanner
    {
        public const byte AckByte = 0xA5;
        public const byte I2cErrorByte = 0xE1;
        public const byte ChecksumErrorByte = 0xE2;

        private readonly Lock _lock = new();
        private DateTime? _lastAckUtc;
        private DateTime? _lastI2cErrorUtc;
        private DateTime? _lastChecksumErrorUtc;

        public DateTime? LastAckUtc
        {
            get { lock (_lock) { return _lastAckUtc; } }
        }

        public DateTime? LastI2cErrorUtc
        {
            get { lock (_lock) { return _lastI2cErrorUtc; } }
        }

        public DateTime? LastChecksumErrorUtc
        {
            get { lock (_lock) { return _lastChecksumErrorUtc; } }
        }

        /// <summary>Feeds one incoming byte. Returns scan result.</summary>
        public AckScanResult Feed(byte b) => Feed(b, DateTime.UtcNow);

        internal AckScanResult Feed(byte b, DateTime nowUtc)
        {
            lock (_lock)
            {
                if (b == AckByte)
                {
                    _lastAckUtc = nowUtc;
                    return AckScanResult.Ack;
                }
                if (b == I2cErrorByte)
                {
                    _lastI2cErrorUtc = nowUtc;
                    return AckScanResult.I2cError;
                }
                if (b == ChecksumErrorByte)
                {
                    _lastChecksumErrorUtc = nowUtc;
                    return AckScanResult.ChecksumError;
                }
                return AckScanResult.None;
            }
        }

        /// <summary>Feeds a buffer of incoming bytes. Returns the highest priority result found (I2cError > ChecksumError > Ack > None).</summary>
        public AckScanResult Feed(byte[] buffer, int offset, int count)
        {
            AckScanResult highest = AckScanResult.None;
            for (int i = offset; i < offset + count; i++)
            {
                var res = Feed(buffer[i]);
                if (res == AckScanResult.I2cError) highest = AckScanResult.I2cError;
                else if (res == AckScanResult.ChecksumError && highest != AckScanResult.I2cError) highest = AckScanResult.ChecksumError;
                else if (res == AckScanResult.Ack && highest == AckScanResult.None) highest = AckScanResult.Ack;
            }
            return highest;
        }

        public void Reset()
        {
            lock (_lock)
            {
                _lastAckUtc = null;
                _lastI2cErrorUtc = null;
                _lastChecksumErrorUtc = null;
            }
        }

        /// <summary>True if an ACK was seen within <paramref name="window"/> of <paramref name="nowUtc"/>.</summary>
        public bool IsRecentlyAcked(TimeSpan window, DateTime nowUtc)
        {
            lock (_lock)
            {
                return _lastAckUtc.HasValue && (nowUtc - _lastAckUtc.Value) <= window;
            }
        }

        /// <summary>True if an I2C error was seen within <paramref name="window"/> of <paramref name="nowUtc"/>.</summary>
        public bool IsRecentI2cError(TimeSpan window, DateTime nowUtc)
        {
            lock (_lock)
            {
                return _lastI2cErrorUtc.HasValue && (nowUtc - _lastI2cErrorUtc.Value) <= window;
            }
        }
    }
}
