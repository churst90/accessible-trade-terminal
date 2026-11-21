using AccessibleTrader.Core.Models;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// A keyboard binding as words a speech synthesiser voices reliably: "Control plus Alt plus
    /// Shift plus H", never "Ctrl+Alt+Shift+H". One implementation for the toolbar and the chart
    /// context menu, both of which read the user's live profile so a rebind is reflected.
    /// </summary>
    public static class ShortcutWords
    {
        /// <summary>
        /// The words for a command's binding, or null when it has none. When a command has more
        /// than one binding, a letter/digit chord wins over a function key: the portable chord is
        /// the one the toolbar and manual already name (replay's Ctrl+Alt+Shift+P over F11).
        /// </summary>
        public static string? ForCommand(IShortcutManager? manager, SystemCommand command)
        {
            var matches = manager?.CurrentProfile?.Shortcuts
                .Where(s => s.Command == command)
                .OrderBy(s => IsPortable(s.Key) ? 0 : 1)
                .ThenByDescending(s => (s.Ctrl ? 1 : 0) + (s.Alt ? 1 : 0) + (s.Shift ? 1 : 0))
                .ToList();

            return matches is { Count: > 0 } ? ForBinding(matches[0]) : null;
        }

        /// <summary>One binding in words, modifiers first in the app's standing order.</summary>
        public static string ForBinding(ShortcutDefinition binding)
        {
            var parts = new List<string>(4);
            if (binding.Ctrl) parts.Add("Control");
            if (binding.Alt) parts.Add("Alt");
            if (binding.Shift) parts.Add("Shift");
            parts.Add(KeyWords(binding.Key));
            return string.Join(" plus ", parts);
        }

        /// <summary>
        /// A command's name as a person says it, "Zoom in" and not "ZoomIn": the Go bar's name
        /// for it (every command has one; GoBarRegistry's census test sees to that), else the
        /// enum name split into words. The keyboard settings showed "NavLeft" and "JumpToLatest"
        /// until 2026-11-21.
        /// </summary>
        public static string ActionName(string command) =>
            Enum.TryParse<SystemCommand>(command, out var c) && Names.Value.TryGetValue(c, out var name)
                ? name
                : SplitWords(command);

        private static readonly Lazy<Dictionary<SystemCommand, string>> Names = new(() =>
            GoBar.GoBarRegistry.All
                .GroupBy(e => e.Command)
                .ToDictionary(g => g.Key, g => g.First().Name));

        private static string SplitWords(string name)
        {
            var sb = new System.Text.StringBuilder(name.Length + 8);
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) sb.Append(' ');
                sb.Append(name[i]);
            }
            return sb.ToString();
        }

        private static bool IsPortable(string key) =>
            key.Length == 1 && char.IsLetterOrDigit(key[0]);

        /// <summary>The spoken name of a key, including the named keys the menu can meet.</summary>
        public static string KeyWords(string key)
        {
            if (key.Length == 1)
            {
                char c = key[0];
                return c switch
                {
                    ' ' => "Space",
                    '\\' => "Backslash",
                    ',' => "Comma",
                    '.' => "Period",
                    ';' => "Semicolon",
                    '\'' => "Apostrophe",
                    '[' => "Left Bracket",
                    ']' => "Right Bracket",
                    '-' => "Minus",
                    '=' => "Equals",
                    '/' => "Slash",
                    _ => char.IsLetter(c) ? c.ToString().ToUpperInvariant() : c.ToString(),
                };
            }

            return key.ToUpperInvariant() switch
            {
                "SPACE" => "Space",
                "TAB" => "Tab",
                "ENTER" => "Enter",
                "RETURN" => "Enter",
                "ESCAPE" => "Escape",
                "ESC" => "Escape",
                "DELETE" => "Delete",
                "BACKSPACE" => "Backspace",
                "PAGEUP" => "Page Up",
                "PAGEDOWN" => "Page Down",
                "HOME" => "Home",
                "END" => "End",
                "UP" => "Up Arrow",
                "DOWN" => "Down Arrow",
                "LEFT" => "Left Arrow",
                "RIGHT" => "Right Arrow",
                "OEM5" => "Backslash",
                "OEM1" => "Semicolon",
                "OEMCOMMA" => "Comma",
                "OEMPERIOD" => "Period",
                "OEMSEMICOLON" => "Semicolon",
                "OEMQUOTES" => "Apostrophe",
                "OEM7" => "Apostrophe",
                "OEM2" => "Slash",
                "OEM3" => "Backtick",
                "OEM4" => "Left Bracket",
                "OEM6" => "Right Bracket",
                "OEMMINUS" => "Minus",
                "OEMPLUS" => "Equals",
                "CONTEXTMENU" => "Menu key",
                _ => key,
            };
        }
    }
}
