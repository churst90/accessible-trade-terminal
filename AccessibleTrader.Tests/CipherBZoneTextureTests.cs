using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fixtures;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Texture only in the extreme zones, and one soft noise, louder further out.</b>
///
/// <para>Cody, 2026-10-31: "there should only be texturing, i.e. the white noise under the sound,
/// within those extreme zones. In the case of cipher b ... the softer texturing between the 53 and 60
/// and above/below the 60/-60 lines have the louder ... As of right now though, I am hearing the
/// softer white noise all the time ... instead of the louder white noise, we can just amplify the
/// softer noise which isn't so hard on the ears".</para>
///
/// <para>He was right: Cipher B's two waves carried a texture of their own (0.15 pink) on every bar,
/// and their zones switched to WHITE noise (0.30 past ±53, 0.45 past ±60). Nine other components had
/// a texture of their own as well. Now: no indicator component has one; Cipher B's waves are clean
/// inside ±53, 0.25 pink past ±53, 0.45 pink past ±60; every zone is pink. The plain RSI was already
/// clean between 30 and 70 (pinned here).</para>
/// </summary>
public sealed class CipherBZoneTextureTests
{
    private static readonly Ohlcv Bar = new(DateTime.UtcNow, 100, 110, 95, 105, 1000);

    private static IndicatorModelFactory Factory(IIndicatorPreferencesService? prefs = null) => new(
        new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
        prefs ?? new MockIndicatorPreferencesService());

    /// <summary>A new series as the app builds it, with the provider's levels.</summary>
    private static ChartSeries Fresh(IIndicatorProvider provider, string code)
    {
        var meta = provider.GetIndicators().Single(m => m.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        var s = Factory().CreateSeriesFromMetadata(meta, meta.Name, meta.DefaultPane, new List<(string, string)>(), null);
        foreach (var d in provider.GetDefaultLevels(code)) s.Levels.Add(SeriesManagementService.FromDescriptor(d, d.Value));
        return s;
    }

    private static AudioPoint Sound(ChartSeries s, string component, double v) =>
        new DefaultSonificationStrategy(new SoundPatchRegistry()).CreateAudioPoint(
            s, s.Components.Single(c => c.Name == component || c.DisplayName == component), v, Bar, 5, 10, (-100, 100), 1f);

    private static (float, string) Texture(ChartSeries s, string component, double v)
    {
        var p = Sound(s, component, v);
        return (p.NoiseAmount, p.NoiseAmount > 0 ? p.NoiseType : "none");
    }

    // ── Cipher B's waves ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(CipherBProvider.CompWT1)]
    [InlineData(CipherBProvider.CompWT2)]
    public void The_waves_are_clean_inside_53_soft_to_60_and_the_same_noise_louder_past_60(string wave)
    {
        var s = Fresh(new CipherBProvider(), "CIPHER_B");
        foreach (double v in new[] { 0.0, 10, -10, 30, -30, 52.9, -52.9 })
            Assert.Equal((0f, "none"), Texture(s, wave, v));
        foreach (double v in new[] { 54.0, 59.9, -54, -59.9 })
            Assert.Equal((0.25f, "pink"), Texture(s, wave, v));
        foreach (double v in new[] { 61.0, 90, -61, -90 })
            Assert.Equal((0.45f, "pink"), Texture(s, wave, v));
    }

    /// <summary>The other Cipher B parts have no texture anywhere but their own extremes either.</summary>
    [Fact]
    public void No_cipher_b_part_is_textured_in_its_normal_range()
    {
        var s = Fresh(new CipherBProvider(), "CIPHER_B");
        foreach (var c in s.Components.Where(c => c.DisplayType is ComponentDisplayType.Oscillator or ComponentDisplayType.Histogram))
        {
            double inside = c.Name is CipherBProvider.CompMcbRsi or CipherBProvider.CompMcbStoch ? 50 : 10;
            Assert.True(Sound(s, c.Name, inside).NoiseAmount == 0f, $"{c.DisplayName} is textured at {inside}");
        }
    }

    // ── The plain RSI, and the fleet ────────────────────────────────────────────────────

    [Fact]
    public void The_plain_rsi_is_clean_between_30_and_70_and_pink_beyond()
    {
        var s = Fresh(new SkenderBoundedOscillatorProvider(), "Rsi");
        string line = s.Components.Single(c => c.DisplayType == ComponentDisplayType.Oscillator).Name;
        foreach (double v in new[] { 31.0, 50, 69 }) Assert.Equal((0f, "none"), Texture(s, line, v));
        Assert.Equal((0.45f, "pink"), Texture(s, line, 75));
        Assert.Equal((0.45f, "pink"), Texture(s, line, 25));
    }

    /// <summary>
    /// The rule for every indicator: texture comes from a zone, never from the component itself, and
    /// no zone is white noise. (The price line, volume and drawings keep their built-in timbres; those
    /// are not declared by indicators.)
    /// </summary>
    [Fact]
    public void No_indicator_declares_a_texture_of_its_own_or_a_white_zone()
    {
        var own = new List<string>();
        var white = new List<string>();
        int levels = 0;
        foreach (var p in IndicatorProviderFixture.AllProviders())
            foreach (var m in p.GetIndicators())
            {
                foreach (var c in m.Components.SelectMany(c => c.OtherForm is { } f ? new[] { c, f } : new[] { c }))
                    if (c.DefaultNoiseAmount is > 0f) own.Add($"{m.Code}.{c.Name} {c.DefaultNoiseAmount}");
                foreach (var l in p.GetDefaultLevels(m.Code).Where(l => l.ZoneNoiseAmount > 0f))
                {
                    levels++;
                    if (!string.Equals(l.ZoneNoiseType ?? "pink", "pink", StringComparison.OrdinalIgnoreCase))
                        white.Add($"{m.Code}.{l.Name} {l.ZoneNoiseType}");
                }
            }
        Assert.True(levels >= 30, $"only {levels} textured levels found: the scan proves nothing");
        Assert.Empty(own);
        Assert.Empty(white);
    }

    /// <summary>
    /// Pulse's two lines answer to its 70/30 zones: clean between them (they had a brown line tinge on
    /// every bar, and Fast Trigger 0.06 of its own too), the zone's pink past them. A line with no
    /// zones keeps its tinge, as MACD's does.
    /// </summary>
    [Theory]
    [InlineData(PulseProvider.CompWtFast)]
    [InlineData(PulseProvider.CompAnchorSlow)]
    public void Pulses_lines_are_clean_between_70_and_30(string line)
    {
        var s = Fresh(new PulseProvider(), PulseProvider.Code);
        foreach (var v in new[] { 31.0, 50, 69 }) Assert.Equal((0f, "none"), Texture(s, line, v));
        Assert.Equal((0.10f, "pink"), Texture(s, line, 80));
        Assert.Equal((0.10f, "pink"), Texture(s, line, 20));

        var macd = Fresh(new SkenderZeroCrossProvider(), "Macd");
        Assert.Equal((0.05f, "brown"), Texture(macd, "Macd", 0.5));
    }

    /// <summary>
    /// Fear &amp; Greed's line keeps the line tinge: its Extreme Greed / Extreme Fear lines are not
    /// overbought/oversold lines, so their zone texture cannot play and the line has no zone to be
    /// clean outside of (a question in TODO). Its own 0.10 is gone.
    /// </summary>
    [Fact]
    public void Fear_and_greeds_line_keeps_the_line_tinge_until_its_extremes_count_as_zones()
    {
        var s = Fresh(new FearGreedProvider(Substitute.For<ICrossSeriesCache>()), "FEAR_GREED");
        Assert.Equal((0.05f, "brown"), Texture(s, FearGreedProvider.CompSentiment, 50));
        Assert.Equal((0.05f, "brown"), Texture(s, FearGreedProvider.CompSentiment, 90));
    }

    /// <summary>
    /// COT, Crowding Index and Funding Rate have no texture anywhere, past their extreme lines too, and
    /// those lines are not overbought/oversold lines. Cody, 2026-10-31: "no, cot, crowd funding should
    /// not use the texturing or over sold/ob lines". Until then each part had 0.10 of its own on every
    /// bar, and each extreme line declared a zone texture that never played (not an extreme by role).
    /// </summary>
    [Theory]
    [InlineData("COT_POSITIONING", CotPositioningProvider.CompZScore, 0.5, 2.5)]
    [InlineData("CROWDING_INDEX", CrowdingIndexProvider.CompCrowdingScore, 0.5, 3.0)]
    [InlineData("FUNDING_RATE", FundingRateProvider.CompFundingRate, 0.02, 0.08)]
    public void Cot_crowding_and_funding_have_no_texture_and_no_overbought_or_oversold_line(
        string code, string part, double inside, double past)
    {
        var xs = Substitute.For<ICrossSeriesCache>();
        IIndicatorProvider p = code switch
        {
            "COT_POSITIONING" => new CotPositioningProvider(xs),
            "CROWDING_INDEX"  => new CrowdingIndexProvider(xs),
            _                 => new FundingRateProvider(xs),
        };
        Assert.All(p.GetDefaultLevels(code), l => Assert.Equal(0f, l.ZoneNoiseAmount));
        var s = Fresh(p, code);
        Assert.NotEmpty(s.Levels);
        Assert.All(s.Levels, l => Assert.DoesNotContain(l.EffectiveRole, new[] { LevelRole.Overbought, LevelRole.Oversold }));
        foreach (var v in new[] { inside, -inside, past, -past }) Assert.Equal((0f, "none"), Texture(s, part, v));
    }

    // ── Saved charts and saved defaults ────────────────────────────────────────────────

    private static readonly List<IIndicatorProvider> Providers = new() { new CipherBProvider() };
    private static IndicatorEngine Engine() => new(new IndicatorService(Providers, NullLogger<IndicatorService>.Instance),
        new CustomIndicatorRegistry(), Providers);

    private static ChartSeries Restore(SeriesConfig saved, IIndicatorPreferencesService? prefs = null)
    {
        var meta = new CipherBProvider().GetIndicators();
        WorkspaceInitializer.MigrateSeriesConfig(saved, meta);
        prefs ??= new MockIndicatorPreferencesService();
        return SeriesManagementService.MaterializeSaved(saved, meta[0], Factory(prefs),
            Array.Empty<IReadOnlyDictionary<string, object>>(), Engine(), prefs);
    }

    private static LevelConfig L(ChartSeries s, string name) => s.Levels.Single(l => l.Name == name);

    /// <summary>Cody's own saved Cipher B (its levels hold the 0.30 / 0.45 white they were saved with).</summary>
    [Fact]
    public void Codys_saved_chart_opens_clean_inside_53_and_pink_in_both_zones()
    {
        var saved = JsonConvert.DeserializeObject<SeriesConfig>(CodysCipherBSave.Json)!;
        Assert.Contains(saved.Levels, l => l.Name == "Overbought" && Math.Abs(l.ZoneNoiseAmount - 0.30f) < 1e-6 && l.ZoneNoiseType == "white");
        var s = Restore(saved);
        foreach (var n in new[] { "Overbought", "Oversold" }) Assert.Equal((0.25f, "pink"), (L(s, n).ZoneNoiseAmount, L(s, n).ZoneNoiseType));
        foreach (var n in new[] { "Extreme OB", "Extreme OS" }) Assert.Equal((0.45f, "pink"), (L(s, n).ZoneNoiseAmount, L(s, n).ZoneNoiseType));
        Assert.Equal((0f, "none"), Texture(s, CipherBProvider.CompWT1, 20));
        Assert.Equal((0.25f, "pink"), Texture(s, CipherBProvider.CompWT1, -55));
        Assert.Equal((0.45f, "pink"), Texture(s, CipherBProvider.CompWT2, 65));
    }

    /// <summary>A level saved before levels kept a baseline, holding any texture it had as a default.</summary>
    [Theory]
    [InlineData(0.10f)]
    [InlineData(0.30f)]
    public void A_level_saved_without_a_baseline_at_an_old_default_takes_the_new(float old)
    {
        var saved = new SeriesConfig { Id = "cb-1", Name = "Cipher B", IndicatorCode = "CIPHER_B", Pane = "Pane_CIPHER_B" };
        saved.Levels.Add(new LevelConfig { Name = "Overbought", Value = 53, IsVisible = true, PlayEarcon = true, EarconVolume = 0.6f,
            ZoneNoiseAmount = old, ZoneNoiseType = "white", ColorHex = "#8C8C96", DashStyle = DashStyle.Dot });
        var s = Restore(saved);
        Assert.Equal((0.25f, "pink"), (L(s, "Overbought").ZoneNoiseAmount, L(s, "Overbought").ZoneNoiseType));
    }

    [Fact]
    public void A_texture_the_user_chose_on_a_saved_level_stays()
    {
        var saved = JsonConvert.DeserializeObject<SeriesConfig>(CodysCipherBSave.Json)!;
        var ob = saved.Levels.Single(l => l.Name == "Overbought");
        ob.ZoneNoiseAmount = 0.35f;
        var s = Restore(saved);
        // The amount was chosen and stays; the type was not (it is still the saved baseline's white),
        // so it takes the new pink, field by field as every healed level does.
        Assert.Equal((0.35f, "pink"), (L(s, "Overbought").ZoneNoiseAmount, L(s, "Overbought").ZoneNoiseType));

        var bare = new SeriesConfig { Id = "cb-2", Name = "Cipher B", IndicatorCode = "CIPHER_B", Pane = "Pane_CIPHER_B" };
        bare.Levels.Add(new LevelConfig { Name = "Extreme OB", Value = 60, IsVisible = true, PlayEarcon = true, EarconVolume = 0.8f,
            ZoneNoiseAmount = 0.45f, ZoneNoiseType = "brown", ColorHex = "#8C8C96" });
        Assert.Equal("brown", L(Restore(bare), "Extreme OB").ZoneNoiseType);
    }

    /// <summary>
    /// "Save as default" writes every field, so an indicator default saved before 2026-10-31 holds the
    /// waves' old 0.15 and the old white zones whether or not anyone touched them. Those are read as
    /// the old defaults; anything else is the user's.
    /// </summary>
    [Theory]
    [InlineData(CipherBProvider.CompWT1, CipherBProvider.CompWT2)]
    [InlineData(CipherBProvider.CompWT2, CipherBProvider.CompWT1)]
    public void Defaults_saved_with_the_old_textures_take_the_new_and_chosen_ones_stay(string oldWave, string chosenWave)
    {
        var prefs = Substitute.For<IIndicatorPreferencesService>();
        prefs.GetPreferences("CIPHER_B").Returns(new List<ComponentPreference>
        {
            new() { Name = oldWave,    NoiseAmount = 0.15f },
            new() { Name = chosenWave, NoiseAmount = 0.40f },
        });
        prefs.GetLevelPreferences("CIPHER_B").Returns(new List<LevelPreference>
        {
            new() { Name = "Overbought", ZoneNoiseAmount = 0.30f, ZoneNoiseType = "white" },
            new() { Name = "Extreme OB", ZoneNoiseAmount = 0.45f, ZoneNoiseType = "white" },
            new() { Name = "Oversold",   ZoneNoiseAmount = 0.35f, ZoneNoiseType = "white" },
        });
        var s = Restore(new SeriesConfig { Id = "cb-3", Name = "Cipher B", IndicatorCode = "CIPHER_B", Pane = "Pane_CIPHER_B" }, prefs);

        Assert.Equal(0f, s.Components.Single(c => c.Name == oldWave).NoiseAmount);
        Assert.Equal(0.40f, s.Components.Single(c => c.Name == chosenWave).NoiseAmount);
        Assert.Equal((0.25f, "pink"), (L(s, "Overbought").ZoneNoiseAmount, L(s, "Overbought").ZoneNoiseType));
        Assert.Equal((0.45f, "pink"), (L(s, "Extreme OB").ZoneNoiseAmount, L(s, "Extreme OB").ZoneNoiseType));
        Assert.Equal((0.35f, "white"), (L(s, "Oversold").ZoneNoiseAmount, L(s, "Oversold").ZoneNoiseType));
    }
}
