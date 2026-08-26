using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Order lifecycle events (fills, partial fills, stop/TP executions, rejections) are
    /// money events and must ALWAYS reach the blind user through speech + earcon — they
    /// are exempt from speech/sonification toggles, playback gating, and narration
    /// settings. Regression guard for the audit finding that OrderFilledEvent had zero
    /// subscribers (a fill was completely silent).
    /// </summary>
    public class OrderEventAnnouncementTests
    {
        private (AccessibilityFeedbackCoordinator coordinator, SpyEventBus eventBus,
                 CounterSpeechManager speech, MockEarconService earcons) CreateHarness()
        {
            var eventBus = new SpyEventBus();
            var sonify = new MockNavigationSonifier();
            var speech = new CounterSpeechManager();
            var store = new MockWorkspaceStore();
            var formatter = new SpeechFormatter();
            var speechRouter = new SpeechFeedbackRouter(speech, formatter, store);
            var earcons = new MockEarconService();
            var audioRouter = new AudioFeedbackRouter(sonify, earcons);
            var navMgr = new NavigationFeedbackManager(speechRouter, formatter);

            var coordinator = new AccessibilityFeedbackCoordinator(
                store, navMgr, speechRouter, audioRouter, formatter, eventBus,
                earcons, new SdkCandlePatternAnalyzer(),
                new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())), new ChartPatternFocus(), new MockAutoNarrationService());

            return (coordinator, eventBus, speech, earcons);
        }

        private static OrderUpdate Update(
            OrderSide side = OrderSide.Buy,
            double qty = 0.5,
            double price = 45000,
            double remaining = 0,
            OrderStatus status = OrderStatus.Filled,
            bool stop = false,
            bool tp = false)
            => new("ord-123", "BTCUSD", side, qty, price, remaining, status, stop, tp, DateTime.UtcNow);

        [Fact]
        public void OrderFilled_SpeaksAndPlaysEarcon()
        {
            var (_, bus, speech, earcons) = CreateHarness();

            bus.Publish(new OrderFilledEvent(Update()));

            Assert.Equal(1, earcons.OrderFillCount);
            Assert.Equal(OrderSide.Buy, earcons.LastOrderFillSide);
            Assert.Contains("Order filled", speech.LastSpokenText);
            Assert.Contains("Bought", speech.LastSpokenText);
            Assert.Contains("BTCUSD", speech.LastSpokenText);
        }

        [Fact]
        public void OrderFilled_SellSide_SaysSold()
        {
            var (_, bus, speech, earcons) = CreateHarness();

            bus.Publish(new OrderFilledEvent(Update(side: OrderSide.Sell)));

            Assert.Equal(OrderSide.Sell, earcons.LastOrderFillSide);
            Assert.Contains("Sold", speech.LastSpokenText);
        }

        [Fact]
        public void PartialFill_AnnouncesRemainingQuantity()
        {
            var (_, bus, speech, earcons) = CreateHarness();

            bus.Publish(new OrderPartialFillEvent(
                Update(qty: 0.3, remaining: 0.2, status: OrderStatus.PartialFill)));

            Assert.Equal(1, earcons.OrderFillCount);
            Assert.Contains("Partial fill", speech.LastSpokenText);
            Assert.Contains("0.2 remaining", speech.LastSpokenText);
        }

        [Fact]
        public void StopHit_PlaysStopEarconAndSpeaks()
        {
            var (_, bus, speech, earcons) = CreateHarness();

            bus.Publish(new StopHitEvent(Update(side: OrderSide.Sell, stop: true)));

            Assert.Equal(1, earcons.StopHitCount);
            Assert.Contains("Stop loss hit", speech.LastSpokenText);
        }

        [Fact]
        public void TakeProfitHit_PlaysTpEarconAndSpeaks()
        {
            var (_, bus, speech, earcons) = CreateHarness();

            bus.Publish(new TakeProfitHitEvent(Update(side: OrderSide.Sell, tp: true)));

            Assert.Equal(1, earcons.TakeProfitHitCount);
            Assert.Contains("Take profit hit", speech.LastSpokenText);
        }

        [Fact]
        public void OrderRejected_SpeaksTheReason()
        {
            // The reason was reaching this handler and being dropped, so every
            // rejection sounded the same: the user learned something had not
            // happened and never what to change. Insufficient balance and a sell
            // with nothing to sell are different problems with different fixes.
            var (_, bus, speech, _) = CreateHarness();

            bus.Publish(new OrderRejectedEvent(
                Update(qty: 0, price: 0, status: OrderStatus.Rejected),
                "insufficient paper balance — that position needs 45,000.00 USDT and the account holds 1,200.00"));

            Assert.Contains("Order rejected", speech.LastSpokenText);
            Assert.Contains("BTCUSD", speech.LastSpokenText);
            Assert.Contains("insufficient paper balance", speech.LastSpokenText);
            Assert.Contains("1,200.00", speech.LastSpokenText);
        }

        [Fact]
        public void OrderRejected_WithNoReason_SpeaksNoTrailingNoise()
        {
            // A broker that gives no reason must not have one invented for it.
            // The old fallback recited the order id, which is a guid read aloud.
            var (_, bus, speech, _) = CreateHarness();

            bus.Publish(new OrderRejectedEvent(
                Update(qty: 0, price: 0, status: OrderStatus.Rejected), ""));

            Assert.Equal("Order rejected for BTCUSD.", speech.LastSpokenText);
            Assert.DoesNotContain("ord-123", speech.LastSpokenText);
        }

        [Fact]
        public void OrderCancelled_IsSpokenNotSilent()
        {
            // 2026-07-22 audit: cancels were the ONE order state change that
            // vanished silently — logged, never announced.
            var (_, bus, speech, _) = CreateHarness();

            bus.Publish(new OrderCancelledEvent(
                Update(qty: 0, price: 0, status: OrderStatus.Cancelled)));

            Assert.Contains("Order cancelled", speech.LastSpokenText);
            Assert.Contains("BTCUSD", speech.LastSpokenText);
            Assert.DoesNotContain("ord-123", speech.LastSpokenText);
        }

        [Fact]
        public void CancelledAfterPartialFill_SpeaksTheExecutedPart()
        {
            // A cancelled order that partially filled first left the trader with
            // a position. On venues that emulate market orders as IOC limits
            // (Gemini) the DEFAULT order type is the one most likely to partially
            // fill and cancel; MEXC's "partially filled then canceled" status is
            // one terminal message. A bare "cancelled" says "you are flat" — the
            // opposite of the truth.
            var (_, bus, speech, _) = CreateHarness();

            bus.Publish(new OrderCancelledEvent(
                Update(qty: 0.4, price: 99.5, remaining: 0.6, status: OrderStatus.Cancelled)));

            Assert.Contains("Order cancelled", speech.LastSpokenText);
            Assert.Contains("partial fill", speech.LastSpokenText);
            Assert.Contains("bought 0.4", speech.LastSpokenText);
            Assert.Contains("99.5", speech.LastSpokenText);
        }

        [Fact]
        public void OrderExpired_IsSpokenAsExpiredNotCancelled()
        {
            // Expired is not a cancel (nobody asked) and not a rejection (the
            // venue accepted the order). Binance mapped EXPIRED→Rejected and four
            // providers squashed it into Cancelled — the trader heard the wrong
            // reason their order left the book.
            var (_, bus, speech, _) = CreateHarness();

            bus.Publish(new OrderExpiredEvent(
                Update(qty: 0, price: 0, status: OrderStatus.Expired)));

            Assert.Contains("Order expired", speech.LastSpokenText);
            Assert.Contains("BTCUSD", speech.LastSpokenText);
            Assert.DoesNotContain("cancelled", speech.LastSpokenText);
            Assert.DoesNotContain("rejected", speech.LastSpokenText);
        }

        [Fact]
        public void OrderReplaced_SaysStillWorking_NeverCancelled()
        {
            // A replaced order is STILL LIVE under a new id. Saying "cancelled"
            // tells the trader they are flat; they re-enter and are double-sized
            // with the original still resting — the audit's Schwab REPLACED bug.
            var (_, bus, speech, _) = CreateHarness();

            bus.Publish(new OrderReplacedEvent(
                Update(qty: 0, price: 0, status: OrderStatus.Replaced)));

            Assert.Contains("Order replaced", speech.LastSpokenText);
            Assert.Contains("still working", speech.LastSpokenText);
            Assert.DoesNotContain("cancelled", speech.LastSpokenText);
        }

        [Fact]
        public void TerminatedFormat_ThreadsTheProviderReason()
        {
            string msg = AccessibilityFeedbackCoordinator.FormatTerminated(
                "expired", Update(qty: 0, price: 0, status: OrderStatus.Expired)
                    with { Reason = "day order reached the close" });

            Assert.Contains("Order expired for BTCUSD.", msg);
            Assert.Contains("day order reached the close", msg);
        }

        [Fact]
        public void RejectedStopLeg_IsNotAnnouncedAsAStopHit()
        {
            // The stop/take-profit flags say which LEG an update belongs to, not
            // that it executed. Routing on the flag alone announced "Stop hit" for
            // a protective order the broker had REFUSED — the opposite of what
            // happened, on the one channel a trader acts on immediately.
            var (_, bus, speech, earcons) = CreateHarness();

            bus.Publish(new OrderRejectedEvent(
                Update(qty: 0, price: 0, status: OrderStatus.Rejected, stop: true),
                "cannot sell 1 BTC — the account holds none"));

            Assert.Contains("Order rejected", speech.LastSpokenText);
            Assert.DoesNotContain("Stop hit", speech.LastSpokenText);
            Assert.Equal(0, earcons.StopHitCount);
        }

        [Fact]
        public void FillWithoutPrice_OmitsAtClause()
        {
            string msg = AccessibilityFeedbackCoordinator.FormatFill(
                "Order filled", Update(price: 0));

            Assert.DoesNotContain(" at ", msg);
            Assert.Contains("Bought 0.5 BTCUSD", msg);
        }

        /// <summary>
        /// A fill nobody asked for must not sound like one they placed.
        ///
        /// <para>
        /// The paper broker's forced liquidation emits a <c>Filled</c> update carrying
        /// <c>Reason = "LIQUIDATED — the short's collateral was exhausted at …"</c>, and the fill
        /// formatter dropped <c>Reason</c> because fills were assumed to be requested. The trader
        /// heard "Order filled. Bought 1 BTCUSD at 200. Loss 100." — a trade they did not place,
        /// worded exactly like one they did, with the fact that they had been wiped out left out.
        /// <c>FormatTerminated</c> has always spoken the reason; fills now do too.
        /// </para>
        /// </summary>
        [Fact]
        public void AForcedLiquidation_DoesNotAnnounceAsAnOrdinaryFill()
        {
            var liquidation = Update(qty: 1, price: 200) with
            {
                RealizedPnL = -100,
                Reason = "LIQUIDATED — the short's collateral was exhausted at 200"
            };

            string msg = AccessibilityFeedbackCoordinator.FormatFill("Order filled", liquidation);

            Assert.Contains("LIQUIDATED", msg);
            Assert.Contains("collateral was exhausted", msg);
            // Still a fill, and still says what it cost — the reason is added, not substituted.
            Assert.Contains("Bought 1 BTCUSD", msg);
            Assert.Contains("Loss", msg);
        }

        /// <summary>
        /// The vacuity half: an ordinary fill carries no reason and must gain no trailing clause.
        /// </summary>
        [Fact]
        public void AnOrdinaryFill_GainsNoReasonClause()
        {
            string msg = AccessibilityFeedbackCoordinator.FormatFill("Order filled", Update());

            Assert.EndsWith(".", msg);
            Assert.DoesNotContain("LIQUIDATED", msg);
            Assert.Equal("Order filled. Bought 0.5 BTCUSD at 45000.00.", msg);
        }

        // ── Which utterances cut off the one before them ─────────────────────
        //
        // A2/F2. Every `Speak` call in the coordinator names an `interrupt:` value and
        // NOTHING in the suite observed any of them — the only assertion on the word
        // anywhere was a grep over .razor source, and the test double discarded the
        // flag before anyone could read it. Mutant M17 flips one of these and the
        // suite stayed green.
        //
        // The rule the code encodes, and these pin: an event that changes the
        // trader's position or their money — a fill, a partial, a stop, a take
        // profit, a rejection, a margin warning — interrupts, because hearing it
        // thirty seconds late is the same as not hearing it. An event that merely
        // retires an order they already know about — a cancel they asked for, an
        // expiry, a replace — does not, because stamping on a bar reading to say
        // "your cancel went through" is the terminal talking over the user.

        private static bool SpokeInterrupting(CounterSpeechManager speech)
        {
            var utterance = Assert.Single(speech.Utterances);
            return utterance.Interrupt;
        }

        [Theory]
        [InlineData("fill")]
        [InlineData("partial")]
        [InlineData("stop")]
        [InlineData("takeprofit")]
        [InlineData("rejected")]
        [InlineData("margin")]
        public void MoneyEvents_InterruptWhateverIsBeingSpoken(string which)
        {
            var (_, bus, speech, _) = CreateHarness();

            switch (which)
            {
                case "fill":       bus.Publish(new OrderFilledEvent(Update())); break;
                case "partial":    bus.Publish(new OrderPartialFillEvent(
                                       Update(qty: 0.3, remaining: 0.2, status: OrderStatus.PartialFill))); break;
                case "stop":       bus.Publish(new StopHitEvent(Update(side: OrderSide.Sell, stop: true))); break;
                case "takeprofit": bus.Publish(new TakeProfitHitEvent(Update(side: OrderSide.Sell, tp: true))); break;
                case "rejected":   bus.Publish(new OrderRejectedEvent(
                                       Update(qty: 0, price: 0, status: OrderStatus.Rejected), "Insufficient balance")); break;
                case "margin":     bus.Publish(new MarginWarningEvent("BTCUSD", 0.12,
                                       "Margin warning for BTCUSD. Liquidation is 3 percent away.")); break;
            }

            Assert.True(SpokeInterrupting(speech),
                $"'{which}' was queued behind whatever was already speaking instead of interrupting it.");
            // Interrupting is two acts, not one: the router silences first and then
            // speaks. A Speak(interrupt: true) with no Silence leaves the previous
            // utterance running underneath on the managers that queue.
            Assert.True(speech.SilenceCalls >= 1,
                $"'{which}' claimed to interrupt but never silenced the current utterance.");
        }

        [Theory]
        [InlineData("cancelled")]
        [InlineData("expired")]
        [InlineData("replaced")]
        public void OrderRetirements_WaitTheirTurn(string which)
        {
            var (_, bus, speech, _) = CreateHarness();

            switch (which)
            {
                case "cancelled": bus.Publish(new OrderCancelledEvent(
                                      Update(status: OrderStatus.Cancelled))); break;
                case "expired":   bus.Publish(new OrderExpiredEvent(
                                      Update(status: OrderStatus.Expired))); break;
                case "replaced":  bus.Publish(new OrderReplacedEvent(
                                      Update(status: OrderStatus.Cancelled))); break;
            }

            Assert.False(SpokeInterrupting(speech),
                $"'{which}' cut off whatever the user was listening to for a routine state change.");
            Assert.Equal(0, speech.SilenceCalls);
        }

        /// <summary>
        /// The vacuity check for the pair above. Both theories read the same field, so a
        /// double that silently stopped recording it would turn one green and the other
        /// red — but a double that recorded a CONSTANT would turn exactly one of them
        /// green and could survive if only one theory existed. This asserts the two
        /// values actually differ within a single harness, which is the claim.
        /// </summary>
        [Fact]
        public void TheInterruptFlagIsRecorded_AndTheTwoClassesDisagree()
        {
            var (_, bus, speech, _) = CreateHarness();

            bus.Publish(new OrderFilledEvent(Update()));
            bus.Publish(new OrderCancelledEvent(Update(status: OrderStatus.Cancelled)));

            Assert.Equal(2, speech.Utterances.Count);
            Assert.True(speech.Utterances[0].Interrupt);
            Assert.False(speech.Utterances[1].Interrupt);
        }
    }
}
