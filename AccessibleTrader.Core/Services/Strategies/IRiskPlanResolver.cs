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
        ResolvedRiskPlan? Resolve(
            RiskPlan plan,
            OrderSide side,
            IReadOnlyList<Ohlcv> history,
            WorkspaceState state);
    }
}
