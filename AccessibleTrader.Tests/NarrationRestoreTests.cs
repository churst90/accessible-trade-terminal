using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Narration is a per-series and per-component SWITCH the user sets with N, and the workspace
/// file already carries both flags — <c>SeriesConfig.IsAutoNarrated</c> and
/// <c>ComponentConfig.IsAutoNarrated</c> are serialised with everything else. What was missing
/// was the READ side: <c>RestoreSeriesFromSaved</c> copied mute, volume and visibility back onto
/// the rebuilt series and the factory's saved-component merge copied visibility, mute, volume
/// and frequency, and neither copied narration. Cody, 2026-09-05: "Workspaces also do not
/// persist narration per component when the terminal is restarted. Everything should be left as
/// the user had it before the terminal closed."
///
/// Core series (Candles, Price, Volume) never had the defect: they restore their saved config
/// verbatim. It was every INDICATOR — the ones N is most often pressed on.
/// </summary>
public class NarrationRestoreTests
{
    [Fact]
    public void ANarratingIndicator_IsStillNarrating_AfterRestore()
    {
        var (svc, store) = Build();
        var saved = new SeriesConfig { Id = "ema-1", IndicatorCode = "EMA", Pane = "Main" };
        saved.Parameters["Period"] = 20;
        saved.IsAutoNarrated = true;

        svc.RestoreSeriesFromSaved(saved, EmaMeta());

        var restored = Assert.Single(store.State.ActiveSeries, s => s.Id == "ema-1");
        Assert.True(restored.IsAutoNarrated,
            "the series flag N sets was written to disk and dropped on the way back");
    }

    [Fact]
    public void AComponentSelectedWithN_IsStillSelected_AfterRestore()
    {
        // Two components, one selected: "narrate only Buy". The selection is the whole point of
        // pressing N on a component, and it lived only until the next restart.
        var (svc, store) = Build();
        var saved = new SeriesConfig { Id = "c-1", IndicatorCode = "TWOCOMP", Pane = "Oscillator" };
        saved.IsAutoNarrated = true;
        saved.Components.Add(new ComponentConfig { Name = "Buy",  IsAutoNarrated = true });
        saved.Components.Add(new ComponentConfig { Name = "Sell", IsAutoNarrated = false });

        svc.RestoreSeriesFromSaved(saved, TwoComponentMeta());

        var restored = Assert.Single(store.State.ActiveSeries, s => s.Id == "c-1");
        Assert.True(restored.Components.Single(c => c.Name == "Buy").IsAutoNarrated);
        Assert.False(restored.Components.Single(c => c.Name == "Sell").IsAutoNarrated);
    }

    [Fact]
    public void AComponentMutedWithM_IsStillMuted_AfterRestore()
    {
        // Found while fixing narration: the factory's hand-written component clone copied
        // IsVisible and IsEnabled but neither IsMuted nor IsAutoNarrated, so the saved-state
        // merge two lines above it was setting a mute the clone then threw away. M on a
        // component was undone by every restart, for as long as component mute has existed.
        var (svc, store) = Build();
        var saved = new SeriesConfig { Id = "c-2", IndicatorCode = "TWOCOMP", Pane = "Oscillator" };
        saved.Components.Add(new ComponentConfig { Name = "Buy",  IsMuted = true });
        saved.Components.Add(new ComponentConfig { Name = "Sell", IsMuted = false });

        svc.RestoreSeriesFromSaved(saved, TwoComponentMeta());

        var restored = Assert.Single(store.State.ActiveSeries, s => s.Id == "c-2");
        Assert.True(restored.Components.Single(c => c.Name == "Buy").IsMuted);
        Assert.False(restored.Components.Single(c => c.Name == "Sell").IsMuted);
    }

    [Fact]
    public void AnIndicatorThatWasNotNarrating_StaysQuiet_AfterRestore()
    {
        // The control: restoring must not switch narration ON either. Both flags default to
        // false, so this pins the copy as a copy rather than a constant.
        var (svc, store) = Build();
        var saved = new SeriesConfig { Id = "ema-2", IndicatorCode = "EMA", Pane = "Main" };
        saved.Parameters["Period"] = 50;

        svc.RestoreSeriesFromSaved(saved, EmaMeta());

        var restored = Assert.Single(store.State.ActiveSeries, s => s.Id == "ema-2");
        Assert.False(restored.IsAutoNarrated);
        Assert.All(restored.Components, c => Assert.False(c.IsAutoNarrated));
    }

    // ── Scaffolding (the IndicatorCohortNamingTests build, unchanged) ─────────────

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

    private static IndicatorMetadata EmaMeta() => new()
    {
        Code = "EMA", Name = "EMA", DefaultPane = "Main",
        Parameters =
        {
            new IndicatorParameterMetadata
            {
                Name = "Period", DisplayName = "Period",
                DataType = typeof(int), DefaultValue = 20.0, MinValue = 2.0, MaxValue = 400.0,
            },
        },
        Components = { new IndicatorComponentMetadata { Name = "Line", DisplayName = "EMA" } },
    };

    private static IndicatorMetadata TwoComponentMeta() => new()
    {
        Code = "TWOCOMP", Name = "Two component", DefaultPane = "Oscillator",
        Components =
        {
            new IndicatorComponentMetadata { Name = "Buy",  DisplayName = "Buy",  DisplayType = ComponentDisplayType.TriangleUp },
            new IndicatorComponentMetadata { Name = "Sell", DisplayName = "Sell", DisplayType = ComponentDisplayType.TriangleDown },
        },
    };
}
