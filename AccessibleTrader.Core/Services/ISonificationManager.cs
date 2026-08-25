using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services
{
    public interface ISonificationManager
    {
        bool IsEnabled { get; set; }
        bool IsPlaying { get; }

        event Action PlaybackFinished;
        event Action<int> PlaybackPointReached;

        /// <summary>Set <paramref name="force"/> to bypass the F3 chart-sonification
        /// gate — earcons have their own mute tier (Shift+F3, enforced by
        /// EarconService) and must not die with navigation tones.</summary>
        void PlayNote(double frequency, double durationSeconds, string waveformType, float volume, float pan = 0, double delayMilliseconds = 0, bool force = false);

        /// <summary>
        /// Plays an entire <see cref="SoundPatch"/> — all oscillator layers, with envelope and noise
        /// blend/colour — as one-shot voices. Used by the Sound Designer preview and earcon overrides;
        /// unlike <see cref="PlayNote"/> it carries envelope/noise and sounds multi-oscillator patches.
        /// </summary>
        void PlayPatch(SoundPatch patch, float volumeScale = 1f, float pan = 0f, bool force = false);
        void Stop();
        void Silence();

        // NO SonifySeries/SonifyComponent here either — same reason as on IAudioFeedbackRouter.
        // SyncNavigationSlots is the one writer of voice slot 0.

        AudioPoint CreateAudioPoint(ChartSeries series, int componentIndex, Ohlcv point, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, int dataIndex, float masterVolume = 1.0f, double? overrideValue = null);

        /// <summary>Sets the master output gain applied to all audio voices. Range 0.0–1.0.</summary>
        void SetMasterVolume(float volume);
    }
}