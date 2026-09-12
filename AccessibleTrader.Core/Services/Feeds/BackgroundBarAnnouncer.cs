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
    /// <b>Cody, 2026-09-08: notification and earcon by default, speech opt-in.</b> A bar close
    /// on a chart you are not looking at interrupting the chart you ARE looking at is the kind
    /// of thing that gets a feature switched off altogether — and for a screen-reader user an
    /// interruption is not a notification, it is the loss of the sentence being read. The
    /// notification and the earcon are ambient; speech is behind
    /// <see cref="SettingsKeys.SpeakBackgroundTabBars"/>, default off.
    ///
    /// <para>Two corrections to what this paragraph used to claim. (1) "By default" was false
    /// of the toast until 2026-09-11: it rode <c>notifications.desktop.newBars</c>, which
    /// defaulted OFF, so the shipped default was an earcon and nothing else. It is true now —
    /// <see cref="SettingsKeys.NotifyUnseenEvents"/> defaults ON. (2) The EARCON has never ridden
    /// any of those switches; it plays unconditionally, which is what "ambient" means here and
    /// what <c>SettingsKeys</c> used to describe the other way round.</para>
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
        private readonly Accessibility.HeadlessChartFactory? _charts;
        private readonly ILogger<BackgroundBarAnnouncer>? _logger;
        private bool _disposed;

        /// <summary>
        /// One narrator per background tab, with the crossover memory the ladder needs. Keyed by
        /// identity and rebuilt when the tab's series change — the same shape
        /// <c>LocalBackgroundMonitor</c> uses for the browser-closed half, because it is the same
        /// job: a chart nobody is looking at, scanned at its close.
        /// </summary>
        private readonly Dictionary<ChartIdentity, (string Signature, Accessibility.HeadlessChart Chart)> _narrators = new();
        private readonly object _narratorGate = new();

        /// <param name="charts">The narration ladder for a background tab, added 2026-09-11 on
        /// Cody's call ("giving the narration for other tabs would be useful to have the full
        /// ladder"). Optional: without it this class behaves as it did, announcing the two-clause
        /// sentence alone, which is what every head that does not register the factory gets.</param>
        public BackgroundBarAnnouncer(
            IMarketFeedHub hub,
            IBackgroundTabFeedService tabFeeds,
            IWorkspaceStore store,
            IEventBus bus,
            ISettingsManager settings,
            IEarconService earcons,
            ISpeechFeedbackRouter speech,
            ILogger<BackgroundBarAnnouncer>? logger = null,
            Accessibility.HeadlessChartFactory? charts = null)
        {
            _hub = hub;
            _tabFeeds = tabFeeds;
            _store = store;
            _bus = bus;
            _settings = settings;
            _earcons = earcons;
            _speech = speech;
            _charts = charts;
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

            // The earcon goes first and goes NOW — it is ambient, it says "something closed
            // somewhere else", and it must not wait behind an indicator computation.
            try { _earcons.PlayNewBar(); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Background bar earcon failed."); }

            // The words. Composing the ladder means computing the tab's indicators, which is
            // async, so the rest of the announcement leaves this thread — the feed hub raised
            // this event and must not be blocked on a scan.
            _ = AnnounceAsync(feed, closed, opened);
        }

        /// <summary>
        /// The notification and the speech, with the narration ladder behind them when the tab
        /// has series flagged with N.
        ///
        /// <para><b>Cody, 2026-09-11:</b> <i>"giving the narration for other tabs would be useful
        /// to have the full ladder."</i> Until then a background tab got a two-clause sentence
        /// while the SAME chart with the browser closed got the full ladder — so closing the
        /// browser told you more than leaving it open, which is not a defensible thing for a
        /// terminal to do.</para>
        /// </summary>
        private async Task AnnounceAsync(ChartFeed feed, Ohlcv closed, Ohlcv opened)
        {
            try
            {
                string sentence = BackgroundSentence(feed.Identity, closed, opened);
                string? ladder = await LadderAsync(feed).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(ladder)) sentence = sentence + " " + ladder;

                // The notification route. DesktopNotificationService subscribes this under the
                // one "events you cannot see" switch — and, since 2026-09-11, this is the ONLY
                // bar close that takes it: the focused chart's is spoken in the live region and
                // never notified, because the user is already being told. The ladder rides the
                // notification body too, exactly as it does with the browser closed: the
                // notification IS the announcement, so it carries the whole sentence.
                _bus.Publish(new BackgroundBarClosedEvent(feed.Identity, closed, opened, ladder));

                // Speech, opt-in. Named symbol first: this is by definition about a chart the
                // user is not looking at, so an announcement that opened with a price would be
                // unattributable. Never interrupting, on the Event channel — the same tier the
                // focused bar close uses, so Shift+F2 silences both. ONE utterance, ladder
                // included, as in-session and as headless.
                if (SpeakEnabled())
                    _speech.Speak(sentence, interrupt: false, channel: SpeechChannel.Event);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Background bar announcement failed for {Symbol}.",
                    feed.Identity.Symbol);
            }
        }

        /// <summary>
        /// The tab's narration ladder at this close, or null when it has nothing under N, no
        /// factory is registered, or the scan found nothing to say.
        /// </summary>
        private async Task<string?> LadderAsync(ChartFeed feed)
        {
            if (_charts == null) return null;

            var configs = SavedSeriesFor(feed.Identity);
            if (configs == null || !Accessibility.HeadlessChartFactory.HasNarratedSeries(configs)) return null;

            string signature = Accessibility.HeadlessChartFactory.Signature(configs);
            Accessibility.HeadlessChart chart;
            lock (_narratorGate)
            {
                // A tab whose series changed gets a NEW narrator: the old one's memory is about
                // components that may no longer exist. Same rule as the headless chart cache.
                if (!_narrators.TryGetValue(feed.Identity, out var held) || held.Signature != signature)
                {
                    held = (signature, _charts.Create(feed.Identity, configs));
                    _narrators[feed.Identity] = held;
                }
                chart = held.Chart;
            }

            var bars = feed.Bars;
            if (bars == null || bars.Count == 0) return null;

            // isFullHistory: the feed holds the tab's whole buffer, not a catch-up window, so
            // the chart never needs to ask for more.
            var observed = await chart.ObserveAsync(bars, isFullHistory: true, CancellationToken.None)
                                      .ConfigureAwait(false);
            return observed?.Narration;
        }

        /// <summary>
        /// The series the user actually has on that tab, from the workspace snapshot — the same
        /// source the background workspace monitors read. Null when the tab is not in the
        /// snapshot list, which means it is not a tab we should be narrating.
        /// </summary>
        private IReadOnlyList<SeriesConfig>? SavedSeriesFor(ChartIdentity identity)
        {
            try
            {
                var snapshots = _store.State.TabSnapshots;
                if (snapshots == null) return null;
                foreach (var snap in snapshots)
                {
                    if (!snap.Identity.Equals(identity)) continue;
                    var series = snap.ActiveSeries;
                    if (series == null || series.Count == 0) return null;
                    return series.Select(s => s.Config).ToList();
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not read the saved series for {Symbol}.", identity.Symbol);
                return null;
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
