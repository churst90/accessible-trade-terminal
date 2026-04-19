using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Disposables;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Core.Models;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Accessibility
{
    public interface IAccessibilityFeedbackCoordinator
    {
    }

    /// <summary>
    /// The reactive "Brain" for accessibility routing.
    /// Observes the WorkspaceStore and routes state changes to Speech and Sonification engines.
    /// SILENCE POLICY: Default is silence; only specific data or user-initiated changes trigger feedback.
    /// </summary>
    public class AccessibilityFeedbackCoordinator : IAccessibilityFeedbackCoordinator, IDisposable
    {
        private readonly IWorkspaceStore _store;
        private readonly INavigationFeedbackManager _navManager;
        private readonly ISpeechFeedbackRouter _speechRouter;
        private readonly IAudioFeedbackRouter _audioRouter;
        private readonly ISpeechFormatter _formatter;
        private readonly IEventBus _eventBus;
        private readonly IEarconService _earconService;
        private readonly ISdkCandlePatternAnalyzer _patternAnalyzer;
        // Held purely to ensure AutoNarrationService is instantiated at startup.
        // All work is done inside that service via its own subscriptions.
        private readonly IAutoNarrationService _autoNarration;
        private readonly CompositeDisposable _subscriptions = new();

        private WorkspaceState _previousState;

        // Candle pattern debounce: only re-announce when pattern changes, not on every tick.
        private CandlePattern _lastAnnouncedPattern = CandlePattern.None;
        private CandleType _lastAnnouncedType = CandleType.Normal;
        private DateTime _lastPatternAnnouncement = DateTime.MinValue;
        private static readonly TimeSpan PatternDebounce = TimeSpan.FromSeconds(5);

        public AccessibilityFeedbackCoordinator(
            IWorkspaceStore store,
            INavigationFeedbackManager navManager,
            ISpeechFeedbackRouter speechRouter,
            IAudioFeedbackRouter audioRouter,
            ISpeechFormatter formatter,
            IEventBus eventBus,
            IEarconService earconService,
            ISdkCandlePatternAnalyzer patternAnalyzer,
            IAutoNarrationService autoNarration)
        {
            _store = store;
            _navManager = navManager;
            _speechRouter = speechRouter;
            _audioRouter = audioRouter;
            _formatter = formatter;
            _eventBus = eventBus;
            _earconService = earconService;
            _patternAnalyzer = patternAnalyzer;
            _autoNarration = autoNarration;
            _previousState = store.State;

            // OBSERVE THE STORE DIRECTLY
            _subscriptions.Add(store.StateStream.Subscribe(OnStateChanged));

            // Legacy support for specific manual events
            _subscriptions.Add(_eventBus.Subscribe<FeedbackRequestEvent>(OnFeedbackRequest));
            _subscriptions.Add(_eventBus.Subscribe<AnnouncementEvent>(e => _speechRouter.Speak(e.Message, e.Interrupt)));

            _subscriptions.Add(_eventBus.Subscribe<AlertFiredEvent>(ev => {
                var alert = ev.Alert;
                if (alert.Definition.Delivery == AlertDelivery.Speech || alert.Definition.Delivery == AlertDelivery.Both)
                    _speechRouter.Speak(alert.SpeechText, interrupt: true);
                if (alert.Definition.Delivery == AlertDelivery.Earcon || alert.Definition.Delivery == AlertDelivery.Both)
                    _audioRouter.PlayEarcon(FeedbackType.Alert);
            }));

            // Live bar events — gated by AnnounceNewBars setting
            _subscriptions.Add(_eventBus.Subscribe<NewBarEvent>(OnNewBar));
            _subscriptions.Add(_eventBus.Subscribe<IntraBarUpdateEvent>(OnIntraBarUpdate));
        }

        private void OnStateChanged(WorkspaceState state)
        {
            // Toggle confirmations that must fire even while playback is running.
            // Check these BEFORE the IsPlaying gate so Alt+C / Alt+L / F2 / F3 are always announced.
            if (state.IsSpeechEnabled != _previousState.IsSpeechEnabled)
                _speechRouter.Speak(state.IsSpeechEnabled ? "Speech on" : "Speech off", interrupt: true);
            if (state.IsSonificationEnabled != _previousState.IsSonificationEnabled)
                _speechRouter.Speak(state.IsSonificationEnabled ? "Sound on" : "Sound off", interrupt: true);
            if (state.IsHeikinAshi != _previousState.IsHeikinAshi)
                _speechRouter.Speak(state.IsHeikinAshi ? "Heikin-Ashi candles" : "Standard candles", interrupt: true);
            if (state.IsLogScale != _previousState.IsLogScale)
                _speechRouter.Speak(state.IsLogScale ? "Log scale" : "Linear scale", interrupt: true);

            // 1. GATING: Silence all navigation feedback while playback is active
            // The PlaybackOrchestrator handles its own sonification/speech.
            if (state.IsPlaying)
            {
                _previousState = state;
                return;
            }

            // TAB SWITCH GATE: suppress secondary viewport/status announcements on the single
            // state transition where ActiveTabIndex changes. The tab label is already announced
            // by WorkspaceStore.Dispatch via AnnouncementEvent. Letting viewport/initStatus
            // announcements race with that produces "loading history" / "loading link" speech.
            bool isTabSwitch = state.ActiveTabIndex != _previousState.ActiveTabIndex;
            if (isTabSwitch)
            {
                _navManager.IsSpeechEnabled = state.IsSpeechEnabled;
                _audioRouter.IsSonificationEnabled = state.IsSonificationEnabled;
                _previousState = state;
                return;
            }

            // Sync enabled state to feedback routers on every state update so F2/F3 actually take effect.
            _navManager.IsSpeechEnabled = state.IsSpeechEnabled;
            _audioRouter.IsSonificationEnabled = state.IsSonificationEnabled;

            bool indexChanged     = state.CurrentDataIndex    != _previousState.CurrentDataIndex;
            bool seriesChanged    = state.FocusedSeriesId     != _previousState.FocusedSeriesId;
            bool componentChanged = state.FocusedComponentIndex != _previousState.FocusedComponentIndex;
            bool binChanged       = state.FocusedBinIndex     != _previousState.FocusedBinIndex;
            bool contextChanged   = state.LastInteractionContext != _previousState.LastInteractionContext;
            bool dataStatusChanged  = state.DataStatus   != _previousState.DataStatus;
            bool initStatusChanged  = state.InitStatus   != _previousState.InitStatus;

            bool viewportLengthChanged = state.ViewportLength      != _previousState.ViewportLength;
            bool viewportStartChanged  = state.ViewportStartIndex  != _previousState.ViewportStartIndex;

            // 2. STATUS ANNOUNCEMENTS (Zoom, Pan, Speed, Mute, Hide)
            if (state.PanningGranularity != _previousState.PanningGranularity)
            {
                _speechRouter.Speak($"Panning step: {state.PanningGranularity} percent");
            }
            if (state.PlaybackSpeed != _previousState.PlaybackSpeed)
            {
                _speechRouter.Speak($"Playback speed: {state.PlaybackSpeed:F1}x");
            }

            // VIEWPORT ANNOUNCEMENT POLICY:
            // Zoom (ViewportLength changes) → always announce.
            // Pan  (ViewportStartIndex changes WITHOUT cursor moving) → announce.
            // Jump commands (NAV_LIVE, NAV_HOME, NAV_END): cursor AND viewport both move together.
            //   → suppress the viewport description; only the bar at the new position is spoken.
            bool isCursorJump = indexChanged && viewportStartChanged && !viewportLengthChanged;
            bool shouldAnnounceViewport = viewportLengthChanged || (viewportStartChanged && !isCursorJump);

            if (shouldAnnounceViewport && state.Data != null && state.Data.Any())
            {
                int startIdx = Math.Clamp(state.ViewportStartIndex, 0, state.Data.Count - 1);
                int endIdx   = Math.Clamp(state.ViewportStartIndex + state.ViewportLength - 1, 0, state.Data.Count - 1);
                string msg   = _formatter.FormatViewportDescription(state.ViewportLength, state.Data[startIdx].Date, state.Data[endIdx].Date);
                _speechRouter.Speak(msg, true);
            }

            // 3. SERIES STATE (Mute/Hide)
            var currentFocusedSeries = state.ActiveSeries.FirstOrDefault(s => s.Id == state.FocusedSeriesId);
            var prevFocusedSeries = _previousState.ActiveSeries.FirstOrDefault(s => s.Id == _previousState.FocusedSeriesId);

            if (currentFocusedSeries != null && prevFocusedSeries != null && currentFocusedSeries.Id == prevFocusedSeries.Id)
            {
                if (currentFocusedSeries.IsMuted != prevFocusedSeries.IsMuted)
                {
                    _speechRouter.Speak($"{currentFocusedSeries.FriendlyName} {(currentFocusedSeries.IsMuted ? "muted" : "active")}");
                }
                if (currentFocusedSeries.IsVisible != prevFocusedSeries.IsVisible)
                {
                    _speechRouter.Speak($"{currentFocusedSeries.FriendlyName} {(currentFocusedSeries.IsVisible ? "visible" : "hidden")}");
                }
            }

            // 4. DATA STATUS / INITIALIZATION FEEDBACK
            if (dataStatusChanged && state.DataStatus == DataStatus.LoadingHistorical)
            {
                _speechRouter.Speak("Loading history...", true);
            }

            if (initStatusChanged && state.InitStatus == InitializationStatus.Ready)
            {
                var id = state.Identity;
                string readyMsg = (!string.IsNullOrEmpty(id.Symbol) && !string.IsNullOrEmpty(id.Provider))
                    ? $"{id.Symbol} on {id.Provider}, {id.Timeframe}. Ready."
                    : "Chart ready.";
                _speechRouter.Speak(readyMsg, interrupt: true);
            }

            if (initStatusChanged && state.InitStatus == InitializationStatus.Error)
            {
                _speechRouter.Speak("Chart failed to load.", interrupt: true);
            }

            // 5. NAVIGATION FEEDBACK is handled exclusively via FeedbackRequestEvent (OnFeedbackRequest).
            // NavigationEngine.NavigateX/Y/Series publish FeedbackRequestEvent after each move.
            // Driving feedback from OnStateChanged as well would cause double-announcement because
            // the second call interrupts the first — cutting off series names mid-sentence.

            _previousState = state;
        }

        /// <summary>
        /// Called when a live bar is finalized and a new one opens.
        /// Announces the closed bar's pattern and the new bar's open price, then plays bell earcon.
        /// </summary>
        private void OnNewBar(NewBarEvent e)
        {
            var state = _store.State;
            if (!state.IsSpeechEnabled || !state.AnnounceNewBars) return;

            // Pattern on the finalized bar (use up to 2 prior bars for context).
            var data = state.Data;
            Ohlcv? prev  = (data != null && data.Count >= 2) ? data[^2] : (Ohlcv?)null;
            Ohlcv? prev2 = (data != null && data.Count >= 3) ? data[^3] : (Ohlcv?)null;
            var analysis = _patternAnalyzer.Analyze(e.ClosedBar, prev, prev2);

            string patternSuffix = FormatPatternSuffix(analysis.Type, analysis.Pattern, finalized: true);
            string closedMsg = $"Close {SpeechPriceFormatter.FormatPrice(e.ClosedBar.Close)}{patternSuffix}.";
            string openMsg   = $"New bar: Open {SpeechPriceFormatter.FormatPrice(e.NewBar.Open)}";

            _earconService.PlayNewBar();
            _speechRouter.Speak($"{closedMsg} {openMsg}", interrupt: false);

            // Reset intra-bar debounce for the new bar.
            _lastAnnouncedPattern = CandlePattern.None;
            _lastAnnouncedType    = CandleType.Normal;
            _lastPatternAnnouncement = DateTime.UtcNow;
        }

        /// <summary>
        /// Called on every intra-bar tick. Runs pattern recognition and announces
        /// when a new pattern is detected, subject to a debounce window.
        /// </summary>
        private void OnIntraBarUpdate(IntraBarUpdateEvent e)
        {
            var state = _store.State;
            if (!state.IsSpeechEnabled || !state.AnnounceNewBars) return;
            if (state.IsPlaying) return;

            // Per-user feedback (2026-04-09): intra-bar pattern updates were firing in
            // real time regardless of auto-narration state, which breaks the convention
            // that continuous verbal output requires the user to have explicitly
            // enabled narration. The finalized-bar announcement (OnNewBar) stays gated
            // only on AnnounceNewBars — that's a single event-at-close notification,
            // not continuous narration. Intra-bar "still forming" updates, however,
            // are continuous and must respect the Candles series' IsAutoNarrated flag.
            var candles = state.ActiveSeries.FirstOrDefault(s => s.Id == state.PrimarySeriesId)
                       ?? state.ActiveSeries.FirstOrDefault(s => s.Id == CoreSeriesIds.Candles);
            if (candles == null || !candles.IsAutoNarrated) return;

            // Debounce: don't announce more often than PatternDebounce.
            if (DateTime.UtcNow - _lastPatternAnnouncement < PatternDebounce) return;

            var analysis = _patternAnalyzer.Analyze(e.CurrentBar, e.PreviousBar, e.TwoBarsAgo);

            bool patternChanged = analysis.Pattern != _lastAnnouncedPattern
                               || analysis.Type    != _lastAnnouncedType;

            // Only announce non-trivial, non-Normal forming patterns.
            bool isInteresting = analysis.Pattern != CandlePattern.None
                              || analysis.Type != CandleType.Normal;

            if (patternChanged && isInteresting)
            {
                string msg = FormatFormingPattern(analysis.Type, analysis.Pattern);
                if (!string.IsNullOrEmpty(msg))
                {
                    _speechRouter.Speak(msg, interrupt: false);
                    _lastAnnouncedPattern    = analysis.Pattern;
                    _lastAnnouncedType       = analysis.Type;
                    _lastPatternAnnouncement = DateTime.UtcNow;
                }
            }
        }

        // ── Pattern Speech Helpers ─────────────────────────────────────────────

        private static string FormatPatternSuffix(CandleType type, CandlePattern pattern, bool finalized)
        {
            string verb = finalized ? "" : " forming";
            if (pattern != CandlePattern.None)
                return $", {FormatPatternName(pattern)}{verb}";
            if (type != CandleType.Normal)
                return $", {FormatTypeName(type)}{verb}";
            return "";
        }

        private static string FormatFormingPattern(CandleType type, CandlePattern pattern)
        {
            if (pattern != CandlePattern.None) return $"{FormatPatternName(pattern)} forming";
            if (type != CandleType.Normal)     return $"{FormatTypeName(type)} forming";
            return "";
        }

        private static string FormatPatternName(CandlePattern p) => p switch
        {
            CandlePattern.BullishEngulfing    => "Bullish engulfing",
            CandlePattern.BearishEngulfing    => "Bearish engulfing",
            CandlePattern.BullishHarami       => "Bullish harami",
            CandlePattern.BearishHarami       => "Bearish harami",
            CandlePattern.PiercingLine        => "Piercing line",
            CandlePattern.DarkCloudCover      => "Dark cloud cover",
            CandlePattern.TweezerBottom       => "Tweezer bottom",
            CandlePattern.TweezerTop          => "Tweezer top",
            CandlePattern.MorningStar         => "Morning star",
            CandlePattern.EveningStar         => "Evening star",
            CandlePattern.ThreeWhiteSoldiers  => "Three white soldiers",
            CandlePattern.ThreeBlackCrows     => "Three black crows",
            _                                  => ""
        };

        private static string FormatTypeName(CandleType t) => t switch
        {
            CandleType.Doji             => "Doji",
            CandleType.DragonflyDoji    => "Dragonfly doji",
            CandleType.GravestoneDoji   => "Gravestone doji",
            CandleType.LongLeggedDoji   => "Long-legged doji",
            CandleType.Hammer           => "Hammer",
            CandleType.HangingMan       => "Hanging man",
            CandleType.InvertedHammer   => "Inverted hammer",
            CandleType.ShootingStar     => "Shooting star",
            CandleType.MarubozuBullish  => "Bullish marubozu",
            CandleType.MarubozuBearish  => "Bearish marubozu",
            CandleType.SpinningTop      => "Spinning top",
            _                            => ""
        };

        private void OnFeedbackRequest(FeedbackRequestEvent e)
        {
            // Filter out Meta-Speech (e.g., "Audio mode: Idle")
            if (e.Message != null && (e.Message.Contains("Audio mode:") || e.Message.Contains("Playback mode:"))) return;

            switch (e.Type)
            {
                case FeedbackType.StateChange:
                    if (!string.IsNullOrEmpty(e.Message))
                        _speechRouter.Speak(e.Message, interrupt: true);
                    break;

                case FeedbackType.Navigation:
                    _navManager.HandleNavigationFeedback(_store.State, e.IsXMove, e.IsYMove, e.Message ?? "", isJump: e.IsJump);
                    break;

                case FeedbackType.VolumeChange:
                    if (!string.IsNullOrEmpty(e.Message))
                        _speechRouter.Speak(e.Message, interrupt: false);
                    break;

                case FeedbackType.Error:
                    if (!string.IsNullOrEmpty(e.Message))
                        _speechRouter.Speak(e.Message, interrupt: true);
                    break;

                case FeedbackType.Boundary:
                    // Earcon only — no spoken phrase at viewport boundaries per user preference.
                    _audioRouter.PlayEarcon(FeedbackType.Boundary);
                    break;

                case FeedbackType.Info:
                    if (e.Message == "CONTEXT_SUMMARY")
                    {
                        var state = _store.State;
                        var id = state.Identity;
                        string timeframe = string.IsNullOrEmpty(id.Timeframe) ? "unknown timeframe" : id.Timeframe;
                        string symbol = string.IsNullOrEmpty(id.Symbol) ? "no symbol" : id.Symbol;
                        string provider = string.IsNullOrEmpty(id.Provider) ? "" : $" on {id.Provider}";
                        string msg = $"{symbol}{provider}, {timeframe}";

                        // Append focused series/pane info if a component is focused.
                        var focusedSeries = state.ActiveSeries.FirstOrDefault(s => s.Id == state.FocusedSeriesId);
                        if (focusedSeries != null && state.LastInteractionContext == InteractionContext.Component)
                        {
                            int compIdx = Math.Clamp(state.FocusedComponentIndex, 0, focusedSeries.Components.Count - 1);
                            string? subPane = focusedSeries.Components.Count > 0
                                ? focusedSeries.Components[compIdx].SubPaneName
                                : null;
                            string paneLabel = string.IsNullOrEmpty(subPane) ? "main pane" : subPane + " pane";
                            msg += $". Focused on {focusedSeries.FriendlyName}, {paneLabel}";

                            // If series has multiple panes, append total pane count.
                            int distinctSubPanes = focusedSeries.Components
                                .Select(c => c.SubPaneName)
                                .Where(n => !string.IsNullOrEmpty(n))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .Count();
                            if (distinctSubPanes > 0)
                                msg += $". {focusedSeries.FriendlyName} has {distinctSubPanes + 1} panes";
                        }

                        _speechRouter.Speak(msg, interrupt: true);
                    }
                    else if (!string.IsNullOrEmpty(e.Message))
                    {
                        _speechRouter.Speak(e.Message, interrupt: true);
                    }
                    break;
            }
        }

        public void Dispose()
        {
            _subscriptions.Dispose();
        }
    }
}
