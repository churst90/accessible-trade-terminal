using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Wavetable oscillator + one-shot sample playback + WAV import parsing.
    /// Rendering tests reuse the perceptual approach: real engine output, energy
    /// and frequency measured, not intent.
    /// </summary>
    public class WavetableTests
    {
        // ── WAV builder (in-memory test fixtures) ────────────────────────────

        private static byte[] BuildWav(float[] samples, int rate, int bits = 16, int channels = 1, bool asFloat = false)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            int bytesPerSample = asFloat ? 4 : bits / 8;
            int dataLen = samples.Length * bytesPerSample * channels;
            w.Write("RIFF"u8); w.Write(36 + dataLen); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16);
            w.Write((short)(asFloat ? 3 : 1)); w.Write((short)channels);
            w.Write(rate); w.Write(rate * bytesPerSample * channels);
            w.Write((short)(bytesPerSample * channels)); w.Write((short)(asFloat ? 32 : bits));
            w.Write("data"u8); w.Write(dataLen);
            foreach (var s in samples)
                for (int c = 0; c < channels; c++)
                {
                    if (asFloat) w.Write(s);
                    else if (bits == 16) w.Write((short)(Math.Clamp(s, -1f, 1f) * 32767));
                    else if (bits == 8) w.Write((byte)(Math.Clamp(s, -1f, 1f) * 127 + 128));
                }
            return ms.ToArray();
        }

        private static float[] SineCycle(int len)
        {
            var t = new float[len];
            for (int i = 0; i < len; i++) t[i] = (float)Math.Sin(2 * Math.PI * i / len);
            return t;
        }

        // ── WavFileReader ────────────────────────────────────────────────────

        [Theory]
        [InlineData(16, false)]
        [InlineData(8, false)]
        [InlineData(32, true)]
        public void WavReader_RoundTripsAmplitudeAndRate(int bits, bool asFloat)
        {
            var src = SineCycle(600);
            var bytes = BuildWav(src, 48000, bits, channels: 1, asFloat: asFloat);

            Assert.True(WavFileReader.TryParse(bytes, out var mono, out int rate, out var err), err);
            Assert.Equal(48000, rate);
            Assert.Equal(600, mono.Length);
            // Peak within quantization tolerance of 1.0 (8-bit is coarse).
            double tol = bits == 8 ? 0.03 : 0.001;
            Assert.InRange(mono[150], 1.0 - tol, 1.0 + tol); // sin peak at quarter cycle
        }

        [Fact]
        public void WavReader_MonoizesStereo()
        {
            var src = SineCycle(100);
            var bytes = BuildWav(src, 44100, 16, channels: 2);
            Assert.True(WavFileReader.TryParse(bytes, out var mono, out _, out var err), err);
            Assert.Equal(100, mono.Length); // frames, not interleaved samples
        }

        /// <summary>
        /// <b>PCM8 is unsigned: silence is 128, not 0.</b>
        ///
        /// <para>
        /// <c>WavReader_RoundTripsAmplitudeAndRate</c> above checks the PEAK of an 8-bit file, and
        /// the peak is exactly where this cannot be seen: drop the <c>- 128</c> and the peak
        /// sample reads 1.992, which <c>Math.Clamp(sum / channels, -1, 1)</c> pins at 1.0 — the
        /// same number the correct reader produces. The A2g mutant set removed that subtraction
        /// and nothing failed.
        /// </para>
        ///
        /// <para>
        /// The zero crossing is where an offset shows. A silent 8-bit sample is the byte 128, and
        /// it has to come back as 0: read unsigned it comes back as 1.0, so an imported earcon
        /// arrives as a full-scale DC step — a click the limiter then ducks the whole mix for.
        /// </para>
        /// </summary>
        [Fact]
        public void WavReader_ReadsEightBitSilenceAsSilence()
        {
            // BuildWav maps 0f to byte 128, which is PCM8's zero point.
            var bytes = BuildWav(new float[] { 0f, 0f, 0f, 0f }, 44100, bits: 8);

            Assert.True(WavFileReader.TryParse(bytes, out var mono, out _, out var err), err);
            Assert.All(mono, s => Assert.InRange(s, -0.02f, 0.02f));
        }

        /// <summary>
        /// And the negative half of the range, which an unsigned read cannot reach at all: every
        /// sample of an unsigned read is ≥ 0, so a waveform that swings below zero comes back
        /// rectified as well as offset.
        /// </summary>
        [Fact]
        public void WavReader_ReadsEightBitNegativeSamplesAsNegative()
        {
            var src = SineCycle(600);
            var bytes = BuildWav(src, 44100, bits: 8);

            Assert.True(WavFileReader.TryParse(bytes, out var mono, out _, out var err), err);

            Assert.True(mono.Any(s => s < -0.5f),
                "no sample of an 8-bit sine came back below -0.5 — the unsigned byte range was " +
                "not re-centred, so the whole clip is offset and rectified.");
            Assert.InRange(mono[450], -1.0 - 0.03, -1.0 + 0.03);   // sin trough at three-quarter cycle
        }

        [Fact]
        public void WavReader_RejectsGarbage()
        {
            Assert.False(WavFileReader.TryParse(new byte[10], out _, out _, out var e1));
            Assert.NotEmpty(e1);
            var junk = new byte[100];
            new Random(7).NextBytes(junk);
            Assert.False(WavFileReader.TryParse(junk, out _, out _, out var e2));
            Assert.NotEmpty(e2);
        }

        // ── Wavetable voices ─────────────────────────────────────────────────

        private static float[] Render(AudioEngine engine, int samples, int settle = 8192)
        {
            var buf = new float[1024];
            for (int done = 0; done < settle; done += buf.Length) engine.Read(buf, 0, buf.Length);
            var outBuf = new float[samples];
            for (int done = 0; done < samples; done += buf.Length)
            {
                engine.Read(buf, 0, buf.Length);
                Array.Copy(buf, 0, outBuf, done, Math.Min(buf.Length, samples - done));
            }
            return outBuf;
        }

        private static double Rms(float[] s)
        {
            double sum = 0;
            foreach (var x in s) sum += (double)x * x;
            return Math.Sqrt(sum / s.Length);
        }

        /// <summary>Fundamental frequency estimate via zero-crossing count (mono channel).</summary>
        private static double EstimateFrequency(float[] stereo, int rate)
        {
            int crossings = 0;
            float prev = stereo[0];
            for (int i = 2; i < stereo.Length; i += 2) // left channel
            {
                float cur = stereo[i];
                if ((prev <= 0 && cur > 0) || (prev >= 0 && cur < 0)) crossings++;
                prev = cur;
            }
            double seconds = (stereo.Length / 2.0) / rate;
            return crossings / 2.0 / seconds;
        }

        [Fact]
        public void WavetableVoice_PlaysAtTheRequestedPitch()
        {
            WavetableBank.RegisterWavetable("test_sine600", SineCycle(600));
            var engine = new AudioEngine();
            engine.SetVoice(0, 440, 0.5f, 0f, "wavetable:test_sine600", true, 10);

            var audio = Render(engine, 88200); // 1 s stereo
            Assert.True(Rms(audio) > 0.05, "Wavetable voice is silent.");
            double freq = EstimateFrequency(audio, 44100);
            Assert.InRange(freq, 440 * 0.97, 440 * 1.03);
        }

        [Fact]
        public void WavetableVoice_CustomShape_DiffersFromSine()
        {
            // A bright saw-like table must not render as a plain sine.
            var saw = new float[600];
            for (int i = 0; i < 600; i++) saw[i] = 2f * i / 600f - 1f;
            WavetableBank.RegisterWavetable("test_saw600", saw);

            var e1 = new AudioEngine(); e1.SetVoice(0, 220, 0.5f, 0f, "wavetable:test_saw600", true, 10);
            var e2 = new AudioEngine(); e2.SetVoice(0, 220, 0.5f, 0f, "sine", true, 10);
            var a = Render(e1, 44100);
            var b = Render(e2, 44100);
            double diff = 0;
            for (int i = 0; i < a.Length; i++) { double d = a[i] - b[i]; diff += d * d; }
            Assert.True(Math.Sqrt(diff / a.Length) > 0.05, "Custom wavetable rendered like a sine.");
        }

        [Fact]
        public void UnknownWavetable_FallsBackToSine_NeverSilent()
        {
            var engine = new AudioEngine();
            engine.SetVoice(0, 440, 0.5f, 0f, "wavetable:does_not_exist", true, 10);
            Assert.True(Rms(Render(engine, 44100)) > 0.05,
                "Missing wavetable must fall back to an audible sine, not silence.");
        }

        // ── One-shot samples ─────────────────────────────────────────────────

        [Fact]
        public void SampleVoice_PlaysOnce_ThenGoesSilent()
        {
            // 0.25 s of constant-ish tone at the engine rate.
            var clip = new float[11025];
            for (int i = 0; i < clip.Length; i++) clip[i] = (float)Math.Sin(2 * Math.PI * 500 * i / 44100.0);
            WavetableBank.RegisterSample("test_oneshot", clip, 44100);

            var engine = new AudioEngine();
            engine.SetVoice(0, 440, 0.6f, 0f, "sample:test_oneshot", true, 10);

            var during = Render(engine, 8820, settle: 2048);  // ~0.1 s in — playing
            var after = Render(engine, 8820, settle: 44100);  // ~0.6 s later — clip is over
            Assert.True(Rms(during) > 0.05, "Sample voice is silent while the clip should play.");
            Assert.True(Rms(after) < 0.01, $"Sample voice still sounding after the clip end ({Rms(after):F4} RMS).");
        }

        [Fact]
        public void SampleVoice_ResamplesToNaturalSpeed()
        {
            // A 500 Hz tone recorded at 22.05 kHz must still sound at 500 Hz on the
            // 44.1 kHz engine (step 0.5), not chipmunked to 1000 Hz.
            var clip = new float[22050]; // 1 s at source rate
            for (int i = 0; i < clip.Length; i++) clip[i] = (float)Math.Sin(2 * Math.PI * 500 * i / 22050.0);
            WavetableBank.RegisterSample("test_halfrate", clip, 22050);

            var engine = new AudioEngine();
            engine.SetVoice(0, 0, 0.6f, 0f, "sample:test_halfrate", true, 10);
            var audio = Render(engine, 44100, settle: 4096);
            double freq = EstimateFrequency(audio, 44100);
            Assert.InRange(freq, 500 * 0.95, 500 * 1.05);
        }

        // ── Import: which of the two kinds a file becomes ────────────────────

        private sealed class TempPaths : IPlatformPathService
        {
            // TestTemp rather than a directory of our own: one run-scoped root, removed at
            // process exit. See TestTempScanTests for why the suite insists on it.
            public string AppDataDirectory { get; } = TestTemp.NewDir("att-wavetable-lib");
            public string CacheDirectory => AppDataDirectory;
        }

        /// <summary>
        /// <b>A single cycle and a clip are two different instruments, and length is what tells
        /// them apart.</b>
        ///
        /// <para>
        /// A WAVETABLE is looped at pitch: it behaves like a built-in oscillator with a custom
        /// shape, so pitch mapping, envelopes, noise and partials all still apply. A SAMPLE is
        /// played once at natural speed with no pitch mapping at all. <c>Import</c> decides which
        /// one a file becomes from <c>mono.Length &lt;= WavetableMaxFrames</c>, and nothing tested
        /// that comparison — <c>WavetableLibraryService</c> had no test of any kind.
        /// </para>
        ///
        /// <para>
        /// The A2g mutant set inverted the comparison. An AKWF single cycle (~600 frames) would
        /// then register as a sample: a custom oscillator shape becomes an unpitched 14 ms click,
        /// and every patch referencing it loses its pitch. Nothing failed.
        /// </para>
        /// </summary>
        [Fact]
        public void Import_AShortFileBecomesAWavetable()
        {
            var paths = new TempPaths();
            var lib = new WavetableLibraryService(paths, NullLogger<WavetableLibraryService>.Instance);

            // 600 frames — the AKWF single-cycle length named in the service's own doc comment.
            var (ok, message, id) = lib.Import("akwf_cycle.wav", BuildWav(SineCycle(600), 44100));

            Assert.True(ok, message);
            Assert.NotNull(id);
            Assert.Contains(id!, lib.WavetableIds);
            Assert.DoesNotContain(id!, lib.SampleIds);
            Assert.True(WavetableBank.TryGetWavetable(id!, out _),
                "a 600-frame single cycle did not register as a wavetable, so it cannot be played at pitch.");
        }

        [Fact]
        public void Import_ALongFileBecomesASample()
        {
            var paths = new TempPaths();
            var lib = new WavetableLibraryService(paths, NullLogger<WavetableLibraryService>.Instance);

            // Comfortably past WavetableMaxFrames (4096) — half a second of audio.
            var (ok, message, id) = lib.Import("chime.wav", BuildWav(SineCycle(22050), 44100));

            Assert.True(ok, message);
            Assert.NotNull(id);
            Assert.Contains(id!, lib.SampleIds);
            Assert.DoesNotContain(id!, lib.WavetableIds);
            Assert.True(WavetableBank.TryGetSample(id!, out _, out _),
                "a half-second clip did not register as a sample.");
        }

        /// <summary>
        /// The boundary itself, from both sides. A single off-by-one here silently reclassifies
        /// every file of exactly the threshold length.
        /// </summary>
        [Fact]
        public void Import_TheBoundaryIsInclusiveOnTheWavetableSide()
        {
            var paths = new TempPaths();
            var lib = new WavetableLibraryService(paths, NullLogger<WavetableLibraryService>.Instance);

            var (okAt, _, atLimit) = lib.Import("at_limit.wav",
                BuildWav(SineCycle(WavetableLibraryService.WavetableMaxFrames), 44100));
            var (okOver, _, overLimit) = lib.Import("over_limit.wav",
                BuildWav(SineCycle(WavetableLibraryService.WavetableMaxFrames + 1), 44100));

            Assert.True(okAt);
            Assert.True(okOver);
            Assert.Contains(atLimit!, lib.WavetableIds);
            Assert.Contains(overLimit!, lib.SampleIds);
        }
    }
}
