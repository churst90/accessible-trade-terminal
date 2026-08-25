using System.Diagnostics;
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
///   <item><description><c>--ro-bind / /</c> — the whole filesystem is read-only, so a script can't write, persist, or tamper with host files.</description></item>
///   <item><description><c>--tmpfs $HOME</c> — the user's home is replaced by an empty, private, ephemeral filesystem, so the API-key store (<c>~/.local/share/AccessibleTrader</c>), the workspaces, the browser profiles and the SSH keys are not merely unwritable but <b>not there</b>. See the remarks below for why the earlier "read access is not an exfiltration vector" reasoning was wrong.</description></item>
///   <item><description><c>--clearenv</c> plus a named passthrough — the worker does not inherit the host's environment block, which on a machine that configures credentials that way IS the credentials.</description></item>
///   <item><description><c>--tmpfs /tmp</c> — a private writable scratch for the .NET runtime (R2R / shadow-copy), discarded on exit.</description></item>
///   <item><description><c>--proc /proc</c>, <c>--dev /dev</c> — minimal pseudo-filesystems the CLR needs.</description></item>
///   <item><description><c>--die-with-parent</c>, <c>--new-session</c> — the worker dies with the host and can't perform TIOCSTI terminal injection.</description></item>
/// </list>
///
/// <para>
/// <b>Why the home tmpfs, when the mount was already read-only.</b> This class used to argue that
/// read access was harmless because "with no network and no writable mount, a hostile indicator
/// has no channel out beyond its numeric result frames". The result frames ARE a channel: an
/// indicator returns an arbitrary <c>double[]</c> that the host then renders, speaks and persists,
/// and a strategy returns orders. A file the worker can read is a file the worker can encode into
/// what it returns. So the fix is not to make the home unwritable, it is to make it absent.
/// </para>
///
/// <para>
/// <b>What has to survive the tmpfs.</b> On a machine where .NET was installed by the
/// <c>dotnet-install</c> script — the default on Linux — the runtime lives in <c>~/.dotnet</c>,
/// and an app installed per-user lives under the home too. A naive <c>--tmpfs $HOME</c> therefore
/// hides the worker, or the runtime it needs, and scripting stops working for exactly the users
/// who installed the ordinary way. <see cref="PathsToPreserve"/> resolves the worker's own
/// directory and the .NET root, and each one that falls under the home is re-bound read-only
/// AFTER the tmpfs (bwrap applies mounts in argv order). Nothing else is: an app installed to
/// <c>/opt</c> or <c>/usr</c> is already covered by <c>--ro-bind / /</c>, which the tmpfs does not
/// touch.
/// </para>
///
/// <para>
/// stdin/stdout/stderr are inherited file descriptors, so the existing stdio
/// frame protocol works unchanged inside the sandbox.
/// </para>
///
/// <para>
/// If <c>bwrap</c> isn't installed (it ships in most distros' <c>bubblewrap</c>
/// package but isn't guaranteed) the launcher REFUSES to run the worker and
/// throws <see cref="ScriptSandboxUnavailableException"/> with an install
/// hint — an unsandboxed worker could read the user's files (including
/// API-key storage) and reach the network, and before 2026-07 that downgrade
/// happened silently. Setting <c>ACCESSIBLETRADER_ALLOW_UNSANDBOXED_SCRIPTS=1</c>
/// restores the old fallback behaviour (<see cref="SandboxApplied"/> =
/// <c>false</c>), and every launch under the override is recorded to the
/// security event log.
/// </para>
///
/// <para>
/// Hardening follow-up still outstanding: a <c>--seccomp</c> BPF syscall whitelist as
/// defence-in-depth. It needs a compiled BPF program shipped alongside the worker and a
/// per-architecture syscall set, which is a different size of job from the mount flags.
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
        : this(fallback, FindBwrap(), allowUnsandboxed: null)
    {
    }

    /// <summary>
    /// Test seam: inject the resolved bwrap path and the unsandboxed-override
    /// decision so refusal behaviour is verifiable on any machine.
    /// </summary>
    internal LinuxBwrapLauncher(IScriptWorkerLauncher? fallback, string? bwrapPath, bool? allowUnsandboxed)
    {
        _fallback = fallback ?? new DefaultProcessLauncher();
        _bwrapPath = bwrapPath;
        _allowUnsandboxed = allowUnsandboxed;
    }

    private readonly bool? _allowUnsandboxed;

    public IScriptWorkerProcess Launch(string workerExecutablePath)
    {
        if (string.IsNullOrEmpty(workerExecutablePath))
            throw new ArgumentException("workerExecutablePath is required", nameof(workerExecutablePath));
        if (!File.Exists(workerExecutablePath))
            throw new FileNotFoundException("Worker executable not found.", workerExecutablePath);

        // bwrap only exists / makes sense on Linux. Anywhere else, defer —
        // CreateDefaultLauncher never selects this launcher off-Linux, so this
        // branch is only reachable in tests or manual composition.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            SandboxApplied = false;
            return _fallback.Launch(workerExecutablePath);
        }

        if (_bwrapPath is null)
        {
            // No kernel sandbox available. Refuse unless the user explicitly
            // accepted unsandboxed execution; never downgrade silently.
            SandboxPolicy.EnforceOrThrow(
                _allowUnsandboxed ?? SandboxPolicy.AllowUnsandboxedFallback,
                details: "the 'bwrap' binary was not found on this system.",
                remedy: "Install your distribution's 'bubblewrap' package (e.g. apt/dnf/pacman/emerge install bubblewrap) and restart.");

            SandboxPolicy.RecordUnsandboxedFallback(nameof(LinuxBwrapLauncher), "bwrap not found");
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
    /// Builds the bwrap argument vector (excluding the leading bwrap path), resolving the home
    /// directory and the paths that must survive the home tmpfs from the running environment.
    /// </summary>
    internal static List<string> BuildBwrapArgs(string workerDir, string workerExecutablePath) =>
        BuildBwrapArgs(workerDir, workerExecutablePath,
            Environment.GetEnvironmentVariable("HOME"),
            PathsToPreserve(workerDir),
            DotNetRoot());

    /// <summary>
    /// Test seam over <see cref="BuildBwrapArgs(string, string)"/>: the home directory, the
    /// must-survive paths and the .NET root are injected, so the argv shape is verifiable on a
    /// machine whose home layout is nothing like the one under test.
    /// </summary>
    /// <param name="home">
    /// The user's home. When null, empty, relative, <c>/</c>, or equal to the worker's own
    /// directory, the home tmpfs is SKIPPED — hiding <c>/</c> would take the whole system with it,
    /// and a worker that lives directly in the home cannot be both hidden and executable. Skipping
    /// loudly beats shipping a flag combination that does not do what it claims.
    /// </param>
    /// <param name="preserve">Directories re-bound read-only after the tmpfs, if they fall under
    /// <paramref name="home"/>. See <see cref="PathsToPreserve"/>.</param>
    /// <param name="dotnetRoot">Value for the <c>DOTNET_ROOT</c> passthrough, or null to omit it.</param>
    internal static List<string> BuildBwrapArgs(
        string workerDir,
        string workerExecutablePath,
        string? home,
        IReadOnlyList<string> preserve,
        string? dotnetRoot)
    {
        var args = new List<string>
        {
            "--unshare-all",        // no network (and new pid/ipc/uts/cgroup ns)
            "--die-with-parent",    // worker exits if the host dies
            "--new-session",        // detach controlling terminal — blocks TIOCSTI injection
        };

        // The host's environment block is not the worker's business, and on a machine that
        // configures credentials that way it is the credentials. Everything the worker needs is
        // named explicitly; anything a future need adds has to be added here, deliberately.
        args.Add("--clearenv");
        if (!string.IsNullOrEmpty(home))
            args.AddRange(new[] { "--setenv", "HOME", home });
        if (!string.IsNullOrEmpty(dotnetRoot))
            // Load-bearing under --clearenv on the common per-user install: without it the
            // apphost probes the system locations, does not find ~/.dotnet, and dies with
            // "You must install .NET to run this application."
            args.AddRange(new[] { "--setenv", "DOTNET_ROOT", dotnetRoot });

        args.AddRange(new[] { "--ro-bind", "/", "/" });   // whole filesystem read-only

        if (ShouldMaskHome(home, workerDir))
        {
            // Order matters: bwrap applies mounts as it reads them, so the tmpfs must land before
            // the re-binds or they are the things that get hidden.
            args.AddRange(new[] { "--tmpfs", home! });
            foreach (var path in preserve)
                if (IsUnder(path, home!))
                    args.AddRange(new[] { "--ro-bind", path, path });
        }

        args.AddRange(new[] { "--proc", "/proc" });
        args.AddRange(new[] { "--dev", "/dev" });
        args.AddRange(new[] { "--tmpfs", "/tmp" });      // private writable scratch for the CLR
        args.AddRange(new[] { "--chdir", workerDir });
        args.Add("--");
        args.Add(workerExecutablePath);
        return args;
    }

    /// <summary>
    /// Whether the home tmpfs can be applied at all. See the <paramref name="home"/> parameter of
    /// <see cref="BuildBwrapArgs(string, string, string?, IReadOnlyList{string}, string?)"/>.
    /// </summary>
    internal static bool ShouldMaskHome(string? home, string workerDir)
    {
        if (string.IsNullOrWhiteSpace(home)) return false;
        if (!Path.IsPathRooted(home)) return false;

        var normalized = Normalize(home);
        if (normalized == "/") return false;

        // A worker sitting directly in the home would need the home re-bound to run, which undoes
        // the tmpfs. Nothing ships that way, but silently emitting a self-cancelling pair of flags
        // is worse than declining and saying so in the flags that ARE emitted.
        return Normalize(workerDir) != normalized;
    }

    /// <summary>
    /// The directories the worker cannot run without: its own, and wherever .NET lives. Each is
    /// re-bound read-only if it falls under the home tmpfs. Paths that are already contained in
    /// another entry are dropped, so the argv says each thing once.
    /// </summary>
    internal static IReadOnlyList<string> PathsToPreserve(string workerDir)
    {
        var candidates = new List<string>();

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return;
            var normalized = Normalize(path);
            if (normalized.Length == 0 || normalized == "/") return;
            if (!candidates.Contains(normalized, StringComparer.Ordinal)) candidates.Add(normalized);
        }

        Add(workerDir);
        Add(DotNetRoot());
        // The shared-framework directory itself, for layouts DotNetRoot() cannot decompose
        // (a self-contained publish, or a distro that arranges things its own way).
        try { Add(Path.GetDirectoryName(typeof(object).Assembly.Location)); } catch { /* single-file: no location */ }

        // Drop anything already covered by a shorter entry — binding ~/.dotnet and then
        // ~/.dotnet/shared/Microsoft.NETCore.App/10.0.x is a no-op that reads as two policies.
        return candidates
            .Where(p => !candidates.Any(other => !ReferenceEquals(other, p)
                                                 && !string.Equals(other, p, StringComparison.Ordinal)
                                                 && IsUnder(p, other)))
            .ToList();
    }

    /// <summary>
    /// Where the .NET root is, for the <c>DOTNET_ROOT</c> passthrough and the re-bind.
    /// <c>DOTNET_ROOT</c> if the host has one; otherwise derived from the shared-framework layout
    /// <c>&lt;root&gt;/shared/Microsoft.NETCore.App/&lt;version&gt;</c>. Null for a self-contained
    /// deployment, which has no root to point at and does not need one — the runtime sits beside
    /// the worker and is covered by the worker-directory bind.
    /// </summary>
    internal static string? DotNetRoot()
    {
        var fromEnv = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Path.IsPathRooted(fromEnv) && Directory.Exists(fromEnv))
            return Normalize(fromEnv);

        string? runtimeDir;
        try { runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location); }
        catch { return null; }
        if (string.IsNullOrEmpty(runtimeDir)) return null;

        // <root>/shared/Microsoft.NETCore.App/<version>  →  up three.
        var version   = Path.GetDirectoryName(runtimeDir);
        var product   = Path.GetDirectoryName(version);
        var candidate = Path.GetDirectoryName(product);
        if (string.IsNullOrEmpty(candidate)) return null;
        if (!string.Equals(Path.GetFileName(version), "Microsoft.NETCore.App", StringComparison.Ordinal)) return null;
        if (!string.Equals(Path.GetFileName(product), "shared", StringComparison.Ordinal)) return null;
        return Directory.Exists(candidate) ? Normalize(candidate) : null;
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="ancestor"/> or sits inside it.</summary>
    private static bool IsUnder(string path, string ancestor)
    {
        var p = Normalize(path);
        var a = Normalize(ancestor);
        if (a.Length == 0) return false;
        if (string.Equals(p, a, StringComparison.Ordinal)) return true;
        return p.StartsWith(a.EndsWith('/') ? a : a + "/", StringComparison.Ordinal);
    }

    /// <summary>Absolute, with any trailing separator removed so prefix comparisons are honest.</summary>
    private static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        return full.Length > 1 ? full.TrimEnd('/') : full;
    }

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
