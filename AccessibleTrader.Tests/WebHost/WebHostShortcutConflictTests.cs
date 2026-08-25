using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests.WebHost
{
    /// <summary>
    /// Shortcut conflicts AFTER the WebHost's browser remap.
    ///
    /// <para>
    /// The Linux WebHost rewrites every <c>Ctrl+Shift+letter</c> chord to
    /// <c>Alt+Shift+letter</c> at startup, because browsers reserve the former at chrome level and
    /// a page cannot cancel them. That rewrite happens at runtime, on a profile the existing
    /// conflict test never sees — <c>ShortcutConflictTests</c> checks the DEFAULT profile only.
    /// </para>
    ///
    /// <para>
    /// So a binding can be unique everywhere the test suite looks and collide head-on for every
    /// WebHost user. That is not hypothetical: three shortcuts added this cycle —
    /// <c>Alt+Shift+L</c> (layout summary), <c>Alt+Shift+H</c> (show all) and <c>Alt+Shift+M</c>
    /// (unmute all) — landed exactly on the remapped Text Label, Horizontal Line and Measure Tool.
    /// The default profile was clean, the suite was green, and the WebHost would have had two
    /// commands on one chord in three places.
    /// </para>
    ///
    /// <para>
    /// The rule this encodes: <b>a new binding has to be checked against the profile the user
    /// actually runs, not the one that ships.</b>
    /// </para>
    /// </summary>
    public class WebHostShortcutConflictTests
    {
        /// <summary>Points at an empty temp dir so this machine's own shortcuts.json cannot
        /// leak in and mask a real clash.</summary>
        private sealed class EmptyPaths : IPlatformPathService
        {
            public EmptyPaths()
            {
                AppDataDirectory = Path.Combine(Path.GetTempPath(), "at-webhost-shortcut-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(AppDataDirectory);
                CacheDirectory = AppDataDirectory;
            }
            public string AppDataDirectory { get; }
            public string CacheDirectory { get; }
        }

        private static IShortcutManager RemappedProfile()
        {
            var shortcuts = new ShortcutManager(new EmptyPaths());
            AccessibleTrader.WebHost.Services.WebHostShortcutRemap
                .ApplyBrowserHostOverrides(shortcuts, NullLogger.Instance);
            return shortcuts;
        }

        private static string Chord(ShortcutDefinition b)
        {
            var parts = new List<string>();
            if (b.Ctrl) parts.Add("Ctrl");
            if (b.Alt) parts.Add("Alt");
            if (b.Shift) parts.Add("Shift");
            parts.Add(b.Key.ToUpperInvariant());
            return string.Join("+", parts);
        }

        [Fact]
        public void NoTwoCommandsShareAChordOnTheWebHost()
        {
            var profile = RemappedProfile().CurrentProfile;

            var clashes = profile.Shortcuts
                .GroupBy(Chord)
                .Where(g => g.Select(b => b.Command).Distinct().Count() > 1)
                .Select(g => $"{g.Key} → {string.Join(", ", g.Select(b => b.Command).Distinct())}")
                .ToList();

            Assert.True(clashes.Count == 0,
                "Two commands share a chord AFTER the WebHost remap. The default profile can be " +
                "clean and still collide here — Ctrl+Shift+letter becomes Alt+Shift+letter, so any " +
                "new Alt+Shift+letter binding must be checked against that:\n  " +
                string.Join("\n  ", clashes));
        }

        [Fact]
        public void TheRemapActuallyRan()
        {
            // Guards the test itself. If the remap silently stopped applying, the conflict check
            // above would pass by testing the wrong profile — which is precisely the failure it
            // exists to catch, reproduced one level up.
            var profile = RemappedProfile().CurrentProfile;

            Assert.DoesNotContain(profile.Shortcuts,
                b => b.Ctrl && b.Shift && !b.Alt && b.Key.Length == 1 && char.IsLetter(b.Key[0]));
        }

        [Fact]
        public void EveryCommandThatHadABindingStillHasOne()
        {
            // The remap removes and re-adds. A command that lost its only chord in that shuffle is
            // simply unreachable on the WebHost, and nothing else would report it.
            var before = new ShortcutManager(new EmptyPaths())
                .CurrentProfile.Shortcuts.Select(b => b.Command).Distinct().ToHashSet();

            var after = RemappedProfile().CurrentProfile.Shortcuts
                .Select(b => b.Command).Distinct().ToHashSet();

            // Chords the remap deliberately DROPS because the browser owns them outright and the
            // command is reachable another way — named so the exemption is a decision on record.
            var deliberatelyDropped = new[]
            {
                SystemCommand.AddTab, SystemCommand.CloseTab,
                SystemCommand.SwitchTabNext, SystemCommand.SwitchTabPrev,
            };

            var lost = before.Except(after).Except(deliberatelyDropped).ToList();

            Assert.True(lost.Count == 0,
                "Commands with no chord at all on the WebHost:\n  " +
                string.Join("\n  ", lost));
        }
    }
}
