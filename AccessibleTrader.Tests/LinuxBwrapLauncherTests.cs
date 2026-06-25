using System.Linq;
using AccessibleTrader.Core.Services.Scripting;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Pins the bubblewrap sandbox flags for the Linux script-worker launcher (L5).
/// These assert the argv shape rather than spawning bwrap, so they run on any OS.
/// </summary>
public class LinuxBwrapLauncherTests
{
    [Fact]
    public void BuildBwrapArgs_AppliesNetworkAndFilesystemIsolation()
    {
        var args = LinuxBwrapLauncher.BuildBwrapArgs("/opt/app", "/opt/app/ScriptWorker");

        // Network namespace unshared (no sockets → no exfiltration).
        Assert.Contains("--unshare-all", args);
        // Whole filesystem read-only.
        Assert.Equal(args.IndexOf("/"), args.IndexOf("--ro-bind") + 1);
        Assert.Contains("--ro-bind", args);
        // Private writable scratch, minimal pseudo-filesystems, and lifetime guard.
        Assert.Contains("--tmpfs", args);
        Assert.Contains("--proc", args);
        Assert.Contains("--dev", args);
        Assert.Contains("--die-with-parent", args);
        Assert.Contains("--new-session", args);
    }

    [Fact]
    public void BuildBwrapArgs_RunsTheWorkerAfterTheSeparator()
    {
        var args = LinuxBwrapLauncher.BuildBwrapArgs("/opt/app", "/opt/app/ScriptWorker");

        int sep = args.IndexOf("--");
        Assert.True(sep >= 0, "argv must contain a '--' separator before the command");
        // The worker executable is the program run inside the sandbox.
        Assert.Equal("/opt/app/ScriptWorker", args.Last());
        Assert.True(args.IndexOf("/opt/app/ScriptWorker") > sep);
        // cwd inside the sandbox is the worker directory.
        Assert.Equal("/opt/app", args[args.IndexOf("--chdir") + 1]);
    }
}
