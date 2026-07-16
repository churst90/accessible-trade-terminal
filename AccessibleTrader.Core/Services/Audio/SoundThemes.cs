using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Audio
{
    // NOTE: this namespace has its own SoundPatch record (the registry bell
    // descriptor in SoundPatchRegistry.cs), which shadows the Sdk model — the
    // Sound Designer patch type is therefore written fully qualified below.

    /// <summary>
    /// Factory voice bank + named sound themes.
    ///
    /// A THEME gives each indicator family its own instrument, so during full-chart
    /// playback the user knows "that's the RSI" / "that's the MACD" by timbre alone.
    /// A theme is just a map from component family (display type + role) to a factory
    /// patch id; the patches are ordinary multi-oscillator <see cref="SoundPatch"/>es
    /// built from classic additive-synthesis recipes (organ drawbars, flute + breath,
    /// clarinet odd-harmonics, inharmonic glass, detuned strings).
    ///
    /// Scope rules (why candles/volume/wicks are NEVER themed): their timbre IS
    /// semantic — body size and volume magnitude are encoded as sub-octave grit
    /// computed per bar, and a fixed patch would silence that encoding. Themes apply
    /// only to line/oscillator/band components, whose partials are decorative.
    ///
    /// "classic" maps everything to null — the built-in sine + light partials — so
    /// the default soundscape is exactly what it was before themes existed.
    /// </summary>
    public sealed record SoundThemeInfo(string Id, string Name, string Description);

    public static class SoundThemes
    {
        public const string SettingsKey = "audio.soundTheme";
        public const string ClassicId = "classic";

        public static readonly IReadOnlyList<SoundThemeInfo> All = new[]
        {
            new SoundThemeInfo(ClassicId,   "Classic (pure tones)",
                "The original palette: sine with light square/triangle colouring."),
            new SoundThemeInfo("orchestra", "Orchestra (instrument per family)",
                "Flute price/MA lines, clarinet oscillators, organ zero-cross indicators, glass bands."),
            new SoundThemeInfo("organ",     "Pipe organ (drawbar registrations)",
                "Every family a different organ stop: soft flutes to bright mixtures."),
            new SoundThemeInfo("strings",   "Strings (warm detuned ensemble)",
                "Detuned ensemble voices; families differ by register and brightness."),
        };

        /// <summary>
        /// The factory instrument patches. Ids are stable ("voice_*") and resolvable
        /// through <see cref="ISoundPatchLibrary.GetPatch"/>, so a user can also pick
        /// them manually for any component in the Properties dialog, preview them in
        /// the Sound Designer, and assign them to earcons.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, Sdk.Models.SoundPatch> FactoryPatches = BuildFactoryPatches();

        private static IReadOnlyDictionary<string, Sdk.Models.SoundPatch> BuildFactoryPatches()
        {
            var d = new Dictionary<string, Sdk.Models.SoundPatch>(StringComparer.OrdinalIgnoreCase);
            void Add(string id, string name, string desc, params OscillatorLayer[] layers)
                => d[id] = new Sdk.Models.SoundPatch
                {
                    Id = id, Name = name, Description = desc,
                    EnvelopeType = "Sustain", DurationSeconds = 0.45, Volume = 1.0f,
                    Oscillators = new List<OscillatorLayer>(layers),
                };
            static OscillatorLayer L(string wave, double ratio, float gain, float noise = 0f, string noiseType = "pink")
                => new() { Waveform = wave, FreqRatio = ratio, Gain = gain, NoiseAmount = noise, NoiseType = noiseType };

            // Flute: nearly pure fundamental, faint octave, a whisper of breath noise.
            Add("voice_flute", "Flute", "Warm and breathy — factory voice for price and MA lines.",
                L("sine", 1.0, 1.0f), L("sine", 2.0, 0.18f), L("triangle", 1.0, 0.15f, 0.03f));

            // Clarinet: odd harmonics dominate (1, 3, 5) — hollow, reedy.
            Add("voice_reed", "Clarinet", "Hollow, reedy odd-harmonics — factory voice for bounded oscillators.",
                L("sine", 1.0, 1.0f), L("sine", 3.0, 0.35f), L("sine", 5.0, 0.15f), L("square", 1.0, 0.10f));

            // Organ (principal chorus): drawbar-style 16'+8'+4'+2⅔'+2'.
            Add("voice_organ", "Pipe organ", "Full drawbar registration — factory voice for zero-cross indicators.",
                L("sine", 0.5, 0.45f), L("sine", 1.0, 1.0f), L("sine", 2.0, 0.45f), L("sine", 3.0, 0.28f), L("sine", 4.0, 0.18f));

            // Glass: inharmonic partials — bell-like, crystalline, unmistakable.
            Add("voice_glass", "Glass", "Inharmonic crystalline shimmer — factory voice for band edges.",
                L("sine", 1.0, 1.0f), L("sine", 2.76, 0.22f), L("sine", 5.40, 0.09f));

            // Strings: two saws a whisker apart beat gently over a sine core.
            Add("voice_strings", "Strings", "Warm detuned ensemble.",
                L("sine", 1.0, 0.6f), L("sawtooth", 0.997, 0.28f), L("sawtooth", 1.003, 0.28f));

            // Organ variants for the all-organ theme (different registrations per family).
            Add("voice_organ_soft", "Organ, soft flutes", "8'+4' flutes — quiet stop.",
                L("sine", 1.0, 1.0f), L("sine", 2.0, 0.30f));
            Add("voice_organ_quint", "Organ, quint", "8'+2⅔' — nasal quint colour.",
                L("sine", 1.0, 1.0f), L("sine", 3.0, 0.45f));
            Add("voice_organ_bright", "Organ, mixture", "Bright upper-work mixture.",
                L("sine", 1.0, 1.0f), L("sine", 2.0, 0.5f), L("sine", 4.0, 0.32f), L("sine", 6.0, 0.16f));

            // Strings variants by register.
            Add("voice_strings_low", "Strings, low", "Dark cello-register ensemble.",
                L("sine", 0.5, 0.55f), L("sawtooth", 0.499, 0.25f), L("sawtooth", 0.501, 0.25f), L("sine", 1.0, 0.4f));
            Add("voice_strings_bright", "Strings, bright", "Violin-register ensemble with edge.",
                L("sine", 1.0, 0.5f), L("sawtooth", 1.995, 0.22f), L("sawtooth", 2.005, 0.22f));

            return d;
        }

        /// <summary>
        /// Component families a theme voices. Everything else (candles, wicks, volume,
        /// markers, profiles, heatmaps) keeps its semantic built-in timbre.
        /// </summary>
        private enum Family { None, LineOverlay, BoundedOscillator, ZeroCross, Band }

        private static Family Classify(ComponentDisplayType type, ComponentRole role)
        {
            // Semantic timbres are off-limits (grit encodes size on these).
            if (role is ComponentRole.Body or ComponentRole.Wick or ComponentRole.Volume or ComponentRole.Histogram)
                return Family.None;
            return type switch
            {
                ComponentDisplayType.Line or ComponentDisplayType.StepLine => role is ComponentRole.UpperBand or ComponentRole.LowerBand or ComponentRole.Median
                    ? Family.Band
                    : Family.LineOverlay,
                ComponentDisplayType.Oscillator => Family.BoundedOscillator,
                ComponentDisplayType.ZeroArea   => Family.ZeroCross,
                ComponentDisplayType.Area       => Family.Band,
                _ => Family.None,
            };
        }

        /// <summary>
        /// Resolves the factory patch a theme assigns to a component, or null for
        /// "keep the built-in timbre" (always null for the classic theme and for
        /// semantic components).
        /// </summary>
        public static string? ResolvePatchId(string? themeId, ComponentDisplayType type, ComponentRole role)
        {
            if (string.IsNullOrEmpty(themeId) || themeId == ClassicId) return null;
            var family = Classify(type, role);
            if (family == Family.None) return null;

            return (themeId, family) switch
            {
                ("orchestra", Family.LineOverlay)       => "voice_flute",
                ("orchestra", Family.BoundedOscillator) => "voice_reed",
                ("orchestra", Family.ZeroCross)         => "voice_organ",
                ("orchestra", Family.Band)              => "voice_glass",

                ("organ", Family.LineOverlay)       => "voice_organ_soft",
                ("organ", Family.BoundedOscillator) => "voice_organ_quint",
                ("organ", Family.ZeroCross)         => "voice_organ",
                ("organ", Family.Band)              => "voice_organ_bright",

                ("strings", Family.LineOverlay)       => "voice_strings",
                ("strings", Family.BoundedOscillator) => "voice_strings_bright",
                ("strings", Family.ZeroCross)         => "voice_strings_low",
                ("strings", Family.Band)              => "voice_glass",

                _ => null,
            };
        }
    }
}
