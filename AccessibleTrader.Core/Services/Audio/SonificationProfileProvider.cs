using System;
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
        // The actual 880/220 split is applied per-component in DefaultSonificationStrategy.CreateAudioPoint
        // based on comp.Name ("Upper"/"Lower"), since one profile covers both wick components.
        if (role == ComponentRole.Wick || displayType == ComponentDisplayType.Wick)
            return new SonificationProfile("sine", "sine", "sine", AmplitudeMapping.DeltaFromPrice, PitchMapping.None, 440, 1.0, false, "Ping");

        // 3. Bars and Histograms: sharp square wave
        if (role == ComponentRole.Histogram || role == ComponentRole.Volume || displayType == ComponentDisplayType.Bar || displayType == ComponentDisplayType.Histogram)
            return new SonificationProfile("square", "square", "square", AmplitudeMapping.Size, PitchMapping.PriceDirection, 440, 1.0, false, "Sustain");

        // 4. Oscillators: triangle above, sine below
        if (displayType == ComponentDisplayType.Oscillator)
            return new SonificationProfile("triangle", "triangle", "sine", AmplitudeMapping.None, PitchMapping.Value, 440, 1.0, true, "Sustain");

        // 5. Dot / Arrow: transient Ping earcon, direction-mapped pitch.
        //     Sparse signal markers — one earcon per signal bar, silence on NaN bars.
        //     660 Hz = bullish/positive, 220 Hz = bearish/negative. CipherB dots use this default.
        if (displayType == ComponentDisplayType.Dot || displayType == ComponentDisplayType.Arrow)
            return new SonificationProfile("sine", "sine", "sine", AmplitudeMapping.None, PitchMapping.Direction, 660, 1.0, false, "Ping");

        // 4. Candle bodies: direction-mapped pitch (440 Hz bullish / 220 Hz bearish).
        // DeltaFromPrice amplitude: body volume scales with |close-open|/(high-low) ratio
        // so a doji is quiet and a marubozu is loud, viewport-normalized (no absolute-price floor issue).
        if (displayType == ComponentDisplayType.Candle)
            return new SonificationProfile("square", "square", "square", AmplitudeMapping.DeltaFromPrice, PitchMapping.Direction, 440, 1.0, false, "Sustain");

        // 5. Static levels: quiet low sine
        if (displayType == ComponentDisplayType.Level)
            return new SonificationProfile("sine", "sine", "sine", AmplitudeMapping.None, PitchMapping.None, 220, 1.0, false, "Sustain");

        // 6. New marker shapes: transient ping, direction-mapped pitch (440 Hz up / 220 Hz down).
        //    TriangleUp/Down have fixed visual direction; Diamond/Square/Cross are value-neutral markers.
        if (displayType is ComponentDisplayType.TriangleUp or ComponentDisplayType.TriangleDown
            or ComponentDisplayType.Diamond or ComponentDisplayType.Square or ComponentDisplayType.Cross)
            return new SonificationProfile("sine", "sine", "sine", AmplitudeMapping.None, PitchMapping.Direction, 440, 1.0, false, "Ping");

        // 7. GradientDot: continuous momentum ribbon — smooth sustain, pitch tracks WT value.
        //    Renders on every bar (not sparse), so Sustain avoids rapid-fire click artefacts.
        //    PitchMapping.Value lets the tone glide higher (positive/overbought) or lower (negative/oversold).
        if (displayType == ComponentDisplayType.GradientDot)
            return new SonificationProfile("sine", "sine", "sine", AmplitudeMapping.None, PitchMapping.Value, 440, 1.0, true, "Sustain");

        // 7. Default lines: smooth sine
        return new SonificationProfile("sine", "sine", "sine", AmplitudeMapping.None, PitchMapping.Value, 440, 1.0, false, "Sustain");
    }
}
