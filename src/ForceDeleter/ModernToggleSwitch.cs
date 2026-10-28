using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ForceDeleter;

/// <summary>
/// Modern pill toggle switch matching Screen 4 (Settings) with smooth slide animation.
/// </summary>
public class ModernToggleSwitch : Control
{
    private bool _checked = false;
    private float _knobPos = 0f;
    private readonly System.Windows.Forms.Timer _animTimer;

    public event EventHandler? CheckedChanged;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked != value)
            {
                _checked = value;
                _animTimer.Start();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public ModernToggleSwitch()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        this.DoubleBuffered = true;
        this.Size = new Size(46, 24);
        this.Cursor = Cursors.Hand;
        this.BackColor = Color.Transparent;
        this.TabStop = true;
        this.AccessibleRole = AccessibleRole.CheckButton;

        _animTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _animTimer.Tick += (s, e) =>
        {
            float target = _checked ? 1f : 0f;
            _knobPos += (target - _knobPos) * 0.3f;
            if (Math.Abs(target - _knobPos) < 0.05f)
            {
                _knobPos = target;
                _animTimer.Stop();
            }
            this.Invalidate();
        };
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Checked = !Checked;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        UITheme.EnableAntiAlias(g);

        var rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
        int radius = rect.Height / 2;

        // Background pill
        Color trackColor = _checked
            ? UITheme.AccentRed
            : UITheme.Dark ? Color.FromArgb(46, 52, 66) : Color.FromArgb(200, 206, 218);

        using (var path = UITheme.GetRoundedPath(rect, radius))
        using (var brush = new SolidBrush(trackColor))
        {
            g.FillPath(brush, path);
        }

        // Knob (circle)
        int knobSz = rect.Height - 6;
        float startX = 3f;
        float endX = rect.Width - knobSz - 3f;
        float currentX = startX + (endX - startX) * _knobPos;
        float currentY = 3f;

        using (var knobBrush = new SolidBrush(Color.White))
        {
            g.FillEllipse(knobBrush, currentX, currentY, knobSz, knobSz);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animTimer?.Stop();
            _animTimer?.Dispose();
        }
        base.Dispose(disposing);
    }
}
