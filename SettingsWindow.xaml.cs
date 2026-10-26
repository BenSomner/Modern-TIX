using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace TixClock;

/// <summary>Settings dialog. Every change is applied to the clock straight away and saved.</summary>
public partial class SettingsWindow : Window
{
    private const string CustomPreset = "Custom";

    private readonly MainWindow _clock;
    private readonly AppSettings _settings;
    private readonly Border[] _swatches;
    private bool _loading = true;

    public SettingsWindow(MainWindow clock, AppSettings settings)
    {
        InitializeComponent();
        _clock = clock;
        _settings = settings;
        _swatches = [Swatch0, Swatch1, Swatch2, Swatch3];

        if (!ThemeHelper.SupportsBackdrop)
        {
            BackdropBox.IsEnabled = false;
            BackdropNote.Visibility = Visibility.Visible;
            RestartNote.Visibility = Visibility.Collapsed;
        }

        string[] halfHours = Enumerable.Range(0, 48).Select(i => new TimeOnly(i / 2, i % 2 * 30).ToString("HH:mm")).ToArray();
        NightStartBox.ItemsSource = halfHours;
        NightEndBox.ItemsSource = halfHours;

        foreach (var preset in ColourPresets.All) PresetBox.Items.Add(preset.Name);
        PresetBox.Items.Add(CustomPreset);

        VersionText.Text = $"Version {AppVersion}";
        Tabs.SelectedIndex = Math.Clamp(_settings.SettingsTab, 0, Tabs.Items.Count - 1);
        Refresh();
    }

    /// <summary>The project's Version, without any "+commit" build suffix.</summary>
    private static string AppVersion =>
        (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "")
        .Split('+')[0];

    public void ShowAbout() => Tabs.SelectedItem = AboutTab;

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Controls inside the tabs raise SelectionChanged too (combo boxes); only the tab strip matters here.
        if (e.OriginalSource != Tabs || _loading) return;
        _settings.SettingsTab = Tabs.SelectedIndex;
        _settings.Save();
    }

    /// <summary>Reloads the controls from the settings, e.g. after a change from the clock's right-click menu.</summary>
    public void Refresh()
    {
        _loading = true;
        Use24HourBox.IsChecked = _settings.Use24Hour;
        IntervalBox.SelectedIndex = _settings.UpdateIntervalSeconds switch { 1 => 0, 60 => 2, _ => 1 };
        TooltipBox.IsChecked = _settings.ShowTooltip;
        TooltipDateBox.IsChecked = _settings.ShowDateInTooltip;
        ScaleSlider.Value = _settings.Scale;
        RotationBox.SelectedIndex = _settings.Rotation / 90;
        BrightnessSlider.Value = _settings.Brightness;
        GlowSlider.Value = _settings.GlowStrength;
        ShapeBox.SelectedIndex = (int)_settings.Shape;
        SmoothBox.IsChecked = _settings.SmoothTransitions;
        ThemeBox.SelectedIndex = (int)_settings.Theme;
        BackdropBox.SelectedIndex = ThemeHelper.SupportsBackdrop ? (int)_settings.Backdrop : (int)BackdropChoice.Solid;
        OpacitySlider.Value = _settings.BackgroundOpacity;
        NightBox.IsChecked = _settings.NightDimming;
        NightStartBox.SelectedItem = _settings.NightStart;
        NightEndBox.SelectedItem = _settings.NightEnd;
        NightBrightnessSlider.Value = _settings.NightBrightness;
        TopmostBox.IsChecked = _settings.AlwaysOnTop;
        LockBox.IsChecked = _settings.LockPosition;
        ClickThroughBox.IsChecked = _settings.ClickThrough;
        FullScreenBox.IsChecked = _settings.HideWhenFullScreen;
        StartupBox.IsChecked = StartupHelper.IsEnabled();
        PortableBox.IsChecked = AppSettings.IsPortable;
        SettingsPathText.Text = $"Settings are saved in {AppSettings.FilePath}";
        OffMatchThemeBox.IsChecked = _settings.OffColour is null;
        UpdateLabels();
        _loading = false;
    }

    private void UpdateLabels()
    {
        ScaleText.Text = $"{_settings.Scale:P0}";
        BrightnessText.Text = $"{_settings.Brightness:P0}";
        GlowText.Text = $"{_settings.GlowStrength:P0}";
        OpacityText.Text = $"{_settings.BackgroundOpacity:P0}";
        NightBrightnessText.Text = $"{_settings.NightBrightness:P0}";
        OpacitySlider.IsEnabled = !_settings.UsesSystemBackdrop;
        NightOptions.IsEnabled = _settings.NightDimming;
        NightOptions.Opacity = _settings.NightDimming ? 1 : 0.5; // the Fluent slider and labels don't grey out
        TooltipDateBox.IsEnabled = _settings.ShowTooltip;

        Color[] colours = _settings.BlockColours();
        for (int i = 0; i < _swatches.Length; i++) _swatches[i].Background = new SolidColorBrush(colours[i]);
        SwatchOff.Background = new SolidColorBrush(_clock.CurrentOffColour);
        OffSwatchButton.IsEnabled = _settings.OffColour is not null;

        bool wasLoading = _loading;
        _loading = true;
        int preset = ColourPresets.IndexOf(_settings, _clock.IsDarkTheme);
        PresetBox.SelectedIndex = preset >= 0 ? preset : ColourPresets.All.Length;
        _loading = wasLoading;
    }

    private void PresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || PresetBox.SelectedIndex < 0 || PresetBox.SelectedIndex >= ColourPresets.All.Length) return;
        ColourPresets.Apply(ColourPresets.All[PresetBox.SelectedIndex], _settings, _clock.IsDarkTheme);
        _clock.ApplySettings();
        UpdateLabels();
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        _settings.Use24Hour = Use24HourBox.IsChecked == true;
        _settings.UpdateIntervalSeconds = IntervalBox.SelectedIndex switch { 0 => 1, 2 => 60, _ => 4 };
        _settings.ShowTooltip = TooltipBox.IsChecked == true;
        _settings.ShowDateInTooltip = TooltipDateBox.IsChecked == true;
        _settings.Scale = Math.Round(ScaleSlider.Value, 2);
        _settings.Rotation = Math.Max(0, RotationBox.SelectedIndex) * 90;
        _settings.Brightness = Math.Round(BrightnessSlider.Value, 2);
        _settings.GlowStrength = Math.Round(GlowSlider.Value, 2);
        _settings.Shape = (LedShape)Math.Max(0, ShapeBox.SelectedIndex);
        _settings.SmoothTransitions = SmoothBox.IsChecked == true;
        _settings.BackgroundOpacity = Math.Round(OpacitySlider.Value, 2);
        _settings.NightDimming = NightBox.IsChecked == true;
        if (NightStartBox.SelectedItem is string start) _settings.NightStart = start;
        if (NightEndBox.SelectedItem is string end) _settings.NightEnd = end;
        _settings.NightBrightness = Math.Round(NightBrightnessSlider.Value, 2);
        _settings.AlwaysOnTop = TopmostBox.IsChecked == true;
        _settings.LockPosition = LockBox.IsChecked == true;
        _settings.ClickThrough = ClickThroughBox.IsChecked == true;
        _settings.HideWhenFullScreen = FullScreenBox.IsChecked == true;
        if (ThemeHelper.SupportsBackdrop) _settings.Backdrop = (BackdropChoice)BackdropBox.SelectedIndex;

        if (_settings.UsesSystemBackdrop == _clock.IsLayered)
        {
            // The new background needs the other kind of window. Restart once this event has finished.
            _settings.Save();
            Dispatcher.BeginInvoke(() => App.Restart(openSettings: true));
            return;
        }

        var theme = (ThemeChoice)ThemeBox.SelectedIndex;
        if (theme != _settings.Theme)
        {
            _settings.Theme = theme;
            ThemeHelper.ApplyAppTheme(theme);
        }

        _clock.ApplySettings();
        UpdateLabels();
    }

    private void PickColour_Click(object sender, RoutedEventArgs e)
    {
        string tag = (string)((Button)sender).Tag;
        bool isOff = tag == "off";
        int block = isOff ? -1 : int.Parse(tag);
        Color current = isOff ? _clock.CurrentOffColour : _settings.BlockColours()[block];

        using var dialog = new Forms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B),
        };
        if (dialog.ShowDialog(new Win32Owner(this)) != Forms.DialogResult.OK) return;

        string hex = AppSettings.ToHex(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
        if (isOff) _settings.OffColour = hex;
        else _settings.SetBlockColour(block, hex);

        _clock.ApplySettings();
        UpdateLabels();
    }

    private void OffMatchTheme_Click(object sender, RoutedEventArgs e)
    {
        // Unticking starts the custom colour from whatever the theme colour looked like, made opaque.
        Color current = _clock.CurrentOffColour;
        Color background = ThemeHelper.IsDark(_settings.Theme) ? ThemeHelper.DarkBackground : ThemeHelper.LightBackground;
        _settings.OffColour = OffMatchThemeBox.IsChecked == true ? null : AppSettings.ToHex(Blend(current, background));
        _clock.ApplySettings();
        UpdateLabels();
    }

    private void ResetColours_Click(object sender, RoutedEventArgs e)
    {
        _settings.ResetColours();
        _clock.ApplySettings();
        Refresh();
    }

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StartupHelper.SetEnabled(StartupBox.IsChecked == true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            MessageBox.Show(this, $"Couldn't change the startup setting: {ex.Message}", "TIX Clock",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        StartupBox.IsChecked = StartupHelper.IsEnabled();
    }

    private void Portable_Click(object sender, RoutedEventArgs e)
    {
        bool portable = PortableBox.IsChecked == true;
        try
        {
            _settings.SetPortable(portable);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            string message = portable
                ? $"Couldn't save settings next to the app, so they're staying where they are.\n\n{ex.Message}\n\n" +
                  "Portable mode needs the app to be in a folder you can write to, such as a USB stick or your " +
                  "Documents, rather than Program Files."
                : $"Couldn't move settings back from the app's folder.\n\n{ex.Message}";
            MessageBox.Show(this, message, "TIX Clock", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        PortableBox.IsChecked = AppSettings.IsPortable;
        SettingsPathText.Text = $"Settings are saved in {AppSettings.FilePath}";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static Color Blend(Color top, Color bottom)
    {
        double a = top.A / 255.0;
        return Color.FromRgb(
            (byte)Math.Round(top.R * a + bottom.R * (1 - a)),
            (byte)Math.Round(top.G * a + bottom.G * (1 - a)),
            (byte)Math.Round(top.B * a + bottom.B * (1 - a)));
    }

    private sealed class Win32Owner(Window window) : Forms.IWin32Window
    {
        public IntPtr Handle { get; } = new WindowInteropHelper(window).Handle;
    }
}
