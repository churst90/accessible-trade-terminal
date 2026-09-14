using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Immutable;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A playback voice that is never told to stop never stops.</b>
///
/// <para>
/// Playback voices are CONTINUOUS oscillators — <c>continuous = !IsPing(EnvelopeType)</c> — so
/// unlike a navigation ping they do not self-terminate. Two separate decisions keep them from
/// droning forever, and the A2g mutant set found neither of them under test.
/// </para>
///
/// <para>
/// <b>Which voices are continuous.</b> <c>AudioSequencer.IsPing</c> is the one place the envelope
/// name is tested, and it compares case-INsensitively on purpose: <c>EnvelopeType</c> is free text
/// arriving from provider metadata, saved workspaces and imported patch JSON, none of which
/// normalise it. Its own doc comment records what a case-sensitive compare did — an imported
/// <c>"ping"</c> was a marker for the NaN guard, continuous for the voice, and got duration 0: a
/// permanent drone on a playback slot that never decays. Restoring
/// <c>StringComparison.Ordinal</c> broke nothing, so the helper this was extracted into was itself
/// unguarded.
/// </para>
///
/// <para>
/// <b>Which slots get silenced.</b> <c>SilencePlaybackVoices</c> runs on <c>Stop</c> and on
/// entering pause, and it has to cover the CLOUD range (96-127) as well as the component range
/// (32-95) — cloud fills are armed by a second pass that runs outside the voice plan. Narrowing
/// its loop to stop at <c>PlaybackSlotEnd</c> left the last bar's cloud chord ringing
/// indefinitely, and broke nothing.
/// </para>
///
/// <para>
/// Both are the same failure from the user's side: a sound that outlives the thing that caused it,
/// on a surface being listened to for hours. There is no visual cue that it is still there.
/// </para>
/// </summary>
public sealed class PlaybackVoiceLifecycleTests
{
    // ── Envelope naming ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Every spelling of "Ping" that can reach the sequencer from a source that does not
    /// normalise case. Asserted through the helper the render path actually calls, so a compare
    /// tightened anywhere behind it shows up here.
    /// </summary>
    [Theory]
    [InlineData("Ping")]
    [InlineData("ping")]
    [InlineData("PING")]
    [InlineData("pInG")]
    public void EverySpellingOfPingIsAPing(string envelopeType)
    {
        Assert.True(AudioSequencer.IsPing(envelopeType),
            $"'{envelopeType}' was not recognised as a Ping. EnvelopeType is free text from provider " +
            "metadata, saved workspaces and imported patch JSON — a case-sensitive compare here makes " +
            "the voice continuous with duration 0, which is a drone that never decays.");
    }

    [Theory]
    [InlineData("Sustain")]
    [InlineData("sustain")]
    [InlineData("")]
    [InlineData(null)]
    public void NothingElseIsAPing(string? envelopeType)
    {
        // The vacuity twin: a helper that answered true to everything would satisfy the theory
        // above while making every playback voice a one-shot.
        Assert.False(AudioSequencer.IsPing(envelopeType));
    }

    /// <summary>
    /// And the consequence, end to end: a marker component whose envelope arrives lowercase must
    /// be armed as a one-shot with a real duration, not as a continuous voice with none.
    /// </summary>
    [Fact]
    public async Task ALowercasePingIsArmedAsAOneShotWithADuration()
    {
        var driver = await PlayOneBarAsync(MarkerSeries(envelopeType: "ping"));

        var voices = driver.Calls.Where(c => c.Slot is >= 32 and <= 95).ToList();
        Assert.NotEmpty(voices);
        Assert.All(voices, v =>
        {
            Assert.False(v.Continuous,
                "a lowercase 'ping' was armed as a CONTINUOUS voice — it will never self-terminate.");
            Assert.True(v.DurationSeconds > 0,
                $"a lowercase 'ping' was armed with duration {v.DurationSeconds} — a zero-length " +
                "voice on a playback slot is a permanent drone.");
        });
    }

    // ── Silencing ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>Stop</c> must silence every slot this sequencer can drive. The cloud range is the half
    /// that a second, separate pass arms, so it is the half a narrowed loop leaves behind.
    /// </summary>
    [Fact]
    public void StopSilencesTheCloudSlotsAsWellAsTheComponentSlots()
    {
        var driver = new SpyDriver();
        var sequencer = new AudioSequencer(driver,
            new DefaultSonificationStrategy(new SoundPatchRegistry()),
            new MockWorkspaceStore(), new SoundPatchRegistry(),
            NullLogger<AudioSequencer>.Instance);

        sequencer.Stop();

        // 32-95 are the component voices; 96-127 are the cloud fills. Navigation (0-15) and
        // earcons (16-31) are deliberately left alone so arrow-key auditioning survives a stop.
        for (int slot = 32; slot < AudioEngine.MaxVoices; slot++)
            Assert.True(driver.Stopped.Contains(slot),
                $"Stop() left slot {slot} armed. Slots 96-127 are the cloud fills, armed by a pass " +
                "that runs outside the voice plan — leaving them is the last bar's chord droning on.");
    }

    [Fact]
    public void StopLeavesTheNavigationAndEarconSlotsAlone()
    {
        // The other direction, and the one a careless widening breaks: stopping playback must not
        // cut the arrow-key voice or a cue that is still sounding.
        var driver = new SpyDriver();
        var sequencer = new AudioSequencer(driver,
            new DefaultSonificationStrategy(new SoundPatchRegistry()),
            new MockWorkspaceStore(), new SoundPatchRegistry(),
            NullLogger<AudioSequencer>.Instance);

        sequencer.Stop();

        for (int slot = 0; slot < 32; slot++)
            Assert.False(driver.Stopped.Contains(slot),
                $"Stop() silenced slot {slot}, which belongs to navigation (0-15) or earcons (16-31).");
    }

    // ── Harness ─────────────────────────────────────────────────────────────────────

    private sealed record VoiceCall(int Slot, float Volume, bool Continuous, double DurationSeconds, string Envelope);

    private sealed class SpyDriver : IAudioDriver
    {
        public List<VoiceCall> Calls { get; } = new();
        public HashSet<int> Stopped { get; } = new();
        public int SampleRate => 44100;
        public int Channels => 2;
        public event Action<int>? PointReached { add { } remove { } }
        public void SetVoice(int slot, double frequency, float volume, float pan, string waveform,
            bool continuous, double durationSeconds = 0.2, int dataIndex = -1, string envelope = "Sustain",
            bool click = false, float noiseAmount = 0f, string noiseType = "pink", float squareMix = 0f,
            float sawMix = 0f, float triangleMix = 0f, float subSawMix = 0f)
        {
            lock (Calls) Calls.Add(new VoiceCall(slot, volume, continuous, durationSeconds, envelope));
        }
        public void StopVoice(int slot) { lock (Stopped) Stopped.Add(slot); }
        public void StopAll() { }
        public void Reset() { }
        public void SetMasterGain(float gain) { }
        public void Pause() { }
        public void Resume() { }
    }

    private const int BarCount = 20;

    /// <summary>A one-component Dot series whose every bar carries a signal, with the envelope
    /// spelled however the caller asks — which is the point of the test above.</summary>
    private static ChartSeries MarkerSeries(string envelopeType)
    {
        var comp = new ComponentConfig
        {
            Name = "dot", DisplayName = "dot", DisplayType = ComponentDisplayType.Dot,
            Role = ComponentRole.Signal, DataMapping = "close",
            IsVisible = true, IsEnabled = true, Volume = 1f, Waveform = "sine",
            AmplitudeMapping = AmplitudeMapping.None, PitchMapping = PitchMapping.Direction,
            BaseFrequency = 660, FreqMultiplier = 1.0,
            BullishFrequency = 660, BearishFrequency = 220,
            EnvelopeType = envelopeType,
        };
        var cfg = new SeriesConfig { Id = "s", Name = "s", Pane = "Main", IsVisible = true, Volume = 1f };
        cfg.Components.Add(comp);

        var data = new SeriesDataBuffer { SeriesId = "s" };
        var values = new double[BarCount];
        for (int i = 0; i < BarCount; i++) values[i] = 100 + i;
        data.ComponentData["dot"] = values;

        return new ChartSeries(cfg, data);
    }

    private static async Task<SpyDriver> PlayOneBarAsync(ChartSeries series)
    {
        var bars = new List<Ohlcv>(BarCount);
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < BarCount; i++) bars.Add(new Ohlcv(t.AddHours(i), 100, 102, 98, 101, 1000));

        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial with
        {
            ActiveSeries = ImmutableList.Create(series),
            ViewportStartIndex = 0,
            ViewportLength = BarCount,
            ViewportRange = (90, 130),
            PaneRanges = ImmutableDictionary<string, (double Min, double Max)>.Empty.Add("Main", (90, 130)),
            ChartVolume = 1f,
            PlaybackSpeed = 1.0f,
        });

        var driver = new SpyDriver();
        var sequencer = new AudioSequencer(driver, new DefaultSonificationStrategy(new SoundPatchRegistry()),
            store, new SoundPatchRegistry(), NullLogger<AudioSequencer>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await sequencer.StartPlaybackAsync(series, bars, BarCount - 1, cts.Token);
        return driver;
    }
}
