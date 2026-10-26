using System.Diagnostics;
using System.Windows;

namespace TixClock;

public partial class App : Application
{
    private const string MutexName = @"Local\TixClock.SingleInstance";
    private const string ShowEventName = @"Local\TixClock.Show";
    private const string RestartArg = "--restart";
    private const string SettingsArg = "--settings";

    private Mutex? _mutex;
    private bool _ownsMutex;
    private EventWaitHandle? _showEvent;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, MutexName, out _ownsMutex);
        if (!_ownsMutex && e.Args.Contains(RestartArg))
        {
            // Restarted by the previous copy, which is still closing down.
            try
            {
                _ownsMutex = _mutex.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (AbandonedMutexException)
            {
                _ownsMutex = true;
            }
        }
        if (!_ownsMutex)
        {
            // Already running: ask that copy to show itself, then quit.
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var existing))
            {
                existing.Set();
                existing.Dispose();
            }
            Shutdown();
            return;
        }

        var settings = AppSettings.Load();
        ThemeHelper.ApplyAppTheme(settings.Theme);
        ThemeHelper.ApplyTrayColourMode(settings.Theme);
        StartupHelper.RefreshPath();

        var clock = new MainWindow(settings);
        MainWindow = clock;
        _tray = new TrayIcon(clock);

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(new Action(clock.ShowClock)), null, Timeout.Infinite, false);

        clock.Show();
        if (e.Args.Contains(SettingsArg)) clock.OpenSettings();
    }

    /// <summary>Starts a fresh copy and closes this one, e.g. to switch between window types.</summary>
    public static void Restart(bool openSettings)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        start.ArgumentList.Add(RestartArg);
        if (openSettings) start.ArgumentList.Add(SettingsArg);
        Process.Start(start);
        ((MainWindow)Current.MainWindow).ExitApp();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _showEvent?.Dispose();
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
