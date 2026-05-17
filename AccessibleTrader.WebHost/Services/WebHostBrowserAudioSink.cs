using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// Fan-out sink for float32 stereo PCM chunks bound for the browser when
    /// the server has no local audio device (Windows / macOS WebHost deploys
    /// and headless servers, where the L3 process-pipe path to
    /// pw-cat / pacat / aplay finds nothing).
    ///
    /// The audio pump in <see cref="WebHostAudioDriver"/> calls
    /// <see cref="Publish"/> once per buffer; <see cref="Chunks"/> is a hot
    /// observable that the per-circuit <c>BrowserAudioBridge</c> component
    /// subscribes to and forwards via JS interop to
    /// <c>accessibleTrader.audioPush</c>.
    ///
    /// Singleton — one instance shared by the driver and every connected
    /// circuit. Late subscribers join mid-stream; missed chunks are simply
    /// missed (PCM is time-sensitive and replay would desync the schedule
    /// the browser builds on top of <c>AudioContext.currentTime</c>).
    /// </summary>
    public sealed class WebHostBrowserAudioSink : IDisposable
    {
        private readonly Subject<byte[]> _chunks = new();

        /// <summary>
        /// Audio chunks as raw little-endian float32 interleaved stereo bytes
        /// at <see cref="WebHostAudioDriver"/>'s sample rate (44.1 kHz).
        /// </summary>
        public IObservable<byte[]> Chunks => _chunks;

        /// <summary>True when at least one circuit is subscribed. Lets the
        /// driver short-circuit the engine read + per-chunk array allocation
        /// when no browser is listening, so the server doesn't spin
        /// generating silence into the void.</summary>
        public bool HasSubscribers => _chunks.HasObservers;

        public void Publish(byte[] chunk) => _chunks.OnNext(chunk);

        public void Dispose()
        {
            _chunks.OnCompleted();
            _chunks.Dispose();
        }
    }
}
