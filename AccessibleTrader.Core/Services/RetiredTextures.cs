using AccessibleTrader.Core.Services.Indicators;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Noise textures indicators used to declare and no longer do, so a saved "default" holding one
    /// is known to be the old default and not the user's choice.
    ///
    /// <para>Cody, 2026-10-31: "there should only be texturing ... within those extreme zones ...
    /// I am hearing the softer white noise all the time", and "instead of the louder white noise, we
    /// can just amplify the softer noise". Until then eleven components carried a texture of their
    /// own on every bar (Cipher B's two waves 0.15, and nine others), and Cipher B's and the four
    /// sentiment indicators' zones were white noise. Now no indicator component has a texture of its
    /// own, and the zones are pink.</para>
    ///
    /// <para>"Save as default" in Properties writes EVERY field, the texture included, so an
    /// indicator default saved before 2026-10-31 holds the old value whether or not the user ever
    /// touched it. A saved value equal to a retired default is therefore read as "never chosen" and
    /// the indicator's current default applies; any other value is the user's and is kept. Levels on
    /// a saved chart are healed against their own baseline instead
    /// (<c>SeriesManagementService.HealSavedLevels</c>).</para>
    /// </summary>
    public static class RetiredTextures
    {
        /// <summary>The component's own texture until 2026-10-31, or null when it never had one.</summary>
        public static float? BaseNoise(string indicatorCode, string componentName) =>
            (indicatorCode.ToUpperInvariant(), componentName) switch
            {
                ("CIPHER_B", CipherBProvider.CompWT1 or CipherBProvider.CompWT2)              => 0.15f,
                ("FEAR_GREED", FearGreedProvider.CompSentiment)                               => 0.10f,
                ("CROWDING_INDEX", CrowdingIndexProvider.CompCrowdingScore)                   => 0.10f,
                ("COT_POSITIONING", CotPositioningProvider.CompZScore)                        => 0.10f,
                ("FUNDING_RATE", FundingRateProvider.CompFundingRate)                         => 0.10f,
                (TopBottomDetectorProvider.Code, TopBottomDetectorProvider.CompCapitulation
                                               or TopBottomDetectorProvider.CompDistribution) => 0.05f,
                (PulseProvider.Code, PulseProvider.CompWtFast)                                => 0.06f,
                (PulseProvider.Code, PulseProvider.CompMfi)                                   => 0.05f,
                ("OPEN_INTEREST", OpenInterestProvider.CompOiValue)                           => 0.08f,
                _ => null,
            };

        public static bool IsRetiredBaseNoise(string indicatorCode, string componentName, float saved) =>
            BaseNoise(indicatorCode, componentName) is { } old && Math.Abs(saved - old) < 1e-4f;

        /// <summary>Every zone texture (amount, type) the level has had as a default before 2026-10-31.</summary>
        public static IReadOnlyList<(float Amount, string Type)> ZoneNoise(string indicatorCode, string levelName) =>
            (indicatorCode.ToUpperInvariant(), levelName) switch
            {
                // Cipher B: 0.10 / 0.20 white until 2026-10-17, then 0.30 / 0.45 white.
                ("CIPHER_B", "Overbought" or "Oversold")   => new[] { (0.10f, "white"), (0.30f, "white") },
                ("CIPHER_B", "Extreme OB" or "Extreme OS") => new[] { (0.20f, "white"), (0.45f, "white") },
                ("FEAR_GREED", "Extreme Greed" or "Extreme Fear")
                    or ("CROWDING_INDEX", "Long Crowded" or "Short Crowded")
                    or ("COT_POSITIONING", "Crowded Long" or "Crowded Short")
                    or ("FUNDING_RATE", "Extreme Long" or "Extreme Short") => new[] { (0.20f, "white") },
                _ => Array.Empty<(float, string)>(),
            };

        /// <summary>True when a saved level texture is one of the level's retired defaults (amount AND type).</summary>
        public static bool IsRetiredZoneNoise(string indicatorCode, string levelName, float amount, string? type) =>
            ZoneNoise(indicatorCode, levelName).Any(z => Math.Abs(amount - z.Amount) < 1e-4f
                && string.Equals(type ?? "pink", z.Type, StringComparison.OrdinalIgnoreCase));
    }
}
