using System.Collections.Concurrent;

namespace ForceDeleter;

public enum SecureWipeFileStatus
{
    Completed,
    SkippedReparsePoint,
    Failed
}

public sealed record SecureWipeFileResult(SecureWipeFileStatus Status, string Message);

/// <summary>Content overwrite results, independent of whether the selected path was deleted.</summary>
public sealed class SecureWipeReport
{
    public bool Requested { get; init; }
    public int OverwrittenFiles { get; init; }
    public int SkippedLinks { get; init; }
    public int FailedFiles { get; init; }
    public int UnverifiedDirectories { get; init; }
    public IReadOnlyList<string> Failures { get; init; } = Array.Empty<string>();
    public bool HasFailures => FailedFiles > 0 || UnverifiedDirectories > 0;

    public string Describe()
    {
        if (!Requested) return string.Empty;
        string summary = $"Secure overwrite: {OverwrittenFiles} file(s) completed, {SkippedLinks} link(s) skipped, " +
            $"{FailedFiles} file(s) failed, {UnverifiedDirectories} directory scan(s) unverified.";
        return Failures.Count == 0 ? summary : summary + Environment.NewLine + string.Join(Environment.NewLine, Failures);
    }
}

/// <summary>One entry per selected path, shared by parallel workers and later deletion tiers.</summary>
internal sealed class SecureWipeTracker
{
    // Traversal and retries preserve the original spelling. Windows also supports
    // case-sensitive directories, where a.txt and A.txt are different objects;
    // folding case here could skip an overwrite or erase another scan's failure.
    private readonly ConcurrentDictionary<string, SecureWipeFileResult> _files = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _scanFailures = new(StringComparer.Ordinal);

    public void Overwrite(string path, Action<string>? log)
    {
        string key = SecurityManager.ToLongPath(path);
        if (_files.TryGetValue(key, out var previous) && previous.Status != SecureWipeFileStatus.Failed)
            return;

        // A successful retry replaces the earlier failure; deletion itself never clears it.
        _files[key] = ForceDeleteEngine.SecureShredFile(path, log);
    }

    public void SkipLink(string path) => _files[SecurityManager.ToLongPath(path)] =
        new SecureWipeFileResult(SecureWipeFileStatus.SkippedReparsePoint, "Link target was not overwritten.");

    public void ScanFailed(string path, string message) => _scanFailures[SecurityManager.ToLongPath(path)] = message;

    public void ScanCompleted(string path) => _scanFailures.TryRemove(SecurityManager.ToLongPath(path), out _);

    public void Renamed(string oldPath, string newPath)
    {
        string oldKey = SecurityManager.ToLongPath(oldPath).TrimEnd('\\');
        string newKey = SecurityManager.ToLongPath(newPath).TrimEnd('\\');
        string prefix = oldKey + "\\";
        // Directory renames occur before/after workers; a file worker renames only its own entries.
        foreach (var entry in _files.ToArray())
        {
            if (entry.Key.Equals(oldKey, StringComparison.Ordinal) || entry.Key.StartsWith(prefix, StringComparison.Ordinal))
            {
                _files.TryRemove(entry.Key, out _);
                _files[newKey + entry.Key[oldKey.Length..]] = entry.Value;
            }
        }
        foreach (var entry in _scanFailures.ToArray())
        {
            if (entry.Key.Equals(oldKey, StringComparison.Ordinal) || entry.Key.StartsWith(prefix, StringComparison.Ordinal))
            {
                _scanFailures.TryRemove(entry.Key, out _);
                _scanFailures[newKey + entry.Key[oldKey.Length..]] = entry.Value;
            }
        }
    }

    public SecureWipeReport Snapshot()
    {
        var files = _files.ToArray();
        var scans = _scanFailures.ToArray();
        return new SecureWipeReport
        {
            Requested = true,
            OverwrittenFiles = files.Count(entry => entry.Value.Status == SecureWipeFileStatus.Completed),
            SkippedLinks = files.Count(entry => entry.Value.Status == SecureWipeFileStatus.SkippedReparsePoint),
            FailedFiles = files.Count(entry => entry.Value.Status == SecureWipeFileStatus.Failed),
            UnverifiedDirectories = scans.Length,
            Failures = files.Where(entry => entry.Value.Status == SecureWipeFileStatus.Failed)
                .Select(entry => $"{entry.Key}: {entry.Value.Message}")
                .Concat(scans.Select(entry => $"{entry.Key}: {entry.Value}"))
                .OrderBy(message => message, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }
}
