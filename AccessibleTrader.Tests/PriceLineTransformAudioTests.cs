using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The audio half of the close-line contract: pitch follows the same bar the words do.</b>
///
/// <para>
/// <c>NavigationSonifier.SyncNavigationSlots</c> transformed the bar to Heikin-Ashi for every
/// focused series, the close line included — so with HA on the line's pitch swept to the HA
/// average while <c>SpeechFormatter</c> spoke the raw close. Two halves of one readout on two
/// different numbers, and the pitch is the half a user scanning by ear reads fastest.
/// </para>
///
/// <para>
/// The candles keep the transform: pitch and direction there are meant to track the candle
/// colours on screen, and a Heikin-Ashi candle IS a different candle. That asymmetry is the
/// contract, so both halves of it are pinned here — a fix that simply stopped transforming
/// anything would pass the first test and fail the second.
/// </para>
/// </summary>
public sealed class PriceLineTransformAudioTests
{
    // Bar 0 flat at 90 seeds the transform; bar 1 is the one under test.
    //   raw close  = 108
    //   HA  close  = (100 + 110 + 97.5 + 108) / 4 = 103.875
    private const double RawClose = 108.0;
    private const double HaClose = 103.875;

    private static readonly Ohlcv[] TheBars =
    {
        new(new DateTime(2026, 1, 1), 90, 90, 90, 90, 1),
        new(new DateTime(2026, 1, 2), 100, 110, 97.5, 108, 1),
    };

    private static ChartSeries LineSeries(string id, string pane = "Main")
    {
        var cfg = new SeriesConfig { Id = id, Name = id, IndicatorCode = id.ToUpperInvariant(), Pane = pane };
        cfg.Components.Add(new ComponentConfig
        {
            Name = "line", DisplayName = "Price", IsVisible = true, IsMuted = false,
            DisplayType = ComponentDisplayType.Line, DataMapping = "close",
            BaseFrequency = 440, FreqMultiplier = 1.0, Volume = 1.0f, Waveform = "sine",
        });
        var buf = new SeriesDataBuffer { SeriesId = id, FirstBarDate = TheBars[0].Date };
        buf.ComponentData["line"] = TheBars.Select(b => (double)b.Close).ToArray();
        return new ChartSeries(cfg, buf);
    }

    /// <summary>The bar the sonifier handed the strategy for the focused series.</summary>
    private static Ohlcv SonifiedBar(string seriesId, bool heikinAshi, string pane = "Main")
    {
        var recorder = new RecordingStrategy();
        var sonifier = new NavigationSonifier(new SilentDriver(), recorder, new SoundPatchRegistry());
        var series = LineSeries(seriesId, pane);

        sonifier.SyncNavigationSlots(WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(TheBars),
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = series.Id,
            FocusedSeriesIndex = 0,
            FocusedComponentIndex = 0,
            CurrentDataIndex = 1,
            ViewportStartIndex = 0,
            ViewportLength = 100,
            ChartVolume = 1.0f,
            IsHeikinAshi = heikinAshi,
            PaneRanges = ImmutableDictionary<string, (double Min, double Max)>.Empty.Add("Main", (0, 200)).Add(pane == "Main" ? "_" : pane, (0, 200)),
            ViewportRange = (0, 200),
        });

        Assert.NotNull(recorder.LastPoint);
        return recorder.LastPoint!.Value;
    }

    /// <summary>The headline: the close line is sonified from the raw bar even with HA on.</summary>
    [Fact]
    public void ThePriceLineIsSonifiedFromTheRawBarWithHeikinAshiOn()
    {
        Assert.Equal(RawClose, SonifiedBar(CoreSeriesIds.Price, heikinAshi: true).Close);
    }

    /// <summary>
    /// The control. Same bar, same keypress, a series that IS the candles — that one still
    /// follows the transform, so the test above is a statement about the close line and not
    /// about the transform having been switched off.
    /// </summary>
    [Fact]
    public void TheCandleSeriesIsStillSonifiedFromTheHeikinAshiBar()
    {
        Assert.Equal(HaClose, SonifiedBar(CoreSeriesIds.Candles, heikinAshi: true).Close);
    }

    /// <summary>
    /// <b>A volume bar is heard by the candle it is painted by</b> (Cody, 2026-11-22: two volume
    /// bars on BTC 4h "sound positive but are reported as red"). The renderer paints every pane
    /// below the price pane from the real bars and the colour word is said from the real bar, so
    /// the tone must take the real bar too. Here the real candle is up (100 to 108) and so is the
    /// Heikin-Ashi one, so the CLOSE is what tells the two apart, as in the tests above.
    /// </summary>
    [Fact]
    public void A_series_in_its_own_pane_is_sonified_from_the_real_bar_with_Heikin_Ashi_on()
    {
        Assert.Equal(RawClose, SonifiedBar(CoreSeriesIds.Volume, heikinAshi: true, pane: "Volume").Close);
        // The control: the same series laid over the candles still follows them.
        Assert.Equal(HaClose, SonifiedBar(CoreSeriesIds.Volume, heikinAshi: true, pane: "Main").Close);
    }

    /// <summary>Cody's bars, end to end: a down market candle under an up Heikin-Ashi candle. The
    /// volume tone is the bearish one, the colour the renderer and the words use is the down
    /// colour, and the precondition (the Heikin-Ashi candle IS up) is checked so this is not
    /// vacuous.</summary>
    [Fact]
    public void A_volume_bar_under_a_down_candle_sounds_down_on_a_Heikin_Ashi_chart()
    {
        // A climb from 90 to 120 leaves the Heikin-Ashi open far below the market; the last
        // candle opens at 120 and closes at 116, down, while its Heikin-Ashi candle is still up.
        var t = new DateTime(2026, 11, 22);
        var bars = new List<Ohlcv>();
        for (int i = 0; i < 6; i++) bars.Add(new(t.AddHours(4 * i), 90 + 5 * i, 96 + 5 * i, 89 + 5 * i, 95 + 5 * i, 10));
        bars.Add(new(t.AddHours(24), 120, 121, 115, 116, 10));
        int last = bars.Count - 1;
        var ha = ChartMath.BarAsDrawn(new TimeSeriesBuffer<Ohlcv>(bars), last, true);
        Assert.True(ha.Close >= ha.Open, "precondition: the Heikin-Ashi candle is up");
        Assert.True(bars[last].Close < bars[last].Open, "precondition: the market's candle is down");

        var cfg = new SeriesConfig { Id = CoreSeriesIds.Volume, Name = "Volume", IndicatorCode = "VOLUME", Pane = "Volume" };
        var comp = new ComponentConfig
        {
            Name = "Volume", DisplayName = "Volume", IsVisible = true, Role = ComponentRole.Volume,
            DisplayType = ComponentDisplayType.Histogram, ColorSource = ColorSource.PriceAction,
            PitchMapping = PitchMapping.PriceDirection, BullishFrequency = 880, BearishFrequency = 220,
            BaseFrequency = 440, FreqMultiplier = 1.0, Volume = 1.0f, Waveform = "sine",
        };
        cfg.Components.Add(comp);
        var buf = new SeriesDataBuffer { SeriesId = cfg.Id, FirstBarDate = bars[0].Date };
        buf.ComponentData["Volume"] = bars.Select(b => b.Volume).ToArray();
        var series = new ChartSeries(cfg, buf);

        var heard = new HeardStrategy();
        new NavigationSonifier(new SilentDriver(), heard, new SoundPatchRegistry()).SyncNavigationSlots(WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = series.Id, FocusedSeriesIndex = 0, FocusedComponentIndex = 0,
            CurrentDataIndex = last, ViewportStartIndex = 0, ViewportLength = 100, ChartVolume = 1.0f,
            IsHeikinAshi = true,
            PaneRanges = ImmutableDictionary<string, (double Min, double Max)>.Empty.Add("Volume", (0, 20)),
            ViewportRange = (80, 130),
        });

        Assert.Equal(220, heard.LastFrequency);
        // What the words say for that bar, from the same real bar the renderer paints it by.
        Assert.Equal("red bar", AccessibleTrader.Core.Services.Accessibility.MarkerLook.DescribeBar(null, series, comp, last, bars[last]));
    }

    /// <summary>
    /// The vacuity check: with HA off both series read the same raw bar, so the two tests above
    /// are only meaningful because the transform genuinely changes the number.
    /// </summary>
    [Theory]
    [InlineData(CoreSeriesIds.Price)]
    [InlineData(CoreSeriesIds.Candles)]
    public void WithHeikinAshiOffEverySeriesReadsTheRawBar(string seriesId)
    {
        Assert.Equal(RawClose, SonifiedBar(seriesId, heikinAshi: false).Close);
    }

    // ── Stubs ───────────────────────────────────────────────────────────────────

    /// <summary>Records the bar it was handed; the AudioPoint itself is irrelevant here.</summary>
    private sealed class RecordingStrategy : ISonificationStrategy
    {
        public Ohlcv? LastPoint { get; private set; }

        public AudioPoint CreateAudioPoint(ChartSeries series, ComponentConfig comp, double val, Ohlcv point,
            int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, float chartVolume, double? prevVal = null, bool isLogScale = false, int dataIndex = -1)
        {
            LastPoint = point;
            return new AudioPoint(comp.BaseFrequency, 1.0f, comp.Waveform, 0.0, "Sustain");
        }

        public AudioPoint MapToAudio(ChartSeries series, int dataIndex, List<Ohlcv> data, int relativeIndex,
            int viewportWidth, (double Min, double Max) viewportRange, float chartVolume, bool isLogScale = false)
            => new(440, 1, "sine", 0, "Sustain");

        public AudioPoint MapComponentToAudio(ChartSeries series, int componentIndex, int dataIndex, List<Ohlcv> data,
            int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, float chartVolume, bool isLogScale = false)
            => new(440, 1, "sine", 0, "Sustain");

        public int ResolveComponentVoiceCount(ComponentConfig comp) => 1;
    }

    /// <summary>The real strategy, keeping the frequency it chose.</summary>
    private sealed class HeardStrategy : ISonificationStrategy
    {
        private readonly DefaultSonificationStrategy _real = new(new SoundPatchRegistry());
        public double LastFrequency { get; private set; }

        public AudioPoint CreateAudioPoint(ChartSeries series, ComponentConfig comp, double val, Ohlcv point,
            int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, float chartVolume, double? prevVal = null, bool isLogScale = false, int dataIndex = -1)
        {
            var p = _real.CreateAudioPoint(series, comp, val, point, relativeIndex, viewportWidth, viewportRange, chartVolume, prevVal, isLogScale, dataIndex);
            LastFrequency = p.Frequency;
            return p;
        }

        public AudioPoint MapToAudio(ChartSeries series, int dataIndex, List<Ohlcv> data, int relativeIndex,
            int viewportWidth, (double Min, double Max) viewportRange, float chartVolume, bool isLogScale = false)
            => new(440, 1, "sine", 0, "Sustain");

        public AudioPoint MapComponentToAudio(ChartSeries series, int componentIndex, int dataIndex, List<Ohlcv> data,
            int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, float chartVolume, bool isLogScale = false)
            => new(440, 1, "sine", 0, "Sustain");

        public int ResolveComponentVoiceCount(ComponentConfig comp) => 1;
    }

    private sealed class SilentDriver : IAudioDriver
    {
        public int SampleRate => 48000;
        public int Channels => 2;
#pragma warning disable CS0067
        public event Action<int>? PointReached;
#pragma warning restore CS0067

        public void SetVoice(int slot, double frequency, float volume, float pan, string waveform,
            bool continuous, double durationSeconds = 0.2, int dataIndex = -1, string envelope = "Sustain",
            bool click = false, float noiseAmount = 0f, string noiseType = "pink", float squareMix = 0f,
            float sawMix = 0f, float triangleMix = 0f, float subSawMix = 0f) { }

        public void StopVoice(int slot) { }
        public void StopAll() { }
        public void Reset() { }
        public void SetMasterGain(float gain) { }
        public void Pause() { }
        public void Resume() { }
    }
}
