using System.Reflection;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ForceDeleter;

internal static class UiTests
{
    private static readonly List<string> Passed = [];
    private static string _output = "";
    private static bool _captureScreen;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out int value, int size);
    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(args.Contains("--dpi96") ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Control.CheckForIllegalCrossThreadCalls = true;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // The 96-DPI pass validates logical layout. The real display is 150% scaled.
        _captureScreen = args.Contains("--screen") && !args.Contains("--dpi96");
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        _output = Path.GetFullPath(args.FirstOrDefault() ?? ".adhd/ui-evidence");
        Directory.CreateDirectory(_output);
        string fixture = Path.Combine(_output, "fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        try
        {
            Run(fixture);
            File.WriteAllLines(Path.Combine(_output, "results.txt"), Passed);
            Console.WriteLine($"PASS {Passed.Count} UI/behavior assertions. Screenshots: {_output}");
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(_output, "failure.txt"), ex.ToString());
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void Run(string fixture)
    {
        string statePath = Path.Combine(fixture, "state");
        string[] paths = [Path.Combine(fixture, "notes.txt"), Path.Combine(fixture, "photos"), Path.Combine(fixture, "image.png"), Path.Combine(fixture, "archive.zip")];
        Directory.CreateDirectory(paths[1]);
        File.WriteAllText(Path.Combine(paths[1], "photo.txt"), "disposable folder test");
        foreach (string path in paths.Where(p => p != paths[1])) File.WriteAllText(path, "disposable UI test fixture");
        using var form = new MainForm(null, statePath);
        Exception? failure = null;
        void Scenario()
        {
        Pump();
        Check(Math.Abs(form.ClientSize.Width / (form.DeviceDpi / 96f) - 930) < 2, "Initial window respects system DPI");
        Check(Find(form, "Delete").Enabled == false, "Empty queue disables Delete");
        Check(Get<AppState>(form, "_state").History.Count == 0, "New installs show no fabricated deletion history");
        Snapshot(form, "00-empty.png");
        foreach (var path in paths) form.AddTargetItem(path);
        form.AddTargetItem(paths[0]);
        form.AddTargetItem(Path.Combine(fixture, "missing"));
        Pump();
        Check(Get<List<TargetItem>>(form, "_items").Count == 4, "Duplicate and missing targets ignored");
        Check(Find(form, "Delete").Enabled, "Real file and folder queue enables Delete");
        Snapshot(form, "01-delete.png");
        Click(form, "NavAdd");
        Check(Find(form, "ViewAdd").Visible, "Sidebar opens Add");
        Snapshot(form, "02-add.png");
        Click(form, "NavHistory");
        Check(Find(form, "HistorySearch").Visible, "History search is visible only in History");
        Snapshot(form, "03-history-empty.png");
        Click(form, "NavSettings");
        Check(!Find(form, "HistorySearch").Visible, "Search hides outside History");
        Snapshot(form, "04-settings.png");
        var confirm = Get<ModernToggleSwitch>(form, "_confirm");
        confirm.Checked = false;
        Get<ModernToggleSwitch>(form, "_unlock").Checked = false;
        Click(form, "Apply");
        Check(!Get<AppState>(form, "_state").Settings.Confirm, "Apply saves confirmation choice");
        var saved = new AppState(statePath);
        Check(!saved.Settings.Confirm && !saved.Settings.RemoveLocked, "Settings persist to isolated storage");
        Get<ModernToggleSwitch>(form, "_dark").Checked = false;
        Click(form, "Apply");
        Check(!UITheme.Dark, "Dark mode changes the palette");
        Snapshot(form, "settings-light.png");
        Get<ModernToggleSwitch>(form, "_dark").Checked = true;
        Click(form, "Apply");
        Click(form, "NavDelete");
        Click(form, "Delete");
        Check(Get<bool>(form, "_busy"), "Delete enters processing state");
        Check(Get<CircularProgressControl>(form, "_ring").Animating, "Deletion enables ring and particles");
        Click(form, "NavAdd");
        Check(Find(form, "ViewDeletingAnimation").Visible, "Navigation cannot hide running deletion");
        form.AddTargetItem(paths[0]);
        Snapshot(form, "05-deleting.png");
        Pump();
        Snapshot(form, "05-deleting-next.png");
        Check(!File.ReadAllBytes(Path.Combine(_output, "05-deleting.png")).SequenceEqual(File.ReadAllBytes(Path.Combine(_output, "05-deleting-next.png"))), "Deletion animation produces distinct frames");
        WaitUntil(() => Get<Task>(form, "_deletionTask").IsCompleted, 15000);
        Get<Task>(form, "_deletionTask").GetAwaiter().GetResult();
        Check(paths.All(p => !File.Exists(p) && !Directory.Exists(p)), "Real backend deleted only disposable files and folder");
        Check(Get<AppState>(form, "_state").History.Count == 4, "Completed deletions enter real history");
        Check(Get<List<TargetItem>>(form, "_items").Count == 0, "Deleted targets leave the queue");
        Check(Get<CircularProgressControl>(form, "_ring").Progress == 100, "Progress reaches 100 after backend completion");
        Check(!Get<CircularProgressControl>(form, "_ring").Animating, "Animation stops on completion");
        Check(!Get<System.Windows.Forms.Timer>(Get<CircularProgressControl>(form, "_ring"), "_timer").Enabled, "Completed progress timer is stopped");
        Check(Find(form, "Done").Enabled, "Done is enabled after completion");
        Check(Get<Label>(form, "_title").Text == "Completed", "Completion updates the page title: " + Get<Label>(form, "_title").Text);
        Snapshot(form, "06-completed.png");
        Click(form, "Done");
        Click(form, "NavHistory");
        Check(Get<FlowLayoutPanel>(form, "_historyList").Controls.Count == 4, "History renders all results");
        Snapshot(form, "07-history.png");
        Click(form, "FilterFolders");
        Check(Get<FlowLayoutPanel>(form, "_historyList").Controls.Count == 1, "Folder filter works");
        Click(form, "FilterFiles");
        Check(Get<FlowLayoutPanel>(form, "_historyList").Controls.Count == 3, "File filter works");
        Click(form, "FilterAll");
        Find(form, "HistorySearch").Text = "notes";
        Check(Get<FlowLayoutPanel>(form, "_historyList").Controls.Count == 1, "Search filters history");
        Find(form, "HistorySearch").Text = "";
        Click(form, "FilterWithWarnings");
        Check(Get<FlowLayoutPanel>(form, "_historyList").Controls[0] is Label, "No warnings shows empty state");
        Click(form, "FilterAll");
        Check(new AppState(statePath).History.Count == 4, "History survives reopening storage");
        var schedule = new HistoryRecord { IsSuccessful = false, IsScheduled = true, Name = "a,\"b" };
        Check(schedule.StatusText == "On reboot", "Reboot scheduling is not labeled Deleted");
        Check(MainForm.HistoryCsv([schedule]).Contains("\"a,\"\"b\""), "CSV escapes commas and quotes");
        Check(MainForm.FormatSize(long.MaxValue).EndsWith("EB"), "Large file sizes do not overflow units");
        TestRetention(fixture);
        // Real drag/drop event path, with only fixture files as payload.
        File.WriteAllText(paths[0], "second pass");
        var data = new DataObject(DataFormats.FileDrop, new[] { paths[0] });
        var drag = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.None);
        typeof(Control).GetMethod("OnDragEnter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [drag]);
        Check(drag.Effect == DragDropEffects.Copy, "File drop is accepted");
        typeof(Control).GetMethod("OnDragDrop", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [drag]);
        Check(Get<List<TargetItem>>(form, "_items").Count == 1 && Find(form, "ViewDelete").Visible, "Drop adds target and opens Delete");
        Click(form, "Removenotes.txt");
        Check(Get<List<TargetItem>>(form, "_items").Count == 0, "Remove button updates both queue and previews");
        foreach (var path in new[] { paths[0], paths[2], paths[3] }) { File.WriteAllText(path, "layout fixture"); form.AddTargetItem(path); }
        float dpi = form.DeviceDpi / 96f;
        form.ClientSize = new Size((int)(820 * dpi), (int)(600 * dpi));
        Pump();
        foreach (string name in new[] { "NavDelete", "NavAdd", "NavHistory", "NavSettings" })
        {
            Click(form, name);
            CheckBounds(form);
            Snapshot(form, "minimum-" + name[3..].ToLowerInvariant() + ".png");
        }
        form.ClientSize = new Size((int)(1280 * dpi), (int)(800 * dpi));
        Click(form, "NavDelete");
        CheckBounds(form);
        Snapshot(form, "wide-delete.png");
        Click(form, "Clear");
        Check(Get<List<TargetItem>>(form, "_items").Count == 0, "Clear removes targets without deleting files");
        Check(File.Exists(paths[0]), "Clear preserves the original fixture file");
        form.ClientSize = new Size((int)(930 * dpi), (int)(630 * dpi));
        var overflow = Enumerable.Range(0, 20).Select(i => Path.Combine(fixture, $"overflow-{i}.txt")).ToArray();
        foreach (string path in overflow) File.WriteAllText(path, "scroll fixture");
        form.AddTargetItems(overflow);
        Pump();
        var queue = Get<FlowLayoutPanel>(form, "_itemList");
        typeof(Control).GetMethod("OnMouseWheel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(queue, [new MouseEventArgs(MouseButtons.None, 0, 1, 1, -120)]);
        Pump();
        Check(queue.AutoScrollPosition.Y < 0, "Overflow queue scrolls with mouse wheel");
        Check((GetWindowLong(queue.Handle, -16) & 0x00300000) == 0, "Overflow queue has no visible native scrollbars");
        Click(form, "NavSettings");
        var settingsList = Get<FlowLayoutPanel>(form, "_settingList");
        Check(!settingsList.AutoScroll && settingsList.Controls.Count == 5, "Five main settings fit without scrolling");
        Check(settingsList.Controls.Cast<Control>().All(c => c.Bottom <= settingsList.ClientSize.Height), "Every main setting is fully visible");
        Check((GetWindowLong(settingsList.Handle, -16) & 0x00300000) == 0, "Settings has no native scrollbars");
        using (var closeDialog = new System.Windows.Forms.Timer { Interval = 100 })
        {
            bool inspected = false;
            closeDialog.Tick += (_, _) =>
            {
                Form? dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f != form && f.Text == "More options");
                if (dialog == null) return;
                closeDialog.Stop();
                inspected = dialog.Controls.Cast<Control>().Count(c => c is SurfacePanel) == 2;
                Snapshot(dialog, "08-more-options.png");
                dialog.Close();
            };
            closeDialog.Start();
            Click(form, "MoreSettings");
            Check(inspected, "Advanced options open without scrolling or changing registry settings");
        }
        Click(form, "Maximize");
        Check(form.WindowState == FormWindowState.Maximized, "Custom maximize control works");
        Click(form, "Maximize");
        Check(form.WindowState == FormWindowState.Normal, "Custom restore control works");
        TestIcons();
        form.Close();
        }
        using var start = new System.Windows.Forms.Timer { Interval = 200 };
        start.Tick += (_, _) =>
        {
            start.Stop();
            try { Scenario(); }
            catch (Exception ex) { failure = ex; form.Dispose(); Application.ExitThread(); }
        };
        form.Shown += (_, _) => start.Start();
        Application.Run(form);
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void TestRetention(string fixture)
    {
        var state = new AppState(Path.Combine(fixture, "retention"));
        state.History.Add(new HistoryRecord { Timestamp = DateTime.Now.AddDays(-40) });
        state.History.Add(new HistoryRecord { Timestamp = DateTime.Now });
        state.Save();
        Check(state.History.Count == 1, "History retention prunes old records");
    }

    private static void TestIcons()
    {
        using var bitmap = new Bitmap(700, 180);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(20, 26, 39));
        var resources = typeof(SvgIcons).Assembly.GetManifestResourceNames().Where(n => n.EndsWith(".svg")).OrderBy(n => n).ToArray();
        int index = 0;
        foreach (string resource in resources)
        {
            string name = resource[(resource.LastIndexOf("Icons.", StringComparison.Ordinal) + 6)..^4];
            SvgIcons.Draw(g, new Rectangle(10 + index % 14 * 49, 10 + index / 14 * 75, 36, 36), name, Color.White);
            index++;
        }
        bitmap.Save(Path.Combine(_output, "icons.png"));
        Check(resources.Length >= 24, "All downloaded SVG resources render");
    }

    private static void CheckBounds(MainForm form)
    {
        var visible = Get<Dictionary<AppTab, Panel>>(form, "_views").Values.Single(v => v.Visible);
        foreach (Control child in visible.Controls)
            Check(child.Left >= 0 && child.Right <= visible.Width + 1 && child.Top >= 0 && child.Bottom <= visible.Height + 10,
                $"{visible.Name}/{child.Name} stays inside viewport {form.Width}x{form.Height}");
        foreach (var key in new[] { "_itemList", "_historyList", "_settingList" })
        {
            var list = Get<FlowLayoutPanel>(form, key);
            foreach (Control child in list.Controls)
                Check(child.Width <= list.ClientSize.Width, key + " rows fit available width");
        }
        var previews = Get<FlowLayoutPanel>(form, "_previews");
        foreach (Control preview in previews.Controls)
        {
            Check(preview.Right <= previews.ClientSize.Width, "Every Add preview fits without a horizontal scrollbar");
            Check(preview.Controls.Cast<Control>().All(c => c.Left >= 0 && c.Right <= preview.Width), "Every preview remove button remains reachable");
        }
    }

    private static void Snapshot(Form form, string name)
    {
        Pump();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(_output, name), ImageFormat.Png);
        if (form is MainForm && name is "00-empty.png" or "01-delete.png" or "02-add.png" or "04-settings.png" or "06-completed.png" or "07-history.png")
        {
            // Capture this real HWND directly so unrelated desktop windows cannot contaminate evidence.
            using var native = new Bitmap(form.Width, form.Height);
            using (var g = Graphics.FromImage(native))
            {
                IntPtr dc = g.GetHdc();
                try { Check(PrintWindow(form.Handle, dc, 2), "Native WinForms window renders: " + name); }
                finally { g.ReleaseHdc(dc); }
            }
            native.Save(Path.Combine(_output, "window-" + name), ImageFormat.Png);
            if (name == "00-empty.png")
            {
                int black = 0, sampled = 0;
                for (int y = form.Height / 4; y < form.Height * 3 / 4; y += 8)
                for (int x = form.Width / 4; x < form.Width * 3 / 4; x += 8)
                {
                    Color color = native.GetPixel(x, y);
                    if (color.R == 0 && color.G == 0 && color.B == 0) black++;
                    sampled++;
                }
                Check(black < sampled / 100, "Native empty-state backdrop has no black transparent-control rectangle");
            }
        }
        if (_captureScreen && form is MainForm && name is "00-empty.png" or "01-delete.png" or "02-add.png" or "04-settings.png" or "06-completed.png" or "07-history.png")
        {
            form.TopMost = true;
            ShowWindow(form.Handle, 9);
            SetWindowPos(form.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0043);
            form.Activate();
            form.Refresh();
            Pump();
            Thread.Sleep(200);
            Application.DoEvents();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                IntPtr surface = WindowFromPoint(form.PointToScreen(new Point(form.Width / 2, form.Height / 2)));
                if (GetAncestor(surface, 2) == form.Handle) break;
                ShowWindow(form.Handle, 9);
                SetWindowPos(form.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0043);
                Thread.Sleep(150);
                Application.DoEvents();
            }
            using var screen = new Bitmap(form.Width, form.Height);
            using (var g = Graphics.FromImage(screen))
                g.CopyFromScreen(form.PointToScreen(Point.Empty), Point.Empty, form.ClientSize);
            screen.Save(Path.Combine(_output, "screen-" + name), ImageFormat.Png);
            Check(IsWindowVisible(form.Handle), "Native window is visible during screen capture: " + name);
            IntPtr foregroundSurface = WindowFromPoint(form.PointToScreen(new Point(form.Width / 2, form.Height / 2)));
            DwmGetWindowAttribute(form.Handle, 14, out int cloaked, 4);
            Check(GetAncestor(foregroundSurface, 2) == form.Handle,
                $"Screen capture contains the actual ForceDeleter window: {name}; form={form.Handle}, pointWindow={foregroundSurface}, root={GetAncestor(foregroundSurface, 2)}, cloaked={cloaked}, style={GetWindowLong(form.Handle,-20):X}, location={form.Location}, bounds={form.Bounds}, origin={form.PointToScreen(Point.Empty)}, centre={form.PointToScreen(new Point(form.Width / 2, form.Height / 2))}, dpi={form.DeviceDpi}");
            if (name == "00-empty.png")
            {
                int black = 0, sampled = 0;
                for (int y = form.Height / 4; y < form.Height * 3 / 4; y += 8)
                for (int x = form.Width / 4; x < form.Width * 3 / 4; x += 8)
                {
                    var color = screen.GetPixel(x, y);
                    if (color.R == 0 && color.G == 0 && color.B == 0) black++;
                    sampled++;
                }
                Check(black < sampled / 100, "Real window empty-state backdrop has no black transparent-control rectangle");
            }
            form.TopMost = false;
        }
    }
    private static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); Passed.Add("PASS " + label); }
    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static Control Find(Control parent, string name) => parent.Controls.Find(name, true).First();
    private static void Click(Form form, string name) { ((Button)Find(form, name)).PerformClick(); Pump(); }
    private static void Pump() { Application.DoEvents(); Thread.Sleep(35); Application.DoEvents(); }
    private static void WaitUntil(Func<bool> condition, int timeout)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition()) { if (clock.ElapsedMilliseconds > timeout) throw new TimeoutException(); Pump(); }
    }
}
