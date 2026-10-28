using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ForceDeleter;

public enum TargetFileType
{
    Document,
    Folder,
    Image,
    Archive,
    Executable,
    Generic
}

public enum ItemLockStatus
{
    Checking,
    Ready,      // Green dot: unlocked, ready to delete
    Locked,     // Orange/Yellow dot: locked by one or more processes
    Protected   // Red dot: system or permission denied
}

/// <summary>
/// 삭제 대상 파일/폴더 모델.
/// 폴더 추가 시 UI 스레드를 블로킹하지 않고 백그라운드에서 Win32 FindFirstFileExW로
/// 총 용량과 하위 파일 수를 초고속 비동기 집계합니다.
/// </summary>
public class TargetItem
{
    public string FullPath { get; set; }
    public string Name { get; set; }
    public bool IsDirectory { get; set; }
    public TargetFileType FileType { get; set; }
    public ItemLockStatus Status { get; set; } = ItemLockStatus.Checking;
    public List<LockingProcessInfo> LockingProcesses { get; set; } = [];
    public long FileSizeBytes { get; set; }
    public int FileCount { get; set; } = 1;
    public bool IsSizeCalculated { get; private set; }

    public TargetItem(string path)
    {
        FullPath = Path.GetFullPath(path.Trim('\"', ' '));
        Name = Path.GetFileName(FullPath);
        if (string.IsNullOrEmpty(Name))
            Name = FullPath; // Root drive like C:\

        IsDirectory = Directory.Exists(FullPath);

        if (IsDirectory)
        {
            FileType = TargetFileType.Folder;
            FileSizeBytes = 0;
            FileCount = 0;
            IsSizeCalculated = false;
        }
        else
        {
            FileType = ClassifyExtension(Path.GetExtension(FullPath));
            try
            {
                var fi = new FileInfo(FullPath);
                FileSizeBytes = fi.Length;
            }
            catch
            {
                FileSizeBytes = 0;
            }
            FileCount = 1;
            IsSizeCalculated = true;
        }
    }

    /// <summary>
    /// 디렉토리의 총 파일 수와 용량을 백그라운드에서 비동기 집계한 뒤 완료 콜백을 호출합니다.
    /// (심볼릭 링크/정션은 무한 루프 및 외부 트리 탐색 방지를 위해 건너뜀)
    /// </summary>
    public Task PopulateDirectoryMetricsAsync(Action? onCompleted = null, CancellationToken cancellationToken = default)
    {
        if (!IsDirectory || IsSizeCalculated)
        {
            onCompleted?.Invoke();
            return Task.CompletedTask;
        }

        return Task.Run(() =>
        {
            long totalSize = 0;
            int totalFiles = 0;
            AccumulateDirectoryMetrics(FullPath, ref totalSize, ref totalFiles, cancellationToken);

            FileSizeBytes = totalSize;
            FileCount = Math.Max(1, totalFiles);
            IsSizeCalculated = true;
            onCompleted?.Invoke();
        }, cancellationToken);
    }

    private static void AccumulateDirectoryMetrics(
        string dirPath,
        ref long totalSize,
        ref int totalFiles,
        CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return;

        string longDir = SecurityManager.ToLongPath(dirPath);
        string pattern = longDir.EndsWith('\\') ? longDir + "*" : longDir + "\\*";

        IntPtr hFind = NativeMethods.FindFirstFileExW(
            pattern,
            NativeMethods.FindExInfoBasic,
            out NativeMethods.WIN32_FIND_DATA data,
            NativeMethods.FindExSearchNameMatch,
            IntPtr.Zero,
            NativeMethods.FIND_FIRST_EX_LARGE_FETCH);

        if (hFind == NativeMethods.INVALID_HANDLE_VALUE || hFind == IntPtr.Zero)
            return;

        try
        {
            do
            {
                if (token.IsCancellationRequested)
                    break;

                string name = data.cFileName;
                if (name == "." || name == "..")
                    continue;

                bool isDir = (data.dwFileAttributes & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) != 0;
                bool isReparse = (data.dwFileAttributes & NativeMethods.FILE_ATTRIBUTE_REPARSE_POINT) != 0;

                if (isDir)
                {
                    if (!isReparse)
                    {
                        AccumulateDirectoryMetrics(Path.Combine(dirPath, name), ref totalSize, ref totalFiles, token);
                    }
                }
                else
                {
                    totalFiles++;
                    long fileSize = ((long)data.nFileSizeHigh << 32) | (uint)data.nFileSizeLow;
                    totalSize += fileSize;
                }
            }
            while (NativeMethods.FindNextFileW(hFind, out data));
        }
        finally
        {
            NativeMethods.FindClose(hFind);
        }
    }

    private static TargetFileType ClassifyExtension(string ext)
    {
        return ext.ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" or ".ico" => TargetFileType.Image,
            ".txt" or ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".csv" or ".json" or ".xml" or ".md" => TargetFileType.Document,
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => TargetFileType.Archive,
            ".exe" or ".dll" or ".sys" or ".msi" => TargetFileType.Executable,
            _ => TargetFileType.Generic
        };
    }
}
