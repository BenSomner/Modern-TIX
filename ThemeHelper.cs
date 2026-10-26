using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace TixClock;

/// <summary>Light/dark detection and the DWM calls behind rounded corners and Acrylic/Mica backdrops.</summary>
internal static class ThemeHelper
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_NONE = 1;
    private const int DWMSBT_MAINWINDOW = 2;       // Mica
    private const int DWMSBT_TRANSIENTWINDOW = 3;  // Acrylic

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const uint LWA_ALPHA = 0x2;

    public static readonly Color DarkBackground = Color.FromRgb(0x20, 0x20, 0x20);
    public static readonly Color LightBackground = Color.FromRgb(0xF3, 0xF3, 0xF3);

    public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    /// <summary>System backdrops arrived in Windows 11 22H2.</summary>
    public static bool SupportsBackdrop => Environment.OSVersion.Version.Build >= 22621;

    public static bool SystemUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }

    public static bool IsDark(ThemeChoice choice) => choice switch
    {
        ThemeChoice.Dark => true,
        ThemeChoice.Light => false,
        _ => !SystemUsesLightTheme(),
    };

    /// <summary>Switches the WPF Fluent theme used by menus, tooltips and the settings window.</summary>
    public static void ApplyAppTheme(ThemeChoice choice)
    {
        Application.Current.ThemeMode = choice switch
        {
            ThemeChoice.Light => ThemeMode.Light,
            ThemeChoice.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
    }

    /// <summary>The tray menu is WinForms, which picks its colour mode once at startup.</summary>
    public static void ApplyTrayColourMode(ThemeChoice choice)
    {
        System.Windows.Forms.Application.SetColorMode(choice switch
        {
            ThemeChoice.Light => System.Windows.Forms.SystemColorMode.Classic,
            ThemeChoice.Dark => System.Windows.Forms.SystemColorMode.Dark,
            _ => System.Windows.Forms.SystemColorMode.System,
        });
    }

    /// <summary>Applies dark mode, rounded corners and the backdrop. Returns true if a system backdrop is in use.</summary>
    public static bool ApplyWindowFrame(Window window, bool dark, BackdropChoice backdrop)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return false;

        int darkFlag = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkFlag, sizeof(int));

        if (IsWindows11)
        {
            int corners = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));
        }

        bool useBackdrop = SupportsBackdrop && backdrop != BackdropChoice.Solid;
        if (SupportsBackdrop)
        {
            int type = !useBackdrop ? DWMSBT_NONE
                : backdrop == BackdropChoice.Mica ? DWMSBT_MAINWINDOW
                : DWMSBT_TRANSIENTWINDOW;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int));
        }

        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
        {
            target.BackgroundColor = useBackdrop ? Colors.Transparent : dark ? DarkBackground : LightBackground;
        }
        return useBackdrop;
    }

    /// <summary>Keeps the widget out of Alt+Tab.</summary>
    public static void HideFromAltTab(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
    }

    /// <summary>
    /// Lets mouse input pass straight through to whatever is underneath. Windows only honours this on layered
    /// windows, so a normal (Acrylic/Mica) window is made layered at full opacity while it's switched on.
    /// </summary>
    public static void SetClickThrough(Window window, bool enabled, bool wpfLayered)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        int style = GetWindowLong(hwnd, GWL_EXSTYLE);
        int flags = wpfLayered ? WS_EX_TRANSPARENT : WS_EX_TRANSPARENT | WS_EX_LAYERED;
        SetWindowLong(hwnd, GWL_EXSTYLE, enabled ? style | flags : style & ~flags);
        if (enabled && !wpfLayered) SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
    }

    public static bool IsOnScreen(double left, double top) =>
        left >= SystemParameters.VirtualScreenLeft - 40 &&
        top >= SystemParameters.VirtualScreenTop - 40 &&
        left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 40 &&
        top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colourKey, byte alpha, uint flags);
}
