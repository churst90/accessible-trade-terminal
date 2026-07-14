using System;

namespace AccessibleTrader.Core.Services
{
    public interface IAudioDriver
    {
        int SampleRate { get; }
        int Channels { get; }
        event Action<int>? PointReached;

        void SetVoice(int slot, double frequency, float volume, float pan, string waveform, bool continuous, double durationSeconds = 0.2, int dataIndex = -1, string envelope = "Sustain", bool click = false, float noiseAmount = 0f, string noiseType = "pink", float squareMix = 0f, float sawMix = 0f, float triangleMix = 0f, float subSawMix = 0f);
        void StopVoice(int slot);
        void StopAll();
        void Reset();
        void SetMasterGain(float gain);

        void Pause();
        void Resume();

        // ── Telemetry (post-audit W4 additions) ──
        // Ring-buffer overflow counters surfaced so UI layers (JournalModal)
        // can report session audio health. Drivers without a real engine —
        // e.g. unit-test mocks — default all values to 0 / no-op.
        long DroppedCommandCount => 0;
        long TotalCommandCount => 0;
        void ResetAudioTelemetry() { }
    }
}
