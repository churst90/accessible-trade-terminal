using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The UI earcon round-robin has to stay off the slots other cues have reserved.</b>
///
/// <para>
/// Slots 16–31 are the earcon range, and three different callers write into it.
/// <c>NavigationSonifier.PlayNote</c> and <c>PlayPatch</c> take the next slot round-robin;
/// <c>EarconPatchPlayer</c> puts a level cue's patch layers on 26–29; <c>CrossEarcon</c> puts its
/// two-note chirp on 30 and 31. The round-robin was modulo 16, so it walked the whole range and
/// the eleventh note of any burst landed on 26 — cutting off whatever cue was sounding there.
/// </para>
///
/// <para>
/// The collision is not rare, and the timing is the worst possible. A level cross fires the chirp
/// AND is exactly the kind of bar that produces a burst of other UI notes, so the cue was most
/// likely to be stolen on the bars where it was the thing worth hearing. The slot map in
/// <c>NavigationSonifier</c> had this written down as a known defect rather than a documented
/// design, which is how it survived: the comment said the right thing and the code did not.
/// </para>
/// </summary>
public sealed class UiEarconSlotTests
{
    private sealed record VoiceCall(int Slot, double Frequency, float Volume, double DurationSeconds);

    private sealed class SpyDriver : IAudioDriver
    {
        public List<VoiceCall> Calls { get; } = new();
        public int SampleRate => 44100;
        public int Channels => 2;
        public event Action<int>? PointReached { add { } remove { } }
        public void SetVoice(int slot, double frequency, float volume, float pan, string waveform,
            bool continuous, double durationSeconds = 0.2, int dataIndex = -1, string envelope = "Sustain",
            bool click = false, float noiseAmount = 0f, string noiseType = "pink", float squareMix = 0f,
            float sawMix = 0f, float triangleMix = 0f, float subSawMix = 0f)
        {
            lock (Calls) Calls.Add(new VoiceCall(slot, frequency, volume, durationSeconds));
        }
        public void StopVoice(int slot) { }
        public void StopAll() { }
        public void Reset() { }
        public void SetMasterGain(float gain) { }
        public void Pause() { }
        public void Resume() { }
    }

    private static NavigationSonifier Sonifier(SpyDriver driver)
        => new(driver, new DefaultSonificationStrategy(new SoundPatchRegistry()), new SoundPatchRegistry());

    // ── The reservation ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Two hundred notes is far more than the ten-slot round-robin holds, so if it can reach the
    /// reserved slots at all it will reach every one of them several times over.
    /// </summary>
    [Fact]
    public void PlayNote_NeverTouchesTheReservedCueOrChirpSlots()
    {
        var driver = new SpyDriver();
        var sonifier = Sonifier(driver);

        for (int i = 0; i < 200; i++)
            sonifier.PlayNote(440 + i, 0.1, "sine", 0.2f, 0f);

        var trespass = driver.Calls.Where(c => c.Slot >= EarconPatchPlayer.CueSlotStart).ToList();

        Assert.True(trespass.Count == 0,
            "PlayNote wrote to " + string.Join(", ", trespass.Select(c => c.Slot).Distinct().Order()) +
            " — 26–29 are EarconPatchPlayer's level-cue layers and 30/31 are CrossEarcon's chirp; " +
            "landing there cuts them off mid-note.");
    }

    /// <summary>
    /// <c>PlayPatch</c> claims from the same counter, one slot per oscillator layer, so a
    /// multi-layer patch advances it several notches at a time. Same rule, separate call site —
    /// and a fix applied to only one of them would leave the other free to trespass.
    /// </summary>
    [Fact]
    public void PlayPatch_NeverTouchesTheReservedCueOrChirpSlots()
    {
        var driver = new SpyDriver();
        var sonifier = Sonifier(driver);

        var patch = new AccessibleTrader.Sdk.Models.SoundPatch
        {
            Id = "p", Name = "p", BaseFrequency = 440, FreqMultiplier = 1, Volume = 0.5f,
            DurationSeconds = 0.1, EnvelopeType = "Ping",
            Oscillators = new List<OscillatorLayer>
            {
                new() { Waveform = "sine",     FreqRatio = 1.0, Gain = 1.0f },
                new() { Waveform = "triangle", FreqRatio = 2.0, Gain = 0.5f },
                new() { Waveform = "square",   FreqRatio = 3.0, Gain = 0.3f },
            },
        };

        for (int i = 0; i < 80; i++) sonifier.PlayPatch(patch);

        Assert.DoesNotContain(driver.Calls, c => c.Slot >= EarconPatchPlayer.CueSlotStart);
    }

    /// <summary>
    /// The vacuity half, and it is not optional: a round-robin that had quietly collapsed onto a
    /// single slot would satisfy every assertion above while making UI earcons monophonic — one
    /// note cancelling the last, which is the failure the round-robin exists to prevent.
    /// </summary>
    [Fact]
    public void TheRoundRobinStillUsesItsWholeRange()
    {
        var driver = new SpyDriver();
        var sonifier = Sonifier(driver);

        for (int i = 0; i < 200; i++) sonifier.PlayNote(440, 0.1, "sine", 0.2f, 0f);

        var used = driver.Calls.Select(c => c.Slot).Distinct().Order().ToList();

        Assert.Equal(NavigationSonifier.UiRoundRobinSlots, used.Count);
        Assert.All(used, s => Assert.InRange(s, 16, EarconPatchPlayer.CueSlotStart - 1));
    }

    /// <summary>
    /// The reserved slots are reserved FOR something, so prove the thing still reaches them.
    /// Otherwise "nothing writes to 30/31" would be satisfiable by a chirp that had stopped
    /// firing entirely.
    /// </summary>
    [Fact]
    public void TheChirpStillOwnsThirtyAndThirtyOne()
    {
        var driver = new SpyDriver();
        CrossEarcon.Fire(driver, direction: +1);

        Assert.Contains(driver.Calls, c => c.Slot == CrossEarcon.SlotA);
    }

    // ── Staggered earcons ───────────────────────────────────────────────────────────

    /// <summary>
    /// A phrase built from delayed notes has to arrive as separate onsets.
    ///
    /// <para>
    /// <c>PlayNote</c>'s <c>delay</c> argument was discarded for a long time — every note of a
    /// sequence earcon was armed at once, so what should have been a rising three-note phrase was
    /// a single chord. "The delay is passed through" is a weaker claim than the one that matters,
    /// which is about the sound, so this renders the phrase and counts the onsets in the samples.
    /// </para>
    ///
    /// <para>
    /// Rendered against the ENGINE's sample clock rather than the wall clock, and that is not
    /// fastidiousness — the first version of this test slept alongside the real delays, passed on
    /// its own, and failed inside the full suite, because under load a 5 ms sleep is not 5 ms and
    /// the render loop fell behind the notes it was supposed to be separating. Sample counts are
    /// the only clock in an audio test that means the same thing on a loaded CI box as on an idle
    /// laptop. What the delay itself does is asserted separately below, where it can be asserted
    /// without racing anything.
    /// </para>
    /// </summary>
    [Fact]
    public void AStaggeredEarconArrivesAsThreeDistinctOnsets()
    {
        var engine = new AudioEngine();
        var driver = new EngineBackedDriver(engine);
        var sonifier = new NavigationSonifier(driver,
            new DefaultSonificationStrategy(new SoundPatchRegistry()), new SoundPatchRegistry());

        // The 120 ms the phrase is spaced by, counted in BUFFER samples — the buffer is
        // interleaved stereo, so a millisecond is two samples per frame and not one. Getting that
        // wrong renders half the gap, which butts a 60 ms note against the next one and reads as a
        // single sustained onset; the first version of this test did exactly that.
        const int GapSamples = 44100 * 120 / 1000 * 2;
        var samples = new List<float>();

        // Three short notes, each armed after the previous one's 60 ms has decayed. Each arm goes
        // through the real round-robin, so a slot collision would cut a note short and show up as
        // a missing onset rather than as a passing test.
        foreach (double freq in new[] { 523.25, 659.25, 783.99 })
        {
            sonifier.PlayNote(freq, 0.06, "sine", 0.5f, 0f);
            var buf = new float[441];
            for (int done = 0; done < GapSamples; done += buf.Length)
            {
                engine.Read(buf, 0, buf.Length);
                samples.AddRange(buf);
            }
        }

        int onsets = CountOnsets(samples.ToArray());

        Assert.True(onsets == 3,
            $"expected three separate onsets from a staggered three-note earcon, counted {onsets}. " +
            "Fewer means notes ran together or one was cut off by another claiming its slot.");
    }

    /// <summary>
    /// The delay itself, asserted without a clock race in it.
    ///
    /// <para>
    /// Two facts are enough and both are one-sided: immediately after the call the driver has NOT
    /// been touched (a synchronous arm would mean the delay was dropped, which is the original
    /// bug), and given as long as it needs, it eventually is. Neither depends on the scheduler
    /// being punctual — only on half a second not having elapsed inside a method return.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ADelayedNoteIsNotArmedSynchronously()
    {
        var driver = new SpyDriver();
        var sonifier = Sonifier(driver);

        sonifier.PlayNote(440, 0.06, "sine", 0.5f, 0f, delay: 500);

        lock (driver.Calls) Assert.Empty(driver.Calls);

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            lock (driver.Calls) { if (driver.Calls.Count > 0) break; }
            await Task.Delay(25);
        }

        lock (driver.Calls)
            Assert.True(driver.Calls.Count == 1, "the delayed note never reached the driver at all");
    }

    /// <summary>
    /// And the slot claim, which is the subtle half. Slots are taken at CALL time, not at fire
    /// time, so a phrase's notes hold three different slots and none of them cuts off the one
    /// before. A round-robin that resolved the slot inside the delayed continuation would put a
    /// fast phrase back onto one slot and collapse it into a single note again — while every
    /// timing assertion above still passed.
    /// </summary>
    [Fact]
    public async Task EveryNoteOfADelayedPhraseLandsOnItsOwnSlot()
    {
        var driver = new SpyDriver();
        var sonifier = Sonifier(driver);

        sonifier.PlayNote(523.25, 0.06, "sine", 0.5f, 0f, delay: 0);
        sonifier.PlayNote(659.25, 0.06, "sine", 0.5f, 0f, delay: 60);
        sonifier.PlayNote(783.99, 0.06, "sine", 0.5f, 0f, delay: 120);

        // Wait for arrival rather than measuring it — the claim is about WHICH slot, not when.
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            lock (driver.Calls) { if (driver.Calls.Count >= 3) break; }
            await Task.Delay(25);
        }

        List<int> slots;
        lock (driver.Calls) slots = driver.Calls.Select(c => c.Slot).ToList();

        Assert.Equal(3, slots.Count);
        Assert.Equal(3, slots.Distinct().Count());
    }

    /// <summary>
    /// The same three notes with no delays must collapse to ONE onset. Without this, an onset
    /// counter that simply found three peaks in any burst would call the broken behaviour correct.
    /// </summary>
    [Fact]
    public void ThreeSimultaneousNotesAreOneOnset()
    {
        var engine = new AudioEngine();
        var driver = new EngineBackedDriver(engine);
        var sonifier = new NavigationSonifier(driver,
            new DefaultSonificationStrategy(new SoundPatchRegistry()), new SoundPatchRegistry());

        sonifier.PlayNote(523.25, 0.06, "sine", 0.5f, 0f);
        sonifier.PlayNote(659.25, 0.06, "sine", 0.5f, 0f);
        sonifier.PlayNote(783.99, 0.06, "sine", 0.5f, 0f);

        var samples = new float[44100 / 2];
        var buf = new float[441];
        for (int done = 0; done < samples.Length; done += buf.Length)
        {
            engine.Read(buf, 0, buf.Length);
            Array.Copy(buf, 0, samples, done, Math.Min(buf.Length, samples.Length - done));
        }

        Assert.Equal(1, CountOnsets(samples));
    }

    /// <summary>
    /// Counts note starts by walking the short-window envelope and marking each rise from
    /// effective silence into signal. Windowed rather than sample-by-sample because the samples
    /// themselves cross zero every cycle; the threshold is a long way below note level and a long
    /// way above the engine's noise floor, so neither end is sensitive to exact tuning.
    /// </summary>
    private static int CountOnsets(float[] stereo)
    {
        const int Window = 441;              // 5 ms
        const float Silence = 0.01f;
        const float Sounding = 0.05f;

        int onsets = 0;
        bool inNote = false;
        for (int start = 0; start + Window <= stereo.Length; start += Window)
        {
            float peak = 0f;
            for (int i = start; i < start + Window; i++) peak = Math.Max(peak, Math.Abs(stereo[i]));

            if (!inNote && peak >= Sounding) { onsets++; inNote = true; }
            else if (inNote && peak < Silence) inNote = false;
        }
        return onsets;
    }

    private sealed class EngineBackedDriver(AudioEngine engine) : IAudioDriver
    {
        public int SampleRate => engine.SampleRate;
        public int Channels => engine.Channels;
        public event Action<int>? PointReached { add { } remove { } }
        public void SetVoice(int slot, double frequency, float volume, float pan, string waveform,
            bool continuous, double durationSeconds = 0.2, int dataIndex = -1, string envelope = "Sustain",
            bool click = false, float noiseAmount = 0f, string noiseType = "pink", float squareMix = 0f,
            float sawMix = 0f, float triangleMix = 0f, float subSawMix = 0f)
            => engine.SetVoice(slot, frequency, volume, pan, waveform, continuous, durationSeconds,
                dataIndex, envelope, click, noiseAmount, noiseType, squareMix, sawMix, triangleMix, subSawMix);
        public void StopVoice(int slot) => engine.StopVoice(slot);
        public void StopAll() => engine.StopAll();
        public void Reset() => engine.Reset();
        public void SetMasterGain(float gain) => engine.SetMasterGain(gain);
        public void Pause() { }
        public void Resume() { }
    }
}
