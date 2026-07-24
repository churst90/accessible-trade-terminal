using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using NSubstitute;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Regression fence for the browser-title price bug: after a session resume or a tab
/// switch, the tab must reach InitStatus == Ready WITHOUT a manual "Load Chart" — because
/// the title only shows a price when InitStatus == Ready, and previously only LoadChartAsync
/// set it. The resume path and the tab-switch catch-up loaded data (DataStatus=Ready) but
/// left InitStatus at Booting, so the title stayed blank until the user clicked Load Chart.
/// </summary>
public sealed class MarketOrchestratorTitleReadinessTests
{
    private static (MarketOrchestrator orch, WorkspaceStore store, EventBus bus) Make()
    {
        // Store and orchestrator share ONE event bus, as they do per-circuit in production —
        // so the store's TabSwitchedEvent reaches the orchestrator's subscription.
        var bus = new EventBus();
        var store = new WorkspaceStore(
            bus, new ViewportRangeCalculator(), new ViewportNavigationService(), new VolumeStateService());

        var dataManager = Substitute.For<IDataManager>();
        // CatchUpFromSnapshotAsync / RefreshDataAsync return completed tasks by default.

        var orch = new MarketOrchestrator(
            Substitute.For<IDataService>(), dataManager, store,
            Substitute.For<IWorkspaceInitializer>(), bus, new DemoPolicy(isDemo: false));
        return (orch, store, bus);
    }

    private static ChartIdentity Sym() =>
        new() { Provider = "P", Symbol = "SYM", Timeframe = "1h", Market = "Crypto" };

    [Fact]
    public async Task LoadRestoredActiveTab_marks_InitStatus_Ready()
    {
        var (orch, store, _) = Make();
        store.Dispatch(new SetIdentityAction(Sym()));
        Assert.NotEqual(InitializationStatus.Ready, store.State.InitStatus); // Booting after restore

        await orch.LoadRestoredActiveTabAsync();

        Assert.Equal(InitializationStatus.Ready, store.State.InitStatus); // title gate now open
    }

    [Fact]
    public async Task LoadRestoredActiveTab_with_no_symbol_is_a_noop()
    {
        var (orch, store, _) = Make(); // no identity set
        await orch.LoadRestoredActiveTabAsync();
        Assert.NotEqual(InitializationStatus.Ready, store.State.InitStatus);
    }

    [Fact]
    public async Task Tab_switch_marks_InitStatus_Ready_after_catchup()
    {
        var (_, store, bus) = Make();
        store.Dispatch(new SetIdentityAction(Sym())); // the switched-to tab has a symbol

        // The store publishes TabSwitchedEvent on SwitchTab; the orchestrator's handler runs
        // on a background task, so settle until it drives InitStatus to Ready.
        bus.Publish(new TabSwitchedEvent(store.State.ActiveTabIndex, "SYM"));
        for (int i = 0; i < 200 && store.State.InitStatus != InitializationStatus.Ready; i++)
            await Task.Delay(10);

        Assert.Equal(InitializationStatus.Ready, store.State.InitStatus);
    }
}
