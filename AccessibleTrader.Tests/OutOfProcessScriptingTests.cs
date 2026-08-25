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


    [Fact]
    public async Task Roundtrip_TrivialIndicator_EchoesClosePrices()
    {
        var workerPath = ScriptWorkerPath.Resolve();
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

    // ACCESSIBLETRADER_SCRIPT_IN_PROCESS is honoured only in DEBUG builds;
    // Release ignores the env var entirely — that is the documented security
    // policy, and this test asserts BOTH sides of it from one method. The
    // previous shape (#if DEBUG around the whole Fact) compiled the test out of
    // Release, which meant Release never verified its half of the policy AND
    // the Debug and Release suites differed by one test — a difference the
    // doc-drift count guard cannot represent (README says one number; the CI
    // guard lists Release, a developer's machine lists Debug).
    [Fact]
    public async Task InProcessOptIn_IsHonouredInDebug_AndIgnoredInRelease()
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
                // A path that cannot exist. Debug never resolves it (the opt-in
                // skips the worker); Release MUST resolve it — and fail — because
                // honouring the opt-in there would run untrusted script code
                // in-process with no OS sandbox.
                workerPathResolver: () => "/__should_never_be_used__/AccessibleTrader.ScriptWorker.exe");

#if DEBUG
            var result = await scripting.CompileIndicatorAsync(TrivialIndicatorSource);

            Assert.True(result.Success,
                "In-process compile failed: " + string.Join(" | ", result.Errors ?? Array.Empty<string>()));
            Assert.NotNull(result.Indicator);
            Assert.Equal("ECHO_CLOSE", result.Indicator!.Id);
            scripting.UnloadScript(result.Indicator.Id);
#else
            // The env var must be ignored: the service goes to spawn the real
            // worker, whose path here cannot exist, so the compile must NOT
            // succeed. A throw is equally acceptable — only in-process success
            // is the policy violation.
            try
            {
                var result = await scripting.CompileIndicatorAsync(TrivialIndicatorSource);
                Assert.False(result.Success,
                    "Release honoured ACCESSIBLETRADER_SCRIPT_IN_PROCESS: the compile succeeded "
                    + "without the worker, i.e. untrusted script code ran in-process.");
            }
            catch (Exception)
            {
                // The spawn attempt on the bogus path threw — the worker path
                // was consulted, which is exactly what the policy requires.
            }
#endif
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, prev);
        }
    }
}
