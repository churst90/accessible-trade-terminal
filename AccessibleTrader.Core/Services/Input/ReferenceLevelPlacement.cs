using System;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Input
{
    /// <summary>
    /// Decides where the <c>0</c> shortcut puts a new reference level.
    ///
    /// <para>
    /// ── The defect this exists to prevent ──────────────────────────────────────
    /// The command used to add a level at <b>literal zero</b> on whatever series held focus, and it
    /// was named "Zero line" because it was written for oscillators, where a zero crossing is the
    /// thing you want a line at. Press the same key with the <i>price</i> series focused and you get
    /// a level at 0 on a chart trading near 64,000.
    /// </para>
    ///
    /// <para>
    /// That is not merely useless. <c>ViewportRangeCalculator</c> expands the price range to cover
    /// every visible level on a main-pane series, so a level at zero drags the y-axis to the origin
    /// and compresses all price action into the top sliver of the pane. The level persists in the
    /// workspace, so the chart comes back broken at every launch — which is exactly how it was
    /// found: a maintainer screenshot of a BTC 4h chart whose axis ran 0 → 70,000, with the offending
    /// entry still sitting in <c>__last-session__.json</c> as
    /// <c>{"Name":"Zero","Value":0.0,"IsVisible":true}</c> on the <c>CANDLES</c> series.
    /// </para>
    ///
    /// <para>
    /// ── The rule ───────────────────────────────────────────────────────────────
    /// A reference level must be in the units of the pane it lands on. On an oscillator pane the
    /// meaningful constant is zero. On a price pane there is no meaningful constant at all — so the
    /// level goes at <b>the price under the cursor</b>, which is both well-defined and what someone
    /// pressing "mark a level" on a price chart actually wants.
    /// </para>
    /// </summary>
    public static class ReferenceLevelPlacement
    {
        /// <summary>Panes whose values are prices, and therefore have no meaningful zero.</summary>
        public static bool IsPricePane(string? pane) =>
            string.IsNullOrEmpty(pane) || pane.Equals("Main", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The level to add, or <c>null</c> when one cannot be placed.
        ///
        /// <para>
        /// Returning null matters as much as returning a level. A key that silently does nothing is
        /// indistinguishable from a key that is not bound, so the caller must speak the reason —
        /// which is why <paramref name="reason"/> is always set.
        /// </para>
        /// </summary>
        /// <param name="pane">The focused series' pane.</param>
        /// <param name="cursorPrice">
        /// Close of the bar under the cursor, or NaN when there is no data. Only consulted for a
        /// price pane.
        /// </param>
        /// <param name="reason">What to say — the confirmation, or why nothing happened.</param>
        public static LevelConfig? For(string? pane, double cursorPrice, out string reason)
        {
            if (!IsPricePane(pane))
            {
                reason = "Zero line added";
                return new LevelConfig
                {
                    Name = "Zero",
                    Value = 0,
                    ColorHex = "#888888",
                    DashStyle = DashStyle.Dash,
                    IsVisible = true
                };
            }

            // A price pane. Zero is never the answer here; the cursor's price is.
            if (!double.IsFinite(cursorPrice) || cursorPrice <= 0)
            {
                reason = "No price under the cursor to place a level at.";
                return null;
            }

            reason = $"Level added at {cursorPrice:0.########}";
            return new LevelConfig
            {
                Name = "Level",
                Value = cursorPrice,
                ColorHex = "#888888",
                DashStyle = DashStyle.Dash,
                IsVisible = true
            };
        }
    }
}
