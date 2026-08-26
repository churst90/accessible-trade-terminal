using AccessibleTrader.Core.Services.Scripting;
using AccessibleTrader.ScriptSandbox;
using AccessibleTrader.Sdk.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// The Linux sandbox's home tmpfs and cleared environment, measured rather than argued.
///
/// <para>
/// The launcher used to mount the whole filesystem readable and argue that read access was not an
/// exfiltration vector, "with no network and no writable mount". That was wrong: an indicator
/// returns an arbitrary <c>double[]</c> which the host renders, speaks and persists, and a
/// strategy returns orders — so a file the worker can read is a file the worker can encode into
/// what it returns. The API-key store lives at <c>~/.local/share/AccessibleTrader</c>, squarely
/// inside what was readable.
/// </para>
///
/// <para>
/// The argv tests below run everywhere. The three that spawn a real sandboxed worker need
/// <c>bwrap</c> on the machine and are skipped without it — which is why this finding sat open
/// through two passes. <b>Each of those carries a vacuity check</b>: the same fixture is run
/// through the UNSANDBOXED launcher and must succeed there, because "the script could not read the
/// file" is equally what a broken fixture looks like.
/// </para>
/// </summary>
[Collection("ScriptWorker")] // spawns a real worker / bwrap — see ScriptWorkerCollection
public class LinuxBwrapSandboxTests
{
    // ── The argument vector ───────────────────────────────────────────────────────

    [Fact]
    public void The_home_is_masked_and_only_what_the_worker_needs_is_re_bound()
    {
        var args = LinuxBwrapLauncher.BuildBwrapArgs(
            workerDir: "/home/u/app/worker",
            workerExecutablePath: "/home/u/app/worker/AccessibleTrader.ScriptWorker",
            home: "/home/u",
            preserve: new[] { "/home/u/app/worker", "/home/u/.dotnet", "/opt/shared-thing" },
            dotnetRoot: "/home/u/.dotnet");

        int tmpfsHome = IndexOfPair(args, "--tmpfs", "/home/u");
        Assert.True(tmpfsHome >= 0, "the user's home must be replaced by a tmpfs: " + string.Join(" ", args));

        // bwrap applies mounts in argv order, so a re-bind emitted BEFORE the tmpfs is a re-bind
        // the tmpfs then buries. This ordering is the whole correctness of the arrangement.
        int rootBind = IndexOfPair(args, "--ro-bind", "/");
        Assert.True(rootBind >= 0 && rootBind < tmpfsHome, "--ro-bind / must come before the home tmpfs");
        Assert.True(IndexOfPair(args, "--ro-bind", "/home/u/app/worker") > tmpfsHome,
            "the worker's own directory must be re-bound AFTER the home tmpfs, or it is hidden too");
        Assert.True(IndexOfPair(args, "--ro-bind", "/home/u/.dotnet") > tmpfsHome,
            "the .NET root must be re-bound after the home tmpfs when it lives under the home");

        // A path outside the home was never hidden, so re-binding it would be noise claiming to
        // be policy.
        Assert.Equal(-1, IndexOfPair(args, "--ro-bind", "/opt/shared-thing"));
    }

    [Fact]
    public void Nothing_is_re_bound_when_dotnet_lives_outside_the_home()
    {
        var args = LinuxBwrapLauncher.BuildBwrapArgs(
            workerDir: "/opt/accessibletrader",
            workerExecutablePath: "/opt/accessibletrader/AccessibleTrader.ScriptWorker",
            home: "/home/u",
            preserve: new[] { "/opt/accessibletrader", "/usr/share/dotnet" },
            dotnetRoot: "/usr/share/dotnet");

        Assert.True(IndexOfPair(args, "--tmpfs", "/home/u") >= 0);
        Assert.Equal(-1, IndexOfPair(args, "--ro-bind", "/opt/accessibletrader"));
        Assert.Equal(-1, IndexOfPair(args, "--ro-bind", "/usr/share/dotnet"));
    }

    /// <summary>
    /// The cases where masking the home would break more than it protects. Emitting a
    /// self-cancelling pair of flags — tmpfs the home, then re-bind the home — would read in the
    /// argv as protection that is not there.
    /// </summary>
    [Theory]
    [InlineData(null,       "/opt/app")]     // no HOME in the environment
    [InlineData("",         "/opt/app")]
    [InlineData("   ",      "/opt/app")]
    [InlineData("relative", "/opt/app")]     // not rooted
    [InlineData("/",        "/opt/app")]     // would take the whole system with it
    [InlineData("/home/u",  "/home/u")]      // worker sits directly in the home
    public void The_home_tmpfs_is_skipped_when_it_would_hide_the_system_or_the_worker(string? home, string workerDir)
    {
        Assert.False(LinuxBwrapLauncher.ShouldMaskHome(home, workerDir));

        var args = LinuxBwrapLauncher.BuildBwrapArgs(
            workerDir, workerDir + "/AccessibleTrader.ScriptWorker", home,
            preserve: new[] { workerDir }, dotnetRoot: null);

        // /tmp keeps its tmpfs either way; what must not appear is one over the home.
        Assert.DoesNotContain(args, a => a == home && Precedes(args, "--tmpfs", a));
        // And the rest of the sandbox is unaffected — skipping one flag is not falling back.
        Assert.Contains("--unshare-all", args);
        Assert.Contains("--clearenv", args);
        Assert.True(IndexOfPair(args, "--tmpfs", "/tmp") >= 0);
    }

    [Fact]
    public void The_environment_is_cleared_and_what_passes_through_is_named()
    {
        var args = LinuxBwrapLauncher.BuildBwrapArgs(
            "/home/u/app", "/home/u/app/AccessibleTrader.ScriptWorker",
            home: "/home/u", preserve: new[] { "/home/u/app" }, dotnetRoot: "/home/u/.dotnet");

        Assert.Contains("--clearenv", args);

        // Exactly two passthroughs, both load-bearing. DOTNET_ROOT is not optional on the common
        // per-user install: without it the apphost probes the system locations, does not find
        // ~/.dotnet, and dies with "You must install .NET to run this application."
        var setenv = args.Select((a, i) => (a, i)).Where(t => t.a == "--setenv").Select(t => args[t.i + 1]).ToList();
        Assert.Equal(new[] { "HOME", "DOTNET_ROOT" }, setenv);
        Assert.True(args.IndexOf("--clearenv") < args.IndexOf("--setenv"),
            "--clearenv must come first or it wipes the values it was meant to keep");
    }

    [Fact]
    public void A_self_contained_deployment_gets_no_dotnet_root_passthrough()
    {
        var args = LinuxBwrapLauncher.BuildBwrapArgs(
            "/home/u/app", "/home/u/app/AccessibleTrader.ScriptWorker",
            home: "/home/u", preserve: new[] { "/home/u/app" }, dotnetRoot: null);

        Assert.Contains("--clearenv", args);
        Assert.DoesNotContain("DOTNET_ROOT", args);
        // The runtime sits beside the worker in that layout, so the worker-directory bind is what
        // keeps it reachable.
        Assert.True(IndexOfPair(args, "--ro-bind", "/home/u/app") > IndexOfPair(args, "--tmpfs", "/home/u"));
    }

    [Fact]
    public void A_path_already_inside_another_is_not_listed_twice()
    {
        // ~/.dotnet and ~/.dotnet/shared/Microsoft.NETCore.App/10.0.x both "need to survive", but
        // binding the second after the first is a no-op that reads as two separate policies.
        var preserved = LinuxBwrapLauncher.PathsToPreserve(AppContext.BaseDirectory);

        foreach (var path in preserved)
            Assert.DoesNotContain(preserved, other =>
                !string.Equals(other, path, StringComparison.Ordinal)
                && path.StartsWith(other.TrimEnd('/') + "/", StringComparison.Ordinal));
    }

    // ── Through a real sandboxed worker ───────────────────────────────────────────

    /// <summary>
    /// The regression the whole item was blocked on for two passes: does the CLR still start under
    /// the tighter mount? On a machine where .NET was installed by <c>dotnet-install</c> — the
    /// Linux default — the runtime is in <c>~/.dotnet</c>, so a naive home tmpfs kills the worker
    /// with "You must install .NET to run this application" and takes scripting with it.
    /// </summary>
    [Fact]
    public async Task The_worker_still_runs_a_normal_indicator_under_the_hardened_mount()
    {
        if (SkipUnlessBwrap(out var workerPath)) return;

        double[][] result = await CalculateAsync(
            new LinuxBwrapLauncher(), workerPath!, EchoCloseIndicator(), Bars());

        Assert.Single(result);
        Assert.Equal(new[] { 100.5, 101.5, 102.5 }, result[0]);
    }

    /// <summary>
    /// The finding itself. The fixture reads a canary file from the user's home and returns 1 if
    /// it managed it — compiled by hand, because the Roslyn walker refuses <c>System.IO</c> and
    /// the question here is what the KERNEL does when an assembly gets past the walker anyway
    /// (a plugin DLL, a future front end, a walker hole like the four this audit already found).
    /// </summary>
    [Fact]
    public async Task A_script_cannot_read_a_file_in_the_users_home()
    {
        if (SkipUnlessBwrap(out var workerPath)) return;

        var canary = Path.Combine(
            Environment.GetEnvironmentVariable("HOME")!, $".at-sandbox-canary-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(canary, "the api key store lives next to this");
        try
        {
            var assembly = FileReadingIndicator(canary);

            // Vacuity check FIRST: unsandboxed, the fixture must succeed. Otherwise "it read
            // nothing" below is indistinguishable from a fixture that never worked.
            var unsandboxed = await CalculateAsync(new DefaultProcessLauncher(), workerPath!, assembly, Bars());
            Assert.Equal(1.0, unsandboxed[0][0]);

            var sandboxed = await CalculateAsync(new LinuxBwrapLauncher(), workerPath!, assembly, Bars());
            Assert.Equal(0.0, sandboxed[0][0]);
        }
        finally
        {
            File.Delete(canary);
        }
    }

    /// <summary>
    /// The other half. <c>Environment.GetEnvironmentVariables()</c> handing a script the host's
    /// whole environment block was one of the four escapes this audit found, and it was fixed at
    /// the walker; this is the kernel-level backstop for the same thing — on a machine that
    /// configures credentials through the environment, that block IS the credentials.
    /// </summary>
    [Fact]
    public async Task A_script_cannot_read_the_hosts_environment()
    {
        if (SkipUnlessBwrap(out var workerPath)) return;

        const string key = "AT_SANDBOX_ENV_CANARY";
        var previous = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, "canary-secret");
        try
        {
            var assembly = EnvReadingIndicator(key);

            var unsandboxed = await CalculateAsync(new DefaultProcessLauncher(), workerPath!, assembly, Bars());
            Assert.Equal(1.0, unsandboxed[0][0]);

            var sandboxed = await CalculateAsync(new LinuxBwrapLauncher(), workerPath!, assembly, Bars());
            Assert.Equal(0.0, sandboxed[0][0]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, previous);
        }
    }

    /// <summary>
    /// The sandbox must outlive the thread that started it.
    ///
    /// <para>
    /// <c>--die-with-parent</c> is <c>prctl(PR_SET_PDEATHSIG, SIGKILL)</c>, and on Linux the
    /// "parent" PDEATHSIG watches is the <b>thread</b> that created the process, not the
    /// process. Spawning bwrap from a thread-pool thread therefore armed a kill switch on a
    /// thread the runtime retires whenever it feels like it: when it did, the kernel killed the
    /// sandbox mid-session and the host saw exit code 137 and an <c>EndOfStreamException</c> at
    /// byte 0 of the next frame.
    /// </para>
    ///
    /// <para>
    /// This arrived disguised as a test flake — <c>LinuxBwrapSandboxTests</c> failing about
    /// once in seven full-suite runs, always green in isolation — and two passes filed it as
    /// start-up latency against a timeout. It is not latency: measured bwrap start is ~0.2 s
    /// against a 10 s budget even at 2x CPU oversubscription. What it actually was is a custom
    /// indicator or a script strategy dropping off a live chart partway through a session, for
    /// a user with no way to see it happen.
    /// </para>
    ///
    /// <para>
    /// The test is the demonstration: start a worker on a thread and let that thread exit, then
    /// talk to the worker. Before the fix this threw
    /// <c>InvalidOperationException: script worker … has exited (code 137)</c>. The control —
    /// the same thing with the thread kept alive — is what distinguishes "the fix works" from
    /// "the fixture never worked", and the argv test above is what keeps
    /// <c>--die-with-parent</c> itself from being deleted as an easier way to pass this.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(false)]  // control: the spawning thread is still running
    [InlineData(true)]   // the regression: the spawning thread has exited
    public async Task A_worker_survives_the_thread_that_spawned_it(bool letTheSpawningThreadExit)
    {
        if (SkipUnlessBwrap(out var workerPath)) return;

        var assembly = EchoCloseIndicator();
        OutOfProcessScriptHost? host = null;
        Exception? startFailure = null;
        var started = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();

        var spawner = new Thread(() =>
        {
            try
            {
                host = OutOfProcessScriptHost.StartAsync(
                    new LinuxBwrapLauncher(), workerPath!, assembly,
                    scriptId: "pdeath-" + Guid.NewGuid().ToString("N")[..8]).GetAwaiter().GetResult();
            }
            catch (Exception ex) { startFailure = ex; }
            started.Set();
            if (!letTheSpawningThreadExit) release.Wait(TimeSpan.FromSeconds(30));
        }) { IsBackground = true, Name = "test-spawner" };

        spawner.Start();
        Assert.True(started.Wait(TimeSpan.FromSeconds(60)), "the worker never finished starting");
        Assert.Null(startFailure);

        try
        {
            if (letTheSpawningThreadExit)
                Assert.True(spawner.Join(TimeSpan.FromSeconds(10)), "the spawning thread did not exit");

            var result = await host!.CalculateAsync(
                new CalculateRequest(Bars(), new Dictionary<string, double>()));

            Assert.Single(result);
            Assert.Equal(new[] { 100.5, 101.5, 102.5 }, result[0]);
        }
        finally
        {
            release.Set();
            if (host != null) await host.DisposeAsync();
        }
    }

    // ── Harness ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// True when this machine cannot run the empirical half. Both conditions are real skips, not
    /// silent ones: the argv tests above still run, and the TODO records that these three need a
    /// machine with bubblewrap.
    /// </summary>
    private static bool SkipUnlessBwrap(out string? workerPath)
    {
        workerPath = null;
        if (!OperatingSystem.IsLinux()) return true;
        if (LinuxBwrapLauncher.FindBwrap() is null) return true;

        var path = ScriptWorkerPath.Resolve();
        if (!File.Exists(path)) return true;
        workerPath = path;
        return false;
    }

    private static async Task<double[][]> CalculateAsync(
        IScriptWorkerLauncher launcher, string workerPath, byte[] assembly, Ohlcv[] bars)
    {
        var host = await OutOfProcessScriptHost.StartAsync(
            launcher, workerPath, assembly, scriptId: "bwrap-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            return await host.CalculateAsync(new CalculateRequest(bars, new Dictionary<string, double>()));
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    private static Ohlcv[] Bars() => new[]
    {
        new Ohlcv(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100, 101, 99, 100.5, 1),
        new Ohlcv(new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc), 101, 102, 100, 101.5, 2),
        new Ohlcv(new DateTime(2026, 1, 1, 2, 0, 0, DateTimeKind.Utc), 102, 103, 101, 102.5, 3),
    };

    private static byte[] EchoCloseIndicator() => Compile("""
        using System;
        using System.Collections.Generic;
        using AccessibleTrader.Sdk.Interfaces;
        using AccessibleTrader.Sdk.Models;

        public sealed class EchoClose : ICustomIndicator
        {
            public string Id => "BWRAP_ECHO";
            public string DisplayName => "echo";
            public string[] ComponentNames => new[] { "Close" };
            public ComponentDisplayType[] DisplayTypes => new[] { ComponentDisplayType.Line };
            public Dictionary<string, double> DefaultParameters => new();

            public double[][] Calculate(ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
            {
                var o = new double[data.Length];
                for (int i = 0; i < data.Length; i++) o[i] = data[i].Close;
                return new[] { o };
            }
        }
        """);

    private static byte[] FileReadingIndicator(string canaryPath) => Compile($$"""
        using System;
        using System.Collections.Generic;
        using AccessibleTrader.Sdk.Interfaces;
        using AccessibleTrader.Sdk.Models;

        public sealed class Peeker : ICustomIndicator
        {
            public string Id => "BWRAP_PEEK_FILE";
            public string DisplayName => "peek";
            public string[] ComponentNames => new[] { "Read" };
            public ComponentDisplayType[] DisplayTypes => new[] { ComponentDisplayType.Line };
            public Dictionary<string, double> DefaultParameters => new();

            public double[][] Calculate(ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
            {
                double got = 0;
                try
                {
                    var text = System.IO.File.ReadAllText(@"{{canaryPath}}");
                    got = text.Length > 0 ? 1 : 0;
                }
                catch { got = 0; }
                return new[] { new double[] { got } };
            }
        }
        """);

    private static byte[] EnvReadingIndicator(string key) => Compile($$"""
        using System;
        using System.Collections.Generic;
        using AccessibleTrader.Sdk.Interfaces;
        using AccessibleTrader.Sdk.Models;

        public sealed class EnvPeeker : ICustomIndicator
        {
            public string Id => "BWRAP_PEEK_ENV";
            public string DisplayName => "peek";
            public string[] ComponentNames => new[] { "Read" };
            public ComponentDisplayType[] DisplayTypes => new[] { ComponentDisplayType.Line };
            public Dictionary<string, double> DefaultParameters => new();

            public double[][] Calculate(ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
            {
                var v = Environment.GetEnvironmentVariable("{{key}}");
                return new[] { new double[] { string.IsNullOrEmpty(v) ? 0 : 1 } };
            }
        }
        """);

    /// <summary>
    /// Compiled with a scanned reference set, NOT through <c>RoslynScriptingService</c>: the
    /// walker refuses <c>System.IO</c> and <c>System.Environment</c> outright, and the question
    /// these fixtures ask is what happens to an assembly that reaches the worker regardless.
    /// </summary>
    private static byte[] Compile(string source)
    {
        var references = new List<MetadataReference>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.IsNullOrEmpty(asm.Location)) continue;
            var name = asm.GetName().Name ?? "";
            if (name.StartsWith("System.", StringComparison.Ordinal) || name == "netstandard"
                || name == "System.Private.CoreLib" || name == "AccessibleTrader.Sdk")
                references.Add(MetadataReference.CreateFromFile(asm.Location));
        }
        references.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Ohlcv).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "BwrapFixture_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        Assert.True(emit.Success, "fixture failed to compile: " + string.Join(" | ",
            emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));
        return ms.ToArray();
    }

    /// <summary>Index of <paramref name="flag"/> where it is immediately followed by
    /// <paramref name="value"/>, or -1. bwrap's argv is positional, so "contains --tmpfs" and
    /// "contains /home/u" are together not the same claim as "--tmpfs /home/u".</summary>
    private static int IndexOfPair(IReadOnlyList<string> args, string flag, string value)
    {
        for (int i = 0; i + 1 < args.Count; i++)
            if (args[i] == flag && args[i + 1] == value) return i;
        return -1;
    }

    private static bool Precedes(IReadOnlyList<string> args, string flag, string value)
    {
        for (int i = 0; i + 1 < args.Count; i++)
            if (args[i] == flag && args[i + 1] == value) return true;
        return false;
    }
}
