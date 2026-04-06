using System;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;

#if WINDOWS
using NAudio.Wave;
#elif ANDROID
using Android.Media;
#elif IOS || MACCATALYST
using System.Runtime.InteropServices;
using AVFoundation;
using AudioToolbox;
#endif

namespace AccessibleTrader.BlazorClient.Services
{
    /// <summary>
    /// Multi-platform audio driver.
    /// Windows:          NAudio WASAPI output (pull model via ISampleProvider).
    /// Android:          AudioTrack PCM-Float push loop on a dedicated background thread.
    /// iOS / macCatalyst: AVAudioEngine + AVAudioSourceNode render callback (push model).
    /// </summary>
    public class BlazorAudioDriver : IAudioDriver,
#if WINDOWS
        ISampleProvider,
#endif
        IDisposable
    {
        private readonly AudioEngine _engine;
        private bool _audioInitialized;
        private bool _disposed;

#if WINDOWS
        private WasapiOut? _wasapiOut;
        private readonly WaveFormat _format;
        public WaveFormat WaveFormat => _format;

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

        public BlazorAudioDriver()
        {
            _engine = new AudioEngine();
            _engine.PointReached += index => PointReached?.Invoke(index);
#if WINDOWS
            _format = WaveFormat.CreateIeeeFloatWaveFormat(_engine.SampleRate, 2);
#endif
        }

        private void EnsureAudioInit()
        {
            if (_audioInitialized) return;
            _audioInitialized = true;

#if WINDOWS
            try
            {
                _wasapiOut = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 50);
                _wasapiOut.Init(this);
                _wasapiOut.Play();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"AUDIO INIT ERROR (Windows): {ex.Message}");
                _wasapiOut = null;
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
                Console.Error.WriteLine($"AUDIO INIT ERROR (Android): {ex.Message}");
                _audioTrack = null;
            }

#elif IOS || MACCATALYST
            try
            {
                _avEngine = new AVAudioEngine();
                // Use (sampleRate, channels) constructor — creates standard non-interleaved Float32 format.
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

                    // De-interleave: copy per-channel samples to each AVAudioBuffer channel.
                    var channelBuf = new float[(int)frameCount];
                    for (int ch = 0; ch < outputData.Count && ch < Channels; ch++)
                    {
                        for (int i = 0; i < (int)frameCount; i++)
                            channelBuf[i] = buf[i * Channels + ch];
                        Marshal.Copy(channelBuf, 0, outputData[ch].Data, (int)frameCount);
                    }
                    return 0; // noErr
                });

                _avEngine.AttachNode(_sourceNode);
                _avEngine.Connect(_sourceNode, _avEngine.MainMixerNode, format);
                _avEngine.StartAndReturnError(out var error);
                if (error != null)
                    Console.Error.WriteLine($"AUDIO INIT ERROR (iOS/Mac): {error.LocalizedDescription}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"AUDIO INIT ERROR (iOS/Mac): {ex.Message}");
                _avEngine = null;
            }
#endif
        }

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
            _wasapiOut?.Pause();
#elif ANDROID
            _audioTrack?.Pause();
#elif IOS || MACCATALYST
            _avEngine?.Pause();
#endif
        }

        public void Resume()
        {
#if WINDOWS
            _wasapiOut?.Play();
#elif ANDROID
            _audioTrack?.Play();
#elif IOS || MACCATALYST
            _avEngine?.StartAndReturnError(out _);
#endif
        }

        // Windows ISampleProvider pull callback
        public int Read(float[] buffer, int offset, int count)
        {
            int read = _engine.Read(buffer, offset, count);
            _engine.ProcessEvents();
            return read;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
#if WINDOWS
            _wasapiOut?.Stop();
            _wasapiOut?.Dispose();
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
    }
}

