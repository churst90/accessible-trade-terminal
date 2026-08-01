using System;
using System.IO;
using System.Linq;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The terminal ships TOOLS, not opinions.
    ///
    /// <para>
    /// Until 2026-08-01 the app chose a strategy for the user: a per-asset "recommended" preset
    /// surfaced as a highlighted library row, a starred dropdown entry, and two separate
    /// "Use Recommended" buttons. Every branch of that chooser returned a Cipher-B variant — the
    /// component this project's own research falsified (eight versions of pure-Cipher confluence
    /// walked forward to break-even; structure labels tested indistinguishable from random).
    /// </para>
    ///
    /// <para>
    /// Shipping an automatic recommendation built on a tested-null component is worse than shipping
    /// no recommendation at all, because it carries the app's authority. These tests exist so the
    /// behaviour cannot come back by accident — a reintroduced auto-pick fails here rather than
    /// silently reaching users.
    /// </para>
    /// </summary>
    public class StrategyLibraryPolicyTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static string[] AppSources()
        {
            string root = RepoRoot();
            return new[] { "AccessibleTrader.Core", "AccessibleTrader.BlazorClient.Components", "AccessibleTrader.Maui" }
                .Select(d => Path.Combine(root, d))
                .Where(Directory.Exists)
                .SelectMany(d => Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories))
                .Where(f => f.EndsWith(".cs") || f.EndsWith(".razor"))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .ToArray();
        }

        /// <summary>Strips comments so a note ABOUT the removal does not read as a reintroduction.</summary>
        private static string CodeOnly(string path)
        {
            var lines = File.ReadAllLines(path)
                .Where(l => !l.TrimStart().StartsWith("//") && !l.TrimStart().StartsWith("@*")
                         && !l.TrimStart().StartsWith("///") && !l.TrimStart().StartsWith("*"));
            return string.Join("\n", lines);
        }

        [Fact]
        public void NoPerAssetStrategyRecommenderExistsInAppCode()
        {
            var offenders = AppSources()
                .Where(f =>
                {
                    string code = CodeOnly(f);
                    return code.Contains("GetV23LongPresetFor") || code.Contains("GetV23ShortPresetFor")
                        || code.Contains("GetRecommendedV23") || code.Contains("RecommendV23");
                })
                .Select(f => Path.GetFileName(f))
                .ToList();

            Assert.True(offenders.Count == 0,
                "A per-asset strategy recommender is back in shipping code. The terminal must not " +
                "choose a strategy for the user:\n  " + string.Join("\n  ", offenders));
        }

        [Fact]
        public void NoUseRecommendedControlIsRendered()
        {
            var offenders = AppSources()
                .Where(f => f.EndsWith(".razor"))
                .Where(f => CodeOnly(f).Contains("Use Recommended") || CodeOnly(f).Contains("Use recommended"))
                .Select(Path.GetFileName)
                .ToList();

            Assert.True(offenders.Count == 0,
                "A 'Use Recommended' control is rendered again:\n  " + string.Join("\n  ", offenders));
        }

        [Fact]
        public void AssetClassifierStillProfilesAssets()
        {
            // The profiling half was deliberately KEPT. Classifying an asset by volatility, cycle,
            // regime and liquidity is a neutral measurement and the basis of the research lab's
            // character classifier — it is the mapping from profile to a specific strategy that was
            // an opinion. This test guards against over-correcting and deleting the useful half.
            var t = typeof(AccessibleTrader.Core.Services.Strategies.AssetClassifier);
            Assert.NotNull(t.GetMethod("Classify"));
            Assert.Null(t.GetMethod("RecommendV23Long"));
            Assert.Null(t.GetMethod("RecommendV23Short"));
        }
    }
}
