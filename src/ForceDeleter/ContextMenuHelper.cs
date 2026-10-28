using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace ForceDeleter;

/// <summary>
/// Registers or unregisters the Windows Explorer right-click context menu
/// for files and directories.
/// </summary>
public static class ContextMenuHelper
{
    private const string MenuKeyName = "ForceDelete";
    private const string MenuDisplayName = "무조건 강제 삭제 (Force Delete)";

    public static bool IsContextMenuInstalled()
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(@"*\shell\" + MenuKeyName);
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    public static bool RegisterContextMenu(Action<string>? log = null) => InstallContextMenu(log);
    public static bool UnregisterContextMenu(Action<string>? log = null) => UninstallContextMenu(log);

    public static bool InstallContextMenu(Action<string>? log = null)
    {
        try
        {
            string exePath = Process.GetCurrentProcess().MainModule?.FileName
                ?? Environment.ProcessPath
                ?? throw new InvalidOperationException("Cannot determine exe path");

            // 1. Files (*\shell\ForceDelete)
            using (var key = Registry.ClassesRoot.CreateSubKey(@"*\shell\" + MenuKeyName))
            {
                key.SetValue("", MenuDisplayName);
                key.SetValue("Icon", $"\"{exePath}\",0");
                key.SetValue("MultiSelectModel", "Player");

                using var cmdKey = key.CreateSubKey("command");
                cmdKey.SetValue("", $"\"{exePath}\" \"%1\"");
            }

            // 2. Folders (Directory\shell\ForceDelete)
            using (var key = Registry.ClassesRoot.CreateSubKey(@"Directory\shell\" + MenuKeyName))
            {
                key.SetValue("", MenuDisplayName);
                key.SetValue("Icon", $"\"{exePath}\",0");
                key.SetValue("MultiSelectModel", "Player");

                using var cmdKey = key.CreateSubKey("command");
                cmdKey.SetValue("", $"\"{exePath}\" \"%1\"");
            }

            log?.Invoke("[✓] Windows Explorer context menu registered successfully!");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[!] Context menu installation failed: {ex.Message}");
            return false;
        }
    }

    public static bool UninstallContextMenu(Action<string>? log = null)
    {
        try
        {
            Registry.ClassesRoot.DeleteSubKeyTree(@"*\shell\" + MenuKeyName, false);
            Registry.ClassesRoot.DeleteSubKeyTree(@"Directory\shell\" + MenuKeyName, false);
            log?.Invoke("[✓] Windows Explorer context menu removed successfully!");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[!] Context menu removal failed: {ex.Message}");
            return false;
        }
    }
}
