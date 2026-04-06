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

        public SdkCA Analyze(Ohlcv current, Ohlcv? previous = null, Ohlcv? twoBarsAgo = null)
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

                // Morning Star
                if (IsLargeBody(pa2, false) && IsSmallBody(pa1) && isBullish
                    && c.Close > (pa2.Open + pa2.Close) / 2.0)
                    return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.MorningStar,
                        3, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Evening Star
                if (IsLargeBody(pa2, true) && IsSmallBody(pa1) && !isBullish
                    && c.Close < (pa2.Open + pa2.Close) / 2.0)
                    return Build(SdkCD.Bearish, SdkCT.Normal, SdkCP.EveningStar,
                        3, bodyPct, upperPct, lowerPct, changePct, reversal: true);

                // Three White Soldiers
                if (isBullish && IsLargeBody(pa1, true) && IsLargeBody(pa2, true)
                    && c.Open > pa1.Open && c.Close > pa1.Close
                    && pa1.Open > pa2.Open && pa1.Close > pa2.Close)
                    return Build(SdkCD.Bullish, SdkCT.Normal, SdkCP.ThreeWhiteSoldiers,
                        3, bodyPct, upperPct, lowerPct, changePct, continuation: true);

                // Three Black Crows
                if (!isBullish && IsLargeBody(pa1, false) && IsLargeBody(pa2, false)
                    && c.Open < pa1.Open && c.Close < pa1.Close
                    && pa1.Open < pa2.Open && pa1.Close < pa2.Close)
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
                if (currBodyHigh < prevBodyHigh && currBodyLow > prevBodyLow && bodyPct < 50)
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

            // Hammer / Hanging Man
            bool bodyInUpperZone = (Math.Min(c.Open, c.Close) - c.Low) / range * 100.0 > (100.0 - _t.HammerBodyUpperZonePercent);
            if (lowerWick > bodySize * _t.WickMultiplierForHammer && upperWick < bodySize && bodyInUpperZone)
            {
                bool priorBearish = p1.HasValue && p1.Value.Close < p1.Value.Open;
                var hmType = priorBearish ? SdkCT.Hammer : SdkCT.HangingMan;
                return Build(priorBearish ? SdkCD.Bullish : dir, hmType, SdkCP.None,
                    1, bodyPct, upperPct, lowerPct, changePct, reversal: priorBearish);
            }

            // Shooting Star / Inverted Hammer
            bool bodyInLowerZone = (c.High - Math.Max(c.Open, c.Close)) / range * 100.0 > (100.0 - _t.HammerBodyUpperZonePercent);
            if (upperWick > bodySize * _t.WickMultiplierForHammer && lowerWick < bodySize && bodyInLowerZone)
            {
                bool priorBullish = p1.HasValue && p1.Value.Close > p1.Value.Open;
                var ssType = priorBullish ? SdkCT.ShootingStar : SdkCT.InvertedHammer;
                return Build(priorBullish ? SdkCD.Bearish : dir, ssType, SdkCP.None,
                    1, bodyPct, upperPct, lowerPct, changePct, reversal: priorBullish);
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
