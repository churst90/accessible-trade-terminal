using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace AccessibleTrader.Core.Services.Scripting;

/// <summary>
/// Windows launcher that spawns the ScriptWorker inside an AppContainer
/// sandbox. The AppContainer profile SID is added to the child's token
/// at creation time via <c>PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES</c>;
/// the resulting process runs under a restricted SID with no inherent
/// access to the user's file system, no network capabilities, and no
/// ability to reach system-wide Mach / COM / named-object endpoints.
///
/// <para>
/// Because <see cref="System.Diagnostics.Process.Start()"/> doesn't
/// expose the <c>STARTUPINFOEX</c> attribute list, this launcher calls
/// <c>CreateProcessW</c> directly via P/Invoke
/// (<see cref="WindowsInterop"/>), manually creates three anonymous
/// pipes for stdio redirection, and returns a bespoke
/// <see cref="AppContainerScriptWorkerProcess"/> implementation of
/// <see cref="IScriptWorkerProcess"/>.
/// </para>
///
/// <para>
/// The AppContainer profile is created on first use via
/// <c>CreateAppContainerProfile</c> (userenv.dll) and cached for the
/// life of the process. The profile is scoped to our app — multiple
/// instances of the terminal share the same sandbox identity, which is
/// fine because the profile grants no capabilities.
/// </para>
///
/// <para>
/// <b>File ACL note:</b> the AppContainer SID must have read+execute
/// permission on the worker's install directory. Standard Windows app
/// installers under <c>Program Files</c> grant this automatically via
/// the built-in <c>ALL APPLICATION PACKAGES</c> ACE. Dev builds under
/// <c>%USERPROFILE%</c> may not — if <c>CreateProcessW</c> fails with
/// <c>ERROR_ACCESS_DENIED</c> the launcher records the precise
/// <c>GetLastError</c> code and REFUSES to run the worker
/// (<see cref="ScriptSandboxUnavailableException"/>) rather than
/// silently dropping the OS sandbox boundary. Developers can set
/// <c>ACCESSIBLETRADER_ALLOW_UNSANDBOXED_SCRIPTS=1</c> to restore the
/// old fall-through behaviour; every launch under the override is
/// recorded to the security event log.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAppContainerLauncher : IScriptWorkerLauncher
{
    private const string ProfileName = "AccessibleTrader.ScriptWorker.Sandbox";
    private const string ProfileDisplayName = "AccessibleTrader Script Worker";
    private const string ProfileDescription =
        "Sandboxed host for user-compiled Roslyn indicators. No network, no filesystem outside the sandbox dir.";

    private readonly IScriptWorkerLauncher _fallback;
    private readonly Lazy<IntPtr> _profileSid;

    /// <summary>
    /// SID of the AppContainer profile used to isolate the worker.
    /// <see cref="IntPtr.Zero"/> if profile creation/derivation failed.
    /// </summary>
    public IntPtr AppContainerSid => _profileSid.Value;

    /// <summary>
    /// <c>true</c> if the last <see cref="Launch"/> actually applied the
    /// AppContainer security attribute to the child process.
    /// <c>false</c> indicates the call fell through to the default
    /// unsandboxed launcher (e.g. unsupported OS, ACL denial on the
    /// worker path, profile SID derivation failed).
    /// </summary>
    public bool SandboxApplied { get; private set; }

    /// <summary>
    /// Last <c>GetLastError</c> reported by
    /// <c>CreateProcessW</c>. Zero if the call succeeded or was never
    /// made. Exposed for diagnostic surfaces.
    /// </summary>
    public int LastCreateProcessError { get; private set; }

    public WindowsAppContainerLauncher(IScriptWorkerLauncher? fallback = null)
    {
        _fallback = fallback ?? new DefaultProcessLauncher();
        _profileSid = new Lazy<IntPtr>(EnsureProfileSid);
    }

    public IScriptWorkerProcess Launch(string workerExecutablePath)
    {
        if (string.IsNullOrEmpty(workerExecutablePath))
            throw new ArgumentException("workerExecutablePath is required", nameof(workerExecutablePath));
        if (!File.Exists(workerExecutablePath))
            throw new FileNotFoundException("Worker executable not found.", workerExecutablePath);

        var sid = _profileSid.Value;
        if (sid == IntPtr.Zero)
        {
            // No AppContainer available. Refuse unless the user explicitly
            // accepted unsandboxed execution — never downgrade silently.
            RecordFallback("appcontainer profile SID unavailable (unsupported OS or userenv.dll call failed)",
                win32Error: 0);
            SandboxPolicy.EnforceOrThrow(
                SandboxPolicy.AllowUnsandboxedFallback,
                details: "the Windows AppContainer profile could not be created (unsupported OS or userenv.dll call failed).",
                remedy: "Windows 8 or newer is required for AppContainer isolation.");
            SandboxApplied = false;
            SandboxPolicy.RecordUnsandboxedFallback(nameof(WindowsAppContainerLauncher), "profile SID unavailable");
            return _fallback.Launch(workerExecutablePath);
        }

        try
        {
            return LaunchInAppContainer(workerExecutablePath, sid);
        }
        catch (Win32Exception w32)
        {
            // Access denied (0x5) on the worker image ACL is the most common
            // dev-box failure; the user can grant read access to
            // "ALL APPLICATION PACKAGES" on the worker directory to fix it.
            // Record for diagnostic surfaces, then refuse (or fall through
            // when the unsandboxed override is set).
            LastCreateProcessError = w32.NativeErrorCode;
            RecordFallback(
                $"CreateProcessW failed (0x{w32.NativeErrorCode:X}): {w32.Message}",
                w32.NativeErrorCode);
            SandboxPolicy.EnforceOrThrow(
                SandboxPolicy.AllowUnsandboxedFallback,
                details: $"launching inside the Windows AppContainer failed (0x{w32.NativeErrorCode:X}: {w32.Message}).",
                remedy: "If this is error 0x5 (access denied), grant 'ALL APPLICATION PACKAGES' read access to the application directory.");
            SandboxApplied = false;
            SandboxPolicy.RecordUnsandboxedFallback(nameof(WindowsAppContainerLauncher),
                $"CreateProcessW failed 0x{w32.NativeErrorCode:X}");
            return _fallback.Launch(workerExecutablePath);
        }
    }

    private static void RecordFallback(string message, int win32Error)
    {
        var log = AccessibleTrader.Sdk.Services.PluginHostServices.SecurityEvents;
        if (log is null) return;
        log.Record(new AccessibleTrader.Sdk.Services.SecurityEvent(
            DateTime.UtcNow,
            AccessibleTrader.Sdk.Services.SecurityEventKind.AppContainerFallback,
            Source: "WindowsAppContainerLauncher",
            Message: message,
            Data: new Dictionary<string, string>
            {
                ["win32Error"] = $"0x{win32Error:X}",
            }));
    }

    private IScriptWorkerProcess LaunchInAppContainer(string workerExecutablePath, IntPtr sid)
    {
        // ── 1. Create three anonymous pipes ──────────────────────────────
        // Each pipe has a "client" end (the side the child reads/writes)
        // marked inheritable so CreateProcess passes it to the child, and
        // a "host" end we keep and wrap as a managed stream. Host ends
        // are explicitly marked non-inheritable to prevent accidental
        // leakage to grandchildren (AppContainer denies those anyway, but
        // defense-in-depth is cheap).
        var saInheritable = new WindowsInterop.SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<WindowsInterop.SECURITY_ATTRIBUTES>(),
            lpSecurityDescriptor = IntPtr.Zero,
            bInheritHandle = true,
        };

        IntPtr hChildStdInRead = IntPtr.Zero, hHostStdInWrite = IntPtr.Zero;
        IntPtr hHostStdOutRead = IntPtr.Zero, hChildStdOutWrite = IntPtr.Zero;
        IntPtr hHostStdErrRead = IntPtr.Zero, hChildStdErrWrite = IntPtr.Zero;
        IntPtr attrList = IntPtr.Zero;
        IntPtr scPtr = IntPtr.Zero;
        var pi = default(WindowsInterop.PROCESS_INFORMATION);
        bool succeeded = false;

        try
        {
            if (!WindowsInterop.CreatePipe(out hChildStdInRead, out hHostStdInWrite, ref saInheritable, 0))
                ThrowLastError("CreatePipe (stdin)");
            if (!WindowsInterop.SetHandleInformation(hHostStdInWrite, WindowsInterop.HANDLE_FLAG_INHERIT, 0))
                ThrowLastError("SetHandleInformation (stdin host)");

            if (!WindowsInterop.CreatePipe(out hHostStdOutRead, out hChildStdOutWrite, ref saInheritable, 0))
                ThrowLastError("CreatePipe (stdout)");
            if (!WindowsInterop.SetHandleInformation(hHostStdOutRead, WindowsInterop.HANDLE_FLAG_INHERIT, 0))
                ThrowLastError("SetHandleInformation (stdout host)");

            if (!WindowsInterop.CreatePipe(out hHostStdErrRead, out hChildStdErrWrite, ref saInheritable, 0))
                ThrowLastError("CreatePipe (stderr)");
            if (!WindowsInterop.SetHandleInformation(hHostStdErrRead, WindowsInterop.HANDLE_FLAG_INHERIT, 0))
                ThrowLastError("SetHandleInformation (stderr host)");

            // ── 2. Build proc-thread attribute list with 1 slot ─────────
            // Two-call pattern: first with NULL buffer to retrieve the
            // required size, then allocate and init.
            IntPtr listSize = IntPtr.Zero;
            WindowsInterop.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref listSize);
            // Expected to "fail" with ERROR_INSUFFICIENT_BUFFER; listSize
            // is now populated.
            attrList = Marshal.AllocHGlobal(listSize);
            if (!WindowsInterop.InitializeProcThreadAttributeList(attrList, 1, 0, ref listSize))
                ThrowLastError("InitializeProcThreadAttributeList");

            // ── 3. Populate SECURITY_CAPABILITIES with AppContainer SID ─
            var sc = new WindowsInterop.SECURITY_CAPABILITIES
            {
                AppContainerSid = sid,
                Capabilities    = IntPtr.Zero,
                CapabilityCount = 0,
                Reserved        = 0,
            };
            scPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WindowsInterop.SECURITY_CAPABILITIES>());
            Marshal.StructureToPtr(sc, scPtr, fDeleteOld: false);

            if (!WindowsInterop.UpdateProcThreadAttribute(
                    attrList, 0,
                    (IntPtr)WindowsInterop.PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES,
                    scPtr,
                    (IntPtr)Marshal.SizeOf<WindowsInterop.SECURITY_CAPABILITIES>(),
                    IntPtr.Zero, IntPtr.Zero))
                ThrowLastError("UpdateProcThreadAttribute");

            // ── 4. Build STARTUPINFOEX + call CreateProcessW ────────────
            var si = default(WindowsInterop.STARTUPINFOEXW);
            si.StartupInfo.cb = Marshal.SizeOf<WindowsInterop.STARTUPINFOEXW>();
            si.StartupInfo.dwFlags = WindowsInterop.STARTF_USESTDHANDLES;
            si.StartupInfo.hStdInput  = hChildStdInRead;
            si.StartupInfo.hStdOutput = hChildStdOutWrite;
            si.StartupInfo.hStdError  = hChildStdErrWrite;
            si.lpAttributeList = attrList;

            // CreateProcessW wants a writable buffer for lpCommandLine
            // (historically it modifies the contents). Quote the path to
            // survive spaces in the install directory; the worker takes
            // no args.
            var cmdLine = "\"" + workerExecutablePath + "\"";
            var workingDir = Path.GetDirectoryName(workerExecutablePath) ?? AppContext.BaseDirectory;

            bool createdOk = WindowsInterop.CreateProcessW(
                lpApplicationName: workerExecutablePath,
                lpCommandLine:     cmdLine,
                lpProcessAttributes: IntPtr.Zero,
                lpThreadAttributes:  IntPtr.Zero,
                bInheritHandles:   true,
                dwCreationFlags:   WindowsInterop.CREATE_NO_WINDOW | WindowsInterop.EXTENDED_STARTUPINFO_PRESENT,
                lpEnvironment:     IntPtr.Zero,
                lpCurrentDirectory: workingDir,
                lpStartupInfo:     ref si,
                lpProcessInformation: out pi);

            if (!createdOk)
            {
                LastCreateProcessError = Marshal.GetLastWin32Error();
                throw new Win32Exception(LastCreateProcessError,
                    $"CreateProcessW failed (error 0x{LastCreateProcessError:X}). " +
                    "Most common cause on dev builds: the worker directory's ACL does not grant read+execute " +
                    "to the AppContainer SID. Install under Program Files, or grant 'ALL APPLICATION PACKAGES' " +
                    "read+execute on the worker directory.");
            }

            // ── 5. Close child-side handles in the host ─────────────────
            // The child has duplicated them into its own handle table. If
            // we keep them open on our side, the EOF signal the child
            // sends by closing its stdout never reaches us (because our
            // copy keeps the pipe alive).
            WindowsInterop.CloseHandle(hChildStdInRead);  hChildStdInRead   = IntPtr.Zero;
            WindowsInterop.CloseHandle(hChildStdOutWrite); hChildStdOutWrite = IntPtr.Zero;
            WindowsInterop.CloseHandle(hChildStdErrWrite); hChildStdErrWrite = IntPtr.Zero;

            // ── 6. Wrap host-side handles as SafeFileHandles ────────────
            // Ownership transfers to the SafeFileHandle which the
            // FileStream then owns — closing the stream closes the handle.
            var sfStdin  = new SafeFileHandle(hHostStdInWrite,  ownsHandle: true); hHostStdInWrite = IntPtr.Zero;
            var sfStdout = new SafeFileHandle(hHostStdOutRead,  ownsHandle: true); hHostStdOutRead = IntPtr.Zero;
            var sfStderr = new SafeFileHandle(hHostStdErrRead,  ownsHandle: true); hHostStdErrRead = IntPtr.Zero;

            var proc = new AppContainerScriptWorkerProcess(
                pi.hProcess, pi.hThread, pi.dwProcessId,
                sfStdin, sfStdout, sfStderr);

            // Transfer ownership: clearing pi.hProcess/hThread so the
            // finally block doesn't close them.
            pi.hProcess = IntPtr.Zero;
            pi.hThread  = IntPtr.Zero;

            SandboxApplied = true;
            LastCreateProcessError = 0;
            succeeded = true;
            return proc;
        }
        finally
        {
            // ── Cleanup: release anything we still own ──────────────────
            if (!succeeded)
            {
                if (hChildStdInRead   != IntPtr.Zero) WindowsInterop.CloseHandle(hChildStdInRead);
                if (hChildStdOutWrite != IntPtr.Zero) WindowsInterop.CloseHandle(hChildStdOutWrite);
                if (hChildStdErrWrite != IntPtr.Zero) WindowsInterop.CloseHandle(hChildStdErrWrite);
                if (hHostStdInWrite   != IntPtr.Zero) WindowsInterop.CloseHandle(hHostStdInWrite);
                if (hHostStdOutRead   != IntPtr.Zero) WindowsInterop.CloseHandle(hHostStdOutRead);
                if (hHostStdErrRead   != IntPtr.Zero) WindowsInterop.CloseHandle(hHostStdErrRead);
                if (pi.hProcess       != IntPtr.Zero) WindowsInterop.CloseHandle(pi.hProcess);
                if (pi.hThread        != IntPtr.Zero) WindowsInterop.CloseHandle(pi.hThread);
            }

            if (attrList != IntPtr.Zero)
            {
                WindowsInterop.DeleteProcThreadAttributeList(attrList);
                Marshal.FreeHGlobal(attrList);
            }
            if (scPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(scPtr);
            }
        }
    }

    [DoesNotReturn]
    private static void ThrowLastError(string operation)
    {
        int err = Marshal.GetLastWin32Error();
        throw new Win32Exception(err, $"{operation} failed (error 0x{err:X})");
    }

    private IntPtr EnsureProfileSid()
    {
        try
        {
            // First try to derive the SID for an existing profile — this
            // is a pure query, idempotent, and the cheapest path.
            int hr = NativeMethods.DeriveAppContainerSidFromAppContainerName(
                ProfileName, out IntPtr sid);
            if (hr == 0 && sid != IntPtr.Zero)
                return sid;

            // Profile doesn't exist — create it.
            hr = NativeMethods.CreateAppContainerProfile(
                ProfileName, ProfileDisplayName, ProfileDescription,
                capabilities: IntPtr.Zero, capabilityCount: 0,
                out IntPtr newSid);
            if (hr == 0 && newSid != IntPtr.Zero)
                return newSid;

            // Some error states (e.g. profile already exists) are recoverable
            // via the derive call. Retry once.
            hr = NativeMethods.DeriveAppContainerSidFromAppContainerName(
                ProfileName, out sid);
            return hr == 0 ? sid : IntPtr.Zero;
        }
        catch (DllNotFoundException)
        {
            // userenv.dll / kernelbase.dll missing the AppContainer API —
            // older Windows. Treat as unsupported and fall through.
            return IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static class NativeMethods
    {
        // userenv.dll — AppContainer profile management. Available on
        // Windows 8+ / Server 2012+.

        [DllImport("userenv.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern int CreateAppContainerProfile(
            string pszAppContainerName,
            string pszDisplayName,
            string pszDescription,
            IntPtr capabilities,
            int capabilityCount,
            out IntPtr ppSidAppContainerSid);

        [DllImport("userenv.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern int DeriveAppContainerSidFromAppContainerName(
            string pszAppContainerName,
            out IntPtr ppsidAppContainerSid);
    }
}

