using System.Text.RegularExpressions;
using AccessibleTrader.Core.Services.Analysis;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>Nothing decides support-versus-resistance except <see cref="LevelPolarity"/>.</b>
    ///
    /// <para>
    /// ── Why this exists ────────────────────────────────────────────────────────
    /// This is not a defect report. Both known instances were fixed before this file was written.
    /// It is the RECURRENCE that earns the guard: the same one-line invariant was got wrong twice
    /// in three weeks, by two different wrong proxies, and each was fixed only at the site where
    /// somebody happened to notice it.
    /// </para>
    ///
    /// <list type="number">
    /// <item><description>
    /// <b>By sound.</b> <c>NavigationFeedbackManager</c> classified a zone line with
    /// <c>if ((float)comp.BaseFrequency >= 500f)</c>. Frequency is a sonification setting, so a
    /// level voiced for audibility rather than semantics was announced as the opposite structural
    /// level — and the 500 was undocumented.
    /// </description></item>
    /// <item><description>
    /// <b>By spelling.</b> <c>AutoNarrationService</c> classified one with two literal
    /// <c>Contains</c> calls, <c>"Resistance"</c> and <c>"resistance"</c>. <c>RESISTANCE_1</c> and
    /// <c>res_upper</c> fell through, so a resistance break was announced as
    /// <i>"Support at 61,200 broken."</i>
    /// </description></item>
    /// </list>
    ///
    /// <para>
    /// Both produce the same failure: the application states, out loud and with no visual to
    /// contradict it, the OPPOSITE directional claim about the level a trader is about to act on.
    /// The point of this test is to make a third variant unwritable rather than to wait for a
    /// third one to be found.
    /// </para>
    ///
    /// <para>
    /// ── What is enforced ───────────────────────────────────────────────────────
    /// Over the narration layer (<c>AccessibleTrader.Core/Services/Accessibility</c>) plus any file
    /// anywhere that already calls into <see cref="LevelPolarity"/>:
    /// </para>
    ///
    /// <list type="bullet">
    /// <item><description>
    /// No file may decide polarity by NAME — a <c>Contains</c> whose literal holds "resistance" or
    /// "support".
    /// </description></item>
    /// <item><description>
    /// No file may decide it by FREQUENCY — a frequency compared against a numeric literal.
    /// </description></item>
    /// <item><description>
    /// Any file that says <i>"…resistance at &lt;price&gt;"</i> or <i>"…support at &lt;price&gt;"</i>
    /// must reference <see cref="LevelPolarity"/>. This is the path check: banning the two known
    /// proxies alone would leave a third one free, so an announcer has to be shown asking the
    /// chokepoint, not merely shown not asking the wrong question.
    /// </description></item>
    /// </list>
    ///
    /// <para>
    /// ── Scope, and why there is no exemption list ───────────────────────────────
    /// Indicator providers are out of scope, and that is a statement about what they do rather than
    /// a hole. <c>CipherSRProvider</c>'s "Resistance pivot at {price}" and
    /// <c>ValueDeviationProvider</c>'s "Resistance zone at …, well above value" describe a
    /// component the provider itself CONSTRUCTED from a pivot high or from a tier above the value
    /// area. They know the polarity because they built it; they are not classifying a level handed
    /// to them. The narration layer is the opposite case — it receives levels from thirty-odd
    /// providers and has to decide what to call each one.
    /// </para>
    ///
    /// <para>
    /// There is deliberately <b>no per-file allowlist</b>. Both historical bugs would have been
    /// allowlisted: each looked local and reasonable at its own call site, and that is exactly how
    /// the invariant escaped twice. If this test ever fails, route the decision through
    /// <see cref="LevelPolarity"/> — do not add an exemption.
    /// </para>
    /// </summary>
    public class LevelPolarityScanTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        /// <summary>The narration layer: everything that turns chart state into words.</summary>
        private const string NarrationDir = "AccessibleTrader.Core/Services/Accessibility";

        /// <summary>Projects swept for the "already calls the chokepoint" half of the scope.</summary>
        private static readonly string[] ScannedProjects =
        {
            "AccessibleTrader.Core",
            "AccessibleTrader.BlazorClient",
            "AccessibleTrader.BlazorClient.Components",
            "AccessibleTrader.WebHost",
            "Plugins",
        };

        /// <summary>Deciding polarity from the component's NAME.</summary>
        private static readonly Regex ByName = new(
            @"Contains\s*\(\s*""[^""]*(resistance|support)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Deciding polarity from the tone it is voiced at, in either operand order.</summary>
        private static readonly Regex ByFrequency = new(
            @"[Ff]requency\s*(>=|<=|>|<|==|!=)\s*-?\d"
            + @"|-?\d+(\.\d+)?[fFdDmM]?\s*(>=|<=|>|<)\s*[A-Za-z_.()]*[Ff]requency",
            RegexOptions.Compiled);

        /// <summary>A spoken claim that a specific PRICE is a support or a resistance.</summary>
        private static readonly Regex SpeaksALevel = new(
            @"""[^""]*(resistance|support)\s+at\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string ChokepointName = nameof(LevelPolarity);

        private static IEnumerable<string> FilesInScope(string root)
        {
            var narration = Path.Combine(root, NarrationDir.Replace('/', Path.DirectorySeparatorChar));
            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (Directory.Exists(narration))
                foreach (var f in Directory.EnumerateFiles(narration, "*.cs", SearchOption.AllDirectories))
                    seen.Add(f);

            // Anything that already calls the chokepoint opts itself in, so a decision cannot be
            // moved out of the narration folder to escape the rules.
            foreach (var proj in ScannedProjects)
            {
                var dir = Path.Combine(root, proj);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     || f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                    if (Path.GetFileName(f) == ChokepointName + ".cs") continue;
                    if (File.ReadAllText(f).Contains(ChokepointName + ".", StringComparison.Ordinal))
                        seen.Add(f);
                }
            }
            return seen.OrderBy(x => x, StringComparer.Ordinal);
        }

        /// <summary>
        /// Strips a trailing line comment so the historical bugs quoted in the comments of the very
        /// files they were fixed in do not trip their own guard. Crude — a <c>//</c> inside a string
        /// literal truncates the line — which errs toward missing a violation rather than inventing
        /// one, and no call site in scope puts a double slash inside a literal.
        /// </summary>
        private static string CodeOnly(string line)
        {
            int i = line.IndexOf("//", StringComparison.Ordinal);
            return i < 0 ? line : line[..i];
        }

        [Fact]
        public void NoNarrationPathDecidesPolarityByNameOrByFrequency()
        {
            string root = RepoRoot();
            var offenders = new List<string>();

            foreach (var file in FilesInScope(root))
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string code = CodeOnly(lines[i]);
                    string rel = Path.GetRelativePath(root, file);
                    if (ByName.IsMatch(code))
                        offenders.Add($"{rel}:{i + 1} decides by NAME — {lines[i].Trim()}");
                    if (ByFrequency.IsMatch(code))
                        offenders.Add($"{rel}:{i + 1} decides by FREQUENCY — {lines[i].Trim()}");
                }
            }

            Assert.True(offenders.Count == 0,
                "Support versus resistance is being decided by a proxy again. A component's name is "
                + "the provider's label and its frequency is a sonification setting; neither is a "
                + "fact about the market. Call LevelPolarity.IsResistance(level, referencePrice) "
                + "instead — and read its remarks about WHICH price to pass, because a break has to "
                + "be judged against the close before the break.\n\n"
                + string.Join("\n", offenders));
        }

        [Fact]
        public void EverySpokenLevelClaimGoesThroughTheChokepoint()
        {
            string root = RepoRoot();
            var missing = new List<string>();
            var announcers = new List<string>();

            foreach (var file in FilesInScope(root))
            {
                string text = File.ReadAllText(file);
                if (!SpeaksALevel.IsMatch(text)) continue;

                string rel = Path.GetRelativePath(root, file);
                announcers.Add(rel);
                if (!text.Contains(ChokepointName + ".", StringComparison.Ordinal))
                    missing.Add(rel);
            }

            // Vacuity check. A rename or a folder move could empty the sweep, and an empty sweep
            // passes silently — the failure mode this repo has already been bitten by twice.
            Assert.Contains(announcers, a => a.EndsWith("AutoNarrationService.cs", StringComparison.Ordinal));
            Assert.Contains(announcers, a => a.EndsWith("NavigationFeedbackManager.cs", StringComparison.Ordinal));

            Assert.True(missing.Count == 0,
                "These files name a price and call it support or resistance without asking "
                + "LevelPolarity what it is:\n" + string.Join("\n", missing));
        }
    }
}
