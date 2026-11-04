using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A profile bin's pitch is its part of the profile, its grit is its size</b> (Cody, 2026-11-03:
/// "I like the sine wave pitches for the different ranges as it is easy to spot kind of like before.
/// i.e. value area has a certain pitch with the length texturing, poc has it's own chime tone with
/// texturing, lvn's hvn's and bars with no data should be a low tone"). Every bin is a sine and equally
/// loud. The heatmap follows the same size rule against its column.
///
/// <para>Two tries the same day were rejected: d8533b2f (pitch = price, one sound for every type) and
/// babe4449 (pitch = price, each type a shape: chord, octave, rising and falling edges, tick).</para>
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
        public List<int> Stopped { get; } = new();
        public void StopVoice(int slot) => Stopped.Add(slot);
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

    // Bottom to top: no data (NaN), no data (zero), LVN, outside, VAL, value area, POC, HVN, VAH, outside.
    private static readonly List<ProfileBin> AllTypes = new()
    {
        Bin(100, double.NaN),
        Bin(101, 0),
        Bin(102, 20),
        Bin(103, 300),
        Bin(104, 400, va: true),
        Bin(105, 400, va: true),
        Bin(106, 1000, va: true, poc: true),
        Bin(107, 800, va: true),
        Bin(108, 400, va: true),
        Bin(109, 300),
    };

    private static IReadOnlyList<NavigationSonifier.ProfileNote> Notes(int i) => NavigationSonifier.ProfileBinNotes(AllTypes, i, 1f);

    [Fact]
    public void The_pitch_is_the_bins_part_of_the_profile_not_its_price()
    {
        Assert.Equal(new[]
        {
            ProfileNodeType.LVN, ProfileNodeType.Normal, ProfileNodeType.VAL, ProfileNodeType.ValueArea,
            ProfileNodeType.POC, ProfileNodeType.HVN, ProfileNodeType.VAH, ProfileNodeType.Normal,
        }, AllTypes.Skip(2).Select(b => ProfileBinClassifier.Classify(b, AllTypes)));

        var hz = Enumerable.Range(0, AllTypes.Count).Select(i => Notes(i)[0].Voice.Frequency).ToArray();
        Assert.Equal(new[] { 110.0, 110.0, 165.0, 330.0, 440.0, 440.0, 880.0, 220.0, 440.0, 330.0 }, hz);
    }

    [Fact]
    public void High_and_low_volume_nodes_and_empty_bins_are_low_and_each_its_own()
    {
        double noData = Notes(0)[0].Voice.Frequency, lvn = Notes(2)[0].Voice.Frequency, hvn = Notes(7)[0].Voice.Frequency;
        double valueArea = Notes(5)[0].Voice.Frequency, outside = Notes(3)[0].Voice.Frequency;
        Assert.Equal(3, new[] { noData, lvn, hvn }.Distinct().Count());
        Assert.All(new[] { noData, lvn, hvn }, f => Assert.True(f < outside && f < valueArea, $"{f} Hz is not low"));
    }

    [Fact]
    public void The_POC_is_a_chime_that_clicks_and_nothing_else_clicks()
    {
        var poc = Notes(6);
        Assert.Equal(3, poc.Count);
        Assert.Equal(new[] { 0, 1, 2 }, poc.Select(n => n.Slot));
        Assert.Equal(2.0, poc[1].Voice.Frequency / poc[0].Voice.Frequency, 6);
        Assert.Equal(2.76, poc[2].Voice.Frequency / poc[0].Voice.Frequency, 6);   // the bell's partial
        Assert.True(poc[0].Voice.DurationSeconds > Notes(5)[0].Voice.DurationSeconds, "the chime rings longer");
        for (int i = 0; i < AllTypes.Count; i++)
        {
            var notes = Notes(i);
            Assert.Equal(i == 6, notes[0].Voice.Click);
            if (i != 6) Assert.Single(notes);
        }
    }

    [Fact]
    public void Every_bin_is_a_sine_and_only_its_first_note_is_gritty()
    {
        for (int i = 0; i < AllTypes.Count; i++)
        {
            var notes = Notes(i);
            Assert.All(notes, n =>
            {
                Assert.Equal("sine", n.Voice.Waveform);
                Assert.Equal("Ping", n.Voice.Envelope);
                Assert.Equal(0f, n.Voice.SquareMix);
                Assert.Equal(0f, n.Voice.TriangleMix);
            });
            Assert.Equal(1f, notes[0].Voice.Volume);
            Assert.All(notes.Skip(1), n =>
            {
                Assert.True(n.Voice.Volume < 1f, $"bin {i}: a chime partial as loud as the note");
                Assert.Equal(0f, n.Voice.SubSawMix);
            });
        }
        Assert.True(Notes(6)[0].Voice.SubSawMix > Notes(7)[0].Voice.SubSawMix, "the POC is grittier than a smaller HVN");
        Assert.True(Notes(7)[0].Voice.SubSawMix > Notes(2)[0].Voice.SubSawMix, "an HVN is grittier than an LVN");
    }

    [Fact]
    public void A_bin_with_no_volume_is_the_lowest_tone_and_clean()
    {
        Assert.Equal(0f, Notes(0)[0].Voice.SubSawMix);   // NaN
        Assert.Equal(0f, Notes(1)[0].Voice.SubSawMix);   // zero
        Assert.Equal(AllTypes.Select((_, i) => Notes(i)[0].Voice.Frequency).Min(), Notes(0)[0].Voice.Frequency);
        var bins = new List<ProfileBin> { Bin(100, double.NaN), Bin(101, 400), Bin(102, 800) };
        Assert.Equal(0.30f, NavigationSonifier.ProfileBinNotes(bins, 2, 1f)[0].Voice.SubSawMix, 3);
        Assert.Equal(0.15f, NavigationSonifier.ProfileBinNotes(bins, 1, 1f)[0].Voice.SubSawMix, 3);
    }

    [Fact]
    public void A_profile_bin_silences_every_other_navigation_voice_and_plays_its_chime()
    {
        var cfg = new SeriesConfig { Id = "vp", Name = "vp", Pane = "Main", IsVisible = true, Volume = 1f };
        cfg.Components.Add(new ComponentConfig { Name = "profile", DisplayType = ComponentDisplayType.Profile, IsVisible = true });
        var series = new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = "vp", ProfileBins = AllTypes });
        var (sonifier, spy) = Sonifier();
        sonifier.SonifyProfile(series, 6, 1f);
        Assert.Equal(new[] { 0, 1, 2 }, spy.Stopped.Take(3));
        Assert.Equal(8, spy.Stopped.Count);
        Assert.Equal(new[] { 0, 1, 2 }, spy.Calls.Select(c => c.Slot));
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
