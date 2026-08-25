using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Scripting;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// The script worker's memory ceiling has two halves and they are in different projects.
///
/// <para>
/// <see cref="OutOfProcessScriptHost"/> polls <c>WorkingSet64</c> every two seconds against a
/// 256 MB quota. A poll is a backstop, not a limit: <c>new double[500_000_000]</c> is a perfectly
/// legal thing for a script to compile, the runtime hands over four gigabytes, and the supervisor
/// finds out up to two seconds later. The other half is
/// <c>System.GC.HeapHardLimit</c> in the worker's own runtimeconfig, which makes the RUNTIME
/// refuse the allocation at the point it is made.
/// </para>
///
/// <para>
/// The limit has to stay below the quota or the ordering inverts and the runtime never gets to
/// refuse anything. Nothing else connects the two numbers, so these guards do.
/// </para>
/// </summary>
public class ScriptWorkerMemoryLimitTests
{

    [Fact]
    public void The_worker_declares_a_heap_hard_limit_below_the_supervisor_quota()
    {
        var workerPath = ScriptWorkerPath.Resolve();
        // NOT Path.ChangeExtension(workerPath, null): the worker has no extension on Unix, so
        // that reads ".ScriptWorker" as the extension and strips it.
        var configPath = Path.Combine(Path.GetDirectoryName(workerPath)!,
            "AccessibleTrader.ScriptWorker.runtimeconfig.json");
        Assert.True(File.Exists(configPath), $"ScriptWorker runtimeconfig not found at '{configPath}'.");

        using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
        var props = doc.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties");

        Assert.True(props.TryGetProperty("System.GC.HeapHardLimit", out var limitElement),
            "The worker's runtimeconfig has no System.GC.HeapHardLimit, so the only memory ceiling "
          + "on user script code is a 2-second poll.");

        // Written as a JSON number by RuntimeHostConfigurationOption. A string here would be read
        // by nobody, so the shape is part of the claim.
        Assert.Equal(JsonValueKind.Number, limitElement.ValueKind);
        long limit = limitElement.GetInt64();

        Assert.True(limit > 0, "Heap hard limit must be positive.");
        Assert.True(limit < OutOfProcessScriptHost.DefaultMaxWorkingSetBytes,
            $"Heap hard limit ({limit:N0}) must sit below the supervisor's working-set quota "
          + $"({OutOfProcessScriptHost.DefaultMaxWorkingSetBytes:N0}), otherwise the poll fires "
          + "first and the runtime never refuses the allocation.");
    }

    /// <summary>
    /// End-to-end: a script that asks for four gigabytes is refused BY THE RUNTIME, inside a
    /// worker that is still alive afterwards to say so.
    ///
    /// <para>
    /// The assertion deliberately turns on WHICH refusal came back. "The call failed" is true
    /// under the old behaviour too — the poll kills the worker and the host reports a memory-quota
    /// kill — so a test that only checked for failure would have stayed green with the heap limit
    /// removed. What is new is that the error is the allocation's own, which means the four
    /// gigabytes were never handed out.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_script_asking_for_four_gigabytes_is_refused_by_the_runtime_not_by_the_poll()
    {
        var workerPath = ScriptWorkerPath.Resolve();
        Assert.True(File.Exists(workerPath), $"ScriptWorker executable not found at '{workerPath}'.");

        const string src = """
            public sealed class GluttonIndicator : ICustomIndicator
            {
                public string Id => "GLUTTON";
                public string DisplayName => "glutton";
                public string[] ComponentNames => new[] { "x" };
                public ComponentDisplayType[] DisplayTypes => new[] { ComponentDisplayType.Line };
                public Dictionary<string, double> DefaultParameters => new();

                public double[][] Calculate(System.ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
                {
                    var hog = new double[500_000_000];   // 4 GB
                    hog[0] = data.Length;
                    return new[] { new double[data.Length] };
                }
            }
            """;

        var scripting = new RoslynScriptingService(
            workerLauncher: new DefaultProcessLauncher(),
            workerPathResolver: () => workerPath);

        var compile = await scripting.CompileIndicatorAsync(src);
        Assert.True(compile.Success,
            "Compile failed: " + string.Join(" | ", compile.Errors ?? Array.Empty<string>()));
        Assert.NotNull(compile.Indicator);

        try
        {
            var bars = new Ohlcv[] { new(DateTime.UtcNow, 100, 101, 99, 100.5, 1) };
            var ex = Assert.ThrowsAny<Exception>(
                () => compile.Indicator!.Calculate(bars, new Dictionary<string, double>()));

            Assert.Contains("OutOfMemory", ex.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("exceeded quota", ex.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            scripting.UnloadScript(compile.Indicator!.Id);
        }
    }

    /// <summary>
    /// The control. A heap limit set too low would refuse ordinary indicator work too, and every
    /// assertion above would still pass — so an ordinary script has to keep working through the
    /// same worker.
    /// </summary>
    [Fact]
    public async Task An_ordinary_indicator_still_runs_under_the_heap_limit()
    {
        var workerPath = ScriptWorkerPath.Resolve();
        Assert.True(File.Exists(workerPath), $"ScriptWorker executable not found at '{workerPath}'.");

        const string src = """
            public sealed class SumIndicator : ICustomIndicator
            {
                public string Id => "HEAP_CONTROL_SUM";
                public string DisplayName => "sum";
                public string[] ComponentNames => new[] { "Sum" };
                public ComponentDisplayType[] DisplayTypes => new[] { ComponentDisplayType.Line };
                public Dictionary<string, double> DefaultParameters => new();

                public double[][] Calculate(System.ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
                {
                    // A few megabytes of honest working memory — comfortably legal, and enough
                    // that a limit set absurdly low would fail here too.
                    var scratch = new double[1_000_000];
                    var outp = new double[data.Length];
                    double running = 0;
                    for (int i = 0; i < data.Length; i++)
                    {
                        running += data[i].Close;
                        scratch[i % scratch.Length] = running;
                        outp[i] = running;
                    }
                    return new[] { outp };
                }
            }
            """;

        var scripting = new RoslynScriptingService(
            workerLauncher: new DefaultProcessLauncher(),
            workerPathResolver: () => workerPath);

        var compile = await scripting.CompileIndicatorAsync(src);
        Assert.True(compile.Success,
            "Compile failed: " + string.Join(" | ", compile.Errors ?? Array.Empty<string>()));

        try
        {
            var bars = new Ohlcv[]
            {
                new(DateTime.UtcNow.AddMinutes(-2), 100, 101, 99, 100, 1),
                new(DateTime.UtcNow.AddMinutes(-1), 100, 102, 100, 101, 2),
                new(DateTime.UtcNow,                101, 103, 100, 102, 3),
            };
            var result = compile.Indicator!.Calculate(bars, new Dictionary<string, double>());
            Assert.Single(result);
            Assert.Equal(new[] { 100.0, 201.0, 303.0 }, result[0]);
        }
        finally
        {
            scripting.UnloadScript(compile.Indicator!.Id);
        }
    }
}
