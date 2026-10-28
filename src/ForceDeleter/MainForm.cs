using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;

namespace ForceDeleter;

public enum AppTab { Delete, Add, History, Settings, DeletingAnimation }

public partial class MainForm : Form
{
    private readonly AppState _state;
    private readonly List<TargetItem> _items = [];
    private readonly Dictionary<AppTab, Panel> _views = [];
    private readonly Dictionary<AppTab, IconButton> _navigation = [];
    private readonly ToolTip _tips;
    private readonly List<HistoryRecord> _results = [];
    private AppTab _currentTab;
    private bool _busy;
    private Task _deletionTask = Task.CompletedTask;
    private bool _initialSizeApplied;
    private string _filter = "All";
    private int _totalItems;
    private long _totalBytes;
    private bool _hasFolderSizeUnknown;
    private readonly Label _title = new();
    private readonly Panel _caption = new();
    private readonly SurfacePanel _deleteCard = new();
    private readonly FlowLayoutPanel _itemList = NewList();
    private readonly IconButton _delete = new();
    private readonly IconButton _clear = new();
    private readonly IconButton _empty = new();
    private readonly SurfacePanel _addCard = new();
    private readonly SurfacePanel _dropZone = new();
    private readonly IconButton _addFile = new();
    private readonly IconButton _addFolder = new();
    private readonly SurfacePanel _previewCard = new();
    private readonly FlowLayoutPanel _previews = NewList(false);
    private readonly SurfacePanel _searchBox = new();
    private readonly TextBox _search = new();
    private readonly FlowLayoutPanel _filters = NewList(false);
    private readonly SurfacePanel _historyCard = new();
    private readonly FlowLayoutPanel _historyList = NewList();
    private readonly IconButton _export = new();
    private readonly SurfacePanel _settingsCard = new();
    private readonly FlowLayoutPanel _settingList = NewList();
    private readonly ModernToggleSwitch _secure = new();
    private readonly ModernToggleSwitch _turbo = new();
    private readonly ModernToggleSwitch _confirm = new();
    private readonly ModernToggleSwitch _unlock = new();
    private readonly ModernToggleSwitch _dark = new();
    private readonly ModernToggleSwitch _context = new();
    private readonly RetentionPicker _retention = new();
    private readonly IconButton _apply = new();
    private readonly IconButton _moreSettings = new();
    private readonly SurfacePanel _progressCard = new();
    private readonly CircularProgressControl _ring = new();
    private readonly SurfacePanel _resultCard = new();
    private readonly IconButton _success = new();
    private readonly IconButton _warning = new();
    private readonly IconButton _done = new();

    public MainForm(string? initialPath = null) : this(initialPath == null ? null : new[] { initialPath }) { }
    public MainForm(IEnumerable<string>? initialPaths) : this(initialPaths, null) { }

    internal MainForm(IEnumerable<string>? initialPaths, string? dataDirectory)
    {
        InitializeComponent();
        _state = new AppState(dataDirectory);
        UITheme.Dark = _state.Settings.DarkMode;
        _tips = new ToolTip(components!);
        FormBorderStyle = FormBorderStyle.None;
        ClientSize = new Size(930, 630);
        MinimumSize = new Size(820, 600);
        DoubleBuffered = true;
        BackColor = UITheme.BgApp;
        BuildShell();
        BuildDelete();
        BuildAdd();
        BuildHistory();
        BuildSettings();
        BuildProgress();
        EnableDrop(this);
        RefreshItems();
        if (initialPaths != null) AddTargetItems(initialPaths);
        SwitchTab(AppTab.Delete);
        ApplyTheme(this);
        FormClosing += (_, e) => { if (_busy) e.Cancel = true; };
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.O && !_busy) { PromptFiles(); e.Handled = true; }
        };
    }

    private int S(float n) => (int)Math.Round(n * DeviceDpi / 96f);
    private static FlowLayoutPanel NewList(bool vertical = true) => new ThemedFlowPanel
    {
        AutoScroll = true, WrapContents = false,
        FlowDirection = vertical ? FlowDirection.TopDown : FlowDirection.LeftToRight,
        Margin = Padding.Empty
    };

    private void BuildShell()
    {
        _caption.Name = "Caption";
        _caption.BackColor = Color.Transparent;
        _caption.MouseDown += DragWindow;
        _caption.DoubleClick += (_, _) => ToggleMaximize();
        Controls.Add(_caption);
        AddButton(_caption, "Minimize", "minus", "", () => WindowState = FormWindowState.Minimized).Quiet = true;
        AddButton(_caption, "Maximize", "square", "", ToggleMaximize).Quiet = true;
        AddButton(_caption, "Close", "x", "", Close).Quiet = true;
        _title.BackColor = Color.Transparent;
        _title.MouseDown += DragWindow;
        Controls.Add(_title);
        foreach (var pair in new[] { (AppTab.Delete, "trash"), (AppTab.Add, "folder"), (AppTab.History, "clock"), (AppTab.Settings, "settings") })
        {
            var tab = pair.Item1;
            var button = AddButton(this, "Nav" + tab, pair.Item2, "", () => SwitchTab(tab));
            button.Quiet = true;
            button.AccessibleName = tab.ToString();
            _tips.SetToolTip(button, tab.ToString());
            _navigation.Add(tab, button);
        }
        foreach (var tab in Enum.GetValues<AppTab>())
        {
            var view = new Panel { Name = "View" + tab, BackColor = Color.Transparent, Visible = false };
            _views.Add(tab, view);
            Controls.Add(view);
        }
    }

    private static IconButton AddButton(Control parent, string name, string icon, string text, Action click, IconButton? button = null)
    {
        button ??= new IconButton();
        button.Name = name;
        button.IconName = icon;
        button.Text = text;
        button.AccessibleName = text.Length == 0 ? name : text;
        button.Click += (_, _) => click();
        parent.Controls.Add(button);
        return button;
    }

    private void BuildDelete()
    {
        var view = _views[AppTab.Delete];
        view.Controls.Add(_deleteCard);
        _deleteCard.Controls.Add(_itemList);
        AddButton(_deleteCard, "EmptyAdd", "folder-up", "Add files or drop them here", PromptFiles, _empty).Quiet = true;
        EnableDrop(_deleteCard);
        EnableDrop(_itemList);
        AddButton(view, "Delete", "trash", "Delete", () => _deletionTask = DeleteAsync(), _delete).Primary = true;
        AddButton(view, "Clear", "x", "Clear", ClearItems, _clear);
    }

    private void BuildAdd()
    {
        var view = _views[AppTab.Add];
        view.Controls.Add(_addCard);
        _addCard.Controls.Add(_dropZone);
        _dropZone.Border = false;
        _dropZone.Cursor = Cursors.Hand;
        _dropZone.AccessibleName = "Drop files or folders here";
        EnableDrop(_dropZone);
        _dropZone.Click += (_, _) => PromptFiles();
        _dropZone.Paint += (_, e) =>
        {
            var g = e.Graphics;
            var r = new Rectangle(S(1), S(1), _dropZone.Width - S(3), _dropZone.Height - S(3));
            using var path = UITheme.GetRoundedPath(r, S(10));
            using var pen = new Pen(Color.FromArgb(UITheme.Dark ? 60 : 70, UITheme.BorderSubtle), S(1)) { DashPattern = [6, 4] };
            g.DrawPath(pen, path);
            int cx = r.Width / 2, cy = r.Height / 2 - S(10);
            SvgIcons.Draw(g, new Rectangle(cx - S(44), cy - S(42), S(88), S(88)), "folder-filled", UITheme.AccentBlue);
            SvgIcons.Draw(g, new Rectangle(cx - S(20), cy - S(10), S(40), S(40)), "upload", UITheme.TextPrimary, 2);
            UITheme.Text(g, "Drop files or folders here", new Rectangle(0, cy + S(60), r.Width, S(25)), S(14), UITheme.TextMuted, align: ContentAlignment.MiddleCenter);
        };
        AddButton(_addCard, "AddFiles", "file-filled", "Add files", PromptFiles, _addFile).Selector = true;
        AddButton(_addCard, "AddFolder", "folder-filled", "Add folder", PromptFolder, _addFolder).Selector = true;
        view.Controls.Add(_previewCard);
        _previewCard.Controls.Add(_previews);
        _previews.AutoScroll = false;
    }

    private void BuildHistory()
    {
        var view = _views[AppTab.History];
        Controls.Add(_searchBox);
        _searchBox.Paint += (_, e) => SvgIcons.Draw(e.Graphics, new Rectangle(S(15), S(12), S(21), S(21)), "search", UITheme.TextMuted);
        _search.Name = "HistorySearch";
        _search.BorderStyle = BorderStyle.None;
        _search.PlaceholderText = "Search history...";
        _search.TextChanged += (_, _) => RefreshHistory();
        _searchBox.Controls.Add(_search);
        view.Controls.Add(_filters);
        _filters.AutoScroll = false;
        foreach (string filter in new[] { "All", "Files", "Folders", "Successful", "With Warnings" })
        {
            var chip = AddButton(_filters, "Filter" + filter.Replace(" ", ""), "", filter, () =>
            {
                _filter = filter;
                foreach (IconButton b in _filters.Controls) { b.Selected = b.Text == filter; b.Invalidate(); }
                RefreshHistory();
            });
            chip.Selected = filter == "All";
            chip.TextSize = 14;
        }
        view.Controls.Add(_historyCard);
        _historyCard.Controls.Add(_historyList);
        AddButton(view, "Export", "download", "Export", ExportHistory, _export);
    }

    private void BuildSettings()
    {
        var view = _views[AppTab.Settings];
        view.Controls.Add(_settingsCard);
        _settingsCard.Controls.Add(_settingList);
        _settingList.AutoScroll = false;
        _secure.Checked = _state.Settings.SecureDelete;
        _turbo.Checked = _state.Settings.TurboMode;
        _confirm.Checked = _state.Settings.Confirm;
        _unlock.Checked = _state.Settings.RemoveLocked;
        _dark.Checked = _state.Settings.DarkMode;
        _context.Checked = ContextMenuHelper.IsContextMenuInstalled();
        Setting("Secure delete", "Overwrite files to prevent recovery", "shield", UITheme.DotGreen, _secure);
        Setting("Show confirmation prompt", "Ask before permanently deleting files", "alert-triangle", UITheme.DotOrange, _confirm);
        Setting("Remove locked files", "Try to unlock and delete in-use files", "lock", UITheme.AccentBlue, _unlock);
        Setting("Dark mode", "Use dark appearance across the app", "moon", UITheme.AccentBlue, _dark);
        _retention.Name = "HistoryRetention";
        _retention.SelectedIndex = _state.Settings.RetentionDays switch { 7 => 0, 90 => 2, 0 => 3, _ => 1 };
        Setting("History retention", "Keep a log of deleted files for", "clock", UITheme.DotGreen, _retention);
        AddButton(view, "MoreSettings", "settings", "More options", ShowAdvancedSettings, _moreSettings).Quiet = true;
        _moreSettings.TextSize = 14;
        AddButton(view, "Apply", "check", "Apply", ApplySettings, _apply);
    }

    private void Setting(string title, string subtitle, string icon, Color color, Control input)
    {
        var row = new SurfacePanel { IsRow = true, Tag = input, Margin = new Padding(0, 0, 0, S(8)) };
        input.AccessibleName = title;
        input.Name = input.Name.Length == 0 ? title.Replace(" ", "") : input.Name;
        row.Controls.Add(input);
        row.Paint += (_, e) =>
        {
            UITheme.Badge(e.Graphics, new Rectangle(S(15), S(12), S(50), S(50)), icon, color);
            int available = input.Left - S(99);
            UITheme.Text(e.Graphics, title, new Rectangle(S(87), S(14), available, S(25)), S(16), UITheme.TextPrimary);
            UITheme.Text(e.Graphics, subtitle, new Rectangle(S(87), S(40), available, S(22)), S(13), UITheme.TextSecondary);
        };
        _settingList.Controls.Add(row);
    }

    private void BuildProgress()
    {
        var view = _views[AppTab.DeletingAnimation];
        view.Controls.Add(_progressCard);
        _progressCard.Controls.Add(_ring);
        _progressCard.Paint += (_, e) =>
        {
            int y = Math.Max(S(20), (_progressCard.Height - S(140)) / 2);
            foreach (bool right in new[] { false, true })
            {
                int cx = right ? _progressCard.Width * 5 / 6 : _progressCard.Width / 6;
                UITheme.Badge(e.Graphics, new Rectangle(cx - S(35), y, S(70), S(70)), right ? "device-floppy" : "file-filled", UITheme.TextPrimary);
                UITheme.Text(e.Graphics, right ? FormatSize(_totalBytes) : $"{_totalItems} items", new Rectangle(cx - S(100), y + S(78), S(200), S(32)), S(22), UITheme.TextPrimary, true, ContentAlignment.MiddleCenter);
                UITheme.Text(e.Graphics, right ? (_hasFolderSizeUnknown ? "known file size" : "total size") : "in total", new Rectangle(cx - S(100), y + S(114), S(200), S(24)), S(14), UITheme.TextMuted, align: ContentAlignment.MiddleCenter);
            }
        };
        view.Controls.Add(_resultCard);
        AddButton(_resultCard, "SuccessDetails", "check", "0 items deleted successfully", () => ShowResults(true), _success);
        AddButton(_resultCard, "WarningDetails", "alert-triangle", "0 items could not be deleted", () => ShowResults(false), _warning);
        _success.Overlay = g => PaintResult(g, _success, true);
        _warning.Overlay = g => PaintResult(g, _warning, false);
        AddButton(view, "Done", "check", "Done", () => SwitchTab(AppTab.Delete), _done);
    }

    private void PaintResult(Graphics g, IconButton button, bool success)
    {
        // Cover the button's centered caption with the reference's left-aligned result layout.
        var r = new Rectangle(S(2), S(2), button.Width - S(5), button.Height - S(5));
        using var path = UITheme.GetRoundedPath(r, S(8));
        using var brush = new SolidBrush(success ? UITheme.BgItemCard : UITheme.Dark ? Color.FromArgb(38, 35, 34) : Color.FromArgb(255, 246, 224));
        g.FillPath(brush, path);
        using var borderPen = new Pen(Color.FromArgb(UITheme.Dark ? 40 : 50, UITheme.BorderSubtle));
        g.DrawPath(borderPen, path);
        Color color = success ? UITheme.DotGreen : UITheme.DotOrange;
        var badgeR = new Rectangle(S(15), (button.Height - S(36)) / 2, S(36), S(36));
        using var badgePath = UITheme.GetRoundedPath(badgeR, S(6));
        using var badgeBg = new SolidBrush(Color.FromArgb(UITheme.Dark ? 28 : 20, color));
        using var badgePen = new Pen(Color.FromArgb(UITheme.Dark ? 75 : 65, color));
        g.FillPath(badgeBg, badgePath);
        g.DrawPath(badgePen, badgePath);
        SvgIcons.Draw(g, new Rectangle(badgeR.X + S(7), badgeR.Y + S(7), S(22), S(22)), success ? "check" : "alert-triangle", color, 2.2f);
        UITheme.Text(g, button.Text, new Rectangle(S(65), 0, button.Width - S(240), button.Height), S(16), UITheme.TextPrimary);
        long bytes = _results.Where(r => r.IsSuccessful == success).Sum(r => r.TotalSizeBytes);
        UITheme.Text(g, FormatSize(bytes), new Rectangle(button.Width - S(130), 0, S(80), button.Height), S(14), UITheme.TextMuted);
        SvgIcons.Draw(g, new Rectangle(button.Width - S(36), S(19), S(18), S(18)), "chevron-down", UITheme.TextMuted);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_views.Count == 0) return;
        int w = ClientSize.Width, h = ClientSize.Height;
        _caption.SetBounds(S(101), 0, w - S(102), S(46));
        for (int i = 0; i < _caption.Controls.Count; i++)
            _caption.Controls[i].SetBounds(_caption.Width - S(153 - i * 52), S(3), S(50), S(40));
        int titleWidth = _currentTab == AppTab.History ? w - S(565) : w - S(180);
        _title.SetBounds(S(134), S(58), Math.Max(S(180), titleWidth), S(48));
        // WinForms may still use the assigned font during nested layout/paint callbacks.
        if (_title.Font.Size != S(29) || _title.Font.Unit != GraphicsUnit.Pixel)
            _title.Font = UITheme.Font(S(29), true);
        int navIndex = 0;
        foreach (var nav in _navigation.Values) nav.SetBounds(S(18), S(51 + 69 * navIndex++), S(64), S(64));
        foreach (var view in _views.Values) view.SetBounds(S(129), S(114), w - S(156), h - S(146));
        int cw = _views[AppTab.Delete].Width, ch = _views[AppTab.Delete].Height;
        _deleteCard.SetBounds(0, S(6), cw, ch - S(106));
        _itemList.SetBounds(S(26), S(27), cw - S(52), _deleteCard.Height - S(32));
        _empty.SetBounds(S(20), S(20), cw - S(40), _deleteCard.Height - S(40));
        int clearWidth = (int)(cw * .3);
        _delete.SetBounds(0, ch - S(68), cw - clearWidth - S(13), S(64));
        _clear.SetBounds(cw - clearWidth, ch - S(68), clearWidth, S(64));
        _addCard.SetBounds(0, 0, cw, ch - S(116));
        _dropZone.SetBounds(S(19), S(19), cw - S(38), _addCard.Height - S(142));
        int selectorWidth = (cw - S(58)) / 2;
        _addFile.SetBounds(S(19), _addCard.Height - S(96), selectorWidth, S(78));
        _addFolder.SetBounds(S(39) + selectorWidth, _addCard.Height - S(96), selectorWidth, S(78));
        _previewCard.SetBounds(0, ch - S(98), cw, S(98));
        _previews.SetBounds(S(18), S(18), cw - S(36), S(65));
        int previewCount = _previews.Controls.Count;
        if (previewCount > 0)
        {
            int previewWidth = Math.Min(S(232), (_previews.ClientSize.Width - S(16) * (previewCount - 1)) / previewCount);
            for (int i = 0; i < previewCount; i++)
            {
                _previews.Controls[i].Width = previewWidth;
                _previews.Controls[i].Margin = new Padding(0, 0, i == previewCount - 1 ? 0 : S(16), 0);
            }
        }
        _searchBox.SetBounds(w - S(411), S(56), S(384), S(45));
        _search.SetBounds(S(52), S(12), S(315), S(25));
        _filters.SetBounds(S(4), S(4), cw - S(8), S(42));
        foreach (IconButton chip in _filters.Controls)
        {
            chip.SetBounds(0, 0, S(chip.Text switch { "All" => 60, "Files" => 72, "Folders" => 90, "Successful" => 114, _ => 138 }), S(37));
            chip.Margin = new Padding(0, 0, S(9), 0);
        }
        _historyCard.SetBounds(0, S(62), cw, ch - S(118));
        _historyList.SetBounds(S(18), S(14), cw - S(36), _historyCard.Height - S(28));
        _export.SetBounds(cw - S(153), ch - S(48), S(153), S(48));
        _settingsCard.SetBounds(0, S(3), cw, ch - S(58));
        _settingList.SetBounds(S(21), S(13), cw - S(42), _settingsCard.Height - S(26));
        _apply.SetBounds(cw - S(244), ch - S(46), S(244), S(46));
        _moreSettings.SetBounds(S(3), ch - S(44), S(170), S(42));
        foreach (SurfacePanel row in _settingList.Controls)
        {
            row.Size = new Size(_settingList.ClientSize.Width, Math.Min(S(76), (_settingList.Height - S(32)) / 5));
            row.Margin = new Padding(0, 0, 0, row == _settingList.Controls[^1] ? 0 : S(8));
            var input = (Control)row.Tag!;
            int iw = input is RetentionPicker ? S(152) : S(54);
            int ih = input is RetentionPicker ? S(46) : S(29);
            input.SetBounds(row.Width - iw - S(20), (row.Height - ih) / 2, iw, ih);
        }
        _progressCard.SetBounds(0, 0, cw, (int)(ch * .5));
        int ringSize = Math.Min(S(240), _progressCard.Height - S(4));
        _ring.SetBounds((cw - ringSize) / 2, (_progressCard.Height - ringSize) / 2, ringSize, ringSize);
        _resultCard.SetBounds(0, _progressCard.Bottom + S(9), cw, ch - _progressCard.Height - S(99));
        _success.SetBounds(S(17), S(16), cw - S(34), S(57));
        _warning.SetBounds(S(17), S(74), cw - S(34), S(57));
        _done.SetBounds(0, ch - S(65), cw, S(65));
        ResizeRows();
    }

    private void ResizeRows()
    {
        foreach (Control row in _itemList.Controls) row.Width = Math.Max(1, _itemList.ClientSize.Width - (_itemList.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0));
        foreach (Control row in _historyList.Controls) row.Width = Math.Max(1, _historyList.ClientSize.Width - (_historyList.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0));
    }

    protected override void OnShown(EventArgs e)
    {
        if (!_initialSizeApplied)
        {
            _initialSizeApplied = true;
            MinimumSize = new Size(S(820), S(600));
            ClientSize = new Size(S(930), S(630));
            CenterToScreen();
            PerformLayout();
        }
        base.OnShown(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2) return;
        using var bg = new SolidBrush(UITheme.BgApp);
        e.Graphics.FillRectangle(bg, ClientRectangle);
        using var rail = new SolidBrush(Color.FromArgb(UITheme.Dark ? 42 : 20, 2, 4, 10));
        e.Graphics.FillRectangle(rail, 0, 0, S(101), Height);
        using var line = new Pen(Color.FromArgb(UITheme.Dark ? 45 : 35, UITheme.BorderSubtle));
        e.Graphics.DrawLine(line, S(101), 0, S(101), Height);
        UITheme.EnableAntiAlias(e.Graphics);
        using var border = UITheme.GetRoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), S(17));
        using var pen = new Pen(Color.FromArgb(78, 90, 116));
        e.Graphics.DrawPath(pen, border);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width < 2 || Height < 2) return;
        using var path = UITheme.GetRoundedPath(new Rectangle(0, 0, Width, Height), WindowState == FormWindowState.Maximized ? 0 : S(17));
        var previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != 0x84 || WindowState == FormWindowState.Maximized || (int)m.Result != 1) return;
        var point = PointToClient(new Point(unchecked((short)((long)m.LParam & 0xffff)), unchecked((short)((long)m.LParam >> 16))));
        int edge = S(7);
        bool left = point.X < edge, right = point.X >= Width - edge, top = point.Y < edge, bottom = point.Y >= Height - edge;
        m.Result = (IntPtr)(top && left ? 13 : top && right ? 14 : bottom && left ? 16 : bottom && right ? 17 : left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 1);
    }

    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(Handle, NativeMethods.WM_NCLBUTTONDOWN, (IntPtr)NativeMethods.HT_CAPTION, IntPtr.Zero);
    }

    private void ToggleMaximize() => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;

    private void SwitchTab(AppTab tab)
    {
        if (_busy && tab != AppTab.DeletingAnimation) return;
        _currentTab = tab;
        foreach (var view in _views) view.Value.Visible = view.Key == tab;
        foreach (var nav in _navigation)
        {
            nav.Value.Selected = nav.Key == tab || tab == AppTab.DeletingAnimation && nav.Key == AppTab.Delete;
            nav.Value.Invalidate();
        }
        _title.Text = tab == AppTab.DeletingAnimation ? (_busy ? "Deleting" : "Completed") : tab.ToString();
        _searchBox.Visible = tab == AppTab.History;
        _ring.Animating = tab == AppTab.DeletingAnimation && _busy;
        if (tab == AppTab.History) RefreshHistory();
        PerformLayout();
        Invalidate(true);
    }

    public void AddTargetItem(string path) => AddTargetItems([path]);

    public void AddTargetItems(IEnumerable<string> paths)
    {
        if (_busy) return;
        bool added = false;
        foreach (string raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string path;
            try { path = Path.GetFullPath(raw.Trim('"', ' ')); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            if ((!File.Exists(path) && !Directory.Exists(path)) || _items.Any(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase))) continue;
            var item = new TargetItem(path);
            _items.Add(item);
            added = true;
            if (IsHandleCreated) _ = InspectTarget(item);
            else Shown += (_, _) => { if (_items.Contains(item)) _ = InspectTarget(item); };
        }
        if (added) RefreshItems();
    }

    private async Task InspectTarget(TargetItem item)
    {
        try
        {
            if (item.IsDirectory && !item.IsSizeCalculated)
            {
                _ = item.PopulateDirectoryMetricsAsync(() =>
                {
                    if (!IsDisposed && !Disposing)
                        BeginInvoke(() => { _itemList.Invalidate(true); _previews.Invalidate(true); });
                });
            }
            var locks = await Task.Run(() => ProcessUnlocker.FindLockingProcesses(item.FullPath));
            if (IsDisposed || Disposing || !_items.Contains(item)) return;
            item.LockingProcesses = locks;
            item.Status = locks.Count > 0 ? ItemLockStatus.Locked : ItemLockStatus.Ready;
            _itemList.Invalidate(true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            item.Status = ItemLockStatus.Protected;
        }
    }

    private void ClearItems() { if (_busy) return; _items.Clear(); RefreshItems(); }
    private void RemoveItem(TargetItem item) { if (_busy) return; _items.Remove(item); RefreshItems(); }
    private static void ClearControls(Control parent)
    {
        while (parent.Controls.Count > 0) parent.Controls[0].Dispose();
    }

    private void RefreshItems()
    {
        _itemList.SuspendLayout();
        _previews.SuspendLayout();
        ClearControls(_itemList);
        ClearControls(_previews);
        foreach (var item in _items)
        {
            _itemList.Controls.Add(ItemRow(item, false));
            if (_previews.Controls.Count < 3) _previews.Controls.Add(ItemRow(item, true));
        }
        _itemList.ResumeLayout();
        _previews.ResumeLayout();
        _empty.Visible = _items.Count == 0;
        _itemList.Visible = _items.Count > 0;
        _delete.Enabled = _items.Count > 0 && !_busy;
        _clear.Enabled = _items.Count > 0 && !_busy;
        _previewCard.AccessibleName = $"{_items.Count} selected items";
        PerformLayout();
    }

    private SurfacePanel ItemRow(TargetItem item, bool preview)
    {
        var row = new SurfacePanel { IsRow = true, Size = new Size(preview ? S(232) : _itemList.Width, S(preview ? 60 : 74)), Margin = new Padding(0, 0, preview ? S(16) : 0, S(12)), Tag = item };
        var remove = AddButton(row, "Remove" + item.Name, "x", "", () => RemoveItem(item));
        remove.Circular = true;
        remove.Quiet = true;
        remove.AccessibleName = "Remove " + item.Name;
        row.Resize += (_, _) => remove.SetBounds(row.Width - S(preview ? 45 : 59), S(preview ? 14 : 18), S(preview ? 32 : 38), S(preview ? 32 : 38));
        remove.SetBounds(row.Width - S(preview ? 45 : 59), S(preview ? 14 : 18), S(preview ? 32 : 38), S(preview ? 32 : 38));
        row.Paint += (_, e) =>
        {
            int badgeSize = S(preview ? 42 : 54);
            UITheme.Badge(e.Graphics, new Rectangle(S(preview ? 11 : 17), S(preview ? 9 : 10), badgeSize, badgeSize), ItemIcon(item.FileType), ItemColor(item.FileType));
            UITheme.Text(e.Graphics, item.Name, new Rectangle(S(preview ? 60 : 94), 0, row.Width - S(preview ? 109 : 213), row.Height), S(preview ? 14 : 18), UITheme.TextPrimary);
            if (preview) return;
            (string label, Color badgeColor) = item.Status switch
            {
                ItemLockStatus.Locked => ("Locked", UITheme.DotOrange),
                ItemLockStatus.Protected => ("Protected", UITheme.AccentRed),
                ItemLockStatus.Checking => ("Checking", UITheme.TextMuted),
                _ => ("Ready", UITheme.DotGreen)
            };
            int badgeW = S(68), badgeH = S(22);
            var badgeRect = new Rectangle(row.Width - S(140), (row.Height - badgeH) / 2, badgeW, badgeH);
            using var badgePath = UITheme.GetRoundedPath(badgeRect, S(4));
            using var badgeBg = new SolidBrush(Color.FromArgb(UITheme.Dark ? 28 : 22, badgeColor));
            using var badgePen = new Pen(Color.FromArgb(UITheme.Dark ? 85 : 75, badgeColor));
            e.Graphics.FillPath(badgeBg, badgePath);
            e.Graphics.DrawPath(badgePen, badgePath);
            UITheme.Text(e.Graphics, label, badgeRect, S(11), badgeColor, bold: true, align: ContentAlignment.MiddleCenter);
        };
        _tips.SetToolTip(row, item.FullPath);
        EnableDrop(row);
        return row;
    }

    private static string ItemIcon(TargetFileType type) => type switch { TargetFileType.Folder => "folder-filled", TargetFileType.Image => "photo", TargetFileType.Archive => "file-zip", _ => "file-filled" };
    private static Color ItemColor(TargetFileType type) => type switch { TargetFileType.Folder => UITheme.AccentBlue, TargetFileType.Image => UITheme.AccentPurple, TargetFileType.Archive => UITheme.DotOrange, _ => UITheme.TextPrimary };

    private void RefreshHistory()
    {
        _historyList.SuspendLayout();
        ClearControls(_historyList);
        _state.Prune();
        var records = _state.History.Where(h => (h.Name.Contains(_search.Text, StringComparison.OrdinalIgnoreCase) || h.FullPath.Contains(_search.Text, StringComparison.OrdinalIgnoreCase)) &&
            (_filter switch { "Files" => !h.IsDirectory, "Folders" => h.IsDirectory, "Successful" => h.IsSuccessful, "With Warnings" => !h.IsSuccessful, _ => true }));
        foreach (var record in records)
        {
            var row = new SurfacePanel { Border = false, Height = S(66), Width = _historyList.Width, Margin = Padding.Empty };
            var more = AddButton(row, "HistoryDetails", "dots", "", () => MessageBox.Show(this, $"{record.FullPath}\n\n{record.Details}", record.StatusText, MessageBoxButtons.OK, MessageBoxIcon.Information));
            more.Circular = true;
            row.Resize += (_, _) => more.SetBounds(row.Width - S(33), S(20), S(32), S(32));
            more.SetBounds(row.Width - S(33), S(20), S(32), S(32));
            row.Paint += (_, e) => PaintHistoryRow(e.Graphics, row, record);
            _historyList.Controls.Add(row);
        }
        if (_historyList.Controls.Count == 0)
        {
            var empty = new Label { Text = _state.History.Count == 0 ? "No deletion history yet" : "No matching history", TextAlign = ContentAlignment.MiddleCenter, ForeColor = UITheme.TextMuted, Width = _historyList.Width, Height = S(180), Font = UITheme.Font(S(17)) };
            _historyList.Controls.Add(empty);
        }
        _historyList.ResumeLayout();
        ResizeRows();
    }

    private void PaintHistoryRow(Graphics g, Control row, HistoryRecord rec)
    {
        UITheme.Badge(g, new Rectangle(0, S(10), S(51), S(51)), ItemIcon(rec.FileType), ItemColor(rec.FileType));
        int nameX = Math.Max(S(210), (int)(row.Width * .37)), statusX = row.Width - S(151);
        UITheme.Text(g, rec.Timestamp.ToString("MMM d, yyyy", CultureInfo.InvariantCulture), new Rectangle(S(67), S(14), nameX - S(80), S(24)), S(14), UITheme.TextPrimary);
        UITheme.Text(g, rec.Timestamp.ToString("h:mm tt", CultureInfo.InvariantCulture), new Rectangle(S(67), S(38), nameX - S(80), S(20)), S(13), UITheme.TextSecondary);
        UITheme.Text(g, rec.Name, new Rectangle(nameX, S(14), Math.Max(1, statusX - nameX - S(12)), S(24)), S(15), UITheme.TextPrimary);
        string details = rec.IsDirectory ? "Folder" : $"{rec.FileCount} {(rec.FileCount == 1 ? "item" : "items")}, {FormatSize(rec.TotalSizeBytes)}";
        UITheme.Text(g, details, new Rectangle(nameX, S(38), Math.Max(1, statusX - nameX - S(12)), S(20)), S(13), UITheme.TextSecondary);
        Color statusColor = rec.IsSuccessful ? UITheme.DotGreen : UITheme.DotOrange;
        int badgeW = S(92), badgeH = S(24);
        var badgeRect = new Rectangle(statusX, (row.Height - badgeH) / 2, badgeW, badgeH);
        using var badgePath = UITheme.GetRoundedPath(badgeRect, S(4));
        using var badgeBg = new SolidBrush(Color.FromArgb(UITheme.Dark ? 28 : 20, statusColor));
        using var badgePen = new Pen(Color.FromArgb(UITheme.Dark ? 80 : 70, statusColor));
        g.FillPath(badgeBg, badgePath);
        g.DrawPath(badgePen, badgePath);
        UITheme.Text(g, rec.StatusText, badgeRect, S(11), statusColor, bold: true, align: ContentAlignment.MiddleCenter);
        using var separator = new Pen(Color.FromArgb(60, UITheme.BorderSubtle));
        g.DrawLine(separator, 0, row.Height - 1, row.Width, row.Height - 1);
    }

    private async Task DeleteAsync()
    {
        if (_busy || _items.Count == 0) return;
        if (_state.Settings.Confirm && MessageBox.Show(this, $"{_items.Count}개 항목을 영구 삭제할까요?\n휴지통으로 이동하지 않으며 되돌릴 수 없습니다.", "Confirm deletion", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        _busy = true;
        _results.Clear();
        var targets = _items.ToArray();
        bool unlock = _state.Settings.RemoveLocked, secure = _state.Settings.SecureDelete, turbo = _state.Settings.TurboMode;
        _totalItems = targets.Length;
        _totalBytes = targets.Sum(i => i.FileSizeBytes);
        _hasFolderSizeUnknown = targets.Any(i => i.IsDirectory);
        _done.Enabled = false;
        _ring.Progress = 0;
        _ring.StatusText = "Deleting files...";
        UpdateResults();
        SwitchTab(AppTab.DeletingAnimation);
        try
        {
            var reporting = new Progress<HistoryRecord>(record =>
            {
                _results.Add(record);
                _state.History.Insert(0, record);
                _ring.Progress = _results.Count * 100f / targets.Length;
                _tips.SetToolTip(_ring, record.Name);
                UpdateResults();
            });
            await Task.Run(() => Parallel.ForEach(targets, new ParallelOptions
            {
                MaxDegreeOfParallelism = turbo ? Math.Clamp(Environment.ProcessorCount * 2, 4, 16) : 1
            }, item =>
            {
                DeleteResult result;
                try { result = ForceDeleteEngine.ForceDelete(item.FullPath, unlock, secure, turbo); }
                catch (Exception ex) { result = new DeleteResult(DeleteResultStatus.Failed, ex.Message, 0); }
                var record = new HistoryRecord
                {
                    Name = item.Name, FullPath = item.FullPath, IsDirectory = item.IsDirectory, FileType = item.FileType,
                    TotalSizeBytes = item.FileSizeBytes, IsSuccessful = result.Status == DeleteResultStatus.Success,
                    IsScheduled = result.Status == DeleteResultStatus.ScheduledOnReboot, Details = result.Message
                };
                ((IProgress<HistoryRecord>)reporting).Report(record);
            }));
            _ring.StatusText = _results.All(r => r.IsSuccessful) ? "Completed" : "With warnings";
            // Let the displayed arc settle after real work; never inflate backend progress.
            await Task.Delay(350);
            _items.RemoveAll(i => !File.Exists(i.FullPath) && !Directory.Exists(i.FullPath));
            SaveState();
        }
        finally
        {
            _ring.Animating = false;
            _done.Enabled = true;
            _title.Text = "Completed";
            _busy = false;
            RefreshItems();
        }
    }

    private void UpdateResults()
    {
        _success.Text = $"{_results.Count(r => r.IsSuccessful)} items deleted successfully";
        _warning.Text = $"{_results.Count(r => !r.IsSuccessful)} items need attention";
        _success.Invalidate(); _warning.Invalidate();
    }

    private void ShowResults(bool success)
    {
        var records = _results.Where(r => r.IsSuccessful == success).ToArray();
        MessageBox.Show(this, records.Length == 0 ? "No items" : string.Join("\n\n", records.Select(r => $"{r.Name} — {r.StatusText}\n{r.Details}")), success ? "Deleted items" : "Items with warnings", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void PromptFiles()
    {
        using var dialog = new OpenFileDialog { Title = "삭제할 파일 선택", Multiselect = true, Filter = "All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        AddTargetItems(dialog.FileNames);
        SwitchTab(AppTab.Delete);
    }

    private void PromptFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "삭제할 폴더 선택", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        AddTargetItem(dialog.SelectedPath);
        SwitchTab(AppTab.Delete);
    }

    private void EnableDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += (_, e) => e.Effect = !_busy && e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        control.DragDrop += (_, e) =>
        {
            if (_busy || e.Data?.GetData(DataFormats.FileDrop) is not string[] paths) return;
            AddTargetItems(paths);
            SwitchTab(AppTab.Delete);
        };
    }

    private void ApplySettings()
    {
        bool actual = ContextMenuHelper.IsContextMenuInstalled();
        if (_context.Checked != actual)
        {
            bool ok = _context.Checked ? ContextMenuHelper.InstallContextMenu() : ContextMenuHelper.UninstallContextMenu();
            if (!ok) { _context.Checked = actual; MessageBox.Show(this, "탐색기 메뉴 설정을 변경하지 못했습니다. 관리자 권한을 확인하세요.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        }
        _state.Settings.SecureDelete = _secure.Checked;
        _state.Settings.TurboMode = _turbo.Checked;
        _state.Settings.Confirm = _confirm.Checked;
        _state.Settings.RemoveLocked = _unlock.Checked;
        _state.Settings.DarkMode = _dark.Checked;
        _state.Settings.RetentionDays = _retention.SelectedIndex switch { 0 => 7, 2 => 90, 3 => 0, _ => 30 };
        UITheme.Dark = _dark.Checked;
        ApplyTheme(this);
        SaveState();
        _apply.Text = "Applied";
        _apply.Invalidate();
    }

    private void ShowAdvancedSettings()
    {
        using var dialog = new Form
        {
            Text = "More options", StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(S(540), S(232)), FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, BackColor = UITheme.BgApp, ShowInTaskbar = false
        };
        var options = new[]
        {
            ("Turbo fast delete", "Use the optimized native deletion engine", _turbo),
            ("Explorer context menu", "Add Force Delete to the Explorer menu", _context)
        };
        var toggles = new List<ModernToggleSwitch>();
        for (int i = 0; i < options.Length; i++)
        {
            var option = options[i];
            var row = new SurfacePanel { IsRow = true, Bounds = new Rectangle(S(16), S(16 + 76 * i), S(508), S(66)) };
            var toggle = new ModernToggleSwitch { Checked = option.Item3.Checked, AccessibleName = option.Item1, Bounds = new Rectangle(S(436), S(19), S(54), S(29)) };
            toggles.Add(toggle);
            row.Controls.Add(toggle);
            row.Paint += (_, e) =>
            {
                UITheme.Text(e.Graphics, option.Item1, new Rectangle(S(16), S(7), S(410), S(26)), S(17), UITheme.TextPrimary);
                UITheme.Text(e.Graphics, option.Item2, new Rectangle(S(16), S(34), S(410), S(22)), S(13), UITheme.TextSecondary);
            };
            dialog.Controls.Add(row);
        }
        var apply = AddButton(dialog, "ApplyAdvanced", "check", "Apply", () =>
        {
            _turbo.Checked = toggles[0].Checked;
            _context.Checked = toggles[1].Checked;
            ApplySettings();
            dialog.Close();
        });
        apply.Primary = true;
        apply.Bounds = new Rectangle(S(344), S(178), S(180), S(42));
        dialog.ShowDialog(this);
    }

    private void SaveState()
    {
        try { _state.Save(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { MessageBox.Show(this, "기록/설정을 저장하지 못했습니다.\n" + e.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void ApplyTheme(Control control)
    {
        if (control is FlowLayoutPanel) control.BackColor = UITheme.BgCardContainer;
        if (control is ThemedFlowPanel scroll) scroll.ApplyScrollTheme();
        if (control == _filters) control.BackColor = UITheme.BgApp;
        if (control is TextBox or ComboBox)
        {
            control.BackColor = UITheme.BgItemCard;
            control.ForeColor = UITheme.TextPrimary;
            control.Font = UITheme.Font(S(14));
        }
        if (control == _title) control.ForeColor = UITheme.TextPrimary;
        foreach (Control child in control.Controls) ApplyTheme(child);
        control.Invalidate();
    }

    internal static string HistoryCsv(IEnumerable<HistoryRecord> history)
    {
        static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        return "Timestamp,Name,Path,Status,Bytes,Details\r\n" + string.Join("\r\n", history.Select(h => string.Join(",", Quote(h.Timestamp.ToString("O")), Quote(h.Name), Quote(h.FullPath), Quote(h.StatusText), h.TotalSizeBytes.ToString(CultureInfo.InvariantCulture), Quote(h.Details))));
    }

    private void ExportHistory()
    {
        using var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv|Text (*.txt)|*.txt", FileName = $"ForceDeleter_History_{DateTime.Now:yyyyMMdd_HHmm}.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(dialog.FileName, HistoryCsv(_state.History), new UTF8Encoding(true)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MessageBox.Show(this, e.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    internal static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }
}
