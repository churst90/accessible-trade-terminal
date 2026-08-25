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
    }
}
