using System;
using System.Collections.Generic;

namespace AccessibleTrader.Core.Services.Input
{
    public interface IKeyNormalizationService
    {
        string Normalize(string key);
    }

    /// <summary>
    /// Unifies platform-specific key names into a single semantic standard.
    /// Maps browser (Web), Android (DPAD), and iOS key strings to the 
    /// standard names expected by the ShortcutManager.
    /// </summary>
    public class KeyNormalizationService : IKeyNormalizationService
    {
        private static readonly Dictionary<string, string> _normalizationMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // --- ARROWS ---
            { "ARROWLEFT", "LEFT" },
            { "ARROWRIGHT", "RIGHT" },
            { "ARROWUP", "UP" },
            { "ARROWDOWN", "DOWN" },
            { "DPADLEFT", "LEFT" },
            { "DPADRIGHT", "RIGHT" },
            { "DPADUP", "UP" },
            { "DPADDOWN", "DOWN" },
            { "LEFTARROW", "LEFT" },
            { "RIGHTARROW", "RIGHT" },
            { "UPARROW", "UP" },
            { "DOWNARROW", "DOWN" },

            // --- FUNCTION KEYS ---
            { "F1", "F1" }, { "F2", "F2" }, { "F3", "F3" }, { "F4", "F4" },
            { "F5", "F5" }, { "F6", "F6" }, { "F7", "F7" }, { "F8", "F8" },
            { "F9", "F9" }, { "F10", "F10" }, { "F11", "F11" }, { "F12", "F12" },

            // --- NAVIGATION ---
            { "PAGE_UP", "PAGEUP" },
            { "PAGE_DOWN", "PAGEDOWN" },
            { "MOVE_HOME", "HOME" },
            { "MOVE_END", "END" },
            { "PRIOR", "PAGEUP" },
            { "NEXT", "PAGEDOWN" },

            // --- MISC ---
            { "ESC", "ESCAPE" },
            { "DEL", "DELETE" },
            { "INS", "INSERT" },
            { "ENTER", "RETURN" }, // Some platforms use Enter, some Return
            { "BACK", "BACKSPACE" },
            { "TAB", "TAB" },
            
            // --- SYMBOLS (OEM) ---
            { "OEM_MINUS", "-" },
            { "OEM_PLUS", "=" },
            { "OEM_4", "[" },
            { "OEM_6", "]" },
            { "OEM_1", ";" },
            { "OEM_7", "'" },
            { "OEM_PERIOD", "." },
            { "OEM_COMMA", "," },
            { "OEM_2", "/" },
            { "OEM_3", "`" },
            { "OEM_5", "\\" }
        };

        public string Normalize(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;

            string upperKey = key.Trim().ToUpperInvariant();

            // 1. Exact match in map
            if (_normalizationMap.TryGetValue(upperKey, out var normalized))
            {
                return normalized;
            }

            // 2. Cleanup common prefixes/suffixes
            string cleanKey = upperKey;
            if (cleanKey.StartsWith("KEY_")) cleanKey = cleanKey.Substring(4);
            if (cleanKey.StartsWith("KEY")) cleanKey = cleanKey.Substring(3);

            // 3. Second pass after cleanup
            if (_normalizationMap.TryGetValue(cleanKey, out normalized))
            {
                return normalized;
            }

            return cleanKey;
        }
    }
}
