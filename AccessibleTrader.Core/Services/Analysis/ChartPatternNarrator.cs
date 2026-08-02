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
    /// assembled at each call site. Four rules, and each exists because breaking it would turn a
    /// description into a prediction — or, in the case of the third, already had:
    /// </para>
    ///
    /// <list type="number">
    ///   <item>
    ///     <b>Every live pattern is hedged — "possible", and "forming".</b> The structure is real;
    ///     the completion is not. A confident-sounding announcement in a domain where sounding
    ///     confident is most of the con is worse than saying nothing.
    ///   </item>
    ///   <item>
    ///     <b>The trigger level is always spoken.</b> It is the actionable content in the sentence
    ///     and the reason the feature reports forming patterns at all. "Possible double top in
    ///     progress" without the neckline is trivia; with it, it is a level to watch.
    ///   </item>
    ///   <item>
    ///     <b>An outcome is stated as what price DID, never as a verdict.</b> The word "completed"
    ///     was ambiguous in exactly the way that matters: a user hearing it could not tell whether
    ///     the pattern had worked out or failed. It says neither — it only ever meant "price closed
    ///     through the line" — so the narrator now says that instead, naming the side and the level.
    ///     A pattern that ages out unconfirmed is reported the same way, as the level having held.
    ///   </item>
    ///   <item>
    ///     <b>No pattern is ever called bullish or bearish.</b> The conventional readings — head and
    ///     shoulders is bearish, ascending triangles break up — are exactly the claims this project
    ///     has tested and failed to confirm. The queued edge <c>triangle-direction-bias</c> would
    ///     have to come back positive before a single directional word could be added here.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// The measured target is spoken because a target is arithmetic on numbers already on the
    /// screen and a trader asking "where does this go if it breaks?" is asking about a convention
    /// they already use. It is always introduced as the <i>measured</i> target and always tied to
    /// <i>if it breaks</i>, which is the difference between reporting a convention and endorsing
    /// it. See <see cref="ChartPattern.MeasuredTarget"/> — it has never been tested here.
    /// </para>
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
        /// One pattern as speech, with its levels.
        ///
        /// <para>Forming: "Possible double top forming, neckline 42,100, measured target 39,400 if it breaks."</para>
        /// <para>Confirmed: "Double top: price closed below the neckline at 42,100, measured target 39,400."</para>
        /// <para>Expired: "Double top did not confirm — the neckline at 42,100 held."</para>
        /// </summary>
        public static string Describe(ChartPattern p, Func<double, string> formatPrice)
        {
            string name = Name(p.Kind);
            string level = formatPrice(p.TriggerLevel);
            string role = TriggerName(p.Kind);
            string side = p.BreaksBelow ? "below" : "above";

            return p.State switch
            {
                ChartPatternState.Forming =>
                    $"Possible {name} forming, {role} {level}{Target(p, formatPrice, " if it breaks")}.",

                // Never the bare word "completed": name the side and the level so confirmation
                // cannot be mistaken for a good outcome.
                ChartPatternState.Completed =>
                    $"{Capitalize(name)}: price closed {side} the {role} at {level}{Target(p, formatPrice)}.",

                ChartPatternState.Expired =>
                    $"{Capitalize(name)} did not confirm — the {role} at {level} held.",

                _ => $"{Capitalize(name)}, {role} {level}."
            };
        }

        /// <summary>
        /// The measured-move clause, or nothing when the pattern carries no usable target.
        /// Never spoken for an expired pattern: there is no break to project from.
        ///
        /// <para>
        /// A target at or below zero is dropped. The projection is a subtraction, so on a
        /// low-priced instrument a tall formation can put the conventional target underneath zero —
        /// measuring live snapshots produced "measured target -0.0001" on a sub-cent coin. A
        /// negative price is not a conservative estimate, it is a number that cannot happen, and it
        /// is the kind of number a user might type into an order ticket. Saying nothing is the
        /// honest output: the convention has no answer here.
        /// </para>
        /// </summary>
        private static string Target(ChartPattern p, Func<double, string> formatPrice, string qualifier = "")
            => p.MeasuredTarget is double t && t > 0
                ? $", measured target {formatPrice(t)}{qualifier}"
                : "";

        /// <summary>
        /// What to say when the cursor crosses INTO a pattern's region.
        ///
        /// <para>
        /// The edge is named because a chart formation is a region, not a point, and stepping into
        /// one from the left is a different situation from stepping into it from the right. Moving
        /// forward in time the user meets the structure first and its outcome later, so the honest
        /// order is "start of … , here is the level to watch". Moving backward they meet the
        /// outcome first, and hearing "end of" tells them the shape they are about to walk through
        /// is already resolved. Without the edge word both cases sounded identical, and there was
        /// no way by ear to tell which way through the pattern you were travelling.
        /// </para>
        /// </summary>
        public static string DescribeEntry(ChartPattern p, bool movingRight, Func<double, string> formatPrice)
        {
            string edge = movingRight ? "Start of" : "End of";
            int span = Math.Max(1, p.EndBarIndex - p.StartBarIndex);
            return $"{edge} {Lowercase(Describe(p, formatPrice))} Spans {span} bars.";
        }

        /// <summary>
        /// The same formation as it stood at <paramref name="barIndex"/> — forming until the bar
        /// that resolved it.
        ///
        /// <para>
        /// <b>This is a no-lookahead rule, and it was violated in the first version.</b> A pattern
        /// record carries the outcome the whole series eventually produced, so describing it
        /// verbatim announced that outcome at every bar it overlapped — including its own starting
        /// bar. Walking onto the left edge of a formation produced "start of double top: price
        /// closed below the neckline", stating a break that had not happened yet and that nobody
        /// standing on that bar could have known about. Measuring the narration across real
        /// snapshots is what exposed it; every unit test passed, because each sentence was
        /// individually well-formed.
        /// </para>
        ///
        /// <para>
        /// It is the same discipline as <see cref="ChartPattern.KnownAtIndex"/>, one level up: that
        /// one governs whether a shape may be mentioned at a bar, this one governs what may be said
        /// about it there. Panning back through history has to reproduce what was actually knowable
        /// at the time, or the feature quietly teaches that charts were more legible than they were.
        /// </para>
        /// </summary>
        public static ChartPattern AsOf(ChartPattern p, int barIndex)
        {
            if (p.CompletedAtIndex is int done)
                return barIndex >= done ? p : p with { State = ChartPatternState.Forming };

            if (p.State == ChartPatternState.Expired)
                return barIndex >= p.RelevanceEndsAt ? p : p with { State = ChartPatternState.Forming };

            return p;
        }

        /// <summary>
        /// What to say on the bar where the pattern's story ends — the bar that broke the trigger,
        /// or the bar it aged out on. This is the announcement that closes the loop on a "start of"
        /// the user already heard, and it is the whole reason the outcome wording had to stop being
        /// the word "completed".
        /// </summary>
        public static string DescribeResolution(ChartPattern p, Func<double, string> formatPrice)
        {
            string name = Name(p.Kind);
            string level = formatPrice(p.TriggerLevel);
            string role = TriggerName(p.Kind);

            return p.State switch
            {
                ChartPatternState.Completed =>
                    $"{Capitalize(name)} confirmed here: closed {(p.BreaksBelow ? "below" : "above")} "
                    + $"the {role} at {level}{Target(p, formatPrice)}.",

                ChartPatternState.Expired =>
                    $"{Capitalize(name)} ends here without confirming — the {role} at {level} held.",

                // Still live at the right-hand edge: there is no outcome to report yet.
                _ => ""
            };
        }

        /// <summary>
        /// The patterns overlapping a bar, as one utterance.
        ///
        /// <para>
        /// Capped at <paramref name="max"/> because a chart region can satisfy four definitions at
        /// once, and reading all of them is how a user learns to tune the feature out. Ordered by
        /// <see cref="Dominance"/> so the one described in full is the one a sighted reader would
        /// name first.
        /// </para>
        /// </summary>
        public static string DescribeMany(IEnumerable<ChartPattern> patterns, Func<double, string> formatPrice, int max = 2)
        {
            var ordered = ByDominance(patterns).Take(max).ToList();
            if (ordered.Count == 0) return "";
            return string.Join(" ", ordered.Select(p => Describe(p, formatPrice)));
        }

        /// <summary>
        /// Patterns ranked by which one deserves the user's attention first.
        ///
        /// <para>
        /// Overlap is not a defect to be suppressed — a region genuinely can be an inverse head and
        /// shoulders and an ascending triangle at the same time, and two traders looking at it
        /// would disagree about which it is. What IS a defect is reading all of them at equal
        /// weight, which is what turns the feature into noise. So they are ranked and only the
        /// leader is described in full:
        /// </para>
        /// <list type="number">
        ///   <item><b>Live before resolved.</b> A forming pattern is a decision; a resolved one is
        ///         history.</item>
        ///   <item><b>Bigger structure first.</b> The formation spanning eighty bars is the shape
        ///         the chart is actually making; the twelve-bar flag inside it is a detail of that
        ///         shape. This is also the only tie-break available that is not a directional
        ///         opinion — ranking by "which pattern is more reliable" would be exactly the
        ///         untested claim the whole feature refuses to make.</item>
        ///   <item><b>More recently knowable first</b>, so the freshest reading of the same region
        ///         wins a tie.</item>
        /// </list>
        /// </summary>
        public static IEnumerable<ChartPattern> ByDominance(IEnumerable<ChartPattern> patterns)
            => patterns
                .OrderBy(p => p.State == ChartPatternState.Forming ? 0 : 1)
                .ThenByDescending(p => p.EndBarIndex - p.StartBarIndex)
                .ThenByDescending(p => p.KnownAtIndex)
                .ThenBy(p => p.Kind);

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
        /// coverage came out at exactly one bar per pattern. The bound is therefore
        /// <see cref="ChartPattern.ResolvesAt"/> — the break bar, or the bar the structure aged out.
        /// </para>
        /// </summary>
        /// <para>
        /// Every pattern is returned <see cref="AsOf"/> the requested bar, so a caller cannot
        /// accidentally announce an outcome that had not happened yet. The projection lives here
        /// rather than at each call site deliberately: there are three consumers, and honesty
        /// should not depend on each of them remembering to ask for it.
        /// </para>
        public static IReadOnlyList<ChartPattern> AtBar(IReadOnlyList<ChartPattern> all, int barIndex)
            => all.Where(p => barIndex >= p.KnownAtIndex && barIndex <= p.ResolvesAt)
                  .Select(p => AsOf(p, barIndex))
                  .ToList();

        private static string Capitalize(string s)
            => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

        /// <summary>
        /// Drop the leading capital so a sentence can be prefixed with "Start of" / "End of"
        /// without reading as two sentences jammed together.
        /// </summary>
        private static string Lowercase(string s)
            => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
    }
}
