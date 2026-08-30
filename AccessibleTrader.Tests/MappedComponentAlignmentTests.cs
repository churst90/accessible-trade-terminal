using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The candle and close-line arrays must describe the bars they are sitting on.</b>
///
/// <para>
/// <c>ViewportReducer.SyncMappedComponentData</c> keeps a per-component array for every core
/// series component that declares a <c>DataMapping</c> — <c>upper_wick</c>→high,
/// <c>body</c>→close, <c>lower_wick</c>→low, the price line's <c>line</c>→close. It decided
/// append-vs-rebuild on array LENGTH alone: one longer than last time meant "a live bar was
/// appended", so it copied the old values back at 0..n-1 and filled in the last slot.
/// </para>
///
/// <para>
/// A scrollback fetch that brings back exactly ONE bar is the same arithmetic. The old values
/// were copied back at 0..n-1 with a new bar now in front of them, so every one of them ended
/// up one index to the LEFT of the bar it was measured from — the close line reporting the NEXT
/// bar's close at every historical bar, which is a one-bar look-ahead, and the wicks with it. It
/// stayed wrong until some later update happened to change the length by more than one.
/// </para>
///
/// <para>
/// This is the same defect <c>SeriesDataBuffer.FirstBarDate</c> was introduced to catch in
/// <c>IndicatorOrchestrator</c>; the reducer never carried the stamp. The tests below drive the
/// real store so the reducer, the range calculator and the navigation clamp all run.
/// </para>
/// </summary>
public sealed class MappedComponentAlignmentTests
{
    private static WorkspaceStore Store()
        => new(new EventBus(), new ViewportRangeCalculator(),
               new ViewportNavigationService(), new VolumeStateService());

    private static ChartSeries Candles()
    {
        var cfg = new SeriesConfig { Id = CoreSeriesIds.Candles, IndicatorCode = "CANDLES", Name = "Candles", Pane = "Main" };
        cfg.Components.Add(new ComponentConfig { Name = "upper_wick", IsVisible = true, DisplayType = ComponentDisplayType.Wick, DataMapping = "high" });
        cfg.Components.Add(new ComponentConfig { Name = "body", IsVisible = true, DisplayType = ComponentDisplayType.Candle, DataMapping = "close" });
        cfg.Components.Add(new ComponentConfig { Name = "lower_wick", IsVisible = true, DisplayType = ComponentDisplayType.Wick, DataMapping = "low" });
        return new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = cfg.Id });
    }

    private static ChartSeries PriceLine()
    {
        var cfg = new SeriesConfig { Id = CoreSeriesIds.Price, IndicatorCode = "PRICE", Name = "Price", Pane = "Main" };
        cfg.Components.Add(new ComponentConfig { Name = "line", IsVisible = true, DisplayType = ComponentDisplayType.Line, DataMapping = "close" });
        return new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = cfg.Id });
    }

    /// <summary>Distinct closes so a one-slot shift cannot hide behind equal values.</summary>
    private static Ohlcv Bar(int day, double close)
        => new(new DateTime(2026, 8, day), close - 20, close + 10, close - 30, close, 100);

    private static TimeSeriesBuffer<Ohlcv> Bars(params Ohlcv[] bars) => new(bars);

    /// <summary>
    /// Every mapped array reads back the value its own bar carries, at every index.
    /// </summary>
    private static void AssertAligned(WorkspaceStore store)
    {
        var data = store.State.Data;
        foreach (var s in store.State.ActiveSeries)
        {
            foreach (var c in s.Components)
            {
                if (string.IsNullOrEmpty(c.DataMapping)) continue;
                var arr = s.GetComponentData(c.Name);
                Assert.Equal(data.Count, arr.Length);

                for (int i = 0; i < data.Count; i++)
                {
                    double expected = c.DataMapping switch
                    {
                        "high"  => data[i].High,
                        "low"   => data[i].Low,
                        "close" => data[i].Close,
                        _       => double.NaN,
                    };
                    Assert.Equal(expected, arr[i]);
                }
            }
        }
    }

    private static WorkspaceStore Loaded()
    {
        var store = Store();
        store.Dispatch(new AddSeriesAction(Candles()));
        store.Dispatch(new AddSeriesAction(PriceLine()));
        store.Dispatch(new UpdateDataAction(
            Bars(Bar(26, 70200), Bar(27, 76800), Bar(28, 78000)), IsInitialLoad: true));
        return store;
    }

    /// <summary>
    /// The headline. One bar of scrollback arrives; nothing may move.
    /// </summary>
    [Fact]
    public void A_one_bar_prepend_does_not_shift_the_mapped_arrays()
    {
        var store = Loaded();
        AssertAligned(store);

        var withHistory = store.State.Data.ToList();
        withHistory.Insert(0, Bar(25, 61000));
        store.Dispatch(new UpdateDataAction(Bars(withHistory.ToArray()), IsInitialLoad: false));

        AssertAligned(store);
    }

    /// <summary>
    /// The specific reading that was wrong, stated as the user would meet it: standing on the
    /// oldest bar, the close line must not be quoting the bar to its right. Written against the
    /// value rather than the whole array so a failure names the symptom.
    /// </summary>
    [Fact]
    public void After_a_one_bar_prepend_the_close_line_is_not_a_bar_ahead()
    {
        var store = Loaded();

        var withHistory = store.State.Data.ToList();
        withHistory.Insert(0, Bar(25, 61000));
        store.Dispatch(new UpdateDataAction(Bars(withHistory.ToArray()), IsInitialLoad: false));

        var line = store.State.ActiveSeries
            .First(s => s.Id == CoreSeriesIds.Price)
            .GetComponentData("line");

        Assert.Equal(61000, line[0]);                          // the bar that just arrived
        Assert.NotEqual(store.State.Data[1].Close, line[0]);   // not the one after it
    }

    /// <summary>
    /// The vacuity check for the pair above: a genuine live APPEND still takes the cheap
    /// incremental path and still lands correctly. Without this, "aligned" could be bought by
    /// rebuilding unconditionally, which is a different bug (a full O(n) rebuild per tick).
    /// </summary>
    [Fact]
    public void A_live_append_stays_aligned()
    {
        var store = Loaded();

        var withNewBar = store.State.Data.ToList();
        withNewBar.Add(Bar(29, 79300));
        store.Dispatch(new UpdateDataAction(Bars(withNewBar.ToArray()), IsInitialLoad: false));

        AssertAligned(store);
        Assert.Equal(79300, store.State.ActiveSeries
            .First(s => s.Id == CoreSeriesIds.Price).GetComponentData("line")[^1]);
    }

    /// <summary>
    /// An intra-bar tick replaces the live bar in place — same count, last slot only. The
    /// cheapest path of the three and the one that runs most often.
    /// </summary>
    [Fact]
    public void An_intra_bar_tick_stays_aligned()
    {
        var store = Loaded();

        var ticked = store.State.Data.ToArray();
        ticked[^1] = ticked[^1] with { Close = 78650, High = 78900 };
        store.Dispatch(new UpdateDataAction(Bars(ticked), IsInitialLoad: false));

        AssertAligned(store);
        Assert.Equal(78650, store.State.ActiveSeries
            .First(s => s.Id == CoreSeriesIds.Price).GetComponentData("line")[^1]);
    }

    /// <summary>
    /// A multi-bar prepend, which the length check already caught before this fix. Kept so the
    /// common scrollback case cannot regress while attention is on the one-bar edge.
    /// </summary>
    [Fact]
    public void A_multi_bar_prepend_stays_aligned()
    {
        var store = Loaded();

        var withHistory = store.State.Data.ToList();
        withHistory.InsertRange(0, new[] { Bar(20, 60000), Bar(21, 60500), Bar(22, 61200) });
        store.Dispatch(new UpdateDataAction(Bars(withHistory.ToArray()), IsInitialLoad: false));

        AssertAligned(store);
    }

    /// <summary>
    /// Prepend and append in one update — the shape a catch-up after a disconnect takes. The
    /// count grows by two, so the length check alone would have called it a rebuild anyway; the
    /// stamp is what makes that a decision rather than a coincidence.
    /// </summary>
    [Fact]
    public void A_prepend_and_an_append_in_one_update_stay_aligned()
    {
        var store = Loaded();

        var both = store.State.Data.ToList();
        both.Insert(0, Bar(25, 61000));
        both.Add(Bar(29, 79300));
        store.Dispatch(new UpdateDataAction(Bars(both.ToArray()), IsInitialLoad: false));

        AssertAligned(store);
    }
}
