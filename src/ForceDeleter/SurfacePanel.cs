using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ForceDeleter;

public class ThemedFlowPanel : FlowLayoutPanel
{
    private bool _hidingBars;
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string theme, string? subIdList);
    [DllImport("user32.dll")]
    private static extern bool ShowScrollBar(IntPtr window, int bar, bool show);

    public ThemedFlowPanel() { DoubleBuffered = true; TabStop = true; }
    public void ApplyScrollTheme()
    {
        if (IsHandleCreated) SetWindowTheme(Handle, UITheme.Dark ? "DarkMode_Explorer" : "Explorer", null);
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyScrollTheme(); }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (_hidingBars || m.Msg is not (0x5 or 0xF or 0x47 or 0x85)) return;
        HideBars();
    }

    private void HideBars()
    {
        if (_hidingBars || !IsHandleCreated) return;
        _hidingBars = true;
        try { ShowScrollBar(Handle, 3, false); }
        finally { _hidingBars = false; }
    }

    protected override void OnLayout(LayoutEventArgs levent) { base.OnLayout(levent); HideBars(); }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        int maximum = Math.Max(0, DisplayRectangle.Height - ClientSize.Height);
        AutoScrollPosition = new Point(0, Math.Clamp(-AutoScrollPosition.Y - e.Delta / 3, 0, maximum));
        HideBars();
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Down or Keys.Up or Keys.PageDown or Keys.PageUp || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        int amount = e.KeyCode switch { Keys.Down => 40, Keys.Up => -40, Keys.PageDown => Height / 2, Keys.PageUp => -Height / 2, _ => 0 };
        if (amount != 0)
        {
            int maximum = Math.Max(0, DisplayRectangle.Height - ClientSize.Height);
            AutoScrollPosition = new Point(0, Math.Clamp(-AutoScrollPosition.Y + amount, 0, maximum));
            HideBars();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}

public class SurfacePanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Border { get; set; } = true;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsRow { get; set; }

    public SurfacePanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        UITheme.EnableAntiAlias(e.Graphics);
        if (Width > 2 && Height > 2)
        {
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = (int)((IsRow ? 6 : 10) * DeviceDpi / 96f);
            using var path = UITheme.GetRoundedPath(r, radius);
            Color top = IsRow ? UITheme.BgItemCard : UITheme.BgCardContainer;
            using var brush = new SolidBrush(top);
            e.Graphics.FillPath(brush, path);
            if (Border)
            {
                using var pen = new Pen(IsRow ? Color.FromArgb(UITheme.Dark ? 36 : 48, UITheme.BorderSubtle) : UITheme.BorderSubtle);
                e.Graphics.DrawPath(pen, path);
            }
        }
        base.OnPaint(e);
    }
}

public class IconButton : Button
{
    private bool _hover;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string IconName { get; set; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Primary { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Quiet { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Circular { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selector { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float TextSize { get; set; } = 16;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action<Graphics>? Overlay { get; set; }

    public IconButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2) return;
        var g = e.Graphics;
        // ButtonBase does not reliably erase a transparent owner-drawn button on screen.
        // Repaint the parent's surface explicitly before drawing this frame.
        if (Parent != null)
        {
            var backgroundState = g.Save();
            try
            {
                g.TranslateTransform(-Left, -Top);
                using var background = new PaintEventArgs(g, new Rectangle(Left, Top, Width, Height));
                InvokePaintBackground(Parent, background);
                InvokePaint(Parent, background);
            }
            finally { g.Restore(backgroundState); }
        }
        UITheme.EnableAntiAlias(g);
        float scale = DeviceDpi / 96f;
        int S(float v) => (int)Math.Round(v * scale);
        var r = new Rectangle(1, 1, Width - 3, Height - 3);
        using var path = UITheme.GetRoundedPath(r, Circular ? Height / 2 : S(10));
        if (!Quiet || Selected || _hover)
        {
            Color top = Primary
                ? UITheme.AccentRed
                : Selected
                    ? (UITheme.Dark ? Color.FromArgb(42, 48, 62) : Color.FromArgb(224, 230, 240))
                    : UITheme.BgItemCard;
            if (_hover) top = ControlPaint.Light(top, .08f);
            using var brush = new SolidBrush(top);
            g.FillPath(brush, path);
            using var pen = new Pen(Primary
                ? UITheme.AccentRed
                : Selected
                    ? (UITheme.Dark ? Color.FromArgb(70, 82, 105) : Color.FromArgb(185, 195, 212))
                    : UITheme.BorderSubtle);
            g.DrawPath(pen, path);
        }
        var foreground = !Enabled
            ? UITheme.TextMuted
            : Primary
                ? Color.White
                : Selected
                    ? (UITheme.Dark ? Color.FromArgb(250, 252, 255) : Color.FromArgb(16, 22, 34))
                    : UITheme.TextSecondary;
        int iconSize = S(Selector ? 32 : Parent?.Name == "Caption" ? 15 : Quiet && !Circular ? 24 : 21);
        if (Selector)
        {
            SvgIcons.Draw(g, new Rectangle(S(34), (Height - iconSize) / 2, iconSize, iconSize), IconName,
                IconName == "folder-filled" ? UITheme.AccentBlue : UITheme.TextPrimary);
            SvgIcons.Draw(g, new Rectangle(Width - S(40), (Height - S(20)) / 2, S(20), S(20)), "chevron-right", UITheme.TextMuted);
            SvgIcons.Draw(g, new Rectangle(S(59), Height / 2 + S(7), S(21), S(21)), "circle-plus-filled", UITheme.AccentBlue);
            UITheme.Text(g, Text, new Rectangle(S(90), 0, Math.Max(0, Width - S(140)), Height), S(16), UITheme.TextSecondary);
        }
        else if (Text.Length == 0 && IconName.Length > 0)
            SvgIcons.Draw(g, new Rectangle((Width - iconSize) / 2, (Height - iconSize) / 2, iconSize, iconSize), IconName, foreground);
        else if (Text.Length > 0)
        {
            using var font = UITheme.Font(S(TextSize));
            int tw = (int)Math.Ceiling(g.MeasureString(Text, font).Width);
            int all = tw + (IconName.Length == 0 ? 0 : iconSize + S(12));
            int x = (Width - all) / 2;
            if (IconName.Length != 0)
                SvgIcons.Draw(g, new Rectangle(x, (Height - iconSize) / 2, iconSize, iconSize), IconName, foreground);
            UITheme.Text(g, Text, new Rectangle(x + (IconName.Length == 0 ? 0 : iconSize + S(12)), 0, tw + S(4), Height), S(TextSize), foreground);
        }
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(r, -S(5), -S(5)), foreground, BackColor);
        Overlay?.Invoke(g);
    }
}
