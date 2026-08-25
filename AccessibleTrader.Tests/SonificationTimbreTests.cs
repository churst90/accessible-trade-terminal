using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// The audio design rules, asserted for every built-in voice.
///
/// <para>
/// This file exists because of what the wick defects turned out to be. Both were regressions of a
/// kind nothing could catch: a renamed field left the sonifier's idea of a component's identity
/// pointing at nothing, and a wrong denominator flattened a whole dimension of the output to
/// silence. Neither threw, neither failed a build, and neither was visible in any test — the audio
/// surface had no assertions about what anything is supposed to SOUND like, so it survived every
/// release until a user noticed by ear. See <see cref="WickSonificationTests"/> for those two.
/// </para>
///
/// <para>
/// The rules below are the ones the code and <see cref="SonificationProfileProvider"/> state
/// explicitly, so a future rename or refactor has to break a test rather than a user's ears.
/// Components are built from the real profile provider rather than by hand, for the same reason.
/// </para>
/// </summary>
public sealed class SonificationTimbreTests
{
    private static ComponentConfig Component(
        ComponentDisplayType type, ComponentRole role, string name = "c", string dataMapping = "close")
    {
        var p = new SonificationProfileProvider().GetProfile(type, role, name);
        return new ComponentConfig
        {
            Name = name,
            DisplayName = name,
            DisplayType = type,
            Role = role,
            DataMapping = dataMapping,
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

    private static ChartSeries Series(string id = "candles") =>
        new(new SeriesConfig { Id = id, Name = id }, new SeriesDataBuffer { SeriesId = id });

    private static AudioPoint Play(
        ComponentConfig comp, Ohlcv bar, double val,
        (double Min, double Max)? range = null, ChartSeries? series = null)
        => new DefaultSonificationStrategy(new SoundPatchRegistry())
            .CreateAudioPoint(series ?? Series(), comp, val, bar,
                relativeIndex: 5, viewportWidth: 20,
                viewportRange: range ?? (0.0, 100.0), chartVolume: 1f);

    private static Ohlcv Bar(double o, double h, double l, double c, double v = 1000)
        => new(default, o, h, l, c, v);

    // ── The candle body ─────────────────────────────────────────────────────────────

    /// <summary>
    /// "Loudness never encodes size." A doji and a marubozu are equally present and differ only in
    /// character — that is what lets grit mean size without the chart also getting quieter.
    /// </summary>
    [Fact]
    public void BodyLoudnessIsConstantRegardlessOfBodySize()
    {
        var body = Component(ComponentDisplayType.Candle, ComponentRole.Body);

        var doji = Play(body, Bar(50, 55, 45, 50), 50);
        var marubozu = Play(body, Bar(45, 55, 45, 55), 55);

        Assert.Equal(doji.Volume, marubozu.Volume, 4);
    }

    /// <summary>Body size reads as sub-octave weight, normalised by the bar's own range.</summary>
    [Fact]
    public void BodyGritGrowsWithBodySize()
    {
        var body = Component(ComponentDisplayType.Candle, ComponentRole.Body);

        var doji = Play(body, Bar(50, 55, 45, 50), 50);        // body 0 of range 10
        var half = Play(body, Bar(48, 55, 45, 53), 53);        // body 5 of range 10
        var full = Play(body, Bar(45, 55, 45, 55), 55);        // body 10 of range 10

        Assert.Equal(0f, doji.SubSawMix);
        Assert.True(half.SubSawMix > doji.SubSawMix && full.SubSawMix > half.SubSawMix,
            $"grit must rise with body size; got {doji.SubSawMix} / {half.SubSawMix} / {full.SubSawMix}");
    }

    /// <summary>Direction reads by pitch AND by a slight colour shift, so it survives either cue.</summary>
    [Fact]
    public void BodyDirectionReadsByPitchAndColour()
    {
        var body = Component(ComponentDisplayType.Candle, ComponentRole.Body);

        var up = Play(body, Bar(46, 55, 45, 54), 54);
        var down = Play(body, Bar(54, 55, 45, 46), 46);

        Assert.True(up.Frequency > down.Frequency,
            $"an up bar must be the higher tone; got {up.Frequency} vs {down.Frequency}");
        Assert.True(up.SquareMix > down.SquareMix, "an up bar is a hair brighter");
        Assert.True(down.TriangleMix > up.TriangleMix, "a down bar is a hair warmer");
    }

    // ── Volume ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Same rule as the body: a quiet bar must stay clearly audible rather than dropping toward
    /// silence, with intensity carried by texture instead.
    /// </summary>
    [Fact]
    public void VolumeLoudnessIsConstantAndIntensityIsTexture()
    {
        var vol = Component(ComponentDisplayType.Bar, ComponentRole.Volume, "Volume", "volume");

        var quiet = Play(vol, Bar(50, 51, 49, 50, v: 5), 5);
        var heavy = Play(vol, Bar(50, 51, 49, 50, v: 95), 95);

        Assert.Equal(quiet.Volume, heavy.Volume, 4);
        Assert.True(heavy.SubSawMix > quiet.SubSawMix,
            $"a big volume bar must carry more weight; got {quiet.SubSawMix} vs {heavy.SubSawMix}");
    }

    /// <summary>The volume bed is brown-tinged — that is what separates it from the body.</summary>
    [Fact]
    public void VolumeCarriesABrownNoiseTinge()
    {
        var vol = Component(ComponentDisplayType.Bar, ComponentRole.Volume, "Volume", "volume");

        var point = Play(vol, Bar(50, 51, 49, 50, v: 50), 50);

        Assert.True(point.NoiseAmount > 0f, "the volume bed carries a noise tinge");
        Assert.Equal("brown", point.NoiseType);
    }

    // ── Oscillators ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Above the visible midpoint is bright, below is warm, so the zone is audible without a
    /// number. The split is on the MIDPOINT rather than a reference level because many oscillators
    /// leave that unset.
    /// </summary>
    [Fact]
    public void OscillatorZonesAreBrightAboveAndWarmBelow()
    {
        var osc = Component(ComponentDisplayType.Oscillator, ComponentRole.None, "RSI");

        var high = Play(osc, Bar(50, 51, 49, 50), 80, range: (0.0, 100.0));
        var low = Play(osc, Bar(50, 51, 49, 50), 20, range: (0.0, 100.0));

        Assert.True(high.SquareMix > low.SquareMix, "the upper zone is the brighter one");
        Assert.True(low.TriangleMix > high.TriangleMix, "the lower zone is the warmer one");
    }

    /// <summary>
    /// "Oscillator zones use triangle (warm) / square (bright), never sawtooth." A same-octave saw
    /// fizzes harshly, which is exactly what a continuously-sounding voice must not do.
    /// </summary>
    [Fact]
    public void OscillatorsNeverUseSawtooth()
    {
        var osc = Component(ComponentDisplayType.Oscillator, ComponentRole.None, "RSI");

        foreach (double v in new[] { 5.0, 25.0, 50.0, 75.0, 95.0 })
        {
            var point = Play(osc, Bar(50, 51, 49, 50), v, range: (0.0, 100.0));
            Assert.Equal(0f, point.SawMix);
        }
    }

    [Fact]
    public void OscillatorPitchTracksItsValue()
    {
        var osc = Component(ComponentDisplayType.Oscillator, ComponentRole.None, "RSI");

        var low = Play(osc, Bar(50, 51, 49, 50), 10, range: (0.0, 100.0));
        var high = Play(osc, Bar(50, 51, 49, 50), 90, range: (0.0, 100.0));

        Assert.True(high.Frequency > low.Frequency,
            $"a rising oscillator must rise in pitch; got {low.Frequency} vs {high.Frequency}");
    }

    // ── Histogram ───────────────────────────────────────────────────────────────────

    [Fact]
    public void HistogramGritGrowsWithMagnitude()
    {
        var hist = Component(ComponentDisplayType.Histogram, ComponentRole.Histogram, "MACD hist");

        var small = Play(hist, Bar(50, 51, 49, 50), 5, range: (-100.0, 100.0));
        var large = Play(hist, Bar(50, 51, 49, 50), 95, range: (-100.0, 100.0));

        Assert.True(large.SubSawMix > small.SubSawMix,
            $"histogram weight must track magnitude; got {small.SubSawMix} vs {large.SubSawMix}");
    }

    /// <summary>
    /// The histogram and the volume bed sound during playback at the same time, so their fixed
    /// character has to differ or they blur into one instrument.
    /// </summary>
    [Fact]
    public void TheHistogramAndTheVolumeBedAreDistinctInstruments()
    {
        var hist = Component(ComponentDisplayType.Histogram, ComponentRole.Histogram, "MACD hist");
        var vol = Component(ComponentDisplayType.Bar, ComponentRole.Volume, "Volume", "volume");

        var h = Play(hist, Bar(50, 51, 49, 50), 50, range: (-100.0, 100.0));
        var v = Play(vol, Bar(50, 51, 49, 50, v: 50), 50);

        Assert.NotEqual(h.SquareMix, v.SquareMix);
        Assert.True(v.NoiseAmount > h.NoiseAmount, "only the volume bed is brown-tinged");
    }

    // ── The price line ──────────────────────────────────────────────────────────────

    /// <summary>
    /// "No saw — the line is a single continuous voice; grit would imply a second note." The line
    /// is the one voice that must never acquire weight, because there is no size for it to encode.
    /// </summary>
    [Fact]
    public void ThePriceLineIsWarmAndNeverGritty()
    {
        var line = Component(ComponentDisplayType.Line, ComponentRole.PriceAction, "Price");

        var point = Play(line, Bar(50, 51, 49, 50), 50);

        Assert.Equal(0f, point.SawMix);
        Assert.Equal(0f, point.SubSawMix);
        Assert.True(point.TriangleMix > 0f && point.SquareMix > 0f, "the line is warm with definition");
    }

    // ── Cross-cutting rules ─────────────────────────────────────────────────────────

    /// <summary>
    /// Muting is absolute. A muted component that still emitted a partial would be a voice the
    /// user switched off and can still hear.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AMutedOrHiddenComponentIsSilent(bool muted, bool hidden)
    {
        var body = Component(ComponentDisplayType.Candle, ComponentRole.Body);
        body.IsMuted = muted;
        body.IsVisible = !hidden;

        Assert.Equal(0f, Play(body, Bar(45, 55, 45, 55), 55).Volume);
    }

    /// <summary>
    /// A component's built-in timbre is the fallback, not an overlay: a user patch carries its own
    /// character, and mixing the built-in partials on top would mean the patch never sounds the way
    /// it did in the Sound Designer.
    /// </summary>
    [Fact]
    public void AUserPatchOptsOutOfTheBuiltInPartials()
    {
        var body = Component(ComponentDisplayType.Candle, ComponentRole.Body);
        var registry = new SoundPatchRegistry();
        var builtIn = Play(body, Bar(45, 55, 45, 55), 55);
        Assert.True(builtIn.SubSawMix > 0f, "precondition: this candle is gritty without a patch");

        body.SoundPatchId = registry.GetPatchIds().First();
        var patched = Play(body, Bar(45, 55, 45, 55), 55);

        Assert.Equal(0f, patched.SquareMix);
        Assert.Equal(0f, patched.SawMix);
        Assert.Equal(0f, patched.TriangleMix);
        Assert.Equal(0f, patched.SubSawMix);
    }

    /// <summary>
    /// A NaN value is a gap in the data, not a note. Sonifying one would put a tone where the
    /// indicator has nothing to say — usually its warm-up period.
    /// </summary>
    [Fact]
    public void ANaNValueIsSilent()
    {
        var osc = Component(ComponentDisplayType.Oscillator, ComponentRole.None, "RSI");

        Assert.Equal(0f, Play(osc, Bar(50, 51, 49, 50), double.NaN).Volume);
    }
}
