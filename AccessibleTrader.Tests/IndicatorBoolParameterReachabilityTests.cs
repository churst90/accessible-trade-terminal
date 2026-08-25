using System.Text.RegularExpressions;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The second half of the bug <see cref="BoolIndicatorParameterTests"/> was written for.
    ///
    /// <para>
    /// That file proved the value survives the trip <i>into</i> the parameter dictionary:
    /// <c>IndicatorModelFactory.TryParseParamValue</c> turns "true" into <c>1.0</c> because
    /// <c>SeriesConfig.Parameters</c> is typed <c>double</c>. It never checked the other end. Three
    /// providers read that parameter with a private helper that handled <c>bool</c> and
    /// <c>string</c> and nothing else, so a <c>double</c> — the only type it can ever be given —
    /// fell straight through to the hardcoded default. Cipher SR's Adaptive Break could not be
    /// turned off, Cipher S's Adaptive Smoothing could not be turned on, and Spider Lines' Fast
    /// Mode did nothing: three checkboxes the dialog rendered as working knobs.
    /// </para>
    ///
    /// <para>
    /// So these tests assert on what the provider <b>observed</b>, by flipping the switch and
    /// requiring the output to move. A test that only asked "does the helper parse a double" would
    /// have stayed green through the original bug — the helper it asked was the one that already
    /// worked.
    /// </para>
    /// </summary>
    public class IndicatorBoolParameterReachabilityTests
    {
        private static readonly DateTime Start = new(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private sealed class Buf : IIndicatorResultBuffer
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

        /// <summary>A series with enough movement that a smoothing/threshold change has something to bite on.</summary>
        private static Ohlcv[] Series(int count, int seed)
        {
            var rng = new Random(seed);
            var bars = new List<Ohlcv>(count);
            double p = 100;
            for (int i = 0; i < count; i++)
            {
                p += (rng.NextDouble() - 0.5) * 6.0;
                if (i % 97 == 0) p += 12;           // occasional impulse, so breaks and pivots exist
                double hi = p + 1.5, lo = p - 1.5;
                // Volume has to VARY. Cipher SR's pivot detection requires a bar to clear 1.2×
                // the trailing average, so a smoothly rising volume series produces no pivots at
                // all, no levels, and two runs of all-NaN that agree perfectly — a fixture that
                // would report "the flag was ignored" for a provider that read it correctly.
                double vol = 1000 * (0.6 + rng.NextDouble());
                if (i % 13 == 0) vol *= 3;
                bars.Add(new Ohlcv(Start.AddHours(i), p, hi, lo, p, vol));
            }
            return bars.ToArray();
        }

        /// <summary>
        /// Runs a provider twice — the switch off, then on — the way the app supplies it: as the
        /// <c>double</c> that <c>IndicatorModelFactory.TryParseParamValue</c> produces from the
        /// word the UI wrote, not as a <c>bool</c> the provider would have handled all along.
        /// </summary>
        private static (Dictionary<string, double[]> Off, Dictionary<string, double[]> On) RunBothWays(
            IIndicatorProvider provider, string code, string param, Ohlcv[] bars,
            Dictionary<string, object>? others = null)
        {
            Assert.True(IndicatorModelFactory.TryParseParamValue("false", out double offValue));
            Assert.True(IndicatorModelFactory.TryParseParamValue("true", out double onValue));

            Dictionary<string, double[]> Run(double flag)
            {
                var p = others == null
                    ? new Dictionary<string, object>()
                    : new Dictionary<string, object>(others);
                p[param] = flag;                    // a double, exactly as the series config carries it
                var buf = new Buf(bars.Length);
                provider.Calculate(code, bars, p, buf);
                return buf.Data;
            }

            return (Run(offValue), Run(onValue));
        }

        private static int DifferingValues(Dictionary<string, double[]> a, Dictionary<string, double[]> b)
        {
            int diff = 0;
            foreach (var (name, left) in a)
            {
                if (!b.TryGetValue(name, out var right)) { diff++; continue; }
                for (int i = 0; i < Math.Min(left.Length, right.Length); i++)
                {
                    bool ln = double.IsNaN(left[i]), rn = double.IsNaN(right[i]);
                    if (ln && rn) continue;
                    if (ln != rn || Math.Abs(left[i] - right[i]) > 1e-9) diff++;
                }
            }
            return diff;
        }

        [Fact]
        public void CipherSrAdaptiveBreakCanBeTurnedOff()
        {
            var bars = Series(400, seed: 11);
            var (off, on) = RunBothWays(new CipherSrProvider(), "CIPHER_SR", "AdaptiveBreak", bars);

            Assert.True(DifferingValues(off, on) > 0,
                "AdaptiveBreak made no difference to any component — the provider never saw the flag");
        }

        [Fact]
        public void CipherSAdaptiveSmoothingCanBeTurnedOn()
        {
            var bars = Series(400, seed: 12);
            var (off, on) = RunBothWays(new CipherSProvider(), "CIPHER_S", "AdaptiveSmoothing", bars);

            Assert.True(DifferingValues(off, on) > 0,
                "AdaptiveSmoothing made no difference to any component — the provider never saw the flag");
        }

        [Fact]
        public void SpiderLinesFastModeCanBeTurnedOn()
        {
            var bars = Series(500, seed: 13);
            var (off, on) = RunBothWays(new SpiderLinesProvider(), "SPIDER_LINES", "FastMode", bars);

            // Fast Mode swaps every line from EMA to HMA. If the flag were dropped, both runs
            // would be EMA and identical to the last decimal.
            Assert.True(DifferingValues(off, on) > 0,
                "FastMode made no difference — every line is still an EMA");
        }

        [Theory]
        [InlineData(1.0, true)]
        [InlineData(0.0, false)]
        [InlineData(true, true)]
        [InlineData(false, false)]
        [InlineData("true", true)]
        [InlineData("False", false)]
        [InlineData("1", true)]
        [InlineData("0", false)]
        [InlineData(1, true)]
        [InlineData(0L, false)]
        public void TheSharedReaderUnderstandsEveryShapeTheValueArrivesIn(object raw, bool expected)
        {
            var p = new Dictionary<string, object> { ["Flag"] = raw };
            Assert.Equal(expected, IndicatorParams.GetBool(p, "Flag", !expected));
        }

        [Fact]
        public void TheSharedReaderFallsBackToTheDefaultRatherThanGuessing()
        {
            var p = new Dictionary<string, object> { ["Junk"] = "sometimes" };

            Assert.True(IndicatorParams.GetBool(p, "Missing", true));
            Assert.False(IndicatorParams.GetBool(p, "Missing", false));
            // Unreadable text must not collapse to false: a switch that defaults ON has to stay
            // ON when the stored value is unreadable, or a saved workspace silently disarms it.
            Assert.True(IndicatorParams.GetBool(p, "Junk", true));
            Assert.True(IndicatorParams.GetBool(null, "Junk", true));
        }

        [Fact]
        public void TheSharedReaderIsNotAtTheMercyOfTheAmbientCulture()
        {
            // Workspaces persist parameters as JSON; a comma-decimal locale must not change what
            // a switch means. Same defect class as the price-format sweep.
            var previous = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                var p = new Dictionary<string, object> { ["Flag"] = "1.0" };
                Assert.True(IndicatorParams.GetBool(p, "Flag", false));
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void NoIndicatorProviderKeepsItsOwnBooleanReader()
        {
            // The class, not the three instances. Six providers had four different private
            // GetBool implementations and three of them were wrong; a seventh copy would be the
            // same bug again. The numeric accessors are deliberately NOT covered here — they
            // disagree about rounding and culture, and unifying them moves shipped indicator
            // values, which is its own pass.
            string dir = FindIndicatorsDirectory();
            var files = Directory.GetFiles(dir, "*Provider.cs", SearchOption.AllDirectories);
            Assert.True(files.Length > 20, $"expected the indicator provider set, found {files.Length}");

            var privateReader = new Regex(@"\b(bool|Boolean)\s+GetBool\s*\(", RegexOptions.Compiled);
            var offenders = files.Where(f => privateReader.IsMatch(File.ReadAllText(f)))
                                 .Select(Path.GetFileName)
                                 .ToList();

            Assert.True(offenders.Count == 0,
                "these providers declare their own boolean reader instead of using " +
                "IndicatorParams.GetBool: " + string.Join(", ", offenders));

            // Vacuity floor on the POPULATION: if nobody reads a boolean parameter through the
            // shared helper any more, this test is guarding an empty set and should say so.
            int callSites = files.Sum(f =>
                Regex.Matches(File.ReadAllText(f), @"IndicatorParams\.GetBool\(").Count);
            Assert.True(callSites >= 8,
                $"only {callSites} shared boolean reads found — the helper has been routed around");
        }

        private static string FindIndicatorsDirectory()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "AccessibleTrader.Core", "Services", "Indicators");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate AccessibleTrader.Core/Services/Indicators.");
        }
    }
}
