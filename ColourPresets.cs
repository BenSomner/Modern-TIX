using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace TixClock;

/// <summary>Ready-made colour schemes for the four blocks.</summary>
internal static class ColourPresets
{
    /// <summary>A preset's colours can depend on whether the theme is dark.</summary>
    public sealed record Preset(string Name, Func<bool, string[]> GetColours);

    public static readonly Preset[] All =
    [
        new("Classic TIX", _ => [AppSettings.DefaultHourTens, AppSettings.DefaultHourUnits,
                                 AppSettings.DefaultMinuteTens, AppSettings.DefaultMinuteUnits]),
        new("Windows accent colour", _ => Repeat(AppSettings.ToHex(AccentColour()))),
        new("Monochrome", dark => Repeat(dark ? "#F2F2F2" : "#2B2B2B")),
        new("Warm night", _ => ["#FFB300", "#FB8C00", "#F4511E", "#FFB300"]),
        new("Ocean", _ => ["#26C6DA", "#29B6F6", "#5C6BC0", "#26C6DA"]),
        new("Neon", _ => ["#FF1744", "#00E676", "#2979FF", "#FFEA00"]),
    ];

    public static void Apply(Preset preset, AppSettings settings, bool dark)
    {
        string[] colours = preset.GetColours(dark);
        for (int block = 0; block < 4; block++) settings.SetBlockColour(block, colours[block]);
    }

    /// <summary>The preset the current colours match, or -1 if they've been customised.</summary>
    public static int IndexOf(AppSettings settings, bool dark)
    {
        for (int i = 0; i < All.Length; i++)
        {
            string[] colours = All[i].GetColours(dark);
            if (Enumerable.Range(0, 4).All(b =>
                    string.Equals(colours[b], settings.GetBlockColour(b), StringComparison.OrdinalIgnoreCase)))
            {
                return i;
            }
        }
        return -1;
    }

    private static string[] Repeat(string colour) => [colour, colour, colour, colour];

    private static Color AccentColour()
    {
        // AccentColor is stored as 0xAABBGGRR.
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
        if (key?.GetValue("AccentColor") is int abgr)
        {
            return Color.FromRgb((byte)abgr, (byte)(abgr >> 8), (byte)(abgr >> 16));
        }
        Color glass = SystemParameters.WindowGlassColor;
        return Color.FromRgb(glass.R, glass.G, glass.B);
    }
}
