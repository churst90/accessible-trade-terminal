using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// THE NAME IS COMPUTED AGAINST THE CHART, and it is recomputed when the chart changes.
///
/// <para>
/// <see cref="IndicatorInstanceNameTests"/> pins the rule; this pins the wiring, which is the
/// half that can be right in isolation and useless in the app. Adding a second EMA has to rename
/// the FIRST one too — a distinguishing suffix on one of a pair distinguishes it from nothing,
/// and the first EMA was named while it was alone on the chart.
/// </para>
/// </summary>
public class IndicatorCohortNamingTests
{
    [Fact]
    public void OneInstance_IsNamedWithoutItsParameters()
    {
        var (svc, store) = Build();

        svc.RegisterSeriesFromMetadata(EmaMeta(), Params(20));

        Assert.Equal("EMA", Named(store, "EMA").Single());
    }

    [Fact]
    public void AddingASecond_RenamesTHEFIRSTONETOO()
    {
        // The wiring test, and the one that fails against a namer that only looks forwards:
        // "EMA" and "EMA 50" would leave the user to infer that the unqualified one is the 20,
        // which is exactly the guessing the name exists to remove.
        var (svc, store) = Build();

        svc.RegisterSeriesFromMetadata(EmaMeta(), Params(20));
        svc.RegisterSeriesFromMetadata(EmaMeta(), Params(50));

        var names = Named(store, "EMA").ToList();
        Assert.Equal(2, names.Count);
        Assert.Contains("EMA 20", names);
        Assert.Contains("EMA 50", names);
    }

    [Fact]
    public void ADifferentIndicator_IsNotACohortMate()
    {
        // The cohort is one INDICATOR, not the chart. An SMA on the chart is no reason for the
        // lone EMA to start reciting its period — there is nothing to confuse it with.
        var (svc, store) = Build();

        svc.RegisterSeriesFromMetadata(EmaMeta(), Params(20));
        svc.RegisterSeriesFromMetadata(SmaMeta(), Params(50));

        Assert.Equal("EMA", Named(store, "EMA").Single());
        Assert.Equal("SMA", Named(store, "SMA").Single());
    }

    [Fact]
    public void AnIndicatorNamedByItsPeriod_SaysItAlone_AndTheSecondNeedsNothingMore()
    {
        // The declaration reaches the chart: a lone EMA that declares its period as its name is
        // "EMA 20" from the moment it is added, and a second at 50 is "EMA 50" with no rename
        // of the first needed — they were never ambiguous.
        var (svc, store) = Build();
        var meta = EmaMeta();
        meta.NamedByParameters.Add("Period");

        svc.RegisterSeriesFromMetadata(meta, Params(20));
        Assert.Equal("EMA 20", Named(store, "EMA").Single());

        svc.RegisterSeriesFromMetadata(meta, Params(50));
        Assert.Equal(new[] { "EMA 20", "EMA 50" }, Named(store, "EMA").OrderBy(n => n).ToArray());
    }

    [Fact]
    public void TwoIdenticalInstances_AreNumberedOneAndTwo_NotTwoAndTwo()
    {
        // RenameCohort used to let the namer fall back to siblings + 1, which for a pair is 2
        // for both. The reducer and the Object Tree then held two objects with one name.
        var (svc, store) = Build();

        svc.RegisterSeriesFromMetadata(EmaMeta(), Params(20));
        svc.RegisterSeriesFromMetadata(EmaMeta(), Params(20));

        var names = Named(store, "EMA").ToList();
        Assert.Equal(2, names.Distinct().Count());
        Assert.Contains("EMA 1", names);
        Assert.Contains("EMA 2", names);
    }

    [Fact]
    public void ARestoredWorkspace_DerivesTheName_InsteadOfReadingBackWhatWasSaved()
    {
        // THE REPORT OF 2026-09-05. "When I move through series, I now hear again all of the
        // parameters in a huge list." The namer had been fixed the day before; the restore path
        // pinned the saved Name/FriendlyName back onto the fresh series, and every workspace on
        // disk was saved when the namer recited everything. So the fix existed only on charts
        // built from scratch, and Cody's charts were all restored.
        var (svc, store) = Build();
        var saved = new SeriesConfig
        {
            Id = "ema-1", IndicatorCode = "EMA", Pane = "Main",
            Name = "EMA 20 close 0.5 3", FriendlyName = "EMA 20 close 0.5 3",
        };
        saved.Parameters["Period"] = 20;

        svc.RestoreSeriesFromSaved(saved, EmaMeta());

        var restored = Assert.Single(store.State.ActiveSeries, s => s.Id == "ema-1");
        Assert.Equal("EMA", restored.Config.FriendlyName);
        Assert.Equal("EMA", restored.Config.Name);
        Assert.Equal(20, restored.Config.Parameters["Period"]);   // the parameters ARE restored
    }

    [Fact]
    public void RestoringTwoOfAKind_NamesThemAgainstEachOther_LikeAnAdd()
    {
        // A load restores series one at a time, so the first is alone when it lands. The
        // cohort is re-named as the second arrives, exactly as it is after an interactive add.
        var (svc, store) = Build();
        var a = new SeriesConfig { Id = "a", IndicatorCode = "EMA", Pane = "Main", Name = "stale", FriendlyName = "stale" };
        a.Parameters["Period"] = 20;
        var b = new SeriesConfig { Id = "b", IndicatorCode = "EMA", Pane = "Main", Name = "stale", FriendlyName = "stale" };
        b.Parameters["Period"] = 50;

        svc.RestoreSeriesFromSaved(a, EmaMeta());
        svc.RestoreSeriesFromSaved(b, EmaMeta());

        Assert.Equal(new[] { "EMA 20", "EMA 50" }, Named(store, "EMA").OrderBy(n => n).ToArray());
    }

    // ── Scaffolding ─────────────────────────────────────────────────────────────

    private static IEnumerable<string> Named(IWorkspaceStore store, string code)
        => store.State.ActiveSeries
            .Where(s => string.Equals(s.Config.IndicatorCode, code, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Config.FriendlyName);

    private static Dictionary<string, object> Params(double period)
        => new() { ["Period"] = period };

    private static IndicatorMetadata EmaMeta() => MovingAverage("EMA");
    private static IndicatorMetadata SmaMeta() => MovingAverage("SMA");

    private static IndicatorMetadata MovingAverage(string code) => new()
    {
        Code = code,
        Name = code,
        DefaultPane = "Main",
        Parameters =
        {
            new IndicatorParameterMetadata
            {
                Name = "Period", DisplayName = "Period",
                DataType = typeof(int), DefaultValue = 20.0, MinValue = 2.0, MaxValue = 400.0,
            },
        },
        Components =
        {
            new IndicatorComponentMetadata { Name = "Line", DisplayName = code },
        },
    };

    private static (ISeriesManagementService svc, IWorkspaceStore store) Build()
    {
        var bus = new EventBus();
        var store = new WorkspaceStore(bus, new MockViewportRangeCalculator(),
            new MockViewportNavigationService(), new MockVolumeStateService());

        var styling = new StylingService(new ComponentRoleMapper(),
            new SonificationProfileProvider(), new PaneAssignmentService());
        var factory = new IndicatorModelFactory(styling, new MockIndicatorPreferencesService());
        var library = new WorkspaceLibraryService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceLibraryService>.Instance,
            new TempWorkspacePaths());
        var registry = new CustomIndicatorRegistry();
        var providers = new List<IIndicatorProvider>();
        var indicatorService = new IndicatorService(providers,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<IndicatorService>.Instance);
        var engine = new IndicatorEngine(indicatorService, registry, providers);

        var svc = new SeriesManagementService(store, bus, factory, styling, library, registry,
            engine, new MockIndicatorPreferencesService());
        return (svc, store);
    }
}
