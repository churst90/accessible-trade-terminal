using System.Collections.Concurrent;
using System.Linq;

namespace AccessibleTrader.Core.Services.Audio
{
    /// <summary>
    /// A named bell synthesis preset describing how to build a Ping-envelope voice:
    /// waveform blend, harmonic content, decay, and optional detuned-pair parameters.
    /// These are distinct from user-editable <see cref="AccessibleTrader.Sdk.Models.SoundPatch"/>
    /// objects in <c>ISoundPatchLibrary</c>; registry patches are code-defined built-ins.
    /// </summary>
    public record SoundPatch(
        /// <summary>"sine", "triangle", "sawtooth", or "square".</summary>
        string BaseWaveform,
        /// <summary>0–1: blend of 2nd (or Nth) harmonic into the fundamental. 0 = pure fundamental.</summary>
        float HarmonicAmount,
        /// <summary>Frequency multiplier for the blended harmonic. 2.0 = octave, 2.756 = bell minor-third.</summary>
        float HarmonicFreqMultiplier,
        /// <summary>Fallback decay in ms when ComponentConfig.DecayMs is null.</summary>
        int DefaultDecayMs,
        /// <summary>When true the sequencer fires two voices: primary + a detuned copy offset by DetuneIntervalHz.</summary>
        bool IsDetuned,
        /// <summary>Second-voice pitch offset in Hz above the primary voice (used only when IsDetuned = true).</summary>
        float DetuneIntervalHz,
        /// <summary>Delay in ms before the second detuned voice fires (0 = simultaneous, 40 = staggered).</summary>
        int DetunedOffsetMs
    );

    /// <summary>
    /// Registry of named bell synthesis patches used by AudioSequencer and NavigationSonifier.
    /// Built-in patches are registered in the constructor; providers or tests can add custom patches
    /// via <see cref="Register"/>.
    /// </summary>
    public interface ISoundPatchRegistry
    {
        /// <summary>Returns true and populates <paramref name="patch"/> when the ID is found.</summary>
        bool TryGetPatch(string patchId, out SoundPatch patch);
        /// <summary>Registers or replaces a patch by ID.</summary>
        void Register(string patchId, SoundPatch patch);
        /// <summary>All registered patch IDs (built-ins + any registered at runtime), for selection UIs.</summary>
        System.Collections.Generic.IReadOnlyCollection<string> GetPatchIds();
    }

    /// <inheritdoc />
    public class SoundPatchRegistry : ISoundPatchRegistry
    {
        private readonly ConcurrentDictionary<string, SoundPatch> _patches = new();

        public SoundPatchRegistry()
        {
            RegisterBuiltins();
        }

        private void RegisterBuiltins()
        {
            // Clean bell — pure sine with slight octave harmonic, used for crossover signals.
            Register("sine_bell", new SoundPatch(
                BaseWaveform: "sine",
                HarmonicAmount: 0.25f,
                HarmonicFreqMultiplier: 2.0f,
                DefaultDecayMs: 300,
                IsDetuned: false,
                DetuneIntervalHz: 0f,
                DetunedOffsetMs: 0
            ));

            // Hollow structural bell — triangle fundamental with natural odd harmonics, used for divergences.
            Register("triangle_bell", new SoundPatch(
                BaseWaveform: "triangle",
                HarmonicAmount: 0.0f,       // triangle already has natural odd harmonics
                HarmonicFreqMultiplier: 2.0f,
                DefaultDecayMs: 250,
                IsDetuned: false,
                DetuneIntervalHz: 0f,
                DetunedOffsetMs: 0
            ));

            // Crisp boundary bell — triangle with 3rd harmonic crystalline overtone, used for SR dots.
            Register("crystal_bell", new SoundPatch(
                BaseWaveform: "triangle",
                HarmonicAmount: 0.15f,
                HarmonicFreqMultiplier: 3.0f,   // 3rd harmonic for crystalline overtone
                DefaultDecayMs: 200,
                IsDetuned: false,
                DetuneIntervalHz: 0f,
                DetunedOffsetMs: 0
            ));

            // Metallic pair — two simultaneous/staggered voices, used for Manipulation/Exhaustion.
            Register("detuned_pair_bell", new SoundPatch(
                BaseWaveform: "triangle",
                HarmonicAmount: 0.2f,
                HarmonicFreqMultiplier: 2.756f, // minor 3rd overtone
                DefaultDecayMs: 320,
                IsDetuned: true,
                DetuneIntervalHz: 100f,         // second voice +100 Hz above primary
                DetunedOffsetMs: 40            // 40ms stagger before second voice fires
            ));

            // Dual simultaneous tones 220 Hz apart — golden chord for Triple Confluence Buy.
            // Both voices fire at the same time (DetunedOffsetMs=0) for a unified chord quality
            // distinct from the staggered metallic character of detuned_pair_bell.
            Register("dual_tone_bell", new SoundPatch(
                BaseWaveform: "sine",
                HarmonicAmount: 0.2f,
                HarmonicFreqMultiplier: 2.0f,
                DefaultDecayMs: 500,
                IsDetuned: true,
                DetuneIntervalHz: 220f,         // second voice at primary + 220 Hz (e.g. 440 + 220 = 660 Hz)
                DetunedOffsetMs: 0             // simultaneous — golden chord, not staggered metallic
            ));

            // Quality long setup — bright ascending chord (sine + perfect 5th above), long sustain.
            // Used by composite strategies / signal composer to mark a high-quality long setup.
            // Distinct from sine_bell (single tone) and dual_tone_bell (Triple Confluence golden chord)
            // by its long 700ms decay and rising perfect-fifth interval (220 Hz above the fundamental
            // when fundamental = 440 Hz, giving the major triad open quality of a "go" bell).
            Register("setup_long_bell", new SoundPatch(
                BaseWaveform: "sine",
                HarmonicAmount: 0.30f,
                HarmonicFreqMultiplier: 3.0f,   // octave + perfect fifth (12th) for shimmer
                DefaultDecayMs: 700,
                IsDetuned: true,
                DetuneIntervalHz: 220f,         // perfect fifth above 440 Hz fundamental
                DetunedOffsetMs: 0             // simultaneous bright chord
            ));

            // Quality short setup — heavy descending tone (triangle + minor 3rd below + low octave),
            // long sustain. Used by composite strategies / signal composer to mark a high-quality
            // short setup. Distinct from setup_long_bell by its triangle base, descending interval,
            // and a -150 Hz second voice (sounds like a tolling low bell, signalling weight/risk).
            Register("setup_short_bell", new SoundPatch(
                BaseWaveform: "triangle",
                HarmonicAmount: 0.35f,
                HarmonicFreqMultiplier: 0.5f,   // octave below for low-bell weight
                DefaultDecayMs: 700,
                IsDetuned: true,
                DetuneIntervalHz: -150f,        // descending minor-3rd-ish under fundamental
                DetunedOffsetMs: 60            // brief stagger gives a "two-toll" character
            ));

            // Gradient blend — used for Cipher A momentum gradient dots. All this patch still
            // contributes is the neutral/midpoint waveform below.
            //
            // It used to carry GradientWaveformA/B ("sine" bullish, "sawtooth" bearish) and
            // nothing ever read them: both renderers choose the blend waveform themselves and
            // both hardcode triangle/sawtooth — NavigationSonifier.SyncNavigationSlots and
            // AudioSequencer.ComputeGradientBlend. The patch did not even agree with the sound,
            // naming sine where the renderers play triangle. Removed rather than wired up,
            // because wiring them would change what a user hears; if the blend ever should be
            // configurable it belongs in ComponentConfig with the other per-component sound
            // settings, not in a code-defined built-in.
            Register("gradient_blend", new SoundPatch(
                BaseWaveform: "triangle",       // neutral/midpoint waveform
                HarmonicAmount: 0.0f,
                HarmonicFreqMultiplier: 2.0f,
                DefaultDecayMs: 80,
                IsDetuned: false,
                DetuneIntervalHz: 0f,
                DetunedOffsetMs: 0
            ));
        }

        /// <inheritdoc />
        public bool TryGetPatch(string patchId, out SoundPatch patch) =>
            _patches.TryGetValue(patchId, out patch!);

        /// <inheritdoc />
        public void Register(string patchId, SoundPatch patch) =>
            _patches[patchId] = patch;

        /// <inheritdoc />
        public System.Collections.Generic.IReadOnlyCollection<string> GetPatchIds() =>
            _patches.Keys.ToArray();
    }
}
