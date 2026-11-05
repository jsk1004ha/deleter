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
    public bool IsDeleted { get; set; }
    public bool IsScheduled { get; set; }
    public SecureWipeReport SecureWipe { get; set; } = new();
    public string StatusText => SecureWipe.HasFailures
        ? (IsScheduled ? "On reboot; wipe failed" : IsDeleted ? "Deleted; wipe failed" : "Partial; wipe failed")
        : IsScheduled ? "On reboot" : IsSuccessful ? "Deleted" : "Partial";
    public string Details { get; set; } = "";

    internal void ApplyResult(DeleteResult result)
    {
        IsSuccessful = !result.HasWarnings;
        IsDeleted = result.Success;
        IsScheduled = result.ScheduledForReboot;
        SecureWipe = result.SecureWipe;
        Details = result.Message;
    }
}
