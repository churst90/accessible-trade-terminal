using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// One rule, scanned across every shipping project: **a service does not build its own
    /// per-user path.** It asks <c>IPlatformPathService</c>, and the two or three files that
    /// implement that interface are the only places <see cref="Environment.GetFolderPath"/> may
    /// appear.
    ///
    /// <para>
    /// This exists because the same defect has now shipped three times.
    /// <c>WorkspacePerUserIsolationTests</c> and <c>IndicatorPrefsPerUserIsolationTests</c> both
    /// exist, both are good, and both docstrings describe the identical bug — a service composing
    /// <c>Path.Combine(Environment.GetFolderPath(LocalApplicationData), "AccessibleTrader", …)</c>
    /// instead of taking the path service. Each was fixed at the site where it was reported. Only
    /// a scan catches the fourth one, and the scan is what turned up the third.
    /// </para>
    ///
    /// <para>
    /// Two things make the hand-rolled version wrong rather than merely inelegant. On Unix
    /// <c>GetFolderPath</c> returns an <b>empty string</b> when the directory it resolves to does
    /// not exist yet, so <c>Path.Combine</c> yields a RELATIVE path that resolves against the
    /// process's current directory — on the hosted server that is the deployment directory, which
    /// a redeploy replaces. And in the hosted, multi-user head there is no such thing as "the"
    /// local app data directory: <c>UserScopedPathService</c> exists precisely because two signed-in
    /// users must not share one.
    /// </para>
    /// </summary>
    public class PerUserPathPolicyTests
    {
        /// <summary>
        /// The files that ARE the path layer, plus the one caller that legitimately cannot use it.
        /// Every entry needs a reason; "it was already like that" is not one — that is what
        /// <see cref="KnownOffenders"/> is for.
        /// </summary>
        private static readonly Dictionary<string, string> Sanctioned = new()
        {
            ["PlatformPaths.cs"] =
                "Core's local-app-data resolver. This is the file that adds the absolute-path "
                + "guarantee GetFolderPath does not give you, so it is the one place that has to call it.",
            ["WebHostPathService.cs"] =
                "The WebHost's IPlatformPathService for the single-user desktop host.",
            ["UserScopedPathService.cs"] =
                "The WebHost's IPlatformPathService for the hosted multi-user head, which is the "
                + "whole reason this rule exists.",
            ["App.xaml.cs"] =
                "Windows last-chance crash handler. It runs when the app is already failing, "
                + "possibly before or because of DI, so it cannot resolve a service — and on "
                + "Windows GetFolderPath always returns a valid absolute path, so the Unix "
                + "empty-string failure mode does not apply.",
        };

        /// <summary>
        /// Real instances of the defect that are tracked but not yet fixed. **This list may only
        /// ever shrink.** Adding a file here is not a fix, and the test below fails if an entry
        /// stops being an offender, so a fix cannot leave a stale exemption behind.
        ///
        /// <para>
        /// Empty since 2026-08-22, when the last entry — <c>SchwabOAuthService.cs</c>, the third
        /// instance, found by this scan — was fixed: the refresh token now persists only through
        /// <c>PluginHostServices.SecureStorage</c>, and the legacy DPAPI file is located via the
        /// Windows-only <c>%APPDATA%</c> environment variable purely to migrate and delete it.
        /// </para>
        /// </summary>
        private static readonly Dictionary<string, string> KnownOffenders = new();

        private static string[] Sources() =>
            StrategyLibraryPolicyTests.ShippingProjectDirectories()
                .SelectMany(d => Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories))
                .Where(f => f.EndsWith(".cs") || f.EndsWith(".razor"))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .ToArray();

        /// <summary>
        /// Every file that calls <see cref="Environment.GetFolderPath"/> outside a comment, by
        /// file name. Comments are stripped first — <c>IndicatorPreferencesService</c> and
        /// <c>WorkspaceLibraryService</c> both *describe* this bug in their docstrings, having
        /// each been the victim of it once, and a scan that flagged them would be flagging the
        /// documentation of the fix.
        /// </summary>
        private static (Dictionary<string, string> hits, int scanned) Scan()
        {
            var hits = new Dictionary<string, string>();
            int scanned = 0;
            foreach (var file in Sources())
            {
                scanned++;
                var code = PipelineIdentityAndResilienceTests.StripCommentsAndStrings(File.ReadAllText(file));
                if (!code.Contains("Environment.GetFolderPath")) continue;
                hits[Path.GetFileName(file)] = file;
            }
            return (hits, scanned);
        }

        [Fact]
        public void NoServiceBuildsItsOwnLocalAppDataPath()
        {
            var (hits, scanned) = Scan();

            Assert.True(scanned >= 300,
                $"the scan only saw {scanned} shipping sources; it is not covering the tree. "
                + "Fix the discovery, do not lower the floor.");

            var offenders = hits.Keys
                .Where(f => !Sanctioned.ContainsKey(f) && !KnownOffenders.ContainsKey(f))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            Assert.True(offenders.Count == 0,
                "These files build a local-app-data path themselves instead of taking "
                + "IPlatformPathService. On Unix GetFolderPath returns \"\" when the directory does "
                + "not exist yet, which turns Path.Combine into a RELATIVE path, and in the hosted "
                + "head there is no single per-user directory to resolve:\n  "
                + string.Join("\n  ", offenders.Select(f => $"{f}  ({hits[f]})")));
        }

        /// <summary>
        /// The exemption lists have to stay honest in both directions. An entry that is no longer
        /// an offender means the bug was fixed and the exemption outlived it — which is how a
        /// baseline quietly becomes permission. Removing the file from the list is part of the fix.
        /// </summary>
        [Fact]
        public void EveryExemptionIsStillLoadBearing()
        {
            var (hits, _) = Scan();

            var stale = Sanctioned.Keys.Concat(KnownOffenders.Keys)
                .Where(f => !hits.ContainsKey(f))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            Assert.True(stale.Count == 0,
                "These files are exempted from the per-user-path rule but no longer call "
                + "Environment.GetFolderPath. If that is because they were fixed, delete the "
                + "exemption — the list must only ever shrink:\n  " + string.Join("\n  ", stale));
        }

        /// <summary>
        /// The tracked-but-unfixed list is not a place to park new work. If it grows, the rule has
        /// stopped being a rule.
        /// </summary>
        [Fact]
        public void TheKnownOffenderListHasNotGrown()
        {
            Assert.True(KnownOffenders.Count == 0,
                "A file was added to KnownOffenders. That is not a fix — it is a record that the "
                + "defect shipped again. Fix it, or raise this number deliberately and say why in "
                + "docs/TODO.md. The list reached zero on 2026-08-22 and must stay there. "
                + "Current list: " + string.Join(", ", KnownOffenders.Keys));
        }
    }
}
