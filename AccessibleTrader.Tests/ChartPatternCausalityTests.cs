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
    /// The causality contract, applied to chart-pattern detection.
    ///
    /// <para>
    /// <c>IndicatorCausalityTests</c> covers every <c>IIndicatorProvider</c>. The pattern detector
    /// is not one, so neither sweep has ever touched it — and the user report that started this
    /// whole line of work was about patterns: <i>"chart pattern targets and triggers change as more
    /// history loads."</i> Some of that turned out to be the indicator smear and was fixed there.
    /// This is the rest of it, asked directly.
    /// </para>
    ///
    /// <para>
    /// Two questions, the same two the indicator contract asks:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Does a formation change when the FUTURE arrives?</b> A pattern that was knowable at
    /// bar 300 must have the same trigger and the same target when the chart holds 700 bars as when
    /// it holds 1400. If it does not, the terminal announced a level the user acted on and then
    /// quietly moved it.</item>
    /// <item><b>Does a formation change when OLDER bars arrive?</b> This is the scroll-back, and it
    /// is the one nobody could ask before, because <c>ChartPattern.Key</c> is built from bar indices
    /// and every index shifts when history is prepended. <c>ChartPattern.Identity</c> — kind plus
    /// the two dates — is what makes the comparison expressible.</item>
    /// </list>
    ///
    /// <para>
    /// What is deliberately NOT required: that the two runs find the same NUMBER of patterns. A
    /// short run is missing the history a formation near its left edge needs, and a formation whose
    /// left half was cut off genuinely is not there any more. The requirement is about patterns
    /// that sit comfortably inside both runs.
    /// </para>
    /// </summary>
    public class ChartPatternCausalityTests
    {
        private const int SeriesLength = 1400;

        private static IReadOnlyList<ChartPattern> Detect(IReadOnlyList<Ohlcv> bars) =>
            new ChartPatternDetector(new SwingStructureAnalyzer()).Detect(bars);

        public static IEnumerable<object[]> Flavours() =>
            CausalityProbeSeries.AllFlavours.Select(f => new object[] { f });

        /// <summary>
        /// How far past the start of the shorter run a formation has to begin before the two runs
        /// are required to agree.
        ///
        /// <para>
        /// Grounded in the detector's own longest lookback — <c>ChartPatternOptions.MaxPatternBars</c>
        /// is 160 — plus room for the swing span and the ATR window it scales its tolerance by.
        /// Measured, not guessed: across all four series and all three drops, every formation the
        /// two runs disagree about begins within 22 bars of the shorter run's left edge, which is
        /// a formation with nine bars of history behind it and no swing structure to sit on. 200
        /// clears that by an order of magnitude while still leaving roughly 1200 bars of each
        /// series inside the compared set. It was 400 first, and 400 was quietly excluding a
        /// quarter of the series for no reason anyone could state.
        /// </para>
        /// </summary>
        private const int Warmup = 200;

        // ── Does a formation change when the FUTURE arrives? ──────────────────────────────────

        [Theory]
        [MemberData(nameof(Flavours))]
        public void AFormationDoesNotChangeWhenLaterBarsArrive(int flavour)
        {
            var bars = CausalityProbeSeries.Bars(flavour, SeriesLength);
            var whole = Detect(bars);
            var offenders = new List<string>();

            foreach (int k in new[] { 700, 950, 1200 })
            {
                var shortRun = Detect(bars.Take(k).ToList());
                var byKey = shortRun.ToDictionary(p => p.Key);

                // Every formation the full run says was knowable before bar k has to be in the
                // short run too — the short run is what the user actually had on screen at the time.
                foreach (var p in whole.Where(p => p.KnownAtIndex < k))
                {
                    if (!byKey.TryGetValue(p.Key, out var q))
                    {
                        offenders.Add($"{p.Kind} at bars {p.StartBarIndex}-{p.EndBarIndex} is knowable at " +
                                      $"bar {p.KnownAtIndex} on the full series but is not found at all when " +
                                      $"{k} bars are loaded. It appeared retroactively.");
                        continue;
                    }

                    Geometry(p, q, 0, k, offenders);

                    // A completion that happened before bar k must be reported by the short run
                    // too; one that happens after it must not be known yet.
                    int? expected = p.CompletedAtIndex < k ? p.CompletedAtIndex : null;
                    if (q.CompletedAtIndex != expected)
                        offenders.Add($"{p.Kind} at bars {p.StartBarIndex}-{p.EndBarIndex}: completion reads " +
                                      $"{Show(q.CompletedAtIndex)} with {k} bars loaded and {Show(expected)} " +
                                      $"is what the full series says had happened by then.");
                }
            }

            Assert.True(offenders.Count == 0,
                $"Chart patterns on series {flavour} change once later bars arrive. A trigger or target " +
                $"the terminal has already spoken cannot move, and a formation cannot appear on a bar " +
                $"in the past:\n  " + string.Join("\n  ", offenders.Distinct().Take(20)));
        }

        // ── Does a formation change when OLDER bars arrive? ───────────────────────────────────

        [Theory]
        [MemberData(nameof(Flavours))]
        public void AFormationDoesNotChangeWhenOlderBarsArrive(int flavour)
        {
            var bars = CausalityProbeSeries.Bars(flavour, SeriesLength);
            var whole = Detect(bars);
            var offenders = new List<string>();
            int compared = 0;

            foreach (int d in new[] { 23, 91, 140 })
            {
                var shortRun = Detect(bars.Skip(d).ToList());
                var byIdentity = shortRun.ToDictionary(p => p.Identity);

                // Only formations that begin well clear of the shorter run's left edge: one that
                // starts at its second bar is missing the history it needs, and that is warmup
                // rather than a defect.
                foreach (var p in whole.Where(p => p.StartBarIndex >= d + Warmup))
                {
                    if (!byIdentity.TryGetValue(p.Identity, out var q))
                    {
                        offenders.Add($"{p.Kind} from {p.StartTime:yyyy-MM-dd HH:mm} to " +
                                      $"{p.EndTime:yyyy-MM-dd HH:mm} is on the chart, and disappears when " +
                                      $"{d} older bars are prepended. Scrolling back would delete it.");
                        continue;
                    }

                    compared++;
                    Geometry(p, q, d, int.MaxValue, offenders);

                    if (q.KnownAtIndex + d != p.KnownAtIndex)
                        offenders.Add($"{p.Kind} from {p.StartTime:yyyy-MM-dd HH:mm}: becomes knowable at a " +
                                      $"different bar once {d} older bars are prepended " +
                                      $"({q.KnownAtIndex + d} vs {p.KnownAtIndex}).");

                    if (Shift(q.CompletedAtIndex, d) != p.CompletedAtIndex)
                        offenders.Add($"{p.Kind} from {p.StartTime:yyyy-MM-dd HH:mm}: completion moves from " +
                                      $"{Show(p.CompletedAtIndex)} to {Show(Shift(q.CompletedAtIndex, d))} " +
                                      $"once {d} older bars are prepended.");
                }

                // And nothing may be INVENTED in the region both runs hold in full.
                foreach (var q in shortRun.Where(q => q.StartBarIndex >= Warmup))
                {
                    if (!whole.Any(p => p.Identity == q.Identity))
                        offenders.Add($"{q.Kind} from {q.StartTime:yyyy-MM-dd HH:mm} to " +
                                      $"{q.EndTime:yyyy-MM-dd HH:mm} is found only when {d} older bars are " +
                                      $"MISSING. Loading more history would make it vanish.");
                }
            }

            Assert.True(offenders.Count == 0,
                $"Chart patterns on series {flavour} change when older bars are prepended — a scroll-back " +
                $"rewrites what the terminal already said about the chart:\n  " +
                string.Join("\n  ", offenders.Distinct().Take(20)));

            // A version of this that matched nothing would pass every assertion above it. Series 0
            // is the quietest of the four and still clears this comfortably.
            Assert.True(compared >= 30,
                $"Only {compared} formations on series {flavour} were actually compared across the three " +
                $"drops. Either the warmup is excluding the series or the detector stopped finding things.");
        }

        // ── The guard's own honesty ───────────────────────────────────────────────────────────

        [Fact]
        public void TheseSeriesActuallyContainPatternsToCompare()
        {
            // Both theories above pass trivially against a series with no formations in it, and a
            // detector returning nothing at all would be the most reliable way to satisfy them.
            int total = 0;
            var kinds = new HashSet<ChartPatternKind>();
            foreach (int flavour in CausalityProbeSeries.AllFlavours)
            {
                var found = Detect(CausalityProbeSeries.Bars(flavour, SeriesLength));
                total += found.Count;
                foreach (var p in found) kinds.Add(p.Kind);
            }

            Assert.True(total > 200, $"Only {total} patterns across all four series — the comparison above " +
                                     $"has almost nothing to compare.");
            Assert.True(kinds.Count >= 8, $"Only {kinds.Count} distinct formation kinds are exercised: " +
                                          $"{string.Join(", ", kinds.OrderBy(k => k.ToString()))}.");
        }

        [Fact]
        public void IdentityIsIndexFreeAndKeyIsNot()
        {
            // The whole prepend comparison rests on this: two runs over the same dates produce the
            // same Identity and deliberately different Keys.
            var bars = CausalityProbeSeries.Bars(1, SeriesLength);
            var whole = Detect(bars);
            var shifted = Detect(bars.Skip(91).ToList());

            var shared = whole.Select(p => p.Identity).Intersect(shifted.Select(p => p.Identity)).ToList();
            Assert.NotEmpty(shared);

            var a = whole.First(p => p.Identity == shared[^1]);
            var b = shifted.First(p => p.Identity == shared[^1]);
            Assert.Equal(a.Identity, b.Identity);
            Assert.NotEqual(a.Key, b.Key);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The parts of a formation that describe its shape and the levels the terminal speaks.
        /// These are what the user acts on, so they are what may not move.
        /// </summary>
        private static void Geometry(ChartPattern p, ChartPattern q, int shift, int limit, List<string> offenders)
        {
            string where = shift == 0
                ? $"{p.Kind} at bars {p.StartBarIndex}-{p.EndBarIndex}"
                : $"{p.Kind} from {p.StartTime:yyyy-MM-dd HH:mm}";

            if (!Close(p.TriggerLevel, q.TriggerLevel))
                offenders.Add($"{where}: trigger level moves from {q.TriggerLevel:G8} to {p.TriggerLevel:G8}.");
            if (!Close(p.MeasuredTarget, q.MeasuredTarget))
                offenders.Add($"{where}: measured target moves from {Show(q.MeasuredTarget)} to {Show(p.MeasuredTarget)}.");
            if (!Close(p.SecondaryLevel, q.SecondaryLevel))
                offenders.Add($"{where}: second level moves from {Show(q.SecondaryLevel)} to {Show(p.SecondaryLevel)}.");
            if (p.BreaksBelow != q.BreaksBelow)
                offenders.Add($"{where}: the side it breaks reverses.");
            if (Shift(q.ExpiresAtIndex, shift) is int e && p.ExpiresAtIndex is int pe && e != pe && pe < limit)
                offenders.Add($"{where}: expiry moves from bar {pe} to bar {e}.");
        }

        private static int? Shift(int? v, int by) => v is null ? null : v + by;

        private static bool Close(double a, double b) =>
            (double.IsNaN(a) && double.IsNaN(b)) || Math.Abs(a - b) <= 1e-9 * Math.Max(1, Math.Abs(a));

        private static bool Close(double? a, double? b) =>
            (a is null && b is null) || (a is double x && b is double y && Close(x, y));

        private static string Show(int? v) => v?.ToString() ?? "none";
        private static string Show(double? v) => v?.ToString("G8") ?? "none";
    }
}
