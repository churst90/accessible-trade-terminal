using System.Text.RegularExpressions;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A documented chord must be paired with the tool it actually opens.</b>
///
/// <para>
/// Cody, 2026-09-13: <i>"wrong, the measure tool is alt shift m, risk reward tool is alt shift p
/// and rectangle is alt shift r. Be sure all the shortcuts are documented correctly."</i> He was
/// right, and the interesting part is that the reference docs were ALREADY right — it was a
/// changelog entry and a TODO note, written by hand in the same breath as the feature, that put
/// the risk/reward tool on Alt+Shift+R.
/// </para>
///
/// <para>
/// <b>The guard that existed could not have caught it.</b> <c>check_doc_drift.py</c> asserts that
/// each default chord appears SOMEWHERE in <c>docs/SHORTCUTS.md</c> — a presence check, and the
/// repo already knows what those are worth (<c>scan-guards-need-a-path-check</c>). "Alt+Shift+P"
/// appearing anywhere satisfied it, including on a line calling it the rectangle. What matters is
/// the PAIRING, and nothing tested that.
/// </para>
///
/// <para>
/// So this reads the live default profile and, for every drawing chord, finds the lines in the
/// user-facing docs that mention that chord and checks none of them names a DIFFERENT tool. It is
/// deliberately a test rather than another rule in the python script: the binding table lives in
/// C#, and reading it through <see cref="ShortcutManager"/> means the test cannot drift from the
/// parser the way a second regex would.
/// </para>
/// </summary>
public sealed class DrawingShortcutDocParityTests
{
    /// <summary>
    /// The words a reader would use for each drawing tool. Matching is case-insensitive and a hit
    /// on ANY word counts as naming that tool.
    /// </summary>
    private static readonly Dictionary<SystemCommand, string[]> ToolWords = new()
    {
        // "trendline" as well as "trend line": whole-word matching means the compound spelling
        // is a different word, and the docs use both.
        [SystemCommand.DrawTrend]        = new[] { "trendline", "trend line", "trend" },
        [SystemCommand.DrawHorizontal]   = new[] { "horizontal" },
        [SystemCommand.DrawVertical]     = new[] { "vertical" },
        [SystemCommand.DrawChannel]      = new[] { "channel" },
        [SystemCommand.DrawFibonacci]    = new[] { "fibonacci retracement", "fib retracement" },
        [SystemCommand.DrawLabel]        = new[] { "label" },
        [SystemCommand.DrawFibExtension] = new[] { "fibonacci extension", "fib extension" },
        [SystemCommand.DrawPitchfork]    = new[] { "pitchfork" },
        [SystemCommand.DrawRectangle]    = new[] { "rectangle" },
        [SystemCommand.DrawMeasure]      = new[] { "measure" },
        [SystemCommand.DrawGannFan]      = new[] { "gann fan" },
        [SystemCommand.DrawRiskReward]   = new[] { "risk/reward", "risk reward", "risk-reward" },
        [SystemCommand.DrawAnchoredVwap] = new[] { "anchored vwap" },
        [SystemCommand.DrawGannBox]      = new[] { "gann box" },
        [SystemCommand.DrawAngleFib]     = new[] { "angle fib", "angle fibonacci", "angle" },
    };

    private static readonly string[] Docs =
    {
        "docs/SHORTCUTS.md", "docs/USER_MANUAL.md", "docs/QUICKSTART.md",
        "docs/README.md", "docs/TODO.md", "docs/CHANGES.md", "docs/WHATSNEW.md",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private sealed class TempPaths : IPlatformPathService
    {
        public TempPaths(string root) { AppDataDirectory = root; CacheDirectory = root; }
        public string AppDataDirectory { get; }
        public string CacheDirectory { get; }
    }

    /// <summary>
    /// The chord each drawing command is bound to in the shipped default profile, read from a
    /// real <see cref="ShortcutManager"/> over a throwaway directory — the same way
    /// <c>ShortcutHelpParityTests</c> does it, so both tests see one source of truth.
    /// </summary>
    private static Dictionary<SystemCommand, string> DefaultChords()
    {
        var dir = TestTemp.NewDir("att-drawing-chord-parity-");
        try { return Read(new ShortcutManager(new TempPaths(dir))); }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ } }
    }

    private static Dictionary<SystemCommand, string> Read(ShortcutManager mgr)
    {
        var chords = new Dictionary<SystemCommand, string>();
        foreach (var s in mgr.CurrentProfile.Shortcuts)
        {
            if (!ToolWords.ContainsKey(s.Command)) continue;
            var parts = new List<string>();
            if (s.Ctrl) parts.Add("Ctrl");
            if (s.Alt) parts.Add("Alt");
            if (s.Shift) parts.Add("Shift");
            parts.Add(s.Key);
            chords[s.Command] = string.Join("+", parts);
        }
        return chords;
    }

    /// <summary>Guards the guard: a renamed command or a broken reader would check nothing.</summary>
    [Fact]
    public void EveryDrawingToolHasADefaultChord()
    {
        var chords = DefaultChords();
        var missing = ToolWords.Keys.Where(c => !chords.ContainsKey(c)).ToList();
        Assert.True(missing.Count == 0, "No default binding found for: " + string.Join(", ", missing));
        Assert.Equal(ToolWords.Count, chords.Count);
    }

    /// <summary>
    /// Every chord is unique. Two drawing tools on one chord is a defect in the profile, and it
    /// would also make the pairing check below meaningless.
    /// </summary>
    [Fact]
    public void NoTwoDrawingToolsShareAChord()
    {
        var byChord = DefaultChords().GroupBy(kv => kv.Value).Where(g => g.Count() > 1).ToList();
        Assert.True(byChord.Count == 0,
            "Shared chords: " + string.Join("; ", byChord.Select(g => $"{g.Key} -> {string.Join(", ", g.Select(kv => kv.Key))}")));
    }

    /// <summary>
    /// <b>The real check.</b> No documented unit may put a drawing chord alongside the name of a
    /// DIFFERENT drawing tool without naming its own.
    ///
    /// <para>
    /// The unit is a markdown table ROW or a whole prose PARAGRAPH, and arriving at that took two
    /// wrong answers worth recording. A per-LINE check misses a wrapped sentence, where the chord
    /// and its tool name land on different lines — a false negative on the one thing the guard
    /// exists to catch. A nearest-name-within-N-characters check instead reported twenty-two
    /// offences that were all the same table: in <c>| Trendline | Alt+Shift+T |</c> the chord sits
    /// at the end of its row and the NEXT row's tool name is closer than its own.
    /// </para>
    ///
    /// <para>
    /// A row is self-contained and a paragraph is self-contained; both are how a reader takes the
    /// text in. So: join consecutive prose lines into one unit, keep each table row as its own,
    /// and ask of each unit whether a chord in it appears without its tool but with another's.
    /// </para>
    /// </summary>
    [Fact]
    public void NoDocumentedUnitPairsAChordWithTheWrongTool()
    {
        string root = RepoRoot();
        var chords = DefaultChords();
        var offenders = new List<string>();

        foreach (string relative in Docs)
        {
            string path = Path.Combine(root, relative);
            if (!File.Exists(path)) continue;

            foreach (var (unit, line) in Units(File.ReadAllLines(path)))
            {
                string lower = unit.ToLowerInvariant();

                foreach (var (command, chord) in chords)
                {
                    // Whole chord only: "Alt+Shift+P" must not match inside "Ctrl+Alt+Shift+P".
                    if (!Regex.IsMatch(unit, $@"(?<![\w+]){Regex.Escape(chord)}(?![\w+])",
                                       RegexOptions.IgnoreCase))
                        continue;

                    if (Names(lower, command)) continue;   // names its own

                    var wrong = ToolWords.Keys
                        .Where(other => other != command && Names(lower, other))
                        .Select(other => other.ToString())
                        .ToList();

                    if (wrong.Count > 0)
                        offenders.Add($"{relative}:{line}  {chord} opens {command}, but the text names "
                                    + $"{string.Join("/", wrong)} and not {command}\n    {Trim(unit)}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} documented chord(s) paired with the wrong drawing tool:\n  "
          + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Whether the text names this tool, matched on WHOLE WORDS.
    ///
    /// <para>
    /// A plain <c>Contains</c> found the angle tool inside "rect<b>angle</b>", so every sentence
    /// about the rectangle read as one about the angle fib as well. The words here are short and
    /// ordinary enough that substring matching was always going to do this to one of them.
    /// </para>
    /// </summary>
    private static bool Names(string lowerText, SystemCommand command) =>
        ToolWords[command].Any(w =>
            Regex.IsMatch(lowerText, $@"(?<![a-z]){Regex.Escape(w)}(?![a-z])"));

    /// <summary>
    /// The document as self-contained units: one per table row, one per prose paragraph. Returns
    /// the unit's text and the line it starts on.
    /// </summary>
    private static IEnumerable<(string Text, int Line)> Units(string[] lines)
    {
        var paragraph = new List<string>();
        int paragraphStart = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            bool isRow = line.TrimStart().StartsWith('|');
            bool isBlank = string.IsNullOrWhiteSpace(line);

            // A LIST ITEM and a BLOCKQUOTE LINE are units of their own, like a table row.
            // Gluing them into one paragraph is what produced the last round of false positives:
            // a numbered list whose step 2 uses Alt+Shift+T as a generic example and whose step 5
            // lists the three-anchor tools became one "unit" naming a chord and three other tools.
            // A reader takes a list item as one thought; so does this.
            bool isListItem = Regex.IsMatch(line, @"^\s*(\d+\.|[-*+])\s");
            bool isQuote = line.TrimStart().StartsWith('>');

            if (isRow || isBlank || isListItem || isQuote)
            {
                if (paragraph.Count > 0)
                {
                    yield return (string.Join(" ", paragraph), paragraphStart + 1);
                    paragraph.Clear();
                }
                if (!isBlank) yield return (line, i + 1);
                continue;
            }

            if (paragraph.Count == 0) paragraphStart = i;
            paragraph.Add(line);
        }

        if (paragraph.Count > 0) yield return (string.Join(" ", paragraph), paragraphStart + 1);
    }

    private static string Trim(string unit) =>
        unit.Length <= 150 ? unit.Trim() : unit.Trim()[..150] + "…";

    /// <summary>
    /// And the three Cody named, pinned by value. A general property can be satisfied by a doc
    /// that mentions no tool at all; this says what the answers are.
    /// </summary>
    [Theory]
    [InlineData(SystemCommand.DrawMeasure, "Alt+Shift+M")]
    [InlineData(SystemCommand.DrawRiskReward, "Alt+Shift+P")]
    [InlineData(SystemCommand.DrawRectangle, "Alt+Shift+R")]
    public void TheThreeThatWereConfused(SystemCommand command, string expected)
        => Assert.Equal(expected, DefaultChords()[command]);
}
