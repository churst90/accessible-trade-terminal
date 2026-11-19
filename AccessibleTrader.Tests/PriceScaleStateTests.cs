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
/// The price scale is state before it is ink: the reducer's one-choice invariants, the per-tab
/// copy, the workspace file, and the range refit the store owes a COMPARE overlay when the mode
/// changes. Also the base itself — the first visible close as drawn — because every other layer
/// reads it through this one helper.
/// </summary>
public sealed class PriceScaleStateTests
{
    private static Ohlcv Bar(int i, double close) =>
        new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i),
            close, close + 4, close - 1, close, 1000);

    private static WorkspaceState State(PriceScaleMode mode = PriceScaleMode.Regular, bool log = false) =>
        WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Spot", "Binance", "BTCUSDT", "1h"),
            Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, 8).Select(i => Bar(i, 100 + i))),
            ScaleMode = mode,
            IsLogScale = log,
            ViewportStartIndex = 0,
            ViewportLength = 8,
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

    // ── Reducer: one choice, never an impossible pair ─────────────────────

    [Fact]
    public void Choosing_percent_clears_log()
    {
        var after = Reduce(State(log: true), new SetPriceScaleAction(PriceScaleMode.Percent, false));

        Assert.Equal(PriceScaleMode.Percent, after.ScaleMode);
        Assert.False(after.IsLogScale);
    }

    [Fact]
    public void Choosing_percent_cannot_carry_log_even_if_the_action_says_so()
    {
        var after = Reduce(State(log: true), new SetPriceScaleAction(PriceScaleMode.Percent, true));

        Assert.Equal(PriceScaleMode.Percent, after.ScaleMode);
        Assert.False(after.IsLogScale);   // the relabel wins; percent is never logarithmic
    }

    [Fact]
    public void Turning_log_on_from_indexed_gives_regular_plus_log()
    {
        var after = Reduce(State(PriceScaleMode.IndexedTo100), new ToggleLogScaleAction());

        Assert.Equal(PriceScaleMode.Regular, after.ScaleMode);
        Assert.True(after.IsLogScale);
    }

    [Fact]
    public void Turning_log_off_keeps_regular()
    {
        var after = Reduce(State(log: true), new ToggleLogScaleAction());

        Assert.Equal(PriceScaleMode.Regular, after.ScaleMode);
        Assert.False(after.IsLogScale);
    }

    [Fact]
    public void Choosing_logarithmic_is_regular_plus_log()
    {
        var after = Reduce(State(PriceScaleMode.Percent), new SetPriceScaleAction(PriceScaleMode.Regular, true));

        Assert.Equal(PriceScaleMode.Regular, after.ScaleMode);
        Assert.True(after.IsLogScale);
    }

    // ── Per tab ───────────────────────────────────────────────────────────

    [Fact]
    public void A_new_tab_starts_regular()
    {
        var after = Reduce(State(PriceScaleMode.Percent), new AddTabAction());

        Assert.Equal(1, after.ActiveTabIndex);
        Assert.Equal(PriceScaleMode.Regular, after.ScaleMode);
    }

    [Fact]
    public void Each_tab_keeps_its_own_scale_across_switches()
    {
        var percent = Reduce(State(PriceScaleMode.Percent), new AddTabAction());
        var indexed = Reduce(percent, new SetPriceScaleAction(PriceScaleMode.IndexedTo100, false));

        var backToZero = Reduce(indexed, new SwitchTabAction(0));
        Assert.Equal(PriceScaleMode.Percent, backToZero.ScaleMode);

        var backToOne = Reduce(backToZero, new SwitchTabAction(1));
        Assert.Equal(PriceScaleMode.IndexedTo100, backToOne.ScaleMode);
    }

    // ── Saved workspaces ──────────────────────────────────────────────────

    private sealed class Dir : IDisposable
    {
        public string Path { get; } = TestTemp.NewDir("att-pricescale-");
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }

    private static WorkspaceLibraryService Library(string dir) =>
        new(NullLogger<WorkspaceLibraryService>.Instance, new TempWorkspacePaths())
        { LibraryDirectoryOverride = dir };

    [Fact]
    public void Percent_and_indexed_round_trip_through_the_workspace_file()
    {
        using var dir = new Dir();
        var active = State(PriceScaleMode.Percent);
        var withSnapshot = Reduce(active with { TabSnapshots = ImmutableList<TabSnapshot>.Empty }, new AddTabAction());
        var source = withSnapshot with
        {
            ScaleMode = PriceScaleMode.Percent,
            TabSnapshots = withSnapshot.TabSnapshots!
                .Select(s => s with { ScaleMode = PriceScaleMode.IndexedTo100 }).ToImmutableList(),
        };

        var lib = Library(dir.Path);
        var store = new MockWorkspaceStore();
        store.EmitState(source);
        lib.SaveWorkspaceProfile("scales", store);

        var loaded = lib.LoadProfile("scales")!;
        Assert.Equal(PriceScaleMode.Percent, loaded.Tabs[1].ScaleMode);
        Assert.Equal(PriceScaleMode.IndexedTo100, loaded.Tabs[0].ScaleMode);
    }

    [Fact]
    public void A_saved_file_without_the_field_loads_as_regular()
    {
        using var dir = new Dir();
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
              "IsLogScale": true,
              "Series": [],
              "PaneHeightRatios": {}
            }
          ]
        }
        """);

        var loaded = Library(dir.Path).LoadProfile("old");

        Assert.NotNull(loaded);
        Assert.Equal(PriceScaleMode.Regular, loaded!.Tabs[0].ScaleMode);
        Assert.True(loaded.Tabs[0].IsLogScale);
    }

    [Fact]
    public void Restore_sends_the_saved_scale_and_log_in_one_action()
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
                    ScaleMode = PriceScaleMode.Percent,
                }
            }
        };

        initializer.RestoreWorkspace(config);

        var action = Assert.Single(store.DispatchedActions.OfType<SetPriceScaleAction>());
        Assert.Equal(PriceScaleMode.Percent, action.Mode);
        Assert.False(action.Log);
    }

    [Fact]
    public void Restore_sends_logarithmic_as_regular_plus_log()
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
                    ScaleMode = PriceScaleMode.Regular, IsLogScale = true,
                }
            }
        };

        initializer.RestoreWorkspace(config);

        var action = Assert.Single(store.DispatchedActions.OfType<SetPriceScaleAction>());
        Assert.Equal(PriceScaleMode.Regular, action.Mode);
        Assert.True(action.Log);
    }

    // ── The base ──────────────────────────────────────────────────────────

    [Fact]
    public void The_base_is_the_first_VISIBLE_close()
    {
        var bars = Enumerable.Range(0, 10).Select(i => Bar(i, 100 + i)).ToList();

        Assert.Equal(103, ChartMath.ScaleBase(bars, 3, false));
    }

    [Fact]
    public void A_start_past_the_data_is_clamped_to_the_last_bar()
    {
        var bars = Enumerable.Range(0, 5).Select(i => Bar(i, 100 + i)).ToList();

        Assert.Equal(104, ChartMath.ScaleBase(bars, 99, false));
    }

    [Fact]
    public void The_base_on_a_Heikin_Ashi_chart_is_the_HA_close_as_drawn()
    {
        var bars = Enumerable.Range(0, 5).Select(i => Bar(i, 100 + i * 10)).ToList();
        int at = 2;
        var ha = ChartMath.CalculateHeikinAshi(bars);

        Assert.Equal(ha[at].Close, ChartMath.ScaleBase(bars, at, true));
        Assert.NotEqual(bars[at].Close, ChartMath.ScaleBase(bars, at, true));
    }

    [Fact]
    public void The_state_helper_uses_the_state_viewport_and_chart_style()
    {
        var state = State() with { ViewportStartIndex = 2 };

        Assert.Equal(102, ChartMath.ScaleBase(state));
        Assert.Equal(102, ChartMath.ScaleBase(state with { ChartStyle = PriceChartStyle.Line }));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_zero_negative_or_missing_base_cannot_carry_the_mode(double baseValue)
    {
        Assert.False(ChartMath.BaseUsable(baseValue));
        Assert.Equal(PriceScaleMode.Regular,
            ChartMath.EffectiveScaleMode(PriceScaleMode.Percent, baseValue, false));
        Assert.Equal(PriceScaleMode.Regular,
            ChartMath.EffectiveScaleMode(PriceScaleMode.IndexedTo100, baseValue, false));
    }

    [Fact]
    public void The_log_flag_also_forces_regular_drawing()
        => Assert.Equal(PriceScaleMode.Regular,
            ChartMath.EffectiveScaleMode(PriceScaleMode.Percent, 100, isLogScale: true));

    // ── The store refit, through the store ────────────────────────────────

    private static ChartSeries CompareSeries(params double[] values)
    {
        var config = new SeriesConfig
        {
            Id = "compare-line", Name = "Compare symbol", Pane = "Main",
            IndicatorCode = "COMPARE",
        };
        config.Components.Add(new ComponentConfig
        {
            Name = "Compare", DisplayName = "Compare",
            DisplayType = ComponentDisplayType.Line, IsVisible = true, IsDrawn = true,
        });
        var buffer = new SeriesDataBuffer { SeriesId = config.Id };
        buffer.ComponentData["Compare"] = values;
        return new ChartSeries(config, buffer);
    }

    private static WorkspaceState CompareState()
    {
        var bars = Enumerable.Range(0, 10).Select(i => Bar(i, 100 + i)).ToList();
        return WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(CompareSeries(300, 320, 340, 360, 360, 360, 360, 360, 360, 360)),
            ViewportStartIndex = 0,
            ViewportLength = 10,
        };
    }

    [Fact]
    public void Choosing_percent_with_a_compare_overlay_refits_the_range_through_the_store()
    {
        var initial = CompareState();
        using var store = new WorkspaceStore(new SpyEventBus(), new ViewportRangeCalculator(),
            new MockViewportNavigationService(), new MockVolumeStateService());
        store.Dispatch(new UpdateSettingsAction(_ => initial));

        double before = store.State.ViewportRange.Max;
        Assert.True(before < 118, $"the raw comparison should be far off the candles and guarded out, but max was {before}");

        store.Dispatch(new SetPriceScaleAction(PriceScaleMode.Percent, false));

        // The overlay is rebased to 100 at the left edge and rises to 120 by the last bar, so
        // the pane must hold at least the chart's own 120 price now.
        Assert.True(store.State.ViewportRange.Max >= 120,
            $"the rebased comparison was clipped: max {store.State.ViewportRange.Max}");
    }

    [Fact]
    public void Choosing_percent_without_a_compare_overlay_does_not_move_the_range()
    {
        var initial = CompareState() with { ActiveSeries = ImmutableList<ChartSeries>.Empty };
        using var store = new WorkspaceStore(new SpyEventBus(), new ViewportRangeCalculator(),
            new MockViewportNavigationService(), new MockVolumeStateService());
        store.Dispatch(new UpdateSettingsAction(_ => initial));
        var before = store.State.ViewportRange;

        store.Dispatch(new SetPriceScaleAction(PriceScaleMode.Percent, false));

        Assert.Equal(before, store.State.ViewportRange);
    }
}
