using System.ComponentModel;

namespace ForceDeleter;

public class RetentionPicker : IconButton
{
    private readonly ContextMenuStrip _menu = new();
    private static readonly string[] Choices = ["7 days", "30 days", "90 days", "Forever"];
    private int _selectedIndex = 1;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set { _selectedIndex = Math.Clamp(value, 0, Choices.Length - 1); Invalidate(); }
    }

    public RetentionPicker()
    {
        AccessibleName = "History retention";
        for (int i = 0; i < Choices.Length; i++)
        {
            int index = i;
            _menu.Items.Add(Choices[i], null, (_, _) => SelectedIndex = index);
        }
        Overlay = g =>
        {
            float dpi = DeviceDpi / 96f;
            UITheme.Text(g, Choices[_selectedIndex], new Rectangle((int)(18 * dpi), 0, Width - (int)(50 * dpi), Height), 16 * dpi, UITheme.TextPrimary);
            SvgIcons.Draw(g, new Rectangle(Width - (int)(33 * dpi), (Height - (int)(18 * dpi)) / 2, (int)(18 * dpi), (int)(18 * dpi)), "chevron-down", UITheme.TextSecondary);
        };
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        _menu.BackColor = UITheme.BgItemCard;
        _menu.ForeColor = UITheme.TextPrimary;
        _menu.Show(this, new Point(0, Height));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _menu.Dispose();
        base.Dispose(disposing);
    }
}
