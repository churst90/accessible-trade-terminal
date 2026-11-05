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

            double changePct = (p1.HasValue && p1.Value.Close != 0)
                ? (c.Close - p1.Value.Close) / p1.Value.Close * 100.0
                : 0.0;

            // ── NO MOVEMENT ──────────────────────────────────────────────────
            //
            // A bar whose high is its low has no body, no wicks and no shape. It used to fall into
            // the doji test (a zero body of a zero range), and on a thin pair that is most dojis:
            // on Bitstamp BTCUSDT 1h, 276 of 1,000 bars never moved, and 236 of the 242 "dojis" and
            // the middle bar of 9 of the 10 "abandoned babies" were such bars. A doji is a market
            // that moved and came back; this is a market where nothing traded. It is named for
            // what it is and takes part in no pattern (Moved() below keeps it out of the longer ones).
            if (c.High <= c.Low)
                return Build(SdkCD.Neutral, SdkCT.NoMovement, SdkCP.None, 1, 0, 0, 0, changePct);

            double range = c.High - c.Low;

            bool isBullish   = c.Close >= c.Open;
            double bodySize  = Math.Abs(c.Close - c.Open);
            double bodyPct   = bodySize / range * 100.0;
            double upperWick = c.High - Math.Max(c.Open, c.Close);
            double lowerWick = Math.Min(c.Open, c.Close) - c.Low;
            double upperPct  = upperWick / range * 100.0;
            double lowerPct  = lowerWick / range * 100.0;

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
            //
            // ── THE TEXTBOOK RULES (2026-11-04) ──────────────────────────────
            //
            // Cody asked for the definitions to be "more textbook and accurate". Three rules now
            // hold for every pattern, where until then they held for a few or none:
            //
            //  1. A REVERSAL NEEDS A TREND TO REVERSE. A bullish reversal must come after the
            //     closes fell over the TrendLookbackBars bars before the pattern began, a bearish
            //     one after they rose (Nison, Bulkowski). Only the hammer and the shooting star
            //     checked; 138 of 297 two- and three-bar reversals in 1,000 Bitstamp hours came
            //     after a move their own way, like a tweezer top after a fall. With no trend to
            //     read (too little history, or dead flat) no reversal is named. The continuation
            //     patterns need the trend they continue the same way.
            //  2. SIZE IS JUDGED AGAINST THE BARS BEFORE, as TA-Lib does: a long body is at least
            //     the average body of the AverageLookbackBars before it, a short one is under it,
            //     and equal highs or lows are within EqualFractionOfAverageRange of the average
            //     range. A long body must still fill LargeBodyMinPercent of its own range.
            //  3. A BAR THAT NEVER MOVED IS IN NO PATTERN (above).
            int here = IndexOfCurrent(recent, c);

            // ── Five-bar ─────────────────────────────────────────────────────
            var b1 = Back(recent, here, 1);
            var b2 = Back(recent, here, 2);
            var b3 = Back(recent, here, 3);
            var b4 = Back(recent, here, 4);

            if (Moved(b1) && Moved(b2) && Moved(b3) && Moved(b4))
            {
                bool? down5 = TrendBefore(recent, here, 5);

                // Rising Three Methods. In an advance: a long bullish bar, three small bars that
                // pull back WITHIN its range without undoing it, and a second long bullish bar
                // closing beyond the first. The containment is the pattern: a pullback that breaks
                // the first bar's range is a reversal attempt, not a pause inside a trend.
                if (down5 == false
                    && IsLargeBody(b4!.Value, true, AvgBody(recent, here, 4))
                    && IsSmallBody(b3!.Value, AvgBody(recent, here, 3)) && IsSmallBody(b2!.Value, AvgBody(recent, here, 2))
                    && IsSmallBody(b1!.Value, AvgBody(recent, here, 1))
                    && BodyInRange(b3.Value, b4.Value) && BodyInRange(b2.Value, b4.Value) && BodyInRange(b1.Value, b4.Value)
                    && b1.Value.Close < b4.Value.Close
                    && IsLargeBody(c, true, AvgBody(recent, here, 0)) && c.Close > b4.Value.Close)
                    return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.RisingThreeMethods,
                        5, bodyPct, upperPct, lowerPct, changePct, continuation: true);

                // Falling Three Methods — the mirror, in a decline.
                if (down5 == true
                    && IsLargeBody(b4!.Value, false, AvgBody(recent, here, 4))
                    && IsSmallBody(b3!.Value, AvgBody(recent, here, 3)) && IsSmallBody(b2!.Value, AvgBody(recent, here, 2))
                    && IsSmallBody(b1!.Value, AvgBody(recent, here, 1))
                    && BodyInRange(b3.Value, b4.Value) && BodyInRange(b2.Value, b4.Value) && BodyInRange(b1.Value, b4.Value)
                    && b1.Value.Close > b4.Value.Close
                    && IsLargeBody(c, false, AvgBody(recent, here, 0)) && c.Close < b4.Value.Close)
                    return Build(SdkCD.Bearish, SdkCT.Normal, SdkCP.FallingThreeMethods,
                        5, bodyPct, upperPct, lowerPct, changePct, continuation: true);
            }

            // ── Four-bar ─────────────────────────────────────────────────────
            if (Moved(b1) && Moved(b2) && Moved(b3))
            {
                // Three Line Strike. Three rising bullish bars, then one bearish bar that opens
                // above the third's close and closes below the FIRST one's open — swallowing the
                // whole advance in a single bar.
                //
                // It is classified as a CONTINUATION despite looking like the opposite, and that
                // is the received reading (Morris; TA-Lib scores it the same way) rather than this
                // codebase's opinion: the strike is taken as a shake-out inside the advance, and
                // the three rising bars are the trend it continues, so no trend before is asked
                // for. Bulkowski's tests found it more often reverses; the terminal names the
                // shape and states the lean, and does not tell the user what to do about it,
                // which is the standing rule for every pattern here.
                if (IsBull(b3!.Value) && IsBull(b2!.Value) && IsBull(b1!.Value)
                    && b2.Value.Close > b3.Value.Close && b1.Value.Close > b2.Value.Close
                    && !isBullish && c.Open > b1.Value.Close && c.Close < b3.Value.Open)
                    return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.ThreeLineStrikeBullish,
                        4, bodyPct, upperPct, lowerPct, changePct, continuation: true);

                // Bearish Three Line Strike — the mirror.
                if (!IsBull(b3!.Value) && !IsBull(b2!.Value) && !IsBull(b1!.Value)
                    && b2.Value.Close < b3.Value.Close && b1.Value.Close < b2.Value.Close
                    && isBullish && c.Open < b1.Value.Close && c.Close > b3.Value.Open)
                    return Build(SdkCD.Bearish, SdkCT.Normal, SdkCP.ThreeLineStrikeBearish,
                        4, bodyPct, upperPct, lowerPct, changePct, continuation: true);
            }

            // ── Three-bar patterns ───────────────────────────────────────────
            if (Moved(p1) && Moved(p2))
            {
                var pa2 = p2!.Value;
                var pa1 = p1!.Value;
                bool? down3 = TrendBefore(recent, here, 3);
                double? avg2 = AvgBody(recent, here, 2), avg1 = AvgBody(recent, here, 1), avg0 = AvgBody(recent, here, 0);

                // ── ABANDONED BABY, which keeps a TRUE GAP ───────────────────────────────────
                //
                // The doji's entire RANGE — not its body — must sit clear of both neighbours'
                // ranges. In the stars a classical gap has been replaced with a body tolerance,
                // because 24/7 crypto does not gap and the pattern would otherwise be undetectable
                // there. (Piercing line and dark cloud cover keep theirs too: the open beyond the
                // prior bar's low or high.) Not here: an abandoned baby with the gaps
                // loosened IS a morning or evening doji star, and the two would become the same
                // detector wearing two names. So this one stays strict and is simply rare on
                // crypto and findable on anything with a session break. Being honest about which
                // markets a pattern can occur in beats reporting one that did not happen.
                //
                // The first bar is long and the third is not short (TA-Lib), as in the stars.
                if (down3 == true && IsLargeBody(pa2, false, avg2) && IsDoji(pa1)
                    && isBullish && !IsSmallBody(c, avg0)
                    && pa1.High < pa2.Low && c.Low > pa1.High)
                    return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.AbandonedBabyBullish,
                        3, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                if (down3 == false && IsLargeBody(pa2, true, avg2) && IsDoji(pa1)
                    && !isBullish && !IsSmallBody(c, avg0)
                    && pa1.Low > pa2.High && c.High < pa1.Low)
                    return Build(SdkCD.Bearish, SdkCT.Normal, SdkCP.AbandonedBabyBearish,
                        3, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Morning / Evening DOJI Star — a star whose middle bar is a doji rather than
                // merely small-bodied. Tested before the plain star, which it would otherwise
                // match: a doji IS a small body, so the general case would swallow the specific
                // one and the more decisive shape would never be named.
                if (down3 == true && IsLargeBody(pa2, false, avg2) && IsDoji(pa1)
                    && isBullish && !IsSmallBody(c, avg0)
                    && BodyHigh(pa1) < BodyLow(pa2) + BodySize(pa2) * _t.StarBodyOverlapAllowed
                    && c.Close > (pa2.Open + pa2.Close) / 2.0)
                    return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.MorningDojiStar,
                        3, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                if (down3 == false && IsLargeBody(pa2, true, avg2) && IsDoji(pa1)
                    && !isBullish && !IsSmallBody(c, avg0)
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
                // The third bar is a real body, not another small one (TA-Lib).
                if (down3 == true && IsLargeBody(pa2, false, avg2) && IsSmallBody(pa1, avg1)
                    && isBullish && !IsSmallBody(c, avg0)
                    && BodyHigh(pa1) < BodyLow(pa2) + BodySize(pa2) * _t.StarBodyOverlapAllowed
                    && c.Close > (pa2.Open + pa2.Close) / 2.0)
                    return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.MorningStar,
                        3, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Evening Star — the mirror.
                if (down3 == false && IsLargeBody(pa2, true, avg2) && IsSmallBody(pa1, avg1)
                    && !isBullish && !IsSmallBody(c, avg0)
                    && BodyLow(pa1) > BodyHigh(pa2) - BodySize(pa2) * _t.StarBodyOverlapAllowed
                    && c.Close < (pa2.Open + pa2.Close) / 2.0)
                    return Build(SdkCD.Bearish, SdkCT.Normal, SdkCP.EveningStar,
                        3, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Three White Soldiers. Each candle must OPEN INSIDE the previous body — that is the
                // definition, and it is what distinguishes a steady advance from three bars gapping
                // away from each other, which is a different (and exhaustion-flavoured) thing. The
                // previous test only required each open to be above the last open, so a gapped
                // staircase qualified.
                //
                // And all THREE bodies are large. Only the first two were tested until 2026-11-04,
                // so the third could be a small body under a long wick: Bitstamp BTCUSDT 1h's
                // 19:00 bar of 2026-11-02 closed a "three black crows" with a body 20% of its range.
                //
                // A REVERSAL, after a decline — Nison's "advancing white soldiers" end a fall, and
                // Bulkowski and Investopedia file both as reversals. They were called continuations
                // here until 2026-11-04.
                if (down3 == true && isBullish && IsLargeBody(c, true, avg0) && IsLargeBody(pa1, true, avg1) && IsLargeBody(pa2, true, avg2)
                    && c.Open > pa1.Open && c.Open <= pa1.Close && c.Close > pa1.Close
                    && pa1.Open > pa2.Open && pa1.Open <= pa2.Close && pa1.Close > pa2.Close)
                    return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.ThreeWhiteSoldiers,
                        3, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Three Black Crows — the mirror, after an advance.
                if (down3 == false && !isBullish && IsLargeBody(c, false, avg0) && IsLargeBody(pa1, false, avg1) && IsLargeBody(pa2, false, avg2)
                    && c.Open < pa1.Open && c.Open >= pa1.Close && c.Close < pa1.Close
                    && pa1.Open < pa2.Open && pa1.Open >= pa2.Close && pa1.Close < pa2.Close)
                    return Build(SdkCD.Bearish, SdkCT.Normal, SdkCP.ThreeBlackCrows,
                        3, bodyPct, upperPct, lowerPct, changePct, reversal: true);

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

                // Three Inside Up: a bullish harami (long bearish bar, small bullish body inside
                // it), then a bullish bar closing above the FIRST bar's open — back above where
                // the decline's last long bar began (TA-Lib). It confirmed on closing above the
                // middle bar until 2026-11-04, which a bar barely higher than a small body did.
                if (down3 == true && IsLargeBody(pa2, false, avg2) && IsBull(pa1) && IsSmallBody(pa1, avg1)
                    && IsInsideBody(pa1, pa2)
                    && isBullish && c.Close > pa2.Open)
                    return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.ThreeInsideUp,
                        3, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Three Inside Down — the mirror: closes below the first bar's open.
                if (down3 == false && IsLargeBody(pa2, true, avg2) && !IsBull(pa1) && IsSmallBody(pa1, avg1)
                    && IsInsideBody(pa1, pa2)
                    && !isBullish && c.Close < pa2.Open)
                    return Build(SdkCD.Bearish, SdkCT.Normal, SdkCP.ThreeInsideDown,
                        3, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Three Outside Up: bullish ENGULFING, then a bullish bar closing above it.
                if (down3 == true && !IsBull(pa2) && IsBull(pa1)
                    && pa1.Open <= pa2.Close && pa1.Close >= pa2.Open
                    && BodySize(pa1) >= BodySize(pa2) * _t.EngulfingBodyOverlapRequired
                    && isBullish && c.Close > pa1.Close)
                    return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.ThreeOutsideUp,
                        3, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Three Outside Down — the mirror.
                if (down3 == false && IsBull(pa2) && !IsBull(pa1)
                    && pa1.Open >= pa2.Close && pa1.Close <= pa2.Open
                    && BodySize(pa1) >= BodySize(pa2) * _t.EngulfingBodyOverlapRequired
                    && !isBullish && c.Close < pa1.Close)
                    return Build(SdkCD.Bearish, SdkCT.Normal, SdkCP.ThreeOutsideDown,
                        3, bodyPct, upperPct, lowerPct, changePct, reversal: true);
            }

            // ── Two-bar patterns ─────────────────────────────────────────────
            if (Moved(p1))
            {
                var prev = p1!.Value;
                bool prevBullish = prev.Close >= prev.Open;
                double prevBodySize = Math.Abs(prev.Close - prev.Open);
                bool? down2 = TrendBefore(recent, here, 2);
                double? avgPrev = AvgBody(recent, here, 1), avgHere = AvgBody(recent, here, 0);

                // Bullish Engulfing, after a decline
                if (down2 == true && isBullish && !prevBullish
                    && c.Open <= prev.Close && c.Close >= prev.Open
                    && bodySize >= prevBodySize * _t.EngulfingBodyOverlapRequired)
                    return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.BullishEngulfing,
                        2, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Bearish Engulfing, after an advance
                if (down2 == false && !isBullish && prevBullish
                    && c.Open >= prev.Close && c.Close <= prev.Open
                    && bodySize >= prevBodySize * _t.EngulfingBodyOverlapRequired)
                    return Build(SdkCD.Bearish, SdkCT.Normal, SdkCP.BearishEngulfing,
                        2, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Harami: a long body, then a SHORT body of the other colour inside it (Nison;
                // TA-Lib: first long, second short). Until 2026-11-04 neither size was tested, so
                // two near-equal small bodies, one inside the other, made a harami.
                //
                // There is deliberately no test of the second body against its OWN range. It used
                // to require bodyPct < 50 — which is not part of the definition and rejected any
                // harami whose small body happened to fill most of its own small range. Short is
                // short against the bars before, which is what IsSmallBody now reads.
                if (IsInsideBody(c, prev) && IsSmallBody(c, avgHere))
                {
                    if (down2 == true && isBullish && IsLargeBody(prev, false, avgPrev))
                        return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.BullishHarami,
                            2, bodyPct, upperPct, lowerPct, changePct, reversal: true);
                    if (down2 == false && !isBullish && IsLargeBody(prev, true, avgPrev))
                        return Build(SdkCD.Bearish, SdkCT.Normal, SdkCP.BearishHarami,
                            2, bodyPct, upperPct, lowerPct, changePct, reversal: true);
                }

                // Piercing Line: a long bearish bar, then a bullish one opening below its low and
                // closing past the middle of its body, still inside it.
                double prevMid = (prev.Open + prev.Close) / 2.0;
                if (down2 == true && IsLargeBody(prev, false, avgPrev)
                    && isBullish && c.Open < prev.Low && c.Close > prevMid && c.Close < prev.Open)
                    return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.PiercingLine,
                        2, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Dark Cloud Cover — the mirror.
                if (down2 == false && IsLargeBody(prev, true, avgPrev)
                    && !isBullish && c.Open > prev.High && c.Close < prevMid && c.Close > prev.Open)
                    return Build(SdkCD.Bearish, SdkCT.Normal, SdkCP.DarkCloudCover,
                        2, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Tweezers: two lows (or highs) equal within a twentieth of the bars' average range,
                // TA-Lib's "equal". It was 0.1% of the price, which on bitcoin is about $80 — a
                // fifth of an hour's typical range, so lows $70 apart counted as the same.
                double tol = EqualTolerance(recent, here, c);

                // Tweezer Bottom, after a decline
                if (down2 == true && Math.Abs(c.Low - prev.Low) <= tol && isBullish && !prevBullish)
                    return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.TweezerBottom,
                        2, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Tweezer Top, after an advance
                if (down2 == false && Math.Abs(c.High - prev.High) <= tol && !isBullish && prevBullish)
                    return Build(SdkCD.Bearish, SdkCT.Normal, SdkCP.TweezerTop,
                        2, bodyPct, upperPct, lowerPct, changePct, reversal: true);
            }

            // ── Single-bar patterns ──────────────────────────────────────────
            //
            // The trend into this bar: the bars before it, never the bar itself, which is the
            // candidate reversal — a strong hammer close must not drag the trend measurement up
            // and then be labelled as reversing the trend it just created.
            bool? down1 = TrendBefore(recent, here, 1);

            // Doji family. A doji is indecision and leans neither way, but a dragonfly after a
            // fall and a gravestone after a rise are the reversal readings Nison gives them;
            // anywhere else they are the shape and no more.
            if (bodyPct < _t.DojiBodyMaxPercent)
            {
                SdkCT dojiType = SdkCT.Doji;
                if (lowerPct > 40 && upperPct < 8) dojiType = SdkCT.DragonflyDoji;
                else if (upperPct > 40 && lowerPct < 8) dojiType = SdkCT.GravestoneDoji;
                else if (upperPct > 25 && lowerPct > 25) dojiType = SdkCT.LongLeggedDoji;

                if (dojiType == SdkCT.DragonflyDoji && down1 == true)
                    return Build(SdkCD.Bullish, dojiType, SdkCP.None, 1, bodyPct, upperPct, lowerPct, changePct, reversal: true);
                if (dojiType == SdkCT.GravestoneDoji && down1 == false)
                    return Build(SdkCD.Bearish, dojiType, SdkCP.None, 1, bodyPct, upperPct, lowerPct, changePct, reversal: true);
                return Build(SdkCD.Neutral, dojiType, SdkCP.None, 1, bodyPct, upperPct, lowerPct, changePct);
            }

            // Marubozu: a body that is nearly all of its range AND long against the bars before
            // (TA-Lib). A $2 bar with a $2 body was one until 2026-11-04.
            double? avgBody = AvgBody(recent, here, 0);
            if (bodyPct >= _t.MarubozuBodyMinPercent && (avgBody == null || bodySize >= avgBody.Value))
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
            //
            // With no trend to read, neither name is given. Until 2026-11-04 an unknown trend
            // made the long lower wick a "Hanging man" and the long upper wick an "Inverted
            // hammer", one bearish name and one bullish for the same lack of evidence.
            //
            // Direction here is the shape's IMPLICATION, not the candle's own colour: a hanging man
            // is bearish even though it can close green, and that is the entire point of the name.

            // Hammer (ends a decline, bullish) / Hanging Man (ends an advance, bearish)
            bool bodyInUpperZone = (Math.Min(c.Open, c.Close) - c.Low) / range * 100.0 > (100.0 - _t.HammerBodyUpperZonePercent);
            if (down1.HasValue && lowerWick > bodySize * _t.WickMultiplierForHammer && upperWick < bodySize && bodyInUpperZone)
            {
                return down1.Value
                    ? Build(SdkCD.Bullish, SdkCT.Hammer, SdkCP.None, 1, bodyPct, upperPct, lowerPct, changePct, reversal: true)
                    : Build(SdkCD.Bearish, SdkCT.HangingMan, SdkCP.None, 1, bodyPct, upperPct, lowerPct, changePct, reversal: true);
            }

            // Inverted Hammer (ends a decline, bullish) / Shooting Star (ends an advance, bearish)
            bool bodyInLowerZone = (c.High - Math.Max(c.Open, c.Close)) / range * 100.0 > (100.0 - _t.HammerBodyUpperZonePercent);
            if (down1.HasValue && upperWick > bodySize * _t.WickMultiplierForHammer && lowerWick < bodySize && bodyInLowerZone)
            {
                return down1.Value
                    ? Build(SdkCD.Bullish, SdkCT.InvertedHammer, SdkCP.None, 1, bodyPct, upperPct, lowerPct, changePct, reversal: true)
                    : Build(SdkCD.Bearish, SdkCT.ShootingStar, SdkCP.None, 1, bodyPct, upperPct, lowerPct, changePct, reversal: true);
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
            SdkCP.AbandonedBabyBullish or SdkCP.AbandonedBabyBearish or
            SdkCP.ThreeWhiteSoldiers   or SdkCP.ThreeBlackCrows;

        private static bool IsContinuationPattern(SdkCP p) => p is
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
            if (r <= 0) return false;                         // no movement is not a doji
            return Math.Abs(b.Close - b.Open) / r * 100.0 < _t.DojiBodyMaxPercent;
        }

        private static double BodyHigh(Ohlcv b) => Math.Max(b.Open, b.Close);
        private static double BodyLow(Ohlcv b) => Math.Min(b.Open, b.Close);
        private static double BodySize(Ohlcv b) => Math.Abs(b.Close - b.Open);

        /// <summary>A bar that exists and moved: its high is above its low.</summary>
        private static bool Moved(Ohlcv? b) => b.HasValue && b.Value.High > b.Value.Low;

        /// <summary>
        /// True if the closes FELL over the <c>TrendLookbackBars</c> bars before a pattern of
        /// <paramref name="barCount"/> bars ending at <paramref name="here"/>, false if they rose,
        /// null if there is not enough history or they ended where they began.
        ///
        /// <para>
        /// The measurement ENDS at the bar before the pattern's first bar. The pattern is the
        /// candidate reversal; including any of it would let its own move set the trend it is then
        /// said to reverse — a strong hammer close dragging the trend up and being called the end
        /// of a rise. It was measured this way for the hammer family only, from the bar before the
        /// classified one; every reversal reads it now, from before its own first bar.
        /// </para>
        ///
        /// <para>
        /// Null rather than a guess. It used to fall back to the previous candle's colour, which is
        /// not a trend: one green bar inside a long decline turned a hammer into a hanging man.
        /// The bars are located by DATE (<see cref="IndexOfCurrent"/>), so a caller holding a longer
        /// series never has the trend read from bars that had not happened yet.
        /// </para>
        /// </summary>
        private bool? TrendBefore(IReadOnlyList<Ohlcv>? recent, int here, int barCount)
        {
            if (recent == null || here < 0) return null;
            int look = Math.Max(1, _t.TrendLookbackBars);
            int end = here - barCount;
            int start = end - look;
            if (start < 0 || end >= recent.Count) return null;
            double a = recent[start].Close, b = recent[end].Close;
            if (b < a) return true;
            if (b > a) return false;
            return null;                                      // dead flat: no trend to interrupt
        }

        /// <summary>
        /// The average BODY of the last <c>AverageLookbackBars</c> bars that moved before the bar
        /// <paramref name="back"/> places before <paramref name="here"/>; null with fewer than
        /// <c>EqualLookbackBars</c> of them in the window, when the bar is judged by its own range
        /// alone. Bars that never moved are skipped, not counted: a run of silent hours would
        /// otherwise make every bar after it "long".
        /// </summary>
        private double? AvgBody(IReadOnlyList<Ohlcv>? recent, int here, int back)
            => Average(recent, here - back, Math.Max(1, _t.AverageLookbackBars), b => BodySize(b));

        /// <summary>
        /// How close two highs or lows must be to count as equal: a fraction of the average range of
        /// the bars before (TA-Lib), or 0.1% of the price with too little history.
        /// </summary>
        private double EqualTolerance(IReadOnlyList<Ohlcv>? recent, int here, Ohlcv c)
        {
            var avgRange = Average(recent, here - 1, Math.Max(1, _t.EqualLookbackBars), b => b.High - b.Low);
            return avgRange.HasValue
                ? avgRange.Value * _t.EqualFractionOfAverageRange
                : c.Close * _t.TweezerTolerancePercent / 100.0;
        }

        private double? Average(IReadOnlyList<Ohlcv>? recent, int bar, int lookback, Func<Ohlcv, double> of)
        {
            if (recent == null || bar < 0 || bar > recent.Count) return null;
            double sum = 0; int n = 0;
            // The last `lookback` bars that MOVED, however far back that reaches in the window:
            // counting slots instead let two silent hours shrink a five-bar average to three
            // bars, and then to none, so the 0.1%-of-price fallback decided most tweezers on a
            // thin pair.
            for (int i = bar - 1; i >= 0 && n < lookback; i--)
            {
                var b = recent[i];
                if (b.High <= b.Low) continue;
                sum += of(b); n++;
            }
            return n >= Math.Min(lookback, Math.Max(1, _t.EqualLookbackBars)) ? sum / n : null;
        }

        /// <summary>
        /// A long body of the given colour: at least <c>LargeBodyMinPercent</c> of its own range,
        /// and, when there is history to compare with, at least the average body before it.
        /// </summary>
        private bool IsLargeBody(Ohlcv bar, bool bullish, double? avgBody)
        {
            double r = bar.High - bar.Low;
            if (r <= 0) return false;
            double body = Math.Abs(bar.Close - bar.Open);
            return body / r * 100.0 >= _t.LargeBodyMinPercent
                && (bar.Close >= bar.Open) == bullish
                && (avgBody == null || body >= avgBody.Value);
        }

        /// <summary>
        /// A short body: under the average body before it, or, with no history, under
        /// <c>SmallBodyMaxPercent</c> of its own range. A bar that never moved is neither.
        /// </summary>
        private bool IsSmallBody(Ohlcv bar, double? avgBody)
        {
            double r = bar.High - bar.Low;
            if (r <= 0) return false;
            double body = Math.Abs(bar.Close - bar.Open);
            return avgBody.HasValue
                ? body < avgBody.Value
                : body / r * 100.0 < _t.SmallBodyMaxPercent;
        }
    }
}
