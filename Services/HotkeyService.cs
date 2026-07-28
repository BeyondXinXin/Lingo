using System.Runtime.InteropServices;
using Lingo.Infrastructure;

namespace Lingo.Services;

// 基于 Win32 RegisterHotKey 的全局快捷键，消息窗口零轮询、零占用
internal sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 1;

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private readonly MessageWindow _window;
    private bool _registered;

    public HotkeyService()
    {
        _window = new MessageWindow(OnHotkeyPressed);
    }

    public event Action? HotkeyPressed;

    public bool TryRegister(string hotkey, out string error)
    {
        Unregister();

        if (!TryParse(hotkey, out uint modifiers, out Keys key))
        {
            error = $"快捷键“{hotkey}”格式无效，请重新设置。";
            return false;
        }

        if (RegisterHotKey(_window.Handle, HotkeyId, modifiers | ModNoRepeat, (uint)key))
        {
            _registered = true;
            error = string.Empty;
            return true;
        }

        error = $"快捷键 {hotkey} 注册失败，可能已被其他程序占用。";
        AppLogger.Error(error);
        return false;
    }

    public void Unregister()
    {
        if (_registered)
        {
            UnregisterHotKey(_window.Handle, HotkeyId);
            _registered = false;
        }
    }

    public void Dispose()
    {
        Unregister();
        _window.DestroyHandle();
    }

    public static bool TryParse(string hotkey, out uint modifiers, out Keys key)
    {
        modifiers = 0;
        key = Keys.None;
        if (string.IsNullOrWhiteSpace(hotkey))
        {
            return false;
        }

        foreach (string rawPart in hotkey.Split('+'))
        {
            string part = rawPart.Trim();
            switch (part.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL":
                    modifiers |= ModControl;
                    break;
                case "ALT":
                    modifiers |= ModAlt;
                    break;
                case "SHIFT":
                    modifiers |= ModShift;
                    break;
                case "WIN":
                    modifiers |= ModWin;
                    break;
                default:
                    if (key != Keys.None || !Enum.TryParse(part, ignoreCase: true, out key) || IsModifierKey(key))
                    {
                        return false;
                    }

                    break;
            }
        }

        // 必须包含修饰键，避免吞掉普通按键
        return key != Keys.None && modifiers != 0;
    }

    public static string Format(Keys keyData)
    {
        List<string> parts = [];
        if (keyData.HasFlag(Keys.Control))
        {
            parts.Add("Ctrl");
        }

        if (keyData.HasFlag(Keys.Alt))
        {
            parts.Add("Alt");
        }

        if (keyData.HasFlag(Keys.Shift))
        {
            parts.Add("Shift");
        }

        Keys key = keyData & Keys.KeyCode;
        if (key == Keys.None || IsModifierKey(key))
        {
            return string.Empty;
        }

        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    private static bool IsModifierKey(Keys key) =>
        key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu
            or Keys.LControlKey or Keys.RControlKey
            or Keys.LShiftKey or Keys.RShiftKey
            or Keys.LMenu or Keys.RMenu
            or Keys.LWin or Keys.RWin;

    private void OnHotkeyPressed() => HotkeyPressed?.Invoke();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private sealed class MessageWindow : NativeWindow
    {
        private readonly Action _onHotkey;

        public MessageWindow(Action onHotkey)
        {
            _onHotkey = onHotkey;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId)
            {
                _onHotkey();
            }

            base.WndProc(ref m);
        }
    }
}
