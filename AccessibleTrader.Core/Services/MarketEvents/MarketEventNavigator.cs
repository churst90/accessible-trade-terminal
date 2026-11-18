using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.MarketEvents
{
    /// <summary>
    /// Apostrophe / Shift+apostrophe: jump to the next / previous bar carrying a market event
    /// that passes the setting, the way comma and period move between chart formations.
    ///
    /// <para>
    /// The jump is routed as ordinary navigation with <c>IsJump</c>, so the bar is read by the
    /// same composition an arrow key or a formation jump uses — including its events clause. A
    /// bespoke sentence here would drift out of step with that wording.
    /// </para>
    /// </summary>
    public sealed class MarketEventNavigator
    {
        private readonly IWorkspaceStore _store;
        private readonly IEventBus _eventBus;
        private readonly IMarketEventBarMapCache _map;

        public MarketEventNavigator(
            IWorkspaceStore store,
            IEventBus eventBus,
            IMarketEventBarMapCache? map = null)
        {
            _store = store;
            _eventBus = eventBus;
            _map = map ?? new MarketEventBarMapCache();
        }

        /// <summary>Move to the next (forward) or previous bar with a passing event.</summary>
        public void Jump(bool forward)
        {
            var state = _store.State;
            var level = MarketEventLevels.Parse(state.MarketEventsLevel);

            // Never silent. These keys were free until 2026-11-18; a key that moves the cursor
            // or does nothing with no explanation is indistinguishable from a broken binding.
            if (level == MarketEventLevel.Off)
            {
                _eventBus.Publish(new FeedbackRequestEvent(
                    FeedbackType.Boundary,
                    "Market events are off. Turn them on in Settings, Narration."));
                return;
            }

            var data = state.Data;
            if (data == null || data.Count == 0)
            {
                _eventBus.Publish(new FeedbackRequestEvent(
                    FeedbackType.Boundary,
                    forward ? "No later market events." : "No earlier market events."));
                return;
            }

            var kinds = MarketEventSettings.Kinds(state);
            var market = state.SelectedMarketType;
            var placements = _map.For(state, DateTime.UtcNow);

            int current = state.CurrentDataIndex;
            int target = forward
                ? placements.Where(p => Passes(p, level, kinds, market) && p.BarIndex > current)
                    .Select(p => p.BarIndex).DefaultIfEmpty(-1).Min()
                : placements.Where(p => Passes(p, level, kinds, market) && p.BarIndex < current)
                    .Select(p => p.BarIndex).DefaultIfEmpty(-1).Max();

            if (target < 0)
            {
                _eventBus.Publish(new FeedbackRequestEvent(
                    FeedbackType.Boundary,
                    forward ? "No later market events." : "No earlier market events."));
                return;
            }

            _store.Dispatch(new NavigateAction(target));
            _store.Dispatch(new SetInteractionContextAction(InteractionContext.Component));
            _eventBus.Publish(new FeedbackRequestEvent(
                FeedbackType.Navigation, "", true, IsXMove: true, IsJump: true));
        }

        private static bool Passes(
            MarketEventBarPlacement p,
            MarketEventLevel level,
            MarketEventKindFilter kinds,
            MarketType market)
            => p.IsPlaced
            && p.Event.Touches(market)
            && MarketEventLevels.Includes(level, p.Event.Importance)
            && kinds.Includes(p.Event.Kind);
    }
}
