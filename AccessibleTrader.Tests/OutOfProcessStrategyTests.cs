using System.Collections.Immutable;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Scripting;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// A script strategy driven through the real worker process.
///
/// <para>
/// Until this shipped, <c>CompileStrategyAsync</c> loaded user IL into the trading host with
/// <c>alc.LoadFromStream</c> — no worker, no OS sandbox, no memory or CPU quota, no kill switch —
/// while indicators had been out-of-process for months. The half of the scripting surface that
/// can open a position was the half still running next to the credentials.
/// </para>
///
/// <para>
/// These tests drive the production proxy against a real spawned worker rather than a fake
/// transport, because the things that break here are the things a fake cannot have: the delta the
/// worker's history is kept in step by, the state that is sent only when it changes, and the fact
/// that a strategy which asks the runtime for four gigabytes takes down a process that is not
/// this one.
/// </para>
/// </summary>
public class OutOfProcessStrategyTests
{
    // ── The fixture strategy ──────────────────────────────────────────────────────
    // Reports each of OnBar's three arguments back through a field of the order it emits, so a
    // host-side assertion can say exactly what the strategy saw on the far side of the pipe.
    private const string ReporterSource = """
        using System;
        using System.Collections.Generic;
        using AccessibleTrader.Sdk.Models;
        using AccessibleTrader.Sdk.Plugins;
        using AccessibleTrader.Sdk.Strategies;
        using AccessibleTrader.Sdk.Trading;

        public sealed class ReporterStrategy : ITradingStrategy
        {
            public string Id => "OOP_REPORTER";
            public string Name => "Reporter";
            public string Description => "reports its arguments back as order fields";
            public StrategyComplexityLevel Complexity => StrategyComplexityLevel.Advanced;

            public IReadOnlyList<StrategyParameter> Parameters => new StrategyParameter[]
            {
                new StrategyParameter("period", "bars", StrategyParameterType.Integer, 14, 2, 200),
                new StrategyParameter("threshold", "level", StrategyParameterType.Double, 1.5),
                new StrategyParameter("enabled", "on", StrategyParameterType.Boolean, true),
                new StrategyParameter("label", "text", StrategyParameterType.String, "hello",
                                      null, null, new[] { "hello", "bye" }),
            };

            private int _period;
            private double _threshold;
            private bool _enabled;
            private string _label = "";
            private int _fills;
            private double _lastFillPrice;
            private int _initialHistoryCount;

            public void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state,
                                   IDictionary<string, object> parameterValues)
            {
                _initialHistoryCount = history.Count;
                if (parameterValues.TryGetValue("period", out var p))    _period = Convert.ToInt32(p);
                if (parameterValues.TryGetValue("threshold", out var t)) _threshold = Convert.ToDouble(t);
                if (parameterValues.TryGetValue("enabled", out var e))   _enabled = Convert.ToBoolean(e);
                if (parameterValues.TryGetValue("label", out var l))     _label = Convert.ToString(l) ?? "";
            }

            public StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
            {
                if (history.Count == 0) return null;
                return new StrategySignal(
                    _enabled ? OrderSide.Buy : OrderSide.Sell,
                    OrderType.Limit,
                    Quantity: history.Count,
                    LimitPrice: history[0].Close,
                    StopLoss: newBar.Close,
                    TakeProfit: _threshold,
                    Rationale: _label + "|" + state.SymbolDisplayName,
                    Confidence: state.ActiveSeries.Count,
                    TpLadder: new double[] { _period, _fills, state.Data.Count, _initialHistoryCount },
                    TpClosePortions: new double[] { 0.5, 0.25 },
                    StopAdjust: StopAdjustOnTp1.TrailByAtr,
                    TrailAtrPeriod: 21,
                    TrailAtrMultiple: 2.75);
            }

            public void OnOrderFilled(OrderUpdate fill) { _fills++; _lastFillPrice = fill.FilledPrice; }
            public void OnStop() { }
            public StrategyMetrics GetMetrics() =>
                new StrategyMetrics(_fills, 3, 0.5, 1.25, _lastFillPrice, 2.5, 9.5, 4.25);
        }
        """;

    // ── The whole surface, one bar ────────────────────────────────────────────────

    [Fact]
    public async Task Every_field_of_an_order_survives_the_trip_back_from_the_worker()
    {
        await using var fixture = await StartAsync(ReporterSource);

        var bars = Bars(3);
        var state = WorkspaceState.Initial with { SymbolDisplayName = "Bitcoin" };
        fixture.Strategy.Initialize(Array.Empty<Ohlcv>(), state, new Dictionary<string, object>
        {
            ["period"] = 34,
            ["threshold"] = 2.25,
            ["enabled"] = false,
            ["label"] = "bye",
        });

        var signal = fixture.Strategy.OnBar(bars[0], new[] { bars[0] }, state);

        Assert.NotNull(signal);
        Assert.Equal(OrderSide.Sell, signal!.Side);            // enabled:false crossed as a bool
        Assert.Equal(OrderType.Limit, signal.OrderType);
        Assert.Equal(1, signal.Quantity);
        Assert.Equal(bars[0].Close, signal.LimitPrice);
        Assert.Equal(bars[0].Close, signal.StopLoss);
        Assert.Equal(2.25, signal.TakeProfit);                  // a double parameter
        Assert.Equal("bye|Bitcoin", signal.Rationale);          // a string parameter + carried state
        Assert.Equal(0, signal.Confidence);
        Assert.Equal(new double[] { 34, 0, 0, 0 }, signal.TpLadder);   // 34 = an int parameter
        Assert.Equal(new double[] { 0.5, 0.25 }, signal.TpClosePortions);
        Assert.Equal(StopAdjustOnTp1.TrailByAtr, signal.StopAdjust);
        Assert.Equal(21, signal.TrailAtrPeriod);
        Assert.Equal(2.75, signal.TrailAtrMultiple);
    }

    [Fact]
    public async Task The_declared_metadata_crosses_including_parameter_defaults_of_every_wire_type()
    {
        await using var fixture = await StartAsync(ReporterSource);

        Assert.Equal("OOP_REPORTER", fixture.Strategy.Id);
        Assert.Equal("Reporter", fixture.Strategy.Name);
        Assert.Equal("reports its arguments back as order fields", fixture.Strategy.Description);
        Assert.Equal(StrategyComplexityLevel.Advanced, fixture.Strategy.Complexity);

        var byName = fixture.Strategy.Parameters.ToDictionary(p => p.Name, StringComparer.Ordinal);
        Assert.Equal(4, byName.Count);

        // StrategyParameter.DefaultValue is `object`, so the wire has to say what it is carrying.
        // An int arrives as long and a float as double — narrowing back is the reader's job, and
        // every consumer already goes through Convert.
        Assert.Equal(StrategyParameterType.Integer, byName["period"].Type);
        Assert.Equal(14L, byName["period"].DefaultValue);
        Assert.Equal(2L, byName["period"].MinValue);
        Assert.Equal(200L, byName["period"].MaxValue);
        Assert.Equal(1.5d, byName["threshold"].DefaultValue);
        Assert.Equal(true, byName["enabled"].DefaultValue);
        Assert.Equal("hello", byName["label"].DefaultValue);
        Assert.Equal(new[] { "hello", "bye" }, byName["label"].AllowedValues);
        Assert.Null(byName["threshold"].MinValue);
    }

    // ── The incremental history ───────────────────────────────────────────────────

    /// <summary>
    /// The reason the protocol is not simply "send OnBar's three arguments": a backtest is one
    /// OnBar per bar with a history that grows by one each time, so re-sending it whole is
    /// quadratic. This drives 400 bars and checks, on every one of them, that the worker's copy
    /// agrees with the host's about both its length and its first bar.
    /// </summary>
    [Fact]
    public async Task The_worker_stays_in_step_with_a_history_that_grows_one_bar_at_a_time()
    {
        await using var fixture = await StartAsync(ReporterSource);

        var bars = Bars(400);
        var state = WorkspaceState.Initial with { Data = new TimeSeriesBuffer<Ohlcv>(bars) };
        fixture.Strategy.Initialize(Array.Empty<Ohlcv>(), state, new Dictionary<string, object>());

        var history = ImmutableList<Ohlcv>.Empty;
        for (int i = 0; i < bars.Length; i++)
        {
            history = history.Add(bars[i]);
            var signal = fixture.Strategy.OnBar(bars[i], history, state);

            Assert.NotNull(signal);
            Assert.Equal(history.Count, signal!.Quantity);      // the worker's history is the right LENGTH
            Assert.Equal(bars[0].Close, signal.LimitPrice);     // …and starts at the right BAR
            Assert.Equal(bars[i].Close, signal.StopLoss);       // …and this bar is this bar
            Assert.Equal(bars.Length, signal.TpLadder![2]);     // state.Data crossed once and stayed
        }
    }

    /// <summary>
    /// The scrollback case. Array lengths cannot tell an append from a prepend — that confusion
    /// once smeared a whole indicator's values onto the wrong bars — so the delta is pinned by
    /// first-bar date as well as by count, and older bars arriving force a full resend rather
    /// than being appended to the end of what the worker already holds.
    /// </summary>
    [Fact]
    public async Task Older_bars_arriving_resync_the_worker_instead_of_being_appended()
    {
        await using var fixture = await StartAsync(ReporterSource);

        var all = Bars(60);
        var recent = all.Skip(20).ToArray();     // what a fresh chart load holds
        var state = WorkspaceState.Initial;
        fixture.Strategy.Initialize(Array.Empty<Ohlcv>(), state, new Dictionary<string, object>());

        var history = ImmutableList<Ohlcv>.Empty;
        foreach (var bar in recent)
        {
            history = history.Add(bar);
            fixture.Strategy.OnBar(bar, history, state);
        }
        var beforeScrollback = fixture.Strategy.OnBar(recent[^1], history, state);
        Assert.Equal(recent[0].Close, beforeScrollback!.LimitPrice);
        Assert.Equal(recent.Length, beforeScrollback.Quantity);

        // The user scrolls back: twenty older bars are prepended. Every bar the worker already
        // holds has moved right by twenty, and the first bar is a different bar.
        var scrolled = ImmutableList.CreateRange(all);
        var after = fixture.Strategy.OnBar(all[^1], scrolled, state);

        Assert.Equal(all[0].Close, after!.LimitPrice);
        Assert.Equal(all.Length, after.Quantity);
        Assert.NotEqual(beforeScrollback.LimitPrice, after.LimitPrice);
    }

    // ── The workspace state ───────────────────────────────────────────────────────

    /// <summary>
    /// State crosses only when the host's reference changes — the backtester passes one
    /// <c>liveState</c> to every bar of a run, and re-sending a 5,000-bar buffer and a full
    /// component stack per bar would cost more than the strategy does. The risk that buys is a
    /// STALE state on the far side, so this changes it mid-run and checks the strategy sees the
    /// change on the very next bar.
    /// </summary>
    [Fact]
    public async Task A_changed_workspace_state_reaches_the_strategy_on_the_next_bar()
    {
        await using var fixture = await StartAsync(ReporterSource);

        var bars = Bars(6);
        var first = WorkspaceState.Initial with { SymbolDisplayName = "Bitcoin" };
        fixture.Strategy.Initialize(Array.Empty<Ohlcv>(), first, new Dictionary<string, object>());

        var history = ImmutableList<Ohlcv>.Empty;
        for (int i = 0; i < 3; i++)
        {
            history = history.Add(bars[i]);
            Assert.Equal("|Bitcoin", fixture.Strategy.OnBar(bars[i], history, first)!.Rationale);
        }

        var second = first with { SymbolDisplayName = "Ethereum" };
        for (int i = 3; i < bars.Length; i++)
        {
            history = history.Add(bars[i]);
            Assert.Equal("|Ethereum", fixture.Strategy.OnBar(bars[i], history, second)!.Rationale);
        }
    }

    /// <summary>
    /// The series stack a strategy's conditions are actually built out of, across the boundary
    /// and back — a component array read by name, at an index, inside the worker.
    /// </summary>
    [Fact]
    public async Task An_indicator_component_is_readable_from_inside_the_worker()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using AccessibleTrader.Sdk.Models;
            using AccessibleTrader.Sdk.Plugins;
            using AccessibleTrader.Sdk.Strategies;
            using AccessibleTrader.Sdk.Trading;

            public sealed class RsiReaderStrategy : ITradingStrategy
            {
                public string Id => "OOP_RSI_READER";
                public string Name => "Rsi reader";
                public string Description => "reads a component out of state.ActiveSeries";
                public StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;
                public IReadOnlyList<StrategyParameter> Parameters => new StrategyParameter[0];

                public void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state,
                                       IDictionary<string, object> parameterValues) { }
                public void OnOrderFilled(OrderUpdate fill) { }
                public void OnStop() { }
                public StrategyMetrics GetMetrics() => new StrategyMetrics(0, 0, 0, 0, 0, 0);

                public StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
                {
                    var rsi = state.ActiveSeries.FirstOrDefault(s => s.IndicatorCode == "RSI");
                    if (rsi == null) return null;
                    var values = rsi.Data.ComponentData["RSI"];
                    return new StrategySignal(OrderSide.Buy, OrderType.Market,
                        Quantity: values.Length,
                        LimitPrice: values[history.Count - 1],
                        StopLoss: null, TakeProfit: null,
                        Rationale: rsi.FriendlyName, Confidence: 1.0);
                }
            }
            """;

        await using var fixture = await StartAsync(source);

        var config = new SeriesConfig { Name = "RSI(14)", FriendlyName = "Relative Strength", IndicatorCode = "RSI" };
        config.Components.Add(new ComponentConfig { Name = "RSI", DisplayType = ComponentDisplayType.Oscillator });
        var data = new SeriesDataBuffer { SeriesId = config.Id };
        data.ComponentData["RSI"] = new[] { 30.5, 44.25, 71.75 };

        var state = WorkspaceState.Initial with
        {
            ActiveSeries = ImmutableList<ChartSeries>.Empty.Add(new ChartSeries(config, data)),
        };

        var bars = Bars(3);
        fixture.Strategy.Initialize(Array.Empty<Ohlcv>(), state, new Dictionary<string, object>());

        var signal = fixture.Strategy.OnBar(bars[1], bars.Take(2).ToList(), state);

        Assert.NotNull(signal);
        Assert.Equal(3, signal!.Quantity);
        Assert.Equal(44.25, signal.LimitPrice);
        Assert.Equal("Relative Strength", signal.Rationale);
    }

    // ── Fills, metrics, teardown ──────────────────────────────────────────────────

    [Fact]
    public async Task A_fill_reaches_the_strategy_and_its_metrics_come_back()
    {
        await using var fixture = await StartAsync(ReporterSource);

        var state = WorkspaceState.Initial;
        fixture.Strategy.Initialize(Array.Empty<Ohlcv>(), state, new Dictionary<string, object>());

        Assert.Equal(0, fixture.Strategy.GetMetrics().TotalSignals);

        fixture.Strategy.OnOrderFilled(new OrderUpdate(
            OrderId: "abc-1", Symbol: "BTC/USD", Side: OrderSide.Buy,
            FilledQuantity: 0.25, FilledPrice: 61234.5, RemainingQuantity: 0,
            Status: OrderStatus.Filled, StopTriggered: false, TakeProfitTriggered: true,
            Timestamp: new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc),
            RealizedPnL: 12.5, Trailing: true, Reason: "target"));

        var metrics = fixture.Strategy.GetMetrics();
        Assert.Equal(1, metrics.TotalSignals);          // the fixture counts fills here
        Assert.Equal(61234.5, metrics.TotalPnL);        // …and echoes the fill price here
        Assert.Equal(3, metrics.WinningTrades);
        Assert.Equal(0.5, metrics.WinRate);
        Assert.Equal(1.25, metrics.MaxDrawdown);
        Assert.Equal(2.5, metrics.SharpeRatio);
        Assert.Equal(9.5, metrics.GrossProfit);
        Assert.Equal(4.25, metrics.GrossLoss);

        // OnStop is the strategy's own teardown and must NOT end the worker — the causality
        // probe calls it between runs, and a worker that died there would end the check after
        // its first run with nothing to say.
        fixture.Strategy.OnStop();
        Assert.True(fixture.Host.IsAlive);
        Assert.Equal(1, fixture.Strategy.GetMetrics().TotalSignals);
    }

    /// <summary>
    /// Initialize starts the strategy over. In-process the causality probe gets that by calling
    /// <c>Activator</c> on the prototype's type; through a proxy there is nothing to construct,
    /// so the worker discards the instance and builds a fresh one on every Initialize frame. If
    /// it did not, the probe's short run would inherit the long run's accumulated state and the
    /// gate would report look-ahead where there is none.
    /// </summary>
    [Fact]
    public async Task Initialize_gives_back_a_strategy_that_has_seen_nothing()
    {
        await using var fixture = await StartAsync(ReporterSource);

        var state = WorkspaceState.Initial;
        fixture.Strategy.Initialize(Array.Empty<Ohlcv>(), state, new Dictionary<string, object>());
        fixture.Strategy.OnOrderFilled(Fill(1));
        fixture.Strategy.OnOrderFilled(Fill(2));
        Assert.Equal(2, fixture.Strategy.GetMetrics().TotalSignals);

        Assert.Same(fixture.Strategy, ((IRestartableStrategy)fixture.Strategy).StartFresh());
        fixture.Strategy.Initialize(Bars(9), state, new Dictionary<string, object>());

        Assert.Equal(0, fixture.Strategy.GetMetrics().TotalSignals);
        // …and the fresh instance was handed the history Initialize was called with.
        var signal = fixture.Strategy.OnBar(Bars(1)[0], Bars(1), state);
        Assert.Equal(9, signal!.TpLadder![3]);

        static OrderUpdate Fill(int i) => new(
            $"f{i}", "BTC/USD", OrderSide.Buy, 1, 100 + i, 0, OrderStatus.Filled,
            false, false, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, false, null);
    }

    // ── It really is another process ──────────────────────────────────────────────

    /// <summary>
    /// The proof that this is not simply a more elaborate in-process call. A strategy that asks
    /// the runtime for four gigabytes inside <c>OnBar</c> gets an OutOfMemoryException — from the
    /// worker's own GC heap hard limit, which is what makes the refusal immediate rather than a
    /// swap storm two seconds later — and the trading host is still standing to read about it.
    /// In-process, that allocation would have been the HOST's.
    /// </summary>
    [Fact]
    public async Task A_strategy_that_asks_for_four_gigabytes_fails_in_the_worker_not_here()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using AccessibleTrader.Sdk.Models;
            using AccessibleTrader.Sdk.Plugins;
            using AccessibleTrader.Sdk.Strategies;
            using AccessibleTrader.Sdk.Trading;

            public sealed class GluttonStrategy : ITradingStrategy
            {
                public string Id => "OOP_GLUTTON";
                public string Name => "Glutton";
                public string Description => "asks for four gigabytes";
                public StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;
                public IReadOnlyList<StrategyParameter> Parameters => new StrategyParameter[0];

                public void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state,
                                       IDictionary<string, object> parameterValues) { }
                public void OnOrderFilled(OrderUpdate fill) { }
                public void OnStop() { }
                public StrategyMetrics GetMetrics() => new StrategyMetrics(0, 0, 0, 0, 0, 0);

                public StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
                {
                    var hog = new double[500_000_000];
                    hog[0] = newBar.Close;
                    return null;
                }
            }
            """;

        await using var fixture = await StartAsync(source);

        var state = WorkspaceState.Initial;
        fixture.Strategy.Initialize(Array.Empty<Ohlcv>(), state, new Dictionary<string, object>());

        var bars = Bars(1);
        var ex = Assert.ThrowsAny<Exception>(() => fixture.Strategy.OnBar(bars[0], bars, state));
        Assert.Contains("OnBar threw", ex.Message, StringComparison.Ordinal);
        Assert.Contains("OutOfMemoryException", ex.Message, StringComparison.Ordinal);

        // The worker refused the allocation and kept running; the host never allocated anything.
        Assert.True(fixture.Host.IsAlive);
    }

    /// <summary>
    /// The worker answers <c>LoadAssembly</c> with whichever kind it found, so pressing Compile
    /// Indicator on a strategy now reaches the indicator path holding strategy metadata. The
    /// message has to name what actually happened — the mechanical answer is
    /// "StartAsync has not completed successfully", which describes nothing the author did.
    /// </summary>
    [Fact]
    public async Task A_strategy_compiled_as_an_indicator_is_told_which_one_it_is()
    {
        RequireWorker();
        var scripting = new RoslynScriptingService(
            workerLauncher: new DefaultProcessLauncher(),
            workerPathResolver: ScriptWorkerPath.Resolve);

        var result = await scripting.CompileIndicatorAsync(ReporterSource);

        Assert.False(result.Success);
        Assert.Null(result.Indicator);
        var errors = string.Join(" | ", result.Errors ?? Array.Empty<string>());
        Assert.Contains("ITradingStrategy, not ICustomIndicator", errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_assembly_holding_neither_kind_of_script_is_refused_by_name()
    {
        const string source = """
            public sealed class NotAScriptAtAll
            {
                public int Answer => 42;
            }
            """;

        var workerPath = RequireWorker();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await OutOfProcessScriptHost.StartAsync(
                new DefaultProcessLauncher(), workerPath, Compile(source), scriptId: "neither"));

        Assert.Contains("ICustomIndicator or ITradingStrategy", ex.Message, StringComparison.Ordinal);
    }

    // ── Through the real service ──────────────────────────────────────────────────

    /// <summary>
    /// The policy assertion. <c>CompileStrategyAsync</c>'s default path must hand back a proxy —
    /// not an instance of the script's own class, which is what it returned for as long as
    /// strategies ran in the trading host.
    /// </summary>
    [Fact]
    public async Task CompileStrategyAsync_returns_a_proxy_for_something_running_elsewhere()
    {
        RequireWorker();
        var scripting = new RoslynScriptingService(
            workerLauncher: new DefaultProcessLauncher(),
            workerPathResolver: ScriptWorkerPath.Resolve);

        var result = await scripting.CompileStrategyAsync("""
            using System;
            using System.Collections.Generic;
            using AccessibleTrader.Sdk.Models;
            using AccessibleTrader.Sdk.Plugins;
            using AccessibleTrader.Sdk.Strategies;
            using AccessibleTrader.Sdk.Trading;

            public sealed class BreakoutStrategy : ITradingStrategy
            {
                public string Id => "OOP_BREAKOUT";
                public string Name => "Breakout";
                public string Description => "buys a two-tenths-percent push";
                public StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;
                public IReadOnlyList<StrategyParameter> Parameters => new StrategyParameter[0];

                public void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state,
                                       IDictionary<string, object> parameterValues) { }
                public void OnOrderFilled(OrderUpdate fill) { }
                public void OnStop() { }
                public StrategyMetrics GetMetrics() => new StrategyMetrics(0, 0, 0, 0, 0, 0);

                public StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
                {
                    if (history.Count < 2) return null;
                    if (newBar.Close <= history[history.Count - 2].Close * 1.002) return null;
                    return new StrategySignal(OrderSide.Buy, OrderType.Market, 1.0, null,
                        newBar.Close * 0.99, newBar.Close * 1.02, "breakout", 0.5);
                }
            }
            """);

        Assert.True(result.Success, "compile failed: " + string.Join(" | ", result.Errors ?? Array.Empty<string>()));
        var strategy = Assert.IsType<OutOfProcessStrategy>(result.Strategy);
        Assert.Equal("OOP_BREAKOUT", strategy.Id);

        // UnloadScript is keyed by the strategy's own Id, same as an indicator, and takes the
        // worker down with it.
        scripting.UnloadScript(strategy.Id);
    }

    // ── Harness ───────────────────────────────────────────────────────────────────

    private sealed class StrategyFixture : IAsyncDisposable
    {
        public required OutOfProcessScriptHost Host { get; init; }
        public required OutOfProcessStrategy Strategy { get; init; }
        public ValueTask DisposeAsync() => Strategy.DisposeAsync();
    }

    private static async Task<StrategyFixture> StartAsync(string source)
    {
        var workerPath = RequireWorker();
        var host = await OutOfProcessScriptHost.StartAsync(
            new DefaultProcessLauncher(), workerPath, Compile(source),
            scriptId: "strategy-test-" + Guid.NewGuid().ToString("N")[..8]);
        Assert.True(host.IsStrategy, "the worker did not report a strategy for this assembly");
        return new StrategyFixture { Host = host, Strategy = new OutOfProcessStrategy(host) };
    }

    private static string RequireWorker()
    {
        var path = ScriptWorkerPath.Resolve();
        Assert.True(File.Exists(path),
            $"ScriptWorker executable not found at '{path}' — build AccessibleTrader.ScriptWorker.");
        return path;
    }

    /// <summary>
    /// Compiled with the production reference set (<c>BuildReferences(includeHostCore: true)</c>)
    /// but WITHOUT the sandbox walker or the causality gate. Both belong to
    /// <c>CompileStrategyAsync</c> and have their own tests; the fixtures here need to report
    /// what they were handed, which is by definition index-anchored and which the gate is
    /// correctly there to refuse.
    /// </summary>
    private static byte[] Compile(string source)
    {
        var compilation = CSharpCompilation.Create(
            "StrategyFixture_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest)) },
            RoslynScriptingService.BuildReferences(includeHostCore: true),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        Assert.True(emit.Success, "fixture failed to compile: " + string.Join(" | ",
            emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));
        return ms.ToArray();
    }

    private static Ohlcv[] Bars(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new Ohlcv(
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i),
                100 + i, 101 + i, 99 + i, 100.5 + i, 1000 + i))
            .ToArray();
}
