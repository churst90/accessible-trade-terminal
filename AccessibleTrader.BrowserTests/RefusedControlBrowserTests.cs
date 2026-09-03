using Microsoft.Playwright;

namespace AccessibleTrader.BrowserTests;

/// <summary>
/// A control that cannot act right now must still BE there.
///
/// <para>
/// Until 2026-09-02 the toolbar a user met on a cold start was five controls shorter than the
/// toolbar in the markup: Load and the four pan/zoom buttons carried native <c>disabled</c>
/// until a chart was loaded, and a natively disabled button is not "greyed out" to a screen
/// reader — it is absent from the tab order and absent from NVDA's and JAWS's button lists.
/// Nothing said the controls existed and nothing said what would bring them back.
/// </para>
///
/// <para>
/// This is the half of the fix that only a real browser can see. bUnit renders the attributes;
/// it cannot say whether Chromium puts the element in the tab order, what the accessibility
/// tree calls it, or whether the click the component now has to swallow actually arrives. So
/// this drives the real toolbar of the real app in real Chromium, on the cold-start page where
/// the defect lived.
/// </para>
/// </summary>
[Collection("Terminal browser")]
public sealed class RefusedControlBrowserTests
{
    private readonly TerminalBrowserFixture _fixture;
    public RefusedControlBrowserTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    private const string PanLeft = "button[aria-label='Pan chart left']";

    [BrowserFact]
    public async Task A_refused_toolbar_button_is_still_in_the_tab_order_and_still_has_a_name()
    {
        await using var t = await _fixture.NewPageAsync();

        var btn = t.Page.Locator(PanLeft);
        Assert.Equal(1, await btn.CountAsync());

        // Not natively disabled — that is the whole point. Chromium's own accessibility tree is
        // the authority here rather than the attribute: a disabled button is reported with no
        // focusability at all, and the sweep that walks this page would have skipped it.
        Assert.False(await btn.EvaluateAsync<bool>("e => e.disabled"));
        Assert.Equal("true", await btn.GetAttributeAsync("aria-disabled"));

        // Reachable by keyboard, which is the property `disabled` removed.
        await btn.FocusAsync();
        Assert.True(await btn.EvaluateAsync<bool>("e => document.activeElement === e"),
            "The refused toolbar button cannot take focus, so a keyboard user cannot find it.");

        // And it says why, through the description a screen reader reads after the name and the
        // "unavailable" state. Resolved the way the browser resolves it, from the live DOM.
        string description = await btn.EvaluateAsync<string>(@"e => {
            const ids = (e.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean);
            return ids.map(id => (document.getElementById(id)?.textContent || '').trim())
                      .filter(Boolean).join(' ');
        }");
        Assert.Contains("No chart is loaded", description, StringComparison.OrdinalIgnoreCase);
    }

    [BrowserFact]
    public async Task Pressing_it_says_why_rather_than_doing_nothing()
    {
        // `aria-disabled` does not stop activation the way `disabled` did, so the refusal has to
        // be real AND audible. A button that swallows the press in silence is indistinguishable
        // from a broken binding — the failure the whole feedback contract exists to forbid.
        //
        // Activated by KEYBOARD, and not only because that is how this product is used.
        // Playwright's own actionability check treats aria-disabled="true" as "element is not
        // enabled" and will not click it — it waits out the full 30 s timeout. That is
        // Playwright's policy, not the browser's: Chromium delivers both the click and the
        // Enter, because aria-disabled is advisory and changes no native behaviour. Worth
        // knowing before someone reads a `ClickAsync` timeout here as the app being broken.
        await using var t = await _fixture.NewPageAsync();
        await t.ClearSpokenAsync();

        await t.Page.Locator(PanLeft).FocusAsync();
        await t.PressAsync("Enter");
        var spoken = await t.WaitForSpeechAsync();

        Assert.Contains(spoken, u => u.Text.Contains("No chart is loaded", StringComparison.OrdinalIgnoreCase));
    }

    [BrowserFact]
    public async Task The_reason_span_is_invisible_and_the_button_carries_no_stray_text()
    {
        // The reason lives in a visually-hidden sibling span. Two ways that can go wrong and
        // both are silent: the scoped stylesheet not shipping (the sentence appears on screen
        // under every gated button), or the span landing INSIDE the button (the accessible name
        // becomes "Pan left No chart is loaded yet…", which breaks WCAG 2.5.3 Label in Name and
        // renames the control in the screen reader's button list).
        await using var t = await _fixture.NewPageAsync();

        var btn = t.Page.Locator(PanLeft);
        Assert.DoesNotContain("No chart is loaded", await btn.InnerTextAsync(),
            StringComparison.OrdinalIgnoreCase);

        var reason = t.Page.Locator("#" + await btn.GetAttributeAsync("aria-describedby"));
        // Proved red by emptying ToolbarIconButton.razor.css. Note that weakening any ONE of
        // the four properties in that rule is NOT enough to trip this — position, width/height,
        // overflow and clip each hide the span on their own, so they mask each other. That
        // redundancy is deliberate for a visually-hidden utility; it just means the honest
        // sabotage for this guard is the whole stylesheet, not one line of it.
        var box = await reason.BoundingBoxAsync();
        Assert.True(box is null || (box.Width <= 2 && box.Height <= 2),
            $"The blocked-reason span is {box?.Width}x{box?.Height} on screen — the scoped "
            + "stylesheet that hides it did not ship.");
    }
}
