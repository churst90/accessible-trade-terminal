using System.Diagnostics;

namespace AccessibleTrader.Core.Services.Scripting;

/// <summary>
/// Default unsandboxed launcher. Spawns the worker exe as a regular child
/// process under the host's token — no OS-level capability restriction,
/// no network/filesystem blocking. The process boundary alone gives us:
///
/// <list type="bullet">
///   <item><description>isolated GC / ALC — user indicator faults don't crash the trading host</description></item>
///   <item><description>kill-on-timeout — <see cref="OutOfProcessScriptHost"/> enforces budgets</description></item>
///   <item><description>no shared handle table — user code can't see the host's credential service, sockets, or file handles</description></item>
/// </list>
///
/// A determined attacker whose script escapes the in-worker Roslyn
/// semantic sandbox and runs arbitrary .NET APIs can still touch the
/// file system and network from within the worker. That's what the
/// per-platform launchers (<see cref="WindowsAppContainerLauncher"/>,
/// <see cref="MacSandboxExecLauncher"/>, Android isolated-process)
/// add on top. This launcher is the fallback when none of those apply.
/// </summary>
public sealed class DefaultProcessLauncher : IScriptWorkerLauncher
{
    public IScriptWorkerProcess Launch(string workerExecutablePath)
    {
        if (string.IsNullOrEmpty(workerExecutablePath))
            throw new System.ArgumentException("workerExecutablePath is required", nameof(workerExecutablePath));
        if (!File.Exists(workerExecutablePath))
            throw new FileNotFoundException("Worker executable not found.", workerExecutablePath);

        var psi = new ProcessStartInfo
        {
            FileName               = workerExecutablePath,
            UseShellExecute        = false,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            WorkingDirectory       = Path.GetDirectoryName(workerExecutablePath) ?? System.AppContext.BaseDirectory,
        };

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.Start();
        return new DotNetProcessAdapter(proc);
    }
}
