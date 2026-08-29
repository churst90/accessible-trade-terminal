// The Toolbar's shape-change confirmation — the inline role="alertdialog" that warns before
// a load strips the current tab's indicators and drawings.
//
// Filed by A1 and fixed 2026-08-29. It declared itself a dialog and implemented none of the
// modal contract: no ModalStateChangedEvent, so CommandDispatcher's modal stack never knew it
// was open (Escape went to the chart, and MainLayout — which arms the Tab trap off that same
// event — never trapped Tab), and no focus move, so a screen-reader user was left standing on
// the Load button with a destructive prompt unread on screen. On MAUI the same event hides the
// SkiaSharp canvas, so it drew UNDERNEATH the chart.
//
// ModalContractScanTests now covers role="alertdialog" and asserts the events STRUCTURALLY.
// This file asserts the half a source scan cannot see: that the events actually fire when the
// dialog opens and closes, that Escape reaches it, and that focus lands inside it and comes
// back to the button that opened it. Both are needed — the scan cannot watch behaviour, and a
// behavioural test on one dialog cannot stop the next one skipping the contract entirely.

using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Bunit;
using NSubstitute;
using Cmp = AccessibleTrader.BlazorClient.Components;

namespace AccessibleTrader.Tests.Blazor;

public sealed class ToolbarShapeChangeDialogTests
{
    /// <summary>
    /// A toolbar whose next Load is a shape change that would strip user content: the current
    /// tab is OHLCV with one non-core series on it, and the selected provider returns scalar
    /// analytics. That combination is the only one that raises the dialog.
    /// </summary>
    private static (BlazorTestHarness Harness, List<ModalStateChangedEvent> Modal) Arrange()
    {
        var h = new BlazorTestHarness();

        // One NON-CORE series — an indicator the user added. The warning counts exactly these,
        // so seeding "candles" instead would leave nothing to strip and raise no dialog.
        var ema = new ChartSeries(new SeriesConfig { Id = "ema-20", Name = "EMA 20" },
                                  new SeriesDataBuffer { SeriesId = "ema-20" });
        var state = WorkspaceState.Initial with
        {
            CurrentDataShape = ProviderDataShape.Ohlcv,
            ActiveSeries = ImmutableList.Create(ema),
        };
        h.WorkspaceStore.State.Returns(_ => state);

        h.MarketOrchestrator.SelectedSymbol.Returns("BTC/USD");
        h.MarketOrchestrator.GetSelectedProviderDataShapeAsync()
            .Returns(Task.FromResult(ProviderDataShape.SingleValueLine));

        var modal = new List<ModalStateChangedEvent>();
        h.EventBus.Subscribe<ModalStateChangedEvent>(modal.Add);
        return (h, modal);
    }

    private static IRenderedComponent<Cmp.Toolbar> Open(BlazorTestHarness h)
    {
        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();
        cut.Find("#toolbar-load-btn").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[role=alertdialog]")));
        return cut;
    }

    [Fact]
    public void Opening_it_announces_itself_as_a_modal_and_focuses_its_heading()
    {
        var (h, modal) = Arrange();
        using var _ = h;

        var cut = Open(h);

        // The event is what puts it on the dispatcher's modal stack, arms the Tab trap and
        // hides the MAUI canvas. Without it the dialog is a floating div that Escape, Tab and
        // the native canvas all ignore.
        var opened = Assert.Single(modal);
        Assert.True(opened.IsOpen);
        Assert.Equal("ShapeChangeWarning", opened.ModalName);

        Assert.Contains(h.Ctx.JSInterop.Invocations["accessibleTrader.focusElement"],
                        i => (string?)i.Arguments[0] == "switchWarnTitle");

        // The heading is the focus target, so it has to be able to take focus at all.
        Assert.Equal("-1", cut.Find("#switchWarnTitle").GetAttribute("tabindex"));
    }

    [Fact]
    public void Escape_closes_it_and_cancels_rather_than_loading()
    {
        var (h, modal) = Arrange();
        using var _ = h;

        var cut = Open(h);
        modal.Clear();

        // Exactly what CommandDispatcher publishes on Escape once the dialog is on its stack.
        h.EventBus.Publish(new CloseTopModalEvent("ShapeChangeWarning"));

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[role=alertdialog]")));

        var closed = Assert.Single(modal);
        Assert.False(closed.IsOpen);
        Assert.Equal("ShapeChangeWarning", closed.ModalName);

        // Escape is the non-destructive branch. A dialog that warns about discarding the
        // user's indicators must not read "get me out of here" as consent to discard them.
        h.MarketOrchestrator.DidNotReceive().LoadChartAsync();
        h.MarketOrchestrator.DidNotReceive().LoadChartInNewTabAsync();
    }

    [Fact]
    public void Closing_it_returns_focus_to_the_button_that_opened_it()
    {
        var (h, modal) = Arrange();
        using var _ = h;

        var cut = Open(h);

        cut.Find("[role=alertdialog] button[aria-label^='Cancel']").Click();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[role=alertdialog]")));

        // Not "focus went somewhere valid" — the id of the control the user pressed. A
        // dismissed dialog that leaves focus on <body> puts a screen-reader user back at the
        // top of the document with no idea the prompt is gone.
        cut.WaitForAssertion(() => Assert.Contains(
            h.Ctx.JSInterop.Invocations["accessibleTrader.focusElement"],
            i => (string?)i.Arguments[0] == "toolbar-load-btn"));
    }

    [Fact]
    public void An_ordinary_load_raises_no_dialog_and_no_modal_event()
    {
        // The vacuity check for the three above: if the toolbar raised this dialog on every
        // load, all of them would pass while the warning was meaningless. Same tab shape as
        // the provider being loaded, so nothing would be stripped.
        var (h, modal) = Arrange();
        using var _ = h;
        h.MarketOrchestrator.GetSelectedProviderDataShapeAsync()
            .Returns(Task.FromResult(ProviderDataShape.Ohlcv));

        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();
        cut.Find("#toolbar-load-btn").Click();

        cut.WaitForAssertion(() => h.MarketOrchestrator.Received().LoadChartAsync());
        Assert.Empty(cut.FindAll("[role=alertdialog]"));
        Assert.Empty(modal);
    }
}
