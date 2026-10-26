using Microsoft.Win32;

namespace TixClock;

/// <summary>"Start with Windows" via the current user's Run key.</summary>
internal static class StartupHelper
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Modern TIX";

    /// <summary>The entry's name before the app was renamed from "TIX Clock".</summary>
    private const string OldValueName = "TIX Clock";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// If the exe has moved since startup was enabled, point the Run entry at its new location. An entry left
    /// from before the rename (which points at the old TixClock.exe) is replaced with one under the new name.
    /// </summary>
    public static void RefreshPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return;

        if (key.GetValue(OldValueName) is string)
        {
            key.DeleteValue(OldValueName, throwOnMissingValue: false);
            key.SetValue(ValueName, Command);
        }
        else if (key.GetValue(ValueName) is string current && current != Command)
        {
            key.SetValue(ValueName, Command);
        }
    }
}
