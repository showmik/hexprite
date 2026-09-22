using System.Windows;

namespace Hexprite.Services
{
    /// <summary>
    /// Provides access to the system clipboard for text operations.
    /// </summary>
    public class ClipboardService : IClipboardService
    {
        /// <summary>Sets the clipboard text with retries for transient clipboard locks.</summary>
        public void SetText(string text)
        {
            if (text == null) return;
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    Clipboard.SetDataObject(text, true);
                    return;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    if (i == 2) throw;
                    System.Threading.Thread.Sleep(50);
                }
            }
        }
    }
}