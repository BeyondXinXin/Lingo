using Lingo.Models;
using Lingo.Services;
using System.ComponentModel;

namespace Lingo.Forms;

// 单个翻译服务的结果卡片，操作按钮在鼠标移入时显示
internal sealed class ResultPanel : CardPanel
{
    private readonly Label _titleLabel;
    private readonly IconButton _speakSourceButton;
    private readonly IconButton _speakResultButton;
    private readonly IconButton _copyButton;
    private readonly Panel _header;
    private readonly ScrollFreeRichTextBox _textBox;
    private readonly SlimScrollBar _scrollBar;

    private int _layoutDpi;
    private bool _hasResult;
    private string? _streamText;

    public ResultPanel(string title)
    {
        Dock = DockStyle.Fill;
        BorderColor = Theme.PaneBorder;

        _header = new Panel { Dock = DockStyle.Top };
        _titleLabel = Theme.CreatePaneTitle(title);
        _titleLabel.Dock = DockStyle.None;

        // 图标与 AIxyz 翻译助手一致：朗读原文、朗读翻译结果、复制翻译结果
        _speakSourceButton = new IconButton(IconFont.MediaOutput, "朗读原文") { Visible = false };
        _speakResultButton = new IconButton(IconFont.Headphones, "朗读翻译结果") { Visible = false };
        _copyButton = new IconButton(IconFont.ContentCopy, "复制翻译结果") { Visible = false };
        _speakSourceButton.Click += OnSpeakSourceClicked;
        _speakResultButton.Click += OnSpeakResultClicked;
        _copyButton.Click += OnCopyClicked;

        // 隐藏原生滚动条，改用右侧的自绘细滚动条，仅内容超出时显示
        _textBox = new ScrollFreeRichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.StressBg,
            ForeColor = Theme.Text,
        };
        _scrollBar = new SlimScrollBar
        {
            Visible = false,
            BackColor = Theme.StressBg,
        };
        _textBox.Scrolled += (_, _) => UpdateScrollBar();
        _textBox.ClientSizeChanged += (_, _) => UpdateScrollBar();
        _scrollBar.ValueChanged += value => _textBox.ScrollTop = value;

        _header.Controls.Add(_titleLabel);
        _header.Controls.Add(_speakSourceButton);
        _header.Controls.Add(_speakResultButton);
        _header.Controls.Add(_copyButton);
        Controls.Add(_textBox);
        Controls.Add(_header);
        Controls.Add(_scrollBar);
        _scrollBar.BringToFront();

        _header.Resize += (_, _) => LayoutHeader();
        Resize += (_, _) => LayoutScrollBar();
        HookHoverTracking(this);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title
    {
        get => _titleLabel.Text;
        set => _titleLabel.Text = value;
    }

    // 由父窗口注入，供“朗读原文”取当前输入框文本
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string>? SourceTextProvider { get; set; }

    public void UpdateMetrics(int dpi)
    {
        _layoutDpi = dpi;
        int horizontal = ScaleMetric(12);
        int vertical = ScaleMetric(8);
        SuspendLayout();
        Padding = new Padding(horizontal, vertical, horizontal, vertical);
        _header.Height = Math.Max(_titleLabel.PreferredHeight, ScaleMetric(26));
        Size buttonSize = new(ScaleMetric(26), ScaleMetric(24));
        _speakSourceButton.Size = buttonSize;
        _speakResultButton.Size = buttonSize;
        _copyButton.Size = buttonSize;
        ResumeLayout();
        LayoutHeader();
        LayoutScrollBar();
    }

    private int ScaleMetric(int value) => (int)Math.Round(value * (_layoutDpi > 0 ? _layoutDpi : DeviceDpi) / 96F);

    public void ShowLoading()
    {
        _hasResult = false;
        _streamText = null;
        SetText("翻译中…", Theme.TextMuted);
        UpdateButtonVisibility();
    }

    public void ShowIdle(string message)
    {
        _hasResult = false;
        _streamText = null;
        SetText(message, Theme.TextMuted);
        UpdateButtonVisibility();
    }

    // 流式输出途中的增量刷新：partial 为已累积的完整译文，尽量只追加后缀避免整段重设闪烁
    public void ShowStreaming(string partial)
    {
        if (partial.Length == 0)
        {
            return;
        }

        if (_streamText is not null && partial.StartsWith(_streamText, StringComparison.Ordinal))
        {
            _textBox.AppendText(partial[_streamText.Length..]);
            UpdateScrollBar();
        }
        else
        {
            SetText(partial, Theme.Text);
        }

        _streamText = partial;
    }

    public void ShowResult(TranslationResult result)
    {
        if (result.Success)
        {
            _hasResult = result.Text.Length > 0;
            // 流式已把全文显示完整时不再重设，避免收尾时跳动
            if (_streamText is null || !string.Equals(_streamText.Trim(), result.Text, StringComparison.Ordinal))
            {
                SetText(result.Text, Theme.Text);
            }
        }
        else
        {
            _hasResult = false;
            SetText(result.ErrorMessage, Theme.Danger);
        }

        _streamText = null;
        UpdateButtonVisibility();
    }

    private void SetText(string text, Color color)
    {
        _textBox.ForeColor = color;
        _textBox.Text = text;
        _textBox.ScrollTop = 0;
        UpdateScrollBar();
    }

    // 右侧细滚动条位于内边距空白带内，不与文本重叠
    private void LayoutScrollBar()
    {
        int inset = ScaleMetric(2);
        int top = Padding.Top + _header.Height + inset;
        _scrollBar.SetBounds(Width - Padding.Right + inset, top,
            ScaleMetric(6), Math.Max(0, Height - top - Padding.Bottom - inset));
        UpdateScrollBar();
    }

    private void UpdateScrollBar()
    {
        int view = _textBox.ClientSize.Height;
        int content = ContentHeight();
        bool overflow = content > view + 2 && view > 0;
        _scrollBar.Visible = overflow;
        if (overflow)
        {
            _scrollBar.SetMetrics(content, view, _textBox.ScrollTop);
        }
    }

    private int ContentHeight()
    {
        if (_textBox.TextLength == 0)
        {
            return 0;
        }

        // 末字符的绝对 Y 坐标 + 行高 ≈ 内容总高度
        int lastTop = _textBox.GetPositionFromCharIndex(_textBox.TextLength - 1).Y + _textBox.ScrollTop;
        return lastTop + (int)Math.Ceiling(_textBox.Font.GetHeight(DeviceDpi)) + ScaleMetric(2);
    }

    private void OnCopyClicked(object? sender, EventArgs e)
    {
        if (_hasResult)
        {
            ClipboardService.TrySetText(_textBox.Text);
        }
    }

    private void OnSpeakSourceClicked(object? sender, EventArgs e)
    {
        string? text = SourceTextProvider?.Invoke();
        if (!string.IsNullOrWhiteSpace(text))
        {
            TtsService.Toggle(text);
        }
    }

    private void OnSpeakResultClicked(object? sender, EventArgs e)
    {
        if (_hasResult)
        {
            TtsService.Toggle(_textBox.Text);
        }
    }

    private void LayoutHeader()
    {
        int right = _header.Width;
        int gap = ScaleMetric(2);
        foreach (IconButton button in new[] { _copyButton, _speakResultButton, _speakSourceButton })
        {
            if (button.Visible)
            {
                right -= button.Width + gap;
                button.Location = new Point(right, (_header.Height - button.Height) / 2);
            }
        }

        _titleLabel.SetBounds(0, 0, Math.Max(0, right), _header.Height);
    }

    // 鼠标移入卡片任意区域时显示图标按钮，完全移出后隐藏
    private void HookHoverTracking(Control control)
    {
        control.MouseEnter += (_, _) => UpdateButtonVisibility();
        control.MouseLeave += (_, _) => UpdateButtonVisibility();
        foreach (Control child in control.Controls)
        {
            HookHoverTracking(child);
        }
    }

    private void UpdateButtonVisibility()
    {
        bool hovered = ClientRectangle.Contains(PointToClient(MousePosition));
        _speakSourceButton.Visible = hovered;
        _speakResultButton.Visible = hovered;
        _copyButton.Visible = hovered && _hasResult;
        LayoutHeader();
    }
}
