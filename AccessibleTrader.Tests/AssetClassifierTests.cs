using System;
using System.Collections.Generic;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests
{
    public class AssetClassifierTests
    {
        [Fact]
        public void Classify_TooFewBars_ReturnsNull()
        {
            var bars = new List<Ohlcv>();
            for (int i = 0; i < 50; i++)
                bars.Add(new Ohlcv(new DateTime(2024, 1, 1).AddDays(i), 100, 101, 99, 100, 1000));
            Assert.Null(AssetClassifier.Classify(bars));
        }

        [Fact]
        public void Classify_FlatLowVolBullSeries_Returns_LowVol_Random_Bull()
        {
            // 600 bars, 0.5% noise, gentle uptrend = low vol, random walk, bull-biased.
            var bars = SyntheticUptrend(600, dailyDriftPct: 0.001, noisePct: 0.005);
            var p = AssetClassifier.Classify(bars);
            Assert.NotNull(p);
            Assert.Equal(AssetClassifier.VolatilityClass.Low, p!.Volatility);
            Assert.Equal(AssetClassifier.RegimeClass.BullBiased, p.Regime);
        }

        [Fact]
        public void Classify_HighVolNoTrend_Returns_HighOrExtreme_Range()
        {
            // 600 bars, ±4% noise, no drift = high vol, range-bound.
            var bars = SyntheticUptrend(600, dailyDriftPct: 0.0, noisePct: 0.04);
            var p = AssetClassifier.Classify(bars);
            Assert.NotNull(p);
            // Should land in High or Extreme volatility class.
            Assert.True(p!.Volatility == AssetClassifier.VolatilityClass.High
                     || p.Volatility == AssetClassifier.VolatilityClass.Extreme);
            // Either Range or BearBiased depending on noise realization — accept both.
            Assert.NotEqual(AssetClassifier.RegimeClass.BullBiased, p.Regime);
        }

        // The four RecommendV23Long tests that lived here were removed 2026-08-01 along with the
        // method they covered. They asserted which Cipher-B variant the app should pick for a given
        // asset profile — i.e. they encoded the opinion, so they had to go with it. Classify() is
        // still tested above; see StrategyLibraryPolicyTests for the guard that keeps the
        // recommender from coming back.

        private static List<Ohlcv> SyntheticUptrend(int n, double dailyDriftPct, double noisePct, int seed = 42)
        {
            // Generate realistic intraday range proportional to noisePct so ATR
            // reflects the volatility regime we're trying to express. Bar high/low
            // spread = ~2× noisePct of the bar's mid-price, plus the close-vs-open swing.
            var rng = new Random(seed);
            var bars = new List<Ohlcv>(n);
            var anchor = new DateTime(2020, 1, 1);
            double price = 100.0;
            for (int i = 0; i < n; i++)
            {
                double drift = price * dailyDriftPct;
                double closeShock = price * noisePct * (rng.NextDouble() - 0.5);
                double newPrice = price + drift + closeShock;
                // Realistic intraday range: ~2× noisePct of mid, asymmetric per bar.
                double mid = (price + newPrice) * 0.5;
                double rangeHalf = mid * noisePct * (0.8 + 0.4 * rng.NextDouble());
                double high = Math.Max(price, newPrice) + rangeHalf;
                double low  = Math.Min(price, newPrice) - rangeHalf;
                bars.Add(new Ohlcv(anchor.AddDays(i), price, high, low, newPrice, 1_000_000));
                price = newPrice;
            }
            return bars;
        }
    }
}
