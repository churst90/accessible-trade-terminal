// Razor components live in this namespace. An "unused using" sweep run before
// BlazorClient.Components has generated its component types will not see them and
// will offer to delete this line; it is used. See the same note in WebHost/Program.cs.
using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Core.Services.Workspace;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Tests.Blazor;
using Bunit;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// Save says what it saves, and Load says what it will close.
///
/// <para>
/// Cody, 2026-11-05: "does this save the currently focused chart, or all workspaces (tabs) at
/// once? ... whether to open the workspace in a new tab or load it in the current tab replacing
/// what was already loaded". Save always wrote every tab and Load always closed every tab, and
/// neither dialog said so. Save now offers "All tabs" (the default) or "This tab only"; Load
/// offers "Replace my tabs" or "Open as new tabs", and each saved workspace in its list says
/// what it holds. The app does not track whether a tab changed since it was saved, so Replace
/// warns whenever a tab with a chart would close, and pressing Load again goes ahead.
/// </para>
/// </summary>
public class WorkspaceSaveLoadChoiceTests
{
    private static readonly ChartIdentity KrakenBtc = new("Crypto", "Kraken", "BTCUSDT", "1h");
    private static readonly ChartIdentity BitstampEth = new("Crypto", "Bitstamp", "ETHUSDT", "1d");

    private static TabConfiguration Tab(ChartIdentity id) =>
        new() { Market = id.Market, Provider = id.Provider, Symbol = id.Symbol, Timeframe = id.Timeframe };

    private static SavedActiveStrategy On(string spec, ChartIdentity id) =>
        new() { SpecId = spec, Symbol = id.Symbol, ChartProvider = id.Provider, ChartSymbol = id.Symbol,
                ChartTimeframe = id.Timeframe, ChartMarket = id.Market };

    private static WorkspaceConfiguration TwoTabs(int active = 1) => new()
    {
        ActiveTabIndex = active,
        Tabs = { Tab(KrakenBtc), Tab(BitstampEth) },
        ActiveStrategies = new() { On("a", KrakenBtc), On("b", BitstampEth), new() { SpecId = "c", Symbol = "" } },
    };

    private static WorkspaceStore RealStore(params ChartIdentity[] tabs)
    {
        var store = new WorkspaceStore(new EventBus(), new ViewportRangeCalculator(), new ViewportNavigationService(), new VolumeStateService());
        for (int i = 0; i < tabs.Length; i++)
        {
            if (i > 0) store.Dispatch(new AddTabAction());
            store.Dispatch(new SetIdentityAction(tabs[i]));
        }
        return store;
    }

    private static WorkspaceInitializer Initializer(IWorkspaceStore store, IStrategyEngine? engine = null,
                                                    IStrategyLibrary? library = null)
    {
        var indicators = Substitute.For<IIndicatorService>();
        indicators.GetAvailableIndicators().Returns(new List<IndicatorMetadata>());
        return new WorkspaceInitializer(Substitute.For<ISeriesManagementService>(), store, indicators,
            engine, engine == null ? null : Substitute.For<IConfigurableStrategyFactory>(), library);
    }

    private static List<ChartIdentity> TabsInOrder(IWorkspaceStore store)
    {
        var st = store.State;
        var all = new List<(int, ChartIdentity)> { (st.ActiveTabIndex, st.Identity) };
        all.AddRange((st.TabSnapshots ?? System.Collections.Immutable.ImmutableList<TabSnapshot>.Empty).Select(s => (s.TabIndex, s.Identity)));
        return all.OrderBy(t => t.Item1).Select(t => t.Item2).ToList();
    }

    // ── This tab only ────────────────────────────────────────────────────────

    [Fact]
    public void This_tab_only_keeps_the_tab_in_front_and_the_strategies_on_it()
    {
        var one = WorkspaceLibraryService.OnlyActiveTab(TwoTabs(active: 1));

        var tab = Assert.Single(one.Tabs);
        Assert.Equal("ETHUSDT", tab.Symbol);
        Assert.Equal(0, one.ActiveTabIndex);
        // "a" runs on the Kraken tab, which was not saved; "c" belongs to no tab.
        Assert.Equal(new[] { "b", "c" }, one.ActiveStrategies.Select(s => s.SpecId));
    }

    // ── What a workspace holds ───────────────────────────────────────────────

    [Fact]
    public void The_load_list_says_what_each_workspace_holds()
    {
        Assert.Equal("2 tabs: Kraken BTCUSDT 1h, Bitstamp ETHUSDT 1d, 3 strategies",
                     WorkspaceLibraryService.Describe(TwoTabs()));
        var six = new WorkspaceConfiguration();
        for (int i = 0; i < 6; i++) six.Tabs.Add(Tab(KrakenBtc with { Symbol = "S" + i }));
        six.Tabs[5].Symbol = "";
        six.ActiveStrategies = new() { On("a", KrakenBtc) };
        Assert.Equal("6 tabs: Kraken S0 1h, Kraken S1 1h, Kraken S2 1h, Kraken S3 1h, and 2 more, 1 strategy",
                     WorkspaceLibraryService.Describe(six));
        Assert.Equal("1 tab: an empty chart", WorkspaceLibraryService.Describe(
            new WorkspaceConfiguration { Tabs = { new TabConfiguration { Symbol = "" } } }));
        Assert.Equal("could not be read", WorkspaceLibraryService.Describe(null));
    }

    // ── Open as new tabs ─────────────────────────────────────────────────────

    [Fact]
    public void Open_as_new_tabs_keeps_my_tabs_and_adds_the_saved_ones_after_them()
    {
        var mine = new ChartIdentity("Crypto", "Hyperliquid", "SOLUSDT", "4h");
        var store = RealStore(mine);

        Initializer(store).AppendWorkspace(TwoTabs(active: 1));

        Assert.Equal(new[] { mine, KrakenBtc, BitstampEth }, TabsInOrder(store));
        // The saved workspace's own front tab is in front.
        Assert.Equal(BitstampEth, store.State.Identity);
    }

    [Fact]
    public void Open_as_new_tabs_on_a_fresh_start_fills_the_empty_tab()
    {
        var store = RealStore();
        Assert.Equal(1, store.State.TabCount);

        Initializer(store).AppendWorkspace(TwoTabs(active: 0));

        Assert.Equal(new[] { KrakenBtc, BitstampEth }, TabsInOrder(store));
        Assert.Equal(KrakenBtc, store.State.Identity);
    }

    [Fact]
    public void Open_as_new_tabs_keeps_running_strategies_and_does_not_start_one_twice()
    {
        var store = RealStore(KrakenBtc);
        var spec = (string id) => new StrategySpec(id, id, "", OrderSide.Buy,
            new ConditionGroup("root", LogicOperator.And, new List<ConditionNode>()),
            new RiskPlan(Stop: new StopSource(StopSourceKind.PercentOfPrice, PercentValue: 1.0),
                TpLadder: Array.Empty<TpLadderRung>(),
                Sizing: new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005),
                Entry: new EntryTrigger(EntryTriggerKind.Immediate)),
            StrategyExecutionMode.Suggestion);
        var library = Substitute.For<IStrategyLibrary>();
        library.All.Returns(new List<StrategySpec> { spec("a"), spec("b"), spec("c") });
        var engine = Substitute.For<IStrategyEngine>();
        // "a" is already running on the Kraken chart.
        engine.ActiveStrategies.Returns(new List<ActiveStrategy>
        {
            new("inst-a", Substitute.For<ITradingStrategy>(), new Dictionary<string, object>(),
                StrategyExecutionMode.Suggestion, false, "BTCUSDT", "a", KrakenBtc),
        });

        Initializer(store, engine, library).AppendWorkspace(TwoTabs());

        engine.DidNotReceive().RemoveStrategy(Arg.Any<string>());
        var started = engine.ReceivedCalls().Where(c => c.GetMethodInfo().Name == "AddStrategy")
                            .Select(c => (string?)c.GetArguments()[3]).ToList();
        Assert.Equal(new[] { "b", "c" }, started);
    }

    // ── The dialogs ──────────────────────────────────────────────────────────

    private static IRenderedComponent<SaveWorkspaceModal> OpenSave(BlazorTestHarness h, WorkspaceState state)
    {
        h.WorkspaceStore.State.Returns(_ => state);
        h.WorkspaceLibrary.GetAvailableProfiles().Returns(new List<string>());
        h.WorkspaceLibrary.CaptureWorkspace(Arg.Any<IWorkspaceStore>()).Returns(_ => TwoTabs(active: 1));
        return h.OpenModal<SaveWorkspaceModal>(bus => bus.Publish(new OpenSaveWorkspaceEvent()));
    }

    private static WorkspaceState TwoTabState(BlazorTestHarness h) => RealStore(KrakenBtc, BitstampEth).State;

    [Fact]
    public void Save_saves_every_tab_unless_this_tab_only_is_chosen()
    {
        using var h = new BlazorTestHarness();
        var cut = OpenSave(h, TwoTabState(h));
        Assert.Contains("All 2 tabs", cut.Find("fieldset").TextContent);
        Assert.Contains("This tab only (Bitstamp ETHUSDT 1d)", cut.Find("fieldset").TextContent);
        Assert.True(cut.Find("#save-scope-all").HasAttribute("checked"));

        cut.Find("#workspace-name-input").Change("Both");
        cut.Find("#workspace-name-input").KeyDown("Enter");
        h.WorkspaceLibrary.Received(1).SaveWorkspaceProfile("Both", h.WorkspaceStore);

        cut = OpenSave(h, TwoTabState(h));
        cut.Find("#save-scope-this").Change(true);
        cut.Find("#workspace-name-input").Change("Eth");
        cut.Find("#workspace-name-input").KeyDown("Enter");

        h.WorkspaceLibrary.Received(1).SaveProfile("Eth", Arg.Is<WorkspaceConfiguration>(c =>
            c.Tabs.Count == 1 && c.Tabs[0].Symbol == "ETHUSDT"));
        h.WorkspaceLibrary.Received(1).SaveWorkspaceProfile(Arg.Any<string>(), Arg.Any<IWorkspaceStore>());
    }

    [Fact]
    public void With_one_tab_there_is_nothing_to_choose()
    {
        using var h = new BlazorTestHarness();
        var cut = OpenSave(h, RealStore(KrakenBtc).State);
        Assert.Empty(cut.FindAll("#save-scope-this"));
    }

    private static (IRenderedComponent<LoadWorkspaceModal> Cut, IWorkspaceInitializer Init, List<string> Said)
        OpenLoad(BlazorTestHarness h, IWorkspaceStore store)
    {
        h.With(store);
        h.WorkspaceLibrary.GetAvailableProfiles().Returns(new List<string> { "Camel", "Scalps" });
        h.WorkspaceLibrary.LoadProfile("Camel").Returns(_ => TwoTabs());
        h.WorkspaceLibrary.LoadProfile("Scalps").Returns(_ => new WorkspaceConfiguration { Tabs = { Tab(KrakenBtc) } });
        var said = new List<string>();
        h.EventBus.Subscribe<AnnouncementEvent>(e => said.Add(e.Message));
        var cut = h.OpenModal<LoadWorkspaceModal>(bus => bus.Publish(new OpenLoadWorkspaceEvent()));
        return (cut, h.Ctx.Services.GetService(typeof(IWorkspaceInitializer)) as IWorkspaceInitializer ?? throw new InvalidOperationException(), said);
    }

    private static void PressLoad(IRenderedComponent<LoadWorkspaceModal> cut) =>
        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith("Load")).Click();

    [Fact]
    public void The_load_list_describes_each_workspace()
    {
        using var h = new BlazorTestHarness();
        var (cut, _, _) = OpenLoad(h, RealStore());
        var options = cut.FindAll("li[role=option]");
        Assert.Contains("Camel, 2 tabs: Kraken BTCUSDT 1h, Bitstamp ETHUSDT 1d, 3 strategies", options[0].TextContent);
        Assert.Contains("Scalps, 1 tab: Kraken BTCUSDT 1h", options[1].TextContent);
    }

    [Fact]
    public void Replace_names_the_tabs_it_would_close_and_a_second_Load_replaces_them()
    {
        using var h = new BlazorTestHarness();
        var store = RealStore(KrakenBtc, BitstampEth);
        var (cut, init, said) = OpenLoad(h, store);
        Assert.True(cut.Find("#load-mode-replace").HasAttribute("checked"));

        PressLoad(cut);

        init.DidNotReceive().RestoreWorkspace(Arg.Any<WorkspaceConfiguration>());
        string warning = "Replacing closes your 2 tabs: Kraken BTCUSDT 1h, Bitstamp ETHUSDT 1d. Anything on them "
                       + "you have not saved as a workspace is lost. Press Load again to replace them, or choose Open as new tabs.";
        Assert.Equal(warning, said.Last());
        Assert.Contains(warning, cut.Find("p[role=status]").TextContent);
        Assert.Equal(2, store.State.TabCount);

        PressLoad(cut);

        init.Received(1).RestoreWorkspace(Arg.Any<WorkspaceConfiguration>());
        Assert.Equal("Workspace loaded: Camel", said.Last());
    }

    [Fact]
    public void Moving_off_the_workspace_asks_again()
    {
        // The second Load confirms only straight after the warning: a user who moved to another
        // workspace and back has not answered the question for this one.
        using var h = new BlazorTestHarness();
        var (cut, init, said) = OpenLoad(h, RealStore(KrakenBtc));
        PressLoad(cut);
        Assert.StartsWith("Replacing closes your tab, Kraken BTCUSDT 1h.", said.Last());
        int warnings = said.Count;

        cut.FindAll("li[role=option]")[1].Click();
        cut.FindAll("li[role=option]")[0].Click();
        PressLoad(cut);

        init.DidNotReceive().RestoreWorkspace(Arg.Any<WorkspaceConfiguration>());
        Assert.Equal(warnings + 1, said.Count);
        Assert.StartsWith("Replacing closes", said.Last());

        // And from the warning, switching to Open as new tabs and back asks again too.
        cut.Find("#load-mode-append").Change(true);
        cut.Find("#load-mode-replace").Change(true);
        PressLoad(cut);
        init.DidNotReceive().RestoreWorkspace(Arg.Any<WorkspaceConfiguration>());
    }

    [Fact]
    public void Nothing_to_close_means_nothing_to_ask()
    {
        using var h = new BlazorTestHarness();
        var (cut, init, said) = OpenLoad(h, RealStore());
        PressLoad(cut);
        init.Received(1).RestoreWorkspace(Arg.Any<WorkspaceConfiguration>());
        Assert.Equal("Workspace loaded: Camel", said.Last());
    }

    [Fact]
    public void Open_as_new_tabs_closes_nothing_and_does_not_ask()
    {
        using var h = new BlazorTestHarness();
        var store = RealStore(KrakenBtc, BitstampEth);
        var (cut, init, said) = OpenLoad(h, store);

        cut.Find("#load-mode-append").Change(true);
        PressLoad(cut);

        init.Received(1).AppendWorkspace(Arg.Any<WorkspaceConfiguration>());
        init.DidNotReceive().RestoreWorkspace(Arg.Any<WorkspaceConfiguration>());
        Assert.Equal(2, store.State.TabCount);
        Assert.Equal("Workspace opened as new tabs: Camel, 2 tabs: Kraken BTCUSDT 1h, Bitstamp ETHUSDT 1d, 3 strategies",
                     said.Last());
    }
}
