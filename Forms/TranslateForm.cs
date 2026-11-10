using System.Runtime.InteropServices;
using Lingo.Infrastructure;
using Lingo.Models;
using Lingo.Services;

namespace Lingo.Forms;

// 常驻复用的翻译浮窗：Esc 隐藏、关闭即隐藏、优先显示在鼠标所在屏幕
internal sealed class TranslateForm : Form
{
    private const int VerticalScrollBar = 1;

    private readonly Func<AppSettings> _getSettings;
    private readonly Action _persistSettings;
    private readonly TranslationService _translationService;

    private readonly TextBox _sourceBox;
    private readonly CardPanel _sourceCard;
    private readonly Label _sourceTitle;
    private readonly Panel _resultsHost;
    private readonly TableLayoutPanel _resultsLayout;
    private readonly List<ResultPanel> _panels = [];
    private readonly System.Windows.Forms.Timer _autoTranslateTimer;

    private bool _suppressTextChanged;
    private string _lastTranslatedText = string.Empty;

    public TranslateForm(Func<AppSettings> getSettings, Action persistSettings, TranslationService translationService)
    {
        _getSettings = getSettings;
        _persistSettings = persistSettings;
        _translationService = translationService;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = Theme.TranslationFont;
        Text = "Lingo";
        Icon = AppIcon.Get();
        // Sizable 才会在标题栏显示窗口图标，禁用最大化/最小化保持精简
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        KeyPreview = true;
        BackColor = Theme.MainBg;
        ForeColor = Theme.Text;

        _sourceBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.StressBg,
            ForeColor = Theme.Text,
            ScrollBars = ScrollBars.Vertical,
            AccessibleName = "原文",
            PlaceholderText = "输入需要翻译的文本，停顿或按 Enter 翻译",
            Margin = new Padding(0),
        };
        _sourceBox.KeyDown += OnSourceBoxKeyDown;
        _sourceBox.TextChanged += OnSourceBoxTextChanged;
        _sourceTitle = Theme.CreatePaneTitle("原文");
        _sourceCard = new CardPanel
        {
            BorderColor = Theme.PaneBorder,
        };
        _sourceCard.Controls.Add(_sourceBox);
        _sourceCard.Controls.Add(_sourceTitle);
        _sourceBox.GotFocus += (_, _) => _sourceCard.BorderColor = Theme.BorderFocus;
        _sourceBox.LostFocus += (_, _) => _sourceCard.BorderColor = Theme.PaneBorder;

        _resultsHost = new Panel { AutoScroll = true };
        _resultsLayout = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1 };
        _resultsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _resultsHost.Controls.Add(_resultsLayout);
        Controls.Add(_sourceCard);
        Controls.Add(_resultsHost);

        // 停止输入片刻后自动翻译，行为与 AIxyz 一致
        _autoTranslateTimer = new System.Windows.Forms.Timer { Interval = 700 };
        _autoTranslateTimer.Tick += (_, _) =>
        {
            _autoTranslateTimer.Stop();
            StartTranslation(_sourceBox.Text.Trim(), _getSettings(), force: false);
        };

        // 启动时按当前启用的翻译服务预建结果面板，后续显示直接复用
        RebuildPanels(Math.Max(1, TranslationService.CreateEnabledTranslators(_getSettings()).Count));
        ResumeLayout();
        ClientSizeChanged += (_, _) => LayoutPanes();
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
        LayoutPanes();
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
        UpdateSourceScrollBar();
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
        TtsService.Stop();
        SaveWindowBounds();
        // 仅隐藏不销毁，窗体与面板常驻后台，下次显示直接复用
        Hide();
        // 归还显示期间触碰的闲置物理页，避免反复开关后驻留占用缓慢增长
        MemoryTrimmer.ReleaseWorkingSet();
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
            Dpi = DeviceDpi,
        };
        _persistSettings();
    }

    private void PositionWindow(AppSettings settings)
    {
        Screen cursorScreen = Screen.FromPoint(Cursor.Position);
        Rectangle workingArea = cursorScreen.WorkingArea;

        // 先将隐藏窗口移到目标屏幕，等系统更新窗口 DPI 后再恢复尺寸。
        if (!Screen.FromHandle(Handle).Equals(cursorScreen))
        {
            Bounds = CenterIn(workingArea, new Size(
                Math.Min(Width, workingArea.Width), Math.Min(Height, workingArea.Height)));
        }

        UpdateMinimumSize();
        WindowBounds saved = settings.TranslateWindow;
        Rectangle savedBounds = new(saved.X, saved.Y, saved.Width, saved.Height);
        // 旧版只存像素尺寸，首次恢复按原尺寸处理。
        int savedDpi = saved.Dpi > 0 ? saved.Dpi : DeviceDpi;
        Size size = saved.IsEmpty
            ? SizeFromClientSize(LogicalToDeviceUnits(new Size(780, 250)))
            : new Size((int)Math.Round(saved.Width * (double)DeviceDpi / savedDpi),
                (int)Math.Round(saved.Height * (double)DeviceDpi / savedDpi));
        size = new Size(
            Math.Clamp(size.Width, Math.Min(MinimumSize.Width, workingArea.Width), workingArea.Width),
            Math.Clamp(size.Height, Math.Min(MinimumSize.Height, workingArea.Height), workingArea.Height));

        if (!saved.IsEmpty && workingArea.IntersectsWith(savedBounds) &&
            Screen.FromRectangle(savedBounds).Equals(cursorScreen))
        {
            Bounds = new Rectangle(
                Math.Clamp(saved.X, workingArea.Left, workingArea.Right - size.Width),
                Math.Clamp(saved.Y, workingArea.Top, workingArea.Bottom - size.Height),
                size.Width, size.Height);
        }
        else
        {
            Bounds = CenterIn(workingArea, size);
        }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        BeginInvoke(() =>
        {
            UpdateMinimumSize();
            LayoutPanes();
        });
    }

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

        _translationService.StartTranslation(text, translators,
            onPartial: (translator, partial) =>
            {
                if (IsDisposed)
                {
                    return;
                }

                try
                {
                    // 流式 token 到达即刷新译文
                    BeginInvoke(() =>
                    {
                        if (panelMap.TryGetValue(translator, out ResultPanel? panel) && !panel.IsDisposed)
                        {
                            panel.ShowStreaming(partial);
                        }
                    });
                }
                catch (InvalidOperationException)
                {
                    // 窗口句柄在回调前被销毁（程序退出），直接丢弃
                }
            },
            onResult: (translator, result) =>
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

    // 按启用的翻译服务数量增删结果面板
    private void RebuildPanels(int count)
    {
        if (_panels.Count == count)
        {
            return;
        }

        _resultsLayout.SuspendLayout();
        while (_panels.Count > count)
        {
            ResultPanel extra = _panels[^1];
            _panels.RemoveAt(_panels.Count - 1);
            _resultsLayout.Controls.Remove(extra);
            extra.Dispose();
        }

        while (_panels.Count < count)
        {
            ResultPanel panel = new(string.Empty)
            {
                SourceTextProvider = () => _sourceBox.Text,
            };
            _resultsLayout.Controls.Add(panel, 0, _panels.Count);
            _panels.Add(panel);
        }

        _resultsLayout.RowCount = count;
        _resultsLayout.RowStyles.Clear();
        for (int i = 0; i < count; i++)
        {
            _resultsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / count));
        }

        _resultsLayout.ResumeLayout();
        if (IsHandleCreated)
        {
            LayoutPanes();
        }
    }

    private void LayoutPanes()
    {
        int inset = LogicalToDeviceUnits(12);
        int gap = LogicalToDeviceUnits(10);
        int width = Math.Max(0, ClientSize.Width - 2 * inset);
        int height = Math.Max(0, ClientSize.Height - 2 * inset);
        bool sideBySide = ClientSize.Width >= LogicalToDeviceUnits(560);
        int sourceWidth = sideBySide ? (width - gap) * 43 / 100 : width;
        int sourceHeight = sideBySide ? height : Math.Min(LogicalToDeviceUnits(96), height);
        int resultsX = sideBySide ? sourceWidth + gap : 0;
        int resultsY = sideBySide ? 0 : sourceHeight + gap;

        _sourceCard.SuspendLayout();
        _sourceCard.Padding = new Padding(inset, LogicalToDeviceUnits(8), inset, LogicalToDeviceUnits(8));
        _sourceTitle.Height = Math.Max(_sourceTitle.PreferredHeight, LogicalToDeviceUnits(26));
        _sourceCard.SetBounds(inset, inset, sourceWidth, sourceHeight);
        _sourceCard.ResumeLayout();
        UpdateSourceScrollBar();

        _resultsLayout.SuspendLayout();
        _resultsHost.SetBounds(inset + resultsX, inset + resultsY,
            Math.Max(0, width - resultsX), Math.Max(0, height - resultsY));
        // 结果多时滚动整列，每个结果仍有独立的正文滚动区。
        _resultsLayout.Height = Math.Max(_resultsHost.ClientSize.Height,
            _panels.Count * LogicalToDeviceUnits(104) - gap);
        for (int i = 0; i < _panels.Count; i++)
        {
            _panels[i].Margin = new Padding(0, 0, 0, i == _panels.Count - 1 ? 0 : gap);
            _panels[i].UpdateMetrics(DeviceDpi);
        }
        _resultsLayout.ResumeLayout();
    }

    private void UpdateSourceScrollBar()
    {
        if (!_sourceBox.IsHandleCreated)
        {
            return;
        }

        int lines = _sourceBox.GetLineFromCharIndex(_sourceBox.TextLength) + 1;
        float lineHeight = _sourceBox.Font.GetHeight(_sourceBox.DeviceDpi);
        // 切换原生滚动条的显示状态，保留输入框句柄及撤销记录。
        ShowScrollBar(_sourceBox.Handle, VerticalScrollBar, lines * lineHeight > _sourceBox.ClientSize.Height);
    }

    private void UpdateMinimumSize()
    {
        Size min = SizeFromClientSize(LogicalToDeviceUnits(new Size(340, 234)));
        Size workingArea = Screen.FromHandle(Handle).WorkingArea.Size;
        MinimumSize = new Size(Math.Min(min.Width, workingArea.Width), Math.Min(min.Height, workingArea.Height));
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowScrollBar(IntPtr hWnd, int bar, [MarshalAs(UnmanagedType.Bool)] bool show);
}
