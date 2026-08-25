using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Trading
{
    /// <summary>
    /// The exit plan a <see cref="StrategySignal"/> carries, expressed as decisions rather
    /// than as bookkeeping: does this bar reach the stop, does it reach the next ladder rung,
    /// how much does that rung close, where does the stop go once the first rung clears, and
    /// where does an ATR trail sit after this bar.
    ///
    /// <para>
    /// ── Why it is one class ────────────────────────────────────────────────────
    /// The backtester has modelled a stop, a multi-rung take-profit ladder, a move to
    /// breakeven and a ratcheting ATR trail since Phase 11. Live, none of it existed:
    /// <c>StrategyEngine.ExecuteSignalAsync</c> built a six-field order and dropped
    /// <c>TpLadder</c>, <c>TpClosePortions</c>, <c>StopAdjust</c>, <c>TrailAtrPeriod</c> and
    /// <c>TrailAtrMultiple</c> on the floor. So the number a strategy was accepted on and the
    /// order it actually placed were two different strategies wearing one name — and the
    /// difference was invisible, because the backtest is the only place the user ever sees
    /// the ladder work.
    /// </para>
    ///
    /// <para>
    /// Writing the rules once, and having both the replay and the live manager drive them,
    /// is what makes that divergence structural rather than a promise. A rule that changes
    /// here changes in both places or in neither; there is no third copy to forget.
    /// </para>
    ///
    /// <para>
    /// Everything here is a pure function of a bar and the position's state. It places no
    /// orders, books no P&amp;L, and holds nothing — the backtester keeps its own equity
    /// ledger and <see cref="Strategies.StrategyPositionManager"/> keeps the live one.
    /// </para>
    /// </summary>
    public static class ManagedExitRules
    {
        /// <summary>
        /// Did this bar's range reach the protective stop? Expressed by the side of the
        /// POSITION: a long is stopped when the low trades down to the level, a short when
        /// the high trades up to it. Touching counts — a stop resting exactly at the low
        /// filled.
        /// </summary>
        public static bool StopHit(OrderSide positionSide, double stop, Ohlcv bar) =>
            positionSide == OrderSide.Buy ? bar.Low <= stop : bar.High >= stop;

        /// <summary>
        /// Did this bar's range reach a take-profit level? Same convention as
        /// <see cref="StopHit"/>, opposite direction.
        /// </summary>
        public static bool TargetHit(OrderSide positionSide, double target, Ohlcv bar) =>
            positionSide == OrderSide.Buy ? bar.High >= target : bar.Low <= target;

        /// <summary>
        /// How much a ladder rung closes. Portions are fractions of the position's INITIAL
        /// quantity, not of what is left — three rungs of 1/3 close the whole position, and
        /// three rungs of 1/3 of the remainder would leave 30% of it riding forever. Capped
        /// at the remainder so the last rung cannot oversell into a short.
        /// </summary>
        public static double CloseQuantity(double remainingQuantity, double initialQuantity, double portion) =>
            Math.Min(remainingQuantity, initialQuantity * portion);

        /// <summary>
        /// The ladder a signal actually carries: its <see cref="StrategySignal.TpLadder"/> when
        /// it set one, otherwise the single <see cref="StrategySignal.TakeProfit"/> closing the
        /// whole position, otherwise nothing at all. A rung with no matching portion splits the
        /// position evenly across the rungs — the same fallback the backtester has always used,
        /// so a strategy that sets prices but no portions behaves identically in both.
        /// </summary>
        public static (Queue<double> Prices, Queue<double> Portions) BuildLadder(StrategySignal signal)
        {
            var prices = new Queue<double>();
            var portions = new Queue<double>();

            if (signal.TpLadder != null && signal.TpLadder.Count > 0)
            {
                for (int t = 0; t < signal.TpLadder.Count; t++)
                {
                    prices.Enqueue(signal.TpLadder[t]);
                    double portion = signal.TpClosePortions != null && t < signal.TpClosePortions.Count
                        ? signal.TpClosePortions[t]
                        : (1.0 / signal.TpLadder.Count);
                    portions.Enqueue(portion);
                }
            }
            else if (signal.TakeProfit.HasValue)
            {
                prices.Enqueue(signal.TakeProfit.Value);
                portions.Enqueue(1.0);
            }

            return (prices, portions);
        }

        /// <summary>
        /// Where the stop goes once the first ladder rung clears.
        /// <see cref="StopAdjustOnTp1.MoveToBreakeven"/> and
        /// <see cref="StopAdjustOnTp1.TrailByAtr"/> both anchor at the entry — the trail then
        /// ratchets away from it bar by bar via <see cref="AtrTrailStop"/> — and
        /// <see cref="StopAdjustOnTp1.None"/> leaves the original stop alone.
        /// </summary>
        public static double? StopAfterFirstTarget(StopAdjustOnTp1 mode, double entryPrice, double? currentStop) =>
            mode switch
            {
                StopAdjustOnTp1.MoveToBreakeven => entryPrice,
                StopAdjustOnTp1.TrailByAtr      => entryPrice,
                _                               => currentStop,
            };

        /// <summary>
        /// The ATR trailing stop after <paramref name="barIndex"/> closes, ratcheted: it moves
        /// only in the position's favour and never retreats, so a trail cannot widen a stop the
        /// market has already walked past.
        ///
        /// <para>
        /// The average is a plain mean of true range over <paramref name="period"/> bars ending
        /// at <paramref name="barIndex"/>. That is the arithmetic the backtester has always run,
        /// and it is shared verbatim rather than "corrected" to Wilder's smoothing here: the
        /// point of this method is that the live trail sits where the replayed one sat. Changing
        /// the average is a change to both, deliberately, in one place.
        /// </para>
        ///
        /// <para>Returns <paramref name="currentStop"/> unchanged whenever the trail cannot be
        /// computed — too few bars, a nonsense period — so a caller can assign the result
        /// unconditionally.</para>
        /// </summary>
        public static double AtrTrailStop(
            IReadOnlyList<Ohlcv> bars,
            int barIndex,
            int period,
            double multiple,
            OrderSide positionSide,
            double currentStop)
        {
            if (bars == null || period <= 0 || !double.IsFinite(multiple)) return currentStop;
            if (barIndex < period || barIndex >= bars.Count) return currentStop;

            double atrSum = 0;
            for (int a = barIndex - period + 1; a <= barIndex; a++)
            {
                double tr = bars[a].High - bars[a].Low;
                if (a > 0)
                {
                    tr = Math.Max(tr, Math.Abs(bars[a].High - bars[a - 1].Close));
                    tr = Math.Max(tr, Math.Abs(bars[a].Low  - bars[a - 1].Close));
                }
                atrSum += tr;
            }

            double atr = atrSum / period;
            if (!double.IsFinite(atr)) return currentStop;

            double trailDistance = atr * multiple;
            double newStop = positionSide == OrderSide.Buy
                ? bars[barIndex].Close - trailDistance
                : bars[barIndex].Close + trailDistance;

            if (!double.IsFinite(newStop)) return currentStop;

            // Ratchet: only ever in the favourable direction.
            if (positionSide == OrderSide.Buy  && newStop > currentStop) return newStop;
            if (positionSide == OrderSide.Sell && newStop < currentStop) return newStop;
            return currentStop;
        }

        /// <summary>The side that closes a position — a long is exited by a sell.</summary>
        public static OrderSide ClosingSide(OrderSide positionSide) =>
            positionSide == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;

        /// <summary>
        /// Quantities smaller than this are treated as fully closed. Mirrors the backtester's
        /// <c>openRemainingQty &lt;= 0.000001</c> test so a ladder that closes the position in
        /// the replay closes it live too, rather than leaving a dust remainder nobody can sell.
        /// </summary>
        public const double QuantityEpsilon = 0.000001;
    }
}
