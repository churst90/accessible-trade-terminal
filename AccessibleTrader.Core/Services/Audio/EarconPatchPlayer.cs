using System;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Audio
{
    /// <summary>
    /// Resolves and plays user-assigned Sound Designer patches for the reference-level
    /// cues (cross chirp, approach ping, sustained-zone tone). Each cue has an earcon
    /// key assignable in the Sound Designer's earcon panel; when no patch is assigned
    /// the caller falls back to its built-in tone, so the default soundscape is
    /// unchanged until the user opts in.
    /// </summary>
    public static class EarconPatchPlayer
    {
        // Earcon override keys — listed in SoundDesignerModal's assignment panel.
        public const string CrossUpKey   = "LevelCrossUp";
        public const string CrossDownKey = "LevelCrossDown";
        public const string ApproachKey  = "LevelApproach";
        public const string SustainedKey = "LevelSustained";

        // Slots 26–29: one-shot patch layers for level cues. Inside the UI earcon
        // range (16–31), below the chirp pair on 30/31, so cues layer cleanly over
        // navigation (0–15) and playback (32–95) voices.
        public const int CueSlotStart = 26;
        private const int MaxLayers = 4;

        /// <summary>
        /// Plays the patch assigned to <paramref name="earconKey"/>, if any.
        /// Returns false when no override exists (caller plays its built-in tone).
        /// </summary>
        public static bool TryPlayOverride(ISoundPatchLibrary? library, IAudioDriver? driver,
            string earconKey, float volumeScale, float pan)
        {
            if (library == null || driver == null) return false;
            if (!library.EarconOverrides.EarconPatchIds.TryGetValue(earconKey, out var patchId)
                || string.IsNullOrEmpty(patchId))
                return false;
            var patch = library.GetPatch(patchId);
            if (patch == null) return false;

            double baseFreq = patch.BaseFrequency * patch.FreqMultiplier;
            string env = string.IsNullOrEmpty(patch.EnvelopeType) ? "Ping" : patch.EnvelopeType;
            var layers = patch.EffectiveLayers();
            for (int i = 0; i < layers.Count && i < MaxLayers; i++)
            {
                var layer = layers[i];
                driver.SetVoice(CueSlotStart + i, baseFreq * layer.FreqRatio,
                    Math.Clamp(patch.Volume * layer.Gain * volumeScale, 0f, 1f),
                    pan, layer.Waveform, false, patch.DurationSeconds, -1, env,
                    i == 0, Math.Max(0f, layer.NoiseAmount),
                    string.IsNullOrEmpty(layer.NoiseType) ? "pink" : layer.NoiseType);
            }
            return true;
        }
    }
}
