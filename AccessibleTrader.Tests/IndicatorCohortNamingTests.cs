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
