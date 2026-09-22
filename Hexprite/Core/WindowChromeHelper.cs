using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Hexprite.Core
{
    public static partial class WindowChromeHelper
    {
        // Many systems report older build numbers if app.manifest isn't fully updated, 
        // but Windows 11 generally reports >= 22000 if manifested.
        public static bool IsWindows11OrGreater => Environment.OSVersion.Version.Build >= 22000;

        // Windows 11 natively provides shadows and rounded borders even with WindowStyle=None.
        // Windows 10 loses them with WindowStyle=None, so we provide a fallback 2px border.
        public static Thickness FallbackBorderThickness => IsWindows11OrGreater ? new Thickness(0) : new Thickness(2);

        [LibraryImport("dwmapi.dll")]
        private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public static readonly DependencyProperty ForceRoundedCornersProperty =
            DependencyProperty.RegisterAttached(
                "ForceRoundedCorners", typeof(bool), typeof(WindowChromeHelper),
                new PropertyMetadata(defaultValue: false, OnForceRoundedCornersChanged));

        public static void SetForceRoundedCorners(DependencyObject element, bool value)
            => element.SetValue(ForceRoundedCornersProperty, value);

        public static bool GetForceRoundedCorners(DependencyObject element)
            => (bool)element.GetValue(ForceRoundedCornersProperty);

        private static void OnForceRoundedCornersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Window window && (bool)e.NewValue)
            {
                if (window.IsLoaded)
                    ApplyRoundedCorners(window);
                else
                    window.SourceInitialized += (s, _) =>
                    {
                        if (s is Window w) ApplyRoundedCorners(w);
                    };
            }
        }

        private static void ApplyRoundedCorners(Window window)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
                int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
                int DWMWCP_ROUND = 2; // 2 = Round
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref DWMWCP_ROUND, sizeof(int));
            }
            catch { }
        }
    }
}
