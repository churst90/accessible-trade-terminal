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
    }

    internal class OscillatorVoice
    {
        public int Slot { get; set; }
        public double Phase { get; set; }

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
        private const int GLIDE_SAMPLES = 220; 
        private const int ENVELOPE_SAMPLES = 220;
        private const int FADE_SAMPLES = 882;

        private float _masterGain = 1.0f;
        private float _targetMasterGain = 1.0f;

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
            _targetMasterGain = Math.Clamp(gain, 0.0f, 1.0f);
        }

        public void SetVoice(int slot, double freq, float vol, float pan, string wave, bool continuous, double durationSec, int dataIndex = -1, string envelope = "Sustain", bool click = false, float noiseAmount = 0f, string noiseType = "pink")
        {
            if (slot < 0 || slot >= _voices.Length) return;

            // Voice-slot pooling: OscillatorVoice instances are allocated once in the ctor
            // (permanent 64-element array); VoiceCommand is a struct value type so no heap
            // allocation per command. The only remaining per-call allocation in the old
            // implementation was `wave.ToLower()` — cut by using OrdinalIgnoreCase compares
            // so the hot path through SetVoice now allocates zero bytes.
            var waveType = ParseWaveform(wave);

            EnqueueCommand(new VoiceCommand
            {
                Slot = slot, Frequency = freq, Volume = vol, Pan = pan,
                Waveform = waveType, IsContinuous = continuous,
                EnvelopeType = envelope, TriggerClick = click,
                DurationSamples = durationSec * _sampleRate,
                DataIndex = dataIndex, IsActive = true,
                NoiseAmount = Math.Max(0f, noiseAmount),
                NoiseType = noiseType ?? "pink"
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
            return WaveformType.Sine;
        }

        public void StopVoice(int slot)
        {
            if (slot < 0 || slot >= _voices.Length) return;
            EnqueueCommand(new VoiceCommand { Slot = slot, IsActive = false });
        }

        public void Reset()
        {
            // Route all mutations through the ring buffer so the audio callback thread
            // is the sole writer to _voices[].  Direct writes here would race with Read()
            // on the WASAPI callback thread and can produce clicks or corrupted state.
            StopAll();
            // Master gain is written only from the main thread and read only in Read();
            // both are single-word aligned floats, so a torn read is impossible on x86/x64.
            _targetMasterGain = 0;
            _masterGain = 0;
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
            if (stopAllRequested) _targetMasterGain = 0.0f;

            // 2. APPLY EFFECTIVE COMMANDS
            if (anyPending)
            {
                for (int i = 0; i < MaxVoices; i++)
                {
                    if (!_pendingSet[i]) continue;

                    var cmd = _pendingCommands[i];
                    if (cmd.IsActive && _targetMasterGain == 0.0f) _targetMasterGain = 1.0f;

                    var voice = _voices[i];
                    if (!cmd.IsActive)
                    {
                        voice.IsActive = false;
                        voice.TargetVolume = 0;
                        voice.Continuous = false;
                        voice.Phase = 0;
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
                    
                    if (cmd.TriggerClick || !voice.IsActive)
                    {
                        voice.Phase = 0;
                        voice.CurrentFrequency = cmd.Frequency;
                        voice.CurrentPan = cmd.Pan;
                        voice.CurrentVolume = cmd.Volume;
                    }
                    // else: active voice — current values are the glide start point; the
                    // per-frame exponential convergence in Read() handles the smooth transition.

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
                    
                    float sample = v.Waveform switch
                    {
                        WaveformType.Sine     => (float)Math.Sin(v.Phase),
                        WaveformType.Square   => v.Phase < Math.PI ? 1.0f : -1.0f,
                        WaveformType.Sawtooth => (float)(2.0 * (v.Phase / (2.0 * Math.PI)) - 1.0),
                        WaveformType.Triangle => (float)(Math.Abs((v.Phase / Math.PI) % 2 - 1) * 2 - 1),
                        WaveformType.Noise    => 0.0f, // Pure noise: base sample is 0; noise path below provides signal
                        _                     => (float)Math.Sin(v.Phase)
                    };

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
                            // Two-stage low-pass for deeper, warmer texture.
                            v.NoiseState  = 0.99f * v.NoiseState  + 0.01f * white;
                            v.NoiseState2 = 0.99f * v.NoiseState2 + 0.01f * v.NoiseState;
                            noiseSignal = v.NoiseState2;
                        }
                        else
                        {
                            // pink: one-pole filtered
                            v.NoiseState = 0.997f * v.NoiseState + 0.003f * white;
                            noiseSignal = v.NoiseState;
                        }
                        float noiseAmt = (v.Waveform == WaveformType.Noise) ? 1.0f : v.NoiseAmount;
                        // Additive blend: oscillator stays at full amplitude; noise is layered on top.
                        sample = sample + noiseAmt * noiseSignal;
                    }

                    // Panning (Linear)
                    float p = (Math.Clamp(v.CurrentPan, -1.0f, 1.0f) + 1.0f) / 2.0f;
                    leftSum += sample * renderVolume * (1.0f - p);
                    rightSum += sample * renderVolume * p;

                    v.Phase += 2.0 * Math.PI * v.CurrentFrequency / _sampleRate;
                    if (v.Phase >= 2.0 * Math.PI) v.Phase -= 2.0 * Math.PI;

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
