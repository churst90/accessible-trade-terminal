namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Named-field parameter block for <see cref="IAudioDriver.SetVoice(int, in VoiceParams)"/>.
    ///
    /// The legacy 16-positional-parameter SetVoice grew one argument per audio
    /// feature (noise, partials, grit) until wrong-position bugs were one refactor
    /// away — two adjacent floats (say SawMix and TriangleMix) transpose silently
    /// and compile clean. New code should build a VoiceParams; the positional
    /// overload remains for existing call sites and migrates opportunistically.
    ///
    /// Defaults mirror the positional signature exactly, so
    /// <c>driver.SetVoice(slot, new VoiceParams { Frequency = 440, Volume = 0.5f })</c>
    /// is identical to the two-argument-plus-defaults positional call.
    /// </summary>
    public readonly record struct VoiceParams()
    {
        public double Frequency { get; init; } = 0;
        public float Volume { get; init; } = 0f;
        public float Pan { get; init; } = 0f;
        public string Waveform { get; init; } = "sine";
        /// <summary>True = plays until StopVoice; false = self-terminates after <see cref="DurationSeconds"/>.</summary>
        public bool Continuous { get; init; } = false;
        public double DurationSeconds { get; init; } = 0.2;
        /// <summary>Bar index reported via PointReached when this voice starts; -1 = none.</summary>
        public int DataIndex { get; init; } = -1;
        /// <summary>"Sustain" (ADSR) or "Ping" (exponential decay).</summary>
        public string Envelope { get; init; } = "Sustain";
        /// <summary>Fire the boundary click transient with this voice.</summary>
        public bool Click { get; init; } = false;
        public float NoiseAmount { get; init; } = 0f;
        /// <summary>"white", "pink", or "brown".</summary>
        public string NoiseType { get; init; } = "pink";
        // Additive partials mixed into the base waveform (see AudioEngine).
        public float SquareMix { get; init; } = 0f;
        public float SawMix { get; init; } = 0f;
        public float TriangleMix { get; init; } = 0f;
        /// <summary>Sub-octave sawtooth blend — the "grit" that encodes size.</summary>
        public float SubSawMix { get; init; } = 0f;
    }
}
