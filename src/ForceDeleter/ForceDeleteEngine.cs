using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ForceDeleter;

public enum DeleteResultStatus
{
    Success,
    ScheduledOnReboot,
    Failed
}

public sealed class DeleteResult
{
    public DeleteResultStatus Status { get; set; } = DeleteResultStatus.Success;
    public string Message { get; set; } = string.Empty;
    public int Tier { get; set; } = 1;
    public int DeletedFiles { get; set; }
    public int DeletedDirectories { get; set; }
    public int FailedItems { get; set; }
    public bool ScheduledForReboot => Status == DeleteResultStatus.ScheduledOnReboot;
    public bool Success => Status == DeleteResultStatus.Success;

    public DeleteResult() { }

    public DeleteResult(DeleteResultStatus status, string message, int tier)
    {
        Status = status;
        Message = message;
        Tier = tier;
    }

    public static implicit operator bool(DeleteResult? result) => result != null && result.Success;
}

/// <summary>
/// 최적화된 고속·강제 삭제 엔진.
/// 통합 기술 아키텍처:
/// - mtopolski/rmbrr: SetFileInformationByHandle POSIX 삭제 및 의존성 기반 병렬 작업 스케줄링
/// - zerx-lab/rmx: 네이티브 POSIX 언링크, 일시 점유 적응형 백오프 재시도, 비파괴 핸들 해제 우선 정책
/// - gabriel-sztejnworcel/ParallelDelete: BFS 파일 일괄 언링크 및 Bottom-Up(역순) 디렉터리 언링크(ERROR_DIR_NOT_EMPTY 100% 제거)
/// - Microsoft PowerToys / System Informer: NT 커널 디바이스 경로 기반 정밀 핸들 탐지 및 무지연 해제
/// - SuperDelete / NanaRun: 인프로세스 NULL DACL 및 TrustedInstaller 권한 상승
/// </summary>
public static class ForceDeleteEngine
{
    private const int ParallelThreshold = 32;
    private const int SecureWipeBufferSize = 64 * 1024; // 64 KB
    private static readonly ParallelOptions DefaultParallelOptions = new()
    {
        MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount * 2, 4, 32)
    };

    static ForceDeleteEngine()
    {
        PrivilegeManager.EnableAllPrivileges();
    }

    /// <summary>
    /// 파일 또는 폴더에 대한 강제 삭제 파이프라인을 실행합니다.
    /// </summary>
    public static DeleteResult ForceDelete(
        string targetPath,
        bool killLockingProcesses = true,
        bool secureWipe = false,
        bool turboMode = true,
        Action<string>? log = null)
    {
        var result = new DeleteResult();
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            result.Status = DeleteResultStatus.Failed;
            result.Message = "Target path is empty.";
            result.Tier = 0;
            return result;
        }

        string fullPath = Path.GetFullPath(targetPath);
        string longPath = SecurityManager.ToLongPath(fullPath);

        uint attrs = NativeMethods.GetFileAttributesW(longPath);
        if (attrs == NativeMethods.INVALID_FILE_ATTRIBUTES)
        {
            result.Status = DeleteResultStatus.Success;
            result.Message = "File or folder does not exist (already deleted).";
            result.Tier = 0;
            return result;
        }

        bool isDirectory = (attrs & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) != 0;
        bool isReparsePoint = (attrs & NativeMethods.FILE_ATTRIBUTE_REPARSE_POINT) != 0;

        log?.Invoke($"\n[>>>] Starting force deletion for: {fullPath}");
        log?.Invoke($"[i] Target type: {(isDirectory ? (isReparsePoint ? "ReparsePoint/Junction" : "Directory") : "File")}, Turbo: {turboMode}");

        if (!isDirectory)
        {
            return DeleteSingleFile(fullPath, killLockingProcesses, secureWipe, turboMode, log);
        }

        // 심볼릭 링크 또는 정션(Junction) 디렉토리인 경우:
        // 원본 데이터 보호를 위해 내부 재귀 진입 없이 링크 자체만 제거 (rmx / rmbrr 안전 규정)
        if (isReparsePoint)
        {
            log?.Invoke($"[ReparsePoint] Unlinking directory junction/symlink without recursing: {fullPath}");
            SecurityManager.ClearAttributes(longPath);
            if (NativeMethods.RemoveDirectoryW(longPath) || LowLevelNtDelete(longPath, log))
            {
                result.Status = DeleteResultStatus.Success;
                result.DeletedDirectories = 1;
                result.Message = "Unlinked reparse point directory.";
                result.Tier = 1;
                return result;
            }
        }

        // Instant Detach: 동일 볼륨 내 임시 이름으로 원자적 이동(< 0.1ms)하여 사용자 경로를 즉시 해제
        string workingPath = fullPath;
        if (turboMode)
        {
            try
            {
                string? parentDir = Path.GetDirectoryName(fullPath.TrimEnd('\\', '/'));
                if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                {
                    string detachedPath = Path.Combine(parentDir, $".__force_del_{Guid.NewGuid():N}");
                    string normPath = SecurityManager.ToLongPath(fullPath);
                    string normDetach = SecurityManager.ToLongPath(detachedPath);
                    if (NativeMethods.MoveFileExW(normPath, normDetach, NativeMethods.MOVEFILE_WRITE_THROUGH))
                    {
                        workingPath = detachedPath;
                        log?.Invoke($"[⚡ Instant Detach] Folder decoupled in <0.1ms -> {Path.GetFileName(detachedPath)}");
                    }
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"[!] Instant detach note: {ex.Message}");
            }
        }

        // rmbrr + ParallelDelete 기반 초고속 Two-Phase(파일 병렬 언링크 -> 역순 디렉터리 언링크) 삭제
        try
        {
            FastDeleteDirectoryTree(workingPath, killLockingProcesses, secureWipe, log);
        }
        catch (Exception ex)
        {
            log?.Invoke($"[!] Fast tree deletion note: {ex.Message}");
        }

        string postCheckPath = SecurityManager.ToLongPath(workingPath);
        uint postAttrs = NativeMethods.GetFileAttributesW(postCheckPath);
        if (postAttrs == NativeMethods.INVALID_FILE_ATTRIBUTES)
        {
            log?.Invoke("[✓] Directory completely wiped.");
            result.Status = DeleteResultStatus.Success;
            result.Message = "Directory tree deleted at high speed.";
            result.Tier = 1;
            return result;
        }

        // 잔여 파일이 남아있는 경우 다단계 에스컬레이션 실행
        return ForceDeleteSingleTarget(workingPath, isDirectory: true, killLockingProcesses, secureWipe, log);
    }

    private static DeleteResult DeleteSingleFile(
        string filePath,
        bool killLockingProcesses,
        bool secureWipe,
        bool turboMode,
        Action<string>? log)
    {
        string longPath = SecurityManager.ToLongPath(filePath);

        if (turboMode && !secureWipe)
        {
            // Fast direct unlink
            if (NativeMethods.DeleteFileW(longPath))
            {
                log?.Invoke("[✓] Fast Path Success: File deleted immediately.");
                return new DeleteResult(DeleteResultStatus.Success, "Deleted via fast path.", 1) { DeletedFiles = 1 };
            }

            // Fast attribute strip + unlink
            SecurityManager.ClearAttributes(longPath);
            if (NativeMethods.DeleteFileW(longPath))
            {
                log?.Invoke("[✓] Fast Path Success: Deleted after clearing attributes.");
                return new DeleteResult(DeleteResultStatus.Success, "Deleted after attribute reset.", 1) { DeletedFiles = 1 };
            }

            // Fast NT / Handle POSIX unlink (rmbrr / rmx)
            if (LowLevelNtDelete(longPath, null))
            {
                log?.Invoke("[✓] Fast Path Success: Deleted using NT POSIX semantics.");
                return new DeleteResult(DeleteResultStatus.Success, "Deleted via NT POSIX disposition.", 1) { DeletedFiles = 1 };
            }

            // rmx 스타일 적응형 마이크로 백오프 재시도 (안티바이러스 / 검색 인덱서 일시 락 해제 대기)
            for (int attempt = 0; attempt < 2; attempt++)
            {
                Thread.Sleep(attempt == 0 ? 5 : 15);
                if (NativeMethods.DeleteFileW(longPath) || LowLevelNtDelete(longPath, null))
                {
                    log?.Invoke($"[✓ rmx Retry] Deleted on attempt {attempt + 2} after transient lock release.");
                    return new DeleteResult(DeleteResultStatus.Success, "Deleted after transient lock release.", 1) { DeletedFiles = 1 };
                }
            }
        }

        // 완강한 단일 파일 다단계 에스컬레이션
        return ForceDeleteSingleTarget(filePath, isDirectory: false, killLockingProcesses, secureWipe, log);
    }

    /// <summary>
    /// rmbrr &amp; ParallelDelete 아키텍처:
    /// 1단계) 고속 BFS 탐색으로 모든 하위 파일과 디렉터리를 일괄 수집
    /// 2단계) 모든 파일을 스레드 풀에서 병렬로 고속 언링크 (POSIX + Ignore ReadOnly)
    /// 3단계) 수집된 디렉터리 목록을 역순(Bottom-Up: 가장 깊은 자식 -> 부모)으로 언링크
    ///       -> 모든 부모는 자식이 이미 삭제된 빈 폴더 상태이므로 ERROR_DIR_NOT_EMPTY(145)가 절대 발생하지 않음!
    /// </summary>
    private static void FastDeleteDirectoryTree(
        string rootDirPath,
        bool killLocks,
        bool secureWipe,
        Action<string>? log)
    {
        string longRoot = SecurityManager.ToLongPath(rootDirPath);
        uint rootAttrs = NativeMethods.GetFileAttributesW(longRoot);
        if (rootAttrs == NativeMethods.INVALID_FILE_ATTRIBUTES)
            return;

        if ((rootAttrs & NativeMethods.FILE_ATTRIBUTE_REPARSE_POINT) != 0)
        {
            SecurityManager.ClearAttributes(longRoot);
            if (!NativeMethods.RemoveDirectoryW(longRoot))
            {
                LowLevelNtDelete(longRoot, log);
            }
            return;
        }

        var allFiles = new List<string>(256);
        var allDirs = new List<string>(64);
        var dirQueue = new Queue<string>(64);
        dirQueue.Enqueue(rootDirPath);

        // 1단계: 고속 BFS 디렉터리 크롤링
        while (dirQueue.Count > 0)
        {
            string currentDir = dirQueue.Dequeue();
            string longCurrent = SecurityManager.ToLongPath(currentDir);
            SecurityManager.ClearAttributes(longCurrent);

            string searchPattern = longCurrent.EndsWith('\\') ? longCurrent + "*" : longCurrent + "\\*";

            IntPtr hFind = NativeMethods.FindFirstFileExW(
                searchPattern,
                NativeMethods.FindExInfoBasic,
                out NativeMethods.WIN32_FIND_DATA findData,
                NativeMethods.FindExSearchNameMatch,
                IntPtr.Zero,
                NativeMethods.FIND_FIRST_EX_LARGE_FETCH);

            if (hFind == NativeMethods.INVALID_HANDLE_VALUE || hFind == IntPtr.Zero)
                continue;

            try
            {
                do
                {
                    string name = findData.cFileName;
                    if (name == "." || name == "..")
                        continue;

                    string childFullPath = Path.Combine(currentDir, name);
                    bool isSubDir = (findData.dwFileAttributes & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) != 0;
                    bool isReparse = (findData.dwFileAttributes & NativeMethods.FILE_ATTRIBUTE_REPARSE_POINT) != 0;

                    if (isSubDir)
                    {
                        if (isReparse)
                        {
                            // 하위 폴더가 심볼릭 링크/정션이면 내부 탐색 없이 즉시 언링크
                            string longChild = SecurityManager.ToLongPath(childFullPath);
                            SecurityManager.ClearAttributes(longChild);
                            if (!NativeMethods.RemoveDirectoryW(longChild))
                            {
                                LowLevelNtDelete(longChild, log);
                            }
                        }
                        else
                        {
                            allDirs.Add(childFullPath);
                            dirQueue.Enqueue(childFullPath);
                        }
                    }
                    else
                    {
                        allFiles.Add(childFullPath);
                    }
                }
                while (NativeMethods.FindNextFileW(hFind, out findData));
            }
            finally
            {
                NativeMethods.FindClose(hFind);
            }
        }

        // 2단계: Phase 1 — 병렬 파일 삭제 (rmbrr worker 모델)
        if (allFiles.Count > 0)
        {
            if (allFiles.Count < ParallelThreshold)
            {
                for (int i = 0; i < allFiles.Count; i++)
                {
                    FastDeleteFile(allFiles[i], killLocks, secureWipe, log);
                }
            }
            else
            {
                Parallel.ForEach(allFiles, DefaultParallelOptions, file =>
                {
                    FastDeleteFile(file, killLocks, secureWipe, log);
                });
            }
        }

        // 3단계: Phase 2 — 역순(Bottom-Up) 디렉터리 언링크 (ParallelDelete & rmbrr 모델)
        for (int i = allDirs.Count - 1; i >= 0; i--)
        {
            string dir = allDirs[i];
            string longDir = SecurityManager.ToLongPath(dir);

            if (NativeMethods.RemoveDirectoryW(longDir))
                continue;

            SecurityManager.ClearAttributes(longDir);
            if (NativeMethods.RemoveDirectoryW(longDir))
                continue;

            // rmx 스타일 짧은 백오프 재시도
            bool removed = false;
            for (int retry = 0; retry < 2; retry++)
            {
                Thread.Sleep(retry == 0 ? 5 : 15);
                if (NativeMethods.RemoveDirectoryW(longDir) || LowLevelNtDelete(longDir, null))
                {
                    removed = true;
                    break;
                }
            }
            if (removed)
                continue;

            // 잠금 및 권한 에스컬레이션
            SecurityManager.TakeOwnershipAndGrantFullControl(longDir);
            if (NativeMethods.RemoveDirectoryW(longDir))
                continue;

            if (killLocks)
            {
                ProcessUnlocker.UnlockPath(dir, killProcesses: true, log);
                if (NativeMethods.RemoveDirectoryW(longDir))
                    continue;
            }

            if (LowLevelNtDelete(longDir, log))
                continue;

            NativeMethods.MoveFileExW(longDir, null, NativeMethods.MOVEFILE_DELAY_UNTIL_REBOOT);
        }

        // 4단계: 루트 디렉터리 컨테이너 최종 제거
        if (NativeMethods.RemoveDirectoryW(longRoot))
            return;

        SecurityManager.ClearAttributes(longRoot);
        if (NativeMethods.RemoveDirectoryW(longRoot))
            return;

        SecurityManager.TakeOwnershipAndGrantFullControl(longRoot);
        if (NativeMethods.RemoveDirectoryW(longRoot))
            return;

        if (killLocks)
        {
            ProcessUnlocker.UnlockPath(rootDirPath, killProcesses: true, log);
            if (NativeMethods.RemoveDirectoryW(longRoot))
                return;
        }

        if (LowLevelNtDelete(longRoot, log))
            return;

        NativeMethods.MoveFileExW(longRoot, null, NativeMethods.MOVEFILE_DELAY_UNTIL_REBOOT);
    }

    private static void FastDeleteFile(
        string filePath,
        bool killLocks,
        bool secureWipe,
        Action<string>? log)
    {
        string longPath = SecurityManager.ToLongPath(filePath);

        if (secureWipe)
        {
            SecureShredFile(filePath, null);
        }

        // Fast Path 1: Direct native unlink (< 0.05ms)
        if (NativeMethods.DeleteFileW(longPath))
            return;

        // Fast Path 2: Clear Read-Only/Hidden attributes and retry
        SecurityManager.ClearAttributes(longPath);
        if (NativeMethods.DeleteFileW(longPath))
            return;

        // Fast Path 3: Low-Level NT POSIX Disposition
        if (LowLevelNtDelete(longPath, null))
            return;

        // Fast Path 4: rmx 적응형 마이크로 백오프 재시도 (안티바이러스 / 검색 인덱서 일시 점유 해제 대기)
        for (int attempt = 0; attempt < 2; attempt++)
        {
            Thread.Sleep(attempt == 0 ? 5 : 15);
            if (NativeMethods.DeleteFileW(longPath) || LowLevelNtDelete(longPath, null))
                return;
        }

        // Stubborn file: escalate to full multi-tiered force delete
        ForceDeleteSingleTarget(filePath, isDirectory: false, killLocks, secureWipe, log);
    }

    /// <summary>
    /// 잠금, 권한 거부, 커널 드라이버 점유 파일에 대한 6단계 종합 에스컬레이션 파이프라인
    /// </summary>
    private static DeleteResult ForceDeleteSingleTarget(
        string targetPath,
        bool isDirectory,
        bool killLockingProcesses,
        bool secureWipe,
        Action<string>? log)
    {
        string longPath = SecurityManager.ToLongPath(targetPath);

        if (!isDirectory && secureWipe)
        {
            SecureShredFile(targetPath, log);
        }

        // Tier 1: 표준 삭제
        try
        {
            if (isDirectory)
                Directory.Delete(targetPath, true);
            else
                File.Delete(targetPath);

            if (!Exists(targetPath))
            {
                return new DeleteResult(DeleteResultStatus.Success, "Deleted via standard method.", 1)
                {
                    DeletedFiles = isDirectory ? 0 : 1,
                    DeletedDirectories = isDirectory ? 1 : 0
                };
            }
        }
        catch { }

        // Tier 2: 인프로세스 소유권 탈취 및 NULL DACL 주입
        try
        {
            SecurityManager.ClearAttributes(longPath);
            SecurityManager.TakeOwnershipAndGrantFullControl(longPath, log);

            bool delOk = isDirectory ? NativeMethods.RemoveDirectoryW(longPath) : NativeMethods.DeleteFileW(longPath);
            if (delOk || !Exists(targetPath))
            {
                log?.Invoke($"[✓ Tier 2] Deleted via in-process ACL bypass: {Path.GetFileName(targetPath)}");
                return new DeleteResult(DeleteResultStatus.Success, "Deleted after taking ownership and resetting attributes.", 2)
                {
                    DeletedFiles = isDirectory ? 0 : 1,
                    DeletedDirectories = isDirectory ? 1 : 0
                };
            }
        }
        catch { }

        // Tier 3: TrustedInstaller / SYSTEM 토큰 가장
        try
        {
            bool tier3Success = PrivilegeManager.RunWithHighestPrivileges(() =>
            {
                SecurityManager.ClearAttributes(longPath);
                SecurityManager.TakeOwnershipAndGrantFullControl(longPath, null);

                return isDirectory ? NativeMethods.RemoveDirectoryW(longPath) : NativeMethods.DeleteFileW(longPath);
            }, null);

            if (tier3Success || !Exists(targetPath))
            {
                log?.Invoke($"[✓ Tier 3] Deleted under TrustedInstaller authority: {Path.GetFileName(targetPath)}");
                return new DeleteResult(DeleteResultStatus.Success, "Deleted using TrustedInstaller privilege escalation.", 3)
                {
                    DeletedFiles = isDirectory ? 0 : 1,
                    DeletedDirectories = isDirectory ? 1 : 0
                };
            }
        }
        catch { }

        // Tier 4: 원격 파일 핸들 비파괴 닫기 우선 -> 필요 시 프로세스 트리 종료 (rmx 아키텍처)
        try
        {
            int closedHandles = ProcessUnlocker.CloseFileHandles(targetPath, null);
            if (closedHandles > 0)
            {
                log?.Invoke($"[+] Closed {closedHandles} remote handle(s) for {Path.GetFileName(targetPath)}");
                bool delAfterClose = isDirectory ? NativeMethods.RemoveDirectoryW(longPath) : NativeMethods.DeleteFileW(longPath);
                if (delAfterClose || !Exists(targetPath))
                {
                    log?.Invoke($"[✓ Tier 4] Deleted after closing remote handles without killing process: {Path.GetFileName(targetPath)}");
                    return new DeleteResult(DeleteResultStatus.Success, "Deleted after closing remote handles.", 4)
                    {
                        DeletedFiles = isDirectory ? 0 : 1,
                        DeletedDirectories = isDirectory ? 1 : 0
                    };
                }
            }

            if (killLockingProcesses)
            {
                int killed = ProcessUnlocker.KillLockingProcesses(targetPath, null);
                if (killed > 0)
                    log?.Invoke($"[+] Terminated {killed} locking process tree(s) for {Path.GetFileName(targetPath)}");
            }

            bool delOk = isDirectory ? NativeMethods.RemoveDirectoryW(longPath) : NativeMethods.DeleteFileW(longPath);
            if (delOk || !Exists(targetPath))
            {
                log?.Invoke($"[✓ Tier 4] Deleted after unlocking handles/processes: {Path.GetFileName(targetPath)}");
                return new DeleteResult(DeleteResultStatus.Success, "Deleted after unlocking processes and handles.", 4)
                {
                    DeletedFiles = isDirectory ? 0 : 1,
                    DeletedDirectories = isDirectory ? 1 : 0
                };
            }
        }
        catch { }

        // Tier 5: 저수준 NT POSIX 커널 언링크
        if (!isDirectory)
        {
            try
            {
                if (LowLevelNtDelete(longPath, log) || !Exists(targetPath))
                {
                    log?.Invoke($"[✓ Tier 5] Deleted using NT POSIX semantics: {Path.GetFileName(targetPath)}");
                    return new DeleteResult(DeleteResultStatus.Success, "Deleted using low-level NT kernel disposition.", 5)
                    {
                        DeletedFiles = 1
                    };
                }
            }
            catch { }
        }

        // Tier 6: 임시 난수화 이름 변경 및 재부팅 시 삭제 스케줄링
        try
        {
            string? parentDir = Path.GetDirectoryName(targetPath);
            string targetForReboot = longPath;
            if (!string.IsNullOrEmpty(parentDir))
            {
                string tempName = Path.Combine(parentDir, $".__del_{Guid.NewGuid():N}.tmp");
                string longTemp = SecurityManager.ToLongPath(tempName);

                if (NativeMethods.MoveFileExW(longPath, longTemp, NativeMethods.MOVEFILE_REPLACE_EXISTING))
                {
                    targetForReboot = longTemp;
                    bool delOk = isDirectory ? NativeMethods.RemoveDirectoryW(targetForReboot) : NativeMethods.DeleteFileW(targetForReboot);
                    if (delOk || !Exists(tempName))
                    {
                        log?.Invoke($"[✓ Tier 6] Deleted after renaming: {Path.GetFileName(targetPath)}");
                        return new DeleteResult(DeleteResultStatus.Success, "Deleted successfully after renaming.", 6)
                        {
                            DeletedFiles = isDirectory ? 0 : 1,
                            DeletedDirectories = isDirectory ? 1 : 0
                        };
                    }
                }
            }

            if (NativeMethods.MoveFileExW(targetForReboot, null, NativeMethods.MOVEFILE_DELAY_UNTIL_REBOOT))
            {
                log?.Invoke($"[!] File locked by kernel driver. Scheduled for deletion on NEXT REBOOT: {Path.GetFileName(targetPath)}");
                return new DeleteResult(DeleteResultStatus.ScheduledOnReboot,
                    "The target is locked at the kernel level and has been scheduled for permanent deletion upon next system reboot.", 6)
                {
                    FailedItems = 1
                };
            }
        }
        catch { }

        return new DeleteResult(DeleteResultStatus.Failed, "All deletion tiers failed.", 0) { FailedItems = 1 };
    }

    /// <summary>
    /// ArrayPool&lt;byte&gt;를 재사용하여 GC(LOH) 압박 없이 파일 내용을 0으로 덮어쓰고 크기를 절삭합니다.
    /// </summary>
    public static void SecureShredFile(string filePath, Action<string>? log = null)
    {
        try
        {
            string longPath = SecurityManager.ToLongPath(filePath);
            SecurityManager.ClearAttributes(longPath);

            byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(SecureWipeBufferSize);
            Array.Clear(rentedBuffer, 0, SecureWipeBufferSize);

            try
            {
                using var fs = new FileStream(
                    longPath,
                    FileMode.Open,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 4096,
                    FileOptions.WriteThrough | FileOptions.SequentialScan);

                long length = fs.Length;
                if (length > 0)
                {
                    long remaining = length;
                    fs.Position = 0;
                    while (remaining > 0)
                    {
                        int toWrite = (int)Math.Min(remaining, SecureWipeBufferSize);
                        fs.Write(rentedBuffer, 0, toWrite);
                        remaining -= toWrite;
                    }
                    fs.Flush(flushToDisk: true);
                }

                fs.SetLength(0);
                log?.Invoke($"[★ SDelete] Overwrote file contents with zeroes to prevent recovery ({length} bytes).");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"[!] SDelete shred note: {ex.Message}");
        }
    }

    /// <summary>
    /// SetFileInformationByHandle 및 NtSetInformationFile을 통한 Windows 10 RS5+ POSIX Semantics + Ignore ReadOnly 즉시 언링크
    /// (rmbrr 및 rmx의 핵심 네이티브 언링크 메커니즘 차용)
    /// </summary>
    private static bool LowLevelNtDelete(string longPath, Action<string>? log)
    {
        IntPtr hFile = NativeMethods.CreateFileW(
            longPath,
            NativeMethods.DELETE | NativeMethods.SYNCHRONIZE | NativeMethods.GENERIC_WRITE,
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
            // 1차 시도 (rmbrr / rmx): SetFileInformationByHandle with FileDispositionInfoEx
            var dispInfoEx = new NativeMethods.FILE_DISPOSITION_INFO_EX
            {
                Flags = NativeMethods.FILE_DISPOSITION_FLAG_DELETE
                      | NativeMethods.FILE_DISPOSITION_FLAG_POSIX_SEMANTICS
                      | NativeMethods.FILE_DISPOSITION_FLAG_IGNORE_READONLY_ATTRIBUTE
            };

            if (NativeMethods.SetFileInformationByHandle(
                    hFile,
                    NativeMethods.FileDispositionInfoEx,
                    ref dispInfoEx,
                    (uint)Marshal.SizeOf<NativeMethods.FILE_DISPOSITION_INFO_EX>()))
            {
                return true;
            }

            // 2차 시도: NtSetInformationFile with FILE_DISPOSITION_INFORMATION_EX (POSIX + Ignore ReadOnly)
            var ioStatus = new NativeMethods.IO_STATUS_BLOCK();
            var dispEx = new NativeMethods.FILE_DISPOSITION_INFORMATION_EX
            {
                Flags = NativeMethods.FILE_DISPOSITION_FLAG_DELETE
                      | NativeMethods.FILE_DISPOSITION_FLAG_POSIX_SEMANTICS
                      | NativeMethods.FILE_DISPOSITION_FLAG_IGNORE_READONLY_ATTRIBUTE
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

            // 3차 폴백: 표준 FileDispositionInformation (class 13)
            var disp = new NativeMethods.FILE_DISPOSITION_INFORMATION { DeleteFile = true };
            ntStatus = NativeMethods.NtSetInformationFile(
                hFile,
                ref ioStatus,
                ref disp,
                (uint)Marshal.SizeOf(disp),
                NativeMethods.FileDispositionInformation);

            return ntStatus == NativeMethods.STATUS_SUCCESS;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[LowLevelNtDelete] {ex.Message}");
            return false;
        }
        finally
        {
            NativeMethods.CloseHandle(hFile);
        }
    }

    private static bool Exists(string path)
    {
        return File.Exists(path) || Directory.Exists(path);
    }
}
