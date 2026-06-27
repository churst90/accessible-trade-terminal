using System.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// Remaps Ctrl+Shift+letter shortcuts to Alt+Shift+letter on the
    /// WebHost. Firefox reserves chords like Ctrl+Shift+T (reopen closed
    /// tab), Ctrl+Shift+H (history), Ctrl+Shift+P (private window),
    /// Ctrl+Shift+J (browser console), Ctrl+Shift+R (hard reload), and
    /// Ctrl+Shift+W (close window) at the chrome level — they're handled
    /// before any page-level keyboard listener fires, so even our
    /// capture-phase <c>preventDefault</c> can't stop them. The MAUI head
    /// runs inside a WebView with no browser chrome, so its default
    /// shortcut profile uses Ctrl+Shift+letter for every drawing tool
    /// (T = trend, H = horizontal, R = rectangle, etc.). Under the
    /// WebHost we shift each of those to Alt+Shift+letter (a chord
    /// Firefox does not claim), preserving the "same letter, same
    /// drawing tool" muscle memory.
    ///
    /// Three-modifier chords (Ctrl+Alt+Shift+letter) are not touched —
    /// they're not Firefox-reserved and we want to keep them as-is.
    /// Non-letter Ctrl+Shift chords (Ctrl+Shift+Space for
    /// PlayComponent, Ctrl+Shift+Tab for SwitchTabPrev, Ctrl+Shift+F12
    /// etc.) are handled separately below.
    ///
    /// On top of the letter remap, a handful of <b>single-Ctrl</b> chords are
    /// reserved by the browser at the chrome level — they are dispatched before
    /// any page-level listener, so even our capture-phase <c>preventDefault</c>
    /// cannot stop them. These are rebound to web-safe equivalents:
    /// <list type="bullet">
    /// <item>Ctrl+T (new browser tab) → AddTab also answers to Alt+Shift+N.</item>
    /// <item>Ctrl+W (close browser tab) → CloseTab via the tab bar's × button or
    /// Delete while the tab bar is focused.</item>
    /// <item>Ctrl+Tab / Ctrl+Shift+Tab (switch browser tab) → reach the tab bar
    /// with Ctrl+Alt+Shift+T, then arrows / number row.</item>
    /// <item>Ctrl+PageUp / Ctrl+PageDown (switch browser tab) → sub-pane jumps
    /// move to Alt+PageUp / Alt+PageDown.</item>
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

            // 1. Ctrl+Shift+letter → Alt+Shift+letter (drawing tools, detailed summary).
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

            //    …and shift the sub-pane jumps off Ctrl+PageUp/Down (browser tab cycling)
            //    onto Alt+PageUp/Down, which browsers do not reserve.
            remapped += RebindModifier(profile,
                s => s.Ctrl && !s.Alt && !s.Shift && KeyIs(s.Key, "PAGEUP"),
                s => s with { Ctrl = false, Alt = true });
            remapped += RebindModifier(profile,
                s => s.Ctrl && !s.Alt && !s.Shift && KeyIs(s.Key, "PAGEDOWN"),
                s => s with { Ctrl = false, Alt = true });

            if (remapped > 0)
            {
                shortcuts.LoadProfile(profile); // rebuild the lookup dictionary
                logger.LogInformation(
                    "Shortcuts: applied {Count} browser-host override(s) — Ctrl+Shift+letter → Alt+Shift+letter, plus reserved single-Ctrl tab/page chords rebound to web-safe equivalents.",
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

        /// <summary>Replace every binding matching <paramref name="match"/> with <paramref name="rebind"/>(it). Returns how many.</summary>
        private static int RebindModifier(
            ShortcutProfile profile,
            System.Func<ShortcutDefinition, bool> match,
            System.Func<ShortcutDefinition, ShortcutDefinition> rebind)
        {
            var hits = profile.Shortcuts.Where(match).ToList();
            foreach (var h in hits)
            {
                profile.Shortcuts.Remove(h);
                profile.Shortcuts.Add(rebind(h));
            }
            return hits.Count;
        }

        private static bool KeyIs(string key, string expected)
            => string.Equals(key, expected, System.StringComparison.OrdinalIgnoreCase);

        private static bool IsSingleAsciiLetter(string key)
            => key.Length == 1 && key[0] is >= 'A' and <= 'Z';
    }
}
