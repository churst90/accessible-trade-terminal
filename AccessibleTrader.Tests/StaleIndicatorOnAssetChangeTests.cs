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

        [Fact]
        public async Task HeikinAshiToggle_ForcesFullRecalculation()
        {
            // Indicators compute on the candles the chart shows (IndicatorCandles), so switching
            // Heikin-Ashi on or off leaves every buffer describing the other candle type. Nothing
            // else changes — same asset, same bars, same series — so nothing else would recompute.
            var (svc, store, data, spy) = BuildOrchestration();

            store.EmitState(ReadyState("Bitstamp", "BTC/USD", "1h"));
            data.RaiseDataUpdated();
            await spy.WaitForAnyCallAsync();
            spy.Reset();

            store.EmitState(ReadyState("Bitstamp", "BTC/USD", "1h") with { IsHeikinAshi = true });
            await spy.WaitForAnyCallAsync();

            Assert.True(spy.FullCalls > 0, "switching Heikin-Ashi must recalculate every bar");
            Assert.Equal(0, spy.LastOnlyCalls);

            svc.Dispose();
        }

        // ── Built charts compute on the bars they draw ───────────────────────
        //
        // Found in the chart-type photographs, 2026-11-20: on Renko, range bars, line break, Kagi
        // and point and figure every indicator (the volume pane included) was computed on the
        // feed's time bars and drawn one value per brick, and choosing a built type recomputed
        // nothing.

        /// <summary>Bricks: what the store holds as Data on a built chart while the feed
        /// still holds its twenty time bars.</summary>
        private static TimeSeriesBuffer<Ohlcv> Bricks(int count, double lastClose = 0)
        {
            var t0 = new DateTime(2026, 08, 20, 0, 0, 0, DateTimeKind.Utc);
            var bars = Enumerable.Range(0, count)
                .Select(i => new Ohlcv(t0.AddHours(i * 5), 100 + i, 101 + i, 100 + i, 101 + i, 1))
                .ToList();
            if (lastClose > 0) bars[^1] = bars[^1] with { Close = lastClose, High = lastClose };
            return new TimeSeriesBuffer<Ohlcv>(bars);
        }

        private static WorkspaceState Built(TimeSeriesBuffer<Ohlcv> bricks, PriceChartStyle style = PriceChartStyle.Renko) =>
            ReadyState("Bitstamp", "BTC/USD", "1h") with
            {
                ChartStyle = style,
                BrickBoxSize = 1,
                Data = bricks,
                TimeBars = new TimeSeriesBuffer<Ohlcv>(MakeBars(20)),
            };

        [Fact]
        public async Task Choosing_a_built_chart_recomputes_every_indicator_on_its_bricks()
        {
            var (svc, store, data, spy) = BuildOrchestration();
            store.EmitState(ReadyState("Bitstamp", "BTC/USD", "1h"));
            data.RaiseDataUpdated();
            await spy.WaitForAnyCallAsync();
            spy.Reset();

            var bricks = Bricks(4);
            store.EmitState(Built(bricks));
            await spy.WaitForAnyCallAsync();

            Assert.True(spy.FullCalls > 0, "choosing Renko must recalculate every bar");
            Assert.Same(bricks, spy.LastData);
            svc.Dispose();
        }

        [Fact]
        public async Task A_new_brick_is_a_full_recalculation_on_the_bricks_and_the_feed_does_not_start_one()
        {
            var (svc, store, data, spy) = BuildOrchestration();
            store.EmitState(Built(Bricks(4)));
            await spy.WaitForAnyCallAsync();
            spy.Reset();

            // The feed's tick alone: the store has not rebuilt yet, so nothing may compute on
            // the time bars (or on the old bricks) from it.
            data.SetBars(MakeBars(21));
            data.RaiseDataUpdated();
            await Task.Delay(200);
            Assert.Equal(0, spy.FullCalls + spy.LastOnlyCalls);

            var five = Bricks(5);
            store.EmitState(Built(five));
            await spy.WaitForAnyCallAsync();

            Assert.True(spy.FullCalls > 0, "a new brick shifts every index the buffers were built for");
            Assert.Same(five, spy.LastData);
            svc.Dispose();
        }

        [Fact]
        public async Task A_change_to_the_last_brick_alone_takes_the_incremental_path_on_the_bricks()
        {
            var (svc, store, data, spy) = BuildOrchestration();
            store.EmitState(Built(Bricks(4)));
            await spy.WaitForAnyCallAsync();
            spy.Reset();

            var moved = Bricks(4, lastClose: 103.5);
            store.EmitState(Built(moved));
            await spy.WaitForAnyCallAsync();

            Assert.True(spy.LastOnlyCalls > 0);
            Assert.Equal(0, spy.FullCalls);
            Assert.Same(moved, spy.LastData);
            svc.Dispose();
        }

        [Fact]
        public async Task Leaving_a_built_chart_recomputes_on_the_time_bars()
        {
            var (svc, store, data, spy) = BuildOrchestration();
            store.EmitState(Built(Bricks(4), PriceChartStyle.Kagi));
            await spy.WaitForAnyCallAsync();
            spy.Reset();

            store.EmitState(ReadyState("Bitstamp", "BTC/USD", "1h"));
            await spy.WaitForAnyCallAsync();

            Assert.True(spy.FullCalls > 0);
            Assert.Same(data.Data, spy.LastData);
            svc.Dispose();
        }

        [Fact]
        public async Task Another_built_type_with_as_many_bars_is_still_a_full_recalculation()
        {
            var (svc, store, data, spy) = BuildOrchestration();
            store.EmitState(Built(Bricks(4), PriceChartStyle.Renko));
            await spy.WaitForAnyCallAsync();
            spy.Reset();

            store.EmitState(Built(Bricks(4), PriceChartStyle.LineBreak));
            await spy.WaitForAnyCallAsync();

            Assert.True(spy.FullCalls > 0);
            Assert.Equal(0, spy.LastOnlyCalls);
            svc.Dispose();
        }

        [Fact]
        public async Task A_full_recalculation_cancelled_by_a_later_tick_is_still_owed()
        {
            // Every trigger cancels the one before it. A viewport move straight after a chart type
            // change asks only for the last bar; it cancelled the full recalculation still waiting
            // its turn, and the buffers stayed sized for the old bars (a blank Renko volume pane).
            var (svc, store, data, spy) = BuildOrchestration();
            store.EmitState(ReadyState("Bitstamp", "BTC/USD", "1h"));
            data.RaiseDataUpdated();
            await spy.WaitForAnyCallAsync();
            spy.Reset();

            var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            spy.HoldNextFull = hold;
            store.EmitState(ReadyState("Bitstamp", "BTC/USD", "1h") with { IsHeikinAshi = true });
            await spy.WaitForAnyCallAsync();       // the full one has started and holds the lock

            data.SetBars(MakeBars(21));
            data.RaiseDataUpdated();               // a plain tick: cancels the full one's token
            await Task.Delay(100);
            spy.Reset();
            hold.SetResult();
            await spy.WaitForAnyCallAsync();       // the tick's own recalculation

            Assert.True(spy.LastWasFull, "the tick after a cancelled full recalculation must do the full one");
            svc.Dispose();
        }

        [Fact]
        public async Task A_cancelled_full_recalculation_does_not_mark_the_debt_paid()
        {
            // Three triggers in a row: the full one (1) is overtaken by a tick (2) that takes the
            // owed full recalculation but must wait for the lock; when (1) finishes, cancelled, it
            // must not clear the debt, or a third tick (3) that overtakes (2) goes back to the
            // last-bar update while nothing full has finished.
            var (svc, store, data, spy) = BuildOrchestration();
            store.EmitState(ReadyState("Bitstamp", "BTC/USD", "1h"));
            data.RaiseDataUpdated();
            await spy.WaitForAnyCallAsync();
            spy.Reset();

            var hold1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            spy.HoldNextFull = hold1;
            store.EmitState(ReadyState("Bitstamp", "BTC/USD", "1h") with { IsHeikinAshi = true });
            await spy.WaitForAnyCallAsync();       // (1) holds the lock

            var hold2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            data.SetBars(MakeBars(21));
            data.RaiseDataUpdated();               // (2): owed full, waiting for the lock
            await Task.Delay(100);
            spy.HoldNextFull = hold2;
            spy.Reset();
            hold1.SetResult();
            await spy.WaitForAnyCallAsync();       // (2) has the lock and holds it

            data.SetBars(MakeBars(22));
            data.RaiseDataUpdated();               // (3) cancels (2)
            await Task.Delay(100);
            spy.Reset();
            hold2.SetResult();
            await spy.WaitForAnyCallAsync();

            Assert.True(spy.LastWasFull, "nothing full finished uncancelled, so (3) still owes it");
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
            public IReadOnlyList<Ohlcv>? LastData;
            public bool LastWasFull;
            /// <summary>When set, the next full recalculation waits for it (holding the
            /// service's lock, as a slow real one does).</summary>
            public TaskCompletionSource? HoldNextFull;

            public async Task RecalculateAllAsync(IReadOnlyList<Ohlcv> data, IEnumerable<ChartSeries> series,
                CancellationToken ct, List<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)>? historicalBooks = null)
            {
                var hold = Interlocked.Exchange(ref HoldNextFull, null);
                if (hold != null)
                {
                    _called.Release();
                    await hold.Task;
                }
                LastData = data;
                LastWasFull = true;
                Interlocked.Increment(ref FullCalls);
                if (hold == null) _called.Release();
            }

            public Task RecalculateLastAsync(IReadOnlyList<Ohlcv> data, IEnumerable<ChartSeries> series,
                CancellationToken ct, List<OrderBookEntry>? lastBids = null, List<OrderBookEntry>? lastAsks = null)
            {
                LastData = data;
                LastWasFull = false;
                Interlocked.Increment(ref LastOnlyCalls);
                _called.Release();
                return Task.CompletedTask;
            }

            public void Reset()
            {
                LastData = null;
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
