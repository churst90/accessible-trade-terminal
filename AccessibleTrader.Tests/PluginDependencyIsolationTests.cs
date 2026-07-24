using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Guards the plugin dependency-flattening hazard: plugins ship their NuGet
    /// dependencies into one shared output directory (no per-plugin .deps.json
    /// isolation yet), so if two plugins resolve the SAME third-party assembly at
    /// DIFFERENT versions, last-writer-wins and one plugin binds against the wrong
    /// version — the class of failure that bit Binance vs MEXC on CryptoExchange.Net.
    /// It's currently clean only because MEXC is the sole CryptoExchange.Net consumer;
    /// this test fails the moment that luck runs out.
    ///
    /// Framework-provided assemblies (Microsoft.*, System.*, runtime.*) are excluded
    /// — the shared framework unifies their patch versions at load time.
    /// </summary>
    public class PluginDependencyIsolationTests
    {
        private static bool IsFrameworkProvided(string name) =>
            name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("runtime.", StringComparison.OrdinalIgnoreCase)
            || name.Equals("NETStandard.Library", StringComparison.OrdinalIgnoreCase);

        private static string? FindPluginsRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Plugins")))
                    return Path.Combine(dir.FullName, "Plugins");
                dir = dir.Parent;
            }
            return null;
        }

        [Fact]
        public void No_two_plugins_resolve_the_same_third_party_assembly_at_different_versions()
        {
            var pluginsRoot = FindPluginsRoot();
            Assert.True(pluginsRoot != null, "Could not locate the Plugins directory from the test base directory.");

            var assetFiles = Directory.GetFiles(pluginsRoot!, "project.assets.json", SearchOption.AllDirectories);
            // Restore output must exist (CI builds before testing). If it doesn't the
            // guard can't do its job — fail loudly rather than pass vacuously.
            Assert.True(assetFiles.Length >= 5,
                $"Expected several plugin project.assets.json files; found {assetFiles.Length}. Run a restore/build first.");

            // package name -> version -> set of plugins declaring it
            var byPackage = new Dictionary<string, Dictionary<string, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in assetFiles)
            {
                var plugin = file.Replace('\\', '/').Split("/obj/")[0].Split('/').Last();
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (!doc.RootElement.TryGetProperty("libraries", out var libs)) continue;

                foreach (var lib in libs.EnumerateObject())
                {
                    if (!lib.Value.TryGetProperty("type", out var type) || type.GetString() != "package") continue;
                    int slash = lib.Name.LastIndexOf('/');
                    if (slash <= 0) continue;
                    var name = lib.Name[..slash];
                    var version = lib.Name[(slash + 1)..];
                    if (IsFrameworkProvided(name)) continue;

                    if (!byPackage.TryGetValue(name, out var versions))
                        byPackage[name] = versions = new(StringComparer.OrdinalIgnoreCase);
                    if (!versions.TryGetValue(version, out var plugins))
                        versions[version] = plugins = new();
                    plugins.Add(plugin);
                }
            }

            var clashes = byPackage
                .Where(kv => kv.Value.Count > 1)
                .Select(kv => $"{kv.Key}: " + string.Join("; ",
                    kv.Value.OrderBy(v => v.Key).Select(v => $"{v.Key} ({string.Join(", ", v.Value)})")))
                .ToList();

            Assert.True(clashes.Count == 0,
                "Plugins resolve the same third-party assembly at different versions — they will clash in the "
                + "flattened plugin output directory. Align the versions or give the plugins isolated dep folders:\n  "
                + string.Join("\n  ", clashes));
        }
    }
}
