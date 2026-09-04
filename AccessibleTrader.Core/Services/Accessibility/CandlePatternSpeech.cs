using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// THE ONE PATH for candle-pattern detail: one classifier, one vocabulary, four routes.
    ///
    /// <para>
    /// Until 2026-09-04 there were THREE classifiers with three sets of thresholds and three
    /// spellings of the same names. <see cref="SdkCandlePatternAnalyzer"/> knew all twelve
    /// patterns and both bar-close routes used it; <c>BarDetailService.ClassifyBar</c>
    /// (Ctrl+Shift+D / Alt+Shift+D) and <c>SpeechFormatter.ClassifyCandleType</c> (the arrow-key
    /// candle summary) were single-bar-only copies with different numbers. So the two routes that
    /// read HISTORY — every bar the user was not present for — could not say "three white
    /// soldiers", "morning star" or "bullish engulfing" at all, and on the shapes they did share
    /// they could disagree with the live announcement about the same bar.
    /// </para>
    ///
    /// <para>
    /// Worse than the gap was the disagreement. A marubozu needed a 90% body to be one by the
    /// arrow keys and 95% by the analyser, so a 92% body was a marubozu when scanned and an
    /// ordinary candle when it closed. And hammer-vs-hanging-man — the same shape, opposite
    /// meanings — was decided by the trend in the analyser and not decided at all in the copies,
    /// which named every one of them a hammer. That is not a rounding difference; it announces
    /// the opposite direction to someone who cannot see the chart.
    /// </para>
    ///
    /// <para>
    /// Every method here is deliberately pure. The routes differ only in HOW MUCH of the analysis
    /// they read — the arrow keys take the shape phrase, the detail key adds the bias clause, the
    /// live bar takes the suffix form — never in what the shape IS called.
    /// </para>
    /// </summary>
    public static class CandlePatternSpeech
    {
        /// <summary>
        /// How many trailing bars to hand the analyser.
        ///
        /// <para>
        /// Three are needed for the pattern itself (a morning star is three bars) and
        /// <see cref="CandlePatternThresholds.TrendLookbackBars"/> more for the hammer/hanging-man
        /// decision, which defaults to 3. This is far above both so a caller never has to know the
        /// configured lookback, and it is small enough that building it on every arrow keypress
        /// costs nothing measurable next to the speech it feeds.
        /// </para>
        /// </summary>
        public const int ContextBars = 32;

        /// <summary>
        /// Classify the bar at <paramref name="index"/> of <paramref name="data"/>, as the user
        /// sees it drawn.
        ///
        /// <para>
        /// The whole point of routing every caller through here is that the trailing context is
        /// assembled ONCE and correctly. Two things are easy to get wrong and both have already
        /// happened in this repo: handing the analyser the classified bar as its own predecessor
        /// (an engulfing pattern tested against itself), and mixing raw bars into a Heikin-Ashi
        /// readout so a multi-bar pattern is judged on candles that are not the ones on screen.
        /// </para>
        ///
        /// <para>
        /// <paramref name="current"/> overrides the bar at the index when the caller already holds
        /// it — the live forming bar is not in <paramref name="data"/> at all, and the arrow-key
        /// formatter is handed the drawn bar by its caller. When it is supplied, the context is
        /// built from the bars BEFORE the index with this bar appended, so the analyser's trend
        /// window ends where it should.
        /// </para>
        /// </summary>
        public static CandleAnalysis AnalyzeAt(
            ISdkCandlePatternAnalyzer analyzer,
            IReadOnlyList<Ohlcv>? data,
            int index,
            bool heikinAshi,
            Ohlcv? current = null)
        {
            var context = ContextEndingAt(data, index, heikinAshi, current);

            if (context.Count == 0)
            {
                // Nothing to work with but the bar itself. The analyser copes — it simply cannot
                // reach any pattern that needs a predecessor — and a caller with a bar and no
                // series is a real case (tests, and the first bar of a fresh load).
                return current.HasValue
                    ? analyzer.Analyze(current.Value)
                    : analyzer.Analyze(default);
            }

            var bar   = context[^1];
            var prev  = context.Count >= 2 ? context[^2] : (Ohlcv?)null;
            var prev2 = context.Count >= 3 ? context[^3] : (Ohlcv?)null;

            return analyzer.Analyze(bar, prev, prev2, context);
        }

        /// <summary>
        /// Classify the bar that is still FORMING — the one route whose bar is not in the series
        /// yet, because the store only commits it when it closes.
        ///
        /// <para>
        /// It gets its own entry point rather than an index because there is no index to give:
        /// the live bar may or may not be in <paramref name="history"/> depending on whether the
        /// caller reads the store or the event. Appending it to the history first, and only then
        /// asking for the last bar as drawn, is also what makes the Heikin-Ashi answer right — an
        /// HA candle is derived from the HA candle before it, so a forming bar transformed in
        /// isolation is not the candle on screen.
        /// </para>
        ///
        /// <para>
        /// THE TRAILING DUPLICATE MATTERS. <c>WorkspaceStore</c> replaces the live bar IN PLACE,
        /// so the stored series already ends with an earlier snapshot of the bar now forming, and
        /// <c>IntraBarUpdateEvent.PreviousBar</c> is that snapshot rather than the bar before it.
        /// Appending without dropping it hands the analyser the forming bar as its own
        /// predecessor: a bullish engulfing is then tested against an earlier version of itself,
        /// which it engulfs by construction as soon as the body grows. The same mistake was found
        /// and fixed on the bar-close route in an earlier pass; the intra-bar route kept it,
        /// because the event's own field looked like the right thing to pass.
        /// </para>
        /// </summary>
        public static CandleAnalysis AnalyzeForming(
            ISdkCandlePatternAnalyzer analyzer,
            IReadOnlyList<Ohlcv>? history,
            Ohlcv forming,
            bool heikinAshi)
        {
            var combined = new List<Ohlcv>((history?.Count ?? 0) + 1);
            if (history != null) combined.AddRange(history);
            if (combined.Count > 0 && combined[^1].Date == forming.Date)
                combined.RemoveAt(combined.Count - 1);
            combined.Add(forming);
            return AnalyzeAt(analyzer, combined, combined.Count - 1, heikinAshi);
        }

        /// <summary>
        /// The trailing window ending at and INCLUDING the classified bar, oldest first, at most
        /// <see cref="ContextBars"/> long. Empty when there is no usable data.
        /// </summary>
        public static IReadOnlyList<Ohlcv> ContextEndingAt(
            IReadOnlyList<Ohlcv>? data, int index, bool heikinAshi, Ohlcv? current = null)
        {
            if (data == null || data.Count == 0)
                return current.HasValue ? new[] { current.Value } : Array.Empty<Ohlcv>();

            int idx = Math.Clamp(index, 0, data.Count - 1);

            if (!current.HasValue)
                return ChartMath.BarsAsDrawn(data, idx, ContextBars, heikinAshi);

            // The caller's own bar is the last one. Everything before it comes from the series so
            // the multi-bar patterns and the trend lookback have real predecessors to read.
            var history = idx >= 1
                ? ChartMath.BarsAsDrawn(data, idx - 1, ContextBars - 1, heikinAshi)
                : Array.Empty<Ohlcv>();

            var window = new List<Ohlcv>(history.Count + 1);
            window.AddRange(history);
            window.Add(current.Value);
            return window;
        }

        /// <summary>
        /// A bar's place inside a multi-bar pattern: which pattern, which bar of it this is, how
        /// many bars it spans, and the bar it completes on.
        /// </summary>
        public sealed record Membership(CandlePattern Pattern, int Position, int BarCount, DateTime CompletesAt);

        /// <summary>
        /// Which multi-bar pattern, if any, the bar at <paramref name="index"/> is PART OF —
        /// including when it is the first or second bar and the pattern only becomes visible
        /// later.
        ///
        /// <para>
        /// Cody, 2026-09-04: <i>"when alt shift d says 3 white soldiers, how do i know which
        /// candles are part of that formation because that's the only candle that reports that
        /// formation"</i>. He was exactly right. The analyser answers about ONE bar and a pattern
        /// is only recognisable on its LAST bar, so a three-bar shape was announced on one bar in
        /// three and the other two said nothing — leaving a listener who cannot see the chart with
        /// a name and no way to find the candles it refers to.
        /// </para>
        ///
        /// <para>
        /// THIS LOOKS FORWARD, AND THAT IS ONLY LEGITIMATE BECAUSE IT IS A READOUT OF HISTORY.
        /// Standing on a bar in the past, the bars after it have already happened and the user can
        /// arrow to them; saying "bar 1 of 3" is describing the chart, not predicting it. It is
        /// bounded by the data that exists — a lookahead past the last loaded bar returns nothing,
        /// so at the live edge there is no forward claim to make — and it is deliberately NOT
        /// wired into the bar-close or forming-bar announcements, which speak in real time and
        /// must only ever say what was knowable then. The repo's causality contract is about
        /// exactly that distinction; see <c>docs/CHART_PATTERN_NARRATION.md</c>.
        /// </para>
        ///
        /// <para>
        /// Returns the NEAREST completion. A bar can be the third soldier of one advance and the
        /// first of the next; the pattern that ends here is the one being described now.
        /// </para>
        /// </summary>
        public static Membership? MembershipAt(
            ISdkCandlePatternAnalyzer analyzer,
            IReadOnlyList<Ohlcv>? data,
            int index,
            bool heikinAshi)
        {
            if (data == null || data.Count == 0) return null;
            if (index < 0 || index >= data.Count) return null;

            // MaxPatternBars - 1: a three-bar pattern is the longest the analyser knows, so a bar
            // can be at most two bars ahead of the one that completes the pattern containing it.
            for (int ahead = 0; ahead <= MaxPatternBars - 1; ahead++)
            {
                int completeAt = index + ahead;
                if (completeAt >= data.Count) break;

                var a = AnalyzeAt(analyzer, data, completeAt, heikinAshi);
                if (a.Pattern == CandlePattern.None || a.PatternBarCount <= 1) continue;

                int start = completeAt - a.PatternBarCount + 1;
                if (index < start) continue;          // the pattern does not reach back this far

                return new Membership(a.Pattern, index - start + 1, a.PatternBarCount, data[completeAt].Date);
            }

            return null;
        }

        /// <summary>The longest pattern the analyser recognises. Three-bar is the whole set.</summary>
        public const int MaxPatternBars = 3;

        /// <summary>
        /// The clause that says WHICH CANDLES: "bar 3 of 3" on the bar whose own name already gave
        /// the pattern, "bar 1 of 3, three white soldiers" on a bar that is part of one without
        /// looking like one. Empty when the bar is not in a multi-bar pattern, which is most bars.
        ///
        /// <para>
        /// The pattern is named again only when the shape did NOT name it. On the completing bar
        /// the reading already opens with "Three white soldiers", so repeating it would be the
        /// same doubling the direction-prefix rule exists to prevent.
        /// </para>
        /// </summary>
        public static string MembershipClause(CandleAnalysis here, Membership? m)
        {
            if (m == null) return "";
            bool shapeAlreadyNamedIt = here.Pattern == m.Pattern;
            string where = $"bar {m.Position} of {m.BarCount}";
            return shapeAlreadyNamedIt ? where : $"{where}, {PatternName(m.Pattern)}";
        }

        /// <summary>
        /// What this candle IS, in one phrase: the multi-bar pattern when there is one, otherwise
        /// the single-bar type, otherwise just the direction. Never empty.
        ///
        /// <para>
        /// A NAMED SHAPE IS NEVER PREFIXED WITH A DIRECTION WORD. Every one of the twelve patterns
        /// carries its side in its name or its definition — three white soldiers is not bearish,
        /// a piercing line is not bearish — and so do the shapes whose whole identity is the trend
        /// they interrupt: hammer and hanging man are the same candle, and hearing which one it is
        /// IS hearing the direction. "Bullish three white soldiers" is three redundant syllables
        /// on a phrase the user hears on every bar of a scan, and "Bearish Bearish Marubozu" (a
        /// real reported reading) is what happens when a second place also decides to add one.
        /// </para>
        ///
        /// <para>
        /// The direction word therefore appears only when there is no shape to name — the ordinary
        /// candle, which is most of them, and which has always read as "Bullish." or "Bearish."
        /// followed by the prices. That is also exactly what the live bar-close suffix does, which
        /// is what lets the four routes agree word for word.
        /// </para>
        /// </summary>
        public static string DescribeShape(CandleAnalysis a)
        {
            string name = a.Pattern != CandlePattern.None ? PatternName(a.Pattern) : TypeName(a.Type);
            if (name.Length > 0) return name;

            string dir = DirectionWord(a.Direction);
            return dir.Length == 0 ? "Candle" : dir;
        }

        /// <summary>
        /// The extra clause the DETAIL key adds and the scanning routes do not: how many bars the
        /// shape spans and which way it leans. "2-bar reversal", "3-bar continuation", "reversal",
        /// or empty.
        ///
        /// <para>
        /// Bar count is stated only for multi-bar patterns, because that is the fact a reader of
        /// history cannot otherwise recover — hearing "morning star" on one bar gives no clue that
        /// the two bars behind the cursor are part of it. On a one-bar shape it would be noise.
        /// </para>
        /// </summary>
        public static string Bias(CandleAnalysis a)
        {
            string lean = a.IsReversal ? "reversal" : a.IsContinuation ? "continuation" : "";
            if (lean.Length == 0) return "";
            return a.PatternBarCount > 1 ? $"{a.PatternBarCount}-bar {lean}" : lean;
        }

        /// <summary>
        /// The trailing clause the new-bar announcement appends to a closing price:
        /// ", Bullish engulfing" finalized, ", Doji forming" not. Empty on an unremarkable bar,
        /// which is most of them.
        /// </summary>
        public static string Suffix(CandleType type, CandlePattern pattern, bool finalized)
        {
            string verb = finalized ? "" : " forming";
            if (pattern != CandlePattern.None) return $", {PatternName(pattern)}{verb}";
            if (type != CandleType.Normal)     return $", {TypeName(type)}{verb}";
            return "";
        }

        /// <summary>The intra-bar sentence: "Bullish engulfing forming", or empty.</summary>
        public static string Forming(CandleType type, CandlePattern pattern)
        {
            if (pattern != CandlePattern.None) return $"{PatternName(pattern)} forming";
            if (type != CandleType.Normal)     return $"{TypeName(type)} forming";
            return "";
        }

        public static string PatternName(CandlePattern p) => p switch
        {
            CandlePattern.BullishEngulfing   => "Bullish engulfing",
            CandlePattern.BearishEngulfing   => "Bearish engulfing",
            CandlePattern.BullishHarami      => "Bullish harami",
            CandlePattern.BearishHarami      => "Bearish harami",
            CandlePattern.PiercingLine       => "Piercing line",
            CandlePattern.DarkCloudCover     => "Dark cloud cover",
            CandlePattern.TweezerBottom      => "Tweezer bottom",
            CandlePattern.TweezerTop         => "Tweezer top",
            CandlePattern.MorningStar        => "Morning star",
            CandlePattern.EveningStar        => "Evening star",
            CandlePattern.ThreeWhiteSoldiers => "Three white soldiers",
            CandlePattern.ThreeBlackCrows    => "Three black crows",
            _                                => ""
        };

        public static string TypeName(CandleType t) => t switch
        {
            CandleType.Doji             => "Doji",
            CandleType.DragonflyDoji    => "Dragonfly doji",
            CandleType.GravestoneDoji   => "Gravestone doji",
            CandleType.LongLeggedDoji   => "Long-legged doji",
            CandleType.Hammer           => "Hammer",
            CandleType.HangingMan       => "Hanging man",
            CandleType.InvertedHammer   => "Inverted hammer",
            CandleType.ShootingStar     => "Shooting star",
            CandleType.MarubozuBullish  => "Bullish marubozu",
            CandleType.MarubozuBearish  => "Bearish marubozu",
            CandleType.SpinningTop      => "Spinning top",
            _                            => ""
        };

        /// <summary>
        /// Just the direction, from the candle's own colour — what a readout says when the user
        /// has turned pattern description OFF and there is therefore no shape to name.
        ///
        /// <para>
        /// It lives here rather than at the call site so that the two words stay in one place.
        /// A caller writing <c>close >= open ? "Bullish" : "Bearish"</c> inline is how the
        /// vocabulary drifted apart the first time.
        /// </para>
        /// </summary>
        public static string DirectionOnly(Ohlcv bar)
            => DirectionWord(bar.Close >= bar.Open ? CandleDirection.Bullish : CandleDirection.Bearish);

        private static string DirectionWord(CandleDirection d) => d switch
        {
            CandleDirection.Bullish => "Bullish",
            CandleDirection.Bearish => "Bearish",
            _                       => ""          // Neutral: a doji has no side to take
        };
    }
}
