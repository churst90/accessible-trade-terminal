namespace AccessibleTrader.Sdk.Enums
{
    /// <summary>
    /// What a provider can actually do, as one vocabulary.
    ///
    /// <para>
    /// **This is what the dashboard renders from.** Every control it draws must
    /// correspond to a flag the connected provider declares; where a capability is
    /// missing the control is absent or disabled with a reason, never present and
    /// inert. A flag is therefore a promise to a user, not a note to a developer.
    /// </para>
    ///
    /// <para>
    /// **Why the vocabulary grew (2026-08-04).** The original seven flags were
    /// mostly honest — a static audit of all ten trading plugins found only two bad
    /// claims — but they were far too few to gate anything useful, which is the real
    /// reason six of the seven gated nothing in the UI. The order contract carries
    /// reduce-only, post-only, time-in-force, hedge-mode position side and
    /// cross/isolated margin, and there was no way to say whether a provider
    /// honoured any of them. Flags below were added only where something can gate on
    /// them and a provider can truthfully declare them; adding vocabulary nobody sets
    /// is the same disease in a new coat.
    /// </para>
    ///
    /// <para>
    /// **What is deliberately NOT here.** Transport traits —
    /// <c>SupportsOrderEventStreaming</c>, <c>SupportsOrderStatusQuery</c>,
    /// <c>SupportsSimultaneousStopAndTarget</c> — stay as properties on
    /// <c>ITradingProvider</c>. They describe how the order service should TALK to a
    /// provider (stream versus poll), not what the user may do, and they have no UI
    /// consumer. Mixing them in would make this enum two things at once.
    /// </para>
    ///
    /// <para>
    /// Bit values are stable: existing flags keep the positions they had, so any
    /// persisted or logged value keeps its meaning.
    /// </para>
    /// </summary>
    [Flags]
    public enum ProviderCapabilities
    {
        /// <summary>No advanced capabilities; basic spot trading only.</summary>
        None = 0,

        // ── Market data ──────────────────────────────────────────────────────

        /// <summary>
        /// Level 2 order book, via <c>IOrderBookProvider</c>. Note this is the
        /// depth-snapshot interface, NOT the bid/ask tuple that
        /// <c>IMarketDataProvider.GetOrderBookAsync</c> returns — the two methods
        /// share a name, and conflating them produced eight false audit findings.
        /// </summary>
        L2 = 1 << 0,

        /// <summary>Full market depth beyond standard L2 (e.g. full book snapshots).</summary>
        MarketDepth = 1 << 4,

        // ── Products ─────────────────────────────────────────────────────────

        /// <summary>
        /// Short selling. Requires a borrow or a derivative contract — there is no
        /// shorting on plain spot settlement, because selling an asset you do not
        /// hold means someone lent it to you.
        /// </summary>
        Shorting = 1 << 1,

        /// <summary>
        /// Margin trading on the spot book: the venue lends the asset or the quote.
        /// Replaces the old <c>SupportsMarginTrading</c> bool, which could disagree
        /// with the <see cref="Leverage"/> flag because they were separate fields.
        /// </summary>
        MarginTrading = 1 << 7,

        /// <summary>
        /// Futures or perpetual contracts — a distinct product, usually a distinct
        /// API. Replaces the old <c>SupportsFuturesTrading</c> bool.
        /// </summary>
        FuturesTrading = 1 << 8,

        // ── Order features ───────────────────────────────────────────────────

        /// <summary>
        /// One-cancels-the-other pairs. Satisfied EITHER by honouring
        /// <c>TradeSignal.OcoGroupId</c> or by implementing
        /// <c>IOcoTradingProvider</c> for a venue-native pair.
        /// </summary>
        OCO = 1 << 2,

        /// <summary>
        /// Trailing stop-loss and/or trailing take-profit, via the signal's
        /// <c>TrailStopValue</c> / <c>TrailTpValue</c> fields. The dashboard's
        /// trailing fields are gated on this, so declaring it without honouring
        /// those fields renders controls that cannot do anything.
        /// </summary>
        TrailingStop = 1 << 3,

        /// <summary>
        /// Protective legs ATTACHED to an entry — several orders from one signal, or
        /// a broker-native bracket order class. Distinct from merely accepting a
        /// standalone stop order, which is <c>SupportsStopLoss</c>.
        /// </summary>
        Brackets = 1 << 6,

        /// <summary>Honours <c>TradeSignal.ReduceOnly</c> — the order may only reduce a position.</summary>
        ReduceOnly = 1 << 9,

        /// <summary>Honours <c>TradeSignal.PostOnly</c> — maker-only limit orders.</summary>
        PostOnly = 1 << 10,

        /// <summary>Honours <c>TradeSignal.TimeInForce</c> — GTC / IOC / FOK / Day / GTD.</summary>
        TimeInForce = 1 << 11,

        // ── Margin controls ──────────────────────────────────────────────────

        /// <summary>
        /// A leverage multiplier can be set, either per order via
        /// <c>TradeSignal.Leverage</c> or per session via <c>SetLeverageAsync</c>.
        /// Implies <see cref="MarginTrading"/> or <see cref="FuturesTrading"/> —
        /// there must be a product for the leverage to apply to — and requires
        /// <c>MaxLeverage</c> above 1.
        /// </summary>
        Leverage = 1 << 5,

        /// <summary>
        /// Honours <c>TradeSignal.MarginType</c>: isolated margin caps loss at the
        /// position's own collateral, cross draws on the whole balance. Different
        /// liquidation maths, so it is a real user-facing choice.
        /// </summary>
        IsolatedMargin = 1 << 12,

        /// <summary>
        /// Hedge mode — honours <c>TradeSignal.PositionSide</c>, allowing a
        /// simultaneous long and short in one symbol.
        /// </summary>
        HedgeMode = 1 << 13,

        // ── Wallet ───────────────────────────────────────────────────────────

        /// <summary>
        /// Deposit addresses and deposit history can be read. See
        /// <c>docs/WALLET_AND_PORTFOLIO_DESIGN.md</c> — the address path is gated on
        /// an <c>IWalletProvider</c> interface as well, because for a feature whose
        /// failure mode is showing someone the wrong address to send money to, a
        /// compiler-enforced fact beats a hand-written flag.
        /// </summary>
        DepositAddresses = 1 << 14,

        /// <summary>
        /// Funds can be withdrawn through the API. Requires a separate,
        /// withdrawal-enabled credential; the terminal's ordinary trading keys must
        /// never be able to move money.
        /// </summary>
        Withdrawals = 1 << 15,
    }
}
