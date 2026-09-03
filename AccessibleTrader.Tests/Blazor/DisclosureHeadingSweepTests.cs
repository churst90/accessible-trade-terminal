// A section title that is only BOLD is not a section title.
//
// The 2026-09-01 audit's finding was HelpModal: 471 lines, 18 sections, one heading. Every
// section title was `<summary style="font-weight:700">` — a heading to anyone who can see the
// weight, and nothing at all to the population this product exists for. Pressing H, or opening
// NVDA's heading list, in the app's own keyboard reference returned a single entry.
//
// Measured before the fix, by rendering every dialog in ModalCatalog: 27 <summary> elements
// across six dialogs, and NOT ONE of them contained a heading. HelpModal was 18 of the 27; the
// same shape was live in SoundDesigner (3), ObjectTree (2), CustomScripts (2), Settings (1) and
// Strategy (1). Fixing only the dialog the audit happened to name would have left a guard that
// passed for the wrong reason.
//
// StrategyModal has a SECOND one — the backtest Trade Log — that this sweep never sees, because
// it renders only once a backtest has produced results and the catalog opens the dialog cold. It
// was fixed in the same pass, and it is guarded by nothing but that fact being written here.
//
// The rule this enforces is CONTAINMENT, not existence: a <summary> must contain a heading
// element. That is deliberate and it is the only construct that works —
//
//   * <h3><summary>…  is not an option. <summary> must be the FIRST CHILD of <details>; wrap it
//     and the parser sees no summary at all, the browser paints its own "Details" label, and the
//     disclosure stops toggling. Breaking the widget, not styling it.
//   * <summary><h3>…  is explicitly conforming: the HTML content model for <summary> is
//     "phrasing content, optionally intermixed with HEADING CONTENT". That clause exists for
//     exactly this.
//
// EXEMPTION, and there is exactly one. A <summary> inside [role="tree"] is a tree NODE, not a
// document section — ObjectTreeModal builds its hierarchy that way, and role="treeitem" already
// replaces the native disclosure mapping there. A heading inside a treeitem would be inventing
// structure that the tree role already expresses. The exemption keys on the ancestor role rather
// than on a list of file names, so a new tree gets it by being a tree and a new Help-shaped
// dialog cannot quietly join it.
//
// What this does NOT assert, on purpose: that the heading is at the right LEVEL. Level is a
// judgement about the dialog's outline (Help's sections sit under its h2, Strategy's Trade Log
// sits under the h3 for Backtest), and a test that fixed it would either be wrong for one of
// them or would encode a per-dialog table nobody would maintain.

using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Bunit;

namespace AccessibleTrader.Tests.Blazor;

public class DisclosureHeadingSweepTests
{
    private static readonly string[] HeadingTags = { "H1", "H2", "H3", "H4", "H5", "H6" };

    /// <summary>
    /// A heading element with something in it. The text check is not padding: an empty
    /// &lt;h3&gt;&lt;/h3&gt; beside the old bold text satisfies "contains a heading" and gives a
    /// screen-reader user an unnamed entry in the heading list, which is worse than none.
    /// </summary>
    private static bool IsHeading(IElement el) =>
        (HeadingTags.Contains(el.TagName) ||
         string.Equals(el.GetAttribute("role"), "heading", StringComparison.OrdinalIgnoreCase))
        && !string.IsNullOrWhiteSpace(el.TextContent);

    /// <summary>A tree node, not a document section. The one exemption; see the header.</summary>
    private static bool IsTreeNode(IElement summary) =>
        summary.Closest("[role='tree']") != null;

    internal static (List<string> Unheaded, int Swept) Sweep(string dialog, IRenderedFragment cut)
    {
        var all = cut.Nodes.OfType<IElement>()
                     .SelectMany(r => new[] { r }.Concat(r.QuerySelectorAll("*")))
                     .ToList();

        var unheaded = new List<string>();
        int swept = 0;
        foreach (var summary in all.Where(e => e.TagName == "SUMMARY"))
        {
            if (IsTreeNode(summary)) continue;
            swept++;
            if (summary.QuerySelectorAll("*").Any(IsHeading)) continue;
            unheaded.Add($"{dialog}: <summary>{Squash(summary.TextContent)}</summary>");
        }
        return (unheaded, swept);
    }

    private static string Squash(string s)
    {
        var t = string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return t.Length <= 70 ? t : t[..70] + "…";
    }

    [Theory]
    [MemberData(nameof(DialogNames))]
    public void EveryDisclosureSectionTitleIsAHeading(string name)
    {
        using var h = new BlazorTestHarness();
        var cut = ModalCatalog.OpenDialog(h, ModalCatalog.Dialog(name));
        Assert.NotEmpty(cut.FindAll("[role='dialog'], [role='alertdialog']"));

        var (unheaded, _) = Sweep(name, cut);

        Assert.True(unheaded.Count == 0,
            "Disclosure sections whose title is bold text and nothing else — a screen reader user "
            + "pressing H walks straight past them:\n  " + string.Join("\n  ", unheaded)
            + "\n\nPut the title in a heading INSIDE the <summary>: "
            + "<summary><h3 style=\"display:inline; font-size:inherit; font-weight:inherit; margin:0;\">"
            + "Title</h3></summary>. Never <h3><summary> — <summary> must be the first child of "
            + "<details> or the disclosure stops working. Pick the level from the dialog's own "
            + "outline, not from this message.");
    }

    /// <summary>
    /// The sweep above is a per-dialog assertion and every one of them passes vacuously on a
    /// dialog that renders no &lt;details&gt; at all. This is the floor: the six dialogs that
    /// carried the defect must still be rendering disclosure sections for the sweep to mean
    /// anything. 25 is the pre-fix rendered count (27) minus ObjectTree's two exempt tree nodes.
    /// </summary>
    [Fact]
    public void TheSweepActuallyReachesTheDisclosureSectionsItGuards()
    {
        int total = 0;
        var perDialog = new List<string>();
        foreach (var name in ModalCatalog.Dialogs.Select(d => d.Name))
        {
            using var h = new BlazorTestHarness();
            var cut = ModalCatalog.OpenDialog(h, ModalCatalog.Dialog(name));
            var (_, swept) = Sweep(name, cut);
            total += swept;
            if (swept > 0) perDialog.Add($"{name}={swept}");
        }

        Assert.True(total >= 25,
            $"The disclosure sweep saw only {total} non-tree <summary> elements across every "
            + $"catalog dialog ({string.Join(", ", perDialog)}). It saw 25 when the guard was "
            + "written. A dialog that has stopped rendering its sections makes the sweep above "
            + "pass without measuring anything.");
    }


    // ── the branches no render reaches ────────────────────────────────────────

    /// <summary>
    /// The same rule, read off the source instead of the DOM.
    ///
    /// <para>
    /// The sweep above is the better instrument everywhere it can look, and it cannot look
    /// everywhere: <c>StrategyModal</c>'s backtest Trade Log sits inside
    /// <c>@if (_btResult != null)</c> nested in <c>@if (_btResult.Trades.Any())</c>, so a cold
    /// catalog open never renders it and the render sweep would have reported a clean pass over a
    /// section that still had the defect. This one reads every <c>&lt;summary&gt;</c> in the
    /// component library including the ones behind conditionals.
    /// </para>
    ///
    /// <para>
    /// It is deliberately the weaker check of the two and is kept as a companion, not a
    /// replacement — it proves the markup contains a heading, not that a browser exposes one.
    /// </para>
    /// </summary>
    [Fact]
    public void EverySummaryInTheComponentLibraryContainsAHeading_IncludingUnrenderedBranches()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        string root = Path.Combine(dir!.FullName, "AccessibleTrader.BlazorClient.Components");

        var offenders = new List<string>();
        int scanned = 0;

        foreach (string file in Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);

            // CodeOnly, not WithoutRazorComments: this scan has to survive the C# XML doc comments
            // in every @code block, which are full of `/// <summary>` and would otherwise read as
            // eighty disclosure sections with no headings. CodeOnly strips line comments whose
            // "//" starts the line, which is exactly what those are.
            string src = ModalContractScanTests.CodeOnly(File.ReadAllText(file));

            if (TreeFiles.TryGetValue(name, out string? marker))
            {
                // A pinned exemption is only as good as the reason recorded on it. ObjectTreeModal
                // is exempt because its <summary> elements are tree nodes; if the tree goes, so
                // does the exemption, and this fails rather than silently widening.
                Assert.True(src.Contains(marker, StringComparison.Ordinal),
                    $"{name} is exempt from the disclosure-heading rule because its <summary> "
                    + $"elements are tree nodes, but it no longer contains {marker}. Either the "
                    + "exemption is stale or the tree moved; do not leave it in place unexamined.");
                continue;
            }

            foreach (var (open, close) in SummarySpans(src))
            {
                scanned++;
                string inner = src[open..close];
                if (Regex.IsMatch(inner, @"<h[1-6]\b[^>]*>\s*\S") ||
                    Regex.IsMatch(inner, "role\\s*=\\s*\"heading\"")) continue;
                offenders.Add($"{name}: <summary>{Squash(Regex.Replace(inner, "<[^>]*>", " "))}</summary>");
            }
        }

        Assert.True(scanned >= 26,
            $"The source scan found only {scanned} <summary> elements in the component library; it "
            + "found 26 when this was written (the 25 the render sweep reaches, plus StrategyModal's "
            + "Trade Log). A scan that has stopped finding them passes for the wrong reason.");

        Assert.True(offenders.Count == 0,
            "Disclosure sections whose title is not a heading in the source:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>Files whose &lt;summary&gt; elements are tree nodes, with the marker that says so.</summary>
    private static readonly Dictionary<string, string> TreeFiles = new(StringComparer.Ordinal)
    {
        ["ObjectTreeModal.razor"] = "role=\"tree\"",
    };

    /// <summary>Start and end offsets of the body of every &lt;summary&gt; element in the source.</summary>
    private static IEnumerable<(int Open, int Close)> SummarySpans(string src)
    {
        int from = 0;
        while (true)
        {
            int tag = src.IndexOf("<summary", from, StringComparison.Ordinal);
            if (tag < 0) yield break;
            int open = src.IndexOf('>', tag);
            int close = open < 0 ? -1 : src.IndexOf("</summary>", open, StringComparison.Ordinal);
            if (open < 0 || close < 0) yield break;
            yield return (open + 1, close);
            from = close + 1;
        }
    }

    public static IEnumerable<object[]> DialogNames() =>
        ModalCatalog.Dialogs.Select(d => new object[] { d.Name });
}
