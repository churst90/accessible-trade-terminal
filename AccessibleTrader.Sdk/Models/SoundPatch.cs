using System;

namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// A named, serializable audio preset that can be assigned to any
    /// <see cref="ComponentConfig"/> or earcon feedback type.
    /// </summary>
    public class SoundPatch
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "New Patch";

        // ── Oscillator ────────────────────────────────────────────────────────
        /// <summary>Waveform name: "sine", "square", "sawtooth", "triangle", "noise".</summary>
        public string Waveform { get; set; } = "sine";

        /// <summary>Blend of pink noise into the base waveform [0–1]. 0 = pure waveform.</summary>
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

        public SoundPatch Clone() => new SoundPatch
        {
            Id = Guid.NewGuid().ToString(), // clone gets a new ID
            Name = Name + " (copy)",
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
