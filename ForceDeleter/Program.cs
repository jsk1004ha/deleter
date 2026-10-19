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
                    var res = ForceDeleteEngine.ForceDelete(args[1], true, false, true, Console.WriteLine);
                    Environment.ExitCode = res.Status == DeleteResultStatus.Success ? 0 : 1;
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