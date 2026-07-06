using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services
{
    public interface ISonificationManager
    {
        bool IsEnabled { get; set; }
        bool IsPlaying { get; }

        event Action PlaybackFinished;
        event Action<int> PlaybackPointReached;

        void PlayNote(double frequency, double durationSeconds, string waveformType, float volume, float pan = 0, double delayMilliseconds = 0);

        /// <summary>
        /// Plays an entire <see cref="SoundPatch"/> — all oscillator layers, with envelope and noise
        /// blend/colour — as one-shot voices. Used by the Sound Designer preview and earcon overrides;
        /// unlike <see cref="PlayNote"/> it carries envelope/noise and sounds multi-oscillator patches.
        /// </summary>
        void PlayPatch(SoundPatch patch, float volumeScale = 1f, float pan = 0f);
        void Stop();
        void Silence();

        void SonifySeries(ChartSeries series, Ohlcv point, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, int dataIndex, float masterVolume = 1.0f, double durationSeconds = 0.2, double delayMilliseconds = 0);
        void SonifyComponent(ChartSeries series, int componentIndex, Ohlcv point, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, int dataIndex, float masterVolume = 1.0f, double durationSeconds = 0.2, double delayMilliseconds = 0);
        
        AudioPoint CreateAudioPoint(ChartSeries series, int componentIndex, Ohlcv point, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, int dataIndex, float masterVolume = 1.0f, double? overrideValue = null);

        /// <summary>Sets the master output gain applied to all audio voices. Range 0.0–1.0.</summary>
        void SetMasterVolume(float volume);
    }
}