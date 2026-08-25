using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;
using System.Reactive.Linq;

namespace AccessibleTrader.Core.Services
{
    public class SonificationManager : ISonificationManager, IDisposable
    {
        private readonly IPlaybackOrchestrator _playback;
        private readonly INavigationSonifier _navigation;
        private readonly IWorkspaceStore _store;
        private readonly IMainThreadService _mainThreadService;
        private readonly IEventBus _eventBus;
        private readonly ILevelCrossingMonitor? _levelCrossing;
        private readonly List<IDisposable> _subscriptions = new();
        private readonly SonificationStateMachine _stateMachine;

        private WorkspaceState _currentState;
        private bool _isEnabled = true;

        public bool IsEnabled 
        { 
            get => _isEnabled; 
            set { _isEnabled = value; if (!value) Stop(); } 
        }

        public bool IsPlaying => _playback.IsPlaying;
        public SonificationState CurrentState => _stateMachine.CurrentState;
        public IObservable<SonificationState> StateChanged => _stateMachine.StateChanged;

        public event Action? PlaybackFinished;
        public event Action<int>? PlaybackPointReached;

        public SonificationManager(
            IPlaybackOrchestrator playback,
            INavigationSonifier navigation,
            IWorkspaceStore store,
            IMainThreadService mainThreadService,
            IEventBus eventBus,
            ILevelCrossingMonitor? levelCrossing = null)
        {
            _playback = playback;
            _navigation = navigation;
            _store = store;
            _mainThreadService = mainThreadService;
            _eventBus = eventBus;
            _levelCrossing = levelCrossing;
            _currentState = store.State;
            _stateMachine = new SonificationStateMachine(eventBus);

            _playback.PlaybackFinished += () => {
                _mainThreadService.InvokeOnMainThread(() => {
                    if (_store.State.IsPlaying) _store.Dispatch(new SetPlaybackAction(false, _store.State.PlaybackScope));
                });
                _stateMachine.Fire(SonificationTrigger.PlaybackStopped);
                PlaybackFinished?.Invoke();
            };
            
            _playback.PlaybackPointReached += (idx) => {
                // AudioSequencer already dispatches NavigateAction directly from its loop.
                // Dispatching again here from the main thread would create a race condition
                // where the cursor snaps back to a previous position mid-playback.
                PlaybackPointReached?.Invoke(idx);
            };

            _subscriptions.Add(_store.StateStream.Subscribe(state => {
                IsEnabled = state.IsSonificationEnabled;

                bool playingToggled = state.IsPlaying != _currentState.IsPlaying;
                bool scopeChanged = state.PlaybackScope != _currentState.PlaybackScope;
                bool indexChanged = state.CurrentDataIndex != _currentState.CurrentDataIndex;
                bool focusChanged = state.FocusedSeriesId != _currentState.FocusedSeriesId || state.FocusedComponentIndex != _currentState.FocusedComponentIndex;
                bool binChanged = state.FocusedBinIndex != _currentState.FocusedBinIndex;
                bool symbolChanged = state.Identity.Symbol != _currentState.Identity.Symbol
                                  || state.Identity.Timeframe != _currentState.Identity.Timeframe
                                  || state.Identity.Provider != _currentState.Identity.Provider;

                var oldState = _currentState;
                _currentState = state;

                // Symbol/timeframe/provider swap invalidates level-crossing history —
                // approach and sustained state is per-chart.
                if (symbolChanged) _levelCrossing?.Reset();

                if ((playingToggled && state.IsPlaying) || (state.IsPlaying && scopeChanged))
                {
                    _stateMachine.Fire(SonificationTrigger.PlaybackStarted);
                    _playback.StartPlayback(state);
                    return;
                }

                if (playingToggled && !state.IsPlaying)
                {
                    _stateMachine.Fire(SonificationTrigger.PlaybackStopped);
                    Stop();
                    return;
                }

                if (focusChanged)
                {
                    _stateMachine.Fire(SonificationTrigger.SelectionChanged);
                }

                if (indexChanged)
                {
                    _stateMachine.Fire(SonificationTrigger.NavigationStarted);
                }

                bool allowNavSounds = !state.IsPlaying || state.IsPaused;

                if (IsEnabled && allowNavSounds && (indexChanged || focusChanged || binChanged))
                {
                    _navigation.SyncNavigationSlots(state);
                    // Layer the three-tier level-crossing earcons (approach / crossing /
                    // sustained) on top. Fires at most a few quiet sines per bar — does
                    // not replace PlayEarcon on crossing, which still runs via the
                    // existing sonification strategy path.
                    if (indexChanged)
                    {
                        _levelCrossing?.OnBarNavigated(state);

                        // Cluster ticks: the OTHER markers on this bar, quietly, on slots
                        // 3-7. Without them a bar carrying several simultaneous signals
                        // sounds exactly like a bar carrying one — only the focused
                        // component is voiced by SyncNavigationSlots above.
                        //
                        // RESTORED 2026-08-24. CHANGES.md:13057 documents this call as
                        // shipped ("After SyncNavigationSlots on X-navigation events, calls
                        // FireClusterTicksAsync when not in playback mode") and it had been
                        // deleted at some point since, leaving 84 lines of provider code and
                        // SignalTierClassifier serving nothing. Its 12 tests all invoke the
                        // method directly, so removing the only caller could not turn any of
                        // them red — see NavigationSonifierClusterTests for the shape, and
                        // ClusterTicksFireOnNavigationTests for the guard on THIS call.
                        //
                        // crossSeriesMode: false — navigation scans only the focused series;
                        // cross-indicator audio belongs to playback. Fire-and-forget by
                        // design: navigation response must not wait on it.
                        _ = _navigation.FireClusterTicksAsync(
                            state,
                            state.CurrentDataIndex,
                            excludeSeriesId: state.FocusedSeriesId ?? string.Empty,
                            excludeComponentIndex: state.FocusedComponentIndex,
                            crossSeriesMode: false);
                    }
                }
            }));

            // Handle alerts via EventBus to trigger state machine
            _subscriptions.Add(_eventBus.AsObservable<AlertFiredEvent>().Subscribe(_ => _stateMachine.Fire(SonificationTrigger.AlertFired)));

            // Stop the navigation voice immediately when any navigation key is released.
            _subscriptions.Add(_eventBus.AsObservable<NavKeyReleasedEvent>().Subscribe(_ => _navigation.StopNavigationVoice()));
        }

        public void PlayNote(double freq, double dur, string wave, float vol, float pan, double delay = 0, bool force = false)
        {
            if (IsEnabled || force) _navigation.PlayNote(freq, dur, wave, vol, pan, delay);
        }

        public void PlayPatch(AccessibleTrader.Sdk.Models.SoundPatch patch, float volumeScale = 1f, float pan = 0f, bool force = false)
        {
            if (IsEnabled || force) _navigation.PlayPatch(patch, volumeScale, pan);
        }

        public void Stop() => _playback.Stop();
        public void Silence()
        {
            _playback.Stop();
            _navigation.Silence();
            _stateMachine.Fire(SonificationTrigger.Reset);
        }

        public AudioPoint CreateAudioPoint(ChartSeries series, int componentIndex, Ohlcv point, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, int dataIndex, float masterVolume = 1.0f, double? overrideValue = null)
        {
            return _navigation.CreateAudioPoint(series, componentIndex, point, relativeIndex, viewportWidth, viewportRange, dataIndex, masterVolume, overrideValue);
        }

        public void SetMasterVolume(float volume)
        {
            _navigation.SetMasterGain(volume);
        }

        public void Dispose()
        {
            foreach (var sub in _subscriptions) sub.Dispose();
            _subscriptions.Clear();
            _stateMachine.Dispose();
            Stop();
        }

        public enum SonificationState
        {
            Idle,
            GlobalAmbient,
            SelectionFocused,
            Navigating,
            PlaybackActive,
            AlertTriggered
        }

        public enum SonificationTrigger
        {
            DataReceived,
            SelectionChanged,
            NavigationStarted,
            PlaybackStarted,
            PlaybackStopped,
            AlertFired,
            Reset
        }

        private class SonificationStateMachine : AccessibleTrader.Sdk.Services.StateMachine<SonificationState, SonificationTrigger>
        {
            private readonly IEventBus _eventBus;
            public SonificationStateMachine(IEventBus eventBus) : base(SonificationState.Idle) 
            {
                _eventBus = eventBus;
            }

            protected override SonificationState Transition(SonificationState currentState, SonificationTrigger trigger)
            {
                return (currentState, trigger) switch
                {
                    (SonificationState.Idle, SonificationTrigger.DataReceived) => SonificationState.GlobalAmbient,
                    (_, SonificationTrigger.SelectionChanged) => SonificationState.SelectionFocused,
                    (_, SonificationTrigger.NavigationStarted) => SonificationState.Navigating,
                    (_, SonificationTrigger.PlaybackStarted) => SonificationState.PlaybackActive,
                    (SonificationState.PlaybackActive, SonificationTrigger.PlaybackStopped) => SonificationState.Idle,
                    (_, SonificationTrigger.AlertFired) => SonificationState.AlertTriggered,
                    (_, SonificationTrigger.Reset) => SonificationState.Idle,
                    _ => currentState
                };
            }

            protected override void OnTransitioned(SonificationState newState)
            {
                // Silence meta-speech about internal state transitions.
                // We only want to hear Play/Pause/Stop which are handled by the CommandDispatcher/SpeechRouter.
            }
        }
    }
}
