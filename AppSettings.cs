using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace TixClock;

public enum ThemeChoice { System, Light, Dark }

public enum BackdropChoice { Acrylic, Mica, Solid }

public enum LedShape { Square, Round }

/// <summary>User preferences, stored as JSON in %APPDATA%\ModernTIX\settings.json (or next to the exe when portable).</summary>
public sealed class AppSettings
{
    public const string DefaultHourTens = "#E53935";
    public const string DefaultHourUnits = "#43A047";
    public const string DefaultMinuteTens = "#1E88E5";
    public const string DefaultMinuteUnits = "#E53935";

    public string HourTensColour { get; set; } = DefaultHourTens;
    public string HourUnitsColour { get; set; } = DefaultHourUnits;
    public string MinuteTensColour { get; set; } = DefaultMinuteTens;
    public string MinuteUnitsColour { get; set; } = DefaultMinuteUnits;

    /// <summary>Colour of unlit squares; null follows the theme.</summary>
    public string? OffColour { get; set; }

    public int UpdateIntervalSeconds { get; set; } = 4;
    // Defaults follow the original hardware: 12-hour time and instant pattern changes.
    public bool Use24Hour { get; set; }
    public double Brightness { get; set; } = 1.0;
    public double Scale { get; set; } = 1.0;

    /// <summary>Clockwise rotation in degrees: 0, 90, 180 or 270.</summary>
    public int Rotation { get; set; }

    /// <summary>Glow around lit squares, 0 (none) to 1 (full). The glow's size is fixed; this sets its intensity.</summary>
    public double GlowStrength { get; set; } = 0.7;

    /// <summary>Early builds stored the glow as an on/off "Glow" setting; "off" carries over as 0%.</summary>
    [JsonPropertyName("Glow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyGlow
    {
        get => null;
        set { if (value == false) GlowStrength = 0; }
    }

    public LedShape Shape { get; set; } = LedShape.Square;
    public bool SmoothTransitions { get; set; }
    public bool ShowTooltip { get; set; } = true;
    public bool ShowDateInTooltip { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = true;
    public bool LockPosition { get; set; }
    public bool ClickThrough { get; set; }
    public bool HideWhenFullScreen { get; set; } = true;
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;
    public BackdropChoice Backdrop { get; set; } = BackdropChoice.Acrylic;

    /// <summary>Opacity of the solid background; 0 leaves just the LEDs floating on the desktop.</summary>
    public double BackgroundOpacity { get; set; } = 1.0;

    public bool NightDimming { get; set; }
    public string NightStart { get; set; } = "22:00";
    public string NightEnd { get; set; } = "07:00";
    public double NightBrightness { get; set; } = 0.35;

    /// <summary>The settings window reopens on the tab used last, including after a restart.</summary>
    public int SettingsTab { get; set; }

    public double? Left { get; set; }
    public double? Top { get; set; }

    private static readonly string AppDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    private static readonly string FolderPath = Path.Combine(AppDataFolder, "ModernTIX");

    private static readonly string AppDataFilePath = Path.Combine(FolderPath, "settings.json");

    /// <summary>If this file exists next to the exe, the app is in portable mode and uses it instead of %APPDATA%.</summary>
    public static readonly string PortableFilePath = Path.Combine(AppContext.BaseDirectory, "ModernTIX.settings.json");

    // Where settings lived before the app was renamed from "TIX Clock".
    private static readonly string OldFolderPath = Path.Combine(AppDataFolder, "TixClock");
    private static readonly string OldPortableFilePath = Path.Combine(AppContext.BaseDirectory, "TixClock.settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Where settings are read from and saved to.</summary>
    public static string FilePath { get; private set; } = AppDataFilePath;

    public static bool IsPortable => FilePath == PortableFilePath;

    public static AppSettings Load()
    {
        MoveSettingsFromOldName();
        FilePath = File.Exists(PortableFilePath) ? PortableFilePath : AppDataFilePath;

        AppSettings settings;
        try
        {
            settings = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            settings = new();
        }
        settings.Normalise();
        return settings;
    }

    public void Save()
    {
        try
        {
            Write(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth interrupting the user over; the next save will try again.
        }
    }

    /// <summary>
    /// Moves the settings next to the exe, or back to %APPDATA%. Throws if the new location can't be written,
    /// e.g. when the app is in Program Files, in which case nothing changes.
    /// </summary>
    public void SetPortable(bool portable)
    {
        string target = portable ? PortableFilePath : AppDataFilePath;
        if (target == FilePath) return;

        Write(target);
        string previous = FilePath;
        FilePath = target;

        // The portable file is what switches portable mode on, so it has to go when switching back.
        // The %APPDATA% copy is left alone when going portable; it's simply not read any more.
        if (!portable) File.Delete(previous);
    }

    /// <summary>Carries settings over from the "TIX Clock" locations, unless the new ones already exist.</summary>
    private static void MoveSettingsFromOldName()
    {
        try
        {
            if (File.Exists(OldPortableFilePath) && !File.Exists(PortableFilePath))
            {
                File.Move(OldPortableFilePath, PortableFilePath);
            }

            string oldFile = Path.Combine(OldFolderPath, "settings.json");
            if (File.Exists(oldFile) && !File.Exists(AppDataFilePath))
            {
                Directory.CreateDirectory(FolderPath);
                File.Move(oldFile, AppDataFilePath);
                if (!Directory.EnumerateFileSystemEntries(OldFolderPath).Any()) Directory.Delete(OldFolderPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left where they are; the app starts with defaults rather than failing.
        }
    }

    private void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    public void ResetColours()
    {
        HourTensColour = DefaultHourTens;
        HourUnitsColour = DefaultHourUnits;
        MinuteTensColour = DefaultMinuteTens;
        MinuteUnitsColour = DefaultMinuteUnits;
        OffColour = null;
    }

    public Color[] BlockColours() =>
    [
        ParseColour(HourTensColour, DefaultHourTens),
        ParseColour(HourUnitsColour, DefaultHourUnits),
        ParseColour(MinuteTensColour, DefaultMinuteTens),
        ParseColour(MinuteUnitsColour, DefaultMinuteUnits),
    ];

    public string GetBlockColour(int block) => block switch
    {
        0 => HourTensColour,
        1 => HourUnitsColour,
        2 => MinuteTensColour,
        _ => MinuteUnitsColour,
    };

    public void SetBlockColour(int block, string hex)
    {
        switch (block)
        {
            case 0: HourTensColour = hex; break;
            case 1: HourUnitsColour = hex; break;
            case 2: MinuteTensColour = hex; break;
            default: MinuteUnitsColour = hex; break;
        }
    }

    public static Color ParseColour(string? hex, string fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex) is Color c) return c;
        }
        catch (FormatException)
        {
        }
        return (Color)ColorConverter.ConvertFromString(fallback);
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Acrylic and Mica need a normal window; everything else uses a per-pixel transparent one.</summary>
    [JsonIgnore]
    public bool UsesSystemBackdrop => ThemeHelper.SupportsBackdrop && Backdrop != BackdropChoice.Solid;

    /// <summary>True between NightStart and NightEnd, including periods that cross midnight.</summary>
    public bool IsNightTime(DateTime now)
    {
        var time = TimeOnly.FromDateTime(now);
        var start = TimeOnly.ParseExact(NightStart, "HH:mm");
        var end = TimeOnly.ParseExact(NightEnd, "HH:mm");
        return start <= end
            ? time >= start && time < end
            : time >= start || time < end;
    }

    public double EffectiveBrightness(DateTime now) =>
        NightDimming && IsNightTime(now) ? NightBrightness : Brightness;

    /// <summary>Times are chosen in half-hour steps, so anything else is rounded to the nearest one.</summary>
    private static string NormaliseTime(string? value, string fallback)
    {
        if (!TimeOnly.TryParse(value, out var time)) time = TimeOnly.ParseExact(fallback, "HH:mm");
        int halfHours = (int)Math.Round(time.ToTimeSpan().TotalMinutes / 30.0) % 48;
        return new TimeOnly(halfHours / 2, halfHours % 2 * 30).ToString("HH:mm");
    }

    private void Normalise()
    {
        UpdateIntervalSeconds = Math.Clamp(UpdateIntervalSeconds, 1, 3600);
        Brightness = Math.Clamp(Brightness, 0.2, 1.0);
        NightBrightness = Math.Clamp(NightBrightness, 0.05, 1.0);
        BackgroundOpacity = Math.Clamp(BackgroundOpacity, 0.0, 1.0);
        NightStart = NormaliseTime(NightStart, "22:00");
        NightEnd = NormaliseTime(NightEnd, "07:00");
        GlowStrength = Math.Clamp(GlowStrength, 0.0, 1.0);
        Scale = Math.Clamp(Scale, 0.5, 3.0);
        Rotation = (int)(Math.Round((((Rotation % 360) + 360) % 360) / 90.0) * 90) % 360;
    }
}
