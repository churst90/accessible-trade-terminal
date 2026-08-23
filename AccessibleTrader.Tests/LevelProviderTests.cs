using System;
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
    /// The five level providers plus the LevelService aggregator. These feed
    /// RiskPlanResolver and ProtectiveLevelValidator — i.e. actual stop and target
    /// placement — so the load-bearing assertions are the causality clips (a pivot that
    /// needs future bars to confirm must not be visible in a backtest slice) and the
    /// support/resistance kind mapping.
    /// </summary>
    public class LevelProviderTests
    {
        // ── Shared fixture helpers ──────────────────────────────────────────────

        private static Ohlcv Bar(double high, double low, int minute, double? close = null) => new(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(minute),
            (high + low) / 2, high, low, close ?? (high + low) / 2, 1000);

        private static List<Ohlcv> FlatBars(int count, double high = 10, double low = 5)
            => Enumerable.Range(0, count).Select(i => Bar(high, low, i)).ToList();

        private static WorkspaceState StateWith(params ChartSeries[] series)
            => WorkspaceState.Initial with { ActiveSeries = ImmutableList.CreateRange(series) };

        private static ChartSeries IndicatorSeries(string code,
            params (string Component, double[] Values)[] components)
        {
            var s = new ChartSeries();
            s.Config.Name = code;
            s.Config.IndicatorCode = code;
            foreach (var (name, values) in components)
            {
                s.Config.Components.Add(new ComponentConfig { Name = name, DisplayName = name });
                s.Data.ComponentData[name] = values;
            }
            return s;
        }

        private static ChartSeries DrawingSeries(DrawingType type,
            double? p1 = null, double? p2 = null)
        {
            var s = new ChartSeries();
            s.Drawing = new DrawingData { Type = type, AnchorPrice1 = p1, AnchorPrice2 = p2 };
            return s;
        }

        private sealed class StubLevelProvider : ILevelProvider
        {
            private readonly Func<IReadOnlyList<PriceLevel>> _levels;
            public StubLevelProvider(Func<IReadOnlyList<PriceLevel>> levels) => _levels = levels;
            public StubLevelProvider(params PriceLevel[] levels) : this(() => levels) { }
            public string SourceId => "stub";
            public IReadOnlyList<PriceLevel> GetLevels(IReadOnlyList<Ohlcv> history, WorkspaceState state)
                => _levels();
        }

        private static readonly List<Ohlcv> SomeBars = FlatBars(5);
        private static readonly WorkspaceState EmptyState = WorkspaceState.Initial;

        // ── LevelService aggregation ────────────────────────────────────────────

        [Fact]
        public void GetAllLevels_MergesEveryProvider()
        {
            var svc = new LevelService(new ILevelProvider[]
            {
                new StubLevelProvider(new PriceLevel(90, LevelKind.Support)),
                new StubLevelProvider(new PriceLevel(110, LevelKind.Resistance), new PriceLevel(100, LevelKind.Poc)),
            });

            var all = svc.GetAllLevels(SomeBars, EmptyState);

            Assert.Equal(new[] { 90.0, 110.0, 100.0 }, all.Select(l => l.Price));
        }

        [Fact]
        public void ThrowingProvider_IsSkipped_OthersStillContribute()
        {
            var svc = new LevelService(new ILevelProvider[]
            {
                new StubLevelProvider(() => throw new InvalidOperationException("indicator not ready")),
                new StubLevelProvider(new PriceLevel(95, LevelKind.Support)),
            });

            var all = svc.GetAllLevels(SomeBars, EmptyState);

            Assert.Equal(95.0, Assert.Single(all).Price);
        }

        [Fact]
        public void NullReturningProvider_IsTolerated()
        {
            var svc = new LevelService(new ILevelProvider[]
            {
                new StubLevelProvider(() => null!),
                new StubLevelProvider(new PriceLevel(95, LevelKind.Support)),
            });

            Assert.Single(svc.GetAllLevels(SomeBars, EmptyState));
        }

        [Fact]
        public void NearestBelow_IsStrictlyBelow_AndNearest()
        {
            var svc = new LevelService(new ILevelProvider[]
            {
                new StubLevelProvider(
                    new PriceLevel(90, LevelKind.Support),
                    new PriceLevel(95, LevelKind.Support),
                    new PriceLevel(100, LevelKind.Support),   // equal to price — excluded (strict)
                    new PriceLevel(105, LevelKind.Resistance)),
            });

            var below = svc.NearestBelow(100, SomeBars, EmptyState);

            Assert.Equal(95.0, below!.Price);
        }

        [Fact]
        public void NearestAbove_IsStrictlyAbove_AndNearest()
        {
            var svc = new LevelService(new ILevelProvider[]
            {
                new StubLevelProvider(
                    new PriceLevel(100, LevelKind.Resistance), // equal — excluded
                    new PriceLevel(105, LevelKind.Resistance),
                    new PriceLevel(110, LevelKind.Resistance)),
            });

            Assert.Equal(105.0, svc.NearestAbove(100, SomeBars, EmptyState)!.Price);
        }

        [Fact]
        public void KindFilter_SkipsCloserLevelsOfOtherKinds()
        {
            var svc = new LevelService(new ILevelProvider[]
            {
                new StubLevelProvider(
                    new PriceLevel(98, LevelKind.Poc),
                    new PriceLevel(92, LevelKind.Support)),
            });

            Assert.Equal(92.0, svc.NearestBelow(100, SomeBars, EmptyState, LevelKind.Support)!.Price);
            Assert.Equal(98.0, svc.NearestBelow(100, SomeBars, EmptyState)!.Price);
        }

        [Fact]
        public void NoQualifyingLevel_ReturnsNull()
        {
            var svc = new LevelService(new ILevelProvider[]
            {
                new StubLevelProvider(new PriceLevel(105, LevelKind.Resistance)),
            });

            Assert.Null(svc.NearestBelow(100, SomeBars, EmptyState));
            Assert.Null(svc.NearestAbove(200, SomeBars, EmptyState));
        }

        // ── DrawnHorizontalLevelProvider ────────────────────────────────────────

        [Fact]
        public void Drawn_EmptyHistoryOrNoDrawings_ReturnsEmpty()
        {
            var p = new DrawnHorizontalLevelProvider();
            Assert.Empty(p.GetLevels(new List<Ohlcv>(), StateWith(DrawingSeries(DrawingType.HorizontalLine, 7))));
            Assert.Empty(p.GetLevels(SomeBars, StateWith(IndicatorSeries("RSI"))));
        }

        [Fact]
        public void Drawn_LineBelowPriceIsSupport_AboveIsResistance()
        {
            // Bars close at 7.5; lines at 6 (below) and 9 (above).
            var p = new DrawnHorizontalLevelProvider();
            var state = StateWith(
                DrawingSeries(DrawingType.HorizontalLine, 6),
                DrawingSeries(DrawingType.HorizontalLine, 9));

            var levels = p.GetLevels(SomeBars, state);

            Assert.Equal(2, levels.Count);
            var support = Assert.Single(levels, l => l.Kind == LevelKind.Support);
            Assert.Equal(6.0, support.Price);
            Assert.Equal(0.8, support.Strength);
            Assert.Equal("Drawn", support.Source);
            Assert.Equal(9.0, Assert.Single(levels, l => l.Kind == LevelKind.Resistance).Price);
        }

        [Theory]
        [InlineData(DrawingType.TrendLine)]
        [InlineData(DrawingType.RiskReward)]
        [InlineData(DrawingType.Rectangle)]
        public void Drawn_TwoAnchorDrawings_ContributeBothEndpoints(DrawingType type)
        {
            var p = new DrawnHorizontalLevelProvider();
            var levels = p.GetLevels(SomeBars, StateWith(DrawingSeries(type, 6, 9)));

            Assert.Equal(new[] { 6.0, 9.0 }, levels.Select(l => l.Price).OrderBy(x => x));
        }

        [Fact]
        public void Drawn_InvalidPrices_AreSkipped()
        {
            var p = new DrawnHorizontalLevelProvider();
            var state = StateWith(
                DrawingSeries(DrawingType.HorizontalLine, double.NaN),
                DrawingSeries(DrawingType.HorizontalLine, -5),
                DrawingSeries(DrawingType.HorizontalLine, 0),
                DrawingSeries(DrawingType.TrendLine, 6, null)); // null second anchor tolerated

            var levels = p.GetLevels(SomeBars, state);

            Assert.Equal(6.0, Assert.Single(levels).Price);
        }

        // ── SwingPivotLevelProvider ─────────────────────────────────────────────

        [Fact]
        public void Swing_HistoryShorterThanWindow_ReturnsEmpty()
        {
            var p = new SwingPivotLevelProvider(); // needs 2*5+1 bars
            Assert.Empty(p.GetLevels(FlatBars(10), EmptyState));
        }

        [Fact]
        public void Swing_DetectsSwingHighAndSwingLow()
        {
            var bars = FlatBars(21);
            bars[10] = Bar(20, 5, 10);  // swing high: strictly highest within ±5
            bars[15] = Bar(10, 1, 15);  // swing low: strictly lowest within ±5
            var p = new SwingPivotLevelProvider();

            var levels = p.GetLevels(bars, EmptyState);

            Assert.Equal(2, levels.Count);
            var high = Assert.Single(levels, l => l.Kind == LevelKind.Resistance);
            Assert.Equal(20.0, high.Price);
            Assert.Contains("Swing High", high.Source);
            var low = Assert.Single(levels, l => l.Kind == LevelKind.Support);
            Assert.Equal(1.0, low.Price);
        }

        [Fact]
        public void Swing_ExtremumWithinLookbackOfTheLastBar_IsNotAPivotYet()
        {
            // The causality property: a maximum on the final bar cannot be a confirmed
            // swing high — it needs LookbackBars future bars to fail to exceed it.
            var bars = FlatBars(21);
            bars[20] = Bar(30, 5, 20);
            bars[18] = Bar(25, 5, 18);
            var p = new SwingPivotLevelProvider();

            Assert.Empty(p.GetLevels(bars, EmptyState));
        }

        [Fact]
        public void Swing_PlateauOfEqualHighs_IsNotAPivot()
        {
            var bars = FlatBars(21);
            bars[9] = Bar(20, 5, 9);
            bars[10] = Bar(20, 5, 10); // neighbour ties — ">=" disqualifies both
            var p = new SwingPivotLevelProvider();

            Assert.Empty(p.GetLevels(bars, EmptyState));
        }

        [Fact]
        public void Swing_MaxPivotsCap_KeepsTheMostRecent()
        {
            var bars = FlatBars(40);
            bars[8]  = Bar(20, 5, 8);   // oldest pivot — should be dropped
            bars[20] = Bar(21, 5, 20);
            bars[32] = Bar(22, 5, 32);
            var p = new SwingPivotLevelProvider { MaxPivots = 2 };

            var levels = p.GetLevels(bars, EmptyState);

            Assert.Equal(2, levels.Count);
            Assert.DoesNotContain(levels, l => l.Price == 20.0);
        }

        // ── IchimokuLevelProvider ───────────────────────────────────────────────

        [Fact]
        public void Ichimoku_NoIchimokuSeries_ReturnsEmpty()
        {
            var p = new IchimokuLevelProvider();
            Assert.Empty(p.GetLevels(SomeBars, StateWith(IndicatorSeries("RSI"))));
        }

        [Fact]
        public void Ichimoku_EmitsKijunAndKumoBounds()
        {
            var series = IndicatorSeries("ICHIMOKU",
                ("Kijun-sen", new[] { 100.0, 101.0 }),
                ("Senkou Span A", new[] { 95.0, 96.0 }),
                ("Senkou Span B", new[] { 105.0, 104.0 }));
            var p = new IchimokuLevelProvider();

            var levels = p.GetLevels(FlatBars(2), StateWith(series));

            Assert.Equal(3, levels.Count);
            Assert.Equal(101.0, Assert.Single(levels, l => l.Kind == LevelKind.Kijun).Price);
            // Kumo top/bottom are max/min of the two Senkou spans, whichever is which.
            Assert.Equal(104.0, Assert.Single(levels, l => l.Kind == LevelKind.KumoTop).Price);
            Assert.Equal(96.0, Assert.Single(levels, l => l.Kind == LevelKind.KumoBottom).Price);
        }

        [Fact]
        public void Ichimoku_BacktestSlice_DoesNotReadComponentValuesFromTheFuture()
        {
            // In backtest mode history is a truncated slice while the component arrays
            // hold the full run — values past history.Count are the future.
            var kijun = new double[10];
            Array.Fill(kijun, 100.0);
            for (int i = 5; i < 10; i++) kijun[i] = 999.0;
            var series = IndicatorSeries("ICHIMOKU", ("Kijun-sen", kijun));
            var p = new IchimokuLevelProvider();

            var levels = p.GetLevels(FlatBars(5), StateWith(series));

            Assert.Equal(100.0, Assert.Single(levels).Price);
        }

        [Fact]
        public void Ichimoku_KijunAlone_WhenSenkouSpansAreMissing()
        {
            var series = IndicatorSeries("ICHIMOKU", ("Kijun-sen", new[] { 100.0 }));
            var p = new IchimokuLevelProvider();

            var levels = p.GetLevels(FlatBars(1), StateWith(series));

            Assert.Equal(LevelKind.Kijun, Assert.Single(levels).Kind);
        }

        [Fact]
        public void Ichimoku_IndicatorCodeMatch_IsCaseInsensitive()
        {
            var series = IndicatorSeries("Ichimoku", ("Kijun-sen", new[] { 100.0 }));
            Assert.Single(new IchimokuLevelProvider().GetLevels(FlatBars(1), StateWith(series)));
        }

        // ── CipherSrLevelProvider ───────────────────────────────────────────────

        private static double[] SparseArray(int length, params (int Index, double Value)[] pivots)
        {
            var arr = new double[length];
            Array.Fill(arr, double.NaN);
            foreach (var (i, v) in pivots) arr[i] = v;
            return arr;
        }

        [Theory]
        [InlineData(null, null, 100, 4)]    // AutoScale default ON: clamp(100/25, 2, 15)
        [InlineData(null, null, 1000, 15)]  // upper clamp
        [InlineData(null, null, 10, 2)]     // lower clamp
        [InlineData(0.0, 8.0, 100, 8)]      // explicit PivotBars honoured when AutoScale off
        [InlineData(0.0, 100.0, 100, 60)]   // explicit clamp upper
        [InlineData(0.0, 1.0, 100, 2)]      // explicit clamp lower
        [InlineData(1.0, 8.0, 100, 4)]      // AutoScale ON ignores PivotBars
        public void CipherSr_ConfirmationLag_MirrorsTheIndicator(
            double? autoScale, double? pivotBars, int barCount, int expected)
        {
            Dictionary<string, double>? parameters = null;
            if (autoScale.HasValue || pivotBars.HasValue)
            {
                parameters = new Dictionary<string, double>();
                if (autoScale.HasValue) parameters["AutoScale"] = autoScale.Value;
                if (pivotBars.HasValue) parameters["PivotBars"] = pivotBars.Value;
            }

            Assert.Equal(expected, CipherSrLevelProvider.ResolveConfirmationLag(parameters, barCount));
        }

        [Fact]
        public void CipherSr_EmitsPivotsFromBothComponents_WithMatchingKinds()
        {
            var series = IndicatorSeries("CIPHER_SR",
                ("Resistance", SparseArray(100, (40, 110.0))),
                ("Support",    SparseArray(100, (50, 90.0))));
            var p = new CipherSrLevelProvider();

            var levels = p.GetLevels(FlatBars(100), StateWith(series));

            Assert.Equal(2, levels.Count);
            Assert.Equal(110.0, Assert.Single(levels, l => l.Kind == LevelKind.Resistance).Price);
            Assert.Equal(90.0, Assert.Single(levels, l => l.Kind == LevelKind.Support).Price);
        }

        [Fact]
        public void CipherSr_PivotInsideTheConfirmationLag_IsNotVisible()
        {
            // 100 bars, AutoScale default → lag 4, so indices 96..99 are unconfirmed.
            // This is the backtest-flattery fence: before the 2026-07-26 fix a strategy
            // could act on a pivot that still needed future bars to confirm.
            var series = IndicatorSeries("CIPHER_SR",
                ("Resistance", SparseArray(100, (97, 110.0), (90, 105.0))),
                ("Support",    Array.Empty<double>()));
            var p = new CipherSrLevelProvider();

            var levels = p.GetLevels(FlatBars(100), StateWith(series));

            Assert.Equal(105.0, Assert.Single(levels).Price);
        }

        [Fact]
        public void CipherSr_MoreRecentPivot_IsStronger()
        {
            var series = IndicatorSeries("CIPHER_SR",
                ("Resistance", SparseArray(100, (20, 110.0), (80, 111.0))),
                ("Support",    Array.Empty<double>()));
            var p = new CipherSrLevelProvider();

            var levels = p.GetLevels(FlatBars(100), StateWith(series));

            double older = levels.Single(l => l.Price == 110.0).Strength;
            double newer = levels.Single(l => l.Price == 111.0).Strength;
            Assert.True(newer > older, $"expected recency weighting, got older={older} newer={newer}");
        }

        [Fact]
        public void CipherSr_PivotOlderThanTheLookbackWindow_IsDropped()
        {
            var series = IndicatorSeries("CIPHER_SR",
                ("Resistance", SparseArray(100, (5, 110.0), (60, 105.0))),
                ("Support",    Array.Empty<double>()));
            var p = new CipherSrLevelProvider { LookbackBars = 50 };

            var levels = p.GetLevels(FlatBars(100), StateWith(series));

            Assert.Equal(105.0, Assert.Single(levels).Price);
        }

        // ── VolumeProfileLevelProvider ──────────────────────────────────────────

        private static ProfileBin Bin(double mid, double volume,
            bool poc = false, bool valueArea = false, bool singlePrint = false) => new()
        {
            PriceLow = mid - 0.5,
            PriceHigh = mid + 0.5,
            TotalVolume = volume,
            TpoPeriodCount = 1,
            IsPOC = poc,
            IsValueArea = valueArea,
            IsSinglePrint = singlePrint,
        };

        private static ChartSeries ProfileSeries(string code, params ProfileBin[] bins)
        {
            var s = IndicatorSeries(code);
            s.ProfileBins = bins.ToList();
            return s;
        }

        // Volumes 50/30/10/5/5 → mean 20, HVN threshold 26, LVN threshold 8.
        private static ProfileBin[] StandardBins() => new[]
        {
            Bin(100, 50, poc: true, valueArea: true),  // POC (and HVN: 50 > 26)
            Bin(101, 30, valueArea: true),             // VAH (highest VA mid) + HVN
            Bin(99, 10, valueArea: true),              // VAL (lowest VA mid), not LVN (10 ≥ 8)
            Bin(102, 5),                               // LVN by volume (5 < 8)
            Bin(98, 5, singlePrint: true),             // LVN by single print
        };

        [Fact]
        public void Profile_NonProfileSeries_IsIgnored()
        {
            var s = ProfileSeries("RSI", StandardBins());
            Assert.Empty(new VolumeProfileLevelProvider().GetLevels(SomeBars, StateWith(s)));
        }

        [Fact]
        public void Profile_ClassifiesPocVahValHvnLvn_LikeProfileBinClassifier()
        {
            var s = ProfileSeries("VPVR", StandardBins());

            var levels = new VolumeProfileLevelProvider().GetLevels(SomeBars, StateWith(s));

            Assert.Equal(100.0, Assert.Single(levels, l => l.Kind == LevelKind.Poc).Price);
            Assert.Equal(101.0, Assert.Single(levels, l => l.Kind == LevelKind.Vah).Price);
            Assert.Equal(99.0, Assert.Single(levels, l => l.Kind == LevelKind.Val).Price);
            Assert.Equal(new[] { 100.0, 101.0 },
                levels.Where(l => l.Kind == LevelKind.Hvn).Select(l => l.Price).OrderBy(x => x));
            Assert.Equal(new[] { 98.0, 102.0 },
                levels.Where(l => l.Kind == LevelKind.Lvn).Select(l => l.Price).OrderBy(x => x));
        }

        [Fact]
        public void Profile_CodeMatch_IsCaseInsensitive_AndCoversTpo()
        {
            var s = ProfileSeries("tpo", StandardBins());
            var levels = new VolumeProfileLevelProvider().GetLevels(SomeBars, StateWith(s));
            Assert.Contains(levels, l => l.Kind == LevelKind.Poc && l.Source.StartsWith("TPO"));
        }

        [Fact]
        public void Profile_ActiveBacktestCache_OverridesTheLiveBins()
        {
            // The future-leak fence: during a backtest the live series bins hold the FINAL
            // profile; the provider must read the bar-i snapshot from the cache instead.
            var s = ProfileSeries("VPVR", StandardBins());
            var cache = new BacktestProfileCache();
            cache.Set("VPVR", new[] { Bin(55, 10, poc: true) });

            var levels = new VolumeProfileLevelProvider(cache).GetLevels(SomeBars, StateWith(s));

            Assert.Equal(55.0, Assert.Single(levels, l => l.Kind == LevelKind.Poc).Price);
            Assert.DoesNotContain(levels, l => l.Price == 100.0);
        }

        [Fact]
        public void Profile_InactiveCache_FallsThroughToTheLiveBins()
        {
            var s = ProfileSeries("VPVR", StandardBins());
            var cache = new BacktestProfileCache(); // nothing set → inactive

            var levels = new VolumeProfileLevelProvider(cache).GetLevels(SomeBars, StateWith(s));

            Assert.Equal(100.0, Assert.Single(levels, l => l.Kind == LevelKind.Poc).Price);
        }

        [Fact]
        public void Profile_EmptyBins_YieldNoLevels()
        {
            var s = ProfileSeries("VPVR");
            Assert.Empty(new VolumeProfileLevelProvider().GetLevels(SomeBars, StateWith(s)));
        }
    }
}
