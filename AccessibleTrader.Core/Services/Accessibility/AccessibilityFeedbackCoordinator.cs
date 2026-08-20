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
using AccessibleTrader.Core.Services.Analysis;
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
        private readonly IChartPatternCache _patternCache;
        private readonly IChartPatternFocus _patternFocus;
        private readonly Trading.QuickTradeService? _quickTrade;
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
            IChartPatternCache patternCache,
            IChartPatternFocus patternFocus,
            IAutoNarrationService autoNarration,
            Trading.IQuickTradeService? quickTrade = null)
        {
            _store = store;
            _navManager = navManager;
            _speechRouter = speechRouter;
            _audioRouter = audioRouter;
            _formatter = formatter;
            _eventBus = eventBus;
            _earconService = earconService;
            _patternAnalyzer = patternAnalyzer;
            _patternCache = patternCache;
            _patternFocus = patternFocus;
            _quickTrade = quickTrade as Trading.QuickTradeService;
            _autoNarration = autoNarration;
            _previousState = store.State;

            // OBSERVE THE STORE DIRECTLY
            _subscriptions.Add(store.StateStream.Subscribe(OnStateChanged));

            // Legacy support for specific manual events
            _subscriptions.Add(_eventBus.Subscribe<FeedbackRequestEvent>(OnFeedbackRequest));
            _subscriptions.Add(_eventBus.Subscribe<AnnouncementEvent>(e => _speechRouter.Speak(e.Message, e.Interrupt)));

            _subscriptions.Add(_eventBus.Subscribe<AlertFiredEvent>(ev => {
                var alert = ev.Alert;
                // Ambient tier (Shift+F2 / Shift+F3) unless the user marked THIS
                // alert break-through — then it pierces every mute (Critical).
                bool pierce = alert.Definition.BreakThroughMutes;
                if (alert.Definition.Delivery == AlertDelivery.Speech || alert.Definition.Delivery == AlertDelivery.Both)
                    _speechRouter.Speak(alert.SpeechText, interrupt: true,
                        channel: pierce ? SpeechChannel.Critical : SpeechChannel.Event);
                if (alert.Definition.Delivery == AlertDelivery.Earcon || alert.Definition.Delivery == AlertDelivery.Both)
                    _earconService.PlayAlert(breakThroughMutes: pierce);
            }));

            // Live bar events — gated by AnnounceNewBars setting
            _subscriptions.Add(_eventBus.Subscribe<NewBarEvent>(OnNewBar));
            _subscriptions.Add(_eventBus.Subscribe<IntraBarUpdateEvent>(OnIntraBarUpdate));

            // Order lifecycle events — money events are NEVER gated on speech/sonification
            // toggles, playback state, or narration settings. Like the Error branch in
            // OnFeedbackRequest: earcon first (immediate cue even mid-phrase), then
            // interrupting speech with the detail. Speech is mirrored to the journal by
            // the speech manager, so every fill is reviewable after the fact.
            _subscriptions.Add(_eventBus.Subscribe<OrderFilledEvent>(e =>
            {
                _earconService.PlayOrderFill(e.Order.Side);
                _speechRouter.Speak(FormatFill("Order filled", e.Order), interrupt: true, channel: SpeechChannel.OrderEvent);
            }));
            _subscriptions.Add(_eventBus.Subscribe<OrderPartialFillEvent>(e =>
            {
                _earconService.PlayOrderFill(e.Order.Side);
                _speechRouter.Speak(FormatPartialFill(e.Order), interrupt: true, channel: SpeechChannel.OrderEvent);
            }));
            _subscriptions.Add(_eventBus.Subscribe<StopHitEvent>(e =>
            {
                _earconService.PlayStopHit();
                _speechRouter.Speak(FormatFill(e.Order.Trailing ? "Trailing stop hit" : "Stop loss hit", e.Order), interrupt: true, channel: SpeechChannel.OrderEvent);
            }));
            _subscriptions.Add(_eventBus.Subscribe<TakeProfitHitEvent>(e =>
            {
                _earconService.PlayTakeProfitHit();
                _speechRouter.Speak(FormatFill(e.Order.Trailing ? "Trailing take profit hit" : "Take profit hit", e.Order), interrupt: true, channel: SpeechChannel.OrderEvent);
            }));
            // The reason was being carried on the event and dropped here, so every
            // rejection sounded identical — the user learned that something did not
            // happen and never what to change about it. Insufficient balance, a
            // sell with nothing to sell and a venue refusal are different problems
            // with different fixes.
            _subscriptions.Add(_eventBus.Subscribe<OrderRejectedEvent>(e =>
            {
                _audioRouter.PlayEarcon(FeedbackType.Error, ErrorSeverity.High);
                string why = string.IsNullOrWhiteSpace(e.Reason) ? "" : " " + e.Reason.TrimEnd('.') + ".";
                _speechRouter.Speak($"Order rejected for {e.Order.Symbol}.{why}", interrupt: true, channel: SpeechChannel.OrderEvent);
            }));
            // Cancels were the one order state change that vanished silently
            // (2026-07-22 audit). Not an error earcon — user-initiated cancels are
            // routine — but always spoken on the order channel.
            _subscriptions.Add(_eventBus.Subscribe<OrderCancelledEvent>(e =>
            {
                _audioRouter.PlayEarcon(FeedbackType.StateChange, ErrorSeverity.Low);
                _speechRouter.Speak($"Order cancelled for {e.Order.Symbol}.", interrupt: false, channel: SpeechChannel.OrderEvent);
            }));
            // Margin/liquidation proximity — a leveraged position drifting toward its
            // liquidation price is a High-severity safety event: error earcon plus an
            // interrupting spoken warning on the order channel. Detection (and per-symbol
            // debouncing) lives in TradingReconciliationCoordinator, which publishes
            // MarginWarningEvent; this is purely the voice/earcon presentation.
            _subscriptions.Add(_eventBus.Subscribe<MarginWarningEvent>(e =>
            {
                _audioRouter.PlayEarcon(FeedbackType.Error, ErrorSeverity.High);
                _speechRouter.Speak(e.Message, interrupt: true, channel: SpeechChannel.OrderEvent);
            }));
        }

        // ── Order speech formatting ────────────────────────────────────────────
        // Internal static so OrderEventAnnouncementTests can verify formats directly.

        internal static string FormatFill(string prefix, Sdk.Trading.OrderUpdate o)
        {
            string side = o.Side == Sdk.Plugins.OrderSide.Buy ? "bought" : "sold";
            string at = o.FilledPrice > 0 ? $" at {SpeechPriceFormatter.FormatPrice(o.FilledPrice)}" : "";
            // On a closing fill the provider supplies realized P&L — announce it so
            // the trader hears the result without opening the dashboard.
            string pnl = o.RealizedPnL.HasValue
                ? $" {(o.RealizedPnL.Value >= 0 ? "Profit" : "Loss")} {SpeechPriceFormatter.FormatPrice(Math.Abs(o.RealizedPnL.Value))}."
                : "";
            return $"{prefix}. {Capitalize(side)} {FormatQty(o.FilledQuantity)} {o.Symbol}{at}.{pnl}";
        }

        internal static string FormatPartialFill(Sdk.Trading.OrderUpdate o)
        {
            string baseMsg = FormatFill("Partial fill", o);
            return o.RemainingQuantity > 0
                ? $"{baseMsg} {FormatQty(o.RemainingQuantity)} remaining."
                : baseMsg;
        }

        private static string FormatQty(double qty) => qty.ToString("0.########");

        private static string Capitalize(string s) =>
            s.Length > 0 ? char.ToUpperInvariant(s[0]) + s.Substring(1) : s;

        private void OnStateChanged(WorkspaceState state)
        {
            // Toggle confirmations that must fire even while playback is running.
            // Check these BEFORE the IsPlaying gate so Alt+C / Alt+L / F2 / F3 are always announced.
            //
            // Speech-toggle specifically also fires an Info earcon so the blind user hears an
            // immediate audio cue for the state change. (The earcon goes through EarconService
            // which gates on sonification-IsEnabled, so in the rare case where both speech AND
            // sonification are off at the same time the earcon is silent — but in that mode
            // the user has explicitly opted into full silence.)
            //
            // Sonification-toggle does NOT fire an earcon on purpose: turning sonification OFF
            // while immediately playing a beep contradicts the intent, and turning it ON is
            // immediately evidenced by the first subsequent navigation producing sound. The
            // speech confirmation carries the state transition either way.
            if (state.IsSpeechEnabled != _previousState.IsSpeechEnabled)
            {
                _audioRouter.PlayEarcon(FeedbackType.Info);
                _speechRouter.Speak(state.IsSpeechEnabled ? "Speech on" : "Speech off",
                    interrupt: true, channel: SpeechChannel.Critical);
            }
            if (state.IsSonificationEnabled != _previousState.IsSonificationEnabled)
                _speechRouter.Speak(state.IsSonificationEnabled ? "Sound on" : "Sound off",
                    interrupt: true, channel: SpeechChannel.Critical);
            if (state.IsEventSpeechEnabled != _previousState.IsEventSpeechEnabled)
                _speechRouter.Speak(state.IsEventSpeechEnabled ? "Alerts and events on" : "Alerts and events muted",
                    interrupt: true, channel: SpeechChannel.Critical);
            if (state.IsEarconsEnabled != _previousState.IsEarconsEnabled)
                _speechRouter.Speak(state.IsEarconsEnabled ? "Earcons on" : "Earcons muted",
                    interrupt: true, channel: SpeechChannel.Critical);
            if (state.IsHeikinAshi != _previousState.IsHeikinAshi)
            {
                // The caveat is spoken only when it applies — Heikin-Ashi going ON with formation
                // description already enabled. Otherwise it is noise about a feature the user is
                // not using.
                //
                // Formations are detected on STANDARD candles regardless of what is displayed, and
                // the user has to be told, because their spoken bar values ARE Heikin-Ashi. Without
                // this line the terminal reads HA opens and closes and then names a neckline that
                // exists in neither of the numbers it just read. The reason for the split is that a
                // Heikin-Ashi close is an average of four prices, not a price anything ever traded
                // at — so a level derived from one cannot be put in an order, and the trigger and
                // measured target are exactly the numbers a user might act on.
                string msg = state.IsHeikinAshi
                    ? (state.DescribeChartPatterns
                        ? "Heikin-Ashi candles. Chart formations are still read from standard candles."
                        : "Heikin-Ashi candles")
                    : "Standard candles";
                _speechRouter.Speak(msg, interrupt: true);
            }
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
                // Forget where the cursor was. The formation diff compares the current bar against
                // the previous one, and after a tab switch "the previous one" belonged to a
                // different chart — so the very first arrow key on the new tab would diff bar 300
                // of BTC against bar 300 of TAO. -1 means "no idea", which makes the next move take
                // the jump path and simply describe what is here.
                _lastPatternBar = -1;
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
            if (!state.AnnounceNewBars) return; // speech mute handled by the Event channel

            // Pattern on the finalized bar (use up to 2 prior bars for context).
            var data = state.Data;
            Ohlcv? prev  = (data != null && data.Count >= 2) ? data[^2] : (Ohlcv?)null;
            Ohlcv? prev2 = (data != null && data.Count >= 3) ? data[^3] : (Ohlcv?)null;
            var analysis = _patternAnalyzer.Analyze(e.ClosedBar, prev, prev2, data);

            string patternSuffix = FormatPatternSuffix(analysis.Type, analysis.Pattern, finalized: true);
            string closedMsg = $"Close {SpeechPriceFormatter.FormatPrice(e.ClosedBar.Close)}{patternSuffix}.";
            string openMsg   = $"New bar: Open {SpeechPriceFormatter.FormatPrice(e.NewBar.Open)}";

            _earconService.PlayNewBar();
            _speechRouter.Speak($"{closedMsg} {openMsg}", interrupt: false, channel: SpeechChannel.Event);

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
            if (!state.AnnounceNewBars) return; // speech mute handled by the Event channel
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

            // The forming bar is not in state.Data yet, so the trend context is the stored history
            // with the live bar appended — otherwise hammer-vs-hanging-man on the bar being watched
            // in real time is the one place that still guesses.
            var history = state.Data;
            IReadOnlyList<Ohlcv>? context = history == null || history.Count == 0
                ? null
                : history.Append(e.CurrentBar).ToList();

            var analysis = _patternAnalyzer.Analyze(e.CurrentBar, e.PreviousBar, e.TwoBarsAgo, context);

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
                    _speechRouter.Speak(msg, interrupt: false, channel: SpeechChannel.Event);
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
                    // The chart-formation clause is computed BEFORE the bar is read and handed in,
                    // rather than spoken afterwards as a second utterance. Two Speak calls in one
                    // keypress do not reliably produce two announcements: on the web head speech
                    // goes into an ARIA live region, Blazor batches the whole event handler into a
                    // single render, and only the last write to that region ever reaches the DOM —
                    // so the earlier phrase is silently dropped. Composing one utterance is the only
                    // arrangement in which everything true about a bar is actually heard.
                    // The armed-trade reminder leads even the formation clause. While a trade is
                    // armed it is the single most consequential fact about the current keystroke,
                    // and a user who has forgotten they are armed is the failure mode the whole
                    // feature is designed against.
                    string? nav = e.IsXMove ? ChartPatternContext() : null;
                    string armed = _quickTrade?.ArmedSuffix() ?? "";
                    if (armed.Length > 0)
                        nav = string.IsNullOrWhiteSpace(nav) ? armed : armed + " " + nav;

                    _navManager.HandleNavigationFeedback(
                        _store.State, e.IsXMove, e.IsYMove, e.Message ?? "", isJump: e.IsJump,
                        extraContext: nav);
                    break;

                case FeedbackType.VolumeChange:
                    if (!string.IsNullOrEmpty(e.Message))
                        _speechRouter.Speak(e.Message, interrupt: false);
                    break;

                case FeedbackType.Error:
                    // Earcon FIRST so the blind user gets an immediate audio cue even if
                    // the screen reader is mid-phrase. Speech then follows with the detail.
                    // Previously this branch did speech only, which meant order-placement
                    // failures, provider disconnects, and any ReportError(..., High) path
                    // produced no earcon — violating the silent-failure rule.
                    _audioRouter.PlayEarcon(FeedbackType.Error, ErrorSeverity.High);
                    if (!string.IsNullOrEmpty(e.Message))
                        _speechRouter.Speak(e.Message, interrupt: true, channel: SpeechChannel.Critical);
                    break;

                case FeedbackType.Boundary:
                    // Earcon always. A bare boundary — the viewport edge — is earcon ONLY, per user
                    // preference: hitting the end of the chart is a routine event that does not need
                    // a sentence every time.
                    //
                    // But a boundary carrying a MESSAGE is a different thing, and its message used
                    // to be discarded here. Ten call sites passed one and none of them was ever
                    // heard: "No more [component] signals in this direction", "Focused trendline has
                    // no anchors", "Focused trendline anchors are off-chart", "No chart formations
                    // on this chart". SHORTCUTS.md documents the first of those as spoken, and it
                    // has never spoken. Each of them explains why a key the user just pressed did
                    // nothing, which is precisely the case where silence is indistinguishable from a
                    // broken binding — the exact failure the feedback contract forbids.
                    _audioRouter.PlayEarcon(FeedbackType.Boundary);
                    if (!string.IsNullOrEmpty(e.Message))
                        _speechRouter.Speak(e.Message, interrupt: true);
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

        // ── Chart-pattern description (opt-in) ─────────────────────────────────

        // The bar the cursor was on last time a pattern readout was computed. -1 means "no idea",
        // which is the correct state after a load, a jump, or a timeframe change — see below.
        private int _lastPatternBar = -1;

        /// <summary>
        /// The chart-formation clause for the bar the cursor has just moved to, or "" for silence.
        ///
        /// <para>
        /// This is EDGE-TRIGGERED, and that is the whole design. The first version described
        /// whatever overlapped the current bar and suppressed repeats, which sounds equivalent and
        /// is not: as the overlapping set churned bar by bar — a flag dropping out here, a triangle
        /// arriving there — the readout kept changing, so the suppression kept failing, and the
        /// user heard a different pile of formations every few bars with no way to tell which were
        /// new. What a person actually needs to know is when they have <b>crossed into</b> a
        /// formation and when it <b>resolved</b>. Those are two events per pattern over its whole
        /// life, not one utterance per bar.
        /// </para>
        ///
        /// <list type="bullet">
        ///   <item><b>Entering a region</b> — spoken with the edge named, so the direction of travel
        ///         is audible: "start of" walking forward, "end of" walking back.</item>
        ///   <item><b>The resolution bar</b> — the bar that broke the trigger, or the bar the shape
        ///         aged out on. This is what closes the loop on an entry the user already heard.</item>
        ///   <item><b>Everything in between</b> — silence. The pattern has already been described
        ///         and the level is not changing.</item>
        /// </list>
        ///
        /// <para>
        /// A jump (any move of more than one bar, or the first move after a load) cannot be
        /// diffed — there is no adjacent previous bar to have crossed an edge from — so it falls
        /// back to describing what is here, which is exactly what "where am I?" wants.
        /// </para>
        /// </summary>
        // Internal, not private: this is the third defect to land in formation narration, and each
        // one was in how the pieces combine — the pin, the diff, the edge words — rather than in
        // any piece alone. None of it was reachable from a test without standing up the whole
        // navigation stack, which is why the composition kept going unchecked.
        // See ChartPatternPinNarrationTests.
        internal string ChartPatternContext()
        {
            var state = _store.State;
            if (!state.DescribeChartPatterns) { _lastPatternBar = -1; return ""; }

            var data = state.Data;
            int idx = state.CurrentDataIndex;
            if (data == null || idx < 0 || idx >= data.Count) { _lastPatternBar = -1; return ""; }

            var all = _patternCache.For(state.Identity, data);
            int prev = _lastPatternBar;
            _lastPatternBar = idx;

            if (all.Count == 0) return "";

            // Ranked once, then the user's pin (if any) is floated to the front. Everything below
            // reads from this list, so the pin applies identically to the jump path, the entry
            // announcement and the overlap count.
            string chartKey = ChartPatternCache.KeyFor(state.Identity);
            var here = _patternFocus.Apply(chartKey,
                ChartPatternNarrator.ByDominance(ChartPatternNarrator.AtBar(all, idx)).ToList());

            // Jump, or first move: no edge was crossed, so describe where we landed. If that is
            // exactly a formation's first or last bar — which is what the comma and period keys aim
            // at — say which, so a jump reads the same way a step onto the same bar would.
            if (prev < 0 || Math.Abs(idx - prev) != 1)
            {
                if (here.Count == 0) return "";

                var landedOnStart = here.FirstOrDefault(p => p.KnownAtIndex == idx);
                if (landedOnStart != null)
                    return ChartPatternNarrator.DescribeEntry(landedOnStart, idx, SpeechPriceFormatter.FormatPrice)
                         + ChartPatternNarrator.DescribeContainment(landedOnStart, all)
                         + OverlapNote(here.Count);

                var landedOnEnd = here.FirstOrDefault(p => p.ResolvesAt == idx);
                if (landedOnEnd != null)
                {
                    string res = ChartPatternNarrator.DescribeResolution(landedOnEnd, SpeechPriceFormatter.FormatPrice);
                    if (!string.IsNullOrEmpty(res)) return res + OverlapNote(here.Count);
                }

                return ChartPatternNarrator.DescribeMany(here, SpeechPriceFormatter.FormatPrice, max: 1)
                     + OverlapNote(here.Count);
            }

            if (here.Count == 0) return "";

            bool movingRight = idx > prev;

            // Diffed on Key, never on the records themselves. AtBar projects each pattern to the
            // state it held at the requested bar, so the SAME formation is a different record on
            // the bar it resolved — and a record-wise diff would report it as newly entered there,
            // announcing "start of" at the finish line.
            var beforeKeys = ChartPatternNarrator.AtBar(all, prev).Select(p => p.Key).ToHashSet();

            var parts = new List<string>();

            // 1. Regions crossed into on this step. Ranked, and only the leader is described in
            //    full — see ChartPatternNarrator.ByDominance for why overlap is ranked rather than
            //    hidden.
            // A formation's FIRST KNOWABLE BAR always speaks, in either direction of travel.
            //
            // Crossing-in alone is not enough: step right past the start bar and then step back
            // onto it and the diff sees the formation on both bars, so nothing is said — the one
            // bar where the shape actually begins goes silent precisely when the user is going back
            // to re-read it. A bar that announced something once must announce it every time you
            // stand on it, or the chart is not reproducible by ear.
            var entered = _patternFocus.Apply(chartKey,
                here.Where(p => !beforeKeys.Contains(p.Key) || p.KnownAtIndex == idx).ToList());
            if (entered.Count > 0)
            {
                // Containment is appended to the LEADER only. Saying "inside a larger X" after every
                // one of three overlapping shapes would restate the same parent three times.
                parts.Add(ChartPatternNarrator.DescribeEntry(entered[0], idx, SpeechPriceFormatter.FormatPrice)
                        + ChartPatternNarrator.DescribeContainment(entered[0], all));
                if (entered.Count > 1) parts.Add(OverlapNote(entered.Count).TrimStart());
            }

            // 2. Patterns whose story ends exactly here. Only when walking forward: arriving at a
            //    break bar from the right means the user is leaving the pattern, and they were told
            //    the outcome by the "end of" entry a moment ago.
            if (movingRight)
            {
                var enteredKeys = entered.Select(p => p.Key).ToHashSet();
                foreach (var p in here.Where(p => p.ResolvesAt == idx && !enteredKeys.Contains(p.Key)))
                {
                    string res = ChartPatternNarrator.DescribeResolution(p, SpeechPriceFormatter.FormatPrice);
                    if (!string.IsNullOrEmpty(res)) parts.Add(res);
                }
            }

            return string.Join(" ", parts);
        }

        /// <summary>
        /// How the terminal admits that a region satisfies more than one definition at once.
        /// Naming the count rather than reading them all keeps the utterance short while making it
        /// obvious that something was left unsaid — Alt+Shift+D reads the full list.
        /// </summary>
        private static string OverlapNote(int total)
            => total > 1 ? $" Plus {total - 1} more formation{(total == 2 ? "" : "s")} here." : "";

        public void Dispose()
        {
            _subscriptions.Dispose();
        }
    }
}
