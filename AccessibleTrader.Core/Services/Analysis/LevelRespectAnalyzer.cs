using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Analysis
{
    /// <summary>
    /// Measures how often price actually respected a line, instead of asking the user to judge
    /// it by eye. This is the objective replacement for "that level looks important" — and for a
    /// blind trader it is not a degraded substitute for looking at the chart, it is strictly more
    /// information than looking provides.
    /// </summary>
    public interface ILevelRespectAnalyzer
    {
        /// <summary>
        /// Scores each candidate over <paramref name="bars"/>. Candidates whose value array does
        /// not match the bar count are skipped rather than throwing — a caller assembling dozens
        /// of lines should not lose the whole report to one malformed entry.
        /// </summary>
        IReadOnlyList<RespectStats> Analyze(
            IReadOnlyList<Ohlcv> bars,
            IReadOnlyList<LineCandidate> candidates,
            RespectOptions? options = null);
    }

    /// <inheritdoc cref="ILevelRespectAnalyzer"/>
    public sealed class LevelRespectAnalyzer : ILevelRespectAnalyzer
    {
        public IReadOnlyList<RespectStats> Analyze(
            IReadOnlyList<Ohlcv> bars,
            IReadOnlyList<LineCandidate> candidates,
            RespectOptions? options = null)
        {
            var opts = options ?? RespectOptions.Default;
            var results = new List<RespectStats>(candidates.Count);
            if (bars.Count == 0) return results;

            var atr = IndicatorMath.Atr(bars.ToArray(), opts.AtrPeriod);

            foreach (var candidate in candidates)
            {
                if (candidate.Values.Length != bars.Count) continue;
                results.Add(AnalyzeOne(bars, atr, candidate, opts));
            }

            return results;
        }

        private static RespectStats AnalyzeOne(
            IReadOnlyList<Ohlcv> bars, double[] atr, LineCandidate candidate, RespectOptions opts)
        {
            var touches = new List<LineTouch>();
            // -1 means "nothing counted yet". Must not be int.MinValue: `i - lastCountedBar`
            // would overflow to a negative number and silently suppress every touch.
            int lastCountedBar = -1;

            for (int i = 1; i < bars.Count; i++)
            {
                double line = candidate.Values[i];
                double a = atr[i];
                if (double.IsNaN(line) || double.IsNaN(a) || a <= 0) continue;

                // De-duplicate: one approach that lingers is one interaction. Counting each bar
                // of a multi-bar consolidation against the line is how naive touch counters
                // manufacture significance out of chop.
                if (lastCountedBar >= 0 && i - lastCountedBar < opts.MinSeparationBars) continue;

                double tolerance = a * opts.TouchToleranceAtr;
                var bar = bars[i];

                // A touch is the bar's RANGE reaching the line's band — wicks count, because a
                // wick into a level and back out is precisely the interaction we care about.
                bool inRange = bar.Low <= line + tolerance && bar.High >= line - tolerance;
                if (!inRange) continue;

                // Which side price was on BEFORE the touch decides whether this is a support or
                // a resistance test. Using the previous close (not this bar's) avoids classifying
                // by the very bar that pierced the line.
                double prevClose = bars[i - 1].Close;
                if (Math.Abs(prevClose - line) < 1e-12) continue; // sitting exactly on it: no side
                bool fromAbove = prevClose > line;

                var (held, reactionAtr) = ResolveOutcome(bars, atr, candidate.Values, i, fromAbove, opts);

                // Cosasverdes' three-way taxonomy. Penetration is judged on the WICK: whether the
                // bar traded through the line at all, regardless of where it closed. A touch that
                // never penetrated and still rejected is the clean ricochet (2); one that
                // penetrated and was reclaimed is the sweep (1); anything that broke is 0.
                bool penetrated = fromAbove ? bar.Low < line : bar.High > line;
                var kind = !held ? InteractionKind.Through
                    : penetrated ? InteractionKind.Reclaim
                    : InteractionKind.Ricochet;

                touches.Add(new LineTouch(i, bar.Date, fromAbove, held, reactionAtr, kind));
                lastCountedBar = i;
            }

            int holds = touches.Count(t => t.Held);
            var holdReactions = touches.Where(t => t.Held).Select(t => t.ReactionAtr).ToList();

            int lastIdx = bars.Count - 1;
            double currentValue = candidate.Values[lastIdx];
            double lastAtr = atr[lastIdx];
            double distanceAtr = double.IsNaN(currentValue) || double.IsNaN(lastAtr) || lastAtr <= 0
                ? double.NaN
                : (bars[lastIdx].Close - currentValue) / lastAtr;

            var lastTouch = touches.Count > 0 ? touches[^1] : null;

            return new RespectStats(
                candidate.Id,
                candidate.Label,
                candidate.Kind,
                touches.Count,
                holds,
                touches.Count > 0 ? (double)holds / touches.Count : double.NaN,
                holdReactions.Count > 0 ? holdReactions.Average() : double.NaN,
                Median(holdReactions),
                touches.Count(t => t.Held && t.FromAbove),
                touches.Count(t => t.Held && !t.FromAbove),
                lastTouch?.Time,
                lastTouch != null ? lastIdx - lastTouch.BarIndex : -1,
                currentValue,
                distanceAtr,
                touches);
        }

        /// <summary>
        /// Decides a touch's outcome by walking forward at most <c>ReactionWindowBars</c>.
        ///
        /// A BREAK is a close beyond the line by more than <c>BreakCloseAtr</c>. Closes are used
        /// rather than wicks on purpose: a wick through and back is a sweep, which is the level
        /// working, not failing.
        ///
        /// A HOLD requires price to CLOSE back away from the line by at least
        /// <c>MinReactionAtr</c> before any such break close. Without that floor, drifting
        /// sideways along a line would score as respect.
        ///
        /// Both sides of the decision use closes, and that asymmetry with touch DETECTION (which
        /// uses wicks) is deliberate. A single bar that wicks deep through a level and closes far
        /// back on the original side is a sweep — the level working. Measuring that same bar's
        /// reaction from its HIGH would make a bar that wicked down and then closed straight
        /// through the level look identical to one that rejected off it, because both have the
        /// same high. Closes tell the two apart; wicks do not.
        ///
        /// A touch that neither breaks nor reacts far enough is NOT a hold — it is counted as an
        /// interaction that failed to produce a reaction, which is the honest reading.
        /// </summary>
        private static (bool Held, double ReactionAtr) ResolveOutcome(
            IReadOnlyList<Ohlcv> bars, double[] atr, double[] line,
            int touchIdx, bool fromAbove, RespectOptions opts)
        {
            double a = atr[touchIdx];
            if (double.IsNaN(a) || a <= 0) return (false, double.NaN);

            double breakDistance = a * opts.BreakCloseAtr;
            double best = 0;
            int end = Math.Min(bars.Count - 1, touchIdx + opts.ReactionWindowBars);

            for (int j = touchIdx; j <= end; j++)
            {
                double lineAtJ = line[j];
                if (double.IsNaN(lineAtJ)) break;

                var bar = bars[j];

                if (fromAbove)
                {
                    // Testing support: a close well BELOW the line breaks it.
                    if (bar.Close < lineAtJ - breakDistance)
                        return (false, best / a);
                    // Reaction is how far price CLOSED back above the line.
                    best = Math.Max(best, bar.Close - lineAtJ);
                }
                else
                {
                    if (bar.Close > lineAtJ + breakDistance)
                        return (false, best / a);
                    best = Math.Max(best, lineAtJ - bar.Close);
                }

                if (best >= a * opts.MinReactionAtr)
                    return (true, best / a);
            }

            // Survived the window without breaking, but never reacted far enough to call it a
            // bounce. Reported as a touch that did not hold.
            return (false, best / a);
        }

        private static double Median(List<double> values)
        {
            if (values.Count == 0) return double.NaN;
            var sorted = values.OrderBy(v => v).ToList();
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 1
                ? sorted[mid]
                : (sorted[mid - 1] + sorted[mid]) / 2.0;
        }
    }
}
