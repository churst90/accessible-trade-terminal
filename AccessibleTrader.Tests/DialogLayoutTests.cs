using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests;

/// <summary>
/// The dialog layout pass of 2026-11-21. Every dialog was opened at 1280x760, 1920x1080 and
/// 390x800 and measured; these are the rules that fixed what was found, pinned in both copies
/// of app.css (and the Trading Dashboard's own sheet) so a later edit cannot quietly drop one.
///
/// Each check reads the value the cascade ends on: the LAST rule for that selector that sets
/// the property. A later rule putting the old value back fails here, as surely as deleting
/// the fix would.
/// </summary>
public sealed class DialogLayoutTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    public static TheoryData<string> Sheets => new()
    {
        Path.Combine("AccessibleTrader.BlazorClient", "wwwroot", "app.css"),
        Path.Combine("AccessibleTrader.WebHost", "wwwroot", "app.css"),
    };

    private static string Css(string relative)
    {
        string full = Path.Combine(RepoRoot(), relative);
        Assert.True(File.Exists(full), $"{relative} is missing; the scan would pass on nothing");
        return DialogContrastScanTests.StripComments(File.ReadAllText(full));
    }

    /// <summary>The value the cascade ends on for <paramref name="property"/> under exactly
    /// <paramref name="selector"/>, or null if no rule for that selector sets it.</summary>
    internal static string? FinalValue(string css, string selector, string property)
    {
        string? value = null;
        foreach (Match rule in Regex.Matches(css, @"([^{}]+)\{([^{}]*)\}"))
        {
            var selectors = rule.Groups[1].Value.Split(',').Select(s => Regex.Replace(s.Trim(), @"\s+", " "));
            if (!selectors.Contains(selector)) continue;
            foreach (var decl in rule.Groups[2].Value.Split(';'))
            {
                int colon = decl.IndexOf(':');
                if (colon < 0) continue;
                if (decl[..colon].Trim() == property)
                    value = Regex.Replace(decl[(colon + 1)..].Trim(), @"\s+", " ");
            }
        }
        return value;
    }

    private const string Field = @".modal-content input:not([type=""checkbox""]):not([type=""radio""]):not([type=""range""]):not([type=""color""])";

    [Theory]
    [MemberData(nameof(Sheets))]
    public void A_table_cell_never_squeezes_to_one_letter(string sheet)
    {
        // overflow-wrap:anywhere on the dialog let the positions table shrink each column to a
        // letter: "Instr / umen / t".
        Assert.Equal("break-word", FinalValue(Css(sheet), ".modal-content td", "overflow-wrap"));
        Assert.Equal("break-word", FinalValue(Css(sheet), ".modal-content th", "overflow-wrap"));
    }

    [Theory]
    [MemberData(nameof(Sheets))]
    public void A_wide_table_scrolls_in_its_own_box(string sheet)
    {
        Assert.Equal("auto", FinalValue(Css(sheet), ".table-scroll", "overflow-x"));
        Assert.Equal("100%", FinalValue(Css(sheet), ".table-scroll", "max-width"));
    }

    [Theory]
    [MemberData(nameof(Sheets))]
    public void A_long_label_or_fieldset_does_not_widen_the_dialog(string sheet)
    {
        // The page-wide label rule is nowrap for the toolbar; Alerts, Settings Narration, Market
        // events and the strategy builder all scrolled sideways on a phone-width window.
        Assert.Equal("normal", FinalValue(Css(sheet), ".modal-content label", "white-space"));
        Assert.Equal("0", FinalValue(Css(sheet), ".modal-content fieldset", "min-width"));
    }

    [Theory]
    [MemberData(nameof(Sheets))]
    public void A_field_is_a_target_you_can_hit_and_stays_inside_its_row(string sheet)
    {
        Assert.Equal("28px", FinalValue(Css(sheet), Field, "min-height"));
        Assert.Equal("100%", FinalValue(Css(sheet), @".modal-content input:not([type=""checkbox""]):not([type=""radio""])", "max-width"));
        Assert.Equal("border-box", FinalValue(Css(sheet), ".modal-content select", "box-sizing"));
    }

    [Theory]
    [MemberData(nameof(Sheets))]
    public void Dialog_tabs_wrap_and_a_small_window_keeps_a_gutter(string sheet)
    {
        Assert.Equal("wrap", FinalValue(Css(sheet), @".modal-content [role=""tablist""]", "flex-wrap"));
        Assert.Equal("16px", FinalValue(Css(sheet), ".modal-overlay", "padding-left"));
        Assert.Equal("0", FinalValue(Css(sheet), ".modal-content", "min-width"));
    }

    [Theory]
    [MemberData(nameof(Sheets))]
    public void A_file_picker_button_is_drawn_like_the_dialogs_buttons(string sheet)
    {
        // The browser's own was a white slab on the dark panel (My Data's "Choose File").
        Assert.Equal("var(--text-primary)", FinalValue(Css(sheet), @".modal-content input[type=""file""]::file-selector-button", "color"));
        Assert.StartsWith("color-mix(", FinalValue(Css(sheet), @".modal-content input[type=""file""]::file-selector-button", "background-color"));
    }

    [Fact]
    public void The_trading_dashboard_stacks_and_wraps_on_a_narrow_window()
    {
        string razor = File.ReadAllText(Path.Combine(RepoRoot(), "AccessibleTrader.BlazorClient.Components", "TradingDashboardModal.razor"));
        string css = DialogContrastScanTests.StripComments(
            string.Join("\n", Regex.Matches(razor, @"<style>(.*?)</style>", RegexOptions.Singleline).Select(m => m.Groups[1].Value)));

        // The order ticket and the account tables were two equal halves; the tables need the
        // room. Wide: a narrow ticket beside the tables. Under 900px (the last rule, inside
        // its @media): one column, so neither is squeezed.
        Assert.Contains("grid-template-columns: minmax(300px, 360px) minmax(0, 1fr)", css);
        Assert.Matches(@"@media \(max-width: 900px\) \{ \.dashboard-grid \{ grid-template-columns: minmax\(0, 1fr\); \} \}", Regex.Replace(css, @"\s+", " "));
        Assert.Equal("minmax(0, 1fr)", FinalValue(css, ".dashboard-grid", "grid-template-columns"));
        // "Risk % of balance" and its Size button ran past the dialog's edge at 390 wide.
        Assert.Equal("wrap", FinalValue(css, ".order-entry .entry-row", "flex-wrap"));
        Assert.DoesNotContain("calc(100vw - 96px)", razor);
        // At 1280 wide the positions table needed 812px of 754 and cut the Close buttons off.
        Assert.Equal("normal", FinalValue(css, ".mini-table td button", "white-space"));
        // The Order Book tab's asks and bids: two fixed halves pushed the bids past the edge.
        Assert.Contains("grid-template-columns:repeat(auto-fit, minmax(min(100%, 10rem), 1fr))", razor);
        Assert.DoesNotContain("grid-template-columns:1fr 1fr", razor);
    }

    [Fact]
    public void Final_value_follows_the_cascade()
    {
        const string css = ".a, .b { color: red; } .b { color: blue } .c { color: green; }";
        Assert.Equal("red", FinalValue(css, ".a", "color"));
        Assert.Equal("blue", FinalValue(css, ".b", "color"));
        Assert.Null(FinalValue(css, ".a", "margin"));
    }
}
