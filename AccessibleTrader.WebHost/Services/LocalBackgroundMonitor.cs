using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Alerts;
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
    /// Scope, deliberately: SIMPLE alerts (price/pattern rules) that carry an
    /// explicit Symbol + Provider — the watch list is DERIVED from your saved
    /// alerts, no separate configuration. Condition-tree and current-chart
    /// alerts need the full indicator pipeline and stay session-only (the
    /// Settings text says so).
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
    /// It also no longer builds a throwaway DI scope per poll. It runs inside
    /// <see cref="HeadlessSession"/> — one scope for the life of the process —
    /// so subscriptions inside it outlive a tick, providers stay configured
    /// between polls, and a fired alert can be PUBLISHED on a real event bus
    /// where the ordinary in-session subscribers (email, Telegram, webhooks, the
    /// journal) pick it up unchanged.
    ///
    /// Opt-in: Settings → General → "Keep monitoring when the browser is closed"
    /// (monitoring.backgroundLocal, default off). Read per poll, so toggling
    /// takes effect without a restart.
    /// </summary>
    public sealed class LocalBackgroundMonitor : BackgroundService
    {
        public const string SettingKey = "monitoring.backgroundLocal";
        public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

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
        // on every subsequent poll.
        private readonly AlertEvaluator _evaluator = new(
            new SdkCandlePatternAnalyzer(), new IndicatorContextAnalyzer());

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
            // The user silenced alerts from the tray — skip delivery until it expires.
            if (_snooze.IsActive) return;

            var services = _session.Services;
            var settings = services.GetRequiredService<ISettingsManager>();
            if (!(settings.GetSetting(SettingKey)?.ToObject<bool>() ?? false)) return;

            var alerts = services.GetRequiredService<IWorkspaceLibraryService>().LoadAlerts();
            WarnOnceAboutUnwatchable(DeriveUnwatchable(alerts));

            // The routing rule that replaced "stand down while a circuit is open". A symbol an
            // open browser session already watches belongs to that session; everything else is
            // ours. See CircuitAlertCoverage for why this is not a pause.
            var watches = OwnedWatches(DeriveWatches(alerts), CircuitAlertCoverage.CoveredSymbols());
            if (watches.Count == 0) return;

            var data = services.GetRequiredService<IDataService>();
            await data.InitializeAsync(services.GetRequiredService<IPluginLoaderService>());
            await data.ConfigureStoredKeyProvidersAsync();

            foreach (var watch in watches)
            {
                ct.ThrowIfCancellationRequested();
                var provider = await data.GetProviderAsync(watch.Provider);
                if (provider == null) continue;

                List<Ohlcv> bars;
                try
                {
                    var (ohlcv, _) = await provider.FetchOhlcvAsync(new MarketDataRequest(
                        watch.Market, watch.Symbol, watch.Timeframe, Limit: 3));
                    bars = ohlcv;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Background fetch failed for {Symbol} on {Provider}.",
                        watch.Symbol, watch.Provider);
                    NoteFeedFailure(watch.Symbol, watch.Provider);
                    continue;
                }
                NoteFeedRecovered(watch.Symbol);
                if (bars.Count < 2) continue;

                var state = WorkspaceState.Initial with { SymbolDisplayName = watch.Symbol };
                var fired = _evaluator.EvaluateAlerts(
                    watch.Alerts, state, bars[^1], bars[^2],
                    new Dictionary<string, double>()).ToList();

                foreach (var f in fired) Deliver(f, watch.Symbol);
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
            _presenter.Notify("Alert monitoring", text, urgent: true);
            _presenter.Speak(text);
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

        public sealed record Watch(string Provider, string Symbol, string Timeframe,
            IReadOnlyList<AlertDefinition> Alerts, string Market = "Spot");

        /// <summary>
        /// Why background evaluation cannot watch an active alert, or null when it
        /// can — see <see cref="AccessibleTrader.Core.Services.Alerts.BackgroundWatchability"/>,
        /// which the alerts UI shares so the exclusion and the user-facing warning
        /// can never disagree.
        /// </summary>
        public static string? WhyUnwatchable(AlertDefinition a)
            => AccessibleTrader.Core.Services.Alerts.BackgroundWatchability.WhyUnwatchable(a);

        /// <summary>
        /// The active alerts the background monitors CANNOT evaluate, with the
        /// reason each is excluded — for the monitors' once-per-change warning and
        /// for the alerts UI to say at creation time.
        /// </summary>
        public static IReadOnlyList<(AlertDefinition Alert, string Reason)> DeriveUnwatchable(
            IEnumerable<AlertDefinition> alerts) =>
            alerts.Where(a => a.IsActive)
                  .Select(a => (Alert: a, Reason: WhyUnwatchable(a)))
                  .Where(t => t.Reason != null)
                  .Select(t => (t.Alert, t.Reason!))
                  .ToList();

        /// <summary>
        /// The watch list IS the user's alert list: every active alert the
        /// background evaluator can honestly evaluate (see
        /// <see cref="WhyUnwatchable"/>) with an explicit Symbol AND Provider.
        /// Grouped so each (provider, market, symbol, timeframe) costs one fetch
        /// per poll. Market rides along from the alert (defaulting to "Spot" for
        /// pre-existing alerts) — it used to be hardcoded to "Spot" at the fetch,
        /// so a Futures or Derivatives alert quietly watched the wrong market.
        /// </summary>
        public static IReadOnlyList<Watch> DeriveWatches(IEnumerable<AlertDefinition> alerts) =>
            alerts
                .Where(a => a.IsActive && WhyUnwatchable(a) == null)
                .GroupBy(a => (Provider: a.Provider!.Trim(),
                               Symbol: a.Symbol!.Trim(),
                               Timeframe: string.IsNullOrWhiteSpace(a.Timeframe) ? "1h" : a.Timeframe!.Trim(),
                               Market: string.IsNullOrWhiteSpace(a.Market) ? "Spot" : a.Market!.Trim()),
                    StringTupleComparer.Instance)
                .Select(g => new Watch(g.Key.Provider, g.Key.Symbol, g.Key.Timeframe, g.ToList(), g.Key.Market))
                .ToList();

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
            _presenter.PlayNotificationSound();
            _presenter.Notify("Trading alert", text, urgent: false);
            _presenter.Speak(text);

            // And then publish it on the long-lived session's bus, so the ordinary in-session
            // subscribers see a background alert for the first time: AlertDeliveryService's
            // email / Telegram / webhook fan-out, and the journal. Last, and inside a try:
            // a broken channel must never cost the user the announcement above.
            try { _session.Get<IEventBus>().Publish(new AccessibleTrader.Core.Models.AlertFiredEvent(fired)); }
            catch (Exception ex) { _logger.LogWarning(ex, "Background alert could not be published to the headless session bus."); }
        }
    }
}
