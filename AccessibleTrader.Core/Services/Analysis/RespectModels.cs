using System;
using System.Collections.Generic;

namespace AccessibleTrader.Core.Services.Analysis
{
    /// <summary>What kind of line a candidate is, so results can be grouped and narrated.</summary>
    public enum LineKind
    {
        /// <summary>A fixed price — round number, prior period high/low, drawn horizontal.</summary>
        Horizontal,
        /// <summary>A moving average computed on the chart's own timeframe.</summary>
        MovingAverage,
        /// <summary>A moving average computed on a HIGHER timeframe and stepped onto this chart.</summary>
        MultiTimeframeMovingAverage,
        /// <summary>A sloped line — trendline, channel edge, log-space origin line.</summary>
        Sloped,
        /// <summary>Anything contributed by an <c>ILevelProvider</c> (pivots, VPVR nodes, Ichimoku).</summary>
        Provider
    }

    /// <summary>
    /// A line to be measured, expressed as its price at every bar. Representing every candidate
    /// as a per-bar price array — rather than as a typed geometry — is what lets one analyzer
    /// score horizontals, moving averages, multi-timeframe averages and sloped origin lines with
    /// exactly the same code. <see cref="Values"/>[i] is NaN where the line is not yet defined
    /// (indicator warmup, or bars before a sloped line's anchor).
    /// </summary>
    public record LineCandidate(string Id, string Label, LineKind Kind, double[] Values);

    /// <summary>
    /// Tuning for <see cref="ILevelRespectAnalyzer"/>. All distances are in ATR multiples rather
    /// than percentages so the same settings behave consistently across a $0.00002 memecoin and
    /// a $100k index — and across the volatility eras of a single asset's history.
    /// </summary>
    /// <param name="AtrPeriod">ATR lookback used to normalise every distance.</param>
    /// <param name="TouchToleranceAtr">
    /// How close the bar's range must come to the line to count as a touch.
    /// </param>
    /// <param name="ReactionWindowBars">
    /// Bars after a touch in which the outcome (hold or break) is decided.
    /// </param>
    /// <param name="MinReactionAtr">
    /// How far price must travel back AWAY from the line, within the window, for a touch to count
    /// as a hold. Without this floor, "price wandered sideways" would score as respect.
    /// </param>
    /// <param name="BreakCloseAtr">
    /// How far beyond the line a CLOSE must be to call the touch a break. Closes are used rather
    /// than wicks because a wick through a level and back is the classic sweep — the opposite of
    /// a break.
    /// </param>
    /// <param name="MinSeparationBars">
    /// Minimum bars between counted touches. A single approach that lingers for six bars is one
    /// interaction, not six, and counting it six times is the main way a naive touch-counter
    /// manufactures fake significance.
    /// </param>
    /// <param name="MinTouches">Candidates with fewer counted touches are reported but never ranked as reliable.</param>
    public record RespectOptions(
        int AtrPeriod = 14,
        double TouchToleranceAtr = 0.25,
        int ReactionWindowBars = 10,
        double MinReactionAtr = 1.0,
        double BreakCloseAtr = 0.5,
        int MinSeparationBars = 5,
        int MinTouches = 5)
    {
        public static RespectOptions Default { get; } = new();
    }

    /// <summary>
    /// How price interacted with a line on one touch. The three-way split — rather than a plain
    /// held/broke bool — is the "monodirectional interaction" taxonomy Cosasverdes teaches, and
    /// it is worth keeping distinct because the middle case is the one everybody's mental model
    /// gets wrong: a wick through a level followed by a reclaim is the level WORKING.
    /// </summary>
    public enum InteractionKind
    {
        /// <summary>Passed straight through with no meaningful reaction. Worth 0.</summary>
        Through,
        /// <summary>Penetrated the line, then reclaimed it and reacted away — a sweep. Worth 1.</summary>
        Reclaim,
        /// <summary>Came to the line and rejected without penetrating it at all. Worth 2.</summary>
        Ricochet
    }

    /// <summary>One counted interaction between price and a line.</summary>
    /// <param name="BarIndex">Bar at which the touch was detected.</param>
    /// <param name="Time">That bar's timestamp.</param>
    /// <param name="FromAbove">True when price approached the line from above (testing it as support).</param>
    /// <param name="Held">True when price reacted away without closing decisively through.</param>
    /// <param name="ReactionAtr">Maximum move away from the line within the window, in ATR.</param>
    /// <param name="Kind">Ricochet / reclaim / through.</param>
    public record LineTouch(int BarIndex, DateTime Time, bool FromAbove, bool Held, double ReactionAtr,
        InteractionKind Kind = InteractionKind.Through)
    {
        /// <summary>Ricochet 2, reclaim 1, through 0.</summary>
        public int Points => Kind switch
        {
            InteractionKind.Ricochet => 2,
            InteractionKind.Reclaim => 1,
            _ => 0
        };
    }

    /// <summary>
    /// How well one line was respected over the measured history.
    /// </summary>
    /// <param name="Touches">Counted interactions after de-duplication by <c>MinSeparationBars</c>.</param>
    /// <param name="Holds">Touches that reacted away without a decisive close through.</param>
    /// <param name="HoldRate">Holds / Touches, or NaN when there were no touches.</param>
    /// <param name="MeanReactionAtr">Mean reaction size across HOLDS only, in ATR.</param>
    /// <param name="MedianReactionAtr">Median reaction size across holds — resistant to one outlier bounce.</param>
    /// <param name="SupportHolds">Holds where price approached from above (level acted as support).</param>
    /// <param name="ResistanceHolds">Holds where price approached from below (level acted as resistance).</param>
    /// <param name="LastTouchTime">Timestamp of the most recent counted touch, if any.</param>
    /// <param name="BarsSinceLastTouch">Bars since the most recent touch, or -1 when never touched.</param>
    /// <param name="CurrentValue">The line's price on the last bar, or NaN.</param>
    /// <param name="DistanceAtr">Distance from the last close to the line, in ATR, or NaN.</param>
    public record RespectStats(
        string Id,
        string Label,
        LineKind Kind,
        int Touches,
        int Holds,
        double HoldRate,
        double MeanReactionAtr,
        double MedianReactionAtr,
        int SupportHolds,
        int ResistanceHolds,
        DateTime? LastTouchTime,
        int BarsSinceLastTouch,
        double CurrentValue,
        double DistanceAtr,
        IReadOnlyList<LineTouch> TouchDetail)
    {
        /// <summary>Clean rejections that never penetrated the line (2 points each).</summary>
        public int Ricochets { get { int n = 0; foreach (var t in TouchDetail) if (t.Kind == InteractionKind.Ricochet) n++; return n; } }

        /// <summary>Penetrate-then-reclaim sweeps (1 point each).</summary>
        public int Reclaims { get { int n = 0; foreach (var t in TouchDetail) if (t.Kind == InteractionKind.Reclaim) n++; return n; } }

        /// <summary>
        /// Mean interaction points per touch, 0..2. A line price merely drifts through scores near
        /// 0; one price genuinely defends scores near 2. NaN with no touches.
        /// </summary>
        public double MeanPoints
        {
            get
            {
                if (TouchDetail.Count == 0) return double.NaN;
                int total = 0;
                foreach (var t in TouchDetail) total += t.Points;
                return (double)total / TouchDetail.Count;
            }
        }

        /// <summary>
        /// True when the sample is large enough for the hold rate to mean anything. A 100% hold
        /// rate on two touches is noise, and presenting it beside a 71% rate on 34 touches would
        /// actively mislead — so ranking must gate on this, not on hold rate alone.
        /// </summary>
        public bool IsReliable(RespectOptions options) => Touches >= options.MinTouches;

        /// <summary>
        /// Ranking score: hold rate shrunk toward 0.5 by sample size, then weighted by how hard
        /// price reacted. The shrink is a Wilson-style prior — it is what stops a 2-for-2 line
        /// from outranking a 24-of-34 line. Returns NaN when there were no touches.
        /// </summary>
        public double Score
        {
            get
            {
                if (Touches <= 0) return double.NaN;
                // Add-4 smoothing: two pseudo-holds and two pseudo-breaks. At 0 real touches the
                // rate reads 0.5 (no information); the prior's influence decays as touches grow.
                double shrunk = (Holds + 2.0) / (Touches + 4.0);
                double reaction = double.IsNaN(MedianReactionAtr) ? 0 : Math.Min(MedianReactionAtr, 5.0);
                return shrunk * (1.0 + reaction);
            }
        }
    }
}
