using Lingo.Infrastructure;
using Lingo.Models;
using Lingo.Services;

namespace Lingo.Forms;

// 常驻复用的翻译浮窗：Esc 隐藏、关闭即隐藏、优先显示在鼠标所在屏幕
internal sealed class TranslateForm : Form
{
    private readonly Func<AppSettings> _getSettings;
    private readonly Action _persistSettings;
    private readonly TranslationService _translationService;

    private readonly TextBox _sourceBox;
    private readonly CardPanel _sourceCard;
    private readonly TableLayoutPanel _layout;
    private readonly List<ResultPanel> _panels = [];
    private readonly System.Windows.Forms.Timer _autoTranslateTimer;

    private bool _suppressTextChanged;
    private string _lastTranslatedText = string.Empty;

    public TranslateForm(Func<AppSettings> getSettings, Action persistSettings, TranslationService translationService)
    {
        _getSettings = getSettings;
        _persistSettings = persistSettings;
        _translationService = translationService;

        Text = "Lingo";
        Icon = AppIcon.Get();
        // Sizable 才会在标题栏显示窗口图标，禁用最大化/最小化保持精简
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        KeyPreview = true;
        MinimumSize = new Size(320, 150);
        BackColor = Theme.MainBg;
        ForeColor = Theme.Text;

        _sourceBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.MainBg,
            ForeColor = Theme.Text,
            PlaceholderText = "输入需要翻译的文本，停顿或按 Enter 翻译",
            Margin = new Padding(0),
        };
        _sourceBox.KeyDown += OnSourceBoxKeyDown;
        _sourceBox.TextChanged += OnSourceBoxTextChanged;
        _sourceCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = 10,
            BorderWidth = 2F,
            BorderColor = Theme.BorderStrong,
            BackColor = Theme.MainBg,
            Padding = new Padding(12, 10, 12, 10),
            Margin = new Padding(0, 0, 0, 10),
        };
        _sourceCard.Controls.Add(_sourceBox);

        _layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            BackColor = Theme.MainBg,
            ColumnCount = 1,
            RowCount = 1,
        };
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        // 输入区保持紧凑固定高度，缩小窗口时优先压缩结果区，最小可只剩一行输入
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
        _layout.Controls.Add(_sourceCard, 0, 0);
        Controls.Add(_layout);

        // 停止输入片刻后自动翻译，行为与 AIxyz 一致
        _autoTranslateTimer = new System.Windows.Forms.Timer { Interval = 700 };
        _autoTranslateTimer.Tick += (_, _) =>
        {
            _autoTranslateTimer.Stop();
            StartTranslation(_sourceBox.Text.Trim(), _getSettings(), force: false);
        };
    }

    public void ShowTranslation(string text)
    {
        AppSettings settings = _getSettings();
        if (!Visible)
        {
            PositionWindow(settings);
        }

        _suppressTextChanged = true;
        _sourceBox.Text = text;
        _sourceBox.SelectionStart = _sourceBox.TextLength;
        _suppressTextChanged = false;

        // 弹出时置顶一次拉到前台，随后取消置顶，不遮挡其他窗口
        TopMost = true;
        Show();
        Activate();
        BeginInvoke(() => TopMost = false);
        StartTranslation(text, settings, force: true);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            HideWindow();
            return;
        }

        base.OnKeyDown(e);
    }

    // 输入框内 Enter 立即翻译，Shift+Enter 换行
    private void OnSourceBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            _autoTranslateTimer.Stop();
            StartTranslation(_sourceBox.Text.Trim(), _getSettings(), force: true);
        }
    }

    private void OnSourceBoxTextChanged(object? sender, EventArgs e)
    {
        if (_suppressTextChanged)
        {
            return;
        }

        // 防抖：每次输入都重置计时，停顿后才发起翻译
        _autoTranslateTimer.Stop();
        _autoTranslateTimer.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 关闭按钮仅隐藏窗口，程序继续驻留托盘
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideWindow();
            return;
        }

        base.OnFormClosing(e);
    }

    private void HideWindow()
    {
        _autoTranslateTimer.Stop();
        _translationService.CancelActive();
        SaveWindowBounds();
        Hide();
    }

    private void SaveWindowBounds()
    {
        if (WindowState != FormWindowState.Normal)
        {
            return;
        }

        AppSettings settings = _getSettings();
        settings.TranslateWindow = new WindowBounds
        {
            X = Bounds.X,
            Y = Bounds.Y,
            Width = Bounds.Width,
            Height = Bounds.Height,
        };
        _persistSettings();
    }

    private void PositionWindow(AppSettings settings)
    {
        Screen cursorScreen = Screen.FromPoint(Cursor.Position);
        Rectangle workingArea = cursorScreen.WorkingArea;

        WindowBounds saved = settings.TranslateWindow;
        if (!saved.IsEmpty)
        {
            Rectangle savedBounds = new(saved.X, saved.Y, saved.Width, saved.Height);
            if (workingArea.IntersectsWith(savedBounds))
            {
                Bounds = savedBounds;
                return;
            }

            // 记忆的位置不在鼠标所在屏幕时，沿用记忆尺寸并居中到当前屏幕
            Size size = ClampSize(savedBounds.Size, workingArea);
            Bounds = CenterIn(workingArea, size);
            return;
        }

        Bounds = CenterIn(workingArea, ClampSize(new Size(600, 480), workingArea));
    }

    private static Size ClampSize(Size size, Rectangle workingArea) => new(
        Math.Min(size.Width, workingArea.Width),
        Math.Min(size.Height, workingArea.Height));

    private static Rectangle CenterIn(Rectangle workingArea, Size size) => new(
        workingArea.X + (workingArea.Width - size.Width) / 2,
        workingArea.Y + (workingArea.Height - size.Height) / 3,
        size.Width,
        size.Height);

    private void StartTranslation(string text, AppSettings settings, bool force)
    {
        IReadOnlyList<ITranslator> translators = TranslationService.CreateEnabledTranslators(settings);
        if (translators.Count == 0)
        {
            RebuildPanels(1);
            _panels[0].Title = "翻译";
            _panels[0].ShowIdle("未启用任何翻译服务，请在托盘菜单打开“设置”进行配置。");
            _lastTranslatedText = string.Empty;
            return;
        }

        RebuildPanels(translators.Count);
        for (int i = 0; i < translators.Count; i++)
        {
            _panels[i].Title = translators[i].Name;
        }

        if (text.Length == 0)
        {
            foreach (ResultPanel panel in _panels)
            {
                panel.ShowIdle("剪贴板中没有可翻译的文本，可在上方输入。");
            }

            _lastTranslatedText = string.Empty;
            return;
        }

        // 自动触发时跳过与上次相同的文本，避免重复请求
        if (!force && text == _lastTranslatedText)
        {
            return;
        }

        _lastTranslatedText = text;
        Dictionary<ITranslator, ResultPanel> panelMap = [];
        for (int i = 0; i < translators.Count; i++)
        {
            panelMap[translators[i]] = _panels[i];
            _panels[i].ShowLoading();
        }

        _translationService.StartTranslation(text, translators, (translator, result) =>
        {
            if (IsDisposed)
            {
                return;
            }

            try
            {
                BeginInvoke(() =>
                {
                    if (panelMap.TryGetValue(translator, out ResultPanel? panel) && !panel.IsDisposed)
                    {
                        panel.ShowResult(result);
                    }
                });
            }
            catch (InvalidOperationException)
            {
                // 窗口句柄在回调前被销毁（程序退出），直接丢弃结果
            }
        });
    }

    // 按启用的翻译服务数量增删结果面板并重排行高
    private void RebuildPanels(int count)
    {
        if (_panels.Count == count)
        {
            return;
        }

        _layout.SuspendLayout();
        while (_panels.Count > count)
        {
            ResultPanel extra = _panels[^1];
            _panels.RemoveAt(_panels.Count - 1);
            _layout.Controls.Remove(extra);
            extra.Dispose();
        }

        while (_panels.Count < count)
        {
            ResultPanel panel = new(string.Empty);
            _panels.Add(panel);
            _layout.Controls.Add(panel, 0, _panels.Count);
        }

        _layout.RowCount = 1 + count;
        while (_layout.RowStyles.Count > _layout.RowCount)
        {
            _layout.RowStyles.RemoveAt(_layout.RowStyles.Count - 1);
        }

        while (_layout.RowStyles.Count < _layout.RowCount)
        {
            _layout.RowStyles.Add(new RowStyle());
        }

        for (int i = 0; i < count; i++)
        {
            _layout.RowStyles[i + 1] = new RowStyle(SizeType.Percent, 100F / count);
            _layout.SetCellPosition(_panels[i], new TableLayoutPanelCellPosition(0, i + 1));
            _panels[i].Margin = new Padding(0, 0, 0, i == count - 1 ? 0 : 10);
        }

        _layout.ResumeLayout();
    }
}
