using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ForceDeleter;

/// <summary>
/// 인프로세스(In-Process) 보안 및 ACL 우회 모듈.
/// 기존 takeown.exe / icacls.exe 외부 프로세스(Process.Start) 호출을 완전히 제거하고,
/// Win32 advapi32.dll (SetNamedSecurityInfoW) 및 Native NULL DACL 주입으로 마이크로초 단위로 권한을 획득합니다.
/// </summary>
public static class SecurityManager
{
    private static readonly byte[] AdminSidBytes;

    static SecurityManager()
    {
        var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        AdminSidBytes = new byte[adminSid.BinaryLength];
        adminSid.GetBinaryForm(AdminSidBytes, 0);
    }

    /// <summary>
    /// MAX_PATH(260자) 제한 및 특수 경로(CON, NUL 등) 우회를 위해 \\?\ 접두사를 부여합니다.
    /// </summary>
    public static string ToLongPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
            return path;

        string fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return @"\\?\UNC\" + fullPath.Substring(2);
        }

        return @"\\?\" + fullPath;
    }

    /// <summary>
    /// ToLongPath의 이전 호환성 별칭입니다.
    /// </summary>
    public static string NormalizePath(string path) => ToLongPath(path);

    /// <summary>
    /// ReadOnly, Hidden, System 속성을 즉시 제거하여 일반 파일 속성(FILE_ATTRIBUTE_NORMAL)으로 정규화합니다.
    /// </summary>
    public static bool ClearAttributes(string path)
    {
        try
        {
            string longPath = ToLongPath(path);
            uint currentAttrs = NativeMethods.GetFileAttributesW(longPath);

            if (currentAttrs != NativeMethods.INVALID_FILE_ATTRIBUTES)
            {
                return NativeMethods.SetFileAttributesW(longPath, NativeMethods.FILE_ATTRIBUTE_NORMAL);
            }
        }
        catch
        {
            // 속성 조회 실패 시 무시
        }

        return false;
    }

    /// <summary>
    /// ClearAttributes의 이전 호환성 별칭입니다.
    /// </summary>
    public static bool StripAttributes(string path, Action<string>? log = null)
    {
        bool result = ClearAttributes(path);
        if (result && log != null)
        {
            log($"[+] Reset file attributes to NORMAL: {Path.GetFileName(path)}");
        }
        return result;
    }

    /// <summary>
    /// 외부 프로세스(takeown.exe / icacls.exe)를 띄우지 않고,
    /// 인프로세스 Win32 SetNamedSecurityInfoW 호출로 소유자(Administrators) 변경 및 NULL DACL(모든 권한 허용)을 즉시 적용합니다.
    /// </summary>
    public static bool TakeOwnershipAndGrantFullControl(string path, Action<string>? log = null)
    {
        string longPath = ToLongPath(path);
        ClearAttributes(longPath);

        try
        {
            GCHandle pinnedSid = GCHandle.Alloc(AdminSidBytes, GCHandleType.Pinned);
            try
            {
                IntPtr pAdminSid = pinnedSid.AddrOfPinnedObject();

                // 1) 소유자(Owner)를 Builtin Administrators로 즉시 변경 (SeTakeOwnershipPrivilege / SeRestorePrivilege 활용)
                uint ownerResult = NativeMethods.SetNamedSecurityInfoW(
                    longPath,
                    NativeMethods.SE_FILE_OBJECT,
                    NativeMethods.OWNER_SECURITY_INFORMATION,
                    pAdminSid,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero);

                if (ownerResult == 0)
                {
                    log?.Invoke($"[+] Set ownership to Administrators for {Path.GetFileName(path)}");
                }

                // 2) NULL DACL 주입 (pDacl = IntPtr.Zero): 모든 거부(DENY) ACE를 무력화하고 전면 접근 허용
                uint daclResult = NativeMethods.SetNamedSecurityInfoW(
                    longPath,
                    NativeMethods.SE_FILE_OBJECT,
                    NativeMethods.DACL_SECURITY_INFORMATION | NativeMethods.PROTECTED_DACL_SECURITY_INFORMATION,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero);

                if (daclResult == 0)
                {
                    log?.Invoke($"[✓ SuperDelete] Applied in-process NULL DACL for {Path.GetFileName(path)}");
                    return true;
                }
            }
            finally
            {
                pinnedSid.Free();
            }

            // 3) NULL DACL이 특정 파일시스템 정책에 의해 거부될 경우 관리형 .NET ACL로 Administrators / Everyone FullControl 부여
            GrantManagedFullControl(path);
            log?.Invoke($"[+] Granted FullControl via managed ACL for {Path.GetFileName(path)}");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[!] TakeOwnershipAndGrantFullControl error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 이전 호환성을 위한 TakeOwnershipAndGrantPermissions 메서드입니다.
    /// </summary>
    public static bool TakeOwnershipAndGrantPermissions(string path, Action<string>? log = null)
    {
        return TakeOwnershipAndGrantFullControl(path, log);
    }

    /// <summary>
    /// 이전 호환성을 위한 BypassAclSuperDelete 메서드입니다.
    /// </summary>
    public static bool BypassAclSuperDelete(string path, Action<string>? log = null)
    {
        return TakeOwnershipAndGrantFullControl(path, log);
    }

    private static void GrantManagedFullControl(string path)
    {
        var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var worldSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);

        if (Directory.Exists(path))
        {
            var dirInfo = new DirectoryInfo(path);
            var security = new DirectorySecurity();
            security.SetOwner(adminSid);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(
                worldSid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            dirInfo.SetAccessControl(security);
        }
        else if (File.Exists(path))
        {
            var fileInfo = new FileInfo(path);
            var security = new FileSecurity();
            security.SetOwner(adminSid);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(
                worldSid,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            fileInfo.SetAccessControl(security);
        }
    }
}
