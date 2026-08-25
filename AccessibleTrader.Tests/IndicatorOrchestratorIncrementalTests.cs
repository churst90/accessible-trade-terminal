using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Tier 2 coverage for <see cref="IndicatorOrchestrator.RecalculateLastAsync"/>'s
    /// incremental grow-vs-overwrite branch. The 2026-04-23 audit initially called this
    /// branch wrong; on re-read it was correct. The behaviour is:
    ///
    ///   data.Count == arr.Length  → same-bar tick: overwrite <c>arr[^1]</c> in place.
    ///   data.Count &gt; arr.Length → first-tick-of-new-bar (or jumped bars): allocate
    ///                                a new array of length data.Count, fill with NaN,
    ///                                copy the old values into the head, write the fresh
    ///                                value at <c>newArr[^1]</c>. Missing bars in the
    ///                                middle remain NaN so they never fire signals.
    ///   key missing from buffer    → silently skipped (TryGetValue branch).
    ///   empty data                 → early return, no dispatch.
    /// </summary>
    public class IndicatorOrchestratorIncrementalTests
    {
        [Fact]
        public async Task RecalculateLastAsync_SameBarTick_OverwritesCurrentBarInPlace()
        {
            // Existing buffer length (3) == data.Count (3) → same-bar update path.
            // The tail value (previously 30) must be overwritten with the engine's fresh 42.
            var original = new[] { 10.0, 20.0, 30.0 };
            var (orch, store) = Build(
                engineIncremental: (code, data, prev) => new Dictionary<string, double> { ["rsi"] = 42.0 });

            var series = MakeIndicatorSeries("rsi", "RSI", ("rsi", original));
            var data = MakeBars(3);

            await orch.RecalculateLastAsync(data, new[] { series }, CancellationToken.None);

            var dispatched = AssertDispatched(store, series.Id);
            var result = dispatched.Data.ComponentData["rsi"];
            Assert.Equal(3, result.Length);
            Assert.Equal(10.0, result[0]);
            Assert.Equal(20.0, result[1]);
            Assert.Equal(42.0, result[2]);
        }

        [Fact]
        public async Task RecalculateLastAsync_FirstTickOfNewBar_GrowsArrayAndWritesTail()
        {
            // data.Count (4) > arr.Length (3) → grow path. The new tail lands at index 3;
            // indices 0-2 keep their pre-tick values (copied via Array.Copy).
            var original = new[] { 10.0, 20.0, 30.0 };
            var (orch, store) = Build(
                engineIncremental: (code, data, prev) => new Dictionary<string, double> { ["rsi"] = 99.0 });

            var series = MakeIndicatorSeries("rsi", "RSI", ("rsi", original));
            var data = MakeBars(4);

            await orch.RecalculateLastAsync(data, new[] { series }, CancellationToken.None);

            var dispatched = AssertDispatched(store, series.Id);
            var result = dispatched.Data.ComponentData["rsi"];
            Assert.Equal(4, result.Length);
            Assert.Equal(10.0, result[0]);
            Assert.Equal(20.0, result[1]);
            Assert.Equal(30.0, result[2]);
            Assert.Equal(99.0, result[3]);
        }

        [Fact]
        public async Task RecalculateLastAsync_SlowDataArrival_FillsSkippedBarsWithNaN()
        {
            // data.Count jumped by 3 (6 bars) vs. the stored array (3 entries). The grow
            // path still runs, but the three middle bars must land as NaN so they never
            // fire signals — Array.Fill(newArr, NaN) before Array.Copy guarantees this.
            var original = new[] { 1.0, 2.0, 3.0 };
            var (orch, store) = Build(
                engineIncremental: (code, data, prev) => new Dictionary<string, double> { ["rsi"] = 77.0 });

            var series = MakeIndicatorSeries("rsi", "RSI", ("rsi", original));
            var data = MakeBars(6);

            await orch.RecalculateLastAsync(data, new[] { series }, CancellationToken.None);

            var dispatched = AssertDispatched(store, series.Id);
            var result = dispatched.Data.ComponentData["rsi"];
            Assert.Equal(6, result.Length);
            Assert.Equal(1.0, result[0]);
            Assert.Equal(2.0, result[1]);
            Assert.Equal(3.0, result[2]);
            Assert.True(double.IsNaN(result[3]));
            Assert.True(double.IsNaN(result[4]));
            Assert.Equal(77.0, result[5]);
        }

        [Fact]
        public async Task RecalculateLastAsync_EngineKeyNotInBuffer_SilentlySkipped()
        {
            // The engine returned a key ("typo_key") that doesn't exist on the buffer.
            // The orchestrator's TryGetValue branch makes this a no-op for that key;
            // the known "rsi" key still updates correctly. Guards against plugin-author
            // typos — the corresponding ValidateBufferKeys warning fires separately
            // on the full-recalc path; the incremental path just drops the stray write.
            var original = new[] { 10.0, 20.0, 30.0 };
            var (orch, store) = Build(
                engineIncremental: (code, data, prev) => new Dictionary<string, double>
                {
                    ["rsi"]       = 44.0,
                    ["typo_key"]  = 999.0,
                });

            var series = MakeIndicatorSeries("rsi", "RSI", ("rsi", original));
            var data = MakeBars(3);

            await orch.RecalculateLastAsync(data, new[] { series }, CancellationToken.None);

            var dispatched = AssertDispatched(store, series.Id);
            Assert.True(dispatched.Data.ComponentData.ContainsKey("rsi"));
            Assert.False(dispatched.Data.ComponentData.ContainsKey("typo_key"));
            Assert.Equal(44.0, dispatched.Data.ComponentData["rsi"][^1]);
        }

        [Fact]
        public async Task RecalculateLastAsync_EmptyData_EarlyReturnsWithoutDispatch()
        {
            // data.Count == 0 triggers the early return — no engine call, no dispatch.
            bool engineCalled = false;
            var (orch, store) = Build(engineIncremental: (code, data, prev) =>
            {
                engineCalled = true;
                return new Dictionary<string, double>();
            });

            var series = MakeIndicatorSeries("rsi", "RSI", ("rsi", new[] { 1.0, 2.0 }));

            await orch.RecalculateLastAsync(Array.Empty<Ohlcv>(), new[] { series }, CancellationToken.None);

            Assert.False(engineCalled);
            Assert.Empty(store.DispatchedActions);
        }

        [Fact]
        public async Task RecalculateLastAsync_Cancelled_StopsBeforeEngineCall()
        {
            // Pre-cancelled token: the foreach body exits before any engine work happens.
            // Protects a shutdown path — orchestrator must never hand a cancelled token's
            // remaining work to the engine.
            bool engineCalled = false;
            var (orch, store) = Build(engineIncremental: (code, data, prev) =>
            {
                engineCalled = true;
                return new Dictionary<string, double>();
            });

            var series = MakeIndicatorSeries("rsi", "RSI", ("rsi", new[] { 1.0, 2.0 }));

            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await orch.RecalculateLastAsync(MakeBars(3), new[] { series }, cts.Token);

            Assert.False(engineCalled);
            Assert.Empty(store.DispatchedActions);
        }

        [Fact]
        public async Task RecalculateLastAsync_MultipleComponents_GrowAndOverwriteIndependently()
        {
            // One series carrying two component arrays of different lengths (simulating a
            // rewind/edge case where one key is already up-to-date and the other needs
            // growing). The new-bar growth must only affect the shorter array; the already-
            // aligned one takes the overwrite path.
            var shortArr = new[] { 10.0, 20.0, 30.0 };      // length 3 < data.Count=4 → grow
            var longArr  = new[] { 1.0, 2.0, 3.0, 4.0 };     // length 4 == data.Count=4 → overwrite
            var (orch, store) = Build(
                engineIncremental: (code, data, prev) => new Dictionary<string, double>
                {
                    ["short"] = 77.0,
                    ["long"]  = 88.0,
                });

            var series = MakeIndicatorSeries("ind", "IND", ("short", shortArr), ("long", longArr));
            var data = MakeBars(4);

            await orch.RecalculateLastAsync(data, new[] { series }, CancellationToken.None);

            var dispatched = AssertDispatched(store, series.Id);
            var shortResult = dispatched.Data.ComponentData["short"];
            var longResult  = dispatched.Data.ComponentData["long"];
            Assert.Equal(4, shortResult.Length);
            Assert.Equal(4, longResult.Length);
            Assert.Equal(77.0, shortResult[^1]);
            Assert.Equal(30.0, shortResult[2]); // preserved via Array.Copy
            Assert.Equal(88.0, longResult[^1]);
            Assert.Equal(1.0, longResult[0]);   // head preserved by in-place overwrite
        }

        // ── Scrollback: bars that arrive at the FRONT ────────────────────────
        //
        // Reported from real use, 2026-08-25: "when I zoom out the chart and pan, only the
        // latest bar on indicators is populated… as I scroll back it deletes it or something."
        //
        // Every grow case above is an APPEND — new bars at the end, so `Array.Copy(old, new)`
        // leaves each old value on the bar it was computed from. A scrollback PREPEND grows the
        // array by the same rule and is silently wrong: the old values stay at indices 0..n-1
        // while the bars they belong to have moved right by however many were prepended. The
        // chart then shows the previous history's values smeared onto the new older bars, NaN
        // across the middle, and a single fresh value at the right edge.
        //
        // Array lengths alone cannot tell the two apart — 3 values against 6 bars is the same
        // arithmetic either way. The buffer has to know which bar it starts at.

        [Fact]
        public async Task RecalculateLastAsync_AfterAScrollbackPrepend_RecomputesInsteadOfSmearing()
        {
            // Buffer was computed over bars 3,4,5 and stamped with bar 3's date. A prepend
            // makes the series bars 0..5, so index 0 is now bar 0 — a bar this buffer has
            // never seen a value for.
            var (orch, store, engine) = BuildWithEngine(
                engineIncremental: (code, data, prev) => new Dictionary<string, double> { ["rsi"] = 99.0 },
                engineFull: data => new Dictionary<string, double[]>
                {
                    ["rsi"] = Enumerable.Range(0, data.Count).Select(i => 1000.0 + i).ToArray(),
                });

            var series = MakeIndicatorSeries("rsi", "RSI", BarTime(3), ("rsi", new[] { 10.0, 20.0, 30.0 }));

            await orch.RecalculateLastAsync(MakeBars(0, 6), new[] { series }, CancellationToken.None);

            Assert.Equal(1, engine.FullCalcCalls);

            var result = AssertDispatched(store, series.Id).Data.ComponentData["rsi"];
            Assert.Equal(6, result.Length);
            // Every bar carries a value again, and it is the recomputed one — not the old
            // history shifted onto the wrong bars.
            Assert.Equal(Enumerable.Range(0, 6).Select(i => 1000.0 + i), result);
            Assert.DoesNotContain(10.0, result);
        }

        [Fact]
        public async Task RecalculateLastAsync_AfterAScrollbackPrepend_StampsTheNewFirstBar()
        {
            // The stamp has to move with the data, or the very next tick sees a mismatch again
            // and every tick pays for a full recalculation forever.
            var (orch, store, _) = BuildWithEngine(
                engineIncremental: (code, data, prev) => new Dictionary<string, double> { ["rsi"] = 99.0 },
                engineFull: data => new Dictionary<string, double[]> { ["rsi"] = new double[data.Count] });

            var series = MakeIndicatorSeries("rsi", "RSI", BarTime(3), ("rsi", new[] { 10.0, 20.0, 30.0 }));

            await orch.RecalculateLastAsync(MakeBars(0, 6), new[] { series }, CancellationToken.None);

            Assert.Equal(BarTime(0), AssertDispatched(store, series.Id).Data.FirstBarDate);
        }

        [Fact]
        public async Task RecalculateLastAsync_OrdinaryNewBar_StaysIncremental()
        {
            // The control, and the reason the fix is a DATE check rather than "recalculate
            // whenever the length changed": an appended bar must still take the cheap path,
            // or every tick on every chart becomes a full recalculation of every indicator.
            var (orch, store, engine) = BuildWithEngine(
                engineIncremental: (code, data, prev) => new Dictionary<string, double> { ["rsi"] = 99.0 },
                engineFull: data => new Dictionary<string, double[]> { ["rsi"] = new double[data.Count] });

            var series = MakeIndicatorSeries("rsi", "RSI", BarTime(0), ("rsi", new[] { 10.0, 20.0, 30.0 }));

            await orch.RecalculateLastAsync(MakeBars(0, 4), new[] { series }, CancellationToken.None);

            Assert.Equal(0, engine.FullCalcCalls);
            var result = AssertDispatched(store, series.Id).Data.ComponentData["rsi"];
            Assert.Equal(new[] { 10.0, 20.0, 30.0, 99.0 }, result);
        }

        [Fact]
        public async Task RecalculateLastAsync_UnstampedBuffer_IsTreatedAsAligned()
        {
            // Buffers built outside the orchestrator (SeriesManagementService, a restored
            // workspace, the backtester) carry no stamp. Treating that as a mismatch would
            // make the first tick after every one of them a full recalculation; treating it as
            // aligned is safe because an unstamped buffer is either empty — which the caller
            // already routes to a full recalculation — or was built from this same data.
            var (orch, store, engine) = BuildWithEngine(
                engineIncremental: (code, data, prev) => new Dictionary<string, double> { ["rsi"] = 99.0 },
                engineFull: data => new Dictionary<string, double[]> { ["rsi"] = new double[data.Count] });

            var series = MakeIndicatorSeries("rsi", "RSI", ("rsi", new[] { 10.0, 20.0, 30.0 }));
            series.Data.FirstBarDate = null;

            await orch.RecalculateLastAsync(MakeBars(0, 3), new[] { series }, CancellationToken.None);

            Assert.Equal(0, engine.FullCalcCalls);
            Assert.Equal(99.0, AssertDispatched(store, series.Id).Data.ComponentData["rsi"][^1]);
        }

        // ── Fixtures ─────────────────────────────────────────────────────────

        private static (IndicatorOrchestrator orch, MockWorkspaceStore store) Build(
            Func<string, IReadOnlyList<Ohlcv>, Dictionary<string, double[]>, Dictionary<string, double>> engineIncremental)
        {
            var (orch, store, _) = BuildWithEngine(engineIncremental, null);
            return (orch, store);
        }

        private static (IndicatorOrchestrator orch, MockWorkspaceStore store, StubIndicatorEngine engine) BuildWithEngine(
            Func<string, IReadOnlyList<Ohlcv>, Dictionary<string, double[]>, Dictionary<string, double>> engineIncremental,
            Func<IReadOnlyList<Ohlcv>, Dictionary<string, double[]>>? engineFull)
        {
            var store = new MockWorkspaceStore();
            var engine = new StubIndicatorEngine(engineIncremental, engineFull);
            var mapper = new IndicatorStateMapper();
            var drawing = new NoOpDrawingService();
            var profile = new NoOpProfileService();
            var heatmap = new NoOpHeatmapService();
            var notifications = new MockNotificationHub();
            var logger = NullLogger<IndicatorOrchestrator>.Instance;
            var orch = new IndicatorOrchestrator(engine, mapper, drawing, profile, heatmap, store, notifications, logger);
            return (orch, store, engine);
        }

        /// <summary>
        /// Builds a ChartSeries with IndicatorCode set (routes to the incremental path) and
        /// pre-populated ComponentData arrays. Components declare no DataMapping, no
        /// Heatmap/Profile display types, and no IsProfile flag, so RecalculateLastAsync
        /// takes the <c>!string.IsNullOrEmpty(s.IndicatorCode)</c> branch.
        /// </summary>
        private static ChartSeries MakeIndicatorSeries(string id, string code, params (string name, double[] values)[] arrays)
            => MakeIndicatorSeries(id, code, BarTime(0), arrays);

        private static ChartSeries MakeIndicatorSeries(string id, string code, DateTime firstBarDate,
            params (string name, double[] values)[] arrays)
        {
            var cfg = new SeriesConfig
            {
                Id = id,
                Name = id,
                IndicatorCode = code,
                Pane = "Main",
            };
            foreach (var (name, _) in arrays)
            {
                cfg.Components.Add(new ComponentConfig
                {
                    Name = name,
                    DisplayType = ComponentDisplayType.Line,
                });
            }
            var buf = new SeriesDataBuffer { SeriesId = id, FirstBarDate = firstBarDate };
            foreach (var (name, values) in arrays)
                buf.ComponentData[name] = (double[])values.Clone();
            return new ChartSeries(cfg, buf);
        }

        private static readonly DateTime Origin = new(2026, 04, 23);

        private static DateTime BarTime(int index) => Origin.AddMinutes(index);

        private static List<Ohlcv> MakeBars(int count) => MakeBars(0, count);

        /// <summary>Bars <paramref name="firstIndex"/> .. firstIndex+count-1 on the shared minute grid.</summary>
        private static List<Ohlcv> MakeBars(int firstIndex, int count)
        {
            var list = new List<Ohlcv>(count);
            for (int i = 0; i < count; i++)
                list.Add(new Ohlcv(BarTime(firstIndex + i), 100, 100, 100, 100, 0));
            return list;
        }

        private static UpdateSeriesDataAction AssertDispatched(MockWorkspaceStore store, string seriesId)
        {
            var action = store.DispatchedActions
                .OfType<UpdateSeriesDataAction>()
                .FirstOrDefault(a => a.SeriesId == seriesId);
            Assert.NotNull(action);
            return action!;
        }

        // ── Stubs ────────────────────────────────────────────────────────────

        private sealed class StubIndicatorEngine : IIndicatorEngine
        {
            private readonly Func<string, IReadOnlyList<Ohlcv>, Dictionary<string, double[]>, Dictionary<string, double>> _incremental;
            private readonly Func<IReadOnlyList<Ohlcv>, Dictionary<string, double[]>>? _full;

            /// <summary>Records every full-recalculation the orchestrator asked for.</summary>
            public int FullCalcCalls { get; private set; }

            public StubIndicatorEngine(Func<string, IReadOnlyList<Ohlcv>, Dictionary<string, double[]>, Dictionary<string, double>> incremental,
                                       Func<IReadOnlyList<Ohlcv>, Dictionary<string, double[]>>? full = null)
            {
                _incremental = incremental;
                _full = full;
            }

            public Task<Dictionary<string, double[]>> CalculateAsync(string code, IReadOnlyList<Ohlcv> data,
                Dictionary<string, object> parameters, CancellationToken ct)
                => Task.FromResult(new Dictionary<string, double[]>());

            public Task<Dictionary<string, double>> CalculateIncrementalAsync(string code, IReadOnlyList<Ohlcv> data,
                Dictionary<string, object> parameters, Dictionary<string, double[]> previousResults, CancellationToken ct)
                => Task.FromResult(_incremental(code, data, previousResults));

            public IIndicatorProvider? GetProvider(string indicatorCode) => null;

            public Task<(Dictionary<string, double[]> Results, IReadOnlyList<ZoneBandConfig> ZoneBands)>
                CalculateWithBandsAsync(string code, IReadOnlyList<Ohlcv> data, Dictionary<string, object> parameters, CancellationToken ct)
            {
                FullCalcCalls++;
                return Task.FromResult<(Dictionary<string, double[]>, IReadOnlyList<ZoneBandConfig>)>(
                    (_full?.Invoke(data) ?? new Dictionary<string, double[]>(), Array.Empty<ZoneBandConfig>()));
            }
        }

        private sealed class NoOpDrawingService : IDrawingService
        {
            public Dictionary<string, double[]> CalculateDrawingData(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
                => new Dictionary<string, double[]>();
        }

        private sealed class NoOpProfileService : IProfileService
        {
            public List<ProfileBin> CalculateVolumeProfile(IReadOnlyList<Ohlcv> data, int binCount = 50) => new();
            public List<ProfileBin> CalculateMarketProfile(IReadOnlyList<Ohlcv> data, int binCount = 50) => new();
        }

        private sealed class NoOpHeatmapService : IHeatmapService
        {
            public List<List<ProfileBin>> GenerateHeatmap(IReadOnlyList<Ohlcv> data,
                List<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> historicalBooks, double sensitivity) => new();
            public List<ProfileBin> GenerateBarHeatmap(Ohlcv bar, List<OrderBookEntry> bids, List<OrderBookEntry> asks, double sensitivity) => new();
        }
    }
}
