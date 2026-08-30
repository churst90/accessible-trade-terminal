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

    private static ChartSeries LineSeries(string id)
    {
        var cfg = new SeriesConfig { Id = id, Name = id, IndicatorCode = id.ToUpperInvariant(), Pane = "Main" };
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
    private static Ohlcv SonifiedBar(string seriesId, bool heikinAshi)
    {
        var recorder = new RecordingStrategy();
        var sonifier = new NavigationSonifier(new SilentDriver(), recorder, new SoundPatchRegistry());
        var series = LineSeries(seriesId);

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
            PaneRanges = ImmutableDictionary<string, (double Min, double Max)>.Empty.Add("Main", (0, 200)),
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
            int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, float chartVolume, double? prevVal = null)
        {
            LastPoint = point;
            return new AudioPoint(comp.BaseFrequency, 1.0f, comp.Waveform, 0.0, "Sustain");
        }

        public AudioPoint MapToAudio(ChartSeries series, int dataIndex, List<Ohlcv> data, int relativeIndex,
            int viewportWidth, (double Min, double Max) viewportRange, float chartVolume)
            => new(440, 1, "sine", 0, "Sustain");

        public AudioPoint MapComponentToAudio(ChartSeries series, int componentIndex, int dataIndex, List<Ohlcv> data,
            int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, float chartVolume)
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
