using Microsoft.Playwright;
using System.Text.Json;

namespace AccessibleTrader.BrowserTests;

/// <summary>
/// The two structural routes a screen-reader user navigates by — landmarks and headings —
/// measured against the running app.
///
/// <para>
/// Both halves guard a defect that was live. The landmark half: <c>&lt;nav role="toolbar"&gt;</c>
/// left this app with THREE landmarks, none of them containing any of the ~41 toolbar controls,
/// because an explicit role overrides the element's implicit one. The heading half:
/// <c>HelpModal</c> had 471 lines and eighteen sections under ONE heading, so the app's own
/// keyboard reference — the screen where heading navigation matters most — offered the <c>H</c>
/// key a single stop.
/// </para>
///
/// <para>
/// <b>Two instruments, and the difference between them is load-bearing.</b> Playwright's
/// <c>GetByRole</c> is NOT the browser's accessibility tree: it is Playwright's own ARIA
/// implementation, evaluated in-page over the DOM (its injected script carries its own
/// <c>getAriaRole</c>, <c>kAriaLevelRoles</c> and, notably, its own <c>display: contents</c>
/// handling). Writing a role query and calling the result "what the accessibility tree says" is
/// the same mistake as calling a markup scan a behavioural test. So the heading case asserts
/// BOTH: the role query, and Chromium's real tree read over CDP
/// (<c>Accessibility.getFullAXTree</c>) — which is the one that can say a heading is present and
/// <c>ignored</c>, the failure mode the whole fix turns on.
/// </para>
///
/// <para>
/// <b>What is still not certified.</b> Blink's tree is not Orca's, and an unignored node in a
/// tree is not the same as an AT announcing it. A heading nested inside <c>&lt;summary&gt;</c> —
/// whose role is in the button family, whose children ARIA calls presentational — has to survive
/// AT-SPI as well. Firefox + Orca is this product's real target on Linux and that pass has NOT
/// been run; it is recorded as unverified in <c>docs/TODO.md</c> rather than implied by a green
/// suite here.
/// </para>
/// </summary>
[Collection("Terminal browser")]
public sealed class LandmarkAndHeadingBrowserTests
{
    private readonly TerminalBrowserFixture _fixture;
    public LandmarkAndHeadingBrowserTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    [BrowserFact]
    public async Task The_cold_start_page_exposes_the_landmarks_a_screen_reader_navigates_by()
    {
        await using var t = await _fixture.NewPageAsync();

        Assert.Equal(1, await t.Page.GetByRole(AriaRole.Banner).CountAsync());
        Assert.Equal(1, await t.Page.GetByRole(AriaRole.Main).CountAsync());
        Assert.Equal(1, await t.Page.GetByRole(AriaRole.Contentinfo).CountAsync());

        // The two the audit's finding removed. TouchNavBar is a third, C#-gated out of the DOM on
        // a desktop browser, so two is the desktop floor and not a typo.
        var navs = t.Page.GetByRole(AriaRole.Navigation);
        int navCount = await navs.CountAsync();
        Assert.True(navCount >= 2,
            $"The page exposes {navCount} navigation landmarks. The toolbar and the indicator bar " +
            "must each be one: with role=\"toolbar\" on them this app had ZERO, and the ~41 " +
            "toolbar controls could not be reached by landmark navigation at all.");

        // Two same-role landmarks are indistinguishable in a landmark list unless they are named.
        var navNames = new List<string>();
        for (int i = 0; i < navCount; i++)
            navNames.Add((await navs.Nth(i).GetAttributeAsync("aria-label") ?? "").Trim());
        Assert.DoesNotContain("", navNames);
        Assert.Equal(navNames.Count, navNames.Distinct().Count());

        // The status strip. Before it was a landmark neither D nor Tab reached it, and it holds
        // the PAPER badge — the one persistent sign that orders are simulated.
        //
        // The badge itself is NOT asserted, and that is a measured limit rather than an oversight:
        // this harness boots the WebHost in HostMode.Full, so DemoPolicy.AllowLiveTrading is TRUE
        // and `trading.paperTradingMode` defaults off — the badge does not render here at all.
        var status = t.Page.GetByRole(AriaRole.Region, new() { Name = "Terminal status" });
        Assert.Equal(1, await status.CountAsync());

        // INVERTED on 2026-09-04, and the inversion is the point. This used to assert that the
        // strip contained a role="status" live region, on the reasoning that an element cannot be
        // both a landmark and a live region so the role had to move to a child. The premise was
        // right; the conclusion — that it should be a live region at ALL — was wrong, and it cost
        // the user sentences. The strip mirrors what the speech buffers in MainLayout are already
        // announcing, so it was a second announcer for one sentence, and every screen reader
        // suppresses a live-region message that duplicates the one it just queued: whichever copy
        // arrived second was dropped, and when the strip's polite copy arrived FIRST the assertive
        // copy purged it and was then dropped as a duplicate of what it had purged, so the
        // sentence was spoken neither time. Measured on the AT-SPI bus: polite-first on 6 of 16
        // presses. The strip stays a NAMED LANDMARK — that is what makes it reachable, and it is
        // why removing aria-live costs a screen-reader user nothing.
        // See LiveRegionInventoryBrowserTests for the full inventory.
        Assert.Equal(0, await status.GetByRole(AriaRole.Status).CountAsync());
        Assert.Equal(0, await status.Locator("[aria-live]").CountAsync());

        // And the role that started all this is gone from the live document, not just from source.
        Assert.Equal(0, await t.Page.Locator("[role='toolbar']").CountAsync());
    }

    [BrowserFact]
    public async Task Every_help_section_is_a_heading_in_chromiums_own_accessibility_tree()
    {
        await using var t = await _fixture.NewPageAsync();

        await t.PressAsync("F1");
        Assert.True(await t.WaitForDialogAsync(), "F1 did not open the Help dialog.");

        var dialog = t.TopDialog();
        int sections = await dialog.Locator("details").CountAsync();
        Assert.True(sections >= 18,
            $"Help renders {sections} disclosure sections; it had 18 when this was written, so the " +
            "heading counts below are being compared against a dialog that has shrunk.");

        // ── instrument 1: the role query ──────────────────────────────────────
        Assert.Equal(1, await dialog.GetByRole(AriaRole.Heading, new() { Level = 2 }).CountAsync());
        int byRole = await dialog.GetByRole(AriaRole.Heading, new() { Level = 3 }).CountAsync();
        Assert.Equal(sections, byRole);

        // Inside the SUMMARY, not merely somewhere in the dialog. A count alone stays green if the
        // headings move into the collapsed bodies and the titles go back to bold text.
        Assert.Equal(byRole, await dialog.Locator("summary > h3").CountAsync());

        // ── instrument 2: Chromium's real accessibility tree ──────────────────
        var headings = await AxHeadingsAsync(t);
        var levelThree = headings.Where(h => h.Level == 3 && !h.Ignored).ToList();
        Assert.Equal(sections, levelThree.Count);
        Assert.DoesNotContain("", levelThree.Select(h => h.Name.Trim()));
        Assert.Equal(levelThree.Count, levelThree.Select(h => h.Name.Trim()).Distinct().Count());

        // The app's own h1 is NOT in the tree while a dialog is open, and that is the modal
        // background treatment working rather than a heading that went missing: the header is
        // one of the eight `data-background-region` roots, and `inert` removes a subtree from
        // the accessibility tree outright (see ModalBackgroundInertBrowserTests).
        //
        // This assertion used to read `Equal(1, …)` and it was correct when it was written —
        // before 2026-09-04 the page outline behind a dialog was still exposed, which is the
        // thing `aria-modal` asks a screen reader to ignore and cannot enforce. It is re-aimed
        // rather than deleted, because the claim it was making — that the dialog's h2 continues
        // ONE page outline and does not start a second — is still worth pinning; it is just
        // only observable with the dialog closed. So: none while open, exactly one once closed.
        Assert.Equal(0, headings.Count(h => h.Level == 1 && !h.Ignored));

        await t.PressAsync("Escape");
        Assert.True(await t.WaitForNoDialogAsync(), "Escape did not close Help.");
        Assert.Equal(1, (await AxHeadingsAsync(t)).Count(h => h.Level == 1 && !h.Ignored));
    }

    /// <summary>
    /// The headings must not have become tab stops. Both of this repo's Tab counters would
    /// happily report a dialog twice as long to walk: adding <c>tabindex</c> to the eighteen
    /// headings parks focus on elements where Enter does nothing, and no existing gate looks.
    /// </summary>
    [BrowserFact]
    public async Task The_help_headings_did_not_add_tab_stops()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.PressAsync("F1");
        Assert.True(await t.WaitForDialogAsync());

        // 18 <summary> + the Close button. The <h2> is deliberately tabindex="-1".
        Assert.Equal(19, await t.TabStopCountInTopDialogAsync());
    }

    // ── reading the browser's own tree ────────────────────────────────────────

    private readonly record struct AxHeading(int Level, string Name, bool Ignored);

    /// <summary>
    /// Every heading node in Chromium's accessibility tree for the whole page, with the
    /// <c>ignored</c> flag Playwright's role queries cannot report. Whole-page rather than
    /// dialog-scoped because <c>getFullAXTree</c> is a page-level command; the app shell
    /// contributes exactly one h1 and the speech prompt's h2, both asserted above.
    /// </summary>
    private static async Task<List<AxHeading>> AxHeadingsAsync(TerminalPage t)
    {
        var cdp = await t.Page.Context.NewCDPSessionAsync(t.Page);
        await cdp.SendAsync("Accessibility.enable");
        var result = await cdp.SendAsync("Accessibility.getFullAXTree");
        Assert.NotNull(result);

        var found = new List<AxHeading>();
        foreach (var node in result!.Value.GetProperty("nodes").EnumerateArray())
        {
            if (!node.TryGetProperty("role", out var role) ||
                role.GetProperty("value").GetString() != "heading") continue;

            int level = 0;
            if (node.TryGetProperty("properties", out var props))
                foreach (var p in props.EnumerateArray())
                    if (p.GetProperty("name").GetString() == "level")
                        level = p.GetProperty("value").GetProperty("value").GetInt32();

            string name = node.TryGetProperty("name", out var n)
                ? n.GetProperty("value").GetString() ?? "" : "";
            bool ignored = node.TryGetProperty("ignored", out var ig) && ig.GetBoolean();

            found.Add(new AxHeading(level, name, ignored));
        }

        Assert.NotEmpty(found);   // an empty tree would make every count above agree at zero
        return found;
    }
}
