using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ForceDeleter;

/// <summary>
/// Privilege escalation manager based on techniques from NanaRun (MinSudo).
/// Enables Windows privileges (SeDebug, SeTakeOwnership, SeBackup, etc.) and
/// allows stealing and impersonating NT AUTHORITY\SYSTEM and TrustedInstaller tokens.
/// </summary>
public static class PrivilegeManager
{
    private static readonly string[] RequiredPrivileges =
    [
        NativeMethods.SE_DEBUG_NAME,
        NativeMethods.SE_TAKE_OWNERSHIP_NAME,
        NativeMethods.SE_BACKUP_NAME,
        NativeMethods.SE_RESTORE_NAME,
        NativeMethods.SE_SECURITY_NAME,
        NativeMethods.SE_IMPERSONATE_NAME
    ];

    /// <summary>
    /// Enables all critical privileges in the current process access token.
    /// </summary>
    public static bool EnableAllPrivileges(Action<string>? log = null)
    {
        if (!NativeMethods.OpenProcessToken(
                NativeMethods.GetCurrentProcess(),
                NativeMethods.TOKEN_ADJUST_PRIVILEGES | NativeMethods.TOKEN_QUERY,
                out IntPtr hToken))
        {
            log?.Invoke($"[!] OpenProcessToken failed: {Marshal.GetLastWin32Error()}");
            return false;
        }

        bool allSuccess = true;
        try
        {
            foreach (var privName in RequiredPrivileges)
            {
                if (EnablePrivilege(hToken, privName))
                {
                    log?.Invoke($"[+] Privilege enabled: {privName}");
                }
                else
                {
                    log?.Invoke($"[-] Failed to enable privilege: {privName}");
                    allSuccess = false;
                }
            }
        }
        finally
        {
            NativeMethods.CloseHandle(hToken);
        }

        return allSuccess;
    }

    private static bool EnablePrivilege(IntPtr hToken, string privilegeName)
    {
        if (!NativeMethods.LookupPrivilegeValueW(null, privilegeName, out NativeMethods.LUID luid))
        {
            return false;
        }

        var tp = new NativeMethods.TOKEN_PRIVILEGES
        {
            PrivilegeCount = 1,
            Privileges = new NativeMethods.LUID_AND_ATTRIBUTES
            {
                Luid = luid,
                Attributes = NativeMethods.SE_PRIVILEGE_ENABLED
            }
        };

        if (!NativeMethods.AdjustTokenPrivileges(
                hToken, false, ref tp,
                (uint)Marshal.SizeOf(tp), IntPtr.Zero, IntPtr.Zero))
        {
            return false;
        }

        return Marshal.GetLastWin32Error() == 0;
    }

    /// <summary>
    /// Impersonates NT AUTHORITY\SYSTEM token by finding lsass.exe or winlogon.exe.
    /// </summary>
    public static bool ImpersonateSystem(Action<string>? log = null)
    {
        EnableAllPrivileges(null);

        // Find lsass.exe or winlogon.exe
        string[] targetProcessNames = ["lsass", "winlogon"];
        foreach (var procName in targetProcessNames)
        {
            var processes = Process.GetProcessesByName(procName);
            foreach (var proc in processes)
            {
                try
                {
                    IntPtr hProc = NativeMethods.OpenProcess(
                        NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_DUP_HANDLE,
                        false, (uint)proc.Id);

                    if (hProc == IntPtr.Zero || hProc == NativeMethods.INVALID_HANDLE_VALUE)
                        continue;

                    try
                    {
                        if (NativeMethods.OpenProcessToken(
                                hProc,
                                NativeMethods.TOKEN_DUPLICATE | NativeMethods.TOKEN_IMPERSONATE | NativeMethods.TOKEN_QUERY,
                                out IntPtr hToken))
                        {
                            try
                            {
                                if (NativeMethods.DuplicateTokenEx(
                                        hToken,
                                        NativeMethods.TOKEN_ALL_ACCESS,
                                        IntPtr.Zero,
                                        NativeMethods.SecurityImpersonation,
                                        NativeMethods.TokenImpersonation,
                                        out IntPtr hImpersonationToken))
                                {
                                    try
                                    {
                                        if (NativeMethods.ImpersonateLoggedOnUser(hImpersonationToken))
                                        {
                                            log?.Invoke($"[+] Impersonated SYSTEM via {proc.ProcessName} (PID: {proc.Id})");
                                            return true;
                                        }
                                    }
                                    finally
                                    {
                                        NativeMethods.CloseHandle(hImpersonationToken);
                                    }
                                }
                            }
                            finally
                            {
                                NativeMethods.CloseHandle(hToken);
                            }
                        }
                    }
                    finally
                    {
                        NativeMethods.CloseHandle(hProc);
                    }
                }
                catch
                {
                    // Continue searching
                }
            }
        }

        log?.Invoke("[-] Failed to impersonate SYSTEM");
        return false;
    }

    /// <summary>
    /// Starts the TrustedInstaller service, steals its process token, and impersonates it.
    /// Technique directly ported from NanaRun/MinSudo.
    /// </summary>
    public static bool ImpersonateTrustedInstaller(Action<string>? log = null)
    {
        // Must first enable SeDebugPrivilege
        EnableAllPrivileges(null);

        IntPtr hSCM = NativeMethods.OpenSCManagerW(null, null, NativeMethods.SC_MANAGER_ALL_ACCESS);
        if (hSCM == IntPtr.Zero)
        {
            log?.Invoke($"[!] Failed to open SCM: {Marshal.GetLastWin32Error()}");
            return false;
        }

        IntPtr hService = IntPtr.Zero;
        try
        {
            hService = NativeMethods.OpenServiceW(hSCM, "TrustedInstaller",
                NativeMethods.SERVICE_START | NativeMethods.SERVICE_QUERY_STATUS);

            if (hService == IntPtr.Zero)
            {
                log?.Invoke($"[!] Failed to open TrustedInstaller service: {Marshal.GetLastWin32Error()}");
                return false;
            }

            // Start the service if not running
            NativeMethods.StartServiceW(hService, 0, IntPtr.Zero);

            // Wait for service to run and obtain its PID
            int attempts = 0;
            uint tiPid = 0;
            int bufferSize = Marshal.SizeOf<NativeMethods.SERVICE_STATUS_PROCESS>();
            IntPtr statusBuffer = Marshal.AllocHGlobal(bufferSize);

            try
            {
                while (attempts++ < 30) // Up to 3 seconds
                {
                    if (NativeMethods.QueryServiceStatusEx(
                            hService, 0, statusBuffer,
                            (uint)bufferSize, out _))
                    {
                        var status = Marshal.PtrToStructure<NativeMethods.SERVICE_STATUS_PROCESS>(statusBuffer);
                        if (status.dwCurrentState == NativeMethods.SERVICE_RUNNING && status.dwProcessId != 0)
                        {
                            tiPid = status.dwProcessId;
                            break;
                        }
                    }
                    System.Threading.Thread.Sleep(100);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(statusBuffer);
            }

            if (tiPid == 0)
            {
                // Fallback: check if TrustedInstaller process is already running
                var tiProcs = Process.GetProcessesByName("TrustedInstaller");
                if (tiProcs.Length > 0)
                {
                    tiPid = (uint)tiProcs[0].Id;
                }
            }

            if (tiPid == 0)
            {
                log?.Invoke("[-] TrustedInstaller service failed to start");
                return false;
            }

            log?.Invoke($"[+] TrustedInstaller service running (PID: {tiPid})");

            // Open TrustedInstaller process & duplicate token
            IntPtr hProc = NativeMethods.OpenProcess(
                NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_DUP_HANDLE,
                false, tiPid);

            if (hProc == IntPtr.Zero)
            {
                log?.Invoke($"[!] Failed to open TrustedInstaller process: {Marshal.GetLastWin32Error()}");
                return false;
            }

            try
            {
                if (NativeMethods.OpenProcessToken(
                        hProc,
                        NativeMethods.TOKEN_DUPLICATE | NativeMethods.TOKEN_IMPERSONATE | NativeMethods.TOKEN_QUERY,
                        out IntPtr hToken))
                {
                    try
                    {
                        if (NativeMethods.DuplicateTokenEx(
                                hToken,
                                NativeMethods.TOKEN_ALL_ACCESS,
                                IntPtr.Zero,
                                NativeMethods.SecurityImpersonation,
                                NativeMethods.TokenImpersonation,
                                out IntPtr hImpersonationToken))
                        {
                            try
                            {
                                if (NativeMethods.ImpersonateLoggedOnUser(hImpersonationToken))
                                {
                                    log?.Invoke("[✓] Successfully impersonating NT SERVICE\\TrustedInstaller!");
                                    return true;
                                }
                            }
                            finally
                            {
                                NativeMethods.CloseHandle(hImpersonationToken);
                            }
                        }
                    }
                    finally
                    {
                        NativeMethods.CloseHandle(hToken);
                    }
                }
            }
            finally
            {
                NativeMethods.CloseHandle(hProc);
            }
        }
        finally
        {
            if (hService != IntPtr.Zero)
                NativeMethods.CloseServiceHandle(hService);
            NativeMethods.CloseServiceHandle(hSCM);
        }

        log?.Invoke("[-] Failed to impersonate TrustedInstaller");
        return false;
    }

    /// <summary>
    /// Reverts thread impersonation back to the original process identity.
    /// </summary>
    public static void Revert()
    {
        NativeMethods.RevertToSelf();
    }

    /// <summary>
    /// Executes an action with the highest possible privileges (TrustedInstaller -> SYSTEM -> Admin).
    /// </summary>
    public static T RunWithHighestPrivileges<T>(Func<T> action, Action<string>? log = null)
    {
        EnableAllPrivileges(log);

        bool impersonated = false;
        try
        {
            impersonated = ImpersonateTrustedInstaller(log);
            if (!impersonated)
            {
                log?.Invoke("[i] Fallback to SYSTEM impersonation...");
                impersonated = ImpersonateSystem(log);
            }

            return action();
        }
        finally
        {
            if (impersonated)
            {
                Revert();
            }
        }
    }

    public static void RunWithHighestPrivileges(Action action, Action<string>? log = null)
    {
        RunWithHighestPrivileges<bool>(() =>
        {
            action();
            return true;
        }, log);
    }
}
