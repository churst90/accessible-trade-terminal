using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// Reported from live use: "are we sure both wicks independently sound different on each candle
/// if they are genuinely different sizes? Right now they are the same tone."
///
/// <para>
/// The audio design says a wick's LENGTH is carried by sub-octave grit and its SIDE by pitch —
/// upper 880 Hz, lower 220 Hz — so a candle's two wicks should be separable by ear and each
/// should be rough in proportion to its own length. These tests use the real component
/// definitions from <see cref="CoreIndicatorProvider"/> rather than hand-built ones, because
/// both defects here came from the sonifier's idea of a wick's identity drifting from what the
/// indicator actually names it.
/// </para>
/// </summary>
public sealed class WickSonificationTests
{
    private static (ComponentConfig upper, ComponentConfig lower) RealWickComponents()
    {
        var candles = new CoreIndicatorProvider().GetIndicators().First(i => i.Code == "CANDLES");
        ComponentConfig From(string name)
        {
            var m = candles.Components.First(c => c.Name == name);
            return new ComponentConfig
            {
                Name = m.Name,
                DisplayName = m.DisplayName ?? m.Name,
                DisplayType = m.DisplayType,
                Role = m.Role,
                DataMapping = m.DataMapping ?? "",
                IsVisible = true,
                IsEnabled = true,
                Volume = 1f,
                FreqMultiplier = 1f,
                Waveform = "sine",
            };
        }
        return (From("upper_wick"), From("lower_wick"));
    }

    private static AudioPoint Sonify(ComponentConfig comp, Ohlcv bar, double val)
        => new DefaultSonificationStrategy(new SoundPatchRegistry())
            .CreateAudioPoint(
                new ChartSeries(new SeriesConfig { Id = "candles", Name = "Candles" },
                    new SeriesDataBuffer { SeriesId = "candles" }),
                comp, val, bar,
                relativeIndex: 5, viewportWidth: 20,
                viewportRange: (90.0, 110.0), chartVolume: 1f);

    /// <summary>
    /// A candle with a long upper wick and no lower one. The two components must not produce the
    /// same pitch — that is the only cue for which end of the candle you are hearing.
    /// </summary>
    [Fact]
    public void UpperAndLowerWicksAreDifferentPitches()
    {
        var (upper, lower) = RealWickComponents();
        var bar = new Ohlcv(default, Open: 100, High: 108, Low: 100, Close: 101, Volume: 10);

        var up = Sonify(upper, bar, bar.High);
        var down = Sonify(lower, bar, bar.Low);

        Assert.True(up.Frequency > down.Frequency,
            $"upper wick must be the brighter tone; got upper {up.Frequency} Hz, lower {down.Frequency} Hz");
    }

    /// <summary>
    /// Each wick's grit must track ITS OWN length. On this candle the upper wick is long and the
    /// lower one does not exist, so the upper must be audibly rough and the lower perfectly clean.
    /// </summary>
    [Fact]
    public void EachWicksGritTracksItsOwnLength()
    {
        var (upper, lower) = RealWickComponents();
        var bar = new Ohlcv(default, Open: 100, High: 108, Low: 100, Close: 101, Volume: 10);

        var up = Sonify(upper, bar, bar.High);
        var down = Sonify(lower, bar, bar.Low);

        Assert.True(down.SubSawMix == 0f,
            $"a wick that does not exist must be a clean ping, got grit {down.SubSawMix}");
        Assert.True(up.SubSawMix > 0.15f,
            $"a wick spanning most of the bar must be clearly rough, got grit {up.SubSawMix}");
    }

    /// <summary>
    /// The mirror image, so neither side is special-cased into working.
    /// </summary>
    [Fact]
    public void ALongLowerWickIsRoughAndAMissingUpperOneIsClean()
    {
        var (upper, lower) = RealWickComponents();
        var bar = new Ohlcv(default, Open: 107, High: 108, Low: 100, Close: 108, Volume: 10);

        var up = Sonify(upper, bar, bar.High);
        var down = Sonify(lower, bar, bar.Low);

        Assert.True(up.SubSawMix == 0f,
            $"a wick that does not exist must be a clean ping, got grit {up.SubSawMix}");
        Assert.True(down.SubSawMix > 0.15f,
            $"a wick spanning most of the bar must be clearly rough, got grit {down.SubSawMix}");
    }

    /// <summary>
    /// Two wicks of genuinely different length on the SAME candle must not sound alike — this is
    /// the question as the user asked it.
    /// </summary>
    [Fact]
    public void TwoWicksOfDifferentLengthOnOneCandleDiffer()
    {
        var (upper, lower) = RealWickComponents();
        // Upper wick 6, lower wick 1 — a six-to-one difference.
        var bar = new Ohlcv(default, Open: 101, High: 108, Low: 100, Close: 102, Volume: 10);

        var up = Sonify(upper, bar, bar.High);
        var down = Sonify(lower, bar, bar.Low);

        Assert.True(up.SubSawMix - down.SubSawMix > 0.1f,
            $"a 6:1 length difference must be audible; got upper {up.SubSawMix}, lower {down.SubSawMix}");
    }
}
