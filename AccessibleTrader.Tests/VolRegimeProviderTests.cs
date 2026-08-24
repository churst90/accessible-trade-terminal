using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Math pins for VolRegimeProvider (2026-06-12, era-robustness research).
    /// The ratio component is the load-bearing claim: fast realized vol over slow
    /// realized vol must read >1 when recent vol is elevated vs its own baseline
    /// and &lt;1 when compressed — independent of the ABSOLUTE vol level, which is
    /// what makes the signal stationary across an asset's maturation.
    /// </summary>
    public class VolRegimeProviderTests
    {
        private static readonly Dictionary<string, object> SmallWindows = new()
        {
            ["ShortWindow"] = 10,
            ["LongWindow"] = 100,
            ["RankWindow"] = 100,
        };

        /// <summary>Random-walk closes with a fixed per-bar return magnitude (alternating sign).</summary>
        private static List<Ohlcv> AlternatingWalk(params (int bars, double retMagnitude)[] phases)
        {
            var data = new List<Ohlcv>();
            var t = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            double price = 100.0;
            int i = 0;
            foreach (var (bars, mag) in phases)
            {
                for (int k = 0; k < bars; k++, i++)
                {
                    price *= Math.Exp((i % 2 == 0 ? 1 : -1) * mag);
                    data.Add(new Ohlcv(t.AddDays(i), price, price * 1.001, price * 0.999, price, 1000));
                }
            }
            return data;
        }

        private static Dictionary<string, double[]> Run(List<Ohlcv> data, Dictionary<string, object>? p = null)
        {
            var results = new Dictionary<string, double[]>();
            var buffer = new IndicatorResultBuffer(results, data.Count);
            new VolRegimeProvider().Calculate(VolRegimeProvider.Code, data.ToArray(),
                p ?? new Dictionary<string, object>(SmallWindows), buffer);
            return results;
        }

        [Fact]
        public void WarmupBars_AreNaN_UntilLongWindowFills()
        {
            var results = Run(AlternatingWalk((200, 0.01)));
            var ratio = results[VolRegimeProvider.CompVolRatio];

            // LongWindow=100 plus the NaN first return → nothing before index 100.
            Assert.All(ratio.Take(100), v => Assert.True(double.IsNaN(v)));
            Assert.False(double.IsNaN(ratio[150]));
        }

        [Fact]
        public void ConstantVol_RatioNearOne_StateNeutral()
        {
            var results = Run(AlternatingWalk((300, 0.01)));
            var ratio = results[VolRegimeProvider.CompVolRatio];
            var state = results[VolRegimeProvider.CompVolState];

            Assert.Equal(1.0, ratio[^1], 2);
            Assert.Equal(0.0, state[^1]);
        }

        [Fact]
        public void VolSpike_PushesRatioAboveExpansionThreshold()
        {
            // 250 calm bars (1% moves) then 30 violent bars (5% moves): the short leg
            // jumps 5×, the long leg barely moves → ratio must clear 1.25 and flag +1.
            var results = Run(AlternatingWalk((250, 0.01), (30, 0.05)));

            Assert.True(results[VolRegimeProvider.CompVolRatio][^1] > 1.25);
            Assert.Equal(1.0, results[VolRegimeProvider.CompVolState][^1]);
            // And the spike sits at the very top of its own trailing distribution.
            Assert.True(results[VolRegimeProvider.CompVolPercentile][^1] > 0.9);
        }

        [Fact]
        public void VolCollapse_PushesRatioBelowCompressionThreshold()
        {
            var results = Run(AlternatingWalk((250, 0.05), (30, 0.01)));

            Assert.True(results[VolRegimeProvider.CompVolRatio][^1] < 0.75);
            Assert.Equal(-1.0, results[VolRegimeProvider.CompVolState][^1]);
        }

        [Fact]
        public void RatioIsScaleInvariant_AcrossAbsoluteVolLevels()
        {
            // The era-robustness property itself: the same RELATIVE spike (5× the
            // baseline) must produce the same ratio whether the era's baseline vol
            // is 0.4% per bar (mature asset) or 4% per bar (2017 era).
            var matureEra = Run(AlternatingWalk((250, 0.004), (30, 0.02)));
            var earlyEra  = Run(AlternatingWalk((250, 0.04),  (30, 0.20)));

            double matureRatio = matureEra[VolRegimeProvider.CompVolRatio][^1];
            double earlyRatio  = earlyEra[VolRegimeProvider.CompVolRatio][^1];

            Assert.Equal(matureRatio, earlyRatio, 1);
        }

        [Fact]
        public void EmptySeries_IsANoOp_NotAnIndexOutOfRange()
        {
            // Regression: r[0] = NaN ran before any length check, so an empty bar
            // series (a chart that failed to load, a symbol with no history yet)
            // threw IndexOutOfRangeException where every peer provider returns.
            var results = Run(new List<Ohlcv>());
            Assert.All(results.Values, arr => Assert.Empty(arr));
        }

        [Fact]
        public void StabilityWindow_TracksLongWindowParameter()
        {
            var p = new VolRegimeProvider();
            Assert.Equal(366, p.GetStabilityWindow(VolRegimeProvider.Code, new Dictionary<string, object>()));
            Assert.Equal(101, p.GetStabilityWindow(VolRegimeProvider.Code,
                new Dictionary<string, object> { ["LongWindow"] = 100 }));
        }
    }
}
