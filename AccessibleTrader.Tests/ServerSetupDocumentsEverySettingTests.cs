using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Code review, 2026-11-20: the hosted server read settings (<c>Launch:Disabled</c>,
    /// <c>Accounts:SupportEmail</c>, the security-event and plugin-trust variables) that
    /// <c>appsettings.json</c> does not carry and the deploy guide's table did not list, so a key
    /// renamed in code would quietly stop doing anything on the server. Every setting the WebHost
    /// reads must have a row in <c>docs/SERVER_SETUP.md</c>'s environment table.
    /// </summary>
    public class ServerSetupDocumentsEverySettingTests
    {
        // Operating-system variables the host passes through to the desktop session; not settings.
        private static readonly HashSet<string> NotSettings = new(StringComparer.Ordinal)
        {
            "DBUS_SESSION_BUS_ADDRESS", "XDG_RUNTIME_DIR",
        };

        private static readonly Regex SectionKey = new(@"""([A-Z][A-Za-z0-9]*(?::[A-Z][A-Za-z0-9]*)+)""");
        private static readonly Regex BareKey = new(@"Configuration\[""([A-Za-z][A-Za-z0-9]*)""\]");
        private static readonly Regex EnvVar = new(@"GetEnvironmentVariable\(""([A-Z][A-Z0-9_]*)""");

        internal static SortedSet<string> SettingsReadBy(string webHostDir)
        {
            var found = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(webHostDir, "*.cs", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(webHostDir, file).Replace('\\', '/');
                if (rel.StartsWith("obj/") || rel.StartsWith("bin/")) continue;
                string text = File.ReadAllText(file);
                foreach (Match m in SectionKey.Matches(text))
                    if (!m.Groups[1].Value.StartsWith("Sqlite:")) found.Add(m.Groups[1].Value.Replace(":", "__"));
                foreach (Match m in BareKey.Matches(text)) found.Add(m.Groups[1].Value);
                foreach (Match m in EnvVar.Matches(text))
                    if (!NotSettings.Contains(m.Groups[1].Value)) found.Add(m.Groups[1].Value);
            }
            return found;
        }

        internal static string EnvironmentTable(string serverSetup)
        {
            int start = serverSetup.IndexOf("## Environment variables", StringComparison.Ordinal);
            Assert.True(start >= 0, "SERVER_SETUP.md has no '## Environment variables' section");
            int end = serverSetup.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
            return end < 0 ? serverSetup[start..] : serverSetup[start..end];
        }

        [Fact]
        public void Every_setting_the_server_reads_has_a_row_in_the_deploy_guide()
        {
            string root = RepoPaths.RepoRoot();
            string webHost = Path.Combine(root, "AccessibleTrader.WebHost");
            Assert.True(File.Exists(Path.Combine(webHost, "Program.cs")), "scanned the wrong folder: " + webHost);
            var read = SettingsReadBy(webHost);

            // Vacuity: the scan must see the settings known to be read, or it proves nothing.
            Assert.Contains("Leaderboard__OutputPath", read);
            Assert.Contains("AccountEmail__Password", read);
            Assert.Contains("PathBase", read);
            Assert.Contains("ACCOUNTS_SEED_EMAIL", read);
            Assert.True(read.Count >= 20, $"only {read.Count} settings found: {string.Join(", ", read)}");

            string table = EnvironmentTable(File.ReadAllText(Path.Combine(root, "docs", "SERVER_SETUP.md")));
            var missing = read.Where(k => !table.Contains("`" + k + "`", StringComparison.Ordinal)).ToList();
            Assert.True(missing.Count == 0,
                "read by the server but not in SERVER_SETUP.md's environment table: " + string.Join(", ", missing));
        }
    }
}
