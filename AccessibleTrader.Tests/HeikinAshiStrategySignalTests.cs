using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Core.Strategies;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// A strategy that reads its signals on Heikin-Ashi candles (<see cref="StrategySpec.SignalsOnHeikinAshi"/>,
/// 2026-10-22), modelled on TradingView's "Fill orders on standard OHLC": its conditions read
/// Heikin-Ashi candles and the indicators computed on them, while its entry, stop, targets, fills
/// and P&amp;L stay on the prices the market traded. Filling at Heikin-Ashi prices is the known
/// trap: a Heikin-Ashi close is an average, often a price that never traded.
///
/// <para>The fixture is one bar that only Heikin-Ashi sees: flat at 100, then a bar opening,
/// closing and lowest at 100 with a high of 140. Its real close is 100; its Heikin-Ashi close is
/// the average of the four, 110. A condition "close above 105" is true on that bar on Heikin-Ashi
/// and false on the real bars, and a stop 1% under the real entry is 99, not 108.90.</para>
/// </summary>
public sealed class HeikinAshiStrategySignalTests
{
    private static readonly DateTime T0 = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
    private const int WickBar = 30;
    private const string SpyCode = "HA_SIGNAL_SPY";

    private static List<Ohlcv> Bars(int n = 40) => Enumerable.Range(0, n)
        .Select(i => i == WickBar
            ? new Ohlcv(T0.AddHours(i), 100, 140, 100, 100, 1000)
            : new Ohlcv(T0.AddHours(i), 100, 100, 100, 100, 1000))
        .ToList();

    /// <summary>Its one component is the closes it was handed: what it computed on is visible.</summary>
    private sealed class SpyProvider : IIndicatorProvider
    {
        public string Name => "Spy";
        public List<IndicatorMetadata> GetIndicators() => new() { new IndicatorMetadata { Name = "Spy", Code = SpyCode } };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            var close = buffer.GetComponentSpan("close");
            for (int i = 0; i < data.Length; i++) close[i] = data[i].Close;
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 0;

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> calculatedResults,
            int index, Dictionary<string, object> parameters) => "";
    }

    private static IndicatorService Indicators() =>
        new(new List<IIndicatorProvider> { new SpyProvider() }, NullLogger<IndicatorService>.Instance);

    private sealed class Catalog : ISignalCatalog
    {
        private readonly List<SignalDescriptor> _all = new()
        {
            new("SPY.close", SpyCode, "close", SignalKind.Line, "Spy close"),
            new("CANDLES.body", "CANDLES", "body", SignalKind.Line, "Close"),
        };
        public IReadOnlyList<SignalDescriptor> All => _all;
        public SignalDescriptor? GetById(string id) => _all.FirstOrDefault(d => d.Id == id);
        public IReadOnlyList<SignalDescriptor> GetForIndicator(string code) => _all.Where(d => d.IndicatorCode == code).ToList();
        public void Refresh() { }
    }

    private static ChartSeries Series(string id, string code, string component, double[] data)
    {
        var config = new SeriesConfig { Id = id, IndicatorCode = code, Name = code };
        config.Components.Add(new ComponentConfig { Name = component });
        var buffer = new SeriesDataBuffer { SeriesId = id };
        buffer.ComponentData[component] = data;
        return new ChartSeries(config, buffer);
    }

    /// <summary>The chart: its spy indicator holds what the chart computed (Heikin-Ashi closes on a
    /// Heikin-Ashi chart); its candle series holds the real closes on either, as the live chart's does.</summary>
    private static WorkspaceState Chart(List<Ohlcv> raw, bool heikinAshi) => WorkspaceState.Initial with
    {
        Identity = new ChartIdentity("Spot", "Bitstamp", "BTC/USD", "1h"),
        IsHeikinAshi = heikinAshi,
        ActiveSeries = ImmutableList.Create(
            Series("spy", SpyCode, "close", (heikinAshi ? ChartMath.CalculateHeikinAshi(raw) : raw).Select(b => b.Close).ToArray()),
            Series(CoreSeriesIds.Candles, "CANDLES", "body", raw.Select(b => b.Close).ToArray())),
        Data = new TimeSeriesBuffer<Ohlcv>(raw),
        InitStatus = InitializationStatus.Ready,
    };

    private static StrategySpec Spec(bool heikinAshi, string descriptor = "SPY.close",
        EntryTrigger? entry = null) => new(
        Id: "ha", Name: "Heikin-Ashi wick", Description: "",
        Side: OrderSide.Buy,
        Conditions: new ConditionLeaf("above", descriptor, LeafOperator.GreaterThan, Value: 105),
        Risk: new RiskPlan(
            Stop: new StopSource(StopSourceKind.PercentOfPrice, PercentValue: 1.0),
            TpLadder: new List<TpLadderRung> { new(TargetSourceKind.RiskRewardMultiple, Multiple: 2.0, ClosePortion: 1.0) },
            Sizing: new PositionSizing(SizingMode.FixedRiskPercent, RiskPercent: 0.005),
            Entry: entry ?? new EntryTrigger(EntryTriggerKind.Immediate)),
        SignalsOnHeikinAshi: heikinAshi);

    private static ConfigurableStrategy Strategy(StrategySpec spec, SpyEventBus? bus = null)
    {
        var catalog = new Catalog();
        return new ConfigurableStrategy(spec, new ConditionEvaluator(catalog), new RiskPlanResolver(levels: null),
            catalog, bus ?? new SpyEventBus(), instanceId: "t");
    }

    /// <summary>Every bar up to and including <paramref name="last"/>, as the live engine hands them,
    /// each with the state its host builds for this strategy.</summary>
    private static StrategySignal? Replay(ConfigurableStrategy strategy, List<Ohlcv> raw, bool chartIsHeikinAshi, int last,
        bool hostComputesHeikinAshi = true)
    {
        strategy.Initialize(Array.Empty<Ohlcv>(), WorkspaceState.Initial, new Dictionary<string, object>());
        StrategySignal? signal = null;
        for (int i = 0; i <= last; i++)
        {
            var history = raw.Take(i + 1).ToList();
            var chart = Chart(history, chartIsHeikinAshi);
            var state = hostComputesHeikinAshi
                ? StrategyCandles.For(Indicators(), chart, history.ToArray(), StrategyCandles.WantsHeikinAshi(strategy))
                : chart;
            signal = strategy.OnBar(history[^1], history, state);
        }
        return signal;
    }

    // ── The conditions read Heikin-Ashi, the orders the real prices ───────────

    /// <remarks>The price leaf on a Heikin-Ashi chart is the case that needs the candle series
    /// emptied (<c>StrategyCandles.WithoutPriceLines</c>): there the Heikin-Ashi set is the chart's
    /// own series, whose candle series carries the real closes.</remarks>
    [Theory]
    [InlineData("SPY.close", false)]      // an indicator computed on the candles
    [InlineData("SPY.close", true)]
    [InlineData("CANDLES.body", false)]   // the close itself, which the candle series carries raw
    [InlineData("CANDLES.body", true)]
    public void A_Heikin_Ashi_signal_fires_on_the_Heikin_Ashi_close_and_places_its_stop_from_the_real_close(string descriptor, bool chartIsHeikinAshi)
    {
        var signal = Replay(Strategy(Spec(heikinAshi: true, descriptor)), Bars(), chartIsHeikinAshi, last: WickBar);

        Assert.NotNull(signal);
        Assert.Equal(99.0, signal!.StopLoss!.Value, 6);   // 1% under 100; on the Heikin-Ashi close, 108.90
    }

    [Theory]
    [InlineData("SPY.close")]
    [InlineData("CANDLES.body")]
    public void The_same_strategy_on_the_real_candles_does_not_see_the_Heikin_Ashi_close(string descriptor)
    {
        Assert.Null(Replay(Strategy(Spec(heikinAshi: false, descriptor)), Bars(), chartIsHeikinAshi: false, last: WickBar));
    }

    /// <summary>A strategy that has not asked for Heikin-Ashi reads the real bars on a Heikin-Ashi
    /// chart too: the chart's candle type is a way of looking, not a trading rule.</summary>
    [Fact]
    public void On_a_Heikin_Ashi_chart_only_the_strategy_that_asked_reads_Heikin_Ashi()
    {
        Assert.Null(Replay(Strategy(Spec(heikinAshi: false)), Bars(), chartIsHeikinAshi: true, last: WickBar));
        Assert.NotNull(Replay(Strategy(Spec(heikinAshi: true)), Bars(), chartIsHeikinAshi: true, last: WickBar));
    }

    /// <summary>"The last N candles closed in the trade's direction" reads the candles the
    /// conditions read. Every real bar here opens and closes at 100 — none closes up — while the
    /// wick's Heikin-Ashi candle opens at 100 and closes at 110.</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void The_next_candle_close_trigger_reads_the_candles_the_conditions_read(bool heikinAshi, bool fires)
    {
        // "Spy close above 99" holds on every bar, so both strategies arm on bar 0 and only the
        // trigger differs: one candle closing up, which only the wick's Heikin-Ashi candle does.
        var spec = Spec(heikinAshi, entry: new EntryTrigger(EntryTriggerKind.OnNextNCandleClose, NCandles: 1)) with
        {
            Conditions = new ConditionLeaf("always", "SPY.close", LeafOperator.GreaterThan, Value: 99),
        };

        var signal = Replay(Strategy(spec), Bars(), chartIsHeikinAshi: false, last: WickBar);

        Assert.Equal(fires, signal != null);
    }

    /// <summary>With no host computing the Heikin-Ashi series, the strategy does not trade the real
    /// bars under a Heikin-Ashi label: it does nothing, and says why, once.</summary>
    [Fact]
    public void Without_Heikin_Ashi_series_a_Heikin_Ashi_strategy_does_not_trade_and_says_why_once()
    {
        var bus = new SpyEventBus();
        var strategy = Strategy(Spec(heikinAshi: true) with
        {
            Conditions = new ConditionLeaf("always", "SPY.close", LeafOperator.GreaterThan, Value: 99),
        }, bus);

        var signal = Replay(strategy, Bars(), chartIsHeikinAshi: false, last: WickBar, hostComputesHeikinAshi: false);

        Assert.Null(signal);
        var said = Assert.Single(bus.Log.OfType<FeedbackRequestEvent>(), e => e.Message?.Contains("Heikin-Ashi") == true);
        Assert.Equal("Strategy 'Heikin-Ashi wick' reads Heikin-Ashi signals and none were computed for it. It will not trade until they are.", said.Message);
    }

    // ── The backtester tests what the live engine trades ──────────────────────

    private static BacktestConfig Config() => new(CommissionRate: 0, SlippagePercent: 0, WarmupBars: 5);

    [Fact]
    public async Task A_backtest_of_a_Heikin_Ashi_strategy_on_a_candle_chart_signals_on_Heikin_Ashi_and_fills_at_real_prices()
    {
        var raw = Bars();
        var result = await new StrategyBacktester(indicators: Indicators())
            .RunAsync(Strategy(Spec(heikinAshi: true)), raw, Config(), Chart(raw, heikinAshi: false));

        var trade = Assert.Single(result.Trades);
        Assert.Equal(100.0, trade.EntryPrice, 6);
        Assert.Equal(99.0, trade.StopPrice!.Value, 6);
    }

    [Fact]
    public async Task A_backtest_of_the_same_strategy_on_the_real_candles_takes_no_trade()
    {
        var raw = Bars();
        var result = await new StrategyBacktester(indicators: Indicators())
            .RunAsync(Strategy(Spec(heikinAshi: false)), raw, Config(), Chart(raw, heikinAshi: false));

        Assert.Empty(result.Trades);
    }

    /// <summary>
    /// The defect this fixed: a backtest started on a Heikin-Ashi chart handed a real-bar strategy
    /// the chart's Heikin-Ashi indicators and filled it at real prices, while the live engine
    /// recomputed its indicators on the real bars. The backtest showed a trade live would never take.
    /// </summary>
    [Fact]
    public async Task A_backtest_on_a_Heikin_Ashi_chart_reads_a_real_bar_strategy_on_the_real_bars()
    {
        var raw = Bars();
        var result = await new StrategyBacktester(indicators: Indicators())
            .RunAsync(Strategy(Spec(heikinAshi: false)), raw, Config(), Chart(raw, heikinAshi: true));

        Assert.Empty(result.Trades);
    }

    [Fact]
    public async Task A_backtest_with_a_date_filter_reads_the_Heikin_Ashi_series_at_the_bar_it_stands_on()
    {
        // The window starts at bar 10: the Heikin-Ashi series are sliced with the real ones, so
        // the wick is still read on its own bar, and the order fills where the unfiltered run's
        // does (the next bar's open). Unsliced, the wick sits 10 bars past the window's end.
        var raw = Bars();
        var whole = await new StrategyBacktester(indicators: Indicators())
            .RunAsync(Strategy(Spec(heikinAshi: true)), raw, Config(), Chart(raw, heikinAshi: false));
        var filtered = await new StrategyBacktester(indicators: Indicators())
            .RunAsync(Strategy(Spec(heikinAshi: true)), raw, Config() with { StartDate = raw[10].Date }, Chart(raw, heikinAshi: false));

        Assert.Equal(raw[WickBar + 1].Date, Assert.Single(whole.Trades).EntryTime);
        Assert.Equal(raw[WickBar + 1].Date, Assert.Single(filtered.Trades).EntryTime);
    }

    [Fact]
    public async Task A_backtester_that_cannot_compute_Heikin_Ashi_refuses_with_the_reason()
    {
        var raw = Bars();
        var result = await new StrategyBacktester()
            .RunAsync(Strategy(Spec(heikinAshi: true)), raw, Config(), Chart(raw, heikinAshi: false));

        Assert.Empty(result.Trades);
        Assert.Contains("Heikin-Ashi", result.SpeechSummary);
    }
}
