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
    /// etc.) are also left alone — Firefox doesn't reserve them.
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

            // Snapshot the candidates first — we mutate the list inside the loop.
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

            if (remapped > 0)
            {
                shortcuts.LoadProfile(profile); // rebuild the lookup dictionary
                logger.LogInformation(
                    "Shortcuts: remapped {Count} Ctrl+Shift+letter chord(s) to Alt+Shift+letter (Firefox reserves several Ctrl+Shift+* chords at browser-chrome level).",
                    remapped);
            }
        }

        private static bool IsSingleAsciiLetter(string key)
            => key.Length == 1 && key[0] is >= 'A' and <= 'Z';
    }
}
