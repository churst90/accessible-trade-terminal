using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Audio
{
    public interface IPlaybackOrchestrator
    {
        bool IsPlaying { get; }
        event Action? PlaybackFinished;
        event Action<int>? PlaybackPointReached;
        void StartPlayback(WorkspaceState state);
        void Stop();
    }

    public class PlaybackOrchestrator : IPlaybackOrchestrator, IDisposable
    {
        private readonly IAudioSequencer _sequencer;
        private readonly IAudioDriver _audioDriver;
        private readonly ILogger<PlaybackOrchestrator> _logger;
        private readonly System.Reactive.Disposables.CompositeDisposable _subs = new();
        private CancellationTokenSource? _playbackCts;

        public bool IsPlaying => _sequencer.IsPlaying;
        public event Action? PlaybackFinished;
        public event Action<int>? PlaybackPointReached;

        public PlaybackOrchestrator(
            IAudioSequencer sequencer,
            IAudioDriver audioDriver,
            ILogger<PlaybackOrchestrator> logger)
        {
            _sequencer = sequencer;
            _audioDriver = audioDriver;
            _logger = logger;

            _subs.Add(_sequencer.PlaybackFinished.Subscribe(_ => PlaybackFinished?.Invoke()));
            _subs.Add(_sequencer.PointReached.Subscribe(idx => PlaybackPointReached?.Invoke(idx)));
        }

        /// <summary>
        /// Starts the sequencer on exactly the <see cref="PlaybackPlan"/> the dispatcher admitted
        /// and the coordinator announced. The selection rule lives in
        /// <see cref="PlaybackPlan.Resolve"/>, not here — see that type for why.
        ///
        /// <para>
        /// Speech is NOT this class's job and never was, whatever an older comment in the
        /// coordinator said: this class has no speech router and no event bus. Everything the
        /// user hears in words about playback — start, pause, resume, stop, finished, speed, the
        /// landmark dates while it runs — comes from
        /// <c>AccessibilityFeedbackCoordinator</c> observing the store.
        /// </para>
        /// </summary>
        public void StartPlayback(WorkspaceState state)
        {
            var plan = PlaybackPlan.Resolve(state, state.PlaybackScope);
            if (!plan.IsPlayable || state.Data == null) return;

            Stop();
            _playbackCts = new CancellationTokenSource();

            if (state.PlaybackScope == PlaybackScope.Chart)
            {
                SafeFireAndForget.Run(
                    () => _sequencer.StartMultiSeriesPlaybackAsync(plan.Series, state.Data.ToList(), plan.StartIndex, _playbackCts.Token),
                    _logger, "MultiSeriesPlayback");
            }
            else
            {
                SafeFireAndForget.Run(
                    () => _sequencer.StartPlaybackAsync(plan.Series[0], state.Data.ToList(), plan.StartIndex, _playbackCts.Token, plan.ComponentFilter),
                    _logger, "SeriesPlayback");
            }
        }

        public void Stop()
        {
            _playbackCts?.Cancel();
            _playbackCts = null;
            _sequencer.Stop();
            for (int i = 0; i < AudioEngine.MaxVoices; i++) _audioDriver.StopVoice(i);
        }

        public void Dispose() => _subs.Dispose();
    }
}
