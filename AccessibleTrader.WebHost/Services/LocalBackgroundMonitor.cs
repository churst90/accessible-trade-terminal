using System.Diagnostics;
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
    /// speech through Orca's D-Bus (or spd-say), desktop toasts through
    /// notify-send, and a notification sound through paplay/pw-play. So: close
    /// the browser and your alerts keep evaluating and keep being HEARD.
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
        private readonly string _soundPath;
        private readonly string? _notifySend;
        private readonly string? _gdbus;
        private readonly string? _spdSay;
        private readonly string? _player;

        // One evaluator for the monitor's lifetime: it owns the per-alert
        // hysteresis/edge state, so a level crossed at 03:00 doesn't re-fire
        // on every subsequent poll.
        private readonly AlertEvaluator _evaluator = new(
            new SdkCandlePatternAnalyzer(), new IndicatorContextAnalyzer());

        public LocalBackgroundMonitor(
            IServiceScopeFactory scopes,
            DemoPolicy demo,
            IPlatformPathService paths,
            RecentAlertsBuffer recent,
            Tray.AlertSnooze snooze,
            ILogger<LocalBackgroundMonitor> logger)
        {
            _scopes = scopes;
            _demo = demo;
            _recent = recent;
            _snooze = snooze;
            _logger = logger;

            _notifySend = WebHostSpeechManager.FindOnPath("notify-send", File.Exists);
            _gdbus = WebHostSpeechManager.FindOnPath("gdbus", File.Exists);
            _spdSay = WebHostSpeechManager.FindOnPath("spd-say", File.Exists);
            _player = WebHostSpeechManager.FindOnPath("paplay", File.Exists)
                   ?? WebHostSpeechManager.FindOnPath("pw-play", File.Exists);

            // Notification sound: app-data/sounds/alert.wav. Users (or the future
            // factory sound bank) drop their own file there; until then a small
            // generated two-tone beep means the feature is audible on day one.
            var soundsDir = Path.Combine(paths.AppDataDirectory, "sounds");
            Directory.CreateDirectory(soundsDir);
            _soundPath = Path.Combine(soundsDir, "alert.wav");
            try
            {
                if (!File.Exists(_soundPath))
                    File.WriteAllBytes(_soundPath, GenerateDefaultBeepWav());
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not write default alert sound."); }
        }

        // ── The poll loop ────────────────────────────────────────────────────

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            if (_demo.IsDemo || _demo.IsHosted) return; // local desktops only

            _logger.LogInformation(
                "Local background monitor available (speech: {Speech}, toast: {Toast}, sound: {Sound}). Waiting for the opt-in setting.",
                _gdbus != null ? "orca" : _spdSay != null ? "spd-say" : "none",
                _notifySend != null, _player != null);

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
                    continue;
                }
                if (bars.Count < 2) continue;

                var state = WorkspaceState.Initial with { SymbolDisplayName = watch.Symbol };
                var fired = _evaluator.EvaluateAlerts(
                    watch.Alerts, state, bars[^1], bars[^2],
                    new Dictionary<string, double>()).ToList();

                foreach (var f in fired) Deliver(f);
            }
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

            if (_player != null && File.Exists(_soundPath))
                Run(_player, _soundPath);

            if (_notifySend != null)
                Run(_notifySend, "--app-name=Accessible Trade Terminal",
                    "--urgency=normal", "Trading alert", text);

            // Orca first (the user's own voice/rate config), spd-say fallback —
            // the same ladder the in-session speech manager uses.
            if (_gdbus != null && TrySpeakViaOrca(text)) return;
            if (_spdSay != null) Run(_spdSay, text);
        }

        private bool TrySpeakViaOrca(string text)
        {
            try
            {
                Run(_gdbus!, "call", "--session",
                    "--dest=org.gnome.Orca1.Service",
                    "--object-path=/org/gnome/Orca1/Service",
                    "--method=org.gnome.Orca1.Service.PresentMessage",
                    text);
                return true;
            }
            catch { return false; }
        }

        private void Run(string file, params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = file,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var _ = Process.Start(psi);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Background delivery command {File} failed.", file);
            }
        }

        // ── Default sound (replace by dropping your own alert.wav) ───────────

        /// <summary>16-bit mono 22.05 kHz WAV: a two-tone rising blip (660→880 Hz,
        /// 300 ms total) with linear decay. Small, unambiguous, replaceable.</summary>
        public static byte[] GenerateDefaultBeepWav()
        {
            const int rate = 22050;
            const double dur = 0.3;
            int samples = (int)(rate * dur);
            var pcm = new short[samples];
            for (int i = 0; i < samples; i++)
            {
                double t = (double)i / rate;
                double freq = t < dur / 2 ? 660 : 880;
                double envelope = 1.0 - (double)i / samples;
                pcm[i] = (short)(Math.Sin(2 * Math.PI * freq * t) * envelope * short.MaxValue * 0.4);
            }

            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            int dataLen = samples * 2;
            w.Write("RIFF"u8); w.Write(36 + dataLen); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(dataLen);
            foreach (var s in pcm) w.Write(s);
            w.Flush();
            return ms.ToArray();
        }
    }
}
