using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using Microsoft.Extensions.Logging;

#if ANDROID
using Android.Media;
#elif IOS || MACCATALYST
using AVFoundation;
using AudioToolbox;
#endif

namespace AccessibleTrader.BlazorClient.Services
{
    /// <summary>
    /// Multi-platform audio driver.
    /// Windows:          winmm waveOut (Float32) via P/Invoke — no external package.
    /// Android:          AudioTrack PCM-Float push loop on a dedicated background thread.
    /// iOS / macCatalyst: AVAudioEngine + AVAudioSourceNode render callback (push model).
    ///
    /// MUST live in the MAUI host project, NOT the platform-agnostic
    /// AccessibleTrader.BlazorClient.Components RCL. The Components RCL targets
    /// plain net10.0, so the WINDOWS / ANDROID / IOS / MACCATALYST conditional
    /// symbols are never defined and every platform branch becomes dead code.
    /// Building the MAUI host then resolves a driver with no `EnsureAudioInit`
    /// body — no audio device opens, the entire audio engine is silent.
    /// (See 2026-04-27 evening 18 — audio-driver-relocation fix in CHANGES.md.)
    /// </summary>
    public class BlazorAudioDriver : IAudioDriver, IDisposable
    {
        private readonly AudioEngine _engine;
        private readonly ILogger<BlazorAudioDriver> _logger;
        private bool _audioInitialized;
        private bool _disposed;

#if WINDOWS
        // Three-buffer round-robin. waveOutWrite queues a buffer; WOM_DONE callback
        // signals a worker that refills it and queues it back. Three buffers is the
        // conventional minimum that keeps the device fed without adding audible
        // latency.
        //
        // The refill MUST NOT happen inside the winmm callback. Win32 docs:
        // "Calling other wave functions [from this callback] will cause deadlock."
        // We post the returning header pointer to a ThreadPool work item so the
        // unprepare/refill/write sequence runs outside winmm's callback context.
        private const int WinBufferFrames = 1024;
        private const int WinNumBuffers = 3;
        private IntPtr _hWaveOut = IntPtr.Zero;
        private readonly WaveOutProc _callback;
        private WaveHdrState[] _buffers = Array.Empty<WaveHdrState>();
        private readonly object _refillLock = new();

        private sealed class WaveHdrState
        {
            public WAVEHDR Header;
            public GCHandle HeaderPin;
            public GCHandle DataPin;
            public float[] Data = Array.Empty<float>();
        }

#elif ANDROID
        private AudioTrack? _audioTrack;
        private CancellationTokenSource? _audioCts;
        private const int AndroidBufferFrames = 1024;

#elif IOS || MACCATALYST
        private AVAudioEngine? _avEngine;
        private AVAudioSourceNode? _sourceNode;
#endif

        public event Action<int>? PointReached;
        public int SampleRate => _engine.SampleRate;
        public int Channels   => _engine.Channels;

        public BlazorAudioDriver(ILogger<BlazorAudioDriver> logger, AccessibleTrader.Sdk.Services.ISecurityEventLog? securityEvents = null)
        {
            _logger = logger;
            _engine = new AudioEngine();
            _engine.PointReached += index => PointReached?.Invoke(index);

            _engine.CommandDropped += droppedTotal =>
            {
                if (droppedTotal % 10 != 0) return;
                _logger.LogWarning("AudioEngine: {DroppedTotal} command(s) dropped due to buffer overflow. Consider reducing sonification density.", droppedTotal);
                securityEvents?.Record(new AccessibleTrader.Sdk.Services.SecurityEvent(
                    UtcTimestamp: DateTime.UtcNow,
                    Kind: AccessibleTrader.Sdk.Services.SecurityEventKind.AudioCommandDropped,
                    Source: "AudioEngine",
                    Message: $"{droppedTotal} voice command(s) dropped; ring buffer full."));
            };

#if WINDOWS
            _callback = WaveOutCallback;
#endif
        }

        public long DroppedCommandCount => _engine.DroppedCommandCount;
        public long TotalCommandCount => _engine.TotalCommandCount;
        public void ResetAudioTelemetry() => _engine.ResetTelemetry();

        private void EnsureAudioInit()
        {
            if (_audioInitialized) return;
            _audioInitialized = true;

#if WINDOWS
            try
            {
                var fmt = new WAVEFORMATEX
                {
                    wFormatTag = WAVE_FORMAT_IEEE_FLOAT,
                    nChannels = (ushort)_engine.Channels,
                    nSamplesPerSec = (uint)_engine.SampleRate,
                    wBitsPerSample = 32,
                    cbSize = 0,
                };
                fmt.nBlockAlign = (ushort)(fmt.nChannels * (fmt.wBitsPerSample / 8));
                fmt.nAvgBytesPerSec = fmt.nSamplesPerSec * fmt.nBlockAlign;

                int mmr = waveOutOpen(out _hWaveOut, WAVE_MAPPER, ref fmt, _callback, UIntPtr.Zero, CALLBACK_FUNCTION);
                if (mmr != MMSYSERR_NOERROR)
                {
                    _logger.LogError("waveOutOpen failed: MMRESULT={MMR}", mmr);
                    _hWaveOut = IntPtr.Zero;
                    return;
                }

                _buffers = new WaveHdrState[WinNumBuffers];
                int samplesPerBuffer = WinBufferFrames * _engine.Channels;
                for (int i = 0; i < WinNumBuffers; i++)
                {
                    var s = new WaveHdrState { Data = new float[samplesPerBuffer] };
                    // First fill: read from the engine so the first play has real audio.
                    int got = _engine.Read(s.Data, 0, samplesPerBuffer);
                    if (got < samplesPerBuffer)
                        Array.Clear(s.Data, got, samplesPerBuffer - got);
                    _engine.ProcessEvents();
                    PrepareAndWriteBuffer(s);
                    _buffers[i] = s;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audio init failed (Windows).");
                _hWaveOut = IntPtr.Zero;
            }

#elif ANDROID
            try
            {
                int bufferBytes = AndroidBufferFrames * Channels * sizeof(float);
                int minBuf = AudioTrack.GetMinBufferSize(_engine.SampleRate,
                    ChannelOut.Stereo, Encoding.PcmFloat);
                bufferBytes = Math.Max(bufferBytes, minBuf);

                _audioTrack = new AudioTrack.Builder()
                    .SetAudioAttributes(new AudioAttributes.Builder()
                        .SetUsage(AudioUsageKind.Media)!
                        .SetContentType(AudioContentType.Music)!
                        .Build()!)!
                    .SetAudioFormat(new AudioFormat.Builder()
                        .SetEncoding(Encoding.PcmFloat)!
                        .SetSampleRate(_engine.SampleRate)!
                        .SetChannelMask(ChannelOut.Stereo)!
                        .Build()!)!
                    .SetBufferSizeInBytes(bufferBytes)!
                    .SetTransferMode(AudioTrackMode.Stream)!
                    .Build();

                _audioTrack.Play();
                _audioCts = new CancellationTokenSource();
                var token = _audioCts.Token;
                var buf   = new float[AndroidBufferFrames * Channels];
                Task.Factory.StartNew(() =>
                {
                    while (!token.IsCancellationRequested && _audioTrack.PlayState == PlayState.Playing)
                    {
                        int read = _engine.Read(buf, 0, buf.Length);
                        _engine.ProcessEvents();
                        _audioTrack.Write(buf, 0, read, WriteMode.NonBlocking);
                    }
                }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audio init failed (Android).");
                _audioTrack = null;
            }

#elif IOS || MACCATALYST
            try
            {
                _avEngine = new AVAudioEngine();
                var format = new AVAudioFormat((double)_engine.SampleRate, (uint)Channels);

                _sourceNode = new AVAudioSourceNode(format, (ref bool isSilence,
                    ref AudioTimeStamp timestamp, uint frameCount,
                    AudioBuffers outputData) =>
                {
                    int sampleCount = (int)(frameCount * Channels);
                    var buf = new float[sampleCount];
                    _engine.Read(buf, 0, sampleCount);
                    _engine.ProcessEvents();
                    isSilence = false;

                    var channelBuf = new float[(int)frameCount];
                    for (int ch = 0; ch < outputData.Count && ch < Channels; ch++)
                    {
                        for (int i = 0; i < (int)frameCount; i++)
                            channelBuf[i] = buf[i * Channels + ch];
                        Marshal.Copy(channelBuf, 0, outputData[ch].Data, (int)frameCount);
                    }
                    return 0;
                });

                _avEngine.AttachNode(_sourceNode);
                _avEngine.Connect(_sourceNode, _avEngine.MainMixerNode, format);
                _avEngine.StartAndReturnError(out var error);
                if (error != null)
                    _logger.LogError("Audio init failed (iOS/Mac): {Error}.", error.LocalizedDescription);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audio init failed (iOS/Mac).");
                _avEngine = null;
            }
#endif
        }

#if WINDOWS
        private void PrepareAndWriteBuffer(WaveHdrState s)
        {
            if (_hWaveOut == IntPtr.Zero) return;
            s.DataPin = GCHandle.Alloc(s.Data, GCHandleType.Pinned);
            s.Header = new WAVEHDR
            {
                lpData = s.DataPin.AddrOfPinnedObject(),
                dwBufferLength = (uint)(s.Data.Length * sizeof(float)),
                dwBytesRecorded = 0,
                dwUser = UIntPtr.Zero,
                dwFlags = 0,
                dwLoops = 0,
                lpNext = IntPtr.Zero,
                reserved = UIntPtr.Zero,
            };
            s.HeaderPin = GCHandle.Alloc(s.Header, GCHandleType.Pinned);
            waveOutPrepareHeader(_hWaveOut, s.HeaderPin.AddrOfPinnedObject(), (uint)Marshal.SizeOf<WAVEHDR>());
            waveOutWrite(_hWaveOut, s.HeaderPin.AddrOfPinnedObject(), (uint)Marshal.SizeOf<WAVEHDR>());
        }

        private void WaveOutCallback(IntPtr hWaveOut, uint uMsg, UIntPtr dwInstance, UIntPtr dwParam1, UIntPtr dwParam2)
        {
            if (uMsg != WOM_DONE || _disposed || _hWaveOut == IntPtr.Zero) return;
            // PER WIN32 DOCS: refilling MUST NOT happen inside this callback —
            // calling waveOutUnprepareHeader / waveOutPrepareHeader / waveOutWrite
            // from the callback context can deadlock the winmm subsystem on some
            // drivers. Hand the returning header pointer off to a ThreadPool worker
            // which performs the wave calls without restriction.
            //
            // UnsafeQueueUserWorkItem skips ExecutionContext capture (we don't need
            // it for an audio refill) and dispatches faster than the safe variant.
            IntPtr hdrPtr = (IntPtr)(long)dwParam1;
            ThreadPool.UnsafeQueueUserWorkItem(static state =>
            {
                var (driver, ptr) = ((BlazorAudioDriver, IntPtr))state!;
                driver.RefillBufferOnWorker(ptr);
            }, (this, hdrPtr));
        }

        // Runs on a ThreadPool worker thread, NOT inside the winmm WOM_DONE callback.
        // Locks across multiple in-flight refills so that two simultaneously-returning
        // buffers (rare with 3-buffer round-robin but possible if the audio service
        // batches notifications) can't race on the shared _engine.Read / pin state.
        private void RefillBufferOnWorker(IntPtr hdrPtr)
        {
            if (_disposed || _hWaveOut == IntPtr.Zero) return;
            lock (_refillLock)
            {
                if (_disposed || _hWaveOut == IntPtr.Zero) return;
                for (int i = 0; i < _buffers.Length; i++)
                {
                    var s = _buffers[i];
                    if (!s.HeaderPin.IsAllocated || s.HeaderPin.AddrOfPinnedObject() != hdrPtr) continue;
                    try
                    {
                        waveOutUnprepareHeader(_hWaveOut, hdrPtr, (uint)Marshal.SizeOf<WAVEHDR>());
                        s.HeaderPin.Free();
                        s.DataPin.Free();

                        int got = _engine.Read(s.Data, 0, s.Data.Length);
                        if (got < s.Data.Length)
                            Array.Clear(s.Data, got, s.Data.Length - got);
                        _engine.ProcessEvents();
                        PrepareAndWriteBuffer(s);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Audio buffer refill failed.");
                    }
                    return;
                }
            }
        }
#endif

        public void SetVoice(int slot, double frequency, float volume, float pan,
            string waveform, bool continuous, double durationSeconds = 0.2,
            int dataIndex = -1, string envelope = "Sustain", bool click = false, float noiseAmount = 0f, string noiseType = "pink")
        {
            EnsureAudioInit();
            _engine.SetVoice(slot, frequency, volume, pan, waveform, continuous,
                durationSeconds, dataIndex, envelope, click, noiseAmount, noiseType);
        }

        public void StopVoice(int slot) => _engine.StopVoice(slot);
        public void StopAll()           => _engine.StopAll();
        public void Reset()             => _engine.Reset();
        public void SetMasterGain(float gain) => _engine.SetMasterGain(gain);

        public void Pause()
        {
#if WINDOWS
            if (_hWaveOut != IntPtr.Zero) waveOutPause(_hWaveOut);
#elif ANDROID
            _audioTrack?.Pause();
#elif IOS || MACCATALYST
            _avEngine?.Pause();
#endif
        }

        public void Resume()
        {
#if WINDOWS
            if (_hWaveOut != IntPtr.Zero) waveOutRestart(_hWaveOut);
#elif ANDROID
            _audioTrack?.Play();
#elif IOS || MACCATALYST
            _avEngine?.StartAndReturnError(out _);
#endif
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
#if WINDOWS
            if (_hWaveOut != IntPtr.Zero)
            {
                // waveOutReset returns all queued buffers as WOM_DONE; each fires a
                // ThreadPool refill work item. Acquire _refillLock to drain any
                // in-flight refill before we free the pins out from under it.
                waveOutReset(_hWaveOut);
                lock (_refillLock)
                {
                    foreach (var s in _buffers)
                    {
                        try
                        {
                            if (s.HeaderPin.IsAllocated)
                            {
                                waveOutUnprepareHeader(_hWaveOut, s.HeaderPin.AddrOfPinnedObject(), (uint)Marshal.SizeOf<WAVEHDR>());
                                s.HeaderPin.Free();
                            }
                            if (s.DataPin.IsAllocated) s.DataPin.Free();
                        }
                        catch { }
                    }
                    waveOutClose(_hWaveOut);
                    _hWaveOut = IntPtr.Zero;
                }
            }
#elif ANDROID
            _audioCts?.Cancel();
            _audioTrack?.Stop();
            _audioTrack?.Release();
            _audioTrack?.Dispose();
#elif IOS || MACCATALYST
            _avEngine?.Stop();
            _avEngine?.Dispose();
            _sourceNode?.Dispose();
#endif
        }

#if WINDOWS
        // ── winmm.dll P/Invoke ──────────────────────────────────────────────────
        private const int WAVE_MAPPER = -1;
        private const int MMSYSERR_NOERROR = 0;
        private const int CALLBACK_FUNCTION = 0x00030000;
        private const ushort WAVE_FORMAT_IEEE_FLOAT = 0x0003;
        private const uint WOM_DONE = 0x3BD;

        private delegate void WaveOutProc(IntPtr hWaveOut, uint uMsg, UIntPtr dwInstance, UIntPtr dwParam1, UIntPtr dwParam2);

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public UIntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public UIntPtr reserved;
        }

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern int waveOutOpen(out IntPtr hWaveOut, int uDeviceID, ref WAVEFORMATEX lpFormat,
            WaveOutProc dwCallback, UIntPtr dwInstance, int dwFlags);

        [DllImport("winmm.dll")]
        private static extern int waveOutPrepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, uint uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutUnprepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, uint uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutWrite(IntPtr hWaveOut, IntPtr lpWaveOutHdr, uint uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutReset(IntPtr hWaveOut);

        [DllImport("winmm.dll")]
        private static extern int waveOutPause(IntPtr hWaveOut);

        [DllImport("winmm.dll")]
        private static extern int waveOutRestart(IntPtr hWaveOut);

        [DllImport("winmm.dll")]
        private static extern int waveOutClose(IntPtr hWaveOut);
#endif
    }
}
