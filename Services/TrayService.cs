using Lingo.Infrastructure;

namespace Lingo.Services;

internal sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public TrayService()
    {
        ContextMenuStrip menu = new();
        menu.Items.Add("翻译剪贴板(&T)", null, (_, _) => TranslateRequested?.Invoke());
        menu.Items.Add("设置(&S)", null, (_, _) => SettingsRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出(&X)", null, (_, _) => ExitRequested?.Invoke());

        _notifyIcon = new NotifyIcon
        {
            Icon = AppIcon.Get(),
            Text = "Lingo — 剪贴板翻译",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => TranslateRequested?.Invoke();
    }

    public event Action? TranslateRequested;

    public event Action? SettingsRequested;

    public event Action? ExitRequested;

    public void ShowBalloon(string title, string message) =>
        _notifyIcon.ShowBalloonTip(3000, title, message, ToolTipIcon.Info);

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
    }
}
