using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Coverage for <see cref="ValueDeviationAnalyzer"/> and <see cref="ValueDeviationProvider"/>.
    ///
    /// The invariants worth defending: the profile must be CAUSAL (a POC computed from future
    /// bars would make every tier a lie), tiers must deepen with distance, a collapsed value area
    /// must not manufacture an extreme reading, and the marks must land on reversal bars only.
    /// </summary>
    public class ValueDeviationTests
    {
        private static readonly DateTime Start = new(2020, 1, 1);

        private static List<Ohlcv> Flat(int count, double price, double vol = 1000)
        {
            var bars = new List<Ohlcv>(count);
            for (int i = 0; i < count; i++)
                bars.Add(new Ohlcv(Start.AddDays(i), price, price + 0.5, price - 0.5, price, vol));
            return bars;
        }

        // ── Causality ─────────────────────────────────────────────────────────

        [Fact]
        public void ProfileIsCausal_FutureBarsCannotChangeAnEarlierPoc()
        {
            // Build a series, take the reference, then append a violent move and take it again.
            // Every value at an index that existed before must be unchanged.
            var bars = Flat(400, 100);
            var analyzer = new ValueDeviationAnalyzer();
            var before = analyzer.Reference(bars, 120).Poc;

            var extended = new List<Ohlcv>(bars);
            for (int i = 0; i < 120; i++)
                extended.Add(new Ohlcv(Start.AddDays(400 + i), 500, 505, 495, 500, 100000));

            var after = analyzer.Reference(extended, 120).Poc;

            for (int i = 0; i < bars.Count; i++)
            {
                if (double.IsNaN(before[i]) && double.IsNaN(after[i])) continue;
                Assert.Equal(before[i], after[i], 6);
            }
        }

        [Fact]
        public void BarsBeforeTheWindowFills_HaveNoReading()
        {
            var bars = Flat(300, 100);
            var devs = new ValueDeviationAnalyzer().Analyze(bars, 120, 5, 2.0);

            for (int i = 0; i < 120; i++)
                Assert.Equal(0, devs[i].Tier);
        }

        // ── Tier behaviour ────────────────────────────────────────────────────

        [Fact]
        public void DeeperDeviationProducesADeeperTier()
        {
            // 300 bars of two-sided business around 100 to give the profile a real value area,
            // then a drop that steps progressively further below it.
            var bars = new List<Ohlcv>();
            var rng = new Random(7);
            for (int i = 0; i < 300; i++)
            {
                double p = 100 + (rng.NextDouble() - 0.5) * 8;
                bars.Add(new Ohlcv(Start.AddDays(i), p, p + 1, p - 1, p, 1000));
            }
            int drop = bars.Count;
            foreach (double p in new[] { 96.0, 92.0, 86.0, 78.0 })
                for (int k = 0; k < 6; k++)
                    bars.Add(new Ohlcv(Start.AddDays(bars.Count), p, p + 0.5, p - 0.5, p, 50));

            var devs = new ValueDeviationAnalyzer().Analyze(bars, 250, 5, 2.0);

            var tiers = new List<int>();
            for (int leg = 0; leg < 4; leg++)
            {
                int idx = drop + leg * 6 + 5;
                if (idx < devs.Length && devs[idx].Tier > 0) tiers.Add(devs[idx].Tier);
            }

            Assert.True(tiers.Count >= 3, "expected readings on most legs");
            for (int i = 1; i < tiers.Count; i++)
                Assert.True(tiers[i] >= tiers[i - 1],
                    $"tier must not shrink as price falls further: {string.Join(",", tiers)}");
        }

        [Fact]
        public void PriceInsideValue_ReportsTierZero()
        {
            var bars = Flat(400, 100);
            var devs = new ValueDeviationAnalyzer().Analyze(bars, 120, 5, 2.0);
            Assert.Equal(0, devs[^1].Tier);
        }

        [Fact]
        public void BelowValueIsFlaggedAsSuch()
        {
            var bars = new List<Ohlcv>();
            var rng = new Random(11);
            for (int i = 0; i < 300; i++)
            {
                double p = 100 + (rng.NextDouble() - 0.5) * 8;
                bars.Add(new Ohlcv(Start.AddDays(i), p, p + 1, p - 1, p, 1000));
            }
            for (int i = 0; i < 20; i++)
                bars.Add(new Ohlcv(Start.AddDays(bars.Count), 80, 80.5, 79.5, 80, 50));

            var devs = new ValueDeviationAnalyzer().Analyze(bars, 250, 5, 2.0);
            var last = devs[^1];
            Assert.True(last.Tier > 0);
            Assert.True(last.BelowValue);
            Assert.True(last.DeviationVa < 0);
        }

        [Fact]
        public void ZeroVolumeSeries_ProducesNoReadingRatherThanGarbage()
        {
            var bars = Flat(400, 100, vol: 0);
            var devs = new ValueDeviationAnalyzer().Analyze(bars, 120, 5, 2.0);
            Assert.All(devs, d => Assert.Equal(0, d.Tier));
        }

        [Fact]
        public void TierNeverExceedsTheConfiguredCount()
        {
            var bars = new List<Ohlcv>();
            var rng = new Random(3);
            for (int i = 0; i < 300; i++)
            {
                double p = 100 + (rng.NextDouble() - 0.5) * 8;
                bars.Add(new Ohlcv(Start.AddDays(i), p, p + 1, p - 1, p, 1000));
            }
            for (int i = 0; i < 20; i++)
                bars.Add(new Ohlcv(Start.AddDays(bars.Count), 40, 40.5, 39.5, 40, 10));

            var devs = new ValueDeviationAnalyzer().Analyze(bars, 250, 5, 2.0);
            Assert.All(devs, d => Assert.InRange(d.Tier, 0, 5));
        }

        // ── Provider surface ──────────────────────────────────────────────────

        private sealed class Buf : AccessibleTrader.Sdk.Interfaces.IIndicatorResultBuffer
        {
            public readonly Dictionary<string, double[]> Data = new();
            private readonly int _n;
            public Buf(int n) => _n = n;
            public Span<double> GetComponentSpan(string name)
            {
                if (!Data.TryGetValue(name, out var a)) { a = new double[_n]; Data[name] = a; }
                return a;
            }
            public void SetValue(string name, int i, double v) => GetComponentSpan(name)[i] = v;
            public void WriteZoneBands(string code, List<ZoneBandConfig> z) { }
            public IReadOnlyList<ZoneBandConfig> ReadZoneBands(string code) => Array.Empty<ZoneBandConfig>();
        }

        private static (Buf Buffer, List<Ohlcv> Bars) RunProvider(bool requireMomentum = false)
        {
            var bars = new List<Ohlcv>();
            var rng = new Random(23);
            for (int i = 0; i < 400; i++)
            {
                double p = 100 + (rng.NextDouble() - 0.5) * 10;
                bars.Add(new Ohlcv(Start.AddDays(i), p, p + 1.2, p - 1.2, p, 1000));
            }
            // A sharp flush well below value, ending in a clean bullish reversal bar.
            for (int i = 0; i < 14; i++)
                bars.Add(new Ohlcv(Start.AddDays(bars.Count), 84 - i * 0.6, 84.4 - i * 0.6, 83.2 - i * 0.6, 83.4 - i * 0.6, 400));
            var prev = bars[^1];
            bars.Add(new Ohlcv(Start.AddDays(bars.Count), prev.Close, prev.Close + 2.5, prev.Low - 1.5, prev.Close + 2.0, 900));

            var buf = new Buf(bars.Count);
            new ValueDeviationProvider().Calculate(ValueDeviationProvider.Code, bars.ToArray(),
                new Dictionary<string, object>
                {
                    ["Window"] = 300, ["Tiers"] = 5, ["MaxTierVa"] = 2.0,
                    ["RequireMomentumTurn"] = requireMomentum ? 1 : 0,
                }, buf);
            return (buf, bars);
        }

        [Fact]
        public void Provider_PrintsABuyMarkOnTheReversalBarBelowValue()
        {
            var (buf, bars) = RunProvider();
            int last = bars.Count - 1;

            double shallow = buf.Data[ValueDeviationProvider.CompBuyShallow][last];
            double mid = buf.Data[ValueDeviationProvider.CompBuyMid][last];
            double deep = buf.Data[ValueDeviationProvider.CompBuyDeep][last];

            Assert.True(!double.IsNaN(shallow) || !double.IsNaN(mid) || !double.IsNaN(deep),
                "expected a buy mark on the reversal bar");
        }

        [Fact]
        public void Provider_PlacesBuyMarksBelowTheBarSoTheyDoNotObscureIt()
        {
            var (buf, bars) = RunProvider();
            for (int i = 0; i < bars.Count; i++)
            {
                foreach (var key in new[] { ValueDeviationProvider.CompBuyShallow,
                                            ValueDeviationProvider.CompBuyMid,
                                            ValueDeviationProvider.CompBuyDeep })
                {
                    double v = buf.Data[key][i];
                    if (!double.IsNaN(v)) Assert.True(v < bars[i].Low, $"{key} at {i} was not below the bar");
                }
            }
        }

        [Fact]
        public void Provider_NeverMarksABarThatIsNotAReversal()
        {
            var (buf, bars) = RunProvider();
            for (int i = 1; i < bars.Count; i++)
            {
                bool marked = new[] { ValueDeviationProvider.CompBuyShallow,
                                      ValueDeviationProvider.CompBuyMid,
                                      ValueDeviationProvider.CompBuyDeep }
                    .Any(k => !double.IsNaN(buf.Data[k][i]));
                if (!marked) continue;

                double range = bars[i].High - bars[i].Low;
                Assert.True(bars[i].Low < bars[i - 1].Low, $"bar {i} did not undercut the prior low");
                Assert.True(bars[i].Close > bars[i].Open, $"bar {i} did not close up");
                Assert.True((bars[i].Close - bars[i].Low) / range > 0.5, $"bar {i} did not close strong");
            }
        }

        [Fact]
        public void Provider_ExposesThePocAndValueBounds()
        {
            var (buf, bars) = RunProvider();
            Assert.False(double.IsNaN(buf.Data[ValueDeviationProvider.CompPoc][^1]));
            Assert.True(buf.Data[ValueDeviationProvider.CompValueHigh][^1]
                        > buf.Data[ValueDeviationProvider.CompValueLow][^1]);
        }

        [Fact]
        public void Provider_DetailFactRefusesToPromiseAnything()
        {
            var (buf, bars) = RunProvider();
            string fact = new ValueDeviationProvider().GetDetailFact(ValueDeviationProvider.Code,
                bars.ToArray(), buf.Data, bars.Count - 1, new Dictionary<string, object>());

            Assert.Contains("value", fact, StringComparison.OrdinalIgnoreCase);
            // It reports what was measured historically, never a forecast.
            Assert.DoesNotContain("will ", fact, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Provider_InvertedModeSaysSoInSpeech()
        {
            var (buf, bars) = RunProvider();
            string fact = new ValueDeviationProvider().GetDetailFact(ValueDeviationProvider.Code,
                bars.ToArray(), buf.Data, bars.Count - 1,
                new Dictionary<string, object> { ["InvertForMomentum"] = 1 });

            Assert.Contains("Inverted", fact, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Provider_TrimSpeechFramesItAsScalingOutNotShorting()
        {
            string? speech = new ValueDeviationProvider().GetComponentSpeech(
                "Trim tier 4-5", 100, new Ohlcv(Start, 1, 1, 1, 1, 1),
                new Dictionary<string, double[]>(), 0);

            Assert.NotNull(speech);
            Assert.Contains("Trim", speech, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("short", speech!, StringComparison.OrdinalIgnoreCase);
        }
    }
}
