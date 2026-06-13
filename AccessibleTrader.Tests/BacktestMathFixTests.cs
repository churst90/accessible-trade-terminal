using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Core.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Pins for the 2026-06-12 backtester math fixes:
    /// (1) Sharpe annualisation derives periods-per-year from the equity curve's actual
    ///     time span instead of a hardcoded √252 — the old constant understated intraday
    ///     Sharpe by ~20× and overstated weekly backtests;
    /// (2) RiskPlanResolver normalises TP-ladder portions whose SUM exceeds 1.0 — per-rung
    ///     clamping let [0.5, 0.5, 0.5] through and the backtester silently shrank the
    ///     later rungs, so the executed ladder differed from the configured one.
    /// </summary>
    public class BacktestMathFixTests
    {
        // ── Sharpe annualisation ────────────────────────────────────────────────

        private static List<(DateTime Date, double EquityValue)> Curve(TimeSpan spacing, int points)
        {
            // Alternating up/down walk: constant per-period return distribution so two
            // curves differing only in spacing have identical mean/std — any difference
            // in Sharpe must come from the annualisation factor alone.
            var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var curve = new List<(DateTime, double)>();
            double equity = 10_000;
            for (int i = 0; i < points; i++)
            {
                curve.Add((start + spacing * i, equity));
                equity *= (i % 2 == 0) ? 1.02 : 0.995;
            }
            return curve;
        }

        [Fact]
        public void Sharpe_ScalesWithObservedSamplingFrequency()
        {
            // Identical return series; hourly spacing has 24× the sampling frequency of
            // daily, so annualised Sharpe must be √24 higher — under the old hardcoded
            // √252 both would have reported the SAME value.
            double hourly = StrategyBacktester.ComputeSharpe(Curve(TimeSpan.FromHours(1), 200));
            double daily  = StrategyBacktester.ComputeSharpe(Curve(TimeSpan.FromDays(1), 200));

            Assert.True(hourly > 0 && daily > 0);
            Assert.Equal(Math.Sqrt(24.0), hourly / daily, 3);
        }

        [Fact]
        public void Sharpe_DailySpacing_AnnualisesToCalendarYear()
        {
            // 366 daily points spanning exactly one year → periodsPerYear == returns count
            // → factor √365. Verify against a hand-computed expectation.
            var curve = Curve(TimeSpan.FromDays(1), 366);
            var returns = new List<double>();
            for (int i = 1; i < curve.Count; i++)
                returns.Add((curve[i].EquityValue - curve[i - 1].EquityValue) / curve[i - 1].EquityValue);
            double mean = returns.Average();
            double std = Math.Sqrt(returns.Select(r => (r - mean) * (r - mean)).Average());
            double years = (curve[^1].Date - curve[0].Date).TotalDays / 365.25;
            double expected = mean / std * Math.Sqrt(returns.Count / years);

            Assert.Equal(expected, StrategyBacktester.ComputeSharpe(curve), 6);
        }

        [Fact]
        public void Sharpe_ZeroTimeSpan_ReturnsZero()
        {
            var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var degenerate = new List<(DateTime, double)> { (t, 10_000), (t, 10_100), (t, 10_050) };

            Assert.Equal(0.0, StrategyBacktester.ComputeSharpe(degenerate));
        }

        // ── TP-ladder portion normalisation ─────────────────────────────────────

        private static IReadOnlyList<Ohlcv> Bars(int n)
        {
            var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return Enumerable.Range(0, n)
                .Select(i => new Ohlcv(start.AddHours(i), 100, 101, 99, 100, 1000))
                .ToArray();
        }

        private static RiskPlan Plan(params double[] portions) =>
            new(
                Stop: new StopSource(StopSourceKind.PercentOfPrice, PercentValue: 1.0),
                TpLadder: portions
                    .Select((p, i) => new TpLadderRung(
                        TargetSourceKind.RiskRewardMultiple, Multiple: 2.0 + i, ClosePortion: p))
                    .ToList(),
                Sizing: new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005),
                Entry: new EntryTrigger(EntryTriggerKind.Immediate));

        [Fact]
        public void OverAllocatedLadder_IsNormalisedToSumOne_PreservingShape()
        {
            var resolver = new RiskPlanResolver(levels: null);

            var resolved = resolver.Resolve(Plan(0.5, 0.5, 0.5), OrderSide.Buy, Bars(20),
                WorkspaceState.Initial);

            Assert.NotNull(resolved);
            Assert.Equal(1.0, resolved!.ClosePortions.Sum(), 9);
            // Equal rungs stay equal after scaling.
            Assert.All(resolved.ClosePortions, p => Assert.Equal(1.0 / 3.0, p, 9));
        }

        [Fact]
        public void UnderAllocatedLadder_IsLeftUntouched()
        {
            // Sums under 1.0 are intentional — the remainder rides until stop/end-of-data.
            var resolver = new RiskPlanResolver(levels: null);

            var resolved = resolver.Resolve(Plan(0.3, 0.3), OrderSide.Buy, Bars(20),
                WorkspaceState.Initial);

            Assert.NotNull(resolved);
            Assert.Equal(new[] { 0.3, 0.3 }, resolved!.ClosePortions);
        }
    }
}
