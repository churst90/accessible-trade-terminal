using System;
using AccessibleTrader.Sdk.Models;

// Aliases to avoid ambiguity with AccessibleTrader.Core.Services.CandleAnalysis
using SdkCA  = AccessibleTrader.Sdk.Analysis.CandleAnalysis;
using SdkCD  = AccessibleTrader.Sdk.Analysis.CandleDirection;
using SdkCT  = AccessibleTrader.Sdk.Analysis.CandleType;
using SdkCP  = AccessibleTrader.Sdk.Analysis.CandlePattern;
using SdkThr = AccessibleTrader.Sdk.Analysis.CandlePatternThresholds;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// Implements ISdkCandlePatternAnalyzer using configurable CandlePatternThresholds.
    /// Recognises single-bar (Doji, Hammer, ShootingStar, Marubozu, SpinningTop),
    /// two-bar (Engulfing, Harami, PiercingLine, DarkCloudCover, TweezerBottom/Top),
    /// and three-bar (MorningStar, EveningStar, ThreeWhiteSoldiers, ThreeBlackCrows) patterns.
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

            // ── Three-bar patterns (highest priority) ────────────────────────
            if (p1.HasValue && p2.HasValue)
            {
                var pa2 = p2.Value;
                var pa1 = p1.Value;

                // Morning Star. The STAR — the small middle body — must sit BELOW the first bar's
                // body; that separation is what makes the shape a star rather than three ordinary
                // bars. Classically it is a gap, which 24/7 crypto never produces, so the test here
                // is body-below-body rather than a true gap. Without any such test the pattern fires
                // on [long red, small body, green closing above the midpoint] no matter where the
                // middle bar sits — including above the first bar's open, which is not the pattern.
                if (IsLargeBody(pa2, false) && IsSmallBody(pa1) && isBullish
                    && BodyHigh(pa1) < BodyLow(pa2) + BodySize(pa2) * StarBodyOverlapAllowed
                    && c.Close > (pa2.Open + pa2.Close) / 2.0)
                    return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.MorningStar,
                        3, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Evening Star — the mirror.
                if (IsLargeBody(pa2, true) && IsSmallBody(pa1) && !isBullish
                    && BodyLow(pa1) > BodyHigh(pa2) - BodySize(pa2) * StarBodyOverlapAllowed
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
            SdkCP.BullishEngulfing or SdkCP.BearishEngulfing or
            SdkCP.BullishHarami    or SdkCP.BearishHarami    or
            SdkCP.MorningStar      or SdkCP.EveningStar       or
            SdkCP.TweezerBottom    or SdkCP.TweezerTop        or
            SdkCP.PiercingLine     or SdkCP.DarkCloudCover;

        private static bool IsContinuationPattern(SdkCP p) => p is
            SdkCP.ThreeWhiteSoldiers or SdkCP.ThreeBlackCrows;

        /// <summary>
        /// How much of the first bar's body the star is allowed to overlap. A true gap is 0, but
        /// 24/7 markets do not gap, so a small tolerance keeps the pattern findable on crypto while
        /// still requiring the star to sit clearly outside the body it is reversing.
        /// </summary>
        private const double StarBodyOverlapAllowed = 0.10;

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
            return bodyPct >= 50.0 && (bar.Close >= bar.Open) == bullish;
        }

        private static bool IsSmallBody(Ohlcv bar)
        {
            double r = bar.High - bar.Low;
            if (r <= 0) return true;
            return Math.Abs(bar.Close - bar.Open) / r * 100.0 < 30.0;
        }
    }
}
