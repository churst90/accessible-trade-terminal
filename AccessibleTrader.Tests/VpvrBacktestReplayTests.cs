using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Core.Services.Strategies.Levels;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Verifies the VPVR backtest-replay chain documented in docs/TODO.md Phase 12:
    ///   <c>StrategyBacktester</c> → <c>IBacktestProfileCache</c> → <c>VolumeProfileLevelProvider</c>.
    ///
    /// The production bug this pins: in backtest mode a strategy that gates on POC /
    /// VAH / VAL / HVN / LVN would otherwise read <c>ChartSeries.ProfileBins</c>, which
    /// reflects the workspace's *current* viewport profile — i.e. the final state at
    /// bar N. A strategy evaluated at bar 100 would see POC values computed against
    /// bars [0..N-1], not bars [0..100]. <see cref="VolumeProfileLevelProvider"/> is
    /// supposed to prefer <see cref="IBacktestProfileCache"/> snapshots when IsActive.
    ///
    /// We pin three links in the chain:
    ///   1) <see cref="BacktestProfileCache"/> IsActive / Set / Get / Clear semantics.
    ///   2) <see cref="VolumeProfileLevelProvider"/> reads from the cache when active,
    ///      returning bar-i POC rather than the workspace's final-state ProfileBins.
    ///   3) Falls back to series.ProfileBins when the cache is empty (live path unchanged).
    /// </summary>
    public class VpvrBacktestReplayTests
    {
        // ── Link 1: cache primitive semantics ───────────────────────────────────

        [Fact]
        public void BacktestProfileCache_IsActive_ReflectsSnapshotPresence()
        {
            var cache = new BacktestProfileCache();
            Assert.False(cache.IsActive);

            cache.Set("VPVR", new List<ProfileBin>
            {
                Bin(100, true),
            });
            Assert.True(cache.IsActive);

            cache.Clear();
            Assert.False(cache.IsActive);
        }

        [Fact]
        public void BacktestProfileCache_Get_RoundTripsLastSet()
        {
            var cache = new BacktestProfileCache();
            var firstSnapshot  = new List<ProfileBin> { Bin(100, true) };
            var secondSnapshot = new List<ProfileBin> { Bin(200, true) };

            cache.Set("VPVR", firstSnapshot);
            cache.Set("VPVR", secondSnapshot);

            Assert.Same(secondSnapshot, cache.Get("VPVR"));
            Assert.Null(cache.Get("TPO"));
        }

        // ── Link 2: provider reads cache during replay ──────────────────────────

        [Fact]
        public void VolumeProfileLevelProvider_ReadsCacheBinsWhenActive()
        {
            var cache = new BacktestProfileCache();
            var cachedBins = new List<ProfileBin>
            {
                Bin(500.0, isPoc: true),                                 // cached POC
                Bin(450.0, isPoc: false, isValueArea: true),             // cached VA boundary
            };
            var liveBins = new List<ProfileBin>
            {
                Bin(999.0, isPoc: true),                                 // workspace POC (stale in backtest)
            };

            // Simulate in-progress backtest: cache has a bar-i snapshot for VPVR.
            cache.Set("VPVR", cachedBins);

            var seriesConfig = new SeriesConfig { Id = "vpvr", Name = "VPVR", IndicatorCode = "VPVR" };
            var buffer = new SeriesDataBuffer { SeriesId = "vpvr" };
            var series = new ChartSeries(seriesConfig, buffer);
            series.ProfileBins = liveBins;

            var state = WorkspaceState.Initial with
            {
                ActiveSeries = new[] { series }.ToImmutableList(),
            };

            var provider = new VolumeProfileLevelProvider(cache);
            var levels = provider.GetLevels(System.Array.Empty<Ohlcv>(), state);

            // The POC returned MUST be the cached 500.0 — NOT the workspace's stale 999.0.
            // A bug that silently dropped the cache read would flip this assertion.
            var poc = levels.FirstOrDefault(l => l.Kind == LevelKind.Poc);
            Assert.NotNull(poc);
            Assert.Equal(500.0, poc!.Price);
            Assert.DoesNotContain(levels, l => l.Price == 999.0);
        }

        // ── Link 3: provider falls through to series.ProfileBins when cache empty ──

        [Fact]
        public void VolumeProfileLevelProvider_FallsThroughWhenCacheInactive()
        {
            var cache = new BacktestProfileCache(); // never Set — IsActive=false
            var liveBins = new List<ProfileBin>
            {
                Bin(123.0, isPoc: true),
            };

            var seriesConfig = new SeriesConfig { Id = "vpvr", Name = "VPVR", IndicatorCode = "VPVR" };
            var buffer = new SeriesDataBuffer { SeriesId = "vpvr" };
            var series = new ChartSeries(seriesConfig, buffer);
            series.ProfileBins = liveBins;

            var state = WorkspaceState.Initial with
            {
                ActiveSeries = new[] { series }.ToImmutableList(),
            };

            var provider = new VolumeProfileLevelProvider(cache);
            var levels = provider.GetLevels(System.Array.Empty<Ohlcv>(), state);

            var poc = levels.FirstOrDefault(l => l.Kind == LevelKind.Poc);
            Assert.NotNull(poc);
            Assert.Equal(123.0, poc!.Price);
        }

        [Fact]
        public void VolumeProfileLevelProvider_ReturnsEmptyWhenNoProfileSeries()
        {
            var cache = new BacktestProfileCache();
            var seriesConfig = new SeriesConfig { Id = "sma", Name = "SMA", IndicatorCode = "SMA" };
            var buffer = new SeriesDataBuffer { SeriesId = "sma" };
            var series = new ChartSeries(seriesConfig, buffer);

            var state = WorkspaceState.Initial with
            {
                ActiveSeries = new[] { series }.ToImmutableList(),
            };

            var provider = new VolumeProfileLevelProvider(cache);
            Assert.Empty(provider.GetLevels(System.Array.Empty<Ohlcv>(), state));
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static ProfileBin Bin(double priceMid, bool isPoc, bool isValueArea = true, double volume = 1000)
            => new ProfileBin
            {
                PriceLow    = priceMid - 0.5,
                PriceHigh   = priceMid + 0.5,
                TotalVolume = volume,
                TpoPeriodCount = 1,
                IsPOC       = isPoc,
                IsValueArea = isValueArea,
            };
    }
}
