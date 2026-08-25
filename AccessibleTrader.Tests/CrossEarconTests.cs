using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;

namespace AccessibleTrader.Tests;

/// <summary>
/// Directional cross earcon: fired by both navigation and playback when a value crosses a level.
/// These assert the synchronous first note (its pitch encodes direction) and the no-op guard;
/// the second note is scheduled ~70 ms later and isn't asserted here to keep the test deterministic.
/// </summary>
public class CrossEarconTests
{
    [Fact]
    public void Fire_UpCross_StartsLowOnSlot30()
    {
        var driver = new SpyDriver();
        CrossEarcon.Fire(driver, direction: 1);

        Assert.NotEmpty(driver.Calls);
        var first = driver.Calls[0];
        Assert.Equal(CrossEarcon.SlotA, first.Slot);       // slot 30
        Assert.True(first.Frequency < 600, "up-cross should start on the low note (rising).");
        Assert.Equal("Ping", first.Envelope);
    }

    [Fact]
    public void Fire_DownCross_StartsHighOnSlot30()
    {
        var driver = new SpyDriver();
        CrossEarcon.Fire(driver, direction: -1);

        var first = driver.Calls[0];
        Assert.Equal(CrossEarcon.SlotA, first.Slot);
        Assert.True(first.Frequency > 600, "down-cross should start on the high note (falling).");
    }

    [Fact]
    public void Fire_NoCross_DoesNothing()
    {
        var driver = new SpyDriver();
        CrossEarcon.Fire(driver, direction: 0);
        Assert.Empty(driver.Calls);
    }

    private sealed record Call(int Slot, double Frequency, string Envelope);

    private sealed class SpyDriver : IAudioDriver
    {
        public List<Call> Calls { get; } = new();
        public int SampleRate => 48000;
        public int Channels => 2;
#pragma warning disable CS0067
        public event Action<int>? PointReached;
#pragma warning restore CS0067
        public void SetVoice(int slot, double frequency, float volume, float pan, string waveform,
            bool continuous, double durationSeconds = 0.2, int dataIndex = -1, string envelope = "Sustain",
            bool click = false, float noiseAmount = 0f, string noiseType = "pink", float squareMix = 0f, float sawMix = 0f,
            float triangleMix = 0f, float subSawMix = 0f)
            => Calls.Add(new Call(slot, frequency, envelope));
        public void StopVoice(int slot) { }
        public void StopAll() { }
        public void Reset() { }
        public void SetMasterGain(float gain) { }
        public void Pause() { }
        public void Resume() { }
    }
}
