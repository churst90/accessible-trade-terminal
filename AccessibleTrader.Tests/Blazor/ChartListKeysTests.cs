using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;
using Bunit;
using NSubstitute;
using Cmp = AccessibleTrader.BlazorClient.Components;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// Alt+C and Alt+L put the focus on the toolbar's Chart type and Price scale lists. Until
/// 2026-11-19 they toggled Heikin Ashi and Log, one entry of each list. Cody: "change alt c and
/// l to focus on those dropdowns".
/// </summary>
public sealed class ChartListKeysTests
{
    private sealed class TempPaths : IPlatformPathService
    {
        public string AppDataDirectory { get; } = TestTemp.NewDir("att-chartlist-");
        public string CacheDirectory => AppDataDirectory;
    }

    [Theory]
    [InlineData("C", SystemCommand.FocusChartTypeSelect)]
    [InlineData("L", SystemCommand.FocusPriceScaleSelect)]
    public void Alt_C_and_Alt_L_are_the_list_keys(string key, SystemCommand expected)
    {
        Assert.Equal(expected, new ShortcutManager(new TempPaths()).GetCommand(key, shift: false, ctrl: false, alt: true));
    }

    [Theory]
    [InlineData(SystemCommand.FocusChartTypeSelect, "chart-type-select")]
    [InlineData(SystemCommand.FocusPriceScaleSelect, "price-scale-select")]
    public void The_key_asks_for_its_list_without_the_chart_having_focus(SystemCommand command, string id)
    {
        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial);
        var bus = new SpyEventBus();
        var dispatcher = new CommandDispatcher(bus, Substitute.For<INavigationEngine>(), store,
            Substitute.For<IBarDetailService>(), new IndicatorCrossingEngine(store, bus));

        dispatcher.Dispatch(command);   // no ChartFocusEvent: the key works from the toolbar too

        Assert.Equal(id, Assert.Single(bus.Log.OfType<FocusToolbarControlEvent>()).ElementId);
        Assert.Empty(store.DispatchedActions);   // focus only: nothing about the chart changes
    }

    private static WorkspaceState Chart(ProviderDataShape shape = ProviderDataShape.Ohlcv) =>
        WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto", "kraken", "BTC/USD", "1h"),
            ActiveSeries = ImmutableList.Create(new ChartSeries(
                new SeriesConfig { Id = "candles", Name = "Candles" }, new SeriesDataBuffer { SeriesId = "candles" })),
            CurrentDataShape = shape,
        };

    [Theory]
    [InlineData("chart-type-select", "Chart type")]
    [InlineData("price-scale-select", "Price scale")]
    public void The_toolbar_focuses_the_list_it_names(string id, string name)
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => Chart());
        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();
        Assert.NotNull(cut.Find("#" + id));   // the id the key asks for is the list's

        h.EventBus.Publish(new FocusToolbarControlEvent(id, name));

        cut.WaitForAssertion(() => Assert.Contains(h.Ctx.JSInterop.Invocations["accessibleTrader.focusElement"],
            i => (string?)i.Arguments[0] == id));
    }

    [Fact]
    public void In_focus_mode_the_key_shows_the_toolbar_first()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => Chart());
        var toggles = new List<ToggleFocusModeEvent>();
        using var sub = h.EventBus.Subscribe<ToggleFocusModeEvent>(toggles.Add);
        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();
        h.EventBus.Publish(new FocusModeChangedEvent(true));

        h.EventBus.Publish(new FocusToolbarControlEvent("chart-type-select", "Chart type"));

        cut.WaitForAssertion(() => Assert.Contains(h.Ctx.JSInterop.Invocations["accessibleTrader.focusElement"],
            i => (string?)i.Arguments[0] == "chart-type-select"));
        Assert.Single(toggles);
    }

    [Fact]
    public void On_a_tab_with_no_lists_the_key_says_so()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => Chart(ProviderDataShape.SingleValueLine));
        var said = new List<FeedbackRequestEvent>();
        using var sub = h.EventBus.Subscribe<FeedbackRequestEvent>(said.Add);
        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();
        Assert.Empty(cut.FindAll("#chart-type-select"));

        h.EventBus.Publish(new FocusToolbarControlEvent("chart-type-select", "Chart type"));

        cut.WaitForAssertion(() => Assert.Contains(said, f => f.Message == "No Chart type list on this tab: it is for price charts."));
    }
}
