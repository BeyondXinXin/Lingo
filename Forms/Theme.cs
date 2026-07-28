using System.Drawing.Drawing2D;

namespace Lingo.Forms;

// AIxyz 深色主题配色，取自参考项目 theme_colors.ini 的 [Dark] 方案
internal static class Theme
{
    public static readonly Color MainBg = Color.FromArgb(33, 37, 43);       // 窗口背景
    public static readonly Color StressBg = Color.FromArgb(29, 31, 35);     // 输入区/卡片深色背景
    public static readonly Color TweakBg = Color.FromArgb(50, 56, 66);      // 按钮常态背景
    public static readonly Color HoverBg = Color.FromArgb(44, 49, 58);      // 悬停背景
    public static readonly Color PressedBg = Color.FromArgb(80, 88, 106);   // 按下背景

    public static readonly Color Text = Color.FromArgb(236, 238, 240);
    public static readonly Color TextMuted = Color.FromArgb(172, 175, 178);
    public static readonly Color Danger = Color.FromArgb(224, 108, 117);

    public static readonly Color Border = Color.FromArgb(120, 123, 126);
    public static readonly Color BorderStrong = Color.FromArgb(196, 199, 202); // 翻译编辑框的亮色轮廓（比 AIxyz 原值提亮）
    public static readonly Color BorderFocus = Color.FromArgb(29, 147, 171);

    public static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        int diameter = Math.Max(1, radius * 2);
        Rectangle arc = new(bounds.Location, new Size(diameter, diameter));
        GraphicsPath path = new();
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}
