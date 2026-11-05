using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using ForceDeleter;

internal static class EngineTests
{
    private static int Main(string[] args)
    {
        CheckReportingModels();
        if (args.Contains("--report-only"))
        {
            Console.WriteLine("PASS: deletion/overwrite reporting and history serialization.");
            return 0;
        }
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("The deletion engine regression tests require Windows.");
            return 1;
        }

        string root = Path.Combine(Path.GetTempPath(), "ForceDeleter-EngineTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (bool turbo in new[] { false, true })
            {
                CheckFileLink(root, turbo);
                CheckNestedFileLink(root, turbo);
                CheckDirectoryLink(root, turbo);
                CheckDanglingLink(root, turbo);
                CheckWriteFailureDoesNotHideDeletion(root, turbo);
                CheckFolderWipeResults(root, turbo);
            }
            CheckOrdinaryFile(root);
            CheckMissingAndInvalidPaths(root);
            CheckOverwriteRetryAfterPermissionRecovery(root);
            CheckDirectoryFallbackAfterScanFailure(root);
            Console.WriteLine("PASS: link safety, overwrite outcomes, parallel aggregation, permission retries, directory fallback, and history reporting.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void CheckFileLink(string root, bool turbo)
    {
        string target = Path.Combine(root, $"outside-file-{turbo}.txt");
        string link = Path.Combine(root, $"file-link-{turbo}.txt");
        File.WriteAllText(target, "unselected original bytes");
        File.CreateSymbolicLink(link, target);

        // Also cover callers that invoke the public shred operation directly.
        SecureWipeFileResult shred = ForceDeleteEngine.SecureShredFile(link);
        Require(shred.Status == SecureWipeFileStatus.SkippedReparsePoint, "A file link was not reported as skipped.");
        Require(File.ReadAllText(target) == "unselected original bytes", "Direct shred followed the file link.");
        DeleteResult result = ForceDeleteEngine.ForceDelete(link, killLockingProcesses: false, secureWipe: true, turboMode: turbo);
        Require(result.Success, "The selected file link was not deleted.");
        Require(result.SecureWipe.SkippedLinks == 1 && !result.HasWarnings, "A safely skipped file link was reported as a failed overwrite.");
        Require(File.ReadAllText(target) == "unselected original bytes", "Secure deletion changed the file-link target.");
        RequireMissing(link);
    }

    private static void CheckNestedFileLink(string root, bool turbo)
    {
        string target = Path.Combine(root, $"outside-nested-{turbo}.txt");
        string folder = Path.Combine(root, $"selected-folder-{turbo}");
        File.WriteAllText(target, "outside selected directory");
        Directory.CreateDirectory(folder);
        File.CreateSymbolicLink(Path.Combine(folder, "linked.txt"), target);
        File.WriteAllText(Path.Combine(folder, "ordinary.txt"), "selected bytes");
        DeleteResult result = ForceDeleteEngine.ForceDelete(folder, killLockingProcesses: false, secureWipe: true, turboMode: turbo);
        Require(result.Success, "The selected directory was not deleted.");
        Require(result.SecureWipe.OverwrittenFiles == 1 && result.SecureWipe.SkippedLinks == 1 && !result.HasWarnings,
            "A directory did not aggregate its ordinary file and skipped file link.");
        Require(File.ReadAllText(target) == "outside selected directory", "Tree deletion overwrote an external file-link target.");
        RequireMissing(folder);
    }

    private static void CheckDirectoryLink(string root, bool turbo)
    {
        string target = Path.Combine(root, $"outside-directory-{turbo}");
        string link = Path.Combine(root, $"directory-link-{turbo}");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "original.txt"), "directory original");
        Directory.CreateSymbolicLink(link, target);
        DeleteResult result = ForceDeleteEngine.ForceDelete(link, killLockingProcesses: false, secureWipe: true, turboMode: turbo);
        Require(result.Success, "The directory link was not deleted.");
        Require(result.SecureWipe.SkippedLinks == 1 && !result.HasWarnings, "A directory link was not reported as safely skipped.");
        Require(File.ReadAllText(Path.Combine(target, "original.txt")) == "directory original", "Deletion entered a directory link.");
        RequireMissing(link);
    }

    private static void CheckDanglingLink(string root, bool turbo)
    {
        string missingTarget = Path.Combine(root, $"absent-target-{turbo}.txt");
        string link = Path.Combine(root, $"dangling-link-{turbo}.txt");
        File.CreateSymbolicLink(link, missingTarget);
        DeleteResult result = ForceDeleteEngine.ForceDelete(link, killLockingProcesses: false, secureWipe: true, turboMode: turbo);
        Require(result.Success, "The dangling link was not deleted.");
        Require(result.SecureWipe.SkippedLinks == 1 && !result.HasWarnings, "A dangling link was not reported as safely skipped.");
        RequireMissing(link);
        RequireMissing(missingTarget);
    }

    private static void CheckOrdinaryFile(string root)
    {
        string file = Path.Combine(root, "ordinary-shred.txt");
        File.WriteAllText(file, "ordinary selected bytes");
        SecureWipeFileResult shred = ForceDeleteEngine.SecureShredFile(file);
        Require(shred.Status == SecureWipeFileStatus.Completed, "A successful overwrite was not reported.");
        Require(new FileInfo(file).Length == 0, "Ordinary secure overwrite stopped working.");
        DeleteResult result = ForceDeleteEngine.ForceDelete(file, killLockingProcesses: false, secureWipe: true);
        Require(result.Success && !result.HasWarnings && result.SecureWipe.OverwrittenFiles == 1, "Ordinary secure deletion failed.");
        RequireMissing(file);
    }

    private static void CheckReportingModels()
    {
        var report = new SecureWipeReport
        {
            Requested = true, OverwrittenFiles = 2, FailedFiles = 1,
            Failures = ["blocked.txt: Win32 32: file is in use"]
        };
        var result = new DeleteResult(DeleteResultStatus.Success, "Deleted via standard method.\n" + report.Describe(), 1)
        {
            SecureWipe = report
        };
        Require(result.Success && result.HasWarnings, "Deletion success concealed an overwrite failure.");
        var history = new HistoryRecord();
        history.ApplyResult(result);
        Require(history.IsDeleted && !history.IsSuccessful && history.StatusText == "Deleted; wipe failed",
            "History classified a failed overwrite as an unqualified success.");
        HistoryRecord restored = JsonSerializer.Deserialize<HistoryRecord>(JsonSerializer.Serialize(history))!;
        Require(restored.SecureWipe.FailedFiles == 1 && restored.StatusText == history.StatusText && restored.Details.Contains("Win32 32"),
            "History lost overwrite outcomes or error details after serialization.");
        result.Status = DeleteResultStatus.ScheduledOnReboot;
        history.ApplyResult(result);
        Require(!history.IsDeleted && history.StatusText == "On reboot; wipe failed", "Reboot scheduling was labeled as deletion.");
        result.Status = DeleteResultStatus.Failed;
        history.ApplyResult(result);
        Require(history.StatusText == "Partial; wipe failed", "Deletion and overwrite failure were not distinguished.");
        history.ApplyResult(new DeleteResult(DeleteResultStatus.Success, "Deleted.", 1));
        Require(history.IsSuccessful && history.StatusText == "Deleted" && !history.SecureWipe.Requested,
            "Ordinary deletion was incorrectly marked as a secure overwrite.");
        HistoryRecord legacy = JsonSerializer.Deserialize<HistoryRecord>("{\"IsSuccessful\":true,\"IsScheduled\":false}")!;
        Require(legacy.StatusText == "Deleted" && !legacy.SecureWipe.Requested, "Legacy history compatibility changed.");
        Require(new SecureWipeReport { Requested = true, UnverifiedDirectories = 1 }.HasFailures,
            "An unreadable directory was treated as a verified overwrite.");
    }

    private static void CheckWriteFailureDoesNotHideDeletion(string root, bool turbo)
    {
        string file = Path.Combine(root, $"write-blocked-{turbo}.txt");
        File.WriteAllText(file, "bytes that cannot be overwritten");
        using var blocker = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        SecureWipeFileResult shred = ForceDeleteEngine.SecureShredFile(file);
        Require(shred.Status == SecureWipeFileStatus.Failed && shred.Message.Contains("Win32 32:"),
            "The fixture must reject overwrite with a sharing violation.");
        bool failedBeforeDelete = false;
        DeleteResult result = ForceDeleteEngine.ForceDelete(file, killLockingProcesses: false, secureWipe: true, turboMode: turbo,
            log: message =>
            {
                if (!message.StartsWith("[!] Secure overwrite failed for ")) return;
                failedBeforeDelete = true;
                // Release only after the overwrite has failed, so the following
                // ordinary deletion can succeed deterministically.
                blocker.Dispose();
            });
        Require(failedBeforeDelete && result.Success, "The fixture did not exercise successful deletion after failed overwrite.");
        Require(result.HasWarnings && result.SecureWipe.FailedFiles == 1 && result.SecureWipe.OverwrittenFiles == 0,
            "An overwrite failure disappeared after ordinary deletion succeeded.");
        Require(result.Message.Contains("Win32 32:") && result.Message.Contains(Path.GetFileName(file)),
            "The failed file and original overwrite error were not returned.");
        RequireMissing(file);
    }

    private static void CheckFolderWipeResults(string root, bool turbo)
    {
        string folder = Path.Combine(root, $"mixed-folder-{turbo}");
        Directory.CreateDirectory(Path.Combine(folder, "nested"));
        // More than 32 files exercises the parallel worker branch.
        for (int i = 0; i < 40; i++) File.WriteAllText(Path.Combine(folder, $"ordinary-{i}.txt"), "selected bytes");
        string file = Path.Combine(folder, "nested", "blocked.txt");
        File.WriteAllText(file, "blocked selected bytes");
        using var blocker = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        DeleteResult result = ForceDeleteEngine.ForceDelete(folder, killLockingProcesses: false, secureWipe: true, turboMode: turbo,
            log: message =>
            {
                if (message.StartsWith("[!] Secure overwrite failed for ") && message.Contains("blocked.txt")) blocker.Dispose();
            });
        Require(result.Success, "The mixed directory was not deleted.");
        Require(result.HasWarnings && result.SecureWipe.OverwrittenFiles == 40 && result.SecureWipe.FailedFiles == 1,
            "Parallel directory deletion lost or double-counted an overwrite outcome.");
        Require(result.SecureWipe.UnverifiedDirectories == 0 && result.Message.Contains("blocked.txt"),
            "Directory result details did not preserve the failed child.");
        RequireMissing(folder);
    }

    private static void CheckOverwriteRetryAfterPermissionRecovery(string root)
    {
        string folder = Path.Combine(root, "permission-retry");
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "denied.txt");
        File.WriteAllText(file, "retry must overwrite these bytes");
        using var reader = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var directory = new DirectoryInfo(folder);
        var selectedFile = new FileInfo(file);
        DirectorySecurity originalDirectoryAcl = directory.GetAccessControl();
        FileSecurity originalFileAcl = selectedFile.GetAccessControl();
        SecurityIdentifier user = WindowsIdentity.GetCurrent().User!;
        try
        {
            DirectorySecurity directoryAcl = directory.GetAccessControl();
            directoryAcl.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.DeleteSubdirectoriesAndFiles, AccessControlType.Deny));
            directory.SetAccessControl(directoryAcl);
            FileSecurity fileAcl = selectedFile.GetAccessControl();
            fileAcl.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete, AccessControlType.Deny));
            selectedFile.SetAccessControl(fileAcl);
            bool sawFailure = false, sawTruncation = false;
            DeleteResult result = ForceDeleteEngine.ForceDelete(file, killLockingProcesses: false, secureWipe: true, turboMode: false,
                log: message =>
                {
                    if (message.StartsWith("[!] Secure overwrite failed for ")) sawFailure = true;
                    if (!message.StartsWith("[★ SDelete]")) return;
                    sawTruncation = reader.Length == 0;
                    reader.Dispose();
                });
            Require(sawFailure && sawTruncation && result.Success, "Permission recovery did not retry the failed overwrite before deletion.");
            Require(!result.HasWarnings && result.SecureWipe.OverwrittenFiles == 1 && result.SecureWipe.FailedFiles == 0,
                "A recovered overwrite was double-counted or retained its obsolete failure.");
            RequireMissing(file);
        }
        finally
        {
            directory.SetAccessControl(originalDirectoryAcl);
            if (File.Exists(file)) selectedFile.SetAccessControl(originalFileAcl);
        }
    }

    private static void CheckDirectoryFallbackAfterScanFailure(string root)
    {
        string folder = Path.Combine(root, "scan-recovery");
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "unseen.txt");
        File.WriteAllText(file, "fallback must overwrite these bytes");
        using var reader = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var directory = new DirectoryInfo(folder);
        DirectorySecurity originalAcl = directory.GetAccessControl();
        try
        {
            DirectorySecurity deniedAcl = directory.GetAccessControl();
            deniedAcl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny));
            directory.SetAccessControl(deniedAcl);
            IntPtr find = NativeMethods.FindFirstFileExW(SecurityManager.ToLongPath(folder) + "\\*",
                NativeMethods.FindExInfoBasic, out _, NativeMethods.FindExSearchNameMatch, IntPtr.Zero, NativeMethods.FIND_FIRST_EX_LARGE_FETCH);
            int error = Marshal.GetLastWin32Error();
            if (find != NativeMethods.INVALID_HANDLE_VALUE && find != IntPtr.Zero) NativeMethods.FindClose(find);
            Require(find == NativeMethods.INVALID_HANDLE_VALUE && error == NativeMethods.ERROR_ACCESS_DENIED,
                "The fixture must deny the initial directory scan.");
            bool sawTruncation = false;
            DeleteResult result = ForceDeleteEngine.ForceDelete(folder, killLockingProcesses: false, secureWipe: true, turboMode: false,
                log: message =>
                {
                    if (!message.StartsWith("[★ SDelete]")) return;
                    sawTruncation = reader.Length == 0;
                    reader.Dispose();
                });
            Require(sawTruncation && result.Success, "Directory fallback deleted a previously unseen file without overwriting it.");
            Require(!result.HasWarnings && result.SecureWipe.OverwrittenFiles == 1 && result.SecureWipe.UnverifiedDirectories == 0,
                "A recovered directory scan did not update the final overwrite report.");
            RequireMissing(folder);
        }
        finally
        {
            if (Directory.Exists(folder)) directory.SetAccessControl(originalAcl);
        }
    }

    private static void CheckMissingAndInvalidPaths(string root)
    {
        Require(ForceDeleteEngine.ForceDelete(Path.Combine(root, "absent.txt")).Success, "A genuinely absent file is not idempotent.");
        Require(ForceDeleteEngine.ForceDelete(Path.Combine(root, "absent-parent", "absent.txt")).Success, "A genuinely absent parent is not idempotent.");

        string invalid = Path.Combine(root, "invalid|name.txt");
        uint attributes = NativeMethods.GetFileAttributesW(SecurityManager.ToLongPath(invalid));
        int error = Marshal.GetLastWin32Error();
        Require(attributes == NativeMethods.INVALID_FILE_ATTRIBUTES && error != NativeMethods.ERROR_FILE_NOT_FOUND && error != NativeMethods.ERROR_PATH_NOT_FOUND,
            "The invalid-path fixture must exercise a non-not-found Win32 error.");
        DeleteResult result = ForceDeleteEngine.ForceDelete(invalid, killLockingProcesses: false);
        Require(result.Status == DeleteResultStatus.Failed, "An attribute lookup error was reported as successful deletion.");
        Require(result.Message.Contains($"Win32 {error}:"), "The original Win32 error was not preserved.");
    }

    private static void RequireMissing(string path)
    {
        uint attributes = NativeMethods.GetFileAttributesW(SecurityManager.ToLongPath(path));
        int error = Marshal.GetLastWin32Error();
        Require(attributes == NativeMethods.INVALID_FILE_ATTRIBUTES &&
            (error == NativeMethods.ERROR_FILE_NOT_FOUND || error == NativeMethods.ERROR_PATH_NOT_FOUND),
            $"Expected an absent path, but got attributes {attributes} / Win32 {error}: {path}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
