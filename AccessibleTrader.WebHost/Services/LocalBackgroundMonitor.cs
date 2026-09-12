using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Feeds;
using AccessibleTrader.Core.Services.Notifications;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Core.Services.Workspace;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// The local background-monitoring core: on a LOCAL WebHost (HostMode.Full —
    /// not the hosted terminal, not the demo) the server process outlives the
    /// browser tab, and everything needed to keep watching is server-side —
    /// speech, a desktop toast and a notification sound, each through whatever
    /// this desktop provides (see DesktopDeliveryPlan: Orca/spd-say +
    /// notify-send + paplay on Linux, say + Notification Center + afplay on
    /// macOS, SAPI + the Action Center on Windows). So: close the browser and
    /// your alerts keep evaluating and keep being HEARD.
    ///
    /// Until 2026-09-06 that last sentence was true on LINUX ONLY and said so
    /// nowhere: every probe went through WebHostSpeechManager.FindOnPath, which
    /// returns null on anything that is not Linux, so a Windows or macOS user
    /// got a monitor that ran, watched, and delivered silently to no one.
    ///
    /// Scope: every alert that carries an explicit Symbol + Provider — the watch
    /// list is DERIVED from your saved alerts, no separate configuration. Until
    /// 2026-09-11 that read "SIMPLE alerts (price/pattern rules)", because the
    /// evaluator was handed a blank chart; indicator, POC, trend, zone and
    /// condition-tree alerts are evaluated now against a chart composed per
    /// symbol (HeadlessChart). Current-chart alerts — no symbol — stay
    /// session-only, because there is nothing to fetch.
    ///
    /// Until 2026-09-06 (Phase 1) the monitor PAUSED entirely while any browser
    /// session was connected, because the in-session pipeline owned delivery then
    /// and both speaking through the same Orca would double every announcement.
    /// That was true of the symbol ON SCREEN and false of every other one: the
    /// in-session pipeline gates alerts to the focused chart, so an alert on a
    /// symbol with no tab open was evaluated by NOBODY while the browser was
    /// connected — closing your browser made MORE of your alerts work than
    /// leaving it open. The pause is now a ROUTING rule: see
    /// <see cref="CircuitAlertCoverage"/>, which is the same per-symbol
    /// suppression the hosted monitor already uses.
    ///
    /// Since 2026-09-11 (Phase 4) that routing rule sits under a policy about
    /// CHANNELS rather than owners, and it is worth stating because the two are
    /// easy to conflate. Cody: what happens on the chart in FRONT of the trader is
    /// spoken in the browser and never notified; everything else is a system
    /// notification. Coverage still decides WHO announces a given symbol — that is
    /// this class's question and it has not changed — while
    /// <see cref="Core.Services.Notifications.NotificationPolicy"/> decides which
    /// channel the circuit uses for the events it keeps.
    ///
    /// It also no longer builds a throwaway DI scope per poll. It runs inside
    /// <see cref="HeadlessSession"/> — one scope for the life of the process —
    /// so subscriptions inside it outlive a tick, providers stay configured
    /// between polls, and a fired alert can be PUBLISHED on a real event bus
    /// where the ordinary in-session subscribers (email, Telegram, webhooks, the
    /// journal) pick it up unchanged.
    ///
    /// Since 2026-09-11 (Phase 3 D4) it also speaks the NARRATION LADDER for the
    /// saved tabs' series flagged with N — the same scan the focused chart runs,
    /// through <see cref="HeadlessChart"/> — composed into the bar-close
    /// sentence as one utterance, exactly as in-session.
    ///
    /// Opt-in: Settings → General → "Keep monitoring when the browser is closed"
    /// (<see cref="Core.Services.SettingsKeys.BackgroundLocalMonitoring"/>, default
    /// off). Read per poll — and that became TRUE on 2026-09-11 rather than merely
    /// claimed: <c>ISettingsManager</c> is Scoped and caches its document for the
    /// life of the instance, and this monitor's scope is the life of the PROCESS, so
    /// until <see cref="HeadlessSession.RefreshSettings"/> was added at the top of
    /// this poll the switch was read once, at the first poll, and never again.
    /// </summary>
    public sealed class LocalBackgroundMonitor : BackgroundService
    {
        /// <summary>The master switch. Defined in Core beside every other settings key since
        /// 2026-09-11; this alias stays because four test files and the tray name it here.</summary>
        public const string SettingKey = SettingsKeys.BackgroundLocalMonitoring;
        public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

        /// <summary>The narrowest fetch a poll ever makes: the forming bar, the one that just
        /// closed, and one more. Alerts read the last two; the bar-close observer reads the
        /// newest date.</summary>
        internal const int MinFetch = 3;

        private readonly HeadlessSession _session;
        private readonly DemoPolicy _demo;
        private readonly RecentAlertsBuffer _recent;
        private readonly Tray.AlertSnooze _snooze;
        private readonly ILogger<LocalBackgroundMonitor> _logger;

        /// <summary>Sound, toast and speech. Injected rather than built here so this class can
        /// be constructed — and its escalation driven — without probing the PATH or spawning a
        /// process. See <see cref="IDesktopAlertPresenter"/>.</summary>
        private readonly IDesktopAlertPresenter _presenter;

        // One evaluator for the monitor's lifetime: it owns the per-alert
        // hysteresis/edge state, so a level crossed at 03:00 doesn't re-fire
        // on every subsequent poll. Built on the first poll rather than here because two of its
        // collaborators live in the headless scope: the level service (a POC alert reads the
        // profile's point of control through it) and the condition-tree evaluator (an advanced
        // alert is nothing without one). Until 2026-09-11 it was built with neither, which was
        // consistent with the blank state it was handed and just as useless.
        private AlertEvaluator? _evaluator;
        private readonly HashSet<string> _reportedFailures = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Whether the tray's snooze was active when this poll began.
        ///
        /// <para><b>What a snooze does and does not do.</b> It silences the DESKTOP — sound,
        /// notification and speech — for alerts, bar closes, the narration ladder and the
        /// monitor's own status reports. It does not stop evaluation, it does not stop the
        /// recent-alerts list filling up (catching up on what you missed is the point of a
        /// snooze), and it does not stop the email / Telegram / webhook fan-out, which is a
        /// routing rule rather than a noise one. Order fills, stops and take-profits are not
        /// covered at all: money always pierces (Cody, 2026-09-11), which is why
        /// <c>HeadlessOrderWatch</c> and <c>HeadlessOrderAnnouncer</c> never consult it.</para>
        /// </summary>
        private bool _announcementsSnoozed;

        /// <summary>The one notification switch as of this poll — see
        /// <see cref="NotificationPolicy.NotifyUnseen"/>. Default ON.</summary>
        private bool _notifyUnseen = true;

        public LocalBackgroundMonitor(
            HeadlessSession session,
            DemoPolicy demo,
            RecentAlertsBuffer recent,
            Tray.AlertSnooze snooze,
            IDesktopAlertPresenter presenter,
            ILogger<LocalBackgroundMonitor> logger)
        {
            _session = session;
            _demo = demo;
            _recent = recent;
            _snooze = snooze;
            _presenter = presenter;
            _logger = logger;
        }

        // ── The poll loop ────────────────────────────────────────────────────

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            if (_demo.IsDemo || _demo.IsHosted) return; // local desktops only

            _logger.LogInformation(
                "Local background monitor available ({Delivery}). Waiting for the opt-in setting.",
                _presenter.Describe());

            while (!ct.IsCancellationRequested)
            {
                try { await PollOnceAsync(ct); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { _logger.LogWarning(ex, "Background monitor poll failed; retrying next cycle."); }

                try { await Task.Delay(PollInterval, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        /// <remarks>Internal rather than private so the whole poll — routing, evaluation and
        /// delivery — can be driven once in a test. The DOUBLING hazard this phase introduces
        /// cannot be proved from the pure helpers alone: it only shows up in what actually
        /// reaches the desktop with a circuit open versus with none.</remarks>
        internal async Task PollOnceAsync(CancellationToken ct)
        {
            // The user silenced alerts and bar closes from the tray.
            //
            // It gates ANNOUNCEMENT, never OBSERVATION, and that is a fix rather than a style
            // point: this used to be the first statement of the method, so a 30-minute snooze on
            // a 1-minute chart was a 30-bar gap. HeadlessChart re-seeded when the poll came back,
            // the first close after the snooze was swallowed on both oracles, and DeadFeedTracker
            // never advanced — so a feed that died during the snooze was never reported either.
            // Same rule as the browser-coverage gate one level down: watch always, speak by gate.
            // One read per poll, held in a field so the four announcement sites need not thread
            // it through. Safe because a BackgroundService's polls are serialised — the loop
            // awaits each PollOnceAsync before starting the next.
            _announcementsSnoozed = _snooze.IsActive;

            var services = _session.Services;

            // Settings come off disk again on every poll. Without this the scope's cached
            // document — loaded at the FIRST poll and never since — answered every switch
            // read below for the life of the process. See HeadlessSession.RefreshSettings.
            _session.RefreshSettings();
            var settings = services.GetRequiredService<ISettingsManager>();
            if (!(settings.GetSetting(SettingKey)?.ToObject<bool>() ?? false)) return;

            var alerts = services.GetRequiredService<IWorkspaceLibraryService>().LoadAlerts();

            // The saved tabs are read for three reasons that answer to three different switches:
            // bar closes (the New-bars category, opt-in), the narration ladder (the Narration
            // tab's master switch, default on, over the per-series N flag), and the ALERTS — an
            // indicator alert reads the indicator as the user configured it on the chart, and a
            // point-of-control alert reads the chart's profile. Any one is a reason to know
            // which charts the user had open.
            bool barClosesOn = WatchBarCloses(settings);
            _notifyUnseen = barClosesOn;   // the SAME switch; held for the delivery sites below
            bool narrationOn = NarrateOnBarClose(settings);
            bool anyAlerts = alerts.Any(a => a.IsActive);
            var session = barClosesOn || narrationOn || anyAlerts ? LoadLastSession(services) : null;
            var tabWatches = DeriveBarCloseWatches(session);

            WarnOnceAboutUnwatchable(DeriveUnwatchable(alerts, session));

            // The routing rule that replaced "stand down while a circuit is open". A symbol an
            // open browser session already watches belongs to that session; everything else is
            // ours. See CircuitAlertCoverage for why this is not a pause.
            var covered = CircuitAlertCoverage.CoveredSymbols();
            var watches = OwnedWatches(DeriveWatches(alerts, session), covered);

            // Bar closes (Phase 3 D1). Same routing rule as alerts: a symbol an open browser
            // already covers belongs to that browser — in-session the focused chart publishes
            // NewBarEvent and BackgroundBarAnnouncer covers the other live tabs, so announcing
            // here too would be the doubling this phase's predecessors were built to avoid.
            //
            // OBSERVED whether or not a browser covers them; ANNOUNCED only when owned. Until
            // 2026-09-11 a covered chart was dropped from the list entirely, so the moment the
            // browser closed the monitor met the chart for the first time — and a first sighting
            // only seeds. Cody, three tabs, a 1-minute chart, browser shut: the earliest possible
            // announcement was the SECOND bar to close after the hand-off, on top of the circuit
            // retention period the hand-off itself waits for. Watching the timestamp while the
            // browser is open costs one small fetch a minute per saved tab and means the seed
            // is already warm when the chart becomes ours.
            var barWatches = barClosesOn
                ? tabWatches.Where(w => ClearsTimeframeFloor(w.Timeframe, TimeframeFloor(settings))).ToList()
                : new List<Watch>();
            var announceKeys = OwnedWatches(barWatches, covered)
                .Select(WatchKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // The narration ladder (Phase 3 D4). Cody, 2026-09-11: "I also want the narration
            // ladder to also be spoken when the browser is closed too." Same ownership rule, and
            // observed-while-covered for the same reason as the bar close above.
            //
            // BEHIND THE TIMEFRAME FLOOR since 2026-09-11, and that reverses what this comment
            // used to say. The old argument was that the floor is "new-bar announcements only",
            // so a 1-minute pane flagged with N earned a reading a minute. The effect was that
            // raising the floor to silence a fast chart silenced its bar close and left it
            // reciting its ladder every minute anyway — while the settings hint promised
            // silence. Cody's call: the floor means "do not talk to me about charts faster than
            // this", and it means it about every sentence.
            string? floor = TimeframeFloor(settings);
            var narrationWatches = narrationOn
                ? tabWatches.Where(w => HeadlessChartFactory.HasNarratedSeries(w.Series)
                                        && ClearsTimeframeFloor(w.Timeframe, floor)).ToList()
                : new List<Watch>();
            var narrateKeys = OwnedWatches(narrationWatches, covered)
                .Select(WatchKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // One fetch per chart, however many reasons there are to want it. A symbol carrying
            // an alert AND sitting in an open tab is two reasons and must stay one request.
            var targets = MergeTargets(watches, barWatches, narrationWatches);
            ForgetChartsNotIn(targets);
            if (targets.Count == 0) return;

            // Serialised with the order watch's identical preamble — two loops on one scope
            // means one non-thread-safe IDataService. See HeadlessSession.EnsureDataReadyAsync.
            await _session.EnsureDataReadyAsync();
            var data = services.GetRequiredService<IDataService>();

            foreach (var target in targets)
            {
                ct.ThrowIfCancellationRequested();
                var watch = target.Watch;
                var provider = await data.GetProviderAsync(watch.Provider);
                if (provider == null) continue;

                // The chart, when this tab has anything under N or an alert that reads a series,
                // decides how deep the fetch is: the indicators' warmup on a cold buffer, MinFetch
                // once it is warm.
                var chart = target.Narrate || watch.Alerts.Count > 0 ? GetChart(services, watch) : null;
                if (chart != null && !chart.HasSeries) chart = null;   // price alerts only: bars suffice
                int limit = Math.Max(MinFetch, chart?.FetchLimit ?? MinFetch);

                var bars = await FetchAsync(provider, watch, limit);
                if (bars == null) continue;
                NoteFeedRecovered(watch.Symbol);
                if (bars.Count < 2) continue;

                // The chart is OBSERVED on every poll — covered or not — so the ladder's memory
                // of what has already been said, and the alerts' memory of last poll's values,
                // are warm at hand-off. Whether anything is SPOKEN is decided below, by ownership.
                string? ladder = null;
                HeadlessObservation? observed = null;
                if (chart != null)
                {
                    observed = await chart.ObserveAsync(bars, isFullHistory: limit >= chart.BarsNeeded, ct);
                    if (observed.NeedsMoreHistory)
                    {
                        // A poll was missed for longer than the catch-up window covers: ask once
                        // for the full window, then let the chart decide whether to re-seed.
                        var more = await FetchAsync(provider, watch, chart.BarsNeeded);
                        if (more != null && more.Count >= 2)
                        {
                            bars = more;
                            observed = await chart.ObserveAsync(bars, isFullHistory: true, ct);
                        }
                    }
                    ladder = observed.Narration;
                }

                // Bar closes FIRST, and outside the alert path: a chart with no alerts on it is
                // the ordinary case for this half, and burying it under an alert loop that runs
                // zero times is how it would come to depend on something unrelated.
                // OBSERVE every chart that was fetched; ANNOUNCE only the ones whose bar
                // closes are wanted. Until 2026-09-11 the observation itself was conditional,
                // so the memory was never seeded while the new-bars switch was off — ticking
                // the box mid-session then swallowed the first close after it, and so did a
                // chart fetched only for its alerts or its ladder. Same rule as the fetch list
                // above, one level down: the gate decides what is SAID, never what is WATCHED.
                // The seed costs one timestamp per chart.
                bool closed = NoteBarClose(watch, bars);
                bool speakLadder = ladder != null && narrateKeys.Contains(WatchKey(watch));

                if (closed && target.WatchBarCloses && announceKeys.Contains(WatchKey(watch)))
                    AnnounceBarClose(watch, closed: bars[^2], opened: bars[^1], speakLadder ? ladder : null);
                else if (speakLadder)
                    AnnounceNarration(watch, ladder!);

                if (watch.Alerts.Count == 0) continue;

                // The state the evaluator reads: the chart's, with every referenced series
                // computed and the previous poll's values for the crossover memory — or, for a
                // chart with nothing to compute, the fetched bars alone (a three-bar pattern
                // reads Data; a price crossing reads the two newest closes). Until 2026-09-11
                // this was WorkspaceState.Initial and a fresh empty dictionary on every poll:
                // no Data, no series, no memory — see docs/BACKGROUND_MONITOR_PHASE3_SCOPE.md F3.
                var state = observed?.State ?? BareState(watch, bars);
                var previous = observed?.PreviousValues ?? new Dictionary<string, double>();
                var fired = Evaluator(services).EvaluateAlerts(
                    watch.Alerts, state, bars[^1], bars[^2], previous).ToList();

                foreach (var f in fired) Deliver(f, watch.Symbol);
            }
        }

        /// <summary>The fetched bars and nothing else — the state for a chart that has no series
        /// to compute. Price and candle alerts read this; a three-bar pattern needs the bars in
        /// <c>Data</c>, which the blank state never had.</summary>
        private static WorkspaceState BareState(Watch watch, IReadOnlyList<Ohlcv> bars) =>
            WorkspaceState.Initial with
            {
                Identity = new ChartIdentity(watch.Market, watch.Provider, watch.Symbol, watch.Timeframe),
                SymbolDisplayName = watch.Symbol,
                Data = new TimeSeriesBuffer<Ohlcv>(bars.ToList()),
                CurrentDataIndex = bars.Count - 1,
                InitStatus = InitializationStatus.Ready,
                DataStatus = DataStatus.Ready,
            };

        /// <summary>
        /// The evaluator, with the headless scope's level service (POC alerts) and condition
        /// evaluator (tree alerts) when the host registers them. Its two failure events are
        /// SPOKEN, once per alert: an alert whose rule throws, or whose tree has a leaf nothing
        /// here can answer, is an alert that is not being watched, and the in-session
        /// orchestrator says so too. Silence must never read as coverage.
        /// </summary>
        private AlertEvaluator Evaluator(IServiceProvider services)
        {
            if (_evaluator != null) return _evaluator;
            _evaluator = new AlertEvaluator(
                new SdkCandlePatternAnalyzer(), new IndicatorContextAnalyzer(),
                services.GetService<ILevelService>(),
                services.GetService<IConditionEvaluator>());
            _evaluator.EvaluationDegraded += (alert, why) =>
                Announce($"Alert {alert.Name} cannot be fully evaluated in the background: {why}.");
            _evaluator.EvaluationFailed += (alert, ex) =>
            {
                if (!_reportedFailures.Add(alert.Id)) return;
                _logger.LogWarning(ex, "Background alert '{Name}' failed to evaluate.", alert.Name);
                Announce($"Alert {alert.Name} failed to evaluate in the background: {ex.Message}");
            };
            return _evaluator;
        }

        /// <summary>One fetch, with the dead-feed bookkeeping. Null on failure.</summary>
        private async Task<List<Ohlcv>?> FetchAsync(IMarketDataProvider provider, Watch watch, int limit)
        {
            try
            {
                var (ohlcv, _) = await provider.FetchOhlcvAsync(new MarketDataRequest(
                    watch.Market, watch.Symbol, watch.Timeframe, Limit: limit));
                return ohlcv;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Background fetch failed for {Symbol} on {Provider}.",
                    watch.Symbol, watch.Provider);
                NoteFeedFailure(watch.Symbol, watch.Provider);
                return null;
            }
        }

        // ── Dead-feed detection ──────────────────────────────────────────────
        //
        // A fetch failure used to be a LogDebug and a `continue`. There was no
        // consecutive-failure counter, no FeedbackRequestEvent, nothing that ever said
        // "we can no longer watch BTC/USD". The provider's API key expires at 02:00 and the
        // user's stop-loss alert is watching nothing until they happen to notice.
        //
        // On THIS class that is worse than a design limit: it exists precisely because it can
        // speak through Orca, spd-say and notify-send, and it did not use any of them to
        // report its own failure. So the report goes out on the same channel the alerts do.

        /// <summary>
        /// The escalation, the once-only latch and the reset — shared with
        /// <see cref="HostedAlertMonitor"/>, which used to carry its own copy of all three.
        /// Keyed on symbol alone: this monitor serves one desktop user.
        /// </summary>
        private readonly DeadFeedTracker<string> _deadFeeds = new(StringComparer.OrdinalIgnoreCase);

        /// <remarks>Internal rather than private so the escalation can be driven directly —
        /// the loop that calls it needs a provider, a data service and a settings store to
        /// reach, and an inline bound nobody can call is a bound nobody tests.</remarks>
        internal void NoteFeedFailure(string symbol, string provider)
        {
            if (_deadFeeds.NoteFailure(symbol) is not int n) return;

            string text = $"Alert monitoring stopped for {symbol}: {provider} has failed "
                        + $"{n} times in a row. Alerts on this symbol are not being watched.";
            _logger.LogWarning("{Text}", text);
            Announce(text);
        }

        internal void NoteFeedRecovered(string symbol)
        {
            if (!_deadFeeds.NoteRecovery(symbol)) return;

            // Recovery is worth saying too: a user who heard the failure has no other way to
            // learn that their alerts are live again, and would keep watching manually.
            string text = $"Alert monitoring resumed for {symbol}.";
            _logger.LogInformation("{Text}", text);
            Announce(text);
        }

        /// <summary>
        /// Speaks and notifies, without the earcon or the recent-alerts entry — this is the
        /// monitor reporting on itself, not an alert firing, and filing it as an alert would
        /// put a fake row in the tray's list.
        /// </summary>
        private void Announce(string text)
        {
            if (_announcementsSnoozed) return;
            DesktopAnnouncement.Present(_presenter,
                "Alert monitoring", text, text, urgent: true, withSound: false, _logger);
        }

        // Warn once per distinct set, not once per poll: the monitor polls every
        // minute for as long as the app runs, and a warning that repeats forever
        // trains the reader to ignore the log.
        private string? _lastUnwatchableKey;

        private void WarnOnceAboutUnwatchable(IReadOnlyList<(AlertDefinition Alert, string Reason)> unwatchable)
        {
            var key = string.Join("|", unwatchable.Select(u => u.Alert.Id).OrderBy(id => id, StringComparer.Ordinal));
            if (key == _lastUnwatchableKey) return;
            _lastUnwatchableKey = key;
            if (unwatchable.Count == 0) return;

            _logger.LogWarning(
                "{Count} active alert(s) cannot be watched in the background: {Detail}. " +
                "They still work while their chart is open.",
                unwatchable.Count,
                string.Join("; ", unwatchable.Select(u => $"'{u.Alert.Name}' — {u.Reason}")));
        }

        // ── Watch derivation (pure; unit-tested) ─────────────────────────────

        /// <param name="Series">The saved tab's series configs, when the watch came from a tab —
        /// the narration ladder is built from the ones flagged with N. Null for an alert watch.</param>
        public sealed record Watch(string Provider, string Symbol, string Timeframe,
            IReadOnlyList<AlertDefinition> Alerts, string Market = "Spot",
            IReadOnlyList<SeriesConfig>? Series = null);

        /// <summary>One chart to fetch this poll, and the reasons it is wanted.</summary>
        internal sealed record Target(Watch Watch, bool WatchBarCloses, bool Narrate);

        // ── Bar closes with the browser closed (Phase 3 D1/D2) ───────────────
        //
        // A bar close is not an event this process can RECEIVE headless. The only publisher of
        // NewBarEvent is the workspace store's live-data path, and nothing headless dispatches
        // into a store — which is why HeadlessSession's NewBars subscriber sat there with a
        // mask, a comment and no producer until 2026-09-08. So the monitor OBSERVES it instead:
        // it already re-fetches every watched symbol once a minute, and "the newest bar is later
        // than the newest one I saw last time" is the whole test.
        //
        // Newest seen bar per (provider, symbol, timeframe, market). Instance state, so it
        // survives polls exactly as the persistent evaluator does.
        private readonly Dictionary<string, DateTime> _lastBarSeen = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Stable key for a watch — the same tuple <see cref="DeriveWatches"/> groups by.</summary>
        internal static string WatchKey(Watch w) =>
            $"{w.Provider}|{w.Market}|{w.Symbol}|{w.Timeframe}".ToLowerInvariant();

        /// <summary>
        /// The charts to watch for bar closes: <b>the tabs the user had open</b>, from the most
        /// recent autosaved session.
        ///
        /// <para>Deliberately NOT the alert list. An alert is a question about a price; a chart
        /// is what the user chose to watch, and "close the browser and still get new-bar
        /// notifications" is a statement about charts. A user with no alerts at all still has
        /// tabs open.</para>
        ///
        /// <para><b>Capped at <see cref="BackgroundTabFeedService.MaxLiveBackgroundFeeds"/>,
        /// reusing that budget rather than inventing a second one.</b> Eight is already the
        /// answer this codebase gives to "how many background charts do we keep current", and
        /// two different numbers for one idea is how they drift apart. Past the cap the tabs are
        /// simply not watched, and that is logged, because silence must never read as coverage.</para>
        /// </summary>
        internal static IReadOnlyList<Watch> DeriveBarCloseWatches(WorkspaceConfiguration? session)
        {
            if (session?.Tabs == null || session.Tabs.Count == 0) return Array.Empty<Watch>();

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<Watch>();
            foreach (var tab in session.Tabs)
            {
                if (string.IsNullOrWhiteSpace(tab.Symbol) || string.IsNullOrWhiteSpace(tab.Provider))
                    continue;

                var w = new Watch(
                    tab.Provider.Trim(),
                    tab.Symbol.Trim(),
                    string.IsNullOrWhiteSpace(tab.Timeframe) ? "1h" : tab.Timeframe.Trim(),
                    Array.Empty<AlertDefinition>(),
                    string.IsNullOrWhiteSpace(tab.Market) ? "Spot" : tab.Market.Trim(),
                    tab.Series);

                // Two tabs on the same chart are one watch. Duplicates would fetch twice and
                // announce twice — and the second announcement would be indistinguishable from
                // the bug this whole phase exists to fix.
                if (!seen.Add(WatchKey(w))) continue;
                result.Add(w);
                if (result.Count >= BackgroundTabFeedService.MaxLiveBackgroundFeeds) break;
            }
            return result;
        }

        /// <summary>
        /// Whether a timeframe clears the user's floor.
        ///
        /// <para>Default "1m" — every timeframe announces. <b>Cody, 2026-09-08.</b> The gate that
        /// stops a one-minute chart being a toast a minute is the opt-in category switch
        /// (<see cref="SettingsKeys.DesktopNotifyNewBars"/>, default off); this is the escape
        /// hatch for someone who wants bar closes but not every minute of them.</para>
        ///
        /// <para>An unparseable floor lets everything through rather than silencing everything:
        /// a typo in a setting must not be a mute switch the user cannot see.</para>
        /// </summary>
        internal static bool ClearsTimeframeFloor(string timeframe, string? floor)
        {
            int floorSeconds = TimeframeUtility.ToSeconds(floor ?? "");
            if (floorSeconds <= 0) return true;
            int barSeconds = TimeframeUtility.ToSeconds(timeframe ?? "");
            // An unknown timeframe is announced: it is a chart the user opened, and refusing to
            // speak about it because this process cannot parse its name is the wrong direction.
            if (barSeconds <= 0) return true;
            return barSeconds >= floorSeconds;
        }

        /// <summary>
        /// Why THIS monitor cannot watch an active alert, or null when it can — see
        /// <see cref="AccessibleTrader.Core.Services.Alerts.BackgroundWatchability"/>, which the
        /// alerts UI shares so the exclusion and the user-facing warning can never disagree.
        /// The chart's saved series come from the last autosaved tab for the alert's symbol.
        /// </summary>
        public static string? WhyUnwatchable(AlertDefinition a, WorkspaceConfiguration? session)
            => AccessibleTrader.Core.Services.Alerts.BackgroundWatchability.WhyUnwatchable(a, SavedSeriesFor(session, a));

        /// <summary>
        /// The active alerts a background monitor CANNOT evaluate, with the reason each is
        /// excluded — for the monitor's once-per-change warning and for the alerts UI to say at
        /// creation time. The predicate is the monitor's own: this monitor composes a chart per
        /// symbol and refuses almost nothing; the hosted monitor evaluates against a blank chart
        /// and passes <c>BackgroundWatchability.WhyUnwatchableWithoutAChart</c>.
        /// </summary>
        public static IReadOnlyList<(AlertDefinition Alert, string Reason)> DeriveUnwatchable(
            IEnumerable<AlertDefinition> alerts, Func<AlertDefinition, string?> whyUnwatchable) =>
            alerts.Where(a => a.IsActive)
                  .Select(a => (Alert: a, Reason: whyUnwatchable(a)))
                  .Where(t => t.Reason != null)
                  .Select(t => (t.Alert, t.Reason!))
                  .ToList();

        /// <inheritdoc cref="DeriveUnwatchable(IEnumerable{AlertDefinition}, Func{AlertDefinition, string?})"/>
        public static IReadOnlyList<(AlertDefinition Alert, string Reason)> DeriveUnwatchable(
            IEnumerable<AlertDefinition> alerts, WorkspaceConfiguration? session = null) =>
            DeriveUnwatchable(alerts, a => WhyUnwatchable(a, session));

        /// <summary>
        /// The watch list IS the user's alert list: every active alert the background evaluator
        /// can honestly evaluate (see <paramref name="whyUnwatchable"/>) with an explicit Symbol
        /// AND Provider. Grouped so each (provider, market, symbol, timeframe) costs one fetch
        /// per poll. Market rides along from the alert (defaulting to "Spot" for pre-existing
        /// alerts) — it used to be hardcoded to "Spot" at the fetch, so a Futures or Derivatives
        /// alert quietly watched the wrong market.
        /// </summary>
        public static IReadOnlyList<Watch> DeriveWatches(
            IEnumerable<AlertDefinition> alerts, Func<AlertDefinition, string?> whyUnwatchable,
            WorkspaceConfiguration? session = null) =>
            alerts
                .Where(a => a.IsActive && whyUnwatchable(a) == null)
                .GroupBy(a => (Provider: a.Provider!.Trim(),
                               Symbol: a.Symbol!.Trim(),
                               Timeframe: string.IsNullOrWhiteSpace(a.Timeframe) ? "1h" : a.Timeframe!.Trim(),
                               Market: string.IsNullOrWhiteSpace(a.Market) ? "Spot" : a.Market!.Trim()),
                    StringTupleComparer.Instance)
                // The saved tab's series ride along: the alerts' indicators are built from them.
                .Select(g => new Watch(g.Key.Provider, g.Key.Symbol, g.Key.Timeframe, g.ToList(), g.Key.Market,
                                       SavedSeriesFor(session, g.Key.Provider, g.Key.Symbol, g.Key.Timeframe, g.Key.Market)))
                .ToList();

        /// <summary>This monitor's watches: the chart-composing predicate, with the saved session.</summary>
        public static IReadOnlyList<Watch> DeriveWatches(IEnumerable<AlertDefinition> alerts, WorkspaceConfiguration? session = null)
            => DeriveWatches(alerts, a => WhyUnwatchable(a, session), session);

        /// <summary>
        /// The series saved on the tab an alert belongs to. The tab on the same chart when there
        /// is one; failing that, any saved tab on the same symbol at the same venue — an alert
        /// written on the 1-hour chart still means the RSI as the user set it up, and a profile
        /// on the 5-minute tab is the profile they have. Empty when nothing is saved for the
        /// symbol, which a point-of-control alert reports as unwatchable.
        /// </summary>
        internal static IReadOnlyList<SeriesConfig> SavedSeriesFor(WorkspaceConfiguration? session, AlertDefinition a)
            => SavedSeriesFor(session, a.Provider ?? "", a.Symbol ?? "",
                string.IsNullOrWhiteSpace(a.Timeframe) ? "1h" : a.Timeframe!, string.IsNullOrWhiteSpace(a.Market) ? "Spot" : a.Market!);

        internal static IReadOnlyList<SeriesConfig> SavedSeriesFor(
            WorkspaceConfiguration? session, string provider, string symbol, string timeframe, string market)
        {
            if (session?.Tabs == null) return Array.Empty<SeriesConfig>();
            bool Same(string? x, string? y) => string.Equals(x?.Trim(), y?.Trim(), StringComparison.OrdinalIgnoreCase);
            var sameSymbol = session.Tabs
                .Where(t => Same(t.Provider, provider) && Same(t.Symbol, symbol) && t.Series != null && t.Series.Count > 0)
                .ToList();
            var exact = sameSymbol.FirstOrDefault(t =>
                Same(string.IsNullOrWhiteSpace(t.Timeframe) ? "1h" : t.Timeframe, timeframe)
                && Same(string.IsNullOrWhiteSpace(t.Market) ? "Spot" : t.Market, market));
            return (IReadOnlyList<SeriesConfig>?)(exact ?? sameSymbol.FirstOrDefault())?.Series ?? Array.Empty<SeriesConfig>();
        }

        /// <summary>
        /// The routing rule, pure and therefore testable: the watches THIS session owns, given
        /// what the open browser circuits are already covering.
        ///
        /// <para>
        /// It replaces a whole-process pause (<c>ActiveCircuits &gt; 0</c> → return), which was
        /// right about the on-screen symbol and wrong about every other one. Empty coverage —
        /// the browser is closed — means every watch is ours, which is the behaviour that
        /// existed before and is the case that must not regress.
        /// </para>
        ///
        /// <para>
        /// THE HAZARD is doubling, not silence. A symbol that appears in <paramref name="covered"/>
        /// is being evaluated by a circuit's own pipeline right now; taking it here would speak
        /// the same alert twice through the same Orca. Comparison is case-insensitive because
        /// that is what the alert pipeline itself uses.
        /// </para>
        /// </summary>
        public static IReadOnlyList<Watch> OwnedWatches(
            IReadOnlyList<Watch> watches, IReadOnlySet<string> covered)
        {
            if (covered.Count == 0) return watches;
            return watches.Where(w => !covered.Contains(w.Symbol)).ToList();
        }

        private sealed class StringTupleComparer
            : IEqualityComparer<(string Provider, string Symbol, string Timeframe, string Market)>
        {
            public static readonly StringTupleComparer Instance = new();
            public bool Equals((string Provider, string Symbol, string Timeframe, string Market) a,
                (string Provider, string Symbol, string Timeframe, string Market) b) =>
                string.Equals(a.Provider, b.Provider, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Symbol, b.Symbol, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Timeframe, b.Timeframe, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Market, b.Market, StringComparison.OrdinalIgnoreCase);
            public int GetHashCode((string Provider, string Symbol, string Timeframe, string Market) v) =>
                HashCode.Combine(v.Provider.ToLowerInvariant(), v.Symbol.ToLowerInvariant(),
                    v.Timeframe.ToLowerInvariant(), v.Market.ToLowerInvariant());
        }

        // ── Bar closes with the browser closed (Phase 3 D1/D2) ───────────────

        /// <summary>
        /// Merges the alert watches and the bar-close watches into one fetch list.
        /// <b>One request per chart, however many reasons there are to want it.</b>
        /// </summary>
        internal static IReadOnlyList<(Watch Watch, bool WatchBarCloses)> MergeTargets(
            IEnumerable<Watch> alertWatches, IEnumerable<Watch> barWatches)
            => MergeTargets(alertWatches, barWatches, Array.Empty<Watch>())
                .Select(t => (t.Watch, t.WatchBarCloses)).ToList();

        /// <summary>
        /// Three reasons to want a chart — its alerts, its bar closes, its narration ladder —
        /// merged into one fetch per chart.
        /// </summary>
        internal static IReadOnlyList<Target> MergeTargets(
            IEnumerable<Watch> alertWatches, IEnumerable<Watch> barWatches, IEnumerable<Watch> narrationWatches)
        {
            var byKey = new Dictionary<string, Target>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in alertWatches) byKey[WatchKey(w)] = new Target(w, false, false);
            foreach (var w in barWatches)
            {
                // Keep the ALERT watch when both exist — it is the one carrying the alert list.
                // Taking the bar watch instead would silently drop every alert on that chart,
                // which is the kind of loss that shows up as "my alert stopped working" weeks
                // later with nothing in a log. The tab's SERIES ride along either way, because
                // the alert watch never carries any.
                byKey[WatchKey(w)] = byKey.TryGetValue(WatchKey(w), out var existing)
                    ? existing with { Watch = existing.Watch with { Series = w.Series ?? existing.Watch.Series }, WatchBarCloses = true }
                    : new Target(w, true, false);
            }
            foreach (var w in narrationWatches)
            {
                byKey[WatchKey(w)] = byKey.TryGetValue(WatchKey(w), out var existing)
                    ? existing with { Watch = existing.Watch with { Series = w.Series ?? existing.Watch.Series }, Narrate = true }
                    : new Target(w, false, true);
            }
            return byKey.Values.ToList();
        }

        /// <summary>Whether the user wants to be told about events they cannot see — one
        /// switch across the whole feature since 2026-09-11, default ON. See
        /// <see cref="NotificationPolicy.NotifyUnseen"/>.</summary>
        private static bool WatchBarCloses(ISettingsManager settings)
            => NotificationPolicy.NotifyUnseen(settings);

        /// <summary>The Narration tab's master switch — default ON, as it is in-session
        /// (<c>AppSettings.NarrateSignalsOnBarClose</c>). N picks WHICH series speak; this says
        /// whether any of them do.</summary>
        private static bool NarrateOnBarClose(ISettingsManager settings)
        {
            try { return settings.GetSetting(SettingsKeys.NarrateSignalsOnBarClose)?.ToObject<bool>() ?? true; }
            catch { return true; }
        }

        private static string? TimeframeFloor(ISettingsManager settings)
        {
            try { return settings.GetSetting(SettingsKeys.HeadlessNewBarMinTimeframe)?.ToString(); }
            catch { return null; }
        }

        /// <summary>
        /// The most recently autosaved session — the tabs the user had open when they last had
        /// a browser attached. Returns null when nothing has been saved, which is the ordinary
        /// state on a fresh install and not an error.
        /// </summary>
        private WorkspaceConfiguration? LoadLastSession(IServiceProvider services)
        {
            try
            {
                var library = services.GetRequiredService<IWorkspaceLibraryService>();
                var newest = library.GetAllProfilesWithTimes()
                    .Where(p => p.Name.StartsWith(SessionAutosaveService.LastSessionProfileName, StringComparison.Ordinal))
                    .OrderByDescending(p => p.LastWriteUtc)
                    .Select(p => p.Name)
                    .FirstOrDefault();
                return newest == null ? null : library.LoadProfile(newest);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read the last saved session; no bar closes will be watched.");
                return null;
            }
        }

        /// <summary>
        /// One announcement per chart per poll, and none on the first sighting. Returns whether
        /// a bar closed since the last poll; the caller decides whether that is announced.
        ///
        /// <para><b>The seed matters.</b> Without it, starting the terminal announces a bar close
        /// on every watched chart at once — bars that closed while it was not running, presented
        /// as news. And a poll that was missed (a laptop asleep, a provider down for ten minutes)
        /// must not produce ten announcements when it comes back: the newest bar is the only one
        /// that is still true, so exactly one is spoken however many were skipped.</para>
        ///
        /// <para>Tracked while a browser covers the chart too, so the seed is warm at hand-off;
        /// the browser is the one saying it until then.</para>
        /// </summary>
        private bool NoteBarClose(Watch watch, IReadOnlyList<Ohlcv> bars)
        {
            var newest = bars[^1].Date;
            string key = WatchKey(watch);

            lock (_lastBarSeen)
            {
                if (!_lastBarSeen.TryGetValue(key, out var previous))
                {
                    _lastBarSeen[key] = newest;   // seed only
                    return false;
                }
                if (newest <= previous) return false;   // nothing closed since last poll
                _lastBarSeen[key] = newest;
            }
            return true;
        }

        /// <summary>
        /// The bar-close sentence, with the narration ladder behind it when there is one — ONE
        /// utterance, as in-session, where the coordinator defers the new-bar sentence to the
        /// narrator so the two are spoken together or not at all.
        /// </summary>
        private void AnnounceBarClose(Watch watch, Ohlcv closed, Ohlcv opened, string? ladder)
        {
            if (_announcementsSnoozed) return;
            string title = DesktopNotificationService.NewBarTitle(watch.Symbol, watch.Timeframe);
            string sentence = BackgroundBarAnnouncer.BackgroundSentence(
                new ChartIdentity(watch.Market, watch.Provider, watch.Symbol, watch.Timeframe),
                closed, opened);
            if (ladder != null) sentence = sentence + " " + ladder;

            _logger.LogInformation("Background bar close: {Sentence}", sentence);

            // Sound, toast and speech are THIS monitor's, for the same reason the alert delivery
            // below is: routing them through the headless DesktopNotificationService would put an
            // already-opted-in delivery behind a second switch, and give two owners one event.
            // The headless session is built WITHOUT the NewBars category for exactly this reason
            // — see HeadlessSession. The toast body IS the sentence: the notification is the one
            // path for the words, and the screen reader reads it (DesktopAnnouncement).
            DesktopAnnouncement.Present(_presenter,
                title, sentence, sentence, urgent: false, withSound: true, _logger);
        }

        /// <summary>
        /// The ladder on its own — the New-bars category is off, or the chart is below its floor,
        /// but something under N had news at this close. Led by the symbol, because everything
        /// this monitor says is about a chart the user is not looking at; the bar-close sentence
        /// normally supplies that lead and here there is none.
        /// </summary>
        private void AnnounceNarration(Watch watch, string ladder)
        {
            if (_announcementsSnoozed) return;
            string title = $"{watch.Symbol} {watch.Timeframe}";
            string text = $"{title}: {ladder}";
            _logger.LogInformation("Background narration: {Text}", text);
            DesktopAnnouncement.Present(_presenter,
                title, text, text, urgent: false, withSound: false, _logger);
        }

        // ── The chart per watch (Phase 3 D4: the ladder, then the alerts) ───────

        /// <summary>
        /// One chart per watched (provider, market, symbol, timeframe), kept for as long as the
        /// saved tab's series and the alerts on it stay the same. Keyed on the watch; the
        /// signature says whether the saved configs or the alert list changed underneath it (N
        /// pressed on something, a parameter edited, an alert added) — in which case it is
        /// rebuilt and re-seeded. A save that changed NOTHING the chart reads keeps it and its
        /// warm memory, which is what makes the hand-off from an open browser announce the first
        /// close after it rather than the second.
        /// </summary>
        private readonly Dictionary<string, (string Signature, HeadlessChart Chart)> _charts
            = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _warnedMissingIndicators = new(StringComparer.Ordinal);
        private bool _warnedNoFactory;

        private HeadlessChart? GetChart(IServiceProvider services, Watch watch)
        {
            string key = WatchKey(watch);
            var saved = watch.Series ?? Array.Empty<SeriesConfig>();
            string signature = HeadlessChartFactory.Signature(saved, watch.Alerts);

            if (_charts.TryGetValue(key, out var held) && held.Signature == signature)
                return held.Chart;

            var factory = services.GetService<HeadlessChartFactory>();
            if (factory == null)
            {
                if (!_warnedNoFactory)
                {
                    _warnedNoFactory = true;
                    _logger.LogWarning("Headless charts are not registered in this host; the narration ladder will not be spoken with the browser closed, and indicator alerts will not be evaluated.");
                }
                return null;
            }

            try
            {
                var chart = factory.Create(
                    new ChartIdentity(watch.Market, watch.Provider, watch.Symbol, watch.Timeframe), saved, watch.Alerts);
                _charts[key] = (signature, chart);
                _logger.LogInformation(
                    "Headless chart watching {Symbol} {Timeframe}: {Narrated} narrated series, {Total} in all, {Bars} bars of history.",
                    watch.Symbol, watch.Timeframe, chart.NarratedSeries.Count, chart.Series.Count, chart.BarsNeeded);
                ReportMissingIndicators(watch, chart);
                return chart;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Headless chart could not be built for {Symbol} {Timeframe}.",
                    watch.Symbol, watch.Timeframe);
                return null;
            }
        }

        /// <summary>
        /// An alert on an indicator this process cannot build — a plugin not loaded here, a
        /// code nothing answers to — is an alert that is not being watched. Said once per
        /// distinct set per chart, on the same channel the alerts use; the alternative is the
        /// evaluator returning null on every poll while the user believes the market is watched.
        /// </summary>
        private void ReportMissingIndicators(Watch watch, HeadlessChart chart)
        {
            if (chart.MissingIndicators.Count == 0) return;
            string key = WatchKey(watch) + "|" + string.Join(",", chart.MissingIndicators.OrderBy(c => c, StringComparer.OrdinalIgnoreCase));
            if (!_warnedMissingIndicators.Add(key)) return;

            var names = watch.Alerts
                .Where(a => a.IndicatorCode != null && chart.MissingIndicators.Contains(a.IndicatorCode, StringComparer.OrdinalIgnoreCase))
                .Select(a => a.Name).Distinct().ToList();
            string which = names.Count > 0 ? $"Alert {string.Join(", ", names)} on {watch.Symbol}" : $"An alert on {watch.Symbol}";
            string text = $"{which} cannot be watched in the background: indicator {string.Join(", ", chart.MissingIndicators)} is not available here.";
            _logger.LogWarning("{Text}", text);
            Announce(text);
        }

        /// <summary>A chart that is no longer wanted for any reason — its tab closed, its last N
        /// flag and its last alert gone — is forgotten; its memory would otherwise sit in the
        /// dictionary for the life of the process.
        ///
        /// <para>BOTH memories, and that is the point: <c>_charts</c> used to be pruned here
        /// while <c>_lastBarSeen</c> kept its entry for the life of the process. The two then
        /// disagreed about the same chart — a re-opened tab met a rebuilt (cold) chart and a
        /// stale "the newest bar I saw" — and a re-opened tab is the ordinary case.</para></summary>
        private void ForgetChartsNotIn(IEnumerable<Target> targets)
        {
            var keep = targets.Select(t => WatchKey(t.Watch)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var key in _charts.Keys.Where(k => !keep.Contains(k)).ToList())
                _charts.Remove(key);
            lock (_lastBarSeen)
                foreach (var key in _lastBarSeen.Keys.Where(k => !keep.Contains(k)).ToList())
                    _lastBarSeen.Remove(key);
        }

        // ── Delivery: sound → toast → speech ─────────────────────────────────

        private void Deliver(AlertFired fired, string watchedSymbol)
        {
            // AlertEvaluator constructs AlertFired with Symbol left null — the in-session
            // pipeline stamps it afterwards from the on-screen chart (AlertOrchestrator's
            // "enriched"), and this monitor never did. So every background alert reached the
            // tray's recent list, and would now reach webhook per-asset routing, with no
            // symbol on it at all. The watch knows which market it fetched; use it.
            if (string.IsNullOrEmpty(fired.Symbol) && !string.IsNullOrWhiteSpace(watchedSymbol))
                fired = fired with { Symbol = watchedSymbol };

            string text = fired.SpeechText;
            _logger.LogInformation("Background alert fired: {Text}", text);

            // Record it so the tray's recent-alerts list and unread-count label can show it.
            // Directly, not through InSessionAlertRecorder: the headless session deliberately
            // does not resolve that recorder, because it and this line would file the same
            // alert in the buffer twice. See HeadlessSession.
            _recent.Add(text, fired.Symbol);

            // Sound, toast and speech are THIS monitor's, under THIS monitor's opt-in switch.
            // They are not routed through the headless DesktopNotificationService — that would
            // put an already-opted-in delivery behind notifications.desktop.alerts, which
            // defaults off, and silently un-ship the feature. The headless service is built
            // without the Alerts category for exactly this reason.
            // The desktop half only. The recent-alerts entry above and the channel fan-out
            // below are unaffected by either gate: a snooze is about noise at this desk, and
            // "do not notify me" is about this desk too — neither is an instruction to stop
            // sending your alerts to Telegram.
            if (!_announcementsSnoozed && _notifyUnseen)
                DesktopAnnouncement.Present(_presenter,
                    "Trading alert", text, text, urgent: false, withSound: true, _logger);

            // And then publish it on the long-lived session's bus, so the ordinary in-session
            // subscribers see a background alert for the first time: AlertDeliveryService's
            // email / Telegram / webhook fan-out, and the journal. Last, and inside a try:
            // a broken channel must never cost the user the announcement above.
            try { _session.Get<IEventBus>().Publish(new AccessibleTrader.Core.Models.AlertFiredEvent(fired)); }
            catch (Exception ex) { _logger.LogWarning(ex, "Background alert could not be published to the headless session bus."); }
        }
    }
}
