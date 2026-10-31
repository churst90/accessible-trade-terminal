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
        string? BelowLabel = null,

        /// <summary>
        /// False = a level that is HEARD but not DRAWN: it textures, clicks, is spoken as a zone,
        /// narrated when crossed and alertable exactly like a drawn level, for the components that
        /// subscribe to it (<c>SubscribedLevelNames</c>), but no line is painted and the axis is not
        /// stretched to reach it. For a line whose extremes are not the pane's: Cipher B's 0–100
        /// RSI and Stochastic share a −100…+100 pane whose ±53/±60 belong to the waves, and take
        /// undrawn 80/20 levels of their own (Cody, 2026-10-17: "80 / 20", "sound only", "yes, they
        /// should be narrated").
        /// </summary>
        bool IsDrawn = true,

        /// <summary>Line width in pixels, as the level is first drawn.</summary>
        float Thickness = 1f,

        /// <summary>
        /// The indicator parameter this level's value IS, or null for a fixed level. A bound level
        /// takes its value from that parameter whenever the parameter is set (creation, restore,
        /// Properties), so one number drives both the line and the calculation that reads it:
        /// Cipher B's "Over Sold Level 1" is both its −60 line and the buy dot's threshold, as in
        /// Market Cipher B (2026-10-31). See <see cref="ParameterBinding"/>.
        /// </summary>
        string? ValueParameter = null
    );
}
