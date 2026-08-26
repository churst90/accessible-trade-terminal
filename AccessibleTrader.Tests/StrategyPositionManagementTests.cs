using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Core.Strategies;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The live half of the exit plan — the take-profit ladder, the move to breakeven, the
    /// ratcheting ATR trail — and the position memory that survives a restart.
    ///
    /// <para>
    /// What these guard is the gap between two things the user is invited to treat as one: the
    /// backtest they accepted the strategy on, and the order the strategy actually places. The
    /// live path used to build a six-field <c>TradeSignal</c> and drop <c>TpLadder</c>,
    /// <c>TpClosePortions</c>, <c>StopAdjust</c>, <c>TrailAtrPeriod</c> and
    /// <c>TrailAtrMultiple</c> on the floor, know nothing about the position it already held,
    /// and come back from a restart flat while the broker still held it. Each of those has a
    /// test here, and the headline one is <see cref="Live_exits_match_the_replayed_exits_bar_for_bar"/>:
    /// the same bars and the same signal through <see cref="StrategyBacktester"/> and through
    /// <see cref="StrategyPositionManager"/> have to produce the same exits in the same order at
    /// the same sizes, because that is what "the backtest is about this strategy" means.
    /// </para>
    /// </summary>
    public class StrategyPositionManagementTests : IDisposable
    {
        private readonly string _dir;

        public StrategyPositionManagementTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "att-managed-positions-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
            // The trail-scaling test needs one app-data directory per multiple, so it makes
            // siblings of _dir; sweep those too rather than leaving them in the temp folder.
            foreach (var sibling in Directory.EnumerateDirectories(
                         Path.GetDirectoryName(_dir)!, Path.GetFileName(_dir) + "-m*"))
            {
                try { Directory.Delete(sibling, recursive: true); } catch { /* best effort */ }
            }
        }

        // ── Harness ──────────────────────────────────────────────────────────────

        private sealed class FakePaths : IPlatformPathService
        {
            public FakePaths(string dir) { AppDataDirectory = dir; CacheDirectory = dir; }
            public string AppDataDirectory { get; }
            public string CacheDirectory { get; }
        }

        /// <summary>Records every order placed and can be told to refuse.</summary>
        private sealed class RecordingOrders : IOrderExecutionService
        {
            public readonly List<(string Provider, TradeSignal Signal)> Placed = new();
            public Func<TradeSignal, string>? Answer;
            public ProviderResult<List<Position>>? Positions;

            /// <summary>
            /// The scripted answer is still written in the wire vocabulary — that is what a
            /// provider actually returns — and parsed by the same recogniser production uses, so
            /// a test cannot hand the manager an outcome no provider could produce.
            /// </summary>
            public Task<OrderPlacement> PlaceOrderAsync(string provider, TradeSignal signal)
            {
                Placed.Add((provider, signal));
                return Task.FromResult(
                    OrderPlacement.Parse(Answer?.Invoke(signal) ?? ("order-" + Placed.Count)));
            }

            public Task<ProviderResult<List<Position>>> GetPositionsAsync(string provider) =>
                Task.FromResult(Positions ?? ProviderResult<List<Position>>.Ok(new List<Position>()));

            // Everything else is unused by these tests.
            public Task<bool> CancelOrderAsync(string provider, string orderId, string symbol) => Task.FromResult(true);
            public Task<bool> SupportsOcoPairsAsync(string provider) => Task.FromResult(false);
            public Task<(bool Ok, string Message)> PlaceOcoPairAsync(string provider, string symbol,
                OrderSide side, double quantity, double limitPrice, double stopTriggerPrice) => Task.FromResult((false, ""));
            public Task<ProviderResult<List<Balance>>> GetBalancesAsync(string provider) =>
                Task.FromResult(ProviderResult<List<Balance>>.Ok(new List<Balance>()));
            public Task<ProviderResult<List<OpenOrder>>> GetOpenOrdersAsync(string provider, string? symbol = null) =>
                Task.FromResult(ProviderResult<List<OpenOrder>>.Ok(new List<OpenOrder>()));
            public Task<double> GetMaxLeverageAsync(string provider) => Task.FromResult(1.0);
            public Task<double> SetLeverageAsync(string provider, string symbol, double leverage) => Task.FromResult(1.0);
            public Task<bool> SupportsTradingAsync(string provider) => Task.FromResult(true);
            public Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(
                string provider, string symbol, int depth = 10) => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));
            public Task<IObservable<OrderBookUpdate>?> SubscribeOrderBookAsync(string provider, string symbol) =>
                Task.FromResult<IObservable<OrderBookUpdate>?>(null);
            public Task<ProviderResult<List<TradeFill>>> GetFillsAsync(string provider, string? symbol = null, int limit = 50) =>
                Task.FromResult(ProviderResult<List<TradeFill>>.Ok(new List<TradeFill>()));
            public Task<bool> SupportsMarginTradingAsync(string provider) => Task.FromResult(false);
            public Task<ProviderCapabilities> GetCapabilitiesAsync(string provider) =>
                Task.FromResult(ProviderCapabilities.None);
        }

        private sealed class Harness
        {
            public readonly RecordingOrders Orders = new();
            public readonly EventBus Bus = new();
            public readonly StrategyPositionManager Manager;
            public readonly List<string> Spoken = new();

            public Harness(string dir)
            {
                Bus.Subscribe<FeedbackRequestEvent>(e => { if (e.Message != null) Spoken.Add(e.Message); });
                Manager = new StrategyPositionManager(Bus, Orders, Substitute.For<IAppLogger>(), new FakePaths(dir));
            }
        }

        /// <summary>A hand-written counter — NSubstitute's proxy did not record the loader's
        /// call here, and a guard whose instrument is in doubt guards nothing.</summary>
        private sealed class CountingPositions : IStrategyPositionManager
        {
            public int Reconciles;
            public Task ReconcileAsync() { Reconciles++; return Task.CompletedTask; }
            public IReadOnlyList<ManagedStrategyPosition> Open => Array.Empty<ManagedStrategyPosition>();
            public ManagedStrategyPosition? Get(string instanceId) => null;
            public void Adopt(string instanceId, string? specId) { }
            public StrategyEntryPlan PlanEntry(ActiveStrategy active, StrategySignal signal, double quantity,
                string provider, string symbol) => new(StrategyEntryDisposition.Open, null, null);
            public void OpenPosition(ActiveStrategy active, StrategySignal signal, double quantity,
                string provider, string symbol, double referencePrice, string? entryOrderId) { }
            public IReadOnlyList<StrategyExitOrder> OnBarClosed(string instanceId, Ohlcv bar,
                IReadOnlyList<Ohlcv> history) => Array.Empty<StrategyExitOrder>();
            public Task<bool> PlaceExitsAsync(IReadOnlyList<StrategyExitOrder> orders) => Task.FromResult(true);
            public void ExitAccepted(string exitId) { }
            public void ExitRejected(string exitId) { }
            public void Forget(string instanceId) { }
        }

        private static Ohlcv Bar(int minute, double open, double high, double low, double close) =>
            new(new DateTime(2026, 1, 1, 0, minute, 0, DateTimeKind.Utc), open, high, low, close, 1000);

        private static ActiveStrategy Active(string instanceId = "inst-1", string? specId = "spec-1",
            string name = "Ladder strategy")
        {
            var strategy = Substitute.For<ITradingStrategy>();
            strategy.Name.Returns(name);
            return new ActiveStrategy(instanceId, strategy, new Dictionary<string, object>(),
                StrategyExecutionMode.Auto, IsPaused: false, Symbol: "BTC/USD", SpecId: specId);
        }

        private static StrategySignal LadderSignal(
            OrderSide side = OrderSide.Buy,
            double? stop = 98,
            IReadOnlyList<double>? rungs = null,
            IReadOnlyList<double>? portions = null,
            StopAdjustOnTp1 adjust = StopAdjustOnTp1.MoveToBreakeven,
            int atrPeriod = 14,
            double atrMultiple = 1.5) =>
            new(Side: side, OrderType: OrderType.Market, Quantity: 3.0, LimitPrice: null,
                StopLoss: stop, TakeProfit: (rungs ?? new[] { 102.0, 104.0, 106.0 })[0],
                Rationale: "test", Confidence: 1.0,
                TpLadder: rungs ?? new[] { 102.0, 104.0, 106.0 },
                TpClosePortions: portions ?? new[] { 1.0 / 3, 1.0 / 3, 1.0 / 3 },
                StopAdjust: adjust, TrailAtrPeriod: atrPeriod, TrailAtrMultiple: atrMultiple);

        // ── The rungs that used to be dropped ────────────────────────────────────

        [Fact]
        public async Task Every_ladder_rung_fires_live_not_only_the_first()
        {
            var h = new Harness(_dir);
            var active = Active();
            h.Manager.OpenPosition(active, LadderSignal(), quantity: 3.0,
                provider: "Kraken", symbol: "BTC/USD", referencePrice: 100, entryOrderId: "entry-1");

            var bars = new List<Ohlcv> { Bar(0, 100, 100.5, 99.5, 100) };

            // Each bar reaches exactly one more rung.
            foreach (var (high, close) in new[] { (102.5, 102.0), (104.5, 104.0), (106.5, 106.0) })
            {
                bars.Add(Bar(bars.Count, close - 2, high, close - 2.5, close));
                var exits = h.Manager.OnBarClosed("inst-1", bars[^1], bars);
                Assert.True(await h.Manager.PlaceExitsAsync(exits));
            }

            // Three rungs, three reduce-only sells of one unit each — not one sell and two
            // targets the user was told about and never got.
            var sells = h.Orders.Placed.Where(p => p.Signal.Side == OrderSide.Sell).ToList();
            Assert.Equal(3, sells.Count);
            Assert.All(sells, s => Assert.Equal(1.0, s.Signal.Quantity, 6));
            Assert.All(sells, s => Assert.True(s.Signal.ReduceOnly));
            Assert.All(sells, s => Assert.Equal("Kraken", s.Provider));

            // Fully closed: nothing left to manage.
            Assert.Null(h.Manager.Get("inst-1"));
        }

        [Fact]
        public async Task The_stop_moves_to_breakeven_after_the_first_rung()
        {
            var h = new Harness(_dir);
            h.Manager.OpenPosition(Active(), LadderSignal(stop: 98), quantity: 3.0,
                provider: "Kraken", symbol: "BTC/USD", referencePrice: 100, entryOrderId: "entry-1");

            var bars = new List<Ohlcv> { Bar(0, 100, 100.5, 99.5, 100) };

            // Rung one.
            bars.Add(Bar(1, 100, 102.5, 99.8, 102));
            Assert.True(await h.Manager.PlaceExitsAsync(h.Manager.OnBarClosed("inst-1", bars[^1], bars)));
            Assert.Equal(100, h.Manager.Get("inst-1")!.StopPrice);   // entry, not the original 98

            // A bar that dips to 99.5 is now a stop-out. Against the ORIGINAL stop of 98 it
            // would not have been, which is the whole point of the move.
            bars.Add(Bar(2, 102, 102, 99.5, 100));
            var exits = h.Manager.OnBarClosed("inst-1", bars[^1], bars);
            Assert.Single(exits);
            Assert.Equal(2.0, exits[0].Quantity, 6);                 // the whole remainder
            Assert.Contains("stop", exits[0].Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void The_stop_wins_a_bar_that_reaches_both_it_and_a_rung()
        {
            var h = new Harness(_dir);
            h.Manager.OpenPosition(Active(), LadderSignal(stop: 98), quantity: 3.0,
                provider: "Kraken", symbol: "BTC/USD", referencePrice: 100, entryOrderId: "entry-1");

            // High 106 clears all three rungs; low 97 clears the stop. The replay assumes the
            // worse outcome happened first and so must this — a live emulation that guessed the
            // other way would report three targets on a bar the trader was stopped out of.
            var bars = new List<Ohlcv> { Bar(0, 100, 106, 97, 99) };
            var exits = h.Manager.OnBarClosed("inst-1", bars[0], bars);

            Assert.Single(exits);
            Assert.Equal(3.0, exits[0].Quantity, 6);
            Assert.Contains("stop", exits[0].Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task The_atr_trail_ratchets_and_never_retreats()
        {
            var h = new Harness(_dir);
            h.Manager.OpenPosition(
                Active(), LadderSignal(stop: 98, adjust: StopAdjustOnTp1.TrailByAtr, atrPeriod: 3, atrMultiple: 1.0),
                quantity: 3.0, provider: "Kraken", symbol: "BTC/USD", referencePrice: 100, entryOrderId: "entry-1");

            var bars = new List<Ohlcv>
            {
                Bar(0, 100, 100.5, 99.5, 100),
                Bar(1, 100, 100.5, 99.5, 100),
                Bar(2, 100, 100.5, 99.5, 100),
            };

            // Rung one arms the trail.
            bars.Add(Bar(3, 100, 102.5, 99.8, 102));
            Assert.True(await h.Manager.PlaceExitsAsync(h.Manager.OnBarClosed("inst-1", bars[^1], bars)));
            double armed = h.Manager.Get("inst-1")!.StopPrice!.Value;

            // Price runs up: the trail follows.
            bars.Add(Bar(4, 102, 103.5, 102, 103.4));
            h.Manager.OnBarClosed("inst-1", bars[^1], bars);
            double advanced = h.Manager.Get("inst-1")!.StopPrice!.Value;
            Assert.True(advanced > armed, $"the trail did not advance: {armed} → {advanced}");

            // Price falls back without reaching the stop: the trail must NOT follow it down.
            // A trail that retreats is a stop the market has already walked past.
            bars.Add(Bar(5, 103.4, 103.4, advanced + 0.2, advanced + 0.3));
            h.Manager.OnBarClosed("inst-1", bars[^1], bars);
            Assert.Equal(advanced, h.Manager.Get("inst-1")!.StopPrice!.Value, 9);
        }

        /// <summary>
        /// The trail distance is actually read off the SIGNAL, not off a default.
        ///
        /// <para>
        /// This exists because the field scan below could not catch it: a sabotage run that
        /// replaced <c>p.TrailAtrMultiple</c> with a hardcoded 1.5 at the one site that uses it
        /// left the guard green, because the property is still named in the record and in
        /// OpenPosition. A presence check cannot tell "consumed" from "mentioned" — so the
        /// multiple gets pinned behaviourally instead, by running the same bars twice and
        /// asserting the distance doubles when the multiple does.
        /// </para>
        /// </summary>
        [Fact]
        public async Task The_trail_distance_scales_with_the_signals_atr_multiple()
        {
            async Task<double> DistanceFor(double multiple)
            {
                string dir = _dir + "-m" + multiple.ToString("0.0");
                Directory.CreateDirectory(dir);
                var h = new Harness(dir);
                // ONE rung closing half, so the position stays open for the run that follows and
                // no later rung interferes.
                h.Manager.OpenPosition(
                    Active(), LadderSignal(stop: 98, rungs: new[] { 102.0 }, portions: new[] { 0.5 },
                                           adjust: StopAdjustOnTp1.TrailByAtr,
                                           atrPeriod: 3, atrMultiple: multiple),
                    quantity: 3.0, provider: "Kraken", symbol: "BTC/USD",
                    referencePrice: 100, entryOrderId: "entry-1");

                // The run has to be strong enough that even the 2x trail clears the breakeven
                // anchor on the last bar — otherwise the ratchet holds the wide one at the anchor
                // and the two distances are not comparable at all. (The first draft of this test
                // used a gentle run and failed for exactly that reason: correct behaviour, wrong
                // premise.)
                var bars = new List<Ohlcv>
                {
                    Bar(0, 100, 101,   99,   100),
                    Bar(1, 100, 101,   99,   100),
                    Bar(2, 100, 101,   99,   100),
                    Bar(3, 100, 102.5, 99.8, 102),   // rung one arms the trail
                    Bar(4, 102, 106,   101,  105),
                    Bar(5, 105, 110,   104,  109),
                    Bar(6, 109, 114,   108,  113),
                };
                for (int i = 3; i < bars.Count; i++)
                    Assert.True(await h.Manager.PlaceExitsAsync(
                        h.Manager.OnBarClosed("inst-1", bars[i], bars.Take(i + 1).ToList())));

                var open = h.Manager.Get("inst-1");
                Assert.NotNull(open);
                return bars[^1].Close - open!.StopPrice!.Value;
            }

            double one = await DistanceFor(1.0);
            double two = await DistanceFor(2.0);

            Assert.True(one > 0, $"the trail never moved off the entry anchor (distance {one})");
            Assert.Equal(2 * one, two, 6);
        }

        [Fact]
        public async Task The_trail_does_not_run_before_the_first_rung()
        {
            var h = new Harness(_dir);
            // Rungs deliberately out of reach: this is about the trail NOT running, so no rung
            // may fire. (The first draft used the default 102/104/106 ladder and the rising bars
            // cleared rung one, which armed the trail legitimately and made the test read as a
            // bug in the manager.)
            h.Manager.OpenPosition(
                Active(), LadderSignal(stop: 90, rungs: new[] { 200.0 }, portions: new[] { 1.0 },
                                       adjust: StopAdjustOnTp1.TrailByAtr, atrPeriod: 3, atrMultiple: 1.0),
                quantity: 3.0, provider: "Kraken", symbol: "BTC/USD", referencePrice: 100, entryOrderId: "entry-1");

            var bars = new List<Ohlcv>();
            for (int i = 0; i < 6; i++) bars.Add(Bar(i, 100 + i, 100.5 + i, 99.5 + i, 100 + i));

            foreach (var b in bars)
                Assert.True(await h.Manager.PlaceExitsAsync(h.Manager.OnBarClosed("inst-1", b, bars.Take(bars.IndexOf(b) + 1).ToList())));

            // Untouched at the strategy's own level. Before the first rung the stop belongs to
            // the strategy and the trail has no business second-guessing it — same as the replay.
            Assert.Equal(90, h.Manager.Get("inst-1")!.StopPrice);
        }

        // ── Parity with the replay ───────────────────────────────────────────────

        private sealed class OneShotStrategy : ITradingStrategy
        {
            private readonly StrategySignal _signal;
            private int _bars;
            public OneShotStrategy(StrategySignal signal) { _signal = signal; }
            public string Id => "PARITY";
            public string Name => "Parity";
            public string Description => "emits once on the first bar";
            public StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;
            public IReadOnlyList<StrategyParameter> Parameters => Array.Empty<StrategyParameter>();
            public void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state, IDictionary<string, object> p) { _bars = 0; }
            public StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state) =>
                _bars++ == 0 ? _signal : null;
            public void OnOrderFilled(OrderUpdate fill) { }
            public void OnStop() { }
            public StrategyMetrics GetMetrics() => new(0, 0, 0, 0, 0, 0);
        }

        /// <summary>
        /// The headline guard. A 3-rung ladder with a breakeven move, replayed by the backtester
        /// and walked live by the manager over the SAME bars, must close the same quantities in
        /// the same order and end at the same place.
        ///
        /// <para>Costs are zeroed (no commission, no slippage) so the comparison is about the
        /// exit DECISIONS and not about the backtester's ledger, which the live path does not
        /// keep. Entry is aligned by hand: the replay fills at the next bar's open, so the live
        /// position is opened at that price and walked from that bar forward.</para>
        /// </summary>
        [Fact]
        public async Task Live_exits_match_the_replayed_exits_bar_for_bar()
        {
            var signal = LadderSignal(stop: 98, rungs: new[] { 102.0, 104.0, 106.0 },
                portions: new[] { 1.0 / 3, 1.0 / 3, 1.0 / 3 });

            var bars = new List<Ohlcv>
            {
                Bar(0, 100, 100.5, 99.5, 100),      // signal decided here
                Bar(1, 100, 100.5, 99.5, 100),      // replay fills at this open: 100
                Bar(2, 100, 102.5, 99.8, 102),      // rung 1 (102); stop → breakeven 100
                Bar(3, 102, 104.5, 101.0, 104),     // rung 2 (104)
                Bar(4, 104, 104.5,  99.0,  99.5),   // breakeven stop (100) takes the remainder
                Bar(5,  99,  99.5,  98.5,  99),     // trailing bar: the replay loops to Count-2
            };

            // ── The replay ──
            var backtester = new StrategyBacktester();
            var result = await backtester.RunAsync(
                new OneShotStrategy(signal), bars,
                new BacktestConfig(WarmupBars: 0, ReplayProfiles: false,
                                   CommissionRate: 0, SlippagePercent: 0),
                WorkspaceState.Initial);
            var replayed = result.Trades.ToList();

            // ── The live walk ──
            var h = new Harness(_dir);
            h.Manager.OpenPosition(Active(), signal, quantity: 3.0, provider: "Kraken",
                symbol: "BTC/USD", referencePrice: bars[1].Open, entryOrderId: "entry-1");

            var live = new List<StrategyExitOrder>();
            for (int i = 1; i < bars.Count - 1; i++)
            {
                var exits = h.Manager.OnBarClosed("inst-1", bars[i], bars.Take(i + 1).ToList());
                live.AddRange(exits);
                Assert.True(await h.Manager.PlaceExitsAsync(exits));
            }

            Assert.Equal(3, replayed.Count);
            Assert.Equal(replayed.Count, live.Count);
            for (int i = 0; i < replayed.Count; i++)
            {
                Assert.Equal(replayed[i].Quantity, live[i].Quantity, 6);
            }

            // ...and the shape of the sequence, not just the sizes: two targets then a stop.
            Assert.Contains("target 1", live[0].Reason);
            Assert.Contains("target 2", live[1].Reason);
            Assert.Contains("stop", live[2].Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("TP rung", replayed[0].ExitReason);
            Assert.Contains("TP rung", replayed[1].ExitReason);
            Assert.Contains("Breakeven stop", replayed[2].ExitReason);

            // Both are flat at the end.
            Assert.Null(h.Manager.Get("inst-1"));
        }

        // ── Position awareness ───────────────────────────────────────────────────

        [Fact]
        public void A_counter_signal_closes_before_it_opens()
        {
            var h = new Harness(_dir);
            var active = Active();
            h.Manager.OpenPosition(active, LadderSignal(side: OrderSide.Buy), quantity: 3.0,
                provider: "Kraken", symbol: "BTC/USD", referencePrice: 100, entryOrderId: "entry-1");

            var plan = h.Manager.PlanEntry(active, LadderSignal(side: OrderSide.Sell), quantity: 2.0,
                provider: "Kraken", symbol: "BTC/USD");

            Assert.Equal(StrategyEntryDisposition.Reverse, plan.Disposition);
            Assert.NotNull(plan.CloseFirst);
            Assert.Equal(OrderSide.Sell, plan.CloseFirst!.Side);   // closes the LONG
            Assert.Equal(3.0, plan.CloseFirst.Quantity, 6);        // the whole remainder
        }

        [Fact]
        public void A_repeat_signal_on_the_same_side_adds_nothing_and_rearms_the_plan()
        {
            var h = new Harness(_dir);
            var active = Active();
            h.Manager.OpenPosition(active, LadderSignal(stop: 98), quantity: 3.0,
                provider: "Kraken", symbol: "BTC/USD", referencePrice: 100, entryOrderId: "entry-1");

            var plan = h.Manager.PlanEntry(active,
                LadderSignal(stop: 99, rungs: new[] { 110.0 }, portions: new[] { 1.0 }),
                quantity: 3.0, provider: "Kraken", symbol: "BTC/USD");

            Assert.Equal(StrategyEntryDisposition.AlreadyOpen, plan.Disposition);
            Assert.Null(plan.CloseFirst);
            Assert.Empty(h.Orders.Placed);                                   // pyramiding is not a thing

            var open = h.Manager.Get("inst-1")!;
            Assert.Equal(99, open.StopPrice);                                // re-armed at the new levels
            Assert.Equal(new[] { 110.0 }, open.TargetPrices);
            Assert.Equal(3.0, open.RemainingQuantity, 6);                    // size unchanged
        }

        [Fact]
        public async Task A_refused_exit_leaves_the_position_open_and_the_level_armed()
        {
            var h = new Harness(_dir);
            h.Orders.Answer = _ => "ORDER_FAILED:insufficient paper balance";
            h.Manager.OpenPosition(Active(), LadderSignal(stop: 98), quantity: 3.0,
                provider: "Kraken", symbol: "BTC/USD", referencePrice: 100, entryOrderId: "entry-1");

            var bars = new List<Ohlcv> { Bar(0, 100, 100, 97, 97.5) };   // the stop is reached
            var exits = h.Manager.OnBarClosed("inst-1", bars[0], bars);
            Assert.False(await h.Manager.PlaceExitsAsync(exits));

            // Believing you are flat while the broker still holds the position is the one
            // outcome worse than a retry, so the record comes back whole.
            var open = h.Manager.Get("inst-1");
            Assert.NotNull(open);
            Assert.Equal(3.0, open!.RemainingQuantity, 6);
            Assert.Equal(98, open.StopPrice);
            Assert.Contains(h.Spoken, m => m.Contains("still open", StringComparison.OrdinalIgnoreCase));

            // And the next bar still beyond the level tries again.
            h.Orders.Answer = null;
            var retry = h.Manager.OnBarClosed("inst-1", Bar(1, 97.5, 98, 96, 96.5), bars);
            Assert.Single(retry);
            Assert.True(await h.Manager.PlaceExitsAsync(retry));
            Assert.Null(h.Manager.Get("inst-1"));
        }

        // ── Restart ──────────────────────────────────────────────────────────────

        [Fact]
        public void A_position_survives_the_process_and_is_readopted_by_spec()
        {
            var first = new Harness(_dir);
            first.Manager.OpenPosition(Active("inst-1", "spec-1"), LadderSignal(stop: 98), quantity: 3.0,
                provider: "Kraken", symbol: "BTC/USD", referencePrice: 100, entryOrderId: "entry-1");

            // A new process: new manager over the same app-data directory, new instance id.
            var second = new Harness(_dir);
            Assert.Null(second.Manager.Get("inst-2"));           // not adopted until the spec registers
            second.Manager.Adopt("inst-2", "spec-1");

            var restored = second.Manager.Get("inst-2");
            Assert.NotNull(restored);
            Assert.Equal(OrderSide.Buy, restored!.Side);
            Assert.Equal(3.0, restored.RemainingQuantity, 6);
            Assert.Equal(98, restored.StopPrice);
            Assert.Equal(new[] { 102.0, 104.0, 106.0 }, restored.TargetPrices);
            Assert.False(restored.Verified);                     // not until the broker says so
        }

        [Fact]
        public void A_readopted_position_stops_the_restart_from_opening_a_second_one()
        {
            var first = new Harness(_dir);
            first.Manager.OpenPosition(Active("inst-1", "spec-1"), LadderSignal(), quantity: 3.0,
                provider: "Kraken", symbol: "BTC/USD", referencePrice: 100, entryOrderId: "entry-1");

            var second = new Harness(_dir);
            var reborn = Active("inst-2", "spec-1");
            second.Manager.Adopt("inst-2", "spec-1");

            // The same conditions are still true on the next bar. Before this, the strategy came
            // back flat and this signal opened a SECOND long on top of the first.
            var plan = second.Manager.PlanEntry(reborn, LadderSignal(), quantity: 3.0,
                provider: "Kraken", symbol: "BTC/USD");

            Assert.Equal(StrategyEntryDisposition.AlreadyOpen, plan.Disposition);
            Assert.Empty(second.Orders.Placed);
        }

        [Fact]
        public async Task Reconciliation_drops_a_position_the_broker_no_longer_holds()
        {
            var first = new Harness(_dir);
            first.Manager.OpenPosition(Active("inst-1", "spec-1"), LadderSignal(), quantity: 3.0,
                provider: "Kraken", symbol: "BTC/USD", referencePrice: 100, entryOrderId: "entry-1");

            var second = new Harness(_dir);
            second.Manager.Adopt("inst-2", "spec-1");
            second.Orders.Positions = ProviderResult<List<Position>>.Ok(new List<Position>());

            await second.Manager.ReconcileAsync();

            Assert.Null(second.Manager.Get("inst-2"));
            Assert.Contains(second.Spoken, m => m.Contains("no longer open", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task Reconciliation_keeps_a_position_the_venue_cannot_speak_about()
        {
            var first = new Harness(_dir);
            first.Manager.OpenPosition(Active("inst-1", "spec-1"), LadderSignal(), quantity: 3.0,
                provider: "Bitstamp", symbol: "BTC/USD", referencePrice: 100, entryOrderId: "entry-1");

            var second = new Harness(_dir);
            second.Manager.Adopt("inst-2", "spec-1");
            // A spot venue has no positions concept. That is not evidence the position is gone,
            // and dropping it would leave the only stop it has unmanaged.
            second.Orders.Positions = ProviderResult<List<Position>>.NotSupported("spot venue");

            await second.Manager.ReconcileAsync();

            var kept = second.Manager.Get("inst-2");
            Assert.NotNull(kept);
            Assert.False(kept!.Verified);
            Assert.Contains(second.Spoken, m => m.Contains("could not confirm", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task Reconciliation_hands_back_a_position_whose_side_disagrees()
        {
            var first = new Harness(_dir);
            first.Manager.OpenPosition(Active("inst-1", "spec-1"), LadderSignal(side: OrderSide.Buy), quantity: 3.0,
                provider: "Kraken", symbol: "BTC/USD", referencePrice: 100, entryOrderId: "entry-1");

            var second = new Harness(_dir);
            second.Manager.Adopt("inst-2", "spec-1");
            // The broker holds a SHORT. We cannot explain that, so we will not fire reduce-only
            // orders at it — the user is told and takes it over.
            second.Orders.Positions = ProviderResult<List<Position>>.Ok(new List<Position>
            {
                new("BTCUSD", -3.0, 100, 300, 0),
            });

            await second.Manager.ReconcileAsync();

            Assert.Null(second.Manager.Get("inst-2"));
            Assert.Contains(second.Spoken, m => m.Contains("holds a short", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task Reconciliation_takes_the_brokers_size_as_the_truth()
        {
            var first = new Harness(_dir);
            first.Manager.OpenPosition(Active("inst-1", "spec-1"), LadderSignal(), quantity: 3.0,
                provider: "Kraken", symbol: "BTC/USD", referencePrice: 100, entryOrderId: "entry-1");

            var second = new Harness(_dir);
            second.Manager.Adopt("inst-2", "spec-1");
            // A rung went out as the process died: the broker holds two, we remember three.
            // Separator and case differ too — "BTC-USD" against "BTC/USD" — which is the
            // ordinary case across venues, not an exotic one.
            second.Orders.Positions = ProviderResult<List<Position>>.Ok(new List<Position>
            {
                new("btc-usd", 2.0, 100, 200, 0),
            });

            await second.Manager.ReconcileAsync();

            var confirmed = second.Manager.Get("inst-2");
            Assert.NotNull(confirmed);
            Assert.True(confirmed!.Verified);
            Assert.Equal(2.0, confirmed.RemainingQuantity, 6);
        }

        [Fact]
        public async Task An_unadopted_position_is_announced_as_unmanaged_not_as_resumed()
        {
            var first = new Harness(_dir);
            first.Manager.OpenPosition(Active("inst-1", "spec-1"), LadderSignal(), quantity: 3.0,
                provider: "Kraken", symbol: "BTC/USD", referencePrice: 100, entryOrderId: "entry-1");

            // The spec is never re-registered — deleted from the library, or its auto-activate
            // flag turned off while the position was open. Nothing calls Adopt, so no engine
            // instance drives the bar walk and the stop is not running.
            var second = new Harness(_dir);
            second.Orders.Positions = ProviderResult<List<Position>>.Ok(new List<Position>
            {
                new("BTC/USD", 3.0, 100, 300, 0),
            });

            await second.Manager.ReconcileAsync();

            Assert.Contains(second.Spoken, m =>
                m.Contains("NOT being managed", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(second.Spoken, m =>
                m.Contains("resumed managing", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Symbol_matching_ignores_separators_and_case_but_not_identity()
        {
            Assert.True(StrategyPositionManager.SymbolsMatch("BTC/USD", "btc-usd"));
            Assert.True(StrategyPositionManager.SymbolsMatch("BTCUSD", "BTC_USD"));
            Assert.False(StrategyPositionManager.SymbolsMatch("BTC/USD", "BTC/USDT"));
            Assert.False(StrategyPositionManager.SymbolsMatch("BTC/USD", ""));
            Assert.False(StrategyPositionManager.SymbolsMatch(null, "BTC/USD"));
        }

        // ── The fill anchor ──────────────────────────────────────────────────────

        [Fact]
        public void The_breakeven_anchor_moves_to_the_real_fill_price()
        {
            var h = new Harness(_dir);
            h.Manager.OpenPosition(Active(), LadderSignal(), quantity: 3.0, provider: "Kraken",
                symbol: "BTC/USD", referencePrice: 100, entryOrderId: "entry-1");

            // The market moved between the bar close and the fill; the venue says 100.8.
            h.Bus.Publish(new OrderFilledEvent(new OrderUpdate(
                "entry-1", "BTC/USD", OrderSide.Buy, 3.0, 100.8, 0,
                OrderStatus.Filled, false, false, DateTime.UtcNow)));

            Assert.Equal(100.8, h.Manager.Get("inst-1")!.EntryPrice, 6);

            // An unrelated fill must not move it.
            h.Bus.Publish(new OrderFilledEvent(new OrderUpdate(
                "someone-elses-order", "BTC/USD", OrderSide.Buy, 1.0, 50, 0,
                OrderStatus.Filled, false, false, DateTime.UtcNow)));
            Assert.Equal(100.8, h.Manager.Get("inst-1")!.EntryPrice, 6);
        }

        // ── The engine actually wires it in ──────────────────────────────────────

        /// <summary>
        /// Everything above tests the manager. This tests the SEAM, which is where the defect
        /// actually lived: <c>StrategyEngine.ExecuteSignalAsync</c> built its six-field
        /// TradeSignal and never handed the rest of the plan to anybody. A manager that works
        /// perfectly and is never called is the bug wearing a fix.
        /// </summary>
        [Fact]
        public async Task The_engine_hands_the_whole_plan_to_the_manager_and_runs_it_on_the_next_bar()
        {
            var h = new Harness(_dir);
            var dataManager = Substitute.For<IDataManager>();
            var store = Substitute.For<IWorkspaceStore>();

            var bars = new List<Ohlcv>
            {
                Bar(0, 100, 100.5, 99.5, 100),
                Bar(1, 100, 100.5, 99.5, 100),
            };
            store.State.Returns(_ => WorkspaceState.Initial with
            {
                Identity = new ChartIdentity("Spot", "Kraken", "BTC/USD", "1h"),
                Data = new TimeSeriesBuffer<Ohlcv>(bars),
                CurrentDataIndex = bars.Count - 1,
            });

            var strategy = Substitute.For<ITradingStrategy>();
            strategy.Name.Returns("Ladder strategy");
            var signal = LadderSignal();
            bool emitted = false;
            strategy.OnBar(Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyList<Ohlcv>>(), Arg.Any<WorkspaceState>())
                    .Returns(_ => { if (emitted) return null; emitted = true; return signal; });

            using var engine = new StrategyEngine(h.Bus, h.Orders, Substitute.For<IAppLogger>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<StrategyEngine>.Instance,
                dataManager, store, Substitute.For<IStrategyIndicatorCache>(),
                feedHub: null, positions: h.Manager);

            string id = engine.AddStrategy(strategy, null, StrategyExecutionMode.Auto, specId: "spec-1");
            dataManager.DataUpdated += Raise.Event<Action>();

            // Both halves, not just the placement. ExecuteSignalAsync records the order with the
            // broker BEFORE it registers the plan with the manager, so waiting only on
            // Orders.Placed lets the assertion below run in the gap between the two — which it
            // did about one run in three. A timeout here still fails, so a manager that is never
            // populated is still caught; only the moment of looking has moved.
            await WaitUntil(() => h.Orders.Placed.Count > 0 && h.Manager.Get(id) != null);

            // The entry went, and the manager now holds the ladder the ORDER could not carry.
            Assert.Equal(OrderSide.Buy, h.Orders.Placed[0].Signal.Side);
            var managed = h.Manager.Get(id);
            Assert.NotNull(managed);
            Assert.Equal(new[] { 102.0, 104.0, 106.0 }, managed!.TargetPrices);
            Assert.Equal(StopAdjustOnTp1.MoveToBreakeven, managed.StopAdjust);

            // A later bar reaching rung one produces a reduce-only sell from the engine's own
            // bar-close path — not from a test poking the manager.
            bars.Add(Bar(2, 100, 102.5, 99.8, 102));
            dataManager.DataUpdated += Raise.Event<Action>();

            await WaitUntil(() => h.Orders.Placed.Any(p => p.Signal.Side == OrderSide.Sell));
            var exit = h.Orders.Placed.First(p => p.Signal.Side == OrderSide.Sell);
            Assert.True(exit.Signal.ReduceOnly);
            Assert.Equal(1.0, exit.Signal.Quantity, 6);
        }

        /// <summary>
        /// The ENGINE re-adopts, not just the manager.
        ///
        /// <para>
        /// <see cref="A_readopted_position_stops_the_restart_from_opening_a_second_one"/> calls
        /// <c>Adopt</c> by hand, so it stays green even if <c>AddStrategy</c> never calls it — a
        /// sabotage run proved exactly that. This drives the real seam: a library spec is
        /// registered on a fresh engine and must come back holding its position, before any bar
        /// can be evaluated.
        /// </para>
        /// </summary>
        [Fact]
        public async Task Registering_a_spec_on_a_fresh_engine_restores_its_position()
        {
            var first = new Harness(_dir);
            first.Manager.OpenPosition(Active("inst-1", "spec-1"), LadderSignal(), quantity: 3.0,
                provider: "Kraken", symbol: "BTC/USD", referencePrice: 100, entryOrderId: "entry-1");

            // A new process.
            var h = new Harness(_dir);
            var dataManager = Substitute.For<IDataManager>();
            var store = Substitute.For<IWorkspaceStore>();
            var bars = new List<Ohlcv> { Bar(0, 100, 100.5, 99.5, 100), Bar(1, 100, 100.5, 99.5, 100) };
            store.State.Returns(_ => WorkspaceState.Initial with
            {
                Identity = new ChartIdentity("Spot", "Kraken", "BTC/USD", "1h"),
                Data = new TimeSeriesBuffer<Ohlcv>(bars),
                CurrentDataIndex = bars.Count - 1,
            });

            var strategy = Substitute.For<ITradingStrategy>();
            strategy.Name.Returns("Ladder strategy");
            strategy.OnBar(Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyList<Ohlcv>>(), Arg.Any<WorkspaceState>())
                    .Returns(LadderSignal());   // the same conditions are still true

            using var engine = new StrategyEngine(h.Bus, h.Orders, Substitute.For<IAppLogger>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<StrategyEngine>.Instance,
                dataManager, store, Substitute.For<IStrategyIndicatorCache>(),
                feedHub: null, positions: h.Manager);

            string id = engine.AddStrategy(strategy, null, StrategyExecutionMode.Auto, specId: "spec-1");

            var restored = h.Manager.Get(id);
            Assert.NotNull(restored);
            Assert.Equal(3.0, restored!.RemainingQuantity, 6);

            // ...and the still-true conditions must not open a second long on top of it.
            dataManager.DataUpdated += Raise.Event<Action>();
            await Task.Delay(200);
            Assert.Empty(h.Orders.Placed);
        }

        // The deadline bounds FAILURE, not success: the loop returns the moment the condition
        // holds, so a generous timeout costs nothing when things work and only decides how long a
        // genuine hang takes to report. Five seconds of wall clock was tight enough that this test
        // failed once in a full parallel run and passed alone — the suite had simply got busier.
        // A test that fails because the machine was loaded teaches nobody anything.
        private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 30_000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return;
                await Task.Delay(10);
            }
            Assert.True(condition(), "condition never became true within the timeout");
        }

        /// <summary>
        /// Startup asks the broker what it actually holds.
        ///
        /// <para>
        /// Adoption alone restores what we REMEMBER; it cannot notice that a stop filled while
        /// the app was down, or that the user closed the position by hand. Only the venue can
        /// say. A sabotage run that deleted the reconcile call from the auto-loader left every
        /// other test green, which is why this one exists at the loader rather than at the
        /// manager.
        /// </para>
        /// </summary>
        [Fact]
        public async Task Startup_reconciles_remembered_positions_against_the_broker()
        {
            var positions = new CountingPositions();

            var library = Substitute.For<IStrategyLibrary>();
            library.All.Returns(new List<StrategySpec>());

            var loader = new StrategyAutoLoader(
                library,
                Substitute.For<IConfigurableStrategyFactory>(),
                Substitute.For<IStrategyEngine>(),
                Substitute.For<IAppLogger>(),
                roslyn: null,
                positions: positions);

            await loader.LoadAllAsync();

            // Reconciliation is NOT conditional on there being auto-activate specs: a position
            // can outlive the strategy that opened it, and that is precisely the case where
            // nobody would otherwise be told.
            Assert.Equal(1, positions.Reconciles);
        }

        // ── The contract that keeps it wired ─────────────────────────────────────

        /// <summary>
        /// **Every field of <see cref="StrategySignal"/> is consumed by the live path.**
        ///
        /// <para>
        /// This is the guard the original defect needed and did not have. Five fields —
        /// <c>TpLadder</c>, <c>TpClosePortions</c>, <c>StopAdjust</c>, <c>TrailAtrPeriod</c>,
        /// <c>TrailAtrMultiple</c> — existed on the signal, were populated by
        /// <c>ConfigurableStrategy</c>, were honoured by the backtester, and were silently
        /// dropped by the engine. Nothing failed, because nothing was checking that the live
        /// path had an opinion about each one.
        /// </para>
        ///
        /// <para>
        /// So: every property name must appear in the live execution path's sources. The floor
        /// is on the POPULATION (the property count), never on the violations — a floor on the
        /// number of unconsumed fields shrinks every time someone does the right thing and goes
        /// red for it. A new field added to StrategySignal fails this until somebody decides
        /// where live reads it, which is exactly the decision that was skipped last time.
        /// </para>
        ///
        /// <para>
        /// **What this cannot do.** It is a presence check, not a path check: a field that is
        /// stored and persisted but no longer read at the one place it matters still passes.
        /// A sabotage run proved that — hardcoding 1.5 in place of <c>p.TrailAtrMultiple</c>
        /// left this green. The behavioural pin for that lives in
        /// <see cref="The_trail_distance_scales_with_the_signals_atr_multiple"/>; this guard's
        /// job is the coarser and more common failure, which is a field nothing mentions at all.
        /// </para>
        /// </summary>
        [Fact]
        public void No_field_of_a_strategy_signal_is_dropped_by_the_live_path()
        {
            var properties = typeof(StrategySignal).GetProperties()
                .Where(p => p.Name != "EqualityContract")
                .Select(p => p.Name)
                .ToList();

            // Vacuity floor: the record had 13 members when this was written. If reflection
            // stops seeing them, the test is proving nothing — fix the discovery, not the floor.
            Assert.True(properties.Count >= 13,
                $"only {properties.Count} StrategySignal properties found; the scan is not seeing the record.");

            string live = string.Concat(LivePathSources().Select(File.ReadAllText));

            // Fields that are deliberately NOT execution instructions. Each needs a reason;
            // "the live path happens not to read it" is not one — that is the defect. The
            // assertion below fails if one of these STARTS being read, so an exemption cannot
            // quietly outlive its reason.
            var notAnInstruction = new Dictionary<string, string>
            {
                ["Confidence"] =
                    "A 0–1 score describing how strongly the condition tree fired. It reaches the "
                    + "user through the setup rationale and the journal; it does not size, price "
                    + "or gate the order, and a live path that acted on it would be sizing off a "
                    + "number no backtest ever sized off.",
            };

            foreach (var (name, why) in notAnInstruction)
            {
                Assert.True(properties.Contains(name),
                    $"'{name}' is exempted here but is no longer a StrategySignal field — drop the exemption.");
                Assert.False(live.Contains(name, StringComparison.Ordinal),
                    $"'{name}' is exempted as not-an-instruction ({why}) but the live path now reads it. "
                    + "Either the exemption is stale or the read is wrong — decide, do not leave both.");
            }

            var dropped = properties.Where(name => !live.Contains(name, StringComparison.Ordinal))
                                    .Where(name => !notAnInstruction.ContainsKey(name))
                                    .OrderBy(n => n, StringComparer.Ordinal)
                                    .ToList();

            Assert.True(dropped.Count == 0,
                "These StrategySignal fields are read by the backtester but never by the live path, "
                + "which is exactly how the ladder, the breakeven move and the ATR trail came to be "
                + "simulated and never traded:\n  " + string.Join("\n  ", dropped));
        }

        /// <summary>
        /// The rules are shared, not copied. Both the replay and the live manager must go
        /// through <see cref="ManagedExitRules"/>, and neither may keep a private copy of the
        /// stop/target comparison — a second copy is how the two drift apart again, silently,
        /// with the backtest still saying the strategy works.
        /// </summary>
        [Fact]
        public void The_replay_and_the_live_walk_share_one_set_of_exit_rules()
        {
            string backtester = File.ReadAllText(SourceFile("StrategyBacktester.cs"));
            string manager    = File.ReadAllText(SourceFile("StrategyPositionManager.cs"));

            foreach (var (name, body) in new[] { ("StrategyBacktester", backtester), ("StrategyPositionManager", manager) })
            {
                Assert.True(body.Contains("ManagedExitRules.StopHit", StringComparison.Ordinal),
                    $"{name} does not test the stop through ManagedExitRules.");
                Assert.True(body.Contains("ManagedExitRules.TargetHit", StringComparison.Ordinal),
                    $"{name} does not test targets through ManagedExitRules.");
                Assert.True(body.Contains("ManagedExitRules.BuildLadder", StringComparison.Ordinal),
                    $"{name} does not build its ladder through ManagedExitRules.");
                Assert.True(body.Contains("ManagedExitRules.StopAfterFirstTarget", StringComparison.Ordinal),
                    $"{name} does not adjust the stop through ManagedExitRules.");
                Assert.True(body.Contains("ManagedExitRules.AtrTrailStop", StringComparison.Ordinal),
                    $"{name} does not trail through ManagedExitRules.");
            }
        }

        /// <summary>The files that make up the live auto-execution path.</summary>
        private static IEnumerable<string> LivePathSources() => new[]
        {
            SourceFile("StrategyEngine.cs"),
            SourceFile("StrategyPositionManager.cs"),
            SourceFile("IStrategyPositionManager.cs"),
            SourceFile("ManagedExitRules.cs"),
        };

        private static string SourceFile(string fileName)
        {
            var root = StrategyLibraryPolicyTests.ShippingProjectDirectories()
                .Select(Path.GetDirectoryName)
                .Where(d => d != null)
                .Distinct()
                .ToList();

            foreach (var dir in StrategyLibraryPolicyTests.ShippingProjectDirectories())
            {
                var hit = Directory.EnumerateFiles(dir, fileName, SearchOption.AllDirectories)
                    .FirstOrDefault(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                      && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
                if (hit != null) return hit;
            }

            throw new FileNotFoundException($"{fileName} was not found under any shipping project; "
                + "the scan cannot be trusted until discovery is fixed.");
        }

        [Fact]
        public void Removing_the_strategy_says_the_position_is_no_longer_managed()
        {
            var h = new Harness(_dir);
            h.Manager.OpenPosition(Active(), LadderSignal(), quantity: 3.0, provider: "Kraken",
                symbol: "BTC/USD", referencePrice: 100, entryOrderId: "entry-1");

            h.Manager.Forget("inst-1");

            Assert.Null(h.Manager.Get("inst-1"));
            Assert.Contains(h.Spoken, m =>
                m.Contains("no longer being managed", StringComparison.OrdinalIgnoreCase));
        }
    }
}
