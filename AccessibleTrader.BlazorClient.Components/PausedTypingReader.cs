using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;

namespace AccessibleTrader.BlazorClient.Components
{
    /// <summary>
    /// Says a search box's result ("12 matches") once the user pauses typing, politely, on the
    /// interface channel. A count that only lives in an aria-describedby target is read when
    /// focus ARRIVES on the box, never while typing in it, and a role=status that is created
    /// already filled is often not read at all (accessibility review 2026-11-27: the toolbar's
    /// symbol find and the Settings search). Each keystroke restarts the wait, so only the
    /// count for what the user stopped on is said. The same shape as the Go bar's polite read.
    /// </summary>
    internal sealed class PausedTypingReader : IDisposable
    {
        public static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(500);

        private readonly IEventBus _bus;
        private readonly TimeProvider _clock;
        private readonly Func<Action, Task> _invoke;
        private CancellationTokenSource? _cts;

        /// <param name="invoke">The component's InvokeAsync, so the sentence is built and
        /// published on the renderer's thread.</param>
        public PausedTypingReader(IEventBus bus, TimeProvider clock, Func<Action, Task> invoke)
        {
            _bus = bus;
            _clock = clock;
            _invoke = invoke;
        }

        /// <summary>Starts the wait again; when it ends, says what <paramref name="sentence"/>
        /// returns then (nothing when it returns null or empty).</summary>
        public void Restart(Func<string?> sentence)
        {
            Cancel();
            var cts = new CancellationTokenSource();
            _cts = cts;
            _ = SayAfterPauseAsync(sentence, cts.Token);
        }

        private async Task SayAfterPauseAsync(Func<string?> sentence, CancellationToken token)
        {
            try
            {
                await Task.Delay(Pause, _clock, token);
                await _invoke(() =>
                {
                    if (token.IsCancellationRequested) return;
                    string? text = sentence();
                    if (string.IsNullOrEmpty(text)) return;
                    _bus.Publish(new AnnouncementEvent(text, Interrupt: false, Channel: SpeechChannel.Interface));
                });
            }
            catch (OperationCanceledException)
            {
                // A new keystroke, a close or a dispose replaced this read.
            }
            catch (ObjectDisposedException)
            {
                // The component went away during the wait.
            }
        }

        public void Cancel()
        {
            if (_cts == null) return;
            try { _cts.Cancel(); } catch (ObjectDisposedException) { }
            _cts.Dispose();
            _cts = null;
        }

        public void Dispose() => Cancel();
    }
}
