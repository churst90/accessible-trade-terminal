using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Resolves a <see cref="RiskPlan"/> into concrete entry/stop/TP prices and quantity
    /// given the current bar history and side. Used by <c>ConfigurableStrategy</c> the moment
    /// a setup's conditions evaluate true, before deciding whether the setup clears the
    /// minimum reward/risk gate. Returns null if the plan cannot be resolved (e.g. a
    /// Phase-4 stop source like BelowKijun is requested but not yet implemented).
    /// </summary>
    public interface IRiskPlanResolver
    {
        /// <param name="accountEquity">
        /// The equity to size against, when the caller knows it. Overrides
        /// <see cref="RiskPlan.NotionalEquity"/>, which is a static number typed into
        /// <c>RiskPlanEditor</c> and never reconciled against anything.
        ///
        /// <para>Its absence was the defect: a backtest sized every trade against the same
        /// notional from the first bar to the last, so it had no compounding and no
        /// drawdown-driven size reduction — <c>TotalReturn</c> and <c>MaxDrawdown</c>
        /// described a strategy nobody could trade. Live, the same static number went to the
        /// exchange with no reconciliation against the actual balance, so a user who left the
        /// default 10000 and held a $500 account was sized 20x too large.</para>
        ///
        /// <para>Null keeps the plan's own notional, which is what a caller that genuinely
        /// has no equity figure should pass.</para>
        /// </param>
        ResolvedRiskPlan? Resolve(
            RiskPlan plan,
            OrderSide side,
            IReadOnlyList<Ohlcv> history,
            WorkspaceState state,
            double? accountEquity = null);
    }
}
