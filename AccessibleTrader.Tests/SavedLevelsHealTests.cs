using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A restored chart's levels follow the indicator, except where the user changed them.</b>
///
/// <para>Cody, 2026-10-17, after the 80/20 push: "ctrl left/right only jumps between midline crosses
/// … the texturing has disappeared and ctrl left/right does not jump to os/ob zones now". His chart
/// was restored from a session saved that afternoon (read from his own file): Cipher B's five levels
/// as saved — ±53 at 0.10, ±60 at 0.20, Zero hidden — and no "Stoch Overbought"/"Stoch Oversold".
/// Saved levels were restored verbatim, so the RSI and Stochastic subscribed to levels the chart did
/// not have, and the louder ±53/±60 texture never arrived. Every test passed, because every test
/// built Cipher B fresh. These restore through the real path (MigrateSeriesConfig then
/// MaterializeSaved), from a save shaped like his.</para>
/// </summary>
public sealed class SavedLevelsHealTests
{
    private static readonly IndicatorModelFactory Factory = new(
        new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
        new MockIndicatorPreferencesService());

    /// <summary>Cody's saved Cipher B levels, field for field (no baseline: saved before it existed).</summary>
    private static List<LevelConfig> CodysSavedLevels() => new()
    {
        new() { Name = "Extreme OB", Value = 60,  IsVisible = true,  PlayEarcon = true,  EarconVolume = 0.8f, ZoneNoiseAmount = 0.2f, ZoneNoiseType = "white", ColorHex = "#8C8C96", DashStyle = DashStyle.Solid },
        new() { Name = "Overbought", Value = 53,  IsVisible = true,  PlayEarcon = true,  EarconVolume = 0.6f, ZoneNoiseAmount = 0.1f, ZoneNoiseType = "white", ColorHex = "#8C8C96", DashStyle = DashStyle.Dot },
        new() { Name = "Zero",       Value = 0,   IsVisible = false, PlayEarcon = false, EarconVolume = 0.7f, ZoneNoiseAmount = 0f,   ZoneNoiseType = "pink",  ColorHex = "#8C8C96", DashStyle = DashStyle.Solid },
        new() { Name = "Oversold",   Value = -53, IsVisible = true,  PlayEarcon = true,  EarconVolume = 0.6f, ZoneNoiseAmount = 0.1f, ZoneNoiseType = "white", ColorHex = "#8C8C96", DashStyle = DashStyle.Dot },
        new() { Name = "Extreme OS", Value = -60, IsVisible = true,  PlayEarcon = true,  EarconVolume = 0.8f, ZoneNoiseAmount = 0.2f, ZoneNoiseType = "white", ColorHex = "#8C8C96", DashStyle = DashStyle.Solid },
    };

    private static ChartSeries Restore(IEnumerable<LevelConfig> savedLevels)
    {
        var providers = new List<IIndicatorProvider> { new CipherBProvider() };
        var engine = new IndicatorEngine(new IndicatorService(providers, NullLogger<IndicatorService>.Instance),
            new CustomIndicatorRegistry(), providers);
        var meta = new CipherBProvider().GetIndicators();
        var saved = new SeriesConfig { Id = "cb-1", Name = "Cipher B", IndicatorCode = "CIPHER_B", Pane = "Pane_CIPHER_B" };
        foreach (var l in savedLevels) saved.Levels.Add(l);
        WorkspaceInitializer.MigrateSeriesConfig(saved, meta);
        return SeriesManagementService.MaterializeSaved(saved, meta[0], Factory,
            Array.Empty<IReadOnlyDictionary<string, object>>(), engine, new MockIndicatorPreferencesService());
    }

    private static LevelConfig L(ChartSeries s, string name) => s.Levels.Single(l => l.Name == name);

    [Fact]
    public void Codys_saved_chart_gains_the_80_20_and_the_louder_texture_and_keeps_his_hidden_zero()
    {
        var s = Restore(CodysSavedLevels());

        Assert.False(L(s, "Stoch Overbought").IsDrawn);
        Assert.Equal(80, L(s, "Stoch Overbought").Value);
        Assert.Equal(20, L(s, "Stoch Oversold").Value);
        Assert.Equal(0.25f, L(s, "Overbought").ZoneNoiseAmount);   // 0.30 until 2026-10-31
        Assert.Equal(0.25f, L(s, "Oversold").ZoneNoiseAmount);
        Assert.Equal(0.45f, L(s, "Extreme OB").ZoneNoiseAmount);
        Assert.Equal("pink", L(s, "Extreme OB").ZoneNoiseType);   // white until 2026-10-31
        Assert.False(L(s, "Zero").IsVisible);                      // his choice stands
        Assert.All(s.Levels, l => Assert.NotNull(l.ProviderDefaults));
    }

    /// <summary>The restored RSI is textured at its 80 again — the symptom, end to end.</summary>
    [Fact]
    public void On_the_restored_chart_the_rsi_is_textured_past_80()
    {
        var s = Restore(CodysSavedLevels());
        var rsi = s.Components.Single(c => c.DisplayName == "RSI");
        var bar = new Ohlcv(DateTime.UtcNow, 100, 110, 95, 105, 1);
        var strategy = new DefaultSonificationStrategy(new SoundPatchRegistry());
        float clean = strategy.CreateAudioPoint(s, rsi, 50, bar, 5, 10, (-100, 100), 1f).NoiseAmount;
        Assert.True(strategy.CreateAudioPoint(s, rsi, 85, bar, 5, 10, (-100, 100), 1f).NoiseAmount > clean);
    }

    /// <summary>What the user changed is theirs: a level they moved, a texture they set.</summary>
    [Fact]
    public void A_users_own_edits_survive_the_heal()
    {
        var withBaseline = CodysSavedLevels();
        var ob = withBaseline.Single(l => l.Name == "Overbought");
        ob.ProviderDefaults = new LevelDefaults { Value = 53, ZoneNoiseAmount = 0.30f, ColorHex = "#8C8C96", Dash = DashStyle.Dot,
            PlayEarcon = true, EarconVolume = 0.6f, ZoneNoiseType = "white", LabelsKnown = true };
        ob.Value = 55;                 // moved by the user
        ob.ZoneNoiseAmount = 0.20f;    // set by the user
        var s = Restore(withBaseline);
        Assert.Equal(55, L(s, "Overbought").Value);
        Assert.Equal(0.20f, L(s, "Overbought").ZoneNoiseAmount);

        // No baseline (an old save) and a value that was never a default: also the user's.
        var legacy = CodysSavedLevels();
        legacy.Single(l => l.Name == "Extreme OB").ZoneNoiseAmount = 0.33f;
        Assert.Equal(0.33f, L(Restore(legacy), "Extreme OB").ZoneNoiseAmount);
    }

    /// <summary>
    /// Ctrl+Right on the restored chart's RSI lands on its 80 crossing, not on the 50 midline it
    /// crosses first — the jump Cody lost.
    /// </summary>
    [Fact]
    public void Ctrl_right_on_the_restored_rsi_jumps_to_its_80_not_its_midline()
    {
        const int n = 30;
        var s = Restore(CodysSavedLevels());
        int rsiIndex = s.Components.FindIndex(c => c.DisplayName == "RSI");
        var rsiName = s.Components[rsiIndex].Name;
        foreach (var c in s.Components) s.Data.ComponentData[c.Name] = Enumerable.Repeat(double.NaN, n).ToArray();
        // 40 until bar 5, 60 until bar 12 (crosses 50 at 5), then 85 (crosses 80 at 12).
        s.Data.ComponentData[rsiName] = Enumerable.Range(0, n).Select(i => i < 5 ? 40.0 : i < 12 ? 60.0 : 85.0).ToArray();

        var bus = new SpyEventBus();
        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, n).Select(i => new Ohlcv(
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i), 100, 101, 99, 100, 10)).ToList()),
            ActiveSeries = ImmutableList.Create(s),
            PrimarySeriesId = s.Id, FocusedSeriesId = s.Id, FocusedComponentIndex = rsiIndex,
            CurrentDataIndex = 1, ViewportStartIndex = 0, ViewportLength = n,
        });
        new IndicatorCrossingEngine(store, bus).HandleCrossJump(SystemCommand.NavRightJump);
        Assert.Equal(12, store.DispatchedActions.OfType<NavigateAction>().LastOrDefault()?.NewIndex);
    }

    /// <summary>
    /// The last-resort midline jump honours a hidden midline, as every level-based jump already did
    /// (Cody, 2026-09-12 and again 2026-10-17): an oscillator with no other line to aim at and its
    /// zero hidden does not land on zero crossings.
    /// </summary>
    [Fact]
    public void A_hidden_midline_is_not_a_target_even_for_the_fallback()
    {
        const int n = 20;
        var cfg = new SeriesConfig { Id = "osc", Name = "Osc", FriendlyName = "Osc", IndicatorCode = "OSC", Pane = "Pane_OSC" };
        cfg.Components.Add(new ComponentConfig { Name = "Line", DisplayName = "Line", DisplayType = ComponentDisplayType.Oscillator, IsVisible = true, ReferenceLevel = 0 });
        cfg.Levels.Add(new LevelConfig { Name = "Zero", Value = 0, IsVisible = false });
        var buf = new SeriesDataBuffer { SeriesId = "osc" };
        buf.ComponentData["Line"] = Enumerable.Range(0, n).Select(i => i < 8 ? -5.0 : 5.0).ToArray();
        var s = new ChartSeries(cfg, buf);

        var bus = new SpyEventBus();
        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, n).Select(i => new Ohlcv(
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i), 100, 101, 99, 100, 10)).ToList()),
            ActiveSeries = ImmutableList.Create(s),
            PrimarySeriesId = s.Id, FocusedSeriesId = s.Id, FocusedComponentIndex = 0,
            CurrentDataIndex = 1, ViewportStartIndex = 0, ViewportLength = n,
        });
        new IndicatorCrossingEngine(store, bus).HandleCrossJump(SystemCommand.NavRightJump);
        Assert.NotEqual(8, store.DispatchedActions.OfType<NavigateAction>().LastOrDefault()?.NewIndex);
    }
}
