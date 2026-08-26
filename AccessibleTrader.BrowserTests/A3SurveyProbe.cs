using System.Text.Json;
using Microsoft.Playwright;

namespace AccessibleTrader.BrowserTests;

/// <summary>
/// The A3 survey: walks every route in <see cref="ModalRoutes"/> and writes what the browser
/// actually did to <c>scratchpad/a3_survey.json</c>. It asserts almost nothing on purpose — its
/// job is to produce the evidence the assertions are then written against, the same way the
/// sandbox audit compiled 25 candidate escapes before deciding which four were real.
///
/// <para>
/// Kept in the tree rather than thrown away: re-running it after a UI change is how the next
/// pass finds out what moved, and it costs one test.
/// </para>
/// </summary>
[Collection("Terminal browser")]
public sealed class A3SurveyProbe
{
    private readonly TerminalBrowserFixture _fixture;
    public A3SurveyProbe(TerminalBrowserFixture fixture) => _fixture = fixture;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    [BrowserFact]
    public async Task Survey_every_route_and_write_the_evidence()
    {
        var report = new List<Dictionary<string, object?>>();

        // The toolbar as a screen reader sees it, first — a route that cannot be found is a
        // finding in itself, and "the button is not there" and "the button is there but unnamed"
        // are different findings.
        await using (var t = await _fixture.NewPageAsync())
        {
            var buttons = await t.Page.EvaluateAsync<string>(@"() => JSON.stringify(
                Array.from(document.querySelectorAll('button'))
                     .filter(b => b.offsetParent !== null)
                     .map(b => ({
                        label: b.getAttribute('aria-label') || '',
                        text: (b.textContent || '').replace(/\s+/g, ' ').trim(),
                        id: b.id || ''
                     })))");
            File.WriteAllText(Path.Combine(RepoRoot(), "scratchpad", "a3_buttons.json"), buttons);

            File.WriteAllText(Path.Combine(RepoRoot(), "scratchpad", "a3_page_unnamed.json"),
                JsonSerializer.Serialize(await t.UnnamedControlsOnPageAsync(),
                    new JsonSerializerOptions { WriteIndented = true }));
        }

        foreach (var route in ModalRoutes.All)
        {
            var row = new Dictionary<string, object?>
            {
                ["modal"] = route.Modal,
                ["how"] = route.How.ToString(),
                ["trigger"] = route.Trigger,
                ["expectedFocus"] = route.ExpectedFocusId,
            };

            // A fresh page per route. Full mode's workspace store is a process singleton, so a
            // dialog left open (or a setting toggled) by one route would leak into the next.
            await using var t = await _fixture.NewPageAsync();
            try
            {
                var before = await t.ActiveElementAsync();
                row["focusBeforeOpen"] = before.Describe();

                if (route.NeedsChartFocus)
                {
                    await t.FocusChartAsync();
                    row["chartFocused"] = true;
                }

                if (route.How == OpenBy.Shortcut)
                {
                    await t.PressAsync(route.Trigger);
                }
                else
                {
                    var button = t.Page.GetByRole(AriaRole.Button, new() { Name = route.Trigger, Exact = true });
                    int count = await button.CountAsync();
                    row["toolbarButtonCount"] = count;
                    if (count == 0) { row["opened"] = false; row["note"] = "no toolbar button with that accessible name"; report.Add(row); continue; }
                    await button.First.ClickAsync();
                }

                bool opened = await t.WaitForDialogAsync(8_000);
                row["opened"] = opened;
                if (!opened) { report.Add(row); continue; }

                row["dialogIds"] = await t.VisibleDialogIdsAsync();
                row["dialogAccessibleName"] = await t.TopDialogAccessibleNameAsync();
                row["openModalCount"] = await t.OpenModalCountAsync();

                bool landed = await t.WaitForFocusAsync(route.ExpectedFocusId, 4_000);
                row["focusLandedOnDeclaredTarget"] = landed;
                row["actualFocus"] = (await t.ActiveElementAsync()).Describe();

                row["unnamedControls"] = await t.UnnamedControlsInTopDialogAsync();

                // Walk the tabs. Most dialogs here are tabbed, and only one tab's controls exist
                // in the DOM at a time, so a single-shot sweep sees a fraction of the app.
                var tabs = await t.TopDialogTabNamesAsync();
                row["tabs"] = tabs;
                if (tabs.Count > 0)
                {
                    var perTab = new Dictionary<string, IReadOnlyList<string>>();
                    foreach (var tab in tabs)
                    {
                        if (!await t.ClickTopDialogTabAsync(tab)) continue;
                        await t.Page.WaitForTimeoutAsync(120);   // let the re-render land
                        perTab[tab] = await t.UnnamedControlsInTopDialogAsync();
                    }
                    row["unnamedByTab"] = perTab;
                }

                // Tab trap: press Tab a dozen times and see whether focus ever leaves the dialog.
                //
                // The escape list alone would be a vacuous guard — "focus never left the dialog"
                // is also what a Tab key that does nothing at all looks like. So record where
                // focus actually went on each press; `tabDistinctStops` is the vacuity check, and
                // a 1 there means this row proves nothing about any trap.
                var escapees = new List<string>();
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
                    if (!inside) escapees.Add(here.Describe());
                }
                row["tabEscapes"] = escapees;
                row["tabStops"] = stops;
                row["tabDistinctStops"] = stops.Distinct().Count();

                await t.PressAsync("Escape");
                row["escapeClosed"] = await t.WaitForNoDialogAsync(5_000);
                row["openModalCountAfterEscape"] = await t.OpenModalCountAsync();

                // Where focus ends up after a close is a RACE unless it is waited on: the modal
                // hides, then MainLayout restores focus on the next render. Reading
                // document.activeElement immediately caught the gap on one route out of 24 in the
                // first survey — which looked like "the Order Book dumps you on <body>" and was
                // really the harness reading too early.
                await t.WaitForFocusAsync("chart-interact-zone", 3_000);
                row["focusAfterEscape"] = (await t.ActiveElementAsync()).Describe();
            }
            catch (Exception ex)
            {
                row["error"] = ex.GetType().Name + ": " + ex.Message.Split('\n')[0];
            }

            report.Add(row);
        }

        var path = Path.Combine(RepoRoot(), "scratchpad", "a3_survey.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }));

        // The only assertion: the survey covered what it claims to cover.
        Assert.Equal(ModalRoutes.All.Count(), report.Count);
    }
}
