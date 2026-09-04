using Microsoft.Playwright;

namespace AccessibleTrader.BrowserTests;

/// <summary>
/// Two dialogs open at once, in a real browser.
///
/// <para>
/// Until 2026-09-02 no test anywhere in this repository opened two dialogs, and the code had two
/// different ideas of which one was on top: <c>CommandDispatcher</c> kept a stack in OPEN order
/// for Escape, while <c>keyboard.js</c>'s Tab trap took the last visible dialog in DOM order —
/// and DOM order is the constant render order in <c>MainLayout.razor</c>, where Help is rendered
/// before nineteen other modals. So Settings then F1 gave a user Escape that closed Help and a
/// Tab that was trapped in Settings, underneath it. The review's interim mitigation (prefer the
/// dialog that CONTAINS focus) left two holes, both demonstrated red here before the fix:
/// </para>
///
/// <list type="number">
///   <item>Focus that had left every dialog (a click on the overlay, a blur) was rehomed into the
///   DOM-last dialog — the one underneath — while the top dialog still claimed
///   <c>aria-modal="true"</c>, so the screen reader would not describe where the user now was.</item>
///   <item>Closing the top dialog left focus on <c>&lt;body&gt;</c>: the dispatcher restored focus
///   to the chart only when the LAST modal closed, and nothing restored it to the dialog beneath.</item>
/// </list>
///
/// <para>
/// The fix is the one <c>ModalStack</c> in Core, pushed whole to the browser and resolved to a
/// dialog element by <c>data-modal-name</c>; these cases are what it is held to. Both routes
/// here are cold-start keyboard routes (F12, then F1 — <c>OpenHelp</c> is in the dispatcher's
/// <c>allowedWhileModalOpen</c> list), plus the one in-app case of a dialog opening another:
/// Settings' Appearance tab opening the theme editor. Every containment answer names the dialog
/// it means by <c>aria-labelledby</c>, because "the top dialog" is exactly what is in dispute
/// when two are open.
/// </para>
/// </summary>
[Collection("Terminal browser")]
public sealed class StackedModalBrowserTests
{
    private readonly TerminalBrowserFixture _fixture;
    public StackedModalBrowserTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    private const string SettingsTitle = "settings-title";
    private const string SettingsOpener = "tab-general";   // where focus stands when F1 is pressed
    private const string HelpTitle     = "help-title";
    private const string ThemeTitle    = "theme-editor-title";
    // The visible text IS the accessible name since the 2026-09-03 settings restructure (the
    // button carries a title, no aria-label), so focus return is checked by the element's id.
    private const string NewThemeName  = "New theme";
    private const string NewThemeId    = "s-theme-new";

    /// <summary>
    /// F12, two Tabs into Settings (past the search box, onto the General tab button), then F1:
    /// Help stacked on top of Settings, with Settings LAST in the DOM. The Tabs are deliberate:
    /// the return target after Help closes is then a specific control — a heading is also where
    /// a dialog puts focus on open, so "back on the heading" could be imitated by a re-focus.
    /// Two rather than one for history as much as anything: when this route was written, F1
    /// pressed INSIDE the search input did nothing (the keydown handler's form-control guard
    /// swallowed every F-key). That is fixed and pinned by FunctionKeysInFormControlsBrowserTests;
    /// the return-target argument above is the reason the second Tab stays.
    /// </summary>
    private static async Task<TerminalPage> OpenSettingsThenHelpAsync(TerminalPage t)
    {
        await t.PressAsync("F12");
        Assert.True(await t.WaitForFocusAsync(SettingsTitle), "Settings did not open on F12.");
        await t.PressAsync("Tab");
        await t.PressAsync("Tab");
        Assert.True(await t.WaitForFocusAsync(SettingsOpener),
            $"Two Tabs from Settings' heading should land on the General tab; focus is on {(await t.ActiveElementAsync()).Describe()}.");
        await t.PressAsync("F1");
        Assert.True(await t.WaitForFocusAsync(HelpTitle),
            $"Help did not open on F1 over Settings; focus is on {(await t.ActiveElementAsync()).Describe()}.");
        Assert.Equal(2, (await t.VisibleDialogIdsAsync()).Count);
        return t;
    }

    [BrowserFact]
    public async Task Escape_closes_only_the_top_dialog_and_leaves_the_one_beneath_open()
    {
        await using var t = await _fixture.NewPageAsync();
        await OpenSettingsThenHelpAsync(t);

        await t.PressAsync("Escape");

        Assert.True(await t.WaitForDialogGoneAsync(HelpTitle), "Help did not close on Escape.");
        Assert.NotEqual(FocusPlace.NoDialogSeen, await t.FocusRelativeToDialogAsync(SettingsTitle)); // still rendered
        Assert.Equal(1, await t.OpenModalCountAsync());
        Assert.Single(await t.VisibleDialogIdsAsync());
    }

    [BrowserFact]
    public async Task Closing_the_top_dialog_returns_focus_to_the_dialog_beneath_it()
    {
        await using var t = await _fixture.NewPageAsync();
        await OpenSettingsThenHelpAsync(t);

        await t.PressAsync("Escape");
        Assert.True(await t.WaitForDialogGoneAsync(HelpTitle), "Help did not close on Escape.");

        // Focus was on Settings' General tab when F1 was pressed, so that is where it should
        // return. Waited on, not sampled: the closing render is a race (see the Escape theory
        // in ModalBrowserContractTests).
        Assert.True(await t.WaitForFocusAsync(SettingsOpener),
            $"After closing Help over Settings, focus is on {(await t.ActiveElementAsync()).Describe()} " +
            "rather than back in Settings. The dispatcher only restores focus when the LAST modal " +
            "closes; a stacked close leaves the user on <body> with a dialog still open.");
        Assert.Equal(FocusPlace.Inside, await t.FocusRelativeToDialogAsync(SettingsTitle));
    }

    [BrowserFact]
    public async Task Tab_from_outside_every_dialog_lands_in_the_top_dialog_not_the_one_beneath()
    {
        await using var t = await _fixture.NewPageAsync();
        await OpenSettingsThenHelpAsync(t);

        // A click on the overlay does this; so does any script blur. Focus is now on <body>,
        // inside no dialog, and the trap has to decide which dialog to rehome it into.
        await t.BlurActiveElementAsync();
        await t.PressAsync("Tab");

        var here = await t.ActiveElementAsync();
        var inHelp = await t.FocusRelativeToDialogAsync(HelpTitle);
        Assert.True(inHelp != FocusPlace.NoDialogSeen, "Help is no longer rendered — it should still be open.");
        Assert.True(inHelp == FocusPlace.Inside,
            $"Tab from <body> with Help open over Settings put focus on {here.Describe()}, which is " +
            "not in Help. The trap rehomed escaped focus into the DOM-last dialog (Settings) — " +
            "underneath the dialog whose aria-modal=\"true\" the screen reader is honouring.");

        // Shift+Tab, the same way.
        await t.BlurActiveElementAsync();
        await t.PressAsync("Shift+Tab");
        here = await t.ActiveElementAsync();
        Assert.True(await t.FocusRelativeToDialogAsync(HelpTitle) == FocusPlace.Inside,
            $"Shift+Tab from <body> with Help open over Settings put focus on {here.Describe()}, not in Help.");
    }

    [BrowserFact]
    public async Task Tab_and_ShiftTab_stay_in_the_top_dialog_from_its_heading()
    {
        await using var t = await _fixture.NewPageAsync();
        await OpenSettingsThenHelpAsync(t);

        for (int i = 0; i < 8; i++)
        {
            await t.PressAsync("Tab");
            var here = await t.ActiveElementAsync();
            Assert.True(await t.FocusRelativeToDialogAsync(HelpTitle) == FocusPlace.Inside,
                $"Tab #{i + 1} with Help over Settings put focus on {here.Describe()}, outside Help.");
        }
        for (int i = 0; i < 8; i++)
        {
            await t.PressAsync("Shift+Tab");
            var here = await t.ActiveElementAsync();
            Assert.True(await t.FocusRelativeToDialogAsync(HelpTitle) == FocusPlace.Inside,
                $"Shift+Tab #{i + 1} with Help over Settings put focus on {here.Describe()}, outside Help.");
        }
    }

    [BrowserFact]
    public async Task The_app_exposes_one_modal_stack_in_open_order_and_the_trap_reads_it()
    {
        // The dispatcher's Escape stack and the Tab trap's idea of "top" must be the SAME stack.
        // This reads the JS side; ModalStackTests reads the C# side; both are fed by the same
        // ModalStateChangedEvent, and the names must be the ones the dialogs wear.
        await using var t = await _fixture.NewPageAsync();
        await OpenSettingsThenHelpAsync(t);

        Assert.Equal(new[] { "Settings", "Help" }, await t.ModalStackAsync());

        await t.PressAsync("Escape");
        Assert.True(await t.WaitForDialogGoneAsync(HelpTitle));
        Assert.Equal(new[] { "Settings" }, await t.ModalStackAsync());

        // GlobalInputService drops a keystroke identical to the last one within 50 ms — the
        // window-JS-vs-element-Blazor dedupe. This harness can land the second Escape inside
        // that window; a person cannot. Pace it like a person.
        await Task.Delay(250);
        await t.PressAsync("Escape");
        Assert.True(await t.WaitForNoDialogAsync(),
            "Settings did not close on the second Escape. (If it closed on a 250 ms pause but " +
            "not without one, that is GlobalInputService's 50 ms dedupe, not the stack.)");
        Assert.Empty(await t.ModalStackAsync());
        Assert.Equal(0, await t.OpenModalCountAsync());
    }

    [BrowserFact]
    public async Task Theme_editor_over_Settings_returns_focus_to_the_button_that_opened_it()
    {
        // The one place in the app where a dialog opens another dialog by its own control, and
        // the WCAG 2.4.3 shape of the question: when the child closes, the user should be back
        // on the control they activated, not on the parent's heading and not on <body>.
        await using var t = await _fixture.NewPageAsync();
        await t.PressAsync("F12");
        Assert.True(await t.WaitForFocusAsync(SettingsTitle), "Settings did not open on F12.");
        Assert.True(await t.ClickTopDialogTabAsync("Appearance"), "Settings has no Appearance tab.");

        // The tab click is a Blazor round-trip; wait for the panel's button rather than count it.
        var newTheme = t.Page.GetByRole(AriaRole.Button, new() { Name = NewThemeName, Exact = true });
        try { await newTheme.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 }); }
        catch (TimeoutException) { }
        catch (PlaywrightException) { }
        Assert.True(await newTheme.CountAsync() > 0, $"No button named \"{NewThemeName}\" on the Appearance tab.");
        await newTheme.First.ClickAsync();
        Assert.True(await t.WaitForFocusAsync(ThemeTitle),
            $"The theme editor did not open, or did not take focus; focus is on {(await t.ActiveElementAsync()).Describe()}.");
        Assert.Equal(2, (await t.VisibleDialogIdsAsync()).Count);

        await t.PressAsync("Escape");
        Assert.True(await t.WaitForDialogGoneAsync(ThemeTitle), "The theme editor did not close on Escape.");
        Assert.NotEqual(FocusPlace.NoDialogSeen, await t.FocusRelativeToDialogAsync(SettingsTitle)); // Settings still up

        bool back = false;
        try
        {
            await t.Page.WaitForFunctionAsync(
                "id => document.activeElement && document.activeElement.id === id",
                NewThemeId, new PageWaitForFunctionOptions { Timeout = 5_000 });
            back = true;
        }
        catch (TimeoutException) { }
        catch (PlaywrightException) { }
        Assert.True(back,
            $"After closing the theme editor, focus is on {(await t.ActiveElementAsync()).Describe()} " +
            $"rather than back on the \"{NewThemeName}\" button that opened it.");
    }

    [BrowserFact]
    public async Task Pressing_F1_twice_does_not_leave_a_phantom_modal_on_the_stack()
    {
        // F1 is allowed while a modal is open and HelpModal.ShowAsync has no visibility guard, so
        // F1, F1 publishes two opens for one dialog. The old stack held [Help, Help]; one Escape
        // closed the dialog and left an entry no dialog answered to, after which Escape targeted
        // an invisible modal and every chart command was refused until reload. Found by the
        // 2026-09-02 modal-specialist review, by tracing; this is the observation.
        await using var t = await _fixture.NewPageAsync();
        await t.PressAsync("F1");
        Assert.True(await t.WaitForFocusAsync(HelpTitle), "Help did not open on F1.");
        await Task.Delay(250);                       // GlobalInputService's 50 ms dedupe
        await t.PressAsync("F1");
        await Task.Delay(250);
        Assert.Equal(new[] { "Help" }, await t.ModalStackAsync());

        await t.PressAsync("Escape");
        Assert.True(await t.WaitForNoDialogAsync(), "Help did not close on Escape.");
        Assert.Empty(await t.ModalStackAsync());
        Assert.Equal(0, await t.OpenModalCountAsync());

        // The proof that nothing is stuck: a dialog other than Help still opens, which the
        // dispatcher refuses while it believes a modal is open.
        await Task.Delay(250);
        await t.PressAsync("F12");
        Assert.True(await t.WaitForFocusAsync(SettingsTitle),
            "F12 did nothing after F1, F1, Escape — the dispatcher still thinks a modal is open.");
    }
}
