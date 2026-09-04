using AccessibleTrader.Sdk.Models;

// Aliases to avoid ambiguity with AccessibleTrader.Core.Services.CandleAnalysis
using SdkCA = AccessibleTrader.Sdk.Analysis.CandleAnalysis;
using SdkCD = AccessibleTrader.Sdk.Analysis.CandleDirection;
using SdkCT = AccessibleTrader.Sdk.Analysis.CandleType;
using SdkCP = AccessibleTrader.Sdk.Analysis.CandlePattern;
using SdkThr = AccessibleTrader.Sdk.Analysis.CandlePatternThresholds;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// Implements ISdkCandlePatternAnalyzer using configurable CandlePatternThresholds.
    /// Recognises single-bar (Doji, Hammer, ShootingStar, Marubozu, SpinningTop),
    /// two-bar (Engulfing, Harami, PiercingLine, DarkCloudCover, TweezerBottom/Top),
    /// three-bar (MorningStar, EveningStar, ThreeWhiteSoldiers, ThreeBlackCrows, ThreeInside
    /// Up/Down, ThreeOutside Up/Down, Morning/EveningDojiStar, AbandonedBaby Bullish/Bearish),
    /// four-bar (ThreeLineStrike Bullish/Bearish) and five-bar (Rising/FallingThreeMethods)
    /// patterns — 24 in all.
    /// All arithmetic uses raw doubles — no formatting or rounding inside the analyzer.
    /// </summary>
    public class SdkCandlePatternAnalyzer : AccessibleTrader.Sdk.Analysis.ISdkCandlePatternAnalyzer
    {
        private readonly SdkThr _t;

        public SdkCandlePatternAnalyzer(SdkThr? thresholds = null)
        {
            _t = thresholds ?? new SdkThr();
        }

        public SdkCA Analyze(Ohlcv current, Ohlcv? previous = null, Ohlcv? twoBarsAgo = null,
                             IReadOnlyList<Ohlcv>? recent = null)
        {
            var c  = current;
            var p1 = previous;
            var p2 = twoBarsAgo;

            double range = c.High - c.Low;
            if (range <= 0) range = 1e-10;

            bool isBullish   = c.Close >= c.Open;
            double bodySize  = Math.Abs(c.Close - c.Open);
            double bodyPct   = bodySize / range * 100.0;
            double upperWick = c.High - Math.Max(c.Open, c.Close);
            double lowerWick = Math.Min(c.Open, c.Close) - c.Low;
            double upperPct  = upperWick / range * 100.0;
            double lowerPct  = lowerWick / range * 100.0;

            double changePct = (p1.HasValue && p1.Value.Close != 0)
                ? (c.Close - p1.Value.Close) / p1.Value.Close * 100.0
                : 0.0;

            SdkCD dir = isBullish ? SdkCD.Bullish : SdkCD.Bearish;

            // ── LONGEST FIRST ────────────────────────────────────────────────
            //
            // The order below is by SPAN, longest to shortest, and that is not a style choice: a
            // rising three methods CONTAINS a small bar that is a doji, a three line strike
            // contains three white soldiers, and a three inside up contains a harami. Every one of
            // those shorter shapes matches on some bar of the longer one, so testing shortest-first
            // would mean the longer pattern could never be reached — the analyser would announce
            // the part instead of the whole. Within the three-bar block the same rule applies again
            // (abandoned baby before doji star before plain star), most specific first.
            //
            // Bars four and five come from `recent` rather than from parameters. The interface
            // hands over two explicit predecessors, which was the whole reach the analyser had;
            // everything beyond that is read off the trailing window, and when no window is passed
            // the long patterns simply cannot fire. That is honest degradation rather than a
            // wrong answer: a caller with no history gets the shapes its data can support.
            int here = IndexOfCurrent(recent, c);

            // ── Five-bar ─────────────────────────────────────────────────────
            var b1 = Back(recent, here, 1);
            var b2 = Back(recent, here, 2);
            var b3 = Back(recent, here, 3);
            var b4 = Back(recent, here, 4);

            if (b1.HasValue && b2.HasValue && b3.HasValue && b4.HasValue)
            {
                // Rising Three Methods. A long bullish bar, three small bars that pull back WITHIN
                // its range without undoing it, and a second long bullish bar closing beyond the
                // first. The containment is the pattern: a pullback that breaks the first bar's
                // range is a reversal attempt, not a pause inside a trend.
                if (IsLargeBody(b4.Value, true)
                    && IsSmallBody(b3.Value) && IsSmallBody(b2.Value) && IsSmallBody(b1.Value)
                    && BodyInRange(b3.Value, b4.Value) && BodyInRange(b2.Value, b4.Value) && BodyInRange(b1.Value, b4.Value)
                    && b1.Value.Close < b4.Value.Close
                    && IsLargeBody(c, true) && c.Close > b4.Value.Close)
                    return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.RisingThreeMethods,
                        5, bodyPct, upperPct, lowerPct, changePct, continuation: true);

                // Falling Three Methods — the mirror.
                if (IsLargeBody(b4.Value, false)
                    && IsSmallBody(b3.Value) && IsSmallBody(b2.Value) && IsSmallBody(b1.Value)
                    && BodyInRange(b3.Value, b4.Value) && BodyInRange(b2.Value, b4.Value) && BodyInRange(b1.Value, b4.Value)
                    && b1.Value.Close > b4.Value.Close
                    && IsLargeBody(c, false) && c.Close < b4.Value.Close)
                    return Build(SdkCD.Bearish, SdkCT.Normal, SdkCP.FallingThreeMethods,
                        5, bodyPct, upperPct, lowerPct, changePct, continuation: true);
            }

            // ── Four-bar ─────────────────────────────────────────────────────
            if (b1.HasValue && b2.HasValue && b3.HasValue)
            {
                // Three Line Strike. Three rising bullish bars, then one bearish bar that opens
                // above the third's close and closes below the FIRST one's open — swallowing the
                // whole advance in a single bar.
                //
                // It is classified as a CONTINUATION despite looking like the opposite, and that
                // is the received reading rather than this codebase's opinion: the strike is taken
                // as a shake-out inside the advance. The terminal names the shape and states the
                // lean; it does not tell the user what to do about it, which is the standing rule
                // for every pattern here.
                if (IsBull(b3.Value) && IsBull(b2.Value) && IsBull(b1.Value)
                    && b2.Value.Close > b3.Value.Close && b1.Value.Close > b2.Value.Close
                    && !isBullish && c.Open > b1.Value.Close && c.Close < b3.Value.Open)
                    return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.ThreeLineStrikeBullish,
                        4, bodyPct, upperPct, lowerPct, changePct, continuation: true);

                // Bearish Three Line Strike — the mirror.
                if (!IsBull(b3.Value) && !IsBull(b2.Value) && !IsBull(b1.Value)
                    && b2.Value.Close < b3.Value.Close && b1.Value.Close < b2.Value.Close
                    && isBullish && c.Open < b1.Value.Close && c.Close > b3.Value.Open)
                    return Build(SdkCD.Bearish, SdkCT.Normal, SdkCP.ThreeLineStrikeBearish,
                        4, bodyPct, upperPct, lowerPct, changePct, continuation: true);
            }

            // ── Three-bar patterns ───────────────────────────────────────────
            if (p1.HasValue && p2.HasValue)
            {
                var pa2 = p2.Value;
                var pa1 = p1.Value;

                // ── ABANDONED BABY, and it is the one pattern here that keeps a TRUE GAP ──────
                //
                // The doji's entire RANGE — not its body — must sit clear of both neighbours'
                // ranges. Everywhere else in this analyser a classical gap has been replaced with
                // a body tolerance, because 24/7 crypto does not gap and the pattern would
                // otherwise be undetectable there. Not here: an abandoned baby with the gaps
                // loosened IS a morning or evening doji star, and the two would become the same
                // detector wearing two names. So this one stays strict and is simply rare on
                // crypto and findable on anything with a session break. Being honest about which
                // markets a pattern can occur in beats reporting one that did not happen.
                if (IsDoji(pa1) && !IsBull(pa2) && isBullish
                    && pa1.High < pa2.Low && c.Low > pa1.High)
                    return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.AbandonedBabyBullish,
                        3, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                if (IsDoji(pa1) && IsBull(pa2) && !isBullish
                    && pa1.Low > pa2.High && c.High < pa1.Low)
                    return Build(SdkCD.Bearish, SdkCT.Normal, SdkCP.AbandonedBabyBearish,
                        3, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Morning / Evening DOJI Star — a star whose middle bar is a doji rather than
                // merely small-bodied. Tested before the plain star, which it would otherwise
                // match: a doji IS a small body, so the general case would swallow the specific
                // one and the more decisive shape would never be named.
                if (IsLargeBody(pa2, false) && IsDoji(pa1) && isBullish
                    && BodyHigh(pa1) < BodyLow(pa2) + BodySize(pa2) * _t.StarBodyOverlapAllowed
                    && c.Close > (pa2.Open + pa2.Close) / 2.0)
                    return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.MorningDojiStar,
                        3, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                if (IsLargeBody(pa2, true) && IsDoji(pa1) && !isBullish
                    && BodyLow(pa1) > BodyHigh(pa2) - BodySize(pa2) * _t.StarBodyOverlapAllowed
                    && c.Close < (pa2.Open + pa2.Close) / 2.0)
                    return Build(SdkCD.Bearish, SdkCT.Normal, SdkCP.EveningDojiStar,
                        3, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Morning Star. The STAR — the small middle body — must sit BELOW the first bar's
                // body; that separation is what makes the shape a star rather than three ordinary
                // bars. Classically it is a gap, which 24/7 crypto never produces, so the test here
                // is body-below-body rather than a true gap. Without any such test the pattern fires
                // on [long red, small body, green closing above the midpoint] no matter where the
                // middle bar sits — including above the first bar's open, which is not the pattern.
                if (IsLargeBody(pa2, false) && IsSmallBody(pa1) && isBullish
                    && BodyHigh(pa1) < BodyLow(pa2) + BodySize(pa2) * _t.StarBodyOverlapAllowed
                    && c.Close > (pa2.Open + pa2.Close) / 2.0)
                    return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.MorningStar,
                        3, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Evening Star — the mirror.
                if (IsLargeBody(pa2, true) && IsSmallBody(pa1) && !isBullish
                    && BodyLow(pa1) > BodyHigh(pa2) - BodySize(pa2) * _t.StarBodyOverlapAllowed
                    && c.Close < (pa2.Open + pa2.Close) / 2.0)
                    return Build(SdkCD.Bearish, SdkCT.Normal, SdkCP.EveningStar,
                        3, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Three White Soldiers. Each candle must OPEN INSIDE the previous body — that is the
                // definition, and it is what distinguishes a steady advance from three bars gapping
                // away from each other, which is a different (and exhaustion-flavoured) thing. The
                // previous test only required each open to be above the last open, so a gapped
                // staircase qualified.
                if (isBullish && IsLargeBody(pa1, true) && IsLargeBody(pa2, true)
                    && c.Open > pa1.Open && c.Open <= pa1.Close && c.Close > pa1.Close
                    && pa1.Open > pa2.Open && pa1.Open <= pa2.Close && pa1.Close > pa2.Close)
                    return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.ThreeWhiteSoldiers,
                        3, bodyPct, upperPct, lowerPct, changePct, continuation: true);

                // Three Black Crows — the mirror.
                if (!isBullish && IsLargeBody(pa1, false) && IsLargeBody(pa2, false)
                    && c.Open < pa1.Open && c.Open >= pa1.Close && c.Close < pa1.Close
                    && pa1.Open < pa2.Open && pa1.Open >= pa2.Close && pa1.Close < pa2.Close)
                    return Build(SdkCD.Bearish, SdkCT.Normal, SdkCP.ThreeBlackCrows,
                        3, bodyPct, upperPct, lowerPct, changePct, continuation: true);

                // ── THE TWO CONFIRMED REVERSALS ──────────────────────────────────────────────
                //
                // Three inside and three outside are a two-bar pattern plus a bar that confirms
                // it. That third bar is the entire difference: a harami says the trend stalled,
                // a three inside up says the stall resolved upward and did so before the trader
                // had to guess. They are worth naming separately for exactly that reason — the
                // shape carries the confirmation the shorter one is still waiting for.
                //
                // They sit AFTER the stars deliberately. A star's middle bar must sit clear of
                // the first bar's body while a harami's must sit inside it, so the two cannot
                // both hold except at the very edge of the overlap tolerance; where they can, the
                // star is the more decisive reading and wins.

                // Three Inside Up: bullish harami, then a bullish bar closing above it.
                if (IsLargeBody(pa2, false) && IsBull(pa1) && IsInsideBody(pa1, pa2)
                    && isBullish && c.Close > pa1.Close)
                    return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.ThreeInsideUp,
                        3, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Three Inside Down — the mirror.
                if (IsLargeBody(pa2, true) && !IsBull(pa1) && IsInsideBody(pa1, pa2)
                    && !isBullish && c.Close < pa1.Close)
                    return Build(SdkCD.Bearish, SdkCT.Normal, SdkCP.ThreeInsideDown,
                        3, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Three Outside Up: bullish ENGULFING, then a bullish bar closing above it.
                if (!IsBull(pa2) && IsBull(pa1)
                    && pa1.Open <= pa2.Close && pa1.Close >= pa2.Open
                    && BodySize(pa1) >= BodySize(pa2) * _t.EngulfingBodyOverlapRequired
                    && isBullish && c.Close > pa1.Close)
                    return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.ThreeOutsideUp,
                        3, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Three Outside Down — the mirror.
                if (IsBull(pa2) && !IsBull(pa1)
                    && pa1.Open >= pa2.Close && pa1.Close <= pa2.Open
                    && BodySize(pa1) >= BodySize(pa2) * _t.EngulfingBodyOverlapRequired
                    && !isBullish && c.Close < pa1.Close)
                    return Build(SdkCD.Bearish, SdkCT.Normal, SdkCP.ThreeOutsideDown,
                        3, bodyPct, upperPct, lowerPct, changePct, reversal: true);
            }

            // ── Two-bar patterns ─────────────────────────────────────────────
            if (p1.HasValue)
            {
                var prev = p1.Value;
                bool prevBullish = prev.Close >= prev.Open;
                double prevBodySize = Math.Abs(prev.Close - prev.Open);

                // Bullish Engulfing
                if (isBullish && !prevBullish
                    && c.Open <= prev.Close && c.Close >= prev.Open
                    && bodySize >= prevBodySize * _t.EngulfingBodyOverlapRequired)
                    return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.BullishEngulfing,
                        2, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Bearish Engulfing
                if (!isBullish && prevBullish
                    && c.Open >= prev.Close && c.Close <= prev.Open
                    && bodySize >= prevBodySize * _t.EngulfingBodyOverlapRequired)
                    return Build(SdkCD.Bearish, SdkCT.Normal, SdkCP.BearishEngulfing,
                        2, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Harami
                double prevBodyHigh = Math.Max(prev.Open, prev.Close);
                double prevBodyLow  = Math.Min(prev.Open, prev.Close);
                double currBodyHigh = Math.Max(c.Open, c.Close);
                double currBodyLow  = Math.Min(c.Open, c.Close);
                // Harami: this body is contained within the previous body, and the colours differ.
                // There is deliberately no test on bodyPct here. It used to require bodyPct < 50 —
                // the current body as a fraction of its OWN range — which is not part of the
                // definition and rejected any harami whose small body happened to fill most of its
                // own small range. Containment inside the previous body is the whole pattern.
                if (currBodyHigh < prevBodyHigh && currBodyLow > prevBodyLow)
                {
                    if (isBullish && !prevBullish)
                        return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.BullishHarami,
                            2, bodyPct, upperPct, lowerPct, changePct, reversal: true);
                    if (!isBullish && prevBullish)
                        return Build(SdkCD.Bearish, SdkCT.Normal, SdkCP.BearishHarami,
                            2, bodyPct, upperPct, lowerPct, changePct, reversal: true);
                }

                // Piercing Line
                double prevMid = (prev.Open + prev.Close) / 2.0;
                if (!prevBullish && isBullish && c.Open < prev.Low && c.Close > prevMid && c.Close < prev.Open)
                    return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.PiercingLine,
                        2, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Dark Cloud Cover
                double prevMid2 = (prev.Open + prev.Close) / 2.0;
                if (prevBullish && !isBullish && c.Open > prev.High && c.Close < prevMid2 && c.Close > prev.Open)
                    return Build(SdkCD.Bearish, SdkCT.Normal, SdkCP.DarkCloudCover,
                        2, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Tweezer Bottom
                double tolLow = c.Low > 0 ? c.Low * _t.TweezerTolerancePercent / 100.0 : 0.0001;
                if (Math.Abs(c.Low - prev.Low) <= tolLow && isBullish && !prevBullish)
                    return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.TweezerBottom,
                        2, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Tweezer Top
                double tolHigh = c.High > 0 ? c.High * _t.TweezerTolerancePercent / 100.0 : 0.0001;
                if (Math.Abs(c.High - prev.High) <= tolHigh && !isBullish && prevBullish)
                    return Build(SdkCD.Bearish, SdkCT.Normal, SdkCP.TweezerTop,
                        2, bodyPct, upperPct, lowerPct, changePct, reversal: true);
            }

            // ── Single-bar patterns ──────────────────────────────────────────

            // Doji family
            if (bodyPct < _t.DojiBodyMaxPercent)
            {
                SdkCT dojiType = SdkCT.Doji;
                if (lowerPct > 40 && upperPct < 8) dojiType = SdkCT.DragonflyDoji;
                else if (upperPct > 40 && lowerPct < 8) dojiType = SdkCT.GravestoneDoji;
                else if (upperPct > 25 && lowerPct > 25) dojiType = SdkCT.LongLeggedDoji;
                return Build(SdkCD.Neutral, dojiType, SdkCP.None,
                    1, bodyPct, upperPct, lowerPct, changePct, reversal: dojiType != SdkCT.Doji);
            }

            // Marubozu
            if (bodyPct >= _t.MarubozuBodyMinPercent)
            {
                var mbType = isBullish ? SdkCT.MarubozuBullish : SdkCT.MarubozuBearish;
                return Build(dir, mbType, SdkCP.None, 1, bodyPct, upperPct, lowerPct, changePct);
            }

            // ── The two shapes whose NAME is decided by the trend they interrupt ──
            //
            // A hammer and a hanging man are the same candle. So are an inverted hammer and a
            // shooting star. What separates each pair is the trend it appears in: hammer and
            // inverted hammer end a decline (bullish), hanging man and shooting star end an advance
            // (bearish). Getting it wrong does not merely mislabel — it announces the opposite
            // direction to the one the shape implies.
            //
            // This used to be decided by the COLOUR OF THE SINGLE PREVIOUS CANDLE, which is not a
            // trend: one green bar inside a sustained decline turned a hammer into a hanging man.
            // CandlePatternThresholds.TrendLookbackBars had been declared for exactly this job and
            // was never read by anything. It is now.
            bool? priorDowntrend = PriorTrendIsDown(recent, p1);

            // Direction here is the shape's IMPLICATION, not the candle's own colour: a hanging man
            // is bearish even though it can close green, and that is the entire point of the name.
            // When the trend is unknown there is no implication to state, so it falls back to the
            // candle's own direction — a fact rather than a claim — and asserts no reversal.

            // Hammer (ends a decline, bullish) / Hanging Man (ends an advance, bearish)
            bool bodyInUpperZone = (Math.Min(c.Open, c.Close) - c.Low) / range * 100.0 > (100.0 - _t.HammerBodyUpperZonePercent);
            if (lowerWick > bodySize * _t.WickMultiplierForHammer && upperWick < bodySize && bodyInUpperZone)
            {
                var hmType = priorDowntrend == true ? SdkCT.Hammer : SdkCT.HangingMan;
                var hmDir = priorDowntrend switch { true => SdkCD.Bullish, false => SdkCD.Bearish, _ => dir };
                return Build(hmDir, hmType, SdkCP.None,
                    1, bodyPct, upperPct, lowerPct, changePct, reversal: priorDowntrend.HasValue);
            }

            // Inverted Hammer (ends a decline, bullish) / Shooting Star (ends an advance, bearish)
            bool bodyInLowerZone = (c.High - Math.Max(c.Open, c.Close)) / range * 100.0 > (100.0 - _t.HammerBodyUpperZonePercent);
            if (upperWick > bodySize * _t.WickMultiplierForHammer && lowerWick < bodySize && bodyInLowerZone)
            {
                var ssType = priorDowntrend == false ? SdkCT.ShootingStar : SdkCT.InvertedHammer;
                var ssDir = priorDowntrend switch { true => SdkCD.Bullish, false => SdkCD.Bearish, _ => dir };
                return Build(ssDir, ssType, SdkCP.None,
                    1, bodyPct, upperPct, lowerPct, changePct, reversal: priorDowntrend.HasValue);
            }

            // Spinning Top
            if (bodyPct < _t.SpinningTopBodyMaxPercent && upperPct > 15 && lowerPct > 15)
                return Build(SdkCD.Neutral, SdkCT.SpinningTop, SdkCP.None,
                    1, bodyPct, upperPct, lowerPct, changePct);

            // Generic
            return Build(dir, SdkCT.Normal, SdkCP.None, 1, bodyPct, upperPct, lowerPct, changePct);
        }

        private static SdkCA Build(
            SdkCD direction, SdkCT type, SdkCP pattern,
            int barCount, double bodyPct, double upperPct, double lowerPct, double changePct,
            bool reversal = false, bool continuation = false) => new()
        {
            Direction        = direction,
            Type             = type,
            Pattern          = pattern,
            PatternBarCount  = barCount,
            BodyPercent      = bodyPct,
            UpperWickPercent = upperPct,
            LowerWickPercent = lowerPct,
            ChangePercent    = changePct,
            IsReversal       = reversal   || IsReversalPattern(pattern),
            IsContinuation   = continuation || IsContinuationPattern(pattern)
        };

        private static bool IsReversalPattern(SdkCP p) => p is
            SdkCP.BullishEngulfing     or SdkCP.BearishEngulfing     or
            SdkCP.BullishHarami        or SdkCP.BearishHarami        or
            SdkCP.MorningStar          or SdkCP.EveningStar          or
            SdkCP.TweezerBottom        or SdkCP.TweezerTop           or
            SdkCP.PiercingLine         or SdkCP.DarkCloudCover       or
            SdkCP.ThreeInsideUp        or SdkCP.ThreeInsideDown      or
            SdkCP.ThreeOutsideUp       or SdkCP.ThreeOutsideDown     or
            SdkCP.MorningDojiStar      or SdkCP.EveningDojiStar      or
            SdkCP.AbandonedBabyBullish or SdkCP.AbandonedBabyBearish;

        private static bool IsContinuationPattern(SdkCP p) => p is
            SdkCP.ThreeWhiteSoldiers     or SdkCP.ThreeBlackCrows or
            SdkCP.RisingThreeMethods     or SdkCP.FallingThreeMethods or
            SdkCP.ThreeLineStrikeBullish or SdkCP.ThreeLineStrikeBearish;

        /// <summary>
        /// Where <paramref name="current"/> sits inside the trailing window, found by DATE from
        /// the live edge backwards; the last bar when the window does not carry it.
        ///
        /// <para>
        /// By date rather than by position, for the reason recorded on <c>PriorTrendIsDown</c>:
        /// callers pass a window that happens to end at the classified bar today, but one that
        /// classified a HISTORICAL bar with a longer window in hand would otherwise read bars four
        /// and five out of the FUTURE. A five-bar continuation assembled from bars that had not
        /// happened is not a near-miss, it is a fabricated pattern.
        /// </para>
        /// </summary>
        private static int IndexOfCurrent(IReadOnlyList<Ohlcv>? recent, Ohlcv current)
        {
            if (recent == null || recent.Count == 0) return -1;
            for (int i = recent.Count - 1; i >= 0; i--)
                if (recent[i].Date == current.Date) return i;
            return recent.Count - 1;
        }

        /// <summary>The bar <paramref name="back"/> places before <paramref name="index"/>, or null.</summary>
        private static Ohlcv? Back(IReadOnlyList<Ohlcv>? recent, int index, int back)
        {
            if (recent == null || index < 0) return null;
            int i = index - back;
            return i >= 0 && i < recent.Count ? recent[i] : (Ohlcv?)null;
        }

        private static bool IsBull(Ohlcv b) => b.Close >= b.Open;

        /// <summary>A body wholly inside another bar's HIGH–LOW range — the three-methods test.</summary>
        private static bool BodyInRange(Ohlcv inner, Ohlcv outer)
            => BodyHigh(inner) <= outer.High && BodyLow(inner) >= outer.Low;

        /// <summary>A body wholly inside another bar's BODY — the harami test.</summary>
        private static bool IsInsideBody(Ohlcv inner, Ohlcv outer)
            => BodyHigh(inner) < BodyHigh(outer) && BodyLow(inner) > BodyLow(outer);

        /// <summary>
        /// A doji by the same threshold the single-bar classification uses. Shared rather than
        /// re-derived so "the middle bar is a doji" cannot come to mean two different things
        /// depending on whether it is being named or being used inside a longer pattern.
        /// </summary>
        private bool IsDoji(Ohlcv b)
        {
            double r = b.High - b.Low;
            if (r <= 0) return true;
            return Math.Abs(b.Close - b.Open) / r * 100.0 < _t.DojiBodyMaxPercent;
        }

        private static double BodyHigh(Ohlcv b) => Math.Max(b.Open, b.Close);
        private static double BodyLow(Ohlcv b) => Math.Min(b.Open, b.Close);
        private static double BodySize(Ohlcv b) => Math.Abs(b.Close - b.Open);

        /// <summary>
        /// True if the bars leading INTO the current one were falling, false if rising, null if
        /// there is not enough context to say.
        ///
        /// <para>
        /// The comparison deliberately ENDS at the bar before the current one. The current bar is
        /// the candidate reversal; including it would let a strong hammer close drag the trend
        /// measurement bullish and then be labelled as reversing the trend it just created.
        /// </para>
        ///
        /// <para>
        /// Returning null rather than guessing matters: the caller uses it to decide whether to
        /// assert <c>IsReversal</c> at all. With no context the shape is still named, but nothing
        /// downstream is told that a reversal has been identified.
        /// </para>
        /// </summary>
        private bool? PriorTrendIsDown(IReadOnlyList<Ohlcv>? recent, Ohlcv? previous)
        {
            int look = Math.Max(1, _t.TrendLookbackBars);

            if (recent != null && recent.Count >= look + 2)
            {
                // Where the classified bar sits in the list, rather than assuming it is last.
                // Callers pass their whole loaded series today and it happens to end at the bar
                // being classified — but if one ever classifies a HISTORICAL bar with the full
                // series in hand, measuring the trend from the end of the list would measure it
                // from bars that had not happened yet. A hammer and a hanging man are the same
                // candle distinguished only by the trend before it, so that does not mislabel by a
                // shade: it announces the opposite direction to someone who cannot see the chart.
                int endIdx = recent.Count - 2;
                if (previous.HasValue)
                {
                    for (int i = recent.Count - 1; i >= 0; i--)
                        if (recent[i].Date == previous.Value.Date) { endIdx = i; break; }
                }
                if (endIdx - look < 0) return null;

                var end = recent[endIdx];
                var start = recent[endIdx - look];
                if (end.Close < start.Close) return true;
                if (end.Close > start.Close) return false;
                return null;                                  // dead flat: no trend to interrupt
            }

            // No usable context. Do NOT fall back to the previous candle's colour — that is the bug
            // this method exists to remove. Say "unknown" and let the caller decline to claim a
            // reversal. One bar is deliberately not enough; `previous` is used above only to locate
            // the classified bar within the list, never as a trend on its own.
            return null;
        }

        private bool IsLargeBody(Ohlcv bar, bool bullish)
        {
            double r = bar.High - bar.Low;
            if (r <= 0) return false;
            double bodyPct = Math.Abs(bar.Close - bar.Open) / r * 100.0;
            return bodyPct >= _t.LargeBodyMinPercent && (bar.Close >= bar.Open) == bullish;
        }

        private bool IsSmallBody(Ohlcv bar)
        {
            double r = bar.High - bar.Low;
            if (r <= 0) return true;
            return Math.Abs(bar.Close - bar.Open) / r * 100.0 < _t.SmallBodyMaxPercent;
        }
    }
}
