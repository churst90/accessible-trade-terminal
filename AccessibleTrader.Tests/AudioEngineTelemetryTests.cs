using AccessibleTrader.Core.Services.Audio;

namespace AccessibleTrader.Tests
{
    public class AudioEngineTelemetryTests
    {
        [Fact]
        public void DroppedCommandCount_StartsAtZero()
        {
            var engine = new AudioEngine();
            Assert.Equal(0, engine.DroppedCommandCount);
            Assert.Equal(0, engine.TotalCommandCount);
        }

        [Fact]
        public void OverflowEnqueue_IncrementsDroppedCounter()
        {
            var engine = new AudioEngine();

            // Ring buffer capacity is 1024. Fire 3000 SetVoice calls without ever draining
            // the queue (we don't call Read). Expect ~2000 drops, and for every call to have
            // contributed to TotalCommandCount.
            long droppedNotifications = 0;
            engine.CommandDropped += _ => droppedNotifications++;

            for (int i = 0; i < 3000; i++)
            {
                engine.SetVoice(slot: i % 64, freq: 440.0, vol: 0.5f, pan: 0f,
                    wave: "sine", continuous: true, durationSec: 0);
            }

            Assert.Equal(3000, engine.TotalCommandCount);
            Assert.True(engine.DroppedCommandCount > 0,
                "Expected at least one drop when flooding past ring capacity.");
            Assert.Equal(engine.DroppedCommandCount, droppedNotifications);
        }

        [Fact]
        public void ResetTelemetry_ZeroesBothCounters()
        {
            var engine = new AudioEngine();
            for (int i = 0; i < 3000; i++)
                engine.SetVoice(i % 64, 440.0, 0.5f, 0f, "sine", true, 0);
            Assert.True(engine.DroppedCommandCount > 0);
            Assert.Equal(3000, engine.TotalCommandCount);

            engine.ResetTelemetry();

            Assert.Equal(0, engine.DroppedCommandCount);
            Assert.Equal(0, engine.TotalCommandCount);
        }

        [Fact]
        public void NormalUsage_BelowCapacity_ProducesNoDrops()
        {
            var engine = new AudioEngine();
            for (int i = 0; i < 500; i++)
                engine.SetVoice(i % 64, 440.0, 0.5f, 0f, "sine", true, 0);

            Assert.Equal(500, engine.TotalCommandCount);
            Assert.Equal(0, engine.DroppedCommandCount);
        }
    }
}
