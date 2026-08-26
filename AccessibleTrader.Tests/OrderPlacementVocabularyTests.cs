using AccessibleTrader.Core.Services.Trading;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The order-outcome vocabulary, which until now had no test at all.
    ///
    /// <para>
    /// <b>Why this file exists.</b> The A2 sabotage audit (2026-08-26) deleted the
    /// <c>PROVIDER_</c> arm from <c>GeneralOrderService.IsErrorSentinel</c> — so a
    /// <c>PROVIDER_NOT_CONFIGURED</c> return would have been treated as a successful order id —
    /// and <b>nothing in 4,830 tests went red</b>. That mutant is the acceptance criterion for
    /// this work: the classification deciding whether a blind trader hears "Order placed" or
    /// hears what went wrong is now pinned, code by code, outcome by outcome.
    /// </para>
    ///
    /// <para>
    /// Three recognisers used to exist and they disagreed. Every disagreement was audible.
    /// <c>ORDER_DUPLICATE_SUPPRESSED</c> matched none of the dashboard's prefixes, so the routine
    /// screen-reader double-Enter announced "Order placed" for an order the dedup gate had just
    /// refused to send; <c>ORDER_SUBMITTED</c> — a success — matched <c>IsErrorSentinel</c>'s
    /// <c>ORDER_</c> prefix and silently disabled bracket verification and fill polling.
    /// </para>
    /// </summary>
    public sealed class OrderPlacementVocabularyTests
    {
        /// <summary>
        /// One sample of every reserved code, with the outcome it must produce. This table IS the
        /// protocol documented in PROVIDER_AUTHORING §14.
        /// </summary>
        public static TheoryData<string, OrderOutcome> Vocabulary => new()
        {
            { "ORDER_FAILED",                              OrderOutcome.Rejected },
            { "ORDER_FAILED:the venue said no",            OrderOutcome.Rejected },
            { "ORDER_REJECTED_QUANTITY",                   OrderOutcome.Rejected },
            { "ORDER_REJECTED_PRICE",                      OrderOutcome.Rejected },
            { "ORDER_DUPLICATE_SUPPRESSED",                OrderOutcome.Duplicate },
            { "ORDER_UNCERTAIN",                           OrderOutcome.Uncertain },
            { "ORDER_UNCERTAIN:EX-991",                    OrderOutcome.Uncertain },
            { "ORDER_SUBMITTED",                           OrderOutcome.Accepted },
            { "PROVIDER_NOT_CONFIGURED",                   OrderOutcome.Unavailable },
            { "PROVIDER_NOT_CONNECTED:Kraken is offline",  OrderOutcome.Unavailable },
            { "PROVIDER_NOT_SUPPORTED:Polygon is data only", OrderOutcome.Unavailable },
        };

        [Theory]
        [MemberData(nameof(Vocabulary))]
        public void EveryReservedCode_ParsesToItsOutcome(string code, OrderOutcome expected)
        {
            var placement = OrderPlacement.Parse(code);

            Assert.Equal(expected, placement.Outcome);
            Assert.Equal(code, placement.Raw);
        }

        /// <summary>
        /// The exhaustiveness guard. A new <see cref="OrderOutcome"/> added without a sample above
        /// is an outcome no test has ever seen a caller handle — which is exactly the state this
        /// vocabulary was in before B1.
        /// </summary>
        [Fact]
        public void EveryOutcome_HasASampleInTheTable()
        {
            var covered = Vocabulary.Select(row => (OrderOutcome)row[1]!).ToHashSet();
            covered.Add(OrderOutcome.Placed);   // sampled by the order-id tests below

            foreach (var outcome in Enum.GetValues<OrderOutcome>())
                Assert.Contains(outcome, covered);
        }

        /// <summary>
        /// A caller must never be left announcing a dangling half-sentence, and a caller must
        /// never be handed an id for something that is not on the venue.
        /// </summary>
        [Theory]
        [MemberData(nameof(Vocabulary))]
        public void ANonSuccessAlwaysSpeaks_AndCarriesNoIdItDoesNotHave(string code, OrderOutcome expected)
        {
            var placement = OrderPlacement.Parse(code);

            if (expected is OrderOutcome.Placed or OrderOutcome.Accepted)
            {
                Assert.Null(placement.FailureMessage);
                return;
            }

            Assert.False(string.IsNullOrWhiteSpace(placement.FailureMessage), $"{code} would be silent.");
            Assert.EndsWith(".", placement.FailureMessage!.TrimEnd());
            // Only ORDER_UNCERTAIN:{id} carries an id, because only it names a real order.
            if (expected != OrderOutcome.Uncertain) Assert.Null(placement.OrderId);
        }

        // ── The vacuity half: an order id is a success ────────────────────────

        [Theory]
        [InlineData("EX-1")]
        [InlineData("8891")]
        [InlineData("paper-1a2b3c4d5e6f")]
        [InlineData("c9f1e0c2-2f5c-4c2f-9d3e-000000000001")]
        public void AnOrderId_IsPlacedAndSilent(string id)
        {
            var placement = OrderPlacement.Parse(id);

            Assert.Equal(OrderOutcome.Placed, placement.Outcome);
            Assert.True(placement.Succeeded);
            Assert.True(placement.HasOrderId);
            Assert.Equal(id, placement.OrderId);
            Assert.Null(placement.FailureMessage);
        }

        // ── The two disagreements that made this type necessary ───────────────

        /// <summary>
        /// The one the dashboard got wrong. A suppressed duplicate matched neither
        /// <c>StartsWith("ORDER_FAILED")</c> nor <c>StartsWith("PROVIDER_NOT")</c>, so the ticket
        /// announced "Order placed" — after the double-Enter the gate exists to absorb.
        /// </summary>
        [Fact]
        public void ASuppressedDuplicate_IsNotASuccess()
        {
            var placement = OrderPlacement.Parse("ORDER_DUPLICATE_SUPPRESSED");

            Assert.False(placement.Succeeded);
            Assert.False(placement.HasOrderId);
            Assert.Contains("duplicate", placement.FailureMessage!, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The one the order service got wrong, in the other direction. Nine providers return
        /// <c>ORDER_SUBMITTED</c> when the venue accepts an order without giving an id back. It is
        /// a SUCCESS with nothing to poll — and the old <c>ORDER_</c> prefix test read it as a
        /// failure, skipping bracket verification on the very orders least able to report a
        /// missing stop.
        /// </summary>
        [Fact]
        public void AVenueAcceptanceWithNoId_IsASuccessWithNothingToPoll()
        {
            var placement = OrderPlacement.Parse("ORDER_SUBMITTED");

            Assert.Equal(OrderOutcome.Accepted, placement.Outcome);
            Assert.True(placement.Succeeded);
            Assert.False(placement.HasOrderId);   // nothing for the status poller to chase
            Assert.Null(placement.FailureMessage);
        }

        /// <summary>
        /// <b>Mutant M21's guard, generalised.</b> Both prefixes are reserved for non-id results,
        /// so an unrecognised one is a refusal — never an order id. The old exact-match recogniser
        /// fell through to "it went" here, which is how deleting one arm of a classifier could
        /// turn a provider's refusal into a spoken confirmation with no test noticing.
        /// </summary>
        [Theory]
        [InlineData("ORDER_WHAT_IS_THIS")]
        [InlineData("ORDER_THROTTLED")]
        [InlineData("PROVIDER_MADE_UP")]
        [InlineData("PROVIDER_NOT")]              // the family prefix with nothing after it
        public void AnUnrecognisedReservedCode_IsARefusalNotAnOrderId(string code)
        {
            var placement = OrderPlacement.Parse(code);

            Assert.False(placement.Succeeded, $"{code} would be announced as a placed order.");
            Assert.False(placement.HasOrderId);
            Assert.False(string.IsNullOrWhiteSpace(placement.FailureMessage), $"{code} would be silent.");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void AnEmptyAnswer_IsAFailureNotASuccess(string? code)
        {
            var placement = OrderPlacement.Parse(code);

            Assert.False(placement.Succeeded);
            Assert.False(string.IsNullOrWhiteSpace(placement.FailureMessage));
        }

        // ── ORDER_UNCERTAIN keeps its own words ───────────────────────────────

        /// <summary>
        /// The order most likely IS live. Announcing it under a caller's "Close failed" /
        /// "Order rejected" headline is how the same position gets opened twice, so
        /// <see cref="OrderPlacement.RefusalAnnouncement"/> drops the headline for this one
        /// outcome and for no other.
        /// </summary>
        [Fact]
        public void AnUncertainOrder_IsNotAnnouncedUnderAFailureHeadline()
        {
            var placement = OrderPlacement.Parse("ORDER_UNCERTAIN:EX-991");
            string spoken = placement.RefusalAnnouncement("Close failed for BTC/USD.");

            Assert.True(placement.NeedsVerification);
            Assert.DoesNotContain("Close failed", spoken, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Not placed", spoken, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("EX-991", spoken);
            Assert.Contains("Check your open orders", spoken);
        }

        /// <summary>
        /// A caller whose headline carries information the user still needs — which strategy
        /// placed it, which position it belongs to — supplies a neutral one rather than losing it.
        /// </summary>
        [Fact]
        public void AnUncertainOrder_KeepsANeutralHeadlineWhenTheCallerSuppliesOne()
        {
            string spoken = OrderPlacement.Parse("ORDER_UNCERTAIN:EX-991")
                .RefusalAnnouncement("Trend Follower could not place its Buy order.",
                                     "Trend Follower's Buy order:");

            Assert.StartsWith("Trend Follower's Buy order:", spoken, StringComparison.Ordinal);
            Assert.DoesNotContain("could not place", spoken, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The vacuity half: an ordinary refusal DOES keep the caller's headline.</summary>
        [Fact]
        public void AnOrdinaryRefusal_KeepsTheCallersHeadline()
        {
            string spoken = OrderPlacement.Parse("ORDER_FAILED:the venue said no")
                .RefusalAnnouncement("Close failed for BTC/USD.");

            Assert.StartsWith("Close failed for BTC/USD.", spoken, StringComparison.Ordinal);
            Assert.Contains("the venue said no", spoken);
        }
    }
}
