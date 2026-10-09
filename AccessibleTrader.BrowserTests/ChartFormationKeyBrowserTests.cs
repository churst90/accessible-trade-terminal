namespace AccessibleTrader.BrowserTests;

/// <summary>
/// One press of semicolon is one command, observed in a real Chromium against the real WebHost.
///
/// <para>
/// Reported from live use (2026-10-09): "I can't tell which formation is being changed as I keep
/// hearing 1 of 2 each time I press ;". The cause was not in the formation code. A keypress on the
/// focused chart reaches .NET twice — once from keyboard.js's window listener and once from
/// ChartArea's own <c>@onkeydown</c> fallback — and GlobalInputService drops the second as a
/// duplicate only if both arrive under the same key NAME. keyboard.js sends <c>OEM1</c> for
/// <c>;</c> and <c>:</c>; the element path sent the raw characters. The names differed, the
/// dedupe let both through, and every press cycled the pin TWICE: forward one and straight back,
/// so the readout never changed and the same position was spoken every time. Shift+semicolon
/// likewise cleared the pin and then said "No formation was pinned." about its own work.
/// </para>
///
/// <para>
/// No unit test could see it: each pipeline is correct alone, and the defect is that both run.
/// This is the vantage point where they are both real.
/// </para>
/// </summary>
[Collection("Terminal browser")]
public sealed class ChartFormationKeyBrowserTests
{
    private readonly TerminalBrowserFixture _fixture;
    public ChartFormationKeyBrowserTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    // Every answer CycleFocus / ClearFocus can give, whatever the seeded data holds at the cursor.
    private static readonly string[] CycleAnswers =
    {
        "Chart formation description is off",
        "No chart formation at this bar",
        "Only one formation here",
        "Leading with",
    };

    private static readonly string[] ClearAnswers =
    {
        "Formation choice cleared",
        "No formation was pinned",
    };

    private static async Task<List<string>> AnswersAfter(TerminalPage t, string chord, string[] answers)
    {
        await t.ClearSpokenAsync();
        await t.PressAsync(chord);

        bool IsAnswer(Utterance u) => answers.Any(a => u.Text.Contains(a, StringComparison.OrdinalIgnoreCase));
        await t.WaitForSpeechAsync(IsAnswer);

        // The second dispatch, when there is one, arrives a round trip later. Give it time to
        // land before counting; a count taken on the first utterance would always say one.
        await Task.Delay(1_500);
        return (await t.SpokenAsync()).Where(IsAnswer).Select(u => u.Text).ToList();
    }

    [BrowserFact]
    public async Task One_press_of_semicolon_runs_the_pin_command_once()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.LoadSeededChartAsync();
        await t.FocusChartAsync();

        var answers = await AnswersAfter(t, "Semicolon", CycleAnswers);

        Assert.True(answers.Count == 1,
            $"One press of semicolon was answered {answers.Count} times: [{string.Join(" | ", answers)}]. "
            + "Zero means the key never reached the dispatcher; two means the window listener and the "
            + "chart's @onkeydown both dispatched it, which cycles the pin forward and back so the "
            + "user hears the same position on every press.");
    }

    [BrowserFact]
    public async Task One_press_of_shift_semicolon_runs_the_clear_command_once()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.LoadSeededChartAsync();
        await t.FocusChartAsync();

        var answers = await AnswersAfter(t, "Shift+Semicolon", ClearAnswers);

        Assert.True(answers.Count == 1,
            $"One press of Shift+semicolon was answered {answers.Count} times: [{string.Join(" | ", answers)}]. "
            + "Two means the command ran twice — the second run reports on the first one's work.");
    }
}
