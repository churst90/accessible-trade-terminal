using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.StrategyLab;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The StrategyLab's <c>--cipherb old|new</c> switch (LabCipherProfile). The 2026-10-17 re-run of
    /// every Cipher-B-dependent lab result is an OLD-versus-NEW comparison on identical data; if the
    /// switch silently did nothing, every "delta" would be NEW minus NEW = 0 and would read as "the
    /// refit changed nothing". So the load-bearing test here goes through the engine the lab host
    /// actually resolves — the same path gate, confluence, asset-profile and the strategy workspace
    /// use — and requires (a) old and new to differ, and (b) old to equal Cipher B computed with the
    /// pre-2026-10-17 parameters spelled out by hand.
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

        private static Dictionary<string, double[]> ViaLabEngine(List<Ohlcv> bars, bool old, bool? goldOld = null)
        {
            LabCipherProfile.CipherBOld = old;
            LabCipherProfile.GoldOld = goldOld;
            try
            {
                var engine = LabHost.Build().Services.GetRequiredService<IIndicatorEngine>();
                return engine.CalculateAsync("CIPHER_B", bars, new Dictionary<string, object>(), default).Result;
            }
            finally { LabCipherProfile.Reset(); }
        }

        private static Dictionary<string, double[]> Direct(List<Ohlcv> bars, Dictionary<string, object> p)
        {
            var engine = LabHost.Build().Services.GetRequiredService<IndicatorEngine>();
            return engine.CalculateAsync("CIPHER_B", bars, p, default).Result;
        }

        private static readonly string[] Pinned =
        {
            CipherBProvider.CompWT2, CipherBProvider.CompMoneyFlowWave, CipherBProvider.CompBlue, CipherBProvider.CompGold,
        };

        private static bool Same(double[] a, double[] b) =>
            a.Length == b.Length && a.Zip(b).All(p => p.First.Equals(p.Second)); // NaN.Equals(NaN) is true

        [Fact]
        public void Old_IsThePreRefitCipherB_AndDiffersFromNew_ThroughTheLabEngine()
        {
            var bars = Bars();
            var oldRes = ViaLabEngine(bars, old: true);
            var newRes = ViaLabEngine(bars, old: false);
            var byHand = Direct(bars, new Dictionary<string, object>
            {
                ["WTSignalPeriod"] = 4.0, ["MoneyFlowMode"] = "Legacy", ["BuyDotMode"] = "Gated", ["GoldDotMode"] = "Confluence",
            });
            var defaults = Direct(bars, new Dictionary<string, object>());

            foreach (var c in Pinned)
            {
                Assert.True(Same(oldRes[c], byHand[c]), $"--cipherb old must equal the pre-refit parameters on '{c}'");
                Assert.True(Same(newRes[c], defaults[c]), $"--cipherb new must equal today's defaults on '{c}'");
            }
            // The property the re-run rests on: the switch changes the numbers. Wave and money flow
            // differ on every non-warmup bar; the buy dot fires on different bars.
            Assert.False(Same(oldRes[CipherBProvider.CompWT2], newRes[CipherBProvider.CompWT2]));
            Assert.False(Same(oldRes[CipherBProvider.CompMoneyFlowWave], newRes[CipherBProvider.CompMoneyFlowWave]));
            Assert.False(Same(oldRes[CipherBProvider.CompBlue], newRes[CipherBProvider.CompBlue]));
        }

        [Fact]
        public void GoldFlag_SwitchesTheGoldDotAlone()
        {
            var bars = Bars();
            var newAll = ViaLabEngine(bars, old: false);
            var newWithOldGold = ViaLabEngine(bars, old: false, goldOld: true);
            var oldGoldByHand = Direct(bars, new Dictionary<string, object> { ["GoldDotMode"] = "Confluence" });

            Assert.True(Same(newWithOldGold[CipherBProvider.CompGold], oldGoldByHand[CipherBProvider.CompGold]));
            Assert.True(Same(newWithOldGold[CipherBProvider.CompWT2], newAll[CipherBProvider.CompWT2]),
                "--cipherb-gold must not move the wave");
        }

        [Fact]
        public void CallerParameter_WinsOverTheProfile()
        {
            LabCipherProfile.CipherBOld = true;
            try
            {
                var p = LabCipherProfile.Apply("CIPHER_B", new Dictionary<string, object> { ["WTSignalPeriod"] = 6.0 });
                Assert.Equal(6.0, p["WTSignalPeriod"]);
                Assert.Equal("Legacy", p["MoneyFlowMode"]);
                Assert.Equal("Gated", p["BuyDotMode"]);
                Assert.Equal("Confluence", p["GoldDotMode"]);
                var other = new Dictionary<string, object>();
                Assert.Same(other, LabCipherProfile.Apply("CIPHER_A", other));
            }
            finally { LabCipherProfile.Reset(); }
        }

        [Fact]
        public void Consume_StripsTheFlags_AndRefusesATypo()
        {
            try
            {
                var rest = LabCipherProfile.Consume(new[] { "gate", "--cipherb", "old", "--tf", "1d", "--cipherb-gold", "new" });
                Assert.Equal(new[] { "gate", "--tf", "1d" }, rest);
                Assert.True(LabCipherProfile.CipherBOld);
                Assert.False(LabCipherProfile.GoldOld);
                Assert.Throws<ArgumentException>(() => LabCipherProfile.Consume(new[] { "--cipherb", "olde" }));
            }
            finally { LabCipherProfile.Reset(); }
        }
    }
}
