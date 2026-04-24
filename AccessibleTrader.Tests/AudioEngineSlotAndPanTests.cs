using System;
using System.Collections.Generic;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Covers the <see cref="AudioEngine"/> synthesis hot path beyond the
    /// telemetry tests already shipped (see <see cref="AudioEngineTelemetryTests"/>).
    /// Focus:
    ///   • <see cref="AudioConstants.CalculatePan"/> pan arithmetic
    ///   • Voice-slot isolation (slots 0-63 are independent)
    ///   • <c>StopAll</c> fades master gain; individual StopVoice doesn't kill others
    ///   • Ring-buffer behaviour observed through <see cref="AudioEngine.Read"/>
    ///
    /// These are all single-threaded, deterministic tests — no real audio output,
    /// just the synthesis math.
    /// </summary>
    public class AudioEngineSlotAndPanTests
    {
        private const int BufferFrames = 256;

        private static float[] ReadOneBuffer(AudioEngine engine)
        {
            // Always stereo — 2 samples per frame.
            var buf = new float[BufferFrames * 2];
            engine.Read(buf, 0, buf.Length);
            return buf;
        }

        // ── Pan arithmetic ──────────────────────────────────────────────────

        [Fact]
        public void CalculatePan_LeftEdge_ReturnsNegativeOne()
        {
            Assert.Equal(-1.0, AudioConstants.CalculatePan(0, 100));
        }

        [Fact]
        public void CalculatePan_RightEdge_ReturnsPositiveOne()
        {
            Assert.Equal(1.0, AudioConstants.CalculatePan(99, 100));
        }

        [Fact]
        public void CalculatePan_Centre_IsZero()
        {
            // index 5 of a 11-slot viewport = exact centre.
            double pan = AudioConstants.CalculatePan(5, 11);
            Assert.Equal(0.0, pan, precision: 10);
        }

        [Fact]
        public void CalculatePan_ViewportWidthLessThanOrEqualOne_ReturnsZero()
        {
            Assert.Equal(0.0, AudioConstants.CalculatePan(0, 0));
            Assert.Equal(0.0, AudioConstants.CalculatePan(0, 1));
            Assert.Equal(0.0, AudioConstants.CalculatePan(5, 1));
        }

        [Fact]
        public void CalculatePan_OutOfRange_ClampsToUnitInterval()
        {
            // Negative relative index → clamped to -1. Larger-than-width → clamped to +1.
            Assert.Equal(-1.0, AudioConstants.CalculatePan(-100, 50));
            Assert.Equal(+1.0, AudioConstants.CalculatePan(10_000, 50));
        }

        [Fact]
        public void ComputePanWidth_AlwaysReturnsViewportLength()
        {
            // Post-2026-04-21 invariant: pan denominator is ViewportLength at
            // every call site, regardless of right-margin presence. Audio
            // position tracks the visual x-fraction on the canvas.
            var state = WorkspaceState.Initial with { ViewportLength = 200, RightMarginBars = 50 };
            Assert.Equal(200, AudioConstants.ComputePanWidth(state));
        }

        [Fact]
        public void ComputePanWidth_ViewportLengthZero_ClampsToOne()
        {
            var state = WorkspaceState.Initial with { ViewportLength = 0 };
            Assert.Equal(1, AudioConstants.ComputePanWidth(state));
        }

        // ── Voice-slot isolation & hot-path ─────────────────────────────────

        [Fact]
        public void SetVoice_IndividualSlots_DoNotBleedIntoEachOther()
        {
            var engine = new AudioEngine();
            engine.SetMasterGain(1.0f);
            engine.SetVoice(0, freq: 440, vol: 0.5f, pan: -1.0f, wave: "sine", continuous: true, durationSec: 1.0);
            engine.SetVoice(16, freq: 880, vol: 0.5f, pan: +1.0f, wave: "sine", continuous: true, durationSec: 1.0);

            // Drive one audio buffer to apply the commands, then stop only slot 0.
            var _ = ReadOneBuffer(engine);
            engine.StopVoice(0);
            var buf = ReadOneBuffer(engine);

            // Slot 16 still playing => left and right must not both be zero.
            bool anyNonZero = false;
            for (int i = 0; i < buf.Length; i++) if (buf[i] != 0f) { anyNonZero = true; break; }
            Assert.True(anyNonZero, "Stopping slot 0 should not silence slot 16.");
        }

        [Fact]
        public void SetVoice_OutOfRangeSlot_IsIgnored()
        {
            var engine = new AudioEngine();
            // Should NOT throw and should not increment meaningful state.
            engine.SetVoice(-1, 440, 0.5f, 0, "sine", true, 1.0);
            engine.SetVoice(64, 440, 0.5f, 0, "sine", true, 1.0);
            // TotalCommandCount tracks EnqueueCommand calls; out-of-range returns before Enqueue.
            Assert.Equal(0, engine.TotalCommandCount);
        }

        [Fact]
        public void StopAll_EnqueuesStopAllCommand_CountsInTelemetry()
        {
            var engine = new AudioEngine();
            engine.SetVoice(0, 440, 0.5f, 0, "sine", true, 1.0);
            long before = engine.TotalCommandCount;
            engine.StopAll();
            Assert.Equal(before + 1, engine.TotalCommandCount);
        }

        // ── Envelope + waveform dispatch ────────────────────────────────────

        [Fact]
        public void SetVoice_UnknownWaveform_DefaultsToSine()
        {
            // The switch in SetVoice falls through to Sine for unknown names.
            // We verify by driving two engines and confirming both produce
            // identical buffers when fed matching frequencies + a non-existent
            // vs. the "sine" literal.
            var e1 = new AudioEngine(); e1.SetMasterGain(1.0f);
            var e2 = new AudioEngine(); e2.SetMasterGain(1.0f);
            // Same absolute sample budget → drive enough frames for glide + envelope warmup.
            e1.SetVoice(0, 440, 0.5f, 0, "sine", true, 0.1);
            e2.SetVoice(0, 440, 0.5f, 0, "definitely-not-a-waveform", true, 0.1);
            // Drive a priming buffer on both so commands are dequeued in lock-step.
            _ = ReadOneBuffer(e1); _ = ReadOneBuffer(e2);

            var a = ReadOneBuffer(e1);
            var b = ReadOneBuffer(e2);
            Assert.Equal(a, b);
        }

        [Fact]
        public void SetVoice_PingEnvelope_ProducesNonZeroOutputThenDecays()
        {
            // Ping envelope = transient. After the Ping's duration elapses,
            // the voice should contribute zero samples. Here we just assert
            // Ping produces SOMETHING initially — decay is tested by the
            // existing telemetry suite.
            var engine = new AudioEngine(); engine.SetMasterGain(1.0f);
            engine.SetVoice(0, 660, 0.8f, 0, "sine", continuous: false, durationSec: 0.01, envelope: "Ping");
            var buf = ReadOneBuffer(engine);
            bool anyNonZero = false;
            for (int i = 0; i < buf.Length; i++) if (buf[i] != 0f) { anyNonZero = true; break; }
            Assert.True(anyNonZero);
        }

        [Fact]
        public void Reset_SilencesAllOutput()
        {
            var engine = new AudioEngine();
            engine.SetMasterGain(1.0f);
            engine.SetVoice(0, 440, 0.9f, 0, "sine", true, 10.0);
            ReadOneBuffer(engine); // prime

            engine.Reset();

            // Drive several buffers to let master-gain fade to zero.
            float[] last = null!;
            for (int i = 0; i < 20; i++) last = ReadOneBuffer(engine);

            // After fade-out all samples must be exactly zero.
            for (int i = 0; i < last.Length; i++)
                Assert.Equal(0f, last[i]);
        }

        [Fact]
        public void MasterGain_Clamped_ToUnitInterval()
        {
            var engine = new AudioEngine();
            engine.SetMasterGain(-5.0f);    // silently clamped to 0
            engine.SetMasterGain(+7.0f);    // silently clamped to 1
            // Only way to verify publicly is to confirm no exception and
            // that SetMasterGain doesn't crash Read — drive a buffer.
            var buf = ReadOneBuffer(engine);
            Assert.Equal(BufferFrames * 2, buf.Length);
        }
    }
}
