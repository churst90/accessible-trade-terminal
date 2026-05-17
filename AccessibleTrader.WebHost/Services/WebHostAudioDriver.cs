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
        private readonly string? _playerPath;
        private readonly string[] _playerArgs = Array.Empty<string>();

        private Process? _player;
        private Stream? _playerStdin;
        private Thread? _pumpThread;
        private CancellationTokenSource? _cts;
        private volatile bool _paused;
        private bool _disposed;

        public event Action<int>? PointReached;
        public int SampleRate => _engine.SampleRate;
        public int Channels   => _engine.Channels;

        public long DroppedCommandCount => _engine.DroppedCommandCount;
        public long TotalCommandCount   => _engine.TotalCommandCount;
        public void ResetAudioTelemetry() => _engine.ResetTelemetry();

        public WebHostAudioDriver(ILogger<WebHostAudioDriver> logger)
        {
            _logger = logger;

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
                _logger.LogInformation(
                    "Audio: no local PCM sink found (pw-cat / pacat / aplay). Sonification is silent until a server-side audio backend or browser WebAudio fallback is added.");
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

        public void SetVoice(int slot, double frequency, float volume, float pan, string waveform, bool continuous, double durationSeconds = 0.2, int dataIndex = -1, string envelope = "Sustain", bool click = false, float noiseAmount = 0f, string noiseType = "pink")
            => _engine.SetVoice(slot, frequency, volume, pan, waveform, continuous, durationSeconds, dataIndex, envelope, click, noiseAmount, noiseType);

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

            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (_paused)
                    {
                        Thread.Sleep(20);
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

                    if (_playerStdin is null) return;
                    _playerStdin.Write(bytes, 0, byteLen);
                    // No explicit Flush — pw-cat / pacat read continuously and the
                    // OS pipe buffer naturally back-pressures the producer at audio rate.
                }
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

            // pw-cat (PipeWire native) — preferred.
            string? p = FindOnPath("pw-cat", fileExists);
            if (p is not null)
                return (p, new[]
                {
                    "--playback",
                    "--rate", RateHz.ToString(),
                    "--channels", ChannelCount.ToString(),
                    "--format", "f32",
                    "--raw",
                    "-",
                });

            // pacat (PulseAudio).
            p = FindOnPath("pacat", fileExists);
            if (p is not null)
                return (p, new[]
                {
                    "--playback",
                    $"--rate={RateHz}",
                    $"--channels={ChannelCount}",
                    "--format=float32le",
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
