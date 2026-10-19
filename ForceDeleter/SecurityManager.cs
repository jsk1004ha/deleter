using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ForceDeleter;

/// <summary>
/// Security & Permissions Manager based on DeadLock and RickStrahl/DeleteFiles.
/// Handles:
/// - Long paths (\\?\ prefix for up to 32,767 characters)
/// - Stripping Read-Only, Hidden, System file attributes
/// - Taking file/folder ownership (takeown / SetNamedSecurityInfo)
/// - Overriding ACLs to grant Everyone:Full Control (icacls / FileSecurity)
/// </summary>
public static class SecurityManager
{
    /// <summary>
    /// Normalizes path to support extended-length paths (up to 32,767 chars)
    /// and special Windows names (RickStrahl/DeleteFiles technique).
    /// </summary>
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        path = Path.GetFullPath(path);

        // Don't double-prefix
        if (path.StartsWith(@"\\?\") || path.StartsWith(@"\\.\"))
            return path;

        if (path.StartsWith(@"\\")) // UNC path
            return @"\\?\UNC\" + path.Substring(2);

        return @"\\?\" + path;
    }

    /// <summary>
    /// Strips Read-Only, Hidden, System, and other inhibiting attributes.
    /// </summary>
    public static bool StripAttributes(string path, Action<string>? log = null)
    {
        try
        {
            string longPath = NormalizePath(path);
            uint currentAttrs = NativeMethods.GetFileAttributesW(longPath);

            if (currentAttrs != 0xFFFFFFFF)
            {
                // Reset to FILE_ATTRIBUTE_NORMAL
                bool result = NativeMethods.SetFileAttributesW(longPath, NativeMethods.FILE_ATTRIBUTE_NORMAL);
                if (result)
                {
                    log?.Invoke($"[+] Reset file attributes to NORMAL: {Path.GetFileName(path)}");
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"[!] StripAttributes error: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Takes ownership of the file/folder and grants Everyone Full Control.
    /// Combines Win32 APIs, icacls, and takeown to guarantee access.
    /// </summary>
    public static bool TakeOwnershipAndGrantPermissions(string path, Action<string>? log = null)
    {
        bool success = false;

        // Method 1: In-process API ownership change using SID
        try
        {
            success = SetOwnershipAndDaclNative(path, log);
        }
        catch (Exception ex)
        {
            log?.Invoke($"[!] Native ACL modification failed: {ex.Message}");
        }

        // Method 2: System utilities takeown + icacls (DeadLock technique)
        try
        {
            RunTakeown(path, log);
            RunIcacls(path, log);
            success = true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[!] CLI permission grant failed: {ex.Message}");
        }

        return success;
    }

    private static bool SetOwnershipAndDaclNative(string path, Action<string>? log)
    {
        string normPath = NormalizePath(path);

        // Convert Administrators group SID: S-1-5-32-544
        if (NativeMethods.ConvertStringSidToSidW("S-1-5-32-544", out IntPtr pAdminSid))
        {
            try
            {
                uint result = NativeMethods.SetNamedSecurityInfoW(
                    normPath,
                    NativeMethods.SE_FILE_OBJECT,
                    NativeMethods.OWNER_SECURITY_INFORMATION,
                    pAdminSid,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero);

                if (result == 0)
                {
                    log?.Invoke($"[+] Set ownership to Administrators for {Path.GetFileName(path)}");
                }
            }
            finally
            {
                NativeMethods.LocalFree(pAdminSid);
            }
        }

        // Apply Everyone Full Control using .NET Security APIs
        try
        {
            bool isDir = Directory.Exists(path);
            var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);

            if (isDir)
            {
                var dirInfo = new DirectoryInfo(path);
                var dirSecurity = dirInfo.GetAccessControl();
                dirSecurity.AddAccessRule(new FileSystemAccessRule(
                    everyone,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                dirInfo.SetAccessControl(dirSecurity);
            }
            else
            {
                var fileInfo = new FileInfo(path);
                var fileSecurity = fileInfo.GetAccessControl();
                fileSecurity.AddAccessRule(new FileSystemAccessRule(
                    everyone,
                    FileSystemRights.FullControl,
                    AccessControlType.Allow));
                fileInfo.SetAccessControl(fileSecurity);
            }

            log?.Invoke($"[+] Granted Everyone:FullControl via ACL");
            return true;
        }
        catch
        {
            // Fallback to icacls
            return false;
        }
    }

    private static void RunTakeown(string path, Action<string>? log)
    {
        bool isDir = Directory.Exists(path);
        string args = isDir
            ? $"/F \"{path}\" /R /D Y"
            : $"/F \"{path}\"";

        var psi = new ProcessStartInfo("takeown.exe", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var proc = Process.Start(psi);
        proc?.WaitForExit(3000);
        if (proc?.ExitCode == 0)
        {
            log?.Invoke($"[+] takeown.exe succeeded for {Path.GetFileName(path)}");
        }
    }

    private static void RunIcacls(string path, Action<string>? log)
    {
        bool isDir = Directory.Exists(path);
        string args = isDir
            ? $"\"{path}\" /grant:r Everyone:(OI)(CI)F /T /C /Q"
            : $"\"{path}\" /grant:r Everyone:F /C /Q";

        var psi = new ProcessStartInfo("icacls.exe", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var proc = Process.Start(psi);
        proc?.WaitForExit(5000);
        if (proc?.ExitCode == 0)
        {
            log?.Invoke($"[+] icacls.exe granted Full Control for {Path.GetFileName(path)}");
        }
    }
}
