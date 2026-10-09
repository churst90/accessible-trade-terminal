using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Ctrl+Left/Right on the price stops at horizontal and vertical lines too.</b>
///
/// <para>
/// Cody, 2026-10-09: <i>"crosses should be found with ctrl left/right arrows too."</i> On the
/// candles and the price line the key only ever looked at TREND lines; a horizontal line — the
/// drawing whose whole purpose is "tell me when price gets here" — was invisible to it, and the
/// key said the series had no crossings while one sat on the chart.
/// </para>
///
/// <para>
/// The stops are merged: trend-line crossings, horizontal-line crossings (by the same rule the
/// crossing earcon uses, so the key lands exactly where the chirp sounds) and vertical lines' bars,
/// nearest first in the direction of travel. Hidden drawings are not stops.
/// </para>
/// </summary>
public sealed class LineDrawingCrossJumpTests
{
    private static readonly DateTime T0 = new(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static TimeSeriesBuffer<Ohlcv> Closes(params double[] closes) =>
        new(closes.Select((c, i) => new Ohlcv(T0.AddDays(i), c, c, c, c, 1000)).ToList());

    private static ChartSeries Candles()
    {
        var config = new SeriesConfig
        {
            Id = CoreSeriesIds.Candles, IndicatorCode = "CANDLES", Name = "Candles", FriendlyName = "Candles", Pane = "Main",
        };
        config.Components.Add(new ComponentConfig
        {
            Name = "body", DisplayName = "Body", DisplayType = ComponentDisplayType.Candle, Role = ComponentRole.Body, IsVisible = true,
        });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = config.Id });
    }

    private static ChartSeries Horizontal(double price, string id = "hl") => LineDrawingCrossingEarconTests.Horizontal(price, id);
    private static ChartSeries Vertical(int bar, string id = "vl") => LineDrawingCrossingEarconTests.Vertical(T0.AddDays(bar), id);

    private static ChartSeries TrendLine(int bar1, double price1, int bar2, double price2, string id = "tl")
    {
        var config = new SeriesConfig { Id = id, Name = "TrendLine", FriendlyName = "Trend line", Pane = "Main" };
        config.Components.Add(new ComponentConfig { Name = "Line", DisplayType = ComponentDisplayType.Line, IsVisible = true });
        var s = new ChartSeries(config, new SeriesDataBuffer { SeriesId = id });
        s.Drawing = new DrawingData
        {
            Type = DrawingType.TrendLine,
            AnchorDate1 = T0.AddDays(bar1), AnchorPrice1 = price1,
            AnchorDate2 = T0.AddDays(bar2), AnchorPrice2 = price2,
        };
        return s;
    }

    private static (IndicatorCrossingEngine Engine, WorkspaceStore Store, List<FeedbackRequestEvent> Spoken) Build(
        TimeSeriesBuffer<Ohlcv> data, int cursor, string focusedId, params ChartSeries[] series)
    {
        var bus = new EventBus();
        var store = new WorkspaceStore(bus, new MockViewportRangeCalculator(),
            new MockViewportNavigationService(), new MockVolumeStateService());
        store.Dispatch(new UpdateSettingsAction(st => st with
        {
            Data = data,
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = focusedId,
            FocusedComponentIndex = 0,
            CurrentDataIndex = cursor,
        }));
        var spoken = new List<FeedbackRequestEvent>();
        bus.Subscribe<FeedbackRequestEvent>(spoken.Add);
        return (new IndicatorCrossingEngine(store, bus), store, spoken);
    }

    // ── Horizontal lines ─────────────────────────────────────────────────────

    [Fact]
    public void OnTheCandles_CtrlRight_StopsWherePriceCrossesAHorizontalLine_AndSaysWhichWay()
    {
        //                     0   1   2    3    4   5
        var data = Closes(90, 95, 99, 105, 110, 95);
        var (engine, store, spoken) = Build(data, 0, CoreSeriesIds.Candles, Candles(), Horizontal(100));

        engine.HandleCrossJump(SystemCommand.NavRightJump);

        Assert.Equal(3, store.State.CurrentDataIndex);
        var f = Assert.Single(spoken);
        Assert.Equal(FeedbackType.Info, f.Type);
        Assert.Contains("above", f.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("horizontal line", f.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("100", f.Message);

        engine.HandleCrossJump(SystemCommand.NavRightJump);

        Assert.Equal(5, store.State.CurrentDataIndex);
        Assert.Contains("below", spoken[^1].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnTheCandles_CtrlLeft_FindsTheNearestCrossingBehind()
    {
        var data = Closes(90, 105, 95, 105, 106, 107);
        var (engine, store, _) = Build(data, 5, CoreSeriesIds.Candles, Candles(), Horizontal(100));

        engine.HandleCrossJump(SystemCommand.NavLeftJump);

        Assert.Equal(3, store.State.CurrentDataIndex);
    }

    /// <summary>The price line is a price series too.</summary>
    [Fact]
    public void OnThePriceLine_TheSameStopsApply()
    {
        var config = new SeriesConfig { Id = CoreSeriesIds.Price, Name = "Price", FriendlyName = "Price line", Pane = "Main" };
        config.Components.Add(new ComponentConfig
        {
            Name = "close", DisplayName = "Close", DisplayType = ComponentDisplayType.Line, Role = ComponentRole.PriceAction, IsVisible = true,
        });
        var price = new ChartSeries(config, new SeriesDataBuffer { SeriesId = config.Id });
        var data = Closes(90, 95, 105);
        var (engine, store, _) = Build(data, 0, CoreSeriesIds.Price, price, Horizontal(100));

        engine.HandleCrossJump(SystemCommand.NavRightJump);

        Assert.Equal(2, store.State.CurrentDataIndex);
    }

    [Fact]
    public void AHiddenHorizontalLine_IsNotAStop()
    {
        var data = Closes(90, 95, 105);
        var hl = Horizontal(100);
        hl.IsVisible = false;
        var (engine, store, spoken) = Build(data, 0, CoreSeriesIds.Candles, Candles(), hl);

        engine.HandleCrossJump(SystemCommand.NavRightJump);

        Assert.Equal(0, store.State.CurrentDataIndex);
        // With nothing visible to cross, the key says what it says on a bare chart.
        Assert.Equal(IndicatorCrossingEngine.NoTrendlinesMessage(Candles()), Assert.Single(spoken).Message);
    }

    // ── Vertical lines ───────────────────────────────────────────────────────

    [Fact]
    public void OnTheCandles_CtrlRightAndLeft_StopAtAVerticalLinesBar()
    {
        var data = Closes(100, 100, 100, 100, 100, 100);
        var (engine, store, spoken) = Build(data, 0, CoreSeriesIds.Candles, Candles(), Vertical(3));

        engine.HandleCrossJump(SystemCommand.NavRightJump);
        Assert.Equal(3, store.State.CurrentDataIndex);
        Assert.Contains("vertical line", Assert.Single(spoken).Message, StringComparison.OrdinalIgnoreCase);

        // Standing on it, the next stop to the right is not the same bar again.
        engine.HandleCrossJump(SystemCommand.NavRightJump);
        Assert.Equal(3, store.State.CurrentDataIndex);

        store.Dispatch(new NavigateAction(5));
        engine.HandleCrossJump(SystemCommand.NavLeftJump);
        Assert.Equal(3, store.State.CurrentDataIndex);
    }

    [Fact]
    public void AHiddenVerticalLine_IsNotAStop()
    {
        var data = Closes(100, 100, 100, 100);
        var vl = Vertical(2);
        vl.IsVisible = false;
        var (engine, store, _) = Build(data, 0, CoreSeriesIds.Candles, Candles(), vl);

        engine.HandleCrossJump(SystemCommand.NavRightJump);

        Assert.Equal(0, store.State.CurrentDataIndex);
    }

    // ── Merged with trend lines: nearest wins ────────────────────────────────

    [Fact]
    public void TrendLineHorizontalAndVerticalStops_AreMerged_NearestFirst()
    {
        // Price is flat at 100 except a spike to 120 at bar 6. The vertical sits on bar 2. The
        // horizontal at 110 is crossed at bars 6 (up) and 7 (down). The trend line rises 2 per bar
        // from 90, so the close stays at-or-above it through bar 6 and drops below it at bar 7.
        // Stops, left to right: 2 (vertical), 6 (horizontal), 7 (horizontal and trend line).
        var data = Closes(100, 100, 100, 100, 100, 100, 120, 100, 100, 100);
        var tl = TrendLine(0, 90, 9, 108);   // 90, 92, 94, 96, 98, 100, 102, 104, 106, 108
        var (engine, store, spoken) = Build(data, 0, CoreSeriesIds.Candles,
            Candles(), tl, Horizontal(110), Vertical(2));

        engine.HandleCrossJump(SystemCommand.NavRightJump);
        Assert.Equal(2, store.State.CurrentDataIndex);                       // the vertical line
        Assert.Contains("vertical line", spoken[^1].Message, StringComparison.OrdinalIgnoreCase);

        engine.HandleCrossJump(SystemCommand.NavRightJump);
        Assert.Equal(6, store.State.CurrentDataIndex);                       // 100 → 120: horizontal (and trend line)
        Assert.Contains("horizontal line", spoken[^1].Message, StringComparison.OrdinalIgnoreCase);

        engine.HandleCrossJump(SystemCommand.NavRightJump);
        Assert.Equal(7, store.State.CurrentDataIndex);                       // 120 → 100: horizontal + trend line
    }

    [Fact]
    public void AHiddenTrendLine_IsNotAStopEither()
    {
        var data = Closes(100, 100, 100, 100);
        var tl = TrendLine(0, 98, 3, 104);   // 98, 100, 102, 104 — the close of 100 crosses it at bar 1
        tl.IsVisible = false;
        var (engine, store, _) = Build(data, 0, CoreSeriesIds.Candles, Candles(), tl);

        engine.HandleCrossJump(SystemCommand.NavRightJump);

        Assert.Equal(0, store.State.CurrentDataIndex);
    }

    // ── A horizontal line in focus walks its own crossings ───────────────────

    /// <summary>With the line itself focused, the key used to say "No points of interest on Line"
    /// — the drawing whose only point of interest is where price crosses it. It now walks price
    /// across THAT line, as a focused trend line already does, and ignores the others.</summary>
    [Fact]
    public void WithAHorizontalLineFocused_CtrlRight_WalksPriceAcrossThatLineOnly()
    {
        var data = Closes(90, 115, 105, 95);
        var (engine, store, spoken) = Build(data, 0, "hl", Candles(), Horizontal(100), Horizontal(110, id: "other"));

        engine.HandleCrossJump(SystemCommand.NavRightJump);

        Assert.Equal(1, store.State.CurrentDataIndex);
        Assert.Contains("above", spoken[^1].Message, StringComparison.OrdinalIgnoreCase);

        engine.HandleCrossJump(SystemCommand.NavRightJump);

        Assert.Equal(3, store.State.CurrentDataIndex);  // bar 2 crosses the OTHER line, not this one
        Assert.Contains("below", spoken[^1].Message, StringComparison.OrdinalIgnoreCase);
    }
}
