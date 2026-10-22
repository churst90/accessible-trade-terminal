using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Core.Services.Workspace;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Indicators on a Heikin-Ashi chart read Heikin-Ashi candles</b> (<see cref="IndicatorCandles"/>).
/// Cody, 2026-10-22: <i>"when hiken ashi is on, have indicators use those candles … whatever tv
/// does, that is what I want."</i> TradingView hands an indicator on a Heikin-Ashi chart the
/// Heikin-Ashi open, high, low and close, with the volume unchanged.
///
/// <para>Before this, every indicator computed on the real bars while the chart drew Heikin-Ashi
/// candles. These tests cover each place the flag has to travel: the stamp (foreground, headless
/// tab, background tab), the swap in the indicator service and the engine's custom-indicator
/// path, the providers that build Heikin-Ashi themselves (Cipher B, DBSI), the full recalculation
/// on a toggle (in <c>StaleIndicatorOnAssetChangeTests</c>), and the renderer's candles. The
/// other half is what does NOT follow it: a strategy is tested on real prices, so the live
/// engine and a background tab both recompute its indicators on the real bars.</para>
/// </summary>
public sealed class HeikinAshiIndicatorCandlesTests
{
    private static readonly DateTime T0 = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Seeded noisy bars whose open is never the close, so a Heikin-Ashi candle always
    /// differs from the bar it came from.</summary>
    private static List<Ohlcv> Bars(int n = 300, int seed = 7)
    {
        var rng = new Random(seed);
        var bars = new List<Ohlcv>(n);
        double px = 100;
        for (int i = 0; i < n; i++)
        {
            double o = px;
            double c = o * (1 + (rng.NextDouble() - 0.48) * 0.06);
            double h = Math.Max(o, c) * (1 + rng.NextDouble() * 0.02);
            double l = Math.Min(o, c) * (1 - rng.NextDouble() * 0.02);
            bars.Add(new Ohlcv(T0.AddHours(i), o, h, l, c, 1000 + rng.Next(0, 5000)));
            px = c;
        }
        return bars;
    }

    private static List<Ohlcv> Ha(List<Ohlcv> raw) => ChartMath.CalculateHeikinAshi(raw);

    private static Dictionary<string, object> Flagged(bool heikinAshi, params (string Key, object Value)[] extra)
    {
        var p = new Dictionary<string, object>();
        foreach (var (k, v) in extra) p[k] = v;
        IndicatorCandles.Stamp(p, heikinAshi);
        return p;
    }

    // ── A provider that writes back the bars it was handed ────────────────────

    private const string SpyCode = "HA_SPY";

    /// <summary>Its components are the candles it received: whatever it computes on is visible.</summary>
    private class SpyProvider : IIndicatorProvider
    {
        public virtual string Name => "Spy";
        public List<IndicatorMetadata> GetIndicators() => new() { new IndicatorMetadata { Name = "Spy", Code = SpyCode } };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            var open = buffer.GetComponentSpan("open");
            var close = buffer.GetComponentSpan("close");
            var volume = buffer.GetComponentSpan("volume");
            for (int i = 0; i < data.Length; i++)
            {
                open[i] = data[i].Open;
                close[i] = data[i].Close;
                volume[i] = data[i].Volume;
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 0;

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> calculatedResults,
            int index, Dictionary<string, object> parameters) => "";
    }

    /// <summary>The same spy, declaring it builds Heikin-Ashi itself: it must be handed the real bars.</summary>
    private sealed class MarkerSpyProvider : SpyProvider, IComputesHeikinAshiItself
    {
        public override string Name => "MarkerSpy";
    }

    private static Dictionary<string, double[]> Run(IIndicatorService service, string code, IReadOnlyList<Ohlcv> bars,
        Dictionary<string, object> parameters)
    {
        var results = new Dictionary<string, double[]>();
        service.CalculateIndicator(code, bars.ToArray(), parameters, new IndicatorResultBuffer(results, bars.Count));
        return results;
    }

    private static IndicatorService Service(params IIndicatorProvider[] providers)
        => new(providers, NullLogger<IndicatorService>.Instance);

    // ── The indicator service ─────────────────────────────────────────────────

    [Fact]
    public void A_flagged_calculation_reads_Heikin_Ashi_candles_with_the_volume_unchanged()
    {
        var raw = Bars();
        var ha = Ha(raw);
        var r = Run(Service(new SpyProvider()), SpyCode, raw, Flagged(true));

        Assert.Equal(ha.Select(b => b.Close), r["close"]);
        Assert.Equal(ha.Select(b => b.Open), r["open"]);
        Assert.Equal(raw.Select(b => b.Volume), r["volume"]);
        Assert.NotEqual(raw.Select(b => b.Close), r["close"]);   // the fixture can tell them apart
    }

    [Fact]
    public void An_unflagged_calculation_reads_the_real_bars()
    {
        var raw = Bars();
        var r = Run(Service(new SpyProvider()), SpyCode, raw, Flagged(false));
        Assert.Equal(raw.Select(b => b.Close), r["close"]);
        Assert.Equal(raw.Select(b => b.Open), r["open"]);
    }

    [Fact]
    public void The_last_bar_update_reads_Heikin_Ashi_candles_too()
    {
        var raw = Bars();
        var ha = Ha(raw);
        var results = new Dictionary<string, double[]>();
        Service(new SpyProvider()).UpdateLast(SpyCode, raw.ToArray(), Flagged(true),
            new IndicatorResultBuffer(results, raw.Count));
        Assert.Equal(ha[^1].Close, results["close"][^1]);
    }

    [Fact]
    public void A_provider_that_builds_Heikin_Ashi_itself_is_handed_the_real_bars()
    {
        var raw = Bars();
        var r = Run(Service(new MarkerSpyProvider()), SpyCode, raw, Flagged(true));
        Assert.Equal(raw.Select(b => b.Close), r["close"]);
    }

    [Fact]
    public void Stamping_off_removes_a_flag_left_on_a_reused_parameter_map()
    {
        var p = Flagged(true);
        Assert.True(IndicatorCandles.IsHeikinAshi(p));
        IndicatorCandles.Stamp(p, false);
        Assert.False(p.ContainsKey(IndicatorCandles.HeikinAshiKey));
    }

    // ── A custom compiled indicator ───────────────────────────────────────────

    private sealed class CustomCloses : ICustomIndicator
    {
        public string Id => "HaSpyCustom";
        public string DisplayName => "Ha Spy Custom";
        public string[] ComponentNames => new[] { "Close" };
        public ComponentDisplayType[] DisplayTypes => new[] { ComponentDisplayType.Line };
        public Dictionary<string, double> DefaultParameters => new();
        public ComponentCausality[] Causality => new[] { ComponentCausality.Causal };
        public double[][] Calculate(ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
        {
            var c = new double[data.Length];
            for (int i = 0; i < data.Length; i++) c[i] = data[i].Close;
            return new[] { c };
        }
    }

    [Fact]
    public async Task A_custom_indicator_on_a_Heikin_Ashi_chart_reads_Heikin_Ashi_candles()
    {
        var raw = Bars();
        var ha = Ha(raw);
        var registry = new CustomIndicatorRegistry();
        registry.Register(new CustomCloses());
        var engine = new IndicatorEngine(Service(), registry, Array.Empty<IIndicatorProvider>());

        var full = await engine.CalculateAsync("HaSpyCustom", raw, Flagged(true), CancellationToken.None);
        Assert.Equal(ha.Select(b => b.Close), Assert.Single(full.Values));

        var last = await engine.CalculateIncrementalAsync("HaSpyCustom", raw, Flagged(true),
            new Dictionary<string, double[]>(), CancellationToken.None);
        Assert.Equal(ha[^1].Close, Assert.Single(last.Values));

        var plain = await engine.CalculateAsync("HaSpyCustom", raw, Flagged(false), CancellationToken.None);
        Assert.Equal(raw.Select(b => b.Close), Assert.Single(plain.Values));
    }

    // ── Cipher B: what Market Cipher B reads on a Heikin-Ashi chart ───────────

    private static Dictionary<string, double[]> CipherB(IReadOnlyList<Ohlcv> bars, bool flagged)
    {
        var results = new Dictionary<string, double[]>();
        new CipherBProvider().Calculate("CIPHER_B", bars.ToArray(), Flagged(flagged), new IndicatorResultBuffer(results, bars.Count));
        return results;
    }

    /// <summary>
    /// Measured against the Market Cipher B screenshots on a Heikin-Ashi BTCUSD daily chart
    /// (2026-10-22): the waves read Heikin-Ashi; the money flow builds Heikin-Ashi from the
    /// chart's candles, so on a Heikin-Ashi chart it is Heikin-Ashi of Heikin-Ashi (RMSE 4.6/4.8
    /// against 6.2/6.5 for Heikin-Ashi of the real bars); the "RSI" and "Stochastic" lines read
    /// the real bars (2.1/1.2 against 3.5/1.9 on Heikin-Ashi).
    /// </summary>
    [Fact]
    public void Cipher_B_on_a_Heikin_Ashi_chart_reads_what_Market_Cipher_B_reads()
    {
        var raw = Bars();
        var ha = Ha(raw);
        var onChart = CipherB(raw, flagged: true);
        var wavesOnHa = CipherB(ha, flagged: false);   // its own money flow then builds HA of these
        var onRaw = CipherB(raw, flagged: false);

        Assert.Equal(wavesOnHa[CipherBProvider.CompWT1], onChart[CipherBProvider.CompWT1]);
        Assert.Equal(wavesOnHa[CipherBProvider.CompWT2], onChart[CipherBProvider.CompWT2]);
        Assert.Equal(wavesOnHa[CipherBProvider.CompMoneyFlowWave], onChart[CipherBProvider.CompMoneyFlowWave]);
        Assert.Equal(onRaw[CipherBProvider.CompMcbRsi], onChart[CipherBProvider.CompMcbRsi]);
        Assert.Equal(onRaw[CipherBProvider.CompMcbStoch], onChart[CipherBProvider.CompMcbStoch]);

        // Each comparison can fail: the candle types give different values for every one.
        Assert.NotEqual(onRaw[CipherBProvider.CompWT1], onChart[CipherBProvider.CompWT1]);
        Assert.NotEqual(onRaw[CipherBProvider.CompMoneyFlowWave], onChart[CipherBProvider.CompMoneyFlowWave]);
        Assert.NotEqual(wavesOnHa[CipherBProvider.CompMcbRsi], onChart[CipherBProvider.CompMcbRsi]);
        Assert.NotEqual(wavesOnHa[CipherBProvider.CompMcbStoch], onChart[CipherBProvider.CompMcbStoch]);
    }

    // ── DBSI: its own switch, never applied twice ─────────────────────────────

    [Fact]
    public void DBSI_on_a_Heikin_Ashi_chart_computes_on_Heikin_Ashi_once_whatever_its_own_switch_says()
    {
        var raw = Bars();
        var service = Service(new DbsiStyleProvider());
        var p = DbsiStyleProvider.ParamHeikinAshi;

        var ownSwitchOnly = Run(service, DbsiStyleProvider.Code, raw, Flagged(false, (p, 1.0)));
        var chartOnly = Run(service, DbsiStyleProvider.Code, raw, Flagged(true, (p, 0.0)));
        var both = Run(service, DbsiStyleProvider.Code, raw, Flagged(true, (p, 1.0)));
        var neither = Run(service, DbsiStyleProvider.Code, raw, Flagged(false, (p, 0.0)));

        foreach (var key in ownSwitchOnly.Keys)
        {
            Assert.Equal(ownSwitchOnly[key], chartOnly[key]);
            Assert.Equal(ownSwitchOnly[key], both[key]);
        }
        Assert.NotEqual(ownSwitchOnly[DbsiStyleProvider.CompTrendSource], neither[DbsiStyleProvider.CompTrendSource]);
    }

    // ── The foreground chart stamps the flag on every path ────────────────────

    private sealed class RecordingEngine : IIndicatorEngine
    {
        public readonly List<Dictionary<string, object>> Parameters = new();

        public Task<Dictionary<string, double[]>> CalculateAsync(string code, IReadOnlyList<Ohlcv> data,
            Dictionary<string, object> parameters, CancellationToken ct)
        {
            Parameters.Add(new(parameters));
            return Task.FromResult(new Dictionary<string, double[]> { ["v"] = new double[data.Count] });
        }

        public Task<Dictionary<string, double>> CalculateIncrementalAsync(string code, IReadOnlyList<Ohlcv> data,
            Dictionary<string, object> parameters, Dictionary<string, double[]> previousResults, CancellationToken ct)
        {
            Parameters.Add(new(parameters));
            return Task.FromResult(new Dictionary<string, double> { ["v"] = 1 });
        }

        public IIndicatorProvider? GetProvider(string indicatorCode) => null;

        public Task<(Dictionary<string, double[]> Results, IReadOnlyList<ZoneBandConfig> ZoneBands)>
            CalculateWithBandsAsync(string code, IReadOnlyList<Ohlcv> data, Dictionary<string, object> parameters, CancellationToken ct)
        {
            Parameters.Add(new(parameters));
            return Task.FromResult<(Dictionary<string, double[]>, IReadOnlyList<ZoneBandConfig>)>(
                (new Dictionary<string, double[]> { ["v"] = new double[data.Count] }, Array.Empty<ZoneBandConfig>()));
        }
    }

    private sealed class NoDrawings : IDrawingService
    {
        public Dictionary<string, double[]> CalculateDrawingData(DrawingData drawing, IReadOnlyList<Ohlcv> chartData) => new();
    }

    private sealed class NoProfiles : IProfileService
    {
        public List<ProfileBin> CalculateVolumeProfile(IReadOnlyList<Ohlcv> data, int binCount = 50) => new();
        public List<ProfileBin> CalculateMarketProfile(IReadOnlyList<Ohlcv> data, int binCount = 50) => new();
    }

    private sealed class NoHeatmap : IHeatmapService
    {
        public List<List<ProfileBin>> GenerateHeatmap(IReadOnlyList<Ohlcv> data,
            List<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> historicalBooks, double sensitivity) => new();
        public List<ProfileBin> GenerateBarHeatmap(Ohlcv bar, List<OrderBookEntry> bids, List<OrderBookEntry> asks, double sensitivity) => new();
    }

    private static ChartSeries IndicatorSeries(string code, DateTime? firstBarDate = null, int filled = 0)
    {
        var cfg = new SeriesConfig { Id = "ind", Name = "ind", IndicatorCode = code, Pane = "Main" };
        cfg.Components.Add(new ComponentConfig { Name = "v", DisplayType = ComponentDisplayType.Line });
        var buf = new SeriesDataBuffer { SeriesId = "ind", FirstBarDate = firstBarDate };
        if (filled > 0) buf.ComponentData["v"] = new double[filled];
        return new ChartSeries(cfg, buf);
    }

    public enum Path { Full, LastBar, Prepend }

    [Theory]
    [InlineData(Path.Full)]
    [InlineData(Path.LastBar)]
    [InlineData(Path.Prepend)]
    public async Task Every_recalculation_path_stamps_the_charts_candle_type(Path path)
    {
        foreach (bool heikinAshi in new[] { true, false })
        {
            var store = new MockWorkspaceStore();
            store.EmitState(WorkspaceState.Initial with { IsHeikinAshi = heikinAshi });
            var engine = new RecordingEngine();
            var orch = new IndicatorOrchestrator(engine, new IndicatorStateMapper(), new NoDrawings(), new NoProfiles(),
                new NoHeatmap(), store, new MockNotificationHub(), NullLogger<IndicatorOrchestrator>.Instance);
            var bars = Bars(30);

            switch (path)
            {
                case Path.Full:
                    await orch.RecalculateAllAsync(bars, new[] { IndicatorSeries("X") }, CancellationToken.None);
                    break;
                case Path.LastBar:
                    await orch.RecalculateLastAsync(bars, new[] { IndicatorSeries("X", bars[0].Date, filled: 30) }, CancellationToken.None);
                    break;
                case Path.Prepend:   // the buffer starts at a later bar: recomputed in full
                    await orch.RecalculateLastAsync(bars, new[] { IndicatorSeries("X", bars[5].Date, filled: 25) }, CancellationToken.None);
                    break;
            }

            var p = Assert.Single(engine.Parameters);
            Assert.Equal(heikinAshi, IndicatorCandles.IsHeikinAshi(p));
        }
    }

    // ── Strategies stay on the real bars ──────────────────────────────────────

    /// <summary>A chart showing Heikin-Ashi, its spy series holding what the chart computed:
    /// the Heikin-Ashi closes.</summary>
    private static WorkspaceState ChartState(List<Ohlcv> raw, bool heikinAshi)
    {
        var series = IndicatorSeries(SpyCode);
        series.Data.ComponentData["close"] = (heikinAshi ? Ha(raw) : raw).Select(b => b.Close).ToArray();
        return WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Spot", "Bitstamp", "BTC/USD", "1h"),
            IsHeikinAshi = heikinAshi,
            ActiveSeries = ImmutableList.Create(series),
            Data = new TimeSeriesBuffer<Ohlcv>(raw),
            InitStatus = InitializationStatus.Ready,
        };
    }

    /// <summary>A spy strategy; <paramref name="readsHeikinAshi"/> is a strategy whose spec reads
    /// its signals on Heikin-Ashi candles (<c>StrategySpec.SignalsOnHeikinAshi</c>).</summary>
    private static ITradingStrategy SpyStrategy(bool readsHeikinAshi, List<WorkspaceState> seen)
    {
        var strategy = readsHeikinAshi
            ? Substitute.For<ITradingStrategy, IReadsHeikinAshiSignals>()
            : Substitute.For<ITradingStrategy>();
        if (readsHeikinAshi) ((IReadsHeikinAshiSignals)strategy).SignalsOnHeikinAshi.Returns(true);
        strategy.Name.Returns("Spy");
        strategy.OnBar(Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyList<Ohlcv>>(), Arg.Do<WorkspaceState>(seen.Add))
            .Returns((StrategySignal?)null);
        return strategy;
    }

    private static (StrategyEngine Engine, List<WorkspaceState> Seen) StrategyEngineWithSpy(bool readsHeikinAshi = false)
    {
        var seen = new List<WorkspaceState>();
        var strategy = SpyStrategy(readsHeikinAshi, seen);
        var store = Substitute.For<IWorkspaceStore>();
        store.State.Returns(WorkspaceState.Initial);
        var engine = new StrategyEngine(Substitute.For<IEventBus>(), Substitute.For<IOrderExecutionService>(),
            Substitute.For<IAppLogger>(), NullLogger<StrategyEngine>.Instance, Substitute.For<IDataManager>(), store,
            Substitute.For<IStrategyIndicatorCache>(), indicators: Service(new SpyProvider()));
        engine.AddStrategy(strategy);
        return (engine, seen);
    }

    private static void EvaluateBar(StrategyEngine engine, List<Ohlcv> raw, WorkspaceState state)
        => typeof(StrategyEngine)
            .GetMethod("EvaluateBar", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(engine, new object[] { raw[^1], raw, state });

    [Fact]
    public void A_strategy_on_a_Heikin_Ashi_chart_reads_its_indicators_on_the_real_bars()
    {
        var raw = Bars(120);
        var (engine, seen) = StrategyEngineWithSpy();

        EvaluateBar(engine, raw, ChartState(raw, heikinAshi: true));

        var state = Assert.Single(seen);
        Assert.False(state.IsHeikinAshi);
        Assert.Equal(raw.Select(b => b.Close), state.ActiveSeries.Single().Data.ComponentData["close"]);
        Assert.Null(state.HeikinAshiSeries);
    }

    /// <summary>
    /// A strategy that reads its signals on Heikin-Ashi (Cody, 2026-10-22: "is using indicator
    /// values based on ha something that would be useful in a strategy? If so, let's do it") gets
    /// the Heikin-Ashi series beside the real ones, on either chart: the real ones place its stop
    /// and targets, the Heikin-Ashi ones decide its conditions.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_strategy_reading_Heikin_Ashi_signals_gets_the_Heikin_Ashi_series_beside_the_real_ones(bool chartIsHeikinAshi)
    {
        var raw = Bars(120);
        var (engine, seen) = StrategyEngineWithSpy(readsHeikinAshi: true);

        EvaluateBar(engine, raw, ChartState(raw, chartIsHeikinAshi));

        var state = Assert.Single(seen);
        Assert.False(state.IsHeikinAshi);
        Assert.Equal(raw.Select(b => b.Close), state.ActiveSeries.Single().Data.ComponentData["close"]);
        Assert.NotNull(state.HeikinAshiSeries);
        Assert.Equal(Ha(raw).Select(b => b.Close), state.HeikinAshiSeries!.Single().Data.ComponentData["close"]);
    }

    [Fact]
    public void A_strategy_on_a_candle_chart_reads_the_chart_as_it_is()
    {
        var raw = Bars(120);
        var (engine, seen) = StrategyEngineWithSpy();
        var chart = ChartState(raw, heikinAshi: false);

        EvaluateBar(engine, raw, chart);

        Assert.Same(chart, Assert.Single(seen));
    }

    // ── A background tab: alerts read it as drawn, strategies on the real bars ──

    [Fact]
    public async Task A_background_Heikin_Ashi_tab_alerts_on_Heikin_Ashi_and_trades_strategies_on_the_real_bars()
    {
        const string symbol = "KAS/USDT";
        var raw = Bars(120);
        var feeds = Substitute.For<IMarketFeeds>();
        feeds.GetBarsAsync(Arg.Any<ChartIdentity>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Ohlcv>)raw);

        var alerts = Substitute.For<IAlertOrchestrator>();
        alerts.GetAlerts().Returns(new List<AlertDefinition>
        {
            new() { Id = "a", Name = "a", Target = AlertTarget.Price, Condition = AlertCondition.CrossesAbove,
                    Threshold = 1e9, Delivery = AlertDelivery.Both, Symbol = symbol },
        });
        var alertStates = new List<WorkspaceState>();
        var evaluator = Substitute.For<IAlertEvaluator>();
        evaluator.EvaluateAlerts(Arg.Any<IReadOnlyList<AlertDefinition>>(), Arg.Do<WorkspaceState>(alertStates.Add),
                Arg.Any<Ohlcv>(), Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyDictionary<string, double>>())
            .Returns(Enumerable.Empty<AlertFired>());

        var strategyStates = new List<WorkspaceState>();
        var strategy = Substitute.For<ITradingStrategy>();
        strategy.Name.Returns("Spy");
        strategy.OnBar(Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyList<Ohlcv>>(), Arg.Do<WorkspaceState>(strategyStates.Add))
            .Returns((StrategySignal?)null);
        var engine = Substitute.For<IStrategyEngine>();
        engine.ActiveStrategies.Returns(new List<ActiveStrategy>
        {
            new("s", strategy, new Dictionary<string, object>(), StrategyExecutionMode.Suggestion, false, symbol),
        });

        var monitor = new BackgroundWorkspaceMonitor(
            new ChartIdentity("Spot", "Bitstamp", symbol, "1d"), symbol,
            ImmutableList.Create(IndicatorSeries(SpyCode)),
            feeds, Service(new SpyProvider()), alerts, evaluator, engine, new SpyEventBus(),
            NullLogger.Instance, TimeSpan.FromHours(1), heikinAshi: true);

        await monitor.EvaluateOnceAsync(CancellationToken.None);
        await monitor.EvaluateOnceAsync(CancellationToken.None);

        var alertState = Assert.Single(alertStates);
        Assert.True(alertState.IsHeikinAshi);
        Assert.Equal(Ha(raw).Select(b => b.Close), alertState.ActiveSeries.Single().Data.ComponentData["close"]);

        Assert.NotEmpty(strategyStates);
        Assert.All(strategyStates, s =>
        {
            Assert.False(s.IsHeikinAshi);
            Assert.Equal(raw.Select(b => b.Close), s.ActiveSeries.Single().Data.ComponentData["close"]);
        });
    }

    /// <summary>A background candle tab computes the Heikin-Ashi series only for a strategy
    /// that reads its signals there, and hands them beside the real ones.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_background_tab_computes_Heikin_Ashi_series_for_a_strategy_that_reads_them(bool readsHeikinAshi)
    {
        const string symbol = "KAS/USDT";
        var raw = Bars(120);
        var feeds = Substitute.For<IMarketFeeds>();
        feeds.GetBarsAsync(Arg.Any<ChartIdentity>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Ohlcv>)raw);
        var alerts = Substitute.For<IAlertOrchestrator>();
        alerts.GetAlerts().Returns(new List<AlertDefinition>());

        var strategyStates = new List<WorkspaceState>();
        var strategy = SpyStrategy(readsHeikinAshi, strategyStates);
        var engine = Substitute.For<IStrategyEngine>();
        engine.ActiveStrategies.Returns(new List<ActiveStrategy>
        {
            new("s", strategy, new Dictionary<string, object>(), StrategyExecutionMode.Suggestion, false, symbol),
        });

        var monitor = new BackgroundWorkspaceMonitor(
            new ChartIdentity("Spot", "Bitstamp", symbol, "1d"), symbol,
            ImmutableList.Create(IndicatorSeries(SpyCode)),
            feeds, Service(new SpyProvider()), alerts, Substitute.For<IAlertEvaluator>(), engine, new SpyEventBus(),
            NullLogger.Instance, TimeSpan.FromHours(1), heikinAshi: false);

        await monitor.EvaluateOnceAsync(CancellationToken.None);
        await monitor.EvaluateOnceAsync(CancellationToken.None);

        Assert.NotEmpty(strategyStates);
        Assert.All(strategyStates, s =>
        {
            Assert.Equal(raw.Select(b => b.Close), s.ActiveSeries.Single().Data.ComponentData["close"]);
            if (readsHeikinAshi)
                Assert.Equal(Ha(raw).Select(b => b.Close), s.HeikinAshiSeries!.Single().Data.ComponentData["close"]);
            else
                Assert.Null(s.HeikinAshiSeries);
        });
    }

    // ── A headless tab (browser closed) computes on the candles it shows ──────

    private static HeadlessChartFactory HeadlessStack()
    {
        var providers = new List<IIndicatorProvider> { new CoreIndicatorProvider(), new SkenderTrendProvider() };
        var indicators = new IndicatorService(providers, NullLogger<IndicatorService>.Instance);
        var engine = new IndicatorEngine(indicators, new CustomIndicatorRegistry(), providers);
        var styling = new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());
        var prefs = new MockIndicatorPreferencesService();
        return new HeadlessChartFactory(indicators, engine, new IndicatorStateMapper(), new IndicatorModelFactory(styling, prefs),
            prefs, new IndicatorContextAnalyzer());
    }

    private static SeriesConfig SavedEma()
    {
        var cfg = new SeriesConfig
        {
            Id = "e", Name = "EMA", FriendlyName = "EMA", IndicatorCode = "Ema", Pane = "Main",
            IsAutoNarrated = true, IsVisible = true,
        };
        cfg.Parameters["lookbackPeriods"] = 10;
        return cfg;
    }

    private static async Task<Dictionary<string, double[]>> HeadlessEma(List<Ohlcv> bars, bool heikinAshi)
    {
        var chart = HeadlessStack().Create(new ChartIdentity("Spot", "Bitstamp", "BTC/USD", "1h"),
            new[] { SavedEma() }, Array.Empty<AlertDefinition>(), heikinAshi);
        var obs = await chart.ObserveAsync(bars, isFullHistory: true, CancellationToken.None);
        Assert.NotNull(obs.State);
        return obs.State!.ActiveSeries.Single(s => s.IndicatorCode == "Ema").Data.ComponentData;
    }

    [Fact]
    public async Task A_headless_Heikin_Ashi_tab_computes_its_indicators_on_Heikin_Ashi_candles()
    {
        var raw = Bars(120);
        var onChart = await HeadlessEma(raw, heikinAshi: true);
        var onHaBars = await HeadlessEma(Ha(raw), heikinAshi: false);
        var onRaw = await HeadlessEma(raw, heikinAshi: false);

        Assert.NotEmpty(onChart);
        foreach (var key in onChart.Keys)
        {
            Assert.Equal(onHaBars[key], onChart[key]);
            Assert.NotEqual(onRaw[key], onChart[key]);
        }
    }

    // ── The renderer: a panned Heikin-Ashi candle is the whole series' candle ──

    /// <summary>
    /// Sixty bars flat at 100, then twenty flat at 200, viewed from bar 60. Heikin-Ashi over the
    /// whole history opens bar 60 at 100 (the midpoint of the candle before) and closes it at 200:
    /// a body across the whole gap, which is what the readouts say and the indicators compute on.
    /// Built from the visible slice alone, bar 60 opened at its own midpoint, 200, and drew as a
    /// sliver. By bar 70 the whole-series candle has closed the gap too, so its column is the
    /// background through the middle.
    /// </summary>
    [Fact]
    public void A_panned_Heikin_Ashi_candle_is_drawn_from_the_whole_series()
    {
        var bars = Enumerable.Range(0, 80).Select(i =>
        {
            double p = i < 60 ? 100 : 200;
            return new Ohlcv(T0.AddDays(i), p, p + 1, p - 1, p, 1000);
        }).ToList();

        var f = DbsiChartHarness.Render(bars, new[] { DbsiChartHarness.Candles() }, 800, 600, 1f,
            start: 60, length: 20, heikinAshi: true, range: (90, 210));

        int x60 = (int)f.X(60), x70 = (int)f.X(70);
        int yTop = (int)f.Y(180), yBottom = (int)f.Y(120);
        int differing = 0, rows = 0;
        for (int y = yTop; y <= yBottom; y++, rows++)
            if (f.Bmp.GetPixel(x60, y) != f.Bmp.GetPixel(x70, y)) differing++;

        Assert.True(rows > 20, $"the fixture spans too few rows ({rows})");
        Assert.True(differing > rows * 0.8,
            $"bar 60's body should cover 120..180; only {differing} of {rows} rows differ from bar 70's background");
    }
}
