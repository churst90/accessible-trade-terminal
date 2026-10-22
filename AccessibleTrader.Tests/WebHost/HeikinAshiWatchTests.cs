using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.WebHost.Services;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// A saved tab showing Heikin-Ashi is watched as Heikin-Ashi with the browser closed: its
/// indicators compute on the candles it shows (IndicatorCandles), as the open chart's do, so its
/// alerts and narration say what the tab shows. The flag rides with the tab's series through
/// every way a watch is derived and merged.
/// </summary>
public sealed class HeikinAshiWatchTests
{
    private static SeriesConfig Ema() => new() { Id = "e", Name = "EMA", IndicatorCode = "Ema", Pane = "Main" };

    private static WorkspaceConfiguration Session(bool heikinAshi) => new()
    {
        Tabs = new()
        {
            new TabConfiguration
            {
                Market = "Spot", Provider = "Bitstamp", Symbol = "BTC/USD", Timeframe = "1h",
                IsHeikinAshi = heikinAshi, Series = new() { Ema() },
            },
        },
    };

    private static AlertDefinition PriceAlert() => new()
    {
        Id = Guid.NewGuid().ToString(), Name = "BTC above", Target = AlertTarget.Price,
        Condition = AlertCondition.CrossesAbove, Threshold = 50_000, Delivery = AlertDelivery.Both,
        IsActive = true, Symbol = "BTC/USD", Provider = "Bitstamp", Timeframe = "1h",
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_bar_close_watch_carries_the_tabs_candle_type(bool heikinAshi)
        => Assert.Equal(heikinAshi, Assert.Single(LocalBackgroundMonitor.DeriveBarCloseWatches(Session(heikinAshi))).HeikinAshi);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_alert_watch_carries_the_saved_tabs_candle_type(bool heikinAshi)
        => Assert.Equal(heikinAshi, Assert.Single(LocalBackgroundMonitor.DeriveWatches(new[] { PriceAlert() }, Session(heikinAshi))).HeikinAshi);

    [Fact]
    public void Merging_an_alert_watch_with_the_tabs_watch_keeps_both_the_alerts_and_the_candle_type()
    {
        var alertWatch = new LocalBackgroundMonitor.Watch("Bitstamp", "BTC/USD", "1h", new[] { PriceAlert() });
        var tabWatch = Assert.Single(LocalBackgroundMonitor.DeriveBarCloseWatches(Session(heikinAshi: true)));

        var merged = Assert.Single(LocalBackgroundMonitor.MergeTargets(new[] { alertWatch }, new[] { tabWatch }, new[] { tabWatch }));

        Assert.True(merged.Watch.HeikinAshi);
        Assert.Single(merged.Watch.Alerts);
        Assert.True(merged.WatchBarCloses);
        Assert.True(merged.Narrate);
    }
}
