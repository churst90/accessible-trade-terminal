// ChartContextMenu — bUnit coverage. The chart-level right-click menu (Phase B):
// opens via OpenChartContextMenuEvent from DrawingInteractionManager (mouse) or
// CommandDispatcher (Application key), lists chart actions plus every active
// series BY NAME so acting on an indicator never requires pointing at a
// 2-pixel-wide line.

using System.Collections.Immutable;
using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public class ChartContextMenuTests
{
    private static ChartSeries NewSeries(string id, string friendlyName)
    {
        var config = new SeriesConfig { Id = id, Name = friendlyName, FriendlyName = friendlyName };
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = id });
    }

    private static BlazorTestHarness NewHarness(params ChartSeries[] series)
    {
        var h = new BlazorTestHarness();
        var state = WorkspaceState.Initial with
        {
            ActiveSeries = series.ToImmutableList(),
        };
        h.WorkspaceStore.State.Returns(_ => state);
        h.Ctx.Services.AddSingleton(new ChartHoverTracker(new BlazorInputService(), h.WorkspaceStore));
        return h;
    }

    private static IRenderedComponent<AccessibleTrader.BlazorClient.Components.ChartContextMenu>
        Open(BlazorTestHarness h, double x = 100, double y = 100, int barIndex = 42) =>
        h.OpenModal<AccessibleTrader.BlazorClient.Components.ChartContextMenu>(
            bus => bus.Publish(new OpenChartContextMenuEvent(x, y, barIndex)));

    [Fact]
    public void Opens_with_chart_actions_and_every_series_listed_by_name()
    {
        var h = NewHarness(NewSeries("candles", "Candles"), NewSeries("macd-1", "MACD 12 26 9"));
        using var _ = h;
        var cut = Open(h);

        var text = cut.Markup;
        Assert.Contains("Play from here", text);
        Assert.Contains("Jump to latest", text);
        Assert.Contains("crosshair", text, System.StringComparison.OrdinalIgnoreCase);
        // Series reachable as menu items — no pixel-precise pointing required.
        Assert.Contains("Candles", text);
        Assert.Contains("MACD 12 26 9", text);
    }

    [Fact]
    public void Selecting_a_series_shows_its_actions_with_a_back_item()
    {
        var h = NewHarness(NewSeries("macd-1", "MACD 12 26 9"));
        using var _ = h;
        var cut = Open(h);

        cut.FindAll("button").First(b => b.TextContent.Contains("MACD 12 26 9")).Click();

        var text = cut.Markup;
        Assert.Contains("Back", text);
        Assert.Contains("Focus", text);
        Assert.Contains("Mute", text);
        Assert.Contains("Hide", text);
        Assert.Contains("Properties", text);
        Assert.Contains("Remove", text);
    }

    [Fact]
    public void Mute_action_publishes_a_series_scoped_ToggleMuteEvent()
    {
        var h = NewHarness(NewSeries("macd-1", "MACD 12 26 9"));
        using var _ = h;
        ToggleMuteEvent? seen = null;
        h.EventBus.Subscribe<ToggleMuteEvent>(ev => seen = ev);

        var cut = Open(h);
        cut.FindAll("button").First(b => b.TextContent.Contains("MACD 12 26 9")).Click();
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Mute").Click();

        Assert.NotNull(seen);
        Assert.Equal("macd-1", seen!.SeriesId);
        Assert.Equal("SERIES", seen.Scope);
    }

    // The two refusals below used to be `disabled`, which is invisible to the user this
    // product is for: the menu item is not greyed out for them, it is ABSENT from the
    // menu they are arrowing through. Both now assert the whole of the new contract —
    // still refused, still reachable, and it says why — because asserting only
    // aria-disabled would pass on a button that had quietly stopped refusing.

    [Fact]
    public void Remove_is_refused_for_the_primary_price_series_and_says_why()
    {
        var h = NewHarness(NewSeries("candles", "Candles"));
        using var _ = h;
        var cut = Open(h);
        cut.FindAll("button").First(b => b.TextContent.Contains("Candles")).Click();

        var remove = cut.FindAll("button").First(b => b.TextContent.Trim() == "Remove");
        GatedButtonAssert.IsRefusedBecause(cut, remove, "cannot be removed");

        // And it still refuses: pressing it deletes nothing.
        int deletes = 0;
        using var sub = h.EventBus.Subscribe<DeleteSeriesEvent>(_ => deletes++);
        remove.Click();
        Assert.Equal(0, deletes);
    }

    [Fact]
    public void Play_from_here_is_refused_when_the_click_landed_in_the_empty_right_margin()
    {
        var h = NewHarness(NewSeries("candles", "Candles"));
        using var _ = h;
        var cut = Open(h, barIndex: -1);

        var play = cut.FindAll("button").First(b => b.TextContent.Contains("Play from here"));
        GatedButtonAssert.IsRefusedBecause(cut, play, "not over a bar");

        int plays = 0;
        using var sub = h.EventBus.Subscribe<PlaybackCommand>(_ => plays++);
        play.Click();
        Assert.Equal(0, plays);
    }

    [Fact]
    public void Keyboard_origin_with_NaN_coordinates_still_opens_and_self_positions()
    {
        var h = NewHarness(NewSeries("candles", "Candles"));
        using var _ = h;
        var cut = Open(h, x: double.NaN, y: double.NaN, barIndex: 10);

        Assert.NotEmpty(cut.FindAll("[role='menu']"));
    }
}
