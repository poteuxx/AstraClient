using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace AstraClient.Interop;

/// <summary>
/// Attached behavior that makes a borderless WindowChrome window behave correctly:
/// constrains maximized bounds to the monitor work area (prevents edge overflow/clipping),
/// applies Windows 11 rounded corners + dark mode, and toggles corner rounding on maximize.
/// Usage: interop:WindowBehavior.Enable="True" on the Window.
/// </summary>
public static class WindowBehavior
{
    public static readonly DependencyProperty EnableProperty =
        DependencyProperty.RegisterAttached(
            "Enable", typeof(bool), typeof(WindowBehavior),
            new PropertyMetadata(false, OnEnableChanged));

    public static void SetEnable(DependencyObject d, bool value) => d.SetValue(EnableProperty, value);
    public static bool GetEnable(DependencyObject d) => (bool)d.GetValue(EnableProperty);

    private const int WM_GETMINMAXINFO = 0x0024;
    private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window || !(bool)e.NewValue)
            return;

        if (window.IsLoaded)
            Hook(window);
        else
            window.SourceInitialized += (_, _) => Hook(window);
    }

    private static void Hook(Window window)
    {
        DwmInterop.TryEnableRoundedCorners(window);
        DwmInterop.TryEnableDarkMode(window);

        var hwnd = DwmInterop.GetHwnd(window);
        if (hwnd == IntPtr.Zero) return;

        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        window.StateChanged += (_, _) => OnStateChanged(window);
        OnStateChanged(window);
    }

    private static void OnStateChanged(Window window)
    {
        bool maximized = window.WindowState == WindowState.Maximized;
        if (window.Content is DependencyObject root)
        {
            var border = FindRootBorder(root);
            if (border != null)
                border.CornerRadius = new CornerRadius(maximized ? 0 : 12);
        }
    }

    private static System.Windows.Controls.Border? FindRootBorder(DependencyObject root)
    {
        if (root is System.Windows.Controls.Border b &&
            (b.Name == "RootBorder" || b.Tag as string == "RootBorder"))
            return b;

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var found = FindRootBorder(VisualTreeHelper.GetChild(root, i));
            if (found != null) return found;
        }
        return null;
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            AdjustMaximizedBounds(hwnd, lParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static void AdjustMaximizedBounds(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

        IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(monitor, ref monitorInfo))
            {
                RECT rcWork = monitorInfo.rcWork;
                RECT rcMonitor = monitorInfo.rcMonitor;
                mmi.ptMaxPosition.X = rcWork.Left - rcMonitor.Left;
                mmi.ptMaxPosition.Y = rcWork.Top - rcMonitor.Top;
                mmi.ptMaxSize.X = rcWork.Right - rcWork.Left;
                mmi.ptMaxSize.Y = rcWork.Bottom - rcWork.Top;
            }
        }

        Marshal.StructureToPtr(mmi, lParam, true);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }
}
