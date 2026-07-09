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

    // ── Missing-bwrap refusal policy (Phase A security hardening) ─────────
    // A hostile custom indicator running unsandboxed can read the user's
    // files (including API-key storage) and reach the network, so the silent
    // DefaultProcessLauncher fallback is refused unless the user explicitly
    // opts in via ACCESSIBLETRADER_ALLOW_UNSANDBOXED_SCRIPTS.

    private sealed class RecordingLauncher : IScriptWorkerLauncher
    {
        public int LaunchCalls;
        public IScriptWorkerProcess Launch(string workerExecutablePath)
        {
            LaunchCalls++;
            throw new System.NotSupportedException("test fallback reached — stop before spawning anything");
        }
    }

    private static string CreateFakeWorkerFile()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"at-fake-worker-{System.Guid.NewGuid():N}");
        System.IO.File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void Launch_WithoutBwrap_AndNoOverride_RefusesInsteadOfFallingBack()
    {
        if (!System.OperatingSystem.IsLinux()) return; // refusal branch is Linux-only

        var fallback = new RecordingLauncher();
        var launcher = new LinuxBwrapLauncher(fallback, bwrapPath: null, allowUnsandboxed: false);
        var worker = CreateFakeWorkerFile();
        try
        {
            var ex = Assert.Throws<ScriptSandboxUnavailableException>(() => launcher.Launch(worker));
            // The message is shown verbatim in the Custom Scripts modal — it must
            // name the missing tool, the fix, and the explicit override.
            Assert.Contains("bwrap", ex.Message);
            Assert.Contains("bubblewrap", ex.Message);
            Assert.Contains(SandboxPolicy.OverrideEnvVar, ex.Message);
            Assert.Equal(0, fallback.LaunchCalls);
        }
        finally
        {
            System.IO.File.Delete(worker);
        }
    }

    [Fact]
    public void Launch_WithoutBwrap_WithExplicitOverride_UsesFallbackAndReportsUnsandboxed()
    {
        if (!System.OperatingSystem.IsLinux()) return;

        var fallback = new RecordingLauncher();
        var launcher = new LinuxBwrapLauncher(fallback, bwrapPath: null, allowUnsandboxed: true);
        var worker = CreateFakeWorkerFile();
        try
        {
            // The fallback stub throws NotSupportedException once reached, which
            // proves the override routed to the fallback rather than refusing.
            Assert.Throws<System.NotSupportedException>(() => launcher.Launch(worker));
            Assert.Equal(1, fallback.LaunchCalls);
            Assert.False(launcher.SandboxApplied);
        }
        finally
        {
            System.IO.File.Delete(worker);
        }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("no", false)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    public void SandboxPolicy_OverrideEnvVar_ParsesStrictly(string? value, bool expected)
    {
        Assert.Equal(expected, SandboxPolicy.IsOverrideValue(value));
    }

    [Fact]
    public void SandboxPolicy_EnforceOrThrow_ThrowsOnlyWithoutOverride()
    {
        Assert.Throws<ScriptSandboxUnavailableException>(
            () => SandboxPolicy.EnforceOrThrow(false, "details.", "remedy."));
        SandboxPolicy.EnforceOrThrow(true, "details.", "remedy."); // must not throw
    }
}
