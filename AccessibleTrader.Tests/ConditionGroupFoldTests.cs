using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// How <see cref="ConditionEvaluator"/> folds a group — specifically the <c>Score</c> operator,
    /// which is the one that gates on a number rather than on unanimity.
    ///
    /// <para>
    /// ── Why this file exists ───────────────────────────────────────────────────
    /// A2d/D05: replacing <c>subtreeScore &gt;= group.ScoreThreshold.Value</c> with
    /// <c>subtreeScore &gt;= 0</c> left the full suite green. Every existing condition test drives
    /// And / Or / Not or the multi-timeframe routing; nothing had ever built a Score group and
    /// asked whether the threshold was honoured.
    /// </para>
    ///
    /// <para>
    /// A Score group is a confluence gate: "fire when enough orthogonal sources agree". With the
    /// threshold ignored it fires when ANY of them do — including when none do, since a sum of
    /// zero still clears zero. That is not a degraded strategy, it is a different strategy, and
    /// it is invisible from outside: the user's spec still says "at least 5 points of confluence"
    /// and the fires look like fires.
    /// </para>
    ///
    /// <para>
    /// The leaves are higher-timeframe price leaves because those resolve from a stub with no
    /// indicator engine in the way — the point under test is the fold, not the leaf.
    /// </para>
    /// </summary>
    public class ConditionGroupFoldTests
    {
        private const string Tf = "1d";

        private static ConditionLeaf Leaf(string id, double threshold, double score) =>
            new(id, "TEST.Value", LeafOperator.GreaterThan, Value: threshold, Score: score, Timeframe: Tf);

        /// <summary>Two leaves against an HTF close of 500: "above 100" is true, "above 1000" is not.</summary>
        private static (ConditionEvaluator Eval, IReadOnlyList<Ohlcv> History, WorkspaceState State) Fixture()
        {
            var mtf = new StubMtf();
            mtf.CachedBars[("binance", "BTC/USDT", Tf)] = new List<Ohlcv>
            {
                new(new DateTime(2026, 04, 20), 500, 500, 500, 500, 0),
                new(new DateTime(2026, 04, 21), 500, 500, 500, 500, 0),
            };
            var history = new List<Ohlcv> { new(new DateTime(2026, 04, 23), 500, 500, 500, 500, 0) };
            var state = WorkspaceState.Initial with
            {
                Identity = new ChartIdentity("Spot", "binance", "BTC/USDT", "5m"),
            };
            return (new ConditionEvaluator(new StubCatalog(), mtf, levels: null), history, state);
        }

        [Fact]
        public void AScoreGroupBelowItsThresholdDoesNotFire()
        {
            var (eval, history, state) = Fixture();
            var group = new ConditionGroup("root", LogicOperator.Score, new List<ConditionNode>
            {
                Leaf("hit",  threshold: 100,  score: 2.0),   // true  → contributes 2
                Leaf("miss", threshold: 1000, score: 5.0),   // false → contributes 0
            }, ScoreThreshold: 5.0);

            var result = eval.Evaluate(group, history, state);

            Assert.False(result.OverallTrue, $"2 points of confluence cleared a threshold of 5 (score {result.Score})");
            Assert.Equal(2.0, result.Score, 6);
            Assert.Equal(7.0, result.MaxScore, 6);
            Assert.True(result.LeafResults["hit"]);
            Assert.False(result.LeafResults["miss"]);
        }

        [Fact]
        public void AScoreGroupThatMeetsItsThresholdFires()
        {
            var (eval, history, state) = Fixture();
            var group = new ConditionGroup("root", LogicOperator.Score, new List<ConditionNode>
            {
                Leaf("hit",  threshold: 100,  score: 2.0),
                Leaf("miss", threshold: 1000, score: 5.0),
            }, ScoreThreshold: 2.0);   // exactly met — the comparison is >=, not >

            Assert.True(eval.Evaluate(group, history, state).OverallTrue);
        }

        [Fact]
        public void AScoreGroupWhereNothingFiresIsFalseEvenWithAZeroThreshold()
        {
            // The specimen the mutant produced: a sum of zero clears a threshold of zero, so a
            // group in which no leaf resolved true would still fire. Zero confluence is not
            // confluence, and the group must not fire on it.
            var (eval, history, state) = Fixture();
            var group = new ConditionGroup("root", LogicOperator.Score, new List<ConditionNode>
            {
                Leaf("missA", threshold: 1000, score: 3.0),
                Leaf("missB", threshold: 2000, score: 4.0),
            }, ScoreThreshold: 3.0);

            var result = eval.Evaluate(group, history, state);
            Assert.False(result.OverallTrue);
            Assert.Equal(0.0, result.Score, 6);
        }

        [Fact]
        public void AScoreGroupWithNoThresholdDegradesToOr()
        {
            var (eval, history, state) = Fixture();

            var oneTrue = new ConditionGroup("root", LogicOperator.Score, new List<ConditionNode>
            {
                Leaf("hit",  threshold: 100,  score: 1.0),
                Leaf("miss", threshold: 1000, score: 1.0),
            });
            Assert.True(eval.Evaluate(oneTrue, history, state).OverallTrue);

            var noneTrue = new ConditionGroup("root", LogicOperator.Score, new List<ConditionNode>
            {
                Leaf("missA", threshold: 1000, score: 1.0),
                Leaf("missB", threshold: 2000, score: 1.0),
            });
            Assert.False(eval.Evaluate(noneTrue, history, state).OverallTrue);
        }

        [Fact]
        public void AndAndOrStillMeanWhatTheySay()
        {
            // Vacuity floor: if the fixture stopped resolving leaves at all, every assertion
            // above would pass for the wrong reason. These two cannot.
            var (eval, history, state) = Fixture();
            var children = new List<ConditionNode> { Leaf("hit", 100, 1.0), Leaf("miss", 1000, 1.0) };

            Assert.False(eval.Evaluate(new ConditionGroup("and", LogicOperator.And, children), history, state).OverallTrue);
            Assert.True(eval.Evaluate(new ConditionGroup("or", LogicOperator.Or, children), history, state).OverallTrue);
        }

        // ── Stubs ────────────────────────────────────────────────────────────

        private sealed class StubCatalog : ISignalCatalog
        {
            public IReadOnlyList<SignalDescriptor> All { get; }
                = new[] { new SignalDescriptor("TEST.Value", "TEST", "Value", SignalKind.Line, "Test Value") };
            public SignalDescriptor? GetById(string id) => All.FirstOrDefault(d => d.Id == id);
            public IReadOnlyList<SignalDescriptor> GetForIndicator(string code)
                => All.Where(d => d.IndicatorCode == code).ToList();
            public void Refresh() { }
        }

        private sealed class StubMtf : IMultiTimeframeDataService
        {
            public Dictionary<(string p, string s, string tf), IReadOnlyList<Ohlcv>> CachedBars { get; } = new();

            public Task<IReadOnlyList<Ohlcv>> GetBarsAsync(string market, string provider, string symbol,
                string timeframe, int count) => Task.FromResult(GetCachedBars(provider, symbol, timeframe));

            public IReadOnlyList<Ohlcv> GetCachedBars(string provider, string symbol, string timeframe)
                => CachedBars.TryGetValue((provider, symbol, timeframe), out var v) ? v : Array.Empty<Ohlcv>();

            public void Clear() => CachedBars.Clear();

            public Task PrewarmIndicatorAsync(string market, string provider, string symbol, string timeframe,
                string indicatorCode, Dictionary<string, object> parameters, int count) => Task.CompletedTask;

            public Dictionary<string, double[]>? GetCachedIndicator(string provider, string symbol,
                string timeframe, string indicatorCode) => null;
        }
    }
}
