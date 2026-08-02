using System;
using System.Collections.Generic;
using System.Linq;

namespace AccessibleTrader.Core.Services.Analysis
{
    /// <summary>
    /// Turns a <see cref="ChartPattern"/> into the sentence a user hears.
    ///
    /// <para>
    /// The wording carries the honesty of the feature, so it is centralised here rather than being
    /// assembled at each call site. Three rules, and each exists because breaking it would turn a
    /// description into a prediction:
    /// </para>
    ///
    /// <list type="number">
    ///   <item>
    ///     <b>Every forming pattern is hedged — "possible", and "in progress".</b> The structure is
    ///     real; the completion is not. A confident-sounding announcement in a domain where sounding
    ///     confident is most of the con is worse than saying nothing.
    ///   </item>
    ///   <item>
    ///     <b>The trigger level is always spoken.</b> It is the only actionable content in the
    ///     sentence and the reason the feature reports forming patterns at all. "Possible double top
    ///     in progress" without the neckline is trivia; with it, it is a level to watch.
    ///   </item>
    ///   <item>
    ///     <b>No pattern is ever called bullish or bearish, and no target is ever given.</b> The
    ///     conventional readings — head and shoulders is bearish, ascending triangles break up — are
    ///     exactly the claims this project has tested and failed to confirm. The queued edge
    ///     <c>triangle-direction-bias</c> would have to come back positive before a single
    ///     directional word could be added here.
    ///   </item>
    /// </list>
    /// </summary>
    public static class ChartPatternNarrator
    {
        public static string Name(ChartPatternKind kind) => kind switch
        {
            ChartPatternKind.DoubleTop => "double top",
            ChartPatternKind.DoubleBottom => "double bottom",
            ChartPatternKind.HeadAndShoulders => "head and shoulders",
            ChartPatternKind.InverseHeadAndShoulders => "inverse head and shoulders",
            ChartPatternKind.AscendingTriangle => "ascending triangle",
            ChartPatternKind.DescendingTriangle => "descending triangle",
            ChartPatternKind.SymmetricalTriangle => "symmetrical triangle",
            ChartPatternKind.RisingWedge => "rising wedge",
            ChartPatternKind.FallingWedge => "falling wedge",
            ChartPatternKind.BullFlag => "bull flag",
            ChartPatternKind.BearFlag => "bear flag",
            _ => kind.ToString()
        };

        /// <summary>The level's role, so the number that follows means something.</summary>
        private static string TriggerName(ChartPatternKind kind) => kind switch
        {
            ChartPatternKind.DoubleTop or ChartPatternKind.DoubleBottom
                or ChartPatternKind.HeadAndShoulders or ChartPatternKind.InverseHeadAndShoulders
                => "neckline",
            ChartPatternKind.BullFlag or ChartPatternKind.BearFlag => "flag edge",
            _ => "trigger"
        };

        /// <summary>
        /// One pattern as speech.
        ///
        /// <para>Forming: "Possible double top in progress, neckline 42,100."</para>
        /// <para>Completed: "Double top completed, neckline 42,100 broken."</para>
        /// </summary>
        public static string Describe(ChartPattern p, Func<double, string> formatPrice)
        {
            string name = Name(p.Kind);
            string level = formatPrice(p.TriggerLevel);
            string role = TriggerName(p.Kind);

            return p.State == ChartPatternState.Forming
                ? $"Possible {name} in progress, {role} {level}."
                : $"{char.ToUpperInvariant(name[0])}{name[1..]} completed, {role} {level} broken.";
        }

        /// <summary>
        /// The patterns overlapping a bar, as one utterance.
        ///
        /// <para>
        /// Capped at <paramref name="max"/> because a chart region can satisfy four definitions at
        /// once, and reading all of them is how a user learns to tune the feature out. Forming
        /// patterns are read first: a completed pattern is history, a forming one is a decision.
        /// </para>
        /// </summary>
        public static string DescribeMany(IEnumerable<ChartPattern> patterns, Func<double, string> formatPrice, int max = 2)
        {
            var ordered = patterns
                .OrderBy(p => p.State == ChartPatternState.Forming ? 0 : 1)
                .ThenByDescending(p => p.KnownAtIndex)
                .Take(max)
                .ToList();

            if (ordered.Count == 0) return "";
            return string.Join(" ", ordered.Select(p => Describe(p, formatPrice)));
        }

        /// <summary>
        /// The patterns that were knowable at <paramref name="barIndex"/> and whose structure spans
        /// it — i.e. what a sighted person would see on screen while looking at that bar.
        ///
        /// <para>
        /// The lower bound is <see cref="ChartPattern.KnownAtIndex"/> and it is not cosmetic: it
        /// stops the terminal describing, at bar 400, a shape whose final pivot was not confirmed
        /// until bar 430. Panning back through history must reproduce what was actually visible at
        /// the time, or the feature quietly teaches a false sense of how legible the chart was.
        /// </para>
        ///
        /// <para>
        /// The upper bound needs care. A pattern's own span always ENDS BEFORE it becomes knowable —
        /// confirmation lag guarantees it — so bounding by <c>EndBarIndex</c> collapses the window to
        /// the single bar <c>KnownAtIndex</c>, and the pattern is announced once, on one bar, and
        /// never when the user pans into the region it describes. That was the first implementation,
        /// and measuring how often the feature actually spoke on real snapshots is what exposed it:
        /// coverage came out at exactly one bar per pattern.
        /// </para>
        /// </summary>
        public static IReadOnlyList<ChartPattern> AtBar(IReadOnlyList<ChartPattern> all, int barIndex)
            => all.Where(p => barIndex >= p.KnownAtIndex && barIndex <= RelevantUntil(p)).ToList();

        /// <summary>
        /// The last bar at which a pattern is still worth mentioning.
        ///
        /// <para>
        /// A completed pattern stops being current the moment it resolves, so it is relevant up to
        /// the bar that broke its trigger. A forming one has no known end, so it stays relevant for
        /// as long again as it took to form — a triangle that built over forty bars is still the
        /// context forty bars later, and stale after that. Anchoring the decay to the formation's own
        /// length rather than a fixed bar count keeps it proportionate across timeframes.
        /// </para>
        /// </summary>
        private static int RelevantUntil(ChartPattern p)
        {
            if (p.CompletedAtIndex is int done) return done;
            int formationLength = Math.Max(1, p.EndBarIndex - p.StartBarIndex);
            return p.KnownAtIndex + formationLength;
        }
    }
}
