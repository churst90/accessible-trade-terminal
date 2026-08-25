using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Setup lifecycle events must be reviewable in the journal — a blind trader who
    /// missed the announcement needs the full trade plan (entry / stop / every TP rung)
    /// on record, not just the ones that became signals.
    /// </summary>
    public class JournalSetupLoggingTests
    {
        private static (JournalService Journal, SpyEventBus Bus) Build()
        {
            var bus = new SpyEventBus();
            var store = Substitute.For<IWorkspaceStore>();
            store.State.Returns(WorkspaceState.Initial);
            return (new JournalService(bus, store), bus);
        }

        private static ResolvedRiskPlan Plan() => new(
            EntryPrice: 2450.5, StopPrice: 2410.0,
            TpPrices: new List<double> { 2531.5, 2612.5 },
            ClosePortions: new List<double> { 0.5, 0.5 },
            Quantity: 1.0, RewardRiskRatio: 2.0, RiskCash: 50.0, Notes: "");

        [Fact]
        public void ArmedSetup_IsJournaled_WithFullTradePlan()
        {
            var (journal, bus) = Build();

            bus.Publish(new SetupArmedEvent("Test Strat", "i1", OrderSide.Buy,
                "Waiting for pullback to 2440.", Plan()));

            var entry = Assert.Single(journal.Snapshot());
            Assert.Equal(JournalEntryKind.StrategySignal, entry.Kind);
            Assert.Contains("ARMED", entry.Text);
            Assert.Contains("Entry 2450.5000", entry.Text);
            Assert.Contains("stop 2410.0000", entry.Text);
            Assert.Contains("target 1 2531.5000", entry.Text);
            Assert.Contains("target 2 2612.5000", entry.Text);
        }

        [Fact]
        public void EntryReached_AndDropped_AreJournaled()
        {
            var (journal, bus) = Build();

            bus.Publish(new SetupEntryReachedEvent("Test Strat", "i1", OrderSide.Buy, 2440.25, 3));
            bus.Publish(new SetupDroppedEvent("Test Strat", "i1",
                new List<string> { "Blue dot" }, SetupStillActive: false));

            var entries = journal.Snapshot();
            Assert.Equal(2, entries.Count);
            Assert.Contains(entries, e => e.Text.Contains("entry zone reached at 2440.2500"));
            Assert.Contains(entries, e => e.Text.Contains("Blue dot") && e.Text.Contains("invalidated"));
        }

        [Fact]
        public void DroppedWithNoLabels_IsNotJournaled()
        {
            var (journal, bus) = Build();

            bus.Publish(new SetupDroppedEvent("Test Strat", "i1",
                new List<string>(), SetupStillActive: true));

            Assert.Empty(journal.Snapshot());
        }
    }
}
