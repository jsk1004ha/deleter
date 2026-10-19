using System;
using System.Collections.Generic;
using System.IO;

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

public class TargetItem
{
    public string FullPath { get; set; }
    public string Name { get; set; }
    public bool IsDirectory { get; set; }
    public TargetFileType FileType { get; set; }
    public ItemLockStatus Status { get; set; } = ItemLockStatus.Checking;
    public List<LockingProcessInfo> LockingProcesses { get; set; } = [];
    public long FileSizeBytes { get; set; }

    public TargetItem(string path)
    {
        FullPath = path.Trim('\"', ' ');
        Name = Path.GetFileName(FullPath);
        if (string.IsNullOrEmpty(Name))
            Name = FullPath; // Root drive like C:\

        IsDirectory = Directory.Exists(FullPath);

        if (IsDirectory)
        {
            FileType = TargetFileType.Folder;
        }
        else
        {
            string ext = Path.GetExtension(FullPath).ToLowerInvariant();
            FileType = ext switch
            {
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" or ".ico" => TargetFileType.Image,
                ".txt" or ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".csv" or ".json" or ".xml" or ".md" => TargetFileType.Document,
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => TargetFileType.Archive,
                ".exe" or ".dll" or ".sys" or ".msi" => TargetFileType.Executable,
                _ => TargetFileType.Generic
            };

            try
            {
                var fi = new FileInfo(FullPath);
                FileSizeBytes = fi.Length;
            }
            catch
            {
                FileSizeBytes = 0;
            }
        }
    }
}
