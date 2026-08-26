namespace AccessibleTrader.BrowserTests;

/// <summary>
/// What the terminal says, and whether it cuts off what was already speaking.
///
/// <para>
/// A2/F2: changing an order fill from <c>interrupt: true</c> to <c>interrupt: false</c> broke
/// nothing, because the entire 4,830-test suite contains exactly one assertion that touches an
/// <c>interrupt:</c> value and it is a grep over <c>.razor</c> source. Nothing observes the flag
/// through behaviour. These tests do, at the browser boundary — the last point before the words
/// reach a human.
/// </para>
///
/// <para>
/// The distinction is not cosmetic. Interrupting is what makes "No chart loaded." arrive when the
/// user pressed the arrow key rather than after the queue drains; not interrupting is what stops a
/// modal-opened announcement from stepping on the sentence in progress. The policy is one of the
/// most user-visible decisions in the application and until now it was written down only in the
/// call sites.
/// </para>
/// </summary>
[Collection("Terminal browser")]
public sealed class SpeechBrowserTests
{
    private readonly TerminalBrowserFixture _fixture;
    public SpeechBrowserTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Vacuity floor for everything else in this file. If the recorder ever stops seeing calls —
    /// and it silently did, for an hour, because Blazor memoizes the function it resolves for a JS
    /// interop identifier — every assertion below passes by observing nothing.
    /// </summary>
    [BrowserFact]
    public async Task The_speech_recorder_actually_sees_what_the_app_says()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.ClearSpokenAsync();

        await t.PressAsync("Alt+o");
        await t.WaitForDialogAsync();
        var spoken = await t.WaitForSpeechAsync();

        Assert.NotEmpty(spoken);
    }

    [BrowserFact]
    public async Task Opening_a_modal_announces_it_without_cutting_off_what_is_speaking()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.ClearSpokenAsync();

        await t.PressAsync("Alt+o");
        await t.WaitForDialogAsync();
        var spoken = await t.WaitForSpeechAsync();

        var announcement = Assert.Single(spoken, u => u.Text.Contains("Object tree", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("opened", announcement.Text, StringComparison.OrdinalIgnoreCase);

        // Deliberately non-interrupting: a dialog opening is context, not an emergency, and
        // cutting off a price readout to say "dialog opened" loses the number the user asked for.
        Assert.False(announcement.Interrupt,
            $"\"{announcement.Text}\" interrupted whatever was speaking. Modal-open announcements " +
            "are context and must queue behind the sentence in progress.");
    }

    [BrowserFact]
    public async Task Closing_a_modal_is_announced_too()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.PressAsync("Alt+o");
        await t.WaitForDialogAsync();
        await t.ClearSpokenAsync();

        await t.PressAsync("Escape");
        await t.WaitForNoDialogAsync();
        var spoken = await t.WaitForSpeechAsync();

        Assert.Contains(spoken, u => u.Text.Contains("closed", StringComparison.OrdinalIgnoreCase));
    }

    [BrowserFact]
    public async Task Moving_focus_to_the_chart_interrupts()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.ClearSpokenAsync();

        await t.FocusChartAsync();
        var spoken = await t.WaitForSpeechAsync();

        var announcement = Assert.Single(spoken, u => u.Text.Contains("chart area", StringComparison.OrdinalIgnoreCase));
        Assert.True(announcement.Interrupt,
            $"\"{announcement.Text}\" queued behind whatever was speaking. Focus moved because the " +
            "user asked it to; they need to hear where they are now, not in ten seconds.");
    }

    [BrowserFact]
    public async Task Navigating_an_empty_chart_says_so_and_interrupts()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.FocusChartAsync();
        await t.ClearSpokenAsync();

        await t.PressAsync("ArrowRight");
        var spoken = await t.WaitForSpeechAsync();

        var announcement = Assert.Single(spoken, u => u.Text.Contains("No chart loaded", StringComparison.OrdinalIgnoreCase));
        Assert.True(announcement.Interrupt);
    }

    /// <summary>
    /// Turning speech off has to be audible, which is a rule with an obvious failure mode: check
    /// the flag first and the confirmation is the announcement that never happens.
    /// </summary>
    [BrowserFact]
    public async Task Turning_speech_off_still_says_so()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.ClearSpokenAsync();

        await t.PressAsync("F2");
        var spoken = await t.WaitForSpeechAsync();

        Assert.Contains(spoken, u => u.Text.Contains("Speech off", StringComparison.OrdinalIgnoreCase));
    }
}
