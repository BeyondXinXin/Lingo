using Lingo.Models;
using Lingo.Services;
using System.ComponentModel;

namespace Lingo.Forms;

// 翻译窗口中单个翻译服务的结果卡片：AIxyz 风格圆角亮边框，图标按钮仅在鼠标移入时显示
internal sealed class ResultPanel : CardPanel
{
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly IconButton _speakSourceButton;
    private readonly IconButton _speakResultButton;
    private readonly IconButton _copyButton;
    private readonly Panel _header;
    private readonly RichTextBox _textBox;

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
            Height = 26,
            BackColor = Theme.MainBg,
        };

        _titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            Location = new Point(0, 5),
            ForeColor = Theme.TextMuted,
            BackColor = Theme.MainBg,
        };

        _statusLabel = new Label
        {
            AutoSize = true,
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

        // RichTextBox 的垂直滚动条仅在内容超出时出现
        _textBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.MainBg,
            ForeColor = Theme.Text,
        };

        _header.Controls.Add(_titleLabel);
        _header.Controls.Add(_statusLabel);
        _header.Controls.Add(_speakSourceButton);
        _header.Controls.Add(_speakResultButton);
        _header.Controls.Add(_copyButton);
        Controls.Add(_textBox);
        Controls.Add(_header);

        _header.Resize += (_, _) => LayoutHeader();
        _statusLabel.SizeChanged += (_, _) => LayoutHeader();
        LayoutHeader();

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
        _statusLabel.Text = "翻译中…";
        SetText(string.Empty, Theme.TextMuted);
        UpdateButtonVisibility();
    }

    public void ShowIdle(string message)
    {
        _hasResult = false;
        _statusLabel.Text = string.Empty;
        SetText(message, Theme.TextMuted);
        UpdateButtonVisibility();
    }

    public void ShowResult(TranslationResult result)
    {
        if (result.Success)
        {
            _hasResult = result.Text.Length > 0;
            _statusLabel.Text = $"{result.Elapsed.TotalSeconds:0.0}s";
            SetText(result.Text, Theme.Text);
        }
        else
        {
            _hasResult = false;
            _statusLabel.Text = "失败";
            SetText(result.ErrorMessage, Theme.Danger);
        }

        UpdateButtonVisibility();
    }

    private void SetText(string text, Color color)
    {
        _textBox.ForeColor = color;
        _textBox.Text = text;
    }

    private void OnCopyClicked(object? sender, EventArgs e)
    {
        if (_hasResult && ClipboardService.TrySetText(_textBox.Text))
        {
            _statusLabel.Text = "已复制";
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
            button.Location = new Point(right, 1);
        }

        _statusLabel.Location = new Point(right - _statusLabel.Width - 8, 5);
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
