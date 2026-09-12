using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// Monitors series flagged with <see cref="SeriesConfig.IsAutoNarrated"/> and announces
    /// new indicator signals and zone transitions via TTS as they appear on live bar closes.
    ///
    /// <para>
    /// This is the IN-SESSION half: it is bound to the focused chart's <see cref="IWorkspaceStore"/>
    /// and scans after every <see cref="RedrawEvent"/>. What it scans FOR — markers, zone lines,
    /// clouds, overlay and level crosses, the volume reading, oscillator zones — and the memory
    /// of what has already been said live in <see cref="NarrationScanner"/>, which has no store
    /// and no bus, so the same ladder can be spoken by the background monitor with the browser
    /// closed (<see cref="HeadlessChart"/>). Cody, 2026-09-11: <i>"I also want the
    /// narration ladder to also be spoken when the browser is closed too."</i> Two copies of the
    /// scan would have been two places every rule has to be added; this class now answers only
    /// the store-shaped questions — is the chart ready, which bar just closed, is the master
    /// switch on, what did the coordinator defer.
    /// </para>
    ///
    /// <para>
    /// ONE SCAN, ONE UTTERANCE: everything a single scan finds — across every narrated series —
    /// is composed into one phrase and spoken once, most consequential first, capped. See
    /// <see cref="ScanUtterance"/> for why, for the order, and for what a cap can safely drop.
    /// </para>
    /// </summary>
    public sealed class AutoNarrationService : IAutoNarrationService, IDisposable
    {
        private readonly IWorkspaceStore _store;
        private readonly IEventBus _eventBus;
        private readonly ISpeechFeedbackRouter _speechRouter;
        private readonly NarrationScanner _scanner;
        private readonly List<IDisposable> _subs = new();

        /// <summary>
        /// The new-bar sentence the coordinator handed over, waiting to be spoken as the first
        /// clause of this bar's narration. Null when there is none. See
        /// <see cref="DeferBarCloseSentence"/>.
        /// </summary>
        private string? _pendingBarCloseSentence;

        /// <summary>Set of series IDs that were narrated in the previous StateStream emission.</summary>
        private HashSet<string> _prevNarratedIds = new();

        /// <summary>Data count seen on the last RedrawEvent. Zero = uninitialized.</summary>
        private int _lastDataCount = 0;

        public AutoNarrationService(
            IWorkspaceStore store,
            IEventBus eventBus,
            ISpeechFeedbackRouter speechRouter,
            IIndicatorContextAnalyzer contextAnalyzer)
        {
            _store = store;
            _eventBus = eventBus;
            _speechRouter = speechRouter;
            _scanner = new NarrationScanner(contextAnalyzer);

            // Detect narration toggled on/off for specific series so we can seed or clear state.
            _subs.Add(store.StateStream.Subscribe(OnStateChanged));

            // Scan for new signals after every indicator recalculation pass.
            // RedrawEvent fires at the end of both RecalculateAllAsync and RecalculateLastAsync,
            // covering every bar close and every live intra-bar tick.
            _subs.Add(_eventBus.AsObservable<RedrawEvent>()
                .Subscribe(_ => OnIndicatorsUpdated()));
        }

        // ── StateStream: detect narration enable/disable ─────────────────────────

        private void OnStateChanged(WorkspaceState state)
        {
            var currentIds = new HashSet<string>(
                state.ActiveSeries.Where(s => s.IsAutoNarrated).Select(s => s.Id));

            // Newly enabled — seed so no historical signals fire. The seed is the FORMING bar,
            // not the bar count: see NarrationScanner.Seed for the bar that could never speak.
            foreach (var id in currentIds.Except(_prevNarratedIds))
            {
                var series = state.ActiveSeries.FirstOrDefault(s => s.Id == id);
                if (series != null) _scanner.Seed(series, state);
            }

            // Disabled — clean up tracking to keep dictionaries lean
            foreach (var id in _prevNarratedIds.Except(currentIds))
                _scanner.Forget(id);

            _prevNarratedIds = currentIds;
        }

        // ── RedrawEvent: scan for new signals ────────────────────────────────────

        /// <inheritdoc />
        public bool WillNarrateBarClose()
        {
            var state = _store.State;
            return state.Data is { Count: > 0 }
                && state.DataStatus != DataStatus.LoadingHistorical
                && state.InitStatus == InitializationStatus.Ready
                && state.IsSpeechEnabled
                && state.NarrateSignalsOnBarClose
                && _prevNarratedIds.Any();
        }

        /// <inheritdoc />
        public void DeferBarCloseSentence(string sentence)
        {
            if (string.IsNullOrWhiteSpace(sentence)) return;
            // Replacing rather than appending: if two bars closed without a scan in between,
            // the older sentence describes a bar that is no longer the one that just closed,
            // and speaking it late is worse than not speaking it.
            _pendingBarCloseSentence = sentence.Trim();
        }

        /// <summary>
        /// ONE utterance per bar close: the new-bar sentence the coordinator deferred, then
        /// whatever this scan found, spoken together or not at all.
        ///
        /// <para>
        /// The pending sentence is taken BEFORE the scan and spoken even when the scan itself
        /// bails out — a narration gate must not be able to swallow the new-bar announcement,
        /// which answers to a different switch (<c>AnnounceNewBars</c>).
        /// </para>
        /// </summary>
        private void OnIndicatorsUpdated()
        {
            string? pending = _pendingBarCloseSentence;
            _pendingBarCloseSentence = null;

            string? scanned = ScanForNarration();

            string whole = string.Join(" ", new[] { pending, scanned }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            if (whole.Length == 0) return;

            _speechRouter.Speak(whole, interrupt: false, channel: SpeechChannel.Event);
        }

        /// <summary>What this scan found, or null. Speaks nothing itself — see the caller.</summary>
        private string? ScanForNarration()
        {
            var state = _store.State;
            if (state.Data == null || state.Data.Count == 0) return null;
            if (state.DataStatus == DataStatus.LoadingHistorical) return null;
            if (state.InitStatus != InitializationStatus.Ready) return null;
            if (!state.IsSpeechEnabled) return null;

            // The Narration tab's master switch. It sits ABOVE the per-series flag, not instead
            // of it: N picks WHICH series speak, this says whether any of them
            // do. Default ON, so on a chart with nothing flagged it gates nothing — what it buys
            // is one place to silence the whole channel without un-flagging six series and
            // having to remember which six they were.
            //
            // Placed exactly where the F2 speech gate is, and it inherits that gate's one known
            // edge: the per-series seeding in OnStateChanged is a DIFFERENT subscription and
            // keeps running while this is off (so re-enabling never replays a whole session),
            // but the 20-bar PivotConfirmWindow means the first scan after re-enabling can still
            // speak a signal from up to 20 bars back. That is pre-existing behaviour for F2, it
            // is bounded, and a signal 20 bars old is arguably still worth hearing — noted here
            // rather than special-cased.
            if (!state.NarrateSignalsOnBarClose) return null;

            if (!_prevNarratedIds.Any()) return null;

            int currentCount = state.Data.Count;
            bool isNewBar = currentCount > _lastDataCount && _lastDataCount > 0;
            int scanIndex = isNewBar ? _lastDataCount - 1 : currentCount - 1;
            _lastDataCount = currentCount;

            // Only scan confirmed (closed) bars to avoid announcing unstable live-bar values.
            // On bar close: just-closed bar is scanIndex. On intra-bar tick: penultimate bar.
            int closedBound = isNewBar ? scanIndex : currentCount - 2;
            if (closedBound < 0) return null;

            return _scanner.ScanAll(state.ActiveSeries, state, closedBound, isNewBar);
        }

        public void Dispose()
        {
            foreach (var s in _subs) s.Dispose();
        }
    }
}
