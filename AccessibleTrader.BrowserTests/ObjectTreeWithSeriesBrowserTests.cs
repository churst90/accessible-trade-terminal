using Microsoft.Playwright;

namespace AccessibleTrader.BrowserTests;

/// <summary>
/// The Object Tree, measured over a chart that actually has series on it.
///
/// <para>
/// Until this file existed, it could not be. Every browser route reached the app at cold start,
/// where <c>ActiveSeries</c> is empty and the tree body renders "No series active on chart" with
/// no rows in it at all — so the entire tree contract (expansion state, the roving tabindex, the
/// <c>&lt;details&gt;</c> toggle handling that hung Alt+O on 2026-09-04) was being asserted over
/// an empty dialog, and every one of those assertions passed vacuously.
/// <see cref="BlazorDisclosureRenderTests.The_object_tree_route_is_measured_over_an_empty_chart"/>
/// pins the cold-start fact; this class is what closes it.
/// </para>
///
/// <para>
/// The series come from <see cref="TerminalServerFactory.SeededSymbol"/> — an OHLCV dataset
/// written into the harness's throwaway data root before the host boots and served by the
/// built-in "My Data" provider. No network, no API key, no test-only branch in production code.
/// </para>
/// </summary>
[Collection("Terminal browser")]
public sealed class ObjectTreeWithSeriesBrowserTests
{
    private readonly TerminalBrowserFixture _fixture;
    public ObjectTreeWithSeriesBrowserTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    /// <summary>
    /// The seam itself. If this goes red every other test in this file is measuring the empty
    /// state again, so it fails with the diagnosis rather than leaving the rest to pass vacuously.
    /// </summary>
    [BrowserFact]
    public async Task The_seeded_dataset_loads_a_chart_with_series()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.LoadSeededChartAsync();

        var names = await t.ActiveSeriesNamesAsync();
        Assert.True(names.Count > 0,
            "The seeded My Data symbol produced no series. Everything else in this file would " +
            "now be measuring the empty chart, which is the vacuity this file exists to close.");
    }

    /// <summary>
    /// The vacuity check for the rest of the class, stated as its own case so a tree that goes
    /// empty again fails HERE, by name, instead of turning the assertions below green.
    /// </summary>
    [BrowserFact]
    public async Task The_tree_renders_panes_and_series_when_the_chart_has_data()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.LoadSeededChartAsync();

        await t.PressAsync("Alt+o");
        Assert.True(await t.WaitForDialogAsync(), "Object Tree did not open.");

        var panes = await t.Page.EvaluateAsync<int>("() => document.querySelectorAll('.pane-node').length");
        var rows  = await t.Page.EvaluateAsync<int>("() => document.querySelectorAll('.series-node').length");

        Assert.True(panes > 0, "The tree rendered no panes over a loaded chart.");
        Assert.True(rows  > 0, "The tree rendered no series rows over a loaded chart.");
    }

    /// <summary>
    /// The Alt+O hang, as a standing regression.
    ///
    /// <para>
    /// <c>ObjectTreeModal</c> rendered <c>&lt;details open="@IsPaneOpen(...)"&gt;</c> and flipped a
    /// bool on every <c>toggle</c>. Blazor applies an element's attributes after inserting it, so
    /// rendering an open disclosure fires a <c>toggle</c> nobody caused; against a flip that echo
    /// is a loop — echo says closed, the re-render removes <c>open</c>, that fires another toggle,
    /// which flips back, forever. The tab locked up.
    /// </para>
    ///
    /// <para>
    /// This counts toggle events over a settle window instead of asserting the tree "works":
    /// a loop is unbounded, so any small ceiling separates it from the handful of events a
    /// correct render produces. A hang would also fail the wait above — but it would fail as a
    /// timeout, which names nothing.
    /// </para>
    /// </summary>
    [BrowserFact]
    public async Task Opening_the_tree_over_a_loaded_chart_does_not_loop_the_disclosure_toggles()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.LoadSeededChartAsync();

        await t.Page.EvaluateAsync(@"() => { window.__toggles = 0;
            document.addEventListener('toggle', () => window.__toggles++, true); }");

        await t.PressAsync("Alt+o");
        Assert.True(await t.WaitForDialogAsync(), "Object Tree did not open.");

        await t.Page.WaitForTimeoutAsync(1_500);
        int settled = await t.Page.EvaluateAsync<int>("() => window.__toggles");

        int nodes = await t.Page.EvaluateAsync<int>(
            "() => document.querySelectorAll('.pane-node, .series-details').length");
        Assert.True(nodes > 0, "No disclosures rendered, so no loop could have been observed.");

        Assert.True(settled <= nodes * 4,
            $"{settled} toggle events over {nodes} disclosures in 1.5s after opening the tree. " +
            "An echo the handler answers by flipping is unbounded — this is that loop.");
    }

    /// <summary>
    /// <c>aria-expanded</c> is what a screen reader reads, and Chromium stops taking the state
    /// from <c>&lt;details&gt;</c> once the attribute is present — so the two disagreeing means the
    /// user is told the opposite of what is on screen.
    /// </summary>
    [BrowserFact]
    public async Task Every_pane_header_reports_the_expansion_state_its_details_actually_has()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.LoadSeededChartAsync();

        await t.PressAsync("Alt+o");
        Assert.True(await t.WaitForDialogAsync(), "Object Tree did not open.");

        var mismatches = await t.Page.EvaluateAsync<string[]>(@"() =>
            [...document.querySelectorAll('details.pane-node')].flatMap(d => {
                const s = d.querySelector('summary.pane-header');
                if (!s) return [`pane with no summary`];
                const declared = s.getAttribute('aria-expanded');
                const actual = d.open ? 'true' : 'false';
                return declared === actual ? [] : [`${s.getAttribute('aria-label')}: says ${declared}, is ${actual}`];
            })");

        Assert.True(mismatches.Length == 0,
            "Pane headers disagree with their own disclosures: " + string.Join("; ", mismatches));
    }

    /// <summary>
    /// Collapsing a pane must STAY collapsed. This is the self-correcting property the fix
    /// depends on: the handler reads the disclosure's real state, so the echo reports what the
    /// component already holds and changes nothing. A handler that flips would come back open.
    /// </summary>
    [BrowserFact]
    public async Task Collapsing_a_pane_stays_collapsed_and_says_so()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.LoadSeededChartAsync();

        await t.PressAsync("Alt+o");
        Assert.True(await t.WaitForDialogAsync(), "Object Tree did not open.");

        var header = t.Page.Locator("details.pane-node > summary.pane-header").First;
        Assert.Equal("true", await header.GetAttributeAsync("aria-expanded"));

        await header.ClickAsync();
        await t.Page.WaitForTimeoutAsync(1_000);

        Assert.Equal("false", await header.GetAttributeAsync("aria-expanded"));
        Assert.False(await t.Page.Locator("details.pane-node").First.EvaluateAsync<bool>("d => d.open"),
            "The pane re-opened itself after being collapsed — the toggle echo is being answered " +
            "by a flip again.");
    }

    /// <summary>
    /// One tab stop in the whole tree, which is what makes Tab pass over it in a single press and
    /// the arrow keys the way through it. Two stops is a roving tabindex that stopped roving; zero
    /// means Tab skips the tree entirely and it cannot be reached without the mouse.
    /// </summary>
    [BrowserFact]
    public async Task The_tree_has_exactly_one_tab_stop()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.LoadSeededChartAsync();

        await t.PressAsync("Alt+o");
        Assert.True(await t.WaitForDialogAsync(), "Object Tree did not open.");

        int stops = await t.Page.EvaluateAsync<int>(
            "() => document.querySelectorAll('[role=tree] [role=treeitem][tabindex=\"0\"]').length");

        Assert.Equal(1, stops);
    }

    /// <summary>
    /// A treeitem with no accessible name is a row a blind user hears as nothing at all. The names
    /// come from Chromium's own accessibility tree rather than from the DOM attributes, because
    /// what matters is the name the AT computes, not the one the markup intended.
    /// </summary>
    [BrowserFact]
    public async Task Every_tree_row_has_an_accessible_name()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.LoadSeededChartAsync();

        await t.PressAsync("Alt+o");
        Assert.True(await t.WaitForDialogAsync(), "Object Tree did not open.");

        var rows = await t.Page.EvaluateAsync<int>(
            "() => document.querySelectorAll('[role=tree] [role=treeitem]').length");
        Assert.True(rows > 0, "No tree rows, so nothing was named or unnamed.");

        var cdp = await t.Page.Context.NewCDPSessionAsync(t.Page);
        await cdp.SendAsync("Accessibility.enable");
        var axTree = await cdp.SendAsync("Accessibility.getFullAXTree");
        Assert.NotNull(axTree);

        var unnamed = new List<string>();
        foreach (var node in axTree!.Value.GetProperty("nodes").EnumerateArray())
        {
            if (!node.TryGetProperty("role", out var role)) continue;
            if (role.GetProperty("value").GetString() != "treeitem") continue;
            string? name = node.TryGetProperty("name", out var n)
                ? n.GetProperty("value").GetString()
                : null;
            if (string.IsNullOrWhiteSpace(name))
                unnamed.Add(node.TryGetProperty("nodeId", out var id) ? id.ToString() : "?");
        }

        Assert.True(unnamed.Count == 0,
            $"{unnamed.Count} of the tree's treeitems have no accessible name.");
    }
}
