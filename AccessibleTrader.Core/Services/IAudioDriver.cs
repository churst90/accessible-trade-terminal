using System;

namespace AccessibleTrader.Core.Services
{
    public interface IAudioDriver
    {
        int SampleRate { get; }
        int Channels { get; }
        event Action<int>? PointReached;

        void SetVoice(int slot, double frequency, float volume, float pan, string waveform, bool continuous, double durationSeconds = 0.2, int dataIndex = -1, string envelope = "Sustain", bool click = false, float noiseAmount = 0f, string noiseType = "pink");
        void StopVoice(int slot);
        void StopAll();
        void Reset();
        void SetMasterGain(float gain);

        void Pause();
        void Resume();
    }
}
