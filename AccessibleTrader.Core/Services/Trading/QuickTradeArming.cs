namespace AccessibleTrader.Core.Services.Trading
{
    /// <summary>
    /// What the risk percentage is a percentage <i>of</i>.
    ///
    /// <para>
    /// These are two established and genuinely different ways to size a trade, and the terminal
    /// supported only one of them while calling it "percent", which is ambiguous between them.
    /// </para>
    /// </summary>
    public enum QuickTradeSizingMode
    {
        /// <summary>
        /// <b>The percentage is the position's value.</b> 0.5% of a $100,000 account buys $500 worth
        /// of the instrument, whatever the stop is. This is how an exchange order ticket behaves —
        /// you say how much you want to buy — and it is what most people mean by "half a percent".
        ///
        /// <para>
        /// The stop is then purely protective, and what you stand to lose depends on where you put
        /// it: a stop 1% away loses $5 of that $500, a stop 20% away loses $100.
        /// </para>
        /// </summary>
        PositionValue = 0,

        /// <summary>
        /// <b>The percentage is what you lose if the stop is hit.</b> Size is risk divided by stop
        /// distance, so the position grows as the stop tightens. Risking 0.5% of $100,000 with a
        /// stop $700 below a $64,000 entry buys 0.714 BTC — a $45,700 position that loses exactly
        /// $500.
        ///
        /// <para>
        /// This is the sizing the "1% rule" of risk management actually refers to, and it makes every
        /// trade's downside identical regardless of instrument or volatility. It is also the one that
        /// surprises people, because the position value is not the number they named and can be many
        /// times the account on a tight stop.
        /// </para>
        /// </summary>
        RiskAtStop = 1,
    }

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
        bool IsLong,
        QuickTradeSizingMode SizingMode = QuickTradeSizingMode.PositionValue)
    {
        public static QuickTradeState Idle { get; } =
            new(QuickTradeStage.Idle, 0, 0, null, null, IsLong: true);

        /// <summary>
        /// The cash the percentage refers to. What it BUYS in
        /// <see cref="QuickTradeSizingMode.PositionValue"/>; what it can LOSE in
        /// <see cref="QuickTradeSizingMode.RiskAtStop"/>.
        /// </summary>
        public double RiskCash => AccountEquity * RiskPercent / 100.0;

        /// <summary>
        /// What the position is worth — quantity times entry. In position-value mode this is the
        /// budget by construction; in risk mode it is the number that surprises people.
        /// </summary>
        public double? Notional =>
            PositionSize is double q && EntryPrice is double e && q > 0 && e > 0 ? q * e : null;

        /// <summary>
        /// What is actually lost if the stop is hit — quantity times stop distance.
        ///
        /// <para>
        /// In risk mode this equals <see cref="RiskCash"/> by construction. In position-value mode it
        /// is derived, and it is the figure that matters: a $500 position with a stop 1% away risks
        /// $5, and with a stop 20% away risks $100. Announcing it is how the mode stays honest about
        /// what it is not controlling.
        /// </para>
        /// </summary>
        public double? RiskAtStop =>
            PositionSize is double q && StopDistance is double d && q > 0 && d > 0 ? q * d : null;

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
                if (SizingMode == QuickTradeSizingMode.PositionValue)
                {
                    // The budget IS the position's value, so the quantity is simply what that buys.
                    // The stop does not enter into it — it decides the loss, not the size.
                    if (EntryPrice is not double px || px <= 0) return null;
                    double units = RiskCash / px;
                    return double.IsFinite(units) && units > 0 ? units : null;
                }

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
