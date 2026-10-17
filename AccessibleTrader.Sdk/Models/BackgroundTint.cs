namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// Where a component shades the chart background, bar by bar, from its own value — one colour
    /// where the value is at or above its tint midline, another where it is below (Cody,
    /// 2026-10-17: <i>"shading/tinting should be something any indicator can do"</i>).
    ///
    /// <para>
    /// Visual only: a tint is never spoken, sounded or narrated. It is the component's own data
    /// drawn a second way, so it is a switch of its own and does NOT follow the component's
    /// <see cref="ComponentConfig.IsVisible"/> — Cipher B's Anchor Polarity is a hidden data line
    /// that can still tint, and hiding a line must not take a tint the user asked for with it.
    /// See <see cref="ComponentConfig.BackgroundTint"/> for the rest of the shape.
    /// </para>
    ///
    /// <para>
    /// <b>APPEND-ONLY.</b> Saved workspaces store the ORDINAL (the same rule as
    /// <see cref="ComponentDisplayType"/>): add at the end, never reorder or delete.
    /// </para>
    /// </summary>
    public enum BackgroundTint
    {
        /// <summary>No tint. The default for every component that does not declare one.</summary>
        Off,
        /// <summary>The pane (or sub-pane strip) the component itself is drawn in.</summary>
        ThisPane,
        /// <summary>The price chart (the "Main" pane), whichever pane the component is drawn in.</summary>
        PriceChart,
    }
}
