using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Guard: no two commands may share a key combination in the default profile.
    ///
    /// <para>
    /// A duplicate binding is a uniquely nasty bug in a keyboard-first, screen-reader-first app.
    /// There is no visual cue that a key did the wrong thing, so the user presses what they
    /// believe is "step replay forward" and silently toggles braille output instead. Nothing on
    /// screen contradicts them. This test exists so that failure mode can never ship — adding a
    /// binding that collides fails the build rather than the user.
    /// </para>
    /// </summary>
    public class ShortcutConflictTests
    {
        private readonly record struct Combo(string Key, bool Ctrl, bool Alt, bool Shift)
        {
            public override string ToString()
            {
                string mods = (Ctrl ? "Ctrl+" : "") + (Alt ? "Alt+" : "") + (Shift ? "Shift+" : "");
                return mods + Key;
            }
        }

        /// <summary>Path service pointed at an empty temp dir so the user's own shortcuts.json
        /// on this machine cannot leak into the test and mask a real default-profile clash.</summary>
        private sealed class EmptyPaths : IPlatformPathService
        {
            public EmptyPaths()
            {
                AppDataDirectory = TestTemp.NewDir("at-shortcut-tests-");
                CacheDirectory = AppDataDirectory;
            }
            public string AppDataDirectory { get; }
            public string CacheDirectory { get; }
        }

        private static List<(Combo Combo, SystemCommand Command)> DefaultBindings()
        {
            var manager = new ShortcutManager(new EmptyPaths());
            var profile = manager.CurrentProfile;
            Assert.NotNull(profile);

            return profile!.Shortcuts
                .Select(s => (new Combo(s.Key.ToUpperInvariant(), s.Ctrl, s.Alt, s.Shift), s.Command))
                .ToList();
        }

        [Fact]
        public void NoTwoCommandsShareAKeyCombination()
        {
            var bindings = DefaultBindings();

            var clashes = bindings
                .GroupBy(b => b.Combo)
                // One command legitimately owning several combos is fine (replay toggle has both
                // F11 and the web-safe chord). A single COMBO driving several commands is not.
                .Where(g => g.Select(x => x.Command).Distinct().Count() > 1)
                .ToList();

            Assert.True(clashes.Count == 0,
                "Conflicting key bindings:\n" + string.Join("\n", clashes.Select(g =>
                    $"  {g.Key} → {string.Join(", ", g.Select(x => x.Command).Distinct())}")));
        }

        [Fact]
        public void ReplayTransportSitsOnF9ThroughF11()
        {
            var bindings = DefaultBindings();

            Combo? For(SystemCommand cmd) => bindings
                .Where(b => b.Command == cmd)
                .Select(b => (Combo?)b.Combo)
                .FirstOrDefault();

            Assert.Equal("F9", For(SystemCommand.ReplayStepForward)?.Key);
            Assert.Equal("F9", For(SystemCommand.ReplayStepBack)?.Key);
            Assert.True(For(SystemCommand.ReplayStepBack)?.Shift);
            Assert.Equal("F10", For(SystemCommand.ReplayPlayPause)?.Key);
        }

        [Fact]
        public void ReplayDoesNotStealTheAccessibilityFKeys()
        {
            // F2/F3 (speech, sonification) and F4 (braille) are the accessibility tier. Anything
            // that shadows them is worse than a missing feature.
            var bindings = DefaultBindings();
            var reserved = new[] { "F2", "F3", "F4" };

            foreach (var b in bindings)
            {
                if (!reserved.Contains(b.Combo.Key)) continue;
                Assert.True(
                    b.Command is SystemCommand.ToggleSpeech or SystemCommand.ToggleEventSpeech
                        or SystemCommand.ToggleSonification or SystemCommand.ToggleEarcons
                        or SystemCommand.ToggleBraille or SystemCommand.OpenBrailleSettings,
                    $"{b.Combo} is bound to {b.Command}, shadowing the accessibility F-key tier.");
            }
        }

        [Fact]
        public void EveryBoundCommandIsADefinedEnumValue()
        {
            var defined = Enum.GetValues<SystemCommand>().ToHashSet();
            foreach (var b in DefaultBindings())
                Assert.Contains(b.Command, defined);
        }
    }
}
