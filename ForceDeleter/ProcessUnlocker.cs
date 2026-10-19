using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace ForceDeleter;

public record LockingProcessInfo(int ProcessId, string Name, string Description, string AppType);

/// <summary>
/// Handles detection of file locks and unlocking mechanisms.
/// Combines:
/// - Windows Restart Manager (theminji/windows-force-delete & marianpekar/file-unlocker)
/// - NtQuerySystemInformation handle enumeration and forced handle closing (DUPLICATE_CLOSE_SOURCE)
/// </summary>
public static class ProcessUnlocker
{
    /// <summary>
    /// Uses Windows Restart Manager to find all processes holding a lock on the given file or directory.
    /// </summary>
    public static List<LockingProcessInfo> FindLockingProcesses(string path, Action<string>? log = null)
    {
        var result = new List<LockingProcessInfo>();
        string sessionKey = Guid.NewGuid().ToString();

        int res = NativeMethods.RmStartSession(out uint sessionHandle, 0, sessionKey);
        if (res != 0)
        {
            log?.Invoke($"[!] RmStartSession failed with error {res}");
            return result;
        }

        try
        {
            string[] resources = [path];
            res = NativeMethods.RmRegisterResources(sessionHandle, (uint)resources.Length, resources, 0, null, 0, null);
            if (res != 0)
            {
                log?.Invoke($"[!] RmRegisterResources failed with error {res}");
                return result;
            }

            uint nProcInfoNeeded = 0;
            uint nProcInfo = 0;
            uint lpdwRebootReasons = NativeMethods.RmRebootReasonNone;

            // First call with null to obtain required count
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
        catch (Exception ex)
        {
            log?.Invoke($"[!] Error during lock detection: {ex.Message}");
        }
        finally
        {
            NativeMethods.RmEndSession(sessionHandle);
        }

        return result;
    }

    /// <summary>
    /// Force terminates all processes locking the specified path.
    /// </summary>
    public static int KillLockingProcesses(string path, Action<string>? log = null)
    {
        var lockingProcesses = FindLockingProcesses(path, log);
        int killedCount = 0;

        foreach (var lockInfo in lockingProcesses)
        {
            try
            {
                // Never kill critical system processes or self
                if (lockInfo.ProcessId == Process.GetCurrentProcess().Id || lockInfo.ProcessId <= 4)
                {
                    log?.Invoke($"[!] Skipping protected system process {lockInfo.Name} (PID: {lockInfo.ProcessId})");
                    continue;
                }

                var proc = Process.GetProcessById(lockInfo.ProcessId);
                if (!proc.HasExited)
                {
                    log?.Invoke($"[x] Terminating process {proc.ProcessName} (PID: {proc.Id})...");
                    proc.Kill();
                    proc.WaitForExit(1500);
                    killedCount++;
                    log?.Invoke($"[✓] Process terminated: {proc.ProcessName}");
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
    /// Enumerates system handles and force-closes any open handles to the file
    /// without killing the host process (DuplicateHandle with DUPLICATE_CLOSE_SOURCE).
    /// </summary>
    public static int CloseFileHandles(string targetPath, Action<string>? log = null)
    {
        int closedCount = 0;
        string normalizedTarget = Path.GetFullPath(targetPath).TrimEnd('\\').ToLowerInvariant();

        // Enumerate handles via NtQuerySystemInformation
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

                // Skip current process
                if (entry.UniqueProcessId == Process.GetCurrentProcess().Id || entry.UniqueProcessId <= 4)
                    continue;

                IntPtr hProcess = NativeMethods.OpenProcess(
                    NativeMethods.PROCESS_DUP_HANDLE,
                    false, entry.UniqueProcessId);

                if (hProcess == IntPtr.Zero)
                    continue;

                try
                {
                    // Duplicate handle into current process to inspect object name
                    if (NativeMethods.DuplicateHandle(
                            hProcess,
                            new IntPtr(entry.HandleValue),
                            NativeMethods.GetCurrentProcess(),
                            out IntPtr hDup,
                            0, false, NativeMethods.DUPLICATE_SAME_ACCESS))
                    {
                        try
                        {
                            string? handleName = QueryObjectNameWithTimeout(hDup, 150);
                            if (!string.IsNullOrEmpty(handleName))
                            {
                                // Handle names are NT device paths like \Device\HarddiskVolume3\...
                                if (handleName.EndsWith(Path.GetFileName(targetPath), StringComparison.OrdinalIgnoreCase))
                                {
                                    // Close handle in source process
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

    /// <summary>
    /// Queries the object name with a strict timeout to prevent hangs on pipe handles.
    /// </summary>
    private static string? QueryObjectNameWithTimeout(IntPtr handle, int timeoutMs)
    {
        string? result = null;
        var thread = new Thread(() =>
        {
            int bufSize = 0x2000;
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
                        result = Marshal.PtrToStringUni(oni.Name.Buffer, oni.Name.Length / 2);
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
        })
        {
            IsBackground = true
        };

        thread.Start();
        if (!thread.Join(timeoutMs))
        {
            // Thread timed out (e.g. named pipe handle lock)
            return null;
        }

        return result;
    }
}
