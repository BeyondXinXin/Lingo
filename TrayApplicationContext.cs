using Lingo.Forms;
using Lingo.Infrastructure;
using Lingo.Models;
using Lingo.Services;

namespace Lingo;

// 无主窗口的托盘常驻上下文，负责组装各服务并处理生命周期
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly TranslationService _translationService = new();
    private readonly HotkeyService _hotkeyService = new();
    private readonly TrayService _trayService = new();

    private AppSettings _settings;
    private TranslateForm? _translateForm;
    private SettingsForm? _settingsForm;

    public TrayApplicationContext()
    {
        _settings = SettingsStore.Load();

        _trayService.TranslateRequested += TranslateClipboard;
        _trayService.SettingsRequested += OpenSettings;
        _trayService.ExitRequested += ExitApplication;
        _hotkeyService.HotkeyPressed += TranslateClipboard;

        if (!_hotkeyService.TryRegister(_settings.Hotkey, out string error))
        {
            _trayService.ShowBalloon("Lingo", error);
        }

        AppLogger.Info($"Lingo 启动，快捷键 {_settings.Hotkey}");
        // 启动收尾后整理一次，降低初始驻留占用
        MemoryTrimmer.TrimLater(delayMilliseconds: 3000);
    }

    private void TranslateClipboard()
    {
        string? raw = ClipboardService.TryGetText();
        string text = raw is null
            ? string.Empty
            : ClipboardService.NormalizeForTranslation(raw).Trim();

        GetTranslateForm().ShowTranslation(text);
    }

    private void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }

        using SettingsForm form = new(_settings);
        _settingsForm = form;
        try
        {
            if (form.ShowDialog() == DialogResult.OK && form.Result is not null)
            {
                ApplyNewSettings(form.Result);
            }
        }
        finally
        {
            _settingsForm = null;
            // 设置窗口销毁后顺手把窗体相关内存还给系统
            MemoryTrimmer.TrimLater();
        }
    }

    private void ApplyNewSettings(AppSettings newSettings)
    {
        bool startupChanged = _settings.LaunchAtStartup != newSettings.LaunchAtStartup;
        _settings = newSettings;
        SettingsStore.Save(_settings);

        if (startupChanged)
        {
            StartupService.SetEnabled(_settings.LaunchAtStartup);
        }

        if (!_hotkeyService.TryRegister(_settings.Hotkey, out string error))
        {
            _trayService.ShowBalloon("Lingo", error);
        }
    }

    private TranslateForm GetTranslateForm()
    {
        if (_translateForm is null || _translateForm.IsDisposed)
        {
            _translateForm = new TranslateForm(
                () => _settings,
                () => SettingsStore.Save(_settings),
                _translationService);
        }

        return _translateForm;
    }

    private void ExitApplication()
    {
        _hotkeyService.Dispose();
        _trayService.Dispose();
        _translationService.Dispose();
        _translateForm?.Dispose();

        AppLogger.Info("Lingo 退出");
        ExitThread();
    }
}
