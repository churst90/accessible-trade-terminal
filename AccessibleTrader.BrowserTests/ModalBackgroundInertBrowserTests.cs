using Microsoft.Playwright;

namespace AccessibleTrader.BrowserTests;

/// <summary>
/// The modal background is <c>inert</c> while a dialog is open — measured in a real browser,
/// because the thing being fixed is a browser behaviour and not a markup claim.
///
/// <para>
/// Every dialog in this app already declares <c>aria-modal="true"</c>. That attribute is
/// ADVISORY: it asks the screen reader not to describe anything outside the dialog, and it does
/// nothing whatsoever about focus, about clicks, or about an assistive technology that walks the
/// tree anyway. On 2026-09-04 that gap was measured on this codebase — with the AT-SPI bridge
/// attached and Orca running, 6 of 14 modals lost focus to somewhere outside themselves (the
/// body, a heading behind the dialog, background toolbar buttons), and every one of those moves
/// carried an EMPTY JavaScript stack: dispatched by the embedder, not by page script. An app
/// cannot out-focus a mover it never sees. <c>inert</c> is the standard treatment and the only
/// one that answers this: it removes the destinations.
/// </para>
///
/// <para>
/// The background is an opt-IN list of tagged region roots rather than "everything except the
/// dialog", and the reason is in <see cref="LiveRegionInventoryBrowserTests"/>: the two ARIA
/// speech buffers are SIBLINGS of <c>&lt;main&gt;</c>, and they are the whole announcing channel
/// of a screen-reader-first application. <c>inert</c> strips a subtree out of the accessibility
/// tree, so inerting a common wrapper would not mute the background — it would silence the app
/// for as long as any dialog was open. That is asserted here directly, not assumed.
/// </para>
/// </summary>
[Collection("Terminal browser")]
public sealed class ModalBackgroundInertBrowserTests
{
    private readonly TerminalBrowserFixture _fixture;
    public ModalBackgroundInertBrowserTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    private const string SettingsTitle = "settings-title";

    private static Task<int> RegionCountAsync(TerminalPage t) =>
        t.Page.EvaluateAsync<int>("() => document.querySelectorAll('[data-background-region]').length");

    private static Task<int> InertRegionCountAsync(TerminalPage t) =>
        t.Page.EvaluateAsync<int>("() => document.querySelectorAll('[data-background-region][inert]').length");

    [BrowserFact]
    public async Task Cold_start_has_background_regions_and_none_of_them_is_inert()
    {
        // The vacuity floor for every case below, and it has two halves on purpose. "No region
        // is inert" is trivially true of a page that tagged nothing at all — which is exactly
        // what a build with the attribute dropped from the components would look like — so the
        // count of tagged regions is asserted first.
        await using var t = await _fixture.NewPageAsync();

        int regions = await RegionCountAsync(t);
        Assert.True(regions >= 5,
            $"Only {regions} background regions are tagged; the chrome that must go inert behind a "
            + "dialog is the header, toolbar, tab bar, chart <main>, indicator bar, status bar and footer.");
        Assert.Equal(0, await InertRegionCountAsync(t));
    }

    [BrowserFact]
    public async Task Opening_a_dialog_inerts_every_background_region()
    {
        await using var t = await _fixture.NewPageAsync();
        int regions = await RegionCountAsync(t);

        await t.PressAsync("F12");
        Assert.True(await t.WaitForFocusAsync(SettingsTitle), "Settings did not open on F12.");

        Assert.Equal(regions, await InertRegionCountAsync(t));
    }

    [BrowserFact]
    public async Task A_background_control_cannot_take_focus_while_a_dialog_is_open()
    {
        // The behaviour, not the attribute. `inert` is only worth shipping if the browser
        // actually refuses the focus — an attribute that an engine ignored would leave every
        // assertion above green and the user still able to land on the toolbar behind a dialog.
        await using var t = await _fixture.NewPageAsync();
        await t.PressAsync("F12");
        Assert.True(await t.WaitForFocusAsync(SettingsTitle), "Settings did not open on F12.");

        string outcome = await t.Page.EvaluateAsync<string>(@"() => {
            const btn = document.querySelector('[data-background-region] button');
            if (!btn) return 'no-background-button';
            btn.focus();
            return document.activeElement === btn ? 'took-focus' : 'refused';
        }");

        Assert.Equal("refused", outcome);
    }

    [BrowserFact]
    public async Task The_speech_live_regions_are_never_inside_the_inert_subtree()
    {
        // If this ever fails, the app has gone SILENT for screen-reader users whenever a dialog
        // is open, and every other test in this file would still be green.
        await using var t = await _fixture.NewPageAsync();
        await t.PressAsync("F12");
        Assert.True(await t.WaitForFocusAsync(SettingsTitle), "Settings did not open on F12.");

        string report = await t.Page.EvaluateAsync<string>(@"() => {
            return ['aria-speech-1', 'aria-speech-2'].map(id => {
                const el = document.getElementById(id);
                if (!el) return id + ':missing';
                return id + ':' + (el.closest('[inert]') ? 'inert' : 'live');
            }).join(',');
        }");

        Assert.Equal("aria-speech-1:live,aria-speech-2:live", report);
    }

    [BrowserFact]
    public async Task Closing_the_last_dialog_clears_inert_and_the_chart_takes_focus_again()
    {
        // The race this pins: CommandDispatcher publishes RequestChartFocusEvent from its own
        // ModalStack.Changed handler, and the call that CLEARS inert is a different subscriber
        // to the same event. Focus on an inert element is a silent no-op, so if the two land in
        // the wrong order the chart focus is simply lost and the user is left on <body> with no
        // dialog and no chart. focusElement retries a refused focus for exactly this reason;
        // this is the end-to-end statement of it.
        await using var t = await _fixture.NewPageAsync();
        await t.PressAsync("F12");
        Assert.True(await t.WaitForFocusAsync(SettingsTitle), "Settings did not open on F12.");

        await t.PressAsync("Escape");
        Assert.True(await t.WaitForNoDialogAsync(), "Escape did not close Settings.");

        Assert.Equal(0, await InertRegionCountAsync(t));
        Assert.True(await t.WaitForFocusAsync("chart-interact-zone"),
            $"Focus did not return to the chart; it is on {(await t.ActiveElementAsync()).Describe()}.");
    }
}
