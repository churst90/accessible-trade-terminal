using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using AccessibleTrader.Core.Services;
using AccessibleTrader.WebHost.Account;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// Per-circuit setup that must run once for each browser connection.
    ///
    /// Currently it re-applies the Firefox <c>Ctrl+Shift+letter</c> → <c>Alt+Shift+letter</c>
    /// drawing-tool / detailed-point-summary remap (<see cref="WebHostShortcutRemap"/>).
    /// That used to run app-once in <c>Program.cs</c>, but once the multi-user scoping
    /// change made <see cref="IShortcutManager"/> per-circuit (Scoped), the remap has to
    /// run per circuit too — otherwise each new visitor's shortcut profile keeps the raw
    /// <c>Ctrl+Shift</c> chords that Firefox swallows. A <see cref="CircuitHandler"/> is
    /// the Blazor Server hook for per-circuit work, and (unlike the shared RCL's
    /// <c>MainLayout</c>) it can reference WebHost-only types like
    /// <see cref="WebHostShortcutRemap"/>.
    ///
    /// Registered Scoped, so the <see cref="IShortcutManager"/> injected here is the same
    /// per-circuit instance the components use.
    /// </summary>
    public sealed class WebHostBrowserCircuitHandler : CircuitHandler
    {
        // Ops observability: how many live browser sessions this process is
        // carrying. Logged on every open/close so the hosted operator can see
        // session churn in journalctl. Cleanup itself is Blazor's job — a closed
        // tab's circuit is retained ~3 minutes for reconnects, then disposed,
        // which disposes every per-circuit scoped service (feeds, providers,
        // audio) via standard DI scope disposal.
        private static int _activeCircuits;

        // Per-user circuit counts (keyed by ICurrentUser.DataKey): the hosted
        // alert monitor suppresses server-side evaluation for users whose OWN
        // session is connected — their in-session pipeline owns delivery then,
        // and double-sending every email/Telegram/push is the hosted analogue
        // of the local monitor's double-speech bug.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _circuitsByUser
            = new(StringComparer.Ordinal);
        private string? _userKey;

        /// <summary>Live circuits currently held by this user (0 when offline).</summary>
        internal static int ActiveCircuitsForUser(string userKey) =>
            _circuitsByUser.TryGetValue(userKey, out var n) ? n : 0;

        // ── Which symbols a user actually has on screen ──────────────────────
        //
        // The hosted monitor used to skip a user ENTIRELY while any of their circuits was
        // connected, handing ownership to the in-session pipeline. But the in-session
        // pipeline only evaluates alerts whose Symbol matches the chart on screen, and
        // BackgroundWorkspaceMonitor covers other open TABS only, is opt-in, and is gated
        // to desktop. Net effect on the hosted terminal: an alert on a symbol with no tab
        // open was evaluated by NOBODY while the browser was connected — so **closing your
        // browser made more of your alerts work than leaving it open.**
        //
        // Suppression is now per SYMBOL rather than per user: the in-session pipeline keeps
        // the alerts it can genuinely see, and the server takes the rest.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<
            string, System.Collections.Concurrent.ConcurrentDictionary<string, string>> _symbolsByUser
            = new(StringComparer.Ordinal);

        /// <summary>
        /// Symbols this user's live circuits currently have on screen. Compared
        /// case-INSENSITIVELY, as the alert pipeline does; nothing is upper-cased here. Empty
        /// when they are offline — which correctly means "suppress nothing".
        /// </summary>
        internal static IReadOnlySet<string> OnScreenSymbolsForUser(string userKey)
        {
            if (!_symbolsByUser.TryGetValue(userKey, out var perCircuit))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return perCircuit.Values
                .Where(v => !string.IsNullOrEmpty(v))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Test seam: forget every recorded on-screen symbol.</summary>
        internal static void ResetOnScreenSymbolsForTests() => _symbolsByUser.Clear();

        /// <summary>Test seam: record a symbol as on screen for a user's circuit.</summary>
        internal static void RecordOnScreenSymbol(string userKey, string circuitId, string? symbol)
        {
            var perCircuit = _symbolsByUser.GetOrAdd(userKey,
                _ => new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.Ordinal));
            perCircuit[circuitId] = symbol ?? "";
        }

        private static void ForgetCircuitSymbol(string userKey, string circuitId)
        {
            if (!_symbolsByUser.TryGetValue(userKey, out var perCircuit)) return;
            perCircuit.TryRemove(circuitId, out _);
            if (perCircuit.IsEmpty) _symbolsByUser.TryRemove(userKey, out _);
        }

        /// <summary>Live subscription to this circuit's workspace state, tracking the symbol
        /// on screen. Disposed with the circuit.</summary>
        private IDisposable? _symbolWatch;
        private string? _circuitId;

        /// <summary>This circuit's registration in <see cref="CircuitAlertCoverage"/> — the
        /// LOCAL analogue of the per-user suppression above. Disposed with the circuit, and it
        /// must be: a stale registration keeps a closed circuit's symbols "covered" and the
        /// background monitor would stop watching them with nothing on screen.</summary>
        private IDisposable? _coverage;

        /// <summary>This circuit's registration in <see cref="CircuitOrderCoverage"/> — which
        /// venues' fills it is announcing. Disposed with the circuit for the same reason: a
        /// stale registration keeps a closed browser's venues "covered" and the headless
        /// announcer would stay quiet about fills nobody is announcing.</summary>
        private IDisposable? _orderCoverage;

        /// <summary>
        /// <b>RETAINED circuits, for ops observability only — never a gate.</b>
        ///
        /// <para>It goes up in <c>OnCircuitOpenedAsync</c> and down in
        /// <c>OnCircuitClosedAsync</c>, and Blazor holds a closed tab's circuit for about three
        /// minutes in case the client reconnects. So this reads 1 for three minutes after the
        /// last tab is closed and 2 for three minutes after a reload. Anything that asks "can I
        /// reach the user right now" must read <see cref="BrowserPresence"/> instead, which
        /// counts CONNECTIONS; gating on this counter would reinstate the three-minute silence
        /// the 2026-09-11 hand-off fix removed.</para>
        /// </summary>
        internal static int ActiveCircuits => _activeCircuits;

        private readonly ILogger<WebHostBrowserCircuitHandler> _logger;
        private readonly IServiceProvider _scope;

        // NOTHING user-scoped may be a constructor parameter here, and IShortcutManager used to
        // be one. An object exists before its methods run, so a constructor dependency is built
        // before OnCircuitOpenedAsync can call ICurrentUser.Set — the shortcut manager resolved
        // its file path at that moment and every hosted user shared users/anon/shortcuts.json.
        // Resolve per-circuit services from _scope INSIDE OnCircuitOpenedAsync, after Set.
        // PerUserPathPolicyTests.TheCircuitHandlerTakesNothingUserScopedInItsConstructor pins it.
        public WebHostBrowserCircuitHandler(
            ILogger<WebHostBrowserCircuitHandler> logger,
            IServiceProvider scope)
        {
            _logger = logger;
            _scope = scope;   // the circuit's scoped provider — resolve optional account services from it
        }

        public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            int now = System.Threading.Interlocked.Increment(ref _activeCircuits);
            _logger.LogInformation("Browser circuit opened ({Active} active).", now);

            // Hosted accounts: capture "who is this visitor" into the per-circuit ICurrentUser
            // so UserScopedPathService routes their data directory. Resolved optionally — when
            // accounts are off these services aren't registered and this is a no-op.
            try
            {
                if (_scope.GetService<ICurrentUser>() is CurrentUser current)
                {
                    var authProvider = _scope.GetService<AuthenticationStateProvider>();
                    if (authProvider != null)
                    {
                        var state = await authProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
                        current.Set(state.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                        if (current.IsAuthenticated)
                        {
                            _userKey = current.DataKey;
                            _circuitsByUser.AddOrUpdate(_userKey, 1, (_, n) => n + 1);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Per-circuit current-user capture failed.");
            }

            try
            {
                // Resolved here, not injected: this is the first moment the shortcut manager can
                // safely learn which user's shortcuts.json it owns. See the constructor note.
                var shortcuts = _scope.GetService<IShortcutManager>();
                if (shortcuts != null)
                    WebHostShortcutRemap.ApplyBrowserHostOverrides(shortcuts, _logger);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Per-circuit browser shortcut remap failed.");
            }

            // Force-create the in-session alert recorder so it subscribes to THIS circuit's
            // event bus for its lifetime (local Full mode only — null otherwise). The scope
            // holds it, so its subscription lives until the circuit closes.
            try { _scope.GetService<InSessionAlertRecorder>(); }
            catch (Exception ex) { _logger.LogDebug(ex, "In-session alert recorder init skipped."); }

            // Bind this tab to the user's shared paper account. Force-created for the same reason
            // as the recorder above: the account has to be watching THIS chart from the moment the
            // circuit opens, not from whenever something first asks for the broker — otherwise a
            // resting order stops being evaluated as soon as another tab takes focus.
            try { _scope.GetService<PaperAccountAttachment>(); }
            catch (Exception ex) { _logger.LogDebug(ex, "Paper account attach skipped."); }

            // Track which symbol THIS circuit is showing, so the hosted alert monitor can
            // suppress per symbol instead of per user. See OnScreenSymbolsForUser.
            try
            {
                if (_userKey != null && _scope.GetService<IWorkspaceStore>() is { } store)
                {
                    _circuitId = circuit.Id;
                    var key = _userKey;
                    var id = _circuitId;
                    RecordOnScreenSymbol(key, id, store.State.SymbolDisplayName);
                    _symbolWatch = store.StateStream.Subscribe(
                        st => RecordOnScreenSymbol(key, id, st.SymbolDisplayName));
                }
            }
            catch (Exception ex)
            {
                // Failing to track means the server evaluates an alert the in-session pipeline
                // may also see — a possible duplicate, which is far better than the silence
                // this replaces.
                _logger.LogDebug(ex, "On-screen symbol tracking could not start for this circuit.");
            }

            RegisterCoverage(circuit.Id);
        }

        /// <summary>The id the coverage registrations are filed under; kept so a reconnect can re-file them.</summary>
        private string? _coverageCircuitId;

        /// <summary>
        /// Claims this circuit's symbols and venues for in-browser announcement, so the headless
        /// monitors leave them alone. Called when the circuit opens and again when a dropped
        /// connection comes back; see <see cref="ReleaseCoverage"/> for the other half.
        /// </summary>
        internal void RegisterCoverage(string circuitId)
        {
            _coverageCircuitId = circuitId;
            // The LOCAL desktop's version of the same rule (HostMode.Full has one user, so the
            // per-user keying above never engages there — nothing sets _userKey with accounts
            // off). Registered as a CALLBACK rather than a snapshot because coverage includes
            // the background workspace monitors, which start and stop on tab switches without a
            // workspace-state change of their own. See CircuitAlertCoverage.
            try
            {
                var store = _scope.GetService<IWorkspaceStore>();
                if (store != null && _coverage == null)
                {
                    var monitoring = _scope.GetService<
                        AccessibleTrader.Core.Services.Workspace.IBackgroundMonitoringService>();
                    _coverage = CircuitAlertCoverage.Register(
                        circuitId, () => CoveredSymbols(store, monitoring));
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Alert-coverage registration could not start for this circuit.");
            }

            // The same rule for ORDER FILLS, keyed by venue instead of by symbol (Phase 2).
            // The headless session subscribes the same singleton provider streams this circuit
            // does, so both would announce the same fill; this says which venues THIS circuit
            // has actually hooked, and the headless announcer takes the rest. A callback, not a
            // snapshot: a stream that dies removes itself from the order service's set with no
            // event of its own, and a snapshot would go on claiming coverage that had gone.
            try
            {
                var orders = _scope.GetService<AccessibleTrader.Core.Services.IOrderExecutionService>();
                if (orders != null && _orderCoverage == null)
                    _orderCoverage = CircuitOrderCoverage.Register(circuitId, () =>
                        // …plus PAPER, always. The paper broker's stream is subscribed for the
                        // order service's whole lifetime rather than hooked on demand, so it never
                        // appears in LiveOrderStreamProviders — and the paper ACCOUNT is shared
                        // through PaperAccountHub, so this circuit's service and the headless one
                        // subscribe to the same subject. Leaving it out is a real double: the
                        // browser speaks the paper fill and spd-say says it again.
                        orders.LiveOrderStreamProviders.Append(
                            AccessibleTrader.Core.Services.GeneralOrderService.PaperProviderName));
            }
            catch (Exception ex)
            {
                // Registering nothing means the headless side may also announce a fill this
                // circuit announces — a duplicate. Failing the other way is silence.
                _logger.LogDebug(ex, "Order-coverage registration could not start for this circuit.");
            }
        }

        /// <summary>Hands this circuit's symbols and venues back to the headless monitors.</summary>
        internal void ReleaseCoverage()
        {
            _coverage?.Dispose();
            _coverage = null;

            _orderCoverage?.Dispose();
            _orderCoverage = null;
        }

        internal bool HoldsCoverage => _coverage != null || _orderCoverage != null;

        /// <summary>
        /// Tell this circuit's own deliverers whether they can still reach anybody.
        ///
        /// <para>Releasing coverage hands the user's events to the headless session; this is the
        /// other half of the same hand-off, and without it the circuit goes on delivering them
        /// for the three minutes Blazor retains a disconnected circuit — a second toast, a
        /// second entry in the recent list, a second email and a second webhook POST. Swallows
        /// everything: a disposing scope must not throw out of a circuit lifecycle method.</para>
        /// </summary>
        /// <summary>The session snapshot, from whichever hand-off point reached it first.</summary>
        private void SaveSessionNow(string why)
        {
            try
            {
                (_scope.GetService(typeof(AccessibleTrader.Core.Services.Workspace.ISessionAutosaveService))
                    as AccessibleTrader.Core.Services.Workspace.ISessionAutosaveService)?.SaveNow();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Session autosave on {Why} failed (scope may already be disposed).", why);
            }
        }

        private void SetPresence(bool connected)
        {
            try { (_scope.GetService(typeof(CircuitPresence)) as CircuitPresence)?.SetConnected(connected); }
            catch (Exception ex) { _logger.LogDebug(ex, "Could not update circuit presence."); }
        }

        // ── THE HAND-OFF HAPPENS WHEN THE CONNECTION DROPS, NOT WHEN THE CIRCUIT DIES ────────
        //
        // Closing the browser tab does not close the circuit. Blazor keeps a disconnected
        // circuit for DisconnectedCircuitRetentionPeriod (three minutes by default) in case the
        // client comes back, and OnCircuitClosedAsync — where coverage used to be released —
        // runs only when that expires. For those three minutes this circuit went on claiming
        // BTCUSDT while its speech went into a live region nobody would ever render, and the
        // headless monitor, correctly, said nothing about a chart a browser "covered". Cody,
        // 2026-09-11: three tabs, a 1-minute chart, browser closed, no notification.
        //
        // A circuit with no connection cannot reach the user by any route, so the headless side
        // owns everything the instant the connection is down, and takes it back the instant
        // the connection is up. A reconnect after a network blip may therefore hear one bar
        // close twice — once from spd-say, once in the browser — which is the failure mode
        // this project prefers to silence, every time.

        public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            ReleaseCoverage();
            SetPresence(false);

            // The final autosave used to run ONLY in OnCircuitClosedAsync, about three minutes
            // after the tab closed — so for those three minutes the headless monitor derived its
            // watch list from the PREVIOUS autosave, and then the tab list changed underneath it
            // at the worst possible moment: a changed list changes the chart signature, which
            // rebuilds the chart, which swallows one bar close in the hand-off window. Saving
            // here means the monitor reads the tabs the user actually just left. Idempotent —
            // the close-time save still runs.
            SaveSessionNow("connection down");
            var id = _coverageCircuitId ?? circuit?.Id;
            if (id != null) BrowserPresence.Disconnected(id);
            return base.OnConnectionDownAsync(circuit, cancellationToken);
        }

        public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            // Also runs for the FIRST connection, right after OnCircuitOpenedAsync has already
            // registered — RegisterCoverage is idempotent for that reason, and so is
            // BrowserPresence.Connected (keyed on the circuit id, not counted).
            var id = _coverageCircuitId ?? circuit?.Id;
            if (id != null)
            {
                RegisterCoverage(id);
                BrowserPresence.Connected(id);
            }
            SetPresence(true);
            return base.OnConnectionUpAsync(circuit, cancellationToken);
        }

        /// <summary>
        /// What this circuit is actually evaluating alerts for: the focused chart (the only
        /// symbol <c>AlertOrchestrator</c> considers — see its Part A symbol gating) plus every
        /// non-focused tab that has a running <c>BackgroundWorkspaceMonitor</c>. The latter is
        /// opt-in and off by default, which is precisely why it must be asked rather than
        /// assumed: if it is off, those symbols are covered by nobody in-session and the
        /// headless session should take them.
        /// </summary>
        internal static IEnumerable<string> CoveredSymbols(
            IWorkspaceStore store,
            AccessibleTrader.Core.Services.Workspace.IBackgroundMonitoringService? monitoring)
        {
            // BOTH spellings of "the chart on screen", and that is deliberate. The display name
            // is what the alert list and the in-session pipeline compare against; the identity's
            // raw symbol is what the headless monitor's watch list is built from (a saved tab's
            // TabConfiguration.Symbol). They are usually the same string and are allowed not to
            // be — an analytics series displays as "Fear and Greed Index" — and a coverage claim
            // that depends on which one the state happens to hold is a claim that fails towards
            // "nobody is watching this", which costs the user a duplicate announcement.
            //
            // It also carried the bug Cody reported on 2026-09-11: a RESUMED session never set
            // SymbolDisplayName at all, so the focused chart claimed nothing and the background
            // monitor announced its every bar close. That is fixed at the source in
            // MarketOrchestrator.LoadRestoredActiveTabAsync; this stays belt-and-braces, because
            // the cost of being wrong here is asymmetric.
            var state = store.State;
            if (!string.IsNullOrWhiteSpace(state.SymbolDisplayName)) yield return state.SymbolDisplayName;
            if (!string.IsNullOrWhiteSpace(state.Identity.Symbol)
                && !string.Equals(state.Identity.Symbol, state.SymbolDisplayName, StringComparison.OrdinalIgnoreCase))
                yield return state.Identity.Symbol;

            if (monitoring?.IsEnabled != true) yield break;
            foreach (var m in monitoring.Monitors)
                if (!string.IsNullOrWhiteSpace(m.SymbolDisplayName)) yield return m.SymbolDisplayName;
        }
        public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            int now = System.Threading.Interlocked.Decrement(ref _activeCircuits);
            _logger.LogInformation("Browser circuit closed ({Active} active).", now);

            _symbolWatch?.Dispose();
            _symbolWatch = null;

            ReleaseCoverage();

            // Belt and braces: a circuit can be torn down without a connection-down (process
            // shutdown, an abort), and a presence entry left behind would read as "a browser is
            // connected" forever — which under the policy means total silence from the headless
            // side. Idempotent, so the ordinary path (down, then closed three minutes later)
            // costs nothing here.
            if (_coverageCircuitId != null) BrowserPresence.Disconnected(_coverageCircuitId);

            if (_userKey != null)
            {
                var key = _userKey;
                if (_circuitId != null) ForgetCircuitSymbol(key, _circuitId);
                _circuitId = null;
                _userKey = null;
                while (_circuitsByUser.TryGetValue(key, out var n))
                {
                    if (n <= 1)
                    {
                        if (_circuitsByUser.TryRemove(new KeyValuePair<string, int>(key, n))) break;
                    }
                    else if (_circuitsByUser.TryUpdate(key, n - 1, n)) break;
                }
            }

            // Final session snapshot before the circuit's scoped services die —
            // a browser refresh was previously a destructive act that lost every
            // unsaved tab, drawing, and indicator stack.
            SaveSessionNow("circuit close");
            return Task.CompletedTask;
        }

    }
}
