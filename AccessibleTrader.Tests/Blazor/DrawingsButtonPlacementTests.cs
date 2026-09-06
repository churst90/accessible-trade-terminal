using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Bunit;
using NSubstitute;
using Cmp = AccessibleTrader.BlazorClient.Components;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// Which bar the Drawings button lives on, and why it is the bottom one.
///
/// <para>
/// Cody, 2026-09-06: "drawing tools is more appropriate on the bottom bar where the indicator and
/// custom script buttons are." The indicator bar is the <b>chart-content</b> bar — everything on
/// it acts on what is drawn on this chart, and its two switches act on the focused series. The top
/// toolbar is the <b>application</b> bar: accounts, orders, workspaces, settings. A drawing is
/// chart content in the same sense an indicator is: you add it to a chart, it appears in the
/// Object Tree beside the indicators, it sonifies like a series, and Shift+Arrow nudges it like
/// one. Reading order on the bottom bar is Add, Scripts, Drawings.
/// </para>
///
/// <para>
/// Asserted on both bars, not one. "It is on the indicator bar now" is half the claim; the other
/// half is that it is no longer on the toolbar, and a test that checks only the first would stay
/// green if the button were duplicated.
/// </para>
/// </summary>
public class DrawingsButtonPlacementTests
{
    /// <summary>
    /// Selected by ACCESSIBLE NAME, which is "Drawings" — the visible label, with no AriaLabel
    /// override. That is the toolbar convention (WCAG 2.5.3 Label-in-Name holds by construction)
    /// and it is also the route the browser harness clicks by, so the move stays invisible to it.
    /// </summary>
    private const string DrawingsButton = "button[aria-label='Drawings']";

    /// <summary>
    /// A tab showing price bars. Both bars gate Drawings on the data shape — an analytics feed
    /// has no OHLCV for a trend line to anchor to — so a harness left on the default shape would
    /// find no button on either bar and pass both halves for the wrong reason.
    /// </summary>
    private static BlazorTestHarness OhlcvTab()
    {
        var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto", "kraken", "BTC/USD", "1h"),
            CurrentDataShape = ProviderDataShape.Ohlcv,
        });
        return h;
    }

    [Fact]
    public void TheDrawingsButtonIsOnTheIndicatorBar()
    {
        using var h = OhlcvTab();

        var cut = h.Ctx.RenderComponent<Cmp.IndicatorBar>();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(DrawingsButton)));
    }

    [Fact]
    public void TheDrawingsButtonIsNotOnTheTopToolbar()
    {
        using var h = OhlcvTab();

        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();

        // Vacuity floor: the toolbar rendered, it simply no longer holds this one control.
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button")));
        Assert.Empty(cut.FindAll(DrawingsButton));
    }

    [Fact]
    public void ItReadsAfterAddAndScripts()
    {
        // The order is the claim: the three things you put ON a chart, in the order they are
        // reached for. On a bar with no arrow-key handler, DOM order IS the Tab order, so this
        // is what a screen-reader user actually walks through.
        using var h = OhlcvTab();

        var cut = h.Ctx.RenderComponent<Cmp.IndicatorBar>();

        cut.WaitForAssertion(() =>
        {
            var labels = cut.FindAll("button")
                .Select(b => b.GetAttribute("aria-label") ?? "")
                .Where(l => l.Contains("indicator", StringComparison.OrdinalIgnoreCase)
                         || l.Contains("Scripts", StringComparison.OrdinalIgnoreCase)
                         || l.Equals("Drawings", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.Equal(
                new[] { "Add indicator to chart", "Custom Scripts (Alt+Comma)", "Drawings" },
                labels);
        });
    }

    [Fact]
    public void OnAFeedWithNoPriceBars_TheButtonIsOnNeitherBar()
    {
        // The gate came down with the button. A trend line needs bars to anchor to, so on an
        // analytics feed the tool would open onto a chart it cannot draw on.
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Analytics", "glassnode", "BTC-MVRV", "1d"),
            CurrentDataShape = ProviderDataShape.SingleValueLine,
        });

        var bar = h.Ctx.RenderComponent<Cmp.IndicatorBar>();
        var toolbar = h.Ctx.RenderComponent<Cmp.Toolbar>();

        bar.WaitForAssertion(() => Assert.Empty(bar.FindAll(DrawingsButton)));
        toolbar.WaitForAssertion(() => Assert.Empty(toolbar.FindAll(DrawingsButton)));
    }
}
