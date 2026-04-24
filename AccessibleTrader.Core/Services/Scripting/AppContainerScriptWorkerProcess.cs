using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace AccessibleTrader.Core.Services.Scripting;

/// <summary>
/// <see cref="IScriptWorkerProcess"/> implementation for a Windows
/// AppContainer-sandboxed child process. Constructed by
/// <see cref="WindowsAppContainerLauncher"/> from the
/// <see cref="WindowsInterop.PROCESS_INFORMATION"/> returned by
/// <c>CreateProcessW</c> plus the host-side ends of three anonymous
/// pipes.
///
/// <para>
/// Wraps the raw pipe handles as <see cref="FileStream"/> objects that
/// own their <see cref="SafeFileHandle"/>s — closing the streams closes
/// the handles, which is exactly what we want on disposal (the child
/// sees EOF on its stdin and the host's stdout pump exits). The process
/// handle and primary thread handle are closed in
/// <see cref="Dispose"/>.
/// </para>
///
/// <para>
/// <see cref="WorkingSet64"/> is fetched via <c>GetProcessMemoryInfo</c>
/// (psapi.dll) rather than <see cref="System.Diagnostics.Process"/> —
/// the latter would require re-opening the process by PID, which is
/// fine on Windows but an extra round-trip. Calling the API directly
/// on the already-open handle is simpler.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class AppContainerScriptWorkerProcess : IScriptWorkerProcess
{
    private IntPtr _hProcess;
    private IntPtr _hThread;
    private readonly uint _processId;
    private readonly FileStream _stdin;
    private readonly FileStream _stdout;
    private readonly FileStream _stderr;
    private readonly StreamReader _stderrReader;
    private long _workingSet;
    private bool _disposed;

    public AppContainerScriptWorkerProcess(
        IntPtr hProcess,
        IntPtr hThread,
        uint processId,
        SafeFileHandle hostStdinWrite,
        SafeFileHandle hostStdoutRead,
        SafeFileHandle hostStderrRead)
    {
        _hProcess = hProcess;
        _hThread  = hThread;
        _processId = processId;

        // FileStream takes ownership of each SafeFileHandle — closing the
        // stream closes the underlying OS handle. Explicit buffer size
        // matches .NET's default Process redirect-stream behaviour (4 KB)
        // so existing frame-read latency profiles carry over.
        _stdin  = new FileStream(hostStdinWrite,  FileAccess.Write, bufferSize: 4096, isAsync: true);
        _stdout = new FileStream(hostStdoutRead,  FileAccess.Read,  bufferSize: 4096, isAsync: true);
        _stderr = new FileStream(hostStderrRead,  FileAccess.Read,  bufferSize: 4096, isAsync: true);
        _stderrReader = new StreamReader(_stderr, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
    }

    public Stream       StdinWrite   => _stdin;
    public Stream       StdoutRead   => _stdout;
    public StreamReader StderrReader => _stderrReader;

    public bool HasExited
    {
        get
        {
            if (_hProcess == IntPtr.Zero) return true;
            if (!WindowsInterop.GetExitCodeProcess(_hProcess, out uint code)) return false;
            return code != WindowsInterop.STILL_ACTIVE;
        }
    }

    public int ExitCode
    {
        get
        {
            if (_hProcess == IntPtr.Zero) return -1;
            if (!WindowsInterop.GetExitCodeProcess(_hProcess, out uint code)) return -1;
            return (int)code;
        }
    }

    public bool Kill(bool entireProcessTree)
    {
        // AppContainer child cannot spawn grandchildren (process-create is
        // denied by the sandbox profile), so entireProcessTree is a no-op.
        if (_hProcess == IntPtr.Zero) return true;
        if (HasExited) return true;
        return WindowsInterop.TerminateProcess(_hProcess, 1);
    }

    public bool WaitForExit(int milliseconds)
    {
        if (_hProcess == IntPtr.Zero) return true;
        uint wait = WindowsInterop.WaitForSingleObject(
            _hProcess,
            milliseconds < 0 ? WindowsInterop.INFINITE : (uint)milliseconds);
        return wait == WindowsInterop.WAIT_OBJECT_0;
    }

    public void Refresh()
    {
        if (_hProcess == IntPtr.Zero) return;
        try
        {
            if (WindowsInterop.GetProcessMemoryInfo(_hProcess, out var counters,
                    (uint)Marshal.SizeOf<WindowsInterop.PROCESS_MEMORY_COUNTERS>()))
            {
                _workingSet = (long)counters.WorkingSetSize;
            }
        }
        catch
        {
            // Process may have exited between the HasExited check in the
            // poller and this call — leave _workingSet at its last value
            // and let the subsequent HasExited check handle cleanup.
        }
    }

    public long WorkingSet64 => _workingSet;

    public TimeSpan TotalProcessorTime
    {
        get
        {
            // GetProcessTimes reports user + kernel CPU time via FILETIME
            // (100-ns ticks). AppContainer-launched children can legitimately
            // be queried for this without elevated rights because we own the
            // handle. Returning zero on failure is the documented "no data"
            // signal that OutOfProcessScriptHost's CPU-quota poller treats
            // as a skip-this-tick.
            if (_hProcess == IntPtr.Zero) return TimeSpan.Zero;
            try
            {
                if (WindowsInterop.GetProcessTimes(_hProcess,
                        out long creation, out long exit, out long kernel, out long user))
                {
                    return TimeSpan.FromTicks(kernel + user);
                }
            }
            catch { }
            return TimeSpan.Zero;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _stderrReader.Dispose(); } catch { }
        try { _stdin.Dispose();        } catch { }
        try { _stdout.Dispose();       } catch { }
        try { _stderr.Dispose();       } catch { }

        var p = _hProcess; _hProcess = IntPtr.Zero;
        var t = _hThread;  _hThread  = IntPtr.Zero;
        if (p != IntPtr.Zero) try { WindowsInterop.CloseHandle(p); } catch { }
        if (t != IntPtr.Zero) try { WindowsInterop.CloseHandle(t); } catch { }
    }
}
