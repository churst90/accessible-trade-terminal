using System;
using System.Collections.Generic;
using System.Linq;

namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// A single oscillator inside a <see cref="SoundPatch"/>. A patch can layer several of
    /// these to build richer timbres (e.g. a sine + triangle bell, or a tone + noise transient).
    /// Each layer plays as its own engine voice, mixed by <see cref="Gain"/>.
    /// </summary>
    public class OscillatorLayer
    {
        /// <summary>Waveform name: "sine", "square", "sawtooth", "triangle", "noise".</summary>
        public string Waveform { get; set; } = "sine";

        /// <summary>Mix level for this layer [0–1]. Layers are additive; keep the sum modest to avoid clipping.</summary>
        public float Gain { get; set; } = 1.0f;

        /// <summary>Frequency multiple of the patch's resolved frequency. 1.0 = fundamental, 2.0 = octave, 0.5 = sub-octave.</summary>
        public double FreqRatio { get; set; } = 1.0;

        /// <summary>Blend of noise into this layer [0–1]. 0 = pure waveform.</summary>
        public float NoiseAmount { get; set; } = 0f;

        /// <summary>Noise colour for this layer: "white", "pink", or "brown".</summary>
        public string NoiseType { get; set; } = "pink";

        public OscillatorLayer Clone() => new()
        {
            Waveform = Waveform,
            Gain = Gain,
            FreqRatio = FreqRatio,
            NoiseAmount = NoiseAmount,
            NoiseType = NoiseType,
        };
    }

    /// <summary>
    /// A named, serializable audio preset that can be assigned to any
    /// <see cref="ComponentConfig"/> or earcon feedback type.
    /// </summary>
    public class SoundPatch
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "New Patch";

        // ── Oscillator ────────────────────────────────────────────────────────
        /// <summary>
        /// Layered oscillators. When non-empty this is the source of truth for the timbre;
        /// when empty the legacy single-oscillator <see cref="Waveform"/> / <see cref="NoiseAmount"/>
        /// fields are used (see <see cref="EffectiveLayers"/>). New patches populate this list.
        /// </summary>
        public List<OscillatorLayer> Oscillators { get; set; } = new();

        /// <summary>Legacy single-oscillator waveform. Retained for back-compat and as the first-layer mirror.</summary>
        public string Waveform { get; set; } = "sine";

        /// <summary>Legacy single-oscillator noise blend [0–1].</summary>
        public float NoiseAmount { get; set; } = 0f;

        /// <summary>Base frequency in Hz. Sonification mapping multiplies this by pitch ratio.</summary>
        public double BaseFrequency { get; set; } = 440.0;

        /// <summary>Multiplier applied to the resolved frequency. 1.0 = no change.</summary>
        public double FreqMultiplier { get; set; } = 1.0;

        /// <summary>Output volume [0–1].</summary>
        public float Volume { get; set; } = 1.0f;

        // ── Envelope ─────────────────────────────────────────────────────────
        /// <summary>"Sustain" = standard ADSR; "Ping" = exponential decay (wick/earcon style).</summary>
        public string EnvelopeType { get; set; } = "Sustain";

        /// <summary>Note duration in seconds. Used for Ping/non-continuous voices.</summary>
        public double DurationSeconds { get; set; } = 0.3;

        // ── Category / Description ────────────────────────────────────────────
        /// <summary>Optional freeform description shown in the Sound Designer modal.</summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Returns the effective oscillator layers to render. Uses <see cref="Oscillators"/> when
        /// present; otherwise synthesizes a single layer from the legacy <see cref="Waveform"/> /
        /// <see cref="NoiseAmount"/> fields so older patches (and imported JSON) keep working.
        /// </summary>
        public IReadOnlyList<OscillatorLayer> EffectiveLayers()
        {
            if (Oscillators.Count > 0) return Oscillators;
            return new List<OscillatorLayer>
            {
                new() { Waveform = Waveform, Gain = 1f, FreqRatio = 1.0, NoiseAmount = NoiseAmount, NoiseType = "pink" }
            };
        }

        public SoundPatch Clone() => new SoundPatch
        {
            Id = Guid.NewGuid().ToString(), // clone gets a new ID
            Name = Name + " (copy)",
            Oscillators = Oscillators.Select(o => o.Clone()).ToList(),
            Waveform = Waveform,
            NoiseAmount = NoiseAmount,
            BaseFrequency = BaseFrequency,
            FreqMultiplier = FreqMultiplier,
            Volume = Volume,
            EnvelopeType = EnvelopeType,
            DurationSeconds = DurationSeconds,
            Description = Description,
        };
    }
}
