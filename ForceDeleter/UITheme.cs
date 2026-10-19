using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ForceDeleter;

/// <summary>
/// Modern dark-themed custom UI drawing utilities and controls.
/// Provides rounded shapes, smooth anti-aliased vector icons, and glowing buttons.
/// </summary>
public static class UITheme
{
    // Color Palette matching the user's design
    public static readonly Color BgApp = Color.FromArgb(17, 19, 28);           // Main dark background
    public static readonly Color BgSidebar = Color.FromArgb(15, 17, 24);       // Left rail
    public static readonly Color BgCardContainer = Color.FromArgb(23, 27, 39); // Main card container
    public static readonly Color BgItemCard = Color.FromArgb(31, 37, 52);      // Each item row
    public static readonly Color BgItemCardHover = Color.FromArgb(38, 45, 64);
    
    // Accent Colors
    public static readonly Color AccentRed = Color.FromArgb(244, 63, 94);       // Coral / Red
    public static readonly Color AccentRedDark = Color.FromArgb(225, 29, 72);
    public static readonly Color AccentRedGlow = Color.FromArgb(100, 244, 63, 94);
    
    public static readonly Color AccentBlue = Color.FromArgb(59, 130, 246);     // Folders
    public static readonly Color AccentPurple = Color.FromArgb(168, 85, 247);   // Media / Images
    public static readonly Color AccentGray = Color.FromArgb(148, 163, 184);    // Docs
    
    // Status Dots
    public static readonly Color DotGreen = Color.FromArgb(34, 197, 94);       // Ready / Unlocked
    public static readonly Color DotOrange = Color.FromArgb(245, 158, 11);     // Locked
    public static readonly Color DotRed = Color.FromArgb(239, 68, 68);         // Protected
    
    // Button & Borders
    public static readonly Color BgSubtleBtn = Color.FromArgb(33, 38, 54);
    public static readonly Color BgSubtleBtnHover = Color.FromArgb(43, 50, 70);
    public static readonly Color BorderSubtle = Color.FromArgb(45, 52, 74);
    public static readonly Color TextPrimary = Color.FromArgb(255, 255, 255);
    public static readonly Color TextSecondary = Color.FromArgb(148, 163, 184);
    public static readonly Color TextMuted = Color.FromArgb(100, 116, 139);

    public static GraphicsPath GetRoundedPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        int d = radius * 2;
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

    // Vector Icon Drawing Helpers
    public static void DrawTrashIcon(Graphics g, Rectangle r, Color color)
    {
        using var pen = new Pen(color, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        int cx = r.X + r.Width / 2;
        int cy = r.Y + r.Height / 2;

        // Top lid
        g.DrawLine(pen, cx - 8, cy - 6, cx + 8, cy - 6);
        g.DrawLine(pen, cx - 3, cy - 8, cx + 3, cy - 8);

        // Can body
        g.DrawLine(pen, cx - 6, cy - 6, cx - 5, cy + 8);
        g.DrawLine(pen, cx + 6, cy - 6, cx + 5, cy + 8);
        g.DrawLine(pen, cx - 5, cy + 8, cx + 5, cy + 8);

        // Inner vertical slats
        g.DrawLine(pen, cx - 2, cy - 3, cx - 2, cy + 5);
        g.DrawLine(pen, cx + 2, cy - 3, cx + 2, cy + 5);
    }

    public static void DrawFolderIcon(Graphics g, Rectangle r, Color color)
    {
        using var brush = new SolidBrush(color);
        int cx = r.X + r.Width / 2;
        int cy = r.Y + r.Height / 2;

        // Folder outline
        using var pen = new Pen(color, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        var pts = new Point[]
        {
            new(cx - 8, cy - 6),
            new(cx - 3, cy - 6),
            new(cx - 1, cy - 3),
            new(cx + 8, cy - 3),
            new(cx + 8, cy + 7),
            new(cx - 8, cy + 7)
        };
        g.DrawPolygon(pen, pts);
    }

    public static void DrawClockIcon(Graphics g, Rectangle r, Color color)
    {
        using var pen = new Pen(color, 2f);
        int cx = r.X + r.Width / 2;
        int cy = r.Y + r.Height / 2;

        g.DrawEllipse(pen, cx - 8, cy - 8, 16, 16);
        g.DrawLine(pen, cx, cy - 5, cx, cy);
        g.DrawLine(pen, cx, cy, cx + 4, cy);
    }

    public static void DrawGearIcon(Graphics g, Rectangle r, Color color)
    {
        using var pen = new Pen(color, 2f);
        int cx = r.X + r.Width / 2;
        int cy = r.Y + r.Height / 2;

        g.DrawEllipse(pen, cx - 4, cy - 4, 8, 8);
        for (int i = 0; i < 8; i++)
        {
            double angle = i * Math.PI / 4.0;
            int x1 = cx + (int)(6 * Math.Cos(angle));
            int y1 = cy + (int)(6 * Math.Sin(angle));
            int x2 = cx + (int)(9 * Math.Cos(angle));
            int y2 = cy + (int)(9 * Math.Sin(angle));
            g.DrawLine(pen, x1, y1, x2, y2);
        }
    }

    public static void DrawCloseIcon(Graphics g, Rectangle r, Color color)
    {
        using var pen = new Pen(color, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        int cx = r.X + r.Width / 2;
        int cy = r.Y + r.Height / 2;
        int sz = 4;
        g.DrawLine(pen, cx - sz, cy - sz, cx + sz, cy + sz);
        g.DrawLine(pen, cx + sz, cy - sz, cx - sz, cy + sz);
    }

    public static void DrawFileDocIcon(Graphics g, Rectangle r, Color color)
    {
        using var pen = new Pen(color, 1.8f) { LineJoin = LineJoin.Round };
        int cx = r.X + r.Width / 2;
        int cy = r.Y + r.Height / 2;

        // Paper outline with folded corner
        var pts = new Point[]
        {
            new(cx - 5, cy - 7),
            new(cx + 2, cy - 7),
            new(cx + 6, cy - 3),
            new(cx + 6, cy + 7),
            new(cx - 5, cy + 7)
        };
        g.DrawPolygon(pen, pts);
        g.DrawLine(pen, cx + 2, cy - 7, cx + 2, cy - 3);
        g.DrawLine(pen, cx + 2, cy - 3, cx + 6, cy - 3);
    }

    public static void DrawImageIcon(Graphics g, Rectangle r, Color color)
    {
        using var pen = new Pen(color, 1.8f) { LineJoin = LineJoin.Round };
        int cx = r.X + r.Width / 2;
        int cy = r.Y + r.Height / 2;

        g.DrawRectangle(pen, cx - 7, cy - 6, 14, 12);
        // Sun/Dot
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, cx - 4, cy - 4, 3, 3);
        // Mountain lines
        g.DrawLine(pen, cx - 6, cy + 4, cx - 2, cy);
        g.DrawLine(pen, cx - 2, cy, cx + 1, cy + 3);
        g.DrawLine(pen, cx + 1, cy + 3, cx + 3, cy + 1);
        g.DrawLine(pen, cx + 3, cy + 1, cx + 6, cy + 4);
    }
}
