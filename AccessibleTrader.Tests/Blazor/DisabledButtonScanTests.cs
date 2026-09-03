// No button in this app may be made ABSENT by state.
//
// `disabled` on a <button> is not "greyed out" to the user this product exists for. It
// removes the control from the tab order and from the screen reader's element list
// entirely: the button is gone, nothing says it was ever there, and nothing says which
// field would bring it back. On 2026-09-02 a bUnit probe put quantity 0 into the order
// ticket and watched "Submit Buy Order" leave the document with the quantity field still
// announcing itself as valid — the money screen, on a product for blind traders.
//
// Thirty-four call sites carried a dynamic `disabled` expression that day. All of them
// went through GatedButton (or ToolbarIconButton's Gate, which is the same contract), so
// the rule this scan enforces is: a state-dependent button refuses OUT LOUD, or it does
// not refuse. A static `disabled` on markup that is never state-dependent is not the
// defect and is not what is scanned for.
//
// This is a PATH check rather than a presence check (scan-guards-need-a-path-check): it
// asserts the absence of the defect shape in the files that render buttons, not the
// presence of a helpful-looking call somewhere in the file.

using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests.Blazor;

public sealed class DisabledButtonScanTests
{
    private static string ComponentsDir() =>
        Path.Combine(RepoPaths.RepoRoot(), "AccessibleTrader.BlazorClient.Components");

    private static List<string> RazorFiles()
    {
        var dir = ComponentsDir();
        Assert.True(Directory.Exists(dir), $"Component library not found at {dir}");
        var files = Directory.GetFiles(dir, "*.razor", SearchOption.AllDirectories).ToList();
        // Vacuity floor: this suite once found 34 gated buttons across 16 files, so a
        // scan that walks a handful of files is reading the wrong directory.
        Assert.True(files.Count >= 30,
            $"Only {files.Count} .razor files under {dir} — the scan is pointed somewhere wrong.");
        return files;
    }

    /// <summary>
    /// Every opening tag of the named elements, whole.
    ///
    /// <para>
    /// Written as a quote-aware scan rather than <c>&lt;tag[^&gt;]*&gt;</c> because a Razor
    /// attribute value routinely contains a <c>&gt;</c> — <c>OnClick="() =&gt; Remove(idx)"</c>
    /// is the ordinary shape in this library — and a naive matcher truncates the tag at
    /// the lambda arrow, losing every attribute after it. The first version of this scan
    /// did exactly that and reported two call sites as having no Gate when both had one.
    /// Multi-line tags are the norm here too, so nothing may be read a line at a time:
    /// a literal per-line IndexOf is how the 2026-08-31 OHLCV scan gate missed two
    /// providers whose method signature was wrapped.
    /// </para>
    /// </summary>
    internal static IEnumerable<string> OpeningTags(string src, params string[] names)
    {
        foreach (Match start in Regex.Matches(src, @"<(" + string.Join('|', names) + @")(?=[\s/>])"))
        {
            int i = start.Index + start.Length;
            char quote = '\0';
            while (i < src.Length)
            {
                char c = src[i];
                if (quote != '\0') { if (c == quote) quote = '\0'; }
                else if (c == '"' || c == '\'') quote = c;
                else if (c == '>') { i++; break; }
                i++;
            }
            yield return src[start.Index..Math.Min(i, src.Length)];
        }
    }

    /// <summary>
    /// A dynamic `disabled` — <c>disabled="@…"</c> — on a button.
    /// The lookbehind is load-bearing: <c>\bdisabled</c> also matches inside
    /// <c>aria-disabled</c>, which is the ATTRIBUTE THE FIX ADDS, so without it this scan
    /// flags GatedButton itself and can never go green.
    /// </summary>
    private static IEnumerable<string> DynamicDisabledButtons(string path)
    {
        string src = File.ReadAllText(path);
        foreach (var tag in OpeningTags(src, "button", "ToolbarIconButton", "GatedButton"))
            if (Regex.IsMatch(tag, @"(?<![-\w])disabled\s*=\s*""@"))
                yield return $"{Path.GetFileName(path)}: {Squash(tag)}";
    }

    private static string Squash(string s) =>
        string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void No_button_is_disabled_by_state()
    {
        var offenders = RazorFiles().SelectMany(DynamicDisabledButtons).ToList();

        Assert.True(offenders.Count == 0,
            "These buttons vanish from the tab order and from the screen reader's button list "
            + "when their condition holds, with nothing to say why:\n  "
            + string.Join("\n  ", offenders)
            + "\n\nUse <GatedButton Gate=\"SomeGate\"> instead, where SomeGate() returns null when the "
            + "action can be taken and otherwise the reason it cannot, as a sentence the user can act "
            + "on. The component keeps the button reachable, announces it as unavailable, wires the "
            + "reason to aria-describedby, and refuses the click itself — so the handler is exactly as "
            + "unreachable as it was under `disabled`. There is no exemption list here on purpose.");
    }

    [Fact]
    public void Every_GatedButton_carries_a_gate()
    {
        // EditorRequired is a design-time warning, not a build error, and a GatedButton
        // with no Gate is silently always-available — the failure this component exists
        // to prevent, running in the opposite direction.
        var missing = new List<string>();
        foreach (var path in RazorFiles())
        {
            string src = File.ReadAllText(path);
            foreach (var tag in OpeningTags(src, "GatedButton"))
                if (!Regex.IsMatch(tag, @"(?<![-\w])Gate\s*=\s*"""))
                    missing.Add($"{Path.GetFileName(path)}: {Squash(tag)}");
        }

        Assert.True(missing.Count == 0,
            "GatedButton call sites with no Gate — always available, never explaining anything:\n  "
            + string.Join("\n  ", missing));
    }

    [Fact]
    public void Every_ToolbarIconButton_gate_is_a_method_group_or_lambda_returning_a_reason()
    {
        // ToolbarIconButton's Gate is optional (most toolbar buttons are never refused),
        // so the guard here is narrower: where a gate IS given it must not be a bool.
        // A `Gate="@(!HasChartData)"` would not compile, but `Gate="@(() => !HasChartData)"`
        // is the shape most likely to be reached for by someone converting the old
        // `Disabled` parameter, and it would put the string "True" in the user's ear.
        var offenders = new List<string>();
        foreach (var path in RazorFiles())
        {
            string src = File.ReadAllText(path);
            foreach (Match m in Regex.Matches(src, @"Gate\s*=\s*""([^""]*)""", RegexOptions.Singleline))
            {
                var v = m.Groups[1].Value;
                if (v.Contains("=> !") || v.Contains("=>!"))
                    offenders.Add($"{Path.GetFileName(path)}: Gate=\"{v}\"");
            }
        }

        Assert.True(offenders.Count == 0,
            "A gate must return the REASON (a sentence, or null), never a boolean:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_scan_can_see_the_defect_it_is_looking_for()
    {
        // Without this, a green result above is also what a scan whose regex no longer
        // matches anything would report. Both shapes are the real ones: the single-line
        // form and the wrapped form that a per-line IndexOf would miss.
        string oneLine = """<button @onclick="DeleteScript" disabled="@(_selected == null)">Delete</button>""";
        string wrapped = """
                         <button class="submit-btn"
                                 disabled="@(!CanSubmit)"
                                 @onclick="SubmitOrder">
                             Submit
                         </button>
                         """;
        string clean = """<GatedButton class="submit-btn" Gate="SubmitGate" OnClick="SubmitOrder">Submit</GatedButton>""";

        var tmp = TestTemp.NewDir("att-disabled-scan-");
        try
        {
            string p1 = Path.Combine(tmp, "One.razor");   File.WriteAllText(p1, oneLine);
            string p2 = Path.Combine(tmp, "Two.razor");   File.WriteAllText(p2, wrapped);
            string p3 = Path.Combine(tmp, "Three.razor"); File.WriteAllText(p3, clean);

            Assert.Single(DynamicDisabledButtons(p1));
            Assert.Single(DynamicDisabledButtons(p2));
            Assert.Empty(DynamicDisabledButtons(p3));
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }
}
