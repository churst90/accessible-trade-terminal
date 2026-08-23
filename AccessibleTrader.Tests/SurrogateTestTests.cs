using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.StrategyLab;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Coverage for <see cref="SurrogateTest"/> — the control that every "beat random" verdict in
/// this repo rests on.
///
/// <para>
/// <c>CatalogueProvenanceTests</c> already verifies that a control was *claimed*: it asserts
/// <c>Provenance.Controls</c> contains "random". Nothing verified that the control *computes
/// correctly*, and the two failure modes are opposite in consequence. A block bootstrap that
/// resamples the wrong axis, or draws correlated blocks, or quietly returns the real series,
/// produces surrogates that are too easy or too hard to beat — and it does so silently, biasing
/// every verdict in the same direction, including the six specs retired as falsified and the one
/// kept as <c>ControlTested</c>. There is no crash and no NaN to notice.
/// </para>
///
/// <para>
/// So the tests below are mostly about the *properties that make the control valid* rather than
/// about outputs: the return distribution is preserved exactly (each surrogate return is one of
/// the real returns), the blocks are contiguous runs of <see cref="SurrogateTest.BlockLength"/>
/// (which is the only reason volatility clustering survives), the price path is destroyed, the
/// timestamps are not, and the same seed reproduces the same series. Plus the two arithmetic
/// paths a reader is most likely to get wrong: the +1-corrected empirical p-value, and the
/// zero-variance case that must be NaN rather than infinity.
/// </para>
/// </summary>
public class SurrogateTestTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // ── BlockBootstrap: the properties that make it a fair control ─────

    /// <summary>
    /// A surrogate has to be the same length as the real series or the two hold rates are
    /// measured over different amounts of history and are not comparable.
    /// </summary>
    [Fact]
    public void BlockBootstrap_PreservesSeriesLength()
    {
        var bars = Walk(317);

        var surrogate = SurrogateTest.BlockBootstrap(bars, new Random(1));

        Assert.Equal(bars.Count, surrogate.Count);
    }

    /// <summary>
    /// Timestamps come from the real series, in order. This is load-bearing rather than
    /// cosmetic: the multi-timeframe specs resample the surrogate to a higher timeframe, and if
    /// the surrogate's bars did not fall into the same buckets as the real ones, the
    /// multi-timeframe arm of the surrogate test would be comparing a different number of
    /// higher-timeframe bars and could not be run at all.
    /// </summary>
    [Fact]
    public void BlockBootstrap_CopiesTimestampsFromTheRealSeriesInOrder()
    {
        var bars = Walk(200);

        var surrogate = SurrogateTest.BlockBootstrap(bars, new Random(2));

        Assert.Equal(bars.Select(b => b.Date), surrogate.Select(b => b.Date));
    }

    /// <summary>
    /// Every surrogate bar must be a bar a market could have printed. The touch detector reads
    /// highs and lows, so a bar whose high sits below its close would register phantom
    /// penetrations of the line and shift the surrogate hold rate for a reason that has nothing
    /// to do with the null hypothesis.
    ///
    /// <para>
    /// The fixture deliberately contains malformed source bars — high under low, close outside
    /// the range — because that is the only thing that makes this test capable of failing. The
    /// shape ratios are taken from the same source bar as the price, so a well-formed input can
    /// only produce a well-formed output and the repair lines in <c>BlockBootstrap</c> never
    /// fire. Providers do emit rows like these, and without the malformed bars this test would be
    /// asserting arithmetic rather than guarding anything.
    /// </para>
    /// </summary>
    [Fact]
    public void BlockBootstrap_RepairsTheOhlcInvariant_EvenFromMalformedSourceBars()
    {
        var bars = Walk(400);
        bars[80] = bars[80] with { High = bars[80].Low, Low = bars[80].High };   // inverted
        bars[81] = bars[81] with { High = bars[81].Close * 0.9 };                // high under close
        bars[82] = bars[82] with { Low = bars[82].Close * 1.1 };                 // low over close

        var surrogate = SurrogateTest.BlockBootstrap(bars, new Random(3));

        for (int i = 0; i < surrogate.Count; i++)
        {
            var b = surrogate[i];
            Assert.True(b.High >= Math.Max(b.Open, b.Close), $"bar {i}: high {b.High} below body top");
            Assert.True(b.Low <= Math.Min(b.Open, b.Close), $"bar {i}: low {b.Low} above body bottom");
            Assert.True(b.High >= b.Low, $"bar {i}: high below low");
            Assert.True(b.Close > 0 && double.IsFinite(b.Close), $"bar {i}: close {b.Close}");
            Assert.True(double.IsFinite(b.Open) && double.IsFinite(b.High) && double.IsFinite(b.Low),
                $"bar {i}: non-finite OHLC");
        }
    }

    /// <summary>
    /// **The property the whole method exists for.** A surrogate is only "statistically
    /// identical" if its returns are the real returns rearranged — and it only preserves
    /// volatility clustering if they are rearranged in contiguous *blocks*. An implementation
    /// that drew returns independently would pass every other test in this file while destroying
    /// the clustering the docstring promises, which makes surrogates smoother than reality and
    /// every real series look significant.
    ///
    /// <para>
    /// The series is built so each log return is a distinct multiple of 1e-5, which makes each
    /// surrogate return decodable back to the exact source bar it was drawn from. Three things
    /// are then asserted: every decoded index is a real index (a resampling, not a synthesis);
    /// indices advance by exactly one inside each block (contiguity); and the breaks happen only
    /// at block boundaries. The last assertion is what pins the block *length* — a longer block
    /// would leave interior boundaries continuous, a shorter one would break inside a block.
    /// </para>
    /// </summary>
    [Fact]
    public void BlockBootstrap_ResamplesContiguousBlocksOfBlockLengthBars()
    {
        // Pinned like the FNV constants below: the block length is a research parameter, and
        // changing it silently reseeds the null distribution behind every stored verdict.
        Assert.Equal(20, SurrogateTest.BlockLength);

        const int n = 200;
        var bars = DistinctReturnSeries(n);

        var surrogate = SurrogateTest.BlockBootstrap(bars, new Random(11));

        var sourceIndex = DecodeReturnSources(bars, surrogate);

        var breaks = new List<int>();
        for (int i = 1; i < n; i++)
            if (sourceIndex[i] != sourceIndex[i - 1] + 1) breaks.Add(i);

        Assert.All(breaks, i => Assert.True(i % SurrogateTest.BlockLength == 0,
            $"the return run broke at position {i}, which is not a multiple of " +
            $"{SurrogateTest.BlockLength} — blocks are not contiguous runs of that length"));

        int boundaries = n / SurrogateTest.BlockLength - 1;
        Assert.True(breaks.Count >= boundaries / 2,
            $"only {breaks.Count} of {boundaries} block boundaries actually re-drew; the series is " +
            "barely being resampled at all");
    }

    /// <summary>
    /// A bar's shape must come from the same source bar as its return. Range and return magnitude
    /// are correlated in real markets — a big move prints a big bar — and resampling the two
    /// independently would break that correlation while leaving every other property in this file
    /// intact, producing surrogates whose bars are the wrong size for their moves. The touch
    /// detector reads exactly those wicks.
    /// </summary>
    [Fact]
    public void BlockBootstrap_TakesEachBarsShapeFromTheSameSourceBarAsItsReturn()
    {
        var bars = DistinctReturnSeries(200);

        var surrogate = SurrogateTest.BlockBootstrap(bars, new Random(11));
        var sourceIndex = DecodeReturnSources(bars, surrogate);

        for (int i = 0; i < surrogate.Count; i++)
        {
            double wickSource = (surrogate[i].High / surrogate[i].Close - 1) / ShapeStep;
            double bodySource = (1 - surrogate[i].Open / surrogate[i].Close) / (ShapeStep / 2);
            Assert.True(Math.Abs(wickSource - sourceIndex[i]) < 1e-3,
                $"position {i}: wick came from bar {wickSource:0.##} but the return came from " +
                $"bar {sourceIndex[i]}");
            double lowSource = (1 - surrogate[i].Low / surrogate[i].Close) / ShapeStep;
            Assert.True(Math.Abs(bodySource - sourceIndex[i]) < 1e-3,
                $"position {i}: open came from bar {bodySource:0.##} but the return came from " +
                $"bar {sourceIndex[i]}");
            Assert.True(Math.Abs(lowSource - sourceIndex[i]) < 1e-3,
                $"position {i}: low came from bar {lowSource:0.##} but the return came from " +
                $"bar {sourceIndex[i]}");
        }
    }

    /// <summary>
    /// Preserving the returns is half the job; the other half is destroying the price path, which
    /// is what removes the memory of specific levels. If the surrogate tracked the real series
    /// the test would be comparing a line against itself and would never reject anything.
    /// </summary>
    [Fact]
    public void BlockBootstrap_DestroysTheRealPricePath()
    {
        var bars = Walk(400);

        var surrogate = SurrogateTest.BlockBootstrap(bars, new Random(5));

        int identical = surrogate.Where((b, i) => Math.Abs(b.Close - bars[i].Close) < 1e-9).Count();
        Assert.True(identical < bars.Count / 10,
            $"{identical}/{bars.Count} surrogate closes match the real series — the path survived");
    }

    /// <summary>
    /// Seeded reproducibility. Every command in the lab derives its seed from the asset name so a
    /// rerun reproduces exactly; that promise is only worth anything if the bootstrap itself is a
    /// pure function of the RNG it is handed.
    /// </summary>
    [Fact]
    public void BlockBootstrap_IsAPureFunctionOfTheSuppliedRandom()
    {
        var bars = Walk(300);

        var a = SurrogateTest.BlockBootstrap(bars, new Random(99));
        var b = SurrogateTest.BlockBootstrap(bars, new Random(99));
        var c = SurrogateTest.BlockBootstrap(bars, new Random(100));

        Assert.Equal(a.Select(x => x.Close), b.Select(x => x.Close));
        Assert.NotEqual(a.Select(x => x.Close), c.Select(x => x.Close));
    }

    /// <summary>
    /// Below <c>BlockLength + 2</c> bars there is nothing to resample and the method returns the
    /// real series. Pinned because of what it means one layer up: a surrogate identical to the
    /// real series scores the same hold rate, every surrogate then ties, and the tie counts
    /// against the real series — so the p-value goes to 1.0 and the verdict is "no evidence".
    /// The degenerate case is conservative, which is the only acceptable direction for it to
    /// fail, and it would be easy to "fix" into being permissive.
    /// </summary>
    [Fact]
    public void BlockBootstrap_TooShortToResample_ReturnsTheRealSeries()
    {
        var bars = Walk(SurrogateTest.BlockLength + 1);

        var surrogate = SurrogateTest.BlockBootstrap(bars, new Random(7));

        Assert.Equal(bars.Select(b => b.Close), surrogate.Select(b => b.Close));
    }

    /// <summary>
    /// Providers do emit zero-priced rows — there is a standing finding about zero-valued open
    /// interest rows reaching analytics. A zero close makes <c>log(0)</c> the natural result, and
    /// one NaN in the price path poisons every bar after it, so the guards are asserted rather
    /// than assumed.
    /// </summary>
    [Fact]
    public void BlockBootstrap_ZeroPricedBars_ProduceNoNaNs()
    {
        var bars = Walk(200);
        bars[50] = new Ohlcv(bars[50].Date, 0, 0, 0, 0, 0);
        bars[51] = new Ohlcv(bars[51].Date, 0, 0, 0, 0, 0);

        var surrogate = SurrogateTest.BlockBootstrap(bars, new Random(13));

        Assert.All(surrogate, b => Assert.True(
            double.IsFinite(b.Open) && double.IsFinite(b.High) &&
            double.IsFinite(b.Low) && double.IsFinite(b.Close),
            "a zero-priced input bar produced a non-finite surrogate bar"));
    }

    // ── Run: the statistics that become the verdict ────────────────────

    /// <summary>
    /// The one-sided empirical p-value, with the +1 correction on both sides. With 99 surrogates
    /// and none matching the real series the answer is 1/100, not 0/99 — a p-value of exactly
    /// zero is a claim no finite number of surrogates can support, and the correction is what
    /// keeps the lab from printing one.
    /// </summary>
    [Fact]
    public void Run_PValue_IsTheCorrectedEmpiricalFraction()
    {
        var result = RunScripted(real: 0.80, surrogates: Enumerable.Repeat(0.10, 99));

        Assert.Equal(99, result.SurrogateCount);
        Assert.Equal(0, result.BeatenBy);
        Assert.Equal(0.01, result.PValue, 12);
    }

    /// <summary>
    /// A tie counts *against* the real series. With every surrogate equalling the real hold rate
    /// the p-value is 1.0 — no evidence — where a strict <c>&gt;</c> comparison would report 0.1
    /// and read as significant at the 10% level. The direction of that comparison is the entire
    /// difference between the two readings, and nothing else in the codebase records the choice.
    /// </summary>
    [Fact]
    public void Run_TiedSurrogatesCountAgainstTheRealSeries()
    {
        var result = RunScripted(real: 0.50, surrogates: Enumerable.Repeat(0.50, 9));

        Assert.Equal(9, result.BeatenBy);
        Assert.Equal(1.0, result.PValue, 12);
    }

    /// <summary>
    /// Mean, standard deviation and z-score on known inputs. The standard deviation is the
    /// *sample* one (n-1): with holds of 0.2 and 0.4 that is sqrt(0.02) ≈ 0.1414, where the
    /// population form would give 0.1 and inflate every z-score by 41%.
    /// </summary>
    [Fact]
    public void Run_ZScore_UsesTheSampleStandardDeviation()
    {
        var result = RunScripted(real: 0.50, surrogates: new[] { 0.20, 0.40 });

        Assert.Equal(0.30, result.SurrogateMean, 12);
        Assert.Equal(Math.Sqrt(0.02), result.SurrogateStdDev, 12);
        Assert.Equal(0.20 / Math.Sqrt(0.02), result.ZScore, 10);
    }

    /// <summary>
    /// Zero variance across surrogates must give NaN, not infinity. This is the shape of a
    /// degenerate run — an analyzer returning a constant, a series too short to resample — and an
    /// infinite z-score would print as the most significant result the lab has ever produced.
    /// NaN is unmissable in a report; +∞ reads as a discovery.
    /// </summary>
    [Fact]
    public void Run_ZeroVarianceSurrogates_GiveNaNZScoreNotInfinity()
    {
        var result = RunScripted(real: 0.90, surrogates: Enumerable.Repeat(0.30, 20));

        Assert.Equal(0.0, result.SurrogateStdDev, 12);
        Assert.True(double.IsNaN(result.ZScore), $"expected NaN, got {result.ZScore}");
        Assert.False(double.IsInfinity(result.ZScore));
    }

    /// <summary>
    /// Edge is real minus surrogate mean in percentage points, and it must keep its sign — a
    /// series that respects its own average *less* than random has to report negative, because
    /// that is the finding.
    /// </summary>
    [Fact]
    public void Run_EdgePp_IsSignedPercentagePoints()
    {
        var better = RunScripted(real: 0.62, surrogates: Enumerable.Repeat(0.50, 10));
        var worse = RunScripted(real: 0.30, surrogates: Enumerable.Repeat(0.50, 10));

        Assert.Equal(12.0, better.EdgePp, 10);
        Assert.Equal(-20.0, worse.EdgePp, 10);
    }

    /// <summary>
    /// A surrogate that produced no touches is dropped from the distribution entirely rather than
    /// counted as a loss. Counting it would pad the denominator with runs that never measured
    /// anything and drag every p-value down — manufacturing significance out of failed draws.
    /// </summary>
    [Fact]
    public void Run_SurrogatesWithNoTouches_AreExcludedFromTheDistribution()
    {
        var analyzer = new ScriptedAnalyzer(
            (10, 0.60),      // the real series
            (0, double.NaN), // surrogate produced no touches — must not count
            (5, 0.70),
            (0, double.NaN),
            (5, 0.20));

        var result = Run(analyzer, requestedSurrogates: 4);

        Assert.Equal(2, result.SurrogateCount);
        Assert.Equal(1, result.BeatenBy);
        Assert.Equal(2.0 / 3.0, result.PValue, 12);
    }

    /// <summary>
    /// No touches on the real series means there is nothing to test, and the result says so with
    /// NaN rather than a number. It also stops before running a single surrogate — the expensive
    /// part — which is worth pinning because the early return is easy to lose in a refactor.
    /// </summary>
    [Fact]
    public void Run_RealSeriesWithNoTouches_ReportsNothingAndRunsNoSurrogates()
    {
        var analyzer = new ScriptedAnalyzer((0, double.NaN));

        var result = Run(analyzer, requestedSurrogates: 50);

        Assert.Equal(0, result.Touches);
        Assert.True(double.IsNaN(result.RealHold));
        Assert.Equal(0, result.SurrogateCount);
        Assert.Equal(1, analyzer.Calls);
    }

    /// <summary>
    /// End to end with the real analyzer and the real ranker: the same seed reproduces the run
    /// exactly, and a different seed does not. Without the second half a hard-coded
    /// <c>new Random(0)</c> inside the loop would satisfy the first.
    /// </summary>
    [Fact]
    public void Run_IsReproducibleForASeedAndSensitiveToIt()
    {
        var bars = Walk(500);
        var analyzer = new LevelRespectAnalyzer();
        var ranker = new MaRespectRanker(analyzer, new ResamplerService());
        var spec = new MaSpec("SMA", 20);
        var opts = RespectOptions.Default;

        SurrogateTest.Result Once(int seed) =>
            SurrogateTest.Run("TEST", bars, "1d", spec, ranker, analyzer, opts, 25, seed);

        var first = Once(4242);
        var again = Once(4242);
        var other = Once(9999);

        Assert.True(first.SurrogateCount > 0,
            "fixture produced no scored surrogates — the reproducibility assertion would be vacuous");
        Assert.Equal(first.SurrogateMean, again.SurrogateMean, 12);
        Assert.Equal(first.BeatenBy, again.BeatenBy);
        Assert.NotEqual(first.SurrogateMean, other.SurrogateMean, 12);
    }

    // ── The seed the reproducibility above depends on ──────────────────

    /// <summary>
    /// <c>string.GetHashCode()</c> is randomised per process in .NET. A lab command that seeds a
    /// control with it resamples on every run, and a p-value that moves between runs is not a
    /// p-value — this has already bitten this repo once, with the same bucket reading -5.6 and
    /// then -1.8 on consecutive runs of unchanged code.
    ///
    /// <para>
    /// The fix was then written seven times as a private <c>StableSeed</c> copy while six other
    /// seed sites kept the raw hash, including <c>RespectCommand</c>'s call into
    /// <see cref="SurrogateTest.Run"/> — under a comment that read "seeded off the asset name so
    /// a rerun reproduces exactly". This guard is the reason that cannot come back: one shared
    /// <see cref="StableSeed"/>, and no <c>GetHashCode()</c> anywhere in the lab.
    /// </para>
    /// </summary>
    [Fact]
    public void NoLabCommandDerivesASeedFromGetHashCode()
    {
        var lab = Path.Combine(RepoPaths.RepoRoot(), "AccessibleTrader.StrategyLab");
        Assert.True(Directory.Exists(lab), $"StrategyLab not found at {lab}");

        var offenders = new List<string>();
        int scanned = 0;
        foreach (var file in Directory.EnumerateFiles(lab, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;
            scanned++;

            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!line.Contains("GetHashCode()")) continue;
                if (line.TrimStart().StartsWith("//") || line.TrimStart().StartsWith("///")) continue;
                offenders.Add($"{Path.GetFileName(file)}:{i + 1}  {line.Trim()}");
            }
        }

        Assert.True(scanned >= 20, $"the scan only saw {scanned} lab sources; it is not covering the lab");
        Assert.True(offenders.Count == 0,
            "these lab sources derive a value from the per-process-randomised string hash; use " +
            "StableSeed.From so a rerun reproduces:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The seed is a pure function of the string and does not move between processes. Asserting
    /// the literal values is the point — the constants are the contract, and changing them
    /// silently reseeds every control in the lab so that no stored verdict can be reproduced
    /// against a fresh run.
    /// </summary>
    [Fact]
    public void StableSeed_IsFixedForever()
    {
        Assert.Equal(282597168, StableSeed.From("BTCUSD"));
        Assert.Equal(1961661828, StableSeed.From("ETHUSD"));
        Assert.Equal(18652613, StableSeed.From(""));

        // Non-negative by construction, so no call site needs Math.Abs — which throws on
        // int.MinValue, a value a raw hash can actually return.
        Assert.All(new[] { "BTCUSD", "ETHUSD", "￿￿￿", "a", "" },
            s => Assert.True(StableSeed.From(s) >= 0, $"negative seed for '{s}'"));
    }

    // ── Fixtures ───────────────────────────────────────────────────────

    private const double ReturnStep = 1e-5;
    private const double ShapeStep = 1e-4;

    /// <summary>
    /// A deterministic random walk with realistic bar shapes: each bar opens at the previous
    /// close and wicks a little either side of the body.
    /// </summary>
    private static List<Ohlcv> Walk(int count, int seed = 7, double startPrice = 100)
    {
        var rng = new Random(seed);
        var bars = new List<Ohlcv>(count);
        double price = startPrice;
        for (int i = 0; i < count; i++)
        {
            double close = price * Math.Exp((rng.NextDouble() - 0.5) * 0.02);
            double high = Math.Max(price, close) * 1.003;
            double low = Math.Min(price, close) * 0.997;
            bars.Add(new Ohlcv(Start.AddDays(i), price, high, low, close, 1000));
            price = close;
        }
        return bars;
    }

    /// <summary>
    /// A series whose log return at bar i is exactly <c>i * 1e-5</c> and whose wick reaches
    /// exactly <c>i * 1e-4</c> above its close. Both are distinct per bar, so a surrogate bar can
    /// be decoded back to the source bar its *return* came from and, independently, to the source
    /// bar its *shape* came from — which is how the two are proven to be the same bar.
    /// </summary>
    private static List<Ohlcv> DistinctReturnSeries(int count)
    {
        var bars = new List<Ohlcv>(count);
        double price = 100;
        for (int i = 0; i < count; i++)
        {
            if (i > 0) price *= Math.Exp(ReturnStep * i);
            bars.Add(new Ohlcv(Start.AddDays(i), price * (1 - i * ShapeStep / 2),
                               price * (1 + i * ShapeStep), price * (1 - i * ShapeStep),
                               price, 1000));
        }
        return bars;
    }

    /// <summary>
    /// Recovers, for each surrogate bar, the index of the real bar whose log return it used.
    /// Only valid against <see cref="DistinctReturnSeries"/>.
    /// </summary>
    private static int[] DecodeReturnSources(List<Ohlcv> bars, List<Ohlcv> surrogate)
    {
        var index = new int[surrogate.Count];
        for (int i = 0; i < surrogate.Count; i++)
        {
            double prev = i == 0 ? bars[0].Close : surrogate[i - 1].Close;
            double exact = Math.Log(surrogate[i].Close / prev) / ReturnStep;
            index[i] = (int)Math.Round(exact);
            Assert.True(Math.Abs(exact - index[i]) < 1e-4,
                $"position {i}: return does not match any real return (decoded {exact})");
            Assert.InRange(index[i], 1, bars.Count - 1);
        }
        return index;
    }

    private static SurrogateTest.Result RunScripted(double real, IEnumerable<double> surrogates)
    {
        var script = new List<(int, double)> { (10, real) };
        script.AddRange(surrogates.Select(h => (5, h)));
        return Run(new ScriptedAnalyzer(script.ToArray()), script.Count - 1);
    }

    private static SurrogateTest.Result Run(ScriptedAnalyzer analyzer, int requestedSurrogates)
    {
        var bars = Walk(200);
        var ranker = new MaRespectRanker(new LevelRespectAnalyzer(), new ResamplerService());
        return SurrogateTest.Run("TEST", bars, "1d", new MaSpec("SMA", 20), ranker, analyzer,
                                 RespectOptions.Default, requestedSurrogates, seed: 1234);
    }

    /// <summary>
    /// Returns scripted (touches, hold-rate) pairs in call order — first call is the real series,
    /// the rest are surrogates. Substituting the analyzer is what lets the statistics be tested
    /// against known inputs instead of against whatever a random walk happened to produce.
    /// </summary>
    private sealed class ScriptedAnalyzer : ILevelRespectAnalyzer
    {
        private readonly Queue<(int touches, double holdRate)> _script;

        public ScriptedAnalyzer(params (int touches, double holdRate)[] script) =>
            _script = new Queue<(int, double)>(script);

        public int Calls { get; private set; }

        public IReadOnlyList<RespectStats> Analyze(
            IReadOnlyList<Ohlcv> bars,
            IReadOnlyList<LineCandidate> candidates,
            RespectOptions? options = null)
        {
            Calls++;
            if (_script.Count == 0) return Array.Empty<RespectStats>();
            var (touches, hold) = _script.Dequeue();
            return new[]
            {
                new RespectStats("id", "label", LineKind.MovingAverage,
                    touches, (int)Math.Round(touches * (double.IsNaN(hold) ? 0 : hold)), hold,
                    0, 0, 0, 0, null, -1, double.NaN, double.NaN, Array.Empty<LineTouch>())
            };
        }
    }
}
