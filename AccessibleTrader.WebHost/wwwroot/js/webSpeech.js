// Browser-native TTS for the WebHost. Augments the existing
// `window.accessibleTrader` object (defined in keyboard.js) with a speak()
// method that wraps the SpeechSynthesis Web API. Called from
// AccessibleTrader.WebHost.Components.BrowserSpeechBridge via JS interop.
(function () {
    window.accessibleTrader = window.accessibleTrader || {};

    window.accessibleTrader.speak = function (text, interrupt) {
        if (!('speechSynthesis' in window)) return;
        try {
            if (interrupt) {
                window.speechSynthesis.cancel();
            }
            if (!text) return;
            var u = new SpeechSynthesisUtterance(text);
            // Default rate/pitch produce a calm cadence; users can override
            // their preferred voice via the OS-level speech-synthesis config.
            u.rate = 1.0;
            window.speechSynthesis.speak(u);
        } catch (e) {
            // Never throw across the JS bridge. SpeechSynthesis quirks (eg.
            // Firefox cancelling a not-yet-started utterance) shouldn't
            // bubble into the .NET side.
        }
    };
})();
