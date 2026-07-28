using Lingo.Models;
using Lingo.Services;

namespace Lingo.Forms;

// 翻译窗口中单个翻译服务的结果卡片：圆角深色面板内含标题、状态、复制按钮和结果文本
internal sealed class ResultPanel : CardPanel
{
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly DarkButton _copyButton;
    private readonly TextBox _textBox;

    public ResultPanel(string title)
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(12, 8, 12, 10);
        CornerRadius = 10;

        Panel header = new()
        {
            Dock = DockStyle.Top,
            Height = 28,
            BackColor = Theme.StressBg,
        };

        _titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            Location = new Point(0, 6),
            ForeColor = Theme.TextMuted,
        };

        _statusLabel = new Label
        {
            AutoSize = true,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.StressBg,
        };

        _copyButton = new DarkButton
        {
            Text = "复制",
            Size = new Size(56, 26),
            TabStop = false,
            Visible = false,
        };
        _copyButton.Click += OnCopyClicked;

        _textBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.StressBg,
            ForeColor = Theme.Text,
        };

        header.Controls.Add(_titleLabel);
        header.Controls.Add(_statusLabel);
        header.Controls.Add(_copyButton);
        Controls.Add(_textBox);
        Controls.Add(header);

        header.Resize += (_, _) => LayoutHeader(header);
        _statusLabel.SizeChanged += (_, _) => LayoutHeader(header);
        LayoutHeader(header);
    }

    public string TranslatorName => _titleLabel.Text;

    public void ShowLoading()
    {
        _statusLabel.Text = "翻译中…";
        _copyButton.Visible = false;
        _textBox.ForeColor = Theme.TextMuted;
        _textBox.Text = string.Empty;
    }

    public void ShowIdle(string message)
    {
        _statusLabel.Text = string.Empty;
        _copyButton.Visible = false;
        _textBox.ForeColor = Theme.TextMuted;
        _textBox.Text = message;
    }

    public void ShowResult(TranslationResult result)
    {
        if (result.Success)
        {
            _statusLabel.Text = $"{result.Elapsed.TotalSeconds:0.0}s";
            _textBox.ForeColor = Theme.Text;
            _textBox.Text = result.Text;
            _copyButton.Visible = result.Text.Length > 0;
        }
        else
        {
            _statusLabel.Text = "失败";
            _textBox.ForeColor = Theme.Danger;
            _textBox.Text = result.ErrorMessage;
            _copyButton.Visible = false;
        }
    }

    private void OnCopyClicked(object? sender, EventArgs e)
    {
        if (_textBox.Text.Length > 0 && ClipboardService.TrySetText(_textBox.Text))
        {
            _statusLabel.Text = "已复制";
        }
    }

    private void LayoutHeader(Panel header)
    {
        _copyButton.Location = new Point(header.Width - _copyButton.Width, 0);
        _statusLabel.Location = new Point(_copyButton.Left - _statusLabel.Width - 8, 6);
    }
}
