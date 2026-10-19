using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ForceDeleter;

public enum DeleteResultStatus
{
    Success,
    ScheduledOnReboot,
    Failed
}

public record DeleteResult(DeleteResultStatus Status, string Message, int Tier);

/// <summary>
/// Multi-tiered universal force deletion engine combining all researched techniques:
/// Tier 1: Standard .NET Delete
/// Tier 2: Attribute Stripping & ACL Take Ownership (DeadLock / RickStrahl)
/// Tier 3: TrustedInstaller / SYSTEM Token Impersonation (NanaRun)
/// Tier 4: Restart Manager Process Killing & Handle Closing (windows-force-delete)
/// Tier 5: Low-Level NT POSIX Kernel Unlink (NtSetInformationFile)
/// Tier 6: Name Randomization & Reboot Scheduling (AhmedElbehary-Dev)
/// </summary>
public static class ForceDeleteEngine
{
    /// <summary>
    /// Executes the multi-tiered deletion pipeline on a file or folder.
    /// Incorporates techniques from:
    /// - NanaRun (TrustedInstaller / SYSTEM token impersonation)
    /// - windows-force-delete & fuck-delete-it (Restart Manager & Process Tree Termination)
    /// - SuperDelete (ACL bypass via NULL DACL & 32k long paths)
    /// - FastDelete (Parallel multi-threaded directory tree deletion)
    /// - SDelete-Gui (Zero/random overwrite anti-recovery secure shredding)
    /// </summary>
    public static DeleteResult ForceDelete(string targetPath, bool killLockingProcesses = true, bool secureWipe = false, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return new DeleteResult(DeleteResultStatus.Failed, "Target path is empty.", 0);

        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
            return new DeleteResult(DeleteResultStatus.Success, "File or folder does not exist (already deleted).", 0);

        bool isDirectory = Directory.Exists(targetPath);
        log?.Invoke($"\n[>>>] Starting force deletion for: {targetPath}");
        log?.Invoke($"[i] Target type: {(isDirectory ? "Directory" : "File")}");

        // SDelete-Gui technique: Overwrite file before unlinking if secure delete is requested
        if (!isDirectory && secureWipe)
        {
            SecureShredFile(targetPath, log);
        }

        // FastDelete technique: If directory, delete contents in parallel across multi-cores
        if (isDirectory)
        {
            try
            {
                DeleteDirectoryContents(targetPath, killLockingProcesses, secureWipe, log);
            }
            catch (Exception ex)
            {
                log?.Invoke($"[!] Directory contents cleanup warning: {ex.Message}");
            }
        }

        // ==========================================
        // TIER 1: Standard .NET Delete
        // ==========================================
        log?.Invoke("\n[Tier 1] Attempting standard deletion...");
        try
        {
            if (isDirectory)
                Directory.Delete(targetPath, true);
            else
                File.Delete(targetPath);

            if (!Exists(targetPath))
            {
                log?.Invoke("[✓] Tier 1 Success: Deleted with standard methods.");
                return new DeleteResult(DeleteResultStatus.Success, "Deleted via standard method.", 1);
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"[x] Tier 1 failed: {ex.Message}");
        }

        // ==========================================
        // TIER 2: Attribute Stripping & Take Ownership (DeadLock)
        // ==========================================
        log?.Invoke("\n[Tier 2] Resetting attributes & overriding ownership/ACLs...");
        try
        {
            SecurityManager.StripAttributes(targetPath, log);
            SecurityManager.TakeOwnershipAndGrantPermissions(targetPath, log);

            string normPath = SecurityManager.NormalizePath(targetPath);
            bool delOk = isDirectory ? NativeMethods.RemoveDirectoryW(normPath) : NativeMethods.DeleteFileW(normPath);

            if (delOk || !Exists(targetPath))
            {
                log?.Invoke("[✓] Tier 2 Success: Deleted after permissions & attribute reset.");
                return new DeleteResult(DeleteResultStatus.Success, "Deleted after taking ownership and resetting attributes.", 2);
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"[x] Tier 2 failed: {ex.Message}");
        }

        // ==========================================
        // TIER 3: NanaRun TrustedInstaller / SYSTEM Impersonation
        // ==========================================
        log?.Invoke("\n[Tier 3] Escalating to TrustedInstaller / SYSTEM token (NanaRun)...");
        try
        {
            bool tier3Success = PrivilegeManager.RunWithHighestPrivileges(() =>
            {
                SecurityManager.StripAttributes(targetPath, log);
                SecurityManager.TakeOwnershipAndGrantPermissions(targetPath, log);

                string normPath = SecurityManager.NormalizePath(targetPath);
                return isDirectory ? NativeMethods.RemoveDirectoryW(normPath) : NativeMethods.DeleteFileW(normPath);
            }, log);

            if (tier3Success || !Exists(targetPath))
            {
                log?.Invoke("[✓] Tier 3 Success: Deleted under TrustedInstaller / SYSTEM authority.");
                return new DeleteResult(DeleteResultStatus.Success, "Deleted using TrustedInstaller privilege escalation.", 3);
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"[x] Tier 3 failed: {ex.Message}");
        }

        // ==========================================
        // TIER 4: Process Unlocker (Restart Manager & Handle Closing)
        // ==========================================
        log?.Invoke("\n[Tier 4] Detecting & releasing file locks...");
        try
        {
            // Close handles in other processes
            int closedHandles = ProcessUnlocker.CloseFileHandles(targetPath, log);
            if (closedHandles > 0)
                log?.Invoke($"[+] Closed {closedHandles} remote handle(s).");

            // Kill locking processes if enabled
            if (killLockingProcesses)
            {
                int killed = ProcessUnlocker.KillLockingProcesses(targetPath, log);
                if (killed > 0)
                    log?.Invoke($"[+] Terminated {killed} locking process(es).");
            }

            string normPath = SecurityManager.NormalizePath(targetPath);
            bool delOk = isDirectory ? NativeMethods.RemoveDirectoryW(normPath) : NativeMethods.DeleteFileW(normPath);

            if (delOk || !Exists(targetPath))
            {
                log?.Invoke("[✓] Tier 4 Success: Deleted after unlocking file handles/processes.");
                return new DeleteResult(DeleteResultStatus.Success, "Deleted after unlocking processes and handles.", 4);
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"[x] Tier 4 failed: {ex.Message}");
        }

        // ==========================================
        // TIER 5: Low-Level NT POSIX Kernel Unlink
        // ==========================================
        if (!isDirectory)
        {
            log?.Invoke("\n[Tier 5] Invoking NT kernel POSIX unlink (NtSetInformationFile)...");
            try
            {
                if (LowLevelNtDelete(targetPath, log) || !Exists(targetPath))
                {
                    log?.Invoke("[✓] Tier 5 Success: Deleted using NT POSIX semantics.");
                    return new DeleteResult(DeleteResultStatus.Success, "Deleted using low-level NT kernel disposition.", 5);
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"[x] Tier 5 failed: {ex.Message}");
            }
        }

        // ==========================================
        // TIER 6: Rename Trick & Reboot Scheduling (AhmedElbehary-Dev)
        // ==========================================
        log?.Invoke("\n[Tier 6] Attempting temp rename & reboot deletion scheduling...");
        try
        {
            string parentDir = Path.GetDirectoryName(targetPath) ?? Path.GetTempPath();
            string tempName = Path.Combine(parentDir, $"_DELETED_{Guid.NewGuid():N}.tmp");

            // Rename to decouple from original locking path
            if (NativeMethods.MoveFileExW(SecurityManager.NormalizePath(targetPath), SecurityManager.NormalizePath(tempName), 0))
            {
                log?.Invoke($"[+] Renamed to temporary name: {Path.GetFileName(tempName)}");
                targetPath = tempName;

                // Try deleting the renamed file
                if (NativeMethods.DeleteFileW(SecurityManager.NormalizePath(targetPath)) || !Exists(targetPath))
                {
                    log?.Invoke("[✓] Tier 6 Success: Deleted after renaming.");
                    return new DeleteResult(DeleteResultStatus.Success, "Deleted successfully after renaming.", 6);
                }
            }

            // Ultimate fallback: Schedule on reboot via MoveFileEx
            string rebootPath = SecurityManager.NormalizePath(targetPath);
            if (NativeMethods.MoveFileExW(rebootPath, null, NativeMethods.MOVEFILE_DELAY_UNTIL_REBOOT))
            {
                log?.Invoke("[!] File is locked by active OS kernel driver. Scheduled for deletion on NEXT REBOOT.");
                return new DeleteResult(DeleteResultStatus.ScheduledOnReboot,
                    "The file is locked at the kernel level and has been scheduled for permanent deletion upon next system reboot.", 6);
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"[x] Tier 6 failed: {ex.Message}");
        }

        return new DeleteResult(DeleteResultStatus.Failed, "All deletion tiers failed.", 0);
    }

    private static bool LowLevelNtDelete(string filePath, Action<string>? log)
    {
        string normPath = SecurityManager.NormalizePath(filePath);

        // Open with DELETE access, FILE_SHARE_ALL, and BACKUP_SEMANTICS
        IntPtr hFile = NativeMethods.CreateFileW(
            normPath,
            NativeMethods.DELETE | NativeMethods.SYNCHRONIZE,
            NativeMethods.FILE_SHARE_ALL,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_BACKUP_SEMANTICS | NativeMethods.FILE_FLAG_OPEN_REPARSE_POINT,
            IntPtr.Zero);

        if (hFile == IntPtr.Zero || hFile == NativeMethods.INVALID_HANDLE_VALUE)
        {
            log?.Invoke($"[!] CreateFileW for NT delete failed: {Marshal.GetLastWin32Error()}");
            return false;
        }

        try
        {
            var ioStatus = new NativeMethods.IO_STATUS_BLOCK();

            // Try POSIX semantics (FileDispositionInformationEx)
            var dispEx = new NativeMethods.FILE_DISPOSITION_INFORMATION_EX
            {
                Flags = NativeMethods.FILE_DISPOSITION_FLAG_DELETE |
                        NativeMethods.FILE_DISPOSITION_FLAG_POSIX_SEMANTICS |
                        NativeMethods.FILE_DISPOSITION_FLAG_FORCE_IMAGE_SECTION_CHECK
            };

            int ntStatus = NativeMethods.NtSetInformationFile(
                hFile,
                ref ioStatus,
                ref dispEx,
                (uint)Marshal.SizeOf(dispEx),
                NativeMethods.FileDispositionInformationEx);

            if (ntStatus == NativeMethods.STATUS_SUCCESS)
            {
                log?.Invoke("[+] NtSetInformationFile (POSIX) accepted disposition.");
                return true;
            }

            // Fallback: standard FileDispositionInformation (class 13)
            var disp = new NativeMethods.FILE_DISPOSITION_INFORMATION { DeleteFile = true };
            ntStatus = NativeMethods.NtSetInformationFile(
                hFile,
                ref ioStatus,
                ref disp,
                (uint)Marshal.SizeOf(disp),
                NativeMethods.FileDispositionInformation);

            return ntStatus == NativeMethods.STATUS_SUCCESS;
        }
        finally
        {
            NativeMethods.CloseHandle(hFile);
        }
    }

    private static void DeleteDirectoryContents(string dirPath, bool killLocks, bool secureWipe, Action<string>? log)
    {
        // First optimize: unlock and grant permissions to the top directory to ease child deletions
        SecurityManager.StripAttributes(dirPath, null);
        SecurityManager.TakeOwnershipAndGrantPermissions(dirPath, null);

        try
        {
            // FastDelete technique: parallel multithreaded directory content processing across CPU cores
            var entries = System.Linq.Enumerable.ToList(Directory.EnumerateFileSystemEntries(dirPath));
            int maxDegree = Math.Clamp(Environment.ProcessorCount * 2, 4, 32);

            System.Threading.Tasks.Parallel.ForEach(entries, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = maxDegree }, entry =>
            {
                try
                {
                    if (Directory.Exists(entry))
                    {
                        DeleteDirectoryContents(entry, killLocks, secureWipe, log);
                        ForceDelete(entry, killLocks, secureWipe, log);
                    }
                    else
                    {
                        ForceDelete(entry, killLocks, secureWipe, log);
                    }
                }
                catch (Exception ex)
                {
                    log?.Invoke($"[!] FastDelete entry error {Path.GetFileName(entry)}: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            log?.Invoke($"[!] Directory enumeration warning for {Path.GetFileName(dirPath)}: {ex.Message}");
        }
    }

    /// <summary>
    /// Overwrites file data with zero bytes and flushes to physical storage before unlinking
    /// (Tulpep/SDelete-Gui and Sysinternals SDelete anti-recovery shredding technique).
    /// </summary>
    public static void SecureShredFile(string filePath, Action<string>? log = null)
    {
        try
        {
            string normPath = SecurityManager.NormalizePath(filePath);
            SecurityManager.StripAttributes(normPath, null);
            using (var fs = new FileStream(normPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            {
                long length = fs.Length;
                if (length > 0)
                {
                    byte[] zeroBuffer = new byte[Math.Min(length, 65536)];
                    long remaining = length;
                    fs.Position = 0;
                    while (remaining > 0)
                    {
                        int toWrite = (int)Math.Min(remaining, zeroBuffer.Length);
                        fs.Write(zeroBuffer, 0, toWrite);
                        remaining -= toWrite;
                    }
                    fs.Flush(flushToDisk: true);
                }
                fs.SetLength(0);
                log?.Invoke($"[★ SDelete] Overwrote file contents with zeroes to prevent recovery ({length} bytes).");
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"[!] SDelete shred note: {ex.Message}");
        }
    }

    private static bool Exists(string path)
    {
        return File.Exists(path) || Directory.Exists(path);
    }
}
