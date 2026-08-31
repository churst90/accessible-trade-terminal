namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Pure path arithmetic over the repository layout. Deliberately separate from
    /// <see cref="ProviderRoster"/>: the roster constructs real providers, which touches the
    /// process-global <c>PluginHostServices</c> bridge and therefore obliges the calling test
    /// class to enroll in <c>[Collection("ProviderCredentialBridge")]</c>. Several source-scan
    /// tests only ever needed the repo root, and keeping that on the roster made
    /// "references ProviderRoster ⇒ must enroll" — the rule
    /// <c>ProviderCredentialBridgeEnrollmentTests</c> enforces — false. Nothing here touches
    /// any global; callers need no collection.
    /// </summary>
    public static class RepoPaths
    {
        /// <summary>Walks up from the test binaries to the directory holding the solution file.</summary>
        public static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            if (dir == null)
                throw new InvalidOperationException("Could not locate AccessibleTrader.slnx above " + AppContext.BaseDirectory);
            return dir.FullName;
        }

        /// <summary>The plugin project directories on disk that are expected to ship a provider —
        /// <c>Plugins/Providers</c> and <c>Plugins/Analytics</c>. Indicators and Strategies are
        /// deliberately excluded: they ship no provider.</summary>
        public static IEnumerable<string> ProviderPluginProjectsOnDisk()
        {
            foreach (var group in new[] { "Providers", "Analytics" })
                foreach (var d in PluginProjectsIn(group))
                    yield return d;
        }

        /// <summary>
        /// Just <c>Plugins/Providers</c> — the market-data/trading venues, without the analytics
        /// feeds. Some contracts are specific to a venue: a venue is expected to be able to list
        /// what it trades, where an analytics feed's "symbols" are the series names it computes
        /// and are legitimately a fixed set.
        /// </summary>
        public static IEnumerable<string> MarketDataPluginProjectsOnDisk() => PluginProjectsIn("Providers");

        private static IEnumerable<string> PluginProjectsIn(string group)
        {
            var root = Path.Combine(RepoRoot(), "Plugins", group);
            if (!Directory.Exists(root)) yield break;
            foreach (var d in Directory.GetDirectories(root))
                yield return Path.GetFileName(d);
        }
    }
}
