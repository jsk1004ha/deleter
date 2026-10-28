using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ForceDeleter;

public record LockingProcessInfo(int ProcessId, string Name, string Description, string AppType);

/// <summary>
/// 파일 및 디렉터리 잠금 탐지 및 핸들 강제 해제 모듈.
/// 참고 아키텍처:
/// - Microsoft PowerToys (File Locksmith): DOS -> NT 디바이스 경로 변환 및 파일 핸들 역추적
/// - winsiderss/systeminformer (Process Hacker): GetFileType(DISK) 필터링을 통한 NtQueryObject 무지연 고속 핸들 스캔
/// - Windows Restart Manager: 애플리케이션 및 서비스 레벨 잠금 프로세스 식별
/// </summary>
public static class ProcessUnlocker
{
    private static readonly ConcurrentDictionary<string, string> DevicePrefixCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// DOS 드라이브 경로(예: C:\Folder\file.txt)를 NT 커널 디바이스 경로(예: \Device\HarddiskVolume3\Folder\file.txt)로 변환합니다.
    /// PowerToys File Locksmith 핵심 알고리즘 차용.
    /// </summary>
    public static string? GetNtDevicePath(string dosPath)
    {
        if (string.IsNullOrWhiteSpace(dosPath))
            return null;

        try
        {
            string fullPath = Path.GetFullPath(dosPath.Trim('\"', ' '));
            string root = Path.GetPathRoot(fullPath) ?? string.Empty; // e.g. "C:\"
            string drive = root.TrimEnd('\\'); // e.g. "C:"

            if (drive.Length == 2 && drive[1] == ':')
            {
                string ntDevice = DevicePrefixCache.GetOrAdd(drive, static d =>
                {
                    var sb = new StringBuilder(512);
                    if (NativeMethods.QueryDosDeviceW(d, sb, (uint)sb.Capacity) > 0)
                    {
                        return sb.ToString();
                    }
                    return string.Empty;
                });

                if (!string.IsNullOrEmpty(ntDevice))
                {
                    string relative = fullPath.Substring(root.Length);
                    return ntDevice + "\\" + relative.TrimStart('\\');
                }
            }

            return fullPath;
        }
        catch
        {
            return dosPath;
        }
    }

    /// <summary>
    /// Restart Manager 및 NT 핸들 테이블 스캔을 결합하여 대상을 점유 중인 모든 프로세스를 정밀 식별합니다.
    /// </summary>
    public static List<LockingProcessInfo> FindLockingProcesses(string path, Action<string>? log = null)
    {
        var result = new List<LockingProcessInfo>();
        var seenPids = new HashSet<int>();

        // 1단계: Windows Restart Manager (데스크톱 앱, 서비스 식별)
        string sessionKey = Guid.NewGuid().ToString();
        int res = NativeMethods.RmStartSession(out uint sessionHandle, 0, sessionKey);
        if (res == 0)
        {
            try
            {
                string[] resources = [path];
                res = NativeMethods.RmRegisterResources(sessionHandle, (uint)resources.Length, resources, 0, null, 0, null);
                if (res == 0)
                {
                    uint nProcInfoNeeded = 0;
                    uint nProcInfo = 0;
                    uint lpdwRebootReasons = NativeMethods.RmRebootReasonNone;

                    res = NativeMethods.RmGetList(sessionHandle, out nProcInfoNeeded, ref nProcInfo, null, ref lpdwRebootReasons);
                    if (res == NativeMethods.ERROR_MORE_DATA && nProcInfoNeeded > 0)
                    {
                        var processInfo = new NativeMethods.RM_PROCESS_INFO[nProcInfoNeeded];
                        nProcInfo = nProcInfoNeeded;

                        res = NativeMethods.RmGetList(sessionHandle, out nProcInfoNeeded, ref nProcInfo, processInfo, ref lpdwRebootReasons);
                        if (res == 0)
                        {
                            for (int i = 0; i < nProcInfo; i++)
                            {
                                int pid = (int)processInfo[i].Process.dwProcessId;
                                if (seenPids.Add(pid))
                                {
                                    string appName = processInfo[i].strAppName;
                                    string svcName = processInfo[i].strServiceShortName;
                                    string displayName = !string.IsNullOrEmpty(appName) ? appName :
                                                         !string.IsNullOrEmpty(svcName) ? svcName : $"PID {pid}";

                                    string appTypeStr = processInfo[i].ApplicationType switch
                                    {
                                        1 => "MainWindow",
                                        2 => "OtherWindow",
                                        3 => "Service",
                                        4 => "Explorer",
                                        5 => "Console",
                                        6 => "Critical",
                                        _ => "Unknown"
                                    };

                                    result.Add(new LockingProcessInfo(pid, displayName, svcName, appTypeStr));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"[!] Restart Manager detection note: {ex.Message}");
            }
            finally
            {
                NativeMethods.RmEndSession(sessionHandle);
            }
        }

        // 2단계: Restart Manager에서 감지되지 않은 경우에만 NT 핸들 테이블 직접 스캔 (PowerToys File Locksmith)
        if (result.Count == 0)
        {
            try
            {
                EnumerateHandleLockingProcesses(path, result, seenPids, log);
            }
            catch (Exception ex)
            {
                log?.Invoke($"[!] Handle table detection note: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// 원격 파일 핸들을 강제 해제하고, 필요 시 점유 프로세스 트리를 종료합니다.
    /// </summary>
    public static void UnlockPath(string path, bool killProcesses = true, Action<string>? log = null)
    {
        try
        {
            CloseFileHandles(path, log);
            if (killProcesses)
            {
                KillLockingProcesses(path, log);
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"[!] UnlockPath error: {ex.Message}");
        }
    }

    /// <summary>
    /// 대상을 잠그고 있는 프로세스 트리 전체를 강제 종료합니다 (자식 프로세스 포함).
    /// </summary>
    public static int KillLockingProcesses(string path, Action<string>? log = null)
    {
        var lockingProcesses = FindLockingProcesses(path, log);
        int killedCount = 0;
        int currentPid = Process.GetCurrentProcess().Id;

        foreach (var lockInfo in lockingProcesses)
        {
            try
            {
                if (lockInfo.ProcessId == currentPid || lockInfo.ProcessId <= 4)
                {
                    log?.Invoke($"[!] Skipping protected system process {lockInfo.Name} (PID: {lockInfo.ProcessId})");
                    continue;
                }

                var proc = Process.GetProcessById(lockInfo.ProcessId);
                if (!proc.HasExited)
                {
                    log?.Invoke($"[x] Terminating process {proc.ProcessName} (PID: {proc.Id}) and its child tree...");
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(1500);
                    killedCount++;
                    log?.Invoke($"[✓] Process tree terminated: {proc.ProcessName}");
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"[!] Failed to terminate {lockInfo.Name} (PID: {lockInfo.ProcessId}): {ex.Message}");
            }
        }

        return killedCount;
    }

    /// <summary>
    /// 호스트 프로세스를 종료하지 않고 열려 있는 대상 파일 핸들만 원격으로 강제 닫습니다.
    /// (DuplicateHandle with DUPLICATE_CLOSE_SOURCE)
    /// </summary>
    public static int CloseFileHandles(string targetPath, Action<string>? log = null)
    {
        string? targetNtPath = GetNtDevicePath(targetPath);
        if (string.IsNullOrEmpty(targetNtPath))
            return 0;

        bool isDirectory = Directory.Exists(targetPath);
        string targetNtDirPrefix = targetNtPath.EndsWith('\\') ? targetNtPath : targetNtPath + "\\";

        int closedCount = 0;
        int currentPid = Process.GetCurrentProcess().Id;

        int infoLength = 0x10000;
        IntPtr infoPtr = Marshal.AllocHGlobal(infoLength);
        int returnLength = 0;

        try
        {
            while (NativeMethods.NtQuerySystemInformation(
                       NativeMethods.SystemHandleInformation,
                       infoPtr,
                       infoLength,
                       out returnLength) == NativeMethods.STATUS_INFO_LENGTH_MISMATCH)
            {
                Marshal.FreeHGlobal(infoPtr);
                infoLength = Math.Max(infoLength * 2, returnLength + 0x10000);
                infoPtr = Marshal.AllocHGlobal(infoLength);
            }

            int handleCount = Marshal.ReadInt32(infoPtr);
            long entryOffset = IntPtr.Size;
            int entrySize = Marshal.SizeOf<NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO>();

            for (int i = 0; i < handleCount; i++)
            {
                IntPtr currentEntry = new IntPtr(infoPtr.ToInt64() + entryOffset + (i * entrySize));
                var entry = Marshal.PtrToStructure<NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO>(currentEntry);

                if (entry.UniqueProcessId == currentPid || entry.UniqueProcessId <= 4)
                    continue;

                IntPtr hProcess = NativeMethods.OpenProcess(
                    NativeMethods.PROCESS_DUP_HANDLE,
                    false, entry.UniqueProcessId);

                if (hProcess == IntPtr.Zero)
                    continue;

                try
                {
                    if (NativeMethods.DuplicateHandle(
                            hProcess,
                            new IntPtr(entry.HandleValue),
                            NativeMethods.GetCurrentProcess(),
                            out IntPtr hDup,
                            0, false, NativeMethods.DUPLICATE_SAME_ACCESS))
                    {
                        try
                        {
                            // System Informer / File Locksmith 핵심 최적화:
                            // GetFileType이 FILE_TYPE_DISK인 경우에만 NtQueryObject를 호출하여
                            // 네임드 파이프나 소켓에 의한 블로킹을 원천 차단하고 마이크로초 단위로 즉시 판별합니다.
                            if (NativeMethods.GetFileType(hDup) != NativeMethods.FILE_TYPE_DISK)
                                continue;

                            string? handleName = QueryObjectName(hDup);
                            if (!string.IsNullOrEmpty(handleName))
                            {
                                bool isMatch = isDirectory
                                    ? (handleName.Equals(targetNtPath, StringComparison.OrdinalIgnoreCase) ||
                                       handleName.StartsWith(targetNtDirPrefix, StringComparison.OrdinalIgnoreCase))
                                    : handleName.Equals(targetNtPath, StringComparison.OrdinalIgnoreCase);

                                if (isMatch)
                                {
                                    if (NativeMethods.DuplicateHandle(
                                            hProcess,
                                            new IntPtr(entry.HandleValue),
                                            IntPtr.Zero,
                                            out _,
                                            0, false, NativeMethods.DUPLICATE_CLOSE_SOURCE))
                                    {
                                        closedCount++;
                                        log?.Invoke($"[✓] Closed handle in PID {entry.UniqueProcessId} for {Path.GetFileName(targetPath)}");
                                    }
                                }
                            }
                        }
                        finally
                        {
                            NativeMethods.CloseHandle(hDup);
                        }
                    }
                }
                finally
                {
                    NativeMethods.CloseHandle(hProcess);
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"[!] Handle closing error: {ex.Message}");
        }
        finally
        {
            Marshal.FreeHGlobal(infoPtr);
        }

        return closedCount;
    }

    private static void EnumerateHandleLockingProcesses(
        string targetPath,
        List<LockingProcessInfo> result,
        HashSet<int> seenPids,
        Action<string>? log)
    {
        string? targetNtPath = GetNtDevicePath(targetPath);
        if (string.IsNullOrEmpty(targetNtPath))
            return;

        bool isDirectory = Directory.Exists(targetPath);
        string targetNtDirPrefix = targetNtPath.EndsWith('\\') ? targetNtPath : targetNtPath + "\\";

        int currentPid = Process.GetCurrentProcess().Id;
        int infoLength = 0x10000;
        IntPtr infoPtr = Marshal.AllocHGlobal(infoLength);
        int returnLength = 0;

        try
        {
            while (NativeMethods.NtQuerySystemInformation(
                       NativeMethods.SystemHandleInformation,
                       infoPtr,
                       infoLength,
                       out returnLength) == NativeMethods.STATUS_INFO_LENGTH_MISMATCH)
            {
                Marshal.FreeHGlobal(infoPtr);
                infoLength = Math.Max(infoLength * 2, returnLength + 0x10000);
                infoPtr = Marshal.AllocHGlobal(infoLength);
            }

            int handleCount = Marshal.ReadInt32(infoPtr);
            long entryOffset = IntPtr.Size;
            int entrySize = Marshal.SizeOf<NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO>();

            for (int i = 0; i < handleCount; i++)
            {
                IntPtr currentEntry = new IntPtr(infoPtr.ToInt64() + entryOffset + (i * entrySize));
                var entry = Marshal.PtrToStructure<NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO>(currentEntry);

                int pid = entry.UniqueProcessId;
                if (pid == currentPid || pid <= 4 || seenPids.Contains(pid))
                    continue;

                IntPtr hProcess = NativeMethods.OpenProcess(
                    NativeMethods.PROCESS_DUP_HANDLE,
                    false, (uint)pid);

                if (hProcess == IntPtr.Zero)
                    continue;

                try
                {
                    if (NativeMethods.DuplicateHandle(
                            hProcess,
                            new IntPtr(entry.HandleValue),
                            NativeMethods.GetCurrentProcess(),
                            out IntPtr hDup,
                            0, false, NativeMethods.DUPLICATE_SAME_ACCESS))
                    {
                        try
                        {
                            if (NativeMethods.GetFileType(hDup) != NativeMethods.FILE_TYPE_DISK)
                                continue;

                            string? handleName = QueryObjectName(hDup);
                            if (!string.IsNullOrEmpty(handleName))
                            {
                                bool isMatch = isDirectory
                                    ? (handleName.Equals(targetNtPath, StringComparison.OrdinalIgnoreCase) ||
                                       handleName.StartsWith(targetNtDirPrefix, StringComparison.OrdinalIgnoreCase))
                                    : handleName.Equals(targetNtPath, StringComparison.OrdinalIgnoreCase);

                                if (isMatch && seenPids.Add(pid))
                                {
                                    try
                                    {
                                        var p = Process.GetProcessById(pid);
                                        result.Add(new LockingProcessInfo(pid, p.ProcessName, p.MainModule?.ModuleName ?? "", "ProcessHandle"));
                                    }
                                    catch
                                    {
                                        result.Add(new LockingProcessInfo(pid, $"PID {pid}", "", "ProcessHandle"));
                                    }
                                }
                            }
                        }
                        finally
                        {
                            NativeMethods.CloseHandle(hDup);
                        }
                    }
                }
                finally
                {
                    NativeMethods.CloseHandle(hProcess);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(infoPtr);
        }
    }

    /// <summary>
    /// GetFileType(FILE_TYPE_DISK) 검증을 거친 디스크 파일 핸들의 NT 이름을 무지연 조회합니다.
    /// </summary>
    private static string? QueryObjectName(IntPtr handle)
    {
        int bufSize = 0x1000;
        IntPtr buf = Marshal.AllocHGlobal(bufSize);
        try
        {
            if (NativeMethods.NtQueryObject(
                    handle,
                    NativeMethods.ObjectNameInformation,
                    buf, bufSize, out _) == NativeMethods.STATUS_SUCCESS)
            {
                var oni = Marshal.PtrToStructure<NativeMethods.OBJECT_NAME_INFORMATION>(buf);
                if (oni.Name.Buffer != IntPtr.Zero && oni.Name.Length > 0)
                {
                    return Marshal.PtrToStringUni(oni.Name.Buffer, oni.Name.Length / 2);
                }
            }
        }
        catch
        {
            // Ignored
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
        return null;
    }
}
