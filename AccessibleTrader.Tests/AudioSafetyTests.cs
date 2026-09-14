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

        /// <summary>
        /// Finite is not the same as in range. A volume of 50 is 50x full scale on a channel
        /// nobody can turn down in time.
        ///
        /// <para>
        /// <b>Asserted RELATIONALLY, and the reason is this file's own docstring.</b> The original
        /// version checked <c>Peak &lt;= 1.0f</c>, which the brickwall limiter downstream
        /// guarantees no matter what the clamp does — widen the clamp to <c>[0, 10]</c> and the
        /// peak is still 1.0, because the limiter caught it. That is guarding the limiter, not the
        /// clamp. The class docstring above works this trap out for the FREQUENCY tests and then
        /// the volume test walked into it one method later; it is why the A2g mutant set could
        /// widen this clamp tenfold with nothing going red.
        /// </para>
        ///
        /// <para>
        /// An absolute RMS bound does not fix it either: after the limiter a sine is still a sine,
        /// so an over-loud one comes back at ceiling/√2 ≈ 0.70 rather than pinned at 0.99, and no
        /// single threshold cleanly separates that from a legitimate 0.50. What DOES state the
        /// contract is the relation: asking for more than full scale must give you <i>exactly
        /// full scale</i> — identical output to asking for 1.0. Under the widened clamp the loud
        /// voice engages the limiter and the two stop matching (0.50 against 0.70).
        /// </para>
        /// </summary>
        [Theory]
        [InlineData(1.5f)]
        [InlineData(50f)]
        [InlineData(1000f)]
        public void An_out_of_range_volume_renders_exactly_as_full_scale_does(float volume)
        {
            double atFullScale = SteadyRms(1.0f);
            double asked = SteadyRms(volume);

            Assert.True(atFullScale > 0.05,
                $"the full-scale reference was inaudible (RMS {atFullScale:F6}) — this proves nothing.");
            Assert.True(Math.Abs(asked - atFullScale) < 0.02,
                $"volume {volume} rendered at RMS {asked:F6} where volume 1.0 renders at {atFullScale:F6} — " +
                "the clamp at the SetVoice boundary is not holding, and the limiter is absorbing the difference.");

            static double SteadyRms(float vol)
            {
                var engine = new AudioEngine();
                engine.SetMasterGain(1.0f);
                engine.SetVoice(0, 440, vol, 0, "sine", continuous: true, durationSec: 10.0);
                double rms = 0;
                for (int i = 0; i < 40; i++) rms = Rms(ReadOneBuffer(engine));
                return rms;
            }
        }

        [Fact]
        public void An_ordinary_voice_is_quieter_than_a_full_scale_one()
        {
            // Vacuity twin for the relation above: "matches full scale" would be satisfiable by
            // an engine that rendered every volume identically. Half the volume must be quieter.
            var loud = new AudioEngine();
            loud.SetMasterGain(1.0f);
            loud.SetVoice(0, 440, 1.0f, 0, "sine", continuous: true, durationSec: 10.0);

            var quiet = new AudioEngine();
            quiet.SetMasterGain(1.0f);
            quiet.SetVoice(0, 440, 0.3f, 0, "sine", continuous: true, durationSec: 10.0);

            double loudRms = 0, quietRms = 0;
            for (int i = 0; i < 40; i++) { loudRms = Rms(ReadOneBuffer(loud)); quietRms = Rms(ReadOneBuffer(quiet)); }

            Assert.True(quietRms < loudRms * 0.6,
                $"volume 0.3 ({quietRms:F6}) was not meaningfully quieter than volume 1.0 ({loudRms:F6}).");
        }

        // ── The user's own zero ──────────────────────────────────────────────

        /// <summary>
        /// <b>A volume the user chose is not a condition to recover from.</b>
        ///
        /// <para>
        /// <c>Read()</c> re-arms the master gain after a stop-all fade, because a stop-all drives
        /// the target to zero and something has to bring it back. It used to re-arm to a hardcoded
        /// <c>1.0f</c>, which could not tell "the stop-all just faded us to zero" from "the user
        /// set the volume to zero": setting the volume to 0% and pressing one arrow key restored
        /// FULL output, so a mute was never a mute — and the order-fill, stop-hit and boundary
        /// earcons all pass fixed literal volumes, so they would fire at full scale on a master
        /// the user had deliberately silenced.
        /// </para>
        ///
        /// <para>
        /// <b>What actually protects the zero is re-arming to <c>_userMasterGain</c>.</b> The A2g
        /// mutant set dropped the companion <c>&amp;&amp; _stopAllFaded</c> guard and nothing broke;
        /// a three-way follow-up (flag alone / literal alone / both) showed why — the flag is
        /// belt and braces, since re-arming to the user's own value is idempotent, while restoring
        /// a literal is the defect. These tests are written against the half that carries the
        /// weight, and they go red the moment that literal comes back.
        /// </para>
        /// </summary>
        [Fact]
        public void A_master_gain_the_user_set_to_zero_survives_the_next_voice_command()
        {
            var engine = new AudioEngine();
            engine.SetMasterGain(1.0f);
            engine.SetVoice(0, 440, 0.9f, 0, "sine", continuous: true, durationSec: 10.0);
            ReadOneBuffer(engine);                      // prime, so there is something to un-mute

            engine.SetMasterGain(0f);                   // the user chooses silence
            for (int i = 0; i < 40; i++) ReadOneBuffer(engine);   // let the fade complete

            // An arrow key. An earcon. Anything at all.
            engine.SetVoice(16, 880, 1.0f, 0, "sine", continuous: true, durationSec: 10.0);

            double loudest = 0;
            for (int i = 0; i < 40; i++) loudest = Math.Max(loudest, Rms(ReadOneBuffer(engine)));

            Assert.True(loudest < 0.001,
                $"a voice command restored output to RMS {loudest:F6} on a master the user had set to zero.");
        }

        [Fact]
        public void A_master_gain_the_user_set_to_zero_survives_a_stop_all_as_well()
        {
            // The interaction the flag exists for: our own stop-all arrives while the user's zero
            // is already in force, and the voice command behind it must not re-arm to full.
            //
            // The zero is allowed to SETTLE first, deliberately. Measuring from the instant
            // SetMasterGain(0f) is called would catch the ~20 ms fade-down on its way to silence
            // and report it as a failure — that ramp is the declick, and it is supposed to be
            // there.
            var engine = new AudioEngine();
            engine.SetMasterGain(1.0f);
            engine.SetVoice(0, 440, 0.9f, 0, "sine", continuous: true, durationSec: 10.0);
            ReadOneBuffer(engine);

            engine.SetMasterGain(0f);
            for (int i = 0; i < 40; i++) ReadOneBuffer(engine);   // the user's silence takes hold

            engine.StopAll();
            engine.SetVoice(16, 880, 1.0f, 0, "sine", continuous: true, durationSec: 10.0);

            double loudest = 0;
            for (int i = 0; i < 60; i++) loudest = Math.Max(loudest, Rms(ReadOneBuffer(engine)));

            Assert.True(loudest < 0.001, $"output came back at RMS {loudest:F6} after a stop-all at user-zero.");
        }

        /// <summary>
        /// Vacuity twin for both of the above: an engine that had simply latched itself off would
        /// satisfy them, and a mute nobody can undo is its own bug.
        ///
        /// <para>
        /// A voice is re-armed after the gain goes back up, because that is both what the app does
        /// (the user raises the volume, then presses a key) and what the engine requires: once
        /// <c>_masterGain</c> actually reaches zero the per-frame loop deactivates every voice, so
        /// there is nothing left for a gain change alone to bring back.
        /// </para>
        /// </summary>
        [Fact]
        public void Raising_the_gain_again_does_bring_the_sound_back()
        {
            var engine = new AudioEngine();
            engine.SetMasterGain(0f);
            engine.SetVoice(0, 440, 0.9f, 0, "sine", continuous: true, durationSec: 10.0);
            for (int i = 0; i < 40; i++) ReadOneBuffer(engine);

            engine.SetMasterGain(1.0f);
            engine.SetVoice(0, 440, 0.9f, 0, "sine", continuous: true, durationSec: 10.0);

            double loudest = 0;
            for (int i = 0; i < 40; i++) loudest = Math.Max(loudest, Rms(ReadOneBuffer(engine)));

            Assert.True(loudest > 0.01, $"raising the master gain left the engine silent (RMS {loudest:F6}).");
        }

        // ── The Ping envelope ────────────────────────────────────────────────

        /// <summary>
        /// <b>A Ping is a transient, and the decay rate is what makes it one.</b>
        ///
        /// <para>
        /// Markers — dots, arrows, wicks, signal shapes — are Ping-envelope voices, and the whole
        /// reason they read as discrete events rather than notes is that <c>exp(-5·progress)</c>
        /// drops them to ~0.7% of their onset level by the end of their duration. Flatten the
        /// exponent and every marker holds most of its level for its whole duration, smearing the
        /// sparse signals into the continuous bed they are supposed to stand out from.
        /// </para>
        ///
        /// <para>
        /// Asserted as a RATIO between the start and end of one ping rather than against a
        /// magic number, so it states the shape rather than the constant: the tail must be a
        /// small fraction of the onset. With the exponent at -0.5 the measured ratio is 0.61.
        /// </para>
        /// </summary>
        [Fact]
        public void A_ping_decays_to_a_fraction_of_its_onset()
        {
            var engine = new AudioEngine();
            engine.SetMasterGain(1.0f);
            // Long enough that one buffer is a small slice of the envelope, so the first and
            // last buffers really are the onset and the tail.
            engine.SetVoice(0, 440, 0.9f, 0, "sine", continuous: false, durationSec: 0.5,
                            dataIndex: -1, envelope: "Ping");

            double onset = 0, tail = 0;
            const int Buffers = 20;          // 20 × 512 frames ≈ 0.23 s at 44.1 kHz
            for (int i = 0; i < Buffers; i++)
            {
                double r = Rms(ReadOneBuffer(engine));
                if (i == 1) onset = r;       // buffer 1: past the 12 ms attack fade, still early
                tail = r;
            }

            Assert.True(onset > 0.01, $"the ping was inaudible at its onset (RMS {onset:F6}) — this proves nothing.");
            Assert.True(tail < onset * 0.35,
                $"a Ping held {tail / onset:P0} of its onset level to the end of its duration " +
                $"(onset RMS {onset:F6}, tail RMS {tail:F6}) — that is a note, not a transient.");
        }

        [Fact]
        public void A_sustain_voice_does_not_decay_like_a_ping()
        {
            // The contrast that makes the assertion above about the PING envelope specifically,
            // rather than about any voice fading out on its own.
            var engine = new AudioEngine();
            engine.SetMasterGain(1.0f);
            engine.SetVoice(0, 440, 0.9f, 0, "sine", continuous: true, durationSec: 10.0,
                            dataIndex: -1, envelope: "Sustain");

            double onset = 0, tail = 0;
            for (int i = 0; i < 20; i++)
            {
                double r = Rms(ReadOneBuffer(engine));
                if (i == 1) onset = r;
                tail = r;
            }

            Assert.True(tail > onset * 0.8,
                $"a Sustain voice decayed from {onset:F6} to {tail:F6} — it is meant to hold.");
        }
    }
}
