using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Analysis
{
    /// <summary>The classical chart formations the terminal can describe.</summary>
    public enum ChartPatternKind
    {
        DoubleTop, DoubleBottom,
        HeadAndShoulders, InverseHeadAndShoulders,
        AscendingTriangle, DescendingTriangle, SymmetricalTriangle,
        RisingWedge, FallingWedge,
        BullFlag, BearFlag,

        /// <summary>
        /// A horizontal range — flat top, flat bottom, price rotating between them.
        ///
        /// <para>
        /// The most common state a market is in, and it was the one shape the detector could not
        /// name: the triangle grid handled every sloping combination and let flat-against-flat fall
        /// through. So the single most frequent thing a chart does produced silence, which is the
        /// worst possible gap in a feature whose job is to say what the chart is doing.
        /// </para>
        ///
        /// <para>
        /// It is the only kind with <b>two</b> live levels. Everything else has one line that
        /// confirms it; a range can break either way, and pretending otherwise by picking a side
        /// would be inventing a directional opinion out of a shape that is definitionally
        /// undecided.
        /// </para>
        /// </summary>
        Rectangle
    }

    /// <summary>
    /// Where a pattern is in its life. This distinction is the entire reason the feature is worth
    /// having rather than being a curiosity.
    /// </summary>
    public enum ChartPatternState
    {
        /// <summary>
        /// The structure is present and the level that would confirm it has NOT been reached. This
        /// is the only state that can be acted on, because by the time a pattern completes the move
        /// it names has already begun.
        /// </summary>
        Forming,

        /// <summary>
        /// Price has closed through the confirming level. Reported because "it did complete" is a
        /// fact worth hearing, and because it closes the loop on a <see cref="Forming"/> report the
        /// user already heard.
        /// <para>
        /// <b>This says nothing about whether the trade worked.</b> It is a statement about price
        /// crossing a line, not about the outcome of acting on it. The narrator therefore never
        /// speaks the bare word "completed" — it says which side of the level price closed on, so
        /// the user cannot mistake confirmation for profit.
        /// </para>
        /// </summary>
        Completed,

        /// <summary>
        /// The structure aged out without price ever closing through the trigger.
        ///
        /// <para>
        /// This is the third of the three things that can happen to a pattern and it used to be
        /// invisible: an unconfirmed pattern simply stayed <see cref="Forming"/> forever, so a
        /// double top from 2019 whose neckline never broke was still announced as a live decision.
        /// Worse, the old unbounded resolve would mark it <see cref="Completed"/> if the neckline
        /// happened to break two hundred bars later — an unrelated move wearing the pattern's name.
        /// </para>
        /// <para>
        /// Nothing here judges the pattern. "Did not confirm" is a fact about price, on the same
        /// footing as "confirmed", and both are stated the same way: what price did, relative to
        /// which level.
        /// </para>
        /// </summary>
        Expired
    }

    /// <param name="KnownAtIndex">
    /// The earliest bar at which this pattern's STRUCTURE could honestly have been announced. Every
    /// pivot it rests on is only knowable <c>Span</c> bars after it printed, so this is the maximum
    /// of those confirmation bars. Nothing may be announced before it. Without this the feature
    /// would tell a user about a shape that was not visible at the time, which is the same lookahead
    /// the Cipher SR proximity result turned out to be.
    /// <para>
    /// It is deliberately INDEPENDENT of whether the pattern later completed. An earlier version
    /// overwrote it with the break bar for completed patterns, which meant the same pattern reported
    /// a different "knowable at" depending on how much future data happened to be loaded — a
    /// property a lookahead test caught immediately and a user never would have.
    /// </para>
    /// </param>
    /// <param name="CompletedAtIndex">
    /// The bar that closed through <paramref name="TriggerLevel"/>, or null while the pattern is
    /// still forming. Always at or after <paramref name="KnownAtIndex"/>.
    /// </param>
    /// <param name="TriggerLevel">
    /// The price that confirms the pattern — the neckline of a top or a head-and-shoulders, the
    /// boundary of a triangle or flag. This is the actionable number: it is what a
    /// <see cref="ChartPatternState.Forming"/> report exists to hand over.
    /// </param>
    /// <param name="ExpiresAtIndex">
    /// The last bar at which the structure is still live. A pattern that has not confirmed by here
    /// is <see cref="ChartPatternState.Expired"/>, and a break after here belongs to some later
    /// move, not to this shape.
    /// <para>
    /// Set to the formation's own length past <paramref name="KnownAtIndex"/>: a triangle that
    /// built over forty bars stays context for forty more. Anchoring the decay to the pattern's own
    /// span rather than a fixed bar count is what keeps it proportionate on a 1-minute chart and a
    /// weekly one at the same time.
    /// </para>
    /// </param>
    /// <param name="BreaksBelow">
    /// Which way price must close through <paramref name="TriggerLevel"/> to confirm. Carried so
    /// the narrator can say <i>closed below the neckline</i> rather than the ambiguous
    /// <i>completed</i>, and so the measured target can be projected in the right direction.
    /// </param>
    /// <param name="MeasuredTarget">
    /// The conventional measured move: the height of the formation projected from
    /// <paramref name="TriggerLevel"/> in the break direction.
    /// <para>
    /// <b>This is geometry, not a forecast, and this project has never tested it.</b> It is
    /// arithmetic on two numbers already on the screen — the same status as the trigger level
    /// itself. It is reported because a trader asking "where does this thing go if it breaks?" is
    /// asking about a convention they already use, and answering it by ear is the terminal's job;
    /// the narrator states it as the convention's number and never as a prediction. If it were ever
    /// to be scored, it would first have to survive the same controls as everything in the edge
    /// registry — and every price-derived pattern claim tested here so far has come back null.
    /// </para>
    /// </param>
    public record ChartPattern(
        ChartPatternKind Kind,
        ChartPatternState State,
        int StartBarIndex,
        int EndBarIndex,
        int KnownAtIndex,
        double TriggerLevel,
        DateTime StartTime,
        DateTime EndTime,
        int? CompletedAtIndex = null,
        int? ExpiresAtIndex = null,
        bool BreaksBelow = true,
        double? MeasuredTarget = null,
        double? SecondaryLevel = null)
    {
        /// <summary>
        /// <see cref="ExpiresAtIndex"/>, falling back to the formation-length rule when a caller
        /// (or an older test) constructed the record without one.
        /// </summary>
        public int RelevanceEndsAt =>
            ExpiresAtIndex ?? KnownAtIndex + Math.Max(1, EndBarIndex - StartBarIndex);

        /// <summary>
        /// The bar at which this pattern's story finishes — the break bar if it confirmed,
        /// otherwise the bar it aged out on. This is the bar the narrator speaks its outcome at,
        /// and the last bar at which it is worth mentioning at all.
        /// </summary>
        public int ResolvesAt => CompletedAtIndex ?? RelevanceEndsAt;

        /// <summary>
        /// Stable identity for one formation, independent of the life stage it is being described
        /// at.
        ///
        /// <para>
        /// Needed because the narrator projects a pattern back to the state it held at a given bar,
        /// which produces a different record for the same shape on different bars. Diffing the
        /// records themselves would then report every formation as newly entered on the bar it
        /// resolved — announcing "start of" at the finish line.
        /// </para>
        /// </summary>
        public (ChartPatternKind, int, int, int) Key => (Kind, StartBarIndex, EndBarIndex, KnownAtIndex);

        /// <summary>
        /// The same formation, identified without reference to where the array starts.
        ///
        /// <para>
        /// <see cref="Key"/> is built from bar indices, which is right for everything inside one
        /// loaded range and useless across two. Scrolling back prepends older bars and every index
        /// on the chart shifts; the formation the user is looking at is the same shape on the same
        /// two dates, but its Key is now a different tuple. So "is this the pattern I found before
        /// the scroll-back?" cannot be asked with Key, which is why the prepend half of the
        /// causality contract had nothing to compare on and was never written.
        /// </para>
        /// </summary>
        public (ChartPatternKind, DateTime, DateTime) Identity => (Kind, StartTime, EndTime);
    }

    /// <param name="Span">Bars either side a pivot must dominate. Passed through to the swing analyzer.</param>
    /// <param name="ToleranceAtr">
    /// How close two highs (or two lows) must be to count as "equal", in ATR. Fixed percentages
    /// break across instruments — 1% is nothing on a small cap and enormous on a bond ETF — so the
    /// instrument's own volatility sets the scale.
    /// </param>
    /// <param name="MinPatternBars">
    /// Shortest formation worth naming. Below this the "pattern" is three bars of noise, and naming
    /// noise in a voice that sounds authoritative is worse than silence.
    /// </param>
    /// <param name="MaxPatternBars">
    /// Longest formation to look back over. A double top whose two highs are two years apart is not
    /// a double top, it is two highs.
    /// </param>
    public record ChartPatternOptions(
        int Span = 5,
        double ToleranceAtr = 0.75,
        int MinPatternBars = 12,
        int MaxPatternBars = 160,
        double MinSwingAtr = 1.0)
    {
        public static ChartPatternOptions Default { get; } = new();
    }

    public interface IChartPatternDetector
    {
        /// <summary>
        /// Every pattern present in <paramref name="bars"/>, ordered by when it became knowable.
        /// </summary>
        IReadOnlyList<ChartPattern> Detect(IReadOnlyList<Ohlcv> bars, ChartPatternOptions? options = null);
    }

    /// <summary>
    /// Finds classical chart formations and reports them with a life stage.
    ///
    /// <para>
    /// ── What this is for, and what it is emphatically not ───────────────────────
    /// This is an <b>accessibility feature</b>. "Price has made two highs at roughly the same level
    /// with a trough between them" is what a sighted person reads off a chart in one glance, and
    /// delivering that by ear is the terminal's reason to exist. It is <b>description, never a
    /// score and never a signal</b>, which is the same rule the strategy library enforces and for
    /// the same reason: a marker in the product's own UI reads as the product's endorsement.
    /// </para>
    ///
    /// <para>
    /// The evidence says that is the correct posture. Every price-derived pattern claim tested in
    /// this project has come back null — a random horizontal line was respected 59% of the time,
    /// real swing levels held 46.2% against 46.7% for random lines, fib ratios did nothing across
    /// 355,000 tests, and structure labels were indistinguishable from random in the confluence
    /// work. So the detector names shapes and states facts about them. It never says what happens
    /// next. (The one claim that would have to be true before a directional hint could be added —
    /// "ascending triangles break up" — is queued as <c>triangle-direction-bias</c> and untested.)
    /// </para>
    ///
    /// <para>
    /// ── FORMING is the point ────────────────────────────────────────────────────
    /// A detector that only reports completed patterns is useless: by the time a head and shoulders
    /// completes, the neckline has broken and the move it names is underway. So every pattern is
    /// reported from the moment its structure is knowable, as <see cref="ChartPatternState.Forming"/>,
    /// carrying the <see cref="ChartPattern.TriggerLevel"/> that would confirm it. That is the number
    /// a person can act on, and it is available before the event rather than after.
    /// </para>
    ///
    /// <para>
    /// ── NO LOOKAHEAD ────────────────────────────────────────────────────────────
    /// Built on <see cref="ISwingStructureAnalyzer"/>, whose pivots carry a
    /// <see cref="SwingPoint.ConfirmedAtIndex"/> — the bar at which the pivot could first be KNOWN,
    /// which is Span bars after it printed. Every pattern's <see cref="ChartPattern.KnownAtIndex"/>
    /// is derived from those, never from the pivot bars themselves. This is not a theoretical
    /// nicety: a level anchored at a pivot only knowable N bars later is exactly what made the
    /// Cipher SR proximity result a lookahead artifact.
    /// </para>
    /// </summary>
    public sealed class ChartPatternDetector : IChartPatternDetector
    {
        private readonly ISwingStructureAnalyzer _swings;

        public ChartPatternDetector(ISwingStructureAnalyzer swings) => _swings = swings;

        public IReadOnlyList<ChartPattern> Detect(IReadOnlyList<Ohlcv> bars, ChartPatternOptions? options = null)
        {
            var o = options ?? ChartPatternOptions.Default;
            var found = new List<ChartPattern>();
            if (bars == null || bars.Count < o.MinPatternBars + o.Span * 2) return found;

            var atr = Atr(bars, 14);
            var structure = _swings.Analyze(bars, new SwingOptions(o.Span, o.MinSwingAtr));
            var swings = structure.Swings;
            if (swings.Count < 3) return found;

            var highs = swings.Where(s => s.IsHigh).ToList();
            var lows = swings.Where(s => !s.IsHigh).ToList();

            found.AddRange(DoubleTops(bars, highs, lows, atr, o));
            found.AddRange(DoubleBottoms(bars, highs, lows, atr, o));
            found.AddRange(HeadAndShoulders(bars, highs, lows, atr, o, inverse: false));
            found.AddRange(HeadAndShoulders(bars, lows, highs, atr, o, inverse: true));
            found.AddRange(TrianglesAndWedges(bars, highs, lows, atr, o));
            found.AddRange(Flags(bars, atr, o));

            return found
                .OrderBy(p => p.KnownAtIndex)
                .ThenBy(p => p.Kind)
                .ToList();
        }

        // ── Double top / bottom ─────────────────────────────────────────────────

        private static IEnumerable<ChartPattern> DoubleTops(
            IReadOnlyList<Ohlcv> bars, List<SwingPoint> highs, List<SwingPoint> lows,
            double[] atr, ChartPatternOptions o)
        {
            for (int i = 1; i < highs.Count; i++)
            {
                var a = highs[i - 1];
                var b = highs[i];
                int width = b.BarIndex - a.BarIndex;
                if (width < o.MinPatternBars || width > o.MaxPatternBars) continue;

                double tol = atr[b.BarIndex] * o.ToleranceAtr;
                if (tol <= 0 || Math.Abs(a.Price - b.Price) > tol) continue;

                // The trough between them is the neckline. Without one there is no pattern, only two
                // highs that happen to be level.
                var trough = lows.Where(l => l.BarIndex > a.BarIndex && l.BarIndex < b.BarIndex)
                                 .OrderBy(l => l.Price).FirstOrDefault();
                if (trough == null) continue;
                if (Math.Min(a.Price, b.Price) - trough.Price < tol) continue;   // too shallow to be a double top

                int known = Math.Max(b.ConfirmedAtIndex, trough.ConfirmedAtIndex);
                int expires = Expiry(known, a.BarIndex, b.BarIndex);
                var p = Resolve(bars, known, expires, trough.Price, breakBelow: true);

                // Measured move: the depth from the twin highs down to the neckline, projected the
                // same distance below it.
                double target = trough.Price - (Math.Max(a.Price, b.Price) - trough.Price);

                yield return new ChartPattern(ChartPatternKind.DoubleTop, p.State,
                    a.BarIndex, b.BarIndex, known, trough.Price, a.Time, b.Time, p.CompletedAt,
                    expires, BreaksBelow: true, MeasuredTarget: target);
            }
        }

        private static IEnumerable<ChartPattern> DoubleBottoms(
            IReadOnlyList<Ohlcv> bars, List<SwingPoint> highs, List<SwingPoint> lows,
            double[] atr, ChartPatternOptions o)
        {
            for (int i = 1; i < lows.Count; i++)
            {
                var a = lows[i - 1];
                var b = lows[i];
                int width = b.BarIndex - a.BarIndex;
                if (width < o.MinPatternBars || width > o.MaxPatternBars) continue;

                double tol = atr[b.BarIndex] * o.ToleranceAtr;
                if (tol <= 0 || Math.Abs(a.Price - b.Price) > tol) continue;

                var peak = highs.Where(h => h.BarIndex > a.BarIndex && h.BarIndex < b.BarIndex)
                                .OrderByDescending(h => h.Price).FirstOrDefault();
                if (peak == null) continue;
                if (peak.Price - Math.Max(a.Price, b.Price) < tol) continue;

                int known = Math.Max(b.ConfirmedAtIndex, peak.ConfirmedAtIndex);
                int expires = Expiry(known, a.BarIndex, b.BarIndex);
                var p = Resolve(bars, known, expires, peak.Price, breakBelow: false);

                double target = peak.Price + (peak.Price - Math.Min(a.Price, b.Price));

                yield return new ChartPattern(ChartPatternKind.DoubleBottom, p.State,
                    a.BarIndex, b.BarIndex, known, peak.Price, a.Time, b.Time, p.CompletedAt,
                    expires, BreaksBelow: false, MeasuredTarget: target);
            }
        }

        // ── Head and shoulders ──────────────────────────────────────────────────

        /// <summary>
        /// Three peaks with the middle one highest and the outer two roughly level; the neckline is
        /// the higher of the two intervening troughs, which is the level a break must clear.
        /// Inverted by swapping the roles of highs and lows, because the inverse pattern is the same
        /// geometry reflected — writing it twice is how the two drift apart.
        /// </summary>
        private static IEnumerable<ChartPattern> HeadAndShoulders(
            IReadOnlyList<Ohlcv> bars, List<SwingPoint> peaks, List<SwingPoint> troughs,
            double[] atr, ChartPatternOptions o, bool inverse)
        {
            int sign = inverse ? -1 : 1;

            for (int i = 2; i < peaks.Count; i++)
            {
                var left = peaks[i - 2];
                var head = peaks[i - 1];
                var right = peaks[i];

                int width = right.BarIndex - left.BarIndex;
                if (width < o.MinPatternBars || width > o.MaxPatternBars) continue;

                double tol = atr[right.BarIndex] * o.ToleranceAtr;
                if (tol <= 0) continue;

                // The head must clearly exceed both shoulders, and the shoulders must be level with
                // each other. "Clearly" is one tolerance unit — without it, any three peaks with a
                // marginally taller middle one qualify.
                if ((head.Price - left.Price) * sign < tol) continue;
                if ((head.Price - right.Price) * sign < tol) continue;
                if (Math.Abs(left.Price - right.Price) > tol * 1.5) continue;

                var t1 = troughs.Where(t => t.BarIndex > left.BarIndex && t.BarIndex < head.BarIndex).ToList();
                var t2 = troughs.Where(t => t.BarIndex > head.BarIndex && t.BarIndex < right.BarIndex).ToList();
                if (t1.Count == 0 || t2.Count == 0) continue;

                // The neckline is the trough closest to being broken — the conservative choice, so
                // "completed" is never announced before a break that actually matters.
                double neck = inverse
                    ? Math.Min(t1.Min(t => t.Price), t2.Min(t => t.Price))
                    : Math.Max(t1.Max(t => t.Price), t2.Max(t => t.Price));

                int known = new[] { right.ConfirmedAtIndex, t1.Max(t => t.ConfirmedAtIndex), t2.Max(t => t.ConfirmedAtIndex) }.Max();
                int expires = Expiry(known, left.BarIndex, right.BarIndex);
                var p = Resolve(bars, known, expires, neck, breakBelow: !inverse);

                // Head-to-neckline, projected from the neckline the other way.
                double height = Math.Abs(head.Price - neck);
                double target = inverse ? neck + height : neck - height;

                yield return new ChartPattern(
                    inverse ? ChartPatternKind.InverseHeadAndShoulders : ChartPatternKind.HeadAndShoulders,
                    p.State, left.BarIndex, right.BarIndex, known, neck, left.Time, right.Time, p.CompletedAt,
                    expires, BreaksBelow: !inverse, MeasuredTarget: target);
            }
        }

        // ── Triangles and wedges ────────────────────────────────────────────────

        /// <summary>
        /// Triangles and wedges are the same measurement — the slope of the last two highs against
        /// the slope of the last two lows — read off a 3x3 grid of sign combinations. Splitting them
        /// into separate detectors would mean maintaining the same geometry five times.
        ///
        /// <para>
        /// A slope counts as flat when the two swings differ by less than one tolerance unit, which
        /// is what stops a barely-sloping line from being called ascending.
        /// </para>
        /// </summary>
        private static IEnumerable<ChartPattern> TrianglesAndWedges(
            IReadOnlyList<Ohlcv> bars, List<SwingPoint> highs, List<SwingPoint> lows,
            double[] atr, ChartPatternOptions o)
        {
            if (highs.Count < 2 || lows.Count < 2) yield break;

            for (int i = 1; i < highs.Count; i++)
            {
                var h1 = highs[i - 1];
                var h2 = highs[i];

                // The two most recent lows that finished before this high pair did.
                var l = lows.Where(x => x.BarIndex < h2.BarIndex && x.BarIndex > h1.BarIndex - o.MaxPatternBars)
                            .OrderBy(x => x.BarIndex).ToList();
                if (l.Count < 2) continue;
                var l1 = l[^2];
                var l2 = l[^1];

                int start = Math.Min(h1.BarIndex, l1.BarIndex);
                int end = Math.Max(h2.BarIndex, l2.BarIndex);
                int width = end - start;
                if (width < o.MinPatternBars || width > o.MaxPatternBars) continue;

                double tol = atr[end] * o.ToleranceAtr;
                if (tol <= 0) continue;

                int hs = Sign(h2.Price - h1.Price, tol);
                int ls = Sign(l2.Price - l1.Price, tol);

                ChartPatternKind? kind = (hs, ls) switch
                {
                    (0, 0) => ChartPatternKind.Rectangle,              // flat top, flat bottom
                    (0, +1) => ChartPatternKind.AscendingTriangle,     // flat top, rising lows
                    (-1, 0) => ChartPatternKind.DescendingTriangle,    // falling highs, flat bottom
                    (-1, +1) => ChartPatternKind.SymmetricalTriangle,  // converging both sides
                    (+1, +1) => ChartPatternKind.RisingWedge,
                    (-1, -1) => ChartPatternKind.FallingWedge,
                    _ => null
                };
                if (kind == null) continue;

                // ── Range: two live levels, and no assumed break direction ──────
                if (kind == ChartPatternKind.Rectangle)
                {
                    double top = Math.Max(h1.Price, h2.Price);
                    double bottom = Math.Min(l1.Price, l2.Price);
                    double height = top - bottom;

                    // A range has to be tall enough to trade inside. Below two tolerance units the
                    // "range" is a flat line, and naming a flat line a formation is noise wearing a
                    // technical word.
                    if (height < tol * 2) continue;

                    int rk = new[] { h2.ConfirmedAtIndex, l2.ConfirmedAtIndex }.Max();
                    int rexp = Expiry(rk, start, end);
                    var rr = ResolveRange(bars, rk, rexp, top, bottom);

                    // The target only exists once a side has broken — while price is still inside,
                    // projecting one would mean picking a direction the shape has not picked.
                    double? rtarget = rr.State == ChartPatternState.Completed
                        ? (rr.BrokeBelow ? bottom - height : top + height)
                        : null;

                    yield return new ChartPattern(ChartPatternKind.Rectangle, rr.State, start, end, rk,
                        top, bars[Math.Min(start, bars.Count - 1)].Date, bars[Math.Min(end, bars.Count - 1)].Date,
                        rr.CompletedAt, rexp, BreaksBelow: rr.BrokeBelow, MeasuredTarget: rtarget,
                        SecondaryLevel: bottom);
                    continue;
                }

                // A wedge must actually converge — both boundaries sloping the same way is not
                // enough, the gap between them has to be closing, or this is just a channel.
                if (kind is ChartPatternKind.RisingWedge or ChartPatternKind.FallingWedge)
                {
                    double openGap = Math.Abs(h1.Price - l1.Price);
                    double closeGap = Math.Abs(h2.Price - l2.Price);
                    if (closeGap >= openGap) continue;
                }

                // The boundary a break would cross first. Ascending triangles and rising wedges are
                // read against their flat/upper edge; the others against the lower.
                bool breakBelow = kind is ChartPatternKind.DescendingTriangle
                                       or ChartPatternKind.RisingWedge;
                double trigger = breakBelow ? l2.Price : h2.Price;

                int known = new[] { h2.ConfirmedAtIndex, l2.ConfirmedAtIndex }.Max();
                int expires = Expiry(known, start, end);
                var p = Resolve(bars, known, expires, trigger, breakBelow);

                // The convention for a triangle or wedge is the widest part of the formation —
                // its opening mouth — projected from whichever boundary breaks.
                double mouth = Math.Abs(h1.Price - l1.Price);
                double target = breakBelow ? trigger - mouth : trigger + mouth;

                yield return new ChartPattern(kind.Value, p.State, start, end, known, trigger,
                    bars[Math.Min(start, bars.Count - 1)].Date, bars[Math.Min(end, bars.Count - 1)].Date,
                    p.CompletedAt, expires, breakBelow, target);
            }
        }

        // ── Flags and pennants ──────────────────────────────────────────────────

        /// <summary>
        /// A sharp directional impulse (the pole) followed by a shallow drift against it (the flag).
        /// Measured on bars rather than swings, because a flag is usually too short to contain
        /// confirmed pivots — which also means its "knowable at" bar is simply the last bar of the
        /// consolidation, with no pivot confirmation to wait for.
        /// </summary>
        private static IEnumerable<ChartPattern> Flags(
            IReadOnlyList<Ohlcv> bars, double[] atr, ChartPatternOptions o)
        {
            const int PoleBars = 8;
            const int FlagMin = 4;
            const int FlagMax = 20;
            const double PoleAtr = 4.0;          // the impulse must be genuinely large
            const double FlagRetraceMax = 0.5;   // and the drift genuinely shallow

            for (int end = PoleBars + FlagMin; end < bars.Count; end++)
            {
                for (int flagLen = FlagMin; flagLen <= FlagMax; flagLen++)
                {
                    int flagStart = end - flagLen + 1;
                    int poleStart = flagStart - PoleBars;
                    if (poleStart < 1) break;

                    double a = atr[flagStart];
                    if (a <= 0) continue;

                    double pole = bars[flagStart - 1].Close - bars[poleStart].Close;
                    if (Math.Abs(pole) < PoleAtr * a) continue;

                    bool bull = pole > 0;

                    var flag = bars.Skip(flagStart).Take(flagLen).ToList();
                    double flagHigh = flag.Max(b => b.High);
                    double flagLow = flag.Min(b => b.Low);

                    // Shallow: the consolidation must not give back much of the pole...
                    double retrace = bull
                        ? (bars[flagStart - 1].Close - flagLow) / Math.Abs(pole)
                        : (flagHigh - bars[flagStart - 1].Close) / Math.Abs(pole);
                    if (retrace < 0 || retrace > FlagRetraceMax) continue;

                    // ...and must be tight relative to the impulse that preceded it.
                    if (flagHigh - flagLow > Math.Abs(pole) * FlagRetraceMax) continue;

                    double trigger = bull ? flagHigh : flagLow;
                    int flagEnd = end;
                    int expires = Expiry(flagEnd, poleStart, end);
                    var p = Resolve(bars, flagEnd, expires, trigger, breakBelow: !bull);

                    // The flag's convention is the pole re-flown from the breakout. `pole` already
                    // carries the sign, so one expression covers both directions.
                    double target = trigger + pole;

                    yield return new ChartPattern(
                        bull ? ChartPatternKind.BullFlag : ChartPatternKind.BearFlag,
                        p.State, poleStart, end, flagEnd, trigger,
                        bars[poleStart].Date, bars[end].Date, p.CompletedAt,
                        expires, BreaksBelow: !bull, MeasuredTarget: target);

                    end += flagLen;   // one flag per region — otherwise every length reports the same shape
                    break;
                }
            }
        }

        // ── Shared ──────────────────────────────────────────────────────────────

        /// <summary>
        /// The last bar at which a formation spanning <paramref name="start"/>..<paramref name="end"/>
        /// and knowable at <paramref name="known"/> is still live. Kept as one expression so the
        /// detector and the narrator can never disagree about when a pattern stops mattering.
        /// </summary>
        internal static int Expiry(int known, int start, int end)
            => known + Math.Max(1, end - start);

        /// <summary>
        /// Decide what became of a pattern known at <paramref name="knownAt"/>: did it confirm,
        /// is it still live, or did it age out?
        ///
        /// <para>
        /// The scan starts AT the knowable bar, never before it. A pattern is Forming until a bar
        /// CLOSES through the trigger — closes rather than wicks, because a wick through a level is
        /// the single most common way a pattern reader is faked out, and reporting it as complete
        /// would make the feature actively misleading.
        /// </para>
        ///
        /// <para>
        /// The scan also STOPS at <paramref name="expiresAt"/>, and that bound is what makes the
        /// three states mean anything. Without it the scan ran to the end of the series, so a
        /// double top whose neckline broke two hundred bars later was reported as that double top
        /// completing — attributing an unrelated move to a shape that had long since stopped being
        /// the reason for it. It also meant nothing could ever be reported as having failed to
        /// confirm, because every pattern was either Completed or waiting forever.
        /// </para>
        ///
        /// <para>
        /// Forming is returned only when the series has not yet reached the expiry bar — i.e. the
        /// verdict genuinely is not in yet. That keeps the hedged, actionable wording for the one
        /// case that deserves it: a decision still open at the right-hand edge of the chart.
        /// </para>
        /// </summary>
        private static (ChartPatternState State, int? CompletedAt) Resolve(
            IReadOnlyList<Ohlcv> bars, int knownAt, int expiresAt, double trigger, bool breakBelow)
        {
            if (knownAt >= bars.Count) return (ChartPatternState.Forming, null);

            int last = Math.Min(expiresAt, bars.Count - 1);
            for (int i = knownAt; i <= last; i++)
            {
                bool through = breakBelow ? bars[i].Close < trigger : bars[i].Close > trigger;
                if (through) return (ChartPatternState.Completed, i);
            }

            return bars.Count - 1 > expiresAt
                ? (ChartPatternState.Expired, null)
                : (ChartPatternState.Forming, null);
        }

        /// <summary>
        /// A range resolves on whichever boundary breaks FIRST, and reports which one it was.
        ///
        /// <para>
        /// The single-trigger <see cref="Resolve"/> cannot express this: it is handed a direction
        /// up front. A range has not chosen a direction — that is what makes it a range — so the
        /// direction is an OUTPUT here rather than an input. Scanning for one side only would
        /// silently mis-report every break the other way as the range still being intact.
        /// </para>
        /// </summary>
        private static (ChartPatternState State, int? CompletedAt, bool BrokeBelow) ResolveRange(
            IReadOnlyList<Ohlcv> bars, int knownAt, int expiresAt, double top, double bottom)
        {
            if (knownAt >= bars.Count) return (ChartPatternState.Forming, null, false);

            int last = Math.Min(expiresAt, bars.Count - 1);
            for (int i = knownAt; i <= last; i++)
            {
                if (bars[i].Close > top) return (ChartPatternState.Completed, i, false);
                if (bars[i].Close < bottom) return (ChartPatternState.Completed, i, true);
            }

            return bars.Count - 1 > expiresAt
                ? (ChartPatternState.Expired, null, false)
                : (ChartPatternState.Forming, null, false);
        }

        private static int Sign(double delta, double tol)
            => delta > tol ? +1 : delta < -tol ? -1 : 0;

        /// <summary>
        /// Wilder ATR, carried forward. Every tolerance in this file is expressed in ATR so the
        /// detector behaves the same on a $3 small cap and a $600 index fund — a fixed percentage
        /// would make one of them fire constantly and the other never.
        /// </summary>
        private static double[] Atr(IReadOnlyList<Ohlcv> bars, int period)
        {
            int n = bars.Count;
            var atr = new double[n];
            if (n == 0) return atr;

            double sum = 0;
            for (int i = 1; i < n; i++)
            {
                double tr = Math.Max(bars[i].High - bars[i].Low,
                            Math.Max(Math.Abs(bars[i].High - bars[i - 1].Close),
                                     Math.Abs(bars[i].Low - bars[i - 1].Close)));
                if (i <= period)
                {
                    sum += tr;
                    atr[i] = sum / i;
                }
                else
                {
                    atr[i] = (atr[i - 1] * (period - 1) + tr) / period;
                }
            }
            atr[0] = atr.Length > 1 ? atr[1] : 0;
            return atr;
        }
    }
}
