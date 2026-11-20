using System.Globalization;
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
        // Optional so existing construction (tests, manual composition) keeps working; DI
        // supplies it. Only consumer is the unhandled-FeedbackType arm in OnFeedbackRequest.
        private readonly ILogger<AccessibilityFeedbackCoordinator>? _logger;
        // Optional so the many existing constructions keep working; DI supplies it. Gives the
        // Shift+F1 summary the selected drawing anchor — the one read-without-move the nudge has.
        private readonly IDrawingInteractionManager? _drawings;
        // Optional for the same reason. Carries the two NARRATION-tab switches that decide
        // whether the forming bar is commented on; the Speech-tab "describe" switches stay on
        // WorkspaceState because the pure formatters need them. Null means "not supplied", and
        // every read below falls back to the SHIPPED default rather than to false — a bare
        // substitute answering false to an opt-OUT setting is how a feature silently inverts.
        private readonly IAppSettings? _appSettings;
        // The bar-to-market-event map. Built once per chart data/setting and reused on every
        // arrow key, exactly as _patternCache does for formations.
        private readonly MarketEvents.IMarketEventBarMapCache _marketEventMap;
        // Optional so the many existing constructions keep working; DI supplies the one
        // sonifier every other sound goes through. Used only for the live-notice bell.
        private readonly Audio.INavigationSonifier? _navigationSonifier;
        private readonly CompositeDisposable _subscriptions = new();

        // The bar-close narrator. Held so the new-bar sentence can be composed INTO its
        // utterance rather than racing it — see OnNewBar.
        private readonly IAutoNarrationService _autoNarration;

        private WorkspaceState _previousState;

        // The formation last announced as FORMING, by Identity rather than Key: a scroll-back
        // prepends bars and shifts every index, and the user must not be told about the same
        // double top again because its array position moved. Reset when a bar closes, the same
        // way the candle pattern memory is.
        private (ChartPatternKind, DateTime, DateTime)? _lastFormingPattern;

        // True from a playback start until the sequencer's first NavigateAction lands. That
        // first move is a jump to the plan's start bar, not a step through time, so it must not
        // produce a landmark — see PlaybackNarration.LandmarkForStep.
        private bool _awaitingFirstPlaybackStep;

        // The bar index of the last SIGNAL utterance playback spoke, or -1 when none has been
        // spoken in this run. Playback signals are rate-limited by bar distance
        // (PlaybackNarration.MinBarsBetweenSignals) rather than by wall clock, because the bar
        // is what the user is listening to: at 4x the tones move four times as fast and the
        // words have to thin out with them. Reset on every playback start, so the first signal
        // of a run is never suppressed by the last signal of the previous one.
        private int _lastPlaybackSignalBar = -1;

        // How rare the rarest marker in that utterance was (PlaybackNarration.FireCount), or
        // int.MaxValue when the utterance carried no signal at all. A RARER signal is allowed
        // through the window the last one opened; see PlaybackSpeechForStep.
        private int _lastPlaybackSignalRarity = int.MaxValue;

        /// <summary>
        /// The plan the current run is playing, captured at the moment playback started.
        ///
        /// <para>
        /// Held rather than re-resolved per step so the words and the tones cannot describe
        /// different series. <c>PlaybackPlan.Resolve</c> reads <c>FocusedSeriesId</c> and
        /// <c>FocusedComponentIndex</c>, and both can move under the user's hands mid-run;
        /// the sequencer is playing what those fields said when Space was pressed, and speech
        /// has to be about the same thing.
        /// </para>
        /// </summary>
        private Audio.PlaybackPlan? _playbackPlan;

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
            // Naming this parameter is what CONSTRUCTS the narrator — nothing else asks the
            // container for it, and a service DI is never asked for is a service that never
            // runs. It is now also stored: the new-bar announcement is handed to it so that
            // one utterance describes a bar close. See OnNewBar.
            IAutoNarrationService autoNarration,
            Trading.IQuickTradeService? quickTrade = null,
            ILogger<AccessibilityFeedbackCoordinator>? logger = null,
            IDrawingInteractionManager? drawings = null,
            IAppSettings? appSettings = null,
            MarketEvents.IMarketEventBarMapCache? marketEventMap = null,
            Audio.INavigationSonifier? navigationSonifier = null)
        {
            _logger = logger;
            _navigationSonifier = navigationSonifier;
            _drawings = drawings;
            _appSettings = appSettings;
            _marketEventMap = marketEventMap ?? new MarketEvents.MarketEventBarMapCache();
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
            _autoNarration = autoNarration;
            _quickTrade = quickTrade as Trading.QuickTradeService;
            _previousState = store.State;

            // OBSERVE THE STORE DIRECTLY
            _subscriptions.Add(store.StateStream.Subscribe(OnStateChanged));

            // Legacy support for specific manual events
            _subscriptions.Add(_eventBus.Subscribe<FeedbackRequestEvent>(OnFeedbackRequest));
            _subscriptions.Add(_eventBus.Subscribe<AnnouncementEvent>(e =>
                _speechRouter.Speak(e.Message, e.Interrupt, e.Channel ?? SpeechChannel.Chart)));
            // Shift+F1. Its own event so that no subscriber has to recognise a sentinel to
            // avoid speaking one — see ContextSummaryRequestEvent.
            _subscriptions.Add(_eventBus.Subscribe<ContextSummaryRequestEvent>(_ => SpeakContextSummary()));

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
            // Live market-event notices. The Event tier is the whole point: Shift+F2 mutes
            // alerts and monitoring, and a countdown before CPI is exactly that kind of news;
            // F2, the chart mute, must not silence it. One bell, shared with the bar bell.
            _subscriptions.Add(_eventBus.Subscribe<MarketEventNoticeEvent>(e =>
            {
                _navigationSonifier?.PlayMarketEventBell(_store.State, e.Importance);
                _speechRouter.Speak(e.Text, interrupt: false, channel: SpeechChannel.Event);
            }));

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
                _speechRouter.Speak(FormatTerminated("cancelled", e.Order), interrupt: false, channel: SpeechChannel.OrderEvent);
            }));
            // Expired is not a cancel (nobody asked) and not a rejection (the venue
            // accepted the order) — a day order at the close, an IOC remainder. The
            // trader's intent lapsed; say so in those words.
            _subscriptions.Add(_eventBus.Subscribe<OrderExpiredEvent>(e =>
            {
                _audioRouter.PlayEarcon(FeedbackType.StateChange, ErrorSeverity.Low);
                _speechRouter.Speak(FormatTerminated("expired", e.Order), interrupt: false, channel: SpeechChannel.OrderEvent);
            }));
            // A replaced order is STILL LIVE under a new id. Saying "cancelled"
            // here tells the trader they are flat while the order rests — they
            // re-enter and are double-sized with the original still working.
            _subscriptions.Add(_eventBus.Subscribe<OrderReplacedEvent>(e =>
            {
                _audioRouter.PlayEarcon(FeedbackType.StateChange, ErrorSeverity.Low);
                _speechRouter.Speak(
                    $"Order replaced for {e.Order.Symbol}. It is still working under a new order id.",
                    interrupt: false, channel: SpeechChannel.OrderEvent);
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
        // Static so OrderEventAnnouncementTests can verify formats directly. PUBLIC rather
        // than internal since 2026-09-06: the headless session announces the same fills with
        // no browser attached, through spd-say/say/SAPI instead of this coordinator's speech
        // router, and it lives in the WebHost assembly. One wording for the fill, whoever is
        // carrying it — a second copy of these sentences is a second place they can drift.

        public static string FormatFill(string prefix, Sdk.Trading.OrderUpdate o)
        {
            string side = o.Side == Sdk.Plugins.OrderSide.Buy ? "bought" : "sold";
            string at = o.FilledPrice > 0 ? $" at {SpeechPriceFormatter.FormatPrice(o.FilledPrice)}" : "";
            // On a closing fill the provider supplies realized P&L — announce it so
            // the trader hears the result without opening the dashboard.
            string pnl = o.RealizedPnL.HasValue
                ? $" {(o.RealizedPnL.Value >= 0 ? "Profit" : "Loss")} {SpeechPriceFormatter.FormatPrice(Math.Abs(o.RealizedPnL.Value))}."
                : "";
            // A fill that carries a reason is a fill NOBODY ASKED FOR — the paper broker's
            // forced liquidation is the one that exists today, and it announced as an ordinary
            // "Order filled. Bought 1 BTC/USD at 200. Loss 100." The trader heard a trade they
            // did not place, worded exactly like one they did, and the fact that their collateral
            // was exhausted was the part left out. FormatTerminated has always spoken Reason;
            // fills dropped it because fills were assumed to be requested.
            string why = string.IsNullOrWhiteSpace(o.Reason) ? "" : " " + o.Reason!.TrimEnd('.') + ".";
            return $"{prefix}. {Capitalize(side)} {FormatQty(o.FilledQuantity)} {o.Symbol}{at}.{pnl}{why}";
        }

        public static string FormatPartialFill(Sdk.Trading.OrderUpdate o)
        {
            string baseMsg = FormatFill("Partial fill", o);
            return o.RemainingQuantity > 0
                ? $"{baseMsg} {FormatQty(o.RemainingQuantity)} remaining."
                : baseMsg;
        }

        /// <summary>
        /// A cancelled/expired order that partially filled first left the trader
        /// with a position. Announcing it as a bare "cancelled" hides that — on
        /// venues that emulate market orders as IOC limits (Gemini) the DEFAULT
        /// order type is the one most likely to partially fill and then cancel,
        /// and MEXC's "partially filled then canceled" status is one terminal
        /// message. So a terminal announcement always speaks the executed part.
        /// </summary>
        public static string FormatTerminated(string what, Sdk.Trading.OrderUpdate o)
        {
            string why = string.IsNullOrWhiteSpace(o.Reason) ? "" : " " + o.Reason.TrimEnd('.') + ".";
            if (o.FilledQuantity > 0)
            {
                string side = o.Side == Sdk.Plugins.OrderSide.Buy ? "bought" : "sold";
                string at = o.FilledPrice > 0 ? $" at {SpeechPriceFormatter.FormatPrice(o.FilledPrice)}" : "";
                return $"Order {what} for {o.Symbol} after a partial fill: {side} {FormatQty(o.FilledQuantity)}{at}.{why}";
            }
            return $"Order {what} for {o.Symbol}.{why}";
        }

        private static string FormatQty(double qty) => qty.ToString("0.########", CultureInfo.InvariantCulture);

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
                // "Chart speech", not "Speech": since 2026-09-30 F2 silences only the chart and its
                // narration, and a dialog still speaks with it off, so "Speech off" would be a lie.
                _speechRouter.Speak(state.IsSpeechEnabled ? "Chart speech on" : "Chart speech off",
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
            // Only the shown type's own settings count: RANGEBAR 500 typed on a Renko chart stores the
            // range first and changes nothing on screen until the type follows.
            bool renkoSettingsChanged = state.ChartStyle.IsBrickStyle()
                && _previousState.ChartStyle == state.ChartStyle
                && (!Charts.BuiltCharts.SameFor(state.ChartStyle, state.Renko, _previousState.Renko)
                    || state.BrickBoxSize != _previousState.BrickBoxSize);
            // A new base on a Baseline chart is a change to what is drawn, said like one.
            bool baselineChanged = state.ChartStyle == PriceChartStyle.Baseline
                && _previousState.ChartStyle == PriceChartStyle.Baseline
                && state.Renko.BaselinePrice != _previousState.Renko.BaselinePrice;
            if (state.IsHeikinAshi != _previousState.IsHeikinAshi
                || state.ChartStyle != _previousState.ChartStyle
                || renkoSettingsChanged || baselineChanged)
            {
                // One sentence, on the Interface channel (this is a chart-type choice, not a
                // reading). Heikin-Ashi going ON is still the whole existing warning when the
                // user comes from Line or Area, because that action also puts the chart back on
                // candles — two visible changes, so the sentence names both.
                //
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
                string msg;
                if (state.IsHeikinAshi && !_previousState.IsHeikinAshi)
                {
                    string ha = state.DescribeChartPatterns
                        ? "Heikin-Ashi candles. Chart formations are still read from standard candles."
                        : "Heikin-Ashi candles";
                    msg = _previousState.ChartStyle is PriceChartStyle.Line or PriceChartStyle.Area
                            || Charts.ChartTypeWords.ChangeSentence(_previousState) != null
                        ? ha.TrimEnd('.') + ". Candle chart."
                        : state.DescribeChartPatterns
                            ? "Heikin Ashi chart. Chart formations are still read from standard candles."
                            : "Heikin Ashi chart.";
                }
                else
                {
                    msg = state.ChartStyle switch
                    {
                        PriceChartStyle.Line => "Line chart.",
                        PriceChartStyle.Area => "Area chart.",
                        _ when state.ChartStyle.IsBrickStyle() => BrickChartSpeech.ChangeSentence(state),
                        _ when Charts.ChartTypeWords.ChangeSentence(state) is { } typeSentence => typeSentence,
                        _ => "Candle chart.",
                    };
                }
                // The change moved the focus off the Price line, which Line and Area do not draw
                // (ChartTypeFocus): say where the arrows now are, in the price series' new shape
                // (PriceSeriesShape), so the next reading is not a surprise.
                if (state.FocusedSeriesId != _previousState.FocusedSeriesId)
                {
                    if (state.FocusedSeriesId == CoreSeriesIds.Price) msg += " On the price line.";
                    else if (state.FocusedSeriesId == CoreSeriesIds.Candles)
                        msg += state.ChartStyle switch
                        {
                            PriceChartStyle.Line or PriceChartStyle.Area => " On the close line.",
                            _ when Charts.ChartTypeWords.LandingWords(state.ChartStyle) is { } landing => landing,
                            _ when state.ChartStyle.IsBrickStyle() => $" On the {Charts.BuiltCharts.Noun(state.ChartStyle, 2)}.",
                            _ => " On the candles.",
                        };
                }
                _speechRouter.Speak(msg, interrupt: true, channel: SpeechChannel.Interface);
            }
            // ── The price scale is ONE choice: Regular, Logarithmic, Percent, Indexed ──────
            //
            // The select and the Go bar write both fields, so this speaks the pair as one
            // change. A log turn-on from Percent/Indexed writes Regular as well; saying
            // "Log scale. Regular scale." would name a second change the user did not make, so
            // log-on wins the sentence and the select shows the rest. Log-off is the Regular
            // entry, which replaces the old "Linear scale".
            if (state.ScaleMode != _previousState.ScaleMode || state.IsLogScale != _previousState.IsLogScale)
            {
                string? sentence = null;
                if (state.IsLogScale && !_previousState.IsLogScale)
                {
                    sentence = "Log scale.";
                }
                else if (state.ScaleMode != _previousState.ScaleMode)
                {
                    double baseValue = ChartMath.ScaleBase(state);
                    bool usable = ChartMath.BaseUsable(baseValue);
                    string price = usable ? SpeechPriceFormatter.FormatPrice(baseValue) : "";
                    sentence = state.ScaleMode switch
                    {
                        PriceScaleMode.Percent when usable =>
                            $"Percent scale. Zero is {price}, the first close on screen.",
                        PriceScaleMode.IndexedTo100 when usable =>
                            $"Indexed to 100. 100 is {price}, the first close on screen.",
                        PriceScaleMode.Percent =>
                            "Percent scale needs a positive first close; showing the regular scale.",
                        PriceScaleMode.IndexedTo100 =>
                            "Indexed to 100 needs a positive first close; showing the regular scale.",
                        _ => "Regular scale.",
                    };

                    // A restored workspace may select Percent before its first load, when there
                    // is no close to be the zero yet. That is not a failed choice and must not
                    // be announced as one; the chart draws Regular until the data arrives.
                    if (state.ScaleMode != PriceScaleMode.Regular && !usable
                        && state.Data is not { Count: > 0 })
                        sentence = null;
                }
                else if (_previousState.IsLogScale && !state.IsLogScale)
                {
                    // Log off is the Regular entry; the mode did not change because it can only
                    // have been Regular while the log flag was on.
                    sentence = "Regular scale.";
                }

                if (sentence != null)
                    _speechRouter.Speak(sentence, interrupt: true, channel: SpeechChannel.Interface);
            }

            // PLAYBACK. Every word the user hears about playback is spoken from here, and it has
            // to sit ABOVE the gate below because the gate is exactly what playback engages.
            //
            // Until 2026-09-02 the gate's comment read "The PlaybackOrchestrator handles its own
            // sonification/speech" and nothing did: the orchestrator owns tones only — it has no
            // speech router and no event bus — and no other class spoke a start, pause, resume,
            // stop or finish. Space produced tones with no words, Shift+= mid-playback changed
            // the speed silently (the announcement was below the gate, so it worked only when it
            // was useless), and the last bar ending sounded like a crash. PlaybackNarration holds
            // the sentences; this block decides when each is due.
            //
            // Not on a tab switch. Two tabs can differ in every one of these flags, and the tab
            // label is already being announced by WorkspaceStore.Dispatch; on the web head only
            // the last write to the live region in a render batch survives, so "Playback speed:
            // 2.0x" or "Playback stopped" here would eat the label. The sequencer does stop when
            // the incoming tab is not playing (SonificationManager sees the same transition), and
            // the user hears the tones end and the tab name — which is the whole story.
            bool isTabSwitch    = state.ActiveTabIndex != _previousState.ActiveTabIndex;
            bool playingToggled = state.IsPlaying != _previousState.IsPlaying;
            bool scopeChanged   = state.PlaybackScope != _previousState.PlaybackScope;
            bool pausedToggled  = state.IsPaused != _previousState.IsPaused;

            if (isTabSwitch)
            {
                // First arm on purpose: it has to win over every playback transition below,
                // including the speed line. Proven by sabotage — an unconditional speed line
                // or this arm demoted below the chain both speak over the tab label.
                _awaitingFirstPlaybackStep = false;
            }
            else if (state.PlaybackSpeed != _previousState.PlaybackSpeed)
            {
                _speechRouter.Speak(PlaybackNarration.SpeedText(state.PlaybackSpeed));
                // Persisting it is NOT this class's job: PreferencePersistenceService observes
                // the store and writes every preference back, debounced, which is what keeps
                // Shift+= held down from being one file write per keypress.
            }
            else if (state.IsPlaying && (playingToggled || scopeChanged))
            {
                // Announce the plan the orchestrator is about to play, from the same resolver it
                // reads, so the sentence and the sound cannot name different series or bars.
                var plan = Audio.PlaybackPlan.Resolve(state, state.PlaybackScope);
                if (plan.IsPlayable)
                    _speechRouter.Speak(PlaybackNarration.StartText(state, plan), interrupt: true);
                else
                    // The dispatcher refuses before dispatching, so this is for any OTHER
                    // caller of SetPlaybackAction(true) — which would otherwise reproduce the
                    // silent "playing" state exactly.
                    _speechRouter.Speak(plan.RefusalReason ?? Audio.PlaybackPlan.NoSeriesReason, interrupt: true);
                // Only a cursor that is NOT already on the start bar has a jump ahead of it.
                // Series and component scope start AT the cursor, so their first NavigateAction
                // moves nothing and the first real step must be allowed to land a landmark.
                _awaitingFirstPlaybackStep = state.CurrentDataIndex != plan.StartIndex;
                _lastPlaybackSignalBar = -1;
                _lastPlaybackSignalRarity = int.MaxValue;
                _playbackPlan = plan.IsPlayable ? plan : null;
            }
            else if (playingToggled)
            {
                // SetPlaybackAction(false) clears IsPaused in the same reduction, so a stop
                // from a paused state is one sentence, not "Resumed. Playback stopped".
                //
                // A user's stop answers a keypress and interrupts. The sequencer's own finish
                // answers nothing: it must not clip a short plan's start sentence (a two-bar
                // component plays out in 200 ms) or the last landmark, so it queues — and it
                // carries the boundary earcon, because with F2 on the sentence is muted and
                // the end of the tone stream would again be indistinguishable from a crash.
                bool finished = PlaybackNarration.ReachedEnd(state);
                if (finished) _audioRouter.PlayEarcon(FeedbackType.Boundary);
                _speechRouter.Speak(PlaybackNarration.EndText(state), interrupt: !finished);
            }
            else if (state.IsPlaying && pausedToggled)
            {
                _speechRouter.Speak(state.IsPaused ? PlaybackNarration.PauseText(state) : PlaybackNarration.ResumeText,
                    interrupt: true);
            }
            else
            {
                // Non-interrupting: a landmark must never clip the one before it, and it must
                // never clip a speed or pause confirmation the user just asked for.
                bool indexMoved = state.CurrentDataIndex != _previousState.CurrentDataIndex;
                bool isFirstStep = _awaitingFirstPlaybackStep;
                string? landmark = PlaybackNarration.LandmarkForStep(_previousState, state, isFirstStep);
                if (state.IsPlaying && indexMoved) _awaitingFirstPlaybackStep = false;

                // ONE UTTERANCE PER STEP. The landmark, the signals the narrated series printed
                // on this bar, and any chart pattern that resolved on it are composed and spoken
                // once — the live-region rule that the bar-close narrator was rebuilt around on
                // 2026-09-02, and it bites harder here: on the web head only the last write to
                // the region in a render batch survives, and playback writes ten times a second.
                //
                // Landmark FIRST, against this file's usual most-consequential-first ordering,
                // and the reason is that it is not a competing claim — it is the WHEN of the
                // clause behind it. "June 3. Cipher: buy signal." reads as one fact; reversed it
                // reads as two, and the landmark's whole job is to timestamp the tones.
                string? spoken = PlaybackSpeechForStep(state, landmark, indexMoved && !isFirstStep);
                if (spoken != null)
                    _speechRouter.Speak(spoken, interrupt: false);
            }

            // 1. GATING: everything below is navigation and viewport feedback for a cursor the
            // USER is moving. During playback the sequencer moves the cursor ten times a second,
            // and a viewport description or a mute confirmation on every tick would bury the
            // tones. Playback's own speech is the block above; the toggle confirmations are
            // above that.
            if (state.IsPlaying)
            {
                _previousState = state;
                return;
            }

            // TAB SWITCH GATE: suppress secondary viewport/status announcements on the single
            // state transition where ActiveTabIndex changes. The tab label is already announced
            // by WorkspaceStore.Dispatch via AnnouncementEvent. Letting viewport/initStatus
            // announcements race with that produces "loading history" / "loading link" speech.
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

            // 2. STATUS ANNOUNCEMENTS (Zoom, Pan, Mute, Hide). Playback speed is in the playback
            // block above the gate — it was here once, which is why Shift+= only spoke while
            // nothing was playing.
            if (state.PanningGranularity != _previousState.PanningGranularity)
            {
                _speechRouter.Speak($"Panning step: {state.PanningGranularity} percent");
            }

            // VIEWPORT ANNOUNCEMENT POLICY:
            // Zoom (ViewportLength changes) → always announce.
            // Pan  (ViewportStartIndex changes WITHOUT cursor moving) → announce.
            // Jump commands (NAV_LIVE, NAV_HOME, NAV_END): cursor AND viewport both move together.
            //   → suppress the viewport description; only the bar at the new position is spoken.
            // Live data (a bar appended, or older history prepended): the reducer slides the
            //   window so the live edge stays in view, or so the user stays on the same bar.
            //   → the feed moved the chart, not the user. Say nothing about the range; the bar
            //   close speaks for itself. Cody, 2026-09-05: "when a new bar announcement comes
            //   in, I don't hear the viewport announcement too". NewBarViewportSilenceTests.
            bool isCursorJump = indexChanged && viewportStartChanged && !viewportLengthChanged;
            bool dataArrived  = (state.Data?.Count ?? 0) != (_previousState.Data?.Count ?? 0);
            bool shouldAnnounceViewport = viewportLengthChanged
                                       || (viewportStartChanged && !isCursorJump && !dataArrived);

            if (shouldAnnounceViewport && state.Data != null && state.Data.Any())
            {
                int startIdx = Math.Clamp(state.ViewportStartIndex, 0, state.Data.Count - 1);
                int endIdx   = Math.Clamp(state.ViewportStartIndex + state.ViewportLength - 1, 0, state.Data.Count - 1);
                DateTime rangeStart = BrickChartSpeech.TryGetBrick(state, startIdx, out _, out var firstSpan)
                    ? firstSpan.Start : state.Data[startIdx].Date;
                DateTime rangeEnd = BrickChartSpeech.TryGetBrick(state, endIdx, out _, out var lastSpan)
                    ? lastSpan.End : state.Data[endIdx].Date;
                string msg   = _formatter.FormatViewportDescription(state.ViewportLength,
                    rangeStart, rangeEnd, PlaybackNarration.BarSeconds(state));
                _speechRouter.Speak(msg, true);
            }

            // 3. SERIES STATE (Mute/Hide)
            var currentFocusedSeries = state.ActiveSeries.FirstOrDefault(s => s.Id == state.FocusedSeriesId);
            var prevFocusedSeries = _previousState.ActiveSeries.FirstOrDefault(s => s.Id == _previousState.FocusedSeriesId);

            if (currentFocusedSeries != null && prevFocusedSeries != null && currentFocusedSeries.Id == prevFocusedSeries.Id)
            {
                // The OTHER flag rides along, because the two fail the same way from the user's
                // side — the series makes no sound — and are cleared by different keys. Saying
                // "active" about a series that is still hidden is the terminal reporting a state
                // it is not in. See VisibilityStateSpeech.
                if (currentFocusedSeries.IsMuted != prevFocusedSeries.IsMuted)
                {
                    _speechRouter.Speak($"{currentFocusedSeries.FriendlyName} "
                        + $"{(currentFocusedSeries.IsMuted ? "muted" : "active")}"
                        + VisibilityStateSpeech.OtherFlagClause(!currentFocusedSeries.IsVisible, "hidden"));
                }
                if (currentFocusedSeries.IsVisible != prevFocusedSeries.IsVisible)
                {
                    _speechRouter.Speak($"{currentFocusedSeries.FriendlyName} "
                        + $"{(currentFocusedSeries.IsVisible ? "visible" : "hidden")}"
                        + VisibilityStateSpeech.OtherFlagClause(currentFocusedSeries.IsMuted, "muted"));
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
                // Critical channel and an earcon, like every other failure in this class.
                //
                // This was the one failure here left on the default Manual channel, which F2
                // silences — so a user who had muted manual speech watched a chart fail to load
                // in complete silence, with no earcon either, while "Chart ready." on the line
                // above would have been just as silent. A load failure is not a courtesy
                // announcement; FeedbackRouters' contract forbids a silent failure outright.
                var failedId = state.Identity;
                string failMsg = !string.IsNullOrEmpty(failedId.Symbol)
                    ? $"{failedId.Symbol} on {failedId.Provider} failed to load."
                    : "Chart failed to load.";
                _audioRouter.PlayEarcon(FeedbackType.Error, ErrorSeverity.High);
                _speechRouter.Speak(failMsg, interrupt: true, channel: SpeechChannel.Critical);
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

            // A brick chart announces the brick that was appended, not a time bar's open.
            // Nothing is said when a feed bar closes without completing a brick: the store
            // publishes no bar event for that at all on a brick chart (the brick count did
            // not move), and the intra-bar candle commentary is suppressed below.
            if (state.ChartStyle.IsBrickStyle())
            {
                if (state.Data == null || state.Data.Count == 0) return;
                int last = state.Data.Count - 1;
                var brick = state.Data[last];
                var span = last < state.BrickSpans.Length ? state.BrickSpans[last] : default;
                if (last >= state.BrickSpans.Length) return;

                _earconService.PlayNewBar();
                string? signal = state.ChartStyle == PriceChartStyle.PointAndFigure
                    ? PnfSignalNews(state.Data, last) : null;
                _speechRouter.Speak(
                    $"New {(span.Up ? "up" : "down")} {Charts.BuiltCharts.Noun(state.ChartStyle)}, "
                    + (state.ChartStyle == PriceChartStyle.Kagi
                        ? Charts.KagiLines.Words(Charts.KagiLines.At(state.Data, last)) + ", "
                        : state.ChartStyle == PriceChartStyle.PointAndFigure
                        ? Charts.PointFigureColumns.Words(brick, state.BrickBoxSize) + ", "
                        : "")
                    + $"{SpeechPriceFormatter.FormatPrice(brick.Open)} to "
                    + $"{SpeechPriceFormatter.FormatPrice(brick.Close)}."
                    + (signal == null ? "" : " " + signal + "."),
                    interrupt: false, channel: SpeechChannel.Narration);
                if (state.ChartStyle == PriceChartStyle.PointAndFigure)
                {
                    // The column's boxes are said; growth from here says only more.
                    _pnfSaidFor = brick.Date;
                    _pnfSaidBoxes = Charts.PointFigureColumns.Boxes(brick, state.BrickBoxSize);
                }
                return;
            }

            // WHICH BAR JUST CLOSED. The store commits the state and THEN publishes this event,
            // so by the time it arrives `Data[^1]` is the bar that just OPENED and the closed
            // bar is `Data[^2]`. This method used to read `prev = Data[^2]`, which handed the
            // candle analyser the closed bar as its own predecessor: an engulfing pattern was
            // tested against itself and the trend context ran one bar into the future. Locate
            // the closed bar by its date and count back from there.
            //
            // The context assembly itself now lives in CandlePatternSpeech, which is what makes
            // this route and the three that read history agree bar for bar. It also picks up the
            // Heikin-Ashi transform: with HA on, the candle that just closed on screen is the HA
            // candle, and naming the raw one would describe a shape nobody is looking at.
            var data = state.Data;
            int closedIndex = ClosedBarIndex(data, e.ClosedBar);
            var analysis = CandlePatternSpeech.AnalyzeAt(
                _patternAnalyzer, data, closedIndex, state.IsHeikinAshi,
                current: closedIndex >= 0 ? null : e.ClosedBar);

            // Gated since 2026-09-04. The Narration tab has always described this announcement as
            // "the closing price of each bar as it finishes, WITH ITS CANDLE PATTERN", and there
            // was no switch that could make the second half untrue — the clause was unconditional.
            // A promise in help text is a contract; either the text drops the clause or the clause
            // gets a switch, and Cody chose the switch.
            string patternSuffix = state.DescribeCandlePatterns
                ? CandlePatternSpeech.Suffix(analysis, finalized: true)
                : "";
            // ── WHICH BAR JUST CLOSED, SAID OUT LOUD ───────────────────────────────────
            //
            // Cody, 2026-09-05: "if I'm on a 1 minute chart and hear a new bar… the timestamp of
            // the candle closing should also be announced." Without it a run of these in the
            // journal is a column of prices with nothing to say which minute each belonged to,
            // and on a fast chart there is no way to tell a fresh announcement from one that
            // arrived while you were reading something else.
            //
            // The unit follows the chart: the time of day on an intraday one, the date on a
            // daily or coarser one, where every bar would otherwise be "00:00".
            int barSeconds = PlaybackNarration.BarSeconds(state);
            string when = SpeechTimeFormatter.FormatBarWhen(e.ClosedBar.Date, barSeconds);

            // The close and open AS DRAWN: on a Heikin-Ashi chart the Heikin-Ashi close of the bar
            // that closed and the Heikin-Ashi open of the one that opened, as TradingView quotes
            // them (Cody, 2026-10-22: "if ha is on and a bar closes, speak that close").
            var (closedBar, newBar) = AsDrawn(data, closedIndex, e.ClosedBar, e.NewBar, state.IsHeikinAshi);
            string closedMsg = $"Close {SpeechPriceFormatter.FormatPrice(closedBar.Close)}{when}{patternSuffix}.";
            // The bar that opened says when it opened, on the same clock as the close (Cody,
            // 2026-11-08: "New bar: open [price] at [timestamp]").
            string openMsg   = $"New bar: Open {SpeechPriceFormatter.FormatPrice(newBar.Open)}"
                             + SpeechTimeFormatter.FormatBarWhen(e.NewBar.Date, barSeconds);

            // A CHART pattern whose story ends on the bar that just closed — a neckline closed
            // through, or a triangle that aged out with its boundary intact — is the event a
            // trader watching a formation is waiting for, and the bar close is the moment it
            // becomes a fact. Same sentence the arrow keys speak on that bar, so the live
            // announcement and a later re-read of the chart agree word for word. Between the
            // close and the open: it is about the bar that closed.
            string outcomes = ChartPatternOutcomesAt(state, data, closedIndex);

            _earconService.PlayNewBar();

            // ── ONE UTTERANCE DESCRIBES A BAR CLOSE ────────────────────────────────────
            //
            // Reported by Cody, 2026-09-05: "even if narration is on for several series, only
            // the newest candle is actually read out on the print of a new bar." Two services
            // were speaking about the same moment — this one the instant the store commits the
            // bar, and AutoNarrationService on the RedrawEvent that follows the recalculation.
            // On the web head speech is an ARIA live region and the second write replaces the
            // first, so the user hears one of the two; which one depends on render batching.
            // This is the same defect NavigationFeedbackManager's "one utterance per bar"
            // composition exists for, on the other route.
            //
            // The narrator is the one that can compose, because it speaks LAST: the indicator
            // values for the closed bar are not recalculated until after this event. So the
            // sentence is handed over when a scan is coming and spoken here when it is not.
            //
            // The order is the bar that closed, everything about it, and only then the bar that
            // opened (Cody, 2026-11-02: "when a bar closes, I should hear the close and the
            // indicator narration ladder. Then the open of the newest bar should be read as
            // well"). The narrator puts its findings between the two halves.
            string closing = $"{closedMsg} {outcomes}".TrimEnd();
            if (_autoNarration.WillNarrateBarClose())
                _autoNarration.DeferBarCloseSentence(closing, openMsg);
            else
                _speechRouter.Speak($"{closing} {openMsg}", interrupt: false, channel: SpeechChannel.Narration);

            // Reset intra-bar debounce for the new bar.
            _lastAnnouncedPattern = CandlePattern.None;
            _lastAnnouncedType    = CandleType.Normal;
            // NOT reset: the forming-formation memory is per FORMATION, not per bar. A double
            // top spans dozens of bars, and clearing this at every close would re-announce the
            // same shape on every one of them — the noise that makes a feature get switched off.
            // It clears when the formation resolves or ages out of the cache.
            _lastPatternAnnouncement = DateTime.UtcNow;
        }

        /// <summary>
        /// Called on every intra-bar tick. Runs pattern recognition and announces
        /// when a new pattern is detected, subject to a debounce window.
        /// </summary>
        /// <summary>
        /// Whether the forming CANDLE pattern is commented on. Default ON — see
        /// <see cref="SettingsKeys.NarrateFormingCandlePatterns"/>.
        /// </summary>
        private bool NarrateFormingCandles => _appSettings?.NarrateFormingCandlePatterns ?? true;

        /// <summary>
        /// Whether a forming chart FORMATION is announced on the live bar. Default OFF.
        /// </summary>
        private bool NarrateFormingCharts => _appSettings?.NarrateFormingChartPatterns ?? false;

        /// <summary>The Kagi line whose turn was last said, by its date, so a price that wavers
        /// across the shoulder says it once.</summary>
        private DateTime _kagiTurnSaidFor;

        /// <summary>The point and figure column whose boxes were last said, by its date, and the
        /// most boxes said for it, so a column is news once per new box.</summary>
        private DateTime _pnfSaidFor;
        private int _pnfSaidBoxes;

        /// <summary>The point and figure column whose buy or sell signal was last said, by its
        /// date: a column gives its signal once, however its forming bar wavers.</summary>
        private DateTime _pnfSignalSaidFor;

        /// <summary>"Buy signal, above the last X top at 105.00" the first time the column at
        /// <paramref name="index"/> is heard past it; null when it gives none, or gave it already.</summary>
        private string? PnfSignalNews(IReadOnlyList<Ohlcv>? columns, int index)
        {
            string? words = Charts.PointFigureColumns.SignalWords(columns, index);
            if (words == null || columns![index].Date == _pnfSignalSaidFor) return null;
            _pnfSignalSaidFor = columns[index].Date;
            return words;
        }

        private void OnIntraBarUpdate(IntraBarUpdateEvent e)
        {
            var state = _store.State;
            if (!state.AnnounceNewBars) return; // speech mute handled by the Event channel
            if (state.IsPlaying) return;

            // A Kagi line grows in place; the one thing worth saying while it does is the moment it
            // turns thick or thin, which is the chart's own signal. Once per line.
            if (state.ChartStyle == PriceChartStyle.Kagi)
            {
                string? turn = BrickChartSpeech.KagiTurnNews(state.Data, e.PreviousBar);
                var line = state.Data is { Count: > 0 } d ? d[^1].Date : default;
                if (turn != null && line != _kagiTurnSaidFor)
                {
                    _kagiTurnSaidFor = line;
                    _speechRouter.Speak(turn, interrupt: false, channel: SpeechChannel.Narration);
                }
                return;
            }

            // A point and figure column grows in place, box by box; each new box is news, as a
            // new Renko brick is. A box taken back and given again by the forming bar is not.
            if (state.ChartStyle == PriceChartStyle.PointAndFigure)
            {
                var column = state.Data is { Count: > 0 } d ? d[^1].Date : default;
                int said = column == _pnfSaidFor ? _pnfSaidBoxes : 0;
                string? grew = BrickChartSpeech.PnfGrowNews(state.Data, e.PreviousBar, state.BrickBoxSize, said);
                if (grew != null)
                {
                    _pnfSaidFor = column;
                    _pnfSaidBoxes = Charts.PointFigureColumns.Boxes(state.Data![^1], state.BrickBoxSize);
                    string? signal = PnfSignalNews(state.Data, state.Data.Count - 1);
                    _speechRouter.Speak(signal == null ? grew : $"{grew} {signal}.",
                        interrupt: false, channel: SpeechChannel.Narration);
                }
                return;
            }

            // The forming-bar commentary is about candle anatomy; a brick has none.
            if (state.ChartStyle.IsBrickStyle()) return;

            // ── ABILITY vs OCCASION (Cody, 2026-09-11) ────────────────────────────────
            //
            // Until now this method was gated on DescribeCandlePatterns alone, which is the
            // SPEECH-tab switch governing whether a candle pattern is ever NAMED — on the arrow
            // keys, in the bar-close suffix, in Alt+Shift+D. So a user who wanted pattern names
            // while arrowing over the chart was also signed up for a running commentary on the
            // bar currently forming, with no way to separate them. The Narration tab exists for
            // exactly this distinction: the Speech tab says WHAT CAN BE SAID, Narration says
            // WHEN THE TERMINAL SPEAKS UNPROMPTED.
            //
            // Both are required. Narrating a pattern the user has told us not to name would be
            // the switch lying in the other direction.
            bool candles = state.DescribeCandlePatterns && NarrateFormingCandles;
            bool charts  = state.DescribeChartPatterns  && NarrateFormingCharts;
            if (!candles && !charts) return;

            // Per-user feedback (2026-04-09): intra-bar pattern updates were firing in
            // real time regardless of auto-narration state, which breaks the convention
            // that continuous verbal output requires the user to have explicitly
            // enabled narration. The finalized-bar announcement (OnNewBar) stays gated
            // only on AnnounceNewBars — that's a single event-at-close notification,
            // not continuous narration. Intra-bar "still forming" updates, however,
            // are continuous and must respect the Candles series' IsAutoNarrated flag.
            var candleSeries = state.ActiveSeries.FirstOrDefault(s => s.Id == state.PrimarySeriesId)
                            ?? state.ActiveSeries.FirstOrDefault(s => s.Id == CoreSeriesIds.Candles);
            if (candleSeries == null || !candleSeries.IsAutoNarrated) return;

            // Debounce: don't announce more often than PatternDebounce. Shared by both halves on
            // purpose — they describe the same forming bar, and two independent debounces would
            // let the pair interleave into a commentary twice as dense as either switch promised.
            if (DateTime.UtcNow - _lastPatternAnnouncement < PatternDebounce) return;

            // The forming FORMATION, and it is the half that was missing. Candle patterns on the
            // live bar have been spoken since 2026-04; chart formations were spoken only once
            // they RESOLVED (OnNewBar's outcome clause) or when the user arrowed onto them. So
            // the two halves of "what shape is this" behaved differently for no reason the user
            // could see. Cody, 2026-09-11: "in progress chart patterns should also be spoken
            // like in progress candle patterns".
            if (charts && AnnounceFormingChartPattern(state)) return;
            if (!candles) return;

            // The forming bar is not in state.Data yet, so the trend context is the stored history
            // with the live bar appended — otherwise hammer-vs-hanging-man on the bar being watched
            // in real time is the one place that still guesses.
            //
            // e.PreviousBar and e.TwoBarsAgo are deliberately NOT passed any more, and this is a
            // fix rather than a tidy-up. The store replaces the live bar in place, so on an
            // intra-bar tick `PreviousBar` is the PREVIOUS SNAPSHOT OF THE FORMING BAR — same
            // date, smaller body — and `TwoBarsAgo` is the bar that actually precedes it. The
            // analyser was therefore testing an engulfing pattern against an earlier version of
            // the very bar it was classifying, which the growing body engulfs by construction.
            // AnalyzeForming reads the stored series, which is what every other route reads.
            var analysis = CandlePatternSpeech.AnalyzeForming(
                _patternAnalyzer, state.Data, e.CurrentBar, state.IsHeikinAshi);

            bool patternChanged = analysis.Pattern != _lastAnnouncedPattern
                               || analysis.Type    != _lastAnnouncedType;

            // Only announce non-trivial, non-Normal forming patterns. A bar with no movement yet
            // is every bar's first tick, not a shape.
            bool isInteresting = (analysis.Pattern != CandlePattern.None
                              || analysis.Type != CandleType.Normal)
                              && analysis.Type != CandleType.NoMovement;

            if (patternChanged && isInteresting)
            {
                string msg = CandlePatternSpeech.Forming(analysis);
                if (!string.IsNullOrEmpty(msg))
                {
                    _speechRouter.Speak(msg, interrupt: false, channel: SpeechChannel.Narration);
                    _lastAnnouncedPattern    = analysis.Pattern;
                    _lastAnnouncedType       = analysis.Type;
                    _lastPatternAnnouncement = DateTime.UtcNow;
                }
            }
        }

        /// <summary>
        /// The index of the bar that just closed: the bar carrying its date, searched from the
        /// live edge backwards. Falls back to the last bar when the store has not appended the
        /// new one (a caller publishing the event ahead of the commit), and to -1 with no data.
        /// </summary>
        /// <summary>
        /// The bar that closed and the bar that opened as the chart draws them. Falls back to the
        /// event's own bars when the closed bar is not the one before the newest — a bar close the
        /// store has already moved past, where there is nothing to transform against.
        /// </summary>
        internal static (Ohlcv Closed, Ohlcv Opened) AsDrawn(
            IReadOnlyList<Ohlcv>? data, int closedIndex, Ohlcv closed, Ohlcv opened, bool isHeikinAshi)
        {
            if (!isHeikinAshi || data == null || closedIndex < 0 || closedIndex + 1 >= data.Count
                || data[closedIndex + 1].Date != opened.Date)
                return (closed, opened);
            var drawn = ChartMath.BarsAsDrawn(data, closedIndex + 1, 2, isHeikinAshi);
            return drawn.Count == 2 ? (drawn[0], drawn[1]) : (closed, opened);
        }

        internal static int ClosedBarIndex(IReadOnlyList<Ohlcv>? data, Ohlcv closed)
        {
            if (data == null || data.Count == 0) return -1;
            for (int i = data.Count - 1; i >= Math.Max(0, data.Count - 3); i--)
                if (data[i].Date == closed.Date) return i;
            return data.Count - 1;
        }

        /// <summary>
        /// Announces a chart FORMATION that has become knowable on the live bar, once per
        /// formation. Returns true when something was said.
        ///
        /// <para>
        /// <b>The missing half.</b> A candle pattern on the forming bar has been spoken since
        /// 2026-04. A chart formation was spoken when the user ARROWED onto it, and again when it
        /// RESOLVED at a bar close — but never while it was building, which is the moment a
        /// trader watching for a double top actually wants. Cody, 2026-09-11.
        /// </para>
        ///
        /// <para>
        /// <b>Three rules it inherits rather than invents.</b> <c>AsOf</c> projects the formation
        /// back to what was knowable AT this bar, so a shape whose eventual break is already in
        /// the record does not announce that break before it happens — the no-lookahead rule.
        /// <c>ByDominance</c> and a cap of one pick which formation speaks when several overlap,
        /// the same ranking the arrow keys use; one rather than the navigation route's two,
        /// because this arrives unbidden while the user is doing something else.
        /// <c>Identity</c> rather than <c>Key</c> is what makes "have I said this already"
        /// survive a scroll-back that shifts every bar index.
        /// </para>
        ///
        /// <para>
        /// It speaks only formations that are still FORMING. One that has already resolved is the
        /// bar-close route's story, and saying it here too would be the doubling every one of
        /// these routes has had to be taught not to do.
        /// </para>
        /// </summary>
        private bool AnnounceFormingChartPattern(WorkspaceState state)
        {
            var data = state.Data;
            if (data == null || data.Count == 0) return false;

            // The live bar is the last one the store holds; the formation is described as it
            // stands there.
            int liveIndex = data.Count - 1;

            IReadOnlyList<ChartPattern> all;
            try { all = _patternCache.For(state.Identity, data); }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Forming chart-pattern scan failed.");
                return false;
            }
            if (all.Count == 0) return false;

            var candidate = ChartPatternNarrator.ByDominance(
                    all.Where(p => p.KnownAtIndex <= liveIndex && liveIndex < p.ResolvesAt))
                .Select(p => ChartPatternNarrator.AsOf(p, liveIndex))
                .FirstOrDefault(p => p.State == ChartPatternState.Forming);

            if (candidate == null) return false;
            if (_lastFormingPattern == candidate.Identity) return false;

            string sentence = ChartPatternNarrator.DescribeEntry(
                candidate, liveIndex, SpeechPriceFormatter.FormatPrice);
            if (string.IsNullOrWhiteSpace(sentence)) return false;

            _lastFormingPattern = candidate.Identity;
            _lastPatternAnnouncement = DateTime.UtcNow;
            _speechRouter.Speak(sentence, interrupt: false, channel: SpeechChannel.Narration);
            return true;
        }

        /// <summary>
        /// The chart patterns that resolved on <paramref name="closedIndex"/>, as the outcome
        /// sentences the navigation readback uses, each followed by a space; "" when there are
        /// none or the user has pattern descriptions off. At most two, most dominant first —
        /// the same cap the arrow keys apply, for the same reason.
        /// </summary>
        private string ChartPatternOutcomesAt(WorkspaceState state, IReadOnlyList<Ohlcv>? data, int closedIndex)
        {
            if (!state.DescribeChartPatterns || data == null || closedIndex < 0) return "";
            var all = _patternCache.For(state.Identity, data);
            if (all.Count == 0) return "";

            var sb = new System.Text.StringBuilder();
            foreach (var p in ChartPatternNarrator.ByDominance(all.Where(p => p.ResolvesAt == closedIndex)).Take(2))
            {
                string res = ChartPatternNarrator.DescribeResolution(p, SpeechPriceFormatter.FormatPrice, place: "on this close");
                if (!string.IsNullOrEmpty(res)) sb.Append(res).Append(' ');
            }
            return sb.ToString();
        }

        /// <summary>
        /// The one sentence playback speaks for the step it has just taken, or null for silence.
        ///
        /// <para>
        /// Three sources, composed rather than spoken separately: the time landmark, the discrete
        /// signals the user's narrated series printed on this bar, and any chart pattern that
        /// resolved on it. The landmark's own cadence is structural — the unit is chosen coarse
        /// enough to space them out (<c>PlaybackNarration.UnitFor</c>) — so only the other two
        /// need the explicit rate limit, and they share ONE window: two utterances 100 ms apart
        /// is the thing the limit exists to prevent, whichever of them produced each.
        /// </para>
        ///
        /// <para>
        /// A landmark is never rate-limited away. It is already sparse, it is the only thing that
        /// says WHERE IN TIME the tones are, and dropping it because a signal spoke two bars ago
        /// would lose the one thing playback has always said.
        /// </para>
        ///
        /// <para>
        /// <paramref name="stepped"/> is false on the sequencer's FIRST NavigateAction, which
        /// jumps the cursor to the plan's start bar rather than walking to it — the same reason
        /// <c>PlaybackNarration.LandmarkForStep</c> refuses that step. The start sentence has
        /// already named that bar, and a signal announced there would be heard as belonging to
        /// wherever the cursor happened to be sitting beforehand.
        /// </para>
        ///
        /// <para>
        /// The chart-pattern outcome is gated on BOTH "Describe chart patterns" (the content
        /// switch, which <c>ChartPatternOutcomesAt</c> reads) and "Narrate during playback" (the
        /// trigger switch, checked by the caller). One switch per content × trigger cell grows
        /// without bound, and a user who turned pattern descriptions off would be startled to
        /// hear them here.
        /// </para>
        /// </summary>
        private string? PlaybackSpeechForStep(WorkspaceState state, string? landmark, bool stepped)
        {
            if (!state.NarrateDuringPlayback) return null;
            if (!state.IsPlaying || state.IsPaused) return landmark;

            string? events = null;
            if (stepped)
            {
                int bar = state.CurrentDataIndex;
                int minGap = PlaybackNarration.MinBarsBetweenSignals(state.PlaybackSpeed);
                bool windowOpen = _lastPlaybackSignalBar < 0 || bar - _lastPlaybackSignalBar >= minGap;

                var signal = PlaybackNarration.SignalStepFor(state, bar, _playbackPlan);

                // ── A RARER SIGNAL IS NOT SILENCED BY A COMMONER ONE TWO BARS AGO ───────────
                //
                // The window is two seconds wide — twenty bars at 1x — and Cipher B prints a
                // WaveTrend cross every ten to twenty. So the gate that exists to stop routine
                // commentary piling up was ALSO eating the one utterance a run is listened to
                // for: a gold dot that lands four bars after a cross had its window already
                // spent by the cross. The half of Cody's 2026-09-05 report the clause ceiling
                // does not explain.
                //
                // The break-through is strictly rarer, never equal, so it cannot chain: whatever
                // speaks claims the window at its OWN rarity, and the next thing has to be rarer
                // still to interrupt. A stream of crosses is throttled exactly as before.
                bool rarerThanLast = signal.Text != null
                                     && signal.RarestFireCount < _lastPlaybackSignalRarity;

                if (windowOpen || rarerThanLast)
                {
                    string outcomes = ChartPatternOutcomesAt(state, state.Data, bar).Trim();
                    events = string.Join(" ", new[] { signal.Text, outcomes }
                        .Where(x => !string.IsNullOrWhiteSpace(x)));
                    if (string.IsNullOrWhiteSpace(events)) events = null;
                    else
                    {
                        _lastPlaybackSignalBar = bar;
                        // A pattern outcome with no signal behind it records int.MaxValue: it is
                        // not a marker, it claims no rarity, and a gold dot three bars later must
                        // not be dropped because a triangle resolved.
                        _lastPlaybackSignalRarity = signal.RarestFireCount;
                    }
                }
            }

            if (landmark == null && events == null) return null;
            if (events == null) return landmark;
            // The landmark carries no full stop of its own — "June 3", "14:00" — so it needs one
            // before the clause behind it, or a screen reader runs the two together.
            return landmark == null ? events : $"{landmark}. {events}";
        }

        // ── Pattern Speech Helpers ─────────────────────────────────────────────
        //
        // The vocabulary and the classification both live in CandlePatternSpeech now. They used
        // to live HERE, which is exactly why the two routes that read history could not name a
        // multi-bar pattern: the names were private to the coordinator, so the only way for the
        // detail key or the arrow keys to say "morning star" was to grow a classifier of their
        // own — and both did, with different thresholds. Anything that needs a candle named goes
        // through the shared helper; nothing re-spells a name locally.

        private void OnFeedbackRequest(FeedbackRequestEvent e)
        {
            // Deliberately no message-content filter here. There used to be one —
            // `if (e.Message.Contains("Audio mode:") || e.Message.Contains("Playback mode:")) return;`
            // — which suppressed some long-deleted meta-speech. By 2026-08-24 nothing in
            // the repo published either string (grepped: the filter and its own comment were
            // the only hits), so it silenced nothing real. What it still did was run BEFORE
            // the type switch, which made it the one `return` in this method that neither
            // speaks nor logs: any future Error or Alert whose text happened to contain
            // those words would have been dropped on the floor. That is precisely the
            // silent-failure shape the comments at the bottom of this switch exist to
            // prevent, so the filter is gone rather than narrowed.
            // The channel a Type would get on its own, unless the publisher named one.
            //
            // Only the dialogs that move money name one. Everything on the chart keeps the
            // tier its Type implies, which is why this is an override rather than a
            // parameter every publisher has to think about: a message that does not opt in
            // behaves exactly as it did before this line existed.
            SpeechChannel Ch(SpeechChannel fallback) => e.Channel ?? fallback;

            switch (e.Type)
            {
                case FeedbackType.StateChange:
                    // The publisher's Interrupt is honoured. This arm used to hard-code
                    // `interrupt: true`, so every caller that passed false — the rebind
                    // confirmation spoken after Tab has already moved on, the reconciliation
                    // "while you were away" reports, background monitoring on/off, a provider
                    // coercing the timeframe, and a dialog saying a choice was replaced while the
                    // screen reader is still reading the option the user landed on — cut off
                    // whatever was being said, and the flag read like a guarantee it was not.
                    // The default stays true, so a publisher that does not say keeps the old
                    // behaviour.
                    if (!string.IsNullOrEmpty(e.Message))
                        _speechRouter.Speak(e.Message, interrupt: e.Interrupt, channel: Ch(SpeechChannel.Chart));
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
                    string? nav = e.IsXMove ? ChartAndMarketEventContext() : null;
                    string armed = _quickTrade?.ArmedSuffix() ?? "";
                    if (armed.Length > 0)
                        nav = string.IsNullOrWhiteSpace(nav) ? armed : armed + " " + nav;

                    _navManager.HandleNavigationFeedback(
                        _store.State, e.IsXMove, e.IsYMove, e.Message ?? "", isJump: e.IsJump,
                        extraContext: nav);
                    break;

                case FeedbackType.VolumeChange:
                    if (!string.IsNullOrEmpty(e.Message))
                        _speechRouter.Speak(e.Message, interrupt: false, channel: Ch(SpeechChannel.Chart));
                    break;

                case FeedbackType.Error:
                    // Earcon FIRST so the blind user gets an immediate audio cue even if
                    // the screen reader is mid-phrase. Speech then follows with the detail.
                    // Previously this branch did speech only, which meant order-placement
                    // failures, provider disconnects, and any ReportError(..., High) path
                    // produced no earcon — violating the silent-failure rule.
                    _audioRouter.PlayEarcon(FeedbackType.Error, ErrorSeverity.High);
                    if (!string.IsNullOrEmpty(e.Message))
                        _speechRouter.Speak(e.Message, interrupt: true, channel: Ch(SpeechChannel.Critical));
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
                        _speechRouter.Speak(e.Message, interrupt: true, channel: Ch(SpeechChannel.Chart));
                    break;

                // Alert had no arm at all, so every FeedbackRequestEvent(Alert) was constructed,
                // published and thrown away — the websocket could drop mid-session
                // (GlobalErrorCoordinator.ReportNetworkRetry) and the trader was told nothing,
                // and a strategy silently overriding the user's configured entry trigger
                // (ConfigurableStrategy) announced itself to no one.
                //
                // This is the same missing-switch-arm defect FeedbackRouters:167 already carries a
                // note about — the EARCON router was fixed in 2026-07-21 and the SPEECH router was
                // not. Earcon first (immediate cue even mid-phrase) then the detail, matching the
                // Error branch. Event channel, not Critical: an alert is something that happened
                // TO the user, so Shift+F2 is allowed to mute it, exactly like AlertFiredEvent.
                case FeedbackType.Alert:
                    _audioRouter.PlayEarcon(FeedbackType.Alert);
                    if (!string.IsNullOrEmpty(e.Message))
                        _speechRouter.Speak(e.Message, interrupt: e.Interrupt, channel: Ch(SpeechChannel.Event));
                    break;

                // The remaining members. None of these has a publisher that carries a message
                // today (ViewportChange is published with "" by ViewportManager and
                // DrawingInteractionManager, whose announcements come from OnStateChanged), but
                // a member with no arm is how Alert stayed silent for months. Speaking any
                // message that IS supplied costs nothing and cannot go quiet.
                case FeedbackType.SeriesSelection:
                case FeedbackType.ComponentSelection:
                case FeedbackType.PointFocus:
                case FeedbackType.ViewportChange:
                    if (!string.IsNullOrEmpty(e.Message))
                        _speechRouter.Speak(e.Message, interrupt: e.Interrupt, channel: Ch(SpeechChannel.Chart));
                    break;

                case FeedbackType.Info:
                    if (!string.IsNullOrEmpty(e.Message))
                        _speechRouter.Speak(e.Message, interrupt: true, channel: Ch(SpeechChannel.Chart));
                    break;

                // Every member of FeedbackType is handled above, and FeedbackTypeCoverageTests
                // enumerates the enum to keep it that way. This arm exists for the member added
                // NEXT year: it fails loud (logged, and spoken if the caller supplied words)
                // rather than dropping the message the way Alert was dropped.
                default:
                    _logger?.LogWarning(
                        "[AccessibilityFeedbackCoordinator] Unhandled FeedbackType {Type} — message '{Message}' has no routing arm.",
                        e.Type, e.Message);
                    if (!string.IsNullOrEmpty(e.Message))
                        _speechRouter.Speak(e.Message, interrupt: e.Interrupt, channel: Ch(SpeechChannel.Chart));
                    break;
            }
        }

        /// <summary>
        /// The Shift+F1 "where am I?" sentence: symbol, provider, timeframe, then the focused
        /// series and pane, then the drawing anchor a nudge would move.
        ///
        /// <para>Reached by <see cref="ContextSummaryRequestEvent"/>. It used to be reached by a
        /// <c>"CONTEXT_SUMMARY"</c> sentinel in a <see cref="FeedbackRequestEvent"/>'s message
        /// field, which every OTHER subscriber of that event took at face value — the status bar
        /// printed it, and spoke it. See the record's own note.</para>
        /// </summary>
        private void SpeakContextSummary()
        {
            var state = _store.State;
            var id = state.Identity;
            string timeframe = string.IsNullOrEmpty(id.Timeframe) ? "unknown timeframe" : id.Timeframe;
            string symbol = string.IsNullOrEmpty(id.Symbol) ? "no symbol" : id.Symbol;
            string provider = string.IsNullOrEmpty(id.Provider) ? "" : $" on {id.Provider}";
            string msg = $"{symbol}{provider}, {timeframe}";

            // WHERE YOU ARE IS NOT CONDITIONAL ON HOW YOU GOT THERE.
            //
            // This clause used to be gated on `LastInteractionContext == Component`, which meant
            // Shift+F1 said nothing at all about the pane in the one situation where it is most
            // needed: Alt+PageUp/PageDown dispatches `SetInteractionContextAction(Series)`, so
            // immediately after a PANE MOVE — the move whose whole purpose is to change which
            // pane you are in — the orientation key answered with the symbol and the timeframe
            // and stopped. The gate was inherited from when this clause described a component;
            // it now describes a series and a pane, and both exist in either context.
            var focusedSeries = state.ActiveSeries.FirstOrDefault(s => s.Id == state.FocusedSeriesId);
            if (focusedSeries != null)
            {
                // The Count > 0 guard on the next line was written for exactly this
                // case and arrived one line too late — the clamp above it had already
                // thrown, taking the subscription that runs this code down with it.
                // Shift+F1 is the orientation key a disoriented user reaches for, so
                // this was the keystroke that silenced the terminal.
                // THE PANE IS THE SERIES' PANE, NOT ITS COMPONENT'S SUB-PANE.
                //
                // Reported by Cody, 2026-09-04: Alt+PageDown onto the volume pane and then
                // Shift+F1 answered "main pane". This block was the LAST reader of the sub-pane
                // model the sixteenth pass retired everywhere else. It asked the focused
                // COMPONENT for its sub-pane (since removed) and called an empty answer "main pane" — but a
                // pane is a Y axis, declared by `ChartSeries.Pane` across the whole series list,
                // and the volume series' components declare no sub-pane at all. So every series
                // outside Main answered "main pane", and the orientation key — the one a
                // disoriented user reaches for — was the key that lied about where they were.
                //
                // `ChartPaneModel` is the single structural model; navigation and
                // `ChartLayoutDescriber` already read it, and this now does too, so the pane
                // Alt+PageUp/Down moved you to and the pane Shift+F1 names cannot disagree.
                string paneKey = ChartPaneModel.KeyOf(focusedSeries);
                var panes = ChartPaneModel.Panes(state.ActiveSeries);
                var pane = panes.FirstOrDefault(p => string.Equals(p.Key, paneKey, StringComparison.Ordinal));
                var paneSeries = pane?.Series ?? new List<ChartSeries> { focusedSeries };

                msg += $". Focused on {focusedSeries.FriendlyName}, "
                     + ChartPaneModel.DisplayName(paneKey, paneSeries) + " pane";

                // WHERE that pane is in the stack, which is the other half of "where am I?" and
                // was never said. The old clause here counted the focused series' own sub-panes
                // and reported them as "panes" — a number about one series, spoken as though it
                // described the chart, and zero for every series that declares none.
                int paneIndex = panes.ToList().FindIndex(p => string.Equals(p.Key, paneKey, StringComparison.Ordinal));
                if (paneIndex >= 0 && panes.Count > 1)
                    msg += $", {paneIndex + 1} of {panes.Count}";
            }

            // A focused drawing: say which anchor Shift+Arrow would move, without
            // moving it. Nudging and cycling both change state; this is the only
            // way to just ask.
            string? anchor = _drawings?.SelectedAnchorSummary();
            if (anchor != null) msg += ". " + anchor;

            _speechRouter.Speak(msg, interrupt: true);
        }

        // ── Chart-pattern description (opt-in) ─────────────────────────────────

        // The bar the cursor was on last time a pattern readout was computed. -1 means "no idea",
        // which is the correct state after a load, a jump, or a timeframe change — see below.
        private int _lastPatternBar = -1;

        /// <summary>
        /// The formation clause, then the market-events clause. They are composed into one
        /// utterance because two Speak calls do not reliably produce two announcements (see the
        /// Navigation arm in OnFeedbackRequest).
        ///
        /// <para>
        /// Events are independent of "Describe chart patterns": a user may want the calendar and
        /// no formation names, or the reverse. The events clause is appended after the formation
        /// clause and is empty unless the level is not Off and something on this bar passes the
        /// level, the kinds and the chart's market.
        /// </para>
        /// </summary>
        internal string ChartAndMarketEventContext()
        {
            string patterns = ChartPatternContext();
            string events = MarketEventContext();
            if (events.Length == 0) return patterns;
            return patterns.Length == 0 ? events : patterns + " " + events;
        }

        /// <summary>The events clause for the bar under the cursor, or "".</summary>
        internal string MarketEventContext()
        {
            var state = _store.State;
            var level = MarketEvents.MarketEventSettings.Level(state);
            if (level == MarketEvents.MarketEventLevel.Off) return "";

            var data = state.Data;
            int idx = state.CurrentDataIndex;
            if (data == null || idx < 0 || idx >= data.Count) return "";

            var placements = _marketEventMap.For(state, DateTime.UtcNow);
            var onBar = placements.Where(p => p.BarIndex == idx).ToList();
            return MarketEvents.MarketEventSpeech.Clause(
                onBar,
                PlaybackNarration.BarSeconds(state),
                level,
                MarketEvents.MarketEventSettings.Kinds(state),
                state.SelectedMarketType);
        }

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
        ///   <item><b>A formation's start or end</b> — the bar it becomes knowable on, and the bar
        ///         that broke the trigger or that the shape aged out on. A step onto it, either
        ///         way, reads the same as a jump to it.</item>
        ///   <item><b>Everything in between</b> — silence on a step. The pattern has already been
        ///         described and the level is not changing.</item>
        /// </list>
        ///
        /// <para>
        /// A jump (any move of more than one bar, or the first move after a load) always says
        /// where it landed, which is exactly what "where am I?" wants. Either way it is one
        /// formation (ChartPatternNarrator.Landing): the semicolon choice wherever it is at the
        /// bar, else the one starting there, else the one ending there, else the leader.
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

            // Ranked once, then the user's semicolon choice (if any) is floated to the front.
            // Everything below reads from this list, so the choice applies identically to the jump
            // path, the entry announcement and the overlap count.
            //
            // The choice is sticky: it stays until semicolon picks another, wherever the user goes
            // and across restarts. Where its formation is not at the bar it is not spoken; a jump
            // reads what is there. Cody, 2026-11-13: "it should be sticky until I
            // intentionally change it ... It should stay regardless of where I navigate to." (The
            // same morning it was dropped on the first bar off the formation; Home then End lost it.)
            string chartKey = ChartPatternCache.KeyFor(state.Identity);
            var atBar = ChartPatternNarrator.AtBar(all, idx);
            var here = _patternFocus.Apply(chartKey, ChartPatternNarrator.ByDominance(atBar).ToList());

            // Jump, or first move: no edge was crossed, so describe where we landed — one formation
            // (ChartPatternNarrator.Landing): the semicolon choice wherever it is at the bar, else
            // the formation starting here, else the one ending here, else the leader. A formation
            // not at the bar is never mentioned, chosen or not.
            //
            // Cody, 2026-11-13: "it should name the formation i selected"; then, after the choice
            // was named from bars it was not on ("Double top confirmed 15 bars back" on the falling
            // wedge's start): "I don't want to hear about another pattern that isn't the pattern
            // start/end I landed on"; then, when the bar's own start beat his choice: "when I move
            // away from that pattern and move back or to the end of the pattern ... the one I
            // selected using ; should be what is read and drawn."
            var chosen = _patternFocus.PinnedIn(chartKey, all);
            string Landed()
            {
                var lead = ChartPatternNarrator.Landing(here, idx, chosen?.Identity);
                if (lead == null) return "";

                // On a start or end the other formations that start or end here too are named
                // ("Also starts here: ..."), not those merely passing through; until 2026-11-16 they
                // were only counted ("Plus 1 more formation here").
                string also = ChartPatternNarrator.DescribeAlsoAtEdges(lead, here, idx);
                if (lead.KnownAtIndex == idx)
                    return ChartPatternNarrator.DescribeEntry(lead, idx, SpeechPriceFormatter.FormatPrice)
                         + ChartPatternNarrator.DescribeContainment(lead, all, idx)
                         + also;
                if (lead.ResolvesAt == idx)
                {
                    string res = ChartPatternNarrator.DescribeResolution(lead, SpeechPriceFormatter.FormatPrice);
                    if (!string.IsNullOrEmpty(res)) return res + also;
                }

                // Lead, not DescribeMany: that re-ranks by size and so ignored the semicolon
                // choice on exactly this path, the one backslash takes.
                //
                // Then the nesting, smallest first (Cody, 2026-11-13: "say 'possible falling wedge
                // inside possible double top' ... if I'm inside a pattern inside a larger parent
                // one"). The leader is the largest, so without it a shape inside it went unnamed
                // behind "plus 1 more formation here". The note then counts only what neither
                // sentence named.
                return DescribeLeaderAndNesting(here.Where(p => p != lead).Prepend(lead).ToList(), idx);
            }

            if (prev < 0 || Math.Abs(idx - prev) != 1) return Landed();

            // A step of one bar speaks on a formation's start or end and is silent between them —
            // the pattern was described on the way in and its level is not changing. On those
            // bars it reads exactly what a jump there reads (Landed), the choice first wherever it
            // is at the bar. The edges are positions, not crossings: a bar that spoke once speaks
            // every time you stand on it, whichever way you came (the first version diffed the
            // formations on either side of the step, and stepping back onto a start said nothing).
            //
            // Until 2026-11-13 the step built its own sentence — the start or end of what it
            // crossed into, then every formation ending there — so , and . said one thing and the
            // arrows another on the same bar: "End of possible double top" against "Double top
            // confirmed here" walking back; both endings against one and a count where two end
            // together; and, worst, the inverse head and shoulders' start under a chosen double
            // top. Cody: "whether it is via , . or using the arrows. if I press ;, it should change
            // the drawing and speech of the selected pattern."
            return here.Any(p => ChartPatternNarrator.IsEdge(p, idx)) ? Landed() : "";
        }

        // Then, as on a start or end, the formations starting or ending at this bar by name: inside a
        // chosen formation a comma or period stop is for one of them (Cody, 2026-11-16).
        private static string DescribeLeaderAndNesting(IReadOnlyList<ChartPattern> here, int idx)
        {
            var nest = ChartPatternNarrator.Nesting(here);
            // Only a chain the leader is part of: otherwise the nesting sentence is about two
            // other formations, and a landing names the one at the bar and nothing else.
            if (!nest.Contains(here[0])) nest = Array.Empty<ChartPattern>();
            // A chain of one is not spoken (DescribeNesting needs two), so it names nothing: on
            // the real TAO/USDT 1h last bar the falling wedge beside the head and shoulders was
            // its own one-shape chain and dropped out of the count, so "plus 1 more" went unsaid.
            var named = (nest.Count > 1 ? nest.Select(p => p.Key) : Enumerable.Empty<(ChartPatternKind, int, int, int)>())
                .Append(here[0].Key)
                .Concat(ChartPatternNarrator.AlsoAtEdges(here[0], here, idx).Select(p => p.Key)).ToHashSet();
            return ChartPatternNarrator.Describe(here[0], SpeechPriceFormatter.FormatPrice)
                 + ChartPatternNarrator.DescribeNesting(nest)
                 + ChartPatternNarrator.DescribeAlsoAtEdges(here[0], here, idx)
                 + OverlapNote(1 + here.Count(p => !named.Contains(p.Key)));
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
