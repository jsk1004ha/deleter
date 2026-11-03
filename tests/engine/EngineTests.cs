using System.Runtime.InteropServices;
using ForceDeleter;

internal static class EngineTests
{
    private static int Main()
    {
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
            }
            CheckOrdinaryFile(root);
            CheckMissingAndInvalidPaths(root);
            Console.WriteLine("PASS: secure overwrite preserves file-link targets, directory links, dangling links, ordinary files, and error results.");
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
        ForceDeleteEngine.SecureShredFile(link);
        Require(File.ReadAllText(target) == "unselected original bytes", "Direct shred followed the file link.");
        DeleteResult result = ForceDeleteEngine.ForceDelete(link, killLockingProcesses: false, secureWipe: true, turboMode: turbo);
        Require(result.Success, "The selected file link was not deleted.");
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
        RequireMissing(link);
        RequireMissing(missingTarget);
    }

    private static void CheckOrdinaryFile(string root)
    {
        string file = Path.Combine(root, "ordinary-shred.txt");
        File.WriteAllText(file, "ordinary selected bytes");
        ForceDeleteEngine.SecureShredFile(file);
        Require(new FileInfo(file).Length == 0, "Ordinary secure overwrite stopped working.");
        Require(ForceDeleteEngine.ForceDelete(file, killLockingProcesses: false, secureWipe: true).Success, "Ordinary deletion failed.");
        RequireMissing(file);
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
