using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Default <see cref="IRiskPlanResolver"/>. Implements the side-symmetric stop/target math
    /// for the price-only stop sources (PercentOfPrice, AtrMultiple, BelowSwingLow, Fixed) and
    /// the price-only target sources (RiskRewardMultiple, PercentOfPrice, Fixed). Phase-4
    /// sources (BelowSupport, BelowKijun, BelowKumo, NextResistance) are implemented in
    /// Session C against <see cref="ILevelService"/> — the resolver consults the registered
    /// level providers (drawn horizontals, swing pivots, Cipher SR pivots, Ichimoku Kijun/Kumo)
    /// to compute the stop/target price. <c>BelowLvn</c> / <c>NextHvn</c> / <c>NextHvn</c> /
    /// <c>Poc</c> / <c>Vah</c> / <c>FibExtension</c> still return null pending VPVR/TPO
    /// integration in a follow-up session.
    /// </summary>
    public class RiskPlanResolver : IRiskPlanResolver
    {
        private readonly ILevelService? _levels;

        public RiskPlanResolver(ILevelService? levels = null)
        {
            _levels = levels;
        }

        public ResolvedRiskPlan? Resolve(
            RiskPlan plan,
            OrderSide side,
            IReadOnlyList<Ohlcv> history,
            WorkspaceState state)
        {
            if (plan == null || history.Count == 0) return null;

            // Entry: only Immediate is honored at resolver level. Non-Immediate triggers go
            // through the entry-armed state machine in ConfigurableStrategy — see Session B.
            double entry = history[^1].Close;

            // Resolve the stop. Returns NaN if the plan references an unimplemented source.
            double stop = ResolveStop(plan.Stop, side, entry, history, state);
            if (double.IsNaN(stop)) return null;

            // Stop must be on the loss side of entry — invalid plans (stop above entry on a long)
            // are rejected here so callers don't have to defensive-check downstream.
            double riskPerUnit = side == OrderSide.Buy ? entry - stop : stop - entry;
            if (riskPerUnit <= 0.0) return null;

            // Resolve the TP ladder. Each rung is independent — early rungs may be valid even
            // if a later rung references an unimplemented source. Drop unresolved rungs.
            var tpPrices = new List<double>();
            var portions = new List<double>();
            foreach (var rung in plan.TpLadder)
            {
                double tp = ResolveTarget(rung, side, entry, riskPerUnit, history, state);
                if (double.IsNaN(tp)) continue;
                tpPrices.Add(tp);
                portions.Add(Math.Clamp(rung.ClosePortion, 0.0, 1.0));
            }
            if (tpPrices.Count == 0) return null;

            // First-target reward/risk ratio is the gate.
            double firstTpReward = side == OrderSide.Buy ? tpPrices[0] - entry : entry - tpPrices[0];
            double rr = firstTpReward / riskPerUnit;
            if (rr < plan.MinRewardRiskRatio) return null;

            double qty = ResolveQuantity(plan.Sizing, plan.NotionalEquity, riskPerUnit);
            double riskCash = qty * riskPerUnit;

            string notes = BuildNotes(plan.Stop, side, stop, history);

            return new ResolvedRiskPlan(
                EntryPrice: entry,
                StopPrice: stop,
                TpPrices: tpPrices,
                ClosePortions: portions,
                Quantity: qty,
                RewardRiskRatio: rr,
                RiskCash: riskCash,
                Notes: notes);
        }

        // ── Stop placement ────────────────────────────────────────────────────

        private double ResolveStop(StopSource s, OrderSide side, double entry,
            IReadOnlyList<Ohlcv> history, WorkspaceState state)
        {
            int sign = side == OrderSide.Buy ? -1 : +1; // longs = stop below, shorts = stop above

            switch (s.Kind)
            {
                case StopSourceKind.PercentOfPrice:
                {
                    double dist = entry * (s.PercentValue / 100.0);
                    return entry + sign * dist - sign * s.BufferTicks;
                }
                case StopSourceKind.AtrMultiple:
                {
                    double atr = ComputeAtr(history, s.AtrPeriod);
                    if (double.IsNaN(atr) || atr <= 0) return double.NaN;
                    double dist = atr * s.AtrMultiple;
                    return entry + sign * dist - sign * s.BufferTicks;
                }
                case StopSourceKind.BelowSwingLow:
                {
                    int n = Math.Min(s.LookbackBars, history.Count);
                    if (n <= 0) return double.NaN;
                    double extreme = side == OrderSide.Buy ? double.PositiveInfinity : double.NegativeInfinity;
                    for (int i = history.Count - n; i < history.Count; i++)
                    {
                        if (side == OrderSide.Buy)
                            extreme = Math.Min(extreme, history[i].Low);
                        else
                            extreme = Math.Max(extreme, history[i].High);
                    }
                    if (double.IsInfinity(extreme)) return double.NaN;
                    return extreme + sign * s.BufferTicks;
                }
                case StopSourceKind.Fixed:
                    return s.FixedPrice;

                // ── Phase-4 sources implemented Session C via ILevelService ──
                case StopSourceKind.BelowSupport:
                {
                    if (_levels == null) return double.NaN;
                    // For longs we want the nearest level *below* entry that could act as support;
                    // for shorts we want the nearest level *above* entry that could act as resistance.
                    var lvl = side == OrderSide.Buy
                        ? _levels.NearestBelow(entry, history, state)
                        : _levels.NearestAbove(entry, history, state);
                    if (lvl == null) return double.NaN;
                    return lvl.Price + sign * s.BufferTicks;
                }
                case StopSourceKind.BelowKijun:
                {
                    if (_levels == null) return double.NaN;
                    var lvl = FirstLevelOfKind(history, state, LevelKind.Kijun);
                    if (lvl == null) return double.NaN;
                    return lvl.Price + sign * s.BufferTicks;
                }
                case StopSourceKind.BelowKumo:
                {
                    if (_levels == null) return double.NaN;
                    // Long: stop below the cloud's lower edge. Short: stop above the cloud's upper edge.
                    var lvl = side == OrderSide.Buy
                        ? FirstLevelOfKind(history, state, LevelKind.KumoBottom)
                        : FirstLevelOfKind(history, state, LevelKind.KumoTop);
                    if (lvl == null) return double.NaN;
                    return lvl.Price + sign * s.BufferTicks;
                }

                case StopSourceKind.BelowLvn:
                {
                    if (_levels == null) return double.NaN;
                    // Long: place stop just below the nearest LVN below entry — LVNs are
                    // breakout-acceleration zones, so a stop below one is rarely re-tested.
                    // Short: mirror with the nearest LVN above entry.
                    var lvl = side == OrderSide.Buy
                        ? _levels.NearestBelow(entry, history, state, LevelKind.Lvn)
                        : _levels.NearestAbove(entry, history, state, LevelKind.Lvn);
                    if (lvl == null) return double.NaN;
                    return lvl.Price + sign * s.BufferTicks;
                }

                default:
                    return double.NaN;
            }
        }

        // ── Target placement ─────────────────────────────────────────────────

        private double ResolveTarget(TpLadderRung r, OrderSide side, double entry, double riskPerUnit,
            IReadOnlyList<Ohlcv> history, WorkspaceState state)
        {
            int sign = side == OrderSide.Buy ? +1 : -1;
            switch (r.Kind)
            {
                case TargetSourceKind.RiskRewardMultiple:
                    return entry + sign * (riskPerUnit * r.Multiple);
                case TargetSourceKind.PercentOfPrice:
                    return entry + sign * (entry * (r.PercentValue / 100.0));
                case TargetSourceKind.Fixed:
                    return r.FixedPrice;

                case TargetSourceKind.NextResistance:
                {
                    if (_levels == null) return double.NaN;
                    // Long: nearest level above entry. Short: nearest level below entry.
                    var lvl = side == OrderSide.Buy
                        ? _levels.NearestAbove(entry, history, state)
                        : _levels.NearestBelow(entry, history, state);
                    return lvl?.Price ?? double.NaN;
                }

                case TargetSourceKind.NextHvn:
                {
                    if (_levels == null) return double.NaN;
                    var lvl = side == OrderSide.Buy
                        ? _levels.NearestAbove(entry, history, state, LevelKind.Hvn)
                        : _levels.NearestBelow(entry, history, state, LevelKind.Hvn);
                    return lvl?.Price ?? double.NaN;
                }
                case TargetSourceKind.Poc:
                {
                    if (_levels == null) return double.NaN;
                    // POC is direction-neutral — for longs we want a POC above entry, for shorts
                    // a POC below. Falls back to the *nearest* POC of either direction so
                    // mean-reversion strategies can target a POC even when several profiles overlap.
                    var lvl = side == OrderSide.Buy
                        ? _levels.NearestAbove(entry, history, state, LevelKind.Poc)
                        : _levels.NearestBelow(entry, history, state, LevelKind.Poc);
                    return lvl?.Price ?? double.NaN;
                }
                case TargetSourceKind.Vah:
                {
                    if (_levels == null) return double.NaN;
                    // Long → VAH above. Short → VAL below (reuses VAL kind, since "Vah" enum
                    // value semantically means "value-area boundary in the trade direction").
                    var kind = side == OrderSide.Buy ? LevelKind.Vah : LevelKind.Val;
                    var lvl = side == OrderSide.Buy
                        ? _levels.NearestAbove(entry, history, state, kind)
                        : _levels.NearestBelow(entry, history, state, kind);
                    return lvl?.Price ?? double.NaN;
                }
                case TargetSourceKind.FibExtension:
                {
                    // Compute the most recent measured swing (high-to-low or low-to-high) over
                    // the last ~50 bars and project the configured Fib level (default 1.618).
                    // Independent of ILevelService — pure history-derived math.
                    return ComputeFibExtension(history, side, r.FibLevel);
                }

                default:
                    return double.NaN;
            }
        }

        // ── Sizing ────────────────────────────────────────────────────────────

        private static double ResolveQuantity(PositionSizing sizing, double equity, double riskPerUnit)
        {
            switch (sizing.Mode)
            {
                case SizingMode.FixedRiskPercent:
                {
                    if (riskPerUnit <= 0) return 0.0;
                    return Math.Max(0.0, equity * sizing.RiskPercent / riskPerUnit);
                }
                case SizingMode.FixedRiskCash:
                {
                    if (riskPerUnit <= 0) return 0.0;
                    return Math.Max(0.0, sizing.RiskCash / riskPerUnit);
                }
                case SizingMode.FixedQuantity:
                default:
                    return Math.Max(0.0, sizing.FixedQuantity);
            }
        }

        // ── Fib extension helper ──────────────────────────────────────────────

        /// <summary>
        /// Projects a Fibonacci extension target from the most recent significant swing.
        /// For longs: finds the lowest low and the highest high in the last ~50 bars where
        /// the high comes after the low (impulse leg up), then targets entry + (highHigh - lowLow) × FibLevel.
        /// Mirror for shorts. Returns NaN when no valid swing exists.
        /// </summary>
        private static double ComputeFibExtension(IReadOnlyList<Ohlcv> history, OrderSide side, double fibLevel)
        {
            const int lookback = 50;
            if (history.Count < 5) return double.NaN;
            int from = System.Math.Max(0, history.Count - lookback);

            int lowIdx = from, highIdx = from;
            double lowVal = history[from].Low, highVal = history[from].High;
            for (int i = from + 1; i < history.Count; i++)
            {
                if (history[i].Low  < lowVal)  { lowVal  = history[i].Low;  lowIdx  = i; }
                if (history[i].High > highVal) { highVal = history[i].High; highIdx = i; }
            }

            if (side == OrderSide.Buy)
            {
                // Need an upward swing — high must come after low.
                if (highIdx <= lowIdx) return double.NaN;
                double range = highVal - lowVal;
                if (range <= 0) return double.NaN;
                return lowVal + range * fibLevel;
            }
            else
            {
                // Need a downward swing — low must come after high.
                if (lowIdx <= highIdx) return double.NaN;
                double range = highVal - lowVal;
                if (range <= 0) return double.NaN;
                return highVal - range * fibLevel;
            }
        }

        // ── ATR helper ────────────────────────────────────────────────────────

        private static double ComputeAtr(IReadOnlyList<Ohlcv> history, int period)
        {
            if (history.Count < period + 1) return double.NaN;
            double sum = 0.0;
            for (int i = history.Count - period; i < history.Count; i++)
            {
                var bar  = history[i];
                var prev = history[i - 1];
                double tr = Math.Max(bar.High - bar.Low,
                            Math.Max(Math.Abs(bar.High - prev.Close),
                                     Math.Abs(bar.Low  - prev.Close)));
                sum += tr;
            }
            return sum / period;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private PriceLevel? FirstLevelOfKind(IReadOnlyList<Ohlcv> history, WorkspaceState state, LevelKind kind)
        {
            if (_levels == null) return null;
            foreach (var lvl in _levels.GetAllLevels(history, state))
                if (lvl.Kind == kind) return lvl;
            return null;
        }

        private static string BuildNotes(StopSource s, OrderSide side, double stop, IReadOnlyList<Ohlcv> history)
        {
            string sideWord = side == OrderSide.Buy ? "below" : "above";
            return s.Kind switch
            {
                StopSourceKind.PercentOfPrice => $"Stop {sideWord} entry by {s.PercentValue:F2}%",
                StopSourceKind.AtrMultiple    => $"Stop {sideWord} entry at {s.AtrMultiple:F1} × ATR({s.AtrPeriod})",
                StopSourceKind.BelowSwingLow  => $"Stop {sideWord} {s.LookbackBars}-bar swing extreme at {stop:F4}",
                StopSourceKind.Fixed          => $"Stop fixed at {stop:F4}",
                StopSourceKind.BelowSupport   => $"Stop {sideWord} nearest S/R level at {stop:F4}",
                StopSourceKind.BelowKijun     => $"Stop {sideWord} Ichimoku Kijun at {stop:F4}",
                StopSourceKind.BelowKumo      => $"Stop {sideWord} Ichimoku Kumo at {stop:F4}",
                StopSourceKind.BelowLvn       => $"Stop {sideWord} nearest volume profile LVN at {stop:F4}",
                _                             => $"Stop at {stop:F4}"
            };
        }
    }
}
