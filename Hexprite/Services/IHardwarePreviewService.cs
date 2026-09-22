using System.Collections.Generic;

namespace Hexprite.Services
{
    /// <summary>
    /// Coarse connection status for the sidebar UI. <see cref="Streaming"/> means frames are
    /// being written successfully but no ACK has been seen (either the firmware predates the
    /// ACK protocol, or none has arrived yet). <see cref="Connected"/> means the board has
    /// positively confirmed it displayed a recent frame.
    /// </summary>
    public enum HardwarePreviewConnectionState
    {
        Disconnected,
        Connecting,
        Streaming,
        Connected,
        Error
    }

    /// <summary>A serial port, paired with a friendly device name where one could be resolved.</summary>
    public sealed record HardwarePreviewPortOption(string PortName, string DisplayName);

    public enum HardwarePreviewPlacement
    {
        TopLeft,
        Center
    }

    public enum HardwarePreviewScale
    {
        Scale1x,
        Scale2x,
        Scale4x,
        ScaleToFit
    }

    public interface IHardwarePreviewService
    {
        event System.EventHandler<bool>? EnabledChanged;
        event System.EventHandler<HardwarePreviewConnectionState>? ConnectionStateChanged;
        event System.EventHandler<(bool[] Pixels, int Width, int Height)>? FrameTransmitted;

        bool IsEnabled { get; set; }
        string? PortName { get; set; }
        int BaudRate { get; set; }

        HardwarePreviewPlacement Placement { get; set; }
        HardwarePreviewScale Scale { get; set; }
        (int Width, int Height) TargetDisplaySize { get; set; }
        (bool[]? Pixels, int Width, int Height) LastTransmittedFrame { get; }

        HardwarePreviewConnectionState ConnectionState { get; }

        /// <summary>
        /// When the service auto-disables due to an error, this contains
        /// a user-readable reason. Null when no error has occurred.
        /// </summary>
        string? LastDisableReason { get; }

        IEnumerable<string> GetAvailablePorts();

        /// <summary>
        /// Same ports as <see cref="GetAvailablePorts"/>, paired with a friendly device name
        /// where the OS can supply one (e.g. "USB-SERIAL CH340 (COM5)"). Falls back to the bare
        /// port name if the lookup is unavailable. Involves a WMI query — call off the UI thread.
        /// </summary>
        IEnumerable<HardwarePreviewPortOption> GetAvailablePortOptions();

        void SendFrame(bool[] pixels, int width, int height);

        /// <summary>
        /// Resets the connection state to Connecting, clears ACK timestamps, and updates probe timing
        /// in preparation for probing a new candidate baud rate.
        /// </summary>
        void ResetConnectionForBaudProbe();
    }
}
