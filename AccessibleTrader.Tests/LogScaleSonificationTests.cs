using System.Collections.Immutable;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// The sonification follows the log-scale toggle, for exactly the panes the picture does.
///
/// <para>
/// Until 2026-09-18 the audio path normalised a value linearly across the viewport range
/// whatever Alt+L said; nothing under <c>Services/Audio</c> read <c>IsLogScale</c>. So on a
/// log-scaled 10k–100k chart the price 31,623 was DRAWN at the vertical middle of the pane and
/// SOUNDED a quarter of the way up (392 Hz of a 200–1000 Hz sweep). The chart said "Log scale"
/// when the key was pressed, and the words were true of the pixels only.
/// </para>
///
/// <para>
/// Three layers are pinned, because a helper with excellent tests and a caller free to stop
/// calling it is the shape the A2h campaign named: the normaliser agrees with the pixel mapping;
/// the strategy moves its pitch when told; and both real callers — arrowing onto a bar and
/// playing the chart — tell it. Indicator panes are never on the log scale, on screen or in
/// sound, and that is pinned too.
/// </para>
/// </summary>
public sealed class LogScaleSonificationTests
{
    // The geometric midpoint of 10k–100k: drawn at the vertical middle on a log scale.
    private const double Lo = 10_000, Hi = 100_000, Mid = 31_622.7766;

    // ── The normaliser is the pixel mapping's ─────────────────────────────────

    [Theory]
    [InlineData(Lo)]
    [InlineData(20_000)]
    [InlineData(Mid)]
    [InlineData(75_000)]
    [InlineData(Hi)]
    public void TheEarUsesTheEyesMaths_OnBothScales(double price)
    {
        foreach (bool isLog in new[] { false, true })
        {
            float y = ChartMath.MapY(price, top: 0f, bottom: 1000f, Lo, Hi, isLog);
            double fromPixels = 1.0 - (y / 1000.0);
            double fromAudio = ChartMath.NormalizedPosition(price, Lo, Hi, isLog);
            Assert.True(Math.Abs(fromPixels - fromAudio) < 1e-6,
                $"price {price} on {(isLog ? "log" : "linear")}: pixels say {fromPixels:F4}, audio says {fromAudio:F4}");
        }
    }

    [Fact]
    public void OnLogScaleTheGeometricMidpointSitsHalfwayUp_AndOnLinearItDoesNot()
    {
        Assert.True(Math.Abs(ChartMath.NormalizedPosition(Mid, Lo, Hi, isLogScale: true) - 0.5) < 1e-6);
        Assert.True(Math.Abs(ChartMath.NormalizedPosition(Mid, Lo, Hi, isLogScale: false) - 0.2403) < 1e-3);
    }

    // ── The strategy moves its pitch ──────────────────────────────────────────

    private static ComponentConfig PriceLine() => new()
    {
        Name = "close", DisplayName = "Close", DisplayType = ComponentDisplayType.Line,
        Role = ComponentRole.PriceAction, DataMapping = "close",
        IsVisible = true, IsEnabled = true, Volume = 1f,
        PitchMapping = PitchMapping.Value, BaseFrequency = 440, FreqMultiplier = 1.0,
        AmplitudeMapping = AmplitudeMapping.None, Waveform = "sine", EnvelopeType = "Sustain",
    };

    private static Ohlcv Bar(double close) =>
        new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), close, close + 100, close - 100, close, 1000);

    [Fact]
    public void AValuePitchedComponentFollowsTheLogScale_WhenTold()
    {
        var strategy = new DefaultSonificationStrategy(new SoundPatchRegistry());
        var series = new ChartSeries(new SeriesConfig { Id = "price", Name = "Price", Pane = "Main", IsVisible = true, Volume = 1f },
            new SeriesDataBuffer { SeriesId = "price" });

        var linear = strategy.CreateAudioPoint(series, PriceLine(), Mid, Bar(Mid), 5, 20, (Lo, Hi), 1f, isLogScale: false);
        var log    = strategy.CreateAudioPoint(series, PriceLine(), Mid, Bar(Mid), 5, 20, (Lo, Hi), 1f, isLogScale: true);

        // 200 + n × 800: a quarter of the way up on linear, halfway on log.
        Assert.InRange(linear.Frequency, 390, 395);
        Assert.InRange(log.Frequency, 599, 601);
    }

    [Fact]
    public void TheDefaultIsStillLinear_SoNoCallerChangedByAccident()
    {
        var strategy = new DefaultSonificationStrategy(new SoundPatchRegistry());
        var series = new ChartSeries(new SeriesConfig { Id = "price", Name = "Price", Pane = "Main", IsVisible = true, Volume = 1f },
            new SeriesDataBuffer { SeriesId = "price" });

        var pt = strategy.CreateAudioPoint(series, PriceLine(), Mid, Bar(Mid), 5, 20, (Lo, Hi), 1f);
        Assert.InRange(pt.Frequency, 390, 395);
    }

    // ── Which panes ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("Main", true)]
    [InlineData("main", true)]
    [InlineData("Pane_RSI", false)]
    [InlineData("Volume", false)]
    public void TheToggleReachesTheMainPaneOnly(string? pane, bool expected)
    {
        Assert.Equal(expected, ViewportRangeCalculator.IsLogScaleFor(true, pane));
        Assert.False(ViewportRangeCalculator.IsLogScaleFor(false, pane));
    }

    // ── The callers ───────────────────────────────────────────────────────────

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
        public IEnumerable<VoiceCall> Audible { get { lock (Calls) return Calls.Where(c => c.Volume > 0f).ToList(); } }
    }

    private const int BarCount = 12;

    private static List<Ohlcv> Bars()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, BarCount).Select(i => new Ohlcv(t.AddHours(i), Mid, Mid + 100, Mid - 100, Mid, 1000)).ToList();
    }

    /// <summary>A price line in the Main pane, or the same line filed in an indicator pane.</summary>
    private static ChartSeries LineSeries(string pane)
    {
        var cfg = new SeriesConfig { Id = "price", Name = "Price", FriendlyName = "Price", IndicatorCode = "PRICE",
            Pane = pane, IsVisible = true, Volume = 1f };
        cfg.Components.Add(PriceLine());
        var data = new SeriesDataBuffer { SeriesId = "price" };
        data.ComponentData["close"] = Enumerable.Repeat(Mid, BarCount).ToArray();
        return new ChartSeries(cfg, data);
    }

    private static WorkspaceState State(ChartSeries series, bool isLogScale) => WorkspaceState.Initial with
    {
        Data = new TimeSeriesBuffer<Ohlcv>(Bars()),
        ActiveSeries = ImmutableList.Create(series),
        FocusedSeriesId = series.Id,
        FocusedComponentIndex = 0,
        CurrentDataIndex = BarCount - 1,
        ViewportStartIndex = 0,
        ViewportLength = BarCount,
        ViewportRange = (Lo, Hi),
        PaneRanges = ImmutableDictionary<string, (double Min, double Max)>.Empty
            .Add("Main", (Lo, Hi)).Add("Pane_X", (Lo, Hi)),
        IsLogScale = isLogScale,
        ChartVolume = 1f,
        PlaybackSpeed = 1.0f,
    };

    private static double NavigationPitch(string pane, bool isLogScale)
    {
        var driver = new SpyDriver();
        var sonifier = new NavigationSonifier(driver, new DefaultSonificationStrategy(new SoundPatchRegistry()), new SoundPatchRegistry());
        sonifier.SyncNavigationSlots(State(LineSeries(pane), isLogScale));
        var call = Assert.Single(driver.Audible.GroupBy(c => c.Slot).Select(g => g.Last()));
        return call.Frequency;
    }

    /// <summary>Arrowing onto a bar: the pitch is where the bar is DRAWN.</summary>
    [Fact]
    public void ArrowingOntoABar_PitchFollowsTheToggle_InTheMainPane()
    {
        Assert.InRange(NavigationPitch("Main", isLogScale: false), 390, 395);
        Assert.InRange(NavigationPitch("Main", isLogScale: true), 599, 601);
    }

    [Fact]
    public void ArrowingOntoABar_AnIndicatorPaneIgnoresTheToggle()
    {
        Assert.InRange(NavigationPitch("Pane_X", isLogScale: false), 390, 395);
        Assert.InRange(NavigationPitch("Pane_X", isLogScale: true), 390, 395);
    }

    private static async Task<List<double>> PlaybackPitches(string pane, bool isLogScale)
    {
        var series = LineSeries(pane);
        var store = new MockWorkspaceStore();
        store.EmitState(State(series, isLogScale));
        var driver = new SpyDriver();
        var sequencer = new AudioSequencer(driver, new DefaultSonificationStrategy(new SoundPatchRegistry()),
            store, new SoundPatchRegistry(), NullLogger<AudioSequencer>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await sequencer.StartMultiSeriesPlaybackAsync(new[] { series }, Bars(), BarCount - 1, cts.Token);
        return driver.Audible.Select(c => c.Frequency).ToList();
    }

    /// <summary>Playing the chart: the same rule, through the other caller.</summary>
    [Fact]
    public async Task Playback_PitchFollowsTheToggle_InTheMainPane()
    {
        var linear = await PlaybackPitches("Main", isLogScale: false);
        var log = await PlaybackPitches("Main", isLogScale: true);
        Assert.NotEmpty(linear);
        Assert.NotEmpty(log);
        Assert.All(linear, f => Assert.InRange(f, 390, 395));
        Assert.All(log, f => Assert.InRange(f, 599, 601));
    }

    [Fact]
    public async Task Playback_AnIndicatorPaneIgnoresTheToggle()
    {
        var log = await PlaybackPitches("Pane_X", isLogScale: true);
        Assert.NotEmpty(log);
        Assert.All(log, f => Assert.InRange(f, 390, 395));
    }
}
