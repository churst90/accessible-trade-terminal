namespace AccessibleTrader.BrowserTests;

/// <summary>
/// <b>Settings → Keyboard → Rebind, driven by real keystrokes.</b>
///
/// <para>
/// Measured broken on 2026-09-24, both in the same way. The capture was a document-level
/// listener, and keyboard.js's window-level shortcut trap runs before it. For a modifier chord
/// the trap called <c>stopImmediatePropagation</c>, so no chord could ever be captured: the row
/// sat on "waiting..." forever. Escape, which the panel says cancels, was dispatched as a
/// shortcut (closing Settings) AND captured, so the command was rebound to Escape. No test
/// had ever pressed a key into the capture; the C# side was tested by calling
/// <c>OnKeyCaptured</c> directly, which is past the part that was broken.
/// </para>
///
/// <para>
/// Every test here deletes the saved <c>shortcuts.json</c> when it ends. The WebHost's
/// shortcut manager is scoped per circuit and reads that file, and every test in the
/// collection shares one server, so a rebinding left behind would change the keys for
/// everything that runs after.
/// </para>
/// </summary>
[Collection("Terminal browser")]
public sealed class KeyRebindCaptureBrowserTests
{
    private readonly TerminalBrowserFixture _fixture;
    public KeyRebindCaptureBrowserTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    private void ForgetRebindings()
    {
        foreach (var f in Directory.EnumerateFiles(_fixture.DataRoot, "shortcuts.json", SearchOption.AllDirectories))
            File.Delete(f);
    }

    private static async Task StartRebindAsync(TerminalPage t, string command)
    {
        await t.PressAsync("F12");
        Assert.True(await t.WaitForDialogAsync(), "Settings did not open.");
        Assert.True(await t.ClickTopDialogTabAsync("Keyboard"), "Settings has no Keyboard tab.");
        await t.Page.ClickAsync($"[aria-label='Rebind {command}']");
        await t.Page.WaitForSelectorAsync("[aria-label='Cancel key capture']");
        // The capture field must hold focus: on a button, NVDA and JAWS stay in browse mode
        // and eat letters and arrows before the page sees them.
        Assert.True(await t.WaitForFocusAsync("rebind-capture"), "the capture field did not take focus");
    }

    /// <summary>
    /// Leaving the capture field cancels it, so a capture cannot stay armed and turn the next
    /// key typed anywhere into a binding (an accessibility review of the first fix found exactly
    /// that: the first letter typed on another Settings tab rebound the command).
    /// </summary>
    [BrowserFact]
    public async Task Leaving_the_capture_field_cancels_so_no_later_key_is_swallowed()
    {
        try
        {
            await using var t = await _fixture.NewPageAsync();
            await StartRebindAsync(t, "ToggleNarration");
            await t.Page.EvaluateAsync("() => document.getElementById('settings-title').focus()");
            await t.Page.WaitForTimeoutAsync(300);
            Assert.False(await StillCapturingAsync(t), "the capture stayed armed after focus left it");

            await t.Page.Keyboard.PressAsync("Control+Alt+Shift+Y");
            await t.Page.WaitForTimeoutAsync(300);
            Assert.Equal("N", await ShortcutCellAsync(t, "ToggleNarration"));
        }
        finally { ForgetRebindings(); }
    }

    private static Task<string> ShortcutCellAsync(TerminalPage t, string command) =>
        t.Page.EvaluateAsync<string>(@"cmd => {
            const row = [...document.querySelectorAll('.shortcuts-table tr')]
                .find(r => r.cells[0] && r.cells[0].textContent.trim() === cmd);
            return row ? row.cells[1].textContent.trim() : 'NO ROW'; }", command);

    private static Task<bool> StillCapturingAsync(TerminalPage t) =>
        t.Page.EvaluateAsync<bool>("() => !!document.querySelector(\"[aria-label='Cancel key capture']\")");

    /// <summary>
    /// A chord is captured, and the rebinding then actually fires the command. The second half
    /// is what the key-name spelling decides: a key stored under a name the trap never sends
    /// is a binding that shows in the table and does nothing.
    /// </summary>
    [BrowserFact]
    public async Task A_modifier_chord_is_captured_and_then_runs_the_command()
    {
        try
        {
            await using var t = await _fixture.NewPageAsync();
            await StartRebindAsync(t, "OpenHelp");

            await t.Page.Keyboard.PressAsync("Control+Alt+Shift+Y");
            await t.Page.WaitForFunctionAsync(
                "() => !document.querySelector(\"[aria-label='Cancel key capture']\")",
                null, new() { Timeout = 5_000 });

            var cell = await ShortcutCellAsync(t, "OpenHelp");
            Assert.Contains("Y", cell);

            // Focus goes back to the row, not to <body> with the capture field that held it.
            Assert.True(await t.WaitForFocusAsync("rebind-OpenHelp"),
                "after the capture, focus did not return to the row's Rebind button");
            // And the new key is said; a successful rebind used to be silent.
            IReadOnlyList<Utterance> spoken = Array.Empty<Utterance>();
            for (int i = 0; i < 25 && !spoken.Any(u => u.Text.Contains("OpenHelp is now", StringComparison.Ordinal)); i++)
            {
                await t.Page.WaitForTimeoutAsync(200);
                spoken = await t.SpokenAsync();
            }
            Assert.True(spoken.Any(u => u.Text.Contains("OpenHelp is now", StringComparison.Ordinal)),
                "the rebind was not announced; heard: " + string.Join(" | ", spoken.Select(u => u.Text)));

            await t.PressAsync("Escape");
            Assert.True(await t.WaitForNoDialogAsync(), "Settings did not close.");

            await t.Page.Keyboard.PressAsync("Control+Alt+Shift+Y");
            Assert.True(await t.WaitForDialogAsync(),
                $"the rebound chord (shown as \"{cell}\") opened nothing");
            Assert.Contains("Help", await t.ModalStackAsync());
        }
        finally { ForgetRebindings(); }
    }

    /// <summary>
    /// "Escape cancels", as the panel says: the capture ends, nothing is rebound, and Settings
    /// stays open. Before the fix all three were false at once.
    /// </summary>
    [BrowserFact]
    public async Task Escape_cancels_the_capture_binds_nothing_and_leaves_Settings_open()
    {
        try
        {
            await using var t = await _fixture.NewPageAsync();
            await StartRebindAsync(t, "ToggleNarration");
            await t.Page.Keyboard.PressAsync("Escape");
            await t.Page.WaitForTimeoutAsync(500);

            Assert.Contains("Settings", await t.ModalStackAsync());
            Assert.False(await StillCapturingAsync(t), "the capture is still waiting for a key");
            Assert.True(await t.WaitForFocusAsync("rebind-ToggleNarration"),
                "after Escape, focus did not return to the row's Rebind button");

            var cell = await ShortcutCellAsync(t, "ToggleNarration");
            Assert.DoesNotContain("ESC", cell, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("N", cell);
        }
        finally { ForgetRebindings(); }
    }
}
