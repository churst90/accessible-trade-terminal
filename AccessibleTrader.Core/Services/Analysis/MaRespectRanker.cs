using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Analysis
{
    /// <summary>
    /// One moving average to test. <paramref name="SourceTimeframe"/> null means "the chart's own
    /// timeframe"; a value means the average is computed on that HIGHER timeframe's closes and
    /// stepped onto the chart.
    ///
    /// <para>
    /// That distinction is the entire point of this type. A 70-period average of daily closes and
    /// a 10-period average of weekly closes both span 70 days, but they are NOT the same line: the
    /// weekly version ignores the intra-week path and only moves once a week. Which of the two
    /// price actually respects is an empirical question, and this ranker is how it gets answered
    /// instead of argued about.
    /// </para>
    /// </summary>
    public record MaSpec(string MaType, int Period, string? SourceTimeframe = null)
    {
        public string Id => SourceTimeframe == null
            ? $"MA:{MaType}:{Period}"
            : $"MA:{MaType}:{Period}@{SourceTimeframe}";

        public string Label => SourceTimeframe == null
            ? $"{Period} {MaType}"
            : $"{SourceTimeframe} {Period} {MaType}";
    }

    /// <summary>
    /// Ranks moving averages — including multi-timeframe ones — by how well price actually
    /// respected them over the loaded history.
    /// </summary>
    public interface IMaRespectRanker
    {
        /// <summary>
        /// Builds one <see cref="LineCandidate"/> per spec and scores them all. Results are sorted
        /// best-first by <see cref="RespectStats.Score"/>, with candidates below
        /// <c>MinTouches</c> sorted last regardless of hold rate — a perfect record on two touches
        /// must never outrank a strong record on thirty.
        /// </summary>
        IReadOnlyList<RespectStats> Rank(
            IReadOnlyList<Ohlcv> bars,
            string chartTimeframe,
            IEnumerable<MaSpec>? specs = null,
            RespectOptions? options = null);

        /// <summary>Materialises a spec as a per-bar price array, or null when it cannot be built.</summary>
        LineCandidate? BuildCandidate(IReadOnlyList<Ohlcv> bars, string chartTimeframe, MaSpec spec);
    }

    /// <inheritdoc cref="IMaRespectRanker"/>
    public sealed class MaRespectRanker : IMaRespectRanker
    {
        /// <summary>
        /// Periods worth testing by default. Deliberately includes the ones traders and desks
        /// actually watch (10/20/21/50/100/200) plus 89 — a Fibonacci period with a real following
        /// in crypto. The ranker's job is to report which of these a market honours, not to assume.
        /// </summary>
        public static readonly int[] DefaultPeriods = { 10, 20, 21, 50, 89, 100, 200 };

        /// <summary>Higher timeframes probed for each base timeframe, coarsest last.</summary>
        public static readonly IReadOnlyDictionary<string, string[]> DefaultHigherTimeframes =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["1h"] = new[] { "4h", "1d" },
                ["2h"] = new[] { "4h", "1d" },
                ["4h"] = new[] { "1d", "1w" },
                // Monthly is included for daily/2-day charts because it is a level traders
                // actually cite by name ("the monthly 10 EMA"), and the whole point of this
                // ranker is to test such claims rather than assume them either way.
                ["1d"] = new[] { "2d", "1w", "1M" },
                ["2d"] = new[] { "1w", "1M" },
                ["1w"] = new[] { "1M" },
            };

        private readonly ILevelRespectAnalyzer _analyzer;
        private readonly IResamplerService _resampler;

        public MaRespectRanker(ILevelRespectAnalyzer analyzer, IResamplerService resampler)
        {
            _analyzer = analyzer;
            _resampler = resampler;
        }

        /// <summary>
        /// The default probe set: every default period in both EMA and SMA on the chart timeframe,
        /// plus the same periods as EMAs on each higher timeframe.
        /// </summary>
        public static IReadOnlyList<MaSpec> DefaultSpecs(string chartTimeframe)
        {
            var specs = new List<MaSpec>();
            foreach (int period in DefaultPeriods)
            {
                specs.Add(new MaSpec("EMA", period));
                specs.Add(new MaSpec("SMA", period));
            }

            if (DefaultHigherTimeframes.TryGetValue(chartTimeframe ?? "", out var higher))
                foreach (var tf in higher)
                    foreach (int period in DefaultPeriods)
                        specs.Add(new MaSpec("EMA", period, tf));

            return specs;
        }

        public IReadOnlyList<RespectStats> Rank(
            IReadOnlyList<Ohlcv> bars,
            string chartTimeframe,
            IEnumerable<MaSpec>? specs = null,
            RespectOptions? options = null)
        {
            var opts = options ?? RespectOptions.Default;
            var specList = (specs ?? DefaultSpecs(chartTimeframe)).ToList();

            var candidates = new List<LineCandidate>(specList.Count);
            foreach (var spec in specList)
            {
                var candidate = BuildCandidate(bars, chartTimeframe, spec);
                if (candidate != null) candidates.Add(candidate);
            }

            var stats = _analyzer.Analyze(bars, candidates, opts);

            return stats
                .OrderByDescending(s => s.IsReliable(opts))
                .ThenByDescending(s => double.IsNaN(s.Score) ? double.NegativeInfinity : s.Score)
                .ToList();
        }

        public LineCandidate? BuildCandidate(IReadOnlyList<Ohlcv> bars, string chartTimeframe, MaSpec spec)
        {
            if (bars.Count == 0 || spec.Period < 1) return null;

            double[] values = spec.SourceTimeframe == null
                ? BuildSameTimeframe(bars, spec)
                : BuildHigherTimeframe(bars, chartTimeframe, spec);

            if (values.Length != bars.Count) return null;

            var kind = spec.SourceTimeframe == null
                ? LineKind.MovingAverage
                : LineKind.MultiTimeframeMovingAverage;
            return new LineCandidate(spec.Id, spec.Label, kind, values);
        }

        private static double[] BuildSameTimeframe(IReadOnlyList<Ohlcv> bars, MaSpec spec)
        {
            var closes = new double[bars.Count];
            for (int i = 0; i < bars.Count; i++) closes[i] = bars[i].Close;
            return MovingAverageHelper.Calculate(closes, spec.Period, spec.MaType);
        }

        /// <summary>
        /// Computes the average on resampled higher-timeframe closes, then steps it back onto the
        /// base bars.
        ///
        /// <para>
        /// NO FUTURE LEAK: for a base bar inside higher-timeframe bucket <c>j</c>, the value used
        /// is the average through bucket <c>j-1</c> — the last bucket that had actually CLOSED at
        /// that moment. Using bucket <c>j</c> would let a Monday bar see Friday's weekly close.
        /// This is also what makes the line a genuine step function, which is the property the
        /// multi-timeframe claim is about in the first place.
        /// </para>
        /// </summary>
        private double[] BuildHigherTimeframe(IReadOnlyList<Ohlcv> bars, string chartTimeframe, MaSpec spec)
        {
            var result = new double[bars.Count];
            Array.Fill(result, double.NaN);

            string htf = spec.SourceTimeframe!;
            // A "higher" timeframe that isn't actually higher would resample to itself and make
            // the step logic meaningless.
            if (!string.IsNullOrEmpty(chartTimeframe) &&
                TimeframeUtility.ToMilliseconds(htf) <= TimeframeUtility.ToMilliseconds(chartTimeframe))
                return result;

            var htfBars = _resampler.Resample(bars.ToList(), htf);
            if (htfBars.Count < spec.Period + 1) return result;

            var htfCloses = new double[htfBars.Count];
            for (int j = 0; j < htfBars.Count; j++) htfCloses[j] = htfBars[j].Close;
            var htfMa = MovingAverageHelper.Calculate(htfCloses, spec.Period, spec.MaType);

            // Bucket start → index, for an O(1) lookup per base bar. Keyed on Ticks rather than a
            // epoch conversion so the mapping is immune to the DateTimeKind of the incoming bars.
            var bucketIndex = new Dictionary<long, int>(htfBars.Count);
            for (int j = 0; j < htfBars.Count; j++)
                bucketIndex[htfBars[j].Date.Ticks] = j;

            for (int i = 0; i < bars.Count; i++)
            {
                var bucketStart = TimeframeUtility.GetPeriodStart(bars[i].Date, htf);
                if (!bucketIndex.TryGetValue(bucketStart.Ticks, out int j)) continue;
                int lastClosed = j - 1;
                if (lastClosed < 0 || lastClosed >= htfMa.Length) continue;
                result[i] = htfMa[lastClosed];
            }

            return result;
        }
    }
}
