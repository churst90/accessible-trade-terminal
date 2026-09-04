using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// F3 silences the tones. It does not stop the chart from playing.
///
/// <para><b>The defect, reported from real use on 2026-09-04.</b> Cody: "pressing F3 mutes
/// sonification; if I press Home and then Space to play the chart, I hear a second of audio then
/// it says playback stopped. Does F3 gate the ability to play back the chart even if sonification
/// is disabled? Because the chart should still play, especially if we're going to have speech
/// narration during playback."</para>
///
/// <para><b>What it was.</b> <c>SonificationManager.IsEnabled</c> was
/// <c>set { _isEnabled = value; if (!value) Stop(); }</c>, and <c>Stop()</c> is
/// <c>_playback.Stop()</c> — it cancels the sequencer's CancellationTokenSource. The store
/// subscription assigns that property on EVERY state change, unconditionally, and the sequencer
/// dispatches a <c>NavigateAction</c> for every bar it plays. So with sonification off, each bar
/// re-assigned <c>false</c> and cancelled the playback that was producing it. Measured before the
/// fix: <b>2 bars of 200</b> with F3 off against 200 of 200 with it on — the two being how far
/// the loop got before the first cancel landed, which is the "second of audio" Cody heard.</para>
///
/// <para><b>Why the fix is in two places.</b> The setter stops cancelling (a property assignment
/// reads as free at every call site, so a setter that kills a background job is a trap whatever
/// guards it), and <c>AudioSequencer</c> gained the check that F3 actually deserves: it renders
/// silence per bar while the cursor keeps walking. That is what <c>docs/SHORTCUTS.md</c> has
/// always promised — "F3 — Toggle chart sonification (navigation tones, playback)" — and it is
/// what lets playback stay the terminal's narration mode with the tones off.</para>
/// </summary>
public sealed class PlaybackSurvivesSonificationMuteTests
{
    private sealed class SpyDriver : IAudioDriver
    {
        public List<int> Voiced { get; } = new();
        public int SampleRate => 44100;
        public int Channels => 2;
        public event Action<int>? PointReached { add { } remove { } }
        public void SetVoice(int slot, double frequency, float volume, float pan, string waveform,
            bool continuous, double durationSeconds = 0.2, int dataIndex = -1, string envelope = "Sustain",
            bool click = false, float noiseAmount = 0f, string noiseType = "pink", float squareMix = 0f,
            float sawMix = 0f, float triangleMix = 0f, float subSawMix = 0f)
        {
            if (volume > 0f) lock (Voiced) Voiced.Add(slot);
        }
        public void StopVoice(int slot) { }
        public void StopAll() { }
        public void Reset() { }
        public void SetMasterGain(float gain) { }
        public void Pause() { }
        public void Resume() { }
    }

    private const int BarCount = 120;

    private static List<Ohlcv> Bars()
    {
        var list = new List<Ohlcv>(BarCount);
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < BarCount; i++)
            list.Add(new Ohlcv(t.AddHours(i), 100 + i * 0.1, 102 + i * 0.1, 98 + i * 0.1, 101 + i * 0.1, 1000));
        return list;
    }

    private static ChartSeries Candles()
    {
        var cfg = new SeriesConfig
        {
            Id = "s1", Name = "Candles", FriendlyName = "Candles",
            IndicatorCode = "CANDLES", Pane = "Main", IsVisible = true, Volume = 1f,
        };
        cfg.Components.Add(new ComponentConfig
        {
            Name = "Close", DisplayName = "Close", DisplayType = ComponentDisplayType.Line,
            IsVisible = true, IsEnabled = true, Volume = 1f, BaseFrequency = 440,
            PitchMapping = PitchMapping.Price, AmplitudeMapping = AmplitudeMapping.None,
            Waveform = "sine", EnvelopeType = "Sustain",
        });

        var buf = new SeriesDataBuffer { SeriesId = "s1" };
        var arr = new double[BarCount];
        for (int i = 0; i < BarCount; i++) arr[i] = 101 + i * 0.1;
        buf.ComponentData["Close"] = arr;
        return new ChartSeries(cfg, buf);
    }

    /// <summary>
    /// Drives a REAL store, SonificationManager, PlaybackOrchestrator and AudioSequencer — the
    /// same four objects the app wires — and plays the chart end to end. The wiring is the point:
    /// the defect lived in the loop between the sequencer's per-bar <c>NavigateAction</c> and the
    /// manager's store subscription, so any test that substitutes either half cannot see it.
    /// </summary>
    private static async Task<(List<int> reached, SpyDriver driver)> PlayChartAsync(bool sonificationEnabled)
    {
        var bus = new EventBus();
        var store = new WorkspaceStore(bus, new ViewportRangeCalculator(),
            new ViewportNavigationService(), new VolumeStateService());

        store.Dispatch(new UpdateSettingsAction(_ => WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(Bars()),
            ActiveSeries = ImmutableList.Create(Candles()),
            PrimarySeriesId = "s1",
            FocusedSeriesId = "s1",
            ViewportStartIndex = 0,
            ViewportLength = BarCount,
            ViewportRange = (90, 130),
            PaneRanges = ImmutableDictionary<string, (double Min, double Max)>.Empty.Add("Main", (90, 130)),
            ChartVolume = 1f,
            PlaybackSpeed = 10f,          // fast: this is about how far it gets, not timing
            CurrentDataIndex = 0,
            IsSonificationEnabled = sonificationEnabled,
        }));

        var driver = new SpyDriver();
        var sequencer = new AudioSequencer(driver, new DefaultSonificationStrategy(new SoundPatchRegistry()),
            store, new SoundPatchRegistry(), NullLogger<AudioSequencer>.Instance);
        var orchestrator = new PlaybackOrchestrator(sequencer, driver, NullLogger<PlaybackOrchestrator>.Instance);
        using var manager = new SonificationManager(orchestrator, new MockNavigationSonifier(), store,
            new MockMainThreadService(), bus);

        var reached = new List<int>();
        orchestrator.PlaybackPointReached += i => { lock (reached) reached.Add(i); };
        var finished = new TaskCompletionSource();
        orchestrator.PlaybackFinished += () => finished.TrySetResult();

        store.Dispatch(new SetPlaybackAction(true, PlaybackScope.Chart));

        await Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        await Task.Delay(150);

        lock (reached) return (reached.ToList(), driver);
    }

    [Fact]
    public async Task The_whole_chart_plays_with_sonification_muted()
    {
        var (reached, _) = await PlayChartAsync(sonificationEnabled: false);

        Assert.Equal(BarCount, reached.Count);
        Assert.Equal(BarCount - 1, reached[^1]);
    }

    /// <summary>
    /// The control. Without it the assertion above could pass on a build where playback never
    /// runs at all, and this pair is also what dates the defect: the two runs differed only in
    /// <c>IsSonificationEnabled</c>.
    /// </summary>
    [Fact]
    public async Task The_whole_chart_plays_with_sonification_on()
    {
        var (reached, driver) = await PlayChartAsync(sonificationEnabled: true);

        Assert.Equal(BarCount, reached.Count);
        Assert.NotEmpty(driver.Voiced);
    }

    /// <summary>
    /// …and F3 still means something: the tones are silent. Without this the fix would read as
    /// "F3 no longer affects playback at all", which contradicts SHORTCUTS.md and would make the
    /// mute useless on the one surface that produces the most sound.
    /// </summary>
    [Fact]
    public async Task Muted_playback_writes_no_playback_voices()
    {
        var (_, driver) = await PlayChartAsync(sonificationEnabled: false);

        Assert.Empty(driver.Voiced);
    }
}
