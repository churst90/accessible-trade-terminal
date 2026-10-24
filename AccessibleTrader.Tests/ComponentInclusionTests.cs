using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// Properties → Parts → Include (Cody, 2026-10-23: "those components just won't be added"). A part
/// left out is moved out of <see cref="SeriesConfig.Components"/> into
/// <see cref="SeriesConfig.ExcludedComponents"/>, so nothing that draws, sounds, speaks, narrates or
/// navigates meets it; it is saved with its settings and comes back in its own place.
/// </summary>
public class ComponentInclusionTests
{
    private static IndicatorMetadata Meta() => new CipherAProvider().GetIndicators()[0];
    private static List<string> Order() => Meta().Components.Select(c => c.Name).ToList();

    private static IndicatorModelFactory Factory(IIndicatorPreferencesService? prefs = null) => new(
        new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
        prefs ?? new MockIndicatorPreferencesService());

    private static ChartSeries Fresh(IIndicatorPreferencesService? prefs = null) =>
        Factory(prefs).CreateSeriesFromMetadata(Meta(), "Cipher A", "Main", new(), null);

    private static List<string> Names(IEnumerable<ComponentConfig> cs) => cs.Select(c => c.Name).ToList();

    [Fact]
    public void A_part_the_indicator_leaves_out_by_default_is_not_among_the_components()
    {
        var s = Fresh();
        Assert.DoesNotContain(CipherAProvider.CompRibbon9, Names(s.Components));
        Assert.Equal(new[] { CipherAProvider.CompRibbon9, CipherAProvider.CompPivotHigh, CipherAProvider.CompPivotLow },
            Names(s.Config.ExcludedComponents));
        Assert.All(s.Config.ExcludedComponents, c => Assert.False(c.IsEnabled));
        Assert.All(s.Components, c => Assert.True(c.IsEnabled));
    }

    [Fact]
    public void An_included_part_goes_back_to_its_own_place_and_a_left_out_one_leaves()
    {
        var cfg = Fresh().Config;
        var order = Order();

        Assert.True(ComponentInclusion.SetIncluded(cfg, CipherAProvider.CompPivotLow, true, order));
        Assert.True(ComponentInclusion.SetIncluded(cfg, CipherAProvider.CompRibbon9, true, order));
        Assert.True(ComponentInclusion.SetIncluded(cfg, CipherAProvider.CompGreenDot, false, order));
        Assert.Equal(order.Where(n => n != CipherAProvider.CompGreenDot && n != CipherAProvider.CompPivotHigh), Names(cfg.Components));
        Assert.False(cfg.ExcludedComponents.Single(c => c.Name == CipherAProvider.CompGreenDot).IsEnabled);

        // Back in, first again — not appended at the end.
        Assert.True(ComponentInclusion.SetIncluded(cfg, CipherAProvider.CompGreenDot, true, order));
        Assert.Equal(CipherAProvider.CompGreenDot, cfg.Components[0].Name);
        Assert.Equal(order, Names(ComponentInclusion.AllParts(cfg, order)));
    }

    [Fact]
    public void The_last_included_part_cannot_be_left_out()
    {
        var cfg = Fresh().Config;
        var order = Order();
        foreach (var n in Names(cfg.Components).Skip(1)) Assert.True(ComponentInclusion.SetIncluded(cfg, n, false, order));
        Assert.Single(cfg.Components);
        Assert.False(ComponentInclusion.SetIncluded(cfg, cfg.Components[0].Name, false, order));
        Assert.Single(cfg.Components);
        Assert.False(ComponentInclusion.SetIncluded(cfg, "No Such Part", true, order));
    }

    /// <summary>A saved chart keeps its own choices: a symbol left out stays out, a part included stays in,
    /// in its place, with the settings it was saved with.</summary>
    [Fact]
    public void Choices_survive_a_restore()
    {
        var saved = Fresh().Config;
        var order = Order();
        ComponentInclusion.SetIncluded(saved, CipherAProvider.CompRibbon9, true, order);
        ComponentInclusion.SetIncluded(saved, CipherAProvider.CompYellowDiamond, false, order);
        saved.ExcludedComponents.Single(c => c.Name == CipherAProvider.CompYellowDiamond).IsMuted = true;

        var restored = Restore(saved.Clone());
        Assert.Contains(CipherAProvider.CompRibbon9, Names(restored.Components));
        Assert.Equal(order.IndexOf(CipherAProvider.CompRibbon9) - 1, Names(restored.Components).IndexOf(CipherAProvider.CompRibbon9));
        Assert.DoesNotContain(CipherAProvider.CompYellowDiamond, Names(restored.Components));
        Assert.True(restored.Config.ExcludedComponents.Single(c => c.Name == CipherAProvider.CompYellowDiamond).IsMuted);
    }

    /// <summary>A chart saved before Ribbon 9 existed gets it left out, as a new chart does.</summary>
    [Fact]
    public void A_chart_saved_before_a_part_existed_gets_its_default()
    {
        var saved = Fresh().Config;
        saved.ExcludedComponents.Clear();
        var restored = Restore(saved);
        Assert.Contains(CipherAProvider.CompRibbon9, Names(restored.Config.ExcludedComponents));
        Assert.DoesNotContain(CipherAProvider.CompRibbon9, Names(restored.Components));
    }

    /// <summary>"Save as Defaults" carries Include: new charts follow it, a chart's own saved choice wins.</summary>
    [Fact]
    public void Include_saved_as_the_indicators_default_reaches_new_charts_only()
    {
        var prefs = new Prefs(new ComponentPreference { Name = CipherAProvider.CompRibbon9, IsEnabled = true },
                              new ComponentPreference { Name = CipherAProvider.CompRedX, IsEnabled = false });
        var s = Fresh(prefs);
        Assert.Contains(CipherAProvider.CompRibbon9, Names(s.Components));
        Assert.Contains(CipherAProvider.CompRedX, Names(s.Config.ExcludedComponents));

        var restored = Restore(Fresh().Config, prefs);   // saved with the defaults: Ribbon 9 out, Red X in
        Assert.Contains(CipherAProvider.CompRibbon9, Names(restored.Config.ExcludedComponents));
        Assert.Contains(CipherAProvider.CompRedX, Names(restored.Components));
    }

    /// <summary>The shaded band goes with its edge: leave out EMA 33 and the EMA 5–33 band is not drawn.</summary>
    [Fact]
    public void A_fill_whose_edge_is_left_out_is_not_drawn()
    {
        var cfg = Fresh().Config;
        var wide = cfg.CloudFills.Single(f => f.LowerComponentName == "EMA 33");
        var fast = cfg.CloudFills.Single(f => f.LowerComponentName == "EMA 11");
        Assert.True(DataLayer.DrawsFill(cfg, wide));
        ComponentInclusion.SetIncluded(cfg, "EMA 33", false, Order());
        Assert.False(DataLayer.DrawsFill(cfg, wide));
        Assert.True(DataLayer.DrawsFill(cfg, fast));
    }

    private static ChartSeries Restore(SeriesConfig saved, IIndicatorPreferencesService? prefs = null)
    {
        var meta = new CipherAProvider().GetIndicators();
        WorkspaceInitializer.MigrateSeriesConfig(saved, meta);
        var providers = new List<IIndicatorProvider> { new CipherAProvider() };
        var engine = new IndicatorEngine(new IndicatorService(providers, Microsoft.Extensions.Logging.Abstractions.NullLogger<IndicatorService>.Instance), new CustomIndicatorRegistry(), providers);
        return SeriesManagementService.MaterializeSaved(saved, meta[0], Factory(prefs),
            Array.Empty<IReadOnlyDictionary<string, object>>(), engine, prefs ?? new MockIndicatorPreferencesService());
    }

    private sealed class Prefs(params ComponentPreference[] prefs) : IIndicatorPreferencesService
    {
        public List<ComponentPreference>? GetPreferences(string indicatorCode) => indicatorCode == "CIPHER_A" ? prefs.ToList() : null;
        public void SavePreferences(string indicatorCode, List<ComponentPreference> p) { }
        public void ClearPreferences(string indicatorCode) { }
        public List<LevelPreference> GetLevelPreferences(string indicatorCode) => new();
        public void SaveLevelPreference(string indicatorCode, LevelPreference pref) { }
        public void ClearAllPreferences() { }
    }
}
