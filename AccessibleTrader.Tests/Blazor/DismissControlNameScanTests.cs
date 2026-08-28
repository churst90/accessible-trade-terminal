// M19 / A2 finding F9 — "a dialog button loses its accessible name".
//
// A2's mutant stripped aria-label="Close alerts dialog" from AlertsModal's dismiss button and
// survived a green suite twice. F9's underlying census: 181 of the 193 literal aria-label values
// in the component library are named by no test at all, so removing any of them is free.
//
// The obvious guard does not catch this one. AccessibleNameSweepTests (browser harness) asks
// whether each control HAS an accessible name — and the mutated button still does: the text node
// "Close" names it. What is lost is not the name but its specificity, and that is a real
// regression rather than a cosmetic one. A screen reader's element/button list is the primary way
// a blind user surveys a dialog, and it reads names with no surrounding context: twenty-two
// buttons all announcing "Close button" are twenty-two identical rows. TradingDashboardModal's own
// source comment says exactly this about its position rows ("a wall of Close buttons is
// unnavigable by button list") — the app already knows the rule and did not apply it to itself.
//
// SO THE RULE IS: a control whose accessible name consists ENTIRELY of generic dismiss words
// ("Close", "Cancel", "OK", "Back", "Done"…) has not said what it dismisses. At least one word of
// the name must be specific to what the control acts on.
//
// WHY A SOURCE SCAN AND NOT A RENDER SWEEP. This deliberately reads the .razor markup instead of
// rendering each dialog and asking the DOM. Most of the sites this found are inside conditional
// branches that a freshly-opened dialog does not render — SettingsModal's Cancel exists only while
// a key rebind is capturing, TradingDashboardModal's only while an order review is armed, the
// Toolbar's only during a destructive-switch confirmation. A render sweep over the default open
// state sees 7 of the 22 and reports the other 15 as covered. The browser harness stays the
// authority on whether a name EXISTS (it resolves label/for, placeholders and hidden text, which
// markup cannot); this file is the authority on whether the name SAYS anything, which markup can.
//
// Both halves of WCAG's naming guidance are asserted: 2.4.6 (labels describe purpose) as the rule
// above, and 2.5.3 Label in Name — an aria-label must still contain the button's visible text, or
// a speech-input user saying what they can see does not activate it.

using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests.Blazor;

public class DismissControlNameScanTests
{
    /// <summary>
    /// Words that carry no information about WHAT is being acted on. A name built only from
    /// these is generic by definition. Kept deliberately short — every addition weakens the
    /// guard, so anything arguable (an action verb like "Save", "Delete", "Run") stays out and
    /// is allowed to stand alone.
    /// </summary>
    private static readonly IReadOnlySet<string> GenericWords = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        "close", "cancel", "ok", "okay", "done", "back", "dismiss", "exit", "quit", "no", "yes",
        "dialog", "modal", "window", "panel", "box", "button", "the", "a", "an", "this", "and",
    };

    // The open tag steps over quoted attribute values rather than stopping at the first '>'.
    // A naive [^>]* ends the tag in the middle of @onclick="() => AddColorRule(comp)" and then
    // reports the lambda body as the button's visible text, which is how the first run of this
    // scan produced four findings that were its own parsing.
    private static readonly Regex ButtonElement =
        new(@"<button\b(?:[^>""']|""[^""]*""|'[^']*')*>(.*?)</button>",
            RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex AriaLabel =
        new(@"aria-label\s*=\s*""([^""]*)""", RegexOptions.Compiled);
    private static readonly Regex Tag = new(@"<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex Words = new(@"[A-Za-z]+", RegexOptions.Compiled);

    private sealed record Control(string File, int Line, string Name, string VisibleText, bool FromAriaLabel);

    private static string ComponentsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "AccessibleTrader.BlazorClient.Components");
    }

    /// <summary>
    /// Every &lt;button&gt; in the RCL whose name can be read statically, plus the count of the
    /// ones that cannot. A name interpolated from C# (<c>aria-label="@(...)"</c>) is specific by
    /// construction — it names a position, an account, a series — so it is skipped rather than
    /// guessed at, and the skip is counted so a silent drift toward "everything is dynamic" is
    /// visible in the vacuity check below.
    /// </summary>
    private static (List<Control> Static, int Dynamic) ScanButtons()
    {
        var found = new List<Control>();
        int dynamic = 0;

        foreach (var file in Directory.EnumerateFiles(ComponentsDir(), "*.razor", SearchOption.AllDirectories)
                                      .OrderBy(f => f, StringComparer.Ordinal))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            var src = File.ReadAllText(file);
            var name = Path.GetFileName(file);

            foreach (Match m in ButtonElement.Matches(src))
            {
                var openTag = m.Value[..(m.Groups[1].Index - m.Index)];
                var visible = Tag.Replace(m.Groups[1].Value, " ").Trim();

                var label = AriaLabel.Match(openTag);
                var accessible = label.Success ? label.Groups[1].Value : visible;

                if (accessible.Contains('@')) { dynamic++; continue; }
                if (Words.Matches(accessible).Count == 0) { dynamic++; continue; }

                found.Add(new Control(name, src[..m.Index].Count(c => c == '\n') + 1,
                                      accessible, visible, label.Success));
            }
        }
        return (found, dynamic);
    }

    /// <summary>The kill: M19 makes AlertsModal's Close button land in this list.</summary>
    [Fact]
    public void NoControlIsAnnouncedByGenericWordsAlone()
    {
        var (controls, _) = ScanButtons();

        var generic = controls
            .Where(c => Words.Matches(c.Name).Select(w => w.Value).All(GenericWords.Contains))
            .ToList();

        Assert.True(generic.Count == 0,
            "These controls announce nothing but a generic verb, so a screen reader's button list "
            + "shows several identical rows and none of them says what it acts on. Give each an "
            + "aria-label naming what it closes, cancels or goes back from — keeping the visible "
            + "text inside the label (WCAG 2.5.3):\n  "
            + string.Join("\n  ", generic.Select(c => $"{c.File}:{c.Line}  \"{c.Name}\"")));
    }

    /// <summary>
    /// The controls that already fail WCAG 2.5.3, as of 2026-08-28.
    ///
    /// <para>
    /// An exact set rather than a budget, the same discipline as
    /// <c>AccessibleNameSweepTests.KnownUnnamed</c>: it fails when something new appears AND when
    /// one of these is fixed without being struck off, so the list cannot quietly become a
    /// permanent exemption. Keyed on file plus visible text — a line number churns on every edit
    /// above it, and rewording the visible text is itself a reason to re-examine the entry.
    /// Duplicates within one file collapse (SettingsModal has three "Send test" buttons); that is
    /// deliberate, since they are the same defect written three times.
    /// </para>
    ///
    /// <para>
    /// Closing these is filed as its own item — see docs/TODO.md. It is not part of M19: M19 is
    /// about a name that says nothing, and every entry here already says a great deal. The defect
    /// is the reverse one, a label so much richer than the visible text that the visible text is
    /// no longer inside it.
    /// </para>
    /// </summary>
    private static readonly IReadOnlySet<string> KnownLabelInNameGaps = new HashSet<string>(
        StringComparer.Ordinal)
    {
        "AIAnalystModal|Review setups today",
        "ApiKeysModal|Save Profile",
        "ConditionTreeEditor|+ Group at root",
        "ConditionTreeEditor|+ Leaf at root",
        "CustomScriptsModal|&#128194; Import from file…",
        "CustomScriptsModal|+ Add to Chart",
        "CustomScriptsModal|Export .atpkg",
        "JournalModal|Copy visible",
        "SettingsModal|Add webhook",
        "SettingsModal|Customise…",
        "SettingsModal|Export CSV",
        "SettingsModal|Reset to theme default",
        "SettingsModal|Send test",
        "SettingsModal|Speak status",
        "SettingsModal|Use theme's",
        "SoundDesignerModal|Export JSON",
        "StrategyModal|Clear range",
        "StrategyModal|Go to Build Setup",
        "StrategyModal|Walk-fwd: first half",
        "StrategyModal|Walk-fwd: last half",
        "SummaryExport|Add to Engine",
        "SummaryExport|Import latest",
        "SummaryExport|🔊 Read aloud",
        "SummaryExport|Save spec",
        "ThemeEditorModal|Reset all",
        "TradingDashboardModal|Place OCO pair",
        "WalletModal|Get address",
        "WalletModal|Read character by character",
        "WatchlistModal|Add all shown",
        "WatchlistModal|Delete list",
        "WithdrawModal|Get quote",
        "WithdrawModal|Read characters",
    };

    /// <summary>
    /// WCAG 2.5.3 Label in Name. The fix for the rule above is to add an aria-label, and the
    /// wrong version of that fix — a label that replaces the visible text instead of extending it
    /// — breaks voice control: "click Close" no longer matches a button whose name is
    /// "Dismiss the alerts panel". Asserted here so the remedy cannot introduce a second defect,
    /// and it caught one while this file was being written (DrawingToolsModal already carried
    /// aria-label="Close dialog", which is generic; the replacement had to keep "Close" in it).
    /// </summary>
    [Fact]
    public void AnAriaLabelContainsTheControlsVisibleText()
    {
        var (controls, _) = ScanButtons();

        var mismatched = controls
            .Where(c => c.FromAriaLabel)
            .Where(c => c.VisibleText.Length > 0 && !c.VisibleText.Contains('@'))
            .Where(c => !ContainsVisibleWords(c.Name, c.VisibleText))
            .ToList();

        string Key(Control c) => $"{Path.GetFileNameWithoutExtension(c.File)}|{c.VisibleText}";

        var unexpected = mismatched.Where(c => !KnownLabelInNameGaps.Contains(Key(c))).ToList();
        Assert.True(unexpected.Count == 0,
            "These controls carry an aria-label that does not contain their visible text, so a "
            + "speech-input user reading the screen aloud cannot activate them. Extend the visible "
            + "text rather than replacing it:\n  "
            + string.Join("\n  ", unexpected.Select(c =>
                $"{c.File}:{c.Line}  visible \"{c.VisibleText}\" vs announced \"{c.Name}\"")));

        var stillListed = mismatched.Select(Key).ToHashSet(StringComparer.Ordinal);
        var fixedSince = KnownLabelInNameGaps.Where(k => !stillListed.Contains(k)).ToList();
        Assert.True(fixedSince.Count == 0,
            "These were on KnownLabelInNameGaps and no longer violate 2.5.3 — good. Delete them "
            + "from the list so it keeps meaning something:\n  "
            + string.Join("\n  ", fixedSince.Order()));
    }

    private static bool ContainsVisibleWords(string accessibleName, string visibleText)
    {
        var announced = Words.Matches(accessibleName).Select(w => w.Value).ToList();
        var visible = Words.Matches(visibleText).Select(w => w.Value).ToList();
        if (visible.Count == 0) return true;
        // Contiguous subsequence, case-insensitively — the criterion is about the visible string
        // appearing in the name, not about the same words in any order.
        for (int i = 0; i + visible.Count <= announced.Count; i++)
        {
            bool all = true;
            for (int j = 0; j < visible.Count && all; j++)
                all = string.Equals(announced[i + j], visible[j], StringComparison.OrdinalIgnoreCase);
            if (all) return true;
        }
        return false;
    }

    /// <summary>
    /// The vacuity check. Both assertions above are "no matches", which is also what a scan that
    /// found no buttons at all returns — and this scan reads a directory path and a regex, either
    /// of which can quietly stop matching (the RCL moves, Razor markup is reformatted so a button
    /// spans lines differently). Pinned to a floor rather than an exact count so ordinary UI work
    /// does not fail it.
    /// </summary>
    [Fact]
    public void TheScanActuallyReadsTheComponentLibrary()
    {
        var (controls, dynamic) = ScanButtons();

        Assert.True(controls.Count >= 150,
            $"The button scan found only {controls.Count} statically-named controls in the RCL "
            + $"({dynamic} skipped as dynamically named). It found over 200 when written; a "
            + "collapse means the path or the element regex stopped matching and both assertions "
            + "in this file are passing against nothing.");

        Assert.True(controls.Count(c => c.FromAriaLabel) >= 100,
            $"Only {controls.Count(c => c.FromAriaLabel)} controls were read via aria-label. "
            + "A2/F9 counted 193 literal aria-label values in this library; if this number falls "
            + "off, the attribute regex is no longer matching them.");
    }
}
