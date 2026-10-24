using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// RESET TO DEFAULTS RESETS EVERYTHING (Cody, 2026-10-23).
///
/// <para>
/// Until then Properties → Reset to Defaults rebuilt the parts and levels and carried the
/// parameters across, so the periods and modes the user had changed survived the reset. These pin
/// what it now resets, and what it deliberately keeps (<see cref="SeriesManagementService.ResetToIndicatorDefaults"/>).
/// </para>
/// </summary>
public class ResetToIndicatorDefaultsTests
{
    [Fact]
    public void Parameters_NumericBoolAndText_GoBackToTheMetadataDefaults()
    {
        var (svc, store, _) = Build();
        svc.RegisterSeriesFromMetadata(Meta(), new() { ["Period"] = 50.0, ["Legacy"] = true, ["Source"] = "open" });
        var s = Edited(store);
        Assert.Equal(50, s.Parameters["Period"]);

        svc.ResetToIndicatorDefaults(s, Meta());

        var after = Single(store);
        Assert.Equal(20, after.Parameters["Period"]);
        Assert.Equal(0, after.Parameters["Legacy"]);
        Assert.Equal("close", after.StringParameters["Source"]);
    }

    [Fact]
    public void Parts_ComeBackAsTheIndicatorShipsThem_ADefaultOffPartStaysLeftOut()
    {
        var (svc, store, _) = Build();
        svc.RegisterSeriesFromMetadata(Meta(), null);
        var s = Edited(store);
        // The user leaves the line out and includes the default-off part.
        var line = s.Config.Components.Single(c => c.Name == "Line");
        s.Config.Components.Remove(line); s.Config.ExcludedComponents.Add(line);
        var extra = s.Config.ExcludedComponents.Single(c => c.Name == "Extra");
        s.Config.ExcludedComponents.Remove(extra); s.Config.Components.Add(extra);
        s.Config.Components.Single(c => c.Name == "Extra").ColorHex = "#123456";

        svc.ResetToIndicatorDefaults(s, Meta());

        var after = Single(store);
        Assert.Equal(new[] { "Line" }, after.Config.Components.Select(c => c.Name));
        Assert.Equal(new[] { "Extra" }, after.Config.ExcludedComponents.Select(c => c.Name));
        Assert.NotEqual("#123456", after.Config.ExcludedComponents.Single().ColorHex);
    }

    [Fact]
    public void SeriesWideSettings_Reset_SwitchesTheKeysToggleAreKept()
    {
        var (svc, store, _) = Build();
        svc.RegisterSeriesFromMetadata(Meta(), null);
        var s = Edited(store);
        var shipped = (s.Config.Volume, s.Config.AnnounceAcrossSeries);
        s.Config.Volume = 0.25f;
        s.Config.AnnounceAcrossSeries = !shipped.AnnounceAcrossSeries;
        s.Config.IsMuted = true;
        s.Config.IsAutoNarrated = true;

        svc.ResetToIndicatorDefaults(s, Meta());

        var after = Single(store);
        Assert.Equal(shipped, (after.Config.Volume, after.Config.AnnounceAcrossSeries));
        Assert.True(after.Config.IsMuted);
        Assert.True(after.Config.IsAutoNarrated);
    }

    [Fact]
    public void KeepsTheUsersOwnLevels_AndAProfilesAnchor_AndClearsSavedPreferences()
    {
        var (svc, store, prefs) = Build();
        svc.RegisterSeriesFromMetadata(Meta(), new() { ["AnchorStart"] = 1_700_000_000.0 });
        var s = Edited(store);
        s.Levels.Add(new LevelConfig { Name = "My level", Value = 42, IsUserDefined = true });

        svc.ResetToIndicatorDefaults(s, Meta());

        var after = Single(store);
        Assert.Equal(1_700_000_000.0, after.Parameters["AnchorStart"]);
        Assert.Contains(after.Levels, l => l.Name == "My level" && l.Value == 42);
        Assert.Equal(new[] { "RESETME" }, prefs.Cleared);
    }

    [Fact]
    public void TheNameIsDerivedAgain_FromTheDefaultParameters()
    {
        // Two of them, 20 and 50, are "Reset me 20" and "Reset me 50". Reset the 50 and it is a 20:
        // a name still saying 50 would describe settings the indicator no longer has.
        var (svc, store, _) = Build();
        svc.RegisterSeriesFromMetadata(Meta(), new() { ["Period"] = 20.0 });
        svc.RegisterSeriesFromMetadata(Meta(), new() { ["Period"] = 50.0 });
        var fifty = store.State.ActiveSeries.Single(x => x.Parameters["Period"] == 50).Clone();
        Assert.Contains("50", fifty.FriendlyName);

        svc.ResetToIndicatorDefaults(fifty, Meta());

        Assert.All(store.State.ActiveSeries, x => Assert.DoesNotContain("50", x.FriendlyName));
        Assert.Equal(2, store.State.ActiveSeries.Count);
    }

    // ── Scaffolding (the NarrationRestoreTests build, with a prefs service that records) ──

    private sealed class RecordingPrefs : MockIndicatorPreferencesService, IIndicatorPreferencesService
    {
        public List<string> Cleared { get; } = new();
        void IIndicatorPreferencesService.ClearPreferences(string indicatorCode) => Cleared.Add(indicatorCode);
    }

    /// <summary>As the dialog does it: a clone of the series in the store, edited, then reset.</summary>
    private static ChartSeries Edited(IWorkspaceStore store) => Single(store).Clone();

    private static ChartSeries Single(IWorkspaceStore store) => Assert.Single(store.State.ActiveSeries);

    private static (ISeriesManagementService svc, IWorkspaceStore store, RecordingPrefs prefs) Build()
    {
        var bus = new EventBus();
        var store = new WorkspaceStore(bus, new MockViewportRangeCalculator(),
            new MockViewportNavigationService(), new MockVolumeStateService());
        var prefs = new RecordingPrefs();
        var styling = new StylingService(new ComponentRoleMapper(),
            new SonificationProfileProvider(), new PaneAssignmentService());
        var factory = new IndicatorModelFactory(styling, prefs);
        var library = new WorkspaceLibraryService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceLibraryService>.Instance,
            new TempWorkspacePaths());
        var registry = new CustomIndicatorRegistry();
        var providers = new List<IIndicatorProvider>();
        var indicatorService = new IndicatorService(providers,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<IndicatorService>.Instance);
        var engine = new IndicatorEngine(indicatorService, registry, providers);
        var svc = new SeriesManagementService(store, bus, factory, styling, library, registry, engine, prefs);
        return (svc, store, prefs);
    }

    private static IndicatorMetadata Meta() => new()
    {
        Code = "RESETME", Name = "Reset me", DefaultPane = "Main",
        Parameters =
        {
            new IndicatorParameterMetadata { Name = "Period", DisplayName = "Period", DataType = typeof(int), DefaultValue = 20.0 },
            new IndicatorParameterMetadata { Name = "Legacy", DisplayName = "Legacy mode", DataType = typeof(bool), DefaultValue = false },
            new IndicatorParameterMetadata { Name = "Source", DisplayName = "Source", DataType = typeof(string), DefaultValue = "close" },
        },
        Components =
        {
            new IndicatorComponentMetadata { Name = "Line", DisplayName = "Line" },
            new IndicatorComponentMetadata { Name = "Extra", DisplayName = "Extra", DefaultIsEnabled = false },
        },
    };
}
