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
    private readonly ResultPanel _baiduPanel;
    private readonly ResultPanel _customPanel;
    private readonly TableLayoutPanel _layout;

    public TranslateForm(Func<AppSettings> getSettings, Action persistSettings, TranslationService translationService)
    {
        _getSettings = getSettings;
        _persistSettings = persistSettings;
        _translationService = translationService;

        Text = "Lingo";
        Icon = AppIcon.Get();
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        KeyPreview = true;
        MinimumSize = new Size(320, 150);
        BackColor = Theme.MainBg;
        ForeColor = Theme.Text;
        Font = new Font("Microsoft YaHei UI", 9F);

        _sourceBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.StressBg,
            ForeColor = Theme.Text,
            PlaceholderText = "输入需要翻译的文本，Enter 翻译",
            Margin = new Padding(0),
        };
        _sourceBox.KeyDown += OnSourceBoxKeyDown;
        _sourceCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = 10,
            Padding = new Padding(12, 10, 12, 10),
            Margin = new Padding(0, 0, 0, 10),
        };
        _sourceCard.Controls.Add(_sourceBox);

        _baiduPanel = new ResultPanel("百度翻译");
        _baiduPanel.Margin = new Padding(0, 0, 0, 10);
        _customPanel = new ResultPanel("模型翻译");
        _customPanel.Margin = new Padding(0);

        _layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            BackColor = Theme.MainBg,
            ColumnCount = 1,
            RowCount = 3,
        };
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        // 输入区保持紧凑固定高度，缩小窗口时优先压缩结果区，最小可只剩一行输入
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        _layout.Controls.Add(_sourceCard, 0, 0);
        _layout.Controls.Add(_baiduPanel, 0, 1);
        _layout.Controls.Add(_customPanel, 0, 2);
        Controls.Add(_layout);
    }

    public void ShowTranslation(string text)
    {
        AppSettings settings = _getSettings();
        if (!Visible)
        {
            PositionWindow(settings);
        }

        _sourceBox.Text = text;
        _sourceBox.SelectionStart = _sourceBox.TextLength;

        // 弹出时置顶一次拉到前台，随后取消置顶，不遮挡其他窗口
        TopMost = true;
        Show();
        Activate();
        BeginInvoke(() => TopMost = false);
        StartTranslation(text, settings);
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

    // 输入框内 Enter 直接翻译，Shift+Enter 换行
    private void OnSourceBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            StartTranslation(_sourceBox.Text.Trim(), _getSettings());
        }
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

        Bounds = CenterIn(workingArea, ClampSize(new Size(560, 460), workingArea));
    }

    private static Size ClampSize(Size size, Rectangle workingArea) => new(
        Math.Min(size.Width, workingArea.Width),
        Math.Min(size.Height, workingArea.Height));

    private static Rectangle CenterIn(Rectangle workingArea, Size size) => new(
        workingArea.X + (workingArea.Width - size.Width) / 2,
        workingArea.Y + (workingArea.Height - size.Height) / 3,
        size.Width,
        size.Height);

    private void StartTranslation(string text, AppSettings settings)
    {
        UpdatePanelLayout(settings);

        if (text.Length == 0)
        {
            string hint = "剪贴板中没有可翻译的文本，可在上方输入后按 Enter 翻译。";
            _baiduPanel.ShowIdle(hint);
            _customPanel.ShowIdle(hint);
            return;
        }

        IReadOnlyList<ITranslator> translators = TranslationService.CreateEnabledTranslators(settings);
        if (translators.Count == 0)
        {
            string hint = "未启用任何翻译服务，请在托盘菜单打开“设置”进行配置。";
            _baiduPanel.ShowIdle(hint);
            _customPanel.ShowIdle(hint);
            return;
        }

        if (settings.Baidu.Enabled)
        {
            _baiduPanel.ShowLoading();
        }

        if (settings.CustomApi.Enabled)
        {
            _customPanel.ShowLoading();
        }

        _translationService.StartTranslation(text, translators, result =>
        {
            if (IsDisposed)
            {
                return;
            }

            try
            {
                BeginInvoke(() => ApplyResult(result));
            }
            catch (InvalidOperationException)
            {
                // 窗口句柄在回调前被销毁（程序退出），直接丢弃结果
            }
        });
    }

    private void ApplyResult(TranslationResult result)
    {
        if (result.TranslatorName == _baiduPanel.TranslatorName)
        {
            _baiduPanel.ShowResult(result);
        }
        else if (result.TranslatorName == _customPanel.TranslatorName)
        {
            _customPanel.ShowResult(result);
        }
    }

    private void UpdatePanelLayout(AppSettings settings)
    {
        bool anyEnabled = settings.Baidu.Enabled || settings.CustomApi.Enabled;
        bool showBaidu = settings.Baidu.Enabled || !anyEnabled;
        bool showCustom = settings.CustomApi.Enabled;

        _baiduPanel.Visible = showBaidu;
        _customPanel.Visible = showCustom;

        int visibleCount = (showBaidu ? 1 : 0) + (showCustom ? 1 : 0);
        _layout.RowStyles[1] = new RowStyle(SizeType.Percent, showBaidu ? 100F / visibleCount : 0F);
        _layout.RowStyles[2] = new RowStyle(SizeType.Percent, showCustom ? 100F / visibleCount : 0F);
    }
}
