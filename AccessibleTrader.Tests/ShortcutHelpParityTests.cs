using System.Text.RegularExpressions;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Input;

namespace AccessibleTrader.Tests;

/// <summary>
/// The keyboard is the whole interface here, and it is written down in three places:
/// <see cref="ShortcutManager"/>'s default profile (what actually happens),
/// <c>HelpModal.razor</c> (what F1 says), and <c>docs/SHORTCUTS.md</c> (what the manual says).
/// Nothing compared them.
///
/// <para>
/// A2/F6 filed this as "nothing pins the keyboard shortcut table", and writing it found the
/// drift immediately: <b>37 of the 124 default bindings appeared nowhere in the in-app help</b>,
/// including every quick-trade chord — the keys that place and size real orders — plus tab
/// management, workspace save/load and the whole orientation-and-recovery family. Worse than
/// missing, one row was wrong: F4 was documented as "Speak current context snapshot", which it
/// stopped being at the 2026-07-21 F-key redesign (it toggles braille; the snapshot moved to
/// Shift+F1). The canonical <c>docs/SHORTCUTS.md</c> was, and is, complete — so the copy that
/// had rotted is the one a blind user reaches without leaving the terminal.
/// </para>
///
/// <para>
/// Both directions are asserted, because they fail differently. A combination documented but not
/// bound is a key the user presses and nothing happens. A binding that exists but is documented
/// nowhere is a feature that, for a keyboard-only user, does not exist.
/// </para>
/// </summary>
public class ShortcutHelpParityTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string HelpModalSource() => File.ReadAllText(Path.Combine(
        RepoRoot(), "AccessibleTrader.BlazorClient.Components", "HelpModal.razor"));

    private static string ShortcutsDoc() => File.ReadAllText(Path.Combine(
        RepoRoot(), "docs", "SHORTCUTS.md"));

    private sealed class TempPaths : IPlatformPathService
    {
        public TempPaths(string root) { AppDataDirectory = root; CacheDirectory = root; }
        public string AppDataDirectory { get; }
        public string CacheDirectory { get; }
    }

    /// <summary>A key combination, canonicalised so the three sources are comparable.</summary>
    private readonly record struct Combo(string Key, bool Shift, bool Ctrl, bool Alt)
    {
        public override string ToString()
        {
            var parts = new List<string>();
            if (Ctrl) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            parts.Add(Key);
            return string.Join("+", parts);
        }
    }

    /// <summary>
    /// Prose spellings a human writes for a key, mapped onto the spelling the bindings use.
    /// This layer sits IN FRONT of the production
    /// <see cref="KeyNormalizationService.NormalizeKey"/>, which handles the platform aliases
    /// (ArrowLeft, OEM_4, Enter/Return) but knows nothing about "←" or "Page Up" — those are
    /// documentation words, not key events. Every entry here is a spelling that appears in one
    /// of the two documents.
    /// </summary>
    private static readonly Dictionary<string, string> DisplayAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["←"] = "LEFT", ["→"] = "RIGHT", ["↑"] = "UP", ["↓"] = "DOWN",
        ["Page Up"] = "PAGEUP", ["Page Down"] = "PAGEDOWN",
        // The bindings carry both an OEM spelling and the bare character for the punctuation
        // keys; collapse onto the OEM one so two bindings for one physical key are one Combo.
        ["\\"] = "OEM5", ["["] = "OEM4", ["]"] = "OEM6", ["-"] = "OEMMINUS", ["="] = "OEMPLUS",
        [" "] = "SPACE", ["Spacebar"] = "SPACE",
        ["Esc"] = "ESCAPE", ["Del"] = "DELETE", ["Application"] = "CONTEXTMENU",
    };

    /// <summary>
    /// Splits a documentation cell into the alternatives it lists — "Ctrl+← / Ctrl+→" is two
    /// bindings on one row.
    ///
    /// <para>
    /// The separator is a SPACED slash, and that is not a stylistic preference: <c>/</c> is
    /// itself a key. Splitting on a bare slash tore "Alt+Shift+/" into "Alt+Shift+" and "", so
    /// the one chord bound to the slash could not be documented in a form this harness would
    /// accept — the guard demanded documentation and rejected every spelling of it.
    /// </para>
    /// </summary>
    private static IEnumerable<string> Alternatives(string cell) =>
        Regex.Split(cell, @"\s+/\s+");

    private static Combo? Parse(string token)
    {
        token = token.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Trim();
        if (token.Length == 0) return null;

        var parts = token.Split('+', StringSplitOptions.TrimEntries).ToList();
        bool ctrl = false, alt = false, shift = false;
        while (parts.Count > 0)
        {
            string head = parts[0].ToUpperInvariant();
            if (head == "CTRL") ctrl = true;
            else if (head == "ALT") alt = true;
            else if (head == "SHIFT") shift = true;
            else break;
            parts.RemoveAt(0);
        }
        if (parts.Count != 1 || parts[0].Length == 0) return null;

        string key = parts[0];
        if (DisplayAliases.TryGetValue(key, out var aliased)) key = aliased;
        key = KeyNormalizationService.NormalizeKey(key).ToUpperInvariant();
        return key.Length == 0 ? null : new Combo(key, shift, ctrl, alt);
    }

    /// <summary>The default profile, as a set of canonical combinations.</summary>
    private static Dictionary<Combo, SystemCommand> DefaultBindings()
    {
        var dir = TestTemp.NewDir("att-shortcut-parity-");
        try
        {
            var mgr = new ShortcutManager(new TempPaths(dir));
            var map = new Dictionary<Combo, SystemCommand>();
            foreach (var s in mgr.CurrentProfile.Shortcuts)
            {
                var combo = Parse((s.Ctrl ? "Ctrl+" : "") + (s.Alt ? "Alt+" : "")
                                  + (s.Shift ? "Shift+" : "") + s.Key);
                if (combo is { } c) map.TryAdd(c, s.Command);
            }
            return map;
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ } }
    }

    /// <summary>The key column of every shortcut row in the Help dialog's static tables.</summary>
    private static List<(string Raw, string Token, string Description)> HelpKeyCells()
    {
        var rows = new List<(string, string, string)>();
        foreach (Match m in Regex.Matches(HelpModalSource(), @"<tr><td>([^<]*)</td><td>([^<]*)</td>"))
            foreach (var token in Alternatives(m.Groups[1].Value))
                rows.Add((m.Groups[1].Value, token, m.Groups[2].Value));
        return rows;
    }

    /// <summary>
    /// Every combination NAMED as a key anywhere in the Help dialog — the table key cells plus
    /// the <c>&lt;strong&gt;</c>/<c>&lt;kbd&gt;</c> emphasis the prose sections use. Deliberately
    /// wider than <see cref="HelpKeyCells"/>: for the "is it documented at all" direction, a
    /// shortcut explained in a paragraph counts.
    /// </summary>
    private static HashSet<Combo> DocumentedInHelp()
    {
        string src = HelpModalSource();
        var found = new HashSet<Combo>();
        void Harvest(string pattern)
        {
            foreach (Match m in Regex.Matches(src, pattern))
                foreach (var token in Alternatives(m.Groups[1].Value))
                    if (Parse(token) is { } c) found.Add(c);
        }
        Harvest(@"<td>([^<]*)</td>");
        Harvest(@"<(?:strong|kbd)>([^<]*)</(?:strong|kbd)>");
        return found;
    }

    /// <summary>The same for the markdown manual: table first columns, bold, and code spans.</summary>
    private static HashSet<Combo> DocumentedInManual()
    {
        string src = ShortcutsDoc();
        var found = new HashSet<Combo>();
        void Add(string cell)
        {
            foreach (var token in Alternatives(cell))
                if (Parse(token) is { } c) found.Add(c);
        }
        foreach (Match m in Regex.Matches(src, @"`([^`]+)`")) Add(m.Groups[1].Value);
        foreach (Match m in Regex.Matches(src, @"\*\*([^*]+)\*\*")) Add(m.Groups[1].Value);
        foreach (var line in src.Split('\n'))
        {
            if (!line.TrimStart().StartsWith('|')) continue;
            var cells = line.Trim().Trim('|').Split('|');
            if (cells.Length > 0) Add(cells[0]);
        }
        return found;
    }

    /// <summary>
    /// Rows in the Help dialog whose key column is real and correct but is NOT a ShortcutManager
    /// binding. Every entry needs a reason, because the alternative reading of an entry here is
    /// "we could not make the test pass".
    /// </summary>
    private static readonly Dictionary<string, string> NotShortcutManagerBindings = new()
    {
        ["TAB"] = "browser/host focus order, not a bindable command",
        ["SHIFT+TAB"] = "browser/host focus order, not a bindable command",
    };

    // ── Forward: what the Help dialog tells you to press must do something ───

    [Fact]
    public void EveryKeyTheHelpDialogListsIsBoundToACommand()
    {
        var bindings = DefaultBindings();
        var broken = new List<string>();
        int checkedCount = 0;

        foreach (var (raw, token, _) in HelpKeyCells())
        {
            var combo = Parse(token);
            if (combo is not { } c)
            {
                broken.Add($"'{token}' (in row '{raw}') is not a parseable key combination");
                continue;
            }
            if (NotShortcutManagerBindings.ContainsKey(c.ToString().ToUpperInvariant())) continue;
            checkedCount++;
            if (!bindings.ContainsKey(c))
                broken.Add($"'{token.Trim()}' → {c} is documented in the Help dialog and bound to nothing");
        }

        // Vacuity: a regex that stopped matching would report zero problems and zero checks.
        Assert.True(checkedCount > 60,
            $"only {checkedCount} key cells were parsed out of HelpModal.razor — the table markup changed "
            + "shape and this test is no longer reading it.");
        Assert.True(broken.Count == 0, string.Join("\n", broken));
    }

    // ── Reverse: everything that is bound must be written down ───────────────

    [Fact]
    public void EveryDefaultBindingIsDocumentedInTheHelpDialog()
    {
        var documented = DocumentedInHelp();
        var missing = DefaultBindings()
            .Where(kv => !documented.Contains(kv.Key))
            .Select(kv => $"{kv.Key}  ({kv.Value})")
            .OrderBy(s => s)
            .ToList();

        Assert.True(missing.Count == 0,
            $"{missing.Count} default binding(s) exist and the F1 Help dialog never mentions them. "
            + "For a keyboard-only user that is the same as not shipping them:\n  "
            + string.Join("\n  ", missing));
    }

    [Fact]
    public void EveryDefaultBindingIsDocumentedInTheShortcutsManual()
    {
        var documented = DocumentedInManual();
        var missing = DefaultBindings()
            .Where(kv => !documented.Contains(kv.Key))
            .Select(kv => $"{kv.Key}  ({kv.Value})")
            .OrderBy(s => s)
            .ToList();

        Assert.True(missing.Count == 0,
            $"{missing.Count} default binding(s) are missing from docs/SHORTCUTS.md:\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>
    /// The vacuity check for the two reverse tests. Both pass trivially if the harvesters return
    /// everything or the binding table returns nothing, so pin the magnitudes: the profile is
    /// over a hundred combinations and each document names most of them.
    /// </summary>
    [Fact]
    public void TheParitySourcesAreAllNonEmpty()
    {
        var bindings = DefaultBindings();
        Assert.True(bindings.Count > 100, $"only {bindings.Count} default bindings parsed");
        Assert.True(DocumentedInHelp().Count > 100, "the Help dialog harvester found almost nothing");
        Assert.True(DocumentedInManual().Count > 100, "the manual harvester found almost nothing");
        // And it is genuinely discriminating — a harvester that accepted every string would
        // contain this.
        Assert.DoesNotContain(new Combo("BANANA", false, false, false), DocumentedInHelp());
    }

    // ── The row that was wrong rather than missing ───────────────────────────

    /// <summary>
    /// A missing row is invisible; a wrong row is worse, because the user believes it. F4 was
    /// documented as "Speak current context snapshot" for the five weeks after the F-key mute
    /// grammar moved that to Shift+F1 and gave F4 to braille.
    ///
    /// <para>
    /// Scoped to <c>Toggle*</c> commands on purpose. Those name exactly one thing each — the
    /// thing they toggle — so "the description must contain that word" is a real claim rather
    /// than a wording preference. Descriptions for the rest of the table are prose and are left
    /// alone; asserting on them would be a style guard wearing a correctness guard's clothes.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryToggleShortcutSaysWhatItToggles()
    {
        var bindings = DefaultBindings();
        var wrong = new List<string>();
        int checkedCount = 0;

        foreach (var (raw, token, description) in HelpKeyCells())
        {
            if (Parse(token) is not { } combo) continue;
            if (!bindings.TryGetValue(combo, out var command)) continue;

            string name = command.ToString();
            if (!name.StartsWith("Toggle", StringComparison.Ordinal)) continue;

            // "ToggleHeikinAshi" → the subject words are Heikin, Ashi. ANY of them is enough:
            // the wording is allowed to paraphrase ("Toggle audio mute on focused series" for
            // ToggleIndicatorAudio names the audio, not the indicator) but it may not describe
            // a different feature entirely, which is what F4 did. Words under four letters are
            // skipped — "Log" matches too much English to be evidence.
            var subject = Regex.Matches(name["Toggle".Length..], "[A-Z][a-z0-9]*")
                               .Select(w => w.Value)
                               .Where(w => w.Length >= 4)
                               .ToList();
            if (subject.Count == 0) continue;

            checkedCount++;
            if (subject.Any(w => description.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;
            wrong.Add($"'{raw}' → {command}, but the Help dialog describes it as "
                      + $"\"{description}\" — no mention of {string.Join(" or ", subject)}");
        }

        Assert.True(checkedCount >= 6,
            $"only {checkedCount} Toggle* rows were reached; the parser is no longer finding them");
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }
}
