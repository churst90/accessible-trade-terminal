using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// SkenderCalculationCore's calculation semantics. IndicatorsThatRenderNothingTests
    /// already guards RESOLUTION (every offered code finds a method, every declared
    /// component is a property the result writes); this file guards what the numbers ARE:
    /// hand-checked values, the Nullable-parameter conversion fence (Convert.ChangeType
    /// throws on a Nullable target, which used to silently blank every optional smoothed
    /// line), the derived __SQUEEZE / __CROSSOVER components no catalog declares, the
    /// UpdateLast windowed recompute, and the quote pool not leaking state across calls.
    /// </summary>
    public class SkenderCalculationCoreTests
    {
        private static Ohlcv[] BarsFromCloses(params double[] closes)
            => BarsFromCloses((IReadOnlyList<double>)closes);

        private static Ohlcv[] BarsFromCloses(IReadOnlyList<double> closes)
        {
            var bars = new Ohlcv[closes.Count];
            var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < closes.Count; i++)
                bars[i] = new Ohlcv(t0.AddHours(i), closes[i], closes[i] + 0.1, closes[i] - 0.1, closes[i], 1000);
            return bars;
        }

        private static Dictionary<string, double[]> Run(string code, Ohlcv[] bars,
            params (string Name, object Value)[] parameters)
        {
            var dict = new Dictionary<string, double[]>();
            var buffer = new IndicatorResultBuffer(dict, bars.Length);
            SkenderCalculationCore.Calculate(code, bars,
                parameters.ToDictionary(p => p.Name, p => p.Value), buffer);
            return dict;
        }

        // ── The numbers themselves ──────────────────────────────────────────────

        [Fact]
        public void Sma_MatchesTheHandComputedAverage()
        {
            var bars = BarsFromCloses(1, 2, 3, 4, 5, 6, 7, 8, 9, 10);

            var result = Run("Sma", bars, ("lookbackPeriods", 3));

            var sma = result["Sma"];
            Assert.True(double.IsNaN(sma[0]));
            Assert.True(double.IsNaN(sma[1]));
            Assert.Equal(2.0, sma[2], 10);   // (1+2+3)/3
            Assert.Equal(9.0, sma[9], 10);   // (8+9+10)/3
        }

        [Fact]
        public void WarmupBars_AreNaN_NotZero()
        {
            // A zero in the warm-up region would sonify and speak as a real price of 0.
            var bars = BarsFromCloses(Enumerable.Range(1, 30).Select(i => (double)i).ToArray());

            var sma = Run("Sma", bars, ("lookbackPeriods", 14))["Sma"];

            for (int i = 0; i < 13; i++)
                Assert.True(double.IsNaN(sma[i]), $"warm-up index {i} should be NaN, was {sma[i]}");
            Assert.False(double.IsNaN(sma[13]));
        }

        // ── Parameter conversion ────────────────────────────────────────────────

        [Fact]
        public void NullableOptionalParameter_PassedAsDouble_StillReachesTheCalculation()
        {
            // The regression fence for the 2026-08-22 patch: Skender's optional parameters
            // are Nullable<T>, Convert.ChangeType throws on a Nullable target, and the catch
            // fell back to null — so the smoothed line the user had configured was computed
            // as all-null and rendered empty. The UI hands parameters over as doubles, which
            // is exactly the shape that broke.
            var bars = BarsFromCloses(Enumerable.Range(1, 60).Select(i => 100 + Math.Sin(i / 5.0) * 3).ToArray());

            var result = Run("Adl", bars, ("smaPeriods", (object)3.0));

            Assert.True(result.ContainsKey("AdlSma"), "AdlSma span was never created");
            Assert.Contains(result["AdlSma"], v => !double.IsNaN(v));
        }

        [Fact]
        public void UnconvertibleParameterValue_FallsBackToTheMethodDefault()
        {
            var bars = BarsFromCloses(Enumerable.Range(1, 80).Select(i => 100 + Math.Sin(i / 5.0) * 3).ToArray());

            var withGarbage = Run("Macd", bars, ("fastPeriods", "not a number"));
            var withDefaults = Run("Macd", bars);

            Assert.Equal(withDefaults["Macd"], withGarbage["Macd"]);
        }

        [Fact]
        public void MissingParameters_UseTheMethodDefaults()
        {
            var bars = BarsFromCloses(Enumerable.Range(1, 80).Select(i => 100 + Math.Sin(i / 5.0) * 3).ToArray());

            var implicitRun = Run("Macd", bars);
            var explicitRun = Run("Macd", bars,
                ("fastPeriods", 12), ("slowPeriods", 26), ("signalPeriods", 9));

            Assert.Equal(explicitRun["Macd"], implicitRun["Macd"]);
            Assert.Equal(explicitRun["Signal"], implicitRun["Signal"]);
        }

        [Fact]
        public void UnknownCode_IsASilentNoOp()
        {
            var bars = BarsFromCloses(1, 2, 3, 4, 5);
            var result = Run("DefinitelyNotAnIndicator", bars);
            Assert.Empty(result);
        }

        // ── Derived components (no catalog declares them, so no other sweep sees them) ──

        private static Ohlcv[] VolatilityShiftBars(bool calmFirst)
        {
            var closes = new double[100];
            for (int i = 0; i < 100; i++)
            {
                double amp = (i < 60) == calmFirst ? 0.2 : 3.0;
                closes[i] = 100 + (i % 2 == 0 ? amp : -amp);
            }
            return BarsFromCloses(closes);
        }

        [Fact]
        public void BollingerBands_SqueezeComponent_FlagsExpansionAndSqueeze()
        {
            var expanding = Run("BB", VolatilityShiftBars(calmFirst: true),
                ("lookbackPeriods", 20), ("standardDeviations", 2.0));
            var squeezing = Run("BB", VolatilityShiftBars(calmFirst: false),
                ("lookbackPeriods", 20), ("standardDeviations", 2.0));

            var expSqueeze = expanding["__SQUEEZE"];
            var sqSqueeze = squeezing["__SQUEEZE"];

            // First 20 bars are always neutral (no width history yet).
            Assert.All(expSqueeze.Take(20), v => Assert.Equal(0, v));
            // Calm → volatile: the band width blows out past 1.5× its trailing average.
            Assert.Contains(2.0, expSqueeze.Skip(60));
            // Volatile → calm: the width drops under 0.6× its trailing average.
            Assert.Contains(1.0, sqSqueeze.Skip(60));
        }

        private static Ohlcv[] TrendReversalBars(bool downFirst)
        {
            var closes = new double[120];
            for (int i = 0; i < 120; i++)
            {
                double slope = (i < 60) == downFirst ? -0.5 : 0.5;
                closes[i] = 100 + (i < 60 ? slope * i : slope * (i - 60) + (downFirst ? -30 : 30));
            }
            return BarsFromCloses(closes);
        }

        [Fact]
        public void Macd_CrossoverComponent_FlagsBullishAndBearishCrosses()
        {
            var vShape = Run("Macd", TrendReversalBars(downFirst: true));
            var aShape = Run("Macd", TrendReversalBars(downFirst: false));

            // Down-then-up produces a MACD line crossing UP through its signal (1);
            // up-then-down produces the bearish cross (2).
            Assert.Contains(1.0, vShape["__CROSSOVER"].Skip(60));
            Assert.Contains(2.0, aShape["__CROSSOVER"].Skip(60));
        }

        // ── UpdateLast: the incremental path must agree with the full recompute ──

        [Fact]
        public void UpdateLast_WritesTheSameLastValue_AsAFullRecalculation()
        {
            var bars = BarsFromCloses(Enumerable.Range(1, 120).Select(i => 100 + Math.Sin(i / 9.0) * 5).ToArray());
            var parameters = new Dictionary<string, object> { ["lookbackPeriods"] = 14 };

            var full = new Dictionary<string, double[]>();
            SkenderCalculationCore.Calculate("Sma", bars, parameters, new IndicatorResultBuffer(full, bars.Length));

            var incremental = new Dictionary<string, double[]>();
            SkenderCalculationCore.UpdateLast("Sma", bars, parameters, new IndicatorResultBuffer(incremental, bars.Length));

            Assert.Equal(full["Sma"][^1], incremental["Sma"][^1], 10);
            // And it is genuinely incremental — only the last slot is written.
            Assert.Equal(0, incremental["Sma"][^2]);
        }

        [Fact]
        public void UpdateLast_OnDataShorterThanTheStabilityWindow_StillWorks()
        {
            var bars = BarsFromCloses(Enumerable.Range(1, 20).Select(i => (double)i).ToArray());
            var parameters = new Dictionary<string, object> { ["lookbackPeriods"] = 3 };

            var incremental = new Dictionary<string, double[]>();
            SkenderCalculationCore.UpdateLast("Sma", bars, parameters, new IndicatorResultBuffer(incremental, bars.Length));

            Assert.Equal(19.0, incremental["Sma"][^1], 10); // (18+19+20)/3
        }

        // ── GetStabilityWindow ──────────────────────────────────────────────────

        [Theory]
        [InlineData(null, null, 35)]              // 14 default × 2.5
        [InlineData("lookbackPeriods", 30, 75)]
        [InlineData("lookbackPeriods", 100, 200)] // capped at 200
        [InlineData("smaPeriods", 20, 50)]        // any *Period* key counts
        [InlineData("lookbackPeriods", 4, 35)]    // never below the 14-period floor
        [InlineData("standardDeviations", 50, 35)]// non-period parameters are ignored
        public void StabilityWindow_ScalesWithTheLargestPeriodParameter(
            string? key, int? value, int expected)
        {
            var parameters = new Dictionary<string, object>();
            if (key != null) parameters[key] = value!;

            Assert.Equal(expected, SkenderCalculationCore.GetStabilityWindow("ANY", parameters));
        }

        [Fact]
        public void StabilityWindow_ParsesStringValuedPeriods()
        {
            var parameters = new Dictionary<string, object> { ["lookbackPeriods"] = "30" };
            Assert.Equal(75, SkenderCalculationCore.GetStabilityWindow("ANY", parameters));
        }

        // ── The quote pool must not leak state between calls ────────────────────

        [Fact]
        public void SequentialCalculations_DoNotContaminateEachOther()
        {
            // Quotes are pooled and reused; a stale field surviving Reset-by-assignment
            // would silently shift every value of the NEXT indicator run.
            var first = Run("Sma", BarsFromCloses(1, 2, 3, 4, 5, 6, 7, 8, 9, 10), ("lookbackPeriods", 3));
            var second = Run("Sma", BarsFromCloses(20, 30, 40, 50, 60, 70, 80, 90, 100, 110), ("lookbackPeriods", 3));

            Assert.Equal(9.0, first["Sma"][^1], 10);
            Assert.Equal(100.0, second["Sma"][^1], 10); // (90+100+110)/3
        }
    }
}
