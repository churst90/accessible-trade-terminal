using Microsoft.Playwright;

namespace AccessibleTrader.BrowserTests;

/// <summary>
/// The modal contract, asserted in a real browser.
///
/// <para>
/// This is the suite A2 asked for. The existing bUnit contract
/// (<c>ModalAccessibilityContractTests</c>) asserts that a focus call happened and that its target
/// exists — deleting <c>WalletModal</c>'s own <c>focusElement("wallet-asset")</c> left it green,
/// because <c>ModalBase.ShowModalAsync</c> had already focused the heading. Every ModalBase modal
/// has that hole. Here the question is <c>document.activeElement</c> against a target declared by
/// hand in <see cref="ModalRoutes"/>, which is a different question and the one the user lives in.
/// </para>
///
/// <para>
/// It also closes the structural blind spot that motivated the whole harness: bUnit applies a
/// render synchronously and a browser does not, so Alt+T could open the trading dashboard without
/// moving focus at all and survive a dedicated focus suite, a modal catalog and 4,830 green tests.
/// </para>
/// </summary>
[Collection("Terminal browser")]
public sealed class ModalBrowserContractTests
{
    private readonly TerminalBrowserFixture _fixture;
    public ModalBrowserContractTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    public static TheoryData<string> RouteNames
    {
        get
        {
            var d = new TheoryData<string>();
            foreach (var r in ModalRoutes.All.Where(r => r.ColdStartReachable)) d.Add(r.Name);
            return d;
        }
    }

    private static ModalRoute Route(string name) => ModalRoutes.All.Single(r => r.Name == name);

    private async Task<TerminalPage> OpenAsync(TerminalPage t, ModalRoute route)
    {
        if (route.NeedsChartFocus) await t.FocusChartAsync();

        if (route.How == OpenBy.Shortcut)
        {
            await t.PressAsync(route.Trigger);
        }
        else
        {
            var button = t.Page.GetByRole(AriaRole.Button, new() { Name = route.Trigger, Exact = true });
            Assert.True(await button.CountAsync() > 0,
                $"No toolbar button whose accessible name is \"{route.Trigger}\". A screen-reader user " +
                "navigates by that name, so if it changed, the route changed.");
            await button.First.ClickAsync();
        }

        Assert.True(await t.WaitForDialogAsync(),
            $"{route.Modal} did not open via {route.Trigger}.");
        return t;
    }

    [BrowserTheory]
    [MemberData(nameof(RouteNames))]
    public async Task Opening_a_dialog_puts_focus_on_the_declared_target(string routeName)
    {
        var route = Route(routeName);
        await using var t = await _fixture.NewPageAsync();
        await OpenAsync(t, route);

        bool landed = await t.WaitForFocusAsync(route.ExpectedFocusId);
        var actual = await t.ActiveElementAsync();

        Assert.True(landed,
            $"{route.Modal} opened via {route.Trigger} but focus is on {actual.Describe()}, " +
            $"not on '{route.ExpectedFocusId}'. Why that target: {route.Why}");
    }

    [BrowserTheory]
    [MemberData(nameof(RouteNames))]
    public async Task Every_dialog_announces_a_name(string routeName)
    {
        var route = Route(routeName);
        await using var t = await _fixture.NewPageAsync();
        await OpenAsync(t, route);

        var name = await t.TopDialogAccessibleNameAsync();

        Assert.False(name is null,
            $"{route.Modal} has neither aria-labelledby nor aria-label; a screen reader announces " +
            "it as an unnamed dialog.");
        Assert.False(name!.Length == 0,
            $"{route.Modal}'s aria-labelledby resolves to nothing — it points at an element that " +
            "does not exist or has no text, which announces exactly like having no label at all.");
    }

    [BrowserTheory]
    [MemberData(nameof(RouteNames))]
    public async Task Escape_closes_the_dialog_and_gives_focus_back_to_the_chart(string routeName)
    {
        var route = Route(routeName);
        await using var t = await _fixture.NewPageAsync();
        await OpenAsync(t, route);
        await t.WaitForFocusAsync(route.ExpectedFocusId);

        await t.PressAsync("Escape");

        Assert.True(await t.WaitForNoDialogAsync(), $"{route.Modal} did not close on Escape.");
        Assert.Equal(0, await t.OpenModalCountAsync());

        // Focus restoration is a race with the closing render, so it is waited on rather than
        // sampled. Sampling it reported "the Order Book dumps you on <body>" on exactly one route
        // out of twenty-four, once — the harness reading too early, not the app losing focus.
        Assert.True(await t.WaitForFocusAsync("chart-interact-zone"),
            $"After closing {route.Modal}, focus is on {(await t.ActiveElementAsync()).Describe()} " +
            "rather than back on the chart. A keyboard user has nowhere to continue from.");
    }

    [BrowserTheory]
    [MemberData(nameof(RouteNames))]
    public async Task Tab_never_escapes_an_open_dialog(string routeName)
    {
        var route = Route(routeName);
        await using var t = await _fixture.NewPageAsync();
        await OpenAsync(t, route);
        await t.WaitForFocusAsync(route.ExpectedFocusId);

        var stops = new List<string>();
        for (int i = 0; i < 12; i++)
        {
            await t.PressAsync("Tab");
            var where = await t.FocusRelativeToTopDialogAsync();
            var here = await t.ActiveElementAsync();
            stops.Add(here.Describe());
            Assert.True(where != FocusPlace.NoDialogSeen,
                $"After Tab #{i + 1} in {route.Modal}, the trap's own dialog predicate sees NO " +
                "dialog although one was opened. This predicate is the one keyboard.js uses; a " +
                "dialog it cannot see is a dialog it cannot trap. (This used to read as 'inside'.)");
            Assert.True(where == FocusPlace.Inside,
                $"Tab #{i + 1} in {route.Modal} moved focus to {here.Describe()}, outside the dialog. " +
                "The overlay is still up, so the user is now operating controls they cannot see.");
        }

        // The vacuity check, and it is not optional. "Focus never left the dialog" is also exactly
        // what a Tab key that does nothing at all looks like. A dialog with one control is a
        // legitimate single stop, so the floor is the number of focusable controls, not a constant.
        //
        // The Trading Dashboard used to be the example here — with no chart loaded it was
        // literally one Close button, because a venue that could not execute orders replaced the
        // ENTIRE dialog with a wall. That was the chart coupling, not a small dialog: the accounts
        // were there all along. Since 2026-08-26 the same route surveys seven distinct stops and a
        // five-tab tablist (see scratchpad/a3_survey.json), which is why the example is gone rather
        // than updated.
        int focusable = await t.TabStopCountInTopDialogAsync();
        int expectedStops = Math.Min(focusable, 12);
        Assert.True(stops.Distinct().Count() >= Math.Min(expectedStops, 2) || focusable <= 1,
            $"{route.Modal} reports {focusable} focusable controls but Tab only ever reached " +
            $"{stops.Distinct().Count()} of them. Either the trap is pinning focus, or this test is " +
            "proving nothing — both are worth failing over.");
    }

    [BrowserTheory]
    [MemberData(nameof(RouteNames))]
    public async Task ShiftTab_never_escapes_an_open_dialog(string routeName)
    {
        // The missing half of the guard above, and the half that was broken.
        //
        // Tab_never_escapes_an_open_dialog has been green since it was written, and it was right
        // — forward containment genuinely worked. But it only ever pressed Tab, so it exercised
        // one direction of a rule stated in both. Shift+Tab escaped EVERY dialog in the app.
        //
        // The mechanism is worth stating because it is why nobody caught it by reading: the trap
        // tested `active === first`, ModalBase opens focus on the <h2 tabindex="-1">, and the
        // focusable selector deliberately excludes tabindex="-1". So on open the heading was
        // neither `first` nor `last` and no branch fired at all. The very first Shift+Tab — the
        // reflex after overshooting — walked backward out of the dialog onto the toolbar behind,
        // while aria-modal="true" told the screen reader not to describe anything out there.
        //
        // Pressed from the opening focus target on purpose. Tabbing inward first would move focus
        // onto a real tab stop and hide the defect completely.
        var route = Route(routeName);
        await using var t = await _fixture.NewPageAsync();
        await OpenAsync(t, route);
        await t.WaitForFocusAsync(route.ExpectedFocusId);

        var stops = new List<string>();
        for (int i = 0; i < 12; i++)
        {
            await t.PressAsync("Shift+Tab");
            var where = await t.FocusRelativeToTopDialogAsync();
            var here = await t.ActiveElementAsync();
            stops.Add(here.Describe());
            Assert.True(where != FocusPlace.NoDialogSeen,
                $"After Shift+Tab #{i + 1} in {route.Modal}, the trap's own dialog predicate sees " +
                "NO dialog although one was opened — see Tab_never_escapes_an_open_dialog.");
            Assert.True(where == FocusPlace.Inside,
                $"Shift+Tab #{i + 1} in {route.Modal} moved focus to {here.Describe()}, outside " +
                "the dialog. The overlay is still up and aria-modal=\"true\" has restricted the " +
                "screen reader to the dialog, so the user is now standing on a control their " +
                "screen reader will not describe.");
        }

        // Same vacuity floor as the forward case, same reason: "focus never left" is also what a
        // Shift+Tab that does nothing looks like.
        int focusable = await t.TabStopCountInTopDialogAsync();
        Assert.True(stops.Distinct().Count() >= Math.Min(Math.Min(focusable, 12), 2) || focusable <= 1,
            $"{route.Modal} reports {focusable} focusable controls but Shift+Tab only ever reached " +
            $"{stops.Distinct().Count()} of them. Either the trap is pinning focus, or this test is " +
            "proving nothing — both are worth failing over.");
    }
}
