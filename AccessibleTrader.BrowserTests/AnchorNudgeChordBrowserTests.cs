using Microsoft.Playwright;

namespace AccessibleTrader.BrowserTests;

/// <summary>
/// The keyboard nudge's chord routing, observed in a real Chromium against the real WebHost.
///
/// <para>Two things the unit tests cannot see. <c>keyboard.js</c> forwards every Alt-modified
/// key to .NET and calls <c>preventDefault</c>, even from a text field — which is right for
/// most chords and wrong for <c>Alt+Shift+Arrow</c>, whose native meaning inside a text field
/// (select-by-word on macOS) the user wants more than a chart command the dispatcher would
/// drop anyway. And on the chart, the same chord must reach the dispatcher and be ANSWERED: a
/// cold-start WebHost has no chart loaded, so the honest answer is "No chart loaded." rather
/// than silence.</para>
///
/// <para>The positive case runs first and proves the recorder can see this chord's answer;
/// only then does the negative case mean anything.</para>
/// </summary>
[Collection("Terminal browser")]
public sealed class AnchorNudgeChordBrowserTests
{
    private readonly TerminalBrowserFixture _fixture;
    public AnchorNudgeChordBrowserTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    /// <summary>The toolbar's market select: a native form control that every build renders
    /// (the number inputs are hidden on the demo host the harness runs). The carve-out covers
    /// INPUT, TEXTAREA and SELECT alike.</summary>
    private const string MarketSelect = "market-select";

    [BrowserFact]
    public async Task Alt_Shift_Left_on_the_chart_is_answered_not_swallowed()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.FocusChartAsync();
        await t.ClearSpokenAsync();

        await t.PressAsync("Alt+Shift+ArrowLeft");

        var spoken = await t.WaitForSpeechAsync();
        Assert.Contains(spoken, u =>
            u.Text.Contains("No chart loaded", StringComparison.OrdinalIgnoreCase)
            || u.Text.Contains("Focus a drawing first", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Records every keydown that anything cancels. That is the ONE property the carve-out
    /// changes. "Nothing was spoken" cannot see it: with focus in the select the chart is
    /// blurred, so the dispatcher's chart-focus gate drops the command whether or not the key
    /// was forwarded — the first version of this test was green with the carve-out deleted.
    /// A bubble listener cannot see it either: keyboard.js calls stopImmediatePropagation on
    /// every Alt chord, so nothing downstream ever runs. Patching preventDefault itself is the
    /// only vantage point that survives that.
    /// </summary>
    private static Task InstallKeydownProbe(TerminalPage t) => t.Page.EvaluateAsync(
        "() => { window.__prevented = []; const orig = KeyboardEvent.prototype.preventDefault;" +
        "  KeyboardEvent.prototype.preventDefault = function () {" +
        "    if (this.type === 'keydown') window.__prevented.push(this.key); return orig.call(this); }; }");

    private static Task ResetProbe(TerminalPage t) => t.Page.EvaluateAsync("() => { window.__prevented = []; }");

    private static Task<bool> WasPrevented(TerminalPage t, string key) =>
        t.Page.EvaluateAsync<bool>("k => (window.__prevented || []).includes(k)", key);

    [BrowserFact]
    public async Task Alt_Shift_Arrow_inside_a_toolbar_form_control_is_left_to_the_control()
    {
        await using var t = await _fixture.NewPageAsync();
        await InstallKeydownProbe(t);

        // Positive control first, on the same page: on the chart the chord IS cancelled by
        // keyboard.js (it is a chart command), and the probe sees that.
        await t.FocusChartAsync();
        await ResetProbe(t);
        await t.PressAsync("Alt+Shift+ArrowLeft");
        Assert.True(await WasPrevented(t, "ArrowLeft"), "the probe must see the chord cancelled on the chart");

        await t.Page.FocusAsync($"#{MarketSelect}");
        Assert.True(await t.WaitForFocusAsync(MarketSelect),
            $"Could not put focus on the toolbar's market select; focus is on {(await t.ActiveElementAsync()).Describe()}.");

        await ResetProbe(t);
        await t.PressAsync("Alt+Shift+ArrowLeft");
        await t.PressAsync("Alt+Shift+ArrowUp");
        Assert.False(await WasPrevented(t, "ArrowLeft"),
            "Alt+Shift+Left inside a form control was cancelled by keyboard.js: the control loses its own binding " +
            "(select-by-word on macOS) for a chart command the dispatcher would drop anyway.");
        Assert.False(await WasPrevented(t, "ArrowUp"));
        Assert.Equal(MarketSelect, (await t.ActiveElementAsync()).Id);
    }
}
