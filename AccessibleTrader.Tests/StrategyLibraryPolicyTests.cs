using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
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
        private static string RepoRoot() => ProviderRoster.RepoRoot();

        /// <summary>
        /// Projects that are NOT shipping code. <c>StrategyLab</c> is the research lab and is
        /// where the catalogue is supposed to live; the test project and the calibrator tool
        /// never reach a user.
        /// </summary>
        private static readonly string[] NotShippingCode =
        {
            "AccessibleTrader.StrategyLab",
            "AccessibleTrader.Tests",
            "DotPadCalibrator",
        };

        /// <summary>
        /// Every shipping project, read from the solution file.
        ///
        /// <para>
        /// This used to be a hand-written list of three directory names, one of which
        /// (<c>AccessibleTrader.Maui</c>) had not existed for a long time and was silently
        /// dropped by a <c>.Where(Directory.Exists)</c>. It scanned two of the shipping projects.
        /// Missing were <c>AccessibleTrader.BlazorClient</c> and — the one that matters —
        /// <c>AccessibleTrader.WebHost</c>, whose <c>Program.cs</c> is the composition root and
        /// the single most natural home for exactly the first-launch seeder these guards exist
        /// to prevent. Reading the solution means adding a project cannot leave a hole.
        /// </para>
        /// </summary>
        internal static string[] ShippingProjectDirectories()
        {
            string root = RepoRoot();
            string slnx = File.ReadAllText(Path.Combine(root, "AccessibleTrader.slnx"));

            var dirs = System.Text.RegularExpressions.Regex
                .Matches(slnx, @"Project\s+Path=""([^""]+)""")
                .Select(m => m.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar))
                .Select(rel => Path.GetDirectoryName(Path.Combine(root, rel))!)
                .Where(dir => !NotShippingCode.Contains(Path.GetFileName(dir)))
                .Distinct()
                .OrderBy(d => d, StringComparer.Ordinal)
                .ToArray();

            // Assert existence rather than filtering on it — a filter is how the stale Maui
            // entry above went unnoticed for however long it was wrong.
            foreach (var dir in dirs)
                Assert.True(Directory.Exists(dir), $"AccessibleTrader.slnx lists a project whose directory is missing: {dir}");

            return dirs;
        }

        private static string[] AppSources() =>
            ShippingProjectDirectories()
                .SelectMany(d => Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories))
                .Where(f => f.EndsWith(".cs") || f.EndsWith(".razor"))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .ToArray();

        /// <summary>
        /// The four guards below all iterate <see cref="AppSources"/>; a scan that covers less
        /// than it says it does passes for the wrong reason, which is precisely how the WebHost
        /// stayed outside these rules.
        /// </summary>
        [Fact]
        public void TheScanCoversEveryShippingProject()
        {
            var names = ShippingProjectDirectories().Select(Path.GetFileName).ToList();

            foreach (var required in new[]
            {
                "AccessibleTrader.Core",
                "AccessibleTrader.BlazorClient",
                "AccessibleTrader.BlazorClient.Components",
                "AccessibleTrader.WebHost",
            })
                Assert.Contains(required, names);

            Assert.DoesNotContain("AccessibleTrader.StrategyLab", names);   // the catalogue's own home
            Assert.DoesNotContain("AccessibleTrader.Tests", names);

            // …and the file scan behind them is non-empty in each of those projects.
            var files = AppSources();
            foreach (var required in new[]
            {
                "AccessibleTrader.Core",
                "AccessibleTrader.BlazorClient",
                "AccessibleTrader.BlazorClient.Components",
                "AccessibleTrader.WebHost",
            })
                Assert.Contains(files, f => f.Contains(required + Path.DirectorySeparatorChar));
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
        public void NoStrategyCatalogueShipsInsideTheApp()
        {
            // Second half of the split, added 2026-08-01: the specs themselves moved to the
            // research lab. Shipping code must not carry a catalogue of strategies at all —
            // not as a seeder, not as an "examples" list that a future refactor quietly
            // reintroduces into the library on first launch.
            var offenders = AppSources()
                .Where(f =>
                {
                    string code = CodeOnly(f);
                    return code.Contains("BuiltInStrategySeeds")
                        || code.Contains("EnsureSeeded")
                        || code.Contains("StrategyCatalogue.AllSpecs");
                })
                .Select(f => Path.GetFileName(f))
                .ToList();

            Assert.True(offenders.Count == 0,
                "A built-in strategy catalogue is back in shipping code. Specs live in " +
                "AccessibleTrader.StrategyLab/Catalogue and reach a library only through an " +
                "explicit import:\n  " + string.Join("\n  ", offenders));
        }

        [Fact]
        public void ImportedStrategiesCannotStartThemselves()
        {
            // The import path is the ONLY way a strategy the user did not write reaches the
            // library, which makes "importing a file never starts anything" a policy property
            // and not merely an implementation detail of StrategyBundleService.
            //
            // This used to be a substring match for "IsAutoActivate = false" over the WHOLE of
            // StrategyBundle.cs — comments included, and the file's own class remarks contain
            // that sentence. It passed with an `IsAutoActivate = true` on the live path as long
            // as the false string existed anywhere in the file. Now it imports a bundle that
            // asks to auto-start and looks at what the library got.
            var library = new RecordingLibrary();
            var hostile = new StrategySpec(
                Id: "policy.autostart",
                Name: "Wants to start itself",
                Description: "a bundle that claims auto-activate",
                Side: OrderSide.Buy,
                Conditions: new ConditionLeaf("leaf", "REGIME.AboveSma200", LeafOperator.GreaterThan, Value: 0),
                Risk: new RiskPlan(
                    new StopSource(StopSourceKind.AtrMultiple),
                    new List<TpLadderRung> { new(TargetSourceKind.RiskRewardMultiple, Multiple: 2.0, ClosePortion: 1.0) },
                    new PositionSizing(),
                    new EntryTrigger()),
                IsAutoActivate: true);

            var result = StrategyBundleService.Import(
                new StrategyBundle(StrategyBundle.CurrentFormatVersion, "test", null, DateTime.UtcNow,
                    new List<StrategySpec> { hostile }),
                library);

            Assert.Single(result.Imported);
            Assert.False(Assert.Single(library.All).IsAutoActivate,
                "An imported strategy came back auto-activating. Importing a file must never start "
              + "trading logic in an app the user has not touched since.");
        }

        /// <summary>Minimal in-memory library — the import path only needs lookup and upsert.</summary>
        private sealed class RecordingLibrary : IStrategyLibrary
        {
            private readonly List<StrategySpec> _specs = new();
            public IReadOnlyList<StrategySpec> All => _specs;
            public StrategySpec? GetById(string id) => _specs.FirstOrDefault(s => s.Id == id);
            public void Upsert(StrategySpec spec)
            {
                _specs.RemoveAll(s => s.Id == spec.Id);
                _specs.Add(spec);
            }
            public void Remove(string id) => _specs.RemoveAll(s => s.Id == id);
            public void Save() { }
            public void Reload() { }
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
