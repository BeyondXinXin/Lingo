using Lingo.Models;
using Lingo.Services;
using System.ComponentModel;

namespace Lingo.Forms;

// 翻译窗口中单个翻译服务的结果卡片：AIxyz 风格圆角亮边框，图标按钮仅在鼠标移入时显示
internal sealed class ResultPanel : CardPanel
{
    private readonly Label _titleLabel;
    private readonly IconButton _speakSourceButton;
    private readonly IconButton _speakResultButton;
    private readonly IconButton _copyButton;
    private readonly Panel _header;
    private readonly ScrollFreeRichTextBox _textBox;
    private readonly SlimScrollBar _scrollBar;

    private bool _hasResult;

    public ResultPanel(string title)
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(12, 6, 12, 8);
        CornerRadius = 10;
        BorderWidth = 2F;
        BorderColor = Theme.BorderStrong;
        BackColor = Theme.MainBg;

        _header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 28,
            BackColor = Theme.MainBg,
        };

        _titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            Location = new Point(0, 4),
            ForeColor = Theme.TextMuted,
            BackColor = Theme.MainBg,
        };

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
            BackColor = Theme.MainBg,
            ForeColor = Theme.Text,
        };
        _scrollBar = new SlimScrollBar
        {
            Visible = false,
            BackColor = Theme.MainBg,
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
        LayoutHeader();
        LayoutScrollBar();

        HookHoverTracking(this);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title
    {
        get => _titleLabel.Text;
        set => _titleLabel.Text = value;
    }

    public void ShowLoading()
    {
        _hasResult = false;
        SetText("翻译中…", Theme.TextMuted);
        UpdateButtonVisibility();
    }

    public void ShowIdle(string message)
    {
        _hasResult = false;
        SetText(message, Theme.TextMuted);
        UpdateButtonVisibility();
    }

    public void ShowResult(TranslationResult result)
    {
        if (result.Success)
        {
            _hasResult = result.Text.Length > 0;
            SetText(result.Text, Theme.Text);
        }
        else
        {
            _hasResult = false;
            SetText(result.ErrorMessage, Theme.Danger);
        }

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
        int top = Padding.Top + _header.Height + 2;
        _scrollBar.SetBounds(Width - Padding.Right + 3, top, 6, Height - top - Padding.Bottom - 2);
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
        return lastTop + _textBox.Font.Height + 2;
    }

    private void OnCopyClicked(object? sender, EventArgs e)
    {
        if (_hasResult)
        {
            ClipboardService.TrySetText(_textBox.Text);
        }
    }

    // 朗读功能暂未实现，先占位图标
    private void OnSpeakSourceClicked(object? sender, EventArgs e)
    {
    }

    private void OnSpeakResultClicked(object? sender, EventArgs e)
    {
    }

    private void LayoutHeader()
    {
        int right = _header.Width;
        foreach (IconButton button in new[] { _copyButton, _speakResultButton, _speakSourceButton })
        {
            right -= button.Width + 2;
            button.Location = new Point(right, 2);
        }
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
        if (hovered)
        {
            LayoutHeader();
        }
    }
}
