using System;

namespace AccessibleTrader.Core.Services.Trading
{
    /// <summary>
    /// What the quick-trade system is currently holding.
    ///
    /// <para>
    /// Modelled on <c>DrawingInteractionManager</c>'s anchor state machine, deliberately. Placing a
    /// trade from the chart is the same interaction as placing a two-anchor drawing: arm a tool,
    /// move the cursor to a bar, commit — and the hard-won part of that machinery is not the
    /// geometry, it is the discipline that a half-finished placement is always cancellable, always
    /// announced, and never leaves invisible state behind. Those are exactly the properties an
    /// armed order needs, and they matter far more here because the artefact is money rather than
    /// a line.
    /// </para>
    /// </summary>
    public enum QuickTradeStage
    {
        /// <summary>Nothing armed. Every trading hotkey except the arm keys is inert.</summary>
        Idle,

        /// <summary>
        /// A risk percentage is chosen and the system is waiting for a stop.
        ///
        /// <para>
        /// This stage exists because <b>a risk percentage does not define a position size</b>. "Risk
        /// 1%" is meaningless until the distance to the stop is known — that distance is the divisor.
        /// Arming without a stop and letting the user place a market order would either guess the
        /// size or silently size from equity alone, and both are how an accessible interface
        /// quietly becomes a dangerous one.
        /// </para>
        /// </summary>
        AwaitingStop,

        /// <summary>
        /// Risk and stop are both known, so the size is computed and the order can be placed.
        /// </summary>
        Ready,
    }

    /// <summary>
    /// An armed quick trade: the risk budget, the stop, and everything derived from them.
    ///
    /// <para>
    /// Immutable and recomputed rather than mutated, so the announcement can never describe a state
    /// that has already moved on — the same reason the workspace state is a record.
    /// </para>
    /// </summary>
    public sealed record QuickTradeState(
        QuickTradeStage Stage,
        double RiskPercent,
        double AccountEquity,
        double? StopPrice,
        double? EntryPrice,
        bool IsLong)
    {
        public static QuickTradeState Idle { get; } =
            new(QuickTradeStage.Idle, 0, 0, null, null, IsLong: true);

        /// <summary>The cash the trade is allowed to lose. Risk percent of equity, nothing else.</summary>
        public double RiskCash => AccountEquity * RiskPercent / 100.0;

        /// <summary>
        /// Distance from entry to stop, per unit. The denominator of the whole calculation.
        /// </summary>
        public double? StopDistance =>
            StopPrice.HasValue && EntryPrice.HasValue && EntryPrice.Value > 0
                ? Math.Abs(EntryPrice.Value - StopPrice.Value)
                : null;

        /// <summary>
        /// Position size in units of the instrument: risk cash divided by the per-unit loss.
        ///
        /// <para>
        /// Null rather than zero when the stop distance is unknown or zero. A stop AT the entry
        /// implies infinite size, and returning a number there — any number — would be the single
        /// most dangerous rounding this application could perform.
        /// </para>
        /// </summary>
        public double? PositionSize
        {
            get
            {
                double? d = StopDistance;
                if (!d.HasValue || d.Value <= 0) return null;
                double size = RiskCash / d.Value;
                return double.IsFinite(size) && size > 0 ? size : null;
            }
        }

        /// <summary>
        /// True when everything needed to place an order is present and finite. The order path
        /// checks this rather than re-deriving the conditions, so there is one definition of ready.
        /// </summary>
        public bool CanPlace =>
            Stage == QuickTradeStage.Ready && PositionSize.HasValue && EntryPrice is > 0;
    }
}
