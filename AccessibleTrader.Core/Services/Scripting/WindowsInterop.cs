using System;
using System.Runtime.InteropServices;

namespace AccessibleTrader.Core.Services.Scripting;

/// <summary>
/// Win32 P/Invoke signatures, structs, and constants used by
/// <see cref="WindowsAppContainerLauncher"/> to spawn the worker via
/// <c>CreateProcessW</c> with a <c>STARTUPINFOEX</c> carrying the
/// AppContainer <c>SECURITY_CAPABILITIES</c> attribute.
///
/// <para>
/// This is the only code in the codebase that bypasses
/// <see cref="System.Diagnostics.Process.Start()"/>. It exists because
/// <see cref="System.Diagnostics.ProcessStartInfo"/> does not expose
/// the proc-thread attribute list — the one mechanism
/// <see cref="System.Diagnostics.Process"/> offers no route to. Every
/// Win32 signature lives in one file for easy review; all functions
/// return <c>false</c> on failure and set <c>GetLastError</c>.
/// </para>
/// </summary>
internal static class WindowsInterop
{
    // ── STARTUPINFO flags ────────────────────────────────────────────────
    public const uint STARTF_USESTDHANDLES = 0x00000100;

    // ── CreateProcess flags ──────────────────────────────────────────────
    public const uint CREATE_NO_WINDOW            = 0x08000000;
    public const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;

    // ── Proc-thread attributes ───────────────────────────────────────────
    // PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES — its numeric value is
    // constructed via the ProcThreadAttributeValue macro; for
    // ProcThreadAttributeSecurityCapabilities (9) with PROC_THREAD_ATTRIBUTE_INPUT
    // (0x20000) the computed value is 0x00020009.
    public const int PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES = 0x00020009;

    // ── Handle flags ─────────────────────────────────────────────────────
    public const uint HANDLE_FLAG_INHERIT = 0x00000001;

    // ── Wait / exit codes ────────────────────────────────────────────────
    public const uint STILL_ACTIVE   = 259;
    public const uint WAIT_OBJECT_0  = 0;
    public const uint WAIT_TIMEOUT   = 0x00000102;
    public const uint INFINITE       = 0xFFFFFFFF;

    // ── Structs ──────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFOW
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct STARTUPINFOEXW
    {
        public STARTUPINFOW StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SECURITY_CAPABILITIES
    {
        public IntPtr AppContainerSid;
        public IntPtr Capabilities;
        public uint   CapabilityCount;
        public uint   Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_MEMORY_COUNTERS
    {
        public uint  cb;
        public uint  PageFaultCount;
        public ulong PeakWorkingSetSize;
        public ulong WorkingSetSize;
        public ulong QuotaPeakPagedPoolUsage;
        public ulong QuotaPagedPoolUsage;
        public ulong QuotaPeakNonPagedPoolUsage;
        public ulong QuotaNonPagedPoolUsage;
        public ulong PagefileUsage;
        public ulong PeakPagefileUsage;
    }

    // ── Kernel32 ─────────────────────────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CreateProcessW(
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOEXW lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CreatePipe(
        out IntPtr hReadPipe,
        out IntPtr hWritePipe,
        ref SECURITY_ATTRIBUTES lpPipeAttributes,
        uint nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    // InitializeProcThreadAttributeList — called twice: first with a NULL
    // list pointer to discover the required size, then again with the
    // allocated buffer to actually initialize. lpSize is in/out.
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList,
        int dwAttributeCount,
        int dwFlags,
        ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        IntPtr attribute,
        IntPtr lpValue,
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [DllImport("kernel32.dll")]
    public static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    // ── psapi.dll — working-set inspection ───────────────────────────────

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetProcessMemoryInfo(
        IntPtr hProcess,
        out PROCESS_MEMORY_COUNTERS counters,
        uint cb);

    /// <summary>GetProcessTimes — user + kernel CPU time as 100-ns ticks via FILETIME.
    /// Creation and exit FILETIMEs are returned too; we ignore them (the host tracks
    /// wall-clock separately) and only consume kernel + user for the CPU-quota check.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetProcessTimes(
        IntPtr hProcess,
        out long lpCreationTime,
        out long lpExitTime,
        out long lpKernelTime,
        out long lpUserTime);
}
