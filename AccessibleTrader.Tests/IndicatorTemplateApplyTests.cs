using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;
using DynamicData;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// Applying an indicator template is a replacement, not a merge: the chart's own indicators go,
/// the template's arrive with fresh ids through the workspace restore path, pane heights follow,
/// and the whole edit is one Ctrl+Z away. Symbol, timeframe, drawings, candles and alerts stay.
/// </summary>
public sealed class IndicatorTemplateApplyTests
{
    private static readonly ChartIdentity Btc1h = new("Crypto", "Kraken", "BTCUSDT", "1h");
    private static readonly ChartIdentity Eth4h = new("Crypto", "Kraken", "ETHUSDT", "4h");

    private sealed class Fixture
    {
        public required WorkspaceStore Store { get; init; }
        public required SeriesManagementService Series { get; init; }
        public required WorkspaceInitializer Init { get; init; }
        public required IIndicatorService Indicators { get; init; }
        public required ChartUndoStack Undo { get; init; }
        public required EventBus Bus { get; init; }
    }

    /// <summary>
    /// The real store with its StateStream silenced. The pipeline under test then has ONLY the
    /// IndicatorUpdatedEvent path to notice a new series, which is the path this test is about —
    /// a production store emits a state change too, and that would hide a dropped event.
    /// </summary>
    private sealed class EventOnlyStore : IWorkspaceStore
    {
        private readonly WorkspaceStore _inner;

        public EventOnlyStore(WorkspaceStore inner) => _inner = inner;

        public WorkspaceState State => _inner.State;
        public IObservable<WorkspaceState> StateStream => System.Reactive.Linq.Observable.Never<WorkspaceState>();
        public void Dispatch(WorkspaceAction action) => _inner.Dispatch(action);
        public IObservable<IChangeSet<Ohlcv, DateTime>> DataStream => _inner.DataStream;
        public IObservable<IChangeSet<ChartSeries, string>> SeriesStream => _inner.SeriesStream;
    }

    private static Fixture Build()
    {
        var bus = new EventBus();
        var store = new WorkspaceStore(bus, new ViewportRangeCalculator(),
            new ViewportNavigationService(), new VolumeStateService());
        var styling = new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());
        var prefs = new MockIndicatorPreferencesService();
        var factory = new IndicatorModelFactory(styling, prefs);
        var library = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, new TempWorkspacePaths());
        var registry = new CustomIndicatorRegistry();
        var providers = new List<IIndicatorProvider>
        {
            new CoreIndicatorProvider(),
            new SkenderTrendProvider(),
            new SkenderBoundedOscillatorProvider(),
        };
        var indicators = new IndicatorService(providers, NullLogger<IndicatorService>.Instance);
        var engine = new IndicatorEngine(indicators, registry, providers);
        var series = new SeriesManagementService(store, bus, factory, styling, library, registry, engine, prefs);
        var undo = new ChartUndoStack();
        var init = new WorkspaceInitializer(series, store, indicators, undo: undo);
        return new Fixture
        {
            Store = store, Series = series, Init = init, Indicators = indicators, Undo = undo, Bus = bus,
        };
    }

    private static IndicatorMetadata Meta(IIndicatorService indicators, string code) =>
        indicators.GetAvailableIndicators()
            .First(m => string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase));

    private static SeriesConfig SavedEma(double period, string id = "saved-ema") => new()
    {
        Id = id,
        Name = "EMA",
        FriendlyName = $"EMA {period}",
        IndicatorCode = "Ema",
        Pane = "Main",
        Parameters = { ["lookbackPeriods"] = period },
    };

    private static SeriesConfig SavedRsi(double period, string id = "saved-rsi") => new()
    {
        Id = id,
        Name = "RSI",
        FriendlyName = $"RSI {period}",
        IndicatorCode = "Rsi",
        Pane = "Pane_Rsi",
        Parameters = { ["lookbackPeriods"] = period },
    };

    private static List<ChartSeries> IndicatorsOn(IWorkspaceStore store) =>
        store.State.ActiveSeries.Where(s => !SeriesManagementService.IsCoreCode(s.IndicatorCode) && !s.IsDrawing).ToList();

    /// <summary>A loaded chart with candles, a drawing and indicators A and B.</summary>
    private static (Fixture F, string A, string B, string Drawing) SeedChart()
    {
        var f = Build();
        f.Store.Dispatch(new SetIdentityAction(Btc1h));
        f.Series.RegisterSeriesFromMetadata(Meta(f.Indicators, "CANDLES"));

        f.Series.RegisterSeriesFromMetadata(Meta(f.Indicators, "Ema"),
            new Dictionary<string, object> { ["lookbackPeriods"] = 5 });
        f.Series.RegisterSeriesFromMetadata(Meta(f.Indicators, "Rsi"),
            new Dictionary<string, object> { ["lookbackPeriods"] = 7 });
        string a = f.Store.State.ActiveSeries.Single(s => s.IndicatorCode == "Ema").Id;
        string b = f.Store.State.ActiveSeries.Single(s => s.IndicatorCode == "Rsi").Id;

        string drawing = "drawing-1";
        f.Store.Dispatch(new AddSeriesAction(new ChartSeries(
            new SeriesConfig { Id = drawing, Name = "Trend (1)", IndicatorCode = "" },
            new SeriesDataBuffer { SeriesId = drawing })
        {
            Drawing = new DrawingData
            {
                Type = DrawingType.TrendLine,
                AnchorDate1 = new DateTime(2026, 1, 1),
                AnchorPrice1 = 100,
                AnchorDate2 = new DateTime(2026, 2, 1),
                AnchorPrice2 = 120,
            },
        }));

        f.Store.Dispatch(new SetPaneHeightRatiosAction(
            new Dictionary<string, float> { ["OldPane"] = 0.25f }.ToImmutableDictionary()));

        f.Store.Dispatch(new SelectSeriesAction(a));
        return (f, a, b, drawing);
    }

    private static IndicatorTemplate Template() => new()
    {
        Series = { SavedEma(20, "template-ema"), SavedRsi(14, "template-rsi") },
        PaneHeightRatios = { ["Pane_Rsi"] = 0.4f, ["Main"] = 0.6f },
        SavedFrom = "Kraken BTCUSDT 1h",
        SavedUtc = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public void Apply_replaces_the_indicators_and_keeps_symbol_timeframe_candles_and_drawings()
    {
        var (f, a, b, drawing) = SeedChart();
        var template = Template();

        f.Init.ApplyIndicatorTemplate(template, "Oscillators");

        Assert.Equal(Btc1h, f.Store.State.Identity);
        Assert.Contains(f.Store.State.ActiveSeries, s => s.Id == CoreSeriesIds.Candles);
        Assert.Contains(f.Store.State.ActiveSeries, s => s.Id == drawing && s.IsDrawing);
        Assert.DoesNotContain(f.Store.State.ActiveSeries, s => s.Id == a || s.Id == b);

        var kept = IndicatorsOn(f.Store);
        Assert.Equal(new[] { "Ema", "Rsi" }, kept.Select(s => s.IndicatorCode));
        Assert.DoesNotContain(kept, s => s.Id is "template-ema" or "template-rsi");
        Assert.Equal(20, kept.Single(s => s.IndicatorCode == "Ema").Parameters["lookbackPeriods"]);
        Assert.Equal(14, kept.Single(s => s.IndicatorCode == "Rsi").Parameters["lookbackPeriods"]);
        Assert.Equal(0.4f, f.Store.State.PaneHeightRatios!["Pane_Rsi"]);
        Assert.Equal("Apply indicator template Oscillators", f.Undo.NextUndoDescription);
    }

    [Fact]
    public void Applying_the_same_template_to_two_tabs_gives_different_ids()
    {
        var f = Build();
        f.Store.Dispatch(new SetIdentityAction(Btc1h));
        f.Series.RegisterSeriesFromMetadata(Meta(f.Indicators, "CANDLES"));
        f.Series.RegisterSeriesFromMetadata(Meta(f.Indicators, "CANDLES"));
        var template = Template();

        f.Init.ApplyIndicatorTemplate(template, "Oscillators");
        var first = IndicatorsOn(f.Store).Select(s => s.Id).ToList();

        f.Store.Dispatch(new AddTabAction());
        f.Store.Dispatch(new SetIdentityAction(Eth4h));
        f.Init.ApplyIndicatorTemplate(template, "Oscillators");
        var second = IndicatorsOn(f.Store).Select(s => s.Id).ToList();

        Assert.NotEmpty(first);
        Assert.NotEmpty(second);
        Assert.Empty(first.Intersect(second));
    }

    [Fact]
    public void Undo_puts_the_old_indicators_back_and_takes_the_template_ones_away()
    {
        var (f, a, b, drawing) = SeedChart();
        var before = IndicatorsOn(f.Store).Select(s => new
        {
            s.Id,
            Code = s.IndicatorCode,
            Period = s.Parameters["lookbackPeriods"],
        }).ToList();

        f.Init.ApplyIndicatorTemplate(Template(), "Oscillators");
        Assert.True(f.Undo.Undo());

        Assert.Equal(before.Select(x => (x.Id, x.Code, x.Period)),
                     IndicatorsOn(f.Store).Select(x => (x.Id, x.IndicatorCode, x.Parameters["lookbackPeriods"])));
        Assert.Contains(f.Store.State.ActiveSeries, s => s.Id == a);
        Assert.Contains(f.Store.State.ActiveSeries, s => s.Id == b);
        Assert.Contains(f.Store.State.ActiveSeries, s => s.Id == drawing);
        Assert.Equal(0.25f, f.Store.State.PaneHeightRatios!["OldPane"]);
        Assert.False(f.Store.State.PaneHeightRatios!.ContainsKey("Pane_Rsi"));
    }

    [Fact]
    public void When_the_focused_indicator_is_removed_focus_lands_on_candles()
    {
        var (f, a, _, _) = SeedChart();
        Assert.Equal(a, f.Store.State.FocusedSeriesId);

        f.Init.ApplyIndicatorTemplate(Template(), "Oscillators");

        Assert.Equal(CoreSeriesIds.Candles, f.Store.State.FocusedSeriesId);
    }

    [Fact]
    public void A_retired_indicator_in_a_template_is_skipped_and_named()
    {
        var (f, _, _, _) = SeedChart();
        var template = Template();
        template.Series.Add(new SeriesConfig
        {
            Id = "retired",
            Name = "Spider Lines",
            IndicatorCode = "SPIDER_LINES",
            Pane = "Main",
        });

        f.Init.ApplyIndicatorTemplate(template, "Old");

        Assert.DoesNotContain(f.Store.State.ActiveSeries, s => s.IndicatorCode == "SPIDER_LINES");
        Assert.Contains("Spider Lines was removed", f.Init.LastRestoreNotice);
    }

    [Fact]
    public void A_retired_only_template_still_removes_the_old_indicators()
    {
        var (f, a, b, _) = SeedChart();
        var template = new IndicatorTemplate
        {
            Series = { new SeriesConfig { Id = "retired", IndicatorCode = "LOUKAS_CYCLES", Pane = "Main" } },
        };

        f.Init.ApplyIndicatorTemplate(template, "Old");

        Assert.DoesNotContain(f.Store.State.ActiveSeries, s => s.Id == a || s.Id == b);
        Assert.Contains("Loukas Cycle Detection was removed", f.Init.LastRestoreNotice);
        Assert.Empty(IndicatorsOn(f.Store));
    }

    // ── Compute on the data already loaded, through the app's pipeline ──────

    private sealed class FakeDataManager : IDataManager
    {
        private TimeSeriesBuffer<Ohlcv> _bars;

        public FakeDataManager(IEnumerable<Ohlcv> bars) => _bars = new TimeSeriesBuffer<Ohlcv>(bars);

        public TimeSeriesBuffer<Ohlcv> Data => _bars;
        public ChartIdentity Identity { get; set; } = Btc1h;
        public Task RefreshDataAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task CatchUpFromSnapshotAsync(TimeSeriesBuffer<Ohlcv> snapshotData, CancellationToken ct = default) => Task.CompletedTask;
        public Task PrependOlderDataAsync() => Task.CompletedTask;
        public Task StartLiveUpdates() => Task.CompletedTask;
        public Task StopLiveUpdatesAsync() => Task.CompletedTask;
        public Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync()
            => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));
#pragma warning disable CS0067
        public event Action? DataUpdated;
        public event Action<string>? ErrorOccurred;
#pragma warning restore CS0067
    }

    /// <summary>Stands in for IndicatorOrchestrator's calculation and writes its result into the
    /// store, exactly as the real one does through UpdateSeriesDataAction.</summary>
    private sealed class ComputingOrchestrator : IIndicatorOrchestrator
    {
        private readonly IWorkspaceStore _store;

        public ComputingOrchestrator(IWorkspaceStore store) => _store = store;

        public Task RecalculateAllAsync(IReadOnlyList<Ohlcv> data, IEnumerable<ChartSeries> series,
            CancellationToken ct, List<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)>? historicalBooks = null)
        {
            foreach (var s in series.Where(s => !SeriesManagementService.IsCoreCode(s.IndicatorCode)))
            {
                string component = s.Components.FirstOrDefault()?.Name ?? "Value";
                var buffer = new SeriesDataBuffer { SeriesId = s.Id };
                buffer.ComponentData[component] = Enumerable.Repeat(50.0, data.Count).ToArray();
                _store.Dispatch(new UpdateSeriesDataAction(s.Id, buffer));
            }
            return Task.CompletedTask;
        }

        public Task RecalculateLastAsync(IReadOnlyList<Ohlcv> data, IEnumerable<ChartSeries> series,
            CancellationToken ct, List<OrderBookEntry>? lastBids = null, List<OrderBookEntry>? lastAsks = null)
            => RecalculateAllAsync(data, series, ct);
    }

    [Fact]
    public async Task Applied_indicators_compute_on_the_loaded_data_without_a_chart_reload()
    {
        var (f, _, _, _) = SeedChart();
        var bars = Enumerable.Range(0, 40)
            .Select(i => new Ohlcv(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i),
                100, 101, 99, 100, 10))
            .ToList();
        f.Store.Dispatch(new RequestInitializationStatusAction(InitializationStatus.Ready));
        var orchestrator = new ComputingOrchestrator(f.Store);
        using var pipeline = new DataOrchestrationService(
            new EventOnlyStore(f.Store),
            new FakeDataManager(bars),
            orchestrator,
            new OrderBookHistoryService(),
            f.Bus,
            NullLogger<DataOrchestrationService>.Instance);

        f.Init.ApplyIndicatorTemplate(Template(), "Oscillators");

        string rsiId = f.Store.State.ActiveSeries.Single(s => s.IndicatorCode == "Rsi").Id;
        bool computed = false;
        for (int i = 0; i < 100 && !computed; i++)
        {
            await Task.Delay(50);
            computed = f.Store.State.ActiveSeries.Any(s => s.Id == rsiId
                && s.Data.ComponentData.Count > 0
                && s.Data.ComponentData.Values.Any(v => v.Length == bars.Count));
        }

        Assert.True(computed, "the applied RSI never got values from the already-loaded bars");
    }
}
