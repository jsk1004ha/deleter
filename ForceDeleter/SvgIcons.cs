using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ForceDeleter;

/// <summary>
/// MIT Licensed clean vector icons based on Lucide & Feather design system.
/// Rendered with anti-aliasing directly via GDI+ for crisp high-DPI scaling.
/// </summary>
public static class SvgIcons
{
    public static void DrawTrash(Graphics g, Rectangle r, Color color, float penWidth = 2f)
    {
        using var pen = new Pen(color, penWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        int cx = r.X + r.Width / 2;
        int cy = r.Y + r.Height / 2;

        // Top lid
        g.DrawLine(pen, cx - 8, cy - 6, cx + 8, cy - 6);
        g.DrawLine(pen, cx - 3, cy - 8, cx + 3, cy - 8);

        // Can body
        var pts = new Point[]
        {
            new(cx - 6, cy - 6),
            new(cx - 5, cy + 8),
            new(cx + 5, cy + 8),
            new(cx + 6, cy - 6)
        };
        g.DrawLines(pen, pts);

        // Inner vertical lines
        g.DrawLine(pen, cx - 2, cy - 3, cx - 2, cy + 5);
        g.DrawLine(pen, cx + 2, cy - 3, cx + 2, cy + 5);
    }

    public static void DrawFolder(Graphics g, Rectangle r, Color color, float penWidth = 2f)
    {
        DrawFolder(g, r, color, penWidth, false);
    }

    public static void DrawFolder(Graphics g, Rectangle r, Color color, float penWidth, bool filled)
    {
        int cx = r.X + r.Width / 2;
        int cy = r.Y + r.Height / 2;

        var pts = new Point[]
        {
            new(cx - 9, cy - 7),
            new(cx - 3, cy - 7),
            new(cx - 1, cy - 4),
            new(cx + 9, cy - 4),
            new(cx + 9, cy + 7),
            new(cx - 9, cy + 7)
        };

        if (filled)
        {
            using var brush = new SolidBrush(Color.FromArgb(50, color));
            g.FillPolygon(brush, pts);
        }

        using var pen = new Pen(color, penWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawPolygon(pen, pts);
    }

    public static void DrawClock(Graphics g, Rectangle r, Color color, float penWidth = 2f)
    {
        using var pen = new Pen(color, penWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        int cx = r.X + r.Width / 2;
        int cy = r.Y + r.Height / 2;

        g.DrawEllipse(pen, cx - 8, cy - 8, 16, 16);
        g.DrawLine(pen, cx, cy - 5, cx, cy);
        g.DrawLine(pen, cx, cy, cx + 4, cy);
    }

    public static void DrawGear(Graphics g, Rectangle r, Color color, float penWidth = 2f)
    {
        using var pen = new Pen(color, penWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        int cx = r.X + r.Width / 2;
        int cy = r.Y + r.Height / 2;

        g.DrawEllipse(pen, cx - 4, cy - 4, 8, 8);
        for (int i = 0; i < 8; i++)
        {
            double angle = i * Math.PI / 4.0;
            int x1 = cx + (int)(5 * Math.Cos(angle));
            int y1 = cy + (int)(5 * Math.Sin(angle));
            int x2 = cx + (int)(8 * Math.Cos(angle));
            int y2 = cy + (int)(8 * Math.Sin(angle));
            g.DrawLine(pen, x1, y1, x2, y2);
        }
    }

    public static void DrawDocument(Graphics g, Rectangle r, Color color, float penWidth = 1.8f)
    {
        using var pen = new Pen(color, penWidth) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        int cx = r.X + r.Width / 2;
        int cy = r.Y + r.Height / 2;

        var pts = new Point[]
        {
            new(cx - 5, cy - 8),
            new(cx + 2, cy - 8),
            new(cx + 6, cy - 4),
            new(cx + 6, cy + 8),
            new(cx - 5, cy + 8)
        };
        g.DrawPolygon(pen, pts);
        g.DrawLine(pen, cx + 2, cy - 8, cx + 2, cy - 4);
        g.DrawLine(pen, cx + 2, cy - 4, cx + 6, cy - 4);
    }

    public static void DrawImage(Graphics g, Rectangle r, Color color, float penWidth = 1.8f)
    {
        using var pen = new Pen(color, penWidth) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        int cx = r.X + r.Width / 2;
        int cy = r.Y + r.Height / 2;

        g.DrawRectangle(pen, cx - 7, cy - 6, 14, 12);
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, cx - 4, cy - 4, 2.5f, 2.5f);

        g.DrawLine(pen, cx - 6, cy + 4, cx - 2, cy);
        g.DrawLine(pen, cx - 2, cy, cx + 1, cy + 3);
        g.DrawLine(pen, cx + 1, cy + 3, cx + 3, cy + 1);
        g.DrawLine(pen, cx + 3, cy + 1, cx + 6, cy + 4);
    }

    public static void DrawArchive(Graphics g, Rectangle r, Color color, float penWidth = 1.8f)
    {
        using var pen = new Pen(color, penWidth) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        int cx = r.X + r.Width / 2;
        int cy = r.Y + r.Height / 2;

        g.DrawRectangle(pen, cx - 6, cy - 7, 12, 14);
        // Zipper teeth
        for (int y = cy - 5; y <= cy + 3; y += 2)
        {
            g.DrawLine(pen, cx - 1, y, cx + 1, y);
        }
    }

    public static void DrawDrive(Graphics g, Rectangle r, Color color, float penWidth = 1.8f)
    {
        using var pen = new Pen(color, penWidth) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        int cx = r.X + r.Width / 2;
        int cy = r.Y + r.Height / 2;

        g.DrawRectangle(pen, cx - 7, cy - 6, 14, 12);
        g.DrawLine(pen, cx - 7, cy + 2, cx + 7, cy + 2);
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, cx + 3, cy + 4, 2, 2);
    }

    public static void DrawUploadArrow(Graphics g, Rectangle r, Color color, float penWidth = 2.5f)
    {
        using var pen = new Pen(color, penWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        int cx = r.X + r.Width / 2;
        int cy = r.Y + r.Height / 2;

        g.DrawLine(pen, cx, cy + 6, cx, cy - 5);
        g.DrawLine(pen, cx - 5, cy, cx, cy - 5);
        g.DrawLine(pen, cx + 5, cy, cx, cy - 5);
    }

    public static void DrawPlus(Graphics g, Rectangle r, Color color, float penWidth = 2f)
    {
        using var pen = new Pen(color, penWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        int cx = r.X + r.Width / 2;
        int cy = r.Y + r.Height / 2;
        int sz = 4;
        g.DrawLine(pen, cx - sz, cy, cx + sz, cy);
        g.DrawLine(pen, cx, cy - sz, cx, cy + sz);
    }

    public static void DrawClose(Graphics g, Rectangle r, Color color, float penWidth = 1.8f)
    {
        using var pen = new Pen(color, penWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        int cx = r.X + r.Width / 2;
        int cy = r.Y + r.Height / 2;
        int sz = 4;
        g.DrawLine(pen, cx - sz, cy - sz, cx + sz, cy + sz);
        g.DrawLine(pen, cx + sz, cy - sz, cx - sz, cy + sz);
    }

    public static void DrawCheck(Graphics g, Rectangle r, Color color, float penWidth = 2f)
    {
        using var pen = new Pen(color, penWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        int cx = r.X + r.Width / 2;
        int cy = r.Y + r.Height / 2;
        g.DrawLine(pen, cx - 5, cy, cx - 1, cy + 4);
        g.DrawLine(pen, cx - 1, cy + 4, cx + 6, cy - 4);
    }

    public static void DrawAlert(Graphics g, Rectangle r, Color color, float penWidth = 2f)
    {
        using var pen = new Pen(color, penWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        int cx = r.X + r.Width / 2;
        int cy = r.Y + r.Height / 2;

        var pts = new Point[]
        {
            new(cx, cy - 7),
            new(cx - 7, cy + 6),
            new(cx + 7, cy + 6)
        };
        g.DrawPolygon(pen, pts);
        g.DrawLine(pen, cx, cy - 2, cx, cy + 2);
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, cx - 1, cy + 4, 2, 2);
    }

    public static void DrawChevronRight(Graphics g, Rectangle r, Color color, float penWidth = 2f)
    {
        using var pen = new Pen(color, penWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        int cx = r.X + r.Width / 2;
        int cy = r.Y + r.Height / 2;
        g.DrawLine(pen, cx - 2, cy - 4, cx + 2, cy);
        g.DrawLine(pen, cx + 2, cy, cx - 2, cy + 4);
    }

    public static void DrawChevronDown(Graphics g, Rectangle r, Color color, float penWidth = 2f)
    {
        using var pen = new Pen(color, penWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        int cx = r.X + r.Width / 2;
        int cy = r.Y + r.Height / 2;
        g.DrawLine(pen, cx - 4, cy - 2, cx, cy + 2);
        g.DrawLine(pen, cx, cy + 2, cx + 4, cy - 2);
    }

    public static void DrawShield(Graphics g, Rectangle r, Color color, float penWidth = 2f)
    {
        using var pen = new Pen(color, penWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        int cx = r.X + r.Width / 2;
        int cy = r.Y + r.Height / 2;

        var pts = new Point[]
        {
            new(cx, cy - 7),
            new(cx + 6, cy - 4),
            new(cx + 6, cy + 2),
            new(cx, cy + 7),
            new(cx - 6, cy + 2),
            new(cx - 6, cy - 4)
        };
        g.DrawPolygon(pen, pts);
    }

    public static void DrawLock(Graphics g, Rectangle r, Color color, float penWidth = 2f)
    {
        using var pen = new Pen(color, penWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        int cx = r.X + r.Width / 2;
        int cy = r.Y + r.Height / 2;

        g.DrawRectangle(pen, cx - 6, cy - 1, 12, 9);
        g.DrawArc(pen, cx - 4, cy - 7, 8, 8, 180, 180);
    }

    public static void DrawMoon(Graphics g, Rectangle r, Color color, float penWidth = 2f)
    {
        using var pen = new Pen(color, penWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        int cx = r.X + r.Width / 2;
        int cy = r.Y + r.Height / 2;
        g.DrawArc(pen, cx - 6, cy - 6, 12, 12, 45, 270);
    }
}
