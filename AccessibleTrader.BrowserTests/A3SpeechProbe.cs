using System.Text.Json;

namespace AccessibleTrader.BrowserTests;

/// <summary>
/// What the terminal actually SAYS, observed at the browser boundary rather than asserted from a
/// grep over Razor source. Writes <c>scratchpad/a3_speech.json</c>.
/// </summary>
[Collection("Terminal browser")]
public sealed class A3SpeechProbe
{
    private readonly TerminalBrowserFixture _fixture;
    public A3SpeechProbe(TerminalBrowserFixture fixture) => _fixture = fixture;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    [BrowserFact]
    public async Task What_the_app_says_when_you_drive_it()
    {
        var report = new List<object>();

        async Task Record(string what, Func<TerminalPage, Task> action)
        {
            await using var t = await _fixture.NewPageAsync();
            await t.ClearSpokenAsync();
            await action(t);
            await t.WaitForSpeechAsync(4_000);
            await t.Page.WaitForTimeoutAsync(400);   // let a burst finish arriving
            // The live region is the OTHER half of the speech story, and reading both is what
            // distinguishes "the app said nothing" from "the harness is deaf".
            var live = await t.Page.EvaluateAsync<string>(
                @"() => JSON.stringify(Array.from(document.querySelectorAll('[aria-live]'))
                        .map(e => ({ id: e.id || '', text: (e.textContent || '').trim() }))
                        .filter(e => e.text.length > 0))");
            report.Add(new
            {
                scenario = what,
                spoken = await t.SpokenAsync(),
                liveRegions = JsonSerializer.Deserialize<object>(live),
            });
        }

        await Record("open the Object Tree with Alt+O", async t =>
        {
            await t.PressAsync("Alt+o");
            await t.WaitForDialogAsync();
        });

        await Record("open and then Escape out of the Object Tree", async t =>
        {
            await t.PressAsync("Alt+o");
            await t.WaitForDialogAsync();
            await t.PressAsync("Escape");
            await t.WaitForNoDialogAsync();
        });

        await Record("focus the chart with Ctrl+Alt+Shift+C", async t => await t.FocusChartAsync());

        await Record("arrow-key navigation on an empty chart", async t =>
        {
            await t.FocusChartAsync();
            await t.PressAsync("ArrowRight");
            await t.PressAsync("ArrowRight");
        });

        await Record("toggle speech off with F2 and back on", async t =>
        {
            await t.PressAsync("F2");
            await t.Page.WaitForTimeoutAsync(300);
            await t.PressAsync("F2");
        });

        await Record("Shift+F1 context summary", async t =>
        {
            await t.FocusChartAsync();
            await t.PressAsync("Shift+F1");
        });

        File.WriteAllText(Path.Combine(RepoRoot(), "scratchpad", "a3_speech.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        Assert.NotEmpty(report);
    }
}
