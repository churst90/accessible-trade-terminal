using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// A strategy's indicator must be the indicator on the user's screen.
    ///
    /// <para>
    /// <see cref="StrategyIndicatorCache"/> is what a DLL plugin, a Roslyn strategy and (soon) a
    /// sandboxed script call to ask for "RSI 14". Every RSI the chart draws is Wilder's — that is
    /// what <see cref="IndicatorMath.Rsi"/> computes, what <c>PulseProvider</c> computes, and what
    /// the cache's own interface has always documented. The cache computed a <b>Cutler</b> RSI: a
    /// plain arithmetic mean over the last <c>period</c> changes, no Wilder smoothing. The two do
    /// not converge with more data; on 14 bars they routinely sit several points apart, which is
    /// the whole distance between a 30-threshold entry firing and not firing. The EMA seeded
    /// differently again, so "EMA 20" was a third number.
    /// </para>
    ///
    /// <para>
    /// The cache does not delegate to <see cref="IndicatorMath"/> — these are scalar, last-bar
    /// questions asked once per bar per strategy, and the array helpers allocate two arrays per
    /// call. This file is what stands in for shared code: it pins each cache scalar against the
    /// last slot of the corresponding library series, so the two cannot drift apart again.
    /// </para>
    /// </summary>
    public class StrategyIndicatorCacheParityTests
    {
        private static readonly DateTime Start = new(2021, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>A series with real turns in it — a monotone ramp makes RSI 100 and hides everything.</summary>
        private static List<Ohlcv> Series(int count, int seed = 7)
        {
            var rng = new Random(seed);
            var bars = new List<Ohlcv>(count);
            double p = 100;
            for (int i = 0; i < count; i++)
            {
                p += (rng.NextDouble() - 0.48) * 3.0;
                bars.Add(new Ohlcv(Start.AddHours(i), p, p + 1, p - 1, p, 1000));
            }
            return bars;
        }

        private static double[] Closes(IReadOnlyList<Ohlcv> bars) => bars.Select(b => b.Close).ToArray();

        private static StrategyIndicatorCache Scoped()
        {
            var cache = new StrategyIndicatorCache();
            cache.BeginSeries(new ChartIdentity("Crypto", "TestProvider", "BTC/USD", "1h"), 0);
            return cache;
        }

        [Theory]
        [InlineData(14)]
        [InlineData(7)]
        [InlineData(21)]
        public void RsiMatchesTheLibraryTheChartDrawsFrom(int period)
        {
            var bars = Series(300);
            var expected = IndicatorMath.Rsi(Closes(bars), period)[^1];

            Assert.Equal(expected, Scoped().GetRsi(bars, period), precision: 9);
        }

        [Fact]
        public void TheRsiTestCanTellWilderFromCutler()
        {
            // Vacuity check. If the two conventions happened to agree on this fixture, the
            // parity assertion above would be pinning nothing — so prove the fixture separates
            // them, and by a margin that matters to a threshold rule.
            var bars = Series(300);
            double wilder = IndicatorMath.Rsi(Closes(bars), 14)[^1];
            double cutler = CutlerRsi(bars, 14);

            Assert.True(Math.Abs(wilder - cutler) > 1.0,
                $"fixture cannot separate the two RSIs (Wilder {wilder:F3} vs Cutler {cutler:F3})");
        }

        /// <summary>The implementation this cache shipped with, kept here as the thing being ruled out.</summary>
        private static double CutlerRsi(IReadOnlyList<Ohlcv> data, int period)
        {
            int count = data.Count;
            double gain = 0, loss = 0;
            for (int i = count - period; i < count; i++)
            {
                double change = data[i].Close - data[i - 1].Close;
                if (change > 0) gain += change; else loss -= change;
            }
            double avgGain = gain / period, avgLoss = loss / period;
            return avgLoss == 0 ? 100.0 : 100.0 - 100.0 / (1.0 + avgGain / avgLoss);
        }

        [Theory]
        [InlineData(20)]
        [InlineData(9)]
        [InlineData(50)]
        public void EmaMatchesTheLibraryTheChartDrawsFrom(int period)
        {
            var bars = Series(300);
            var expected = IndicatorMath.Ema(Closes(bars), period)[^1];

            Assert.Equal(expected, Scoped().GetEma(bars, period), precision: 9);
        }

        [Fact]
        public void EmaAgreesWithTheLibraryOnTheFIRSTBarItAnswersAtAll()
        {
            // The seed is the whole difference between the two EMA conventions, and its weight
            // decays as (1-k)^n — so a 300-bar fixture would pass with either seed and prove
            // nothing. At exactly `period` bars the seed is still most of the answer.
            const int period = 20;
            var bars = Series(period);
            double expected = IndicatorMath.Ema(Closes(bars), period)[^1];

            Assert.False(double.IsNaN(expected));
            Assert.Equal(expected, Scoped().GetEma(bars, period), precision: 9);
        }

        [Theory]
        [InlineData(20)]
        [InlineData(50)]
        public void SmaMatchesTheLibraryTheChartDrawsFrom(int period)
        {
            var bars = Series(300);
            var expected = IndicatorMath.Sma(Closes(bars), period)[^1];

            Assert.Equal(expected, Scoped().GetSma(bars, period), precision: 9);
        }

        [Fact]
        public void WarmupIsNaNRatherThanAWrongNumber()
        {
            var cache = Scoped();
            // A number computed from a window that does not exist yet is worse than no number:
            // the leaf evaluator skips NaN and a strategy stays flat, which is the honest answer.
            Assert.True(double.IsNaN(cache.GetRsi(Series(10), 14)));
            Assert.True(double.IsNaN(cache.GetEma(Series(10), 14)));
            Assert.True(double.IsNaN(cache.GetSma(Series(10), 14)));
        }

        [Fact]
        public void BollingerCentresOnTheSameSmaTheCacheReturns()
        {
            // No IndicatorMath equivalent to pin this against — but the middle band must at least
            // be the same SMA the cache would hand a strategy separately, or one series is drawn
            // around a centre the strategy never sees.
            var bars = Series(300);
            var cache = Scoped();

            var (middle, upper, lower) = cache.GetBollingerBands(bars, 20, 2.0);

            Assert.Equal(cache.GetSma(bars, 20), middle, precision: 9);
            Assert.True(upper > middle && middle > lower);
        }
    }
}
