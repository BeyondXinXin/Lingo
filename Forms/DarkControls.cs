using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Lingo.Forms;

// 圆角描边卡片容器，AIxyz 风格的分组面板
internal class CardPanel : Panel
{
    private Color _borderColor = Theme.Border;

    public CardPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.StressBg;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 8;

    // AIxyz 翻译编辑框为 2px 轮廓，默认 1px 用于设置页等普通卡片
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float BorderWidth { get; set; } = 1F;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor
    {
        get => _borderColor;
        set
        {
            _borderColor = value;
            Invalidate();
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // 四角先用父容器背景填充，避免圆角外露出本控件底色
        using SolidBrush parentBrush = new(Parent?.BackColor ?? Theme.MainBg);
        e.Graphics.FillRectangle(parentBrush, ClientRectangle);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath path = Theme.RoundedRectangle(BorderBounds(), CornerRadius);
        using SolidBrush background = new(BackColor);
        e.Graphics.FillPath(background, path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath path = Theme.RoundedRectangle(BorderBounds(), CornerRadius);
        using Pen pen = new(_borderColor, BorderWidth);
        e.Graphics.DrawPath(pen, path);
        base.OnPaint(e);
    }

    // 粗边框需向内收缩，避免描边被控件边缘裁剪
    private Rectangle BorderBounds()
    {
        int inset = (int)Math.Floor(BorderWidth / 2F);
        return new Rectangle(inset, inset, Width - inset * 2 - 1, Height - inset * 2 - 1);
    }
}

// 圆角输入框：内部承载无边框的 TextBox / NumericUpDown，聚焦时边框高亮
internal sealed class RoundedInput : CardPanel
{
    public RoundedInput(Control inner)
    {
        CornerRadius = 6;
        Padding = new Padding(9, 8, 9, 8);
        Height = 42;

        switch (inner)
        {
            case TextBoxBase textBox:
                textBox.BorderStyle = BorderStyle.None;
                break;
            case UpDownBase upDown:
                upDown.BorderStyle = BorderStyle.None;
                break;
        }

        inner.Dock = DockStyle.Fill;
        inner.BackColor = Theme.StressBg;
        inner.ForeColor = Theme.Text;
        inner.GotFocus += (_, _) => BorderColor = Theme.BorderFocus;
        inner.LostFocus += (_, _) => BorderColor = Theme.Border;
        Controls.Add(inner);
    }
}

// 圆角扁平按钮，悬停/按下时加深背景
internal sealed class DarkButton : Button
{
    private bool _hovered;
    private bool _pressed;

    public DarkButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        ForeColor = Theme.Text;
        Cursor = Cursors.Hand;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 6;

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        Graphics g = pevent.Graphics;
        using SolidBrush parentBrush = new(Parent?.BackColor ?? Theme.MainBg);
        g.FillRectangle(parentBrush, ClientRectangle);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color background = _pressed ? Theme.PressedBg : _hovered ? Theme.HoverBg : Theme.TweakBg;
        Rectangle rect = new(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = Theme.RoundedRectangle(rect, CornerRadius);
        using SolidBrush brush = new(background);
        g.FillPath(brush, path);
        using Pen pen = new(Focused ? Theme.BorderFocus : Theme.Border);
        g.DrawPath(pen, path);

        TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

// 无边框图标按钮：用 Material Symbols 字体绘制单个图标，悬停时显示圆角背景
internal sealed class IconButton : Button
{
    private static readonly Font GlyphFont = IconFont.Create(12F);

    private readonly char _glyph;
    private bool _hovered;
    private bool _pressed;

    public IconButton(char glyph, string tooltip)
    {
        _glyph = glyph;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Size = new Size(28, 26);
        TabStop = false;
        Cursor = Cursors.Hand;
        AccessibleName = tooltip;
        new ToolTip().SetToolTip(this, tooltip);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        Graphics g = pevent.Graphics;
        using SolidBrush parentBrush = new(Parent?.BackColor ?? Theme.MainBg);
        g.FillRectangle(parentBrush, ClientRectangle);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (_hovered || _pressed)
        {
            using GraphicsPath path = Theme.RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), 5);
            using SolidBrush hoverBrush = new(_pressed ? Theme.PressedBg : Theme.HoverBg);
            g.FillPath(hoverBrush, path);
        }

        // 私有字体必须走 GDI+ 绘制，TextRenderer(GDI) 无法使用 PrivateFontCollection
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        using SolidBrush textBrush = new(_hovered ? Color.White : Theme.Text);
        using StringFormat format = new()
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        g.DrawString(_glyph.ToString(), GlyphFont, textBrush, ClientRectangle, format);
    }
}

// 隐藏原生滚动条的富文本框：滚轮滚动手动驱动，滚动状态通过 Scrolled 事件同步给外部
internal sealed class ScrollFreeRichTextBox : RichTextBox
{
    private const int EmLineScroll = 0x00B6;
    private const int EmGetScrollPos = 0x0400 + 221;
    private const int EmSetScrollPos = 0x0400 + 222;

    public ScrollFreeRichTextBox()
    {
        ScrollBars = RichTextBoxScrollBars.None;
    }

    public event EventHandler? Scrolled;

    // 当前垂直滚动偏移（像素）
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ScrollTop
    {
        get
        {
            Point pos = default;
            _ = SendMessage(Handle, EmGetScrollPos, IntPtr.Zero, ref pos);
            return pos.Y;
        }
        set
        {
            Point pos = new(0, Math.Max(0, value));
            _ = SendMessage(Handle, EmSetScrollPos, IntPtr.Zero, ref pos);
            Scrolled?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        // 无滚动条样式时 richedit 不响应滚轮，按系统行数手动滚动
        int lines = SystemInformation.MouseWheelScrollLines;
        if (lines <= 0)
        {
            lines = 3;
        }

        _ = SendMessage(Handle, EmLineScroll, IntPtr.Zero, e.Delta > 0 ? -lines : lines);
        Scrolled?.Invoke(this, EventArgs.Empty);
        if (e is HandledMouseEventArgs handled)
        {
            handled.Handled = true;
        }

        base.OnMouseWheel(e);
    }

    protected override void OnVScroll(EventArgs e)
    {
        base.OnVScroll(e);
        Scrolled?.Invoke(this, EventArgs.Empty);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref Point lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, int lParam);
}

// 细圆角滚动条：替代原生滚动条，仅在内容超出可视区时由宿主显示
internal sealed class SlimScrollBar : Control
{
    private int _content;
    private int _view;
    private int _value;
    private bool _dragging;
    private int _dragOffset;
    private bool _hovered;

    public SlimScrollBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Width = 8;
        TabStop = false;
    }

    public event Action<int>? ValueChanged;

    public void SetMetrics(int contentHeight, int viewHeight, int value)
    {
        _content = contentHeight;
        _view = viewHeight;
        _value = Math.Clamp(value, 0, Math.Max(0, contentHeight - viewHeight));
        Invalidate();
    }

    private int MaxValue => Math.Max(1, _content - _view);

    private Rectangle ThumbBounds
    {
        get
        {
            if (_content <= _view || Height <= 0)
            {
                return Rectangle.Empty;
            }

            int thumbHeight = Math.Clamp((int)((float)_view / _content * Height), Math.Min(24, Height), Height);
            int y = (int)((float)_value / MaxValue * (Height - thumbHeight));
            return new Rectangle(0, y, Width, thumbHeight);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Rectangle thumb = ThumbBounds;
        if (thumb.IsEmpty)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath path = Theme.RoundedRectangle(
            new Rectangle(thumb.X, thumb.Y, thumb.Width - 1, thumb.Height - 1), Width / 2);
        // 提高亮度保证深色背景下可见
        using SolidBrush brush = new(_dragging || _hovered ? Theme.BorderStrong : Color.FromArgb(122, 126, 131));
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Rectangle thumb = ThumbBounds;
        if (thumb.IsEmpty)
        {
            return;
        }

        if (thumb.Contains(e.Location))
        {
            _dragOffset = e.Y - thumb.Y;
        }
        else
        {
            // 点击轨道空白处：滑块中心跳到点击位置
            _dragOffset = thumb.Height / 2;
            MoveThumbTo(e.Y - _dragOffset, thumb.Height);
        }

        _dragging = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            MoveThumbTo(e.Y - _dragOffset, ThumbBounds.Height);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    private void MoveThumbTo(int thumbY, int thumbHeight)
    {
        int track = Height - thumbHeight;
        if (track <= 0)
        {
            return;
        }

        int value = (int)((float)Math.Clamp(thumbY, 0, track) / track * MaxValue);
        if (value != _value)
        {
            _value = value;
            Invalidate();
            ValueChanged?.Invoke(value);
        }
    }
}
