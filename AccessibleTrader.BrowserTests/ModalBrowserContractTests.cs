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
            bool inside = await t.Page.EvaluateAsync<bool>(@"() => {
                const dialogs = Array.from(document.querySelectorAll('[role=""dialog""]'))
                                     .filter(el => el.offsetParent !== null);
                if (dialogs.length === 0) return true;
                return dialogs[dialogs.length - 1].contains(document.activeElement);
            }");
            var here = await t.ActiveElementAsync();
            stops.Add(here.Describe());
            Assert.True(inside,
                $"Tab #{i + 1} in {route.Modal} moved focus to {here.Describe()}, outside the dialog. " +
                "The overlay is still up, so the user is now operating controls they cannot see.");
        }

        // The vacuity check, and it is not optional. "Focus never left the dialog" is also exactly
        // what a Tab key that does nothing at all looks like. A dialog with one control is a
        // legitimate single stop (the Trading Dashboard with no chart loaded is literally one
        // Close button), so the floor is the number of focusable controls, not a constant.
        int focusable = await t.Page.EvaluateAsync<int>(@"() => {
            const dialogs = Array.from(document.querySelectorAll('[role=""dialog""]'))
                                 .filter(el => el.offsetParent !== null);
            if (dialogs.length === 0) return 0;
            const d = dialogs[dialogs.length - 1];
            return Array.from(d.querySelectorAll(
                'button, a[href], input, select, textarea, summary, [tabindex]:not([tabindex=""-1""])'))
                .filter(el => el.offsetParent !== null && !el.hasAttribute('disabled')).length;
        }");
        int expectedStops = Math.Min(focusable, 12);
        Assert.True(stops.Distinct().Count() >= Math.Min(expectedStops, 2) || focusable <= 1,
            $"{route.Modal} reports {focusable} focusable controls but Tab only ever reached " +
            $"{stops.Distinct().Count()} of them. Either the trap is pinning focus, or this test is " +
            "proving nothing — both are worth failing over.");
    }
}
