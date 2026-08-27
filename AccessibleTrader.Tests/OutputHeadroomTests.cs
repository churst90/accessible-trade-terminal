using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Immutable;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Whatever the chart is doing, the engine's output stays inside ±1.0.</b>
///
/// <para>
/// <see cref="AudioEngine.Read"/> sums every active voice into <c>leftSum</c>/<c>rightSum</c> and
/// writes the total straight into the host buffer. Nothing between the sum and the buffer bounds
/// it. That is fine for one navigation note and it is fine for two; Chart-scope playback arms one
/// voice per visible component of every visible series — up to sixty-four of them, plus thirty-two
/// cloud fills — and they are all Sustain voices sounding at once, in phase for as long as their
/// frequencies stay close.
/// </para>
///
/// <para>
/// Past ±1.0 the host driver clips, and clipping is not a volume problem. It is broadband
/// distortion: the crack that arrives on the loudest bar, over the top of a screen reader that is
/// speaking, on a surface whose entire job is to be listened to closely for hours. It also cannot
/// be escaped by turning the chart down, because it happens after the mix.
/// </para>
///
/// <para>
/// The measurement below is not a guess at a plausible chart. It builds one, hands it to the real
/// <see cref="AudioSequencer"/> in the real Chart scope, lets it arm the real
/// <see cref="AudioEngine"/> through an <see cref="IAudioDriver"/> exactly as a host driver would,
/// and then renders and looks at the samples.
/// </para>
/// </summary>
public sealed class OutputHeadroomTests
{
    // ── Harness ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Records the arming rather than performing it.
    ///
    /// <para>
    /// The sequencer silences every playback voice in its <c>finally</c>, so an engine wired
    /// straight to it is empty by the time the run returns — which is exactly what the vacuity
    /// test below caught on the first attempt. Recording the calls and replaying them into a fresh
    /// engine measures the mix as it stood DURING the bar, which is the thing that reaches the
    /// speakers. Recording stops when <c>PlaybackFinished</c> fires, so the teardown's StopVoice
    /// calls are not mistaken for part of the bar.
    /// </para>
    /// </summary>
    private sealed class RecordingDriver : IAudioDriver
    {
        private readonly Dictionary<int, Action<AudioEngine>> _armed = new();
        private bool _closed;

        public int SampleRate => 44100;
        public int Channels => 2;
        public event Action<int>? PointReached { add { } remove { } }

        public void CloseRecording() => _closed = true;

        public void SetVoice(int slot, double frequency, float volume, float pan, string waveform,
            bool continuous, double durationSeconds = 0.2, int dataIndex = -1, string envelope = "Sustain",
            bool click = false, float noiseAmount = 0f, string noiseType = "pink", float squareMix = 0f,
            float sawMix = 0f, float triangleMix = 0f, float subSawMix = 0f)
        {
            if (_closed) return;
            _armed[slot] = e => e.SetVoice(slot, frequency, volume, pan, waveform, continuous,
                durationSeconds, dataIndex, envelope, click, noiseAmount, noiseType,
                squareMix, sawMix, triangleMix, subSawMix);
        }

        public void StopVoice(int slot) { if (!_closed) _armed.Remove(slot); }
        public void StopAll() { if (!_closed) _armed.Clear(); }
        public void Reset() => StopAll();
        public void SetMasterGain(float gain) { }
        public void Pause() { }
        public void Resume() { }

        /// <summary>Replays the recorded bar into a fresh engine.</summary>
        public AudioEngine Replay()
        {
            var engine = new AudioEngine();
            foreach (var arm in _armed.Values) arm(engine);
            return engine;
        }

        public int ArmedVoices => _armed.Count;
    }

    private static ComponentConfig Comp(string name, ComponentDisplayType type, ComponentRole role)
    {
        var p = new SonificationProfileProvider().GetProfile(type, role, name);
        return new ComponentConfig
        {
            Name = name,
            DisplayName = name,
            DisplayType = type,
            Role = role,
            DataMapping = "close",
            IsVisible = true,
            IsEnabled = true,
            Volume = 1f,
            Waveform = p.Waveform,
            AboveReferenceWaveform = p.AboveWaveform,
            BelowReferenceWaveform = p.BelowWaveform,
            AmplitudeMapping = p.AmplitudeMapping,
            PitchMapping = p.PitchMapping,
            BaseFrequency = p.BaseFrequency,
            FreqMultiplier = p.FreqMultiplier,
            BullishFrequency = 440.0,
            BearishFrequency = 220.0,
            EnvelopeType = p.EnvelopeType,
        };
    }

    /// <summary>
    /// One indicator pane's worth of series: three line components carrying real arrays, so the
    /// strategy takes its normal value path rather than the OHLCV fallback.
    /// </summary>
    private static ChartSeries IndicatorSeries(string id, int bars)
    {
        var cfg = new SeriesConfig { Id = id, Name = id, Pane = id, IsVisible = true, Volume = 1f };
        var data = new SeriesDataBuffer { SeriesId = id };
        foreach (var (name, role) in new[]
                 {
                     ("fast", ComponentRole.Signal),
                     ("slow", ComponentRole.Median),
                     ("hist", ComponentRole.Histogram),
                 })
        {
            cfg.Components.Add(Comp(name, name == "hist" ? ComponentDisplayType.Histogram : ComponentDisplayType.Line, role));
            var arr = new double[bars];
            for (int i = 0; i < bars; i++) arr[i] = 50 + 20 * Math.Sin(i * 0.3 + name.Length);
            data.ComponentData[name] = arr;
        }
        return new ChartSeries(cfg, data);
    }

    private static ChartSeries CandleSeries(int bars)
    {
        var cfg = new SeriesConfig { Id = CoreSeriesIds.Candles, Name = "Candles", Pane = "Main", IsVisible = true, Volume = 1f };
        cfg.Components.Add(Comp("body", ComponentDisplayType.Candle, ComponentRole.Body));
        cfg.Components.Add(Comp("upper_wick", ComponentDisplayType.Wick, ComponentRole.Wick));
        cfg.Components.Add(Comp("lower_wick", ComponentDisplayType.Wick, ComponentRole.Wick));
        return new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = CoreSeriesIds.Candles });
    }

    private static List<Ohlcv> Bars(int n)
    {
        var list = new List<Ohlcv>(n);
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < n; i++)
        {
            double mid = 100 + 10 * Math.Sin(i * 0.4);
            list.Add(new Ohlcv(t.AddHours(i), mid - 1, mid + 2, mid - 2, mid + 1, 1000));
        }
        return list;
    }

    /// <summary>
    /// Drives one bar of real Chart-scope playback, then renders the tail and returns the loudest
    /// absolute sample. Rendering starts after the voices' 12 ms declick attack has completed, so
    /// the number is the steady-state mix rather than the ramp into it.
    /// </summary>
    private static async Task<(float Peak, int VoiceCount)> RenderChartScopePeakAsync(int indicatorPanes)
    {
        const int BarCount = 40;
        var bars = Bars(BarCount);

        var seriesList = new List<ChartSeries> { CandleSeries(BarCount) };
        for (int i = 0; i < indicatorPanes; i++) seriesList.Add(IndicatorSeries($"ind{i}", BarCount));

        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial with
        {
            ActiveSeries = ImmutableList.CreateRange(seriesList),
            ViewportStartIndex = 0,
            ViewportLength = BarCount,
            ViewportRange = (80, 120),
            PaneRanges = ImmutableDictionary<string, (double Min, double Max)>.Empty
                .Add("Main", (80, 120)),
            // The shipped defaults. Nothing here is turned up.
            ChartVolume = 0.5f,
            PlaybackSpeed = 1.0f,
        });

        var driver = new RecordingDriver();
        var sequencer = new AudioSequencer(driver, new DefaultSonificationStrategy(new SoundPatchRegistry()),
            store, new SoundPatchRegistry(), NullLogger<AudioSequencer>.Instance);
        using var _ = sequencer.PlaybackFinished.Subscribe(__ => driver.CloseRecording());

        // startIndex = the last bar, so exactly one bar is armed and the loop then ends.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await sequencer.StartMultiSeriesPlaybackAsync(seriesList, bars, BarCount - 1, cts.Token);

        var engine = driver.Replay();

        // Render past the per-voice fade-in, then measure a window long enough for the voices'
        // frequencies to drift into and out of phase with each other.
        var buf = new float[1024];
        for (int done = 0; done < 8192; done += buf.Length) engine.Read(buf, 0, buf.Length);

        float peak = 0f;
        for (int done = 0; done < 88_200; done += buf.Length)
        {
            engine.Read(buf, 0, buf.Length);
            for (int i = 0; i < buf.Length; i++) peak = Math.Max(peak, Math.Abs(buf[i]));
        }
        return (peak, driver.ArmedVoices);
    }

    // ── The rule ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A chart with a candle series and five indicator panes — eighteen voices, none of them
    /// turned up, at the default chart volume. This is an ordinary working layout, not a
    /// pathological one, and it is the case the engine has to survive.
    /// </summary>
    [Fact]
    public async Task AnOrdinaryChartScopeMixStaysInsideFullScale()
    {
        var (peak, voices) = await RenderChartScopePeakAsync(indicatorPanes: 5);

        Assert.True(peak <= 1.0f,
            $"Chart-scope playback with {voices} armed voices peaked at {peak:F3} — anything above 1.0 clips " +
            "in the host driver, and clipping is broadband distortion over speech, not a loudness problem.");
    }

    /// <summary>
    /// The vacuity half. A limiter that simply returned silence, or a harness that armed nothing,
    /// would pass the test above without meaning anything — so assert the mix is actually loud.
    /// </summary>
    [Fact]
    public async Task ThatChartIsAudible()
    {
        var (peak, _) = await RenderChartScopePeakAsync(indicatorPanes: 5);
        Assert.True(peak > 0.3f, $"the harness armed nothing worth measuring — peak {peak:F3}");
    }

    /// <summary>
    /// The budget's own ceiling. Sixteen panes runs the voice plan out to the end of its slot range
    /// (32–95) — the loudest arrangement Chart scope can produce — and it must still not clip.
    /// </summary>
    [Fact]
    public async Task TheFullVoiceBudgetStillDoesNotClip()
    {
        var (peak, voices) = await RenderChartScopePeakAsync(indicatorPanes: 25);

        Assert.True(peak <= 1.0f,
            $"a saturated voice plan ({voices} voices armed) peaked at {peak:F3}");
    }

    /// <summary>
    /// Headroom is not a substitute for the master gain. Whatever the limiter does at the top of
    /// its range, turning the chart down has to keep working all the way to silence — otherwise
    /// the fix for clipping is a new floor the user cannot get below.
    /// </summary>
    [Fact]
    public void MasterGainZeroIsStillSilent()
    {
        var engine = new AudioEngine();
        for (int slot = 0; slot < 32; slot++)
            engine.SetVoice(slot, 200 + slot * 7, 1.0f, 0f, "sine", true, 10);
        engine.SetMasterGain(0f);

        var buf = new float[1024];
        for (int done = 0; done < 16_384; done += buf.Length) engine.Read(buf, 0, buf.Length);

        float peak = 0f;
        for (int done = 0; done < 16_384; done += buf.Length)
        {
            engine.Read(buf, 0, buf.Length);
            for (int i = 0; i < buf.Length; i++) peak = Math.Max(peak, Math.Abs(buf[i]));
        }
        Assert.True(peak < 1e-4f, $"master gain 0 still produced {peak:F6}");
    }

    /// <summary>
    /// A single quiet voice must come out of the engine unchanged. A limiter that is always
    /// working is a compressor, and compressing the navigation note would flatten the one
    /// dimension — loudness — that several components use to carry meaning.
    /// </summary>
    [Fact]
    public void OneQuietVoiceIsNotTouched()
    {
        static float[] RenderOne(Action<AudioEngine> arm)
        {
            var e = new AudioEngine();
            arm(e);
            var buf = new float[1024];
            for (int done = 0; done < 8192; done += buf.Length) e.Read(buf, 0, buf.Length);
            var outBuf = new float[8192];
            for (int done = 0; done < outBuf.Length; done += buf.Length)
            {
                e.Read(buf, 0, buf.Length);
                Array.Copy(buf, 0, outBuf, done, buf.Length);
            }
            return outBuf;
        }

        // Hard left, so one channel carries the whole voice at its nominal amplitude.
        var rendered = RenderOne(e => e.SetVoice(0, 440, 0.5f, -1f, "sine", true, 10));

        float peak = 0f;
        for (int i = 0; i < rendered.Length; i += 2) peak = Math.Max(peak, Math.Abs(rendered[i]));

        // 0.5 amplitude through an equal-power pan hard left is full amplitude on the left channel.
        Assert.InRange(peak, 0.49f, 0.51f);
    }
}
