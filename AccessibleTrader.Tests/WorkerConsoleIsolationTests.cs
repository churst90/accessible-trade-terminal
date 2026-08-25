using System.Text;
using AccessibleTrader.Core.Services.Scripting;
using AccessibleTrader.ScriptSandbox;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// The worker's stdout is the IPC pipe, and the user's indicator runs in the same process.
///
/// <para>
/// So a script that prints — the most natural way there is to debug one — wrote raw text into
/// the middle of a binary frame stream. The host read the first four bytes of "hello" as a
/// length prefix and reported a malformed stream or desynced outright, and the author got an
/// error with no relationship to what they had done. The compile-time refusal (see
/// <c>HostileScriptTests.Rejects_ConsoleWrites</c>) is the half that TELLS them; this is the
/// half that makes it safe when the refusal is not reached — a plugin DLL, a future scripting
/// front end, or any path that hands the worker an assembly the Roslyn walker never saw.
/// </para>
/// </summary>
public class WorkerConsoleIsolationTests
{
    [Fact]
    public void IsolateConsole_SendsPrintsToTheDiagnosticSink()
    {
        var original    = Console.Out;
        var originalErr = Console.Error;
        var originalIn  = Console.In;
        try
        {
            var sink = new MemoryStream();
            WorkerDispatcher.IsolateConsole(sink);

            Console.WriteLine("a script printed this");
            Console.Error.WriteLine("and this");

            string captured = Encoding.UTF8.GetString(sink.ToArray());
            Assert.Contains("a script printed this", captured);
            Assert.Contains("and this", captured);
        }
        finally
        {
            Console.SetOut(original);
            Console.SetError(originalErr);
            Console.SetIn(originalIn);
        }
    }

    [Fact]
    public void IsolateConsole_ClosesConsoleInputSoAScriptCannotEatACommandFrame()
    {
        var original   = Console.Out;
        var originalIn = Console.In;
        try
        {
            WorkerDispatcher.IsolateConsole(new MemoryStream());

            // Console.ReadLine() would otherwise consume from the same fd the host sends
            // LoadAssembly and Calculate frames on. TextReader.Null is at EOF forever.
            Assert.Null(Console.In.ReadLine());
        }
        finally
        {
            Console.SetOut(original);
            Console.SetIn(originalIn);
        }
    }

    [Fact]
    public void IsolateConsole_RefusesANullSink()
    {
        Assert.Throws<ArgumentNullException>(() => WorkerDispatcher.IsolateConsole(null!));
    }

    /// <summary>
    /// End to end, through the real worker process: an indicator that prints on every bar must
    /// still return its values. This is the test that would have caught the bug — the unit tests
    /// above pin the mechanism, but only a real spawn proves the frame stream survives.
    /// </summary>
    [Fact]
    public async Task AnIndicatorThatPrintsStillReturnsItsValuesThroughTheRealWorker()
    {
        string workerPath = ScriptWorkerPath.Resolve();
        Assert.True(File.Exists(workerPath),
            $"ScriptWorker executable not found at '{workerPath}' — build AccessibleTrader.ScriptWorker.");

        // Compiled by hand rather than through RoslynScriptingService: the sandbox now refuses
        // System.Console at compile time, and the point here is what the WORKER does when an
        // assembly containing a print reaches it anyway.
        byte[] assembly = CompilePrintingIndicator();

        var host = await OutOfProcessScriptHost.StartAsync(
            new DefaultProcessLauncher(), workerPath, assembly, scriptId: "print-test");
        try
        {
            var bars = new[]
            {
                new Ohlcv(DateTime.UtcNow.AddMinutes(-2), 100, 101, 99, 100.5, 1),
                new Ohlcv(DateTime.UtcNow.AddMinutes(-1), 100.5, 102, 100, 101.0, 2),
                new Ohlcv(DateTime.UtcNow,                101.0, 103, 100.5, 102.0, 3),
            };

            double[][] result = await host.CalculateAsync(
                new CalculateRequest(bars, new Dictionary<string, double>()));

            Assert.Single(result);
            Assert.Equal(bars.Select(b => b.Close), result[0]);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    private static byte[] CompilePrintingIndicator()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using AccessibleTrader.Sdk.Interfaces;
            using AccessibleTrader.Sdk.Models;

            public sealed class PrintingIndicator : ICustomIndicator
            {
                public string Id => "PRINTS";
                public string DisplayName => "prints";
                public string[] ComponentNames => new[] { "Close" };
                public ComponentDisplayType[] DisplayTypes => new[] { ComponentDisplayType.Line };
                public Dictionary<string, double> DefaultParameters => new();

                public double[][] Calculate(ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
                {
                    var outp = new double[data.Length];
                    for (int i = 0; i < data.Length; i++)
                    {
                        Console.WriteLine($"bar {i} close {data[i].Close}");
                        outp[i] = data[i].Close;
                    }
                    return new[] { outp };
                }
            }
            """;

        var references = new List<Microsoft.CodeAnalysis.MetadataReference>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.IsNullOrEmpty(asm.Location)) continue;
            var name = asm.GetName().Name ?? "";
            if (name.StartsWith("System.", StringComparison.Ordinal) || name == "netstandard"
                || name == "AccessibleTrader.Sdk")
                references.Add(Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(asm.Location));
        }
        references.Add(Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        references.Add(Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(Ohlcv).Assembly.Location));

        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "PrintingIndicatorAsm",
            new[] { Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source) },
            references,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        Assert.True(emit.Success,
            "fixture failed to compile: " + string.Join(" | ",
                emit.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                                .Select(d => d.GetMessage())));
        return ms.ToArray();
    }

}
