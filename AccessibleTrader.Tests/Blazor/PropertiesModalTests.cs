// PropertiesModal — bUnit coverage. Per-indicator settings modal that opens
// via OpenPropertiesEvent(seriesId). Reads ActiveSeries from WorkspaceStore;
// does nothing if the supplied series id can't be resolved.

using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;
using Bunit;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public class PropertiesModalTests
{
    /// <summary>Build a minimal but valid ChartSeries the modal can clone +
    /// render. Default ChartSeries() seeds Config + Data so this is a
    /// one-liner; we only override FriendlyName so test assertions can match
    /// on the title.</summary>
    private static ChartSeries NewSeries(string id, string friendlyName)
    {
        var config = new SeriesConfig
        {
            Id = id,
            Name = friendlyName,
            FriendlyName = friendlyName,
        };
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = id });
    }

    private static void SeedActiveSeries(BlazorTestHarness h, params ChartSeries[] series)
    {
        var state = WorkspaceState.Initial with
        {
            ActiveSeries = series.ToImmutableList(),
            FocusedSeriesId = series.Length > 0 ? series[0].Id : null,
        };
        h.WorkspaceStore.State.Returns(_ => state);
    }

    private static IRenderedComponent<AccessibleTrader.BlazorClient.Components.PropertiesModal>
        OpenProperties(BlazorTestHarness h, string? overrideId = null) =>
        h.OpenModal<AccessibleTrader.BlazorClient.Components.PropertiesModal>(
            bus => bus.Publish(new OpenPropertiesEvent(overrideId)));

    /// <summary>The component always emits a top-level &lt;style&gt; block; presence
    /// of the dialog itself is the closed/open marker.</summary>
    private static bool DialogRendered(IRenderedComponent<AccessibleTrader.BlazorClient.Components.PropertiesModal> cut)
        => cut.FindAll("[role='dialog']").Count > 0;

    [Fact]
    public void PropertiesModal_HiddenByDefault_NoDialog()
    {
        using var h = new BlazorTestHarness();

        var cut = h.Ctx.RenderComponent<AccessibleTrader.BlazorClient.Components.PropertiesModal>();

        Assert.False(DialogRendered(cut));
    }

    [Fact]
    public void PropertiesModal_OpenWithoutFocusedSeries_RemainsHidden()
    {
        using var h = new BlazorTestHarness();
        // Default WorkspaceStore has WorkspaceState.Initial → no ActiveSeries,
        // no FocusedSeriesId. Guard: ShowAsync must early-return.

        var cut = OpenProperties(h);

        Assert.False(DialogRendered(cut));
    }

    [Fact]
    public void PropertiesModal_OpenWithMissingSeriesId_RemainsHidden()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, NewSeries("series-A", "Series A"));

        // Open with an id that doesn't match any active series.
        var cut = OpenProperties(h, overrideId: "does-not-exist");

        Assert.False(DialogRendered(cut));
    }

    [Fact]
    public void PropertiesModal_OpenWithValidSeries_RendersDialog()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, NewSeries("rsi-1", "RSI 14"));

        var cut = OpenProperties(h);

        var dialog = cut.Find("[role='dialog']");
        Assert.Equal("props-title", dialog.GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void PropertiesModal_Title_IncludesSeriesFriendlyName()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, NewSeries("rsi-1", "RSI 14"));

        var cut = OpenProperties(h);

        var title = cut.Find("h2#props-title");
        Assert.Contains("RSI 14", title.TextContent);
    }

    [Fact]
    public void PropertiesModal_DefaultActiveTab_IsGeneral()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, NewSeries("rsi-1", "RSI 14"));

        var cut = OpenProperties(h);

        Assert.Equal("true", cut.Find("button#props-tab-general").GetAttribute("aria-selected"));
    }

    [Fact]
    public void PropertiesModal_ClickAppearanceTab_SwitchesActive()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, NewSeries("rsi-1", "RSI 14"));

        var cut = OpenProperties(h);
        cut.Find("button#props-tab-appearance").Click();

        Assert.Equal("true",  cut.Find("button#props-tab-appearance").GetAttribute("aria-selected"));
        Assert.Equal("false", cut.Find("button#props-tab-general").GetAttribute("aria-selected"));
    }

    [Fact]
    public void PropertiesModal_ClickSonificationTab_SwitchesActive()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, NewSeries("rsi-1", "RSI 14"));

        var cut = OpenProperties(h);
        cut.Find("button#props-tab-sonification").Click();

        Assert.Equal("true", cut.Find("button#props-tab-sonification").GetAttribute("aria-selected"));
    }

    [Fact]
    public void PropertiesModal_ClickSpeechTab_SwitchesActive()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, NewSeries("rsi-1", "RSI 14"));

        var cut = OpenProperties(h);
        cut.Find("button#props-tab-speech").Click();

        Assert.Equal("true", cut.Find("button#props-tab-speech").GetAttribute("aria-selected"));
    }

    [Fact]
    public void PropertiesModal_FourTabs_AllPresent()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, NewSeries("rsi-1", "RSI 14"));

        var cut = OpenProperties(h);

        Assert.NotNull(cut.Find("button#props-tab-general"));
        Assert.NotNull(cut.Find("button#props-tab-appearance"));
        Assert.NotNull(cut.Find("button#props-tab-sonification"));
        Assert.NotNull(cut.Find("button#props-tab-speech"));
    }

    [Fact]
    public void PropertiesModal_ApplyButton_PresentWhenOpen()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, NewSeries("rsi-1", "RSI 14"));

        var cut = OpenProperties(h);

        var apply = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Apply Changes"));
        Assert.NotNull(apply);
    }

    [Fact]
    public void PropertiesModal_CancelButton_HidesModal()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, NewSeries("rsi-1", "RSI 14"));

        var cut = OpenProperties(h);
        var cancel = cut.FindAll("button").First(b => b.TextContent.Trim() == "Cancel");

        cancel.Click();

        Assert.False(DialogRendered(cut));
    }
}
