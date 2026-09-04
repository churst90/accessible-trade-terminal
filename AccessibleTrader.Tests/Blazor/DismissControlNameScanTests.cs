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
// above, and 2.5.3 Label in Name — an aria-label must still contain the control's visible text, or
// a speech-input user saying what they can see does not activate it. 2.5.3 is checked in both the
// places the visible text can live: inside a <button>, and in a <label for> pointing at an input
// or select. The 32 button gaps found on 2026-08-28 and the 15 label/for ones found on 2026-08-29
// are all closed, so neither assertion carries an exemption list.

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
    /// WCAG 2.5.3 Label in Name. The fix for the rule above is to add an aria-label, and the
    /// wrong version of that fix — a label that replaces the visible text instead of extending it
    /// — breaks voice control: "click Close" no longer matches a button whose name is
    /// "Dismiss the alerts panel". Asserted here so the remedy cannot introduce a second defect,
    /// and it caught one while this file was being written (DrawingToolsModal already carried
    /// aria-label="Close dialog", which is generic; the replacement had to keep "Close" in it).
    ///
    /// <para>
    /// This assertion carried an exact <c>KnownLabelInNameGaps</c> exemption set of 32 controls
    /// from 2026-08-28 until 2026-08-29, when all 32 were fixed and the set was deleted rather
    /// than emptied — a zero-length allow-list is an invitation to grow one again. Every entry was
    /// the reverse of M19: a label so much richer than the visible text ("Save Profile" announced
    /// as "Save new API key profile") that the visible text was no longer inside it. The fix shape
    /// that closed all 32 is the one to repeat — <b>extend the visible text, do not describe the
    /// control afresh</b>: "Save Profile as a new API key profile", "Export CSV of the chart data",
    /// with a colon only where the visible words do not flow into the rest. The visible words stay
    /// contiguous and at the front, so the announcement still leads with the word a sighted user
    /// would read out and a voice-control user would say.
    /// </para>
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

        Assert.True(mismatched.Count == 0,
            "These controls carry an aria-label that does not contain their visible text, so a "
            + "speech-input user reading the screen aloud cannot activate them. Extend the visible "
            + "text rather than replacing it:\n  "
            + string.Join("\n  ", mismatched.Select(c =>
                $"{c.File}:{c.Line}  visible \"{c.VisibleText}\" vs announced \"{c.Name}\"")));
    }

    // A <label for="x"> paired with the element carrying id="x". The open tag is stepped over the
    // same way as a button's, so a for= inside a quoted attribute value cannot end the tag early.
    private static readonly Regex LabelElement =
        new(@"<label\b(?:[^>""']|""[^""]*""|'[^']*')*>(.*?)</label>",
            RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ForAttr = new(@"\bfor\s*=\s*""([^""]*)""", RegexOptions.Compiled);
    private static readonly Regex ClassAttr = new(@"\bclass\s*=\s*""([^""]*)""", RegexOptions.Compiled);
    private static readonly Regex FormControl =
        new(@"<(?:input|select|textarea|button)\b(?:[^>""']|""[^""]*""|'[^']*')*/?>",
            RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex IdAttr = new(@"\bid\s*=\s*""([^""]*)""", RegexOptions.Compiled);

    /// <summary>
    /// Every form control that is named by an <c>aria-label</c> while a visible
    /// <c>&lt;label for&gt;</c> points at it. The <c>aria-label</c> wins, so the visible label is
    /// what the user reads and the <c>aria-label</c> is what voice control matches — exactly the
    /// 2.5.3 pairing. <c>sr-only</c> labels are excluded: 2.5.3 is about text the user can SEE,
    /// and a visually-hidden label is not it.
    /// </summary>
    private static List<Control> ScanLabelledControls()
    {
        var found = new List<Control>();

        foreach (var file in Directory.EnumerateFiles(ComponentsDir(), "*.razor", SearchOption.AllDirectories)
                                      .OrderBy(f => f, StringComparer.Ordinal))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            var src = File.ReadAllText(file);
            var name = Path.GetFileName(file);

            var labels = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match m in LabelElement.Matches(src))
            {
                var openTag = m.Value[..(m.Groups[1].Index - m.Index)];
                var target = ForAttr.Match(openTag);
                if (!target.Success) continue;
                var cls = ClassAttr.Match(openTag);
                if (cls.Success && cls.Groups[1].Value.Contains("sr-only", StringComparison.Ordinal)) continue;
                labels[target.Groups[1].Value] = Tag.Replace(m.Groups[1].Value, " ").Trim();
            }

            foreach (Match m in FormControl.Matches(src))
            {
                var id = IdAttr.Match(m.Value);
                var label = AriaLabel.Match(m.Value);
                if (!id.Success || !label.Success) continue;
                if (!labels.TryGetValue(id.Groups[1].Value, out var visible)) continue;
                if (visible.Length == 0 || visible.Contains('@')) continue;
                if (label.Groups[1].Value.Contains('@')) continue;
                if (Words.Matches(visible).Count == 0) continue;

                found.Add(new Control(name, src[..m.Index].Count(c => c == '\n') + 1,
                                      label.Groups[1].Value, visible, FromAriaLabel: true));
            }
        }
        return found;
    }

    /// <summary>
    /// WCAG 2.5.3 again, for the other half of the library — the inputs and selects, where the
    /// visible text is a sibling <c>&lt;label for&gt;</c> rather than the element's own content.
    ///
    /// <para>
    /// 15 controls failed this when it was written on 2026-08-29, in the same shape as the button
    /// gaps and for the same reason: the <c>aria-label</c> was written as a fuller description
    /// instead of an extension of the label, so "Profile Name" announced as "Profile nickname" and
    /// "Min size" as "Minimum order size to announce". Fixed rather than pinned, so this starts
    /// life with no exemption list at all.
    /// </para>
    ///
    /// <para>
    /// One thing this REFUTES, and it is recorded because a filed finding said otherwise: the
    /// Toolbar's <c>&lt;label for="market-select"&gt;Market:&lt;/label&gt;</c> paired with
    /// <c>aria-label="Select market"</c> is NOT a violation. The criterion is containment, not
    /// equality — "Select market" contains "market", so a speech-input user saying "click Market"
    /// does match it.
    /// </para>
    /// </summary>
    [Fact]
    public void ALabelledControlsAriaLabelContainsItsVisibleLabel()
    {
        var mismatched = ScanLabelledControls()
            .Where(c => !ContainsVisibleWords(c.Name, c.VisibleText))
            .ToList();

        Assert.True(mismatched.Count == 0,
            "These controls have a visible <label for> AND an aria-label that overrides it without "
            + "containing it, so the name a user can read is not the name voice control matches. "
            + "Extend the label's own words rather than describing the control afresh:\n  "
            + string.Join("\n  ", mismatched.Select(c =>
                $"{c.File}:{c.Line}  visible \"{c.VisibleText}\" vs announced \"{c.Name}\"")));
    }

    /// <summary>
    /// WCAG 2.5.3's comparison, and the ONE definition of it in this suite —
    /// <see cref="LabelInNameRenderSweepTests"/> calls this rather than growing a second
    /// one, because a render sweep and a source scan that disagree about what "contains"
    /// means would each report the other's findings as false positives.
    /// </summary>
    internal static bool ContainsVisibleWords(string accessibleName, string visibleText)
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

        // The floor has come down twice as the library shed aria-labels on purpose (the
        // toolbar's one-convention pass, then the 2026-09-03 settings restructure, which
        // removed the colour-override buttons): over 200 when written, 151 before that
        // restructure, 149 after it. A vacuity floor is there to catch a COLLAPSE, not a
        // count, so it sits well under the number rather than one below it.
        Assert.True(controls.Count >= 120,
            $"The button scan found only {controls.Count} statically-named controls in the RCL "
            + $"({dynamic} skipped as dynamically named). It found 149 on 2026-09-03; a "
            + "collapse means the path or the element regex stopped matching and both assertions "
            + "in this file are passing against nothing.");

        // Same story: 193 literal aria-labels at A2/F9, ~110 after the toolbar convention
        // deleted every AriaLabel whose text already was the name, 98 after the settings
        // restructure removed the colour-override rows. The floor guards the regex, not the
        // library's appetite for aria-label.
        Assert.True(controls.Count(c => c.FromAriaLabel) >= 80,
            $"Only {controls.Count(c => c.FromAriaLabel)} controls were read via aria-label. "
            + "A2/F9 counted 193 literal aria-label values in this library and 2026-09-03 counted "
            + "98; if this number falls off, the attribute regex is no longer matching them.");

        // The population the 2.5.3 assertion actually judges: an aria-label AND statically
        // readable visible text. It is narrower than the count above (a labelled icon-only button
        // has no visible words and is exempt by construction), and since the 32-entry exemption
        // set was deleted that assertion is a bare "no matches" — so the thing it would be
        // vacuous against is this number collapsing, not the two above it. 114 when written.
        var judged = controls.Count(c => c.FromAriaLabel
                                         && c.VisibleText.Length > 0
                                         && !c.VisibleText.Contains('@'));
        Assert.True(judged >= 80,
            $"Only {judged} controls carry both an aria-label and static visible text, so only "
            + "that many are subject to the Label-in-Name assertion. It was 114 when the last gap "
            + "was closed; a collapse means that assertion is passing against almost nothing.");

        // Same argument for the <label for> half: it depends on a second element regex and on the
        // id/for join succeeding, either of which can silently stop matching. 47 when written.
        var labelled = ScanLabelledControls().Count;
        Assert.True(labelled >= 30,
            $"Only {labelled} form controls were found with both a visible <label for> and an "
            + "aria-label. It was 47 when the label/for gaps were closed; a collapse means the "
            + "label regex or the id/for join stopped matching and "
            + nameof(ALabelledControlsAriaLabelContainsItsVisibleLabel) + " judges nothing.");
    }
}
