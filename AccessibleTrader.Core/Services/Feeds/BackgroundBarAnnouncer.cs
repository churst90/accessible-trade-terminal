using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Feeds
{
    /// <summary>
    /// <b>Bars closing on charts you have open but are not looking at.</b>
    ///
    /// <para>
    /// ── The defect this closes ─────────────────────────────────────────────────
    /// The whole new-bar feature reached exactly ONE chart. There is a single publisher of
    /// <see cref="NewBarEvent"/> in the codebase — the workspace store's live-data path — the
    /// only thing that dispatches into it from a tick is <c>DataManager</c>, and
    /// <c>DataManager</c> binds the FOCUSED feed by design. Meanwhile
    /// <see cref="BackgroundTabFeedService"/> deliberately keeps up to
    /// <see cref="BackgroundTabFeedService.MaxLiveBackgroundFeeds"/> non-focused tabs on live
    /// subscriptions, precisely so background charts stay current. Those bars closed in silence.
    /// <b>A user who opened four charts to watch four markets was told about bar closes on one
    /// of them</b> — and the feature table recorded that as working, because it had been read at
    /// the subscriber and never traced to the publisher.
    /// See docs/BACKGROUND_MONITOR_PHASE3_SCOPE.md §1 F1.
    /// </para>
    ///
    /// <para>
    /// ── Why a separate event and not a wider <c>NewBarEvent</c> ───────────────
    /// <see cref="NewBarEvent"/> carries no identity because it is always about the focused
    /// chart, and its subscribers fill in the rest from <c>WorkspaceStore.State</c>:
    /// Heikin-Ashi, candle patterns, chart formations. Every one of those would have described
    /// the FOCUSED chart while announcing a different chart's bar. So this publishes
    /// <see cref="BackgroundBarClosedEvent"/>, which names its own symbol.
    /// </para>
    ///
    /// <para>
    /// ── What reaches the user, and why not speech ─────────────────────────────
    /// <b>Cody, 2026-09-08: toast and earcon by default, speech opt-in.</b> A bar close on a
    /// chart you are not looking at interrupting the chart you ARE looking at is the kind of
    /// thing that gets a feature switched off altogether — and for a screen-reader user an
    /// interruption is not a notification, it is the loss of the sentence being read. The toast
    /// and the earcon are ambient; speech is behind
    /// <see cref="SettingsKeys.SpeakBackgroundTabBars"/>, default off.
    /// </para>
    ///
    /// <para>
    /// ── Why the earcon and the speech live HERE and not in the coordinator ────
    /// <c>AccessibilityFeedbackCoordinator</c> owns the FOCUSED bar close and reads its gate
    /// (<c>AnnounceNewBars</c>) out of <c>WorkspaceState</c>. It has no
    /// <see cref="ISettingsManager"/>, and the background gate must not become another
    /// <see cref="WorkspaceState"/> field — a hand-written clone is a second place every field
    /// has to be added, and this repo has lost switches across a restart that way before. Making
    /// this class the single owner of the background route also satisfies the rule the last two
    /// phases were built on: <b>one delivery owner per event</b>. Toast is the exception, and
    /// deliberately so — it belongs with the other toasts, under the same user switch.
    /// </para>
    ///
    /// <para>
    /// ── Eligibility is TAB membership, not liveness ───────────────────────────
    /// Only feeds <see cref="IBackgroundTabFeedService"/> made live are announced. The hub also
    /// holds leased feeds that belong to monitors, evaluators and split views; those are not
    /// tabs the user opened and have no business announcing anything. Asked at announcement
    /// time, because tabs open and close between a subscription and a bar close.
    /// </para>
    /// </summary>
    public sealed class BackgroundBarAnnouncer : IDisposable
    {
        private readonly IMarketFeedHub _hub;
        private readonly IBackgroundTabFeedService _tabFeeds;
        private readonly IWorkspaceStore _store;
        private readonly IEventBus _bus;
        private readonly ISettingsManager _settings;
        private readonly IEarconService _earcons;
        private readonly ISpeechFeedbackRouter _speech;
        private readonly ILogger<BackgroundBarAnnouncer>? _logger;
        private bool _disposed;

        public BackgroundBarAnnouncer(
            IMarketFeedHub hub,
            IBackgroundTabFeedService tabFeeds,
            IWorkspaceStore store,
            IEventBus bus,
            ISettingsManager settings,
            IEarconService earcons,
            ISpeechFeedbackRouter speech,
            ILogger<BackgroundBarAnnouncer>? logger = null)
        {
            _hub = hub;
            _tabFeeds = tabFeeds;
            _store = store;
            _bus = bus;
            _settings = settings;
            _earcons = earcons;
            _speech = speech;
            _logger = logger;
            _hub.BackgroundFeedUpdated += OnBackgroundFeedUpdated;
        }

        private void OnBackgroundFeedUpdated(ChartFeed feed, FeedUpdateKind kind)
        {
            // LiveAppend is the ONLY kind that means "a bar closed". LiveReplace is the forming
            // bar being refreshed in place — announcing it would be a toast per tick. The other
            // kinds are loads, restores, gap-fills and prepends: history arriving, not a bar
            // ending. This mirrors the store's own test (`newCount > prevDataCount`).
            if (kind != FeedUpdateKind.LiveAppend) return;

            // Replay owns the announcement channel while it runs — the same guard the focused
            // toast applies. Without it, stepping through a recorded session is interleaved with
            // live bar closes from other tabs and neither is followable.
            if (_store.State.IsPlaying) return;

            if (!IsAnOpenBackgroundTab(feed.Identity)) return;

            var bars = feed.Bars;
            // The append has already happened, so the bar that CLOSED is the one before the
            // newly opened last bar. Fewer than two and there is no closed bar to name — which
            // is the seed case, not an error.
            if (bars.Count < 2) return;

            var closed = bars[bars.Count - 2];
            var opened = bars[bars.Count - 1];

            try
            {
                // The toast route. DesktopNotificationService subscribes this under the SAME
                // user switch as the focused bar close, because it is the same idea about a
                // different chart.
                _bus.Publish(new BackgroundBarClosedEvent(feed.Identity, closed, opened));

                // The earcon — ambient by design. It says "something closed somewhere else"
                // without taking the speech channel, which is the whole point of the default.
                _earcons.PlayNewBar();

                // Speech, opt-in. Named symbol first: this is by definition about a chart the
                // user is not looking at, so an announcement that opened with a price would be
                // unattributable. Never interrupting, on the Event channel — the same tier the
                // focused bar close uses, so Shift+F2 silences both.
                if (SpeakEnabled())
                    _speech.Speak(BackgroundSentence(feed.Identity, closed, opened),
                                  interrupt: false, channel: SpeechChannel.Event);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Background bar announcement failed for {Symbol}.",
                    feed.Identity.Symbol);
            }
        }

        private bool SpeakEnabled()
        {
            try { return _settings.GetSetting(SettingsKeys.SpeakBackgroundTabBars)?.ToObject<bool>() ?? false; }
            catch { return false; }
        }

        /// <summary>
        /// "ETH/USD 1h: close 1,234.5 at 09:31. New bar: open 1,234.6."
        ///
        /// <para>Deliberately shorter than the focused chart's sentence, which adds candle
        /// patterns, chart-formation outcomes and the Heikin-Ashi transform — all of them read
        /// out of the state of a DIFFERENT chart, and none of them things a user wants recited
        /// about a tab they are not on.</para>
        /// </summary>
        /// <remarks>PUBLIC because the headless monitor in the WebHost project speaks the same
        /// sentence for a bar that closed with no browser attached. A background tab and a
        /// browser-closed chart are the same news; two spellings of it would be a defect nobody
        /// would notice until they read a journal.</remarks>
        public static string BackgroundSentence(ChartIdentity identity, Ohlcv closed, Ohlcv opened)
        {
            int barSeconds = TimeframeUtility.ToSeconds(identity.Timeframe ?? "");
            if (barSeconds <= 0)
            {
                double gap = (opened.Date - closed.Date).TotalSeconds;
                barSeconds = gap > 0 ? (int)gap : 86400;
            }

            string stamp = SpeechTimeFormatter.FormatBarClock(closed.Date, barSeconds);
            string when = barSeconds < 86400 ? $" at {stamp}" : $" on {stamp}";
            string what = string.Join(" ", new[] { identity.Symbol ?? "", identity.Timeframe ?? "" }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            string lead = what.Length == 0 ? "" : what + ": ";

            return $"{lead}close {SpeechPriceFormatter.FormatPrice(closed.Close)}{when}. "
                 + $"New bar: open {SpeechPriceFormatter.FormatPrice(opened.Open)}.";
        }

        private bool IsAnOpenBackgroundTab(ChartIdentity identity)
        {
            try { return _tabFeeds.LiveBackgroundFeeds.Contains(identity); }
            catch (Exception ex)
            {
                // A feed service that throws must not take the announcement path with it, but
                // it must not silently swallow every bar close either.
                _logger?.LogWarning(ex, "Could not determine background-tab membership for {Symbol}.",
                    identity.Symbol);
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _hub.BackgroundFeedUpdated -= OnBackgroundFeedUpdated;
        }
    }
}
