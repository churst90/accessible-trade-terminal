using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A profile bin is three things at once</b> (Cody, 2026-11-03: "bar size, area type and the
/// position using the pitch"). Its first note's pitch is its price, its grit is its size against
/// the biggest bin, and its SOUND is its type: the Point of Control a chord with a click, a high
/// volume node an octave double, the value area's top and bottom edges two notes rising and
/// falling, the value area warm, outside it plain, a low volume node a short thin tick. The first
/// note is equally loud on every bin. The heatmap follows the same size rule against its column.
///
/// <para>Until 2026-11-02 loudness followed size and pitch followed type, so price was not heard.
/// d8533b2f then gave every type one sound bar a faint warmth and the POC's click, and Cody could
/// not find the POC without hearing the words.</para>
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
        {
            lock (Calls) Calls.Add((slot, frequency, volume, waveform, durationSeconds, envelope, click, squareMix, triangleMix, subSawMix));
        }
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

    // Bottom to top, one of every type: LVN, Normal, VAL, ValueArea, POC, HVN, VAH, Normal.
    private static readonly List<ProfileBin> AllTypes = new()
    {
        Bin(100, 20),
        Bin(101, 300),
        Bin(102, 400, va: true),
        Bin(103, 400, va: true),
        Bin(104, 1000, va: true, poc: true),
        Bin(105, 800, va: true),
        Bin(106, 400, va: true),
        Bin(107, 300),
    };

    private static string Signature(IReadOnlyList<NavigationSonifier.ProfileNote> notes)
    {
        var carrier = notes[0].Voice;
        return string.Join(" | ", notes.Select(n =>
            $"{n.Slot}@{n.DelayMs} x{n.Voice.Frequency / carrier.Frequency:F3} {n.Voice.DurationSeconds:F2}s " +
            $"tri{n.Voice.TriangleMix:F2} sq{n.Voice.SquareMix:F2} {(n.Voice.Click ? "click" : "")}"));
    }

    [Fact]
    public void Every_part_of_the_profile_has_its_own_sound()
    {
        var types = AllTypes.Select(b => ProfileBinClassifier.Classify(b, AllTypes)).ToList();
        Assert.Equal(new[]
        {
            ProfileNodeType.LVN, ProfileNodeType.Normal, ProfileNodeType.VAL, ProfileNodeType.ValueArea,
            ProfileNodeType.POC, ProfileNodeType.HVN, ProfileNodeType.VAH, ProfileNodeType.Normal,
        }, types);

        // Seven types, seven sounds: the two Normal bins (1 and 7) share one, nothing else does.
        var sigs = Enumerable.Range(0, AllTypes.Count)
            .Select(i => Signature(NavigationSonifier.ProfileBinNotes(AllTypes, i, 1f))).ToList();
        Assert.Equal(sigs[1], sigs[7]);
        Assert.Equal(7, sigs.Distinct().Count());
    }

    [Fact]
    public void The_POC_is_a_chord_that_clicks_and_nothing_else_clicks()
    {
        for (int i = 0; i < AllTypes.Count; i++)
        {
            var notes = NavigationSonifier.ProfileBinNotes(AllTypes, i, 1f);
            Assert.Equal(AllTypes[i].IsPOC, notes[0].Voice.Click);
            Assert.All(notes.Skip(1), n => Assert.False(n.Voice.Click));
        }
        var poc = NavigationSonifier.ProfileBinNotes(AllTypes, 4, 1f);
        Assert.Equal(3, poc.Count);
        Assert.All(poc, n => Assert.Equal(0, n.DelayMs));                       // all at once: a chord
        Assert.Equal(1.5, poc[1].Voice.Frequency / poc[0].Voice.Frequency, 6); // the fifth
        Assert.Equal(2.0, poc[2].Voice.Frequency / poc[0].Voice.Frequency, 6); // the octave
        Assert.Equal(new[] { 0, 1, 2 }, poc.Select(n => n.Slot));
    }

    [Fact]
    public void The_value_areas_top_edge_rises_and_its_bottom_edge_falls()
    {
        var vah = NavigationSonifier.ProfileBinNotes(AllTypes, 6, 1f);
        var val = NavigationSonifier.ProfileBinNotes(AllTypes, 2, 1f);
        Assert.Equal(2, vah.Count);
        Assert.Equal(2, val.Count);
        Assert.True(vah[1].DelayMs > 0 && val[1].DelayMs > 0, "an edge is two notes in turn, not a chord");
        Assert.Equal(4.0 / 3.0, vah[1].Voice.Frequency / vah[0].Voice.Frequency, 6);
        Assert.Equal(3.0 / 4.0, val[1].Voice.Frequency / val[0].Voice.Frequency, 6);
    }

    [Fact]
    public void Nodes_and_areas_differ_in_tone_high_volume_doubled_low_volume_a_short_thin_tick()
    {
        var hvn = NavigationSonifier.ProfileBinNotes(AllTypes, 5, 1f);
        Assert.Equal(2, hvn.Count);
        Assert.Equal(2.0, hvn[1].Voice.Frequency / hvn[0].Voice.Frequency, 6);
        Assert.Equal(0, hvn[1].DelayMs);

        var lvn = NavigationSonifier.ProfileBinNotes(AllTypes, 0, 1f);
        var inside = NavigationSonifier.ProfileBinNotes(AllTypes, 3, 1f);
        var outside = NavigationSonifier.ProfileBinNotes(AllTypes, 1, 1f);
        Assert.Single(lvn);
        Assert.Single(inside);
        Assert.Single(outside);
        var shortest = AllTypes.Select((_, i) => NavigationSonifier.ProfileBinNotes(AllTypes, i, 1f)[0].Voice.DurationSeconds).Min();
        Assert.Equal(shortest, lvn[0].Voice.DurationSeconds);
        Assert.True(lvn[0].Voice.SquareMix > inside[0].Voice.SquareMix, "a low volume node is thinner than the value area");
        Assert.True(inside[0].Voice.TriangleMix > 0, "the value area is warm");
        Assert.Equal(0f, outside[0].Voice.TriangleMix);                           // outside it is plain
    }

    [Fact]
    public void Added_notes_are_quieter_than_the_first_and_only_the_first_carries_the_grit()
    {
        for (int i = 0; i < AllTypes.Count; i++)
        {
            var notes = NavigationSonifier.ProfileBinNotes(AllTypes, i, 1f);
            Assert.Equal(1f, notes[0].Voice.Volume);
            Assert.True(notes[0].Voice.SubSawMix > 0f, $"bin {i}: no grit on its first note");
            Assert.All(notes.Skip(1), n =>
            {
                Assert.True(n.Voice.Volume < notes[0].Voice.Volume, $"bin {i}: an added note as loud as the first");
                Assert.Equal(0f, n.Voice.SubSawMix);
            });
        }
    }

    [Fact]
    public async Task An_edges_second_note_plays_and_is_dropped_if_the_user_has_already_moved_on()
    {
        var cfg = new SeriesConfig { Id = "vp", Name = "vp", Pane = "Main", IsVisible = true, Volume = 1f };
        cfg.Components.Add(new ComponentConfig { Name = "profile", DisplayType = ComponentDisplayType.Profile, IsVisible = true });
        var series = new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = "vp", ProfileBins = AllTypes });
        double fourth = NavigationSonifier.ProfileBinNotes(AllTypes, 6, 1f)[1].Voice.Frequency;

        bool Heard(Spy s) { lock (s.Calls) return s.Calls.Any(c => c.Slot == 1 && Math.Abs(c.Freq - fourth) < 1e-9); }

        // Standing on the top edge: the rising note arrives.
        var (still, stillSpy) = Sonifier();
        still.SonifyProfile(series, 6, 1f);
        await Task.Delay(NavigationSonifier.EdgeGapMs * 4);
        Assert.True(Heard(stillSpy), "the top edge's second note never played");

        // Arrowing straight past it: the rising note is not left trailing on the next bin.
        var (moving, movingSpy) = Sonifier();
        moving.SonifyProfile(series, 6, 1f);
        moving.SonifyProfile(series, 7, 1f);
        await Task.Delay(NavigationSonifier.EdgeGapMs * 4);
        Assert.False(Heard(movingSpy), "a stale edge note played after the user moved on");
    }

    [Fact]
    public void A_bin_with_no_volume_reads_clean_rather_than_breaking_the_rest()
    {
        var bins = new List<ProfileBin> { Bin(100, double.NaN), Bin(101, 400), Bin(102, 800) };
        Assert.Equal(0f, NavigationSonifier.ProfileBinNotes(bins, 0, 1f)[0].Voice.SubSawMix);
        Assert.Equal(0.30f, NavigationSonifier.ProfileBinNotes(bins, 2, 1f)[0].Voice.SubSawMix, 3);
        Assert.Equal(0.15f, NavigationSonifier.ProfileBinNotes(bins, 1, 1f)[0].Voice.SubSawMix, 3);
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
