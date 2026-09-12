namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// Describes a reference level line declared by an indicator provider.
    /// All audio fields default to off so providers only need to declare what they use.
    /// </summary>
    public record LevelDescriptor(
        string Name,
        double Value,
        string ColorHex,
        DashStyle Dash,
        bool PlayEarcon       = false,
        float EarconVolume    = 0.7f,
        float ZoneNoiseAmount = 0f,
        string ZoneNoiseType  = "pink",

        /// <summary>
        /// What being ABOVE this line means, in one or two words, for the spoken zone.
        ///
        /// <para>
        /// Overbought and oversold are handled by <see cref="LevelRole"/> and need nothing here.
        /// This is for a line that divides a scale into named BANDS instead: ADX at 25 separates
        /// a developing trend from a strong one, and Choppiness at 61.8 separates a trending
        /// market from a ranging one. Neither is an extreme, so neither has a role that fits, and
        /// until 2026-09-12 the <c>{zone}</c> token on both of those indicators resolved to an
        /// empty string on every bar.
        /// </para>
        ///
        /// <para>
        /// Declare BOTH sides where the pairing is not obvious, and note that it is not always the
        /// obvious way round: Choppiness is INVERTED — a LOW reading means trending. That is
        /// precisely the fact that has to be stated rather than inferred from the order of the
        /// numbers, which is why this is a declaration and not a rule.
        /// </para>
        /// </summary>
        string? AboveLabel = null,

        /// <summary>What being BELOW this line means. See <see cref="AboveLabel"/>.</summary>
        string? BelowLabel = null
    );
}
