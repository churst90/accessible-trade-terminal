using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// What Ctrl+Left/Right says when the cursor is on the candles and there is nothing to cross.
///
/// Cody, 2026-09-05: "When on say the candle for example and I press ctrl left/right, it says
/// 'no trendline crossings'. This seems to report something appropriate everywhere else, but the
/// generic line should be 'series does not have crosses' or something like that." The old
/// sentence named a feature (trend lines) instead of the thing under the cursor, and gave no hint
/// of what the key is for on a price series. Every other route on this key already speaks about
/// its own series — "No more Buy signals in this direction" — so this one does too.
/// </summary>
public class CrossingMessageTests
{
    [Fact]
    public void OnTheCandles_WithNoTrendLine_TheKeyNamesTheSeriesAndSaysHowToGiveItACrossing()
    {
        var (engine, bus, store) = Build();
        var candles = Candles();
        store.Dispatch(new UpdateSettingsAction(st => st with
        {
            Data = Bars(5),
            ActiveSeries = ImmutableList.Create(candles),
            FocusedSeriesId = candles.Id,
            FocusedComponentIndex = 0,
            CurrentDataIndex = 2,
        }));
        var spoken = new List<FeedbackRequestEvent>();
        bus.Subscribe<FeedbackRequestEvent>(spoken.Add);

        engine.HandleCrossJump(SystemCommand.NavLeftJump);

        var f = Assert.Single(spoken);
        Assert.Equal("Candles has no crossings to jump to. Draw a line on the chart and this key finds where price crosses it.", f.Message);
        Assert.DoesNotContain("trendlines found", f.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheSentence_LeadsWithTheSeriesName()
    {
        // The property, not the spelling: whatever the wording, the user hears WHICH series has
        // nothing to cross before anything else.
        var msg = IndicatorCrossingEngine.NoTrendlinesMessage(Candles());
        Assert.StartsWith("Candles ", msg);
    }

    /// <summary>
    /// A component with a value on every bar whose news is on a few (Cipher C's timing window,
    /// a day count) is stepped by Ctrl+Left/Right through its <see cref="ComponentCompanions.Stops"/>
    /// companion — the window opening and closing — not through crossings of its count.
    /// </summary>
    [Fact]
    public void A_component_with_a_stops_companion_jumps_to_its_stops_and_says_when_there_are_none_left()
    {
        var (engine, bus, store) = Build();
        var config = new SeriesConfig { Id = "cc", IndicatorCode = "CIPHER_C", Name = "Cipher C", FriendlyName = "Cipher C", Pane = "Main" };
        config.Components.Add(new ComponentConfig
        {
            Name = "Daily Timing Window", DisplayName = "Daily Timing Window", DisplayType = ComponentDisplayType.Line,
            Role = ComponentRole.Signal, IsVisible = true,
        });
        var series = new ChartSeries(config, new SeriesDataBuffer { SeriesId = "cc" });
        series.Data.ComponentData["Daily Timing Window"] = Enumerable.Range(1, 12).Select(i => (double)i).ToArray();
        var stops = Enumerable.Repeat(double.NaN, 12).ToArray();
        stops[4] = 1; stops[9] = 0;
        series.Data.ComponentData["Daily Timing Window" + ComponentCompanions.Stops] = stops;
        store.Dispatch(new UpdateSettingsAction(st => st with
        {
            Data = Bars(12),
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = series.Id,
            FocusedComponentIndex = 0,
            CurrentDataIndex = 0,
        }));
        var spoken = new List<FeedbackRequestEvent>();
        bus.Subscribe<FeedbackRequestEvent>(spoken.Add);

        engine.HandleCrossJump(SystemCommand.NavRightJump);
        Assert.Equal(4, store.State.CurrentDataIndex);
        engine.HandleCrossJump(SystemCommand.NavRightJump);
        Assert.Equal(9, store.State.CurrentDataIndex);
        engine.HandleCrossJump(SystemCommand.NavRightJump);
        Assert.Equal(9, store.State.CurrentDataIndex);
        Assert.Equal("No more Daily Timing Window changes in this direction", spoken[^1].Message);
        engine.HandleCrossJump(SystemCommand.NavLeftJump);
        Assert.Equal(4, store.State.CurrentDataIndex);
    }

    /// <summary>
    /// A <see cref="ComponentDisplayType.Window"/> with no stops companion is stepped through the
    /// bars it opens and closes on — the same spans it is shaded on (<see cref="WindowSpans"/>):
    /// open on bars 3–6 and 9–10 gives stops at 3, 7 (the first bar closed), 9 and 11.
    /// </summary>
    [Fact]
    public void A_window_with_no_stops_companion_jumps_to_where_it_opens_and_closes()
    {
        var (engine, bus, store) = Build();
        var config = new SeriesConfig { Id = "w", IndicatorCode = "WIN", Name = "Win", FriendlyName = "Win", Pane = "Main" };
        config.Components.Add(new ComponentConfig
        {
            Name = "Season", DisplayName = "Season", DisplayType = ComponentDisplayType.Window,
            Role = ComponentRole.Signal, IsVisible = true,
        });
        var series = new ChartSeries(config, new SeriesDataBuffer { SeriesId = "w" });
        series.Data.ComponentData["Season"] = Enumerable.Range(0, 14)
            .Select(i => i is >= 3 and <= 6 or >= 9 and <= 10 ? (double)i : double.NaN).ToArray();
        store.Dispatch(new UpdateSettingsAction(st => st with
        {
            Data = Bars(14),
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = series.Id,
            FocusedComponentIndex = 0,
            CurrentDataIndex = 0,
        }));
        var spoken = new List<FeedbackRequestEvent>();
        bus.Subscribe<FeedbackRequestEvent>(spoken.Add);

        var landed = new List<int>();
        for (int k = 0; k < 5; k++)
        {
            engine.HandleCrossJump(SystemCommand.NavRightJump);
            landed.Add(store.State.CurrentDataIndex);
        }
        Assert.Equal(new[] { 3, 7, 9, 11, 11 }, landed);
        Assert.Equal("No more Season changes in this direction", spoken[^1].Message);
        engine.HandleCrossJump(SystemCommand.NavLeftJump);
        Assert.Equal(9, store.State.CurrentDataIndex);
    }

    /// <summary>
    /// A marker's bars that carry only a <see cref="ComponentCompanions.Mark"/> (Cipher C's held
    /// and cancelled daily lows, drawn with no low value) are Ctrl+Left/Right stops as well as
    /// its lit bars: lows on 2 and 8, marks on 5 and 10, stops 2, 5, 8, 10.
    /// </summary>
    [Fact]
    public void A_markers_mark_only_bars_are_stops_too()
    {
        var (engine, bus, store) = Build();
        var config = new SeriesConfig { Id = "cc", IndicatorCode = "CIPHER_C", Name = "Cipher C", FriendlyName = "Cipher C", Pane = "Main" };
        config.Components.Add(new ComponentConfig
        {
            Name = "Daily Cycle Low", DisplayName = "Daily Cycle Low", DisplayType = ComponentDisplayType.TriangleUpRing,
            Role = ComponentRole.Signal, IsVisible = true,
        });
        var series = new ChartSeries(config, new SeriesDataBuffer { SeriesId = "cc" });
        var lows = Enumerable.Repeat(double.NaN, 12).ToArray();
        lows[2] = 90; lows[8] = 95;
        var marks = Enumerable.Repeat(double.NaN, 12).ToArray();
        marks[5] = 92; marks[10] = 97;
        series.Data.ComponentData["Daily Cycle Low"] = lows;
        series.Data.ComponentData["Daily Cycle Low" + ComponentCompanions.Mark] = marks;
        store.Dispatch(new UpdateSettingsAction(st => st with
        {
            Data = Bars(12),
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = series.Id,
            FocusedComponentIndex = 0,
            CurrentDataIndex = 0,
        }));
        var landed = new List<int>();
        for (int k = 0; k < 5; k++)
        {
            engine.HandleCrossJump(SystemCommand.NavRightJump);
            landed.Add(store.State.CurrentDataIndex);
        }
        Assert.Equal(new[] { 2, 5, 8, 10, 10 }, landed);
        engine.HandleCrossJump(SystemCommand.NavLeftJump);
        Assert.Equal(8, store.State.CurrentDataIndex);
        Assert.True(double.IsNaN(series.Data.ComponentData["Daily Cycle Low"][5]), "the jump must not write the marks into the lows");
    }

    private static ChartSeries Candles()
    {
        var config = new SeriesConfig
        {
            Id = CoreSeriesIds.Candles, IndicatorCode = "CANDLES",
            Name = "Candles", FriendlyName = "Candles", Pane = "Main",
        };
        config.Components.Add(new ComponentConfig
        {
            Name = "body", DisplayName = "Body", DisplayType = ComponentDisplayType.Candle,
            Role = ComponentRole.Body, IsVisible = true,
        });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = CoreSeriesIds.Candles });
    }

    private static TimeSeriesBuffer<Ohlcv> Bars(int n)
    {
        var start = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        return new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, n)
            .Select(i => new Ohlcv(start.AddDays(i), 100 + i, 101 + i, 99 + i, 100 + i, 1000)));
    }

    private static (IndicatorCrossingEngine engine, EventBus bus, WorkspaceStore store) Build()
    {
        var bus = new EventBus();
        var store = new WorkspaceStore(bus, new MockViewportRangeCalculator(),
            new MockViewportNavigationService(), new MockVolumeStateService());
        return (new IndicatorCrossingEngine(store, bus), bus, store);
    }
}
