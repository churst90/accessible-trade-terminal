using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The profile's waveform says which side leads the row</b> (Cody, 2026-11-18): a triangle when
/// buying leads, a sine when selling leads, the split is even, or there is no split. That is the
/// oscillator rule (above zero triangle, below sine) applied to the split. The accepted
/// 2026-11-03 sound is otherwise untouched: pitch by part, grit by size, constant loudness.
/// </summary>
public sealed class ProfileSplitSonificationTests
{
    private sealed class Spy : IAudioDriver
    {
        public List<(int Slot, double Freq, float Vol, string Wave, double Dur, string Env, bool Click, float Square, float Triangle, float SubSaw)> Calls { get; } = new();
        public int SampleRate => 44100;
        public int Channels => 2;
        public event Action<int>? PointReached { add { } remove { } }
        public void SetVoice(int slot, double frequency, float volume, float pan, string waveform,
            bool continuous, double durationSeconds = 0.2, int dataIndex = -1, string envelope = "Sustain",
            bool click = false, float noiseAmount = 0f, string noiseType = "pink", float squareMix = 0f,
            float sawMix = 0f, float triangleMix = 0f, float subSawMix = 0f)
            => Calls.Add((slot, frequency, volume, waveform, durationSeconds, envelope, click, squareMix, triangleMix, subSawMix));
        public void StopVoice(int slot) { }
        public void StopAll() { }
        public void Reset() { }
        public void SetMasterGain(float gain) { }
        public void Pause() { }
        public void Resume() { }
    }

    private static ProfileBin Bin(double low, double vol, double buy, double sell,
        bool va = false, bool poc = false)
        => new()
        {
            PriceLow = low, PriceHigh = low + 1, TotalVolume = vol,
            BuyVolume = buy, SellVolume = sell, TpoPeriodCount = 0,
            IsPOC = poc, IsValueArea = va,
        };

    private static readonly List<ProfileBin> Mixed = new()
    {
        Bin(100, 100, 60, 40),             // buying leads
        Bin(101, 100, 40, 60),             // selling leads
        Bin(102, 100, 50, 50),             // even
        Bin(103, 100, 0, 0),               // no split (TPO / no volume)
        Bin(104, 1000, 700, 300, poc: true), // buying-led POC chime
    };

    private static (NavigationSonifier Sonifier, Spy Spy) Sonifier()
    {
        var spy = new Spy();
        return (new NavigationSonifier(spy, new DefaultSonificationStrategy(new SoundPatchRegistry()),
            new SoundPatchRegistry()), spy);
    }

    private static ChartSeries Profile(IEnumerable<ProfileBin> bins)
    {
        var cfg = new SeriesConfig { Id = "vp", Name = "vp", Pane = "Main", IsVisible = true };
        cfg.Components.Add(new ComponentConfig { Name = "profile", DisplayType = ComponentDisplayType.Profile, IsVisible = true });
        return new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = "vp", ProfileBins = bins.ToList() });
    }

    private static List<(int Slot, double Freq, float Vol, string Wave, double Dur, string Env, bool Click, float Square, float Triangle, float SubSaw)>
        Play(int bin)
    {
        var (sonifier, spy) = Sonifier();
        sonifier.SonifyProfile(Profile(Mixed), bin, 1f);
        return spy.Calls.Where(c => c.Slot == 0).ToList();
    }

    [Fact]
    public void Buying_leading_the_row_is_a_triangle()
    {
        Assert.All(Play(0), c => Assert.Equal("triangle", c.Wave));
    }

    [Fact]
    public void Selling_leading_the_row_is_a_sine()
    {
        Assert.All(Play(1), c => Assert.Equal("sine", c.Wave));
    }

    [Fact]
    public void An_even_split_is_a_sine()
    {
        Assert.All(Play(2), c => Assert.Equal("sine", c.Wave));
    }

    [Fact]
    public void No_split_is_a_sine()
    {
        Assert.All(Play(3), c => Assert.Equal("sine", c.Wave));
    }

    [Fact]
    public void The_POC_chime_takes_the_waveform_of_its_rows_leader_too()
    {
        var notes = NavigationSonifier.ProfileBinNotes(Mixed, 4, 1f);
        Assert.Equal(3, notes.Count);
        Assert.All(notes, n => Assert.Equal("triangle", n.Voice.Waveform));
    }

    [Fact]
    public void The_waveform_changes_and_pitch_and_grit_do_not()
    {
        var bins = new List<ProfileBin> { Bin(100, 400, 60, 40), Bin(101, 400, 40, 60) };
        var buying = NavigationSonifier.ProfileBinNotes(bins, 0, 1f)[0].Voice;
        var selling = NavigationSonifier.ProfileBinNotes(bins, 1, 1f)[0].Voice;

        Assert.Equal(NavigationSonifier.ProfileOutsideHz, buying.Frequency);
        Assert.Equal(selling.Frequency, buying.Frequency);
        Assert.Equal(selling.SubSawMix, buying.SubSawMix, 6);
        Assert.Equal(selling.Volume, buying.Volume, 6);
        Assert.Equal("triangle", buying.Waveform);
        Assert.Equal("sine", selling.Waveform);
    }
}
