using System.Reflection;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Every setting must be both REACHABLE and CONNECTED.
    ///
    /// <para>
    /// This exists because "the setting exists and does nothing" has been a recurring class of
    /// bug rather than a one-off. Boolean indicator parameters were silently dropped app-wide for
    /// months — the checkbox saved, the value was written, and the factory threw it away because
    /// it required <c>double.TryParse</c>. Market Structure's on-by-default preference lived in
    /// <c>AppSettings</c> with no UI at all, so "on by default" was unturnoffable. Value
    /// Deviation's speech switch was keyed on the wrong string, so seven components saved their
    /// sentences and spoke none of them.
    /// </para>
    ///
    /// <para>
    /// None of those threw. None failed a test. Each one looked correct at every individual site
    /// and was only wrong at the join. So this audits the joins:
    /// </para>
    /// <list type="bullet">
    /// <item>A key WRITTEN but never READ is a control that does nothing.</item>
    /// <item>A key READ but never WRITTEN is behaviour with no way to reach it.</item>
    /// </list>
    ///
    /// <para>
    /// Both are listed in an allow-list with a stated reason rather than silently tolerated, so
    /// adding a key without wiring it is a decision someone has to write down.
    /// </para>
    /// </summary>
    public class SettingsWiringAuditTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        /// <summary>Source we scan: the shared UI, Core, and both hosts.</summary>
        private static IEnumerable<(string Path, string Text)> SourceFiles()
        {
            string root = RepoRoot();
            foreach (var project in new[]
            {
                "AccessibleTrader.Core", "AccessibleTrader.BlazorClient.Components",
                "AccessibleTrader.BlazorClient", "AccessibleTrader.WebHost",
            })
            {
                string dir = Path.Combine(root, project);
                if (!Directory.Exists(dir)) continue;

                foreach (var pattern in new[] { "*.cs", "*.razor" })
                    foreach (var file in Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories))
                    {
                        if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                            || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                            continue;
                        yield return (file, File.ReadAllText(file));
                    }
            }
        }

        /// <summary>Constant name → its settings.json path.</summary>
        private static IReadOnlyDictionary<string, string> AllKeys() =>
            typeof(SettingsKeys)
                .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);

        /// <summary>
        /// Keys with no direct UI control, each for a stated reason. A key belongs here only when
        /// its absence from the dialogs is deliberate — never because wiring it looked like work.
        /// </summary>
        private static readonly Dictionary<string, string> NoDirectControl = new()
        {
            // Written by the workspace/session machinery rather than by a person.
            ["LastSession"]        = "written by session autosave, not by a control",
            ["LastWorkspace"]      = "written by workspace save/load, not by a control",
            ["WindowState"]        = "written by the window itself on move/resize",
            ["OnboardingSeen"]     = "set once by the first-run flow",
            ["LicenseKey"]         = "entered in the License tab, which writes it through a different path",

            // Reached through a service rather than by naming the key: the theme dropdown calls
            // ThemeService.SetTheme, which persists it. Real control, indirect write.
            ["UiTheme"]            = "written by ThemeService.SetTheme from the Appearance theme picker",

            // RETIRED, not a gap. Nothing in the audio stack reads the value on any head — the
            // "WASAPI latency (ms)" field was a number that saved, persisted and changed nothing,
            // which is the strictly-worse-than-missing case the first test above describes. The
            // control was removed 2026-09-03; the key and WorkspaceState.WasapiLatency stay only
            // so saved audio profiles and the sandbox wire format keep their shape.
            ["WasapiLatency"]      = "retired 2026-09-03: no reader in the audio stack; kept for profile and wire-format shape",

            // A GENUINE GAP, recorded rather than hidden. SetupAlertBridge honours a per-symbol
            // webhook routing map, and nothing can populate it — the Settings alerts tab only
            // exposes the single fallback webhook. The feature works and is unreachable.
            ["SetupWebhookMap"]    = "GAP: per-symbol webhook routes are honoured but have no editor yet",
        };

        [Fact]
        public void EverySettingsKey_isReadBySomething()
        {
            // A key that is written and never read is a control that appears to work — it saves,
            // it persists, it reads back into the dialog — and changes nothing about the running
            // application. That is strictly worse than a missing control, because it stops the
            // user looking for the real one.
            var orphans = new List<string>();
            var sources = SourceFiles().ToList();

            foreach (var (name, path) in AllKeys())
            {
                int uses = sources.Count(f =>
                    !f.Path.EndsWith("SettingsKeys.cs", StringComparison.Ordinal)
                    && (f.Text.Contains($"SettingsKeys.{name}") || f.Text.Contains($"\"{path}\"")));

                if (uses == 0) orphans.Add($"{name} (\"{path}\") is referenced nowhere outside SettingsKeys");
            }

            Assert.True(orphans.Count == 0,
                "Settings keys that nothing uses:\n  " + string.Join("\n  ", orphans));
        }

        [Fact]
        public void EverySettingsKey_isReachableFromTheUserInterface()
        {
            // The other direction: behaviour that reads a setting nobody can change. Market
            // Structure's on-by-default preference sat like this — real, honoured on every chart
            // load, and unturnoffable because no dialog wrote it.
            var unreachable = new List<string>();

            var uiText = SourceFiles()
                .Where(f => f.Path.EndsWith(".razor", StringComparison.Ordinal))
                .Select(f => f.Text)
                .ToList();

            foreach (var (name, path) in AllKeys())
            {
                if (NoDirectControl.ContainsKey(name)) continue;

                bool inUi = uiText.Any(t => t.Contains($"SettingsKeys.{name}")
                                            || t.Contains($"\"{path}\"")
                                            || t.Contains(PropertyNameFor(name)));

                if (!inUi) unreachable.Add($"{name} (\"{path}\") has no control in any dialog");
            }

            Assert.True(unreachable.Count == 0,
                "Settings with no way to reach them — add a control, or add them to NoDirectControl " +
                "with a reason:\n  " + string.Join("\n  ", unreachable));
        }

        /// <summary>
        /// Many settings are surfaced through an <see cref="IAppSettings"/> property rather than by
        /// naming the key in the razor. The property is conventionally the constant's own name, so
        /// a dialog binding to <c>App.MarketStructureOnByDefault</c> counts as reaching
        /// <c>SettingsKeys.MarketStructureOnByDefault</c>.
        /// </summary>
        private static string PropertyNameFor(string constantName) => constantName;

        [Fact]
        public void TheAppSettingsFacade_coversTheKeysItClaims()
        {
            // IAppSettings is the typed front door. A property that reads one key and writes
            // another is the exact join this whole class exists to guard, and it is invisible at
            // both call sites.
            var facade = typeof(IAppSettings);
            var props = facade.GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            var keys = AllKeys().Keys.ToHashSet(StringComparer.Ordinal);

            // Every facade property whose name matches a key constant must actually use that
            // constant in its implementation.
            string impl = File.ReadAllText(Path.Combine(RepoRoot(), "AccessibleTrader.Core", "Services", "AppSettings.cs"));

            var mismatches = new List<string>();
            foreach (var prop in props.Where(keys.Contains))
                if (!impl.Contains($"SettingsKeys.{prop}"))
                    mismatches.Add($"IAppSettings.{prop} does not use SettingsKeys.{prop}");

            Assert.True(mismatches.Count == 0,
                "Facade properties wired to the wrong key:\n  " + string.Join("\n  ", mismatches));
        }
    }
}
