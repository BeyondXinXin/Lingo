using System.ComponentModel;
using System.Drawing.Drawing2D;

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
        using GraphicsPath path = Theme.RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius);
        using SolidBrush background = new(BackColor);
        e.Graphics.FillPath(background, path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath path = Theme.RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius);
        using Pen pen = new(_borderColor);
        e.Graphics.DrawPath(pen, path);
        base.OnPaint(e);
    }
}

// 圆角输入框：内部承载无边框的 TextBox / NumericUpDown，聚焦时边框高亮
internal sealed class RoundedInput : CardPanel
{
    public RoundedInput(Control inner)
    {
        CornerRadius = 6;
        Padding = new Padding(9, 6, 9, 6);
        Height = 32;

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
