using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ForceDeleter;

public enum AppTab
{
    Delete,
    Add,
    History,
    Settings,
    DeletingAnimation
}

public partial class MainForm : Form
{
    private AppTab _currentTab = AppTab.Delete;
    private readonly List<TargetItem> _items = [];
    private readonly List<HistoryRecord> _history = [];

    // Main layout panels
    private Panel pnlSidebar = null!;
    private Panel pnlMain = null!;
    private Panel pnlHeader = null!;
    private Label lblTitle = null!;

    // Sidebar navigation buttons
    private Button btnNavDelete = null!;
    private Button btnNavAdd = null!;
    private Button btnNavHistory = null!;
    private Button btnNavSettings = null!;

    // Tab 1: Delete View Controls
    private Panel pnlDeleteView = null!;
    private Panel pnlCardContainer = null!;
    private FlowLayoutPanel pnlItemList = null!;
    private Label lblEmptyHint = null!;
    private Button btnDelete = null!;
    private Button btnClear = null!;

    // Tab 2: Add View Controls
    private Panel pnlAddView = null!;
    private FlowLayoutPanel pnlRecentPreview = null!;

    // Tab 3: History View Controls
    private Panel pnlHistoryView = null!;
    private FlowLayoutPanel pnlHistoryList = null!;
    private string _historyFilter = "All";
    private TextBox txtSearchHistory = null!;

    // Tab 4: Settings View Controls
    private Panel pnlSettingsView = null!;
    private ModernToggleSwitch swTurboMode = null!;
    private ModernToggleSwitch swSecureDelete = null!;
    private ModernToggleSwitch swConfirmPrompt = null!;
    private ModernToggleSwitch swRemoveLocked = null!;
    private ModernToggleSwitch swDarkMode = null!;
    private ModernToggleSwitch swContextMenu = null!;

    // Tab 5: Deleting Animation View Controls
    private Panel pnlDeletingView = null!;
    private CircularProgressControl progressRing = null!;
    private Label lblTotalItems = null!;
    private Label lblTotalSize = null!;
    private Panel pnlSuccessCard = null!;
    private Label lblSuccessText = null!;
    private Label lblSuccessSize = null!;
    private Panel pnlWarningCard = null!;
    private Label lblWarningText = null!;
    private Label lblWarningSize = null!;
    private Button btnDone = null!;

    public MainForm(string? initialPath = null) : this(initialPath != null ? new[] { initialPath } : null)
    {
    }

    public MainForm(IEnumerable<string>? initialPaths)
    {
        InitializeComponent();
        ConfigureCustomWindow();
        BuildModernUI();
        LoadSampleHistory();

        if (initialPaths != null)
        {
            AddTargetItems(initialPaths);
        }
    }

    private static void MakeRoundedControl(Control control, int radius = 12)
    {
        void UpdateRegion()
        {
            if (control.Width > 0 && control.Height > 0)
            {
                using var path = UITheme.GetRoundedPath(new Rectangle(0, 0, control.Width, control.Height), radius);
                control.Region = new Region(path);
            }
        }
        control.SizeChanged += (s, e) => UpdateRegion();
        UpdateRegion();
    }

    private void ConfigureCustomWindow()
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.BackColor = UITheme.BgApp;
        this.Size = new Size(820, 600);
        this.MinimumSize = new Size(760, 540);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.AllowDrop = true;
        this.DoubleBuffered = true;

        MakeRoundedControl(this, 16);
        this.Resize += (s, e) => MakeRoundedControl(this, 16);

        try
        {
            int preference = 2; // DWMWCP_ROUND (Windows 11 rounded corners)
            NativeMethods.DwmSetWindowAttribute(this.Handle, 33, ref preference, sizeof(int));
        }
        catch { }

        this.DragEnter += MainForm_DragEnter;
        this.DragDrop += MainForm_DragDrop;
    }

    private void BuildModernUI()
    {
        // -------------------------------------------------------------
        // Left Sidebar Navigation Rail
        // -------------------------------------------------------------
        pnlSidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = 80,
            BackColor = UITheme.BgSidebar
        };
        pnlSidebar.Paint += (s, e) =>
        {
            UITheme.EnableAntiAlias(e.Graphics);
            Button? activeBtn = _currentTab switch
            {
                AppTab.Delete or AppTab.DeletingAnimation => btnNavDelete,
                AppTab.Add => btnNavAdd,
                AppTab.History => btnNavHistory,
                AppTab.Settings => btnNavSettings,
                _ => null
            };
            if (activeBtn != null)
            {
                var glowRect = new Rectangle(activeBtn.Left - 6, activeBtn.Top - 6, activeBtn.Width + 12, activeBtn.Height + 12);
                using var glowPath = UITheme.GetRoundedPath(glowRect, 16);
                using var glowBrush = new SolidBrush(Color.FromArgb(45, UITheme.AccentRed));
                e.Graphics.FillPath(glowBrush, glowPath);
            }
        };

        btnNavDelete = CreateNavButton(SvgIcons.DrawTrash, AppTab.Delete);
        btnNavDelete.Location = new Point(16, 24);
        btnNavDelete.Click += (s, e) => SwitchTab(AppTab.Delete);
        pnlSidebar.Controls.Add(btnNavDelete);

        btnNavAdd = CreateNavButton(SvgIcons.DrawFolder, AppTab.Add);
        btnNavAdd.Location = new Point(16, 84);
        btnNavAdd.Click += (s, e) => SwitchTab(AppTab.Add);
        pnlSidebar.Controls.Add(btnNavAdd);

        btnNavHistory = CreateNavButton(SvgIcons.DrawClock, AppTab.History);
        btnNavHistory.Location = new Point(16, 144);
        btnNavHistory.Click += (s, e) => SwitchTab(AppTab.History);
        pnlSidebar.Controls.Add(btnNavHistory);

        btnNavSettings = CreateNavButton(SvgIcons.DrawGear, AppTab.Settings);
        btnNavSettings.Location = new Point(16, 204);
        btnNavSettings.Click += (s, e) => SwitchTab(AppTab.Settings);
        pnlSidebar.Controls.Add(btnNavSettings);

        // -------------------------------------------------------------
        // Main Container
        // -------------------------------------------------------------
        pnlMain = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.BgApp,
            Padding = new Padding(20, 20, 24, 24)
        };

        // Window Controls (Right-top)
        pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 50,
            BackColor = Color.Transparent
        };
        pnlHeader.MouseDown += WindowDrag_MouseDown;

        lblTitle = new Label
        {
            Text = "Delete",
            Font = new Font("Segoe UI", 20F, FontStyle.Bold),
            ForeColor = UITheme.TextPrimary,
            AutoSize = true,
            Location = new Point(4, 2)
        };
        lblTitle.MouseDown += WindowDrag_MouseDown;
        pnlHeader.Controls.Add(lblTitle);

        var btnClose = CreateWindowControlButton("✕", () => this.Close(), Color.FromArgb(239, 68, 68));
        btnClose.Location = new Point(pnlHeader.Width - 36, 6);
        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        var btnMaximize = CreateWindowControlButton("□", () =>
        {
            this.WindowState = this.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        });
        btnMaximize.Location = new Point(pnlHeader.Width - 72, 6);
        btnMaximize.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        var btnMinimize = CreateWindowControlButton("—", () => this.WindowState = FormWindowState.Minimized);
        btnMinimize.Location = new Point(pnlHeader.Width - 108, 6);
        btnMinimize.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        pnlHeader.Controls.Add(btnClose);
        pnlHeader.Controls.Add(btnMaximize);
        pnlHeader.Controls.Add(btnMinimize);

        // Build all 5 Views
        BuildDeleteView();
        BuildAddView();
        BuildHistoryView();
        BuildSettingsView();
        BuildDeletingAnimationView();

        // Assembly
        pnlMain.Controls.Add(pnlDeleteView);
        pnlMain.Controls.Add(pnlAddView);
        pnlMain.Controls.Add(pnlHistoryView);
        pnlMain.Controls.Add(pnlSettingsView);
        pnlMain.Controls.Add(pnlDeletingView);
        pnlMain.Controls.Add(pnlHeader);

        this.Controls.Add(pnlSidebar);
        this.Controls.Add(pnlMain);
        pnlSidebar.SendToBack();

        SwitchTab(AppTab.Delete);
    }

    // =============================================================
    // VIEW 1: Delete (Main View - Matches Screen 1)
    // =============================================================
    private void BuildDeleteView()
    {
        pnlDeleteView = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.BgApp
        };

        // Bottom Action Bar
        var pnlBottomActions = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 65,
            BackColor = UITheme.BgApp,
            Padding = new Padding(0, 10, 0, 0)
        };
        pnlBottomActions.Paint += (s, e) =>
        {
            UITheme.EnableAntiAlias(e.Graphics);
            var glowRect = new Rectangle(btnDelete.Left, btnDelete.Top + 6, btnDelete.Width, btnDelete.Height);
            using var glowPath = UITheme.GetRoundedPath(glowRect, 14);
            using var glowBrush = new SolidBrush(Color.FromArgb(40, UITheme.AccentRed));
            e.Graphics.FillPath(glowBrush, glowPath);
        };

        btnClear = new Button
        {
            Text = "✕  Clear",
            Size = new Size(135, 52),
            Location = new Point(pnlBottomActions.Width - 135, 10),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = UITheme.BgSubtleBtn,
            ForeColor = UITheme.TextSecondary,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btnClear.FlatAppearance.BorderSize = 1;
        btnClear.FlatAppearance.BorderColor = UITheme.BorderSubtle;
        MakeRoundedControl(btnClear, 12);
        btnClear.Click += (s, e) => ClearItems();
        pnlBottomActions.Controls.Add(btnClear);

        btnDelete = new Button
        {
            Text = "       Delete",
            Size = new Size(pnlBottomActions.Width - 150, 52),
            Location = new Point(0, 10),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = UITheme.AccentRed,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11.5F, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btnDelete.FlatAppearance.BorderSize = 0;
        MakeRoundedControl(btnDelete, 12);
        btnDelete.Paint += (s, e) =>
        {
            UITheme.EnableAntiAlias(e.Graphics);
            var sz = TextRenderer.MeasureText(btnDelete.Text.Trim(), btnDelete.Font);
            int iconX = Math.Max(16, (btnDelete.Width - sz.Width) / 2 - 22);
            int iconY = (btnDelete.Height / 2) - 8;
            SvgIcons.DrawTrash(e.Graphics, new Rectangle(iconX, iconY, 16, 16), Color.White);
        };
        btnDelete.Click += StartForceDeleteProcess;
        pnlBottomActions.Controls.Add(btnDelete);

        // Card Container
        pnlCardContainer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.BgCardContainer,
            Padding = new Padding(12)
        };
        MakeRoundedControl(pnlCardContainer, 16);
        pnlCardContainer.Paint += (s, e) =>
        {
            UITheme.EnableAntiAlias(e.Graphics);
            using var path = UITheme.GetRoundedPath(new Rectangle(0, 0, pnlCardContainer.Width - 1, pnlCardContainer.Height - 1), 16);
            using var brush = new SolidBrush(UITheme.BgCardContainer);
            e.Graphics.FillPath(brush, path);
            using var pen = new Pen(UITheme.BorderSubtle, 1f);
            e.Graphics.DrawPath(pen, path);
        };

        pnlItemList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = UITheme.BgCardContainer
        };
        pnlCardContainer.Controls.Add(pnlItemList);

        lblEmptyHint = new Label
        {
            Text = "파일이나 폴더를 여기에 끌어다 놓으세요\n(Drag & Drop files or folders here)",
            Font = new Font("Segoe UI", 11F, FontStyle.Regular),
            ForeColor = UITheme.TextMuted,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        lblEmptyHint.MouseDown += WindowDrag_MouseDown;
        pnlCardContainer.Controls.Add(lblEmptyHint);

        pnlDeleteView.Controls.Add(pnlCardContainer);
        pnlDeleteView.Controls.Add(pnlBottomActions);
    }

    // =============================================================
    // VIEW 2: Add (Matches Screen 2)
    // =============================================================
    private void BuildAddView()
    {
        pnlAddView = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.BgApp,
            Visible = false
        };

        var pnlAddCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.BgCardContainer,
            Padding = new Padding(16)
        };
        MakeRoundedControl(pnlAddCard, 16);
        pnlAddCard.Paint += (s, e) =>
        {
            UITheme.EnableAntiAlias(e.Graphics);
            using var path = UITheme.GetRoundedPath(new Rectangle(0, 0, pnlAddCard.Width - 1, pnlAddCard.Height - 1), 16);
            using var brush = new SolidBrush(UITheme.BgCardContainer);
            e.Graphics.FillPath(brush, path);
            using var pen = new Pen(UITheme.BorderSubtle, 1f);
            e.Graphics.DrawPath(pen, path);
        };

        // Top Large Dashed Drop Zone
        var pnlDropZone = new Panel
        {
            Dock = DockStyle.Top,
            Height = 180,
            BackColor = UITheme.BgCardContainer,
            Cursor = Cursors.Hand,
            AllowDrop = true
        };
        pnlDropZone.DragEnter += MainForm_DragEnter;
        pnlDropZone.DragDrop += MainForm_DragDrop;
        pnlDropZone.Paint += (s, e) =>
        {
            UITheme.EnableAntiAlias(e.Graphics);
            var rect = new Rectangle(0, 0, pnlDropZone.Width - 1, pnlDropZone.Height - 1);
            using var path = UITheme.GetRoundedPath(rect, 14);
            using var pen = new Pen(Color.FromArgb(60, 70, 95), 1.5f) { DashStyle = DashStyle.Dash };
            e.Graphics.DrawPath(pen, path);

            // Center Glowing Folder + Upload Arrow
            int cx = pnlDropZone.Width / 2;
            int cy = pnlDropZone.Height / 2;

            // Outer subtle circle aura
            using var auraBrush = new SolidBrush(Color.FromArgb(20, UITheme.AccentPurple));
            e.Graphics.FillEllipse(auraBrush, cx - 45, cy - 45, 90, 90);

            // Center Folder icon with upward arrow
            var folderBox = new Rectangle(cx - 30, cy - 26, 60, 52);
            using var fPath = UITheme.GetRoundedPath(folderBox, 10);
            using var fBrush = new LinearGradientBrush(folderBox, Color.FromArgb(70, 80, 140), Color.FromArgb(40, 50, 90), 45f);
            e.Graphics.FillPath(fBrush, fPath);

            SvgIcons.DrawUploadArrow(e.Graphics, new Rectangle(cx - 15, cy - 15, 30, 30), Color.FromArgb(200, 220, 255), 2.5f);
        };
        pnlDropZone.Click += (s, e) => PromptAddFiles();

        // Middle: 2 Action Selection Cards (Add File, Add Folder)
        var pnlTwoCards = new Panel
        {
            Dock = DockStyle.Top,
            Height = 85,
            BackColor = UITheme.BgCardContainer,
            Padding = new Padding(0, 12, 0, 0)
        };

        int cardW = (670 - 16) / 2;

        // Card 1: Add File (Multi-select)
        var pnlBtnFile = CreateAddSelectorCard("파일 다중 추가 (Add Files)", SvgIcons.DrawDocument, Color.FromArgb(160, 180, 220), () => PromptAddFiles());
        pnlBtnFile.Location = new Point(0, 10);
        pnlBtnFile.Size = new Size(cardW, 68);
        pnlTwoCards.Controls.Add(pnlBtnFile);

        // Card 2: Add Folder
        var pnlBtnFolder = CreateAddSelectorCard("폴더 추가 (Add Folder)", SvgIcons.DrawFolder, UITheme.AccentBlue, () => PromptAddFolder());
        pnlBtnFolder.Location = new Point(cardW + 16, 10);
        pnlBtnFolder.Size = new Size(cardW, 68);
        pnlTwoCards.Controls.Add(pnlBtnFolder);

        // Bottom: Quick Preview of added items
        var pnlRecentArea = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.BgCardContainer,
            Padding = new Padding(0, 10, 0, 0)
        };
        var lblRecentTitle = new Label
        {
            Text = "추가된 대상 미리보기 (Quick Preview)",
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = UITheme.TextSecondary,
            Dock = DockStyle.Top,
            Height = 26
        };
        pnlRecentArea.Controls.Add(lblRecentTitle);

        pnlRecentPreview = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = UITheme.BgCardContainer
        };
        pnlRecentArea.Controls.Add(pnlRecentPreview);
        lblRecentTitle.BringToFront();

        // Order: Dock=Fill first, then Top middle, then Top topmost
        pnlAddCard.Controls.Add(pnlRecentArea);
        pnlAddCard.Controls.Add(pnlTwoCards);
        pnlAddCard.Controls.Add(pnlDropZone);

        pnlAddView.Controls.Add(pnlAddCard);
    }

    private Panel CreateAddSelectorCard(string text, Action<Graphics, Rectangle, Color, float> drawIcon, Color iconColor, Action onClick)
    {
        var pnl = new Panel
        {
            BackColor = UITheme.BgItemCard,
            Cursor = Cursors.Hand
        };
        MakeRoundedControl(pnl, 12);
        pnl.Paint += (s, e) =>
        {
            UITheme.EnableAntiAlias(e.Graphics);
            using var path = UITheme.GetRoundedPath(new Rectangle(0, 0, pnl.Width - 1, pnl.Height - 1), 12);
            using var brush = new SolidBrush(pnl.BackColor);
            e.Graphics.FillPath(brush, path);
            using var borderPen = new Pen(UITheme.BorderSubtle, 1f);
            e.Graphics.DrawPath(borderPen, path);

            // Icon with small plus badge
            var iconBox = new Rectangle(18, 16, 36, 36);
            drawIcon(e.Graphics, iconBox, iconColor, 2f);
            SvgIcons.DrawPlus(e.Graphics, new Rectangle(38, 34, 14, 14), Color.FromArgb(120, 180, 255), 2f);

            // Text
            using var font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
            using var textBrush = new SolidBrush(UITheme.TextPrimary);
            e.Graphics.DrawString(text, font, textBrush, 68, 24);

            // Right Chevron '>'
            SvgIcons.DrawChevronRight(e.Graphics, new Rectangle(pnl.Width - 36, 24, 20, 20), UITheme.TextMuted, 2f);
        };
        pnl.MouseEnter += (s, e) => { pnl.BackColor = UITheme.BgItemCardHover; pnl.Invalidate(); };
        pnl.MouseLeave += (s, e) => { pnl.BackColor = UITheme.BgItemCard; pnl.Invalidate(); };
        pnl.Click += (s, e) => onClick();
        return pnl;
    }

    // =============================================================
    // VIEW 3: History (Matches Screen 3)
    // =============================================================
    private void BuildHistoryView()
    {
        pnlHistoryView = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.BgApp,
            Visible = false
        };

        // Top Search Bar & Filter Chips
        var pnlTopHistory = new Panel
        {
            Dock = DockStyle.Top,
            Height = 44,
            BackColor = UITheme.BgApp
        };

        txtSearchHistory = new TextBox
        {
            PlaceholderText = "Search history...",
            Location = new Point(pnlTopHistory.Width - 220, 6),
            Size = new Size(210, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = UITheme.BgCardContainer,
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9.5F)
        };
        txtSearchHistory.TextChanged += (s, e) => RefreshHistoryListUI();
        pnlTopHistory.Controls.Add(txtSearchHistory);

        // Filter chips: All, Files, Folders, Successful, With Warnings
        string[] chips = ["All", "Files", "Folders", "Successful", "With Warnings"];
        int chipX = 0;
        foreach (var chip in chips)
        {
            var btnChip = CreateFilterChip(chip, chip == "All");
            btnChip.Location = new Point(chipX, 4);
            pnlTopHistory.Controls.Add(btnChip);
            chipX += btnChip.Width + 8;
        }

        // Bottom Export Bar
        var pnlBottomHistory = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 55,
            BackColor = UITheme.BgApp
        };
        var btnExport = new Button
        {
            Text = "📥  Export",
            Size = new Size(125, 42),
            Location = new Point(pnlBottomHistory.Width - 125, 8),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = UITheme.BgSubtleBtn,
            ForeColor = UITheme.TextPrimary,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btnExport.FlatAppearance.BorderSize = 1;
        btnExport.FlatAppearance.BorderColor = UITheme.BorderSubtle;
        MakeRoundedControl(btnExport, 10);
        btnExport.Click += (s, e) => ExportHistory();
        pnlBottomHistory.Controls.Add(btnExport);

        // Central List Container
        var pnlHistCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.BgCardContainer,
            Padding = new Padding(12)
        };
        MakeRoundedControl(pnlHistCard, 16);
        pnlHistCard.Paint += (s, e) =>
        {
            UITheme.EnableAntiAlias(e.Graphics);
            using var path = UITheme.GetRoundedPath(new Rectangle(0, 0, pnlHistCard.Width - 1, pnlHistCard.Height - 1), 16);
            using var brush = new SolidBrush(UITheme.BgCardContainer);
            e.Graphics.FillPath(brush, path);
            using var pen = new Pen(UITheme.BorderSubtle, 1f);
            e.Graphics.DrawPath(pen, path);
        };

        pnlHistoryList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = UITheme.BgCardContainer
        };
        pnlHistCard.Controls.Add(pnlHistoryList);

        // Docking order: Dock=Fill first, then Dock=Bottom, then Dock=Top
        pnlHistoryView.Controls.Add(pnlHistCard);
        pnlHistoryView.Controls.Add(pnlBottomHistory);
        pnlHistoryView.Controls.Add(pnlTopHistory);
    }

    private Button CreateFilterChip(string text, bool isActive)
    {
        var btn = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            BackColor = isActive ? Color.FromArgb(60, UITheme.AccentRed) : UITheme.BgSubtleBtn,
            ForeColor = isActive ? UITheme.AccentRed : UITheme.TextSecondary,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.BorderColor = isActive ? UITheme.AccentRed : UITheme.BorderSubtle;
        MakeRoundedControl(btn, 12);
        btn.Click += (s, e) =>
        {
            _historyFilter = text;
            if (btn.Parent != null)
            {
                foreach (Control c in btn.Parent.Controls)
                {
                    if (c is Button b && b.Tag is "chip")
                    {
                        b.BackColor = UITheme.BgSubtleBtn;
                        b.ForeColor = UITheme.TextSecondary;
                        b.FlatAppearance.BorderColor = UITheme.BorderSubtle;
                    }
                }
            }
            btn.BackColor = Color.FromArgb(60, UITheme.AccentRed);
            btn.ForeColor = UITheme.AccentRed;
            btn.FlatAppearance.BorderColor = UITheme.AccentRed;
            RefreshHistoryListUI();
        };
        btn.Tag = "chip";
        return btn;
    }

    // =============================================================
    // VIEW 4: Settings (Matches Screen 4)
    // =============================================================
    private void BuildSettingsView()
    {
        pnlSettingsView = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.BgApp,
            Visible = false
        };

        // Bottom Apply Bar
        var pnlBottomSettings = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 65,
            BackColor = UITheme.BgApp
        };
        pnlBottomSettings.Paint += (s, e) =>
        {
            UITheme.EnableAntiAlias(e.Graphics);
            var glowRect = new Rectangle(pnlBottomSettings.Width - 142, 6, 142, 48);
            using var glowPath = UITheme.GetRoundedPath(glowRect, 14);
            using var glowBrush = new SolidBrush(Color.FromArgb(40, UITheme.AccentRed));
            e.Graphics.FillPath(glowBrush, glowPath);
        };

        var btnApply = new Button
        {
            Text = "✓  Apply",
            Size = new Size(135, 48),
            Location = new Point(pnlBottomSettings.Width - 135, 8),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = UITheme.AccentRed,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btnApply.FlatAppearance.BorderSize = 0;
        MakeRoundedControl(btnApply, 12);
        btnApply.Click += (s, e) =>
        {
            MessageBox.Show(this, "설정이 성공적으로 저장되었습니다.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SwitchTab(AppTab.Delete);
        };
        pnlBottomSettings.Controls.Add(btnApply);

        // Settings Card
        var pnlSettingsCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.BgCardContainer,
            Padding = new Padding(16)
        };
        MakeRoundedControl(pnlSettingsCard, 16);
        pnlSettingsCard.Paint += (s, e) =>
        {
            UITheme.EnableAntiAlias(e.Graphics);
            using var path = UITheme.GetRoundedPath(new Rectangle(0, 0, pnlSettingsCard.Width - 1, pnlSettingsCard.Height - 1), 16);
            using var brush = new SolidBrush(UITheme.BgCardContainer);
            e.Graphics.FillPath(brush, path);
            using var pen = new Pen(UITheme.BorderSubtle, 1f);
            e.Graphics.DrawPath(pen, path);
        };

        var pnlScroll = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = false,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = UITheme.BgCardContainer
        };
        pnlSettingsCard.Controls.Add(pnlScroll);

        // Setting 1: Turbo fast delete
        swTurboMode = new ModernToggleSwitch { Checked = true };
        pnlScroll.Controls.Add(CreateSettingRow("Turbo fast delete", "Multi-threaded native unlinking & instant detach", SvgIcons.DrawTrash, UITheme.AccentRed, swTurboMode));

        // Setting 2: Secure delete
        swSecureDelete = new ModernToggleSwitch { Checked = false };
        pnlScroll.Controls.Add(CreateSettingRow("Secure delete", "Overwrite files to prevent recovery", SvgIcons.DrawShield, UITheme.AccentPurple, swSecureDelete));

        // Setting 2: Show confirmation prompt
        swConfirmPrompt = new ModernToggleSwitch { Checked = true };
        pnlScroll.Controls.Add(CreateSettingRow("Show confirmation prompt", "Ask before permanently deleting files", SvgIcons.DrawAlert, Color.FromArgb(245, 158, 11), swConfirmPrompt));

        // Setting 3: Remove locked files
        swRemoveLocked = new ModernToggleSwitch { Checked = true };
        pnlScroll.Controls.Add(CreateSettingRow("Remove locked files", "Try to unlock and delete in use files", SvgIcons.DrawLock, UITheme.AccentBlue, swRemoveLocked));

        // Setting 4: Dark mode
        swDarkMode = new ModernToggleSwitch { Checked = true };
        pnlScroll.Controls.Add(CreateSettingRow("Dark mode", "Use dark appearance across the app", SvgIcons.DrawMoon, UITheme.AccentPurple, swDarkMode));

        // Setting 5: Context menu integration
        swContextMenu = new ModernToggleSwitch { Checked = ContextMenuHelper.IsContextMenuInstalled() };
        swContextMenu.CheckedChanged += (s, e) =>
        {
            if (swContextMenu.Checked) ContextMenuHelper.InstallContextMenu();
            else ContextMenuHelper.UninstallContextMenu();
        };
        pnlScroll.Controls.Add(CreateSettingRow("Explorer Context Menu", "Add 'Force Delete' to Windows Explorer right-click menu", SvgIcons.DrawGear, UITheme.AccentRed, swContextMenu));

        // Order: Dock=Fill first, then Dock=Bottom
        pnlSettingsView.Controls.Add(pnlSettingsCard);
        pnlSettingsView.Controls.Add(pnlBottomSettings);
    }

    private Panel CreateSettingRow(string title, string subtitle, Action<Graphics, Rectangle, Color, float> drawIcon, Color iconColor, Control rightControl)
    {
        var row = new Panel
        {
            Width = 660,
            Height = 62,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = UITheme.BgItemCard
        };
        MakeRoundedControl(row, 12);

        row.Paint += (s, e) =>
        {
            UITheme.EnableAntiAlias(e.Graphics);
            using var path = UITheme.GetRoundedPath(new Rectangle(0, 0, row.Width - 1, row.Height - 1), 12);
            using var brush = new SolidBrush(UITheme.BgItemCard);
            e.Graphics.FillPath(brush, path);

            // Icon Badge Box (Left: 38x38)
            var badgeRect = new Rectangle(14, 12, 38, 38);
            using var bPath = UITheme.GetRoundedPath(badgeRect, 8);
            using var bBrush = new SolidBrush(Color.FromArgb(30, iconColor));
            e.Graphics.FillPath(bBrush, bPath);
            drawIcon(e.Graphics, badgeRect, iconColor, 2f);

            // Title & Subtitle
            using var fontTitle = new Font("Segoe UI", 10.5F, FontStyle.Bold);
            using var fontSub = new Font("Segoe UI", 8.5F, FontStyle.Regular);
            using var textBrush = new SolidBrush(UITheme.TextPrimary);
            using var subBrush = new SolidBrush(UITheme.TextSecondary);

            e.Graphics.DrawString(title, fontTitle, textBrush, 64, 14);
            e.Graphics.DrawString(subtitle, fontSub, subBrush, 64, 35);
        };

        rightControl.Location = new Point(row.Width - rightControl.Width - 20, (row.Height - rightControl.Height) / 2);
        rightControl.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        row.Controls.Add(rightControl);

        return row;
    }

    // =============================================================
    // VIEW 5: Deleting Animation (Matches Screen 5)
    // =============================================================
    private void BuildDeletingAnimationView()
    {
        pnlDeletingView = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.BgApp,
            Visible = false
        };

        // Bottom Done Button Bar
        var pnlBottomDeleting = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 65,
            BackColor = UITheme.BgApp,
            Padding = new Padding(0, 10, 0, 0)
        };
        pnlBottomDeleting.Paint += (s, e) =>
        {
            UITheme.EnableAntiAlias(e.Graphics);
            var glowRect = new Rectangle(btnDone.Left, btnDone.Top + 6, btnDone.Width, btnDone.Height);
            using var glowPath = UITheme.GetRoundedPath(glowRect, 14);
            using var glowBrush = new SolidBrush(Color.FromArgb(40, UITheme.AccentRed));
            e.Graphics.FillPath(glowBrush, glowPath);
        };

        btnDone = new Button
        {
            Text = "       Done",
            Size = new Size(pnlBottomDeleting.Width, 52),
            Location = new Point(0, 10),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = UITheme.AccentRed,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11.5F, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Enabled = false
        };
        btnDone.FlatAppearance.BorderSize = 0;
        MakeRoundedControl(btnDone, 12);
        btnDone.Paint += (s, e) =>
        {
            UITheme.EnableAntiAlias(e.Graphics);
            int iconX = (btnDone.Width / 2) - 45;
            int iconY = (btnDone.Height / 2) - 8;
            SvgIcons.DrawTrash(e.Graphics, new Rectangle(iconX, iconY, 16, 16), Color.White);
        };
        btnDone.Click += (s, e) => SwitchTab(AppTab.Delete);
        pnlBottomDeleting.Controls.Add(btnDone);

        // Center Card
        var pnlCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.BgCardContainer,
            Padding = new Padding(16)
        };
        MakeRoundedControl(pnlCard, 16);
        pnlCard.Paint += (s, e) =>
        {
            UITheme.EnableAntiAlias(e.Graphics);
            using var path = UITheme.GetRoundedPath(new Rectangle(0, 0, pnlCard.Width - 1, pnlCard.Height - 1), 16);
            using var brush = new SolidBrush(UITheme.BgCardContainer);
            e.Graphics.FillPath(brush, path);
            using var pen = new Pen(UITheme.BorderSubtle, 1f);
            e.Graphics.DrawPath(pen, path);
        };

        // Top Statistics & Circular Ring Dashboard
        var pnlTopDashboard = new Panel
        {
            Dock = DockStyle.Top,
            Height = 210,
            BackColor = UITheme.BgCardContainer
        };

        // Center Ring Progress
        progressRing = new CircularProgressControl
        {
            Size = new Size(180, 180),
            Location = new Point((680 - 180) / 2, 10)
        };
        pnlTopDashboard.Controls.Add(progressRing);

        // Left Stat: Items Total
        var pnlLeftStat = new Panel
        {
            Size = new Size(130, 95),
            Location = new Point(30, 50),
            BackColor = UITheme.BgItemCard
        };
        MakeRoundedControl(pnlLeftStat, 12);
        pnlLeftStat.Paint += (s, e) =>
        {
            UITheme.EnableAntiAlias(e.Graphics);
            using var path = UITheme.GetRoundedPath(new Rectangle(0, 0, pnlLeftStat.Width - 1, pnlLeftStat.Height - 1), 12);
            using var brush = new SolidBrush(UITheme.BgItemCard);
            e.Graphics.FillPath(brush, path);
            SvgIcons.DrawDocument(e.Graphics, new Rectangle(pnlLeftStat.Width / 2 - 12, 12, 24, 24), Color.FromArgb(200, 215, 235), 2f);
        };
        lblTotalItems = new Label
        {
            Text = "3 items\nin total",
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Bottom,
            Height = 44,
            BackColor = Color.Transparent
        };
        pnlLeftStat.Controls.Add(lblTotalItems);
        pnlTopDashboard.Controls.Add(pnlLeftStat);

        // Right Stat: Total Size
        var pnlRightStat = new Panel
        {
            Size = new Size(130, 95),
            Location = new Point(pnlTopDashboard.Width - 160, 50),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = UITheme.BgItemCard
        };
        MakeRoundedControl(pnlRightStat, 12);
        pnlRightStat.Paint += (s, e) =>
        {
            UITheme.EnableAntiAlias(e.Graphics);
            using var path = UITheme.GetRoundedPath(new Rectangle(0, 0, pnlRightStat.Width - 1, pnlRightStat.Height - 1), 12);
            using var brush = new SolidBrush(UITheme.BgItemCard);
            e.Graphics.FillPath(brush, path);
            SvgIcons.DrawDrive(e.Graphics, new Rectangle(pnlRightStat.Width / 2 - 12, 12, 24, 24), Color.FromArgb(200, 215, 235), 2f);
        };
        lblTotalSize = new Label
        {
            Text = "0 MB\ntotal size",
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Bottom,
            Height = 44,
            BackColor = Color.Transparent
        };
        pnlRightStat.Controls.Add(lblTotalSize);
        pnlTopDashboard.Controls.Add(pnlRightStat);

        // Result Card 1: Successes
        pnlSuccessCard = CreateResultCard(true, "0 items deleted successfully", "0 MB", out lblSuccessText, out lblSuccessSize);
        pnlSuccessCard.Dock = DockStyle.Top;
        MakeRoundedControl(pnlSuccessCard, 12);

        // Result Card 2: Warnings/Failures
        pnlWarningCard = CreateResultCard(false, "0 items could not be deleted", "0 MB", out lblWarningText, out lblWarningSize);
        pnlWarningCard.Dock = DockStyle.Top;
        MakeRoundedControl(pnlWarningCard, 12);

        // Order inside pnlCard: Dock=Top added 1st gets bottom, added last gets top!
        pnlCard.Controls.Add(pnlWarningCard);
        pnlCard.Controls.Add(pnlSuccessCard);
        pnlCard.Controls.Add(pnlTopDashboard);

        // Order inside pnlDeletingView: Dock=Fill first, then Dock=Bottom
        pnlDeletingView.Controls.Add(pnlCard);
        pnlDeletingView.Controls.Add(pnlBottomDeleting);
    }

    private Panel CreateResultCard(bool isSuccess, string defaultText, string defaultSize, out Label outText, out Label outSize)
    {
        var card = new Panel
        {
            Height = 56,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = UITheme.BgItemCard
        };

        var lblT = new Label
        {
            Text = defaultText,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(48, 18),
            AutoSize = true
        };
        var lblS = new Label
        {
            Text = defaultSize,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = UITheme.TextSecondary,
            Location = new Point(card.Width - 120, 18),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            AutoSize = true
        };

        card.Paint += (s, e) =>
        {
            UITheme.EnableAntiAlias(e.Graphics);
            using var path = UITheme.GetRoundedPath(new Rectangle(0, 0, card.Width - 1, card.Height - 1), 10);
            using var brush = new SolidBrush(UITheme.BgItemCard);
            e.Graphics.FillPath(brush, path);

            if (isSuccess)
            {
                using var checkBrush = new SolidBrush(UITheme.DotGreen);
                e.Graphics.FillEllipse(checkBrush, 16, 18, 20, 20);
                SvgIcons.DrawCheck(e.Graphics, new Rectangle(16, 18, 20, 20), Color.White, 2f);
            }
            else
            {
                using var warnBrush = new SolidBrush(UITheme.DotOrange);
                e.Graphics.FillEllipse(warnBrush, 16, 18, 20, 20);
                SvgIcons.DrawAlert(e.Graphics, new Rectangle(16, 18, 20, 20), Color.White, 2f);
            }

            // Down chevron
            SvgIcons.DrawChevronDown(e.Graphics, new Rectangle(card.Width - 36, 18, 20, 20), UITheme.TextMuted, 2f);
        };

        card.Controls.Add(lblT);
        card.Controls.Add(lblS);

        outText = lblT;
        outSize = lblS;
        return card;
    }

    // =============================================================
    // Navigation / Tab Switcher
    // =============================================================
    private void SwitchTab(AppTab tab)
    {
        _currentTab = tab;

        pnlDeleteView.Visible = (tab == AppTab.Delete);
        pnlAddView.Visible = (tab == AppTab.Add);
        pnlHistoryView.Visible = (tab == AppTab.History);
        pnlSettingsView.Visible = (tab == AppTab.Settings);
        pnlDeletingView.Visible = (tab == AppTab.DeletingAnimation);

        lblTitle.Text = tab switch
        {
            AppTab.Delete => "Delete",
            AppTab.Add => "Add",
            AppTab.History => "History",
            AppTab.Settings => "Settings",
            AppTab.DeletingAnimation => "Deleting",
            _ => "Delete"
        };

        // Update active sidebar button styles
        UpdateNavButtonStyle(btnNavDelete, tab == AppTab.Delete || tab == AppTab.DeletingAnimation);
        UpdateNavButtonStyle(btnNavAdd, tab == AppTab.Add);
        UpdateNavButtonStyle(btnNavHistory, tab == AppTab.History);
        UpdateNavButtonStyle(btnNavSettings, tab == AppTab.Settings);
        pnlSidebar.Invalidate();

        if (tab == AppTab.History)
            RefreshHistoryListUI();
    }

    private Button CreateNavButton(Action<Graphics, Rectangle, Color, float> drawIcon, AppTab tab)
    {
        var btn = new Button
        {
            Size = new Size(48, 48),
            FlatStyle = FlatStyle.Flat,
            BackColor = UITheme.BgSidebar,
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 0;
        MakeRoundedControl(btn, 12);

        btn.Paint += (s, e) =>
        {
            UITheme.EnableAntiAlias(e.Graphics);
            bool isActive = (_currentTab == tab || (_currentTab == AppTab.DeletingAnimation && tab == AppTab.Delete));

            using var path = UITheme.GetRoundedPath(new Rectangle(0, 0, btn.Width, btn.Height), 12);
            Color fillCol = isActive ? UITheme.AccentRed : (btn.ClientRectangle.Contains(btn.PointToClient(Cursor.Position)) ? Color.FromArgb(32, 38, 54) : UITheme.BgSidebar);
            using var brush = new SolidBrush(fillCol);
            e.Graphics.FillPath(brush, path);

            Color iconColor = isActive ? Color.White : UITheme.TextSecondary;
            drawIcon(e.Graphics, new Rectangle(0, 0, btn.Width, btn.Height), iconColor, 2f);
        };

        btn.MouseEnter += (s, e) => btn.Invalidate();
        btn.MouseLeave += (s, e) => btn.Invalidate();

        return btn;
    }

    private void UpdateNavButtonStyle(Button btn, bool isActive)
    {
        btn.Invalidate();
    }

    private Button CreateWindowControlButton(string text, Action onClick, Color? hoverColor = null)
    {
        var btn = new Button
        {
            Text = text,
            Size = new Size(32, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = UITheme.TextSecondary,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.MouseEnter += (s, e) => { btn.BackColor = hoverColor ?? Color.FromArgb(34, 40, 56); btn.ForeColor = Color.White; };
        btn.MouseLeave += (s, e) => { btn.BackColor = Color.Transparent; btn.ForeColor = UITheme.TextSecondary; };
        btn.Click += (s, e) => onClick();
        return btn;
    }

    // =============================================================
    // Item List & Card Controls
    // =============================================================
    public void AddTargetItems(IEnumerable<string> paths)
    {
        if (paths == null) return;
        bool addedAny = false;
        var newlyAdded = new List<TargetItem>();

        foreach (var rawPath in paths)
        {
            if (string.IsNullOrWhiteSpace(rawPath)) continue;
            string path = rawPath.Trim('\"', ' ');

            if (!File.Exists(path) && !Directory.Exists(path))
                continue;

            if (_items.Any(i => i.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                continue;

            var item = new TargetItem(path);
            _items.Add(item);
            newlyAdded.Add(item);
            addedAny = true;
        }

        if (addedAny)
        {
            RefreshItemListUI();

            // Check locks in parallel for newly added items
            Task.Run(() =>
            {
                Parallel.ForEach(newlyAdded, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount * 2, 4, 16) }, item =>
                {
                    var locks = ProcessUnlocker.FindLockingProcesses(item.FullPath);
                    item.LockingProcesses = locks;
                    item.Status = locks.Count > 0 ? ItemLockStatus.Locked : ItemLockStatus.Ready;
                    this.BeginInvoke(new Action(() => UpdateItemRowUI(item)));
                });
            });
        }
    }

    public void AddTargetItem(string path)
    {
        AddTargetItems(new[] { path });
    }

    private void ClearItems()
    {
        _items.Clear();
        RefreshItemListUI();
    }

    private void RemoveItem(TargetItem item)
    {
        _items.Remove(item);
        RefreshItemListUI();
    }

    private void RefreshItemListUI()
    {
        pnlItemList.SuspendLayout();
        pnlItemList.Controls.Clear();

        if (_items.Count == 0)
        {
            lblEmptyHint.Visible = true;
            pnlItemList.Visible = false;
            btnDelete.Enabled = false;
            btnDelete.Text = "       Delete";
            btnClear.Enabled = false;
        }
        else
        {
            lblEmptyHint.Visible = false;
            pnlItemList.Visible = true;
            btnDelete.Enabled = true;
            btnDelete.Text = _items.Count > 1 ? $"       Delete All ({_items.Count})" : "       Delete";
            btnClear.Enabled = true;

            foreach (var item in _items)
            {
                var card = CreateItemCardControl(item);
                pnlItemList.Controls.Add(card);
            }
        }

        pnlItemList.ResumeLayout();
        btnDelete.Invalidate();
        UpdateRecentPreviewCards();
    }

    private void UpdateRecentPreviewCards()
    {
        if (pnlRecentPreview == null) return;
        pnlRecentPreview.SuspendLayout();
        pnlRecentPreview.Controls.Clear();

        foreach (var item in _items)
        {
            var chip = new Panel
            {
                Size = new Size(185, 46),
                Margin = new Padding(0, 0, 10, 8),
                BackColor = UITheme.BgItemCard
            };
            MakeRoundedControl(chip, 10);
            chip.Paint += (s, e) =>
            {
                UITheme.EnableAntiAlias(e.Graphics);
                using var path = UITheme.GetRoundedPath(new Rectangle(0, 0, chip.Width - 1, chip.Height - 1), 10);
                using var brush = new SolidBrush(UITheme.BgItemCard);
                e.Graphics.FillPath(brush, path);
                using var pen = new Pen(UITheme.BorderSubtle, 1f);
                e.Graphics.DrawPath(pen, path);

                var badgeRect = new Rectangle(8, 7, 32, 32);
                using var bPath = UITheme.GetRoundedPath(badgeRect, 6);
                using var bBrush = new SolidBrush(Color.FromArgb(36, 44, 62));
                e.Graphics.FillPath(bBrush, bPath);

                if (item.IsDirectory)
                    SvgIcons.DrawFolder(e.Graphics, badgeRect, UITheme.AccentBlue, 2f, true);
                else if (item.FileType == TargetFileType.Image)
                    SvgIcons.DrawImage(e.Graphics, badgeRect, UITheme.AccentPurple, 2f);
                else
                    SvgIcons.DrawDocument(e.Graphics, badgeRect, Color.White, 2f);

                using var font = new Font("Segoe UI", 9F, FontStyle.Bold);
                using var textBrush = new SolidBrush(UITheme.TextPrimary);
                var sf = new StringFormat { Trimming = StringTrimming.EllipsisPath, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center };
                e.Graphics.DrawString(item.Name, font, textBrush, new Rectangle(46, 6, chip.Width - 78, 34), sf);
            };

            var btnX = new Button
            {
                Text = "✕",
                Size = new Size(22, 22),
                Location = new Point(chip.Width - 28, 12),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(42, 50, 70),
                ForeColor = UITheme.TextSecondary,
                Font = new Font("Segoe UI", 7F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnX.FlatAppearance.BorderSize = 0;
            MakeRoundedControl(btnX, 11);
            btnX.Click += (s, e) => RemoveItem(item);
            chip.Controls.Add(btnX);

            pnlRecentPreview.Controls.Add(chip);
        }

        pnlRecentPreview.ResumeLayout();
    }

    private Panel CreateItemCardControl(TargetItem item)
    {
        int cardWidth = pnlCardContainer.ClientSize.Width - 30;
        if (cardWidth < 300) cardWidth = 660;

        var pnlCard = new Panel
        {
            Width = cardWidth,
            Height = 62,
            Margin = new Padding(0, 0, 0, 10),
            BackColor = UITheme.BgItemCard,
            Tag = item
        };

        pnlCard.Paint += (s, e) =>
        {
            UITheme.EnableAntiAlias(e.Graphics);
            using var path = UITheme.GetRoundedPath(new Rectangle(0, 0, pnlCard.Width - 1, pnlCard.Height - 1), 12);
            using var brush = new SolidBrush(UITheme.BgItemCard);
            e.Graphics.FillPath(brush, path);

            // Icon Badge Box (Left: 38x38)
            var badgeRect = new Rectangle(14, 12, 38, 38);
            using var badgePath = UITheme.GetRoundedPath(badgeRect, 8);

            Color badgeBg = item.FileType switch
            {
                TargetFileType.Folder => Color.FromArgb(30, 42, 68),
                TargetFileType.Image => Color.FromArgb(44, 32, 66),
                TargetFileType.Archive => Color.FromArgb(50, 44, 25),
                _ => Color.FromArgb(42, 48, 66)
            };
            Color iconColor = item.FileType switch
            {
                TargetFileType.Folder => UITheme.AccentBlue,
                TargetFileType.Image => UITheme.AccentPurple,
                TargetFileType.Archive => Color.FromArgb(245, 158, 11),
                _ => Color.FromArgb(241, 245, 249)
            };

            using var badgeBrush = new SolidBrush(badgeBg);
            e.Graphics.FillPath(badgeBrush, badgePath);

            // Draw clean icon
            if (item.FileType == TargetFileType.Folder)
                SvgIcons.DrawFolder(e.Graphics, badgeRect, iconColor, 2f, filled: true);
            else if (item.FileType == TargetFileType.Image)
                SvgIcons.DrawImage(e.Graphics, badgeRect, iconColor, 2f);
            else if (item.FileType == TargetFileType.Archive)
                SvgIcons.DrawArchive(e.Graphics, badgeRect, iconColor, 2f);
            else
                SvgIcons.DrawDocument(e.Graphics, badgeRect, iconColor, 2f);

            // File Name Text
            using var textBrush = new SolidBrush(UITheme.TextPrimary);
            using var font = new Font("Segoe UI", 10.5F, FontStyle.Regular);
            var textRect = new Rectangle(66, 18, pnlCard.Width - 140, 26);
            var sf = new StringFormat
            {
                Trimming = StringTrimming.EllipsisPath,
                FormatFlags = StringFormatFlags.NoWrap,
                LineAlignment = StringAlignment.Center
            };
            e.Graphics.DrawString(item.Name, font, textBrush, textRect, sf);

            // Status Indicator Dot (Green / Orange)
            Color dotColor = item.Status switch
            {
                ItemLockStatus.Locked => UITheme.DotOrange,
                ItemLockStatus.Protected => UITheme.DotRed,
                _ => UITheme.DotGreen
            };

            int dotX = pnlCard.Width - 65;
            int dotY = (pnlCard.Height / 2) - 4;
            using var dotBrush = new SolidBrush(dotColor);
            e.Graphics.FillEllipse(dotBrush, dotX, dotY, 9, 9);
        };

        // Round Close button (X) on right
        var btnRemove = new Button
        {
            Size = new Size(30, 30),
            Location = new Point(pnlCard.Width - 42, 16),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(44, 52, 72),
            Cursor = Cursors.Hand
        };
        btnRemove.FlatAppearance.BorderSize = 0;
        btnRemove.Paint += (s, e) =>
        {
            UITheme.EnableAntiAlias(e.Graphics);
            using var path = UITheme.GetRoundedPath(new Rectangle(0, 0, btnRemove.Width, btnRemove.Height), 15);
            using var brush = new SolidBrush(btnRemove.BackColor);
            e.Graphics.FillPath(brush, path);
            SvgIcons.DrawClose(e.Graphics, new Rectangle(0, 0, btnRemove.Width, btnRemove.Height), UITheme.TextSecondary, 1.8f);
        };
        btnRemove.MouseEnter += (s, e) => { btnRemove.BackColor = Color.FromArgb(60, 70, 96); btnRemove.Invalidate(); };
        btnRemove.MouseLeave += (s, e) => { btnRemove.BackColor = Color.FromArgb(44, 52, 72); btnRemove.Invalidate(); };
        btnRemove.Click += (s, e) => RemoveItem(item);

        pnlCard.Controls.Add(btnRemove);
        return pnlCard;
    }

    private void UpdateItemRowUI(TargetItem item)
    {
        foreach (Control c in pnlItemList.Controls)
        {
            if (c.Tag == item)
            {
                c.Invalidate();
                break;
            }
        }
    }

    // =============================================================
    // VIEW 5 & Execution: Force Delete Process with Animation
    // =============================================================
    private async void StartForceDeleteProcess(object? sender, EventArgs e)
    {
        if (_items.Count == 0)
        {
            MessageBox.Show(this, "삭제할 파일이나 폴더를 목록에 먼저 추가하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (swConfirmPrompt.Checked)
        {
            var confirm = MessageBox.Show(this,
                $"목록에 있는 {_items.Count}개의 항목을 권한과 파일종류에 상관없이 무조건 영구 삭제하시겠습니까?\n\n(휴지통으로 가지 않으며 즉시 완전 삭제됩니다!)",
                "무조건 강제 삭제 확인",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes)
                return;
        }

        // Switch to Deleting Animation View (Screen 5)
        SwitchTab(AppTab.DeletingAnimation);

        long totalBytes = _items.Sum(i => i.FileSizeBytes);
        lblTotalItems.Text = $"{_items.Count} items\nin total";
        lblTotalSize.Text = $"{FormatSize(totalBytes)}\ntotal size";
        btnDone.Enabled = false;

        progressRing.Progress = 0f;
        progressRing.StatusText = "Initializing...";

        int totalCount = _items.Count;
        int successCount = 0;
        int failedCount = 0;
        long successBytes = 0;
        long failedBytes = 0;

        var itemsToProcess = new List<TargetItem>(_items);

        await Task.Run(() =>
        {
            int completedCount = 0;
            int maxDegree = swTurboMode.Checked ? Math.Clamp(Environment.ProcessorCount * 2, 4, 16) : 1;
            var pOptions = new ParallelOptions { MaxDegreeOfParallelism = maxDegree };

            Parallel.ForEach(itemsToProcess, pOptions, item =>
            {
                var res = ForceDeleteEngine.ForceDelete(
                    item.FullPath, 
                    killLockingProcesses: swRemoveLocked.Checked, 
                    secureWipe: swSecureDelete.Checked, 
                    turboMode: swTurboMode.Checked, 
                    log: null);

                bool ok = (res.Status == DeleteResultStatus.Success || res.Status == DeleteResultStatus.ScheduledOnReboot);

                if (ok)
                {
                    Interlocked.Increment(ref successCount);
                    Interlocked.Add(ref successBytes, item.FileSizeBytes);
                }
                else
                {
                    Interlocked.Increment(ref failedCount);
                    Interlocked.Add(ref failedBytes, item.FileSizeBytes);
                }

                // Add to history (thread-safe)
                lock (_history)
                {
                    _history.Insert(0, new HistoryRecord
                    {
                        Name = item.Name,
                        FullPath = item.FullPath,
                        IsDirectory = item.IsDirectory,
                        FileType = item.FileType,
                        FileCount = 1,
                        TotalSizeBytes = item.FileSizeBytes,
                        IsSuccessful = ok
                    });
                }

                int currentDone = Interlocked.Increment(ref completedCount);
                float pct = ((float)currentDone / totalCount) * 100f;

                this.BeginInvoke(new Action(() =>
                {
                    progressRing.StatusText = totalCount > 1 
                        ? $"Deleting ({currentDone}/{totalCount}) {item.Name}..."
                        : $"Deleting {item.Name}...";
                    progressRing.Progress = pct;
                }));
            });
        });

        // Finished state update
        progressRing.Progress = 100f;
        progressRing.StatusText = "Completed";

        lblSuccessText.Text = $"{successCount} items deleted successfully";
        lblSuccessSize.Text = FormatSize(successBytes);
        lblWarningText.Text = $"{failedCount} items could not be deleted";
        lblWarningSize.Text = FormatSize(failedBytes);

        btnDone.Enabled = true;

        // Clear removed items from the main list
        _items.RemoveAll(i => !File.Exists(i.FullPath) && !Directory.Exists(i.FullPath));
        RefreshItemListUI();
    }

    // =============================================================
    // Dialog Prompts & History
    // =============================================================
    private void PromptAddFiles()
    {
        using var ofd = new OpenFileDialog
        {
            Title = "삭제할 파일 선택 (다중 파일 동시 선택 가능 - Ctrl/Shift 키)",
            Multiselect = true,
            Filter = "모든 파일 (*.*)|*.*"
        };
        if (ofd.ShowDialog(this) == DialogResult.OK)
        {
            AddTargetItems(ofd.FileNames);
            SwitchTab(AppTab.Delete);
        }
    }

    private void PromptAddFolder()
    {
        using var fbd = new FolderBrowserDialog
        {
            Description = "강제 삭제할 폴더 선택 (내부 모든 파일/폴더가 강제 삭제 대상에 등록됩니다)",
            UseDescriptionForTitle = true
        };
        if (fbd.ShowDialog(this) == DialogResult.OK)
        {
            AddTargetItem(fbd.SelectedPath);
            SwitchTab(AppTab.Delete);
        }
    }

    private void LoadSampleHistory()
    {
        // Add sample demo history records matching Screen 3
        _history.Add(new HistoryRecord { Name = "notes.txt", Timestamp = DateTime.Now.AddDays(-1), FileType = TargetFileType.Document, TotalSizeBytes = 12 * 1024, IsSuccessful = true });
        _history.Add(new HistoryRecord { Name = "photos", Timestamp = DateTime.Now.AddDays(-2), IsDirectory = true, FileType = TargetFileType.Folder, FileCount = 128, TotalSizeBytes = 2400L * 1024 * 1024, IsSuccessful = true });
        _history.Add(new HistoryRecord { Name = "image.png", Timestamp = DateTime.Now.AddDays(-4), FileType = TargetFileType.Image, TotalSizeBytes = 450 * 1024, IsSuccessful = true });
        _history.Add(new HistoryRecord { Name = "old-project", Timestamp = DateTime.Now.AddDays(-6), IsDirectory = true, FileType = TargetFileType.Folder, FileCount = 342, TotalSizeBytes = 1100L * 1024 * 1024, IsSuccessful = false });
        _history.Add(new HistoryRecord { Name = "temp.log", Timestamp = DateTime.Now.AddDays(-8), FileType = TargetFileType.Document, TotalSizeBytes = 85 * 1024, IsSuccessful = true });
    }

    private void RefreshHistoryListUI()
    {
        if (pnlHistoryList == null) return;
        pnlHistoryList.SuspendLayout();
        pnlHistoryList.Controls.Clear();

        string query = txtSearchHistory?.Text.Trim().ToLowerInvariant() ?? "";

        var filtered = _history.Where(h =>
        {
            if (!string.IsNullOrEmpty(query) && !h.Name.ToLowerInvariant().Contains(query))
                return false;

            return _historyFilter switch
            {
                "Files" => !h.IsDirectory,
                "Folders" => h.IsDirectory,
                "Successful" => h.IsSuccessful,
                "With Warnings" => !h.IsSuccessful,
                _ => true
            };
        }).ToList();

        foreach (var rec in filtered)
        {
            pnlHistoryList.Controls.Add(CreateHistoryRowCard(rec));
        }

        pnlHistoryList.ResumeLayout();
    }

    private Panel CreateHistoryRowCard(HistoryRecord rec)
    {
        int cardWidth = pnlHistoryList.ClientSize.Width - 25;
        if (cardWidth < 300) cardWidth = 660;

        var row = new Panel
        {
            Width = cardWidth,
            Height = 60,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = UITheme.BgItemCard
        };

        row.Paint += (s, e) =>
        {
            UITheme.EnableAntiAlias(e.Graphics);
            using var path = UITheme.GetRoundedPath(new Rectangle(0, 0, row.Width - 1, row.Height - 1), 12);
            using var brush = new SolidBrush(UITheme.BgItemCard);
            e.Graphics.FillPath(brush, path);

            // Icon Badge Box (Left: 36x36)
            var badgeRect = new Rectangle(14, 12, 36, 36);
            using var bPath = UITheme.GetRoundedPath(badgeRect, 8);
            using var bBrush = new SolidBrush(Color.FromArgb(40, 48, 66));
            e.Graphics.FillPath(bBrush, bPath);

            if (rec.IsDirectory)
                SvgIcons.DrawFolder(e.Graphics, badgeRect, UITheme.AccentBlue, 2f, true);
            else if (rec.FileType == TargetFileType.Image)
                SvgIcons.DrawImage(e.Graphics, badgeRect, UITheme.AccentPurple, 2f);
            else
                SvgIcons.DrawDocument(e.Graphics, badgeRect, Color.White, 2f);

            // Date & Time (Column 1)
            using var fontBold = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            using var fontSub = new Font("Segoe UI", 8F, FontStyle.Regular);
            using var brushWhite = new SolidBrush(Color.White);
            using var brushMuted = new SolidBrush(UITheme.TextSecondary);

            e.Graphics.DrawString(rec.Timestamp.ToString("MMM dd, yyyy"), fontBold, brushWhite, 64, 14);
            e.Graphics.DrawString(rec.Timestamp.ToString("hh:mm tt"), fontSub, brushMuted, 64, 34);

            // File Name & Details (Column 2)
            e.Graphics.DrawString(rec.Name, fontBold, brushWhite, 220, 14);
            string detailStr = rec.IsDirectory
                ? $"{rec.FileCount} files • {FormatSize(rec.TotalSizeBytes)}"
                : $"{rec.FileCount} file • {FormatSize(rec.TotalSizeBytes)}";
            e.Graphics.DrawString(detailStr, fontSub, brushMuted, 220, 34);

            // Status Badge (Green / Orange)
            Color statusCol = rec.IsSuccessful ? UITheme.DotGreen : UITheme.DotOrange;
            using var dotBrush = new SolidBrush(statusCol);
            int dotX = row.Width - 150;
            e.Graphics.FillEllipse(dotBrush, dotX, 26, 8, 8);
            e.Graphics.DrawString(rec.StatusText, fontBold, brushWhite, dotX + 14, 21);

            // Right More (...) Icon
            int mx = row.Width - 40;
            e.Graphics.FillEllipse(brushMuted, mx, 28, 3, 3);
            e.Graphics.FillEllipse(brushMuted, mx + 6, 28, 3, 3);
            e.Graphics.FillEllipse(brushMuted, mx + 12, 28, 3, 3);
        };

        return row;
    }

    private void ExportHistory()
    {
        using var sfd = new SaveFileDialog
        {
            Title = "히스토리 내보내기",
            Filter = "텍스트 파일 (*.txt)|*.txt|CSV 파일 (*.csv)|*.csv",
            FileName = $"ForceDeleter_History_{DateTime.Now:yyyyMMdd_HHmm}.txt"
        };
        if (sfd.ShowDialog(this) == DialogResult.OK)
        {
            var lines = _history.Select(h => $"{h.Timestamp:yyyy-MM-dd HH:mm:ss}\t{h.Name}\t{h.StatusText}\t{FormatSize(h.TotalSizeBytes)}");
            File.WriteAllLines(sfd.FileName, lines);
            MessageBox.Show(this, "성공적으로 내보냈습니다.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private string FormatSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }

    private void WindowDrag_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(this.Handle, NativeMethods.WM_NCLBUTTONDOWN, (IntPtr)NativeMethods.HT_CAPTION, IntPtr.Zero);
        }
    }

    private void MainForm_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
    }

    private void MainForm_DragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                AddTargetItems(files);
                SwitchTab(AppTab.Delete);
            }
        }
    }
}
