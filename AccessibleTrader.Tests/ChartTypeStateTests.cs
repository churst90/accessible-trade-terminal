using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// The chart type is state before it is ink: the reducer's invariants, the per-tab copy,
/// and the workspace file. Line and Area are raw-close chart types, so they can never be
/// combined with Heikin-Ashi; the four choices the user sees are Candles, Heikin Ashi,
/// Line and Area, and the tables below pin that one selection is all of it.
/// </summary>
public sealed class ChartTypeStateTests
{
    private static Ohlcv Bar(int i) =>
        new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i), 100 + i, 101 + i, 99 + i, 100.5 + i, 1000);

    private static WorkspaceState State(PriceChartStyle style = PriceChartStyle.Candles, bool heikinAshi = false) =>
        WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Spot", "Binance", "BTCUSDT", "1h"),
            Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, 8).Select(Bar)),
            ChartStyle = style,
            IsHeikinAshi = heikinAshi,
            ActiveTabIndex = 0,
            TabSnapshots = ImmutableList<TabSnapshot>.Empty,
        };

    private static WorkspaceState Reduce(WorkspaceState initial, WorkspaceAction action)
    {
        using var store = new WorkspaceStore(new SpyEventBus(), new MockViewportRangeCalculator(),
            new MockViewportNavigationService(), new MockVolumeStateService());
        store.Dispatch(new UpdateSettingsAction(_ => initial));
        store.Dispatch(action);
        return store.State;
    }

    [Fact]
    public void Choosing_line_clears_Heikin_Ashi()
    {
        var after = Reduce(State(heikinAshi: true), new SetChartStyleAction(PriceChartStyle.Line, false));

        Assert.Equal(PriceChartStyle.Line, after.ChartStyle);
        Assert.False(after.IsHeikinAshi);
    }

    [Fact]
    public void Choosing_area_clears_Heikin_Ashi_even_if_the_action_says_otherwise()
    {
        var after = Reduce(State(heikinAshi: true), new SetChartStyleAction(PriceChartStyle.Area, true));

        Assert.Equal(PriceChartStyle.Area, after.ChartStyle);   // the raw-close type wins
        Assert.False(after.IsHeikinAshi);                       // it can never carry HA
    }

    [Fact]
    public void Turning_Heikin_Ashi_on_from_area_gives_candles_and_HA()
    {
        var after = Reduce(State(PriceChartStyle.Area), new ToggleHeikinAshiAction());

        Assert.Equal(PriceChartStyle.Candles, after.ChartStyle);
        Assert.True(after.IsHeikinAshi);
    }

    [Fact]
    public void Turning_Heikin_Ashi_off_keeps_candles()
    {
        var after = Reduce(State(heikinAshi: true), new ToggleHeikinAshiAction());

        Assert.Equal(PriceChartStyle.Candles, after.ChartStyle);
        Assert.False(after.IsHeikinAshi);
    }

    [Fact]
    public void Choosing_Heikin_Ashi_in_the_select_is_candles_plus_HA()
    {
        var after = Reduce(State(PriceChartStyle.Line), new SetChartStyleAction(PriceChartStyle.Candles, true));

        Assert.Equal(PriceChartStyle.Candles, after.ChartStyle);
        Assert.True(after.IsHeikinAshi);
    }

    // ── Per tab ───────────────────────────────────────────────────────────

    [Fact]
    public void A_new_tab_starts_as_candles()
    {
        var after = Reduce(State(PriceChartStyle.Area), new AddTabAction());

        Assert.Equal(1, after.ActiveTabIndex);
        Assert.Equal(PriceChartStyle.Candles, after.ChartStyle);
    }

    [Fact]
    public void Each_tab_keeps_its_own_chart_type_across_switches()
    {
        var line = Reduce(State(PriceChartStyle.Line), new AddTabAction());      // tab 0 = Line, tab 1 = Candles
        var area = Reduce(line, new SetChartStyleAction(PriceChartStyle.Area, false));

        var backToZero = Reduce(area, new SwitchTabAction(0));
        Assert.Equal(PriceChartStyle.Line, backToZero.ChartStyle);

        var backToOne = Reduce(backToZero, new SwitchTabAction(1));
        Assert.Equal(PriceChartStyle.Area, backToOne.ChartStyle);
    }

    // ── Saved workspaces ──────────────────────────────────────────────────

    private sealed class Dir : IDisposable
    {
        public string Path { get; } = TestTemp.NewDir("att-charttype-");
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }

    private static WorkspaceLibraryService Library(string dir) =>
        new(NullLogger<WorkspaceLibraryService>.Instance, new TempWorkspacePaths())
        { LibraryDirectoryOverride = dir };

    [Fact]
    public void Line_and_Area_round_trip_through_the_workspace_file()
    {
        using var dir = new Dir();
        var active = State(PriceChartStyle.Area);
        var oldTab = State(PriceChartStyle.Line) with { ActiveTabIndex = 0 };

        var withSnapshot = Reduce(active with { TabSnapshots = ImmutableList<TabSnapshot>.Empty }, new AddTabAction());
        var source = withSnapshot with
        {
            ChartStyle = PriceChartStyle.Area,
            TabSnapshots = withSnapshot.TabSnapshots!.Select(s => s with { ChartStyle = PriceChartStyle.Line }).ToImmutableList(),
        };

        var lib = Library(dir.Path);
        var store = new MockWorkspaceStore();
        store.EmitState(source);
        lib.SaveWorkspaceProfile("types", store);

        var loaded = lib.LoadProfile("types")!;
        Assert.Equal(PriceChartStyle.Area, loaded.Tabs[1].ChartStyle);
        Assert.Equal(PriceChartStyle.Line, loaded.Tabs[0].ChartStyle);
    }

    [Fact]
    public void A_saved_file_without_the_field_loads_as_candles()
    {
        using var dir = new Dir();
        // The literal shape WorkspaceLibraryService wrote before ChartStyle existed.
        File.WriteAllText(Path.Combine(dir.Path, "old.json"), """
        {
          "Mode": 0,
          "SelectedMarketType": 0,
          "ActiveTabIndex": 0,
          "Tabs": [
            {
              "Market": "Spot",
              "Provider": "Bitstamp",
              "Symbol": "BTC/USD",
              "Timeframe": "1h",
              "ViewportStartIndex": 0,
              "ViewportLength": 100,
              "IsHeikinAshi": false,
              "IsLogScale": false,
              "Series": [],
              "PaneHeightRatios": {}
            }
          ]
        }
        """);

        var loaded = Library(dir.Path).LoadProfile("old");

        Assert.NotNull(loaded);
        Assert.Equal(PriceChartStyle.Candles, loaded!.Tabs[0].ChartStyle);
    }

    [Fact]
    public void Restore_sends_the_saved_chart_type_to_the_store()
    {
        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial);
        var initializer = new WorkspaceInitializer(
            Substitute.For<ISeriesManagementService>(), store, Substitute.For<IIndicatorService>());
        var config = new WorkspaceConfiguration
        {
            Tabs =
            {
                new TabConfiguration
                {
                    Provider = "Bitstamp", Symbol = "BTC/USD", Timeframe = "1h", Market = "Spot",
                    ChartStyle = PriceChartStyle.Area,
                }
            }
        };

        initializer.RestoreWorkspace(config);

        var action = Assert.Single(store.DispatchedActions.OfType<SetChartStyleAction>());
        Assert.Equal(PriceChartStyle.Area, action.Style);
        Assert.False(action.HeikinAshi);
    }
}
