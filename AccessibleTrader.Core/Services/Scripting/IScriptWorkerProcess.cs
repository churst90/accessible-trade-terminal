namespace AccessibleTrader.Core.Services.Scripting;

/// <summary>
/// Launcher-owned abstraction over a spawned worker process. Replaces
/// the direct <see cref="System.Diagnostics.Process"/> return type that
/// previous iterations of <see cref="IScriptWorkerLauncher"/> used.
///
/// <para>
/// Why this exists: two launchers — the Windows AppContainer one and
/// the Android isolated-process one — cannot produce a fully-functional
/// <see cref="System.Diagnostics.Process"/>. AppContainer requires
/// <c>CreateProcessW</c> with <c>STARTUPINFOEX</c> and manually-managed
/// inheritable pipes, and the .NET <see cref="System.Diagnostics.Process"/>
/// class will not populate its <c>StandardInput</c> / <c>StandardOutput</c>
/// streams for a process created any other way than via
/// <see cref="System.Diagnostics.Process.Start()"/>. Android has no
/// OS-level process-spawn primitive at all — the "worker" is a bound
/// <c>Service</c> and IPC travels over <c>ParcelFileDescriptor</c> pipes.
/// This abstraction is the minimum surface
/// <see cref="OutOfProcessScriptHost"/> needs so a launcher can produce
/// whichever underlying primitive fits the platform.
/// </para>
///
/// <para>
/// Stream ownership: the implementation owns the underlying transport
/// and closes it on <see cref="IDisposable.Dispose"/>. Callers read/write
/// the exposed streams but must not close them directly.
/// </para>
/// </summary>
public interface IScriptWorkerProcess : IDisposable
{
    /// <summary>Stream the host writes frames to. Closes on dispose.</summary>
    Stream StdinWrite { get; }

    /// <summary>Stream the host reads frames from. Closes on dispose.</summary>
    Stream StdoutRead { get; }

    /// <summary>
    /// Reader for the worker's stderr channel (or equivalent diagnostic
    /// channel on platforms without stderr).
    /// <see cref="OutOfProcessScriptHost"/> pumps this to the logger.
    /// </summary>
    StreamReader StderrReader { get; }

    /// <summary><c>true</c> once the worker has exited / unbound.</summary>
    bool HasExited { get; }

    /// <summary>
    /// Process exit code. Only defined once <see cref="HasExited"/> is
    /// <c>true</c>; may throw or return a platform-defined sentinel
    /// (e.g. <c>-1</c>) on implementations where a meaningful exit code
    /// isn't available (Android service unbind).
    /// </summary>
    int ExitCode { get; }

    /// <summary>
    /// Kill the worker. <paramref name="entireProcessTree"/> is honoured
    /// where the platform supports it (Windows, POSIX). On Android it
    /// maps to an immediate unbind + <c>Process.killProcess</c> for the
    /// isolated-process UID.
    /// </summary>
    bool Kill(bool entireProcessTree);

    /// <summary>
    /// Block up to <paramref name="milliseconds"/> waiting for the
    /// worker to exit. Returns <c>true</c> if it exited within the
    /// window, <c>false</c> otherwise.
    /// </summary>
    bool WaitForExit(int milliseconds);

    /// <summary>
    /// Refresh cached OS-level metrics (e.g. <see cref="WorkingSet64"/>).
    /// Idempotent — safe to call from the memory-quota poller.
    /// </summary>
    void Refresh();

    /// <summary>
    /// Current resident-set size in bytes. Used by
    /// <see cref="OutOfProcessScriptHost"/>'s memory-quota poller.
    /// Implementations that can't observe this (e.g. Android before
    /// querying <c>ActivityManager</c>) return <c>0</c>, which the
    /// poller treats as "no data" and skips.
    /// </summary>
    long WorkingSet64 { get; }

    /// <summary>
    /// Total processor time the worker has consumed (user + kernel). Used by
    /// <see cref="OutOfProcessScriptHost"/>'s CPU-quota poller: a tight loop
    /// inside the 5-second wall-clock budget would otherwise peg a core with
    /// no intervention. Implementations that cannot observe CPU time (Android
    /// isolated-process without elevated query permissions) should return
    /// <see cref="TimeSpan.Zero"/>, which the poller treats as "no data" and
    /// skips the check that tick.
    /// </summary>
    TimeSpan TotalProcessorTime { get; }
}
