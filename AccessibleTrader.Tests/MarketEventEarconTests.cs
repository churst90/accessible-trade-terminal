using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// Stage 2 part D: the market-event landing bell. A small inharmonic bell, one strike for
/// Medium and Low, a falling fourth for High, deduplicated per bar, with the same filter as
/// the spoken clause and its own four silences.
/// </summary>
public class MarketEventEarconTests
{
    private static readonly DateTime Now = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static MarketEvent Event(MarketEventImportance importance = MarketEventImportance.Medium)
        => MarketEventTestData.Event(
            importance: importance,
            moment: new DateTime(2024, 3, 12, 12, 30, 0, DateTimeKind.Utc));

    private static WorkspaceState State(
        MarketEvent? e = null,
        string level = "all",
        bool sound = true,
        bool earcons = true,
        int index = 1,
        bool playing = false,
        bool paused = false)
    {
        var bars = new[]
        {
            MarketEventTestData.Bar(new DateTime(2024, 3, 11, 0, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc)),
        };
        return MarketEventTestData.State(bars, timeframe: "1d", index: index, level: level) with
        {
            MarketEventsSoundOnEventBars = sound,
            IsEarconsEnabled = earcons,
            IsPlaying = playing,
            IsPaused = paused,
        };
    }

    private static NavigationSonifier Sonifier(
        SpyDriver driver,
        MarketEvent[]? events = null,
        ISoundPatchLibrary? patchLibrary = null)
        => new(driver, new NoOpStrategy(), new SoundPatchRegistry(), patchLibrary,
            new MarketEventBarMapCache(MarketEventTestData.Calendar(events ?? Array.Empty<MarketEvent>())));

    [Fact]
    public void HighIsAFallingFourthOfTwoStrikes()
    {
        var notes = NavigationSonifier.BellNotes(MarketEventImportance.High, 1f).ToList();

        Assert.Equal(6, notes.Count);
        Assert.Equal(NavigationSonifier.BellF5, notes[0].Frequency);
        Assert.Equal(NavigationSonifier.BellF5 * 2.76, notes[1].Frequency, 6);
        Assert.Equal(NavigationSonifier.BellF5 * 5.40, notes[2].Frequency, 6);
        Assert.Equal(NavigationSonifier.BellC5, notes[3].Frequency);
        Assert.Equal(NavigationSonifier.BellC5 * 2.76, notes[4].Frequency, 6);
        Assert.Equal(NavigationSonifier.BellC5 * 5.40, notes[5].Frequency, 6);

        Assert.All(notes.Take(3), n => Assert.Equal(0, n.DelayMs));
        Assert.All(notes.Skip(3), n => Assert.Equal(110, n.DelayMs));

        Assert.Equal(0.18, notes[0].Duration);
        Assert.Equal(0.08, notes[1].Duration);
        Assert.Equal(0.04, notes[2].Duration);
        Assert.Equal(0.13f, notes[0].Volume, 6);
        Assert.Equal(0.13f * 0.35f, notes[1].Volume, 6);
        Assert.Equal(0.13f * 0.15f, notes[2].Volume, 6);
    }

    [Theory]
    [InlineData(MarketEventImportance.Medium, 0.10f)]
    [InlineData(MarketEventImportance.Low, 0.06f)]
    public void MediumAndLowAreOneStrikeAtTheirOwnVolume(
        MarketEventImportance importance, float expected)
    {
        var notes = NavigationSonifier.BellNotes(importance, 1f);

        Assert.Equal(3, notes.Count);
        Assert.All(notes, n => Assert.Equal(0, n.DelayMs));
        Assert.Equal(NavigationSonifier.BellF5, notes[0].Frequency);
        Assert.Equal(expected, notes[0].Volume, 6);
        Assert.Equal(expected * 0.35f, notes[1].Volume, 6);
        Assert.Equal(expected * 0.15f, notes[2].Volume, 6);
    }

    [Fact]
    public void ChartVolumeScalesAndClampsTheBell()
    {
        Assert.Equal(0.065f, NavigationSonifier.BellNotes(MarketEventImportance.High, 0.5f)[0].Volume, 6);
        Assert.Equal(0f, NavigationSonifier.BellNotes(MarketEventImportance.High, 0f)[0].Volume, 6);
        Assert.Equal(1f, NavigationSonifier.BellNotes(MarketEventImportance.High, 10f)[0].Volume, 6);
    }

    [Fact]
    public void ArrivingOnAnEventBarSoundsOnceAndLeavingResetsIt()
    {
        var driver = new SpyDriver();
        var sonifier = Sonifier(driver, new[] { Event() });
        var state = State(Event());

        sonifier.SyncNavigationSlots(state);
        Assert.Equal(3, sonifier.LastMarketEventBellNotes.Count);
        Assert.Equal(3, driver.UiNotes().Count);

        sonifier.SyncNavigationSlots(state);
        Assert.Equal(3, driver.UiNotes().Count);

        sonifier.SyncNavigationSlots(state with { CurrentDataIndex = 0 });
        sonifier.SyncNavigationSlots(state);
        Assert.Equal(6, driver.UiNotes().Count);
    }

    [Fact]
    public void TheLevelOffIsSilent()
    {
        var driver = new SpyDriver();
        var sonifier = Sonifier(driver, new[] { Event() });

        sonifier.SyncNavigationSlots(State(Event(), level: "off"));

        Assert.Empty(sonifier.LastMarketEventBellNotes);
        Assert.Empty(driver.UiNotes());
    }

    [Fact]
    public void TheSoundBoxUntickedIsSilent()
    {
        var driver = new SpyDriver();
        var sonifier = Sonifier(driver, new[] { Event() });

        sonifier.SyncNavigationSlots(State(Event(), sound: false));

        Assert.Empty(sonifier.LastMarketEventBellNotes);
        Assert.Empty(driver.UiNotes());
    }

    [Fact]
    public void ShiftF3IsSilent()
    {
        var driver = new SpyDriver();
        var sonifier = Sonifier(driver, new[] { Event() });

        sonifier.SyncNavigationSlots(State(Event(), earcons: false));

        Assert.Empty(sonifier.LastMarketEventBellNotes);
        Assert.Empty(driver.UiNotes());
    }

    [Fact]
    public void ABarWithOnlyAFilteredOutEventIsSilentAndItsClauseIsEmpty()
    {
        var low = MarketEventTestData.Event(
            importance: MarketEventImportance.Low,
            moment: new DateTime(2024, 3, 12, 12, 30, 0, DateTimeKind.Utc));
        var driver = new SpyDriver();
        var sonifier = Sonifier(driver, new[] { low });
        var state = State(low, level: "importantOnly");

        sonifier.SyncNavigationSlots(state);

        Assert.Empty(sonifier.LastMarketEventBellNotes);
        Assert.Empty(driver.UiNotes());
        Assert.Equal("", MarketEventSpeech.Clause(
            new[] { MarketEventTestData.Placed(low, 1) }, 86400, MarketEventLevel.ImportantOnly,
            MarketEventKindFilter.All, MarketType.Crypto));
    }

    [Fact]
    public void ATextLabelOnTheSameBarDelaysTheBellBy130Ms()
    {
        var label = LabelSeries();
        var e = Event();
        var driver = new SpyDriver();
        var sonifier = Sonifier(driver, new[] { e });
        var bars = new List<Ohlcv>
        {
            MarketEventTestData.Bar(new DateTime(2024, 3, 11, 0, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc)),
        };
        var state = State(e) with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            CurrentDataIndex = 1,
            ActiveSeries = ImmutableList.Create(label),
            FocusedSeriesId = label.Id,
            FocusedComponentIndex = 0,
            PaneRanges = ImmutableDictionary<string, (double Min, double Max)>.Empty.Add("Main", (0, 100000)),
            ViewportRange = (0, 100000),
        };

        sonifier.SyncNavigationSlots(state);

        Assert.NotEmpty(sonifier.LastMarketEventBellNotes);
        Assert.All(sonifier.LastMarketEventBellNotes, n => Assert.Equal(130, n.DelayMs));
    }

    [Fact]
    public void APatchOverrideReplacesTheBellOnceWithItsImportanceScale()
    {
        foreach (var (importance, scale) in new[]
        {
            (MarketEventImportance.High, 1.0f),
            (MarketEventImportance.Medium, 0.75f),
            (MarketEventImportance.Low, 0.5f),
        })
        {
            var library = Substitute.For<ISoundPatchLibrary>();
            library.EarconOverrides.Returns(new EarconSettings
            {
                EarconPatchIds = { [EarconPatchPlayer.MarketEventKey] = "patch-market" },
            });
            library.GetPatch("patch-market").Returns(new AccessibleTrader.Sdk.Models.SoundPatch
            {
                Id = "patch-market", Name = "Market bell", BaseFrequency = 500, Volume = 0.8f,
                DurationSeconds = 0.4, EnvelopeType = "Ping", Waveform = "triangle",
            });

            var driver = new SpyDriver();
            var sonifier = Sonifier(driver, new[] { Event(importance) }, library);

            sonifier.SyncNavigationSlots(State(Event(importance)));

            Assert.Empty(sonifier.LastMarketEventBellNotes);
            var cue = Assert.Single(driver.UiNotes());
            Assert.Equal(EarconPatchPlayer.CueSlotStart, cue.Slot);
            Assert.Equal(500, cue.Frequency);
            Assert.Equal(0.8f * scale, cue.Volume, 6);
        }
    }

    [Fact]
    public void PlaybackFiresTheBellAndArrowingWithF3OffDoesNot()
    {
        var navigation = Substitute.For<INavigationSonifier>();
        var playback = Substitute.For<IPlaybackOrchestrator>();
        playback.IsPlaying.Returns(true);
        var store = new MockWorkspaceStore();
        using var manager = new SonificationManager(
            playback, navigation, store, new MockMainThreadService(), new SpyEventBus());
        var bars = new[]
        {
            MarketEventTestData.Bar(new DateTime(2024, 3, 11, 0, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc)),
        };

        // Start playback without a new bell at the start bar.
        store.EmitState(State(playing: true, index: 0));
        navigation.DidNotReceive().FireMarketEventEarcon(Arg.Any<WorkspaceState>(), Arg.Any<int>());

        // Playback crosses onto the next bar: the bell fires, even with F3 off.
        store.EmitState(State(playing: true, index: 1) with { IsSonificationEnabled = false });
        navigation.Received(1).FireMarketEventEarcon(Arg.Any<WorkspaceState>(), 1);

        // Arrowing with F3 off never reaches SyncNavigationSlots, so no bell.
        var arrowStore = new MockWorkspaceStore();
        var arrowNavigation = Substitute.For<INavigationSonifier>();
        var notPlaying = Substitute.For<IPlaybackOrchestrator>();
        notPlaying.IsPlaying.Returns(false);
        using var arrowManager = new SonificationManager(
            notPlaying, arrowNavigation, arrowStore, new MockMainThreadService(), new SpyEventBus());
        arrowStore.EmitState(State(index: 0) with { IsSonificationEnabled = false });
        arrowStore.EmitState(State(index: 1) with { IsSonificationEnabled = false });

        arrowNavigation.DidNotReceive().SyncNavigationSlots(Arg.Any<WorkspaceState>());
        arrowNavigation.DidNotReceive().FireMarketEventEarcon(Arg.Any<WorkspaceState>(), Arg.Any<int>());
    }

    [Theory]
    [InlineData("AccessibleTrader.WebHost/ServiceCollectionExtensions.cs", "AddScoped")]
    [InlineData("AccessibleTrader.BlazorClient/ServiceCollectionExtensions.cs", "AddSingleton")]
    public void TheSonifierAndTheMapShareOneLifetimeOnEachHead(string file, string lifetime)
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), file));

        Assert.Contains($"services.{lifetime}<IMarketEventBarMapCache, MarketEventBarMapCache>();", text);
        Assert.Contains($"services.{lifetime}<INavigationSonifier, NavigationSonifier>();", text);
    }

    private static ChartSeries LabelSeries()
    {
        var cfg = new SeriesConfig { Id = "label-1", Name = "Label: here", Pane = "Main" };
        cfg.Components.Add(new ComponentConfig
        {
            Name = "Label", DisplayName = "here", DisplayType = ComponentDisplayType.Line,
            IsVisible = true, Volume = 1.0f, Waveform = "sine", BaseFrequency = 440,
        });
        var buf = new SeriesDataBuffer { SeriesId = cfg.Id };
        buf.ComponentData["Label"] = new[] { double.NaN, 100.0 };
        return new ChartSeries(cfg, buf)
        {
            Drawing = new DrawingData
            {
                Type = DrawingType.TextLabel,
                AnchorDate1 = new DateTime(2024, 3, 12, 0, 0, 0, DateTimeKind.Utc),
                AnchorPrice1 = 100,
                Text = "here",
            },
        };
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private sealed record VoiceCall(int Slot, double Frequency, float Volume, double Duration);

    [Fact]
    public void TheHeadlineSoundIsARisingTriangleBlipNotTheBell()
    {
        var driver = new SpyDriver();
        var sonifier = Sonifier(driver);

        sonifier.PlayHeadlineSound(State(), MarketEventImportance.High);
        var high = sonifier.LastHeadlineNotes;
        Assert.Equal(new[] { NavigationSonifier.HeadlineA5, NavigationSonifier.HeadlineE6, NavigationSonifier.HeadlineA6 },
            high.Select(n => n.Frequency));
        Assert.Equal(new[] { 0.0, 70.0, 140.0 }, high.Select(n => n.DelayMs));
        Assert.Empty(sonifier.LastMarketEventBellNotes);

        sonifier.PlayHeadlineSound(State(), MarketEventImportance.Low);
        Assert.Equal(2, sonifier.LastHeadlineNotes.Count);
        Assert.True(sonifier.LastHeadlineNotes[0].Volume < high[0].Volume);
    }

    [Fact]
    public void TheHeadlineSoundHonoursShiftF3ButNotTheEventBarBox()
    {
        var sonifier = Sonifier(new SpyDriver());

        sonifier.PlayHeadlineSound(State(earcons: false), MarketEventImportance.High);
        Assert.Empty(sonifier.LastHeadlineNotes);

        sonifier.PlayHeadlineSound(State(sound: false), MarketEventImportance.High);
        Assert.NotEmpty(sonifier.LastHeadlineNotes);
    }

    [Fact]
    public void TheLiveNoticeBellIsTheSameBellWithoutABar()
    {
        var driver = new SpyDriver();
        var sonifier = Sonifier(driver, Array.Empty<MarketEvent>());

        sonifier.PlayMarketEventBell(State(sound: true, earcons: true), MarketEventImportance.High);

        var notes = sonifier.LastMarketEventBellNotes;
        Assert.Equal(NavigationSonifier.BellNotes(MarketEventImportance.High, 0.5f), notes);
        Assert.Equal(6, notes.Count);
        Assert.Equal(NavigationSonifier.BellF5, notes[0].Frequency);
        Assert.Equal(NavigationSonifier.BellC5, notes[3].Frequency);
        Assert.All(notes.Take(3), n => Assert.Equal(0, n.DelayMs));
        Assert.All(notes.Skip(3), n => Assert.Equal(110, n.DelayMs));
        Assert.Equal(3, driver.UiNotes().Count); // the second strike is scheduled after 110 ms
    }

    [Fact]
    public void TheLiveNoticeBellHonoursTheSoundBoxAndTheEarconMute()
    {
        foreach (var state in new[]
        {
            State(sound: false, earcons: true),
            State(sound: true, earcons: false),
        })
        {
            var driver = new SpyDriver();
            var sonifier = Sonifier(driver, Array.Empty<MarketEvent>());

            sonifier.PlayMarketEventBell(state, MarketEventImportance.High);

            Assert.Empty(sonifier.LastMarketEventBellNotes);
            Assert.Empty(driver.UiNotes());
        }
    }

    private sealed class SpyDriver : IAudioDriver
    {
        public int SampleRate => 48000;
        public int Channels => 2;
#pragma warning disable CS0067
        public event Action<int>? PointReached;
#pragma warning restore CS0067
        private readonly object _gate = new();
        private readonly List<VoiceCall> _calls = new();

        public List<VoiceCall> UiNotes()
        {
            lock (_gate) return _calls.Where(c => c.Slot >= 16).ToList();
        }

        public void SetVoice(int slot, double frequency, float volume, float pan, string waveform,
            bool continuous, double durationSeconds = 0.2, int dataIndex = -1, string envelope = "Sustain",
            bool click = false, float noiseAmount = 0f, string noiseType = "pink", float squareMix = 0f,
            float sawMix = 0f, float triangleMix = 0f, float subSawMix = 0f)
        {
            lock (_gate) _calls.Add(new VoiceCall(slot, frequency, volume, durationSeconds));
        }

        public void StopVoice(int slot) { }
        public void StopAll() { }
        public void Reset() { }
        public void SetMasterGain(float gain) { }
        public void Pause() { }
        public void Resume() { }
    }

    private sealed class NoOpStrategy : ISonificationStrategy
    {
        public AudioPoint CreateAudioPoint(ChartSeries series, ComponentConfig comp, double val, Ohlcv point,
            int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, float chartVolume,
            double? prevVal = null, bool isLogScale = false, int dataIndex = -1, bool isInverted = false)
            => new(comp.BaseFrequency, 1.0f, comp.Waveform, 0.2, "Sustain");

        public AudioPoint MapToAudio(ChartSeries series, int dataIndex, List<Ohlcv> data, int relativeIndex,
            int viewportWidth, (double Min, double Max) viewportRange, float chartVolume, bool isLogScale = false, bool isInverted = false)
            => new(440, 1, "sine", 0.2, "Sustain");

        public AudioPoint MapComponentToAudio(ChartSeries series, int componentIndex, int dataIndex, List<Ohlcv> data,
            int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, float chartVolume,
            bool isLogScale = false, bool isInverted = false)
            => new(440, 1, "sine", 0.2, "Sustain");

        public int ResolveComponentVoiceCount(ComponentConfig comp) => 1;
    }
}
