using System.Reactive.Linq;
using System.Reactive.Subjects;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Audio;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// Which mute tier a spoken message belongs to. The 2026-07-21 redesign: the
    /// gate lives HERE, at the router, not at call sites — per-call-site
    /// IsSpeechEnabled checks are exactly how the F2 bypasses crept in.
    /// </summary>
    public enum SpeechChannel
    {
        /// <summary>Response to something the user just did (navigation values,
        /// zoom/pan, context summary, status). Muted by F2.</summary>
        Manual,
        /// <summary>Something that happened to the user (alerts, monitoring,
        /// new bars, auto-narration). Muted by Shift+F2.</summary>
        Event,
        /// <summary>Order-execution outcomes (fills, stops, take-profits).
        /// Break through BOTH mutes by default — the manual's "the one feedback
        /// you never miss" promise — unless speech.muteIncludesOrderEvents opts
        /// them into the Event tier.</summary>
        OrderEvent,
        /// <summary>Errors and the mute-toggle confirmations themselves.
        /// Never muted: "Speech off" must be heard, and silent failures are
        /// forbidden by the feedback contract.</summary>
        Critical,
    }

    public interface ISpeechFeedbackRouter
    {
        void Speak(string message, bool interrupt = true, SpeechChannel channel = SpeechChannel.Manual);
        void SpeakPoint(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, Ohlcv point, string prefix = "");
        void SpeakProfile(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, int binIndex, string prefix = "");
        void SpeakHeatmap(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, int dataIndex, int binIndex, string prefix = "");
    }

    public class SpeechFeedbackRouter : ISpeechFeedbackRouter, IDisposable
    {
        private readonly ISpeechManager _speechManager;
        private readonly ISpeechFormatter _speechFormatter;
        private readonly IWorkspaceStore _store;
        private readonly Subject<(string Message, bool Interrupt)> _speechSubject = new();
        private readonly IDisposable _subscription;

        // Optional so existing three-arg construction (tests) keeps working; DI
        // supplies the settings facade, enabling the order-event opt-in.
        private readonly IAppSettings? _appSettings;

        public SpeechFeedbackRouter(ISpeechManager speechManager, ISpeechFormatter speechFormatter, IWorkspaceStore store,
            IAppSettings? appSettings = null)
        {
            _speechManager = speechManager;
            _speechFormatter = speechFormatter;
            _store = store;
            _appSettings = appSettings;

            _subscription = _speechSubject
                .Subscribe(x =>
                {
                    if (x.Interrupt)
                    {
                        _speechManager.Silence();
                        _speechManager.Speak(x.Message, true);
                    }
                    else
                    {
                        _speechManager.Speak(x.Message, false);
                    }
                });
        }

        // ── Speech priority ──────────────────────────────────────────────────
        //
        // <b>What went wrong.</b> There was no priority anywhere. ISpeechManager exposes only
        // Speak(string, bool interrupt) — no priority, no queue, no politeness level — and
        // `interrupt` defaults to TRUE on this router, with the subscription above
        // implementing it as Silence() then Speak(). SpeechChannel is a MUTE TIER, not a
        // priority: IsChannelAudible only decides whether to emit at all.
        //
        // So OrderRejectedEvent speaks "Order rejected for BTCUSDT. Insufficient balance." on
        // the OrderEvent channel with interrupt: true, and the user's next arrow key ~200 ms
        // later calls Speak(barReading, interrupt: true) on the Manual channel — which calls
        // Silence() and truncates the rejection mid-word. The user hears "Order rejec—" and a
        // price. Key-repeat on the arrow keys is the NORMAL way this terminal is read, so this
        // is not an edge case; it is the common one.
        //
        // <b>The fix.</b> A monotonic "speaking-until" stamp per utterance, as the finding
        // suggests. A lower-priority message that arrives while a strictly-higher-priority one
        // is still in flight does not interrupt it — it is spoken WITHOUT interrupting, so the
        // screen reader queues it behind. Equal or higher priority interrupts as before,
        // because a second order outcome genuinely does supersede the first.
        //
        // The arbitration lives here rather than in ISpeechManager on purpose: this is the one
        // place that already knows the channel, and widening the platform interface would put
        // the same decision in three implementations that could then disagree.

        private static int PriorityOf(SpeechChannel channel) => channel switch
        {
            SpeechChannel.Critical   => 3,
            SpeechChannel.OrderEvent => 2,
            SpeechChannel.Event      => 1,
            _                        => 0,   // Manual
        };

        /// <summary>
        /// Characters a screen reader gets through in a second. ~180 wpm at ~5 characters a
        /// word is 15; deliberately on the FAST side, because over-estimating the duration
        /// would suppress a legitimate interrupt for longer than the speech actually lasts,
        /// and a chart that stops responding to arrow keys is a worse bug than a clipped word.
        /// </summary>
        private const double SpeechCharsPerSecond = 15.0;

        /// <summary>
        /// Ceiling on how long one utterance may hold priority. A pathological message must
        /// not be able to lock out navigation.
        /// </summary>
        private static readonly TimeSpan MaxProtectedSpeech = TimeSpan.FromSeconds(4);

        private readonly object _priorityGate = new();
        private int _inFlightPriority;
        private DateTime _inFlightUntilUtc = DateTime.MinValue;

        /// <summary>
        /// Decides whether this message may interrupt, and records it as in flight.
        /// </summary>
        private bool MayInterrupt(string message, bool requested, SpeechChannel channel)
        {
            int priority = PriorityOf(channel);
            var now = DateTime.UtcNow;

            var estimated = TimeSpan.FromSeconds(message.Length / SpeechCharsPerSecond);
            if (estimated > MaxProtectedSpeech) estimated = MaxProtectedSpeech;

            lock (_priorityGate)
            {
                bool higherStillSpeaking = _inFlightPriority > priority && now < _inFlightUntilUtc;

                // A message that is not allowed to interrupt still becomes the in-flight one
                // only if it outranks what is there; otherwise the protected utterance keeps
                // its slot for the rest of its estimated duration.
                if (priority >= _inFlightPriority || now >= _inFlightUntilUtc)
                {
                    _inFlightPriority = priority;
                    _inFlightUntilUtc = now + estimated;
                }

                return requested && !higherStillSpeaking;
            }
        }

        /// <summary>Test seam: forget any in-flight utterance.</summary>
        internal void ResetSpeechPriorityForTests()
        {
            lock (_priorityGate)
            {
                _inFlightPriority = 0;
                _inFlightUntilUtc = DateTime.MinValue;
            }
        }

        public void Speak(string message, bool interrupt = true, SpeechChannel channel = SpeechChannel.Manual)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (!IsChannelAudible(channel)) return;
            _speechSubject.OnNext((message, MayInterrupt(message, interrupt, channel)));
        }

        private bool IsChannelAudible(SpeechChannel channel)
        {
            var state = _store.State;
            return channel switch
            {
                SpeechChannel.Manual => state.IsSpeechEnabled,
                SpeechChannel.Event => state.IsEventSpeechEnabled,
                // Order outcomes break through unless the user explicitly opted
                // them into the event mute (Settings → speech.muteIncludesOrderEvents).
                SpeechChannel.OrderEvent =>
                    state.IsEventSpeechEnabled || !(_appSettings?.MuteIncludesOrderEvents ?? false),
                _ => true, // Critical
            };
        }

        public void SpeakPoint(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, Ohlcv point, string prefix = "")
        {
            bool isXMove = previousState == null || state.CurrentDataIndex != previousState.CurrentDataIndex;
            bool isYMove = previousState != null && (state.FocusedComponentIndex != previousState.FocusedComponentIndex || state.FocusedBinIndex != previousState.FocusedBinIndex);
            
            string speechText = _speechFormatter.FormatPointFeedback(state, isXMove, isYMove, series, point, prefix);
            Speak(speechText, true);
        }

        public void SpeakProfile(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, int binIndex, string prefix = "")
        {
            bool isXMove = previousState == null || state.CurrentDataIndex != previousState.CurrentDataIndex;
            bool isYMove = previousState != null && state.FocusedBinIndex != previousState.FocusedBinIndex;

            string speechText = _speechFormatter.FormatProfileFeedback(state, isXMove, isYMove, series, binIndex, prefix);
            Speak(speechText, true);
        }

        public void SpeakHeatmap(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, int dataIndex, int binIndex, string prefix = "")
        {
            bool isXMove = previousState == null || state.CurrentDataIndex != previousState.CurrentDataIndex;
            bool isYMove = previousState != null && state.FocusedBinIndex != previousState.FocusedBinIndex;

            string speechText = _speechFormatter.FormatHeatmapFeedback(state, isXMove, isYMove, series, dataIndex, binIndex, prefix);
            Speak(speechText, true);
        }

        public void Dispose()
        {
            _subscription.Dispose();
        }
    }

    public interface IAudioFeedbackRouter
    {
        bool IsSonificationEnabled { get; set; }
        void PlayEarcon(FeedbackType type, ErrorSeverity severity = ErrorSeverity.Medium);

        // NO Sonify* members. There were four (SonifySeries/SonifyComponent/SonifyProfile/
        // SonifyHeatmap), with zero production call sites, all of which wrote voice slot 0 —
        // the slot the single-navigation-path redesign exists to keep under one writer.
        // A second exported way to reach it is the bug, whether or not anything calls it today.
        // Navigation sonification goes through SonificationManager.SyncNavigationSlots.
        void Silence();
    }

    public class AudioFeedbackRouter : IAudioFeedbackRouter
    {
        private readonly INavigationSonifier _sonifier;
        private readonly IEarconService _earcons;

        public bool IsSonificationEnabled { get; set; } = true;

        public AudioFeedbackRouter(INavigationSonifier sonifier, IEarconService earcons)
        {
            _sonifier = sonifier;
            _earcons = earcons;
        }

        public void PlayEarcon(FeedbackType type, ErrorSeverity severity = ErrorSeverity.Medium)
        {
            switch (type)
            {
                case FeedbackType.Error: _earcons.PlayError(severity); break;
                case FeedbackType.Info: _earcons.PlayInfo(); break;
                // FOUND 2026-07-21: Alert had no case — every PlayEarcon(Alert)
                // call (fired alerts with Delivery=Earcon, NotificationHub,
                // the sandbox advisory) was SILENT. Now a real alert sound.
                case FeedbackType.Alert: _earcons.PlayAlert(); break;
                case FeedbackType.Boundary: _earcons.PlayBoundary(); break;

                // FOUND 2026-08-21, the same defect one rung further down: Alert got its arm in
                // July and the OTHER five members were left dead. PlayEarcon(StateChange) is what
                // AccessibilityFeedbackCoordinator requests for OrderCancelledEvent — a call added
                // specifically because "cancels were the one order state change that vanished
                // silently", and which itself did nothing. Navigation, VolumeChange,
                // SeriesSelection, ComponentSelection, PointFocus and ViewportChange were dead too,
                // taking five of sixteen EarconType values with them through
                // GlobalErrorCoordinator.PlayEarcon's mapping.
                //
                // A caller that asks for a sound gets a sound. Info's neutral blip is the floor —
                // no member of this enum is allowed to mean silence, because a silent earcon is
                // indistinguishable from a broken binding, and that is what the feedback contract
                // exists to forbid.
                default: _earcons.PlayInfo(); break;
            }
        }

        public void Silence() => _sonifier.Silence();
    }
}
