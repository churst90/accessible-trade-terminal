using System;
using AccessibleTrader.Core.Services.Audio;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>A volume the user set is the volume that plays. Silence included.</b>
    ///
    /// <para>
    /// <c>AudioEngine.Read</c> re-arms the master gain when a voice command arrives after
    /// <c>StopAll</c> has faded output to zero — otherwise the next note after a stop would be
    /// inaudible. That re-arm used to snap the target to a hardcoded <c>1.0f</c> and its only
    /// condition was "current target is zero", which cannot tell OUR zero from the user's.
    /// </para>
    ///
    /// <para>
    /// So a deliberate mute was undone by the next keystroke. Chart volume to 0%, speech confirms
    /// "0 percent", press an arrow key, and master gain snapped back to FULL. Navigation notes
    /// stayed quiet because chart volume also multiplies into each note's own volume — but earcons
    /// pass fixed literal volumes, so an order fill, a stop hit or a boundary cue then fired at
    /// full scale on an output the user had silenced. The mute looked like it worked right up
    /// until the moment it mattered.
    /// </para>
    ///
    /// <para>
    /// Proven to fail: restoring <c>if (cmd.IsActive &amp;&amp; _targetMasterGain == 0.0f)
    /// _targetMasterGain = 1.0f;</c> turns <see cref="AUserSetGainOfZeroSurvivesTheNextVoiceCommand"/>
    /// and <see cref="AUserSetGainIsRestoredAfterAStopAllFade"/> red.
    /// </para>
    /// </summary>
    public class MasterGainTests
    {
        private const int BufferFrames = 256;

        private static float[] ReadOneBuffer(AudioEngine engine)
        {
            var buf = new float[BufferFrames * 2];
            engine.Read(buf, 0, buf.Length);
            return buf;
        }

        /// <summary>Peak absolute sample across <paramref name="buffers"/> rendered buffers.</summary>
        private static float PeakOver(AudioEngine engine, int buffers)
        {
            float peak = 0f;
            for (int b = 0; b < buffers; b++)
            {
                var buf = ReadOneBuffer(engine);
                foreach (var s in buf) peak = Math.Max(peak, Math.Abs(s));
            }
            return peak;
        }

        /// <summary>
        /// Render and discard, long enough for a master-gain ramp to finish. Gain changes are
        /// deliberately faded over 882 frames rather than snapped (that fade is what stops the
        /// click), so a measurement taken across the ramp sees the OLD level on its way out and
        /// says nothing about where the engine settled. These tests are about where it settles.
        /// </summary>
        private static void Settle(AudioEngine engine) => PeakOver(engine, 20);

        [Fact]
        public void AUserSetGainOfZeroSurvivesTheNextVoiceCommand()
        {
            var engine = new AudioEngine();
            engine.SetMasterGain(0f);                                     // the user mutes
            Settle(engine);

            engine.SetVoice(0, 440, 1.0f, 0f, "sine", true, 10.0);        // the next arrow key
            Settle(engine);

            Assert.Equal(0f, PeakOver(engine, 40));
        }

        /// <summary>
        /// The same thing an earcon does: a short one-shot on its own slot at a fixed literal
        /// volume, with no chart-volume factor anywhere in its path. This is the sound the user
        /// actually heard at full scale on a muted master.
        /// </summary>
        [Fact]
        public void AnEarconStyleOneShotIsSilentOnAMutedMaster()
        {
            var engine = new AudioEngine();
            engine.SetMasterGain(0f);
            Settle(engine);

            engine.SetVoice(20, 880, 0.14f, 0f, "square", false, 0.09);
            Assert.Equal(0f, PeakOver(engine, 40));
        }

        /// <summary>
        /// The condition the re-arm exists for, which must keep working: StopAll fades output to
        /// zero, and the next note has to be audible again — at the gain the USER chose, not at 1.0.
        /// </summary>
        [Fact]
        public void AUserSetGainIsRestoredAfterAStopAllFade()
        {
            var engine = new AudioEngine();
            engine.SetMasterGain(0.25f);

            engine.SetVoice(0, 440, 1.0f, 0f, "sine", true, 10.0);
            Settle(engine);
            float before = PeakOver(engine, 40);
            Assert.True(before > 0.05f, "sanity: the voice must be audible at 25% before the stop");

            engine.StopAll();
            Settle(engine);                                                // let the fade complete

            engine.SetVoice(0, 440, 1.0f, 0f, "sine", true, 10.0);         // a new note re-arms
            Settle(engine);
            float after = PeakOver(engine, 60);

            Assert.True(after > 0.05f, "the re-arm must make sound again after a stop-all fade");
            // Restored to the user's 0.25, not to a hardcoded 1.0. The bound is generous because
            // the ramp and the per-voice envelope both move; what it rules out is a 4x jump.
            Assert.True(after < 0.5f,
                $"master gain was restored to roughly {after:F2}, not the user's 0.25 — the re-arm "
                + "is inventing a volume the user never chose.");
        }

        /// <summary>
        /// A stop-all fade must not be able to strand the engine silent: the flag is one-shot, so
        /// a second stop/restart cycle behaves like the first.
        /// </summary>
        [Fact]
        public void RepeatedStopAndRestartCyclesStayAudible()
        {
            var engine = new AudioEngine();
            engine.SetMasterGain(1.0f);

            for (int cycle = 0; cycle < 3; cycle++)
            {
                engine.SetVoice(0, 440, 0.8f, 0f, "sine", true, 10.0);
                Settle(engine);
                Assert.True(PeakOver(engine, 40) > 0.05f, $"cycle {cycle}: voice should be audible");
                engine.StopAll();
                Settle(engine);
            }
        }

        /// <summary>
        /// <c>Reset</c> also zeroes the gain, and that zero is equally ours — a chart teardown must
        /// not leave the engine permanently silent for the next chart.
        /// </summary>
        [Fact]
        public void ResetIsRecoveredFromLikeAStopAll()
        {
            var engine = new AudioEngine();
            engine.SetMasterGain(1.0f);
            engine.Reset();

            engine.SetVoice(0, 440, 0.8f, 0f, "sine", true, 10.0);
            Settle(engine);
            Assert.True(PeakOver(engine, 60) > 0.05f);
        }

        /// <summary>
        /// An explicit request always wins, including one that arrives while a stop-all fade is in
        /// flight — otherwise the pending re-arm would overwrite it a moment later.
        /// </summary>
        [Fact]
        public void SettingTheGainDuringAStopAllFadeIsHonoured()
        {
            var engine = new AudioEngine();
            engine.SetMasterGain(1.0f);
            engine.SetVoice(0, 440, 1.0f, 0f, "sine", true, 10.0);
            PeakOver(engine, 5);

            engine.StopAll();
            engine.SetMasterGain(0f);          // user mutes mid-fade

            engine.SetVoice(0, 440, 1.0f, 0f, "sine", true, 10.0);
            Settle(engine);
            Assert.Equal(0f, PeakOver(engine, 40));
        }
    }
}
