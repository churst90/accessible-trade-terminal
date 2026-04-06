using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Core.Services.Accessibility
{
    public interface IEarconService
    {
        void PlayError(ErrorSeverity severity);
        void PlaySuccess();
        void PlayRetry();
        void PlayConnectionState(ConnectionState state);
        void PlayBoundary();
        void PlayInfo();
        /// <summary>Bell-like tone signalling a new live bar has opened.</summary>
        void PlayNewBar();
    }

    public class EarconService : IEarconService
    {
        private readonly ISonificationManager _sonificationManager;
        private readonly ISoundPatchLibrary _patchLibrary;
        private readonly ConcurrentDictionary<string, DateTime> _lastPlayed = new();
        private readonly TimeSpan _minInterval = TimeSpan.FromMilliseconds(200);

        public EarconService(ISonificationManager sonificationManager, ISoundPatchLibrary patchLibrary)
        {
            _sonificationManager = sonificationManager;
            _patchLibrary = patchLibrary;
        }

        /// <summary>
        /// Plays a note using the assigned SoundPatch for <paramref name="earconKey"/> if one
        /// is configured in <see cref="EarconSettings"/>; otherwise falls back to
        /// <paramref name="defaultFreq"/>, <paramref name="defaultDuration"/>,
        /// <paramref name="defaultWave"/>, and <paramref name="defaultVol"/>.
        /// </summary>
        private void PlayWithPatchFallback(string earconKey, double defaultFreq, double defaultDuration,
            string defaultWave, float defaultVol, float pan = 0f)
        {
            if (_patchLibrary.EarconOverrides.EarconPatchIds.TryGetValue(earconKey, out var patchId))
            {
                var patch = _patchLibrary.GetPatch(patchId);
                if (patch != null)
                {
                    _sonificationManager.PlayNote(
                        patch.BaseFrequency * patch.FreqMultiplier,
                        patch.DurationSeconds,
                        patch.Waveform,
                        patch.Volume,
                        pan);
                    return;
                }
            }
            _sonificationManager.PlayNote(defaultFreq, defaultDuration, defaultWave, defaultVol, pan);
        }

        public void PlayInfo()
        {
            if (!CanPlay("info")) return;
            PlayWithPatchFallback("Info", 660, 0.1, "sine", 0.1f);
        }

        private bool CanPlay(string key)
        {
            if (!_sonificationManager.IsEnabled) return false;
            if (_lastPlayed.TryGetValue(key, out var last) && DateTime.Now - last < _minInterval) return false;
            _lastPlayed[key] = DateTime.Now;
            return true;
        }

        public void PlayError(ErrorSeverity severity)
        {
            if (!CanPlay("error")) return;

            switch (severity)
            {
                case ErrorSeverity.Low:
                    _sonificationManager.PlayNote(150, 0.1, "sine", 0.1f, 0);
                    break;
                case ErrorSeverity.Medium:
                    _sonificationManager.PlayNote(100, 0.2, "sawtooth", 0.15f, 0);
                    break;
                case ErrorSeverity.High:
                case ErrorSeverity.Critical:
                    _sonificationManager.PlayNote(80, 0.4, "square", 0.2f, -0.5f);
                    _sonificationManager.PlayNote(85, 0.4, "square", 0.2f, 0.5f);
                    break;
            }
        }

        public void PlaySuccess()
        {
            if (!CanPlay("success")) return;
            _sonificationManager.PlayNote(440, 0.1, "sine", 0.1f, 0);
            _sonificationManager.PlayNote(880, 0.1, "sine", 0.1f, 0);
        }

        public void PlayRetry()
        {
            if (!CanPlay("retry")) return;
            _sonificationManager.PlayNote(330, 0.1, "sine", 0.1f, 0);
            _sonificationManager.PlayNote(220, 0.1, "sine", 0.1f, 0);
        }

        public void PlayBoundary()
        {
            if (!CanPlay("boundary")) return;
            PlayWithPatchFallback("Boundary", 150, 0.1, "square", 0.1f);
        }

        public void PlayNewBar()
        {
            if (!CanPlay("newbar")) return;
            if (_patchLibrary.EarconOverrides.EarconPatchIds.TryGetValue("NewBar", out var patchId))
            {
                var patch = _patchLibrary.GetPatch(patchId);
                if (patch != null)
                {
                    _sonificationManager.PlayNote(patch.BaseFrequency * patch.FreqMultiplier, patch.DurationSeconds, patch.Waveform, patch.Volume, 0);
                    return;
                }
            }
            // Bell: three sine partials at fundamental + octave + minor third above octave.
            _sonificationManager.PlayNote(880,  0.06, "sine", 0.12f, 0);
            _sonificationManager.PlayNote(1320, 0.06, "sine", 0.08f, 0);
            _sonificationManager.PlayNote(2200, 0.06, "sine", 0.05f, 0);
        }

        public void PlayConnectionState(ConnectionState state)
        {
            if (!CanPlay($"conn_{state}")) return;

            switch (state)
            {
                case ConnectionState.Connecting:
                    _sonificationManager.PlayNote(440, 0.05, "sine", 0.05f, 0);
                    break;
                case ConnectionState.Connected:
                    _sonificationManager.PlayNote(800, 0.2, "sine", 0.1f, 0);
                    break;
                case ConnectionState.Disconnected:
                    _sonificationManager.PlayNote(150, 0.3, "square", 0.1f, 0);
                    break;
                case ConnectionState.Error:
                    _sonificationManager.PlayNote(100, 0.5, "sawtooth", 0.2f, 0);
                    break;
            }
        }
    }
}
