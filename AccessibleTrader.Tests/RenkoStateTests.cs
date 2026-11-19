using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Charts;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// The reducers' half of Renko: a brick chart's Data is bricks while TimeBars keeps the real
/// bars, the box freezes, and every switch either side is reversible.
/// </summary>
public sealed class RenkoStateTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static List<Ohlcv> Rising(int count = 40, double step = 10)
        => Enumerable.Range(0, count)
            .Select(i => new Ohlcv(Start.AddHours(i), 100 + i * step, 101 + i * step, 99 + i * step, 100 + i * step, 1000))
            .ToList();

    private static List<Ohlcv> Oscillating(int count = 45)
        => Enumerable.Range(0, count).Select(i =>
        {
            double c = 100 + Math.Sin(i / 2.0) * 8;
            return new Ohlcv(Start.AddHours(i), c, c + 3 + i % 4, c - 3, c, 1000);
        }).ToList();

    private static WorkspaceState State(IReadOnlyList<Ohlcv>? bars = null, RenkoSettings? renko = null) =>
        WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Spot", "Bitstamp", "BTC/USD", "1h"),
            Data = new TimeSeriesBuffer<Ohlcv>(bars ?? Rising()),
            Renko = renko ?? new RenkoSettings(RenkoBoxMode.Fixed, 10),
            CurrentDataIndex = (bars ?? Rising()).Count - 1,
            ViewportLength = 30,
            InitStatus = InitializationStatus.Ready,
        };

    private static WorkspaceState Reduce(WorkspaceState initial, params WorkspaceAction[] actions)
    {
        using var store = new WorkspaceStore(
            new SpyEventBus(), new MockViewportRangeCalculator(),
            new MockViewportNavigationService(), new MockVolumeStateService());
        store.Dispatch(new UpdateSettingsAction(_ => initial));
        foreach (var action in actions) store.Dispatch(action);
        return store.State;
    }

    [Fact]
    public void Choosing_Renko_keeps_the_time_bars_beside_the_bricks_and_puts_the_cursor_on_the_newest_brick()
    {
        var timeBars = new TimeSeriesBuffer<Ohlcv>(Rising());

        var after = Reduce(State(Rising()) with { Data = timeBars },
            new SetChartStyleAction(PriceChartStyle.Renko, false));

        Assert.Equal(PriceChartStyle.Renko, after.ChartStyle);
        Assert.Same(timeBars, after.TimeBars);
        Assert.Same(timeBars, after.MarketBars);
        Assert.NotEmpty(after.Data);
        Assert.Equal(after.Data.Count, after.BrickSpans.Length);
        Assert.Equal(after.Data.Count - 1, after.CurrentDataIndex);
        Assert.All(after.Data, b => Assert.Equal(0, b.Open % 10, 9));
    }

    [Fact]
    public void Leaving_Renko_puts_the_real_bars_back_and_clears_the_brick_fields()
    {
        var timeBars = new TimeSeriesBuffer<Ohlcv>(Rising());
        var renko = Reduce(State(Rising()) with { Data = timeBars },
            new SetChartStyleAction(PriceChartStyle.Renko, false));

        var line = Reduce(renko, new SetChartStyleAction(PriceChartStyle.Line, false));

        Assert.Equal(PriceChartStyle.Line, line.ChartStyle);
        Assert.Null(line.TimeBars);
        Assert.Empty(line.BrickSpans);
        Assert.Same(renko.TimeBars, line.Data);
        Assert.Equal(line.Data.Count - 1, line.CurrentDataIndex);
    }

    [Fact]
    public void Heikin_Ashi_on_from_Renko_restores_the_real_bars_as_candles_plus_HA()
    {
        var renko = Reduce(State(), new SetChartStyleAction(PriceChartStyle.Renko, false));

        var ha = Reduce(renko, new ToggleHeikinAshiAction());

        Assert.Equal(PriceChartStyle.Candles, ha.ChartStyle);
        Assert.True(ha.IsHeikinAshi);
        Assert.Null(ha.TimeBars);
        Assert.Empty(ha.BrickSpans);
        Assert.Same(renko.TimeBars, ha.Data);
    }

    [Fact]
    public void A_live_update_on_Renko_keeps_the_incoming_time_bars_and_rebuilds()
    {
        var renko = Reduce(State(), new SetChartStyleAction(PriceChartStyle.Renko, false));

        var incoming = new TimeSeriesBuffer<Ohlcv>(Rising(41));
        var after = Reduce(renko, new UpdateDataAction(incoming, IsInitialLoad: false));

        Assert.Same(incoming, after.TimeBars);
        Assert.Same(incoming, after.MarketBars);
        Assert.Equal(after.Data.Count, after.BrickSpans.Length);
        Assert.Equal(500, after.Data[^1].Close);
    }

    [Fact]
    public void Choosing_Renko_before_any_data_keeps_the_choice_and_bricks_the_first_load()
    {
        var chosen = Reduce(
            WorkspaceState.Initial with
            {
                Identity = new ChartIdentity("Spot", "Bitstamp", "BTC/USD", "1h"),
                Renko = new RenkoSettings(RenkoBoxMode.Fixed, 10),
            },
            new SetChartStyleAction(PriceChartStyle.Renko, false));

        Assert.Equal(PriceChartStyle.Renko, chosen.ChartStyle);
        Assert.Empty(chosen.Data);
        Assert.Equal(0, chosen.BrickBoxSize);

        var loaded = Reduce(chosen, new UpdateDataAction(new TimeSeriesBuffer<Ohlcv>(Rising()), IsInitialLoad: true));

        Assert.NotEmpty(loaded.Data);
        Assert.Equal(10, loaded.BrickBoxSize, 12);
        Assert.Equal(loaded.Data.Count, loaded.BrickSpans.Length);
        Assert.Equal(Rising().Count, loaded.TimeBars!.Count);
    }

    [Fact]
    public void A_restored_tab_keeps_the_frozen_box_its_ATR_settings_resolved()
    {
        var withSavedBox = Reduce(
            WorkspaceState.Initial with
            {
                Identity = new ChartIdentity("Spot", "Bitstamp", "BTC/USD", "1h"),
            },
            new SetRenkoSettingsAction(new RenkoSettings(RenkoBoxMode.Atr, 0, 14), 37));

        Assert.Equal(37, withSavedBox.BrickBoxSize, 10);
        Assert.Equal(PriceChartStyle.Candles, withSavedBox.ChartStyle);
        Assert.Empty(withSavedBox.Data);
        var chosen = Reduce(withSavedBox, new SetChartStyleAction(PriceChartStyle.Renko, false));
        Assert.Equal(37, chosen.BrickBoxSize, 10);
        Assert.Empty(chosen.Data);

        var loaded = Reduce(chosen, new UpdateDataAction(new TimeSeriesBuffer<Ohlcv>(Rising()), IsInitialLoad: true));

        Assert.Equal(37, loaded.BrickBoxSize, 10);
        Assert.NotEmpty(loaded.Data);
        Assert.Equal(loaded.Data.Count, loaded.BrickSpans.Length);
    }

    [Fact]
    public void A_first_load_while_Renko_resolves_the_ATR_box_and_bricks_the_new_bars()
    {
        var empty = WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Spot", "Bitstamp", "BTC/USD", "1h"),
            ChartStyle = PriceChartStyle.Renko,
            Renko = new RenkoSettings(),
            ViewportLength = 30,
        };

        var bars = Oscillating();
        var after = Reduce(empty, new UpdateDataAction(new TimeSeriesBuffer<Ohlcv>(bars), IsInitialLoad: true));

        double expected = AccessibleTrader.Sdk.Indicators.IndicatorMath.Atr(
            bars.Take(bars.Count - 1).ToArray(), 14)[^1];

        Assert.Equal(expected, after.BrickBoxSize, 10);
        Assert.NotEmpty(after.Data);
        Assert.Equal(after.Data.Count, after.BrickSpans.Length);
        Assert.Same(after.TimeBars, after.MarketBars);
    }

    [Fact]
    public void The_ATR_box_freezes_across_appends_and_prepends_and_moves_only_when_the_settings_do()
    {
        var bars = Oscillating();
        var renko = Reduce(State(bars, new RenkoSettings()) with { Data = new TimeSeriesBuffer<Ohlcv>(bars) },
            new SetChartStyleAction(PriceChartStyle.Renko, false));
        double box = renko.BrickBoxSize;
        Assert.True(box > 0);

        var appended = bars.Concat(new[]
        {
            new Ohlcv(bars[^1].Date.AddHours(1), 90, 140, 80, 135, 5000),
        }).ToList();
        var afterAppend = Reduce(renko, new UpdateDataAction(new TimeSeriesBuffer<Ohlcv>(appended), false));
        Assert.Equal(box, afterAppend.BrickBoxSize, 12);

        var older = Enumerable.Range(0, 10)
            .Select(i => new Ohlcv(Start.AddHours(-10 + i), 100, 101, 99, 100, 1000))
            .Concat(appended)
            .ToList();
        var afterPrepend = Reduce(afterAppend, new UpdateDataAction(new TimeSeriesBuffer<Ohlcv>(older), false));
        Assert.Equal(box, afterPrepend.BrickBoxSize, 12);

        var afterChange = Reduce(afterPrepend, new SetRenkoSettingsAction(new RenkoSettings(RenkoBoxMode.Atr, 0, 20)));
        Assert.NotEqual(box, afterChange.BrickBoxSize);
        Assert.Equal(20, afterChange.Renko.AtrLength);
        Assert.Equal(afterChange.Data.Count, afterChange.BrickSpans.Length);
    }

    [Fact]
    public void A_tab_snapshot_holds_the_time_bars_and_the_switch_back_rebuilds_the_same_bricks()
    {
        var renko = Reduce(State(), new SetChartStyleAction(PriceChartStyle.Renko, false));
        var lastBrick = renko.Data[^1];

        var twoTabs = Reduce(renko, new AddTabAction());
        var snap = twoTabs.TabSnapshots!.Single();
        Assert.Equal(PriceChartStyle.Renko, snap.ChartStyle);
        Assert.Equal(renko.BrickBoxSize, snap.BrickBoxSize, 12);
        Assert.Same(renko.TimeBars, snap.Data);

        var back = Reduce(twoTabs, new SwitchTabAction(0));

        Assert.Equal(PriceChartStyle.Renko, back.ChartStyle);
        Assert.Equal(renko.BrickBoxSize, back.BrickBoxSize, 12);
        Assert.Equal(renko.Renko, back.Renko);
        Assert.Equal(renko.TimeBars!.Count, back.TimeBars!.Count);
        Assert.Equal(renko.Data.Count, back.Data.Count);
        Assert.Equal(lastBrick.Close, back.Data[^1].Close);
        Assert.Equal(renko.Data.Count, back.BrickSpans.Length);
    }
}
