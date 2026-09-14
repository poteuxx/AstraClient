using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AstraClient.Interop;

/// <summary>
/// Guarded DWM interop for Windows 11 window styling: rounded corner preference,
/// system backdrop (Mica/Acrylic where supported) and legacy blur-behind.
/// Every call is best-effort: failures are swallowed so the app degrades gracefully
/// to the simulated frosted-glass look on older builds (e.g. Windows 11 22000).
/// </summary>
public static class DwmInterop
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private const int DWMWCP_DEFAULT = 0;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWCP_ROUNDSMALL = 3;

    // DWMSBT = DWM System Backdrop Type
    private const int DWMSBT_AUTO = 0;
    private const int DWMSBT_MAINWINDOW = 1;      // Mica
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DWM_BLURBEHIND blurBehind);

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_BLURBEHIND
    {
        public int dwFlags;
        public bool fEnable;
        public IntPtr hRgnBlur;
        public bool fTransitionOnMaximized;
    }

    private const int DWM_BB_ENABLE = 0x00000001;
    private const int DWM_BB_BLURREGION = 0x00000002;
    private const int DWM_BB_TRANSITIONONMAXIMIZED = 0x00000004;

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    public static IntPtr GetHwnd(Window window)
        => new WindowInteropHelper(window).Handle;

    /// <summary>Applies rounded corners (Windows 11 build 22000+). No-op elsewhere.</summary>
    public static void TryEnableRoundedCorners(Window window, bool small = false)
    {
        try
        {
            var hwnd = GetHwnd(window);
            if (hwnd == IntPtr.Zero) return;
            int pref = small ? DWMWCP_ROUNDSMALL : DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch { /* best effort */ }
    }

    /// <summary>Enables dark title bar / immersive dark mode where supported.</summary>
    public static void TryEnableDarkMode(Window window)
    {
        try
        {
            var hwnd = GetHwnd(window);
            if (hwnd == IntPtr.Zero) return;
            int useDark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
        }
        catch { }
    }

    /// <summary>Attempts a real system backdrop (Acrylic). Requires Windows 11 22621+.</summary>
    public static bool TryEnableAcrylicBackdrop(Window window)
    {
        try
        {
            var hwnd = GetHwnd(window);
            if (hwnd == IntPtr.Zero) return false;
            int backdrop = DWMSBT_TRANSIENTWINDOW;
            return DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) == 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Enables legacy DWM blur-behind across a rounded region so the real desktop shows,
    /// blurred, behind the translucent window fill. Best-effort; some configurations with
    /// AllowsTransparency may ignore it, in which case the simulated glass carries the look.
    /// </summary>
    public static void TryEnableBlurBehind(Window window, int cornerRadius = 12)
    {
        IntPtr rgn = IntPtr.Zero;
        try
        {
            var hwnd = GetHwnd(window);
            if (hwnd == IntPtr.Zero) return;

            int w = (int)Math.Max(1, window.ActualWidth);
            int h = (int)Math.Max(1, window.ActualHeight);
            rgn = CreateRoundRectRgn(0, 0, w, h, cornerRadius * 2, cornerRadius * 2);

            var bb = new DWM_BLURBEHIND
            {
                dwFlags = DWM_BB_ENABLE | DWM_BB_BLURREGION | DWM_BB_TRANSITIONONMAXIMIZED,
                fEnable = true,
                hRgnBlur = rgn,
                fTransitionOnMaximized = true
            };
            DwmEnableBlurBehindWindow(hwnd, ref bb);
        }
        catch { }
        finally
        {
            if (rgn != IntPtr.Zero) DeleteObject(rgn);
        }
    }
}
