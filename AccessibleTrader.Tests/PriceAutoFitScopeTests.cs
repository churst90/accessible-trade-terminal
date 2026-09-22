using System.Collections.Immutable;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>What the price pane's auto-fit is allowed to see.</b>
///
/// <para>
/// Until 2026-09-21 it saw the OHLC buffer, visible reference levels and declared bounds, and
/// nothing else. Every indicator pane had expanded to fit its components since the beginning;
/// the price pane alone did not. So a Bollinger band, a Keltner or Donchian channel, an MA
/// cloud or a Chandelier Exit stop that swung wider than the candles was drawn OUTSIDE the
/// pane — and clipping on this chart is not merely a visual defect, because
/// <see cref="ChartMath.NormalizedPosition"/> normalises a value across exactly this range and
/// the sonification turns the result into a frequency. <b>Off the pane is also silent.</b>
/// </para>
///
/// <para>
/// The switch (<see cref="WorkspaceState.ScalePriceOnly"/>, Alt+F) exists because the two
/// answers trade against each other and neither is free: including a wide band widens the range
/// and so compresses the price line's share of the pitch band. Default OFF — include — because
/// that failure is AUDIBLE where the other is silent, and a user cannot investigate something
/// they were never told about.
/// </para>
/// </summary>
public sealed class PriceAutoFitScopeTests
{
    private const int BarCount = 50;

    /// <summary>
    /// Candles spanning 95–105. The span matters and is not arbitrary: the units guard this
    /// pass reuses from the reference levels admits a value up to three visible spans outside
    /// the data, so the fixture needs a realistic span for "just outside" and "nowhere near" to
    /// be different things. Here that boundary sits at 65 and 135.
    /// </summary>
    private static List<Ohlcv> Bars()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, BarCount)
            .Select(i => new Ohlcv(t.AddMinutes(i), 100, 105, 95, 100, 1000))
            .ToList();
    }

    private static ChartSeries Candles()
    {
        var cfg = new SeriesConfig
        {
            Id = "candles", Name = "Candles", FriendlyName = "Candles",
            IndicatorCode = "CANDLES", Pane = "Main", IsVisible = true, Volume = 1f,
        };
        return new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = "candles" });
    }

    /// <summary>A Main-pane band: two components, one above the candles and one below.</summary>
    private static ChartSeries Band(double upper, double lower, bool seriesVisible = true,
                                    bool upperVisible = true, string? subPane = null)
    {
        var cfg = new SeriesConfig
        {
            Id = "bb", Name = "Bollinger Bands", FriendlyName = "Bollinger Bands",
            IndicatorCode = "Bb", Pane = "Main", IsVisible = seriesVisible, Volume = 1f,
        };
        cfg.Components.Add(new ComponentConfig
        {
            Name = "upper", DisplayName = "Upper Band", IsVisible = upperVisible, IsEnabled = true,
            SubPaneName = subPane ?? string.Empty,
        });
        cfg.Components.Add(new ComponentConfig
        {
            Name = "lower", DisplayName = "Lower Band", IsVisible = true, IsEnabled = true,
            SubPaneName = subPane ?? string.Empty,
        });
        var data = new SeriesDataBuffer { SeriesId = "bb" };
        data.ComponentData["upper"] = Enumerable.Repeat(upper, BarCount).ToArray();
        data.ComponentData["lower"] = Enumerable.Repeat(lower, BarCount).ToArray();
        return new ChartSeries(cfg, data);
    }

    private static WorkspaceState StateWith(bool scalePriceOnly, params ChartSeries[] series) =>
        WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(Bars()),
            ActiveSeries = ImmutableList.CreateRange(series),
            ViewportStartIndex = 0,
            ViewportLength = BarCount,
            ScalePriceOnly = scalePriceOnly,
        };

    private static (double Min, double Max) MainRange(bool scalePriceOnly, params ChartSeries[] series)
        => new ViewportRangeCalculator().Calculate(StateWith(scalePriceOnly, series)).MainRange;

    // ── The defect, stated as the difference between the two answers ─────────────

    [Fact]
    public void ABandWiderThanTheCandles_IsInsideThePane_ByDefault()
    {
        var (min, max) = MainRange(scalePriceOnly: false, Candles(), Band(upper: 110, lower: 90));

        Assert.True(max >= 110, $"the upper band at 110 must be inside the pane; the pane tops out at {max}");
        Assert.True(min <= 90, $"the lower band at 90 must be inside the pane; the pane bottoms out at {min}");
    }

    [Fact]
    public void ThatIsAChange_TheOldBehaviourClippedIt_AndScalePriceOnlyStillDoes()
    {
        var (min, max) = MainRange(scalePriceOnly: true, Candles(), Band(upper: 110, lower: 90));

        // The pre-2026-09-21 answer, which the switch preserves: OHLC 95–105 plus the 5% buffer.
        Assert.True(max < 110, "with ScalePriceOnly the band must be allowed off the pane, or the switch does nothing");
        Assert.True(min > 90, "with ScalePriceOnly the band must be allowed off the pane, or the switch does nothing");
        Assert.InRange(max, 105, 106);
        Assert.InRange(min, 94, 95);
    }

    /// <summary>
    /// The consequence that the user actually hears, asserted as a consequence rather than as a
    /// range: a value outside the pane normalises to a CLAMPED 0 or 1, which is the same number
    /// the pane's own floor and ceiling give. Two different prices, one pitch — the band goes
    /// mute at the edge and stays mute however far past it travels.
    /// </summary>
    [Fact]
    public void ClippedMeansMute_WhichIsWhyTheDefaultIncludesOverlays()
    {
        var clipped  = MainRange(scalePriceOnly: true,  Candles(), Band(upper: 110, lower: 90));
        var included = MainRange(scalePriceOnly: false, Candles(), Band(upper: 110, lower: 90));

        double at110 = ChartMath.NormalizedPosition(110, clipped.Min, clipped.Max, isLogScale: false);
        double at200 = ChartMath.NormalizedPosition(200, clipped.Min, clipped.Max, isLogScale: false);
        Assert.Equal(at110, at200, 9);   // both pinned at the ceiling: indistinguishable by ear

        double inc110 = ChartMath.NormalizedPosition(110, included.Min, included.Max, isLogScale: false);
        double inc200 = ChartMath.NormalizedPosition(200, included.Min, included.Max, isLogScale: false);
        Assert.True(inc110 < inc200, "inside the pane, a higher value must be a higher pitch");
        Assert.True(inc110 < 1.0, "the band must not be sitting on the ceiling once it is included");
    }

    // ── The three conditions, each one "what the eye is shown" ───────────────────

    [Fact]
    public void AHiddenSeriesDoesNotMoveTheAxis()
    {
        var (min, max) = MainRange(scalePriceOnly: false, Candles(), Band(upper: 110, lower: 90, seriesVisible: false));

        Assert.InRange(max, 105, 106);
        Assert.InRange(min, 94, 95);
    }

    [Fact]
    public void AHiddenComponentDoesNotMoveTheAxis_ButItsVisibleSiblingStillDoes()
    {
        var (min, max) = MainRange(scalePriceOnly: false, Candles(), Band(upper: 110, lower: 90, upperVisible: false));

        Assert.True(max < 110, "the hidden upper band must not leave its footprint on the axis");
        Assert.True(min <= 90, "the visible lower band must still be inside the pane");
    }

    [Fact]
    public void ASubPaneStripIsItsOwnAxis_AndDoesNotMoveTheMainOne()
    {
        var (min, max) = MainRange(scalePriceOnly: false, Candles(), Band(upper: 110, lower: 90, subPane: "Strip"));

        Assert.InRange(max, 105, 106);
        Assert.InRange(min, 94, 95);
    }

    /// <summary>
    /// The units guard, and it is the same one the reference levels above it use, for the same
    /// reason: a series whose <c>Pane</c> is unset falls back to "Main", and an indicator that
    /// emits something other than a price — LoukasCyclesProvider's day counts are the worked
    /// example — would otherwise run the price axis from 0 to 70,000 and compress every candle
    /// into a sliver. A component three visible spans outside the data is not a band.
    /// </summary>
    [Fact]
    public void AComponentThatIsObviouslyNotAPrice_IsRefused()
    {
        var (min, max) = MainRange(scalePriceOnly: false, Candles(), Band(upper: 90_000, lower: 0));

        Assert.InRange(max, 105, 106);
        Assert.InRange(min, 94, 95);
    }

    /// <summary>
    /// And the guard is measured against the ORIGINAL price span, not the running one, so a
    /// ladder of ever-wider components cannot walk the axis out one plausible step at a time.
    /// Each rung here is within three spans of the rung below it and far outside the candles.
    /// </summary>
    [Fact]
    public void TheUnitsGuardCannotBeWalkedOutwardsOneStepAtATime()
    {
        var ladder = new List<ChartSeries> { Candles() };
        double v = 105;
        for (int i = 0; i < 12; i++)
        {
            v += 20;                                  // two spans of the 95–105 data per rung
            var cfg = new SeriesConfig { Id = $"rung{i}", Name = $"Rung {i}", Pane = "Main", IsVisible = true, Volume = 1f };
            cfg.Components.Add(new ComponentConfig { Name = "v", IsVisible = true, IsEnabled = true });
            var buf = new SeriesDataBuffer { SeriesId = $"rung{i}" };
            buf.ComponentData["v"] = Enumerable.Repeat(v, BarCount).ToArray();
            ladder.Add(new ChartSeries(cfg, buf));
        }

        var (_, max) = MainRange(scalePriceOnly: false, ladder.ToArray());

        // Measured against the ORIGINAL span, only the first rung (125) is admissible: the guard
        // allows three spans of 10, so it stops at 135. Measured against a RUNNING span it would
        // not stop at all — once the axis reaches 125 its span is 30, and 145 is then well within
        // three of those, and so on up the ladder to 305.
        Assert.True(max < 135, $"the axis walked out to {max} — the plausibility guard is reading a moving target");
    }

    /// <summary>
    /// NaN is WARMUP, and warmup is universal — an EMA's first N bars, a pivot indicator's
    /// every-bar-but-a-few. A range calculator that let one through would produce a NaN axis and
    /// a silent chart, so this pins the skip rather than trusting it.
    /// </summary>
    [Fact]
    public void WarmupNaNIsSkipped_NotPropagatedIntoTheAxis()
    {
        var cfg = new SeriesConfig { Id = "ema", Name = "EMA 20", Pane = "Main", IsVisible = true, Volume = 1f };
        cfg.Components.Add(new ComponentConfig { Name = "line", IsVisible = true, IsEnabled = true });
        var buf = new SeriesDataBuffer { SeriesId = "ema" };
        var data = Enumerable.Repeat(double.NaN, BarCount).ToArray();
        for (int i = 20; i < BarCount; i++) data[i] = 108;      // warmup, then real values
        buf.ComponentData["line"] = data;

        var (min, max) = MainRange(scalePriceOnly: false, Candles(), new ChartSeries(cfg, buf));

        Assert.False(double.IsNaN(min) || double.IsNaN(max), "a warmup prefix must not reach the axis");
        Assert.True(max >= 108, "the EMA's real values must still be inside the pane");
    }

    /// <summary>A series with no overlay at all must be bit-for-bit what it always was.</summary>
    [Fact]
    public void WithNoOverlays_TheAnswerIsUnchangedByTheNewPass()
    {
        var withFlagOff = MainRange(scalePriceOnly: false, Candles());
        var withFlagOn  = MainRange(scalePriceOnly: true,  Candles());

        Assert.Equal(withFlagOn.Min, withFlagOff.Min, 9);
        Assert.Equal(withFlagOn.Max, withFlagOff.Max, 9);
    }

    /// <summary>
    /// <b>Cody's chart, 2026-09-22, and it is a regression this file's own change caused.</b>
    ///
    /// <para>
    /// Bollinger Bands declares SEVEN components on the Main pane and three of them are not
    /// prices: PercentB (0 to 1), ZScore (about ±3) and Width (a ratio). On BTC at 60,000–86,000
    /// the span is 26,000, so 0.5 sits 2.31 spans below the low — INSIDE the three-span
    /// allowance the reference levels use. The price axis was dragged to zero and the candles
    /// crushed into the top quarter of the pane. Before the components could expand the range at
    /// all they were merely drawn flat along the bottom, which is ugly and audible but not this.
    /// </para>
    ///
    /// <para>
    /// The span rule cannot express what is wrong, because nothing is wrong with the DISTANCE —
    /// it is that a price is the same order of magnitude as other prices, and 0.5 is not.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(0.5, "PercentB")]
    [InlineData(0.2, "ZScore")]
    [InlineData(0.06, "Width")]
    public void AnIndicatorsNonPriceComponentCannotDragThePriceAxis(double value, string name)
    {
        // BTC, as photographed: candles 60,000–86,000.
        var bars = Enumerable.Range(0, BarCount)
            .Select(i => new Ohlcv(new DateTime(2026, 1, 1).AddHours(i), 73000, 86000, 60000, 73000, 1000))
            .ToList();

        var cfg = new SeriesConfig { Id = "bb", Name = "Bollinger Bands", Pane = "Main", IsVisible = true, Volume = 1f };
        cfg.Components.Add(new ComponentConfig { Name = "UpperBand", IsVisible = true, IsEnabled = true });
        cfg.Components.Add(new ComponentConfig { Name = name, IsVisible = true, IsEnabled = true });
        var buf = new SeriesDataBuffer { SeriesId = "bb" };
        buf.ComponentData["UpperBand"] = Enumerable.Repeat(88000.0, BarCount).ToArray();
        buf.ComponentData[name] = Enumerable.Repeat(value, BarCount).ToArray();

        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(Candles(), new ChartSeries(cfg, buf)),
            ViewportStartIndex = 0,
            ViewportLength = BarCount,
        };

        var (min, max) = new ViewportRangeCalculator().Calculate(state).MainRange;

        Assert.True(min > 50000,
            $"{name} at {value} dragged the price floor to {min:F0}. The axis then runs from "
          + "nothing to the high and the candles occupy the top sliver of the pane — which is "
          + "also the top sliver of the PITCH range, so the price line loses most of its swing.");
        // The band above the candles is a price and must still be included.
        Assert.True(max >= 88000, $"the upper band at 88,000 was excluded; the pane tops out at {max:F0}");
    }

    /// <summary>
    /// <b>The level that actually broke Cody's chart, found in his saved workspace.</b>
    ///
    /// <para>
    /// <c>LEVEL on Candles: value=0.0 name='Zero'</c> — presumably from pressing <c>0</c> on the
    /// price pane, where zero is not a meaningful neutral. On BTC at 60,000–86,000 it sits 2.31
    /// spans below the low, inside the three-span allowance reference levels have always had, so
    /// it dragged the axis to nothing. Two wrong guesses preceded finding it: the Bollinger
    /// components (real, fixed, and not this) and a stale binary (it was not that either).
    /// </para>
    /// </summary>
    [Fact]
    public void AZeroLevelOnThePriceSeriesCannotDragTheAxis()
    {
        var bars = Enumerable.Range(0, BarCount)
            .Select(i => new Ohlcv(new DateTime(2026, 1, 1).AddHours(i), 73000, 86000, 60000, 73000, 1000))
            .ToList();

        var cfg = new SeriesConfig { Id = "candles", Name = "Candles", Pane = "Main", IsVisible = true, Volume = 1f };
        cfg.Levels.Add(new LevelConfig { Name = "Zero", Value = 0.0, IsVisible = true });

        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = "candles" })),
            ViewportStartIndex = 0,
            ViewportLength = BarCount,
        };

        var (min, _) = new ViewportRangeCalculator().Calculate(state).MainRange;

        Assert.True(min > 50000,
            $"a 'Zero' level pulled the price floor to {min:F0}, so the candles occupy the top "
          + "sliver of the pane — and of the pitch range with it");
    }

    /// <summary>A support level just under the candles is a price and must still widen the pane;
    /// the clause must not throw out the levels it exists to admit.</summary>
    [Fact]
    public void ASupportLevelNearThePriceStillWidensThePane()
    {
        var bars = Enumerable.Range(0, BarCount)
            .Select(i => new Ohlcv(new DateTime(2026, 1, 1).AddHours(i), 73000, 86000, 60000, 73000, 1000))
            .ToList();

        var cfg = new SeriesConfig { Id = "candles", Name = "Candles", Pane = "Main", IsVisible = true, Volume = 1f };
        cfg.Levels.Add(new LevelConfig { Name = "Support", Value = 52000, IsVisible = true });

        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = "candles" })),
            ViewportStartIndex = 0,
            ViewportLength = BarCount,
        };

        var (min, _) = new ViewportRangeCalculator().Calculate(state).MainRange;
        Assert.True(min <= 52000, $"the support level at 52,000 was excluded; the pane bottoms at {min:F0}");
    }

    /// <summary>
    /// The magnitude clause only ever TIGHTENS. A pane straddling zero has no meaningful
    /// magnitude to compare against, so it must fall back to the span rule rather than rejecting
    /// everything — an oscillator that found its way onto Main would otherwise lose its own data.
    /// </summary>
    [Fact]
    public void APaneStraddlingZeroFallsBackToTheSpanRule()
    {
        Assert.True(ViewportRangeCalculator.IsPlausiblyTheSameQuantity(
            value: -40, dataMin: -50, dataMax: 50, dataSpan: 100));
        Assert.True(ViewportRangeCalculator.IsPlausiblyTheSameQuantity(
            value: 120, dataMin: -50, dataMax: 50, dataSpan: 100));
    }

    /// <summary>A genuine band outside the candles is still a price and still included — the
    /// clause must not undo the change it is correcting.</summary>
    [Theory]
    [InlineData(95000.0)]
    [InlineData(55000.0)]
    public void ABandTenPercentOutsideTheCandlesIsStillAPrice(double bandValue)
    {
        Assert.True(ViewportRangeCalculator.IsPlausiblyTheSameQuantity(
            bandValue, dataMin: 60000, dataMax: 86000, dataSpan: 26000));
    }

    // ── The toggle reaches the numbers ───────────────────────────────────────────

    /// <summary>
    /// <b>The bug this one exists for, and it is the reason the toggle needed a line in the
    /// store rather than only a reducer case.</b> <c>WorkspaceStore</c> recomputes
    /// <c>ViewportRange</c> only when the data, the viewport or the series list changes.
    /// <c>IsLogScale</c> rightly is not in that list — it changes how the range is MAPPED, not
    /// what it is — but <c>ScalePriceOnly</c> changes the numbers themselves, so a toggle that
    /// is missing from the gate does nothing visible or audible until the next tick happens to
    /// recompute, which on a closed market is never.
    /// </summary>
    [Fact]
    public void PressingTheToggle_RecomputesTheRangeImmediately()
    {
        var store = new WorkspaceStore(
            new SpyEventBus(), new ViewportRangeCalculator(),
            new ViewportNavigationService(), new VolumeStateService());

        store.Dispatch(new UpdateDataAction(new TimeSeriesBuffer<Ohlcv>(Bars()), IsInitialLoad: true));
        store.Dispatch(new AddSeriesAction(Band(upper: 110, lower: 90)));

        var included = store.State.ViewportRange;
        Assert.True(included.Max >= 110, "precondition: the band is in the pane before the toggle");

        store.Dispatch(new ToggleScalePriceOnlyAction());

        Assert.True(store.State.ScalePriceOnly);
        Assert.True(store.State.ViewportRange.Max < 110,
            "the range did not recompute when ScalePriceOnly flipped — check WorkspaceStore's recompute gate");

        store.Dispatch(new ToggleScalePriceOnlyAction());
        Assert.Equal(included.Max, store.State.ViewportRange.Max, 9);
    }
}

/// <summary>
/// <b>A fix to the writer does nothing for the data the old writer produced.</b>
///
/// <para>
/// The <c>0</c> key used to add a level at literal zero on whatever series held focus, because it
/// was written for oscillators. On the price series that left
/// <c>{"Name":"Zero","Value":0.0}</c> on CANDLES, and levels persist — so the chart came back with
/// its y-axis running from the origin at every launch. <b>The key was fixed on 2026-09-06</b>
/// (<c>ReferenceLevelPlacement</c>: on a price pane the level goes at the cursor price, because a
/// price pane has no meaningful constant). What was never done is clearing the ones already
/// saved, and Cody's workspace still carried one three weeks later — at the cost of two wrong
/// diagnoses before anyone read the file.
/// </para>
/// </summary>
public sealed class StaleZeroLevelHealingTests
{
    private static SeriesConfig PriceSeriesWith(params (string Name, double Value)[] levels)
    {
        var cfg = new SeriesConfig { Id = "candles", Name = "Candles", IndicatorCode = "CANDLES", Pane = "Main" };
        foreach (var (n, v) in levels) cfg.Levels.Add(new LevelConfig { Name = n, Value = v, IsVisible = true });
        return cfg;
    }

    [Fact]
    public void AZeroLevelOnThePriceSeriesIsDroppedOnRestore()
    {
        var cfg = PriceSeriesWith(("Zero", 0.0));

        WorkspaceInitializer.MigrateSeriesConfig(cfg, new List<IndicatorMetadata>());

        Assert.Empty(cfg.Levels);
    }

    /// <summary>A support line is exactly what the feature is for and must survive.</summary>
    [Fact]
    public void ARealPriceLevelSurvivesRestore()
    {
        var cfg = PriceSeriesWith(("Support", 52000), ("Zero", 0.0), ("Resistance", 91000));

        WorkspaceInitializer.MigrateSeriesConfig(cfg, new List<IndicatorMetadata>());

        Assert.Equal(new[] { "Support", "Resistance" }, cfg.Levels.Select(l => l.Name));
    }

    /// <summary>
    /// An oscillator's zero is meaningful — MACD crosses it, Cipher B swings about it — so the
    /// healing must be scoped to price panes and nowhere else.
    /// </summary>
    [Fact]
    public void AZeroLevelOnAnOscillatorPaneIsLeftAlone()
    {
        var cfg = new SeriesConfig { Id = "macd", Name = "MACD", IndicatorCode = "MACD", Pane = "Pane_MACD" };
        cfg.Levels.Add(new LevelConfig { Name = "Zero", Value = 0.0, IsVisible = true });

        WorkspaceInitializer.MigrateSeriesConfig(cfg, new List<IndicatorMetadata>());

        Assert.Single(cfg.Levels);
    }
}
