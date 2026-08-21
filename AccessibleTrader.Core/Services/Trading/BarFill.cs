using System;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Core.Services.Trading
{
    /// <summary>
    /// Where an order that rests at a price actually fills when a bar reaches it.
    ///
    /// <para>
    /// ── The rule ───────────────────────────────────────────────────────────────
    /// **An order fills at its stated level only if the market was ever at that level.**
    /// If the order was already executable at the bar's OPEN, the market never came to the
    /// level — the level was already behind it — so the fill happens at the open.
    /// </para>
    ///
    /// <para>
    /// ── Why it is one function ─────────────────────────────────────────────────
    /// Ignoring this is a single mistake that wears four different faces, and each face was
    /// written, reviewed and believed independently: a buy stop below the market that fills
    /// below the market (free money), a buy limit above the market that fills above it (the
    /// user overcharged), a stop the price gapped through that books the loss at the stop it
    /// skipped (a backtest that flatters every strategy it runs), and a target gapped past
    /// that books less than the gap gave. Same missing question — *where was the market?* —
    /// four sites, opposite signs. It is written once here so the next fill site inherits the
    /// answer instead of guessing at it.
    /// </para>
    ///
    /// <para>
    /// The gap case is not a rounding detail. A stop that fills at its level regardless of
    /// where the market opened is a simulator quietly promising that a stop caps a loss
    /// exactly, which is the single most expensive thing a paper account can teach.
    /// </para>
    /// </summary>
    public static class BarFill
    {
        /// <summary>
        /// The fill price for a resting order that this bar has crossed.
        /// </summary>
        /// <param name="level">The order's trigger or limit price.</param>
        /// <param name="barOpen">The open of the bar the fill happens on.</param>
        /// <param name="orderSide">The side of the ORDER being filled, not of any position.</param>
        /// <param name="stopLike">
        /// True for orders that trigger when price moves <i>through</i> the level in the
        /// unfavourable direction — stops, and trailing exits of either label. False for
        /// limit-like orders, which execute at the level <i>or better</i>.
        /// </param>
        public static double Price(double level, double barOpen, OrderSide orderSide, bool stopLike)
        {
            if (!double.IsFinite(level) || level <= 0 || !double.IsFinite(barOpen) || barOpen <= 0)
                return level;

            bool executableAtOpen = stopLike
                ? (orderSide == OrderSide.Buy ? barOpen >= level : barOpen <= level)
                : (orderSide == OrderSide.Buy ? barOpen <= level : barOpen >= level);

            return executableAtOpen ? barOpen : level;
        }

        /// <summary>
        /// A stop exit, expressed by the side of the POSITION it protects — the form a
        /// backtester has to hand. A long is stopped out by a sell, so the sides invert.
        /// </summary>
        public static double StopExit(double stop, double barOpen, OrderSide positionSide) =>
            Price(stop, barOpen, Opposite(positionSide), stopLike: true);

        /// <summary>
        /// A target exit, expressed by the side of the POSITION it closes.
        /// </summary>
        public static double TargetExit(double target, double barOpen, OrderSide positionSide) =>
            Price(target, barOpen, Opposite(positionSide), stopLike: false);

        private static OrderSide Opposite(OrderSide side) =>
            side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
    }
}
