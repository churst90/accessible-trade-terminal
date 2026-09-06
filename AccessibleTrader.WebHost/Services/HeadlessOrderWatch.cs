using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// <b>Phase 2 of the background monitor: order fills with the browser closed.</b>
    ///
    /// <para>
    /// Alerts were Phase 1 — this is the other half of "something happened to my money while
    /// I was not looking". It keeps the live order stream of every venue the user has an
    /// active key and open work on hooked to the long-lived
    /// <see cref="HeadlessSession"/>, so a stop triggering at 03:00 on an order resting since
    /// yesterday is spoken, toasted and sounded through this desktop rather than discovered
    /// the next morning. <see cref="HeadlessOrderAnnouncer"/> is the delivery half.
    /// </para>
    ///
    /// <para>
    /// ── SAFETY LINE, and it is not decoration ─────────────────────────────────
    /// <b>The headless session REPORTS; it never ACTS.</b> This class subscribes to streams
    /// and reads open orders and positions. It never places an order, never moves a stop,
    /// never runs a strategy. Anything that acts on the market stays in-session, where a
    /// person is present to hear it happen.
    /// </para>
    ///
    /// <para>
    /// ── The decisions the scope document said to settle before writing this ───
    /// </para>
    /// <list type="bullet">
    ///   <item><b>Credentials with no user session.</b> Registered for <c>HostMode.Full</c>
    ///   only, and it stands down on demo and hosted (<see cref="DemoPolicy"/>). Local Full is
    ///   one desktop, one user and one key store, so "the stored key" is unambiguous. On the
    ///   hosted head keys are per user and there is no user to attribute a headless
    ///   subscription to — that needs its own design and does not get one by accident here.</item>
    ///
    ///   <item><b>Eligibility, and why it is not simply "has a key".</b> A venue is watched
    ///   when it has an active, non-withdrawal stored key AND the account currently has an
    ///   open order or an open position. Holding an authenticated socket open all night for
    ///   an account with nothing at stake spends the user's rate limit to learn nothing.
    ///   Withdrawal profiles are excluded here for the same reason
    ///   <c>ConfigureStoredKeyProvidersAsync</c> excludes them.</item>
    ///
    ///   <item><b>Unattended reconnect, and the rate limit.</b> A socket that dies at 03:00
    ///   used to leave a dead entry in the order service's map that its own idempotency check
    ///   then refused to replace for the life of the process — the user believing they were
    ///   watched and not being. A terminated stream now removes itself, and this loop
    ///   re-subscribes on its next tick. The retry cadence is deliberately the POLL interval
    ///   (60 s) and not a tight reconnect loop: hammering a venue that is refusing is how a
    ///   key gets rate-limited, and a rate-limited key is a longer outage than the one being
    ///   retried.</item>
    ///
    ///   <item><b>Silent non-coverage is worse than no feature.</b> Three consecutive polls
    ///   that cannot establish or keep a venue's stream escalate to the user through the same
    ///   channels an alert uses, via the same <see cref="DeadFeedTracker{TKey}"/> the OHLCV
    ///   watch has used since 2026-08-29 — said once, and said again when it recovers,
    ///   because a user who heard the failure has no other way to learn it is over.</item>
    /// </list>
    ///
    /// <para>
    /// ── The doubling hazard ───────────────────────────────────────────────────
    /// Provider plugins are singletons, so this subscription and a browser circuit's are to
    /// the same stream. Which of the two SPEAKS is decided per venue at delivery time by
    /// <see cref="CircuitOrderCoverage"/> — see <see cref="HeadlessOrderAnnouncer"/>. The
    /// subscription itself is never torn down when a browser opens: a stream dropped and
    /// re-established on every tab open would lose exactly the events it exists to catch.
    /// </para>
    ///
    /// <para>
    /// Opt-in: the same switch as the alert monitor — Settings → General → "Keep monitoring
    /// when the browser is closed" (<see cref="LocalBackgroundMonitor.SettingKey"/>, default
    /// off). Read per poll, so turning it off stops the account queries without a restart.
    /// </para>
    /// </summary>
    public sealed class HeadlessOrderWatch : BackgroundService
    {
        private readonly HeadlessSession _session;
        private readonly DemoPolicy _demo;
        private readonly IDesktopAlertPresenter _presenter;
        private readonly ILogger<HeadlessOrderWatch> _logger;

        /// <summary>Consecutive failures per venue, and the once-only escalation latch —
        /// the same rule the OHLCV watch uses, keyed on the provider because that is what a
        /// broker feed going down is scoped to.</summary>
        private readonly DeadFeedTracker<string> _deadStreams = new(StringComparer.OrdinalIgnoreCase);

        public HeadlessOrderWatch(
            HeadlessSession session,
            DemoPolicy demo,
            IDesktopAlertPresenter presenter,
            ILogger<HeadlessOrderWatch> logger)
        {
            _session = session;
            _demo = demo;
            _presenter = presenter;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            if (_demo.IsDemo || _demo.IsHosted) return;   // local desktops only — see the credentials note

            _logger.LogInformation(
                "Headless order watch available ({Delivery}). Waiting for the opt-in setting.",
                _presenter.Describe());

            while (!ct.IsCancellationRequested)
            {
                try { await PollOnceAsync(ct); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { _logger.LogWarning(ex, "Headless order watch poll failed; retrying next cycle."); }

                try { await Task.Delay(LocalBackgroundMonitor.PollInterval, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        /// <remarks>Internal rather than private for the same reason the alert monitor's poll
        /// is: the eligibility rule, the re-subscribe and the escalation only mean anything
        /// together, and a loop nobody can drive is a loop nobody tests.</remarks>
        internal async Task PollOnceAsync(CancellationToken ct)
        {
            var services = _session.Services;
            var settings = services.GetRequiredService<ISettingsManager>();
            if (!(settings.GetSetting(LocalBackgroundMonitor.SettingKey)?.ToObject<bool>() ?? false)) return;

            var orders = services.GetService<IOrderExecutionService>();
            var keys = services.GetService<IApiKeyService>();
            if (orders == null || keys == null) return;

            // Serialised with the alert monitor's identical preamble: both loops tick on the
            // same 60-second interval against the same scoped IDataService, whose InitializeAsync
            // is not thread-safe. See HeadlessSession.EnsureDataReadyAsync.
            await _session.EnsureDataReadyAsync();
            var data = services.GetRequiredService<IDataService>();

            foreach (var name in await KeyedProvidersAsync(keys))
            {
                ct.ThrowIfCancellationRequested();

                // Already hooked. Nothing to ask the venue, nothing to spend.
                if (orders.LiveOrderStreamProviders.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    NoteStreamHealthy(name);
                    continue;
                }

                bool? hasWork = await HasOpenWorkAsync(data, name);
                if (hasWork == null)
                {
                    // The venue could not be asked. That is exactly the dead feed the tracker
                    // is for: an expired key or an unreachable API means fills are not being
                    // watched, whatever the user believes.
                    NoteStreamFailure(name, "its account could not be read");
                    continue;
                }

                // A key with nothing resting behind it is not a failure — it is an account with
                // nothing to watch, and it is healthy: it answered. Counting it as a failure
                // would announce a dead feed to a user who simply has no open orders.
                //
                // Note where this call is NOT: one line further down, before the subscribe.
                // Clearing the counter there — on the strength of the ACCOUNT query — would reset
                // it every poll for a venue whose STREAM will not establish, and the warning that
                // case exists to produce could never arrive. Two different feeds; two different
                // health facts.
                if (hasWork == false)
                {
                    NoteStreamHealthy(name);
                    continue;
                }

                try { await orders.SubscribeOrderUpdatesAsync(name); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Headless order watch could not hook {Provider}.", name);
                }

                // ASSERT THE ARTIFACT, NOT THE INCANTATION: what matters is whether the stream
                // is actually hooked afterwards, not whether the call returned. A provider that
                // is not a trading provider, or whose stream was already dead, leaves the set
                // unchanged and must escalate rather than be assumed covered.
                if (orders.LiveOrderStreamProviders.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Headless order watch is now watching fills on {Provider}.", name);
                    NoteStreamHealthy(name);
                }
                else
                {
                    NoteStreamFailure(name, "its order stream could not be established");
                }
            }
        }

        // ── Eligibility ──────────────────────────────────────────────────────

        /// <summary>
        /// The venues with an active, non-withdrawal stored key. Withdrawal profiles are
        /// excluded because that credential exists to move funds OFF the venue and must never
        /// become a session credential — the same separation the trading path keeps.
        /// </summary>
        private async Task<IReadOnlyList<string>> KeyedProvidersAsync(IApiKeyService keys)
        {
            List<ApiKeyConfig> stored;
            try { stored = await keys.GetAllKeysAsync(); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Headless order watch could not read the stored keys.");
                return Array.Empty<string>();
            }

            return stored
                .Where(k => k.IsActive && !k.AllowsWithdrawal && !string.IsNullOrEmpty(k.ApiKey))
                .Select(k => k.Provider)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Whether this venue currently has an open order or an open position — true, false,
        /// or <c>null</c> when the venue could not be asked at all.
        ///
        /// <para>
        /// The three-state answer is the point. "No open orders" and "I could not reach the
        /// exchange" look identical from a caller that only has a bool, and treating the second
        /// as the first is how a user ends up believing an expired key is an empty account.
        /// </para>
        /// </summary>
        internal static async Task<bool?> HasOpenWorkAsync(IDataService data, string providerName)
        {
            IMarketDataProvider? provider;
            try { provider = await data.GetProviderAsync(providerName); }
            catch { return null; }
            if (provider is not ITradingProvider tp) return false;   // a data-only feed has no orders to watch

            bool asked = false;

            try
            {
                if ((await tp.GetOpenOrdersAsync()).Count > 0) return true;
                asked = true;
            }
            catch { /* try positions before giving up — a spot venue may refuse a null-symbol order query */ }

            try
            {
                if ((await tp.GetPositionsAsync()).Count > 0) return true;
                asked = true;
            }
            catch { /* fall through */ }

            return asked ? false : null;
        }

        // ── Escalation ───────────────────────────────────────────────────────

        private void NoteStreamFailure(string provider, string why)
        {
            if (_deadStreams.NoteFailure(provider) is not int n) return;

            string text = $"Order monitoring stopped for {provider}: {why} on "
                        + $"{n} checks in a row. Fills, stops and take-profits on {provider} are not being watched.";
            _logger.LogWarning("{Text}", text);
            Announce(text);
        }

        private void NoteStreamHealthy(string provider)
        {
            if (!_deadStreams.NoteRecovery(provider)) return;

            string text = $"Order monitoring resumed for {provider}.";
            _logger.LogInformation("{Text}", text);
            Announce(text);
        }

        /// <summary>
        /// Speaks and toasts without the notification sound: this is the watch reporting on
        /// ITSELF, not a fill, and the sound is the cue that means money moved.
        /// </summary>
        private void Announce(string text)
        {
            try
            {
                _presenter.Notify("Order monitoring", text, urgent: true);
                _presenter.Speak(text);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Headless order watch could not report its own state.");
            }
        }
    }
}
