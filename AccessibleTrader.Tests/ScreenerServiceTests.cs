using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Screening;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Screening;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Coverage for <see cref="ScreenerService"/>.
    ///
    /// The behaviours pinned here are the ones that decide whether a screen can be trusted:
    /// a symbol that failed to fetch must be REPORTED, not dropped (otherwise "0 matched" is
    /// indistinguishable from "40 never loaded"); the indicator set must be derived from the
    /// condition tree so the screen doesn't compute the whole registry per symbol; and every
    /// entry must produce exactly one row, in watchlist order.
    /// </summary>
    public class ScreenerServiceTests
    {
        // ── Fakes ─────────────────────────────────────────────────────────────

        private sealed class FakeDataService : IDataService
        {
            private readonly Func<string, List<Ohlcv>> _bars;
            public int FetchCount;
            public int MaxConcurrent;
            private int _current;

            public FakeDataService(Func<string, List<Ohlcv>> bars) => _bars = bars;

            public async Task<(List<Ohlcv>, List<(long, double)>)> FetchOhlcvAsync(string p, MarketDataRequest r)
            {
                Interlocked.Increment(ref FetchCount);
                int now = Interlocked.Increment(ref _current);
                // Track the high-water mark of in-flight fetches so the rate-limit guard is testable.
                int observed;
                do { observed = MaxConcurrent; }
                while (now > observed && Interlocked.CompareExchange(ref MaxConcurrent, now, observed) != observed);

                try
                {
                    await Task.Yield();
                    return (_bars(r.Symbol), new List<(long, double)>());
                }
                finally { Interlocked.Decrement(ref _current); }
            }

            public void RegisterProvider(IMarketDataProvider provider) { }
            public Task InitializeAsync(IPluginLoaderService _) => Task.CompletedTask;
            public Task ConfigureStoredKeyProvidersAsync() => Task.CompletedTask;
            public Task<List<string>> LoadAvailableMarketsAsync() => Task.FromResult(new List<string>());
            public Task<List<string>> LoadProvidersAsync() => Task.FromResult(new List<string>());
            public Task<List<string>> LoadProvidersByMarketTypeAsync(string _) => Task.FromResult(new List<string>());
            public Task<List<string>> GetSupportedSubTypesAsync(string a, string b) => Task.FromResult(new List<string>());
            public Task<List<string>> LoadSymbolsAsync(string a, string b) => Task.FromResult(new List<string>());
            public Task<List<string>> GetSupportedTimeframesAsync(string _) => Task.FromResult(new List<string>());
            public Task<bool> IsProviderConfiguredAsync(string _) => Task.FromResult(true);
            public bool IsProviderConfigured(string _) => true;
            public Task<bool> ProviderRequiresApiKeyAsync(string _) => Task.FromResult(false);
            public Task<(List<OrderBookEntry>, List<OrderBookEntry>)> GetOrderBookAsync(string p, string s, int l = 10)
                => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));
            public Task<List<MarketType>> GetSupportedMarketsForProviderAsync(string _) => Task.FromResult(new List<MarketType>());
            public Task<IMarketDataProvider?> GetProviderAsync(string _) => Task.FromResult<IMarketDataProvider?>(null);
            public Task<IProviderPlugin?> GetPluginAsync(string _) => Task.FromResult<IProviderPlugin?>(null);
        }

        /// <summary>Records which indicator codes the screener asked for.</summary>
        private sealed class RecordingBuilder : IOfflineWorkspaceBuilder
        {
            public readonly List<string> RequestedCodes = new();

            public Task<WorkspaceState> BuildAsync(
                ChartIdentity identity, IReadOnlyList<Ohlcv> bars, IEnumerable<string> indicatorCodes,
                IDictionary<string, string>? failures = null, CancellationToken ct = default)
            {
                lock (RequestedCodes) RequestedCodes.AddRange(indicatorCodes);
                return Task.FromResult(WorkspaceState.Initial with { Identity = identity });
            }
        }

        /// <summary>Returns a fixed verdict, optionally keyed off the last close.</summary>
        private sealed class FakeEvaluator : IConditionEvaluator
        {
            private readonly Func<IReadOnlyList<Ohlcv>, bool> _predicate;
            public FakeEvaluator(Func<IReadOnlyList<Ohlcv>, bool> predicate) => _predicate = predicate;

            /// <summary>Always answers cleanly — this fake decides by predicate, so there is
            /// never a leaf it could not evaluate.</summary>
            public string? LastDegradation => null;

            public ConditionEvaluation Evaluate(ConditionNode root, IReadOnlyList<Ohlcv> history, WorkspaceState state)
            {
                bool ok = _predicate(history);
                return new ConditionEvaluation(ok, new Dictionary<string, bool>(), ok ? 1 : 0, 1);
            }
        }

        private sealed class FakeCatalog : ISignalCatalog
        {
            private readonly List<SignalDescriptor> _all;
            public FakeCatalog(params SignalDescriptor[] all) => _all = all.ToList();
            public IReadOnlyList<SignalDescriptor> All => _all;
            public SignalDescriptor? GetById(string id) => _all.FirstOrDefault(d => d.Id == id);
            public IReadOnlyList<SignalDescriptor> GetForIndicator(string code) =>
                _all.Where(d => d.IndicatorCode == code).ToList();
            public void Refresh() { }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static List<Ohlcv> Bars(int count, double lastClose = 100)
        {
            var bars = new List<Ohlcv>(count);
            var start = new DateTime(2026, 1, 1);
            for (int i = 0; i < count; i++)
            {
                double close = i == count - 1 ? lastClose : 100;
                bars.Add(new Ohlcv(start.AddDays(i), 100, 101, 99, close, 10));
            }
            return bars;
        }

        private static List<WatchlistEntry> Entries(params string[] symbols) =>
            symbols.Select(s => new WatchlistEntry("Binance", s, MarketType.Crypto)).ToList();

        private static ScreenerService Build(
            IDataService data,
            IOfflineWorkspaceBuilder? builder = null,
            IConditionEvaluator? evaluator = null,
            ISignalCatalog? catalog = null) =>
            new(data,
                builder ?? new RecordingBuilder(),
                evaluator ?? new FakeEvaluator(_ => true),
                catalog ?? new FakeCatalog());

        // ── Tests ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task EveryEntry_ProducesExactlyOneRow_InWatchlistOrder()
        {
            var data = new FakeDataService(_ => Bars(50));
            var service = Build(data);
            var entries = Entries("AAA", "BBB", "CCC");

            var result = await service.RunAsync(
                ScreenerSpec.Create("s", null), entries);

            Assert.Equal(3, result.Rows.Count);
            Assert.Equal(new[] { "AAA", "BBB", "CCC" }, result.Rows.Select(r => r.Entry.Symbol));
        }

        [Fact]
        public async Task FetchFailure_IsReportedAsAFailedRow_NotDropped()
        {
            var data = new FakeDataService(symbol =>
                symbol == "BAD" ? throw new InvalidOperationException("boom") : Bars(50));
            var service = Build(data);

            var result = await service.RunAsync(
                ScreenerSpec.Create("s", null), Entries("GOOD", "BAD"));

            Assert.Equal(2, result.Rows.Count);
            var bad = result.Rows.Single(r => r.Entry.Symbol == "BAD");
            Assert.Equal(ScreenerRowStatus.Failed, bad.Status);
            Assert.False(bad.Matched);
            Assert.Contains("boom", bad.Detail);

            // The counts must let a caller say "1 evaluated, 1 could not be evaluated".
            Assert.Equal(1, result.EvaluatedCount);
            Assert.Equal(1, result.FailedCount);
            Assert.Equal(1, result.MatchCount);
        }

        [Fact]
        public async Task TooFewBars_IsInsufficientHistory_NotAMatch()
        {
            var data = new FakeDataService(_ => Bars(1));
            var service = Build(data);

            var result = await service.RunAsync(ScreenerSpec.Create("s", null), Entries("THIN"));

            var row = Assert.Single(result.Rows);
            Assert.Equal(ScreenerRowStatus.InsufficientHistory, row.Status);
            Assert.False(row.Matched);
            Assert.Equal(0, result.MatchCount);
        }

        [Fact]
        public async Task NullRoot_MatchesEverything_SoAScreenDoublesAsAQuoteList()
        {
            var data = new FakeDataService(_ => Bars(10));
            // Deliberately give an evaluator that would say "false" — with a null Root it must
            // never be consulted at all.
            var service = Build(data, evaluator: new FakeEvaluator(_ => false));

            var result = await service.RunAsync(ScreenerSpec.Create("s", null), Entries("AAA", "BBB"));

            Assert.Equal(2, result.MatchCount);
            Assert.All(result.Rows, r => Assert.True(r.Matched));
        }

        [Fact]
        public async Task LastCloseAndPercentChange_ComeFromTheFinalTwoBars()
        {
            var bars = new List<Ohlcv>
            {
                new(new DateTime(2026, 1, 1), 100, 100, 100, 200, 1),
                new(new DateTime(2026, 1, 2), 100, 100, 100, 220, 1),
            };
            var data = new FakeDataService(_ => bars);
            var service = Build(data);

            var result = await service.RunAsync(ScreenerSpec.Create("s", null), Entries("AAA"));

            var row = Assert.Single(result.Rows);
            Assert.Equal(220, row.LastClose);
            Assert.Equal(10.0, row.PercentChange, 6);
            Assert.Equal(new DateTime(2026, 1, 2), row.LastBarTime);
        }

        [Fact]
        public async Task Progress_ReportsOncePerSymbol()
        {
            var data = new FakeDataService(_ => Bars(10));
            var service = Build(data);
            var seen = new List<int>();
            var progress = new Progress<ScreenerProgress>(p => { lock (seen) seen.Add(p.Completed); });

            await service.RunAsync(ScreenerSpec.Create("s", null), Entries("A", "B", "C"), progress);

            // Progress is posted through the synchronization context, so give it a beat to drain.
            for (int i = 0; i < 50 && seen.Count < 3; i++) await Task.Delay(10);
            lock (seen) Assert.Equal(3, seen.Count);
        }

        [Fact]
        public async Task Concurrency_IsCappedSoScreensDoNotTripRateLimits()
        {
            var data = new FakeDataService(_ => Bars(10));
            var service = Build(data);

            await service.RunAsync(
                ScreenerSpec.Create("s", null),
                Entries(Enumerable.Range(0, 40).Select(i => $"S{i}").ToArray()));

            Assert.InRange(data.MaxConcurrent, 1, ScreenerService.MaxConcurrency);
        }

        // ── Indicator-code resolution ─────────────────────────────────────────

        [Fact]
        public void ResolveIndicatorCodes_AlwaysIncludesTheCoreProjections()
        {
            var service = Build(new FakeDataService(_ => Bars(10)));
            var codes = service.ResolveIndicatorCodes(ScreenerSpec.Create("s", null));

            Assert.Contains("CANDLES", codes);
            Assert.Contains("PRICE", codes);
            Assert.Contains("VOLUME", codes);
        }

        [Fact]
        public void ResolveIndicatorCodes_PullsOneCodePerReferencedSignal_Deduplicated()
        {
            var catalog = new FakeCatalog(
                new SignalDescriptor("RSI.line", "RSI", "line", SignalKind.Oscillator, "RSI"),
                new SignalDescriptor("MACD.hist", "MACD", "hist", SignalKind.Oscillator, "MACD histogram"));

            var service = Build(new FakeDataService(_ => Bars(10)), catalog: catalog);
            var root = new ConditionGroup("g", LogicOperator.And, new ConditionNode[]
            {
                new ConditionLeaf("l1", "RSI.line", LeafOperator.LessThan, 30),
                new ConditionLeaf("l2", "MACD.hist", LeafOperator.GreaterThan, 0),
                new ConditionLeaf("l3", "RSI.line", LeafOperator.GreaterThan, 10),
            });

            var codes = service.ResolveIndicatorCodes(ScreenerSpec.Create("s", null) with { Root = root });

            Assert.Contains("RSI", codes);
            Assert.Contains("MACD", codes);
            Assert.Equal(1, codes.Count(c => c == "RSI"));
        }

        [Fact]
        public void ResolveIndicatorCodes_UnknownSignalId_FallsBackToItsOwnPrefix()
        {
            // A descriptor the catalog has never heard of still names its indicator in the id.
            // Attempting the computation beats silently skipping the leaf.
            var service = Build(new FakeDataService(_ => Bars(10)), catalog: new FakeCatalog());
            var root = new ConditionLeaf("l1", "CIPHER_B.Triple Confluence", LeafOperator.Fired);

            var codes = service.ResolveIndicatorCodes(ScreenerSpec.Create("s", null) with { Root = root });

            Assert.Contains("CIPHER_B", codes);
        }

        [Fact]
        public void ResolveIndicatorCodes_IncludesColumnsEvenWhenNoTreeReferencesThem()
        {
            var catalog = new FakeCatalog(
                new SignalDescriptor("ATR.line", "ATR", "line", SignalKind.Oscillator, "ATR"));
            var service = Build(new FakeDataService(_ => Bars(10)), catalog: catalog);

            var codes = service.ResolveIndicatorCodes(
                ScreenerSpec.Create("s", null) with { Columns = new[] { "ATR.line" } });

            Assert.Contains("ATR", codes);
        }

        [Fact]
        public void ResolveHtfPairs_ReturnsDistinctTimeframeIndicatorPairs()
        {
            var catalog = new FakeCatalog(
                new SignalDescriptor("RSI.line", "RSI", "line", SignalKind.Oscillator, "RSI"));
            var service = Build(new FakeDataService(_ => Bars(10)), catalog: catalog);

            var root = new ConditionGroup("g", LogicOperator.And, new ConditionNode[]
            {
                new ConditionLeaf("l1", "RSI.line", LeafOperator.LessThan, 30, Timeframe: "1w"),
                new ConditionLeaf("l2", "RSI.line", LeafOperator.LessThan, 40, Timeframe: "1w"),
                new ConditionLeaf("l3", "RSI.line", LeafOperator.LessThan, 50), // no timeframe → not HTF
            });

            var pairs = service.ResolveHtfPairs(ScreenerSpec.Create("s", null) with { Root = root });

            Assert.Single(pairs);
            Assert.Equal(("1w", "RSI"), pairs[0]);
        }

        [Fact]
        public void EnumerateLeaves_WalksNestedGroups()
        {
            var root = new ConditionGroup("g1", LogicOperator.Or, new ConditionNode[]
            {
                new ConditionLeaf("a", "X.1", LeafOperator.Fired),
                new ConditionGroup("g2", LogicOperator.And, new ConditionNode[]
                {
                    new ConditionLeaf("b", "X.2", LeafOperator.Fired),
                    new ConditionLeaf("c", "X.3", LeafOperator.Fired),
                }),
            });

            var ids = ScreenerService.EnumerateLeaves(root).Select(l => l.Id).ToList();
            Assert.Equal(new[] { "a", "b", "c" }, ids);
        }
    }
}
