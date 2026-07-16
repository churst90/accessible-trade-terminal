using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace AccessibleTrader.Core.Services.Audio
{
    /// <summary>
    /// Process-wide registry of user audio material for the engine:
    ///
    /// WAVETABLES — single-cycle waveforms (an AKWF file, one period of a cello,
    /// anything). A wavetable voice loops the table at any pitch, exactly like a
    /// built-in oscillator with a custom shape — pitch mapping, envelopes, noise,
    /// and partials all still apply. Referenced as waveform "wavetable:{id}".
    ///
    /// SAMPLES — full one-shot clips (an earcon chime, a recorded sound). Played
    /// once at natural speed, resampled to the engine rate. No pitch mapping.
    /// Referenced as waveform "sample:{id}".
    ///
    /// Static by design: AudioEngine instances are constructed freely (one per host
    /// driver, many in tests) and have no DI; material registered here is immutable
    /// float data resolved at SetVoice (command-enqueue) time, so the audio callback
    /// thread never touches these dictionaries.
    /// </summary>
    public static class WavetableBank
    {
        public const string WavetablePrefix = "wavetable:";
        public const string SamplePrefix = "sample:";

        /// <summary>Wavetables longer than this are rejected (a single cycle never needs more).</summary>
        public const int MaxWavetableLength = 65536;
        /// <summary>Samples longer than this are truncated on registration (~10 s @ 44.1 kHz).</summary>
        public const int MaxSampleLength = 441_000;

        private static readonly ConcurrentDictionary<string, float[]> _tables =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, (float[] Data, int Rate)> _samples =
            new(StringComparer.OrdinalIgnoreCase);

        public static void RegisterWavetable(string id, float[] singleCycle)
        {
            if (string.IsNullOrWhiteSpace(id) || singleCycle == null) return;
            if (singleCycle.Length < 8 || singleCycle.Length > MaxWavetableLength) return;
            _tables[id] = singleCycle;
        }

        public static void RegisterSample(string id, float[] data, int sampleRate)
        {
            if (string.IsNullOrWhiteSpace(id) || data == null || data.Length == 0 || sampleRate <= 0) return;
            if (data.Length > MaxSampleLength) data = data.Take(MaxSampleLength).ToArray();
            _samples[id] = (data, sampleRate);
        }

        public static bool TryGetWavetable(string id, out float[] table) => _tables.TryGetValue(id, out table!);
        public static bool TryGetSample(string id, out float[] data, out int rate)
        {
            if (_samples.TryGetValue(id, out var s)) { data = s.Data; rate = s.Rate; return true; }
            data = Array.Empty<float>(); rate = 0; return false;
        }

        public static void Remove(string id)
        {
            _tables.TryRemove(id, out _);
            _samples.TryRemove(id, out _);
        }

        public static IReadOnlyList<string> WavetableIds => _tables.Keys.OrderBy(k => k).ToList();
        public static IReadOnlyList<string> SampleIds => _samples.Keys.OrderBy(k => k).ToList();
    }
}
