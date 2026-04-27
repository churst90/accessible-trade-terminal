using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Trade-confidence scorer. Takes a fired strategy plus the indicator
    /// readings at the fire-bar and returns a 0-100 confidence score.
    ///
    /// Why exist: a strategy fire is binary (fired or not), but two fires
    /// of the same strategy can be very different in quality. v23 LONG
    /// firing while Hurst is 0.30 (deeply mean-reverting) AND price is at
    /// a Pivot S2 AND AVWAP-bias is bullish is *much* higher conviction
    /// than v23 LONG firing in a trending regime mid-range. The ranker
    /// surfaces that quality difference numerically so the user can
    /// triage signals without having to read each indicator individually.
    ///
    /// Inputs are deliberately simple — caller passes a snapshot of the
    /// indicator readings at the fire-bar (which the harness already
    /// computes for feature-snapshot CSV exports). No I/O, no DI, no
    /// async — pure function for easy testing.
    ///
    /// Scoring is empirical: each signal contributes a bounded amount
    /// (0-25 typically) and the total is clamped to [0, 100]. The base
    /// strategy itself is the bedrock signal (40 points if it fired);
    /// gates layer on top. A tagged-but-imperfect setup might score 55;
    /// a high-conviction multi-confluence setup might score 90.
    /// </summary>
    public static class TradeRanker
    {
        /// <summary>
        /// Snapshot of the relevant indicator readings at the fire bar.
        /// Any field can be NaN if that indicator wasn't loaded — the
        /// scorer skips missing inputs cleanly without penalising the score.
        /// </summary>
        public sealed record SignalContext(
            OrderSide Side,
            double HurstValue          = double.NaN, // HURST.Hurst, range 0..1
            double AvwapBias           = double.NaN, // ANCHORED_VWAP.AVWAP Bias, -1/0/+1
            double PivotZone           = double.NaN, // PIVOTS.Pivot Zone, -1/0/+1
            double AnchorWave          = double.NaN, // CIPHER_B.Anchor Wave
            double AboveSma200         = double.NaN, // REGIME.AboveSma200 (signed dist from MA)
            double Funding             = double.NaN, // BNVISION_FUNDING.Funding raw
            string? TimeframeMinutes   = null        // bar interval label, optional weight
        );

        /// <summary>
        /// Compute the rank (0-100). Higher = more confident the trade has edge.
        /// </summary>
        public static int Score(SignalContext ctx)
        {
            // Bedrock: strategy fired = 40 points unconditional. The remaining
            // 60 are split between gate alignments. We assume the caller only
            // calls Score() when a strategy actually fired — so 40 is the floor.
            double s = 40.0;

            // Hurst regime: reversal strategies want mean-reverting (H < 0.5).
            // 0.5 = neutral, 0.30 = strongly mean-reverting (max bonus),
            // 0.70 = strongly trending (max penalty).
            if (!double.IsNaN(ctx.HurstValue))
            {
                // Bonus when Hurst aligns with reversal-strategy preference
                // (mean-reverting). Both sides benefit equally — a v23 SHORT
                // also wants mean-reverting since it's a reversal too.
                double hurstAlignment = (0.5 - ctx.HurstValue) * 2.0;   // -1..+1
                s += Math.Clamp(hurstAlignment * 15.0, -10.0, 15.0);
            }

            // AVWAP bias: aligned with side = bullish for longs, bearish for shorts.
            if (!double.IsNaN(ctx.AvwapBias))
            {
                double bias = ctx.Side == OrderSide.Buy ? ctx.AvwapBias : -ctx.AvwapBias;
                s += Math.Clamp(bias * 8.0, -5.0, 8.0);
            }

            // Pivot zone: longs want -1 (at support); shorts want +1 (at resistance).
            // Aligned = +12 (the strongest single bonus); anti-aligned = -8.
            if (!double.IsNaN(ctx.PivotZone))
            {
                double zoneAlign = ctx.Side == OrderSide.Buy ? -ctx.PivotZone : ctx.PivotZone;
                s += Math.Clamp(zoneAlign * 12.0, -8.0, 12.0);
            }

            // Anchor wave: confirms regime washout (longs want negative anchor =
            // recent capitulation; shorts want positive = recent rally).
            if (!double.IsNaN(ctx.AnchorWave))
            {
                double anchorSign = ctx.Side == OrderSide.Buy ? -ctx.AnchorWave : ctx.AnchorWave;
                // Anchor wave magnitude is roughly ±100 in fixed mode. Normalise.
                double normalised = Math.Clamp(anchorSign / 60.0, -1.0, 1.0);
                s += normalised * 8.0;
            }

            // Faber regime: longs want price > SMA200, shorts want price < SMA200.
            if (!double.IsNaN(ctx.AboveSma200))
            {
                double faberAlign = ctx.Side == OrderSide.Buy
                    ? Math.Sign(ctx.AboveSma200)
                    : -Math.Sign(ctx.AboveSma200);
                s += faberAlign * 7.0;
            }

            // Funding (crypto-specific): contrarian — longs benefit when funding
            // is negative (longs paid, retail short-crowded), shorts benefit when
            // funding is positive. Smaller weight because funding signal is noisy.
            if (!double.IsNaN(ctx.Funding))
            {
                double fundingAlign = ctx.Side == OrderSide.Buy
                    ? -Math.Sign(ctx.Funding)
                    :  Math.Sign(ctx.Funding);
                s += fundingAlign * 5.0;
            }

            // Timeframe weight: higher TF = more weight per bar. Small bonus
            // for ≥1d (the empirical sweet spot for v23 family); small penalty
            // for ≤1h (intraday noise).
            if (!string.IsNullOrEmpty(ctx.TimeframeMinutes)
                && int.TryParse(ctx.TimeframeMinutes, out int tfMin))
            {
                if (tfMin >= 1440)      s += 5.0;   // ≥1d
                else if (tfMin >= 240)  s += 0.0;   // 4h baseline
                else if (tfMin >= 60)   s -= 3.0;   // 1h
                else                    s -= 6.0;   // sub-hour
            }

            return (int)Math.Round(Math.Clamp(s, 0.0, 100.0));
        }

        /// <summary>
        /// Convenience: textual confidence band, useful for narration.
        /// </summary>
        public static string ConfidenceBand(int score) => score switch
        {
            >= 85 => "very high",
            >= 70 => "high",
            >= 55 => "moderate",
            >= 40 => "marginal",
            _     => "weak",
        };
    }
}
