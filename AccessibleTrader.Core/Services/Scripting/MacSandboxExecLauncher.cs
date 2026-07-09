using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace AccessibleTrader.Core.Services.Scripting;

/// <summary>
/// macOS / Mac Catalyst launcher that spawns the ScriptWorker under the
/// <c>sandbox-exec</c> profile in <c>sandbox-profiles/script-worker.sb</c>
/// (shipped next to the worker binary).
///
/// The profile starts from <c>(deny default)</c> and grants only:
/// <list type="bullet">
///   <item><description>read access to system libraries (<c>/usr/lib</c>, <c>/System/Library</c>) and the worker's own directory,</description></item>
///   <item><description>read + write access to <c>TMPDIR</c> (for .NET's ReadyToRun / shadow-copy cache),</description></item>
///   <item><description>self-signal, process-info-self, and <c>com.apple.system.logger</c> mach-lookup.</description></item>
/// </list>
///
/// Everything else — network, outbound file writes, mach-lookup to
/// anything else, process-exec — is denied by the OS. A successful
/// escape of the in-worker Roslyn semantic sandbox therefore still can't
/// phone home, persist files, or reach the host's Keychain.
///
/// <para>
/// If <c>sandbox-exec</c> itself isn't available (it's a macOS standard
/// binary at <c>/usr/bin/sandbox-exec</c>, but some hardened corporate
/// images mask it) or the profile file is missing, the launcher REFUSES
/// to run the worker and throws
/// <see cref="ScriptSandboxUnavailableException"/> — unsandboxed
/// execution must never happen silently. Setting
/// <c>ACCESSIBLETRADER_ALLOW_UNSANDBOXED_SCRIPTS=1</c> restores the old
/// fallback (<see cref="SandboxApplied"/> = <c>false</c>) and records a
/// security event on every launch.
/// </para>
/// </summary>
public sealed class MacSandboxExecLauncher : IScriptWorkerLauncher
{
    private const string SandboxExecPath = "/usr/bin/sandbox-exec";
    private const string ProfileRelativePath = "sandbox-profiles/script-worker.sb";

    private readonly IScriptWorkerLauncher _fallback;

    /// <summary>
    /// <c>true</c> if the last <see cref="Launch"/> successfully applied
    /// the sandbox profile; <c>false</c> if it fell through to the
    /// default unsandboxed launcher.
    /// </summary>
    public bool SandboxApplied { get; private set; }

    public MacSandboxExecLauncher(IScriptWorkerLauncher? fallback = null)
    {
        _fallback = fallback ?? new DefaultProcessLauncher();
    }

    public IScriptWorkerProcess Launch(string workerExecutablePath)
    {
        if (string.IsNullOrEmpty(workerExecutablePath))
            throw new ArgumentException("workerExecutablePath is required", nameof(workerExecutablePath));
        if (!File.Exists(workerExecutablePath))
            throw new FileNotFoundException("Worker executable not found.", workerExecutablePath);

        // Platform guard: sandbox-exec only exists on macOS. On any other
        // OS the fallback path is the correct behaviour.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            SandboxApplied = false;
            return _fallback.Launch(workerExecutablePath);
        }

        var workerDir = Path.GetDirectoryName(workerExecutablePath) ?? AppContext.BaseDirectory;
        var profilePath = Path.Combine(workerDir, ProfileRelativePath);

        if (!File.Exists(SandboxExecPath) || !File.Exists(profilePath))
        {
            var missing = !File.Exists(SandboxExecPath)
                ? $"'{SandboxExecPath}' is not available on this system."
                : $"the sandbox profile '{ProfileRelativePath}' is missing next to the worker binary.";
            SandboxPolicy.EnforceOrThrow(
                SandboxPolicy.AllowUnsandboxedFallback,
                details: missing,
                remedy: "Reinstall the application (the profile ships with it), or ask your administrator to unmask sandbox-exec.");

            SandboxPolicy.RecordUnsandboxedFallback(nameof(MacSandboxExecLauncher), missing);
            SandboxApplied = false;
            return _fallback.Launch(workerExecutablePath);
        }

        var psi = new ProcessStartInfo
        {
            FileName               = SandboxExecPath,
            UseShellExecute        = false,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            WorkingDirectory       = workerDir,
        };

        // The profile references (param "WORKER_DIR") and (param "TMPDIR")
        // — sandbox-exec expands `-D KEY=VALUE` pairs into those. TMPDIR
        // defaults to the system temp if the environment doesn't set it.
        psi.ArgumentList.Add("-D");
        psi.ArgumentList.Add($"WORKER_DIR={workerDir}");
        psi.ArgumentList.Add("-D");
        psi.ArgumentList.Add($"TMPDIR={Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)}");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add(profilePath);
        psi.ArgumentList.Add(workerExecutablePath);

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.Start();
        SandboxApplied = true;
        return new DotNetProcessAdapter(proc);
    }
}
