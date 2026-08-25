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
            { "OEM_5", "\\" },

            // --- SHIFTED DIGITS ---
            //
            // A browser reports event.key as the character the keypress PRODUCES, so Shift+1 arrives
            // as "!" and never as "1". Every shortcut bound to a digit with Shift held was therefore
            // unreachable: Ctrl+Alt+Shift+1/2/3 (arm a quick trade) and Ctrl+Alt+Shift+0 (disarm)
            // silently did nothing, while Ctrl+Alt+Shift+X worked because Shift+X is still "X".
            // Found by a maintainer testing paper trading, not by any test.
            //
            // This mapping is US-layout. A French AZERTY keyboard produces digits only WITH Shift, so
            // there "1" arrives unshifted and the entries below never fire — which is harmless,
            // because the binding matches directly. The layout-proof answer is event.code
            // ("Digit1"), which is also accepted below so that a caller passing a code rather than a
            // key resolves identically.
            { "!", "1" }, { "@", "2" }, { "#", "3" }, { "$", "4" }, { "%", "5" },
            { "^", "6" }, { "&", "7" }, { "*", "8" }, { "(", "9" }, { ")", "0" },

            // --- KEY CODES (layout-independent) ---
            { "DIGIT0", "0" }, { "DIGIT1", "1" }, { "DIGIT2", "2" }, { "DIGIT3", "3" }, { "DIGIT4", "4" },
            { "DIGIT5", "5" }, { "DIGIT6", "6" }, { "DIGIT7", "7" }, { "DIGIT8", "8" }, { "DIGIT9", "9" },
            { "NUMPAD0", "0" }, { "NUMPAD1", "1" }, { "NUMPAD2", "2" }, { "NUMPAD3", "3" }, { "NUMPAD4", "4" },
            { "NUMPAD5", "5" }, { "NUMPAD6", "6" }, { "NUMPAD7", "7" }, { "NUMPAD8", "8" }, { "NUMPAD9", "9" },
        };

        public string Normalize(string key) => NormalizeKey(key);

        /// <summary>
        /// The single normalisation function, exposed statically so that
        /// <see cref="AccessibleTrader.Core.Services.ShortcutManager"/> can run <b>binding</b> keys
        /// through the very same code that incoming keys go through.
        ///
        /// <para>
        /// It has to be the same function on both sides or bindings quietly become unreachable. That
        /// is not hypothetical: bindings declared as <c>"ENTER"</c> were compared against incoming
        /// keys that this map rewrites to <c>"RETURN"</c>, so Shift+Enter and Ctrl+Enter — the two
        /// keys that actually place a quick trade — matched nothing. Both sides now normalise, so a
        /// binding can be written in whichever spelling reads best.
        /// </para>
        /// </summary>
        public static string NormalizeKey(string key)
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
