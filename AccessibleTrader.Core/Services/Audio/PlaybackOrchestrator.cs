using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
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

        public void StartPlayback(WorkspaceState state)
        {
            if (state.Data == null || state.Data.Count == 0) return;

            Stop();
            _playbackCts = new CancellationTokenSource();

            if (state.PlaybackScope == PlaybackScope.Chart)
            {
                // Chart scope: every visible, unmuted, non-drawing, non-profile series plays
                // simultaneously. All series' components are heard layered on top of each other,
                // bar by bar. Muted series are excluded so the user's mute actually silences them.
                var playList = state.ActiveSeries
                    .Where(s => s.IsVisible && !s.IsMuted && !s.IsDrawing && !s.IsProfile)
                    .ToList();

                if (playList.Count == 0) return;

                int start = Math.Clamp(state.ViewportStartIndex, 0, state.Data.Count - 1);
                SafeFireAndForget.Run(
                    () => _sequencer.StartMultiSeriesPlaybackAsync(playList, state.Data.ToList(), start, _playbackCts.Token),
                    _logger, "MultiSeriesPlayback");
            }
            else
            {
                // Series scope: all components of the focused series play simultaneously.
                // Component scope: only the focused component of the focused series plays.
                string seriesId = state.FocusedSeriesId ?? state.PrimarySeriesId;
                var series = state.ActiveSeries.FirstOrDefault(s => s.Id == seriesId)
                          ?? state.ActiveSeries.FirstOrDefault();
                if (series == null) return;

                // Series/Component: start from cursor so the user hears from the current position forward.
                int start = Math.Clamp(Math.Max(0, state.CurrentDataIndex), 0, state.Data.Count - 1);

                // -1 is already this call's "no component filter" value, which is the right
                // answer for a series with nothing to filter to.
                int componentFilter = state.PlaybackScope == PlaybackScope.Component
                    ? series.ClampComponent(state.FocusedComponentIndex)
                    : -1;

                SafeFireAndForget.Run(
                    () => _sequencer.StartPlaybackAsync(series, state.Data.ToList(), start, _playbackCts.Token, componentFilter),
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
