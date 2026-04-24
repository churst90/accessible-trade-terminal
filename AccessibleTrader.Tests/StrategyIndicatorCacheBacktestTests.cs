using System;
using System.Collections.Generic;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Phase 10-F(b). Pins the contract that <see cref="StrategyIndicatorCache"/> is
/// invalidated per bar during a backtest so a strategy reading SMA / EMA / RSI
/// via the cache sees the value computed against the current growing history
/// buffer, not a stale value from a prior bar.
///
/// Before this fix the backtester relied on the engine's live-loop invalidation
/// path and never called <c>Invalidate(historyBuffer.Count)</c> itself, so a
/// backtest run with the cache in play would return the SMA computed at the
/// first bar for every subsequent bar — the cache key (<c>"SMA|period|count"</c>)
/// was frozen at the initial count.
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
