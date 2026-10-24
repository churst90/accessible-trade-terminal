using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// A PART THAT SHOULD NOT SHOW BY DEFAULT IS LEFT OUT, NOT HIDDEN (Cody, 2026-10-23: audit every
/// indicator; "not added hidden and muted"). A hidden part was still a keyboard stop that spoke and
/// could be narrated; a part left out (<see cref="SeriesConfig.ExcludedComponents"/>) is none of
/// those until Properties → Appearance → Parts includes it, and is still computed, so strategies,
/// and the parts that read it, see it as before.
/// </summary>
public class HiddenPartsAreLeftOutTests
{
    private static IEnumerable<(IIndicatorProvider P, IndicatorMetadata M)> AllIndicators() =>
        IndicatorProviderFixture.AllProviders().SelectMany(p => p.GetIndicators().Select(m => (p, m)));

    [Fact]
    public void No_indicator_ships_a_part_hidden()
    {
        var hidden = AllIndicators()
            .SelectMany(x => x.M.Components.Where(c => !c.IsVisible).Select(c => $"{x.M.Code}.{c.Name}"))
            .ToList();
        Assert.True(hidden.Count == 0,
            "Declare DefaultIsEnabled = false (left out) instead of IsVisible = false: " + string.Join(", ", hidden));
    }

    [Fact]
    public void Every_indicator_includes_at_least_one_part_by_default()
    {
        var empty = AllIndicators()
            .Where(x => x.M.Components.Count > 0 && x.M.Components.All(c => c.DefaultIsEnabled == false))
            .Select(x => x.M.Code).ToList();
        Assert.Empty(empty);
    }

    [Fact]
    public void The_audit_reached_the_parts_that_were_hidden()
    {
        // Vacuity check for the guard above: these were IsVisible = false until 2026-10-23.
        foreach (var (code, part) in new[]
                 {
                     ("CIPHER_B", CipherBProvider.CompRed), ("CIPHER_B", CipherBProvider.CompWT1Anchor),
                     ("ICHIMOKU", IchimokuProvider.CompKumoPolarity),
                     (DbsiStyleProvider.Code, DbsiStyleProvider.CompControl),
                     (PivotLevelsProvider.Code, PivotLevelsProvider.CompR3),
                 })
        {
            var meta = AllIndicators().Select(x => x.M).FirstOrDefault(m => m.Code == code);
            Assert.True(meta != null, code);
            var c = meta!.Components.FirstOrDefault(c => c.Name == part);
            Assert.True(c != null, $"{code}.{part}");
            Assert.Equal(false, c!.DefaultIsEnabled);
        }
    }

    [Fact]
    public void A_new_chart_has_them_left_out_and_shown_once_included()
    {
        var s = Build(null);
        var red = Assert.Single(s.Config.ExcludedComponents, c => c.Name == CipherBProvider.CompRed);
        Assert.True(red.IsVisible);
        Assert.DoesNotContain(s.Components, c => c.Name == CipherBProvider.CompRed);
        Assert.Contains(s.Components, c => c.Name == CipherBProvider.CompBlue);
    }

    // ── A chart saved before 2026-10-23 ──

    [Fact]
    public void Saved_hidden_at_the_default_that_hid_it_comes_back_left_out()
    {
        var s = Build(new() { Saved(CipherBProvider.CompBullDiv, visible: false, against: false) });
        var div = Assert.Single(s.Config.ExcludedComponents, c => c.Name == CipherBProvider.CompBullDiv);
        Assert.True(div.IsVisible);
    }

    [Fact]
    public void Saved_shown_only_because_its_retired_default_showed_it_comes_back_left_out()
    {
        // Before 2026-10-17 Cipher B's red dot was shown by default, and the save had no record of
        // the default beside the flag (RetiredComponentDefault).
        var s = Build(new() { Saved(CipherBProvider.CompRed, visible: true, against: null) });
        Assert.Contains(s.Config.ExcludedComponents, c => c.Name == CipherBProvider.CompRed);
    }

    [Fact]
    public void Saved_shown_by_the_user_stays_included_and_shown()
    {
        var s = Build(new() { Saved(CipherBProvider.CompBullDiv, visible: true, against: false) });
        var div = Assert.Single(s.Components, c => c.Name == CipherBProvider.CompBullDiv);
        Assert.True(div.IsVisible);
    }

    [Fact]
    public void Saved_hidden_but_tinted_by_the_user_stays_included_and_hidden()
    {
        // Its tint shades the price chart while the line is hidden; left out, it would not.
        var saved = Saved(CipherBProvider.CompAnchorPolarity, visible: false, against: false);
        saved.UserBackgroundTint = BackgroundTint.PriceChart;
        var s = Build(new() { saved });
        var anchor = Assert.Single(s.Components, c => c.Name == CipherBProvider.CompAnchorPolarity);
        Assert.False(anchor.IsVisible);
        Assert.Equal(BackgroundTint.PriceChart, anchor.BackgroundTintInEffect());

        // A tint set back to Off is no use of it: left out.
        saved.UserBackgroundTint = BackgroundTint.Off;
        Assert.Contains(Build(new() { saved }).Config.ExcludedComponents, c => c.Name == CipherBProvider.CompAnchorPolarity);
    }

    // ── A chart saved since ──

    [Fact]
    public void Included_by_the_user_then_hidden_with_H_stays_included_and_hidden()
    {
        var s = Build(new() { Saved(CipherBProvider.CompRed, visible: false, against: true, enabled: true) });
        var red = Assert.Single(s.Components, c => c.Name == CipherBProvider.CompRed);
        Assert.False(red.IsVisible);
    }

    [Fact]
    public void Defaults_saved_while_it_was_hidden_leave_it_out_and_shown()
    {
        var prefs = new Prefs(new ComponentPreference
            { Name = CipherBProvider.CompRed, IsVisible = false, IsEnabled = true, ColorHex = "#123456" });
        var s = Build(null, prefs);
        var red = Assert.Single(s.Config.ExcludedComponents, c => c.Name == CipherBProvider.CompRed);
        Assert.True(red.IsVisible);
        Assert.Equal("#123456", red.ColorHex);
    }

    // ── Scaffolding ──

    private static ComponentConfig Saved(string name, bool visible, bool? against, bool enabled = true) =>
        new() { Name = name, IsVisible = visible, ProviderIsVisible = against, IsEnabled = enabled };

    private sealed class Prefs(ComponentPreference pref) : MockIndicatorPreferencesService, IIndicatorPreferencesService
    {
        List<ComponentPreference>? IIndicatorPreferencesService.GetPreferences(string indicatorCode) => new() { pref };
    }

    private static ChartSeries Build(List<ComponentConfig>? overrides, IIndicatorPreferencesService? prefs = null)
    {
        var meta = new CipherBProvider().GetIndicators()[0];
        var factory = new IndicatorModelFactory(
            new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
            prefs ?? new MockIndicatorPreferencesService());
        return factory.CreateSeriesFromMetadata(meta, meta.Name, "Pane_CIPHER_B",
            SeriesManagementService.DefaultParameterList(meta), overrides);
    }
}
