using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ForceDeleter;

static class Program
{
    private const string SingleInstanceMutexName = @"Local\ForceDeleter_SingleInstance_Mutex_v2";
    private const string SingleInstancePipeName = "ForceDeleter_SingleInstance_Pipe_v2";

    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // 관리자 및 핵심 토큰 권한 활성화 (SeDebugPrivilege, SeTakeOwnershipPrivilege 등)
        PrivilegeManager.EnableAllPrivileges();

        // 1. 명령줄 옵션 처리
        if (args.Length > 0)
        {
            string firstArg = args[0].Trim().ToLowerInvariant();

            if (firstArg is "--install" or "/install")
            {
                bool success = ContextMenuHelper.InstallContextMenu();
                MessageBox.Show(
                    success ? "Windows 탐색기 우클릭 메뉴('무조건 강제 삭제')가 성공적으로 등록되었습니다." : "우클릭 메뉴 등록 실패 (관리자 권한 필요).",
                    "ForceDeleter",
                    MessageBoxButtons.OK,
                    success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                return;
            }

            if (firstArg is "--uninstall" or "/uninstall")
            {
                bool success = ContextMenuHelper.UninstallContextMenu();
                MessageBox.Show(
                    success ? "Windows 탐색기 우클릭 메뉴가 해제되었습니다." : "우클릭 메뉴 해제 실패 (관리자 권한 필요).",
                    "ForceDeleter",
                    MessageBoxButtons.OK,
                    success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                return;
            }

            if (firstArg is "--delete" or "-d" or "/delete")
            {
                var targetPaths = args.Skip(1).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
                if (targetPaths.Length > 0)
                {
                    Console.WriteLine($"[ForceDeleter CLI] Multi-file batch deletion started for {targetPaths.Length} targets...");
                    int failed = 0;
                    Parallel.ForEach(targetPaths, new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount * 2, 4, 16)
                    }, path =>
                    {
                        if (File.Exists(path) || Directory.Exists(path))
                        {
                            var res = ForceDeleteEngine.ForceDelete(path, true, false, true, Console.WriteLine);
                            if (res.Status == DeleteResultStatus.Failed)
                            {
                                Interlocked.Increment(ref failed);
                            }
                        }
                    });
                    Environment.ExitCode = (failed == 0) ? 0 : 1;
                    return;
                }
            }
        }

        var validPaths = args
            .Where(p => !p.StartsWith("--", StringComparison.Ordinal) && !p.StartsWith("/", StringComparison.Ordinal) && (File.Exists(p) || Directory.Exists(p)))
            .ToArray();

        // 2. 탐색기 우클릭 다중 선택 시 여러 프로세스가 동시에 실행되더라도
        // 첫 번째 인스턴스로 모든 경로를 Named Pipe로 전달하고 후속 인스턴스는 즉시 종료합니다.
        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            if (TryForwardPathsToRunningInstance(validPaths))
            {
                return;
            }
        }

        using var cts = new CancellationTokenSource();
        var mainForm = new MainForm(validPaths.Length > 0 ? validPaths : null);

        if (isFirstInstance)
        {
            StartPipeServer(mainForm, cts.Token);
        }

        Application.Run(mainForm);
        cts.Cancel();
    }

    private static bool TryForwardPathsToRunningInstance(string[] paths)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", SingleInstancePipeName, PipeDirection.Out);
            client.Connect(timeout: 1500);

            using var writer = new StreamWriter(client, Encoding.UTF8);
            if (paths.Length == 0)
            {
                writer.WriteLine("__ACTIVATE__");
            }
            else
            {
                foreach (string path in paths)
                {
                    writer.WriteLine(path);
                }
            }
            writer.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void StartPipeServer(MainForm mainForm, CancellationToken token)
    {
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        SingleInstancePipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    string payload = await reader.ReadToEndAsync(token).ConfigureAwait(false);

                    var receivedPaths = payload
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(p => p != "__ACTIVATE__" && (File.Exists(p) || Directory.Exists(p)))
                        .ToArray();

                    if (!mainForm.IsDisposed)
                    {
                        mainForm.BeginInvoke(new Action(() =>
                        {
                            if (receivedPaths.Length > 0)
                            {
                                mainForm.AddTargetItems(receivedPaths);
                            }
                            if (mainForm.WindowState == FormWindowState.Minimized)
                            {
                                mainForm.WindowState = FormWindowState.Normal;
                            }
                            mainForm.Activate();
                        }));
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(100, token).ConfigureAwait(false);
                }
            }
        }, token);
    }
}
