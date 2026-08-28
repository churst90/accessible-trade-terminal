using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>The About dialog is a claim about the build, and nothing was checking it.</b>
    ///
    /// <para>
    /// Found while cutting 2.4.0: the "Providers" row in <c>SettingsModal.razor</c> listed fourteen
    /// trading venues and the tree has sixteen. **Gemini and Kraken Futures both shipped in 2.3.0
    /// and neither was ever added here**, so for a whole release the dialog a user opens to find
    /// out what they have was telling two of them they did not have it. The repository link in the
    /// same table pointed at a different GitHub org entirely.
    /// </para>
    ///
    /// <para>
    /// This is the same shape as the README plugin-count drift that <c>doc-drift.yml</c> already
    /// guards — "29 data providers" survived three releases in the README's most prominent
    /// section — except that this copy is *inside the application*, where a reader has no
    /// changelog next to it. A count in prose has nothing checking it unless something checks it.
    /// </para>
    ///
    /// <para>
    /// Analytics providers are deliberately not listed in the dialog and so are not checked here:
    /// the row answers "which venues can I trade on", which is a question about
    /// <c>Plugins/Providers/</c>. If an analytics row is ever added, extend this test with it
    /// rather than letting the new row be the unguarded one.
    /// </para>
    /// </summary>
    public class AboutDialogHonestyTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        /// <summary>
        /// Plugin directory name → the display name the dialog uses. Only entries whose display
        /// name is not simply the directory suffix need to appear; everything else is matched by
        /// its own name. Each mapping is a spelling decision, not an exemption — a venue that is
        /// missing from the dialog cannot be silenced by adding it here, because the assertion is
        /// on the dialog's text, not on this table.
        /// </summary>
        private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.Ordinal)
        {
            ["Fmp"] = "FMP",
            ["InteractiveBrokers"] = "Interactive Brokers",
            ["KrakenFutures"] = "Kraken Futures",
            ["Mexc"] = "MEXC",
            ["TwelveData"] = "Twelve Data",
        };

        private static string AboutVenuesRow(string root)
        {
            var razor = Path.Combine(root, "AccessibleTrader.BlazorClient.Components", "SettingsModal.razor");
            Assert.True(File.Exists(razor), $"SettingsModal.razor not found at {razor}");

            var m = Regex.Match(File.ReadAllText(razor),
                @"<tr><td>Trading venues</td><td>(?<list>[^<]*)</td></tr>");
            Assert.True(m.Success,
                "The About tab's trading-venue row was not found in SettingsModal.razor. If the row "
              + "was renamed or restructured, update this test — do not delete it: the row went two "
              + "venues stale for an entire release with nothing watching it.");
            return m.Groups["list"].Value;
        }

        [Fact]
        public void TheAboutDialogListsEveryTradingVenueThatShips()
        {
            string root = RepoRoot();
            string listed = AboutVenuesRow(root);

            var providersDir = Path.Combine(root, "Plugins", "Providers");
            var expected = Directory.EnumerateDirectories(providersDir)
                .Select(Path.GetFileName)
                .Where(n => n!.StartsWith("AccessibleTrader.Plugins.", StringComparison.Ordinal))
                .Select(n => n!["AccessibleTrader.Plugins.".Length..])
                .Select(n => DisplayNames.TryGetValue(n, out var d) ? d : n)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            // Vacuity check: a wrong path or a rename would leave nothing to compare against, and
            // an empty expectation passes every assertion below it.
            Assert.True(expected.Count >= 16,
                $"Only {expected.Count} trading plugin directories were found under {providersDir}. "
              + "That is fewer than shipped in 2.3.0, so the sweep is looking in the wrong place.");

            var missing = expected.Where(v => !listed.Contains(v, StringComparison.Ordinal)).ToList();
            Assert.True(missing.Count == 0,
                "The About dialog does not list these trading venues, which ship: "
              + string.Join(", ", missing)
              + ".\nAdd them to the 'Trading venues' row in SettingsModal.razor. This row is what a "
              + "user reads to find out which venues their build supports.");
        }

        [Fact]
        public void TheAboutDialogDoesNotListAVenueThatDoesNotShip()
        {
            // The other direction, and the one an "add the missing ones" fix leaves open: a venue
            // removed from the tree stays in the dialog forever, promising something the build
            // cannot do.
            string root = RepoRoot();
            string listed = AboutVenuesRow(root);

            var shipping = Directory.EnumerateDirectories(Path.Combine(root, "Plugins", "Providers"))
                .Select(Path.GetFileName)
                .Select(n => n!["AccessibleTrader.Plugins.".Length..])
                .Select(n => DisplayNames.TryGetValue(n, out var d) ? d : n)
                .ToHashSet(StringComparer.Ordinal);

            var phantom = listed.Split(',')
                .Select(v => v.Trim())
                .Where(v => v.Length > 0 && !shipping.Contains(v))
                .ToList();

            Assert.True(phantom.Count == 0,
                "The About dialog lists trading venues with no plugin behind them: "
              + string.Join(", ", phantom)
              + ".\nEither the plugin was removed and the row was not, or the display name here "
              + "disagrees with the directory name (see DisplayNames in this file).");
        }

        [Fact]
        public void TheAboutDialogPointsAtThisRepository()
        {
            // It pointed at github.com/accessible-trader/accessible-trader, which is not where this
            // code lives. A wrong link in About is a user who cannot file the bug they came to file.
            string root = RepoRoot();
            var razor = Path.Combine(root, "AccessibleTrader.BlazorClient.Components", "SettingsModal.razor");
            string text = File.ReadAllText(razor);

            Assert.Contains("https://github.com/churst90/accessible-trade-terminal", text, StringComparison.Ordinal);
        }
    }
}
