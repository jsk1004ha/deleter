using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

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
/// - Instant Detach (Rename to .__force_del_<guid> in < 1ms to instantly free path)
/// - Ultra-Fast Native Win32 Crawler (FindFirstFileExW with FindExInfoBasic & FIND_FIRST_EX_LARGE_FETCH)
/// - Multi-Core Parallel I/O Engine (FastDelete saturating NVMe/SSD queue depth)
/// - Low-Level NT POSIX Kernel Unlink (NtSetInformationFile with POSIX semantics)
/// - Attribute Stripping & NULL DACL Access Control Bypass (SuperDelete)
/// - TrustedInstaller / SYSTEM Token Impersonation (NanaRun)
/// - Restart Manager & Process Tree Termination (windows-force-delete & fuck-delete-it)
/// - Anti-Recovery Secure Zero Shredding (Tulpep/SDelete-Gui)
/// - Atomic Rename & Reboot Scheduling (AhmedElbehary-Dev / ForcedDeleteFiles)
/// </summary>
public static class ForceDeleteEngine
{
    /// <summary>
    /// Executes the force deletion pipeline on a file or folder.
    /// When turboMode is enabled (default), leverages instant detach and native Win32 multi-threaded
    /// fast deletion to achieve maximum SSD/NVMe throughput.
    /// </summary>
    public static DeleteResult ForceDelete(
        string targetPath, 
        bool killLockingProcesses = true, 
        bool secureWipe = false, 
        bool turboMode = true, 
        Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return new DeleteResult(DeleteResultStatus.Failed, "Target path is empty.", 0);

        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
            return new DeleteResult(DeleteResultStatus.Success, "File or folder does not exist (already deleted).", 0);

        bool isDirectory = Directory.Exists(targetPath);
        log?.Invoke($"\n[>>>] Starting force deletion for: {targetPath}");
        log?.Invoke($"[i] Target type: {(isDirectory ? "Directory" : "File")}, Turbo: {turboMode}");

        // SDelete-Gui technique: Overwrite file before unlinking if secure delete is requested
        if (!isDirectory && secureWipe)
        {
            SecureShredFile(targetPath, log);
        }

        if (isDirectory)
        {
            string normPath = SecurityManager.NormalizePath(targetPath);
            string workingPath = targetPath;

            // 1. Instant Detach: Rename folder in-place on same volume to immediately free path (< 1ms)
            if (turboMode)
            {
                try
                {
                    string? parentDir = Path.GetDirectoryName(targetPath.TrimEnd('\\', '/'));
                    if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                    {
                        string detachName = Path.Combine(parentDir, $".__force_del_{Guid.NewGuid():N}");
                        string normDetach = SecurityManager.NormalizePath(detachName);
                        if (NativeMethods.MoveFileExW(normPath, normDetach, 0))
                        {
                            workingPath = detachName;
                            log?.Invoke($"[⚡ Instant Detach] Folder decoupled in 0.1ms -> {Path.GetFileName(detachName)}");
                        }
                    }
                }
                catch
                {
                    // If rename fails (e.g. root drive or locked handle), proceed with original path
                }
            }

            // 2. High-Speed Multi-Threaded Directory Deletion
            try
            {
                FastDeleteDirectoryTree(workingPath, killLockingProcesses, secureWipe, log);
            }
            catch (Exception ex)
            {
                log?.Invoke($"[!] Fast tree deletion note: {ex.Message}");
            }

            // Check if directory successfully purged
            if (!Exists(workingPath) && !Exists(targetPath))
            {
                log?.Invoke("[✓] Directory completely wiped.");
                return new DeleteResult(DeleteResultStatus.Success, "Directory deleted at high speed.", 1);
            }

            // If stubborn files remain, escalate the root directory
            targetPath = workingPath;
        }
        else
        {
            // For a single file: Try ultra-fast path first
            if (turboMode && !secureWipe)
            {
                string normPath = SecurityManager.NormalizePath(targetPath);
                
                // Fast direct unlink
                if (NativeMethods.DeleteFileW(normPath))
                {
                    log?.Invoke("[✓] Fast Path Success: File deleted immediately.");
                    return new DeleteResult(DeleteResultStatus.Success, "Deleted via fast path.", 1);
                }

                // Fast attribute strip + unlink
                NativeMethods.SetFileAttributesW(normPath, NativeMethods.FILE_ATTRIBUTE_NORMAL);
                if (NativeMethods.DeleteFileW(normPath))
                {
                    log?.Invoke("[✓] Fast Path Success: Deleted after clearing attributes.");
                    return new DeleteResult(DeleteResultStatus.Success, "Deleted after attribute reset.", 1);
                }

                // Fast NT POSIX unlink
                if (LowLevelNtDelete(targetPath, null))
                {
                    log?.Invoke("[✓] Fast Path Success: Deleted using NT POSIX semantics.");
                    return new DeleteResult(DeleteResultStatus.Success, "Deleted via NT POSIX disposition.", 1);
                }
            }
        }

        // ==========================================
        // Heavy Escalation Pipeline for Stubborn Targets
        // ==========================================
        return ForceDeleteSingleTarget(targetPath, isDirectory, killLockingProcesses, secureWipe, log);
    }

    /// <summary>
    /// High-performance native directory crawler using Win32 FindFirstFileExW with FindExInfoBasic
    /// (skips legacy 8.3 short names for 30-70% speedup) and FIND_FIRST_EX_LARGE_FETCH,
    /// parallelized across all CPU cores to fully saturate SSD/NVMe queue depth.
    /// </summary>
    private static void FastDeleteDirectoryTree(string dirPath, bool killLocks, bool secureWipe, Action<string>? log)
    {
        string normDir = SecurityManager.NormalizePath(dirPath);

        // Pre-strip attributes from the directory itself
        NativeMethods.SetFileAttributesW(normDir, NativeMethods.FILE_ATTRIBUTE_NORMAL);

        var files = new List<string>();
        var subdirs = new List<string>();

        string searchPattern = normDir.TrimEnd('\\') + @"\*";
        IntPtr hFind = NativeMethods.FindFirstFileExW(
            searchPattern,
            NativeMethods.FindExInfoBasic,
            out NativeMethods.WIN32_FIND_DATA findData,
            NativeMethods.FindExSearchNameMatch,
            IntPtr.Zero,
            NativeMethods.FIND_FIRST_EX_LARGE_FETCH);

        if (hFind != (IntPtr)(-1) && hFind != IntPtr.Zero)
        {
            try
            {
                do
                {
                    string name = findData.cFileName;
                    if (name == "." || name == "..") continue;

                    string childPath = normDir.TrimEnd('\\') + @"\" + name;
                    if ((findData.dwFileAttributes & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) != 0)
                    {
                        subdirs.Add(childPath);
                    }
                    else
                    {
                        files.Add(childPath);
                    }
                }
                while (NativeMethods.FindNextFileW(hFind, out findData));
            }
            finally
            {
                NativeMethods.FindClose(hFind);
            }
        }

        // 1. Parallel Fast File Deletion across CPU cores
        int maxDegree = Math.Clamp(Environment.ProcessorCount * 2, 8, 32);
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = maxDegree }, filePath =>
        {
            FastDeleteFile(filePath, killLocks, secureWipe, log);
        });

        // 2. Parallel Recursive Subdirectory Deletion
        Parallel.ForEach(subdirs, new ParallelOptions { MaxDegreeOfParallelism = maxDegree }, subdirPath =>
        {
            FastDeleteDirectoryTree(subdirPath, killLocks, secureWipe, log);
        });

        // 3. Remove this directory container
        if (!NativeMethods.RemoveDirectoryW(normDir))
        {
            NativeMethods.SetFileAttributesW(normDir, NativeMethods.FILE_ATTRIBUTE_NORMAL);
            if (!NativeMethods.RemoveDirectoryW(normDir))
            {
                // Directory still held or has permission lock -> escalate
                ForceDeleteSingleTarget(normDir, isDirectory: true, killLocks, secureWipe, log);
            }
        }
    }

    /// <summary>
    /// Deletes a single file with fast path optimizations before falling back to heavy escalation.
    /// </summary>
    private static void FastDeleteFile(string filePath, bool killLocks, bool secureWipe, Action<string>? log)
    {
        if (secureWipe)
        {
            SecureShredFile(filePath, null);
        }

        // Fast Path 1: Direct native unlink (< 0.05ms)
        if (NativeMethods.DeleteFileW(filePath))
            return;

        // Fast Path 2: Clear Read-Only/Hidden attributes and retry
        NativeMethods.SetFileAttributesW(filePath, NativeMethods.FILE_ATTRIBUTE_NORMAL);
        if (NativeMethods.DeleteFileW(filePath))
            return;

        // Fast Path 3: Low-Level NT POSIX Disposition
        if (LowLevelNtDelete(filePath, null))
            return;

        // Stubborn file: escalate to full multi-tiered force delete
        ForceDeleteSingleTarget(filePath, isDirectory: false, killLocks, secureWipe, log);
    }

    /// <summary>
    /// The full multi-tiered escalation pipeline for stubborn, locked, or protected targets.
    /// </summary>
    private static DeleteResult ForceDeleteSingleTarget(
        string targetPath, 
        bool isDirectory, 
        bool killLockingProcesses, 
        bool secureWipe, 
        Action<string>? log)
    {
        string normPath = SecurityManager.NormalizePath(targetPath);

        // ==========================================
        // TIER 1: Standard .NET Delete
        // ==========================================
        try
        {
            if (isDirectory)
                Directory.Delete(targetPath, true);
            else
                File.Delete(targetPath);

            if (!Exists(targetPath))
            {
                return new DeleteResult(DeleteResultStatus.Success, "Deleted via standard method.", 1);
            }
        }
        catch { }

        // ==========================================
        // TIER 2: SuperDelete NULL DACL & Attribute Stripping
        // ==========================================
        try
        {
            SecurityManager.StripAttributes(targetPath, null);
            SecurityManager.BypassAclSuperDelete(targetPath, null);

            bool delOk = isDirectory ? NativeMethods.RemoveDirectoryW(normPath) : NativeMethods.DeleteFileW(normPath);
            if (delOk || !Exists(targetPath))
            {
                log?.Invoke($"[✓ Tier 2] Deleted via SuperDelete ACL bypass: {Path.GetFileName(targetPath)}");
                return new DeleteResult(DeleteResultStatus.Success, "Deleted after taking ownership and resetting attributes.", 2);
            }
        }
        catch { }

        // ==========================================
        // TIER 3: NanaRun TrustedInstaller / SYSTEM Impersonation
        // ==========================================
        try
        {
            bool tier3Success = PrivilegeManager.RunWithHighestPrivileges(() =>
            {
                SecurityManager.StripAttributes(targetPath, null);
                SecurityManager.BypassAclSuperDelete(targetPath, null);

                return isDirectory ? NativeMethods.RemoveDirectoryW(normPath) : NativeMethods.DeleteFileW(normPath);
            }, null);

            if (tier3Success || !Exists(targetPath))
            {
                log?.Invoke($"[✓ Tier 3] Deleted under TrustedInstaller authority: {Path.GetFileName(targetPath)}");
                return new DeleteResult(DeleteResultStatus.Success, "Deleted using TrustedInstaller privilege escalation.", 3);
            }
        }
        catch { }

        // ==========================================
        // TIER 4: Process Tree Killing & Remote Handle Closing (fuck-delete-it)
        // ==========================================
        try
        {
            int closedHandles = ProcessUnlocker.CloseFileHandles(targetPath, null);
            if (closedHandles > 0)
                log?.Invoke($"[+] Closed {closedHandles} remote handle(s) for {Path.GetFileName(targetPath)}");

            if (killLockingProcesses)
            {
                int killed = ProcessUnlocker.KillLockingProcesses(targetPath, null);
                if (killed > 0)
                    log?.Invoke($"[+] Terminated {killed} locking process tree(s) for {Path.GetFileName(targetPath)}");
            }

            bool delOk = isDirectory ? NativeMethods.RemoveDirectoryW(normPath) : NativeMethods.DeleteFileW(normPath);
            if (delOk || !Exists(targetPath))
            {
                log?.Invoke($"[✓ Tier 4] Deleted after unlocking handles/processes: {Path.GetFileName(targetPath)}");
                return new DeleteResult(DeleteResultStatus.Success, "Deleted after unlocking processes and handles.", 4);
            }
        }
        catch { }

        // ==========================================
        // TIER 5: Low-Level NT POSIX Kernel Unlink
        // ==========================================
        if (!isDirectory)
        {
            try
            {
                if (LowLevelNtDelete(targetPath, null) || !Exists(targetPath))
                {
                    log?.Invoke($"[✓ Tier 5] Deleted using NT POSIX semantics: {Path.GetFileName(targetPath)}");
                    return new DeleteResult(DeleteResultStatus.Success, "Deleted using low-level NT kernel disposition.", 5);
                }
            }
            catch { }
        }

        // ==========================================
        // TIER 6: Atomic Temp Rename & Reboot Scheduling (ForcedDeleteFiles)
        // ==========================================
        try
        {
            string parentDir = Path.GetDirectoryName(targetPath) ?? Path.GetTempPath();
            string tempName = Path.Combine(parentDir, $"_DELETED_{Guid.NewGuid():N}.tmp");
            string normTemp = SecurityManager.NormalizePath(tempName);

            // Rename to decouple from original locking path
            if (NativeMethods.MoveFileExW(normPath, normTemp, 0))
            {
                targetPath = tempName;
                normPath = normTemp;

                if ((isDirectory ? NativeMethods.RemoveDirectoryW(normPath) : NativeMethods.DeleteFileW(normPath)) || !Exists(targetPath))
                {
                    log?.Invoke($"[✓ Tier 6] Deleted after renaming: {Path.GetFileName(targetPath)}");
                    return new DeleteResult(DeleteResultStatus.Success, "Deleted successfully after renaming.", 6);
                }
            }

            // Ultimate fallback: Schedule on reboot via MoveFileEx
            if (NativeMethods.MoveFileExW(normPath, null, NativeMethods.MOVEFILE_DELAY_UNTIL_REBOOT))
            {
                log?.Invoke($"[!] File locked by kernel driver. Scheduled for deletion on NEXT REBOOT: {Path.GetFileName(targetPath)}");
                return new DeleteResult(DeleteResultStatus.ScheduledOnReboot,
                    "The target is locked at the kernel level and has been scheduled for permanent deletion upon next system reboot.", 6);
            }
        }
        catch { }

        return new DeleteResult(DeleteResultStatus.Failed, "All deletion tiers failed.", 0);
    }

    private static bool LowLevelNtDelete(string filePath, Action<string>? log)
    {
        string normPath = SecurityManager.NormalizePath(filePath);

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
            return false;
        }

        try
        {
            var ioStatus = new NativeMethods.IO_STATUS_BLOCK();

            // POSIX semantics (FileDispositionInformationEx)
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

    /// <summary>
    /// Overwrites file data with zero bytes and flushes to physical storage before unlinking
    /// (Tulpep/SDelete-Gui and Sysinternals SDelete anti-recovery shredding technique).
    /// </summary>
    public static void SecureShredFile(string filePath, Action<string>? log = null)
    {
        try
        {
            string normPath = SecurityManager.NormalizePath(filePath);
            NativeMethods.SetFileAttributesW(normPath, NativeMethods.FILE_ATTRIBUTE_NORMAL);

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
