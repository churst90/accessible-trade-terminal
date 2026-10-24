using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fixtures;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Cipher B shows what stock Market Cipher B shows, on new charts AND on Cody's saved one
/// (2026-10-17).</b>
///
/// <para>Cody: "finish disabling the elements on cipher b to make it similar to stock market
/// cipher's config". The red Overbought Crossover, the gold Triple Confluence Buy, the four
/// divergences and the Anchor Fill appear on none of the five dated MCB charts; they were hidden by
/// default, still computed and still keyboard stops. Since 2026-10-23 the parts among them are LEFT
/// OUT by default (Cody: "not added hidden and muted"; <see cref="HiddenPartsAreLeftOutTests"/>):
/// still computed, but not keyboard stops until Properties → Parts includes them. Visibility is saved per chart, and until now a
/// restore put the saved flag back verbatim — so a default change reached only charts added
/// afterwards, and Cody's chart (saved while they were shown by default) would have kept them, and
/// their sounds. These restore HIS save (<see cref="CodysCipherBSave"/>) through the real path,
/// <see cref="WorkspaceInitializer.MigrateSeriesConfig"/> then
/// <see cref="SeriesManagementService.MaterializeSaved"/>.</para>
/// </summary>
public sealed class CipherBStockVisibilityTests
{
    private static readonly IndicatorModelFactory Factory = new(
        new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
        new MockIndicatorPreferencesService());

    /// <summary>Ours, not Market Cipher B's: hidden by default 2026-10-17, left out by default
    /// 2026-10-23. The gold dot was here on 2026-10-17, shown again 2026-10-18 and removed from
    /// Cipher B 2026-10-22 (CipherBGoldDotTests).</summary>
    private static readonly string[] LeftOutByDefault =
    {
        CipherBProvider.CompWT1Anchor, CipherBProvider.CompWT2Anchor, CipherBProvider.CompAnchorPolarity,
        CipherBProvider.CompRed,
        CipherBProvider.CompBullDiv, CipherBProvider.CompBearDiv, CipherBProvider.CompHidBull, CipherBProvider.CompHidBear,
    };

    /// <summary>The five whose default changed on 2026-10-17 and stayed changed (the anchors were
    /// always hidden; gold was hidden that day and shown again the next).</summary>
    private static readonly string[] NewlyHidden =
    {
        CipherBProvider.CompRed,
        CipherBProvider.CompBullDiv, CipherBProvider.CompBearDiv, CipherBProvider.CompHidBull, CipherBProvider.CompHidBear,
    };

    /// <summary>Every part, in the indicator's order — Properties → Parts, and the order
    /// Ctrl+Up/Down walks the included ones in.</summary>
    private static readonly string[] AllParts =
    {
        "Anchor Wave", "Anchor Wave 2", "WT Histogram", "Wave Trend", "Wave Trend 2", "Money Flow Wave",
        "WaveTrend Cross Bull", "WaveTrend Cross Bear", "Anchor Polarity",
        "Oversold Crossover", "Overbought Crossover", "Bullish Divergence",
        "Bearish Divergence", "Hidden Bull Continuation", "Hidden Bear Continuation", "RSI", "Stochastic",
    };

    /// <summary>The keyboard stops of a chart with the defaults: every part but the left-out ones.</summary>
    private static readonly string[] KeyboardOrder = AllParts.Except(LeftOutByDefault).ToArray();

    private static List<string> LeftOut(ChartSeries s) => s.Config.ExcludedComponents.Select(c => c.Name).ToList();

    private static void Include(ChartSeries s, string name) =>
        Assert.True(ComponentInclusion.SetIncluded(s.Config, name, true, AllParts));

    private static (IndicatorEngine Engine, List<IndicatorMetadata> Meta) Cipher()
    {
        var providers = new List<IIndicatorProvider> { new CipherBProvider() };
        var engine = new IndicatorEngine(new IndicatorService(providers, NullLogger<IndicatorService>.Instance),
            new CustomIndicatorRegistry(), providers);
        return (engine, new CipherBProvider().GetIndicators());
    }

    private static ChartSeries Fresh()
    {
        var (_, meta) = Cipher();
        return Factory.CreateSeriesFromMetadata(meta[0], "Cipher B", "Pane_CIPHER_B",
            new List<(string, string)>(), componentOverrides: null);
    }

    /// <summary>A saved series through the restore path a resumed session takes.</summary>
    private static ChartSeries Restore(SeriesConfig saved)
    {
        var (engine, meta) = Cipher();
        WorkspaceInitializer.MigrateSeriesConfig(saved, meta);
        return SeriesManagementService.MaterializeSaved(saved, meta[0], Factory,
            Array.Empty<IReadOnlyDictionary<string, object>>(), engine, new MockIndicatorPreferencesService());
    }

    private static SeriesConfig CodysSave() => JsonConvert.DeserializeObject<SeriesConfig>(CodysCipherBSave.Json)!;

    /// <summary>What a session save writes and the next launch reads: the restored config on disk.</summary>
    private static SeriesConfig SaveAgain(ChartSeries s) =>
        JsonConvert.DeserializeObject<SeriesConfig>(JsonConvert.SerializeObject(s.Config))!;

    private static ComponentConfig C(ChartSeries s, string name) => s.Components.Single(c => c.Name == name);
    private static CloudFillConfig Fill(ChartSeries s, string name) => s.Config.CloudFills.Single(f => f.DisplayName == name);

    private static IEnumerable<string> Visible(ChartSeries s) => s.Components.Where(c => c.IsVisible).Select(c => c.Name);

    // ── A new chart ──────────────────────────────────────────────────────────────────

    [Fact]
    public void A_new_Cipher_B_shows_only_what_Market_Cipher_B_shows_and_keeps_every_stop_in_order()
    {
        var s = Fresh();
        Assert.Equal(KeyboardOrder, s.Components.Select(c => c.Name));
        Assert.Equal(LeftOutByDefault, LeftOut(s));
        Assert.All(s.Components.Concat(s.Config.ExcludedComponents), c => Assert.True(c.IsVisible));
        Assert.Equal(AllParts, ComponentInclusion.AllParts(s.Config, AllParts).Select(c => c.Name));
        Assert.False(Fill(s, "Anchor Fill").IsVisible);
        Assert.False(Fill(s, "WT Fill").IsVisible);
        Assert.True(s.Config.ZoneBands.Single(b => b.DisplayName == "Money Flow Bar").IsVisible);
        // The baseline a later restore needs is recorded from the first build.
        Assert.All(s.Components.Concat(s.Config.ExcludedComponents), c => Assert.Equal(c.IsVisible, c.ProviderIsVisible));
        Assert.All(s.Config.CloudFills, f => Assert.Equal(f.IsVisible, f.ProviderIsVisible));
    }

    /// <summary>Left out is not removed: every left-out signal is still computed (strategies, and a
    /// part the user includes, read it); it is not a component the chart sounds; and once included it
    /// sounds like any shown dot.</summary>
    [Fact]
    public void The_left_out_signals_are_still_computed_and_sound_only_once_included()
    {
        var bars = CausalityProbeSeries.Bars(CausalityProbeSeries.HourlyFlavours.First(), 1400);
        var results = new Dictionary<string, double[]>();
        new CipherBProvider().Calculate("CIPHER_B", bars.ToArray(), new Dictionary<string, object>(),
            new IndicatorResultBuffer(results, bars.Count));
        foreach (var name in NewlyHidden)
            Assert.True(results[name].Any(v => !double.IsNaN(v)), $"{name} fired nowhere on 1400 bars — the check would be vacuous.");

        var s = Fresh();
        Assert.All(NewlyHidden, n => Assert.DoesNotContain(n, s.Components.Select(c => c.Name)));
        var strategy = new DefaultSonificationStrategy(new SoundPatchRegistry());
        var bar = bars[^1];
        foreach (var name in NewlyHidden)
        {
            Include(s, name);
            Assert.True(strategy.CreateAudioPoint(s, C(s, name), -40, bar, 5, 10, (-100, 100), 1f).Volume > 0f, name);
        }
    }

    // ── Cody's saved chart ───────────────────────────────────────────────────────────

    [Fact]
    public void Codys_saved_chart_takes_the_new_defaults()
    {
        var save = CodysSave();
        // The fixture really is the old shape: the six (gold among them) shown, the Anchor Fill
        // shown, no baseline.
        Assert.All(NewlyHidden.Append("Triple Confluence Buy"), n => Assert.True(save.Components.Single(c => c.Name == n).IsVisible));
        Assert.True(save.CloudFills.Single(f => f.DisplayName == "Anchor Fill").IsVisible);
        Assert.All(save.Components, c => Assert.Null(c.ProviderIsVisible));

        var s = Restore(save);

        Assert.Equal(KeyboardOrder, s.Components.Select(c => c.Name));
        Assert.Equal(LeftOutByDefault, LeftOut(s));
        Assert.All(s.Components, c => Assert.True(c.IsVisible));
        Assert.False(Fill(s, "Anchor Fill").IsVisible);
        Assert.False(Fill(s, "WT Fill").IsVisible);
        // His own switches are untouched: RSI and Stochastic muted as he left them.
        Assert.True(C(s, CipherBProvider.CompMcbRsi).IsMuted);
        Assert.True(C(s, CipherBProvider.CompMcbStoch).IsMuted);
        // And the next save records the baseline, so it no longer leans on the retired table.
        var next = SaveAgain(s);
        Assert.All(next.Components.Concat(next.ExcludedComponents), c => Assert.NotNull(c.ProviderIsVisible));
        Assert.All(next.CloudFills, f => Assert.NotNull(f.ProviderIsVisible));
    }

    /// <summary>Restoring the restored chart changes nothing — the heal is not a toggle.</summary>
    [Fact]
    public void A_second_restore_is_stable()
    {
        var once = Restore(CodysSave());
        var twice = Restore(SaveAgain(once));
        Assert.Equal(Visible(once), Visible(twice));
        Assert.Equal(LeftOut(once), LeftOut(twice));
        Assert.Equal(once.Config.CloudFills.Select(f => f.IsVisible), twice.Config.CloudFills.Select(f => f.IsVisible));
    }

    // ── The user's own choices are kept ──────────────────────────────────────────────

    /// <summary>
    /// After the restore, Cody includes the bullish divergence again (Properties → Parts) and hides
    /// the cross-down dots; shows the Anchor Fill and hides the money flow. All four survive the next
    /// launch.
    /// </summary>
    [Fact]
    public void What_the_user_shows_or_hides_after_the_change_survives_a_restart()
    {
        var s = Restore(CodysSave());
        Include(s, CipherBProvider.CompBullDiv);
        C(s, CipherBProvider.CompCrossBear).IsVisible = false;
        C(s, CipherBProvider.CompMoneyFlowWave).IsVisible = false;
        Fill(s, "Anchor Fill").IsVisible = true;

        var back = Restore(SaveAgain(s));

        Assert.True(C(back, CipherBProvider.CompBullDiv).IsVisible);
        Assert.False(C(back, CipherBProvider.CompCrossBear).IsVisible);
        Assert.False(C(back, CipherBProvider.CompMoneyFlowWave).IsVisible);
        Assert.True(Fill(back, "Anchor Fill").IsVisible);
        Assert.Contains(CipherBProvider.CompBearDiv, LeftOut(back));   // untouched: still the default
    }

    /// <summary>
    /// In a save from BEFORE the baseline existed, a flag that differs from the old default was the
    /// user's: a divergence he had hidden is left out (as the new default, nothing to see); Anchor
    /// Polarity, hidden by default until 2026-10-23, shown by him stays included and shown; and a
    /// component whose default never changed keeps whatever he chose: the cross-up dots hidden.
    /// </summary>
    [Fact]
    public void In_an_old_save_a_flag_that_was_not_the_old_default_is_the_users()
    {
        var save = CodysSave();
        save.Components.Single(c => c.Name == CipherBProvider.CompAnchorPolarity).IsVisible = true;
        save.Components.Single(c => c.Name == CipherBProvider.CompCrossBull).IsVisible = false;
        save.Components.Single(c => c.Name == CipherBProvider.CompHidBear).IsVisible = false;
        save.CloudFills.Single(f => f.DisplayName == "WT Fill").IsVisible = true;
        save.CloudFills.Single(f => f.DisplayName == "Anchor Fill").IsVisible = false;

        var s = Restore(save);

        Assert.True(C(s, CipherBProvider.CompAnchorPolarity).IsVisible);
        Assert.False(C(s, CipherBProvider.CompCrossBull).IsVisible);
        Assert.Contains(CipherBProvider.CompHidBear, LeftOut(s));
        // WT Fill shown in an old save equals the OLD default (shown until 2026-10-17), so it follows
        // the indicator — hidden. The retired table can only know defaults, not intent.
        Assert.False(Fill(s, "WT Fill").IsVisible);
        Assert.False(Fill(s, "Anchor Fill").IsVisible);
    }

    /// <summary>
    /// The rule is the indicator's, not Cipher B's: any provider that later changes a default
    /// visibility reaches charts saved with the baseline. A component saved hidden against a
    /// hidden default becomes visible when the provider shows it by default; one the user hid
    /// against a visible default stays hidden.
    /// </summary>
    [Fact]
    public void A_provider_that_shows_a_component_by_default_reaches_saved_charts_too()
    {
        var meta = new IndicatorMetadata
        {
            Code = "VISDEMO", Name = "Vis Demo", DefaultPane = "Oscillator",
            Components =
            {
                new IndicatorComponentMetadata { Name = "Was hidden", IsVisible = true },
                new IndicatorComponentMetadata { Name = "User hid", IsVisible = true },
            },
        };
        var saved = new SeriesConfig { Id = "v-1", IndicatorCode = "VISDEMO", Pane = "Oscillator" };
        saved.Components.Add(new ComponentConfig { Name = "Was hidden", IsVisible = false, ProviderIsVisible = false });
        saved.Components.Add(new ComponentConfig { Name = "User hid", IsVisible = false, ProviderIsVisible = true });

        var s = Factory.CreateSeriesFromMetadata(meta, "Vis Demo", "Oscillator", new(), saved.Components.ToList(), "v-1");

        Assert.True(C(s, "Was hidden").IsVisible);
        Assert.False(C(s, "User hid").IsVisible);
    }
}
