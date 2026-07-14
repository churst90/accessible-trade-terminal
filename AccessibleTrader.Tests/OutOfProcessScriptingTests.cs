using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Scripting;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Roundtrip integration test for the phase-4 Track C out-of-process
/// script sandbox. Compiles a trivial indicator with Roslyn, spawns the
/// worker, sends OHLCV bars, verifies computed values match expected.
///
/// Only runs when the ScriptWorker executable is actually on disk next
/// to the test assembly's build output. On a fresh clone before
/// <c>dotnet build</c> the worker won't exist; the test fact skips
/// itself with a clear <see cref="SkipException"/>-style check rather
/// than a hard fail.
/// </summary>
public class OutOfProcessScriptingTests
{
    // A minimal ICustomIndicator that emits Close (the final OHLCV close
    // price) as its single component. Deterministic, no warmup, easy to
    // assert the roundtripped values equal the input closes.
    private const string TrivialIndicatorSource = """
        public sealed class EchoCloseIndicator : ICustomIndicator
        {
            public string Id => "ECHO_CLOSE";
            public string DisplayName => "Echo Close";
            public string[] ComponentNames => new[] { "Close" };
            public ComponentDisplayType[] DisplayTypes => new[] { ComponentDisplayType.Line };
            public Dictionary<string, double> DefaultParameters => new();

            public double[][] Calculate(System.ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
            {
                var closes = new double[data.Length];
                for (int i = 0; i < data.Length; i++) closes[i] = data[i].Close;
                return new[] { closes };
            }
        }
        """;

    private static string ResolveWorkerPath()
    {
        // Test assembly lives at …/AccessibleTrader.Tests/bin/<Config>/net10.0
        // Worker lives at …/AccessibleTrader.ScriptWorker/bin/<Config>/net10.0
        // Walk up three levels to the solution root, then back down.
        var baseDir   = AppContext.BaseDirectory;
        var solutionRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
        var config    = Path.GetFileName(Path.GetDirectoryName(baseDir.TrimEnd(Path.DirectorySeparatorChar))) ?? "Debug";
        var exeName   = OperatingSystem.IsWindows()
            ? "AccessibleTrader.ScriptWorker.exe"
            : "AccessibleTrader.ScriptWorker";
        return Path.Combine(solutionRoot, "AccessibleTrader.ScriptWorker", "bin", config, "net10.0", exeName);
    }

    [Fact]
    public async Task Roundtrip_TrivialIndicator_EchoesClosePrices()
    {
        var workerPath = ResolveWorkerPath();
        if (!File.Exists(workerPath))
        {
            // Fresh clone hasn't built the worker yet — skip rather than
            // hard-fail the suite. Either `dotnet build
            // AccessibleTrader.ScriptWorker/...` first or let the test
            // run's dependency graph build it (which happens via the
            // ProjectReference we added to this csproj).
            Assert.Fail($"ScriptWorker executable not found at '{workerPath}'. " +
                        "Build the ScriptWorker project first. If this is a fresh clone, " +
                        "ensure the ProjectReference in AccessibleTrader.Tests.csproj is wired.");
            return;
        }

        var scripting = new RoslynScriptingService(
            workerLauncher: new DefaultProcessLauncher(),
            workerPathResolver: () => workerPath);

        CompileResult compile;
        try
        {
            compile = await scripting.CompileIndicatorAsync(TrivialIndicatorSource);
        }
        catch (Exception ex)
        {
            Assert.Fail("CompileIndicatorAsync threw: " + ex);
            return;
        }

        Assert.True(compile.Success,
            "Compile failed: " + string.Join(" | ", compile.Errors ?? Array.Empty<string>()));
        Assert.NotNull(compile.Indicator);
        Assert.Equal("ECHO_CLOSE", compile.Indicator!.Id);
        Assert.Equal(new[] { "Close" }, compile.Indicator.ComponentNames);

        // Build a few bars and call Calculate through the proxy.
        var bars = new Ohlcv[]
        {
            new(DateTime.UtcNow.AddMinutes(-4), 100, 101, 99, 100.5, 1),
            new(DateTime.UtcNow.AddMinutes(-3), 100.5, 102, 100, 101.0, 2),
            new(DateTime.UtcNow.AddMinutes(-2), 101.0, 103, 100.5, 102.0, 3),
            new(DateTime.UtcNow.AddMinutes(-1), 102.0, 104, 101.5, 103.5, 4),
            new(DateTime.UtcNow,                103.5, 105, 102.5, 104.0, 5),
        };

        try
        {
            double[][] result = compile.Indicator.Calculate(bars, new Dictionary<string, double>());
            Assert.Single(result);
            Assert.Equal(bars.Length, result[0].Length);
            for (int i = 0; i < bars.Length; i++)
                Assert.Equal(bars[i].Close, result[0][i]);
        }
        finally
        {
            // Clean up the worker process.
            scripting.UnloadScript(compile.Indicator.Id);
        }
    }

#if DEBUG
    // Release builds ignore ACCESSIBLETRADER_SCRIPT_IN_PROCESS entirely — that is
    // the documented security policy, so the opt-in path only exists (and is only
    // testable) in Debug. Compiling the test out in Release keeps CI honest instead
    // of asserting behavior the build intentionally does not have. (This mismatch
    // kept the Release CI suite red from 1.4.0 through 1.6.0.)
    [Fact]
    public async Task InProcessOptIn_FallsBackToLegacyPath_WhenEnvVarSet()
    {
        // The env var is read lazily by RoslynScriptingService so we can
        // set it for the scope of this test and unset after.
        const string key = "ACCESSIBLETRADER_SCRIPT_IN_PROCESS";
        var prev = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "1");

            var scripting = new RoslynScriptingService(
                workerLauncher: new DefaultProcessLauncher(),
                // Point at a non-existent path; if in-process opt-in works
                // this path never resolves because we don't spawn the worker.
                workerPathResolver: () => "/__should_never_be_used__/AccessibleTrader.ScriptWorker.exe");

            var result = await scripting.CompileIndicatorAsync(TrivialIndicatorSource);

            Assert.True(result.Success,
                "In-process compile failed: " + string.Join(" | ", result.Errors ?? Array.Empty<string>()));
            Assert.NotNull(result.Indicator);
            Assert.Equal("ECHO_CLOSE", result.Indicator!.Id);
            scripting.UnloadScript(result.Indicator.Id);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, prev);
        }
    }
#endif
}
