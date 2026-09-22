using System;
using Hexprite.Core;

namespace Hexprite.Services
{
    /// <summary>
    /// Application-level clipboard for pixel selection data.
    /// Shared as a singleton across all document tabs so that
    /// copy in one tab and paste in another works seamlessly.
    /// </summary>
    public interface IPixelClipboardService
    {
        /// <summary>Returns true if clipboard contains pixel data.</summary>
        bool HasData { get; }

        /// <summary>The current clipboard content, or null if empty.</summary>
        PixelClipboardData? Data { get; }

        /// <summary>Stores new pixel data on the clipboard.</summary>
        void Store(PixelClipboardData data);

        /// <summary>Raised after <see cref="Store"/> is called.</summary>
        event EventHandler? ClipboardChanged;
    }
}
