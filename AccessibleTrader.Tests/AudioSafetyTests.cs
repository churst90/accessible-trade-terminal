using AccessibleTrader.Core.Services.Audio;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Two audio-engine defects, both measured rather than argued.
    ///
    /// <para><b>"Make it stop" did not, if anything arrived within 20 ms.</b>
    /// <c>StopAll</c>/<c>Reset</c> faded the master gain to zero, and the per-frame loop
    /// deactivated voices only once <c>_masterGain</c> actually reached <c>0.0f</c> — which is
    /// the ONLY code that deactivated voices on a stop-all. But the apply-commands block
    /// re-armed <c>_targetMasterGain = _userMasterGain</c> for any voice command queued behind
    /// the stop-all in the same pass, so the gain never reached zero and every voice that was
    /// sounding kept sounding. The window is the whole fade (<c>FADE_SAMPLES</c> = 882, ~20 ms)
    /// plus whatever is queued, and this is the path behind
    /// <c>NavigationSonifier.Silence()</c> → <c>AudioFeedbackRouter.Silence()</c> — the user's
    /// "make it stop" control. One arrow key inside 20 ms and it was a no-op.</para>
    ///
    /// <para><b>A sawtooth above the sample rate was unbounded.</b>
    /// Phase was wrapped by a single subtraction, which is correct only while the per-sample
    /// increment is below 2π, i.e. while frequency &lt; SampleRate. Above that the phase grew
    /// without bound and the sawtooth reader <c>2·(Phase/2π) − 1</c> is LINEAR in phase with no
    /// clamp, so amplitude ramped upward forever. Nothing clamped the frequency either: it
    /// arrives as <c>BaseFrequency × FreqMultiplier × FreqRatio</c>, the Sound Designer's
    /// handlers are bare <c>double.TryParse</c>, <c>FreqRatio</c> has no bound in the UI at
    /// all, and <c>ImportPatchJson</c> validates nothing but the id.</para>
    ///
    /// <para><b>How this is tested, and why not by peak.</b> The audit measured raw peaks of
    /// 81.7, 4843 and 127983 against a full scale of 1. Those numbers are PRE-LIMITER: the
    /// brickwall limiter added in the 2026-08-26 chart-clipping fix now sits downstream of
    /// every voice, so a peak assertion on the output buffer passes whatever the oscillator
    /// does — it would be guarding the limiter, not this fix. Measured both ways to be sure:
    /// with the defect restored the output does not overflow, it <b>pins at the limiter
    /// ceiling</b>, RMS 0.990000 against a normal 0.252309. That is a sustained full-scale
    /// roar in headphones worn by a blind user, and it is what these tests assert against.</para>
    /// </summary>
    public class AudioSafetyTests
    {
        private const int BufferSamples = 1024;

        private static float[] ReadOneBuffer(AudioEngine engine)
        {
            var buf = new float[BufferSamples];
            engine.Read(buf, 0, buf.Length);
            return buf;
        }

        private static double Rms(float[] buf)
        {
            double sum = 0;
            foreach (var s in buf) sum += (double)s * s;
            return Math.Sqrt(sum / buf.Length);
        }

        private static float Peak(float[] buf)
        {
            float peak = 0;
            foreach (var s in buf) peak = Math.Max(peak, Math.Abs(s));
            return peak;
        }

        // ── Stop-all ─────────────────────────────────────────────────────────

        [Fact]
        public void StopAll_silences_even_when_a_voice_command_arrives_right_behind_it()
        {
            // The measured case, exactly: prime a continuous voice, StopAll(), then a
            // SetVoice on ANOTHER slot inside the fade window. Residual RMS was 0.397307.
            var engine = new AudioEngine();
            engine.SetMasterGain(1.0f);
            engine.SetVoice(0, 440, 0.9f, 0, "sine", continuous: true, durationSec: 10.0);
            ReadOneBuffer(engine); // prime

            engine.StopAll();
            engine.SetVoice(16, 880, 0.9f, 0, "sine", continuous: false, durationSec: 0.001);

            float[] last = null!;
            for (int i = 0; i < 40; i++) last = ReadOneBuffer(engine);

            Assert.Equal(0.0, Rms(last), 6);
        }

        [Fact]
        public void StopAll_silences_when_nothing_arrives_behind_it()
        {
            // The control. This case always passed, which is why the defect hid: the existing
            // Reset_SilencesAllOutput never enqueued a command after Reset().
            var engine = new AudioEngine();
            engine.SetMasterGain(1.0f);
            engine.SetVoice(0, 440, 0.9f, 0, "sine", continuous: true, durationSec: 10.0);
            ReadOneBuffer(engine);

            engine.StopAll();

            float[] last = null!;
            for (int i = 0; i < 40; i++) last = ReadOneBuffer(engine);

            Assert.Equal(0.0, Rms(last), 6);
        }

        [Fact]
        public void A_voice_started_after_a_stop_all_is_still_heard()
        {
            // The other direction, and the one a naive fix breaks: releasing every voice on a
            // stop-all must not swallow the sound that comes after it. A "make it stop" that
            // also makes the next thing silent is its own bug.
            var engine = new AudioEngine();
            engine.SetMasterGain(1.0f);
            engine.SetVoice(0, 440, 0.9f, 0, "sine", continuous: true, durationSec: 10.0);
            ReadOneBuffer(engine);

            engine.StopAll();
            for (int i = 0; i < 40; i++) ReadOneBuffer(engine);   // let it go quiet

            engine.SetVoice(3, 660, 0.9f, 0, "sine", continuous: true, durationSec: 10.0);

            double loudest = 0;
            for (int i = 0; i < 40; i++) loudest = Math.Max(loudest, Rms(ReadOneBuffer(engine)));

            Assert.True(loudest > 0.01, $"the voice after the stop-all was inaudible (RMS {loudest}).");
        }

        // ── Frequency bounds ─────────────────────────────────────────────────

        /// <summary>A quiet, ordinary voice — the reference the pathological cases are
        /// compared against. Measured at RMS ~0.2523.</summary>
        private static double BaselineRms()
        {
            var engine = new AudioEngine();
            engine.SetMasterGain(1.0f);
            engine.SetVoice(0, 440, 0.5f, 0, "sine", continuous: true, durationSec: 10.0);

            double rms = 0;
            for (int i = 0; i < 40; i++) rms = Rms(ReadOneBuffer(engine));
            return rms;
        }

        /// <summary>RMS after adding a voice at <paramref name="freq"/> alongside that
        /// ordinary one.</summary>
        private static double RmsWithVoiceAt(double freq, string wave = "sawtooth", float subSaw = 0f)
        {
            var engine = new AudioEngine();
            engine.SetMasterGain(1.0f);
            engine.SetVoice(0, 440, 0.5f, 0, "sine", continuous: true, durationSec: 10.0);
            for (int i = 0; i < 40; i++) ReadOneBuffer(engine);

            engine.SetVoice(1, freq, 0.5f, 0, wave, continuous: true, durationSec: 10.0,
                            subSawMix: subSaw);

            double rms = 0;
            for (int i = 0; i < 40; i++) rms = Rms(ReadOneBuffer(engine));
            return rms;
        }

        [Theory]
        [InlineData(44100.0)]
        [InlineData(44200.0)]
        [InlineData(50_000.0)]
        [InlineData(200_000.0)]
        [InlineData(1e9)]
        public void A_frequency_above_the_sample_rate_does_not_pin_the_output_at_full_scale(double freq)
        {
            // With the defect restored this reads 0.990000 — the limiter ceiling — for every
            // one of these frequencies. A sustained full-scale roar is the harm, whether or
            // not a limiter caps the raw number.
            double rms = RmsWithVoiceAt(freq);

            Assert.True(rms < 0.9,
                $"a voice at {freq} Hz pinned the output at {rms:F6} (the limiter ceiling is 0.99).");
        }

        [Theory]
        [InlineData(-440.0)]
        [InlineData(-50_000.0)]
        public void A_negative_frequency_does_not_pin_the_output_at_full_scale(double freq)
        {
            // A negative frequency ran the accumulator DOWNWARD to the same effect. It is a
            // phase direction, not a pitch, so it clamps to silence rather than its magnitude.
            double rms = RmsWithVoiceAt(freq);

            Assert.True(rms < 0.9, $"a voice at {freq} Hz pinned the output at {rms:F6}.");
        }

        [Fact]
        public void A_sub_saw_layer_is_bounded_too()
        {
            // SubPhase advances at half frequency through the same accumulator and the same
            // linear reader, and had the same single-subtraction wrap.
            double rms = RmsWithVoiceAt(200_000, wave: "sine", subSaw: 1.0f);

            Assert.True(rms < 0.9, $"a sub-saw layer pinned the output at {rms:F6}.");
        }

        [Fact]
        public void An_ordinary_sawtooth_is_still_audible()
        {
            // Vacuity check for every bound above: they would all pass on an engine rendering
            // silence, and "quieter than the ceiling" is trivially true of nothing at all.
            var engine = new AudioEngine();
            engine.SetMasterGain(1.0f);
            engine.SetVoice(0, 440, 0.5f, 0, "sawtooth", continuous: true, durationSec: 10.0);

            double loudest = 0;
            for (int i = 0; i < 40; i++) loudest = Math.Max(loudest, Rms(ReadOneBuffer(engine)));

            Assert.True(loudest > 0.01, $"an ordinary 440 Hz sawtooth was inaudible (RMS {loudest}).");
        }

        [Fact]
        public void The_baseline_is_well_below_the_limiter_ceiling()
        {
            // The other half of the vacuity check: if an ordinary chart already ran the
            // limiter at its ceiling, "below 0.9" would say nothing about the pathological
            // cases. Measured baseline is ~0.2523.
            Assert.InRange(BaselineRms(), 0.05, 0.6);
        }

        [Fact]
        public void An_out_of_range_volume_cannot_exceed_full_scale()
        {
            // Finite is not the same as in range. A volume of 50 is 50x full scale on a
            // channel nobody can turn down in time.
            var engine = new AudioEngine();
            engine.SetMasterGain(1.0f);
            engine.SetVoice(0, 440, 50f, 0, "sine", continuous: true, durationSec: 10.0);

            float loudest = 0;
            for (int i = 0; i < 40; i++) loudest = Math.Max(loudest, Peak(ReadOneBuffer(engine)));

            Assert.True(loudest <= 1.0f, $"volume 50 peaked at {loudest}.");
        }
    }
}
