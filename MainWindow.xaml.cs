using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;

namespace TixClock;

/// <summary>The clock widget itself: four blocks of squares, redrawn as the time changes and reshuffled on a timer.</summary>
public partial class MainWindow : Window
{
    private const double CellSize = 18;
    private const double CellGap = 4;
    private const double BlockGap = 14;
    private const double GlowRadius = 12;
    private const int FullScreenCheckTicks = 5; // the timer ticks every 200 ms, so about once a second

    private static readonly DoubleAnimation FadeIn = Frozen(new DoubleAnimation(1, TimeSpan.FromMilliseconds(400)));
    private static readonly DoubleAnimation FadeOut = Frozen(new DoubleAnimation(0, TimeSpan.FromMilliseconds(400)));

    private readonly AppSettings _settings;
    private readonly bool _layered;

    // Each square is an unlit layer with a lit layer on top, so lighting a square is just fading the top layer.
    private readonly Border[][] _litCells = new Border[4][];
    private readonly Border[][] _offCells = new Border[4][];
    private readonly bool[][] _pattern = new bool[4][];
    private readonly DispatcherTimer _timer;
    private readonly ToolTip _toolTip = new();

    private Color _offColour;
    private double _brightness = -1;
    private int[]? _shownDigits;
    private DateTime _lastShuffle;
    private bool _dark;
    private bool _needsDefaultPosition;
    private bool _sizeSettled;
    private int _fullScreenCountdown;
    private bool _autoHidden;
    private bool _shownDuringFullScreen;
    private SettingsWindow? _settingsWindow;

    private MenuItem _use24HourItem = null!;
    private MenuItem _topmostItem = null!;
    private MenuItem _lockItem = null!;
    private readonly List<(MenuItem Item, int Seconds)> _intervalItems = [];
    private readonly List<(MenuItem Item, int Degrees)> _rotationItems = [];

    public MainWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        // Background opacity and floating LEDs need a per-pixel transparent window, which can't have a
        // system backdrop. Switching between the two needs a new window, so the app restarts to do it.
        _layered = !settings.UsesSystemBackdrop;
        if (_layered)
        {
            AllowsTransparency = true;
        }
        else
        {
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                GlassFrameThickness = new Thickness(-1),
                ResizeBorderThickness = new Thickness(0),
                UseAeroCaptionButtons = false,
            });
        }

        BuildCells();
        ContextMenu = BuildContextMenu();

        if (settings.Left is double left && settings.Top is double top && ThemeHelper.IsOnScreen(left, top))
        {
            Left = left;
            Top = top;
        }
        else
        {
            _needsDefaultPosition = true;
        }

        Root.ToolTip = _toolTip;
        Root.ToolTipOpening += (_, _) => _toolTip.Content = _settings.ShowDateInTooltip
            ? $"{CurrentTimeText}\n{TixTime.FormatDate(DateTime.Now)}"
            : CurrentTimeText;

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => Tick();

        SourceInitialized += (_, _) =>
        {
            ThemeHelper.HideFromAltTab(this);
            ThemeHelper.SetClickThrough(this, _settings.ClickThrough, _layered);
            ApplyTheme();
        };
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        ApplySettings(save: false);
    }

    public string CurrentTimeText => TixTime.FormatTime(DateTime.Now, _settings.Use24Hour);

    public Color CurrentOffColour => _offColour;

    public bool IsDarkTheme => _dark;

    /// <summary>True for the per-pixel transparent window, false for the Acrylic/Mica one.</summary>
    public bool IsLayered => _layered;

    public bool ClickThrough => _settings.ClickThrough;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        // With WindowChrome, SizeToContent first sizes the window with the standard frame before the chrome
        // removes it, leaving a gap on the right and bottom. Re-run it now that the frame has gone.
        SizeToContent = SizeToContent.Manual;
        SizeToContent = SizeToContent.WidthAndHeight;
        UpdateLayout();

        if (_needsDefaultPosition)
        {
            Rect area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 24;
            Top = area.Top + 24;
            _needsDefaultPosition = false;
        }
        KeepOnScreen();
        _sizeSettled = true;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_sizeSettled) return;
        // Rotating or resizing grows the window from its top-left corner; keep it centred where it was instead.
        Left -= (e.NewSize.Width - e.PreviousSize.Width) / 2;
        Top -= (e.NewSize.Height - e.PreviousSize.Height) / 2;
        KeepOnScreen();
        SavePosition();
    }

    /// <summary>Nudges the clock back inside the work area of the monitor it's on.</summary>
    private void KeepOnScreen()
    {
        if (PresentationSource.FromVisual(this)?.CompositionTarget is not { } target) return;
        var area = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        Point topLeft = target.TransformFromDevice.Transform(new Point(area.Left, area.Top));
        Point bottomRight = target.TransformFromDevice.Transform(new Point(area.Right, area.Bottom));
        Left = Math.Max(topLeft.X, Math.Min(Left, bottomRight.X - ActualWidth));
        Top = Math.Max(topLeft.Y, Math.Min(Top, bottomRight.Y - ActualHeight));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // The Fluent theme may set its own backdrop once the window is up, so apply ours again.
        Dispatcher.BeginInvoke(ApplyTheme, DispatcherPriority.ApplicationIdle);
        Tick();
        _timer.Start();
    }

    private void BuildCells()
    {
        for (int block = 0; block < 4; block++)
        {
            int columns = TixTime.BlockColumns[block];
            var grid = new UniformGrid
            {
                Rows = TixTime.Rows,
                Columns = columns,
                Margin = new Thickness(block == 0 ? 0 : BlockGap - CellGap, 0, 0, 0),
            };
            int count = columns * TixTime.Rows;
            _litCells[block] = new Border[count];
            _offCells[block] = new Border[count];
            _pattern[block] = new bool[count];
            for (int i = 0; i < count; i++)
            {
                var off = new Border();
                var lit = new Border { Opacity = 0 };
                var cell = new Grid { Width = CellSize, Height = CellSize, Margin = new Thickness(CellGap / 2) };
                cell.Children.Add(off);
                cell.Children.Add(lit);
                _offCells[block][i] = off;
                _litCells[block][i] = lit;
                grid.Children.Add(cell);
            }
            BlocksPanel.Children.Add(grid);
        }
    }

    private void Tick()
    {
        DateTime now = DateTime.Now;

        // Night dimming switches brightness at the set times.
        if (_settings.EffectiveBrightness(now) != _brightness) UpdateBrushes();

        if (--_fullScreenCountdown <= 0)
        {
            _fullScreenCountdown = FullScreenCheckTicks;
            CheckFullScreen();
        }

        int[] digits = TixTime.Digits(now, _settings.Use24Hour);
        bool timeChanged = _shownDigits is null || !digits.AsSpan().SequenceEqual(_shownDigits);

        // At a one-minute interval the pattern changes with the minute, so there's no separate reshuffle.
        bool due = _settings.UpdateIntervalSeconds < 60 &&
                   now - _lastShuffle >= TimeSpan.FromSeconds(_settings.UpdateIntervalSeconds);

        if (!timeChanged && !due) return;

        for (int block = 0; block < 4; block++)
        {
            _pattern[block] = TixTime.RandomPattern(_pattern[block].Length, digits[block], _pattern[block]);
        }
        // The first pattern appears straight away rather than fading in from blank.
        bool animate = _shownDigits is not null;
        _shownDigits = digits;
        _lastShuffle = now;
        Paint(animate);
    }

    private void Paint(bool animate)
    {
        bool fade = animate && _settings.SmoothTransitions && IsVisible;
        for (int block = 0; block < 4; block++)
        {
            for (int i = 0; i < _litCells[block].Length; i++)
            {
                Border lit = _litCells[block][i];
                bool on = _pattern[block][i];
                if (fade)
                {
                    lit.BeginAnimation(OpacityProperty, on ? FadeIn : FadeOut);
                }
                else
                {
                    lit.BeginAnimation(OpacityProperty, null);
                    lit.Opacity = on ? 1 : 0;
                }
            }
        }
    }

    /// <summary>Colours, brightness, glow and shape of every square.</summary>
    private void UpdateBrushes()
    {
        _brightness = _settings.EffectiveBrightness(DateTime.Now);
        Color[] colours = _settings.BlockColours();
        var corners = new CornerRadius(_settings.Shape == LedShape.Round ? CellSize / 2 : 4);

        _offColour = _settings.OffColour is { } custom
            ? AppSettings.ParseColour(custom, "#404040")
            : _dark ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1C, 0x00, 0x00, 0x00);
        var offBrush = Frozen(new SolidColorBrush(_offColour));

        for (int block = 0; block < 4; block++)
        {
            var litBrush = Frozen(new SolidColorBrush(colours[block]) { Opacity = _brightness });

            // Fixed size, sized to fit inside the padding; the slider only changes how strong it is.
            Effect? glow = _settings.GlowStrength > 0
                ? Frozen(new DropShadowEffect
                {
                    Color = colours[block],
                    BlurRadius = GlowRadius,
                    ShadowDepth = 0,
                    Opacity = _settings.GlowStrength * _brightness,
                })
                : null;

            for (int i = 0; i < _litCells[block].Length; i++)
            {
                _litCells[block][i].Background = litBrush;
                _litCells[block][i].Effect = glow;
                _litCells[block][i].CornerRadius = corners;
                _offCells[block][i].Background = offBrush;
                _offCells[block][i].CornerRadius = corners;
            }
        }
    }

    private void ApplyTheme()
    {
        _dark = ThemeHelper.IsDark(_settings.Theme);
        Color background = _dark ? ThemeHelper.DarkBackground : ThemeHelper.LightBackground;

        if (_layered)
        {
            // Fully transparent pixels let clicks through a layered window, so keep a trace of alpha
            // at 0% opacity to keep the whole clock draggable.
            byte alpha = (byte)Math.Max(1, Math.Round(_settings.BackgroundOpacity * 255));
            Background = Brushes.Transparent;
            Root.Background = Frozen(new SolidColorBrush(Color.FromArgb(alpha, background.R, background.G, background.B)));
            Root.CornerRadius = new CornerRadius(8);
        }
        else
        {
            bool backdrop = ThemeHelper.ApplyWindowFrame(this, _dark, _settings.Backdrop);
            Background = backdrop ? Brushes.Transparent : new SolidColorBrush(background);
            Root.Background = Brushes.Transparent;
        }
        UpdateBrushes();
    }

    /// <summary>Applies every setting to the widget. Called whenever something changes them.</summary>
    public void ApplySettings(bool save = true)
    {
        Topmost = _settings.AlwaysOnTop;
        RootScale.ScaleX = RootScale.ScaleY = _settings.Scale;
        RootRotate.Angle = _settings.Rotation;
        Root.ToolTip = _settings.ShowTooltip ? _toolTip : null;
        ThemeHelper.SetClickThrough(this, _settings.ClickThrough, _layered);
        ApplyTheme();
        if (IsLoaded)
        {
            CheckFullScreen();
            Tick();
        }
        if (save) _settings.Save();
    }

    /// <summary>Hides the clock while a full-screen app is in front of it, and brings it back afterwards.</summary>
    private void CheckFullScreen()
    {
        bool fullScreen = _settings.HideWhenFullScreen && FullScreenDetector.IsActive(this);
        if (fullScreen)
        {
            if (IsVisible && !_shownDuringFullScreen)
            {
                Hide();
                _autoHidden = true;
            }
        }
        else
        {
            _shownDuringFullScreen = false;
            if (_autoHidden)
            {
                _autoHidden = false;
                Show();
            }
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        {
            Dispatcher.BeginInvoke(ApplyTheme, DispatcherPriority.ApplicationIdle);
        }
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        _use24HourItem = new MenuItem { Header = "24-hour time", IsCheckable = true };
        _use24HourItem.Click += (_, _) => Change(s => s.Use24Hour = _use24HourItem.IsChecked);
        menu.Items.Add(_use24HourItem);

        var interval = new MenuItem { Header = "Change pattern" };
        foreach (var (label, seconds) in new[] { ("Every second", 1), ("Every 4 seconds", 4), ("Every minute", 60) })
        {
            // Checkable so the Fluent theme draws the tick; Opened resets every tick from the settings.
            var item = new MenuItem { Header = label, IsCheckable = true };
            item.Click += (_, _) => Change(s => s.UpdateIntervalSeconds = seconds);
            interval.Items.Add(item);
            _intervalItems.Add((item, seconds));
        }
        menu.Items.Add(interval);

        var rotate = new MenuItem { Header = "Rotate" };
        foreach (var (label, degrees) in new[] { ("Normal", 0), ("90° clockwise", 90), ("180°", 180), ("90° anticlockwise", 270) })
        {
            var item = new MenuItem { Header = label, IsCheckable = true };
            item.Click += (_, _) => Change(s => s.Rotation = degrees);
            rotate.Items.Add(item);
            _rotationItems.Add((item, degrees));
        }
        menu.Items.Add(rotate);
        menu.Items.Add(new Separator());

        _topmostItem = new MenuItem { Header = "Always on top", IsCheckable = true };
        _topmostItem.Click += (_, _) => Change(s => s.AlwaysOnTop = _topmostItem.IsChecked);
        menu.Items.Add(_topmostItem);

        _lockItem = new MenuItem { Header = "Lock position", IsCheckable = true };
        _lockItem.Click += (_, _) => Change(s => s.LockPosition = _lockItem.IsChecked);
        menu.Items.Add(_lockItem);
        // Click-through is deliberately left out: once it's on, this menu can't be reached to undo it,
        // so it's only offered in settings and the tray menu.
        menu.Items.Add(new Separator());

        var settingsItem = new MenuItem { Header = "Settings…" };
        settingsItem.Click += (_, _) => OpenSettings();
        menu.Items.Add(settingsItem);

        var aboutItem = new MenuItem { Header = "About" };
        aboutItem.Click += (_, _) => OpenSettings(showAbout: true);
        menu.Items.Add(aboutItem);

        var hideItem = new MenuItem { Header = "Hide" };
        hideItem.Click += (_, _) => Hide();
        menu.Items.Add(hideItem);

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => ExitApp();
        menu.Items.Add(exitItem);

        menu.Opened += (_, _) =>
        {
            _use24HourItem.IsChecked = _settings.Use24Hour;
            _topmostItem.IsChecked = _settings.AlwaysOnTop;
            _lockItem.IsChecked = _settings.LockPosition;
            foreach (var (item, seconds) in _intervalItems) item.IsChecked = _settings.UpdateIntervalSeconds == seconds;
            foreach (var (item, degrees) in _rotationItems) item.IsChecked = _settings.Rotation == degrees;
        };
        return menu;
    }

    private void Change(Action<AppSettings> change)
    {
        change(_settings);
        ApplySettings();
        _settingsWindow?.Refresh();
    }

    public void SetAlwaysOnTop(bool value) => Change(s => s.AlwaysOnTop = value);

    public void SetClickThrough(bool value) => Change(s => s.ClickThrough = value);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_settings.LockPosition || e.ButtonState != MouseButtonState.Pressed) return;
        DragMove();
        SavePosition();
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        double scale = Math.Round(_settings.Scale + (e.Delta > 0 ? 0.1 : -0.1), 1);
        Change(s => s.Scale = Math.Clamp(scale, 0.5, 3.0));
        e.Handled = true;
    }

    private void SavePosition()
    {
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Save();
    }

    public void ShowClock()
    {
        // Asked for explicitly, so stay visible even if a full-screen app is still running.
        if (_autoHidden || FullScreenDetector.IsActive(this)) _shownDuringFullScreen = true;
        _autoHidden = false;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            ShowClock();
        }
    }

    public void OpenSettings(bool showAbout = false)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this, _settings);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        else
        {
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
        }
        if (showAbout) _settingsWindow.ShowAbout();
    }

    public void ExitApp() => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!e.Cancel) SavePosition();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        base.OnClosed(e);
        Application.Current.Shutdown();
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
