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
    /// Settings text says so). The monitor PAUSES while any browser session is
    /// connected — the in-session alert pipeline owns delivery then, and both
    /// speaking through the same Orca would double every announcement.
    ///
    /// Opt-in: Settings → General → "Keep monitoring when the browser is closed"
    /// (monitoring.backgroundLocal, default off). Read per poll, so toggling
    /// takes effect without a restart.
    /// </summary>
    public sealed class LocalBackgroundMonitor : BackgroundService
    {
        public const string SettingKey = "monitoring.backgroundLocal";
        public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

        private readonly IServiceScopeFactory _scopes;
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
            IServiceScopeFactory scopes,
            DemoPolicy demo,
            RecentAlertsBuffer recent,
            Tray.AlertSnooze snooze,
            IDesktopAlertPresenter presenter,
            ILogger<LocalBackgroundMonitor> logger)
        {
            _scopes = scopes;
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

        private async Task PollOnceAsync(CancellationToken ct)
        {
            // A connected browser session owns alert delivery — both this monitor
            // and the circuit speak through the same local Orca, and doubling
            // every announcement is exactly the bug the speech-output work killed.
            if (WebHostBrowserCircuitHandler.ActiveCircuits > 0) return;

            // The user silenced alerts from the tray — skip delivery until it expires.
            if (_snooze.IsActive) return;

            using var scope = _scopes.CreateScope();
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsManager>();
            if (!(settings.GetSetting(SettingKey)?.ToObject<bool>() ?? false)) return;

            var alerts = scope.ServiceProvider.GetRequiredService<IWorkspaceLibraryService>().LoadAlerts();
            WarnOnceAboutUnwatchable(DeriveUnwatchable(alerts));
            var watches = DeriveWatches(alerts);
            if (watches.Count == 0) return;

            var data = scope.ServiceProvider.GetRequiredService<IDataService>();
            await data.InitializeAsync(scope.ServiceProvider.GetRequiredService<IPluginLoaderService>());
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

                foreach (var f in fired) Deliver(f);
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

        private void Deliver(AlertFired fired)
        {
            string text = fired.SpeechText;
            _logger.LogInformation("Background alert fired: {Text}", text);

            // Record it so the tray's recent-alerts list and unread-count label can show it.
            _recent.Add(text, fired.Symbol);

            _presenter.PlayNotificationSound();
            _presenter.Notify("Trading alert", text, urgent: false);
            _presenter.Speak(text);
        }
    }
}
