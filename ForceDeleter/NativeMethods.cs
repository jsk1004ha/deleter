using System;
using System.Runtime.InteropServices;

namespace ForceDeleter;

/// <summary>
/// Native Windows API definitions combining techniques from:
/// - NanaRun (TrustedInstaller / SYSTEM token theft & privilege escalation)
/// - windows-force-delete & file-unlocker-for-windows (Restart Manager & Handle closing)
/// - DeadLock (Take ownership & ACL overrides)
/// - ForcedDeleteFiles (MoveFileEx reboot scheduling)
/// - RickStrahl/DeleteFiles (Long path & attribute manipulation)
/// </summary>
internal static class NativeMethods
{
    // ==========================================
    // Process & Token Access
    // ==========================================
    public const uint PROCESS_TERMINATE = 0x0001;
    public const uint PROCESS_DUP_HANDLE = 0x0040;
    public const uint PROCESS_QUERY_INFORMATION = 0x0400;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint MAXIMUM_ALLOWED = 0x02000000;

    public const uint TOKEN_ALL_ACCESS = 0xF01FF;
    public const uint TOKEN_DUPLICATE = 0x0002;
    public const uint TOKEN_IMPERSONATE = 0x0004;
    public const uint TOKEN_QUERY = 0x0008;
    public const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    public const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;

    public const string SE_DEBUG_NAME = "SeDebugPrivilege";
    public const string SE_IMPERSONATE_NAME = "SeImpersonatePrivilege";
    public const string SE_TAKE_OWNERSHIP_NAME = "SeTakeOwnershipPrivilege";
    public const string SE_RESTORE_NAME = "SeRestorePrivilege";
    public const string SE_BACKUP_NAME = "SeBackupPrivilege";
    public const string SE_SECURITY_NAME = "SeSecurityPrivilege";

    public const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    public const uint DUPLICATE_SAME_ACCESS = 0x00000002;
    public const uint DUPLICATE_CLOSE_SOURCE = 0x00000001;

    // ==========================================
    // File Access & Flags
    // ==========================================
    public const uint DELETE = 0x00010000;
    public const uint READ_CONTROL = 0x00020000;
    public const uint WRITE_DAC = 0x00040000;
    public const uint WRITE_OWNER = 0x00080000;
    public const uint SYNCHRONIZE = 0x00100000;
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint GENERIC_ALL = 0x10000000;

    public const uint FILE_SHARE_READ = 0x00000001;
    public const uint FILE_SHARE_WRITE = 0x00000002;
    public const uint FILE_SHARE_DELETE = 0x00000004;
    public const uint FILE_SHARE_ALL = FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE;

    public const uint OPEN_EXISTING = 3;

    public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    public const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    public const uint FILE_FLAG_DELETE_ON_CLOSE = 0x04000000;

    public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    public const uint FILE_ATTRIBUTE_READONLY = 0x01;
    public const uint FILE_ATTRIBUTE_HIDDEN = 0x02;
    public const uint FILE_ATTRIBUTE_SYSTEM = 0x04;
    public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

    public const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004;

    // ==========================================
    // NtSetInformationFile & NT Status
    // ==========================================
    public const uint FileDispositionInformation = 13;
    public const uint FileDispositionInformationEx = 64;
    public const uint FILE_DISPOSITION_FLAG_DELETE = 0x1;
    public const uint FILE_DISPOSITION_FLAG_POSIX_SEMANTICS = 0x2;
    public const uint FILE_DISPOSITION_FLAG_FORCE_IMAGE_SECTION_CHECK = 0x4;
    public const uint FILE_DISPOSITION_FLAG_ON_CLOSE = 0x8;

    public const int SystemHandleInformation = 16;
    public const int ObjectNameInformation = 1;
    public const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);
    public const int STATUS_SUCCESS = 0;

    // ==========================================
    // Services & SCM
    // ==========================================
    public const uint SC_MANAGER_CONNECT = 0x0001;
    public const uint SC_MANAGER_ALL_ACCESS = 0xF003F;
    public const uint SERVICE_START = 0x0010;
    public const uint SERVICE_QUERY_STATUS = 0x0004;
    public const uint SERVICE_RUNNING = 4;
    public const uint SERVICE_STOPPED = 1;

    public const int SecurityImpersonation = 2;
    public const int TokenImpersonation = 2;

    // ==========================================
    // Restart Manager
    // ==========================================
    public const uint RmRebootReasonNone = 0;
    public const int CCH_RM_MAX_APP_NAME = 255;
    public const int CCH_RM_MAX_SVC_NAME = 63;
    public const int ERROR_MORE_DATA = 234;

    // ==========================================
    // Security & Ownership
    // ==========================================
    public const int SE_FILE_OBJECT = 1;
    public const uint OWNER_SECURITY_INFORMATION = 0x00000001;
    public const uint DACL_SECURITY_INFORMATION = 0x00000004;
    public const uint PROTECTED_DACL_SECURITY_INFORMATION = 0x80000000;
    public const uint UNPROTECTED_DACL_SECURITY_INFORMATION = 0x20000000;

    public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

    // ==========================================
    // Structs
    // ==========================================
    [StructLayout(LayoutKind.Sequential)]
    public struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privileges;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IO_STATUS_BLOCK
    {
        public IntPtr Status;
        public IntPtr Information;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FILE_DISPOSITION_INFORMATION
    {
        [MarshalAs(UnmanagedType.U1)]
        public bool DeleteFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FILE_DISPOSITION_INFORMATION_EX
    {
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_HANDLE_TABLE_ENTRY_INFO
    {
        public ushort UniqueProcessId;
        public ushort CreatorBackTraceIndex;
        public byte ObjectTypeIndex;
        public byte HandleAttributes;
        public ushort HandleValue;
        public IntPtr Object;
        public uint GrantedAccess;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OBJECT_NAME_INFORMATION
    {
        public UNICODE_STRING Name;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SERVICE_STATUS_PROCESS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
        public uint dwProcessId;
        public uint dwServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RM_UNIQUE_PROCESS
    {
        public uint dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
        public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
        public string strServiceShortName;
        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    // ==========================================
    // DLL Imports: kernel32.dll
    // ==========================================
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DuplicateHandle(
        IntPtr hSourceProcessHandle, IntPtr hSourceHandle,
        IntPtr hTargetProcessHandle, out IntPtr lpTargetHandle,
        uint dwDesiredAccess, bool bInheritHandle, uint dwOptions);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetFileAttributesW(string lpFileName, uint dwFileAttributes);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint GetFileAttributesW(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MoveFileExW(string lpExistingFileName, string? lpNewFileName, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RemoveDirectoryW(string lpPathName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteFileW(string lpFileName);

    // ==========================================
    // DLL Imports: ntdll.dll
    // ==========================================
    [DllImport("ntdll.dll")]
    public static extern int NtSetInformationFile(
        IntPtr FileHandle, ref IO_STATUS_BLOCK IoStatusBlock,
        ref FILE_DISPOSITION_INFORMATION FileInformation,
        uint Length, uint FileInformationClass);

    [DllImport("ntdll.dll")]
    public static extern int NtSetInformationFile(
        IntPtr FileHandle, ref IO_STATUS_BLOCK IoStatusBlock,
        ref FILE_DISPOSITION_INFORMATION_EX FileInformation,
        uint Length, uint FileInformationClass);

    [DllImport("ntdll.dll")]
    public static extern int NtQuerySystemInformation(
        int SystemInformationClass, IntPtr SystemInformation,
        int SystemInformationLength, out int ReturnLength);

    [DllImport("ntdll.dll")]
    public static extern int NtQueryObject(
        IntPtr Handle, int ObjectInformationClass,
        IntPtr ObjectInformation, int ObjectInformationLength,
        out int ReturnLength);

    // ==========================================
    // DLL Imports: advapi32.dll
    // ==========================================
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DuplicateTokenEx(
        IntPtr hExistingToken, uint dwDesiredAccess,
        IntPtr lpTokenAttributes, int ImpersonationLevel,
        int TokenType, out IntPtr phNewToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool LookupPrivilegeValueW(
        string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AdjustTokenPrivileges(
        IntPtr TokenHandle, bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState, uint BufferLength,
        IntPtr PreviousState, IntPtr ReturnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ImpersonateLoggedOnUser(IntPtr hToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetThreadToken(IntPtr ThreadHandle, IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RevertToSelf();

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr OpenSCManagerW(string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr OpenServiceW(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool StartServiceW(IntPtr hService, uint dwNumServiceArgs, IntPtr lpServiceArgVectors);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryServiceStatusEx(
        IntPtr hService, int InfoLevel, IntPtr lpBuffer,
        uint cbBufSize, out uint pcbBytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseServiceHandle(IntPtr hSCObject);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint SetNamedSecurityInfoW(
        string pObjectName,
        int ObjectType,
        uint SecurityInfo,
        IntPtr psidOwner,
        IntPtr psidGroup,
        IntPtr pDacl,
        IntPtr pSacl);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ConvertStringSidToSidW(
        [MarshalAs(UnmanagedType.LPWStr)] string StringSid,
        out IntPtr pSid);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr LocalFree(IntPtr hMem);

    // ==========================================
    // DLL Imports: rstrtmgr.dll (Restart Manager)
    // ==========================================
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    public static extern int RmStartSession(out uint pSessionHandle, uint dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll")]
    public static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    public static extern int RmRegisterResources(
        uint pSessionHandle, uint nFiles, string[] rgsFileNames,
        uint nApplications, RM_UNIQUE_PROCESS[]? rgApplications,
        uint nServices, string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    public static extern int RmGetList(
        uint dwSessionHandle, out uint pnProcInfoNeeded,
        ref uint pnProcInfo, [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
        ref uint lpdwRebootReasons);

    // ==========================================
    // UI & Window Movement Helpers
    // ==========================================
    public const int WM_NCLBUTTONDOWN = 0xA1;
    public const int HT_CAPTION = 0x2;

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
