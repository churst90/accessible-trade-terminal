using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Dialog surfaces must take their colours from the theme, never from a literal.
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// Dialogs used to be a fixed light panel (<c>#f2f2f2</c>) with <c>#111</c> ink. The
    /// panel was moved onto <c>var(--bg-surface)</c>; four more-specific rules —
    /// <c>.modal-content h2</c>, <c>.modal-content label</c>, <c>.object-tree-item</c> and
    /// <c>.shortcuts-table</c> — were left behind, so every dialog heading and form label
    /// was black ink on a dark panel at roughly 1.2:1. <c>.shortcuts-table</c> also pinned
    /// its header and zebra stripe light, so rows alternated readable and unreadable.
    /// </para>
    ///
    /// <para>
    /// **Screen-reader users were unaffected, which is exactly why it survived** — the app's
    /// most-exercised path is the one that cannot see this. The people it hit are the
    /// low-vision half of the audience, and nothing in the suite was looking. This test is
    /// that missing pair of eyes: it does not judge contrast ratios, it enforces the one
    /// structural property that makes contrast a theme decision rather than an accident.
    /// </para>
    ///
    /// <para>
    /// There are two copies of app.css (MAUI client and WebHost) and they have already
    /// drifted apart. Both are scanned, so a fix cannot land in one and miss the other.
    /// </para>
    /// </summary>
    public class DialogContrastScanTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static IEnumerable<string> StyleSheets()
        {
            foreach (var rel in new[]
                     {
                         Path.Combine("AccessibleTrader.BlazorClient", "wwwroot", "app.css"),
                         Path.Combine("AccessibleTrader.WebHost", "wwwroot", "app.css"),
                     })
            {
                string full = Path.Combine(RepoRoot(), rel);
                if (File.Exists(full)) yield return full;
            }
        }

        /// <summary>
        /// Comments removed first. A rule is code; a note explaining what the colour USED to
        /// be is not, and a scanner that cannot tell them apart makes it impossible to
        /// document the very fix it is guarding.
        /// </summary>
        internal static string StripComments(string css) =>
            Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);

        /// <summary>Selector-plus-body pairs, comments already gone.</summary>
        internal static IEnumerable<(string Selector, string Body)> Rules(string css)
        {
            foreach (Match m in Regex.Matches(StripComments(css), @"([^{}]+)\{([^{}]*)\}"))
                yield return (m.Groups[1].Value.Trim(), m.Groups[2].Value);
        }

        // Anything rendered inside a dialog. These are the surfaces that follow
        // --bg-surface, so their foregrounds have to follow --text-on-surface.
        private static readonly string[] DialogScoped =
            { ".modal-content", ".object-tree-item", ".shortcuts-table" };

        // Colour-bearing properties. A literal in any of these can strand text on a
        // surface it was never checked against.
        private static readonly Regex ColourDecl = new(
            @"(?<prop>color|background|background-color|border|border-top|border-bottom|border-left|border-right)\s*:\s*(?<value>[^;]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex Literal = new(
            @"#[0-9a-fA-F]{3,8}\b|\brgba?\s*\(|\bhsla?\s*\(", RegexOptions.Compiled);

        [Fact]
        public void NoDialogScopedRuleHardcodesAColour()
        {
            var failures = new List<string>();
            int rulesChecked = 0;

            foreach (var sheet in StyleSheets())
            {
                string name = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(sheet))!)
                            + "/" + Path.GetFileName(sheet);

                foreach (var (selector, body) in Rules(sheet is null ? "" : File.ReadAllText(sheet)))
                {
                    if (!DialogScoped.Any(d => selector.Contains(d, StringComparison.Ordinal))) continue;
                    rulesChecked++;

                    foreach (Match d in ColourDecl.Matches(body))
                    {
                        string value = d.Groups["value"].Value;

                        // color-mix over a variable is the sanctioned way to tint a themed
                        // colour; the literal inside it (e.g. `60%, #000`) is a mix weight,
                        // not a surface colour, and it moves with the theme.
                        if (value.Contains("color-mix", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!Literal.IsMatch(value)) continue;

                        failures.Add($"{name}  {selector} {{ {d.Groups["prop"].Value}: {value.Trim()} }}");
                    }
                }
            }

            Assert.True(rulesChecked > 0,
                "Scanned no dialog-scoped rules — the stylesheet moved and this test is now decorative.");

            Assert.True(failures.Count == 0,
                "Dialog-scoped rules with hardcoded colours. These do not move when the theme does, "
              + "which is how dialog headings ended up as black ink on a dark panel at ~1.2:1. Use "
              + "var(--text-on-surface) / var(--border-color), or color-mix over one of them:\n  "
              + string.Join("\n  ", failures));
        }

        [Fact]
        public void TheScannerWouldHaveCaughtTheOriginalDefect()
        {
            // The exact rules as they shipped. If this ever stops reporting them, the
            // scanner has broken rather than the CSS having been fixed — check which
            // before touching anything.
            const string css = @"
                .modal-content h2 { font-size: 1.1rem; color: #111; }
                .shortcuts-table tr:nth-child(even) { background: #e8e8e8; }
                .modal-content label { color: var(--text-on-surface); font-weight: 600; }
                .modal-content input { background: color-mix(in srgb, var(--bg-surface) 60%, #000); }
                #blazor-error-ui { background: #700; }
            ";

            var flagged = new List<string>();
            foreach (var (selector, body) in Rules(css))
            {
                if (!DialogScoped.Any(d => selector.Contains(d, StringComparison.Ordinal))) continue;
                foreach (Match d in ColourDecl.Matches(body))
                {
                    string v = d.Groups["value"].Value;
                    if (v.Contains("color-mix", StringComparison.OrdinalIgnoreCase)) continue;
                    if (Literal.IsMatch(v)) flagged.Add(selector);
                }
            }

            Assert.Contains(".modal-content h2", flagged);
            Assert.Contains(".shortcuts-table tr:nth-child(even)", flagged);

            // A themed value, a color-mix tint, and a non-dialog rule are all fine.
            Assert.DoesNotContain(".modal-content label", flagged);
            Assert.DoesNotContain(".modal-content input", flagged);
            Assert.DoesNotContain("#blazor-error-ui", flagged);
        }

        [Fact]
        public void TheScannerIgnoresColoursNamedInComments()
        {
            // The sibling scanner in DispatcherAffinityScanTests failed this way on its
            // first run, flagging the comment that explained the bug. Same trap, so the
            // same assertion is made here rather than learned twice.
            const string css = @"
                /* These were a fixed #f2f2f2 panel with #111 ink. */
                .modal-content h2 { color: var(--text-on-surface); }
            ";

            var flagged = Rules(css)
                .Where(r => DialogScoped.Any(d => r.Selector.Contains(d, StringComparison.Ordinal)))
                .SelectMany(r => ColourDecl.Matches(r.Body).Cast<Match>())
                .Where(d => Literal.IsMatch(d.Groups["value"].Value))
                .ToList();

            Assert.Empty(flagged);
        }
    }
}
