using System;

namespace ForceDeleter;

public class HistoryRecord
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public TargetFileType FileType { get; set; }
    public int FileCount { get; set; } = 1;
    public long TotalSizeBytes { get; set; }
    public bool IsSuccessful { get; set; } = true;
    public bool IsScheduled { get; set; }
    public string StatusText => IsScheduled ? "On reboot" : IsSuccessful ? "Deleted" : "Partial";
    public string Details { get; set; } = "";
}
