using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Immutable;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A voice the user switched off must not be audible — on every render path, not just the one
/// that was tested.</b>
///
/// <para>
/// <see cref="SonificationTimbreTests"/> already states this rule, and states it well, but it
/// states it for <c>ComponentDisplayType.Candle</c> and nothing else. That is not a small gap.
/// Most display types do share one path — <c>DefaultSonificationStrategy.CreateAudioPoint</c>,
/// where mute collapses <c>baseVolume</c> to zero and every amplitude branch multiplies through it
/// — but three do not. Cloud components are rendered by <c>AudioSequencer.PlayCloudComponent</c>,
/// cloud FILLS by <c>FireCloudVoices</c>, and Profile/Heatmap by <c>NavigationSonifier</c>'s own
/// two methods. Each of those wrote its own volume arithmetic, and the rule was only ever asserted
/// against the path that did not need it.
/// </para>
///
/// <para>
/// The two things a mute has to survive here are different in kind. Muting a series or a component
/// is a switch; turning a volume down to zero is a slider that happens to be at its end stop. Both
/// are the user saying "not this one", and a floor that quietly re-raises a zero is the worse of
/// the two failures, because there is no control left to reach for.
/// </para>
/// </summary>
public sealed class MuteIsAbsoluteTests
{
    // ── The shared path: every display type through the strategy ────────────────────

    /// <summary>
    /// Every display type that resolves through <c>CreateAudioPoint</c>. Written as an enumeration
    /// of the enum rather than a hand-picked list, so a display type added later is covered the
    /// day it exists rather than the day somebody remembers this file.
    /// </summary>
    public static TheoryData<ComponentDisplayType> EveryDisplayType()
    {
        var d = new TheoryData<ComponentDisplayType>();
        foreach (var t in Enum.GetValues<ComponentDisplayType>()) d.Add(t);
        return d;
    }

    private static ComponentConfig Component(ComponentDisplayType type, string name = "c")
    {
        var role = type switch
        {
            ComponentDisplayType.Candle => ComponentRole.Body,
            ComponentDisplayType.Wick => ComponentRole.Wick,
            ComponentDisplayType.Histogram => ComponentRole.Histogram,
            ComponentDisplayType.Line => ComponentRole.PriceAction,
            _ => ComponentRole.Signal,
        };
        var p = new SonificationProfileProvider().GetProfile(type, role, name);
        return new ComponentConfig
        {
            Name = name, DisplayName = name, DisplayType = type, Role = role,
            DataMapping = "close", IsVisible = true, IsEnabled = true, Volume = 1f,
            Waveform = p.Waveform,
            AboveReferenceWaveform = p.AboveWaveform,
            BelowReferenceWaveform = p.BelowWaveform,
            AmplitudeMapping = p.AmplitudeMapping,
            PitchMapping = p.PitchMapping,
            BaseFrequency = p.BaseFrequency,
            FreqMultiplier = p.FreqMultiplier,
            BullishFrequency = 440.0, BearishFrequency = 220.0,
            EnvelopeType = p.EnvelopeType,
        };
    }

    private static ChartSeries Series(string id = "candles")
        => new(new SeriesConfig { Id = id, Name = id, IsVisible = true, Volume = 1f },
               new SeriesDataBuffer { SeriesId = id });

    private static AudioPoint Play(ComponentConfig comp, ChartSeries? series = null, float chartVolume = 1f)
        => new DefaultSonificationStrategy(new SoundPatchRegistry())
            .CreateAudioPoint(series ?? Series(), comp, val: 55,
                new Ohlcv(default, 45, 55, 45, 55, 1000),
                relativeIndex: 5, viewportWidth: 20, viewportRange: (0.0, 100.0),
                chartVolume: chartVolume);

    [Theory]
    [MemberData(nameof(EveryDisplayType))]
    public void AMutedComponentIsSilent(ComponentDisplayType type)
    {
        var comp = Component(type);
        Assert.True(Play(comp).Volume > 0f, $"{type} is inaudible even unmuted — this case proves nothing");

        comp.IsMuted = true;
        Assert.Equal(0f, Play(comp).Volume);
    }

    [Theory]
    [MemberData(nameof(EveryDisplayType))]
    public void AHiddenComponentIsSilent(ComponentDisplayType type)
    {
        var comp = Component(type);
        comp.IsVisible = false;
        Assert.Equal(0f, Play(comp).Volume);
    }

    [Theory]
    [MemberData(nameof(EveryDisplayType))]
    public void AComponentTurnedDownToZeroIsSilent(ComponentDisplayType type)
    {
        var comp = Component(type);
        comp.Volume = 0f;
        Assert.Equal(0f, Play(comp).Volume);
    }

    [Theory]
    [MemberData(nameof(EveryDisplayType))]
    public void AMutedSeriesSilencesItsComponents(ComponentDisplayType type)
    {
        var series = Series();
        series.IsMuted = true;
        Assert.Equal(0f, Play(Component(type), series).Volume);
    }

    // ── The dedicated paths ─────────────────────────────────────────────────────────

    private sealed record VoiceCall(int Slot, double Frequency, float Volume);

    private sealed class SpyDriver : IAudioDriver
    {
        public List<VoiceCall> Calls { get; } = new();
        public int SampleRate => 44100;
        public int Channels => 2;
        public event Action<int>? PointReached { add { } remove { } }
        public void SetVoice(int slot, double frequency, float volume, float pan, string waveform,
            bool continuous, double durationSeconds = 0.2, int dataIndex = -1, string envelope = "Sustain",
            bool click = false, float noiseAmount = 0f, string noiseType = "pink", float squareMix = 0f,
            float sawMix = 0f, float triangleMix = 0f, float subSawMix = 0f)
        {
            lock (Calls) Calls.Add(new VoiceCall(slot, frequency, volume));
        }
        public void StopVoice(int slot) { }
        public void StopAll() { }
        public void Reset() { }
        public void SetMasterGain(float gain) { }
        public void Pause() { }
        public void Resume() { }

        /// <summary>Anything that would actually be heard. A zero-volume command is not a sound.</summary>
        public IEnumerable<VoiceCall> Audible => Calls.Where(c => c.Volume > 0f);
    }

    private static ChartSeries CloudSeries(int bars, float compVolume = 1f, bool muted = false, bool visible = true)
    {
        var cfg = new SeriesConfig
        {
            Id = "cloudy", Name = "cloudy", Pane = "Main",
            IsVisible = visible, IsMuted = muted, Volume = 1f,
        };
        var comp = Component(ComponentDisplayType.Cloud, "width");
        comp.Volume = compVolume;
        comp.BullishFrequency = 440;
        comp.BearishFrequency = 220;
        cfg.Components.Add(comp);

        var data = new SeriesDataBuffer { SeriesId = "cloudy" };
        var arr = new double[bars];
        for (int i = 0; i < bars; i++) arr[i] = 10 + i;       // steadily widening, so nothing is near-zero
        data.ComponentData["width"] = arr;

        return new ChartSeries(cfg, data);
    }

    private static List<Ohlcv> Bars(int n)
    {
        var list = new List<Ohlcv>(n);
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < n; i++) list.Add(new Ohlcv(t.AddHours(i), 100, 102, 98, 101, 1000));
        return list;
    }

    /// <summary>Runs one bar of real Chart-scope playback over the given series and returns what was armed.</summary>
    private static async Task<SpyDriver> PlayOneChartBarAsync(IReadOnlyList<ChartSeries> seriesList, float chartVolume = 0.5f)
    {
        const int BarCount = 20;
        var bars = Bars(BarCount);

        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial with
        {
            ActiveSeries = ImmutableList.CreateRange(seriesList),
            ViewportStartIndex = 0,
            ViewportLength = BarCount,
            ViewportRange = (90, 110),
            PaneRanges = ImmutableDictionary<string, (double Min, double Max)>.Empty.Add("Main", (90, 110)),
            ChartVolume = chartVolume,
            PlaybackSpeed = 1.0f,
        });

        var driver = new SpyDriver();
        var sequencer = new AudioSequencer(driver, new DefaultSonificationStrategy(new SoundPatchRegistry()),
            store, new SoundPatchRegistry(), NullLogger<AudioSequencer>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await sequencer.StartMultiSeriesPlaybackAsync(seriesList, bars, BarCount - 1, cts.Token);
        return driver;
    }

    /// <summary>
    /// A Cloud component's volume slider at zero.
    ///
    /// <para>
    /// <c>PlayCloudComponent</c> computed its level as
    /// <c>Math.Clamp(normalized * comp.Volume * chartVolume * series.Volume, 0.05f, 1f)</c>. The
    /// floor is there so a thin cloud stays perceptible, which is right — but it applied to the
    /// user's own zero as well, so a cloud turned all the way down came back at 5%. That is not a
    /// quiet cloud. It is a control that does not work at the one setting the user reaches for
    /// when they want the thing to stop.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ACloudComponentTurnedDownToZeroIsSilent()
    {
        var audible = (await PlayOneChartBarAsync(new[] { CloudSeries(20, compVolume: 0f) })).Audible.ToList();

        Assert.True(audible.Count == 0,
            $"a cloud at volume 0 still sounded at {string.Join(", ", audible.Select(c => c.Volume.ToString("F3")))}");
    }

    /// <summary>The vacuity twin: the same cloud, left alone, must be loud enough to have proved something.</summary>
    [Fact]
    public async Task ACloudComponentLeftAloneIsAudible()
    {
        var audible = (await PlayOneChartBarAsync(new[] { CloudSeries(20) })).Audible.ToList();
        Assert.NotEmpty(audible);
    }

    /// <summary>
    /// The master chart volume at zero has to reach the cloud path too — same floor, same
    /// consequence, and this one silences everything else on the chart while the clouds carry on.
    /// </summary>
    [Fact]
    public async Task ChartVolumeZeroSilencesClouds()
    {
        var audible = (await PlayOneChartBarAsync(new[] { CloudSeries(20) }, chartVolume: 0f)).Audible.ToList();
        Assert.Empty(audible);
    }

    // ── Cloud FILLS: a different code path again ────────────────────────────────────

    private static ChartSeries FillSeries(bool muted = false, bool visible = true)
    {
        const int bars = 20;
        var cfg = new SeriesConfig
        {
            Id = "filled", Name = "filled", Pane = "Main",
            IsVisible = visible, IsMuted = muted, Volume = 1f,
        };
        cfg.Components.Add(Component(ComponentDisplayType.Line, "upper"));
        cfg.Components.Add(Component(ComponentDisplayType.Line, "lower"));
        cfg.CloudFills.Add(new CloudFillConfig
        {
            UpperComponentName = "upper",
            LowerComponentName = "lower",
            IsVisible = true,
            Sonification = new CloudSonificationConfig(
                BullishFrequency: 440, BearishFrequency: 220,
                SoundPatchId: "sine_bell", DecayMs: 80, MaxVolume: 0.8f),
        });

        var data = new SeriesDataBuffer { SeriesId = "filled" };
        var up = new double[bars];
        var low = new double[bars];
        for (int i = 0; i < bars; i++) { up[i] = 100 + i; low[i] = 100 - i; }
        data.ComponentData["upper"] = up;
        data.ComponentData["lower"] = low;

        return new ChartSeries(cfg, data);
    }

    /// <summary>
    /// <c>FireCloudVoices</c> runs as a second pass, outside the voice plan that filters muted and
    /// hidden series — so it consulted <c>fill.IsVisible</c> and nothing else. A muted series went
    /// on sounding its fills, which means muting a series did not mute the series.
    /// </summary>
    [Fact]
    public async Task AMutedSeriesDoesNotSoundItsCloudFills()
    {
        var audible = (await PlayOneChartBarAsync(new[] { FillSeries(muted: true) }))
            .Audible.Where(c => c.Slot >= 96).ToList();

        Assert.True(audible.Count == 0, $"{audible.Count} cloud-fill voices fired for a MUTED series");
    }

    [Fact]
    public async Task AHiddenSeriesDoesNotSoundItsCloudFills()
    {
        var audible = (await PlayOneChartBarAsync(new[] { FillSeries(visible: false) }))
            .Audible.Where(c => c.Slot >= 96).ToList();

        Assert.True(audible.Count == 0, $"{audible.Count} cloud-fill voices fired for a HIDDEN series");
    }

    /// <summary>Vacuity: an ordinary series' fills DO sound, so the two assertions above are about mute.</summary>
    [Fact]
    public async Task AnOrdinarySeriesDoesSoundItsCloudFills()
    {
        var audible = (await PlayOneChartBarAsync(new[] { FillSeries() }))
            .Audible.Where(c => c.Slot >= 96).ToList();

        Assert.NotEmpty(audible);
    }

    /// <summary>
    /// The master chart volume never reached the fill path at all — <c>FireCloudVoices</c> scales
    /// by the fill's own <c>MaxVolume</c> and nothing else. So turning the whole chart down left
    /// the fills exactly where they were, and at zero they were the only thing still playing.
    /// </summary>
    [Fact]
    public async Task ChartVolumeZeroSilencesCloudFills()
    {
        var audible = (await PlayOneChartBarAsync(new[] { FillSeries() }, chartVolume: 0f))
            .Audible.Where(c => c.Slot >= 96).ToList();

        Assert.True(audible.Count == 0, $"{audible.Count} cloud-fill voices fired at chart volume zero");
    }

    // ── Profile and Heatmap: NavigationSonifier's own paths ─────────────────────────

    private static (NavigationSonifier Sonifier, SpyDriver Driver) NavSonifier()
    {
        var driver = new SpyDriver();
        return (new NavigationSonifier(driver, new DefaultSonificationStrategy(new SoundPatchRegistry()),
            new SoundPatchRegistry()), driver);
    }

    private static ChartSeries ProfileSeries()
    {
        var cfg = new SeriesConfig { Id = "vp", Name = "vp", Pane = "Main", IsVisible = true, Volume = 1f };
        cfg.Components.Add(Component(ComponentDisplayType.Profile, "profile"));
        var data = new SeriesDataBuffer { SeriesId = "vp" };
        data.ProfileBins = new List<ProfileBin>
        {
            new() { PriceLow = 100, PriceHigh = 101, TotalVolume = 500, TpoPeriodCount = 0, IsPOC = false, IsValueArea = false },
            new() { PriceLow = 101, PriceHigh = 102, TotalVolume = 900, TpoPeriodCount = 0, IsPOC = false, IsValueArea = false },
            new() { PriceLow = 102, PriceHigh = 103, TotalVolume = 300, TpoPeriodCount = 0, IsPOC = false, IsValueArea = false },
        };
        return new ChartSeries(cfg, data);
    }

    private static ChartSeries HeatmapSeries()
    {
        var cfg = new SeriesConfig { Id = "hm", Name = "hm", Pane = "Main", IsVisible = true, Volume = 1f };
        cfg.Components.Add(Component(ComponentDisplayType.Heatmap, "heat"));
        var data = new SeriesDataBuffer { SeriesId = "hm" };
        data.HeatmapData = new List<List<ProfileBin>>
        {
            new()
            {
                new() { PriceLow = 100, PriceHigh = 101, TotalVolume = 500, TpoPeriodCount = 0, IsPOC = false, IsValueArea = false },
                new() { PriceLow = 101, PriceHigh = 102, TotalVolume = 900, TpoPeriodCount = 0, IsPOC = false, IsValueArea = false },
            },
        };
        return new ChartSeries(cfg, data);
    }

    /// <summary>
    /// A profile has one component and the Properties dialog can mute it. The series-level mute is
    /// gated upstream in <c>SyncNavigationSlots</c>; the component-level one was consulted by
    /// nobody on this path, so muting the only voice a profile has did nothing at all.
    /// </summary>
    [Fact]
    public void AMutedProfileComponentIsSilent()
    {
        var (sonifier, driver) = NavSonifier();
        var series = ProfileSeries();

        sonifier.SonifyProfile(series, binIndex: 1, masterVolume: 1f);
        Assert.NotEmpty(driver.Audible);            // precondition

        driver.Calls.Clear();
        series.Components[0].IsMuted = true;
        sonifier.SonifyProfile(series, binIndex: 1, masterVolume: 1f);

        Assert.Empty(driver.Audible);
    }

    [Fact]
    public void AMutedHeatmapComponentIsSilent()
    {
        var (sonifier, driver) = NavSonifier();
        var series = HeatmapSeries();

        sonifier.SonifyHeatmap(series, dataIndex: 0, binIndex: 1, masterVolume: 1f);
        Assert.NotEmpty(driver.Audible);            // precondition

        driver.Calls.Clear();
        series.Components[0].IsMuted = true;
        sonifier.SonifyHeatmap(series, dataIndex: 0, binIndex: 1, masterVolume: 1f);

        Assert.Empty(driver.Audible);
    }

    /// <summary>
    /// Hiding a component is the other half of the same switch, and hidden has always meant
    /// silent everywhere else.
    /// </summary>
    [Fact]
    public void AHiddenProfileComponentIsSilent()
    {
        var (sonifier, driver) = NavSonifier();
        var series = ProfileSeries();
        series.Components[0].IsVisible = false;

        sonifier.SonifyProfile(series, binIndex: 1, masterVolume: 1f);

        Assert.Empty(driver.Audible);
    }
}
