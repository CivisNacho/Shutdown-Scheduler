using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Shutdown_Scheduler.Services;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon = new();
    private readonly LocalizationService _loc;

    public TrayService(LocalizationService loc)
    {
        _loc = loc;
        _icon.Icon = SystemIcons.Application;
        _icon.Text = loc.T("AppTitle");
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        RebuildMenu(false);
    }

    public event EventHandler? OpenRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler? ExitRequested;

    public void Show(bool activeSchedule)
    {
        RebuildMenu(activeSchedule);
        _icon.Visible = true;
    }

    public void Hide() => _icon.Visible = false;

    private void RebuildMenu(bool activeSchedule)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_loc.T("Open"), null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        if (activeSchedule)
        {
            menu.Items.Add(_loc.T("CancelSchedule"), null, (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty));
            menu.Items.Add(_loc.T("CancelAndExit"), null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        }
        else
        {
            menu.Items.Add(_loc.T("Exit"), null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        }
        _icon.ContextMenuStrip = menu;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
