using System;
using Hexprite.Core;

namespace Hexprite.Services
{
    /// <summary>
    /// Singleton implementation of <see cref="IPixelClipboardService"/>.
    /// Holds a single <see cref="PixelClipboardData"/> snapshot that
    /// persists across tab switches.
    /// </summary>
    public class PixelClipboardService : IPixelClipboardService
    {
        public bool HasData => Data != null;

        public PixelClipboardData? Data { get; private set; }

        public event EventHandler? ClipboardChanged;

        public void Store(PixelClipboardData data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            ClipboardChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
