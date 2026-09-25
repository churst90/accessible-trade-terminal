using AccessibleTrader.Core.Services.Scripting;
using AccessibleTrader.Sdk.Services;

namespace AccessibleTrader.Tests;

/// <summary>
/// The script sandbox's REFUSAL policy as the shipped code reaches it — not only through the test
/// seam that injects the decision.
///
/// <para>
/// <b>Why this file exists (A2n, 2026-09-24).</b> <c>LinuxBwrapLauncherTests</c> pins the refusal
/// and the override, but always through the internal constructor that is HANDED
/// <c>allowUnsandboxed: true/false</c>. The branch production takes — no injected decision, so the
/// environment variable decides — was never run: replacing it with "allow" survived the full suite.
/// Likewise nothing checked that an override leaves a security event (the whole reason the override
/// is tolerable), that the platform-refusing launcher refuses, that a launcher which forgets
/// <c>SandboxApplied</c> reads as UNsandboxed, or that macOS refuses a missing
/// <c>sandbox-exec</c> at all — macOS code cannot run here, and it had no seam.
/// </para>
/// </summary>
[Collection("ProviderCredentialBridge")] // touches the global PluginHostServices.SecurityEvents
public class SandboxRefusalPolicyTests
{
    private sealed class RecordingLauncher : IScriptWorkerLauncher
    {
        public int LaunchCalls;
        public IScriptWorkerProcess Launch(string workerExecutablePath)
        {
            LaunchCalls++;
            throw new NotSupportedException("test fallback reached — stop before spawning anything");
        }
    }

    private sealed class RecordingLog : ISecurityEventLog
    {
        public readonly List<SecurityEvent> Events = new();
        public void Record(SecurityEvent ev) { lock (Events) Events.Add(ev); }
        public IReadOnlyList<SecurityEvent> Recent(int limit = 200, DateTime? since = null)
        { lock (Events) return Events.ToList(); }
    }

    private static string FakeWorker()
    {
        var path = TestTemp.NewPath("at-fake-worker-");
        File.WriteAllText(path, "");
        return path;
    }

    /// <summary>
    /// With no decision injected and the override variable UNSET, a missing bwrap refuses. This is
    /// the path every Linux install and the WebHost take.
    /// </summary>
    [Fact]
    public void Without_bwrap_the_shipped_launcher_refuses_unless_the_variable_is_set()
    {
        if (!OperatingSystem.IsLinux()) return; // the refusal branch is Linux-only

        var previous = Environment.GetEnvironmentVariable(SandboxPolicy.OverrideEnvVar);
        var worker = FakeWorker();
        try
        {
            Environment.SetEnvironmentVariable(SandboxPolicy.OverrideEnvVar, null);
            var fallback = new RecordingLauncher();
            var launcher = new LinuxBwrapLauncher(fallback, bwrapPath: null, allowUnsandboxed: null);

            Assert.Throws<ScriptSandboxUnavailableException>(() => launcher.Launch(worker));
            Assert.Equal(0, fallback.LaunchCalls);

            // Control: the same shipped path honours the variable when it IS set, so the refusal
            // above is the variable being read, not a launcher that can only refuse.
            Environment.SetEnvironmentVariable(SandboxPolicy.OverrideEnvVar, "1");
            Assert.Throws<NotSupportedException>(() => launcher.Launch(worker));
            Assert.Equal(1, fallback.LaunchCalls);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SandboxPolicy.OverrideEnvVar, previous);
            File.Delete(worker);
        }
    }

    /// <summary>
    /// Running unsandboxed under the override is tolerable only because it is never invisible:
    /// every such launch records an <see cref="SecurityEventKind.UnsandboxedScriptOverride"/>.
    /// </summary>
    [Fact]
    public void An_unsandboxed_launch_under_the_override_is_recorded_as_a_security_event()
    {
        if (!OperatingSystem.IsLinux()) return;

        var prior = PluginHostServices.SecurityEvents;
        var worker = FakeWorker();
        try
        {
            // The log is a process-wide static that a WebHost start-up elsewhere in the suite can
            // replace. Retry only when THAT happened (the global is no longer ours) — never when
            // the event is simply missing, which is the failure this test exists to report.
            for (int attempt = 0; ; attempt++)
            {
                var log = new RecordingLog();
                PluginHostServices.SecurityEvents = log;
                var launcher = new LinuxBwrapLauncher(new RecordingLauncher(), bwrapPath: null, allowUnsandboxed: true);
                Assert.Throws<NotSupportedException>(() => launcher.Launch(worker));

                bool stillOurs = ReferenceEquals(PluginHostServices.SecurityEvents, log);
                var ev = log.Recent().Where(e => e.Kind == SecurityEventKind.UnsandboxedScriptOverride).ToList();
                if (ev.Count == 0 && !stillOurs && attempt < 5) continue;

                var one = Assert.Single(ev);
                Assert.Equal(nameof(LinuxBwrapLauncher), one.Source);
                Assert.Contains(SandboxPolicy.OverrideEnvVar, one.Message);
                break;
            }
        }
        finally
        {
            PluginHostServices.SecurityEvents = prior;
            File.Delete(worker);
        }
    }

    /// <summary>
    /// iOS and macCatalyst refuse scripting outright, BEFORE anything is launched — the path is a
    /// file that does not exist, so a launcher that tried to run it would fail differently.
    /// </summary>
    [Theory]
    [InlineData("iOS")]
    [InlineData("macCatalyst")]
    public void The_refusing_launcher_refuses_without_launching(string platform)
    {
        var launcher = new RefusingScriptWorkerLauncher(platform);
        var ex = Assert.Throws<ScriptingNotSupportedOnPlatformException>(
            () => launcher.Launch("/nonexistent/at-worker-" + Guid.NewGuid().ToString("N")));
        Assert.Equal(platform, ex.Platform);
        Assert.False(((IScriptWorkerLauncher)launcher).SandboxApplied);
    }

    /// <summary>
    /// The interface default is the conservative one: a launcher that does not say it sandboxed
    /// its worker has NOT. The unsandboxed default launcher relies on that default.
    /// </summary>
    [Fact]
    public void A_launcher_that_does_not_claim_a_sandbox_reads_as_unsandboxed()
    {
        Assert.False(((IScriptWorkerLauncher)new DefaultProcessLauncher()).SandboxApplied);
    }

    /// <summary>
    /// macOS: a missing <c>sandbox-exec</c> (or a missing profile) refuses exactly as a missing
    /// bwrap does on Linux. Reached through the seam because macOS cannot be run here.
    /// </summary>
    [Fact]
    public void On_macOS_a_missing_sandbox_exec_refuses_unless_overridden()
    {
        var worker = FakeWorker();
        try
        {
            var fallback = new RecordingLauncher();
            var refusing = new MacSandboxExecLauncher(fallback, isMacOS: true,
                sandboxExecPath: "/nonexistent/sandbox-exec", allowUnsandboxed: false);
            var ex = Assert.Throws<ScriptSandboxUnavailableException>(() => refusing.Launch(worker));
            Assert.Contains("sandbox-exec", ex.Message);
            Assert.Equal(0, fallback.LaunchCalls);

            // sandbox-exec present but the deny-default profile missing: still a refusal. (The
            // "tool" here is any existing file — the refusal fires before anything is run.)
            var noProfile = new MacSandboxExecLauncher(fallback, isMacOS: true,
                sandboxExecPath: worker, allowUnsandboxed: false);
            var ex2 = Assert.Throws<ScriptSandboxUnavailableException>(() => noProfile.Launch(worker));
            Assert.Contains("sandbox profile", ex2.Message);
            Assert.Equal(0, fallback.LaunchCalls);

            // Control: the explicit override reaches the fallback and says so.
            var allowed = new MacSandboxExecLauncher(fallback, isMacOS: true,
                sandboxExecPath: "/nonexistent/sandbox-exec", allowUnsandboxed: true);
            Assert.Throws<NotSupportedException>(() => allowed.Launch(worker));
            Assert.Equal(1, fallback.LaunchCalls);
            Assert.False(allowed.SandboxApplied);
        }
        finally
        {
            File.Delete(worker);
        }
    }
}
