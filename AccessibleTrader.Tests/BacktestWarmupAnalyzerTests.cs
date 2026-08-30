using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// First coverage for <see cref="BacktestWarmupAnalyzer"/> — the thing that decides how many
    /// bars a backtest throws away before it starts believing its own signals.
    ///
    /// <para>
    /// ── Why this file exists ───────────────────────────────────────────────────
    /// A2d/D06: breaking the running maximum (<c>if (window &gt; maxWindow)</c>) so no indicator
    /// could ever raise the warmup above the 50-bar floor left the full suite green. The class was
    /// named in exactly one test file, and only as a constructor argument being wired into a
    /// Blazor harness — nobody had ever asked it a question.
    /// </para>
    ///
    /// <para>
    /// What the defect costs is a backtest that is optimistic in the direction nobody checks. A
    /// 200-period moving average has not converged at bar 60; a strategy gated on it trades the
    /// warmup residue, and the resulting equity curve is not wrong in a way that looks wrong. It
    /// is the same failure class as the seed-residue work in <c>MovingAverageGapTests</c>, one
    /// level up: there the indicator was unconverged, here the harness stops waiting for it.
    /// </para>
    ///
    /// <para>
    /// The 1.2x safety multiplier is asserted as the observable number rather than restated as an
    /// expression — <c>Math.Ceiling(maxWindow * 1.2)</c> in the test would be
    /// <see href="https://en.wikipedia.org/wiki/Tautology">the production line typed twice</see>
    /// and would survive any change to it.
    /// </para>
    /// </summary>
    public class BacktestWarmupAnalyzerTests
    {
        private static IIndicatorProvider Provider(string code, int stabilityWindow)
        {
            var p = Substitute.For<IIndicatorProvider>();
            p.GetIndicators().Returns(new List<IndicatorMetadata>
            {
                new() { Code = code, Name = code, Category = "Test" }
            });
            p.GetStabilityWindow(code, Arg.Any<Dictionary<string, object>>()).Returns(stabilityWindow);
            return p;
        }

        private static StrategySpec Spec(params string[] descriptorIds)
        {
            var children = descriptorIds
                .Select((id, i) => (ConditionNode)new ConditionLeaf($"leaf{i}", id, LeafOperator.GreaterThan, 0))
                .ToList();

            return new StrategySpec(
                Id: "spec.warmup", Name: "Warmup", Description: "fixture",
                Side: OrderSide.Buy,
                Conditions: new ConditionGroup("root", LogicOperator.And, children),
                Risk: new RiskPlan(
                    Stop: new StopSource(StopSourceKind.PercentOfPrice, PercentValue: 1.0),
                    TpLadder: Array.Empty<TpLadderRung>(),
                    Sizing: new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005),
                    Entry: new EntryTrigger(EntryTriggerKind.Immediate)));
        }

        [Fact]
        public void TheSlowestReferencedIndicatorSetsTheWarmup()
        {
            var analyzer = new BacktestWarmupAnalyzer(
                new StubCatalog(("SLOW.Line", "SLOW")),
                new[] { Provider("SLOW", 200) });

            // 200 bars to stabilise, plus the 1.2x safety margin = 240. NOT the 50-bar floor.
            Assert.Equal(240, analyzer.RecommendedWarmup(Spec("SLOW.Line")));
        }

        [Fact]
        public void TheLargestWindowWinsWhenSeveralIndicatorsAreReferenced()
        {
            var analyzer = new BacktestWarmupAnalyzer(
                new StubCatalog(("FAST.Line", "FAST"), ("SLOW.Line", "SLOW")),
                new[] { Provider("FAST", 30), Provider("SLOW", 200) });

            Assert.Equal(240, analyzer.RecommendedWarmup(Spec("FAST.Line", "SLOW.Line")));
        }

        [Fact]
        public void TheFloorAppliesWhenEveryIndicatorIsFasterThanIt()
        {
            var analyzer = new BacktestWarmupAnalyzer(
                new StubCatalog(("FAST.Line", "FAST")),
                new[] { Provider("FAST", 10) });

            Assert.Equal(60, analyzer.RecommendedWarmup(Spec("FAST.Line")));   // 50 * 1.2
            Assert.Equal(120, analyzer.RecommendedWarmup(Spec("FAST.Line"), floor: 100));
        }

        [Fact]
        public void AProviderThatThrowsIsSkippedRatherThanTakingTheBacktestWithIt()
        {
            var angry = Substitute.For<IIndicatorProvider>();
            angry.GetIndicators().Returns(_ => throw new InvalidOperationException("plugin is unhappy"));

            var analyzer = new BacktestWarmupAnalyzer(
                new StubCatalog(("SLOW.Line", "SLOW")),
                new[] { angry, Provider("SLOW", 200) });

            Assert.Equal(240, analyzer.RecommendedWarmup(Spec("SLOW.Line")));
        }

        [Fact]
        public void ReferencedIndicatorsWalksTheWholeTreeAndDeduplicates()
        {
            var analyzer = new BacktestWarmupAnalyzer(
                new StubCatalog(("SLOW.Line", "SLOW"), ("SLOW.Other", "SLOW"), ("FAST.Line", "FAST")),
                new[] { Provider("SLOW", 200), Provider("FAST", 30) });

            var codes = analyzer.ReferencedIndicators(Spec("SLOW.Line", "SLOW.Other", "FAST.Line"));

            Assert.Equal(2, codes.Count);
            Assert.Contains("SLOW", codes);
            Assert.Contains("FAST", codes);
        }

        private sealed class StubCatalog : ISignalCatalog
        {
            public StubCatalog(params (string Id, string Code)[] entries)
                => All = entries
                    .Select(e => new SignalDescriptor(e.Id, e.Code, "Line", SignalKind.Line, e.Id))
                    .ToList();

            public IReadOnlyList<SignalDescriptor> All { get; }
            public SignalDescriptor? GetById(string id) => All.FirstOrDefault(d => d.Id == id);
            public IReadOnlyList<SignalDescriptor> GetForIndicator(string code)
                => All.Where(d => d.IndicatorCode == code).ToList();
            public void Refresh() { }
        }
    }
}
