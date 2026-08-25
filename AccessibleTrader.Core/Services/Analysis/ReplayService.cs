using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Analysis
{
    /// <summary>
    /// Bar replay: reveals loaded history one bar at a time so the user can practise reading a
    /// market forward, without hindsight.
    ///
    /// <para>
    /// The implementation is deliberately boring — it dispatches a growing PREFIX of the real bar
    /// list through the ordinary <see cref="UpdateDataAction"/> path. Everything downstream
    /// (indicator recomputation, viewport, speech, sonification, strategy evaluation, the Dot Pad)
    /// therefore behaves exactly as it does live, with no replay-specific branches anywhere else
    /// in the app. Nothing needs to know replay exists.
    /// </para>
    /// </summary>
    public interface IReplayService
    {
        bool IsActive { get; }
        /// <summary>Bars currently revealed. Zero when inactive.</summary>
        int RevealedCount { get; }
        /// <summary>Bars in the captured history. Zero when inactive.</summary>
        int TotalCount { get; }
        bool IsPlaying { get; }

        /// <summary>
        /// Captures the store's current history and reveals the first <paramref name="startIndex"/>
        /// bars. Returns false when there is nothing to replay or replay is already running.
        /// </summary>
        bool Start(int startIndex);

        /// <summary>
        /// Reveals <paramref name="delta"/> more bars (negative hides bars again). Returns the new
        /// revealed count, or -1 when replay is not running.
        /// </summary>
        int Step(int delta = 1);

        /// <summary>Auto-advances one bar every <paramref name="intervalMs"/> until paused or finished.</summary>
        void Play(int intervalMs = 1000);

        void Pause();

        /// <summary>Restores the full captured history and leaves replay mode. Safe to call when inactive.</summary>
        void Stop();
    }

    /// <inheritdoc cref="IReplayService"/>
    public sealed class ReplayService : IReplayService, IDisposable
    {
        /// <summary>Floor on revealed bars — indicators need some history to mean anything.</summary>
        public const int MinRevealed = 2;

        private readonly IWorkspaceStore _store;
        private readonly IEventBus _eventBus;

        private readonly object _gate = new();
        private readonly IDisposable? _commandSub;
        private IReadOnlyList<Ohlcv> _history = Array.Empty<Ohlcv>();
        private int _revealed;
        private Timer? _timer;

        public ReplayService(IWorkspaceStore store, IEventBus eventBus)
        {
            _store = store;
            _eventBus = eventBus;
            _commandSub = eventBus.Subscribe<ReplayCommandEvent>(e => Handle(e.Command));
        }

        /// <summary>Applies a transport verb from the keyboard. Idempotent and safe when inactive.</summary>
        public void Handle(ReplayCommand command)
        {
            switch (command)
            {
                case ReplayCommand.Toggle:
                    if (IsActive) Stop();
                    else if (!Start(_store.State.CurrentDataIndex))
                        Announce("Not enough history loaded to replay.");
                    break;

                case ReplayCommand.StepForward:
                case ReplayCommand.StepBack:
                    if (!IsActive) { Announce("Replay is not running. Control Alt Shift P starts it."); break; }
                    int at = Step(command == ReplayCommand.StepForward ? 1 : -1);
                    if (at >= 0) Announce($"Bar {at} of {TotalCount}.");
                    break;

                case ReplayCommand.PlayPause:
                    if (!IsActive) { Announce("Replay is not running. Control Alt Shift P starts it."); break; }
                    if (IsPlaying) Pause(); else Play();
                    break;
            }
        }

        public bool IsActive { get; private set; }
        public bool IsPlaying { get; private set; }
        public int RevealedCount { get { lock (_gate) return IsActive ? _revealed : 0; } }
        public int TotalCount { get { lock (_gate) return IsActive ? _history.Count : 0; } }

        public bool Start(int startIndex)
        {
            lock (_gate)
            {
                if (IsActive) return false;

                var data = _store.State.Data;
                if (data == null || data.Count < MinRevealed + 1) return false;

                _history = new List<Ohlcv>(data);
                // startIndex is a bar index; revealing "up to and including" it means count = index + 1.
                _revealed = Math.Clamp(startIndex + 1, MinRevealed, _history.Count - 1);
                IsActive = true;
            }

            // Order matters: the mode flag must land BEFORE the truncated buffer, or a live tick
            // arriving between the two dispatches would be treated as a normal update.
            _store.Dispatch(new SetReplayModeAction(true));
            PushPrefix();
            Announce($"Replay started at bar {RevealedCount} of {TotalCount}. " +
                     "F9 steps forward, Shift F9 back, F8 plays or pauses.");
            return true;
        }

        public int Step(int delta = 1)
        {
            int revealed, total;
            bool finished;

            lock (_gate)
            {
                if (!IsActive) return -1;
                total = _history.Count;
                _revealed = Math.Clamp(_revealed + delta, MinRevealed, total);
                revealed = _revealed;
                finished = revealed >= total;
            }

            PushPrefix();

            if (finished && IsPlaying)
            {
                Pause();
                Announce("Replay reached the end of history.");
            }
            return revealed;
        }

        public void Play(int intervalMs = 1000)
        {
            if (!IsActive || IsPlaying) return;
            if (intervalMs < 50) intervalMs = 50;

            IsPlaying = true;
            _timer = new Timer(_ => Step(1), null, intervalMs, intervalMs);
            Announce("Replay playing.");
        }

        public void Pause()
        {
            if (!IsPlaying) return;
            IsPlaying = false;
            _timer?.Dispose();
            _timer = null;
            Announce("Replay paused.");
        }

        public void Stop()
        {
            IReadOnlyList<Ohlcv> full;
            lock (_gate)
            {
                if (!IsActive) return;
                full = _history;
                IsActive = false;
                _revealed = 0;
                _history = Array.Empty<Ohlcv>();
            }

            if (IsPlaying)
            {
                IsPlaying = false;
                _timer?.Dispose();
                _timer = null;
            }

            // Restore the captured history first, then clear the mode flag. The reverse order
            // would leave a window where a live tick could append to the truncated prefix.
            _store.Dispatch(new UpdateDataAction(new TimeSeriesBuffer<Ohlcv>(full), IsInitialLoad: false));
            _store.Dispatch(new SetReplayModeAction(false));
            _store.Dispatch(new JumpToLatestAction());
            Announce("Replay stopped. Full history restored.");
        }

        private void PushPrefix()
        {
            int revealed;
            List<Ohlcv> prefix;
            lock (_gate)
            {
                if (!IsActive) return;
                revealed = _revealed;
                prefix = new List<Ohlcv>(revealed);
                for (int i = 0; i < revealed && i < _history.Count; i++) prefix.Add(_history[i]);
            }

            _store.Dispatch(new UpdateDataAction(new TimeSeriesBuffer<Ohlcv>(prefix), IsInitialLoad: false));
            // Park the cursor on the newest revealed bar so arrow navigation and the bar-detail
            // readout describe the bar the user just uncovered.
            _store.Dispatch(new NavigateAction(revealed - 1));
        }

        private void Announce(string message) =>
            _eventBus.Publish(new AnnouncementEvent(message, true));

        public void Dispose()
        {
            _commandSub?.Dispose();
            _timer?.Dispose();
            _timer = null;
        }
    }
}
