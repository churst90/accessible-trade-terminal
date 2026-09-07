namespace AccessibleTrader.Sdk.Plugins
{
    /// <summary>
    /// The venue's verdict on an order that was NOT placed.
    /// </summary>
    /// <param name="Accepted">True when the venue validated the order as placeable.</param>
    /// <param name="Message">The venue's own words: the rejection reason, or its description of
    /// the order it would have placed.</param>
    public sealed record OrderDryRunResult(bool Accepted, string Message);

    /// <summary>
    /// Optional capability: <b>validate an order against the REAL venue without placing it.</b>
    ///
    /// <para>
    /// ── Why this exists ───────────────────────────────────────────────────────
    /// Nine order-construction defects across eight venues (see
    /// <c>docs/PROVIDER_CONFORMANCE_SCOPE.md</c>) were all the wrong value in the wrong field of
    /// an outgoing request, and every one was found after the fact. Several venues offer a
    /// dry-run form of their order endpoint — Kraken's <c>AddOrder validate=true</c>, Binance's
    /// <c>/api/v3/order/test</c>, Tradier's <c>preview=true</c> — which runs the venue's own
    /// validation over the real payload and places nothing. Until 2026-09-07 no plugin used any
    /// of them. They are the only way to check a payload against a live venue with no sandbox,
    /// no funded account and no risk.
    /// </para>
    ///
    /// <para>
    /// ── The contract ──────────────────────────────────────────────────────────
    /// An implementation MUST build the dry-run request from the SAME code path that
    /// <see cref="ITradingProvider.PlaceOrderAsync"/> uses, differing only by the venue's
    /// validate flag or endpoint. A dry run that constructs its own payload validates nothing
    /// about the order that would actually be sent. The provider conformance suite asserts the
    /// two payloads are identical apart from that flag.
    /// </para>
    ///
    /// <para>
    /// A dry run never returns an order id. A transport or credential failure THROWS, so a caller
    /// cannot mistake "could not ask" for "the venue said no".
    /// </para>
    /// </summary>
    public interface IOrderDryRunProvider
    {
        Task<OrderDryRunResult> DryRunOrderAsync(TradeSignal signal);
    }
}
