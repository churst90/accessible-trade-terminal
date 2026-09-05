using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Input;
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
        Assert.Equal("Candles has no crossings to jump to. Draw a trend line and this key finds where price crosses it.", f.Message);
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
