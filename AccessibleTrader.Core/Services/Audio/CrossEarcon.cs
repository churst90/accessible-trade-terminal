using System;
using System.Threading.Tasks;

namespace AccessibleTrader.Core.Services.Audio
{
    /// <summary>
    /// Fires a short, directional two-note earcon when an indicator value crosses a reference /
    /// overbought / oversold level. Up-cross rises (C5 → G5), down-cross falls (G5 → C5), so the
    /// direction is unmistakable. Used by BOTH navigation (arrow onto a cross bar, either direction)
    /// and playback (Space / Shift+Space / Ctrl+Shift+Space) so crosses are always audible.
    ///
    /// The chirp lives on two dedicated slots at the top of the earcon range (16-31), clear of the
    /// oscillator / playback voices, so it layers cleanly over whatever tone is sounding.
    /// </summary>
    internal static class CrossEarcon
    {
        public const int SlotA = 30;
        public const int SlotB = 31;

        private const double LowFreq = 523.25;   // C5
        private const double HighFreq = 783.99;  // G5 (a perfect fifth up)
        private const double NoteDur = 0.09;      // seconds per note
        private const int GapMs = 70;             // stagger between the two notes
        private const float BaseVol = 0.5f;   // sits clearly above the continuous bed (was 0.22)

        /// <summary>
        /// Fires the chirp. <paramref name="direction"/>: +1 = crossed up (rising), -1 = crossed down
        /// (falling), 0 = no-op. <paramref name="volume"/> scales the base level (e.g. a level's
        /// EarconVolume); <paramref name="pan"/> places it at the bar's stereo position.
        /// </summary>
        public static void Fire(IAudioDriver driver, int direction, float volume = 1f, float pan = 0f)
        {
            if (direction == 0 || driver == null) return;

            double f1 = direction > 0 ? LowFreq : HighFreq;
            double f2 = direction > 0 ? HighFreq : LowFreq;
            float vol = Math.Clamp(BaseVol * volume, 0f, 1f);

            // Ping envelope + click for a crisp, self-terminating transient.
            driver.SetVoice(SlotA, f1, vol, pan, "sine", false, NoteDur, -1, "Ping", true, 0f);
            _ = Task.Delay(GapMs).ContinueWith(
                _ => driver.SetVoice(SlotB, f2, vol, pan, "sine", false, NoteDur, -1, "Ping", true, 0f),
                TaskScheduler.Default);
        }
    }
}
