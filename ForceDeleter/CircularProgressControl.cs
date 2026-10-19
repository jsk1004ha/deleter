using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ForceDeleter;

/// <summary>
/// Modern circular animated progress ring with glowing neon particles and center status.
/// Matches Screen 5 ("Deleting" animated state) in the user design.
/// </summary>
public class CircularProgressControl : Control
{
    private float _currentProgress = 0f;
    private float _targetProgress = 0f;
    private readonly System.Windows.Forms.Timer _animTimer;
    private float _glowPulse = 0f;
    private bool _pulseDirection = true;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string StatusText { get; set; } = "Deleting files...";

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float Progress
    {
        get => _targetProgress;
        set
        {
            _targetProgress = Math.Clamp(value, 0f, 100f);
            if (!_animTimer.Enabled)
                _animTimer.Start();
        }
    }

    public CircularProgressControl()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        this.DoubleBuffered = true;
        this.Size = new Size(180, 180);
        this.BackColor = Color.Transparent;

        _animTimer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60fps
        _animTimer.Tick += (s, e) =>
        {
            // Smooth lerp progress
            _currentProgress += (_targetProgress - _currentProgress) * 0.15f;
            if (Math.Abs(_targetProgress - _currentProgress) < 0.2f)
                _currentProgress = _targetProgress;

            // Pulse glowing aura
            if (_pulseDirection)
            {
                _glowPulse += 0.04f;
                if (_glowPulse >= 1f) _pulseDirection = false;
            }
            else
            {
                _glowPulse -= 0.04f;
                if (_glowPulse <= 0f) _pulseDirection = true;
            }

            this.Invalidate();
            if (_currentProgress >= _targetProgress && _targetProgress >= 100f)
            {
                // Finished
            }
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        UITheme.EnableAntiAlias(g);

        int sz = Math.Min(this.Width, this.Height) - 30;
        int cx = this.Width / 2;
        int cy = this.Height / 2;
        var ringRect = new Rectangle(cx - sz / 2, cy - sz / 2, sz, sz);

        // 1. Draw glowing background particle dots
        DrawGlowingParticles(g, cx, cy, sz / 2 + 12);

        // 2. Background Track
        using (var trackPen = new Pen(Color.FromArgb(50, 40, 25, 32), 9f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawEllipse(trackPen, ringRect);
        }

        // 3. Progress Arc (Neon Red/Coral)
        float sweepAngle = (_currentProgress / 100f) * 360f;
        if (sweepAngle > 0.1f)
        {
            // Outer glow
            int glowAlpha = (int)(40 + 30 * _glowPulse);
            using (var glowPen = new Pen(Color.FromArgb(glowAlpha, UITheme.AccentRed), 15f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawArc(glowPen, ringRect, -90, sweepAngle);
            }

            // Core sharp arc
            using (var arcPen = new Pen(UITheme.AccentRed, 9f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawArc(arcPen, ringRect, -90, sweepAngle);
            }
        }

        // 4. Center Content (Trash Icon + Percentage + Subtext)
        var iconRect = new Rectangle(cx - 10, cy - 42, 20, 20);
        SvgIcons.DrawTrash(g, iconRect, UITheme.AccentRed, 2f);

        // Percentage Text
        using (var fontPercent = new Font("Segoe UI", 16F, FontStyle.Bold))
        using (var textBrush = new SolidBrush(Color.White))
        {
            string pctStr = $"{(int)_currentProgress}%";
            var szPct = g.MeasureString(pctStr, fontPercent);
            g.DrawString(pctStr, fontPercent, textBrush, cx - szPct.Width / 2, cy - 14);
        }

        // Subtext "Deleting files..."
        using (var fontSub = new Font("Segoe UI", 8F, FontStyle.Regular))
        using (var subBrush = new SolidBrush(UITheme.TextSecondary))
        {
            var szSub = g.MeasureString(StatusText, fontSub);
            g.DrawString(StatusText, fontSub, subBrush, cx - szSub.Width / 2, cy + 18);
        }
    }

    private void DrawGlowingParticles(Graphics g, int cx, int cy, int radius)
    {
        // 8 decorative dots rotating around the ring
        double[] angles = [0.3, 1.2, 2.5, 3.8, 4.6, 5.7];
        foreach (var a in angles)
        {
            double angle = a + (_glowPulse * 0.2);
            int px = cx + (int)(radius * Math.Cos(angle));
            int py = cy + (int)(radius * Math.Sin(angle));
            int dotSize = (int)(3 + 2 * Math.Sin(a * 3 + _glowPulse * 2));
            using var brush = new SolidBrush(Color.FromArgb(120, UITheme.AccentRed));
            g.FillEllipse(brush, px - dotSize / 2, py - dotSize / 2, dotSize, dotSize);
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
