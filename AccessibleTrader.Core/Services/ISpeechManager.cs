using System;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Provides a platform-agnostic bridge for screen reader speech (TTS).
    /// Implementation may use NVDA Direct, ARIA Live, or native OS APIs.
    /// </summary>
    public interface ISpeechManager
    {
        bool IsSpeechEnabled { get; set; }
        Action<string>? OnSpeak { get; set; }
        void Speak(string text, bool interrupt = false);
        void Silence();
    }
}
