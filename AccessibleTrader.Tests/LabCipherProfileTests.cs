using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.StrategyLab;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The StrategyLab's Cipher switches (LabCipherProfile). <c>--cipher-candles ha</c> must compute each
    /// Cipher as a Heikin-Ashi chart does, through the engine the lab host actually resolves — the same
    /// path gate, confluence, asset-profile and the strategy workspace use — or the "Heikin-Ashi" run
    /// is the candle run again. <c>--cipherb old</c> (the pre-2026-10-17 Cipher B) and
    /// <c>--cipherb-gold</c> are refused: the old modes went on 2026-10-31 and the gold dot on
    /// 2026-10-22, and a run asked to compare them would compare today's Cipher B with itself.
    /// </summary>
    [Collection(nameof(LabCipherProfileTests))]
    [CollectionDefinition(nameof(LabCipherProfileTests), DisableParallelization = true)]
    public class LabCipherProfileTests
    {
        private static List<Ohlcv> Bars(int n = 700, int seed = 7)
        {
            var rng = new Random(seed);
            var bars = new List<Ohlcv>(n);
            var t0 = new DateTime(2020, 1, 1);
            double px = 100;
            for (int i = 0; i < n; i++)
            {
                // A random walk with slow cycles, so the wave reaches ±60 and crosses often.
                double next = px * Math.Exp(0.03 * Math.Sin(i / 9.0) + 0.02 * (rng.NextDouble() - 0.5));
                double hi = Math.Max(px, next) * (1 + 0.01 * rng.NextDouble());
                double lo = Math.Min(px, next) * (1 - 0.01 * rng.NextDouble());
                bars.Add(new Ohlcv(t0.AddDays(i), px, hi, lo, next, 1000 + rng.Next(1000)));
                px = next;
            }
            return bars;
        }

        private static bool Same(double[] a, double[] b) =>
            a.Length == b.Length && a.Zip(b).All(p => p.First.Equals(p.Second)); // NaN.Equals(NaN) is true

        [Fact]
        public void OldFlag_IsRefused_BecauseThereIsNoOldCipherB()
        {
            // Cipher B's old modes (MoneyFlowMode Legacy, BuyDotMode Gated) were removed on 2026-10-31.
            // A run that asks for the old indicator must stop and say so, not compare today's Cipher B
            // with itself and report "no difference".
            try
            {
                var ex = Assert.Throws<ArgumentException>(() => LabCipherProfile.Consume(new[] { "gate", "--cipherb", "old" }));
                Assert.Contains("removed", ex.Message);
                Assert.True(LabCipherProfile.IsDefault);
                Assert.DoesNotContain("MoneyFlowMode", LabCipherProfile.Apply("CIPHER_B", new Dictionary<string, object>()).Keys);
                Assert.DoesNotContain("BuyDotMode", LabCipherProfile.Apply("CIPHER_B", new Dictionary<string, object>()).Keys);
            }
            finally { LabCipherProfile.Reset(); }
        }

        [Fact]
        public void GoldFlag_IsRefused_BecauseThereIsNoGoldDot()
        {
            // Cipher B's gold dot was removed on 2026-10-22. A run that still asks to switch it must stop
            // and say so, not compare two identical runs and report "no difference".
            try
            {
                var ex = Assert.Throws<ArgumentException>(() => LabCipherProfile.Consume(new[] { "gate", "--cipherb-gold", "old" }));
                Assert.Contains("removed", ex.Message);
                var res = Calc("CIPHER_B", Bars(), new Dictionary<string, object>(), viaLab: true);
                Assert.DoesNotContain("Triple Confluence Buy", res.Keys);
                Assert.DoesNotContain("GoldDotMode", LabCipherProfile.Apply("CIPHER_B", new Dictionary<string, object>()).Keys);
            }
            finally { LabCipherProfile.Reset(); }
        }

        private static Dictionary<string, double[]> Calc(string code, List<Ohlcv> bars, Dictionary<string, object> p, bool viaLab)
        {
            var sp = LabHost.Build().Services;
            IIndicatorEngine engine = viaLab ? sp.GetRequiredService<IIndicatorEngine>() : sp.GetRequiredService<IndicatorEngine>();
            return engine.CalculateAsync(code, bars, p, default).Result;
        }

        private static bool SameResults(Dictionary<string, double[]> a, Dictionary<string, double[]> b) =>
            a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var o) && Same(kv.Value, o));

        [Fact]
        public void CipherCandlesHa_ComputesEachCipherAsOnAHeikinAshiChart_ThroughTheLabEngine()
        {
            // The exploratory re-run (docs/EXACT_CIPHERS_RERUN_PREREG.md section 6) compares the Ciphers
            // on candles and on Heikin-Ashi with everything else fixed. If the switch did nothing, the
            // "Heikin-Ashi" run would be the candle run again. So: through the engine the lab host
            // resolves, each Cipher must equal the same Cipher given the Heikin-Ashi chart parameter by
            // hand, and must differ from the candle Cipher.
            var bars = Bars();
            var haByHand = new Dictionary<string, object> { [IndicatorCandles.HeikinAshiKey] = 1.0 };
            foreach (var code in new[] { "CIPHER_A", "CIPHER_B", "CIPHER_SR" })
            {
                Dictionary<string, double[]> lab;
                LabCipherProfile.CiphersOnHeikinAshi = true;
                try { lab = Calc(code, bars, new Dictionary<string, object>(), viaLab: true); }
                finally { LabCipherProfile.Reset(); }
                var ha = Calc(code, bars, new Dictionary<string, object>(haByHand), viaLab: false);
                var candles = Calc(code, bars, new Dictionary<string, object>(), viaLab: false);
                Assert.True(SameResults(lab, ha), $"--cipher-candles ha must compute {code} as a Heikin-Ashi chart does");
                Assert.False(SameResults(lab, candles), $"--cipher-candles ha must change {code}");
            }
        }

        [Fact]
        public void CipherCandlesHa_LeavesOtherIndicatorsAndTheCallersSettingAlone()
        {
            LabCipherProfile.CiphersOnHeikinAshi = true;
            try
            {
                Assert.False(LabCipherProfile.IsDefault);
                var p = new Dictionary<string, object>();
                Assert.Same(p, LabCipherProfile.Apply("ATR", p));
                Assert.Equal(1.0, LabCipherProfile.Apply("CIPHER_SR", p)[IndicatorCandles.HeikinAshiKey]);
                Assert.Empty(p);
                var caller = new Dictionary<string, object> { [IndicatorCandles.HeikinAshiKey] = 0.0 };
                Assert.Equal(0.0, LabCipherProfile.Apply("CIPHER_B", caller)[IndicatorCandles.HeikinAshiKey]);
                Assert.Contains("HEIKIN-ASHI", LabCipherProfile.Describe());
            }
            finally { LabCipherProfile.Reset(); }
            Assert.True(LabCipherProfile.IsDefault);
            Assert.DoesNotContain("HEIKIN-ASHI", LabCipherProfile.Describe());
        }

        [Fact]
        public void Consume_StripsTheFlags_AndRefusesATypo()
        {
            try
            {
                var rest = LabCipherProfile.Consume(new[] { "gate", "--cipherb", "new", "--tf", "1d" });
                Assert.Equal(new[] { "gate", "--tf", "1d" }, rest);
                Assert.True(LabCipherProfile.IsDefault);
                Assert.Throws<ArgumentException>(() => LabCipherProfile.Consume(new[] { "--cipherb", "olde" }));
                Assert.Throws<ArgumentException>(() => LabCipherProfile.Consume(new[] { "--cipherb" }));
                rest = LabCipherProfile.Consume(new[] { "cipher-a-battery", "--cipher-candles", "ha", "--snapshots", "x" });
                Assert.Equal(new[] { "cipher-a-battery", "--snapshots", "x" }, rest);
                Assert.True(LabCipherProfile.CiphersOnHeikinAshi);
                Assert.Throws<ArgumentException>(() => LabCipherProfile.Consume(new[] { "--cipher-candles", "heikin" }));
                Assert.Throws<ArgumentException>(() => LabCipherProfile.Consume(new[] { "--cipher-candles" }));
            }
            finally { LabCipherProfile.Reset(); }
        }
    }
}
