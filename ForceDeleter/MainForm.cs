using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ForceDeleter;

public partial class MainForm : Form
{
    private TextBox txtPath = null!;
    private Button btnBrowseFile = null!;
    private Button btnBrowseFolder = null!;
    private Button btnCheckLocks = null!;
    private Button btnKillLocks = null!;
    private Button btnForceDelete = null!;
    private Button btnContextMenu = null!;
    private CheckBox chkKillLocks = null!;
    private CheckBox chkElevateTI = null!;
    private ListView lvLocks = null!;
    private RichTextBox rtbLog = null!;
    private Label lblStatus = null!;
    private ProgressBar progressBar = null!;

    public MainForm(string? initialPath = null)
    {
        InitializeComponent();
        BuildUI();

        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            SetTargetPath(initialPath);
            _ = CheckLocksAsync();
        }
    }

    private void BuildUI()
    {
        // -------------------------------------------------------------
        // Header Banner
        // -------------------------------------------------------------
        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 65,
            BackColor = Color.FromArgb(30, 41, 59),
            Padding = new Padding(16, 12, 16, 12)
        };

        var lblTitle = new Label
        {
            Text = "Universal Force Deleter (무조건 강제 삭제기)",
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(16, 10)
        };

        var lblSubtitle = new Label
        {
            Text = "NanaRun (TrustedInstaller) + ForceDelete (RestartManager/Handles) + DeadLock (ACL/Ownership)",
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = Color.FromArgb(203, 213, 225),
            AutoSize = true,
            Location = new Point(17, 34)
        };

        pnlHeader.Controls.Add(lblTitle);
        pnlHeader.Controls.Add(lblSubtitle);
        this.Controls.Add(pnlHeader);

        // -------------------------------------------------------------
        // Drop & Input Area (Top)
        // -------------------------------------------------------------
        var grpTarget = new GroupBox
        {
            Text = "대상 파일 / 폴더 선택 (파일을 창에 끌어다 놓으셔도 됩니다)",
            Location = new Point(16, 75),
            Size = new Size(712, 105),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };

        txtPath = new TextBox
        {
            Location = new Point(16, 26),
            Size = new Size(490, 26),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular)
        };
        txtPath.TextChanged += (s, e) => UpdateTargetInfo();

        btnBrowseFile = new Button
        {
            Text = "파일 선택...",
            Location = new Point(516, 24),
            Size = new Size(88, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Font = new Font("Segoe UI", 8.5F)
        };
        btnBrowseFile.Click += BtnBrowseFile_Click;

        btnBrowseFolder = new Button
        {
            Text = "폴더 선택...",
            Location = new Point(610, 24),
            Size = new Size(88, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Font = new Font("Segoe UI", 8.5F)
        };
        btnBrowseFolder.Click += BtnBrowseFolder_Click;

        lblStatus = new Label
        {
            Text = "선택된 대상 없음",
            Location = new Point(16, 62),
            Size = new Size(680, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = Color.FromArgb(71, 85, 105),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular)
        };

        grpTarget.Controls.Add(txtPath);
        grpTarget.Controls.Add(btnBrowseFile);
        grpTarget.Controls.Add(btnBrowseFolder);
        grpTarget.Controls.Add(lblStatus);
        this.Controls.Add(grpTarget);

        // -------------------------------------------------------------
        // Locks & Options Area (Middle)
        // -------------------------------------------------------------
        var grpLocks = new GroupBox
        {
            Text = "파일 잠금 상태 및 실행 옵션 (Restart Manager)",
            Location = new Point(16, 190),
            Size = new Size(712, 175),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };

        lvLocks = new ListView
        {
            Location = new Point(16, 26),
            Size = new Size(540, 105),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular)
        };
        lvLocks.Columns.Add("PID", 60);
        lvLocks.Columns.Add("프로세스 이름", 140);
        lvLocks.Columns.Add("서비스 / 설명", 180);
        lvLocks.Columns.Add("유형", 120);

        btnCheckLocks = new Button
        {
            Text = "잠금 확인",
            Location = new Point(566, 25),
            Size = new Size(130, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Font = new Font("Segoe UI", 8.5F)
        };
        btnCheckLocks.Click += async (s, e) => await CheckLocksAsync();

        btnKillLocks = new Button
        {
            Text = "잠금 프로세스 종료",
            Location = new Point(566, 62),
            Size = new Size(130, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            ForeColor = Color.FromArgb(185, 28, 28),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold)
        };
        btnKillLocks.Click += BtnKillLocks_Click;

        chkKillLocks = new CheckBox
        {
            Text = "삭제 시 잠금 프로세스 자동 종료",
            Location = new Point(16, 142),
            Size = new Size(240, 22),
            Checked = true,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular)
        };

        chkElevateTI = new CheckBox
        {
            Text = "TrustedInstaller / SYSTEM 권한 상승 (NanaRun)",
            Location = new Point(270, 142),
            Size = new Size(320, 22),
            Checked = true,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular)
        };

        grpLocks.Controls.Add(lvLocks);
        grpLocks.Controls.Add(btnCheckLocks);
        grpLocks.Controls.Add(btnKillLocks);
        grpLocks.Controls.Add(chkKillLocks);
        grpLocks.Controls.Add(chkElevateTI);
        this.Controls.Add(grpLocks);

        // -------------------------------------------------------------
        // Main Action Button Area
        // -------------------------------------------------------------
        btnForceDelete = new Button
        {
            Text = "🔥 무조건 강제 삭제 (Force Delete Everything)",
            Location = new Point(16, 375),
            Size = new Size(500, 48),
            BackColor = Color.FromArgb(220, 38, 38),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        btnForceDelete.FlatAppearance.BorderSize = 0;
        btnForceDelete.Click += BtnForceDelete_Click;
        this.Controls.Add(btnForceDelete);

        btnContextMenu = new Button
        {
            Text = ContextMenuHelper.IsContextMenuInstalled() ? "탐색기 우클릭 메뉴 제거" : "탐색기 우클릭 메뉴 등록",
            Location = new Point(526, 375),
            Size = new Size(202, 48),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Font = new Font("Segoe UI", 9F)
        };
        btnContextMenu.Click += BtnContextMenu_Click;
        this.Controls.Add(btnContextMenu);

        // -------------------------------------------------------------
        // Progress Bar
        // -------------------------------------------------------------
        progressBar = new ProgressBar
        {
            Location = new Point(16, 432),
            Size = new Size(712, 10),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Style = ProgressBarStyle.Continuous,
            Visible = false
        };
        this.Controls.Add(progressBar);

        // -------------------------------------------------------------
        // Real-Time Log Viewer Area (Bottom)
        // -------------------------------------------------------------
        var grpLog = new GroupBox
        {
            Text = "실시간 작업 로그 (Multi-Tier Deletion Log)",
            Location = new Point(16, 450),
            Size = new Size(712, 180),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };

        rtbLog = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.FromArgb(241, 245, 249),
            Font = new Font("Consolas", 9F),
            BorderStyle = BorderStyle.None
        };
        grpLog.Controls.Add(rtbLog);
        this.Controls.Add(grpLog);

        // Setup Drag & Drop
        this.DragEnter += MainForm_DragEnter;
        this.DragDrop += MainForm_DragDrop;

        Log("[i] 프로그램 초기화 완료.");
        Log("[i] 관리자 권한 및 특권(SeDebug, SeTakeOwnership 등) 자동 활성화 준비 완료.");
        Log("[i] 삭제할 파일이나 폴더를 선택하거나 창 위로 드래그 앤 드롭하세요.");
    }

    private void Log(string message)
    {
        if (rtbLog.InvokeRequired)
        {
            rtbLog.BeginInvoke(new Action(() => Log(message)));
            return;
        }

        // Limit lines to prevent UI lag on massive directory deletions
        if (rtbLog.Lines.Length > 1000)
        {
            string[] lines = rtbLog.Lines;
            var newLines = new string[700];
            Array.Copy(lines, 300, newLines, 0, 700);
            rtbLog.Lines = newLines;
        }

        rtbLog.AppendText(message + Environment.NewLine);
        rtbLog.SelectionStart = rtbLog.TextLength;
        rtbLog.ScrollToCaret();
    }

    private void SetTargetPath(string path)
    {
        txtPath.Text = path.Trim('\"', ' ');
        UpdateTargetInfo();
    }

    private void UpdateTargetInfo()
    {
        string path = txtPath.Text.Trim();
        if (string.IsNullOrEmpty(path))
        {
            lblStatus.Text = "선택된 대상 없음";
            lblStatus.ForeColor = Color.FromArgb(71, 85, 105);
            return;
        }

        if (File.Exists(path))
        {
            try
            {
                var fi = new FileInfo(path);
                lblStatus.Text = $"[파일] 크기: {FormatSize(fi.Length)} | 속성: {fi.Attributes} | 마지막 수정: {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}";
                lblStatus.ForeColor = Color.FromArgb(30, 41, 59);
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"[파일] 정보 조회 오류: {ex.Message}";
            }
        }
        else if (Directory.Exists(path))
        {
            try
            {
                var di = new DirectoryInfo(path);
                lblStatus.Text = $"[폴더] 하위 파일/폴더 재귀 삭제 대상 | 속성: {di.Attributes}";
                lblStatus.ForeColor = Color.FromArgb(30, 41, 59);
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"[폴더] 정보 조회 오류: {ex.Message}";
            }
        }
        else
        {
            lblStatus.Text = "경로가 존재하지 않거나 접근할 수 없습니다.";
            lblStatus.ForeColor = Color.FromArgb(185, 28, 28);
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

    private void BtnBrowseFile_Click(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Title = "강제 삭제할 파일 선택",
            CheckFileExists = false,
            CheckPathExists = true,
            Filter = "모든 파일 (*.*)|*.*"
        };

        if (ofd.ShowDialog(this) == DialogResult.OK)
        {
            SetTargetPath(ofd.FileName);
            _ = CheckLocksAsync();
        }
    }

    private void BtnBrowseFolder_Click(object? sender, EventArgs e)
    {
        using var fbd = new FolderBrowserDialog
        {
            Description = "강제 삭제할 폴더 선택 (내부 모든 파일/폴더가 강제 삭제됩니다)",
            UseDescriptionForTitle = true
        };

        if (fbd.ShowDialog(this) == DialogResult.OK)
        {
            SetTargetPath(fbd.SelectedPath);
            _ = CheckLocksAsync();
        }
    }

    private void MainForm_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            e.Effect = DragDropEffects.Copy;
        }
    }

    private void MainForm_DragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                SetTargetPath(files[0]);
                _ = CheckLocksAsync();
            }
        }
    }

    private async Task CheckLocksAsync()
    {
        string path = txtPath.Text.Trim();
        if (string.IsNullOrEmpty(path) || (!File.Exists(path) && !Directory.Exists(path)))
        {
            return;
        }

        btnCheckLocks.Enabled = false;
        lvLocks.Items.Clear();
        Log($"\n[🔍] 파일 잠금 프로세스 검색 중: {Path.GetFileName(path)}...");

        var locks = await Task.Run(() => ProcessUnlocker.FindLockingProcesses(path, Log));

        lvLocks.Items.Clear();
        if (locks.Count == 0)
        {
            Log("[✓] 점유 중인 프로세스가 없습니다. (자유 상태)");
        }
        else
        {
            Log($"[!] {locks.Count}개의 잠금 프로세스 발견!");
            foreach (var l in locks)
            {
                var item = new ListViewItem(l.ProcessId.ToString());
                item.SubItems.Add(l.Name);
                item.SubItems.Add(l.Description);
                item.SubItems.Add(l.AppType);
                lvLocks.Items.Add(item);
                Log($"  -> PID: {l.ProcessId} | 이름: {l.Name} | 유형: {l.AppType}");
            }
        }

        btnCheckLocks.Enabled = true;
    }

    private void BtnKillLocks_Click(object? sender, EventArgs e)
    {
        string path = txtPath.Text.Trim();
        if (string.IsNullOrEmpty(path))
            return;

        Log("\n[x] 잠금 프로세스 강제 종료 및 핸들 해제 시작...");
        Task.Run(() =>
        {
            int handles = ProcessUnlocker.CloseFileHandles(path, Log);
            int killed = ProcessUnlocker.KillLockingProcesses(path, Log);
            this.Invoke(new Action(async () =>
            {
                MessageBox.Show(this,
                    $"완료:\n- 해제된 핸들: {handles}개\n- 종료된 프로세스: {killed}개",
                    "잠금 해제 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
                await CheckLocksAsync();
            }));
        });
    }

    private async void BtnForceDelete_Click(object? sender, EventArgs e)
    {
        string path = txtPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
        {
            MessageBox.Show(this, "삭제할 유효한 파일이나 폴더를 먼저 선택하세요.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var confirm = MessageBox.Show(this,
            $"다음 대상을 모든 권한 및 잠금을 무시하고 강제로 영구 삭제하시겠습니까?\n(휴지통으로 가지 않으며 즉시 완전 삭제됩니다!)\n\n대상:\n{path}",
            "무조건 강제 삭제 경고",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (confirm != DialogResult.Yes)
            return;

        btnForceDelete.Enabled = false;
        btnBrowseFile.Enabled = false;
        btnBrowseFolder.Enabled = false;
        progressBar.Style = ProgressBarStyle.Marquee;
        progressBar.MarqueeAnimationSpeed = 30;
        progressBar.Visible = true;
        bool killLocks = chkKillLocks.Checked;

        Log("\n=======================================================");
        Log("         무조건 강제 삭제 파이프라인 가동");
        Log("=======================================================");

        var result = await Task.Run(() => ForceDeleteEngine.ForceDelete(path, killLocks, Log));

        progressBar.Visible = false;
        btnForceDelete.Enabled = true;
        btnBrowseFile.Enabled = true;
        btnBrowseFolder.Enabled = true;

        if (result.Status == DeleteResultStatus.Success)
        {
            MessageBox.Show(this,
                $"삭제 완료!\n\n성공 단계: Tier {result.Tier}\n메시지: {result.Message}",
                "삭제 성공",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            txtPath.Clear();
            lvLocks.Items.Clear();
            UpdateTargetInfo();
        }
        else if (result.Status == DeleteResultStatus.ScheduledOnReboot)
        {
            MessageBox.Show(this,
                $"해당 파일은 시스템 핵심 커널에 의해 물리적으로 잠겨 있습니다.\n\n안전하게 [다음 Windows 재부팅 시 삭제]되도록 시스템 레지스트리에 예약 등록되었습니다.\n재부팅하면 자동으로 영구 삭제됩니다.",
                "재부팅 시 삭제 예약됨",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        else
        {
            MessageBox.Show(this,
                $"삭제 실패:\n{result.Message}\n\n상세한 로그를 확인해주세요.",
                "삭제 실패",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void BtnContextMenu_Click(object? sender, EventArgs e)
    {
        if (ContextMenuHelper.IsContextMenuInstalled())
        {
            if (ContextMenuHelper.UninstallContextMenu(Log))
            {
                btnContextMenu.Text = "탐색기 우클릭 메뉴 등록";
                MessageBox.Show(this, "탐색기 우클릭 메뉴가 성공적으로 제거되었습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        else
        {
            if (ContextMenuHelper.InstallContextMenu(Log))
            {
                btnContextMenu.Text = "탐색기 우클릭 메뉴 제거";
                MessageBox.Show(this, "탐색기 우클릭 메뉴가 등록되었습니다!\n이제 파일이나 폴더를 우클릭하여 바로 삭제할 수 있습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
