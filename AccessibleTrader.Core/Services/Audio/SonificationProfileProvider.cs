using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Audio;

/// <summary>
/// Maps ComponentDisplayType and ComponentRole to SonificationProfile data structures.
/// Extracted from StylingService.GetSonificationProfile.
/// </summary>
public sealed class SonificationProfileProvider : ISonificationProfileProvider
{
    public SonificationProfile GetProfile(ComponentDisplayType displayType, ComponentRole role, string componentName)
    {
        // Display-type-specific checks come first so that components whose Role happens to be
        // Histogram (e.g. ZeroArea Money Flow Wave) still receive the correct oscillator profile
        // rather than being routed to the generic Bars/Histograms square-wave profile.

        // 1a. ZeroArea (Money Flow Wave): triangle/sine sustain, constant amplitude.
        //     Matches Oscillator profile so the wave sounds like a smooth continuous oscillator
        //     rather than "breathing" louder/quieter with absolute value distance from zero.
        //     Above zero: triangle waveform; below zero: sine waveform (same as Oscillator).
        if (displayType == ComponentDisplayType.ZeroArea)
            return new SonificationProfile("triangle", "triangle", "sine", AmplitudeMapping.None, PitchMapping.Value, 440, 1.0, true, "Sustain");

        // 1b. ZeroDot (Money Flow dot): discrete ping at zero-line, pitch by value sign.
        //     Positive value = 660 Hz (bright), negative = 220 Hz (low). One note per signal bar.
        if (displayType == ComponentDisplayType.ZeroDot)
            return new SonificationProfile("sine", "sine", "sine", AmplitudeMapping.None, PitchMapping.Direction, 660, 1.0, false, "Ping");

        // 2. Wicks: sine pings with fixed-frequency tones — upper=880 Hz, lower=220 Hz.
        // PitchMapping.None with BaseFrequency=440, FreqMultiplier=1.0.
        // One profile covers both wick components, so the 880/220 split and the per-wick grit are
        // applied in DefaultSonificationStrategy.CreateAudioPoint. Which end a component describes
        // is decided from its DataMapping ("high"/"low"), NOT its name — see
        // DefaultSonificationStrategy.IsUpperWick for the rename that made a name test useless.
        if (role == ComponentRole.Wick || displayType == ComponentDisplayType.Wick)
            return new SonificationProfile("sine", "sine", "sine", AmplitudeMapping.DeltaFromPrice, PitchMapping.None, 440, 1.0, false, "Ping");

        // 3a. Volume bars: base SINE with a brown-noise tinge and a SUB-OCTAVE saw weight ∝ bar
        //     size (both set in DefaultSonificationStrategy.CreateAudioPoint) so intensity reads as
        //     GRIT, not loudness — quiet bars stay clearly audible instead of dropping toward
        //     silence. Sub-octave rather than same-octave: the latter fizzes. A sustained envelope
        //     makes it a continuous bed during playback.
        //     CAUTION: the 330 Hz base frequency below is NOT what you hear. PitchMapping
        //     .PriceDirection replaces it outright with the component's Bullish/BearishFrequency
        //     (see ISonificationStrategy's pitch block), which is the same pair the candle body
        //     uses — so the volume bed does not sit under the body, it sits on top of it. That is
        //     a real defect and it is written up in docs/TODO.md; the 330 here is dead weight
        //     until the pitch mapping is changed.
        if (role == ComponentRole.Volume)
            return new SonificationProfile("sine", "sine", "sine", AmplitudeMapping.None, PitchMapping.PriceDirection, 330, 1.0, false, "Sustain");

        // 3b. Histograms and other bars: base SINE with a fixed square partial (reedy character,
        //     set in CreateAudioPoint) plus saw ∝ magnitude — a distinct timbre from the volume
        //     bed, so the two never blur together when both sound during playback.
        if (role == ComponentRole.Histogram || displayType == ComponentDisplayType.Bar || displayType == ComponentDisplayType.Histogram)
            return new SonificationProfile("sine", "sine", "sine", AmplitudeMapping.Size, PitchMapping.PriceDirection, 440, 1.0, false, "Sustain");

        // 4. Oscillators: base SINE; the upper and lower halves are differentiated by a square
        //     (bright) or triangle (warm) partial set in CreateAudioPoint, not by swapping the
        //     whole waveform. NEVER sawtooth — a same-octave saw fizzes, and this voice sounds
        //     continuously. The split is on the visible-range MIDPOINT rather than ReferenceLevel,
        //     because many oscillators leave that unset and the zone must always be audible.
        if (displayType == ComponentDisplayType.Oscillator)
            return new SonificationProfile("sine", "sine", "sine", AmplitudeMapping.None, PitchMapping.Value, 440, 1.0, true, "Sustain");

        // 5. Dot / Arrow: transient Ping earcon, direction-mapped pitch.
        //     Sparse signal markers — one earcon per signal bar, silence on NaN bars.
        //     660 Hz = bullish/positive, 220 Hz = bearish/negative. CipherB dots use this default.
        if (displayType == ComponentDisplayType.Dot || displayType == ComponentDisplayType.Arrow)
            return new SonificationProfile("sine", "sine", "sine", AmplitudeMapping.None, PitchMapping.Direction, 660, 1.0, false, "Ping");

        // 6. Candle bodies: direction-mapped pitch (440 Hz bullish / 220 Hz bearish).
        // DeltaFromPrice amplitude: body loudness is CONSTANT (see the DeltaFromPrice block in
        // ISonificationStrategy) and body size is carried by grit instead. This line used to say
        // "a doji is quiet and a marubozu is loud", which was the behaviour before that change and
        // the exact opposite of the comment three lines below it.
        if (displayType == ComponentDisplayType.Candle)
            // Base SINE with a fixed square partial + saw ∝ body size (set in CreateAudioPoint):
            // a distinct "body" timbre vs the pure-sine price line, where body size reads as GRIT
            // rather than loudness (loudness held constant so a doji and a marubozu are equally
            // present, differing in character).
            return new SonificationProfile("sine", "sine", "sine", AmplitudeMapping.DeltaFromPrice, PitchMapping.Direction, 440, 1.0, false, "Sustain");

        // 7. Static levels: quiet low sine
        if (displayType == ComponentDisplayType.Level)
            return new SonificationProfile("sine", "sine", "sine", AmplitudeMapping.None, PitchMapping.None, 220, 1.0, false, "Sustain");

        // 8. New marker shapes: transient ping, direction-mapped pitch (440 Hz up / 220 Hz down).
        //    TriangleUp/Down have fixed visual direction; Diamond/Square/Cross are value-neutral markers.
        if (displayType is ComponentDisplayType.TriangleUp or ComponentDisplayType.TriangleDown
            or ComponentDisplayType.Diamond or ComponentDisplayType.Square or ComponentDisplayType.Cross)
            return new SonificationProfile("sine", "sine", "sine", AmplitudeMapping.None, PitchMapping.Direction, 440, 1.0, false, "Ping");

        // 9. GradientDot: continuous momentum ribbon — smooth sustain, pitch tracks WT value.
        //    Renders on every bar (not sparse), so Sustain avoids rapid-fire click artefacts.
        //    PitchMapping.Value lets the tone glide higher (positive/overbought) or lower (negative/oversold).
        if (displayType == ComponentDisplayType.GradientDot)
            return new SonificationProfile("sine", "sine", "sine", AmplitudeMapping.None, PitchMapping.Value, 440, 1.0, true, "Sustain");

        // 10. Default lines: smooth sine
        return new SonificationProfile("sine", "sine", "sine", AmplitudeMapping.None, PitchMapping.Value, 440, 1.0, false, "Sustain");
    }
}
