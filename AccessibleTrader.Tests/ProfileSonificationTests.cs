using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A profile bin sounds like a bar</b> (Cody, 2026-11-02: "the aplitude/volume of the sound is
/// tied to the length of the bars but as with histograms, the size of the bars in the profile
/// should have associated grittiness"). Loudness is constant, size is the sub-octave grit against
/// the biggest bin, pitch is the bin's price, the value area is warm and the POC clicks. The
/// heatmap, the other distribution series, follows the same size rule against its column.
///
/// <para>Until then a profile bin's loudness ran 0.15 to 1.0 with its size and its pitch was its
/// node type (POC 880, HVN 660, LVN 220…), so two bins of one type were the same note wherever
/// they sat.</para>
/// </summary>
public sealed class ProfileSonificationTests
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

    private static ProfileBin Bin(double low, double vol, bool va = false, bool poc = false)
        => new() { PriceLow = low, PriceHigh = low + 1, TotalVolume = vol, TpoPeriodCount = 0, IsPOC = poc, IsValueArea = va };

    // Bottom to top: a thin bin, the value area (its middle bin the POC), another thin bin.
    private static readonly List<ProfileBin> Bins = new()
    {
        Bin(100, 100),
        Bin(101, 500, va: true),
        Bin(102, 1000, va: true, poc: true),
        Bin(103, 250, va: true),
        Bin(104, 100),
    };

    private static (NavigationSonifier, Spy) Sonifier()
    {
        var spy = new Spy();
        return (new NavigationSonifier(spy, new DefaultSonificationStrategy(new SoundPatchRegistry()), new SoundPatchRegistry()), spy);
    }

    private static ChartSeries Profile()
    {
        var cfg = new SeriesConfig { Id = "vp", Name = "vp", Pane = "Main", IsVisible = true, Volume = 1f };
        cfg.Components.Add(new ComponentConfig { Name = "profile", DisplayType = ComponentDisplayType.Profile, IsVisible = true });
        return new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = "vp", ProfileBins = Bins });
    }

    private static List<(int Slot, double Freq, float Vol, string Wave, double Dur, string Env, bool Click, float Square, float Triangle, float SubSaw)> PlayAll()
    {
        var (sonifier, spy) = Sonifier();
        var series = Profile();
        for (int i = 0; i < Bins.Count; i++) sonifier.SonifyProfile(series, i, 1f);
        return spy.Calls.Where(c => c.Slot == 0).ToList();
    }

    [Fact]
    public void Every_bin_is_equally_loud_whatever_its_size()
    {
        var calls = PlayAll();
        Assert.Equal(Bins.Count, calls.Count);
        Assert.All(calls, c => Assert.Equal(1f, c.Vol));
    }

    [Fact]
    public void Grit_is_the_bins_size_against_the_biggest_bin()
    {
        var calls = PlayAll();
        Assert.Equal(0.30f, calls[2].SubSaw, 3);   // the POC, the biggest
        Assert.Equal(0.15f, calls[1].SubSaw, 3);   // half its size
        Assert.Equal(0.075f, calls[3].SubSaw, 3);  // a quarter
        Assert.Equal(0.03f, calls[0].SubSaw, 3);   // a tenth: nearly clean
    }

    [Fact]
    public void Pitch_climbs_with_price_from_220_to_880_and_two_bins_of_one_size_differ()
    {
        var calls = PlayAll();
        Assert.Equal(220.0, calls[0].Freq, 6);
        Assert.Equal(880.0, calls[^1].Freq, 6);
        Assert.Equal(440.0, calls[2].Freq, 6);     // the middle bin, an octave up
        for (int i = 1; i < calls.Count; i++) Assert.True(calls[i].Freq > calls[i - 1].Freq, $"bin {i} is not higher than bin {i - 1}");
        // Bins 0 and 4 are the same size and the same node type: under the old rule they were one note.
        Assert.NotEqual(calls[0].Freq, calls[4].Freq);
    }

    [Fact]
    public void The_value_area_is_warm_and_only_the_POC_clicks()
    {
        var calls = PlayAll();
        for (int i = 0; i < Bins.Count; i++)
        {
            Assert.Equal(Bins[i].IsValueArea ? 0.12f : 0f, calls[i].Triangle);
            Assert.Equal(Bins[i].IsPOC, calls[i].Click);
            Assert.Equal(0.10f, calls[i].Square);
            Assert.Equal("sine", calls[i].Wave);
            Assert.Equal("Ping", calls[i].Env);
            Assert.Equal(0.40, calls[i].Dur);
        }
    }

    [Fact]
    public void A_bin_with_no_volume_reads_clean_rather_than_breaking_the_rest()
    {
        var bins = new List<ProfileBin> { Bin(100, double.NaN), Bin(101, 400), Bin(102, 800) };
        var nan = NavigationSonifier.ProfileBinVoice(bins, 0, 1f);
        Assert.Equal(0f, nan.SubSawMix);
        Assert.Equal(0.30f, NavigationSonifier.ProfileBinVoice(bins, 2, 1f).SubSawMix, 3);
        Assert.Equal(0.15f, NavigationSonifier.ProfileBinVoice(bins, 1, 1f).SubSawMix, 3);
    }

    [Fact]
    public void A_heatmap_bin_is_equally_loud_and_its_size_is_grit_against_its_column()
    {
        var cfg = new SeriesConfig { Id = "hm", Name = "hm", Pane = "Main", IsVisible = true, Volume = 1f };
        cfg.Components.Add(new ComponentConfig { Name = "heat", DisplayType = ComponentDisplayType.Heatmap, IsVisible = true });
        var series = new ChartSeries(cfg, new SeriesDataBuffer
        {
            SeriesId = "hm",
            HeatmapData = new List<List<ProfileBin>> { new() { Bin(100, 200), Bin(101, 800), Bin(102, 400) } },
        });
        var (sonifier, spy) = Sonifier();
        for (int i = 0; i < 3; i++) sonifier.SonifyHeatmap(series, 0, i, 1f);
        var calls = spy.Calls.Where(c => c.Slot == 0).ToList();
        Assert.All(calls, c => Assert.Equal(1f, c.Vol));
        Assert.Equal(new[] { 0.075f, 0.30f, 0.15f }, calls.Select(c => (float)Math.Round(c.SubSaw, 3)));
    }
}
