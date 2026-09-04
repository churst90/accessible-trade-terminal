using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;
using Bunit;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// The Object Tree is selection-follows-focus (the WAI-ARIA APG default for a single-select
/// tree, and Cody's call on 2026-09-03).
///
/// <para>
/// <c>aria-selected</c> on a series row has always mirrored the CHART's focused series, but until
/// this pass arrowing in the tree moved only the tree's DOM focus — <c>SelectSeriesAction</c> fired
/// on Enter or click. So a user standing on "Rectangle 1" with "Trend line 2" focused on the chart
/// pressed Shift+Arrow (allowed under this dialog) and nudged Trend line 2, learning which only from
/// the settle sentence's second-to-last clause. Now the row that has focus IS the chart's focused
/// series: focusing a series row dispatches the selection, and the tree's one tab stop is that row,
/// so Tab into the tree lands on the series the chart is on.
/// </para>
/// </summary>
public class ObjectTreeSelectionFollowsFocusTests
{
    private static ChartSeries Series(string id, string name)
    {
        var config = new SeriesConfig { Id = id, Name = name, FriendlyName = name };
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = id });
    }

    private static void Seed(BlazorTestHarness h, string? focusedId)
    {
        var state = WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto", "kraken", "BTC/USD", "1h"),
            ActiveSeries = ImmutableList.Create(Series("candles", "Candles"), Series("rsi", "RSI 14")),
            FocusedSeriesId = focusedId,
        };
        h.WorkspaceStore.State.Returns(_ => state);
    }

    private static IRenderedComponent<AccessibleTrader.BlazorClient.Components.ObjectTreeModal> Open(BlazorTestHarness h) =>
        h.OpenModal<AccessibleTrader.BlazorClient.Components.ObjectTreeModal>(
            bus => bus.Publish(new OpenObjectTreeEvent()));

    private static AngleSharp.Dom.IElement Row(IRenderedComponent<AccessibleTrader.BlazorClient.Components.ObjectTreeModal> cut, string seriesName) =>
        cut.FindAll("[role='treeitem'][aria-level='2']")
           .Single(e => e.GetAttribute("aria-label")!.StartsWith(seriesName + ",", StringComparison.Ordinal));

    [Fact]
    public void The_tab_stop_is_the_charts_focused_series_not_the_first_pane()
    {
        using var h = new BlazorTestHarness();
        Seed(h, focusedId: "rsi");

        var cut = Open(h);

        Assert.Equal("0",  Row(cut, "RSI 14").GetAttribute("tabindex"));
        Assert.Equal("-1", Row(cut, "Candles").GetAttribute("tabindex"));
        Assert.Equal("-1", cut.Find("[role='treeitem'][aria-level='1']").GetAttribute("tabindex"));
        Assert.Equal("true", Row(cut, "RSI 14").GetAttribute("aria-selected"));
    }

    [Fact]
    public void With_no_series_focused_the_first_pane_header_is_the_tab_stop()
    {
        // Nothing to follow: fall back to the documented tree entry point.
        using var h = new BlazorTestHarness();
        Seed(h, focusedId: null);

        var cut = Open(h);

        Assert.Equal("0", cut.Find("[role='treeitem'][aria-level='1']").GetAttribute("tabindex"));
        Assert.All(cut.FindAll("[role='treeitem'][aria-level='2']"),
            row => Assert.Equal("-1", row.GetAttribute("tabindex")));
    }

    [Fact]
    public void Under_a_collapsed_pane_the_tab_stop_is_that_panes_header()
    {
        // A tabindex of 0 on a row inside a closed <details> is a stop nothing can reach, and
        // Tab would skip the tree. The pane's header is the reachable thing that re-opens it.
        using var h = new BlazorTestHarness();
        Seed(h, focusedId: "rsi");
        var cut = Open(h);

        cut.Find("details.pane-node").TriggerEvent("ontoggle", EventArgs.Empty);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("0",  cut.Find("[role='treeitem'][aria-level='1']").GetAttribute("tabindex"));
            Assert.Equal("-1", Row(cut, "RSI 14").GetAttribute("tabindex"));
        });
    }

    [Fact]
    public void Tabbing_onto_a_rows_buttons_does_not_select()
    {
        // The trigger is @onfocus on the treeitems, not @onfocusin on the row: focusin bubbles
        // from the Hide/Mute/Delete buttons Tab visits on its way out, and a user tabbing to
        // Close would have walked the chart's focus to the last series in the tree. Checked
        // after a positive holds.
        using var h = new BlazorTestHarness();
        Seed(h, focusedId: "candles");
        var cut = Open(h);

        Row(cut, "RSI 14").Focus();
        cut.WaitForAssertion(() =>
            h.WorkspaceStore.Received(1).Dispatch(Arg.Is<SelectSeriesAction>(a => a.SeriesId == "rsi")));

        h.WorkspaceStore.ClearReceivedCalls();
        // focusin is the BUBBLING focus event — the one a Tab onto the button would deliver to
        // an ancestor row wired with @onfocusin. bUnit throws when nothing in the bubbling path
        // handles it, and that exception IS the property: no handler above the button listens.
        // Under the regression (a handler on the row) the event bubbles, dispatches, and the
        // assertion below fails.
        foreach (var name in new[] { "Hide Candles", "Mute Candles" })
        {
            try { cut.Find($"button[aria-label='{name}']").FocusIn(); }
            catch (MissingEventHandlerException) { /* nothing listens above the button — correct */ }
        }

        // A negative after the dispatcher has demonstrably run once above.
        h.WorkspaceStore.DidNotReceive().Dispatch(Arg.Any<SelectSeriesAction>());
    }

    [Fact]
    public void Focusing_a_series_row_focuses_that_series_on_the_chart()
    {
        using var h = new BlazorTestHarness();
        Seed(h, focusedId: "candles");
        var cut = Open(h);

        Row(cut, "RSI 14").Focus();

        // Polled: bUnit queues the handler on the renderer dispatcher, and a read before it has
        // run is the seventh flake of this repo.
        cut.WaitForAssertion(() =>
            h.WorkspaceStore.Received(1).Dispatch(Arg.Is<SelectSeriesAction>(a => a.SeriesId == "rsi")));
    }

    [Fact]
    public void Focusing_the_row_that_is_already_focused_dispatches_nothing()
    {
        // A redundant dispatch is a store update and a re-render for nothing, on every focus
        // event the row and its buttons produce. The negative is checked AFTER a positive
        // holds, so it cannot pass by reading before the dispatcher has run.
        using var h = new BlazorTestHarness();
        Seed(h, focusedId: "candles");
        var cut = Open(h);

        Row(cut, "Candles").Focus();
        Row(cut, "RSI 14").Focus();

        cut.WaitForAssertion(() =>
            h.WorkspaceStore.Received(1).Dispatch(Arg.Is<SelectSeriesAction>(a => a.SeriesId == "rsi")));
        h.WorkspaceStore.Received(1).Dispatch(Arg.Any<SelectSeriesAction>());
    }
}
