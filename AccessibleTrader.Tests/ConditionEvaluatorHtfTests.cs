using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Tier 2 coverage for <see cref="ConditionEvaluator"/>'s multi-timeframe path.
    /// Targets two invariants:
    ///
    /// 1) <c>HtfLastClosedIndexExclusive</c> (private static binary search) must return the
    ///    count-exclusive index of the most recent HTF bar strictly earlier than the main-TF
    ///    bar's Date — i.e. `bars[0..result]` is the slice of already-closed HTF bars. Tested
    ///    via reflection to pin edge cases (empty / before-all / after-all / perfect alignment).
    ///
    /// 2) The per-(leafId, timeframe) warning dedup initialised 2026-04-23 (Week 4) replaces
    ///    a process-wide static bool. Each distinct missing-HTF leaf must surface its
    ///    degradation at least once per ConditionEvaluator instance, while a single chatty
    ///    leaf is rate-limited to one log line. Tested via a TraceListener that captures
    ///    Debug.WriteLine output during two Evaluate calls on the same missing HTF leaf.
    /// </summary>
    public class ConditionEvaluatorHtfTests
    {
        // ── Reflection access to the private static binary-search helper ─────

        private static int InvokeHtfLastClosedIndexExclusive(IReadOnlyList<Ohlcv> htfBars, DateTime mainBarDate)
        {
            var method = typeof(ConditionEvaluator).GetMethod(
                "HtfLastClosedIndexExclusive",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException("HtfLastClosedIndexExclusive not found.");
            return (int)method.Invoke(null, new object[] { htfBars, mainBarDate })!;
        }

        // ── Binary-search edge cases ──────────────────────────────────────────

        [Fact]
        public void HtfLastClosedIndex_EmptyHtf_ReturnsZero()
        {
            var bars = new List<Ohlcv>();
            int idx = InvokeHtfLastClosedIndexExclusive(bars, new DateTime(2026, 04, 23));
            Assert.Equal(0, idx);
        }

        [Fact]
        public void HtfLastClosedIndex_MainBarBeforeAllHtf_ReturnsZero()
        {
            var bars = new List<Ohlcv>
            {
                new Ohlcv(new DateTime(2026, 04, 20), 1, 1, 1, 1, 0),
                new Ohlcv(new DateTime(2026, 04, 21), 1, 1, 1, 1, 0),
                new Ohlcv(new DateTime(2026, 04, 22), 1, 1, 1, 1, 0),
            };
            int idx = InvokeHtfLastClosedIndexExclusive(bars, new DateTime(2026, 04, 19));
            Assert.Equal(0, idx);
        }

        [Fact]
        public void HtfLastClosedIndex_MainBarAfterAllHtf_ReturnsCount()
        {
            var bars = new List<Ohlcv>
            {
                new Ohlcv(new DateTime(2026, 04, 20), 1, 1, 1, 1, 0),
                new Ohlcv(new DateTime(2026, 04, 21), 1, 1, 1, 1, 0),
                new Ohlcv(new DateTime(2026, 04, 22), 1, 1, 1, 1, 0),
            };
            int idx = InvokeHtfLastClosedIndexExclusive(bars, new DateTime(2026, 04, 30));
            Assert.Equal(3, idx);
        }

        [Fact]
        public void HtfLastClosedIndex_PerfectAlignment_ExcludesEqualDateBar()
        {
            // Strictly-less semantics: an HTF bar whose open-time equals the main-TF bar's
            // time has not yet closed and must not be visible to the strategy. So when
            // mainBarDate == bars[2].Date the result is 2 — the slice bars[0..2] covers
            // only bar0 and bar1.
            var bars = new List<Ohlcv>
            {
                new Ohlcv(new DateTime(2026, 04, 20), 1, 1, 1, 1, 0),
                new Ohlcv(new DateTime(2026, 04, 21), 1, 1, 1, 1, 0),
                new Ohlcv(new DateTime(2026, 04, 22), 1, 1, 1, 1, 0),
            };
            int idx = InvokeHtfLastClosedIndexExclusive(bars, new DateTime(2026, 04, 22));
            Assert.Equal(2, idx);
        }

        [Fact]
        public void HtfLastClosedIndex_MainBarBetweenHtfBars_ReturnsUpperBound()
        {
            var bars = new List<Ohlcv>
            {
                new Ohlcv(new DateTime(2026, 04, 20, 00, 00, 00), 1, 1, 1, 1, 0),
                new Ohlcv(new DateTime(2026, 04, 21, 00, 00, 00), 1, 1, 1, 1, 0),
                new Ohlcv(new DateTime(2026, 04, 22, 00, 00, 00), 1, 1, 1, 1, 0),
                new Ohlcv(new DateTime(2026, 04, 23, 00, 00, 00), 1, 1, 1, 1, 0),
            };
            // Main bar at 21:12:00 is strictly after bars[1]=04/21 00:00 and strictly
            // before bars[2]=04/22 00:00, so the slice must include bar0 + bar1 only.
            int idx = InvokeHtfLastClosedIndexExclusive(bars, new DateTime(2026, 04, 21, 12, 00, 00));
            Assert.Equal(2, idx);
        }

        // ── Behavioural coverage: HTF routing + warning dedup ─────────────────

        [Fact]
        public void Evaluate_HtfLeafWithNoCachedData_ReturnsFalseAndSetsLastDegradation()
        {
            var catalog = new StubCatalog();
            var mtf = new StubMtf();  // no cached bars, no cached indicator
            var eval = new ConditionEvaluator(catalog, mtf, levels: null);

            var leaf = new ConditionLeaf(
                Id: "leafA",
                SignalDescriptorId: "TEST.Value",
                Operator: LeafOperator.GreaterThan,
                Value: 0,
                Timeframe: "1h");
            var result = eval.Evaluate(leaf, NewHistory(new[] { 1.0, 2.0, 3.0 }), StubState());

            Assert.False(result.OverallTrue);
            Assert.NotNull(eval.LastDegradation);
            Assert.Contains("leafA", eval.LastDegradation!);
            Assert.Contains("1h", eval.LastDegradation!);
        }

        [Fact]
        public void Evaluate_HtfLeafMissingDataTwice_DebugWritesOnlyOncePerLeaf()
        {
            // Per-(leafId,timeframe) dedup: the same evaluator calling Evaluate twice with the
            // same missing-HTF leaf must log exactly one line. A second distinct leaf logs a
            // second line. Uses Trace.Listeners since Debug.WriteLine routes through it.
            var catalog = new StubCatalog();
            var mtf = new StubMtf();
            var eval = new ConditionEvaluator(catalog, mtf, levels: null);

            var leafA = new ConditionLeaf("leafA", "TEST.Value", LeafOperator.GreaterThan, 0, Timeframe: "1h");
            var leafB = new ConditionLeaf("leafB", "TEST.Value", LeafOperator.GreaterThan, 0, Timeframe: "4h");

            var capture = new CapturingListener();
            Trace.Listeners.Add(capture);
            try
            {
                eval.Evaluate(leafA, NewHistory(new[] { 1.0 }), StubState());
                eval.Evaluate(leafA, NewHistory(new[] { 1.0 }), StubState());
                eval.Evaluate(leafB, NewHistory(new[] { 1.0 }), StubState());
            }
            finally
            {
                Trace.Listeners.Remove(capture);
            }

            int linesForA = capture.Lines.Count(l => l.Contains("leafA") && l.Contains("1h"));
            int linesForB = capture.Lines.Count(l => l.Contains("leafB") && l.Contains("4h"));
#if DEBUG
            // One deduped Debug.WriteLine per (leaf, timeframe).
            Assert.Equal(1, linesForA);
            Assert.Equal(1, linesForB);
#else
            // Debug.WriteLine compiles out entirely in Release (CI runs Release —
            // this mismatch kept the CI suite red from 1.4.0 through 1.6.0).
            Assert.Equal(0, linesForA);
            Assert.Equal(0, linesForB);
#endif

            // LastDegradation is overwritten each Evaluate — the most recent call wins.
            Assert.NotNull(eval.LastDegradation);
            Assert.Contains("leafB", eval.LastDegradation!);
        }

        [Fact]
        public void Evaluate_HtfPriceLeaf_MainBarAfterAllHtf_UsesLastHtfClose()
        {
            // Price leaf, no indicator cache, HTF bars with close = 500. Main-TF bar is after
            // all HTF bars, so endExclusive = htfBars.Count and the evaluator reads close=500.
            // Leaf: close > 100 → true.
            var catalog = new StubCatalog();
            var mtf = new StubMtf();
            mtf.CachedBars[("binance", "BTC/USDT", "1d")] = new List<Ohlcv>
            {
                new Ohlcv(new DateTime(2026, 04, 20), 100, 100, 100, 100, 0),
                new Ohlcv(new DateTime(2026, 04, 21), 200, 200, 200, 200, 0),
                new Ohlcv(new DateTime(2026, 04, 22), 500, 500, 500, 500, 0),
            };

            var eval = new ConditionEvaluator(catalog, mtf, levels: null);
            var leaf = new ConditionLeaf("priceLeaf", "TEST.Value", LeafOperator.GreaterThan, Value: 100, Timeframe: "1d");

            // Main-TF history ends AFTER all HTF bars → binary search returns HTF count.
            var history = new List<Ohlcv>
            {
                new Ohlcv(new DateTime(2026, 04, 23, 12, 0, 0), 500, 500, 500, 500, 0),
            };
            var result = eval.Evaluate(leaf, history, StubState());

            Assert.True(result.OverallTrue);
            Assert.Null(eval.LastDegradation);
        }

        [Fact]
        public void Evaluate_HtfPriceLeaf_MainBarBeforeAllHtf_ReturnsFalseWithNoDataAvailable()
        {
            // Main-TF is earlier than every HTF bar, so endExclusive = 0. Price leaf sees
            // no closed HTF bars → returns false. This path is the future-leak guard:
            // a backtest replaying bars from before the HTF cache begins must not peek
            // at future HTF closes.
            var catalog = new StubCatalog();
            var mtf = new StubMtf();
            mtf.CachedBars[("binance", "BTC/USDT", "1d")] = new List<Ohlcv>
            {
                new Ohlcv(new DateTime(2026, 04, 20), 100, 100, 100, 100, 0),
                new Ohlcv(new DateTime(2026, 04, 21), 200, 200, 200, 200, 0),
            };

            var eval = new ConditionEvaluator(catalog, mtf, levels: null);
            var leaf = new ConditionLeaf("priceLeaf", "TEST.Value", LeafOperator.GreaterThan, Value: 0, Timeframe: "1d");

            var history = new List<Ohlcv>
            {
                // Before all HTF bars → binary search returns 0 → EvaluateHtfPriceLeaf
                // sees upTo=0 and returns false.
                new Ohlcv(new DateTime(2026, 04, 18, 12, 0, 0), 1, 1, 1, 1, 0),
            };
            var result = eval.Evaluate(leaf, history, StubState());
            Assert.False(result.OverallTrue);
        }

        [Fact]
        public void Evaluate_HtfIndicatorLeaf_ClipsToEndExclusive()
        {
            // Cached indicator has four values. Main-TF bar perfectly aligns with the LAST
            // HTF bar's Date, so strictly-less semantics means the last bar is excluded and
            // the evaluator reads index 2 (value=10). Leaf is GreaterThan 5 → true.
            // If the clip were broken and the evaluator read index 3 instead (value=-99),
            // the leaf would return false and this test would fail.
            var catalog = new StubCatalog();
            var mtf = new StubMtf();
            mtf.CachedBars[("binance", "BTC/USDT", "1d")] = new List<Ohlcv>
            {
                new Ohlcv(new DateTime(2026, 04, 20), 1, 1, 1, 1, 0),
                new Ohlcv(new DateTime(2026, 04, 21), 1, 1, 1, 1, 0),
                new Ohlcv(new DateTime(2026, 04, 22), 1, 1, 1, 1, 0),
                new Ohlcv(new DateTime(2026, 04, 23), 1, 1, 1, 1, 0),
            };
            mtf.CachedIndicators[("binance", "BTC/USDT", "1d", "TEST")] =
                new Dictionary<string, double[]>
                {
                    ["Value"] = new[] { 1.0, 2.0, 10.0, -99.0 }
                };

            var eval = new ConditionEvaluator(catalog, mtf, levels: null);
            var leaf = new ConditionLeaf("indLeaf", "TEST.Value", LeafOperator.GreaterThan, Value: 5, Timeframe: "1d");

            var history = new List<Ohlcv>
            {
                // Perfect alignment with the last HTF bar — the last HTF bar is the one
                // currently forming and must be excluded.
                new Ohlcv(new DateTime(2026, 04, 23), 1, 1, 1, 1, 0),
            };
            var result = eval.Evaluate(leaf, history, StubState());
            Assert.True(result.OverallTrue);
        }

        // ── Stubs ────────────────────────────────────────────────────────────

        private static WorkspaceState StubState()
        {
            return WorkspaceState.Initial with
            {
                Identity = new ChartIdentity("Spot", "binance", "BTC/USDT", "5m"),
            };
        }

        private static IReadOnlyList<Ohlcv> NewHistory(double[] closes)
        {
            var list = new List<Ohlcv>(closes.Length);
            var t0 = new DateTime(2026, 04, 23, 0, 0, 0);
            for (int i = 0; i < closes.Length; i++)
                list.Add(new Ohlcv(t0.AddMinutes(i * 5), closes[i], closes[i], closes[i], closes[i], 0));
            return list;
        }

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
            public Dictionary<(string p, string s, string tf, string code), Dictionary<string, double[]>> CachedIndicators { get; } = new();

            public Task<IReadOnlyList<Ohlcv>> GetBarsAsync(string market, string provider, string symbol, string timeframe, int count)
                => Task.FromResult(GetCachedBars(provider, symbol, timeframe));

            public IReadOnlyList<Ohlcv> GetCachedBars(string provider, string symbol, string timeframe)
                => CachedBars.TryGetValue((provider, symbol, timeframe), out var v) ? v : Array.Empty<Ohlcv>();

            public void Clear() { CachedBars.Clear(); CachedIndicators.Clear(); }

            public Task PrewarmIndicatorAsync(string market, string provider, string symbol, string timeframe,
                string indicatorCode, Dictionary<string, object> parameters, int count)
                => Task.CompletedTask;

            public Dictionary<string, double[]>? GetCachedIndicator(string provider, string symbol, string timeframe, string indicatorCode)
                => CachedIndicators.TryGetValue((provider, symbol, timeframe, indicatorCode), out var v) ? v : null;
        }

        private sealed class CapturingListener : TraceListener
        {
            public List<string> Lines { get; } = new();
            private readonly StringBuilder _buf = new();
            public override void Write(string? message) => _buf.Append(message);
            public override void WriteLine(string? message)
            {
                _buf.Append(message);
                Lines.Add(_buf.ToString());
                _buf.Clear();
            }
        }
    }
}
