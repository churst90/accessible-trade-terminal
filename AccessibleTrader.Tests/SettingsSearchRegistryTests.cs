using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The Settings search box answers from a hand-written registry, and a registry is a list
    /// that drifts. Before 2026-09-03 it named about half the settings — "speak the time",
    /// "sound theme", "magnet snap" all returned "0 matching settings", a confident false
    /// negative from the one control that exists so nobody has to remember which tab holds a
    /// setting — and it still listed a WASAPI field after the field was gone. Both are joins
    /// between two lists in one file, so this reads the file.
    /// </summary>
    public class SettingsSearchRegistryTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static string SettingsModal() => File.ReadAllText(Path.Combine(
            RepoRoot(), "AccessibleTrader.BlazorClient.Components", "SettingsModal.razor"));

        private static readonly Regex RegistryRow =
            new(@"new\(""(?<label>[^""]+)"",\s*""(?<tab>[^""]+)"",\s*""(?<id>[^""]+)""", RegexOptions.Compiled);

        /// <summary>
        /// Labels that are not settings, or are per-row and cannot be named once. The search
        /// box itself; the named-webhook rows, whose ids carry a Razor index.
        /// </summary>
        private static readonly HashSet<string> NotASetting = new(StringComparer.Ordinal) { "s-search" };

        [Fact]
        public void EveryLabelledSettingHasARegistryRow()
        {
            string text = SettingsModal();
            var registryIds = RegistryRow.Matches(text).Select(m => m.Groups["id"].Value).ToHashSet(StringComparer.Ordinal);

            var missing = Regex.Matches(text, @"<label for=""(s-[a-z0-9\-]+)""")
                .Select(m => m.Groups[1].Value)
                .Where(id => !NotASetting.Contains(id) && !registryIds.Contains(id))
                .Distinct()
                .ToList();

            Assert.True(missing.Count == 0,
                "Settings with a label but no search-registry row (search says \"0 matching\" for them):\n  "
                + string.Join("\n  ", missing));
        }

        [Fact]
        public void EveryRegistryRowPointsAtAControlThatRenders()
        {
            string text = SettingsModal();
            var ids = Regex.Matches(text, @"\bid=""(s-[a-z0-9\-]+)""").Select(m => m.Groups[1].Value)
                           .Concat(Regex.Matches(text, @"ElementId=""(s-[a-z0-9\-]+)""").Select(m => m.Groups[1].Value))
                           .Concat(Regex.Matches(text, @"\bid=""(tab-[a-z]+)""").Select(m => m.Groups[1].Value))
                           .Concat(Regex.Matches(text, @"TabElementId\(tab\)").Select(_ => "tab-*"))
                           .ToHashSet(StringComparer.Ordinal);

            var dangling = RegistryRow.Matches(text)
                .Select(m => m.Groups["id"].Value)
                .Where(id => !ids.Contains(id) && !(id.StartsWith("tab-", StringComparison.Ordinal) && ids.Contains("tab-*")))
                .ToList();

            Assert.True(dangling.Count == 0,
                "Search-registry rows whose control id renders nowhere (the jump lands on nothing):\n  "
                + string.Join("\n  ", dangling));
        }

        [Fact]
        public void EveryRegistryRowNamesATabThatExists()
        {
            // GoToSetting sets _activeTab to the row's Tab string. A tab name that is not in
            // SettingsTabs hides every panel at once.
            string text = SettingsModal();
            var tabs = Regex.Match(text, @"SettingsTabs\s*=\s*\{([^}]*)\}").Groups[1].Value;
            var known = Regex.Matches(tabs, @"""([A-Za-z]+)""").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            Assert.True(known.Count >= 7, "SettingsTabs was not found — the scan has lost its anchor.");

            var unknown = RegistryRow.Matches(text)
                .Select(m => m.Groups["tab"].Value)
                .Where(t => !known.Contains(t))
                .Distinct()
                .ToList();

            Assert.True(unknown.Count == 0,
                "Search-registry rows naming a tab that is not in SettingsTabs:\n  " + string.Join("\n  ", unknown));
        }
    }
}
