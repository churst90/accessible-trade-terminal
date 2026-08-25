using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Pins the web-safe tab-switching entry point. Ctrl+Tab / Ctrl+Number are
    /// reserved by the browser, so <see cref="SystemCommand.FocusTabBar"/>
    /// (Ctrl+Alt+Shift+T) is the keyboard path onto the workspace tab switcher bar.
    /// The dispatcher publishes <see cref="FocusTabBarEvent"/> whenever pressed; the
    /// tab bar always renders (even with a single tab) so the user can reach the
    /// new-tab button (Insert) from the keyboard.
    /// </summary>
    public class FocusTabBarDispatchTests
    {
        private static ChartIdentity BTC => new("Spot", "Binance", "BTCUSDT", "1h");

        private static (CommandDispatcher dispatcher, EventBus bus, WorkspaceStore store) Build()
        {
            var bus = new EventBus();
            var store = new WorkspaceStore(
                bus,
                new MockViewportRangeCalculator(),
                new MockViewportNavigationService(),
                new MockVolumeStateService());
            var nav = Substitute.For<INavigationEngine>();
            var bar = Substitute.For<IBarDetailService>();
            var crossing = new IndicatorCrossingEngine(store, bus);
            var dispatcher = new CommandDispatcher(bus, nav, store, bar, crossing);
            return (dispatcher, bus, store);
        }

        [Fact]
        public void FocusTabBar_PublishesEvent_WhenMultipleTabsOpen()
        {
            var (dispatcher, bus, store) = Build();
            // Prime a single BTC tab, then add a second so the switcher bar would render.
            store.Dispatch(new UpdateSettingsAction(_ => WorkspaceState.Initial with
            {
                Identity = BTC,
                ActiveTabIndex = 0,
                TabSnapshots = ImmutableList<TabSnapshot>.Empty
            }));
            store.Dispatch(new AddTabAction());
            Assert.Equal(2, store.State.TabCount);

            var captured = new List<FocusTabBarEvent>();
            bus.Subscribe<FocusTabBarEvent>(captured.Add);

            dispatcher.Dispatch(SystemCommand.FocusTabBar);

            Assert.Single(captured);
        }

        [Fact]
        public void FocusTabBar_PublishesEvent_EvenWithSingleTab()
        {
            var (dispatcher, bus, store) = Build();
            Assert.Equal(1, store.State.TabCount);

            var focus = new List<FocusTabBarEvent>();
            bus.Subscribe<FocusTabBarEvent>(focus.Add);

            dispatcher.Dispatch(SystemCommand.FocusTabBar);

            // The bar always renders, so focusing it is always actionable (Insert = new tab).
            Assert.Single(focus);
        }
    }
}
