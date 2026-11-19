using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Alerts;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// Alerts on the charts built off the clock (Cody, 2026-11-19, "yes on your suggestions on the
/// alerts"): a price, candle or drawn-level alert reads the REAL price behind the bricks; an
/// indicator alert reads the indicator as drawn, on the bricks; and three new conditions — new up
/// bar, new down bar, reversal — watch the chart's own bars.
/// </summary>
public sealed class BuiltChartAlertsTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly ChartIdentity Id = new("Spot", "Bitstamp", "BTC/USD", "1h");
    private static readonly IReadOnlyDictionary<string, double> NoPrev = new Dictionary<string, double>();

    private static Ohlcv Bar(int i, double open, double close) =>
        new(Start.AddHours(i), open, Math.Max(open, close) + 1, Math.Min(open, close) - 1, close, 10);

    /// <summary>The real bars behind the bricks: closes 120, 128, 136.</summary>
    private static readonly Ohlcv[] Real = { Bar(0, 110, 120), Bar(1, 120, 128), Bar(2, 128, 136) };

    /// <summary>A chart built off the clock, its bricks given by hand as (open, close).</summary>
    private static WorkspaceState Built(
        (double Open, double Close)[] bricks,
        PriceChartStyle style = PriceChartStyle.Renko,
        ChartIdentity? identity = null,
        Ohlcv[]? real = null,
        params ChartSeries[] series)
    {
        var drawn = bricks.Select((b, i) => new Ohlcv(Start.AddHours(i), b.Open,
            Math.Max(b.Open, b.Close), Math.Min(b.Open, b.Close), b.Close, 10)).ToArray();
        var spans = drawn.Select((b, i) => new BrickSpan(b.Date, b.Date.AddHours(1), i, i, b.Close > b.Open))
            .ToImmutableArray();
        return WorkspaceState.Initial with
        {
            Identity = identity ?? Id,
            SymbolDisplayName = "BTC/USD",
            ChartStyle = style,
            Data = new TimeSeriesBuffer<Ohlcv>(drawn),
            TimeBars = new TimeSeriesBuffer<Ohlcv>(real ?? Real),
            BrickSpans = spans,
            BrickBoxSize = 10,
            ActiveSeries = ImmutableList.Create(series),
            CurrentDataIndex = drawn.Length - 1,
            InitStatus = InitializationStatus.Ready,
        };
    }

    private static AlertEvaluator NewEvaluator(ISdkCandlePatternAnalyzer? pattern = null) =>
        new(pattern ?? Substitute.For<ISdkCandlePatternAnalyzer>(), Substitute.For<IIndicatorContextAnalyzer>());

    /// <summary>Evaluated the way the on-screen chart used to call it: with the DRAWN bars.</summary>
    private static List<AlertFired> Eval(AlertEvaluator ev, AlertDefinition a, WorkspaceState s,
        IReadOnlyDictionary<string, double>? prev = null) =>
        ev.EvaluateAlerts(new[] { a }, s, s.Data[^1], s.Data[^2], prev ?? NoPrev).ToList();

    private static AlertDefinition Alert(AlertTarget target, AlertCondition condition, double? threshold = null,
        string id = "a") => new()
    {
        Id = id, Name = "Up", Target = target, Condition = condition, Threshold = threshold,
        Delivery = AlertDelivery.Speech,
    };

    private static ChartSeries Series(string id, string code, string comp, double[] data)
    {
        var buffer = new SeriesDataBuffer { SeriesId = id };
        var config = new SeriesConfig { Id = id, Name = code, FriendlyName = code, IndicatorCode = code, Pane = "Main" };
        buffer.ComponentData[comp] = data;
        config.Components.Add(new ComponentConfig { Name = comp, DisplayName = comp });
        return new ChartSeries(config, buffer);
    }

    // ---- The real price ---------------------------------------------------------------------

    /// <summary>
    /// Bricks 110→120 and 120→130; the market went 128 then 136. A 133 alert is crossed by the
    /// market and never by a brick; a 125 alert is crossed by a brick (120 then 130) and never by
    /// the market, which was already above it.
    /// </summary>
    [Theory]
    [InlineData(133.0, 136.0)]
    [InlineData(125.0, double.NaN)]
    public void A_price_alert_on_a_Renko_chart_fires_on_the_real_price_not_the_bricks(double level, double firesAt)
    {
        var state = Built(new[] { (110.0, 120.0), (120.0, 130.0) });
        var fired = Eval(NewEvaluator(), Alert(AlertTarget.Price, AlertCondition.CrossesAbove, level), state);

        if (double.IsNaN(firesAt)) { Assert.Empty(fired); return; }
        Assert.Equal(firesAt, Assert.Single(fired).TriggeringValue);
    }

    /// <summary>
    /// "Price crosses the SMA" on a Renko chart: the SMA is drawn per brick (127, then 132) and the
    /// price is the market's (128, then 136). Both sides are measured against the line's newest
    /// value, 132; against the brick before's 127, 128 was already above and nothing crossed.
    /// </summary>
    [Fact]
    public void Price_crossing_a_line_on_a_Renko_chart_compares_the_real_price_with_the_lines_newest_value()
    {
        var state = Built(new[] { (110.0, 120.0), (120.0, 130.0) },
            series: Series("sma", "Sma", "Sma", new[] { 127.0, 132.0 }));
        var alert = Alert(AlertTarget.Price, AlertCondition.CrossesAbove) with
        {
            LineIndicatorCode = "Sma", LineComponentName = "Sma", LineSeriesId = "sma",
        };

        var fired = Assert.Single(Eval(NewEvaluator(), alert, state));
        Assert.Equal(136, fired.TriggeringValue);
    }

    /// <summary>An indicator alert reads the indicator as drawn: one value per brick, at the newest brick.</summary>
    [Fact]
    public void An_indicator_alert_on_a_Renko_chart_still_reads_the_newest_brick()
    {
        var state = Built(new[] { (110.0, 120.0), (120.0, 130.0) },
            series: Series("rsi", "Rsi", "Rsi", new[] { 50.0, 75.0 }));
        var alert = Alert(AlertTarget.Indicator, AlertCondition.CrossesAbove, 70) with
        {
            IndicatorCode = "Rsi", ComponentName = "Rsi", SeriesId = "rsi",
        };

        var fired = Assert.Single(Eval(NewEvaluator(), alert, state,
            new Dictionary<string, double> { ["#rsi.Rsi"] = 65, ["Rsi.Rsi"] = 65 }));
        Assert.Equal(75, fired.TriggeringValue);
    }

    /// <summary>A brick has no wicks: a candle-pattern alert on a Renko chart reads the real candles.</summary>
    [Fact]
    public void A_pattern_alert_on_a_Renko_chart_reads_the_real_candles()
    {
        var pattern = Substitute.For<ISdkCandlePatternAnalyzer>();
        Ohlcv seen = default;
        IReadOnlyList<Ohlcv>? context = null;
        pattern.Analyze(Arg.Any<Ohlcv>(), Arg.Any<Ohlcv?>(), Arg.Any<Ohlcv?>(), Arg.Any<IReadOnlyList<Ohlcv>?>())
            // Only what it is handed matters; the evaluator isolates the throw per alert.
            .Returns<CandleAnalysis>(ci =>
            {
                seen = ci.ArgAt<Ohlcv>(0);
                context = ci.ArgAt<IReadOnlyList<Ohlcv>?>(3);
                throw new InvalidOperationException("seen");
            });

        var state = Built(new[] { (110.0, 120.0), (120.0, 130.0) });
        Eval(NewEvaluator(pattern), Alert(AlertTarget.Candle, AlertCondition.PatternDetected) with
        {
            Pattern = CandlePattern.BullishEngulfing,
        }, state);

        Assert.Equal(Real[^1], seen);
        Assert.Equal(Real.Length, context!.Count);
        Assert.Equal(Real[^1], context[^1]);
    }

    // ---- New up bar, new down bar, reversal -------------------------------------------------

    private static readonly (double, double)[] TwoUp = { (100, 110), (110, 120) };

    private static (double, double)[] Then(params (double, double)[] more) => TwoUp.Concat(more).ToArray();

    [Fact]
    public void A_new_up_brick_fires_once_and_says_the_brick()
    {
        var ev = NewEvaluator();
        var alert = Alert(AlertTarget.Candle, AlertCondition.NewUpBar);

        Assert.Empty(Eval(ev, alert, Built(TwoUp)));                    // the first look only remembers
        var state = Built(Then((120, 130)));
        var fired = Assert.Single(Eval(ev, alert, state));
        Assert.Equal("Up: new up brick, 120 to 130.", fired.SpeechText);
        Assert.Empty(Eval(ev, alert, state));                            // once per brick
        Assert.Empty(Eval(ev, alert, Built(Then((120, 130)))));         // a rebuilt buffer, the same bricks
    }

    [Theory]
    [InlineData(AlertCondition.NewUpBar, "Up: new up brick, 120 to 130.")]
    [InlineData(AlertCondition.NewDownBar, "Up: new down brick, 120 to 110.")]
    [InlineData(AlertCondition.Reversal, "Up: reversal, new down brick, 120 to 110.")]
    public void Several_bricks_in_one_update_fire_once_for_the_newest_that_matches(AlertCondition condition, string said)
    {
        var ev = NewEvaluator();
        var alert = Alert(AlertTarget.Candle, condition);
        Eval(ev, alert, Built(TwoUp));

        var fired = Assert.Single(Eval(ev, alert, Built(Then((120, 130), (120, 110)))));
        Assert.Equal(said, fired.SpeechText);
    }

    [Theory]
    [InlineData(AlertCondition.NewDownBar)]
    [InlineData(AlertCondition.Reversal)]
    public void A_brick_the_same_way_is_neither_down_nor_a_reversal(AlertCondition condition)
    {
        var ev = NewEvaluator();
        var alert = Alert(AlertTarget.Candle, condition);
        Eval(ev, alert, Built(TwoUp));
        Assert.Empty(Eval(ev, alert, Built(Then((120, 130)))));
    }

    [Fact]
    public void Two_new_up_bricks_say_the_newer()
    {
        var ev = NewEvaluator();
        var alert = Alert(AlertTarget.Candle, AlertCondition.NewUpBar);
        Eval(ev, alert, Built(TwoUp));
        Assert.Equal("Up: new up brick, 130 to 140.",
            Assert.Single(Eval(ev, alert, Built(Then((120, 130), (130, 140))))).SpeechText);
    }

    /// <summary>A reversal is a turn: the second down brick in a row is a new down brick, not a reversal.</summary>
    [Fact]
    public void A_down_brick_after_a_down_brick_is_not_a_reversal()
    {
        var ev = NewEvaluator();
        var reversal = Alert(AlertTarget.Candle, AlertCondition.Reversal, id: "r");
        var down = Alert(AlertTarget.Candle, AlertCondition.NewDownBar, id: "d");
        var turned = Then((120, 110));
        Eval(ev, reversal, Built(turned));
        Eval(ev, down, Built(turned));

        var again = Built(turned.Append((110.0, 100.0)).ToArray());
        Assert.Empty(Eval(ev, reversal, again));
        Assert.Single(Eval(ev, down, again));
    }

    [Fact]
    public void Another_chart_or_rebuilt_bricks_start_again_silently()
    {
        var ev = NewEvaluator();
        var alert = Alert(AlertTarget.Candle, AlertCondition.NewUpBar);
        Eval(ev, alert, Built(TwoUp));

        var eth = Id with { Symbol = "ETH/USD" };
        Assert.Empty(Eval(ev, alert, Built(Then((120, 130)), identity: eth)));       // a tab switch
        Assert.Single(Eval(ev, alert, Built(Then((120, 130), (130, 140)), identity: eth)));

        // History reloaded: none of the bricks is the one remembered.
        Assert.Empty(Eval(ev, alert, Built(new[] { (205.0, 215.0), (215.0, 225.0) }, identity: eth)));
    }

    [Theory]
    [InlineData(PriceChartStyle.LineBreak, "Up: new up block, 120 to 130.")]
    [InlineData(PriceChartStyle.RangeBars, "Up: new up range bar, 120 to 130.")]
    public void The_new_bar_is_named_after_the_chart_type(PriceChartStyle style, string said)
    {
        var ev = NewEvaluator();
        var alert = Alert(AlertTarget.Candle, AlertCondition.NewUpBar);
        Eval(ev, alert, Built(TwoUp, style));
        Assert.Equal(said, Assert.Single(Eval(ev, alert, Built(Then((120, 130)), style))).SpeechText);
    }

    /// <summary>
    /// On a time chart the newest candle is still forming, so a candle is up or down once the next
    /// one opens: a forming candle that dips and recovers says nothing.
    /// </summary>
    [Fact]
    public void On_a_time_chart_a_candle_is_judged_when_the_next_one_opens()
    {
        WorkspaceState Candles(params Ohlcv[] bars) => WorkspaceState.Initial with
        {
            Identity = Id,
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            CurrentDataIndex = bars.Length - 1,
        };
        var ev = NewEvaluator();
        var down = Alert(AlertTarget.Candle, AlertCondition.NewDownBar);

        Assert.Empty(Eval(ev, down, Candles(Bar(0, 100, 110), Bar(1, 110, 111))));
        Assert.Empty(Eval(ev, down, Candles(Bar(0, 100, 110), Bar(1, 110, 104))));   // still forming
        var fired = Assert.Single(Eval(ev, down, Candles(Bar(0, 100, 110), Bar(1, 110, 105), Bar(2, 105, 106))));
        Assert.Equal("Up: new down candle, 110 to 105.", fired.SpeechText);
    }

    [Fact]
    public void A_new_bar_condition_needs_the_candle_target_and_the_chart()
    {
        var onPrice = Alert(AlertTarget.Price, AlertCondition.NewUpBar);
        var ev = NewEvaluator();
        Eval(ev, onPrice, Built(TwoUp));
        Assert.Empty(Eval(ev, onPrice, Built(Then((120, 130)))));
        Assert.Contains("Candle target", BackgroundWatchability.WhyUnfireable(onPrice));

        var onCandle = Alert(AlertTarget.Candle, AlertCondition.Reversal) with { Symbol = "BTC/USD", Provider = "Bitstamp" };
        Assert.Null(BackgroundWatchability.WhyUnfireable(onCandle));
        Assert.Null(BackgroundWatchability.WhyUnwatchable(onCandle, Array.Empty<SeriesConfig>()));
        Assert.Contains("chart's own bars", BackgroundWatchability.WhyUnwatchableWithoutAChart(onCandle));
        Assert.Equal("BTC/USD candle reverses (a bar the other way)",
            AlertDescriptions.Describe(onCandle, Array.Empty<ChartSeries>()));
    }

    // ---- The on-screen chart evaluates on every real tick ------------------------------------

    /// <summary>
    /// A Renko chart's bricks move only when one completes; the market moves every tick. A tick
    /// that changes only the real bars must still be evaluated, and with the real bars: on a chart
    /// with one brick, the price is still there to read.
    /// </summary>
    [Fact]
    public void The_on_screen_chart_evaluates_price_alerts_on_real_ticks_between_bricks()
    {
        var store = new MockWorkspaceStore();
        var evaluator = Substitute.For<IAlertEvaluator>();
        var calls = new List<Ohlcv>();
        evaluator.EvaluateAlerts(Arg.Any<IReadOnlyList<AlertDefinition>>(), Arg.Any<WorkspaceState>(),
                Arg.Any<Ohlcv>(), Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyDictionary<string, double>>())
            .Returns(ci => { calls.Add(ci.ArgAt<Ohlcv>(2)); return Enumerable.Empty<AlertFired>(); });
        var library = Substitute.For<IWorkspaceLibraryService>();
        library.LoadAlerts().Returns(new List<AlertDefinition> { Alert(AlertTarget.Price, AlertCondition.CrossesAbove, 133) });
        var orch = new AlertOrchestrator(store, evaluator, new SpyEventBus(), library, NullLogger<AlertOrchestrator>.Instance);
        orch.Start();

        var first = Built(new[] { (110.0, 120.0) });
        store.EmitState(first);                                                       // warm-up
        var tick = Real.Take(2).Append(Bar(2, 128, 137)).ToArray();
        store.EmitState(first with { TimeBars = new TimeSeriesBuffer<Ohlcv>(tick) });   // same brick, new price

        Assert.Equal(137, Assert.Single(calls).Close);
    }
}
