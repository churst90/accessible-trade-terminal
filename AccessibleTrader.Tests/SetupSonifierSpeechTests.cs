using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// What a composite setup SAYS when it confirms and when a condition drops out.
    ///
    /// <para>
    /// Written for mutation campaign A2l (2026-09-24). <c>TierBRegressionTests</c> pins the
    /// ladder note on the ARMED path only; the note was later added to the CONFIRMED path because
    /// an Immediate-trigger setup — and every pure-pulse tree, auto-promoted to Immediate — never
    /// publishes SetupArmedEvent, so those users never heard how their targets execute. Removing
    /// it again left the suite green. So did swapping "Setup still active" and "Setup
    /// invalidated" on a dropout, which tells a trader to keep waiting on a dead setup.
    /// </para>
    /// </summary>
    public class SetupSonifierSpeechTests
    {
        private sealed class AllSpeech : ISpeechManager
        {
            public readonly List<string> Said = new();
            public bool IsActive => true;
            public bool IsSpeechEnabled { get; set; } = true;
            public string SpeechMode => "Test";
            public Action<string>? OnSpeak { get; set; }
            public void Silence() { }
            public void Speak(string text, bool interrupt = false) => Said.Add(text);
        }

        private static (SpyEventBus Bus, AllSpeech Speech) Rig()
        {
            var bus = new SpyEventBus();
            var speech = new AllSpeech();
            _ = new SetupSonifier(bus, new MockEarconService(), speech);
            return (bus, speech);
        }

        private static ResolvedRiskPlan Plan(params double[] targets) => new(
            EntryPrice: 100, StopPrice: 98, TpPrices: targets, ClosePortions: new double[targets.Length],
            Quantity: 1, RewardRiskRatio: 2, RiskCash: 20, Notes: "");

        [Fact]
        public void An_immediate_confirmation_with_a_ladder_says_the_terminal_runs_the_rungs()
        {
            var (bus, speech) = Rig();

            bus.Publish(new SetupConfirmedEvent("Pulse", "i1", OrderSide.Buy,
                "Long setup confirmed. Entry 100.", Plan(102, 104, 106), Symbol: "BTC/USD"));

            string said = Assert.Single(speech.Said);
            Assert.StartsWith("BTC/USD: Long setup confirmed. Entry 100.", said);
            Assert.Contains("Ladder has 3 rungs", said);
            Assert.Contains("app has to be running", said);
        }

        [Fact]
        public void A_single_target_confirmation_says_nothing_about_a_ladder()
        {
            var (bus, speech) = Rig();
            bus.Publish(new SetupConfirmedEvent("Pulse", "i1", OrderSide.Buy, "Confirmed.", Plan(102)));
            Assert.Equal("Confirmed.", Assert.Single(speech.Said));
        }

        [Fact]
        public void A_dropout_that_kills_the_setup_says_invalidated()
        {
            var (bus, speech) = Rig();
            bus.Publish(new SetupDroppedEvent("S", "i1", new[] { "RSI below 30" }, SetupStillActive: false, Symbol: "ETH/USD"));
            Assert.Equal("ETH/USD: RSI below 30 dropped off. Setup invalidated.", Assert.Single(speech.Said));
        }

        [Fact]
        public void A_dropout_the_setup_survives_says_still_active()
        {
            var (bus, speech) = Rig();
            bus.Publish(new SetupDroppedEvent("S", "i1", new[] { "Wave cross", "Volume spike" }, SetupStillActive: true));
            Assert.Equal("Wave cross, Volume spike dropped off. Setup still active.", Assert.Single(speech.Said));
        }
    }
}
