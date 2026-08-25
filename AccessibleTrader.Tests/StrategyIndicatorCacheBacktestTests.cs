using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.Tests;

/// <summary>
/// Phase 10-F(b). Pins the contract that <see cref="StrategyIndicatorCache"/> is
/// invalidated per bar during a backtest so a strategy reading SMA / EMA / RSI
/// via the cache sees the value computed against the current growing history
/// buffer, not a stale value from a prior bar.
///
/// Before this fix the backtester relied on the engine's live-loop invalidation
/// path and never called <c>BeginSeries(..., historyBuffer.Count)</c> itself, so a
/// backtest run with the cache in play would return the SMA computed at the
/// first bar for every subsequent bar — the cache key (<c>"SMA|period|count"</c>)
/// was frozen at the initial count.
///
/// Also pins the series scope added 2026-08-25: the same key carried no symbol,
/// provider or timeframe, so two charts sitting at the same bar count read each
/// other's values. See <see cref="Two_series_at_the_same_bar_count_do_not_share_a_cached_value"/>.
/// </summary>
public sealed class StrategyIndicatorCacheBacktestTests
{
    [Fact]
    public async System.Threading.Tasks.Task Cache_invalidates_per_bar_during_backtest()
    {
        // Build a monotonically-increasing close series so each bar's SMA is distinct.
        var bars = new List<Ohlcv>();
        for (int i = 0; i < 30; i++)
        {
            var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i);
            double close = 100 + i;
            bars.Add(new Ohlcv(t, close, close, close, close, 1000));
        }

        var cache = new StrategyIndicatorCache();
        var strategy = new SmaProbeStrategy(cache, period: 5);

        var backtester = new AccessibleTrader.Core.Strategies.StrategyBacktester(
            profileService: null,
            profileCache: null,
            mtf: null,
            indicatorCache: cache);

        var config = new BacktestConfig(StartingCapital: 10_000, WarmupBars: 0);
        var result = await backtester.RunAsync(strategy, bars, config);

        // Sanity: backtest completed.
        Assert.NotNull(result);

        // The probe records the SMA it reads at each bar. If the cache is invalidated
        // correctly, consecutive values differ (1 per bar). If invalidation is broken,
        // every value after the first is identical.
        var smas = strategy.ObservedSmas;
        Assert.True(smas.Count >= 15);

        // First (period-1) bars produce NaN (insufficient history); skip those.
        var observed = new List<double>();
        foreach (var v in smas)
        {
            if (!double.IsNaN(v)) observed.Add(v);
        }
        Assert.True(observed.Count >= 10);

        // Every neighbour pair should differ — SMA of a monotonic series is strictly
        // increasing, so no two adjacent bars can share a cached value.
        for (int i = 1; i < observed.Count; i++)
        {
            Assert.NotEqual(observed[i - 1], observed[i]);
        }
    }

    /// <summary>
    /// Two different series, identical bar counts, different prices. The old key
    /// (<c>"SMA|period|count"</c>) carried no series identity, so the second series read
    /// the first one's moving average — silently, with no error. This is the guard: it
    /// goes red the moment <c>BeginSeries</c> stops contributing the identity to the key.
    /// </summary>
    [Fact]
    public void Two_series_at_the_same_bar_count_do_not_share_a_cached_value()
    {
        static List<Ohlcv> Series(double basePrice)
        {
            var bars = new List<Ohlcv>();
            for (int i = 0; i < 100; i++)
            {
                var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i);
                double close = basePrice + i;
                bars.Add(new Ohlcv(t, close, close, close, close, 1000));
            }
            return bars;
        }

        // Same length (100), same period, wildly different price levels.
        var btc = Series(60_000);
        var kas = Series(0.05);

        var btcId = new ChartIdentity("Spot", "Bitstamp", "BTC/USD", "1h");
        var kasId = new ChartIdentity("Spot", "Kraken", "KAS/USD", "4h");

        var cache = new StrategyIndicatorCache();

        cache.BeginSeries(btcId, btc.Count);
        double btcSma = cache.GetSma(btc, period: 20);

        cache.BeginSeries(kasId, kas.Count);
        double kasSma = cache.GetSma(kas, period: 20);

        // Recompute independently: neither may have been served the other's entry.
        double expectedBtc = 0, expectedKas = 0;
        for (int i = btc.Count - 20; i < btc.Count; i++) expectedBtc += btc[i].Close;
        for (int i = kas.Count - 20; i < kas.Count; i++) expectedKas += kas[i].Close;
        expectedBtc /= 20;
        expectedKas /= 20;

        Assert.Equal(expectedBtc, btcSma, precision: 8);
        Assert.Equal(expectedKas, kasSma, precision: 8);

        // And going back to the first series must still return ITS value, not the
        // second's — opening a scope evicts only that scope's stale bar counts.
        cache.BeginSeries(btcId, btc.Count);
        Assert.Equal(expectedBtc, cache.GetSma(btc, period: 20), precision: 8);
    }

    /// <summary>
    /// With no scope open the cache must compute and return without storing. An entry
    /// written under an unattributable key is exactly the cross-series bug in miniature,
    /// so the correct behaviour is to skip the cache rather than guess an identity.
    /// </summary>
    [Fact]
    public void An_unscoped_read_computes_correctly_and_caches_nothing()
    {
        var a = new List<Ohlcv>();
        var b = new List<Ohlcv>();
        for (int i = 0; i < 50; i++)
        {
            var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i);
            a.Add(new Ohlcv(t, 100 + i, 100 + i, 100 + i, 100 + i, 1000));
            b.Add(new Ohlcv(t, 900 + i, 900 + i, 900 + i, 900 + i, 1000));
        }

        var cache = new StrategyIndicatorCache();

        // No BeginSeries call anywhere in this flow.
        double aSma = cache.GetSma(a, period: 10);
        double bSma = cache.GetSma(b, period: 10);

        double expectedA = 0, expectedB = 0;
        for (int i = 40; i < 50; i++) { expectedA += a[i].Close; expectedB += b[i].Close; }
        expectedA /= 10;
        expectedB /= 10;

        Assert.Equal(expectedA, aSma, precision: 8);
        Assert.Equal(expectedB, bSma, precision: 8);
        Assert.NotEqual(aSma, bSma);
    }

    /// <summary>
    /// Minimal strategy whose OnBar records the SMA it reads from the shared cache.
    /// Doesn't emit any signals — the test only cares about what the cache returned.
    /// </summary>
    private sealed class SmaProbeStrategy : ITradingStrategy
    {
        private readonly IStrategyIndicatorCache _cache;
        private readonly int _period;
        public List<double> ObservedSmas { get; } = new();

        public SmaProbeStrategy(IStrategyIndicatorCache cache, int period)
        {
            _cache = cache;
            _period = period;
        }

        public string Id => "test.sma-probe";
        public string Name => "SMA probe";
        public string Description => "Test-only strategy that reads the shared indicator cache on every bar.";
        public StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;
        public IReadOnlyList<StrategyParameter> Parameters { get; } = Array.Empty<StrategyParameter>();

        public void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state, IDictionary<string, object> parameterValues) { }

        public StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
        {
            ObservedSmas.Add(_cache.GetSma(history, _period));
            return null;
        }

        public void OnOrderFilled(OrderUpdate fill) { }
        public void OnStop() { }
        public StrategyMetrics GetMetrics() => new(0, 0, 0, 0, 0, 0);
    }
}
