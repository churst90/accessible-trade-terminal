using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// Browser-host overrides for the shortcut profile.
    ///
    /// Until 2026-09-05 this class's main job was rewriting every Ctrl+Shift+letter
    /// drawing chord to Alt+Shift+letter, because Firefox and Chrome reserve most of
    /// that row at the chrome level — Ctrl+Shift+T (reopen closed tab), Ctrl+Shift+H
    /// (history), Ctrl+Shift+P (private window), Ctrl+Shift+J (console), Ctrl+Shift+R
    /// (hard reload), Ctrl+Shift+W (close window) — and they are dispatched before any
    /// page-level listener fires, so even a capture-phase <c>preventDefault</c> cannot
    /// stop them. The DEFAULT profile now binds those commands to Alt+Shift+letter on
    /// every head (see <c>ShortcutManager.InitializeDefaultProfile</c>), so the letter
    /// rewrite is a legacy step for a saved shortcuts.json that still carries the old
    /// chords: same letter, same command, browser-safe modifier.
    ///
    /// Three-modifier chords (Ctrl+Alt+Shift+letter) are not touched — they are not
    /// browser-reserved. Non-letter Ctrl+Shift chords (Ctrl+Shift+Space for
    /// PlayComponent, Ctrl+Shift+Tab for SwitchTabPrev, Ctrl+Shift+F12 etc.) are handled
    /// separately below.
    ///
    /// A handful of <b>single-Ctrl</b> chords are
    /// reserved by the browser at the chrome level — they are dispatched before
    /// any page-level listener, so even our capture-phase <c>preventDefault</c>
    /// cannot stop them. These are rebound to web-safe equivalents:
    /// <list type="bullet">
    /// <item>Ctrl+T (new browser tab) → AddTab also answers to Alt+Shift+N.</item>
    /// <item>Ctrl+W (close browser tab) → CloseTab via the tab bar's × button or
    /// Delete while the tab bar is focused.</item>
    /// <item>Ctrl+Tab / Ctrl+Shift+Tab (switch browser tab) → reach the tab bar
    /// with Ctrl+Alt+Shift+T, then arrows / number row.</item>
    /// <item>Ctrl+PageUp / Ctrl+PageDown (switch browser tab) — nothing to do: pane
    /// navigation is Alt+PageUp / Alt+PageDown on every head now, and Ctrl+PageUp/Down is
    /// deliberately left unbound rather than reassigned.</item>
    /// </list>
    /// The reserved bindings are removed so the Help dialog never advertises a
    /// chord the browser eats.
    ///
    /// Run-once at app startup from <c>Program.cs</c>. Mutates the
    /// in-memory profile only; does not persist to disk so user
    /// customisations are preserved.
    /// </summary>
    public static class WebHostShortcutRemap
    {
        public static void ApplyBrowserHostOverrides(IShortcutManager shortcuts, ILogger logger)
        {
            var profile = shortcuts.CurrentProfile;
            int remapped = 0;

            // 1. Ctrl+Shift+letter → Alt+Shift+letter. LEGACY since 2026-09-05: the default
            //    profile binds the drawing tools and the detailed summary to Alt+Shift+letter on
            //    every head, so on a fresh profile this loop finds nothing. It stays for a saved
            //    shortcuts.json that still carries the old Ctrl+Shift chords — those are the
            //    browser-reserved ones, and a user who rebinds a command onto Ctrl+Shift+letter
            //    by hand is choosing a chord the browser eats.
            //    Snapshot the candidates first — we mutate the list inside the loop.
            var candidates = profile.Shortcuts
                .Where(s => s.Ctrl && s.Shift && !s.Alt && IsSingleAsciiLetter(s.Key))
                .ToList();

            foreach (var s in candidates)
            {
                // Remove the Ctrl+Shift+letter binding.
                profile.Shortcuts.Remove(s);

                // Add the equivalent Alt+Shift+letter binding for the same command.
                profile.Shortcuts.Add(s with { Ctrl = false, Alt = true });
                remapped++;
            }

            // 2. Reserved single-Ctrl chrome chords that the page cannot intercept.
            //    Drop the ones with another in-app path (AddTab/CloseTab/SwitchTab)…
            remapped += RemoveChord(profile, s => s.Ctrl && !s.Alt && !s.Shift && KeyIs(s.Key, "T"));   // Ctrl+T  (AddTab → Alt+Shift+N)
            remapped += RemoveChord(profile, s => s.Ctrl && !s.Alt && !s.Shift && KeyIs(s.Key, "W"));   // Ctrl+W  (CloseTab → × / Delete in tab bar)
            remapped += RemoveChord(profile, s => s.Ctrl && !s.Alt && KeyIs(s.Key, "TAB"));             // Ctrl+Tab / Ctrl+Shift+Tab (→ Ctrl+Alt+Shift+T)

            //    Ctrl+PageUp/Down (browser tab cycling) needs no rule any more. Pane navigation
            //    was moved onto Alt+PageUp/Down in the DEFAULT profile, so the desktop and the
            //    browser agree about a navigation key instead of the Help dialog having to
            //    explain a difference. A user who rebinds something onto Ctrl+PageUp/Down is
            //    still choosing a chord the browser eats, and ShortcutConflictTests says so.
            if (remapped > 0)
            {
                shortcuts.LoadProfile(profile); // rebuild the lookup dictionary
                logger.LogInformation(
                    "Shortcuts: applied {Count} browser-host override(s) — reserved single-Ctrl tab chords dropped, plus any legacy Ctrl+Shift+letter binding moved to Alt+Shift+letter.",
                    remapped);
            }
        }

        /// <summary>Remove every binding matching <paramref name="match"/>. Returns how many.</summary>
        private static int RemoveChord(ShortcutProfile profile, System.Func<ShortcutDefinition, bool> match)
        {
            var hits = profile.Shortcuts.Where(match).ToList();
            foreach (var h in hits) profile.Shortcuts.Remove(h);
            return hits.Count;
        }

        private static bool KeyIs(string key, string expected)
            => string.Equals(key, expected, System.StringComparison.OrdinalIgnoreCase);

        private static bool IsSingleAsciiLetter(string key)
            => key.Length == 1 && key[0] is >= 'A' and <= 'Z';
    }
}
