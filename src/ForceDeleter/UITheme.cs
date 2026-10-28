using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ForceDeleter;

public static class UITheme
{
    public static bool Dark { get; set; } = true;
    public static Color BgApp => Dark ? Color.FromArgb(18, 20, 26) : Color.FromArgb(242, 244, 248);
    public static Color BgTop => BgApp;
    public static Color BgCardContainer => Dark ? Color.FromArgb(24, 27, 35) : Color.FromArgb(255, 255, 255);
    public static Color BgItemCard => Dark ? Color.FromArgb(30, 34, 44) : Color.FromArgb(248, 249, 252);
    public static Color BorderSubtle => Dark ? Color.FromArgb(46, 52, 66) : Color.FromArgb(216, 222, 232);
    public static Color TextPrimary => Dark ? Color.FromArgb(244, 246, 250) : Color.FromArgb(22, 28, 40);
    public static Color TextSecondary => Dark ? Color.FromArgb(152, 164, 184) : Color.FromArgb(86, 98, 120);
    public static Color TextMuted => Dark ? Color.FromArgb(108, 118, 138) : Color.FromArgb(128, 138, 154);
    public static readonly Color AccentRed = Color.FromArgb(224, 49, 62);
    public static readonly Color AccentBlue = Color.FromArgb(88, 145, 226);
    public static readonly Color AccentPurple = Color.FromArgb(120, 145, 185);
    public static readonly Color DotGreen = Color.FromArgb(52, 178, 110);
    public static readonly Color DotOrange = Color.FromArgb(232, 168, 60);

    public static GraphicsPath GetRoundedPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Min(Math.Min(rect.Width, rect.Height), Math.Max(0, radius * 2));
        if (d < 2) { path.AddRectangle(rect); return path; }
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void EnableAntiAlias(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    public static Font Font(float pixels, bool bold = false) =>
        new(bold ? "Segoe UI Semibold" : "Segoe UI", pixels, FontStyle.Regular, GraphicsUnit.Pixel);

    public static void Text(Graphics g, string text, Rectangle bounds, float pixels, Color color,
        bool bold = false, ContentAlignment align = ContentAlignment.MiddleLeft)
    {
        using var font = Font(pixels, bold);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat
        {
            Alignment = align == ContentAlignment.MiddleCenter ? StringAlignment.Center : StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        g.DrawString(text, font, brush, bounds, format);
    }

    public static void Badge(Graphics g, Rectangle rect, string icon, Color color)
    {
        using var path = GetRoundedPath(rect, Math.Max(6, rect.Width / 5));
        using var brush = new SolidBrush(Dark ? Color.FromArgb(37, 41, 50) : Color.FromArgb(234, 238, 245));
        g.FillPath(brush, path);
        var inset = Math.Max(5, rect.Width / 5);
        SvgIcons.Draw(g, Rectangle.Inflate(rect, -inset, -inset), icon, color);
    }
}
