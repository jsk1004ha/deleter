using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ForceDeleter;

public class CircularProgressControl : Control
{
    private float _displayed, _target, _phase;
    private bool _animating;
    private string _status = "Deleting files...";
    private readonly System.Windows.Forms.Timer _timer;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Animating
    {
        get => _animating;
        set
        {
            _animating = value;
            if (!value) _displayed = _target;
            UpdateTimer();
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string StatusText { get => _status; set { _status = value; Invalidate(); } }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Progress
    {
        get => _target;
        set { _target = Math.Clamp(value, 0, 100); if (!_animating) _displayed = _target; Invalidate(); }
    }

    public CircularProgressControl()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
        _timer = new System.Windows.Forms.Timer { Interval = 25 };
        _timer.Tick += (_, _) =>
        {
            _displayed += (_target - _displayed) * .18f;
            if (Math.Abs(_target - _displayed) < .15f) _displayed = _target;
            _phase += .025f;
            Invalidate();
        };
    }

    private void UpdateTimer() { if (_animating && Visible) _timer.Start(); else _timer.Stop(); }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); UpdateTimer(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        UITheme.EnableAntiAlias(g);
        float scale = Math.Min(Width, Height) / 240f;
        var state = g.Save();
        g.TranslateTransform(Width / 2f, Height / 2f);
        g.ScaleTransform(scale, scale);
        var ring = new Rectangle(-96, -96, 192, 192);
        using var track = new Pen(UITheme.Dark ? Color.FromArgb(46, 47, 56) : Color.FromArgb(221, 217, 226), 10);
        g.DrawEllipse(track, ring);
        float sweep = _displayed * 3.6f;
        if (sweep > .1f)
        {
            using var arc = new Pen(UITheme.AccentRed, 10) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(arc, ring, -90, sweep);
        }
        if (_animating)
        {
            float sweepAngle = (_phase * 70f) % 360f;
            using var reticlePen = new Pen(Color.FromArgb(UITheme.Dark ? 40 : 30, UITheme.BorderSubtle), 1.5f);
            using var activeTickPen = new Pen(UITheme.AccentRed, 2f);
            for (int i = 0; i < 24; i++)
            {
                float tickAngle = i * 15f;
                float rad = tickAngle * (float)Math.PI / 180f;
                float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);
                float diff = Math.Abs((tickAngle - sweepAngle + 540f) % 360f - 180f);
                bool isActive = diff < 40f;
                float r1 = isActive ? 109f : 111f;
                float r2 = 115f;
                var p = isActive ? activeTickPen : reticlePen;
                g.DrawLine(p, cos * r1, sin * r1, cos * r2, sin * r2);
            }
            float headRad = sweepAngle * (float)Math.PI / 180f;
            using var headBrush = new SolidBrush(UITheme.AccentRed);
            g.FillEllipse(headBrush, (float)Math.Cos(headRad) * 114f - 2f, (float)Math.Sin(headRad) * 114f - 2f, 4f, 4f);
        }
        SvgIcons.Draw(g, new Rectangle(-20, -54, 40, 40), "trash", UITheme.AccentRed, 2.1f);
        UITheme.Text(g, $"{(int)Math.Round(_displayed)}%", new Rectangle(-75, -6, 150, 40), 29, UITheme.TextPrimary, true, ContentAlignment.MiddleCenter);
        UITheme.Text(g, _status, new Rectangle(-88, 32, 176, 25), 14, UITheme.TextSecondary, align: ContentAlignment.MiddleCenter);
        g.Restore(state);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
