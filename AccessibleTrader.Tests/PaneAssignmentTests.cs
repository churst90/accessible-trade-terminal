using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A pane is a Y axis, so two indicators share one only when they are in the same units.</b>
///
/// <para>
/// Cody, 2026-09-11: <i>"the RSI almost sounds flat, I still hear the texturing but the line is
/// definitely not correct sounding"</i> — then, <i>"RSI sounds correct after I removed the
/// MACD"</i>. Thirty indicators declared one shared <c>"Oscillator"</c> pane across five
/// incompatible scale families; <c>ViewportRangeCalculator</c> computes ONE range per pane; MACD
/// is a price difference (±800 on BTC) and RSI is bounded 0–100. RSI's whole working span was
/// ~2.5% of the pitch range. Diagnosis: <c>docs/SHARED_OSCILLATOR_PANE_2026-09-11.md</c>.
/// </para>
///
/// <para>
/// The fix has four parts and each has a guard here: (1) every non-overlay indicator declares a
/// pane of its own; (2) ONE resolver, <see cref="PaneAssignmentService.PaneFor"/>, decides the
/// pane at every site that turns metadata into a series; (3) a workspace saved on the retired
/// shared pane heals on restore, in the browser and headless alike; (4) the Add Indicator
/// dialog tells the user where the indicator will land, from the same resolver.
/// </para>
/// </summary>
public sealed class PaneAssignmentTests
{
    // ── The reported defect, end to end ──────────────────────────────────────

    private static IndicatorMetadata RealMeta(string code) =>
        IndicatorProviderFixture.AllProviders()
            .SelectMany(p => p.GetIndicators())
            .First(m => string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase));

    private static ChartSeries SeriesOf(IndicatorMetadata meta, string pane, double[] values)
    {
        var config = new SeriesConfig { Id = meta.Code.ToLowerInvariant(), IndicatorCode = meta.Code, Name = meta.Name, Pane = pane };
        config.Components.Add(new ComponentConfig { Name = "Line", DisplayType = ComponentDisplayType.Line, ColorHex = "#FFFFFF" });
        var buf = new SeriesDataBuffer { SeriesId = config.Id };
        buf.ComponentData["Line"] = values;
        return new ChartSeries(config, buf);
    }

    private static WorkspaceState StateWith(params ChartSeries[] series)
    {
        const int bars = 40;
        var data = Enumerable.Range(0, bars).Select(i => new Ohlcv(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i),
            100_000, 100_100, 99_900, 100_000, 10)).ToList();
        return WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(data),
            ActiveSeries = series.ToImmutableList(),
            ViewportStartIndex = 0,
            ViewportLength = bars,
        };
    }

    /// <summary>RSI 30→70 on one series, MACD ±800 on another — Cody's chart, in numbers.</summary>
    private static (ChartSeries Rsi, ChartSeries Macd) RsiAndMacd(string rsiPane, string macdPane)
    {
        var rsi = Enumerable.Range(0, 40).Select(i => 30.0 + i).ToArray();            // 30 … 69
        var macd = Enumerable.Range(0, 40).Select(i => -800.0 + i * 41.0).ToArray();  // −800 … +799
        return (SeriesOf(RealMeta("Rsi"), rsiPane, rsi), SeriesOf(RealMeta("Macd"), macdPane, macd));
    }

    /// <summary>
    /// The range RSI is normalised against — pitch, the drawn line, the hit tester and the
    /// Alt+Shift+/ description all read it — must be RSI's own. With the panes production
    /// assigns today it is; put both back on one pane and this goes red with the number Cody
    /// heard.
    /// </summary>
    [Fact]
    public void RsiBesideMacd_IsNormalisedAgainstItsOwnRange()
    {
        var (rsi, macd) = RsiAndMacd(
            PaneAssignmentService.PaneFor(RealMeta("Rsi")),
            PaneAssignmentService.PaneFor(RealMeta("Macd")));

        var ranges = new ViewportRangeCalculator().Calculate(StateWith(rsi, macd)).PaneRanges;

        Assert.True(ranges.TryGetValue(rsi.Pane, out var rsiRange), $"no range for RSI's pane '{rsi.Pane}'");
        double rsiSpan = 69 - 30;
        double paneSpan = rsiRange.Max - rsiRange.Min;
        Assert.True(paneSpan <= rsiSpan * 1.5,
            $"RSI's pane spans {rsiRange.Min:F0}…{rsiRange.Max:F0} for data that spans 30…69 — its working "
          + $"span is {rsiSpan / paneSpan:P0} of the pitch range. It is sharing a pane with something on "
          + "another scale.");
    }

    /// <summary>The mechanism, stated as the failing case so the guard above is known to bite.</summary>
    [Fact]
    public void OnOneSharedPane_RsiWouldBeFlat()
    {
        var (rsi, macd) = RsiAndMacd(PaneAssignmentService.RetiredSharedPane, PaneAssignmentService.RetiredSharedPane);
        var ranges = new ViewportRangeCalculator().Calculate(StateWith(rsi, macd)).PaneRanges;
        var shared = ranges[PaneAssignmentService.RetiredSharedPane];
        Assert.True(shared.Max - shared.Min > 1_500, "the shared pane did not stretch to MACD");
        Assert.True((69 - 30) / (shared.Max - shared.Min) < 0.03, "RSI should occupy under 3% of a shared pane");
    }

    // ── (1) The fleet: every non-overlay indicator has a pane of its own ──────

    private static List<(string Provider, IndicatorMetadata Meta, string Pane)> Fleet() =>
        IndicatorProviderFixture.ProviderTypes()
            .Select(t => (t.Name, Provider: IndicatorProviderFixture.Create(t)))
            .SelectMany(p => p.Provider.GetIndicators().Select(m => (p.Name, m, PaneAssignmentService.PaneFor(m))))
            .ToList();

    [Fact]
    public void NoTwoIndicators_ShareAPane_UnlessItsUnitsAreFixedByDefinition()
    {
        var fleet = Fleet();
        Assert.True(fleet.Count > 60, $"the scan only reached {fleet.Count} indicators — it is not checking the fleet");

        var collisions = fleet
            .Where(f => !PaneAssignmentService.IsSharedByDesign(f.Pane))
            .GroupBy(f => f.Pane, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(f => f.Meta.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(g => $"pane '{g.Key}' is shared by {string.Join(", ", g.Select(f => $"{f.Provider}/{f.Meta.Code}"))}")
            .OrderBy(x => x)
            .ToList();

        Assert.True(collisions.Count == 0,
            "A pane is a Y axis: one range is computed across everything in it, so two indicators on "
          + "different scales in one pane squash the smaller one flat (RSI beside MACD, 2026-09-11). "
          + "Only Main (price) and Volume may be shared:\n  " + string.Join("\n  ", collisions));
    }

    [Fact]
    public void TheRetiredSharedPane_IsDeclaredByNoProvider()
    {
        var declarers = Fleet()
            .Where(f => string.Equals(f.Meta.DefaultPane?.Trim(), PaneAssignmentService.RetiredSharedPane, StringComparison.OrdinalIgnoreCase))
            .Select(f => $"{f.Provider}/{f.Meta.Code}")
            .ToList();
        Assert.True(declarers.Count == 0,
            $"\"{PaneAssignmentService.RetiredSharedPane}\" was the shared bucket thirty indicators went flat in. "
          + "Declare a pane of the indicator's own (Pane_<Code>) instead:\n  " + string.Join("\n  ", declarers));
    }

    /// <summary>The overlays still overlay: the fix must not have pushed a moving average off the price axis.</summary>
    [Fact]
    public void PriceOverlays_StillLandOnMain()
    {
        foreach (var code in new[] { "Ema", "Sma", "Bb", "Vwap", "Ichimoku", "ParabolicSar" })
            Assert.Equal(ChartPaneModel.MainPaneKey, PaneAssignmentService.PaneFor(RealMeta(code)));
        Assert.Equal(PaneAssignmentService.VolumePane, PaneAssignmentService.PaneFor(RealMeta("VOLUME")));
    }

    // ── (2) One resolver ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("Oscillator", "Pane_Rsi")]      // the retired bucket → own pane
    [InlineData("oscillator", "Pane_Rsi")]      // whatever the case in a hand-edited file
    [InlineData("", "Pane_Rsi")]                // nothing declared → own pane, never Main by accident
    [InlineData("Main", "Main")]                // an overlay stays on the price axis
    [InlineData("Volume", "Volume")]
    [InlineData("Pane_CIPHER_B", "Pane_CIPHER_B")] // a declared own pane is kept verbatim
    [InlineData("My dataset", "My dataset")]    // a My Data dataset names its own pane
    public void PaneFor_ResolvesTheDeclaration(string declared, string expected)
    {
        var meta = new IndicatorMetadata { Code = "Rsi", Name = "RSI", DefaultPane = declared };
        Assert.Equal(expected, PaneAssignmentService.PaneFor(meta));
    }

    [Fact]
    public void PaneFor_TreatsANullDeclarationAsAnOwnPane()
    {
        var meta = new IndicatorMetadata { Code = "Rsi", Name = "RSI", DefaultPane = null! };
        Assert.Equal("Pane_Rsi", PaneAssignmentService.PaneFor(meta));
    }

    /// <summary>The metadata-free path and the resolver agree on what an own pane is called.</summary>
    [Fact]
    public void TheCodeOnlyPath_AgreesOnTheShapeOfAnOwnPane()
    {
        Assert.Equal(PaneAssignmentService.OwnPaneKey("Rsi"), new PaneAssignmentService().GetPane("Rsi"));
        Assert.Equal("Pane_Rsi", PaneAssignmentService.OwnPaneKey("Rsi"));
    }

    private static (ISeriesManagementService svc, IWorkspaceStore store) SeriesService(params IIndicatorProvider[] providers)
    {
        var bus = new EventBus();
        var store = new WorkspaceStore(bus, new ViewportRangeCalculator(),
            new MockViewportNavigationService(), new MockVolumeStateService());
        var styling = new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());
        var factory = new IndicatorModelFactory(styling, new MockIndicatorPreferencesService());
        var library = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, new TempWorkspacePaths());
        var registry = new CustomIndicatorRegistry();
        var list = providers.ToList();
        var indicatorService = new IndicatorService(list, NullLogger<IndicatorService>.Instance);
        var engine = new IndicatorEngine(indicatorService, registry, list);
        var svc = new SeriesManagementService(store, bus, factory, styling, library, registry, engine, new MockIndicatorPreferencesService());
        return (svc, store);
    }

    /// <summary>Adding RSI and MACD from the dialog puts them on two panes — the Add path.</summary>
    [Fact]
    public void AddingRsiAndMacd_PutsThemOnTwoPanes()
    {
        var (svc, store) = SeriesService(new SkenderBoundedOscillatorProvider(), new SkenderZeroCrossProvider());

        svc.RegisterSeriesFromMetadata(RealMeta("Rsi"));
        svc.RegisterSeriesFromMetadata(RealMeta("Macd"));

        var rsi = Assert.Single(store.State.ActiveSeries, s => s.Config.IndicatorCode == "Rsi");
        var macd = Assert.Single(store.State.ActiveSeries, s => s.Config.IndicatorCode == "Macd");
        Assert.Equal("Pane_Rsi", rsi.Pane);
        Assert.Equal("Pane_Macd", macd.Pane);
        Assert.NotEqual(rsi.Pane, macd.Pane);
    }

    /// <summary>Two instances of ONE indicator share its pane: they are in the same units.</summary>
    [Fact]
    public void TwoRsis_ShareTheRsiPane()
    {
        var (svc, store) = SeriesService(new SkenderBoundedOscillatorProvider());
        svc.RegisterSeriesFromMetadata(RealMeta("Rsi"), new() { ["Period"] = 14 });
        svc.RegisterSeriesFromMetadata(RealMeta("Rsi"), new() { ["Period"] = 7 });

        var panes = store.State.ActiveSeries.Where(s => s.Config.IndicatorCode == "Rsi").Select(s => s.Pane).Distinct().ToList();
        Assert.Equal(new[] { "Pane_Rsi" }, panes);
    }

    // ── (3) A saved workspace heals ──────────────────────────────────────────

    [Fact]
    public void MigrateSeriesConfig_MovesASeriesOffTheRetiredPane_OntoItsOwn()
    {
        var saved = new SeriesConfig { Id = "rsi-1", IndicatorCode = "Rsi", Pane = PaneAssignmentService.RetiredSharedPane };
        WorkspaceInitializer.MigrateSeriesConfig(saved, new List<IndicatorMetadata> { RealMeta("Rsi") });
        Assert.Equal("Pane_Rsi", saved.Pane);
    }

    /// <summary>A drawing, or an indicator whose provider is gone, keeps whatever it saved.</summary>
    [Fact]
    public void MigrateSeriesConfig_LeavesAnUnknownIndicatorWhereItWas()
    {
        var saved = new SeriesConfig { Id = "x", IndicatorCode = "GONE", Pane = PaneAssignmentService.RetiredSharedPane };
        WorkspaceInitializer.MigrateSeriesConfig(saved, new List<IndicatorMetadata>());
        Assert.Equal(PaneAssignmentService.RetiredSharedPane, saved.Pane);
    }

    /// <summary>
    /// The browser restore path, both formats. The initializer hands the migrated config to the
    /// series service, so what the service receives is what the chart gets.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RestoreWorkspace_HandsTheSeriesServiceAHealedPane(bool multiTab)
    {
        var seriesService = Substitute.For<ISeriesManagementService>();
        var store = Substitute.For<IWorkspaceStore>();
        store.State.Returns(WorkspaceState.Initial);
        // Built BEFORE the Returns: RealMeta constructs providers through NSubstitute, and a
        // substitute configured inside another's Returns is the documented way to confuse it.
        var allMeta = new List<IndicatorMetadata> { RealMeta("Rsi"), RealMeta("Macd") };
        var indicators = Substitute.For<IIndicatorService>();
        indicators.GetAvailableIndicators().Returns(allMeta);
        var init = new WorkspaceInitializer(seriesService, store, indicators,
            Substitute.For<IStrategyEngine>(), Substitute.For<IConfigurableStrategyFactory>(), Substitute.For<IStrategyLibrary>());

        var rsi = new SeriesConfig { Id = "rsi-1", IndicatorCode = "Rsi", Pane = PaneAssignmentService.RetiredSharedPane };
        var macd = new SeriesConfig { Id = "macd-1", IndicatorCode = "Macd", Pane = PaneAssignmentService.RetiredSharedPane };
        var config = multiTab
            ? new WorkspaceConfiguration { Tabs = { new TabConfiguration { Series = { rsi, macd } } } }
            : new WorkspaceConfiguration { Series = { rsi, macd } };

        init.RestoreWorkspace(config);

        seriesService.Received(1).RestoreSeriesFromSaved(Arg.Is<SeriesConfig>(c => c.Id == "rsi-1" && c.Pane == "Pane_Rsi"), Arg.Any<IndicatorMetadata?>());
        seriesService.Received(1).RestoreSeriesFromSaved(Arg.Is<SeriesConfig>(c => c.Id == "macd-1" && c.Pane == "Pane_Macd"), Arg.Any<IndicatorMetadata?>());
    }

    private static HeadlessChartFactory Headless()
    {
        var providers = new List<IIndicatorProvider> { new CoreIndicatorProvider(), new SkenderBoundedOscillatorProvider(), new SkenderZeroCrossProvider() };
        var indicators = new IndicatorService(providers, NullLogger<IndicatorService>.Instance);
        var engine = new IndicatorEngine(indicators, new CustomIndicatorRegistry(), providers);
        var styling = new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());
        var prefs = new MockIndicatorPreferencesService();
        return new HeadlessChartFactory(indicators, engine, new IndicatorStateMapper(), new IndicatorModelFactory(styling, prefs), prefs, new IndicatorContextAnalyzer());
    }

    /// <summary>The browser-closed path: a saved tab's series heal the same way.</summary>
    [Fact]
    public void HeadlessChart_RestoresASavedSeriesOntoItsOwnPane()
    {
        var saved = new List<SeriesConfig>
        {
            new() { Id = "rsi-1", IndicatorCode = "Rsi", Name = "RSI", Pane = PaneAssignmentService.RetiredSharedPane, IsAutoNarrated = true, IsVisible = true },
            new() { Id = "macd-1", IndicatorCode = "Macd", Name = "MACD", Pane = PaneAssignmentService.RetiredSharedPane, IsAutoNarrated = true, IsVisible = true },
        };
        var chart = Headless().Create(new ChartIdentity("Spot", "Bitstamp", "BTC/USD", "1h"), saved);

        Assert.Equal("Pane_Rsi", Assert.Single(chart.Series, s => s.Id == "rsi-1").Pane);
        Assert.Equal("Pane_Macd", Assert.Single(chart.Series, s => s.Id == "macd-1").Pane);
    }

    /// <summary>An alert on an indicator the tab does not carry builds that indicator from its metadata — through the resolver.</summary>
    [Fact]
    public void HeadlessChart_BuildsAnAlertOnlySeriesOnItsOwnPane()
    {
        var alert = new AlertDefinition
        {
            Id = "a1", Name = "RSI over 70", Target = AlertTarget.Indicator, IndicatorCode = "Rsi",
            Condition = AlertCondition.CrossesAbove, Threshold = 70, Delivery = AlertDelivery.Speech,
        };
        var chart = Headless().Create(new ChartIdentity("Spot", "Bitstamp", "BTC/USD", "1h"), saved: null, new[] { alert });

        var rsi = Assert.Single(chart.Series, s => string.Equals(s.Config.IndicatorCode, "Rsi", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Pane_Rsi", rsi.Pane);
    }

    // ── (4) The dialog says where it will land ───────────────────────────────

    [Theory]
    [InlineData("Rsi", "its own pane")]
    [InlineData("Macd", "its own pane")]
    [InlineData("Ema", "Main pane")]
    [InlineData("VOLUME", "Volume pane")]
    public void TheAddIndicatorDialog_NamesThePaneTheIndicatorWillLandOn(string code, string expected)
    {
        Assert.Equal(expected, AccessibleTrader.BlazorClient.Components.AddIndicatorModal.PaneDescription(RealMeta(code)));
    }
}
