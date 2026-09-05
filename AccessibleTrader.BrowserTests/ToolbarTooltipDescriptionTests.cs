using Microsoft.Playwright;

namespace AccessibleTrader.BrowserTests;

/// <summary>
/// Does a toolbar button still SAY what it does?
///
/// <para>
/// Every <c>ToolbarIconButton</c> carries a <c>Tooltip</c> — the full phrase plus the chord —
/// and that tooltip reaches a screen reader as the button's accessible DESCRIPTION, which Orca,
/// NVDA and JAWS all read after the name. It is the only place the keyboard shortcut is spoken
/// while arrowing along the toolbar.
/// </para>
///
/// <para>
/// On 2026-09-02 the gated-button pass gave every toolbar button an unconditional
/// <c>aria-describedby</c> pointing at an always-rendered reason span that is EMPTY while the
/// button works. That deleted every one of those tooltips, silently, because the accessible
/// description is computed from <c>aria-describedby</c> first and from <c>title</c> only when
/// that source is absent — and a describedby resolving to the empty string counts as present.
/// Measured in Chromium 2026-09-03:
/// </para>
/// <code>
///   title only, no describedby     -> description "Open trading dashboard (Alt+T)"
///   describedby -> empty span      -> description NULL
///   describedby -> span with text  -> description that text
/// </code>
/// <para>
/// The user reported it as "I'm not hearing the tooltip help messages any longer when I arrow
/// over the buttons, except for the first button" — and the two he could still hear something
/// from, Objects and Levels, are exactly the two whose <c>AriaLabel</c> says more than their
/// <c>Label</c>. He was hearing the NAME, not the tooltip.
/// </para>
///
/// <para>
/// THIS TEST PINS THE PROPERTY, NOT A SPELLING. It does not look for an attribute; it asks
/// Chromium's own accessibility tree what the description of the button IS, which is the only
/// question a screen-reader user's experience turns on. A future refactor that moves the
/// tooltip from <c>title</c> to <c>aria-description</c> would keep this green, and any change
/// that suppresses it again turns it red however it is spelled.
/// </para>
///
/// <para>
/// The expectations are written out by hand rather than read from Toolbar.razor: a table
/// generated from the source agrees with the source by construction and can never disagree
/// with it. Same argument as <see cref="ModalRoute"/>.
/// </para>
/// </summary>
[Collection("Terminal browser")]
public sealed class ToolbarTooltipDescriptionTests
{
    private readonly TerminalBrowserFixture _fixture;
    public ToolbarTooltipDescriptionTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Accessible name -> the description it must expose, for buttons that are UNGATED on a
    /// cold start. The five gated ones (Load chart, and the four pan/zoom) are covered by the
    /// second test, which asserts the opposite half of the same contract.
    ///
    /// <para>
    /// THE CONVENTION, set by Cody on 2026-09-03 and uniform across the top toolbar: the visible
    /// label is the thing's own name and IS the accessible name (no AriaLabel override, so WCAG
    /// 2.5.3 Label-in-Name holds by construction), and the tooltip is
    /// "&lt;verb&gt; &lt;thing&gt;, &lt;chord spelled out in words&gt;". Spelled out, not
    /// "Ctrl+Alt+Shift+J": the chord is read by a speech synthesiser, and how each one voices
    /// "+" is not something this app can rely on.
    /// </para>
    /// </summary>
    public static TheoryData<string, string> UngatedButtons => new()
    {
        { "Object tree",     "Open object tree, Alt plus O" },
        { "Drawings",        "Open drawing tools, Alt plus D" },
        { "Sound designer",  "Open sound designer, Alt plus W" },
        { "Trade dashboard", "Open trade dashboard, Alt plus T" },
        // "Order book" left this list on 2026-09-05: the button is gated on the current
        // provider having a book, and this harness loads no providers (the plugin DLLs under
        // bin/ are refused by the trust allow-list), so like Deposit it is absent here.
        { "Strategies",      "Open strategies, Alt plus S" },
        { "Watch lists",     "Open watch lists and screener, Alt plus M" },
        { "Levels",          "Open level respect report, Alt plus R" },
        { "Trade journal",   "Open trade journal, Control plus Alt plus Shift plus J" },
        { "AI analyst",      "Open AI analyst, Control plus Alt plus Shift plus A" },
        { "Alerts",          "Open alerts, Alt plus J" },
        { "API keys",        "Open API keys, Alt plus K" },
        { "Save workspace",  "Save workspace, Control plus Alt plus Shift plus W" },
        { "Load workspace",  "Load workspace, Control plus Alt plus W" },
        { "Settings",        "Open settings, F12" },
        { "Help",            "Open help, F1" },
    };

    [BrowserTheory]
    [MemberData(nameof(UngatedButtons))]
    public async Task An_available_toolbar_button_describes_itself_with_its_tooltip(
        string accessibleName, string expectedDescription)
    {
        await using var t = await _fixture.NewPageAsync();

        var buttons = await AxButtonsAsync(t);

        // The floor. "No button by that name" and "the button has no description" are different
        // failures, and a sweep that found nothing at all must not read as a pass.
        Assert.True(buttons.ContainsKey(accessibleName),
            $"No button named '{accessibleName}' in the accessibility tree. Found: "
            + string.Join(" | ", buttons.Keys.OrderBy(k => k)));

        Assert.Equal(expectedDescription, buttons[accessibleName]);
    }

    /// <summary>
    /// The other half: while a button IS refused, the reason must displace the tooltip. Cold
    /// start means no chart, so ChartDataGate is closed on all four pan/zoom buttons — and the
    /// reason is the more urgent sentence, so it is the one that must be described.
    /// </summary>
    [BrowserFact]
    public async Task A_refused_toolbar_button_describes_the_reason_instead_of_its_tooltip()
    {
        await using var t = await _fixture.NewPageAsync();

        var buttons = await AxButtonsAsync(t);

        const string expected = "No chart is loaded yet. Choose a symbol and press Load.";
        foreach (var name in new[] { "Pan left", "Pan right", "Zoom in", "Zoom out" })
        {
            Assert.True(buttons.ContainsKey(name),
                $"No button named '{name}' in the accessibility tree. Found: "
                + string.Join(" | ", buttons.Keys.OrderBy(k => k)));
            Assert.Equal(expected, buttons[name]);
        }
    }

    /// <summary>
    /// Every non-ignored button in Chromium's own accessibility tree, name -> description.
    /// A node with no description at all maps to the empty string, so "the tooltip vanished"
    /// and "the tooltip changed" both fail with the value that was actually computed.
    /// </summary>
    private static async Task<Dictionary<string, string>> AxButtonsAsync(TerminalPage t)
    {
        var cdp = await t.Page.Context.NewCDPSessionAsync(t.Page);
        await cdp.SendAsync("Accessibility.enable");
        var result = await cdp.SendAsync("Accessibility.getFullAXTree");
        Assert.NotNull(result);

        var found = new Dictionary<string, string>();
        foreach (var node in result!.Value.GetProperty("nodes").EnumerateArray())
        {
            if (!node.TryGetProperty("role", out var role) ||
                role.GetProperty("value").GetString() != "button") continue;
            if (node.TryGetProperty("ignored", out var ig) && ig.GetBoolean()) continue;

            string name = node.TryGetProperty("name", out var n)
                ? n.GetProperty("value").GetString() ?? "" : "";
            if (name.Length == 0) continue;

            string desc = node.TryGetProperty("description", out var d)
                ? d.GetProperty("value").GetString() ?? "" : "";

            found[name] = desc;
        }

        Assert.NotEmpty(found);   // an empty tree would make every lookup above fail for the wrong reason
        return found;
    }
}
