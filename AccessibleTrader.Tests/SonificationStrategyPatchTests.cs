using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;
using SoundPatch = AccessibleTrader.Sdk.Models.SoundPatch; // disambiguate from the registry's record

namespace AccessibleTrader.Tests;

/// <summary>
/// DefaultSonificationStrategy.CreateAudioPoint patch resolution — built-in vs user patches,
/// per-colour (green/red) selection, multi-oscillator PatchLayers, cross direction, zone-noise
/// max-combine — plus ResolveComponentVoiceCount slot sizing. Core logic behind per-component sound.
/// </summary>
public class SonificationStrategyPatchTests
{
    private static readonly (double Min, double Max) Range = (0, 100);
    private static readonly Ohlcv BullBar = new(new DateTime(2026, 1, 1), 100, 101, 99, 101, 0); // close >= open
    private static readonly Ohlcv BearBar = new(new DateTime(2026, 1, 1), 100, 101, 99, 99, 0);  // close < open

    private static DefaultSonificationStrategy Strat(FakeLibrary lib) =>
        new(new SoundPatchRegistry(), lib);

    private static ChartSeries Series(ComponentConfig comp, params LevelConfig[] levels)
    {
        var cfg = new SeriesConfig { Id = "s", Name = "s", IndicatorCode = "RSI", Pane = "RSI", Volume = 1f, IsVisible = true };
        cfg.Components.Add(comp);
        foreach (var l in levels) cfg.Levels.Add(l);
        return new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = "s" });
    }

    private static ComponentConfig Comp(string? patchId = null) => new()
    {
        Name = "Value", DisplayName = "Value", IsVisible = true,
        DisplayType = ComponentDisplayType.Line, Waveform = "sine", Volume = 1f, SoundPatchId = patchId,
    };

    private static SoundPatch UserPatch(params string[] waveforms)
    {
        var p = new SoundPatch { Name = "u" };
        foreach (var w in waveforms) p.Oscillators.Add(new OscillatorLayer { Waveform = w, Gain = 1f, FreqRatio = 1.0 });
        return p;
    }

    // ── Patch resolution ─────────────────────────────────────────────────────────

    [Fact]
    public void RegistryPatch_SetsPatchId_AndDoesNotOverrideTimbre()
    {
        var comp = Comp("crystal_bell"); // built-in registry id
        var pt = Strat(new FakeLibrary()).CreateAudioPoint(Series(comp), comp, 50, BullBar, 0, 10, Range, 1f);

        Assert.Equal("crystal_bell", pt.PatchId);
        Assert.Null(pt.PatchLayers);
        Assert.Equal("sine", pt.Waveform); // component waveform preserved; registry drives decay/detune downstream
    }

    [Fact]
    public void UserSingleLayerPatch_OverridesWaveform_NoPatchLayers()
    {
        var lib = new FakeLibrary();
        var patch = UserPatch("square");
        lib.Add(patch);
        var comp = Comp(patch.Id);

        var pt = Strat(lib).CreateAudioPoint(Series(comp), comp, 50, BullBar, 0, 10, Range, 1f);

        Assert.Null(pt.PatchId);        // not a registry patch
        Assert.Null(pt.PatchLayers);    // single layer → carried by the scalar fields
        Assert.Equal("square", pt.Waveform);
    }

    [Fact]
    public void UserMultiLayerPatch_SetsPatchLayers()
    {
        var lib = new FakeLibrary();
        var patch = UserPatch("sine", "triangle", "sawtooth");
        lib.Add(patch);
        var comp = Comp(patch.Id);

        var pt = Strat(lib).CreateAudioPoint(Series(comp), comp, 50, BullBar, 0, 10, Range, 1f);

        Assert.NotNull(pt.PatchLayers);
        Assert.Equal(3, pt.PatchLayers!.Count);
        Assert.Equal("sine", pt.Waveform); // primary layer mirrored onto the scalar field
    }

    // ── Per-colour (green/red) ───────────────────────────────────────────────────

    [Fact]
    public void PerColour_PicksBullishPatchOnUpBar_BearishOnDownBar()
    {
        var lib = new FakeLibrary();
        var bull = UserPatch("square"); var bear = UserPatch("triangle");
        lib.Add(bull); lib.Add(bear);
        var comp = Comp();
        comp.BullishSoundPatchId = bull.Id;
        comp.BearishSoundPatchId = bear.Id;
        var strat = Strat(lib);
        var series = Series(comp);

        Assert.Equal("square",   strat.CreateAudioPoint(series, comp, 50, BullBar, 0, 10, Range, 1f).Waveform);
        Assert.Equal("triangle", strat.CreateAudioPoint(series, comp, 50, BearBar, 0, 10, Range, 1f).Waveform);
    }

    // ── Cross direction ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(68, 72, 1)]   // crossed up through 70
    [InlineData(72, 68, -1)]  // crossed down through 70
    [InlineData(50, 55, 0)]   // no cross
    public void CrossDirection_ReflectsLevelCross(double prev, double val, int expected)
    {
        var comp = Comp();
        comp.TriggerBoundaryClick = true;
        var series = Series(comp, new LevelConfig { Name = "Overbought", Value = 70, PlayEarcon = true, IsVisible = true });

        var pt = Strat(new FakeLibrary()).CreateAudioPoint(series, comp, val, BullBar, 0, 10, Range, 1f, prevVal: prev);

        Assert.Equal(expected, pt.CrossDirection);
    }

    // ── Zone noise combine ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.1f, 0.3f)]  // base below zone → zone wins
    [InlineData(0.5f, 0.5f)]  // base above zone → base kept (max, never reduced)
    public void ZoneNoise_TakesMaxOfBaseAndZone(float baseNoise, float expected)
    {
        var comp = Comp();
        comp.NoiseAmount = baseNoise;
        var series = Series(comp, new LevelConfig
        {
            Name = "Overbought", Value = 70, IsVisible = true, ZoneNoiseAmount = 0.3f, ZoneNoiseType = "pink",
        });

        var pt = Strat(new FakeLibrary()).CreateAudioPoint(series, comp, 75, BullBar, 0, 10, Range, 1f); // val 75 → in OB zone

        Assert.Equal(expected, pt.NoiseAmount);
    }

    // ── Voice-count planning ─────────────────────────────────────────────────────

    [Fact]
    public void ResolveComponentVoiceCount_PlainComponent_IsOne()
        => Assert.Equal(1, Strat(new FakeLibrary()).ResolveComponentVoiceCount(Comp()));

    [Fact]
    public void ResolveComponentVoiceCount_GradientComponent_IsTwo()
    {
        var comp = Comp();
        comp.UsesGradientSpeech = true;
        Assert.Equal(2, Strat(new FakeLibrary()).ResolveComponentVoiceCount(comp));
    }

    [Fact]
    public void ResolveComponentVoiceCount_DetunedRegistryPatch_IsTwo()
        => Assert.Equal(2, Strat(new FakeLibrary()).ResolveComponentVoiceCount(Comp("detuned_pair_bell")));

    [Fact]
    public void ResolveComponentVoiceCount_MultiLayerUserPatch_MatchesLayerCount()
    {
        var lib = new FakeLibrary();
        var patch = UserPatch("sine", "triangle", "square");
        lib.Add(patch);
        Assert.Equal(3, Strat(lib).ResolveComponentVoiceCount(Comp(patch.Id)));
    }

    [Fact]
    public void ResolveComponentVoiceCount_PerColour_UsesLargestLayerCount()
    {
        var lib = new FakeLibrary();
        var two = UserPatch("sine", "triangle"); var three = UserPatch("sine", "triangle", "square");
        lib.Add(two); lib.Add(three);
        var comp = Comp();
        comp.BullishSoundPatchId = two.Id;
        comp.BearishSoundPatchId = three.Id;
        Assert.Equal(3, Strat(lib).ResolveComponentVoiceCount(comp));
    }

    // ── In-memory ISoundPatchLibrary ─────────────────────────────────────────────
    private sealed class FakeLibrary : ISoundPatchLibrary
    {
        private readonly Dictionary<string, SoundPatch> _patches = new();
        public void Add(SoundPatch p) => _patches[p.Id] = p;

        public IReadOnlyList<SoundPatch> GetPatches() => _patches.Values.ToList();
        public void AddPatch(SoundPatch patch) => _patches[patch.Id] = patch;
        public void RemovePatch(string id) => _patches.Remove(id);
        public void UpdatePatch(SoundPatch patch) => _patches[patch.Id] = patch;
        public SoundPatch? GetPatch(string? id) => id != null && _patches.TryGetValue(id, out var v) ? v : null;
        public void SavePatches() { }
        public EarconSettings EarconOverrides { get; } = new();
        public void SaveEarconOverrides() { }
        public void ResetToDefaults() { }
            public string ExportPatchJson(SoundPatch patch) => "";
        public SoundPatch? ImportPatchJson(string json) => null;
    }
}
