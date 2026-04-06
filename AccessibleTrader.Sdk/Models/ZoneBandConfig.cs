namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// Defines a thin horizontal band rendered around a single component's carry-forward level value.
    /// Used for S/R zone shading — each active level gets its own narrow colored band (e.g. ±0.3% of price).
    /// Stored in <see cref="SeriesConfig.ZoneBands"/> — purely visual, not navigable or audible.
    /// </summary>
    public class ZoneBandConfig
    {
        /// <summary>Name of the component whose carry-forward value is the band centre price.</summary>
        public string ComponentName { get; set; } = "";
        /// <summary>Band fill color in #RRGGBB or #AARRGGBB (SkiaSharp alpha-first 8-hex) format.</summary>
        public string ColorHex { get; set; } = "#50808080";
        /// <summary>
        /// Half-width of the band expressed as a percentage of the centre price.
        /// E.g. 0.3 means the band spans price × (1 ± 0.003), giving a ±0.3% zone around the level.
        /// </summary>
        public float BandWidthPct { get; set; } = 0.3f;
        /// <summary>Whether this band is currently rendered.</summary>
        public bool IsVisible { get; set; } = true;
        /// <summary>Label shown in Properties → Appearance.</summary>
        public string DisplayName { get; set; } = "";

        public ZoneBandConfig Clone() => new ZoneBandConfig
        {
            ComponentName = ComponentName,
            ColorHex      = ColorHex,
            BandWidthPct  = BandWidthPct,
            IsVisible     = IsVisible,
            DisplayName   = DisplayName,
        };
    }
}
