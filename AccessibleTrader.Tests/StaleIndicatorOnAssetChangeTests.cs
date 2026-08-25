using System.Collections.Immutable;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Workspace.Reducers;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Regression cover for the 2026-08-20 report: "when I change the asset and Market Structure
    /// is still on the chart, it still contains the old values from the previous asset — I have to
    /// remove it and re-add it."
    ///
    /// Two independent defects produced that symptom, and both are pinned here.
    ///
    /// <b>1. The flag was dropped on the first calculation.</b> <see cref="ChartSeries.WithData"/>
    /// rebuilt the series without <see cref="ChartSeries.RequiresFullRecalcOnTick"/>, and the store
    /// replaces its series through exactly that method on every UpdateSeriesDataAction. So the
    /// pivot-based indicators (Market Structure, Value Deviation) lost the flag the moment they
    /// were first computed and never took the full-recalc branch again — their historical bars kept
    /// whatever was written the first time. Removing and re-adding the indicator rebuilt it through
    /// IndicatorModelFactory with the flag set, which is exactly the workaround that was reported.
    ///
    /// <b>2. Nothing forced a full recalculation on an asset change.</b> A same-shape switch
    /// (Bitstamp BTC → ETH) deliberately keeps the user's indicators, so every buffer arrives at the
    /// new load holding the previous asset's values. <c>needsFull</c> only fires for series with
    /// EMPTY buffers, so the incremental path ran and refreshed the last bar alone.
    /// </summary>
    public class StaleIndicatorOnAssetChangeTests
    {
        // ── Defect 1: the flag must survive the store round-trip ─────────────

        [Fact]
        public void WithData_PreservesRequiresFullRecalcOnTick()
        {
            var series = MakeSeries("swing", "SWING_STRUCTURE", requiresFullRecalc: true);

            var updated = series.WithData(new SeriesDataBuffer { SeriesId = series.Id });

            Assert.True(updated.RequiresFullRecalcOnTick,
                "WithData must carry the flag; the store never sees the factory-built instance again.");
        }

        [Fact]
        public void SeriesReducer_UpdateSeriesData_PreservesRequiresFullRecalcOnTick()
        {
            // The real loss path: IndicatorOrchestrator dispatches UpdateSeriesDataAction after the
            // first calculation, and the reducer rebuilds the series with WithData.
            var series = MakeSeries("swing", "SWING_STRUCTURE", requiresFullRecalc: true);
            var state = WorkspaceState.Initial with
            {
                ActiveSeries = ImmutableList.Create(series)
            };

            var next = SeriesReducer.Reduce(
                state,
                new UpdateSeriesDataAction(series.Id, new SeriesDataBuffer { SeriesId = series.Id }),
                new EventBus());

            Assert.True(next.ActiveSeries.Single().RequiresFullRecalcOnTick);
        }

        [Fact]
        public void MarketStructureMetadata_StillDeclaresFullRecalcOnTick()
        {
            // If this ever flips to false the pivot markers go stale on every tick again, and the
            // two tests above would keep passing while the behaviour regressed.
            var meta = new AccessibleTrader.Core.Services.Indicators.SwingStructureProvider()
                .GetIndicators()
                .Single();

            Assert.True(meta.RequiresFullRecalcOnTick);
        }

        // ── Defect 2: an asset change must force a full recalculation ────────

        [Fact]
        public async Task AssetChange_ForcesFullRecalculation()
        {
            var (svc, store, data, spy) = BuildOrchestration();

            // Chart A loads and computes.
            store.EmitState(ReadyState("Bitstamp", "BTC/USD", "1h"));
            data.RaiseDataUpdated();
            await spy.WaitForAnyCallAsync();
            spy.Reset();

            // Same shape, different symbol: the series still carry BTC's arrays.
            store.EmitState(ReadyState("Bitstamp", "ETH/USD", "1h"));
            data.RaiseDataUpdated();
            await spy.WaitForAnyCallAsync();

            Assert.True(spy.FullCalls > 0, "an asset change must recalculate every bar, not just the last one");
            Assert.Equal(0, spy.LastOnlyCalls);

            svc.Dispose();
        }

        [Fact]
        public async Task TimeframeChange_ForcesFullRecalculation()
        {
            var (svc, store, data, spy) = BuildOrchestration();

            store.EmitState(ReadyState("Bitstamp", "BTC/USD", "1h"));
            data.RaiseDataUpdated();
            await spy.WaitForAnyCallAsync();
            spy.Reset();

            // Same asset, different granularity — the bars underneath the indicator all changed.
            store.EmitState(ReadyState("Bitstamp", "BTC/USD", "1d"));
            data.RaiseDataUpdated();
            await spy.WaitForAnyCallAsync();

            Assert.True(spy.FullCalls > 0);
            Assert.Equal(0, spy.LastOnlyCalls);

            svc.Dispose();
        }

        [Fact]
        public async Task LiveTickOnSameAsset_StillTakesTheIncrementalPath()
        {
            // The control case. Forcing a full recalculation on every tick would be the lazy fix and
            // a real performance regression, so prove the force is scoped to identity changes.
            var (svc, store, data, spy) = BuildOrchestration();

            store.EmitState(ReadyState("Bitstamp", "BTC/USD", "1h"));
            data.RaiseDataUpdated();
            await spy.WaitForAnyCallAsync();
            spy.Reset();

            // A tick: same identity, one more bar. No new state emission — just fresh data.
            data.SetBars(MakeBars(21));
            data.RaiseDataUpdated();
            await spy.WaitForAnyCallAsync();

            Assert.True(spy.LastOnlyCalls > 0, "a same-asset tick must stay on the cheap incremental path");
            Assert.Equal(0, spy.FullCalls);

            svc.Dispose();
        }

        // ── Fixtures ─────────────────────────────────────────────────────────

        private static (DataOrchestrationService svc, MockWorkspaceStore store, FakeDataManager data, SpyIndicatorOrchestrator spy)
            BuildOrchestration()
        {
            var store = new MockWorkspaceStore();
            var data = new FakeDataManager(MakeBars(20));
            var spy = new SpyIndicatorOrchestrator();
            var svc = new DataOrchestrationService(
                store, data, spy, new OrderBookHistoryService(), new EventBus(),
                NullLogger<DataOrchestrationService>.Instance);
            return (svc, store, data, spy);
        }

        /// <summary>A loaded chart carrying one indicator series that ALREADY holds computed
        /// values — the state an asset switch actually starts from.</summary>
        private static WorkspaceState ReadyState(string provider, string symbol, string timeframe)
        {
            var series = MakeSeries("swing", "SWING_STRUCTURE", requiresFullRecalc: true);
            series.Data.ComponentData["SwingHigh"] = Enumerable.Repeat(72000.0, 20).ToArray();

            return WorkspaceState.Initial with
            {
                Identity = new ChartIdentity("Spot", provider, symbol, timeframe),
                InitStatus = InitializationStatus.Ready,
                DataStatus = DataStatus.Ready,
                ActiveSeries = ImmutableList.Create(series)
            };
        }

        private static ChartSeries MakeSeries(string id, string code, bool requiresFullRecalc)
        {
            var cfg = new SeriesConfig { Id = id, Name = id, IndicatorCode = code, Pane = "Main" };
            cfg.Components.Add(new ComponentConfig { Name = "SwingHigh", DisplayType = ComponentDisplayType.Square });
            return new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = id })
            {
                RequiresFullRecalcOnTick = requiresFullRecalc
            };
        }

        private static List<Ohlcv> MakeBars(int count)
        {
            var t0 = new DateTime(2026, 08, 20, 0, 0, 0, DateTimeKind.Utc);
            return Enumerable.Range(0, count)
                .Select(i => new Ohlcv(t0.AddHours(i), 100, 101, 99, 100, 1))
                .ToList();
        }

        // ── Doubles ──────────────────────────────────────────────────────────

        /// <summary>MockDataManager can't raise DataUpdated from a test, and these tests are
        /// entirely about what happens when it fires.</summary>
        private sealed class FakeDataManager : IDataManager
        {
            private TimeSeriesBuffer<Ohlcv> _bars;

            public FakeDataManager(IEnumerable<Ohlcv> bars) => _bars = new TimeSeriesBuffer<Ohlcv>(bars);

            public void SetBars(IEnumerable<Ohlcv> bars) => _bars = new TimeSeriesBuffer<Ohlcv>(bars);
            public void RaiseDataUpdated() => DataUpdated?.Invoke();

            public TimeSeriesBuffer<Ohlcv> Data => _bars;
            public ChartIdentity Identity { get; set; } = ChartIdentity.Empty;
            public Task RefreshDataAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task CatchUpFromSnapshotAsync(TimeSeriesBuffer<Ohlcv> snapshotData, CancellationToken ct = default) => Task.CompletedTask;
            public Task PrependOlderDataAsync() => Task.CompletedTask;
            public Task StartLiveUpdates() => Task.CompletedTask;
            public Task StopLiveUpdatesAsync() => Task.CompletedTask;
            public Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync()
                => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

            public event Action? DataUpdated;
#pragma warning disable CS0067
            public event Action<string>? ErrorOccurred;
#pragma warning restore CS0067
        }

        private sealed class SpyIndicatorOrchestrator : IIndicatorOrchestrator
        {
            private readonly SemaphoreSlim _called = new(0);

            public int FullCalls;
            public int LastOnlyCalls;

            public Task RecalculateAllAsync(IReadOnlyList<Ohlcv> data, IEnumerable<ChartSeries> series,
                CancellationToken ct, List<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)>? historicalBooks = null)
            {
                Interlocked.Increment(ref FullCalls);
                _called.Release();
                return Task.CompletedTask;
            }

            public Task RecalculateLastAsync(IReadOnlyList<Ohlcv> data, IEnumerable<ChartSeries> series,
                CancellationToken ct, List<OrderBookEntry>? lastBids = null, List<OrderBookEntry>? lastAsks = null)
            {
                Interlocked.Increment(ref LastOnlyCalls);
                _called.Release();
                return Task.CompletedTask;
            }

            public void Reset()
            {
                Interlocked.Exchange(ref FullCalls, 0);
                Interlocked.Exchange(ref LastOnlyCalls, 0);
                while (_called.CurrentCount > 0) _called.Wait(0);
            }

            /// <summary>The recalculation runs on a fire-and-forget task, so the assertions have to
            /// wait for it rather than assume it already happened.</summary>
            public async Task WaitForAnyCallAsync()
            {
                Assert.True(await _called.WaitAsync(TimeSpan.FromSeconds(5)),
                    "no recalculation was triggered within 5s");
                // Let any same-trigger follow-up land so the counters are stable when read.
                await Task.Delay(50);
            }
        }
    }
}
