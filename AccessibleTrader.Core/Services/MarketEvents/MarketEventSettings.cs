using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.MarketEvents
{
    /// <summary>The setting as the workspace mirror carries it: one level and five kind boxes.</summary>
    public static class MarketEventSettings
    {
        public static MarketEventLevel Level(WorkspaceState state)
            => MarketEventLevels.Parse(state.MarketEventsLevel);

        public static MarketEventKindFilter Kinds(WorkspaceState state) => new(
            Economy: state.MarketEventsEconomy,
            Fed: state.MarketEventsFed,
            Expiries: state.MarketEventsExpiries,
            Holidays: state.MarketEventsHolidays,
            Halvings: state.MarketEventsHalvings);

        /// <summary>The event ids the user asked to be alerted about by name.</summary>
        public static IReadOnlyList<string> Watched(WorkspaceState state)
            => state.MarketEventsWatched ?? Array.Empty<string>();
    }
}
