using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Alerts;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Models;
using Bunit;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// OpenAlertsEvent's optional parameters are how the chart menu pre-fills the form. They must
/// select the target/series/component and keep the clicked value from being overwritten by the
/// live-reading prefill.
/// </summary>
public sealed class AlertChartMenuPrefillTests
{
    private static readonly DateTime T0 = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

    private static ChartSeries Rsi()
    {
        var config = new SeriesConfig
        {
            Id = "rsi-1", Name = "RSI", FriendlyName = "RSI 14",
            IndicatorCode = "RSI", Pane = "Pane_Rsi",
        };
        config.Components.Add(new ComponentConfig { Name = "RSI", DisplayName = "RSI" });
        var buffer = new SeriesDataBuffer { SeriesId = "rsi-1" };
        buffer.ComponentData["RSI"] = Enumerable.Repeat(61.25, 10).ToArray();
        return new ChartSeries(config, buffer);
    }

    private static BlazorTestHarness Harness()
    {
        var h = new BlazorTestHarness();
        var bars = Enumerable.Range(0, 10)
            .Select(i => new Ohlcv(T0.AddHours(i), 64_000 + i, 64_300 + i, 63_900 + i, 64_241.5 + i, 10))
            .ToArray();
        h.WorkspaceStore.State.Returns(WorkspaceState.Initial with
        {
            SymbolDisplayName = "BTC/USD",
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            CurrentDataIndex = 9,
            ActiveSeries = ImmutableList.Create(Rsi()),
        });
        return h;
    }

    [Fact]
    public void A_price_alert_request_prefills_the_target_and_the_clicked_value()
    {
        using var h = Harness();
        var cut = h.OpenModal<AccessibleTrader.BlazorClient.Components.AlertsModal>(
            bus => bus.Publish(new OpenAlertsEvent(AlertTarget.Price, Value: 123.45)));

        Assert.Equal("Price", cut.Find("select#alert-target").GetAttribute("value"));
        Assert.Equal(AlertDescriptions.FormatForInput(123.45),
            cut.Find("input#alert-threshold").GetAttribute("value"));
    }

    [Fact]
    public void An_indicator_alert_request_selects_the_series_component_and_value()
    {
        using var h = Harness();
        var cut = h.OpenModal<AccessibleTrader.BlazorClient.Components.AlertsModal>(
            bus => bus.Publish(new OpenAlertsEvent(
                AlertTarget.Indicator, SeriesId: "rsi-1", ComponentName: "RSI", Value: 52.3)));

        Assert.Equal("Indicator", cut.Find("select#alert-target").GetAttribute("value"));
        Assert.Equal("rsi-1", cut.Find("select#alert-indicator").GetAttribute("value"));
        Assert.Equal("RSI", cut.Find("select#alert-component").GetAttribute("value"));
        Assert.Equal(AlertDescriptions.FormatForInput(52.3),
            cut.Find("input#alert-threshold").GetAttribute("value"));
    }
}
