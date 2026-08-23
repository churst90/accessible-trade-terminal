using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The culture contract, in two layers.
    ///
    /// Layer 1 — the hosts. Everything this app emits is English and dot-decimal: spoken
    /// numbers, JSON order payloads, request URLs, lab CSVs. Nothing is localized. So every
    /// host pins <c>CultureInfo.DefaultThreadCurrentCulture</c> to invariant before it does
    /// anything else. That is the only fix that covers bare <c>$"{value}"</c> interpolation
    /// of a double — a surface no source scan can enumerate. The first test here checks the
    /// pin exists AND that it precedes the host's first real action, because a pin that runs
    /// after the host has started formatting is a comment, not a fix (the Polygon lesson:
    /// presence of the right call is not the same as no path around it).
    ///
    /// Layer 2 — the call sites. Providers also run under test hosts and could be rehosted,
    /// so wire-facing parse/format sites carry InvariantCulture explicitly as defense in
    /// depth. The audit found the class live: on de-DE, Coinbase's
    /// <c>double.TryParse(o["filled_size"]?.ToString())</c> reads "0.5" as 5.0 — a blind
    /// trader fills 0.5 BTC and hears "filled 5 BTC"; under th-TH, dates render in the
    /// Buddhist calendar and every range request silently returns nothing.
    ///
    /// The remaining offenders are held in per-file ratchet baselines below. The rules:
    /// a file may never exceed its baseline (no new offenders), and a baseline may never
    /// exceed the file (fixes must tighten the ratchet in the same commit, so the baseline
    /// stays a record of debt, not a mute button). Fix a site, shrink its number; the test
    /// output prints the corrected dictionary when either direction drifts.
    /// </summary>
    public class CultureInvariantScanTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        // ------------------------------------------------------------------
        // Layer 1: every host pins invariant culture before its first action.
        // ------------------------------------------------------------------

        /// <summary>Host entry point → a marker for its first real action.</summary>
        private static readonly (string File, string FirstAction)[] Hosts =
        {
            ("AccessibleTrader.WebHost/Program.cs",        "WebApplication.CreateBuilder"),
            ("AccessibleTrader.BlazorClient/MauiProgram.cs", "MauiApp.CreateBuilder"),
            ("AccessibleTrader.ScriptWorker/Program.cs",   "new WorkerDispatcher"),
            ("AccessibleTrader.StrategyLab/Program.cs",    "args[0].ToLowerInvariant"),
        };

        [Fact]
        public void EveryHostPinsInvariantCultureBeforeItsFirstRealAction()
        {
            var problems = new List<string>();
            foreach (var (relPath, firstAction) in Hosts)
            {
                string path = Path.Combine(RepoRoot(), relPath);
                Assert.True(File.Exists(path), $"Host entry point moved: {relPath}");
                string text = File.ReadAllText(path);

                int pinCulture = text.IndexOf(
                    "CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture",
                    StringComparison.Ordinal);
                int pinUi = text.IndexOf(
                    "CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture",
                    StringComparison.Ordinal);
                int action = text.IndexOf(firstAction, StringComparison.Ordinal);

                if (pinCulture < 0 || pinUi < 0)
                    problems.Add($"{relPath}: missing the invariant-culture pin (need both "
                        + "DefaultThreadCurrentCulture and DefaultThreadCurrentUICulture).");
                else if (action < 0)
                    problems.Add($"{relPath}: first-action marker \"{firstAction}\" not found — "
                        + "the host changed shape; update the marker so the ordering check "
                        + "still means something.");
                else if (pinCulture > action || pinUi > action)
                    problems.Add($"{relPath}: the culture pin appears AFTER \"{firstAction}\". "
                        + "A pin that runs after the host starts working is not a pin.");
            }

            Assert.True(problems.Count == 0,
                "A host does not pin invariant culture up front. On a comma-decimal or "
                + "non-Gregorian locale, every bare $\"{value}\" in that host formats wrong.\n  "
                + string.Join("\n  ", problems));
        }

        // ------------------------------------------------------------------
        // Layer 2a: culture-sensitive number/date PARSING in Plugins/ + Sdk/.
        // ------------------------------------------------------------------

        /// <summary>
        /// Remaining unpinned parse sites, per file. Ratchet: shrink as they are fixed.
        /// Verified 2026-08-23 against the audit item ("Coinbase parses every number with the
        /// ambient culture") plus the twin sweep that found Bitstamp, Alpaca and the analytics
        /// providers doing the same.
        /// </summary>
        // Burned to zero 2026-08-23 (49 sites across 10 files, Coinbase's 21 first). Keep it
        // empty: any entry appearing here again is a regression, not a baseline.
        private static readonly Dictionary<string, int> ParseBaseline = new(StringComparer.Ordinal)
        {
        };

        private static readonly Regex ParseCall = new(
            @"\b(double|decimal|float|DateTime|DateTimeOffset)\.(Try)?Parse(Exact)?\s*\(",
            RegexOptions.Compiled);

        [Fact]
        public void CultureSensitiveParsingOnlyShrinks()
        {
            AssertRatchet(ParseBaseline, file => CountOffenders(file, ParseCall, span =>
                    !span.Contains("Invariant", StringComparison.Ordinal)
                    && !span.Contains("CultureInfo", StringComparison.Ordinal)
                    && !span.Contains("NumberStyles", StringComparison.Ordinal)),
                "a culture-sensitive Parse. On de-DE, double.Parse(\"0.5\") is 5.0 — fills, "
                + "balances and candles come back 10x wrong, silently. Pass "
                + "CultureInfo.InvariantCulture (or use JToken's .Value<double>(), which is "
                + "invariant by construction).");
        }

        // ------------------------------------------------------------------
        // Layer 2b: dates FORMATTED with the ambient culture in Plugins/ + Sdk/.
        // ------------------------------------------------------------------

        /// <summary>
        /// Remaining ambient-culture date-format sites, per file. Ratchet: shrink as fixed.
        /// Covers BOTH spellings — .ToString("yyyy-MM-dd") and interpolated {dt:yyyy-MM-dd} —
        /// because Oanda and TwelveData use the interpolated form and a ToString-only grep
        /// walked straight past them (found 2026-08-23).
        /// </summary>
        // Burned to zero 2026-08-23. Of the 39 sites first counted, 4 were scan false
        // positives (the newline fix on DateInterpolation below); the other 35 were real and
        // are fixed. Keep it empty: any entry here again is a regression, not a baseline.
        private static readonly Dictionary<string, int> DateFormatBaseline = new(StringComparer.Ordinal)
        {
        };

        // A ToString whose format string contains a date component.
        private static readonly Regex DateToString = new(
            @"\.ToString\(\s*""[^""]*(yyyy|MM|dd|HH:mm)[^""]*""",
            RegexOptions.Compiled);

        // An interpolation hole with a date format. A hole cannot carry a culture, so the only
        // clean forms are FormattableString.Invariant($"...") / string.Create(invariant, $"...")
        // — both put "Invariant" on the line, which is what the span check looks for.
        // Newlines are excluded from the hole on purpose: without that, a multi-line { } code
        // block whose comment happens to contain a colon and "dd" matches — the first run of
        // this scan reported 4 such false positives in SignalDescriptor/Okx/Finra/Wikipedia.
        private static readonly Regex DateInterpolation = new(
            @"\{[^{}""\r\n]*:[^{}""\r\n]*(yyyy|MM|dd|HH:mm)[^{}""\r\n]*\}",
            RegexOptions.Compiled);

        [Fact]
        public void AmbientCultureDateFormattingOnlyShrinks()
        {
            AssertRatchet(DateFormatBaseline, file =>
                    CountOffenders(file, DateToString, span =>
                        !span.Contains("Invariant", StringComparison.Ordinal)
                        && !span.Contains("CultureInfo", StringComparison.Ordinal))
                    + CountOffenders(file, DateInterpolation, span =>
                        !span.Contains("Invariant", StringComparison.Ordinal)),
                "a date formatted with the ambient culture into a request. Under th-TH the year "
                + "renders in the Buddhist calendar (2026 → 2569); the venue returns nothing and "
                + "the chart is blank with no error. Use CultureInfo.InvariantCulture — for the "
                + "interpolated spelling, wrap with FormattableString.Invariant($\"...\").");
        }

        // ------------------------------------------------------------------
        // Shared machinery.
        // ------------------------------------------------------------------

        private static readonly string[] ScannedRoots = { "Plugins", "AccessibleTrader.Sdk" };

        private static IEnumerable<string> SourceFiles()
        {
            string root = RepoRoot();
            foreach (var top in ScannedRoots)
            {
                string dir = Path.Combine(root, top);
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                        continue;
                    yield return file;
                }
            }
        }

        /// <summary>
        /// Count matches whose surrounding call span fails <paramref name="isOffender"/>.
        /// The span runs from the match to the balanced closing paren (or line end for
        /// interpolations), so a culture argument on a later line of a multi-line call is
        /// seen — Coinbase's open-orders TryParse spans four lines.
        /// </summary>
        private static int CountOffenders(string file, Regex pattern, Func<string, bool> isOffender)
        {
            string text = File.ReadAllText(file);
            int count = 0;
            foreach (Match m in pattern.Matches(text))
            {
                string span = BalancedSpan(text, m.Index);
                // For interpolation matches (no trailing open paren) fall back to the line.
                if (span.Length == 0) span = LineContaining(text, m.Index);
                if (isOffender(span)) count++;
            }
            return count;
        }

        private static string BalancedSpan(string text, int from)
        {
            int open = text.IndexOf('(', from);
            if (open < 0 || open - from > 200) return "";
            // If a line break appears before the open paren this was not a call site.
            if (text.AsSpan(from, open - from).Contains('\n')) return "";
            int depth = 0;
            for (int i = open; i < text.Length && i < open + 2000; i++)
            {
                if (text[i] == '(') depth++;
                else if (text[i] == ')' && --depth == 0)
                    return text.Substring(from, i - from + 1);
            }
            return "";
        }

        private static string LineContaining(string text, int at)
        {
            int start = text.LastIndexOf('\n', Math.Min(at, text.Length - 1)) + 1;
            int end = text.IndexOf('\n', at);
            return text.Substring(start, (end < 0 ? text.Length : end) - start);
        }

        private static void AssertRatchet(
            Dictionary<string, int> baseline, Func<string, int> countIn, string offenderNoun)
        {
            string root = RepoRoot();
            var actual = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var file in SourceFiles())
            {
                int n = countIn(file);
                if (n > 0)
                    actual[Path.GetRelativePath(root, file).Replace('\\', '/')] = n;
            }

            var grew = actual.Where(kv => kv.Value > baseline.GetValueOrDefault(kv.Key))
                             .Select(kv => $"{kv.Key}: {baseline.GetValueOrDefault(kv.Key)} → {kv.Value}")
                             .ToList();
            var slack = baseline.Where(kv => kv.Value > actual.GetValueOrDefault(kv.Key))
                                .Select(kv => $"{kv.Key}: baseline {kv.Value}, actual {actual.GetValueOrDefault(kv.Key)}")
                                .ToList();

            string dictionary = string.Join("\n",
                actual.Select(kv => $"            [\"{kv.Key}\"] = {kv.Value},"));

            Assert.True(grew.Count == 0,
                $"A file gained {offenderNoun}\nGrew:\n  " + string.Join("\n  ", grew)
                + "\n\nIf every new site is genuinely pinned and this is a scan defect, the "
                + "current full count is:\n" + dictionary);

            Assert.True(slack.Count == 0,
                "Offenders were fixed but the baseline was not tightened — a slack baseline is "
                + "how a record of debt becomes permission for new debt. Update the baseline "
                + "to:\n" + dictionary);
        }
    }
}
