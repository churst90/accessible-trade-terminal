using System.Collections.Immutable;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// What a drawing sounds like when the chart is played.
///
/// <para><b>The defect, reported from real use on 2026-09-03:</b> "during chart playback via
/// space / ctrl shift space / shift space, trend lines do not sonify, only by moving along the x
/// manually with the arrows." Two independent filters removed them — one in
/// <c>PlaybackPlan.Resolve</c>'s chart scope, one in <c>AudioSequencer.BuildVoicePlan</c> — so
/// the one place a line's shape against price is worth hearing was the one place it was silent.
/// The tests for the two filters are with the code they guard; this file is about the SOUND that
/// results, which neither of them can see.</para>
///
/// <para>The second half matters as much as the first. A trend line covers part of the chart and
/// NaN elsewhere; a component that is simply skipped on those bars leaves its voice running,
/// because the sequencer's voices are continuous and glide. That was survivable while every
/// sonified array was an indicator with a short warm-up at the very start; a drawing has NaN on
/// both sides of a span that sits anywhere, so the voice would sweep in and out of 0 Hz twice a
/// pass.</para>
/// </summary>
public sealed class DrawingPlaybackVoiceTests
{
    private sealed record Fired(int Slot, int DataIndex, double Frequency, float Volume, float NoiseAmount, string NoiseType);

    private sealed class SpyDriver : IAudioDriver
    {
        public List<Fired> Voiced { get; } = new();
        public List<int> Stopped { get; } = new();
        public int SampleRate => 44100;
        public int Channels => 2;
        public event Action<int>? PointReached { add { } remove { } }
        public void SetVoice(int slot, double frequency, float volume, float pan, string waveform,
            bool continuous, double durationSeconds = 0.2, int dataIndex = -1, string envelope = "Sustain",
            bool click = false, float noiseAmount = 0f, string noiseType = "pink", float squareMix = 0f,
            float sawMix = 0f, float triangleMix = 0f, float subSawMix = 0f)
        {
            lock (Voiced) Voiced.Add(new Fired(slot, dataIndex, frequency, volume, noiseAmount, noiseType));
        }
        public void StopVoice(int slot) { lock (Stopped) Stopped.Add(slot); }
        public void StopAll() { }
        public void Reset() { }
        public void SetMasterGain(float gain) { }
        public void Pause() { }
        public void Resume() { }
    }

    private const int BarCount = 40;
    private const int SpanFrom = 10;
    private const int SpanTo = 25;

    private static List<Ohlcv> Bars()
    {
        var list = new List<Ohlcv>(BarCount);
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < BarCount; i++) list.Add(new Ohlcv(t.AddHours(i), 100, 102, 98, 101, 1000));
        return list;
    }

    /// <summary>A trend line whose "Line" array holds values on bars 10-25 and NaN everywhere
    /// else — the shape <c>CalculateLinearPoints</c> produces for a line anchored inside the
    /// chart with neither end extended.</summary>
    private static ChartSeries TrendLineSeries()
    {
        var cfg = new SeriesConfig
        {
            Id = "trend", Name = "Trend line (1)", FriendlyName = "TrendLine Drawing",
            IndicatorCode = "DRAWING", Pane = "Main", IsVisible = true, Volume = 1f,
        };
        cfg.Components.Add(new ComponentConfig
        {
            Name = "Line", DisplayName = "Line", DisplayType = ComponentDisplayType.Line,
            IsVisible = true, IsEnabled = true, Volume = 1f, BaseFrequency = 440,
            PitchMapping = PitchMapping.Price, AmplitudeMapping = AmplitudeMapping.None,
            Waveform = "sine", EnvelopeType = "Sustain",
        });

        var buf = new SeriesDataBuffer { SeriesId = "trend" };
        var arr = new double[BarCount];
        Array.Fill(arr, double.NaN);
        for (int i = SpanFrom; i <= SpanTo; i++) arr[i] = 95 + i * 0.5;
        buf.ComponentData["Line"] = arr;

        var series = new ChartSeries(cfg, buf)
        {
            Drawing = new DrawingData
            {
                Type = DrawingType.TrendLine,
                AnchorDate1 = default, AnchorPrice1 = 100,
                AnchorDate2 = default, AnchorPrice2 = 107,
            }
        };
        return series;
    }

    private static async Task<SpyDriver> PlayWholeChartAsync(params ChartSeries[] seriesList)
    {
        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial with
        {
            ActiveSeries = ImmutableList.CreateRange(seriesList),
            ViewportStartIndex = 0,
            ViewportLength = BarCount,
            ViewportRange = (90, 110),
            PaneRanges = ImmutableDictionary<string, (double Min, double Max)>.Empty.Add("Main", (90, 110)),
            ChartVolume = 1f,
            PlaybackSpeed = 50f,          // fast: this test is about which bars sound, not timing
        });

        var driver = new SpyDriver();
        var sequencer = new AudioSequencer(driver, new DefaultSonificationStrategy(new SoundPatchRegistry()),
            store, new SoundPatchRegistry(), NullLogger<AudioSequencer>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await sequencer.StartMultiSeriesPlaybackAsync(seriesList, Bars(), 0, cts.Token);
        return driver;
    }

    [Fact]
    public async Task A_trend_line_sounds_on_the_bars_it_covers()
    {
        var driver = await PlayWholeChartAsync(TrendLineSeries());

        var audible = driver.Voiced.Where(v => v.Volume > 0f).ToList();
        Assert.NotEmpty(audible);

        // Every bar of the span is heard, and none outside it.
        var heard = audible.Select(v => v.DataIndex).Distinct().OrderBy(i => i).ToList();
        Assert.Equal(Enumerable.Range(SpanFrom, SpanTo - SpanFrom + 1), heard);
    }

    /// <summary>
    /// The bars either side of the span STOP the voice rather than leaving it running. Asserted
    /// as a count against the bars outside the span, not as "StopVoice was called at all" — one
    /// call at the end of playback would satisfy that and prove nothing about the gaps.
    /// </summary>
    [Fact]
    public async Task The_bars_outside_the_span_stop_the_voice_instead_of_gliding_through_them()
    {
        var driver = await PlayWholeChartAsync(TrendLineSeries());

        int barsOutside = BarCount - (SpanTo - SpanFrom + 1);
        Assert.True(driver.Stopped.Count >= barsOutside,
            $"expected at least one StopVoice per bar outside the line's span ({barsOutside}); " +
            $"got {driver.Stopped.Count}");
        Assert.DoesNotContain(driver.Voiced, v => v.DataIndex >= 0 && (v.DataIndex < SpanFrom || v.DataIndex > SpanTo));
    }

    /// <summary>
    /// It is heard AS a drawing: the voice carries the pink roughness that tells a line you drew
    /// from a line an indicator computed. Playing it and playing it distinguishably are separate
    /// claims, and only asserting the first is how a drawing ends up indistinguishable from an
    /// EMA once both are sounding at once.
    /// </summary>
    [Fact]
    public async Task A_trend_line_is_heard_as_a_drawing_not_as_another_line()
    {
        var driver = await PlayWholeChartAsync(TrendLineSeries());

        var audible = driver.Voiced.Where(v => v.Volume > 0f).ToList();
        Assert.All(audible, v =>
        {
            Assert.Equal("pink", v.NoiseType);
            Assert.True(v.NoiseAmount >= DefaultSonificationStrategy.DrawingNoiseAmount,
                $"a drawing's voice carried {v.NoiseAmount} noise");
        });
    }
}
