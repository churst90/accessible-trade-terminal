namespace AccessibleTrader.BrowserTests;

/// <summary>
/// The three bar lists' keys reach the terminal from a real Chromium, once each (Cody, 2026-11-20:
/// comma formations, period headlines, apostrophe market events, Shift going back; slash opens
/// the Go bar).
///
/// <para>
/// Until 2026-11-20 none of apostrophe, Shift+comma, Shift+period, Shift+apostrophe or slash was in
/// keyboard.js's trapped-key list (a browser reports the shifted three as <c>&lt;</c>, <c>&gt;</c>
/// and <c>"</c>). On the chart they still reached .NET through ChartArea's own
/// <c>@onkeydown</c>, but nothing cancelled them, so Firefox's quick find opened over the chart
/// on apostrophe and slash; and away from the chart slash reached nothing at all. So "the key is
/// answered" cannot see the trap — a first version of this test stayed green with the keys taken
/// back out of the list. What only the trap provides is pinned instead: the keydown is cancelled,
/// and slash opens the Go bar from a toolbar button. "Answered once" stays, because two listeners
/// can carry these keys.
/// </para>
/// </summary>
[Collection("Terminal browser")]
public sealed class BarListKeyBrowserTests
{
    private readonly TerminalBrowserFixture _fixture;
    public BarListKeyBrowserTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    private static readonly string[] FormationAnswers =
    {
        "Chart formation description is off",
        "No chart formations on this chart",
        "No further chart formations",
        "No earlier chart formations",
    };

    private static readonly string[] HeadlineAnswers =
    {
        "Headlines are off",
        "No later headlines",
        "No earlier headlines",
        "Headlines are for stocks and coins",
        "Headline:",
    };

    private static readonly string[] EventAnswers =
    {
        "Market events are off",
        "No later market events",
        "No earlier market events",
    };

    /// <summary>Records every keydown anything cancels; patching preventDefault itself is the one
    /// vantage point that survives keyboard.js's stopImmediatePropagation (see
    /// AnchorNudgeChordBrowserTests).</summary>
    private static Task InstallKeydownProbe(TerminalPage t) => t.Page.EvaluateAsync(
        "() => { window.__prevented = []; const orig = KeyboardEvent.prototype.preventDefault;" +
        "  KeyboardEvent.prototype.preventDefault = function () {" +
        "    if (this.type === 'keydown') window.__prevented.push(this.key); return orig.call(this); }; }");

    private static Task<bool> WasPrevented(TerminalPage t, string key) =>
        t.Page.EvaluateAsync<bool>("k => (window.__prevented || []).includes(k)", key);

    private static async Task<List<string>> AnswersAfter(TerminalPage t, string chord, string[] answers)
    {
        await t.ClearSpokenAsync();
        await t.PressAsync(chord);

        bool IsAnswer(Utterance u) => answers.Any(a => u.Text.Contains(a, StringComparison.OrdinalIgnoreCase));
        await t.WaitForSpeechAsync(IsAnswer);

        // A second dispatch, when there is one, arrives a round trip later.
        await Task.Delay(1_500);
        return (await t.SpokenAsync()).Where(IsAnswer).Select(u => u.Text).ToList();
    }

    [BrowserTheory]
    [InlineData("Comma", ",", "formations")]
    [InlineData("Shift+Comma", "<", "formations")]
    [InlineData("Period", ".", "headlines")]
    [InlineData("Shift+Period", ">", "headlines")]
    [InlineData("Quote", "'", "events")]
    [InlineData("Shift+Quote", "\"", "events")]
    public async Task Each_bar_list_key_is_answered_once_from_the_chart_and_cancelled(string chord, string reported, string list)
    {
        await using var t = await _fixture.NewPageAsync();
        await t.LoadSeededChartAsync();
        await t.FocusChartAsync();
        await InstallKeydownProbe(t);

        var answers = await AnswersAfter(t, chord, list switch
        {
            "formations" => FormationAnswers,
            "headlines" => HeadlineAnswers,
            _ => EventAnswers,
        });

        Assert.True(answers.Count == 1,
            $"{chord} on the chart was answered {answers.Count} times: [{string.Join(" | ", answers)}]. "
            + "Zero means the key never reached the dispatcher (not trapped, or folded to another key); "
            + "two means the window listener and the chart's @onkeydown both dispatched it.");
        Assert.True(await WasPrevented(t, reported),
            $"{chord} (reported as '{reported}') was not cancelled on the chart: it is missing from "
            + "keyboard.js's trapped keys, so the browser's own action for it (quick find) runs too.");
    }

    [BrowserFact]
    public async Task Slash_opens_the_Go_bar_from_the_chart_and_from_a_toolbar_button()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.LoadSeededChartAsync();
        await t.FocusChartAsync();
        await InstallKeydownProbe(t);

        await t.PressAsync("Slash");
        Assert.True(await t.WaitForFocusAsync(GoBarInput),
            "Slash on the chart did not open the Go bar with focus in its text box.");
        Assert.True(await WasPrevented(t, "/"), "Slash on the chart was not cancelled; Firefox's quick find opens too.");

        // Off the chart only keyboard.js's window listener can carry the key. A fresh page, so
        // no Go bar is left open from the press above.
        await using var u = await _fixture.NewPageAsync();
        await u.LoadSeededChartAsync();
        await u.Page.FocusAsync(PanLeft);
        Assert.Equal("BUTTON", (await u.ActiveElementAsync()).Tag);
        await u.PressAsync("Slash");
        Assert.True(await u.WaitForFocusAsync(GoBarInput),
            $"Slash on a toolbar button did not open the Go bar; focus is on {(await u.ActiveElementAsync()).Describe()}.");
    }

    private const string GoBarInput = "gobar-input";
    private const string PanLeft = "button[aria-label='Pan left']";
}
