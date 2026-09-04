namespace AccessibleTrader.BrowserTests;

/// <summary>
/// Pins the engine fact the Object Tree's toggle handling depends on.
///
/// <para>Blazor's renderer inserts an element and applies its attributes AFTERWARDS, so a
/// component rendering <c>&lt;details open&gt;</c> produces a real closed→open transition in
/// the DOM — and the browser fires <c>toggle</c> for it, with no user involved.</para>
///
/// <para>ObjectTreeModal renders <c>open</c> from C# state AND listens for <c>toggle</c>. While
/// its handler FLIPPED a bool, that echo was a loop: echo says closed → re-render removes
/// <c>open</c> → that fires another toggle → flips back → re-renders… Alt+O over a chart with
/// any series on it hung the tab (Cody, 2026-09-04). Neither existing harness could see it —
/// bUnit has no browser to fire <c>toggle</c>, and every browser route opens this dialog at
/// cold start, where <c>ActiveSeries</c> is empty and the tree body renders "No series active
/// on chart" with no <c>&lt;details&gt;</c> in it at all.</para>
///
/// <para>The fix is to read the disclosure's real state instead of flipping, which is only
/// worth its interop round-trip while this fact holds. If a future Blazor applied attributes
/// before insertion this test goes red, and the comment in ObjectTreeModal can be retired.</para>
/// </summary>
[Collection("Terminal browser")]
public sealed class BlazorDisclosureRenderTests
{
    private readonly TerminalBrowserFixture _fixture;
    public BlazorDisclosureRenderTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Measured against HelpModal, which renders exactly one <c>&lt;details open&gt;</c>
    /// (HelpModal.razor, the shortcuts section) and has no toggle handler anywhere — so every
    /// event counted here came from the render itself.
    /// </summary>
    [BrowserFact]
    public async Task Blazor_rendering_an_open_disclosure_fires_a_toggle_nobody_caused()
    {
        await using var t = await _fixture.NewPageAsync();

        await t.Page.EvaluateAsync(@"() => { window.__toggles = 0;
            document.addEventListener('toggle', () => window.__toggles++, true); }");

        await t.PressAsync("F1");
        Assert.True(await t.WaitForDialogAsync(), "Help did not open, so nothing was measured.");
        await t.Page.WaitForTimeoutAsync(500);

        var open = await t.Page.EvaluateAsync<int>(
            "() => document.querySelectorAll('details[open]').length");
        Assert.True(open >= 1,
            "Help rendered no open <details>; the fixture, not the claim, is what changed.");

        var toggles = await t.Page.EvaluateAsync<int>("() => window.__toggles");
        Assert.True(toggles >= 1,
            $"Blazor rendered {open} initially-open <details> and the page saw {toggles} toggle " +
            "events. Zero would mean attributes are now applied before insertion — good news, " +
            "and it makes ObjectTreeModal.ReadDisclosureAsync's interop round-trip removable.");
    }

    /// <summary>
    /// The other half of the vacuity finding, kept as a standing note: every browser route that
    /// opens the Object Tree does so with no series on the chart, so none of them renders a
    /// single tree node. Any Object Tree claim made by this suite is about the empty state only.
    /// </summary>
    [BrowserFact]
    public async Task The_object_tree_route_is_measured_over_an_empty_chart()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.PressAsync("Alt+o");
        Assert.True(await t.WaitForDialogAsync());

        var panes = await t.Page.EvaluateAsync<int>(
            "() => document.querySelectorAll('.pane-node').length");
        Assert.Equal(0, panes);
    }
}
