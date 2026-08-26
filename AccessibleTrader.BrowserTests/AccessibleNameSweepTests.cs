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

    /// <summary>
    /// The controls known to have no accessible name, as of the A3 sweep on 2026-08-26.
    ///
    /// <para>
    /// An exact set rather than a "no more than N" budget, on purpose: a budget silently absorbs a
    /// new defect whenever an old one is fixed. This fails both when something new goes unnamed
    /// and when one of these is fixed — the second failure is the one that keeps the list honest,
    /// and clearing an entry is a one-line edit here.
    /// </para>
    /// </summary>
    private static readonly IReadOnlySet<string> KnownUnnamed = new HashSet<string>
    {
        // RiskPlanEditor's take-profit ladder. Every other field in that editor uses
        // <label for="risk-…">; the ladder rows cannot, because they are rendered in a @foreach
        // and a fixed id would collide across rungs. So the visible "R:" / "Close fraction:" /
        // "TP1:" text is not attached to anything, and a screen-reader user arrives at an
        // unlabelled spin button while setting take-profit levels on a real trade.
        "StrategyModal|Build Setup|select after “TP1:”",
        "StrategyModal|Build Setup|select after “TP2:”",
        "StrategyModal|Build Setup|select after “TP3:”",
        "StrategyModal|Build Setup|input type=number after “R:”",
        "StrategyModal|Build Setup|input type=number after “Close fraction:”",

        // Named only by its placeholder, which disappears as soon as the field has content.
        "SoundDesignerModal|(initial)|[placeholder-only] textarea #sd-import-json",
    };

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

        var unexpected = found.Distinct().Where(f => !KnownUnnamed.Contains(f)).ToList();
        var fixedSince = KnownUnnamed.Where(k => k.StartsWith(route.Modal + "|", StringComparison.Ordinal))
                                     .Where(k => !found.Contains(k)).ToList();

        Assert.True(unexpected.Count == 0,
            $"Controls in {route.Modal} that a screen reader announces with no name:\n  "
            + string.Join("\n  ", unexpected)
            + "\n\nEither give them an accessible name, or add them to KnownUnnamed with the reason.");

        Assert.True(fixedSince.Count == 0,
            $"These were on the KnownUnnamed list for {route.Modal} and now have names — good. "
            + "Delete them from the list so it keeps meaning something:\n  "
            + string.Join("\n  ", fixedSince));
    }
}
