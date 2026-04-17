using System;
using System.IO;
using System.Threading;
using AccessibleTrader.Core.Services.Scripting;
using Android.App;
using Android.Content;
using Android.OS;

namespace AccessibleTrader.BlazorClient.Platforms.Android;

/// <summary>
/// <see cref="IScriptWorkerProcess"/> implementation backed by a bound
/// Android <see cref="ScriptWorkerService"/>. Produced by
/// <see cref="AndroidIsolatedProcessLauncher.Launch"/>.
///
/// <para>
/// Owns the host-side ends of the stdin/stdout <see cref="FileStream"/>
/// pipes and the <see cref="IServiceConnection"/> used to track the
/// service lifetime. Disposal unbinds the service and closes the
/// streams, which triggers EOF on the worker side and lets its
/// dispatch loop exit.
/// </para>
///
/// <para>
/// Android doesn't expose a direct working-set read for a bound
/// isolated-process service via the ordinary .NET surface (the
/// process ID is internal to <c>ActivityManager</c> and changes per
/// bind). <see cref="WorkingSet64"/> returns 0, which
/// <see cref="OutOfProcessScriptHost"/>'s memory-quota poller treats
/// as "no data" and skips — the Android process-level
/// <c>android.os.Process.setResourceLimit</c> budgets and the system
/// low-memory killer are the real enforcement on this platform.
/// </para>
/// </summary>
internal sealed class AndroidScriptWorkerProcess : IScriptWorkerProcess
{
    private readonly Context _context;
    private readonly IServiceConnection _connection;
    private readonly FileStream _stdin;
    private readonly FileStream _stdout;
    private readonly Stream _stderr;
    private readonly StreamReader _stderrReader;
    private volatile bool _hasExited;
    private bool _disposed;

    public AndroidScriptWorkerProcess(
        Context context,
        IServiceConnection connection,
        FileStream stdin,
        FileStream stdout)
    {
        _context    = context;
        _connection = connection;
        _stdin      = stdin;
        _stdout     = stdout;

        // No meaningful stderr from an Android bound service — the
        // worker reports diagnostics over its stdout frame channel
        // (Opcode.Diagnostic). Back the StderrReader with an empty
        // stream so OutOfProcessScriptHost's pump exits on its first read.
        _stderr = Stream.Null;
        _stderrReader = new StreamReader(_stderr);
    }

    public Stream       StdinWrite   => _stdin;
    public Stream       StdoutRead   => _stdout;
    public StreamReader StderrReader => _stderrReader;

    public bool HasExited => _hasExited;

    /// <summary>
    /// No meaningful exit code from an unbound service; return -1 as the
    /// platform-defined sentinel noted in <see cref="IScriptWorkerProcess.ExitCode"/>.
    /// </summary>
    public int ExitCode => -1;

    public bool Kill(bool entireProcessTree)
    {
        // "Kill" on Android = close the pipes (worker hits EOF and
        // exits its dispatch loop) + unbind the service (Android
        // schedules the isolated process for termination). There's no
        // direct equivalent of TerminateProcess for a service we don't
        // own.
        try { _stdin.Dispose();  } catch { }
        try { _stdout.Dispose(); } catch { }
        try { _context.UnbindService(_connection); } catch { }
        _hasExited = true;
        return true;
    }

    public bool WaitForExit(int milliseconds)
    {
        // Polling-free: after Kill the pipes are closed and
        // _hasExited is set. Host's Wait is only used during graceful
        // shutdown — return true immediately so the supervisor
        // progresses to the unbind path.
        if (_hasExited) return true;
        // Give the worker a brief window to flush its final Diagnostic
        // frames. The host already sent Shutdown; the worker is
        // expected to exit RunAsync quickly.
        Thread.Sleep(Math.Min(milliseconds, 100));
        return _hasExited;
    }

    public void Refresh()
    {
        // No observable working-set for the isolated-process service
        // through standard Android APIs without the service PID (which
        // isn't exposed to the bound client). No-op.
    }

    public long WorkingSet64 => 0;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Kill(entireProcessTree: false);
        try { _stderrReader.Dispose(); } catch { }
    }
}
