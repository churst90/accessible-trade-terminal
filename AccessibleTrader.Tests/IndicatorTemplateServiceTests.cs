using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// Indicator templates are the workspace library's twin: same folder shape, same name guard,
/// same atomic write and the same "null, never throw" read. What differs is what is kept —
/// the indicators and their panes, not the symbol, timeframe or drawings.
/// </summary>
public sealed class IndicatorTemplateServiceTests
{
    private static (IndicatorTemplateService Templates, WorkspaceLibraryService Workspaces, TempWorkspacePaths Paths)
        Build()
    {
        var paths = new TempWorkspacePaths();
        var workspaces = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, paths);
        var templates = new IndicatorTemplateService(
            NullLogger<IndicatorTemplateService>.Instance, paths, workspaces);
        return (templates, workspaces, paths);
    }

    private static SeriesConfig RichSeries()
    {
        var config = new SeriesConfig
        {
            Id = "rsi-1",
            Name = "RSI",
            FriendlyName = "RSI 14",
            IndicatorCode = "Rsi",
            Pane = "Pane_Rsi",
            PanePinned = true,
            IsMuted = true,
            Volume = 0.35f,
            IsVisible = false,
            IsAutoNarrated = true,
            AnnounceAcrossSeries = false,
        };
        config.Parameters["lookbackPeriods"] = 14;
        config.StringParameters["thresholdMode"] = "percentile";
        config.Components.Add(new ComponentConfig { Name = "Rsi", DisplayType = ComponentDisplayType.Oscillator });
        config.ExcludedComponents.Add(new ComponentConfig { Name = "Signal", IsEnabled = false });
        config.Levels.Add(new LevelConfig
        {
            Name = "Overbought",
            Value = 70,
            ColorHex = "#FF0000",
            DashStyle = DashStyle.Dot,
            Thickness = 2,
            PlayEarcon = true,
            EarconVolume = 0.5f,
            ZoneNoiseAmount = 0.2f,
            ZoneNoiseType = "Rain",
            IsUserDefined = true,
        });
        return config;
    }

    [Fact]
    public void Save_and_load_round_trips_every_kept_field_and_the_pane_ratios()
    {
        var (templates, _, _) = Build();
        var template = new IndicatorTemplate
        {
            Series = { RichSeries() },
            PaneHeightRatios = { ["Pane_Rsi"] = 0.42f },
            SavedFrom = "Kraken BTCUSDT 1h",
            SavedUtc = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc),
        };

        templates.Save("Oscillators", template);
        var loaded = templates.Load("Oscillators");

        Assert.NotNull(loaded);
        Assert.Equal(1, loaded!.Version);
        Assert.Equal("Kraken BTCUSDT 1h", loaded.SavedFrom);
        Assert.Equal(new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc), loaded.SavedUtc);
        Assert.Equal(0.42f, loaded.PaneHeightRatios["Pane_Rsi"]);

        var series = Assert.Single(loaded.Series);
        Assert.Equal("rsi-1", series.Id);
        Assert.Equal("RSI 14", series.FriendlyName);
        Assert.Equal("Pane_Rsi", series.Pane);
        Assert.True(series.PanePinned);
        Assert.True(series.IsMuted);
        Assert.Equal(0.35f, series.Volume);
        Assert.False(series.IsVisible);
        Assert.True(series.IsAutoNarrated);
        Assert.False(series.AnnounceAcrossSeries);
        Assert.Equal(14, series.Parameters["lookbackPeriods"]);
        Assert.Equal("percentile", series.StringParameters["thresholdMode"]);
        Assert.Equal("Rsi", Assert.Single(series.Components).Name);
        Assert.Equal("Signal", Assert.Single(series.ExcludedComponents).Name);

        var level = Assert.Single(series.Levels);
        Assert.Equal("Overbought", level.Name);
        Assert.Equal(70, level.Value);
        Assert.Equal("#FF0000", level.ColorHex);
        Assert.Equal(DashStyle.Dot, level.DashStyle);
        Assert.Equal(2, level.Thickness);
        Assert.True(level.PlayEarcon);
        Assert.Equal(0.5f, level.EarconVolume);
        Assert.Equal(0.2f, level.ZoneNoiseAmount);
        Assert.Equal("Rain", level.ZoneNoiseType);
    }

    [Fact]
    public void GetAvailable_lists_the_saved_templates_and_delete_removes_one()
    {
        var (templates, _, _) = Build();
        templates.Save("Trend", new IndicatorTemplate());
        templates.Save("Oscillators", new IndicatorTemplate());

        Assert.Equal(new[] { "Oscillators", "Trend" }, templates.GetAvailable());

        templates.Delete("Trend");

        Assert.Equal(new[] { "Oscillators" }, templates.GetAvailable());
        Assert.Null(templates.Load("Trend"));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("/etc/passwd")]
    [InlineData("alerts")]
    public void The_same_name_guard_as_workspaces_refuses_unsafe_template_names(string name)
    {
        var (templates, _, paths) = Build();
        templates.Save(name, new IndicatorTemplate { SavedFrom = "written" });

        Assert.Empty(templates.GetAvailable());
        Assert.Null(templates.Load(name));
        templates.Delete(name);

        string folder = Path.Combine(paths.AppDataDirectory, "IndicatorTemplates");
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public void An_unreadable_file_loads_as_null()
    {
        var (templates, _, paths) = Build();
        CorruptFileQuarantine.ClearSessionReports();
        string folder = Path.Combine(paths.AppDataDirectory, "IndicatorTemplates");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "broken.json"), "{ not json ");

        Assert.Null(templates.Load("broken"));
        Assert.Contains("broken.json", string.Join(" ", CorruptFileQuarantine.SessionReports));
    }

    [Fact]
    public void The_folder_is_IndicatorTemplates_beside_Workspaces()
    {
        var (templates, _, paths) = Build();
        templates.Save("mine", new IndicatorTemplate());

        Assert.True(File.Exists(Path.Combine(paths.AppDataDirectory, "IndicatorTemplates", "mine.json")));
        Assert.True(Directory.Exists(Path.Combine(paths.AppDataDirectory, "Workspaces")));
    }

    [Fact]
    public void Two_users_do_not_see_each_others_templates()
    {
        var alicePaths = new TempWorkspacePaths();
        var bobPaths = new TempWorkspacePaths();
        var alice = new IndicatorTemplateService(
            NullLogger<IndicatorTemplateService>.Instance, alicePaths,
            new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, alicePaths));
        var bob = new IndicatorTemplateService(
            NullLogger<IndicatorTemplateService>.Instance, bobPaths,
            new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, bobPaths));

        alice.Save("mine", new IndicatorTemplate());

        Assert.Contains("mine", alice.GetAvailable());
        Assert.DoesNotContain("mine", bob.GetAvailable());
        Assert.Null(bob.Load("mine"));
    }

    // ── Capture ──────────────────────────────────────────────────────────────

    private static ChartSeries Series(string id, string code, string pane, string friendly,
                                      DrawingData? drawing = null) =>
        new(new SeriesConfig { Id = id, Name = friendly, FriendlyName = friendly, IndicatorCode = code, Pane = pane },
            new SeriesDataBuffer { SeriesId = id })
        {
            Drawing = drawing,
        };

    private static IWorkspaceStore StoreWith(params ChartSeries[] series)
    {
        var state = WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto", "Kraken", "BTCUSDT", "1h"),
            ActiveSeries = series.ToImmutableList(),
            PaneHeightRatios = new Dictionary<string, float> { ["Pane_Rsi"] = 0.3f }.ToImmutableDictionary(),
        };
        var store = Substitute.For<IWorkspaceStore>();
        store.State.Returns(state);
        return store;
    }

    [Fact]
    public void Capture_keeps_the_indicators_and_their_panes_and_leaves_core_drawings_and_retired_out()
    {
        var (templates, _, _) = Build();
        var store = StoreWith(
            Series("candles", "CANDLES", "Main", "Candles"),
            Series("price", "PRICE", "Main", "Price"),
            Series("volume", "VOLUME", "Volume", "Volume"),
            Series("ema-20", "Ema", "Main", "EMA 20"),
            Series("ema-50", "Ema", "Main", "EMA 50"),
            Series("rsi-14", "Rsi", "Pane_Rsi", "RSI 14"),
            Series("drawing-1", "", "Main", "Trend (1)",
                new DrawingData { Type = DrawingType.TrendLine, AnchorDate1 = new DateTime(2026, 1, 1), AnchorPrice1 = 100 }),
            Series("spider", "SPIDER_LINES", "Main", "Spider Lines"));

        var captured = templates.Capture(store);

        Assert.Equal(new[] { "EMA 20", "EMA 50", "RSI 14" }, captured.Series.Select(s => s.FriendlyName));
        Assert.Equal(new[] { "Main", "Main", "Pane_Rsi" }, captured.Series.Select(s => s.Pane));
        Assert.Equal("Kraken BTCUSDT 1h", captured.SavedFrom);
        Assert.Equal(0.3f, captured.PaneHeightRatios["Pane_Rsi"]);
        Assert.Equal("3 indicators: EMA 20, EMA 50, RSI 14; 1 pane",
                     IndicatorTemplateService.Describe(captured));
    }

    [Fact]
    public void Describe_names_series_in_order_and_counts_non_main_panes()
    {
        var template = new IndicatorTemplate
        {
            Series =
            {
                new SeriesConfig { FriendlyName = "RSI 14", Pane = "Pane_Rsi" },
                new SeriesConfig { FriendlyName = "EMA 20", Pane = "Main" },
                new SeriesConfig { FriendlyName = "EMA 50", Pane = "Main" },
                new SeriesConfig { FriendlyName = "Cipher B", Pane = "Pane_CIPHER_B" },
            },
        };

        Assert.Equal("4 indicators: RSI 14, EMA 20, EMA 50, Cipher B; 2 panes",
                     IndicatorTemplateService.Describe(template));
        Assert.Equal("could not be read", IndicatorTemplateService.Describe(null));
    }

    [Fact]
    public void Workspace_capture_is_unchanged_by_the_template()
    {
        var (_, workspaces, _) = Build();
        var store = StoreWith(
            Series("candles", "CANDLES", "Main", "Candles"),
            Series("drawing-1", "", "Main", "Trend (1)",
                new DrawingData { Type = DrawingType.TrendLine, AnchorDate1 = new DateTime(2026, 1, 1), AnchorPrice1 = 100 }),
            Series("spider", "SPIDER_LINES", "Main", "Spider Lines"));

        var workspace = workspaces.CaptureWorkspace(store);

        var tab = Assert.Single(workspace.Tabs);
        Assert.Equal(new[] { "CANDLES", "", "SPIDER_LINES" }, tab.Series.Select(s => s.IndicatorCode));
        Assert.NotNull(tab.Series.Single(s => s.IndicatorCode == "").Drawing);
    }
}
