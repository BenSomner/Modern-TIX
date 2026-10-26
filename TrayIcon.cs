using System.Drawing;
using System.Windows.Forms;

namespace TixClock;

/// <summary>Notification-area icon: left-click shows or hides the clock, right-click opens a menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;

    public TrayIcon(MainWindow clock)
    {
        var showItem = new ToolStripMenuItem("Hide clock", null, (_, _) => clock.ToggleVisibility());
        var topItem = new ToolStripMenuItem("Always on top", null, (_, _) => clock.SetAlwaysOnTop(!clock.Topmost));
        var clickThroughItem = new ToolStripMenuItem("Click-through", null,
            (_, _) => clock.SetClickThrough(!clock.ClickThrough));

        _menu = new ContextMenuStrip();
        _menu.Items.Add(showItem);
        _menu.Items.Add(topItem);
        _menu.Items.Add(clickThroughItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Settings…", null, (_, _) => clock.OpenSettings());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => clock.ExitApp());
        _menu.Opening += (_, _) =>
        {
            showItem.Text = clock.IsVisible ? "Hide clock" : "Show clock";
            topItem.Checked = clock.Topmost;
            clickThroughItem.Checked = clock.ClickThrough;
        };

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = App.DisplayName,
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) clock.ToggleVisibility();
        };
        // Hovering the tray icon shows the time as well.
        _icon.MouseMove += (_, _) => _icon.Text = $"{App.DisplayName} – {clock.CurrentTimeText}";
    }

    private static Icon LoadIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/TixClock.ico"));
        using var stream = resource.Stream;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }
}
