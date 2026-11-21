using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Workspace.Reducers;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// The first view of a built chart. A view sized for a hundred candles held seventeen Renko
/// bricks in its left sixth and nothing to the right of them (the website photographs,
/// 2026-11-21). A chart with fewer bricks than the view holds now opens zoomed to fit them,
/// and the candles come back at the zoom they left at.
/// </summary>
public sealed class RenkoFirstViewTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const int CandleZoom = 100;

    // A slow rise: a box of 10 every ten bars, so 200 bars make about 19 bricks.
    private static TimeSeriesBuffer<Ohlcv> SlowRise(int count = 200) => new(
        Enumerable.Range(0, count)
            .Select(i => new Ohlcv(Start.AddHours(i), 100 + i, 101 + i, 99 + i, 100 + i, 1000))
            .ToList());

    // A fast rise: a box of 10 on every bar, so 200 bars make about 199 bricks.
    private static TimeSeriesBuffer<Ohlcv> FastRise(int count = 200) => new(
        Enumerable.Range(0, count)
            .Select(i => new Ohlcv(Start.AddHours(i), 100 + i * 10, 101 + i * 10, 99 + i * 10, 100 + i * 10, 1000))
            .ToList());

    private static WorkspaceState Candles(TimeSeriesBuffer<Ohlcv> bars) =>
        WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Spot", "Bitstamp", "BTC/USD", "1h"),
            Data = bars,
            Renko = new RenkoSettings(RenkoBoxMode.Fixed, 10),
            CurrentDataIndex = bars.Count - 1,
            ViewportLength = CandleZoom,
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

    /// <summary>Every data slot of the view (the length less the right margin) holds a brick,
    /// and the newest brick is the last one shown.</summary>
    private static void AssertBricksFillTheView(WorkspaceState s)
    {
        Assert.True(s.Data.Count > 10, $"the fixture should make more than ten bricks, made {s.Data.Count}");
        Assert.True(s.Data.Count < CandleZoom - s.RightMarginBars, $"the fixture should make fewer bricks than the candles' view holds, made {s.Data.Count}");
        Assert.Equal(s.Data.Count, s.ViewportLength - s.RightMarginBars);
        Assert.Equal(0, s.ViewportStartIndex);
    }

    [Fact]
    public void Choosing_Renko_with_fewer_bricks_than_the_view_holds_fits_the_view_to_them()
    {
        var renko = Reduce(Candles(SlowRise()), new SetChartStyleAction(PriceChartStyle.Renko, false));

        AssertBricksFillTheView(renko);
    }

    [Fact]
    public void Leaving_Renko_puts_the_candles_back_at_the_zoom_they_left_at()
    {
        var renko = Reduce(Candles(SlowRise()), new SetChartStyleAction(PriceChartStyle.Renko, false));
        Assert.NotEqual(CandleZoom, renko.ViewportLength);

        var candles = Reduce(renko, new SetChartStyleAction(PriceChartStyle.Candles, false));

        Assert.Equal(CandleZoom, candles.ViewportLength);
        Assert.Equal(0, candles.TimeBarsViewportLength);
    }

    [Fact]
    public void Renko_with_more_bricks_than_the_view_holds_keeps_the_candles_zoom()
    {
        var renko = Reduce(Candles(FastRise()), new SetChartStyleAction(PriceChartStyle.Renko, false));

        Assert.True(renko.Data.Count > CandleZoom);
        Assert.Equal(CandleZoom, renko.ViewportLength);
    }

    [Fact]
    public void A_Renko_tab_restored_before_its_data_fits_its_first_bricks_and_leaves_to_the_saved_zoom()
    {
        var chosen = Reduce(Candles(new TimeSeriesBuffer<Ohlcv>()) with { CurrentDataIndex = -1 },
            new SetChartStyleAction(PriceChartStyle.Renko, false));

        var loaded = Reduce(chosen, new UpdateDataAction(SlowRise(), IsInitialLoad: true));

        AssertBricksFillTheView(loaded);
        Assert.Equal(CandleZoom, Reduce(loaded, new SetChartStyleAction(PriceChartStyle.Line, false)).ViewportLength);
    }

    [Fact]
    public void A_Renko_tab_switched_away_from_and_back_still_leaves_to_the_candles_zoom()
    {
        var renko = Reduce(Candles(SlowRise()), new SetChartStyleAction(PriceChartStyle.Renko, false));
        var twoTabs = Reduce(renko, new AddTabAction());

        // The new tab never chose Renko, so it holds no remembered zoom of its own.
        Assert.Equal(0, twoTabs.TimeBarsViewportLength);

        var back = Reduce(twoTabs, new SwitchTabAction(0));
        Assert.Equal(renko.ViewportLength, back.ViewportLength);

        Assert.Equal(CandleZoom, Reduce(back, new SetChartStyleAction(PriceChartStyle.Candles, false)).ViewportLength);
    }

    // DataManager's fresh-chart load dispatches the initial UpdateDataAction and then
    // ZoomAction(100). The first fix passed every test above and still showed the half-empty
    // view in the browser, because that default zoom arrived after the fit and undid it.
    [Fact]
    public void A_fresh_load_on_Renko_followed_by_the_default_zoom_still_fills_the_view()
    {
        var renko = Reduce(Candles(SlowRise()), new SetChartStyleAction(PriceChartStyle.Renko, false));

        var reloaded = Reduce(renko,
            new UpdateDataAction(SlowRise(), IsInitialLoad: true),
            new ZoomAction(CandleZoom));

        AssertBricksFillTheView(reloaded);
        Assert.Equal(CandleZoom, Reduce(reloaded, new SetChartStyleAction(PriceChartStyle.Candles, false)).ViewportLength);
    }

    // WorkspaceInitializer restores a tab's saved zoom with ZoomAction while its data is still
    // empty; the first load follows.
    [Fact]
    public void A_restored_Renko_tab_given_its_saved_zoom_before_its_data_fills_the_view()
    {
        var chosen = Reduce(Candles(new TimeSeriesBuffer<Ohlcv>()) with { CurrentDataIndex = -1, ViewportLength = 40 },
            new SetChartStyleAction(PriceChartStyle.Renko, false),
            new ZoomAction(CandleZoom));

        var loaded = Reduce(chosen, new UpdateDataAction(SlowRise(), IsInitialLoad: true));

        AssertBricksFillTheView(loaded);
        Assert.Equal(CandleZoom, Reduce(loaded, new SetChartStyleAction(PriceChartStyle.Candles, false)).ViewportLength);
    }

    [Fact]
    public void A_set_zoom_on_candles_is_still_just_the_zoom()
    {
        var candles = Reduce(Candles(SlowRise()), new ZoomAction(60));

        Assert.Equal(60, candles.ViewportLength);
        Assert.Equal(0, candles.TimeBarsViewportLength);
    }

    [Fact]
    public void A_saved_Renko_tab_keeps_the_candles_zoom_in_the_file()
    {
        var renko = Reduce(Candles(SlowRise()), new SetChartStyleAction(PriceChartStyle.Renko, false));
        Assert.NotEqual(CandleZoom, renko.ViewportLength);

        string dir = TestTemp.NewDir("att-renko-zoom-");
        try
        {
            var lib = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, new TempWorkspacePaths())
            { LibraryDirectoryOverride = dir };
            var store = new MockWorkspaceStore();
            store.EmitState(renko);
            lib.SaveWorkspaceProfile("renko-zoom", store);

            Assert.Equal(CandleZoom, lib.LoadProfile("renko-zoom")!.Tabs[0].ViewportLength);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    // Twenty-three daily bricks beside the candles' ten-slot margin left a third of the chart
    // empty (the second website shoot, 2026-11-21). A built chart's margin is about a tenth of
    // its bricks, two at the least.
    [Fact]
    public void A_few_bricks_keep_a_narrow_right_margin_so_the_chart_is_mostly_bricks()
    {
        var renko = Reduce(Candles(SlowRise()), new SetChartStyleAction(PriceChartStyle.Renko, false));

        Assert.InRange(renko.Data.Count, 12, 29);
        Assert.Equal(2, renko.RightMarginBars);
        Assert.True(renko.RightMarginBars * 10 <= renko.ViewportLength,
            $"{renko.RightMarginBars} empty slots of {renko.ViewportLength} is more than a tenth");
        Assert.Equal(renko.Data.Count - 1, renko.ViewportStartIndex + renko.ViewportLength - renko.RightMarginBars - 1);
    }

    [Theory]
    [InlineData(5, 2)]
    [InlineData(23, 2)]
    [InlineData(45, 4)]
    [InlineData(100, 10)]
    [InlineData(900, 10)]
    public void The_brick_margin_is_a_tenth_of_the_bricks_between_two_and_ten(int bricks, int margin)
    {
        Assert.Equal(margin, PlaybackReducer.BrickMargin(bricks));
    }

    [Fact]
    public void Leaving_Renko_and_opening_a_tab_from_it_give_the_candles_their_margin_back()
    {
        var renko = Reduce(Candles(SlowRise()), new SetChartStyleAction(PriceChartStyle.Renko, false));
        Assert.NotEqual(PlaybackReducer.TimeBarsMargin, renko.RightMarginBars);

        Assert.Equal(PlaybackReducer.TimeBarsMargin,
            Reduce(renko, new SetChartStyleAction(PriceChartStyle.Candles, false)).RightMarginBars);

        var twoTabs = Reduce(renko, new AddTabAction());
        Assert.Equal(PlaybackReducer.TimeBarsMargin, twoTabs.RightMarginBars);
        Assert.Equal(CandleZoom, twoTabs.ViewportLength);

        // ...and the Renko tab keeps its own when switched back to.
        Assert.Equal(renko.RightMarginBars, Reduce(twoTabs, new SwitchTabAction(0)).RightMarginBars);
    }

    [Fact]
    public void A_restored_Renko_tab_gets_the_narrow_margin_with_its_first_bricks()
    {
        var chosen = Reduce(Candles(new TimeSeriesBuffer<Ohlcv>()) with { CurrentDataIndex = -1 },
            new SetChartStyleAction(PriceChartStyle.Renko, false),
            new ZoomAction(CandleZoom));

        var loaded = Reduce(chosen, new UpdateDataAction(SlowRise(), IsInitialLoad: true), new ZoomAction(CandleZoom));

        Assert.Equal(PlaybackReducer.BrickMargin(loaded.Data.Count), loaded.RightMarginBars);
        Assert.Equal(2, loaded.RightMarginBars);
    }
}
