using System;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Perceptual snapshot tests: render real audio through the AudioEngine and
    /// assert ENERGY, not intent. The inaudible-noise bug (pink/brown one-pole
    /// filters attenuating ~30 dB with no makeup gain) lived for months because
    /// every parameter LOOKED right — NoiseAmount was set, the filter ran, and
    /// nothing ever measured what came out. These tests measure what comes out.
    ///
    /// Method: start a continuous voice, render past the attack/declick region,
    /// then measure RMS over ~0.5 s of steady state. Noise level is isolated as
    /// sqrt(rms_textured² − rms_clean²) — the power the texture ADDS to the tone.
    /// Thresholds are deliberately loose (factor-of-two margins): they exist to
    /// catch "silent" and "absurdly loud", not to freeze exact tuning.
    /// </summary>
    public class AudioPerceptualTests
    {
        private const int SampleRate = 44100;
        private const int SettleSamples = 8192;          // skip attack + declick (~93 ms)
        private const int MeasureSamples = 44100;        // ~0.5 s of stereo frames

        private static float[] Render(Action<AudioEngine> arm)
        {
            var engine = new AudioEngine();
            arm(engine);
            var buf = new float[1024];
            // Settle.
            for (int done = 0; done < SettleSamples; done += buf.Length)
                engine.Read(buf, 0, buf.Length);
            // Measure.
            var outBuf = new float[MeasureSamples];
            for (int done = 0; done < MeasureSamples; done += buf.Length)
            {
                engine.Read(buf, 0, buf.Length);
                Array.Copy(buf, 0, outBuf, done, Math.Min(buf.Length, MeasureSamples - done));
            }
            return outBuf;
        }

        private static double Rms(float[] samples, int channel = -1)
        {
            double sum = 0; int n = 0;
            for (int i = channel < 0 ? 0 : channel; i < samples.Length; i += channel < 0 ? 1 : 2)
            {
                sum += (double)samples[i] * samples[i];
                n++;
            }
            return Math.Sqrt(sum / Math.Max(1, n));
        }

        /// <summary>Noise the texture adds on top of the tone, as RMS.</summary>
        private static double AddedNoiseRms(string noiseType, float noiseAmount)
        {
            double clean = Rms(Render(e => e.SetVoice(0, 220, 0.5f, 0f, "sine", true, 10)));
            double tex = Rms(Render(e => e.SetVoice(0, 220, 0.5f, 0f, "sine", true, 10,
                noiseAmount: noiseAmount, noiseType: noiseType)));
            double diff = tex * tex - clean * clean;
            return diff <= 0 ? 0 : Math.Sqrt(diff);
        }

        // ── The regression that motivated this file ──────────────────────────

        [Theory]
        [InlineData("pink")]
        [InlineData("brown")]
        [InlineData("white")]
        public void NoiseTexture_IsActuallyAudible(string noiseType)
        {
            // A 0.3 texture must add clearly perceptible roughness. Measured levels
            // of the tuning Cody approved by ear (2026-07-16): white ~24%, brown ~16%,
            // pink ~11% of tone RMS. The floor is 8% — comfortably below all three,
            // and 4x the ~2% the pre-makeup-gain bug produced. If this fires, noise
            // has gone inaudible again; do NOT lower the floor to make it pass.
            double tone = Rms(Render(e => e.SetVoice(0, 220, 0.5f, 0f, "sine", true, 10)));
            double noise = AddedNoiseRms(noiseType, 0.3f);
            Assert.True(noise / tone > 0.08,
                $"{noiseType} noise at 0.3 adds only {noise / tone:P0} of the tone RMS — inaudible texture regression.");
        }

        [Fact]
        public void NoiseColors_MeanTheSameLoudness()
        {
            // The contract behind the makeup gains: NoiseAmount means roughly the
            // same energy whether white, pink, or brown. Allow a factor of 3.
            // Average several realizations — a single brown-noise buffer (a random walk)
            // has enough RMS variance to occasionally clip the 3x band on its own; the
            // mean over realizations tests the makeup-gain contract, not one draw's luck.
            static double AvgRms(string type)
            {
                const int realizations = 8;
                double sum = 0;
                for (int i = 0; i < realizations; i++) sum += AddedNoiseRms(type, 0.3f);
                return sum / realizations;
            }
            double white = AvgRms("white");
            double pink  = AvgRms("pink");
            double brown = AvgRms("brown");
            Assert.True(pink > white / 3 && pink < white * 3,
                $"pink {pink:F4} vs white {white:F4} — outside 3x band.");
            Assert.True(brown > white / 3 && brown < white * 3,
                $"brown {brown:F4} vs white {white:F4} — outside 3x band.");
        }

        [Fact]
        public void ZeroNoise_AddsNothing()
        {
            double noise = AddedNoiseRms("pink", 0f);
            double tone = Rms(Render(e => e.SetVoice(0, 220, 0.5f, 0f, "sine", true, 10)));
            Assert.True(noise / tone < 0.05, $"NoiseAmount 0 added {noise / tone:P1} energy.");
        }

        // ── Grit (sub-octave sawtooth) ────────────────────────────────────────

        [Fact]
        public void SubSawGrit_ChangesTheWaveformAudibly()
        {
            // Same voice with and without grit must differ substantially sample-wise;
            // a big-wick / big-bar signature must never silently vanish.
            var clean = Render(e => e.SetVoice(0, 220, 0.5f, 0f, "sine", true, 10));
            var gritty = Render(e => e.SetVoice(0, 220, 0.5f, 0f, "sine", true, 10, subSawMix: 0.30f));
            double diff = 0;
            for (int i = 0; i < clean.Length; i++)
            {
                double d = clean[i] - gritty[i];
                diff += d * d;
            }
            double diffRms = Math.Sqrt(diff / clean.Length);
            Assert.True(diffRms > 0.02, $"SubSawMix 0.3 changed the signal by only {diffRms:F4} RMS.");
        }

        // ── Loudness contracts ───────────────────────────────────────────────

        [Fact]
        public void EqualPowerPan_CentreIsBalanced_EdgesAreExclusive()
        {
            var centre = Render(e => e.SetVoice(0, 440, 0.5f, 0f, "sine", true, 10));
            double cl = Rms(centre, 0), cr = Rms(centre, 1);
            Assert.True(Math.Abs(cl - cr) / Math.Max(cl, cr) < 0.05,
                $"Centre pan imbalance: L {cl:F4} vs R {cr:F4}.");

            var left = Render(e => e.SetVoice(0, 440, 0.5f, -1f, "sine", true, 10));
            Assert.True(Rms(left, 1) < Rms(left, 0) * 0.05,
                "Hard-left pan leaks into the right channel.");
        }

        [Fact]
        public void VolumeZero_IsSilent()
        {
            double rms = Rms(Render(e => e.SetVoice(0, 440, 0f, 0f, "sine", true, 10)));
            Assert.True(rms < 1e-4, $"Zero-volume voice produced {rms:F6} RMS.");
        }

        // ── VoiceParams equivalence ──────────────────────────────────────────

        /// <summary>Minimal IAudioDriver over a bare engine — AudioEngine itself is not
        /// an IAudioDriver (host drivers wrap it), and the point is to exercise the
        /// VoiceParams default-interface forwarding exactly as drivers see it.</summary>
        private sealed class EngineDriver : IAudioDriver
        {
            public AudioEngine Engine { get; } = new();
            public int SampleRate => Engine.SampleRate;
            public int Channels => Engine.Channels;
            public event Action<int>? PointReached { add { } remove { } }
            public void SetVoice(int slot, double frequency, float volume, float pan, string waveform,
                bool continuous, double durationSeconds = 0.2, int dataIndex = -1, string envelope = "Sustain",
                bool click = false, float noiseAmount = 0f, string noiseType = "pink", float squareMix = 0f,
                float sawMix = 0f, float triangleMix = 0f, float subSawMix = 0f)
                => Engine.SetVoice(slot, frequency, volume, pan, waveform, continuous, durationSeconds,
                    dataIndex, envelope, click, noiseAmount, noiseType, squareMix, sawMix, triangleMix, subSawMix);
            public void StopVoice(int slot) { }
            public void StopAll() { }
            public void Reset() { }
            public void SetMasterGain(float gain) { }
            public void Pause() { }
            public void Resume() { }
        }

        [Fact]
        public void VoiceParams_RendersIdenticallyToPositionalCall()
        {
            var positional = Render(e => e.SetVoice(0, 330, 0.4f, 0.25f, "triangle", true, 10,
                noiseAmount: 0.2f, noiseType: "brown", subSawMix: 0.15f));

            var driver = new EngineDriver();
            ((IAudioDriver)driver).SetVoice(0, new VoiceParams
            {
                Frequency = 330, Volume = 0.4f, Pan = 0.25f, Waveform = "triangle",
                Continuous = true, DurationSeconds = 10,
                NoiseAmount = 0.2f, NoiseType = "brown", SubSawMix = 0.15f,
            });
            var buf = new float[1024];
            for (int done = 0; done < SettleSamples; done += buf.Length)
                driver.Engine.Read(buf, 0, buf.Length);
            var named = new float[MeasureSamples];
            for (int done = 0; done < MeasureSamples; done += buf.Length)
            {
                driver.Engine.Read(buf, 0, buf.Length);
                Array.Copy(buf, 0, named, done, Math.Min(buf.Length, MeasureSamples - done));
            }

            // Noise is random per render; compare energy, not samples.
            double rp = Rms(positional), rn = Rms(named);
            Assert.True(Math.Abs(rp - rn) / Math.Max(rp, rn) < 0.05,
                $"VoiceParams path RMS {rn:F4} differs from positional {rp:F4}.");
        }
    }
}
