using Microsoft.Playwright;

namespace AccessibleTrader.BrowserTests;

/// <summary>
/// Does every control a user can reach have a name a screen reader can read out?
///
/// <para>
/// A2/F9: <b>181 of the 193 literal <c>aria-label</c> values in the component library are never
/// named by any test</b>, and no test anywhere asserts that a control has an accessible name at
/// all. This is that sweep, run against the browser's own view of each control rather than
/// against the Razor source — a <c>&lt;label&gt;</c> that renders next to an input but carries no
/// <c>for</c> attribute reads as labelled in the markup and is silent in the browser, which is
/// exactly the defect it found.
/// </para>
/// </summary>
[Collection("Terminal browser")]
public sealed class AccessibleNameSweepTests
{
    private readonly TerminalBrowserFixture _fixture;
    public AccessibleNameSweepTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    // THERE IS NO EXEMPTION LIST HERE ANY MORE, and that is deliberate.
    //
    // The A3 sweep on 2026-08-26 pinned six controls as known-unnamed: five take-profit ladder
    // rungs (fixed 2026-08-29 — the recorded reason, "a fixed id would collide across rungs",
    // dissolved the moment the id came from the loop index) and SoundDesignerModal's import
    // textarea, named only by a placeholder that vanishes as soon as the field has content.
    // The textarea now has a <label for>, so the list emptied — and an empty exemption list is
    // deleted rather than kept, the same call made for KnownLabelInNameGaps on 2026-08-29.
    // Anything found from here on is a defect to fix, not a line to add back.
    //
    // WHAT THAT COSTS. The assertion below is now a bare "no matches", which is also what a
    // sweep that examined nothing returns — and the `fixedSince` half that used to notice a
    // collapsed sweep went with the list. So the floor underneath it is load-bearing: the
    // route must have opened a dialog with controls in it before "no unnamed controls" means
    // anything at all.

    public static TheoryData<string> RouteNames
    {
        get
        {
            var d = new TheoryData<string>();
            // One route per modal — opening the same dialog from the toolbar and from its
            // shortcut renders the same markup, and sweeping it twice only doubles the runtime.
            foreach (var name in ModalRoutes.All.Where(r => r.ColdStartReachable)
                                                .GroupBy(r => r.Modal)
                                                .Select(g => g.First().Name))
                d.Add(name);
            return d;
        }
    }

    [BrowserTheory]
    [MemberData(nameof(RouteNames))]
    public async Task Every_control_in_the_dialog_has_an_accessible_name(string routeName)
    {
        var route = ModalRoutes.All.Single(r => r.Name == routeName);
        await using var t = await _fixture.NewPageAsync();

        if (route.NeedsChartFocus) await t.FocusChartAsync();
        if (route.How == OpenBy.Shortcut)
            await t.PressAsync(route.Trigger);
        else
            await t.Page.GetByRole(AriaRole.Button, new() { Name = route.Trigger, Exact = true })
                        .First.ClickAsync();

        Assert.True(await t.WaitForDialogAsync(), $"{route.Modal} did not open.");

        var found = new List<string>();
        foreach (var c in await t.UnnamedControlsInTopDialogAsync())
            found.Add($"{route.Modal}|(initial)|{c}");

        // Walk the tabs. Only one tab's controls exist in the DOM at a time, so sweeping the tab
        // that happens to render first covers a fraction of the application.
        foreach (var tab in await t.TopDialogTabNamesAsync())
        {
            if (!await t.ClickTopDialogTabAsync(tab)) continue;
            await t.Page.WaitForTimeoutAsync(150);
            foreach (var c in await t.UnnamedControlsInTopDialogAsync())
                found.Add($"{route.Modal}|{tab}|{c}");
        }

        var unexpected = found.Distinct().ToList();

        Assert.True(unexpected.Count == 0,
            $"Controls in {route.Modal} that a screen reader announces with no name:\n  "
            + string.Join("\n  ", unexpected)
            + "\n\nGive them an accessible name. There is no exemption list here on purpose — "
            + "see the comment at the top of this file.");

        // The vacuity floor. With the exemption list gone, "unexpected is empty" is also what a
        // sweep that found no controls at all reports — a changed dialog role, a selector typo,
        // a dialog that rendered nothing. Every route in this list opens a dialog with controls
        // in it, so a zero here is a broken instrument, not a clean result.
        int swept = await t.ControlCountInTopDialogAsync();
        Assert.True(swept > 0,
            $"The sweep of {route.Modal} examined ZERO controls, so its clean result means "
            + "nothing. The dialog opened but the control selector matched nothing in it.");
    }
}
