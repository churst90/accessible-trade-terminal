using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// Real audio output for the Linux WebHost (and any host whose server
    /// process has access to a local audio sink). Constructs its own
    /// <see cref="AudioEngine"/> identical to the MAUI head's
    /// <c>BlazorAudioDriver</c>, then runs a dedicated pump thread that
    /// pulls float32 interleaved L/R frames from the engine and pipes
    /// them as raw PCM into a long-lived <c>pw-cat</c> / <c>pacat</c> /
    /// <c>aplay</c> child process. Back-pressure from the audio daemon
    /// paces the loop.
    ///
    /// Selection order (first found wins, locked at startup):
    /// <list type="number">
    /// <item><c>pw-cat</c> — native PipeWire.</item>
    /// <item><c>pacat</c> — PulseAudio (also satisfies PipeWire's
    /// pulseaudio compatibility layer).</item>
    /// <item><c>aplay</c> — ALSA, last-resort.</item>
    /// </list>
    ///
    /// When none of the three is available (Windows / macOS WebHost
    /// deploys, or a fully headless server) the driver stays silent —
    /// the engine still ticks, journal entries still record voice
    /// commands, and IAudioDriver consumers still get
    /// <see cref="PointReached"/> callbacks. A future L3-B phase will
    /// add a browser WebAudio fallback so the public-website demo gets
    /// audio too.
    /// </summary>
    public sealed class WebHostAudioDriver : IAudioDriver, IDisposable
    {
        private const int BufferFrames = 1024;
        private const int ChannelCount = 2;
        private const int RateHz = 44100;
        private const int FloatsPerBuffer = BufferFrames * ChannelCount;
        private const int BytesPerBuffer = FloatsPerBuffer * sizeof(float);

        private readonly AudioEngine _engine = new();
        private readonly ILogger<WebHostAudioDriver> _logger;
        private readonly WebHostBrowserAudioSink _browserSink;
        private readonly string? _playerPath;
        private readonly string[] _playerArgs = Array.Empty<string>();

        private Process? _player;
        private Stream? _playerStdin;
        private Thread? _pumpThread;
        private CancellationTokenSource? _cts;
        private volatile bool _paused;
        private bool _disposed;
        // True when no local PCM sink was found and we're streaming to the
        // browser instead. Pump uses a wall-clock pacer (no pipe back-pressure
        // to throttle the producer) and short-circuits to silence when no
        // circuit is subscribed.
        private readonly bool _browserMode;

        public event Action<int>? PointReached;
        public int SampleRate => _engine.SampleRate;
        public int Channels   => _engine.Channels;

        public long DroppedCommandCount => _engine.DroppedCommandCount;
        public long TotalCommandCount   => _engine.TotalCommandCount;
        public void ResetAudioTelemetry() => _engine.ResetTelemetry();

        // Host shutdown signal — lets the pump distinguish "player died because we
        // are all shutting down (Ctrl+C)" from "player died mid-session". Optional
        // so unit tests can construct the driver without a host.
        private readonly CancellationToken _hostStopping;

        public WebHostAudioDriver(ILogger<WebHostAudioDriver> logger, WebHostBrowserAudioSink browserSink,
            Microsoft.Extensions.Hosting.IHostApplicationLifetime? lifetime = null)
        {
            _logger = logger;
            _browserSink = browserSink;
            _hostStopping = lifetime?.ApplicationStopping ?? CancellationToken.None;

            // PointReached + drop telemetry flow through to the same surfaces the
            // MAUI driver exposes, so SonificationManager / JournalModal don't care
            // which host they're running under.
            _engine.PointReached += index => PointReached?.Invoke(index);
            _engine.CommandDropped += droppedTotal =>
                _logger.LogWarning(
                    "AudioEngine: {DroppedTotal} command(s) dropped due to buffer overflow. Consider reducing sonification density.",
                    droppedTotal);

            (_playerPath, _playerArgs) = PickPlayer(File.Exists);
            if (_playerPath is null)
            {
                // No local sink — fall back to the browser WebAudio path. The
                // pump still runs but writes chunks to WebHostBrowserAudioSink
                // instead of a child process's stdin, and BrowserAudioBridge
                // forwards them over the SignalR circuit to audio.js.
                _browserMode = true;
                StartPump();
                _logger.LogInformation(
                    "Audio: no local PCM sink found (pw-cat / pacat / aplay). Streaming to browser via WebAudio fallback.");
                return;
            }

            try
            {
                StartPlayer();
                StartPump();
                _logger.LogInformation(
                    "Audio: streaming float32 stereo @ {Rate} Hz via {Player}.",
                    RateHz, Path.GetFileName(_playerPath));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Audio: failed to start {Player}; sonification disabled.",
                    _playerPath);
                DisposePlayer();
            }
        }

        // ── IAudioDriver: every call goes through the engine. Pump thread emits whatever it
        //                 produces. Silence is normal between voice commands. ──────────────

        public void SetVoice(int slot, double frequency, float volume, float pan, string waveform, bool continuous, double durationSeconds = 0.2, int dataIndex = -1, string envelope = "Sustain", bool click = false, float noiseAmount = 0f, string noiseType = "pink", float squareMix = 0f, float sawMix = 0f, float triangleMix = 0f, float subSawMix = 0f)
            => _engine.SetVoice(slot, frequency, volume, pan, waveform, continuous, durationSeconds, dataIndex, envelope, click, noiseAmount, noiseType, squareMix, sawMix, triangleMix, subSawMix);

        public void StopVoice(int slot) => _engine.StopVoice(slot);
        public void StopAll() => _engine.StopAll();
        public void Reset() => _engine.Reset();
        public void SetMasterGain(float gain) => _engine.SetMasterGain(gain);

        // Pause/Resume gate the pump thread; the engine keeps state. Mirrors how
        // BlazorAudioDriver freezes the platform output device without dropping voices.
        public void Pause()  => _paused = true;
        public void Resume() => _paused = false;

        // ── Player process + pump thread ─────────────────────────────────────────

        private void StartPlayer()
        {
            var psi = new ProcessStartInfo
            {
                FileName = _playerPath!,
                UseShellExecute = false,
                RedirectStandardInput  = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow = true,
            };
            foreach (var a in _playerArgs) psi.ArgumentList.Add(a);
            _player = Process.Start(psi)
                ?? throw new InvalidOperationException($"Process.Start returned null for {_playerPath}.");
            _playerStdin = _player.StandardInput.BaseStream;
        }

        private void StartPump()
        {
            _cts = new CancellationTokenSource();
            _pumpThread = new Thread(PumpLoop)
            {
                IsBackground = true,
                Name = "WebHostAudioPump",
                Priority = ThreadPriority.AboveNormal,
            };
            _pumpThread.Start();
        }

        private void PumpLoop()
        {
            var floats = new float[FloatsPerBuffer];
            var bytes  = new byte[BytesPerBuffer];
            var token = _cts!.Token;

            // Wall-clock pacer for browser mode. In local-sink mode the OS pipe
            // to pw-cat/pacat/aplay back-pressures naturally; here we have to
            // pace ourselves so we don't generate ~44 kHz of frames into a
            // channel the browser drains at audio rate.
            long pumpStartTicks = Environment.TickCount64;
            long framesSent = 0;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (_paused)
                    {
                        Thread.Sleep(20);
                        // Reset the pacing reference so the wall-clock catch-up
                        // logic doesn't burst-emit the lost interval on resume.
                        pumpStartTicks = Environment.TickCount64;
                        framesSent = 0;
                        continue;
                    }

                    // Skip work entirely when nobody is listening on the browser
                    // side. The engine still ticks via ProcessEvents so voice
                    // state advances and PointReached callbacks fire; we just
                    // don't generate samples nobody will hear.
                    if (_browserMode && !_browserSink.HasSubscribers)
                    {
                        _engine.ProcessEvents();
                        Thread.Sleep(20);
                        pumpStartTicks = Environment.TickCount64;
                        framesSent = 0;
                        continue;
                    }

                    int produced = _engine.Read(floats, 0, FloatsPerBuffer);
                    _engine.ProcessEvents();

                    if (produced <= 0)
                    {
                        // Engine always fills the buffer in normal operation, but
                        // guard so a malformed return doesn't write garbage bytes.
                        continue;
                    }

                    int byteLen = produced * sizeof(float);
                    Buffer.BlockCopy(floats, 0, bytes, 0, byteLen);

                    if (_browserMode)
                    {
                        // Skip publishing pure-silence buffers. Streaming a
                        // continuous 44.1 kHz stream even between sounds means a
                        // navigation tone only plays once the browser drains the
                        // backlog of buffered silence ahead of it — the dominant
                        // source of perceived nav-audio lag on the web host/demo.
                        // Dropping silent buffers lets the browser schedule run
                        // dry, so the next real buffer starts at (now + ~20 ms)
                        // via the audio.js underrun resync instead of behind the
                        // backlog.
                        bool silent = true;
                        for (int i = 0; i < produced; i++)
                        {
                            if (MathF.Abs(floats[i]) > 1e-4f) { silent = false; break; }
                        }

                        if (!silent)
                        {
                            // Allocate a per-chunk copy — Subject<byte[]> delivers
                            // the reference downstream; reusing `bytes` would race
                            // with the JS-interop serializer.
                            var chunk = new byte[byteLen];
                            Buffer.BlockCopy(bytes, 0, chunk, 0, byteLen);
                            _browserSink.Publish(chunk);
                        }

                        framesSent += produced / ChannelCount;
                        long dueMs = framesSent * 1000L / RateHz;
                        long elapsedMs = Environment.TickCount64 - pumpStartTicks;
                        int sleepMs = (int)(dueMs - elapsedMs);
                        if (sleepMs > 1) Thread.Sleep(sleepMs);
                    }
                    else
                    {
                        if (_playerStdin is null) return;
                        _playerStdin.Write(bytes, 0, byteLen);
                        // No explicit Flush — pw-cat / pacat read continuously and the
                        // OS pipe buffer naturally back-pressures the producer at audio rate.
                    }
                }
            }
            catch (IOException ex) when (ex.InnerException is System.Net.Sockets.SocketException
                                         || ex.Message.Contains("pipe", StringComparison.OrdinalIgnoreCase))
            {
                // Broken pipe: the player child (pw-cat/pacat/aplay) is gone. On
                // Ctrl+C the child receives SIGINT with us and dies before our
                // cancellation token trips, so this is the NORMAL shutdown path —
                // don't scare the operator with a stack trace. If it ever happens
                // mid-session the message still says what to do.
                if (token.IsCancellationRequested || _disposed || _hostStopping.IsCancellationRequested)
                    _logger.LogDebug("Audio pump stopped: output pipe closed during shutdown.");
                else
                    _logger.LogWarning(
                        "Audio output player exited (pipe closed); sonification is now silent. " +
                        "Restart the app to recover. ({Message})", ex.Message);
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                _logger.LogWarning(ex,
                    "Audio pump exited unexpectedly; sonification is now silent. " +
                    "Restart the app to recover.");
            }
        }

        // ── Backend probe ────────────────────────────────────────────────────────

        // Pure picker — the `fileExists` predicate makes this trivially
        // testable without touching the real filesystem. Production passes
        // `File.Exists`; unit tests pass a closure that returns true for
        // whichever player path they want to simulate.
        internal static (string? path, string[] args) PickPlayer(Func<string, bool> fileExists)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return (null, Array.Empty<string>());

            // pw-cat (PipeWire native) — preferred. Request a small node latency
            // so PipeWire doesn't pick a large default buffer; otherwise the
            // local-sink path lags navigation tones. ~30 ms is a safe balance
            // against xruns given the pump's 23 ms (1024-frame) buffers.
            string? p = FindOnPath("pw-cat", fileExists);
            if (p is not null)
                return (p, new[]
                {
                    "--playback",
                    "--rate", RateHz.ToString(),
                    "--channels", ChannelCount.ToString(),
                    "--format", "f32",
                    "--latency", "30ms",
                    "--raw",
                    "-",
                });

            // pacat (PulseAudio). --latency-msec keeps the server-side buffer
            // small for the same low-latency reason as pw-cat above.
            p = FindOnPath("pacat", fileExists);
            if (p is not null)
                return (p, new[]
                {
                    "--playback",
                    $"--rate={RateHz}",
                    $"--channels={ChannelCount}",
                    "--format=float32le",
                    "--latency-msec=30",
                    "--raw",
                });

            // aplay (ALSA) — last resort.
            p = FindOnPath("aplay", fileExists);
            if (p is not null)
                return (p, new[]
                {
                    "-t", "raw",
                    "-f", "FLOAT_LE",
                    "-c", ChannelCount.ToString(),
                    "-r", RateHz.ToString(),
                });

            return (null, Array.Empty<string>());
        }

        internal static string? FindOnPath(string exe, Func<string, bool> fileExists)
        {
            foreach (var dir in new[] { "/usr/bin", "/usr/local/bin", "/bin" })
            {
                var p = Path.Combine(dir, exe);
                if (fileExists(p)) return p;
            }
            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _cts?.Cancel(); } catch { }
            try { _pumpThread?.Join(500); } catch { }
            DisposePlayer();
            _cts?.Dispose();
        }

        private void DisposePlayer()
        {
            try { _playerStdin?.Dispose(); } catch { }
            try
            {
                if (_player is { HasExited: false })
                {
                    _player.Kill();
                    _player.WaitForExit(500);
                }
            }
            catch { }
            try { _player?.Dispose(); } catch { }
            _playerStdin = null;
            _player = null;
        }
    }
}
