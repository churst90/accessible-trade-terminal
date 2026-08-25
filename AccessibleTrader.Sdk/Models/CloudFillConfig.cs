namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// Declares how a cloud fill should sound during Chart-scope playback (Space key).
    /// When null, the cloud fill has no audio representation.
    ///
    /// <para><b>When to opt in (Sonification = new CloudSonificationConfig(...))</b>:</para>
    /// Only when the cloud carries a regime signal its two boundary components do
    /// not individually sonify. Examples: Ichimoku Kumo (bullish/bearish cloud = trend
    /// direction), Cipher B "Anchor Fill" (HTF anchor polarity), MA Cloud between
    /// two moving averages of different lengths.
    ///
    /// <para><b>When NOT to opt in (Sonification = null)</b>:</para>
    /// Wave fills between two oscillator lines that each already sonify (e.g. Cipher B
    /// "WT Fill" between WT1 and WT2) — the user already hears both boundaries and a
    /// third cloud voice would duplicate information. Visual-only fills in price-pane
    /// indicators where the fill is a cosmetic background for a band the user isn't
    /// expected to navigate individually (e.g. Bollinger Band fill).
    ///
    /// Cloud voices fire only in <c>AudioSequencer.StartMultiSeriesPlaybackAsync</c> —
    /// not during navigation and not during Series/Component scope playback.
    /// </summary>
    public record CloudSonificationConfig(
        float BullishFrequency,     // Hz when upper >= lower (bullish cloud)
        float BearishFrequency,     // Hz when lower > upper (bearish cloud)
        string SoundPatchId,        // e.g. "sine_bell" — patch identifier for the cloud voice
        int DecayMs,                // voice duration per bar in milliseconds
        float MaxVolume = 0.85f     // volume when cloud is at maximum thickness in the viewport
    );

    /// <summary>
    /// Defines a cloud/ribbon fill between two named components of the same series.
    /// Stored in <see cref="SeriesConfig.CloudFills"/> — separate from Components so fills
    /// are purely visual and do not appear in navigation, sonification, or the component tree.
    /// </summary>
    public class CloudFillConfig
    {
        /// <summary>Name of the component whose data forms the upper boundary.</summary>
        public string UpperComponentName { get; set; } = "";
        /// <summary>Name of the component whose data forms the lower boundary.</summary>
        public string LowerComponentName { get; set; } = "";
        /// <summary>Fill color when upper boundary is above (or equal to) lower boundary (bullish).</summary>
        public string BullishColorHex { get; set; } = "#26A69A";
        /// <summary>Fill color when lower boundary is above upper boundary (bearish).</summary>
        public string BearishColorHex { get; set; } = "#EF5350";
        /// <summary>Whether this fill is currently rendered. Toggled via Properties → Appearance.</summary>
        public bool IsVisible { get; set; } = true;
        /// <summary>Label shown in Properties → Appearance (e.g. "WT Fill", "EMA Fill").</summary>
        public string DisplayName { get; set; } = "";
        /// <summary>
        /// Optional audio configuration for Chart-scope playback.
        /// When null, no sound is produced for this cloud fill (default / existing behavior).
        /// </summary>
        public CloudSonificationConfig? Sonification { get; init; }

        public CloudFillConfig Clone() => new CloudFillConfig
        {
            UpperComponentName = UpperComponentName,
            LowerComponentName = LowerComponentName,
            BullishColorHex    = BullishColorHex,
            BearishColorHex    = BearishColorHex,
            IsVisible          = IsVisible,
            DisplayName        = DisplayName,
            Sonification       = Sonification,
        };
    }
}
