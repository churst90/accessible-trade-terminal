using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace AccessibleTrader.Core.Services.Scripting;

/// <summary>
/// Linux launcher that spawns the ScriptWorker inside a <c>bubblewrap</c>
/// (<c>bwrap</c>) sandbox — the desktop-Linux / WebHost equivalent of the
/// Windows AppContainer and macOS <c>sandbox-exec</c> launchers. Until this
/// shipped, Linux fell through to <see cref="DefaultProcessLauncher"/> with
/// process-isolation only (no kernel-level filesystem/network denial); that
/// was the one platform where a Roslyn-sandbox escape could reach the user's
/// home directory and the network.
///
/// <para>
/// The sandbox baseline (per <c>docs/SANDBOX_DESIGN.md</c> and the L5 TODO):
/// </para>
/// <list type="bullet">
///   <item><description><c>--unshare-all</c> — new PID/IPC/UTS/cgroup and, crucially, <b>network</b> namespace, so a script can't open a socket to phone home.</description></item>
///   <item><description><c>--ro-bind / /</c> — the whole filesystem is read-only, so a script can't write, persist, or tamper with host files. Read access is not an exfiltration vector here: with no network and no writable mount, a hostile indicator has no channel out beyond its numeric result frames.</description></item>
///   <item><description><c>--tmpfs /tmp</c> — a private writable scratch for the .NET runtime (R2R / shadow-copy), discarded on exit.</description></item>
///   <item><description><c>--proc /proc</c>, <c>--dev /dev</c> — minimal pseudo-filesystems the CLR needs.</description></item>
///   <item><description><c>--die-with-parent</c>, <c>--new-session</c> — the worker dies with the host and can't perform TIOCSTI terminal injection.</description></item>
/// </list>
///
/// <para>
/// stdin/stdout/stderr are inherited file descriptors, so the existing stdio
/// frame protocol works unchanged inside the sandbox.
/// </para>
///
/// <para>
/// If <c>bwrap</c> isn't installed (it ships in most distros' <c>bubblewrap</c>
/// package but isn't guaranteed) the launcher falls back to
/// <see cref="DefaultProcessLauncher"/> and reports <see cref="SandboxApplied"/>
/// = <c>false</c> — the worker still runs out-of-process, just without the
/// kernel sandbox, matching the pre-L5 behaviour.
/// </para>
///
/// <para>
/// Hardening follow-ups (not required by the threat model, tracked for later):
/// overlay a <c>--tmpfs</c> on <c>$HOME</c> so scripts can't even read user
/// files; <c>--clearenv</c> with a minimal passthrough; and a <c>--seccomp</c>
/// BPF syscall whitelist as defence-in-depth.
/// </para>
/// </summary>
public sealed class LinuxBwrapLauncher : IScriptWorkerLauncher
{
    private readonly IScriptWorkerLauncher _fallback;
    private readonly string? _bwrapPath;

    /// <summary>
    /// <c>true</c> if the last <see cref="Launch"/> actually wrapped the worker
    /// in bwrap; <c>false</c> if it fell through to the unsandboxed default
    /// (bwrap missing, or not running on Linux).
    /// </summary>
    public bool SandboxApplied { get; private set; }

    public LinuxBwrapLauncher(IScriptWorkerLauncher? fallback = null)
    {
        _fallback = fallback ?? new DefaultProcessLauncher();
        _bwrapPath = FindBwrap();
    }

    public IScriptWorkerProcess Launch(string workerExecutablePath)
    {
        if (string.IsNullOrEmpty(workerExecutablePath))
            throw new ArgumentException("workerExecutablePath is required", nameof(workerExecutablePath));
        if (!File.Exists(workerExecutablePath))
            throw new FileNotFoundException("Worker executable not found.", workerExecutablePath);

        // bwrap only exists / makes sense on Linux. Anywhere else, defer.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || _bwrapPath is null)
        {
            SandboxApplied = false;
            return _fallback.Launch(workerExecutablePath);
        }

        var workerDir = Path.GetDirectoryName(workerExecutablePath) ?? AppContext.BaseDirectory;

        var psi = new ProcessStartInfo
        {
            FileName               = _bwrapPath,
            UseShellExecute        = false,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            WorkingDirectory       = workerDir,
        };

        foreach (var arg in BuildBwrapArgs(workerDir, workerExecutablePath))
            psi.ArgumentList.Add(arg);

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.Start();
        SandboxApplied = true;
        return new DotNetProcessAdapter(proc);
    }

    /// <summary>
    /// Builds the bwrap argument vector (excluding the leading bwrap path).
    /// Internal for unit testing the sandbox flags.
    /// </summary>
    internal static List<string> BuildBwrapArgs(string workerDir, string workerExecutablePath) => new()
    {
        "--unshare-all",        // no network (and new pid/ipc/uts/cgroup ns)
        "--die-with-parent",    // worker exits if the host dies
        "--new-session",        // detach controlling terminal — blocks TIOCSTI injection
        "--ro-bind", "/", "/",  // whole filesystem read-only
        "--proc", "/proc",
        "--dev", "/dev",
        "--tmpfs", "/tmp",      // private writable scratch for the CLR
        "--chdir", workerDir,
        "--",
        workerExecutablePath,
    };

    /// <summary>Resolves the bwrap binary from the common locations, then PATH.</summary>
    internal static string? FindBwrap()
    {
        foreach (var candidate in new[] { "/usr/bin/bwrap", "/bin/bwrap", "/usr/local/bin/bwrap" })
        {
            if (File.Exists(candidate)) return candidate;
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
        {
            foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var full = Path.Combine(dir, "bwrap");
                    if (File.Exists(full)) return full;
                }
                catch { /* malformed PATH entry — skip */ }
            }
        }
        return null;
    }
}
