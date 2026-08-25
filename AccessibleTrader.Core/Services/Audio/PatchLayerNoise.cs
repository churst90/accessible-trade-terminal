using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Audio
{
    /// <summary>
    /// One rule for how a user patch's per-layer noise combines with the computed
    /// zone texture (overbought/oversold roughness), shared by the navigation and
    /// playback renderers. The contract users rely on: assigning a patch changes
    /// the INSTRUMENT, never silences the ZONE CUE — layer 0 always carries at
    /// least the zone texture. (Playback originally dropped it: a patched RSI
    /// lost its overbought roughness under Space but kept it under arrows.)
    /// </summary>
    public static class PatchLayerNoise
    {
        public static (float Amount, string Type) Merge(
            int layerIndex, OscillatorLayer layer, float zoneNoise, string? zoneNoiseType)
        {
            float layerNoise = Math.Max(0f, layer.NoiseAmount);
            string layerType = string.IsNullOrEmpty(layer.NoiseType) ? "pink" : layer.NoiseType;
            if (layerIndex != 0)
                return (layerNoise, layerType); // zone cue rides layer 0 only — no double noise

            return zoneNoise > layerNoise
                ? (zoneNoise, string.IsNullOrEmpty(zoneNoiseType) ? "pink" : zoneNoiseType!)
                : (layerNoise, layerType);
        }
    }
}
