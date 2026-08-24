using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Audio
{
    public enum WaveformType
    {
        Sine,
        Square,
        Sawtooth,
        Triangle,
        /// <summary>Pink noise (one-pole filtered white noise). NoiseAmount blends it into the base waveform.</summary>
        Noise,
        /// <summary>User single-cycle table (see <see cref="WavetableBank"/>) looped at pitch — a custom oscillator shape.</summary>
        Wavetable,
        /// <summary>User one-shot clip (see <see cref="WavetableBank"/>) played once at natural speed. No pitch mapping.</summary>
        Sample,
    }

    internal partial struct VoiceCommand
    {
        public int Slot;
        public double Frequency;
        public float Volume;
        public float Pan;
        public WaveformType Waveform;
        public string EnvelopeType;
        public bool TriggerClick;
        public bool IsContinuous;
        public double DurationSamples;
        public int DataIndex;
        public bool IsActive;
        public bool IsStopAll;
        /// <summary>Additive noise amount [0,∞]. 0 = no noise texture.</summary>
        public float NoiseAmount;
        /// <summary>Noise colour: "white" (raw), "pink" (one-pole filtered), "brown" (heavier filtered).</summary>
        public string NoiseType;
        /// <summary>Additive square-wave partial amount [0,∞] mixed into the base waveform. 0 = none.</summary>
        public float SquareMix;
        /// <summary>Additive sawtooth partial amount [0,∞] mixed into the base waveform. 0 = none.</summary>
        public float SawMix;
        /// <summary>Additive triangle partial amount [0,∞] mixed into the base waveform. 0 = none.</summary>
        public float TriangleMix;
        /// <summary>Additive sub-octave sawtooth partial amount [0,∞] (one octave down). 0 = none.</summary>
        public float SubSawMix;
        /// <summary>Single-cycle table for Wavetable voices — resolved from WavetableBank at
        /// SetVoice time so the audio thread never touches the registry.</summary>
        public float[]? Wavetable;
        /// <summary>Clip data for Sample voices, resolved at SetVoice time.</summary>
        public float[]? SampleData;
        /// <summary>Playback step for Sample voices: sourceRate / engineRate.</summary>
        public double SampleStep;
    }

    internal class OscillatorVoice
    {
        public int Slot { get; set; }
        public double Phase { get; set; }
        /// <summary>Independent phase accumulator for the sub-octave sawtooth partial
        /// (advances at half <see cref="CurrentFrequency"/> → one octave down).</summary>
        public double SubPhase { get; set; }

        public double TargetFrequency { get; set; }
        public float TargetVolume { get; set; }
        public float TargetPan { get; set; }

        public double CurrentFrequency { get; set; }
        public float CurrentVolume { get; set; }
        public float CurrentPan { get; set; }

        public WaveformType Waveform { get; set; } = WaveformType.Sine;

        public string EnvelopeType { get; set; } = "Sustain";
        public bool TriggerClick { get; set; }
        public bool IsActive { get; set; }
        public bool Continuous { get; set; }
        public double RemainingSamples { get; set; }
        public double TotalDurationSamples { get; set; }
        public int DataIndex { get; set; } = -1;

        /// <summary>Additive noise amount. Applied per-sample in <c>Read()</c>.</summary>
        public float NoiseAmount { get; set; }
        /// <summary>Noise colour: "white", "pink", or "brown".</summary>
        public string NoiseType { get; set; } = "pink";
        /// <summary>One-pole filter state for pink noise generation.</summary>
        public float NoiseState { get; set; }
        /// <summary>Two-pole filter state for brown noise generation.</summary>
        public float NoiseState2 { get; set; }

        /// <summary>Additive square-wave partial amount mixed into the base waveform (normalized blend).</summary>
        public float SquareMix { get; set; }
        /// <summary>Additive sawtooth partial amount mixed into the base waveform (normalized blend).</summary>
        public float SawMix { get; set; }
        /// <summary>Additive triangle partial amount mixed into the base waveform (normalized blend).</summary>
        public float TriangleMix { get; set; }
        /// <summary>Additive sub-octave sawtooth partial amount mixed into the base waveform (normalized blend).</summary>
        public float SubSawMix { get; set; }

        /// <summary>Per-voice attack/release fade gain [0,1]. Ramps 0→1 on (re)start and
        /// 1→0 on release over FADE_ENV_SAMPLES so onsets/offsets never snap (declick).</summary>
        public float FadeGain { get; set; }
        /// <summary>True while the voice is fading out toward deactivation.</summary>
        public bool Releasing { get; set; }

        /// <summary>Single-cycle table for Wavetable voices (null otherwise).</summary>
        public float[]? Wavetable { get; set; }
        /// <summary>Clip data for Sample voices (null otherwise).</summary>
        public float[]? SampleData { get; set; }
        /// <summary>Read position into <see cref="SampleData"/>, in source samples.</summary>
        public double SamplePos { get; set; }
        /// <summary>Position advance per engine frame (sourceRate / engineRate).</summary>
        public double SampleStep { get; set; }
    }

    public class AudioEngine
    {
        /// <summary>Total polyphony. Slot layout (see AudioSequencer/NavigationSonifier):
        /// 0-15 navigation, 16-31 UI earcons, 32-95 playback, 96-127 cloud fills.</summary>
        public const int MaxVoices = 128;

        private readonly OscillatorVoice[] _voices = new OscillatorVoice[MaxVoices];
        private readonly VoiceCommand[] _pendingCommands = new VoiceCommand[MaxVoices];
        // Which slots received a command this buffer. Replaces the old 64-bit mask, which
        // structurally capped polyphony at 64 voices (1UL << slot wraps past slot 63).
        private readonly bool[] _pendingSet = new bool[MaxVoices];

        // --- HARD REAL-TIME: LOCK-FREE RING BUFFER IMPLEMENTATION ---
        private struct RingBuffer<T> where T : struct
        {
            private readonly T[] _buffer;
            private readonly int _mask;
            private int _head;
            private int _tail;

            public RingBuffer(int capacity)
            {
                int pow2 = 1; while (pow2 < capacity) pow2 <<= 1;
                _buffer = new T[pow2];
                _mask = pow2 - 1;
                _head = 0; _tail = 0;
            }

            public bool Enqueue(T item)
            {
                int nextHead = (_head + 1) & _mask;
                if (nextHead == Volatile.Read(ref _tail)) return false; // Full
                _buffer[_head] = item;
                Volatile.Write(ref _head, nextHead);
                return true;
            }

            public bool TryDequeue(out T item)
            {
                int tail = _tail;
                if (tail == Volatile.Read(ref _head)) { item = default; return false; } // Empty
                item = _buffer[tail];
                Volatile.Write(ref _tail, (tail + 1) & _mask);
                return true;
            }

            public int Count => (_head - _tail) & _mask;
        }

        private RingBuffer<VoiceCommand> _commandQueue = new(1024);
        private RingBuffer<int> _eventQueue = new(1024);

        // ── Overflow telemetry ────────────────────────────────────────────────────
        // When the command ring buffer is full, Enqueue silently drops the command
        // (the only real-time-safe behaviour — we cannot allocate or block). Tracking
        // how often that happens is the difference between "I trust 1024 is enough"
        // and "I know 1024 is enough." Incremented on every dropped command;
        // readable via DroppedCommandCount. Reset via ResetTelemetry (JournalModal
        // exposes this counter so a user can ask "any audio drops this session?").
        private long _droppedCommandCount;
        private long _totalCommandCount;

        /// <summary>Total number of voice commands dropped because the ring buffer was full since the last <see cref="ResetTelemetry"/> or process start.</summary>
        public long DroppedCommandCount => Interlocked.Read(ref _droppedCommandCount);

        /// <summary>Total number of voice commands attempted (successful + dropped) since the last <see cref="ResetTelemetry"/> or process start.</summary>
        public long TotalCommandCount => Interlocked.Read(ref _totalCommandCount);

        /// <summary>Fires whenever a command is dropped. Payload is the running total of dropped commands after the increment. Subscribers can batch / rate-limit.</summary>
        public event Action<long>? CommandDropped;

        /// <summary>Clears the drop and total counters. Useful for per-session telemetry windows.</summary>
        public void ResetTelemetry()
        {
            Interlocked.Exchange(ref _droppedCommandCount, 0);
            Interlocked.Exchange(ref _totalCommandCount, 0);
        }

        // Used only on the audio callback thread (inside Read()) — no cross-thread access.
        private readonly Random _rng = new();

        private int _sampleRate = 44100;
        // Per-voice attack/release fade length (~12 ms @44.1k). Applied to EVERY voice —
        // including continuous playback voices — so note onsets/offsets ramp instead of
        // snapping, which removes the inter-note clicks heard at slow playback speeds.
        private const int FADE_ENV_SAMPLES = 530;
        private const int ENVELOPE_SAMPLES = 530;
        private const int FADE_SAMPLES = 882;

        private float _masterGain = 1.0f;
        private float _targetMasterGain = 1.0f;

        // The gain the USER (or the app on their behalf) last asked for, kept apart from the
        // stop-all fade. Before this split, Read() re-armed a faded-out master by snapping the
        // target to a hardcoded 1.0f whenever any voice command arrived — and it could not tell
        // "StopAll just faded us to zero" from "the user set the volume to zero". Setting the
        // volume to 0% and pressing an arrow key therefore restored FULL output, so a mute was
        // never a mute: order-fill, stop-hit and boundary earcons pass fixed literal volumes and
        // would fire at full scale on a master that had been silenced deliberately.
        //
        // _stopAllFaded is the flag the old test could not express: true only while the zero was
        // OURS. The re-arm restores _userMasterGain, never a literal — so a user-chosen zero
        // survives every subsequent command.
        private float _userMasterGain = 1.0f;
        private volatile bool _stopAllFaded;

        public int SampleRate => _sampleRate;
        public int Channels => 2;

        public event Action<int>? PointReached;

        public AudioEngine()
        {
            for (int i = 0; i < _voices.Length; i++) _voices[i] = new OscillatorVoice { Slot = i };
        }

        public void UpdateSampleRate(int rate) => _sampleRate = rate;

        public void SetMasterGain(float gain)
        {
            float clamped = Math.Clamp(gain, 0.0f, 1.0f);
            _userMasterGain = clamped;
            _targetMasterGain = clamped;
            // An explicit request supersedes any in-flight stop-all fade: whatever the caller
            // just asked for IS the user's value now, including zero.
            _stopAllFaded = false;
        }

        public void SetVoice(int slot, double freq, float vol, float pan, string wave, bool continuous, double durationSec, int dataIndex = -1, string envelope = "Sustain", bool click = false, float noiseAmount = 0f, string noiseType = "pink", float squareMix = 0f, float sawMix = 0f, float triangleMix = 0f, float subSawMix = 0f)
        {
            if (slot < 0 || slot >= _voices.Length) return;

            // Voice-slot pooling: OscillatorVoice instances are allocated once in the ctor
            // (permanent MaxVoices-element array); VoiceCommand is a struct value type so no heap
            // allocation per command. The only remaining per-call allocation in the old
            // implementation was `wave.ToLower()` — cut by using OrdinalIgnoreCase compares
            // so the hot path through SetVoice now allocates zero bytes.
            var waveType = ParseWaveform(wave);

            // User material ("wavetable:{id}" / "sample:{id}"): resolve the immutable
            // float arrays HERE, on the caller's thread, so the audio callback never
            // touches the WavetableBank dictionaries. Unknown ids fall back to sine —
            // audible, never silent (a missing table must not mute an alert cue).
            float[]? wavetable = null;
            float[]? sampleData = null;
            double sampleStep = 0;
            if (waveType == WaveformType.Wavetable)
            {
                if (!WavetableBank.TryGetWavetable(wave.Substring(WavetableBank.WavetablePrefix.Length), out wavetable!))
                    waveType = WaveformType.Sine;
            }
            else if (waveType == WaveformType.Sample)
            {
                if (WavetableBank.TryGetSample(wave.Substring(WavetableBank.SamplePrefix.Length), out sampleData!, out int srcRate))
                    sampleStep = (double)srcRate / _sampleRate;
                else
                    waveType = WaveformType.Sine;
            }

            EnqueueCommand(new VoiceCommand
            {
                Slot = slot, Frequency = freq, Volume = vol, Pan = pan,
                Waveform = waveType, IsContinuous = continuous,
                Wavetable = wavetable, SampleData = sampleData, SampleStep = sampleStep,
                EnvelopeType = envelope, TriggerClick = click,
                DurationSamples = durationSec * _sampleRate,
                DataIndex = dataIndex, IsActive = true,
                NoiseAmount = Math.Max(0f, noiseAmount),
                NoiseType = noiseType ?? "pink",
                SquareMix = Math.Max(0f, squareMix),
                SawMix = Math.Max(0f, sawMix),
                TriangleMix = Math.Max(0f, triangleMix),
                SubSawMix = Math.Max(0f, subSawMix)
            });
        }

        /// <summary>Case-insensitive waveform parse without allocating a lowercase copy.
        /// SetVoice fires at ~300 calls/sec in the 5-pane playback path; <c>.ToLower()</c>
        /// was allocating a string per call before this was extracted.</summary>
        private static WaveformType ParseWaveform(string wave)
        {
            if (string.IsNullOrEmpty(wave)) return WaveformType.Sine;
            if (wave.Equals("square",   System.StringComparison.OrdinalIgnoreCase)) return WaveformType.Square;
            if (wave.Equals("sawtooth", System.StringComparison.OrdinalIgnoreCase)) return WaveformType.Sawtooth;
            if (wave.Equals("saw",      System.StringComparison.OrdinalIgnoreCase)) return WaveformType.Sawtooth;
            if (wave.Equals("triangle", System.StringComparison.OrdinalIgnoreCase)) return WaveformType.Triangle;
            if (wave.Equals("noise",    System.StringComparison.OrdinalIgnoreCase)) return WaveformType.Noise;
            if (wave.StartsWith(WavetableBank.WavetablePrefix, System.StringComparison.OrdinalIgnoreCase)) return WaveformType.Wavetable;
            if (wave.StartsWith(WavetableBank.SamplePrefix,    System.StringComparison.OrdinalIgnoreCase)) return WaveformType.Sample;
            return WaveformType.Sine;
        }

        public void StopVoice(int slot)
        {
            if (slot < 0 || slot >= _voices.Length) return;
            EnqueueCommand(new VoiceCommand { Slot = slot, IsActive = false });
        }

        /// <summary>Linear-interpolated read of one cycle of a user wavetable at the voice's phase.</summary>
        private static float ReadWavetable(OscillatorVoice v)
        {
            var t = v.Wavetable;
            if (t == null || t.Length == 0) return (float)Math.Sin(v.Phase);
            double idx = v.Phase / (2.0 * Math.PI) * t.Length;
            int i0 = (int)idx;
            double frac = idx - i0;
            if (i0 >= t.Length) i0 -= t.Length;
            int i1 = i0 + 1 >= t.Length ? 0 : i0 + 1;
            return (float)(t[i0] * (1.0 - frac) + t[i1] * frac);
        }

        /// <summary>Linear-interpolated read of a one-shot clip at the voice's sample position.
        /// Past the end returns silence while the release fade completes.</summary>
        private static float ReadSampleClip(OscillatorVoice v)
        {
            var d = v.SampleData;
            if (d == null || d.Length == 0) return 0f;
            double pos = v.SamplePos;
            if (pos >= d.Length - 1) return 0f;
            int i0 = (int)pos;
            double frac = pos - i0;
            return (float)(d[i0] * (1.0 - frac) + d[i0 + 1] * frac);
        }

        public void Reset()
        {
            // Route all mutations through the ring buffer so the audio callback thread
            // is the sole writer to _voices[].  Direct writes here would race with Read()
            // on the WASAPI callback thread and can produce clicks or corrupted state.
            StopAll();
            // Both gains are single-word aligned floats, so a torn read is impossible on
            // x86/x64 even though Read() writes them too (the fade ramp and the re-arm below).
            // The zero written here is OURS, not the user's — flag it so the next voice command
            // restores _userMasterGain rather than treating a deliberate mute as a stale fade.
            _stopAllFaded = true;
            // Only the TARGET goes to zero — Read()'s ramp walks _masterGain down over the
            // fade window. Snapping _masterGain itself here is an audible click.
            _targetMasterGain = 0;
        }

        public void StopAll()
        {
            // Enqueue a stop-all command; Read() applies it at the top of the next buffer.
            // Do NOT directly write _voices[i].IsActive here — that is a data race with the
            // audio callback thread that reads the same array inside Read().
            EnqueueCommand(new VoiceCommand { IsStopAll = true });
        }

        private void EnqueueCommand(VoiceCommand cmd)
        {
            // If the buffer is full, we must drop the command to maintain real-time safety.
            // In a trading context, we prefer dropping a single stale frequency update
            // over causing a heap allocation or blocking the UI thread. Increment the
            // telemetry counter so callers can see how often this is happening and
            // decide whether to enlarge the buffer or reduce sonification density.
            Interlocked.Increment(ref _totalCommandCount);
            if (!_commandQueue.Enqueue(cmd))
            {
                long droppedTotal = Interlocked.Increment(ref _droppedCommandCount);
                CommandDropped?.Invoke(droppedTotal);
            }
        }

        public void ProcessEvents()
        {
            while (_eventQueue.TryDequeue(out int index))
            {
                PointReached?.Invoke(index);
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            // 1. SQUELCH COMMANDS: Use the fixed-size pending buffer instead of a Dictionary.
            bool stopAllRequested = false;
            bool anyPending = false;
            Array.Clear(_pendingSet, 0, MaxVoices);

            while (_commandQueue.TryDequeue(out var cmd))
            {
                if (cmd.IsStopAll)
                {
                    stopAllRequested = true;
                    Array.Clear(_pendingSet, 0, MaxVoices);
                    anyPending = false;
                    continue;
                }
                _pendingCommands[cmd.Slot] = cmd;
                _pendingSet[cmd.Slot] = true;
                anyPending = true;
            }

            // When stop-all is requested, fade master gain to zero.  The per-frame master-gain
            // loop below deactivates all voices once gain reaches 0.0f — that is the ONLY safe
            // write path to _voices[].IsActive, because it executes on this (audio callback) thread.
            if (stopAllRequested)
            {
                _targetMasterGain = 0.0f;
                _stopAllFaded = true;
            }

            // 2. APPLY EFFECTIVE COMMANDS
            if (anyPending)
            {
                for (int i = 0; i < MaxVoices; i++)
                {
                    if (!_pendingSet[i]) continue;

                    var cmd = _pendingCommands[i];
                    // Re-arm after a stop-all fade — and ONLY after one. A zero the user chose
                    // is not a condition to recover from, so it is left exactly where it is.
                    if (cmd.IsActive && _stopAllFaded)
                    {
                        _targetMasterGain = _userMasterGain;
                        _stopAllFaded = false;
                    }

                    var voice = _voices[i];
                    if (!cmd.IsActive)
                    {
                        // Begin a short release fade instead of snapping to silence (declick).
                        // The per-frame loop ramps FadeGain→0, then deactivates the voice.
                        voice.Releasing = true;
                        voice.Continuous = false;
                        continue;
                    }

                    voice.TargetFrequency = cmd.Frequency;
                    voice.TargetVolume = cmd.Volume;
                    voice.TargetPan = cmd.Pan;
                    voice.Waveform = cmd.Waveform;
                    voice.Continuous = cmd.IsContinuous;
                    voice.TotalDurationSamples = cmd.DurationSamples;
                    voice.RemainingSamples = cmd.DurationSamples;
                    voice.DataIndex = cmd.DataIndex;
                    voice.EnvelopeType = cmd.EnvelopeType;
                    voice.NoiseAmount = cmd.NoiseAmount;
                    voice.NoiseType = cmd.NoiseType ?? "pink";
                    voice.SquareMix = cmd.SquareMix;
                    voice.SawMix = cmd.SawMix;
                    voice.TriangleMix = cmd.TriangleMix;
                    voice.SubSawMix = cmd.SubSawMix;
                    voice.Wavetable = cmd.Wavetable;
                    voice.SampleData = cmd.SampleData;
                    voice.SampleStep = cmd.SampleStep;

                    if (cmd.TriggerClick || !voice.IsActive)
                    {
                        voice.Phase = 0;
                        voice.SubPhase = 0;
                        voice.SamplePos = 0;
                        voice.CurrentFrequency = cmd.Frequency;
                        voice.CurrentPan = cmd.Pan;
                        voice.CurrentVolume = cmd.Volume;
                        voice.FadeGain = 0f;   // fade in from silence → no onset click
                    }
                    else if (voice.Waveform == WaveformType.Sample)
                    {
                        // A re-triggered one-shot always restarts from the top.
                        voice.SamplePos = 0;
                    }
                    // else: active voice — current values are the glide start point; the
                    // per-frame exponential convergence in Read() handles the smooth transition.

                    voice.Releasing = false;   // (re)activated — cancel any pending release fade
                    voice.IsActive = true;
                }
            }

            // Process in stereo frames (2 samples per frame) to avoid odd-count stereo artifacts
            int frameCount = count / 2;
            int samplesRead = 0;
            const float GLIDE_FACTOR = 0.05f; // Faster convergence for more reactive sound

            for (int frame = 0; frame < frameCount; frame++)
            {
                if (_masterGain != _targetMasterGain)
                {
                    float step = 1.0f / FADE_SAMPLES;
                    if (_targetMasterGain > _masterGain) _masterGain = Math.Min(_targetMasterGain, _masterGain + step);
                    else _masterGain = Math.Max(_targetMasterGain, _masterGain - step);

                    if (_masterGain == 0.0f)
                    {
                        foreach (var v in _voices) v.IsActive = false;
                    }
                }

                float leftSum = 0;
                float rightSum = 0;

                for (int i = 0; i < _voices.Length; i++)
                {
                    var v = _voices[i];
                    if (!v.IsActive) continue;

                    // Per-voice attack/release fade — applies to ALL voices incl. continuous,
                    // so onsets/offsets ramp over ~12 ms instead of snapping (declick).
                    const float FADE_STEP = 1f / FADE_ENV_SAMPLES;
                    if (v.Releasing)
                    {
                        v.FadeGain -= FADE_STEP;
                        if (v.FadeGain <= 0f) { v.FadeGain = 0f; v.IsActive = false; v.Releasing = false; continue; }
                    }
                    else if (v.FadeGain < 1f)
                    {
                        v.FadeGain = Math.Min(1f, v.FadeGain + FADE_STEP);
                    }

                    // GLIDE: Exponential convergence to target values
                    v.CurrentFrequency += (v.TargetFrequency - v.CurrentFrequency) * GLIDE_FACTOR;
                    v.CurrentPan += (v.TargetPan - v.CurrentPan) * GLIDE_FACTOR;
                    v.CurrentVolume += (v.TargetVolume - v.CurrentVolume) * GLIDE_FACTOR;

                    float renderVolume = v.CurrentVolume;
                    if (v.EnvelopeType == "Ping")
                    {
                        double progress = 1.0 - (v.RemainingSamples / v.TotalDurationSamples);
                        renderVolume = (float)(v.TargetVolume * Math.Exp(-5.0 * progress));
                    }
                    else if (!v.Continuous)
                    {
                        double samplesFromStart = v.TotalDurationSamples - v.RemainingSamples;
                        if (samplesFromStart < ENVELOPE_SAMPLES) 
                            renderVolume = (float)(v.TargetVolume * (samplesFromStart / ENVELOPE_SAMPLES));
                        else if (v.RemainingSamples < ENVELOPE_SAMPLES)
                            renderVolume = (float)(v.TargetVolume * (v.RemainingSamples / ENVELOPE_SAMPLES));
                    }

                    // Apply the per-voice attack/release fade on top of any note envelope.
                    renderVolume *= v.FadeGain;

                    float baseSample = v.Waveform switch
                    {
                        WaveformType.Sine     => (float)Math.Sin(v.Phase),
                        WaveformType.Square   => v.Phase < Math.PI ? 1.0f : -1.0f,
                        WaveformType.Sawtooth => (float)(2.0 * (v.Phase / (2.0 * Math.PI)) - 1.0),
                        WaveformType.Triangle => (float)(Math.Abs((v.Phase / Math.PI) % 2 - 1) * 2 - 1),
                        WaveformType.Noise    => 0.0f, // Pure noise: base sample is 0; noise path below provides signal
                        // Wavetable: the phase accumulator indexes one cycle of the user table
                        // (linear interpolation), so pitch, glide, envelopes, partials, and
                        // noise all behave exactly as for the built-in shapes.
                        WaveformType.Wavetable => ReadWavetable(v),
                        // Sample: one-shot clip at natural speed (resampled to engine rate).
                        WaveformType.Sample    => ReadSampleClip(v),
                        _                     => (float)Math.Sin(v.Phase)
                    };

                    // Additive partials: mix square/saw shapes into the base (usually sine) so a
                    // single voice can be "mostly sine with a touch of grit". Normalized weighted
                    // sum keeps peak amplitude bounded regardless of the mix amounts.
                    float sample = baseSample;
                    if (v.SquareMix > 0f || v.SawMix > 0f || v.TriangleMix > 0f || v.SubSawMix > 0f)
                    {
                        float squareSample = v.Phase < Math.PI ? 1.0f : -1.0f;
                        float sawSample = (float)(2.0 * (v.Phase / (2.0 * Math.PI)) - 1.0);
                        float triSample = (float)(Math.Abs((v.Phase / Math.PI) % 2 - 1) * 2 - 1);
                        float subSawSample = (float)(2.0 * (v.SubPhase / (2.0 * Math.PI)) - 1.0);
                        sample = (baseSample
                                  + v.SquareMix * squareSample
                                  + v.TriangleMix * triSample
                                  + v.SawMix * sawSample
                                  + v.SubSawMix * subSawSample)
                                 / (1f + v.SquareMix + v.TriangleMix + v.SawMix + v.SubSawMix);
                    }

                    // Noise texturing: additive noise layered on top of the oscillator.
                    // "white" = raw white noise; "pink" = one-pole filtered; "brown" = heavier filtered.
                    // Runs on audio callback thread only (_rng is not shared).
                    if (v.NoiseAmount > 0f || v.Waveform == WaveformType.Noise)
                    {
                        float white = (float)(_rng.NextDouble() * 2.0 - 1.0);
                        float noiseSignal;
                        string nType = v.NoiseType ?? "pink";
                        if (v.Waveform == WaveformType.Noise || nType == "white")
                        {
                            noiseSignal = white;
                        }
                        else if (nType == "brown")
                        {
                            // Two-stage low-pass for deeper, warmer texture. The cascade
                            // attenuates the signal ~25 dB, so a makeup gain restores it to
                            // roughly white-noise loudness — without it NoiseAmount 0.3 was
                            // ~0.01 effective and the OB/OS zone texture was inaudible.
                            v.NoiseState  = 0.99f * v.NoiseState  + 0.01f * white;
                            v.NoiseState2 = 0.99f * v.NoiseState2 + 0.01f * v.NoiseState;
                            noiseSignal = Math.Clamp(v.NoiseState2 * 14f, -1f, 1f);
                        }
                        else
                        {
                            // pink: one-pole filtered (~28 dB down) + makeup gain, same
                            // rationale as brown — NoiseAmount means the same loudness for
                            // white, pink, and brown.
                            v.NoiseState = 0.997f * v.NoiseState + 0.003f * white;
                            noiseSignal = Math.Clamp(v.NoiseState * 18f, -1f, 1f);
                        }
                        float noiseAmt = (v.Waveform == WaveformType.Noise) ? 1.0f : v.NoiseAmount;
                        // Additive blend: oscillator stays at full amplitude; noise is layered on top.
                        sample = sample + noiseAmt * noiseSignal;
                    }

                    // Panning (equal-power): constant perceived loudness as the sound sweeps
                    // left→right, so the time axis doesn't dip ~6 dB through the centre.
                    float p = (Math.Clamp(v.CurrentPan, -1.0f, 1.0f) + 1.0f) / 2.0f;
                    double panAngle = p * (Math.PI / 2.0);
                    leftSum += sample * renderVolume * (float)Math.Cos(panAngle);
                    rightSum += sample * renderVolume * (float)Math.Sin(panAngle);

                    v.Phase += 2.0 * Math.PI * v.CurrentFrequency / _sampleRate;
                    if (v.Phase >= 2.0 * Math.PI) v.Phase -= 2.0 * Math.PI;

                    // One-shot sample advance: natural speed (source rate / engine rate).
                    // Start the declick release just before the clip end so the tail never snaps.
                    if (v.Waveform == WaveformType.Sample && v.SampleData != null)
                    {
                        v.SamplePos += v.SampleStep;
                        if (!v.Releasing && v.SamplePos >= v.SampleData.Length - FADE_ENV_SAMPLES * v.SampleStep)
                            v.Releasing = true;
                    }

                    // Sub-octave sawtooth phase: advances at half frequency (one octave down).
                    v.SubPhase += Math.PI * v.CurrentFrequency / _sampleRate;
                    if (v.SubPhase >= 2.0 * Math.PI) v.SubPhase -= 2.0 * Math.PI;

                    if (!v.Continuous)
                    {
                        v.RemainingSamples--;
                        if (v.RemainingSamples <= 0) v.IsActive = false;
                    }
                    
                    if (v.DataIndex != -1 && samplesRead == 0)
                    {
                         _eventQueue.Enqueue(v.DataIndex);
                    }
                }

                buffer[offset + samplesRead] = leftSum * _masterGain;
                samplesRead++;
                buffer[offset + samplesRead] = rightSum * _masterGain;
                samplesRead++;
            }

            return samplesRead;
        }
    }
}
