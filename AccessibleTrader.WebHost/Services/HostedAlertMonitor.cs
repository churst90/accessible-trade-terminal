using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Alerts;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.WebHost.Account;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// Server-side alert evaluation for the HOSTED terminal (Tier 2 item 2,
    /// docs/ROADMAP_2.0.md): every registered user's saved simple alerts keep
    /// evaluating on the server after their browser closes, delivered through
    /// THEIR configured channels (email / Telegram / webhooks — and Web Push
    /// once subscribed). The hosted sibling of <see cref="LocalBackgroundMonitor"/>,
    /// with three differences: it iterates every user directory instead of one
    /// local profile; it seeds each evaluation scope's <see cref="ICurrentUser"/>
    /// so the WHOLE per-user stack (paths, settings, alert files, channel
    /// configs) resolves exactly as it would inside that user's circuit; and it
    /// delivers through alert channels, not local speech — the server has no
    /// speakers that reach anyone.
    ///
    /// Per-user suppression: while a user has a live circuit, their in-session
    /// pipeline owns evaluation AND delivery — evaluating here too would send
    /// every email twice. Bars are fetched ONCE per (provider, symbol,
    /// timeframe) per poll and shared across users; hosted market data is
    /// server-seeded (users cannot add keys), so one data service serves all.
    /// Users can opt out via the "alerts.serverSide" setting (default ON — a
    /// saved alert IS the opt-in; this just honors it when the tab is closed).
    /// </summary>
    public sealed class HostedAlertMonitor : BackgroundService
    {
        public const string SettingKey = "alerts.serverSide";
        public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan ChannelTimeout = TimeSpan.FromSeconds(30);

        private readonly IServiceScopeFactory _scopes;
        private readonly Core.Services.DemoPolicy _demo;
        private readonly string _usersRoot;
        private readonly Push.HostedWebPushSender? _push;
        private readonly ILogger<HostedAlertMonitor> _logger;

        // One persistent evaluator per user: crossing-edge state (was-below,
        // now-above) must survive across polls or every level alert would
        // re-fire on each cycle.
        private readonly Dictionary<string, AlertEvaluator> _evaluators = new(StringComparer.Ordinal);

        public HostedAlertMonitor(
            IServiceScopeFactory scopes,
            Core.Services.DemoPolicy demo,
            string usersRoot,
            ILogger<HostedAlertMonitor> logger,
            Push.HostedWebPushSender? push = null)
        {
            _scopes = scopes;
            _demo = demo;
            _usersRoot = usersRoot;
            _logger = logger;
            _push = push;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            if (!_demo.IsHosted) return;

            _logger.LogInformation("Hosted alert monitor active (users root: {Root}, poll {Seconds}s).",
                _usersRoot, PollInterval.TotalSeconds);

            while (!ct.IsCancellationRequested)
            {
                try { await PollOnceAsync(ct); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { _logger.LogWarning(ex, "Hosted alert poll failed; retrying next cycle."); }

                try { await Task.Delay(PollInterval, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        internal async Task PollOnceAsync(CancellationToken ct)
        {
            var userKeys = EnumerateUserKeys(_usersRoot);
            if (userKeys.Count == 0) return;

            // One shared data scope per poll: hosted market data keys are
            // server-seeded and identical for every user.
            using var dataScope = _scopes.CreateScope();
            var data = dataScope.ServiceProvider.GetRequiredService<IDataService>();
            await data.InitializeAsync(dataScope.ServiceProvider.GetRequiredService<IPluginLoaderService>());
            await data.ConfigureStoredKeyProvidersAsync();
            var barsCache = new Dictionary<(string Provider, string Symbol, string Timeframe, string Market), List<Ohlcv>?>();

            foreach (var userKey in userKeys)
            {
                ct.ThrowIfCancellationRequested();
                if (WebHostBrowserCircuitHandler.ActiveCircuitsForUser(userKey) > 0) continue;

                try
                {
                    await EvaluateUserAsync(userKey, data, barsCache, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Hosted alert evaluation failed for user {User}.", userKey);
                }
            }

            // Users deleted between polls must not pin evaluator state forever.
            foreach (var stale in _evaluators.Keys.Where(k => !userKeys.Contains(k)).ToList())
            {
                _evaluators.Remove(stale);
                _lastUnwatchableByUser.Remove(stale);
            }
        }

        // Once per user per distinct set — a server-side alert the server cannot
        // evaluate must at least be named in the log, not silently skipped forever.
        private readonly Dictionary<string, string> _lastUnwatchableByUser = new(StringComparer.Ordinal);

        private void WarnOnceAboutUnwatchable(
            string userKey, IReadOnlyList<(AlertDefinition Alert, string Reason)> unwatchable)
        {
            var key = string.Join("|", unwatchable.Select(u => u.Alert.Id).OrderBy(id => id, StringComparer.Ordinal));
            if (_lastUnwatchableByUser.TryGetValue(userKey, out var prev) && prev == key) return;
            _lastUnwatchableByUser[userKey] = key;
            if (unwatchable.Count == 0) return;

            _logger.LogWarning(
                "{Count} active alert(s) for {User} cannot be evaluated server-side: {Detail}. " +
                "They still work while the user's chart is open.",
                unwatchable.Count, userKey,
                string.Join("; ", unwatchable.Select(u => $"'{u.Alert.Name}' — {u.Reason}")));
        }

        private async Task EvaluateUserAsync(
            string userKey,
            IDataService data,
            Dictionary<(string, string, string, string), List<Ohlcv>?> barsCache,
            CancellationToken ct)
        {
            using var scope = _scopes.CreateScope();
            if (scope.ServiceProvider.GetService<ICurrentUser>() is not CurrentUser current) return;
            // Seeding the scoped ICurrentUser makes UserScopedPathService — and
            // therefore settings, alerts, and channel configs — resolve to this
            // user's directory, exactly as inside their own circuit.
            current.Set(userKey);

            var settings = scope.ServiceProvider.GetRequiredService<ISettingsManager>();
            if (!(settings.GetSetting(SettingKey)?.ToObject<bool>() ?? true)) return;

            var alerts = scope.ServiceProvider.GetRequiredService<IWorkspaceLibraryService>().LoadAlerts();
            WarnOnceAboutUnwatchable(userKey, LocalBackgroundMonitor.DeriveUnwatchable(alerts));
            var watches = LocalBackgroundMonitor.DeriveWatches(alerts);
            if (watches.Count == 0) return;

            if (!_evaluators.TryGetValue(userKey, out var evaluator))
            {
                evaluator = new AlertEvaluator(
                    new AccessibleTrader.Core.Services.Accessibility.SdkCandlePatternAnalyzer(),
                    new AccessibleTrader.Core.Services.Accessibility.IndicatorContextAnalyzer());
                _evaluators[userKey] = evaluator;
            }

            foreach (var watch in watches)
            {
                ct.ThrowIfCancellationRequested();

                // Market is part of the key: two users watching one symbol on
                // different sub-types (Spot vs Futures) must not share a fetch.
                var key = (watch.Provider, watch.Symbol, watch.Timeframe, watch.Market);
                if (!barsCache.TryGetValue(key, out var bars))
                {
                    bars = await FetchBarsAsync(data, watch, ct);
                    barsCache[key] = bars; // nulls cached too — one failed fetch per poll, not per user
                }
                if (bars == null || bars.Count < 2) continue;

                var state = WorkspaceState.Initial with { SymbolDisplayName = watch.Symbol };
                var fired = evaluator.EvaluateAlerts(
                    watch.Alerts, state, bars[^1], bars[^2],
                    new Dictionary<string, double>()).ToList();

                foreach (var f in fired)
                {
                    _logger.LogInformation("Hosted alert fired for {User}: {Text}", userKey, f.SpeechText);
                    var channels = scope.ServiceProvider.GetServices<IAlertChannel>();
                    await DeliverToChannelsAsync(channels, f, _logger, ct);

                    if (_push != null)
                    {
                        try { await _push.SendToUserAsync(userKey, "Accessible Trader alert", f.SpeechText, ct); }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) { _logger.LogWarning(ex, "Web Push fan-out failed for {User}.", userKey); }
                    }
                }
            }
        }

        private async Task<List<Ohlcv>?> FetchBarsAsync(
            IDataService data, LocalBackgroundMonitor.Watch watch, CancellationToken ct)
        {
            try
            {
                var provider = await data.GetProviderAsync(watch.Provider);
                if (provider == null) return null;
                var (ohlcv, _) = await provider.FetchOhlcvAsync(new MarketDataRequest(
                    watch.Market, watch.Symbol, watch.Timeframe, Limit: 3));
                return ohlcv;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Hosted alert fetch failed for {Symbol} on {Provider}.",
                    watch.Symbol, watch.Provider);
                return null;
            }
        }

        /// <summary>Fan-out mirroring AlertDeliveryService's semantics: every
        /// configured channel, bounded per-channel timeout, one failure never
        /// starves the rest. Internal static for direct testing.</summary>
        internal static async Task DeliverToChannelsAsync(
            IEnumerable<IAlertChannel> channels, AlertFired fired, ILogger logger, CancellationToken ct)
        {
            foreach (var channel in channels)
            {
                bool configured;
                try { configured = channel.IsConfigured; }
                catch { continue; }
                if (!configured) continue;

                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(ChannelTimeout);
                    await channel.SendAsync(fired, timeout.Token);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Hosted alert delivery via {Channel} failed.", channel.Id);
                }
            }
        }

        /// <summary>User directories that contain saved alerts. Pure; internal
        /// for direct testing. The "anon" slot is transient demo state, never a
        /// registered user.</summary>
        internal static IReadOnlyList<string> EnumerateUserKeys(string usersRoot)
        {
            try
            {
                if (!Directory.Exists(usersRoot)) return Array.Empty<string>();
                return Directory.GetDirectories(usersRoot)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrEmpty(name) && name != "anon")
                    .Cast<string>()
                    .Where(name => File.Exists(Path.Combine(usersRoot, name, "Workspaces", "alerts.json")))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }
}
