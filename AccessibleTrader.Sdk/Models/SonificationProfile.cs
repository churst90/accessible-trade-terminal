namespace AccessibleTrader.Sdk.Models;

/// <summary>
/// Defines the acoustic rendering profile for a single chart component.
/// </summary>
public record SonificationProfile(
    string Waveform,
    string AboveWaveform = "sine",
    string BelowWaveform = "sine",
    AmplitudeMapping AmplitudeMapping = AmplitudeMapping.None,
    PitchMapping PitchMapping = PitchMapping.Value,
    double BaseFrequency = 440,
    double FreqMultiplier = 1.0,
    bool TriggerBoundaryClick = false,
    string EnvelopeType = "Sustain"
);
