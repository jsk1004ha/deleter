using System;
using System.Windows.Forms;

namespace ForceDeleter;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Enable all high-privilege token rights upfront (SeDebugPrivilege, SeTakeOwnershipPrivilege, etc.)
        PrivilegeManager.EnableAllPrivileges();

        // Check command-line arguments
        if (args.Length > 0)
        {
            string firstArg = args[0].Trim().ToLowerInvariant();

            if (firstArg == "--install" || firstArg == "/install")
            {
                bool success = ContextMenuHelper.InstallContextMenu();
                MessageBox.Show(
                    success ? "Windows 탐색기 우클릭 메뉴가 등록되었습니다." : "우클릭 메뉴 등록 실패 (관리자 권한 필요).",
                    "ForceDeleter", MessageBoxButtons.OK,
                    success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                return;
            }

            if (firstArg == "--uninstall" || firstArg == "/uninstall")
            {
                bool success = ContextMenuHelper.UninstallContextMenu();
                MessageBox.Show(
                    success ? "Windows 탐색기 우클릭 메뉴가 제거되었습니다." : "우클릭 메뉴 제거 실패 (관리자 권한 필요).",
                    "ForceDeleter", MessageBoxButtons.OK,
                    success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                return;
            }

            if (firstArg == "--delete" || firstArg == "-d" || firstArg == "/delete")
            {
                if (args.Length > 1)
                {
                    var targets = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Skip(args, 1));
                    Console.WriteLine($"[ForceDeleter CLI] Multi-file batch deletion started for {targets.Length} targets...");
                    int failed = 0;
                    System.Threading.Tasks.Parallel.ForEach(targets, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount * 2, 4, 16) }, target =>
                    {
                        var res = ForceDeleteEngine.ForceDelete(target, true, false, true, Console.WriteLine);
                        if (res.Status == DeleteResultStatus.Failed)
                            System.Threading.Interlocked.Increment(ref failed);
                    });
                    Environment.ExitCode = (failed == 0) ? 0 : 1;
                    return;
                }
            }

            // Paths passed via context menu, CLI, or drag onto exe
            Application.Run(new MainForm(args));
            return;
        }

        // Normal GUI launch
        Application.Run(new MainForm());
    }
}