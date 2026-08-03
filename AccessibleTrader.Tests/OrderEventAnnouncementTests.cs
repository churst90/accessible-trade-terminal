using System;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Tests.Mocks;
using Xunit;

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
            var navMgr = new NavigationFeedbackManager(speechRouter, formatter, eventBus, sonify, new MockIndicatorEngine());

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
        public void OrderRejected_SpeaksSymbolWithoutOrderIdNoise()
        {
            var (_, bus, speech, _) = CreateHarness();

            bus.Publish(new OrderRejectedEvent(
                Update(qty: 0, price: 0, status: OrderStatus.Rejected),
                "Order ord-123 rejected"));

            Assert.Contains("Order rejected", speech.LastSpokenText);
            Assert.Contains("BTCUSD", speech.LastSpokenText);
            // GUID-ish order ids are unreadable in speech — they must stay out.
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
        public void FillWithoutPrice_OmitsAtClause()
        {
            string msg = AccessibilityFeedbackCoordinator.FormatFill(
                "Order filled", Update(price: 0));

            Assert.DoesNotContain(" at ", msg);
            Assert.Contains("Bought 0.5 BTCUSD", msg);
        }
    }
}
