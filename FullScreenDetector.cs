using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace TixClock;

/// <summary>Spots a full-screen game, video or presentation on the same monitor as the clock.</summary>
internal static class FullScreenDetector
{
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    private const int QUNS_PRESENTATION_MODE = 4;

    // The desktop and taskbar cover the screen too, but aren't full-screen apps.
    private static readonly string[] ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    public static bool IsActive(Window clock)
    {
        IntPtr own = new WindowInteropHelper(clock).Handle;
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || own == IntPtr.Zero) return false;

        GetWindowThreadProcessId(foreground, out uint pid);
        if (pid == Environment.ProcessId) return false;

        var className = new StringBuilder(64);
        GetClassName(foreground, className, className.Capacity);
        if (ShellClasses.Contains(className.ToString())) return false;

        IntPtr monitor = MonitorFromWindow(foreground, MONITOR_DEFAULTTONEAREST);
        if (monitor != MonitorFromWindow(own, MONITOR_DEFAULTTONEAREST)) return false;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        // A normal maximised window can cover the screen when the taskbar auto-hides, but it keeps its title
        // bar; full-screen apps that maximise themselves are borderless.
        bool maximisedWithTitleBar = IsZoomed(foreground) &&
                                     (GetWindowLong(foreground, GWL_STYLE) & WS_CAPTION) == WS_CAPTION;

        if (GetMonitorInfo(monitor, ref info) && GetWindowRect(foreground, out RECT rect) &&
            !maximisedWithTitleBar &&
            rect.Left <= info.rcMonitor.Left && rect.Top <= info.rcMonitor.Top &&
            rect.Right >= info.rcMonitor.Right && rect.Bottom >= info.rcMonitor.Bottom)
        {
            return true;
        }

        // Exclusive-mode games and presentation mode, which don't always show up as a covering window.
        return SHQueryUserNotificationState(out int state) == 0 &&
               state is QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);
}
