using System.Collections.Immutable;
using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Drawing;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// Text labels and horizontal lines on a non-price pane: placed at a value in that pane's own
/// units, rendered there, spoken, nudged there, saved/reloaded, reachable by pane navigation,
/// and safe when the indicator that owned the pane is removed.
/// </summary>
public sealed class DrawingPaneTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static List<Ohlcv> Bars(int n = 30) =>
        Enumerable.Range(0, n)
            .Select(i => new Ohlcv(T0.AddHours(i), 100 + i, 101 + i, 99 + i, 100.5 + i, 1000 + i))
            .ToList();

    private static IDrawingService RealDrawingService()
    {
        var calc = typeof(IDrawingCalculator);
        return new DrawingService(typeof(DrawingService).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && calc.IsAssignableFrom(t))
            .Select(t => (IDrawingCalculator)Activator.CreateInstance(t)!)
            .ToList());
    }

    private sealed class Harness
    {
        public WorkspaceStore Store { get; }
        public SpyEventBus Bus { get; } = new();
        public DrawingInteractionManager Manager { get; }
        public ChartUndoStack Undo { get; } = new();
        public List<Ohlcv> Bars { get; } = DrawingPaneTests.Bars();

        public Harness()
        {
            Store = new WorkspaceStore(Bus, new ViewportRangeCalculator(),
                new ViewportNavigationService(), new VolumeStateService());
            Store.Dispatch(new UpdateDataAction(new TimeSeriesBuffer<Ohlcv>(Bars), IsInitialLoad: true));
            Manager = new DrawingInteractionManager(
                Bus, RealDrawingService(), Store,
                new IndicatorModelFactory(new MockStylingService(), new MockIndicatorPreferencesService()),
                new BlazorInputService(), undo: Undo);
        }

        public ChartSeries AddSeries(string id, string name, string pane, string code, params double[] values)
        {
            var config = new SeriesConfig
            {
                Id = id, Name = name, FriendlyName = name, Pane = pane, IndicatorCode = code,
            };
            config.Components.Add(new ComponentConfig { Name = "line" });
            var buffer = new SeriesDataBuffer { SeriesId = id };
            buffer.ComponentData["line"] = values;
            var series = new ChartSeries(config, buffer);
            Store.Dispatch(new AddSeriesAction(series));
            return series;
        }

        public void Focus(string id) => Store.Dispatch(new SelectSeriesAction(id));

        public ChartSeries Drawing() => Store.State.ActiveSeries.First(s => s.IsDrawing);
    }

    [Fact]
    public void Keyboard_label_with_an_indicator_focused_lands_in_that_pane_at_that_value()
    {
        var h = new Harness();
        h.AddSeries("candles", "Candles", "Main", "CANDLES");
        var rsi = h.AddSeries("rsi-1", "RSI 14", "Pane_Rsi", "RSI",
            Enumerable.Repeat(50.0, 30).ToArray());
        rsi.Data.ComponentData["line"][7] = 57.25;
        h.Focus("rsi-1");
        h.Store.Dispatch(new SetCursorAction(7));

        PromptForLabelTextEvent? prompt = null;
        h.Bus.Subscribe<PromptForLabelTextEvent>(e => prompt = e);
        h.Manager.HandleAddDrawing("TextLabel", h.Bars);

        var label = h.Drawing();
        Assert.Equal("Pane_Rsi", label.Pane);
        Assert.Equal(57.25, label.Drawing!.AnchorPrice1);
        Assert.Equal(h.Bars[7].Date, label.Drawing.AnchorDate1);
        Assert.NotNull(prompt);
        Assert.Equal(label.Id, prompt!.SeriesId);
        Assert.Contains(h.Bus.Log.OfType<AnnouncementEvent>(),
            e => e.Message == "Text label pinned at 57.25");
    }

    [Fact]
    public void Keyboard_horizontal_line_with_an_indicator_focused_lands_in_that_pane_at_that_value()
    {
        var h = new Harness();
        h.AddSeries("candles", "Candles", "Main", "CANDLES");
        var rsi = h.AddSeries("rsi-1", "RSI 14", "Pane_Rsi", "RSI",
            Enumerable.Repeat(50.0, 30).ToArray());
        rsi.Data.ComponentData["line"][7] = 63;
        h.Focus("rsi-1");
        h.Store.Dispatch(new SetCursorAction(7));

        h.Manager.HandleAddDrawing("Horizontal", h.Bars);

        var line = h.Drawing();
        Assert.Equal("Pane_Rsi", line.Pane);
        Assert.Equal(63, line.Drawing!.AnchorPrice1);
        Assert.Contains(h.Bus.Log.OfType<AnnouncementEvent>(),
            e => e.Message == "Horizontal line added at 63.00");
    }

    [Fact]
    public void Explicit_here_placement_carries_pane_and_price()
    {
        var h = new Harness();
        h.AddSeries("rsi-1", "RSI 14", "Pane_Rsi", "RSI", Enumerable.Repeat(50.0, 30).ToArray());

        string? id = h.Manager.AddHorizontalLineHere(4, 71.5, "Pane_Rsi", h.Bars);

        var line = h.Store.State.ActiveSeries.Single(s => s.Id == id);
        Assert.Equal("Pane_Rsi", line.Pane);
        Assert.Equal(71.5, line.Drawing!.AnchorPrice1);
    }

    [Fact]
    public void A_label_is_drawn_in_its_own_pane_and_not_in_the_price_pane()
    {
        var h = new Harness();
        h.AddSeries("rsi-1", "RSI 14", "Pane_Rsi", "RSI", Enumerable.Repeat(50.0, 30).ToArray());
        string? id = h.Manager.AddTextLabelHere(4, 66, "Pane_Rsi", h.Bars);
        h.Bus.Publish(new LabelTextEnteredEvent(id!, "watch this"));

        var label = h.Store.State.ActiveSeries.Single(s => s.Id == id);
        var theme = new ThemeService(Substitute.For<ISettingsManager>()).Current;
        using var bmp = new SKBitmap(800, 300);
        using var canvas = new SKCanvas(bmp);

        RenderContext Context(string pane) => new(
            canvas, SKRect.Create(0, 0, 800, 300), h.Bars, 0, h.Bars.Count,
            0, 100, false, 800f / h.Bars.Count, 1f, pane, -1, theme);

        Assert.Single(OverlayLayer.LayoutTextLabels(Context("Pane_Rsi"), new[] { label }));
        Assert.Empty(OverlayLayer.LayoutTextLabels(Context("Main"), new[] { label }));
    }

    [Fact]
    public void A_horizontal_line_in_a_pane_speaks_its_value()
    {
        var h = new Harness();
        h.AddSeries("candles", "Candles", "Main", "CANDLES");
        h.AddSeries("rsi-1", "RSI 14", "Pane_Rsi", "RSI", Enumerable.Repeat(50.0, 30).ToArray());
        string? id = h.Manager.AddHorizontalLineHere(7, 63, "Pane_Rsi", h.Bars);
        h.Focus(id!);

        var series = h.Store.State.ActiveSeries.Single(s => s.Id == id);
        var state = h.Store.State with
        {
            CurrentDataIndex = 7,
            ViewportRange = (1000, 2000),
            PaneRanges = ImmutableDictionary<string, (double Min, double Max)>.Empty.Add("Pane_Rsi", (0, 100)),
            SpeakTimestamps = false,
        };
        string said = new SpeechFormatter().FormatPointFeedback(
            state, isXMove: true, isYMove: false, series, h.Bars[7], "NAV_MOVE");

        Assert.Contains("63.00", said);
    }

    [Fact]
    public void The_anchor_nudge_steps_in_the_panes_units_not_the_price_range()
    {
        var h = new Harness();
        h.AddSeries("candles", "Candles", "Main", "CANDLES");
        h.AddSeries("rsi-1", "RSI 14", "Pane_Rsi", "RSI", Enumerable.Repeat(50.0, 30).ToArray());
        string? id = h.Manager.AddHorizontalLineHere(7, 50, "Pane_Rsi", h.Bars);
        h.Focus(id!);
        var before = h.Store.State.ActiveSeries.Single(s => s.Id == id).Drawing!.AnchorPrice1!.Value;
        var (paneMin, paneMax, _) = ChartMath.RangeFor(h.Store.State, "Pane_Rsi");
        double paneStep = DrawingInteractionManager.PriceNudgeStep(paneMax - paneMin, before);
        var (priceMin, priceMax) = h.Store.State.ViewportRange;
        double priceStep = DrawingInteractionManager.PriceNudgeStep(priceMax - priceMin, before);
        Assert.NotEqual(priceStep, paneStep);

        h.Bus.Publish(new NudgeDrawingAnchorEvent(AnchorNudgeDirection.Up));

        var line = h.Store.State.ActiveSeries.Single(s => s.Id == id);
        Assert.Equal(before + paneStep, line.Drawing!.AnchorPrice1!.Value, 9);
    }

    [Fact]
    public void A_label_in_a_pane_survives_a_workspace_round_trip()
    {
        string dir = TestTemp.NewDir("att-drawing-pane-");
        try
        {
            var h = new Harness();
            h.AddSeries("rsi-1", "RSI 14", "Pane_Rsi", "RSI", Enumerable.Repeat(50.0, 30).ToArray());
            string? id = h.Manager.AddTextLabelHere(4, 66, "Pane_Rsi", h.Bars);
            h.Bus.Publish(new LabelTextEnteredEvent(id!, "pane label"));

            var lib = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance,
                new TempWorkspacePaths()) { LibraryDirectoryOverride = dir };
            lib.SaveWorkspaceProfile("pane", h.Store);

            var loaded = lib.LoadProfile("pane")!;
            var saved = loaded.Tabs[0].Series.Single(s => s.Drawing != null);
            Assert.Equal("Pane_Rsi", saved.Pane);
            Assert.Equal(66, saved.Drawing!.AnchorPrice1);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public void Removing_the_indicator_leaves_the_drawing_pane_alive_and_reachable()
    {
        var h = new Harness();
        h.AddSeries("candles", "Candles", "Main", "CANDLES");
        h.AddSeries("rsi-1", "RSI 14", "Pane_Rsi", "RSI", Enumerable.Repeat(50.0, 30).ToArray());
        string? id = h.Manager.AddHorizontalLineHere(7, 63, "Pane_Rsi", h.Bars);

        h.Store.Dispatch(new RemoveSeriesAction("rsi-1"));

        var drawing = h.Store.State.ActiveSeries.Single(s => s.Id == id);
        Assert.Equal("Pane_Rsi", drawing.Pane);
        var panes = ChartPaneModel.Panes(h.Store.State.ActiveSeries);
        Assert.Contains(panes, p => p.Key == "Pane_Rsi");
        Assert.Contains(ChartPaneModel.SeriesInVisualOrder(h.Store.State.ActiveSeries), s => s.Id == id);
        _ = new ViewportRangeCalculator().Calculate(h.Store.State);   // no crash with no indicator data
    }

    [Fact]
    public void A_hand_moved_indicator_keeps_its_pane_across_a_load()
    {
        var meta = new IndicatorMetadata { Code = "Rsi", Name = "RSI", DefaultPane = "Pane_Rsi" };
        var moved = new SeriesConfig
        {
            Id = "rsi-1", IndicatorCode = "Rsi", Pane = "Pane_RSI_2", PanePinned = true,
        };
        WorkspaceInitializer.MigrateSeriesConfig(moved, new List<IndicatorMetadata> { meta });
        Assert.Equal("Pane_RSI_2", moved.Pane);

        var providerDefault = new SeriesConfig { Id = "rsi-2", IndicatorCode = "Rsi", Pane = "Pane_RSI_2" };
        WorkspaceInitializer.MigrateSeriesConfig(providerDefault, new List<IndicatorMetadata> { meta });
        Assert.Equal("Pane_Rsi", providerDefault.Pane);
    }

    [Fact]
    public void Remove_all_drawings_is_one_undo_entry()
    {
        var bus = new SpyEventBus();
        var store = new WorkspaceStore(bus, new ViewportRangeCalculator(),
            new ViewportNavigationService(), new VolumeStateService());
        store.Dispatch(new UpdateDataAction(new TimeSeriesBuffer<Ohlcv>(Bars()), IsInitialLoad: true));
        var undo = new ChartUndoStack();
        _ = new ChartCommandManager(bus, Substitute.For<IDataManager>(),
            Substitute.For<IDrawingInteractionManager>(), Substitute.For<ISeriesManagementService>(),
            store, undo: undo);

        var d1 = new ChartSeries(new SeriesConfig { Id = "d1", Name = "A" }, new SeriesDataBuffer { SeriesId = "d1" })
            { Drawing = new DrawingData { Type = DrawingType.TrendLine, AnchorDate1 = T0, AnchorPrice1 = 100, AnchorDate2 = T0.AddHours(2), AnchorPrice2 = 110 } };
        var d2 = new ChartSeries(new SeriesConfig { Id = "d2", Name = "B" }, new SeriesDataBuffer { SeriesId = "d2" })
            { Drawing = new DrawingData { Type = DrawingType.HorizontalLine, AnchorPrice1 = 105 } };
        store.Dispatch(new AddSeriesAction(d1));
        store.Dispatch(new AddSeriesAction(d2));

        bus.Publish(new RemoveAllDrawingsEvent());
        Assert.DoesNotContain(store.State.ActiveSeries, s => s.IsDrawing);

        Assert.True(undo.Undo());
        Assert.Equal(2, store.State.ActiveSeries.Count(s => s.IsDrawing));
        Assert.Contains(store.State.ActiveSeries, s => s.Id == "d1");
        Assert.Contains(store.State.ActiveSeries, s => s.Id == "d2");
    }
}
