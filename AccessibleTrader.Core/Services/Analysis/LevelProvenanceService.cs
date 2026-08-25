using System.Globalization;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Analysis
{
    /// <summary>
    /// Gathers every candidate line the terminal can think of — moving averages (including
    /// multi-timeframe), the registered <see cref="ILevelProvider"/> levels, prior-period highs and
    /// lows, and round numbers — measures each against history, and reports them ranked with the
    /// reason each one exists.
    ///
    /// <para>
    /// This is the answer to "why is this level here". Mainstream platforms make the user draw
    /// lines by hand and then tell them nothing about whether those lines have ever mattered.
    /// Here the provenance and the evidence arrive together, as text, which is the form a screen
    /// reader handles best.
    /// </para>
    /// </summary>
    public interface ILevelProvenanceService
    {
        /// <summary>
        /// Ranks every candidate near the current price. Only candidates within
        /// <paramref name="nearAtr"/> of the last close are returned — a level 40 ATR away is
        /// true but useless, and burying the relevant ones under it would defeat the purpose.
        /// </summary>
        IReadOnlyList<RespectStats> Analyze(
            IReadOnlyList<Ohlcv> bars,
            string timeframe,
            WorkspaceState? state = null,
            RespectOptions? options = null,
            double nearAtr = 12.0);

        /// <summary>
        /// One or two sentences naming the strongest lines above and below price, with their
        /// evidence. Written to be spoken, so it leads with the actionable part.
        /// </summary>
        string Narrate(IReadOnlyList<RespectStats> ranked, IReadOnlyList<Ohlcv> bars, RespectOptions? options = null, int topN = 2);
    }

    /// <inheritdoc cref="ILevelProvenanceService"/>
    public sealed class LevelProvenanceService : ILevelProvenanceService
    {
        private readonly ILevelRespectAnalyzer _analyzer;
        private readonly IMaRespectRanker _maRanker;
        private readonly IResamplerService _resampler;
        private readonly ILevelService? _levels;

        public LevelProvenanceService(
            ILevelRespectAnalyzer analyzer,
            IMaRespectRanker maRanker,
            IResamplerService resampler,
            ILevelService? levels = null)
        {
            _analyzer = analyzer;
            _maRanker = maRanker;
            _resampler = resampler;
            _levels = levels;
        }

        public IReadOnlyList<RespectStats> Analyze(
            IReadOnlyList<Ohlcv> bars,
            string timeframe,
            WorkspaceState? state = null,
            RespectOptions? options = null,
            double nearAtr = 12.0)
        {
            var opts = options ?? RespectOptions.Default;
            if (bars.Count < opts.AtrPeriod + 2) return Array.Empty<RespectStats>();

            var candidates = new List<LineCandidate>();
            candidates.AddRange(BuildMaCandidates(bars, timeframe));
            candidates.AddRange(BuildProviderCandidates(bars, state));
            candidates.AddRange(BuildPriorPeriodCandidates(bars, timeframe));
            candidates.AddRange(BuildRoundNumberCandidates(bars, opts, nearAtr));

            var stats = _analyzer.Analyze(bars, candidates, opts);

            return stats
                .Where(s => !double.IsNaN(s.DistanceAtr) && Math.Abs(s.DistanceAtr) <= nearAtr)
                .OrderByDescending(s => s.IsReliable(opts))
                .ThenByDescending(s => double.IsNaN(s.Score) ? double.NegativeInfinity : s.Score)
                .ToList();
        }

        public string Narrate(
            IReadOnlyList<RespectStats> ranked, IReadOnlyList<Ohlcv> bars,
            RespectOptions? options = null, int topN = 2)
        {
            var opts = options ?? RespectOptions.Default;
            if (bars.Count == 0) return "No data loaded.";

            var reliable = ranked.Where(s => s.IsReliable(opts)).ToList();
            if (reliable.Count == 0)
                return "No level near price has enough history to judge. " +
                       $"A level needs at least {opts.MinTouches} counted touches before its hold rate means anything.";

            double close = bars[^1].Close;
            var below = reliable.Where(s => s.CurrentValue < close).Take(topN).ToList();
            var above = reliable.Where(s => s.CurrentValue > close).Take(topN).ToList();

            var parts = new List<string>();
            foreach (var s in below) parts.Add($"below: {Describe(s)}");
            foreach (var s in above) parts.Add($"above: {Describe(s)}");

            return parts.Count == 0
                ? "No well-tested level sits near price right now."
                : string.Join(". ", parts) + ".";
        }

        private static string Describe(RespectStats s)
        {
            string pct = double.IsNaN(s.HoldRate) ? "no rate" : $"{s.HoldRate * 100:0}% hold";
            string dist = double.IsNaN(s.DistanceAtr) ? "" : $", {Math.Abs(s.DistanceAtr):0.0} ATR away";
            return $"{s.Label} at {Format(s.CurrentValue)}, {pct} over {s.Touches} touches{dist}";
        }

        private static string Format(double price) =>
            double.IsNaN(price) ? "unknown"
            : price >= 1000 ? price.ToString("N0", CultureInfo.InvariantCulture)
            : price >= 1 ? price.ToString("0.##", CultureInfo.InvariantCulture)
            : price.ToString("0.########", CultureInfo.InvariantCulture);

        // ── Candidate sources ─────────────────────────────────────────────────

        private IEnumerable<LineCandidate> BuildMaCandidates(IReadOnlyList<Ohlcv> bars, string timeframe)
        {
            foreach (var spec in MaRespectRanker.DefaultSpecs(timeframe))
            {
                var candidate = _maRanker.BuildCandidate(bars, timeframe, spec);
                if (candidate != null) yield return candidate;
            }
        }

        /// <summary>
        /// Levels contributed by the registered providers (swing pivots, VPVR nodes, Ichimoku,
        /// drawn horizontals). Each is scored as a constant price across history — which is exactly
        /// the provenance question: has price ever cared about this number before?
        /// </summary>
        private IEnumerable<LineCandidate> BuildProviderCandidates(IReadOnlyList<Ohlcv> bars, WorkspaceState? state)
        {
            if (_levels == null || state == null) yield break;

            IReadOnlyList<PriceLevel> levels;
            try { levels = _levels.GetAllLevels(bars, state); }
            catch { yield break; }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var level in levels)
            {
                if (double.IsNaN(level.Price) || level.Price <= 0) continue;
                string id = $"LVL:{level.Source}:{level.Kind}:{level.Price:R}";
                if (!seen.Add(id)) continue;

                var values = new double[bars.Count];
                Array.Fill(values, level.Price);
                string source = string.IsNullOrEmpty(level.Source) ? "level" : level.Source;
                yield return new LineCandidate(id, $"{source} {level.Kind} at {Format(level.Price)}",
                    LineKind.Provider, values);
            }
        }

        /// <summary>
        /// Prior-period highs and lows — the reference points desks and retail alike mark up. Each
        /// is a step function: within period N, the line holds period N-1's extreme, so there is no
        /// future leak.
        /// </summary>
        private IEnumerable<LineCandidate> BuildPriorPeriodCandidates(IReadOnlyList<Ohlcv> bars, string timeframe)
        {
            string[] periods = PriorPeriodsFor(timeframe);
            foreach (var period in periods)
            {
                var htf = _resampler.Resample(bars.ToList(), period);
                if (htf.Count < 2) continue;

                var bucketIndex = new Dictionary<long, int>(htf.Count);
                for (int j = 0; j < htf.Count; j++) bucketIndex[htf[j].Date.Ticks] = j;

                var highs = new double[bars.Count];
                var lows = new double[bars.Count];
                Array.Fill(highs, double.NaN);
                Array.Fill(lows, double.NaN);

                for (int i = 0; i < bars.Count; i++)
                {
                    var start = TimeframeUtility.GetPeriodStart(bars[i].Date, period);
                    if (!bucketIndex.TryGetValue(start.Ticks, out int j)) continue;
                    if (j - 1 < 0) continue;
                    highs[i] = htf[j - 1].High;
                    lows[i] = htf[j - 1].Low;
                }

                yield return new LineCandidate($"PRIOR:{period}:HIGH", $"prior {period} high", LineKind.Horizontal, highs);
                yield return new LineCandidate($"PRIOR:{period}:LOW", $"prior {period} low", LineKind.Horizontal, lows);
            }
        }

        private static string[] PriorPeriodsFor(string timeframe) => (timeframe ?? "").ToLowerInvariant() switch
        {
            "1m" or "5m" or "15m" or "30m" => new[] { "1d" },
            "1h" or "2h" or "4h" => new[] { "1d", "1w" },
            "1d" or "2d" => new[] { "1w", "1M" },
            _ => new[] { "1M" },
        };

        /// <summary>
        /// Round numbers near price, at a magnitude-appropriate step. These are the only levels in
        /// the set that exist for purely psychological reasons, which makes them the most
        /// interesting to measure — if a market ignores its round numbers, that itself is a finding.
        /// </summary>
        private static IEnumerable<LineCandidate> BuildRoundNumberCandidates(
            IReadOnlyList<Ohlcv> bars, RespectOptions opts, double nearAtr)
        {
            double close = bars[^1].Close;
            if (close <= 0) yield break;

            var atr = IndicatorMath.Atr(bars.ToArray(), opts.AtrPeriod);
            double lastAtr = atr[^1];
            if (double.IsNaN(lastAtr) || lastAtr <= 0) yield break;

            // One order of magnitude below the price itself: $60,000 → $1,000 steps;
            // $0.052 → $0.001 steps. Keeps the count sane at any price scale.
            double step = Math.Pow(10, Math.Floor(Math.Log10(close)) - 1);
            if (step <= 0 || double.IsInfinity(step)) yield break;

            double span = lastAtr * nearAtr;
            double lo = close - span, hi = close + span;

            // Hard cap: a pathological price/ATR ratio must not generate thousands of lines.
            const int MaxRoundNumbers = 20;
            double first = Math.Ceiling(lo / step) * step;
            int emitted = 0;

            for (double price = first; price <= hi && emitted < MaxRoundNumbers; price += step)
            {
                if (price <= 0) continue;
                var values = new double[bars.Count];
                Array.Fill(values, price);
                yield return new LineCandidate($"ROUND:{price:R}", $"round number {Format(price)}",
                    LineKind.Horizontal, values);
                emitted++;
            }
        }
    }
}
