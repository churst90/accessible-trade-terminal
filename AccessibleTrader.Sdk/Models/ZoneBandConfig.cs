namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// Defines a horizontal coloured band rendered in the pane background. Two modes:
    ///
    /// 1. Component-centred (S/R zones, legacy): set <see cref="ComponentName"/> and
    ///    <see cref="BandWidthPct"/>. Band follows the component's carry-forward level
    ///    value with half-width expressed as a percentage of price.
    /// 2. Fixed-value (OB/OS zones, oscillator panes): leave <see cref="ComponentName"/>
    ///    empty and set <see cref="FixedTop"/> and <see cref="FixedBottom"/> to absolute
    ///    Y values in pane coordinates. Band spans the full viewport width between those
    ///    two values.
    ///
    /// Stored in <see cref="SeriesConfig.ZoneBands"/> — purely visual, not navigable or audible.
    /// </summary>
    public class ZoneBandConfig
    {
        /// <summary>Name of the component whose carry-forward value is the band centre price. Empty in fixed mode.</summary>
        public string ComponentName { get; set; } = "";
        /// <summary>Band fill color in #RRGGBB or #AARRGGBB (SkiaSharp alpha-first 8-hex) format.</summary>
        public string ColorHex { get; set; } = "#50808080";
        /// <summary>
        /// Half-width of the band expressed as a percentage of the centre price.
        /// Only used when <see cref="ComponentName"/> is set (component-centred mode).
        /// </summary>
        public float BandWidthPct { get; set; } = 0.3f;
        /// <summary>Top Y value of the band in pane coordinates. NaN = not set (component mode).</summary>
        public double FixedTop { get; set; } = double.NaN;
        /// <summary>Bottom Y value of the band in pane coordinates. NaN = not set (component mode).</summary>
        public double FixedBottom { get; set; } = double.NaN;
        /// <summary>Whether this band is currently rendered.</summary>
        public bool IsVisible { get; set; } = true;
        /// <summary>Label shown in Properties → Appearance.</summary>
        public string DisplayName { get; set; } = "";

        /// <summary>True when both <see cref="FixedTop"/> and <see cref="FixedBottom"/> are set to real values.</summary>
        public bool IsFixedMode =>
            !double.IsNaN(FixedTop) && !double.IsNaN(FixedBottom);

        public ZoneBandConfig Clone() => new ZoneBandConfig
        {
            ComponentName = ComponentName,
            ColorHex      = ColorHex,
            BandWidthPct  = BandWidthPct,
            FixedTop      = FixedTop,
            FixedBottom   = FixedBottom,
            IsVisible     = IsVisible,
            DisplayName   = DisplayName,
        };
    }
}
