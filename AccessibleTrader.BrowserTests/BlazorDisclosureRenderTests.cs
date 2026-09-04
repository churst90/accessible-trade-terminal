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
    /// The tree shows what the chart has — whatever that is.
    ///
    /// <para>
    /// This case used to assert the opposite half of the vacuity finding: that every route
    /// reached the Object Tree over an EMPTY chart, so no route rendered a single node. That was
    /// true, and it was the problem. <see cref="ObjectTreeWithSeriesBrowserTests"/> closed it by
    /// seeding an offline dataset the harness can chart, which means the cold-start emptiness is
    /// no longer a fact of this suite and asserting it would now just be asserting test order.
    /// </para>
    ///
    /// <para>
    /// What replaces it is the invariant that cannot be vacuous either way: the number of series
    /// rows in the tree equals the number of series on the chart. Zero equals zero on a cold
    /// page; four equals four once something has loaded. A tree that renders nothing over a
    /// loaded chart fails here, and so does one that renders rows for series that are gone.
    /// </para>
    /// </summary>
    [BrowserFact]
    public async Task The_object_tree_shows_exactly_the_series_the_chart_has()
    {
        await using var t = await _fixture.NewPageAsync();

        var expected = await t.ActiveSeriesNamesAsync();

        await t.PressAsync("Alt+o");
        Assert.True(await t.WaitForDialogAsync());

        var rows = await t.Page.EvaluateAsync<int>(
            "() => document.querySelectorAll('.series-node').length");
        Assert.Equal(expected.Count, rows);

        bool emptyStateShown = await t.Page.EvaluateAsync<bool>(
            "() => [...document.querySelectorAll('p')].some(p => p.textContent.includes('No series active on chart'))");
        Assert.Equal(expected.Count == 0, emptyStateShown);
    }
}
