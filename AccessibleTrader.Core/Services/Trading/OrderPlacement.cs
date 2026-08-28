namespace AccessibleTrader.Core.Services.Trading
{
    /// <summary>
    /// What actually happened to an order submission, as a closed set of cases a caller must
    /// choose between.
    ///
    /// <para>
    /// The distinction that matters most is not success/failure — it is <b>is something live on
    /// the venue?</b> <see cref="Placed"/>, <see cref="Accepted"/> and <see cref="Uncertain"/> all
    /// mean "there may be a position"; <see cref="Rejected"/>, <see cref="Duplicate"/> and
    /// <see cref="Unavailable"/> all mean "nothing was sent". A vocabulary that collapses those
    /// into a bool is how a suppressed duplicate came to be announced as "Order placed".
    /// </para>
    /// </summary>
    public enum OrderOutcome
    {
        /// <summary>The venue took the order and returned an id. The only outcome with a pollable id.</summary>
        Placed,

        /// <summary>
        /// The venue took the order but gave no id back (<c>ORDER_SUBMITTED</c>). The order is
        /// live and it is NOT a failure — but nothing can poll it, so its fill will be silent
        /// unless the provider streams order events.
        /// </summary>
        Accepted,

        /// <summary>The order was refused. Nothing is on the venue.</summary>
        Rejected,

        /// <summary>
        /// The terminal's dedup gate refused a re-submit of an order sent moments ago — the
        /// screen-reader double-Enter case. Nothing new was sent; the FIRST order may well be live.
        /// </summary>
        Duplicate,

        /// <summary>The provider could not be asked at all: not configured, not connected, not a trading venue.</summary>
        Unavailable,

        /// <summary>
        /// The submit threw and a matching order was then found on the venue. Most likely live.
        /// Announcing this as a failure is how the same position gets opened twice.
        /// </summary>
        Uncertain,
    }

    /// <summary>
    /// The wire vocabulary <c>ITradingProvider.PlaceOrderAsync</c> and
    /// <c>GeneralOrderService</c> speak in. Documented for provider authors in
    /// PROVIDER_AUTHORING §14; named here so the recogniser and the producers cannot drift.
    /// </summary>
    public static class OrderCodes
    {
        /// <summary>Both prefixes are RESERVED for non-id results. A venue order id must never start with either.</summary>
        public const string OrderPrefix    = "ORDER_";
        public const string ProviderPrefix = "PROVIDER_";

        /// <summary><c>ORDER_FAILED:&lt;reason&gt;</c> — everything after the colon is spoken.</summary>
        public const string Failed = "ORDER_FAILED";

        /// <summary>Venue accepted, no id came back. A success, not a failure.</summary>
        public const string Submitted = "ORDER_SUBMITTED";

        /// <summary><c>ORDER_UNCERTAIN:&lt;id&gt;</c> — sent, reply lost, a match found afterwards.</summary>
        public const string Uncertain = "ORDER_UNCERTAIN";

        public const string RejectedQuantity   = "ORDER_REJECTED_QUANTITY";
        public const string RejectedPrice      = "ORDER_REJECTED_PRICE";
        public const string DuplicateSuppressed = "ORDER_DUPLICATE_SUPPRESSED";

        /// <summary>The <c>PROVIDER_NOT_*</c> family: CONFIGURED, CONNECTED, SUPPORTED, bare or <c>CODE:reason</c>.</summary>
        public const string NotAvailablePrefix = "PROVIDER_NOT";
    }

    /// <summary>
    /// The answer to "did the order go?", parsed once and read by every caller.
    ///
    /// <para>
    /// <b>Why this type exists.</b> Placement used to answer with a bare <c>string</c> and each
    /// caller recognised failure for itself. Three non-equivalent recognisers grew up around it:
    /// <c>GeneralOrderService.IsErrorSentinel</c> (any <c>ORDER_</c>/<c>PROVIDER_</c> prefix),
    /// <c>OrderResult.DescribeFailure</c> (an exact-match list) and the trading dashboard's
    /// <c>StartsWith("ORDER_FAILED") || StartsWith("PROVIDER_NOT")</c> pair. They disagreed, and
    /// every disagreement was audible: <c>ORDER_DUPLICATE_SUPPRESSED</c> matched none of the
    /// dashboard's prefixes, so the routine screen-reader double-Enter announced "Order placed"
    /// for an order that was never sent, while <c>ORDER_SUBMITTED</c> — a success — matched
    /// <c>IsErrorSentinel</c> and silently disabled bracket verification and fill polling.
    /// </para>
    ///
    /// <para>
    /// The string protocol survives at the plugin boundary, because twelve provider plugins
    /// implement it and it is the documented contract. It is parsed exactly once, here, at the edge of Core, and
    /// nothing above <c>GeneralOrderService</c> sees a status string again.
    /// </para>
    /// </summary>
    /// <param name="Outcome">Which of the six cases this is.</param>
    /// <param name="Raw">The wire value, verbatim, for logs and for diagnosis.</param>
    /// <param name="OrderId">
    /// The venue's id, or null when there is none to poll — every outcome except
    /// <see cref="OrderOutcome.Placed"/> and a resolved <see cref="OrderOutcome.Uncertain"/>.
    /// </param>
    /// <param name="FailureMessage">
    /// A spoken-ready sentence, present for every outcome except <see cref="OrderOutcome.Placed"/>
    /// and <see cref="OrderOutcome.Accepted"/>. Never null when <see cref="Succeeded"/> is false,
    /// so no caller can be left announcing a dangling half-sentence.
    /// </param>
    public sealed record OrderPlacement(
        OrderOutcome Outcome,
        string Raw,
        string? OrderId,
        string? FailureMessage)
    {
        /// <summary>
        /// True when the venue took the order. <b>This is the gate on saying "Order placed"</b> —
        /// and it deliberately includes <see cref="OrderOutcome.Accepted"/> (live, just unpollable)
        /// and deliberately excludes <see cref="OrderOutcome.Uncertain"/> (which needs its own
        /// words, not a confirmation).
        /// </summary>
        public bool Succeeded => Outcome is OrderOutcome.Placed or OrderOutcome.Accepted;

        /// <summary>
        /// True when there is an id worth polling or cancelling. Narrower than
        /// <see cref="Succeeded"/>: an <see cref="OrderOutcome.Accepted"/> order is live with
        /// nothing to poll, and handing "ORDER_SUBMITTED" to a status poller as an id is how the
        /// poller came to be fed garbage.
        /// </summary>
        public bool HasOrderId => OrderId is { Length: > 0 };

        /// <summary>
        /// True when the order may or may not be live and the user must check before retrying.
        /// Callers must not announce this as either a fill or a refusal.
        /// </summary>
        public bool NeedsVerification => Outcome == OrderOutcome.Uncertain;

        /// <summary>
        /// What to say when <see cref="Succeeded"/> is false.
        ///
        /// <para>
        /// <paramref name="headline"/> is the caller's own framing ("Order rejected for BTC/USD.",
        /// "Close failed for BTC/USD.") and is DROPPED for
        /// <see cref="OrderOutcome.Uncertain"/> — an order that probably went through must not be
        /// announced under a headline that says it failed, because the trader's next action is to
        /// place it again. A caller whose headline carries information the user still needs in
        /// that case (which strategy, which position) supplies a neutral
        /// <paramref name="uncertainHeadline"/> instead of losing it.
        /// </para>
        /// </summary>
        public string RefusalAnnouncement(string headline, string? uncertainHeadline = null) =>
            NeedsVerification
                ? (uncertainHeadline is { Length: > 0 } u ? $"{u} {FailureMessage}" : FailureMessage!)
                : $"{headline} {FailureMessage ?? "No reason was given."}";

        /// <summary>
        /// The single recogniser. Turns the wire string into an outcome and, where the outcome is
        /// not a success, into a sentence worth hearing.
        ///
        /// <para>
        /// The codes are terse and mostly meant for logs, so each is translated into what the user
        /// should do about it. "ORDER_FAILED:insufficient paper balance" is true but unhelpful;
        /// what a person needs to know is that a risk-based size on a tight stop asks for more
        /// notional than the account holds, and that a wider stop is the fix.
        /// </para>
        ///
        /// <para>
        /// <b>The fallback arm is the load-bearing one.</b> Anything carrying a reserved prefix
        /// that this method does not recognise is a REFUSAL, not an order id. The old exact-match
        /// recogniser returned "it went" for those, so a provider that invented a code — or a
        /// future code this build predates — announced success for an order nobody sent.
        /// </para>
        /// </summary>
        public static OrderPlacement Parse(string? raw)
        {
            string s = raw ?? "";

            if (string.IsNullOrWhiteSpace(s))
                return new OrderPlacement(OrderOutcome.Rejected, s, null,
                    "The order was not placed — the provider returned nothing.");

            if (s.StartsWith(OrderCodes.Failed + ":", StringComparison.OrdinalIgnoreCase))
            {
                string reason = s.Substring(OrderCodes.Failed.Length + 1).Trim();

                string message =
                    reason.Length == 0
                        ? "Not placed: the provider rejected the order."
                    : reason.Contains("insufficient", StringComparison.OrdinalIgnoreCase)
                        ? "Not placed: that position costs more than the account holds. "
                          + "A risk-based size grows as the stop gets tighter, so choose a stop further away."
                        : "Not placed: " + reason + ".";

                return new OrderPlacement(OrderOutcome.Rejected, s, null, message);
            }

            // ORDER_UNCERTAIN:{id} — the submit threw AND a matching order was found on the
            // exchange afterwards. The old switch fell through to its default arm ("an order id —
            // it went"), so the one code that means "verify before retrying" was the one code
            // every caller treated as a clean success and said nothing about. It is NOT phrased as
            // "not placed": the order most likely IS live, and telling a user it failed is how the
            // same position gets opened twice.
            if (s.StartsWith(OrderCodes.Uncertain, StringComparison.OrdinalIgnoreCase))
            {
                int idAt = s.IndexOf(':');
                string id = idAt >= 0 ? s[(idAt + 1)..].Trim() : "";
                return new OrderPlacement(OrderOutcome.Uncertain, s, id.Length > 0 ? id : null,
                    "Uncertain: the order was sent, the reply was lost, and a matching order "
                    + (id.Length > 0 ? $"({id}) " : "")
                    + "was found on the exchange. Check your open orders and positions before placing it again.");
            }

            // The venue-accepted-but-no-id fallback nine providers share. A SUCCESS — the caveat
            // (that its fill cannot be announced) is GeneralOrderService's to speak, because only
            // it knows whether the provider streams order events.
            if (s.Equals(OrderCodes.Submitted, StringComparison.OrdinalIgnoreCase))
                return new OrderPlacement(OrderOutcome.Accepted, s, null, null);

            // The PROVIDER_NOT_* family, which carries its reason the same way. These used to be
            // bare codes and now arrive as CODE:reason, so both shapes are handled — a build where
            // one side has been updated and the other has not must still say something usable.
            if (s.StartsWith(OrderCodes.NotAvailablePrefix, StringComparison.OrdinalIgnoreCase))
            {
                int colon = s.IndexOf(':');
                string reason = colon >= 0 ? s[(colon + 1)..].Trim() : "";
                return new OrderPlacement(OrderOutcome.Unavailable, s, null,
                    reason.Length > 0
                        ? "Not placed: " + reason + "."
                        : "Not placed: that provider is not available for trading.");
            }

            if (s.Equals(OrderCodes.DuplicateSuppressed, StringComparison.OrdinalIgnoreCase))
                return new OrderPlacement(OrderOutcome.Duplicate, s, null,
                    "Not placed: that looked like a duplicate of an order just sent. "
                    + "Check your open orders before placing it again.");

            if (s.Equals(OrderCodes.RejectedQuantity, StringComparison.OrdinalIgnoreCase))
                return new OrderPlacement(OrderOutcome.Rejected, s, null,
                    "Not placed: the position size is outside the allowed range.");

            if (s.Equals(OrderCodes.RejectedPrice, StringComparison.OrdinalIgnoreCase))
                return new OrderPlacement(OrderOutcome.Rejected, s, null,
                    "Not placed: the limit price was not usable.");

            if (s.Equals(OrderCodes.Failed, StringComparison.OrdinalIgnoreCase))
                return new OrderPlacement(OrderOutcome.Rejected, s, null,
                    "Not placed: the provider rejected the order.");

            // Reserved prefix, unrecognised code. Refusal by default — see the summary. Naming the
            // code in the spoken sentence is deliberate: the user cannot act on it, but they can
            // repeat it, and a wrong-looking word in a trading announcement is worth reporting.
            if (s.StartsWith(OrderCodes.OrderPrefix, StringComparison.Ordinal)
             || s.StartsWith(OrderCodes.ProviderPrefix, StringComparison.Ordinal))
                return new OrderPlacement(OrderOutcome.Rejected, s, null,
                    $"Not placed: the provider answered with a status this build does not recognise ({s}). "
                    + "Check your open orders before placing it again.");

            // An order id — it went.
            return new OrderPlacement(OrderOutcome.Placed, s, s, null);
        }
    }
}
